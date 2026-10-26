using System.Collections.Generic;
using System;
using System.Threading.Tasks;
using Blocks.Common;
using Blocks.Sessions.Common;
using Unity.Properties;
using UnityEngine;
using UnityEngine.UIElements;

namespace Blocks.Sessions
{
    [UxmlElement]
    public partial class CreateSessionElement : VisualElement
    {
        const string k_EnterSessionNamePlaceholder = "Enter Session Name";
        const string k_CreateButtonText = "CREATE";

        [CreateProperty, UxmlAttribute]
        public SessionSettings SessionSettings
        {
            get => m_SessionSettings;
            set
            {
                if (m_SessionSettings == value)
                    return;

                m_SessionSettings = value;
                if (panel != null)
                    UpdateBindings();
            }
        }
        SessionSettings m_SessionSettings;
        DropdownField m_ShooterMode;
        Label m_CreateStatus;
        bool m_Creating;
        bool m_Refreshing;
        Button m_RecoveryButton;

        CreateSessionViewModel m_ViewModel;

        readonly List<DataBinding> m_Bindings = new();

        public CreateSessionElement()
        {
            AddToClassList(BlocksTheme.ContainerHorizontal);
            // Wrapping centered rows can overlap when their parent is height-constrained.
            // Reserve a separate, non-shrinking row for recovery and status controls.
            style.flexDirection = FlexDirection.Column;
            style.alignItems = Align.Stretch;
            style.justifyContent = Justify.FlexStart;
            style.flexShrink = 0;
            style.marginBottom = 10;
            var controls = new VisualElement();
            controls.AddToClassList(BlocksTheme.ContainerHorizontal);
            controls.style.flexDirection = FlexDirection.Column;
            controls.style.alignItems = Align.Stretch;
            controls.style.justifyContent = Justify.FlexStart;
            controls.style.flexShrink = 0;
            Add(controls);

            var enabledBinding = new DataBinding
            {
                dataSourcePath = new PropertyPath(nameof(m_ViewModel.CanRegisterSession)),
                bindingMode = BindingMode.ToTarget
            };
            controls.SetBinding(new BindingId(nameof(enabledSelf)), enabledBinding);
            m_Bindings.Add(enabledBinding);

            var sessionNameTextField = new TextField
            {
                textEdition =
                {
                    placeholder = k_EnterSessionNamePlaceholder,
                    hidePlaceholderOnFocus = true
                }
            };
            sessionNameTextField.AddToClassList(BlocksTheme.TextField);
            sessionNameTextField.AddToClassList(BlocksTheme.SpaceRight);
            sessionNameTextField.style.marginRight = 0;
            sessionNameTextField.style.marginBottom = 6;
            sessionNameTextField.style.minHeight = 32;
            sessionNameTextField.style.flexShrink = 0;
            var sessionNameBinding = new DataBinding
            {
                dataSourcePath = new PropertyPath(nameof(m_ViewModel.SessionName)),
                bindingMode = BindingMode.ToSource
            };
            sessionNameTextField.SetBinding("value", sessionNameBinding);
            controls.Add(sessionNameTextField);
            m_Bindings.Add(sessionNameBinding);

            var createSessionButton = new Button
            {
                text = k_CreateButtonText
            };
            createSessionButton.AddToClassList(BlocksTheme.Button);
            createSessionButton.style.height = 36;
            createSessionButton.style.minHeight = 36;
            createSessionButton.style.flexShrink = 0;
            var createSessionBinding = new DataBinding
            {
                dataSourcePath = new PropertyPath(nameof(m_ViewModel.HasSessionName)),
                bindingMode = BindingMode.ToTarget
            };
            createSessionButton.SetBinding(new BindingId(nameof(enabledSelf)), createSessionBinding);
            createSessionButton.clicked += CreateSession;
            controls.Add(createSessionButton);
            if (UnityEngine.SceneManagement.SceneManager.GetActiveScene().name == "[BB] Shooter MultiplayerSession")
            {
                m_ShooterMode = new DropdownField("게임 모드", new List<string> { "개인전", "팀전 · 화물 운송" }, 0);
                m_ShooterMode.style.minWidth = 230;
                m_ShooterMode.style.minHeight = 30;
                m_ShooterMode.style.marginBottom = 6;
                m_ShooterMode.style.flexShrink = 0;
                controls.Insert(0, m_ShooterMode);
            }
            m_Bindings.Add(createSessionBinding);
            style.flexWrap = Wrap.NoWrap;
            m_RecoveryButton = new Button(RefreshConnection) { text = "연결 새로고침" };
            m_RecoveryButton.AddToClassList(BlocksTheme.Button);
            m_RecoveryButton.style.height = 36;
            m_RecoveryButton.style.minHeight = 36;
            m_RecoveryButton.style.flexShrink = 0;
            m_RecoveryButton.style.marginTop = 8;
            Add(m_RecoveryButton);
            m_CreateStatus = new Label();
            m_CreateStatus.style.width = Length.Percent(100);
            m_CreateStatus.style.whiteSpace = WhiteSpace.Normal;
            m_CreateStatus.style.color = Color.white;
            m_CreateStatus.style.flexShrink = 0;
            m_CreateStatus.style.marginTop = 6;
            Add(m_CreateStatus);

            RegisterCallback<AttachToPanelEvent>(_ => UpdateBindings());
            RegisterCallback<DetachFromPanelEvent>(_ => CleanupBindings());
        }

