// Copyright (c) .NET Foundation and Contributors (https://dotnetfoundation.org/ & https://stride3d.net)
// Distributed under the MIT license. See the LICENSE.md file in the project root for more information.

using Stride.Assets.Editor.UIEditor.ViewModels;
using Stride.Assets.Presentation.Quantum;
using Stride.Assets.Presentation.ViewModels;
using Stride.Assets.UI;
using Stride.Core;
using Stride.Core.Assets;
using Stride.Core.Assets.Editor.Components.CopyPasteProcessors;
using Stride.Core.Assets.Editor.Services;
using Stride.Core.Assets.Quantum;
using Stride.Core.Diagnostics;
using Stride.Core.Extensions;
using Stride.Core.Mathematics;
using Stride.Core.Presentation.Commands;
using Stride.UI;
using Stride.UI.Controls;
using Stride.UI.Panels;

namespace Stride.Assets.Editor.ViewModels;

public abstract partial class UIEditorBaseViewModel
{
    private readonly AssetCompositeHierarchyPasteProcessor<UIElementDesign, UIElement> hierarchyPasteProcessor = new();
    private string? clipboardText;
    private int clipboardRootCount;

    /// <summary>Copies the selected subtrees to the system clipboard.</summary>
    public ICommandBase CopyCommand { get; private set; } = null!;
    /// <summary>Cuts selected subtrees after writing them to the system clipboard.</summary>
    public ICommandBase CutCommand { get; private set; } = null!;
    /// <summary>Pastes subtrees into the selected container.</summary>
    public ICommandBase PasteCommand { get; private set; } = null!;
    /// <summary>Pastes subtrees at asset root level.</summary>
    public ICommandBase PasteAsRootCommand { get; private set; } = null!;

    private void InitializeEditingCommands()
    {
        ServiceProvider.TryGet<ICopyPasteService>()?.RegisterProcessor(hierarchyPasteProcessor);
        CopyCommand = new AnonymousTaskCommand(ServiceProvider, () => CopySelection(false));
        CutCommand = new AnonymousTaskCommand(ServiceProvider, () => CopySelection(true));
        PasteCommand = new AnonymousTaskCommand(ServiceProvider, () => Paste(false));
        PasteAsRootCommand = new AnonymousTaskCommand(ServiceProvider, () => Paste(true));
        UpdateCommands();
    }

    private List<UIElementViewModel> SelectionRoots()
    {
        var selected = SelectedItems.OfType<UIElementViewModel>().ToHashSet();
        return HierarchyRoot.Children.BreadthFirst(x => x.Children).OfType<UIElementViewModel>()
            .Where(x => selected.Contains(x) && !Ancestors(x).Any(selected.Contains)).ToList();
    }

    private static IEnumerable<UIElementViewModel> Ancestors(UIHierarchyItemViewModel item)
    {
        for (var parent = item.Parent as UIElementViewModel; parent != null; parent = parent.Parent as UIElementViewModel)
            yield return parent;
    }

    /// <summary>Checks container capacity and prevents cycles and cross-asset moves.</summary>
    public bool CanInsert(UIHierarchyItemViewModel target, int count, IReadOnlyCollection<UIElementViewModel>? moving = null)
    {
        if (!Asset.IsEditable || target.Asset != Asset || count <= 0) return false;
        if (moving != null && moving.Any(x => x.Asset != Asset || x == target || Ancestors(target).Contains(x))) return false;
        var remaining = target.Children.Count(x => moving?.Contains(x) != true);
        return target switch
        {
            UIElementViewModel { AssetSideUIElement: Panel } => true,
            UIElementViewModel { AssetSideUIElement: ContentControl } => remaining + count <= 1,
            UIPageRootViewModel => remaining + count <= 1,
            UIRootViewModel => true,
            _ => false
        };
    }

