# Avalonia Visual Script editor

The Avalonia editor replaces the Visual Script placeholder and opens `VisualScriptAsset` (`.sdvs`) in its own asset tab. The WPF reference is `Stride.Assets.Presentation.Wpf/AssetEditors/VisualScriptEditor/Views/VisualScriptEditorView.xaml` and its editor/method/block view models.

## Opening and editing

Choose **Add asset → Script → Visual script** in a local game project, then double-click the new asset. The template creates a `Stride.Engine.SyncScript` with an override of `Update` and a `Start` block. Existing `.sdvs` assets open through the same asset-editor registration.

The surface follows the WPF layout: a left column for the base type, variables, functions and function parameters; a vertical splitter; and the graph on the right. Variables show bold names and gray types. The original add/remove vector icons are reused.

- Add/remove variables, functions and parameters with the adjacent buttons. Double-click a row or press F2 to edit it; Enter commits and Escape cancels. Parameters include `ref`/`out`, type and name.
- The function add menu offers a new function and overridable base/interface methods. Access, virtual modifier, static flag and return type are editable below the function list.
- Right-click the graph to search block templates or browse their categories. Factories include Start, custom code/expression, condition, foreach and variable access. Method-call creation opens the project-aware Roslyn symbol search (up to 100 results).
- Hold the left mouse button on a variable or parameter row, drag it onto the graph and release to choose Get or Set. Function rows select the function graph; they are not drag sources. Dropping a variable onto a block with a compatible `BlockDropTarget` updates that block's reference. The variable inspector suggests current variables, parameters and accessible inherited fields/properties.
- Drag an output port onto an input port to connect them. Execution/value mismatches, duplicate links and self-links are rejected. Disconnected ports are hollow; connected ports are filled.
- Click/Ctrl-click nodes and links, drag a selection rectangle, or use Ctrl+A. Delete removes the selection and all links incident to deleted blocks.
- Drag node titles to move the selection. Positions snap to 16 units and commit on release as one undoable operation. Middle-drag pans; the wheel zooms around the pointer. Each function retains its own camera while the tab remains open. Escape cancels an active gesture.

Node title/content/selection colors, 15-unit connectors, execution/value colors, hover states, shadows and the `#49494E` graph background come from the WPF resources. Compiler diagnostics tint the corresponding nodes/links red and appear in tooltips.

Selecting a block exposes its Quantum properties in the normal inspector. Custom C# fields use AvaloniaEdit, bundled JetBrains Mono, syntax highlighting, native Undo/Redo and project-aware completion after `.` or Ctrl+Space, with the shared original RoslynPad glyphs and descriptions. Completion uses an immutable snippet project and writes no temporary source files into the user's project.

## Model, compilation and persistence

The editor operates on the existing `VisualScriptAsset`, `Method`, `Block`, `Slot` and `Link` models through Quantum. Mutations participate in the normal asset dirty tracking and Undo/Redo. Editing code and regenerating its ports share a transaction; stable ports retain IDs, values and links. Removing a port removes its dependent links.

Background analysis compiles an asset snapshot with the project's Roslyn compilation. Syntax annotations map diagnostics to block/link IDs. Base-class/interface override suggestions and slot generation refresh after graph edits, edits in open C# buffers and assembly hot reload. Session asset notifications arrive on worker threads; model/collection updates and derived Quantum changes are marshalled to the UI dispatcher. Closing cancels analysis and releases subscriptions.

A Visual Script property-graph definition marks link endpoints as references to the actual slots. The normal save path writes reference metadata, preserving endpoint identity and ownership when loading `.sdvs` again. Opening older editable files with inline endpoint copies repairs their references by slot ID; save the asset to persist the repair.

Shared generator fixes required by the port preserve SDK parse features during flow analysis, parse qualified base types/namespaces/usings correctly, and derive the generated class name from the `.sdvs` filename rather than the `.sdvs.cs` filename. Empty base types are allowed.

The existing engine still determines Visual Script execution and code generation. In particular, ref/out method-call arguments remain an engine limitation already present in WPF; this port does not introduce a separate compiler or file format. WPF could not be launched on the Linux verification machine. Layout, commands and resource values were compared with its source; pixel identity, font metrics and platform drag/drop on Windows/macOS still require verification.

## Verification

Use a disposable project copy; the smoke adds temporary assets in that session and briefly changes an existing C# asset's in-memory buffer, restoring it afterwards.

```sh
dotnet build tests/editor/Stride.Editor.Viewport.Smoke/Stride.Editor.Viewport.Smoke.csproj -m:1 -p:StrideSkipWorktreeVersion=true
mkdir -p /tmp/stride-script-parity-config
XDG_CONFIG_HOME=/tmp/stride-script-parity-config dotnet tests/editor/Stride.Editor.Viewport.Smoke/bin/Debug/net10.0/Stride.Editor.Viewport.Smoke.dll /tmp/game-copy/Game.sln --visual-script --glx-presentation
```

The smoke opens the actual tab; checks worker-thread asset notifications/UI dispatch, variables/functions/parameters, project compilation and generated C# syntax, factories, routed variable/parameter drag initiation through list selection, connection and node-drag gestures, selection deletion, Undo/Redo, code/port regeneration, native C# completion insertion/Undo, compiler error mapping/clearing, method search, source-buffer refresh, legacy endpoint repair, template creation and `.sdvs` serialization with endpoint identity. It renders `/tmp/stride-visual-script-editor.png` and saves a reference-aware fixture at `/tmp/stride-visual-script-roundtrip.sdvs`.
