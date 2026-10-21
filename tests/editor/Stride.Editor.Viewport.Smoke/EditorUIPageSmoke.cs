// Copyright (c) .NET Foundation and Contributors (https://dotnetfoundation.org/ & https://stride3d.net)
// Distributed under the MIT license. See the LICENSE.md file in the project root for more information.

using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Input.Raw;
using Stride.Core.Assets.Editor.Avalonia;
using Avalonia.VisualTree;
using Stride.Assets.Editor.Avalonia.Views;
using Stride.Assets.Editor.UIEditor.ViewModels;
using Stride.Assets.Editor.ViewModels;
using Stride.Assets.Presentation.ViewModels;
using Stride.Assets.UI;
using Stride.Core;
using Stride.Core.Assets;
using Stride.Core.Assets.Editor.Services;
using Stride.Core.Assets.Editor.ViewModels;
using Stride.Core.Mathematics;
using Stride.UI;
using Canvas = Stride.UI.Panels.Canvas;
using TextBlock = Stride.UI.Controls.TextBlock;
using Border = Stride.UI.Controls.Border;

internal static class EditorUIPageSmoke
{
    public static async Task Check(SessionViewModel session, Window window)
    {
        var clipboard = session.ServiceProvider.Get<IClipboardService>();
        var previousText = await clipboard.GetTextAsync();
        try { await CheckEditor(session, window); }
        finally { await clipboard.SetTextAsync(previousText); }
    }

    private static async Task CheckEditor(SessionViewModel session, Window window)
    {
        var project = session.CurrentProject!;
        var history = session.ActionService!;
        var child = new TextBlock { Name = "First", Text = "Hello", Width = 80, Height = 30 };
        var other = new TextBlock { Name = "Second", Width = 80, Height = 30 };
        var container = new Canvas { Name = "Container", Width = 300, Height = 300, Children = { child, other } };
        var content = new Border { Name = "Content", Width = 100, Height = 100 };
        var root = new Canvas { Name = "Root", Width = 800, Height = 600, Children = { container, content } };
        var page = new UIPageAsset();
        page.Hierarchy.RootParts.Add(root);
        foreach (var element in new UIElement[] { root, container, child, other, content })
            page.Hierarchy.Parts.Add(new UIElementDesign(element));
        UIPageViewModel asset;
        using (history.CreateTransaction())
            asset = (UIPageViewModel)project.AddAsset(new AssetItem("UIPageSmoke_" + Guid.NewGuid().ToString("N"), page), project.AssetMountPoint);
        session.EditorCollection.OpenAssetEditor(asset);
        var editor = (UIPageEditorViewModel)session.EditorCollection.ActiveEditor!;
        await Wait(() => window.GetVisualDescendants().OfType<UIEditorView>().Any(), "UI editor view");
        var view = window.GetVisualDescendants().OfType<UIEditorView>().Single();
        await view.EditorInitialization.WaitAsync(TimeSpan.FromSeconds(90));
        if (editor.Viewport?.LastException != null) throw editor.Viewport.LastException;
        if (editor.Viewport?.SceneInitialized != true) throw new Exception("UI viewport did not initialize.");
        UIElementViewModel Item(UIElement element) => (UIElementViewModel)editor.FindPartViewModel(new AbsoluteId(asset.Id, element.Id))!;
        var tree = view.FindControl<TreeView>("HierarchyTreeView")!;
        void Select(UIElement element) { editor.ClearSelection(); editor.Select(element.Id, false); }
        void Press(Key key, KeyModifiers modifiers = KeyModifiers.None, Control? source = null)
            => (source ?? tree).RaiseEvent(new Avalonia.Input.KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = key, KeyModifiers = modifiers });
        async Task GameOrder()
        {
            await Task.Delay(250);
            var ids = await editor.Viewport.Controller.InvokeAsync(() =>
                ((Canvas)editor.Viewport.Controller.FindElement(root.Id, container.Id)!).Children.Select(x => x.Id).ToArray());
            if (!ids.SequenceEqual(container.Children.Select(x => x.Id))) throw new Exception($"Asset/game child order differs. Asset: {string.Join(",", container.Children.Select(x => x.Name + ":" + x.Id))}; game: {string.Join(",", ids)}; tree: {string.Join(",", Item(container).Children.Select(x => x.Name))}");
        }

