#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine.SceneManagement;
using UnityEngine;
using Object=UnityEngine.Object;
namespace VFXForge.Editor
{
    public sealed class ForgeBuild : IDisposable
    {
        public GameObject root;
        public readonly List<Object> temporary=new List<Object>();
        public readonly List<string> warnings=new List<string>();
        public string folder; public Scene isolation;
        readonly Dictionary<string,Texture2D> textures=new Dictionary<string,Texture2D>();
        public T Asset<T>(T asset,string name) where T:Object
        {
            if(string.IsNullOrEmpty(folder))temporary.Add(asset);
            else AssetDatabase.CreateAsset(asset,folder+"/"+name+".asset");
            return asset;
        }
        public Texture2D Texture(bool smoke,bool strip=false)
        {
            string key=strip?"SoftStrip":smoke?"CloudNoise":"SoftGlow";if(textures.TryGetValue(key,out var old))return old;
            var texture=new Texture2D(128,128,TextureFormat.RGBA32,false){name=key,wrapMode=TextureWrapMode.Clamp};
            var pixels=new Color[128*128];
            for(int y=0;y<128;y++)for(int x=0;x<128;x++)
            {
                float u=(x+.5f)/128,v=(y+.5f)/128,r=Vector2.Distance(new Vector2(u,v),new Vector2(.5f,.5f))*2;
                float a=Mathf.Pow(Mathf.Clamp01(1-r),smoke?1.2f:2.6f);
                if(strip)a=Mathf.Pow(Mathf.Sin(v*Mathf.PI),.7f);
                if(smoke)a*=.25f+.75f*Mathf.PerlinNoise(u*9+1.7f,v*9+5.3f);
                pixels[y*128+x]=new Color(1,1,1,a);
            }
            texture.SetPixels(pixels);texture.Apply();textures[key]=Asset(texture,key);return textures[key];
        }
        public void Dispose(){if(root)Object.DestroyImmediate(root);foreach(var o in temporary)if(o)Object.DestroyImmediate(o);temporary.Clear();if(isolation.IsValid())EditorSceneManager.ClosePreviewScene(isolation);}
    }
    public static class ForgeBuilder
    {
        public const string Output="Assets/VFXForge/Generated";
        public static ForgeBuild Build(ForgeRecipe recipe,string folder=null)
        {
            if(!recipe)throw new ArgumentException("Choose a VFX recipe.");
            var b=new ForgeBuild{folder=folder};
            try
            {
                b.root=new GameObject(string.IsNullOrWhiteSpace(recipe.effectName)?"Forged VFX":recipe.effectName);
                b.isolation=EditorSceneManager.NewPreviewScene();SceneManager.MoveGameObjectToScene(b.root,b.isolation);
                b.root.transform.localScale=Vector3.one*Mathf.Clamp(recipe.size,.1f,10);
                var playback=b.root.AddComponent<ForgePlayback>();playback.duration=Mathf.Max(.1f,recipe.duration);playback.loop=recipe.loop;
                int index=0;
                foreach(var layer in recipe.layers)
                {
                    int id=index++;if(!layer.enabled)continue;
                    if(layer.shape==ForgeShape.SourcePrefab&&!layer.sourcePrefab){b.warnings.Add(layer.name+": no source prefab selected.");continue;}
                    var go=layer.shape==ForgeShape.SourcePrefab?Object.Instantiate(layer.sourcePrefab):new GameObject(layer.name);
                    go.name=layer.name;go.transform.SetParent(b.root.transform,false);go.transform.localPosition=layer.offset;go.transform.localRotation=Quaternion.Euler(layer.rotation);go.transform.localScale=Vector3.one*Mathf.Max(.01f,layer.scale);
                    if(layer.shape==ForgeShape.SourcePrefab)
                    {
                        var particles=go.GetComponentsInChildren<ParticleSystem>(true);
                        foreach(var ps in particles){ps.Stop(true,ParticleSystemStopBehavior.StopEmittingAndClear);var main=ps.main;main.startDelayMultiplier+=Mathf.Max(0,layer.delay);main.loop=recipe.loop;main.playOnAwake=true;main.stopAction=ParticleSystemStopAction.None;if(layer.replaceSourceMaterials){main.startColor=Color.white;var over=ps.colorOverLifetime;if(over.enabled){var old=over.color;var recolored=new Gradient();recolored.SetKeys(new[]{new GradientColorKey(Color.white,0),new GradientColorKey(Color.white,1)},old.mode==ParticleSystemGradientMode.Gradient&&old.gradient!=null?old.gradient.alphaKeys:new[]{new GradientAlphaKey(1,0),new GradientAlphaKey(0,1)});over.color=recolored;}var speedColor=ps.colorBySpeed;speedColor.enabled=false;}ps.useAutoRandomSeed=false;ps.randomSeed=(uint)(recipe.seed+id+1);}
                        if(layer.replaceSourceMaterials){int sourceIndex=0;foreach(var renderer in go.GetComponentsInChildren<ParticleSystemRenderer>(true))renderer.sharedMaterials=renderer.sharedMaterials.Select(original=>Material(recipe,layer,b,10000+id*1000+sourceIndex++,original?original.mainTexture:null)).ToArray();}
                        foreach(var renderer in go.GetComponentsInChildren<Renderer>(true))foreach(var material in renderer.sharedMaterials)if(!material||!material.shader||!material.shader.isSupported)b.warnings.Add(layer.name+": unsupported or missing source material.");
                        if(particles.Length==0)b.warnings.Add(layer.name+": source contains no ParticleSystem; preview supports ParticleSystem and Forge layers.");
                        continue;
                    }
                    var mat=Material(recipe,layer,b,id);
                    if(layer.shape==ForgeShape.Sparks||layer.shape==ForgeShape.Smoke)
                    {
                        var ps=go.AddComponent<ParticleSystem>();ps.Stop(true,ParticleSystemStopBehavior.StopEmittingAndClear);
                        var main=ps.main;main.duration=Mathf.Max(.1f,recipe.duration);main.loop=recipe.loop;main.playOnAwake=true;main.startDelay=Mathf.Max(0,layer.delay);main.startLifetime=Mathf.Max(.05f,layer.duration);main.startSpeed=Mathf.Max(0,layer.speed);main.startSize=Mathf.Max(.001f,layer.particleSize);main.startColor=Color.white;main.maxParticles=Mathf.Clamp(layer.count*3,32,3000);main.simulationSpace=ParticleSystemSimulationSpace.Local;
                        ps.useAutoRandomSeed=false;ps.randomSeed=(uint)Mathf.Max(1,recipe.seed+id);
                        var emission=ps.emission;emission.rateOverTime=recipe.loop?Mathf.Clamp(layer.count,1,1000)/Mathf.Max(.1f,recipe.duration):0;emission.SetBursts(new[]{new ParticleSystem.Burst(0,(short)Mathf.Clamp(layer.count,1,1000))});
                        var shape=ps.shape;shape.shapeType=layer.emissionShape==ForgeEmission.Cone?ParticleSystemShapeType.Cone:layer.emissionShape==ForgeEmission.Box?ParticleSystemShapeType.Box:ParticleSystemShapeType.Sphere;shape.radius=Mathf.Max(.001f,layer.radius);shape.angle=Mathf.Clamp(layer.spreadAngle,0,90);
                        var color=ps.colorOverLifetime;color.enabled=true;var gradient=new Gradient();gradient.SetKeys(new[]{new GradientColorKey(Color.white,0),new GradientColorKey(Color.white,1)},new[]{new GradientAlphaKey(0,0),new GradientAlphaKey(1,.08f),new GradientAlphaKey(.5f,.55f),new GradientAlphaKey(0,1)});color.color=gradient;
                        if(layer.shape==ForgeShape.Smoke){var size=ps.sizeOverLifetime;size.enabled=true;size.size=new ParticleSystem.MinMaxCurve(1,AnimationCurve.Linear(0,.3f,1,2));}
                        var renderer=ps.GetComponent<ParticleSystemRenderer>();renderer.sharedMaterial=mat;renderer.renderMode=layer.shape==ForgeShape.Sparks?ParticleSystemRenderMode.Stretch:ParticleSystemRenderMode.Billboard;renderer.lengthScale=2;renderer.velocityScale=.08f;
                    }
                    else
                    {
                        var filter=go.AddComponent<MeshFilter>();filter.sharedMesh=b.Asset(Mesh(layer.shape),"Shape_"+id);
                        var renderer=go.AddComponent<MeshRenderer>();renderer.sharedMaterial=mat;renderer.shadowCastingMode=UnityEngine.Rendering.ShadowCastingMode.Off;renderer.receiveShadows=false;
                        var pulse=go.AddComponent<ForgePulse>();pulse.delay=Mathf.Max(0,layer.delay);pulse.duration=Mathf.Max(.05f,layer.duration);pulse.from=Vector3.one*Mathf.Max(.01f,layer.scale)*.15f;pulse.to=Vector3.one*Mathf.Max(.01f,layer.scale)*(layer.shape==ForgeShape.Glow?1:2.8f);pulse.color=mat.HasProperty("_Tint")?mat.GetColor("_Tint"):Color.white;
                    }
                }
                return b;
            }
            catch{b.Dispose();throw;}
        }
        static Material Material(ForgeRecipe recipe,ForgeLayer layer,ForgeBuild b,int id,Texture sourceTexture=null)
        {
            Shader shader=layer.shader?layer.shader:Shader.Find(layer.shape==ForgeShape.Smoke?"VFX Forge/Alpha URP":"VFX Forge/Additive URP");
            if(!shader)throw new InvalidOperationException("VFX Forge shader is not imported yet.");
            var m=layer.material?new Material(layer.material):new Material(shader);m.name=layer.name;
            var color=layer.color;color.r*=recipe.brightness;color.g*=recipe.brightness;color.b*=recipe.brightness;
            var texture=layer.texture?layer.texture:layer.material&&layer.material.mainTexture?layer.material.mainTexture:sourceTexture?sourceTexture:b.Texture(layer.shape==ForgeShape.Smoke,layer.shape==ForgeShape.Ring||layer.shape==ForgeShape.Slash);
            foreach(string key in new[]{"_Tint","_BaseColor","_Color"})if(m.HasProperty(key))m.SetColor(key,color);
            foreach(string key in new[]{"_MainTex","_BaseMap"})if(m.HasProperty(key))m.SetTexture(key,texture);
            if(!m.shader.isSupported)b.warnings.Add(layer.name+": selected shader is unsupported.");
            return b.Asset(m,"Material_"+id);
        }
        static Mesh Mesh(ForgeShape shape)
        {
            var mesh=new Mesh{name="Forge "+shape};
            if(shape==ForgeShape.Glow)
            {
                mesh.vertices=new[]{new Vector3(-1,0,-1),new Vector3(1,0,-1),new Vector3(1,0,1),new Vector3(-1,0,1)};mesh.uv=new[]{Vector2.zero,Vector2.right,Vector2.one,Vector2.up};mesh.triangles=new[]{0,2,1,0,3,2};
            }
            else
            {
                int segments=shape==ForgeShape.Slash?48:96;float sweep=shape==ForgeShape.Slash?Mathf.PI*1.2f:Mathf.PI*2;
                var vertices=new Vector3[(segments+1)*2];var uv=new Vector2[vertices.Length];var triangles=new int[segments*6];
                for(int i=0;i<=segments;i++){float u=i/(float)segments,a=u*sweep;for(int j=0;j<2;j++){float radius=j==0?.82f:1;vertices[i*2+j]=new Vector3(Mathf.Cos(a)*radius,0,Mathf.Sin(a)*radius);uv[i*2+j]=new Vector2(u,j);}}
                for(int i=0;i<segments;i++){int k=i*6,v=i*2;triangles[k]=v;triangles[k+1]=v+2;triangles[k+2]=v+1;triangles[k+3]=v+1;triangles[k+4]=v+2;triangles[k+5]=v+3;}
                mesh.vertices=vertices;mesh.uv=uv;mesh.triangles=triangles;
            }
            mesh.colors=Enumerable.Repeat(Color.white,mesh.vertexCount).ToArray();mesh.RecalculateBounds();mesh.RecalculateNormals();return mesh;
        }
        public static string Save(ForgeRecipe recipe)
        {
            if(!AssetDatabase.IsValidFolder(Output))System.IO.Directory.CreateDirectory(Output);
            string safe=new string(recipe.effectName.Where(c=>char.IsLetterOrDigit(c)||c=='_'||c=='-'||c==' ').ToArray());if(string.IsNullOrWhiteSpace(safe))safe="Forged VFX";
            string folder=AssetDatabase.GenerateUniqueAssetPath(Output+"/"+safe);System.IO.Directory.CreateDirectory(folder);AssetDatabase.Refresh();
            using(var b=Build(recipe,folder))
            {
                string path=folder+"/"+safe+".prefab";PrefabUtility.SaveAsPrefabAsset(b.root,path);
                var copy=Object.Instantiate(recipe);AssetDatabase.CreateAsset(copy,folder+"/Recipe.asset");
                System.IO.File.WriteAllLines(folder+"/Sources.txt",recipe.layers.Where(l=>l.sourcePrefab).Select(l=>AssetDatabase.GetAssetPath(l.sourcePrefab)).Distinct().Prepend("Sources remain unchanged. Original asset licenses apply. Procedural Forge textures and shaders are generated locally."));
                AssetDatabase.SaveAssets();AssetDatabase.Refresh();return path;
            }
        }
        public static void Sample(GameObject root,float time)
        {
            foreach(var ps in root.GetComponentsInChildren<ParticleSystem>(true))ps.Simulate(time,false,true,true);
            root.GetComponent<ForgePlayback>().Sample(time);
        }
    }
}
#endif






