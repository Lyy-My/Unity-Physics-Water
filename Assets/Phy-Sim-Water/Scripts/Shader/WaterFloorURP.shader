// WaterFloorURP.shader  (URP)
// Pool / lakebed / seabed floor: albedo map (tiles or sand) + animated caustics.
// Render this as normal opaque geometry UNDER the water so it shows through refraction.
Shader "Water/FloorURP"
{
    Properties
    {
        [Header(Base)]
        _BaseMap   ("Albedo (tiles / sand)", 2D) = "white" {}
        _BaseColor ("Tint", Color) = (1,1,1,1)
        _BaseTiling("Albedo Tiling", Float) = 1.0

        [Header(Caustics)]
        _Caustics        ("Caustics", 2D) = "black" {}
        _CausticsColor   ("Caustics Color", Color) = (1, 0.96, 0.85, 1)
        _CausticsTiling  ("Caustics Tiling", Float) = 0.35
        _CausticsSpeed   ("Caustics Speed", Float) = 0.05
        _CausticsStrength("Caustics Strength", Float) = 1.2
    }

    SubShader
    {
        Tags { "RenderType"="Opaque" "Queue"="Geometry" "RenderPipeline"="UniversalPipeline" }

        Pass
        {
            Name "FloorForward"
            Tags { "LightMode"="UniversalForward" }

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            TEXTURE2D(_BaseMap);  SAMPLER(sampler_BaseMap);
            TEXTURE2D(_Caustics); SAMPLER(sampler_Caustics);

            CBUFFER_START(UnityPerMaterial)
                float4 _BaseColor, _CausticsColor;
                float  _BaseTiling, _CausticsTiling, _CausticsSpeed, _CausticsStrength;
            CBUFFER_END

            struct Attributes { float4 positionOS : POSITION; float3 normalOS : NORMAL; float2 uv : TEXCOORD0; };
            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float3 normalWS   : TEXCOORD1;
            };

            Varyings vert (Attributes IN)
            {
                Varyings OUT;
                VertexPositionInputs p = GetVertexPositionInputs(IN.positionOS.xyz);
                OUT.positionCS = p.positionCS;
                OUT.positionWS = p.positionWS;
                OUT.normalWS   = TransformObjectToWorldNormal(IN.normalOS);
                return OUT;
            }

            half4 frag (Varyings IN) : SV_Target
            {
                float2 wxz = IN.positionWS.xz;

                float3 albedo = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, wxz * _BaseTiling).rgb * _BaseColor.rgb;

                // animated caustics: two scrolled layers, min() gives the shifting focus
                float2 cuv = wxz * _CausticsTiling;
                float c1 = SAMPLE_TEXTURE2D(_Caustics, sampler_Caustics, cuv + _Time.y * _CausticsSpeed).r;
                float c2 = SAMPLE_TEXTURE2D(_Caustics, sampler_Caustics, cuv * 1.3 - _Time.y * _CausticsSpeed * 0.8).r;
                float caustic = min(c1, c2) * _CausticsStrength;

                Light mainLight = GetMainLight();
                float ndotl = saturate(dot(normalize(IN.normalWS), mainLight.direction));
                float3 lit = albedo * (mainLight.color * ndotl + SampleSH(IN.normalWS));

                lit += _CausticsColor.rgb * caustic * (0.3 + 0.7 * ndotl);
                return half4(lit, 1);
            }
            ENDHLSL
        }
    }
    Fallback "Universal Render Pipeline/Lit"
}
