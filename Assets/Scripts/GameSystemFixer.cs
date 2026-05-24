using UnityEngine;
using UnityEngine.UI;

public class GameSystemFixer : MonoBehaviour
{
    [ContextMenu("Fix All Game Systems")]
    void FixAllSystems()
    {
        FixBunkerZone();
        MoveOxygenSystemToPlayer();
        AddClockUIComponent();
        FixOxygenBarFill();
        
        Debug.Log("<color=green>[FIXER]</color> All systems fixed!");
    }

    [ContextMenu("1. Fix Bunker Zone (Make it a Trigger)")]
    void FixBunkerZone()
    {
        GameObject bunkerZone = GameObject.FindGameObjectWithTag("Bunker");
        
        if (bunkerZone != null)
        {
            BoxCollider collider = bunkerZone.GetComponent<BoxCollider>();
            if (collider != null)
            {
                collider.isTrigger = true;
                Debug.Log("<color=green>[FIXER]</color> BunkerZone collider set to Trigger!");
            }
            else
            {
                Debug.LogWarning("BunkerZone has no BoxCollider!");
            }
        }
        else
        {
            Debug.LogWarning("No GameObject with 'Bunker' tag found!");
        }
    }

    [ContextMenu("2. Move OxygenSystem to Player")]
    void MoveOxygenSystemToPlayer()
    {
        GameObject player = GameObject.FindGameObjectWithTag("Player");
        OxygenSystem currentOxygenSystem = FindObjectOfType<OxygenSystem>();
        
        if (player == null)
        {
            Debug.LogWarning("No Player found! Make sure your player has the 'Player' tag.");
            return;
        }

        if (currentOxygenSystem == null)
        {
            Debug.LogWarning("No OxygenSystem found in scene!");
            return;
        }

        OxygenSystem playerOxygenSystem = player.GetComponent<OxygenSystem>();
        if (playerOxygenSystem == null)
        {
            playerOxygenSystem = player.AddComponent<OxygenSystem>();
        }

        playerOxygenSystem.maxOxygen = currentOxygenSystem.maxOxygen;
        playerOxygenSystem.surfaceDepletionTime = currentOxygenSystem.surfaceDepletionTime;
        playerOxygenSystem.bunkerRechargeTime = currentOxygenSystem.bunkerRechargeTime;
        playerOxygenSystem.bunkerZoneTag = currentOxygenSystem.bunkerZoneTag;
        playerOxygenSystem.oxygenText = currentOxygenSystem.oxygenText;
        playerOxygenSystem.oxygenBarFill = currentOxygenSystem.oxygenBarFill;
        playerOxygenSystem.deathScreen = currentOxygenSystem.deathScreen;
        playerOxygenSystem.lowOxygenWarning = currentOxygenSystem.lowOxygenWarning;
        playerOxygenSystem.deathSound = currentOxygenSystem.deathSound;

        if (currentOxygenSystem.gameObject != player)
        {
            DestroyImmediate(currentOxygenSystem);
            Debug.Log("<color=green>[FIXER]</color> OxygenSystem moved to Player and old component removed!");
        }
        else
        {
            Debug.Log("<color=green>[FIXER]</color> OxygenSystem already on Player!");
        }
    }

    [ContextMenu("3. Add ClockUI to NightTimeManager")]
    void AddClockUIComponent()
    {
        NightTimeManager nightManager = FindObjectOfType<NightTimeManager>();
        
        if (nightManager == null)
        {
            Debug.LogWarning("NightTimeManager not found!");
            return;
        }

        ClockUI clockUI = nightManager.GetComponent<ClockUI>();
        if (clockUI == null)
        {
            clockUI = nightManager.gameObject.AddComponent<ClockUI>();
            Debug.Log("<color=green>[FIXER]</color> ClockUI component added to NightTimeManager!");
        }
        else
        {
            Debug.Log("ClockUI already exists on NightTimeManager!");
        }

        GameObject canvas = GameObject.Find("GameUICanvas");
        if (canvas != null)
        {
            Transform clockText = canvas.transform.Find("ClockText");
            if (clockText != null)
            {
                clockUI.timeText = clockText.GetComponent<TMPro.TextMeshProUGUI>();
                Debug.Log("<color=green>[FIXER]</color> ClockUI assigned to ClockText!");
            }
        }
    }

    [ContextMenu("4. Fix Oxygen Bar Fill Settings")]
    void FixOxygenBarFill()
    {
        GameObject canvas = GameObject.Find("GameUICanvas");
        if (canvas == null)
        {
            Debug.LogWarning("GameUICanvas not found!");
            return;
        }

        Transform barContainer = canvas.transform.Find("OxygenBarContainer");
        if (barContainer == null)
        {
            Debug.LogWarning("OxygenBarContainer not found!");
            return;
        }

        Transform fill = barContainer.Find("Fill");
        if (fill == null)
        {
            Debug.LogWarning("Fill not found in OxygenBarContainer!");
            return;
        }

        Image fillImage = fill.GetComponent<Image>();
        if (fillImage != null)
        {
            fillImage.type = Image.Type.Filled;
            fillImage.fillMethod = Image.FillMethod.Horizontal;
            fillImage.fillOrigin = (int)Image.OriginHorizontal.Left;
            
            RectTransform fillRect = fill.GetComponent<RectTransform>();
            fillRect.anchorMin = new Vector2(0f, 0f);
            fillRect.anchorMax = new Vector2(1f, 1f);
            fillRect.pivot = new Vector2(0f, 0.5f);
            fillRect.offsetMin = Vector2.zero;
            fillRect.offsetMax = Vector2.zero;
            
            Debug.Log("<color=green>[FIXER]</color> Oxygen bar fill settings fixed!");
        }
    }
}
