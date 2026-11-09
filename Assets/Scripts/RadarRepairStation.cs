using UnityEngine;
using UnityEngine.UI;
using Unity.Netcode;
using TMPro;

public static class RadarWearRules
{
    public static int NightNumber(string scene){var match=System.Text.RegularExpressions.Regex.Match(scene??"",@"^Night\s*(\d+)");return match.Success&&int.TryParse(match.Groups[1].Value,out int n)?Mathf.Max(1,n):1;}
    public static int Capacity(int night)=>Mathf.Max(1,20-2*Mathf.Clamp(night,1,1000));
    public static int MetalCost(int night)=>night<=4?10:15;
}

// Shares the authored solar repair point; flare repairs retain priority and their existing UI.
public sealed class RadarRepairStation:MonoBehaviour
{
    RadarSystem radar;SolarFlareSystem station;PlayerController player;
    CompassPointer compass;
    GameObject canvas;TMP_Text prompt;float nextHeartbeat;bool requested;
    bool Online=>NetworkManager.Singleton!=null&&NetworkManager.Singleton.IsListening;
    void Awake(){radar=GetComponent<RadarSystem>();}
    void Update(){
        if(Online&&!radar.IsSpawned)return;
        if(station==null)station=FindFirstObjectByType<SolarFlareSystem>();
        if(player==null)player=Online?NetworkManager.Singleton.LocalClient?.PlayerObject?.GetComponent<PlayerController>():FindFirstObjectByType<PlayerController>();
        if(compass==null)compass=FindFirstObjectByType<CompassPointer>();
        if(compass!=null&&station!=null&&station.fixPointTransform!=null){
            if(radar.UsesRemaining<=0&&!station.IsFlareActive&&(PowerSystem.Instance==null||!PowerSystem.Instance.IsPowerOut))compass.SetTarget(station.fixPointTransform,"RADAR REPAIR",true);
            else if(compass.HasTargetOverride(station.fixPointTransform,"RADAR REPAIR"))compass.ClearTargetOverride();
        }
        bool usable=station!=null&&radar.CanRepairAtStation(player)&&!PlayerSettingsUI.IsGameMenuOpen&&Time.timeScale>0;
        if(!usable){Release();if(canvas!=null)canvas.SetActive(false);return;}
        if(prompt==null)BuildPrompt();canvas.SetActive(true);
        ulong id=Online?NetworkManager.Singleton.LocalClientId:0;
        bool occupied=radar.MaintenanceOwner!=ulong.MaxValue&&radar.MaintenanceOwner!=id;
        if(!radar.NeedsMaintenance)prompt.text="SOLAR / RADAR REPAIR\nRadar operational";
        else if(occupied)prompt.text="SOLAR / RADAR REPAIR\nAnother player is repairing the radar";
        else if(radar.AvailableRepairMetal<radar.RepairMetalCost)prompt.text=$"RADAR MAINTENANCE\nNeed {radar.RepairMetalCost} metal (team has {radar.AvailableRepairMetal})";
        else if(radar.MaintenanceOwner==id)prompt.text=$"REPAIRING RADAR: {Mathf.Clamp01(radar.MaintenanceProgress/radar.MaintenanceSeconds)*100:F0}%\n{radar.RepairMetalCost} metal charged on completion";
        else prompt.text=$"RADAR MAINTENANCE\nHold [E] to repair ({radar.MaintenanceSeconds:F0}s) / {radar.RepairMetalCost} metal";
        bool held=!occupied&&radar.NeedsMaintenance&&radar.AvailableRepairMetal>=radar.RepairMetalCost&&Input.GetKey(KeyCode.E)&&Cursor.lockState==CursorLockMode.Locked;
        if(held&&Time.unscaledTime>=nextHeartbeat){radar.RequestMaintenance(true);requested=true;nextHeartbeat=Time.unscaledTime+.15f;}
        if(!held)Release();
    }
    void Release(){if(!requested)return;requested=false;radar.RequestMaintenance(false);}
    void BuildPrompt(){canvas=new GameObject("Solar and radar repair prompt",typeof(Canvas),typeof(CanvasScaler));canvas.transform.SetParent(transform,false);var c=canvas.GetComponent<Canvas>();c.renderMode=RenderMode.ScreenSpaceOverlay;c.sortingOrder=45;var scale=canvas.GetComponent<CanvasScaler>();scale.uiScaleMode=CanvasScaler.ScaleMode.ScaleWithScreenSize;scale.referenceResolution=new Vector2(1920,1080);var go=new GameObject("Repair details",typeof(RectTransform),typeof(TextMeshProUGUI));go.transform.SetParent(canvas.transform,false);prompt=go.GetComponent<TextMeshProUGUI>();prompt.fontSize=25;prompt.color=new Color(.75f,.95f,1);prompt.alignment=TextAlignmentOptions.Center;prompt.raycastTarget=false;var rt=go.GetComponent<RectTransform>();rt.anchorMin=rt.anchorMax=new Vector2(.5f,0);rt.pivot=new Vector2(.5f,0);rt.anchoredPosition=new Vector2(0,110);rt.sizeDelta=new Vector2(1100,120);}
    void OnDisable(){Release();if(canvas!=null)canvas.SetActive(false);}
    void OnDestroy(){if(canvas!=null)Destroy(canvas);}
}
