# Linux editor development: initial checkpoint

The `linux-editor-first-steps` branch starts from `origin/xplat-editor`
(`18115dbbe`). This is the existing Avalonia editor implementation; it is older
than `master`, so future work must account for changes on both branches.

## First fix

`TimeSpanEditor.PlaceholderTextProperty` was registered for `DateTimeEditor`.
Register it for `TimeSpanEditor` so each control owns its property independently.
The focused regression check is documented in
[`tests/presentation/Stride.Avalonia.Controls.Smoke`](../../tests/presentation/Stride.Avalonia.Controls.Smoke/README.md).

## Full editor build

From the repository root with .NET 10:

```sh
dotnet build sources/editor/Stride.GameStudio.Avalonia.Desktop/Stride.GameStudio.Avalonia.Desktop.csproj -m:1
```

The initial attempt in this environment could not complete package retrieval:
NuGet reported `NU1301` / permission denied for `api.nuget.org`. This does not
establish whether the editor compiles or runs. Retry with working NuGet access
before interpreting subsequent compiler or native build failures.

The retry with network access progressed to compilation, then failed with
`MSB4062` / `Bad IL format` while loading `Stride.Core.AssemblyProcessor.dll`.
Inspection confirmed that `deps/AssemblyProcessor/netstandard2.0/Stride.Core.AssemblyProcessor.dll`
is a Git LFS pointer rather than a binary. Git LFS is not installed in this
environment. Fetch the LFS objects before retrying; the AssemblyProcessor cache
under `/tmp/Stride/AssemblyProcessor/` may also contain copied pointer files
and must be refreshed for the affected hash.

Git LFS 3.8.0 was subsequently installed locally at
`/home/lastryo/Work/.tools/git-lfs/git-lfs` (system installation requires sudo).
The release archive SHA-256 matched the official GitHub release metadata.
All 2380 LFS objects for the checked-out branch were downloaded. The pointer-only
AssemblyProcessor cache was moved to a backup under
`/tmp/stride-lfs-pointer-cache-45u8wplg/`. Repository-local LFS filters use the
absolute executable path, so normal Git operations work without changing PATH.
For explicit LFS commands use:

```sh
PATH=/home/lastryo/Work/.tools/git-lfs:$PATH git lfs pull
```

After downloading LFS objects and refreshing the cache, the full Avalonia desktop
editor build succeeded on Linux: 0 errors, 2687 warnings, elapsed 1:01.91.
Log: `/tmp/stride-linux-build-lfs.log`. This validates compilation only;
application startup and scene rendering have not yet been tested.

## Opening a real sample

The local editor now accepts an existing `.sln` or `.sdpkg` startup argument,
opening it after the main window loads. `MainWindow.OnLoaded` calls its base
implementation so subscribers receive the event. Session warnings and errors
are also written to stderr for diagnosis.

`/home/lastryo/Work/stride-examples/AnimatedModel/AnimatedModel.sln` was prepared
from the official sample and successfully opened. Its library uses the local
`4.4.0.1` engine packages and a local NuGet feed. See `README.local.md` beside
the sample for reproduction and limits. The sample library builds and the
editor reaches the loaded solution state; viewport rendering is unverified.

## Next milestones

1. Review the build warnings and reproduce the successful build as needed.
2. Launch the Avalonia desktop project, then check project opening,
   property editing, and scene rendering separately.
3. Compare the editor branch with `master` before importing build-system changes.
4. Investigate native viewport hosting in `GameEngineHost`: it still contains
   disabled Win32 integration and needs a separate platform-specific design.

The placeholder fix is an isolated control correction, not completion of native
Linux editor support.
