using UnityEngine;
using UnityEngine.UI;
using TMPro;
using UnityEngine.SceneManagement;
using System.Collections;
using Unity.Netcode;

public class OxygenSystem : NetworkBehaviour
{
    [Header("Oxygen Settings")]
    [Tooltip("Maximum oxygen percentage")]
    public float maxOxygen = 100f;
    
    [Tooltip("Time to fully deplete oxygen on surface (180 seconds = 3 minutes)")]
    public float surfaceDepletionTime = 180f;
    
    [Tooltip("Time to fully recharge oxygen in bunker (60 seconds)")]
    public float bunkerRechargeTime = 60f;

    [Header("Zone Detection")]
    [Tooltip("Tag for underground bunker zones")]
    public string bunkerZoneTag = "Bunker";
    
    [Tooltip("Distance to check for bunker zone overlap")]
    public float bunkerCheckRadius = 1f;

    [Header("UI References")]
    public TextMeshProUGUI oxygenText;
    public Image oxygenBarFill;
    public GameObject deathScreen;

    [Header("Audio (Optional)")]
    public AudioClip lowOxygenWarning;
    public AudioClip deathSound;
    public AudioClip enterSurfaceSound;

    public NetworkVariable<bool> IsFlashlightEnabledNetwork = new NetworkVariable<bool>(
        false,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server);

    private float currentOxygen;
    private NetworkOxygenState oxygenState;
    private bool isInBunker = false;
    private bool isDead = false;
    private AudioSource audioSource;
    private bool hasPlayedWarning = false;
    private int surfaceEntryCount = 0;
    private bool wasInBunkerLastFrame = false;

    private float drainRate;
    private float rechargeRate;
    private Collider[] bunkerCheckResults = new Collider[10];
    private Light playerFlashlight;

    void Start()
    {
        currentOxygen = maxOxygen;
        
        drainRate = maxOxygen / surfaceDepletionTime;
        rechargeRate = maxOxygen / bunkerRechargeTime;

        oxygenState = GetComponent<NetworkOxygenState>();
        if (oxygenState == null)
        {
            oxygenState = gameObject.AddComponent<NetworkOxygenState>();
        }

        if (IsServer)
        {
            oxygenState.Initialize(maxOxygen);
        }

        audioSource = GetComponent<AudioSource>();
        if (audioSource == null)
        {
            audioSource = gameObject.AddComponent<AudioSource>();
        }
        
        audioSource.playOnAwake = false;
        audioSource.spatialBlend = 0f;

        EnsureLocalUIReferences();

        if (deathScreen != null)
        {
            deathScreen.SetActive(false);
        }

        if (oxygenBarFill != null)
        {
            oxygenBarFill.type = Image.Type.Filled;
            oxygenBarFill.fillMethod = Image.FillMethod.Horizontal;
            oxygenBarFill.fillOrigin = (int)Image.OriginHorizontal.Left;
            oxygenBarFill.fillAmount = 1f;
            
            if (oxygenBarFill.sprite == null)
            {
                oxygenBarFill.sprite = Resources.Load<Sprite>("UI/Skin/UISprite");
                if (oxygenBarFill.sprite == null)
                {
                    Texture2D tex = new Texture2D(1, 1);
                    tex.SetPixel(0, 0, Color.white);
                    tex.Apply();
                    oxygenBarFill.sprite = Sprite.Create(tex, new Rect(0, 0, 1, 1), new Vector2(0.5f, 0.5f));
                }
            }
            
            Debug.Log($"<color=green>[Oxygen]</color> Bar initialized - Fill Amount: {oxygenBarFill.fillAmount}");
        }
        else
        {
            Debug.LogWarning("<color=red>[Oxygen]</color> Oxygen bar fill Image is not assigned!");
        }

        UpdateOxygenUI();
    }

    public override void OnNetworkSpawn()
    {
        IsFlashlightEnabledNetwork.OnValueChanged += OnFlashlightEnabledNetworkChanged;
        EnsureFlashlightReference();
        ApplyFlashlightState(IsFlashlightEnabledNetwork.Value);
    }

    public override void OnNetworkDespawn()
    {
        IsFlashlightEnabledNetwork.OnValueChanged -= OnFlashlightEnabledNetworkChanged;
    }

