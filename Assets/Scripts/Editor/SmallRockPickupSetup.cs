#if UNITY_EDITOR
using System;
using System.IO;
using System.Linq;
using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;

public static class SmallRockPickupSetup
{
    public const string PrefabPath="Assets/Prefabs/SmallDistractionRock.prefab";
    [MenuItem("Tools/Blind Lunge/Install Small Rock Pickups")]
    public static void Install()
    {
        var scene=EditorSceneManager.OpenScene("Assets/Scenes/Night 3.unity");
        if(UnityEngine.Object.FindObjectsByType<SmallRockPickup>(FindObjectsSortMode.None).Length>0)return;
        const string materialPath="Assets/Art/SmallDistractionRock.mat";
        var material=AssetDatabase.LoadAssetAtPath<Material>(materialPath);
        if(material==null){material=new Material(Shader.Find("Standard")){color=new Color(.34f,.25f,.18f)};AssetDatabase.CreateAsset(material,materialPath);}
        var template=GameObject.CreatePrimitive(PrimitiveType.Sphere);template.name="Small distraction rock";
        template.transform.localScale=new Vector3(.24f,.16f,.2f);
        template.GetComponent<Renderer>().sharedMaterial=material;
        var collider=template.GetComponent<SphereCollider>();collider.isTrigger=true;collider.radius=1.5f;
        template.AddComponent<SmallRockPickup>();
        var prefab=PrefabUtility.SaveAsPrefabAsset(template,PrefabPath);UnityEngine.Object.DestroyImmediate(template);
        var physics=TerrainRockCollisions.Install(scene);
        var group=new GameObject("Small rock pickups");
        var monster=UnityEngine.Object.FindFirstObjectByType<BlindLungeMonster>();
        var entrance=UnityEngine.Object.FindFirstObjectByType<RushMonsterEvent>().exteriorWarningLight.transform.position;
        for(int id=0;id<16;id++)
        {
            Vector3 seed;
            if(id<8){float angle=id*Mathf.PI/4;seed=entrance+new Vector3(Mathf.Cos(angle),0,Mathf.Sin(angle))*(6+id%3*3);}
            else if(id<14){float angle=(id-8)*Mathf.PI/3;seed=monster.transform.position+new Vector3(Mathf.Cos(angle),0,Mathf.Sin(angle))*9;}
            else seed=Vector3.Lerp(entrance,monster.transform.position,(id-13)/3f);
            var item=(GameObject)PrefabUtility.InstantiatePrefab(prefab,scene);
            item.name="Small rock "+(id+1);item.transform.SetParent(group.transform,true);item.transform.position=FindOpenGround(seed);item.transform.rotation=Quaternion.Euler(0,id*137.5f,0);
            item.GetComponent<SmallRockPickup>().pickupId=id;
        }
        UnityEngine.Object.DestroyImmediate(physics.gameObject);
        EditorSceneManager.MarkSceneDirty(scene);EditorSceneManager.SaveScene(scene);AssetDatabase.SaveAssets();
        new NetworkSceneIdentityBuildCheck().OnPreprocessBuild(null);
        File.WriteAllText("Logs/SmallRockPickupSetupResult.txt","PASS: sixteen small-rock pickups on unobstructed ground near the entrance, Blind Lunge and connecting route. Production clocks unchanged.");
    }
    static Vector3 FindOpenGround(Vector3 seed)
    {
        for(int attempt=0;attempt<256;attempt++)
        {
            float angle=attempt*2.399963f;float radius=attempt==0?0:Mathf.Sqrt(attempt)*1.3f;
            Vector3 point=seed+new Vector3(Mathf.Cos(angle)*radius,0,Mathf.Sin(angle)*radius);
            foreach(var terrain in Terrain.activeTerrains)
            {
                Vector3 start=terrain.transform.position,size=terrain.terrainData.size;
                if(point.x<start.x||point.z<start.z||point.x>start.x+size.x||point.z>start.z+size.z)continue;
                point.y=terrain.SampleHeight(point)+start.y;
                var hits=Physics.RaycastAll(point+Vector3.up*200,Vector3.down,210,~0,QueryTriggerInteraction.Ignore)
                    .Where(hit=>hit.collider.GetComponentInParent<PlayerController>()==null&&!(hit.collider is CharacterController)).OrderBy(hit=>hit.distance).ToArray();
                if(hits.Length>0&&hits[0].collider is TerrainCollider&&hits[0].normal.y>.7f)return hits[0].point+Vector3.up*.12f;
            }
        }
        throw new Exception("No open ground for a small rock near "+seed);
    }
    public static void PrepareAndBuildNetworkChecks(){Install();BlindLungeNetworkChecks.Build();}
}
#endif
