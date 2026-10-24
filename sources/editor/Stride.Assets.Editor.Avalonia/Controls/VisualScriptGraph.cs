// Copyright (c) .NET Foundation and Contributors (https://dotnetfoundation.org/ & https://stride3d.net)
// Distributed under the MIT license. See the LICENSE.md file in the project root for more information.
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Stride.Assets.Editor.ViewModels;
using Stride.Assets.Scripts;
using Int2 = Stride.Core.Mathematics.Int2;
using Path = Avalonia.Controls.Shapes.Path;
using Ellipse = Avalonia.Controls.Shapes.Ellipse;

namespace Stride.Assets.Editor.Avalonia.Controls;

/// <summary>Visual-script graph using the WPF node palette, asset positions and 16-unit snapping.</summary>
public sealed class VisualScriptGraph : Grid
{
    private readonly Canvas world = new() { Width = 1, Height = 1, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top };
    private readonly Dictionary<Slot, Ellipse> ports = new();
    private readonly Dictionary<Block, Border> nodes = new();
    private readonly Dictionary<Block, Point> dragPositions = new();
    private readonly List<Path> edges = new();
    private readonly Dictionary<Guid, (Vector Offset, double Zoom)> cameras = new();
    private VisualScriptEditorViewModel? editor;
    private Guid? methodId;
    private bool pending;
    private bool moving;
    private bool panning;
    private Slot? connecting;
    private Point lastPointer;
    private Point connectionEnd;
    private Point? selectionStart;
    private readonly Border selection = new() { BorderBrush = Brush.Parse("#3699B8"), BorderThickness = new Thickness(1), Background = Brush.Parse("#303699B8"), IsHitTestVisible = false, IsVisible = false };
    private Vector offset;
    private double zoom = 1;
    private string? linkLayout;
    public Func<VisualScriptBlockTemplate, Int2, Task>? CreateBlock { get; set; }
    public event Action<Symbol, Int2>? VariableDropped;
    public Int2 ContextPosition { get; private set; }
    public double ZoomFactor => zoom;
    public VisualScriptGraph()
    {
        Background = Brush.Parse("#49494E"); ClipToBounds = true; Focusable = true;
        Children.Add(world); Children.Add(selection);
        world.RenderTransformOrigin = RelativePoint.TopLeft;
        DataContextChanged += (_, _) => Bind(); AttachedToVisualTree += (_, _) => Bind(); DetachedFromVisualTree += (_, _) => Unbind();
        LayoutUpdated += (_, _) => DrawLinks();
        PointerPressed += BackgroundPressed; PointerMoved += Move; PointerReleased += Release;
        PointerCaptureLost += (_, _) => CancelDrag();
        PointerWheelChanged += (_, e) =>
        {
            var point = e.GetPosition(this); var oldZoom = zoom;
            zoom = Math.Clamp(zoom * Math.Pow(1.1, e.Delta.Y), .1, 4);
            offset = point - (point - offset) * (zoom / oldZoom); ApplyTransform(); e.Handled = true;
        };
        KeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape) { CancelDrag(); e.Handled = true; }
            else if (e.Key == Key.Delete) { editor?.DeleteSelectionCommand.Execute(null); e.Handled = true; }
            else if (e.Key == Key.A && e.KeyModifiers.HasFlag(KeyModifiers.Control) && editor?.SelectedMethod is { } method)
            { foreach (var block in method.Blocks) if (!editor.SelectedBlocks.Contains(block)) editor.SelectedBlocks.Add(block); e.Handled = true; }
        };
        DragDrop.SetAllowDrop(this, true);
        AddHandler(DragDrop.DragOverEvent, (_, e) => e.DragEffects = editor?.SelectedMethod != null && e.DataTransfer.TryGetValue(SymbolFormat) != null ? DragDropEffects.Copy : DragDropEffects.None);
        AddHandler(DragDrop.DropEvent, (_, e) =>
        {
            if (e.DataTransfer.TryGetValue(SymbolFormat) is not Symbol symbol || editor?.SelectedMethod == null) return;
            var target = nodes.FirstOrDefault(p => p.Value.TranslatePoint(default, this) is { } origin && new Rect(origin, p.Value.Bounds.Size * zoom).Contains(e.GetPosition(this))).Key;
            if (target != null)
            {
                var member = editor.Node(target).Members.FirstOrDefault(m => m.MemberDescriptor.GetCustomAttributes<BlockDropTargetAttribute>(true).Any() && m.Type.IsInstanceOfType(symbol.Name));
                if (member != null) { editor.Update(target, member.Name, symbol.Name); e.Handled = true; return; }
            }
            VariableDropped?.Invoke(symbol, WorldPosition(e.GetPosition(this))); e.Handled = true;
        });
    }
    public static DataFormat<Symbol> SymbolFormat { get; } = DataFormat.CreateInProcessFormat<Symbol>("Stride.VisualScript.Symbol");
    private Int2 WorldPosition(Point position) => new((int)Math.Round((position.X - offset.X) / zoom), (int)Math.Round((position.Y - offset.Y) / zoom));
    private void Bind()
    {
        if (ReferenceEquals(editor, DataContext)) return;
        Unbind(); editor = DataContext as VisualScriptEditorViewModel;
        if (editor != null) editor.GraphChanged += Changed;
        Rebuild();
    }
    private void Unbind()
    {
        SaveCamera();
        if (editor != null) editor.GraphChanged -= Changed;
        editor = null; methodId = null; CancelDrag(); ContextMenu = null;
    }
    private void SaveCamera() { if (methodId is { } id) cameras[id] = (offset, zoom); }
    private void Changed(object? sender, EventArgs e)
    {
        if (pending) return;
        pending = true;
        Dispatcher.UIThread.Post(() => { pending = false; if (!moving && !panning && connecting == null && selectionStart == null) Rebuild(); });
    }
    public void Rebuild()
    {
        SaveCamera();
        if (methodId != editor?.SelectedMethod?.Method.Id)
        {
            methodId = editor?.SelectedMethod?.Method.Id;
            var camera = methodId.HasValue ? cameras.GetValueOrDefault(methodId.Value, (new Vector(), 1d)) : (new Vector(), 1d);
            offset = camera.Item1; zoom = camera.Item2; ApplyTransform();
        }
        world.Children.Clear(); nodes.Clear(); ports.Clear(); edges.Clear(); linkLayout = null;
        if (editor?.SelectedMethod is not { } method) { ContextMenu = null; return; }
        foreach (var block in method.Blocks) AddBlock(block);
        var menu = new ContextMenu();
        var search = new TextBox { PlaceholderText = "Search", Margin = new Thickness(4) };
        var categories = new ListBox { MinWidth = 160, ItemsSource = editor.BlockTemplates.Select(t => t.Category).Distinct().ToArray() };
        var list = new ListBox { Width = 240, Height = 300, ItemsSource = editor.BlockTemplates, ItemTemplate = new global::Avalonia.Controls.Templates.FuncDataTemplate<VisualScriptBlockTemplate>((template, _) => new TextBlock { Text = template?.Name, Margin = new Thickness(4) }) };
        void Filter()
        {
            var text = search.Text ?? "";
            categories.IsEnabled = text.Length == 0;
            list.ItemsSource = editor.BlockTemplates.Where(t => (text.Length > 0 || categories.SelectedItem is not string category || t.Category == category) && (t.Category + " " + t.Name).Contains(text, StringComparison.OrdinalIgnoreCase)).ToArray();
        }
        search.TextChanged += (_, _) => Filter(); categories.SelectionChanged += (_, _) => Filter();
        var left = new DockPanel { Margin = new Thickness(4) }; DockPanel.SetDock(search, Dock.Top); left.Children.Add(search); left.Children.Add(categories);
        var browser = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,1,*"), MinHeight = 300 };
        Grid.SetColumn(list, 2); browser.Children.Add(left); browser.Children.Add(list);
        async Task AddSelected()
        {
            if (list.SelectedItem is not VisualScriptBlockTemplate template) return;
            menu.Close(); if (CreateBlock != null) await CreateBlock(template, ContextPosition);
        }
        search.KeyDown += async (_, e) =>
        {
            if (e.Key == Key.Down && list.ItemCount > 0) { list.SelectedIndex = 0; list.Focus(); e.Handled = true; }
            else if (e.Key == Key.Enter) { if (list.SelectedItem == null && list.ItemCount > 0) list.SelectedIndex = 0; await AddSelected(); e.Handled = true; }
        };
        list.DoubleTapped += async (_, _) => await AddSelected();
        list.KeyDown += async (_, e) => { if (e.Key == Key.Enter) { e.Handled = true; await AddSelected(); } };
        menu.ItemsSource = new object[] { new MenuItem { Header = browser, IsEnabled = editor.Asset.IsEditable, StaysOpenOnClick = true } };
        menu.Opening += (_, _) => ContextPosition = WorldPosition(lastPointer);
        menu.Opened += (_, _) => Dispatcher.UIThread.Post(() => search.Focus());
        ContextMenu = menu;
        DrawLinks();
    }
    private void AddBlock(VisualScriptBlockViewModel view)
    {
        var block = view.Block;
        var selected = editor!.SelectedBlocks.Contains(view);
        var diagnostics = editor.Diagnostics.Where(d => d.BlockId == block.Id).ToArray();
        var title = new Border
        {
            Background = Brush.Parse(selected ? "#3699B8" : "#194756"), Padding = new Thickness(1, 2), CornerRadius = new CornerRadius(5, 5, 0, 0),
            Child = new TextBlock { Text = view.Title, Margin = new Thickness(10, 0), FontSize = 15, FontWeight = FontWeight.Bold, Foreground = Brush.Parse("#DCDCDC"), HorizontalAlignment = HorizontalAlignment.Center }
        };
        var slots = new Grid { ColumnDefinitions = new ColumnDefinitions("*,*") };
        slots.ColumnDefinitions[0].MinWidth = 50;
        var inputs = new StackPanel(); var outputs = new StackPanel();
        Grid.SetColumn(outputs, 1); slots.Children.Add(inputs); slots.Children.Add(outputs);
        foreach (var slot in block.Slots) (slot.Direction == SlotDirection.Input ? inputs : outputs).Children.Add(AddPort(slot));
        var body = new Border { Background = Brush.Parse(diagnostics.Length > 0 ? "#C05050" : "#595656"), Padding = new Thickness(0, 3), CornerRadius = new CornerRadius(0, 0, 5, 5), Child = slots };
        var node = new Border { Child = new StackPanel { Children = { title, body } }, Tag = block, BoxShadow = BoxShadows.Parse("2 2 10 0 #FF000000") };
        if (diagnostics.Length > 0) ToolTip.SetTip(node, string.Join(Environment.NewLine, diagnostics.Select(d => d.Message)));
        nodes[block] = node; world.Children.Add(node);
        Canvas.SetLeft(node, block.Position.X); Canvas.SetTop(node, block.Position.Y);
        node.PointerPressed += (_, e) =>
        {
            if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;
            Select(view, e.KeyModifiers.HasFlag(KeyModifiers.Control)); Focus(); e.Handled = true;
        };
        title.PointerPressed += (_, e) =>
        {
            if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;
            Select(view, e.KeyModifiers.HasFlag(KeyModifiers.Control)); Focus();
            moving = true; lastPointer = e.GetPosition(this); dragPositions.Clear();
            foreach (var selectedBlock in editor.SelectedBlocks) dragPositions[selectedBlock.Block] = new Point(selectedBlock.Block.Position.X, selectedBlock.Block.Position.Y);
            e.Pointer.Capture(this); e.Handled = true;
        };
    }
    private Control AddPort(Slot slot)
    {
        var output = slot.Direction == SlotDirection.Output;
        var connected = editor!.SelectedMethod!.Method.Links.Values.Any(l => l.Source == slot || l.Target == slot);
        var color = slot.Kind == SlotKind.Execution ? "#C5C5C5" : "#82DF40";
        var disconnected = slot.Kind == SlotKind.Execution ? "#727171" : "#668639";
        var hover = slot.Kind == SlotKind.Execution ? "#EBEBEB" : "#ADFF25";
        var port = new Ellipse { Width = 15, Height = 15, StrokeThickness = 4, Stroke = Brush.Parse(connected ? color : disconnected), Fill = connected ? Brush.Parse(color) : Brushes.Transparent, VerticalAlignment = VerticalAlignment.Center, Tag = slot };
        ports[slot] = port;
        var name = string.IsNullOrWhiteSpace(slot.Name) ? $"{slot.Direction} ({slot.Kind})" : slot.Name;
        ToolTip.SetTip(port, name + (string.IsNullOrWhiteSpace(slot.Type) ? "" : " (" + slot.Type + ")"));
        port.PointerEntered += (_, _) => { port.Stroke = Brush.Parse(hover); if (connected) port.Fill = port.Stroke; };
        port.PointerExited += (_, _) => { port.Stroke = Brush.Parse(connected ? color : disconnected); if (connected) port.Fill = port.Stroke; };
        port.PointerPressed += (_, e) =>
        {
            if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed || !output) return;
            connecting = slot; connectionEnd = e.GetPosition(world); Focus(); e.Pointer.Capture(this); e.Handled = true;
        };
        var row = new DockPanel();
        DockPanel.SetDock(port, output ? Dock.Right : Dock.Left); row.Children.Add(port);
        row.Children.Add(new TextBlock { Text = slot.Name, FontSize = 15, Foreground = Brush.Parse("#DCDCDC"), Margin = output ? new Thickness(10, 0, 2, 0) : new Thickness(2, 0, 10, 0), VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = output ? HorizontalAlignment.Right : HorizontalAlignment.Left });
        return row;
    }
    private void Select(VisualScriptBlockViewModel block, bool additive)
    {
        if (editor == null) return;
        if (!additive && !editor.SelectedBlocks.Contains(block)) { editor.SelectedBlocks.Clear(); editor.SelectedLinks.Clear(); }
        if (additive && editor.SelectedBlocks.Contains(block)) editor.SelectedBlocks.Remove(block);
        else if (!editor.SelectedBlocks.Contains(block)) editor.SelectedBlocks.Add(block);
        foreach (var pair in nodes)
            if (pair.Value.Child is StackPanel panel && panel.Children[0] is Border title)
                title.Background = Brush.Parse(editor.SelectedBlocks.Any(b => b.Block == pair.Key) ? "#3699B8" : "#194756");
    }
    private Point PortPosition(Slot slot) => ports.TryGetValue(slot, out var port) ? port.TranslatePoint(new Point(7.5, 7.5), world) ?? default : default;
    private void DrawLinks()
    {
        if (editor?.SelectedMethod is not { } method) return;
        var layout = string.Join(";", method.Method.Links.Values.Select(l => $"{l.Id}:{PortPosition(l.Source)}:{PortPosition(l.Target)}:{editor.SelectedLinks.Contains(l)}:{editor.Diagnostics.Count}")) + $"/{connecting?.Id}/{connectionEnd}";
        if (layout == linkLayout) return;
        linkLayout = layout;
        foreach (var edge in edges) world.Children.Remove(edge); edges.Clear();
        foreach (var link in method.Method.Links.Values)
        {
            if (!ports.ContainsKey(link.Source) || !ports.ContainsKey(link.Target)) continue;
            var diagnostics = editor.Diagnostics.Where(d => d.LinkId == link.Id).ToArray();
            var color = diagnostics.Length > 0 ? "#C05050" : link.Source.Kind == SlotKind.Execution ? "#C5C5C5" : "#82DF40";
            var path = MakeLink(PortPosition(link.Source), PortPosition(link.Target), editor.SelectedLinks.Contains(link) ? "#3699B8" : color);
            if (diagnostics.Length > 0) ToolTip.SetTip(path, string.Join(Environment.NewLine, diagnostics.Select(d => d.Message)));
            path.PointerEntered += (_, _) => path.Stroke = Brush.Parse(diagnostics.Length > 0 ? "#F06060" : link.Source.Kind == SlotKind.Execution ? "#EBEBEB" : "#ADFF25");
            path.PointerExited += (_, _) => path.Stroke = Brush.Parse(editor.SelectedLinks.Contains(link) ? "#3699B8" : color);
            path.PointerPressed += (_, e) =>
            {
                if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;
                if (!e.KeyModifiers.HasFlag(KeyModifiers.Control)) { editor.SelectedBlocks.Clear(); editor.SelectedLinks.Clear(); }
                if (!editor.SelectedLinks.Remove(link)) editor.SelectedLinks.Add(link);
                linkLayout = null; DrawLinks(); Focus(); e.Handled = true;
            };
        }
        if (connecting != null) MakeLink(PortPosition(connecting), connectionEnd, connecting.Kind == SlotKind.Execution ? "#EBEBEB" : "#ADFF25").IsHitTestVisible = false;
    }
    private Path MakeLink(Point start, Point end, string color)
    {
        var bend = Math.Max(60, Math.Abs(end.X - start.X) * .5);
        var geometry = new StreamGeometry();
        using (var context = geometry.Open()) { context.BeginFigure(start, false); context.CubicBezierTo(start + new Vector(bend, 0), end - new Vector(bend, 0), end, true); context.EndFigure(false); }
        var path = new Path { Data = geometry, Stroke = Brush.Parse(color), StrokeThickness = 3 };
        world.Children.Insert(0, path); edges.Add(path); return path;
    }
    private void BackgroundPressed(object? sender, PointerPressedEventArgs e)
    {
        if (e.Handled) return;
        lastPointer = e.GetPosition(this);
        var properties = e.GetCurrentPoint(this).Properties;
        if (properties.IsMiddleButtonPressed) { panning = true; e.Pointer.Capture(this); e.Handled = true; }
        else if (properties.IsLeftButtonPressed)
        {
            Focus(); if (!e.KeyModifiers.HasFlag(KeyModifiers.Control)) { editor?.SelectedBlocks.Clear(); editor?.SelectedLinks.Clear(); }
            selectionStart = lastPointer; e.Pointer.Capture(this); e.Handled = true;
        }
    }
    private void Move(object? sender, PointerEventArgs e)
    {
        var current = e.GetPosition(this); var delta = current - lastPointer;
        if (panning) { offset += delta; ApplyTransform(); }
        if (moving)
            foreach (var block in dragPositions.Keys.ToArray())
            {
                dragPositions[block] += delta / zoom;
                Canvas.SetLeft(nodes[block], Snap(dragPositions[block].X)); Canvas.SetTop(nodes[block], Snap(dragPositions[block].Y));
            }
        if (connecting != null) connectionEnd = e.GetPosition(world);
        if (selectionStart is { } start)
        {
            var rect = new Rect(start, current).Normalize();
            selection.Margin = new Thickness(rect.X, rect.Y, 0, 0); selection.HorizontalAlignment = HorizontalAlignment.Left; selection.VerticalAlignment = VerticalAlignment.Top;
            selection.Width = rect.Width; selection.Height = rect.Height; selection.IsVisible = true;
        }
        lastPointer = current; DrawLinks();
    }
    public static int Snap(double value) => (int)Math.Round(value / 16) * 16;
    private void Release(object? sender, PointerReleasedEventArgs e)
    {
        var source = connecting;
        var target = source == null ? null : ports.FirstOrDefault(p => p.Key.Direction == SlotDirection.Input && p.Value.TranslatePoint(default, this) is { } origin && new Rect(origin, new Size(15 * zoom, 15 * zoom)).Inflate(5).Contains(e.GetPosition(this))).Key;
        var positions = moving ? dragPositions.ToDictionary(p => p.Key, p => new Int2(Snap(p.Value.X), Snap(p.Value.Y))) : null;
        if (selectionStart is { } start && editor?.SelectedMethod is { } method)
        {
            var rect = new Rect(start, e.GetPosition(this)).Normalize();
            foreach (var block in method.Blocks)
                if (nodes[block.Block].TranslatePoint(default, this) is { } origin && rect.Intersects(new Rect(origin, nodes[block.Block].Bounds.Size * zoom)) && !editor.SelectedBlocks.Contains(block)) editor.SelectedBlocks.Add(block);
        }
        moving = panning = false; connecting = null; selectionStart = null; selection.IsVisible = false;
        e.Pointer.Capture(null);
        if (positions != null) editor?.MoveBlocks(positions);
        if (source != null && target != null) editor?.Connect(source, target);
        Rebuild();
    }
    private void CancelDrag()
    {
        var changed = moving || connecting != null || panning || selectionStart != null;
        moving = panning = false; connecting = null; selectionStart = null; selection.IsVisible = false; dragPositions.Clear();
        if (changed && editor != null) Rebuild();
    }
    private void ApplyTransform() => world.RenderTransform = new MatrixTransform(new Matrix(zoom, 0, 0, zoom, offset.X, offset.Y));
}
