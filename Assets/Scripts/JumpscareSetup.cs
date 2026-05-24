using UnityEngine;
using UnityEngine.UI;
using TMPro;
using Unity.Netcode;

#if UNITY_EDITOR
using UnityEditor;
#endif

public class JumpscareSetup : MonoBehaviour
{
    #if UNITY_EDITOR
    [MenuItem("GameObject/UI/Setup Jumpscare System", false, 11)]
    static void SetupJumpscareSystem()
    {
        Canvas canvas = GameObject.Find("GameUICanvas")?.GetComponent<Canvas>();
        if (canvas == null)
        {
            canvas = FindObjectOfType<Canvas>();
        }
        if (canvas == null)
        {
            Debug.LogError("<color=red>[Jumpscare]</color> No Canvas found!");
            return;
        }

        GameObject jumpscareObj = new GameObject("JumpscareSystem");
        jumpscareObj.transform.SetParent(canvas.transform, false);

        if (jumpscareObj.GetComponent<NetworkObject>() == null)
        {
            jumpscareObj.AddComponent<NetworkObject>();
        }

        RectTransform jumpscareRect = jumpscareObj.AddComponent<RectTransform>();
        jumpscareRect.anchorMin = Vector2.zero;
        jumpscareRect.anchorMax = Vector2.one;
        jumpscareRect.offsetMin = Vector2.zero;
        jumpscareRect.offsetMax = Vector2.zero;

        CanvasGroup canvasGroup = jumpscareObj.AddComponent<CanvasGroup>();

        GameObject imageObj = new GameObject("JumpscareImage");
        imageObj.transform.SetParent(jumpscareObj.transform, false);
        RectTransform imageRect = imageObj.AddComponent<RectTransform>();
        imageRect.anchorMin = Vector2.zero;
        imageRect.anchorMax = Vector2.one;
        imageRect.offsetMin = Vector2.zero;
        imageRect.offsetMax = Vector2.zero;
        Image jumpscareImage = imageObj.AddComponent<Image>();
        jumpscareImage.color = Color.black;
        jumpscareImage.raycastTarget = false;

        GameObject textObj = new GameObject("JumpscareText");
        textObj.transform.SetParent(jumpscareObj.transform, false);
        RectTransform textRect = textObj.AddComponent<RectTransform>();
        textRect.anchorMin = new Vector2(0.5f, 0.3f);
        textRect.anchorMax = new Vector2(0.5f, 0.3f);
        textRect.pivot = new Vector2(0.5f, 0.5f);
        textRect.sizeDelta = new Vector2(800, 100);
        TextMeshProUGUI jumpscareText = textObj.AddComponent<TextMeshProUGUI>();
        jumpscareText.text = "YOU DIED";
        jumpscareText.fontSize = 72;
        jumpscareText.fontStyle = FontStyles.Bold;
        jumpscareText.alignment = TextAlignmentOptions.Center;
        jumpscareText.color = new Color(1f, 0.2f, 0.2f, 1f);

        GameObject resetObj = new GameObject("ResetPrompt");
        resetObj.transform.SetParent(jumpscareObj.transform, false);
        RectTransform resetRect = resetObj.AddComponent<RectTransform>();
        resetRect.anchorMin = new Vector2(0.5f, 0.2f);
        resetRect.anchorMax = new Vector2(0.5f, 0.2f);
        resetRect.pivot = new Vector2(0.5f, 0.5f);
        resetRect.sizeDelta = new Vector2(400, 40);
        TextMeshProUGUI resetText = resetObj.AddComponent<TextMeshProUGUI>();
        resetText.text = "Press R to Restart";
        resetText.fontSize = 24;
        resetText.fontStyle = FontStyles.Bold;
        resetText.alignment = TextAlignmentOptions.Center;
        resetText.color = Color.white;

        JumpscareSystem jumpscareSystem = jumpscareObj.AddComponent<JumpscareSystem>();
        jumpscareSystem.jumpscareImage = jumpscareImage;
        jumpscareSystem.jumpscareText = jumpscareText;
        jumpscareSystem.resetPromptText = resetText;

        Selection.activeGameObject = jumpscareObj;
        Debug.Log("<color=green>[Jumpscare]</color> Jumpscare System created! Assign jumpscare sprites in Inspector.");
    }
    #endif
}
