using UnityEngine;
using Unity.Netcode;
using UnityEngine.InputSystem;

namespace Blocks.Gameplay.Core
{
    public class RpgDuelUI : MonoBehaviour
    {
        private string code = string.Empty;
        private GameNetworkManager manager;

        private void Start()
        {
            manager = GetComponent<GameNetworkManager>();
            manager.maxPlayers = 8;
            manager.NetworkConfig.ConnectionApproval = true;
            manager.ConnectionApprovalCallback = Approve;
        }

        private void Approve(NetworkManager.ConnectionApprovalRequest request, NetworkManager.ConnectionApprovalResponse response)
        {
            response.Approved = manager.ConnectedClients.Count < 8 && !RpgDuelPlayer.Finished();
            response.CreatePlayerObject = response.Approved;
            response.Pending = false;
            if (!response.Approved) response.Reason = "Arena is full or the match has ended.";
        }

        private void OnDestroy()
        {
            if (manager != null) manager.ConnectionApprovalCallback = null;
        }

        private void OnGUI()
        {
            if (manager == null || manager.NetworkState == null) return;
            bool menuOpen = Cursor.lockState != CursorLockMode.Locked;
            bool scoreboard = menuOpen || (Keyboard.current != null && Keyboard.current.tabKey.isPressed);
            GUILayout.BeginArea(new Rect(20, 20, menuOpen ? 430 : 350, Mathf.Min(Screen.height - 40, scoreboard ? 650 : 210)), GUI.skin.box);
            GUILayout.Label("RPG ARENA | Free for all (2-8) | First to 5 kills");
            GUILayout.Label("WASD: Move  Mouse: Look  LMB: Slash  Q: Heavy  E: Heal");
            var state = manager.NetworkState.ConnectionState;
            if (state == GameNetworkManager.ConnectionStates.None || state == GameNetworkManager.ConnectionStates.Failed)
            {
                if (GUILayout.Button("Create Relay Arena")) manager.StartHostConnection();
                code = GUILayout.TextField(code, 6).Trim().ToUpperInvariant();
                GUI.enabled = code.Length > 0;
                if (GUILayout.Button("Join Arena")) { manager.ClientJoinCode = code; manager.StartClientConnection(); }
                GUI.enabled = true;
            }
            else
            {
                GUILayout.Label(state.ToString());
                if (menuOpen && !string.IsNullOrEmpty(manager.JoinCode))
                {
                    GUILayout.Label("Join Code: " + manager.JoinCode);
                    if (GUILayout.Button("Copy Code")) GUIUtility.systemCopyBuffer = manager.JoinCode;
                }
                if (menuOpen && GUILayout.Button("Disconnect / Cancel")) manager.Disconnect();
                GUILayout.Label(menuOpen ? "F: Resume first-person control" : "Esc: Menu / Cursor   Tab: Scoreboard");
            }
            if (!string.IsNullOrEmpty(manager.ConnectionError)) GUILayout.Label(manager.ConnectionError);
            var players = RpgDuelPlayer.Players();
            if (manager.IsConnectedClient && players.Length < 2) GUILayout.Label("Waiting for an opponent...");
            foreach (var player in players)
            {
                if (!scoreboard && !player.IsOwner && player.Kills.Value < RpgDuelPlayer.WinningKills) continue;
                string label = player.IsOwner ? "YOU" : $"Player {player.OwnerClientId}";
                GUILayout.Label($"{label}   HP {player.Health.Value}/100   Kills {player.Kills.Value}/5");
                if (player.Kills.Value >= RpgDuelPlayer.WinningKills) GUILayout.Label(label + " WINS! Disconnect to start a new duel.");
                if (!player.IsOwner) continue;
                if (player.Health.Value == 0) GUILayout.Label($"Respawn: {Mathf.Max(0, (float)(player.RespawnAt.Value - player.Now)):F1}s");
                GUILayout.Label($"Q cooldown: {Mathf.Max(0, (float)(player.SkillReady.Value - player.Now)):F1}s  E cooldown: {Mathf.Max(0, (float)(player.HealReady.Value - player.Now)):F1}s");
                if (player.Now < player.ProtectedUntil.Value) GUILayout.Label("Spawn protection (ends when attacking)");
            }
            GUILayout.EndArea();
            if (!menuOpen && manager.IsConnectedClient)
            {
                var crosshair = new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleCenter, fontSize = 24 };
                GUI.Label(new Rect(Screen.width / 2f - 15, Screen.height / 2f - 15, 30, 30), "+", crosshair);
            }
        }
    }
}


