// Copyright (c) .NET Foundation and Contributors (https://dotnetfoundation.org/ & https://stride3d.net)
// Distributed under the MIT license. See the LICENSE.md file in the project root for more information.
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Microsoft.CodeAnalysis;
using Stride.Assets.Editor.Avalonia.Controls;
using Stride.Assets.Editor.ViewModels;
using Stride.Assets.Scripts;
using Stride.Core.Assets.Editor.Annotations;
using Stride.Core.Assets.Editor.Editors;
using Int2 = Stride.Core.Mathematics.Int2;

namespace Stride.Assets.Editor.Avalonia.Views;

[AssetEditorView<VisualScriptEditorViewModel>]
public partial class VisualScriptEditorView : UserControl, IAssetEditorView
{
    private CancellationTokenSource? searchCancellation;
    private readonly Popup symbolPopup = new() { IsLightDismissEnabled = true, Placement = PlacementMode.Pointer };
    private PointerPressedEventArgs? dragStart;
    private Symbol? dragSymbol;
    private Point dragOrigin;
    public VisualScriptEditorView()
    {
        InitializeComponent();
        // ListBoxItem handles pointer presses while selecting; observe them before selection.
        foreach (var list in new[] { VariablesList, ParametersList })
        {
            list.AddHandler(PointerPressedEvent, SymbolPointerPressed, RoutingStrategies.Tunnel, handledEventsToo: true);
            list.AddHandler(PointerMovedEvent, SymbolPointerMoved, RoutingStrategies.Tunnel, handledEventsToo: true);
            list.AddHandler(PointerReleasedEvent, (_, _) => { dragStart = null; dragSymbol = null; }, RoutingStrategies.Tunnel, handledEventsToo: true);
        }
        AccessibilityBox.ItemsSource = Enum.GetValues<Scripts.Accessibility>();
        ModifierBox.ItemsSource = Enum.GetValues<VirtualModifier>();
        Graph.CreateBlock = CreateBlock;
        Graph.VariableDropped += (symbol, position) =>
        {
            var menu = new ContextMenu { ItemsSource = new[] { VariableItem("Get", false), VariableItem("Set", true) } };
            MenuItem VariableItem(string name, bool setter)
            {
                var item = new MenuItem { Header = name };
                item.Click += (_, _) => (DataContext as VisualScriptEditorViewModel)?.AddVariableBlock(symbol, setter, position);
                return item;
            }
            menu.Open(Graph);
        };
        symbolPopup.PlacementTarget = Graph;
        symbolPopup.Closed += (_, _) => searchCancellation?.Cancel();
        DetachedFromVisualTree += (_, _) => { symbolPopup.IsOpen = false; searchCancellation?.Cancel(); };
    }
    public VisualScriptGraph GraphControl => Graph;
    private void AddMethodClicked(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not VisualScriptEditorViewModel editor) return;
        var items = new List<MenuItem>();
        var fresh = new MenuItem { Header = "New method" }; fresh.Click += (_, _) => editor.AddMethod(); items.Add(fresh);
        foreach (var method in editor.OverridableMethods)
        {
            var item = new MenuItem { Header = method.ToDisplayString() }; item.Click += (_, _) => editor.AddMethod(method); items.Add(item);
        }
        new ContextMenu { ItemsSource = items }.Open(AddMethodButton);
    }
    private async Task CreateBlock(VisualScriptBlockTemplate template, Int2 position)
    {
        if (DataContext is not VisualScriptEditorViewModel editor) return;
        if (template.Type == typeof(MethodCallBlock)) { OpenSymbolSearch(editor, position); return; }
        try { editor.AddBlock((Block)Activator.CreateInstance(template.Type)!, position); }
        catch (Exception exception) { ToolTip.SetTip(Graph, exception.Message); ToolTip.SetIsOpen(Graph, true); }
        await Task.CompletedTask;
    }
    public void OpenSymbolSearch(VisualScriptEditorViewModel editor, Int2 position)
    {
        var search = new TextBox { PlaceholderText = "Search methods..." };
        var values = new ListBox { Height = 300, ItemTemplate = new global::Avalonia.Controls.Templates.FuncDataTemplate<IMethodSymbol>((method, _) => new TextBlock { Text = method?.ToDisplayString(), TextWrapping = global::Avalonia.Media.TextWrapping.Wrap, Margin = new Thickness(4) }) };
        void Accept()
        {
            if (values.SelectedItem is not IMethodSymbol method) return;
            symbolPopup.IsOpen = false; editor.AddMethodCall(method, position);
        }
        async Task Search()
        {
            searchCancellation?.Cancel(); var cancellation = searchCancellation = new CancellationTokenSource();
            try
            {
                await Task.Delay(150, cancellation.Token);
                var results = await editor.SearchMethodsAsync(search.Text ?? "", cancellation.Token);
                if (!cancellation.IsCancellationRequested && symbolPopup.IsOpen) { values.ItemsSource = results; if (results.Count > 0) values.SelectedIndex = 0; }
            }
            catch (OperationCanceledException) { }
            finally { if (ReferenceEquals(searchCancellation, cancellation)) searchCancellation = null; cancellation.Dispose(); }
        }
        search.TextChanged += async (_, _) => await Search();
        search.KeyDown += (_, e) => { if (e.Key == Key.Down) { values.SelectedIndex = Math.Max(0, values.SelectedIndex); values.Focus(); e.Handled = true; } else if (e.Key == Key.Enter) { Accept(); e.Handled = true; } };
        values.DoubleTapped += (_, _) => Accept();
        values.KeyDown += (_, e) => { if (e.Key == Key.Enter) { Accept(); e.Handled = true; } };
        symbolPopup.Child = new Border { Width = 320, Padding = new Thickness(4), Background = this.FindResource("SystemControlBackgroundAltHighBrush") as global::Avalonia.Media.IBrush ?? global::Avalonia.Media.Brushes.DimGray, Child = new StackPanel { Children = { search, values } } };
        symbolPopup.IsOpen = true; Dispatcher.UIThread.Post(() => search.Focus()); _ = Search();
    }
    private void EditorKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Handled || e.Source is TextBox || (e.Source as Control)?.FindAncestorOfType<TextBox>() != null) return;
        if (DataContext is not VisualScriptEditorViewModel editor || e.Key != Key.Delete) return;
        if (VariablesList.IsKeyboardFocusWithin) editor.RemoveSelectedPropertiesCommand.Execute(null);
        else if (MethodsList.IsKeyboardFocusWithin) editor.RemoveSelectedMethodCommand.Execute(null);
        else if (ParametersList.IsKeyboardFocusWithin) editor.RemoveSelectedParametersCommand.Execute(null);
        else editor.DeleteSelectionCommand.Execute(null);
        e.Handled = true;
    }
    private void BeginRename(object? sender, TappedEventArgs e)
    {
        if (sender is TextBlock { Parent: Grid row }) ShowEdit(row);
        e.Handled = true;
    }
    private void ListKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.F2 && sender is ListBox { SelectedItem: { } item } list && list.ContainerFromItem(item)?.GetVisualDescendants().OfType<TextBlock>().FirstOrDefault(t => t.Parent is Grid) is { Parent: Grid row })
        { ShowEdit(row); e.Handled = true; }
    }
    private static void ShowEdit(Grid row)
    {
        if (row.Children[0] is not TextBlock label) return;
        label.IsVisible = false; row.Children[1].IsVisible = true;
        var boxes = row.Children[1] is TextBox box ? new[] { box } : ((Grid)row.Children[1]).Children.OfType<TextBox>().ToArray();
        if (row.DataContext is VisualScriptSymbolViewModel symbol) { boxes[0].Text = symbol.Type; boxes[1].Text = symbol.Name; }
        else if (row.DataContext is VisualScriptMethodViewModel method) boxes[0].Text = method.Name;
        boxes[^1].Focus(); boxes[^1].SelectAll();
    }
    private void CommitEdit(object? sender, KeyEventArgs e)
    {
        if (sender is not TextBox box || e.Key is not (Key.Enter or Key.Escape)) return;
        FinishEdit(box, e.Key == Key.Enter); e.Handled = true;
    }
    private void EditLostFocus(object? sender, RoutedEventArgs e)
    {
        if (sender is not TextBox box) return;
        Dispatcher.UIThread.Post(() =>
        {
            var row = EditRow(box);
            if (row != null && !row.Children[1].IsKeyboardFocusWithin) FinishEdit(box, true);
        });
    }
    private static Grid? EditRow(TextBox box) => box.Parent is Grid { Tag: "SymbolEdit", Parent: Grid row } ? row : box.Parent as Grid;
    private void FinishEdit(TextBox box, bool commit)
    {
        var row = EditRow(box);
        if (row == null || row.Children[0].IsVisible) return;
        row.Children[0].IsVisible = true; row.Children[1].IsVisible = false;
        if (!commit || DataContext is not VisualScriptEditorViewModel editor) return;
        if (row.DataContext is VisualScriptMethodViewModel method) method.Name = box.Text ?? method.Name;
        else if (row.DataContext is VisualScriptSymbolViewModel symbol && row.Children[1] is Grid fields)
        {
            var boxes = fields.Children.OfType<TextBox>().ToArray();
            editor.Change("Edit symbol", () =>
            {
                editor.Node(symbol.Symbol)[nameof(Symbol.Type)].Update(boxes[0].Text ?? "bool");
                editor.Node(symbol.Symbol)[nameof(Symbol.Name)].Update(boxes[1].Text ?? "variable");
                if (symbol.Symbol is Parameter parameter && fields.Children.OfType<ComboBox>().FirstOrDefault()?.SelectedItem is ParameterRefKind kind) editor.Node(parameter)[nameof(Parameter.RefKind)].Update(kind);
            });
        }
    }
    private void RefKindLoaded(object? sender, RoutedEventArgs e) { if (sender is ComboBox box) box.ItemsSource = Enum.GetValues<ParameterRefKind>(); }
    private void SymbolPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        dragStart = null; dragSymbol = null;
        if (e.Source is not Control source || source.FindAncestorOfType<TextBox>(true) != null || !e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;
        if (source.FindAncestorOfType<ListBoxItem>(true)?.DataContext is not VisualScriptSymbolViewModel symbol) return;
        dragStart = e; dragOrigin = e.GetPosition(this); dragSymbol = symbol.Symbol;
    }
    private async void SymbolPointerMoved(object? sender, PointerEventArgs e)
    {
        if (dragStart == null || dragSymbol == null || !e.GetCurrentPoint(this).Properties.IsLeftButtonPressed || Math.Abs(e.GetPosition(this).X - dragOrigin.X) < 4 && Math.Abs(e.GetPosition(this).Y - dragOrigin.Y) < 4) return;
        var start = dragStart; dragStart = null;
        var item = new DataTransferItem(); item.Set(VisualScriptGraph.SymbolFormat, dragSymbol);
        var transfer = new DataTransfer(); transfer.Add(item);
        await DragDrop.DoDragDropAsync(start, transfer, DragDropEffects.Copy);
    }
}
