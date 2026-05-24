using UnityEngine;
using UnityEditor;
using UnityEditor.Animations;

/// <summary>
/// Rebuilds the player Animator Controller with correct layer weight, clips, and transitions.
/// Fires automatically after any asset import via AssetPostprocessor (AssetDatabase is guaranteed ready).
/// Can also be triggered manually via Tools > Rebuild Player Animator.
/// </summary>
public class RebuildPlayerAnimator : AssetPostprocessor
{
    private const string ControllerPath   = "Assets/New Animator Controller.controller";
    private const string AnimationsFolder = "Assets/Animations";

    private const string IdleFbx     = "Assets/Standing Idle.fbx";
    private const string WalkFbx     = "Assets/Walking (1).fbx";
    private const string WalkBackFbx = "Assets/Walking Backwards.fbx";
    private const string SprintFbx   = "Assets/Unarmed Run Forward.fbx";

    // Fires after every import batch — AssetDatabase is fully ready here.
    private static void OnPostprocessAllAssets(
        string[] imported, string[] deleted, string[] moved, string[] movedFrom)
    {
        AnimatorController ctrl =
            AssetDatabase.LoadAssetAtPath<AnimatorController>(ControllerPath);

        if (ctrl == null) return;

        // Only rebuild if clips are missing or layer weight is wrong.
        bool needsRebuild = ctrl.layers[0].defaultWeight != 1f;
        if (!needsRebuild)
        {
            foreach (ChildAnimatorState child in ctrl.layers[0].stateMachine.states)
            {
                if (child.state.motion == null) { needsRebuild = true; break; }
            }
        }

        if (!needsRebuild) return;

        Rebuild();
    }

    [MenuItem("Tools/Rebuild Player Animator")]
    public static void Rebuild()
    {
        // ── 1. Extract clips ────────────────────────────────────────────────
        if (!AssetDatabase.IsValidFolder(AnimationsFolder))
            AssetDatabase.CreateFolder("Assets", "Animations");

        AnimationClip idle     = ExtractClip(IdleFbx,     "Idle");
        AnimationClip walk     = ExtractClip(WalkFbx,     "Walk");
        AnimationClip walkBack = ExtractClip(WalkBackFbx, "WalkBack");
        AnimationClip sprint   = ExtractClip(SprintFbx,   "Sprint");

        if (idle == null || walk == null || walkBack == null || sprint == null)
        {
            Debug.LogError("[RebuildPlayerAnimator] Clip extraction failed — aborting.");
            return;
        }

        // ── 2. Delete old controller and create a fresh one ─────────────────
        if (AssetDatabase.LoadAssetAtPath<AnimatorController>(ControllerPath) != null)
            AssetDatabase.DeleteAsset(ControllerPath);

        AnimatorController ctrl =
            AnimatorController.CreateAnimatorControllerAtPath(ControllerPath);

        // ── 3. Parameters ───────────────────────────────────────────────────
        ctrl.AddParameter("velocityZ",   AnimatorControllerParameterType.Float);
        ctrl.AddParameter("isSprinting", AnimatorControllerParameterType.Bool);

        // ── 4. Remove the default empty state Unity creates ──────────────────
        AnimatorStateMachine sm = ctrl.layers[0].stateMachine;
        ChildAnimatorState[] existing = sm.states;
        foreach (ChildAnimatorState c in existing)
            sm.RemoveState(c.state);

        // ── 5. Add states with clips ─────────────────────────────────────────
        AnimatorState stIdle     = sm.AddState("Idle",     new Vector3(250f,  50f, 0f));
        AnimatorState stWalk     = sm.AddState("Walk",     new Vector3(550f,  50f, 0f));
        AnimatorState stWalkBack = sm.AddState("WalkBack", new Vector3(550f, 200f, 0f));
        AnimatorState stSprint   = sm.AddState("Sprint",   new Vector3(550f, -100f, 0f));

        stIdle.motion     = idle;
        stWalk.motion     = walk;
        stWalkBack.motion = walkBack;
        stSprint.motion   = sprint;

        sm.defaultState = stIdle;

        // ── 6. Transitions ───────────────────────────────────────────────────
        MakeTransition(stIdle,     stWalk,     0.15f, Greater("velocityZ",  0.1f));
        MakeTransition(stIdle,     stWalkBack, 0.15f, Less("velocityZ",    -0.1f));
        MakeTransition(stWalk,     stSprint,   0.10f, If("isSprinting"),    Greater("velocityZ", 0.1f));
        MakeTransition(stWalk,     stWalkBack, 0.15f, Less("velocityZ",    -0.1f));
        MakeTransition(stWalk,     stIdle,     0.15f, Less("velocityZ",     0.1f));
        MakeTransition(stWalkBack, stIdle,     0.15f, Greater("velocityZ", -0.1f));
        MakeTransition(stWalkBack, stWalk,     0.15f, Greater("velocityZ",  0.1f));
        MakeTransition(stSprint,   stWalk,     0.15f, IfNot("isSprinting"));
        MakeTransition(stSprint,   stIdle,     0.15f, Less("velocityZ",     0.1f));

        // ── 7. Layer weight (must copy/modify/reassign) ──────────────────────
        AnimatorControllerLayer[] layers = ctrl.layers;
        layers[0].defaultWeight = 1f;
        ctrl.layers = layers;

        EditorUtility.SetDirty(ctrl);
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        Debug.Log("[RebuildPlayerAnimator] Done — layer weight = 1, all clips assigned.");
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static void MakeTransition(
        AnimatorState from, AnimatorState to, float duration,
        params (string param, AnimatorConditionMode mode, float threshold)[] conds)
    {
        AnimatorStateTransition tr = from.AddTransition(to);
        tr.hasExitTime     = false;
        tr.duration        = duration;
        tr.hasFixedDuration = true;
        foreach (var c in conds)
            tr.AddCondition(c.mode, c.threshold, c.param);
    }

    private static (string, AnimatorConditionMode, float) Greater(string p, float t)
        => (p, AnimatorConditionMode.Greater, t);
    private static (string, AnimatorConditionMode, float) Less(string p, float t)
        => (p, AnimatorConditionMode.Less, t);
    private static (string, AnimatorConditionMode, float) If(string p)
        => (p, AnimatorConditionMode.If, 0f);
    private static (string, AnimatorConditionMode, float) IfNot(string p)
        => (p, AnimatorConditionMode.IfNot, 0f);

    private static AnimationClip ExtractClip(string fbxPath, string clipName)
    {
        string outPath = $"{AnimationsFolder}/{clipName}.anim";

        AnimationClip found = AssetDatabase.LoadAssetAtPath<AnimationClip>(outPath);
        if (found != null) return found;

        foreach (Object obj in AssetDatabase.LoadAllAssetsAtPath(fbxPath))
        {
            if (obj is AnimationClip src && !src.name.StartsWith("__preview__"))
            {
                AnimationClip clip = new AnimationClip();
                EditorUtility.CopySerialized(src, clip);
                clip.name = clipName;

                SerializedObject   so = new SerializedObject(clip);
                SerializedProperty s  = so.FindProperty("m_AnimationClipSettings");
                s.FindPropertyRelative("m_LoopTime").boolValue  = true;
                s.FindPropertyRelative("m_LoopBlend").boolValue = true;
                so.ApplyModifiedPropertiesWithoutUndo();

                AssetDatabase.CreateAsset(clip, outPath);
                Debug.Log($"[RebuildPlayerAnimator] Extracted {clipName}.anim");
                return clip;
            }
        }

        Debug.LogError($"[RebuildPlayerAnimator] No clip found in {fbxPath}");
        return null;
    }
}
