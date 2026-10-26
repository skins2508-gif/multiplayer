using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Blocks.Gameplay.Core;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Blocks.Gameplay.Shooter
{
    public partial class WeaponController
    {
        [Header("Revolver / 네 번째 캐릭터")]
        [SerializeField, Min(0.1f)] private float revolverSingleInterval = 0.30f;
        [SerializeField, Min(0.05f)] private float revolverFanInterval = 0.13f;
        [SerializeField, Min(0.5f)] private float revolverReloadTime = 1.8f;
        [SerializeField, Min(1)] private float revolverDamage = 35f;
        [Tooltip("총구를 위로 드는 기본 각도입니다. 양수일수록 위를 향합니다.")]
        [SerializeField, Range(-20f, 30f)] private float revolverAimPitch = 8f;
        private readonly NetworkVariable<int> m_RevolverAmmo = new NetworkVariable<int>(6, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);
        private readonly NetworkVariable<bool> m_RevolverReloading = new NetworkVariable<bool>(false, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);
        private GameObject m_RevolverVisual;
        private Material m_RevolverGunMaterial, m_RevolverAmmoMaterial;
        private float m_RevolverNextShot, m_RevolverRecoilAt = -100f, m_RevolverReloadAt;
        private bool m_RevolverFanning;
        private bool m_RevolverLastFan;
        private AudioSource m_RevolverShotAudio, m_RevolverActionAudio;
        private AudioClip m_RevolverShotClip, m_RevolverReloadClip, m_RevolverFanClip;
        private readonly AudioClip[] m_RevolverSingleClips = new AudioClip[6];
        private bool m_RevolverCombinedAudio;
        private AudioSource m_DeadeyeVoiceAudio;
        private AudioClip m_DeadeyeVoiceClip;

        private void EnsureRevolverAudio()
        {
            if(m_RevolverShotAudio!=null)return;
            m_RevolverShotClip=Resources.Load<AudioClip>("Audio/Cassidy/Shot");
            for(int i=0;i<5;i++)m_RevolverSingleClips[i]=Resources.Load<AudioClip>("Audio/Cassidy/Shot"+(i+1));
            m_RevolverSingleClips[5]=m_RevolverSingleClips[0];
            m_RevolverReloadClip=Resources.Load<AudioClip>("Audio/Cassidy/Reload");
            m_RevolverFanClip=Resources.Load<AudioClip>("Audio/Cassidy/FanAndReload");
            m_DeadeyeVoiceClip=Resources.Load<AudioClip>("Audio/Cassidy/DeadeyeVoice");
            var go=new GameObject("Cassidy weapon audio");go.transform.SetParent(transform,false);
            m_RevolverShotAudio=go.AddComponent<AudioSource>();
            m_RevolverActionAudio=go.AddComponent<AudioSource>();
            m_DeadeyeVoiceAudio=go.AddComponent<AudioSource>();
            foreach(var source in new[]{m_RevolverShotAudio,m_RevolverActionAudio,m_DeadeyeVoiceAudio})
            {
                source.playOnAwake=false;source.loop=false;
                source.spatialBlend=IsOwner?0:1;
                source.rolloffMode=AudioRolloffMode.Linear;
                source.minDistance=2;source.maxDistance=45;source.dopplerLevel=0;
                source.volume=.65f;source.priority=80;
            }
            m_DeadeyeVoiceAudio.volume=.75f;
            m_DeadeyeVoiceAudio.maxDistance=60f;
            m_DeadeyeVoiceAudio.priority=60;
        }
        private void StopRevolverAudio()
        {
            if(m_DeadeyeVoiceAudio!=null)m_DeadeyeVoiceAudio.Stop();
            if(m_RevolverShotAudio!=null)m_RevolverShotAudio.Stop();
            if(m_RevolverActionAudio!=null)m_RevolverActionAudio.Stop();
            m_RevolverCombinedAudio=false;
        }
        [Rpc(SendTo.Everyone)]
        private void RevolverFanAudioRpc(int shots,RpcParams rpc=default)
        {
            if(rpc.Receive.SenderClientId!=OwnerClientId || CharacterId!=3)return;
            EnsureRevolverAudio();
            m_RevolverActionAudio.Stop();
            // A full recorded burst is used once, not once per bullet. Partial
            // magazines use individual shots so they cannot sound like six bullets.
            m_RevolverCombinedAudio=shots==6 && m_RevolverFanClip!=null;
            if(!m_RevolverCombinedAudio)return;
            m_RevolverActionAudio.clip=m_RevolverFanClip;
            m_RevolverActionAudio.pitch=Mathf.Clamp(m_RevolverFanClip.length/(shots*revolverFanInterval+.25f+revolverReloadTime),.75f,1.25f);
            m_RevolverActionAudio.Play();
        }
        private Transform m_RevolverCylinder, m_RevolverHammer, m_RevolverTriggerPart;
        private Quaternion m_CylinderRest, m_HammerRest, m_TriggerRest;
        private Vector3 m_CylinderPosition, m_CylinderAxis;
        private float m_CylinderAngle;
        public int RevolverAmmo => m_RevolverAmmo.Value;
        public bool RevolverReloading => m_RevolverReloading.Value;
        private bool CanUseRevolver => IsOwner && IsSpawned && CharacterId == 3 &&
            !CassidyRolling && (coreStats == null || coreStats.IsAlive) && Cursor.lockState == CursorLockMode.Locked;

        [Header("Cassidy / 황야의 무법자")]
        [SerializeField, Min(1)] private float deadeyeCooldown = 15f;
        [SerializeField, Min(1)] private float deadeyeDuration = 3f;
        [SerializeField, Min(1)] private float deadeyeRange = 40f;
        [SerializeField, Min(1)] private float deadeyeDamagePerSecond = 150f;
        private readonly NetworkVariable<bool> m_DeadeyeActive = new NetworkVariable<bool>(false, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);
        private readonly Dictionary<MonoBehaviour, float> m_DeadeyeLocks = new Dictionary<MonoBehaviour, float>();
        private float m_DeadeyeStarted, m_DeadeyeReadyAt;
        private int m_DeadeyeInputFrame = -1;
        public bool DeadeyeActive => m_DeadeyeActive.Value;
        public float DeadeyeCooldownRemaining => Mathf.Max(0, m_DeadeyeReadyAt-Time.time);

        private float DeadeyeTargetHealth(MonoBehaviour target)
        {
            if(target is WeaponController player)return player.coreStats!=null?player.coreStats.GetCurrentValue(StatKeys.Health):0;
            if(target is ShooterTrainingBot bot && ShooterEscortMatch.Instance!=null && ShooterEscortMatch.Instance.TryGetTargetBot(bot,out var state))return state.health;
            return 0;
        }

        private bool DeadeyeTargetVisible(MonoBehaviour target, out Vector3 point)
        {
            point=target!=null?target.transform.position+Vector3.up:Vector3.zero;
            if(target==null || target==this || !target.gameObject.activeInHierarchy || Camera.main==null)return false;
            ulong id;
            if(target is WeaponController player)
            {
                if(!player.IsSpawned || player.coreStats==null || !player.coreStats.IsAlive)return false;
                id=player.OwnerClientId;
            }
            else if(target is ShooterTrainingBot bot)
            {
                if(DeadeyeTargetHealth(bot)<=0)return false;
                id=bot.ParticipantId;
            }
            else return false;
            if(ShooterEscortMatch.RejectDamage(OwnerClientId,id))return false;
            var camera=Camera.main;var viewport=camera.WorldToViewportPoint(point);
            var delta=point-camera.transform.position;
            if(viewport.z<=0 || viewport.x<.05f || viewport.x>.95f || viewport.y<.05f || viewport.y>.95f || delta.magnitude>deadeyeRange)return false;
            return NinjaShurikenFlight.FirstHit(camera.transform.position,delta.normalized,delta.magnitude+.2f,transform,out var hit) &&
                (target is WeaponController ? hit.collider.GetComponentInParent<WeaponController>()==target :
                    hit.collider.GetComponentInParent<ShooterTrainingBot>()==target);
        }

        private void CancelDeadeye()
        {
            if(IsOwner && IsSpawned)m_DeadeyeActive.Value=false;
            m_DeadeyeLocks.Clear();
        }

        private void UpdateDeadeye()
        {
            if(!CanUseRevolver || (ShooterEscortMatch.Instance!=null && !ShooterEscortMatch.Instance.CombatAllowed))
            {CancelDeadeye();return;}
            if(!DeadeyeActive)
            {
                if(Keyboard.current!=null && Keyboard.current.eKey.wasPressedThisFrame && DeadeyeCooldownRemaining<=0 &&
                    !RevolverReloading && !m_RevolverFanning && Time.time>=m_RevolverNextShot &&
                    !(ShooterEscortMatch.Instance?.IsSpawnProtected(OwnerClientId)??false))
                {
                    m_DeadeyeLocks.Clear();m_DeadeyeStarted=Time.time;m_DeadeyeReadyAt=Time.time+deadeyeCooldown;
                    m_DeadeyeActive.Value=true;
                    m_DeadeyeInputFrame=Time.frameCount;
                }
                return;
            }
            m_DeadeyeInputFrame=Time.frameCount;
            if(Mouse.current!=null && Mouse.current.rightButton.wasPressedThisFrame){CancelDeadeye();return;}
            foreach(var target in m_DeadeyeLocks.Keys.ToArray())
                if(target==null || !DeadeyeTargetVisible(target,out _))m_DeadeyeLocks.Remove(target);
            foreach(var target in ActivePlayers)
                if(target!=null && DeadeyeTargetVisible(target,out _))
                    m_DeadeyeLocks[target]=(m_DeadeyeLocks.TryGetValue(target,out var charge)?charge:0)+Time.deltaTime;
            if(ShooterEscortMatch.Instance!=null)
                foreach(var bot in ShooterEscortMatch.Instance.TargetBots)
                    if(DeadeyeTargetVisible(bot,out _))
                        m_DeadeyeLocks[bot]=(m_DeadeyeLocks.TryGetValue(bot,out var charge)?charge:0)+Time.deltaTime;
            if((Mouse.current!=null && Mouse.current.leftButton.wasPressedThisFrame) || Time.time-m_DeadeyeStarted>=deadeyeDuration)
            {
                foreach(var entry in m_DeadeyeLocks.OrderByDescending(e=>e.Value).Take(6))
                {
                    if(entry.Key==null || !DeadeyeTargetVisible(entry.Key,out var point))continue;
                    var origin=Camera.main.transform.position;
                    // Immediate clicks still deliver a normal shot; waiting retains charge damage.
                    float damage=Mathf.Max(revolverDamage, Mathf.Min(entry.Value,deadeyeDuration)*deadeyeDamagePerSecond);
                    if(damage>0)DeadeyeShotRpc(origin,(point-origin).normalized,damage);
                }
                m_RevolverNextShot=Time.time+.5f;
                CancelDeadeye();
            }
        }

        [Rpc(SendTo.Everyone)]
        private void DeadeyeShotRpc(Vector3 origin, Vector3 direction, float damage, RpcParams rpc=default)
        {
            if(rpc.Receive.SenderClientId!=OwnerClientId || CharacterId!=3 || direction.sqrMagnitude<.01f ||
                float.IsNaN(damage) || float.IsInfinity(damage))return;
            m_RevolverRecoilAt=Time.time;m_RevolverLastFan=false;
            HeroAttackVisuals.Flash(origin,direction,new Color(1,.55f,.16f));
            var source=Resources.Load<GameObject>("Revolver/Bullet");if(source==null)return;
            var bullet=Instantiate(source,origin,Quaternion.LookRotation(direction));ApplyRevolverMaterial(bullet,true);
            HeroAttackVisuals.Trail(bullet,new Color(1,.65f,.25f));
            bullet.AddComponent<NinjaShurikenFlight>().Initialize(this,direction.normalized,180f,
                Mathf.Clamp(damage,0,deadeyeDuration*deadeyeDamagePerSecond),IsOwner,deadeyeRange,false);
        }

        private void OnGUI()
        {
            if(!IsOwner || !IsSpawned || !DeadeyeActive || Camera.main==null)return;
            GUI.Box(new Rect(Screen.width*.5f-190,Screen.height*.7f,380,32),
                $"황야의 무법자 {Mathf.Max(0,deadeyeDuration-(Time.time-m_DeadeyeStarted)):0.0}초 · 좌클릭 발사 / 우클릭 취소");
            foreach(var entry in m_DeadeyeLocks)
            {
                if(entry.Key==null || !DeadeyeTargetVisible(entry.Key,out var point))continue;
                var screen=Camera.main.WorldToScreenPoint(point);
                float damage=Mathf.Max(revolverDamage,Mathf.Min(entry.Value,deadeyeDuration)*deadeyeDamagePerSecond);
                float health=DeadeyeTargetHealth(entry.Key);
                DrawDeadeyeReticle(new Vector2(screen.x,Screen.height-screen.y),Mathf.Clamp01(damage/Mathf.Max(1,health)),damage>=health);
            }
        }

        private void InitializeRevolver()
        {
            if (IsOwner) { m_RevolverAmmo.Value = 6; m_RevolverReloading.Value = false; }
            m_CharacterId.OnValueChanged += OnRevolverCharacter;
            m_RevolverReloading.OnValueChanged += OnRevolverReloadState;
            m_DeadeyeActive.OnValueChanged += OnDeadeyeVoiceState;
            RefreshRevolver();
        }
        private void OnDeadeyeVoiceState(bool previous, bool active)
        {
            if(!active)
            {
                if(m_DeadeyeVoiceAudio!=null)m_DeadeyeVoiceAudio.Stop();
                return;
            }
            if(previous || CharacterId!=3)return;
            // The replicated activation edge plays once on each client. The
            // left click fires/ends the ability and never repeats the line.
            EnsureRevolverAudio();
            if(m_DeadeyeVoiceClip==null)return;
            m_DeadeyeVoiceAudio.clip=m_DeadeyeVoiceClip;
            m_DeadeyeVoiceAudio.pitch=1f;
            m_DeadeyeVoiceAudio.Play();
        }
        private void OnRevolverCharacter(int oldValue, int value)
        {
            if(value!=3)StopRevolverAudio();
            RefreshRevolver();
            if(m_RevolverVisual!=null)m_RevolverVisual.SetActive(value==3);
        }
        private void OnRevolverReloadState(bool previous, bool value)
        {
            if(value)
            {
                m_RevolverReloadAt=Time.time;
                if(CharacterId!=3)return;
                EnsureRevolverAudio();
                if(!m_RevolverCombinedAudio && m_RevolverReloadClip!=null)
                {
                    m_RevolverActionAudio.clip=m_RevolverReloadClip;
                    m_RevolverActionAudio.pitch=1;
                    m_RevolverActionAudio.Play();
                }
            }
            else
            {
                if(m_RevolverActionAudio!=null)m_RevolverActionAudio.Stop();
                m_RevolverCombinedAudio=false;
            }
        }
        private void RefreshRevolver()
        {
            if (CharacterId != 3 || m_RevolverVisual != null) return;
            var source = Resources.Load<GameObject>("Revolver/Gun");
            if (source == null) { Debug.LogError("Revolver/Gun resource missing", this); return; }
            Transform mount = transform;
            if (!IsOwner && m_Animator != null && m_Animator.isHuman)
                mount = m_Animator.GetBoneTransform(HumanBodyBones.RightHand) ?? transform;
            m_RevolverVisual = new GameObject("Cassidy_Revolver");
            m_RevolverVisual.transform.SetParent(mount, false);
            // Keep the first-person size; enlarge the revolver seen by other players.
            m_RevolverVisual.transform.localScale=Vector3.one*(IsOwner?1f:1.65f);
            var mesh = Instantiate(source, m_RevolverVisual.transform, false);
            ApplyRevolverMaterial(mesh, false);
            foreach (var part in mesh.GetComponentsInChildren<Transform>())
            {
                if (part.name == "CylinderPart")
                {
                    m_RevolverCylinder = part;
                    m_CylinderRest = part.localRotation;
                    m_CylinderPosition = part.localPosition;
                    // Resolved from the barrel mesh below, after all parts are located.
                    m_CylinderAxis = Vector3.zero;
                    m_CylinderAngle = (6 - RevolverAmmo) * 60f;
                }
                if (part.name == "HammerPart") { m_RevolverHammer=part; m_HammerRest=part.localRotation; }
                if (part.name == "TriggerPart") { m_RevolverTriggerPart=part; m_TriggerRest=part.localRotation; }
            }
            if (m_RevolverCylinder != null)
            {
                var axisStart = m_RevolverCylinder.Find("CylinderAxisStart");
                var axisEnd = m_RevolverCylinder.Find("CylinderAxisEnd");
                if (axisStart != null && axisEnd != null)
                    m_CylinderAxis = m_RevolverCylinder.InverseTransformDirection(axisEnd.position - axisStart.position).normalized;
                else Debug.LogError("Revolver cylinder axis markers missing; rotation disabled.", this);
            }
            m_RevolverVisual.transform.localRotation = Quaternion.identity;
        }
        private void ApplyRevolverMaterial(GameObject model, bool ammo)
        {
            Material material = ammo ? m_RevolverAmmoMaterial : m_RevolverGunMaterial;
            if (material == null)
            {
                var shader = Shader.Find("Universal Render Pipeline/Lit");
                if (shader == null) return;
                material = new Material(shader);
                material.SetTexture("_BaseMap", Resources.Load<Texture2D>(ammo ? "Revolver/AmmoColor" : "Revolver/GunColor"));
                material.SetFloat("_Metallic", 0.65f); material.SetFloat("_Smoothness", 0.45f);
                if (ammo) m_RevolverAmmoMaterial = material; else m_RevolverGunMaterial = material;
            }
            foreach (var collider in model.GetComponentsInChildren<Collider>()) collider.enabled = false;
            foreach (var renderer in model.GetComponentsInChildren<Renderer>())
            {
                var materials = new Material[renderer.sharedMaterials.Length];
                for (int i=0;i<materials.Length;i++) materials[i]=material;
                renderer.sharedMaterials=materials;
            }
        }
        private void UpdateRevolver()
        {
            if(CharacterId!=3 || !IsSpawned || (coreStats!=null && !coreStats.IsAlive))StopRevolverAudio();
            if (!CanUseRevolver) return;
            if (DeadeyeActive || m_DeadeyeInputFrame==Time.frameCount || (Keyboard.current!=null && Keyboard.current.eKey.wasPressedThisFrame) ||
                Time.time<m_RevolverNextShot && RevolverAmmo==0) return;
            if (RevolverAmmo == 0 && !m_RevolverFanning && !RevolverReloading && Time.time >= m_RevolverNextShot)
            {
                ReloadRevolver();
                return;
            }
            if (Mouse.current == null) return;
            if (Mouse.current.rightButton.wasPressedThisFrame && !m_RevolverFanning && !RevolverReloading &&
                RevolverAmmo > 0 && Time.time >= m_RevolverNextShot) StartCoroutine(FanRevolver());
        }
        private void FireRevolverSingle()
        {
            if (!CanUseRevolver || DeadeyeActive || m_RevolverFanning || RevolverReloading || Time.time < m_RevolverNextShot) return;
            FireRevolver(false);
        }
        private void FireRevolver(bool fan)
        {
            if (!CanUseRevolver || RevolverAmmo <= 0 || RevolverReloading) return;
            m_RevolverAmmo.Value--;
            m_RevolverNextShot = Time.time + (fan ? revolverFanInterval : revolverSingleInterval);
            var view = Camera.main != null ? Camera.main.transform : transform;
            Vector2 spread = Random.insideUnitCircle * (fan ? 0.07f : 0.002f);
            Vector3 direction = (view.forward + view.right * spread.x + view.up * spread.y).normalized;
            Vector3 target = view.position + direction * 80f;
            if (NinjaShurikenFlight.FirstHit(view.position, direction, 80, transform, out var aimHit)) target = aimHit.point;
            Vector3 origin = view.TransformPoint(new Vector3(0.17f, -0.12f, 0.55f));
            Vector3 offset = origin-view.position;
            if (NinjaShurikenFlight.FirstHit(view.position, offset.normalized, offset.magnitude, transform, out _)) origin=view.position;
            RevolverShotRpc(origin, (target-origin).normalized, fan, 5-RevolverAmmo);
        }
        private IEnumerator FanRevolver()
        {
            m_RevolverFanning=true;
            int shots=RevolverAmmo;
            RevolverFanAudioRpc(shots);
            for(int i=0;i<shots;i++)
            {
                if(!CanUseRevolver || RevolverReloading || RevolverAmmo<=0) break;
                FireRevolver(true);
                yield return new WaitForSeconds(revolverFanInterval);
            }
            m_RevolverFanning=false;
            m_RevolverNextShot=Time.time+0.25f;
        }
        private void ReloadRevolver()
        {
            if (!CanUseRevolver || DeadeyeActive || RevolverReloading || m_RevolverFanning || RevolverAmmo==6) return;
            StartCoroutine(ReloadRevolverRoutine());
        }
        private IEnumerator ReloadRevolverRoutine()
        {
            m_RevolverReloading.Value=true;
            yield return new WaitForSeconds(0.45f);
            if (!CanUseRevolver) { m_RevolverReloading.Value=false; yield break; }
            var view=Camera.main!=null?Camera.main.transform:transform;
            RevolverCasesRpc(view.TransformPoint(new Vector3(.18f,-.20f,.45f)), view.right, 6-RevolverAmmo);
            yield return new WaitForSeconds(Mathf.Max(.05f,revolverReloadTime-.45f));
            if(CanUseRevolver) m_RevolverAmmo.Value=6;
            m_RevolverReloading.Value=false;
        }
        [Rpc(SendTo.Everyone)]
        private void RevolverShotRpc(Vector3 origin, Vector3 direction, bool fan, int shotIndex, RpcParams rpcParams=default)
        {
            if(rpcParams.Receive.SenderClientId!=OwnerClientId || CharacterId!=3) return;
            EnsureRevolverAudio();
            var clip=fan?m_RevolverShotClip:m_RevolverSingleClips[Mathf.Clamp(shotIndex,0,5)];
            if(clip==null)clip=m_RevolverShotClip;
            if((!fan || !m_RevolverCombinedAudio) && clip!=null)m_RevolverShotAudio.PlayOneShot(clip);
            m_RevolverRecoilAt=Time.time;
            m_RevolverLastFan=fan;
            HeroAttackVisuals.Flash(origin,direction,new Color(1,.55f,.16f));
            var source=Resources.Load<GameObject>("Revolver/Bullet"); if(source==null)return;
            var bullet=Instantiate(source,origin,Quaternion.LookRotation(direction)*Quaternion.identity);
            ApplyRevolverMaterial(bullet,true);
            HeroAttackVisuals.Trail(bullet,new Color(1,.65f,.25f));
            bullet.AddComponent<NinjaShurikenFlight>().Initialize(this,direction,90f,fan?revolverDamage*.7f:revolverDamage,IsOwner,80,false);
        }
        [Rpc(SendTo.Everyone)]
        private void RevolverCasesRpc(Vector3 origin, Vector3 right, int count, RpcParams rpcParams=default)
        {
            if(rpcParams.Receive.SenderClientId!=OwnerClientId || CharacterId!=3)return;
            var source=Resources.Load<GameObject>("Revolver/Shell");if(source==null)return;
            for(int i=0;i<Mathf.Clamp(count,0,6);i++)
            {
                var shell=Instantiate(source,origin+Random.insideUnitSphere*.025f,Random.rotation);
                ApplyRevolverMaterial(shell,true);
                var physics=shell.AddComponent<Rigidbody>();physics.mass=.01f;
                physics.linearVelocity=right*Random.Range(.6f,1.2f)+Vector3.up*.4f;
                physics.angularVelocity=Random.onUnitSphere*12f;
                Destroy(shell,2f);
            }
        }
        private void LateUpdateRevolver()
        {
            if(CharacterId!=3)return;
            if(m_RevolverVisual==null)RefreshRevolver();
            if(m_RevolverVisual==null)return;
            m_RevolverVisual.SetActive(!CassidyRolling && (coreStats==null || coreStats.IsAlive));
            if(CassidyRolling)return;
            float elapsed=Time.time-m_RevolverRecoilAt;
            float recoilDuration=m_RevolverLastFan?revolverFanInterval:0.22f;
            float recoil=elapsed<recoilDuration?Mathf.Sin(Mathf.Clamp01(elapsed/recoilDuration)*Mathf.PI):0;
            float reload=RevolverReloading?Mathf.Sin(Mathf.Clamp01((Time.time-m_RevolverReloadAt)/revolverReloadTime)*Mathf.PI):0;
            if(m_RevolverCylinder!=null)
            {
                m_CylinderAngle = Mathf.MoveTowardsAngle(m_CylinderAngle, (6 - RevolverAmmo) * 60f, 750f * Time.deltaTime);
                Quaternion turn = m_CylinderAxis.sqrMagnitude > 0.5f ? m_CylinderRest * Quaternion.AngleAxis(m_CylinderAngle, m_CylinderAxis) : m_CylinderRest;
                m_RevolverCylinder.localRotation = turn;
                m_RevolverCylinder.localPosition = m_CylinderPosition;
            }
            if(m_RevolverHammer!=null)m_RevolverHammer.localRotation=m_HammerRest*Quaternion.Euler(-28*recoil,0,0);
            if(m_RevolverTriggerPart!=null)m_RevolverTriggerPart.localRotation=m_TriggerRest*Quaternion.Euler(12*recoil,0,0);
            Quaternion pose=Quaternion.Euler(-revolverAimPitch-(m_RevolverLastFan?11:18)*recoil,0,-65*reload+(m_RevolverLastFan?8*recoil:0));
            if(DeadeyeActive)pose=Quaternion.Euler(-revolverAimPitch-5,0,-8);
            if(IsOwner && Camera.main!=null)
            {
                var view=Camera.main.transform;
                m_RevolverVisual.transform.SetPositionAndRotation(view.TransformPoint(new Vector3(.19f,-.17f-.12f*reload,.45f-.04f*recoil)),view.rotation*pose);
            }
            else if(m_CowboyAnimator!=null && m_CowboyAnimator.isHuman)
            {
                var hand=m_CowboyAnimator.GetBoneTransform(HumanBodyBones.RightHand);
                Quaternion gunRotation=transform.rotation*pose;
                AlignCowboyWrist(m_CowboyAnimator,gunRotation*Vector3.forward);
                var middle=m_CowboyAnimator.GetBoneTransform(HumanBodyBones.RightMiddleProximal);
                Vector3 palm=middle!=null?Vector3.Lerp(hand.position,middle.position,.65f):hand.position;
                // Measured on Gun.fbx in Blender. Anchor the enlarged handle,
                // not the model origin, to the palm during recoil and reload.
                Vector3 gripLocal=new Vector3(0,-.03f,-.115f);
                m_RevolverVisual.transform.rotation=gunRotation;
                m_RevolverVisual.transform.position=palm-m_RevolverVisual.transform.TransformVector(gripLocal);
            }
            else m_RevolverVisual.transform.localRotation=pose;
        }
        private void CleanupRevolver()
        {
            m_DeadeyeActive.OnValueChanged-=OnDeadeyeVoiceState;
            StopRevolverAudio();
            if(m_RevolverShotAudio!=null)Destroy(m_RevolverShotAudio.gameObject);
            m_DeadeyeLocks.Clear();
            m_CharacterId.OnValueChanged-=OnRevolverCharacter;
            m_RevolverReloading.OnValueChanged-=OnRevolverReloadState;
            m_RevolverFanning=false;
            if(m_RevolverVisual!=null)Destroy(m_RevolverVisual);
            if(m_RevolverGunMaterial!=null)Destroy(m_RevolverGunMaterial);
            if(m_RevolverAmmoMaterial!=null)Destroy(m_RevolverAmmoMaterial);
        }
    }
}
