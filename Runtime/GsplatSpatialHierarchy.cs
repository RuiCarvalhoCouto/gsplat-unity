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

    [Serializable, StructLayout(LayoutKind.Sequential)]
    public struct GsplatLodNode
    {
        public GsplatSpatialNode Bounds;
        // x: geometric error, y: opacity bound, z: contribution bound, w: footprint radius.
        public Vector4 Metrics;
        public uint FirstChild;
        public uint ChildCount;
        public uint FirstSplat;
        public uint SplatCount;
        public uint FirstRepresentative;
        public uint RepresentativeCount;
        public uint Level;
        public uint Padding;
    }

    [Serializable, StructLayout(LayoutKind.Sequential)]
    public struct GsplatLodSplat
    {
        public Vector4 PositionOpacity;
        public Vector4 ScaleContribution;
        public Vector4 Rotation;
        public Vector4 Color;
    }

    static class GsplatSpatialHierarchy
    {
        public const int CoarseNodeLeafCount = 64;
        public const int NodeStride = sizeof(float) * 16;
        public const int LodNodeStride = NodeStride + sizeof(float) * 4 + sizeof(uint) * 8;
        public const int LodSplatStride = sizeof(float) * 16;
        public const int LodBranchingFactor = 8;
        public const int MaxLodRepresentatives = 32;
        const float k_FootprintSigma = 2.8284271247461903f; // 2 * sqrt(2), matching rendered quad support.
        const float k_MinScale = 1e-6f;
        const float k_MinWeight = 1e-8f;
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
                asset.SpatialLodNodes = Array.Empty<GsplatLodNode>();
                asset.SpatialLodSplats = Array.Empty<GsplatLodSplat>();
                asset.SpatialLodSH = Array.Empty<Vector3>();
                asset.SpatialLodRoot = 0;
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
            BuildLodHierarchy(asset, leafNodes, progressCallback);
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
                   asset.SpatialCoarseNodes.Length == coarseCount &&
                   IsValidLod(asset, leafCount);
        }

        static bool IsValidLod(GsplatAsset asset, int leafCount)
        {
            if (asset.SpatialLodNodes is not { Length: > 0 } ||
                asset.SpatialLodSplats == null || asset.SpatialLodSH == null ||
                asset.SpatialLodRoot >= asset.SpatialLodNodes.Length)
                return false;

            var root = asset.SpatialLodNodes[asset.SpatialLodRoot];
            int coefficientCount = GsplatUtils.SHBandsToCoefficientCount(asset.SHBands);
            return root.FirstSplat == 0 && root.SplatCount == asset.SplatCount &&
                   asset.SpatialLodNodes.Length >= leafCount &&
                   asset.SpatialLodSH.Length == asset.SpatialLodSplats.Length * coefficientCount;
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

        static void BuildLodHierarchy(GsplatAsset asset, GsplatSpatialNode[] leafNodes,
            ProgressCallback progressCallback)
        {
            int chunkSize = asset.SpatialChunkSize;
            int coefficientCount = GsplatUtils.SHBandsToCoefficientCount(asset.SHBands);
            var nodes = new System.Collections.Generic.List<GsplatLodNode>(leafNodes.Length * 8 / 7 + 1);
            var representatives = new System.Collections.Generic.List<GsplatLodSplat>();
            var representativeSH = new System.Collections.Generic.List<Vector3>();

            for (int i = 0; i < leafNodes.Length; ++i)
            {
                int firstSplat = i * chunkSize;
                nodes.Add(new GsplatLodNode
                {
                    Bounds = leafNodes[i],
                    Metrics = BuildLeafMetrics(leafNodes[i], (uint)Math.Min(chunkSize,
                        (int)asset.SplatCount - firstSplat)),
                    FirstSplat = (uint)firstSplat,
                    SplatCount = (uint)Math.Min(chunkSize, (int)asset.SplatCount - firstSplat),
                    Level = 0
                });
            }

            int previousOffset = 0;
            int previousCount = leafNodes.Length;
            uint level = 1;
            while (previousCount > 1)
            {
                int currentOffset = nodes.Count;
                int currentCount = (previousCount + LodBranchingFactor - 1) / LodBranchingFactor;
                for (int parentIndex = 0; parentIndex < currentCount; ++parentIndex)
                {
                    int firstChild = previousOffset + parentIndex * LodBranchingFactor;
                    int childCount = Math.Min(LodBranchingFactor,
                        previousOffset + previousCount - firstChild);
                    GsplatSpatialNode bounds = MergeLodNodes(nodes, firstChild, childCount);
                    uint firstSplat = nodes[firstChild].FirstSplat;
                    var lastChild = nodes[firstChild + childCount - 1];
                    uint splatCount = lastChild.FirstSplat + lastChild.SplatCount - firstSplat;
                    int firstRepresentative = representatives.Count;

                    if (level == 1)
                    {
                        BuildRepresentativesFromSplats(asset, (int)firstSplat, (int)splatCount,
                            coefficientCount, representatives, representativeSH);
                    }
                    else
                    {
                        BuildRepresentativesFromChildren(nodes, firstChild, childCount,
                            representatives, representativeSH, coefficientCount);
                    }

                    nodes.Add(new GsplatLodNode
                    {
                        Bounds = bounds,
                        Metrics = BuildParentMetrics(nodes, firstChild, childCount, bounds,
                            representatives, firstRepresentative),
                        FirstChild = (uint)firstChild,
                        ChildCount = (uint)childCount,
                        FirstSplat = firstSplat,
                        SplatCount = splatCount,
                        FirstRepresentative = (uint)firstRepresentative,
                        RepresentativeCount = (uint)(representatives.Count - firstRepresentative),
                        Level = level
                    });
                }

                previousOffset = currentOffset;
                previousCount = currentCount;
                ++level;
                progressCallback?.Invoke("Building LOD hierarchy",
                    0.95f + 0.04f * (1.0f - 1.0f / level));
            }

            asset.SpatialLodNodes = nodes.ToArray();
            asset.SpatialLodSplats = representatives.ToArray();
            asset.SpatialLodSH = representativeSH.ToArray();
            asset.SpatialLodRoot = (uint)(nodes.Count - 1);
        }

        static Vector4 BuildLeafMetrics(GsplatSpatialNode bounds, uint splatCount)
        {
            Vector3 centerExtents = (Vector3)(bounds.CenterMax - bounds.CenterMin) * 0.5f;
            Vector3 footprintExtents = (Vector3)(bounds.FootprintMax - bounds.FootprintMin) * 0.5f;
            return new Vector4(centerExtents.magnitude, 1, splatCount, footprintExtents.magnitude);
        }

        static Vector4 BuildParentMetrics(System.Collections.Generic.List<GsplatLodNode> nodes,
            int firstChild, int childCount, GsplatSpatialNode bounds,
            System.Collections.Generic.List<GsplatLodSplat> representatives, int firstRepresentative)
        {
            Vector3 center = ((Vector3)bounds.CenterMin + (Vector3)bounds.CenterMax) * 0.5f;
            float error = 0;
            for (int i = 0; i < childCount; ++i)
            {
                var child = nodes[firstChild + i];
                Vector3 childCenter =
                    ((Vector3)child.Bounds.CenterMin + (Vector3)child.Bounds.CenterMax) * 0.5f;
                error = Mathf.Max(error, child.Metrics.x + Vector3.Distance(center, childCenter));
            }

            float opacity = 0;
            float contribution = 0;
            for (int i = firstRepresentative; i < representatives.Count; ++i)
            {
                opacity = Mathf.Max(opacity, representatives[i].PositionOpacity.w);
                contribution += representatives[i].ScaleContribution.w;
            }
            Vector3 footprintExtents = (Vector3)(bounds.FootprintMax - bounds.FootprintMin) * 0.5f;
            return new Vector4(error, opacity, contribution, footprintExtents.magnitude);
        }

        static GsplatSpatialNode MergeLodNodes(System.Collections.Generic.List<GsplatLodNode> nodes,
            int first, int count)
        {
            Vector3 centerMin = Vector3.positiveInfinity;
            Vector3 centerMax = Vector3.negativeInfinity;
            Vector3 footprintMin = Vector3.positiveInfinity;
            Vector3 footprintMax = Vector3.negativeInfinity;
            for (int i = 0; i < count; ++i)
            {
                var bounds = nodes[first + i].Bounds;
                centerMin = Vector3.Min(centerMin, bounds.CenterMin);
                centerMax = Vector3.Max(centerMax, bounds.CenterMax);
                footprintMin = Vector3.Min(footprintMin, bounds.FootprintMin);
                footprintMax = Vector3.Max(footprintMax, bounds.FootprintMax);
            }
            return new GsplatSpatialNode(centerMin, centerMax, footprintMin, footprintMax);
        }

        static void BuildRepresentativesFromSplats(GsplatAsset asset, int firstSplat, int splatCount,
            int coefficientCount, System.Collections.Generic.List<GsplatLodSplat> output,
            System.Collections.Generic.List<Vector3> outputSH)
        {
            int representativeCount = Math.Min(MaxLodRepresentatives, splatCount);
            for (int i = 0; i < representativeCount; ++i)
            {
                int begin = firstSplat + (int)((long)splatCount * i / representativeCount);
                int end = firstSplat + (int)((long)splatCount * (i + 1) / representativeCount);
                AppendRepresentative(asset, begin, end, coefficientCount, output, outputSH);
            }
        }

        static void BuildRepresentativesFromChildren(System.Collections.Generic.List<GsplatLodNode> nodes,
            int firstChild, int childCount, System.Collections.Generic.List<GsplatLodSplat> representatives,
            System.Collections.Generic.List<Vector3> representativeSH, int coefficientCount)
        {
            int candidateCount = 0;
            for (int i = 0; i < childCount; ++i)
                candidateCount += (int)nodes[firstChild + i].RepresentativeCount;
            int outputCount = Math.Min(MaxLodRepresentatives, candidateCount);
            if (outputCount == 0)
                return;

            var candidates = new GsplatLodSplat[candidateCount];
            var candidateSH = coefficientCount == 0 ? null : new Vector3[candidateCount * coefficientCount];
            int destination = 0;
            for (int i = 0; i < childCount; ++i)
            {
                var child = nodes[firstChild + i];
                int count = (int)child.RepresentativeCount;
                representatives.CopyTo((int)child.FirstRepresentative, candidates, destination, count);
                if (coefficientCount > 0)
                    representativeSH.CopyTo((int)child.FirstRepresentative * coefficientCount, candidateSH,
                        destination * coefficientCount, count * coefficientCount);
                destination += count;
            }

            for (int i = 0; i < outputCount; ++i)
            {
                int begin = (int)((long)candidateCount * i / outputCount);
                int end = (int)((long)candidateCount * (i + 1) / outputCount);
                AppendRepresentative(candidates, candidateSH, begin, end, coefficientCount,
                    representatives, representativeSH);
            }
        }

        static void AppendRepresentative(GsplatAsset asset, int begin, int end, int coefficientCount,
            System.Collections.Generic.List<GsplatLodSplat> output,
            System.Collections.Generic.List<Vector3> outputSH)
        {
            var samples = new GsplatLodSplat[end - begin];
            var sampleSH = coefficientCount == 0 ? null : new Vector3[samples.Length * coefficientCount];
            for (int i = begin; i < end; ++i)
            {
                asset.GetLodData(i, out var position, out var scale, out var rotation, out var color);
                samples[i - begin] = new GsplatLodSplat
                {
                    PositionOpacity = new Vector4(position.x, position.y, position.z, color.w),
                    ScaleContribution = new Vector4(scale.x, scale.y, scale.z, 0),
                    Rotation = rotation,
                    Color = color
                };
                for (int coefficient = 0; coefficient < coefficientCount; ++coefficient)
                    sampleSH[(i - begin) * coefficientCount + coefficient] =
                        asset.GetLodSH(i, coefficient);
            }
            AppendRepresentative(samples, sampleSH, 0, samples.Length, coefficientCount, output, outputSH);
        }

        static void AppendRepresentative(GsplatLodSplat[] samples, Vector3[] sampleSH, int begin, int end,
            int coefficientCount, System.Collections.Generic.List<GsplatLodSplat> output,
            System.Collections.Generic.List<Vector3> outputSH)
        {
            double totalWeight = 0;
            Vector3 mean = Vector3.zero;
            Vector3 color = Vector3.zero;
            double opacityProduct = 1;
            var secondMoment = new SymmetricMatrix();
            var sh = coefficientCount == 0 ? null : new Vector3[coefficientCount];

            for (int i = begin; i < end; ++i)
            {
                var sample = samples[i];
                Vector3 position = sample.PositionOpacity;
                Vector3 scale = sample.ScaleContribution;
                float opacity = Mathf.Clamp01(sample.PositionOpacity.w);
                double weight = sample.ScaleContribution.w > 0
                    ? sample.ScaleContribution.w
                    : Math.Max(k_MinWeight,
                        opacity * Math.Max(k_MinScale, scale.x) * Math.Max(k_MinScale, scale.y) *
                        Math.Max(k_MinScale, scale.z));
                var covariance = Covariance(scale, sample.Rotation);
                totalWeight += weight;
                mean += position * (float)weight;
                color += (Vector3)sample.Color * (float)weight;
                secondMoment.Add(covariance, position, weight);
                opacityProduct *= 1.0 - opacity;
                for (int coefficient = 0; coefficient < coefficientCount; ++coefficient)
                    sh[coefficient] += sampleSH[i * coefficientCount + coefficient] * (float)weight;
            }

            float inverseWeight = (float)(1.0 / totalWeight);
            mean *= inverseWeight;
            color *= inverseWeight;
            var covarianceResult = secondMoment.ToCovariance(mean, totalWeight);
            covarianceResult.Decompose(out var scaleResult, out var rotationResult);
            float opacityResult = Mathf.Clamp((float)(1.0 - opacityProduct), 1.0f / 255.0f, 0.995f);
            float contribution = (float)Math.Min(totalWeight, float.MaxValue);

            output.Add(new GsplatLodSplat
            {
                PositionOpacity = new Vector4(mean.x, mean.y, mean.z, opacityResult),
                ScaleContribution = new Vector4(scaleResult.x, scaleResult.y, scaleResult.z, contribution),
                Rotation = rotationResult,
                Color = new Vector4(color.x, color.y, color.z, opacityResult)
            });
            for (int coefficient = 0; coefficient < coefficientCount; ++coefficient)
                outputSH.Add(sh[coefficient] * inverseWeight);
        }

        struct SymmetricMatrix
        {
            public double XX;
            public double XY;
            public double XZ;
            public double YY;
            public double YZ;
            public double ZZ;

            public void Add(SymmetricMatrix covariance, Vector3 position, double weight)
            {
                XX += weight * (covariance.XX + position.x * position.x);
                XY += weight * (covariance.XY + position.x * position.y);
                XZ += weight * (covariance.XZ + position.x * position.z);
                YY += weight * (covariance.YY + position.y * position.y);
                YZ += weight * (covariance.YZ + position.y * position.z);
                ZZ += weight * (covariance.ZZ + position.z * position.z);
            }

            public SymmetricMatrix ToCovariance(Vector3 mean, double totalWeight)
            {
                double inverseWeight = 1.0 / totalWeight;
                return new SymmetricMatrix
                {
                    XX = Math.Max(k_MinScale * k_MinScale, XX * inverseWeight - mean.x * mean.x),
                    XY = XY * inverseWeight - mean.x * mean.y,
                    XZ = XZ * inverseWeight - mean.x * mean.z,
                    YY = Math.Max(k_MinScale * k_MinScale, YY * inverseWeight - mean.y * mean.y),
                    YZ = YZ * inverseWeight - mean.y * mean.z,
                    ZZ = Math.Max(k_MinScale * k_MinScale, ZZ * inverseWeight - mean.z * mean.z)
                };
            }

            public void Decompose(out Vector3 scale, out Vector4 rotation)
            {
                Matrix4x4 matrix = Matrix4x4.zero;
                matrix[0, 0] = (float)XX;
                matrix[0, 1] = matrix[1, 0] = (float)XY;
                matrix[0, 2] = matrix[2, 0] = (float)XZ;
                matrix[1, 1] = (float)YY;
                matrix[1, 2] = matrix[2, 1] = (float)YZ;
                matrix[2, 2] = (float)ZZ;
                Matrix4x4 eigenvectors = Matrix4x4.identity;

                for (int sweep = 0; sweep < 10; ++sweep)
                {
                    RotateJacobi(ref matrix, ref eigenvectors, 0, 1);
                    RotateJacobi(ref matrix, ref eigenvectors, 0, 2);
                    RotateJacobi(ref matrix, ref eigenvectors, 1, 2);
                }

                Vector3 eigenvalues = new(matrix[0, 0], matrix[1, 1], matrix[2, 2]);
                SortEigenpair(ref eigenvalues, ref eigenvectors, 0, 1);
                SortEigenpair(ref eigenvalues, ref eigenvectors, 0, 2);
                SortEigenpair(ref eigenvalues, ref eigenvectors, 1, 2);
                if (Vector3.Dot(Vector3.Cross(eigenvectors.GetColumn(0), eigenvectors.GetColumn(1)),
                        eigenvectors.GetColumn(2)) < 0)
                    eigenvectors.SetColumn(2, -eigenvectors.GetColumn(2));

                scale = new Vector3(
                    Mathf.Sqrt(Mathf.Max((float)eigenvalues.x, k_MinScale * k_MinScale)),
                    Mathf.Sqrt(Mathf.Max((float)eigenvalues.y, k_MinScale * k_MinScale)),
                    Mathf.Sqrt(Mathf.Max((float)eigenvalues.z, k_MinScale * k_MinScale)));
                Quaternion quaternion = eigenvectors.rotation.normalized;
                rotation = new Vector4(quaternion.x, quaternion.y, quaternion.z, quaternion.w);
            }
        }

        static SymmetricMatrix Covariance(Vector3 scale, Vector4 rotation)
        {
            Vector4 r2 = rotation + rotation;
            float x = r2.x * rotation.w;
            Vector4 y = r2.y * rotation;
            Vector4 z = r2.z * rotation;
            float w = r2.w * rotation.w;
            Vector3 row0 = new(1.0f - z.z - w, y.z + x, y.w - z.x);
            Vector3 row1 = new(y.z - x, 1.0f - y.y - w, z.w + y.x);
            Vector3 row2 = new(y.w + z.x, z.w - y.x, 1.0f - y.y - z.z);
            Vector3 m0 = new(scale.x * row0.x, scale.y * row1.x, scale.z * row2.x);
            Vector3 m1 = new(scale.x * row0.y, scale.y * row1.y, scale.z * row2.y);
            Vector3 m2 = new(scale.x * row0.z, scale.y * row1.z, scale.z * row2.z);
            return new SymmetricMatrix
            {
                XX = Vector3.Dot(m0, m0),
                XY = Vector3.Dot(m0, m1),
                XZ = Vector3.Dot(m0, m2),
                YY = Vector3.Dot(m1, m1),
                YZ = Vector3.Dot(m1, m2),
                ZZ = Vector3.Dot(m2, m2)
            };
        }

        static void RotateJacobi(ref Matrix4x4 matrix, ref Matrix4x4 eigenvectors, int p, int q)
        {
            float apq = matrix[p, q];
            if (Mathf.Abs(apq) < 1e-12f)
                return;
            float angle = 0.5f * Mathf.Atan2(2.0f * apq, matrix[q, q] - matrix[p, p]);
            float c = Mathf.Cos(angle);
            float s = Mathf.Sin(angle);
            for (int k = 0; k < 3; ++k)
            {
                float mkp = matrix[k, p];
                float mkq = matrix[k, q];
                matrix[k, p] = c * mkp - s * mkq;
                matrix[k, q] = s * mkp + c * mkq;
            }
            for (int k = 0; k < 3; ++k)
            {
                float mpk = matrix[p, k];
                float mqk = matrix[q, k];
                matrix[p, k] = c * mpk - s * mqk;
                matrix[q, k] = s * mpk + c * mqk;
                float vkp = eigenvectors[k, p];
                float vkq = eigenvectors[k, q];
                eigenvectors[k, p] = c * vkp - s * vkq;
                eigenvectors[k, q] = s * vkp + c * vkq;
            }
        }

        static void SortEigenpair(ref Vector3 values, ref Matrix4x4 vectors, int a, int b)
        {
            if (values[a] >= values[b])
                return;
            float value = values[a];
            values[a] = values[b];
            values[b] = value;
            Vector4 vector = vectors.GetColumn(a);
            vectors.SetColumn(a, vectors.GetColumn(b));
            vectors.SetColumn(b, vector);
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
