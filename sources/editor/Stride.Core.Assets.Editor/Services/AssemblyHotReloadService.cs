// Copyright (c) .NET Foundation and Contributors (https://dotnetfoundation.org/ & https://stride3d.net)
// Distributed under the MIT license. See the LICENSE.md file in the project root for more information.

using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using Microsoft.Build.Execution;
using Stride.Core.Assets.Editor.ViewModels;
using Stride.Core.Assets.Presentation.ViewModels;
using Stride.Core.Diagnostics;
using Stride.Core.Presentation.Commands;
using Stride.Core.Presentation.Dirtiables;
using Stride.Core.Presentation.ViewModels;

namespace Stride.Core.Assets.Editor.Services;

/// <summary>Watches local game code and loaded binaries; stages complete generations before changing assets.</summary>
public sealed class AssemblyHotReloadService : DispatcherViewModel
{
    private readonly SessionViewModel session;
    private readonly Func<bool> canReload;
    private readonly LoggerResult logger = new("Hot Reload");
    private readonly List<FileSystemWatcher> watchers = [];
    private readonly CancellationTokenSource lifetime = new();
    private readonly string cacheDirectory = Path.Combine(Path.GetTempPath(), "stride-hot-reload", Guid.NewGuid().ToString("N"));
    private CancellationTokenSource? debounce;
    private ICancellableAsyncBuild? build;
    private Task<bool>? reloadTask;
    private bool isBusy;
    private bool autoReload = true;
    private bool needsBuild;
    private bool pending;
    private int revision;
    private int generation;

    public AssemblyHotReloadService(SessionViewModel session, Func<bool> canReload) : base(session.ServiceProvider)
    {
        this.session = session;
        this.canReload = canReload;
        ReloadCommand = new AnonymousTaskCommand(ServiceProvider, async () => await ReloadAsync());
        session.OutputLog.AddLogger(logger);
        var roots = Assemblies().Select(a => a.ProjectReference?.Location.ToString()).OfType<string>()
            .Select(Path.GetDirectoryName).OfType<string>().Where(Directory.Exists).Distinct().ToArray();
        foreach (var root in roots) Watch(root, true);
        foreach (var root in Assemblies().Select(a => Path.GetDirectoryName(a.Path)).OfType<string>().Where(Directory.Exists).Distinct())
            if (!roots.Any(source => IsWithin(root, source))) Watch(root, false);
        UpdateCommand();
    }

    public ICommandBase ReloadCommand { get; }
    public bool IsBusy { get => isBusy; private set { SetValue(ref isBusy, value); UpdateCommand(); } }
    public bool HasPendingChanges { get => pending; private set => SetValue(ref pending, value); }
    public bool AutoReload { get => autoReload; set { SetValue(ref autoReload, value); if (value && HasPendingChanges) Schedule(); } }
    public int ReloadCount { get; private set; }
    public bool? LastReloadSucceeded { get; private set; }
    public event EventHandler? Reloaded;

    private IEnumerable<PackageLoadedAssembly> Assemblies() => session.LocalPackages
        .SelectMany(p => p.PackageContainer.Package.LoadedAssemblies).Where(a => a.Assembly != null).Distinct();

    private static bool IsWithin(string file, string directory) => Path.GetRelativePath(directory, file) is { } relative
        && relative != ".." && !relative.StartsWith(".." + Path.DirectorySeparatorChar) && !Path.IsPathRooted(relative);

    private void Watch(string root, bool recursive)
    {
        var watcher = new FileSystemWatcher(root) { IncludeSubdirectories = recursive,
            NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite | NotifyFilters.Size | NotifyFilters.DirectoryName };
        watcher.Changed += FileChanged; watcher.Created += FileChanged; watcher.Deleted += FileChanged;
        watcher.Renamed += (sender, e) => { FileChanged(sender, e); FileChanged(sender, new FileSystemEventArgs(WatcherChangeTypes.Deleted, Path.GetDirectoryName(e.OldFullPath)!, Path.GetFileName(e.OldFullPath))); };
        watcher.Error += (_, e) => _ = Dispatcher.InvokeAsync(() => logger.Warning($"Hot reload watcher: {e.GetException().Message}. Use Reload assemblies to retry."));
        watchers.Add(watcher);
        watcher.EnableRaisingEvents = true;
    }