    /// <summary>Creates an element in a validated container and selects it.</summary>
    public void InsertFactory(IUIElementFactory factory, UIHierarchyItemViewModel target, int index, Vector3? position = null)
    {
        if (!CanInsert(target, 1) || factory is UIElementFromLibrary source && !source.CanCreateIn(UIAsset)) return;
        var hierarchy = factory.Create(UIAsset);
        if (hierarchy == null || !CanInsert(target, hierarchy.RootParts.Count)) return;
        using var transaction = Session.ActionService?.CreateTransaction();
        var ids = new List<Guid>();
        foreach (var root in hierarchy.RootParts)
        {
            if (position.HasValue && target is UIElementViewModel { AssetSideUIElement: Canvas })
            {
                root.DependencyProperties.Set(Canvas.AbsolutePositionPropertyKey, position.Value);
                root.DependencyProperties.Set(Canvas.RelativePositionPropertyKey, Vector3.Zero);
            }
            Asset.InsertUIElement(hierarchy.Parts, hierarchy.Parts[root.Id], (target as UIElementViewModel)?.AssetSideUIElement, index);
            if (index >= 0) index++;
            ids.Add(root.Id);
        }
        if (transaction != null) Session.ActionService?.SetName(transaction, $"Add {factory.Name}");
        SelectElements(ids);
    }

    /// <summary>Moves subtrees into a container or to a sibling index in one undo transaction.</summary>
    public void MoveElements(IReadOnlyCollection<UIElementViewModel> elements, UIHierarchyItemViewModel target, int index)
    {
        var roots = elements.Where(x => !Ancestors(x).Any(elements.Contains)).ToList();
        if (!CanInsert(target, roots.Count, roots)) return;
        var hierarchy = UIAssetPropertyGraph.CloneSubHierarchies(Session.AssetNodeContainer, UIAsset,
            roots.Select(x => x.Id.ObjectId), SubHierarchyCloneFlags.None, out _);
        if (index >= 0) index -= roots.Count(x => x.Parent == target && target.Children.IndexOf(x) < index);
        using var transaction = Session.ActionService?.CreateTransaction();
        ClearSelection();
        foreach (var root in roots) Asset.AssetHierarchyPropertyGraph.RemovePartFromAsset(root.UIElementDesign);
        foreach (var root in hierarchy.RootParts)
        {
            Asset.InsertUIElement(hierarchy.Parts, hierarchy.Parts[root.Id], (target as UIElementViewModel)?.AssetSideUIElement, index);
            if (index >= 0) index++;
        }
        if (transaction != null) Session.ActionService?.SetName(transaction, "Move UI elements");
        SelectElements(hierarchy.RootParts.Select(x => x.Id));
    }

    private void SelectElements(IEnumerable<Guid> ids)
    {
        ClearSelection();
        foreach (var id in ids) Select(id, true);
    }

    /// <summary>Refreshes clipboard availability when the editor or a context menu receives focus.</summary>
    public async Task RefreshClipboard()
    {
        clipboardText = null;
        clipboardRootCount = 0;
        try
        {
            var text = await (ServiceProvider.TryGet<IClipboardService>()?.GetTextAsync() ?? Task.FromResult<string?>(null));
            var service = ServiceProvider.TryGet<ICopyPasteService>();
            if (service?.CanPaste(text, UIAsset.GetType(), typeof(UIElement), typeof(AssetCompositeHierarchyData<UIElementDesign, UIElement>)) == true)
            {
                var result = service.DeserializeCopiedData(text!, UIAsset, typeof(UIElement));
                if (result.Items.Count == 1 && result.Items[0].Data is AssetCompositeHierarchyData<UIElementDesign, UIElement> hierarchy
                    && hierarchy.RootParts.All(x => hierarchy.Parts.ContainsKey(x.Id)))
                {
                    clipboardText = text;
                    clipboardRootCount = hierarchy.RootParts.Count;
                }
            }
        }
        catch (Exception exception) { Logger.Warning("Could not read UI elements from the clipboard.", exception); }
        UpdateCommands();
    }

    /// <summary>Checks clipboard contents and the target's capacity, including the single page root rule.</summary>
    public virtual bool CanPaste(bool asRoot)
    {
        var target = asRoot ? HierarchyRoot : SelectedItems.Count == 1 ? SelectedItems.Single() : null;
        return clipboardText != null && target != null && CanInsert(target, clipboardRootCount);
    }

