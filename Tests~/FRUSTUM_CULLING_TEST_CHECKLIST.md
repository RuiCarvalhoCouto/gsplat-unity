# GPU Frustum Culling Test Checklist

Use this compact checklist before merging or releasing GPU frustum culling. Record the Unity version, render pipeline, graphics API, XR mode, GPU, resolution, asset splat count, compression, and SH degree with each test run. Mark a gate `N/A` only when unsupported, and record why.

## Historical compatibility

These results predate the optimization branch and remain as compatibility history. They do not validate the optimization checkpoints.

- [ ] Unity 2021.3 ELTS compatibility subset. `N/A` — Unity version unavailable without a license.
- [ ] Unity 2022.3 ELTS compatibility subset. `N/A` — Unity version unavailable without a license.
- [x] Unity 2023.1 compatibility subset passed.
- [x] Unity 6000.3 compatibility subset passed.
- [x] Unity 6000.4 compatibility subset passed.
- [x] Unity 6000.5 compatibility subset passed.

## Optimization-branch compatibility

For each combination below: import the package; compile C#; import the culling, radix-sort, and render shaders; render one Spark and one Uncompressed asset; toggle culling; and confirm the Console has no new package errors, shader errors, graphics validation errors, or missing-buffer warnings.

- [ ] Unity 2023.1, D3D12.
- [ ] Unity 2023.1, Vulkan.
- [ ] Unity 6000.3, D3D12.
- [ ] Unity 6000.3, Vulkan.
- [ ] Unity 6000.4, D3D12.
- [ ] Unity 6000.4, Vulkan.
- [ ] Unity 6000.5, D3D12.
- [ ] Unity 6000.5, Vulkan.

## Core correctness

Run on at least one pre-6.4 editor and one 6.4+ editor, using both D3D12 and Vulkan.

- [ ] A newly added `GsplatRenderer` starts with frustum culling enabled.
- [ ] Existing pre-feature scenes and prefabs, plus renderers with culling explicitly disabled, remain disabled after reload.
- [ ] With culling disabled, rendering and the direct draw path match the feature baseline.
- [ ] With all splats visible, the visible count equals the candidate count and output matches culling disabled.
- [ ] With part of the asset visible, the visible count is between zero and the candidate count, with no edge popping.
- [ ] With the asset outside every active view, the visible count and indirect instance count are zero; returning it to view restores rendering without a stale frame.
- [ ] Large Gaussians remain visible while their projected footprint overlaps any view edge; perspective, orthographic, aspect-ratio, and FOV changes remain conservative.
- [ ] Spark and Uncompressed assets select the correct kernels and retain the same visible membership as their culling-disabled counterparts.
- [ ] Candidate and visible counts around `0`, `1`, `255`, `256`, `257`, `511`, `512`, and `513` produce valid dispatches, sorting, and drawing.
- [ ] Cutouts, `Sort Always`, `Sort Every N Frames`, and mixed culling states preserve their existing candidate ordering and refresh behavior.

## Spatial hierarchy

- [ ] PLY and SPZ imports default to 256 splats per leaf; 128, 256, and 512 settings produce valid hierarchy metadata after reimport.
- [ ] Spark and Uncompressed imports preserve each splat's position, scale, rotation, color, opacity, and every SH coefficient after Morton reordering.
- [ ] Legacy assets without hierarchy metadata render through flat culling without errors or missing buffers.
- [ ] Diagnostics reports `Hierarchy`, nonzero coarse/leaf totals, and work counts matching selected asset metadata.
- [ ] Conservative aggressiveness `0` has no missing regions or view-edge popping compared with culling disabled.
- [ ] Aggressiveness `0.5` and `1` remain stable while moving and clearly expose any accepted footprint-edge loss.
- [ ] Fully-inside leaves emit splats without exact tests; intersecting leaves report exact-tested splats; outside leaves emit none.
- [ ] Partial final leaves at 129, 257, and 513 uploaded splats never read or draw beyond uploaded data.
- [ ] Cutout create, move, invert, target change, disable, and refresh-rate changes update the active mask without stale splats.
- [ ] Async upload renders only uploaded spatial ranges and reaches the same final output as synchronous upload.
- [ ] Two Spark renderers using global sort merge hierarchy-visible counts and orders correctly.

