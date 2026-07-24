// Copyright (c) 2025 Yize Wu
// SPDX-License-Identifier: MIT

using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Rendering;

namespace Gsplat
{
    public class GsplatAssetUncompressed : GsplatAsset
    {
        public override CompressionMode Compression => CompressionMode.Uncompressed;

        [HideInInspector] public Vector3[] Positions;
        [HideInInspector] public Vector4[] Colors; // RGB, Opacity
        [HideInInspector] public Vector3[] SHs;
        [HideInInspector] public Vector3[] Scales;
        [HideInInspector] public Vector4[] Rotations; // Quaternion, wxyz

        static readonly int k_positionBuffer = Shader.PropertyToID("_PositionBuffer");
        static readonly int k_scaleBuffer = Shader.PropertyToID("_ScaleBuffer");
        static readonly int k_rotationBuffer = Shader.PropertyToID("_RotationBuffer");
        static readonly int k_colorBuffer = Shader.PropertyToID("_ColorBuffer");
        static readonly int k_shBuffer = Shader.PropertyToID("_SHBuffer");
        static readonly int k_splatCount = Shader.PropertyToID("_SplatCount");
        static readonly int k_matrixMv = Shader.PropertyToID("_MatrixMV");
        static readonly int k_depthBuffer = Shader.PropertyToID("_DepthBuffer");
        static readonly int k_orderBuffer = Shader.PropertyToID("_OrderBuffer");
        static readonly int k_activeMaskBuffer = Shader.PropertyToID("_ActiveMaskBuffer");
        static readonly int k_activeCountBuffer = Shader.PropertyToID("_ActiveCountBuffer");

        public override void Allocate()
        {
            Positions = new Vector3[SplatCount];
            Colors = new Vector4[SplatCount];
            Scales = new Vector3[SplatCount];
            Rotations = new Vector4[SplatCount];
            if (SHBands > 0)
                SHs = new Vector3[SplatCount * GsplatUtils.SHBandsToCoefficientCount(SHBands)];
        }

        public override GsplatResource CreateResource()
        {
            return new GsplatResourceUncompressed(this);
        }

        protected override void _UploadData(GsplatResource resource)
        {
            var res = (GsplatResourceUncompressed)resource;
            uint uploadedCount = 0;
            while (uploadedCount < SplatCount)
            {
                int batchSize = GetUploadBatchSize(uploadedCount, GetMaxBytesPerSplat());
                UploadBatch(res, uploadedCount, batchSize);
                uploadedCount += (uint)batchSize;
            }
        }

        protected override async Task _UploadDataAsync(GsplatResource resource)
        {
            var res = (GsplatResourceUncompressed)resource;
            while (res.UploadedCount < SplatCount)
            {
                int batchSize = GetUploadBatchSize(res.UploadedCount, GetMaxBytesPerSplat());
                UploadBatch(res, res.UploadedCount, batchSize);
                res.UploadedCount += (uint)batchSize;
                await Task.Yield();
            }
        }

        int GetMaxBytesPerSplat()
        {
            int shBytes = GsplatUtils.SHBandsToCoefficientCount(SHBands) * Marshal.SizeOf(typeof(Vector3));
            return Math.Max(Marshal.SizeOf(typeof(Vector4)), shBytes);
        }

        void UploadBatch(GsplatResourceUncompressed res, uint uploadedCount, int batchSize)
        {
            int offset = (int)uploadedCount;
            res.PositionBuffer.SetData(Positions, offset, offset, batchSize);
            res.ScaleBuffer.SetData(Scales, offset, offset, batchSize);
            res.RotationBuffer.SetData(Rotations, offset, offset, batchSize);
            res.ColorBuffer.SetData(Colors, offset, offset, batchSize);

            if (SHBands > 0)
            {
                int coefficientCount = GsplatUtils.SHBandsToCoefficientCount(SHBands);
                res.SHBuffer.SetData(SHs, coefficientCount * offset, coefficientCount * offset,
                    coefficientCount * batchSize);
            }
        }

        public override void SetupMaterialPropertyBlock(MaterialPropertyBlock propertyBlock,
            GsplatResource resource)
        {
            var cs = GsplatMaterial.InitOrderShader;
            m_kernelInitOrder = cs.FindKernel("InitOrder");
            m_kernelInitCutoutMask = cs.FindKernel("InitCutoutMask");

            var res = (GsplatResourceUncompressed)resource;
            propertyBlock.SetBuffer(k_positionBuffer, res.PositionBuffer);
            propertyBlock.SetBuffer(k_scaleBuffer, res.ScaleBuffer);
            propertyBlock.SetBuffer(k_rotationBuffer, res.RotationBuffer);
            propertyBlock.SetBuffer(k_colorBuffer, res.ColorBuffer);
            if (SHBands > 0)
                propertyBlock.SetBuffer(k_shBuffer, res.SHBuffer);
        }

