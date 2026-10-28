Shader "VFX Forge/Alpha URP" {
Properties { _MainTex("Texture",2D)="white"{} [HDR]_Tint("Tint",Color)=(1,1,1,1) }
SubShader { Tags { "RenderPipeline"="UniversalPipeline" "Queue"="Transparent" "RenderType"="Transparent" } Pass {
Blend SrcAlpha OneMinusSrcAlpha
ZWrite Off
Cull Off
HLSLPROGRAM
#pragma vertex vert
#pragma fragment frag
#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
TEXTURE2D(_MainTex); SAMPLER(sampler_MainTex);
CBUFFER_START(UnityPerMaterial)
float4 _MainTex_ST;float4 _Tint;
CBUFFER_END
struct A {float4 positionOS:POSITION;float2 uv:TEXCOORD0;half4 color:COLOR;};
struct V {float4 positionCS:SV_POSITION;float2 uv:TEXCOORD0;half4 color:COLOR;};
V vert(A a){V o;o.positionCS=TransformObjectToHClip(a.positionOS.xyz);o.uv=a.uv*_MainTex_ST.xy+_MainTex_ST.zw;o.color=a.color;return o;}
half4 frag(V i):SV_Target{return SAMPLE_TEXTURE2D(_MainTex,sampler_MainTex,i.uv)*i.color*_Tint;}
ENDHLSL
} }
}

