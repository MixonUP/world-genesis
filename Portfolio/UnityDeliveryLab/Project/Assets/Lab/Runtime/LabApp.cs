using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace DeliveryLab
{
    public sealed class LabApp : MonoBehaviour
    {
        static readonly string[] Names={"Vital tonic","Lumen shard","Brass key","Survey chip"};
        static readonly string[] Descriptions={"A small reserve for the next expedition.\nUse to restore 20 health. Kept if health is full.","Condensed light, collected in the field.\nExchange one shard for 5 coins.","A spare key from the old observatory.\nExchange one key for 10 coins.","A reusable survey component.\nExchange one chip for 20 coins."};
        readonly Color[] itemColors={Hex("79E8B2"),Hex("BCA3FF"),Hex("F2C76B"),Hex("75CFEA")};
        readonly List<Tuple<Graphic,int>> skin=new List<Tuple<Graphic,int>>();
        readonly Button[] slotButtons=new Button[12], addButtons=new Button[4];
        readonly Image[] slotIcons=new Image[12], slotMarks=new Image[12];
        readonly Text[] slotTitles=new Text[12], slotCounts=new Text[12];
        readonly Sprite[] icons=new Sprite[4];
        Inventory model; SaveStore store; LabConfig config; Font font; Sprite rounded;
        Text coins,health,status,capacity,inspectTitle,inspectDescription,inspectMeta,themeLabel,saveLabel,useLabel;
        Image inspectIcon; Button use,remove,save,load,recover,fresh; RectTransform left,right,footer;
        bool plain,dirty; int width,height; string actionMessage="Ready for a field test.";
        public static string Arg(string key) { var a=Environment.GetCommandLineArgs(); int i=Array.IndexOf(a,key); return i>=0 && i+1<a.Length?a[i+1]:null; }
        public static bool Flag(string key) => Array.IndexOf(Environment.GetCommandLineArgs(),key)>=0;
        static Color Hex(string text) { ColorUtility.TryParseHtmlString("#"+text,out var c); return c; }
        void Start()
        {
            Application.targetFrameRate=60;
            config=JsonUtility.FromJson<LabConfig>(Resources.Load<TextAsset>("LabConfig").text);
            model=new Inventory(config.stackLimit); Seed();
            string directory=Arg("-labSaveDir")??Path.GetFullPath(Path.Combine(Application.dataPath,"../UserData"));
            store=new SaveStore(directory,config.stackLimit);
            if(store.Load(false,out var saved,out var message)) { model.Restore(saved,out _); actionMessage="Previous session restored on launch."; } else actionMessage=message;
            font=Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf"); rounded=Rounded();
            for(int i=0;i<4;i++) icons[i]=Icon(i);
            BuildUI(); Refresh();
            Debug.Log("[LAB_START] revision="+config.revision+" stackLimit="+config.stackLimit+" save="+store.PrimaryPath);
            if(Flag("-labAutoProof")) StartCoroutine(Proof(false));
            else if(Flag("-labVerifyReload")) StartCoroutine(Proof(true));
        }
        void Seed() { model.Reset(); model.Add("tonic",4,out _); model.Add("shard",7,out _); model.Add("key",2,out _); model.Add("chip",1,out _); }
        void Update()
        {
            if(Screen.width!=width || Screen.height!=height) Layout();
            if(Input.GetKeyDown(KeyCode.F5)) Save();
            if(Input.GetKeyDown(KeyCode.F9)) Load(false);
            if(Input.GetKeyDown(KeyCode.Tab)) ToggleTheme();
            if(Input.GetKeyDown(KeyCode.F12)) Capture();
        }
        void Changed(string message,bool mutation=true) { actionMessage=message; if(mutation) dirty=true; Refresh(); }
        void Save() { bool ok=store.Save(model.Snapshot,out var message); if(ok) dirty=false; Changed(message,false); }
        void Load(bool backup) { if(store.Load(backup,out var candidate,out var message) && model.Restore(candidate,out message)) { dirty=backup; message=backup?"Backup recovered. Save to commit it.":"Loaded. Saved state restored."; } Changed(message,false); }
        void ToggleTheme() { plain=!plain; ApplyTheme(); Refresh(); }
        RectTransform Rect(string name,Transform parent,float x,float y,float w,float h)
        {
            var g=new GameObject(name,typeof(RectTransform)); var r=(RectTransform)g.transform; r.SetParent(parent,false); Place(r,x,y,w,h); return r;
        }
        static void Place(RectTransform r,float x,float y,float w,float h) { r.anchorMin=r.anchorMax=new Vector2(0,1); r.pivot=new Vector2(0,1); r.anchoredPosition=new Vector2(x,-y); r.sizeDelta=new Vector2(w,h); }
        Image Panel(string name,Transform parent,float x,float y,float w,float h,int role)
        {
            var r=Rect(name,parent,x,y,w,h); var im=r.gameObject.AddComponent<Image>(); im.sprite=rounded; im.type=Image.Type.Sliced; im.raycastTarget=false; skin.Add(Tuple.Create((Graphic)im,role)); return im;
        }
        Text Label(string name,Transform parent,string value,float x,float y,float w,float h,int size,int role=4,bool bold=false)
        {
            var r=Rect(name,parent,x,y,w,h); var text=r.gameObject.AddComponent<Text>(); text.font=font; text.fontSize=size; text.text=value; text.color=Color.white; text.raycastTarget=false; text.fontStyle=bold?FontStyle.Bold:FontStyle.Normal; text.alignment=TextAnchor.MiddleLeft; text.horizontalOverflow=HorizontalWrapMode.Wrap; text.verticalOverflow=VerticalWrapMode.Truncate; skin.Add(Tuple.Create((Graphic)text,role)); return text;
        }
        Button Button(string name,Transform parent,string title,float x,float y,float w,float h,int role,Action callback)
        {
            var image=Panel(name,parent,x,y,w,h,role); image.raycastTarget=true;
            var b=image.gameObject.AddComponent<Button>(); b.targetGraphic=image; var cb=b.colors; cb.normalColor=Color.white; cb.highlightedColor=new Color(1.25f,1.25f,1.25f); cb.pressedColor=new Color(.65f,.75f,.8f); cb.selectedColor=Color.white; cb.disabledColor=new Color(.4f,.4f,.4f,.65f); cb.fadeDuration=.1f; b.colors=cb;
            b.navigation=new Navigation{mode=Navigation.Mode.None}; b.onClick.AddListener(()=>callback());
            if(title.Length>0) { var t=Label(name+"Label",image.transform,title,14,0,w-28,h,17,role==3?6:4,true); t.alignment=TextAnchor.MiddleCenter; }
            return b;
        }
        void BuildUI()
        {
            var cg=new GameObject("InventoryLabCanvas",typeof(Canvas),typeof(CanvasScaler),typeof(GraphicRaycaster)); var canvas=cg.GetComponent<Canvas>(); canvas.renderMode=RenderMode.ScreenSpaceOverlay;
            var scaler=cg.GetComponent<CanvasScaler>(); scaler.uiScaleMode=CanvasScaler.ScaleMode.ScaleWithScreenSize; scaler.referenceResolution=new Vector2(1440,900); scaler.matchWidthOrHeight=0;
            if(FindAnyObjectByType<EventSystem>()==null) new GameObject("Input",typeof(EventSystem),typeof(StandaloneInputModule));
            var root=cg.transform;
            var bg=Panel("Background",root,0,0,1440,2000,0); bg.sprite=null;
            Label("Eyebrow",root,"DELIVERY LAB   /   INTERNAL DEMONSTRATION",40,23,800,23,14,5,true);
            Label("Title",root,"Inventory & Save Lab",40,51,900,56,42,4,true);
            Label("Subtitle",root,"Changes stay in this session until you save.",42,111,940,24,18,5);
            var coinBox=Panel("Coins",root,1190,30,210,72,2); Label("CoinCaption",coinBox.transform,"COINS",19,8,100,20,13,5,true); coins=Label("CoinValue",coinBox.transform,"125",19,26,180,40,30,7,true);
            var theme=Button("Theme",root,"",1000,108,400,30,1,ToggleTheme); themeLabel=Label("ThemeLabel",theme.transform,"",12,0,376,30,15,5); themeLabel.alignment=TextAnchor.MiddleRight;
            left=Panel("InventoryPanel",root,40,155,840,540,1).rectTransform;
            right=Panel("InspectorPanel",root,904,155,496,540,1).rectTransform;
            Label("InventoryHeading",left,"FIELD LOCKER",24,18,350,30,17,4,true);
            capacity=Label("Capacity",left,"",450,18,366,30,15,5); capacity.alignment=TextAnchor.MiddleRight;
            for(int i=0;i<4;i++) { int id=i; float x=24+i*198; addButtons[i]=Button("Add_"+Inventory.Items[i],left,"+ "+Names[i],x,64,186,44,2,()=>{int amount=Input.GetKey(KeyCode.LeftShift)||Input.GetKey(KeyCode.RightShift)?config.stackLimit:1;bool ok=model.Add(Inventory.Items[id],amount,out var msg); Changed(msg,ok);}); var accent=Panel("ItemAccent",addButtons[i].transform,0,12,4,20,3); accent.color=itemColors[i]; skin.RemoveAt(skin.Count-1); }
            for(int i=0;i<12;i++) {
                int id=i; float x=24+(i%4)*198, y=132+(i/4)*116;
                var b=Button("Slot_"+(i+1),left,"",x,y,186,104,2,()=>{model.Select(id); Changed("Selected slot "+(id+1)+".");}); slotButtons[i]=b;
                slotMarks[i]=Panel("Selected",b.transform,0,0,4,104,3); slotMarks[i].raycastTarget=false;
                var art=Rect("ItemIcon",b.transform,12,7,64,64).gameObject.AddComponent<Image>(); art.raycastTarget=false; slotIcons[i]=art;
                slotCounts[i]=Label("Quantity",b.transform,"",100,12,72,33,24,4,true); slotCounts[i].alignment=TextAnchor.MiddleRight;
                slotTitles[i]=Label("Name",b.transform,"Empty slot",14,75,160,22,15,5);
                Label("SlotNumber",b.transform,(i+1).ToString("00"),145,51,26,20,11,5).alignment=TextAnchor.MiddleRight;
            }
            saveLabel=Label("SaveStatus",left,"",24,488,792,26,15,5);
            Label("InspectorHeading",right,"ITEM INSPECTOR",26,18,440,30,17,4,true);
            var artBox=Panel("ItemPreview",right,26,66,444,126,2); inspectIcon=Rect("PreviewIcon",artBox.transform,162,6,114,114).gameObject.AddComponent<Image>(); inspectIcon.raycastTarget=false;
            inspectTitle=Label("ItemTitle",right,"",26,209,444,44,31,4,true);
            inspectDescription=Label("ItemDescription",right,"",26,260,444,75,18,5);
            inspectMeta=Label("ItemMetadata",right,"",26,342,444,30,16,5);
            health=Label("Health",right,"",26,383,444,30,19,3,true);
            use=Button("UseItem",right,"USE / EXCHANGE",26,438,266,54,3,()=>{bool ok=model.Use(model.Snapshot.selectedSlot,out var m);Changed(m,ok);});
            useLabel=use.GetComponentInChildren<Text>();
            remove=Button("RemoveItem",right,"REMOVE 1",308,438,162,54,2,()=>{bool ok=model.Remove(model.Snapshot.selectedSlot,1,out var m);Changed(m,ok);});
            footer=Panel("Footer",root,40,730,1360,66,1).rectTransform;
            status=Label("ActionStatus",footer,"",20,5,650,56,17,4);
            save=Button("Save",footer,"SAVE  F5",700,10,142,46,3,Save);
            load=Button("Load",footer,"LOAD  F9",854,10,142,46,2,()=>Load(false));
            recover=Button("Recover",footer,"RECOVER",1008,10,142,46,2,()=>Load(true));
            fresh=Button("NewSession",footer,"NEW SESSION",1162,10,180,46,2,()=>{Seed();Changed("New session. Existing disk save kept until SAVE.");});
            Label("FooterNote",root,Flag("-labAutoProof")||Flag("-labVerifyReload")?"AUTOMATED EVIDENCE RUN · UI callbacks, not human playtest":"INTERNAL DEMO · SHIFT + add = one stack · TAB theme · F12 capture",40,0,1360,24,12,5).gameObject.name="BottomNote";
            ApplyTheme(); Layout();
        }
        void Layout()
        {
            if(left==null) return; width=Screen.width; height=Screen.height; float h=height*1440f/Mathf.Max(width,1);
            float body=Mathf.Max(540,h-270); Place(left,40,155,840,body); Place(right,904,155,496,body); Place(footer,40,h-92,1360,66);
            var note=GameObject.Find("BottomNote"); if(note!=null) Place((RectTransform)note.transform,40,h-24,1360,20);
        }
        void ApplyTheme()
        {
            Color[] colors=plain?new[]{Hex("DCE1E7"),Hex("F6F7F9"),Hex("E3E7ED"),Hex("286AA8"),Hex("182331"),Hex("526274"),Color.white,Hex("725000")}:new[]{Hex("09121D"),Hex("121F2D"),Hex("1E3042"),Hex("83E7B6"),Hex("EDF4FA"),Hex("99AFC3"),Hex("102B21"),Hex("F0CB78")};
            foreach(var pair in skin) pair.Item1.color=colors[pair.Item2];
        }
        void Refresh()
        {
            if(coins==null) return; var s=model.Snapshot; int used=0,total=0;
            for(int i=0;i<12;i++) { var v=s.slots[i]; int kind=Array.IndexOf(Inventory.Items,v.item); bool filled=v.quantity>0; if(filled) {used++;total+=v.quantity;} slotIcons[i].enabled=filled; if(filled) slotIcons[i].sprite=icons[kind]; slotMarks[i].gameObject.SetActive(s.selectedSlot==i); slotTitles[i].text=filled?Names[kind]:"Empty slot"; slotCounts[i].text=filled?v.quantity.ToString():"—"; }
            coins.text=s.coins.ToString("N0"); capacity.text=used+" / 12 slots     ·     "+total+" items";
            bool selected=s.selectedSlot>=0 && s.slots[s.selectedSlot].quantity>0;
            inspectIcon.enabled=selected; use.interactable=selected; remove.interactable=selected;
            if(selected) { var v=s.slots[s.selectedSlot]; int k=Array.IndexOf(Inventory.Items,v.item); inspectIcon.sprite=icons[k]; inspectTitle.text=Names[k]; inspectDescription.text=Descriptions[k]; inspectMeta.text="SLOT "+(s.selectedSlot+1).ToString("00")+"     ·     "+v.quantity+" / "+config.stackLimit+" IN STACK"; bool fullHealth=k==0 && s.health==100; use.interactable=!fullHealth; useLabel.text=fullHealth?"HEALTH FULL":k==0?"USE TONIC":"EXCHANGE +"+(k==1?5:k==2?10:20); }
            else { inspectTitle.text="Room for discovery"; inspectDescription.text="Add an item above, then select its slot.\nUse, exchange or remove it here."; inspectMeta.text="NO ITEM SELECTED"; useLabel.text="SELECT AN ITEM"; }
            health.text="HEALTH    "+s.health+" / 100"; status.text=actionMessage;
            saveLabel.text=(dirty?"● Unsaved changes":"● Session ready")+"     ·     Stack cap "+config.stackLimit+"     ·     JSON schema 1";
            themeLabel.text="THEME: "+(plain?"BASELINE":"STUDIO")+"   /   SWITCH  [TAB]";
        }
        Sprite Rounded()
        {
            const int n=64; var t=new Texture2D(n,n,TextureFormat.RGBA32,false); t.wrapMode=TextureWrapMode.Clamp;
            for(int y=0;y<n;y++) for(int x=0;x<n;x++) { float dx=Mathf.Max(Mathf.Abs(x-31.5f)-19.5f,0),dy=Mathf.Max(Mathf.Abs(y-31.5f)-19.5f,0); float a=Mathf.Clamp01(12-Mathf.Sqrt(dx*dx+dy*dy)); t.SetPixel(x,y,new Color(1,1,1,a)); } t.Apply();
            return Sprite.Create(t,new Rect(0,0,n,n),new Vector2(.5f,.5f),100,0,SpriteMeshType.FullRect,new Vector4(16,16,16,16));
        }
        Sprite Icon(int kind)
        {
            const int n=96; var t=new Texture2D(n,n,TextureFormat.RGBA32,false); t.filterMode=FilterMode.Bilinear;
            for(int y=0;y<n;y++) for(int x=0;x<n;x++) {
                float dx=x-48,dy=y-44; bool filled=false,shine=false;
                if(kind==0) { filled=(dx*dx+dy*dy<25*25 && y<58)||(Mathf.Abs(dx)<12 && y>=48 && y<78)||(Mathf.Abs(dx)<17 && y>=75 && y<84); shine= x<40 && y>28 && y<53; }
                if(kind==1) { filled=Mathf.Abs(dx)/28+Mathf.Abs(y-48)/39f<1; shine=x<47; }
                if(kind==2) { float r=(x-40)*(x-40)+(y-62)*(y-62); filled=(r<20*20 && r>10*10)||(x>=42 && x<52 && y>=17 && y<=55)||(x>=48 && x<68 && y>=18 && y<27)||(x>=48 && x<62 && y>=32 && y<40); shine=x<42; }
                if(kind==3) { filled=Mathf.Abs(dx)<27 && Mathf.Abs(y-48)<27; if((x<27||x>69)&&(y<27||y>69))filled=false; shine=(Mathf.Abs(dx)<13 && Mathf.Abs(y-48)<13)||(y%12<3 && x>15 && x<80 && y>17 && y<79); }
                Color c=Color.clear; if(filled) c=shine?Color.Lerp(itemColors[kind],Color.white,.45f):itemColors[kind]; t.SetPixel(x,y,c);
            }
            // Original dark silhouette stroke keeps pale shapes readable in both palettes.
            var pixels=t.GetPixels(); Color stroke=Hex("254454");
            for(int y=2;y<n-2;y++) for(int x=2;x<n-2;x++) if(pixels[y*n+x].a==0) {
                bool edge=false; for(int oy=-2;oy<=2 && !edge;oy++) for(int ox=-2;ox<=2;ox++) if(pixels[(y+oy)*n+x+ox].a>0) {edge=true;break;}
                if(edge)t.SetPixel(x,y,stroke);
            }
            t.Apply(); return Sprite.Create(t,new Rect(0,0,n,n),new Vector2(.5f,.5f),100);
        }
        [Serializable] sealed class CaptureRecord { public string revision,utc,mode="native-F12-hotkey; input source is recorded separately by the operator"; public InventorySnapshot snapshot; }
        void Capture()
        {
            string dir=Arg("-labCaptureDir")??Path.Combine(Path.GetDirectoryName(store.PrimaryPath),"Captures"); Directory.CreateDirectory(dir);
            string stamp=DateTime.UtcNow.ToString("yyyyMMdd-HHmmss-fff"); string path=Path.Combine(dir,stamp+".png");
            ScreenCapture.CaptureScreenshot(path);
            File.WriteAllText(Path.Combine(dir,stamp+".json"),JsonUtility.ToJson(new CaptureRecord{revision=config.revision,utc=DateTime.UtcNow.ToString("O"),snapshot=model.Snapshot},true));
            Debug.Log("[LAB_CAPTURE] "+path);
        }
        [Serializable] sealed class ProofResult { public bool passed,assisted=true,nativePlayer=true,negativeRejected,corruptionKeptRunning,backupRecovered,themePreservedState,reloadVerified; public int stackLimit,shardStacks,coins,health; public string revision,error,scope; }
        IEnumerator Proof(bool reload)
        {
            yield return null; yield return new WaitForSeconds(.6f);
            string dir=Arg("-labProofDir")??Path.Combine(Application.dataPath,"../Evidence"); Directory.CreateDirectory(dir);
            var result=new ProofResult{stackLimit=config.stackLimit,revision=config.revision,scope=reload?"Restart persistence only; other boolean checks not run in this process":"Assisted UI callbacks, negative quantity, corrupt primary, backup, theme and stack behavior; restart is a separate process"};
            if(!reload) {
                fresh.onClick.Invoke(); string before=JsonUtility.ToJson(model.Snapshot); plain=true; ApplyTheme(); Refresh();
                yield return new WaitForEndOfFrame(); ScreenCapture.CaptureScreenshot(Path.Combine(dir,"01-baseline-theme.png")); yield return new WaitForSeconds(.2f);
                ToggleTheme(); result.themePreservedState=before==JsonUtility.ToJson(model.Snapshot);
                yield return new WaitForEndOfFrame(); ScreenCapture.CaptureScreenshot(Path.Combine(dir,"02-studio-theme.png")); yield return new WaitForSeconds(.2f);
                for(int i=0;i<11;i++) addButtons[1].onClick.Invoke();
                foreach(var slot in model.Snapshot.slots) if(slot.item=="shard") result.shardStacks++;
                slotButtons[0].onClick.Invoke(); use.onClick.Invoke(); slotButtons[1].onClick.Invoke(); use.onClick.Invoke(); save.onClick.Invoke();
                string saved=JsonUtility.ToJson(model.Snapshot); addButtons[2].onClick.Invoke(); save.onClick.Invoke();
                string current=JsonUtility.ToJson(model.Snapshot); result.negativeRejected=!model.Add("tonic",-1,out _) && current==JsonUtility.ToJson(model.Snapshot);
                File.WriteAllText(store.PrimaryPath,"{ deliberate internal proof corruption"); load.onClick.Invoke(); result.corruptionKeptRunning=current==JsonUtility.ToJson(model.Snapshot);
                recover.onClick.Invoke(); result.backupRecovered=saved==JsonUtility.ToJson(model.Snapshot); save.onClick.Invoke();
                result.passed=result.themePreservedState && result.negativeRejected && result.corruptionKeptRunning && result.backupRecovered && result.shardStacks==(config.stackLimit==10?2:1);
            } else {
                var s=model.Snapshot; int shards=0; foreach(var v in s.slots) if(v.item=="shard")shards+=v.quantity;
                result.reloadVerified=s.coins==130 && s.health==84 && shards==17; result.passed=result.reloadVerified;
            }
            result.coins=model.Snapshot.coins; result.health=model.Snapshot.health;
            Changed(result.passed?(reload?"Restart verified. Saved session restored.":"Proof complete. Save, rejection and recovery verified."):"Proof failed — inspect receipt.",false);
            yield return new WaitForEndOfFrame(); ScreenCapture.CaptureScreenshot(Path.Combine(dir,reload?"04-reloaded.png":"03-proof-complete.png")); yield return new WaitForSeconds(.5f);
            File.WriteAllText(Path.Combine(dir,reload?"reload-proof.json":"runtime-proof.json"),JsonUtility.ToJson(result,true)); Debug.Log("[LAB_PROOF] "+JsonUtility.ToJson(result));
            if(!Flag("-labStayOpen")) Application.Quit(result.passed?0:3);
        }
    }
}
