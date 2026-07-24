// Copyright (c) 2025 Yize Wu
// SPDX-License-Identifier: MIT

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Rendering;

namespace Gsplat
{
    public enum CompressionMode
    {
        Uncompressed,
        Spark
    }

    /// <summary>
    /// The coordinate frame the source asset was authored in, using the same naming
    /// convention as the Niantic SPZ library: three letters for X (L/R), Y (U/D), Z (F/B).
    /// The importer converts to Unity (RUF) by applying the appropriate sign flips to
    /// positions, rotation quaternions, and spherical-harmonic coefficients at import time.
    /// </summary>
    public enum SourceCoordinates
    {
        [InspectorName("Unspecified (treated as RUB)")]
        Unspecified = 0,

        [InspectorName("LDB — Left-Down-Back")]
        LDB,

        [InspectorName("RDB — Right-Down-Back")]
        RDB,

        [InspectorName("LUB — Left-Up-Back")]
        LUB,

        [InspectorName("RUB — Right-Up-Back  (3DGS, OpenGL, SPZ)")]
        RUB,

        [InspectorName("LDF — Left-Down-Front")]
        LDF,

        [InspectorName("RDF — Right-Down-Front  (OpenCV, COLMAP)")]
        RDF,

        [InspectorName("LUF — Left-Up-Front  (GLB, glTF)")]
        LUF,

        [InspectorName("RUF — Right-Up-Front  (Unity — no conversion)")]
        RUF,
    }

    public class PlyHeaderInfo
    {
        public uint VertexCount = 0;
        public int PropertyCount = 0;
        public int SHPropertyCount = 0;
        public int PositionOffset = -1;
        public int ColorOffset = -1;
        public int SHOffset = -1;
        public int OpacityOffset = -1;
        public int ScaleOffset = -1;
        public int RotationOffset = -1;

        /// <summary>
        /// Read each line, used for header reading.
        /// </summary>
        /// <param name="fs"></param>
        /// <returns></returns>
        static string ReadLine(Stream fs)
        {
            List<byte> byteBuffer = new List<byte>();
            while (true)
            {
                int b = fs.ReadByte();
                if (b == -1 || b == '\n') break;
                byteBuffer.Add((byte)b);
            }

            // If line had CRLF line endings, remove the CR part
            if (byteBuffer.Count > 0 && byteBuffer.Last() == '\r')
            {
                byteBuffer.RemoveAt(byteBuffer.Count - 1);
            }

            return Encoding.UTF8.GetString(byteBuffer.ToArray());
        }

        public PlyHeaderInfo(Stream fs)
        {
            bool inVertexElement = false;
            while (ReadLine(fs) is { } line && line != "end_header")
            {
                var tokens = line.Split(' ');
                if (tokens.Length >= 2 && tokens[0] == "element")
                {
                    inVertexElement = tokens.Length == 3 && tokens[1] == "vertex";
                    if (inVertexElement)
                        VertexCount = uint.Parse(tokens[2]);
                    continue;
                }

                if (!inVertexElement) continue;
                if (tokens.Length != 3 || tokens[0] != "property") continue;
                switch (tokens[2])
                {
                    case "x":
                        PositionOffset = PropertyCount;
                        break;
                    case "f_dc_0":
                        ColorOffset = PropertyCount;
                        break;
                    case "f_rest_0":
                        SHOffset = PropertyCount;
                        break;
                    case "opacity":
                        OpacityOffset = PropertyCount;
                        break;
                    case "scale_0":
                        ScaleOffset = PropertyCount;
                        break;
                    case "rot_0":
                        RotationOffset = PropertyCount;
                        break;
                }

                if (tokens[2].StartsWith("f_rest_"))
                    SHPropertyCount++;
                PropertyCount++;
            }
        }
    }

    public delegate void ProgressCallback(string info, float progress);

    public abstract class GsplatAsset : ScriptableObject
    {
        // Keep each SetData below backend upload staging allocations used by large GPU resources.
        const int k_MaxUploadBytesPerCall = 8 * 1024 * 1024;

        public uint SplatCount;
        // Pruned splat count during import time; SplatCount + PrunedSplatCount = splat count of the source file.
        public uint PrunedSplatCount;
        public byte SHBands; // 0, 1, 2, 3, or 4
        public Bounds Bounds;
        [HideInInspector] public int SpatialChunkSize = (int)GsplatSpatialChunkSize.Splats256;
        [HideInInspector] public GsplatSpatialNode[] SpatialLeafNodes;
        [HideInInspector] public GsplatSpatialNode[] SpatialCoarseNodes;
        [HideInInspector] public GsplatLodNode[] SpatialLodNodes;
        [HideInInspector] public GsplatLodSplat[] SpatialLodSplats;
        [HideInInspector] public Vector3[] SpatialLodSH;
        [HideInInspector] public uint SpatialLodRoot;
        public abstract CompressionMode Compression { get; }
        public bool HasSpatialHierarchy => GsplatSpatialHierarchy.IsValid(this);

        protected int m_kernelInitOrder;
        protected int m_kernelInitCutoutMask;
        static readonly protected int k_boundsBuffer = Shader.PropertyToID("_BoundsBuffer");
        static readonly protected int k_cutoutsBuffer = Shader.PropertyToID("_CutoutsBuffer");
        static readonly protected int k_cutoutsCount = Shader.PropertyToID("_CutoutsCount");

