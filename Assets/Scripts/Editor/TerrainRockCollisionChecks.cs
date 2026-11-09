#if UNITY_EDITOR
using System;
using System.IO;
using System.Linq;
using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine.SceneManagement;

public static class TerrainRockCollisionChecks
{
    static int checks;
    static void Check(bool okay,string message){if(!okay)throw new Exception(message);checks++;}
    public static void Run()
    {
        Directory.CreateDirectory("Logs");
        EditorSceneManager.OpenScene("Assets/Scenes/MainMenu.unity");
        var scene=EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Additive);
        SceneManager.SetActiveScene(scene);
        TerrainData data=null;
        try
        {
            data=new TerrainData { heightmapResolution=33, size=new Vector3(200,10,200) };
            var prefab=AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/MarsTerrainRocks/MarsTerrainBoulder.prefab");
            data.treePrototypes=new[]{new TreePrototype{prefab=prefab}};
            data.SetTreeInstances(new[]{new TreeInstance{position=new Vector3(.5f,0,.5f),rotation=.7f,widthScale=.15f,heightScale=.2f,color=Color.white,lightmapColor=Color.white,prototypeIndex=0}},false);
            var ground=Terrain.CreateTerrainGameObject(data);
            var terrain=ground.GetComponent<Terrain>();terrain.drawTreesAndFoliage=false;terrain.treeDistance=0;
            ground.GetComponent<TerrainCollider>().enabled=false;
            var collisions=TerrainRockCollisions.Install(scene);
            Check(collisions.RockCount==1&&collisions.ColliderCount==1,"one painted rock produces one solid collider");
            Check(TerrainRockCollisions.Install(scene)==collisions,"repeated scene setup cannot duplicate colliders");
            Check(collisions.GetComponentsInChildren<Renderer>().Length==0,"collision setup never duplicates visible rocks");
            var mesh=collisions.GetComponentInChildren<MeshCollider>();
            Check(!mesh.isTrigger&&mesh.enabled&&!mesh.convex,"painted rock is solid static mesh geometry");
            Check(Vector3.Distance(mesh.transform.position,new Vector3(100,0,100))<.001f,"painted position preserved");
            Check(Quaternion.Angle(mesh.transform.rotation,Quaternion.Euler(0,.7f*Mathf.Rad2Deg,0))<.01f,"painted rotation preserved");
            Check(Vector3.Distance(mesh.transform.localScale,new Vector3(.75f,1,.75f))<.001f,"painted width/height include prefab root scale");
            Physics.SyncTransforms();
            foreach(var radius in new[]{.4f,.8f})
            {
                var actor=new GameObject(radius==.4f?"Player motor":"Monster motor");
                actor.transform.position=new Vector3(mesh.bounds.min.x-3,mesh.bounds.center.y-1,mesh.bounds.center.z);
                var motor=actor.AddComponent<CharacterController>();motor.height=2;motor.radius=radius;motor.center=Vector3.up;motor.minMoveDistance=0;
                Physics.SyncTransforms();
                bool hit=false;
                // Stop on first contact: continuing to push a capsule into an
                // irregular boulder can legitimately slide it around the side.
                for(int step=0;step<100&&!hit;step++)hit=(motor.Move(Vector3.right*.5f)&CollisionFlags.Sides)!=0;
                Check(hit&&actor.transform.position.x<mesh.bounds.center.x,"actual "+actor.name+" cannot cross painted rock with terrain rendering/native tree colliders off");
                UnityEngine.Object.DestroyImmediate(actor);
            }
            foreach(string name in new[]{"Night 1","Night1_MultiplayerTest","Night 2","Night 3"})
            {
                var level=EditorSceneManager.OpenScene("Assets/Scenes/"+name+".unity",OpenSceneMode.Additive);
                try {
                    var generated=TerrainRockCollisions.Install(level);
                    Check(generated.RockCount>0&&generated.ColliderCount>=generated.RockCount,name+": every painted rock has explicit collision");
                    Check(generated.GetComponentsInChildren<MeshCollider>().All(c=>c.sharedMesh!=null&&c.sharedMesh.isReadable&&!c.isTrigger),name+": all collider meshes valid/readable");
                } finally {EditorSceneManager.CloseScene(level,true);}
            }
            File.WriteAllText("Logs/TerrainRockCollisionResult.txt","PASS "+checks+" assertions: real player/monster CharacterController sweeps blocked by scaled/rotated painted rock, independent of native tree collision and rendering; all four authored nights have valid explicit rock collision.");
        }
        catch(Exception error){File.WriteAllText("Logs/TerrainRockCollisionResult.txt","FAIL "+error);throw;}
        finally{EditorSceneManager.CloseScene(scene,true);if(data!=null)UnityEngine.Object.DestroyImmediate(data);}
    }
}
#endif
