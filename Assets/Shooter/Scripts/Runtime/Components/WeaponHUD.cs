using UnityEngine;
using System.Collections;
using Blocks.Gameplay.Core;
using UnityEngine.UIElements;
using Image = UnityEngine.UI.Image;

namespace Blocks.Gameplay.Shooter
{
    /// <summary>
    /// Manages the weapon-related HUD elements including ammo display, weapon icons, and dynamic reticle.
    /// Integrates both UI Toolkit (UIElements) and UGUI systems for comprehensive weapon feedback.
    /// </summary>
    public class WeaponHUD : CoreHUD
    {
        #region Fields & Properties

        [Header("Listening to")]
        [Tooltip("Event triggered when clip ammo count changes.")]
        [SerializeField] private IntPairEvent onClipAmmoChanged;
        [Tooltip("Event triggered when weapon is swapped.")]
        [SerializeField] private WeaponSwapEvent onWeaponChanged;
        [Tooltip("Event triggered when aiming state changes.")]
        [SerializeField] private BoolEvent onAimingStateChanged;
        [Tooltip("Event triggered when weapon is fired.")]
        [SerializeField] private FloatEvent onWeaponFiredEvent;
        [Tooltip("Event triggered when reload starts.")]
        [SerializeField] private FloatEvent onReloadStartedEvent;
        [Tooltip("Event triggered when weapon spread changes.")]
        [SerializeField] private FloatEvent onWeaponSpreadChanged;

        [Header("Component Dependencies")]
        [Tooltip("Reference to the aim controller for reticle management.")]
        [SerializeField] private AimController aimController;

        [Header("UGUI")]
        [Tooltip("UGUI image component for displaying reload progress indicator.")]
        [SerializeField] private Image weaponReload;

        private Coroutine m_ReloadCoroutine;
        private Coroutine m_MessageCoroutine;
        private Coroutine m_ReticleCooldownCoroutine;

        private Label m_AmmoLabel;
        private Label m_WeaponTypeLabel;
        private UnityEngine.UIElements.Image m_WeaponIcon;
        private ProgressBar m_PlayerAmmoBar;
        private VisualElement m_PlayerReticle;
        private WeaponData m_CurrentWeaponData;
        private WeaponController m_NinjaController;
        private ProgressBar m_DvaStaminaDisplay;
        private CoreStatsHandler m_DvaDisplayedStats;

        #endregion

        #region Unity Methods

        protected override void Initialize()
        {
            base.Initialize();
            m_NinjaController = GetComponent<WeaponController>();
            m_DvaDisplayedStats = GetComponent<CoreStatsHandler>();
            if (aimController == null)
            {
                Debug.LogError("AimController is not assigned on WeaponHUD!", this);
            }

            UpdateWeaponReticleAndIcon(onWeaponChanged.LastValue);
            UpdateReticleVisibility(onAimingStateChanged.LastValue);
        }

        private void LateUpdate()
        {
            if (!IsOwner || aimController == null) return;
            UpdateNinjaHUD();

            // Reset reticle position to ensure it stays centered
            // This prevents drift that can occur from UI Toolkit layout calculations
            if (m_PlayerReticle != null)
            {
                if (m_PlayerReticle.style.position != StyleKeyword.Initial)
                {
                    m_PlayerReticle.style.position = StyleKeyword.Initial;
                    m_PlayerReticle.style.left = StyleKeyword.Initial;
                    m_PlayerReticle.style.top = StyleKeyword.Initial;
                }
            }

            // Keep the UGUI reload indicator centered on the reticle
            if (weaponReload != null)
            {
                weaponReload.rectTransform.anchoredPosition = Vector2.zero;
            }
        }

        #endregion

        #region Protected Methods

        protected override void RegisterAdditionalListeners()
        {
            onWeaponFiredEvent.RegisterListener(HandleWeaponFired);
            onClipAmmoChanged.RegisterListener(UpdateClipAmmoDisplay);
            onReloadStartedEvent.RegisterListener(HandleReloadStarted);
            onWeaponSpreadChanged.RegisterListener(UpdateReticleSpread);
            onWeaponChanged.RegisterListener(UpdateWeaponReticleAndIcon);
            onAimingStateChanged.RegisterListener(UpdateReticleVisibility);
        }

