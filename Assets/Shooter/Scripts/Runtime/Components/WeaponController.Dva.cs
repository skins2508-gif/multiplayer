using System.Collections.Generic;
using Blocks.Gameplay.Core;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.InputSystem;

namespace Blocks.Gameplay.Shooter
{
    public partial class WeaponController
    {
        [Header("Dva / 세 번째 캐릭터")]
        [SerializeField, Min(0.05f)] private float dvaShotInterval = 0.12f;
        [SerializeField, Min(1)] private float dvaDamagePerBarrel = 5f;
        [SerializeField, Min(1)] private float dvaRange = 20f;
        private GameObject m_DvaBody, m_DvaView;
        private Camera m_DvaMaskedCamera;
        private bool m_DvaCameraHadBodyLayer;
        private bool m_DvaTrigger;
        private AudioSource m_DvaAttackAudio;
        private float m_DvaAudioStopAt = -1;
        private AudioClip m_DvaFusionClip, m_DvaPistolClip;
        private bool m_DvaAudioPilot;
        private float m_DvaAudioShotAt = -100, m_DvaPreviousPistolShotAt = -100;

        private void PlayDvaAttackAudio(bool pilot)
        {
            if (m_DvaAttackAudio == null)
            {
                var go = new GameObject("Dva Attack Audio");
                go.transform.SetParent(transform, false);
                go.transform.localPosition = Vector3.up;
                m_DvaAttackAudio = go.AddComponent<AudioSource>();
                m_DvaAttackAudio.playOnAwake = false;
                m_DvaAttackAudio.rolloffMode = AudioRolloffMode.Linear;
                m_DvaAttackAudio.minDistance = 2;
                m_DvaAttackAudio.maxDistance = 35;
                m_DvaAttackAudio.dopplerLevel = 0;
                m_DvaAttackAudio.volume = .55f;
                m_DvaFusionClip = Resources.Load<AudioClip>("Audio/Dva/FusionCannons");
                m_DvaPistolClip = Resources.Load<AudioClip>("Audio/Dva/PistolShot");
            }
            m_DvaAudioStopAt = -1;
            m_DvaAudioShotAt = Time.time;
            m_DvaAttackAudio.volume = pilot ? .85f : .55f;
            m_DvaAttackAudio.spatialBlend = IsOwner ? 0 : 1;
            if (m_DvaAudioPilot != pilot) m_DvaAttackAudio.Stop();
            m_DvaAudioPilot = pilot;
            m_DvaAttackAudio.loop = !pilot;
            var clip = pilot ? m_DvaPistolClip : m_DvaFusionClip;
            if (m_DvaAttackAudio.clip != clip) m_DvaAttackAudio.clip = clip;
            if (pilot)
            {
                // Preserve slow shots; compress the single-shot tail for rapid clicks.
                float interval = Time.time - m_DvaPreviousPistolShotAt;
                m_DvaPreviousPistolShotAt = Time.time;
                m_DvaAttackAudio.pitch = Mathf.Clamp(.66f / Mathf.Max(.22f, interval), 1, 3);
                m_DvaAttackAudio.Stop();
                if (clip != null) m_DvaAttackAudio.Play();
            }
            else
            {
                m_DvaAttackAudio.pitch = 1;
                if (!m_DvaAttackAudio.isPlaying && clip != null) m_DvaAttackAudio.Play();
            }
        }

        private void StopDvaAttackAudio()
        {
            if (m_DvaAttackAudio != null) m_DvaAttackAudio.Stop();
        }

        [Rpc(SendTo.Everyone)]
        private void StopDvaAttackAudioRpc(RpcParams rpc = default)
        {
            if (rpc.Receive.SenderClientId != OwnerClientId) return;
            // A short click must still emit the shot transient before its tail is cut.
            if (m_DvaAudioPilot && Time.time < m_DvaAudioShotAt + .1f)
                m_DvaAudioStopAt = m_DvaAudioShotAt + .1f;
            else StopDvaAttackAudio();
        }

