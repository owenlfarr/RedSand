#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Networking;
using Unity.Netcode;
using Unity.Services.Authentication;
using Unity.Services.Core;
using UnityEngine;
using UnityEngine.SceneManagement;

// Opt-in command-line integration fixture. Normal game launches do not create this object.
public sealed class LobbyBrowserIntegrationTest : MonoBehaviour
{
    [Serializable] class Connection { public string id; public string code; }
    static string role, nonce;
    static Task initialization;
    string directory;
    RelayPartyManager party;
    RelayLobbyBrowser browser;
    readonly UnityLobbyDirectoryBackend service = new UnityLobbyDirectoryBackend();
    int assertions;
    readonly System.Collections.Generic.Dictionary<string,int> savedCampaignPrefs = new System.Collections.Generic.Dictionary<string,int>();

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    static void Bootstrap()
    {
        var args = Environment.GetCommandLineArgs();
        int at = Array.IndexOf(args, "-lobbyBrowserTest");
        if (at < 0 || at + 2 >= args.Length) return;
        role = args[at + 1]; nonce = args[at + 2];
        initialization = UnityServices.InitializeAsync(new InitializationOptions().SetProfile("lb-" + role + "-" + nonce));
        var obj = new GameObject("Opt-in lobby integration test");
        DontDestroyOnLoad(obj);
        obj.AddComponent<LobbyBrowserIntegrationTest>();
    }

