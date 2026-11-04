#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using UnityEngine;
using UnityEngine.SceneManagement;

// Explicit command-line-only regression. Uses the real station prefab and game systems.
public sealed class DrillNetworkTest : NetworkBehaviour
{
    static string role, run;
    int assertions, checkId;
    readonly HashSet<ulong> ready = new HashSet<ulong>();
    DrillSystem Drill => FindFirstObjectByType<DrillSystem>();
    class WinRandom : System.Random { public override int Next(int maxValue) => 0; }
    class MissRandom : System.Random { public override int Next(int maxValue) => maxValue - 1; }
    IEnumerator Start()
    {
        var args = Environment.GetCommandLineArgs();
        int index = Array.IndexOf(args, "-drillTest");
        if (index < 0) yield break;
        role = args[index + 1]; run = args[index + 2];
        Application.targetFrameRate = 60; QualitySettings.vSyncCount = 0; Application.runInBackground = true;
        var manager = Unity.Netcode.NetworkManager.Singleton;
        if (!manager.IsListening)
        {
            if (role == "client2")
                while (!File.Exists("Logs/Drill-" + run + "-latejoin.txt")) yield return null;
            DontDestroyOnLoad(manager.gameObject);
            manager.NetworkConfig.ConnectionApproval = false;
            manager.GetComponent<UnityTransport>().SetConnectionData("127.0.0.1", 17864, "127.0.0.1");
            if (role == "host") manager.StartHost(); else manager.StartClient();
        }
        float deadline = Time.realtimeSinceStartup + 180f;
        while (Time.realtimeSinceStartup < deadline) yield return null;
        Fail("regression timeout");
    }
    public override void OnNetworkSpawn()
    {
        if (IsServer) StartCoroutine(gameObject.scene.name == "DrillTransferTest" ? TransferCheck() : Run());
    }
    IEnumerator Run()
    {
        CheckRolls();
        yield return Wait(() => NetworkManager.ConnectedClientsIds.Count == 2, 30f, "host and first remote connected");
        yield return new WaitForSecondsRealtime(1f);
        ulong remote = NetworkManager.ConnectedClientsIds.First(id => id != 0);
        SetRandom(new WinRandom());
        yield return Place(remote, false);
        CommandClientRpc(remote, "enable");
        yield return new WaitForSecondsRealtime(0.3f);
        Check(!Drill.Current.Enabled, "distant client cannot enable drill");
        yield return Place(remote, true);
        CommandClientRpc(remote, "enable");
        yield return Wait(() => Drill.Current.Enabled, 3f, "remote client enables drill through actual ServerRpc");
        yield return new WaitForSecondsRealtime(1.15f);
        Drill.StopDrilling();
        Check(Drill.Current.Metal == 1 && Drill.Current.Gold == 1 && Drill.Current.Plasma == 1, "one independent roll per material after one full second");
        File.WriteAllText("Logs/Drill-" + run + "-latejoin.txt", "ready");
        yield return Wait(() => NetworkManager.ConnectedClientsIds.Count == 3, 30f, "late client joined seeded team stockpile");
        yield return new WaitForSecondsRealtime(0.5f);
        yield return VerifyAll("late join sees inventory and wall screen", Drill.Current);

        // Cancel an incomplete tick synchronously using the real outage path.
        CommandClientRpc(remote, "enable");
        yield return Wait(() => Drill.Current.Enabled, 3f, "restart after first yield");
        yield return new WaitForSecondsRealtime(0.4f);
        var before = Drill.Current;
        Call(FindFirstObjectByType<PowerSystem>(), "TriggerPowerOutage");
        Check(!Drill.Current.Enabled, "power outage cancels synchronously");
        yield return new WaitForSecondsRealtime(1.2f);
        Check(SameMaterials(before, Drill.Current), "no partial tick or production while power is out");
        CommandClientRpc(remote, "enable");
        yield return new WaitForSecondsRealtime(0.3f);
        Check(!Drill.Current.Enabled, "client cannot enable without power");
        FindFirstObjectByType<PowerSystem>().RestorePower();
        Check(!Drill.Current.Enabled, "restoring power requires manual drill restart");
        yield return VerifyAll("outage state and retained inventory replicated", Drill.Current);

        // Radar and defense retain their actual request/approval/cooldown paths.
        foreach (string equipment in new[] { "radar", "defense" })
        {
            SetRandom(new MissRandom());
            CommandClientRpc(remote, "enable");
            yield return Wait(() => Drill.Current.Enabled, 3f, "restart before " + equipment);
            yield return VerifyAll("enabled screen before " + equipment, Drill.Current);
            CommandClientRpc(remote, equipment);
            yield return Wait(() => !Drill.Current.Enabled, 3f, equipment + " successful use cancels drill");
            CommandClientRpc(remote, "enable");
            yield return Wait(() => Drill.Current.Enabled, 3f, "manual restart during " + equipment + " cooldown");
            CommandClientRpc(remote, equipment);
            yield return new WaitForSecondsRealtime(0.3f);
            Check(Drill.Current.Enabled, equipment + " rejected cooldown request does not cancel drill");
            Drill.StopDrilling();
            yield return VerifyAll(equipment + " result shared on all screens", Drill.Current);
        }
        // Only the host rolls: forced wins yield two of each, not two per peer.
        SetRandom(new WinRandom());
        before = Drill.Current;
        CommandClientRpc(remote, "enable");
        yield return Wait(() => Drill.Current.Enabled, 3f, "restart timed production");
        CommandClientRpc(remote, "enable"); // Duplicate desired-state request must not toggle/reset timer.
        yield return new WaitForSecondsRealtime(2.12f);
        CommandClientRpc(remote, "disable");
        yield return Wait(() => !Drill.Current.Enabled, 3f, "remote manual stop");
        Check(Drill.Current.Metal == before.Metal + 2 && Drill.Current.Gold == before.Gold + 2 && Drill.Current.Plasma == before.Plasma + 2,
            "two ticks awarded once globally despite three players and duplicate enable request");
        before = Drill.Current;
        yield return new WaitForSecondsRealtime(1.2f);
        Check(before.Equals(Drill.Current), "stopped drill produces no resources");
        yield return VerifyAll("shared stockpile after manual stop", Drill.Current);

        // Wall/proximity and incapacitated-client checks run on the server.
        var player = NetworkManager.ConnectedClients[remote].PlayerObject;
        Vector3 eye = player.transform.position + Vector3.up * 1.5f;
        Vector3 target = Drill.buttonCollider.ClosestPoint(eye);
        var wall = GameObject.CreatePrimitive(PrimitiveType.Cube);
        wall.transform.position = Vector3.Lerp(eye, target, 0.5f);
        wall.transform.rotation = Quaternion.LookRotation(target - eye);
        wall.transform.localScale = new Vector3(2f, 2f, 0.1f);
        Physics.SyncTransforms();
        CommandClientRpc(remote, "enable");
        yield return new WaitForSecondsRealtime(0.3f);
        Check(!Drill.Current.Enabled, "server rejects switch through wall");
        Destroy(wall); yield return null;
        var movement = player.GetComponent<NetworkPlayerMovementState>();
        movement.MovementEnabled.Value = false;
        CommandClientRpc(remote, "enable");
        yield return new WaitForSecondsRealtime(0.3f);
        Check(!Drill.Current.Enabled, "server rejects incapacitated player");
        movement.MovementEnabled.Value = true;
        player.GetComponent<NetworkOxygenState>().IsDead.Value = true;
        CommandClientRpc(remote, "enable");
        yield return new WaitForSecondsRealtime(0.3f);
        Check(!Drill.Current.Enabled, "server rejects dead player");
        yield return VerifyAll("invalid requests do not alter team totals", Drill.Current);

        var stockpile = NetworkManager.GetComponent<DrillSessionStockpile>();
        Check(stockpile != null && SameMaterials(stockpile.Materials, before), "session stockpile owns earned materials");
        File.WriteAllText("Logs/Drill-" + run + "-expected.txt", before.Metal + "," + before.Gold + "," + before.Plasma);
        File.WriteAllText("Logs/Drill-" + run + "-host-count.txt", assertions.ToString());
        var status = NetworkManager.SceneManager.LoadScene("DrillTransferTest", LoadSceneMode.Single);
        Check(status == SceneEventProgressStatus.Started, "network scene transfer started");
    }
    IEnumerator TransferCheck()
    {
        yield return Wait(() => Drill != null && Drill.IsSpawned, 10f, "replacement scene drill spawned");
        yield return new WaitForSecondsRealtime(1f);
        var values = File.ReadAllText("Logs/Drill-" + run + "-expected.txt").Split(',');
        var expected = new DrillSnapshot { Metal = int.Parse(values[0]), Gold = int.Parse(values[1]), Plasma = int.Parse(values[2]) };
        Check(Drill.Current.Equals(expected), "materials survive network scene transfer; drill stays off");
        yield return VerifyAll("next scene shows same team inventory on every peer", expected);
        int total = assertions + int.Parse(File.ReadAllText("Logs/Drill-" + run + "-host-count.txt"));
        FinishClientRpc(total);
        File.WriteAllText("Logs/DrillNetwork-host-PASS.txt", total + " host assertions; three peers, late join and scene transfer verified. Run=" + run);
        Debug.Log("[DRILL TEST] HOST PASS " + total);
        yield return new WaitForSecondsRealtime(0.5f);
        NetworkManager.Shutdown();
        yield return new WaitForSecondsRealtime(0.3f);
        Check(NetworkManager.GetComponent<DrillSessionStockpile>().Materials.Equals(default), "session shutdown resets materials");
        File.AppendAllText("Logs/DrillNetwork-host-PASS.txt", "\nPASS shutdown reset");
        Application.Quit(0);
    }

