using System.Collections;
using System.Collections.Generic;
using Blocks.Gameplay.Core;
using Unity.Netcode;
using UnityEngine;

namespace Blocks.Gameplay.Shooter
{
    public sealed partial class ShooterEscortMatch
    {
        [Rpc(SendTo.Everyone)]
        void BotMechDestructRpc(int slot,Vector3 origin,double detonateAt,int round,RpcParams rpc=default)
        {
            if(rpc.Receive.SenderClientId!=OwnerClientId || slot<0 || slot>=botViews.Length)return;
            if(!IsSpawned || epoch.Value!=round || !CombatAllowed || !BotInSlot(slot) || detonateAt<=Now)return;
            StartCoroutine(BotMechDestruct(slot,origin,detonateAt,round));
        }

        IEnumerator BotMechDestruct(int slot,Vector3 origin,double detonateAt,int round)
        {
            var mech=botViews[slot]!=null?botViews[slot].CopyDestroyedMech():null;
            if(mech!=null) mech.AddComponent<DvaAbandonedMechLifetime>().seconds = Mathf.Max(0,(float)(detonateAt-Now)) + .1f;
            var warning=new GameObject("AI Dva self destruct");
            warning.transform.position=origin;
            var audio=warning.AddComponent<AudioSource>();
            audio.spatialBlend=1;audio.minDistance=3;audio.maxDistance=60;
            audio.rolloffMode=AudioRolloffMode.Linear;audio.volume=.8f;
            audio.clip=Resources.Load<AudioClip>("Audio/Dva/SelfDestructVoice");
            if(audio.clip!=null)audio.Play();
            var textObject=new GameObject("Countdown");textObject.transform.SetParent(warning.transform,false);
            textObject.transform.localPosition=Vector3.up*1.5f;
            var label=textObject.AddComponent<TextMesh>();label.anchor=TextAnchor.MiddleCenter;
            label.characterSize=.1f;label.fontSize=48;label.color=Color.red;
            var ring=warning.AddComponent<LineRenderer>();
            var shader=Shader.Find("Universal Render Pipeline/Unlit");
            var material=shader!=null?new Material(shader):null;
            if(material!=null){material.color=new Color(1,.25f,.08f);ring.sharedMaterial=material;}
            else ring.enabled=false;
            ring.loop=true;ring.positionCount=64;ring.widthMultiplier=.1f;
            try
            {
                while(IsSpawned && epoch.Value==round && CombatAllowed && BotInSlot(slot) && Now<detonateAt)
                {
                    label.text=$"자폭 {Mathf.Max(0,(float)(detonateAt-Now)):0.0}";
                    if(Camera.main!=null)label.transform.rotation=Camera.main.transform.rotation;
                    DrawBotBlastRing(ring,origin,6);
                    yield return null;
                }
                if(!IsSpawned || epoch.Value!=round || !CombatAllowed || !BotInSlot(slot))yield break;
                if(mech!=null)mech.SetActive(false);
                label.gameObject.SetActive(false);
                if(HasAuthority)DamageFromBotMech(slot,origin);
                float began=Time.time;
                while(Time.time-began<.5f)
                {
                    float progress=(Time.time-began)/.5f;
                    DrawBotBlastRing(ring,origin,6*Mathf.Clamp01(progress*2));
                    ring.widthMultiplier=.3f*(1-progress);
                    yield return null;
                }
            }
            finally
            {
                if(mech!=null)
                {
                    Destroy(mech);
                }
                Destroy(warning);Destroy(material);
            }
        }

        static void DrawBotBlastRing(LineRenderer ring,Vector3 origin,float radius)
        {
            for(int i=0;i<64;i++)
            {
                float a=i*Mathf.PI*2/64;
                ring.SetPosition(i,origin+new Vector3(Mathf.Cos(a)*radius,-.8f,Mathf.Sin(a)*radius));
            }
        }

        void DamageFromBotMech(int slot,Vector3 origin)
        {
            var damaged=new HashSet<IHittable>();
            foreach(var collider in Physics.OverlapSphere(origin,6,~0,QueryTriggerInteraction.Ignore))
            {
                if(!TryBotCombatant(collider,out var id) || !BotCanAttack(BotId(slot),id))continue;
                var target=collider.GetComponentInParent<IHittable>();
                if(target==null || damaged.Contains(target))continue;
                Vector3 point=collider.ClosestPoint(origin),delta=point-origin;
                float distance=delta.magnitude;
                if(distance>.001f && NinjaShurikenFlight.FirstHit(origin,delta/distance,distance+.02f,
                    botViews[slot]!=null?botViews[slot].transform:transform,out var obstruction) &&
                    obstruction.collider.GetComponentInParent<IHittable>()!=target)continue;
                damaged.Add(target);
                target.OnHit(new HitInfo{amount=Mathf.Lerp(250,40,Mathf.InverseLerp(1.5f,6,distance)),
                    attackerId=BotId(slot),hitPoint=point,hitNormal=-delta.normalized,impactForce=delta.normalized*25});
            }
        }
    }
}
