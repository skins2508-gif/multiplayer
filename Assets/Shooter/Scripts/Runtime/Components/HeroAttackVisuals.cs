using UnityEngine;
using UnityEngine.Rendering;
namespace Blocks.Gameplay.Shooter
{
    // Local cosmetics only. Existing shot RPCs and projectiles retain damage authority.
    public static class HeroAttackVisuals
    {
        static Material Make(Color color)
        {
            var shader=Shader.Find("Universal Render Pipeline/Unlit");
            if(!shader)return null;
            var m=new Material(shader);m.SetColor("_BaseColor",color);return m;
        }
        public static void Flash(Vector3 position,Vector3 direction,Color color)
        {
            var material=Make(color);if(!material)return;
            var go=GameObject.CreatePrimitive(PrimitiveType.Sphere);go.name="Hero muzzle flash";
            go.GetComponent<Collider>().enabled=false;
            go.transform.position=position;
            if(direction.sqrMagnitude>.001f)go.transform.rotation=Quaternion.LookRotation(direction);
            go.transform.localScale=new Vector3(.1f,.1f,.23f);
            var r=go.GetComponent<Renderer>();r.sharedMaterial=material;r.shadowCastingMode=ShadowCastingMode.Off;
            Object.Destroy(go,.09f);Object.Destroy(material,.12f);
        }
        public static void Trail(GameObject projectile,Color color)
        {
            var material=Make(color);if(!material)return;
            var trail=projectile.AddComponent<TrailRenderer>();trail.sharedMaterial=material;
            trail.time=.10f;trail.startWidth=.035f;trail.endWidth=0;trail.minVertexDistance=.035f;
            trail.shadowCastingMode=ShadowCastingMode.Off;trail.receiveShadows=false;
            projectile.AddComponent<HeroAttackMaterialCleanup>().material=material;
        }
        public static void Slash(Transform attacker)
        {
            var material=Make(new Color(.35f,1,.55f));if(!material)return;
            var go=new GameObject("Genji slash arc");go.transform.SetParent(attacker,false);
            var line=go.AddComponent<LineRenderer>();line.useWorldSpace=false;line.sharedMaterial=material;
            line.positionCount=18;line.startWidth=.045f;line.endWidth=.008f;
            line.shadowCastingMode=ShadowCastingMode.Off;
            for(int i=0;i<18;i++){float a=Mathf.Lerp(-1.1f,1.1f,i/17f);line.SetPosition(i,new Vector3(Mathf.Sin(a)*1.1f,1.3f+Mathf.Sin(a)*.25f,Mathf.Cos(a)*1.1f));}
            Object.Destroy(go,.16f);Object.Destroy(material,.19f);
        }
    }
    public sealed class HeroAttackMaterialCleanup:MonoBehaviour
    {
        public Material material;
        void OnDestroy(){if(material)Destroy(material);}
    }
}
