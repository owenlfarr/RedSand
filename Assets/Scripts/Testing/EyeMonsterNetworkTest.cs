using System;using System.Linq;using System.Collections;using System.IO;using UnityEngine;using Unity.Netcode;using Unity.Netcode.Transports.UTP;
[DefaultExecutionOrder(-100)] public sealed class EyeMonsterNetworkTest:NetworkBehaviour
{
 public NetworkVariable<int> Stage=new NetworkVariable<int>();
 EyeMonster eye;Camera view;string role;int assertions;bool clientFrozen,clientChased;
 IEnumerator Start(){var args=Environment.GetCommandLineArgs();int at=Array.IndexOf(args,"-eyeTest");if(at<0)yield break;role=args[at+1];Application.runInBackground=true;Application.targetFrameRate=60;QualitySettings.vSyncCount=0; var m=Unity.Netcode.NetworkManager.Singleton;m.GetComponent<UnityTransport>().SetConnectionData("127.0.0.1",17845,"127.0.0.1");yield return null;if(role=="host")m.StartHost();else m.StartClient();yield return new WaitForSecondsRealtime(90);File.WriteAllText("Logs/Eye-"+role+"-FAIL.txt","Timeout");Application.Quit(2);}
 public override void OnNetworkSpawn(){eye=FindFirstObjectByType<EyeMonster>();view=Camera.main;if(IsServer)StartCoroutine(Run());}
 void Update(){if(!IsSpawned||eye==null||NetworkManager.LocalClient?.PlayerObject==null)return;if(!IsServer){if(eye.State.Value==2)clientFrozen=true;if(eye.State.Value==1)clientChased=true;}var p=NetworkManager.LocalClient.PlayerObject;Vector3 pos=IsHost?new Vector3(0,0,25):new Vector3(28,0,0);if(Stage.Value==4)pos*=10;
 if(Stage.Value==6||Stage.Value==7){bool victim=Stage.Value==6?!IsHost:IsHost;pos=victim?eye.transform.position+Vector3.left*2.2f:new Vector3(200,0,200);pos.y=0;}p.transform.position=pos;view.transform.position=pos+Vector3.up*((Stage.Value==6||Stage.Value==7)?1.7f:5f);bool watch=Stage.Value==1&&!IsHost||Stage.Value==3&&IsHost||Stage.Value==5&&!IsHost;
 view.transform.rotation=watch?Quaternion.LookRotation(eye.transform.position+Vector3.up*5-view.transform.position):Quaternion.LookRotation(Vector3.ProjectOnPlane(view.transform.position-eye.transform.position,Vector3.up).normalized);}
 IEnumerator Run(){float deadline=Time.time+25;while(NetworkManager.ConnectedClientsList.Count<2&&Time.time<deadline)yield return null;if(NetworkManager.ConnectedClientsList.Count<2){Fail("Client failed to connect");yield break;}Check(eye.FootstepSource.spatialBlend==1f && eye.FootstepSource.spread==0f,"footsteps are fully positional with directional spread");
 Check(eye.FootstepSource.rolloffMode==AudioRolloffMode.Custom && eye.FootstepSource.GetCustomCurve(AudioSourceCurveType.CustomRolloff).Evaluate(0.7f)<0.06f,"far footsteps fade to faint");
 Check(Mathf.Abs(eye.transform.position.y-0.015f)<0.05f,"elevated spawn snaps to floor");
 eye.killDistance=0.1f;eye.chaseSpeed=2;eye.roamSpeed=1;eye.chaseRange=60;
 Stage.Value=1;yield return new WaitForSeconds(1);Check(eye.State.Value==2,"client alone freezes host");Vector3 held=eye.transform.position;yield return new WaitForSeconds(.4f);Check(Vector3.Distance(held,eye.transform.position)<.01f,"frozen position stable");Check(eye.GetComponentInChildren<Animator>().speed==0,"animation frozen");
 var skin=eye.visual.GetComponentInChildren<SkinnedMeshRenderer>();var baked=new Mesh();skin.BakeMesh(baked,true);float sole=baked.vertices.Min(v=>skin.transform.TransformPoint(v).y);float animatedHeight=baked.vertices.Max(v=>skin.transform.TransformPoint(v).y)-sole;Destroy(baked);Check(animatedHeight<eye.GetComponent<CharacterController>().height*1.3f,"humanoid animation preserves model proportions");
 Check(Mathf.Abs(sole-eye.transform.position.y)<0.05f,"animated soles touch motor floor while frozen");
 uint stepsBefore=eye.StepSequence.Value;Stage.Value=2;yield return new WaitForSeconds(2);Check(eye.StepSequence.Value>stepsBefore,"server emits footsteps from actual movement");Check(eye.State.Value==1,"all look away starts chase");Check(eye.Target.Value==NetworkManager.LocalClientId,"nearest player selected in any direction");Check(eye.transform.position.z>held.z+.2f,"runs directly toward nearest");
 Stage.Value=3;yield return new WaitForSeconds(1);Check(eye.State.Value==2,"host alone freezes all");uint frozenSteps=eye.StepSequence.Value;yield return new WaitForSeconds(.3f);Check(eye.StepSequence.Value==frozenSteps,"footstep events stop while frozen");
 Stage.Value=4;yield return new WaitForSeconds(1);Check(eye.State.Value==0,"no players in range roams");
 Stage.Value=5;yield return new WaitForSeconds(1);Check(eye.State.Value==2,"client reacquires visibility");var other=NetworkManager.ConnectedClientsList.First(c=>c.ClientId!=NetworkManager.LocalClientId);other.PlayerObject.GetComponent<NetworkOxygenState>().IsDead.Value=true;yield return new WaitForSeconds(1);Check(eye.State.Value==1,"dead observer cannot freeze");
 eye.killDistance=1.7f;Stage.Value=6;other.PlayerObject.GetComponent<NetworkOxygenState>().IsDead.Value=false;yield return new WaitForSeconds(1);
 Check(other.PlayerObject.GetComponent<NetworkOxygenState>().IsDead.Value,"normal contact kills joined client (state="+eye.State.Value+", target="+eye.Target.Value+", distance="+Vector3.Distance(eye.transform.position,other.PlayerObject.transform.position)+")");Check(!NetworkManager.LocalClient.PlayerObject.GetComponent<NetworkOxygenState>().IsDead.Value,"client kill leaves host alive");
 Stage.Value=7;yield return new WaitForSeconds(1);Check(NetworkManager.LocalClient.PlayerObject.GetComponent<NetworkOxygenState>().IsDead.Value,"server contact kill");Check(eye.LocalDeathPresentations==1,"victim receives monster death presentation once");
 File.WriteAllText("Logs/Eye-host-PASS.txt",assertions+" real host/client assertions passed");FinishClientRpc();yield return new WaitForSeconds(.5f);Application.Quit();}
 void Check(bool ok,string msg){if(!ok){Fail(msg);throw new Exception(msg);}assertions++;Debug.Log("[Eye Test] PASS "+msg);}
 void Fail(string msg){File.WriteAllText("Logs/Eye-host-FAIL.txt",msg);Application.Quit(2);}
 [ClientRpc]void FinishClientRpc(){if(IsServer)return;if(!clientFrozen||!clientChased||eye.LocalDeathPresentations!=1){File.WriteAllText("Logs/Eye-client-FAIL.txt","Missing replicated state or victim-only contact death");Application.Quit(2);return;}File.WriteAllText("Logs/Eye-client-PASS.txt","PASS actual client camera reports; received replicated states and exactly one victim-only contact death.");Application.Quit();}
}




