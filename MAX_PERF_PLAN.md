# Maximum-Performance 10M-Splat VR Experiments

## Summary

Create two branches from commit `87065d3` on `perf/frustum-culling-optimizations`:

- `perf/hybrid-renderer`
- `perf/tile-renderer`

Shared commits get cherry-picked into both. Hybrid implemented first. Both run full roadmap, even after reaching target.

Acceptance:

- RTX 3080 + Quest 2, native eye resolution.
- Windows URP, D3D12 and Vulkan.
- Representative-route p95 CPU and GPU ≤13.9 ms: native 72 FPS.
- Runtime VRAM ≤8 GB.
- SSIM ≥0.98, LPIPS ≤0.03.
- No visible stereo mismatch, popping, shimmer, or temporal ghosting.
- Expensive import/reimport and larger generated assets allowed.
- Current renderer remains fallback for BiRP, HDRP, moving renderers, moving cutouts, and unsupported APIs.

## Shared Foundation

- Keep base branch unchanged. Separate self-contained commits for benchmark instrumentation and imported optimization data; cherry-pick identical commits into tile branch.
- Establish release-player baseline with GPU Profiler disabled. Current Editor captures remain diagnostic only.
- Extend 256-splat Morton hierarchy into bottom-up 8-way levels.
- Generate deterministic opacity-weighted LOD representatives offline:
  - Fixed maximum 32 representatives per internal node.
  - Combine means and covariance using Gaussian-mixture moments.
  - Derive normalized quaternion and positive scales from covariance eigendecomposition.
  - Combine opacity conservatively and weight DC/SH coefficients by contribution.
  - Retain full-resolution leaves for near views.
- Store node bounds, child/representative ranges, covariance footprint bounds, opacity bounds, geometric error, and contribution bounds.
- Add automatic cache invalidation and importer-version bump. Existing models reimport automatically.
- Add full near/far, opacity-contribution, projected-radius, and viewport culling.
- Add shared-memory workgroup compaction with one global atomic per group; no subgroup-size assumption.
- Add global adaptive controller:
  - Target 12.5 ms GPU budget, leaving 1.4 ms safety margin.
  - Select LOD by projected error, contribution, distance, and frame budget.
  - Use hysteresis and bounded rate changes.
  - Never cross quality-validated minimum settings.
  - Reuse selection under small pose changes; invalidate on camera, asset, transform, cutout, viewport, resolution, or XR-mode changes.
- Add optional fixed XR foveation and GS-only adaptive internal resolution. Other scene rendering stays native.
- Preload benchmark asset fully. Chunk streaming deferred because it improves capacity/loading, not current 10M-scene frame time.

## Hybrid Branch

Implement checkpoints as separate commits:

1. **LOD traversal and budget selection**
   - GPU traversal selects internal representatives or descends toward full leaves.
   - Stereo uses union-selected LOD to prevent eye mismatch.
   - Static fast path only; dynamic cases use current renderer.

2. **Projection prepass**
   - Decode position, covariance, opacity, and SH once per selected splat per eye.
   - Store compact screen-space center, conic, opacity/color, depth, and valid-eye mask.
   - Replace four repeated vertex-shader decodes/covariance/SH evaluations with lightweight quad vertices.

3. **Sorting optimization**
   - Replace four-pass 32-bit depth sort with two-pass 16-bit quantized depth sort.
   - Preserve stable Morton order inside equal-depth bins.
   - Reuse order during small motion; force refresh when projected ordering error exceeds threshold.
   - Fall back to exact current sort when quality validation fails.

4. **Overdraw reduction**
   - Compute opacity-threshold ellipse bounds matching fragment cutoff.
   - Shrink low-contribution quads without changing retained alpha support.
   - Apply contribution pruning, adaptive LOD, fixed foveation, and adaptive GS resolution.
   - Depth-aware upscale GS target before transparent composition.

5. **GPU cleanup**
   - Fuse counter/indirect-argument setup where safe.
   - Reuse all buffers and command resources.
   - Remove normal-path allocations, readbacks, and redundant bindings.
   - Use reduced precision only for validated projected intermediates—not source covariance, depth, or blending.

Expected ceiling: substantial improvement with moderate code growth, but 72 FPS remains uncertain because hardware quads retain fragment overdraw and global approximate sorting.

## Tile-Renderer Branch

Use shared foundation, then replace URP GS draw path with compute tile rasterization. Architecture follows established tile-based GS pipelines and hierarchical sorting research, implemented independently to preserve licensing: [gsplat rasterization](https://docs.gsplat.studio/main/apis/rasterization.html), [StopThePop](https://doi.org/10.1145/3658187).

1. Project selected LOD splats once per eye into compact screen-space data.
2. Bin opacity-aware bounds into fixed 16×16 tiles.
3. Count intersections, prefix-sum offsets, then emit `(eye, tile, depth, splat)` entries.
4. Start with 32-bit `(tile, depth)` radix sorting as correctness baseline.
5. Add segmented tile-local sorting/depth bucketing; retain baseline when quality fails.
6. Dispatch one 16×16 compute group per eye tile:
   - Read opaque depth and reject occluded samples.
   - Blend front-to-back using transmittance.
   - Stop when remaining transmittance falls below `1/255`.
   - Evaluate only pixels inside Gaussian support.
7. Render into per-eye GS textures, then depth-aware composite at current URP transparent injection point.
8. Handle large-footprint splats through dedicated bounded lists.
9. Guard every tile/intersection buffer with capacity checks. Overflow raises adaptive LOD/resolution pressure; never writes out of bounds.
10. Apply same adaptive budget, temporal selection reuse, foveation, and quality floor as hybrid branch.

Expected ceiling: strongest chance of 72 FPS through tile-local work, early alpha termination, and removal of hardware overdraw; highest shader, buffer, synchronization, and pipeline complexity.

## Validation and Comparison

At baseline and every checkpoint:

- Use identical source asset, scene, XR settings, route, graphics API, and camera poses.
- Run five warmed standalone-player samples in alternating order.
- Record CPU/GPU median, p90, p95, VRAM, selected originals/LOD representatives, tile intersections, internal resolution, and overflow.
- Benchmark D3D12 and Vulkan on Unity 6.5. Compile/import/smoke-test installed pre-6.4 and 6.4+ versions; document unavailable 2021.3/2022.3 validation.
- Capture both eyes at fixed front, up, edge, thin-geometry, high-opacity, and rapid-turn poses.
- Compare against base renderer using SSIM/LPIPS and headset A/B.
- Test reload, asset swap, enable/disable, scene transition, disposal, fallback paths, opaque-depth interaction, XR stereo, and normal cameras.
- Report branch complexity: changed source lines, shader kernels, GPU buffers, passes, serialized fields, imported size, import time, and runtime VRAM.
- Preserve licenses; use papers/docs as design references only. Do not copy GraphDeco research-only CUDA code.
- Keep `AGENTS.md`, `LOGS.md`, `NOTES.md`, profiler analyzer, and project benchmark helpers outside commits as already requested.