        private void UpdateDvaAttackAudio()
        {
            if (m_DvaAudioStopAt >= 0 && Time.time >= m_DvaAudioStopAt)
            { StopDvaAttackAudio(); m_DvaAudioStopAt = -1; }
            if (m_DvaAttackAudio == null || !m_DvaAttackAudio.isPlaying) return;
            bool invalid = !IsSpawned || CharacterId != 2 || coreStats == null || !coreStats.IsAlive ||
                DvaPilot != m_DvaAudioPilot || (!DvaPilot && (DvaMatrixActive || DvaSelfDestructing));
            bool expired = !m_DvaAudioPilot && Time.time - m_DvaAudioShotAt > Mathf.Max(.3f, dvaShotInterval * 2.5f);
            bool interrupted = IsOwner && (Cursor.lockState != CursorLockMode.Locked || (DvaPilot && DvaPilotReloading));
            if (invalid || expired || interrupted)
            {
                StopDvaAttackAudio();
                if (IsOwner && IsSpawned) StopDvaAttackAudioRpc();
            }
        }
        private float m_DvaNextShot, m_DvaLastShot = -100;
        private readonly Dictionary<Renderer, bool> m_DvaHiddenRenderers = new Dictionary<Renderer, bool>();
        private readonly List<Transform> m_DvaRotors = new List<Transform>();
        private readonly List<Material> m_DvaMaterials = new List<Material>();
        private Material m_DvaBulletMaterial;
        [SerializeField] private Vector3 dvaArmsOffset = new Vector3(0, -1.24f, -0.12f);
        [SerializeField, Range(0.4f, 1f)] private float dvaArmsScale = 0.70f;
        private readonly Transform[] m_DvaMuzzles = new Transform[2];
        private readonly Transform[] m_DvaWorldMuzzles = new Transform[2];

        [Header("Dva Defense Matrix")]
        [SerializeField, Min(0.1f)] private float dvaMatrixDuration = 3f;
        [SerializeField, Min(0.1f)] private float dvaMatrixRecharge = 6f;
        private readonly NetworkVariable<bool> m_MatrixActive = new NetworkVariable<bool>(false, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);
        private readonly NetworkVariable<Vector3> m_MatrixOrigin = new NetworkVariable<Vector3>(default, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);
        private readonly NetworkVariable<Quaternion> m_MatrixRotation = new NetworkVariable<Quaternion>(Quaternion.identity, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);
        private static readonly HashSet<WeaponController> s_MatrixPlayers = new HashSet<WeaponController>();
        private float m_MatrixEnergy = 1f;
        private bool m_MatrixExhausted;
        private LineRenderer m_MatrixOutline;
        private Material m_MatrixMaterial;
        private GameObject m_MatrixSurface;
        private Material m_MatrixSurfaceMaterial;
        private static readonly Bounds MatrixBounds = new Bounds(new Vector3(0, 0, 2.2f), new Vector3(3f, 2.4f, 0.1f));
        public float DvaMatrixEnergy => m_MatrixEnergy;
        public bool DvaMatrixActive => m_MatrixActive.Value;

        private void UpdateDvaMatrix()
        {
            bool held = Mouse.current != null && Mouse.current.rightButton.isPressed;
            bool usable = CharacterId == 2 && !DvaPilot && !DvaSelfDestructing && (coreStats == null || coreStats.IsAlive) && Cursor.lockState == CursorLockMode.Locked;
            if (!held) m_MatrixExhausted = false;
            bool active = usable && held && !m_MatrixExhausted && m_MatrixEnergy > 0;
            if (active)
            {
                m_MatrixEnergy = Mathf.Max(0, m_MatrixEnergy - Time.deltaTime / dvaMatrixDuration);
                var view = Camera.main != null ? Camera.main.transform : transform;
                m_MatrixOrigin.Value = view.position;
                m_MatrixRotation.Value = view.rotation;
                if (m_MatrixEnergy <= 0) { active = false; m_MatrixExhausted = true; }
            }
            else m_MatrixEnergy = Mathf.Min(1, m_MatrixEnergy + Time.deltaTime / dvaMatrixRecharge);
            m_MatrixActive.Value = active;
        }

        // Intersect the actual shot segment before its first world hit; melee never uses this path.
        public static bool TryBlockDvaMatrix(Vector3 origin, Vector3 direction, float distance, ulong attacker, out Vector3 point)
        {
            point = default;
            float nearest = distance;
            bool blocked = false;
            foreach (var shield in s_MatrixPlayers)
            {
                if (shield == null || !shield.IsSpawned || shield.CharacterId != 2 || !shield.DvaMatrixActive ||
                    shield.OwnerClientId == attacker || (shield.coreStats != null && !shield.coreStats.IsAlive)) continue;
                Quaternion inverse = Quaternion.Inverse(shield.m_MatrixRotation.Value);
                Vector3 localOrigin = inverse * (origin - shield.m_MatrixOrigin.Value);
                Vector3 localDirection = inverse * direction.normalized;
                if (localDirection.z >= 0) continue;
                float entry = 0;
                if (!MatrixBounds.Contains(localOrigin) && !MatrixBounds.IntersectRay(new Ray(localOrigin, localDirection), out entry)) continue;
                if (entry < 0 || entry > nearest) continue;
                nearest = entry;
                point = origin + direction.normalized * entry;
                blocked = true;
            }
            return blocked;
        }

