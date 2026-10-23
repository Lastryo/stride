// Copyright (c) .NET Foundation and Contributors (https://dotnetfoundation.org/ & https://stride3d.net)
// Distributed under the MIT license. See the LICENSE.md file in the project root for more information.

using System.Diagnostics;
using System.Runtime.InteropServices;
using Avalonia;
using Stride.Core;
using Stride.Core.Diagnostics;
using Stride.Core.Assets;
using Stride.Core.Assets.Editor.Components.Status;
using Stride.Core.Assets.Editor.ViewModels;
using Stride.Core.Extensions;
using Stride.Core.IO;
using Stride.Core.Presentation.Commands;
using Stride.Core.Presentation.Services;
using Stride.Core.Presentation.ViewModels;
using Stride.Core.Translation;
using Stride.GameStudio.Avalonia.Services;

namespace Stride.GameStudio.Avalonia.ViewModels;

internal sealed class MainViewModel : ViewModelBase, IMainViewModel
{
    private static readonly string baseTitle = $"Stride Game Studio {StrideVersion.NuGetVersion} ({RuntimeInformation.FrameworkDescription})";
    private SessionViewModel? session;
    private Task<bool>? closingSession;
    private string title = baseTitle;
    private ProjectBuildViewModel? projectBuild;

#if DEBUG
    // Note: only required for the Avalonia designer
    public MainViewModel()
        : this(ViewModelServiceProvider.NullServiceProvider)
    { }
#endif

    public MainViewModel(IViewModelServiceProvider serviceProvider)
        : base(serviceProvider)
    {
        AboutCommand = new AnonymousTaskCommand(serviceProvider, OnAbout, () => DialogService.HasMainWindow);
        CloseCommand = new AnonymousTaskCommand(serviceProvider, OnClose);
#if DEBUG
        CrashCommand = new AnonymousCommand(serviceProvider, () => throw new Exception("Boom!"));
#else
        CrashCommand = DisabledCommand.Instance;
#endif
        ExitCommand = new AnonymousCommand(serviceProvider, OnExit, () => DialogService.HasMainWindow);
        OpenCommand = new AnonymousTaskCommand<UFile?>(serviceProvider, OnOpen);
        SaveCommand = new AnonymousTaskCommand(serviceProvider, OnSave) { IsEnabled = false };
        OpenDebugWindowCommand = new AnonymousTaskCommand(serviceProvider, OnOpenDebugWindow, () => DialogService.HasMainWindow);
        OpenSettingsWindowCommand = new AnonymousTaskCommand(serviceProvider, OnOpenSettingsWindow, () => DialogService.HasMainWindow);
        OpenWebPageCommand = new AnonymousTaskCommand<string>(serviceProvider, OnOpenWebPage);
        UpdatePlatformsCommand = new AnonymousTaskCommand(serviceProvider, OnUpdatePlatforms, CanUpdatePlatforms);

        Status = new StatusViewModel(ServiceProvider);
        Status.PushStatus("Ready");
        Panels = new EditionPanelViewModel(ServiceProvider);
    }

    public event EventHandler? SessionClosing;

    public EditionPanelViewModel Panels { get; }

    public SessionViewModel? Session
    {
        get => session;
        set => SetValue(ref session, value, () =>
        {
            ProjectBuild?.Destroy();
            ProjectBuild = value == null ? null : new ProjectBuildViewModel(value, SaveSessionAsync);
            SaveCommand.IsEnabled = value != null;
            UpdatePlatformsCommand.IsEnabled = CanUpdatePlatforms();
        });
    }

    public ProjectBuildViewModel? ProjectBuild { get => projectBuild; private set => SetValue(ref projectBuild, value); }

    public StatusViewModel Status { get; }

    public string Title
    {
        get => title;
        set => SetValue(ref title, value);
    }

    public ICommandBase AboutCommand { get; }

    public ICommandBase CloseCommand { get; }

    public ICommandBase CrashCommand { get; }

