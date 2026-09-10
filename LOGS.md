## 2026-07-20

- Files changed: `CHANGELOG.md`, `Runtime/GsplatGlobalRenderer.cs`, `Runtime/GsplatMaterial.cs`, `Runtime/GsplatRenderer.cs`, `Runtime/GsplatRendererImpl.cs`, `Runtime/GsplatSortPass.cs`, `Runtime/GsplatSorter.cs`, `Runtime/Materials/GsplatSpark.asset`, `Runtime/Materials/GsplatUncompressed.asset`, `Runtime/Shaders/DeviceRadixSort.hlsl`, `Runtime/Shaders/Gsplat.compute`, `Runtime/Shaders/Gsplat.shader`, `Runtime/Shaders/GsplatFrustumCull.compute`, `Runtime/Shaders/GsplatFrustumCull.compute.meta`, `Runtime/Shaders/GsplatGlobal.shader`, `Runtime/Shaders/GsplatMergeOrderBuffers.compute`, `Runtime/Shaders/GsplatSparkGlobal.hlsl`, `Runtime/Shaders/SortCommon.hlsl`, `Tests~/Editor/Gsplat.FrustumCulling.Tests.asmdef`, `Tests~/Editor/GsplatFrustumCullTests.cs`
- Summary: Added opt-in GPU projected-footprint frustum culling with GPU-resident visible counts, dynamic sorting, indirect per-renderer and global draws, and separated validation tests.
- Reason: Avoid depth sorting and drawing Gaussians that cannot overlap the active camera view while preserving large edge-overlapping splats and existing render behavior when disabled.

<br>

- Files changed: `Tests~/FRUSTUM_CULLING_TEST_CHECKLIST.md`
- Summary: Added a manual release checklist covering Unity version boundaries, rendering correctness, asset paths, sorting, indirect drawing, cutouts, global sorting, render pipelines, graphics APIs, XR, lifecycle, and performance.
- Reason: Define reproducible pass criteria for validating GPU frustum culling across supported configurations, especially the Unity 6.4 object-ID API boundary.

<br>

- Files changed: `Runtime/GsplatGlobalRenderer.cs`, `Runtime/GsplatRendererImpl.cs`, `Runtime/Shaders/GsplatFrustumCull.compute`, `Runtime/Shaders/GsplatMergeOrderBuffers.compute`, `Tests~/Editor/GsplatFrustumCullTests.cs`
- Summary: Replaced hard-coded indexed indirect-draw argument sizes and byte offsets with Unity's platform-defined `IndirectDrawIndexedArgs` layout in the per-renderer and global paths, with a focused GPU assertion for the generated command fields.
- Reason: Keep GPU-generated indirect draw commands portable across supported graphics APIs and Unity versions.

<br>

## 2026-07-22

- Files changed: `Runtime/GsplatRendererImpl.cs`, `Runtime/GsplatSortPass.cs`, `Runtime/Shaders/DeviceRadixSort.hlsl`, `Runtime/Shaders/Gsplat.compute`, `Runtime/Shaders/SortCommon.hlsl`, `Tests~/Editor/GsplatFrustumCullTests.cs`
- Summary: Bound the renderer-visible count buffer for every draw, split radix sorting into static-count and GPU-count kernels so each kernel declares only resources it uses, and added a static-sort regression test.
- Reason: Fix D3D12 rejecting unculled draws and static sort dispatches because optional shader buffers were left unbound.

<br>

- Files changed: `Runtime/GsplatAsset.cs`, `Runtime/GsplatAssetSpark.cs`, `Runtime/GsplatAssetUncompressed.cs`, `Runtime/GsplatSorter.cs`, `Runtime/GsplatUtils.cs`, `Runtime/Shaders/GsplatFrustumCull.compute`, `Tests~/Editor/GsplatFrustumCullTests.cs`, `Tests~/FRUSTUM_CULLING_TEST_CHECKLIST.md`
- Summary: Batched large GPU uploads, corrected the renamed package asset path, removed a Vulkan culling-shader warning, and made the Play Mode Scene view retain the Game camera's culling result.
- Reason: Prevent Unity 6.3 D3D12 device removal on large models, restore fresh-project asset lookup and tests, and make culling observable from the Editor without reculling from the Scene camera.

