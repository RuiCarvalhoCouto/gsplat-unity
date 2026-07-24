using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace Gsplat.Tests
{
    public class GsplatFrustumCullTests
    {
        [Test]
        public void NewRendererEnablesFrustumCulling()
        {
            var gameObject = new GameObject();
            try
            {
                var renderer = gameObject.AddComponent<GsplatRenderer>();
                Assert.That(renderer.EnableFrustumCulling, Is.True);
            }
            finally
            {
                Object.DestroyImmediate(gameObject);
            }
        }

        [Test]
        public void SerializedDisabledCullingIsPreserved()
        {
            var sourceObject = new GameObject();
            var destinationObject = new GameObject();
            try
            {
                var source = sourceObject.AddComponent<GsplatRenderer>();
                source.EnableFrustumCulling = false;
                var serializedRenderer = EditorJsonUtility.ToJson(source);

                var destination = destinationObject.AddComponent<GsplatRenderer>();
                Assert.That(destination.EnableFrustumCulling, Is.True);

                EditorJsonUtility.FromJsonOverwrite(serializedRenderer, destination);
                Assert.That(destination.EnableFrustumCulling, Is.False);
            }
            finally
            {
                Object.DestroyImmediate(sourceObject);
                Object.DestroyImmediate(destinationObject);
            }
        }

        [Test]
        public void StaticSortDoesNotRequireDynamicCountBuffer()
        {
            var shader = AssetDatabase.LoadAssetAtPath<ComputeShader>(
                "Packages/rui.couto.gsplat/Runtime/Shaders/Gsplat.compute");
            Assert.That(shader, Is.Not.Null);

            var sortPass = new GsplatSortPass(shader);
            Assert.That(sortPass.Valid, Is.True);

            using var keys = new GraphicsBuffer(GraphicsBuffer.Target.Structured, 3, sizeof(float));
            using var values = new GraphicsBuffer(GraphicsBuffer.Target.Structured, 3, sizeof(uint));
            using var commandBuffer = new CommandBuffer();
            var resources = GsplatSortPass.SupportResources.Load(3);

            try
            {
                keys.SetData(new[] { 3.0f, -1.0f, 0.5f });
                values.SetData(new uint[] { 0, 1, 2 });
                sortPass.Dispatch(commandBuffer, new GsplatSortPass.Args
                {
                    Count = 3,
                    InputKeys = keys,
                    InputValues = values,
                    Resources = resources
                });
                Graphics.ExecuteCommandBuffer(commandBuffer);

                var sortedKeys = new float[3];
                var sortedValues = new uint[3];
                keys.GetData(sortedKeys);
                values.GetData(sortedValues);
                Assert.That(sortedKeys, Is.EqualTo(new[] { -1.0f, 0.5f, 3.0f }));
                Assert.That(sortedValues, Is.EqualTo(new uint[] { 1, 2, 0 }));
            }
            finally
            {
                resources.Dispose();
            }
        }

        [Test]
        public void CullUncompressedRetainsOverlappingFootprintsOnly()
        {
            var shader = AssetDatabase.LoadAssetAtPath<ComputeShader>(
                "Packages/rui.couto.gsplat/Runtime/Shaders/GsplatFrustumCull.compute");
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
            using var sortArgsBuffer = new GraphicsBuffer(GraphicsBuffer.Target.IndirectArguments, 1,
                sizeof(uint) * 3);
            using var drawArgsBuffer = new GraphicsBuffer(GraphicsBuffer.Target.IndirectArguments, 1,
                GraphicsBuffer.IndirectDrawIndexedArgs.size);

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

            shader.Dispatch(clearKernel, 1, 1, 1);
            shader.SetInt("_EyeCount", 2);
            shader.SetMatrix("_MatrixMV0", Matrix4x4.Translate(new Vector3(100, 0, 0)));
            shader.SetMatrix("_MatrixMV1", Matrix4x4.identity);
            shader.Dispatch(cullKernel, 1, 1, 1);

            countBuffer.GetData(visibleCount);
            Assert.That(visibleCount[0], Is.EqualTo(2),
                "Candidates visible only to the second active view must be retained.");

            int buildArgsKernel = shader.FindKernel("BuildArgs");
            shader.SetInt("_SplatInstanceSize", 1);
            shader.SetInt("_IndexCountPerInstance", 6);
            shader.SetInt("_StartIndex", 2);
            shader.SetInt("_BaseVertex", 3);
            shader.SetBuffer(buildArgsKernel, "_VisibleCountBuffer", countBuffer);
            shader.SetBuffer(buildArgsKernel, "_SortDispatchArgs", sortArgsBuffer);
            shader.SetBuffer(buildArgsKernel, "_DrawArgs", drawArgsBuffer);
            shader.Dispatch(buildArgsKernel, 1, 1, 1);

            var drawArgs = new GraphicsBuffer.IndirectDrawIndexedArgs[1];
            drawArgsBuffer.GetData(drawArgs);
            Assert.That(drawArgs[0].indexCountPerInstance, Is.EqualTo(6));
            Assert.That(drawArgs[0].instanceCount, Is.EqualTo(2));
            Assert.That(drawArgs[0].startIndex, Is.EqualTo(2));
            Assert.That(drawArgs[0].baseVertexIndex, Is.EqualTo(3));
            Assert.That(drawArgs[0].startInstance, Is.Zero);
        }
    }
}
