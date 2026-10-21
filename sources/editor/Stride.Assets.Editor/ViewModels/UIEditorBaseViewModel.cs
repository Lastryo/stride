// Copyright (c) .NET Foundation and Contributors (https://dotnetfoundation.org/ & https://stride3d.net) and Silicon Studio Corp. (https://www.siliconstudio.co.jp)
// Distributed under the MIT license. See the LICENSE.md file in the project root for more information.

using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Linq;
using System.Threading.Tasks;
using Stride.Assets.Editor.GameEditor.ViewModels;
using Stride.Assets.Editor.UIEditor.ViewModels;
using Stride.Assets.Presentation.Quantum;
using Stride.Assets.Presentation.ViewModels;
using Stride.Assets.UI;
using Stride.Core;
using Stride.Core.Assets;
using Stride.Core.Assets.Analysis;
using Stride.Core.Assets.Editor.ViewModels;
using Stride.Core.Assets.Presentation.ViewModels;
using Stride.Core.Assets.Quantum;
using Stride.Core.Diagnostics;
using Stride.Core.Extensions;
using Stride.Core.Mathematics;
using Stride.Core.Presentation.Collections;
using Stride.Core.Presentation.Commands;
using Stride.Core.Presentation.Services;
using Stride.Core.Quantum;
using Stride.UI;
using Stride.UI.Controls;
using Stride.UI.Panels;

namespace Stride.Assets.Editor.ViewModels;

public abstract partial class UIEditorBaseViewModel : AssetCompositeHierarchyEditorViewModel<UIElementDesign, UIElement, UIBaseViewModel, UIHierarchyItemViewModel>
{
    public static readonly ILogger EditorLogger = GlobalLogger.GetLogger("UI");
    public ILogger Logger => EditorLogger;

    private bool destroying;
    private int propertiesRefreshVersion;
    private UIViewportViewModel? viewport;
    private UIElementViewModel? activeRoot;
    private UIElementViewModel? lastSelectedElement;
    private readonly ObservableSet<IUIElementFactory> factories = [];
    private IReadOnlyCollection<IUIElementFactory> panelFactories = [];
    private readonly ObservableSet<UIElementViewModel> selectableElements = [];
    private readonly Dictionary<IObjectNode, UILibraryViewModel> observedLibraries = [];
    private readonly Dictionary<Guid, GameEditorChangePropagator<UIElementDesign, UIElement, UIElementViewModel>> propagators = [];

    protected UIEditorBaseViewModel(UIBaseViewModel asset)
        : base(asset)
    {
        ShowPropertiesCommand = new AnonymousCommand(ServiceProvider, ShowAssetProperties);
        DeleteCommand = new AnonymousTaskCommand(ServiceProvider, DeleteSelectedElements);
        DuplicateSelectionCommand = new AnonymousCommand(ServiceProvider, DuplicateSelectedElements);
        GroupIntoCommand = new AnonymousCommand<IUIElementFactory>(ServiceProvider, GroupInto);
        UngroupCommand = new AnonymousCommand(ServiceProvider, Ungroup);
        ChangeLayoutTypeCommand = new AnonymousCommand<IUIElementFactory>(ServiceProvider, ChangeLayoutType);
        RefreshSelectableElementsCommand = new AnonymousCommand(ServiceProvider, RefreshSelectableElements);
        SetCurrentSelectionCommand = new AnonymousCommand<AbsoluteId>(ServiceProvider, SetCurrentSelection);

        factories.CollectionChanged += FactoriesChanged;
        InitializeLibraryCommands();
        InitializeEditingCommands();
        InitializeLibraries();
        Session.AssetCollection.Assets.CollectionChanged += AssetsCollectionChanged;
        Asset.AssetHierarchyPropertyGraph.PartAdded += OnPartAdded;
        Asset.AssetHierarchyPropertyGraph.PartRemoved += OnPartRemoved;
    }

    public new virtual UIBaseViewModel Asset => (UIBaseViewModel)base.Asset;

