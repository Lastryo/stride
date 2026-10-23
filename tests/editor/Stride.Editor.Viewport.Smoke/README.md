# Live Avalonia viewport smoke check

Requires a display server, Vulkan-capable graphics and a temporary copy of a
project containing a scene with a model. From the repository root:

```sh
dotnet run --project tests/editor/Stride.Editor.Viewport.Smoke/Stride.Editor.Viewport.Smoke.csproj -- /path/to/copied-project.sln
```

The project builds the desktop editor and copies its native runtime libraries.
It opens the project and a scene using the actual UI dispatcher, without relying
on synthetic desktop clicks. It checks native-host layout, hydrated model data,
all transform modes, hierarchy selection, an undoable asset transform, advancing
render frames, and session shutdown. The transform is undone and assets are not
saved. Use a copy because project loading/restoration may update project files
and asset-build caches.

Add `--keep-open` to inspect the scene after the rendering checks. This skips
the automatic shutdown check. This test does not simulate mouse dragging or
camera keyboard input.

Use `--prefabs` to check both prefab-creation workflows: the `Prefabs > Prefab`
template, opening the Prefab Editor and adding an entity, and `Create prefab
from selection` in hierarchy/viewport menus. It checks routed viewport
right-clicks, camera-drag suppression, parent/child selection deduplication,
new IDs, internal component references, base links and hierarchy state,
serialization, edit propagation, extraction of an overridden existing prefab,
and Undo/Redo. Persistence checks save only the disposable project copy.

Use `--add-asset` to check the WPF template catalog, compact category rows,
icons and descriptions, global multiword search, empty results, reopening and
Escape, Enter activation, real scene creation and selection, direct-file
matching and texture creation, and Undo/Redo. It writes
`/tmp/stride-add-asset.png`. The project is not saved. Native file-picker clicks
are not simulated; the check supplies a PNG to the same import path.

Use `--asset-toolbar` to check Asset View command bindings, a real pending source
update and its Undo/Redo, a serialized remote effect request, import into the
existing or new effect library, pending counts and duplicate suppression,
sorting, thumbnail zoom and name-filter commands. It writes
`/tmp/stride-asset-toolbar.png`. The project is not saved.

Use `--asset-cards` to check the WPF asset tile geometry, column spacing, name
wrapping, arrow/Home/End navigation, real card bindings, hover/selection and dirty
markers without opening the scene viewport.
It writes `stride-asset-cards-320.png` and `stride-asset-cards-640.png` to the
system temporary directory for visual inspection.

Use `--asset-build-status` to exercise the blue root, green dependency and gray
excluded dots through real card button activation, including the tooltip hit
area, recursive updates, Undo/Redo and package serialization. It creates two
temporary scene assets, checks root deletion with Undo/Redo and forced roots,
and undoes the changes without saving the project.

Add `--hierarchy-layout` to also check hierarchy controls, reparenting, deletion,
asset drops, and root sibling reordering before/after another entity or into the
scene root. Reordering checks both asset/tree order and undo/redo.
Viewport Delete checks modal confirmation, cancellation, removal and undo.
Created and dropped entities must receive viewport focus and selection without
reselection, including all entities in a drop of multiple assets.
The hierarchy check also covers mixed folder/entity deletion and nested folder
undo/redo, Ctrl+D in the viewport, hierarchy and Asset View, and independent
asset/entity copies with new IDs. Pivot/Center checks group move, rotate and
scale, including offsets from the group center and the toolbar binding.
Use `--selection-tools` to run the folder, duplicate and Pivot/Center checks
without the full hierarchy layout sequence.

Use `--asset-delete` on a disposable project copy to check Asset View Delete,
modal confirmation/cancel, Undo/Redo, moving saved asset files to the system
trash on Save, failed-trash retry, and Undo after Save. On Linux this requires
`gio` and a project copy on the home filesystem (GIO may refuse trash on a
system `/tmp` mount). It uses a temporary `XDG_DATA_HOME` under the copied
project's `obj` directory to keep test trash separate. The check saves the copy.
Delete, Undo and Redo must update the visible cards without reopening the folder.

## Build / run / cancel check (Linux)

`--project-build` needs a dedicated copy of the sample and a small executable
project named `Launch.csproj`. The check saves the session and creates/removes
`BrokenSmoke.cs`, `Directory.Build.props`, `Directory.Build.targets` and
`cancel-gate.txt` in that executable's directory. Use a new `Launch Project`
subdirectory with no existing props/targets. The space also tests launch paths.

For a copy of AnimatedModel, create `Launch Project/Launch.csproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <OutputType>Exe</OutputType>
    <StrideProjectType>Executable</StrideProjectType>
    <StridePlatform>Linux</StridePlatform>
  </PropertyGroup>
  <ItemGroup>
    <ProjectReference Include="../AnimatedModel.Game/AnimatedModel.Game.csproj" />
  </ItemGroup>
</Project>
```

Create `Launch Project/Program.cs`:

```csharp
System.IO.File.WriteAllText(
    System.IO.Path.Combine(System.AppContext.BaseDirectory, "run-marker.txt"),
    System.Environment.ProcessId.ToString());
System.Console.WriteLine("Build/run smoke launched.");
```

Add it to the copied solution, restore, then run:

```sh
dotnet sln /path/to/copied-project.sln add "/path/to/Launch Project/Launch.csproj"
dotnet restore /path/to/copied-project.sln
dotnet run --project tests/editor/Stride.Editor.Viewport.Smoke/Stride.Editor.Viewport.Smoke.csproj -- /path/to/copied-project.sln --project-build
```

