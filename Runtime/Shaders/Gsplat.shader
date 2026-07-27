// Copyright (c) 2025 Yize Wu
// SPDX-License-Identifier: MIT

Shader "Gsplat/Standard"
{
    Properties {}
    SubShader
    {
        Tags
        {
            "RenderType"="Transparent"
            "Queue"="Transparent"
        }

        Pass
        {
            ZWrite Off
            Blend One OneMinusSrcAlpha
            Cull Off

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma require compute
            #pragma multi_compile SH_BANDS_0 SH_BANDS_1 SH_BANDS_2 SH_BANDS_3 SH_BANDS_4
            #pragma multi_compile UNCOMPRESSED SPARK

            #include "UnityCG.cginc"
            #define UNITY_INDIRECT_DRAW_ARGS IndirectDrawIndexedArgs
            #include "UnityIndirect.cginc"
            #include "Gsplat.hlsl"
            #ifdef UNCOMPRESSED
            #include "GsplatUncompressed.hlsl"
            #endif
            #ifdef SPARK
            #include "GsplatSpark.hlsl"
            #endif


            bool _GammaToLinear;
            bool _UseVisibleCount;
            int _SplatCount;
            int _OriginalSplatCount;
            int _SplatInstanceSize;
            int _SHDegree;
            float4x4 _MATRIX_M;
            float _Brightness;
            float _ScaleFactor;
            StructuredBuffer<uint> _OrderBuffer;
            ByteAddressBuffer _VisibleCountBuffer;

            struct appdata
            {
                float4 vertex : POSITION;
                #if !defined(UNITY_INSTANCING_ENABLED) && !defined(UNITY_PROCEDURAL_INSTANCING_ENABLED) && !defined(UNITY_STEREO_INSTANCING_ENABLED)
                uint instanceID : SV_InstanceID;
                #endif
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            bool InitSource(appdata v, out SplatSource source)
            {
                #if !defined(UNITY_INSTANCING_ENABLED) && !defined(UNITY_PROCEDURAL_INSTANCING_ENABLED) && !defined(UNITY_STEREO_INSTANCING_ENABLED)
                source.order = v.instanceID * _SplatInstanceSize + asuint(v.vertex.z);
                #else
                source.order = unity_InstanceID * _SplatInstanceSize + asuint(v.vertex.z);
                #endif

                uint splatCount = _UseVisibleCount ? _VisibleCountBuffer.Load(0) : (uint)_SplatCount;
                if (source.order >= splatCount)
                    return false;

                source.id = _OrderBuffer[source.order];
                source.cornerUV = float2(v.vertex.x, v.vertex.y) * _ScaleFactor;
                return true;
            }

            struct v2f
            {
                float2 uv : TEXCOORD0;
                float4 vertex : SV_POSITION;
                float4 color: COLOR;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            v2f vert(appdata v)
            {
                InitIndirectDrawArgs(0);
                v2f o;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_INITIALIZE_OUTPUT(v2f, o);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                o.vertex = discardVec;

                SplatSource source;
                if (!InitSource(v, source))
                    return o;

                if ((source.id & 0x80000000u) != 0)
                {
                    uint projectedId = source.id & 0x7fffffffu;
                    ProjectedSplat projected =
                        _ProjectedSplatsBuffer[projectedId * 2 + unity_StereoEyeIndex];
                    if (projected.color.a < 0)
                        return o;
                    float clip = min(1.0, sqrt(-log(1.0 / 255.0 / projected.color.a)) / 2.0);
                    float2 uv = source.cornerUV * clip;
                    float2 offset = (source.cornerUV.x * projected.axes.xy +
                                     source.cornerUV.y * projected.axes.zw) * clip;
                    o.vertex = projected.centerProj +
                               float4(offset.x, _ProjectionParams.x * offset.y, 0, 0);
                    o.color = projected.color;
                    o.uv = uv;
                    return o;
                }

                SplatCenter center;
                SplatCorner corner;
                float4 color;
                bool isLod = source.id >= (uint)_OriginalSplatCount;
                uint dataId = isLod ? source.id - (uint)_OriginalSplatCount : source.id;
                float4x4 modelView = mul(UNITY_MATRIX_V, _MATRIX_M);
                bool initialized = isLod
                    ? InitLodSplatData(source, dataId, modelView, center, corner, color)
                    : InitSplatData(source, modelView, center, corner, color);
                if (!initialized)
                    return o;

                #ifndef SH_BANDS_0
                // calculate the model-space view direction
                float3 dir = normalize(mul(center.view, (float3x3)center.modelView));
                float3 sh[SH_COEFFS];
                if (isLod)
                    InitLodSH(dataId, sh);
                else
                    InitSH(source.id, sh);
                color.rgb += EvalSH(sh, dir, _SHDegree);
                #endif

                ClipCorner(corner, color.w);

                o.vertex = center.proj + float4(corner.offset.x, _ProjectionParams.x * corner.offset.y, 0, 0);
                o.color = color;
                o.uv = corner.uv;
                return o;
            }

            float4 frag(v2f i) : SV_Target
            {
                float A = dot(i.uv, i.uv);
                if (A > 1.0) discard;

                float2 absUV = abs(i.uv);
                float maxUV = max(absUV.x, absUV.y);

                float falloff = -exp((maxUV - _ScaleFactor * 1.16) * 25 * _ScaleFactor);
                float alpha = (exp(-A * 4.0) + falloff) * i.color.a;

                if (alpha < 1.0 / 255.0) discard;
                if (_GammaToLinear)
                    return float4(GammaToLinearSpace(i.color.rgb) * alpha * _Brightness, alpha);
                return float4(i.color.rgb * alpha * _Brightness, alpha);
            }
            ENDHLSL


        }
    }
}
