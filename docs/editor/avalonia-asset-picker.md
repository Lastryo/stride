# Avalonia Asset Picker

Use **Select an asset** beside a content reference in the property inspector or
in its context menu. The picker opens at the referenced asset's directory and
preselects it. With an empty or broken reference, it starts at the current asset
view's selected asset directory, or the first local package.

The modal window shows package/folder navigation, thumbnails, asset names,
paths and types. Search matches all entered words against package/path, type
and tags, ignoring case. **All packages** searches the entire session, including
package dependencies. **Include subfolders** changes folder scope. The type
selector can narrow the results, but cannot bypass the reference's accepted
types or the caller's additional predicate.

Select a row and press **Select** or Enter; double-click also confirms. Escape,
Cancel and closing the window discard the selection. No asset is cleared on
cancellation. Use the existing **Clear the reference** command to remove a
reference explicitly.

Browsing has independent selection and does not change the main asset view,
property inspector or preview. Changing scope/search removes hidden selections;
confirmation revalidates assets against the current session. Read-only
references cannot invoke the picker. Assigning a reference uses the existing
Quantum command transaction and supports Undo/Redo, including a combined
selection of properties with different initial references. Targets already
referencing the selected asset are left untouched.

`AssetPickerViewModel` is in the framework-independent
`Stride.Core.Assets.Editor`. It observes asset membership and name/deletion
changes while open and unregisters its observers in `Destroy()`.
`IEditorDialogService.ShowAssetPickerAsync(picker)` shows the Avalonia window and
returns an empty list on cancellation. Callers configure `AcceptedTypes`,
`Filter`, `InitialAsset`, `InitialLocation`, `AllowMultiSelection` and `Message`
before showing it, then destroy the model after consuming the result.
`PickupAssetCommand` owns that lifecycle for property references. An empty
accepted-type list permits all types for general-purpose callers.

The picker browses assets; asset creation, deletion and renaming remain in the
main asset view.

## Verification

Run against a disposable project copy containing a scene and at least two local
texture assets:

```sh
dotnet run --project tests/editor/Stride.Editor.Viewport.Smoke/Stride.Editor.Viewport.Smoke.csproj \
  -p:GeneratePackageOnBuild=false -p:StrideSkipWorktreeVersion=true \
  -- /path/to/copied-project.sln --asset-picker
```

The test uses the real inspector button and modal service. It covers initial
selection, compatible-type restrictions, folder/all-package scope, search and
empty results, isolated selection, Escape/window-close cancellation, combined
reference assignment, unchanged-reference preservation, Undo/Redo, custom
predicates, multiple selection and read-only command invocation. The project is
not saved. It writes `/tmp/stride-asset-picker.png` for visual inspection.
