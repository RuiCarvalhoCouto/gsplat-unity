// Copyright (c) 2026 Rui Couto
// SPDX-License-Identifier: MIT

Shader "Hidden/Gsplat/ProjectedOffscreen"
{
    SubShader
    {
        Tags { "RenderPipeline" = "UniversalPipeline" }

        Pass
        {
            ZTest Always
            ZWrite On
            Blend One OneMinusSrcAlpha
            Cull Off

            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma require compute

            #include "UnityCG.cginc"
            #define UNITY_INDIRECT_DRAW_ARGS IndirectDrawIndexedArgs
            #include "UnityIndirect.cginc"

            int _SplatInstanceSize;
            float _Brightness;
            float _ScaleFactor;
            bool _GammaToLinear;
            StructuredBuffer<uint> _OrderBuffer;
            ByteAddressBuffer _VisibleCountBuffer;

            struct ProjectedSplat
            {
                float4 centerProj;
                float4 axes;
                float4 color;
            };
            StructuredBuffer<ProjectedSplat> _ProjectedSplatsBuffer;

            struct Attributes
            {
                float4 vertex : POSITION;
                #if !defined(UNITY_INSTANCING_ENABLED) && !defined(UNITY_PROCEDURAL_INSTANCING_ENABLED) && !defined(UNITY_STEREO_INSTANCING_ENABLED)
                uint instanceID : SV_InstanceID;
                #endif
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float2 uv : TEXCOORD0;
                float4 positionCS : SV_POSITION;
                float4 color : COLOR;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            Varyings Vert(Attributes input)
            {
                InitIndirectDrawArgs(0);
                UNITY_SETUP_INSTANCE_ID(input);
                Varyings output;
                UNITY_INITIALIZE_OUTPUT(Varyings, output);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);
                output.positionCS = float4(0, 0, 2, 1);

                #if !defined(UNITY_INSTANCING_ENABLED) && !defined(UNITY_PROCEDURAL_INSTANCING_ENABLED) && !defined(UNITY_STEREO_INSTANCING_ENABLED)
                uint instanceId = input.instanceID;
                #else
                uint instanceId = unity_InstanceID;
                #endif
                uint order = instanceId * (uint)_SplatInstanceSize + asuint(input.vertex.z);
                if (order >= _VisibleCountBuffer.Load(0))
                    return output;

                uint projectedId = _OrderBuffer[order] & 0x7fffffffu;
                ProjectedSplat projected =
                    _ProjectedSplatsBuffer[projectedId * 2 + unity_StereoEyeIndex];
                if (projected.color.a < 0)
                    return output;

                float clip = min(1.0, sqrt(-log(1.0 / 255.0 / projected.color.a)) / 2.0);
                float2 corner = input.vertex.xy * _ScaleFactor;
                float2 offset =
                    (corner.x * projected.axes.xy + corner.y * projected.axes.zw) * clip;
                output.positionCS = projected.centerProj +
                                    float4(offset.x, _ProjectionParams.x * offset.y, 0, 0);
                output.color = projected.color;
                output.uv = corner * clip;
                return output;
            }

            float4 Frag(Varyings input) : SV_Target
            {
                float a = dot(input.uv, input.uv);
                if (a > 1.0)
                    discard;
                float maxUv = max(abs(input.uv.x), abs(input.uv.y));
                float falloff = -exp((maxUv - _ScaleFactor * 1.16) * 25 * _ScaleFactor);
                float alpha = (exp(-a * 4.0) + falloff) * input.color.a;
                if (alpha < 1.0 / 255.0)
                    discard;
                float3 color = _GammaToLinear ? GammaToLinearSpace(input.color.rgb) : input.color.rgb;
                return float4(color * alpha * _Brightness, alpha);
            }
            ENDHLSL
        }
    }
}