    public UIViewportViewModel? Viewport { get => viewport; protected set => SetValue(ref viewport, value); }

    public float SnapValue => Viewport?.SnapValue ?? 1.0f;

    public Color GuidelineColor => Viewport?.GuidelineColor ?? Color.Red;
    public float GuidelineThickness => Viewport?.GuidelineThickness ?? 2.0f;
    public Color HighlightColor => Viewport?.HighlightColor ?? Color.Pink;
    public float HighlightThickness => Viewport?.HighlightThickness ?? 2.0f;
    public Color SelectionColor => Viewport?.SelectionColor ?? Color.LimeGreen;
    public float SelectionThickness => Viewport?.SelectionThickness ?? 2.0f;
    public Color SizingColor => Viewport?.SizingColor ?? Color.Cyan;
    public float SizingThickness => Viewport?.SizingThickness ?? 2.0f;

    public virtual Task OpenViewport() => Task.CompletedTask;

    public UIRootViewModel HierarchyRoot => (UIRootViewModel)RootPart;

    public UIAssetBase UIAsset => (UIAssetBase)Asset.Asset;

    public IReadOnlyObservableCollection<IUIElementFactory> Factories => factories;

    /// <summary>The sorted library/category hierarchy of the element palette.</summary>
    public IReadOnlyList<UIElementFactoryGroup> FactoryGroups => factories
        .GroupBy(factory => factory is UIElementFromLibrary ? "User libraries" : "Standard library")
        .OrderBy(group => group.Key, StringComparer.Ordinal)
        .Select(group => new UIElementFactoryGroup(group.Key, group.GroupBy(factory => factory.Category)
            .OrderBy(category => category.Key, StringComparer.OrdinalIgnoreCase)
            .Select(category => (object)new UIElementFactoryGroup(category.Key, category.OrderBy(factory => factory.Name, StringComparer.OrdinalIgnoreCase).Cast<object>().ToArray())).ToArray()))
        .ToArray();

    private void FactoriesChanged(object? sender, NotifyCollectionChangedEventArgs e) => OnPropertyChanged(nameof(FactoryGroups));

    public IReadOnlyCollection<IUIElementFactory> PanelFactories { get => panelFactories; private set => SetValue(ref panelFactories, value); }

    public UIElementViewModel? LastSelectedElement { get => lastSelectedElement; private set => SetValue(ref lastSelectedElement, value); }

    public UIElementViewModel? ActiveRoot
    {
        get => activeRoot;
        protected internal set
        {
            var oldValue = activeRoot;
            SetValue(ref activeRoot, value, () => ActiveRootChanged(oldValue, activeRoot));
        }
    }

    public IReadOnlyObservableCollection<UIElementViewModel> SelectableElements => selectableElements;

    public ICommandBase ShowPropertiesCommand { get; }
    public ICommandBase DeleteCommand { get; }
    public ICommandBase DuplicateSelectionCommand { get; }
    public ICommandBase GroupIntoCommand { get; }
    public ICommandBase UngroupCommand { get; }
    public ICommandBase ChangeLayoutTypeCommand { get; }
    public ICommandBase RefreshSelectableElementsCommand { get; }
    public ICommandBase SetCurrentSelectionCommand { get; }

    public override void Destroy()
    {
        EnsureNotDestroyed(nameof(UIEditorBaseViewModel));
        destroying = true;
        factories.CollectionChanged -= FactoriesChanged;
        Viewport?.Destroy();
        foreach (var element in commandSelection) element.PropertyChanged -= SelectionLibraryChanged;
        commandSelection.Clear();
        foreach (var node in observedLibraries.Keys) node.ItemChanged -= LibraryContentChanged;
        observedLibraries.Clear();
        Asset.AssetHierarchyPropertyGraph.PartAdded -= OnPartAdded;
        Asset.AssetHierarchyPropertyGraph.PartRemoved -= OnPartRemoved;
        foreach (var propagator in propagators.Values)
        {
            propagator.Destroy();
        }
        propagators.Clear();
        Session.AssetCollection.Assets.CollectionChanged -= AssetsCollectionChanged;
        ServiceProvider.TryGet<Stride.Core.Assets.Editor.Services.ICopyPasteService>()?.UnregisterProcessor(hierarchyPasteProcessor);
        base.Destroy();
    }

