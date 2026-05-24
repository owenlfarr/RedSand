using UnityEngine;
using UnityEngine.UI;
using TMPro;

#if UNITY_EDITOR
using UnityEditor;
#endif

public class CompassSetup : MonoBehaviour
{
    #if UNITY_EDITOR
    [MenuItem("GameObject/UI/Setup Compass (Powerhouse Pointer)", false, 10)]
    static void SetupCompass()
    {
        Canvas canvas = GameObject.Find("GameUICanvas")?.GetComponent<Canvas>();
        if (canvas == null)
        {
            canvas = FindObjectOfType<Canvas>();
        }
        if (canvas == null)
        {
            Debug.LogError("<color=red>[Compass]</color> No Canvas found! Please create a Canvas first.");
            return;
        }

        GameObject compassObj = new GameObject("CompassPointer");
        compassObj.transform.SetParent(canvas.transform, false);

        RectTransform compassRect = compassObj.AddComponent<RectTransform>();
        compassRect.anchorMin = new Vector2(0.5f, 1f);
        compassRect.anchorMax = new Vector2(0.5f, 1f);
        compassRect.pivot = new Vector2(0.5f, 1f);
        compassRect.anchoredPosition = new Vector2(0, -70);
        compassRect.sizeDelta = new Vector2(60, 60);

        CanvasGroup canvasGroup = compassObj.AddComponent<CanvasGroup>();

        GameObject needleObj = new GameObject("Needle");
        needleObj.transform.SetParent(compassObj.transform, false);
        RectTransform needleRect = needleObj.AddComponent<RectTransform>();
        needleRect.anchorMin = new Vector2(0.5f, 0.5f);
        needleRect.anchorMax = new Vector2(0.5f, 0.5f);
        needleRect.pivot = new Vector2(0.5f, 0.5f);
        needleRect.sizeDelta = new Vector2(4, 28);
        needleRect.anchoredPosition = Vector2.zero;
        Image needleImage = needleObj.AddComponent<Image>();
        needleImage.color = new Color(1f, 1f, 1f, 0.9f);

        GameObject labelObj = new GameObject("Label");
        labelObj.transform.SetParent(compassObj.transform, false);
        RectTransform labelRect = labelObj.AddComponent<RectTransform>();
        labelRect.anchorMin = new Vector2(0.5f, 0f);
        labelRect.anchorMax = new Vector2(0.5f, 0f);
        labelRect.pivot = new Vector2(0.5f, 1f);
        labelRect.sizeDelta = new Vector2(80, 20);
        labelRect.anchoredPosition = new Vector2(0, -8);
        TextMeshProUGUI labelText = labelObj.AddComponent<TextMeshProUGUI>();
        labelText.text = "POWER";
        labelText.fontSize = 16;
        labelText.fontStyle = FontStyles.Bold;
        labelText.alignment = TextAlignmentOptions.Center;
        labelText.color = new Color(1f, 1f, 1f, 0.9f);

        CompassPointer compassPointer = compassObj.AddComponent<CompassPointer>();
        compassPointer.compassNeedle = needleRect;
        compassPointer.compassCanvasGroup = canvasGroup;
        compassPointer.compassLabel = labelText;

        GameObject powerbox = GameObject.Find("Powerbox");
        if (powerbox != null)
        {
            compassPointer.targetObject = powerbox.transform;
        }

        GameObject player = GameObject.FindGameObjectWithTag("Player");
        if (player != null)
        {
            compassPointer.playerTransform = player.transform;
        }

        Selection.activeGameObject = compassObj;
        Debug.Log("<color=green>[Compass]</color> Minimal compass created! Positioned below oxygen bar.");
    }
    #endif
}