        public override void ComputeDepth(CommandBuffer cmd, Matrix4x4 matrixMv,
            ISorterResource sorterResource, GsplatResource resource)
        {
            var res = (GsplatResourceUncompressed)resource;
            var cs = GsplatMaterial.CalcDepthShader;
            var kernelCalcDepth = 0;
            cmd.SetComputeIntParam(cs, k_splatCount, (int)res.UploadedCount);
            cmd.SetComputeMatrixParam(cs, k_matrixMv, matrixMv);
            cmd.SetComputeBufferParam(cs, kernelCalcDepth, k_positionBuffer, res.PositionBuffer);
            cmd.SetComputeBufferParam(cs, kernelCalcDepth, k_depthBuffer, sorterResource.InputKeys);
            cmd.SetComputeBufferParam(cs, kernelCalcDepth, k_orderBuffer, sorterResource.OrderBuffer);
            cmd.DispatchCompute(cs, kernelCalcDepth, (int)GsplatUtils.DivRoundUp(res.UploadedCount, 1024), 1, 1);
        }

        public override void InitOrder(ISorterResource sorterResource, GsplatResource resource, bool updateBounds)
        {
            var cs = GsplatMaterial.InitOrderShader;
            var res = (GsplatResourceUncompressed)resource;
            sorterResource.OrderBuffer.SetCounterValue(0);
            cs.SetInt(k_splatCount, (int)res.UploadedCount);
            cs.SetBuffer(m_kernelInitOrder, k_orderBuffer, sorterResource.OrderBuffer);
            cs.SetBuffer(m_kernelInitOrder, k_positionBuffer, res.PositionBuffer);
            if (updateBounds)
                cs.EnableKeyword("UPDATE_BOUNDS");
            else
                cs.DisableKeyword("UPDATE_BOUNDS");
            cs.Dispatch(m_kernelInitOrder, (int)GsplatUtils.DivRoundUp(res.UploadedCount, 1024), 1, 1);
        }

        public override void InitCutoutMask(GraphicsBuffer activeMaskBuffer, GraphicsBuffer activeCountBuffer,
            GsplatResource resource, bool updateBounds)
        {
            var cs = GsplatMaterial.InitOrderShader;
            var res = (GsplatResourceUncompressed)resource;
            activeCountBuffer.SetData(new uint[1]);
            cs.SetInt(k_splatCount, (int)res.UploadedCount);
            cs.SetBuffer(m_kernelInitCutoutMask, k_positionBuffer, res.PositionBuffer);
            cs.SetBuffer(m_kernelInitCutoutMask, k_activeMaskBuffer, activeMaskBuffer);
            cs.SetBuffer(m_kernelInitCutoutMask, k_activeCountBuffer, activeCountBuffer);
            if (updateBounds)
                cs.EnableKeyword("UPDATE_BOUNDS");
            else
                cs.DisableKeyword("UPDATE_BOUNDS");
            cs.Dispatch(m_kernelInitCutoutMask, (int)GsplatUtils.DivRoundUp(res.UploadedCount, 1024), 1, 1);
        }

        public override void LoadFromPly(string plyPath, ProgressCallback progressCallback = null,
            SourceCoordinates sourceCoordinates = SourceCoordinates.RUF, float opacityPruneThreshold = 0f)
        {
            using var fs = new FileStream(plyPath, FileMode.Open, FileAccess.Read);
            // C# arrays and NativeArrays make it hard to have a "byte" array larger than 2GB :/
            if (fs.Length >= 2 * 1024 * 1024 * 1024L)
                throw new NotSupportedException("currently files larger than 2GB are not supported");

            LoadFromPlyStream(fs, progressCallback, sourceCoordinates, opacityPruneThreshold);
        }

        public override void LoadFromPlyBytes(byte[] plyBytes, ProgressCallback progressCallback = null,
            SourceCoordinates sourceCoordinates = SourceCoordinates.RUF, float opacityPruneThreshold = 0f)
        {
            if (plyBytes == null || plyBytes.Length == 0)
                throw new ArgumentException("PLY byte array is null or empty.", nameof(plyBytes));
            using var ms = new MemoryStream(plyBytes, writable: false);
            LoadFromPlyStream(ms, progressCallback, sourceCoordinates, opacityPruneThreshold);
        }

