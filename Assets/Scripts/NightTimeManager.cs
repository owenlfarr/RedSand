using UnityEngine;
using UnityEngine.SceneManagement;
using Unity.Netcode;

public class NightTimeManager : NetworkBehaviour
{
    public static NightTimeManager Instance { get; private set; }

    [Header("Time Settings")]
    [Tooltip("Real-time seconds per in-game hour")]
    public float secondsPerHour = 90f;
    
    [Tooltip("Starting hour (24-hour format)")]
    public int startHour = 22;
    
    [Tooltip("Ending hour (24-hour format)")]
    public int endHour = 8;

    [Header("Ending Sequence Trigger")]
    [Tooltip("When enabled, the ending sequence fires after this many real-time seconds instead of waiting for the end hour")]
    public bool useCustomEndingTime = false;

    [Tooltip("Real-time seconds from the start of the night before the ending sequence triggers (only used when Use Custom Ending Time is enabled)")]
    [Min(0f)]
    public float endingSequenceTriggerTime = 540f;

    [Header("Scene Settings")]
    [Tooltip("Name of the scene to load when night ends")]
    public string nextSceneName = "WinScene";

    [Header("Difficulty Progression")]
    [Tooltip("How much to reduce zombie wander amount each hour (0-1)")]
    [Range(0f, 0.1f)]
    public float difficultyIncreasePerHour = 0.08f;

    public int CurrentHour { get; private set; }
    public int CurrentMinute { get; private set; }
    public float NightProgress { get; private set; }

    public NetworkVariable<int> CurrentHourNetwork = new NetworkVariable<int>(
        0,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server);

    public NetworkVariable<int> CurrentMinuteNetwork = new NetworkVariable<int>(
        0,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server);

    public NetworkVariable<float> TotalElapsedTimeNetwork = new NetworkVariable<float>(
        0f,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server);

    public NetworkVariable<float> NightProgressNetwork = new NetworkVariable<float>(
        0f,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server);

    public NetworkVariable<bool> EndingTriggeredNetwork = new NetworkVariable<bool>(
        false,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server);

    private float hourTimer;
    private float totalElapsedTime;
    private bool endingTriggered;
    private int totalNightHours;
    private int hoursElapsed;

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
        if (!IsNetworkSessionActive())
        {
            InitializeLocalState();
            NotifyZombiesOfDifficultyChange();
            return;
        }

