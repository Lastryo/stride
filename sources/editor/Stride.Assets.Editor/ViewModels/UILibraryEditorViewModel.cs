// Copyright (c) .NET Foundation and Contributors (https://dotnetfoundation.org/ & https://stride3d.net) and Silicon Studio Corp. (https://www.siliconstudio.co.jp)
// Distributed under the MIT license. See the LICENSE.md file in the project root for more information.

using System.Collections.Specialized;
using System.ComponentModel;
using Stride.Assets.Editor.UIEditor.Services;
using Stride.Assets.Editor.UIEditor.ViewModels;
using Stride.Assets.Presentation.ViewModels;
using Stride.Assets.UI;
using Stride.Core.Quantum;
using Stride.Core.Assets.Editor.Annotations;
using Stride.Core.Assets.Editor.ViewModels;

namespace Stride.Assets.Editor.ViewModels;

[AssetEditorViewModel<UILibraryViewModel>]
public sealed class UILibraryEditorViewModel : UIEditorBaseViewModel, IAssetEditorViewModel<UILibraryViewModel>
{
    public UILibraryEditorViewModel(UILibraryViewModel asset)
        : base(asset)
    {
        foreach (var root in HierarchyRoot.Children) root.PropertyChanged += RootPropertyChanged;
        HierarchyRoot.Children.CollectionChanged += RootsChanged;
    }

    /// <inheritdoc />
    public override UILibraryViewModel Asset => (UILibraryViewModel)base.Asset;

    /// <inheritdoc />
    protected override UILibraryRootViewModel CreateRootPartViewModel()
    {
        return new UILibraryRootViewModel(Asset);
    }

    /// <inheritdoc />
    public override async Task OpenViewport()
    {
        Viewport ??= new UIViewportViewModel(this, model => new UILibraryEditorController(Asset, (UIViewportViewModel)model));
        await Viewport.Initialize();
        Viewport.ShowGame();
        RegisterAllPropagators();
    }

    /// <inheritdoc />
    public override void Destroy()
    {
        HierarchyRoot.Children.CollectionChanged -= RootsChanged;
        foreach (var root in HierarchyRoot.Children) root.PropertyChanged -= RootPropertyChanged;
        base.Destroy();
    }

    private void RootsChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.OldItems != null)
            foreach (UIElementViewModel root in e.OldItems)
            {
                root.PropertyChanged -= RootPropertyChanged;
                UpdateExport(root, null);
            }
        if (e.NewItems != null)
            foreach (UIElementViewModel root in e.NewItems)
            {
                root.PropertyChanged += RootPropertyChanged;
                UpdateExport(root, root.Name);
            }
        if (ActiveRoot == null || !HierarchyRoot.Children.Contains(ActiveRoot))
            ActiveRoot = HierarchyRoot.Children.OfType<UIElementViewModel>().FirstOrDefault();
    }

    private void RootPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(UIElementViewModel.Name) && sender is UIElementViewModel root)
            UpdateExport(root, root.Name);
    }

    private void UpdateExport(UIElementViewModel root, string? name)
    {
        if (!Asset.IsEditable || Session.ActionService?.UndoRedoInProgress == true) return;
        var node = Session.AssetNodeContainer.GetOrCreateNode(Asset.Asset)[nameof(UILibraryAsset.PublicUIElements)].Target!;
        var index = new NodeIndex(root.Id.ObjectId);
        var exists = Asset.Asset.PublicUIElements.TryGetValue(root.Id.ObjectId, out var previous);
        if (string.IsNullOrWhiteSpace(name) ? !exists : exists && previous == name) return;
        using var transaction = Session.ActionService?.CreateTransaction();
        if (string.IsNullOrWhiteSpace(name)) node.Remove(previous, index);
        else if (exists) node.Update(name, index);
        else node.Add(name, index);
        if (transaction != null) Session.ActionService?.SetName(transaction, "Update UI library export");
    }
}
