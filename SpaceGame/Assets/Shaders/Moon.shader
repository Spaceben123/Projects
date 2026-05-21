Shader "Space/Moon"
{
    Properties
    {
        _ColorTex         ("Color Map",       2D)     = "white" {}
        _DisplacementMap  ("Displacement Map (Height)", 2D) = "gray"  {}
        _DisplacementStrength ("Displacement Strength", Range(0,2)) = 0.5
        _TerminatorSharpness  ("Terminator Sharpness",  Range(1,20)) = 6
        _Brightness           ("Brightness",            Range(0,2)) = 1.0
        _ColorTint            ("Color Tint",            Color) = (1,1,1,1)
    }

    SubShader
    {
        Tags { "RenderType"="Opaque" "RenderPipeline"="UniversalPipeline" "Queue"="Geometry" }

        // ── ForwardLit ───────────────────────────────────────────────────────────
        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode"="UniversalForward" }

            HLSLPROGRAM
            #pragma vertex   vert
            #pragma fragment frag
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE
            #pragma multi_compile_fragment _ _SHADOWS_SOFT

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;
                float2 uv         : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionHCS : SV_POSITION;
                float2 uv          : TEXCOORD0;
                float3 normalWS    : TEXCOORD1;
                float3 positionWS  : TEXCOORD2;
            };

            TEXTURE2D(_ColorTex);        SAMPLER(sampler_ColorTex);
            TEXTURE2D(_DisplacementMap); SAMPLER(sampler_DisplacementMap);

            CBUFFER_START(UnityPerMaterial)
                float4 _ColorTex_ST;
                float4 _DisplacementMap_ST;
                float  _DisplacementStrength;
                float  _TerminatorSharpness;
                float  _Brightness;
                half4  _ColorTint;
            CBUFFER_END

            Varyings vert(Attributes IN)
            {
                Varyings OUT;
                OUT.positionWS  = TransformObjectToWorld(IN.positionOS.xyz);
                OUT.positionHCS = TransformWorldToHClip(OUT.positionWS);
                OUT.normalWS    = TransformObjectToWorldNormal(IN.normalOS);
                OUT.uv          = TRANSFORM_TEX(IN.uv, _ColorTex);
                return OUT;
            }

            half4 frag(Varyings IN) : SV_Target
            {
                // Color
                half3 color = SAMPLE_TEXTURE2D(_ColorTex, sampler_ColorTex, IN.uv).rgb * _ColorTint.rgb;

                // Derive normal from height map using screen-space derivatives
                float h  = SAMPLE_TEXTURE2D(_DisplacementMap, sampler_DisplacementMap, IN.uv).r;
                float hx = ddx(h);
                float hy = ddy(h);
                float3 bumpNormal = normalize(float3(-hx, -hy, 1.0 / max(_DisplacementStrength, 0.001)));
                // Blend bump normal into surface normal in tangent-like space
                float3 N = normalize(IN.normalWS + bumpNormal * _DisplacementStrength * 0.3);

                // Diffuse lighting
                Light mainLight = GetMainLight(TransformWorldToShadowCoord(IN.positionWS));
                float NdotL_geo = dot(N, mainLight.direction);
                float k = _TerminatorSharpness;
                float dayBlend = smoothstep(-1.0 / k, 1.0 / k, NdotL_geo);
                float NdotL = saturate(NdotL_geo);

                half3 lit = color * NdotL * dayBlend * mainLight.color;

                // Very faint fill so dark side isn't pitch black (moonshine / earthshine)
                half3 ambient = color * 0.02;

                return half4((lit + ambient) * _Brightness, 1.0);
            }
            ENDHLSL
        }

        // ── ShadowCaster ─────────────────────────────────────────────────────────
        Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode"="ShadowCaster" }
            ColorMask 0

            HLSLPROGRAM
            #pragma vertex   vertShadow
            #pragma fragment fragShadow

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Shadows.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float4 _ColorTex_ST;
                float4 _DisplacementMap_ST;
                float  _DisplacementStrength;
                float  _TerminatorSharpness;
                float  _Brightness;
                half4  _ColorTint;
            CBUFFER_END

            float3 _LightDirection;

            struct Attributes { float4 positionOS : POSITION; float3 normalOS : NORMAL; };
            struct Varyings   { float4 positionHCS : SV_POSITION; };

            Varyings vertShadow(Attributes IN)
            {
                Varyings OUT;
                float3 posWS  = TransformObjectToWorld(IN.positionOS.xyz);
                float3 normWS = TransformObjectToWorldNormal(IN.normalOS);
                OUT.positionHCS = TransformWorldToHClip(ApplyShadowBias(posWS, normWS, _LightDirection));
                return OUT;
            }
            half4 fragShadow(Varyings IN) : SV_Target { return 0; }
            ENDHLSL
        }

        // ── DepthOnly ─────────────────────────────────────────────────────────────
        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode"="DepthOnly" }
            ColorMask 0

            HLSLPROGRAM
            #pragma vertex   vertDepth
            #pragma fragment fragDepth

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float4 _ColorTex_ST;
                float4 _DisplacementMap_ST;
                float  _DisplacementStrength;
                float  _TerminatorSharpness;
                float  _Brightness;
                half4  _ColorTint;
            CBUFFER_END

            struct Attributes { float4 positionOS : POSITION; };
            struct Varyings   { float4 positionHCS : SV_POSITION; };

            Varyings vertDepth(Attributes IN)
            {
                Varyings OUT;
                OUT.positionHCS = TransformObjectToHClip(IN.positionOS.xyz);
                return OUT;
            }
            half4 fragDepth(Varyings IN) : SV_Target { return 0; }
            ENDHLSL
        }
    }
}