        editor.ClearSelection();
        if (editor.DeleteCommand.CanExecute(null) || editor.GroupIntoCommand.CanExecute(null)) throw new Exception("Empty selection enables editing commands.");
        Select(child);
        if (!editor.DuplicateSelectionCommand.CanExecute(null) || !editor.CopyCommand.CanExecute(null)) throw new Exception("Selection did not enable commands.");
        await Wait(() => session.ActiveProperties.ViewModel != null, "ActiveProperties for child");
        if (session.ActiveProperties.ViewModel?.RootNode?.Children.Count == 0) throw new Exception("ActiveProperties generated no property nodes for selected UI element.");
        if (editor.CanInsert(Item(child), 1) || editor.CanInsert(Item(container), 1, new[] { Item(root) })
            || editor.CanInsert(editor.HierarchyRoot, 1)) throw new Exception("Invalid hierarchy insertion was accepted.");
        if (!editor.CanInsert(Item(content), 1) || editor.CanInsert(Item(content), 2)) throw new Exception("ContentControl capacity check failed.");

        editor.MoveElements(new[] { Item(other) }, Item(container), 0);
        if (container.Children[0].Id != other.Id) throw new Exception("Sibling reorder failed.");
        await GameOrder();
        history.Undo();
        if (container.Children[0].Id != child.Id) throw new Exception("Sibling reorder Undo failed.");
        await GameOrder();
        history.Redo();
        await GameOrder();
        history.Undo();
        await GameOrder();
        editor.MoveElements(new[] { Item(other) }, Item(content), 0);
        if (content.Content?.Id != other.Id || editor.CanInsert(Item(content), 1)) throw new Exception("Reparent to ContentControl failed.");
        history.Undo();
        await GameOrder();

        var factory = new Factory();
        editor.InsertFactory(factory, Item(container), 1, new Vector3(25, 35, 0));
        if (container.Children.Count != 3 || container.Children[1].Name != "Dropped") throw new Exception("Factory insertion index failed.");
        if (container.Children[1].DependencyProperties.Get(Canvas.AbsolutePositionPropertyKey) != new Vector3(25, 35, 0)) throw new Exception("Canvas drop position was lost.");
        await GameOrder();
        history.Undo();
        await GameOrder();

        Select(child);
        var gameHost = (Control)editor.Viewport.EditorHost!;
        Press(Key.D, KeyModifiers.Control, gameHost);
        if (container.Children.Count != 3 || editor.LastSelectedElement?.Id.ObjectId == child.Id) throw new Exception("Ctrl+D did not select an independent duplicate.");
        Press(Key.Delete, source: gameHost);
        if (container.Children.Count != 2) throw new Exception("Delete shortcut failed.");
        history.Undo();
        history.Undo();
        await GameOrder();

        Select(child);
        Press(Key.C, KeyModifiers.Control);
        Select(container);
        await Wait(() => editor.CanPaste(false), "Ctrl+C clipboard availability");
        if (!editor.CanPaste(false) || editor.CanPaste(true)) throw new Exception("Clipboard/page-root availability failed.");
        Press(Key.V, KeyModifiers.Control);
        await Wait(() => container.Children.Count == 3, "Ctrl+V");
        var pasted = container.Children.Last();
        if (pasted.Id == child.Id || ((TextBlock)pasted).Text != "Hello") throw new Exception("Copy/paste did not preserve properties with new IDs.");
        history.Undo();
        await GameOrder();
        Select(child);
        Press(Key.X, KeyModifiers.Control);
        await Wait(() => container.Children.Count == 1, "Ctrl+X");
        Select(container);
        Press(Key.V, KeyModifiers.Control);
        await Wait(() => container.Children.Count == 2, "paste cut subtree");
        history.Undo();
        history.Undo();
        await GameOrder();

