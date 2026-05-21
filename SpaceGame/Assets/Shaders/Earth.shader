Shader "Space/Earth"
{
    Properties
    {
        _DayTex             ("Day Texture",             2D)           = "white" {}
        _NightTex           ("Night Texture",           2D)           = "black" {}
        _NormalMap          ("Normal Map",              2D)           = "bump"  {}
        _SpecularMap        ("Specular Map",            2D)           = "black" {}

        _NormalStrength     ("Normal Strength",         Range(0,2))   = 1.0
        _SpecularColor      ("Specular Color",          Color)        = (0.75, 0.85, 1.0, 1)
        _Shininess          ("Shininess",               Range(1,512)) = 160

        _TerminatorSharpness("Terminator Sharpness (k)",Range(1,30)) = 8.0
        _NightBrightness    ("Night Lights Brightness", Range(0,1))   = 0.35
        _NightDayMaskPow    ("Night Day Mask Power",    Range(1,10))  = 3.0
        _FactionTex      ("Faction Overlay",  2D)         = "black" {}
        _FactionStrength ("Faction Strength", Range(0,1)) = 0.4
        _BorderTex       ("Border Overlay",   2D)         = "black" {}
        _BorderStrength  ("Border Strength",  Range(0,1)) = 0.6
    }

    SubShader
    {
        Tags { "RenderType"="Opaque" "RenderPipeline"="UniversalPipeline" "Queue"="Geometry" }
        LOD 300

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode"="UniversalForward" }
            Cull Back
            ZWrite On
            ZTest LEqual

            HLSLPROGRAM
            #pragma vertex   Vert
            #pragma fragment Frag
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            TEXTURE2D(_DayTex);      SAMPLER(sampler_DayTex);
            TEXTURE2D(_NightTex);    SAMPLER(sampler_NightTex);
            TEXTURE2D(_NormalMap);   SAMPLER(sampler_NormalMap);
            TEXTURE2D(_SpecularMap); SAMPLER(sampler_SpecularMap);
            TEXTURE2D(_FactionTex);  SAMPLER(sampler_FactionTex);
            TEXTURE2D(_BorderTex);   SAMPLER(sampler_BorderTex);

            CBUFFER_START(UnityPerMaterial)
                float4 _DayTex_ST;
                float4 _SpecularColor;
                float  _Shininess;
                float  _TerminatorSharpness;
                float  _NightBrightness;
                float  _NightDayMaskPow;
                float  _NormalStrength;
                float  _FactionStrength;
                float  _BorderStrength;
            CBUFFER_END

            struct Attributes
            {
                float4 posOS    : POSITION;
                float3 normOS   : NORMAL;
                float4 tanOS    : TANGENT;
                float2 uv       : TEXCOORD0;
            };

            struct Varyings
            {
                float4 posCS    : SV_POSITION;
                float2 uv       : TEXCOORD0;
                float3 posWS    : TEXCOORD1;
                float3 normWS   : TEXCOORD2;
                float3 tanWS    : TEXCOORD3;
                float3 bitanWS  : TEXCOORD4;
            };

            Varyings Vert(Attributes IN)
            {
                Varyings OUT;
                VertexPositionInputs posInputs  = GetVertexPositionInputs(IN.posOS.xyz);
                VertexNormalInputs   normInputs = GetVertexNormalInputs(IN.normOS, IN.tanOS);

                OUT.posCS   = posInputs.positionCS;
                OUT.posWS   = posInputs.positionWS;
                OUT.normWS  = normInputs.normalWS;
                OUT.tanWS   = normInputs.tangentWS;
                OUT.bitanWS = normInputs.bitangentWS;
                OUT.uv      = TRANSFORM_TEX(IN.uv, _DayTex);
                return OUT;
            }

            half4 Frag(Varyings IN) : SV_Target
            {
                half4 dayCol   = SAMPLE_TEXTURE2D(_DayTex,      sampler_DayTex,      IN.uv);
                half4 nightCol = SAMPLE_TEXTURE2D(_NightTex,    sampler_NightTex,    IN.uv);
                half3 normalTS = UnpackNormal(SAMPLE_TEXTURE2D(_NormalMap, sampler_NormalMap, IN.uv));
                half  specMask = SAMPLE_TEXTURE2D(_SpecularMap, sampler_SpecularMap, IN.uv).r;

                normalTS.xy *= _NormalStrength;
                normalTS     = normalize(normalTS);

                float3 T = normalize(IN.tanWS);
                float3 B = normalize(IN.bitanWS);
                float3 N = normalize(IN.normWS);
                float3 normalWS = normalize(T * normalTS.x + B * normalTS.y + N * normalTS.z);
                float3 geoNorm  = N;

                Light  light = GetMainLight();
                float3 L = light.direction;
                float3 V = normalize(GetCameraPositionWS() - IN.posWS);
                float3 H = normalize(L + V);

                float NdotL_geo = dot(geoNorm, L);
                float NdotL     = max(0.0, dot(normalWS, L));

                float dayBlend = smoothstep(-1.0 / _TerminatorSharpness,
                                             1.0 / _TerminatorSharpness,
                                             NdotL_geo);

                float NdotH = max(0.0, dot(normalWS, H));
                float spec  = pow(NdotH, _Shininess) * specMask * NdotL;

                half3 sunColor = (half3)light.color;
                half3 litDay   = dayCol.rgb * NdotL * sunColor
                               + spec * (half3)_SpecularColor.rgb * sunColor;
                float nightMask = pow(saturate(1.0 - dayBlend), _NightDayMaskPow);
                half3 litNight = nightCol.rgb * _NightBrightness * nightMask;
                half3 color    = lerp(litNight, litDay, dayBlend);

                half4 faction = SAMPLE_TEXTURE2D(_FactionTex, sampler_FactionTex, IN.uv);
                color.rgb = lerp(color.rgb, faction.rgb, faction.a * _FactionStrength);

                half border = SAMPLE_TEXTURE2D(_BorderTex, sampler_BorderTex, IN.uv).r * _BorderStrength;
                color.rgb += border;

                return half4(color, 1.0);
            }
            ENDHLSL
        }

        Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode"="ShadowCaster" }
            ZWrite On
            ZTest  LEqual
            ColorMask 0
            Cull Back

            HLSLPROGRAM
            #pragma vertex   ShadowVert
            #pragma fragment ShadowFrag
            #pragma multi_compile_vertex _ _CASTING_PUNCTUAL_LIGHT_SHADOW

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Shadows.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float4 _DayTex_ST;
                float4 _SpecularColor;
                float  _Shininess;
                float  _TerminatorSharpness;
                float  _NightBrightness;
                float  _NightDayMaskPow;
                float  _NormalStrength;
                float  _FactionStrength;
                float  _BorderStrength;
            CBUFFER_END

            float3 _LightDirection;
            float3 _LightPosition;

            struct ShadowAttr { float4 posOS : POSITION; float3 normOS : NORMAL; };
            struct ShadowVary { float4 posCS : SV_POSITION; };

            ShadowVary ShadowVert(ShadowAttr IN)
            {
                ShadowVary OUT;
                float3 posWS  = TransformObjectToWorld(IN.posOS.xyz);
                float3 normWS = TransformObjectToWorldNormal(IN.normOS);
                #if _CASTING_PUNCTUAL_LIGHT_SHADOW
                    float3 lightDir = normalize(_LightPosition - posWS);
                #else
                    float3 lightDir = _LightDirection;
                #endif
                OUT.posCS = TransformWorldToHClip(ApplyShadowBias(posWS, normWS, lightDir));
                return OUT;
            }

            half4 ShadowFrag(ShadowVary IN) : SV_Target { return 0; }
            ENDHLSL
        }

        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode"="DepthOnly" }
            ZWrite On
            ColorMask 0
            Cull Back

            HLSLPROGRAM
            #pragma vertex   DepthVert
            #pragma fragment DepthFrag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float4 _DayTex_ST;
                float4 _SpecularColor;
                float  _Shininess;
                float  _TerminatorSharpness;
                float  _NightBrightness;
                float  _NightDayMaskPow;
                float  _NormalStrength;
                float  _FactionStrength;
                float  _BorderStrength;
            CBUFFER_END

            struct DepthAttr { float4 posOS : POSITION; };
            struct DepthVary { float4 posCS : SV_POSITION; };

            DepthVary DepthVert(DepthAttr IN)
            {
                DepthVary OUT;
                OUT.posCS = TransformObjectToHClip(IN.posOS.xyz);
                return OUT;
            }

            half4 DepthFrag(DepthVary IN) : SV_Target { return 0; }
            ENDHLSL
        }
    }

    FallBack "Universal Render Pipeline/Lit"
}