    public override async Task DestroyAsync()
    {
        Destroy();
        if (Viewport != null)
            await Viewport.Controller.GameStopped;
    }

    public void Select(Guid elementId, bool isAdditive)
    {
        var partId = new AbsoluteId(Asset.Id, elementId);
        var element = FindPartViewModel(partId);
        if (element == null)
            return;

        if (SelectedContent.Contains(element))
            return;

        if (!isAdditive)
            ClearSelection();
        SelectedContent.Add(element);
    }

    public void ClearSelection()
    {
        SelectedContent.Clear();
        SelectedItems.Clear();
    }

    public void UpdateProperties(Guid elementId, IReadOnlyDictionary<string, object> changes)
    {
        if (!Asset.IsEditable) return;
        var partId = new AbsoluteId(Asset.Id, elementId);
        var element = (UIElementViewModel?)FindPartViewModel(partId);
        if (element == null)
            return;

        var node = Session.AssetNodeContainer.GetNode(element.AssetSideUIElement);
        if (node == null)
            return;

        var undoService = ServiceProvider.TryGet<IUndoRedoService>();
        using var transaction = undoService?.CreateTransaction();

        foreach (var kv in changes)
        {
            var member = node.TryGetChild(kv.Key);
            if (member == null)
                continue;

            var currentValue = member.Retrieve();
            if (!Equals(currentValue, kv.Value))
            {
                member.Update(kv.Value);
            }
        }
        if (undoService != null && transaction != null)
            undoService.SetName(transaction, $"Update {element.ElementType.Name}");
    }

    public void AddElement(IUIElementFactory factory, UIElementViewModel? targetParent = null)
    {
        targetParent ??= SelectedItems.OfType<UIElementViewModel>().FirstOrDefault(x => CanInsert(x, 1))
            ?? (HierarchyRoot as UIPageRootViewModel)?.RootElement;
        InsertFactory(factory, targetParent ?? (UIHierarchyItemViewModel)HierarchyRoot, -1);
    }

    public Task RefreshPropertiesAsync() => RefreshEditorProperties();

    protected override async Task RefreshEditorProperties()
    {
        // Selection replacement clears and adds items in the same UI turn.
        var version = ++propertiesRefreshVersion;
        await Task.Yield();
        if (IsDestroyed || version != propertiesRefreshVersion) return;

        var selectedElements = SelectedItems.OfType<UIElementViewModel>().ToList();
        EditorProperties.UpdateTypeAndName(
            selectedElements,
            selectedElements.Count,
            e => e.ElementType.Name,
            e => string.IsNullOrEmpty(e.Name) ? e.ElementType.Name : e.Name,
            "elements");
        await EditorProperties.GenerateSelectionPropertiesAsync(selectedElements);
    }

    protected override void SelectedContentCollectionChanged(NotifyCollectionChangedAction action)
    {
        base.SelectedContentCollectionChanged(action);
        SynchronizeSelection();
    }

    protected override void SelectedItemsCollectionChanged(NotifyCollectionChangedAction action)
    {
        base.SelectedItemsCollectionChanged(action);
        SynchronizeSelection();
    }

