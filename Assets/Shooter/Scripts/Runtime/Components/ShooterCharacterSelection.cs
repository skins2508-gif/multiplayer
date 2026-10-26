using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;
using Unity.Netcode;

namespace Blocks.Gameplay.Shooter
{
    /// <summary>Pre-session selection for the existing multiplayer shooter scene.</summary>
    public sealed class ShooterCharacterSelection : MonoBehaviour
    {
        public static int SelectedCharacter { get; private set; }
        const string SceneName = "[BB] Shooter MultiplayerSession";
        const string PreferenceKey = "Shooter.SelectedCharacter";
        VisualElement panel;
        Button shooterButton, ninjaButton, dvaButton, revolverButton;
        Label selectedLabel;
        bool browserHiddenForSession;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetState()
        {
            SelectedCharacter = Mathf.Clamp(PlayerPrefs.GetInt(PreferenceKey, 0), 0, 3);
            SceneManager.sceneLoaded -= OnSceneLoaded;
            SceneManager.sceneLoaded += OnSceneLoaded;
        }

        static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            if (scene.name != SceneName) return;
            if (FindAnyObjectByType<ShooterSessionCanvas>() != null) return;
            foreach (var root in scene.GetRootGameObjects())
                foreach (var document in root.GetComponentsInChildren<UIDocument>(true))
                    if (document.GetComponent<ShooterCharacterSelection>() == null)
                        document.gameObject.AddComponent<ShooterCharacterSelection>();
        }

        void Update()
        {
            var document = GetComponent<UIDocument>();
            if (document == null || document.rootVisualElement == null) return;
            if (panel == null || panel.parent != document.rootVisualElement) Build(document.rootVisualElement);
            var manager = NetworkManager.Singleton;
            // Freeze selection as soon as networking starts; the spawned owner captures this value.
            bool connecting = manager != null && manager.IsListening;
            panel.style.display = connecting ? DisplayStyle.None : DisplayStyle.Flex;
            // This document contains the actual browser, which may differ from the
            // document referenced by GameManager's service-connection fade.
            var root = document.rootVisualElement;
            if (root.Q<VisualElement>(className: "nexus-screen") != null)
            {
                var match = ShooterEscortMatch.Instance;
                bool inSession = (manager != null && manager.IsConnectedClient) ||
                    (match != null && match.IsSpawned);
                bool hide = inSession && !(match != null && match.ReturningToSessionBrowser);
                if (hide)
                {
                    root.style.display = DisplayStyle.None;
                    browserHiddenForSession = true;
                }
                else if (browserHiddenForSession)
                {
                    root.style.display = DisplayStyle.Flex;
                    root.style.opacity = 1;
                    browserHiddenForSession = false;
                }
            }
        }

