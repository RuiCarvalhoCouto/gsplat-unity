// Copyright (c) 2025 Yize Wu
// SPDX-License-Identifier: MIT

using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace Gsplat
{
    [ExecuteAlways]
    public class GsplatRenderer : MonoBehaviour, IGsplat
    {
        public enum GsplatSortMode
        {
            Always,
            SortEveryNFrames,
            CutoutsEveryNSorts,
        }

        public GsplatAsset GsplatAsset;

        // Range is enforced by GsplatRendererEditor based on the bound asset's SHBands.
        public int SHDegree = 3;
        [HideInInspector] public uint RenderOrder = 0;
        public float Brightness = 1.0f;

        [Tooltip(
            "Improves rendering speed by shrinking Gaussian splats while trying to keep the impact on visual quality as small as possible.")]
        [Range(0, 1)]
        public float SplatDownscaleFactor = 0.0f;

        public bool GammaToLinear;
        [Tooltip("Filters splats whose projected footprint is outside the camera view before sorting and drawing.")]
        public bool EnableFrustumCulling;
        [Tooltip(
            "Shrinks spatial chunk bounds toward their Gaussian centers. 0 preserves conservative visibility; higher values can improve rejection at the cost of edge popping.")]
        [Range(0, 1)]
        public float ChunkCullingAggressiveness;
        public bool AsyncUpload;
        public bool RenderBeforeUploadComplete = true;

        [Tooltip("Does cutouts update the Gsplat world bounds? (Costly on moving cutouts)")]
        public bool CutoutsUpdateBounds = true;

        GsplatAsset m_prevAsset;
        GsplatRendererImpl m_renderer;
        bool m_warnedFrustumCullingUnavailable;

        public bool Valid => GsplatAsset &&
                             (RenderBeforeUploadComplete ? SplatCount > 0 : SplatCount == GsplatAsset.SplatCount);

        public uint SplatCount => m_renderer != null ? m_renderer.GsplatResource?.UploadedCount ?? 0 : 0;

        public ISorterResource SorterResource => m_renderer.SorterResource;

        // IGsplat global-merge members: expose per-renderer GPU buffers for the global sorter.
        public GsplatResource GsplatResource => m_renderer?.GsplatResource;
        public byte SHBands => GsplatAsset?.SHBands ?? 0;


        public uint RemainingCount
        {
            get => m_renderer.m_remainingCount;
            set => m_renderer.m_remainingCount = value;
        }

        public Bounds Bounds
        {
            get => m_renderer.m_bounds;
            set => m_renderer.m_bounds = value;
        }

        public GsplatCutout[] Cutouts
        {
            get
            {
                var cutouts = GsplatCutout.m_RegisteredCutouts
                    .Where(component => component.enabled)
                    .Where(component =>
                        component.m_Target == GsplatCutout.Target.All ||
                        (component.m_Target == GsplatCutout.Target.Parent && component.transform.parent == transform) ||
                        (component.m_Target == GsplatCutout.Target.Specific && component.m_SpecifcRenderer == this)
                    );
                return cutouts.ToArray();
            }
        }

        public bool ComputeSortRequired => m_renderer.ComputeSortRequired;
        public bool ComputeCutoutsRequired => m_renderer.ComputeCutoutsRequired;
        internal bool FrustumCullingActive => m_renderer is { FrustumCullingActive: true };
        internal GraphicsBuffer VisibleCountBuffer => m_renderer?.VisibleCountBuffer;
        internal GraphicsBuffer HierarchyCountsBuffer => m_renderer?.HierarchyCountsBuffer;
        internal bool HierarchicalCullingActive => m_renderer is { HierarchicalCullingActive: true };
        internal bool LodCullingActive => m_renderer is { LodCullingActive: true };
        internal GraphicsBuffer SortDispatchArgs => m_renderer?.SortDispatchArgs;
        public GsplatSortMode SortMode = GsplatSortMode.Always;
        [HideInInspector] public uint SortRefreshRate = 1;
        [HideInInspector] public uint CutoutsRefreshRate = 1;

        public void ComputeDepth(CommandBuffer cmd, Matrix4x4 matrixMv) => m_renderer.ComputeDepth(cmd, matrixMv);
        internal void Cull(CommandBuffer cmd, in GsplatCameraInfo cameraInfo) =>
            m_renderer.Cull(cmd, cameraInfo, transform, ChunkCullingAggressiveness, SHDegree);
        internal void Project(CommandBuffer cmd, in GsplatCameraInfo cameraInfo) =>
            m_renderer.Project(cmd, cameraInfo, transform, SHDegree);

        void Reset()
        {
            EnableFrustumCulling = true;
        }

        void OnEnable()
        {
            GsplatSorter.Instance.RegisterGsplat(this);
            m_prevAsset = null;
        }

        void OnDisable()
        {
            GsplatSorter.Instance.UnregisterGsplat(this);
            m_renderer?.Dispose();
            m_renderer = null;
        }

        public void ForceRefresh()
        {
            m_renderer?.ForceRefresh();
        }

#if UNITY_EDITOR
        public void OnDrawGizmos()
        {
            if (GsplatSettings.Instance.DisplayBoundingBoxes && Valid && isActiveAndEnabled)
            {
                Gizmos.matrix = transform.localToWorldMatrix;
                Gizmos.color = Color.green;
                Gizmos.DrawWireCube(Bounds.center, Bounds.size);
            }
        }

        [SerializeField, HideInInspector] string m_assetGuid;
        public string AssetGuid => m_assetGuid;
#endif // #if UNITY_EDITOR

        void OnValidate()
        {
            ForceRefresh();
#if UNITY_EDITOR
            if (GsplatAsset &&
                AssetDatabase.TryGetGUIDAndLocalFileIdentifier(GsplatAsset, out var guid, out long localId))
                m_assetGuid = guid;
#endif // #if UNITY_EDITOR
        }

        public void ReloadAsset()
        {
            m_prevAsset = null;
        }

        public void Update()
        {
            if (!GsplatAsset)
                m_prevAsset = null;
            if (m_prevAsset != GsplatAsset)
            {
                m_renderer?.ReleaseGsplatAsset();
                m_prevAsset = GsplatAsset;
                if (GsplatAsset)
                {
                    if (m_renderer == null)
                        m_renderer = new GsplatRendererImpl(GsplatAsset.SplatCount);
                    else
                        m_renderer.RecreateResources(GsplatAsset.SplatCount);
#if UNITY_EDITOR
                    var asyncUpload = AsyncUpload && Application.isPlaying;
#else
                    var asyncUpload = AsyncUpload;
#endif
                    m_renderer.BindGsplatAsset(GsplatAsset, asyncUpload);
                    GsplatSorter.Instance.MarkGlobalBuffersDirty();
                }
            }

            if (Valid && GsplatSettings.Instance.Valid && GsplatSorter.Instance.Valid)
            {
                bool cullingSupported = GsplatAsset.GsplatMaterial.FrustumCullShader &&
                                        SystemInfo.supportsIndirectArgumentsBuffer;
                if (EnableFrustumCulling && !cullingSupported && !m_warnedFrustumCullingUnavailable)
                {
                    Debug.LogWarning(
                        $"[Gsplat] Frustum culling is unavailable for '{name}'; using the existing render path.", this);
                    m_warnedFrustumCullingUnavailable = true;
                }
                else if (cullingSupported)
                    m_warnedFrustumCullingUnavailable = false;
                m_renderer.SetFrustumCulling(EnableFrustumCulling && cullingSupported);
                m_renderer.NotifyTransform(transform);
                m_renderer.EvaluateRefreshRequired(SortMode, SortRefreshRate - 1, CutoutsRefreshRate - 1);
                m_renderer.DispatchInitOrder(Cutouts, transform.localToWorldMatrix, CutoutsUpdateBounds);
                // When the global sorter has merged all renderers into a single draw call,
                // skip the per-renderer draw — GsplatSorter.DrawAll handles rendering.
                if (!GsplatSorter.Instance.GlobalRenderEnabled)
                    m_renderer.Render(transform, gameObject.layer, GammaToLinear, SHDegree, Brightness,
                        1.0f - SplatDownscaleFactor, RenderOrder);
            }
        }
    }
}
