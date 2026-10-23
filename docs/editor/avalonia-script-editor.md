# Avalonia C# editor

Double-click a C# source asset in the Asset View to open its editor tab. The editor uses AvaloniaEdit with a session-wide Roslyn MSBuild workspace for the loaded local code projects.

## Available actions

- Syntax and semantic highlighting, line numbers, indentation and text selection.
- Native text Undo/Redo (`Ctrl+Z` / `Ctrl+Y`) Find (`Ctrl+F`) and Replace (`Ctrl+H`).
- Project-aware completion after `.` or `Ctrl+Space`, including Stride types. Suggestions are grouped by symbol kind (properties, fields, constants, methods, extension methods, events, locals/parameters, types, namespaces and keywords), alphabetically within each group. Filtering preserves this order. Vector glyphs distinguish symbol kinds and add private/protected/internal access badges.
- Symbol information at the caret and in completion descriptions, and the Format button.
- Parameter help after `(`, `[` or `,`, or on `Ctrl+Shift+Space`. Help opens next to the caret. The current parameter is enclosed in brackets; use the dropdown to select an overload. Named arguments are supported. Escape dismisses the help.
- Source definition navigation (`F12`). Library symbols open a read-only window with the assembly identity and public type/member declarations.
- Compiler code fixes and Roslyn refactorings through the inline bulb, the context menu or `Ctrl+.`. Select an action, preview Before/After for each affected file, then **Apply to buffers**.
- Symbol rename through the context menu or `F2`, with the same multi-file preview. References in the loaded workspace are renamed; strings, comments and filenames are left unchanged.
- Errors and warnings are underlined in the document. Hover a marker for the message and severity icon. The context menu’s **Errors and warnings** submenu navigates to a diagnostic.
- Save (`Ctrl+S` or the context menu) saves the session, including the source buffer. Saved files participate in the existing assembly Hot Reload / LivePlay flow.

Action previews never modify buffers. Applying a preview validates all target buffers and disk snapshots before changing any source, and refuses stale previews and read-only assets. Applying an action does not save files. Affected source files are opened in tabs so text changes can be undone with each tab’s native Undo stack. History survives switching tabs. Undo is per file, so undoing a multi-file action requires undoing each affected file. Closing a tab retains its buffer but ends that tab’s undo history. Rename is refused if it introduces new compiler errors.

The workspace overlays unsaved source buffers without writing them to disk. Source changes mark the asset dirty; closing a tab retains its buffer in the session. Text Undo/Redo belongs to the code editor and is separate from the asset transaction history.

## WPF layout

The normal tab contains only the text editor, matching `ScriptEditorView.xaml`: there is no permanent toolbar, status row or diagnostics panel. The document uses the WPF background (`#1E1E1E` in dark mode, white in light mode), a default 12-point font and the bundled JetBrains Mono v2.304. Regular, italic, bold and bold italic faces load directly from Avalonia resources, without requiring a system font installation or searching a fallback chain. The metadata viewer and action preview use the same font. Ctrl+mouse-wheel zooms between 8 and 72 points within the view.

A 16×16 bulb appears in the margin when actions are available at the caret/selection. Actions open as an anchored menu instead of a separate selection window; edits still have a Before/After preview before application. Commands and their vector icons remain available through the editor context menu.

AvaloniaEdit's base theme is installed in `App.axaml`, so completion lists and their description popups can resolve their templates through the main window. View-local resources are insufficient: AvaloniaEdit parents completion popups to the top-level window rather than the editor control. Editor-specific search styling remains scoped to the view.

The search/replace template follows WPF `ThemeScriptEditor.xaml`: it opens at the top right, uses 150×24 text fields, 24×24 navigation buttons with 16×16 icons, an options dropdown and a three-pixel blue bottom border. The replacement row appears only in replace mode. The underlying AvaloniaEdit search implementation is retained.

Conflict controls and project-load errors appear only when intervention is required. Completion glyphs use the ported vector palette rather than RoslynPad's complete original glyph library. WPF cannot be launched on this Linux test machine, so the comparison is against its XAML and settings; a pixel-level comparison on Windows remains to be done. Font rendering depends on installed fonts and the platform. Font zoom is currently local to the view rather than persisted like WPF’s font-size setting.

## External changes and saving

Clean files reload when changed externally. If both the buffer and the disk changed, saving is blocked until the conflict is resolved:

