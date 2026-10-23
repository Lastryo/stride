# Avalonia LivePlay

Select the executable project for the current desktop platform, then choose
**Project → LivePlay**. The editor saves and builds the project and its assets,
starts a separate Stride debugger/game process, and loads the local game
assemblies there. **Project → Stop LivePlay** stops that child process.
Closing the session also stops it. Output contains a **LivePlay** logger with
startup, update and runtime diagnostics.

Keep **Project → Automatically reload assemblies** enabled. Saving C# source
triggers the editor's existing build/reload service; successful assembly updates
are then sent to the running game over the existing debugger named-pipe protocol.
**Project → Reload assemblies** works as a manual alternative. Compilation errors
leave the running game and its components at the previous successful generation.
Fixing the source and saving it applies the next generation without restarting
that game process.

At the runtime switch, the debugger holds the game's tick lock and snapshots
components from the affected assemblies as YAML. It creates replacement
components, restores serialized members and members marked with
`ScriptComponent.LiveScriptingMask`, resolves entity/content references and cyclic
component references, and replaces the components on their original entities.
Component IDs survive the switch. The ScriptSystem's existing `LiveReload` hook
marks old and new scripts, and old scripts are cancelled after replacement.
A restoration failure rolls back the assembly registration and component switch.

This also repairs the runtime path used by the WPF debugger: its
`LiveAssemblyReloader.Reload` previously threw `NotImplementedException` before
performing a switch. The debugger is now built for the cross-platform editor
framework, its callback pipe is explicitly opened, and each session has unique
pipe addresses. The child uses the executable project's built `data` directory.

## Verification

Use a disposable copy of a project containing a scene, a `.Game` code project and
an executable for the host platform:

```sh
dotnet run --project tests/editor/Stride.Editor.Viewport.Smoke/Stride.Editor.Viewport.Smoke.csproj \
  -p:GeneratePackageOnBuild=false -p:StrideSkipWorktreeVersion=true \
  -- /tmp/copied-project/Project.sln --live-play --glx-presentation
```

The check temporarily adds a custom game and two scripts to the copied code
project. It starts an actual child game, changes script code through the source
watcher and checks the same PID, component IDs, values, live-only counters,
cyclic script/entity references and new property defaults. It also checks
compiler-error retention, recovery, stopping/restarting the child and closing
during startup. The fixture source
and runtime marker are removed afterwards. Builds modify the copied outputs;
the normal LivePlay save/build step can save the copied project.

## Scope and limits

Updates currently transfer complete game assemblies from the existing MSBuild
pipeline. The WPF Roslyn source-group compiler and Visual Studio debugger picker
are not required or connected. Game components are replaced; the existing Game
instance, static state, service instances and suspended async execution stacks
are not recreated. Fields need a serialization/live-scripting member mask to
survive. Removing the type of a component that is still present in the running
scene rejects the update and retains that game's previous generation.

Assets are built for the initial launch. This feature does not stream subsequent
scene/texture edits to the external game. Restart LivePlay to load those changes
or changes to game initialization/dependencies. Old CLR assemblies remain loaded
until the child exits. Linux is covered by the smoke check; Windows and macOS
need their own runtime verification.
