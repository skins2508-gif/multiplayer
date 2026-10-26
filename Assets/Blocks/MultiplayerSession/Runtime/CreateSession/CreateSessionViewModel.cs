using System;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using Unity.Properties;
using Unity.Services.Multiplayer;
using UnityEngine.UIElements;

namespace Blocks.Sessions
{
    public class CreateSessionViewModel : IDisposable, IDataSourceViewHashProvider, INotifyBindablePropertyChanged
    {
        SessionObserver m_SessionObserver;
        ISession m_Session;
        long m_UpdateVersion;
        bool m_Creating, m_Disposed;
        bool m_Recovering;
        Task<IHostSession> m_PendingCreation;

        [CreateProperty]
        public bool CanRegisterSession
        {
            get => m_CanRegisterSession;
            private set
            {
                if (m_CanRegisterSession == value)
                    return;

                m_CanRegisterSession = value;
                ++m_UpdateVersion;
                Notify();
            }
        }
        bool m_CanRegisterSession = true;

        [CreateProperty]
        public bool HasSessionName
        {
            get => m_HasSessionName;
            private set
            {
                if (m_HasSessionName == value)
                    return;

                m_HasSessionName = value;
                Notify();
            }
        }
        bool m_HasSessionName;

        [CreateProperty]
        public string SessionName
        {
            get => m_SessionName;
            private set
            {
                if (m_SessionName == value)
                    return;

                m_SessionName = value;

                HasSessionName = m_SessionName != "";

                ++m_UpdateVersion;
                Notify();
            }
        }
        string m_SessionName;
        public void SetSessionName(string value) => SessionName = value?.Trim() ?? "";

        public CreateSessionViewModel(string sessionType)
        {
            m_SessionObserver = new SessionObserver(sessionType);

            m_SessionObserver.AddingSessionStarted += OnAddingSessionStarted;
            m_SessionObserver.SessionAdded += OnSessionAdded;
            m_SessionObserver.AddingSessionFailed += OnAddingSessionFailed;

            if (m_SessionObserver.Session != null)
            {
                OnSessionAdded(m_SessionObserver.Session);
            }
        }
        void OnAddingSessionFailed(AddingSessionOptions session, SessionException exception) => CanRegisterSession = !m_Creating && !m_Recovering;
        void OnAddingSessionStarted(AddingSessionOptions session) => CanRegisterSession = false;

        void OnSessionAdded(ISession session)
        {
            m_Session = session;
            m_Session.RemovedFromSession += OnSessionRemoved;
            m_Session.Deleted += OnSessionRemoved;
            CanRegisterSession = false;
        }

        void OnSessionRemoved()
        {
            if (m_Session == null) return;
            m_Session.RemovedFromSession -= OnSessionRemoved;
            m_Session.Deleted -= OnSessionRemoved;
            m_Session = null;
            CanRegisterSession = !m_Creating && !m_Recovering;
        }

        public bool AreMultiplayerServicesInitialized()
        {
            return MultiplayerService.Instance != null;
        }

        public async Task<IHostSession> CreateSessionAsync(SessionOptions sessionOptions)
        {
            if (m_Disposed || m_Creating || !CanRegisterSession) throw new InvalidOperationException("이미 방 생성 또는 접속이 진행 중입니다.");
            m_Creating = true;
            CanRegisterSession = false;
            try
            {
                sessionOptions.Name = (sessionOptions.SessionProperties.TryGetValue("shooter-mode", out var mode) ? (mode.Value == "escort" ? "[2대2 운송] " : "[개인전] ") : "") + SessionName;
                m_PendingCreation = MultiplayerService.Instance.CreateSessionAsync(sessionOptions);
                var session = await m_PendingCreation;
                if (m_Recovering) throw new OperationCanceledException("연결 새로고침 요청으로 생성한 방을 정리합니다.");
                return session;
            }
            finally
            {
                m_Creating = false;
                if (!m_Recovering) m_PendingCreation = null;
                if (!m_Disposed) CanRegisterSession = !m_Recovering && m_Session == null && m_SessionObserver?.Session == null;
            }
        }

        public async Task RefreshConnectionAsync()
        {
            if (m_Disposed || m_Recovering) return;
            // A healthy joined room must be left using its normal lobby button.
            if (!m_Creating && m_SessionObserver?.Session != null)
                throw new InvalidOperationException("현재 방에서 나간 뒤 새로고침해 주세요.");
            m_Recovering = true;
            CanRegisterSession = false;
            try
            {
                IHostSession created = null;
                if (m_PendingCreation != null)
                {
                    try { created = await m_PendingCreation; }
                    catch (Exception) { /* The failed request has settled; retry is now safe. */ }
                }
                var session = (ISession)created ?? m_SessionObserver?.Session;
                if (session != null) await session.LeaveAsync();
                m_PendingCreation = null;
            }
            finally
            {
                m_Recovering = false;
                if (!m_Disposed) CanRegisterSession = !m_Creating && m_Session == null && m_SessionObserver?.Session == null;
            }
        }

        public void Dispose()
        {
            m_Disposed = true;
            if (m_SessionObserver != null)
            {
                m_SessionObserver.AddingSessionStarted -= OnAddingSessionStarted;
                m_SessionObserver.SessionAdded -= OnSessionAdded;
                m_SessionObserver.AddingSessionFailed -= OnAddingSessionFailed;
                m_SessionObserver.Dispose();
                m_SessionObserver = null;
            }
            if (m_Session != null)
            {
                m_Session.RemovedFromSession -= OnSessionRemoved;
                m_Session.Deleted -= OnSessionRemoved;
                m_Session = null;
            }
        }

        /// <summary>
        /// This method is used by UIToolkit to determine if any data bound to the UI has changed.
        /// Instead of hashing the data, an m_UpdateVersion counter is incremented when changes occur.
        /// </summary>
        public long GetViewHashCode() => m_UpdateVersion;

        /// <summary>
        /// Suggested implementation of INotifyBindablePropertyChanged from UIToolkit.
        /// </summary>
        public event EventHandler<BindablePropertyChangedEventArgs> propertyChanged;
        void Notify([CallerMemberName] string property = null) =>
            propertyChanged?.Invoke(this, new BindablePropertyChangedEventArgs(property));
    }
}
