using UnityEngine;
using UnityEngine.UI;
using Unity.Netcode;
using TMPro;

public class BunkerTablet : MonoBehaviour
{
    public Transform bunkerArrowWaypoint;
    public float scanDuration = 3f, cooldownDuration = 25f, slideDuration = 0.3f;
    PlayerController player;
    OxygenSystem oxygen;
    NetworkObject identity;
    NetworkPlayerMovementState movement;
    Transform tablet;
    RectTransform arrow;
    Material casingMaterial, trimMaterial, glassMaterial;
    TextMeshProUGUI status;
    GameObject view;
    bool held;
    float slide, scanEnds, readyAt, nextSearch;

    void Awake()
    {
        player = GetComponent<PlayerController>();
        oxygen = GetComponent<OxygenSystem>();
        identity = GetComponent<NetworkObject>();
        movement = GetComponent<NetworkPlayerMovementState>();
    }

    void Update()
    {
        bool online = NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening;
        bool local = !online || (identity != null && identity.IsSpawned && identity.IsOwner);
        bool usable = local && player.enabled && (oxygen == null || !oxygen.IsDead()) &&
            (movement == null || movement.MovementEnabled.Value);
        if (!usable)
        {
            Equip(false);
            slide = 0f;
            if (view != null) view.SetActive(false);
            return;
        }
        if (view == null) BuildView();
        view.SetActive(true);
        bool input = Cursor.lockState == CursorLockMode.Locked && Time.timeScale > 0f;
        if (input && Input.GetKeyDown(KeyCode.Alpha1)) Equip(!held);
        slide = Mathf.MoveTowards(slide, held ? 1f : 0f, Time.unscaledDeltaTime / Mathf.Max(0.01f, slideDuration));
        float eased = Mathf.SmoothStep(0f, 1f, slide);
        tablet.localPosition = Vector3.Lerp(new Vector3(0.22f, -0.52f, 0.5f), new Vector3(0.22f, -0.115f, 0.5f), eased);
        tablet.localRotation = Quaternion.Slerp(Quaternion.Euler(28f, 24f, 0f), Quaternion.LookRotation(new Vector3(0.22f, -0.115f, 0.5f), Vector3.up), eased);
        view.SetActive(slide > 0f);
        if (bunkerArrowWaypoint == null && Time.unscaledTime >= nextSearch)
        {
            nextSearch = Time.unscaledTime + 1f;
            foreach (Transform candidate in FindObjectsByType<Transform>(FindObjectsSortMode.None))
            {
                if (candidate.gameObject.scene != gameObject.scene) continue;
                string name = candidate.name.Replace(" ", "").Replace("_", "").ToLowerInvariant();
                if (name == "bunkerarrowwaypoint") { bunkerArrowWaypoint = candidate; break; }
            }
            if (bunkerArrowWaypoint == null)
            {
                GameObject fallback = GameObject.Find("BunkerZone");
                if (fallback != null) bunkerArrowWaypoint = fallback.transform;
            }
        }
        if (input && held && slide >= 0.99f && Input.GetMouseButtonDown(0) && Time.time >= readyAt && bunkerArrowWaypoint != null)
        {
            scanEnds = Time.time + scanDuration;
            readyAt = scanEnds + cooldownDuration;
        }
        bool scanning = Time.time < scanEnds && bunkerArrowWaypoint != null;
        arrow.gameObject.SetActive(scanning);
        if (scanning)
        {
            Vector3 direction = bunkerArrowWaypoint.position - transform.position;
            Transform camera = player.playerCamera != null ? player.playerCamera : transform;
            float bearing = Mathf.Atan2(direction.x, direction.z) * Mathf.Rad2Deg;
            arrow.localRotation = Quaternion.Euler(0f, 0f, -Mathf.DeltaAngle(camera.eulerAngles.y, bearing));
        }
        status.text = scanning ? "BUNKER SIGNAL" : Time.time < readyAt ? "RECHARGING  " + Mathf.CeilToInt(readyAt - Time.time) + "s"
            : bunkerArrowWaypoint == null ? "NO WAYPOINT SIGNAL" : "LEFT CLICK TO SCAN";
    }

    void Equip(bool value)
    {
        if (held == value) return;
        held = value;
        if (oxygen != null) oxygen.SetTabletFlashlightSuppressed(value);
    }

