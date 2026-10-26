using System;
using System.Linq;
using System.Threading.Tasks;
using Blocks.Sessions;
using Blocks.Sessions.Common;
using Unity.Netcode;
using Unity.Services.Multiplayer;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;

namespace Blocks.Gameplay.Shooter
{
    [RequireComponent(typeof(Canvas),typeof(CanvasScaler),typeof(GraphicRaycaster))]
    [RequireComponent(typeof(CanvasGroup))]
    public sealed class ShooterSessionCanvas : MonoBehaviour
    {
        public SessionSettings settings;
        public UnityEngine.UIElements.UIDocument legacyBrowser;
        [SerializeField] InputField sessionName;
        InputField roomCodeInput;
        [SerializeField] Dropdown mode;
        [SerializeField] Button create,refresh,join,recover,leave,copy;
        [SerializeField] Text status,count,currentName,playerCount,sessionCode,currentPlayers,sessionState,playerSummary;
        [SerializeField] RectTransform listContent;
        [SerializeField] GameObject emptyList,help;
        [SerializeField] Button rowTemplate;
        Canvas canvas;
        CanvasGroup group;
        Font runtimeFont;
        CreateSessionViewModel creator;
        SessionBrowserViewModel browser;

        SessionObserver observer;
        bool busy,recovering,disposed,wasVisible=true;
        float nextStatus;
        readonly Color navy=new Color(.031f,.078f,.133f,.87f);
        readonly Color blue=new Color(.04f,.48f,.73f);
        readonly Color cyan=new Color(.12f,.76f,.97f);
        readonly Color muted=new Color(.59f,.70f,.81f);
        Font Font => runtimeFont!=null?runtimeFont:Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");

