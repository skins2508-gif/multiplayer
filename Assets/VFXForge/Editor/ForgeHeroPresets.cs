#if UNITY_EDITOR
using System;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;
namespace VFXForge.Editor
{
    public static class ForgeHeroPresets
    {
        static bool previewsOnly;
        public static readonly string[] Labels={"겐지 검격","디바 자폭","윈스턴 폭주","캐서디 연사","텔레포트 소환","메르시 치유","겐지 대시","디바 재탑승"};
        public static ForgeRecipe Create(int index)
        {
            var r=ScriptableObject.CreateInstance<ForgeRecipe>();r.effectName=new[]{"Genji Blade Combo","Dva Self Destruct","Winston Primal Rage","Cassidy Rapid Fire","Hero Teleport Summon","Mercy Healing Aura","Genji Swift Dash","Dva Mech Recall"}[index];r.duration=new[]{1.2f,4.5f,3.5f,2.1f,3.6f,3.5f,.9f,2.8f}[index];r.brightness=2;
            Color green=new Color(.15f,1,.06f,1),pink=new Color(1,.1f,.48f,1),gold=new Color(1,.62f,.05f,1),blue=new Color(.05f,.65f,1,1),electric=new Color(.25f,.48f,1,1);
            ForgeLayer Add(string name,ForgeShape shape,Color color,float scale,float delay,float duration,int count=1,float speed=0,float size=.1f,string texture=null,Vector3 offset=default,Vector3 rotation=default)
            {
                var l=new ForgeLayer{name=name,shape=shape,color=color,scale=scale,delay=delay,duration=duration,count=count,speed=speed,particleSize=size,offset=offset,rotation=rotation};
                if(texture!=null)l.texture=AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/VFXForge/ThirdParty/Kenney/Textures/"+texture+".png");r.layers.Add(l);return l;
            }
            void Source(string file,Color color,float scale,float delay=0)
            {
                var prefab=AssetDatabase.LoadAssetAtPath<GameObject>("Assets/GabrielAguiarProductions/FreeQuickEffectsVol1/Prefabs/"+file+".prefab");
                if(prefab)r.layers.Add(new ForgeLayer{name="Free VFX • "+file,shape=ForgeShape.SourcePrefab,sourcePrefab=prefab,replaceSourceMaterials=true,color=color,scale=scale,delay=delay,duration=r.duration-delay});
            }
            if(index==0)
            {
                Add("First diagonal blade",ForgeShape.Slash,green,1.1f,0,.24f,rotation:new Vector3(35,0,35));
                Add("Second reverse blade",ForgeShape.Slash,green,1.3f,.28f,.26f,rotation:new Vector3(-25,180,-40));
                Add("Blade impact star",ForgeShape.Glow,green,1,.14f,.2f,texture:"star_04",offset:Vector3.forward*.8f,rotation:new Vector3(90,0,0));
                Add("Cut sparks",ForgeShape.Sparks,green,1,.14f,.55f,65,5,.065f,"spark_03",Vector3.forward*.8f);
            }
            if(index==1)
            {
                for(int i=0;i<3;i++)Add("Warning pulse "+i,ForgeShape.Ring,pink,1+i*.35f,i*.55f,.42f,rotation:new Vector3(90,0,0));
                Add("Detonation flash",ForgeShape.Glow,new Color(1,.87f,.5f),9,1.8f,.25f,texture:"light_01",rotation:new Vector3(90,0,0));
                Add("Main shockwave",ForgeShape.Ring,gold,5,1.8f,1.2f);
                Add("Pink outer shockwave",ForgeShape.Ring,pink,4.5f,1.9f,1.4f,rotation:new Vector3(15,0,0));
                Add("Flying embers",ForgeShape.Sparks,gold,1,1.8f,1.8f,260,13,.16f,"spark_05");
                Add("Blast smoke",ForgeShape.Smoke,new Color(.4f,.32f,.4f,.7f),1,1.9f,2.5f,70,3,2.2f,"smoke_01");Source("vfx_Explosion_02",pink,2,1.8f);
            }
            if(index==2)
            {
                Source("vfx_Electricity_01",electric,1.6f);
                Add("Nano activation",ForgeShape.Glow,electric,2,0,.55f,texture:"magic_03",offset:Vector3.up,rotation:new Vector3(90,0,0));
                Add("Rage ground pulse",ForgeShape.Ring,gold,2.2f,.15f,1.1f);
                for(int i=0;i<4;i++){Add("Fist shock "+i,ForgeShape.Ring,gold,1.7f,.7f+i*.55f,.5f,offset:new Vector3(i%2==0?-.8f:.8f,.15f,1));Add("Fist sparks "+i,ForgeShape.Sparks,gold,1,.7f+i*.55f,.5f,35,4,.09f,"spark_01",new Vector3(i%2==0?-.8f:.8f,.2f,1));}
                Add("Rage dust",ForgeShape.Smoke,new Color(.5f,.35f,.17f,.35f),1,0,3.1f,45,1.4f,.8f,"smoke_04");
            }
            if(index==3)
            {
                for(int i=0;i<6;i++)
                {
                    float t=i*.22f;
                    Add("Muzzle flash "+i,ForgeShape.Glow,gold,.5f,t,.085f,texture:"muzzle_02",rotation:new Vector3(90,0,i%2==0?0:30));
                    var tracer=Add("Shot tracer "+i,ForgeShape.Sparks,gold,1,t,.16f,3,38,.065f,"trace_01");tracer.emissionShape=ForgeEmission.Cone;tracer.spreadAngle=1.5f;tracer.radius=.015f;
                    var shell=Add("Side casing glint "+i,ForgeShape.Sparks,gold,1,t,.35f,1,3,.06f,"spark_01",new Vector3(.12f,0,-.1f),new Vector3(0,75,15));shell.emissionShape=ForgeEmission.Cone;shell.spreadAngle=10;
                }
                Add("Muzzle smoke",ForgeShape.Smoke,new Color(.45f,.4f,.3f,.22f),1,.15f,1.7f,12,.5f,.2f,"smoke_03");
            }
            if(index==4)
            {
                Add("Ground anchoring halo",ForgeShape.Ring,blue,2,0,2.7f);
                Add("Gate outline",ForgeShape.Ring,blue,2.1f,.3f,2.7f,offset:Vector3.up*1.9f,rotation:new Vector3(90,0,0));
                Add("Gate inner swirl",ForgeShape.Glow,blue,3,.35f,2.6f,texture:"twirl_01",offset:Vector3.up*1.9f,rotation:new Vector3(90,0,0));
                Add("Warp motes",ForgeShape.Sparks,blue,1,.2f,1.9f,180,2,.08f,"star_01",Vector3.up*1.2f);
                Add("Summon completion flash",ForgeShape.Glow,new Color(.7f,1,1),3,1.9f,.3f,texture:"light_01",offset:Vector3.up*1.5f,rotation:new Vector3(90,0,0));
                Add("Arrival shockwave",ForgeShape.Ring,blue,3,1.95f,1.3f);Source("vfx_Implosion_01",blue,1.2f);
            }
            if(index==5)
            {
                Add("Healing floor light",ForgeShape.Glow,new Color(1,.7f,.18f,.22f),3,0,3.5f,texture:"light_01");
                Add("Soft healing halo",ForgeShape.Ring,new Color(1,.7f,.18f,.35f),2,0,3.5f);
                var motes=Add("Surrounding gold motes",ForgeShape.Sparks,gold,1,0,3.4f,110,.35f,.07f,"star_04",Vector3.up);motes.radius=1.8f;
            }
            if(index==6)
            {
                var trail=Add("Directed swift streaks",ForgeShape.Sparks,green,1,0,.45f,90,18,.07f,"trace_03",rotation:new Vector3(0,180,0));trail.emissionShape=ForgeEmission.Cone;trail.spreadAngle=8;trail.radius=.3f;
                Add("Dash edge",ForgeShape.Slash,green,.8f,0,.22f,rotation:new Vector3(90,0,0));Add("Foot dust",ForgeShape.Smoke,new Color(.35f,.4f,.3f,.25f),1,0,.8f,18,1.2f,.5f,"smoke_05");
            }
            if(index==7)
            {
                Add("Recall landing marker",ForgeShape.Ring,pink,1.8f,0,2.4f);
                var jets=Add("Descending booster trail",ForgeShape.Sparks,new Color(.1f,1,.7f),1,0,1.8f,130,5,.14f,"trace_02",new Vector3(0,3,0),new Vector3(90,0,0));jets.emissionShape=ForgeEmission.Cone;jets.spreadAngle=6;
                Add("Touchdown wave",ForgeShape.Ring,pink,2.5f,1.6f,.9f);Add("Landing dust",ForgeShape.Smoke,new Color(.5f,.4f,.45f,.4f),1,1.6f,1.1f,40,3,.8f,"smoke_02");
            }
            return r;
        }
        [MenuItem("Tools/VFX Forge/영웅 효과 8종 제작")]
        public static void SaveAll()
        {
            Directory.CreateDirectory("ArtSource/VFXForge/HeroEffects");var report=new StringBuilder();
            try
            {
                for(int i=0;i<Labels.Length;i++)
                {
                    var recipe=Create(i);
                    try
                    {
                        string path=previewsOnly?(Directory.GetDirectories(ForgeBuilder.Output,recipe.effectName+"*").OrderByDescending(Directory.GetLastWriteTimeUtc).First()+"/"+recipe.effectName+".prefab").Replace(Path.DirectorySeparatorChar,'/'):ForgeBuilder.Save(recipe);
                        var prefab=AssetDatabase.LoadAssetAtPath<GameObject>(path);if(!prefab)throw new Exception("Missing generated prefab: "+path);
                        using(var b=ForgeBuilder.Build(recipe))
                        {
                            var preview=new PreviewRenderUtility();try
                            {
                                preview.AddSingleGO(b.root);preview.camera.clearFlags=CameraClearFlags.SolidColor;preview.camera.backgroundColor=new Color(.018f,.026f,.04f);preview.camera.fieldOfView=55;preview.camera.nearClipPlane=.02f;preview.camera.farClipPlane=200;
                                preview.camera.transform.position=i==1?new Vector3(22,16,-34):i==4?new Vector3(13,9,-21):i==3?new Vector3(3,2,-6):new Vector3(8,5,-12);preview.camera.transform.LookAt(i==4?Vector3.up:i==3?Vector3.forward*1.5f:Vector3.zero);
                                foreach(float t in new[]{0f,.15f,.7f,1.9f,2.5f,3.5f,0f})ForgeBuilder.Sample(b.root,t);
                                ForgeBuilder.Sample(b.root,i==1?2.25f:i==4?2.15f:i==3?.48f:i==0?.19f:.65f);
                                preview.BeginStaticPreview(new Rect(0,0,800,600));preview.Render(true);var image=preview.EndStaticPreview();File.WriteAllBytes("ArtSource/VFXForge/HeroEffects/"+recipe.effectName.Replace(' ','_')+".png",image.EncodeToPNG());UnityEngine.Object.DestroyImmediate(image);
                            }
                            finally{preview.Cleanup();b.root=null;}
                        }
                        report.AppendLine("PASS: "+Labels[i]+" | "+path);
                    }
                    finally{UnityEngine.Object.DestroyImmediate(recipe);}
                }
                report.AppendLine("PASS: eight hero effects saved and previewed with deterministic time scrubbing.");
            }
            catch(Exception e){report.AppendLine("FAIL: "+e);Debug.LogException(e);}
            File.WriteAllText("ArtSource/VFXForge/HeroEffects/Verification.txt",report.ToString());
        }
        [InitializeOnLoadMethod]
        static void Install(){EditorApplication.update+=Tick;}
        static void Tick(){if(!EditorApplication.isCompiling&&!EditorApplication.isUpdating&&!EditorApplication.isPlayingOrWillChangePlaymode&&File.Exists("Temp/VFXForgeHeroes.request")){previewsOnly=File.ReadAllText("Temp/VFXForgeHeroes.request").Trim()=="preview";File.Delete("Temp/VFXForgeHeroes.request");SaveAll();previewsOnly=false;}}
    }
}
#endif



