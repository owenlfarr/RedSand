#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Networking;
using Unity.Services.Lobbies;
using Unity.Services.Lobbies.Models;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

public static class LobbyBrowserChecks
{
    sealed class Fake : ILobbyDirectoryBackend
    {
        public readonly Dictionary<string, Lobby> Lobbies = new Dictionary<string, Lobby>();
        public readonly List<string> Calls = new List<string>();
        public TaskCompletionSource<bool> CreateGate, UpdateGate;
        public bool FailUpdate, FailDelete;
        int serial;
        public async Task<Lobby> Create(string name, int max, Dictionary<string, DataObject> data)
        {
            Calls.Add("create");
            if (CreateGate != null) await CreateGate.Task;
            var lobby = new Lobby(id: (++serial).ToString(), name: name, maxPlayers: max, availableSlots: max - 1, data: data);
            Lobbies.Add(lobby.Id, lobby);
            return lobby;
        }
        public async Task<Lobby> Update(string id, UpdateLobbyOptions options)
        {
            Calls.Add("update");
            if (UpdateGate != null) await UpdateGate.Task;
            if (FailUpdate) throw new Exception("simulated update outage");
            var old = Lobbies[id];
            var next = new Lobby(id: id, name: options.Name ?? old.Name, maxPlayers: old.MaxPlayers, availableSlots: old.AvailableSlots,
                isPrivate: options.IsPrivate ?? old.IsPrivate, isLocked: options.IsLocked ?? old.IsLocked, data: options.Data ?? old.Data);
            Lobbies[id] = next;
            return next;
        }
        public Task Delete(string id)
        {
            Calls.Add("delete");
            if (FailDelete) { FailDelete = false; throw new Exception("simulated delete outage"); }
            Lobbies.Remove(id);
            return Task.CompletedTask;
        }
        public Task Heartbeat(string id) { Calls.Add("heartbeat"); return Task.CompletedTask; }
        public Task<Lobby> Get(string id) => Task.FromResult(Lobbies[id]);
        public Task<QueryResponse> Query(QueryLobbiesOptions options) => Task.FromResult(new QueryResponse(Lobbies.Values.Where(lobby => !lobby.IsPrivate && !lobby.IsLocked).ToList()));
    }

    static int checks;
    static void Check(bool passed, string message)
    {
        if (!passed) throw new Exception(message);
        checks++;
        Debug.Log("[Lobby Browser Test] PASS " + message);
    }

