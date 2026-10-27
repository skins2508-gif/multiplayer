using UnityEditor;
using UnityEngine;

namespace Blocks.Gameplay.Shooter.Editor
{
    // Build only the second character's weapon asset; never modify the shared shooter prefab.
    [InitializeOnLoad]
    public static class NinjaKatanaAssetBuilder
    {
        const string ModelPath = "Assets/Shooter/Art/Ninja/Katana.fbx";
        const string PrefabPath = "Assets/Shooter/Resources/Ninja/Katana.prefab";
        const string MaterialPath = "Assets/Shooter/Art/Ninja/Katana.mat";
        const string Textures = "Assets/katana-real-time-game-ready/textures/Katana_low_Katana_Shader_";

        static NinjaKatanaAssetBuilder() { EditorApplication.delayCall += BuildIfMissing; }
        static void BuildIfMissing()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) return;
            if (AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath) == null) Build();
            BuildShuriken();
        }

        [MenuItem("Tools/Shooter/Build Ninja Shuriken")]
        public static void BuildShuriken()
        {
            const string output = "Assets/Shooter/Resources/Ninja/Shuriken.prefab";
            if (AssetDatabase.LoadAssetAtPath<GameObject>(output) != null) return;
            var source = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Shurikens pack/Prefabs/Shuriken1.prefab");
            if (source == null) return;
            var shader = Shader.Find("Universal Render Pipeline/Lit");
            if (shader == null) return;
            const string matPath = "Assets/Shooter/Art/Ninja/Shuriken.mat";
            var material = AssetDatabase.LoadAssetAtPath<Material>(matPath);
            if (material == null)
            {
                material = new Material(shader);
                material.SetColor("_BaseColor", Color.white);
                material.SetTexture("_BaseMap", AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/Shurikens pack/Textures/Shuriken1_d.png"));
                material.SetTexture("_BumpMap", AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/Shurikens pack/Textures/Shuriken1_n.png"));
                material.EnableKeyword("_NORMALMAP");
                material.SetFloat("_Metallic", 0.25f);
                AssetDatabase.CreateAsset(material, matPath);
            }
            var instance = (GameObject)PrefabUtility.InstantiatePrefab(source);
            try
            {
                foreach (var renderer in instance.GetComponentsInChildren<Renderer>())
                {
                    var materials = new Material[renderer.sharedMaterials.Length];
                    for (int i = 0; i < materials.Length; i++) materials[i] = material;
                    renderer.sharedMaterials = materials;
                }
                PrefabUtility.SaveAsPrefabAsset(instance, output);
                AssetDatabase.SaveAssets();
            }
            finally { Object.DestroyImmediate(instance); }
        }

        [MenuItem("Tools/Shooter/Rebuild Ninja Katana")]
        public static void Build()
        {
            var model = AssetDatabase.LoadAssetAtPath<GameObject>(ModelPath);
            if (model == null) return;
            var shader = Shader.Find("Universal Render Pipeline/Lit");
            if (shader == null) { Debug.LogError("Ninja Katana requires URP Lit."); return; }
            var normalPath = Textures + "Normal.1001.png";
            var importer = AssetImporter.GetAtPath(normalPath) as TextureImporter;
            if (importer != null && importer.textureType != TextureImporterType.NormalMap)
            {
                importer.textureType = TextureImporterType.NormalMap;
                importer.SaveAndReimport();
            }
            var material = AssetDatabase.LoadAssetAtPath<Material>(MaterialPath);
            if (material == null)
            {
                material = new Material(shader);
                AssetDatabase.CreateAsset(material, MaterialPath);
            }
            material.SetTexture("_BaseMap", AssetDatabase.LoadAssetAtPath<Texture2D>(Textures + "BaseColor.1001.png"));
            material.SetTexture("_BumpMap", AssetDatabase.LoadAssetAtPath<Texture2D>(normalPath));
            material.EnableKeyword("_NORMALMAP");
            material.SetFloat("_Metallic", 0.85f);
            material.SetFloat("_Smoothness", 0.45f);
            EditorUtility.SetDirty(material);
            var root = new GameObject("NinjaKatana");
            try
            {
                var visual = (GameObject)PrefabUtility.InstantiatePrefab(model);
                visual.transform.SetParent(root.transform, false);
                foreach (var renderer in visual.GetComponentsInChildren<Renderer>())
                {
                    var materials = new Material[renderer.sharedMaterials.Length];
                    for (int i = 0; i < materials.Length; i++) materials[i] = material;
                    renderer.sharedMaterials = materials;
                }
                PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
                AssetDatabase.SaveAssets();
                Debug.Log("Second-character katana ready: " + PrefabPath);
            }
            finally { Object.DestroyImmediate(root); }
        }
    }
}
