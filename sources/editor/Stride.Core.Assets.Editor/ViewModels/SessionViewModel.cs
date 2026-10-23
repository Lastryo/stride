// Copyright (c) .NET Foundation and Contributors (https://dotnetfoundation.org/ & https://stride3d.net) and Silicon Studio Corp. (https://www.siliconstudio.co.jp)
// Distributed under the MIT license. See the LICENSE.md file in the project root for more information.

using System.Collections.Concurrent;
using System.Collections.Specialized;
using System.Diagnostics;
using System.Runtime.InteropServices;
using Stride.Core.Assets.Analysis;
using Stride.Core.Assets.Editor.Components.Properties;
using Stride.Core.Assets.Editor.Components.Transactions;
using Stride.Core.Assets.Editor.Services;
using Stride.Core.Assets.Presentation.ViewModels;
using Stride.Core.Assets.Quantum;
using Stride.Core.Assets.Templates;
using Stride.Core.Diagnostics;
using Stride.Core.Extensions;
using Stride.Core.IO;
using Stride.Core.Presentation.Collections;
using Stride.Core.Presentation.Commands;
using Stride.Core.Presentation.Dirtiables;
using Stride.Core.Presentation.Quantum.ViewModels;
using Stride.Core.Presentation.Services;
using Stride.Core.Presentation.ViewModels;
using Stride.Core.Translation;

namespace Stride.Core.Assets.Editor.ViewModels;

public sealed partial class SessionViewModel : DispatcherViewModel, ISessionViewModel
{
    public static readonly string StorePackageCategoryName = Tr._("External packages");
    public static readonly string LocalPackageCategoryName = Tr._("Local packages");

    private SessionObjectPropertiesViewModel activeProperties;
    private readonly ConcurrentDictionary<AssetId, AssetViewModel> assetIdMap = [];
    private ProjectViewModel? currentProject;
    private readonly Dictionary<string, PackageCategoryViewModel> packageCategories = [];
    private readonly Dictionary<PackageViewModel, PackageContainer> packageMap = [];
    private readonly PackageSession session;
    private bool sessionStateUpdating;
    private readonly LoggerResult outputLogger = new("Editor");

    private readonly IDebugPage? assetNodesDebugPage;
    private readonly IDebugPage? quantumDebugPage;
    private readonly IDebugPage? undoRedoStackPage;