    async void Start()
    {
        directory = Path.Combine("Logs", "LobbyBrowserRun-" + nonce);
        Directory.CreateDirectory(directory);
        Application.runInBackground = true;
        Application.targetFrameRate = 30;
        QualitySettings.vSyncCount = 0;
        try
        {
            await initialization;
            await Wait(() => (party = FindFirstObjectByType<RelayPartyManager>()) != null && party.LobbyBrowser != null, 30, "menu browser initialized");
            browser = party.LobbyBrowser;
            if (role == "host")
            {
                foreach (string key in new[] { "Campaign.HostNight2Unlocked.v1", "Campaign.HostStartNight2.v1" }) savedCampaignPrefs[key] = PlayerPrefs.GetInt(key, -1);
                typeof(RelayPartyManager).GetField("playtestStartNightThree", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(party, false);
                PlayerPrefs.SetInt("Campaign.HostStartNight2.v1", 0);
            }
            Check(await party.PrepareLobbyServicesAsync(), "Unity authentication ready");
            typeof(RelayPartyManager).GetField("cachedLocalUserName", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(party, "BH-" + role + "-" + nonce);
            if (role == "host") await Host();
            else if (role == "client") await Client();
            else if (role == "observer") await Observer();
            else if (role == "fourth") await Fourth();
            else await RejectedClient();
            File.WriteAllText(FileName(role + "-PASS"), assertions + " assertions");
            Debug.Log("[BROWSER INTEGRATION] PASS " + role + ": " + assertions + " assertions");
            await browser.StopAdvertising();
            party.LeaveLobbyFromUI();
            await Task.Delay(800);
            RestoreCampaignPrefs();
            Application.Quit(0);
        }
        catch (Exception exception)
        {
            File.WriteAllText(FileName(role + "-FAIL"), exception.ToString());
            Debug.LogError("[BROWSER INTEGRATION] FAIL " + role + " " + exception);
            if (browser != null) await browser.StopAdvertising();
            if (party != null) party.LeaveLobbyFromUI();
            RestoreCampaignPrefs();
            Application.Quit(1);
        }
    }

    void RestoreCampaignPrefs()
    {
        foreach (var item in savedCampaignPrefs)
            if (item.Value < 0) PlayerPrefs.DeleteKey(item.Key); else PlayerPrefs.SetInt(item.Key, item.Value);
        if (savedCampaignPrefs.Count > 0) PlayerPrefs.Save();
    }
    string FileName(string name) => Path.Combine(directory, name + ".txt");
    void Signal(string name, string value = "ready") => File.WriteAllText(FileName(name), value);
    bool Has(string name) => File.Exists(FileName(name));
    async Task Wait(Func<bool> condition, int seconds, string label)
    {
        double expires = Time.realtimeSinceStartupAsDouble + seconds;
        while (!condition() && Time.realtimeSinceStartupAsDouble < expires)
        {
            if (Directory.GetFiles(directory, "*-FAIL.txt").Length != 0) throw new Exception("A peer failed while waiting for " + label);
            await Task.Delay(150);
        }
        Check(condition(), label);
    }
    void Check(bool condition, string label)
    {
        if (!condition) throw new Exception(label);
        assertions++;
        Debug.Log("[BROWSER INTEGRATION] PASS " + role + " " + label);
    }
    async Task<Connection> ConnectionFile(string name)
    {
        await Wait(() => Has(name), 100, name);
        return JsonUtility.FromJson<Connection>(File.ReadAllText(FileName(name)));
    }
    NetworkManager Network => NetworkManager.Singleton;
    async Task WaitStopped()
    {
        await Wait(() => !Network.IsListening && !Network.ShutdownInProgress, 20, "network fully stopped");
    }
    async Task CountIs(string id, int count)
    {
        for (int i = 0; i < 12; i++)
        {
            var listing = await service.Get(id);
            if (LobbyAdvertisement.Connected(listing) == count) { Check(true, "cloud network count " + count + "/4"); return; }
            await Task.Delay(1300);
        }
        Check(false, "cloud count did not reach " + count);
    }

    async Task Host()
    {
        party.CreateLobbyFromUI();
        Check(!browser.SelectedPublic, "host chooser defaults to private");
        browser.SelectVisibility(true);
        Check(browser.SelectedPublic, "public option selectable");
        browser.SelectVisibility(false);
        browser.Close();
        await party.CreateParty(false);
        Check(Network.IsHost && Network.IsListening, "original private Relay hosting works");
        Check(!browser.IsPubliclyListed && browser.ListingId == null, "private host never advertised");
        var query = await service.Query(LobbyAdvertisement.QueryOptions(RelayLobbyBrowser.Compatibility));
        Check(!query.Results.Any(row => row.Name == party.LocalDisplayName), "private host absent from real cloud query");
        Signal("private-ready", JsonUtility.ToJson(new Connection { code = party.LatestJoinCode }));
        await Wait(() => Has("private-joined") && Network.ConnectedClientsIds.Count == 2, 60, "private invite client connected");
        Signal("leave-private");
        await Wait(() => Has("private-left") && Network.ConnectedClientsIds.Count == 1, 30, "private client leaves cleanly");
        party.LeaveLobbyFromUI();
        await WaitStopped();
        await party.CreateParty(true);
        Check(Network.IsHost && browser.IsPubliclyListed, "public Relay host advertised: " + browser.LastError);
        string id = browser.ListingId;
        var listing = await service.Get(id);
        Check(listing.Name == party.LocalDisplayName && LobbyAdvertisement.Connected(listing) == 1, "public owner name and initial 1/4 correct");
        Signal("public-ready", JsonUtility.ToJson(new Connection { id = id, code = party.LatestJoinCode }));
        await Wait(() => Has("public-joined") && Network.ConnectedClientsIds.Count == 2, 60, "browser client joins existing Relay flow");
        await CountIs(id, 2);
        Signal("two-connected");
        await Wait(() => Has("observer-joined") && Network.ConnectedClientsIds.Count == 3, 60, "third player connected");
        await CountIs(id, 3);
        Signal("three-connected");
        await Wait(() => Has("fourth-joined") && Network.ConnectedClientsIds.Count == 4, 60, "fourth player connected");
        await CountIs(id, 4);
        await CheckFourMenuSlots();
        Signal("full-ready");
        await Wait(() => Has("fifth-rejected"), 60, "fifth player rejected by existing approval");
        party.StartMatchFromUI();
        await Wait(() => party.MatchStarted && !browser.IsPubliclyListed && SceneManager.GetActiveScene().name != "MainMenu", 60, "listing hidden before match starts");
        Signal("started");
        await Wait(() => SceneManager.GetActiveScene().name == "Night 2" && Network.ConnectedClientsIds.Count == 4 &&
            Network.ConnectedClientsList.All(client => client.PlayerObject != null && client.PlayerObject.IsSpawned), 100, "original five-second Night 1 to Night 2 transfer and party spawn");
        await Wait(() => Has("client-in-night2") && Has("observer-in-night2") && Has("fourth-in-night2") && Has("late-rejected"), 60, "all clients transfer and late join rejected");
        await GameplayChecks();
        Signal("complete");
    }

    async Task Client()
    {
        var privateHost = await ConnectionFile("private-ready");
        await party.JoinParty(privateHost.code);
        await Wait(() => Network.IsConnectedClient, 40, "private invite code joins Relay");
        Signal("private-joined");
        await Wait(() => Has("leave-private"), 40, "private leave requested");
        party.LeaveLobbyFromUI();
        await WaitStopped();
        Signal("private-left");
        var host = await ConnectionFile("public-ready");
        browser.ShowBrowser();
        Task cancelledJoin = browser.JoinListed(host.id);
        browser.Close();
        await cancelledJoin;
        Check(!Network.IsListening, "closing browser cancels an in-flight row join");
        await Task.Delay(1300);
        await browser.JoinListed(host.id);
        await Wait(() => Network.IsConnectedClient, 40, "public browser join connected");
        Signal("public-joined");
        await Wait(() => SceneManager.GetActiveScene().name == "Night 2" && Network.LocalClient.PlayerObject != null && Network.LocalClient.PlayerObject.IsSpawned, 150, "client keeps connection and player on Night 2 transfer");
        Signal("client-in-night2", Network.LocalClientId.ToString());
        await SurfaceChecks();
        await SpectatorAndMenuChecks();
        await CheckWipeReturn();
    }
    async Task Observer()
    {
        var host = await ConnectionFile("public-ready");
        await Wait(() => Has("two-connected"), 60, "two-player count available");
        var query = await service.Query(LobbyAdvertisement.QueryOptions(RelayLobbyBrowser.Compatibility));
        var row = query.Results.Find(lobby => lobby.Id == host.id);
        Check(row != null && row.Name.StartsWith("BH-host-") && LobbyAdvertisement.Connected(row) == 2, "real browser query returns correct owner and 2/4");
        await browser.JoinListed(host.id);
        await Wait(() => Network.IsConnectedClient, 40, "third player joins through browser");
        Signal("observer-joined");
        await Wait(() => Has("started"), 100, "match starts");
        query = await service.Query(LobbyAdvertisement.QueryOptions(RelayLobbyBrowser.Compatibility));
        Check(query.Results.All(lobby => lobby.Id != host.id), "started lobby disappears from real cloud results");
        await Wait(() => SceneManager.GetActiveScene().name == "Night 2" && Network.LocalClient.PlayerObject != null && Network.LocalClient.PlayerObject.IsSpawned, 100, "third player transfers to Night 2");
        Signal("observer-in-night2", Network.LocalClientId.ToString());
        await SurfaceChecks();
        await CheckWipeReturn();
    }
    async Task Fourth()
    {
        var host = await ConnectionFile("public-ready");
        await Wait(() => Has("three-connected"), 100, "third player present");
        var row = await service.Get(host.id);
        Check(row.MaxPlayers == 4 && LobbyAdvertisement.IsJoinable(row, RelayLobbyBrowser.Compatibility), "3/4 lobby accepts fourth player");
        await Task.Delay(1300); // The directory rate-limits consecutive Get requests.
        await browser.JoinListed(host.id);
        await Wait(() => Network.IsConnectedClient, 40, "fourth joins through browser");
        Signal("fourth-joined");
        await CheckFourMenuSlots();
        await Wait(() => Has("started"), 100, "four-player match starts");
        await Wait(() => SceneManager.GetActiveScene().name == "Night 2" && Network.LocalClient.PlayerObject != null && Network.LocalClient.PlayerObject.IsSpawned, 100, "fourth player transfers to Night 2");
        Check(Network.LocalClient.PlayerObject.IsOwner && Network.LocalClient.PlayerObject.GetComponent<PlayerController>().enabled, "fourth owns a controllable player");
        Check(RadioBroadcastStation.CanBroadcast(Network.LocalClient.PlayerObject.GetComponent<PlayerController>()), "fourth is eligible to use PA");
        var voice = Network.GetComponent<VoiceChatManager>();
        await Wait(() => !string.IsNullOrEmpty((string)typeof(VoiceChatManager).GetField("currentRadioChannel", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(voice)), 30, "fourth joined PA voice channel");
        voice.BeginRadioBroadcast();
        await Wait(() => voice.IsRadioBroadcasting, 15, "fourth switches transmission to PA");
        voice.EndRadioBroadcast();
        await Wait(() => !voice.IsRadioBroadcasting, 15, "fourth returns to proximity transmission");
        Signal("fourth-in-night2", Network.LocalClientId.ToString());
        await SurfaceChecks();
        await CheckWipeReturn();
    }
    async Task CheckFourMenuSlots()
    {
        await Wait(() => ((System.Collections.Generic.List<ulong>)typeof(RelayPartyManager).GetMethod("GetOrderedLobbyClientIds", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(party, null)).Count == 4, 15, "four-player menu roster");
        await Task.Delay(1000);
        for (int i = 1; i <= 4; i++)
        {
            var flags = BindingFlags.Instance | BindingFlags.NonPublic;
            var visual = (GameObject)typeof(RelayPartyManager).GetField("player" + i + "Visual", flags).GetValue(party);
            var label = (TMPro.TextMeshPro)typeof(RelayPartyManager).GetField("player" + i + "NameText", flags).GetValue(party);
            Check(visual != null && visual.activeInHierarchy && label != null && label.text.StartsWith("BH-"), "astronaut and synced name " + i);
        }
        if (role == "host") ScreenCapture.CaptureScreenshot(Path.Combine(directory, "FourPlayerMenu.png"));
    }
    async Task SurfaceChecks()
    {
        await Wait(() => Has("fourth-in-night2"), 45, "all four ready for surface telemetry");
        await Wait(() => !NightTransition.BlocksGameplay, 30, "night intro finished before interactions");
        var player = Network.LocalClient.PlayerObject;
        Vector3 original = player.transform.position;
        var control = player.GetComponent<PlayerController>();
        float gravity = control.gravity, walk = control.walkSpeed, sprint = control.sprintSpeed;
        control.gravity = control.walkSpeed = control.sprintSpeed = 0;
        typeof(PlayerController).GetField("velocity", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(control, Vector3.zero);
        Vector3 outside = original + new Vector3(100 + Network.LocalClientId * 3, 50, 0);
        player.GetComponent<OwnerNetworkTransform>().Teleport(outside, player.transform.rotation, player.transform.localScale);
        Signal(role + "-surface");
        await Wait(() => Has("host-surface") && Has("client-surface") && Has("observer-surface") && Has("fourth-surface"), 15, "four crew outside bunker");
        try { await Wait(() => NetworkGameState.Instance.CrewStressDisplay.Value.ToString().Split(new[] {"\n\n"}, StringSplitOptions.None).Length == 4, 30, "four replicated stress rows"); }
        catch { Debug.LogError("Stress diagnostic " + NetworkGameState.Instance.CrewStressDisplay.Value + " local=" + player.transform.position + " dead=" + player.GetComponent<NetworkOxygenState>().IsDead.Value); throw; }
        var monitor = FindFirstObjectByType<CrewStressMonitor>();
        Check(monitor.GetComponentsInChildren<TMPro.TMP_Text>().Any(t => t.name == "Reading" && t.text.Split(new[] {"\n\n"}, StringSplitOptions.None).Length == 4), "PA monitor renders all four crew rows");
        Signal(role + "-stress-checked");
        await Wait(() => Has("host-stress-checked") && Has("client-stress-checked") && Has("observer-stress-checked") && Has("fourth-stress-checked"), 15, "stress verified on all peers");
        player.GetComponent<OwnerNetworkTransform>().Teleport(original, player.transform.rotation, player.transform.localScale);
        control.gravity = gravity; control.walkSpeed = walk; control.sprintSpeed = sprint;
    }
    async Task CampaignChecks(bool host)
    {
        foreach (string scene in new[] { "Night 3", "Night 4", "Night 5" })
        {
            if (host) Network.GetComponent<NetworkLevelProgression>().TransferToLevel(scene);
            await Wait(() => SceneManager.GetActiveScene().name == scene && Network != null && Network.GetComponent<NetworkLevelProgression>()?.IsTransferring != true && Network.LocalClient?.PlayerObject != null && !NightTransition.BlocksGameplay, 60, "four-player campaign arrives " + scene);
            Check(Network.LocalClient.PlayerObject.GetComponent<NetworkOxygenState>().IsDead.Value == (role == "client"), "survivor/spectator state retained " + scene);
            Signal(role + "-" + scene);
            if (host) await Wait(() => Has("client-" + scene) && Has("observer-" + scene) && Has("fourth-" + scene), 20, "all four agree " + scene);
        }
    }
    bool shortenedNight;
    void Update()
    {
        // This fixture is opt-in; keep the authored 15-minute night unchanged.
        if (!shortenedNight && Network != null && Network.IsServer && NightTimeManager.Instance != null &&
            SceneManager.GetActiveScene().name == "Night1_MultiplayerTest")
        {
            NightTimeManager.Instance.useCustomEndingTime = true;
            NightTimeManager.Instance.endingSequenceTriggerTime = 5f;
            shortenedNight = true;
        }
    }

    async Task GameplayChecks()
    {
        await SurfaceChecks();
        await Task.Delay(600);
        Check(!string.IsNullOrEmpty(NetworkGameState.Instance.CrewStressDisplay.Value.ToString()), "actual night stress telemetry populated");
        var monitor = FindFirstObjectByType<CrewStressMonitor>();
        Check(monitor != null && monitor.GetComponentsInChildren<TMPro.TMP_Text>().Any(t => t.name == "Reading" &&
            t.text == NetworkGameState.Instance.CrewStressDisplay.Value.ToString()), "actual PA monitor shows authoritative crew data");
        ulong clientId = ulong.Parse(File.ReadAllText(FileName("client-in-night2")));
        Network.ConnectedClients[0].PlayerObject.GetComponent<NetworkOxygenState>().CurrentOxygen.Value = 57f;
        Network.ConnectedClients[clientId].PlayerObject.GetComponent<OxygenSystem>().MarkDeadFromMonster();
        Signal("spectator-check");
        await Wait(() => Has("spectator-checked"), 30, "remote spectator HUD and settings verified");
        await CampaignChecks(true);
        Signal("wipe");
        double started = Time.realtimeSinceStartupAsDouble;
        foreach (var client in Network.ConnectedClientsList.ToArray()) client.PlayerObject.GetComponent<OxygenSystem>().MarkDeadFromMonster();
        await Wait(() => SceneManager.GetActiveScene().name == "MainMenu" && !Network.IsListening && !Network.ShutdownInProgress,
            20, "host wipe disconnects and returns to menu");
        Check(Time.realtimeSinceStartupAsDouble - started >= 4.9, "wipe waits five real seconds");
        await Wait(() => Has("client-returned") && Has("observer-returned") && Has("fourth-returned"), 20, "all peers returned after wipe");
    }

    async Task SpectatorAndMenuChecks()
    {
        await Wait(() => Has("spectator-check"), 30, "spectator case started");
        await Wait(() => Network.LocalClient.PlayerObject.GetComponent<PlayerDeathSpectator>()?.IsSpectating == true, 15, "remote death enters spectating");
        var owner = Network.LocalClient.PlayerObject;
        var spectator = owner.GetComponent<PlayerDeathSpectator>();
        var seenTargets = new System.Collections.Generic.HashSet<Transform>();
        for (int target = 0; target < 3; target++)
        {
            await Task.Delay(250);
            var watched = PlayerDeathSpectator.HudPlayer;
            Check(seenTargets.Add(watched), "distinct spectator target " + target);
            var oxygen = owner.GetComponent<OxygenSystem>();
            Check(watched != owner.transform && Mathf.Abs(oxygen.oxygenBarFill.fillAmount - watched.GetComponent<OxygenSystem>().GetOxygenPercent() / 100f) < .025f,
                "spectator oxygen follows watched target " + target);
            Check(FindFirstObjectByType<CompassPointer>().playerTransform == watched, "spectator power compass follows watched target " + target);
            var voice = Network.GetComponent<VoiceChatManager>();
            var listener = (Transform)typeof(VoiceChatManager).GetMethod("ResolveLocalVoiceListener", BindingFlags.Instance | BindingFlags.NonPublic)
                .Invoke(voice, new object[] { owner.transform });
            Check(listener == watched.GetComponent<PlayerController>().playerCamera, "spectator voice listener follows watched camera " + target);
            if (target < 2) typeof(PlayerDeathSpectator).GetMethod("SwitchSpectateTarget", BindingFlags.NonPublic | BindingFlags.Instance).Invoke(spectator, new object[] { 1 });
        }
        Check(FindObjectsByType<AudioListener>(FindObjectsSortMode.None).Count(a => a.enabled) == 1 && !AudioListener.pause, "spectator has one active audio listener and restored sound");
        var settings = FindFirstObjectByType<PlayerSettingsUI>();
        var flags = BindingFlags.NonPublic | BindingFlags.Instance;
        typeof(PlayerSettingsUI).GetMethod("BuildGameMenu", flags).Invoke(settings, null);
        await Task.Delay(150);
        Check(PlayerSettingsUI.IsGameMenuOpen && Cursor.lockState == CursorLockMode.None, "in-game menu unlocks cursor while spectating");
        var root = (GameObject)typeof(PlayerSettingsUI).GetField("gameMenu", flags).GetValue(settings);
        root.transform.Find("Menu/Settings").GetComponent<UnityEngine.UI.Button>().onClick.Invoke();
        Check(root.transform.Find("Game Settings/DisplayOptions") != null, "in-game settings reuse display and audio options");
        float previous = PlayerPrefs.GetFloat("MouseSensitivity", 2f);
        try
        {
            root.transform.Find("Game Settings/SensitivitySlider").GetComponent<UnityEngine.UI.Slider>().value = 2.17f;
            Check(Mathf.Abs(owner.GetComponent<PlayerController>().mouseSensitivity - 2.17f) < .01f, "sensitivity applies to current player immediately");
        }
        finally
        {
            PlayerPrefs.SetFloat("MouseSensitivity", previous); PlayerPrefs.Save(); owner.GetComponent<PlayerController>().mouseSensitivity = previous;
            typeof(PlayerSettingsUI).GetMethod("CloseGameMenu", flags).Invoke(settings, null);
        }
        Check(!PlayerSettingsUI.IsGameMenuOpen && (Application.isBatchMode || Cursor.lockState == CursorLockMode.Locked), "resume closes menu (cursor capture checked only with a native game window)");
        Signal("spectator-checked");
    }

    async Task CheckWipeReturn()
    {
        await CampaignChecks(false);
        await Wait(() => Has("wipe"), 60, "party wipe started");
        await Wait(() => SceneManager.GetActiveScene().name == "MainMenu" && !Network.IsListening && !Network.ShutdownInProgress,
            20, "client wipe disconnects and returns to menu");
        Check(Cursor.lockState == CursorLockMode.None && !AudioListener.pause, "menu restores cursor and audio after wipe");
        Signal(role + "-returned");
        await Wait(() => Has("complete"), 20, "host finished");
    }

    async Task RejectedClient()
    {
        var host = await ConnectionFile("public-ready");
        await Wait(() => Has("full-ready"), 100, "public lobby reaches 4/4");
        var row = await service.Get(host.id);
        Check(!LobbyAdvertisement.IsJoinable(row, RelayLobbyBrowser.Compatibility), "full browser row cannot be joined");
        await party.JoinParty(host.code);
        await Wait(() => !string.IsNullOrEmpty(Network.DisconnectReason), 40, "fifth connection rejected");
        Check(Network.DisconnectReason.Contains("full") && !Network.IsConnectedClient, "existing four-player approval limit preserved");
        party.LeaveLobbyFromUI();
        await WaitStopped();
        Signal("fifth-rejected");
        await Wait(() => Has("started"), 100, "host starts match");
        await party.JoinParty(host.code);
        await Wait(() => !string.IsNullOrEmpty(Network.DisconnectReason), 40, "late connection rejected");
        Check(Network.DisconnectReason.Contains("already started") && !Network.IsConnectedClient, "existing match-start approval rejection preserved");
        Signal("late-rejected");
        await Wait(() => Has("complete"), 300, "host finished");
    }
}

#endif

