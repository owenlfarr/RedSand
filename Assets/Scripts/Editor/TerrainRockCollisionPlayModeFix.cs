#if UNITY_EDITOR
using UnityEditor;

// Also cover starting Play without a scene reload and scripts reloading mid-test.
[InitializeOnLoad]
public static class TerrainRockCollisionPlayModeFix
{
    static TerrainRockCollisionPlayModeFix()
    {
        EditorApplication.delayCall+=Ensure;
        EditorApplication.playModeStateChanged+=state=>{if(state==PlayModeStateChange.EnteredPlayMode)Ensure();};
    }
    static void Ensure(){if(EditorApplication.isPlaying)TerrainRockCollisions.EnsureLoadedScenes();}
}
#endif
