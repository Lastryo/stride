# Stride Game Studio for Linux — Avalonia

`linux-editor-first-steps` is an experimental branch of the Stride cross-platform editor. It builds on the Avalonia editor from `xplat-editor`, adding an embedded scene and prefab viewport, hierarchy and property editing, asset management, UI and Graphics Compositor editing, and project build and launch commands.

The main goal is to run Game Studio on Linux. Full parity with the Windows/WPF editor and production readiness are not yet claimed.

## Changes in this branch

The branch starts from `origin/xplat-editor` at commit `18115dbbe`. Avalonia, the separation of WPF and shared libraries, basic ViewModels, Quantum, Undo/Redo, and the cross-platform Launcher were already part of that baseline.

At commit `16957cab6`, this branch adds **376 changed files, 35,956 insertions, and 1,243 deletions** across three commits:

| Commit | Changes |
| --- | --- |
| `3959bbed5` | Fixes for the hierarchy, assets, fonts, icons, and inspector; initial viewport and editor-game infrastructure integration. |
| `7867dbbf2` | Moves the viewport and Asset Preview to rendering inside Avalonia without a separate SDL window, along with related fixes. |
| `16957cab6` | Expands the interface and commands for assets, scenes, prefabs, UI Page/Library, Graphics Compositor, and project builds; adds GPU viewport presentation and integration checks. |

This overview covers committed changes. Uncommitted local edits are excluded from the statistics. Comparing only against `master` mixes the Linux improvements with the inherited `xplat-editor` history.

### Scenes, prefabs, and viewport

- Scenes and prefabs open with embedded Stride rendering. The hierarchy, viewport, and inspector share the current object selection.
- Camera controls, object selection, Move/Rotate/Scale, World/Object/View coordinates, Pivot/Center, snapping, and component gizmos are available.
- Hierarchy operations include creation, deletion, duplication, reparenting, folders, adding objects from assets, and Undo/Redo.
- Prefabs can be created from a template or selection. Base links, overrides, saving, and handling deletion of the source prefab are implemented.
- On compatible Linux/Vulkan devices, images are shared with Avalonia through GPU memory and semaphores. Other presentation paths retain a bitmap fallback.

### Assets and inspector

- Template catalog and file import, search, sorting, asset card scaling, and thumbnails.
- Asset creation and deletion, deletion confirmation, Undo/Redo, and moving files to the system trash when saving.
- Build root asset management and dependency indicators, source updates, and effect import.
- Fixes for Avalonia 12 property templates, reference fields, numeric/vector/color editors, and inspector updates when a property type changes.

### Other editors and commands

- **UI Page / UI Library:** element hierarchy, palette, reparenting and reordering, copy/paste, duplication, renaming, preview, library links, and Undo/Redo.
- **Graphics Compositor:** renderer graph, stages, cameras, render features, connections, deletion, and Undo.
- **Project builds:** executable project selection, build, launch, cancellation, and diagnostic output.
- **Application shell:** document tabs, panels, Output, saving, close confirmation, and editor-game shutdown.

### Infrastructure fixes

Changes cover the game-thread dispatcher, build step completion, native library loading, logging without blocking the UI, and package saving. The scene compiler clones source entities so that building a runtime scene does not mutate the asset. Vulkan shader entry-point pointer lifetimes and render target recreation during resize have also been fixed.

## Requirements

- .NET SDK **10.0.100**, or a compatible SDK according to `global.json` (`rollForward: latestMinor`).
- Git LFS: some dependencies and resources are stored as LFS objects.
- NuGet access for the initial package restore.
- For the graphical editor and viewport checks on Linux: an X11/XWayland session and a Vulkan driver.
- For system trash operations on Linux: `gio`.

This branch uses Avalonia **12.0.2**. The entry point is `Stride.GameStudio.Avalonia.Desktop`.

## Build and run

Run all commands from the repository root.

```sh
git switch linux-editor-first-steps
git lfs install
git lfs pull

dotnet build sources/editor/Stride.GameStudio.Avalonia.Desktop/Stride.GameStudio.Avalonia.Desktop.csproj -c Debug -m:1
dotnet run --project sources/editor/Stride.GameStudio.Avalonia.Desktop --no-build
```

To open an existing project, pass the path to its `.sln` or `.sdpkg` file:

```sh
dotnet run --project sources/editor/Stride.GameStudio.Avalonia.Desktop --no-build -- /path/to/project.sln
```