        void Awake()
        {
            canvas=GetComponent<Canvas>();group=GetComponent<CanvasGroup>();
            if(legacyBrowser!=null)legacyBrowser.enabled=false;
            if(sessionName==null)BuildVisuals();
            runtimeFont=Font.CreateDynamicFontFromOSFont(new[]{"Malgun Gothic","Noto Sans CJK KR","Arial"},24);
            foreach(var text in GetComponentsInChildren<Text>(true))text.font=Font;
            if(EventSystem.current==null)
            {
                var events=new GameObject("Session EventSystem",typeof(EventSystem),typeof(InputSystemUIInputModule));
                events.GetComponent<InputSystemUIInputModule>().AssignDefaultActions();
            }
            if(settings==null){status.text="세션 설정이 연결되지 않았습니다.";enabled=false;return;}
            observer=new SessionObserver(settings.sessionType);
            creator=new CreateSessionViewModel(settings.sessionType);
            browser=new SessionBrowserViewModel(settings.sessionType);
            browser.SelectedSessionIndex=-1;
            ConfigureCodeEntry();

            create.onClick.AddListener(CreateRoom);
            refresh.onClick.AddListener(RefreshRooms);
            join.onClick.AddListener(JoinListedRoom);


            recover.onClick.AddListener(RecoverConnection);
            leave.onClick.AddListener(LeaveRoom);
            copy.onClick.AddListener(()=>{if(observer.Session!=null)GUIUtility.systemCopyBuffer=observer.Session.Code;else JoinRoom();});
            sessionName.onValueChanged.AddListener(value=>creator.SetSessionName(value));
            creator.SetSessionName(sessionName.text);
            WireHelp();
        }
        void Update()
        {
            if(canvas==null || creator==null)return;
            // Visibility follows actual networking, including direct/local AI games.
            var manager=NetworkManager.Singleton;var match=ShooterEscortMatch.Instance;
            bool inSession=(manager!=null && manager.IsConnectedClient)||(match!=null && match.IsSpawned);
            bool visible=!inSession || (match!=null && match.ReturningToSessionBrowser);
            canvas.enabled=visible;group.interactable=visible;group.blocksRaycasts=visible;
            if(!visible && wasVisible)
            {
                sessionName.DeactivateInputField();roomCodeInput.DeactivateInputField();mode.Hide();help.SetActive(false);
                if(EventSystem.current!=null && EventSystem.current.currentSelectedGameObject!=null &&
                   EventSystem.current.currentSelectedGameObject.transform.IsChildOf(transform))EventSystem.current.SetSelectedGameObject(null);
            }
            wasVisible=visible;
            if(!visible || Time.unscaledTime<nextStatus)return;
            nextStatus=Time.unscaledTime+.1f;
            create.interactable=!busy&&!recovering&&creator.CanRegisterSession&&!string.IsNullOrWhiteSpace(sessionName.text);


            recover.interactable=!busy&&!recovering&&observer.Session==null;
            sessionName.interactable=mode.interactable=!busy&&!recovering&&creator.CanRegisterSession;
            var session=observer.Session;
            leave.interactable=session!=null&&!busy;
            bool canJoin=!busy&&!recovering&&session==null&&creator.CanRegisterSession;
            roomCodeInput.gameObject.SetActive(session==null);
            roomCodeInput.interactable=canJoin;
            join.interactable=canJoin&&browser.SelectedAndAvailable;
            refresh.interactable=!busy&&!recovering&&session==null&&browser.CanRefresh;
            sessionCode.gameObject.SetActive(session!=null);
            copy.GetComponentInChildren<Text>().text=session==null?"참가":"복사";
            copy.interactable=session!=null?!busy&&!recovering&&!string.IsNullOrWhiteSpace(session.Code):canJoin&&!string.IsNullOrWhiteSpace(roomCodeInput.text);
            transform.Find("Design/CurrentSession/Title").GetComponent<Text>().text=session==null?"방 코드로 참가":"현재 방";
            sessionState.text=session==null?"세션 미참가 상태":"세션 참가 중";
            playerSummary.text=session==null?"참가한 플레이어 없음":session.Players.Count+"명 참가 중";
            currentName.text=session==null?"MULTIPLAYER · READY FOR BATTLE":session.Name;
            playerCount.text=session==null?"0 / 4":$"{session.Players.Count} / {session.MaxPlayers}";
            sessionCode.text=session==null?"세션 코드 없음":"세션 코드  "+session.Code;
            currentPlayers.text=session==null?"방장이 공유한 코드를 위에 입력한 뒤\n참가 버튼을 누르세요.\n\n왼쪽 방 목록에서 선택해 참가하거나\n새 방을 만들 수도 있습니다.":"세션에 연결되었습니다.\n대기방을 준비하고 있습니다…";
        }
        async void CreateRoom()
        {
            if(busy||recovering||!creator.CanRegisterSession||string.IsNullOrWhiteSpace(sessionName.text))return;
            busy=true;status.text="세션 생성 중…";
            try
            {
                creator.SetSessionName(sessionName.text);
                var options=settings.ToSessionOptions();options.MaxPlayers=4;options.IsPrivate=false;
                options.SessionProperties["shooter-mode"]=new Unity.Services.Multiplayer.SessionProperty(mode.value==1?"escort":"ffa");
                await creator.CreateSessionAsync(options);
                if(!disposed)status.text="세션 생성 완료";
            }
            catch(Exception e){if(!disposed)status.text="세션 생성 실패: "+e.Message;}
            finally{busy=false;}
        }
        async void JoinRoom()
        {
            if(busy||recovering||observer.Session!=null||!creator.CanRegisterSession)return;
            string code=roomCodeInput.text.Trim().ToUpperInvariant();
            if(string.IsNullOrWhiteSpace(code)){status.text="방 코드를 입력해 주세요.";return;}
            roomCodeInput.SetTextWithoutNotify(code);
            busy=true;status.text="코드로 방에 참가하는 중…";
            try
            {
                await MultiplayerService.Instance.JoinSessionByCodeAsync(code,settings.ToJoinSessionOptions());
                if(!disposed){status.text="참가 완료";roomCodeInput.SetTextWithoutNotify("");}
            }
            catch(Exception e){if(!disposed)status.text="참가 실패: 코드를 확인해 주세요. "+e.Message;}
            finally{busy=false;}
        }
        async void RecoverConnection()
        {
            if(busy||recovering)return;recovering=true;status.text="이전 연결을 정리하는 중…";
            try {await creator.RefreshConnectionAsync();if(!disposed){status.text="연결 정리 완료. 다시 시도해 주세요.";}}
            catch(Exception e){if(!disposed)status.text="연결 정리 실패: "+e.Message;}
            finally{recovering=false;}
        }
        async void LeaveRoom()
        {
            if(busy||observer.Session==null)return;busy=true;
            try {await observer.Session.LeaveAsync();if(!disposed)status.text="세션에서 나왔습니다.";}
            catch(Exception e){if(!disposed)status.text="나가기 실패: "+e.Message;}
            finally{busy=false;}
        }
        async void RefreshRooms()
        {
            if(busy||recovering||observer.Session!=null||!browser.CanRefresh)return;
            busy=true;status.text="방 목록을 불러오는 중…";
            try
            {
                await browser.UpdateSessionListAsync(20);
                if(!disposed){status.text=browser.StatusMessage;RebuildRows();}
            }
            catch(Exception e){if(!disposed)status.text="목록 조회 실패: "+e.Message;}
            finally{busy=false;}
        }
        async void JoinListedRoom()
        {
            if(busy||recovering||observer.Session!=null||!creator.CanRegisterSession||!browser.SelectedAndAvailable)return;
            string id=browser.GetSelectedSessionId();
            if(string.IsNullOrEmpty(id))return;
            busy=true;status.text="선택한 방에 참가하는 중…";
            try
            {
                await MultiplayerService.Instance.JoinSessionByIdAsync(id,settings.ToJoinSessionOptions());
                if(!disposed)status.text="참가 완료";
            }
            catch(Exception e){if(!disposed)status.text="참가 실패: "+e.Message;}
            finally{busy=false;}
        }
        void RebuildRows()
        {
            foreach(Transform child in listContent)
                if(child!=rowTemplate.transform){child.gameObject.SetActive(false);Destroy(child.gameObject);}
            browser.SelectedSessionIndex=-1;
            emptyList.SetActive(browser.Sessions.Count==0);
            count.text=browser.Sessions.Count+"개의 방";
            listContent.sizeDelta=new Vector2(0,Mathf.Max(190,browser.Sessions.Count*56));
            for(int i=0;i<browser.Sessions.Count;i++)
            {
                int index=i;var info=browser.Sessions[i];var row=Instantiate(rowTemplate,listContent);
                row.name="Session_"+i;row.gameObject.SetActive(true);
                Position((RectTransform)row.transform,0,i*56,676,52);
                row.GetComponentInChildren<Text>().text=$"{info.Name}     {info.MaxPlayers-info.AvailableSlots} / {info.MaxPlayers}";
                row.interactable=!info.IsLocked&&info.AvailableSlots>0;
                row.onClick.AddListener(()=>{
                    browser.SelectedSessionIndex=index;
                    foreach(Transform child in listContent)
                    {
                        var panel=child.GetComponent<SessionPanelGraphic>();
                        if(panel!=null)panel.color=child==row.transform?blue:new Color(.08f,.14f,.22f);
                    }
                });
            }
        }
        void ConfigureCodeEntry()
        {
            // Upgrade both saved scene UI and UI built at runtime.
            var left=transform.Find("Design/CreateAndBrowse");
            foreach(string name in new[]{"ListTitle","Count","SessionList","Join","Refresh"})
            {
                var item=left.Find(name);if(item!=null)item.gameObject.SetActive(true);
            }
            left.Find("Title").GetComponent<Text>().text="방 생성 · 목록 참가";
            left.Find("Subtitle").GetComponent<Text>().text="CREATE OR JOIN ROOM";
            create.GetComponentInChildren<Text>().text="+  방 생성";
            left.Find("ListTitle").GetComponent<Text>().text="방 목록";
            var right=transform.Find("Design/CurrentSession");
            right.Find("Title").GetComponent<Text>().text="방 코드로 참가";
            roomCodeInput=MakeInput(right,42,215,380,40);
            roomCodeInput.name="RoomCodeInput";
            roomCodeInput.characterLimit=32;
            roomCodeInput.contentType=InputField.ContentType.Alphanumeric;
            ((Text)roomCodeInput.placeholder).text="방 코드 입력";
            copy.interactable=false;
            copy.GetComponentInChildren<Text>().text="참가";
        }
        void WireHelp()
        {
            transform.Find("Design/HelpButton").GetComponent<Button>().onClick.AddListener(()=>help.SetActive(true));
            help.transform.Find("Close").GetComponent<Button>().onClick.AddListener(()=>help.SetActive(false));
            string[] names={"솔저","닌자","디바","캐서디"};
            for(int i=0;i<4;i++) {int hero=i;var button=help.transform.Find("Hero"+i).GetComponent<Button>();
                button.onClick.AddListener(()=>{ShooterCharacterSelection.SetInitialCharacter(hero);status.text=names[hero]+" 선택됨";});}
        }
        void OnDestroy()
        {disposed=true;creator?.Dispose();browser?.Dispose();observer?.Dispose();if(runtimeFont!=null)Destroy(runtimeFont);}