    private void SynchronizeSelection()
    {
        foreach (var element in commandSelection) element.PropertyChanged -= SelectionLibraryChanged;
        commandSelection.Clear();
        commandSelection.UnionWith(SelectedItems.OfType<UIElementViewModel>());
        foreach (var element in commandSelection) element.PropertyChanged += SelectionLibraryChanged;
        LastSelectedElement = SelectedItems.OfType<UIElementViewModel>().LastOrDefault();
        UpdateCommands();
        if (Viewport?.Controller == null)
            return;

        if (SelectedItems.Count == 0)
        {
            Viewport.Controller.AdornerService?.ClearSelection().Forget();
            return;
        }

        if (LastSelectedElement != null)
        {
            ActiveRoot = (LastSelectedElement.GetRootElementViewModel() as UIElementViewModel) ?? LastSelectedElement;
        }
        if (ActiveRoot != null && Viewport.Controller.AdornerService != null)
        {
            var elementIds = SelectedItems.OfType<UIElementViewModel>()
                .Where(e => e.AssetSideUIElement.FindVisualRoot().Id == ActiveRoot.Id.ObjectId)
                .Select(e => e.Id.ObjectId)
                .ToList();
            Viewport.Controller.AdornerService.SelectElements(elementIds).Forget();
        }
    }

    private void ShowAssetProperties()
    {
        EditorProperties.TypeDescription = Asset.TypeDisplayName;
        EditorProperties.Name = Asset.Name;
        Session.ActiveProperties.GenerateSelectionPropertiesAsync(Asset.Yield()).Forget();
    }

    private async Task DeleteSelectedElements()
    {
        var elementsToDelete = SelectionRoots();
        if (elementsToDelete.Count == 0)
            return;

        var undoService = ServiceProvider.TryGet<IUndoRedoService>();
        using var transaction = undoService?.CreateTransaction();

        ClearSelection();
        Asset.AssetHierarchyPropertyGraph.DeleteParts(elementsToDelete.Select(x => x.UIElementDesign), out var mapping);
        var operation = new DeletedPartsTrackingOperation<UIElementDesign, UIElement>(Asset, mapping);
        undoService?.PushOperation(operation);
        if (undoService != null && transaction != null)
            undoService.SetName(transaction, "Delete selected UI elements");
    }

    private void DuplicateSelectedElements()
    {
        var elementsToDuplicate = SelectionRoots();
        if (elementsToDuplicate.Any(e => !e.CanDuplicate())) return;
        if (elementsToDuplicate.Count == 0)
            return;

        var undoService = ServiceProvider.TryGet<IUndoRedoService>();
        using var transaction = undoService?.CreateTransaction();

        ClearSelection();
        foreach (var element in elementsToDuplicate)
        {
            var parent = (UIHierarchyItemViewModel)element.Parent!;
            var subTreeRoot = element.AssetSideUIElement.Id;
            var cloned = UIAssetPropertyGraph.CloneSubHierarchies(Asset.Session.AssetNodeContainer, Asset.Asset, subTreeRoot.Yield(), SubHierarchyCloneFlags.GenerateNewIdsForIdentifiableObjects, out _);
            AssetPartsAnalysis.GenerateNewBaseInstanceIds(cloned);
            var rootDesign = cloned.Parts[cloned.RootParts[0].Id];
            if (!string.IsNullOrEmpty(rootDesign.UIElement.Name))
            {
                var existingNames = parent.Children.OfType<UIElementViewModel>().Select(x => x.Name ?? string.Empty).ToHashSet(StringComparer.OrdinalIgnoreCase);
                rootDesign.UIElement.Name = NamingHelper.ComputeDuplicateName(rootDesign.UIElement.Name, existingNames.Contains);
            }
            Asset.InsertUIElement(cloned.Parts, rootDesign, (parent as UIElementViewModel)?.AssetSideUIElement);
            Select(rootDesign.UIElement.Id, true);
        }

        if (undoService != null && transaction != null)
            undoService.SetName(transaction, "Duplicate elements");
    }

