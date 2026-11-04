#if UNITY_EDITOR
using System;
using System.IO;
using System.Reflection;
using System.Threading;
using UnityEditor;
using UnityEngine;
using Networking;

public static class DeepBugChecks
{
    [InitializeOnLoadMethod] static void Initialize() { EditorApplication.delayCall += Requested; }
    static async void Requested()
    {
        if (!File.Exists("Logs/RunDeepBugChecks.request")) return;
        File.Delete("Logs/RunDeepBugChecks.request");
        GameObject actor = null, voiceObject = null;
        try
        {
            actor = UnityEngine.Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/NetworkRealPlayer.prefab"));
            var controller = actor.GetComponent<PlayerController>();
            var oxygen = actor.GetComponent<OxygenSystem>();
            typeof(OxygenSystem).GetField("isDead", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(oxygen, true);
            typeof(PlayerController).GetMethod("ApplyLocalOwnershipPresentation", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(controller, null);
            if (controller.playerCamera.GetComponent<Camera>().enabled) throw new Exception("Dead camera re-enabled");
            if (RadioBroadcastStation.CanBroadcast(controller)) throw new Exception("Dead player allowed PA");
            voiceObject = new GameObject("PA async regression");
            var voice = voiceObject.AddComponent<VoiceChatManager>();
            var gate = (SemaphoreSlim)typeof(VoiceChatManager).GetField("channelGate", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(voice);
            var intent = typeof(VoiceChatManager).GetField("wantsRadioBroadcast", BindingFlags.Instance | BindingFlags.NonPublic);
            await gate.WaitAsync();
            voice.BeginRadioBroadcast();
            if (!(bool)intent.GetValue(voice)) throw new Exception("PA start not recorded synchronously");
            voice.EndRadioBroadcast();
            if ((bool)intent.GetValue(voice)) throw new Exception("PA release queued instead of immediately cancelling");
            gate.Release();
            await gate.WaitAsync(); gate.Release();
            if (voice.IsRadioBroadcasting) throw new Exception("Cancelled PA started");
            File.WriteAllText("Logs/DeepBugChecks.txt", "PASS: dead camera, dead PA eligibility, queued PA cancellation, broadcast state");
            UnityEngine.Object.DestroyImmediate(actor); actor = null;
            UnityEngine.Object.DestroyImmediate(voiceObject); voiceObject = null;
            // BuildPlayer pumps the editor loop. Run it after this async
            // continuation returns to avoid recursively entering PlayerLoop.
            EditorApplication.delayCall += ReviveSetup.BuildTest;
        }
        catch (Exception ex) { File.WriteAllText("Logs/DeepBugChecks.txt", "FAIL " + ex); Debug.LogException(ex); }
        finally
        {
            if (actor != null) UnityEngine.Object.DestroyImmediate(actor);
            if (voiceObject != null) UnityEngine.Object.DestroyImmediate(voiceObject);
        }
    }
}
#endif
