using UnityEngine;

namespace Blocks.Gameplay.Shooter
{
    // Presentation only: the network match remains the source of the outcome.
    public sealed class BotanicalMatchResult
    {
        float started = -1;
        Texture2D left, right, petal;
        bool loadedVictory;
        public void Reset() { started = -1; }
        public void Draw(bool won)
        {
            if (started < 0) started = Time.unscaledTime;
            if (!left || loadedVictory != won)
            {
                loadedVictory = won;
                left = Resources.Load<Texture2D>("MatchResult/" + (won ? "Victory" : "Defeat") + "Left");
                right = Resources.Load<Texture2D>("MatchResult/" + (won ? "Victory" : "Defeat") + "Right");
            }

            if (!petal) petal = Resources.Load<Texture2D>("MatchResult/Petal");
            float t = Time.unscaledTime - started;
            float reveal = Mathf.SmoothStep(0, 1, Mathf.Clamp01(t / 1.6f));
            var matrix = GUI.matrix; var color = GUI.color;
            float scale = Mathf.Min(Screen.width / 1280f, Screen.height / 720f);
            GUI.matrix = Matrix4x4.TRS(new Vector3(Screen.width/2f, Screen.height/2f, 0), Quaternion.identity, Vector3.one*scale);
            GUI.color = new Color(.015f,.018f,.05f,.65f * reveal);
            GUI.DrawTexture(new Rect(-Screen.width/scale/2,-Screen.height/scale/2,Screen.width/scale,Screen.height/scale), Texture2D.whiteTexture);
            GUI.color = new Color(1,1,1,reveal);
            var center = GUI.matrix;
            float spread = Mathf.Lerp(.3f,1,reveal);
            for(int side=0;side<2;side++)
            {
                float sign=side==0?-1:1;
                GUI.matrix=center;
                GUIUtility.RotateAroundPivot(sign*((1-reveal)*(won?32:-18) + Mathf.Sin(t*1.3f)*(won?1.3f:.5f)), Vector2.zero);
                var texture=side==0?left:right;
                if(texture) GUI.DrawTexture(new Rect(side==0?-420*spread:0,-245,420*spread,490),texture,ScaleMode.StretchToFill,true);
            }
            GUI.matrix=center;
            float titleAlpha=Mathf.Clamp01((t-.65f)/.7f);
            var title=new GUIStyle(GUI.skin.label){fontSize=76,fontStyle=FontStyle.Bold,alignment=TextAnchor.MiddleCenter};
            title.normal.textColor=won?new Color(1,.85f,.45f,titleAlpha):new Color(.72f,.78f,1,titleAlpha);
            GUI.color=Color.white;
            GUI.Label(new Rect(-240,-72,480,120),won?"승리":"패배",title);
            var sub=new GUIStyle(GUI.skin.label){fontSize=17,alignment=TextAnchor.MiddleCenter};
            sub.normal.textColor=new Color(.85f,.87f,.95f,titleAlpha);
            GUI.Label(new Rect(-240,36,480,40),won?"VICTORY":"DEFEAT",sub);
            if(petal) for(int i=0;i<28;i++)
            {
                float age=(t*.14f+i*.137f)%1;
                float x=Mathf.Sin(i*12.31f)*490+Mathf.Sin(t*1.2f+i)*38;
                float y=-290+age*610;
                GUI.matrix=center;
                GUIUtility.RotateAroundPivot(t*(i%2==0?45:-35)+i*31,new Vector2(x,y));
                GUI.color=new Color(won?1:.6f,won?.65f:.68f,1,reveal*Mathf.Sin(age*Mathf.PI)*.8f);
                float size=12+i%4*5;
                GUI.DrawTexture(new Rect(x,y,size,size),petal);
            }
            GUI.matrix=matrix; GUI.color=color;
        }
    }
}
