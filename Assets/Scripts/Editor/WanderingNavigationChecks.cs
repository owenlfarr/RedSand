#if UNITY_EDITOR
using System;
using System.IO;
using System.Collections.Generic;
using System.Reflection;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.SceneManagement;

public static class WanderingNavigationChecks
{
    public static void Run()
    {
        Directory.CreateDirectory("Logs");
        EditorSceneManager.OpenScene("Assets/Scenes/MainMenu.unity");
        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
        SceneManager.SetActiveScene(scene);
        NavMeshData data = null;
        NavMeshDataInstance instance = default;
        try
        {
            var settings = TerrainRockNavigation.Settings();
            data = NavMeshBuilder.BuildNavMeshData(settings, new List<NavMeshBuildSource> {
                new NavMeshBuildSource { shape=NavMeshBuildSourceShape.Box, size=new Vector3(100,1,100), transform=Matrix4x4.TRS(new Vector3(0,-.5f,0),Quaternion.identity,Vector3.one) },
                new NavMeshBuildSource { shape=NavMeshBuildSourceShape.Box, size=new Vector3(4,4,4), transform=Matrix4x4.TRS(new Vector3(0,2,6),Quaternion.identity,Vector3.one) }
            }, new Bounds(Vector3.zero,new Vector3(110,20,110)),Vector3.zero,Quaternion.identity);
            Require(data!=null,"test navmesh built");
            instance=NavMesh.AddNavMeshData(data);
            var surface=new GameObject("Test surface").AddComponent<TerrainRockNavigation>();
            typeof(TerrainRockNavigation).GetField("ready",BindingFlags.NonPublic|BindingFlags.Instance).SetValue(surface,true);
            var surfaces=(Dictionary<int,TerrainRockNavigation>)typeof(TerrainRockNavigation).GetField("surfaces",BindingFlags.NonPublic|BindingFlags.Static).GetValue(null);
            surfaces[scene.handle]=surface;
            var actor=new GameObject("Wandering actor");var motor=actor.AddComponent<CharacterController>();motor.height=2;motor.radius=.4f;motor.center=Vector3.up;
            var route=new TerrainRockRoute();Vector3 goal=Vector3.forward*30;
            // The goal is across a rock: the path has corners, but local wander is clear.
            Require(route.Direction(motor,goal,Vector3.right)==Vector3.right,"sideways wandering preserved across a multi-corner bunker route");
            Require(route.Direction(motor,goal,Vector3.left)==Vector3.left,"opposite wandering preserved");
            Require(route.Direction(motor,goal,Vector3.back)==Vector3.back,"wandering away from bunker preserved");
            motor.enabled=false;actor.transform.position=Vector3.forward*2.4f;motor.enabled=true;
            Vector3 detour=new TerrainRockRoute().Direction(motor,goal,Vector3.forward);
            Require(detour.sqrMagnitude>.5f && Vector3.Dot(detour,Vector3.forward)<.98f,"blocked steps still route around rock");
            var solar=new GameObject("Solar repair timing").AddComponent<SolarFlareSystem>();
            solar.bunkerLights=Array.Empty<Light>();
            solar.checkInterval=120;
            var deadline=typeof(SolarFlareSystem).GetField("nextCheckTime",BindingFlags.NonPublic|BindingFlags.Instance);
            deadline.SetValue(solar,-100f);
            typeof(SolarFlareSystem).GetMethod("CompleteRepair",BindingFlags.NonPublic|BindingFlags.Instance).Invoke(solar,null);
            Require((float)deadline.GetValue(solar)>=Time.time+119.9f,"repair resets overdue flare deadline by a full interval");
            foreach(string name in new[]{"Night 1","Night1_MultiplayerTest","Night 2","Night 3"})
            {
                var level=EditorSceneManager.OpenScene("Assets/Scenes/"+name+".unity",OpenSceneMode.Additive);
                try {
                    var terrain=level.GetRootGameObjects().SelectMany(root=>root.GetComponentsInChildren<Terrain>(true)).Single();
                    Require(terrain.treeMaximumFullLODCount>=terrain.terrainData.treeInstanceCount,name+": all painted rocks fit full-detail budget");
                    Require(terrain.treeBillboardDistance>terrain.treeDistance,name+": no billboard transition within visible range");
                    var flare=level.GetRootGameObjects().SelectMany(root=>root.GetComponentsInChildren<SolarFlareSystem>(true)).Single();
                    float chance=name=="Night 2"?.2f:name=="Night 3"?.21f:.1f;
                    Require(Mathf.Approximately(flare.checkInterval,120)&&Mathf.Approximately(flare.flareChance,chance),name+": flare chance and interval");
                    var clock=level.GetRootGameObjects().SelectMany(root=>root.GetComponentsInChildren<NightTimeManager>(true)).Single();
                    Require(!clock.useCustomEndingTime&&Mathf.Approximately(clock.secondsPerHour*((clock.endHour-clock.startHour+24)%24),900),name+": full night duration");
                } finally {EditorSceneManager.CloseScene(level,true);}
            }
            new NetworkSceneIdentityBuildCheck().OnPreprocessBuild(null);
            File.WriteAllText("Logs/WanderingNavigationResult.txt","PASS: real NavMesh preserves sideways, reverse and multi-corner wandering; obstructed steps still detour. Per-night tuning and 15-minute clocks unchanged.");
            File.WriteAllText("Logs/RockAndFlareRestorationResult.txt","PASS: overdue flare repair deadline reset; all four night scenes have full-detail rock capacity, no in-range billboard transition, 120-second checks, 10%/20%/21% flare tiers and 900-second clocks; network scene identities valid.");
        }
        catch(Exception error){File.WriteAllText("Logs/WanderingNavigationResult.txt","FAIL "+error);throw;}
        finally { if(instance.valid)instance.Remove();if(data!=null)UnityEngine.Object.DestroyImmediate(data);EditorSceneManager.CloseScene(scene,true); }
    }
    static void Require(bool condition,string message){if(!condition)throw new Exception(message);}
}
#endif
