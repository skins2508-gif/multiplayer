using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.Rendering;

namespace Blocks.Gameplay.Shooter
{
    public partial class WeaponController
    {
        [Header("Dva Quick Effects")]
        [SerializeField] private Vector3 dvaBoostVfxOffset = new Vector3(0.42f, 1.2f, -0.45f);
        [SerializeField, Min(0.05f)] private float dvaBoostVfxScale = 0.22f;
        private readonly NetworkVariable<bool> m_DvaBoostVfxActive = new NetworkVariable<bool>(false, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);
        private readonly NetworkVariable<Quaternion> m_DvaBoostVfxRotation = new NetworkVariable<Quaternion>(Quaternion.identity, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);
        private GameObject m_DvaBoostVfxLeft, m_DvaBoostVfxRight, m_DvaMatrixVfx;
        private AudioSource m_DvaBoostAudio;

        private void UpdateDvaBoostAudio(bool active)
        {
            if (active && m_DvaBoostAudio == null)
            {
                var go = new GameObject("Dva Booster Audio");
                go.transform.SetParent(transform, false);
                m_DvaBoostAudio = go.AddComponent<AudioSource>();
                m_DvaBoostAudio.playOnAwake = false;
                m_DvaBoostAudio.loop = true;
                m_DvaBoostAudio.clip = Resources.Load<AudioClip>("Audio/Dva/Boost");
                m_DvaBoostAudio.rolloffMode = AudioRolloffMode.Linear;
                m_DvaBoostAudio.minDistance = 2;
                m_DvaBoostAudio.maxDistance = 35;
                m_DvaBoostAudio.volume = .55f;
            }
            if (m_DvaBoostAudio == null) return;
            if (!active) { m_DvaBoostAudio.Stop(); return; }
            m_DvaBoostAudio.spatialBlend = IsOwner && !DvaPilot ? 0 : 1;
            m_DvaBoostAudio.transform.position = DvaPilot ? m_DvaBombPosition.Value : transform.position + Vector3.up;
            if (!m_DvaBoostAudio.isPlaying && m_DvaBoostAudio.clip != null) m_DvaBoostAudio.Play();
        }
        private readonly List<Material> m_DvaVfxMaterials = new List<Material>();
        private static Texture2D s_DvaSoftParticle;
        private static Texture2D SoftParticleTexture()
        {
            if (s_DvaSoftParticle != null) return s_DvaSoftParticle;
            s_DvaSoftParticle = new Texture2D(64,64,TextureFormat.RGBA32,false);
            s_DvaSoftParticle.name = "Soft particle fallback";
            s_DvaSoftParticle.wrapMode = TextureWrapMode.Clamp;
            var pixels = new Color[64*64];
            for(int y=0;y<64;y++)for(int x=0;x<64;x++)
            {
                float r = new Vector2((x-31.5f)/31.5f,(y-31.5f)/31.5f).magnitude;
                float a = Mathf.Pow(Mathf.Clamp01(1-r),2);
                pixels[y*64+x] = new Color(1,1,1,a);
            }
            s_DvaSoftParticle.SetPixels(pixels);s_DvaSoftParticle.Apply(false,true);
            return s_DvaSoftParticle;
        }

