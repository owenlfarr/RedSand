using UnityEngine;
using UnityEngine.UI;
using Unity.Netcode;
using TMPro;

// Local input and presentation only. The monster's host validates searches and throws.
public sealed class RockThrower:MonoBehaviour
{
    int offlineCount;
    public int OfflineCount {get=>offlineCount;set=>offlineCount=Mathf.Clamp(value,0,1);}
    PlayerController player;
    NetworkPlayerMovementState state;
    Canvas hud;TMP_Text message,hint;
    GameObject heldRock;
    Camera heldCamera;
    Camera worldCamera;
    bool restoreHeldLayer;
    float nextSearch,searchStarted,messageUntil,nextSearchHold;bool searching;
    bool Online=>NetworkManager.Singleton!=null&&NetworkManager.Singleton.IsListening;
    bool Local=>!Online||player.IsSpawned&&player.IsOwner;
    public int Carried=>Online&&state!=null?state.CarriedDistractionRocks.Value:OfflineCount;
    void Awake(){player=GetComponent<PlayerController>();state=GetComponent<NetworkPlayerMovementState>();}
    bool Living(){var life=GetComponent<OxygenSystem>();var networkLife=GetComponent<NetworkOxygenState>();return (life==null||!life.IsDead())&&(!Online||networkLife!=null&&!networkLife.IsDead.Value);}
    void Update(){
        if(!Local){if(heldCamera!=null)heldCamera.gameObject.SetActive(false);if(heldRock!=null)heldRock.SetActive(false);if(hud!=null)hud.gameObject.SetActive(false);return;}
        bool available=BlindLungeMonster.Instance!=null&&(!Online||BlindLungeMonster.Instance.IsSpawned)&&Living()&&player.enabled;
        if(!available){CancelSearch();if(hud!=null)hud.gameObject.SetActive(false);if(heldRock!=null)heldRock.SetActive(false);if(heldCamera!=null)heldCamera.gameObject.SetActive(false);return;}
        if(hud==null&&available)BuildHud();if(hud==null)return;
        hud.gameObject.SetActive(available&&!PlayerSettingsUI.IsGameMenuOpen);if(!available)return;
        UpdateHeldRock();
        var view=player.playerCamera;if(view==null)return;
        bool ground=BlindLungeMonster.GroundInReach(player,view.forward);
        hint.text=Carried>0?"SMALL ROCK  /  LEFT CLICK TO THROW":searching?$"HOLD [E]  /  SEARCHING {Mathf.Clamp01((Time.time-searchStarted)/BlindLungeMonster.SearchDuration)*100:F0}%":ground?(Time.time<nextSearch?"SEARCH COOLDOWN":"HOLD [E] TO SEARCH GROUND"):view.forward.y<-.2f?"SEARCH ONLY ON PLANET SURFACE":"";
        float alpha=searching?1:Mathf.Clamp01((messageUntil-Time.unscaledTime)/.7f);message.alpha=alpha;
        if(searching&&Time.time-searchStarted>4)CancelSearch();
        if(PlayerSettingsUI.IsGameMenuOpen||Time.timeScale<=0||Cursor.lockState!=CursorLockMode.Locked){CancelSearch();return;}
        if(searching){
            if(!Input.GetKey(KeyCode.E)||!ground)CancelSearch();
            else if(Time.time>=nextSearchHold){nextSearchHold=Time.time+.12f;BlindLungeMonster.Instance.UpdateSearchHold(player,view.forward,true);}
        }
        if(Input.GetKeyDown(KeyCode.E)&&ground&&!searching&&Carried==0&&Time.time>=nextSearch){
            nextSearch=Time.time+BlindLungeMonster.SearchCooldown;nextSearchHold=Time.time+.12f;searchStarted=Time.time;searching=true;message.text="Searching...";message.alpha=1;
            BlindLungeMonster.Instance.RequestSearch(player,view.forward);
        }
        if(Input.GetMouseButtonDown(0)&&Carried>0&&!searching)BlindLungeMonster.Instance.RequestThrow(player,view.forward);
    }
    void CancelSearch(){if(!searching)return;searching=false;BlindLungeMonster.Instance?.UpdateSearchHold(player,Vector3.zero,false);ShowResult(5);}
    public void ShowResult(int result){searching=false;messageUntil=Time.unscaledTime+2.5f;if(message==null)return;message.text=result==1?"Found one small rock":result==0?"Nothing found":result==2?"Search only on the planet surface":result==3?"Wait before searching again":result==5?"Search interrupted":"Already holding a small rock";message.alpha=1;}
    void BuildHud(){
        var go=new GameObject("Small rock HUD",typeof(Canvas),typeof(CanvasScaler));hud=go.GetComponent<Canvas>();hud.renderMode=RenderMode.ScreenSpaceOverlay;hud.sortingOrder=35;go.transform.SetParent(transform,false);
        var scaler=go.GetComponent<CanvasScaler>();scaler.uiScaleMode=CanvasScaler.ScaleMode.ScaleWithScreenSize;scaler.referenceResolution=new Vector2(1920,1080);
        message=Label(go.transform,"Search result",new Vector2(300,150),new Vector2(520,44),24);
        hint=Label(go.transform,"Rock controls",new Vector2(300,50),new Vector2(520,40),20);
    }
    void UpdateHeldRock(){
        bool visible=Carried>0&&!PlayerSettingsUI.IsGameMenuOpen&&Time.timeScale>0;
        if(heldRock==null&&visible&&player.playerCamera!=null){
            heldRock=SmallRockModel.Create(player.playerCamera);
            heldRock.name="Physical held rock";
            foreach(var renderer in heldRock.GetComponentsInChildren<Renderer>()){
                renderer.gameObject.layer=31;
                renderer.shadowCastingMode=UnityEngine.Rendering.ShadowCastingMode.Off;
                renderer.receiveShadows=false;
            }
            // Render the physical first-person item after world fog, like a weapon
            // view. This keeps it readable at night and outside nearby walls.
            var cameraObject=new GameObject("Held rock view camera");
            cameraObject.transform.SetParent(player.playerCamera,false);
            heldCamera=cameraObject.AddComponent<Camera>();heldCamera.cullingMask=1<<31;
            worldCamera=player.playerCamera.GetComponent<Camera>();
            restoreHeldLayer=worldCamera!=null&&(worldCamera.cullingMask&(1<<31))!=0;
            heldCamera.clearFlags=CameraClearFlags.Depth;heldCamera.nearClipPlane=.01f;heldCamera.farClipPlane=2;
            var lamp=new GameObject("Held rock soft light");lamp.transform.SetParent(cameraObject.transform,false);
            lamp.transform.localPosition=new Vector3(-.2f,.15f,.15f);
            var light=lamp.AddComponent<Light>();light.type=LightType.Point;light.range=1.5f;light.intensity=2.5f;light.color=new Color(1,.88f,.76f);light.cullingMask=1<<31;
        }
        if(heldRock==null)return;
        heldRock.SetActive(visible);
        heldCamera.gameObject.SetActive(visible);
        if(!visible)return;
        var camera=player.playerCamera.GetComponent<Camera>();
        if(camera==null){heldRock.SetActive(false);heldCamera.gameObject.SetActive(false);return;}
        camera.cullingMask&=~(1<<31);
        heldCamera.fieldOfView=camera.fieldOfView;heldCamera.aspect=camera.aspect;heldCamera.rect=camera.rect;heldCamera.depth=camera.depth+1;
        heldCamera.allowHDR=camera.allowHDR;heldCamera.targetDisplay=camera.targetDisplay;
        float moving=player.GetMoveInput().magnitude;
        float bob=Mathf.Sin(Time.time*7)*.006f*moving;
        heldRock.transform.position=camera.ViewportToWorldPoint(new Vector3(.16f,.18f,.5f))+camera.transform.up*bob;
        heldRock.transform.rotation=camera.transform.rotation*Quaternion.Euler(58,-25,8+Mathf.Sin(Time.time*3)*moving);
        // Camera parents may inherit the player body's nonuniform scale.
        Vector3 scale=player.playerCamera.lossyScale;
        heldRock.transform.localScale=new Vector3(1/scale.x,1/scale.y,1/scale.z);
    }
    static TMP_Text Label(Transform parent,string name,Vector2 position,Vector2 size,float font){var go=new GameObject(name,typeof(RectTransform),typeof(TextMeshProUGUI));go.transform.SetParent(parent,false);var text=go.GetComponent<TextMeshProUGUI>();text.fontSize=font;text.raycastTarget=false;text.alignment=TextAlignmentOptions.Left;var rt=go.GetComponent<RectTransform>();rt.anchorMin=rt.anchorMax=Vector2.zero;rt.pivot=Vector2.zero;rt.anchoredPosition=position;rt.sizeDelta=size;return text;}
    void RestoreCamera(){if(worldCamera!=null&&restoreHeldLayer)worldCamera.cullingMask|=1<<31;}
    void OnDisable(){CancelSearch();if(hud!=null)hud.gameObject.SetActive(false);if(heldRock!=null)heldRock.SetActive(false);if(heldCamera!=null)heldCamera.gameObject.SetActive(false);RestoreCamera();}
    void OnDestroy(){RestoreCamera();if(hud!=null)Destroy(hud.gameObject);if(heldRock!=null)Destroy(heldRock);if(heldCamera!=null)Destroy(heldCamera.gameObject);}
}
