using System;
using System.Collections;
using System.IO;
using UnityEngine;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;

// This component exists only in the generated local test scene; no gameplay startup hook.
[DefaultExecutionOrder(-1000)]
public sealed class WatcherNetworkTest : NetworkBehaviour
{
    public static bool IsRunning => Array.IndexOf(Environment.GetCommandLineArgs(), "-watcherTest") >= 0;
    public NetworkVariable<int> Stage = new NetworkVariable<int>();
    public NetworkVariable<bool> ClientSawStatic = new NetworkVariable<bool>();
    public NetworkVariable<bool> ClientSawFade = new NetworkVariable<bool>();
    PowerStationWatcher watcher;
    double nextReport;
    bool sentWrongOwner, sentStale, sentAction, sawStatic;
    int placedStage = -1;
    float lastExposure;
    int assertions;
    string role;

    IEnumerator Start()
    {
        if (!IsRunning) yield break;
        Application.targetFrameRate = 60;
        QualitySettings.vSyncCount = 0;
        string[] args = Environment.GetCommandLineArgs();
        int index = Array.IndexOf(args, "-watcherTest");
        role = args[index + 1];
        var manager = Unity.Netcode.NetworkManager.Singleton;
        manager.NetworkConfig.ConnectionApproval = false;
        manager.GetComponent<UnityTransport>().SetConnectionData("127.0.0.1", 17841, "127.0.0.1");
        yield return null;
        if (role == "host") manager.StartHost(); else manager.StartClient();
        float expires = Time.realtimeSinceStartup + 210f;
        while (Time.realtimeSinceStartup < expires) yield return null;
        Debug.LogError("[WATCHER TEST] TIMEOUT " + role);
        Application.Quit(2);
    }

    public override void OnNetworkSpawn()
    {
        watcher = FindFirstObjectByType<PowerStationWatcher>();
        watcher.sightRange = 0f;
        watcher.roamSpeed = 0f;
        if (IsServer) StartCoroutine(Run());
    }

    void Update()
    {
        if (!IsRunning || !IsSpawned || watcher == null || NetworkManager.LocalClient == null || NetworkManager.LocalClient.PlayerObject == null) return;
        NetworkObject player = NetworkManager.LocalClient.PlayerObject;
        if (placedStage != Stage.Value)
        {
            placedStage = Stage.Value;
            sentAction = false;
            bool hostTarget = Stage.Value == 1 || Stage.Value == 8;
            Vector3 position = (IsHost == hostTarget) ? new Vector3(0f, 0f, 8f) : new Vector3(100f, 0f, 100f);
            player.transform.SetPositionAndRotation(position, Quaternion.identity);
            var networkTransform = player.GetComponent<Networking.OwnerNetworkTransform>();
            if (networkTransform != null) networkTransform.Teleport(position, Quaternion.identity, Vector3.one);
            player.GetComponent<NetworkPlayerMovementState>().CameraPitch.Value = 0f;
        }
        bool mine = watcher.State.Value == (int)PowerStationWatcher.Phase.Watching && watcher.Target.Value == NetworkManager.LocalClientId;
        if (!IsHost && Stage.Value == 1 && watcher.State.Value == (int)PowerStationWatcher.Phase.Watching && !sentWrongOwner)
        {
            sentWrongOwner = true;
            watcher.ReportActivityServerRpc(watcher.Encounter.Value, watcher.Clock, true);
        }
        if (mine && watcher.Clock >= nextReport)
        {
            nextReport = watcher.Clock + 0.08;
            bool activity = Stage.Value == 1 && watcher.Clock < watcher.GraceEnds - 0.8;
            if (!sentStale)
            {
                sentStale = true;
                watcher.ReportActivityServerRpc(watcher.Encounter.Value - 1, watcher.Clock, true);
            }
            if (Stage.Value >= 3 && watcher.Clock > watcher.GraceEnds + 0.35 && !sentAction)
            {
                sentAction = true;
                if (Stage.Value == 3) player.transform.position += Vector3.right * 0.5f;
                if (Stage.Value == 4) player.GetComponent<NetworkPlayerMovementState>().CameraPitch.Value = 10f;
                if (Stage.Value == 5 || Stage.Value == 7) activity = true; // tool / sprint input
                if (Stage.Value == 6) player.transform.position += Vector3.up * 0.5f;
                if (Stage.Value == 8) player.transform.rotation = Quaternion.Euler(0f, 12f, 0f);
            }
            watcher.ReportActivityServerRpc(watcher.Encounter.Value, watcher.Clock, activity);
        }
        if (!IsHost)
        {
            var effect = FindFirstObjectByType<WatcherStaticEffect>();
            float exposure = effect != null ? effect.Exposure : 0f;
            if (Stage.Value == 1 && exposure > 0.001f) Fail("Static leaked onto unspotted client");
            if (Stage.Value == 2 && mine && exposure > 0.25f && !sawStatic)
            {
                sawStatic = true;
                ObserveServerRpc(true, false);
            }
            if (Stage.Value == 2 && !mine && sawStatic && lastExposure > 0f && exposure == 0f)
                ObserveServerRpc(true, true);
            lastExposure = exposure;
        }
        if (Stage.Value == 99 && !IsServer) { Debug.Log("[WATCHER TEST] CLIENT PASS"); Application.Quit(0); }
    }

