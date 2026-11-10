using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using Unity.Netcode;
using Unity.Collections;
using TMPro;

// Persistent local curtain, coordinated with reliable host messages and black-screen acknowledgements.
public sealed class NightTransition : MonoBehaviour
{
    const string BeginMessage="NightCurtain.Begin",ReadyMessage="NightCurtain.Ready",RevealMessage="NightCurtain.Reveal",CancelMessage="NightCurtain.Cancel",MenuMessage="NightCurtain.Menu";
    static NightTransition instance;
    public static bool BlocksGameplay => instance!=null&&instance.busy;
    public static bool IsNightScene(string scene)=>scene!=null&&scene.StartsWith("Night",StringComparison.OrdinalIgnoreCase);
    public static NightTransition Instance {get {Ensure();return instance;}}
    public static string NightTitle(string scene)=>IsNightScene(scene)?"NIGHT "+RadarWearRules.NightNumber(scene):"";
    public bool FullyBlack => curtain!=null&&curtain.color.a>=.999f;
    public string VisibleTitle => title!=null&&title.color.a>.01f?title.text:"";
    public int CompletedCovers {get;private set;}
    public int EightAmCards {get;private set;}
    public int NightCards {get;private set;}
    public bool AllPeersCovered {get;private set;}
    Image curtain;TMP_Text title;Camera loadingCamera;NetworkManager manager;CustomMessagingManager messages;
    Coroutine animation;readonly HashSet<ulong> covered=new HashSet<ulong>();
    int token,nextToken;string expected="";bool busy,localCovered,readySent,showEight,wasOnline,revealing;float nextBroadcast,coverStarted;
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]static void Ensure(){if(instance!=null)return;var root=new GameObject("Persistent Night Transition");DontDestroyOnLoad(root);instance=root.AddComponent<NightTransition>();}
    void Awake(){instance=this;Build();SceneManager.sceneLoaded+=SceneLoaded;}
    void Build(){
        // Keep a black rendering target alive between the outgoing scene camera
        // and the incoming owned player camera. No listener is attached here.
        var view=new GameObject("Transition black camera",typeof(Camera));view.transform.SetParent(transform,false);
        loadingCamera=view.GetComponent<Camera>();loadingCamera.clearFlags=CameraClearFlags.SolidColor;
        loadingCamera.backgroundColor=Color.black;loadingCamera.cullingMask=0;loadingCamera.depth=-100;
        loadingCamera.nearClipPlane=.01f;loadingCamera.farClipPlane=1;loadingCamera.enabled=false;
        var root=new GameObject("Transition Canvas",typeof(RectTransform),typeof(Canvas),typeof(CanvasScaler),typeof(GraphicRaycaster));root.transform.SetParent(transform,false);
        var canvas=root.GetComponent<Canvas>();canvas.renderMode=RenderMode.ScreenSpaceOverlay;canvas.sortingOrder=32760;
        var scaler=root.GetComponent<CanvasScaler>();scaler.uiScaleMode=CanvasScaler.ScaleMode.ScaleWithScreenSize;scaler.referenceResolution=new Vector2(1920,1080);scaler.screenMatchMode=CanvasScaler.ScreenMatchMode.Expand;
        var black=new GameObject("Black curtain",typeof(RectTransform),typeof(Image));black.transform.SetParent(root.transform,false);var rect=black.GetComponent<RectTransform>();rect.anchorMin=Vector2.zero;rect.anchorMax=Vector2.one;rect.offsetMin=rect.offsetMax=Vector2.zero;curtain=black.GetComponent<Image>();curtain.color=Color.clear;curtain.raycastTarget=false;
        var text=new GameObject("Centered night card",typeof(RectTransform),typeof(TextMeshProUGUI));text.transform.SetParent(root.transform,false);var tr=text.GetComponent<RectTransform>();tr.anchorMin=new Vector2(.1f,.35f);tr.anchorMax=new Vector2(.9f,.65f);tr.offsetMin=tr.offsetMax=Vector2.zero;
        title=text.GetComponent<TextMeshProUGUI>();title.font=TMP_Settings.defaultFontAsset;title.fontSize=80;title.alignment=TextAlignmentOptions.Center;title.color=Color.clear;title.raycastTarget=false;
    }
    void Bind(){
        var current=NetworkManager.Singleton;
        if(current==null||!current.IsListening||current.CustomMessagingManager==null)return;
        if(messages==current.CustomMessagingManager)return;
        Unbind();token=nextToken=0;readySent=false;covered.Clear();manager=current;messages=current.CustomMessagingManager;
        messages.RegisterNamedMessageHandler(BeginMessage,OnBegin);messages.RegisterNamedMessageHandler(ReadyMessage,OnReady);messages.RegisterNamedMessageHandler(RevealMessage,OnReveal);messages.RegisterNamedMessageHandler(CancelMessage,OnCancel);messages.RegisterNamedMessageHandler(MenuMessage,OnMenu);wasOnline=true;
    }
    void Unbind(){if(messages!=null){messages.UnregisterNamedMessageHandler(BeginMessage);messages.UnregisterNamedMessageHandler(ReadyMessage);messages.UnregisterNamedMessageHandler(RevealMessage);messages.UnregisterNamedMessageHandler(CancelMessage);messages.UnregisterNamedMessageHandler(MenuMessage);}messages=null;manager=null;}
    void Update(){
        Bind();
        if(wasOnline&&(manager==null||!manager.IsListening)){
            wasOnline=false;Unbind();if(expected!="MainMenu")CancelLocal();
        }
        if(messages!=null&&localCovered&&!readySent){SendReady();}
    }
    void LateUpdate(){if(loadingCamera!=null)loadingCamera.enabled=busy||(curtain!=null&&curtain.color.a>0);}
    void SceneLoaded(Scene scene,LoadSceneMode mode){
        if(mode!=LoadSceneMode.Single)return;
        if(IsNightScene(scene.name)&&(!string.IsNullOrEmpty(expected)||scene.path.StartsWith("Assets/Scenes/",StringComparison.Ordinal))){
            busy=true;SetBlack(1);SetTitle("",0);
            // A direct/offline Play also gets the correct night number.
            if(string.IsNullOrEmpty(expected)){expected=scene.name;BeginReveal(scene.name);}
        }else if(scene.name=="MainMenu"){
            if(busy||FullyBlack)BeginReveal(scene.name);else CancelLocal();
        }
    }
    void SetBlack(float alpha){curtain.color=new Color(0,0,0,alpha);curtain.raycastTarget=alpha>0||busy;}
    void SetTitle(string value,float alpha){title.text=value;title.color=new Color(1,1,1,alpha);}
    IEnumerator Fade(float from,float to,float seconds){float elapsed=0;while(elapsed<seconds){elapsed+=Time.unscaledDeltaTime;SetBlack(Mathf.Lerp(from,to,Mathf.Clamp01(elapsed/seconds)));yield return null;}SetBlack(to);}
    void StartCover(int id,string scene,bool eight){
        if(token==id&&busy){if(localCovered)SendReady();return;}
        if(animation!=null)StopCoroutine(animation);
        token=id;expected=scene;showEight=eight;busy=true;localCovered=readySent=revealing=false;AllPeersCovered=false;coverStarted=Time.realtimeSinceStartup;
        animation=StartCoroutine(Cover());
    }
    IEnumerator Cover(){
        SetTitle("",0);yield return Fade(curtain.color.a,1,.8f);
        if(showEight){EightAmCards++;SetTitle("8 AM",1);yield return new WaitForSecondsRealtime(1.25f);float elapsed=0;while(elapsed<.35f){elapsed+=Time.unscaledDeltaTime;SetTitle("8 AM",1-Mathf.Clamp01(elapsed/.35f));yield return null;}}
        SetTitle("",0);yield return new WaitForSecondsRealtime(.4f);
        localCovered=true;CompletedCovers++;SendReady();animation=null;
    }
    public IEnumerator CoverForLoad(string scene,bool eightAm){
        Bind();covered.Clear();nextToken=Mathf.Max(nextToken,token)+1;StartCover(nextToken,scene,eightAm);
        nextBroadcast=0;
        if(manager!=null&&manager.IsServer){
            while(manager!=null&&manager.IsListening){
                if(Time.realtimeSinceStartup>=nextBroadcast){nextBroadcast=Time.realtimeSinceStartup+.4f;BroadcastBegin();}
                bool all=localCovered;
                foreach(var id in manager.ConnectedClientsIds)if(!covered.Contains(id))all=false;
                if(all){AllPeersCovered=true;break;}
                if(Time.realtimeSinceStartup-coverStarted>18){
                    foreach(var id in new List<ulong>(manager.ConnectedClientsIds))if(id!=manager.LocalClientId&&!covered.Contains(id))manager.DisconnectClient(id,"Transition acknowledgement timed out.");
                    if(localCovered){AllPeersCovered=true;break;}
                }
                yield return null;
            }
        }else while(busy&&!localCovered)yield return null;
    }
    void BroadcastBegin(){if(messages==null||!manager.IsServer)return;using(var writer=new FastBufferWriter(512,Allocator.Temp)){writer.WriteValueSafe(token);writer.WriteValueSafe(expected);writer.WriteValueSafe(showEight);foreach(var id in manager.ConnectedClientsIds)if(id!=manager.LocalClientId)messages.SendNamedMessage(BeginMessage,id,writer,NetworkDelivery.ReliableSequenced);}}
    void OnBegin(ulong sender,FastBufferReader reader){if(manager==null||manager.IsServer||sender!=NetworkManager.ServerClientId)return;reader.ReadValueSafe(out int id);reader.ReadValueSafe(out string scene);reader.ReadValueSafe(out bool eight);if(id<token)return;StartCover(id,scene,eight);}
    void SendReady(){
        if(!localCovered||manager==null||!manager.IsListening||messages==null)return;
        readySent=true;
        if(manager.IsServer){covered.Add(manager.LocalClientId);return;}
        using(var writer=new FastBufferWriter(4,Allocator.Temp)){writer.WriteValueSafe(token);messages.SendNamedMessage(ReadyMessage,NetworkManager.ServerClientId,writer,NetworkDelivery.ReliableSequenced);}
    }
    void OnReady(ulong sender,FastBufferReader reader){if(manager==null||!manager.IsServer||!manager.ConnectedClients.ContainsKey(sender))return;reader.ReadValueSafe(out int id);if(id==token&&busy)covered.Add(sender);}
    public void ReleaseAfterLoad(string scene){
        Bind();if(manager!=null&&manager.IsServer&&messages!=null)using(var writer=new FastBufferWriter(256,Allocator.Temp)){writer.WriteValueSafe(token);writer.WriteValueSafe(scene);foreach(var id in manager.ConnectedClientsIds)if(id!=manager.LocalClientId)messages.SendNamedMessage(RevealMessage,id,writer,NetworkDelivery.ReliableSequenced);}
        BeginReveal(scene);
    }
    void OnReveal(ulong sender,FastBufferReader reader){if(sender!=NetworkManager.ServerClientId||manager==null||manager.IsServer)return;reader.ReadValueSafe(out int id);reader.ReadValueSafe(out string scene);if(id!=token||!busy||expected!=scene)return;BeginReveal(scene);}
    void BeginReveal(string scene){if(revealing)return;if(animation!=null)StopCoroutine(animation);revealing=true;busy=true;animation=StartCoroutine(Reveal(scene));}
    IEnumerator Reveal(string scene){
        SetBlack(1);SetTitle("",0);
        while(SceneManager.GetActiveScene().name!=scene)yield return null;
        bool night=IsNightScene(scene);
        if(night){
            while(true){
                var nm=NetworkManager.Singleton;var clock=NightTimeManager.Instance;
                bool online=nm!=null&&nm.IsListening;
                bool ready=online?clock!=null&&clock.IsSpawned&&nm.LocalClient?.PlayerObject!=null&&nm.LocalClient.PlayerObject.IsSpawned:FindFirstObjectByType<PlayerController>()!=null;
                if(ready)break;yield return null;
            }
            SetTitle(NightTitle(scene),1);NightCards++;yield return new WaitForSecondsRealtime(1.25f);SetTitle("",0);
        }else yield return new WaitForSecondsRealtime(.2f);
        yield return Fade(1,0,.75f);SetTitle("",0);busy=false;localCovered=false;expected="";revealing=false;animation=null;curtain.raycastTarget=false;
    }
    public void Cancel(){if(manager!=null&&manager.IsServer&&messages!=null)using(var writer=new FastBufferWriter(4,Allocator.Temp)){writer.WriteValueSafe(token);foreach(var id in manager.ConnectedClientsIds)if(id!=manager.LocalClientId)messages.SendNamedMessage(CancelMessage,id,writer,NetworkDelivery.ReliableSequenced);}CancelLocal();}
    void OnCancel(ulong sender,FastBufferReader reader){if(sender!=NetworkManager.ServerClientId)return;reader.ReadValueSafe(out int id);if(id==token)CancelLocal();}
    void CancelLocal(){if(animation!=null)StopCoroutine(animation);animation=null;busy=localCovered=revealing=false;expected="";SetTitle("",0);SetBlack(0);}
    public void ReturnPartyToMenu(){
        if(manager!=null&&manager.IsServer&&messages!=null)using(var writer=new FastBufferWriter(4,Allocator.Temp)){writer.WriteValueSafe(token);foreach(var id in manager.ConnectedClientsIds)if(id!=manager.LocalClientId)messages.SendNamedMessage(MenuMessage,id,writer,NetworkDelivery.ReliableSequenced);}
        StartCoroutine(ReturnLocal());
    }
    void OnMenu(ulong sender,FastBufferReader reader){if(sender!=NetworkManager.ServerClientId)return;reader.ReadValueSafe(out int id);if(id==token)StartCoroutine(ReturnLocal());}
    IEnumerator ReturnLocal(){expected="MainMenu";SetBlack(1);SetTitle("",0);yield return new WaitForSecondsRealtime(.2f);var relay=FindFirstObjectByType<Networking.RelayPartyManager>();if(relay!=null){relay.ReturnToMainMenuAfterMatch();yield break;}var nm=NetworkManager.Singleton;if(nm!=null&&nm.IsListening)nm.Shutdown();while(nm!=null&&nm.ShutdownInProgress)yield return null;SceneManager.LoadScene("MainMenu");}
    public IEnumerator CompleteOffline(string scene,bool eight){yield return CoverForLoad(scene,eight);SceneManager.LoadScene(scene);BeginReveal(scene);}
    void OnDestroy(){SceneManager.sceneLoaded-=SceneLoaded;Unbind();if(instance==this)instance=null;}
}
