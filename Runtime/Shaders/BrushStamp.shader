// ---------------------------------------------------------
// BrushStamp.shader
// Stamps a brush texture onto a RenderTexture at a given
// UV position with configurable size, hardness, and opacity.
// Supports normal paint and eraser blend modes.
// ---------------------------------------------------------
Shader "Hidden/DrawingSystem/BrushStamp"
{
    Properties
    {
        _BrushTex ("Brush Tip", 2D)           = "white" {}
        _Color    ("Brush Color", Color)       = (0, 0, 0, 1)
        _BrushPos ("Brush Position (UV)", Vector) = (0.5, 0.5, 0, 0)
        _BrushSize("Brush Size (UV)",  Float)  = 0.05
        _Hardness ("Edge Hardness",    Range(0, 1)) = 1.0
        _Opacity  ("Stamp Opacity",    Range(0, 1)) = 1.0
        _IsEraser ("Eraser Mode",      Float)  = 0.0
    }

    CGINCLUDE
    #include "UnityCG.cginc"

    // ---- shared declarations ----
    sampler2D _BrushTex;
    fixed4    _Color;
    float4    _BrushPos;   // xy = UV centre
    float     _BrushSize;
    float     _Hardness;
    float     _Opacity;
    float     _IsEraser;

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

    // Shared fragment logic ------------------------------------------------
    // Returns the final color (with premultiplied alpha contribution).
    fixed4 brushFragment(v2f i)
    {
        // Distance from the current fragment to the brush centre in quad-local UV space (0 to 1).
        float2 delta = i.uv - float2(0.5, 0.5);
        float  dist  = length(delta);
        float  radius = 0.5;

        // Discard pixels outside the circular brush radius.
        if (dist > radius)
            discard;

        // Sample the brush tip texture using quad UV (0 to 1).
        fixed4 brushSample = tex2D(_BrushTex, i.uv);

        // Edge hardness falloff.
        // Hardness = 1 -> crisp anti-aliased edge.
        // Hardness = 0 -> soft airbrush falloff.
        float t = saturate(dist / radius);
        float edgeStart = clamp(_Hardness * 0.9, 0.0, 0.9);
        float falloff = 1.0 - smoothstep(edgeStart, 1.0, t);

        // Combine brush texture alpha, falloff, and stamp opacity.
        float alpha = brushSample.a * falloff * _Opacity;

        // Build the output color.
        fixed4 col;
        col.rgb = _Color.rgb;
        col.a   = alpha * _Color.a;

        return col;
    }
    ENDCG

    // =====================================================================
    // SubShader 0 – Normal painting (alpha blended)
    // =====================================================================
    SubShader
    {
        Tags { "Queue"="Transparent" "RenderType"="Transparent" }

        // Pass 0: Normal Painting
        Pass
        {
            Name "PAINT"

            ZWrite Off
            ZTest   Always
            Cull    Off
            Blend   SrcAlpha OneMinusSrcAlpha, One OneMinusSrcAlpha

            CGPROGRAM
            #pragma vertex   vert
            #pragma fragment frag
            #pragma target 3.0

            fixed4 frag(v2f i) : SV_Target
            {
                return brushFragment(i);
            }
            ENDCG
        }

        // Pass 1: Eraser Mode (clears pixels to transparent)
        Pass
        {
            Name "ERASER"

            ZWrite Off
            ZTest   Always
            Cull    Off
            Blend   Zero OneMinusSrcAlpha, Zero OneMinusSrcAlpha

            CGPROGRAM
            #pragma vertex   vert
            #pragma fragment frag_eraser
            #pragma target 3.0

            fixed4 frag_eraser(v2f i) : SV_Target
            {
                float2 delta = i.uv - float2(0.5, 0.5);
                float  dist  = length(delta);
                float  radius = 0.5;

                if (dist > radius)
                    discard;

                fixed4 brushSample = tex2D(_BrushTex, i.uv);

                float t = saturate(dist / radius);
                float edgeStart = clamp(_Hardness * 0.9, 0.0, 0.9);
                float falloff = 1.0 - smoothstep(edgeStart, 1.0, t);
                float erase   = brushSample.a * falloff * _Opacity;

                return fixed4(0, 0, 0, erase);
            }
            ENDCG
        }
    }

    FallBack Off
}
