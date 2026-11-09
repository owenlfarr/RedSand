using UnityEngine;
using UnityEngine.SceneManagement;

// Standard rock materials do not support Terrain's tree/billboard transforms.
// Render painted rocks with their solid meshes so visuals and physics agree.
[DefaultExecutionOrder(-10000)]
public sealed class TerrainRockCollisions : MonoBehaviour
{
    [field: SerializeField] public int RockCount { get; private set; }
    [field: SerializeField] public int ColliderCount { get; private set; }
    [SerializeField] int geometryVersion;
    const int CurrentGeometryVersion = 2;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    static void Register()
    {
        SceneManager.sceneLoaded -= Loaded;
        SceneManager.sceneLoaded += Loaded;
    }

    static void Loaded(Scene scene, LoadSceneMode mode) => Install(scene);

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    public static void EnsureLoadedScenes()
    {
        for(int index=0;index<SceneManager.sceneCount;index++)
        {
            var scene=SceneManager.GetSceneAt(index);
            if(scene.isLoaded)Install(scene);
        }
    }

    public static TerrainRockCollisions Install(Scene scene)
    {
        var roots = scene.GetRootGameObjects();
        foreach (var root in roots)
            if (root.activeSelf && root.TryGetComponent<TerrainRockCollisions>(out var existing))
            {
                if (existing.geometryVersion == CurrentGeometryVersion) return existing;
                root.SetActive(false);
                if (Application.isPlaying) Destroy(root); else DestroyImmediate(root);
            }
        var container = new GameObject("Painted rock collision");
        SceneManager.MoveGameObjectToScene(container, scene);
        var result = container.AddComponent<TerrainRockCollisions>();
        foreach (var root in roots)
        if (root != null && root.activeSelf)
        foreach (var terrain in root.GetComponentsInChildren<Terrain>(false))
        {
            var data = terrain.terrainData;
            if (data == null) continue;
            var prototypes = data.treePrototypes;
            bool allRocks = true;
            foreach (var tree in data.treeInstances)
            {
                if (tree.prototypeIndex < 0 || tree.prototypeIndex >= prototypes.Length || !IsRock(prototypes[tree.prototypeIndex].prefab))
                    allRocks = false;
            }
            // Keep ordinary trees working on mixed terrains; current planet terrains
            // contain only rocks. Grass/detail rendering remains enabled.
            if (allRocks && data.treeInstanceCount > 0) terrain.treeDistance = 0;
            foreach (var tree in data.treeInstances)
            {
                if (tree.prototypeIndex < 0 || tree.prototypeIndex >= prototypes.Length) continue;
                var prefab = prototypes[tree.prototypeIndex].prefab;
                if (!IsRock(prefab)) continue;
                var rock = new GameObject(prefab.name + " collision");
                rock.layer = terrain.gameObject.layer;
                rock.transform.SetParent(container.transform, false);
                rock.transform.position = terrain.transform.position + Vector3.Scale(tree.position, data.size);
                rock.transform.rotation = Quaternion.Euler(0, tree.rotation * Mathf.Rad2Deg, 0);
                rock.transform.localScale = Vector3.Scale(prefab.transform.localScale, new Vector3(tree.widthScale, tree.heightScale, tree.widthScale));
                result.ColliderCount += CopyMeshes(prefab.transform, rock.transform, rock.layer, allRocks);
                result.RockCount++;
            }
        }
        result.geometryVersion = CurrentGeometryVersion;
        Physics.SyncTransforms();
        return result;
    }

    static bool IsRock(GameObject prefab) => prefab != null && (prefab.name.ToLowerInvariant().Contains("rock") || prefab.name.StartsWith("MarsTerrain"));

    static int CopyMeshes(Transform source, Transform target, int layer, bool render)
    {
        int count = 0;
        if (source.TryGetComponent<MeshFilter>(out var filter) && filter.sharedMesh != null)
        {
            var collider = target.gameObject.AddComponent<MeshCollider>();
            collider.sharedMesh = filter.sharedMesh;
            collider.convex = false;
            collider.isTrigger = false;
            if (render && source.TryGetComponent<MeshRenderer>(out var original))
            {
                target.gameObject.AddComponent<MeshFilter>().sharedMesh = filter.sharedMesh;
                var renderer = target.gameObject.AddComponent<MeshRenderer>();
                renderer.sharedMaterials = original.sharedMaterials;
                renderer.shadowCastingMode = original.shadowCastingMode;
                renderer.receiveShadows = original.receiveShadows;
                renderer.enabled = original.enabled;
            }
            count++;
        }
        foreach (Transform child in source)
        {
            if (!child.gameObject.activeSelf) continue;
            var node = new GameObject(child.name);
            node.layer = layer;
            node.transform.SetParent(target, false);
            node.transform.localPosition = child.localPosition;
            node.transform.localRotation = child.localRotation;
            node.transform.localScale = child.localScale;
            count += CopyMeshes(child, node.transform, layer, render);
        }
        return count;
    }
}
