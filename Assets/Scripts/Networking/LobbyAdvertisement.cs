using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using Unity.Services.Lobbies;
using Unity.Services.Lobbies.Models;

namespace Networking
{
    // Discovery only. Relay/NGO remains the connection and authoritative player roster.
    public interface ILobbyDirectoryBackend
    {
        Task<Lobby> Create(string name, int capacity, Dictionary<string, DataObject> data);
        Task<Lobby> Update(string id, UpdateLobbyOptions options);
        Task Delete(string id);
        Task Heartbeat(string id);
        Task<Lobby> Get(string id);
        Task<QueryResponse> Query(QueryLobbiesOptions options);
    }

    public sealed class UnityLobbyDirectoryBackend : ILobbyDirectoryBackend
    {
        public Task<Lobby> Create(string name, int capacity, Dictionary<string, DataObject> data) =>
            LobbyService.Instance.CreateLobbyAsync(name, capacity, new CreateLobbyOptions { IsPrivate = false, Data = data });
        public Task<Lobby> Update(string id, UpdateLobbyOptions options) => LobbyService.Instance.UpdateLobbyAsync(id, options);
        public Task Delete(string id) => LobbyService.Instance.DeleteLobbyAsync(id);
        public Task Heartbeat(string id) => LobbyService.Instance.SendHeartbeatPingAsync(id);
        public Task<Lobby> Get(string id) => LobbyService.Instance.GetLobbyAsync(id);
        public Task<QueryResponse> Query(QueryLobbiesOptions options) => LobbyService.Instance.QueryLobbiesAsync(options);
    }

    public sealed class LobbyAdvertisement
    {
        public const string DirectoryKey = "directory", DirectoryValue = "redsand-browser-v1";
        public const string CompatibilityKey = "build", StateKey = "state", CountKey = "connected", RelayKey = "relay";
        readonly ILobbyDirectoryBackend backend;
        readonly SemaphoreSlim gate = new SemaphoreSlim(1, 1);
        readonly HashSet<string> pendingDeletes = new HashSet<string>();
        int generation;
        bool wanted;
        public string Id { get; private set; }
        public string Error { get; private set; }
        public bool IsListed => !string.IsNullOrEmpty(Id) && wanted;
        public bool WantsPublic => wanted;

        public LobbyAdvertisement(ILobbyDirectoryBackend backend) { this.backend = backend; }

        public static Dictionary<string, DataObject> Data(string relay, string compatibility, int connected, string state = "waiting") =>
            new Dictionary<string, DataObject>
            {
                { DirectoryKey, new DataObject(DataObject.VisibilityOptions.Public, DirectoryValue, DataObject.IndexOptions.S1) },
                { CompatibilityKey, new DataObject(DataObject.VisibilityOptions.Public, compatibility, DataObject.IndexOptions.S2) },
                { StateKey, new DataObject(DataObject.VisibilityOptions.Public, state, DataObject.IndexOptions.S3) },
                { CountKey, new DataObject(DataObject.VisibilityOptions.Public, connected.ToString(CultureInfo.InvariantCulture), DataObject.IndexOptions.N1) },
                { RelayKey, new DataObject(DataObject.VisibilityOptions.Public, relay) }
            };