    public ICommandBase ExitCommand { get; }

    public ICommandBase OpenCommand { get; }

    public ICommandBase SaveCommand { get; }

    public ICommandBase OpenDebugWindowCommand { get; }

    public ICommandBase OpenSettingsWindowCommand { get; }

    public ICommandBase OpenWebPageCommand { get; }

    public ICommandBase UpdatePlatformsCommand { get; }

    private EditorDialogService DialogService => ServiceProvider.Get<EditorDialogService>();

    public async Task<bool?> OpenSession(UFile? filePath, CancellationToken token = default)
    {
        if (closingSession != null) return false;
        if (filePath == null || !File.Exists(filePath))
        {
            filePath = await DialogService.OpenFilePickerAsync();
        }

        if (filePath == null || closingSession != null) return false;

        // We have a session, let's restart cleanly
        if (session is not null)
        {
            if (!await CloseSessionAsync()) return false;
            (Application.Current as App)?.Restart(filePath);
            return true;
        }

        var sessionResult = new PackageSessionResult();
        sessionResult.MessageLogged += (_, e) =>
        {
            if (e.Message.Type >= Stride.Core.Diagnostics.LogMessageType.Warning)
                Console.Error.WriteLine(e.Message);
        };
        var loadedSession = await SessionViewModel.OpenSessionAsync(filePath, sessionResult, this, ServiceProvider, token);

        // Loading has failed
        if (loadedSession == null)
        {
            if (sessionResult.OperationCancelled)
            {
                // The cancelled session might have registered plugins or services, let's restart cleanly
                (Application.Current as App)?.Restart();

                // Null means the user has cancelled the loading operation.
                return null;
            }
            return false;
        }

        Session = loadedSession;
        Title = $"{baseTitle} - {Session.SolutionPath.GetFileNameWithoutExtension()}";
        return true;
    }

    private async Task OnAbout()
    {
        await DialogService.ShowAboutWindowAsync();
    }

    public async Task<bool> SaveSessionAsync()
    {
        if (Session is null || !SaveCommand.IsEnabled) return false;
        SaveCommand.IsEnabled = false;
        var status = Status.PushStatus("Saving solution...");
        try
        {
            EditorDialogService.MainWindow?.FocusManager?.Focus(null);
            var result = new LoggerResult();
            var success = Session.Save(result);
            if (!success)
                await ServiceProvider.Get<IDialogService>().MessageBoxAsync(
                    "Unable to save the solution.\n" + string.Join("\n", result.Messages), MessageBoxButton.OK, MessageBoxImage.Error);
            return success;
        }
        finally
        {
            Status.PopStatus(status);
            SaveCommand.IsEnabled = Session != null;
        }
    }

    private async Task OnSave() => await SaveSessionAsync();

    private async Task OnClose()
    {
        if (closingSession != null) return;
        // We have a session, let's restart empty
        if (session is not null && Application.Current is App app)
        {
            if (await CloseSessionAsync()) app.Restart();
        }
    }

    public Task<bool> CloseSessionAsync() => closingSession ??= CloseSessionCore();

    private async Task<bool> CloseSessionCore()
    {
        // Assign the shared task before a synchronous completion or a reentrant close request.
        await Task.Yield();
        OpenCommand.IsEnabled = CloseCommand.IsEnabled = false;
        try
        {
            if (ProjectBuild != null)
            {
                ProjectBuild.IsClosing = true;
                await ProjectBuild.CancelAndWaitAsync();
            }
            EditorDialogService.MainWindow?.FocusManager?.Focus(null);
            if (Session?.HasUnsavedChanges == true)
            {
                var save = IDialogService.ButtonYes;
                save.Content = Tr._("Save");
                var discard = IDialogService.ButtonNo;
                discard.Content = Tr._("Don't save");
                var result = await ServiceProvider.Get<IDialogService>().MessageBoxAsync(
                    Tr._("Save changes to the current solution before closing?"),
                    [save, discard, IDialogService.ButtonCancel], MessageBoxImage.Question);
                if (result == (int)MessageBoxResult.Yes)
                {
                    if (!await SaveSessionAsync()) return false;
                }
                else if (result != (int)MessageBoxResult.No) return false;
            }
            if (Session is not null)
            {
                SessionClosing?.Invoke(this, EventArgs.Empty);
                await Session.CloseAsync();
                Session = null;
            }
            return true;
        }
        finally
        {
            closingSession = null;
            if (ProjectBuild != null) ProjectBuild.IsClosing = false;
            OpenCommand.IsEnabled = CloseCommand.IsEnabled = true;
        }
    }