        protected override void UnregisterAdditionalListeners()
        {
            onWeaponFiredEvent.UnregisterListener(HandleWeaponFired);
            onClipAmmoChanged.UnregisterListener(UpdateClipAmmoDisplay);
            onReloadStartedEvent.UnregisterListener(HandleReloadStarted);
            onWeaponSpreadChanged.UnregisterListener(UpdateReticleSpread);
            onWeaponChanged.UnregisterListener(UpdateWeaponReticleAndIcon);
            onAimingStateChanged.UnregisterListener(UpdateReticleVisibility);
        }

        protected override void QueryHUDElements(VisualElement root)
        {
            base.QueryHUDElements(root);
            m_WeaponTypeLabel = root.Q<Label>("weapon-type-label");
            m_AmmoLabel = root.Q<Label>("ammo-label");
            m_PlayerAmmoBar = root.Q<ProgressBar>("player-ammo-bar");
            m_DvaStaminaDisplay = root.Q<ProgressBar>("player-stamina-bar");
            m_PlayerReticle = root.Q<VisualElement>("player-reticle");
            m_WeaponIcon = root.Q<UnityEngine.UIElements.Image>("weapon-icon");
        }

        protected override void SetHUDDefaults()
        {
            base.SetHUDDefaults();
            if (weaponReload != null)
            {
                float baseSize = m_CurrentWeaponData?.reticleBaseSize ?? 80f;
                weaponReload.rectTransform.sizeDelta = new Vector2(baseSize, baseSize);
                weaponReload.gameObject.SetActive(false);
            }
            if (m_PlayerReticle != null)
            {
                m_PlayerReticle.style.position = Position.Absolute;
            }
        }

        #endregion

        #region Private Methods

