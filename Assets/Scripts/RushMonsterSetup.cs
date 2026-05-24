using UnityEngine;

public class RushMonsterSetup : MonoBehaviour
{
    [Header("Setup Instructions")]
    [TextArea(3, 10)]
    public string instructions = 
        "This script will automatically set up the Rush Monster system.\n" +
        "1. Add the 'Locker' tag to your Tags list\n" +
        "2. Click the button below in the Inspector\n" +
        "3. Adjust settings in RushMonsterEvent component";

    [ContextMenu("Setup Rush Monster System")]
    public void SetupRushSystem()
    {
        Debug.Log("<color=cyan>[Rush Setup]</color> Starting setup...");

        GameObject locker = GameObject.Find("locker");
        if (locker == null)
        {
            Debug.LogError("<color=red>[Rush Setup]</color> Locker GameObject not found in scene!");
            return;
        }

        LockerInteraction lockerScript = locker.GetComponent<LockerInteraction>();
        if (lockerScript == null)
        {
            lockerScript = locker.AddComponent<LockerInteraction>();
            Debug.Log("<color=green>[Rush Setup]</color> Added LockerInteraction to locker");
        }

        Transform doorTransform = locker.transform.Find("door");
        if (doorTransform != null && lockerScript.lockerDoor == null)
        {
            lockerScript.lockerDoor = doorTransform;
            Debug.Log("<color=green>[Rush Setup]</color> Assigned locker door");
        }

        GameObject rushEventObj = GameObject.Find("RushMonsterSystem");
        if (rushEventObj == null)
        {
            rushEventObj = new GameObject("RushMonsterSystem");
            Debug.Log("<color=green>[Rush Setup]</color> Created RushMonsterSystem GameObject");
        }

        RushMonsterEvent rushEvent = rushEventObj.GetComponent<RushMonsterEvent>();
        if (rushEvent == null)
        {
            rushEvent = rushEventObj.AddComponent<RushMonsterEvent>();
            Debug.Log("<color=green>[Rush Setup]</color> Added RushMonsterEvent component");
        }

        GameObject whiteLightParent = GameObject.Find("Lights White");
        if (whiteLightParent != null && rushEvent.whiteLights == null)
        {
            rushEvent.whiteLights = whiteLightParent.GetComponentsInChildren<Light>();
            Debug.Log($"<color=green>[Rush Setup]</color> Assigned {rushEvent.whiteLights.Length} white lights");
        }

        GameObject redLightParent = GameObject.Find("lights REd");
        if (redLightParent != null && rushEvent.redLights == null)
        {
            rushEvent.redLights = redLightParent.GetComponentsInChildren<Light>();
            Debug.Log($"<color=green>[Rush Setup]</color> Assigned {rushEvent.redLights.Length} red lights");
        }

        RushMonsterUI rushUI = FindObjectOfType<RushMonsterUI>();
        if (rushUI == null)
        {
            GameObject uiObj = new GameObject("RushMonsterUI");
            rushUI = uiObj.AddComponent<RushMonsterUI>();
            Debug.Log("<color=green>[Rush Setup]</color> Created RushMonsterUI");
        }

        Canvas canvas = FindObjectOfType<Canvas>();
        if (canvas != null)
        {
            Transform canvasTransform = canvas.transform;

            GameObject lockerPrompt = canvasTransform.Find("LockerInteractionPrompt")?.gameObject;
            if (lockerPrompt == null)
            {
                lockerPrompt = CreateLockerPrompt(canvasTransform);
                Debug.Log("<color=green>[Rush Setup]</color> Created locker interaction prompt UI");
            }

            if (lockerScript.interactionPromptText == null)
            {
                lockerScript.interactionPromptText = lockerPrompt.GetComponentInChildren<TMPro.TextMeshProUGUI>();
            }
        }

        AddLockerCollider(locker);

        Debug.Log("<color=green>[Rush Setup]</color> Setup complete! Adjust settings in Inspector.");
        Debug.Log("<color=yellow>[Rush Setup]</color> Don't forget to add the 'Locker' tag if it doesn't exist!");
    }

    GameObject CreateLockerPrompt(Transform parent)
    {
        GameObject prompt = new GameObject("LockerInteractionPrompt");
        prompt.transform.SetParent(parent, false);

        RectTransform rect = prompt.AddComponent<RectTransform>();
        rect.anchorMin = new Vector2(0.5f, 0.3f);
        rect.anchorMax = new Vector2(0.5f, 0.3f);
        rect.anchoredPosition = Vector2.zero;
        rect.sizeDelta = new Vector2(400, 80);

        UnityEngine.UI.Image bg = prompt.AddComponent<UnityEngine.UI.Image>();
        bg.color = new Color(0, 0, 0, 0.8f);

        GameObject textObj = new GameObject("Text");
        textObj.transform.SetParent(prompt.transform, false);

        RectTransform textRect = textObj.AddComponent<RectTransform>();
        textRect.anchorMin = Vector2.zero;
        textRect.anchorMax = Vector2.one;
        textRect.anchoredPosition = Vector2.zero;
        textRect.sizeDelta = Vector2.zero;

        TMPro.TextMeshProUGUI text = textObj.AddComponent<TMPro.TextMeshProUGUI>();
        text.text = "Press E to hide in locker";
        text.fontSize = 24;
        text.alignment = TMPro.TextAlignmentOptions.Center;
        text.color = Color.white;

        prompt.SetActive(false);

        return prompt;
    }

    void AddLockerCollider(GameObject locker)
    {
        BoxCollider col = locker.GetComponent<BoxCollider>();
        if (col == null)
        {
            col = locker.AddComponent<BoxCollider>();
            col.isTrigger = false;
            col.size = new Vector3(1f, 2f, 1f);
            col.center = new Vector3(0, 1f, 0);
            Debug.Log("<color=green>[Rush Setup]</color> Added BoxCollider to locker");
        }
    }
}
