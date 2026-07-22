# GPU Frustum Culling Test Checklist

Use this checklist before merging or releasing GPU frustum culling. Every applicable checkbox is a release gate. Mark a test `N/A` only when the platform or render mode is explicitly unsupported, and record the reason.

## Required Unity version coverage

Run the full checklist on the latest installed patch of one pre-6.4 Unity 6 editor and one Unity 6.4+ editor. Run the compatibility subset on every other supported editor.

### Compatibility subset

The compatibility subset consists of package import, C# and shader compilation, the EditMode test, one Spark render smoke test, one Uncompressed render smoke test, and a standalone player build.

- [ ] Unity 2021.3 ELTS compatibility subset passes. `N/A` -> Reason: Can't download Unity 2021 anymore without a license
- [ ] Unity 2022.3 ELTS compatibility subset passes. `N/A` -> Reason: Can't download Unity 2022 anymore without a license
- [ ] Unity 2023.1 compatibility subset passes.
- [ ] Unity 6000.3 compatibility subset passes.
- [ ] Unity 6000.4 compatibility subset passes.
- [ ] Unity 6000.5 compatibility subset passes.

### 6.4 API boundary

`GsplatUtils.GetObjectId` deliberately uses different Unity APIs at this boundary. Test both sides even if a newer editor is the primary development version.

- [ ] In Unity 6000.3 or earlier, the package compiles through the `GetInstanceID()` path without errors or warnings introduced by this change.
- [ ] In Unity 6000.4 exactly, the package compiles through the `GetEntityId()` and `EntityId.ToULong()` path without obsolete-API errors.
- [ ] In the latest Unity version above 6000.4, the same 6.4+ path compiles without errors.
- [ ] In both pre-6.4 and 6.4+ editors, entering and exiting Play Mode repeatedly does not produce duplicate renderer registration, stale-resource, or object-ID errors.
- [ ] In both pre-6.4 and 6.4+ editors, scene reload and domain reload preserve correct registration and rendering.

## Project and fixture setup

- [ ] Use a supported graphics API: D3D12, Vulkan, or Metal.
- [ ] Confirm the project Console is clear before testing.
- [ ] Create equivalent Spark and Uncompressed imports of the same PLY asset.
- [ ] Include at least one large asset representative of the performance target.
- [ ] Include a small asset that can be positioned precisely at view boundaries.
- [ ] Include an SPZ asset, preferably SPZ v4 with SH degree 4, for the Spark path.
- [ ] Create a scene with one renderer and a scene with at least three renderers.
- [ ] Save baseline screenshots with `Enable Frustum Culling` disabled.
- [ ] For visual comparisons, keep camera pose, projection, resolution, color space, asset transform, SH degree, brightness, and splat downscale identical.

## Package import and automated checks

Run this section in every editor listed in the version matrix.

Unity ignores directories whose names end in `~`. To run the included EditMode test, temporarily expose `Tests~/Editor` as a normal `Tests/Editor` package folder or copy it into a disposable host project's `Assets/Tests/GsplatFrustumCulling` folder. Do not commit the temporary folder, generated `.meta` files, project cache, or logs.

- [ ] Install the package into a clean project using `package.json`.
- [ ] Package import completes without C# compilation errors.
- [ ] `GsplatFrustumCull.compute`, radix-sort compute shaders, and render shaders import without shader errors.
- [ ] No new warnings appear when the feature is disabled.
- [ ] Expose the test assembly as described above, then run `Gsplat.FrustumCulling.Tests` in the Unity Test Runner; all tests pass.
- [ ] Reimport the package and affected shaders; all tests still pass.
- [ ] Close and reopen the project; compilation and the render smoke test still pass.

## Default and toggle behavior

Run with Spark and Uncompressed assets unless a test says otherwise.

- [ ] A newly added `GsplatRenderer` has `Enable Frustum Culling` disabled by default.
- [ ] With culling disabled, output matches the branch baseline before this feature at identical settings.
- [ ] With culling disabled, the existing direct render path remains active and no culling resources are required.
- [ ] Enabling culling during Edit Mode takes effect without reloading the asset or scene.
- [ ] Disabling culling during Edit Mode immediately restores the existing render path.
- [ ] Enabling and disabling culling repeatedly in Play Mode causes no flicker, stale frame, exception, warning, or increasing GPU memory usage.
- [ ] The serialized toggle survives scene save, project restart, prefab creation, and prefab instantiation.
- [ ] Two renderers can use different toggle values without affecting one another.

