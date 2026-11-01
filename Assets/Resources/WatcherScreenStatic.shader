Shader "UI/WatcherScreenStatic"
{
    Properties
    {
        [PerRendererData] _MainTex ("Texture", 2D) = "white" {}
        _Color ("Tint", Color) = (1,1,1,1)
        _GrainPixels ("Grain size in screen pixels", Float) = 1.5
    }
    SubShader
    {
        Tags { "Queue"="Transparent" "IgnoreProjector"="True" "RenderType"="Transparent" "PreviewType"="Plane" }
        Cull Off
        Lighting Off
        ZWrite Off
        ZTest [unity_GUIZTestMode]
        Blend SrcAlpha OneMinusSrcAlpha
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"
            struct appdata { float4 vertex : POSITION; float4 color : COLOR; float2 uv : TEXCOORD0; };
            struct v2f { float4 vertex : SV_POSITION; fixed4 color : COLOR; float2 uv : TEXCOORD0; };
            fixed4 _Color;
            float _GrainPixels;
            v2f vert(appdata input)
            {
                v2f output;
                output.vertex = UnityObjectToClipPos(input.vertex);
                output.uv = input.uv;
                output.color = input.color * _Color;
                return output;
            }
            fixed4 frag(v2f input) : SV_Target
            {
                float2 pixel = floor(input.uv * _ScreenParams.xy / max(1.0, _GrainPixels));
                float frame = floor(_Time.y * 24.0);
                float3 p = frac(float3(pixel.xyx) * 0.1031 + frame * 0.071);
                p += dot(p, p.yzx + 33.33);
                float grain = frac((p.x + p.y) * p.z);
                return fixed4(grain.xxx * input.color.rgb, input.color.a);
            }
            ENDCG
        }
    }
}