    private async Task CopySelection(bool cut)
    {
        var roots = SelectionRoots();
        var clipboard = ServiceProvider.TryGet<IClipboardService>();
        var service = ServiceProvider.TryGet<ICopyPasteService>();
        if (roots.Count == 0 || clipboard == null || service == null) return;
        try
        {
            var hierarchy = UIAssetPropertyGraph.CloneSubHierarchies(Session.AssetNodeContainer, UIAsset,
                roots.Select(x => x.Id.ObjectId), SubHierarchyCloneFlags.None, out _);
            var text = service.CopyFromAsset(Asset.AssetHierarchyPropertyGraph, Asset.Id, hierarchy, false);
            if (string.IsNullOrEmpty(text)) return;
            await clipboard.SetTextAsync(text);
            // Clipboard access can yield; cut only the original selection, if it is still present.
            if (cut && roots.All(x => FindPartViewModel(x.Id) == x))
            {
                SelectElements(roots.Select(x => x.Id.ObjectId));
                await DeleteSelectedElements();
            }
            await RefreshClipboard();
        }
        catch (Exception exception) { Logger.Warning("Could not copy UI elements.", exception); }
    }

    private async Task Paste(bool asRoot)
    {
        await RefreshClipboard();
        if (!CanPaste(asRoot)) return;
        var target = asRoot ? HierarchyRoot : SelectedItems.Single();
        var result = ServiceProvider.Get<ICopyPasteService>().DeserializeCopiedData(clipboardText!, UIAsset, typeof(UIElement));
        if (result.Items.Single().Data is not AssetCompositeHierarchyData<UIElementDesign, UIElement> hierarchy
            || !CanInsert(target, hierarchy.RootParts.Count)) return;
        using var transaction = Session.ActionService?.CreateTransaction();
        foreach (var root in hierarchy.RootParts)
            Asset.InsertUIElement(hierarchy.Parts, hierarchy.Parts[root.Id], (target as UIElementViewModel)?.AssetSideUIElement);
        if (transaction != null) Session.ActionService?.SetName(transaction, "Paste UI elements");
        SelectElements(hierarchy.RootParts.Select(x => x.Id));
    }

    private void UpdateCommands()
    {
        var roots = SelectionRoots();
        DeleteCommand.IsEnabled = Asset.IsEditable && roots.Count > 0;
        DuplicateSelectionCommand.IsEnabled = Asset.IsEditable && roots.Count > 0 && roots.All(x => x.CanDuplicate());
        GroupIntoCommand.IsEnabled = Asset.IsEditable && roots.Count > 0 && roots.All(x => x.Parent == roots[0].Parent);
        UngroupCommand.IsEnabled = Asset.IsEditable && SelectedItems.Count == 1 && LastSelectedElement is { IsPanel: true, Parent: UILibraryRootViewModel or UIElementViewModel { IsPanel: true } };
        ChangeLayoutTypeCommand.IsEnabled = Asset.IsEditable && SelectedItems.Count == 1 && LastSelectedElement?.IsPanel == true;
        CopyCommand.IsEnabled = roots.Count > 0 && ServiceProvider.TryGet<IClipboardService>() != null;
        CutCommand.IsEnabled = Asset.IsEditable && CopyCommand.IsEnabled;
        PasteCommand.IsEnabled = CanPaste(false);
        PasteAsRootCommand.IsEnabled = CanPaste(true);
        var linked = SelectedItems.OfType<UIElementViewModel>().Any(x => x.SourceLibrary != null);
        OpenLibraryEditorCommand.IsEnabled = linked;
        SelectLibraryCommand.IsEnabled = SelectedItems.Count == 1 && linked;
        BreakLinkToLibraryCommand.IsEnabled = Asset.IsEditable && linked;
        CreatePageCommand.IsEnabled = Asset.Directory.Package.IsEditable && SelectedItems.Count == 1 && roots.Count == 1;
        CreateLibraryCommand.IsEnabled = Asset.IsEditable && Asset.Directory.Package.IsEditable && roots.Count > 0;
    }
}
