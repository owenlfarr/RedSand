#if UNITY_EDITOR
using System;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using TMPro;
[InitializeOnLoad]public static class BlindLungePlayChecks
{
 const string Armed="BlindLungePlay.Armed",Previous="BlindLungePlay.Previous";
 static float stageAt;static int stage;static double deadline;static BlindLungeMonster monster;static PlayerController player;static RockThrower tool;static Vector3 start;
 static BlindLungePlayChecks(){EditorApplication.delayCall+=Begin;EditorApplication.playModeStateChanged+=Changed;EditorApplication.update+=Tick;}
 static void Begin(){if(!File.Exists("Logs/BlindLungePlay.request")||EditorApplication.isPlayingOrWillChangePlaymode)return;File.Delete("Logs/BlindLungePlay.request");for(int i=0;i<SceneManager.sceneCount;i++)if(SceneManager.GetSceneAt(i).isDirty){File.WriteAllText("Logs/BlindLungePlayResult.txt","SKIPPED unsaved scene");return;}SessionState.SetString(Previous,SceneManager.GetActiveScene().path);EditorSceneManager.OpenScene("Assets/Scenes/Night 3.unity");SessionState.SetBool(Armed,true);EditorApplication.isPlaying=true;}
 static void Changed(PlayModeStateChange change){if(!SessionState.GetBool(Armed,false))return;if(change==PlayModeStateChange.EnteredPlayMode){stage=0;deadline=EditorApplication.timeSinceStartup+35;}if(change==PlayModeStateChange.EnteredEditMode){SessionState.SetBool(Armed,false);EditorApplication.delayCall+=()=>{var path=SessionState.GetString(Previous,"");if(!string.IsNullOrEmpty(path))EditorSceneManager.OpenScene(path);};}}
 static void Require(bool test,string message){if(!test)throw new Exception(message);}
 static void Tick(){if(!SessionState.GetBool(Armed,false)||!EditorApplication.isPlaying||EditorApplication.isCompiling||deadline==0)return;try{
  if(EditorApplication.timeSinceStartup>deadline)throw new Exception("Playtest timeout at stage "+stage);
  if(stage==0){
   if(Time.time<2)return;
   monster=UnityEngine.Object.FindFirstObjectByType<BlindLungeMonster>();player=UnityEngine.Object.FindFirstObjectByType<PlayerController>();Require(monster!=null&&player!=null,"Night 3 monster/player missing");tool=player.GetComponent<RockThrower>();Require(tool!=null,"rock tool installed");
   foreach(var eye in UnityEngine.Object.FindObjectsByType<EyeMonster>(FindObjectsSortMode.None))eye.enabled=false;
   foreach(var zombie in UnityEngine.Object.FindObjectsByType<ZombieAI>(FindObjectsSortMode.None))zombie.enabled=false;
   foreach(var watcher in UnityEngine.Object.FindObjectsByType<PowerStationWatcher>(FindObjectsSortMode.None))watcher.enabled=false;
   foreach(var surveyor in UnityEngine.Object.FindObjectsByType<SurveyorMonster>(FindObjectsSortMode.None))surveyor.enabled=false;
   var terrain=Terrain.activeTerrain;var body=player.GetComponent<CharacterController>();
   Vector3 surface=monster.transform.position+Vector3.right*5;
   surface.y=terrain.SampleHeight(surface)+terrain.transform.position.y+.05f-player.transform.TransformVector(body.center-Vector3.up*body.height*.5f).y;
   body.enabled=false;player.transform.position=surface;body.enabled=true;
   var floor=GameObject.CreatePrimitive(PrimitiveType.Cube);floor.transform.position=new Vector3(0,2999.5f,0);floor.transform.localScale=new Vector3(200,1,200);
   var motor=monster.GetComponent<CharacterController>();motor.enabled=false;monster.transform.position=start=new Vector3(0,3000.05f,0);motor.enabled=true;Physics.SyncTransforms();
   Require(monster.HearNoise(start+Vector3.right*25,40,true),"audible rock target rejected");stageAt=Time.time;stage=1;
  }else if(stage==1&&Time.time-stageAt>1){
   float distance=Vector3.ProjectOnPlane(monster.transform.position-start,Vector3.up).magnitude;Require(Mathf.Abs(distance-10)<.15f,"actual lunge did not stop at 10m: "+distance);
   Require(BlindLungeMonster.GroundInReach(player,Vector3.down),"player cannot search actual planet surface");
   monster.RequestSearch(player,Vector3.down);stageAt=Time.time;stage=2;
  }else if(stage==2&&Time.time-stageAt<=1.3f){monster.UpdateSearchHold(player,Vector3.down,true);
  }else if(stage==2&&Time.time-stageAt>1.3f){
   var result=tool.GetComponentsInChildren<TMP_Text>().FirstOrDefaultText();Require(result,"server search callback did not reach result HUD");
   tool.OfflineCount=1;stageAt=Time.time;stage=3;
  }else if(stage==3&&Time.time-stageAt>.3f){
   var held=(GameObject)typeof(RockThrower).GetField("heldRock",System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Instance).GetValue(tool);
   Require(held!=null&&held.activeInHierarchy,"physical held rock was not visible");
   Require(held.transform.parent==player.playerCamera,"held rock is not attached to the first-person camera");
   var mesh=held.GetComponentInChildren<MeshFilter>();Require(mesh!=null&&mesh.sharedMesh!=null,"held rock has no authored 3D mesh");
   var camera=player.playerCamera.GetComponent<Camera>();Vector3 viewport=camera.WorldToViewportPoint(held.transform.position);
   Require(viewport.x>.05f&&viewport.x<.3f&&viewport.y>.05f&&viewport.y<.35f&&viewport.z>camera.nearClipPlane,"physical rock is not in the bottom-left view");
   Require(held.GetComponentInChildren<MeshRenderer>().sharedMaterial.mainTexture!=null,"held rock has no stone texture");
   monster.RequestThrow(player,Vector3.forward+Vector3.up*.4f);Require(tool.Carried==0,"throw did not consume rock");
   Require(GameObject.Find("Thrown small rock")!=null,"throw did not spawn 3D projectile");stageAt=Time.time;stage=4;
  }else if(stage==4&&Time.time-stageAt>.5f){File.WriteAllText("Logs/BlindLungePlayResult.txt","PASS actual 10m lunge/recovery, ground search and delayed HUD result, authored physical rock mesh attached to the first-person camera in the bottom left, throw consumes one rock and spawns projectile. No scene settings were saved.");deadline=0;EditorApplication.isPlaying=false;}

 }catch(Exception e){File.WriteAllText("Logs/BlindLungePlayResult.txt","FAIL "+e);deadline=0;EditorApplication.isPlaying=false;}}
 static bool FirstOrDefaultText(this TMP_Text[] labels){foreach(var label in labels)if(label.text=="Nothing found"||label.text=="Found one small rock")return true;return false;}
}
#endif
