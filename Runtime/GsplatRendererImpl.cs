// Copyright (c) 2025 Yize Wu
// SPDX-License-Identifier: MIT

using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using Vector3 = UnityEngine.Vector3;

namespace Gsplat
{
    public class GsplatRendererImpl
    {
        sealed class CandidateOrderResource : ISorterResource
        {
            public GraphicsBuffer OrderBuffer { get; }
            public GraphicsBuffer InputKeys => null;
            public bool Initialized { get; set; }

            public CandidateOrderResource(GraphicsBuffer orderBuffer) => OrderBuffer = orderBuffer;
            public void Dispose() { }
        }

        public uint SplatCount { get; private set; }

        MaterialPropertyBlock m_propertyBlock;
        GsplatAsset m_gsplatAsset;
        public uint m_remainingCount = 0;
        public Bounds m_bounds;
        ulong m_gsplatAssetID;

        public GsplatResource GsplatResource;
        public GraphicsBuffer OrderBuffer { get; private set; }
        public GraphicsBuffer CutoutsBuffer { get; private set; }
        public GraphicsBuffer OrderSizeBuffer { get; private set; }
        public GraphicsBuffer BoundsBuffer { get; private set; }
        public GraphicsBuffer VisibleCountBuffer { get; private set; }
        public GraphicsBuffer SortDispatchArgs { get; private set; }
        public GraphicsBuffer DrawArgs { get; private set; }
        public ISorterResource SorterResource { get; private set; }
        public bool FrustumCullingActive { get; private set; }

        GraphicsBuffer m_candidateOrderBuffer;
        CandidateOrderResource m_candidateOrderResource;
        bool m_orderTargetsCandidateBuffer;

        static readonly int k_orderBuffer = Shader.PropertyToID("_OrderBuffer");
        static readonly int k_matrixM = Shader.PropertyToID("_MATRIX_M");
        static readonly int k_splatInstanceSize = Shader.PropertyToID("_SplatInstanceSize");
        static readonly int k_splatCount = Shader.PropertyToID("_SplatCount");
        static readonly int k_gammaToLinear = Shader.PropertyToID("_GammaToLinear");
        static readonly int k_shDegree = Shader.PropertyToID("_SHDegree");
        static readonly int k_brightness = Shader.PropertyToID("_Brightness");
        static readonly int k_scaleFactor = Shader.PropertyToID("_ScaleFactor");
        static readonly int k_useVisibleCount = Shader.PropertyToID("_UseVisibleCount");
        static readonly int k_visibleCountBuffer = Shader.PropertyToID("_VisibleCountBuffer");

        static readonly int k_candidateCount = Shader.PropertyToID("_CandidateCount");
        static readonly int k_useCandidateOrder = Shader.PropertyToID("_UseCandidateOrder");
        static readonly int k_candidateOrderBuffer = Shader.PropertyToID("_CandidateOrderBuffer");
        static readonly int k_depthBuffer = Shader.PropertyToID("_DepthBuffer");
        static readonly int k_sortDispatchArgs = Shader.PropertyToID("_SortDispatchArgs");
        static readonly int k_drawArgs = Shader.PropertyToID("_DrawArgs");
        static readonly int k_eyeCount = Shader.PropertyToID("_EyeCount");
        static readonly int k_viewportSize = Shader.PropertyToID("_ViewportSize");
        static readonly int k_matrixMv0 = Shader.PropertyToID("_MatrixMV0");
        static readonly int k_matrixMv1 = Shader.PropertyToID("_MatrixMV1");
        static readonly int k_matrixMvDepth = Shader.PropertyToID("_MatrixMVDepth");
        static readonly int k_matrixP0 = Shader.PropertyToID("_MatrixP0");
        static readonly int k_matrixP1 = Shader.PropertyToID("_MatrixP1");
        static readonly int k_indexCountPerInstance = Shader.PropertyToID("_IndexCountPerInstance");
        static readonly int k_startIndex = Shader.PropertyToID("_StartIndex");
        static readonly int k_baseVertex = Shader.PropertyToID("_BaseVertex");
        static readonly int k_positionBuffer = Shader.PropertyToID("_PositionBuffer");
        static readonly int k_scaleBuffer = Shader.PropertyToID("_ScaleBuffer");
        static readonly int k_rotationBuffer = Shader.PropertyToID("_RotationBuffer");
        static readonly int k_packedSplatsBuffer = Shader.PropertyToID("_PackedSplatsBuffer");