        void Build(VisualElement root)
        {
            panel?.RemoveFromHierarchy();
            // Keep this layout local to the Shooter scene, not the shared session templates.
            var sessionLayout = root.Q<VisualElement>(className: "blocks-container--horizontal--grow");
            if (sessionLayout != null)
            {
                ShooterSessionBrowserSkin.Apply(root, sessionLayout);
            }
            panel = new VisualElement { name = "shooter-character-selection" };
            panel.style.position = Position.Absolute;
            panel.style.top = 60;
            panel.style.right = 24;
            panel.style.width = Length.Percent(28);
            panel.style.minWidth = 190;
            panel.style.maxWidth = 320;
            panel.style.paddingTop = panel.style.paddingBottom = 12;
            panel.style.paddingLeft = panel.style.paddingRight = 12;
            panel.style.backgroundColor = new Color(0.07f, 0.09f, 0.14f, 0.97f);
            panel.style.color = Color.white;
            panel.Add(new Label("캐릭터 선택"));
            shooterButton = CreateCharacterButton(0, "솔저", "중거리 · 지속 사격");
            ninjaButton = CreateCharacterButton(1, "겐지", "기동 · 수리검 · 검격");
            panel.Add(shooterButton);
            panel.Add(ninjaButton);
            dvaButton = CreateCharacterButton(2, "디바 메카", "근거리 · 방어 · 탈출");
            panel.Add(dvaButton);
            revolverButton = CreateCharacterButton(3, "캐서디", "정밀 사격 · 리볼버 6발");
            panel.Add(revolverButton);
            selectedLabel = new Label();
            panel.Add(selectedLabel);
            var help = new VisualElement();
            help.style.position = Position.Absolute;
            help.style.left = help.style.right = help.style.top = help.style.bottom = 0;
            help.style.backgroundColor = new Color(.07f,.09f,.14f,1);
            help.style.paddingLeft = help.style.paddingRight = 12;
            help.style.paddingTop = help.style.paddingBottom = 12;
            help.style.display = DisplayStyle.None;
            var openHelp = new Button(() => help.style.display = DisplayStyle.Flex) { text = "조작 설명" };
            openHelp.style.height = 34;openHelp.style.marginTop = 8;openHelp.style.flexShrink = 0;
            panel.Add(openHelp);
            var closeHelp = new Button(() => help.style.display = DisplayStyle.None) { text = "캐릭터 선택으로 돌아가기" };
            closeHelp.style.height = 34;closeHelp.style.flexShrink = 0;help.Add(closeHelp);
            var scroll = new ScrollView();scroll.style.flexGrow = 1;help.Add(scroll);
            var instructions = new Label(
                "조작 설명\n\n공통\nWASD 이동 · 마우스 시점\nSpace 점프 · Shift 달리기\nR 재장전 · 탄 소진 시 자동 재장전\nF8 저부하 모드 전환\n\n" +
                "솔저 · 중거리 지속 화력\n라이플로 압박하고 미사일로 마무리하는 캐릭터입니다.\n좌클릭 유지: 연속 사격\n우클릭: 미사일 발사 · 재사용 6초\nE: 5초간 탄약 무제한 · 연사·이동속도 2배\n효과 종료 후 재사용 7.5초\n\n" +
                "겐지 · 기동형 근접 공격\n수리검으로 견제한 뒤 각성 대시로 접근하거나 이탈합니다.\n좌클릭: 수리검 3연발 / 카타나 베기\nE: 각성 5초 (종료 후 재사용 10초)\nSpace 두 번: 2단 점프\n각성 중 왼쪽 Shift: 대시 횡베기\n대시 재사용 0.1초 · 명중 피해 10\n카타나 상태 우클릭 유지: 패링\n\n" +
                "디바 · 근거리 압박과 생존\n기관포와 방어 매트릭스로 전선을 유지합니다.\n좌클릭 유지: 양손 기관포\n우클릭 유지: 방어 매트릭스 · 전방 탄환 차단\nShift: 공중 부스터 (스태미나 소모)\nE: 탈출·자폭\n부스터 중 자폭: 로봇을 앞으로 발사\n로봇 체력 소진 시 파일럿으로 자동 탈출\n탈출 후 좌클릭: 권총 단발 (7발)\n발사 간격 0.14초 · 발당 피해 20\nR: 권총 재장전 1.4초\n파일럿 상태에서는 엄폐하며 생존하세요.\n\n" +
                "캐서디 · 정밀 사격과 순간 화력\n단발로 조준하고 가까운 적에게 남은 탄을 집중합니다.\n좌클릭: 리볼버 단발\n우클릭: 남은 탄 연사\nLShift: 구르기 · 쿨타임 없음\nE: 황야의 무법자 조준 시작\n좌클릭: 즉시 발사 · 우클릭: 취소\n최대 3초 조준 · 재사용 15초\n\n" +
                "팀전\n블루 공격 / 레드 수비\n공격팀만 붙으면 화물 전진\n수비팀만 붙으면 출발 지점 방향으로 후진\n양 팀이 붙으면 운송 정지\n출발 지점보다 뒤로는 이동하지 않음\n아군 스폰에서는 무적 / 공격·운송 불가\n\n개인전\n제한시간 내 최다 킬이 1등\n동점은 공동 순위");
            instructions.style.whiteSpace = WhiteSpace.Normal;
            instructions.style.fontSize = 14;instructions.style.marginTop = 12;
            scroll.Add(instructions);panel.Add(help);
            root.Add(panel);
            panel.style.visibility = Visibility.Hidden;
            var toggle = new Button(() => panel.style.visibility = panel.resolvedStyle.visibility == Visibility.Hidden ? Visibility.Visible : Visibility.Hidden) { text = "캐릭터 · 조작 설명" };
            toggle.style.position = Position.Absolute;
            toggle.style.left = 24;
            toggle.style.bottom = 12;
            toggle.style.height = 36;
            toggle.style.backgroundColor = new Color(.04f,.12f,.20f,.96f);
            toggle.style.color = Color.white;
            root.Add(toggle);
            Refresh();
        }

