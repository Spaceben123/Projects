// Minimal unlit, vertex-colored, alpha-blended URP shader used by
// CountryBorderRenderer (border ribbon meshes) and SurfaceSiteMarkerRenderer
// (billboard markers). Vertex color alpha drives per-segment/per-marker
// visibility (alpha 0 = fully hidden) without any topology rebuild, and
// _Color lets a single shared mesh be tinted per-instance via a
// MaterialPropertyBlock. Requires a per-vertex NORMAL: the fragment stage
// discards any pixel whose surface normal faces away from the camera (see
// Frag below) — both mesh generators must supply a real outward-facing
// normal per vertex (radial direction on the Earth sphere for border
// ribbons, local +Z for camera-facing markers) or their geometry will be
// incorrectly culled.
Shader "Space/VertexColorLine"
{
    Properties
    {
        _Color ("Tint", Color) = (1, 1, 1, 1)
        _LineHalfWidth ("Line Half Width (object space)", Float) = 0
    }

    SubShader
    {
        Tags { "RenderType"="Transparent" "RenderPipeline"="UniversalPipeline" "Queue"="Transparent" }
        LOD 100

        Pass
        {
            Name "ForwardUnlit"
            Tags { "LightMode"="UniversalForward" }
            Cull Off
            ZWrite Off
            ZTest LEqual
            // Small clip-space depth bias toward the camera — cheap extra safety net
            // against coplanar precision noise, on top of the surface-normal discard
            // below (which is what actually fixes self-occlusion by the sphere).
            Offset -1, -1
            Blend SrcAlpha OneMinusSrcAlpha

            HLSLPROGRAM
            #pragma vertex   Vert
            #pragma fragment Frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float4 _Color;
                float  _LineHalfWidth;
            CBUFFER_END

            struct Attributes
            {
                float4 posOS  : POSITION;
                float3 normOS : NORMAL;
                float4 sideOS : TEXCOORD0; // xyz = miter side direction, w = expansion sign (±1)
                float4 color  : COLOR;
            };

            struct Varyings
            {
                float4 posCS  : SV_POSITION;
                float3 normWS : TEXCOORD0;
                float3 posWS  : TEXCOORD1;
                float4 color  : COLOR;
            };

            Varyings Vert(Attributes IN)
            {
                Varyings OUT;

                // Ribbon width is applied here rather than baked into the mesh, so
                // CountryBorderRenderer can drive _LineHalfWidth from camera distance and
                // keep borders a constant pixel thickness at every zoom level. Geometry
                // with no side attribute (e.g. the site-marker quad) has sideOS = 0 and is
                // therefore left exactly where its vertices already are.
                float3 posOS = IN.posOS.xyz + IN.sideOS.xyz * (IN.sideOS.w * _LineHalfWidth);

                VertexPositionInputs posInputs = GetVertexPositionInputs(posOS);
                OUT.posCS   = posInputs.positionCS;
                OUT.posWS   = posInputs.positionWS;
                OUT.normWS  = TransformObjectToWorldNormal(IN.normOS);
                OUT.color   = IN.color * _Color;
                return OUT;
            }

            half4 Frag(Varyings IN) : SV_Target
            {
                // Discard fragments whose surface normal faces away from the camera.
                // This mesh (border ribbons on the Earth sphere, or camera-facing site
                // markers) sits at the same effective radius as a convex sphere: any
                // point whose outward normal faces the camera is guaranteed visible and
                // unoccluded by that sphere, and any point whose normal faces away is on
                // the far/limb side and should be hidden. This is what actually stops
                // the border ribbon from disappearing behind the Earth mesh — depth
                // testing against a *different* mesh (Earth) at nearly the same radius
                // is inherently precision-fragile, so this replaces reliance on that
                // entirely rather than trying to tune a world-space offset by hand.
                float3 viewDirWS = normalize(GetCameraPositionWS() - IN.posWS);
                float3 normalWS  = normalize(IN.normWS);
                if (dot(normalWS, viewDirWS) < 0.0)
                    discard;

                return half4(IN.color.rgb, IN.color.a);
            }
            ENDHLSL
        }
    }

    FallBack Off
}
