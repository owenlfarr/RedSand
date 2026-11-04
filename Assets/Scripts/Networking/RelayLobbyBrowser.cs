using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using TMPro;
using Unity.Netcode;
using Unity.Services.Lobbies.Models;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace Networking
{
    /// <summary>Menu-only discovery UI. It never starts or configures a transport itself.</summary>
    public sealed class RelayLobbyBrowser : MonoBehaviour
    {
        RelayPartyManager owner;
        ILobbyDirectoryBackend backend;
        LobbyAdvertisement advertisement;
        GameObject canvasObject, backdrop, dialog, browseButtonObject, rowsRoot, hostStatusObject;
        TMP_Text notice, hostStatus, visibilityDescription;
        Button publicButton, privateButton, refreshButton, createButton;
        readonly List<Button> rowButtons = new List<Button>();
        bool selectedPublic, requestedPublic, browserOpen, querying, joining, ticking, publishing;
        int viewVersion, lastCount;
        string lastName, relayCode, warning;
        float nextRefresh, nextHostUpdate, nextHeartbeat;
        public string ListingId => advertisement?.Id;
        public bool IsPubliclyListed => advertisement != null && advertisement.IsListed;
        public bool SelectedPublic => selectedPublic;
        public string LastError => warning;
        public bool BrowserOpen => browserOpen;
        public static string Compatibility => "browser1-" + Application.version + "-" + (NetworkManager.Singleton != null ? NetworkManager.Singleton.NetworkConfig.ProtocolVersion : 0);

        public void Initialize(RelayPartyManager manager)
        {
            owner = manager;
            backend = new UnityLobbyDirectoryBackend();
            advertisement = new LobbyAdvertisement(backend);
            BuildShell();
        }
        void OnEnable() { SceneManager.sceneLoaded += SceneLoaded; }
        void OnDisable()
        {
            SceneManager.sceneLoaded -= SceneLoaded;
            requestedPublic = false;
            if (advertisement != null) _ = advertisement.Stop();
            Close();
        }
        void OnDestroy() { if (canvasObject != null) Destroy(canvasObject); }
        void SceneLoaded(Scene scene, LoadSceneMode mode)
        {
            Close();
            if (owner != null && scene.name != owner.MenuSceneName)
            {
                requestedPublic = false;
                if (advertisement != null) _ = advertisement.Stop();
            }
        }

        void Update()
        {
            if (owner == null) return;
            bool menu = SceneManager.GetActiveScene().name == owner.MenuSceneName;
            if (canvasObject == null) BuildShell();
            canvasObject.SetActive(menu);
            var network = NetworkManager.Singleton;
            bool connected = network != null && network.IsListening;
            browseButtonObject.SetActive(menu && !connected);
            browseButtonObject.GetComponent<Button>().interactable = !owner.PartyBusy;
            bool host = connected && network.IsHost && !owner.MatchStarted && menu;
            hostStatusObject.SetActive(host);
            if (host)
                hostStatus.text = (IsPubliclyListed ? "PUBLIC LOBBY" : "PRIVATE / INVITE CODE") + "   " + network.ConnectedClientsIds.Count + "/3" +
                    (string.IsNullOrEmpty(warning) ? "" : "\n" + warning);
            if (!host && requestedPublic && !publishing)
            {
                requestedPublic = false;
                _ = advertisement.Stop();
            }
            if (host && requestedPublic && !publishing && !ticking)
            {
                int count = network.ConnectedClientsIds.Count;
                if (Time.unscaledTime >= nextHostUpdate && (count != lastCount || lastName != owner.LocalDisplayName))
                    _ = UpdateHost(count);
                if (Time.unscaledTime >= nextHeartbeat) _ = Heartbeat();
            }
            if (browserOpen && !joining && !querying && Time.unscaledTime >= nextRefresh) _ = Refresh();
            if (refreshButton != null) refreshButton.interactable = !querying && !joining && Time.unscaledTime >= nextRefresh - 4f;
            if (createButton != null) createButton.interactable = !owner.PartyBusy;
            if (Input.GetKeyDown(KeyCode.Escape) && backdrop.activeSelf) Close();
        }

        public async Task HostReady(bool isPublic, string code, string displayName)
        {
            requestedPublic = isPublic;
            relayCode = code;
            publishing = true;
            warning = null;
            try
            {
                var network = NetworkManager.Singleton;
                lastCount = network != null ? network.ConnectedClientsIds.Count : 1;
                lastName = displayName;
                bool published = await advertisement.Publish(isPublic, displayName, code, Compatibility, Mathf.Max(1, lastCount), 3);
                if (!published && requestedPublic) warning = "Public listing unavailable. Invite code still works.";
                nextHeartbeat = Time.unscaledTime + 12f;
                nextHostUpdate = Time.unscaledTime + 2f;
            }
            finally { publishing = false; }
        }
        async Task UpdateHost(int count)
        {
            ticking = true;
            nextHostUpdate = Time.unscaledTime + 2f;
            try
            {
                if (await advertisement.Update(owner.LocalDisplayName, relayCode, Compatibility, count))
                {
                    lastCount = count;
                    lastName = owner.LocalDisplayName;
                    warning = null;
                }
                else warning = "Lobby list update delayed. Invite code still works.";
            }
            finally { ticking = false; }
        }
        async Task Heartbeat()
        {
            ticking = true;
            nextHeartbeat = Time.unscaledTime + 12f;
            try { await advertisement.Pulse(); }
            finally { ticking = false; }
        }
        public async Task<bool> HideForStart()
        {
            bool hidden = await advertisement.HideForStart();
            if (!hidden) warning = "Could not close the public listing. Please try Start again.";
            return hidden;
        }
        public Task RestoreAfterFailedStart() => HostReady(requestedPublic, relayCode, owner.LocalDisplayName);
        public Task StopAdvertising()
        {
            requestedPublic = false;
            ++viewVersion;
            Close();
            return advertisement.Stop();
        }

        public void ShowHostOptions()
        {
            if (owner == null || owner.PartyBusy || (NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening)) return;
            Close();
            backdrop.SetActive(true);
            dialog = Panel(backdrop.transform, "Host lobby", new Vector2(720f, 430f), Vector2.zero);
            Frame(dialog.transform, 720f, 430f);
            Caption(dialog.transform, "MISSION CONTROL / CREW ACCESS", new Vector2(-18f, 174f), new Vector2(600f, 24f));
            Text(dialog.transform, "HOST A LOBBY", 32f, new Vector2(0f, 121f), new Vector2(650f, 46f));
            privateButton = Button(dialog.transform, "Private", new Vector2(-160f, 52f), new Vector2(280f, 58f), () => SelectVisibility(false));
            publicButton = Button(dialog.transform, "Public", new Vector2(160f, 52f), new Vector2(280f, 58f), () => SelectVisibility(true));
            visibilityDescription = Text(dialog.transform, "", 20f, new Vector2(0f, -22f), new Vector2(640f, 72f));
            SelectVisibility(false); // Preserve the existing invite-only default.
            createButton = Button(dialog.transform, "CREATE LOBBY", new Vector2(-158f, -143f), new Vector2(284f, 56f), () => _ = CreateSelected());
            createButton.GetComponent<Image>().color = Selected;
            Button(dialog.transform, "CANCEL", new Vector2(158f, -143f), new Vector2(284f, 56f), Close);
        }
        public void SelectVisibility(bool isPublic)
        {
            selectedPublic = isPublic;
            if (privateButton == null || publicButton == null) return;
            privateButton.GetComponent<Image>().color = !isPublic ? Selected : Surface;
            publicButton.GetComponent<Image>().color = isPublic ? Selected : Surface;
            visibilityDescription.text = isPublic ? "Anyone can find and join your lobby in the browser.\nYour invite code will also work." : "Only people with your invite code can join.\nYour lobby will not appear in the browser.";
        }
        async Task CreateSelected()
        {
            if (owner.PartyBusy) return;
            bool choice = selectedPublic;
            Close();
            await owner.CreateParty(choice);
        }

        public void ShowBrowser()
        {
            if (owner == null || owner.PartyBusy || (NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening)) return;
            Close();
            backdrop.SetActive(true);
            browserOpen = true;
            dialog = Panel(backdrop.transform, "Public lobby browser", new Vector2(960f, 760f), Vector2.zero);
            Frame(dialog.transform, 960f, 760f);
            Caption(dialog.transform, "MISSION CONTROL / PUBLIC NETWORK", new Vector2(-100f, 330f), new Vector2(660f, 24f));
            var title = Text(dialog.transform, "FIND YOUR CREW", 36f, new Vector2(-100f, 278f), new Vector2(660f, 54f));
            title.alignment = TextAlignmentOptions.MidlineLeft;
            Button(dialog.transform, "CLOSE", new Vector2(362f, 303f), new Vector2(148f, 44f), Close);
            Caption(dialog.transform, "LOBBY HOST", new Vector2(-206f, 214f), new Vector2(450f, 30f));
            Caption(dialog.transform, "CREW", new Vector2(183f, 214f), new Vector2(110f, 30f));
            Rule(dialog.transform, "Column rule", new Vector2(872f, 1f), new Vector2(0f, 188f), Border);
            rowsRoot = new GameObject("Lobby rows", typeof(RectTransform));
            rowsRoot.transform.SetParent(dialog.transform, false);
            rowsRoot.GetComponent<RectTransform>().sizeDelta = new Vector2(872f, 420f);
            Rule(dialog.transform, "Footer rule", new Vector2(872f, 1f), new Vector2(0f, -250f), Border);
            notice = Text(dialog.transform, "Finding public lobbies...", 17f, new Vector2(-118f, -298f), new Vector2(636f, 64f));
            notice.alignment = TextAlignmentOptions.MidlineLeft;
            notice.color = Muted;
            refreshButton = Button(dialog.transform, "REFRESH", new Vector2(330f, -299f), new Vector2(212f, 50f), () => _ = Refresh());
            refreshButton.GetComponent<Image>().color = Selected;
            nextRefresh = 0f;
            _ = Refresh();
        }

        public async Task Refresh()
        {
            if (!browserOpen || querying || joining || Time.unscaledTime < nextRefresh) return;
            querying = true;
            int ticket = viewVersion;
            nextRefresh = Time.unscaledTime + 5f;
            try
            {
                if (!await owner.PrepareLobbyServicesAsync()) throw new Exception("Services unavailable");
                QueryResponse response = await backend.Query(LobbyAdvertisement.QueryOptions(Compatibility));
                if (ticket != viewVersion || !browserOpen || this == null) return;
                var waiting = response.Results.FindAll(lobby => LobbyAdvertisement.IsWaiting(lobby, Compatibility));
                waiting.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase));
                ClearRows();
                for (int i = 0; i < Mathf.Min(6, waiting.Count); i++) AddRow(waiting[i], i);
                notice.text = waiting.Count == 0 ? "No public lobbies yet. You can host one!" : "Invite codes also work from the main menu.";
            }
            catch (Exception exception)
            {
                if (ticket == viewVersion && browserOpen && notice != null)
                {
                    ClearRows();
                    notice.text = "Public lobbies are unavailable. Try Refresh.";
                }
                Debug.LogWarning("[Lobby Browser] Query failed: " + exception.Message);
            }
            finally { querying = false; }
        }

        public async Task JoinListed(string id)
        {
            if (joining || owner.PartyBusy) return;
            joining = true;
            int ticket = viewVersion;
            foreach (var button in rowButtons) if (button != null) button.interactable = false;
            if (notice != null) notice.text = "Checking lobby...";
            try
            {
                if (!await owner.PrepareLobbyServicesAsync()) throw new Exception("Services unavailable");
                // Re-read immediately before joining: cached rows can be full/closed since Refresh.
                Lobby lobby = await backend.Get(id);
                if (ticket != viewVersion || this == null || owner == null) return;
                if (!LobbyAdvertisement.IsJoinable(lobby, Compatibility))
                {
                    if (notice != null) notice.text = "That lobby is full or has started. Refresh to find another.";
                    return;
                }
                string code = LobbyAdvertisement.Value(lobby, LobbyAdvertisement.RelayKey);
                Close();
                await owner.JoinParty(code); // The original Relay/approval/spawn path, unchanged.
            }
            catch (Exception exception)
            {
                if (notice != null) notice.text = "That lobby is no longer available. Try Refresh.";
                Debug.LogWarning("[Lobby Browser] Join failed: " + exception.Message);
            }
            finally { joining = false; nextRefresh = Mathf.Min(nextRefresh, Time.unscaledTime + 1f); }
        }

        void AddRow(Lobby lobby, int index)
        {
            GameObject row = Panel(rowsRoot.transform, "Lobby " + index, new Vector2(872f, 62f), new Vector2(0f, 145f - index * 70f));
            row.GetComponent<Image>().color = Surface;
            Rule(row.transform, "Signal", new Vector2(3f, 24f), new Vector2(-434f, 0f), Selected);
            var name = Text(row.transform, lobby.Name, 20f, new Vector2(-175f, 0f), new Vector2(470f, 46f));
            name.alignment = TextAlignmentOptions.MidlineLeft;
            name.overflowMode = TextOverflowModes.Ellipsis;
            Text(row.transform, LobbyAdvertisement.Connected(lobby) + "/" + lobby.MaxPlayers, 20f, new Vector2(183f, 0f), new Vector2(110f, 45f));
            bool room = LobbyAdvertisement.IsJoinable(lobby, Compatibility);
            var join = Button(row.transform, room ? "JOIN" : "FULL", new Vector2(350f, 0f), new Vector2(146f, 42f), () => _ = JoinListed(lobby.Id));
            join.GetComponent<Image>().color = Selected;
            join.interactable = room;
            rowButtons.Add(join);
        }
        void ClearRows()
        {
            rowButtons.Clear();
            if (rowsRoot != null)
                foreach (Transform child in rowsRoot.transform) { child.gameObject.SetActive(false); Destroy(child.gameObject); }
        }
        public void Close()
        {
            ++viewVersion;
            browserOpen = false;
            if (backdrop != null) backdrop.SetActive(false);
            if (dialog != null) { dialog.SetActive(false); Destroy(dialog); }
            dialog = null;
            notice = null;
            refreshButton = null;
            createButton = null;
            privateButton = publicButton = null;
            rowButtons.Clear();
        }

        void BuildShell()
        {
            canvasObject = new GameObject("Lobby Browser UI", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            canvasObject.transform.SetParent(transform, false);
            var canvas = canvasObject.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 100;
            var scaler = canvasObject.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1800f, 1012.5f);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.Expand;
            scaler.matchWidthOrHeight = 0.5f;
            var browse = Button(canvasObject.transform, "SERVER BROWSER", Vector2.zero, new Vector2(440f, 46f), ShowBrowser);
            browseButtonObject = browse.gameObject;
            var rect = browse.GetComponent<RectTransform>();
            rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = new Vector2(480f, -262f);
            var browseLabel = browse.GetComponentInChildren<TMP_Text>();
            browseLabel.alignment = TextAlignmentOptions.MidlineLeft;
            browseLabel.rectTransform.sizeDelta = new Vector2(392f, 46f);
            browseLabel.fontSize = 18f;
            Rule(browse.transform, "Accent", new Vector2(3f, 22f), new Vector2(-218f, 0f), Selected);
            hostStatus = Text(canvasObject.transform, "", 19f, Vector2.zero, new Vector2(670f, 74f));
            hostStatusObject = hostStatus.gameObject;
            hostStatus.rectTransform.anchorMin = hostStatus.rectTransform.anchorMax = new Vector2(1f, 0f);
            hostStatus.rectTransform.anchoredPosition = new Vector2(-365f, 70f);
            hostStatusObject.SetActive(false);
            backdrop = Panel(canvasObject.transform, "Lobby dialog backdrop", Vector2.zero, Vector2.zero);
            var background = backdrop.GetComponent<RectTransform>();
            background.anchorMin = Vector2.zero;
            background.anchorMax = Vector2.one;
            background.offsetMin = background.offsetMax = Vector2.zero;
            backdrop.GetComponent<Image>().color = new Color(0f, 0f, 0f, 0.80f);
            backdrop.SetActive(false);
        }

        static readonly Color Surface = new Color(0.068f, 0.079f, 0.093f, 0.98f);
        static readonly Color Selected = new Color(0.82f, 0.25f, 0.12f, 1f);
        static readonly Color Border = new Color(0.25f, 0.27f, 0.28f, 1f);
        static readonly Color Muted = new Color(0.65f, 0.67f, 0.65f, 1f);
        static TMP_FontAsset menuFont;
        static void Rule(Transform parent, string name, Vector2 size, Vector2 position, Color color)
        {
            var rule = Panel(parent, name, size, position).GetComponent<Image>();
            rule.color = color;
            rule.raycastTarget = false;
        }
        static void Frame(Transform parent, float width, float height)
        {
            parent.GetComponent<Image>().color = new Color(0.025f, 0.031f, 0.037f, 1f);
            var outline = parent.gameObject.AddComponent<Outline>();
            outline.effectColor = Border;
            outline.effectDistance = new Vector2(1f, -1f);
            Rule(parent, "Red header rule", new Vector2(width, 3f), new Vector2(0f, height * 0.5f), Selected);
        }
        static void Caption(Transform parent, string value, Vector2 position, Vector2 size)
        {
            var caption = Text(parent, value, 15f, position, size);
            caption.alignment = TextAlignmentOptions.MidlineLeft;
            caption.characterSpacing = 2f;
            caption.color = Muted;
        }
        static GameObject Panel(Transform parent, string name, Vector2 size, Vector2 position)
        {
            var obj = new GameObject(name, typeof(RectTransform), typeof(Image));
            obj.transform.SetParent(parent, false);
            var rect = obj.GetComponent<RectTransform>();
            rect.sizeDelta = size;
            rect.anchoredPosition = position;
            obj.GetComponent<Image>().color = Surface;
            return obj;
        }
        static TMP_Text Text(Transform parent, string value, float size, Vector2 position, Vector2 dimensions)
        {
            var obj = new GameObject("Label", typeof(RectTransform), typeof(TextMeshProUGUI));
            obj.transform.SetParent(parent, false);
            var text = obj.GetComponent<TextMeshProUGUI>();
            text.rectTransform.anchoredPosition = position;
            text.rectTransform.sizeDelta = dimensions;
            text.text = value;
            text.richText = false;
            // Reuse the menu's existing font asset; no new font/material or scene edits.
            if (menuFont == null)
                foreach (var existing in FindObjectsByType<TMP_Text>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                    if (existing.font != null && existing.font.name == "SpaceMono-Bold SDF") { menuFont = existing.font; break; }
            if (menuFont != null) text.font = menuFont;
            text.fontSize = size;
            text.color = new Color(0.94f, 0.94f, 0.90f, 1f);
            text.alignment = TextAlignmentOptions.Center;
            text.raycastTarget = false;
            return text;
        }
        static Button Button(Transform parent, string title, Vector2 position, Vector2 size, UnityEngine.Events.UnityAction action)
        {
            var obj = Panel(parent, title, size, position);
            var button = obj.AddComponent<Button>();
            button.targetGraphic = obj.GetComponent<Image>();
            var colors = button.colors;
            colors.highlightedColor = new Color(1.22f, 1.22f, 1.22f, 1f);
            colors.selectedColor = colors.highlightedColor;
            colors.pressedColor = new Color(0.72f, 0.72f, 0.72f, 1f);
            colors.disabledColor = new Color(0.4f, 0.4f, 0.4f, 0.65f);
            button.colors = colors;
            button.onClick.AddListener(action);
            Text(obj.transform, title.ToUpperInvariant(), 18f, Vector2.zero, size);
            return button;
        }
    }
}