        private void UpdateDvaVfxState()
        {
            bool flyingBomb = DvaSelfDestructing && Time.time < m_DvaBombFlightUntil && m_DvaBombVelocity.sqrMagnitude > .01f;
            m_DvaBoostVfxActive.Value = CharacterId == 2 && (DvaBoosting || flyingBomb);
            if (m_DvaBoostVfxActive.Value)
            {
                Vector3 forward = flyingBomb ? m_DvaBombVelocity.normalized : (Camera.main != null ? Camera.main.transform.forward : transform.forward);
                m_DvaBoostVfxRotation.Value = Quaternion.LookRotation(forward);
            }
        }
        private GameObject CreateDvaQuickEffect(string resource, Color color, bool oneShot = false)
        {
            var source = Resources.Load<GameObject>("DvaVfx/" + resource);
            if (source == null) return null;
            var effect = Instantiate(source, transform, false);
            effect.name = "Dva_" + resource + "_QuickEffect";
            // Pack demo hotkeys must not destroy gameplay effects or read legacy input.
            foreach (var behaviour in effect.GetComponentsInChildren<MonoBehaviour>(true))
                if (behaviour != null && behaviour.GetType().Name == "csDestroyEffect") behaviour.enabled = false;
            foreach (var part in effect.GetComponentsInChildren<Transform>(true)) part.gameObject.layer = 0;
            foreach (var ps in effect.GetComponentsInChildren<ParticleSystem>(true))
            {
                ps.Stop(false, ParticleSystemStopBehavior.StopEmittingAndClear);
                var main = ps.main;
                main.loop = !oneShot; main.playOnAwake = false; main.stopAction = ParticleSystemStopAction.None;
                main.scalingMode = ParticleSystemScalingMode.Hierarchy;
                if (!oneShot) main.startColor = color;
                if (oneShot) { main.duration = Mathf.Min(main.duration, 1.2f); main.startLifetimeMultiplier = Mathf.Min(main.startLifetimeMultiplier, 3f); }
                main.maxParticles = Mathf.Min(main.maxParticles, oneShot ? 320 : 160);
                var tint = ps.colorOverLifetime; if (!oneShot) tint.enabled = false;
                var collision = ps.collision; collision.enabled = false;
                // Remove the pack's opaque backing so the owner can see through the field.
                if (ps.name == "BlackBack") ps.gameObject.SetActive(false);
            }
            var shader = Shader.Find("Universal Render Pipeline/Particles/Unlit");
            if (shader != null)
            foreach (var renderer in effect.GetComponentsInChildren<ParticleSystemRenderer>(true))
            {
                var originals = renderer.sharedMaterials;
                var materials = new Material[originals.Length];
                for (int i = 0; i < originals.Length; i++)
                {
                    var material = new Material(shader);
                    var sourceMaterial = originals[i];
                    Texture texture = sourceMaterial != null && sourceMaterial.HasProperty("_MainTex") ? sourceMaterial.GetTexture("_MainTex") : null;
                    if (texture == null && sourceMaterial != null && sourceMaterial.HasProperty("_BaseMap")) texture = sourceMaterial.GetTexture("_BaseMap");
                    // Built-in particle shaders may hide serialized _MainTex after import in URP.
                    if (sourceMaterial != null && sourceMaterial.name.StartsWith("DistortedFlare01"))
                        texture = Resources.Load<Texture2D>("DvaVfx/Textures/DistortedFlare01");
                    else if (sourceMaterial != null && sourceMaterial.name.StartsWith("Flare00"))
                        texture = Resources.Load<Texture2D>("DvaVfx/Textures/Flare00");
                    if (texture == null || texture == Texture2D.whiteTexture) texture = SoftParticleTexture();
                    material.SetTexture("_BaseMap", texture);
                    material.SetColor("_BaseColor", Color.white);
                    material.SetFloat("_Surface", 1); material.SetFloat("_Blend", 2);
                    material.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
                    bool smoke = oneShot && renderer.name.ToLowerInvariant().Contains("smoke");
                    material.SetFloat("_DstBlend", (float)(smoke ? BlendMode.OneMinusSrcAlpha : BlendMode.One));
                    if (smoke) material.SetFloat("_Blend", 0);
                    material.SetFloat("_ZWrite", 0); material.SetFloat("_Cull", (float)CullMode.Off);
                    material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
                    material.SetOverrideTag("RenderType", "Transparent");
                    material.SetFloat("_SrcBlendAlpha", (float)BlendMode.One);
                    material.SetFloat("_DstBlendAlpha", (float)BlendMode.OneMinusSrcAlpha);
                    material.renderQueue = (int)RenderQueue.Transparent;
                    materials[i] = material;
                    if (oneShot) Destroy(material, 8f); else m_DvaVfxMaterials.Add(material);
                }
                renderer.sharedMaterials = materials;
                renderer.shadowCastingMode = ShadowCastingMode.Off;
                renderer.receiveShadows = false;
            }
            effect.SetActive(false);
            return effect;
        }
        private static void SetDvaEffectActive(GameObject effect, bool active)
        {
            if (effect == null || effect.activeSelf == active) return;
            effect.SetActive(active);
            foreach (var ps in effect.GetComponentsInChildren<ParticleSystem>())
                if (active) ps.Play(false); else ps.Stop(false, ParticleSystemStopBehavior.StopEmittingAndClear);
        }
        private GameObject CreateDvaShieldPanel()
        {
            var shader = Shader.Find("Universal Render Pipeline/Unlit");
            if (shader == null) return null;
            var panel = GameObject.CreatePrimitive(PrimitiveType.Quad);
            panel.name = "Dva_StaticShieldPanel";
            panel.transform.SetParent(transform, false);
            panel.GetComponent<Collider>().enabled = false;
            var material = new Material(shader);
            material.SetColor("_BaseColor", new Color(.12f, .9f, .65f, .18f));
            material.SetFloat("_Surface", 1);
            material.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
            material.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
            material.SetFloat("_ZWrite", 0);
            material.SetFloat("_Cull", (float)CullMode.Off);
            material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            material.renderQueue = (int)RenderQueue.Transparent;
            var renderer = panel.GetComponent<MeshRenderer>();
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            m_DvaVfxMaterials.Add(material);
            panel.SetActive(false);
            return panel;
        }
        private void DrawDvaQuickEffects()
        {
            bool boost = IsSpawned && CharacterId == 2 && m_DvaBoostVfxActive.Value;
            UpdateDvaBoostAudio(boost);
            bool shield = IsSpawned && CharacterId == 2 && !DvaPilot && DvaMatrixActive;
            if (boost && m_DvaBoostVfxLeft == null)
            {
                m_DvaBoostVfxLeft = CreateDvaQuickEffect("Boost", new Color(.25f, .8f, 1f, .7f));
                m_DvaBoostVfxRight = CreateDvaQuickEffect("Boost", new Color(.25f, .8f, 1f, .7f));
            }
            if (shield && m_DvaMatrixVfx == null) m_DvaMatrixVfx = CreateDvaShieldPanel();
            SetDvaEffectActive(m_DvaBoostVfxLeft, boost);
            SetDvaEffectActive(m_DvaBoostVfxRight, boost);
            SetDvaEffectActive(m_DvaMatrixVfx, shield);
            if (boost)
            {
                Vector3 center = DvaPilot ? m_DvaBombPosition.Value - Vector3.up : transform.position;
                Quaternion heading = m_DvaBoostVfxRotation.Value;
                for (int side=0; side<2; side++)
                {
                    var effect = side == 0 ? m_DvaBoostVfxLeft : m_DvaBoostVfxRight;
                    if (effect == null) continue;
                    Vector3 offset = dvaBoostVfxOffset; offset.x *= side == 0 ? -1 : 1;
                    effect.transform.SetPositionAndRotation(center + heading * offset, heading * Quaternion.Euler(0,180,0));
                    effect.transform.localScale = Vector3.one * dvaBoostVfxScale;
                }
            }
            if (shield && m_DvaMatrixVfx != null)
            {
                Vector3 origin = m_MatrixOrigin.Value; Quaternion rotation = m_MatrixRotation.Value;
                if (IsOwner && Camera.main != null) { origin = Camera.main.transform.position; rotation = Camera.main.transform.rotation; }
                m_DvaMatrixVfx.transform.SetPositionAndRotation(origin + rotation * new Vector3(0,0,2.2f), rotation);
                m_DvaMatrixVfx.transform.localScale = new Vector3(MatrixBounds.size.x, MatrixBounds.size.y, 1f);
            }
        }
        [SerializeField, Min(0.05f)] private float dvaExplosionVfxScale = 0.6f;
        private void PlayDvaExplosionEffects(Vector3 origin)
        {
            foreach (string resource in new[] { "SelfDestructCore", "SelfDestructRing", "Shockwave" })
            {
                var effect = CreateDvaQuickEffect(resource, Color.white, true);
                if (effect == null) continue;
                bool core = resource == "SelfDestructCore";
                foreach (var ps in effect.GetComponentsInChildren<ParticleSystem>(true))
                {
                    string part = ps.name.ToLowerInvariant();
                    // The secondary layer contributes rings and sparks, not duplicate smoke clouds.
                    if (resource == "SelfDestructRing" && part.Contains("smoke"))
                        ps.gameObject.SetActive(false);
                    var main = ps.main;
                    main.startDelayMultiplier += core ? 0 : .12f;
                    if (part.Contains("spark")) main.startSpeedMultiplier *= 1.35f;
                }
                foreach (var light in effect.GetComponentsInChildren<Light>(true))
                {
                    light.enabled = core;
                    light.shadows = LightShadows.None;
                    light.range = dvaExplosionRadius * 2;
                    light.intensity = Mathf.Min(light.intensity, 6);
                }
                // Detach so the blast stays at the mech, even if the pilot moves or despawns.
                effect.transform.SetParent(null, false);
                effect.transform.SetPositionAndRotation(origin, Quaternion.identity);
                effect.transform.localScale = Vector3.one * dvaExplosionVfxScale * (core ? 1.8f : 1.35f);
                SetDvaEffectActive(effect, true);
                Destroy(effect, 7f);
            }
        }

        private void CleanupDvaQuickEffects()
        {
            if (m_DvaBoostAudio != null) { m_DvaBoostAudio.Stop(); Destroy(m_DvaBoostAudio.gameObject); }
            if (m_DvaBoostVfxLeft != null) Destroy(m_DvaBoostVfxLeft);
            if (m_DvaBoostVfxRight != null) Destroy(m_DvaBoostVfxRight);
            if (m_DvaMatrixVfx != null) Destroy(m_DvaMatrixVfx);
            foreach (var material in m_DvaVfxMaterials) if (material != null) Destroy(material);
            m_DvaVfxMaterials.Clear();
        }
    }
}