<br>

- Files changed: `Runtime/GsplatRenderer.cs`, `Runtime/GsplatRendererImpl.cs`, `Runtime/GsplatSorter.cs`, `Runtime/SRP/GsplatURPFeature.cs`, `Tests~/FRUSTUM_CULLING_TEST_CHECKLIST.md`
- Summary: Routed Unity 6 URP culling through active render-pass view matrices and XR viewport dimensions while retaining the existing camera fallback for other render paths.
- Reason: Prevent XR eye-edge over-culling caused by evaluating projected footprints with mirror/Game View dimensions instead of active OpenXR eye targets, and handle single-pass instanced and multipass view counts correctly.

<br>

- Files changed: `Runtime/SRP/GsplatURPFeature.cs`
- Summary: Used Unity's single-pass stereo culling view and projection matrices for Unity 6 URP XR while retaining the current-eye render matrices for multipass.
- Reason: Keep GPU frustum culling conservative when Unity late-latches the stereo render pose after custom compute parameters have been recorded.

<br>

- Files changed: `Runtime/Shaders/GsplatFrustumCull.compute`, `Runtime/Shaders/GsplatMergeOrderBuffers.compute`, `Tests~/FRUSTUM_CULLING_TEST_CHECKLIST.md`
- Summary: Expanded GPU-generated indirect instance counts only for instanced-stereo shader variants in both per-renderer and global draw paths.
- Reason: Single Pass Instanced rendering consumed two hardware instances per logical splat batch, causing each eye to draw only the back half of the depth-sorted order buffer.

<br>

## 2026-07-23

- Files changed: `Tests~/FRUSTUM_CULLING_TEST_CHECKLIST.md`
- Summary: Replaced the exhaustive frustum-culling checklist with compact compatibility, correctness, lifecycle, player, and performance release gates while preserving historical Unity-version results.
- Reason: Keep manual validation proportional to the optimization checkpoints and clearly separate previous compatibility results from the new branch validation.

<br>

- Files changed: `Runtime/Shaders/GsplatFrustumCull.compute`, `Tests~/Editor/GsplatFrustumCullTests.cs`
- Summary: Reused each candidate's model-space covariance across active views and stopped view testing after the first overlap, with coverage for candidates visible only to the second view.
- Reason: Remove duplicated per-view covariance and projection work from the GPU frustum-culling path without changing its union-of-active-views visibility rule.

<br>

## 2026-07-24

- Files changed: `Runtime/GsplatRenderer.cs`, `Tests~/Editor/GsplatFrustumCullTests.cs`
- Summary: Enabled frustum culling when a new renderer component is added while preserving explicitly serialized disabled values, with focused editor tests for both behaviors.
- Reason: Make the optimization the default for new usage without changing existing scenes created before the feature.

<br>

- Files changed: `Editor/GsplatCullingDiagnosticsWindow.cs`, `Editor/GsplatCullingDiagnosticsWindow.cs.meta`, `Runtime/AssemblyInfo.cs`, `Runtime/AssemblyInfo.cs.meta`, `TODO.md`
- Summary: Added a temporary Editor-only window for one-shot asynchronous readback of per-renderer candidate and visible Gaussian counts, with explicit cleanup tracking.
- Reason: Measure culling effectiveness at fixed test poses without continuous GPU readback or changes to the normal rendering path.

<br>

