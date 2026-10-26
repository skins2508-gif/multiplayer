using UnityEngine;

namespace Blocks.Gameplay.Shooter
{
    public partial class WeaponController
    {
        private GUIStyle m_DeadeyeLabel;

        private void DrawDeadeyeReticle(Vector2 center, float progress, bool ready)
        {
            if(Event.current.type!=EventType.Repaint)return;
            float scale=Mathf.Clamp(Screen.height/1080f,.65f,2f);
            float radius=Mathf.Lerp(46,18,progress)*scale;
            Color oldColor=GUI.color;
            Matrix4x4 oldMatrix=GUI.matrix;
            Color color=ready ? new Color(1,.18f,.12f) : new Color(1,.76f,.23f);
            try
            {
                // A shrinking ring shows damage charging; a complete red cross marks a lethal shot.
                for(int i=0;i<64;i++)
                {
                    float a=i*Mathf.PI*2/64-Mathf.PI*.5f;
                    float b=(i+1)*Mathf.PI*2/64-Mathf.PI*.5f;
                    GUI.color=i/64f<=progress ? color : new Color(0,0,0,.55f);
                    DeadeyeLine(center+new Vector2(Mathf.Cos(a),Mathf.Sin(a))*radius,
                        center+new Vector2(Mathf.Cos(b),Mathf.Sin(b))*radius,2.5f*scale);
                }
                GUI.color=color;
                for(int i=0;i<4;i++)
                {
                    float angle=i*Mathf.PI*.5f;
                    Vector2 axis=new Vector2(Mathf.Cos(angle),Mathf.Sin(angle));
                    DeadeyeLine(center+axis*(radius+4*scale),center+axis*(radius+12*scale),2*scale);
                }
                if(ready)
                {
                    DeadeyeLine(center-new Vector2(6,6)*scale,center+new Vector2(6,6)*scale,2.5f*scale);
                    DeadeyeLine(center-new Vector2(-6,6)*scale,center+new Vector2(-6,6)*scale,2.5f*scale);
                }
                else GUI.DrawTexture(new Rect(center.x-2*scale,center.y-2*scale,4*scale,4*scale),Texture2D.whiteTexture);
                if(m_DeadeyeLabel==null)m_DeadeyeLabel=new GUIStyle(GUI.skin.label){alignment=TextAnchor.MiddleCenter,fontStyle=FontStyle.Bold};
                m_DeadeyeLabel.fontSize=Mathf.RoundToInt(14*scale);
                m_DeadeyeLabel.normal.textColor=color;
                GUI.color=Color.white;
                GUI.Label(new Rect(center.x-65*scale,center.y+radius+14*scale,130*scale,24*scale),
                    ready ? "조준 완료" : $"조준 중 {progress*100:0}%",m_DeadeyeLabel);
            }
            finally {GUI.color=oldColor;GUI.matrix=oldMatrix;}
        }

        private static void DeadeyeLine(Vector2 start,Vector2 end,float width)
        {
            Matrix4x4 matrix=GUI.matrix;
            Vector2 delta=end-start;
            GUIUtility.RotateAroundPivot(Mathf.Atan2(delta.y,delta.x)*Mathf.Rad2Deg,start);
            GUI.DrawTexture(new Rect(start.x,start.y-width*.5f,delta.magnitude,width),Texture2D.whiteTexture);
            GUI.matrix=matrix;
        }
    }
}
