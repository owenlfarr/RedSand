using UnityEngine;
using UnityEngine.SceneManagement;

// Terrain tree painting does not reliably instantiate prefab MeshColliders.
// Build static physics geometry on every peer before scene players start moving.
[DefaultExecutionOrder(-10000)]
public sealed class TerrainRockCollisions : MonoBehaviour
{
    public int RockCount { get; private set; }
    public int ColliderCount { get; private set; }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    static void Register()
    {
        SceneManager.sceneLoaded -= Loaded;
        SceneManager.sceneLoaded += Loaded;
    }

    static void Loaded(Scene scene, LoadSceneMode mode) => Install(scene);

    public static TerrainRockCollisions Install(Scene scene)
    {
        var roots = scene.GetRootGameObjects();
        foreach (var root in roots)
            if (root.TryGetComponent<TerrainRockCollisions>(out var existing)) return existing;
        var container = new GameObject("Painted rock collision");
        SceneManager.MoveGameObjectToScene(container, scene);
        var result = container.AddComponent<TerrainRockCollisions>();
        foreach (var root in roots)
        foreach (var terrain in root.GetComponentsInChildren<Terrain>(false))
        {
            var data = terrain.terrainData;
            if (data == null) continue;
            var prototypes = data.treePrototypes;
            foreach (var tree in data.treeInstances)
            {
                if (tree.prototypeIndex < 0 || tree.prototypeIndex >= prototypes.Length) continue;
                var prefab = prototypes[tree.prototypeIndex].prefab;
                if (prefab == null || (!prefab.name.ToLowerInvariant().Contains("rock") && !prefab.name.StartsWith("MarsTerrain"))) continue;
                var rock = new GameObject(prefab.name + " collision");
                rock.layer = terrain.gameObject.layer;
                rock.transform.SetParent(container.transform, false);
                rock.transform.position = terrain.transform.position + Vector3.Scale(tree.position, data.size);
                rock.transform.rotation = Quaternion.Euler(0, tree.rotation * Mathf.Rad2Deg, 0);
                rock.transform.localScale = Vector3.Scale(prefab.transform.localScale, new Vector3(tree.widthScale, tree.heightScale, tree.widthScale));
                result.ColliderCount += CopyMeshes(prefab.transform, rock.transform, rock.layer);
                result.RockCount++;
            }
        }
        Physics.SyncTransforms();
        return result;
    }

    static int CopyMeshes(Transform source, Transform target, int layer)
    {
        int count = 0;
        if (source.TryGetComponent<MeshFilter>(out var filter) && filter.sharedMesh != null)
        {
            var collider = target.gameObject.AddComponent<MeshCollider>();
            collider.sharedMesh = filter.sharedMesh;
            collider.convex = false;
            collider.isTrigger = false;
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
            count += CopyMeshes(child, node.transform, layer);
        }
        return count;
    }
}
