using UnityEngine;
namespace VFXForge
{
    public sealed class ForgePlayback : MonoBehaviour
    {
        public float duration=2;
        public bool loop;
        public bool destroyWhenFinished=true;
        ForgePulse[] pulses;
        float started;
        void OnEnable(){started=Time.time;pulses=GetComponentsInChildren<ForgePulse>(true);}
        void Update()
        {
            float t=Time.time-started;
            if(loop)t%=Mathf.Max(.1f,duration);
            Sample(t);
            if(!loop&&destroyWhenFinished&&t>duration)Destroy(gameObject);
        }
        public void Sample(float time)
        {
            if(pulses==null)pulses=GetComponentsInChildren<ForgePulse>(true);
            foreach(var pulse in pulses)if(pulse)pulse.Sample(time);
        }
    }
}