    private SessionViewModel(IViewModelServiceProvider serviceProvider, PackageSession session, IMainViewModel main, ILogger logger)
        : base(serviceProvider)
    {
        this.session = session;
        Main = main;

        // Make sure plugins are initialized
        PluginService.EnsureInitialized(logger);

        serviceProvider.RegisterService(new UserDocumentationService(this));

        // Initialize the node container used for asset properties
        AssetNodeContainer = new SessionNodeContainer(serviceProvider) { NodeBuilder = { NodeFactory = new AssetNodeFactory() } };

        // Initialize the asset collection view model
        AssetCollection = new AssetCollectionViewModel(this);

        // Initialize the tags view model
        AssetTags = new TagsViewModel(AssetCollection);

        // Initialize the asset collection view model
        EditorCollection = new EditorCollectionViewModel(this);

        // Initialize debug pages
        var debugService = serviceProvider.Get<IEditorDebugService>();
        assetNodesDebugPage = debugService.CreateAssetNodesDebugPage(this, "Asset nodes visualizer");
        quantumDebugPage = debugService.CreateLogDebugPage(GlobalLogger.GetLogger(GraphViewModel.DefaultLoggerName), "Quantum log");
        if (ActionService is { } actionService)
        {
            undoRedoStackPage = debugService.CreateUndoRedoDebugPage(actionService, "Undo/redo stack");
            ActionHistory = new ActionHistoryViewModel(this);
        }

        ActiveProperties = AssetCollection.AssetViewProperties;
        SelectionService?.RegisterSelectionScope(id => GetAssetById(id.AssetId), o =>
        {
            if (o is AssetViewModel asset)
            {
                return new AbsoluteId(asset.Id, Guid.Empty);
            }
            return null;
        }, AssetCollection.SelectedContent);
        // FIXME xplat-editor
        //AssetCollection.SelectedAssets.CollectionChanged += SelectedAssetsCollectionChanged;
        AssetCollection.SelectedContent.CollectionChanged += (s, e) => UpdateSessionState();
        AssetCollection.SelectedLocations.CollectionChanged += (s, e) => UpdateSessionState();

        // Initialize logs
        AssetLog = new AssetLogViewModel(ServiceProvider, this);
        OutputLog = new LoggerViewModel(ServiceProvider, outputLogger);

        // Initialize the reference view model related to the main asset view
        References = new ReferencesViewModel(this);

        // Construct package categories
        var localPackageName = session.SolutionPath != null ? string.Format(Tr._(@"Solution '{0}'"), session.SolutionPath.GetFileNameWithoutExtension()) : LocalPackageCategoryName;
        packageCategories.Add(LocalPackageCategoryName, new PackageCategoryViewModel(localPackageName, this));
        packageCategories.Add(StorePackageCategoryName, new PackageCategoryViewModel(StorePackageCategoryName, this));
        LocalPackages.CollectionChanged += LocalPackagesCollectionChanged;

        // Initialize commands
        EditSelectedContentCommand = new AnonymousCommand(serviceProvider, OnEditSelectedContent);
        NewDirectoryCommand = new AnonymousCommand<object?>(serviceProvider, param => CreateNewFolder(param));
        ExploreCommand = new AnonymousCommand<object?>(serviceProvider, Explore);
        PreviousSelectionCommand = new AnonymousCommand(serviceProvider, () => { SelectionService?.NavigateBackward(); UpdateSelectionCommands(); });
        NextSelectionCommand = new AnonymousCommand(serviceProvider, () => { SelectionService?.NextSelection(); UpdateSelectionCommands(); });

        // This event must be subscribed before we create the package view models
        PackageCategories.ForEach(x => x.Value.Content.CollectionChanged += PackageCollectionChanged);

        // Create package view models
        this.session.Projects.ForEach(x => CreateProjectViewModel(x, true));

        // Initialize other sub view models
        Thumbnails = new ThumbnailsViewModel(this);

        GraphContainer = new AssetPropertyGraphContainer(AssetNodeContainer);
        if (ServiceProvider.TryGet<ICopyPasteService>() is CopyPasteService copyPaste)
            copyPaste.PropertyGraphContainer = GraphContainer;

        // Initialize session itself in plugins
        foreach (var plugin in PluginService.Plugins)
        {
            plugin.InitializeSession(this);
        }
    }

    /// <summary>
    /// Gets the currently active <see cref="SessionObjectPropertiesViewModel"/>.
    /// </summary>
    // FIXME xplat-editor do we need both ActiveProperties and AssetCollection.AssetViewProperties?
    public SessionObjectPropertiesViewModel ActiveProperties
    {
        get { return activeProperties; }
        set
        {
            if (SetValue(ref activeProperties, value))
            {
                // FIXME xplat-editor
                //ActiveAssetsChanged?.Invoke(this, new ActiveAssetsChangedArgs(value?.GetRelatedAssets().ToList()));
            }
        }
    }

    private AssemblyHotReloadService? hotReload;
    public AssemblyHotReloadService? HotReload { get => hotReload; private set => SetValue(ref hotReload, value); }

    public void StartHotReload(Func<bool> canReload) => HotReload ??= new AssemblyHotReloadService(this, canReload);

    public ActionHistoryViewModel? ActionHistory { get; }

    public IUndoRedoService? ActionService => ServiceProvider.TryGet<IUndoRedoService>();

    public IEnumerable<AssetViewModel> AllAssets => AllPackages.SelectMany(x => x.Assets);

    public IEnumerable<PackageViewModel> AllPackages => PackageCategories.Values.SelectMany(x => x.Content);

    public IEnumerable<ProjectViewModel> Projects => LocalPackages.OfType<ProjectViewModel>();