        public void BuildVisuals()
        {
            var canvas=GetComponent<Canvas>();canvas.renderMode=RenderMode.ScreenSpaceOverlay;canvas.sortingOrder=100;
            var scaler=GetComponent<CanvasScaler>();scaler.uiScaleMode=CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution=new Vector2(1672,941);scaler.screenMatchMode=CanvasScaler.ScreenMatchMode.Expand;
            var design=Rect("Design",transform,0,0,1672,941);design.anchorMin=design.anchorMax=new Vector2(.5f,.5f);
            design.pivot=new Vector2(.5f,.5f);design.anchoredPosition=Vector2.zero;
            var left=Panel("CreateAndBrowse",design,148,100,775,734);Frame(left);
            Label("Title",left,"세션 참가",58,22,650,64,44);
            Label("Subtitle",left,"JOIN SESSION",60,86,600,28,17,muted);
            Label("ModeLabel",left,"모드 선택",58,126,165,44,22);
            mode=MakeDropdown(left,223,126,505,44);
            Label("NameLabel",left,"세션 이름 입력",58,185,165,44,22);
            sessionName=MakeInput(left,223,185,505,44);
            create=MakeButton("Create",left,"+  세션 생성",58,251,670,56,blue);
            recover=MakeButton("Recover",left,"연결 새로고침",58,320,670,54);
            status=Label("Status",left,"",58,378,670,28,16,muted);
            Label("ListTitle",left,"세션 목록",58,396,440,35,22);
            count=Label("Count",left,"0개의 세션",550,396,178,35,22,muted);count.alignment=TextAnchor.MiddleRight;
            var list=Panel("SessionList",left,52,435,676,192);
            var viewport=Rect("Viewport",list,1,1,674,190);viewport.gameObject.AddComponent<RectMask2D>();
            listContent=Rect("Content",viewport,0,0,676,190);
            var scroll=list.gameObject.AddComponent<ScrollRect>();scroll.viewport=viewport;scroll.content=listContent;scroll.horizontal=false;
            scroll.movementType=ScrollRect.MovementType.Clamped;scroll.scrollSensitivity=35;
            emptyList=Label("Empty",viewport,"참가 가능한 세션이 없습니다\n\n다른 플레이어가 생성한 세션을 기다리거나\n직접 세션을 만들어 보세요.",14,12,646,166,20,muted).gameObject;
            emptyList.GetComponent<Text>().alignment=TextAnchor.MiddleCenter;
            rowTemplate=MakeButton("RowTemplate",listContent,"세션",0,0,676,52);rowTemplate.gameObject.SetActive(false);
            join=MakeButton("Join",left,"참가",52,639,326,58);join.interactable=false;
            refresh=MakeButton("Refresh",left,"목록 새로고침",398,639,330,58);
            var right=Panel("CurrentSession",design,964,162,582,618);Frame(right);
            Label("Title",right,"현재 세션",42,22,495,62,44);
            currentName=Label("Name",right,"MULTIPLAYER · READY FOR BATTLE",42,86,495,28,17,muted);
            Label("PlayerLabel",right,"참가 인원",42,115,300,42,22);
            playerCount=Label("Count",right,"0 / 4",350,115,190,42,22);playerCount.alignment=TextAnchor.MiddleRight;
            playerSummary=Label("Players",right,"참가한 플레이어 없음",42,165,495,40,22,muted);
            var codeBox=Panel("CodeBox",right,42,215,380,40);
            sessionCode=Label("Code",codeBox,"세션 코드 없음",12,0,356,40,22,muted);sessionCode.alignment=TextAnchor.MiddleRight;
            copy=MakeButton("Copy",right,"복사",437,215,100,40);copy.interactable=false;
            var empty=Panel("SessionDetails",right,38,266,506,210);
            currentPlayers=Label("Details",empty,"방장이 공유한 코드를 위에 입력한 뒤\n참가 버튼을 누르세요.\n\n왼쪽 방 목록에서 선택해 참가하거나\n새 방을 만들 수도 있습니다.",20,20,466,170,20,muted);currentPlayers.alignment=TextAnchor.MiddleCenter;
            leave=MakeButton("Leave",right,"세션 나가기",38,496,506,58);leave.interactable=false;
            sessionState=Label("Footer",right,"세션 미참가 상태",42,568,495,30,22);
            MakeButton("HelpButton",design,"캐릭터 · 조작 설명",35,865,285,48);
            BuildHelp(design);
        }
        RectTransform Rect(string name,Transform parent,float x,float y,float w,float h)
        {var go=new GameObject(name,typeof(RectTransform));go.transform.SetParent(parent,false);var rect=(RectTransform)go.transform;Position(rect,x,y,w,h);return rect;}
        static void Position(RectTransform rect,float x,float y,float w,float h)
        {rect.anchorMin=rect.anchorMax=new Vector2(0,1);rect.pivot=new Vector2(0,1);rect.anchoredPosition=new Vector2(x,-y);rect.sizeDelta=new Vector2(w,h);}
        RectTransform Panel(string name,Transform parent,float x,float y,float w,float h)
        {var rect=Rect(name,parent,x,y,w,h);var image=rect.gameObject.AddComponent<SessionPanelGraphic>();image.color=navy;return rect;}
        Text Label(string name,Transform parent,string value,float x,float y,float w,float h,int size,Color? color=null)
        {var rect=Rect(name,parent,x,y,w,h);var text=rect.gameObject.AddComponent<Text>();text.font=Font;text.text=value;text.fontSize=size;text.color=color??Color.white;text.alignment=TextAnchor.MiddleLeft;text.raycastTarget=false;text.horizontalOverflow=HorizontalWrapMode.Wrap;if(name=="Title")text.fontStyle=FontStyle.Bold;return text;}
        Button MakeButton(string name,Transform parent,string text,float x,float y,float w,float h,Color? color=null)
        {
            var rect=Panel(name,parent,x,y,w,h);var image=rect.GetComponent<SessionPanelGraphic>();image.color=color??new Color(.11f,.18f,.26f);
            var button=rect.gameObject.AddComponent<Button>();button.targetGraphic=image;
            var colors=button.colors;colors.highlightedColor=new Color(.65f,.9f,1);colors.pressedColor=new Color(.35f,.65f,.8f);colors.disabledColor=new Color(.7f,.75f,.8f,1);button.colors=colors;
            var label=Label("Text",rect,text,8,0,w-16,h,24);label.alignment=TextAnchor.MiddleCenter;label.fontStyle=FontStyle.Bold;return button;
        }
        void Frame(RectTransform rect)
        {var frame=rect.GetComponent<SessionPanelGraphic>();frame.radius=18;frame.borderColor=cyan;frame.insetFrame=true;}
        InputField MakeInput(Transform parent,float x,float y,float w,float h)
        {
            var rect=Panel("SessionNameInput",parent,x,y,w,h);var input=rect.gameObject.AddComponent<InputField>();input.targetGraphic=rect.GetComponent<SessionPanelGraphic>();input.characterLimit=40;
            input.textComponent=Label("Text",rect,"",16,0,w-32,h,22);input.textComponent.supportRichText=false;
            input.placeholder=Label("Placeholder",rect,"세션 이름을 입력하세요...",16,0,w-32,h,20,muted);
            input.customCaretColor=true;input.caretColor=Color.white;return input;
        }
        Dropdown MakeDropdown(Transform parent,float x,float y,float w,float h)
        {
            var rect=Panel("GameModeDropdown",parent,x,y,w,h);var dropdown=rect.gameObject.AddComponent<Dropdown>();dropdown.targetGraphic=rect.GetComponent<SessionPanelGraphic>();
            dropdown.captionText=Label("Caption",rect,"개인전",16,0,w-50,h,22);Label("Arrow",rect,"▼",w-35,0,30,h,20);
            var template=Panel("Template",rect,0,h+3,w,100);var viewport=Rect("Viewport",template,0,0,w,100);viewport.gameObject.AddComponent<RectMask2D>();
            var content=Rect("Content",viewport,0,0,w,48);var item=Rect("Item",content,0,0,w,48);
            var bg=item.gameObject.AddComponent<Image>();bg.color=new Color(.08f,.2f,.3f);
            var toggle=item.gameObject.AddComponent<Toggle>();toggle.targetGraphic=bg;
            var check=Label("Check",item,"✓",12,0,25,48,22);toggle.graphic=check;
            dropdown.itemText=Label("ItemLabel",item,"개인전",45,0,w-60,48,22);
            var scroll=template.gameObject.AddComponent<ScrollRect>();scroll.viewport=viewport;scroll.content=content;scroll.horizontal=false;
            dropdown.template=template;dropdown.options=new System.Collections.Generic.List<Dropdown.OptionData>{new Dropdown.OptionData("개인전"),new Dropdown.OptionData("2:2 팀전 · 화물 운송")};
            template.gameObject.SetActive(false);dropdown.RefreshShownValue();return dropdown;
        }
        void BuildHelp(Transform design)
        {
            var rect=Panel("Help",design,400,100,872,740);help=rect.gameObject;
            Label("Title",rect,"캐릭터 · 조작 설명",30,20,700,55,32);
            string[] names={"솔저","닌자","디바","캐서디"};
            for(int i=0;i<4;i++)MakeButton("Hero"+i,rect,names[i],30+i*205,90,190,48);
            var view=Rect("HelpViewport",rect,30,158,812,465);view.gameObject.AddComponent<RectMask2D>();view.gameObject.AddComponent<Image>().color=Color.clear;
            var content=Rect("HelpContent",view,0,0,790,900);
            Label("Instructions",content,
                "공통  WASD 이동 · Space 점프 · Shift 달리기 · R 재장전\nESC 메뉴 · F8 저부하 모드\n\n"+
                "솔저  좌클릭 연속 사격 · 우클릭 미사일 (6초)\nE: 10초간 탄약 무제한 · 연사·이동속도 2배\n\n"+
                "닌자  좌클릭 수리검 / 카타나 공격 · E 각성 10초 · 종료 후 재사용 20초\nSpace 두 번: 2단 점프\n카타나 상태: Shift 대시 베기 · 우클릭 유지 패링\n\n"+
                "디바  좌클릭 기관포 · 우클릭 방어막\nShift 부스터 · E 탈출·자폭 · 탈출 후 권총 사격\n\n"+
                "캐서디  좌클릭 단발 · 우클릭 남은 탄 연사\nLShift 구르기 · 쿨타임 없음\nE 궁극기 조준 · 좌클릭 발사 · 우클릭 취소\n\n"+
                "팀전  블루 공격 / 레드 수비 · 제한시간 5분\n블루만 화물에 붙으면 전진, 레드만 붙으면 후진\n양 팀이 붙으면 정지 · 아군 스폰에서는 무적\n\n"+
                "개인전  제한시간 내 최다 킬 우승 · 동점은 공동 순위\nAI  빈자리를 자동으로 채웁니다. 방장이 끌 수 있습니다.",0,0,790,900,24);
            var scroll=view.gameObject.AddComponent<ScrollRect>();scroll.content=content;scroll.viewport=view;scroll.horizontal=false;scroll.scrollSensitivity=35;scroll.movementType=ScrollRect.MovementType.Clamped;
            MakeButton("Close",rect,"닫기",30,655,812,55,blue);help.SetActive(false);
        }
    }
}