    void Update()
    {
        if (!IsNetworkSessionActive())
        {
            if (isDead) return;

            EnsureLocalUIReferences();
            CheckBunkerZoneLocal();
            UpdateOxygenLocal();
            UpdateOxygenUI();
            CheckLowOxygenWarning();

            if (currentOxygen <= 0)
            {
                DieLocal();
            }

            return;
        }

        if (oxygenState != null && oxygenState.IsDead.Value) return;

        if (IsOwner)
        {
            CheckBunkerZone();
        }

        if (IsServer)
        {
            UpdateOxygen();

            if (oxygenState != null && oxygenState.CurrentOxygen.Value <= 0f)
            {
                Die();
            }
        }

        if (IsOwner)
        {
            EnsureLocalUIReferences();
            UpdateOxygenUI();
            CheckLowOxygenWarning();
        }
    }

    void CheckBunkerZone()
    {
        if (!IsOwner || oxygenState == null)
        {
            return;
        }

        bool currentlyInBunker = false;
        
        int hitCount = Physics.OverlapSphereNonAlloc(transform.position, bunkerCheckRadius, bunkerCheckResults);
        
        for (int i = 0; i < hitCount; i++)
        {
            if (bunkerCheckResults[i].CompareTag(bunkerZoneTag))
            {
                currentlyInBunker = true;
                break;
            }
        }
        
        if (currentlyInBunker && !wasInBunkerLastFrame)
        {
            wasInBunkerLastFrame = true;
            oxygenState.SetInBunkerServerRpc(true);
            Debug.Log("<color=cyan>[Oxygen]</color> Entered bunker - oxygen recharging");
        }
        else if (!currentlyInBunker && wasInBunkerLastFrame)
        {
            wasInBunkerLastFrame = false;
            oxygenState.SetInBunkerServerRpc(false);
            Debug.Log("<color=orange>[Oxygen]</color> Left bunker - oxygen draining");
            
            surfaceEntryCount++;
            if (surfaceEntryCount == 1 && enterSurfaceSound != null && audioSource != null)
            {
                StartCoroutine(PlaySurfaceSoundTwice());
            }
        }
    }

    void CheckBunkerZoneLocal()
    {
        bool currentlyInBunker = false;
        
        int hitCount = Physics.OverlapSphereNonAlloc(transform.position, bunkerCheckRadius, bunkerCheckResults);
        
        for (int i = 0; i < hitCount; i++)
        {
            if (bunkerCheckResults[i].CompareTag(bunkerZoneTag))
            {
                currentlyInBunker = true;
                break;
            }
        }
        
        if (currentlyInBunker && !wasInBunkerLastFrame)
        {
            isInBunker = true;
            wasInBunkerLastFrame = true;
            Debug.Log("<color=cyan>[Oxygen]</color> Entered bunker - oxygen recharging");
        }
        else if (!currentlyInBunker && wasInBunkerLastFrame)
        {
            isInBunker = false;
            wasInBunkerLastFrame = false;
            Debug.Log("<color=orange>[Oxygen]</color> Left bunker - oxygen draining");
            
            surfaceEntryCount++;
            if (surfaceEntryCount == 1 && enterSurfaceSound != null && audioSource != null)
            {
                StartCoroutine(PlaySurfaceSoundTwice());
            }
        }
        else
        {
            isInBunker = currentlyInBunker;
        }
    }

    void UpdateOxygen()
    {
        if (!IsServer || oxygenState == null)
        {
            return;
        }

        bool shouldRecharge = oxygenState.IsInBunker.Value && (PowerSystem.Instance == null || !PowerSystem.Instance.IsPowerOut);

        if (shouldRecharge)
        {
            oxygenState.CurrentOxygen.Value += rechargeRate * Time.deltaTime;
            oxygenState.CurrentOxygen.Value = Mathf.Min(oxygenState.CurrentOxygen.Value, maxOxygen);
            hasPlayedWarning = false;
        }
        else
        {
            oxygenState.CurrentOxygen.Value -= drainRate * Time.deltaTime;
            oxygenState.CurrentOxygen.Value = Mathf.Max(oxygenState.CurrentOxygen.Value, 0);
        }
    }