## Core visibility correctness

Test perspective and orthographic cameras. Compare enabled and disabled output at each pose.

- [ ] An asset centered in the view renders normally.
- [ ] An asset completely outside the left side produces no visible splats.
- [ ] An asset completely outside the right side produces no visible splats.
- [ ] An asset completely above the view produces no visible splats.
- [ ] An asset completely below the view produces no visible splats.
- [ ] An asset completely behind the camera produces no visible splats.
- [ ] Rotating the camera away from the asset removes it from the indirect draw without a stale frame.
- [ ] Rotating back restores the asset without missing or corrupted splats.
- [ ] Translating the camera across every frustum edge does not produce popping for splats whose projected footprint still overlaps the image.
- [ ] A large Gaussian with its center outside the view remains visible while its projected footprint overlaps the view.
- [ ] The same large Gaussian is culled only after its entire projected footprint leaves the view.
- [ ] Small Gaussians near each edge are neither prematurely culled nor retained after fully leaving the view.
- [ ] Very near Gaussians do not produce NaNs, screen-filling corruption, or unstable culling.
- [ ] Distant Gaussians remain governed by normal camera clipping; this screen-space culling pass does not introduce different near/far-plane behavior.
- [ ] Changing field of view from narrow to wide and back updates visibility correctly.
- [ ] Changing aspect ratio between landscape, square, portrait, and a very wide window updates visibility correctly.
- [ ] Changing Game View resolution while running updates visibility correctly.
- [ ] Orthographic size changes update visibility correctly.
- [ ] A rotated renderer culls correctly.
- [ ] A translated renderer culls correctly.
- [ ] A uniformly scaled renderer culls correctly.
- [ ] A non-uniformly scaled renderer culls conservatively without edge popping.
- [ ] A parented renderer continues to cull correctly while the parent moves, rotates, and scales.

## Asset and renderer settings

- [ ] Spark PLY rendering is visually unchanged for visible splats.
- [ ] Uncompressed PLY rendering is visually unchanged for visible splats.
- [ ] Spark SPZ rendering is visually unchanged for visible splats.
- [ ] Every supported SH degree on the selected assets renders correctly.
- [ ] `Gamma To Linear` produces the same enabled-versus-disabled relationship as before.
- [ ] Brightness changes do not affect culling membership.
- [ ] Splat downscale changes rendering size without causing premature footprint culling.
- [ ] Render order still places splats correctly relative to other transparent objects.
- [ ] Replacing an asset with another asset of the same splat count works with culling enabled.
- [ ] Replacing an asset with a different splat count recreates resources and renders correctly.
- [ ] Replacing a Spark asset with an Uncompressed asset, and back, selects the correct culling kernel.
- [ ] Clearing and reassigning the asset reference does not leak buffers or throw exceptions.

## Sorting and indirect drawing

Use the Frame Debugger, GPU capture, or temporary local instrumentation to inspect the visible-count and indirect-argument buffers. Do not commit temporary instrumentation.

- [ ] With all splats visible, the GPU visible count equals the candidate splat count.
- [ ] With part of the asset visible, the GPU visible count is greater than zero and below the candidate count.
- [ ] With the asset fully outside the view, the GPU visible count is zero.
- [ ] A zero visible count produces an indirect draw with zero instances and does not reuse a previous frame's non-zero arguments.
- [ ] Returning from zero visible splats to a visible view rebuilds valid sort and draw arguments.
- [ ] Dynamic radix-sort dispatch sizes follow the visible count and remain within allocated capacity.
- [ ] Zero visible splats do not cause an out-of-bounds access, invalid dispatch, GPU hang, or validation-layer error.
- [ ] The visible subset remains correctly back-to-front sorted while the camera moves through the asset.
- [ ] Transparent overlap within one asset does not show ordering corruption compared with culling disabled.
- [ ] `Sort Always` updates culling every rendered frame.
- [ ] `Sort Every N Frames` preserves its existing refresh behavior and refreshes immediately after sufficient camera movement.
- [ ] `Cutouts Every N Sorts` preserves its existing refresh behavior.
- [ ] Scene View and Game View can render in the same frame without sharing stale camera-specific visibility or indirect arguments.
- [ ] Two runtime cameras with different poses each render the correct visible subset.

