using UnityEngine;
namespace Blocks.Gameplay.Shooter
{
    public static class DvaPistolScale
    {
        public const float WorldSize = .32f;
        public static void Normalize(Transform gun)
        {
            bool found=false; Bounds bounds=default;
            foreach(var mesh in gun.GetComponentsInChildren<MeshFilter>(true))
            {
                if(!mesh.sharedMesh)continue;
                var b=mesh.sharedMesh.bounds;
                for(int i=0;i<8;i++)
                {
                    var p=mesh.transform.TransformPoint(b.center+Vector3.Scale(b.extents,new Vector3((i&1)==0?-1:1,(i&2)==0?-1:1,(i&4)==0?-1:1)));
                    if(!found){bounds=new Bounds(p,Vector3.zero);found=true;}else bounds.Encapsulate(p);
                }
            }
            if(found && bounds.size.magnitude>.0001f)gun.localScale*=WorldSize/bounds.size.magnitude;
        }
    }
}