    void UpdateOxygenLocal()
    {
        bool shouldRecharge = isInBunker && (PowerSystem.Instance == null || !PowerSystem.Instance.IsPowerOut);

        if (shouldRecharge)
        {
            currentOxygen += rechargeRate * Time.deltaTime;
            currentOxygen = Mathf.Min(currentOxygen, maxOxygen);
            hasPlayedWarning = false;
        }
        else
        {
            currentOxygen -= drainRate * Time.deltaTime;
            currentOxygen = Mathf.Max(currentOxygen, 0);
        }
    }

    void UpdateOxygenUI()
    {
        float oxygenValue = GetCurrentOxygenValue();
        float oxygenPercent = (oxygenValue / maxOxygen) * 100f;

        if (oxygenText != null)
        {
            oxygenText.text = $"O2: {oxygenPercent:F1}%";

            if (oxygenPercent <= 25f)
            {
                oxygenText.color = Color.red;
            }
            else if (oxygenPercent <= 50f)
            {
                oxygenText.color = Color.yellow;
            }
            else
            {
                oxygenText.color = Color.white;
            }
        }

        if (oxygenBarFill != null)
        {
            float fillValue = oxygenValue / maxOxygen;
            oxygenBarFill.fillAmount = fillValue;

            if (oxygenPercent <= 25f)
            {
                oxygenBarFill.color = Color.red;
            }
            else if (oxygenPercent <= 50f)
            {
                oxygenBarFill.color = Color.yellow;
            }
            else
            {
                oxygenBarFill.color = Color.green;
            }
        }
    }

    void CheckLowOxygenWarning()
    {
        float oxygenPercent = (GetCurrentOxygenValue() / maxOxygen) * 100f;

        if (oxygenPercent <= 25f && !hasPlayedWarning)
        {
            if (audioSource != null && lowOxygenWarning != null)
            {
                audioSource.PlayOneShot(lowOxygenWarning);
                hasPlayedWarning = true;
            }
        }
    }

    void Die()
    {
        if (!IsServer || oxygenState == null || oxygenState.IsDead.Value)
        {
            return;
        }

        oxygenState.IsDead.Value = true;
        ClientRpcParams clientRpcParams = new ClientRpcParams
        {
            Send = new ClientRpcSendParams
            {
                TargetClientIds = new[] { OwnerClientId }
            }
        };
        PlayOxygenDeathClientRpc(clientRpcParams);

        Debug.Log("Player died from oxygen deprivation!");
    }

    void DieLocal()
    {
        if (isDead) return;

        isDead = true;

        if (audioSource != null && deathSound != null)
        {
            audioSource.PlayOneShot(deathSound);
        }

        Time.timeScale = 0f;

        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;

        if (deathScreen != null)
        {
            StartCoroutine(ShowDeathSequence());
        }

        Debug.Log("Player died from oxygen deprivation!");
    }

    [ClientRpc]
    private void PlayOxygenDeathClientRpc(ClientRpcParams clientRpcParams = default)
    {
        if (audioSource != null && deathSound != null)
        {
            audioSource.PlayOneShot(deathSound);
        }

        Time.timeScale = 0f;

        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;

        if (deathScreen != null)
        {
            StartCoroutine(ShowDeathSequence());
        }
    }

    IEnumerator ShowDeathSequence()
    {
        deathScreen.SetActive(true);
        
        Transform deathTextTransform = deathScreen.transform.Find("DeathText");
        if (deathTextTransform != null)
        {
            deathTextTransform.gameObject.SetActive(false);
        }

        yield return new WaitForSecondsRealtime(6f);

        if (deathTextTransform != null)
        {
            deathTextTransform.gameObject.SetActive(true);
        }
    }

    IEnumerator PlaySurfaceSoundTwice()
    {
        if (enterSurfaceSound == null)
        {
            Debug.LogWarning("<color=red>[Oxygen]</color> Enter Surface Sound is not assigned!");
            yield break;
        }

        Debug.Log("<color=cyan>[Oxygen]</color> Playing surface entry sound (first time - looping twice)");
        
        audioSource.PlayOneShot(enterSurfaceSound);
        
        yield return new WaitForSeconds(enterSurfaceSound.length);
        
        audioSource.PlayOneShot(enterSurfaceSound);
        
        Debug.Log("<color=cyan>[Oxygen]</color> Surface entry sound completed");
    }