## Cutout interaction

- [ ] A box cutout removes the same splats with culling enabled and disabled.
- [ ] An ellipsoid cutout removes the same splats with culling enabled and disabled.
- [ ] `All`, `Parent`, and `Specific` cutout targets behave correctly.
- [ ] Moving, rotating, scaling, enabling, and disabling a cutout updates the candidate set correctly.
- [ ] Toggling culling while cutouts are active rebuilds the correct candidate/order buffer.
- [ ] `Cutouts Update Bounds` enabled and disabled both behave correctly.
- [ ] A cutout that removes every splat produces a zero visible count and zero-instance draw.
- [ ] Removing that cutout restores rendering without stale data.
- [ ] Culling a partially visible, partially cut-out asset neither restores cut-out splats nor removes visible non-cut-out splats.

## Global sorting interaction

Global sorting supports Spark assets only. Use two renderers first, then at least three to exercise cascaded merges.

- [ ] With global sorting disabled, every renderer uses the correct per-renderer culling result.
- [ ] With global sorting enabled and all Spark renderers uncullled, existing global sorting remains correct.
- [ ] With global sorting enabled and culling enabled on every renderer, visible splats interleave correctly across renderers.
- [ ] With a mixture of culling-enabled and culling-disabled Spark renderers, counts and merged order remain correct.
- [ ] With two Spark renderers, either renderer can have zero visible splats without stale or corrupted output.
- [ ] With at least three Spark renderers, the first, a middle, and the last renderer can independently have zero visible splats.
- [ ] With at least three Spark renderers, partial visible counts merge correctly through every cascade stage.
- [ ] Moving renderers into and out of view updates the global indirect draw count correctly.
- [ ] Enabling, disabling, adding, or removing a renderer rebuilds global buffers safely.
- [ ] Swapping a globally rendered asset for another Spark asset rebuilds buffers safely.
- [ ] Introducing an Uncompressed renderer triggers the existing per-renderer fallback and logs only the expected warning.
- [ ] Removing the Uncompressed renderer restores global sorting.
- [ ] Per-renderer brightness, scale factor, gamma setting, transform, SH bands, and renderer ID remain correct after merging.

## Render pipeline coverage

Run the full behavioral suite in the pipelines used by the target project. At minimum, perform the listed smoke gates because all three pipelines are advertised as supported.

- [ ] BiRP renders correctly with culling enabled and disabled.
- [ ] URP renders correctly with the `Gsplat URP Feature` installed.
- [ ] Unity 6 URP renders correctly with Render Graph compatibility mode disabled.
- [ ] HDRP renders correctly with the custom pass injected before transparents.
- [ ] Transparent scene geometry before and after the splat render queue still blends as expected in each pipeline.
- [ ] MSAA enabled and disabled produce no culling-specific artifacts.
- [ ] Gamma and Linear color-space projects show no culling-specific visual difference.

## Graphics API and player coverage

Run every API/platform combination claimed for the release. D3D11, OpenGL, and other APIs without the required subgroup sorting support are outside the supported render path.

- [ ] Windows Editor on D3D12 passes the core suite.
- [ ] Windows standalone player on D3D12 passes the core smoke test.
- [ ] Vulkan Editor or player passes the core suite on at least one supported desktop platform.
- [ ] Android Vulkan player passes the core smoke test when Android is in release scope.
- [ ] Metal Editor and player pass the core smoke test when macOS or iOS is in release scope.
- [ ] Development player logs contain no C# exception, shader error, graphics validation error, or buffer warning.
- [ ] Non-development player build renders correctly.
- [ ] Mono scripting backend build passes where supported.
- [ ] IL2CPP build passes for each release platform that requires it.
- [ ] An unsupported indirect-argument configuration falls back to the existing render path and emits the expected warning once, not once per frame.

