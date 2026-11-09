#if UNITY_EDITOR
using System;using System.IO;using System.Linq;using UnityEngine;using UnityEditor;using UnityEditor.SceneManagement;using UnityEditor.Build.Reporting;using UnityEngine.SceneManagement;using Unity.Netcode;using Unity.Netcode.Transports.UTP;
[InitializeOnLoad] public static class EyeMonsterChecks
{
 static EyeMonsterChecks(){EditorApplication.delayCall+=Requested;}
 static void Requested(){if(!File.Exists("Logs/EyeBuild.request")||EditorApplication.isPlayingOrWillChangePlaymode)return;File.Delete("Logs/EyeBuild.request");Build();}
 [MenuItem("Tools/Eye Monster/Build Network Checks")]
 public static void Build(){var previous=SceneManager.GetActiveScene();var production=SceneManager.GetSceneByPath("Assets/Scenes/Night 2.unity");bool openedProduction=!production.isLoaded;try{
 if(openedProduction)production=EditorSceneManager.OpenScene("Assets/Scenes/Night 2.unity",OpenSceneMode.Additive);SceneManager.SetActiveScene(production);EyeMonsterRigFix.Apply();EyeMonsterFinalize.Apply();
 var eye=UnityEngine.Object.FindFirstObjectByType<EyeMonster>();if(eye==null)throw new Exception("Open installed Night 2");
 var animator=eye.visual.GetComponent<Animator>();var bones=eye.visual.GetComponentsInChildren<Transform>();var clip=AssetDatabase.LoadAssetAtPath<AnimationClip>("Assets/Animations/EyeChase.anim");
 AnimationMode.StartAnimationMode();AnimationMode.BeginSampling();AnimationMode.SampleAnimationClip(eye.visual,clip,0);AnimationMode.EndSampling();var rotations=bones.Select(b=>b.localRotation).ToArray();AnimationMode.BeginSampling();AnimationMode.SampleAnimationClip(eye.visual,clip,.25f);AnimationMode.EndSampling();int moved=bones.Where((b,i)=>Quaternion.Angle(rotations[i],b.localRotation)>.1f).Count();AnimationMode.StopAnimationMode();File.WriteAllText("Logs/EyeAnimationCheck.txt","Chase animated bones="+moved);if(moved<3)throw new Exception("Chase clip does not animate original skeleton");
 var scene=EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Additive);SceneManager.SetActiveScene(scene);
 var ground=GameObject.CreatePrimitive(PrimitiveType.Cube);ground.transform.position=new Vector3(0,-.5f,0);ground.transform.localScale=new Vector3(1000,1,1000);
 var camera=new GameObject("Camera",typeof(Camera),typeof(AudioListener));camera.tag="MainCamera";camera.GetComponent<Camera>().farClipPlane=1000;
 var actor=new GameObject("Eye test player");actor.AddComponent<NetworkObject>();actor.AddComponent<Networking.OwnerNetworkTransform>();actor.AddComponent<NetworkPlayerMovementState>();actor.AddComponent<NetworkOxygenState>();var prefab=PrefabUtility.SaveAsPrefabAsset(actor,"Assets/_Project/Tests/EyeTestPlayer.prefab");UnityEngine.Object.DestroyImmediate(actor);
 var go=new GameObject("NetworkManager");var transport=go.AddComponent<UnityTransport>();var manager=go.AddComponent<NetworkManager>();manager.NetworkConfig.NetworkTransport=transport;manager.NetworkConfig.PlayerPrefab=prefab;manager.NetworkConfig.ConnectionApproval=false;manager.NetworkConfig.EnableSceneManagement=true;manager.NetworkConfig.Prefabs.Add(new NetworkPrefab{Prefab=prefab});
 var monster=(GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/EyeMonster.prefab"),scene);monster.transform.position=Vector3.up*5f;monster.transform.rotation=Quaternion.identity;
 var test=new GameObject("Eye test coordinator",typeof(NetworkObject),typeof(EyeMonsterNetworkTest));
 EditorSceneManager.SaveScene(scene,"Assets/_Project/Tests/EyeNetworkTest.unity");AssetDatabase.SaveAssets();Directory.CreateDirectory("Logs/EyeTestBuild");
 var report=BuildPipeline.BuildPlayer(new BuildPlayerOptions{scenes=new[]{scene.path},target=BuildTarget.StandaloneWindows64,locationPathName="Logs/EyeTestBuild/EyeTest.exe",options=BuildOptions.Development});
 File.WriteAllText("Logs/EyeBuildResult.txt",report.summary.result+" errors="+report.summary.totalErrors);EditorSceneManager.CloseScene(scene,true);SceneManager.SetActiveScene(previous);
 }catch(Exception e){if(AnimationMode.InAnimationMode())AnimationMode.StopAnimationMode();File.WriteAllText("Logs/EyeBuildResult.txt","FAIL "+e);Debug.LogException(e);}finally{if(openedProduction&&production.isLoaded)EditorSceneManager.CloseScene(production,true);if(previous.isLoaded)SceneManager.SetActiveScene(previous);}}
}
#endif