An Avalonia solution filter is also available:

```sh
dotnet build build/Stride.Editor.Avalonia.slnf -c Debug -m:1
```

A copy of an existing Stride project containing a scene and a model is a useful starting point for verification. The project's package versions must be compatible with the engine in this branch; local NuGet feed setup depends on the project.

If AssemblyProcessor fails with `Bad IL format`, check that `deps/AssemblyProcessor/` contains binaries rather than text Git LFS pointers. After fetching LFS objects, the corresponding stale cache under `/tmp/Stride/AssemblyProcessor/` may also need to be refreshed.

## Viewport controls

| Action | Controls |
| --- | --- |
| Fly camera | Right mouse button + WASD; Q/E to move down/up; Shift to move faster |
| Pan | Middle mouse button |
| Zoom / orthographic projection size | Mouse wheel |
| Flight speed | Right mouse button + mouse wheel |
| Frame selection or scene | F / Frame |
| Move / rotate / scale | W / E / R when not flying |
| Select / toggle selection | Click / Ctrl + click |
| Temporary snapping | Shift during a transformation |

## Verification

Focused component checks that do not require a graphical session:

```sh
dotnet run --project tests/presentation/Stride.Avalonia.Controls.Smoke
dotnet run --project tests/presentation/Stride.Avalonia.PropertyActions.Smoke -p:BuildInParallel=false
dotnet run --project tests/editor/Stride.Editor.GameController.Smoke
```

The integration check requires a graphical session, Vulkan, and a **disposable copy** of a project containing a scene and a model:

```sh
dotnet run --project tests/editor/Stride.Editor.Viewport.Smoke -- /path/to/copied-project.sln
```

It checks model loading, viewport frames, selection, transformations and Undo, camera input, resize, Asset Preview, and session shutdown. `--keep-open` leaves the window open for inspection.

Additional scenarios are enabled with `--hierarchy-layout`, `--selection-tools`, `--add-asset`, `--asset-toolbar`, `--asset-cards`, `--asset-build-status`, `--asset-delete`, `--prefabs`, `--graphics-compositor`, `--ui-page`, `--ui-library`, `--ui-polish`, `--scene-ui`, and `--project-build`.

Some scenarios save files, use the system trash, or require additional fixtures. Read the [viewport smoke instructions](tests/editor/Stride.Editor.Viewport.Smoke/README.md) before running them, especially `--project-build`, `--prefabs`, and `--asset-delete`.

## Limitations and verification status

- This port is under development. Linux checks do not establish Windows/macOS support.
- Scene and dependency changes may rebuild and reload the entire scene/prefab; camera position and selection IDs are preserved.
- The bitmap fallback uses GPU-to-CPU readback and another upload to Avalonia, capped at 30 FPS. GPU sharing depends on device and backend compatibility.
- The branch documentation records a successful Linux build and individual live smoke runs. These do not guarantee stability across all drivers, projects, or long sessions.
- This README was prepared by reviewing history, code, and existing checks. No new build or graphical test runs were performed.

## Structure and documentation

| Path | Purpose |
| --- | --- |
| `sources/editor/Stride.GameStudio.Avalonia.Desktop/` | Desktop entry point and Avalonia configuration |
| `sources/editor/Stride.GameStudio.Avalonia/` | Game Studio shell, windows, and project commands |
| `sources/editor/Stride.Assets.Editor/` | Shared editor logic, game services, and ViewModels |
| `sources/editor/Stride.Assets.Editor.Avalonia/` | Avalonia asset editor views |
| `sources/editor/Stride.Core.Assets.Editor*` | Session, assets, services, and inspector |
| `sources/presentation/` | MVVM, Quantum, and UI components |
| `tests/editor/` and `tests/presentation/` | Port smoke checks |

Shared logic lives in projects without a UI suffix, Avalonia views in `.Avalonia`, and WPF implementations in `.Wpf`.

- [Viewport architecture, checks, and limitations](docs/editor/avalonia-scene-viewport.md).
- [Initial Linux checkpoint](docs/editor/linux-first-steps.md) — a historical record; its next-step list is partly outdated.
- [Editor architecture and Undo/Redo](docs/editor/README.md).
- [Original xplat-editor roadmap](docs/xplat-editor-roadmap.md) — the baseline plan, which should be checked against the current code.
- [Original Stride README](README.old.md).

## License

Stride is distributed under the [MIT license](LICENSE.md). Third-party dependencies may have their own licenses.