        int m_kernelCullClear = -1;
        int m_kernelCull = -1;
        int m_kernelBuildArgs = -1;

        uint m_framesBeforeRecomputeSort = 0;
        uint m_sortsBeforeRecomputeCutouts = 0;
        public bool ComputeSortRequired = true;
        public bool ComputeCutoutsRequired = true;
        Dictionary<ulong, (Vector3, Vector3)> m_prevCamTransforms;

        GsplatCutout.ShaderData[] m_cutoutsData;
        uint m_prevSplatCount;

        public GsplatRendererImpl(uint splatCount)
        {
            SplatCount = splatCount;
            m_prevCamTransforms = new Dictionary<ulong, (Vector3, Vector3)>();
            CreateResources(splatCount);
            CreatePropertyBlock();
        }

        public void RecreateResources(uint splatCount)
        {
            if (SplatCount == splatCount)
                return;
            Dispose();
            SplatCount = splatCount;
            CreateResources(splatCount);
            CreatePropertyBlock();
        }

        public void ComputeDepth(CommandBuffer cmd, Matrix4x4 matrixMv) =>
            m_gsplatAsset.ComputeDepth(cmd, matrixMv, SorterResource, GsplatResource);

        Bounds ExtractBounds()
        {
            uint[] boundsData = new uint[6];
            BoundsBuffer.GetData(boundsData);

            Bounds bounds = default;
            Vector3 bmin = new(GsplatUtils.SortableUintToFloat(boundsData[0]),
                GsplatUtils.SortableUintToFloat(boundsData[1]), GsplatUtils.SortableUintToFloat(boundsData[2]));
            Vector3 bmax = new(GsplatUtils.SortableUintToFloat(boundsData[3]),
                GsplatUtils.SortableUintToFloat(boundsData[4]), GsplatUtils.SortableUintToFloat(boundsData[5]));
            bounds.SetMinMax(bmin, bmax);

            if (bounds.extents.sqrMagnitude < 0.01)
                bounds.extents = new Vector3(0.1f, 0.1f, 0.1f);
            return bounds;
        }

        uint ExtractOrderSize(GraphicsBuffer orderBuffer)
        {
            GraphicsBuffer.CopyCount(orderBuffer, OrderSizeBuffer, 0);
            uint[] count = new uint[1];
            OrderSizeBuffer.GetData(count);
            return count[0];
        }

        public void SetFrustumCulling(bool active)
        {
            if (FrustumCullingActive == active)
                return;

            if (active)
                EnsureCullingResources();
            FrustumCullingActive = active;
            SorterResource.Initialized = false;
            if (m_candidateOrderResource != null)
                m_candidateOrderResource.Initialized = false;
            m_prevSplatCount = uint.MaxValue;
            ForceRefresh();
        }

