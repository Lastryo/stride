// Copyright (c) .NET Foundation and Contributors (https://dotnetfoundation.org/ & https://stride3d.net)
// Distributed under the MIT license. See the LICENSE.md file in the project root for more information.

using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Point = Avalonia.Point;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Stride.Assets.Editor.GameEditor;
using Stride.Assets.Editor.ViewModels;
using Stride.Assets.Presentation.ViewModels;
using Stride.Core.Assets.Editor.Annotations;
using Stride.Core.Assets.Editor.Avalonia;
using Stride.Core.Assets.Editor.Editors;
using Stride.Core.Assets.Presentation.ViewModels;
using Stride.Core.Mathematics;
using Stride.Core.Presentation.Avalonia.Extensions;

namespace Stride.Assets.Editor.Avalonia.Views;

[AssetEditorView<EntityHierarchyEditorViewModel>]
public partial class EntityHierarchyEditorView : UserControl, IAssetEditorView
{
    private bool isCtrlDown;
    private Point dragStartPoint;
    private EntityHierarchyItemViewModel? dragCandidateItem;
    private bool isDragging;
    private readonly List<EntityHierarchyItemViewModel> draggedItems = [];
    private TreeViewItem? currentHoveredItem;
    private DropPosition currentDropPosition;
    private EntityHierarchyEditorViewModel? currentEditor;
    private int bringToViewToken;

