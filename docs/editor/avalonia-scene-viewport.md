# Avalonia scene viewport

Scene, prefab and Asset Preview rendering use a windowless Stride game and an
Avalonia `Image` (`AvaloniaGameView`). No SDL window is created by these views. Open a project, then double-click a scene or prefab asset.
The hierarchy, viewport and property inspector share the current selection.

## Controls

- Right mouse button + WASD: fly; Q/E: down/up; Shift: faster movement.
- Middle mouse button: pan; mouse wheel: move forward/backward.
- F or Frame: frame selected entities, or the whole scene with no selection.
- W/E/R or Move/Rotate/Scale: change gizmo while not flying.
- Click: select; Ctrl + click: toggle selection.
- WorldSpace/ObjectSpace/ViewSpace: transformation coordinates.
- Snap: 1 unit, 15 degrees or 0.1 scale; Shift enables snapping temporarily.

Gizmo edits update the asset's Quantum graph in one undo transaction. Changes in
assets and dependencies rebuild and reload the viewport's runtime content. The
current MVP reloads the owning scene/prefab rather than propagating individual
node changes; camera position and selection IDs survive these reloads.

## Architecture

`Stride.Assets.Editor` owns `EditorGameController<TEditorGame>`,
`GameEditorViewModel`, the hierarchy-game base classes and the four viewport
services: graphics compositor, camera, entity selection and transform.
The existing hierarchy editor owns a `SceneViewportViewModel`, preserving its
current hierarchy/property behavior without duplicating the composite editor
base classes. `SceneViewportController` serves the common scene/prefab path.

`Stride.Assets.Editor.Avalonia` registers `IEditorGameViewFactory`. Its adapter
creates an `AvaloniaGameView` on the UI thread and attaches a game system before
starting `GameContextHeadless`. The existing `HeadlessGraphicsPresenter` owns an
offscreen color/depth buffer; rendering stays in Stride on its game thread.
Asset Preview uses the same view and windowless context.

The view system drains queued Avalonia mouse/keyboard events before InputSystem
updates. `InputSourceSimulated` exposes those events to the existing camera,
picking and gizmo services. Mouse positions are normalized within the control;
pointer capture and focus loss release held buttons/keys. Render target size
follows Avalonia bounds multiplied by the top-level render scaling.

On Linux, compatible devices share a ring of three Vulkan images with Avalonia,
using exported memory and paired semaphores. The desktop prefers Avalonia's Vulkan
backend so scene rendering and composition avoid Vulkan-to-GLX texture interop.
The GPU path has no CPU readback or fixed frame-rate cap. Other backends retain
the staging-texture / `WriteableBitmap` fallback, capped at 30 FPS.

Picking and transformation geometry reuse the WPF renderer and gizmo algorithms.
The existing WPF implementation remains available to its callers. System shaders
are imported from loaded packages, since asset view models are not populated
when the builder is constructed. The builder database is registered as a session
service for the content loader and graphics compositor.

Closing a session awaits editor-game shutdown and disposes the background Asset
Preview before the desktop destroys its graphics contexts. Closing a project,
opening a different project and closing the main window use this path.

## Verification

```sh
dotnet build sources/editor/Stride.GameStudio.Avalonia.Desktop/Stride.GameStudio.Avalonia.Desktop.csproj --no-restore -m:1 -p:UseSharedCompilation=false
dotnet run --project tests/editor/Stride.Editor.GameController.Smoke/Stride.Editor.GameController.Smoke.csproj
# Use a temporary copy of a real project, not a project with unsaved work:
dotnet run --project tests/editor/Stride.Editor.Viewport.Smoke/Stride.Editor.Viewport.Smoke.csproj -- /path/to/copied-project.sln
```

The integration check opens a scene on the real Avalonia UI thread, verifies
hydrated model meshes and detailed, non-empty bitmap frames, switches all three
transform modes, checks hierarchy selection, and commits/reloads/undoes two
unique asset transforms. It sends Avalonia RMB + W and wheel events to the
viewport, checks camera movement and release, resizes the image, renders a
Model Asset Preview through the same path, checks for loaded SDL libraries on
Linux, and closes the session. `--keep-open` retains the test window for visual
inspection. `--pipeline-gc-stress` adds the optional Vulkan lifetime check below.