    IEnumerator Place(ulong id, bool near)
    {
        Vector3 position = Drill.drillButton.position + Vector3.forward * (near ? 1.8f : 20f);
        position.y = 1.6f;
        PositionClientRpc(id, position);
        yield return new WaitForSecondsRealtime(0.6f);
        if (near)
        {
            var actor = NetworkManager.ConnectedClients[id].PlayerObject;
            Vector3 eye = actor.transform.position + Vector3.up * 1.5f;
            Vector3 target = Drill.buttonCollider.ClosestPoint(eye);
            Debug.Log("[DRILL TEST] switch reach diagnostic player=" + actor.transform.position + " button=" + Drill.drillButton.position +
                " distance=" + Vector3.Distance(actor.transform.position, target) + " control=" + actor.GetComponent<PlayerController>().enabled +
                " dead=" + actor.GetComponent<NetworkOxygenState>().IsDead.Value + " movement=" + actor.GetComponent<NetworkPlayerMovementState>().MovementEnabled.Value);
            foreach (var hit in Physics.RaycastAll(eye, (target-eye).normalized, Vector3.Distance(eye,target)+0.02f, ~0, QueryTriggerInteraction.Ignore))
                Debug.Log("[DRILL TEST] reach hit " + hit.collider.name + " distance=" + hit.distance + " target=" + Vector3.Distance(eye,target));
            Check((bool)Call(Drill, "CanUse", NetworkManager.ConnectedClients[id].PlayerObject.gameObject), "placed remote player has valid real switch reach and line of sight");
        }
    }
    [ClientRpc]
    void PositionClientRpc(ulong id, Vector3 position)
    {
        if (NetworkManager.LocalClientId != id) return;
        var player = NetworkManager.LocalClient.PlayerObject;
        var motor = player.GetComponent<CharacterController>(); motor.enabled = false;
        player.transform.position = position;
        var control = player.GetComponent<PlayerController>();
        control.gravity = 0f; control.walkSpeed = control.sprintSpeed = 0f;
        typeof(PlayerController).GetField("velocity", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(control, Vector3.zero);
        motor.enabled = true;
    }
    [ClientRpc]
    void CommandClientRpc(ulong id, string command)
    {
        if (NetworkManager.LocalClientId != id) return;
        if (command == "enable" || command == "disable") Drill.RequestEnabled(command == "enable");
        else if (command == "radar") Call(FindFirstObjectByType<RadarSystem>(), "RequestRadarScanServerRpc", default(ServerRpcParams));
        else if (command == "defense") Call(FindFirstObjectByType<DefenseSystem>(), "RequestActivateDefenseServerRpc", default(ServerRpcParams));
    }
    IEnumerator VerifyAll(string label, DrillSnapshot expected)
    {
        ready.Clear(); checkId++;
        VerifyClientRpc(checkId, label, expected);
        yield return Wait(() => ready.Count == NetworkManager.ConnectedClientsIds.Count, 6f, "all peers: " + label);
    }
    [ClientRpc]
    void VerifyClientRpc(int id, string label, DrillSnapshot expected) { StartCoroutine(VerifyPeer(id, label, expected)); }
    IEnumerator VerifyPeer(int id, string label, DrillSnapshot expected)
    {
        yield return new WaitForSecondsRealtime(0.2f);
        Check(Drill.Current.Equals(expected), "network snapshot: " + label);
        Check(Drill.statusText.text == (expected.Enabled ? "DRILL: ENABLED" : "DRILL: DISABLED"), "wall status: " + label);
        Check(Drill.materialsText.text == $"METAL = {expected.Metal}\nGOLD = {expected.Gold}\nPLASMA = {expected.Plasma}", "wall counters: " + label);
        ReadyServerRpc(id);
    }
    [ServerRpc(RequireOwnership = false)]
    void ReadyServerRpc(int id, ServerRpcParams rpc = default) { if (id == checkId) ready.Add(rpc.Receive.SenderClientId); }
    [ClientRpc]
    void FinishClientRpc(int hostAssertions)
    {
        if (IsServer) return;
        File.WriteAllText("Logs/DrillNetwork-" + role + "-PASS.txt", assertions + " scene-local assertions plus previous scene checks in log. Host=" + hostAssertions + "; run=" + run);
        Debug.Log("[DRILL TEST] PASS " + role);
        Application.Quit(0);
    }
    void CheckRolls()
    {
        var state = new DrillSnapshot { Enabled = true };
        DrillSnapshot.Roll(ref state, new WinRandom());
        Check(state.Metal == 1 && state.Gold == 1 && state.Plasma == 1, "independent successes can award all three materials");
        DrillSnapshot.Roll(ref state, new MissRandom());
        Check(state.Metal == 1 && state.Gold == 1 && state.Plasma == 1, "nonwinning draws award nothing");
        state.Enabled = false; DrillSnapshot.Roll(ref state, new WinRandom());
        Check(state.Metal == 1 && state.Gold == 1 && state.Plasma == 1, "disabled yield guard");
        state = new DrillSnapshot { Enabled = true, Metal = int.MaxValue, Gold = int.MaxValue, Plasma = int.MaxValue };
        DrillSnapshot.Roll(ref state, new WinRandom());
        Check(state.Metal == int.MaxValue && state.Gold == int.MaxValue && state.Plasma == int.MaxValue, "counters cannot overflow negative");
        state = new DrillSnapshot { Enabled = true };
        var rng = new System.Random(4107);
        for (int i = 0; i < 600000; i++) DrillSnapshot.Roll(ref state, rng);
        Check(Math.Abs(state.Metal - 20000) < 1100, "metal 1/30 distribution");
        Check(Math.Abs(state.Gold - 7500) < 700, "gold 1/80 distribution");
        Check(Math.Abs(state.Plasma - 5000) < 550, "plasma 1/120 distribution");
    }
    void SetRandom(System.Random rng) { typeof(DrillSystem).GetField("random", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(Drill, rng); }
    static bool SameMaterials(DrillSnapshot a, DrillSnapshot b) => a.Metal == b.Metal && a.Gold == b.Gold && a.Plasma == b.Plasma;
    static object Call(object target, string method, params object[] args) => target.GetType().GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic).Invoke(target, args);
    IEnumerator Wait(Func<bool> condition, float timeout, string label)
    {
        float deadline = Time.realtimeSinceStartup + timeout;
        while (!condition() && Time.realtimeSinceStartup < deadline) yield return null;
        Check(condition(), label);
    }
    void Check(bool value, string label)
    {
        if (!value) Fail(label);
        assertions++; Debug.Log("[DRILL TEST] PASS " + role + " " + label);
    }
    void Fail(string label)
    {
        File.WriteAllText("Logs/DrillNetwork-" + role + "-FAIL.txt", label);
        Debug.LogError("[DRILL TEST] FAIL " + role + " " + label);
        Application.Quit(1); throw new Exception(label);
    }
}
#endif
