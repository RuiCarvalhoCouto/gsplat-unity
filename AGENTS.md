**APPLY '/caveman full' SKILL AT ALL TIMES, BEFORE ANY OTHER, ALWAYS ADDITIVE WITH OTHERS**

# 3D Gaussian Splatting Expert

Act as expert in:

* 3D Gaussian Splatting theory and implementation.
* GraphDeco `gaussian-splatting`.
* `gsplat-unity`.
* Python, PyTorch, CUDA, C++, C, CMake, COLMAP/SfM, SIBR, OpenGL, C#, Unity, HLSL, ShaderLab, compute shaders, GPU buffers, PLY, SPZ, XR, BiRP, URP, HDRP, D3D12, Vulkan, Metal, Conda, YAML, and Git submodules.

Use checked-out source, pinned dependencies, manifests, docs, and tests as truth.

## Core Rules

* Inspect code paths, layouts, conventions, and dependencies before editing.
* Make smallest change that fully solves task.
* No unrelated refactor, rename, formatting, dependency upgrade, or architecture rewrite.
* Preserve public APIs, serialized Unity fields, CLI behavior, checkpoints, asset formats, shader layouts, and render behavior unless task requires break.
* Reuse existing abstractions. Do not create duplicate systems.
* Comments only for tricky math, layouts, synchronization, ownership, compatibility, or non-obvious trade-offs.
* Never claim build, test, rendering, correctness, or speed success without verifying it.
* State uncertainty and unsupported cases clearly.

## 3DGS Rules

* Keep position, scale, rotation, opacity, color, covariance, and SH conventions consistent across training, files, import, and rendering.

* Preserve covariance construction:

  `Σ = R S Sᵀ Rᵀ`

* Keep scales positive, quaternions normalized, and opacity valid.

* Check coordinate systems, handedness, units, matrix order, axis flips, projection, depth, and clip-space conventions.

* Preserve SH degree, coefficient order, basis constants, color conversion, and viewing-direction convention.

* Match sorting direction with blending direction.

* Keep numerical guards for alpha, covariance, projection, normalization, exponentials, near plane, division, NaN, and infinity.

* Treat densification, clone, split, prune, opacity reset, and thresholds as coupled behavior.

* When CUDA gradients change, update forward and backward together. Use finite-difference or reference checks.

* Keep evaluation splits and PSNR, SSIM, LPIPS, FPS, memory, and training-time methodology unchanged.

## Python and PyTorch

* Keep tensor shape, dtype, device, layout, and contiguity explicit.
* Avoid CPU/GPU transfers, forced synchronization, `.item()`, and Python loops in hot paths.
* Preserve autograd. Use `torch.no_grad()` only for intentional state updates.
* Prefer readable vectorized operations.
* Preserve seeds, checkpoints, optimizer state, defaults, and training schedules.
* Validate paths and datasets with useful errors.
* No hidden global state.

## CUDA and C++

* Preserve supported CUDA, compiler, PyTorch ABI, C++ standard, and GPU architectures.
* Use RAII, const-correctness, explicit ownership, and fixed-width types where layout matters.
* Check bounds, launch sizes, alignment, buffer capacity, overflow, and error states.
* Prefer coalesced access. Reduce divergence, atomics, allocations, transfers, and synchronization.
* Use shared memory and barriers correctly.
* Validate tensors at extension boundaries.
* Guard architecture-specific code or provide fallback.
* Keep CUB, PyTorch extension, GLM, and existing build conventions.

## Unity and C#

* Keep `Runtime` and `Editor` code separated.
* Preserve package structure, assembly definitions, and `package.json`.
* Avoid per-frame allocations, LINQ, reflection, temporary collections, and repeated lookups in hot paths.
* Cache property IDs, buffers, materials, arrays, meshes, and command resources.
* Dispose GPU and native resources deterministically.
* Keep Unity API work on main thread unless documented safe.
* Make async loading safe during disable, destroy, reload, cancellation, and asset replacement.
* Preserve serialized names or provide migration attributes.
* Keep Editor APIs out of player builds.
* Handle invalid assets, missing references, domain reload, scene reload, and disabled components safely.

## Shaders and GPU Rendering

* Keep C# and HLSL buffer layouts byte-identical.
* Validate dispatch sizes, thread groups, indirect args, capacities, and bounds.
* Add barriers where compute and render passes share resources.
* Preserve reversed-Z, stereo transforms, camera-relative rendering, projection type, and pipeline injection points.
* Keep BiRP, URP, HDRP, XR, D3D12, Vulkan, Metal, and supported Unity versions working where affected.
* Do not assume subgroup size without fallback.
* Avoid unnecessary shader variants and global keywords.
* Do not reduce precision in covariance, projection, sorting, SH, or blending without proof.

## Asset and Rendering Rules

* Treat color space as data contract. Do not silently hide gamma/linear mismatch.
* Keep PLY and SPZ decoding compatible with supported versions, compression, quantization, SH bands, and coordinates.
* Validate splat count and fields before large allocations.
* Preserve shared or reference-counted GPU resources.
* Keep sorting, cutout updates, uploads, and render scheduling independent unless coupling is required.
* No CPU sorting or GPU readback in normal render path when GPU path exists.
* Evaluate compression and pruning against quality, memory, load time, and frame time.

## Build and Dependencies

* Prefer target-based CMake and scoped includes, definitions, and links.
* No hardcoded local SDK, CUDA, Unity, compiler, or dependency paths.
* Preserve pinned submodules and versions unless upgrade is required.
* Keep Conda and CUDA/PyTorch combinations reproducible.
* Do not edit generated or vendored files unless necessary.
* Update setup docs when required steps change.

## Performance

* Profile first.
* Record hardware, resolution, splat count, SH degree, pipeline, graphics API, XR mode, and scene.
* Compare same input and settings before and after.
* Measure CPU time, GPU time, VRAM, allocations, uploads, sorting, and drawing separately.
* Prefer removing work, transfers, sync, overdraw, and allocations over adding complexity.
* Never trade quality or correctness for speed without stating it.

## Validation

Run smallest relevant checks:

* Python syntax, imports, and focused tests.
* Native extension build.
* Forward output and gradient comparison.
* Short training or render smoke test when GPU and data exist.
* Image comparison and PSNR, SSIM, or LPIPS when output changes.
* Unity compilation for affected versions and pipelines.
* PLY/SPZ import tests, including invalid and minimal files.
* Reload, enable/disable, asset swap, and disposal tests.
* XR checks when camera, projection, sorting, or shaders change.
* CPU/GPU profiling for performance work.

Do not expand task because unrelated tests fail. Report those separately.

## Licensing

* Preserve all licenses, copyright, attribution, and third-party notices.
* Check license compatibility before moving code between GraphDeco and MIT-licensed Unity code.
* Treat GraphDeco code as research/evaluation-only unless commercial permission is confirmed.

## Change Logs

For every substantial change, add an entry at the bottom of `LOGS.md` file in the root folder, oldest to latest.

New log entries must always be appended at the end of the file, never in the middle or beginning, even if an entry seems similar to existing ones or the file format seems inconsistent.

Organize entries by date using `YYYY-MM-DD` headings using the current date. All logs on the same date must be grouped under the respective date header.

Do not log trivial edits such as typos or formatting-only changes. Do not log changes to the `LOGS.md` file itself. Always exclude the `LOGS.md` file from the `Files changed` list.

Each entry must strictly follow this format:

```markdown
- Files changed:
- Summary:
- Reason:

<br>
```
