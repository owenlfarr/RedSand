#if UNITY_EDITOR
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEditor.Build.Reporting;
public static class LocalPersistenceCheckBuild
{
    public static void Build()
    {
        EditorSceneManager.OpenScene("Assets/Scenes/MainMenu.unity");
        BlindLungeNetworkChecks.Build();
        string company=PlayerSettings.companyName,product=PlayerSettings.productName;
        try {
            PlayerSettings.companyName="RedSandVerification";PlayerSettings.productName="LocalSaveRegression";
            var scene=EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);
            const string path="Assets/_Project/Tests/LocalSaveTest.unity";EditorSceneManager.SaveScene(scene,path);
            var report=BuildPipeline.BuildPlayer(new BuildPlayerOptions{scenes=new[]{path},target=BuildTarget.StandaloneWindows64,options=BuildOptions.Development,locationPathName="Logs/LocalSaveBuild/LocalSaveTest.exe"});
            File.WriteAllText("Logs/LocalSaveBuildResult.txt",report.summary.result+" errors="+report.summary.totalErrors);
        }finally{PlayerSettings.companyName=company;PlayerSettings.productName=product;}
    }
}
#endif