        public void DispatchInitOrder(GsplatCutout[] cutouts, Matrix4x4 matrixWorld, bool cutoutsUpdateBounds)
        {
            if (cutouts.Length == 0)
            {
                if (m_cutoutsData.Length > 0)
                    SorterResource.Initialized = false;
                m_cutoutsData = Array.Empty<GsplatCutout.ShaderData>();
                m_remainingCount = GsplatResource.UploadedCount;
                m_bounds = m_gsplatAsset.Bounds;
                return;
            }

            if (!ComputeCutoutsRequired)
                return;

            SorterResource.Initialized = true;

            var cutoutsUnchanged = m_cutoutsData.Length == cutouts.Length;
            var updatedCutoutsData = new GsplatCutout.ShaderData[cutouts.Length];
            for (int i = 0; i != cutouts.Length; i++)
            {
                updatedCutoutsData[i] = cutouts[i].GetShaderData(matrixWorld);
                if (cutoutsUnchanged)
                    if (updatedCutoutsData[i].matrix != m_cutoutsData[i].matrix ||
                        updatedCutoutsData[i].typeAndFlags != m_cutoutsData[i].typeAndFlags)
                        cutoutsUnchanged = false;
            }

            if (cutoutsUnchanged && m_prevSplatCount == GsplatResource.UploadedCount &&
                m_orderTargetsCandidateBuffer == FrustumCullingActive)
                return;

            m_prevSplatCount = GsplatResource.UploadedCount;
            m_cutoutsData = updatedCutoutsData;
            CutoutsBuffer = m_gsplatAsset.UpdateCutoutsBuffer(CutoutsBuffer, m_cutoutsData);
            if (cutoutsUpdateBounds)
                m_gsplatAsset.UpdateBoundsBuffer(BoundsBuffer);
            var orderTarget = FrustumCullingActive ? m_candidateOrderResource : SorterResource;
            m_gsplatAsset.InitOrder(orderTarget, GsplatResource, cutoutsUpdateBounds);
            m_remainingCount = ExtractOrderSize(orderTarget.OrderBuffer);
            m_bounds = cutoutsUpdateBounds ? ExtractBounds() : m_gsplatAsset.Bounds;
            m_orderTargetsCandidateBuffer = FrustumCullingActive;
        }