        private void DrawDvaMatrix()
        {
            if (!DvaMatrixActive)
            {
                if (m_MatrixOutline != null) m_MatrixOutline.enabled = false;
                if(m_MatrixSurface!=null)m_MatrixSurface.SetActive(false);
                return;
            }
            if (m_MatrixOutline == null)
            {
                var shader = Shader.Find("Universal Render Pipeline/Unlit");
                if (shader == null) return;
                m_MatrixMaterial = new Material(shader);
                m_MatrixMaterial.SetColor("_BaseColor", new Color(0.15f, 1f, 0.55f));
                var visual = new GameObject("Dva_DefenseMatrix");
                visual.transform.SetParent(transform, false);
                m_MatrixOutline = visual.AddComponent<LineRenderer>();
                m_MatrixOutline.sharedMaterial = m_MatrixMaterial;
                m_MatrixOutline.widthMultiplier = 0.045f;
                m_MatrixOutline.shadowCastingMode = ShadowCastingMode.Off;
                m_MatrixOutline.receiveShadows = false;
            }
            m_MatrixOutline.enabled = true;
            Vector3 origin = m_MatrixOrigin.Value;
            Quaternion rotation = m_MatrixRotation.Value;
            if (IsOwner && Camera.main != null) { origin = Camera.main.transform.position; rotation = Camera.main.transform.rotation; }
            if(m_MatrixSurface==null)
            {
                m_MatrixSurface=GameObject.CreatePrimitive(PrimitiveType.Quad);
                m_MatrixSurface.name="Dva Matrix translucent field";
                m_MatrixSurface.transform.SetParent(transform,false);
                m_MatrixSurface.GetComponent<Collider>().enabled=false;
                m_MatrixSurfaceMaterial=new Material(m_MatrixMaterial);
                m_MatrixSurfaceMaterial.SetFloat("_Surface",1);
                m_MatrixSurfaceMaterial.SetFloat("_SrcBlend",(float)BlendMode.SrcAlpha);
                m_MatrixSurfaceMaterial.SetFloat("_DstBlend",(float)BlendMode.OneMinusSrcAlpha);
                m_MatrixSurfaceMaterial.SetFloat("_ZWrite",0);
                m_MatrixSurfaceMaterial.SetFloat("_Cull",0);
                m_MatrixSurfaceMaterial.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
                m_MatrixSurfaceMaterial.renderQueue=3000;
                var r=m_MatrixSurface.GetComponent<Renderer>();r.sharedMaterial=m_MatrixSurfaceMaterial;
                r.shadowCastingMode=ShadowCastingMode.Off;r.receiveShadows=false;
            }
            m_MatrixSurface.SetActive(true);
            m_MatrixSurface.transform.SetPositionAndRotation(origin+rotation*MatrixBounds.center,rotation);
            m_MatrixSurface.transform.localScale=new Vector3(3,2.4f,1);
            m_MatrixSurfaceMaterial.SetColor("_BaseColor",new Color(.12f,1,.5f,.12f+.035f*Mathf.Sin(Time.time*8)));
            Vector3[] corners = new Vector3[8];
            for (int i = 0; i < 8; i++) corners[i] = origin + rotation * (MatrixBounds.center + Vector3.Scale(MatrixBounds.extents,
                new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1)));
            int[] edges = {0,1,3,2,0,4,5,1,5,7,3,7,6,2,6,4};
            m_MatrixOutline.positionCount = edges.Length;
            for (int i = 0; i < edges.Length; i++) m_MatrixOutline.SetPosition(i, corners[edges[i]]);
        }

        [Header("Dva Self Destruct / E")]
        [SerializeField, Min(1f)] private float dvaExplosionRadius = 6f;
        [SerializeField, Min(0.5f)] private float dvaExplosionDelay = 3f;
        [SerializeField, Min(1f)] private float dvaExplosionDamage = 250f;
        private readonly NetworkVariable<double> m_DvaDetonateAt = new NetworkVariable<double>(0, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);
        private LineRenderer m_DvaBlastWarning;
        private TextMesh m_DvaBlastText;
        private Material m_DvaBlastMaterial;
        public bool DvaSelfDestructing => m_DvaDetonateAt.Value > 0;
        public float DvaSelfDestructRemaining => !IsSpawned || !DvaSelfDestructing ? 0 :
            Mathf.Max(0, (float)(m_DvaDetonateAt.Value - NetworkManager.ServerTime.Time));

        private void UpdateDvaSelfDestruct()
        {
            if (CharacterId != 2 || coreStats == null)
            {
                m_DvaDetonateAt.Value = 0;
                return;
            }
            if (DvaSelfDestructing)
            {
                m_DvaTrigger = false;
                if (DvaSelfDestructRemaining <= 0) DetonateDva();
                return;
            }
            if (!DvaPilot && coreStats.IsAlive && Cursor.lockState == CursorLockMode.Locked && Keyboard.current != null && Keyboard.current.eKey.wasPressedThisFrame)
                BeginDvaSelfDestruct();
        }