The 2026-10-17 bitmap integration run passed on Linux: scene and Model Preview
both produced detailed frames; Avalonia RMB + W, pointer rotation and wheel zoom
worked; resize preserved color/depth buffer objects; two uncached scene edits and
Undo reloads retained the model; no native SDL library was mapped; session
shutdown completed with process exit code 0.

The first windowless attempts crashed inside NVIDIA's `vkCmdPipelineBarrier`
while post-processing prepared its textures. `HeadlessGraphicsPresenter` was
replacing and disposing color/depth texture objects on resize. It now recreates
the textures and child views in place, following the swap-chain presenter. The
resize regression asserts stable color/depth object identities and continued
rendered detail. This does not establish a cause for the earlier user-reported
Vulkan pipeline-creation failures.

## Earlier SDL implementation and regression fixes

The following observations describe the previous native-window implementation;
its Scene/Preview hosting path has now been replaced by the windowless view.

The local AnimatedModel integration run passed on XWayland: one hydrated model,
a 713×808 viewport backbuffer, all three active gizmo modes, selection and undo,
advancing render frames, and process exit code 0. The preceding shutdown run
reproduced the NVIDIA `PreviewGame Thread` crash; disposing Asset Preview before
desktop shutdown eliminated that reproduction in the final run.

The subsequent 2026-10-17 17:57 crash was in X11 event-cookie handling through
system sdl2-compat/SDL3. Inspection found concurrent `SDL_PollEvent` calls from
Scene and Preview game threads. After moving video initialization, window
creation and native polling to the UI thread, queuing callbacks per window, and
selecting the shipped SDL2, the integration check passed again: 200 events on
the correct Scene/Preview threads, RMB + W camera movement and release, one
loaded SDL2 library, a 713×808 backbuffer, and process exit code 0. A separate
visual run retained rendered content and gizmos across two screenshots, and
also closed with exit code 0. This addresses confirmed integration defects;
it does not establish the exact cause of the original native fault or rule out
other driver/lifetime failures in longer sessions.

A later live hang (PID 144529) was diagnosed through a .NET diagnostic dump.
The UI thread was blocked in `Logger.Log` while property bindings changed;
Asset Preview held the logger's global monitor and synchronously waited in
`LoggerViewModel.MessageLogged` for that UI thread. Log updates now queue via
`InvokeTask` after releasing the pending-message lock. The smoke check holds
the UI busy while a worker logs 100 messages, requires the worker to finish
without UI dispatch, then verifies that all messages and a UI warning reach
the Output view model. The original hung process is left running to avoid
silently discarding unsaved work; rebuilding cannot update its loaded code.
The updated regression and viewport run passed with all 101 log messages,
200 correctly routed SDL events, camera capture/release, advancing rendering,
session shutdown, and process exit code 0.

Selecting a Scene asset in Asset Preview uses `ScenePreview`, which intentionally
supplies neither content nor a graphics compositor. The WPF view displays guidance
to open the scene editor. Its Avalonia view now does the same instead of inheriting
the generic SDL host template and exposing an unrendered swapchain (observed as
flickering/black content around tiling changes). A diagnostic snapshot of the
running editor confirmed `ScenePreview` was active and `PreviewGame.Faulted` was
false. The smoke check verifies the Scene guidance template and that Model Preview
keeps the native-host template.

Selecting CharacterModel exposed a separate property-grid bug: the two-way URL
text bindings in both content-reference templates wrote initialization text back
to `NodeValue`, clearing the Model reference and triggering a scene rebuild.
The running editor had enabled ModelComponents and unit scale, but null Model
references; the on-disk scene retained its original model URL. The integration
check reproduced the cleared asset reference before the fix. Both URL fields
now use explicit source updates through the existing text validation behavior.
After the fix, selection preserved the model and animation references and the
rendered mesh count; camera/gizmo/SDL checks and shutdown passed with exit code 0.
The running old process is not modified, so rebuilding does not restore references
already cleared in its unsaved asset state.

Moving CharacterModel then exposed mutation inside `SceneAssetCompiler.SceneCommand`:
it attached the source asset's root entities to its temporary runtime Scene.
A subsequent uncached build failed with "This entity already has a scene".
The viewport then unloaded the previous content, loaded a null scene, and raised
`NullReferenceException` in `Scene.SceneCollection.InsertItem`. A live diagnostic
snapshot confirmed the new position, unit scale, and retained Model reference;
the editor process remained alive. Scene compilation now uses an `AssetCloner`
copy. The viewport also checks compiled-content availability before clearing the
previous scene. The regression first reproduced attached source entities, then
passed after the fix: two unique position edits forcing actual compilation,
reload after each edit and Undo, unchanged asset scene ownership, preserved mesh
count and position, camera/SDL checks, and process exit code 0. The older test
undid the edit immediately, allowing cached compilation to hide this defect.