- Files changed: `Tests~/FRUSTUM_CULLING_TEST_CHECKLIST.md`
- Summary: Added new-component default checks and a fixed-pose, alternating-order, median-based performance protocol using one-shot visible-count diagnostics.
- Reason: Make compatibility and performance evidence repeatable across supported Unity versions and graphics APIs without relying on volatile FPS snapshots.

<br>

- Files changed: `Editor/GsplatCullingDiagnosticsWindow.cs`, `Editor/GsplatImporter.cs`
- Summary: Routed temporary diagnostics through the existing cross-version object ID helper and selected version-appropriate object discovery APIs.
- Reason: Restore clean compilation on Unity 6000.5 while preserving compatibility with Unity 2021.3 through 6000.4.

<br>

- Files changed: `Editor/GsplatCullingDiagnosticsWindow.cs`, `Tests~/FRUSTUM_CULLING_TEST_CHECKLIST.md`
- Summary: Saved successful one-shot culling samples to timestamped CSV files in the active project's `ProfilerCaptures` directory, including configuration metadata and optional pose labels.
- Reason: Keep visible-count evidence beside Unity profiler captures in a format suitable for later comparison and analysis.

<br>

- Files changed: `Editor/GsplatCullingDiagnosticsWindow.cs`, `Editor/GsplatImporter.cs`, `Editor/GsplatImporterEditor.cs`, `Runtime/AssemblyInfo.cs`, `Runtime/GsplatAsset.cs`, `Runtime/GsplatAssetSpark.cs`, `Runtime/GsplatAssetSpz.cs`, `Runtime/GsplatAssetSpzUncompressed.cs`, `Runtime/GsplatAssetUncompressed.cs`, `Runtime/GsplatRenderer.cs`, `Runtime/GsplatRendererImpl.cs`, `Runtime/GsplatResource.cs`, `Runtime/GsplatSpatialHierarchy.cs`, `Runtime/GsplatSpatialHierarchy.cs.meta`, `Runtime/Shaders/GsplatFrustumCull.compute`, `Runtime/Shaders/InitOrderSpark.compute`, `Runtime/Shaders/InitOrderUncompressed.compute`, `Tests~/Editor/GsplatFrustumCullTests.cs`, `Tests~/FRUSTUM_CULLING_TEST_CHECKLIST.md`
- Summary: Added import-time Morton ordering with configurable leaf sizes, two-level shared spatial bounds, indirect coarse and leaf culling, fully-inside leaf fast paths, conservative-to-center bound tuning, hierarchy-aware cutout masks, extended diagnostics, and focused validation coverage.
- Reason: Avoid covariance and projection work for every Gaussian each refresh while retaining automatic flat fallback, active-view union behavior, current depth-plane semantics, and existing rendering APIs.

<br>

- Files changed: `Runtime/GsplatRendererImpl.cs`
- Summary: Bound the visible-count buffer to the hierarchy-clear compute kernel.
- Reason: Ensure hierarchy counters reset every culling pass instead of Unity skipping the dispatch and allowing stale counters to accumulate.

<br>

- Files changed: `Editor/GsplatImporter.cs`, `Runtime/GsplatAsset.cs`, `Runtime/GsplatAssetSpark.cs`, `Runtime/GsplatAssetUncompressed.cs`, `Runtime/GsplatResource.cs`, `Runtime/GsplatSpatialHierarchy.cs`, `Tests~/Editor/GsplatFrustumCullTests.cs`
- Summary: Added importer-versioned bottom-up 8-way spatial LOD data with deterministic opacity-weighted Gaussian-mixture representatives, positive covariance-derived scales, normalized rotations, contribution-weighted color and SH, GPU resource uploads, and Spark cache serialization.
- Reason: Prepare reusable offline hierarchy and representative data for budgeted GPU LOD traversal without changing the existing render path.

<br>

