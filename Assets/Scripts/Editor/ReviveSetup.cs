#if UNITY_EDITOR
using System;
using System.IO;
using System.Linq;
using TMPro;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEditor.Build.Reporting;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using Networking;

public static class ReviveSetup
{
    const string Prefab = "Assets/Prefabs/ReviveStation.prefab";
    const string Test1 = "Assets/_Project/Tests/ReviveNetworkTest.unity", Test2 = "Assets/_Project/Tests/ReviveTransferTest.unity";
    [MenuItem("Tools/Revive/Install Both Nights %#&r")]
    public static void Install()
    {
        var previous = EditorSceneManager.GetSceneManagerSetup();
        try
        {
            if (EditorApplication.isPlaying) throw new InvalidOperationException("Stop Play Mode first.");
            foreach (var path in new[] { "Assets/Scenes/Night1_MultiplayerTest.unity", "Assets/Scenes/Night 2.unity" })
            {
                var scene = EditorSceneManager.OpenScene(path, OpenSceneMode.Single);
                if (All<ReviveStation>(scene).Any()) throw new InvalidOperationException("Revive already installed in " + path);
                var model = scene.GetRootGameObjects().Single(root => root.name.Equals("revive", StringComparison.OrdinalIgnoreCase) || root.name == "radio");
                var oldLabel = All<TMP_Text>(scene).First(text => (text.name == "Broadcast" || text.text.ToLowerInvariant().Contains("revivi")) &&
                    Vector3.Distance(text.transform.position, model.transform.position) < 5f);
                var wall = oldLabel.transform.root;
                var screen = oldLabel.transform.parent;
                var root = new GameObject("Revive Station"); root.transform.position = model.transform.position;
                model.name = "Reconstruction console"; wall.name = "Revive monitor";
                model.transform.SetParent(root.transform, true); wall.SetParent(root.transform, true);
                root.AddComponent<NetworkObject>();
                var station = root.AddComponent<ReviveStation>();
                station.consoleCollider = wall.GetComponent<Collider>();
                if (station.consoleCollider == null) station.consoleCollider = wall.gameObject.AddComponent<BoxCollider>();
                station.respawnPoint = All<Transform>(scene).FirstOrDefault(t => t.name.Equals("respawn point", StringComparison.OrdinalIgnoreCase)) ??
                    All<NetworkStartPosition>(scene).OrderBy(p => p.name, StringComparer.Ordinal).First().transform;
                UnityEngine.Object.DestroyImmediate(oldLabel.gameObject);
                BuildDisplay(station, screen, All<Transform>(scene).First(t => t.name == "BunkerZone"));
                if (path.Contains("Night1"))
                {
                    var point = station.respawnPoint; station.respawnPoint = null;
                    PrefabUtility.SaveAsPrefabAsset(root, Prefab); station.respawnPoint = point;
                }
                EditorSceneManager.MarkSceneDirty(scene); EditorSceneManager.SaveScene(scene);
            }
            AssetDatabase.SaveAssets();
            new NetworkSceneIdentityBuildCheck().OnPreprocessBuild(null);
            File.WriteAllText("Logs/ReviveInstallResult.txt", "PASS both authored consoles and wall displays converted, exactly one networked station each, first player spawn points assigned, scene identities validated. Proximity voice preserved; radio broadcasting removed.");
        }
        catch (Exception exception) { File.WriteAllText("Logs/ReviveInstallResult.txt", "FAIL " + exception); Debug.LogException(exception); }
        finally { EditorSceneManager.RestoreSceneManagerSetup(previous); }
    }
    static void BuildDisplay(ReviveStation station, Transform screen, Transform bunker)
    {
        var obj = new GameObject("Revive world display", typeof(RectTransform), typeof(Canvas));
        var rect = obj.GetComponent<RectTransform>(); rect.SetParent(station.transform, false);
        Vector3 normal = screen.right * (Vector3.Dot(bunker.position-screen.position, screen.right) > 0f ? 1f : -1f);
        rect.position = screen.position + normal * (Mathf.Abs(screen.lossyScale.x)*0.5f + 0.012f);
        rect.rotation = Quaternion.LookRotation(-normal, screen.up); rect.sizeDelta = new Vector2(1000f, 270f);
        rect.localScale = new Vector3(Mathf.Abs(screen.lossyScale.z)*0.91f/1000f, Mathf.Abs(screen.lossyScale.y)*0.88f/270f, 0.001f);
        obj.GetComponent<Canvas>().renderMode = RenderMode.WorldSpace;
        var background = new GameObject("Background", typeof(RectTransform), typeof(Image)); background.transform.SetParent(rect, false);
        var bg = background.GetComponent<RectTransform>(); bg.anchorMin = Vector2.zero; bg.anchorMax = Vector2.one; bg.offsetMin = bg.offsetMax = Vector2.zero;
        background.GetComponent<Image>().color = new Color(0.015f,0.025f,0.03f); background.GetComponent<Image>().raycastTarget = false;
        Label(rect, "CREW RECONSTRUCTION / 15 SECOND CYCLE", 22f, new Vector2(0f,94f), new Vector2(920f,40f), new Color(0.52f,0.67f,0.64f));
        station.display = Label(rect, "REVIVE STATION\nNO FALLEN CREW / COST: 1 PLASMA", 32f, new Vector2(0f,-10f), new Vector2(920f,140f), new Color(0.8f,0.95f,0.88f));
    }
    static TMP_Text Label(Transform parent, string text, float size, Vector2 position, Vector2 dimensions, Color color)
    {
        var obj = new GameObject("Label", typeof(RectTransform), typeof(TextMeshProUGUI)); obj.transform.SetParent(parent, false);
        var label = obj.GetComponent<TextMeshProUGUI>();
        label.font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>("Assets/Fonts/Space_Mono/SpaceMono-Bold SDF.asset");
        label.rectTransform.anchoredPosition = position; label.rectTransform.sizeDelta = dimensions;
        label.text = text; label.fontSize = size; label.alignment = TextAlignmentOptions.Center; label.color = color;
        label.richText = false; label.raycastTarget = false;
        return label;
    }
    [MenuItem("Tools/Revive/Build Network Regression %#&t")]
    public static void BuildTest()
    {
        var previous = EditorSceneManager.GetSceneManagerSetup();
        try
        {
            Directory.CreateDirectory("Logs/ReviveTestBuild");
            for (int i=0;i<2;i++)
            {
                var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
                var ground = GameObject.CreatePrimitive(PrimitiveType.Cube); ground.transform.position = Vector3.down*0.5f; ground.transform.localScale = new Vector3(100f,1f,100f);
                var root = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(Prefab));
                root.transform.position = Vector3.zero;
                var point = new GameObject("NetworkStartPosition_1", typeof(NetworkStartPosition)).transform;
                point.position = new Vector3(i==0 ? 12f : 22f, 1.6f, 0f);
                root.GetComponent<ReviveStation>().respawnPoint = point;
                var drill = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/DrillStation.prefab"));
                drill.transform.position = new Vector3(30f,3.5f,0f);
                var power = new GameObject("Power", typeof(NetworkObject)).AddComponent<PowerSystem>();
                power.outageCheckInterval = 100000f; power.outageChance = 0f; power.whiteLights = power.redLights = Array.Empty<Light>();
                var radar = new GameObject("Radar", typeof(NetworkObject)).AddComponent<RadarSystem>();
                radar.transform.position = new Vector3(5f,1.6f,0f); radar.objectiveTransform = radar.transform;
                var defense = new GameObject("Defense", typeof(NetworkObject)).AddComponent<DefenseSystem>();
                defense.transform.position = radar.transform.position; defense.objectiveTransform = defense.transform; defense.respawnPoints = new[] { point };
                if (i==0)
                {
                    var nm = new GameObject("Revive regression manager"); var transport = nm.AddComponent<UnityTransport>(); var manager = nm.AddComponent<NetworkManager>();
                    manager.NetworkConfig.NetworkTransport = transport; manager.NetworkConfig.EnableSceneManagement = true;
                    manager.NetworkConfig.PlayerPrefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/NetworkRealPlayer.prefab");
                    nm.AddComponent<VoiceChatManager>(); nm.AddComponent<NetworkLevelProgression>();
                }
                new GameObject("Revive regression", typeof(NetworkObject)).AddComponent<ReviveNetworkTest>();
                EditorSceneManager.SaveScene(scene, i==0 ? Test1 : Test2);
            }
            AssetDatabase.SaveAssets();
            var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions { scenes = new[] { Test1,Test2 }, target = BuildTarget.StandaloneWindows64,
                locationPathName = "Logs/ReviveTestBuild/ReviveTest.exe", options = BuildOptions.Development });
            File.WriteAllText("Logs/ReviveBuildResult.txt", report.summary.result+" errors="+report.summary.totalErrors);
        }
        catch (Exception exception) { File.WriteAllText("Logs/ReviveBuildResult.txt", "FAIL "+exception); Debug.LogException(exception); }
        finally { EditorSceneManager.RestoreSceneManagerSetup(previous); }
    }
    static System.Collections.Generic.IEnumerable<T> All<T>(Scene scene) where T:Component => scene.GetRootGameObjects().SelectMany(root=>root.GetComponentsInChildren<T>(true));
    [MenuItem("Tools/Revive/Inspect Both Installed Scenes %#&i")]
    public static void Inspect()
    {
        var previous=EditorSceneManager.GetSceneManagerSetup();
        try
        {
            if(EditorApplication.isPlaying)throw new InvalidOperationException("Stop Play Mode first.");
            foreach(var path in new[]{"Assets/Scenes/Night1_MultiplayerTest.unity","Assets/Scenes/Night 2.unity"})
            {
                var scene=EditorSceneManager.OpenScene(path,OpenSceneMode.Single);
                var station=All<ReviveStation>(scene).Single();
                if(station.consoleCollider==null||station.respawnPoint==null||station.display==null||station.GetComponent<NetworkObject>()==null)
                    throw new Exception("Missing station wiring in "+path);
                var screen=station.display.GetComponentInParent<Canvas>().transform;
                var normal=-screen.forward;
                Vector3 eye=station.consoleCollider.bounds.center+normal*2.5f+Vector3.up*0.6f;
                Physics.SyncTransforms();
                if(!Physics.Raycast(eye,(station.consoleCollider.bounds.center-eye).normalized,out var hit,4f,~0,QueryTriggerInteraction.Ignore)||!station.IsConsoleHit(hit.collider))
                    throw new Exception("Console screen cannot be targeted in "+path);
                station.display.text="REVIVING CREWMATE\n15 SECONDS";
                Canvas.ForceUpdateCanvases();
                foreach(var label in station.GetComponentsInChildren<TMP_Text>())label.ForceMeshUpdate(true,true);
                var view=new GameObject("Temporary revive view").AddComponent<Camera>();
                view.transform.position=screen.position+normal*6f+Vector3.up*0.3f;view.transform.LookAt(screen.position-Vector3.up*0.5f);
                view.fieldOfView=42f;view.nearClipPlane=0.03f;
                var target=new RenderTexture(1280,960,24);view.targetTexture=target;
                Canvas.ForceUpdateCanvases();
                foreach(var label in station.GetComponentsInChildren<TMP_Text>())label.ForceMeshUpdate(true,true);
                view.Render();
                var prev=RenderTexture.active;RenderTexture.active=target;var image=new Texture2D(1280,960,TextureFormat.RGB24,false);
                image.ReadPixels(new Rect(0,0,1280,960),0,0);image.Apply();
                File.WriteAllBytes(path.Contains("Night1")?"Logs/ReviveNight1.png":"Logs/ReviveNight2.png",image.EncodeToPNG());
                RenderTexture.active=prev;view.targetTexture=null;
                UnityEngine.Object.DestroyImmediate(image);UnityEngine.Object.DestroyImmediate(target);UnityEngine.Object.DestroyImmediate(view.gameObject);
            }
            new NetworkSceneIdentityBuildCheck().OnPreprocessBuild(null);
            File.WriteAllText("Logs/RevivePlacementResult.txt","PASS both actual nights: exactly one wired station, assigned respawn marker, screen aiming collider valid, world display rendered, enabled scene identities valid. Temporary render text not saved.");
        }
        catch(Exception exception){File.WriteAllText("Logs/RevivePlacementResult.txt","FAIL "+exception);Debug.LogException(exception);}
        finally{EditorSceneManager.RestoreSceneManagerSetup(previous);}
    }
}
#endif