        /// <summary>
        /// Updates the ammo display with current and maximum clip values.
        /// Color codes the display: red when empty, yellow when low, white when adequate.
        /// </summary>
        /// <param name="clip">Contains current ammo (value1) and max ammo (value2).</param>
        private void UpdateNinjaHUD()
        {
            if (m_NinjaController == null) return;
            if (m_NinjaController.CharacterId == 2 && m_DvaStaminaDisplay != null && m_DvaDisplayedStats != null)
            {
                m_DvaStaminaDisplay.highValue = m_DvaDisplayedStats.GetMaxValue(StatKeys.Stamina);
                m_DvaStaminaDisplay.value = m_DvaDisplayedStats.GetCurrentValue(StatKeys.Stamina);
            }
            if (m_PlayerReticle != null)
            {
                // Reset the Soldier-only outline when switching heroes.
                float edge = 0;
                m_PlayerReticle.style.borderTopWidth = m_PlayerReticle.style.borderBottomWidth = edge;
                m_PlayerReticle.style.borderLeftWidth = m_PlayerReticle.style.borderRightWidth = edge;
            }
            if (m_NinjaController.CharacterId == 0)
            {
                if (m_PlayerReticle != null)
                {
                    m_PlayerReticle.style.display = DisplayStyle.Flex;
                    m_PlayerReticle.style.backgroundImage = StyleKeyword.None;
                    m_PlayerReticle.style.backgroundColor = Color.red;
                    m_PlayerReticle.style.width = 4;
                    m_PlayerReticle.style.height = 4;
                    m_PlayerReticle.style.borderTopLeftRadius = m_PlayerReticle.style.borderTopRightRadius = 2;
                    m_PlayerReticle.style.borderBottomLeftRadius = m_PlayerReticle.style.borderBottomRightRadius = 2;
                    m_PlayerReticle.style.borderTopColor = m_PlayerReticle.style.borderBottomColor = Color.red;
                    m_PlayerReticle.style.borderLeftColor = m_PlayerReticle.style.borderRightColor = Color.red;
                }
                if (m_WeaponTypeLabel != null)
                {
                    float remaining = m_NinjaController.SoldierRapidFireRemaining;
                    float cooldown = m_NinjaController.SoldierRapidCooldownRemaining;
                    m_WeaponTypeLabel.text = remaining > 0 ? $"솔저 · 무제한 탄약 · 연사·이동 x2 {remaining:0.0}초" :
                        cooldown > 0 ? $"솔저 · 재사용 {cooldown:0.0}초" : "솔저 · 강화 준비";
                }
                if (m_AmmoLabel != null && m_NinjaController.CurrentWeapon is ModularWeapon rifle)
                {
                    rifle.GetAmmoInfo(out int ammo, out int capacity);
                    m_AmmoLabel.text = m_NinjaController.SoldierInfiniteAmmo ? "∞ / 무제한" : $"{ammo} / {capacity}";
                    float missile = m_NinjaController.SoldierMissileCooldownRemaining;
                    m_AmmoLabel.text += missile > 0 ? $" · 미사일 {missile:0.0}초" : " · 우클릭 미사일 준비";
                }
                return;
            }
            if (m_NinjaController.CharacterId == 3)
            {
                if (m_WeaponTypeLabel != null) m_WeaponTypeLabel.text = m_NinjaController.DeadeyeActive ? "황야의 무법자 · 조준 중" :
                    m_NinjaController.DeadeyeCooldownRemaining>0 ? $"캐서디 · 재사용 {m_NinjaController.DeadeyeCooldownRemaining:0.0}초" : "캐서디 · 궁극기 준비";
                if (m_AmmoLabel != null) m_AmmoLabel.text = m_NinjaController.RevolverReloading ? "재장전 중" :
                    $"{m_NinjaController.RevolverAmmo} / 6";
                if (m_PlayerAmmoBar != null)
                {
                    m_PlayerAmmoBar.style.display = DisplayStyle.Flex;
                    m_PlayerAmmoBar.highValue = 6; m_PlayerAmmoBar.value = m_NinjaController.RevolverAmmo;
                }
                if (m_WeaponIcon != null) m_WeaponIcon.style.display = DisplayStyle.None;
                if (m_PlayerReticle != null)
                {
                    m_PlayerReticle.style.display = DisplayStyle.Flex;
                    m_PlayerReticle.style.backgroundImage = StyleKeyword.None;
                    m_PlayerReticle.style.backgroundColor = Color.white;
                    m_PlayerReticle.style.width = 5; m_PlayerReticle.style.height = 5;
                }
                return;
            }
            bool katana = m_NinjaController.IsKatanaEquipped;
            if (m_WeaponTypeLabel != null) m_WeaponTypeLabel.text = m_NinjaController.CharacterId == 2 ? (m_NinjaController.DvaPilot ? "디바 · 광선총" : "디바 · 양손 기관포") : katana ? "카타나" : "수리검";
            if (m_WeaponTypeLabel != null && m_NinjaController.CharacterId == 1)
                m_WeaponTypeLabel.text = katana ? $"겐지 · 각성 {m_NinjaController.NinjaAwakeningRemaining:0.0}초" :
                    m_NinjaController.NinjaAwakeningCooldownRemaining > 0 ? $"E 각성 · 재사용 {m_NinjaController.NinjaAwakeningCooldownRemaining:0.0}초" : "겐지 · E 각성 준비";
            if (m_AmmoLabel != null)
            {
                m_AmmoLabel.text = katana ?
                    (m_NinjaController.KatanaDashCooldownRemaining > 0 ? $"Shift 대시 {m_NinjaController.KatanaDashCooldownRemaining:0.0}초" : "Shift 대시 준비") : "수리검 · 3연발";
                if (m_NinjaController.CharacterId == 1 && !katana)
                    m_AmmoLabel.text = $"{m_NinjaController.ShurikenAmmo} / 무제한" +
                        (m_NinjaController.ShurikenReloading ? " · 장전 중" : "");
                if (m_NinjaController.CharacterId == 2) m_AmmoLabel.text =
                    m_NinjaController.DvaPilot ? $"{m_NinjaController.DvaPilotAmmo}/7{(m_NinjaController.DvaPilotReloading ? " · 재장전" : "")}" :
                    $"방어 {m_NinjaController.DvaMatrixEnergy * 100f:0}%";
                m_AmmoLabel.style.color = Color.white;
            }
            if (m_PlayerAmmoBar != null)
            {
                bool dva = m_NinjaController.CharacterId == 2;
                bool shuriken = m_NinjaController.CharacterId == 1 && !katana;
                m_PlayerAmmoBar.style.display = dva || shuriken ? DisplayStyle.Flex : DisplayStyle.None;
                if (shuriken) { m_PlayerAmmoBar.highValue = WeaponController.ShurikenMagazineSize; m_PlayerAmmoBar.value = m_NinjaController.ShurikenAmmo; }
                if (dva) { m_PlayerAmmoBar.highValue = 100; m_PlayerAmmoBar.value = m_NinjaController.DvaMatrixEnergy * 100f; }
            }
            if (m_WeaponIcon != null) m_WeaponIcon.style.display = DisplayStyle.None;
            if (m_PlayerReticle != null)
            {
                m_PlayerReticle.style.display = DisplayStyle.Flex;
                m_PlayerReticle.style.backgroundImage = StyleKeyword.None;
                m_PlayerReticle.style.backgroundColor = Color.white;
                m_PlayerReticle.style.width = 5;
                m_PlayerReticle.style.height = 5;
                m_PlayerReticle.style.borderTopLeftRadius = m_PlayerReticle.style.borderTopRightRadius = 3;
                m_PlayerReticle.style.borderBottomLeftRadius = m_PlayerReticle.style.borderBottomRightRadius = 3;
            }
        }

