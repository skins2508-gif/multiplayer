using UnityEngine;
namespace VFXForge
{
    public sealed class ForgePulse : MonoBehaviour
    {
        public float delay,duration=1;
        public Vector3 from=Vector3.one,to=Vector3.one*3;
        public Color color=Color.cyan;
        Renderer target;
        MaterialPropertyBlock block;
        public void Sample(float time)
        {
            if(!target)target=GetComponent<Renderer>();
            if(!target)return;
            float age=time-delay,u=Mathf.Clamp01(age/Mathf.Max(.05f,duration));
            target.enabled=age>=0&&age<duration;
            transform.localScale=Vector3.Lerp(from,to,Mathf.SmoothStep(0,1,u));
            if(block==null)block=new MaterialPropertyBlock();
            block.SetColor("_Tint",color*Mathf.Sin(u*Mathf.PI));target.SetPropertyBlock(block);
        }
    }
}