    private void GroupInto(IUIElementFactory factory)
    {
        var targetPanelType = (factory as UIElementFromSystemLibrary)?.Type;
        if (targetPanelType == null || !typeof(Panel).IsAssignableFrom(targetPanelType))
            return;

        var selected = SelectionRoots();
        if (selected.Count == 0)
            return;

        var parent = selected[0].Parent as UIHierarchyItemViewModel;
        if (parent == null || selected.Any(x => x.Parent != parent) || !GroupIntoCommand.CanExecute(factory))
            return;

        var undoService = ServiceProvider.TryGet<IUndoRedoService>();
        using var transaction = undoService?.CreateTransaction();

        var insertIndex = parent.Children.IndexOf(selected[0]);
        var newPanel = (Panel)Activator.CreateInstance(targetPanelType)!;
        var newPanelDesign = new UIElementDesign(newPanel);

        var hierarchy = UIAssetPropertyGraph.CloneSubHierarchies(Asset.Session.AssetNodeContainer, Asset.Asset, selected.Select(c => c.Id.ObjectId), SubHierarchyCloneFlags.None, out _);
        hierarchy.RootParts.Add(newPanel);
        hierarchy.Parts.Add(newPanelDesign);

        foreach (var child in selected)
        {
            Asset.AssetHierarchyPropertyGraph.RemovePartFromAsset(child.UIElementDesign);
        }

        Asset.InsertUIElement(hierarchy.Parts, newPanelDesign, (parent as UIElementViewModel)?.AssetSideUIElement, insertIndex);

        foreach (var child in selected)
        {
            Asset.InsertUIElement(hierarchy.Parts, hierarchy.Parts[child.Id.ObjectId], newPanel);
        }

        if (undoService != null && transaction != null)
            undoService.SetName(transaction, $"Group into {targetPanelType.Name}");
    }

    private void Ungroup()
    {
        var selected = SelectedItems.OfType<UIElementViewModel>().FirstOrDefault(x => x.AssetSideUIElement is Panel);
        if (selected?.Parent is not UIHierarchyItemViewModel parent
            || parent is not UILibraryRootViewModel && parent is not UIElementViewModel { IsPanel: true }) return;
        var insertIndex = parent.Children.IndexOf(selected);
        var targetParent = (parent as UIElementViewModel)?.AssetSideUIElement;

        var undoService = ServiceProvider.TryGet<IUndoRedoService>();
        using var transaction = undoService?.CreateTransaction();

        var children = selected.Children.OfType<UIElementViewModel>().ToList();
        var hierarchy = UIAssetPropertyGraph.CloneSubHierarchies(Asset.Session.AssetNodeContainer, Asset.Asset, children.Select(c => c.Id.ObjectId), SubHierarchyCloneFlags.None, out _);

        foreach (var child in children)
        {
            Asset.AssetHierarchyPropertyGraph.RemovePartFromAsset(child.UIElementDesign);
        }
        Asset.AssetHierarchyPropertyGraph.RemovePartFromAsset(selected.UIElementDesign);

        foreach (var child in children)
        {
            Asset.InsertUIElement(hierarchy.Parts, hierarchy.Parts[child.Id.ObjectId], targetParent, insertIndex++);
        }

        if (undoService != null && transaction != null)
            undoService.SetName(transaction, $"Ungroup {children.Count} elements");
    }

    private void ChangeLayoutType(IUIElementFactory factory)
    {
        var targetType = (factory as UIElementFromSystemLibrary)?.Type;
        if (targetType == null || !typeof(Panel).IsAssignableFrom(targetType))
            return;

        var selected = SelectedItems.OfType<UIElementViewModel>().FirstOrDefault(x => x.AssetSideUIElement is Panel);
        if (selected == null || selected.ElementType == targetType)
            return;

        var undoService = ServiceProvider.TryGet<IUndoRedoService>();
        using var transaction = undoService?.CreateTransaction();

        var children = selected.Children.OfType<UIElementViewModel>().ToList();
        var targetPanel = (Panel)Activator.CreateInstance(targetType)!;
        var newPanelDesign = new UIElementDesign(targetPanel);

        var hierarchy = UIAssetPropertyGraph.CloneSubHierarchies(Asset.Session.AssetNodeContainer, Asset.Asset, children.Select(c => c.Id.ObjectId), SubHierarchyCloneFlags.None, out _);
        hierarchy.RootParts.Add(targetPanel);
        hierarchy.Parts.Add(newPanelDesign);

        var insertIndex = ((UIHierarchyItemViewModel)selected.Parent!).Children.IndexOf(selected);
        var parentElement = (selected.Parent as UIElementViewModel)?.AssetSideUIElement;

        foreach (var child in children)
        {
            Asset.AssetHierarchyPropertyGraph.RemovePartFromAsset(child.UIElementDesign);
        }
        Asset.AssetHierarchyPropertyGraph.RemovePartFromAsset(selected.UIElementDesign);

        Asset.InsertUIElement(hierarchy.Parts, newPanelDesign, parentElement, insertIndex);

        foreach (var child in children)
        {
            Asset.InsertUIElement(hierarchy.Parts, hierarchy.Parts[child.Id.ObjectId], targetPanel);
        }

        if (undoService != null && transaction != null)
            undoService.SetName(transaction, $"Change layout type to {targetType.Name}");
    }

