using UnityEngine;
using Unity.Netcode;
using Unity.Netcode.Components;
using UnityEngine.InputSystem;

namespace Blocks.Gameplay.Core
{
    /// <summary>Small host-authoritative RPG duel prototype. Clients submit input, never damage.</summary>
    [RequireComponent(typeof(NetworkObject), typeof(NetworkTransform))]
    public class RpgDuelPlayer : NetworkBehaviour
    {
        public const int WinningKills = 5;
        public readonly NetworkVariable<int> Health = new NetworkVariable<int>(100);
        public readonly NetworkVariable<int> Kills = new NetworkVariable<int>();
        public readonly NetworkVariable<double> AttackReady = new NetworkVariable<double>();
        public readonly NetworkVariable<double> SkillReady = new NetworkVariable<double>();
        public readonly NetworkVariable<double> HealReady = new NetworkVariable<double>();
        public readonly NetworkVariable<double> RespawnAt = new NetworkVariable<double>();
        public readonly NetworkVariable<double> ProtectedUntil = new NetworkVariable<double>();
        [Range(5, 100)] public float arenaHalfExtent = 9;
        [Range(0.01f, 1)] public float mouseSensitivity = 0.12f;
        private Camera firstPersonCamera;
        private Camera overviewCamera;
        private AudioListener overviewAudio;
        private bool overviewAudioWasEnabled;
        private float viewYaw;
        private float viewPitch;
        private CursorLockMode previousCursorLock;
        private bool previousCursorVisible;
        private CharacterController controller;
        private float verticalSpeed;
        private Vector2 movement;
        private double lastInput;
        private double nextInputSend;
        private int spawnSlot;
        private NetworkTransform networkTransform;
        public double Now => NetworkManager.ServerTime.Time;

        public override void OnNetworkSpawn()
        {
            networkTransform = GetComponent<NetworkTransform>();
            var capsule = GetComponent<CapsuleCollider>();
            if (capsule != null) capsule.enabled = false;
            controller = GetComponent<CharacterController>();
            if (controller == null) controller = gameObject.AddComponent<CharacterController>();
            controller.height = 2;
            controller.radius = 0.45f;
            controller.stepOffset = 0.3f;
            controller.slopeLimit = 45;
            controller.enabled = IsServer;
            if (IsServer)
            {
                var usedSlots = new System.Collections.Generic.HashSet<int>();
                foreach (var other in Players())
                    if (other != this && other.IsSpawned) usedSlots.Add(other.spawnSlot);
                while (usedSlots.Contains(spawnSlot)) spawnSlot++;
                ResetPosition();
                ProtectedUntil.Value = Now + 2;
            }
            var renderer = GetComponentInChildren<Renderer>();
            if (renderer != null) renderer.material.color = IsOwner ? new Color(0.2f, 0.65f, 1) : new Color(1, 0.3f, 0.25f);
            if (IsOwner) CreateFirstPersonCamera();
        }

        public static RpgDuelPlayer[] Players() => Object.FindObjectsByType<RpgDuelPlayer>(FindObjectsSortMode.None);
        public static bool Finished()
        {
            foreach (var player in Players()) if (player.Kills.Value >= WinningKills) return true;
            return false;
        }

