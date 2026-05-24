using System.IO;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

/// <summary>
/// Editor utility that extracts looping animation clips from the astronaut FBX files
/// and wires them into the New Animator Controller states.
/// Run via Tools → Setup Player Animations.
/// </summary>
public static class SetupPlayerAnimations
{
    // ── FBX source paths ────────────────────────────────────────────────────
    private const string IdleFbxPath     = "Assets/Standing Idle.fbx";
    private const string WalkFbxPath     = "Assets/Walking (1).fbx";
    private const string WalkBackFbxPath = "Assets/Walking Backwards.fbx";
    private const string SprintFbxPath   = "Assets/Unarmed Run Forward.fbx";

    // ── Output paths ────────────────────────────────────────────────────────
    private const string AnimDir         = "Assets/Animations";
    private const string IdleAnimPath    = "Assets/Animations/Idle.anim";
    private const string WalkAnimPath    = "Assets/Animations/Walk.anim";
    private const string WalkBackAnimPath= "Assets/Animations/WalkBack.anim";
    private const string SprintAnimPath  = "Assets/Animations/Sprint.anim";

    // ── Controller path ─────────────────────────────────────────────────────
    private const string ControllerPath  = "Assets/New Animator Controller.controller";

    [MenuItem("Tools/Setup Player Animations")]
    public static void Run()
    {
        if (!Directory.Exists(AnimDir))
            AssetDatabase.CreateFolder("Assets", "Animations");

        AnimationClip idle     = ExtractClip(IdleFbxPath,     IdleAnimPath);
        AnimationClip walk     = ExtractClip(WalkFbxPath,     WalkAnimPath);
        AnimationClip walkBack = ExtractClip(WalkBackFbxPath, WalkBackAnimPath);
        AnimationClip sprint   = ExtractClip(SprintFbxPath,   SprintAnimPath);

        if (idle == null || walk == null || walkBack == null || sprint == null)
        {
            Debug.LogError("[SetupPlayerAnimations] One or more clips could not be extracted. Aborting controller wiring.");
            return;
        }

        WireController(idle, walk, walkBack, sprint);

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        Debug.Log("[SetupPlayerAnimations] Done — all clips extracted and controller wired.");
    }

    // ── Helpers ─────────────────────────────────────────────────────────────

    /// <summary>
    /// Copies the first AnimationClip found in an FBX into a standalone .anim file
    /// with loop time enabled, then returns the new clip asset.
    /// </summary>
    private static AnimationClip ExtractClip(string fbxPath, string outputPath)
    {
        Object[] assets = AssetDatabase.LoadAllAssetsAtPath(fbxPath);
        AnimationClip source = null;

        foreach (Object obj in assets)
        {
            if (obj is AnimationClip clip && !clip.name.StartsWith("__preview__"))
            {
                source = clip;
                break;
            }
        }

        if (source == null)
        {
            Debug.LogError($"[SetupPlayerAnimations] No AnimationClip found in {fbxPath}");
            return null;
        }

        // Copy the clip so we can mutate it.
        AnimationClip copy = Object.Instantiate(source);
        copy.name = Path.GetFileNameWithoutExtension(outputPath);

        // Enable loop time.
        AnimationClipSettings settings = AnimationUtility.GetAnimationClipSettings(copy);
        settings.loopTime = true;
        AnimationUtility.SetAnimationClipSettings(copy, settings);

        // Save as a standalone asset (overwrite if it already exists).
        AnimationClip existing = AssetDatabase.LoadAssetAtPath<AnimationClip>(outputPath);
        if (existing != null)
        {
            EditorUtility.CopySerialized(copy, existing);
            EditorUtility.SetDirty(existing);
        }
        else
        {
            AssetDatabase.CreateAsset(copy, outputPath);
        }

        return AssetDatabase.LoadAssetAtPath<AnimationClip>(outputPath);
    }

    /// <summary>
    /// Assigns the extracted clips to the matching states in the animator controller.
    /// </summary>
    private static void WireController(
        AnimationClip idle, AnimationClip walk,
        AnimationClip walkBack, AnimationClip sprint)
    {
        AnimatorController controller =
            AssetDatabase.LoadAssetAtPath<AnimatorController>(ControllerPath);

        if (controller == null)
        {
            Debug.LogError($"[SetupPlayerAnimations] Controller not found at {ControllerPath}");
            return;
        }

        foreach (AnimatorControllerLayer layer in controller.layers)
        {
            foreach (ChildAnimatorState childState in layer.stateMachine.states)
            {
                AnimatorState state = childState.state;
                switch (state.name)
                {
                    case "Idle":     state.motion = idle;     break;
                    case "Walk":     state.motion = walk;     break;
                    case "WalkBack": state.motion = walkBack; break;
                    case "Sprint":   state.motion = sprint;   break;
                }
                EditorUtility.SetDirty(state);
            }
        }

        EditorUtility.SetDirty(controller);
    }
}