        SyncLocalStateFromNetwork();
    }

    public override void OnNetworkSpawn()
    {
        if (IsServer)
        {
            InitializeNetworkState();
            NotifyZombiesOfDifficultyChange();
        }

        SyncLocalStateFromNetwork();
    }

    void Update()
    {
        if (!IsNetworkSessionActive())
        {
            UpdateLocalNightProgress();
            return;
        }

        if (!IsServer)
        {
            SyncLocalStateFromNetwork();
            return;
        }

        UpdateNetworkNightProgress();
        PushLocalStateToNetwork();

        if (NetworkGameState.Instance != null)
        {
            NetworkGameState.Instance.InitializeSessionSeed(Random.Range(1, int.MaxValue));
            NetworkGameState.Instance.SetRestartInProgress(false);
        }
    }

    void AdvanceHour()
    {
        hourTimer = 0f;
        CurrentMinute = 0;
        hoursElapsed++;

        CurrentHour++;
        if (CurrentHour >= 24)
        {
            CurrentHour = 0;
        }

        NotifyZombiesOfDifficultyChange();

        if (IsNetworkSessionActive())
        {
            HourAdvancedClientRpc(CurrentHour, CurrentMinute, NightProgress);
        }

        if (!useCustomEndingTime && CurrentHour == endHour)
        {
            EndNight();
        }
    }

    void NotifyZombiesOfDifficultyChange()
    {
        float newDifficulty = difficultyIncreasePerHour * hoursElapsed;

        ZombieAI[] zombies = FindObjectsOfType<ZombieAI>();
        
        Debug.Log($"<color=yellow>[Night Manager]</color> Hour {CurrentHour}:00 - Difficulty: {newDifficulty:F2} - Zombies found: {zombies.Length}");
        
        foreach (ZombieAI zombie in zombies)
        {
            zombie.AdjustDifficulty(newDifficulty);
        }
    }

    void EndNight()
    {
        if (IsNetworkSessionActive())
        {
            if (!IsServer)
            {
                return;
            }

            if (EndingTriggeredNetwork.Value)
            {
                return;
            }

            endingTriggered = true;
            EndingTriggeredNetwork.Value = true;
            if (NetworkGameState.Instance != null)
            {
                NetworkGameState.Instance.SetRestartInProgress(true);
            }
            EndNightClientRpc();
            return;
        }

        // If a BunkerEndingSequence is present it owns the scene transition.
        BunkerEndingSequence ending = FindObjectOfType<BunkerEndingSequence>();
        if (ending != null)
        {
            ending.TriggerEnding();
            return;
        }

        // Fallback: direct scene load (legacy behaviour / WinScreen).
        if (!string.IsNullOrEmpty(nextSceneName))
        {
            SceneManager.LoadScene(nextSceneName);
        }
    }

    [ClientRpc]
    private void HourAdvancedClientRpc(int hour, int minute, float progress)
    {
        CurrentHour = hour;
        CurrentMinute = minute;
        NightProgress = progress;
        Debug.Log($"<color=yellow>[Night Manager]</color> Hour advanced to {hour}:{minute:00} on client.");
    }

    [ClientRpc]
    private void EndNightClientRpc()
    {
        endingTriggered = true;

        if (NetworkGameState.Instance != null)
        {
            NetworkGameState.Instance.SetRestartInProgress(true);
        }

        BunkerEndingSequence ending = FindObjectOfType<BunkerEndingSequence>();
        if (ending != null)
        {
            ending.TriggerEnding();
        }
    }

    int CalculateTotalHours()
    {
        if (endHour > startHour)
        {
            return endHour - startHour;
        }
        else
        {
            return (24 - startHour) + endHour;
        }
    }

    public string GetFormattedTime()
    {
        int displayHour = CurrentHour;
        string period = "AM";

        if (displayHour == 0)
        {
            displayHour = 12;
        }
        else if (displayHour == 12)
        {
            period = "PM";
        }
        else if (displayHour > 12)
        {
            displayHour -= 12;
            period = "PM";
        }

        return string.Format("{0}:{1:00} {2}", displayHour, CurrentMinute, period);
    }

    private void InitializeLocalState()
    {
        CurrentHour = startHour;
        CurrentMinute = 0;
        hourTimer = 0f;
        totalElapsedTime = 0f;
        endingTriggered = false;
        totalNightHours = CalculateTotalHours();
        hoursElapsed = 0;
        NightProgress = 0f;
    }

    private void InitializeNetworkState()
    {
        CurrentHour = startHour;
        CurrentMinute = 0;
        hourTimer = 0f;
        totalElapsedTime = 0f;
        endingTriggered = false;
        totalNightHours = CalculateTotalHours();
        hoursElapsed = 0;

        PushLocalStateToNetwork();
    }

    private void UpdateLocalNightProgress()
    {
        hourTimer += Time.deltaTime;
        totalElapsedTime += Time.deltaTime;

        float minuteProgress = (hourTimer / secondsPerHour) * 60f;
        CurrentMinute = Mathf.FloorToInt(minuteProgress);

        if (hourTimer >= secondsPerHour)
        {
            AdvanceHour();
        }

        if (useCustomEndingTime && !endingTriggered && totalElapsedTime >= endingSequenceTriggerTime)
        {
            endingTriggered = true;
            EndNight();
        }

        NightProgress = (float)hoursElapsed / totalNightHours;
    }

    private void UpdateNetworkNightProgress()
    {
        hourTimer += Time.deltaTime;
        totalElapsedTime += Time.deltaTime;

        float minuteProgress = (hourTimer / secondsPerHour) * 60f;
        CurrentMinute = Mathf.FloorToInt(minuteProgress);

        if (hourTimer >= secondsPerHour)
        {
            AdvanceHour();
        }

        if (useCustomEndingTime && !endingTriggered && totalElapsedTime >= endingSequenceTriggerTime)
        {
            endingTriggered = true;
            EndNight();
        }

        NightProgress = (float)hoursElapsed / totalNightHours;
        if (NightProgressNetwork.Value != NightProgress)
        {
            NightProgressNetwork.Value = NightProgress;
        }
    }

    private void PushLocalStateToNetwork()
    {
        if (!IsServer)
        {
            return;
        }

        CurrentHourNetwork.Value = CurrentHour;
        CurrentMinuteNetwork.Value = CurrentMinute;
        TotalElapsedTimeNetwork.Value = totalElapsedTime;
        NightProgressNetwork.Value = NightProgress;
        EndingTriggeredNetwork.Value = endingTriggered;
    }

    private void SyncLocalStateFromNetwork()
    {
        if (!IsNetworkSessionActive())
        {
            return;
        }

        CurrentHour = CurrentHourNetwork.Value;
        CurrentMinute = CurrentMinuteNetwork.Value;
        totalElapsedTime = TotalElapsedTimeNetwork.Value;
        NightProgress = NightProgressNetwork.Value;
        endingTriggered = EndingTriggeredNetwork.Value;
    }

    private bool IsNetworkSessionActive()
    {
        return NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening;
    }
}
