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
        public bool HierarchicalCullingActive => FrustumCullingActive &&
                                                 GsplatResource is { HasSpatialHierarchy: true };
        public GraphicsBuffer HierarchyCountsBuffer => m_hierarchyCountsBuffer;

        GraphicsBuffer m_candidateOrderBuffer;
        CandidateOrderResource m_candidateOrderResource;
        GraphicsBuffer m_activeMaskBuffer;
        GraphicsBuffer m_activeCountBuffer;
        GraphicsBuffer m_dummyActiveMaskBuffer;
        GraphicsBuffer m_visibleCoarseNodes;
        GraphicsBuffer m_fullLeafNodes;
        GraphicsBuffer m_intersectLeafNodes;
        GraphicsBuffer m_hierarchyCountsBuffer;
        GraphicsBuffer m_hierarchyDispatchArgs;
        int m_orderInitializationMode;
        bool m_useActiveMask;

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
        static readonly int k_activeMaskBuffer = Shader.PropertyToID("_ActiveMaskBuffer");
        static readonly int k_useActiveMask = Shader.PropertyToID("_UseActiveMask");
        static readonly int k_uploadedCount = Shader.PropertyToID("_UploadedCount");
        static readonly int k_leafCount = Shader.PropertyToID("_LeafCount");
        static readonly int k_coarseCount = Shader.PropertyToID("_CoarseCount");
        static readonly int k_spatialChunkSize = Shader.PropertyToID("_SpatialChunkSize");
        static readonly int k_groupsPerLeaf = Shader.PropertyToID("_GroupsPerLeaf");
        static readonly int k_exactTest = Shader.PropertyToID("_ExactTest");
        static readonly int k_leafListCountOffset = Shader.PropertyToID("_LeafListCountOffset");
        static readonly int k_chunkCullingAggressiveness =
            Shader.PropertyToID("_ChunkCullingAggressiveness");
        static readonly int k_leafNodesBuffer = Shader.PropertyToID("_LeafNodesBuffer");
        static readonly int k_coarseNodesBuffer = Shader.PropertyToID("_CoarseNodesBuffer");
        static readonly int k_visibleCoarseNodes = Shader.PropertyToID("_VisibleCoarseNodes");
        static readonly int k_fullLeafNodes = Shader.PropertyToID("_FullLeafNodes");
        static readonly int k_intersectLeafNodes = Shader.PropertyToID("_IntersectLeafNodes");
        static readonly int k_hierarchyCounts = Shader.PropertyToID("_HierarchyCounts");
        static readonly int k_hierarchyDispatchArgs = Shader.PropertyToID("_HierarchyDispatchArgs");

        int m_kernelCullClear = -1;
        int m_kernelCull = -1;
        int m_kernelHierarchyClear = -1;
        int m_kernelCullCoarse = -1;
        int m_kernelBuildLeafArgs = -1;
        int m_kernelCullLeaves = -1;
        int m_kernelBuildSplatArgs = -1;
        int m_kernelProcessHierarchy = -1;
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

        static uint ExtractRawCount(GraphicsBuffer countBuffer)
        {
            uint[] count = new uint[1];
            countBuffer.GetData(count);
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
                m_useActiveMask = false;
                m_orderInitializationMode = 0;
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

            int initializationMode = HierarchicalCullingActive ? 2 : FrustumCullingActive ? 1 : 0;
            if (cutoutsUnchanged && m_prevSplatCount == GsplatResource.UploadedCount &&
                m_orderInitializationMode == initializationMode)
                return;

            m_prevSplatCount = GsplatResource.UploadedCount;
            m_cutoutsData = updatedCutoutsData;
            CutoutsBuffer = m_gsplatAsset.UpdateCutoutsBuffer(CutoutsBuffer, m_cutoutsData);
            if (cutoutsUpdateBounds)
                m_gsplatAsset.UpdateBoundsBuffer(BoundsBuffer);
            if (HierarchicalCullingActive)
            {
                EnsureActiveMaskResources();
                m_gsplatAsset.InitCutoutMask(m_activeMaskBuffer, m_activeCountBuffer, GsplatResource,
                    cutoutsUpdateBounds);
                m_remainingCount = ExtractRawCount(m_activeCountBuffer);
                m_useActiveMask = true;
            }
            else
            {
                if (FrustumCullingActive)
                    EnsureCandidateOrderResources();
                var orderTarget = FrustumCullingActive ? m_candidateOrderResource : SorterResource;
                m_gsplatAsset.InitOrder(orderTarget, GsplatResource, cutoutsUpdateBounds);
                m_remainingCount = ExtractOrderSize(orderTarget.OrderBuffer);
                m_useActiveMask = false;
            }
            m_bounds = cutoutsUpdateBounds ? ExtractBounds() : m_gsplatAsset.Bounds;
            m_orderInitializationMode = initializationMode;
        }

        public void Cull(CommandBuffer cmd, Camera camera, Transform transform) =>
            Cull(cmd, new GsplatCameraInfo(camera), transform, 0);

        internal void Cull(CommandBuffer cmd, in GsplatCameraInfo cameraInfo, Transform transform,
            float chunkCullingAggressiveness)
        {
            var cs = m_gsplatAsset.GsplatMaterial.FrustumCullShader;
            Matrix4x4 matrixM = transform.localToWorldMatrix;
            Matrix4x4 matrixMvDepth = cameraInfo.SortViewMatrix * matrixM;
            Matrix4x4 matrixMv0 = cameraInfo.ViewMatrix0 * matrixM;
            Matrix4x4 matrixMv1 = cameraInfo.ViewMatrix1 * matrixM;

            cmd.SetComputeIntParam(cs, k_eyeCount, cameraInfo.ViewCount);
            cmd.SetComputeVectorParam(cs, k_viewportSize,
                new Vector4(Math.Max(1, cameraInfo.ViewportSize.x), Math.Max(1, cameraInfo.ViewportSize.y), 0, 0));
            cmd.SetComputeMatrixParam(cs, k_matrixMv0, matrixMv0);
            cmd.SetComputeMatrixParam(cs, k_matrixMv1, matrixMv1);
            cmd.SetComputeMatrixParam(cs, k_matrixMvDepth, matrixMvDepth);
            cmd.SetComputeMatrixParam(cs, k_matrixP0, cameraInfo.ProjectionMatrix0);
            cmd.SetComputeMatrixParam(cs, k_matrixP1, cameraInfo.ProjectionMatrix1);

            if (HierarchicalCullingActive)
                CullHierarchy(cmd, cs, Mathf.Clamp01(chunkCullingAggressiveness));
            else
                CullFlat(cmd, cs);

            BuildRenderArgs(cmd, cs);
        }

        void CullFlat(CommandBuffer cmd, ComputeShader cs)
        {
            cmd.SetComputeIntParam(cs, k_candidateCount, (int)m_remainingCount);
            cmd.SetComputeIntParam(cs, k_useCandidateOrder, m_cutoutsData.Length > 0 ? 1 : 0);
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
        }

        void CullHierarchy(CommandBuffer cmd, ComputeShader cs, float aggressiveness)
        {
            var resource = GsplatResource;
            int groupsPerLeaf = (resource.SpatialChunkSize + 255) / 256;
            GraphicsBuffer activeMask = m_useActiveMask ? m_activeMaskBuffer : m_dummyActiveMaskBuffer;

            cmd.SetComputeIntParam(cs, k_uploadedCount, (int)resource.UploadedCount);
            cmd.SetComputeIntParam(cs, k_leafCount, resource.SpatialLeafCount);
            cmd.SetComputeIntParam(cs, k_coarseCount, resource.SpatialCoarseCount);
            cmd.SetComputeIntParam(cs, k_spatialChunkSize, resource.SpatialChunkSize);
            cmd.SetComputeIntParam(cs, k_groupsPerLeaf, groupsPerLeaf);
            cmd.SetComputeIntParam(cs, k_useActiveMask, m_useActiveMask ? 1 : 0);
            cmd.SetComputeFloatParam(cs, k_chunkCullingAggressiveness, aggressiveness);

            BindHierarchyBuffers(cmd, cs, m_kernelHierarchyClear, activeMask);
            cmd.SetComputeBufferParam(cs, m_kernelHierarchyClear, k_visibleCountBuffer, VisibleCountBuffer);
            cmd.DispatchCompute(cs, m_kernelHierarchyClear, 1, 1, 1);

            BindHierarchyBuffers(cmd, cs, m_kernelCullCoarse, activeMask);
            cmd.DispatchCompute(cs, m_kernelCullCoarse,
                (int)GsplatUtils.DivRoundUp((uint)resource.SpatialCoarseCount, 64), 1, 1);

            BindHierarchyBuffers(cmd, cs, m_kernelBuildLeafArgs, activeMask);
            cmd.DispatchCompute(cs, m_kernelBuildLeafArgs, 1, 1, 1);

            BindHierarchyBuffers(cmd, cs, m_kernelCullLeaves, activeMask);
            cmd.DispatchCompute(cs, m_kernelCullLeaves, m_hierarchyDispatchArgs, 0);

            BindHierarchyBuffers(cmd, cs, m_kernelBuildSplatArgs, activeMask);
            cmd.DispatchCompute(cs, m_kernelBuildSplatArgs, 1, 1, 1);

            BindHierarchyBuffers(cmd, cs, m_kernelProcessHierarchy, activeMask);
            BindSplatDataBuffers(cmd, cs, m_kernelProcessHierarchy);
            cmd.SetComputeBufferParam(cs, m_kernelProcessHierarchy, k_orderBuffer, SorterResource.OrderBuffer);
            cmd.SetComputeBufferParam(cs, m_kernelProcessHierarchy, k_depthBuffer, SorterResource.InputKeys);
            cmd.SetComputeBufferParam(cs, m_kernelProcessHierarchy, k_visibleCountBuffer, VisibleCountBuffer);

            cmd.SetComputeIntParam(cs, k_exactTest, 0);
            cmd.SetComputeIntParam(cs, k_leafListCountOffset, sizeof(uint));
            cmd.DispatchCompute(cs, m_kernelProcessHierarchy, m_hierarchyDispatchArgs, sizeof(uint) * 3);

            cmd.SetComputeIntParam(cs, k_exactTest, 1);
            cmd.SetComputeIntParam(cs, k_leafListCountOffset, sizeof(uint) * 2);
            cmd.DispatchCompute(cs, m_kernelProcessHierarchy, m_hierarchyDispatchArgs, sizeof(uint) * 6);
        }

        void BindHierarchyBuffers(CommandBuffer cmd, ComputeShader cs, int kernel, GraphicsBuffer activeMask)
        {
            cmd.SetComputeBufferParam(cs, kernel, k_activeMaskBuffer, activeMask);
            cmd.SetComputeBufferParam(cs, kernel, k_leafNodesBuffer, GsplatResource.SpatialLeafNodesBuffer);
            cmd.SetComputeBufferParam(cs, kernel, k_coarseNodesBuffer, GsplatResource.SpatialCoarseNodesBuffer);
            cmd.SetComputeBufferParam(cs, kernel, k_visibleCoarseNodes, m_visibleCoarseNodes);
            cmd.SetComputeBufferParam(cs, kernel, k_fullLeafNodes, m_fullLeafNodes);
            cmd.SetComputeBufferParam(cs, kernel, k_intersectLeafNodes, m_intersectLeafNodes);
            cmd.SetComputeBufferParam(cs, kernel, k_hierarchyCounts, m_hierarchyCountsBuffer);
            cmd.SetComputeBufferParam(cs, kernel, k_hierarchyDispatchArgs, m_hierarchyDispatchArgs);
        }

        void BindSplatDataBuffers(CommandBuffer cmd, ComputeShader cs, int kernel)
        {
            if (GsplatResource is GsplatResourceUncompressed uncompressed)
            {
                cmd.SetComputeBufferParam(cs, kernel, k_positionBuffer, uncompressed.PositionBuffer);
                cmd.SetComputeBufferParam(cs, kernel, k_scaleBuffer, uncompressed.ScaleBuffer);
                cmd.SetComputeBufferParam(cs, kernel, k_rotationBuffer, uncompressed.RotationBuffer);
            }
            else if (GsplatResource is GsplatResourceSpark spark)
                cmd.SetComputeBufferParam(cs, kernel, k_packedSplatsBuffer, spark.PackedSplatsBuffer);
        }

        void BuildRenderArgs(CommandBuffer cmd, ComputeShader cs)
        {
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
            {
                DisposeCullingResources();
                EnsureCullingResources();
            }
            if (asyncUpload)
                gsplatAsset.UploadDataAsync(GsplatResource);
            else
                gsplatAsset.UploadData(GsplatResource);
        }

        public void ReleaseGsplatAsset()
        {
            DisposeCullingResources();
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
            if (SortDispatchArgs != null)
                return;

            SortDispatchArgs = new GraphicsBuffer(GraphicsBuffer.Target.IndirectArguments, 1, sizeof(uint) * 3);
            DrawArgs = new GraphicsBuffer(GraphicsBuffer.Target.IndirectArguments, 1,
                GraphicsBuffer.IndirectDrawIndexedArgs.size);
            DrawArgs.SetData(new GraphicsBuffer.IndirectDrawIndexedArgs[1]);
            if (GsplatResource.HasSpatialHierarchy)
                EnsureHierarchyResources();
            else
                EnsureCandidateOrderResources();
            CacheCullingKernels();
        }

        void EnsureCandidateOrderResources()
        {
            if (m_candidateOrderBuffer != null)
                return;
            m_candidateOrderBuffer = new GraphicsBuffer(GraphicsBuffer.Target.Append, (int)SplatCount, sizeof(uint));
            m_candidateOrderResource = new CandidateOrderResource(m_candidateOrderBuffer);
        }

        void EnsureHierarchyResources()
        {
            if (m_hierarchyCountsBuffer != null)
                return;

            m_visibleCoarseNodes = new GraphicsBuffer(GraphicsBuffer.Target.Structured,
                GsplatResource.SpatialCoarseCount, sizeof(uint));
            m_fullLeafNodes = new GraphicsBuffer(GraphicsBuffer.Target.Structured,
                GsplatResource.SpatialLeafCount, sizeof(uint));
            m_intersectLeafNodes = new GraphicsBuffer(GraphicsBuffer.Target.Structured,
                GsplatResource.SpatialLeafCount, sizeof(uint));
            m_hierarchyCountsBuffer = new GraphicsBuffer(GraphicsBuffer.Target.Raw, 4, sizeof(uint));
            m_hierarchyCountsBuffer.SetData(new uint[4]);
            m_hierarchyDispatchArgs = new GraphicsBuffer(GraphicsBuffer.Target.IndirectArguments, 9, sizeof(uint));
            m_hierarchyDispatchArgs.SetData(new uint[9]);
            m_dummyActiveMaskBuffer = new GraphicsBuffer(GraphicsBuffer.Target.Structured, 1, sizeof(uint));
            m_dummyActiveMaskBuffer.SetData(new uint[1]);
        }

        void EnsureActiveMaskResources()
        {
            if (m_activeMaskBuffer != null)
                return;
            m_activeMaskBuffer = new GraphicsBuffer(GraphicsBuffer.Target.Structured, (int)SplatCount, sizeof(uint));
            m_activeCountBuffer = new GraphicsBuffer(GraphicsBuffer.Target.Raw, 1, sizeof(uint));
            m_activeCountBuffer.SetData(new uint[1]);
        }

        void CacheCullingKernels()
        {
            var cs = m_gsplatAsset.GsplatMaterial.FrustumCullShader;
            m_kernelCullClear = cs.FindKernel("Clear");
            m_kernelCull = cs.FindKernel(GsplatResource is GsplatResourceSpark ? "CullSpark" : "CullUncompressed");
            m_kernelHierarchyClear = cs.FindKernel("ClearHierarchy");
            m_kernelCullCoarse = cs.FindKernel("CullCoarse");
            m_kernelBuildLeafArgs = cs.FindKernel("BuildLeafArgs");
            m_kernelCullLeaves = cs.FindKernel("CullLeaves");
            m_kernelBuildSplatArgs = cs.FindKernel("BuildSplatArgs");
            m_kernelProcessHierarchy = cs.FindKernel(GsplatResource is GsplatResourceSpark
                ? "ProcessHierarchySpark"
                : "ProcessHierarchyUncompressed");
            m_kernelBuildArgs = cs.FindKernel("BuildArgs");
        }

        void DisposeCullingResources()
        {
            m_candidateOrderBuffer?.Dispose();
            m_candidateOrderBuffer = null;
            m_candidateOrderResource = null;
            m_activeMaskBuffer?.Dispose();
            m_activeMaskBuffer = null;
            m_activeCountBuffer?.Dispose();
            m_activeCountBuffer = null;
            m_dummyActiveMaskBuffer?.Dispose();
            m_dummyActiveMaskBuffer = null;
            m_visibleCoarseNodes?.Dispose();
            m_visibleCoarseNodes = null;
            m_fullLeafNodes?.Dispose();
            m_fullLeafNodes = null;
            m_intersectLeafNodes?.Dispose();
            m_intersectLeafNodes = null;
            m_hierarchyCountsBuffer?.Dispose();
            m_hierarchyCountsBuffer = null;
            m_hierarchyDispatchArgs?.Dispose();
            m_hierarchyDispatchArgs = null;
            SortDispatchArgs?.Dispose();
            SortDispatchArgs = null;
            DrawArgs?.Dispose();
            DrawArgs = null;
            m_useActiveMask = false;
            m_orderInitializationMode = 0;
        }

        public void Dispose()
        {
            FrustumCullingActive = false;
            ReleaseGsplatAsset();
            OrderBuffer?.Dispose();
            OrderBuffer = null;
            SorterResource?.Dispose();
            SorterResource = null;
            DisposeCullingResources();
            VisibleCountBuffer?.Dispose();
            VisibleCountBuffer = null;
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
