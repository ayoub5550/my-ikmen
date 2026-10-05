// Sprite material for fighters, helpers, explods and projectiles (dev.5).
// MUGEN palette effects (PalFX / AllPalFX / HitDef palfx / AfterImage) are applied per pixel:
//   rgb = (invert ? 1-rgb : rgb); rgb = lerp(gray, rgb, _Sat); rgb = rgb * _Mul + _Add
// and the MUGEN blend modes are chosen with _SrcBlend/_DstBlend/_BlendOp:
//   normal = SrcAlpha/OneMinusSrcAlpha, add = SrcAlpha/One (premultiplied), sub = RevSub.
// Reference: engine/ikemen-go/src/render_gl33.go + shaders (palfx uniforms "add", "mult", "gray", "neg").
// dev.7: optional crisp pixel-art filtering (global _IKPixelAA), see RenderQuality.cs.
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
            float4 _MainTex_TexelSize;
            float _IKPixelAA;               // global (Options -> Video -> Crisp); not a material property
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
            // dev.7 "Crisp": pixel-art anti-aliasing. With a bilinear texture, sample at the texel
            // centre except within one screen pixel of a texel border, where the two texels are
            // blended over that one pixel: square pixels of even width at any scale, no shimmer.
            float2 CrispUV(float2 uv) {
                float2 px = uv * _MainTex_TexelSize.zw;
                float2 box = clamp(fwidth(px), 1e-5, 1.0);
                float2 tx = px - 0.5 * box;
                float2 off = smoothstep(1.0 - box, 1.0, frac(tx));
                return (floor(tx) + 0.5 + off) * _MainTex_TexelSize.xy;
            }
            fixed4 frag(v2f i) : SV_Target {
                float2 uv = _IKPixelAA > 0.5 ? CrispUV(i.texcoord) : i.texcoord;
                fixed4 c = tex2D(_MainTex, uv);
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
