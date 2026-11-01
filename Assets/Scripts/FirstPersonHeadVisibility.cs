using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using Unity.Netcode;

/// <summary>Camera-specific body mesh; animation and the full third-person model stay intact.</summary>
[RequireComponent(typeof(PlayerController))]
public class FirstPersonHeadVisibility : MonoBehaviour
{
    class BodyView
    {
        public SkinnedMeshRenderer renderer;
        public Mesh fullMesh;
        public Mesh firstPersonMesh;
    }
    readonly List<BodyView> bodies = new List<BodyView>();
    PlayerController player;
    OxygenSystem oxygen;
    NetworkObject identity;
    bool initialized;
    bool usingFirstPersonMesh;

    void Awake()
    {
        player = GetComponent<PlayerController>();
        oxygen = GetComponent<OxygenSystem>();
        identity = GetComponent<NetworkObject>();
    }

    void BuildMeshes()
    {
        initialized = true;
        foreach (SkinnedMeshRenderer renderer in GetComponentsInChildren<SkinnedMeshRenderer>(true))
        {
            Mesh source = renderer.sharedMesh;
            if (source == null) continue;
            if (!source.isReadable)
            {
                Debug.LogWarning("[FirstPersonHeadVisibility] Enable Read/Write on body mesh: " + source.name, renderer);
                continue;
            }
            Transform[] bones = renderer.bones;
            bool[] excludedBones = new bool[bones.Length];
            bool found = false;
            for (int i = 0; i < bones.Length; i++)
            {
                // Include neck-weighted helmet/collar edges that can enter the
                // camera during the forward lean of the sprint animation.
                for (Transform bone = bones[i]; bone != null && bone != transform; bone = bone.parent)
                {
                    string name = bone.name.ToLowerInvariant();
                    if (name.Contains("head") || name.Contains("neck"))
                    {
                        excludedBones[i] = true;
                        found = true;
                        break;
                    }
                }
            }
            if (!found) continue;
            BoneWeight[] weights = source.boneWeights;
            if (weights.Length != source.vertexCount) continue;
            bool[] excludedVertices = new bool[weights.Length];
            for (int i = 0; i < weights.Length; i++)
            {
                BoneWeight weight = weights[i];
                float excludedWeight = Weight(excludedBones, weight.boneIndex0, weight.weight0)
                    + Weight(excludedBones, weight.boneIndex1, weight.weight1)
                    + Weight(excludedBones, weight.boneIndex2, weight.weight2)
                    + Weight(excludedBones, weight.boneIndex3, weight.weight3);
                excludedVertices[i] = excludedWeight > 0.1f;
            }
            Mesh firstPerson = Instantiate(source);
            firstPerson.name = source.name + " (first person body)";
            for (int submesh = 0; submesh < source.subMeshCount; submesh++)
            {
                int[] triangles = source.GetTriangles(submesh);
                List<int> visible = new List<int>(triangles.Length);
                for (int i = 0; i < triangles.Length; i += 3)
                {
                    if (excludedVertices[triangles[i]] || excludedVertices[triangles[i + 1]] || excludedVertices[triangles[i + 2]]) continue;
                    visible.Add(triangles[i]);
                    visible.Add(triangles[i + 1]);
                    visible.Add(triangles[i + 2]);
                }
                firstPerson.SetTriangles(visible, submesh, false);
            }
            firstPerson.bounds = source.bounds;
            bodies.Add(new BodyView { renderer = renderer, fullMesh = source, firstPersonMesh = firstPerson });
        }
    }

    static float Weight(bool[] excluded, int index, float weight)
    {
        return index >= 0 && index < excluded.Length && excluded[index] ? weight : 0f;
    }

    void OnEnable()
    {
        Camera.onPreCull += BeforeCamera;
        Camera.onPostRender += AfterCamera;
        RenderPipelineManager.beginCameraRendering += BeforePipelineCamera;
        RenderPipelineManager.endCameraRendering += AfterPipelineCamera;
    }

    void BeforeCamera(Camera camera)
    {
        RestoreFullMesh();
        if (player == null || !player.enabled || player.playerCamera == null ||
            camera.transform != player.playerCamera || (oxygen != null && oxygen.IsDead())) return;
        bool online = NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening;
        if (online && (identity == null || !identity.IsSpawned || !identity.IsOwner)) return;
        if (!initialized) BuildMeshes();
        foreach (BodyView body in bodies)
            if (body.renderer != null) body.renderer.sharedMesh = body.firstPersonMesh;
        usingFirstPersonMesh = true;
    }

    void RestoreFullMesh()
    {
        if (!usingFirstPersonMesh) return;
        foreach (BodyView body in bodies)
            if (body.renderer != null) body.renderer.sharedMesh = body.fullMesh;
        usingFirstPersonMesh = false;
    }

    void AfterCamera(Camera camera) { RestoreFullMesh(); }
    void BeforePipelineCamera(ScriptableRenderContext context, Camera camera) { BeforeCamera(camera); }
    void AfterPipelineCamera(ScriptableRenderContext context, Camera camera) { RestoreFullMesh(); }

    void OnDisable()
    {
        Camera.onPreCull -= BeforeCamera;
        Camera.onPostRender -= AfterCamera;
        RenderPipelineManager.beginCameraRendering -= BeforePipelineCamera;
        RenderPipelineManager.endCameraRendering -= AfterPipelineCamera;
        RestoreFullMesh();
    }

    void OnDestroy()
    {
        RestoreFullMesh();
        foreach (BodyView body in bodies)
            if (body.firstPersonMesh != null) Destroy(body.firstPersonMesh);
    }
}