    public void RestartLevel()
    {
        Time.timeScale = 1f;

        if (IsServer && NetworkManager.Singleton != null)
        {
            NetworkManager.Singleton.SceneManager.LoadScene(SceneManager.GetActiveScene().name, LoadSceneMode.Single);
            return;
        }

        Debug.Log("<color=orange>[Oxygen]</color> RestartLevel requested on a client; waiting for server authority.");
    }

    public float GetOxygenPercent()
    {
        return (GetCurrentOxygenValue() / maxOxygen) * 100f;
    }

    public bool IsInBunker()
    {
        return GetIsInBunkerValue();
    }

    public bool IsDead()
    {
        if (IsNetworkSessionActive() && oxygenState != null)
        {
            return oxygenState.IsDead.Value;
        }

        return isDead;
    }

    public void MarkDeadFromMonster()
    {
        if (IsNetworkSessionActive())
        {
            if (IsServer && oxygenState != null)
            {
                oxygenState.IsDead.Value = true;
            }

            return;
        }

        isDead = true;
    }

    public void SetFlashlightEnabled(bool enabledState)
    {
        ApplyFlashlightState(enabledState);

        if (!IsNetworkSessionActive())
        {
            return;
        }

        if (IsServer)
        {
            IsFlashlightEnabledNetwork.Value = enabledState;
        }
        else
        {
            SetFlashlightEnabledServerRpc(enabledState);
        }
    }

    private void EnsureLocalUIReferences()
    {
        if (IsNetworkSessionActive() && !IsOwner)
        {
            return;
        }

        if (oxygenText == null)
        {
            GameObject oxygenTextObject = GameObject.Find("OxygenText");
            if (oxygenTextObject != null)
            {
                oxygenText = oxygenTextObject.GetComponent<TextMeshProUGUI>();
            }
        }

        if (oxygenBarFill == null)
        {
            GameObject oxygenBarObject = GameObject.Find("OxygenBarContainer");
            if (oxygenBarObject != null)
            {
                Transform fillTransform = oxygenBarObject.transform.Find("Fill");
                if (fillTransform != null)
                {
                    oxygenBarFill = fillTransform.GetComponent<Image>();
                }
            }

            if (oxygenBarFill == null)
            {
                GameObject fillObject = GameObject.Find("Fill");
                if (fillObject != null)
                {
                    oxygenBarFill = fillObject.GetComponent<Image>();
                }
            }
        }

        if (deathScreen == null)
        {
            deathScreen = GameObject.Find("DeathScreen");
        }
    }

    private void EnsureFlashlightReference()
    {
        if (playerFlashlight != null)
        {
            return;
        }

        Light[] playerLights = GetComponentsInChildren<Light>(true);
        foreach (Light playerLight in playerLights)
        {
            if (playerLight != null && playerLight.type == LightType.Spot)
            {
                playerFlashlight = playerLight;
                return;
            }
        }

        if (playerLights.Length > 0)
        {
            playerFlashlight = playerLights[0];
        }
    }

    private void ApplyFlashlightState(bool enabledState)
    {
        EnsureFlashlightReference();
        if (playerFlashlight == null)
        {
            return;
        }

        playerFlashlight.gameObject.SetActive(true);
        playerFlashlight.enabled = enabledState;
    }

    private void OnFlashlightEnabledNetworkChanged(bool previousValue, bool currentValue)
    {
        ApplyFlashlightState(currentValue);
    }

    [ServerRpc(RequireOwnership = false)]
    private void SetFlashlightEnabledServerRpc(bool enabledState, ServerRpcParams serverRpcParams = default)
    {
        if (!IsServer)
        {
            return;
        }

        if (OwnerClientId != serverRpcParams.Receive.SenderClientId)
        {
            return;
        }

        IsFlashlightEnabledNetwork.Value = enabledState;
    }

    private float GetCurrentOxygenValue()
    {
        if (IsNetworkSessionActive() && oxygenState != null)
        {
            return oxygenState.CurrentOxygen.Value;
        }

        return currentOxygen;
    }

    private bool GetIsInBunkerValue()
    {
        if (IsNetworkSessionActive() && oxygenState != null)
        {
            return oxygenState.IsInBunker.Value;
        }

        return isInBunker;
    }

    private bool IsNetworkSessionActive()
    {
        return NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening;
    }
}