A later `vkCreateGraphicsPipelines` ErrorUnknown (bytecode
`4ec7c896b96eb25f81c351763f2d2067`) occurred in the LuminanceLogShader pass
of HDR post-processing. A live snapshot provided the exact stack and shader;
the extracted SPIR-V 1.4 module passed `spirv-val --target-env vulkan1.2`.
The Vulkan backend pinned an unused "main" array while native stage `pName`
pointers referenced separate entry-point arrays whose `fixed` scopes had ended.
It now pins the actual entry-point arrays through graphics/compute pipeline
creation and frees the handles in `finally`. The optional smoke flag
`--pipeline-gc-stress` creates 48 fresh graphics pipelines while another thread
forces compacting GC, preferring the LuminanceLogShader when available. Earlier
stress runs also passed before this fix, so the pointer lifetime defect is
confirmed by code inspection, but its responsibility for the reported
ErrorUnknown remains unproven. The updated run explicitly selected the same
`4ec7c896b96eb25f81c351763f2d2067` Luminance shader, created all 48 pipelines
under compacting GC, passed model selection, two move/reload/Undo cycles and
camera/SDL checks, and exited with code 0.

## Rapid Asset Preview switching

The subsequent user crashes (PIDs 418037 and 419747) happened on PreviewGame
Thread in NVIDIA at offset `0x302e2a`. The `--preview-switch-stress` smoke option
reproduced the same native signature (PID 432789). A temporary full .NET dump
resolved the managed caller to `TexturePreview.RenderSprite`, SpriteBatch and
`CommandList.BindDescriptorSets` calling `vkUpdateDescriptorSets`. Temporary
extracted dumps were removed after analysis.

A temporary descriptor check caught a disposed sampler at binding 1/1 while
rendering `Textures/normalmapLower`. UI preview attachment invokes
`TextureBasePreviewViewModel.AttachPreviewTexture`, which calls `DisplayMipMap`;
it was disposing/replacing the sampler on the UI thread while rendering used it.
`TexturePreview.DisplayMipMap` now queues sampler replacement on PreviewGame's
script scheduler, skips disposed previews, and acquires the new sampler before
releasing the old one. The temporary Vulkan checks were removed.

Preview selection also records pending requests explicitly, including null.
Change tasks await the existing preview semaphore and skip already-consumed
requests, so a selection made during initialization is processed after it ends.

The extended smoke run performs 144 switches across the copied project's assets,
requires no Preview error log messages, waits for the latest selected preview to
finish initialization, checks continued bitmap rendering, and closes the session.
It passed without the temporary Vulkan guards and exited with code 0. Invoke it
with `--preview-switch-stress` after the copied solution path.

## Remaining limits

The fallback bitmap path requires GPU-to-CPU readback and a second upload by
Avalonia, and is capped at 30 FPS. Windows and macOS GPU sharing still require
implementation and runtime verification. Pointer capture
does not provide an unbounded relative cursor: dragging is limited by screen
edges. Physical picking and dragging all gizmo axes still need a manual pass. This is the common scene/prefab viewport, not the full
WPF multi-scene editing workflow. Grid, component icons, physics debug drawing,
light probes, cubemaps, custom camera shortcuts, drag/drop and Ctrl-drag
entity duplication remain separate work. These are not registered as viewport
services in this MVP.

## GPU presentation stability

The running AnimatedModel editor flickered between a complete scene and an empty
viewport while its presentation counter continued to increase. Ten backbuffer
readbacks contained detailed, opaque scene pixels, and the CPU presentation path
was stable. A controlled comparison captured 50 on-screen frames per backend:
GLX lost scene content in 7 frames; Vulkan retained it in all 50, with GPU
presentation enabled and zero CPU readbacks. The desktop now prefers Vulkan on
X11, retaining GLX and software as startup fallbacks. The underlying Vulkan/GLX
interop defect has not been isolated to a specific driver or Avalonia operation.

Run the optional screen-capture regression with the test window visible and
unobstructed on Linux/Hyprland (`grim` and `hyprctl` must be installed):

