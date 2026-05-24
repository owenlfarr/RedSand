using UnityEngine;
using TMPro;

public class ClockUI : MonoBehaviour
{
    [Header("References")]
    [Tooltip("TextMeshPro component to display the time")]
    public TextMeshProUGUI timeText;

    [Header("Visual Settings")]
    public Color normalColor = Color.white;
    public Color lateNightColor = Color.red;
    
    [Tooltip("Hour when color starts transitioning to late night color")]
    public int lateNightStartHour = 5;

    void Update()
    {
        if (NightTimeManager.Instance == null || timeText == null)
            return;

        timeText.text = NightTimeManager.Instance.GetFormattedTime();

        UpdateTimeColor();
    }

    void UpdateTimeColor()
    {
        int currentHour = NightTimeManager.Instance.CurrentHour;

        if (currentHour >= lateNightStartHour && currentHour < NightTimeManager.Instance.endHour)
        {
            float lerpAmount = Mathf.PingPong(Time.time, 1f);
            timeText.color = Color.Lerp(normalColor, lateNightColor, lerpAmount);
        }
        else
        {
            timeText.color = normalColor;
        }
    }
}
