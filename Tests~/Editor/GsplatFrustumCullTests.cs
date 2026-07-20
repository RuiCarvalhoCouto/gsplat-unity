using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace Gsplat.Tests
{
    public class GsplatFrustumCullTests
    {
        [Test]
        public void CullUncompressedRetainsOverlappingFootprintsOnly()
        {
            var shader = AssetDatabase.LoadAssetAtPath<ComputeShader>(
                "Packages/wu.yize.gsplat/Runtime/Shaders/GsplatFrustumCull.compute");
            Assert.That(shader, Is.Not.Null);

            var positions = new[]
            {
                new Vector3(0, 0, -2),
                new Vector3(100, 0, -2),
                new Vector3(3, 0, -2),
            };
            var scales = new[]
            {
                Vector3.one * 0.1f,
                Vector3.one * 0.1f,
                Vector3.one * 5.0f,
            };
            var rotations = new[]
            {
                new Vector4(1, 0, 0, 0),
                new Vector4(1, 0, 0, 0),
                new Vector4(1, 0, 0, 0),
            };

            using var positionBuffer = new GraphicsBuffer(GraphicsBuffer.Target.Structured, 3, sizeof(float) * 3);
            using var scaleBuffer = new GraphicsBuffer(GraphicsBuffer.Target.Structured, 3, sizeof(float) * 3);
            using var rotationBuffer = new GraphicsBuffer(GraphicsBuffer.Target.Structured, 3, sizeof(float) * 4);
            using var candidateBuffer = new GraphicsBuffer(GraphicsBuffer.Target.Structured, 3, sizeof(uint));
            using var orderBuffer = new GraphicsBuffer(GraphicsBuffer.Target.Structured, 3, sizeof(uint));
            using var depthBuffer = new GraphicsBuffer(GraphicsBuffer.Target.Structured, 3, sizeof(float));
            using var countBuffer = new GraphicsBuffer(GraphicsBuffer.Target.Raw, 1, sizeof(uint));

            positionBuffer.SetData(positions);
            scaleBuffer.SetData(scales);
            rotationBuffer.SetData(rotations);

            int clearKernel = shader.FindKernel("Clear");
            int cullKernel = shader.FindKernel("CullUncompressed");
            shader.SetBuffer(clearKernel, "_VisibleCountBuffer", countBuffer);
            shader.Dispatch(clearKernel, 1, 1, 1);

            var projection = GL.GetGPUProjectionMatrix(Matrix4x4.Perspective(60, 1, 0.1f, 100), false);
            shader.SetInt("_CandidateCount", 3);
            shader.SetInt("_UseCandidateOrder", 0);
            shader.SetInt("_EyeCount", 1);
            shader.SetVector("_ViewportSize", new Vector4(1024, 1024, 0, 0));
            shader.SetMatrix("_MatrixMV0", Matrix4x4.identity);
            shader.SetMatrix("_MatrixMV1", Matrix4x4.identity);
            shader.SetMatrix("_MatrixMVDepth", Matrix4x4.identity);
            shader.SetMatrix("_MatrixP0", projection);
            shader.SetMatrix("_MatrixP1", projection);
            shader.SetBuffer(cullKernel, "_PositionBuffer", positionBuffer);
            shader.SetBuffer(cullKernel, "_ScaleBuffer", scaleBuffer);
            shader.SetBuffer(cullKernel, "_RotationBuffer", rotationBuffer);
            shader.SetBuffer(cullKernel, "_CandidateOrderBuffer", candidateBuffer);
            shader.SetBuffer(cullKernel, "_OrderBuffer", orderBuffer);
            shader.SetBuffer(cullKernel, "_DepthBuffer", depthBuffer);
            shader.SetBuffer(cullKernel, "_VisibleCountBuffer", countBuffer);
            shader.Dispatch(cullKernel, 1, 1, 1);

            var visibleCount = new uint[1];
            countBuffer.GetData(visibleCount);
            Assert.That(visibleCount[0], Is.EqualTo(2));
        }
    }
}
