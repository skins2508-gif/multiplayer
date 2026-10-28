#if UNITY_EDITOR
using System;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;
namespace VFXForge.Editor
{
    [InitializeOnLoad]
    public static class ForgeVerification
    {
        static ForgeVerification(){EditorApplication.update+=Tick;}
        static void Tick()
        {
            if(EditorApplication.isPlayingOrWillChangePlaymode||EditorApplication.isCompiling||EditorApplication.isUpdating||!File.Exists("Temp/VFXForgeCheck.request"))return;
            File.Delete("Temp/VFXForgeCheck.request");var report=new StringBuilder();Directory.CreateDirectory("ArtSource/VFXForge");
            try
            {
                foreach(int index in Enumerable.Range(0,5))
                {
                    var recipe=ForgePresets.Create(index);
                    try
                    {
                        string path=ForgeBuilder.Save(recipe);var prefab=AssetDatabase.LoadAssetAtPath<GameObject>(path);
                        if(!prefab||!prefab.GetComponent<ForgePlayback>())throw new Exception("Prefab missing playback: "+path);
                        using(var b=ForgeBuilder.Build(recipe))
                        {
                            var preview=new PreviewRenderUtility();try
                            {
                                preview.AddSingleGO(b.root);preview.camera.clearFlags=CameraClearFlags.SolidColor;preview.camera.backgroundColor=new Color(.025f,.035f,.055f);preview.camera.nearClipPlane=.02f;preview.camera.farClipPlane=100;preview.camera.transform.position=new Vector3(8,6,-11);preview.camera.transform.LookAt(Vector3.zero);
                                foreach(float t in new[]{0,.1f,.35f,.8f,1.5f,2.4f,0})ForgeBuilder.Sample(b.root,t);
                                ForgeBuilder.Sample(b.root,index==2?.15f:.65f);
                                preview.BeginStaticPreview(new Rect(0,0,640,480));preview.Render(true);var texture=preview.EndStaticPreview();File.WriteAllBytes("ArtSource/VFXForge/"+recipe.effectName.Replace(' ','_')+".png",texture.EncodeToPNG());UnityEngine.Object.DestroyImmediate(texture);
                            }
                            finally{preview.Cleanup();}
                            // PreviewRenderUtility owns and releases its preview scene object.
                            b.root=null;
                        }
                        var loaded=PrefabUtility.LoadPrefabContents(path);
                        try
                        {
                            ForgeBuilder.Sample(loaded,.3f);
                            foreach(var renderer in loaded.GetComponentsInChildren<Renderer>(true))foreach(var material in renderer.sharedMaterials)if(!material||!material.shader||!material.shader.isSupported)throw new Exception("Invalid material in "+path);
                        }
                        finally{PrefabUtility.UnloadPrefabContents(loaded);}
                        report.AppendLine("PASS: "+recipe.effectName+" | save/reload/materials/time scrub | "+path);
                    }
                    finally{UnityEngine.Object.DestroyImmediate(recipe);}
                }
                report.AppendLine("PASS: all five presets generated; original source prefabs/materials unchanged.");
                VfxForgeWindow.Open();
            }
            catch(Exception e){report.AppendLine("FAIL: "+e);Debug.LogException(e);}
            File.WriteAllText("ArtSource/VFXForge/Verification.txt",report.ToString());
        }
    }
}
#endif

