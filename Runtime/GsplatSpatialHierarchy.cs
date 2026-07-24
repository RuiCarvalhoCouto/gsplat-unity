// Copyright (c) 2026 Rui Couto
// SPDX-License-Identifier: MIT

using System;
using System.Runtime.InteropServices;
using UnityEngine;

namespace Gsplat
{
    public enum GsplatSpatialChunkSize
    {
        Splats128 = 128,
        Splats256 = 256,
        Splats512 = 512,
    }

    [Serializable, StructLayout(LayoutKind.Sequential)]
    public struct GsplatSpatialNode
    {
        public Vector4 CenterMin;
        public Vector4 CenterMax;
        public Vector4 FootprintMin;
        public Vector4 FootprintMax;

        public GsplatSpatialNode(Vector3 centerMin, Vector3 centerMax, Vector3 footprintMin,
            Vector3 footprintMax)
        {
            CenterMin = new Vector4(centerMin.x, centerMin.y, centerMin.z, 0);
            CenterMax = new Vector4(centerMax.x, centerMax.y, centerMax.z, 0);
            FootprintMin = new Vector4(footprintMin.x, footprintMin.y, footprintMin.z, 0);
            FootprintMax = new Vector4(footprintMax.x, footprintMax.y, footprintMax.z, 0);
        }
    }

    static class GsplatSpatialHierarchy
    {
        public const int CoarseNodeLeafCount = 64;
        public const int NodeStride = sizeof(float) * 16;
        const float k_FootprintSigma = 2.8284271247461903f; // 2 * sqrt(2), matching rendered quad support.
        const int k_ProgressStride = 0xFFFF;

        public static void Build(GsplatAsset asset, ProgressCallback progressCallback)
        {
            int splatCount = checked((int)asset.SplatCount);
            int chunkSize = asset.SpatialChunkSize;
            if (!IsValidChunkSize(chunkSize))
                chunkSize = (int)GsplatSpatialChunkSize.Splats256;
            asset.SpatialChunkSize = chunkSize;

            if (splatCount == 0)
            {
                asset.SpatialLeafNodes = Array.Empty<GsplatSpatialNode>();
                asset.SpatialCoarseNodes = Array.Empty<GsplatSpatialNode>();
                return;
            }

            var keys = new ulong[splatCount];
            var order = new uint[splatCount];
            Vector3 boundsMin = Vector3.positiveInfinity;
            Vector3 boundsMax = Vector3.negativeInfinity;

            for (int i = 0; i < splatCount; ++i)
            {
                asset.GetSpatialData(i, out var position, out _, out _);
                boundsMin = Vector3.Min(boundsMin, position);
                boundsMax = Vector3.Max(boundsMax, position);
                if ((i & k_ProgressStride) == 0)
                    progressCallback?.Invoke("Building spatial hierarchy", i / (float)splatCount * 0.2f);
            }

            Vector3 size = boundsMax - boundsMin;
            for (int i = 0; i < splatCount; ++i)
            {
                asset.GetSpatialData(i, out var position, out _, out _);
                Vector3 normalized = new(
                    size.x > 0 ? (position.x - boundsMin.x) / size.x : 0,
                    size.y > 0 ? (position.y - boundsMin.y) / size.y : 0,
                    size.z > 0 ? (position.z - boundsMin.z) / size.z : 0);
                uint morton = Morton3D(
                    (uint)Mathf.Clamp(Mathf.FloorToInt(normalized.x * 1023.0f), 0, 1023),
                    (uint)Mathf.Clamp(Mathf.FloorToInt(normalized.y * 1023.0f), 0, 1023),
                    (uint)Mathf.Clamp(Mathf.FloorToInt(normalized.z * 1023.0f), 0, 1023));
                keys[i] = ((ulong)morton << 32) | (uint)i;
                order[i] = (uint)i;
            }

            Array.Sort(keys, order);
            progressCallback?.Invoke("Building spatial hierarchy", 0.45f);
            asset.ApplySpatialOrder(order);
            progressCallback?.Invoke("Building spatial hierarchy", 0.7f);

            int leafCount = (splatCount + chunkSize - 1) / chunkSize;
            var leafNodes = new GsplatSpatialNode[leafCount];
            for (int leafIndex = 0; leafIndex < leafCount; ++leafIndex)
            {
                int begin = leafIndex * chunkSize;
                int end = Math.Min(begin + chunkSize, splatCount);
                leafNodes[leafIndex] = BuildLeaf(asset, begin, end);
                if ((leafIndex & 0xFF) == 0)
                    progressCallback?.Invoke("Building spatial hierarchy",
                        0.7f + 0.25f * leafIndex / Math.Max(1, leafCount));
            }

            int coarseCount = (leafCount + CoarseNodeLeafCount - 1) / CoarseNodeLeafCount;
            var coarseNodes = new GsplatSpatialNode[coarseCount];
            for (int coarseIndex = 0; coarseIndex < coarseCount; ++coarseIndex)
            {
                int begin = coarseIndex * CoarseNodeLeafCount;
                int end = Math.Min(begin + CoarseNodeLeafCount, leafCount);
                coarseNodes[coarseIndex] = MergeNodes(leafNodes, begin, end);
            }

            asset.Bounds = new Bounds((boundsMin + boundsMax) * 0.5f, boundsMax - boundsMin);
            asset.SpatialLeafNodes = leafNodes;
            asset.SpatialCoarseNodes = coarseNodes;
            progressCallback?.Invoke("Building spatial hierarchy", 1);
        }

