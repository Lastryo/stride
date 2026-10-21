// Copyright (c) .NET Foundation and Contributors (https://dotnetfoundation.org/ & https://stride3d.net) and Silicon Studio Corp. (https://www.siliconstudio.co.jp)
// Distributed under the MIT license. See the LICENSE.md file in the project root for more information.

using System;
using System.Linq;
using System.ComponentModel;
using System.Collections.Specialized;
using Stride.Core.Presentation.Avalonia.Extensions;
using Avalonia;
using Avalonia.VisualTree;
using Stride.Assets.Presentation.ViewModels;
using Stride.Core.Assets.Editor.Avalonia;
using Stride.Core;
using Stride.Core.Extensions;
using Stride.Core.Mathematics;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Stride.Assets.Editor.UIEditor.Adorners;
using Stride.Assets.Editor.UIEditor.ViewModels;
using Stride.Assets.Editor.ViewModels;

namespace Stride.Assets.Editor.Avalonia.Views;

public partial class UIEditorView : UserControl
{
    private readonly TaskCompletionSource editorInitializationNotifier = new();
    private UIEditorBaseViewModel? currentEditor;
    private bool isInitialized;

    public UIEditorView()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
        AddHandler(KeyDownEvent, OnEditorKeyDown, RoutingStrategies.Tunnel);
        HierarchyTreeView.AddHandler(PointerPressedEvent, OnDragPressed, RoutingStrategies.Tunnel);
        FactoryListBox.AddHandler(PointerPressedEvent, OnDragPressed, RoutingStrategies.Tunnel);
        HierarchyTreeView.PointerMoved += OnDragMoved;
        FactoryListBox.PointerMoved += OnDragMoved;
        HierarchyTreeView.PointerReleased += (_, _) => dragStart = null;
        FactoryListBox.PointerReleased += (_, _) => dragStart = null;
        foreach (var target in new Control[] { HierarchyTreeView, ViewportHostControl })
        {
            DragDrop.SetAllowDrop(target, true);
            target.AddHandler(DragDrop.DragOverEvent, OnDragOver);
            target.AddHandler(DragDrop.DropEvent, OnDrop);
            target.AddHandler(DragDrop.DragLeaveEvent, (_, _) => ClearDropFeedback());
        }
        ViewportHostControl.AddHandler(PointerPressedEvent, (_, _) =>
        {
            (currentEditor?.Viewport?.EditorHost as Control)?.Focus();
        }, RoutingStrategies.Tunnel);
        ViewportHostControl.AddHandler(PointerReleasedEvent, (_, e) =>
        {
            if (e.InitialPressMouseButton == MouseButton.Right)
            {
                ViewportHostControl.ContextMenu?.Open(ViewportHostControl);
                e.Handled = true;
            }
        }, RoutingStrategies.Bubble, handledEventsToo: true);
        GotFocus += async (_, _) =>
        {
            if (currentEditor != null)
            {
                await currentEditor.RefreshClipboard();
                await currentEditor.RefreshPropertiesAsync();
            }
        };
    }

    public Task EditorInitialization => editorInitializationNotifier.Task;

    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        InitializeEditor(DataContext as UIEditorBaseViewModel);
    }

    protected override void OnLoaded(RoutedEventArgs e)
    {
        base.OnLoaded(e);
        InitializeEditor(DataContext as UIEditorBaseViewModel);
    }

    protected override void OnUnloaded(RoutedEventArgs e)
    {
        UnhookEditor();
        base.OnUnloaded(e);
    }

    private async void InitializeEditor(UIEditorBaseViewModel? editor)
    {
        if (ReferenceEquals(currentEditor, editor) && isInitialized)
            return;

        UnhookEditor();
        currentEditor = editor;

        if (currentEditor == null)
            return;

        isInitialized = true;

        try
        {
            await currentEditor.OpenViewport();
            HookEditor(currentEditor);
            await currentEditor.RefreshPropertiesAsync();
            editorInitializationNotifier.TrySetResult();
        }
        catch (OperationCanceledException)
        {
            editorInitializationNotifier.TrySetCanceled();
        }
        catch (Exception exception)
        {
            if (currentEditor.Viewport != null)
                currentEditor.Viewport.LastException = exception;
            editorInitializationNotifier.TrySetResult();
        }
    }

    private int selectionVersion;

    private async void OnSelectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        var version = ++selectionVersion;
        var editor = currentEditor;
        if (editor?.LastSelectedElement is not { } selected)
            return;
        await HierarchyTreeView.BringItemToView(selected,
            item => (item as UIElementViewModel)?.Parent as UIElementViewModel,
            () => version != selectionVersion || currentEditor != editor);
    }

    private void HookEditor(UIEditorBaseViewModel editor)
    {
        editor.SelectedContent.CollectionChanged += OnSelectionChanged;
        OnSelectionChanged(null, new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Reset));
        if (editor.Viewport?.Controller != null)
        {
            editor.Viewport.Controller.CursorChanged += OnCursorChanged;
        }
    }

    private void UnhookEditor()
    {
        if (currentEditor != null)
        {
            ++selectionVersion;
            currentEditor.SelectedContent.CollectionChanged -= OnSelectionChanged;
            if (currentEditor.Viewport?.Controller != null)
            {
                currentEditor.Viewport.Controller.CursorChanged -= OnCursorChanged;
            }
            currentEditor.Viewport?.HideGame();
            currentEditor = null;
            isInitialized = false;
        }
    }

    private void OnCursorChanged(UICursorType? cursorType)
    {
        Dispatcher.UIThread.InvokeAsync(() =>
        {
            Cursor = cursorType switch
            {
                UICursorType.SizeAll => new Cursor(StandardCursorType.SizeAll),
                UICursorType.SizeNESW => new Cursor(StandardCursorType.TopRightCorner),
                UICursorType.SizeNWSE => new Cursor(StandardCursorType.TopLeftCorner),
                UICursorType.SizeNS => new Cursor(StandardCursorType.SizeNorthSouth),
                UICursorType.SizeWE => new Cursor(StandardCursorType.SizeWestEast),
                _ => Cursor.Default
            };
        });
    }

    private void OnFactoryDoubleTapped(object? sender, TappedEventArgs e)
    {
        if ((e.Source as Visual)?.FindAncestorOfType<TreeViewItem>(true)?.DataContext is IUIElementFactory factory && DataContext is UIEditorBaseViewModel vm)
        {
            vm.AddElement(factory);
            e.Handled = true;
        }
    }

    private void ExpandAllButton_OnClick(object? sender, RoutedEventArgs e)
    {
        SetExpandedRecursively(HierarchyTreeView, true);
    }

    private void CollapseAllButton_OnClick(object? sender, RoutedEventArgs e)
    {
        SetExpandedRecursively(HierarchyTreeView, false);
    }

    private static void SetExpandedRecursively(ItemsControl parent, bool isExpanded)
    {
        foreach (var item in parent.GetRealizedContainers())
        {
            if (item is TreeViewItem tvi)
            {
                tvi.IsExpanded = isExpanded;
                SetExpandedRecursively(tvi, isExpanded);
            }
        }
    }

    private PointerPressedEventArgs? dragStart;
    private global::Avalonia.Point dragOrigin;
    private object? dragData;
    private TreeViewItem? dropFeedback;

    private async void OnEditorKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Handled || currentEditor == null || (e.Source as Visual)?.FindAncestorOfType<TextBox>(true) != null)
            return;
        var editor = currentEditor;
        if (e.KeyModifiers == KeyModifiers.Control && e.Key is Key.C or Key.X or Key.V)
        {
            e.Handled = true;
            if (e.Key == Key.V) await editor.RefreshClipboard();
            var command = e.Key == Key.C ? editor.CopyCommand : e.Key == Key.X ? editor.CutCommand : editor.PasteCommand;
            if (command.CanExecute(null)) command.Execute(null);
            return;
        }
        var action = e.KeyModifiers == KeyModifiers.Control && e.Key == Key.D ? editor.DuplicateSelectionCommand
            : e.KeyModifiers == KeyModifiers.None && e.Key == Key.Delete ? editor.DeleteCommand
            : e.KeyModifiers == KeyModifiers.None && e.Key == Key.F2 ? editor.LastSelectedElement?.RenameCommand : null;
        if (action?.CanExecute(null) == true)
        {
            e.Handled = true;
            action.Execute(null);
        }
    }

    private void OnRenameAttached(object? sender, VisualTreeAttachmentEventArgs e)
    {
        if (sender is not TextBox { DataContext: UIElementViewModel element } box) return;
        void Changed(object? source, PropertyChangedEventArgs args)
        {
            if (args.PropertyName == nameof(element.IsEditing) && element.IsEditing)
                Dispatcher.UIThread.Post(() => { box.Text = element.Name; box.Focus(); box.SelectAll(); });
        }
        element.PropertyChanged += Changed;
        box.DetachedFromVisualTree += Detached;
        void Detached(object? source, VisualTreeAttachmentEventArgs args)
        {
            element.PropertyChanged -= Changed;
            box.DetachedFromVisualTree -= Detached;
        }
    }

    private void OnRenameKeyDown(object? sender, KeyEventArgs e)
    {
        if (sender is not TextBox { DataContext: UIElementViewModel element } box) return;
        if (e.Key is not (Key.Enter or Key.Escape)) return;
        e.Handled = true;
        if (e.Key == Key.Enter) element.Name = box.Text;
        element.IsEditing = false;
        HierarchyTreeView.Focus();
    }

    private void OnRenameLostFocus(object? sender, RoutedEventArgs e)
    {
        if (sender is TextBox { DataContext: UIElementViewModel { IsEditing: true } element } box)
        {
            element.Name = box.Text;
            element.IsEditing = false;
        }
    }

    private async void OnSelectionMenuOpening(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        if (sender is not ContextMenu menu || currentEditor == null) return;
        PopulatePanelMenus(menu);
        await currentEditor.RefreshClipboard();
    }

    private void PopulatePanelMenus(ContextMenu menu)
    {
        var editor = currentEditor!;
        menu.DataContext = editor;
        foreach (var item in menu.Items.OfType<MenuItem>().Where(x => x.Tag is "Group" or "Layout"))
        {
            item.Items.Clear();
            var command = item.Tag is "Group" ? editor.GroupIntoCommand : editor.ChangeLayoutTypeCommand;
            foreach (var factory in editor.PanelFactories)
                item.Items.Add(new MenuItem { Header = factory.Name, Command = command, CommandParameter = factory });
        }
    }

    private async void OnViewportMenuOpening(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        if (sender is not ContextMenu menu || currentEditor == null) return;
        PopulatePanelMenus(menu);
        var editor = currentEditor;
        var candidates = menu.Items.OfType<MenuItem>().Single(x => x.Tag is "Candidates");
        candidates.Items.Clear();
        await editor.RefreshSelectableElementsAsync();
        foreach (var element in editor.SelectableElements)
            candidates.Items.Add(new MenuItem
            {
                Header = string.IsNullOrEmpty(element.Name) ? element.ElementType.Name : element.Name,
                Command = editor.SetCurrentSelectionCommand, CommandParameter = element.Id
            });
        candidates.IsEnabled = candidates.Items.Count > 0;
        await editor.RefreshClipboard();
    }

    private void OnDragPressed(object? sender, PointerPressedEventArgs e)
    {
        dragStart = null;
        if (currentEditor == null || (e.Source as Visual)?.FindAncestorOfType<TextBox>(true) != null) return;
        var visual = e.Source as Visual;
        var element = visual?.FindAncestorOfType<TreeViewItem>(true)?.DataContext as UIElementViewModel;
        if (e.GetCurrentPoint((Control)sender!).Properties.IsRightButtonPressed)
        {
            if (element != null && !currentEditor.SelectedItems.Contains(element))
            {
                currentEditor.ClearSelection();
                currentEditor.SelectedContent.Add(element);
            }
            return;
        }
        if (!e.GetCurrentPoint((Control)sender!).Properties.IsLeftButtonPressed) return;
        if (element != null && (e.KeyModifiers & (KeyModifiers.Control | KeyModifiers.Shift)) == 0 && !currentEditor.SelectedItems.Contains(element))
        {
            currentEditor.ClearSelection();
            currentEditor.SelectedContent.Add(element);
        }
        dragData = sender == FactoryListBox
            ? visual?.FindAncestorOfType<TreeViewItem>(true)?.DataContext as IUIElementFactory : element;
        if (dragData == null) return;
        dragStart = e;
        dragOrigin = e.GetPosition(this);
    }

    private async void OnDragMoved(object? sender, PointerEventArgs e)
    {
        if (dragStart == null || currentEditor == null || !e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;
        var delta = e.GetPosition(this) - dragOrigin;
        if (Math.Abs(delta.X) < 4 && Math.Abs(delta.Y) < 4) return;
        var start = dragStart;
        dragStart = null;
        EditorDragDrop.CurrentDragData = dragData is UIElementViewModel element
            ? currentEditor.SelectedItems.Contains(element) ? currentEditor.SelectedItems.OfType<UIElementViewModel>().ToArray() : new[] { element }
            : dragData;
        try { await DragDrop.DoDragDropAsync(start, new DataTransfer(), DragDropEffects.Copy | DragDropEffects.Move); }
        finally { EditorDragDrop.CurrentDragData = null; ClearDropFeedback(); }
    }

    private (UIHierarchyItemViewModel Target, int Index, TreeViewItem? Row) TreeDropTarget(DragEventArgs e)
    {
        var row = HierarchyTreeView.GetVisualsAt(e.GetPosition(HierarchyTreeView))
            .Select(x => x as TreeViewItem ?? x.FindAncestorOfType<TreeViewItem>()).FirstOrDefault(x => x != null);
        if (row?.DataContext is not UIElementViewModel element) return (currentEditor!.HierarchyRoot, -1, null);
        // Use the header height: an expanded TreeViewItem includes the entire subtree.
        var header = (Control?)row.GetVisualDescendants().OfType<Border>().FirstOrDefault(x => x.Name == "PART_LayoutRoot") ?? row;
        var y = e.GetPosition(header).Y;
        var height = header.Bounds.Height;
        if (height > 0 && (y < 3.0 || y > height - 3.0) && element.Parent is UIHierarchyItemViewModel parent)
        {
            return (parent, parent.Children.IndexOf(element) + (y >= height - 3.0 ? 1 : 0), row);
        }
        return (element, -1, row);
    }

    private static string GetElementDisplayName(UIHierarchyItemViewModel? item)
    {
        return item switch
        {
            UIElementViewModel element => string.IsNullOrWhiteSpace(element.Name) ? $"[{element.ElementType.Name}]" : element.Name,
            UIPageRootViewModel => "[Page]",
            UIRootViewModel => "[Root]",
            _ => "[Unknown]"
        };
    }

    private (bool CanDrop, string Message, string DraggedName) EvaluateDrop(UIHierarchyItemViewModel target, object? data, int index)
    {
        var draggedName = data switch
        {
            IUIElementFactory factory => factory.Name,
            UIElementViewModel[] elements => elements.Length == 1 ? GetElementDisplayName(elements[0]) : $"{elements.Length} elements",
            UIElementViewModel single => GetElementDisplayName(single),
            _ => "Element"
        };

        if (currentEditor == null || data == null)
            return (false, "Cannot drop here", draggedName);

        var movingElements = data as UIElementViewModel[];
        if (data is UIElementViewModel singleElement)
            movingElements = new[] { singleElement };

        int count = movingElements?.Length ?? 1;

        if (movingElements != null && (movingElements.Contains(target) || Ancestors(target).Any(movingElements.Contains)))
        {
            return (false, "Can't drop a UI element on itself or its children", draggedName);
        }

        var targetName = GetElementDisplayName(target);

        if (target is UIElementViewModel uiElement)
        {
            if (uiElement.AssetSideUIElement is Stride.UI.Panels.Panel)
            {
                var actionVerb = data is IUIElementFactory ? $"Create a {draggedName} under" : "Add to";
                return (true, $"{actionVerb} {targetName}", draggedName);
            }

            if (uiElement.AssetSideUIElement is Stride.UI.Controls.ContentControl)
            {
                var remaining = uiElement.Children.Count(x => movingElements?.Contains(x) != true);
                if (remaining + count <= 1)
                {
                    var actionVerb = data is IUIElementFactory ? $"Create a {draggedName} under" : "Add to";
                    return (true, $"{actionVerb} {targetName}", draggedName);
                }
                return (false, $"{targetName} already has a content", draggedName);
            }

            return (false, $"{targetName} does not support adding or inserting children", draggedName);
        }

        if (target is UIPageRootViewModel pageRoot)
        {
            var remaining = pageRoot.Children.Count(x => movingElements?.Contains(x) != true);
            if (remaining + count <= 1)
            {
                var actionVerb = data is IUIElementFactory ? $"Create a {draggedName} under" : "Add to";
                return (true, $"{actionVerb} {targetName}", draggedName);
            }
            return (false, $"{targetName} does not support adding or inserting children", draggedName);
        }

        if (target is UIRootViewModel)
        {
            var actionVerb = data is IUIElementFactory ? $"Create a {draggedName} under" : "Add to";
            return (true, $"{actionVerb} {targetName}", draggedName);
        }

        return (false, $"{targetName} does not support adding or inserting children", draggedName);
    }

    private static IEnumerable<UIHierarchyItemViewModel> Ancestors(UIHierarchyItemViewModel item)
    {
        for (var p = item.Parent as UIHierarchyItemViewModel; p != null; p = p.Parent as UIHierarchyItemViewModel)
            yield return p;
    }

    private bool CanDrop(UIHierarchyItemViewModel target, object? data)
    {
        return EvaluateDrop(target, data, -1).CanDrop;
    }

    private static bool IsAncestor(UIElementViewModel parent, UIElementViewModel child)
    {
        for (var item = child.Parent; item != null; item = item.Parent)
            if (item == parent) return true;
        return false;
    }

    private async Task<(UIHierarchyItemViewModel? Target, Vector3 Position)> ViewportDropTarget(DragEventArgs e, object? data)
    {
        var editor = currentEditor!;
        if (editor.Viewport?.SceneInitialized != true) return (null, default);
        var controller = editor.Viewport.Controller;
        var point = e.GetPosition(ViewportHostControl);
        var normalized = new Vector2((float)(point.X / ViewportHostControl.Bounds.Width), (float)(point.Y / ViewportHostControl.Bounds.Height));
        var hit = await controller.InvokeAsync(() =>
        {
            var position = controller.GetPositionInUI(normalized);
            return (Position: position, Ids: controller.AdornerService.GetElementIdsAtPosition(ref position).Distinct().ToArray());
        });
        var target = hit.Ids.Select(id => editor.FindPartViewModel(new AbsoluteId(editor.Asset.Id, id)))
            .OfType<UIElementViewModel>().FirstOrDefault(target => CanDrop(target, data));
        if (target == null) return editor.CanInsert(editor.HierarchyRoot, 1) ? (editor.HierarchyRoot, default) : (null, default);
        var local = await controller.InvokeAsync(() => controller.GetContainerPosition(target.GetRootElementViewModel().Id.ObjectId, target.Id.ObjectId, hit.Position));
        return (target, local);
    }

    private void OnDragOver(object? sender, DragEventArgs e)
    {
        e.Handled = true;
        e.DragEffects = DragDropEffects.None;
        if (currentEditor == null) return;
        ClearDropFeedback();
        if (sender == ViewportHostControl)
        {
            if (currentEditor.Viewport?.SceneInitialized != true || EditorDragDrop.CurrentDragData is not (IUIElementFactory or UIElementViewModel[])) return;
            e.DragEffects = EditorDragDrop.CurrentDragData is IUIElementFactory ? DragDropEffects.Copy : DragDropEffects.Move;
            return;
        }

        var dragData = EditorDragDrop.CurrentDragData;
        if (dragData is not (IUIElementFactory or UIElementViewModel or UIElementViewModel[])) return;

        var location = TreeDropTarget(e);
        var eval = EvaluateDrop(location.Target, dragData, location.Index);

        if (location.Row != null)
        {
            dropFeedback = location.Row;
            if (location.Index < 0)
            {
                dropFeedback.Classes.Add(eval.CanDrop ? "drag-accept" : "drag-reject");
            }
            else
            {
                if (eval.CanDrop)
                {
                    dropFeedback.Classes.Add(location.Row?.DataContext is UIElementViewModel rowItem && location.Index == location.Target.Children.IndexOf(rowItem)
                        ? "drag-over-before" : "drag-over-after");
                }
                else
                {
                    dropFeedback.Classes.Add("drag-reject");
                }
            }
        }

        if (DragTooltipBorder != null && DragTooltipTitle != null && DragTooltipMessage != null)
        {
            DragTooltipTitle.Text = eval.DraggedName;
            DragTooltipMessage.Text = eval.Message;
            if (DragTooltipAcceptedIcon != null) DragTooltipAcceptedIcon.IsVisible = eval.CanDrop;
            if (DragTooltipRejectedIcon != null) DragTooltipRejectedIcon.IsVisible = !eval.CanDrop;

            var pos = e.GetPosition(this);
            var left = pos.X + 16;
            var top = pos.Y + 8;
            if (Bounds.Width > 0 && left + 260 > Bounds.Width)
                left = Math.Max(0, pos.X - 268);
            if (Bounds.Height > 0 && top + 50 > Bounds.Height)
                top = Math.Max(0, pos.Y - 58);
            Canvas.SetLeft(DragTooltipBorder, left);
            Canvas.SetTop(DragTooltipBorder, top);
            DragTooltipBorder.IsVisible = true;
        }

        if (eval.CanDrop)
        {
            e.DragEffects = dragData is IUIElementFactory ? DragDropEffects.Copy : DragDropEffects.Move;
        }
    }

    private async void OnDrop(object? sender, DragEventArgs e)
    {
        e.Handled = true;
        ClearDropFeedback();
        var editor = currentEditor;
        var data = EditorDragDrop.CurrentDragData;
        if (editor == null || data == null) return;
        UIHierarchyItemViewModel? target;
        int index = -1;
        Vector3? position = null;
        if (sender == ViewportHostControl)
        {
            var location = await ViewportDropTarget(e, data);
            target = location.Target;
            position = location.Position;
        }
        else
        {
            var treeTarget = TreeDropTarget(e);
            target = treeTarget.Target;
            index = treeTarget.Index;
            var eval = EvaluateDrop(target, data, index);
            if (!eval.CanDrop) return;
        }
        if (target == null) return;
        if (data is IUIElementFactory factory) editor.InsertFactory(factory, target, index, position);
        else if (data is UIElementViewModel[] elements) editor.MoveElements(elements, target, index);
        else if (data is UIElementViewModel single) editor.MoveElements(new[] { single }, target, index);
        if (sender == ViewportHostControl) (editor.Viewport?.EditorHost as Control)?.Focus();
    }

    private void ClearDropFeedback()
    {
        dropFeedback?.Classes.Remove("drag-over-into");
        dropFeedback?.Classes.Remove("drag-over-before");
        dropFeedback?.Classes.Remove("drag-over-after");
        dropFeedback?.Classes.Remove("drag-accept");
        dropFeedback?.Classes.Remove("drag-reject");
        dropFeedback = null;
        if (DragTooltipBorder != null)
            DragTooltipBorder.IsVisible = false;
    }
}