    public AssetCollectionViewModel AssetCollection { get; }

    public TagsViewModel AssetTags { get; }

    public AssetLogViewModel AssetLog { get; }

    public LoggerViewModel OutputLog { get; }

    private void InitializeOutputLog(LoggerResult loadingLog)
    {
        OutputLog.AddLoggerWithPast(loadingLog);
        GlobalLogger.GlobalMessageLogged += outputLogger.Log;
    }

    public AssetNodeContainer AssetNodeContainer { get; }

    /// <summary>
    /// Gets the current active project for build/startup operations.
    /// </summary>
    // TODO: this property should become cancellable to maintain action stack consistency! Undoing a "mark as root" operation after changing the current package wouldn't work.
    public ProjectViewModel? CurrentProject
    {
        get => currentProject;
        set
        {
            if (value != null && !LocalPackages.Contains(value))
                throw new ArgumentException("The current project must belong to this session.", nameof(value));
            var oldValue = currentProject;
            //SetValueUncancellable(ref currentProject, value, () => UpdateCurrentProject(oldValue, value));
            SetValue(ref currentProject, value, () => UpdateCurrentProject(oldValue, value));
        }
    }

    /// <summary>
    /// Gets the dependency manager associated to this session.
    /// </summary>
    public IAssetDependencyManager DependencyManager => session.DependencyManager;

    public EditorCollectionViewModel EditorCollection { get; }

    public AssetPropertyGraphContainer GraphContainer { get; }

    public IObservableCollection<PackageViewModel> LocalPackages => PackageCategories[LocalPackageCategoryName].Content;

    public IMainViewModel Main { get; }

    public IReadOnlyDictionary<string, PackageCategoryViewModel> PackageCategories => packageCategories;

    public ReferencesViewModel References { get; }

    public UFile SolutionPath => session.SolutionPath;

    private ICommandBase? importEffectLogCommand;
    private int importEffectLogPendingCount;

    public ICommandBase? ImportEffectLogCommand
    {
        get => importEffectLogCommand;
        set => SetValue(ref importEffectLogCommand, value);
    }

    public int ImportEffectLogPendingCount
    {
        get => importEffectLogPendingCount;
        set => SetValue(ref importEffectLogPendingCount, value);
    }

    public event EventHandler? Closing;

    public IAssetSourceTrackerViewModel SourceTracker { get; private set; }

    public IObservableCollection<PackageViewModel> StorePackages => PackageCategories[StorePackageCategoryName].Content;

    public ThumbnailsViewModel Thumbnails { get; }

    public ICommandBase EditSelectedContentCommand { get; }

    public ICommandBase NewDirectoryCommand { get; }

    public ICommandBase ExploreCommand { get; }

    public ICommandBase NextSelectionCommand { get; }

    public ICommandBase PreviousSelectionCommand { get; }

    public bool IsInFixupAssetContext { get; private set; }

    public IDisposable CreateAssetFixupContext() => new AssetFixupContext(this);

    private sealed class AssetFixupContext : IDisposable
    {
        private readonly SessionViewModel owner;
        private readonly bool previous;
        private readonly bool previousPropagation;
        public AssetFixupContext(SessionViewModel owner)
        {
            this.owner = owner;
            previous = owner.IsInFixupAssetContext;
            previousPropagation = owner.GraphContainer.PropagateChangesFromBase;
            owner.IsInFixupAssetContext = true;
            owner.GraphContainer.PropagateChangesFromBase = false;
        }
        public void Dispose()
        {
            owner.IsInFixupAssetContext = previous;
            owner.GraphContainer.PropagateChangesFromBase = previousPropagation;
        }
    }

    internal PackageSession PackageSession => session;

    internal IAssetsPluginService PluginService => ServiceProvider.Get<IAssetsPluginService>();

    internal SelectionService? SelectionService => ServiceProvider.TryGet<SelectionService>();

    /// <summary>
    /// Raised when some assets are modified.
    /// </summary>
    public event EventHandler<AssetChangedEventArgs>? AssetPropertiesChanged;

