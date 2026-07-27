// Copyright (c) 2026 Rui Couto
// SPDX-License-Identifier: MIT

Shader "Hidden/Gsplat/DepthAwareUpscale"
{
    SubShader
    {
        Tags { "RenderPipeline" = "UniversalPipeline" }

        Pass
        {
            ZTest Always
            ZWrite Off
            Cull Off
            Blend One OneMinusSrcAlpha

            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex Vert
            #pragma fragment Frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"

            TEXTURE2D_X_FLOAT(_GsplatDepthTexture);
            TEXTURE2D_X_FLOAT(_GsplatCameraDepthTexture);
            float4 _GsplatDepthTexture_TexelSize;

            float4 Frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                float2 uv = input.texcoord;
                float sceneDepth = SAMPLE_TEXTURE2D_X_LOD(
                    _GsplatCameraDepthTexture, sampler_PointClamp, uv, 0).r;
                float sceneEyeDepth = LinearEyeDepth(sceneDepth, _ZBufferParams);
                float2 pixel = uv * _GsplatDepthTexture_TexelSize.zw - 0.5;
                float2 fraction = frac(pixel);
                float2 basePixel = floor(pixel);
                float2 offsets[4] =
                {
                    float2(0.5, 0.5),
                    float2(1.5, 0.5),
                    float2(0.5, 1.5),
                    float2(1.5, 1.5)
                };
                float weights[4] =
                {
                    (1.0 - fraction.x) * (1.0 - fraction.y),
                    fraction.x * (1.0 - fraction.y),
                    (1.0 - fraction.x) * fraction.y,
                    fraction.x * fraction.y
                };

                float4 colors[4];
                float eyeDepths[4];
                bool valid[4];
                float nearestEyeDepth = 3.402823466e+38;
                [unroll]
                for (uint i = 0; i < 4; ++i)
                {
                    float2 sampleUv = saturate(
                        (basePixel + offsets[i]) * _GsplatDepthTexture_TexelSize.xy);
                    colors[i] = SAMPLE_TEXTURE2D_X_LOD(_BlitTexture, sampler_PointClamp, sampleUv, 0);
                    float depth = SAMPLE_TEXTURE2D_X_LOD(
                        _GsplatDepthTexture, sampler_PointClamp, sampleUv, 0).r;
                    eyeDepths[i] = LinearEyeDepth(depth, _ZBufferParams);
                    valid[i] = colors[i].a > 0.0 &&
                               eyeDepths[i] <= sceneEyeDepth + max(0.01, sceneEyeDepth * 1e-4);
                    if (valid[i])
                        nearestEyeDepth = min(nearestEyeDepth, eyeDepths[i]);
                }

                if (nearestEyeDepth == 3.402823466e+38)
                    return 0;

                float depthTolerance = max(0.02, nearestEyeDepth * 0.02);
                float4 color = 0;
                float weight = 0;
                [unroll]
                for (uint i = 0; i < 4; ++i)
                {
                    float accepted = valid[i] && abs(eyeDepths[i] - nearestEyeDepth) <= depthTolerance
                        ? weights[i]
                        : 0.0;
                    color += colors[i] * accepted;
                    weight += accepted;
                }
                return weight > 1e-6 ? color / weight : 0;
            }
            ENDHLSL
        }
    }
}
