using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.SceneManagement;

// One scene-local surface shared by authority-side CharacterController enemies.
// Painted terrain trees do not reliably appear as ordinary scene Colliders, so
// include their scaled/rotated footprints explicitly, independently of tree LOD.
public sealed class TerrainRockNavigation : MonoBehaviour
{
    NavMeshData data;
    NavMeshDataInstance instance;
    AsyncOperation building;
    bool ready;
    public bool Ready => ready;
    static readonly Dictionary<int, TerrainRockNavigation> surfaces = new Dictionary<int, TerrainRockNavigation>();

    public static TerrainRockNavigation ForScene(Scene scene)
    {
        if (surfaces.TryGetValue(scene.handle, out var found) && found != null) return found;
        bool hasTerrain = false;
        foreach (var terrain in Terrain.activeTerrains)
            if (terrain.gameObject.scene == scene && terrain.terrainData != null) { hasTerrain = true; break; }
        if (!hasTerrain) return null;
        var go = new GameObject("Terrain rock navigation");
        SceneManager.MoveGameObjectToScene(go, scene);
        var surface = go.AddComponent<TerrainRockNavigation>();
        surfaces[scene.handle] = surface;
        surface.StartCoroutine(surface.Build());
        return surface;
    }

    public static NavMeshBuildSettings Settings()
    {
        var settings = NavMesh.GetSettingsByIndex(0);
        settings.agentRadius = 0.6f;
        settings.agentHeight = 2f;
        settings.agentClimb = 0.25f;
        settings.agentSlope = 42f;
        settings.overrideVoxelSize = true;
        settings.voxelSize = 0.18f;
        settings.overrideTileSize = true;
        settings.tileSize = 128;
        return settings;
    }

    public static List<NavMeshBuildSource> Sources(Scene scene, out Bounds bounds)
    {
        var sources = new List<NavMeshBuildSource>();
        var markups = new List<NavMeshBuildMarkup>();
        foreach (var root in scene.GetRootGameObjects())
        {
            foreach (var actor in root.GetComponentsInChildren<CharacterController>(true))
                markups.Add(new NavMeshBuildMarkup { root = actor.transform, ignoreFromBuild = true });
            foreach (var body in root.GetComponentsInChildren<Rigidbody>(true))
                markups.Add(new NavMeshBuildMarkup { root = body.transform, ignoreFromBuild = true });
            var collected = new List<NavMeshBuildSource>();
            NavMeshBuilder.CollectSources(root.transform, ~0, NavMeshCollectGeometry.PhysicsColliders, 0, markups, collected);
            sources.AddRange(collected);
        }
        bounds = new Bounds();
        bool first = true;
        foreach (var terrain in Terrain.activeTerrains)
        {
            if (terrain.gameObject.scene != scene || terrain.terrainData == null) continue;
            var td = terrain.terrainData;
            var terrainBounds = new Bounds(terrain.transform.position + td.size * 0.5f, td.size);
            if (first) { bounds = terrainBounds; first = false; } else bounds.Encapsulate(terrainBounds);
            var prototypes = td.treePrototypes;
            foreach (var tree in td.treeInstances)
            {
                if (tree.prototypeIndex < 0 || tree.prototypeIndex >= prototypes.Length) continue;
                var prefab = prototypes[tree.prototypeIndex].prefab;
                if (prefab == null || !prefab.name.ToLowerInvariant().Contains("rock") && !prefab.name.StartsWith("MarsTerrain")) continue;
                var meshes = prefab.GetComponentsInChildren<MeshFilter>(true);
                Vector3 position = terrain.transform.position + Vector3.Scale(tree.position, td.size);
                var matrix = Matrix4x4.TRS(position, Quaternion.Euler(0f, tree.rotation * Mathf.Rad2Deg, 0f), new Vector3(tree.widthScale, tree.heightScale, tree.widthScale));
                foreach (var filter in meshes)
                {
                    if (filter.sharedMesh == null) continue;
                    var meshBounds = filter.sharedMesh.bounds;
                    Matrix4x4 local = prefab.transform.worldToLocalMatrix * filter.transform.localToWorldMatrix;
                    // Root prefab scale is part of the painted tree's geometry.
                    local = Matrix4x4.Scale(prefab.transform.localScale) * local;
                    Matrix4x4 world = matrix * local;
                    float height = world.MultiplyVector(Vector3.up * meshBounds.size.y).magnitude;
                    if (height < 0.2f) continue; // Step-over stones must not seal off the ground.
                    Vector3 size = meshBounds.size;
                    size.y += 1f / Mathf.Max(0.001f, world.MultiplyVector(Vector3.up).magnitude);
                    Vector3 center = meshBounds.center;
                    center.y -= 0.25f / Mathf.Max(0.001f, world.MultiplyVector(Vector3.up).magnitude);
                    sources.Add(new NavMeshBuildSource {
                        shape = NavMeshBuildSourceShape.ModifierBox,
                        transform = world * Matrix4x4.Translate(center),
                        size = size,
                        area = 1 // Not Walkable, including the ground underneath the rock.
                    });
                }
            }
        }
        bounds.Expand(new Vector3(12f, 20f, 12f));
        return sources;
    }

