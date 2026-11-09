#if UNITY_EDITOR
using System;
using System.IO;
using System.Reflection;
using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine.SceneManagement;
using Networking;
[InitializeOnLoad] public static class NightThreeFeatureChecks
{
 static int assertions;
 static NightThreeFeatureChecks(){EditorApplication.delayCall+=Requested;}
 static void Requested(){if(!File.Exists("Logs/NightThreeFeatures.request")||EditorApplication.isPlayingOrWillChangePlaymode)return;File.Delete("Logs/NightThreeFeatures.request");Run();}
 static void Check(bool ok,string message){if(!ok)throw new Exception(message);assertions++;}
 static void Call(object o,string name)=>o.GetType().GetMethod(name,BindingFlags.Instance|BindingFlags.NonPublic).Invoke(o,null);
 [MenuItem("Tools/Night Testing/Check PA and Host Unlock")]
 public static void Run(){
  var previous=SceneManager.GetActiveScene();Scene test=default;float volume=AudioListener.volume;
  try{
   assertions=0;
   Check(!NetworkLevelProgression.ShouldResetCampaign(true,true,true),"initial menu-to-night load preserves continuous run");
   Check(NetworkLevelProgression.ShouldResetCampaign(false,true,true),"disconnect during initial load resets run");
   Check(NetworkLevelProgression.ShouldResetCampaign(true,true,false),"return to menu resets run");
   Check(!PAScreecher.SupportsNight("Night 1")&&!PAScreecher.SupportsNight("Night 2"),"earlier nights excluded");
   Check(PAScreecher.SupportsNight("Night 3")&&PAScreecher.SupportsNight("Night 12"),"later nights included");
   var run=new ContinuousNightRun();run.Begin("Night1_MultiplayerTest");
   Check(!run.Arrive("Night1_MultiplayerTest","Night 2",true),"Night 2 does not unlock");
   Check(run.Arrive("Night 2","Night 3",true),"continuous surviving host earns unlock");
   Check(!run.Arrive("Night 2","Night 3",true),"cannot grant twice");
   run.Begin("Night 2");Check(!run.Arrive("Night 2","Night 3",true),"starting Night 2 cannot earn initial unlock");
   run.Begin("Night 1");run.Arrive("Night 1","Night 2",true);run.Reset();Check(!run.Arrive("Night 2","Night 3",true),"restart/disconnect invalidates run");
   run.Begin("Night 1");run.Arrive("Night 1","Night 2",true);Check(!run.Arrive("Night 2","Night 3",false),"dead host cannot earn unlock");
   foreach(string later in new[]{"Night 4","Night 5","Night 6"}){run.Begin("Night 1");run.Arrive("Night 1","Night 2",true);Check(!run.Arrive("Night 2",later,true),"later nights never grant a higher starting night");}
   Check(NightStartUnlock.StartingScene=="Night 2"||NightStartUnlock.StartingScene=="Night1_MultiplayerTest","start selector only permits nights one and two");
   test=EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Additive);test.name="Night 99";SceneManager.SetActiveScene(test);
   var go=new GameObject("PA verification");var clock=go.AddComponent<NightTimeManager>();clock.enabled=false;
   var pa=go.AddComponent<PAScreecher>();Call(pa,"Awake");pa.eventChance=0;
   var budget=typeof(PAScreecher).GetField("nightlyEventLimit",BindingFlags.Instance|BindingFlags.NonPublic);
   budget.SetValue(pa,0);Check(!pa.Trigger(5),"quiet nights cannot trigger a screecher");budget.SetValue(pa,3);
   var voice=go.AddComponent<VoiceChatManager>();
   typeof(PAScreecher).GetField("voice",BindingFlags.Instance|BindingFlags.NonPublic).SetValue(pa,voice);
   AudioListener.volume=.67f;
   Check(pa.Trigger(-10),"manual anomaly trigger");
   var end=typeof(PAScreecher).GetField("offlineEnd",BindingFlags.Instance|BindingFlags.NonPublic);
   double remaining=(double)end.GetValue(pa)-Time.timeAsDouble;
   Check(remaining>=4.9&&remaining<=5.1,"duration clamps to five seconds minimum");
   Check(!pa.Trigger(15),"active events cannot overlap");
   Call(pa,"Update");Check(pa.IsActive&&Mathf.Abs(AudioListener.volume-.67f*.04f)<.001f,"normal hearing is muffled");
   Check(voice.PAScreecherMuted,"in-game voice is interrupted");
   voice.SetReviveMuted(true);
   end.SetValue(pa,Time.timeAsDouble-1);Call(pa,"Update");
   Check(!pa.IsActive&&Mathf.Abs(AudioListener.volume-.67f)<.001f,"hearing restores after expiry");
   Check(!voice.PAScreecherMuted&&voice.ReviveMuted,"PA expiry preserves revive mute");voice.SetReviveMuted(false);
   Check(pa.Trigger(100),"second controlled event");remaining=(double)end.GetValue(pa)-Time.timeAsDouble;Check(remaining>=14.9&&remaining<=15.1,"duration clamps to fifteen seconds maximum");
   Call(pa,"Update");Call(pa,"OnDisable");Check(!pa.IsActive&&!voice.PAScreecherMuted&&Mathf.Abs(AudioListener.volume-.67f)<.001f,"scene cleanup restores audio and voice");
   end.SetValue(pa,Time.timeAsDouble-1);Check(pa.Trigger(5),"third allowed scare");end.SetValue(pa,Time.timeAsDouble-1);Check(!pa.Trigger(5),"hard cap rejects fourth scare");
   File.WriteAllText("Logs/NightThreeFeaturesResult.txt","PASS "+assertions+" assertions: night gating, host run eligibility, bounded durations, overlap, hearing/voice suppression, revive coexistence and cleanup. Unlock preferences were not changed.");
  }catch(Exception e){File.WriteAllText("Logs/NightThreeFeaturesResult.txt","FAIL "+e);Debug.LogException(e);}
  finally{AudioListener.volume=volume;if(test.IsValid())EditorSceneManager.CloseScene(test,true);if(previous.IsValid()&&previous.isLoaded)SceneManager.SetActiveScene(previous);}
 }
}
#endif
