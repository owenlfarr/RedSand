using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

[InitializeOnLoad]
public static class ResizePaintedRocks
{
    const string Request="Logs/TerrainRockResize-20261106/request.txt";
    const string Result="Logs/TerrainRockResize-20261106/result.json";
    [Serializable] public class Change { public string terrain; public int index; public string prefab; public float multiplier,oldWidth,oldHeight,newWidth,newHeight; }
    [Serializable] public class Report { public int changed; public List<Change> instances=new List<Change>(); }
    static ResizePaintedRocks() { EditorApplication.delayCall+=RunRequested; }
    static void RunRequested()
    {
        if(!File.Exists(Request)||File.Exists(Result))return;
        if(EditorApplication.isPlayingOrWillChangePlaymode||EditorApplication.isCompiling||EditorApplication.isUpdating) {EditorApplication.delayCall+=RunRequested;return;}
        var report=new Report();var random=new System.Random(202611061);
        foreach(string guid in AssetDatabase.FindAssets("t:TerrainData"))
        {
            string path=AssetDatabase.GUIDToAssetPath(guid);
            var data=AssetDatabase.LoadAssetAtPath<TerrainData>(path);if(data==null)continue;
            var prototypes=data.treePrototypes;var instances=data.treeInstances;
            var rockIndices=new HashSet<int>();
            for(int i=0;i<prototypes.Length;i++)
            {
                var prefab=prototypes[i].prefab;if(prefab==null)continue;
                string p=AssetDatabase.GetAssetPath(prefab);
                if(p.StartsWith("Assets/Prefabs/Rocks/")||p.StartsWith("Assets/Prefabs/MarsTerrainRocks/"))rockIndices.Add(i);
            }
            if(rockIndices.Count==0)continue;
            bool modified=false;Undo.RecordObject(data,"Randomize painted rock sizes 1.8x to 5x");
            for(int i=0;i<instances.Length;i++)
            {
                var tree=instances[i];if(!rockIndices.Contains(tree.prototypeIndex))continue;
                float multiplier=1.8f+(float)random.NextDouble()*3.2f;
                var change=new Change{terrain=path,index=i,prefab=prototypes[tree.prototypeIndex].prefab.name,multiplier=multiplier,oldWidth=tree.widthScale,oldHeight=tree.heightScale,newWidth=tree.widthScale*multiplier,newHeight=tree.heightScale*multiplier};
                tree.widthScale=change.newWidth;tree.heightScale=change.newHeight;instances[i]=tree;
                report.instances.Add(change);report.changed++;modified=true;
            }
            if(!modified)continue;
            File.WriteAllText("Logs/TerrainRockResize-20261106/before-instances.json",JsonUtility.ToJson(new SavedInstances{instances=data.treeInstances},true));
            data.treeInstances=instances;EditorUtility.SetDirty(data);AssetDatabase.SaveAssetIfDirty(data);
            foreach(var terrain in Terrain.activeTerrains)if(terrain.terrainData==data)terrain.Flush();
            var verify=data.treeInstances;
            foreach(var c in report.instances)if(c.terrain==path)
                if(Mathf.Abs(verify[c.index].widthScale-c.newWidth)>.001f||Mathf.Abs(verify[c.index].heightScale-c.newHeight)>.001f)throw new Exception("Rock resize verification failed");
        }
        File.WriteAllText(Result,JsonUtility.ToJson(report,true));
        File.Delete(Request);SceneView.RepaintAll();Debug.Log("PAINTED_ROCK_RESIZE_PASS: "+report.changed+" rocks scaled 1.8x?5x; positions and rotations retained.");
    }
    [Serializable] public class SavedInstances { public TreeInstance[] instances; }
}
