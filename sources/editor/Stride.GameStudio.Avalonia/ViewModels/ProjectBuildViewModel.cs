// Copyright (c) .NET Foundation and Contributors (https://dotnetfoundation.org/ & https://stride3d.net)
// Distributed under the MIT license. See the LICENSE.md file in the project root for more information.

using System.ComponentModel;
using System.Diagnostics;
using Microsoft.Build.Execution;
using Stride.Core;
using Stride.Core.Assets;
using Stride.Core.Assets.Editor.Components.Status;
using Stride.Core.Assets.Editor.ViewModels;
using Stride.Core.Assets.Presentation.ViewModels;
using Stride.Core.Diagnostics;
using Stride.Core.Presentation.Commands;
using Stride.Core.Presentation.ViewModels;

namespace Stride.GameStudio.Avalonia.ViewModels;

internal sealed class ProjectBuildViewModel : DispatcherViewModel
{
    private readonly SessionViewModel session;
    private readonly Func<Task<bool>> save;
    private readonly LoggerResult logger = new("Build");
    private ICancellableAsyncBuild? currentBuild;
    private Task<bool>? buildTask;
    private bool isBusy;
    private bool isClosing;
    private bool cancelRequested;
    private int? resultStatusToken;

    public ProjectBuildViewModel(SessionViewModel session, Func<Task<bool>> save)
        : base(session.ServiceProvider)
    {
        this.session = session;
        this.save = save;
        BuildProjectCommand = new AnonymousTaskCommand(ServiceProvider, async () => await BuildAsync(false));
        RunProjectCommand = new AnonymousTaskCommand(ServiceProvider, async () => await BuildAsync(true));
        CancelBuildCommand = new AnonymousCommand(ServiceProvider, Cancel);
        session.OutputLog.AddLogger(logger);
        session.PropertyChanged += SessionChanged;
        session.StartHotReload(() => !IsBusy && !IsClosing);
        session.HotReload!.PropertyChanged += HotReloadChanged;
        UpdateCommands();
    }

    public ICommandBase BuildProjectCommand { get; }
    public ICommandBase RunProjectCommand { get; }
    public ICommandBase CancelBuildCommand { get; }
    public bool IsBusy { get => isBusy; private set => SetValue(ref isBusy, value, UpdateCommands); }
    public bool IsClosing { get => isClosing; set => SetValue(ref isClosing, value, UpdateCommands); }
    public bool CanSelectProject => !IsDestroyed && !IsBusy && !IsClosing && session.HotReload?.IsBusy != true;
    public string? LastBuiltAssemblyPath { get; private set; }
    public int? LastStartedProcessId { get; private set; }

    public Task<bool> BuildAsync(bool run)
    {
        Dispatcher.EnsureAccess();
        var project = session.CurrentProject;
        if (IsDestroyed || IsClosing || IsBusy || session.HotReload?.IsBusy == true || project == null || !session.Projects.Contains(project) || run && !CanRun(project))
            return Task.FromResult(false);
        cancelRequested = false;
        IsBusy = true;
        return buildTask = BuildCore(project, run);
    }

    public async Task CancelAndWaitAsync()
    {
        if (!IsBusy) return;
        Cancel();
        if (buildTask != null) await buildTask;
    }

    public override void Destroy()
    {
        Cancel();
        session.PropertyChanged -= SessionChanged;
        if (session.HotReload != null) session.HotReload.PropertyChanged -= HotReloadChanged;
        session.OutputLog.RemoveLogger(logger);
        if (resultStatusToken is { } token) session.Main.Status.PopStatus(token);
        base.Destroy();
        UpdateCommands();
    }

