using System.Collections.Generic;
using System.Text;
using TMPro;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.UI;

/// <summary>The broadcast wall screen: host-authoritative surface crew stress.</summary>
public sealed class CrewStressMonitor : MonoBehaviour
{
    private readonly List<TMP_Text> displays = new List<TMP_Text>();
    private float nextRefresh;
    private string lastText;

    public static void Install()
    {
        var radio = GameObject.Find("Radio");
        if (radio != null && radio.GetComponent<CrewStressMonitor>() == null)
            radio.AddComponent<CrewStressMonitor>();
    }

    public static int StressLevel(float distance, float chaseRange, bool chased)
    {
        if (chased) return 3;
        float margin = distance - Mathf.Max(0f, chaseRange);
        if (margin <= 20f) return 2;
        return margin <= 30f ? 1 : 0;
    }

    private void Start() => BuildDisplays();

    public void BuildDisplays()
    {
        if (displays.Count > 0) return;
        // Mount readable faces on both sides of the actual panel. A bunker-zone
        // center is not a reliable indication of which side the PA operator faces.
        for (int side = -1; side <= 1; side += 2)
        {
            string name = "Surface Crew Stress Display " + side;
            var existing = transform.Find(name);
            if (existing != null)
            {
                var reading = existing.Find("Reading");
                if (reading != null) displays.Add(reading.GetComponent<TMP_Text>());
                continue;
            }
            Vector3 normal = transform.right * side;
            float depth = Mathf.Abs(transform.lossyScale.x) * .5f;
            foreach (var renderer in GetComponentsInChildren<MeshRenderer>())
            {
                var bounds = renderer.bounds;
                float projected = Mathf.Abs(normal.x) * bounds.extents.x + Mathf.Abs(normal.y) * bounds.extents.y + Mathf.Abs(normal.z) * bounds.extents.z;
                depth = Mathf.Max(depth, Vector3.Dot(bounds.center - transform.position, normal) + projected);
            }
            var obj = new GameObject(name, typeof(RectTransform), typeof(Canvas));
            var rect = obj.GetComponent<RectTransform>();
            rect.position = transform.position + normal * (depth + .025f);
            rect.rotation = Quaternion.LookRotation(-normal, transform.up);
            rect.sizeDelta = new Vector2(1200, 620);
            rect.localScale = new Vector3(Mathf.Abs(transform.lossyScale.z) * .9f / 1200,
                Mathf.Abs(transform.lossyScale.y) * .88f / 620, .001f);
            rect.SetParent(transform, true);
            var canvas = obj.GetComponent<Canvas>(); canvas.renderMode = RenderMode.WorldSpace;
            var background = new GameObject("Background", typeof(RectTransform), typeof(Image));
            background.transform.SetParent(rect, false);
            var bg = background.GetComponent<RectTransform>();
            bg.anchorMin = Vector2.zero; bg.anchorMax = Vector2.one; bg.offsetMin = bg.offsetMax = Vector2.zero;
            background.GetComponent<Image>().color = new Color(.008f, .018f, .022f);
            background.GetComponent<Image>().raycastTarget = false;
            Label(rect, "SURFACE CREW // STRESS MONITOR", new Vector2(0, 230), new Vector2(1080, 65), 38);
            var display = Label(rect, "WAITING FOR CREW TELEMETRY", new Vector2(0, 5), new Vector2(1080, 330), 38);
            display.name = "Reading"; displays.Add(display);
            Label(rect, "1/3 NEARBY   /   2/3 CLOSE   /   3/3 CHASED", new Vector2(0, -245), new Vector2(1080, 50), 24);
        }
    }

    private static TMP_Text Label(Transform parent, string value, Vector2 position, Vector2 size, float fontSize)
    {
        var obj = new GameObject("Label", typeof(RectTransform), typeof(TextMeshProUGUI));
        obj.transform.SetParent(parent, false);
        var text = obj.GetComponent<TextMeshProUGUI>();
        text.font = TMP_Settings.defaultFontAsset;
        text.rectTransform.anchoredPosition = position; text.rectTransform.sizeDelta = size;
        text.text = value; text.fontSize = fontSize; text.alignment = TextAlignmentOptions.Center;
        text.color = new Color(.7f, .92f, .89f); text.raycastTarget = false;
        return text;
    }

    private void Update()
    {
        if (displays.Count == 0) BuildDisplays();
        var manager = NetworkManager.Singleton;
        bool online = manager != null && manager.IsListening;
        var state = NetworkGameState.Instance;
        if (Time.unscaledTime >= nextRefresh && (!online || (manager.IsServer && state != null && state.IsSpawned)))
        {
            nextRefresh = Time.unscaledTime + .2f;
            string reading = BuildReading(manager, online);
            if (online) state.CrewStressDisplay.Value = new FixedString512Bytes(reading);
            else SetText(reading);
        }
        if (online && state != null && state.IsSpawned)
        {
            string reading = state.CrewStressDisplay.Value.ToString();
            if (!string.IsNullOrEmpty(reading)) SetText(reading);
        }
    }

    private void SetText(string text)
    {
        if (lastText == text) return;
        lastText = text; foreach (var display in displays) if (display != null) display.text = text;
    }

    private static string BuildReading(NetworkManager manager, bool online)
    {
        var monsters = FindObjectsByType<ZombieAI>(FindObjectsSortMode.None);
        var players = new List<OxygenSystem>();
        if (online)
        {
            var ids = new List<ulong>(manager.ConnectedClientsIds); ids.Sort();
            foreach (ulong id in ids)
                if (manager.ConnectedClients.TryGetValue(id, out var client) && client.PlayerObject != null &&
                    client.PlayerObject.IsSpawned && client.PlayerObject.TryGetComponent<OxygenSystem>(out var oxygen)) players.Add(oxygen);
        }
        else players.AddRange(FindObjectsByType<OxygenSystem>(FindObjectsSortMode.None));
        var result = new StringBuilder();
        int rows = 0;
        foreach (var player in players)
        {
            if (player.IsDead() || player.IsInsideBunkerZone()) continue;
            int stress = 0;
            foreach (var monster in monsters)
            {
                if (!monster.isActiveAndEnabled || monster.IsNeutralized() || (online && !monster.IsSpawned)) continue;
                stress = Mathf.Max(stress, StressLevel(Vector3.Distance(player.transform.position, monster.transform.position),
                    monster.StressChaseRange, monster.IsChasingPlayer(player.transform)));
            }
            string name = online ? manager.GetComponent<Networking.RelayPartyManager>()?.GetPlayerDisplayName(player.OwnerClientId)
                ?? "Player " + (player.OwnerClientId + 1) : player.name;
            // Plain player names cannot inject TMP markup or overflow the network snapshot.
            name = name.Replace("<", "").Replace(">", "").Replace("\n", " ").Replace("\r", " ");
            if (name.Length > 24) name = name.Substring(0, 24);
            if (result.Length > 0) result.Append("\n\n");
            string color = stress == 3 ? "FF5555" : stress == 2 ? "FFAA44" : stress == 1 ? "FFE28A" : "8AE0B1";
            result.Append(name).Append("  <color=#").Append(color).Append('>')
                .Append(stress == 0 ? "CALM 0/3" : "STRESSED " + stress + "/3").Append("</color>");
            if (++rows == Networking.RelayPartyManager.LobbyPlayerLimit) break; // Four rows fit the screen and UTF-8 snapshot.
        }
        return result.Length == 0 ? "NO LIVING CREW OUTSIDE\nNO SURFACE SIGNALS" : result.ToString();
    }
}
