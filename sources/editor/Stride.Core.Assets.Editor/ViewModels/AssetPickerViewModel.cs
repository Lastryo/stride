// Copyright (c) .NET Foundation and Contributors (https://dotnetfoundation.org/ & https://stride3d.net)
// Distributed under the MIT license. See the LICENSE.md file in the project root for more information.

using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using Stride.Core.Assets.Presentation.ViewModels;
using Stride.Core.Presentation.Collections;
using Stride.Core.Presentation.ViewModels;

namespace Stride.Core.Assets.Editor.ViewModels;

public sealed record AssetPickerTypeFilter(Type? Type, string Name);

/// <summary>A picker owns its browsing and selection state independently of the main asset view.</summary>
public sealed class AssetPickerViewModel : DispatcherViewModel
{
    private readonly HashSet<INotifyCollectionChanged> observedCollections = [];
    private readonly HashSet<AssetViewModel> observedAssets = [];
    private object? selectedLocation;
    private string searchText = string.Empty;
    private bool includeSubfolders = true;
    private AssetPickerTypeFilter? selectedType;
    private bool initialized;
    private bool refreshing;
    private bool refreshQueued;

    public AssetPickerViewModel(SessionViewModel session) : base(session.ServiceProvider)
    {
        Session = session;
        SelectedAssets.CollectionChanged += SelectionChanged;
    }

    public SessionViewModel Session { get; }
    // Configure these before showing the picker. An empty accepted-type list permits all asset types.
    public List<Type> AcceptedTypes { get; } = [];
    public Func<AssetViewModel, bool>? Filter { get; set; }
    public AssetViewModel? InitialAsset { get; set; }
    public DirectoryBaseViewModel? InitialLocation { get; set; }
    public bool AllowMultiSelection { get; set; }
    public string Message { get; set; } = "Select an asset.";
    public ObservableList<AssetViewModel> Assets { get; } = [];
    public ObservableList<AssetPickerTypeFilter> AvailableTypes { get; } = [];
    public ObservableCollection<AssetViewModel> SelectedAssets { get; } = [];
    public object? SelectedLocation { get => selectedLocation; set => SetValue(ref selectedLocation, value, Refresh); }
    public string SearchText { get => searchText; set => SetValue(ref searchText, value ?? string.Empty, Refresh); }
    public bool IncludeSubfolders { get => includeSubfolders; set => SetValue(ref includeSubfolders, value, Refresh); }
    public AssetPickerTypeFilter? SelectedType { get => selectedType; set => SetValue(ref selectedType, value, Refresh); }
    public bool HasAssets => Assets.Count > 0;
    public bool CanAccept => SelectedAssets.Count > 0 && (AllowMultiSelection || SelectedAssets.Count == 1)
        && SelectedAssets.All(asset => Assets.Contains(asset) && IsAccepted(asset));
    public string LocationText => SelectedLocation switch
    {
        DirectoryBaseViewModel directory => $"{directory.Package.Name}/{directory.Path}",
        PackageViewModel package => package.Name,
        PackageCategoryViewModel category => category.Name,
        _ => "All packages"
    };

    public void Initialize()
    {
        if (initialized) return;
        initialized = true;
        selectedLocation = InitialAsset != null && IsAccepted(InitialAsset) ? InitialAsset.Directory
            : InitialLocation ?? Session.LocalPackages.FirstOrDefault()?.AssetMountPoint;
        RefreshSources();
        if (InitialAsset != null && Assets.Contains(InitialAsset)) SelectedAssets.Add(InitialAsset);
        OnPropertyChanged(nameof(SelectedLocation));
    }

    public bool IsAccepted(AssetViewModel asset) => !asset.IsDeleted && !asset.Directory.Package.IsDeleted
        && ReferenceEquals(Session.GetAssetById(asset.Id), asset)
        && (AcceptedTypes.Count == 0 || AcceptedTypes.Any(type => type.IsInstanceOfType(asset.AssetItem.Asset)))
        && (Filter?.Invoke(asset) ?? true);

    public IReadOnlyList<AssetViewModel> GetSelection() => CanAccept ? SelectedAssets.ToArray() : [];

