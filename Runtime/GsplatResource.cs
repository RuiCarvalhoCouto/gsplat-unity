using System.Runtime.InteropServices;
using UnityEngine;

namespace Gsplat
{
    public abstract class GsplatResource
    {
        public bool Uploaded;
        public uint UploadedCount;
        public GraphicsBuffer SpatialLeafNodesBuffer { get; private set; }
        public GraphicsBuffer SpatialCoarseNodesBuffer { get; private set; }
        public GraphicsBuffer SpatialLodNodesBuffer { get; private set; }
        public GraphicsBuffer SpatialLodSplatsBuffer { get; private set; }
        public GraphicsBuffer SpatialLodSHBuffer { get; private set; }
        public int SpatialLeafCount { get; private set; }
        public int SpatialCoarseCount { get; private set; }
        public int SpatialLodNodeCount { get; private set; }
        public int SpatialLodSplatCount { get; private set; }
        public int SpatialLodLevelCount { get; private set; }
        public uint SpatialLodRoot { get; private set; }
        public int SpatialChunkSize { get; private set; }
        public bool HasSpatialHierarchy => SpatialLeafNodesBuffer != null && SpatialCoarseNodesBuffer != null;

        protected void CreateSpatialHierarchy(GsplatAsset asset)
        {
            if (!asset.HasSpatialHierarchy)
            {
                CreateLodSplatBuffers(asset);
                return;
            }

            SpatialLeafCount = asset.SpatialLeafNodes.Length;
            SpatialCoarseCount = asset.SpatialCoarseNodes.Length;
            SpatialChunkSize = asset.SpatialChunkSize;
            SpatialLeafNodesBuffer = new GraphicsBuffer(GraphicsBuffer.Target.Structured, SpatialLeafCount,
                GsplatSpatialHierarchy.NodeStride);
            SpatialLeafNodesBuffer.SetData(asset.SpatialLeafNodes);
            SpatialCoarseNodesBuffer = new GraphicsBuffer(GraphicsBuffer.Target.Structured, SpatialCoarseCount,
                GsplatSpatialHierarchy.NodeStride);
            SpatialCoarseNodesBuffer.SetData(asset.SpatialCoarseNodes);
            SpatialLodNodeCount = asset.SpatialLodNodes.Length;
            SpatialLodSplatCount = asset.SpatialLodSplats.Length;
            SpatialLodRoot = asset.SpatialLodRoot;
            SpatialLodLevelCount = (int)asset.SpatialLodNodes[asset.SpatialLodRoot].Level + 1;
            SpatialLodNodesBuffer = new GraphicsBuffer(GraphicsBuffer.Target.Structured, SpatialLodNodeCount,
                GsplatSpatialHierarchy.LodNodeStride);
            SpatialLodNodesBuffer.SetData(asset.SpatialLodNodes);
            CreateLodSplatBuffers(asset);
        }

        void CreateLodSplatBuffers(GsplatAsset asset)
        {
            SpatialLodSplatsBuffer = new GraphicsBuffer(GraphicsBuffer.Target.Structured,
                Mathf.Max(1, SpatialLodSplatCount), GsplatSpatialHierarchy.LodSplatStride);
            if (SpatialLodSplatCount > 0)
                SpatialLodSplatsBuffer.SetData(asset.SpatialLodSplats);
            SpatialLodSHBuffer = new GraphicsBuffer(GraphicsBuffer.Target.Structured,
                Mathf.Max(1, asset.SpatialLodSH?.Length ?? 0), Marshal.SizeOf(typeof(Vector3)));
            if (asset.SpatialLodSH is { Length: > 0 })
                SpatialLodSHBuffer.SetData(asset.SpatialLodSH);
        }

        protected void DisposeSpatialHierarchy()
        {
            SpatialLeafNodesBuffer?.Dispose();
            SpatialLeafNodesBuffer = null;
            SpatialCoarseNodesBuffer?.Dispose();
            SpatialCoarseNodesBuffer = null;
            SpatialLodNodesBuffer?.Dispose();
            SpatialLodNodesBuffer = null;
            SpatialLodSplatsBuffer?.Dispose();
            SpatialLodSplatsBuffer = null;
            SpatialLodSHBuffer?.Dispose();
            SpatialLodSHBuffer = null;
            SpatialLeafCount = 0;
            SpatialCoarseCount = 0;
            SpatialLodNodeCount = 0;
            SpatialLodSplatCount = 0;
            SpatialLodLevelCount = 0;
            SpatialLodRoot = 0;
        }

        public abstract void Dispose();
    }

