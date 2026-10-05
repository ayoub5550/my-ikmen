// Sprite material for fighters, helpers, explods and projectiles (dev.5).
// MUGEN palette effects (PalFX / AllPalFX / HitDef palfx / AfterImage) are applied per pixel:
//   rgb = (invert ? 1-rgb : rgb); rgb = lerp(gray, rgb, _Sat); rgb = rgb * _Mul + _Add
// and the MUGEN blend modes are chosen with _SrcBlend/_DstBlend/_BlendOp:
//   normal = SrcAlpha/OneMinusSrcAlpha, add = SrcAlpha/One (premultiplied), sub = RevSub.
// Reference: engine/ikemen-go/src/render_gl33.go + shaders (palfx uniforms "add", "mult", "gray", "neg").
// Lives under Resources and in GraphicsSettings.m_AlwaysIncludedShaders so IL2CPP keeps it.
Shader "IK/UIPalFx" {
    Properties {
        _MainTex ("Sprite Texture", 2D) = "white" {}
        _Color ("Tint", Color) = (1,1,1,1)
        _Add ("Add", Vector) = (0,0,0,0)
        _Mul ("Mul", Vector) = (1,1,1,1)
        _Sat ("Saturation", Float) = 1
        _Invert ("Invert", Float) = 0
        _Premul ("Premultiply", Float) = 0
        _SrcBlend ("Src", Float) = 5
        _DstBlend ("Dst", Float) = 10
        _BlendOp ("Op", Float) = 0
        _StencilComp ("Stencil Comparison", Float) = 8
        _Stencil ("Stencil ID", Float) = 0
        _StencilOp ("Stencil Operation", Float) = 0
        _StencilWriteMask ("Stencil Write Mask", Float) = 255
        _StencilReadMask ("Stencil Read Mask", Float) = 255
        _ColorMask ("Color Mask", Float) = 15
    }
    SubShader {
        Tags { "Queue"="Transparent" "IgnoreProjector"="True" "RenderType"="Transparent" "PreviewType"="Plane" "CanUseSpriteAtlas"="True" }
        Stencil {
            Ref [_Stencil]
            Comp [_StencilComp]
            Pass [_StencilOp]
            ReadMask [_StencilReadMask]
            WriteMask [_StencilWriteMask]
        }
        Cull Off
        Lighting Off
        ZWrite Off
        ZTest [unity_GUIZTestMode]
        BlendOp [_BlendOp]
        Blend [_SrcBlend] [_DstBlend]
        ColorMask [_ColorMask]
        Pass {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"
            struct appdata_t { float4 vertex : POSITION; float4 color : COLOR; float2 texcoord : TEXCOORD0; };
            struct v2f { float4 vertex : SV_POSITION; fixed4 color : COLOR; float2 texcoord : TEXCOORD0; };
            sampler2D _MainTex;
            fixed4 _Color;
            float4 _Add, _Mul;
            float _Sat, _Invert, _Premul;
            v2f vert(appdata_t v) {
                v2f o;
                o.vertex = UnityObjectToClipPos(v.vertex);
                o.texcoord = v.texcoord;
                o.color = v.color * _Color;
                return o;
            }
            fixed4 frag(v2f i) : SV_Target {
                fixed4 c = tex2D(_MainTex, i.texcoord);
                float3 rgb = c.rgb;
                if (_Invert > 0.5) rgb = 1.0 - rgb;
                float g = dot(rgb, float3(0.299, 0.587, 0.114));
                rgb = lerp(float3(g, g, g), rgb, _Sat);
                rgb = saturate(rgb * _Mul.rgb + _Add.rgb);
                c.rgb = rgb;
                c *= i.color;
                if (_Premul > 0.5) c.rgb *= c.a;
                return c;
            }
            ENDCG
        }
    }
}