    /// <summary>
    /// Raised when some assets are deleted or undeleted.
    /// </summary>
    public event EventHandler<NotifyCollectionChangedEventArgs?>? DeletedAssetsChanged;

    /// <summary>
    /// Raised when the session state changed (e.g. current package).
    /// </summary>
    public event EventHandler<SessionStateChangedEventArgs>? SessionStateChanged;

    /// <inheritdoc />
    public AssetViewModel? GetAssetById(AssetId id)
    {
        assetIdMap.TryGetValue(id, out var result);
        return result;
    }

    public bool HasUnsavedChanges => session.IsDirty || LocalPackages.Any(package =>
        package.IsDirty || package.Package.IsDirty || package.Package.Assets.IsDirty
        || package.Content.OfType<SessionObjectViewModel>().Any(x => x.IsDirty)
        || package.Assets.Any(asset => asset.IsDirty || asset.AssetItem.IsDirty)
        || package.DeletedAssets.Any(asset => asset.IsDirty));

    public event EventHandler<System.ComponentModel.CancelEventArgs>? Saving;
    public event EventHandler? Saved;

    public bool Save(ILogger logger)
    {
        // Keep graph preparation and serialization on the UI thread to exclude concurrent edits.
        Dispatcher.EnsureAccess();
        var result = new LoggerResult("Save");
        result.MessageLogged += (_, e) =>
        {
            logger.Log(e.Message);
            outputLogger.Log(e.Message);
        };
        try
        {
            var saving = new System.ComponentModel.CancelEventArgs();
            Saving?.Invoke(this, saving);
            if (saving.Cancel) return false;
            foreach (var package in LocalPackages)
            {
                if (package.IsDirty || package.Content.OfType<SessionObjectViewModel>().Any(x => x.IsDirty))
                    package.Package.IsDirty = true;
                if (package.DeletedAssets.Count > 0)
                    package.Package.Assets.IsDirty = true;
                foreach (var asset in package.Assets)
                {
                    // A clean undo snapshot does not guarantee that a newly created asset exists on disk.
                    asset.AssetItem.IsDirty |= asset.IsDirty || !File.Exists(asset.AssetItem.FullPath.ToOSPath());
                    asset.PropertyGraph?.PrepareForSave(result, asset.AssetItem);
                }
            }
            if (result.HasErrors) return false;
            session.Save(result, new PackageSaveParameters { DeleteFile = FileTrash.Move });
            if (result.HasErrors) return false;
            ActionHistory?.NotifySave();
            Saved?.Invoke(this, EventArgs.Empty);
            UpdateSessionState();
            return true;
        }
        catch (Exception exception)
        {
            result.Error("Unable to save the solution.", exception);
            return false;
        }
    }

    public async Task CloseAsync()
    {
        if (HotReload != null) await HotReload.StopAsync();
        await EditorCollection.CloseEditorsAsync();
        if (ServiceProvider.TryGet<IAssetPreviewService>() is { } preview)
            await preview.DisposeAsync();
        // Build cancellation can wait for work that dispatches back to the UI.
        if (ServiceProvider.TryGet<AssetBuilderService>() is { } builder)
            await Task.Run(builder.Dispose);
        Destroy();
    }

    /// <inheritdoc />
    public override void Destroy()
    {
        EnsureNotDestroyed(nameof(SessionViewModel));

        Closing?.Invoke(this, EventArgs.Empty);
        HotReload?.Destroy();
        (SourceTracker as AssetSourceTrackerViewModel)?.Destroy();
        GlobalLogger.GlobalMessageLogged -= outputLogger.Log;
        OutputLog.Destroy();

        EditorCollection.Destroy();
        ServiceProvider.TryGet<IAssetPreviewService>()?.Dispose();
        ActionHistory?.Destroy();
        AssetTags.Destroy();
        AssetLog.Destroy();
        ServiceProvider.TryGet<AssetBuilderService>()?.Dispose();
        Thumbnails.Destroy();

        var debugService = ServiceProvider.Get<IEditorDebugService>();
        debugService.UnregisterDebugPage(undoRedoStackPage);
        debugService.UnregisterDebugPage(assetNodesDebugPage);
        debugService.UnregisterDebugPage(quantumDebugPage);
        // Unregister collection
        SelectionService?.UnregisterSelectionScope(AssetCollection.SelectedContent);

        base.Destroy();
    }