        void LoadFromPlyStream(Stream fs, ProgressCallback progressCallback, SourceCoordinates sourceCoordinates,
            float opacityPruneThreshold)
        {
            var plyInfo = new PlyHeaderInfo(fs);
            var shCoeffs = plyInfo.SHPropertyCount / 3;
            SplatCount = plyInfo.VertexCount;
            var sourceSplatCount = plyInfo.VertexCount;
            SHBands = GsplatUtils.CalcSHBandsFromSHPropertyCount(plyInfo.SHPropertyCount);

            if (SHBands > 4 || GsplatUtils.SHBandsToCoefficientCount(SHBands) * 3 != plyInfo.SHPropertyCount)
                throw new NotSupportedException($"unexpected SH property count {plyInfo.SHPropertyCount}");

            if (plyInfo.PositionOffset == -1 || plyInfo.ColorOffset == -1 || plyInfo.OpacityOffset == -1 ||
                plyInfo.ScaleOffset == -1 || plyInfo.RotationOffset == -1)
                throw new NotSupportedException("missing required properties in PLY header");

            var (posXSign, posYSign, posZSign) = GsplatUtils.AxisSigns(sourceCoordinates);
            float rotXSign = posYSign * posZSign;
            float rotYSign = posXSign * posZSign;
            float rotZSign = posXSign * posYSign;

            Allocate();
            var buffer = new byte[plyInfo.PropertyCount * sizeof(float)];
            uint w = 0; // write index; splats below the prune threshold are skipped
            for (uint i = 0; i < plyInfo.VertexCount; i++)
            {
                var readBytes = fs.Read(buffer);
                if (readBytes != buffer.Length)
                    throw new EndOfStreamException($"unexpected end of file, got {readBytes} bytes at vertex {i}");

                var properties = MemoryMarshal.Cast<byte, float>(buffer);
                progressCallback?.Invoke("Reading vertices", i / (float)plyInfo.VertexCount);

                var alpha = GsplatUtils.Sigmoid(properties[plyInfo.OpacityOffset]);
                if (alpha < opacityPruneThreshold)
                    continue;

                Positions[w] = new Vector3(
                    posXSign * properties[plyInfo.PositionOffset],
                    posYSign * properties[plyInfo.PositionOffset + 1],
                    posZSign * properties[plyInfo.PositionOffset + 2]);
                Colors[w] = new Vector4(
                    properties[plyInfo.ColorOffset],
                    properties[plyInfo.ColorOffset + 1],
                    properties[plyInfo.ColorOffset + 2],
                    alpha);

                for (int j = 0, bandOffset = 0; j < SHBands; j++)
                {
                    int bandSize = (j + 1) * 2 + 1; // band l = j+1 has 2l+1 coefficients
                    for (int k = 0; k < bandSize; k++)
                    {
                        float sign = GsplatUtils.ShSign(sourceCoordinates, j + 1, k);
                        int idx = (int)w * shCoeffs + bandOffset + k;
                        SHs[idx] = sign * new Vector3(
                            properties[bandOffset + k + plyInfo.SHOffset],
                            properties[bandOffset + k + plyInfo.SHOffset + shCoeffs],
                            properties[bandOffset + k + plyInfo.SHOffset + shCoeffs * 2]);
                    }
                    bandOffset += bandSize;
                }

                Scales[w] = new Vector3(
                    Mathf.Exp(properties[plyInfo.ScaleOffset]),
                    Mathf.Exp(properties[plyInfo.ScaleOffset + 1]),
                    Mathf.Exp(properties[plyInfo.ScaleOffset + 2]));
                Rotations[w] = new Vector4(
                    properties[plyInfo.RotationOffset],
                    rotXSign * properties[plyInfo.RotationOffset + 1],
                    rotYSign * properties[plyInfo.RotationOffset + 2],
                    rotZSign * properties[plyInfo.RotationOffset + 3]).normalized;

                if (w == 0) Bounds = new Bounds(Positions[w], Vector3.zero);
                else Bounds.Encapsulate(Positions[w]);
                w++;
            }

            if (w == 0 && plyInfo.VertexCount > 0)
                throw new NotSupportedException(
                    $"opacity prune threshold {opacityPruneThreshold} removed all {plyInfo.VertexCount} splats");

            if (w != SplatCount)
            {
                SplatCount = w;
                PrunedSplatCount = sourceSplatCount - SplatCount;
                Array.Resize(ref Positions, (int)w);
                Array.Resize(ref Colors, (int)w);
                Array.Resize(ref Scales, (int)w);
                Array.Resize(ref Rotations, (int)w);
                if (SHBands > 0)
                    Array.Resize(ref SHs, (int)w * shCoeffs);
            }

            BuildSpatialHierarchy(progressCallback);
        }

        internal override void GetSpatialData(int index, out Vector3 position, out Vector3 scale,
            out Vector4 rotation)
        {
            position = Positions[index];
            scale = Scales[index];
            rotation = Rotations[index];
        }

        internal override void ApplySpatialOrder(uint[] sourceAtDestination)
        {
            GsplatSpatialHierarchy.ReorderBlocks(Positions, 1, sourceAtDestination);
            GsplatSpatialHierarchy.ReorderBlocks(Colors, 1, sourceAtDestination);
            GsplatSpatialHierarchy.ReorderBlocks(Scales, 1, sourceAtDestination);
            GsplatSpatialHierarchy.ReorderBlocks(Rotations, 1, sourceAtDestination);
            if (SHBands > 0)
                GsplatSpatialHierarchy.ReorderBlocks(SHs,
                    GsplatUtils.SHBandsToCoefficientCount(SHBands), sourceAtDestination);
        }
    }
}
