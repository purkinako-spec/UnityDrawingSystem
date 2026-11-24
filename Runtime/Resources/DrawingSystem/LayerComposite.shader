// ---------------------------------------------------------
// LayerComposite.shader
// Composites two layer textures together using one of
// several standard blend modes (Normal, Multiply, Screen,
// Overlay). Blending is performed manually in the fragment
// shader so the hardware blend unit is turned off.
// ---------------------------------------------------------
Shader "Hidden/DrawingSystem/LayerComposite"
{
    Properties
    {
        _BaseTex  ("Base Layer",    2D)           = "white" {}
        _BlendTex ("Blend Layer",   2D)           = "black" {}
        _Opacity  ("Blend Opacity", Range(0, 1))  = 1.0
        _BlendMode("Blend Mode",    Int)          = 0
        // 0 = Normal
        // 1 = Multiply
        // 2 = Screen
        // 3 = Overlay
    }

    SubShader
    {
        Tags { "Queue"="Transparent" "RenderType"="Opaque" }

        Pass
        {
            Name "COMPOSITE"

            ZWrite Off
            ZTest  Always
            Cull   Off
            Blend  Off    // We handle blending manually.

            CGPROGRAM
            #pragma vertex   vert
            #pragma fragment frag
            #pragma target 3.0

            #include "UnityCG.cginc"

            sampler2D _BaseTex;
            sampler2D _BlendTex;
            float     _Opacity;
            int       _BlendMode;

            struct appdata
            {
                float4 vertex : POSITION;
                float2 uv     : TEXCOORD0;
            };

            struct v2f
            {
                float4 pos : SV_POSITION;
                float2 uv  : TEXCOORD0;
            };

            v2f vert(appdata v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.uv  = v.uv;
                return o;
            }

            // ----- Blend-mode helpers (operate per-channel) -----

            // Multiply: darkens by multiplying channels.
            inline fixed3 blendMultiply(fixed3 base, fixed3 blend)
            {
                return base * blend;
            }

            // Screen: lightens by inverting, multiplying, then inverting.
            inline fixed3 blendScreen(fixed3 base, fixed3 blend)
            {
                return 1.0 - (1.0 - base) * (1.0 - blend);
            }

            // Overlay: combination of Multiply and Screen depending on
            // the base luminance per channel.
            inline fixed3 blendOverlay(fixed3 base, fixed3 blend)
            {
                // Per-channel: base < 0.5 ? 2*base*blend : 1 - 2*(1-base)*(1-blend)
                fixed3 lo = 2.0 * base * blend;
                fixed3 hi = 1.0 - 2.0 * (1.0 - base) * (1.0 - blend);
                return fixed3(
                    base.r < 0.5 ? lo.r : hi.r,
                    base.g < 0.5 ? lo.g : hi.g,
                    base.b < 0.5 ? lo.b : hi.b
                );
            }

            fixed4 frag(v2f i) : SV_Target
            {
                fixed4 base  = tex2D(_BaseTex,  i.uv);
                fixed4 blend = tex2D(_BlendTex, i.uv);

                // Effective blend alpha = blend texture alpha * user opacity.
                float blendAlpha = blend.a * _Opacity;

                // Compute the blended RGB according to the selected mode.
                fixed3 blended;

                if (_BlendMode == 1)        // Multiply
                    blended = blendMultiply(base.rgb, blend.rgb);
                else if (_BlendMode == 2)   // Screen
                    blended = blendScreen(base.rgb, blend.rgb);
                else if (_BlendMode == 3)   // Overlay
                    blended = blendOverlay(base.rgb, blend.rgb);
                else                        // Normal (0 and fallback)
                    blended = blend.rgb;

                // Lerp between the original base and the blended result
                // using the effective blend alpha.
                fixed3 finalRGB = lerp(base.rgb, blended, blendAlpha);

                // Combine alphas: the composited alpha is the union of
                // both layers' coverage.
                // Porter-Duff "over": a_out = a_blend + a_base * (1 - a_blend)
                float finalAlpha = blendAlpha + base.a * (1.0 - blendAlpha);

                return fixed4(finalRGB, finalAlpha);
            }
            ENDCG
        }
    }

    FallBack Off
}
