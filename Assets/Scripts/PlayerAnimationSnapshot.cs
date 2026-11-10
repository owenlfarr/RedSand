using Unity.Netcode;
using UnityEngine;

// One owner-written snapshot keeps direction, gait and jump timestamps together.
public struct PlayerAnimationSnapshot : INetworkSerializable, System.IEquatable<PlayerAnimationSnapshot>
{
    public Vector2 Move;
    public bool Sprint,Airborne,Takeoff,Suppressed;
    public uint JumpSequence;
    public double JumpedAt,LandedAt;
    public void NetworkSerialize<T>(BufferSerializer<T> s) where T:IReaderWriter {s.SerializeValue(ref Move);s.SerializeValue(ref Sprint);s.SerializeValue(ref Airborne);s.SerializeValue(ref Takeoff);s.SerializeValue(ref Suppressed);s.SerializeValue(ref JumpSequence);s.SerializeValue(ref JumpedAt);s.SerializeValue(ref LandedAt);}
    public bool Equals(PlayerAnimationSnapshot o)=>Move==o.Move&&Sprint==o.Sprint&&Airborne==o.Airborne&&Takeoff==o.Takeoff&&Suppressed==o.Suppressed&&JumpSequence==o.JumpSequence&&JumpedAt==o.JumpedAt&&LandedAt==o.LandedAt;
}

// Clip contact markers measured on the downloaded 71-frame jump.
public static class AstronautJumpTiming
{
    public const float Takeoff=.33f,AirHold=.48f,Landing=.60f,RecoveryEnd=.80f;
    public static bool Sample(PlayerAnimationSnapshot state,double now,float length,out float phase){
        length=Mathf.Max(.01f,length);phase=0;
        if(state.Suppressed)return false;
        if(state.Airborne){phase=state.Takeoff?Mathf.Min(AirHold,Takeoff+(float)System.Math.Max(0,now-state.JumpedAt)/length):AirHold;return true;}
        if(state.JumpSequence==0||state.LandedAt<=0)return false;
        phase=Landing+(float)System.Math.Max(0,now-state.LandedAt)/length;
        return phase<RecoveryEnd;
    }
}