        private void UpdateClipAmmoDisplay(IntPair clip)
        {
            if (m_AmmoLabel != null)
            {
                m_AmmoLabel.text = $"{clip.value1.ToString()} / {clip.value2.ToString()}";
                if (clip.value1 == 0) m_AmmoLabel.style.color = Color.red;
                else if (clip.value1 <= clip.value2 * 0.3f) m_AmmoLabel.style.color = Color.yellow;
                else m_AmmoLabel.style.color = Color.white;
            }

            if (m_PlayerAmmoBar != null)
            {
                m_PlayerAmmoBar.value = clip.value1;
                m_PlayerAmmoBar.highValue = clip.value2;
            }
        }

        /// <summary>
        /// Updates the weapon reticle image, icon, and related UI when the weapon changes.
        /// Handles both weapon equip and unequip scenarios.
        /// </summary>
        /// <param name="payload">Contains the new weapon reference and swap information.</param>
        private void UpdateWeaponReticleAndIcon(WeaponSwapPayload payload)
        {
            if (m_PlayerReticle == null) return;

            if (payload.NewWeapon != null)
            {
                m_CurrentWeaponData = payload.NewWeapon.GetWeaponData();
                if (m_CurrentWeaponData != null)
                {
                    m_PlayerReticle.style.backgroundImage = new StyleBackground(m_CurrentWeaponData.reticleImage);
                    m_WeaponIcon.sprite = m_CurrentWeaponData.weaponIcon;

                    if (m_WeaponTypeLabel != null) m_WeaponTypeLabel.text = m_CurrentWeaponData.weaponName;
                }

                if (payload.NewWeapon is ModularWeapon simpleWeapon)
                {
                    simpleWeapon.GetAmmoInfo(out int current, out int max);
                    UpdateClipAmmoDisplay(new IntPair { value1 = current, value2 = max });
                    UpdateReticleSpread(simpleWeapon.GetCurrentSpreadAngle());
                }
            }
            else
            {
                m_CurrentWeaponData = null;
                if (m_WeaponTypeLabel != null) m_WeaponTypeLabel.text = "No Weapon";
                if (m_WeaponIcon != null) m_WeaponIcon.sprite = null;
                if (m_AmmoLabel != null) m_AmmoLabel.text = "";
                if (m_PlayerAmmoBar != null) m_PlayerAmmoBar.value = 0;
                m_PlayerReticle.style.backgroundImage = StyleKeyword.None;
                m_PlayerReticle.style.display = DisplayStyle.None;
                UpdateReticleSpread(0);
            }
        }