    public EntityHierarchyEditorView()
    {
        InitializeComponent();
        AddHandler(PointerPressedEvent, (s, e) => isCtrlDown = (e.KeyModifiers & KeyModifiers.Control) != 0, RoutingStrategies.Tunnel);
        AddHandler(PointerReleasedEvent, (s, e) => isCtrlDown = (e.KeyModifiers & KeyModifiers.Control) != 0, RoutingStrategies.Tunnel);

        HierarchyTreeView.AddHandler(PointerPressedEvent, OnHierarchyPointerPressed, RoutingStrategies.Tunnel);
        HierarchyTreeView.AddHandler(PointerMovedEvent, OnHierarchyPointerMoved, RoutingStrategies.Tunnel);
        HierarchyTreeView.AddHandler(PointerReleasedEvent, OnHierarchyPointerReleased, RoutingStrategies.Tunnel);
        HierarchyTreeView.AddHandler(PointerCaptureLostEvent, OnHierarchyPointerCaptureLost, RoutingStrategies.Tunnel);

        DragDrop.SetAllowDrop(HierarchyTreeView, true);
        HierarchyTreeView.AddHandler(DragDrop.DragOverEvent, OnTreeDragOver);
        HierarchyTreeView.AddHandler(DragDrop.DragLeaveEvent, OnTreeDragLeave);
        HierarchyTreeView.AddHandler(DragDrop.DropEvent, OnTreeDrop);

        DragDrop.SetAllowDrop(ViewportHostControl, true);
        ViewportHostControl.AddHandler(DragDrop.DragOverEvent, OnViewportDragOver);
        ViewportHostControl.AddHandler(DragDrop.DropEvent, OnViewportDrop);
        ViewportHostControl.AddHandler(KeyDownEvent, OnEditorKeyDown, RoutingStrategies.Tunnel);
        ViewportHostControl.AddHandler(ContextRequestedEvent, (sender, e) =>
        {
            // Pointer menus are opened below after distinguishing a click from a camera drag.
            if (e.TryGetPosition(ViewportHostControl, out _)) e.Handled = true;
        }, RoutingStrategies.Tunnel);
        Point? rightClickStart = null;
        var rightClickMoved = false;
        ViewportHostControl.AddHandler(PointerPressedEvent, (_, e) =>
        {
            rightClickStart = e.GetCurrentPoint(ViewportHostControl).Properties.IsRightButtonPressed
                && e.KeyModifiers == KeyModifiers.None ? e.GetPosition(ViewportHostControl) : null;
            rightClickMoved = false;
        }, RoutingStrategies.Tunnel);
        ViewportHostControl.AddHandler(PointerMovedEvent, (_, e) =>
        {
            if (rightClickStart is { } start)
            {
                var delta = e.GetPosition(ViewportHostControl) - start;
                if (Math.Abs(delta.X) > 4 || Math.Abs(delta.Y) > 4) rightClickMoved = true;
            }
        }, RoutingStrategies.Tunnel);
        // The game view handles pointer releases; open the menu after it releases camera capture.
        ViewportHostControl.AddHandler(PointerReleasedEvent, (_, e) =>
        {
            if (e.InitialPressMouseButton == MouseButton.Right && rightClickStart != null && !rightClickMoved)
            {
                ViewportHostControl.ContextMenu?.Open(ViewportHostControl);
                e.Handled = true;
            }
            rightClickStart = null;
        }, RoutingStrategies.Bubble, handledEventsToo: true);

        DataContextChanged += OnDataContextChanged;
    }

    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        HookEditor(DataContext as EntityHierarchyEditorViewModel);
    }

    protected override async void OnLoaded(RoutedEventArgs e)
    {
        base.OnLoaded(e);
        HookEditor(DataContext as EntityHierarchyEditorViewModel);
        if (DataContext is not EntityHierarchyEditorViewModel editor) return;
        try { await editor.OpenViewport(); }
        catch (OperationCanceledException) { }
        catch (Exception exception)
        {
            if (editor.Viewport != null) editor.Viewport.LastException = exception;
        }
    }

    protected override void OnUnloaded(RoutedEventArgs e)
    {
        UnhookEditor();
        base.OnUnloaded(e);
    }

    private void HookEditor(EntityHierarchyEditorViewModel? editor)
    {
        if (ReferenceEquals(currentEditor, editor))
            return;

        UnhookEditor();

        currentEditor = editor;
        if (currentEditor != null)
        {
            currentEditor.SelectedContent.CollectionChanged += OnSelectedContentChanged;
            currentEditor.SelectionFocusRequested += OnSelectionFocusRequested;
            if (currentEditor.SelectedContent.Count > 0)
            {
                var last = currentEditor.SelectedContent.LastOrDefault();
                if (last != null)
                    _ = BringItemToView(last);
            }
        }
    }

    private void UnhookEditor()
    {
        if (currentEditor != null)
        {
            currentEditor.SelectedContent.CollectionChanged -= OnSelectedContentChanged;
            currentEditor.SelectionFocusRequested -= OnSelectionFocusRequested;
            currentEditor.Viewport?.HideGame();
            currentEditor = null;
        }
    }

    private void OnSelectedContentChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.Action is NotifyCollectionChangedAction.Add or NotifyCollectionChangedAction.Reset)
        {
            object? target = null;
            if (e.NewItems != null && e.NewItems.Count > 0)
            {
                target = e.NewItems[e.NewItems.Count - 1];
            }
            else if (e.Action == NotifyCollectionChangedAction.Reset && currentEditor?.SelectedContent.Count > 0)
            {
                target = currentEditor.SelectedContent.LastOrDefault();
            }

            if (target != null)
            {
                _ = BringItemToView(target);
            }
        }
    }

    public Task<bool> BringItemToView(object item)
    {
        if (isDragging)
            return Task.FromResult(false);

        var token = ++bringToViewToken;
        return HierarchyTreeView.BringItemToView(item, ResolveHierarchyParent, () => token != bringToViewToken);
    }

    public static object? ResolveHierarchyParent(object? item)
    {
        return item switch
        {
            EntityHierarchyItemViewModel hierarchyItem => hierarchyItem.Parent,
            IChildViewModel child => child.GetParent(),
            _ => null
        };
    }

    private void OnSelectionFocusRequested(object? sender, EventArgs e)
    {
        Dispatcher.UIThread.Post(() =>
        {
            if (IsLoaded && ReferenceEquals(sender, DataContext) && ViewportHostControl.Content is Control viewport)
                viewport.Focus();
        }, DispatcherPriority.Input);
    }

    private void OnHierarchyContextMenuOpened(object? sender, RoutedEventArgs e)
    {
        if (sender is ContextMenu contextMenu)
            contextMenu.DataContext = DataContext;
    }

    private async void ExpandAll_OnClick(object? sender, RoutedEventArgs e)
    {
        SetExpanded(true);
        await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Loaded);
        SetExpanded(true);
    }

    private void CollapseAll_OnClick(object? sender, RoutedEventArgs e) => SetExpanded(false);

    private void WorldSpace_OnClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is EntityHierarchyEditorViewModel vm && vm.Viewport != null)
            vm.Viewport.Space = TransformationSpace.WorldSpace;
    }

    private void ObjectSpace_OnClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is EntityHierarchyEditorViewModel vm && vm.Viewport != null)
            vm.Viewport.Space = TransformationSpace.ObjectSpace;
    }

    private void ViewSpace_OnClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is EntityHierarchyEditorViewModel vm && vm.Viewport != null)
            vm.Viewport.Space = TransformationSpace.ViewSpace;
    }

    private void CameraResetFront_OnClick(object? sender, RoutedEventArgs e) => (DataContext as EntityHierarchyEditorViewModel)?.Viewport?.ResetCameraOrientation(CameraOrientation.Front);
    private void CameraResetBack_OnClick(object? sender, RoutedEventArgs e) => (DataContext as EntityHierarchyEditorViewModel)?.Viewport?.ResetCameraOrientation(CameraOrientation.Back);
    private void CameraResetTop_OnClick(object? sender, RoutedEventArgs e) => (DataContext as EntityHierarchyEditorViewModel)?.Viewport?.ResetCameraOrientation(CameraOrientation.Top);
    private void CameraResetBottom_OnClick(object? sender, RoutedEventArgs e) => (DataContext as EntityHierarchyEditorViewModel)?.Viewport?.ResetCameraOrientation(CameraOrientation.Bottom);
    private void CameraResetLeft_OnClick(object? sender, RoutedEventArgs e) => (DataContext as EntityHierarchyEditorViewModel)?.Viewport?.ResetCameraOrientation(CameraOrientation.Left);
    private void CameraResetRight_OnClick(object? sender, RoutedEventArgs e) => (DataContext as EntityHierarchyEditorViewModel)?.Viewport?.ResetCameraOrientation(CameraOrientation.Right);

    private void SnapIncrease_OnClick(object? sender, RoutedEventArgs e) => ChangeSnapStep(sender, 1);
    private void SnapDecrease_OnClick(object? sender, RoutedEventArgs e) => ChangeSnapStep(sender, -1);

    private static void ChangeSnapStep(object? sender, double change)
    {
        if (sender is Visual visual && visual.FindAncestorOfType<Stride.Core.Presentation.Avalonia.Controls.NumericTextBox>() is { Value: { } value } field)
            field.SetCurrentValue(Stride.Core.Presentation.Avalonia.Controls.NumericTextBox.ValueProperty, Math.Clamp(value + change, field.Minimum, field.Maximum));
    }

    private void SetExpanded(bool expanded)
    {
        foreach (var item in HierarchyTreeView.GetVisualDescendants().OfType<TreeViewItem>())
            item.IsExpanded = expanded;
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.Key is Key.LeftCtrl or Key.RightCtrl) isCtrlDown = true;
        OnEditorKeyDown(this, e);
    }

    private void OnEditorKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Source is Visual source && source.FindAncestorOfType<TextBox>(true) != null) return;
        if (e.Key == Key.D && e.KeyModifiers == KeyModifiers.Control && DataContext is EntityHierarchyEditorViewModel duplicateEditor
            && duplicateEditor.DuplicateCommand.CanExecute(null))
        {
            e.Handled = true;
            duplicateEditor.DuplicateCommand.Execute(null);
            return;
        }
        if (e.Key == Key.Delete && DataContext is EntityHierarchyEditorViewModel editor && editor.DeleteCommand.CanExecute(null))
        {
            e.Handled = true;
            editor.DeleteCommand.Execute(null);
        }
    }

    protected override void OnKeyUp(KeyEventArgs e)
    {
        base.OnKeyUp(e);
        if (e.Key is Key.LeftCtrl or Key.RightCtrl) isCtrlDown = false;
    }

    private void LockButton_OnClick(object? sender, RoutedEventArgs e)
    {
        if (sender is Button { DataContext: EntityHierarchyItemViewModel item })
        {
            item.ToggleLock(isCtrlDown);
        }
    }

    private void LoadButton_OnClick(object? sender, RoutedEventArgs e)
    {
        if (sender is Button { DataContext: EntityHierarchyItemViewModel item })
        {
            item.ToggleLoad(isCtrlDown);
        }
    }

    private void OnHierarchyPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        var point = e.GetCurrentPoint(HierarchyTreeView);
        if (point.Properties.IsRightButtonPressed)
        {
            var targetItem = (e.Source as Visual)?.FindAncestorOfType<TreeViewItem>(true);
            if (targetItem?.DataContext is EntityHierarchyItemViewModel rightClickedItem && DataContext is EntityHierarchyEditorViewModel editor)
            {
                if (!editor.SelectedContent.Contains(rightClickedItem))
                {
                    editor.SelectedContent.Clear();
                    editor.SelectedContent.Add(rightClickedItem);
                }
            }
            return;
        }

        if (!point.Properties.IsLeftButtonPressed)
            return;

        if (e.Source is Visual sourceVisual && sourceVisual.FindAncestorOfType<Button>(true) != null)
            return;

        var treeItem = (e.Source as Visual)?.FindAncestorOfType<TreeViewItem>(true);
        if (treeItem?.DataContext is EntityHierarchyItemViewModel item &&
            item is not (SceneRootViewModel or PrefabRootViewModel))
        {
            dragStartPoint = e.GetPosition(HierarchyTreeView);
            dragCandidateItem = item;
        }
        else
        {
            dragCandidateItem = null;
        }
    }

    private void OnHierarchyPointerMoved(object? sender, PointerEventArgs e)
    {
        if (dragCandidateItem == null || DataContext is not EntityHierarchyEditorViewModel editor)
            return;

        var currentPoint = e.GetPosition(HierarchyTreeView);

        if (!isDragging)
        {
            if (!e.GetCurrentPoint(HierarchyTreeView).Properties.IsLeftButtonPressed)
            {
                dragCandidateItem = null;
                return;
            }

            var diff = currentPoint - dragStartPoint;
            if (Math.Abs(diff.X) > 4 || Math.Abs(diff.Y) > 4)
            {
                isDragging = true;
                draggedItems.Clear();
                if (editor.SelectedContent.Contains(dragCandidateItem))
                {
                    draggedItems.AddRange(editor.SelectedContent.OfType<EntityHierarchyItemViewModel>()
                        .Where(x => x is not (SceneRootViewModel or PrefabRootViewModel)));
                }
                else
                {
                    draggedItems.Add(dragCandidateItem);
                }
                e.Pointer.Capture(HierarchyTreeView);
            }
        }

        if (isDragging)
        {
            var (targetItem, position, targetTreeItem) = GetDropTarget(editor, currentPoint, v => e.GetPosition(v));
            var canMove = targetItem != null && editor.CanMoveItems(draggedItems, targetItem, position, out _);

            if (canMove && targetTreeItem != null)
            {
                UpdateDragFeedback(targetTreeItem, position);
            }
            else if (canMove && targetItem == editor.HierarchyRoot)
            {
                UpdateDragFeedback(null, position);
            }
            else
            {
                ClearDragFeedback();
                HierarchyTreeView.Cursor = new Cursor(StandardCursorType.No);
            }
            e.Handled = true;
        }
    }

    private void OnHierarchyPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (isDragging && DataContext is EntityHierarchyEditorViewModel editor)
        {
            var currentPoint = e.GetPosition(HierarchyTreeView);
            var (targetItem, position, _) = GetDropTarget(editor, currentPoint, v => e.GetPosition(v));

            ClearDragFeedback();
            e.Pointer.Capture(null);

            if (targetItem != null && editor.CanMoveItems(draggedItems, targetItem, position, out _))
            {
                editor.MoveItems(draggedItems, targetItem, position);
            }
            e.Handled = true;
        }

        isDragging = false;
        dragCandidateItem = null;
        draggedItems.Clear();
        ClearDragFeedback();
    }

    private void OnHierarchyPointerCaptureLost(object? sender, PointerCaptureLostEventArgs e)
    {
        if (isDragging)
        {
            ClearDragFeedback();
            isDragging = false;
            dragCandidateItem = null;
            draggedItems.Clear();
        }
    }

    private void OnTreeDragOver(object? sender, DragEventArgs e)
    {
        if (DataContext is not EntityHierarchyEditorViewModel editor)
        {
            e.DragEffects = DragDropEffects.None;
            return;
        }

        var assets = GetAssetsFromDrag(e);
        if (assets.Count == 0)
        {
            return;
        }

        var (targetItem, position, targetTreeItem) = GetDropTarget(editor, e.GetPosition(HierarchyTreeView), v => e.GetPosition(v));
        var canDrop = editor.CanDropAssets(assets, targetItem, position, out _);
        if (!canDrop)
        {
            ClearDragFeedback();
            e.DragEffects = DragDropEffects.None;
            return;
        }

        UpdateDragFeedback(targetTreeItem, position);
        e.DragEffects = DragDropEffects.Copy;
        e.Handled = true;
    }

    private void OnTreeDragLeave(object? sender, DragEventArgs e)
    {
        ClearDragFeedback();
    }

    private void OnTreeDrop(object? sender, DragEventArgs e)
    {
        ClearDragFeedback();
        if (DataContext is not EntityHierarchyEditorViewModel editor)
            return;

        var assets = GetAssetsFromDrag(e);
        if (assets.Count == 0)
            return;

        var (targetItem, position, _) = GetDropTarget(editor, e.GetPosition(HierarchyTreeView), v => e.GetPosition(v));
        if (editor.CanDropAssets(assets, targetItem, position, out _))
        {
            editor.DropAssets(assets, targetItem, position);
            e.DragEffects = DragDropEffects.Copy;
            e.Handled = true;
        }
    }

    private void OnViewportDragOver(object? sender, DragEventArgs e)
    {
        if (DataContext is not EntityHierarchyEditorViewModel editor)
        {
            e.DragEffects = DragDropEffects.None;
            return;
        }

        var assets = GetAssetsFromDrag(e);
        if (assets.Count == 0 || !editor.CanDropAssets(assets, editor.HierarchyRoot, DropPosition.Into, out _))
        {
            e.DragEffects = DragDropEffects.None;
            return;
        }

        e.DragEffects = DragDropEffects.Copy;
        e.Handled = true;
    }

    private async void OnViewportDrop(object? sender, DragEventArgs e)
    {
        if (DataContext is not EntityHierarchyEditorViewModel editor)
            return;

        var assets = GetAssetsFromDrag(e);
        if (assets.Count == 0 || !editor.CanDropAssets(assets, editor.HierarchyRoot, DropPosition.Into, out _))
            return;

        var worldPos = Vector3.Zero;
        if (editor.Viewport?.Controller != null)
        {
            try
            {
                worldPos = await editor.Viewport.Controller.GetMousePositionInSceneAsync(false);
                if (editor.Viewport.Snap && editor.Viewport.TranslationSnap > 0)
                {
                    worldPos = MathUtil.Snap(worldPos, editor.Viewport.TranslationSnap);
                }
            }
            catch
            {
                // Fallback to origin if mouse projection fails
            }
        }

        editor.DropAssets(assets, editor.HierarchyRoot, DropPosition.Into, worldPos);
        e.DragEffects = DragDropEffects.Copy;
        e.Handled = true;
    }

    private static IReadOnlyList<AssetViewModel> GetAssetsFromDrag(DragEventArgs e)
    {
        if (EditorDragDrop.CurrentDragData is IReadOnlyList<AssetViewModel> assetList)
            return assetList;

        if (EditorDragDrop.CurrentDragData is AssetViewModel singleAsset)
            return [singleAsset];

        if (EditorDragDrop.CurrentDragData is IEnumerable<AssetViewModel> assetEnum)
            return assetEnum.ToList();

        return [];
    }

    private (EntityHierarchyItemViewModel? TargetItem, DropPosition Position, TreeViewItem? TargetTreeItem) GetDropTarget(
        EntityHierarchyEditorViewModel editor,
        Point treeViewPoint,
        Func<Visual, Point> getRelativePoint)
    {
        var targetTreeItem = HierarchyTreeView.GetVisualsAt(treeViewPoint)
            .Select(v => v as TreeViewItem ?? v.FindAncestorOfType<TreeViewItem>())
            .FirstOrDefault(v => v != null);

        var targetItem = targetTreeItem?.DataContext as EntityHierarchyItemViewModel;
        DropPosition position;

        if (targetTreeItem != null && targetItem != null)
        {
            if (targetItem is SceneRootViewModel or PrefabRootViewModel)
            {
                position = DropPosition.Into;
            }
            else
            {
                var itemPos = getRelativePoint(targetTreeItem);
                var itemHeight = targetTreeItem.Bounds.Height;
                if (itemHeight > 0 && itemPos.Y < itemHeight * 0.25)
                    position = DropPosition.Before;
                else if (itemHeight > 0 && itemPos.Y > itemHeight * 0.75)
                    position = DropPosition.After;
                else
                    position = DropPosition.Into;
            }
        }
        else
        {
            targetItem = editor.HierarchyRoot;
            position = DropPosition.Into;
        }

        return (targetItem, position, targetTreeItem);
    }

    private void UpdateDragFeedback(TreeViewItem? targetTreeItem, DropPosition position)
    {
        if (currentHoveredItem != targetTreeItem || currentDropPosition != position)
        {
            ClearDragFeedback();
        }

        if (targetTreeItem != null)
        {
            currentHoveredItem = targetTreeItem;
            currentDropPosition = position;
            switch (position)
            {
                case DropPosition.Into:
                    targetTreeItem.Classes.Add("drag-over-into");
                    break;
                case DropPosition.Before:
                    targetTreeItem.Classes.Add("drag-over-before");
                    break;
                case DropPosition.After:
                    targetTreeItem.Classes.Add("drag-over-after");
                    break;
            }
            HierarchyTreeView.Cursor = new Cursor(StandardCursorType.DragMove);
        }
        else
        {
            HierarchyTreeView.Cursor = new Cursor(StandardCursorType.DragMove);
        }
    }

    private void ClearDragFeedback()
    {
        if (currentHoveredItem != null)
        {
            currentHoveredItem.Classes.Remove("drag-over-into");
            currentHoveredItem.Classes.Remove("drag-over-before");
            currentHoveredItem.Classes.Remove("drag-over-after");
            currentHoveredItem = null;
        }
        HierarchyTreeView.Cursor = null;
    }
}
