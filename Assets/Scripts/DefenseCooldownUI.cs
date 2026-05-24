using UnityEngine;
using TMPro;
using System.Collections.Generic;

public class DefenseCooldownUI : MonoBehaviour
{
    public static DefenseCooldownUI Instance { get; private set; }

    [Header("UI References")]
    public TextMeshProUGUI cooldownDisplayText;
    public RectTransform cooldownContainer;

    [Header("Display Settings")]
    public float lineSpacing = 30f;

    private Dictionary<DefenseSystem, float> activeDefenses = new Dictionary<DefenseSystem, float>();

    void Awake()
    {
        if (Instance == null)
        {
            Instance = this;
        }
        else
        {
            Destroy(gameObject);
            return;
        }
    }

    void Start()
    {
        if (cooldownDisplayText != null)
        {
            cooldownDisplayText.text = "";
        }
    }

    void Update()
    {
        UpdateCooldownDisplay();
    }

    public void RegisterDefense(DefenseSystem defense, string defenseName, float remainingTime)
    {
        if (!activeDefenses.ContainsKey(defense))
        {
            activeDefenses[defense] = remainingTime;
        }
        else
        {
            activeDefenses[defense] = remainingTime;
        }
    }

    public void UnregisterDefense(DefenseSystem defense)
    {
        if (activeDefenses.ContainsKey(defense))
        {
            activeDefenses.Remove(defense);
        }
    }

    void UpdateCooldownDisplay()
    {
        if (cooldownDisplayText == null) return;

        List<DefenseSystem> toRemove = new List<DefenseSystem>();
        List<string> displayLines = new List<string>();

        foreach (var kvp in activeDefenses)
        {
            if (kvp.Key == null)
            {
                toRemove.Add(kvp.Key);
                continue;
            }

            float remaining = kvp.Key.GetRemainingCooldown();
            
            if (remaining <= 0)
            {
                toRemove.Add(kvp.Key);
            }
            else
            {
                string defenseName = kvp.Key.defenseName;
                if (string.IsNullOrEmpty(defenseName))
                {
                    defenseName = "Defense";
                }
                displayLines.Add($"{defenseName}: {remaining:F1}s");
            }
        }

        foreach (var defense in toRemove)
        {
            activeDefenses.Remove(defense);
        }

        if (displayLines.Count > 0)
        {
            cooldownDisplayText.text = string.Join("\n", displayLines);
            cooldownDisplayText.gameObject.SetActive(true);
        }
        else
        {
            cooldownDisplayText.text = "";
            cooldownDisplayText.gameObject.SetActive(false);
        }
    }
}
