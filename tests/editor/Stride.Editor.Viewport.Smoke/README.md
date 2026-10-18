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