        private void BeginDvaSelfDestruct()
        {
            if(!IsOwner || !IsSpawned || CharacterId!=2 || DvaPilot || DvaSelfDestructing)return;
            m_DvaDetonateAt.Value = NetworkManager.ServerTime.Time + dvaExplosionDelay;
            EjectDva();
            DvaSelfDestructVoiceRpc(m_DvaBombPosition.Value);
            m_DvaTrigger = false;
            StopDvaAttackAudioRpc();
        }

        [Rpc(SendTo.Everyone)]
        private void DvaSelfDestructVoiceRpc(Vector3 origin, RpcParams rpc = default)
        {
            if (rpc.Receive.SenderClientId != OwnerClientId || CharacterId != 2) return;
            var clip = Resources.Load<AudioClip>("Audio/Dva/SelfDestructVoice");
            if (clip == null) return;
            var voice = new GameObject("Dva Self Destruct Voice");
            voice.transform.position = origin;
            var source = voice.AddComponent<AudioSource>();
            source.playOnAwake = false;
            source.loop = false;
            source.spatialBlend = IsOwner ? 0 : 1;
            source.rolloffMode = AudioRolloffMode.Linear;
            source.minDistance = 3;
            source.maxDistance = 60;
            source.volume = .8f;
            source.priority = 60;
            source.clip = clip;
            source.Play();
            Destroy(voice, clip.length + .2f);
        }

        private void DetonateDva()
        {
            // Owner evaluates one explosion; normal IHittable routing delivers each target's damage.
            m_DvaDetonateAt.Value = 0;
            Vector3 origin = m_DvaBombPosition.Value;
            var damaged = new HashSet<IHittable>();
            foreach (var collider in Physics.OverlapSphere(origin, dvaExplosionRadius, ~0, QueryTriggerInteraction.Ignore))
            {
                if (collider.transform.IsChildOf(transform)) continue;
                var target = collider.GetComponentInParent<IHittable>();
                if (target == null || damaged.Contains(target)) continue;
                Vector3 point = collider.ClosestPoint(origin);
                Vector3 delta = point - origin;
                float distance = delta.magnitude;
                if (distance > 0.001f && NinjaShurikenFlight.FirstHit(origin, delta / distance, distance + 0.02f, transform, out var obstruction) &&
                    obstruction.collider.GetComponentInParent<IHittable>() != target) continue;
                damaged.Add(target);
                float amount = Mathf.Lerp(dvaExplosionDamage, dvaExplosionDamage * 0.16f,
                    Mathf.InverseLerp(1.5f, dvaExplosionRadius, distance));
                target.OnHit(new HitInfo { amount = amount, attackerId = OwnerClientId, hitPoint = point,
                    hitNormal = -delta.normalized, impactForce = delta.normalized * 25f });
            }
            DvaExplosionRpc(origin);

        }

        [Rpc(SendTo.Everyone)]
        private void DvaExplosionRpc(Vector3 origin, RpcParams rpcParams = default)
        {
            if (rpcParams.Receive.SenderClientId != OwnerClientId || CharacterId != 2) return;
            PlayDvaExplosionEffects(origin);
            StartCoroutine(DvaExplosionVisual(origin));
        }

        private System.Collections.IEnumerator DvaExplosionVisual(Vector3 origin)
        {
            var shader = Shader.Find("Universal Render Pipeline/Unlit");
            if (shader == null) yield break;
            var effect = new GameObject("Dva_SelfDestructBlast");
            var material = new Material(shader);
            material.SetColor("_BaseColor", new Color(1f, 0.35f, 0.08f));
            var ring = effect.AddComponent<LineRenderer>();
            ring.sharedMaterial = material;
            ring.loop = true; ring.positionCount = 64; ring.widthMultiplier = 0.15f;
            ring.shadowCastingMode = ShadowCastingMode.Off;
            // Scheduled cleanup survives this player's despawn/coroutine cancellation.
            Destroy(effect, 0.6f); Destroy(material, 0.6f);
            float began = Time.time;
            while (Time.time - began < 0.5f)
            {
                float t = (Time.time - began) / 0.5f;
                float radius = dvaExplosionRadius * Mathf.Clamp01(t * 2f);
                ring.widthMultiplier = 0.2f * (1f - t);
                for (int i = 0; i < 64; i++)
                {
                    float angle = i * Mathf.PI * 2f / 64;
                    ring.SetPosition(i, origin + new Vector3(Mathf.Cos(angle), 0, Mathf.Sin(angle)) * radius);
                }
                yield return null;
            }
        }