## Render modes and pipelines

- [ ] URP renders correctly in a pre-6.4 editor and a 6.4+ editor, including the Unity 6 Render Graph path.
- [ ] BiRP and HDRP smoke tests render correctly with culling enabled and disabled.
- [ ] URP Single Pass Instanced retains splats visible to either eye, including one-eye-only splats and large edge-overlapping splats.
- [ ] URP and BiRP multi-pass render the correct subset in both eyes.
- [ ] Head translation and rotation produce no one-eye popping, stale visibility, or unstable depth order.
- [ ] Play Mode Scene View, XR mirror output, gizmos, and other debug rendering remain unskewed and use the intended runtime-camera culling result.
- [ ] Multiple runtime cameras and multiple renderers each use the correct view set, transform, count, and order.

## Lifecycle and player

- [ ] Repeatedly toggle culling, the renderer component, and its GameObject in Edit Mode and Play Mode without stale draws, warnings, or resource growth.
- [ ] Asset replacement, asset clearing/reassignment, scene reload, domain reload, and Play Mode transitions rebuild or release resources correctly.
- [ ] Async upload on and off, including disabling or destroying during upload, never draws beyond uploaded data or leaks resources.
- [ ] A Unity 6000.3 standalone player passes on D3D12 and Vulkan.
- [ ] A Unity 6000.5 standalone player passes on D3D12 and Vulkan.
- [ ] A representative large scene runs for 15 minutes without increasing managed allocations, GPU memory, or warning count.

## Performance A/B

Use identical hardware, scene, fixed camera or headset poses, resolution, pipeline, graphics API, asset, SH degree, and quality settings. Test all-visible, partially-visible, and fully-off-screen poses. Warm up each configuration before capture.

At each pose, enter a descriptive sample label and use `Window > Gsplat > Culling Diagnostics` to capture one culling-enabled sample. Confirm the timestamped CSV was written under the project's `ProfilerCaptures` directory, then close the diagnostics window before timed profiling.

For each Unity-version and graphics-API combination below, capture three paired 30-second runs per pose after warm-up. Alternate order between pairs: disabled/enabled, enabled/disabled, disabled/enabled. Keep camera or headset pose fixed. Capture CPU and GPU profiler data separately when required. Compare medians, not individual peaks or FPS snapshots. Record CPU main-thread time, GPU frame time, culling time, sorting time, draw time, VRAM, and GC allocations. Do not use Deep Profile.

- [ ] Capture paired culling-disabled and culling-enabled results on Unity 6000.3 D3D12 for all-visible, partially-visible, and fully-off-screen poses.
- [ ] Capture the same paired poses on Unity 6000.3 Vulkan.
- [ ] Capture the same paired poses on Unity 6000.5 D3D12.
- [ ] Capture the same paired poses on Unity 6000.5 Vulkan.
- [ ] Culling adds no per-frame managed allocation or GPU-to-CPU readback; all-visible overhead and additional VRAM are quantified.
- [ ] Hierarchy diagnostics show reduced exact-tested splats in selective views and a functioning fully-inside fast path in high-visibility views.
- [ ] Partially-visible and off-screen cases show a repeatable median reduction in sorting, drawing, or total GPU work, with no unexplained total-frame regression.

## Release gate

- [ ] Every applicable optimization-branch gate passes, and unsupported or unavailable combinations are recorded.
- [ ] No unresolved crash, C# exception, shader error, graphics validation error, rendering corruption, stale indirect draw, or resource leak remains.
- [ ] Visual differences are limited to removal of splats whose complete projected footprints are outside every active view, and performance claims are backed by captures.
- [ ] Merge-request performance wording reports measured configurations and results without claiming a universal percentage or target frame rate.
