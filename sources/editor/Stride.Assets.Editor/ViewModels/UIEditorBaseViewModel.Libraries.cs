// Copyright (c) .NET Foundation and Contributors (https://dotnetfoundation.org/ & https://stride3d.net)
// Distributed under the MIT license. See the LICENSE.md file in the project root for more information.

using System.ComponentModel;
using Stride.Assets.Presentation.Quantum;
using Stride.Assets.Presentation.ViewModels;
using Stride.Assets.UI;
using Stride.Core;
using Stride.Core.Assets;
using Stride.Core.Assets.Analysis;
using Stride.Core.Assets.Quantum;
using Stride.Core.Extensions;
using Stride.Core.IO;
using Stride.Core.Presentation.Commands;
using Stride.UI;

namespace Stride.Assets.Editor.ViewModels;

public abstract partial class UIEditorBaseViewModel
{
    private readonly HashSet<UIElementViewModel> commandSelection = [];

    /// <summary>Opens the source libraries of the selected elements.</summary>
    public ICommandBase OpenLibraryEditorCommand { get; private set; } = null!;
    /// <summary>Selects the source library in Asset View.</summary>
    public ICommandBase SelectLibraryCommand { get; private set; } = null!;
    /// <summary>Detaches selected subtrees from their source libraries.</summary>
    public ICommandBase BreakLinkToLibraryCommand { get; private set; } = null!;
    /// <summary>Creates an independent page from one selected subtree.</summary>
    public ICommandBase CreatePageCommand { get; private set; } = null!;
    /// <summary>Creates a library from the selected subtrees and links them to it.</summary>
    public ICommandBase CreateLibraryCommand { get; private set; } = null!;

    private void InitializeLibraryCommands()
    {
        OpenLibraryEditorCommand = new AnonymousCommand(ServiceProvider, () =>
        {
            foreach (var library in SelectedItems.OfType<UIElementViewModel>().Select(x => x.SourceLibrary).OfType<UILibraryViewModel>().Distinct().ToArray())
                Session.EditorCollection.OpenAssetEditor(library);
        });
        SelectLibraryCommand = new AnonymousCommand(ServiceProvider, () =>
        {
            if (LastSelectedElement?.SourceLibrary is { } library) Session.AssetCollection.SelectAssets([library]);
        });
        BreakLinkToLibraryCommand = new AnonymousCommand(ServiceProvider, BreakLibraryLinks);
        CreatePageCommand = new AnonymousCommand(ServiceProvider, () => CreateAssetFromSelection(false));
        CreateLibraryCommand = new AnonymousCommand(ServiceProvider, () => CreateAssetFromSelection(true));
    }

    private void SelectionLibraryChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(UIElementViewModel.SourceLibrary)) UpdateCommands();
    }

    private void BreakLibraryLinks()
    {
        if (!BreakLinkToLibraryCommand.CanExecute(null)) return;
        var parts = SelectionRoots().SelectMany(root => root.Children.BreadthFirst(x => x.Children).OfType<UIElementViewModel>().Prepend(root)).ToArray();
        using var transaction = Session.ActionService?.CreateTransaction();
        Asset.AssetHierarchyPropertyGraph.BreakBasePartLinks(parts.Select(x => x.UIElementDesign));
        if (transaction != null) Session.ActionService?.SetName(transaction, "Break link to UI library");
        UpdateCommands();
    }

    private void CreateAssetFromSelection(bool library)
    {
        var roots = SelectionRoots();
        if (!(library ? CreateLibraryCommand : CreatePageCommand).CanExecute(null)) return;
        var hierarchy = UIAssetPropertyGraph.CloneSubHierarchies(Session.AssetNodeContainer, UIAsset,
            roots.Select(x => x.Id.ObjectId), SubHierarchyCloneFlags.CleanExternalReferences | SubHierarchyCloneFlags.GenerateNewIdsForIdentifiableObjects, out var mapping);
        AssetPartsAnalysis.GenerateNewBaseInstanceIds(hierarchy);
        UIAssetBase created = library ? new UILibraryAsset() : new UIPageAsset();
        created.Hierarchy = hierarchy;
        created.Design = AssetCloner.Clone(UIAsset.Design);
        if (created is UILibraryAsset exports)
            foreach (var root in hierarchy.RootParts.Where(x => !string.IsNullOrWhiteSpace(x.Name)))
                exports.PublicUIElements.Add(root.Id, root.Name!);
        var name = Utilities.BuildValidFileName(string.IsNullOrWhiteSpace(roots[0].Name) ? library ? "UILibrary" : "UIPage" : roots[0].Name);
        var package = Asset.Directory.Package;
        var used = package.Package.Assets.Select(x => (string)x.Location).Concat(package.DeletedAssets.Select(x => x.Url)).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var location = NamingHelper.ComputeDuplicateName((string)UFile.Combine(Asset.Directory.Path, name), used.Contains);
        using var transaction = Session.ActionService?.CreateTransaction();
        var item = new AssetItem(location, created);
        var asset = package.AddAsset(item, Asset.Directory);
        if (library)
        {
            var instanceId = Guid.NewGuid();
            foreach (var (sourceId, cloneId) in mapping)
            {
                if (!UIAsset.Hierarchy.Parts.TryGetValue(sourceId, out var design)) continue;
                Session.AssetNodeContainer.GetOrCreateNode(design)[nameof(UIElementDesign.Base)]
                    .Update(new BasePart(new AssetReference(item.Id, item.Location), cloneId, instanceId));
            }
            Asset.PropertyGraph.RefreshBase();
            foreach (var sourceId in mapping.Keys)
                if (UIAsset.Hierarchy.Parts.TryGetValue(sourceId, out var design))
                    ((IAssetObjectNode)Session.AssetNodeContainer.GetOrCreateNode(design.UIElement)).ResetOverrideRecursively();
        }
        Session.AssetCollection.SelectAssets([asset]);
        if (transaction != null) Session.ActionService?.SetName(transaction, $"Create {asset.Name} from selection");
        UpdateCommands();
    }
}
