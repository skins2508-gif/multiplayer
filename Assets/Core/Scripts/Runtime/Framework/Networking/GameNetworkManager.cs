using UnityEngine;
using Unity.Netcode;
using System;
using System.Threading.Tasks;
using Unity.Netcode.Transports.UTP;
using Unity.Services.Core;
using Unity.Services.Authentication;
using Unity.Services.Relay;
using Unity.Services.Relay.Models;

namespace Blocks.Gameplay.Core
{
    /// <summary>
    /// Manages network connections for the game, extending Unity's <see cref="NetworkManager"/>.
    /// Provides singleton access, connection state management, player name registry, and performance settings.
    /// Supports Host, Client, and Server modes with callbacks for connection events.
    /// </summary>
    public class GameNetworkManager : NetworkManager
    {
        #region Enums

        /// <summary>
        /// Defines the possible connection states for the network manager.
        /// </summary>
        public enum ConnectionStates
        {
            /// <summary>
            /// Not connected to any network session.
            /// </summary>
            None,

            /// <summary>
            /// Currently attempting to establish a connection.
            /// </summary>
            Connecting,

            /// <summary>
            /// Successfully connected to a network session.
            /// </summary>
            Connected,

            /// <summary>
            /// Connection attempt failed or connection was lost.
            /// </summary>
            Failed
        }

        #endregion

        #region Fields & Properties

        /// <summary>
        /// Gets the singleton instance of the GameNetworkManager.
        /// </summary>
        public static GameNetworkManager Instance { get; private set; }

        [Header("Performance Settings")]
        [Tooltip("Target frame rate for the application.")]
        public int targetFrameRate = 60;

        [Tooltip("Enable VSync to synchronize rendering with the display's refresh rate.")]
        public bool enableVSync = false;

        [Header("Player Settings")]
        [Tooltip("The player's display name.")]
        public string PlayerName { get; set; } = "Player";

        /// <summary>
        /// Gets the network state view model used for UI updates.
        /// </summary>
        public NetworkStateViewModel NetworkState { get; private set; }

        private readonly System.Collections.Generic.Dictionary<ulong, string> m_PlayerNames = new System.Collections.Generic.Dictionary<ulong, string>();

        #endregion

        [Header("Relay Settings")]
        [Tooltip("Total players including the host.")]
        [Range(2, 100)] public int maxPlayers = 5;
        public string JoinCode { get; private set; } = string.Empty;
        public string ClientJoinCode { get; set; } = string.Empty;
        public string ConnectionError { get; private set; } = string.Empty;
        private int m_ConnectionAttempt;

        #region Unity Methods

        private void Awake()
        {
            // Enforce singleton pattern
            if (Instance != null && Instance != this)
            {
                Debug.LogWarning("Duplicate GameNetworkManager instance detected. Destroying self.");
                Destroy(gameObject);
                return;
            }

            Instance = this;
            DontDestroyOnLoad(gameObject);

            NetworkState = new NetworkStateViewModel();
            SetFrameRate(targetFrameRate, enableVSync);
        }

        private void Start()
        {
            OnClientConnectedCallback += HandleClientConnected;
            OnClientDisconnectCallback += HandleClientDisconnect;
            OnServerStarted += HandleServerStarted;
            OnTransportFailure += HandleTransportFailure;
        }

        private void OnDestroy()
        {
            if (Instance == this)
            {
                OnClientConnectedCallback -= HandleClientConnected;
                OnClientDisconnectCallback -= HandleClientDisconnect;
                OnServerStarted -= HandleServerStarted;
                OnTransportFailure -= HandleTransportFailure;
                ++m_ConnectionAttempt;
                Instance = null;
            }
        }

        #endregion

        #region Public Methods

        /// <summary>
        /// Starts a network session as a host (both server and client).
        /// Only starts if the current connection state is None or Failed.
        /// </summary>
        public void StartHostConnection() => StartRelayConnection(true);

        public void StartClientConnection() => StartRelayConnection(false);

