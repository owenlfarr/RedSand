using UnityEngine;
namespace Networking
{
    public sealed class ContinuousNightRun
    {
        int stage;
        public void Begin(string scene) { stage=(scene=="Night 1"||scene=="Night1_MultiplayerTest")?1:0; }
        public bool Arrive(string from,string to,bool hostSurvived)
        {
            if(!hostSurvived){stage=0;return false;}
            if(stage==1 && (from=="Night 1"||from=="Night1_MultiplayerTest") && to=="Night 2"){stage=2;return false;}
            bool earned=stage==2 && from=="Night 2" && to=="Night 3";
            stage=0;return earned;
        }
        public void Reset(){stage=0;}
    }
    public static class NightStartUnlock
    {
        const string Key="Campaign.HostNight2Unlocked.v1";
        const string Choice="Campaign.HostStartNight2.v1";
        public static bool IsUnlocked => PlayerPrefs.GetInt(Key,0)==1;
        public static bool StartOnNightTwo => IsUnlocked && PlayerPrefs.GetInt(Choice,0)==1;
        // This reward is a two-choice switch, never a highest-night selector.
        public static string StartingScene => StartOnNightTwo ? "Night 2" : "Night1_MultiplayerTest";
        public static void SelectNightTwo(bool value){if(!IsUnlocked)return;PlayerPrefs.SetInt(Choice,value?1:0);PlayerPrefs.Save();}
        public static void Grant(){
            if(IsUnlocked)return;
            PlayerPrefs.SetInt(Key,1);PlayerPrefs.Save();
            var canvas=new GameObject("Host Night 2 Unlock",typeof(Canvas),typeof(UnityEngine.UI.CanvasScaler));
            var c=canvas.GetComponent<Canvas>();c.renderMode=RenderMode.ScreenSpaceOverlay;c.sortingOrder=900;
            var scaler=canvas.GetComponent<UnityEngine.UI.CanvasScaler>();scaler.uiScaleMode=UnityEngine.UI.CanvasScaler.ScaleMode.ScaleWithScreenSize;scaler.referenceResolution=new Vector2(1920,1080);
            var label=new GameObject("Unlock message",typeof(RectTransform),typeof(TMPro.TextMeshProUGUI));label.transform.SetParent(canvas.transform,false);
            var text=label.GetComponent<TMPro.TextMeshProUGUI>();text.text="HOST UNLOCKED: NIGHT 2 START\nChoose your starting night in future lobbies.";text.raycastTarget=false;text.fontSize=26;text.alignment=TMPro.TextAlignmentOptions.Center;
            var rt=label.GetComponent<RectTransform>();rt.anchorMin=new Vector2(.15f,.78f);rt.anchorMax=new Vector2(.85f,.92f);rt.offsetMin=rt.offsetMax=Vector2.zero;
            Object.Destroy(canvas,7);
        }
    }
}
