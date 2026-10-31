Shader "Hidden/RedSand/OpaqueDistanceFog"
{
    Properties { _MainTex ("Source", 2D) = "white" {} }
    SubShader
    {
        Cull Off ZWrite Off ZTest Always
        Pass
        {
            CGPROGRAM
            #pragma vertex vert_img
            #pragma fragment frag
            #include "UnityCG.cginc"
            sampler2D _MainTex;
            float4 _MainTex_TexelSize;
            UNITY_DECLARE_DEPTH_TEXTURE(_CameraDepthTexture);
            float4 _FogColor;
            float _FogStart;
            float _FogEnd;
            float4 frag(v2f_img input) : SV_Target
            {
                float2 depthUV = input.uv;
                #if UNITY_UV_STARTS_AT_TOP
                if (_MainTex_TexelSize.y < 0) depthUV.y = 1 - depthUV.y;
                #endif
                float depth = SAMPLE_DEPTH_TEXTURE(_CameraDepthTexture, depthUV);
                float eyeDistance = LinearEyeDepth(depth);
                float2 rayXY = (depthUV * 2 - 1) / float2(unity_CameraProjection._m00, unity_CameraProjection._m11);
                float distanceFromCamera = eyeDistance * length(float3(rayXY, 1));
                // A hard saturation removes ALL scene contrast beyond the fog horizon.
                // Include the sky so fogged mountains cannot silhouette against it.
                if (distanceFromCamera >= _FogEnd) return float4(_FogColor.rgb, 1);
                float amount = smoothstep(_FogStart, _FogEnd, distanceFromCamera);
                float4 source = tex2D(_MainTex, input.uv);
                return float4(lerp(source.rgb, _FogColor.rgb, amount), source.a);
            }
            ENDCG
        }
    }
    Fallback Off
}
