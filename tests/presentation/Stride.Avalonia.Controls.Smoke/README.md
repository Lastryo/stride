# Avalonia control registration smoke check

Run from the repository root on Linux or Windows with .NET 10:

```sh
dotnet run --project tests/presentation/Stride.Avalonia.Controls.Smoke
dotnet run --project tests/presentation/Stride.Avalonia.Controls.Smoke -- --timespan-first
```

Each command starts a separate process so both static initialization orders
are checked. The check verifies property ownership and independent placeholder
values. It requires no display server or native renderer.

The project links the two production control sources to allow this focused
regression check without restoring and building the entire editor and engine.
It does not validate templates, rendering, or the complete editor build.