## XR coverage

Test the render modes currently advertised by the package. Unsupported combinations are not release gates unless support is being expanded.

- [ ] BiRP multi-pass renders the correct visible subset in both eyes.
- [ ] URP multi-pass renders the correct visible subset in both eyes.
- [ ] URP Single Pass Instanced renders the correct visible subset in both eyes.
- [ ] A Gaussian visible only to the left eye is retained for both-eye rendering.
- [ ] A Gaussian visible only to the right eye is retained for both-eye rendering.
- [ ] A Gaussian outside both eye frusta is culled.
- [ ] Large Gaussians overlapping only one eye's view are not prematurely culled.
- [ ] Head translation and rotation across frustum edges do not cause one-eye popping or stale visibility.
- [ ] Depth order remains stable under head motion and does not alternate between eye-specific sort orders.
- [ ] Different per-eye projection matrices and asymmetric frusta render correctly.
- [ ] XR mirror view contains the expected visible subset.
- [ ] Entering and leaving XR, pausing the headset, and reloading the scene do not leak or invalidate GPU resources.

## Lifecycle, streaming, and stress

- [ ] Enable and disable the component repeatedly in Edit Mode and Play Mode.
- [ ] Enable and disable the GameObject repeatedly.
- [ ] Instantiate and destroy renderers repeatedly without growing GPU memory after cleanup settles.
- [ ] Reload the active scene repeatedly.
- [ ] Enter and exit Play Mode repeatedly with domain reload enabled.
- [ ] Enter and exit Play Mode repeatedly with domain reload disabled, if supported by the project.
- [ ] `Async Upload` disabled loads and culls correctly.
- [ ] `Async Upload` enabled with `Render Before Upload Complete` enabled never reads beyond the uploaded count.
- [ ] `Async Upload` enabled with `Render Before Upload Complete` disabled begins rendering correctly after upload completes.
- [ ] Disable or destroy a renderer during async upload without an exception, stale draw, or resource leak.
- [ ] Test one renderer whose candidate count is not a multiple of 256.
- [ ] Test visible counts around radix-sort dispatch boundaries, including 0, 1, 255, 256, 3839, 3840, and 3841 when practical.
- [ ] Test the largest intended production asset without buffer-capacity errors or integer overflow symptoms.
- [ ] Run a representative scene continuously for at least 15 minutes without increasing managed allocations, GPU memory, or warning count.

## Performance validation

Use identical hardware, scene, camera path, resolution, pipeline, graphics API, asset compression, splat count, SH degree, and quality settings before and after. Capture CPU and GPU data separately.

- [ ] Record baseline results with culling disabled.
- [ ] Record results with culling enabled while all splats are visible; the overhead is measured and documented.
- [ ] Record results with approximately half the splats visible; visible count, cull time, sort time, draw time, total GPU frame time, and FPS are documented.
- [ ] Record results with the asset fully outside the view; visible count is zero and the avoided sort/draw work is documented.
- [ ] Record results for a large multi-renderer scene with global sorting disabled.
- [ ] Record results for a large all-Spark scene with global sorting enabled.
- [ ] Record Editor and standalone-player results separately.
- [ ] Record CPU main-thread time, GPU frame time, culling time, depth/sort time, global merge time, draw time, VRAM, and GC allocations.
- [ ] Culling introduces no per-frame managed allocation and no GPU-to-CPU readback in the normal render path.
- [ ] Off-screen and partially visible scenarios show a measurable reduction in sorting or drawing cost sufficient to justify enabling the feature.
- [ ] Any all-visible regression and additional VRAM consumption are quantified in the merge request.

## Final release gate

- [ ] Every applicable checkbox above passes on both the selected pre-6.4 and 6.4+ full-suite editors.
- [ ] Every other supported Unity version passes the compatibility subset.
- [ ] No unresolved C# exception, shader error, GPU validation error, rendering corruption, one-eye XR defect, stale indirect draw, or resource leak remains.
- [ ] Visual differences are limited to removal of splats whose complete projected footprints are outside all active views.
- [ ] Performance claims in the merge request are backed by captured measurements.
- [ ] Unsupported combinations and untested release platforms are stated explicitly.