        // Text editing owns clipboard/Delete keys; Escape cancels without an undo entry.
        for (var pass = 0; pass < 3; pass++)
        {
            foreach (var row in tree.GetVisualDescendants().OfType<TreeViewItem>().ToArray()) row.IsExpanded = true;
            await Task.Delay(100);
        }
        Select(child);
        Press(Key.F2);
        await Task.Delay(100);
        var rename = tree.GetVisualDescendants().OfType<TextBox>().Single(box => box.DataContext == Item(child));
        if (!rename.IsVisible || !rename.IsFocused) throw new Exception("F2 did not focus inline rename.");
        rename.Text = "Cancelled";
        Press(Key.Escape, source: rename);
        if (child.Name != "First" || Item(child).IsEditing) throw new Exception("Escape committed a rename.");
        Press(Key.F2);
        await Task.Delay(100);
        rename.Text = "Renamed";
        Press(Key.Enter, source: rename);
        if (child.Name != "Renamed") throw new Exception("Enter did not commit rename.");
        history.Undo();
        if (child.Name != "First") throw new Exception("Rename Undo failed.");
        // Exercise routed drop handlers with the same payload as native palette/tree drags.
        var containerRow = tree.GetVisualDescendants().OfType<TreeViewItem>().Single(row => row.DataContext == Item(container));
        var childRow = tree.GetVisualDescendants().OfType<TreeViewItem>().Single(row => row.DataContext == Item(child));
        var childPoint = childRow.TranslatePoint(new global::Avalonia.Point(40, 12), tree)!.Value;
        var treePoint = containerRow.TranslatePoint(new global::Avalonia.Point(40, 12), tree)!.Value;

        // Test DragOver rejected on leaf TextBlock
        EditorDragDrop.CurrentDragData = factory;
        try
        {
            tree.RaiseEvent(new DragEventArgs(DragDrop.DragOverEvent, new DataTransfer(), tree, childPoint, KeyModifiers.None));
            if (!childRow.Classes.Contains("drag-reject"))
                throw new Exception("DragOver leaf control should add drag-reject class.");
            var tooltipBorder = view.FindControl<global::Avalonia.Controls.Border>("DragTooltipBorder")!;
            var tooltipMsg = view.FindControl<global::Avalonia.Controls.TextBlock>("DragTooltipMessage")!;
            if (!tooltipBorder.IsVisible || !tooltipMsg.Text!.Contains("does not support adding or inserting children"))
                throw new Exception($"Expected rejected tooltip message but got: {tooltipMsg.Text}");
        }
        finally { EditorDragDrop.CurrentDragData = null; }

        // Test DragOver accepted on container Canvas
        EditorDragDrop.CurrentDragData = factory;
        try
        {
            tree.RaiseEvent(new DragEventArgs(DragDrop.DragOverEvent, new DataTransfer(), tree, treePoint, KeyModifiers.None));
            if (!containerRow.Classes.Contains("drag-accept"))
                throw new Exception("DragOver container should add drag-accept class.");
            var tooltipBorder = view.FindControl<global::Avalonia.Controls.Border>("DragTooltipBorder")!;
            var tooltipMsg = view.FindControl<global::Avalonia.Controls.TextBlock>("DragTooltipMessage")!;
            if (!tooltipBorder.IsVisible || !tooltipMsg.Text!.Contains("Create a TextBlock under Container"))
                throw new Exception($"Expected accepted tooltip message but got: {tooltipMsg.Text}");
        }
        finally { EditorDragDrop.CurrentDragData = null; }

        // Verify ShowDisplayName behavior for named vs unnamed elements
        if (!Item(child).ShowDisplayName)
            throw new Exception("Named element should show display name.");
        Item(child).Name = null;
        if (Item(child).ShowDisplayName)
            throw new Exception("Unnamed element should not show display name.");
        Item(child).Name = "First";