    [ServerRpc(RequireOwnership = false)]
    void ObserveServerRpc(bool saw, bool faded, ServerRpcParams rpc = default)
    {
        if (rpc.Receive.SenderClientId == NetworkManager.ServerClientId) return;
        ClientSawStatic.Value |= saw;
        ClientSawFade.Value |= faded;
    }

    IEnumerator Run()
    {
        yield return WaitFor(() => NetworkManager.ConnectedClientsIds.Count == 2, 30f, "two real peers connected");
        Check(watcher.reactionSeconds == 1.5f && watcher.stillSeconds == new Vector2(5f, 10f) && watcher.respawnSeconds == 45f, "production timing defaults");
        watcher.roamSpeed = 1.6f;
        Vector3 patrolStart = watcher.transform.position;
        yield return new WaitForSeconds(2f);
        Debug.Log($"[WATCHER TEST] Initial roaming from {patrolStart} to {watcher.transform.position}; destination {watcher.PatrolDestination}; state {watcher.State.Value}; motor {watcher.GetComponent<CharacterController>().enabled}");
        Check(Vector3.Distance(patrolStart, watcher.transform.position) > 0.3f, "initial random roaming moves");
        watcher.roamSpeed = 0f;
        for (int stage = 1; stage <= 8; stage++)
        {
            watcher.sightRange = 0f;
            Stage.Value = stage;
            foreach (var client in NetworkManager.ConnectedClientsList)
                client.PlayerObject.GetComponent<NetworkOxygenState>().IsDead.Value = false;
            yield return new WaitForSeconds(0.8f);
            // Reset the physical monster between cases, leaving all encounter logic untouched.
            watcher.GetComponent<CharacterController>().enabled = false;
            watcher.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            watcher.GetComponent<CharacterController>().enabled = true;
            watcher.sightRange = 28f;
            yield return WaitFor(() => watcher.State.Value == (int)PowerStationWatcher.Phase.Watching, 4f, "target acquired stage " + stage);
            ulong target = watcher.Target.Value;
            Check((target == 0) == (stage == 1 || stage == 8), "correct target stage " + stage);
            Check(watcher.RequiredStillSeconds.Value >= 5f && watcher.RequiredStillSeconds.Value <= 10f, "random 5-10 second hold stage " + stage);
            yield return WaitFor(() => watcher.State.Value == (int)PowerStationWatcher.Phase.Dormant, 14f, "encounter resolves stage " + stage);
            bool dead = NetworkManager.ConnectedClients[target].PlayerObject.GetComponent<NetworkOxygenState>().IsDead.Value;
            Check(dead == (stage >= 3), "survival/death result stage " + stage);
            foreach (var client in NetworkManager.ConnectedClientsList)
                if (client.ClientId != target) Check(!client.PlayerObject.GetComponent<NetworkOxygenState>().IsDead.Value, "bystander alive stage " + stage);
            if (stage == 1)
            {
                Check(watcher.RejectedReports >= 2, "wrong owner and stale encounter reports rejected");
                yield return new WaitForSeconds(2.2f);
                Check(FindFirstObjectByType<WatcherStaticEffect>().Exposure == 0f, "host static fades in 2 seconds");
                yield return new WaitForSeconds(41.5f);
                Check(watcher.State.Value == (int)PowerStationWatcher.Phase.Dormant, "still absent before 45 second cooldown ends");
            }
            if (stage == 2)
            {
                yield return new WaitForSeconds(2.3f);
                Check(ClientSawStatic.Value && ClientSawFade.Value, "targeted client static rises and fades over network");
            }
            watcher.sightRange = 0f;
            yield return WaitFor(() => watcher.State.Value == (int)PowerStationWatcher.Phase.Roaming, 47f, "respawn stage " + stage);
            watcher.respawnSeconds = 3f;
        }
        watcher.sightRange = 0f;
        Stage.Value = 1;
        foreach (var client in NetworkManager.ConnectedClientsList)
            client.PlayerObject.GetComponent<NetworkOxygenState>().IsDead.Value = false;
        yield return new WaitForSeconds(0.8f);
        watcher.GetComponent<CharacterController>().enabled = false;
        watcher.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
        watcher.GetComponent<CharacterController>().enabled = true;
        watcher.sightRange = 28f;
        var host = NetworkManager.ConnectedClients[0].PlayerObject;
        host.GetComponent<NetworkOxygenState>().IsInBunker.Value = true;
        Check(!watcher.IsEligible(host), "bunker players excluded");
        host.GetComponent<NetworkOxygenState>().IsInBunker.Value = false;
        GameObject wall = GameObject.CreatePrimitive(PrimitiveType.Cube);
        wall.transform.position = new Vector3(0f, 10f, 4f);
        wall.transform.localScale = new Vector3(8f, 30f, 0.5f);
        Physics.SyncTransforms();
        Check(!watcher.CanSee(host), "solid wall blocks detection");
        Destroy(wall);
        watcher.sightRange = 0f;
        watcher.roamSpeed = 1.6f;
        Vector3 before = watcher.transform.position;
        yield return new WaitForSeconds(3f);
        Debug.Log($"[WATCHER TEST] Roaming from {before} to {watcher.transform.position}; destination {watcher.PatrolDestination}; state {watcher.State.Value}; motor {watcher.GetComponent<CharacterController>().enabled}");
        Check(Vector3.Distance(before, watcher.transform.position) > 0.3f, "random roaming moves");
        Check(Mathf.Abs(watcher.transform.position.x) <= watcher.patrolHalfExtents.x + 0.01f && Mathf.Abs(watcher.transform.position.z) <= watcher.patrolHalfExtents.y + 0.01f, "roaming remains within configured bounds");
        Stage.Value = 99;
        Debug.Log("[WATCHER TEST] HOST PASS: " + assertions + " assertions, real NGO host/client transport.");
        File.WriteAllText("Logs/WatcherNetworkTestResult.txt", "PASS " + assertions + " assertions with real host and client.\n" + DateTime.UtcNow.ToString("O"));
        yield return new WaitForSeconds(1f);
        Application.Quit(0);
    }

    IEnumerator WaitFor(Func<bool> condition, float timeout, string label)
    {
        float deadline = Time.realtimeSinceStartup + timeout;
        while (!condition() && Time.realtimeSinceStartup < deadline) yield return null;
        Check(condition(), label);
    }
    void Check(bool condition, string label)
    {
        if (!condition) { Fail(label); return; }
        assertions++;
        Debug.Log("[WATCHER TEST] PASS " + label);
    }
    void Fail(string label)
    {
        Debug.LogError("[WATCHER TEST] FAIL " + label);
        File.WriteAllText("Logs/WatcherNetworkTestResult.txt", "FAIL " + label);
        Application.Quit(1);
        throw new Exception(label);
    }
}