        Button CreateCharacterButton(int character, string title, string subtitle)
        {
            var button = new Button(() => Select(character));
            button.tooltip = title + " 선택";
            button.style.height = 92;
            button.style.marginTop = 10;
            button.style.alignItems = Align.Center;
            button.style.justifyContent = Justify.Center;
            var icon = new VisualElement { pickingMode = PickingMode.Ignore };
            icon.style.width = 80;
            icon.style.height = 54;
            icon.generateVisualContent += context => DrawWeaponIcon(context, character);
            button.Add(icon);
            var nameLabel = new Label(title) { pickingMode = PickingMode.Ignore };
            nameLabel.style.fontSize = 18;
            button.Add(nameLabel);
            var detail = new Label(subtitle) { pickingMode = PickingMode.Ignore };
            detail.style.fontSize = 12;
            button.Add(detail);
            return button;
        }

        static void DrawWeaponIcon(MeshGenerationContext context, int character)
        {
            var painter = context.painter2D;
            if (character == 2)
            {
                painter.strokeColor = new Color(1f, 0.4f, 0.7f);
                painter.lineWidth = 12;
                painter.BeginPath(); painter.MoveTo(new Vector2(20, 12)); painter.LineTo(new Vector2(20, 51)); painter.Stroke();
                painter.BeginPath(); painter.MoveTo(new Vector2(60, 12)); painter.LineTo(new Vector2(60, 51)); painter.Stroke();
                painter.lineWidth = 5;
                painter.BeginPath(); painter.MoveTo(new Vector2(20, 38)); painter.LineTo(new Vector2(60, 38)); painter.Stroke();
                return;
            }
            painter.fillColor = new Color(0.85f, 0.94f, 1f);
            // Vector silhouettes avoid dependence on emoji or installed symbol fonts.
            Vector2[] points = character == 0 || character == 3
                ? new[] { new Vector2(8, 19), new Vector2(66, 19), new Vector2(72, 23),
                    new Vector2(72, 31), new Vector2(40, 31), new Vector2(35, 51),
                    new Vector2(20, 51), new Vector2(24, 31), new Vector2(8, 31) }
                : new[] { new Vector2(28, 39), new Vector2(61, 6), new Vector2(68, 4),
                    new Vector2(66, 14), new Vector2(35, 46) };
            painter.BeginPath();
            painter.MoveTo(points[0]);
            for (int i = 1; i < points.Length; i++) painter.LineTo(points[i]);
            painter.ClosePath();
            painter.Fill();
            if (character == 1)
            {
                painter.strokeColor = new Color(0.35f, 0.85f, 1f);
                painter.lineWidth = 6;
                painter.BeginPath();
                painter.MoveTo(new Vector2(22, 35));
                painter.LineTo(new Vector2(40, 51));
                painter.Stroke();
                painter.lineWidth = 8;
                painter.BeginPath();
                painter.MoveTo(new Vector2(28, 46));
                painter.LineTo(new Vector2(17, 58));
                painter.Stroke();
            }
        }

        void Select(int character)
        {
            if (NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening) return;
            SetInitialCharacter(character);
            Refresh();
        }

        public static void SetInitialCharacter(int character)
        {
            if (NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening) return;
            character = Mathf.Clamp(character, 0, 3);
            SelectedCharacter = character;
            PlayerPrefs.SetInt(PreferenceKey, character);
            PlayerPrefs.Save();
        }

        void Refresh()
        {
            selectedLabel.text = SelectedCharacter == 0 ? "선택됨: 1번 솔저" : SelectedCharacter == 1 ? "선택됨: 2번 겐지" : SelectedCharacter == 2 ? "선택됨: 3번 디바 메카" : "선택됨: 4번 캐서디";
            Highlight(shooterButton, SelectedCharacter == 0);
            Highlight(ninjaButton, SelectedCharacter == 1);
            Highlight(dvaButton, SelectedCharacter == 2);
            Highlight(revolverButton, SelectedCharacter == 3);
        }

        static void Highlight(Button button, bool selected)
        {
            button.style.backgroundColor = selected ? new Color(0.10f, 0.30f, 0.42f) : new Color(0.12f, 0.15f, 0.21f);
            button.style.color = Color.white;
            var border = selected ? new Color(0.35f, 0.85f, 1f) : new Color(0.25f, 0.29f, 0.35f);
            button.style.borderTopColor = button.style.borderBottomColor = border;
            button.style.borderLeftColor = button.style.borderRightColor = border;
            button.style.borderTopWidth = button.style.borderBottomWidth = 2;
            button.style.borderLeftWidth = button.style.borderRightWidth = 2;
        }

        void OnDestroy() { panel?.RemoveFromHierarchy(); }
    }
}

