// Copyright (c) 2025 Yize Wu
// SPDX-License-Identifier: MIT

using System;
using System.Globalization;
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

namespace Gsplat
{
    public class GsplatCullingDiagnosticsWindow : EditorWindow
    {
        GsplatRenderer m_renderer;
        bool m_requestPending;
        bool m_acceptResult;
        ulong m_requestRendererId;
        uint m_requestCandidateCount;
        uint m_candidateCount;
        uint m_visibleCount;
        bool m_hasSample;
        string m_sampleLabel;
        string m_requestLabel;
        string m_logPath;
        string m_status = "No sample captured.";

        [MenuItem("Window/Gsplat/Culling Diagnostics")]
        static void Open()
        {
            GetWindow<GsplatCullingDiagnosticsWindow>("Gsplat Culling");
        }

        void OnEnable()
        {
            m_acceptResult = true;
            SelectRendererIfUnset();
        }

        void OnDisable()
        {
            m_acceptResult = false;
        }

        void OnSelectionChange()
        {
            SelectRendererIfUnset();
        }

        void SelectRendererIfUnset()
        {
            if (m_renderer || !Selection.activeGameObject ||
                !Selection.activeGameObject.TryGetComponent(out GsplatRenderer renderer))
                return;

            m_renderer = renderer;
            ClearSample();
            Repaint();
        }

        void OnGUI()
        {
            var renderer = (GsplatRenderer)EditorGUILayout.ObjectField(
                "Renderer", m_renderer, typeof(GsplatRenderer), true);
            if (renderer != m_renderer)
            {
                m_renderer = renderer;
                ClearSample();
            }

            m_sampleLabel = EditorGUILayout.TextField("Sample Label", m_sampleLabel);

            string unavailableReason = GetUnavailableReason();
            using (new EditorGUI.DisabledScope(unavailableReason != null))
            {
                if (GUILayout.Button(m_requestPending ? "Capture Pending..." : "Capture Once"))
                    Capture();
            }

            if (unavailableReason != null)
                EditorGUILayout.HelpBox(unavailableReason, MessageType.Info);

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Status", m_status);
            if (!string.IsNullOrEmpty(m_logPath))
                EditorGUILayout.LabelField($"Log File: {m_logPath}", EditorStyles.wordWrappedLabel);
            if (!m_hasSample)
                return;

            EditorGUILayout.LabelField("Candidates", m_candidateCount.ToString("N0"));
            EditorGUILayout.LabelField("Visible", m_visibleCount.ToString("N0"));
            string percentage = m_candidateCount == 0
                ? "N/A"
                : ((double)m_visibleCount / m_candidateCount).ToString("P1");
            EditorGUILayout.LabelField("Visible Percentage", percentage);
        }

        string GetUnavailableReason()
        {
            if (m_requestPending)
                return "GPU readback already pending.";
            if (!Application.isPlaying)
                return "Enter Play Mode before capturing.";
            if (!m_renderer)
                return "Select a Gsplat Renderer.";
            if (!m_renderer.isActiveAndEnabled)
                return "Selected renderer is disabled.";
            if (!m_renderer.FrustumCullingActive)
                return "Frustum culling is not active for selected renderer.";
            if (m_renderer.VisibleCountBuffer == null)
                return "Visible-count buffer is unavailable.";
            return null;
        }

        void Capture()
        {
            var buffer = m_renderer.VisibleCountBuffer;
            m_requestRendererId = GsplatUtils.GetObjectId(m_renderer);
            m_requestCandidateCount = m_renderer.RemainingCount;
            m_requestLabel = m_sampleLabel;
            m_requestPending = true;
            m_hasSample = false;
            m_status = "Waiting for GPU readback.";

            try
            {
                AsyncGPUReadback.Request(buffer, OnReadbackComplete);
            }
            catch (Exception exception)
            {
                m_requestPending = false;
                m_status = $"Could not request GPU readback: {exception.Message}";
            }
        }

        void OnReadbackComplete(AsyncGPUReadbackRequest request)
        {
            m_requestPending = false;
            if (!m_acceptResult)
                return;

            if (!m_renderer || GsplatUtils.GetObjectId(m_renderer) != m_requestRendererId)
            {
                m_status = "Discarded sample because selected renderer changed.";
                Repaint();
                return;
            }

            if (request.hasError)
            {
                m_status = "GPU readback failed.";
                Repaint();
                return;
            }

            var data = request.GetData<uint>();
            if (data.Length == 0)
            {
                m_status = "GPU readback returned no data.";
                Repaint();
                return;
            }

            m_candidateCount = m_requestCandidateCount;
            m_visibleCount = data[0];
            m_hasSample = true;
            SaveSample();
            Repaint();
        }

        void SaveSample()
        {
            try
            {
                if (string.IsNullOrEmpty(m_logPath))
                    m_logPath = CreateLogPath();

                bool writeHeader = !File.Exists(m_logPath);
                using var writer = new StreamWriter(m_logPath, true);
                if (writeHeader)
                    writer.WriteLine(
                        "Timestamp,Label,UnityVersion,GraphicsAPI,RenderPipeline,Scene,Renderer,Asset,AssetType,SHDegree,ViewportWidth,ViewportHeight,Candidates,Visible,VisiblePercent");

                double visiblePercent = m_candidateCount == 0
                    ? 0
                    : (double)m_visibleCount / m_candidateCount * 100.0;
                var asset = m_renderer.GsplatAsset;
                writer.WriteLine(string.Join(",",
                    Csv(DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff", CultureInfo.InvariantCulture)),
                    Csv(m_requestLabel),
                    Csv(Application.unityVersion),
                    Csv(SystemInfo.graphicsDeviceType.ToString()),
                    Csv(GraphicsSettings.currentRenderPipeline
                        ? GraphicsSettings.currentRenderPipeline.GetType().Name
                        : "Built-in"),
                    Csv(SceneManager.GetActiveScene().path),
                    Csv(m_renderer.name),
                    Csv(asset ? asset.name : ""),
                    Csv(asset ? asset.GetType().Name : ""),
                    m_renderer.SHDegree.ToString(CultureInfo.InvariantCulture),
                    Screen.width.ToString(CultureInfo.InvariantCulture),
                    Screen.height.ToString(CultureInfo.InvariantCulture),
                    m_candidateCount.ToString(CultureInfo.InvariantCulture),
                    m_visibleCount.ToString(CultureInfo.InvariantCulture),
                    visiblePercent.ToString("F3", CultureInfo.InvariantCulture)));
                m_status = "Sample captured and logged.";
            }
            catch (Exception exception)
            {
                m_status = $"Sample captured, but log write failed: {exception.Message}";
            }
        }

        static string CreateLogPath()
        {
            string projectRoot = Path.GetDirectoryName(Application.dataPath);
            string directory = Path.Combine(projectRoot, "ProfilerCaptures");
            Directory.CreateDirectory(directory);
            string timestamp = DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss", CultureInfo.InvariantCulture);
            string path = Path.Combine(directory, $"{timestamp}_GsplatCulling.csv");
            for (int suffix = 2; File.Exists(path); ++suffix)
                path = Path.Combine(directory, $"{timestamp}_GsplatCulling_{suffix}.csv");
            return path;
        }

        static string Csv(string value)
        {
            return $"\"{(value ?? "").Replace("\"", "\"\"")}\"";
        }

        void ClearSample()
        {
            m_hasSample = false;
            m_status = "No sample captured.";
        }
    }
}