        public void Cull(CommandBuffer cmd, Camera camera, Transform transform)
        {
            var cs = m_gsplatAsset.GsplatMaterial.FrustumCullShader;

            bool stereo = camera.stereoEnabled;
            Matrix4x4 matrixM = transform.localToWorldMatrix;
            Matrix4x4 matrixMvDepth = camera.worldToCameraMatrix * matrixM;
            Matrix4x4 matrixMv0 = (stereo
                ? camera.GetStereoViewMatrix(Camera.StereoscopicEye.Left)
                : camera.worldToCameraMatrix) * matrixM;
            Matrix4x4 matrixP0 = GL.GetGPUProjectionMatrix(stereo
                ? camera.GetStereoProjectionMatrix(Camera.StereoscopicEye.Left)
                : camera.projectionMatrix, false);
            Matrix4x4 matrixMv1 = stereo
                ? camera.GetStereoViewMatrix(Camera.StereoscopicEye.Right) * matrixM
                : matrixMv0;
            Matrix4x4 matrixP1 = stereo
                ? GL.GetGPUProjectionMatrix(camera.GetStereoProjectionMatrix(Camera.StereoscopicEye.Right), false)
                : matrixP0;

            cmd.SetComputeIntParam(cs, k_candidateCount, (int)m_remainingCount);
            cmd.SetComputeIntParam(cs, k_useCandidateOrder, m_cutoutsData.Length > 0 ? 1 : 0);
            cmd.SetComputeIntParam(cs, k_eyeCount, stereo ? 2 : 1);
            cmd.SetComputeVectorParam(cs, k_viewportSize,
                new Vector4(Math.Max(1, camera.pixelWidth), Math.Max(1, camera.pixelHeight), 0, 0));
            cmd.SetComputeMatrixParam(cs, k_matrixMv0, matrixMv0);
            cmd.SetComputeMatrixParam(cs, k_matrixMv1, matrixMv1);
            cmd.SetComputeMatrixParam(cs, k_matrixMvDepth, matrixMvDepth);
            cmd.SetComputeMatrixParam(cs, k_matrixP0, matrixP0);
            cmd.SetComputeMatrixParam(cs, k_matrixP1, matrixP1);
            cmd.SetComputeBufferParam(cs, m_kernelCullClear, k_visibleCountBuffer, VisibleCountBuffer);
            cmd.DispatchCompute(cs, m_kernelCullClear, 1, 1, 1);

            if (m_remainingCount > 0)
            {
                cmd.SetComputeBufferParam(cs, m_kernelCull, k_candidateOrderBuffer, m_candidateOrderBuffer);
                cmd.SetComputeBufferParam(cs, m_kernelCull, k_orderBuffer, SorterResource.OrderBuffer);
                cmd.SetComputeBufferParam(cs, m_kernelCull, k_depthBuffer, SorterResource.InputKeys);
                cmd.SetComputeBufferParam(cs, m_kernelCull, k_visibleCountBuffer, VisibleCountBuffer);
                if (GsplatResource is GsplatResourceUncompressed uncompressed)
                {
                    cmd.SetComputeBufferParam(cs, m_kernelCull, k_positionBuffer, uncompressed.PositionBuffer);
                    cmd.SetComputeBufferParam(cs, m_kernelCull, k_scaleBuffer, uncompressed.ScaleBuffer);
                    cmd.SetComputeBufferParam(cs, m_kernelCull, k_rotationBuffer, uncompressed.RotationBuffer);
                }
                else if (GsplatResource is GsplatResourceSpark spark)
                    cmd.SetComputeBufferParam(cs, m_kernelCull, k_packedSplatsBuffer, spark.PackedSplatsBuffer);
                cmd.DispatchCompute(cs, m_kernelCull, (int)GsplatUtils.DivRoundUp(m_remainingCount, 256), 1, 1);
            }

            var mesh = GsplatSettings.Instance.Mesh;
            cmd.SetComputeIntParam(cs, k_splatInstanceSize, (int)GsplatSettings.Instance.SplatInstanceSize);
            cmd.SetComputeIntParam(cs, k_indexCountPerInstance, (int)mesh.GetIndexCount(0));
            cmd.SetComputeIntParam(cs, k_startIndex, (int)mesh.GetIndexStart(0));
            cmd.SetComputeIntParam(cs, k_baseVertex, (int)mesh.GetBaseVertex(0));
            cmd.SetComputeBufferParam(cs, m_kernelBuildArgs, k_visibleCountBuffer, VisibleCountBuffer);
            cmd.SetComputeBufferParam(cs, m_kernelBuildArgs, k_sortDispatchArgs, SortDispatchArgs);
            cmd.SetComputeBufferParam(cs, m_kernelBuildArgs, k_drawArgs, DrawArgs);
            cmd.DispatchCompute(cs, m_kernelBuildArgs, 1, 1, 1);
        }

        public void BindGsplatAsset(GsplatAsset gsplatAsset, bool asyncUpload = false)
        {
            Debug.Assert(m_gsplatAssetID == 0);
            m_gsplatAssetID = GsplatUtils.GetObjectId(gsplatAsset);
            m_gsplatAsset = gsplatAsset;
            GsplatResource = GsplatResourceManager.Get(gsplatAsset);
            gsplatAsset.SetupMaterialPropertyBlock(m_propertyBlock, GsplatResource);
            if (FrustumCullingActive)
                CacheCullingKernels();
            if (asyncUpload)
                gsplatAsset.UploadDataAsync(GsplatResource);
            else
                gsplatAsset.UploadData(GsplatResource);
        }

        public void ReleaseGsplatAsset()
        {
            GsplatResourceManager.Release(m_gsplatAssetID);
            GsplatResource = null;
            m_gsplatAsset = null;
            m_gsplatAssetID = 0;
        }

