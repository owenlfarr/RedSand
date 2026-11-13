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
using UnityEngine.UI;

public sealed class DeathPresentationNetworkTest : NetworkBehaviour
{
    public NetworkVariable<int> Case = new NetworkVariable<int>();
    public NetworkVariable<int> Phase = new NetworkVariable<int>();
    public NetworkVariable<ulong> Victim = new NetworkVariable<ulong>(ulong.MaxValue);
    public Texture2D scareTexture;
    public ZombieAI firstMonster, secondMonster;
    public JumpscareSystem sharedSystem;
    public RushMonsterEvent rush;
    public SurveyorMonster surveyor;
    public PowerStationWatcher watcher;
    string role;
    int observedCase, assertions, stableFrames;
    bool reported, sawImage;
    float observedStart;
    readonly HashSet<ulong> ready = new HashSet<ulong>();

    IEnumerator Start()
    {
        string[] args = Environment.GetCommandLineArgs();
        int index = Array.IndexOf(args, "-deathPresentationTest");
        if (index < 0) yield break;
        role = args[index + 1];
        Application.targetFrameRate = 60;
        QualitySettings.vSyncCount = 0;
        Application.runInBackground = true;
        yield return null;
        var manager = Unity.Netcode.NetworkManager.Singleton;
        manager.NetworkConfig.ConnectionApproval = false;
        manager.gameObject.AddComponent<ReviveTestInputFreeze>();
        manager.GetComponent<UnityTransport>().SetConnectionData("127.0.0.1", 17852, "127.0.0.1");
        if (role == "host") manager.StartHost(); else manager.StartClient();
        float expires = Time.realtimeSinceStartup + 180f;
        while (Time.realtimeSinceStartup < expires) yield return null;
        Fail("timeout " + role);
    }
    public override void OnNetworkSpawn() { if (IsServer) StartCoroutine(Run()); }