- Files changed: `Editor/GsplatCullingDiagnosticsWindow.cs`, `Runtime/GsplatRenderer.cs`, `Runtime/GsplatRendererImpl.cs`, `Runtime/GsplatSorter.cs`, `Runtime/SRP/GsplatURPFeature.cs`, `Runtime/Shaders/Gsplat.hlsl`, `Runtime/Shaders/Gsplat.shader`, `Runtime/Shaders/GsplatFrustumCull.compute`
- Summary: Added a URP-only static fast path that traverses multilevel nodes on the GPU, selects stereo-conservative LOD representatives using an adaptive 12.5 ms budget, compacts selected splats with one global atomic per workgroup, and renders mixed original/LOD IDs through shared shader buffers.
- Reason: Reduce sorting, vertex, and fragment work for distant hierarchy regions while retaining existing rendering for dynamic transforms, cutouts, global rendering, non-URP pipelines, partial uploads, and unsupported assets.

<br>

- Files changed: `Runtime/GsplatRenderer.cs`, `Runtime/GsplatRendererImpl.cs`, `Runtime/GsplatResource.cs`, `Runtime/GsplatSorter.cs`, `Runtime/SRP/GsplatURPFeature.cs`, `Runtime/Shaders/Gsplat.hlsl`, `Runtime/Shaders/Gsplat.shader`, `Runtime/Shaders/GsplatFrustumCull.compute`
- Summary: Added a dense per-eye projection prepass for selected original and LOD splats, including one-time covariance and SH evaluation, high-bit projected sort payloads, lightweight quad vertices, and projection-only refresh when sort order is reused.
- Reason: Remove four repeated source decodes, covariance projections, and SH evaluations per rendered splat while preserving current fallback rendering and camera-correct projected data during temporal sort reuse.

<br>

## 2026-07-27

- Files changed: `Runtime/GsplatRenderer.cs`, `Runtime/GsplatRendererImpl.cs`, `Runtime/GsplatSortPass.cs`, `Runtime/GsplatSorter.cs`, `Tests~/Editor/GsplatFrustumCullTests.cs`
- Summary: Added stable two-pass sorting of the high 16 sortable depth bits for the hybrid path, a serialized exact 32-bit fallback, and one-frame order reuse guarded by tighter cumulative camera-motion limits.
- Reason: Halve hybrid radix-sort passes and avoid redundant sorting during small motion while preserving full-precision behavior outside the static LOD path and allowing direct quality comparison.

<br>

- Files changed: `Runtime/GsplatRenderer.cs`, `Runtime/GsplatRendererImpl.cs`, `Runtime/GsplatSettings.cs`, `Runtime/GsplatSorter.cs`, `Runtime/SRP/GsplatURPFeature.cs`, `Runtime/Materials/GsplatDepthAwareUpscale.mat`, `Runtime/Materials/GsplatDepthAwareUpscale.mat.meta`, `Runtime/Materials/GsplatProjectedOffscreen.mat`, `Runtime/Materials/GsplatProjectedOffscreen.mat.meta`, `Runtime/Shaders/GsplatDepthAwareUpscale.shader`, `Runtime/Shaders/GsplatDepthAwareUpscale.shader.meta`, `Runtime/Shaders/GsplatFrustumCull.compute`, `Runtime/Shaders/GsplatProjectedOffscreen.shader`, `Runtime/Shaders/GsplatProjectedOffscreen.shader.meta`
- Summary: Added contribution and peripheral LOD controls, opacity-support-aware projected bounds, and optional budget-driven GS-only adaptive resolution with a dedicated projected-splat draw and depth-aware URP composite.
- Reason: Reduce hybrid-path overdraw and pixel cost under GPU pressure while retaining native scene resolution, opaque-depth interaction, stereo-union selection, and existing fallback rendering.

<br>

- Files changed: `Runtime/GsplatRenderer.cs`, `Runtime/GsplatRendererImpl.cs`, `Runtime/GsplatSorter.cs`, `Runtime/Shaders/GsplatFrustumCull.compute`
- Summary: Fused hybrid projection, radix-sort dispatch, and indirect-draw argument generation into one GPU dispatch; reused camera storage; and removed empty-cutout and active-renderer hot-path allocations.
- Reason: Remove redundant command and managed allocation overhead from the hybrid renderer without changing fallback paths, source precision, or GPU buffer layouts.

