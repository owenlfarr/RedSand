#if UNITY_EDITOR
using System;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEngine;
using Unity.Netcode;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine.SceneManagement;
using TMPro;
[InitializeOnLoad]public static class BlindLungeSetup
{
 const string ScenePath="Assets/Scenes/Night 3.unity", PrefabPath="Assets/Prefabs/BlindLungeMonster.prefab";
 static BlindLungeSetup(){EditorApplication.delayCall+=Requested;}
 static void Requested(){if(!File.Exists("Logs/BlindLungeSetup.request")||EditorApplication.isPlayingOrWillChangePlaymode)return;File.Delete("Logs/BlindLungeSetup.request");Install();}
 [MenuItem("Tools/Blind Lunge/Install in Night 3")]
 public static void Install(){
  var previous=SceneManager.GetActiveScene();Scene scene=SceneManager.GetSceneByPath(ScenePath);bool opened=!scene.isLoaded;
  try{
   if(!opened&&scene.isDirty)throw new Exception("Night 3 has unsaved changes; save before installing.");
   if(opened)scene=EditorSceneManager.OpenScene(ScenePath,OpenSceneMode.Additive);SceneManager.SetActiveScene(scene);
   var eye=scene.GetRootGameObjects().SelectMany(g=>g.GetComponentsInChildren<EyeMonster>(true)).FirstOrDefault();
   var existing=scene.GetRootGameObjects().SelectMany(g=>g.GetComponentsInChildren<BlindLungeMonster>(true)).FirstOrDefault();
   if(existing==null){
    var root=new GameObject("Blind Lunge Monster");root.AddComponent<NetworkObject>();
    var motor=root.AddComponent<CharacterController>();motor.height=2.4f;motor.radius=.65f;motor.center=Vector3.up*1.2f;motor.skinWidth=.04f;motor.stepOffset=.4f;
    var monster=root.AddComponent<BlindLungeMonster>();
    Vector3 center=eye!=null?(eye.bunkerEntrance!=null?eye.bunkerEntrance.position:eye.transform.position):Vector3.zero;
    root.transform.position=center+new Vector3(45,0,20);
    foreach(var terrain in Terrain.activeTerrains){var o=terrain.transform.position;var size=terrain.terrainData.size;var pos=root.transform.position;if(pos.x>=o.x&&pos.x<=o.x+size.x&&pos.z>=o.z&&pos.z<=o.z+size.z){pos.y=terrain.SampleHeight(pos)+o.y+.05f;root.transform.position=pos;break;}}
    var cube=GameObject.CreatePrimitive(PrimitiveType.Cube);cube.name="Visual - replace this with model";cube.transform.SetParent(root.transform,false);cube.transform.localPosition=Vector3.up*1.2f;cube.transform.localScale=new Vector3(1.3f,2.4f,1.3f);UnityEngine.Object.DestroyImmediate(cube.GetComponent<Collider>());
    var shader=Shader.Find("Standard");var mat=new Material(shader){color=new Color(.5f,.16f,.12f)};
    const string matPath="Assets/Art/BlindLungePlaceholder.mat";var saved=AssetDatabase.LoadAssetAtPath<Material>(matPath);if(saved==null){AssetDatabase.CreateAsset(mat,matPath);saved=mat;}else UnityEngine.Object.DestroyImmediate(mat);cube.GetComponent<Renderer>().sharedMaterial=saved;monster.visual=cube;
    for(int side=0;side<4;side++){var label=new GameObject("BLIND LUNGE label",typeof(TextMeshPro));label.transform.SetParent(cube.transform,false);label.transform.localPosition=Quaternion.Euler(0,side*90,0)*new Vector3(0,.12f,-.51f);label.transform.localRotation=Quaternion.Euler(0,side*90,0);label.transform.localScale=Vector3.one*.08f;var text=label.GetComponent<TextMeshPro>();text.text="BLIND\nLUNGE";text.fontSize=28;text.alignment=TextAlignmentOptions.Center;text.rectTransform.sizeDelta=new Vector2(130,85);}
    PrefabUtility.SaveAsPrefabAsset(root,PrefabPath);EditorSceneManager.MarkSceneDirty(scene);EditorSceneManager.SaveScene(scene);AssetDatabase.SaveAssets();
   }
   var installed=scene.GetRootGameObjects().SelectMany(g=>g.GetComponentsInChildren<BlindLungeMonster>(true)).First();
   foreach(var text in installed.visual.GetComponentsInChildren<TextMeshPro>()){text.transform.localScale=Vector3.one*.08f;text.ForceMeshUpdate();EditorUtility.SetDirty(text.transform);}
   PrefabUtility.SaveAsPrefabAsset(installed.gameObject,PrefabPath);EditorSceneManager.MarkSceneDirty(scene);EditorSceneManager.SaveScene(scene);AssetDatabase.SaveAssets();
   new NetworkSceneIdentityBuildCheck().OnPreprocessBuild(null);
   File.WriteAllText("Logs/BlindLungeSetupResult.txt","PASS installed labeled cube and reusable prefab in Night 3; validated network scene identities.");
   Check();
  }catch(Exception e){File.WriteAllText("Logs/BlindLungeSetupResult.txt","FAIL "+e);Debug.LogException(e);}
  finally{if(opened&&scene.isLoaded)EditorSceneManager.CloseScene(scene,true);if(previous.IsValid()&&previous.isLoaded)SceneManager.SetActiveScene(previous);}
 }
 static void Assert(bool ok,string description){if(!ok)throw new Exception(description);}
 [MenuItem("Tools/Blind Lunge/Check Core Behaviour")]
 public static void Check(){
  Scene previous=SceneManager.GetActiveScene(),test=default;GameObject clone=null;
  try{
   int assertions=0;Action<bool,string> check=(ok,msg)=>{Assert(ok,msg);assertions++;};
   var end=BlindLungeMonster.LungeEndpoint(Vector3.zero,new Vector3(100,30,0),10);check(Mathf.Abs(end.magnitude-10)<.001f&&end.y==0,"lunge maximum ten metres, no vertical aim");
   check(BlindLungeMonster.LungeEndpoint(Vector3.zero,Vector3.right*4,10)==Vector3.right*4,"stops at nearby sound");
   check(BlindLungeMonster.SearchDuration==1&&BlindLungeMonster.SearchCooldown==2.5f,"one second search, 2.5 second cooldown");
   int wins=0;for(int i=0;i<100;i++)if(BlindLungeMonster.SearchSucceeds(i/100f))wins++;check(wins==10&&!BlindLungeMonster.SearchSucceeds(.1f),"exact ten percent search band");
   var velocity=BlindLungeMonster.ThrowVelocity(Vector3.forward);check(velocity.y>0&&velocity.magnitude>25,"throw travels upward and far");
   check(BlindLungeMonster.RockPosition(Vector3.zero,velocity,1).z>20,"rock distracts more than twenty metres away");
   test=EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Additive);SceneManager.SetActiveScene(test);
   clone=(GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath),test);clone.transform.position=Vector3.zero;
   var monster=clone.GetComponent<BlindLungeMonster>();typeof(BlindLungeMonster).GetMethod("Awake",BindingFlags.NonPublic|BindingFlags.Instance).Invoke(monster,null);
   check(!monster.HasHeardNoise,"silent players are not tracked");
   check(!monster.HearNoise(Vector3.forward*40,60),"outside listening range ignored");
   check(!monster.HearNoise(Vector3.forward*20,12),"quiet steps have shorter audible range");
   check(monster.HearNoise(Vector3.forward*25,40,true),"rock impact heard");
   check(!monster.HearNoise(Vector3.right*5,12)&&monster.LastHeardPosition==Vector3.forward*25,"rock distraction briefly overrides footsteps");
   var wall=GameObject.CreatePrimitive(PrimitiveType.Cube);wall.transform.position=new Vector3(0,1,10);wall.transform.localScale=new Vector3(10,4,1);Physics.SyncTransforms();
   check(!monster.HearNoise(Vector3.forward*25,40,true),"solid wall muffles distant sound");
   check(monster.visual!=null&&monster.visual.GetComponentsInChildren<TextMeshPro>().Length==4,"model swap visual is separate and labeled on four sides");
   File.WriteAllText("Logs/BlindLungeChecksResult.txt","PASS "+assertions+" core assertions. Multiplayer RPC/input, collision deaths and sound balance require a live playtest.");
  }catch(Exception e){File.WriteAllText("Logs/BlindLungeChecksResult.txt","FAIL "+e);Debug.LogException(e);}
  finally{if(clone!=null)UnityEngine.Object.DestroyImmediate(clone);if(test.IsValid())EditorSceneManager.CloseScene(test,true);if(previous.IsValid()&&previous.isLoaded)SceneManager.SetActiveScene(previous);}
 }
}
#endif
