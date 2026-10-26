using UnityEngine;
using UnityEngine.UI;

namespace Blocks.Gameplay.Shooter
{
    /// <summary>Canvas-native rounded panel and inset sci-fi frame, with independent fill opacity.</summary>
    [RequireComponent(typeof(CanvasRenderer))]
    public sealed class SessionPanelGraphic : MaskableGraphic
    {
        public Color borderColor = new Color(.33f,.53f,.69f,1);
        public float borderWidth = 2;
        public float radius = 12;
        public bool insetFrame;
        protected override void OnPopulateMesh(VertexHelper vh)
        {
            vh.Clear();var rect=GetPixelAdjustedRect();
            var outer=Contour(rect,radius);var inner=Contour(Inset(rect,borderWidth),Mathf.Max(0,radius-borderWidth));
            Fill(vh,inner,color);Ring(vh,outer,inner,borderColor);
            if(insetFrame)
            {
                var frame=Inset(rect,8);var inside=Inset(frame,1.5f);
                Ring(vh,Bevel(frame,20),Bevel(inside,19),new Color(.12f,.72f,.94f,.7f));
            }
        }
        static Rect Inset(Rect r,float amount)=>new Rect(r.x+amount,r.y+amount,Mathf.Max(0,r.width-amount*2),Mathf.Max(0,r.height-amount*2));
        static Vector2[] Contour(Rect r,float radius)
        {
            radius=Mathf.Min(radius,Mathf.Min(r.width,r.height)*.5f);
            var result=new Vector2[32];
            Vector2[] centers={new Vector2(r.xMax-radius,r.yMax-radius),new Vector2(r.xMin+radius,r.yMax-radius),new Vector2(r.xMin+radius,r.yMin+radius),new Vector2(r.xMax-radius,r.yMin+radius)};
            for(int corner=0;corner<4;corner++)for(int step=0;step<8;step++)
            {float angle=(corner*90+step*90/7f)*Mathf.Deg2Rad;result[corner*8+step]=centers[corner]+new Vector2(Mathf.Cos(angle),Mathf.Sin(angle))*radius;}
            return result;
        }
        static Vector2[] Bevel(Rect r,float cut)=>new[]{new Vector2(r.xMax,r.yMax-cut),new Vector2(r.xMax-cut,r.yMax),new Vector2(r.xMin+cut,r.yMax),new Vector2(r.xMin,r.yMax-cut),new Vector2(r.xMin,r.yMin+cut),new Vector2(r.xMin+cut,r.yMin),new Vector2(r.xMax-cut,r.yMin),new Vector2(r.xMax,r.yMin+cut)};
        static void Fill(VertexHelper vh,Vector2[] points,Color color)
        {
            int start=vh.currentVertCount;Vector2 center=Vector2.zero;foreach(var p in points)center+=p;center/=points.Length;
            vh.AddVert(center,color,Vector2.zero);foreach(var p in points)vh.AddVert(p,color,Vector2.zero);
            for(int i=0;i<points.Length;i++)vh.AddTriangle(start,start+1+i,start+1+(i+1)%points.Length);
        }
        static void Ring(VertexHelper vh,Vector2[] outer,Vector2[] inner,Color color)
        {
            int start=vh.currentVertCount;
            for(int i=0;i<outer.Length;i++){vh.AddVert(outer[i],color,Vector2.zero);vh.AddVert(inner[i],color,Vector2.zero);}
            for(int i=0;i<outer.Length;i++){int next=(i+1)%outer.Length;vh.AddTriangle(start+i*2,start+next*2,start+i*2+1);vh.AddTriangle(start+i*2+1,start+next*2,start+next*2+1);}
        }
    }
}
