// Self-contained stylized shader for a cloud mesh (URP). Applied directly to the mesh's material —
// this is unrelated to VolumetricCloudsBlit.shader/CloudSunLightSync.cs from earlier, which paint a
// full-screen sky effect and don't touch individual objects. Ignore those two if you only need this.
//
// No dependency on any scene Light or Environment Lighting: lighting direction, tint, and ambient
// colors are all shader properties you set on the material, so the look is fixed regardless of
// scene lighting setup.
//
// Setup:
//   1. Create a Material from this shader.
//   2. Drag it onto the cloud mesh's Renderer.
//   3. Tune _LightDirection/_LightColor and the top/bottom ambient colors on the material to taste.
Shader "Custom/StylizedCloudSurface"
{
    Properties
    {
        _BaseColorTop("Top Ambient Color", Color) = (0.4314, 0.4471, 0.4784, 1) // #6E727A
        _BaseColorBottom("Bottom Ambient Color", Color) = (0.2, 0.2157, 0.2431, 1) // #33373E
        _Opacity("Opacity", Range(0, 1)) = 0.9529 // ~243/255

        _LightDirection("Fake Light Direction", Vector) = (0.4, 0.8, 0.3, 0)
        _LightColor("Fake Light Color", Color) = (1, 0.98, 0.92, 1)
        _LightIntensity("Fake Light Intensity", Range(0, 3)) = 0.55
        _LightWrap("Light Wrap", Range(0, 1)) = 0.9

        _RimColor("Rim (Silver Lining) Color", Color) = (0.75, 0.78, 0.82, 1)
        _RimPower("Rim Power", Range(0.5, 8)) = 3
        _RimIntensity("Rim Intensity", Range(0, 5)) = 0.6

        _NoiseScale("Surface Breakup Scale", Float) = 0.05
        _NoiseStrength("Surface Breakup Strength", Range(0, 1)) = 0.35

        _AOScale("Puffy Cavity Scale", Float) = 0.015
        _AOStrength("Puffy Cavity Strength", Range(0, 1)) = 0.35
    }

    SubShader
    {
        Tags { "RenderType" = "Transparent" "RenderPipeline" = "UniversalPipeline" "Queue" = "Transparent" }

        Pass
        {
            Name "ForwardUnlit"
            Tags { "LightMode" = "UniversalForward" }

            // ZWrite On even though this blends: the mesh is several overlapping sphere lobes,
            // and at this opacity (~0.95) ZWrite Off let them draw out of depth order, showing
            // gaps/cracks where the background poked through between lobes.
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite On

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float4 _BaseColorTop;
                float4 _BaseColorBottom;
                float4 _LightDirection;
                float4 _LightColor;
                float _LightIntensity;
                float _LightWrap;
                float4 _RimColor;
                float _RimPower;
                float _RimIntensity;
                float _NoiseScale;
                float _NoiseStrength;
                float _AOScale;
                float _AOStrength;
                float _Opacity;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float3 normalWS : TEXCOORD1;
            };

            Varyings Vert(Attributes input)
            {
                Varyings output;
                output.positionWS = TransformObjectToWorld(input.positionOS.xyz);
                output.positionCS = TransformWorldToHClip(output.positionWS);
                output.normalWS = TransformObjectToWorldNormal(input.normalOS);
                return output;
            }

            // Cheap hash-based value noise; only used to break up otherwise perfectly smooth shading bands.
            float Hash31(float3 p)
            {
                p = frac(p * 0.3183099 + 0.1);
                p *= 17.0;
                return frac(p.x * p.y * p.z * (p.x + p.y + p.z));
            }

            float ValueNoise3D(float3 p)
            {
                float3 i = floor(p);
                float3 f = frac(p);
                f = f * f * (3.0 - 2.0 * f);

                float n000 = Hash31(i + float3(0, 0, 0));
                float n100 = Hash31(i + float3(1, 0, 0));
                float n010 = Hash31(i + float3(0, 1, 0));
                float n110 = Hash31(i + float3(1, 1, 0));
                float n001 = Hash31(i + float3(0, 0, 1));
                float n101 = Hash31(i + float3(1, 0, 1));
                float n011 = Hash31(i + float3(0, 1, 1));
                float n111 = Hash31(i + float3(1, 1, 1));

                float nx00 = lerp(n000, n100, f.x);
                float nx10 = lerp(n010, n110, f.x);
                float nx01 = lerp(n001, n101, f.x);
                float nx11 = lerp(n011, n111, f.x);

                float nxy0 = lerp(nx00, nx10, f.y);
                float nxy1 = lerp(nx01, nx11, f.y);

                return lerp(nxy0, nxy1, f.z);
            }

            // Layered noise (fixed 3 octaves) so the surface reads as lumpy/puffy rather than
            // one smooth ripple — each octave doubles frequency and halves contribution.
            float Fbm3D(float3 p)
            {
                float sum = 0.0;
                float amplitude = 0.5;
                float frequency = 1.0;

                [unroll]
                for (int i = 0; i < 3; i++)
                {
                    sum += ValueNoise3D(p * frequency) * amplitude;
                    frequency *= 2.0;
                    amplitude *= 0.5;
                }

                return sum;
            }

            float4 Frag(Varyings input) : SV_Target
            {
                float3 normalWS = normalize(input.normalWS);
                float3 viewDirWS = normalize(_WorldSpaceCameraPos - input.positionWS);
                float3 lightDir = normalize(_LightDirection.xyz);

                // Wrap lighting softens the terminator so the mesh reads as translucent/fluffy
                // instead of a hard-lit sphere; purely a function of the fixed _LightDirection property.
                float ndotl = dot(normalWS, lightDir);
                float wrapped = saturate((ndotl + _LightWrap) / (1.0 + _LightWrap));

                float heightMask = saturate(normalWS.y * 0.5 + 0.5);
                float3 baseColor = lerp(_BaseColorBottom.rgb, _BaseColorTop.rgb, heightMask);

                float fresnel = pow(1.0 - saturate(dot(normalWS, viewDirWS)), _RimPower);
                float lightFacing = saturate(dot(viewDirWS, lightDir) * 0.5 + 0.5);
                float3 rim = _RimColor.rgb * fresnel * _RimIntensity * lerp(0.3, 1.0, lightFacing);

                // Fine breakup: small lumpy variation across the surface so shading bands don't
                // read as one smooth gradient.
                float breakup = Fbm3D(input.positionWS * _NoiseScale) * _NoiseStrength;
                baseColor *= saturate(1.0 - breakup);

                // Fake AO: a much coarser noise pass darkens the "cavities" between puffy lobes,
                // faking the self-shadowing a real volumetric cloud would have without any
                // raymarching — cheap depth cue that sells the fluffiness. Fbm3D's output only
                // ever spans roughly the middle of 0-1, so it's contrast-stretched via smoothstep
                // first — otherwise every point on the mesh gets some darkening and the whole
                // cloud just looks dimmer instead of gaining real cavities.
                float cavity = smoothstep(0.3, 0.7, Fbm3D(input.positionWS * _AOScale));
                float ao = lerp(1.0 - _AOStrength, 1.0, cavity);
                baseColor *= ao;

                float3 litColor = baseColor * (1.0 + _LightColor.rgb * _LightIntensity * wrapped) + rim;

                return float4(litColor, _Opacity);
            }
            ENDHLSL
        }

        // Lets the mesh cast a shadow onto the ground/other objects. Independent of the fake
        // shading above — URP supplies _LightDirection for this pass itself during shadow rendering.
        Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode" = "ShadowCaster" }

            ZWrite On
            ZTest LEqual
            ColorMask 0

            HLSLPROGRAM
            #pragma vertex ShadowVert
            #pragma fragment ShadowFrag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Shadows.hlsl"

            float3 _LightDirection;

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
            };

            float4 GetShadowCasterPositionCS(float3 positionWS, float3 normalWS)
            {
                float4 positionCS = TransformWorldToHClip(ApplyShadowBias(positionWS, normalWS, _LightDirection));

                #if UNITY_REVERSED_Z
                    positionCS.z = min(positionCS.z, positionCS.w * UNITY_NEAR_CLIP_VALUE);
                #else
                    positionCS.z = max(positionCS.z, positionCS.w * UNITY_NEAR_CLIP_VALUE);
                #endif

                return positionCS;
            }

            Varyings ShadowVert(Attributes input)
            {
                Varyings output;
                float3 positionWS = TransformObjectToWorld(input.positionOS.xyz);
                float3 normalWS = TransformObjectToWorldNormal(input.normalOS);
                output.positionCS = GetShadowCasterPositionCS(positionWS, normalWS);
                return output;
            }

            half4 ShadowFrag(Varyings input) : SV_Target
            {
                return 0;
            }
            ENDHLSL
        }

        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode" = "DepthOnly" }

            ZWrite On
            ColorMask 0

            HLSLPROGRAM
            #pragma vertex DepthOnlyVert
            #pragma fragment DepthOnlyFrag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
            };

            Varyings DepthOnlyVert(Attributes input)
            {
                Varyings output;
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                return output;
            }

            half4 DepthOnlyFrag(Varyings input) : SV_Target
            {
                return 0;
            }
            ENDHLSL
        }
    }

    Fallback Off
}