        void CreateResources(uint splatCount)
        {
            OrderBuffer = new GraphicsBuffer(GraphicsBuffer.Target.Append, (int)splatCount, sizeof(uint));
            SorterResource = GsplatSorter.Instance.CreateSorterResource(splatCount, OrderBuffer);
            m_cutoutsData = Array.Empty<GsplatCutout.ShaderData>();
            CutoutsBuffer = null;
            OrderSizeBuffer = new GraphicsBuffer(GraphicsBuffer.Target.IndirectArguments, 1, sizeof(uint));
            BoundsBuffer = new GraphicsBuffer(GraphicsBuffer.Target.Structured, 6, sizeof(uint));
            VisibleCountBuffer = new GraphicsBuffer(GraphicsBuffer.Target.Raw, 1, sizeof(uint));
            VisibleCountBuffer.SetData(new uint[1]);
        }

        void CreatePropertyBlock()
        {
            m_propertyBlock ??= new MaterialPropertyBlock();
            m_propertyBlock.SetBuffer(k_orderBuffer, OrderBuffer);
            m_propertyBlock.SetBuffer(k_visibleCountBuffer, VisibleCountBuffer);
        }

        void EnsureCullingResources()
        {
            if (m_candidateOrderBuffer != null)
                return;

            m_candidateOrderBuffer = new GraphicsBuffer(GraphicsBuffer.Target.Append, (int)SplatCount, sizeof(uint));
            m_candidateOrderResource = new CandidateOrderResource(m_candidateOrderBuffer);
            SortDispatchArgs = new GraphicsBuffer(GraphicsBuffer.Target.IndirectArguments, 1, sizeof(uint) * 3);
            DrawArgs = new GraphicsBuffer(GraphicsBuffer.Target.IndirectArguments, 1,
                GraphicsBuffer.IndirectDrawIndexedArgs.size);
            DrawArgs.SetData(new GraphicsBuffer.IndirectDrawIndexedArgs[1]);
            CacheCullingKernels();
        }

        void CacheCullingKernels()
        {
            var cs = m_gsplatAsset.GsplatMaterial.FrustumCullShader;
            m_kernelCullClear = cs.FindKernel("Clear");
            m_kernelCull = cs.FindKernel(GsplatResource is GsplatResourceSpark ? "CullSpark" : "CullUncompressed");
            m_kernelBuildArgs = cs.FindKernel("BuildArgs");
        }

        public void Dispose()
        {
            FrustumCullingActive = false;
            ReleaseGsplatAsset();
            OrderBuffer?.Dispose();
            OrderBuffer = null;
            SorterResource?.Dispose();
            SorterResource = null;
            m_candidateOrderBuffer?.Dispose();
            m_candidateOrderBuffer = null;
            m_candidateOrderResource = null;
            VisibleCountBuffer?.Dispose();
            VisibleCountBuffer = null;
            SortDispatchArgs?.Dispose();
            SortDispatchArgs = null;
            DrawArgs?.Dispose();
            DrawArgs = null;
            CutoutsBuffer?.Dispose();
            CutoutsBuffer = null;
            OrderSizeBuffer?.Dispose();
            OrderSizeBuffer = null;
            BoundsBuffer?.Dispose();
            BoundsBuffer = null;
        }

        public void ForceRefresh()
        {
            m_framesBeforeRecomputeSort = 0;
            m_sortsBeforeRecomputeCutouts = 0;
        }

        public void RefreshOnCameraMove()
        {
            foreach (var cam in Camera.allCameras)
            {
                var id = GsplatUtils.GetObjectId(cam);
                if (m_prevCamTransforms.TryGetValue(id, out (Vector3, Vector3) prevCamTransform))
                {
                    (Vector3 prevCamPos, Vector3 prevCamRot) = prevCamTransform;

                    if ((cam.transform.position - prevCamPos).magnitude >
                        GsplatSettings.Instance.CameraTranslationRefreshTreshold
                        || (cam.transform.eulerAngles - prevCamRot).magnitude >
                        GsplatSettings.Instance.CameraRotationRefreshTreshold)
                    {
                        m_prevCamTransforms[id] = (cam.transform.position, cam.transform.eulerAngles);
                        ForceRefresh();
                    }
                }
                else
                {
                    m_prevCamTransforms.Add(id, (cam.transform.position, cam.transform.eulerAngles));
                    ForceRefresh();
                }
            }
        }