    /// <inheritdoc />
    public Type GetAssetViewModelType(AssetItem assetItem)
    {
        var assetType = assetItem.Asset.GetType();
        return PluginService.GetAssetViewModelType(assetType) ?? typeof(AssetViewModel<>);
    }

    /// <summary>
    /// Notifies the session that a property of some assets has been changed.
    /// </summary>
    /// <remarks>
    /// Since notifications will be raised asynchronously, <paramref name="assets"/> collection should not be modified after it has been passed to this method.
    /// If necessary, caller must provide a copy.
    /// </remarks>
    public async Task NotifyAssetPropertiesChangedAsync(IReadOnlyCollection<AssetViewModel> assets)
    {
        var tasks = assets.Select(x => AssetDependenciesViewModel.NotifyAssetChanged(x.Session, x)).ToList();
        await Task.WhenAll(tasks);
        // We raise this event from a task because it will trigger heavy work on the different subscribers
        await Task.Run(() => AssetPropertiesChanged?.Invoke(this, new AssetChangedEventArgs(assets)));
    }

    /// <inheritdoc />
    public void RegisterAsset(AssetViewModel asset)
    {
        ((IDictionary<AssetId, AssetViewModel>)assetIdMap).Add(asset.Id, asset);
    }

    /// <inheritdoc />
    public void UnregisterAsset(AssetViewModel asset)
    {
        ((IDictionary<AssetId, AssetViewModel>)assetIdMap).Remove(asset.Id);
    }

    public void AutoSelectCurrentProject()
    {
        var host = OperatingSystem.IsWindows() ? PlatformType.Windows : OperatingSystem.IsMacOS() ? PlatformType.macOS : PlatformType.Linux;
        var currentProject = Projects.FirstOrDefault(x => x.Project.Type == ProjectType.Executable && (x.Project.Platform == host || x.Project.Platform == PlatformType.Shared))
            ?? Projects.FirstOrDefault(x => x.Project.Type == ProjectType.Executable) ?? Projects.FirstOrDefault();
        if (currentProject != null)
        {
            SetCurrentProject(currentProject);
        }
    }

    private PackageViewModel CreateProjectViewModel(PackageContainer packageContainer, bool packageAlreadyInSession)
    {
        switch (packageContainer)
        {
            case SolutionProject project:
                {
                    var packageContainerViewModel = new ProjectViewModel(this, project, packageAlreadyInSession);
                    packageMap.Add(packageContainerViewModel, project);
                    if (!packageAlreadyInSession)
                        session.Projects.Add(project);
                    return packageContainerViewModel;
                }
            case StandalonePackage standalonePackage:
                {
                    var packageContainerViewModel = new PackageViewModel(this, standalonePackage, packageAlreadyInSession);
                    packageMap.Add(packageContainerViewModel, standalonePackage);
                    if (!packageAlreadyInSession)
                        session.Projects.Add(standalonePackage);
                    return packageContainerViewModel;
                }
            default:
                throw new ArgumentOutOfRangeException(nameof(packageContainer));
        }
    }

    private void LoadAssetsFromPackages(IProgressViewModel? progressVM, CancellationToken token = default)
    {
        if (progressVM is not null)
        {
            progressVM.Minimum = 0;
            progressVM.ProgressValue = 0;
            progressVM.Maximum = session.Packages.Sum(x => x.Assets.Count);
        }
        double progress = 0.0;

        // Create directory and asset view models for each project
        foreach (var package in AllPackages)
        {
            if (token.IsCancellationRequested)
                return;

            package.LoadPackageInformation(progressVM, ref progress, token);
        }

        // Create actions corresponding to potential upgrades/fixes that occurred during the loading process.
        // FIXME xplat-editor

        SourceTracker = new AssetSourceTrackerViewModel(this);
        UpdateSessionState();

        // This transaction is done to prevent action responding to undoRedoService.TransactionCompletion to occur during loading
        using var transaction = ActionService?.CreateTransaction();
        ProcessAddedPackages(AllPackages).Forget();
    }

