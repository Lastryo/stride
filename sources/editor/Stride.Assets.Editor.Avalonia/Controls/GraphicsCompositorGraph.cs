// Copyright (c) .NET Foundation and Contributors (https://dotnetfoundation.org/ & https://stride3d.net)
// Distributed under the MIT license. See the LICENSE.md file in the project root for more information.

using System.Collections.Specialized;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Stride.Assets.Editor.GraphicsCompositorEditor.ViewModels;
using Stride.Assets.Editor.ViewModels;
using Path = Avalonia.Controls.Shapes.Path;
using Ellipse = Avalonia.Controls.Shapes.Ellipse;

namespace Stride.Assets.Editor.Avalonia.Controls;

/// <summary>The compositor graph uses the same blocks, slots and Quantum links as the WPF editor.</summary>
public sealed class GraphicsCompositorGraph : Grid
{
    private readonly Canvas world = new() { Width = 1, Height = 1, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top };
    private readonly Dictionary<IGraphicsCompositorBlockViewModel, Point> positions = new();
    private readonly Dictionary<IGraphicsCompositorBlockViewModel, Border> titles = new();
    private readonly Dictionary<IGraphicsCompositorSlotViewModel, Ellipse> ports = new();
    private readonly List<INotifyCollectionChanged> subscriptions = new();
    private readonly List<Path> edges = new();
    private GraphicsCompositorEditorViewModel? editor;
    private IGraphicsCompositorBlockViewModel? moving;
    private GraphicsCompositorSlotViewModel? connecting;
    private Point lastPointer;
    private Point connectionEnd;
    private Vector offset = new(30, 30);
    private double zoom = 1;
    private bool panning;
    private bool refreshPending;
    private string? linkLayout;

    public GraphicsCompositorGraph()
    {
        Background = Brush.Parse("#49494E");
        ClipToBounds = true;
        Focusable = true;
        Children.Add(world);
        world.RenderTransformOrigin = RelativePoint.TopLeft;
        ApplyTransform();
        DataContextChanged += (_, _) => BindEditor();
        AttachedToVisualTree += (_, _) => BindEditor();
        DetachedFromVisualTree += (_, _) => UnbindEditor();
        LayoutUpdated += (_, _) => DrawLinks();
        PointerPressed += BeginBackgroundDrag;
        PointerMoved += MovePointer;
        PointerReleased += EndDrag;
        PointerCaptureLost += (_, _) => { moving = null; connecting = null; panning = false; DrawLinks(); };
        PointerWheelChanged += Zoom;
        KeyDown += (_, e) =>
        {
            if (e.Key == Key.Delete && editor != null)
            {
                editor.DeleteSelectionCommand.Execute(null);
                e.Handled = true;
            }
        };
    }

    private void BindEditor()
    {
        if (ReferenceEquals(editor, DataContext)) return;
        UnbindEditor();
        positions.Clear();
        editor = DataContext as GraphicsCompositorEditorViewModel;
        Rebuild();
    }

    private void UnbindEditor()
    {
        foreach (var collection in subscriptions) collection.CollectionChanged -= Changed;
        subscriptions.Clear();
        editor = null;
        ContextMenu = null;
    }

    private void Watch(INotifyCollectionChanged collection)
    {
        if (subscriptions.Contains(collection)) return;
        subscriptions.Add(collection);
        collection.CollectionChanged += Changed;
    }

    private void Changed(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (refreshPending) return;
        refreshPending = true;
        Dispatcher.UIThread.Post(() =>
        {
            refreshPending = false;
            // Do not replace a captured port or title while dragging.
            if (moving == null && connecting == null && !panning) Rebuild();
        });
    }