        EditorDragDrop.CurrentDragData = factory;
        try { tree.RaiseEvent(new DragEventArgs(DragDrop.DropEvent, new DataTransfer(), tree, treePoint, KeyModifiers.None)); }
        finally { EditorDragDrop.CurrentDragData = null; }
        if (container.Children.Count != 3) throw new Exception("Palette-to-tree routed drop failed.");
        history.Undo();
        await GameOrder();
        var viewportHost = view.FindControl<ContentControl>("ViewportHostControl")!;
        EditorDragDrop.CurrentDragData = factory;
        try
        {
            viewportHost.RaiseEvent(new DragEventArgs(DragDrop.DropEvent, new DataTransfer(), viewportHost,
                new global::Avalonia.Point(viewportHost.Bounds.Width / 2, viewportHost.Bounds.Height / 2), KeyModifiers.None));
        }
        finally { EditorDragDrop.CurrentDragData = null; }
        await Wait(() => page.Hierarchy.Parts.Count == 6, "palette-to-viewport routed drop");
        history.Undo();
        await GameOrder();
        Select(child);
        var pointer = new Pointer(511, PointerType.Mouse, true);
        var point = new global::Avalonia.Point(gameHost.Bounds.Width / 2, gameHost.Bounds.Height / 2);
        gameHost.RaiseEvent(new PointerPressedEventArgs(gameHost, pointer, gameHost, point, 0,
            new PointerPointProperties(RawInputModifiers.RightMouseButton, PointerUpdateKind.RightButtonPressed), KeyModifiers.None, 1));
        gameHost.RaiseEvent(new PointerReleasedEventArgs(gameHost, pointer, gameHost, point, 0,
            new PointerPointProperties(RawInputModifiers.None, PointerUpdateKind.RightButtonReleased), KeyModifiers.None, MouseButton.Right));
        await Wait(() => viewportHost.ContextMenu?.IsOpen == true, "viewport context menu");
        await Task.Delay(200);
        var menu = viewportHost.ContextMenu!;
        var menuItems = menu.Items.OfType<MenuItem>().ToArray();
        if (!menuItems.Single(item => item.Tag is "Group").Items.OfType<MenuItem>().Any(item => item.CommandParameter is IUIElementFactory)
            || !menuItems.Single(item => item.Header is "Pin origin (Canvas)").IsVisible
            || menuItems.Single(item => item.Header is "Grid tools").IsVisible
            || menuItems.Single(item => item.Header is "Ungroup").IsVisible
            || !menuItems.Single(item => item.Header is "View").Items.OfType<MenuItem>().All(item => item.Command != null))
            throw new Exception("Viewport menu commands or type visibility failed.");
        menu.Close();
        await session.ServiceProvider.Get<IClipboardService>().SetTextAsync("plain text");
        await editor.RefreshClipboard();
        if (editor.CanPaste(false)) throw new Exception("Unrelated clipboard data enabled Paste.");
        Console.WriteLine("PASS: UI Page hierarchy, capacity/cycle checks, factory positions, keyboard editing, clipboard, rename, game order and Undo/Redo.");
        await session.EditorCollection.CloseEditorAsync(editor).WaitAsync(TimeSpan.FromSeconds(15));
        Console.WriteLine("PASS: UI Page editor closed and its game stopped.");
    }

    private static async Task Wait(Func<bool> condition, string operation)
    {
        for (var i = 0; i < 200; i++) { if (condition()) return; await Task.Delay(50); }
        throw new TimeoutException(operation);
    }

    private sealed class Factory : IUIElementFactory
    {
        public string Name => "TextBlock";
        public string Category => "Smoke";
        public Type Type => typeof(TextBlock);
        public AssetCompositeHierarchyData<UIElementDesign, UIElement> Create(UIAssetBase targetAsset)
        {
            var element = new TextBlock { Name = "Dropped" };
            var hierarchy = new AssetCompositeHierarchyData<UIElementDesign, UIElement>();
            hierarchy.RootParts.Add(element);
            hierarchy.Parts.Add(new UIElementDesign(element));
            return hierarchy;
        }
    }
}
