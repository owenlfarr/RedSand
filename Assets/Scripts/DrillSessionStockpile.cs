using Unity.Netcode;
using UnityEngine;
using System;

// Lives on this session's persistent NetworkManager, not in static/save-game state.
// Scene drills replicate this team inventory; a fresh host starts at zero.
public sealed class DrillSessionStockpile : MonoBehaviour
{
    DrillSnapshot materials;
    public event Action<DrillSnapshot> Changed;
    public DrillSnapshot Materials
    {
        get => materials;
        internal set { if (materials.Equals(value)) return; materials = value; Changed?.Invoke(value); }
    }
    public bool TrySpendMetal(int amount)
    {
        if(manager==null || !manager.IsServer || amount<=0 || materials.Metal<amount)return false;
        var state=materials;state.Metal-=amount;Materials=state;return true;
    }
    public bool TrySpendPlasma()
    {
        if (manager == null || !manager.IsServer || materials.Plasma < 1) return false;
        var state = materials; state.Plasma--; Materials = state; return true;
    }
    public void RefundPlasma()
    {
        if (manager == null || !manager.IsServer) return;
        var state = materials; if (state.Plasma < int.MaxValue) state.Plasma++; Materials = state;
    }
    NetworkManager manager;
    void Awake()
    {
        manager = GetComponent<NetworkManager>();
        manager.OnServerStopped += Clear;
    }
    void Clear(bool wasHost) { Materials = default; }
    void OnDestroy() { if (manager != null) manager.OnServerStopped -= Clear; }
}