    private async Task<bool> BuildCore(ProjectViewModel project, bool run)
    {
        // Publish the shared task before saving can complete synchronously.
        await Task.Yield();
        var status = session.Main.Status;
        var job = status.NotifyBackgroundJobStarted($"Building {project.Name}...", JobPriority.Compile);
        var success = false;
        logger.Clear();
        logger.HasErrors = false;
        logger.Module = "Build";
        if (resultStatusToken is { } previous) status.PopStatus(previous);
        resultStatusToken = null;
        try
        {
            logger.Info($"Building {project.ProjectPath} (Debug / AnyCPU).");
            if (!await save() || cancelRequested) return false;

            var projectDir = Path.GetDirectoryName(project.ProjectPath)!;
            var assetsFile = Path.Combine(projectDir, "obj", "project.assets.json");
            if (!File.Exists(assetsFile))
            {
                logger.Info($"Restoring packages for {project.Name}...");
                var restoreJob = status.NotifyBackgroundJobStarted($"Restoring {project.Name}...", JobPriority.Compile);
                try { await Task.Run(() => PlatformsUpdater.RestoreProject(project.ProjectPath, logger)); }
                finally { status.NotifyBackgroundJobFinished(restoreJob); }
            }

            var properties = new Dictionary<string, string>
            {
                ["StrideBuildEngineLogVerbose"] = "true",
                ["SolutionPlatform"] = "Any CPU",
            };
            if (project.Project.Platform != PlatformType.Shared)
            {
                properties["StridePlatform"] = project.Project.Platform.ToString();
                if (project.Project.Platform == PlatformType.Linux)
                {
                    properties["StrideGraphicsApi"] = "Vulkan";
                }
            }
            if (!string.IsNullOrEmpty(session.SolutionPath))
            {
                var solution = Path.GetFullPath(session.SolutionPath);
                properties["SolutionPath"] = solution;
                properties["SolutionDir"] = Path.GetDirectoryName(solution) + Path.DirectorySeparatorChar;
            }
            currentBuild = await Task.Run(() => VSProjectHelper.CompileProjectAssemblyAsync(project.ProjectPath,
                logger, "Build", "Debug", "AnyCPU", properties));
            if (currentBuild == null)
            {
                logger.Error($"Unable to load and compile {project.ProjectPath}.");
                return false;
            }
            if (cancelRequested) currentBuild.Cancel();
            var result = await currentBuild.BuildTask;
            if (result.Exception != null) logger.Error("Build failed.", result.Exception);
            if (cancelRequested || currentBuild.IsCanceled || result.OverallResult != BuildResultCode.Success || logger.HasErrors)
                return false;
            logger.Module = "Build";
            LastBuiltAssemblyPath = currentBuild.AssemblyPath;
            if (session.HotReload != null) await session.HotReload.ReloadAsync(false);
            if (run)
            {
                var assembly = Path.GetFullPath(currentBuild.AssemblyPath);
                if (!File.Exists(assembly))
                {
                    var dir = Path.GetDirectoryName(assembly)!;
                    var filename = Path.GetFileName(assembly);
                    var rid = OperatingSystem.IsWindows() ? "win-x64" : OperatingSystem.IsMacOS() ? "osx-x64" : "linux-x64";
                    var ridCandidate = Path.Combine(dir, rid, filename);
                    if (File.Exists(ridCandidate)) assembly = ridCandidate;
                }
                if (!File.Exists(assembly)) throw new FileNotFoundException("Build output was not found.", assembly);
                var start = new ProcessStartInfo { WorkingDirectory = Path.GetDirectoryName(assembly)!, UseShellExecute = false };
                if (Path.GetExtension(assembly).Equals(".dll", StringComparison.OrdinalIgnoreCase))
                {
                    var apphost = Path.ChangeExtension(assembly, OperatingSystem.IsWindows() ? ".exe" : null);
                    if (File.Exists(apphost))
                    {
                        if (!OperatingSystem.IsWindows())
                        {
                            try { File.SetUnixFileMode(apphost, File.GetUnixFileMode(apphost) | UnixFileMode.UserExecute | UnixFileMode.GroupExecute | UnixFileMode.OtherExecute); }
                            catch { /* Best effort */ }
                        }
                        start.FileName = apphost;
                    }
                    else
                    {
                        if (!File.Exists(Path.ChangeExtension(assembly, ".runtimeconfig.json")))
                            throw new InvalidOperationException("The selected project has no executable .NET runtime configuration.");
                        start.FileName = "dotnet";
                        start.ArgumentList.Add(assembly);
                    }
                }
                else start.FileName = assembly;
                using var process = Process.Start(start) ?? throw new InvalidOperationException("Unable to start the game process.");
                LastStartedProcessId = process.Id;
                logger.Info($"Started {assembly} (PID {process.Id}).");
            }
            success = true;
            return true;
        }
        catch (Exception exception)
        {
            logger.Module = "Build";
            logger.Error("Unable to build or run the project.", exception);
            return false;
        }
        finally
        {
            currentBuild = null;
            var message = cancelRequested ? "Build cancelled" : success ? "Build successful" : "Build failed";
            logger.Module = "Build";
            logger.Info(message);
            status.NotifyBackgroundJobFinished(job);
            resultStatusToken = status.PushStatus(message);
            IsBusy = false;
            session.HotReload?.ResumePendingChanges();
        }
    }

    private void Cancel()
    {
        if (!IsBusy || cancelRequested) return;
        cancelRequested = true;
        currentBuild?.Cancel();
        logger.Info("Cancelling build...");
        UpdateCommands();
    }

    private static bool CanRun(ProjectViewModel project)
    {
        var host = OperatingSystem.IsWindows() ? PlatformType.Windows : OperatingSystem.IsMacOS() ? PlatformType.macOS : PlatformType.Linux;
        return project.Project.Type == ProjectType.Executable
            && (project.Project.Platform == PlatformType.Shared || project.Project.Platform == host);
    }

    private void HotReloadChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(Stride.Core.Assets.Editor.Services.AssemblyHotReloadService.IsBusy)) UpdateCommands();
    }

    private void SessionChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(SessionViewModel.CurrentProject) or nameof(SessionViewModel.Projects)) UpdateCommands();
    }

    private void UpdateCommands()
    {
        session.HotReload?.RefreshCommand();
        var project = session.CurrentProject;
        BuildProjectCommand.IsEnabled = CanSelectProject && project != null && session.Projects.Contains(project);
        RunProjectCommand.IsEnabled = BuildProjectCommand.IsEnabled && CanRun(project!);
        CancelBuildCommand.IsEnabled = !IsDestroyed && IsBusy && !cancelRequested;
        OnPropertyChanged(nameof(CanSelectProject));
    }
}
