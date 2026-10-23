# Avalonia Sprite Sheet editor

Double-click a Sprite Sheet asset to open its frame list and source image.
Selecting a frame displays its Quantum-backed properties in the main inspector;
**Sheet properties** switches the inspector to the whole asset. Frame names,
source paths, orientation, pixels per unit, center convention and borders can be
edited there. Double-click a frame name to rename it inline; thumbnails show the
frame region. Frames can also be reordered by dragging them in the list.
**Open image** launches the default image application; **Show folder** opens its
source directory.

The toolbar adds, imports, deletes, duplicates and reorders frames. Import accepts
multiple local images or files dropped on the editor, initializes each frame to
the whole source image and records the operation as one undo transaction. Copy
and paste use the editor clipboard serialization service.

Image tools:

- **Region**: drag the rectangle to move it, its eight handles to resize it, or
  drag outside it to draw a new region. Coordinates snap to image pixels and stay
  within the image bounds.
- **ColorPick**: click a pixel to set and enable the sheet color key.
- **MagicWand**: click a sprite to detect its region using the existing
  `TextureTool.FindSpriteRegion`. **Alpha wand** chooses alpha transparency;
  otherwise the tool uses the sheet color key.
- **SpriteCenter**: click or drag to place the pivot, respecting `CenterFromMiddle`.
- **Borders**: drag the nearest border line. Borders cannot overlap. **Lock
  borders** preserves their image positions when the texture region is resized.

Mouse wheel zooms around the pointer; middle-button drag pans. Fit, 1:1 and Frame
control the view. F frames the selection, M toggles the wand, Space selects the
next frame, Delete removes selection, and Ctrl/Cmd+C, V and D copy, paste and
duplicate. Escape cancels a pending image gesture. Text editing retains its own
keyboard handling.

A gesture previews locally and commits asset changes once on release. Capture
loss, detaching the view or changing selection cancels the preview. Source image
changes are watched and reloaded after a short debounce. The view owns and
disposes its decoded textures, Avalonia bitmaps and source watcher.

View models and property/collection changes live in `Stride.Assets.Editor`.
The canvas and image cache live in `Stride.Assets.Editor.Avalonia`; there are no
WPF dependencies or new packages.

## Verification

Use a disposable project copy containing a scene (the shared smoke harness waits
for one during startup):

```sh
dotnet run --project tests/editor/Stride.Editor.Viewport.Smoke/Stride.Editor.Viewport.Smoke.csproj \
  -p:GeneratePackageOnBuild=false -p:StrideSkipWorktreeVersion=true \
  -- /path/to/copied-project.sln --sprite-sheet
```

The check creates a small transparent PNG and a temporary Sprite Sheet asset.
It checks image decoding, alpha/color-key region detection, list and inspector
selection, non-mutating view initialization, add/delete/duplicate/reorder,
clipboard, routed image gestures, Undo/Redo, bounds, pivot/borders, import,
serialization and closing/reopening. It does not save the project. It writes
`/tmp/stride-sprite-sheet.png` for visual inspection.

The outline color is configurable. The default application handles external
image editing; availability depends on the desktop file associations.
