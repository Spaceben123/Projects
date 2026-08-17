Shader "Space/Atmosphere"
{
    Properties
    {
        _PlanetRadius   ("Planet Radius",     Float)       = 10.0
        _AtmoRadius     ("Atmo Radius",       Float)       = 10.173
        _DensityFalloff ("Scale Height R",    Float)       = 0.01334
        _MieFalloff     ("Scale Height M",    Float)       = 0.00188
        _SunIntensity   ("Sun Intensity",     Range(0,100))= 20.0
        _OpticalDepthLUT("Optical Depth LUT", 2D)          = "black" {}
    }

    SubShader
    {
        Tags
        {
            "RenderType"     = "Transparent"
            "RenderPipeline" = "UniversalPipeline"
            "Queue"          = "Transparent+1"
        }

        Pass
        {
            Name "AtmosphericScatter"
            Tags { "LightMode" = "UniversalForward" }

            Cull Back
            ZWrite Off
            ZTest LEqual
            Blend One One

            HLSLPROGRAM
            #pragma vertex   Vert
            #pragma fragment Frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float  _PlanetRadius;
                float  _AtmoRadius;
                float  _DensityFalloff;
                float  _MieFalloff;
                float  _SunIntensity;
                float4 _PlanetCentre;
                float4 _OpticalDepthLUT_ST;
            CBUFFER_END

            TEXTURE2D(_OpticalDepthLUT);
            SAMPLER(sampler_OpticalDepthLUT);

            static const float3 kRayleigh = float3(5.5, 13.0, 22.4);
            static const float3 kMie      = float3(4.0, 4.0, 4.0);
            static const float  kMieG     = 0.76;

            #define NUM_STEPS 16

            float2 RaySphereIntersect(float3 ro, float3 rd, float r)
            {
                float b = dot(ro, rd);
                float c = dot(ro, ro) - r * r;
                float d = b * b - c;
                if (d < 0.0) return float2(1e10, -1e10);
                float s = sqrt(d);
                return float2(-b - s, -b + s);
            }

            float RayleighPhase(float cosTheta)
            {
                return (3.0 / (16.0 * PI)) * (1.0 + cosTheta * cosTheta);
            }

            float MiePhase(float cosTheta, float g)
            {
                float g2 = g * g;
                return (3.0 * (1.0 - g2)) / (8.0 * PI * (2.0 + g2))
                     * (1.0 + cosTheta * cosTheta)
                     / pow(1.0 + g2 - 2.0 * g * cosTheta, 1.5);
            }

            float2 SampleOpticalDepthLUT(float normHeight, float cosZenith)
            {
                float2 uv = float2(saturate(normHeight), cosZenith * 0.5 + 0.5);
                return SAMPLE_TEXTURE2D_LOD(_OpticalDepthLUT, sampler_OpticalDepthLUT, uv, 0).rg;
            }

            struct Attributes { float4 posOS : POSITION; };

            struct Varyings
            {
                float4 posCS : SV_POSITION;
                float3 posWS : TEXCOORD0;
            };

            Varyings Vert(Attributes IN)
            {
                Varyings OUT;
                VertexPositionInputs p = GetVertexPositionInputs(IN.posOS.xyz);
                OUT.posCS = p.positionCS;
                OUT.posWS = p.positionWS;
                return OUT;
            }

            half4 Frag(Varyings IN) : SV_Target
            {
                float3 camPos  = GetCameraPositionWS();
                float3 centre  = _PlanetCentre.xyz;
                float3 rayDir  = normalize(IN.posWS - camPos);
                float3 rayOrig = camPos - centre;

                float2 atmoHit = RaySphereIntersect(rayOrig, rayDir, _AtmoRadius);
                float2 planHit = RaySphereIntersect(rayOrig, rayDir, _PlanetRadius);

                float tEntry = max(atmoHit.x, 0.0);
                float tExit  = atmoHit.y;

                if (planHit.x > 0.0 && planHit.x < tExit)
                    tExit = planHit.x;

                if (tExit <= tEntry)
                    return half4(0, 0, 0, 0);

                Light  mainLight = GetMainLight();
                float3 dirToSun  = mainLight.direction;
                float3 rawColor  = mainLight.color;
                float  maxChan   = max(rawColor.r, max(rawColor.g, rawColor.b));
                float3 sunColor  = rawColor / max(maxChan, 0.0001);

                float cosTheta = dot(rayDir, dirToSun);
                float phaseR   = RayleighPhase(cosTheta);
                float phaseM   = MiePhase(cosTheta, kMieG);

                float  stepSize   = (tExit - tEntry) / float(NUM_STEPS);
                float3 accR       = 0.0;
                float3 accM       = 0.0;
                float  viewDepthR = 0.0;
                float  viewDepthM = 0.0;
                float  atmoThick  = _AtmoRadius - _PlanetRadius;

                for (int i = 0; i < NUM_STEPS; i++)
                {
                    float3 pos    = rayOrig + rayDir * (tEntry + (i + 0.5) * stepSize);
                    float  height = max(0.0, length(pos) - _PlanetRadius);
                    float  normH  = saturate(height / atmoThick);

                    float densR = exp(-height / _DensityFalloff);
                    float densM = exp(-height / _MieFalloff);

                    viewDepthR += densR * stepSize;
                    viewDepthM += densM * stepSize;

                    float3 up       = pos / max(length(pos), 0.000001);
                    float  cosZen   = dot(up, dirToSun);
                    float2 sunDepth = SampleOpticalDepthLUT(normH, cosZen);

                    float3 tau = kRayleigh * (viewDepthR + sunDepth.r)
                               + kMie      * (viewDepthM + sunDepth.g);
                    float3 transmittance = exp(-tau);

                    accR += densR * transmittance * stepSize;
                    accM += densM * transmittance * stepSize;
                }

                float3 scatter = _SunIntensity * sunColor
                               * (kRayleigh * phaseR * accR + kMie * phaseM * accM);

                return half4(max(scatter, 0.0), 0.0);
            }
            ENDHLSL
        }
    }
}