        public static string Value(Lobby lobby, string key) => lobby?.Data != null && lobby.Data.TryGetValue(key, out var data) ? data.Value : null;
        public static int Connected(Lobby lobby) => int.TryParse(Value(lobby, CountKey), out int count) ? count : -1;
        public static bool IsWaiting(Lobby lobby, string compatibility) => lobby != null && !lobby.IsPrivate && !lobby.IsLocked &&
            Value(lobby, DirectoryKey) == DirectoryValue && Value(lobby, CompatibilityKey) == compatibility &&
            Value(lobby, StateKey) == "waiting" && Connected(lobby) >= 1 && Connected(lobby) <= lobby.MaxPlayers &&
            !string.IsNullOrWhiteSpace(Value(lobby, RelayKey));
        public static bool IsJoinable(Lobby lobby, string compatibility) => IsWaiting(lobby, compatibility) && Connected(lobby) < lobby.MaxPlayers;
        public static QueryLobbiesOptions QueryOptions(string compatibility) => new QueryLobbiesOptions
        {
            Count = 20,
            Filters = new List<QueryFilter>
            {
                new QueryFilter(QueryFilter.FieldOptions.S1, DirectoryValue, QueryFilter.OpOptions.EQ),
                new QueryFilter(QueryFilter.FieldOptions.S2, compatibility, QueryFilter.OpOptions.EQ),
                new QueryFilter(QueryFilter.FieldOptions.S3, "waiting", QueryFilter.OpOptions.EQ),
                new QueryFilter(QueryFilter.FieldOptions.IsLocked, "false", QueryFilter.OpOptions.EQ)
            }
        };

        public async Task<bool> Publish(bool isPublic, string owner, string relay, string compatibility, int connected, int capacity)
        {
            int ticket = ++generation;
            wanted = isPublic;
            await gate.WaitAsync();
            try
            {
                if (ticket != generation) return false;
                await RemoveCurrent();
                if (!isPublic) { Error = null; return true; }
                var created = await backend.Create(owner, capacity, Data(relay, compatibility, connected));
                if (ticket != generation || !wanted)
                {
                    await DeleteOrQueue(created.Id);
                    return false;
                }
                Id = created.Id;
                Error = null;
                return true;
            }
            catch (Exception exception) { Error = exception.Message; return false; }
            finally { gate.Release(); }
        }

        public async Task<bool> Update(string owner, string relay, string compatibility, int connected)
        {
            int ticket = generation;
            await gate.WaitAsync();
            try
            {
                if (ticket != generation || !wanted || Id == null) return false;
                await backend.Update(Id, new UpdateLobbyOptions { Name = owner, IsPrivate = false, IsLocked = false, Data = Data(relay, compatibility, connected) });
                Error = null;
                return true;
            }
            catch (Exception exception) { Error = exception.Message; return false; }
            finally { gate.Release(); }
        }

        public async Task<bool> HideForStart()
        {
            wanted = false;
            int ticket = ++generation; // Invalidate in-flight work before waiting for its service call.
            await gate.WaitAsync();
            try
            {
                if (Id == null) return true;
                try { await backend.Update(Id, new UpdateLobbyOptions { IsPrivate = true, IsLocked = true }); }
                catch (LobbyServiceException exception) when (exception.Reason.ToString() == "LobbyNotFound") { Id = null; return true; }
                await RemoveCurrent();
                Error = null;
                return true;
            }
            catch (Exception exception)
            {
                Error = exception.Message;
                if (ticket == generation) wanted = true; // A later Stop must not be undone by this failed request.
                return false;
            }
            finally { gate.Release(); }
        }

        public async Task Stop()
        {
            wanted = false;
            ++generation;
            await gate.WaitAsync();
            try { await RemoveCurrent(); await RetryDeletes(); }
            finally { gate.Release(); }
        }

        public async Task Pulse()
        {
            int ticket = generation;
            await gate.WaitAsync();
            try
            {
                await RetryDeletes();
                if (ticket == generation && wanted && Id != null) await backend.Heartbeat(Id);
            }
            catch (Exception exception) { Error = exception.Message; }
            finally { gate.Release(); }
        }

        async Task RemoveCurrent()
        {
            string id = Id;
            Id = null;
            if (id != null) await DeleteOrQueue(id);
        }
        async Task DeleteOrQueue(string id)
        {
            try { await backend.Delete(id); pendingDeletes.Remove(id); }
            catch (LobbyServiceException exception) when (exception.Reason.ToString() == "LobbyNotFound") { pendingDeletes.Remove(id); }
            catch { pendingDeletes.Add(id); } // No more heartbeats; retry cleanup without touching the Relay connection.
        }
        async Task RetryDeletes()
        {
            foreach (string id in new List<string>(pendingDeletes)) await DeleteOrQueue(id);
        }
    }
}
