# Mixed-value property actions regression check

From the repository root:

```sh
dotnet run --project tests/presentation/Stride.Avalonia.PropertyActions.Smoke -p:BuildInParallel=false
```

The check reads the production property templates and executes their mixed-value
placeholder actions on the actual Avalonia controls. It checks that the action
can resolve the property and that the control receives the expected placeholder.
It requires no display server or rendering backend.

To isolate the reported Vector3 failure:

```sh
dotnet run --project tests/presentation/Stride.Avalonia.PropertyActions.Smoke -p:BuildInParallel=false -- --vector3-only
```

Before the fix, the Vector3 check reproduced `Cannot find a property named
Watermark on type Vector3Editor`. The complete check also failed on NumericTextBox.
After replacing the old property name with `PlaceholderText`, all 23 actions pass.

This covers the runtime action that caused the exception. It does not simulate
multi-selection through the complete editor, render the controls, or investigate
unrelated native crashes on preview threads.