        public void EvaluateRefreshRequired(GsplatRenderer.GsplatSortMode mode, uint sortRefreshRate,
            uint cutoutsRefreshRate)
        {
            if (mode == GsplatRenderer.GsplatSortMode.Always)
            {
                sortRefreshRate = 0;
                cutoutsRefreshRate = 0;
            }

            if (mode == GsplatRenderer.GsplatSortMode.SortEveryNFrames)
            {
                cutoutsRefreshRate = 0;
            }

            RefreshOnCameraMove();

            ComputeSortRequired = false;
            ComputeCutoutsRequired = false;

            if (m_framesBeforeRecomputeSort == 0)
            {
                m_framesBeforeRecomputeSort = sortRefreshRate;
                ComputeSortRequired = true;
                if (m_sortsBeforeRecomputeCutouts == 0)
                {
                    m_sortsBeforeRecomputeCutouts = cutoutsRefreshRate;
                    ComputeCutoutsRequired = true;
                }
                else
                    m_sortsBeforeRecomputeCutouts -= 1;
            }
            else
                m_framesBeforeRecomputeSort -= 1;
        }

        /// <summary>
        /// Render the splats.
        /// </summary>
        /// <param name="transform">Object transform.</param>
        /// <param name="layer">Layer used for rendering.</param>
        /// <param name="gammaToLinear">Covert color space from Gamma to Linear.</param>
        /// <param name="shDegree">Order of SH coefficients used for rendering. The final value is capped by the SHBands property.</param>
        /// <param name="brightness">Brightness color scaling.</param>
        /// <param name="scaleFactor">Splats uv scaling factor, reduce splat size while trying to keep visual fidelity.</param>
        /// <param name="renderOrder">Manual render order placement of the gsplat. The final value is capped by the maximum render order setting.</param>
        public void Render(Transform transform, int layer, bool gammaToLinear = false, int shDegree = 3,
            float brightness = 1.0f, float scaleFactor = 1.0f, uint renderOrder = 0)
        {
            if (m_remainingCount <= 0)
                return;

            m_propertyBlock.SetInteger(k_splatCount, (int)m_remainingCount);
            m_propertyBlock.SetInteger(k_gammaToLinear, gammaToLinear ? 1 : 0);
            m_propertyBlock.SetInteger(k_splatInstanceSize, (int)GsplatSettings.Instance.SplatInstanceSize);
            m_propertyBlock.SetInteger(k_shDegree, Math.Min(m_gsplatAsset.SHBands, shDegree));
            m_propertyBlock.SetFloat(k_brightness, brightness);
            m_propertyBlock.SetFloat(k_scaleFactor, scaleFactor);
            m_propertyBlock.SetMatrix(k_matrixM, transform.localToWorldMatrix);
            m_propertyBlock.SetInteger(k_useVisibleCount, FrustumCullingActive ? 1 : 0);

            uint order = Math.Clamp(renderOrder, 0, GsplatSettings.Instance.MaxRenderOrder - 1);
            var rp = new RenderParams(m_gsplatAsset.Materials[order])
            {
                worldBounds = GsplatUtils.CalcWorldBounds(m_bounds, transform),
                matProps = m_propertyBlock,
                layer = layer
            };

            if (FrustumCullingActive)
            {
                rp.worldBounds = new Bounds(transform.position, Vector3.one * 1e6f);
                Graphics.RenderMeshIndirect(rp, GsplatSettings.Instance.Mesh, DrawArgs);
            }
            else
                Graphics.RenderMeshPrimitives(rp, GsplatSettings.Instance.Mesh, 0,
                    Mathf.CeilToInt(m_remainingCount / (float)GsplatSettings.Instance.SplatInstanceSize));
        }
    }
}
