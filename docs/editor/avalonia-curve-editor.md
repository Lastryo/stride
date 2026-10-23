# Avalonia Curve Editor

Expand a curve property in the inspector and choose **Edit curve…** beside its
normal type/value editor. For example, a particle emitter's `UpdaterSizeOverTime`
contains `SamplerMain.Curve`. Choose an Animation curve to edit keys. Constant,
function, binary and separate-component curves can also be inspected; their
animation children appear as separate entries in the channel list.

The editor supports `float`, `Vector2`, `Vector3`, `Vector4`, `Color4` and
`Quaternion`. Vector, color and quaternion values have individual component
channels; editing one component preserves the others. Quaternion keys currently
use their raw X/Y/Z/W components; sampled rotation curves also offer Euler
angles and axis/angle previews in degrees. Curve sampling and interpolation use the
existing engine `IComputeCurve` implementations.

- **Alt+click** adds a key at the pointer. **Add key** uses the Key/Value fields.
- Click a key to select it; Ctrl+click toggles selection. Drag empty space to
  select a rectangle. Ctrl+A selects all keys in the current channel.
- Drag selected keys to move them together. The drag previews locally and commits
  one Quantum/undo transaction on release. Escape or capture loss cancels it.
- **Key** and **Value** edit a single selected key numerically. **Delete** removes
  selected keys; **Clear** removes the channel's curve keys. Component channels
  share their curve's keyframe objects and times.
- **Fit**, **Fit X**, **Fit Y** frame the curve. **F** frames selected keys, or the
  curve if no keys are selected. **First/Last** select and frame an endpoint.
- Wheel zooms around the pointer; Shift limits zoom to X and Ctrl to Y.
  Middle/right-button drag pans. Ctrl+Z and Ctrl+Y use the session's undo history.

Opening/rendering the view does not modify the curve. Changes made in the
inspector or by Undo/Redo update the graph through a Quantum node listener.
The editor uses the session's asset graph and save pipeline. Reopening the same
curve activates its existing window; closing the session closes its curve
windows and releases listeners and icon bitmaps. The fit icons reuse WPF assets.

View models and edits live in `Stride.Assets.Editor/CurveEditor`. Drawing,
input and windows live in `Stride.Assets.Editor.Avalonia/CurveEditor`.
The inspector template decorates the existing editor and preserves its normal
curve type picker. No WPF assemblies or new packages are required.

## Verification

Use a disposable project copy with a scene:

```sh
dotnet run --project tests/editor/Stride.Editor.Viewport.Smoke/Stride.Editor.Viewport.Smoke.csproj \
  -p:GeneratePackageOnBuild=false -p:StrideSkipWorktreeVersion=true \
  -- /tmp/copied-project/Project.sln --curve-editor --glx-presentation
```

The check creates an unsaved particle scene, opens Curve Editor through the real
inspector command, checks key insertion, editing, deletion and Undo/Redo, routes
an Alt-click through the canvas, exercises all supported value types and saves
and reloads the scene in memory. It renders `/tmp/stride-curve-editor.png`.
The fixture scene is not saved to the project.

Axes use a linear scale. The engine currently exposes only Linear key tangents;
the editor does not add interpolation modes the runtime cannot evaluate.