        public static bool IsValid(GsplatAsset asset)
        {
            if (!IsValidChunkSize(asset.SpatialChunkSize) || asset.SplatCount == 0)
                return false;
            int leafCount = ((int)asset.SplatCount + asset.SpatialChunkSize - 1) / asset.SpatialChunkSize;
            int coarseCount = (leafCount + CoarseNodeLeafCount - 1) / CoarseNodeLeafCount;
            return asset.SpatialLeafNodes is { Length: > 0 } &&
                   asset.SpatialLeafNodes.Length == leafCount &&
                   asset.SpatialCoarseNodes is { Length: > 0 } &&
                   asset.SpatialCoarseNodes.Length == coarseCount;
        }

        public static bool IsValidChunkSize(int chunkSize) =>
            chunkSize == (int)GsplatSpatialChunkSize.Splats128 ||
            chunkSize == (int)GsplatSpatialChunkSize.Splats256 ||
            chunkSize == (int)GsplatSpatialChunkSize.Splats512;

        static GsplatSpatialNode BuildLeaf(GsplatAsset asset, int begin, int end)
        {
            Vector3 centerMin = Vector3.positiveInfinity;
            Vector3 centerMax = Vector3.negativeInfinity;
            Vector3 footprintMin = Vector3.positiveInfinity;
            Vector3 footprintMax = Vector3.negativeInfinity;
            for (int i = begin; i < end; ++i)
            {
                asset.GetSpatialData(i, out var position, out var scale, out var rotation);
                Vector3 extent = CalculateFootprintExtent(scale, rotation);
                centerMin = Vector3.Min(centerMin, position);
                centerMax = Vector3.Max(centerMax, position);
                footprintMin = Vector3.Min(footprintMin, position - extent);
                footprintMax = Vector3.Max(footprintMax, position + extent);
            }
            return new GsplatSpatialNode(centerMin, centerMax, footprintMin, footprintMax);
        }

        static GsplatSpatialNode MergeNodes(GsplatSpatialNode[] nodes, int begin, int end)
        {
            Vector3 centerMin = Vector3.positiveInfinity;
            Vector3 centerMax = Vector3.negativeInfinity;
            Vector3 footprintMin = Vector3.positiveInfinity;
            Vector3 footprintMax = Vector3.negativeInfinity;
            for (int i = begin; i < end; ++i)
            {
                centerMin = Vector3.Min(centerMin, nodes[i].CenterMin);
                centerMax = Vector3.Max(centerMax, nodes[i].CenterMax);
                footprintMin = Vector3.Min(footprintMin, nodes[i].FootprintMin);
                footprintMax = Vector3.Max(footprintMax, nodes[i].FootprintMax);
            }
            return new GsplatSpatialNode(centerMin, centerMax, footprintMin, footprintMax);
        }

        internal static Vector3 CalculateFootprintExtent(Vector3 scale, Vector4 r)
        {
            Vector4 r2 = r + r;
            float x = r2.x * r.w;
            Vector4 y = r2.y * r;
            Vector4 z = r2.z * r;
            float w = r2.w * r.w;

            Vector3 row0 = new(1.0f - z.z - w, y.z + x, y.w - z.x);
            Vector3 row1 = new(y.z - x, 1.0f - y.y - w, z.w + y.x);
            Vector3 row2 = new(y.w + z.x, z.w - y.x, 1.0f - y.y - z.z);
            Vector3 m0 = new(scale.x * row0.x, scale.y * row1.x, scale.z * row2.x);
            Vector3 m1 = new(scale.x * row0.y, scale.y * row1.y, scale.z * row2.y);
            Vector3 m2 = new(scale.x * row0.z, scale.y * row1.z, scale.z * row2.z);
            return k_FootprintSigma * new Vector3(m0.magnitude, m1.magnitude, m2.magnitude);
        }

        internal static void ReorderBlocks<T>(T[] data, int blockSize, uint[] sourceAtDestination)
        {
            if (data == null || data.Length == 0)
                return;

            int count = sourceAtDestination.Length;
            if (data.Length != count * blockSize)
                throw new ArgumentException("Spatial permutation does not match splat data.");

            var visited = new bool[count];
            var temporary = new T[blockSize];
            for (int start = 0; start < count; ++start)
            {
                if (visited[start])
                    continue;

                int current = start;
                Array.Copy(data, current * blockSize, temporary, 0, blockSize);
                while (true)
                {
                    visited[current] = true;
                    int source = checked((int)sourceAtDestination[current]);
                    if (source == start)
                    {
                        Array.Copy(temporary, 0, data, current * blockSize, blockSize);
                        break;
                    }
                    Array.Copy(data, source * blockSize, data, current * blockSize, blockSize);
                    current = source;
                }
            }
        }

        static uint Morton3D(uint x, uint y, uint z) =>
            Expand10Bits(x) | (Expand10Bits(y) << 1) | (Expand10Bits(z) << 2);

        static uint Expand10Bits(uint value)
        {
            value &= 0x000003ff;
            value = (value | value << 16) & 0x030000ff;
            value = (value | value << 8) & 0x0300f00f;
            value = (value | value << 4) & 0x030c30c3;
            value = (value | value << 2) & 0x09249249;
            return value;
        }
    }
}
