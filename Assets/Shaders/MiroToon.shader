Shader "Miro/MiroToon"
{
    Properties
    {
        _BaseMap("Base Map", 2D) = "white" {}
        _BaseColor("Base Color", Color) = (1,1,1,1)

        _CelThreshold("Cel Threshold", Range(0,1)) = 0.5
        _CelSoftness("Cel Softness", Range(0.001,0.2)) = 0.04

        _ShadowTint("Shadow Tint", Color) = (0.55,0.62,0.75,1)
        _ShadowStrength("Shadow Strength", Range(0,1)) = 0.55

        // Dampens how much the scene's actual Main Light color tints the
        // character - 0 = always render under neutral white light (true
        // albedo colors always show through), 1 = fully inherit the Main
        // Light's real color. Keeps environment-colored lighting (e.g. a
        // cyan/magenta-lit level) from overpowering the character's own
        // skin/hair/clothing colors, per the Genshin-style "Light Tint
        // Influence" technique.
        _LightTintInfluence("Light Tint Influence", Range(0,1)) = 0.35

        _RimColor("Rim Color", Color) = (0.65,0.9,1,1)
        _RimPower("Rim Power", Range(0.5,8)) = 4
        _RimIntensity("Rim Intensity", Range(0,1)) = 0.15

        _SpecularIntensity("Specular Intensity", Range(0,1)) = 0.05
        _SpecularPower("Specular Power", Range(1,64)) = 24

        _AmbientStrength("Ambient Strength", Range(0,1)) = 0.15
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

            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS_CASCADE
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS_SCREEN
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
                half   _CelThreshold;
                half   _CelSoftness;
                half4  _ShadowTint;
                half   _ShadowStrength;
                half   _LightTintInfluence;
                half4  _RimColor;
                half   _RimPower;
                half   _RimIntensity;
                half   _SpecularIntensity;
                half   _SpecularPower;
                half   _AmbientStrength;
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

            half4 frag(Varyings IN) : SV_Target
            {
                half4 baseTex = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, IN.uv);
                half3 albedo = baseTex.rgb * _BaseColor.rgb;

                float3 normalWS = normalize(IN.normalWS);

                Light mainLight = GetMainLight(IN.shadowCoord);
                float NdotL = dot(normalWS, mainLight.direction);

                float litMask = smoothstep(_CelThreshold - _CelSoftness, _CelThreshold + _CelSoftness, NdotL);
                litMask *= mainLight.shadowAttenuation;

                // Blend the Main Light's actual color toward neutral white by
                // _LightTintInfluence before applying it to albedo - this is
                // what stops a strongly-colored environment light from
                // washing out the character's own true colors.
                half3 tintedLightColor = lerp(half3(1,1,1), mainLight.color, _LightTintInfluence);
                half3 litColor    = albedo * tintedLightColor;
                half3 shadowColor = albedo * _ShadowTint.rgb * (1.0 - _ShadowStrength);

                half3 celColor = lerp(shadowColor, litColor, litMask);

                half3 ambient = SampleSH(normalWS) * albedo;
                celColor += ambient * _AmbientStrength;

                float3 viewDirWS = normalize(GetWorldSpaceViewDir(IN.positionWS));
                float rim = 1.0 - saturate(dot(normalWS, viewDirWS));
                rim = pow(rim, _RimPower) * _RimIntensity;
                celColor += _RimColor.rgb * rim;

                float3 halfDir = normalize(mainLight.direction + viewDirWS);
                float spec = pow(saturate(dot(normalWS, halfDir)), _SpecularPower) * _SpecularIntensity * litMask;
                celColor += spec;

                half3 finalColor = celColor;
                finalColor = MixFog(finalColor, IN.fogCoord);

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