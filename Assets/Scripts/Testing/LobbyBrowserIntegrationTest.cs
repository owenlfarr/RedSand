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
            Check(await party.PrepareLobbyServicesAsync(), "Unity authentication ready");
            typeof(RelayPartyManager).GetField("cachedLocalUserName", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(party, "BH-" + role + "-" + nonce);
            if (role == "host") await Host();
            else if (role == "client") await Client();
            else if (role == "observer") await Observer();
            else await RejectedClient();
            File.WriteAllText(FileName(role + "-PASS"), assertions + " assertions");
            Debug.Log("[BROWSER INTEGRATION] PASS " + role + ": " + assertions + " assertions");
            await browser.StopAdvertising();
            party.LeaveLobbyFromUI();
            await Task.Delay(800);
            Application.Quit(0);
        }
        catch (Exception exception)
        {
            File.WriteAllText(FileName(role + "-FAIL"), exception.ToString());
            Debug.LogError("[BROWSER INTEGRATION] FAIL " + role + " " + exception);
            if (browser != null) await browser.StopAdvertising();
            if (party != null) party.LeaveLobbyFromUI();
            Application.Quit(1);
        }
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
            if (LobbyAdvertisement.Connected(listing) == count) { Check(true, "cloud network count " + count + "/3"); return; }
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
        Check(listing.Name == party.LocalDisplayName && LobbyAdvertisement.Connected(listing) == 1, "public owner name and initial 1/3 correct");
        Signal("public-ready", JsonUtility.ToJson(new Connection { id = id, code = party.LatestJoinCode }));
        await Wait(() => Has("public-joined") && Network.ConnectedClientsIds.Count == 2, 60, "browser client joins existing Relay flow");
        await CountIs(id, 2);
        Signal("two-connected");
        await Wait(() => Has("observer-joined") && Network.ConnectedClientsIds.Count == 3, 60, "third player connected");
        await CountIs(id, 3);
        Signal("full-ready");
        await Wait(() => Has("fourth-rejected"), 60, "fourth player rejected by existing approval");
        party.StartMatchFromUI();
        await Wait(() => party.MatchStarted && !browser.IsPubliclyListed && SceneManager.GetActiveScene().name != "MainMenu", 60, "listing hidden before match starts");
        Signal("started");
        await Wait(() => SceneManager.GetActiveScene().name == "Night 2" && Network.ConnectedClientsIds.Count == 3 &&
            Network.ConnectedClientsList.All(client => client.PlayerObject != null && client.PlayerObject.IsSpawned), 100, "original five-second Night 1 to Night 2 transfer and party spawn");
        await Wait(() => Has("client-in-night2") && Has("observer-in-night2") && Has("late-rejected"), 60, "all clients transfer and late join rejected");
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
        await SpectatorAndMenuChecks();
        await CheckWipeReturn();
    }
    async Task Observer()
    {
        var host = await ConnectionFile("public-ready");
        await Wait(() => Has("two-connected"), 60, "two-player count available");
        var query = await service.Query(LobbyAdvertisement.QueryOptions(RelayLobbyBrowser.Compatibility));
        var row = query.Results.Find(lobby => lobby.Id == host.id);
        Check(row != null && row.Name.StartsWith("BH-host-") && LobbyAdvertisement.Connected(row) == 2, "real browser query returns correct owner and 2/3");
        await browser.JoinListed(host.id);
        await Wait(() => Network.IsConnectedClient, 40, "third player joins through browser");
        Signal("observer-joined");
        await Wait(() => Has("started"), 100, "match starts");
        query = await service.Query(LobbyAdvertisement.QueryOptions(RelayLobbyBrowser.Compatibility));
        Check(query.Results.All(lobby => lobby.Id != host.id), "started lobby disappears from real cloud results");
        await Wait(() => SceneManager.GetActiveScene().name == "Night 2" && Network.LocalClient.PlayerObject != null && Network.LocalClient.PlayerObject.IsSpawned, 100, "third player transfers to Night 2");
        Signal("observer-in-night2", Network.LocalClientId.ToString());
        await CheckWipeReturn();
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
        Signal("wipe");
        double started = Time.realtimeSinceStartupAsDouble;
        foreach (var client in Network.ConnectedClientsList.ToArray()) client.PlayerObject.GetComponent<OxygenSystem>().MarkDeadFromMonster();
        await Wait(() => SceneManager.GetActiveScene().name == "MainMenu" && !Network.IsListening && !Network.ShutdownInProgress,
            20, "host wipe disconnects and returns to menu");
        Check(Time.realtimeSinceStartupAsDouble - started >= 4.9, "wipe waits five real seconds");
        await Wait(() => Has("client-returned") && Has("observer-returned"), 20, "all peers returned after wipe");
    }

    async Task SpectatorAndMenuChecks()
    {
        await Wait(() => Has("spectator-check"), 30, "spectator case started");
        await Wait(() => Network.LocalClient.PlayerObject.GetComponent<PlayerDeathSpectator>()?.IsSpectating == true, 15, "remote death enters spectating");
        var owner = Network.LocalClient.PlayerObject;
        var spectator = owner.GetComponent<PlayerDeathSpectator>();
        for (int target = 0; target < 2; target++)
        {
            await Task.Delay(250);
            var watched = PlayerDeathSpectator.HudPlayer;
            var oxygen = owner.GetComponent<OxygenSystem>();
            Check(watched != owner.transform && Mathf.Abs(oxygen.oxygenBarFill.fillAmount - watched.GetComponent<OxygenSystem>().GetOxygenPercent() / 100f) < .025f,
                "spectator oxygen follows watched target " + target);
            Check(FindFirstObjectByType<CompassPointer>().playerTransform == watched, "spectator power compass follows watched target " + target);
            var voice = Network.GetComponent<VoiceChatManager>();
            var listener = (Transform)typeof(VoiceChatManager).GetMethod("ResolveLocalVoiceListener", BindingFlags.Instance | BindingFlags.NonPublic)
                .Invoke(voice, new object[] { owner.transform });
            Check(listener == watched.GetComponent<PlayerController>().playerCamera, "spectator voice listener follows watched camera " + target);
            if (target == 0) typeof(PlayerDeathSpectator).GetMethod("SwitchSpectateTarget", BindingFlags.NonPublic | BindingFlags.Instance).Invoke(spectator, new object[] { 1 });
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
        await Wait(() => Has("full-ready"), 100, "public lobby reaches 3/3");
        var row = await service.Get(host.id);
        Check(!LobbyAdvertisement.IsJoinable(row, RelayLobbyBrowser.Compatibility), "full browser row cannot be joined");
        await party.JoinParty(host.code);
        await Wait(() => !string.IsNullOrEmpty(Network.DisconnectReason), 40, "fourth connection rejected");
        Check(Network.DisconnectReason.Contains("full") && !Network.IsConnectedClient, "existing three-player approval limit preserved");
        party.LeaveLobbyFromUI();
        await WaitStopped();
        Signal("fourth-rejected");
        await Wait(() => Has("started"), 100, "host starts match");
        await party.JoinParty(host.code);
        await Wait(() => !string.IsNullOrEmpty(Network.DisconnectReason), 40, "late connection rejected");
        Check(Network.DisconnectReason.Contains("already started") && !Network.IsConnectedClient, "existing match-start approval rejection preserved");
        Signal("late-rejected");
        await Wait(() => Has("complete"), 100, "host finished");
    }
}

#endif

