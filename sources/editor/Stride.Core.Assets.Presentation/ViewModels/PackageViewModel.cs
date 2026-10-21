// Copyright (c) .NET Foundation and Contributors (https://dotnetfoundation.org/ & https://stride3d.net)
// Distributed under the MIT license. See the LICENSE.md file in the project root for more information.

using System.Collections.Specialized;
using Stride.Core.Diagnostics;
using Stride.Core.Extensions;
using Stride.Core.IO;
using Stride.Core.Presentation.Collections;
using Stride.Core.Presentation.ViewModels;
using static Stride.Core.Assets.Presentation.ViewModels.AssetViewModel;

namespace Stride.Core.Assets.Presentation.ViewModels;

public class PackageViewModel : SessionObjectViewModel, IComparable<PackageViewModel>, IChildViewModel
{
    // FIXME should only contain editable viewmodels
    protected readonly SortedObservableCollection<ViewModelBase> content = new(ComparePackageContent);

    public PackageViewModel(ISessionViewModel session, PackageContainer packageContainer, bool packageAlreadyInSession)
        : base(session)
    {
        AssetMountPoint = new AssetMountPointViewModel(this);
        PackageContainer = packageContainer;

        content.Add(AssetMountPoint);
        Dependencies = new DependencyCategoryViewModel(nameof(Dependencies), this, session, Package.RootAssets);
        content.Add(Dependencies);
        foreach (var dependency in PackageContainer.DirectDependencies.ToArray())
            _ = new DirectDependencyReferenceViewModel(dependency, this, Dependencies, false);
        IsLoaded = Package.State >= PackageState.AssetsReady;

        // IsDeleted will make the package added to Session.LocalPackages, so let's do it last
        InitialUndelete(!packageAlreadyInSession);
    }

    /// <summary>
    /// Gets the root directory of this package.
    /// </summary>
    public AssetMountPointViewModel AssetMountPoint { get; }

    public DependencyCategoryViewModel Dependencies { get; }

    /// <summary>
    /// Gets all assets contained in this package.
    /// </summary>
    public IEnumerable<AssetViewModel> Assets => MountPoints.SelectMany(x => x.GetDirectoryHierarchy().SelectMany(y => y.Assets));

    /// <summary>
    /// Gets the list of child item to be used to display in a hierachical view.
    /// </summary>
    /// <remarks>This collection usually contains categories and root folders.</remarks>
    public IReadOnlyObservableCollection<ViewModelBase> Content => content;

    /// <summary>
    /// Gets the list of assets that have been deleted by the user since the beginning of the session.
    /// </summary>
    public IReadOnlyObservableList<AssetViewModel> DeletedAssets => DeletedAssetsInternal;

    /// <summary>
    /// Gets whether this package is editable.
    /// </summary>
    public override bool IsEditable => !Package.IsSystem && IsLoaded;

    public IEnumerable<MountPointViewModel> MountPoints => Content.OfType<MountPointViewModel>();

    internal ObservableList<AssetViewModel> DeletedAssetsInternal { get; } = new ObservableList<AssetViewModel>();

    /// <summary>
    /// Gets or sets the name of this package.
    /// </summary>
    /// <remarks>Modifying this property also modify the <see cref="PackagePath"/> property if the package has already been saved once.</remarks>
    public override string Name
    {
        get => PackagePath.GetFileNameWithoutExtension() ?? string.Empty;
        set { } // TODO rename
    }

    public bool IsLoaded { get; }

    /// <summary>
    /// Gets the underlying <see cref="Package"/> used as a model for this view.
    /// </summary>
    public Package Package => PackageContainer.Package;

    public PackageContainer PackageContainer { get; }

    /// <summary>
    /// Gets or sets the path of this package.
    /// </summary>
    /// <remarks>Modifying this property also modify the <see cref="Name"/> property.</remarks>
    public UFile PackagePath
    {
        get => Package.FullPath;
        set => SetValue(() => Package.FullPath = value);
    }

    /// <summary>
    /// Gets the collection of root assets for this package.
    /// </summary>
    public ObservableSet<AssetViewModel> RootAssets { get; } = new ObservableSet<AssetViewModel>();

    public UDirectory RootDirectory => Package.RootDirectory;

    /// <inheritdoc/>
    public override string TypeDisplayName => "Package";

    /// <inheritdoc/>
    public int CompareTo(PackageViewModel? other)
    {
        return other != null ? string.Compare(Name, other.Name, StringComparison.InvariantCultureIgnoreCase) : -1;
    }

    public DirectoryBaseViewModel GetOrCreateAssetDirectory(string assetDirectory)
    {
        return AssetMountPoint.GetOrCreateDirectory(assetDirectory);
    }