    private void RefreshSelectableElements() => RefreshSelectableElementsAsync().Forget();

    /// <summary>Refreshes the elements under the pointer using the game dispatcher.</summary>
    public async Task RefreshSelectableElementsAsync()
    {
        selectableElements.Clear();
        if (Viewport?.SceneInitialized != true) return;
        var ids = await Viewport.Controller.InvokeAsync(() =>
        {
            var position = Viewport.Controller.GetMousePositionInUI();
            return Viewport.Controller.AdornerService.GetElementIdsAtPosition(ref position).Distinct().ToArray();
        });
        selectableElements.AddRange(ids.Select(id => FindPartViewModel(new AbsoluteId(Asset.Id, id))).OfType<UIElementViewModel>());
    }

    private void SetCurrentSelection(AbsoluteId elementId)
    {
        var element = FindPartViewModel(elementId);
        if (element == null)
            return;

        ClearSelection();
        SelectedContent.Add(element);
    }

    private async void ActiveRootChanged(UIElementViewModel? previousRoot, UIElementViewModel? newRoot)
    {
        if (destroying || Viewport?.Controller == null)
            return;

        try
        {
            if (previousRoot != null)
            {
                var idsToDisable = previousRoot.Children.BreadthFirst(e => e.Children).OfType<UIElementViewModel>().Select(e => e.Id.ObjectId).Concat([previousRoot.Id.ObjectId]).ToList();
                await Viewport.Controller.InvokeAsync(() =>
                {
                    Viewport.Controller.HideUI();
                    foreach (var id in idsToDisable)
                    {
                        Viewport.Controller.AdornerService?.DisableAdorner(id).Forget();
                    }
                });
            }
            if (destroying) return;
            if (newRoot != null)
            {
                var rootId = newRoot.Id.ObjectId;
                var idsToEnable = newRoot.Children.BreadthFirst(e => e.Children).OfType<UIElementViewModel>().Select(e => e.Id.ObjectId).Concat([rootId]).ToList();
                await Viewport.Controller.InvokeAsync(() =>
                {
                    Viewport.Controller.ShowUI(rootId);
                });
                if (destroying) return;
                foreach (var id in idsToEnable)
                {
                    Viewport.Controller.AdornerService?.EnableAdorner(id).Forget();
                }
            }
        }
        catch (OperationCanceledException) when (destroying) { }
        catch (ObjectDisposedException) when (destroying) { }
    }

    private void InitializeLibraries()
    {
        foreach (var node in observedLibraries.Keys) node.ItemChanged -= LibraryContentChanged;
        observedLibraries.Clear();
        factories.Clear();

        var allLibraries = Session.AllPackages
            .GroupBy(p => p.Package.IsSystem)
            .ToDictionary(p => p.Key, p => p.SelectMany(x => x.Assets).OfType<UILibraryViewModel>());

        if (allLibraries.TryGetValue(true, out var sysLibs))
        {
            foreach (var sysLib in sysLibs)
            {
                factories.AddRange(sysLib.Asset.PublicUIElements.Select(x => new UIElementFromSystemLibrary(ServiceProvider, sysLib, x.Key)));
            }
        }
        PanelFactories = factories.Where(f => f.Category == "Panel").OrderBy(f => f.Name).ToList();

        if (allLibraries.TryGetValue(false, out var userLibs))
        {
            foreach (var userLib in userLibs)
            {
                RemoveLibrary(userLib);
                AddLibrary(userLib);
            }
        }
    }