```sh
dotnet run --project tests/editor/Stride.Editor.Viewport.Smoke/Stride.Editor.Viewport.Smoke.csproj -- /path/to/copied-project.sln --require-gpu --presentation-stability
```

It samples 50 screen captures inside the viewport and rejects frames without scene
detail. `--glx-presentation` selects the previous backend for reproducing the bug.
The calibrated regression fails on GLX at frame 0 (12 sampled colors despite 171
reported presentations). Window coordinates come from Hyprland because XWayland's
`PointToScreen` coordinates can refer to a different compositor screen region.
Checking frame counters alone cannot detect this failure. The updated full smoke
run passed scene selection, two uncached move/reload/Undo cycles, camera input,
resize, GPU Model Preview, all 50 screen captures and shutdown with exit code 0.
Both scene and preview reported zero CPU readbacks.

## Captured transformation axes

The common transformation gizmo retains its axis while a transformation is
initialized, including the movement threshold and the button-release frame.
Hover picking resumes after release. Previously it recomputed the axis every
frame, so leaving the handle could end the gesture or switch axes. The incomplete
Ctrl condition left during the port also prevented release cleanup with Ctrl held;
release cleanup now runs independently of modifiers.

The viewport smoke regression initializes an already captured X axis with the
pointer outside the view for Move, Rotate and Scale. It checks axis retention,
release with Ctrl held and resumed hover picking. Before the fix it failed with
"Translation lost its captured axis when the pointer left the gizmo." This is a
state-transition regression; physical handle picking and dragging still need a
manual pass.

## Inspector width during hierarchy selection

At a 1920×1080 editor layout, changing selection rebuilt the property list and
repeatedly hid/showed its automatic vertical scrollbar. The outer inspector
remained 383 pixels wide, but its content viewport alternated between 381 and
365 pixels, including transitions between two selections that both need scrolling.
The inspector now always reserves the vertical scrollbar space. Its scrollbar
can be inactive for short lists without shifting the property fields.

`--inspector-layout` adds a regression that arranges the editor content at
1920×1080 and measures every layout update during 16 hierarchy selections across
lights, cameras, model and background entities. It failed before the change with
widths `381, 365`; after the change all measured content widths were 365 pixels.
The check restores the original content dimensions before the remaining viewport
tests. It exercises the layout size without requiring a physical 1080p monitor.

### Follow-up: rebuilding the inspector

Reserving scrollbar space fixed the 16-pixel width change, but did not prevent
another visible jump: property generation hid and destroyed the entire table
before asynchronously building its replacement. The original width check skipped
hidden layouts and consequently missed this. An expanded regression reproduced
17 layout passes with no property table despite a non-empty hierarchy selection.

Hierarchy selection changes now coalesce within one UI turn, so replacing the
selection does not briefly generate an empty inspector. Property generation
retains the previous graph until its replacement is ready; the Avalonia inspector
keeps the table and filter visible while disabling edits to the old graph.
Replacing or clearing a graph destroys the previous graph once. Superseded
asynchronous results are destroyed and cannot replace the latest selection.
A deliberate empty selection still clears the table and shows the fallback.

The `--inspector-layout` regression now covers 922×1030, 1280×720 and 1920×1080.
It checks all visible widths and the outer panel bounds, rejects hidden table
layouts during selection replacement, switches rapidly between objects, and
checks that the final selection and deliberate deselection are respected.

### Reusing inspector rows and editors

The property behavior now reconciles rows by name, declared type and runtime value
type, rebinding compatible rows in place. Both the main inspector and settings
use this path. Header templates use a recycling DataTemplate and ContentControl;
the property template selector reuses controls only when their provider matches.
Children are reconciled before the next layout rather than queued individually
on the dispatcher. Shared rows retain their expansion state. Vector editors
suppress source writes during DataContext replacement so switching objects cannot
write partially rebound vector components into the newly selected object.

`--inspector-performance` alternates between two cameras twelve times and reports
preparation, synchronous graph application, remaining layout/dispatch time and
retained controls. On the same copied AnimatedModel project, the baseline median
was 285 ms; optimized runs measured 17–21 ms, with graph application around 4 ms
and all 916 controls retained after warm-up. These are local measurements, not a
one-frame guarantee: initial templates and different component types can cost
more. The regression also checks current graph bindings and values, unchanged
entity transforms, no mixed visible graphs at layout boundaries, preserved
collapsed rows, and editing/Undo through a reused position field. It runs before
the existing scene reload, GPU preview and shutdown checks.