    private void Rebuild()
    {
        foreach (var collection in subscriptions) collection.CollectionChanged -= Changed;
        subscriptions.Clear();
        world.Children.Clear();
        titles.Clear();
        ports.Clear();
        edges.Clear();
        linkLayout = null;
        if (editor == null) return;
        Watch(editor.Blocks);
        Watch(editor.SelectedSharedRenderers);
        Watch(editor.SelectedRendererLinks);
        Watch(editor.SharedRendererFactories);
        foreach (var stale in positions.Keys.Where(x => !editor.Blocks.Contains(x)).ToList()) positions.Remove(stale);
        ArrangeNewBlocks();
        foreach (var block in editor.Blocks.Cast<GraphicsCompositorBlockViewModel>())
        {
            Watch(block.InputSlots);
            Watch(block.OutputSlots);
            AddBlock(block);
        }
        DrawLinks();
        var items = new List<object> { new MenuItem { Header = "Create", IsEnabled = false } };
        items.AddRange(editor.SharedRendererFactories.Select(factory => new MenuItem { Header = factory.Name, Command = factory.CreateCommand }));
        items.Add(new Separator());
        items.Add(new MenuItem { Header = "Delete selection", Command = editor.DeleteSelectionCommand, InputGesture = new KeyGesture(Key.Delete) });
        ContextMenu = new ContextMenu { ItemsSource = items };
    }

    private void ArrangeNewBlocks()
    {
        if (editor == null) return;
        // Follow renderer references from the entry points, visiting each block once (cycles are legal).
        var levels = new Dictionary<IGraphicsCompositorBlockViewModel, int>();
        var queue = new Queue<IGraphicsCompositorBlockViewModel>();
        foreach (var entry in editor.Blocks.OfType<EntryPointBlockViewModel>()) { levels[entry] = 0; queue.Enqueue(entry); }
        while (queue.TryDequeue(out var block))
        {
            foreach (var target in block.OutputSlots.SelectMany(x => x.Links).Select(x => x.TargetSlot.Block))
                if (levels.TryAdd(target, levels[block] + 1)) queue.Enqueue(target);
        }
        var rows = new Dictionary<int, double>();
        foreach (var block in editor.Blocks)
        {
            var level = levels.GetValueOrDefault(block, 1);
            var y = rows.GetValueOrDefault(level);
            if (!positions.ContainsKey(block)) positions[block] = new Point(level * 350, y);
            rows[level] = Math.Max(y, positions[block].Y) + 70 + Math.Max(1, block.OutputSlots.Count) * 24;
        }
    }