    IEnumerator Build()
    {
        var settings = Settings();
        var sources = Sources(gameObject.scene, out var bounds);
        data = new NavMeshData(settings.agentTypeID);
        building = NavMeshBuilder.UpdateNavMeshDataAsync(data, settings, sources, bounds);
        yield return building;
        if (this == null) yield break;
        instance = NavMesh.AddNavMeshData(data);
        ready = instance.valid;
        Debug.Log("[TerrainRockNavigation] Ready: " + sources.Count + " ground/obstacle sources.");
    }

    void OnDestroy()
    {
        if (data != null && building != null && !building.isDone) NavMeshBuilder.Cancel(data);
        if (instance.valid) instance.Remove();
        if (data != null) Destroy(data);
        surfaces.Remove(gameObject.scene.handle);
    }
}

// Kept per monster: no shared target/path state between the two motion monsters.
public sealed class TerrainRockRoute
{
    // Unity native objects must be created on the main thread, outside actor constructors.
    NavMeshPath path;
    Vector3[] corners;
    int corner;
    float nextPathTime;
    Vector3 lastGoal, previousPosition;
    float stalled;
    bool sampled;

    public Vector3 Direction(CharacterController motor, Vector3 goal, Vector3 preferred)
    {
        var surface = TerrainRockNavigation.ForScene(motor.gameObject.scene);
        if (surface == null) return preferred;
        if (!surface.Ready) return Vector3.zero;
        if (path == null) path = new NavMeshPath();
        var position = motor.transform.position;
        if (sampled && preferred.sqrMagnitude > 0.01f)
            stalled = Vector3.ProjectOnPlane(position - previousPosition, Vector3.up).sqrMagnitude < 0.00001f ? stalled + Time.deltaTime : 0f;
        previousPosition = position; sampled = true;
        if (Time.time >= nextPathTime || (goal - lastGoal).sqrMagnitude > 4f || stalled > 0.8f)
        {
            nextPathTime = Time.time + 0.65f;
            lastGoal = goal; stalled = 0f; corners = null; corner = 0;
            Vector3 feet = position + motor.center - Vector3.up * (motor.height * 0.5f);
            if (NavMesh.SamplePosition(feet, out var start, 5f, NavMesh.AllAreas) &&
                NavMesh.SamplePosition(goal, out var end, 6f, NavMesh.AllAreas) &&
                NavMesh.CalculatePath(start.position, end.position, NavMesh.AllAreas, path) && path.status != NavMeshPathStatus.PathInvalid)
                corners = path.corners;
        }
        if (corners == null || corners.Length < 2) return Vector3.zero;
        while (corner < corners.Length && Vector3.ProjectOnPlane(corners[corner] - position, Vector3.up).sqrMagnitude < 0.36f) corner++;
        if (corner >= corners.Length) return Vector3.zero;
        Vector3 direction = Vector3.ProjectOnPlane(corners[corner] - position, Vector3.up).normalized;
        // Preserve wandering only on an unobstructed corridor. Tight turns use
        // the route directly rather than smoothing back into the same boulder.
        if (corner == corners.Length - 1 && preferred.sqrMagnitude > 0.01f &&
            NavMesh.SamplePosition(position, out var here, 3f, NavMesh.AllAreas) &&
            !NavMesh.Raycast(here.position, here.position + preferred.normalized * 2f, out _, NavMesh.AllAreas))
            direction = Vector3.Slerp(direction, preferred.normalized, 0.25f).normalized;
        return direction;
    }
}