    private void AddLibrary(UILibraryViewModel library)
    {
        if (library == Asset || observedLibraries.Values.Contains(library))
            return;

        var list = library.Asset.PublicUIElements.Select(x => new UIElementFromLibrary(ServiceProvider, library, x.Key)).OrderBy(x => x.Name);
        factories.AddRange(list);

        var node = Session.AssetNodeContainer.GetOrCreateNode(library.Asset)[nameof(UILibraryAsset.PublicUIElements)].Target!;
        observedLibraries.Add(node, library);
        node.ItemChanged += LibraryContentChanged;
    }

    private void RemoveLibrary(UILibraryViewModel library)
    {
        factories.RemoveWhere(l => l.Category == library.Url);
        foreach (var node in observedLibraries.Where(x => x.Value == library).Select(x => x.Key).ToArray())
        {
            node.ItemChanged -= LibraryContentChanged;
            observedLibraries.Remove(node);
        }
    }

    private void LibraryContentChanged(object? sender, ItemChangeEventArgs e)
    {
        if (!observedLibraries.TryGetValue(e.Collection, out var library)) return;
        var id = (Guid)e.Index.Value!;
        factories.RemoveWhere(f => f is UIElementFromLibrary item && item.Library == library && item.Id == id);
        if (library.Asset.PublicUIElements.ContainsKey(id))
            factories.Add(new UIElementFromLibrary(ServiceProvider, library, id));
    }

