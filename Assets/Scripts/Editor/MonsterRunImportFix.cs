using UnityEditor;
using UnityEngine;

// The imported default take previously played only once. Keep this source clip
// looping for both scene monsters and any future reimport of the model.
public sealed class MonsterRunImportFix : AssetPostprocessor
{
    const string ModelPath = "Assets/Art/Models/Zombie Running.fbx";

    void OnPreprocessAnimation()
    {
        if (assetPath != ModelPath) return;
        Configure((ModelImporter)assetImporter);
    }

    static bool Configure(ModelImporter importer)
    {
        var clips = importer.clipAnimations;
        if (clips.Length == 0) clips = importer.defaultClipAnimations;
        bool changed = false;
        foreach (var clip in clips)
        {
            if (clip.loopTime) continue;
            clip.loopTime = true;
            changed = true;
        }
        if (changed) importer.clipAnimations = clips;
        return changed;
    }

    [InitializeOnLoadMethod]
    static void ScheduleRepair() => EditorApplication.delayCall += Repair;

    [MenuItem("Tools/Monsters/Fix Run Loop")]
    static void Repair()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) return;
        var importer = AssetImporter.GetAtPath(ModelPath) as ModelImporter;
        if (importer != null && Configure(importer)) importer.SaveAndReimport();
    }
}
