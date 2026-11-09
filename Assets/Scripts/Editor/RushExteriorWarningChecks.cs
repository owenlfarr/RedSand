#if UNITY_EDITOR
using System;
using System.Collections;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine.SceneManagement;

public static class RushExteriorWarningChecks
{
    static int checks;
    static void Check(bool condition,string message){if(!condition)throw new Exception(message);checks++;}
    public static void Run()
    {
        Directory.CreateDirectory("Logs");
        EditorSceneManager.OpenScene("Assets/Scenes/MainMenu.unity");
        var scene=EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Additive);
        SceneManager.SetActiveScene(scene);
        try
        {
            var rush=new GameObject("Outside player warning test").AddComponent<RushMonsterEvent>();
            var outside=new GameObject("Ladder light").AddComponent<Light>();
            var inside=new GameObject("Indoor light").AddComponent<Light>();
            rush.exteriorWarningLight=outside;rush.whiteLights=new[]{inside};outside.enabled=true;inside.enabled=true;
            var routine=typeof(RushMonsterEvent).GetMethod("FlickerExteriorWarningLocal",BindingFlags.Instance|BindingFlags.NonPublic);
            var stop=typeof(RushMonsterEvent).GetMethod("StopExteriorWarning",BindingFlags.Instance|BindingFlags.NonPublic);
            // No player or bunker reference exists: the exterior cue must still run.
            var warning=(IEnumerator)routine.Invoke(rush,null);
            for(int pulse=0;pulse<10;pulse++)
            {
                Check(warning.MoveNext()&&!outside.enabled&&inside.enabled,"outside warning turns off without changing indoor lights");
                Check(warning.MoveNext()&&outside.enabled&&inside.enabled,"outside warning turns back on");
            }
            Check(!warning.MoveNext()&&outside.enabled,"normal completion restores entrance light");
            warning=(IEnumerator)routine.Invoke(rush,null);warning.MoveNext();stop.Invoke(rush,null);
            Check(outside.enabled,"cancellation restores a normally lit entrance");
            outside.enabled=false;warning=(IEnumerator)routine.Invoke(rush,null);warning.MoveNext();stop.Invoke(rush,null);
            Check(!outside.enabled,"cleanup preserves an originally unlit entrance");
            foreach(string name in new[]{"Night 1","Night1_MultiplayerTest","Night 2","Night 3"})
            {
                var level=EditorSceneManager.OpenScene("Assets/Scenes/"+name+".unity",OpenSceneMode.Additive);
                try {
                    var installed=level.GetRootGameObjects().SelectMany(root=>root.GetComponentsInChildren<RushMonsterEvent>(true)).Single();
                    var light=installed.exteriorWarningLight;
                    Check(light!=null&&light.gameObject.activeInHierarchy&&light.enabled&&Vector3.Distance(light.transform.position,new Vector3(2186.29f,87.79f,1218.2601f))<.01f,name+": actual exterior entrance light assigned");
                }finally{EditorSceneManager.CloseScene(level,true);}
            }
            new NetworkSceneIdentityBuildCheck().OnPreprocessBuild(null);
            File.WriteAllText("Logs/RushExteriorWarningResult.txt","PASS "+checks+" assertions: exterior warning pulses without a local bunker player, indoor lights untouched, normal/cancel cleanup restores prior state, exterior entrance light assigned in all four night scenes; existing NGO scene identities valid.");
        }
        catch(Exception error){File.WriteAllText("Logs/RushExteriorWarningResult.txt","FAIL "+error);throw;}
        finally{EditorSceneManager.CloseScene(scene,true);}
    }
}
#endif
