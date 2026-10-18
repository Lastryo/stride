# Editor game controller dispatch smoke check

Run from the repository root:

```sh
dotnet run --project tests/editor/Stride.Editor.GameController.Smoke/Stride.Editor.GameController.Smoke.csproj
```

This compiles the production game-thread dispatcher and checks queued results,
callback and enqueue failures, cancellation before execution, cancellation on
shutdown, rejection after shutdown, priority preservation, and inline calls.
It needs neither a display server nor a graphics device.

The full editor build validates the platform adapter and scene-game port. This
check does not exercise SDL window hosting, GPU rendering, or native crashes.