        private async void StartRelayConnection(bool host)
        {
            if (IsListening || ShutdownInProgress ||
                (NetworkState.ConnectionState != ConnectionStates.None &&
                 NetworkState.ConnectionState != ConnectionStates.Failed)) return;

            int attempt = ++m_ConnectionAttempt;
            ConnectionError = string.Empty;
            JoinCode = string.Empty;
            // Relay cannot initialize without a real Unity Cloud project link.
            if (string.IsNullOrWhiteSpace(Application.cloudProjectId))
            {
                ConnectionError = "Unity Cloud 프로젝트가 연결되지 않았습니다. Edit > Project Settings > Services에서 조직과 프로젝트를 선택하여 연결한 뒤 다시 실행하세요.";
                NetworkState.ConnectionState = ConnectionStates.Failed;
                UpdateViewModel();
                return;
            }
            NetworkState.ConnectionState = ConnectionStates.Connecting;
            UpdateViewModel();
            try
            {
                var transport = NetworkConfig.NetworkTransport as UnityTransport;
                if (transport == null) throw new InvalidOperationException("Relay requires UnityTransport in NetworkConfig.");
                string code = (ClientJoinCode ?? string.Empty).Trim().ToUpperInvariant();
                if (!host && string.IsNullOrEmpty(code))
                    throw new InvalidOperationException("Enter the host's Relay join code.");

                await InitializeServicesAsync();
                if (this == null || attempt != m_ConnectionAttempt) return;
                string protocol = Application.platform == RuntimePlatform.WebGLPlayer ? "wss" : "dtls";
                transport.UseWebSockets = protocol == "wss";
                if (host)
                {
                    var allocation = await RelayService.Instance.CreateAllocationAsync(Mathf.Clamp(maxPlayers, 2, 100) - 1);
                    if (this == null || attempt != m_ConnectionAttempt) return;
                    var joinCode = await RelayService.Instance.GetJoinCodeAsync(allocation.AllocationId);
                    if (this == null || attempt != m_ConnectionAttempt) return;
                    transport.SetRelayServerData(AllocationUtils.ToRelayServerData(allocation, protocol));
                    JoinCode = joinCode;
                    if (!StartHost()) throw new InvalidOperationException("Netcode could not start the Relay host.");
                }
                else
                {
                    var allocation = await RelayService.Instance.JoinAllocationAsync(code);
                    if (this == null || attempt != m_ConnectionAttempt) return;
                    transport.SetRelayServerData(AllocationUtils.ToRelayServerData(allocation, protocol));
                    if (!StartClient()) throw new InvalidOperationException("Netcode could not start the Relay client.");
                }
                UpdateViewModel();
            }
            catch (Exception exception)
            {
                if (this == null || attempt != m_ConnectionAttempt) return;
                ConnectionError = exception.Message;
                JoinCode = string.Empty;
                NetworkState.ConnectionState = ConnectionStates.Failed;
                if (IsListening) Shutdown();
                Debug.LogException(exception, this);
                UpdateViewModel();
            }
        }

        private static async Task InitializeServicesAsync()
        {
            if (UnityServices.State != ServicesInitializationState.Initialized)
                await UnityServices.InitializeAsync();
            if (!AuthenticationService.Instance.IsSignedIn)
                await AuthenticationService.Instance.SignInAnonymouslyAsync();
        }
        /// <summary>
        /// Disconnects from the current network session.
        /// Only disconnects if currently connected or connecting.
        /// </summary>
        public void Disconnect()
        {
            if (NetworkState.ConnectionState == ConnectionStates.None ||
                NetworkState.ConnectionState == ConnectionStates.Failed)
            {
                return;
            }

            ++m_ConnectionAttempt;
            JoinCode = string.Empty;
            ConnectionError = string.Empty;
            Shutdown();
            NetworkState.ConnectionState = ConnectionStates.None;
            UpdateViewModel();
        }

        /// <summary>
        /// Registers a player name for the specified client ID.
        /// If the client ID already exists, the name will be updated.
        /// </summary>
        /// <param name="clientId">The unique client ID.</param>
        /// <param name="playerName">The player's display name.</param>
        public void RegisterPlayerName(ulong clientId, string playerName)
        {
            if (m_PlayerNames.ContainsKey(clientId))
            {
                m_PlayerNames[clientId] = playerName;
            }
            else
            {
                m_PlayerNames.Add(clientId, playerName);
            }
        }

        /// <summary>
        /// Removes a player name from the registry for the specified client ID.
        /// </summary>
        /// <param name="clientId">The unique client ID.</param>
        public void UnregisterPlayerName(ulong clientId)
        {
            m_PlayerNames.Remove(clientId);
        }

