using UnityEngine;
using UnityEngine.UI;
using Unity.Netcode;
using TMPro;

// Local input and presentation only. The monster's host validates searches and throws.
public sealed class RockThrower:MonoBehaviour
{
    public int OfflineCount {get;set;}
    PlayerController player;
    NetworkPlayerMovementState state;
    Canvas hud;TMP_Text message,hint;RawImage preview;
    GameObject previewRoot;Transform rock;Camera previewCamera;RenderTexture texture;Material material;
    float nextSearch,searchStarted,messageUntil;bool searching;
    bool Online=>NetworkManager.Singleton!=null&&NetworkManager.Singleton.IsListening;
    bool Local=>!Online||player.IsSpawned&&player.IsOwner;
    public int Carried=>Online&&state!=null?state.CarriedDistractionRocks.Value:OfflineCount;
    void Awake(){player=GetComponent<PlayerController>();state=GetComponent<NetworkPlayerMovementState>();}
    bool Living(){var life=GetComponent<OxygenSystem>();var networkLife=GetComponent<NetworkOxygenState>();return (life==null||!life.IsDead())&&(!Online||networkLife!=null&&!networkLife.IsDead.Value);}
    void Update(){
        if(!Local)return;
        bool available=BlindLungeMonster.Instance!=null&&(!Online||BlindLungeMonster.Instance.IsSpawned)&&Living()&&player.enabled;
        if(hud==null&&available)BuildHud();if(hud==null)return;
        hud.gameObject.SetActive(available&&!PlayerSettingsUI.IsGameMenuOpen);if(!available)return;
        if(previewRoot!=null)previewRoot.SetActive(Carried>0);
        preview.gameObject.SetActive(Carried>0);
        if(rock!=null)rock.Rotate(0,35*Time.unscaledDeltaTime,0);
        var view=player.playerCamera;if(view==null)return;
        bool ground=BlindLungeMonster.GroundInReach(player,view.forward);
        hint.text=Carried>0?"SMALL ROCK  /  CLICK TO THROW":searching?"":ground?(Time.time<nextSearch?"SEARCH COOLDOWN":"[E] SEARCH FOR A SMALL ROCK"):"";
        float alpha=searching?1:Mathf.Clamp01((messageUntil-Time.unscaledTime)/.7f);message.alpha=alpha;
        if(searching&&Time.time-searchStarted>4){searching=false;message.text="Search interrupted";messageUntil=Time.unscaledTime+2;}
        if(PlayerSettingsUI.IsGameMenuOpen||Time.timeScale<=0||Cursor.lockState!=CursorLockMode.Locked)return;
        if(Input.GetKeyDown(KeyCode.E)&&ground&&!searching&&Carried==0&&Time.time>=nextSearch){
            nextSearch=Time.time+BlindLungeMonster.SearchCooldown;searchStarted=Time.time;searching=true;message.text="Searching...";message.alpha=1;
            BlindLungeMonster.Instance.RequestSearch(player,view.forward);
        }
        if(Input.GetMouseButtonDown(0)&&Carried>0&&!searching)BlindLungeMonster.Instance.RequestThrow(player,view.forward);
    }
    public void ShowResult(int result){searching=false;messageUntil=Time.unscaledTime+2.5f;if(message==null)return;message.text=result==1?"Found one small rock":result==0?"Found nothing":result==2?"Look at nearby ground":result==3?"Wait before searching again":"Already holding a small rock";message.alpha=1;}
    void BuildHud(){
        var go=new GameObject("Small rock HUD",typeof(Canvas),typeof(CanvasScaler));hud=go.GetComponent<Canvas>();hud.renderMode=RenderMode.ScreenSpaceOverlay;hud.sortingOrder=35;go.transform.SetParent(transform,false);
        var scaler=go.GetComponent<CanvasScaler>();scaler.uiScaleMode=CanvasScaler.ScaleMode.ScaleWithScreenSize;scaler.referenceResolution=new Vector2(1920,1080);
        message=Label(go.transform,"Search result",new Vector2(300,150),new Vector2(520,44),24);
        hint=Label(go.transform,"Rock controls",new Vector2(300,50),new Vector2(520,40),20);
        var image=new GameObject("Held rock 3D preview",typeof(RectTransform),typeof(RawImage));image.transform.SetParent(go.transform,false);preview=image.GetComponent<RawImage>();preview.raycastTarget=false;
        var rect=image.GetComponent<RectTransform>();rect.anchorMin=rect.anchorMax=Vector2.zero;rect.pivot=Vector2.zero;rect.anchoredPosition=new Vector2(35,75);rect.sizeDelta=new Vector2(110,110);
        texture=new RenderTexture(160,160,16,RenderTextureFormat.ARGB32);texture.Create();preview.texture=texture;
        previewRoot=new GameObject("Held rock preview studio");previewRoot.transform.position=Vector3.down*20000;
        var model=GameObject.CreatePrimitive(PrimitiveType.Sphere);Destroy(model.GetComponent<Collider>());model.name="Small rock model";model.transform.SetParent(previewRoot.transform,false);model.layer=31;model.transform.localScale=new Vector3(.65f,.4f,.5f);rock=model.transform;
        var shader=Shader.Find("Standard");material=new Material(shader);material.color=new Color(.34f,.25f,.18f);model.GetComponent<Renderer>().material=material;
        var camera=new GameObject("Rock preview camera");camera.transform.SetParent(previewRoot.transform,false);camera.transform.localPosition=new Vector3(0,.1f,-2);previewCamera=camera.AddComponent<Camera>();previewCamera.targetTexture=texture;previewCamera.cullingMask=1<<31;previewCamera.clearFlags=CameraClearFlags.SolidColor;previewCamera.backgroundColor=Color.clear;previewCamera.orthographic=true;previewCamera.orthographicSize=.55f;previewCamera.nearClipPlane=.1f;previewCamera.farClipPlane=5;
        var lamp=new GameObject("Rock preview light");lamp.transform.SetParent(previewRoot.transform,false);lamp.transform.localPosition=new Vector3(-.5f,1,-1);var light=lamp.AddComponent<Light>();light.type=LightType.Point;light.intensity=3;light.range=4;light.cullingMask=1<<31;
        previewRoot.SetActive(false);
    }
    static TMP_Text Label(Transform parent,string name,Vector2 position,Vector2 size,float font){var go=new GameObject(name,typeof(RectTransform),typeof(TextMeshProUGUI));go.transform.SetParent(parent,false);var text=go.GetComponent<TextMeshProUGUI>();text.fontSize=font;text.raycastTarget=false;text.alignment=TextAlignmentOptions.Left;var rt=go.GetComponent<RectTransform>();rt.anchorMin=rt.anchorMax=Vector2.zero;rt.pivot=Vector2.zero;rt.anchoredPosition=position;rt.sizeDelta=size;return text;}
    void OnDisable(){searching=false;if(hud!=null)hud.gameObject.SetActive(false);if(previewRoot!=null)previewRoot.SetActive(false);}
    void OnDestroy(){if(hud!=null)Destroy(hud.gameObject);if(previewRoot!=null)Destroy(previewRoot);if(texture!=null){texture.Release();Destroy(texture);}if(material!=null)Destroy(material);}
}