    [MenuItem("Tools/Lobby Browser/Run Lifecycle Tests")]
    public static async void RunTests()
    {
        checks = 0;
        try
        {
            const string version = "test-build", code = "CODE123";
            var backend = new Fake();
            var ad = new LobbyAdvertisement(backend);
            Check(await ad.Publish(false, "Private Host", code, version, 1, 3), "private host succeeds without directory");
            Check(backend.Calls.Count == 0 && !ad.IsListed, "private code never published");
            Check(await ad.Publish(true, "Owen", code, version, 1, 3), "public publish succeeds");
            string id = ad.Id;
            var row = await backend.Get(id);
            Check(row.Name == "Owen" && row.MaxPlayers == 3, "owner name and three-player capacity");
            Check(LobbyAdvertisement.IsJoinable(row, version), "waiting row joinable");
            Check(!LobbyAdvertisement.IsJoinable(row, "different-build"), "incompatible builds excluded");
            Check(await ad.Update("Owen", code, version, 2), "network count increases");
            Check(LobbyAdvertisement.Connected(await backend.Get(id)) == 2, "two actual connected players displayed");
            Check(await ad.Update("Renamed", code, version, 3), "rename and full update");
            row = await backend.Get(id);
            Check(row.Name == "Renamed" && LobbyAdvertisement.Connected(row) == 3, "owner rename and 3/3 displayed");
            Check(row.AvailableSlots == 2 && !LobbyAdvertisement.IsJoinable(row, version), "full state uses network roster, not directory membership");
            Check(await ad.Update("Renamed", code, version, 2), "disconnect updates count");
            Check(LobbyAdvertisement.IsJoinable(await backend.Get(id), version), "freed slot becomes joinable");
            await ad.Pulse();
            Check(backend.Calls.Contains("heartbeat"), "waiting public lobby kept alive");
            Check(await ad.HideForStart(), "hide before start succeeds");
            Check(!ad.IsListed && backend.Lobbies.Count == 0, "started listing removed");
            int calls = backend.Calls.Count;
            await ad.Pulse();
            Check(backend.Calls.Count == calls, "no heartbeat resurrects a started listing");
            Check(!await ad.Update("Late", code, version, 1), "late count update cannot recreate listing");

            await ad.Publish(true, "Retry", code, version, 1, 3);
            backend.FailUpdate = true;
            Check(!await ad.HideForStart(), "service failure blocks start hook cleanly");
            Check(ad.IsListed, "failed hide retains intact host listing for retry");
            backend.FailUpdate = false;
            Check(await ad.HideForStart(), "hide can be retried");

            backend.CreateGate = new TaskCompletionSource<bool>();
            var creating = ad.Publish(true, "Cancelled", code, version, 1, 3);
            var stopping = ad.Stop();
            backend.CreateGate.SetResult(true);
            Check(!await creating, "leave invalidates in-flight creation");
            await stopping;
            Check(backend.Lobbies.Count == 0 && !ad.IsListed, "late creation leaves no orphan listing");
            backend.CreateGate = null;

            await ad.Publish(true, "Update race", code, version, 1, 3);
            backend.UpdateGate = new TaskCompletionSource<bool>();
            var updating = ad.Update("Update race", code, version, 2);
            stopping = ad.Stop();
            backend.UpdateGate.SetResult(true);
            await updating;
            await stopping;
            Check(backend.Lobbies.Count == 0 && !ad.IsListed, "leave wins over in-flight count update");
            backend.UpdateGate = null;

            await ad.Publish(true, "Delete retry", code, version, 1, 3);
            backend.FailDelete = true;
            await ad.Stop();
            Check(backend.Lobbies.Count == 0, "failed cleanup is retried");

            await ad.Publish(true, "Private next", code, version, 1, 3);
            await ad.Publish(false, "Private next", code, version, 1, 3);
            Check(backend.Lobbies.Count == 0 && !ad.IsListed, "public-to-private rehost removes old listing");
            var bad = new Lobby(id: "bad", name: "Bad", maxPlayers: 3, data: LobbyAdvertisement.Data("", version, 1));
            Check(!LobbyAdvertisement.IsJoinable(bad, version), "missing relay code rejected");
            bad = new Lobby(id: "bad", name: "Bad", maxPlayers: 3, data: LobbyAdvertisement.Data(code, version, 4));
            Check(!LobbyAdvertisement.IsWaiting(bad, version), "malformed count excluded");
            bad = new Lobby(id: "bad", name: "Bad", maxPlayers: 3, data: LobbyAdvertisement.Data(code, version, 1, "started"));
            Check(!LobbyAdvertisement.IsWaiting(bad, version), "started state excluded even from cached rows");
            bad = new Lobby(id: "bad", name: "Bad", maxPlayers: 3, isPrivate: true, data: LobbyAdvertisement.Data(code, version, 1));
            Check(!LobbyAdvertisement.IsJoinable(bad, version), "private listing rejected even if cached");
            var options = LobbyAdvertisement.QueryOptions(version);
            Check(options.Count == 20 && options.Filters.Count == 4, "small query filters directory, build, waiting state and lock");
            File.WriteAllText("Logs/LobbyBrowserLifecycleResults.txt", "PASS " + checks + " asynchronous lifecycle and stale-row checks.");
            Debug.Log("[Lobby Browser Test] PASS " + checks + " lifecycle checks.");
        }
        catch (Exception exception)
        {
            File.WriteAllText("Logs/LobbyBrowserLifecycleResults.txt", "FAIL " + exception);
            Debug.LogException(exception);
        }
    }

    [MenuItem("Tools/Lobby Browser/Build Relay Integration Test")]
    public static void BuildIntegration()
    {
        try
        {
            Directory.CreateDirectory("Logs/LobbyBrowserTestBuild");
            var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
            {
                scenes = new[] { "Assets/Scenes/MainMenu.unity", "Assets/Scenes/Night1_MultiplayerTest.unity", "Assets/Scenes/Night 2.unity" },
                locationPathName = "Logs/LobbyBrowserTestBuild/RedSandBrowserTest.exe",
                target = BuildTarget.StandaloneWindows64,
                options = BuildOptions.Development
            });
            File.WriteAllText("Logs/LobbyBrowserBuildResult.txt", report.summary.result + " errors=" + report.summary.totalErrors + " time=" + report.summary.totalTime);
        }
        catch (Exception exception)
        {
            File.WriteAllText("Logs/LobbyBrowserBuildResult.txt", "FAIL " + exception);
            Debug.LogException(exception);
        }
    }
}
#endif