        /// <summary>
        /// Attempts to retrieve the player name for the specified client ID.
        /// </summary>
        /// <param name="clientId">The unique client ID.</param>
        /// <param name="playerName">The retrieved player name, if found.</param>
        /// <returns>True if the player name was found; otherwise, false.</returns>
        public bool TryGetPlayerName(ulong clientId, out string playerName)
        {
            return m_PlayerNames.TryGetValue(clientId, out playerName);
        }

        /// <summary>
        /// Gets the player name for the specified client ID.
        /// If no name is registered, returns a default name in the format "Player-{clientId}".
        /// </summary>
        /// <param name="clientId">The unique client ID.</param>
        /// <returns>The player's display name or a default name.</returns>
        public string GetPlayerName(ulong clientId)
        {
            return m_PlayerNames.TryGetValue(clientId, out string name) ? name : $"Player-{clientId}";
        }

        #endregion

        #region Private Methods

        /// <summary>
        /// Sets the application frame rate and VSync settings.
        /// </summary>
        /// <param name="targetRate">The target frame rate.</param>
        /// <param name="vsync">Whether to enable VSync.</param>
        private void SetFrameRate(int targetRate, bool vsync)
        {
            Application.targetFrameRate = targetRate;
            QualitySettings.vSyncCount = vsync ? 1 : 0;
        }

        /// <summary>
        /// Handles the server started callback.
        /// Updates connection state to Connected if running as a dedicated server.
        /// </summary>
        private void HandleTransportFailure()
        {
            ++m_ConnectionAttempt;
            JoinCode = string.Empty;
            ConnectionError = "Relay transport failed. Check your connection and try again.";
            NetworkState.ConnectionState = ConnectionStates.Failed;
            UpdateViewModel();
        }

        private void HandleServerStarted()
        {
            // Only set connected state for dedicated server mode
            if (IsServer && !IsHost && !IsClient)
            {
                NetworkState.ConnectionState = ConnectionStates.Connected;
            }
            UpdateViewModel();
        }

        /// <summary>
        /// Handles the client connected callback.
        /// Updates connection state to Connected when the local client connects.
        /// </summary>
        /// <param name="clientId">The ID of the connected client.</param>
        private void HandleClientConnected(ulong clientId)
        {
            if (clientId == LocalClientId)
            {
                NetworkState.ConnectionState = ConnectionStates.Connected;
            }
            UpdateViewModel();
        }

        /// <summary>
        /// Handles the client disconnect callback.
        /// Updates connection state and cleans up player name registry.
        /// </summary>
        /// <param name="clientId">The ID of the disconnected client.</param>
        private void HandleClientDisconnect(ulong clientId)
        {
            if (clientId == LocalClientId)
            {
                if (NetworkState.ConnectionState != ConnectionStates.None)
                {
                    bool failed = NetworkState.ConnectionState == ConnectionStates.Connecting ||
                                  NetworkState.ConnectionState == ConnectionStates.Failed;
                    NetworkState.ConnectionState = failed ? ConnectionStates.Failed : ConnectionStates.None;
                    JoinCode = string.Empty;
                    if (failed && string.IsNullOrEmpty(ConnectionError))
                        ConnectionError = string.IsNullOrEmpty(DisconnectReason) ? "Could not connect to the Relay host." : DisconnectReason;
                    if (!string.IsNullOrEmpty(DisconnectReason))
                    {
                        Debug.Log($"Disconnect Reason: {DisconnectReason}");
                    }
                }
            }

            UnregisterPlayerName(clientId);
            UpdateViewModel();
        }

        /// <summary>
        /// Updates the network state view model with current connection information.
        /// Determines the netcode status based on the current role (Host, Server, or Client).
        /// </summary>
        private void UpdateViewModel()
        {
            if (NetworkState == null) return;

            string status = "Offline";
            if (IsHost) status = "Host";
            else if (IsServer) status = "Server";
            else if (IsClient) status = "Client";

            NetworkState.UpdateState(
                connectionState: NetworkState.ConnectionState,
                topology: NetworkConfig.NetworkTopology,
                netcodeStatus: status,
                isConnectedOrHost: IsConnectedClient || IsHost
            );
        }

        #endregion
    }
}



