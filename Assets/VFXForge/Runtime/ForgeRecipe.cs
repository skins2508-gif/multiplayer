using System;
using System.Collections.Generic;
using UnityEngine;

namespace VFXForge
{
    public enum ForgeEmission { Sphere, Cone, Box }
    public enum ForgeShape { Sparks, Smoke, Glow, Ring, Slash, SourcePrefab }
    [Serializable]
    public sealed class ForgeLayer
    {
        public string name="New layer";
        public bool enabled=true;
        public ForgeShape shape;
        [ColorUsage(true,true)] public Color color=new Color(.1f,.7f,1,1);
        public Texture2D texture;
        public Material material;
        public Shader shader;
        public GameObject sourcePrefab;
        public bool replaceSourceMaterials;
        public Vector3 offset,rotation;
        [Min(.01f)] public float scale=1;
        [Min(0)] public float delay;
        [Min(.05f)] public float duration=1;
        [Range(1,1000)] public int count=32;
        [Min(0)] public float speed=3;
        public ForgeEmission emissionShape;
        [Range(0,90)] public float spreadAngle=15;
        [Min(.001f)] public float radius=.08f;
        [Min(.001f)] public float particleSize=.12f;
    }
    [CreateAssetMenu(menuName="VFX Forge/Recipe",fileName="VFX Recipe")]
    public sealed class ForgeRecipe : ScriptableObject
    {
        public string effectName="New VFX";
        [Min(.1f)] public float duration=2;
        public bool loop;
        [Range(.1f,10)] public float size=1;
        [Range(.1f,10)] public float brightness=2;
        [Range(1,100000)] public int seed=1729;
        public List<ForgeLayer> layers=new List<ForgeLayer>();
    }
}