    public class GsplatResourceUncompressed : GsplatResource
    {
        public GraphicsBuffer PositionBuffer { get; private set; }
        public GraphicsBuffer ScaleBuffer { get; private set; }
        public GraphicsBuffer RotationBuffer { get; private set; }
        public GraphicsBuffer ColorBuffer { get; private set; }
        public GraphicsBuffer SHBuffer { get; private set; }

        public GsplatResourceUncompressed(uint splatCount, byte shBands)
        {
            CreateSplatBuffers(splatCount, shBands);
        }

        public GsplatResourceUncompressed(GsplatAsset asset)
        {
            CreateSplatBuffers(asset.SplatCount, asset.SHBands);
            CreateSpatialHierarchy(asset);
        }

        void CreateSplatBuffers(uint splatCount, byte shBands)
        {
            if (splatCount == 0)
                return;
            PositionBuffer = new GraphicsBuffer(GraphicsBuffer.Target.Structured, (int)splatCount,
                Marshal.SizeOf(typeof(Vector3)));
            ScaleBuffer = new GraphicsBuffer(GraphicsBuffer.Target.Structured, (int)splatCount,
                Marshal.SizeOf(typeof(Vector3)));
            RotationBuffer = new GraphicsBuffer(GraphicsBuffer.Target.Structured, (int)splatCount,
                Marshal.SizeOf(typeof(Vector4)));
            ColorBuffer = new GraphicsBuffer(GraphicsBuffer.Target.Structured, (int)splatCount,
                Marshal.SizeOf(typeof(Vector4)));
            if (shBands > 0)
                SHBuffer = new GraphicsBuffer(GraphicsBuffer.Target.Structured,
                    GsplatUtils.SHBandsToCoefficientCount(shBands) * (int)splatCount, Marshal.SizeOf(typeof(Vector3)));
        }

        public override void Dispose()
        {
            PositionBuffer?.Dispose();
            PositionBuffer = null;
            ScaleBuffer?.Dispose();
            ScaleBuffer = null;
            RotationBuffer?.Dispose();
            RotationBuffer = null;
            ColorBuffer?.Dispose();
            ColorBuffer = null;
            SHBuffer?.Dispose();
            SHBuffer = null;
            DisposeSpatialHierarchy();
        }
    }

    public class GsplatResourceSpark : GsplatResource
    {
        public GraphicsBuffer PackedSplatsBuffer { get; private set; }
        public GraphicsBuffer PackedSH1Buffer { get; private set; }
        public GraphicsBuffer PackedSH2Buffer { get; private set; }
        public GraphicsBuffer PackedSH3Buffer { get; private set; }
        public GraphicsBuffer PackedSH4Buffer { get; private set; }

        public GsplatResourceSpark(uint splatCount, byte shBands)
        {
            CreateSplatBuffers(splatCount, shBands);
        }

        public GsplatResourceSpark(GsplatAsset asset)
        {
            CreateSplatBuffers(asset.SplatCount, asset.SHBands);
            CreateSpatialHierarchy(asset);
        }

        void CreateSplatBuffers(uint splatCount, byte shBands)
        {
            if (splatCount == 0)
                return;
            PackedSplatsBuffer = new GraphicsBuffer(GraphicsBuffer.Target.Structured, (int)splatCount,
                sizeof(uint) * 4);
            if (shBands >= 1)
                PackedSH1Buffer = new GraphicsBuffer(GraphicsBuffer.Target.Structured, (int)splatCount,
                    sizeof(uint) * 2);
            if (shBands >= 2)
                PackedSH2Buffer = new GraphicsBuffer(GraphicsBuffer.Target.Structured, (int)splatCount,
                    sizeof(uint) * 4);
            if (shBands >= 3)
                PackedSH3Buffer = new GraphicsBuffer(GraphicsBuffer.Target.Structured, (int)splatCount,
                    sizeof(uint) * 4);
            if (shBands >= 4)
                PackedSH4Buffer = new GraphicsBuffer(GraphicsBuffer.Target.Structured, (int)splatCount,
                    sizeof(uint) * 4);
        }

        public override void Dispose()
        {
            PackedSplatsBuffer?.Dispose();
            PackedSplatsBuffer = null;
            PackedSH1Buffer?.Dispose();
            PackedSH1Buffer = null;
            PackedSH2Buffer?.Dispose();
            PackedSH2Buffer = null;
            PackedSH3Buffer?.Dispose();
            PackedSH3Buffer = null;
            PackedSH4Buffer?.Dispose();
            PackedSH4Buffer = null;
            DisposeSpatialHierarchy();
        }
    }
}
