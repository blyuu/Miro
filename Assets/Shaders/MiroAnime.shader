// MiroAnime - ground-up anime cel-shading character shader for MIRO.
// Technique reference: Adrian Mendez's "Genshin Impact Character Shader
// Breakdown [Unity URP]" (NdotL cel bands, shadow/light tint damping, face
// shadow maps, anisotropic hair, gradient metallic, screen-space outline).
// This reimplements the TECHNIQUE, not the original game's shader/assets.
//
// Architecture: ten independently-toggleable feature groups (see Properties
// below). Phase 1-2 of this shader's build-out wires up groups 1, 2, 9, 10
// (Base Color, Cel Lighting incl. Outer Shadow Band + Additional Lights,
// Ambient/Light Tint Control, Fog). Groups 3-8 (Face Shadow, Anisotropic
// Hair, Metallic Reflection, Outline, Edge Highlight, Mobile Quality) are
// DECLARED here as properties/toggles for a stable material interface going
// forward, but have NO shader logic yet - flipping those toggles currently
// has no visible effect. See Assets/Shaders/MiroAnime.README.md (none yet -
// documented in the chat report instead) for why: this mesh has a single
// material/submesh and only one base-color texture, so Phase 3 (face shadow
// map) and Phase 5 (metallic mask) have no source data to read from yet.
Shader "Miro/MiroAnime"
{
    Properties
    {
        [Header(1 Base Color Base Texture)]
        _BaseMap("Base Map", 2D) = "white" {}
        _BaseColor("Base Color", Color) = (1,1,1,1)

        [Header(2 Custom Cel Lighting)]
        _ShadowThreshold("Shadow Threshold", Range(0,1)) = 0.5
        _ShadowSoftness("Shadow Softness", Range(0.001,0.2)) = 0.08
        _ShadowTint("Shadow Tint", Color) = (0.85,0.82,0.78,1)
        _ShadowStrength("Shadow Strength", Range(0,1)) = 0.3

        [Header(3 Outer Shadow Band)]
        [Toggle(_OUTERSHADOW_ON)] _EnableOuterShadow("Enable Outer Shadow Band", Float) = 1
        _OuterShadowThreshold("Outer Shadow Threshold (NdotL)", Range(-1,0.5)) = -0.15
        _OuterShadowTint("Outer Shadow Tint", Color) = (0.62,0.6,0.62,1)

        [Header(4 Face Shadow Phase3 Not Implemented)]
        [Toggle(_FACESHADOW_ON)] _EnableFaceShadow("Enable Face Shadow", Float) = 0
        _FaceShadowMap("Face Shadow Map (R/G direction mask)", 2D) = "white" {}
        _FaceForward("Face Forward Axis (object space)", Vector) = (0,0,1,0)

        [Header(5 Anisotropic Hair Phase4 Not Implemented)]
        [Toggle(_ANISOHAIR_ON)] _EnableAnisoHair("Enable Anisotropic Hair", Float) = 0
        _HairShift("Hair Highlight Shift", Range(-1,1)) = 0
        _HairHighlightWidth("Hair Highlight Width", Range(1,64)) = 20
        _HairHighlightIntensity("Hair Highlight Intensity", Range(0,1)) = 0

        [Header(6 Metallic Reflection Phase5 Not Implemented)]
        [Toggle(_METALLIC_ON)] _EnableMetallic("Enable Metallic Shading", Float) = 0
        _MetallicMask("Metallic Mask", 2D) = "black" {}
        _MetallicGradient("Metallic Gradient Ramp", 2D) = "white" {}
        _ReflectionStrength("Reflection Strength", Range(0,1)) = 0

        [Header(7 Outline Phase6 Not Implemented)]
        [Toggle(_OUTLINE_ON)] _EnableOutline("Enable Outline", Float) = 0
        _OutlineWidth("Outline Width", Range(0,5)) = 1
        _OutlineColor("Outline Color", Color) = (0.03,0.03,0.04,1)

        [Header(8 Edge Highlight Phase6 Not Implemented)]
        [Toggle(_EDGEHIGHLIGHT_ON)] _EnableEdgeHighlight("Enable White Edge Highlight", Float) = 0
        _EdgeHighlightWidth("Edge Highlight Width", Range(0,1)) = 0

        [Header(9 Ambient Light Tint Control)]
        _LightTintInfluence("Light Tint Influence", Range(0,1)) = 0.2
        _AmbientStrength("Ambient Strength", Range(0,1)) = 0.15
        _AdditionalLightsStrength("Additional Lights Strength", Range(0,2)) = 0.5

        [Header(Specular Rim Carried From MiroToon)]
        _RimColor("Rim Color", Color) = (0.95,0.96,0.98,1)
        _RimPower("Rim Power", Range(0.5,8)) = 6
        _RimIntensity("Rim Intensity", Range(0,1)) = 0.05
        _SpecularIntensity("Specular Intensity", Range(0,1)) = 0.02
        _SpecularPower("Specular Power", Range(1,64)) = 30

        [Header(10 Fog Integration)]
        [Toggle(_FOG_ON)] _EnableFog("Enable Fog Integration", Float) = 1

        [Header(Phase7 Mobile Quality Reserved)]
        [Toggle(_MOBILE_QUALITY)] _QualityMobile("Mobile Quality Mode", Float) = 0
    }

    SubShader
    {
        Tags { "RenderType"="Opaque" "RenderPipeline"="UniversalPipeline" "Queue"="Geometry" }
        LOD 200

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode"="UniversalForward" }

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag

            #pragma shader_feature_local _OUTERSHADOW_ON
            #pragma shader_feature_local _FOG_ON
            // Declared for Phase 3-6/Mobile toggles - no branching reads
            // these yet (groups not implemented), kept here only so the
            // keywords already exist on the material for when they are.
            #pragma shader_feature_local _FACESHADOW_ON
            #pragma shader_feature_local _ANISOHAIR_ON
            #pragma shader_feature_local _METALLIC_ON
            #pragma shader_feature_local _OUTLINE_ON
            #pragma shader_feature_local _EDGEHIGHLIGHT_ON
            #pragma shader_feature_local _MOBILE_QUALITY

            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS_CASCADE
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile _ _ADDITIONAL_LIGHTS_VERTEX _ADDITIONAL_LIGHTS
            #pragma multi_compile _ _ADDITIONAL_LIGHT_SHADOWS
            #pragma multi_compile _ _SHADOWS_SOFT
            #pragma multi_compile_fog

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
                float4 positionCS : SV_POSITION;
                float2 uv         : TEXCOORD0;
                float3 positionWS : TEXCOORD1;
                float3 normalWS   : TEXCOORD2;
                float4 shadowCoord: TEXCOORD3;
                float  fogCoord   : TEXCOORD4;
            };

            TEXTURE2D(_BaseMap); SAMPLER(sampler_BaseMap);

            CBUFFER_START(UnityPerMaterial)
                float4 _BaseMap_ST;
                half4  _BaseColor;

                half   _ShadowThreshold;
                half   _ShadowSoftness;
                half4  _ShadowTint;
                half   _ShadowStrength;

                half   _OuterShadowThreshold;
                half4  _OuterShadowTint;

                half   _LightTintInfluence;
                half   _AmbientStrength;
                half   _AdditionalLightsStrength;

                half4  _RimColor;
                half   _RimPower;
                half   _RimIntensity;
                half   _SpecularIntensity;
                half   _SpecularPower;
            CBUFFER_END

            Varyings vert(Attributes IN)
            {
                Varyings OUT;
                VertexPositionInputs vertexInput = GetVertexPositionInputs(IN.positionOS.xyz);
                VertexNormalInputs normalInput = GetVertexNormalInputs(IN.normalOS);

                OUT.positionCS = vertexInput.positionCS;
                OUT.positionWS = vertexInput.positionWS;
                OUT.normalWS   = normalInput.normalWS;
                OUT.uv         = TRANSFORM_TEX(IN.uv, _BaseMap);
                OUT.shadowCoord = GetShadowCoord(vertexInput);
                OUT.fogCoord   = ComputeFogFactor(vertexInput.positionCS.z);
                return OUT;
            }

            // Shared three-band (lit / shadow / outer-shadow) cel ramp,
            // reused for the Main Light and for each additional light so
            // every light source reads with the same stylized banding
            // instead of additional lights looking like plain PBR falloff.
            half3 CelLight(half3 albedo, half3 lightColor, float NdotL, half shadowAtten)
            {
                float litMask = smoothstep(_ShadowThreshold - _ShadowSoftness, _ShadowThreshold + _ShadowSoftness, NdotL);
                litMask *= shadowAtten;

                half3 tintedLightColor = lerp(half3(1,1,1), lightColor, _LightTintInfluence);
                half3 litColor    = albedo * tintedLightColor;
                half3 shadowColor = albedo * _ShadowTint.rgb * (1.0 - _ShadowStrength);

                half3 celColor = lerp(shadowColor, litColor, litMask);

            #if defined(_OUTERSHADOW_ON)
                // A second, deeper band for surfaces facing fully away from
                // this light (NdotL below a user threshold, typically
                // slightly negative) - keeps the shadow side from reading as
                // one flat slab of color on rounded forms (head, torso).
                float outerMask = smoothstep(_OuterShadowThreshold - _ShadowSoftness, _OuterShadowThreshold + _ShadowSoftness, NdotL);
                half3 outerShadowColor = albedo * _OuterShadowTint.rgb * (1.0 - _ShadowStrength);
                celColor = lerp(outerShadowColor, celColor, outerMask);
            #endif

                return celColor;
            }

            half4 frag(Varyings IN) : SV_Target
            {
                half4 baseTex = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, IN.uv);
                half3 albedo = baseTex.rgb * _BaseColor.rgb;

                float3 normalWS = normalize(IN.normalWS);
                float3 viewDirWS = normalize(GetWorldSpaceViewDir(IN.positionWS));

                Light mainLight = GetMainLight(IN.shadowCoord);
                float mainNdotL = dot(normalWS, mainLight.direction);
                half3 finalColor = CelLight(albedo, mainLight.color, mainNdotL, mainLight.shadowAttenuation);

                // Ambient (SampleSH) - scene ambient probe only, scaled down
                // so it reads as fill light rather than a color wash.
                half3 ambient = SampleSH(normalWS) * albedo;
                finalColor += ambient * _AmbientStrength;

            #if defined(_ADDITIONAL_LIGHTS) || defined(_ADDITIONAL_LIGHTS_VERTEX)
                // Same cel-banding treatment per additional light, with its
                // own independent strength dial so a cluster of colored
                // environment lights (e.g. the subway's cyan/magenta
                // spotlights) never overrides the character's own cel shading
                // - this is the "Additional Light Support" this shader adds
                // over the previous MiroToon shader, which only read the
                // Main Light.
                uint additionalLightsCount = GetAdditionalLightsCount();
                for (uint lightIndex = 0u; lightIndex < additionalLightsCount; lightIndex++)
                {
                    Light light = GetAdditionalLight(lightIndex, IN.positionWS);
                    float ndotl = dot(normalWS, light.direction);
                    half3 addColor = CelLight(albedo, light.color, ndotl, light.shadowAttenuation);
                    half3 addContribution = (addColor - albedo * _ShadowTint.rgb * (1.0 - _ShadowStrength)) * light.distanceAttenuation;
                    finalColor += addContribution * _AdditionalLightsStrength;
                }
            #endif

                // Rim light (edge-glow, kept subtle and near-neutral so it
                // doesn't reintroduce a color cast of its own).
                float rim = 1.0 - saturate(dot(normalWS, viewDirWS));
                rim = pow(rim, _RimPower) * _RimIntensity;
                finalColor += _RimColor.rgb * rim;

                // Specular (kept low-intensity, tight falloff - toon
                // highlight, not a PBR glossy response).
                float3 halfDir = normalize(mainLight.direction + viewDirWS);
                float litMaskForSpec = smoothstep(_ShadowThreshold - _ShadowSoftness, _ShadowThreshold + _ShadowSoftness, mainNdotL) * mainLight.shadowAttenuation;
                float spec = pow(saturate(dot(normalWS, halfDir)), _SpecularPower) * _SpecularIntensity * litMaskForSpec;
                finalColor += spec;

            #if defined(_FOG_ON)
                finalColor = MixFog(finalColor, IN.fogCoord);
            #endif

                return half4(finalColor, baseTex.a * _BaseColor.a);
            }
            ENDHLSL
        }

        UsePass "Universal Render Pipeline/Lit/ShadowCaster"
        UsePass "Universal Render Pipeline/Lit/DepthOnly"
        UsePass "Universal Render Pipeline/Lit/DepthNormals"
    }

    FallBack "Universal Render Pipeline/Lit"
}
