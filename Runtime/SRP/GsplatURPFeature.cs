// Originated from the GaussianSplatHDRPPass in aras-p/UnityGaussianSplatting by Aras Pranckevičius
// https://github.com/aras-p/UnityGaussianSplatting/blob/main/package/Runtime/GaussianSplatHDRPPass.cs
// Copyright (c) 2023 Aras Pranckevičius
// Modified by Yize Wu
// Copyright (c) 2025 Yize Wu
// SPDX-License-Identifier: MIT

#if GSPLAT_ENABLE_URP

using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
#if UNITY_6000_0_OR_NEWER
using UnityEngine.Rendering.RenderGraphModule;
#endif

namespace Gsplat
{
    class GsplatURPFeature : ScriptableRendererFeature
    {
        class GsplatRenderPass : ScriptableRenderPass
        {
#if UNITY_6000_0_OR_NEWER
            class PassData
            {
                public UniversalCameraData CameraData;
            }

            public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
            {
                using var builder = renderGraph.AddUnsafePass(GsplatSorter.k_passName, out PassData passData);
                passData.CameraData = frameData.Get<UniversalCameraData>();
                builder.AllowPassCulling(false);
                builder.SetRenderFunc(static (PassData data, UnsafeGraphContext context) =>
                {
                    var commandBuffer = CommandBufferHelpers.GetNativeCommandBuffer(context.cmd);
                    GsplatSorter.Instance.DispatchSort(commandBuffer, GetCameraInfo(data.CameraData));
                });
            }

            static GsplatCameraInfo GetCameraInfo(UniversalCameraData cameraData)
            {
                if (!cameraData.xr.enabled)
                    return new GsplatCameraInfo(cameraData.camera, true);

                Rect viewport = cameraData.xr.GetViewport(0);
                if (cameraData.xr.singlePassEnabled)
                {
                    var cullingParams = cameraData.xr.cullingParams;
                    Matrix4x4 view = cullingParams.stereoViewMatrix;
                    Matrix4x4 projection = GL.GetGPUProjectionMatrix(cullingParams.stereoProjectionMatrix, false);
                    Matrix4x4 renderView0 = cameraData.GetViewMatrix(0);
                    Matrix4x4 renderProjection0 =
                        GL.GetGPUProjectionMatrix(cameraData.GetProjectionMatrix(0), false);
                    Matrix4x4 renderView1 = cameraData.GetViewMatrix(1);
                    Matrix4x4 renderProjection1 =
                        GL.GetGPUProjectionMatrix(cameraData.GetProjectionMatrix(1), false);
                    return new GsplatCameraInfo(cameraData.camera, 1, viewport.size, view, projection,
                        view, projection, 2, renderView0, renderProjection0, renderView1,
                        renderProjection1, true);
                }

                Matrix4x4 view0 = cameraData.GetViewMatrix(0);
                Matrix4x4 projection0 = GL.GetGPUProjectionMatrix(cameraData.GetProjectionMatrix(0), false);
                return new GsplatCameraInfo(cameraData.camera, 1, viewport.size, view0, projection0,
                    view0, projection0, true);
            }
#else
            public CommandBuffer CommandBuffer;
            public override void Execute(ScriptableRenderContext context, ref RenderingData renderingData)
            {
                GsplatSorter.Instance.DispatchSortUrp(CommandBuffer, renderingData.cameraData.camera);
                context.ExecuteCommandBuffer(CommandBuffer);
            }
#endif
        }

        GsplatRenderPass m_pass;
        bool m_hasGsplats;

        public override void Create()
        {
            m_pass = new GsplatRenderPass { renderPassEvent = RenderPassEvent.BeforeRenderingTransparents };
        }

        public override void OnCameraPreCull(ScriptableRenderer renderer, in CameraData cameraData)
        {
            m_hasGsplats = GsplatSorter.Instance.GatherGsplatsForCamera(cameraData.camera);
#if !UNITY_6000_0_OR_NEWER
            m_pass.CommandBuffer ??= new CommandBuffer { name = "SortGsplats" };
            m_pass.CommandBuffer.Clear();
#endif
        }

        public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
        {
            if (GsplatSorter.Instance.Valid && GsplatSettings.Instance.Valid && m_hasGsplats)
                renderer.EnqueuePass(m_pass);
        }

        protected override void Dispose(bool disposing)
        {
#if !UNITY_6000_0_OR_NEWER
            m_pass.CommandBuffer?.Dispose();
            m_pass.CommandBuffer = null;
#endif
            m_pass = null;
        }
    }
}

#endif