        async void CreateSession()
        {
            if (m_Creating || m_Refreshing || m_ViewModel == null || !m_ViewModel.CanRegisterSession) return;
            var viewModel = m_ViewModel;
            m_Creating = true;
            try
            {
            if (!SessionSettings)
            {
                m_CreateStatus.text = "방 설정이 없습니다. SessionSettings를 확인해 주세요.";
                return;
            }
            if (!m_ViewModel.AreMultiplayerServicesInitialized())
            {
                m_CreateStatus.text = "온라인 서비스 초기화 중입니다. 잠시 후 다시 시도해 주세요.";
                return;
            }

            var options = SessionSettings.ToSessionOptions();
            if (m_ShooterMode != null)
            {
                options.MaxPlayers = 4;
                options.SessionProperties["shooter-mode"] = new Unity.Services.Multiplayer.SessionProperty(m_ShooterMode.index == 1 ? "escort" : "ffa");
            }
            m_CreateStatus.text = "방 생성 중 · 서버 연결을 기다리고 있습니다…";
            var creation = viewModel.CreateSessionAsync(options);
            if (await Task.WhenAny(creation, Task.Delay(10000)) != creation && !m_Refreshing && m_ViewModel == viewModel && panel != null)
                m_CreateStatus.text = "서버 응답이 지연되고 있습니다. 연결 대기 중…";
            await creation;
            if (!m_Refreshing && m_ViewModel == viewModel && panel != null) m_CreateStatus.text = "방 생성 완료";
            }
            catch (Exception exception)
            {
                if (!m_Refreshing && m_ViewModel == viewModel && panel != null)
                    m_CreateStatus.text = "방 생성 실패. 연결을 확인한 뒤 다시 시도해 주세요. (" + exception.GetType().Name + ")";
                if (!(exception is OperationCanceledException)) Debug.LogWarning($"[CreateSession] 방 생성 실패: {exception}");
            }
            finally { m_Creating = false; }
        }

        async void RefreshConnection()
        {
            if (m_Refreshing || m_ViewModel == null) return;
            var viewModel = m_ViewModel;
            m_Refreshing = true;
            m_RecoveryButton.SetEnabled(false);
            m_CreateStatus.text = "연결 정리 중…";
            try
            {
                var recovery = viewModel.RefreshConnectionAsync();
                if (await Task.WhenAny(recovery, Task.Delay(10000)) != recovery && m_ViewModel == viewModel && panel != null)
                    m_CreateStatus.text = "이전 요청 종료를 기다리고 있습니다. 중복 방 생성 방지를 위해 잠시 기다려 주세요.";
                await recovery;
                if (m_ViewModel != viewModel || panel == null) return;
                var browsers = panel.visualTree.Query<SessionBrowserElement>().ToList();
                foreach (var browser in browsers) await browser.RefreshSessionListAsync();
                if (m_ViewModel == viewModel && panel != null)
                    m_CreateStatus.text = "새로고침 완료. 방을 다시 만들거나 목록에서 선택해 주세요.";
            }
            catch (Exception exception)
            {
                if (m_ViewModel == viewModel && panel != null)
                    m_CreateStatus.text = "새로고침 실패: " + exception.Message;
                Debug.LogWarning($"[CreateSession] 연결 복구 실패: {exception}");
            }
            finally { m_Refreshing = false; m_RecoveryButton.SetEnabled(true); }
        }

        void UpdateBindings()
        {
            CleanupBindings();

            m_ViewModel = new CreateSessionViewModel(SessionSettings?.sessionType);
            foreach (var binding in m_Bindings)
            {
                binding.dataSource = m_ViewModel;
            }
        }

        void CleanupBindings()
        {
            m_ViewModel?.Dispose();
            m_ViewModel = null;

            foreach (var binding in m_Bindings)
            {
                binding.dataSource = null;
            }
        }
    }
}