    private void OnExit()
    {
        DialogService.Exit();
    }

    private async Task OnOpen(UFile? initialPath)
    {
        await OpenSession(initialPath);
    }

    private async Task OnOpenDebugWindow()
    {
        await DialogService.ShowDebugWindowAsync();
    }

    private async Task OnOpenSettingsWindow()
    {
        await DialogService.ShowSettingsWindowAsync();
    }

    private async Task OnOpenWebPage(string url)
    {
        try
        {
            var process = new Process { StartInfo = new ProcessStartInfo(url) { UseShellExecute = true } };
            process.Start();
        }
        catch (Exception ex)
        {
            var message = $"{Tr._p("Message", "An error occurred while opening the file.")}{ex.FormatSummary(true)}";
            await ServiceProvider.Get<IDialogService>().MessageBoxAsync(message, MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private bool CanUpdatePlatforms() => GetSharedGameProject() != null;

    private SolutionProject? GetSharedGameProject()
    {
        return (Session?.CurrentProject?.Project as SolutionProject)
            ?? Session?.Projects.Select(p => p.Project as SolutionProject).FirstOrDefault(p => p != null && PlatformsUpdater.CanUpdate(p));
    }

    private async Task OnUpdatePlatforms()
    {
        var gameProject = GetSharedGameProject();
        if (gameProject == null || !PlatformsUpdater.CanUpdate(gameProject)) return;

        var existing = PlatformsUpdater.GetExistingPlatforms(gameProject);
        var vm = new UpdatePlatformsViewModel(ServiceProvider, existing);
        var result = await DialogService.ShowUpdatePlatformsWindowAsync(vm);
        if (result != true) return;

        var selected = vm.SelectedPlatforms.ToList();
        if (selected.Count == 0)
        {
            await ServiceProvider.Get<IDialogService>().MessageBoxAsync("You must select at least one platform.", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var status = Status.PushStatus("Updating platforms...");
        var logger = new LoggerResult();
        var workProgress = new WorkProgressViewModel(ServiceProvider, logger)
        {
            Title = "Updating platforms...",
            IsIndeterminate = true,
            KeepOpen = KeepOpen.Never,
        };
        workProgress.RegisterProgressStatus(logger);
        DialogService.ShowProgressWindow(workProgress);

        try
        {
            var success = await Task.Run(() => PlatformsUpdater.Run(gameProject, selected, vm.ForcePlatformRegeneration, logger));
            await workProgress.NotifyWorkFinished(false, !success || logger.HasErrors);

            if (success)
            {
                Session?.ProcessAddedProjects();
                Session?.ProcessRemovedProjects();
                Session?.AutoSelectCurrentProject();
                await SaveSessionAsync();
            }
            else
            {
                var errors = string.Join("\n", logger.Messages.Where(m => m.Type >= LogMessageType.Warning).Select(m => m.Text));
                await ServiceProvider.Get<IDialogService>().MessageBoxAsync("Failed to update platforms:\n" + errors, MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
        catch (Exception ex)
        {
            await workProgress.NotifyWorkFinished(false, true);
            await ServiceProvider.Get<IDialogService>().MessageBoxAsync("An error occurred while updating platforms:\n" + ex.Message, MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            Status.PopStatus(status);
        }
    }
}
