// WaterURP.shader  (URP) - refraction + depth color + reflection + detail normals + foam,
// now two-sided so it renders correctly when the camera is UNDER the water (swimming).
// Requires URP asset: Depth Texture + Opaque Texture enabled.
Shader "Water/URP"
{
    Properties
    {
        [Header(Displacement)]
        _HeightTex        ("Height RT", 2D) = "black" {}
        _DisplacementScale("Displacement Scale", Float) = 0.10
        _DisplacementClamp("Displacement Clamp", Float) = 0.30
        _NormalScale      ("Sim Normal Scale", Float) = 2.5

        [Header(Detail normals)]
        _DetailNormal   ("Detail Normal", 2D) = "bump" {}
        _DetailStrength ("Detail Strength", Range(0,2)) = 0.6
        _DetailTiling   ("Detail Tiling", Float) = 0.5
        _DetailSpeed    ("Detail Scroll Speed", Float) = 0.04

        [Header(Color and depth)]
        _ShallowColor   ("Shallow Color", Color) = (0.30, 0.62, 0.66, 1)
        _DeepColor      ("Deep Color",    Color) = (0.02, 0.16, 0.22, 1)
        _DepthFade      ("Depth Fade (m)", Float) = 1.5
        _RefractStrength("Refraction Strength", Float) = 0.04

        [Header(Reflection and lighting)]
        _Smoothness    ("Smoothness", Range(0,1)) = 0.9
        _FresnelPow    ("Fresnel Power", Float) = 5
        _SpecIntensity ("Sun Specular", Float) = 1.5

        [Header(Foam)]
        _FoamColor ("Foam Color", Color) = (1,1,1,1)
        _FoamDepth ("Foam Edge (m)", Float) = 0.25

        [Header(Underwater)]
        _UnderwaterColor ("Underwater Tint", Color) = (0.10, 0.30, 0.40, 1)
    }

    SubShader
    {
        Tags { "RenderType"="Transparent" "Queue"="Transparent" "RenderPipeline"="UniversalPipeline" }

        Pass
        {
            Name "WaterForward"
            Tags { "LightMode"="UniversalForward" }
            ZWrite On
            Cull Off                       // two-sided: visible from below when swimming under

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 4.5

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareOpaqueTexture.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"

            TEXTURE2D(_HeightTex);    SAMPLER(sampler_HeightTex);  float4 _HeightTex_TexelSize;
            TEXTURE2D(_DetailNormal); SAMPLER(sampler_DetailNormal);

            CBUFFER_START(UnityPerMaterial)
                float  _DisplacementScale, _DisplacementClamp, _NormalScale;
                float  _DetailStrength, _DetailTiling, _DetailSpeed;
                float4 _ShallowColor, _DeepColor, _FoamColor, _UnderwaterColor;
                float  _DepthFade, _RefractStrength;
                float  _Smoothness, _FresnelPow, _SpecIntensity;
                float  _FoamDepth;
            CBUFFER_END

            struct Attributes { float4 positionOS : POSITION; float2 uv : TEXCOORD0; };
            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv         : TEXCOORD0;
                float3 positionWS : TEXCOORD1;
                float3 normalWS   : TEXCOORD2;
                float4 screenPos  : TEXCOORD3;
            };

            float SampleH(float2 uv){ return SAMPLE_TEXTURE2D_LOD(_HeightTex, sampler_HeightTex, uv, 0).r; }

            Varyings vert (Attributes IN)
            {
                Varyings OUT;
                float2 uv = IN.uv;

                float h = SampleH(uv) * _DisplacementScale;
                h = clamp(h, -_DisplacementClamp, _DisplacementClamp);
                IN.positionOS.y += h;

                float2 t = _HeightTex_TexelSize.xy;
                float hl = SampleH(uv + float2(-t.x,0));
                float hr = SampleH(uv + float2( t.x,0));
                float hu = SampleH(uv + float2(0, t.y));
                float hd = SampleH(uv + float2(0,-t.y));
                float3 n = normalize(float3((hl-hr)*_NormalScale, 1.0, (hd-hu)*_NormalScale));

                VertexPositionInputs p = GetVertexPositionInputs(IN.positionOS.xyz);
                OUT.positionCS = p.positionCS;
                OUT.positionWS = p.positionWS;
                OUT.normalWS   = TransformObjectToWorldNormal(n);
                OUT.uv         = uv;
                OUT.screenPos  = p.positionNDC;
                return OUT;
            }

            half3 DetailN(float2 worldXZ)
            {
                float2 uv1 = worldXZ * _DetailTiling + _Time.y * _DetailSpeed;
                float2 uv2 = worldXZ * _DetailTiling * 1.7 - _Time.y * _DetailSpeed * 0.7;
                half3 a = UnpackNormal(SAMPLE_TEXTURE2D(_DetailNormal, sampler_DetailNormal, uv1));
                half3 b = UnpackNormal(SAMPLE_TEXTURE2D(_DetailNormal, sampler_DetailNormal, uv2));
                return normalize(half3(a.xy + b.xy, a.z * b.z));
            }

            half4 frag (Varyings IN, FRONT_FACE_TYPE cullFace : FRONT_FACE_SEMANTIC) : SV_Target
            {
                float facing = IS_FRONT_VFACE(cullFace, 1.0, -1.0);
                float2 screenUV = IN.screenPos.xy / IN.screenPos.w;

                half3 d = DetailN(IN.positionWS.xz);
                float3 N = normalize(IN.normalWS * facing + float3(d.x, 0, d.y) * _DetailStrength);
                float3 V = normalize(GetWorldSpaceViewDir(IN.positionWS));

                // depth-based absorption
                float sceneRaw = SampleSceneDepth(screenUV);
                float sceneEye = LinearEyeDepth(sceneRaw, _ZBufferParams);
                float surfEye  = IN.screenPos.w;
                float waterDepth = max(0, sceneEye - surfEye);
                float absorb = saturate(waterDepth / max(_DepthFade, 1e-3));
                float3 waterCol = lerp(_ShallowColor.rgb, _DeepColor.rgb, absorb);

                // refraction
                float2 refrUV = screenUV + N.xz * _RefractStrength;
                float3 refr = SampleSceneColor(refrUV);
                float3 body = lerp(refr, waterCol, absorb);

                // reflection probe
                float3 R = reflect(-V, N);
                float mip = (1.0 - _Smoothness) * 6.0;
                half4 enc = SAMPLE_TEXTURECUBE_LOD(unity_SpecCube0, samplerunity_SpecCube0, R, mip);
                float3 refl = DecodeHDREnvironment(enc, unity_SpecCube0_HDR);

                float fres = pow(1.0 - saturate(dot(N, V)), _FresnelPow);
                float3 col = lerp(body, refl, fres);

                // sun specular
                Light mainLight = GetMainLight();
                float3 H = normalize(mainLight.direction + V);
                float spec = pow(saturate(dot(N, H)), exp2(_Smoothness*10)+1) * _SpecIntensity;
                col += mainLight.color * spec;

                // foam at shallow intersections
                float foam = smoothstep(0.6, 1.0, 1.0 - saturate(waterDepth/max(_FoamDepth,1e-3)));
                col = lerp(col, _FoamColor.rgb, foam);

                // when viewed from below the surface, tint underwater
                if (facing < 0) col = lerp(col, _UnderwaterColor.rgb, 0.5);

                return half4(col, 1);
            }
            ENDHLSL
        }
    }
    Fallback Off
}
