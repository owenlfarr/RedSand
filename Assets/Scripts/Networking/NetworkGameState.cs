using UnityEngine;
using Unity.Netcode;

[RequireComponent(typeof(NetworkObject))]
public class NetworkGameState : NetworkBehaviour
{
    public static NetworkGameState Instance { get; private set; }

    public NetworkVariable<int> SessionSeedNetwork = new NetworkVariable<int>(
        0,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server);

    public NetworkVariable<int> ConnectedPlayerCountNetwork = new NetworkVariable<int>(
        0,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server);

    public NetworkVariable<int> AlivePlayerCountNetwork = new NetworkVariable<int>(
        0,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server);

    public NetworkVariable<bool> IsPowerOutNetwork = new NetworkVariable<bool>(
        false,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server);

    public NetworkVariable<bool> IsSolarFlareActiveNetwork = new NetworkVariable<bool>(
        false,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server);

    public NetworkVariable<bool> RadarAvailableNetwork = new NetworkVariable<bool>(
        true,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server);

    public NetworkVariable<bool> DefenseAvailableNetwork = new NetworkVariable<bool>(
        true,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server);

    public NetworkVariable<bool> RestartInProgressNetwork = new NetworkVariable<bool>(
        false,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server);

    public NetworkVariable<Vector3> CompassPointerTargetOverrideNetwork = new NetworkVariable<Vector3>(
        Vector3.zero,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server);

    private void Awake()
    {
        if (Instance == null)
        {
            Instance = this;
        }
    }

    private void OnDestroy()
    {
        if (Instance == this)
        {
            Instance = null;
        }
    }

    public override void OnNetworkSpawn()
    {
        if (Instance == null)
        {
            Instance = this;
        }

        if (IsServer)
        {
            if (SessionSeedNetwork.Value == 0)
            {
                SessionSeedNetwork.Value = Random.Range(1, int.MaxValue);
            }

            RefreshPlayerCounts();
        }

        RegisterCallbacks(true);
    }

    public override void OnNetworkDespawn()
    {
        RegisterCallbacks(false);

        if (Instance == this)
        {
            Instance = null;
        }
    }

    private void OnDisable()
    {
        RegisterCallbacks(false);
    }

    public void InitializeSessionSeed(int seed)
    {
        if (!IsServer)
        {
            return;
        }

        SessionSeedNetwork.Value = seed;
    }

    public void SetPowerOut(bool value)
    {
        if (!IsServer)
        {
            return;
        }

        IsPowerOutNetwork.Value = value;
    }

    public void SetSolarFlareActive(bool value)
    {
        if (!IsServer)
        {
            return;
        }

        IsSolarFlareActiveNetwork.Value = value;
    }

    public void SetRadarAvailable(bool value)
    {
        if (!IsServer)
        {
            return;
        }

        RadarAvailableNetwork.Value = value;
    }

    public void SetDefenseAvailable(bool value)
    {
        if (!IsServer)
        {
            return;
        }

        DefenseAvailableNetwork.Value = value;
    }

    public void SetRestartInProgress(bool value)
    {
        if (!IsServer)
        {
            return;
        }

        RestartInProgressNetwork.Value = value;
    }

    public void SetCompassPointerTargetOverride(Vector3 value)
    {
        if (!IsServer)
        {
            return;
        }

        CompassPointerTargetOverrideNetwork.Value = value;
    }

    public void RefreshPlayerCounts()
    {
        if (!IsServer || NetworkManager.Singleton == null)
        {
            return;
        }

        int connectedCount = NetworkManager.Singleton.ConnectedClientsIds.Count;
        int aliveCount = 0;

        foreach (var client in NetworkManager.Singleton.ConnectedClientsList)
        {
            if (client.PlayerObject != null)
            {
                aliveCount++;
            }
        }

        ConnectedPlayerCountNetwork.Value = connectedCount;
        AlivePlayerCountNetwork.Value = aliveCount;
    }

    private void RegisterCallbacks(bool register)
    {
        if (NetworkManager.Singleton == null)
        {
            return;
        }

        if (register)
        {
            NetworkManager.Singleton.OnClientConnectedCallback += HandleClientConnected;
            NetworkManager.Singleton.OnClientDisconnectCallback += HandleClientDisconnected;
        }
        else
        {
            NetworkManager.Singleton.OnClientConnectedCallback -= HandleClientConnected;
            NetworkManager.Singleton.OnClientDisconnectCallback -= HandleClientDisconnected;
        }
    }

    private void HandleClientConnected(ulong clientId)
    {
        if (IsServer)
        {
            RefreshPlayerCounts();
        }
    }

    private void HandleClientDisconnected(ulong clientId)
    {
        if (IsServer)
        {
            RefreshPlayerCounts();
        }
    }
}
