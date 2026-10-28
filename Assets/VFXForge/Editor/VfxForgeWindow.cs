#if UNITY_EDITOR
using System.Linq;
using UnityEditor;
using UnityEngine;
namespace VFXForge.Editor
{
    public static class ForgePresets
    {
        public static readonly string[] Names={"포털 소환","폭발","검기","치유 오라","대시"};
        public static ForgeRecipe Create(int index)
        {
            var r=ScriptableObject.CreateInstance<ForgeRecipe>();r.effectName=new[]{"Portal Summon","Explosion","Sword Slash","Healing Aura","Dash Burst"}[index];r.duration=index==2?.8f:2.5f;
            Color blue=new Color(.05f,.65f,1,1),gold=new Color(1,.65f,.08f,1),green=new Color(.2f,1,.18f,1);var c=index==1?gold:index==2?green:index==3?gold:blue;
            void Add(string name,ForgeShape shape,float scale,float delay,float length,int count,float speed,float size,Vector3 rotation=default)
            {var layer=new ForgeLayer{name=name,shape=shape,color=c,scale=scale,delay=delay,duration=length,count=count,speed=speed,particleSize=size,rotation=rotation};string file=shape==ForgeShape.Smoke?"smoke_01":shape==ForgeShape.Sparks?"spark_03":shape==ForgeShape.Glow?"light_01":null;if(file!=null)layer.texture=AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/VFXForge/ThirdParty/Kenney/Textures/"+file+".png");r.layers.Add(layer);}
            if(index==0){Add("Portal ring",ForgeShape.Ring,1.5f,0,2.5f,1,0,1,new Vector3(90,0,0));Add("Ground wave",ForgeShape.Ring,2,.3f,1.5f,1,0,1);Add("Gathering sparks",ForgeShape.Sparks,1,0,1.5f,100,1.2f,.1f);Add("Portal core",ForgeShape.Glow,2,0,2.5f,1,0,1,new Vector3(90,0,0));}
            if(index==1){Add("Flash",ForgeShape.Glow,3,0,.35f,1,0,1,new Vector3(90,0,0));Add("Blast ring",ForgeShape.Ring,2,0,1,1,0,1);Add("Embers",ForgeShape.Sparks,1,0,1.2f,140,6,.12f);Add("Smoke",ForgeShape.Smoke,1,.1f,2.2f,36,1.3f,1.2f);}
            if(index==2){Add("Sword arc",ForgeShape.Slash,1.5f,0,.3f,1,0,1,new Vector3(25,0,35));Add("Cut sparks",ForgeShape.Sparks,1,.08f,.55f,40,4,.07f);}
            if(index==3){Add("Ground glow",ForgeShape.Glow,3,0,2.5f,1,0,1);Add("Halo",ForgeShape.Ring,1.8f,0,2.5f,1,0,1);Add("Floating motes",ForgeShape.Sparks,1,0,2.4f,90,.7f,.09f);}
            if(index==4){Add("Speed sparks",ForgeShape.Sparks,1,0,.6f,100,7,.1f);Add("Shock ring",ForgeShape.Ring,.8f,0,.5f,1,0,1,new Vector3(90,0,0));Add("Dust",ForgeShape.Smoke,1,0,.8f,18,2,.6f);}
            string source=index==0?"vfx_Implosion_01":index==1?"vfx_Explosion_01":index==3?"vfx_Heal_01":index==4?"vfx_Hyperdrive_01":null;
            if(source!=null)
            {
                var path="Assets/GabrielAguiarProductions/FreeQuickEffectsVol1/Prefabs/"+source+".prefab";var prefab=AssetDatabase.LoadAssetAtPath<GameObject>(path);
                if(prefab)r.layers.Add(new ForgeLayer{name="Free Quick Effects • "+source,shape=ForgeShape.SourcePrefab,sourcePrefab=prefab,color=c,replaceSourceMaterials=true,duration=r.duration});
            }
            return r;
        }
    }
    public sealed class VfxForgeWindow : EditorWindow
    {
        ForgeRecipe recipe;
        SerializedObject serialized;
        ForgeBuild build;
        PreviewRenderUtility preview;
        Vector2 scroll;
        float time,yaw=25,pitch=20,distance=14;
        double previous;
        bool playing=true,dirty=true;
        string sourceSearch="",message="";
        GameObject[] freeSources; Texture2D[] libraryTextures; string textureSearch=""; int textureLayer;
        [MenuItem("Tools/VFX Forge/통합 VFX 제조기")]
        public static void Open(){var w=GetWindow<VfxForgeWindow>();w.titleContent=new GUIContent("VFX 제조기");w.minSize=new Vector2(900,580);w.Show();}
        void OnEnable(){EditorApplication.update+=Tick;previous=EditorApplication.timeSinceStartup;if(!recipe)SetRecipe(ForgePresets.Create(0));}
        void OnDisable(){EditorApplication.update-=Tick;Cleanup();if(recipe&&!AssetDatabase.Contains(recipe))DestroyImmediate(recipe);}
        void SetRecipe(ForgeRecipe value){if(recipe&&!AssetDatabase.Contains(recipe))DestroyImmediate(recipe);recipe=value;serialized=recipe?new SerializedObject(recipe):null;dirty=true;time=0;message="";}
        void Cleanup(){if(build!=null){build.Dispose();build=null;}if(preview!=null){preview.Cleanup();preview=null;}}
        void Rebuild()
        {
            Cleanup();dirty=false;if(!recipe)return;
            try
            {
                build=ForgeBuilder.Build(recipe);preview=new PreviewRenderUtility();preview.camera.clearFlags=CameraClearFlags.SolidColor;preview.camera.backgroundColor=new Color(.025f,.035f,.055f);preview.camera.nearClipPlane=.02f;preview.camera.farClipPlane=200;preview.AddSingleGO(build.root);
                ForgeBuilder.Sample(build.root,time);
            }
            catch(System.Exception e){message=e.Message;Cleanup();}
        }
        void Tick(){double now=EditorApplication.timeSinceStartup;float dt=(float)(now-previous);previous=now;if(playing&&recipe){time+=Mathf.Min(dt,.1f);if(time>recipe.duration)time=0;}if(this)Repaint();}
        void OnGUI()
        {
            if(dirty&&Event.current.type==EventType.Layout)Rebuild();
            EditorGUILayout.LabelField("통합 VFX 제조기",EditorStyles.boldLabel);
            EditorGUILayout.LabelField("무료 VFX + 셰이더 + 파티클 + 텍스처를 레이어로 조합합니다.",EditorStyles.miniLabel);
            using(new EditorGUILayout.HorizontalScope())for(int i=0;i<ForgePresets.Names.Length;i++){int preset=i;if(GUILayout.Button(ForgePresets.Names[i]))SetRecipe(ForgePresets.Create(preset));}
            using(new EditorGUILayout.HorizontalScope())for(int i=0;i<ForgeHeroPresets.Labels.Length;i++){int hero=i;if(GUILayout.Button(ForgeHeroPresets.Labels[i],EditorStyles.miniButton))SetRecipe(ForgeHeroPresets.Create(hero));}
            using(new EditorGUILayout.HorizontalScope())
            {
                using(new EditorGUILayout.VerticalScope(GUILayout.Width(370)))
                {
                    var selected=(ForgeRecipe)EditorGUILayout.ObjectField("저장된 레시피",AssetDatabase.Contains(recipe)?recipe:null,typeof(ForgeRecipe),false);
                    if(selected&&selected!=recipe)SetRecipe(selected);
                    scroll=EditorGUILayout.BeginScrollView(scroll);
                    if(serialized!=null)
                    {
                        serialized.Update();EditorGUI.BeginChangeCheck();
                        foreach(string property in new[]{"effectName","duration","loop","size","brightness","seed","layers"})EditorGUILayout.PropertyField(serialized.FindProperty(property),true);
                        if(EditorGUI.EndChangeCheck()){serialized.ApplyModifiedProperties();dirty=true;}
                    }
                    EditorGUILayout.Space();EditorGUILayout.LabelField("무료 에셋 팔레트",EditorStyles.boldLabel);
                    sourceSearch=EditorGUILayout.TextField("검색",sourceSearch);
                    if(freeSources==null)freeSources=AssetDatabase.FindAssets("t:Prefab",new[]{"Assets/GabrielAguiarProductions/FreeQuickEffectsVol1"}).Select(g=>AssetDatabase.LoadAssetAtPath<GameObject>(AssetDatabase.GUIDToAssetPath(g))).Where(p=>p).ToArray();
                    foreach(var source in freeSources.Where(p=>p.name.IndexOf(sourceSearch,System.StringComparison.OrdinalIgnoreCase)>=0))
                    {if(GUILayout.Button("+ "+source.name)){Undo.RecordObject(recipe,"Add VFX layer");recipe.layers.Add(new ForgeLayer{name=source.name,shape=ForgeShape.SourcePrefab,sourcePrefab=source,replaceSourceMaterials=true,duration=recipe.duration});dirty=true;}}
                                        EditorGUILayout.Space();EditorGUILayout.LabelField("Kenney 무료 텍스처 · CC0",EditorStyles.boldLabel);
                    if(recipe.layers.Count>0)
                    {
                        textureLayer=EditorGUILayout.Popup("적용할 레이어",Mathf.Clamp(textureLayer,0,recipe.layers.Count-1),recipe.layers.Select(l=>l.name).ToArray());
                        textureSearch=EditorGUILayout.TextField("텍스처 검색",textureSearch);
                        if(libraryTextures==null)libraryTextures=AssetDatabase.FindAssets("t:Texture2D",new[]{"Assets/VFXForge/ThirdParty/Kenney/Textures"}).Select(g=>AssetDatabase.LoadAssetAtPath<Texture2D>(AssetDatabase.GUIDToAssetPath(g))).Where(t=>t).ToArray();
                        EditorGUILayout.LabelField($"사용 가능 {libraryTextures.Length}개",EditorStyles.miniLabel);
                        foreach(var texture in libraryTextures.Where(t=>t.name.IndexOf(textureSearch,System.StringComparison.OrdinalIgnoreCase)>=0).Take(24))
                        {
                            if(GUILayout.Button(new GUIContent("  "+texture.name,texture),GUILayout.Height(32))){Undo.RecordObject(recipe,"Apply free VFX texture");recipe.layers[textureLayer].texture=texture;EditorUtility.SetDirty(recipe);dirty=true;}
                        }
                    }
                    EditorGUILayout.HelpBox("SourcePrefab 레이어에는 다른 프로젝트 VFX 프리팹도 넣을 수 있습니다. 원본 에셋은 변경하지 않으며 원본 라이선스가 적용됩니다.",MessageType.Info);
                    EditorGUILayout.EndScrollView();
                }
                using(new EditorGUILayout.VerticalScope())
                {
                    
                    var rect=GUILayoutUtility.GetRect(320,320,GUILayout.ExpandWidth(true),GUILayout.ExpandHeight(true));
                    if(preview!=null&&build!=null&&Event.current.type==EventType.Repaint)
                    {
                        ForgeBuilder.Sample(build.root,time);
                        preview.camera.transform.position=Quaternion.Euler(pitch,yaw,0)*new Vector3(0,0,-distance);preview.camera.transform.LookAt(Vector3.zero);preview.camera.fieldOfView=45;
                        preview.BeginPreview(rect,GUIStyle.none);preview.Render(true);var texture=preview.EndPreview();GUI.DrawTexture(rect,texture,ScaleMode.StretchToFill,false);
                    }
                    var evt=Event.current;if(rect.Contains(evt.mousePosition)&&evt.type==EventType.MouseDrag){yaw+=evt.delta.x*.5f;pitch=Mathf.Clamp(pitch+evt.delta.y*.4f,-85,85);evt.Use();}if(rect.Contains(evt.mousePosition)&&evt.type==EventType.ScrollWheel){distance=Mathf.Clamp(distance+evt.delta.y*.4f,1,50);evt.Use();}
                    using(new EditorGUILayout.HorizontalScope()){playing=GUILayout.Toggle(playing,"▶ 재생",EditorStyles.miniButton);if(GUILayout.Button("↻ 처음부터")){time=0;dirty=true;}if(GUILayout.Button("카메라 초기화")){yaw=25;pitch=20;distance=14*Mathf.Max(1,recipe?recipe.size:1);}}
                    if(recipe)time=EditorGUILayout.Slider("시간",time,0,Mathf.Max(.1f,recipe.duration));
                    if(build!=null){int count=build.root.GetComponentsInChildren<ParticleSystem>(true).Length;EditorGUILayout.LabelField($"레이어 {recipe.layers.Count(l=>l.enabled)}  ·  파티클 시스템 {count}",EditorStyles.miniLabel);foreach(var warning in build.warnings.Take(3))EditorGUILayout.HelpBox(warning,MessageType.Warning);}
                    if(!string.IsNullOrEmpty(message))EditorGUILayout.HelpBox(message,MessageType.Info);
                    using(new EditorGUI.DisabledScope(!recipe||recipe.layers.All(l=>!l.enabled)))
                    {
                        if(GUILayout.Button("프리팹 + 레시피 저장",GUILayout.Height(32)))
                        {
                            try{string path=ForgeBuilder.Save(recipe);Selection.activeObject=AssetDatabase.LoadAssetAtPath<GameObject>(path);EditorGUIUtility.PingObject(Selection.activeObject);message="저장 완료: "+path;}catch(System.Exception e){message=e.Message;}
                        }
                        if(GUILayout.Button("현재 씬에 배치"))
                        {
                            string path=ForgeBuilder.Save(recipe);var go=(GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(path));Undo.RegisterCreatedObjectUndo(go,"Place VFX");if(SceneView.lastActiveSceneView)go.transform.position=SceneView.lastActiveSceneView.pivot;Selection.activeGameObject=go;
                        }
                    }
                }
            }
        }
    }
}
#endif






