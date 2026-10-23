# Avalonia assembly hot reload

Open a project in the Avalonia editor and edit a C# script in its local game
project. **Project → Automatically reload assemblies** is enabled by default.
After saving the source, the editor debounces changes, builds the local code
projects and replaces their loaded assemblies. **Project → Reload assemblies**
performs the same build and reload manually. The **Hot Reload** logger in Output
shows compilation diagnostics and the reload result.

For a manual check, attach a game script to an entity and change a serialized
property in the inspector without saving the scene. Add another serialized
property to the script and save its source. The inspector should show the new
property and retain the edited value. Undo and Redo switch between the two
assembly generations and their component objects. Introduce a C# compiler error:
the previous generation and the scene values should remain usable.

The watcher handles `.cs`, `.csproj`, `.props` and `.targets` under loaded local
code project directories, and changes to their loaded DLLs. It ignores `bin`,
`obj` and `.git` when watching source. External builds can update the DLL without
another build; timestamp-only notifications do not reload an unchanged module.
Build project / Run project also apply updated assemblies to the editor after a
successful build. Builds and reloads are serialized, and closing the session
cancels pending reload work.

The shared editor service reuses the WPF reload visitor and the existing
MSBuild/assembly processor pipeline. Before switching types, it snapshots the
affected asset objects as YAML. It restores collection item IDs, object references
and override metadata, then refreshes the inspector and notifies asset consumers.
The switch is a single undo transaction. Failed compilation keeps the previous
generation; a failure while restoring objects rolls back the switch.

## Verification

Use a disposable copy of a project containing a scene and a `.Game` code project:

```sh
dotnet run --project tests/editor/Stride.Editor.Viewport.Smoke/Stride.Editor.Viewport.Smoke.csproj \
  -p:GeneratePackageOnBuild=false -p:StrideSkipWorktreeVersion=true \
  -- /tmp/copied-project/Project.sln --hot-reload --glx-presentation
```

The check temporarily writes a uniquely named script and builds the copied
project. It verifies changed method bodies, a new serialized property, unsaved
values, component IDs, entity references, selection, Undo/Redo, scene serialization,
compiler-error retention, automatic source watching and unchanged-DLL suppression.
The source fixture is removed afterwards and the scene is not saved. The copied
build outputs are modified. Projects outside the temporary directory must contain
a `.stride-disposable-smoke-project` marker in the code project directory.

## Current scope

This service reloads assemblies and asset objects inside the editor.
[LivePlay](avalonia-live-play.md) forwards successful reloads to a separately
launched game when started with Project → LivePlay. A normal Run project process
does not receive these updates.

The existing assembly loader uses `Assembly.Load(byte[])`, so old CLR assemblies
remain in memory. Generation files in the temporary `stride-hot-reload` directory
are retained for Undo/Redo and lazy dependency resolution. Restarting the editor
releases those assemblies. Changes to dependencies outside the loaded local code
projects may require rebuilding and reopening the project.
