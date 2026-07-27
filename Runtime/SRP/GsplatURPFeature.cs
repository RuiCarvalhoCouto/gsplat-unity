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
using UnityEngine.Experimental.Rendering;
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
                public bool AdaptiveRendering;
                public TextureHandle Color;
                public TextureHandle Depth;
                public int Width;
                public int Height;
            }

            class CompositePassData
            {
                public TextureHandle Color;
                public TextureHandle Depth;
                public TextureHandle CameraDepth;
                public Material Material;
                public Vector4 DepthTexelSize;
            }

            public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
            {
                var resourceData = frameData.Get<UniversalResourceData>();
                bool adaptiveRendering = GsplatSorter.Instance.HasAdaptiveRendering &&
                                         GsplatSettings.Instance.AdaptiveUpscaleMaterial &&
                                         resourceData.activeColorTexture.IsValid() &&
                                         resourceData.cameraDepthTexture.IsValid();
                TextureHandle adaptiveColor = default;
                TextureHandle adaptiveDepth = default;
                int width = 0;
                int height = 0;
                if (adaptiveRendering)
                {
                    float scale = GsplatSorter.Instance.AdaptiveResolutionScale;
                    var colorDescriptor = resourceData.activeColorTexture.GetDescriptor(renderGraph);
                    width = Mathf.Max(1, Mathf.RoundToInt(colorDescriptor.width * scale));
                    height = Mathf.Max(1, Mathf.RoundToInt(colorDescriptor.height * scale));
                    colorDescriptor.name = "GsplatAdaptiveColor";
                    colorDescriptor.width = width;
                    colorDescriptor.height = height;
                    colorDescriptor.msaaSamples = MSAASamples.None;
                    colorDescriptor.depthBufferBits = DepthBits.None;
                    colorDescriptor.clearBuffer = false;
                    adaptiveColor = renderGraph.CreateTexture(colorDescriptor);

                    var depthDescriptor = colorDescriptor;
                    depthDescriptor.name = "GsplatAdaptiveDepth";
                    depthDescriptor.colorFormat = GraphicsFormat.None;
                    depthDescriptor.depthBufferBits = DepthBits.Depth32;
                    adaptiveDepth = renderGraph.CreateTexture(depthDescriptor);
                }

                using (var builder =
                       renderGraph.AddUnsafePass(GsplatSorter.k_passName, out PassData passData))
                {
                    passData.CameraData = frameData.Get<UniversalCameraData>();
                    passData.AdaptiveRendering = adaptiveRendering;
                    passData.Color = adaptiveColor;
                    passData.Depth = adaptiveDepth;
                    passData.Width = width;
                    passData.Height = height;
                    if (adaptiveRendering)
                    {
                        builder.UseTexture(adaptiveColor, AccessFlags.WriteAll);
                        builder.UseTexture(adaptiveDepth, AccessFlags.WriteAll);
                    }
                    builder.AllowPassCulling(false);
                    builder.SetRenderFunc(static (PassData data, UnsafeGraphContext context) =>
                    {
                        var commandBuffer = CommandBufferHelpers.GetNativeCommandBuffer(context.cmd);
                        GsplatSorter.Instance.DispatchSort(commandBuffer, GetCameraInfo(data.CameraData));
                        if (!data.AdaptiveRendering)
                            return;
                        CoreUtils.SetRenderTarget(commandBuffer, data.Color, data.Depth, ClearFlag.All,
                            Color.clear);
                        commandBuffer.SetViewport(new Rect(0, 0, data.Width, data.Height));
                        GsplatSorter.Instance.DrawAdaptive(commandBuffer);
                    });
                }

                if (!adaptiveRendering)
                    return;

                using (var compositeBuilder = renderGraph.AddRasterRenderPass(
                           "Gsplat.DepthAwareUpscale", out CompositePassData compositeData))
                {
                    compositeData.Color = adaptiveColor;
                    compositeData.Depth = adaptiveDepth;
                    compositeData.CameraDepth = resourceData.cameraDepthTexture;
                    compositeData.Material = GsplatSettings.Instance.AdaptiveUpscaleMaterial;
                    compositeData.DepthTexelSize =
                        new Vector4(1.0f / width, 1.0f / height, width, height);
                    compositeBuilder.UseTexture(adaptiveColor, AccessFlags.Read);
                    compositeBuilder.UseTexture(adaptiveDepth, AccessFlags.Read);
                    compositeBuilder.UseTexture(resourceData.cameraDepthTexture, AccessFlags.Read);
                    compositeBuilder.SetRenderAttachment(resourceData.activeColorTexture, 0,
                        AccessFlags.ReadWrite);
                    compositeBuilder.AllowPassCulling(false);
                    compositeBuilder.AllowGlobalStateModification(true);
                    compositeBuilder.SetRenderFunc(
                        static (CompositePassData data, RasterGraphContext context) =>
                        {
                            context.cmd.SetGlobalTexture("_GsplatDepthTexture", data.Depth);
                            context.cmd.SetGlobalTexture("_GsplatCameraDepthTexture", data.CameraDepth);
                            context.cmd.SetGlobalVector("_GsplatDepthTexture_TexelSize",
                                data.DepthTexelSize);
                            Blitter.BlitTexture(context.cmd, data.Color, Vector4.one, data.Material, 0);
                        });
                }
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
            m_pass.ConfigureInput(ScriptableRenderPassInput.Depth);
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