    /// <summary>
    /// Creates the view models for each asset, directory, profile, project and reference of this package.
    /// </summary>
    /// <param name="token">A cancellation token to cancel the load process. Can be <c>null</c>.</param>
    public void LoadPackageInformation(IProgressViewModel? progressVM, ref double progress, CancellationToken token = default)
    {
        if (token.IsCancellationRequested)
            return;

        foreach (var asset in Package.Assets.ToList())
        {
            if (token.IsCancellationRequested)
                return;

            progressVM?.UpdateProgress($"Processing asset {progress + 1}/{progressVM.Maximum}...", progress);

            var url = asset.Location;
            DirectoryBaseViewModel directory;
            // TODO CSPROJ=XKPKG override rather than cast to subclass
            if (asset.Asset is IProjectAsset && this is ProjectViewModel project)
            {
                directory = project.GetOrCreateProjectDirectory(url.GetFullDirectory());
            }
            else
            {
                directory = GetOrCreateAssetDirectory(url.GetFullDirectory());
            }
            var assetViewModel = CreateAsset(asset, directory);
            directory.AddAsset(assetViewModel);

            progress++;
        }

        FillRootAssetCollection();

        foreach (var explicitDirectory in Package.ExplicitFolders)
        {
            GetOrCreateAssetDirectory(explicitDirectory);
        }
    }

    /// <summary>
    /// Indicates whether the given asset in within the scope of this package, either by being part of this package or part of
    /// one of its dependencies.
    /// </summary>
    /// <param name="asset">The asset for which to check if it's in the scope of this package</param>
    /// <returns><c>True</c> if the asset is in scope, <c>False</c> otherwise.</returns>
    public bool IsInScope(AssetViewModel asset)
    {
        var assetPackage = asset.Directory.Package;
        // Note: Would be better to switch to Dependencies view model as soon as we have FlattenedDependencies in those
        return assetPackage == this || Package.Container.FlattenedDependencies.Any(x => x.Package == assetPackage.Package);
    }

    protected override void UpdateIsDeletedStatus()
    {
        var collection = Package.IsSystem ? Session.StorePackages : Session.LocalPackages;

        if (IsDeleted)
        {
            collection.Remove(this);
        }
        else
        {
            collection.Add(this);
        }
    }

    private static int ComparePackageContent(ViewModelBase x, ViewModelBase y)
    {
        var order = ContentOrder(x).CompareTo(ContentOrder(y));
        return order != 0 ? order : string.Compare(((SessionObjectViewModel)x).Name, ((SessionObjectViewModel)y).Name, StringComparison.InvariantCultureIgnoreCase);

        static int ContentOrder(ViewModelBase item) => item switch
        {
            AssetMountPointViewModel => 0,
            ProjectCodeViewModel => 1,
            ProjectViewModel => 2,
            DependencyCategoryViewModel => 3,
            _ => throw new InvalidOperationException("Unable to sort the given items for the Content collection of PackageViewModel"),
        };
    }

    public AssetViewModel AddAsset(AssetItem assetItem, DirectoryBaseViewModel directory)
    {
        if (directory.Package != this || !IsEditable) throw new InvalidOperationException("Assets can only be added to an editable directory of this package.");
        var asset = CreateAsset(assetItem, directory, canUndoRedoCreation: true);
        directory.AddAsset(asset);
        asset.Initialize();
        asset.AssetItem.IsDirty = true;
        return asset;
    }

    private AssetViewModel CreateAsset(AssetItem assetItem, DirectoryBaseViewModel directory, ILogger? logger = null, bool canUndoRedoCreation = false)
    {
        AssetCollectionItemIdHelper.GenerateMissingItemIds(assetItem.Asset);
        Session.GraphContainer.InitializeAsset(assetItem, logger);
        var assetViewModelType = Session.GetAssetViewModelType(assetItem);
        if (assetViewModelType.IsGenericType)
        {
            assetViewModelType = assetViewModelType.MakeGenericType(assetItem.Asset.GetType());
        }
        return (AssetViewModel)Activator.CreateInstance(assetViewModelType, new ConstructorParameters(assetItem, directory, canUndoRedoCreation))!;
    }

    private void FillRootAssetCollection()
    {
        RootAssets.Clear();
        RootAssets.AddRange(Package.RootAssets.Select(x => Session.GetAssetById(x.Id)).NotNull()!);
        foreach (var dependency in PackageContainer.FlattenedDependencies)
        {
            if (dependency.Package != null)
                RootAssets.AddRange(dependency.Package.RootAssets.Select(x => Session.GetAssetById(x.Id)).NotNull()!);
        }
        RegisterMemberCollectionForActionStack(nameof(RootAssets), RootAssets);
        RootAssets.CollectionChanged += RootAssetsCollectionChanged;
    }

    private void RootAssetsCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        switch (e.Action)
        {
            case NotifyCollectionChangedAction.Add:
                foreach (var asset in e.NewItems!.Cast<AssetViewModel>())
                {
                    Package.RootAssets.Remove(asset.Id);
                    Package.RootAssets.Add(new AssetReference(asset.Id, asset.Url));
                    asset.Dependencies.NotifyRootAssetChange(true);
                }
                break;
            case NotifyCollectionChangedAction.Remove:
                foreach (var asset in e.OldItems!.Cast<AssetViewModel>())
                {
                    Package.RootAssets.Remove(asset.Id);
                    asset.Dependencies.NotifyRootAssetChange(true);
                }
                break;
            default:
                Package.RootAssets.Clear();
                Package.RootAssets.AddRange(RootAssets.Select(asset => new AssetReference(asset.Id, asset.Url)));
                foreach (var asset in Session.AllAssets) asset.Dependencies.NotifyRootAssetChange(false);
                break;
        }
        Package.IsDirty = true;
    }

    IChildViewModel IChildViewModel.GetParent()
    {
        return Session.PackageCategories.Values.First(x => x.Content.Contains(this));
    }

    string IChildViewModel.GetName()
    {
        return Name;
    }
}