- **Reload from disk** discards the buffer and clears text undo history.
- **Keep changes** accepts the current disk version as the baseline and allows saving the buffer over it.

The session also checks previously opened sources when their tabs are closed, so global Save cannot silently overwrite an external edit behind a retained dirty buffer. Existing encoding/BOM and line endings are preserved; Format keeps the document's newline style. Read failures are reported without discarding the buffer.

## Scope and limitations

Semantic services require a successfully loaded local C# project and its restored references. Project loading runs in the background and failures appear in the status bar. Scripts linked from outside the loaded project directories are currently unsupported. Rename searches the projects present in the workspace; projects not loaded by this session are outside its scope.

F12 opens source assets known to the session, or displays metadata declarations. Metadata viewing does not decompile method bodies. Parameter help covers method calls, constructors and indexers; it is implemented using public Roslyn semantic APIs and does not reproduce every language-specific signature-help feature of an IDE.

Context actions use the built-in C# providers. Diagnostics supplied to code fixes are compiler diagnostics; style/analyzer diagnostics, Fix All, external extensions and actions requiring new/deleted files, project/reference changes or non-text operations are not supported. Such actions are refused when their operations are inspected. Full WPF/RoslynPad feature parity is not claimed.

## Bundled font

The unmodified JetBrains Mono v2.304 TTFs are from the [official tagged release](https://github.com/JetBrains/JetBrainsMono/tree/v2.304/fonts/ttf). The editor embeds four faces from `Resources/Fonts/JetBrainsMono` as Avalonia resources. This is the original JetBrains Mono family, without Nerd Font additions.

The original [SIL Open Font License and copyright notice](../../sources/editor/Stride.Assets.Editor.Avalonia/Resources/Fonts/JetBrainsMono/OFL.txt) are kept alongside the fonts, copied to build/publish output, and included in the NuGet package under `licenses/JetBrainsMono/OFL.txt`. Attribution is listed in the repository’s `THIRD PARTY.md`.

## Input responsiveness

Diagnostics, semantic classification, completion and hover information run on worker threads. Unchanged source snapshots reuse the same Roslyn solution. Edits preserve incremental `SourceText` change ranges so unchanged syntax and method bodies can be reused. The semantic colorizer uses a binary search to visit only spans overlapping a rendered line and shares its brushes. Positioning the action bulb does not construct off-screen visual lines.

The smoke check types in a 92,462-character fixture with 1,500 methods, including pauses that let background analysis run. It verifies Roslyn snapshot reuse and single-character change ranges, and checks input-handler time and dispatcher heartbeat gaps. On the Linux/X11 test machine, one before/after run reduced the maximum input-handler time from 25.7 to 6.9 ms and the maximum UI heartbeat gap from 9,946.3 to 60.7 ms. These are local regression measurements, not a guarantee for every project or machine. The fixed run used Liberation Mono. A subsequent run with the bundled JetBrains Mono measured 3.4 ms for the maximum input-handler time and 39.4 ms for the maximum UI heartbeat gap on the same fixture. Restart an already running editor to load the updated assemblies.

## Verification

Build the viewport smoke project and run it against a disposable copy of a Stride game solution:

```sh
dotnet build tests/editor/Stride.Editor.Viewport.Smoke/Stride.Editor.Viewport.Smoke.csproj -m:1 -p:StrideSkipWorktreeVersion=true
dotnet tests/editor/Stride.Editor.Viewport.Smoke/bin/Debug/net10.0/Stride.Editor.Viewport.Smoke.dll /tmp/game-copy/Game.sln --script-editor --glx-presentation
```

The smoke check verifies embedded font resources, all four actual JetBrains Mono faces and the distributed license, then opens the actual Avalonia tab and verifies text/highlighting, native Undo/Redo, editor-only WPF layout, compact search/replace with native Undo, inline action/severity icon wiring, symbol glyphs/access badges and a rendered light/dark icon sheet, project-aware `Transform.Position` completion, actual dot input opening a rendered list with icons, Enter acceptance with native Undo, and Roslyn insertion/description, semantic errors, symbol information, F12 and read-only metadata UI, positional/named parameter help and its caret popup, multi-file rename/preview/Undo/stale protection, an add-using compiler fix, a numeric refactoring, formatting, UTF-8 BOM/CRLF saving, external changes, conflict protection and closed-buffer retention. It restores the source fixture afterwards. Linux/X11 was checked; Windows and macOS were not checked.