        private void DrawDvaSelfDestruct()
        {
            bool active = CharacterId == 2 && DvaSelfDestructing;
            if (!active)
            {
                if (m_DvaBlastWarning != null) m_DvaBlastWarning.gameObject.SetActive(false);
                return;
            }
            if (m_DvaBlastWarning == null)
            {
                var shader = Shader.Find("Universal Render Pipeline/Unlit");
                if (shader == null) return;
                m_DvaBlastMaterial = new Material(shader);
                m_DvaBlastMaterial.SetColor("_BaseColor", new Color(1, 0.15f, 0.05f));
                var warning = new GameObject("Dva_SelfDestructWarning");
                warning.transform.SetParent(transform, false);
                m_DvaBlastWarning = warning.AddComponent<LineRenderer>();
                m_DvaBlastWarning.sharedMaterial = m_DvaBlastMaterial;
                m_DvaBlastWarning.loop = true; m_DvaBlastWarning.positionCount = 64;
                m_DvaBlastWarning.shadowCastingMode = ShadowCastingMode.Off;
                var label = new GameObject("Countdown"); label.transform.SetParent(warning.transform, false);
                m_DvaBlastText = label.AddComponent<TextMesh>();
                m_DvaBlastText.anchor = TextAnchor.MiddleCenter;
                m_DvaBlastText.fontSize = 48; m_DvaBlastText.characterSize = 0.08f;
                m_DvaBlastText.color = new Color(1, 0.25f, 0.1f);
            }
            m_DvaBlastWarning.gameObject.SetActive(true);
            m_DvaBlastWarning.widthMultiplier = 0.04f + 0.025f * (1f + Mathf.Sin(Time.time * 18));
            Vector3 center = m_DvaBombPosition.Value - Vector3.up * 0.88f;
            for (int i = 0; i < 64; i++)
            {
                float angle = i * Mathf.PI * 2f / 64;
                m_DvaBlastWarning.SetPosition(i, center + new Vector3(Mathf.Cos(angle), 0, Mathf.Sin(angle)) * dvaExplosionRadius);
            }
            m_DvaBlastText.text = $"SELF DESTRUCT {DvaSelfDestructRemaining:0.0}";
            m_DvaBlastText.transform.position = m_DvaBombPosition.Value + Vector3.up * 1.6f;
            if (Camera.main != null) m_DvaBlastText.transform.rotation = Camera.main.transform.rotation;
        }