    private void AssetsCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        switch (e.Action)
        {
            case NotifyCollectionChangedAction.Add:
            case NotifyCollectionChangedAction.Remove:
            case NotifyCollectionChangedAction.Replace:
                e.OldItems?.OfType<UILibraryViewModel>().ForEach(RemoveLibrary);
                e.NewItems?.OfType<UILibraryViewModel>().ForEach(AddLibrary);
                break;
            case NotifyCollectionChangedAction.Reset:
                InitializeLibraries();
                break;
        }
    }

    public void RegisterAllPropagators()
    {
        if (Viewport?.Controller == null)
            return;

        ActiveRoot ??= HierarchyRoot.Children.OfType<UIElementViewModel>().FirstOrDefault();

        var allElements = HierarchyRoot.Children.BreadthFirst(x => x.Children).OfType<UIElementViewModel>().ToList();
        foreach (var element in allElements)
        {
            RegisterPropagator(element);
            if (propagators.TryGetValue(element.AssetSideUIElement.Id, out var prop))
            {
                prop.NotifyGameSidePartAdded();
            }

            var isUnderActiveRoot = ActiveRoot != null && (element.GetRootElementViewModel() == ActiveRoot || element == ActiveRoot);
            if (Viewport.Controller.AdornerService != null)
            {
                Viewport.Controller.AdornerService.AddAdorner(element.AssetSideUIElement.Id, isUnderActiveRoot).Forget();
            }
        }
    }

    private void RegisterPropagator(UIElementViewModel viewModel)
    {
        if (Viewport?.Controller == null)
            return;

        var partId = viewModel.AssetSideUIElement.Id;
        if (propagators.ContainsKey(partId))
            return;

        var propagator = new GameEditorChangePropagator<UIElementDesign, UIElement, UIElementViewModel>(
            Viewport.Controller,
            Session.AssetNodeContainer,
            Dispatcher,
            viewModel,
            viewModel.AssetSideUIElement);

        propagators[partId] = propagator;
    }

    private void UnregisterPropagator(Guid partId)
    {
        if (propagators.Remove(partId, out var propagator))
        {
            propagator.Destroy();
        }
    }

    private async void OnPartAdded(object? sender, AssetPartChangeEventArgs e)
    {
        if (!UIAsset.Hierarchy.Parts.TryGetValue(e.PartId, out var partDesign))
            return;

        if (FindPartViewModel(new AbsoluteId(Asset.Id, e.PartId)) != null)
            return;

        var parentElement = UIAsset.GetParent(partDesign.UIElement);
        UIHierarchyItemViewModel? parentVM = null;
        int index = -1;
        if (parentElement != null)
        {
            parentVM = FindPartViewModel(new AbsoluteId(Asset.Id, parentElement.Id)) as UIHierarchyItemViewModel;
            index = parentElement is Panel panel ? panel.Children.IndexOf(partDesign.UIElement) : 0;
        }
        else
        {
            parentVM = HierarchyRoot;
            index = UIAsset.Hierarchy.RootParts.IndexOf(partDesign.UIElement);
        }

        if (parentVM == null)
            return;

        var viewModel = Asset.CreatePartViewModel(partDesign);
        parentVM.InsertChild(index, viewModel);
        UpdateCommands();

        Asset.AssetItem.IsDirty = true;

        if (Viewport?.SceneInitialized == true && Viewport.Controller != null)
        {
            var isUnderActiveRoot = ActiveRoot != null && (viewModel.GetRootElementViewModel() == ActiveRoot || viewModel == ActiveRoot);
            var allNewElements = viewModel.Children.BreadthFirst(x => x.Children).OfType<UIElementViewModel>().Concat([viewModel]).ToList();

            foreach (var elem in allNewElements)
            {
                RegisterPropagator(elem);
            }

            try { await Viewport.Controller.AddPart(parentVM, partDesign.UIElement); }
            catch (OperationCanceledException) when (destroying) { return; }
            catch (ObjectDisposedException) when (destroying) { return; }
            if (destroying || FindPartViewModel(viewModel.Id) != viewModel) return;

            foreach (var elem in allNewElements)
            {
                if (propagators.TryGetValue(elem.AssetSideUIElement.Id, out var prop))
                {
                    prop.NotifyGameSidePartAdded();
                }
                if (Viewport.Controller.AdornerService != null)
                {
                    Viewport.Controller.AdornerService.AddAdorner(elem.AssetSideUIElement.Id, isUnderActiveRoot).Forget();
                }
            }
        }
    }

    private void OnPartRemoved(object? sender, AssetPartChangeEventArgs e)
    {
        var partId = new AbsoluteId(Asset.Id, e.PartId);
        if (FindPartViewModel(partId) is not UIElementViewModel viewModel)
            return;

        if (ActiveRoot == viewModel)
        {
            ActiveRoot = null;
        }

        foreach (var selected in SelectedItems.OfType<UIElementViewModel>().Where(x => x == viewModel || Ancestors(x).Contains(viewModel)).ToList())
            SelectedContent.Remove(selected);

        var elementsToRemove = viewModel.Children.BreadthFirst(x => x.Children).OfType<UIElementViewModel>().Concat([viewModel]).ToList();
        var elementIdsToRemove = elementsToRemove.Select(x => x.AssetSideUIElement.Id).ToList();

        foreach (var element in elementsToRemove)
        {
            UnregisterPropagator(element.AssetSideUIElement.Id);
        }

        var parentVM = viewModel.Parent as UIHierarchyItemViewModel;
        var assetElement = viewModel.AssetSideUIElement;

        parentVM?.RemoveChild(viewModel);
        UpdateCommands();
        viewModel.Destroy();

        Asset.AssetItem.IsDirty = true;

        if (Viewport?.SceneInitialized == true && Viewport.Controller != null)
        {
            if (parentVM != null)
            {
                Viewport.Controller.RemovePart(parentVM, assetElement).Forget();
            }

            if (Viewport.Controller.AdornerService != null)
            {
                foreach (var id in elementIdsToRemove)
                {
                    Viewport.Controller.AdornerService.RemoveAdorner(id).Forget();
                }
            }
        }
    }
}
