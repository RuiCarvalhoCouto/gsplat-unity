// Copyright (c) 2025 Niantic Spatial
// SPDX-License-Identifier: MIT

using System.IO;
using UnityEditor;
using UnityEditor.AssetImporters;

namespace Gsplat.Editor
{
    [CustomEditor(typeof(GsplatImporter))]
    public class GsplatImporterEditor : ScriptedImporterEditor
    {
        public override void OnInspectorGUI()
        {
            serializedObject.Update();

            EditorGUILayout.PropertyField(serializedObject.FindProperty("Compression"));
            EditorGUILayout.PropertyField(serializedObject.FindProperty("SourceCoordinates"));

            var importer = target as GsplatImporter;
            var assetPath = importer ? importer.assetPath : string.Empty;
            var ext = Path.GetExtension(assetPath).ToLowerInvariant();
            if (ext == ".ply")
                EditorGUILayout.PropertyField(serializedObject.FindProperty("OpacityPruneThreshold"));

            serializedObject.ApplyModifiedProperties();
            ApplyRevertGUI();
        }
    }
}