    private void LocalPackagesCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        OnPropertyChanged(nameof(Projects));
        if (e.Action == NotifyCollectionChangedAction.Reset)
        {
            session.Projects.RemoveWhere(x => !x.Package.IsSystem);
        }
        if (e.NewItems != null)
        {
            // When a PackageViewModel is built, we will add it before the Package instance is added to the package map.
            // So we can't assume that the view model will always exists in the packageMap.
            packageMap.Where(x => e.NewItems.Cast<PackageViewModel>().Contains(x.Key)).ForEach(x => session.Projects.Add(x.Value));
        }
        e.OldItems?.Cast<PackageViewModel>().Select(x => packageMap[x]).ForEach(x => session.Projects.Remove(x));
    }

    private void PackageCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        e.NewItems?.Cast<PackageViewModel>().ForEach(x => x.DeletedAssets.CollectionChanged += DeletedAssetChanged);
        e.OldItems?.Cast<PackageViewModel>().ForEach(x => x.DeletedAssets.CollectionChanged -= DeletedAssetChanged);
        return;

        void DeletedAssetChanged(object? _, NotifyCollectionChangedEventArgs ev)
        {
            DeletedAssetsChanged?.Invoke(this, ev);
        }
    }

    /// <summary>
    /// Creates view models for projects that were added directly to the underlying session (e.g. by a template generator).
    /// </summary>
    public void ProcessAddedProjects()
    {
        var newPackages = new List<PackageViewModel>();
        var progress = 0.0;
        foreach (var project in session.Projects.Where(x => !packageMap.ContainsValue(x)).ToList())
        {
            var viewModel = CreateProjectViewModel(project, true);
            viewModel.LoadPackageInformation(null, ref progress);
            newPackages.Add(viewModel);
        }
        ProcessAddedPackages(newPackages).Forget();
    }

    /// <summary>
    /// Removes view models of projects that were removed directly from the underlying session.
    /// </summary>
    public void ProcessRemovedProjects()
    {
        foreach (var package in packageMap.Where(x => !session.Projects.Contains(x.Value)).ToList())
        {
            LocalPackages.Remove(package.Key);
            packageMap.Remove(package.Key);
        }
    }

    private async Task ProcessAddedPackages(IEnumerable<PackageViewModel> packages)
    {
        var packageList = packages.ToList();
        // We must refresh asset bases after all packages have been added, because we might have cross-packages references here.
        packageList.SelectMany(x => x.Assets).ForEach(x => x.Initialize());
        await AssetDependenciesViewModel.TriggerInitialReferenceBuild(this);
        await Dispatcher.InvokeAsync(() => packageList.ForEach(x => Thumbnails.StartInitialBuild(x)));
    }

    private void SetCurrentProject(object selectedItem)
    {
        if (selectedItem is not ProjectViewModel project)
        {
            // Editor.MessageBox(Resources.Strings.SessionViewModel.SelectExecutableAsCurrentProject, MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        CurrentProject = project;
        // FIXME xplat-editor
        //SelectionIsRoot = AssetCollection.SelectedAssets.All(x => x.Dependencies.IsRoot);
    }

    private void UpdateCurrentProject(ProjectViewModel? oldValue, ProjectViewModel? newValue)
    {
        AllAssets.ForEach(x => x.Dependencies.NotifyRootAssetChange(false));
        //if (oldValue != null)
        //{
        //    oldValue.IsCurrentProject = false;
        //}
        //if (newValue != null)
        //{
        //    newValue.IsCurrentProject = true;
        //}
        //ToggleIsRootOnSelectedAssetCommand.IsEnabled = CurrentProject != null;
        UpdateSessionState();
    }

    private void UpdateSelectionCommands()
    {
        PreviousSelectionCommand.IsEnabled = SelectionService?.CanGoBack ?? false;
        NextSelectionCommand.IsEnabled = SelectionService?.CanGoForward ?? false;
    }

    private void UpdateSessionState()
    {
        sessionStateUpdating = true;
        Dispatcher.InvokeAsync(DoUpdateSessionState);

        void DoUpdateSessionState()
        {
            if (!sessionStateUpdating)
                return;

            var asset = AssetCollection.SingleSelectedAsset;
            EditSelectedContentCommand.IsEnabled = AssetCollection.SingleSelectedContent is DirectoryViewModel || asset is { IsEditable: true } && PluginService.GetEditorViewModelType(asset.GetType()) is not null;
            UpdateSelectionCommands();

            sessionStateUpdating = false;
            SessionStateChanged?.Invoke(this, new SessionStateChangedEventArgs());
        }
    }

    #region Commands
    private void OnEditSelectedContent()
    {
        // Cannot edit multi-selection
        if (AssetCollection.SingleSelectedContent == null)
            return;

        // Asset
        var asset = AssetCollection.SingleSelectedAsset;
        if (asset != null)
        {
            EditorCollection.OpenAssetEditor(asset);
        }

        // Folder
        if (AssetCollection.SingleSelectedContent is DirectoryViewModel folder)
        {
            AssetCollection.SelectedLocations.Clear();
            AssetCollection.SelectedLocations.Add(folder);
        }
    }

    public IEnumerable<TemplateDescription> FindTemplates(TemplateScope scope)
    {
        return TemplateManager.FindTemplates(scope, session);
    }

    public DirectoryViewModel? CreateNewFolder(object? target = null)
    {
        target ??= AssetCollection.SelectedLocations.FirstOrDefault();
        var directory = target as DirectoryBaseViewModel;
        var package = target as PackageViewModel ?? (target as IChildViewModel)?.GetParent() as PackageViewModel ?? LocalPackages.FirstOrDefault(p => p.IsEditable);
        if (directory == null && package != null)
        {
            directory = package.AssetMountPoint;
        }
        if (directory == null || !directory.Package.IsEditable)
            return null;

        var baseName = "NewFolder";
        var name = NamingHelper.ComputeNewName(baseName, directory.SubDirectories, x => x.Name, "{0}{1}");
        DirectoryViewModel created;
        using (var transaction = ActionService?.CreateTransaction())
        {
            created = new DirectoryViewModel(name, directory);
            directory.Package.Package.ExplicitFolders.Add(created.Path);
            ((IDirtiable)directory.Package).UpdateDirtiness(true);
            ActionService?.PushOperation(new AnonymousDirtyingOperation(directory.Package.Dirtiables,
                () => { directory.Package.Package.ExplicitFolders.Add(created.Path); ((IDirtiable)directory.Package).UpdateDirtiness(true); },
                () => { directory.Package.Package.ExplicitFolders.Remove(created.Path); }));
            ActionService?.SetName(transaction, $"Create folder '{created.Name}' in '{directory.Name}'");
        }
        return created;
    }

    public void Explore(object? param = null)
    {
        param ??= AssetCollection.SelectedAssets.FirstOrDefault() ?? AssetCollection.SelectedLocations.FirstOrDefault();
        string? targetPath = null;
        if (param is AssetViewModel asset)
        {
            targetPath = asset.AssetItem.FullPath;
        }
        else if (param is DirectoryBaseViewModel dir)
        {
            targetPath = Path.Combine(dir.Package.Package.RootDirectory, dir.Path);
        }
        else if (param is ProjectViewModel proj)
        {
            targetPath = proj.ProjectPath.GetFullDirectory();
        }
        else if (param is PackageViewModel pkg)
        {
            targetPath = pkg.PackagePath.GetFullDirectory();
        }
        else if (!string.IsNullOrEmpty(SolutionPath))
        {
            targetPath = SolutionPath.GetFullDirectory();
        }

        if (string.IsNullOrEmpty(targetPath)) return;
        try
        {
            if (File.Exists(targetPath))
            {
                if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                    Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{targetPath}\"") { UseShellExecute = true });
                else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
                    Process.Start("open", $"-R \"{targetPath}\"");
                else
                {
                    var parentDir = Path.GetDirectoryName(targetPath);
                    if (!string.IsNullOrEmpty(parentDir) && Directory.Exists(parentDir))
                        Process.Start("xdg-open", $"\"{parentDir}\"");
                }
            }
            else if (Directory.Exists(targetPath))
            {
                if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                    Process.Start(new ProcessStartInfo("explorer.exe", $"\"{targetPath}\"") { UseShellExecute = true });
                else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
                    Process.Start("open", $"\"{targetPath}\"");
                else
                    Process.Start("xdg-open", $"\"{targetPath}\"");
            }
        }
        catch
        {
            // Ignore explorer process start errors
        }
    }

    public async Task<AssetViewModel?> CreateAssetFromTemplate(TemplateDescription template, DirectoryBaseViewModel? targetDirectory = null, IEnumerable<UFile>? sourceFiles = null)
    {
        targetDirectory ??= AssetCollection.SelectedLocations.OfType<DirectoryBaseViewModel>().FirstOrDefault()
            ?? LocalPackages.FirstOrDefault(p => p.IsEditable)?.AssetMountPoint;
        if (targetDirectory == null || !targetDirectory.Package.IsEditable) return null;

        var templateAsset = template as TemplateAssetDescription;
        if (templateAsset != null && typeof(IProjectAsset).IsAssignableFrom(templateAsset.GetAssetType())
            && targetDirectory.Package is ProjectViewModel project)
            targetDirectory = project.Code;
        var baseName = templateAsset?.DefaultOutputName ?? template.Name;
        var name = NamingHelper.ComputeNewName(baseName, targetDirectory.Assets, x => x.Name, "{0}{1}");

        var logger = new LoggerResult();
        var parameters = new AssetTemplateGeneratorParameters(targetDirectory.Path, sourceFiles)
        {
            Name = name,
            Description = template,
            Package = targetDirectory.Package.Package,
            Logger = logger,
            Namespace = targetDirectory.Package.Package.RootNamespace ?? "MyGame",
        };

        var generator = TemplateManager.FindTemplateGenerator<AssetTemplateGeneratorParameters>(template);
        if (generator == null)
        {
            GlobalLogger.GetLogger("Asset templates").Error($"No generator is registered for '{template.Name}'.");
            return null;
        }

        var prepared = await generator.PrepareForRun(parameters);
        if (!prepared) return null;

        var ran = await Task.Run(() => generator.Run(parameters));
        foreach (var message in logger.Messages) GlobalLogger.GetLogger("Asset templates").Log(message);
        if (!ran || logger.HasErrors) return null;

        var existingAssetItems = targetDirectory.Package.Assets.Select(x => x.AssetItem).ToHashSet();
        var newAssetItems = targetDirectory.Package.Package.Assets.Where(x => !existingAssetItems.Contains(x)).ToList();
        if (newAssetItems.Count == 0) return null;

        AssetViewModel? lastCreated = null;
        using (var transaction = ActionService?.CreateTransaction())
        {
            foreach (var newItem in newAssetItems)
            {
                var directory = newItem.Asset is IProjectAsset && targetDirectory.Package is ProjectViewModel codeProject
                    ? codeProject.GetOrCreateProjectDirectory(newItem.Location.GetParent())
                    : targetDirectory.Package.GetOrCreateAssetDirectory(newItem.Location.GetParent());
                lastCreated = targetDirectory.Package.AddAsset(newItem, directory);
            }
            ActionService?.SetName(transaction, $"Add asset '{name}'");
        }

        if (lastCreated != null)
        {
            AssetCollection.SelectedLocations.Clear();
            AssetCollection.SelectedLocations.Add(targetDirectory);
            AssetCollection.SelectAssetCommand.Execute(lastCreated);
        }

        return lastCreated;
    }
    #endregion // Commands
}