        private void Update()
        {
            if (!IsSpawned) return;
            if (IsOwner && Keyboard.current != null)
            {
                var keys = Keyboard.current;
                var mouse = Mouse.current;
                if (keys.escapeKey.wasPressedThisFrame) SetCursorLocked(false);
                // F resumes mouse capture without treating a menu click as an attack.
                if (keys.fKey.wasPressedThisFrame && Application.isFocused) SetCursorLocked(true);
                bool canControl = Application.isFocused && Cursor.lockState == CursorLockMode.Locked && Health.Value > 0 && !Finished();
                if (canControl && mouse != null)
                {
                    Vector2 look = mouse.delta.ReadValue() * mouseSensitivity;
                    viewYaw = Mathf.Repeat(viewYaw + look.x, 360);
                    viewPitch = Mathf.Clamp(viewPitch - look.y, -85, 85);
                }
                var input = canControl ? new Vector2((keys.dKey.isPressed ? 1 : 0) - (keys.aKey.isPressed ? 1 : 0),
                    (keys.wKey.isPressed ? 1 : 0) - (keys.sKey.isPressed ? 1 : 0)) : Vector2.zero;
                if (Now >= nextInputSend)
                {
                    nextInputSend = Now + 0.05;
                    MoveRpc(input, viewYaw);
                }
                if (canControl)
                {
                    if (keys.spaceKey.wasPressedThisFrame || (mouse != null && mouse.leftButton.wasPressedThisFrame))
                        AttackRpc(false, viewYaw, viewPitch);
                    if (keys.qKey.wasPressedThisFrame) AttackRpc(true, viewYaw, viewPitch);
                    if (keys.eKey.wasPressedThisFrame) HealRpc();
                }
            }
            if (!IsServer) return;
            if (Health.Value <= 0)
            {
                if (!Finished() && Now >= RespawnAt.Value)
                {
                    Health.Value = 100;
                    ResetPosition();
                    ProtectedUntil.Value = Now + 2;
                }
                return;
            }
            if (Finished()) return;
            if (Now - lastInput > 0.25) movement = Vector2.zero;
            Vector3 direction = transform.rotation * new Vector3(movement.x, 0, movement.y);
            if (controller.isGrounded && verticalSpeed < 0) verticalSpeed = -2;
            verticalSpeed = Mathf.Max(-30, verticalSpeed - 20 * Time.deltaTime);
            Vector3 velocity = direction * 5;
            velocity.y = verticalSpeed;
            Vector3 delta = velocity * Time.deltaTime;
            delta.x = Mathf.Clamp(transform.position.x + delta.x, -arenaHalfExtent, arenaHalfExtent) - transform.position.x;
            delta.z = Mathf.Clamp(transform.position.z + delta.z, -arenaHalfExtent, arenaHalfExtent) - transform.position.z;
            controller.Move(delta);
            if (transform.position.y < -10) ResetPosition();

        }

        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Owner, Delivery = RpcDelivery.Unreliable)]
        private void MoveRpc(Vector2 input, float yaw)
        {
            if (float.IsNaN(input.x) || float.IsNaN(input.y) || float.IsInfinity(input.x) || float.IsInfinity(input.y)) return;
            if (!ValidAngle(yaw)) return;
            transform.rotation = Quaternion.Euler(0, Mathf.Repeat(yaw, 360), 0);
            movement = Vector2.ClampMagnitude(input, 1);
            lastInput = Now;
        }

        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Owner)]
        private void AttackRpc(bool skill, float yaw, float pitch)
        {
            if (Health.Value <= 0 || Finished() || Players().Length < 2) return;
            if (!ValidAngle(yaw) || !ValidAngle(pitch)) return;
            if (Now < AttackReady.Value || (skill && Now < SkillReady.Value)) return;
            transform.rotation = Quaternion.Euler(0, Mathf.Repeat(yaw, 360), 0);
            Vector3 attackDirection = Quaternion.Euler(Mathf.Clamp(pitch, -85, 85), yaw, 0) * Vector3.forward;
            AttackReady.Value = Now + (skill ? 0.8 : 0.6);
            if (skill) SkillReady.Value = Now + 5;
            ProtectedUntil.Value = 0;
            float range = skill ? 3.5f : 2.5f;
            foreach (var target in Players())
            {
                if (target == this || target.Health.Value <= 0 || Now < target.ProtectedUntil.Value) continue;
                Vector3 offset = target.transform.position - transform.position;
                if (offset.magnitude > range || Vector3.Angle(attackDirection, offset) > 70) continue;
                // Test world geometry, excluding fighters, so melee cannot cross cover or floors.
                bool blocked = false;
                foreach (var hit in Physics.RaycastAll(transform.position, offset.normalized, offset.magnitude, ~0, QueryTriggerInteraction.Ignore))
                {
                    if (hit.collider.GetComponentInParent<RpgDuelPlayer>() == null) { blocked = true; break; }
                }
                if (blocked) continue;
                target.Health.Value = Mathf.Max(0, target.Health.Value - (skill ? 40 : 20));
                if (target.Health.Value == 0)
                {
                    Kills.Value++;
                    target.RespawnAt.Value = Now + 3;
                    target.movement = Vector2.zero;
                }
            }
            AttackFeedbackRpc(skill);
        }

        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Owner)]
        private void HealRpc()
        {
            if (Health.Value <= 0 || Health.Value >= 100 || Finished() || Now < HealReady.Value || Players().Length < 2) return;
            HealReady.Value = Now + 10;
            Health.Value = Mathf.Min(100, Health.Value + 30);
        }

        [Rpc(SendTo.Everyone)]
        private void AttackFeedbackRpc(bool skill)
        {
            var effect = GameObject.CreatePrimitive(PrimitiveType.Cube);
            Destroy(effect.GetComponent<Collider>());
            effect.transform.SetPositionAndRotation(transform.position + transform.forward * 1.5f, transform.rotation);
            effect.transform.localScale = new Vector3(skill ? 2 : 1, 0.15f, skill ? 2 : 1.5f);
            effect.GetComponent<Renderer>().material.color = skill ? Color.yellow : Color.white;
            Destroy(effect, 0.15f);
        }

        private static bool ValidAngle(float value) => !float.IsNaN(value) && !float.IsInfinity(value);

        private void CreateFirstPersonCamera()
        {
            overviewCamera = Camera.main;
            if (overviewCamera != null)
            {
                overviewCamera.enabled = false;
                overviewAudio = overviewCamera.GetComponent<AudioListener>();
                if (overviewAudio != null)
                {
                    overviewAudioWasEnabled = overviewAudio.enabled;
                    overviewAudio.enabled = false;
                }
            }
            firstPersonCamera = new GameObject("Local First Person Camera").AddComponent<Camera>();
            firstPersonCamera.tag = "MainCamera";
            firstPersonCamera.fieldOfView = 75;
            firstPersonCamera.nearClipPlane = 0.05f;
            firstPersonCamera.gameObject.AddComponent<AudioListener>();
            viewYaw = transform.eulerAngles.y;
            foreach (var body in GetComponentsInChildren<Renderer>())
                body.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.ShadowsOnly;
            previousCursorLock = Cursor.lockState;
            previousCursorVisible = Cursor.visible;
            SetCursorLocked(Application.isFocused);
            UpdateCamera();
        }

        private static void SetCursorLocked(bool locked)
        {
            Cursor.lockState = locked ? CursorLockMode.Locked : CursorLockMode.None;
            Cursor.visible = !locked;
        }

        private void LateUpdate()
        {
            if (IsOwner && firstPersonCamera != null) UpdateCamera();
        }

        private void UpdateCamera()
        {
            firstPersonCamera.transform.SetPositionAndRotation(transform.position + Vector3.up * 0.65f,
                Quaternion.Euler(viewPitch, viewYaw, 0));
        }

        private void OnApplicationFocus(bool focused)
        {
            if (!focused && IsOwner && firstPersonCamera != null) SetCursorLocked(false);
        }

        public override void OnNetworkDespawn()
        {
            ReleaseCamera();
            base.OnNetworkDespawn();
        }

        public override void OnDestroy()
        {
            ReleaseCamera();
            base.OnDestroy();
        }

        private void ReleaseCamera()
        {
            if (firstPersonCamera == null) return;
            firstPersonCamera.enabled = false;
            var listener = firstPersonCamera.GetComponent<AudioListener>();
            if (listener != null) listener.enabled = false;
            Destroy(firstPersonCamera.gameObject);
            firstPersonCamera = null;
            if (overviewCamera != null) overviewCamera.enabled = true;
            if (overviewAudio != null) overviewAudio.enabled = overviewAudioWasEnabled;
            Cursor.lockState = previousCursorLock;
            Cursor.visible = previousCursorVisible;
        }

        private void ResetPosition()
        {
            movement = Vector2.zero;
            float angle = spawnSlot * Mathf.PI * 2 / 8;
            Vector3 position = new Vector3(Mathf.Cos(angle) * 7, 1.05f, Mathf.Sin(angle) * 7);
            if (arenaHalfExtent > 10)
            {
                Vector3[] spawns = {
                    new Vector3(18, 1.05f, 0), new Vector3(17, 1.05f, 17),
                    new Vector3(0, 1.05f, 18), new Vector3(-17, 1.05f, 17),
                    new Vector3(-18, 1.05f, 0), new Vector3(-17, 1.05f, -17),
                    new Vector3(0, 1.05f, -18), new Vector3(17, 1.05f, -17)
                };
                position = spawns[spawnSlot % spawns.Length];
            }
            Quaternion rotation = Quaternion.LookRotation(new Vector3(-position.x, 0, -position.z));
            controller.enabled = false;
            networkTransform.Teleport(position, rotation, Vector3.one);
            controller.enabled = true;
            verticalSpeed = 0;
        }
    }
}