<br>

- Files changed: `Tests~/FRUSTUM_CULLING_TEST_CHECKLIST.md`
- Summary: Added manual hybrid-renderer correctness, fallback, lifecycle, performance, VRAM, image-quality, and headset acceptance gates.
- Reason: Make remaining 10-million-splat validation explicit and prevent synthetic smoke tests from being mistaken for release-quality or XR performance evidence.

<br>

- Files changed: `Editor/GsplatRendererEditor.cs`, `Runtime/GsplatRenderer.cs`, `Runtime/Materials/GsplatProjectedOffscreen.mat`, `Runtime/Shaders/GsplatProjectedOffscreen.shader`
- Summary: Grouped renderer settings into contextual Inspector sections with expanded tooltips and adaptive-path requirements; restricted adaptive resolution to Play Mode; and matched the established indirect instance-ID shader path without ordinary instancing variants.
- Reason: Improve configuration clarity and prevent Edit Mode adaptive rendering plus an invalid non-instanced offscreen shader variant from triggering graphics-device instability.

<br>

- Files changed: `Runtime/GsplatRenderer.cs`, `Runtime/GsplatRendererImpl.cs`, `Runtime/Shaders/GsplatFrustumCull.compute`, `Tests~/Editor/GsplatFrustumCullTests.cs`
- Summary: Split oversized hierarchy, LOD expansion, and projection workloads across legal two-dimensional dispatches; restricted camera-dependent hybrid projection to Play Mode; deferred dense LOD projection buffers until the hybrid path first runs; and reused model-space covariance across stereo projections.
- Reason: Prevent D3D12 device hangs above the 65,535-group dispatch limit, keep Scene View on the camera-correct fallback path, avoid allocating nearly one gigabyte of projected data while editing a 10-million-splat model, and remove duplicate per-eye covariance work.

<br>

- Files changed: `Editor/GsplatImporter.cs`, `Runtime/GsplatAssetSpark.cs`, `Runtime/GsplatRenderer.cs`, `Runtime/GsplatRendererImpl.cs`, `Runtime/GsplatSorter.cs`, `Runtime/GsplatSpatialHierarchy.cs`, `Runtime/Shaders/Gsplat.hlsl`, `Runtime/Shaders/Gsplat.shader`, `Runtime/Shaders/GsplatFrustumCull.compute`, `Tests~/Editor/GsplatFrustumCullTests.cs`
- Summary: Restored camera-relative source rendering when adaptive resolution is disabled, limited dense projected buffers and IDs to the adaptive path, and stored generated LOD representative rotations in the shader-native wxyz layout with importer and SPZ-cache version invalidation.
- Reason: Remove the Standard shader's unconditional projected-buffer dependency, prevent camera-specific clip-space data from being drawn by unrelated cameras, and stop malformed LOD covariance from stretching representatives into spikes.

<br>

- Files changed: `Runtime/GsplatRendererImpl.cs`
- Summary: Allowed an eligible renderer's current transform to use hierarchical LOD on its first Play Mode frame while retaining fallback after an observed transform change.
- Reason: Prevent a 10-million-splat XR scene from executing a full-resolution exact-sort and stereo-draw fallback before transform stability could be established, which caused a D3D12 device timeout.

<br>

- Files changed: `Editor/GsplatRendererEditor.cs`, `Runtime/GsplatRenderer.cs`
- Summary: Restored contextual Asset, Appearance, Visibility, Sorting, Loading, and Cutouts Inspector sections with conditional controls and expanded field tooltips.
- Reason: Make renderer configuration and performance-quality trade-offs understandable without adding an external Inspector dependency.

<br>