    void Update()
    {
        if (!IsSpawned || NetworkManager.LocalClient == null || NetworkManager.LocalClient.PlayerObject == null || Phase.Value != 2) return;
        var player = NetworkManager.LocalClient.PlayerObject;
        if (observedCase != Case.Value)
        {
            observedCase = Case.Value;
            reported = false; sawImage = false; stableFrames = 0;
            observedStart = Time.unscaledTime;
        }
        if (reported) return;
        var death = player.GetComponent<PlayerDeathSpectator>();
        var presentation = player.GetComponent<LocalJumpscarePresentation>();
        float age = Time.unscaledTime - observedStart;
        bool mine = Victim.Value == NetworkManager.LocalClientId;
        float duration = Case.Value == 7 ? 5.2f : Case.Value == 5 ? 3f : 2f;
        if (mine)
        {
            if (Case.Value != 6)
            {
                if (presentation != null && presentation.IsShowing && !sawImage)
                {
                    sawImage = true;
                    if (Case.Value >= 8)
                    {
                        var picture = (Image)typeof(LocalJumpscarePresentation).GetField("image", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(presentation);
                        Check(picture.canvas.transform.parent == null, "Watcher scare stays outside the moving player hierarchy case " + Case.Value);
                        Check(picture.sprite.texture == watcher.deathJumpscareImage, "Watcher uses its own close-up image case " + Case.Value);
                        Check(watcher.deathJumpscareSound != null && watcher.deathJumpscareSound.length > 0.1f, "Watcher death sound assigned case " + Case.Value);
                    }
                    observedStart = Time.unscaledTime;
                    age = 0f;
                }
                if (!sawImage && age > 1.5f) Fail("no victim jumpscare, case " + Case.Value);
                if (sawImage && age < duration - 0.15f)
                {
                    if (!presentation.ImageIsVisible) Fail("image blinked/disappeared before expiry, case " + Case.Value);
                    if (death != null && death.IsSpectating) Fail("spectating started over active scare, case " + Case.Value);
                    if (AudioListener.pause) Fail("monster scare incorrectly used oxygen mute, case " + Case.Value);
                    stableFrames++;
                }
                if (sawImage && age > duration + 0.2f && presentation.IsShowing) Fail("image stuck after its timer, case " + Case.Value);
            }
            else if (presentation != null && presentation.IsShowing) Fail("oxygen death showed a monster image");
            if (age > Mathf.Max(4f, duration) + 2.3f)
            {
                Check(death != null && death.IsSpectating, "victim spectating case " + Case.Value);
                Check(death.SpectatedClientId != NetworkManager.LocalClientId, "victim watches a survivor case " + Case.Value);
                Check(presentation == null || !presentation.IsShowing, "no reappearing image case " + Case.Value);
                Check(!sharedSystem.IsJumpscareActive(), "global scare active flag cleared case " + Case.Value);
                var overlay = typeof(PlayerDeathSpectator).GetField("overlayCanvas", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(death) as Canvas;
                Check(overlay == null || !overlay.gameObject.activeSelf, "death fade cleared case " + Case.Value);
                Check(!AudioListener.pause, "audio restored case " + Case.Value);
                Check(!player.GetComponent<PlayerController>().enabled, "dead player control stays disabled case " + Case.Value);
                if (Case.Value != 6) Check(stableFrames > 20, "continuous timed image frames case " + Case.Value);
                var listeners = FindObjectsByType<AudioListener>(FindObjectsSortMode.None).Count(listener => listener.enabled);
                Check(listeners == 1, "one active listener while spectating case " + Case.Value);
                reported = true;
                PassedServerRpc(Case.Value);
            }
        }
        else
        {
            if (presentation != null && presentation.IsShowing) Fail("jumpscare leaked onto survivor case " + Case.Value);
            if (player.GetComponent<NetworkOxygenState>().IsDead.Value) Fail("survivor killed by wrong victim event");
            if (age > Mathf.Max(4f, duration) + 2.3f)
            {
                Check(death == null || !death.IsSpectating, "survivor not spectating case " + Case.Value);
                Check(player.GetComponent<PlayerController>().playerCamera.GetComponent<Camera>().enabled, "survivor view intact case " + Case.Value);
                reported = true;
                PassedServerRpc(Case.Value);
            }
        }
    }

    [ServerRpc(RequireOwnership = false)]
    void PassedServerRpc(int current, ServerRpcParams rpc = default)
    {
        if (current == Case.Value) ready.Add(rpc.Receive.SenderClientId);
    }

    IEnumerator Run()
    {
        yield return WaitFor(() => NetworkManager.ConnectedClientsIds.Count == 4, 30f, "host and three real clients connected");
        ulong remote = NetworkManager.ConnectedClientsIds.Max();
        for (int test = 1; test <= 9; test++)
        {
            Phase.Value = 0;
            ready.Clear();
            Case.Value = test;
            Victim.Value = test == 2 || test == 5 || test == 7 || test == 9 ? 0UL : remote;
            firstMonster.GetComponent<CharacterController>().enabled = false;
            secondMonster.GetComponent<CharacterController>().enabled = false;
            firstMonster.transform.position = new Vector3(100f, 0f, 100f);
            secondMonster.transform.position = new Vector3(120f, 0f, 100f);
            foreach (var client in NetworkManager.ConnectedClientsList.ToList())
            {
                if (client.PlayerObject != null) client.PlayerObject.Despawn(true);
            }
            yield return new WaitForSecondsRealtime(0.3f);
            int slot = 0;
            foreach (var client in NetworkManager.ConnectedClientsList.ToList())
            {
                var actor = Instantiate(NetworkManager.NetworkConfig.PlayerPrefab, new Vector3(slot++ * 12f, 0.2f, 0f), Quaternion.identity);
                actor.GetComponent<NetworkObject>().SpawnAsPlayerObject(client.ClientId, true);
            }
            yield return new WaitForSecondsRealtime(0.7f);
            Phase.Value = 2;
            yield return new WaitForSecondsRealtime(0.15f);
            var victim = NetworkManager.ConnectedClients[Victim.Value].PlayerObject;
            if (test == 1 || test == 2)
            {
                var monster = test == 1 ? firstMonster : secondMonster;
                var motor = monster.GetComponent<CharacterController>();
                motor.enabled = false;
                monster.transform.position = victim.transform.position + Vector3.back * 1.2f;
                motor.enabled = true;
                // Use the actual CharacterController hit callback, not a replacement kill implementation.
                Physics.SyncTransforms();
                motor.Move(Vector3.forward * 0.8f);
                yield return null;
                Check(victim.GetComponent<NetworkOxygenState>().IsDead.Value, "actual Movement Detected collision kills intended victim " + test + " victim=" + victim.transform.position + " monster=" + monster.transform.position + " blocked=" + NightTransition.BlocksGameplay);
            }
            else if (test == 6) victim.GetComponent<NetworkOxygenState>().CurrentOxygen.Value = 0f;
            else if (test >= 8)
            {
                watcher.reactionSeconds = 0.1f;
                typeof(PowerStationWatcher).GetMethod("BeginEncounter", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(watcher, new object[] { victim });
                yield return new WaitForSecondsRealtime(0.3f);
                ViolateWatcherClientRpc(new ClientRpcParams { Send = new ClientRpcSendParams { TargetClientIds = new[] { Victim.Value } } });
                yield return new WaitForSecondsRealtime(0.3f);
                Check(victim.GetComponent<NetworkOxygenState>().IsDead.Value, "actual Watcher activity RPC kills intended victim " + test);
                Check(watcher.State.Value == (int)PowerStationWatcher.Phase.Dormant, "Watcher still disappears after kill " + test);
            }
            else
            {
                victim.GetComponent<OxygenSystem>().MarkDeadFromMonster();
                TriggerPresentationClientRpc(test, new ClientRpcParams { Send = new ClientRpcSendParams { TargetClientIds = new[] { Victim.Value } } });
            }
            yield return WaitFor(() => ready.Count == 4, 13f, "all peers completed case " + test);
            if (test >= 8)
            {
                typeof(PowerStationWatcher).GetMethod("PlayDeathJumpscareClientRpc", BindingFlags.Instance | BindingFlags.NonPublic)
                    .Invoke(watcher, new object[] { watcher.Encounter.Value, new ClientRpcParams { Send = new ClientRpcSendParams { TargetClientIds = new[] { Victim.Value } } } });
                yield return new WaitForSecondsRealtime(0.3f);
                CheckNoReplayClientRpc();
                yield return new WaitForSecondsRealtime(0.3f);
            }
            Phase.Value = 0;
            yield return new WaitForSecondsRealtime(0.3f);
        }
        Debug.Log("[DEATH TEST] HOST PASS " + assertions + " local assertions plus per-peer results.");
        File.WriteAllText("Logs/DeathPresentation-host-PASS.txt", assertions + " host assertions; nine scenarios verified by all four peers, including host/client Watcher kills.");
        FinishClientRpc();
        yield return new WaitForSecondsRealtime(0.8f);
        Application.Quit(0);
    }

    [ClientRpc]
    void ViolateWatcherClientRpc(ClientRpcParams rpc = default)
    {
        watcher.ReportActivityServerRpc(watcher.Encounter.Value, watcher.Clock, true);
    }
    [ClientRpc]
    void CheckNoReplayClientRpc()
    {
        var presentation = NetworkManager.LocalClient.PlayerObject.GetComponent<LocalJumpscarePresentation>();
        Check(presentation == null || !presentation.IsShowing, "duplicate Watcher death event cannot replay image case " + Case.Value);
    }
    [ClientRpc]
    void TriggerPresentationClientRpc(int test, ClientRpcParams rpc = default)
    {
        if (test == 3 || test == 7)
        {
            sharedSystem.jumpscareDuration = test == 7 ? 5.2f : 2f;
            sharedSystem.TriggerJumpscareWithTexture(scareTexture);
        }
        if (test == 4)
        {
            var routine = (IEnumerator)typeof(RushMonsterEvent).GetMethod("ShowBuiltInJumpscare", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(rush, new object[] { false });
            StartCoroutine(routine);
        }
        if (test == 5)
        {
            var routine = (IEnumerator)typeof(SurveyorMonster).GetMethod("ShowSurveyorDeath", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(surveyor, new object[] { false });
            StartCoroutine(routine);
        }
    }
    [ClientRpc]
    void FinishClientRpc()
    {
        if (IsServer) return;
        File.WriteAllText("Logs/DeathPresentation-" + role + "-PASS.txt", assertions + " assertions across nine cases.");
        Debug.Log("[DEATH TEST] PASS " + role + " " + assertions + " assertions.");
        Application.Quit(0);
    }
    IEnumerator WaitFor(Func<bool> condition, float seconds, string label)
    {
        float deadline = Time.realtimeSinceStartup + seconds;
        while (!condition() && Time.realtimeSinceStartup < deadline) yield return null;
        Check(condition(), label);
    }
    void Check(bool value, string label)
    {
        if (!value) { Fail(label); return; }
        assertions++;
        Debug.Log("[DEATH TEST] PASS " + role + " " + label);
    }
    void Fail(string label)
    {
        File.WriteAllText("Logs/DeathPresentation-" + role + "-FAIL.txt", label);
        Debug.LogError("[DEATH TEST] FAIL " + role + " " + label);
        Application.Quit(1);
        throw new Exception(label);
    }
}
#endif