        private void InitializeDva()
        {
            InitializeDvaPilot();
            if (IsOwner) m_DvaBoostVfxActive.Value = false;
            s_MatrixPlayers.Add(this);
            m_MatrixEnergy = 1f;
            m_MatrixExhausted = false;
            if (IsOwner) { m_MatrixActive.Value = false; m_DvaDetonateAt.Value = 0; }
            m_CharacterId.OnValueChanged += DvaCharacterChanged;
            RefreshDva();
        }
        private void DvaCharacterChanged(int previous, int current)
        {
            if(current!=2)
            {
                if(m_DvaBody!=null)m_DvaBody.SetActive(false);
                if(m_DvaView!=null)m_DvaView.SetActive(false);
                if(m_DvaPilotGun!=null)m_DvaPilotGun.SetActive(false);
                if(m_MatrixOutline!=null)m_MatrixOutline.gameObject.SetActive(false);
                if(previous==2)
                {
                    foreach(var entry in m_DvaHiddenRenderers)if(entry.Key!=null)entry.Key.enabled=entry.Value;
                    RefreshNinjaWeapons();
                    if(m_RevolverVisual!=null)m_RevolverVisual.SetActive(current==3);
                }
            }
            else
            {
                foreach(var entry in m_DvaHiddenRenderers)if(entry.Key!=null)entry.Key.enabled=false;
                RefreshDva();
            }
        }
        private void RefreshDva()
        {
            if (CharacterId != 2) return;
            if (m_DvaBody != null) return;
            var prefab = Resources.Load<GameObject>("Dva/DvaPinkDisplay");
            if (prefab == null) { Debug.LogError("Dva model missing: Resources/Dva/DvaPink", this); return; }
            foreach (var renderer in GetComponentsInChildren<Renderer>(true))
            {
                m_DvaHiddenRenderers[renderer] = renderer.enabled;
                renderer.enabled = false;
            }
            m_DvaBody = new GameObject("Dva_WorldModel");
            m_DvaBody.transform.SetParent(transform, false);
            var bodyMesh = Instantiate(prefab, m_DvaBody.transform, false);
            float importCorrection = NormalizeDvaImport(bodyMesh, 3f);
            m_DvaBody.transform.localScale = Vector3.one * 0.65f;
            m_DvaBody.transform.localRotation = Quaternion.Euler(0, 90, 0);
            ConfigureDvaModel(m_DvaBody, false);
            foreach(var node in m_DvaBody.GetComponentsInChildren<Transform>(true))
            {
                if(node.name=="DvaMuzzle_L")m_DvaWorldMuzzles[0]=node;
                if(node.name=="DvaMuzzle_R")m_DvaWorldMuzzles[1]=node;
            }
            if (IsOwner)
            {
                var arms = Resources.Load<GameObject>("Dva/DvaArmsDisplay");
                if (arms == null) { Debug.LogError("DvaArms model missing", this); return; }
                m_DvaView = new GameObject("Dva_FirstPersonArms");
                m_DvaView.transform.SetParent(transform, false);
                var armsMesh = Instantiate(arms, m_DvaView.transform, false);
                armsMesh.transform.localScale *= importCorrection;
                m_DvaView.transform.localScale = Vector3.one * dvaArmsScale;
                foreach (var part in m_DvaView.GetComponentsInChildren<Transform>())
                {
                    if (part.name == "DvaMuzzle_L") m_DvaMuzzles[0] = part;
                    if (part.name == "DvaMuzzle_R") m_DvaMuzzles[1] = part;
                }
                ConfigureDvaModel(m_DvaView, true);
            }
        }
        private static float NormalizeDvaImport(GameObject model, float expectedHeight)
        {
            // Measure in parent space, preserving the imported FBX axis/scale conversion.
            var parent = model.transform.parent;
            Bounds bounds = default;
            bool found = false;
            foreach (var filter in model.GetComponentsInChildren<MeshFilter>(true))
            {
                if (filter.sharedMesh == null) continue;
                var b = filter.sharedMesh.bounds;
                var matrix = parent.worldToLocalMatrix * filter.transform.localToWorldMatrix;
                for (int i = 0; i < 8; i++)
                {
                    Vector3 point = matrix.MultiplyPoint3x4(b.center + Vector3.Scale(b.extents,
                        new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1)));
                    if (!found) { bounds = new Bounds(point, Vector3.zero); found = true; }
                    else bounds.Encapsulate(point);
                }
            }
            if (!found || bounds.size.y < 0.0001f)
            {
                Debug.LogError("Dva display model has no valid mesh bounds", model);
                return 1f;
            }
            float correction = expectedHeight / bounds.size.y;
            model.transform.localScale *= correction;
            Debug.Log($"Dva display loaded: {model.name}, height={bounds.size.y:F3}, correction={correction:F3}", model);
            return correction;
        }

        private void ConfigureDvaModel(GameObject model, bool view)
        {
            foreach (var part in model.GetComponentsInChildren<Transform>(true))
            {
                part.gameObject.SetActive(true);
                part.gameObject.layer = gameObject.layer;
            }
            foreach (var collider in model.GetComponentsInChildren<Collider>()) collider.enabled = false;
            foreach (var animator in model.GetComponentsInChildren<Animator>()) animator.enabled = false;
            foreach (var part in model.GetComponentsInChildren<Transform>())
                if (part.name.StartsWith("DvaRotor_")) m_DvaRotors.Add(part);
            foreach (var renderer in model.GetComponentsInChildren<Renderer>())
            {
                renderer.enabled = true;
                renderer.shadowCastingMode = view ? ShadowCastingMode.Off : ShadowCastingMode.On;
                var converted = new Material[renderer.sharedMaterials.Length];
                for (int i = 0; i < converted.Length; i++)
                {
                    var original = renderer.sharedMaterials[i];
                    var shader = Shader.Find("Universal Render Pipeline/Lit");
                    if (shader == null) { converted[i] = original; continue; }
                    var material = new Material(shader);
                    string name = original != null ? original.name.ToLowerInvariant() : "pink";
                    Color color = name.Contains("pink") ? new Color(1, 0.23f, 0.52f) :
                        name.Contains("mint") ? new Color(0.04f, 0.9f, 0.65f) :
                        name.Contains("white") ? new Color(0.85f, 0.9f, 1) :
                        name.Contains("blue") ? new Color(0.04f, 0.2f, 0.35f) : new Color(0.2f, 0.25f, 0.32f);
                    material.SetColor("_BaseColor", color);
                    material.SetFloat("_Metallic", 0.35f);
                    material.SetFloat("_Smoothness", 0.6f);
                    converted[i] = material; m_DvaMaterials.Add(material);
                }
                renderer.sharedMaterials = converted;
            }
        }
        private void UpdateDva()
        {
            if (!IsSpawned) return;
            UpdateDvaPilot();
            UpdateDvaSelfDestruct();
            UpdateDvaMatrix();
            UpdateDvaVfxState();
            if (CharacterId != 2 || DvaPilot) return;
            if (coreStats != null && !coreStats.IsAlive || Cursor.lockState != CursorLockMode.Locked)
            { m_DvaTrigger = false; return; }
            if (DvaSelfDestructing || DvaMatrixActive || !m_DvaTrigger || Time.time < m_DvaNextShot) return;
            m_DvaNextShot = Time.time + dvaShotInterval;
            var view = Camera.main != null ? Camera.main.transform : transform;
            PositionDvaArms();
            for (int side = 0; side < 2; side++)
            {
                Vector3 muzzle = m_DvaMuzzles[side] != null ? m_DvaMuzzles[side].position :
                    view.TransformPoint(new Vector3(side == 0 ? -0.5f : 0.5f, -0.2f, 0.7f));
                Vector3 offset = muzzle - view.position;
                if (NinjaShurikenFlight.FirstHit(view.position, offset.normalized, offset.magnitude, transform, out var obstruction))
                    muzzle = obstruction.point - offset.normalized * 0.02f;
                for (int pellet = 0; pellet < 4; pellet++)
                {
                    Vector2 spread = Random.insideUnitCircle * 0.075f;
                    Vector3 aim = (view.forward + view.right * spread.x + view.up * spread.y).normalized;
                    Vector3 target = view.position + aim * dvaRange;
                    if (NinjaShurikenFlight.FirstHit(view.position, aim, dvaRange, transform, out var aimHit)) target = aimHit.point;
                    Vector3 direction = (target - muzzle).normalized;
                    Vector3 end = muzzle + direction * dvaRange;
                    bool hasHit = NinjaShurikenFlight.FirstHit(muzzle, direction, dvaRange, transform, out var hit);
                    if (TryInterceptBullet(muzzle, direction, hasHit ? hit.distance : dvaRange, OwnerClientId, dvaDamagePerBarrel * 0.25f * Mathf.Lerp(1f, 0.2f, Mathf.InverseLerp(8f, dvaRange, hasHit ? hit.distance : dvaRange)), true, out var blockedPoint))
                    {
                        DvaShotRpc(muzzle, blockedPoint, pellet == 0);
                        continue;
                    }
                    if (hasHit)
                    {
                        end = hit.point;
                        float falloff = Mathf.Lerp(1f, 0.2f, Mathf.InverseLerp(8f, dvaRange, hit.distance));
                        hit.collider.GetComponentInParent<IHittable>()?.OnHit(new HitInfo {
                            amount = dvaDamagePerBarrel * 0.25f * falloff, attackerId = OwnerClientId,
                            hitPoint = hit.point, hitNormal = hit.normal, impactForce = direction * 3 });
                    }
                    DvaShotRpc(muzzle, end, pellet == 0);
                }
            }
        }
        [Rpc(SendTo.Everyone)]
        private void DvaShotRpc(Vector3 origin, Vector3 end, bool flash, RpcParams rpcParams = default)
        {
            if (rpcParams.Receive.SenderClientId != OwnerClientId || CharacterId != 2) return;
            if (flash) PlayDvaAttackAudio(false);
            if(!IsOwner)
            {
                Transform nearest=null;float distance=float.MaxValue;
                foreach(var muzzle in m_DvaWorldMuzzles)
                    if(muzzle!=null && (muzzle.position-origin).sqrMagnitude<distance)
                    {nearest=muzzle;distance=(muzzle.position-origin).sqrMagnitude;}
                if(nearest!=null)origin=nearest.position;
            }
            RenderDvaShot(origin, end, flash);
        }
        private void RenderDvaShot(Vector3 origin, Vector3 end, bool flash)
        {
            m_DvaLastShot = Time.time;
            if (m_DvaBulletMaterial == null)
            {
                var shader = Shader.Find("Universal Render Pipeline/Unlit");
                if (shader == null) return;
                m_DvaBulletMaterial = new Material(shader);
                m_DvaBulletMaterial.SetColor("_BaseColor", new Color(0.25f, 1f, 0.72f));
            }
            var streak = new GameObject("DvaFusionStreak");
            var line = streak.AddComponent<LineRenderer>();
            line.sharedMaterial = m_DvaBulletMaterial;
            line.positionCount = 2; line.startWidth = 0.035f; line.endWidth = 0.009f;
            line.SetPosition(0, origin); line.SetPosition(1, end);
            Destroy(streak, 0.09f);
            if (flash)
            {
                var burst = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                burst.name = "DvaMuzzleFlash";
                burst.GetComponent<Collider>().enabled = false;
                burst.transform.SetPositionAndRotation(origin, Quaternion.LookRotation((end-origin).normalized));
                burst.transform.localScale = new Vector3(0.12f, 0.12f, 0.22f);
                burst.GetComponent<Renderer>().sharedMaterial = m_DvaBulletMaterial;
                burst.GetComponent<Renderer>().shadowCastingMode = ShadowCastingMode.Off;
                Destroy(burst, 0.09f);
            }
        }
        private void PositionDvaArms()
        {
            if (m_DvaView == null || Camera.main == null) return;
            var cameraTransform = Camera.main.transform;
            float elapsed = Time.time - m_DvaLastShot;
            float recoil = elapsed >= 0 && elapsed < dvaShotInterval ?
                Mathf.Sin(elapsed / dvaShotInterval * Mathf.PI) * 0.065f : 0;
            m_DvaView.transform.SetPositionAndRotation(cameraTransform.TransformPoint(dvaArmsOffset + Vector3.back * recoil + (DvaMatrixActive ? new Vector3(0, 0.05f, 0.12f) : Vector3.zero)),
                cameraTransform.rotation * Quaternion.Euler(0, -90, 0));
        }
        private void LateUpdateDva()
        {
            UpdateDvaAttackAudio();
            if (!IsSpawned) return;
            DrawDvaMatrix();
            DrawDvaSelfDestruct();
            DrawDvaQuickEffects();
            if (CharacterId != 2) return;
            if (m_DvaBody == null) RefreshDva();
            // Camera initialization also edits child skinned renderers. Reassert the class-specific visibility afterwards.
            foreach (var entry in m_DvaHiddenRenderers) if (entry.Key != null) entry.Key.enabled = false;
            if (m_DvaBody != null)
                foreach (var renderer in m_DvaBody.GetComponentsInChildren<Renderer>())
                {
                    renderer.enabled = true;
                    renderer.gameObject.layer = IsOwner ? 31 : gameObject.layer;
                    renderer.shadowCastingMode = ShadowCastingMode.On;
                }
            if (m_DvaView != null)
                foreach (var renderer in m_DvaView.GetComponentsInChildren<Renderer>())
                    if (renderer.enabled) renderer.shadowCastingMode = ShadowCastingMode.Off;
            if (IsOwner && Camera.main != null && m_DvaMaskedCamera == null)
            {
                m_DvaMaskedCamera = Camera.main;
                m_DvaCameraHadBodyLayer = (m_DvaMaskedCamera.cullingMask & (1 << 31)) != 0;
                m_DvaMaskedCamera.cullingMask &= ~(1 << 31);
            }
            DrawDvaPilot();
            if (!DvaPilot) PositionDvaArms();
            if (!DvaPilot && Time.time - m_DvaLastShot < 0.18f)
                foreach (var rotor in m_DvaRotors)
                    if (rotor != null) rotor.Rotate((rotor.name.EndsWith("L") ? 1 : -1) * 720 * Time.deltaTime, 0, 0, Space.Self);
        }
        private void CleanupDva()
        {
            StopDvaAttackAudio();
            if (m_DvaAttackAudio != null) Destroy(m_DvaAttackAudio.gameObject);
            CleanupDvaQuickEffects();
            CleanupDvaPilot();
            s_MatrixPlayers.Remove(this);
            if (m_DvaBlastWarning != null) Destroy(m_DvaBlastWarning.gameObject);
            if (m_DvaBlastMaterial != null) Destroy(m_DvaBlastMaterial);
            if (m_MatrixOutline != null) Destroy(m_MatrixOutline.gameObject);
            if (m_MatrixMaterial != null) Destroy(m_MatrixMaterial);
            if(m_MatrixSurface!=null)Destroy(m_MatrixSurface);
            if(m_MatrixSurfaceMaterial!=null)Destroy(m_MatrixSurfaceMaterial);
            if (m_DvaMaskedCamera != null && m_DvaCameraHadBodyLayer) m_DvaMaskedCamera.cullingMask |= 1 << 31;
            m_DvaMaskedCamera = null;
            m_CharacterId.OnValueChanged -= DvaCharacterChanged;
            m_DvaTrigger = false;
            foreach (var entry in m_DvaHiddenRenderers) if (entry.Key != null) entry.Key.enabled = entry.Value;
            m_DvaHiddenRenderers.Clear(); m_DvaRotors.Clear();
            if (m_DvaBody != null) Destroy(m_DvaBody);
            if (m_DvaView != null) Destroy(m_DvaView);
            foreach (var material in m_DvaMaterials) Destroy(material);
            m_DvaMaterials.Clear();
            if (m_DvaBulletMaterial != null) Destroy(m_DvaBulletMaterial);
        }
    }
}