        /// <summary>
        /// Controls reticle visibility based on aiming state and weapon availability.
        /// The reticle only shows when actively aiming with a valid weapon equipped.
        /// </summary>
        /// <param name="isAiming">Whether the player is currently aiming.</param>
        private void UpdateReticleVisibility(bool isAiming)
        {
            if (m_PlayerReticle == null) return;
            bool shouldShow = isAiming && m_CurrentWeaponData != null;
            m_PlayerReticle.style.display = shouldShow ? DisplayStyle.Flex : DisplayStyle.None;
        }

        /// <summary>
        /// Handles weapon fire feedback by triggering a visual cooldown effect on the reticle.
        /// </summary>
        /// <param name="fireRate">The fire rate of the weapon, used for cooldown timing.</param>
        private void HandleWeaponFired(float fireRate)
        {
            if (m_ReticleCooldownCoroutine != null) StopCoroutine(m_ReticleCooldownCoroutine);
            m_ReticleCooldownCoroutine = StartCoroutine(ReticleCooldownCoroutine(fireRate));
        }

        /// <summary>
        /// Updates the reticle size based on weapon spread angle.
        /// Larger spread results in a larger reticle, providing visual feedback for weapon accuracy.
        /// </summary>
        /// <param name="spreadAngle">The current spread angle of the weapon in degrees.</param>
        private void UpdateReticleSpread(float spreadAngle)
        {
            if (m_PlayerReticle == null) return;

            if (m_NinjaController != null && m_NinjaController.CharacterId == 0) return;
            float baseSize = m_CurrentWeaponData?.reticleBaseSize ?? 80f;
            float spreadMultiplier = m_CurrentWeaponData?.reticleSpreadMultiplier ?? 100f;

            // Calculate final reticle size by adding spread contribution to base size
            float finalSize = baseSize + (spreadAngle * spreadMultiplier);

            m_PlayerReticle.style.width = finalSize;
            m_PlayerReticle.style.height = finalSize;
            if (weaponReload != null)
            {
                var weaponReloadSize = finalSize * 0.7f;
                weaponReload.rectTransform.sizeDelta = new Vector2(weaponReloadSize, weaponReloadSize);
            }
        }

        /// <summary>
        /// Initiates the reload animation coroutine when reload begins.
        /// </summary>
        /// <param name="reloadTime">Duration of the reload in seconds.</param>
        private void HandleReloadStarted(float reloadTime)
        {
            if (m_ReloadCoroutine != null) StopCoroutine(m_ReloadCoroutine);
            m_ReloadCoroutine = StartCoroutine(ReloadAnimationCoroutine(reloadTime));
        }

        /// <summary>
        /// Provides visual feedback for weapon fire rate by tinting the reticle grey temporarily.
        /// </summary>
        /// <param name="duration">How long to display the cooldown effect.</param>
        private IEnumerator ReticleCooldownCoroutine(float duration)
        {
            if (m_PlayerReticle == null) yield break;
            m_PlayerReticle.style.unityBackgroundImageTintColor = new StyleColor(Color.grey);
            yield return new WaitForSeconds(duration);
            m_PlayerReticle.style.unityBackgroundImageTintColor = new StyleColor(Color.white);
        }

        /// <summary>
        /// Animates the reload progress indicator as a radial fill over the reload duration.
        /// </summary>
        /// <param name="reloadTime">Total time for the reload animation in seconds.</param>
        private IEnumerator ReloadAnimationCoroutine(float reloadTime)
        {
            if (weaponReload == null) yield break;
            weaponReload.gameObject.SetActive(true);
            weaponReload.fillAmount = 0;

            float elapsedTime = 0f;
            while (elapsedTime < reloadTime)
            {
                elapsedTime += Time.deltaTime;
                weaponReload.fillAmount = elapsedTime / reloadTime;
                yield return null;
            }
            weaponReload.fillAmount = 1;
            weaponReload.gameObject.SetActive(false);
        }

        #endregion

    }
}
