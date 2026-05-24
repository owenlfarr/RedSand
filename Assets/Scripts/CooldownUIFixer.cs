using UnityEngine;
using TMPro;

public class CooldownUIFixer : MonoBehaviour
{
    [ContextMenu("Set Cooldown Positions (Radar: -252.7, Defense: -286.8)")]
    void SetCooldownPositions()
    {
        RadarSystem radarSystem = FindObjectOfType<RadarSystem>();
        DefenseSystem defenseSystem = FindObjectOfType<DefenseSystem>();

        if (radarSystem != null && radarSystem.cooldownText != null)
        {
            RectTransform radarRect = radarSystem.cooldownText.GetComponent<RectTransform>();
            if (radarRect != null)
            {
                Vector2 currentPos = radarRect.anchoredPosition;
                radarRect.anchoredPosition = new Vector2(currentPos.x, -252.7f);
                Debug.Log($"Radar cooldown positioned at Y = -252.7");
            }
        }
        else
        {
            if (radarSystem == null) Debug.LogWarning("RadarSystem not found!");
            else Debug.LogWarning("Radar cooldownText not assigned!");
        }

        if (defenseSystem != null && defenseSystem.cooldownText != null)
        {
            RectTransform defenseRect = defenseSystem.cooldownText.GetComponent<RectTransform>();
            if (defenseRect != null)
            {
                Vector2 currentPos = defenseRect.anchoredPosition;
                defenseRect.anchoredPosition = new Vector2(currentPos.x, -286.8f);
                Debug.Log($"Defense cooldown positioned at Y = -286.8");
            }
        }
        else
        {
            if (defenseSystem == null) Debug.LogWarning("DefenseSystem not found!");
            else Debug.LogWarning("Defense cooldownText not assigned!");
        }

        Debug.Log("Cooldown texts positioned correctly!");
    }

    [ContextMenu("Fix Defense Results Text Width")]
    void FixDefenseResultsWidth()
    {
        DefenseSystem defenseSystem = FindObjectOfType<DefenseSystem>();
        
        if (defenseSystem != null && defenseSystem.defenseResultsText != null)
        {
            RectTransform rect = defenseSystem.defenseResultsText.GetComponent<RectTransform>();
            if (rect != null)
            {
                rect.sizeDelta = new Vector2(500, rect.sizeDelta.y);
                defenseSystem.defenseResultsText.enableWordWrapping = true;
                defenseSystem.defenseResultsText.overflowMode = TextOverflowModes.Overflow;
                
                Debug.Log("Fixed defense results text - set width to 500 with word wrapping enabled");
            }
        }
        else
        {
            Debug.LogWarning("DefenseSystem or defenseResultsText not found!");
        }
    }
}