    private void FileChanged(object sender, FileSystemEventArgs e)
    {
        if (lifetime.IsCancellationRequested) return;
        var path = e.FullPath;
        var extension = Path.GetExtension(path);
        var source = extension is ".cs" or ".csproj" or ".props" or ".targets";
        if (source && path.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar).Any(part =>
            part.Equals("bin", StringComparison.OrdinalIgnoreCase) || part.Equals("obj", StringComparison.OrdinalIgnoreCase) || part == ".git")) return;
        if (!source && !extension.Equals(".dll", StringComparison.OrdinalIgnoreCase)) return;
        _ = Dispatcher.InvokeAsync(() =>
        {
            if (lifetime.IsCancellationRequested) return;
            if (!source && (IsBusy || !Assemblies().Any(a => Path.GetFullPath(a.Path) == Path.GetFullPath(path)))) return;
            needsBuild |= source;
            revision++;
            HasPendingChanges = true;
            if (AutoReload) Schedule();
        });
    }

    private void Schedule()
    {
        debounce?.Cancel(); debounce?.Dispose();
        debounce = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);
        _ = DelayReload(debounce.Token);
    }
    private async Task DelayReload(CancellationToken token)
    {
        try
        {
            await Task.Delay(600, token);
            await Dispatcher.InvokeTask(async () =>
            {
                if (!token.IsCancellationRequested && AutoReload && canReload() && !IsBusy)
                    await ReloadAsync(needsBuild);
            });
        }
        catch (OperationCanceledException) { }
        catch (Exception exception) { logger.Error("Automatic assembly reload failed.", exception); }
    }

    public Task<bool> ReloadAsync(bool recompile = true)
    {
        Dispatcher.EnsureAccess();
        if (lifetime.IsCancellationRequested || IsBusy || recompile && !canReload()) return Task.FromResult(false);
        IsBusy = true;
        return reloadTask = ReloadCore(recompile);
    }

    private async Task<bool> ReloadCore(bool recompile)
    {
        await Task.Yield();
        var startRevision = revision;
        var success = false;
        logger.HasErrors = false;
        logger.Module = "Hot Reload";
        try
        {
            var assemblies = Assemblies().ToArray();
            if (assemblies.Length == 0) { logger.Info("No loaded game assemblies to reload."); success = true; return true; }
            var paths = assemblies.ToDictionary(a => a, a => a.Path);
            if (recompile)
            {
                foreach (var project in assemblies.Select(a => a.ProjectReference?.Location.ToString()).OfType<string>().Distinct())
                {
                    lifetime.Token.ThrowIfCancellationRequested();
                    logger.Info($"Compiling {project}...");
                    build = await Task.Run(() => VSProjectHelper.CompileProjectAssemblyAsync(project, logger, "Build", "Debug", "AnyCPU"));
                    if (build == null) throw new InvalidOperationException($"Cannot compile {project}.");
                    using var registration = lifetime.Token.Register(build.Cancel);
                    var result = await build.BuildTask;
                    if (result.OverallResult != BuildResultCode.Success || build.IsCanceled || logger.HasErrors)
                    {
                        logger.Error("Compilation failed; the previous assemblies and asset objects were retained.");
                        return false;
                    }
                    foreach (var assembly in assemblies.Where(a => a.ProjectReference?.Location.ToString() == project)) paths[assembly] = build.AssemblyPath;
                    build = null;
                }
            }
            lifetime.Token.ThrowIfCancellationRequested();
            // A binary notification may be a timestamp-only rebuild or our own build completion.
            var changed = false;
            foreach (var assembly in assemblies)
            {
                using var stream = File.Open(paths[assembly], FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                using var pe = new PEReader(stream);
                var metadata = pe.GetMetadataReader();
                changed |= metadata.GetGuid(metadata.GetModuleDefinition().Mvid) != assembly.Assembly.ManifestModule.ModuleVersionId;
            }
            if (!changed) { success = true; return true; }
            await Task.WhenAll(session.ActionService!.TransactionCompletion, session.ActionService.UndoRedoCompletion).WaitAsync(lifetime.Token);
            if (session.ActionService.TransactionInProgress || session.ActionService.UndoRedoInProgress)
                throw new InvalidOperationException("An edit transaction is still active; retry Reload assemblies after finishing the edit.");
            lifetime.Token.ThrowIfCancellationRequested();
            var selection = session.ActiveProperties.Selection?.ToArray() ?? [];
            await session.ActiveProperties.GenerateSelectionPropertiesAsync([]);
            try
            {
                var operation = new AssemblyReloadOperation(session, paths,
                    Path.Combine(cacheDirectory, (++generation).ToString()), logger, NotifyReloaded);
                using (var transaction = session.ActionService.CreateTransaction())
                {
                    operation.Execute();
                    session.ActionService.PushOperation(operation);
                    if (operation.AffectedAssets.Count > 0) session.ActionService.PushOperation(new EmptyDirtyingOperation(operation.AffectedAssets.SelectMany(a => a.Dirtiables)));
                    session.ActionService.SetName(transaction, "Reload game assemblies");
                }
                ReloadCount++;
                OnPropertyChanged(nameof(ReloadCount));
                NotifyReloaded(operation.AffectedAssets);
                logger.Info($"Reloaded {assemblies.Length} game assemblies.");
                success = true;
            }
            finally { await session.ActiveProperties.GenerateSelectionPropertiesAsync(selection); }
            return success;
        }
        catch (OperationCanceledException) { logger.Info("Assembly reload cancelled."); return false; }
        catch (Exception exception) { logger.Error("Assembly reload failed; the previous generation was retained.", exception); return false; }
        finally
        {
            build = null;
            LastReloadSucceeded = success;
            OnPropertyChanged(nameof(LastReloadSucceeded));
            if (success && revision == startRevision) { HasPendingChanges = false; needsBuild = false; }
            IsBusy = false;
            if (!lifetime.IsCancellationRequested && revision != startRevision && AutoReload && HasPendingChanges) Schedule();
        }
    }

    private void NotifyReloaded(IReadOnlyCollection<AssetViewModel> affectedAssets)
    {
        foreach (var asset in session.AllAssets) asset.PropertyGraph?.RefreshBase();
        foreach (var asset in session.AllAssets) asset.PropertyGraph?.ReconcileWithBase();
        _ = session.ActiveProperties.RefreshSelectedPropertiesAsync();
        _ = session.NotifyAssetPropertiesChangedAsync(affectedAssets.ToArray());
        Reloaded?.Invoke(this, EventArgs.Empty);
    }

    public async Task StopAsync()
    {
        lifetime.Cancel(); debounce?.Cancel(); build?.Cancel();
        foreach (var watcher in watchers) watcher.EnableRaisingEvents = false;
        UpdateCommand();
        if (reloadTask != null) await reloadTask;
    }
    private void UpdateCommand() => ReloadCommand.IsEnabled = !lifetime.IsCancellationRequested && !IsBusy && canReload();
    public void RefreshCommand() => UpdateCommand();
    public void ResumePendingChanges()
    {
        if (!lifetime.IsCancellationRequested && AutoReload && HasPendingChanges && !IsBusy && canReload()) Schedule();
    }
    public override void Destroy()
    {
        lifetime.Cancel(); debounce?.Cancel(); build?.Cancel();
        foreach (var watcher in watchers) watcher.Dispose();
        watchers.Clear(); debounce?.Dispose();
        session.OutputLog.RemoveLogger(logger);
        // Assembly.Load(byte[]) and Undo retain generations; keep resolver files for lazy dependencies.
        base.Destroy();
    }
}