    private void RefreshSources()
    {
        if (IsDestroyed) return;
        var packages = Session.AllPackages.ToArray();
        var directories = packages.SelectMany(p => p.AssetMountPoint.GetDirectoryHierarchy()).ToArray();
        var collections = directories.SelectMany(d => new INotifyCollectionChanged[] { d.Assets, d.SubDirectories })
            .Concat(Session.PackageCategories.Values.Select(c => (INotifyCollectionChanged)c.Content)).ToHashSet();
        foreach (var collection in observedCollections.Except(collections).ToArray())
        {
            collection.CollectionChanged -= SourcesChanged;
            observedCollections.Remove(collection);
        }
        foreach (var collection in collections.Except(observedCollections))
        {
            collection.CollectionChanged += SourcesChanged;
            observedCollections.Add(collection);
        }
        var assets = Session.AllAssets.ToHashSet();
        foreach (var asset in observedAssets.Except(assets).ToArray())
        {
            asset.PropertyChanged -= AssetChanged;
            observedAssets.Remove(asset);
        }
        foreach (var asset in assets.Except(observedAssets))
        {
            asset.PropertyChanged += AssetChanged;
            observedAssets.Add(asset);
        }
        var types = assets.Where(IsAccepted).GroupBy(a => a.AssetItem.Asset.GetType())
            .Select(g => new AssetPickerTypeFilter(g.Key, g.First().TypeDisplayName))
            .OrderBy(t => t.Name, StringComparer.OrdinalIgnoreCase).ToArray();
        var previousType = selectedType?.Type;
        AvailableTypes.Clear();
        AvailableTypes.Add(new(null, "All accepted types"));
        foreach (var type in types) AvailableTypes.Add(type);
        SelectedType = AvailableTypes.FirstOrDefault(t => t.Type == previousType) ?? AvailableTypes[0];
        Refresh();
    }

    private void SourcesChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (refreshQueued || IsDestroyed) return;
        refreshQueued = true;
        _ = Dispatcher.InvokeAsync(() => { refreshQueued = false; RefreshSources(); });
    }
    private void AssetChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(AssetViewModel.Name) or nameof(AssetViewModel.Url) or nameof(AssetViewModel.IsDeleted)
            or nameof(AssetViewModel.Directory)) Refresh();
    }

    private IEnumerable<AssetViewModel> AssetsAtLocation() => SelectedLocation switch
    {
        DirectoryBaseViewModel directory => DirectoryAssets(directory),
        PackageViewModel package => DirectoryAssets(package.AssetMountPoint),
        PackageCategoryViewModel category => category.Content.SelectMany(p => DirectoryAssets(p.AssetMountPoint)),
        null => Session.AllAssets,
        _ => []
    };

    private IEnumerable<AssetViewModel> DirectoryAssets(DirectoryBaseViewModel directory) => IncludeSubfolders
        ? directory.GetDirectoryHierarchy().SelectMany(d => d.Assets) : directory.Assets;

    public void Refresh()
    {
        if (!initialized || IsDestroyed || refreshing) return;
        refreshing = true;
        try
        {
            var words = SearchText.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
            var matches = AssetsAtLocation().Where(IsAccepted)
                .Where(a => SelectedType?.Type == null || SelectedType.Type.IsInstanceOfType(a.AssetItem.Asset))
                .Where(a => words.All(word => $"{a.Directory.Package.Name}/{a.Url} {a.TypeDisplayName} {string.Join(" ", a.AssetItem.Asset.Tags)}"
                    .Contains(word, StringComparison.OrdinalIgnoreCase)))
                .OrderBy(a => a.Url, StringComparer.OrdinalIgnoreCase).ThenBy(a => a.Directory.Package.Name, StringComparer.OrdinalIgnoreCase).ToArray();
            var selected = SelectedAssets.Where(matches.Contains).ToArray();
            if (!Assets.SequenceEqual(matches))
            {
                Assets.Clear();
                foreach (var asset in matches) Assets.Add(asset);
                SelectedAssets.Clear();
                foreach (var asset in selected) SelectedAssets.Add(asset);
            }
            foreach (var asset in SelectedAssets.Where(a => !matches.Contains(a)).ToArray()) SelectedAssets.Remove(asset);
            OnPropertyChanged(nameof(HasAssets), nameof(CanAccept), nameof(LocationText));
        }
        finally { refreshing = false; }
    }

    private void SelectionChanged(object? sender, NotifyCollectionChangedEventArgs e) => OnPropertyChanged(nameof(CanAccept));

    public override void Destroy()
    {
        foreach (var collection in observedCollections) collection.CollectionChanged -= SourcesChanged;
        foreach (var asset in observedAssets) asset.PropertyChanged -= AssetChanged;
        observedCollections.Clear(); observedAssets.Clear();
        SelectedAssets.CollectionChanged -= SelectionChanged;
        base.Destroy();
    }
}