    private void AddBlock(GraphicsCompositorBlockViewModel block)
    {
        var content = new StackPanel();
        var title = new Border
        {
            Background = Brush.Parse(editor!.SelectedSharedRenderers.Contains(block) ? "#3699B8" : "#194756"),
            CornerRadius = new CornerRadius(5, 5, 0, 0), Padding = new Thickness(10, 2),
            Child = new TextBlock { Text = block.Title, FontSize = 15, FontWeight = FontWeight.Bold, Foreground = Brush.Parse("#EBEBEB"), HorizontalAlignment = HorizontalAlignment.Center }
        };
        titles[block] = title;
        content.Children.Add(title);
        var body = new Grid { ColumnDefinitions = new ColumnDefinitions("50,*"), MinHeight = 21, Margin = new Thickness(0, 3) };
        var inputs = new StackPanel();
        var outputs = new StackPanel();
        Grid.SetColumn(outputs, 1);
        body.Children.Add(inputs);
        body.Children.Add(outputs);
        foreach (var slot in block.InputSlots) inputs.Children.Add(AddPort(slot, false));
        foreach (var slot in block.OutputSlots)
        {
            var row = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), Height = 24 };
            row.Children.Add(new TextBlock { Text = slot.Name, Foreground = Brush.Parse("#EBEBEB"), Margin = new Thickness(10, 0, 2, 0), VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Right });
            var port = AddPort(slot, true);
            Grid.SetColumn(port, 1);
            row.Children.Add(port);
            outputs.Children.Add(row);
        }
        content.Children.Add(new Border { Background = Brush.Parse("#595656"), CornerRadius = new CornerRadius(0, 0, 5, 5), Child = body });
        var node = new Border { Child = content, MinWidth = 150, BoxShadow = BoxShadows.Parse("2 2 10 0 #80000000"), Tag = block, Background = Brushes.Transparent };
        node.Measure(Size.Infinity);
        node.Width = node.DesiredSize.Width;
        Canvas.SetLeft(node, positions[block].X);
        Canvas.SetTop(node, positions[block].Y);
        world.Children.Add(node);
        title.PointerPressed += (_, e) =>
        {
            if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;
            Focus();
            SelectBlock(block, e.KeyModifiers.HasFlag(KeyModifiers.Control));
            moving = block;
            lastPointer = e.GetPosition(this);
            e.Pointer.Capture(this);
            e.Handled = true;
        };
        node.PointerPressed += (_, e) =>
        {
            if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
            {
                SelectBlock(block, e.KeyModifiers.HasFlag(KeyModifiers.Control));
                Focus();
                e.Handled = true;
            }
        };
    }

    private Control AddPort(IGraphicsCompositorSlotViewModel slot, bool output)
    {
        Watch(slot.Links);
        var connected = slot.Links.Count != 0;
        var port = new Ellipse { Width = 15, Height = 15, StrokeThickness = 4, Stroke = Brush.Parse(connected ? "#C5C5C5" : "#727171"), Fill = connected ? Brush.Parse("#C5C5C5") : Brushes.Transparent, VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = output ? HorizontalAlignment.Right : HorizontalAlignment.Left, Tag = slot };
        ports[slot] = port;
        ToolTip.SetTip(port, output ? slot.Name : "Renderer");
        port.PointerEntered += (_, _) => { port.Stroke = Brush.Parse("#EBEBEB"); if (connected) port.Fill = port.Stroke; };
        port.PointerExited += (_, _) => { port.Stroke = Brush.Parse(connected ? "#C5C5C5" : "#727171"); if (connected) port.Fill = port.Stroke; };
        port.PointerPressed += (_, e) =>
        {
            if (!output || !e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;
            Focus();
            connecting = (GraphicsCompositorSlotViewModel)slot;
            connectionEnd = e.GetPosition(world);
            e.Pointer.Capture(this);
            e.Handled = true;
        };
        return new Border { Height = 24, Child = port };
    }

    private Point PortPosition(IGraphicsCompositorSlotViewModel slot)
    {
        if (ports.TryGetValue(slot, out var port) && port.TranslatePoint(new Point(7.5, 7.5), world) is { } point) return point;
        return positions.GetValueOrDefault(slot.Block);
    }

    private void DrawLinks()
    {
        if (editor == null) return;
        var links = editor.Blocks.SelectMany(x => x.OutputSlots).SelectMany(x => x.Links).ToList();
        var layout = string.Join(";", links.Select(link => $"{PortPosition(link.SourceSlot)}:{PortPosition(link.TargetSlot)}:{editor.SelectedRendererLinks.Contains(link)}"))
            + $"/{connecting?.Name}/{connectionEnd}";
        if (layout == linkLayout) return;
        linkLayout = layout;
        foreach (var edge in edges) world.Children.Remove(edge);
        edges.Clear();
        if (editor == null) return;
        foreach (var link in links)
        {
            if (!ports.ContainsKey(link.SourceSlot) || !ports.ContainsKey(link.TargetSlot)) continue;
            var path = MakeLink(PortPosition(link.SourceSlot), PortPosition(link.TargetSlot), editor.SelectedRendererLinks.Contains(link));
            path.PointerPressed += (_, e) =>
            {
                if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;
                Focus();
                var additive = e.KeyModifiers.HasFlag(KeyModifiers.Control);
                editor.SelectedSharedRenderers.Clear();
                if (!additive) editor.SelectedRendererLinks.Clear();
                if (!editor.SelectedRendererLinks.Remove(link)) editor.SelectedRendererLinks.Add(link);
                e.Handled = true;
            };
        }
        if (connecting != null) MakeLink(PortPosition(connecting), connectionEnd, true).IsHitTestVisible = false;
    }

    private Path MakeLink(Point start, Point end, bool selected)
    {
        var distance = Math.Max(60, Math.Abs(end.X - start.X) * .5);
        var geometry = new StreamGeometry();
        using (var context = geometry.Open())
        {
            context.BeginFigure(start, false);
            context.CubicBezierTo(start + new Vector(distance, 0), end - new Vector(distance, 0), end, true);
            context.EndFigure(false);
        }
        var path = new Path { Data = geometry, Stroke = Brush.Parse(selected ? "#3699B8" : "#C5C5C5"), StrokeThickness = selected ? 4 : 3 };
        world.Children.Insert(0, path);
        edges.Add(path);
        return path;
    }

    private void SelectBlock(IGraphicsCompositorBlockViewModel block, bool additive)
    {
        if (editor == null) return;
        editor.SelectedRendererLinks.Clear();
        if (!additive && !editor.SelectedSharedRenderers.Contains(block)) editor.SelectedSharedRenderers.Clear();
        if (additive && editor.SelectedSharedRenderers.Contains(block)) editor.SelectedSharedRenderers.Remove(block);
        else if (!editor.SelectedSharedRenderers.Contains(block)) editor.SelectedSharedRenderers.Add(block);
        foreach (var pair in titles) pair.Value.Background = Brush.Parse(editor.SelectedSharedRenderers.Contains(pair.Key) ? "#3699B8" : "#194756");
    }

    private void BeginBackgroundDrag(object? sender, PointerPressedEventArgs e)
    {
        var properties = e.GetCurrentPoint(this).Properties;
        if (properties.IsMiddleButtonPressed)
        {
            panning = true;
            lastPointer = e.GetPosition(this);
            e.Pointer.Capture(this);
            e.Handled = true;
        }
        else if (properties.IsLeftButtonPressed)
        {
            Focus();
            editor?.SelectedSharedRenderers.Clear();
            editor?.SelectedRendererLinks.Clear();
        }
    }

    private void MovePointer(object? sender, PointerEventArgs e)
    {
        var position = e.GetPosition(this);
        var delta = position - lastPointer;
        if (panning) { offset += delta; ApplyTransform(); }
        if (moving != null)
        {
            var blocks = editor!.SelectedSharedRenderers.Contains(moving) ? editor.SelectedSharedRenderers.ToList() : new List<IGraphicsCompositorBlockViewModel> { moving };
            foreach (var block in blocks)
            {
                positions[block] += delta / zoom;
                var node = world.Children.OfType<Border>().First(x => ReferenceEquals(x.Tag, block));
                Canvas.SetLeft(node, positions[block].X);
                Canvas.SetTop(node, positions[block].Y);
            }
            DrawLinks();
        }
        if (connecting != null) { connectionEnd = e.GetPosition(world); DrawLinks(); }
        lastPointer = position;
    }

    private void EndDrag(object? sender, PointerReleasedEventArgs e)
    {
        if (moving == null && connecting == null && !panning) return;
        var source = connecting;
        var target = ports.FirstOrDefault(pair => pair.Key is SharedRendererInputSlotViewModel && pair.Value.TranslatePoint(default, this) is { } origin && new Rect(origin, new Size(15 * zoom, 15 * zoom)).Inflate(5).Contains(e.GetPosition(this))).Key;
        moving = null;
        connecting = null;
        panning = false;
        e.Pointer.Capture(null);
        if (source != null && target is GraphicsCompositorSlotViewModel slot && source.CanLinkTo(slot))
        {
            source.LinkTo(slot);
            Rebuild();
        }
        else
        {
            DrawLinks();
        }
    }

    private void Zoom(object? sender, PointerWheelEventArgs e)
    {
        var point = e.GetPosition(this);
        var next = Math.Clamp(zoom * Math.Pow(1.15, e.Delta.Y), .15, 3);
        var cursor = new Vector(point.X, point.Y);
        offset = cursor - (cursor - offset) * (next / zoom);
        zoom = next;
        ApplyTransform();
        e.Handled = true;
    }

    private void ApplyTransform() => world.RenderTransform = new MatrixTransform(new Matrix(zoom, 0, 0, zoom, offset.X, offset.Y));
}