    void BuildView()
    {
        view = new GameObject("Physical Bunker Tablet");
        Transform camera = player.playerCamera != null ? player.playerCamera : transform;
        view.transform.SetParent(camera, false);
        tablet = view.transform;
        casingMaterial = Surface("Tablet graphite casing", new Color(0.055f, 0.065f, 0.075f), 0.25f);
        trimMaterial = Surface("Tablet metal rim", new Color(0.23f, 0.27f, 0.29f), 0.6f);
        glassMaterial = Surface("Tablet screen glass", new Color(0.015f, 0.045f, 0.04f), 0.85f);
        Part("Solid casing", Vector3.zero, new Vector3(0.29f, 0.29f, 0.025f), casingMaterial);
        Part("Raised bezel", new Vector3(0f, 0f, -0.015f), new Vector3(0.274f, 0.274f, 0.009f), trimMaterial);
        Part("Inset screen", new Vector3(0f, 0.005f, -0.021f), new Vector3(0.247f, 0.235f, 0.005f), glassMaterial);
        Part("Bottom grip", new Vector3(0f, -0.13f, -0.021f), new Vector3(0.12f, 0.012f, 0.007f), casingMaterial);
        Part("Side button", new Vector3(0.148f, 0.065f, 0f), new Vector3(0.008f, 0.032f, 0.012f), trimMaterial);
        for (int x = -1; x <= 1; x += 2)
            for (int y = -1; y <= 1; y += 2)
                Part("Bezel fastener", new Vector3(x * 0.129f, y * 0.129f, -0.021f), new Vector3(0.006f, 0.006f, 0.003f), casingMaterial);

        GameObject display = new GameObject("Embedded screen UI", typeof(RectTransform), typeof(Canvas));
        display.transform.SetParent(tablet, false);
        Canvas canvas = display.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.WorldSpace;
        canvas.worldCamera = camera.GetComponent<Camera>();
        RectTransform screen = display.GetComponent<RectTransform>();
        screen.sizeDelta = new Vector2(308f, 292f);
        screen.localScale = Vector3.one * 0.0008f;
        screen.localPosition = new Vector3(0f, 0.005f, -0.024f);
        Label(screen, "BUNKER LOCATOR", new Vector2(0f, 112f), 21f);
        status = Label(screen, "LEFT CLICK TO SCAN", new Vector2(0f, -110f), 17f);
        Label(screen, "1  /  STOW", new Vector2(0f, -137f), 12f);
        arrow = new GameObject("Waypoint arrow", typeof(RectTransform)).GetComponent<RectTransform>();
        arrow.SetParent(screen, false);
        arrow.sizeDelta = new Vector2(120f, 120f);
        RectTransform shaft = Panel("Shaft", arrow, new Vector2(12f, 74f), new Color(0.45f, 1f, 0.72f));
        shaft.anchoredPosition = new Vector2(0f, -8f);
        for (int i = -1; i <= 1; i += 2)
        {
            RectTransform wing = Panel("Arrow head", arrow, new Vector2(12f, 48f), new Color(0.45f, 1f, 0.72f));
            wing.anchoredPosition = new Vector2(i * 14f, 25f);
            wing.localRotation = Quaternion.Euler(0f, 0f, i * 40f);
        }
    }

    static Material Surface(string name, Color color, float smoothness)
    {
        Material material = new Material(Shader.Find("Standard"));
        material.name = name;
        material.color = color;
        material.SetFloat("_Glossiness", smoothness);
        material.EnableKeyword("_EMISSION");
        material.SetColor("_EmissionColor", color * 0.35f);
        return material;
    }

    void Part(string name, Vector3 position, Vector3 size, Material material)
    {
        GameObject part = GameObject.CreatePrimitive(PrimitiveType.Cube);
        part.name = name;
        part.transform.SetParent(tablet, false);
        part.transform.localPosition = position;
        part.transform.localScale = size;
        Collider collider = part.GetComponent<Collider>();
        collider.enabled = false;
        Destroy(collider);
        MeshRenderer renderer = part.GetComponent<MeshRenderer>();
        renderer.sharedMaterial = material;
        renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        renderer.receiveShadows = false;
    }

    static RectTransform Panel(string name, Transform parent, Vector2 size, Color color)
    {
        GameObject obj = new GameObject(name, typeof(RectTransform), typeof(Image));
        obj.transform.SetParent(parent, false);
        RectTransform rect = obj.GetComponent<RectTransform>();
        rect.sizeDelta = size;
        obj.GetComponent<Image>().color = color;
        obj.GetComponent<Image>().raycastTarget = false;
        return rect;
    }

    static TextMeshProUGUI Label(Transform parent, string text, Vector2 position, float size)
    {
        GameObject obj = new GameObject(text, typeof(RectTransform), typeof(TextMeshProUGUI));
        obj.transform.SetParent(parent, false);
        TextMeshProUGUI label = obj.GetComponent<TextMeshProUGUI>();
        label.rectTransform.sizeDelta = new Vector2(300f, 36f);
        label.rectTransform.anchoredPosition = position;
        label.text = text;
        label.fontSize = size;
        label.alignment = TextAlignmentOptions.Center;
        label.color = new Color(0.55f, 1f, 0.76f);
        label.raycastTarget = false;
        return label;
    }

    void OnDisable()
    {
        Equip(false);
        slide = 0f;
        if (view != null) view.SetActive(false);
    }
    void OnDestroy()
    {
        if (view != null) Destroy(view);
        if (casingMaterial != null) Destroy(casingMaterial);
        if (trimMaterial != null) Destroy(trimMaterial);
        if (glassMaterial != null) Destroy(glassMaterial);
    }
}


