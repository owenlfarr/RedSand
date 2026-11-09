#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System;
using System.Collections;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEngine;
using UnityEngine.UI;
using Networking;

// Opt-in restart regression, built under a separate company/product identity.
public sealed class LocalPreferencesPersistenceTest : MonoBehaviour
{
    string phase;
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Bootstrap(){var args=Environment.GetCommandLineArgs();int at=Array.IndexOf(args,"-localSaveTest");if(at<0||at+1>=args.Length)return;var test=new GameObject("Local save restart check").AddComponent<LocalPreferencesPersistenceTest>();test.phase=args[at+1];}
    IEnumerator Start()
    {
        yield return null;
        try
        {
            Check(Application.companyName=="RedSandVerification"&&Application.productName=="LocalSaveRegression","isolated test save identity");
            var ui=FindFirstObjectByType<PlayerSettingsUI>();
            typeof(PlayerSettingsUI).GetMethod("BuildGameMenu",BindingFlags.NonPublic|BindingFlags.Instance).Invoke(ui,null);
            var panel=(GameObject)typeof(PlayerSettingsUI).GetField("gameSettings",BindingFlags.NonPublic|BindingFlags.Instance).GetValue(ui);
            var sensitivity=panel.GetComponentsInChildren<Slider>(true).Single(s=>s.name=="SensitivitySlider");
            var volume=panel.GetComponentsInChildren<Slider>(true).Single(s=>s.name=="GameVolumeSlider");
            var buttons=panel.GetComponentsInChildren<Button>(true);
            if(phase=="write")
            {
                PlayerPrefs.SetInt("Campaign.HostNight2Unlocked.v1",0);PlayerPrefs.SetInt("Campaign.HostStartNight2.v1",0);
                NightStartUnlock.Grant();NightStartUnlock.SelectNightTwo(true);
                sensitivity.SetValueWithoutNotify(2);sensitivity.value=3.27f;
                volume.SetValueWithoutNotify(1);volume.value=.37f;
                var vsync=buttons.Single(b=>b.name=="VSYNCButton");vsync.onClick.Invoke();if(QualitySettings.vSyncCount!=0)vsync.onClick.Invoke();
                var cap=buttons.Single(b=>b.name=="FPS LIMITButton");for(int step=0;step<8&&Application.targetFrameRate!=120;step++)cap.onClick.Invoke();
                var fps=buttons.Single(b=>b.name=="FPS COUNTERButton");fps.onClick.Invoke();if(PlayerPrefs.GetInt("RedSand.Settings.FPS",0)!=1)fps.onClick.Invoke();
                var apply=typeof(PlayerSettingsUI).GetMethod("ApplyDisplay",BindingFlags.NonPublic|BindingFlags.Instance);apply.Invoke(ui,null);apply.Invoke(ui,null);
                Check(NightStartUnlock.IsUnlocked&&NightStartUnlock.StartOnNightTwo,"unlock and chosen start saved immediately");
                File.WriteAllText("Logs/LocalSave-write-PASS.txt","PASS actual unlock grant, starting-night selector and settings UI save handlers. Separate read process must verify persistence.");
            }
            else
            {
                Check(NightStartUnlock.IsUnlocked&&NightStartUnlock.StartingScene=="Night 2","earned unlock and starting-night choice survive process restart");
                Check(Mathf.Abs(sensitivity.value-3.27f)<.01f,"sensitivity restored");
                Check(Mathf.Abs(volume.value-.37f)<.01f&&Mathf.Abs(AudioListener.volume-.37f)<.01f,"volume restored and applied");
                Check(QualitySettings.vSyncCount==0&&Application.targetFrameRate==120,"VSync and frame cap restored and applied");
                Check(PlayerPrefs.GetInt("RedSand.Settings.FPS",0)==1,"FPS counter choice survives restart");
                Check(PlayerPrefs.HasKey("RedSand.Settings.Width")&&PlayerPrefs.HasKey("RedSand.Settings.Height")&&PlayerPrefs.HasKey("RedSand.Settings.Mode"),"confirmed display settings saved");
                File.WriteAllText("Logs/LocalSave-read-PASS.txt","PASS unlock, starting-night choice, sensitivity, volume, VSync, frame cap, FPS toggle and confirmed display preferences persist across separate application launches. Real player preferences were not touched.");
            }
            Application.Quit(0);
        }
        catch(Exception error){File.WriteAllText("Logs/LocalSave-"+phase+"-FAIL.txt",error.ToString());Debug.LogException(error);Application.Quit(2);}
    }
    static void Check(bool condition,string message){if(!condition)throw new Exception(message);}
}
#endif
