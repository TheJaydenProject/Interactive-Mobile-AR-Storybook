Shader "UI/RoundedRectangle"
{
    Properties
    {
        [PerRendererData] _MainTex ("Sprite Texture", 2D) = "white" {}
        _Color ("Tint", Color) = (1,1,1,1)
        
        _Radius ("Corner Radius", Float) = 0
        _RectWidth ("Rect Width", Float) = 100
        _RectHeight ("Rect Height", Float) = 100
        _DrawWidth ("Draw Width", Float) = 100
        _DrawHeight ("Draw Height", Float) = 100
        
        // UI Masking
        _StencilComp ("Stencil Comparison", Float) = 8
        _Stencil ("Stencil ID", Float) = 0
        _StencilOp ("Stencil Operation", Float) = 0
        _StencilWriteMask ("Stencil Write Mask", Float) = 255
        _StencilReadMask ("Stencil Read Mask", Float) = 255
        _ColorMask ("Color Mask", Float) = 15
    }

    SubShader
    {
        Tags
        {
            "Queue"="Transparent"
            "IgnoreProjector"="True"
            "RenderType"="Transparent"
            "PreviewType"="Plane"
            "CanUseSpriteAtlas"="True"
        }

        Stencil
        {
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
        Blend SrcAlpha OneMinusSrcAlpha
        ColorMask [_ColorMask]

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            struct appdata_t
            {
                float4 vertex   : POSITION;
                float4 color    : COLOR;
                float2 texcoord : TEXCOORD0;
            };

            struct v2f
            {
                float4 vertex   : SV_POSITION;
                fixed4 color    : COLOR;
                float2 texcoord  : TEXCOORD0;
            };

            sampler2D _MainTex;
            fixed4 _Color;
            float _Radius;
            float _RectWidth;
            float _RectHeight;
            float _DrawWidth;
            float _DrawHeight;

            v2f vert(appdata_t IN)
            {
                v2f OUT;
                OUT.vertex = UnityObjectToClipPos(IN.vertex);
                OUT.texcoord = IN.texcoord;
                OUT.color = IN.color * _Color;
                return OUT;
            }

            fixed4 frag(v2f IN) : SV_Target
            {
                half4 color = tex2D(_MainTex, IN.texcoord) * IN.color;
                
                // Signed Distance Field for Rounded Box
                float2 uv = IN.texcoord - 0.5;
                float2 rectSize = float2(_RectWidth, _RectHeight);
                float2 p = uv * rectSize;
                
                float2 drawSize = float2(_DrawWidth, _DrawHeight);
                float2 d = abs(p) - (drawSize * 0.5) + _Radius;
                float dist = length(max(d, 0.0)) + min(max(d.x, d.y), 0.0) - _Radius;
                
                // Smooth edges (anti-aliasing)
                float alpha = smoothstep(0.0, 1.5, -dist);
                color.a *= alpha;

                return color;
            }
            ENDCG
        }
    }
}
