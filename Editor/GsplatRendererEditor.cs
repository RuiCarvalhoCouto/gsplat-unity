// Copyright (c) 2025 Yize Wu
// SPDX-License-Identifier: MIT

using UnityEditor;
using UnityEngine;

namespace Gsplat.Editor
{
    [CustomEditor(typeof(GsplatRenderer))]
    public class GsplatRendererEditor : UnityEditor.Editor
    {
        static void DrawHeader(string label)
        {
            EditorGUILayout.Space();
            EditorGUILayout.LabelField(label, EditorStyles.boldLabel);
        }

        public override void OnInspectorGUI()
        {
            serializedObject.Update();

            var renderer = (GsplatRenderer)target;
            if (GsplatSettings.Instance.EnableGlobalSort
                && renderer.GsplatAsset
                && renderer.GsplatAsset.Compression == CompressionMode.Uncompressed)
            {
                EditorGUILayout.HelpBox(
                    "Global sort is enabled, but this renderer uses an uncompressed asset. " +
                    "Global sort is all-or-nothing: if any active renderer is uncompressed, the " +
                    "whole scene falls back to per-renderer rendering. Re-import with Spark " +
                    "compression to enable cross-renderer depth ordering.",
                    MessageType.Warning);
            }

            DrawHeader("Asset");
            EditorGUILayout.PropertyField(
                serializedObject.FindProperty(nameof(GsplatRenderer.GsplatAsset)));

            // SH degree: slider max equals the bound asset's SHBands (so a degree-3 asset
            // shows 0–3, a degree-4 SPZ shows 0–4). Without an asset, fall back to 3.
            DrawHeader("Appearance");
            int maxShBands = renderer.GsplatAsset ? renderer.GsplatAsset.SHBands : 3;
            var shDegreeProp = serializedObject.FindProperty(nameof(GsplatRenderer.SHDegree));
            shDegreeProp.intValue = EditorGUILayout.IntSlider(
                new GUIContent("SH Degree", shDegreeProp.tooltip), shDegreeProp.intValue, 0, maxShBands);

            var brightnessProp = serializedObject.FindProperty(nameof(GsplatRenderer.Brightness));
            float brightness = brightnessProp.floatValue;

            // Use log scale for the slider UX
            // range from -3 (~5%) to 3 (~20x)
            float logVal = UnityEngine.Mathf.Log(
                UnityEngine.Mathf.Max(0.001f, brightness)
            );
            logVal = EditorGUILayout.Slider(
                new GUIContent("Log Brightness",
                    "Adjusts brightness on a logarithmic scale for fine control across a wide range."),
                logVal, -4.0f, 3.0f);
            brightness = EditorGUILayout.FloatField(
                new GUIContent("Brightness", brightnessProp.tooltip), UnityEngine.Mathf.Exp(logVal)
            );
            brightnessProp.floatValue = brightness;
            EditorGUILayout.PropertyField(
                serializedObject.FindProperty(nameof(GsplatRenderer.SplatDownscaleFactor)));
            EditorGUILayout.PropertyField(
                serializedObject.FindProperty(nameof(GsplatRenderer.GammaToLinear)));

            var renderOrderProp = serializedObject.FindProperty(nameof(GsplatRenderer.RenderOrder));
            if (GsplatSettings.Instance.MaxRenderOrder > 1)
                renderOrderProp.intValue = EditorGUILayout.IntSlider(
                    new GUIContent("Render Order",
                        "Controls transparent render ordering between Gaussian renderers."),
                    renderOrderProp.intValue, 0, (int)GsplatSettings.Instance.MaxRenderOrder - 1);

            DrawHeader("Visibility");
            var cullingProp =
                serializedObject.FindProperty(nameof(GsplatRenderer.EnableFrustumCulling));
            EditorGUILayout.PropertyField(cullingProp);
            using (new EditorGUI.DisabledScope(!cullingProp.boolValue))
            {
                EditorGUI.indentLevel++;
                EditorGUILayout.PropertyField(
                    serializedObject.FindProperty(nameof(GsplatRenderer.ChunkCullingAggressiveness)));
                EditorGUI.indentLevel--;
            }

            DrawHeader("Sorting");
            EditorGUILayout.PropertyField(serializedObject.FindProperty(nameof(GsplatRenderer.SortMode)));

            // Sort Refresh Rate slider only if on correct mode
            if (renderer.SortMode == GsplatRenderer.GsplatSortMode.SortEveryNFrames ||
                renderer.SortMode == GsplatRenderer.GsplatSortMode.CutoutsEveryNSorts)
            {
                var newSortRefreshRate = (uint)EditorGUILayout.IntSlider(
                    new GUIContent("Sort Refresh Rate",
                        "Number of rendered frames between depth-sort refreshes."),
                    (int)renderer.SortRefreshRate, 1, 60);
                if (newSortRefreshRate != renderer.SortRefreshRate)
                {
                    renderer.SortRefreshRate = newSortRefreshRate;
                    renderer.ForceRefresh();
                }
            }

            // Cutouts Refresh Rate slider only if on correct mode
            if (renderer.SortMode == GsplatRenderer.GsplatSortMode.CutoutsEveryNSorts)
            {
                var newCutoutsRefreshRate = (uint)EditorGUILayout.IntSlider(
                    new GUIContent("Cutouts Refresh Rate",
                        "Number of depth-sort refreshes between cutout-mask refreshes."),
                    (int)renderer.CutoutsRefreshRate, 1, 60);
                if (newCutoutsRefreshRate != renderer.CutoutsRefreshRate)
                {
                    renderer.CutoutsRefreshRate = newCutoutsRefreshRate;
                    renderer.ForceRefresh();
                }
            }

            DrawHeader("Loading");
            EditorGUILayout.PropertyField(serializedObject.FindProperty(nameof(GsplatRenderer.AsyncUpload)));
            if (serializedObject.FindProperty(nameof(GsplatRenderer.AsyncUpload)).boolValue)
            {
                EditorGUI.indentLevel++;
                EditorGUILayout.PropertyField(
                    serializedObject.FindProperty(nameof(GsplatRenderer.RenderBeforeUploadComplete)));
                EditorGUI.indentLevel--;
            }

            DrawHeader("Cutouts");
            EditorGUILayout.PropertyField(
                serializedObject.FindProperty(nameof(GsplatRenderer.CutoutsUpdateBounds)));

            serializedObject.ApplyModifiedProperties();
        }
    }
}
