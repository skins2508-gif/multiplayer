using UnityEngine;
using Unity.Netcode;
using Blocks.Gameplay.Core;
using UnityEngine.InputSystem;

namespace Blocks.Gameplay.Shooter
{
    /// <summary>
    /// Handles shooter-specific player input using Unity's Input System and broadcasts actions via GameEvents.
    /// This component handles inputs for firing, aiming, reloading, and weapon switching.
    /// </summary>
    public class ShooterInputHandler : NetworkBehaviour
    {
        #region Fields

        [Header("Shooter Events")]
        [Tooltip("Raised when the aim button is pressed.")]
        [SerializeField] private Core.GameEvent onSecondaryActionPressed;
        [Tooltip("Raised when the aim button is released.")]
        [SerializeField] private Core.GameEvent onSecondaryActionReleased;
        [Tooltip("Raised when the reload button is pressed.")]
        [SerializeField] private Core.GameEvent onReloadPressed;
        [Tooltip("Raised when the next weapon button is pressed.")]
        [SerializeField] private Core.GameEvent onNextWeaponPressed;
        [Tooltip("Raised when the previous weapon button is pressed.")]
        [SerializeField] private Core.GameEvent onPreviousWeaponPressed;

        private GameplayInputSystem_Actions m_InputActions;
        private WeaponController m_Weapons;
        private CoreStatsHandler m_Stats;
        private CoreCameraController m_Camera;
        private Unity.Cinemachine.CinemachineCamera m_ZoomCamera;
        private float m_NormalFov;
        [SerializeField, Range(1,3)] private float soldierZoom = 1.5f;

        private void ResetSoldierZoom()
        {
            if(m_ZoomCamera!=null){var lens=m_ZoomCamera.Lens;lens.FieldOfView=m_NormalFov;m_ZoomCamera.Lens=lens;}
            m_ZoomCamera=null;
        }
        private void Update()
        {
            if(!IsSpawned || !IsOwner)return;
            bool zoom=false; // Soldier secondary fire now launches a missile.
            var camera=m_Camera!=null?m_Camera.ActiveCameraMode?.CinemachineCamera:null;
            if(m_ZoomCamera!=null && m_ZoomCamera!=camera)ResetSoldierZoom();
            if(!zoom){ResetSoldierZoom();return;}
            if(camera==null)return;
            if(m_ZoomCamera==null){m_ZoomCamera=camera;m_NormalFov=camera.Lens.FieldOfView;}
            var lens=camera.Lens;
            lens.FieldOfView=2*Mathf.Atan(Mathf.Tan(m_NormalFov*Mathf.Deg2Rad*.5f)/soldierZoom)*Mathf.Rad2Deg;
            camera.Lens=lens;
        }

        #endregion

        #region Unity Lifecycle & Network Callbacks

        private void Awake()
        {
            m_InputActions = new GameplayInputSystem_Actions();
            m_Weapons=GetComponent<WeaponController>();m_Stats=GetComponent<CoreStatsHandler>();m_Camera=GetComponent<CoreCameraController>();
        }

        public override void OnNetworkSpawn()
        {
            if (IsOwner)
            {
                RegisterInputActions();
                m_InputActions.Player.Enable();
            }
        }

        public override void OnNetworkDespawn()
        {
            ResetSoldierZoom();
            if (m_InputActions!=null)
            {
                m_InputActions.Player.Disable();
                UnregisterInputActions();
            }
        }

        public override void OnDestroy()
        {
            if(m_InputActions!=null)
            {
                m_InputActions.Disable();
                UnregisterInputActions();
                m_InputActions.Dispose();
                m_InputActions=null;
            }
            base.OnDestroy();
        }

        private void OnEnable()
        {
            if (IsOwner && m_InputActions != null)
            {
                m_InputActions.Player.Enable();
            }
        }

        private void OnDisable()
        {
            ResetSoldierZoom();
            if (IsOwner && m_InputActions != null)
            {
                m_InputActions.Player.Disable();
            }
        }

        #endregion

        #region Input Registration

        private void RegisterInputActions()
        {
            m_InputActions.Player.SecondaryAction.started += HandleAimPressed;
            m_InputActions.Player.SecondaryAction.canceled += HandleAimReleased;

            m_InputActions.Player.Reload.performed += HandleReload;

            m_InputActions.Player.Next.performed += HandleNextWeapon;
            m_InputActions.Player.Previous.performed += HandlePreviousWeapon;
        }

        private void UnregisterInputActions()
        {
            m_InputActions.Player.SecondaryAction.started -= HandleAimPressed;
            m_InputActions.Player.SecondaryAction.canceled -= HandleAimReleased;

            m_InputActions.Player.Reload.performed -= HandleReload;

            m_InputActions.Player.Next.performed -= HandleNextWeapon;
            m_InputActions.Player.Previous.performed -= HandlePreviousWeapon;
        }

        #endregion

        #region Input Handlers

        private void HandleAimPressed(InputAction.CallbackContext context)
        {
            // Cassidy uses secondary fire for fanning, never camera zoom.
            if (m_Weapons!=null && (m_Weapons.CharacterId==0 || m_Weapons.CharacterId==3)) return;
            onSecondaryActionPressed?.Raise();
        }
        private void HandleAimReleased(InputAction.CallbackContext context)
        {
            if(m_Weapons!=null && (m_Weapons.CharacterId==0 || m_Weapons.CharacterId==3))return;
            onSecondaryActionReleased?.Raise();
        }
        private void HandleReload(InputAction.CallbackContext context) => onReloadPressed?.Raise();
        private void HandleNextWeapon(InputAction.CallbackContext context) => onNextWeaponPressed?.Raise();
        private void HandlePreviousWeapon(InputAction.CallbackContext context) => onPreviousWeaponPressed?.Raise();

        #endregion
    }
}