        public GsplatMaterial GsplatMaterial => GsplatSettings.Instance.Materials[(int)Compression];
        public Material[] Materials => GsplatMaterial.Materials[SHBands];

        public abstract void Allocate();
        public abstract void LoadFromPly(string plyPath, ProgressCallback progressCallback = null,
            SourceCoordinates sourceCoordinates = SourceCoordinates.RUF, float opacityPruneThreshold = 0f);

        public virtual void LoadFromPlyBytes(byte[] plyBytes, ProgressCallback progressCallback = null,
            SourceCoordinates sourceCoordinates = SourceCoordinates.RUF, float opacityPruneThreshold = 0f)
            => throw new NotSupportedException($"{GetType().Name} does not support loading PLY from bytes.");

        public abstract GsplatResource CreateResource();

        public void UploadData(GsplatResource resource)
        {
            if (resource.Uploaded) return;
            _UploadData(resource);
            resource.Uploaded = true;
            resource.UploadedCount = SplatCount;
        }

        public Task UploadDataAsync(GsplatResource resource)
        {
            if (resource.Uploaded) return Task.CompletedTask;
            resource.Uploaded = true;
            return _UploadDataAsync(resource);
        }

        protected int GetUploadBatchSize(uint uploadedCount, int maxBytesPerSplat)
        {
            uint remaining = SplatCount - uploadedCount;
            uint configuredBatchSize = Math.Max(1u, GsplatSettings.Instance.UploadBatchSize);
            uint maxBatchSize = (uint)Math.Max(1, k_MaxUploadBytesPerCall / maxBytesPerSplat);
            return (int)Math.Min(remaining, Math.Min(configuredBatchSize, maxBatchSize));
        }

        public GraphicsBuffer UpdateCutoutsBuffer(GraphicsBuffer cutoutsBuffer, GsplatCutout.ShaderData[] cutoutsData)
        {
            var cs = GsplatMaterial.InitOrderShader;
            int numberOfCutouts = cutoutsData.Length;
            int bufferSize = Math.Max(numberOfCutouts, 1);

            if (cutoutsBuffer == null || cutoutsBuffer.count != bufferSize)
            {
                cutoutsBuffer?.Dispose();
                cutoutsBuffer = new GraphicsBuffer(GraphicsBuffer.Target.Structured, bufferSize, GsplatCutout.ShaderDataSize);
            }

            cutoutsBuffer.SetData(cutoutsData);
            cs.SetBuffer(m_kernelInitOrder, k_cutoutsBuffer, cutoutsBuffer);
            cs.SetBuffer(m_kernelInitCutoutMask, k_cutoutsBuffer, cutoutsBuffer);
            cs.SetInt(k_cutoutsCount, numberOfCutouts);
            return cutoutsBuffer;
        }

        public void UpdateBoundsBuffer(GraphicsBuffer BoundsBuffer)
        {
            var cs = GsplatMaterial.InitOrderShader;

            uint max = GsplatUtils.FloatToSortableUint(short.MaxValue);
            uint min = GsplatUtils.FloatToSortableUint(short.MinValue);
            uint[] array = {max, max, max, min, min, min};
            BoundsBuffer.SetData(array);

            cs.SetBuffer(m_kernelInitOrder, k_boundsBuffer, BoundsBuffer);
            cs.SetBuffer(m_kernelInitCutoutMask, k_boundsBuffer, BoundsBuffer);
        }

        protected abstract Task _UploadDataAsync(GsplatResource resource);

        protected abstract void _UploadData(GsplatResource resource);

        public abstract void SetupMaterialPropertyBlock(MaterialPropertyBlock propertyBlock, GsplatResource resource);

        public abstract void ComputeDepth(CommandBuffer cmd, Matrix4x4 matrixMv,
            ISorterResource sorterResource, GsplatResource resource);

        public abstract void InitOrder(ISorterResource sorterResource, GsplatResource resource,
            bool updateBounds);

        public virtual void InitCutoutMask(GraphicsBuffer activeMaskBuffer, GraphicsBuffer activeCountBuffer,
            GsplatResource resource, bool updateBounds) =>
            throw new NotSupportedException($"{GetType().Name} does not support hierarchical cutout masks.");

        internal virtual void GetSpatialData(int index, out Vector3 position, out Vector3 scale,
            out Vector4 rotation) =>
            throw new NotSupportedException($"{GetType().Name} does not support spatial hierarchy building.");

        internal virtual void ApplySpatialOrder(uint[] sourceAtDestination) =>
            throw new NotSupportedException($"{GetType().Name} does not support spatial hierarchy building.");

        internal virtual void GetLodData(int index, out Vector3 position, out Vector3 scale,
            out Vector4 rotation, out Vector4 color) =>
            throw new NotSupportedException($"{GetType().Name} does not support LOD hierarchy building.");

        internal virtual Vector3 GetLodSH(int index, int coefficient) =>
            throw new NotSupportedException($"{GetType().Name} does not support LOD hierarchy building.");

        internal void BuildSpatialHierarchy(ProgressCallback progressCallback = null) =>
            GsplatSpatialHierarchy.Build(this, progressCallback);
    }
}