The check builds the library, launches the executable through its apphost and
through `dotnet`, verifies the marker/PID, prevents launch after a C# error,
checks Output and command states, cancels an MSBuild `sleep 30` gate, and closes
the session during another build. It tests process launching rather than a full
game scene. Run `--close-confirmation` on the usual temporary sample separately
to check that cancelling closure unlocks the project selector and commands.

`--graphics-compositor` opens the compositor editor without starting the scene
viewport. It creates a temporary compositor fixture, checks its three panels and
renderer graph, adds/renames stages, adds cameras and render features, connects
and disconnects the game entry point, removes referenced stages/renderers, checks
Undo, and closes/reopens the editor. Run against a temporary project copy. The
scenario does not save the project.

```sh
dotnet run --project tests/editor/Stride.Editor.Viewport.Smoke/Stride.Editor.Viewport.Smoke.csproj -- /path/to/copied-project.sln --graphics-compositor
```

`--property-type` selects an existing entity with a Light component, changes its
light type through Quantum, and undoes the change. It checks that the type
selector remains visible without reselecting the entity, while the replaced
row is kept alive to exercise template-cache invalidation. Use a temporary
copy of a scene with a Light component (for example AnimatedModel).

`--ui-page` creates a temporary UI Page and checks container capacity, cycle
prevention, subtree reparenting and sibling order, factory insertion and Canvas
positions, Ctrl+C/X/V/D, Delete, F2 with Enter/Escape, command availability,
asset/game child order, routed tree/viewport drops, viewport context menus,
and Undo/Redo. It also closes the editor and waits for the game to stop.
It opens the real UI viewport and uses
the system clipboard (restoring its previous text afterward). The fixture is
not saved. Native pointer dragging is not simulated by this scenario.

```sh
dotnet run --project tests/editor/Stride.Editor.Viewport.Smoke/Stride.Editor.Viewport.Smoke.csproj -- /path/to/copied-project.sln --ui-page
```

`--ui-library` creates temporary Page/Library fixtures and checks creation from
selection, source-library navigation and detachment, export names and palette
updates, multiple roots and active-root rendering, inheritance and cycle
prevention, Undo/Redo, serialization, and closing/reopening the library editor.
It uses the real UI viewport and does not save the project.

```sh
dotnet run --project tests/editor/Stride.Editor.Viewport.Smoke/Stride.Editor.Viewport.Smoke.csproj -- /path/to/copied-project.sln --ui-library
```

`--ui-polish` checks the grouped/sorted palette and type icons, revealing a
selection in a collapsed tree, the four adorner color control bindings, error
copy/resume buttons, and per-asset camera settings saved to disk and restored
on reopening. It restores the clipboard and writes package user settings; use
a temporary project copy.

```sh
dotnet run --project tests/editor/Stride.Editor.Viewport.Smoke/Stride.Editor.Viewport.Smoke.csproj -- /path/to/copied-project.sln --ui-polish
```

`--scene-ui` checks that the game settings provider follows the current project,
then creates a UI Page asset and a Scene containing a UIComponent referencing it.
It reads the final GPU frame to verify fullscreen UI in Group0 and Group1,
disabling/re-enabling the component, and closing the editor. It also resizes the
viewport to landscape and portrait shapes, checking effective page layout and
a square's pixel aspect in all three Resolution Stretch modes (nine cases).
Adaptive modes preserve the square; FixedWidthFixedHeight keeps its configured
non-uniform scaling behavior. Use a temporary
AnimatedModel sample copy: its project compositor supports both UI groups.
Successful frames are saved to `/tmp/stride-scene-ui-Group0.png` and
`/tmp/stride-scene-ui-Group1.png`. Fixture assets are not saved.

```sh
dotnet run --project tests/editor/Stride.Editor.Viewport.Smoke/Stride.Editor.Viewport.Smoke.csproj -- /path/to/copied-AnimatedModel.sln --scene-ui
```

`--prefabs` now also saves and reads the generated scene/prefab from disk and
checks recreation of a missing file at a clean save point. Run it on a disposable
copy under the system temporary directory, or explicitly place a
`.stride-disposable-smoke-project` marker in the copied startup package directory.
Linux deletion checks need a filesystem supported by the system trash; a tmpfs
`/tmp` may not support it, so use a marked disposable copy on the home filesystem.

Prefab checks also delete the generated prefab before its first save and after
saving, verify that scene entities lose their base links while preserving values
and references, check persisted scene data and the deleted file, and exercise
Undo/Redo across saves. Reopening that temporary solution must still succeed.

Use `--sprite-sheet` to check the Avalonia Sprite Sheet editor: cropped thumbnails,
shared list/inspector selection, image loading, alpha/color-key magic wand,
add/delete/duplicate/reorder, clipboard, routed mouse gestures, pixel bounds,
locked borders, pivot and region Undo/Redo, initial source sizing, import,
serialization and reopening. It writes `/tmp/stride-sprite-sheet.png` and never
saves the project. See `docs/editor/avalonia-sprite-sheet.md` for the controls.

Use `--asset-picker` to check the real inspector button and modal Asset Picker:
initial location/selection, mandatory type and custom filters, folders/search,
empty results, independent selection, cancellation, combined reference assignment,
unchanged-reference preservation, Undo/Redo, multiple selection and read-only
commands. The disposable project must contain two local textures. It writes
`/tmp/stride-asset-picker.png` without saving the project. See
`docs/editor/avalonia-asset-picker.md` for controls and the service contract.
