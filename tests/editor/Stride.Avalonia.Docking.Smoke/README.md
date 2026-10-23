# Avalonia docking smoke test

Build `sources/editor/Stride.GameStudio.Avalonia/Stride.GameStudio.Avalonia.csproj`, then run:

```sh
dotnet run --project tests/editor/Stride.Avalonia.Docking.Smoke -p:BuildProjectReferences=false
```

Exercises the real Game Studio layout manager with Avalonia Headless and Dock Fluent templates, without a native display or game project:

- Tool and document views move between live visual presenters without acquiring two parents; their instances and view models survive the move.
- A close command with no active tab is ignored and the closed panel can still reopen.
- Closing a tool updates the View menu state; reopening restores its pane.
- Tabs move between panes and can dock as documents.
- Auto-hide preserves visibility and the original pane across serialization.
- Hidden panels reopen after serialization.
- Floating windows preserve their model and bounds.
- Float is hidden for a sole floating panel, becomes available when another tab joins, and returns after docking in the main workspace.
- Forced repeated Float inside an existing floating window keeps the asset view in the current visible window, preserving its instance and data context.
- Dock as tabbed Document returns a floating panel to the main document area and closes its empty host.
- Closing the last floating tool removes its host; View reopens the panel in the main workspace.
- The default layout has no floating windows; restoring an older layout discards empty hosts.
- UI controls are excluded from saved layouts; invalid layouts fall back to defaults.

The layout is stored under `Internal/Layout/AvaloniaDockLayout`, independently of
AvalonDock's WPF XML format. This test changes settings only in memory.

Manual checks still required in the running editor: drag targets, resizing,
Ctrl+Tab, native window movement across monitors, scene rendering after undocking,
and asynchronous editor teardown (including an editor that fails to close).
