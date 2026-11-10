#if UNITY_EDITOR
using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Collections.Generic;
using UnityEngine;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using UnityEditor.Build.Reporting;

public static class BlindCrawlModelSetup
{
    public const string ModelPath="Assets/Art/Monsters/Crawl/Running Crawl (1).fbx";
    const string Folder="Assets/Art/Monsters/Crawl",PrefabPath="Assets/Prefabs/BlindLungeMonster.prefab";
    const string ClipPath=Folder+"/CrawlLoop.anim",ControllerPath=Folder+"/Crawl.controller";
    static AnimationClip clip;static AnimatorController controller;
    public static void Inspect(){
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene);var result=new StringBuilder();
        foreach(var asset in AssetDatabase.LoadAllAssetsAtPath(ModelPath))result.AppendLine(asset.GetType().Name+" | "+asset.name);
        var root=(GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(ModelPath));
        foreach(var renderer in root.GetComponentsInChildren<Renderer>())foreach(var mat in renderer.sharedMaterials)result.AppendLine(renderer.name+" bounds "+renderer.bounds+" | "+mat.name+" | "+AssetDatabase.GetAssetPath(mat.mainTexture));
        foreach(var motion in AssetDatabase.LoadAllAssetsAtPath(ModelPath).OfType<AnimationClip>().Where(c=>!c.name.StartsWith("__preview")))result.AppendLine("CLIP "+motion.name+" duration "+motion.length+" bindings "+AnimationUtility.GetCurveBindings(motion).Length);
        Directory.CreateDirectory("Logs");File.WriteAllText("Logs/BlindCrawlInspect.txt",result.ToString());
    }
    static void PrepareAssets(){
        var importer=(ModelImporter)AssetImporter.GetAtPath(ModelPath);var imports=importer.defaultClipAnimations;
        foreach(var motion in imports){motion.loopTime=true;motion.loopPose=true;motion.wrapMode=WrapMode.Loop;}
        importer.clipAnimations=imports;importer.importCameras=false;importer.importLights=false;importer.addCollider=false;importer.SaveAndReimport();
        var original=AssetDatabase.LoadAllAssetsAtPath(ModelPath).OfType<AnimationClip>().First(c=>c.name=="mixamo.com");
        var copy=UnityEngine.Object.Instantiate(original);copy.name="CrawlLoop";copy.wrapMode=WrapMode.Loop;
        var settings=AnimationUtility.GetAnimationClipSettings(copy);settings.loopTime=true;settings.loopBlend=true;AnimationUtility.SetAnimationClipSettings(copy,settings);
        foreach(var binding in AnimationUtility.GetCurveBindings(copy).Where(b=>b.path=="mixamorig:Hips"&&(b.propertyName=="m_LocalPosition.x"||b.propertyName=="m_LocalPosition.z"))){
            var curve=AnimationUtility.GetEditorCurve(copy,binding);float value=curve.Evaluate(0);var keys=curve.keys;
            for(int i=0;i<keys.Length;i++){keys[i].value=value;keys[i].inTangent=keys[i].outTangent=0;}curve.keys=keys;AnimationUtility.SetEditorCurve(copy,binding,curve);
        }
        copy.EnsureQuaternionContinuity();clip=AssetDatabase.LoadAssetAtPath<AnimationClip>(ClipPath);
        if(clip==null){AssetDatabase.CreateAsset(copy,ClipPath);clip=copy;}else{EditorUtility.CopySerialized(copy,clip);UnityEngine.Object.DestroyImmediate(copy);EditorUtility.SetDirty(clip);}
        controller=AssetDatabase.LoadAssetAtPath<AnimatorController>(ControllerPath);
        if(controller==null)controller=AnimatorController.CreateAnimatorControllerAtPath(ControllerPath);
        var machine=controller.layers[0].stateMachine;foreach(var child in machine.states)machine.RemoveState(child.state);
        controller.parameters=Array.Empty<AnimatorControllerParameter>();controller.AddParameter("crawlTime",AnimatorControllerParameterType.Float);
        var state=machine.AddState("Crawl");state.motion=clip;state.timeParameter="crawlTime";state.timeParameterActive=true;machine.defaultState=state;
        var layers=controller.layers;layers[0].defaultWeight=1;controller.layers=layers;EditorUtility.SetDirty(controller);
        foreach(string part in new[]{"1001","1002"}){
            string normalPath=Folder+"/Ch25_"+part+"_Normal.png";var normal=(TextureImporter)AssetImporter.GetAtPath(normalPath);
            if(normal.textureType!=TextureImporterType.NormalMap){normal.textureType=TextureImporterType.NormalMap;normal.SaveAndReimport();}
            string materialPath=Folder+"/CrawlSkin"+part+".mat";var material=AssetDatabase.LoadAssetAtPath<Material>(materialPath);
            if(material==null){material=new Material(Shader.Find("Standard"));AssetDatabase.CreateAsset(material,materialPath);}
            material.mainTexture=AssetDatabase.LoadAssetAtPath<Texture2D>(Folder+"/Ch25_"+part+"_Diffuse.png");material.SetTexture("_BumpMap",AssetDatabase.LoadAssetAtPath<Texture2D>(normalPath));material.EnableKeyword("_NORMALMAP");material.SetFloat("_Metallic",0);material.SetFloat("_Glossiness",.2f);EditorUtility.SetDirty(material);
        }
        AssetDatabase.SaveAssets();
    }
    static Bounds BakeBounds(GameObject root){
        bool first=true;Bounds bounds=default;
        foreach(var renderer in root.GetComponentsInChildren<SkinnedMeshRenderer>()){
            var mesh=new Mesh();renderer.BakeMesh(mesh,false);
            foreach(var vertex in mesh.vertices){Vector3 point=root.transform.InverseTransformPoint(renderer.transform.TransformPoint(vertex));if(first){bounds=new Bounds(point,Vector3.zero);first=false;}else bounds.Encapsulate(point);}
            UnityEngine.Object.DestroyImmediate(mesh);
        }
        if(first)throw new Exception("Crawl model has no skinned geometry");return bounds;
    }
    static void InstallOn(BlindLungeMonster monster){
        if(monster.visual!=null)UnityEngine.Object.DestroyImmediate(monster.visual);
        var wrapper=new GameObject("Crawl Visual");wrapper.transform.SetParent(monster.transform,false);
        var model=(GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(ModelPath),monster.gameObject.scene);model.name="Crawl";model.transform.SetParent(wrapper.transform,false);
        foreach(var camera in model.GetComponentsInChildren<Camera>(true))UnityEngine.Object.DestroyImmediate(camera);
        foreach(var listener in model.GetComponentsInChildren<AudioListener>(true))UnityEngine.Object.DestroyImmediate(listener);
        foreach(var collider in model.GetComponentsInChildren<Collider>(true))UnityEngine.Object.DestroyImmediate(collider);
        var animator=model.GetComponent<Animator>();if(animator==null)animator=model.AddComponent<Animator>();animator.runtimeAnimatorController=controller;animator.applyRootMotion=false;animator.cullingMode=AnimatorCullingMode.AlwaysAnimate;
        foreach(var renderer in model.GetComponentsInChildren<SkinnedMeshRenderer>()){
            renderer.updateWhenOffscreen=true;var materials=renderer.sharedMaterials;
            for(int i=0;i<materials.Length;i++){string texture=materials[i].mainTexture!=null?AssetDatabase.GetAssetPath(materials[i].mainTexture):"";materials[i]=AssetDatabase.LoadAssetAtPath<Material>(Folder+(texture.Contains("1002")?"/CrawlSkin1002.mat":"/CrawlSkin1001.mat"));}renderer.sharedMaterials=materials;
        }
        float bottom=float.PositiveInfinity;
        for(int i=0;i<8;i++){clip.SampleAnimation(model,clip.length*i/8);bottom=Mathf.Min(bottom,BakeBounds(model).min.y);}
        wrapper.transform.localPosition=Vector3.up*(-bottom+.015f);clip.SampleAnimation(model,0);
        var driver=monster.GetComponent<BlindCrawlAnimator>();if(driver==null)driver=monster.gameObject.AddComponent<BlindCrawlAnimator>();driver.animator=animator;driver.crawlClip=clip;monster.visual=wrapper;
        EditorUtility.SetDirty(monster);EditorUtility.SetDirty(driver);
    }
    [MenuItem("Tools/Blind Lunge/Install Crawl Model")]
    public static void Install(){
        if(EditorApplication.isPlayingOrWillChangePlaymode)throw new Exception("Stop Play Mode before installing the model.");
        PrepareAssets();
        var contents=PrefabUtility.LoadPrefabContents(PrefabPath);
        try{InstallOn(contents.GetComponent<BlindLungeMonster>());PrefabUtility.SaveAsPrefabAsset(contents,PrefabPath);}finally{PrefabUtility.UnloadPrefabContents(contents);}
        foreach(int night in new[]{3,4,5}){
            var scene=EditorSceneManager.OpenScene("Assets/Scenes/Night "+night+".unity");
            var monsters=scene.GetRootGameObjects().SelectMany(root=>root.GetComponentsInChildren<BlindLungeMonster>(true)).ToArray();if(monsters.Length!=1)throw new Exception("Expected one lunge monster in Night "+night);
            InstallOn(monsters[0]);EditorSceneManager.MarkSceneDirty(scene);EditorSceneManager.SaveScene(scene);
        }
        AssetDatabase.SaveAssets();Validate();
    }
    public static void Validate(){
        clip=AssetDatabase.LoadAssetAtPath<AnimationClip>(ClipPath);if(clip==null||!AnimationUtility.GetAnimationClipSettings(clip).loopTime)throw new Exception("Crawl does not loop");
        int objects=0;var root=PrefabUtility.LoadPrefabContents(PrefabPath);try{ValidateMonster(root.GetComponent<BlindLungeMonster>());objects++;}finally{PrefabUtility.UnloadPrefabContents(root);}
        foreach(int night in new[]{3,4,5}){
            var scene=EditorSceneManager.OpenScene("Assets/Scenes/Night "+night+".unity");foreach(var monster in scene.GetRootGameObjects().SelectMany(go=>go.GetComponentsInChildren<BlindLungeMonster>(true))){ValidateMonster(monster);objects++;}
        }
        new NetworkSceneIdentityBuildCheck().OnPreprocessBuild(null);File.WriteAllText("Logs/BlindCrawlAssets.txt","PASS "+objects+" lunge prefab/scene visuals, looping clip, bone bindings, textures, original collider and canonical scene identities.");
    }
    static void ValidateMonster(BlindLungeMonster monster){
        var driver=monster.GetComponent<BlindCrawlAnimator>();if(driver==null||driver.animator==null||driver.crawlClip!=clip||driver.animator.applyRootMotion)throw new Exception("Crawl visual controller missing or root motion active");
        foreach(var binding in AnimationUtility.GetCurveBindings(clip))if(binding.path!=""&&driver.animator.transform.Find(binding.path)==null)throw new Exception("Unbound crawl bone: "+binding.path);
        if(monster.visual.GetComponentsInChildren<TMPro.TextMeshPro>(true).Length!=0||monster.visual.GetComponentsInChildren<Collider>(true).Length!=0)throw new Exception("Old cube labels or imported visual colliders remain");
        foreach(var renderer in monster.visual.GetComponentsInChildren<SkinnedMeshRenderer>())foreach(var material in renderer.sharedMaterials)if(material==null||material.mainTexture==null||material.GetTexture("_BumpMap")==null)throw new Exception("Crawl skin texture/normal missing");
        var motor=monster.GetComponent<CharacterController>();if(Mathf.Abs(motor.height-2.4f)>.001f||Mathf.Abs(motor.radius-.65f)>.001f)throw new Exception("Gameplay collider changed");
    }
    public static void Render(){
        clip=AssetDatabase.LoadAssetAtPath<AnimationClip>(ClipPath);EditorSceneManager.NewScene(NewSceneSetup.EmptyScene);
        var root=(GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath));var driver=root.GetComponent<BlindCrawlAnimator>();
        var view=new GameObject("Crawl preview",typeof(Camera)).GetComponent<Camera>();view.clearFlags=CameraClearFlags.SolidColor;view.backgroundColor=new Color(.08f,.09f,.11f);view.orthographic=true;
        var light=new GameObject("Preview light",typeof(Light)).GetComponent<Light>();light.type=LightType.Directional;light.intensity=1.3f;light.transform.rotation=Quaternion.Euler(45,-25,0);RenderSettings.ambientLight=Color.gray;
        foreach(float phase in new[]{0,.25f,.5f,.75f}){
            clip.SampleAnimation(driver.animator.gameObject,clip.length*phase);var baked=new List<GameObject>();
            foreach(var renderer in driver.animator.GetComponentsInChildren<SkinnedMeshRenderer>()){
                var mesh=new Mesh();renderer.BakeMesh(mesh,false);var visible=new GameObject("Pose preview",typeof(MeshFilter),typeof(MeshRenderer));visible.transform.SetPositionAndRotation(renderer.transform.position,renderer.transform.rotation);visible.transform.localScale=renderer.transform.lossyScale;visible.GetComponent<MeshFilter>().sharedMesh=mesh;visible.GetComponent<MeshRenderer>().sharedMaterials=renderer.sharedMaterials;renderer.enabled=false;baked.Add(visible);
            }
            Bounds bounds=baked[0].GetComponent<MeshRenderer>().bounds;foreach(var visible in baked)bounds.Encapsulate(visible.GetComponent<MeshRenderer>().bounds);view.orthographicSize=Mathf.Max(bounds.extents.y*1.4f,bounds.extents.x*.85f);view.transform.position=bounds.center+new Vector3(3,1.8f,4).normalized*5;view.transform.LookAt(bounds.center);
            var target=new RenderTexture(960,720,24);view.targetTexture=target;view.Render();RenderTexture.active=target;var image=new Texture2D(960,720,TextureFormat.RGB24,false);image.ReadPixels(new Rect(0,0,960,720),0,0);image.Apply();File.WriteAllBytes("Logs/BlindCrawl-"+phase.ToString("F2",System.Globalization.CultureInfo.InvariantCulture)+".png",image.EncodeToPNG());RenderTexture.active=null;view.targetTexture=null;target.Release();UnityEngine.Object.DestroyImmediate(target);UnityEngine.Object.DestroyImmediate(image);
            foreach(var visible in baked){UnityEngine.Object.DestroyImmediate(visible.GetComponent<MeshFilter>().sharedMesh);UnityEngine.Object.DestroyImmediate(visible);}foreach(var renderer in driver.animator.GetComponentsInChildren<SkinnedMeshRenderer>())renderer.enabled=true;
        }
    }
    public static void RebuildFixture(){
        EditorSceneManager.OpenScene("Assets/Scenes/MainMenu.unity");BlindLungeNetworkChecks.Build();
        if(!File.ReadAllText("Logs/BlindNetworkBuildResult.txt").StartsWith("Succeeded errors=0"))throw new Exception("Crawl fixture build failed");
    }
    public static void InstallAndBuild(){
        Install();Render();EditorSceneManager.OpenScene("Assets/Scenes/MainMenu.unity");BlindLungeSetup.Check();BlindLungeNetworkChecks.Build();
        if(!File.ReadAllText("Logs/BlindLungeChecksResult.txt").StartsWith("PASS")||!File.ReadAllText("Logs/BlindNetworkBuildResult.txt").StartsWith("Succeeded errors=0"))throw new Exception("Lunge checks/build failed");
        var report=BuildPipeline.BuildPlayer(new BuildPlayerOptions{scenes=EditorBuildSettings.scenes.Where(s=>s.enabled).Select(s=>s.path).ToArray(),target=BuildTarget.StandaloneWindows64,locationPathName="Logs/CrawlRelease/RedSand.exe",options=BuildOptions.Development});
        if(report.summary.result!=BuildResult.Succeeded||report.summary.totalErrors!=0)throw new Exception("Crawl production build failed");File.WriteAllText("Logs/BlindCrawlBuildResult.txt","PASS production and lunge multiplayer fixture built with zero errors.");
    }
}
#endif
