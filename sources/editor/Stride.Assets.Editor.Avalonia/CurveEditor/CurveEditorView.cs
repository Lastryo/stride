// Copyright (c) .NET Foundation and Contributors (https://dotnetfoundation.org/ & https://stride3d.net)
// Distributed under the MIT license. See the LICENSE.md file in the project root for more information.
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Input;
using Avalonia.Layout;
using Stride.Assets.Editor.CurveEditor;

namespace Stride.Assets.Editor.Avalonia.CurveEditor;

public sealed class CurveEditorView : UserControl
{
    private readonly CurveEditorViewModel model;
    private readonly ListBox channels;
    private readonly NumericUpDown key = new() { Width = 105, Increment = .01m, FormatString = "0.######" };
    private readonly NumericUpDown value = new() { Width = 105, Increment = .01m, FormatString = "0.######" };
    private readonly ComboBox rotation = new() { ItemsSource = Enum.GetValues<RotationDisplayMode>(), SelectedItem = RotationDisplayMode.Quaternion };
    private bool updating;
    private readonly List<Button> editButtons = [];
    private readonly List<global::Avalonia.Media.Imaging.Bitmap> icons = [];
    public CurveCanvas Canvas { get; }
    public CurveEditorView(CurveEditorViewModel model)
    {
        this.model = model; DataContext = model;
        Canvas = new CurveCanvas { Editor = model, MinHeight = 200 };
        channels = new ListBox { ItemsSource = model.Channels, MinWidth = 170, ItemTemplate = new FuncDataTemplate<CurveChannel>((channel, _) => new TextBlock { Text = channel?.Name }) };
        channels.SelectionChanged += (_, _) => { if (!updating && channels.SelectedItem is CurveChannel channel) { Canvas.Cancel(); model.SelectedChannel = channel; Update(); } };
        var tools = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 5, Margin = new(5) };
        Button Add(string text, Action action) { var button = new Button { Content = text }; button.Click += (_, _) => { action(); Update(); };
            if (text is "Add key" or "Delete" or "Clear") editButtons.Add(button);
            var icon = text switch { "Fit" => "curve_fit-32.png", "Fit X" => "curve_fit_width-32.png", "Fit Y" => "curve_fit_height-32.png", _ => null };
            if (icon != null)
            {
                var bitmap = new global::Avalonia.Media.Imaging.Bitmap(global::Avalonia.Platform.AssetLoader.Open(new Uri("avares://Stride.Assets.Editor.Avalonia/CurveEditor/Icons/" + icon)));
                icons.Add(bitmap);
                var content = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4 };
                content.Children.Add(new Image { Source = bitmap, Width = 16, Height = 16 }); content.Children.Add(new TextBlock { Text = text }); button.Content = content;
            }
            tools.Children.Add(button); return button; }
        Add("Fit", () => Canvas.Fit()); Add("Fit X", () => Canvas.Fit(-1)); Add("Fit Y", () => Canvas.Fit(1));
        Add("Add key", () => model.AddPoint((double)(key.Value ?? 0), (double)(value.Value ?? 0)));
        Add("Delete", model.DeleteSelected); Add("Clear", model.Clear);
        Add("First", () => Navigate(false)); Add("Last", () => Navigate(true));
        tools.Children.Add(new TextBlock { Text = "Key", VerticalAlignment = VerticalAlignment.Center }); tools.Children.Add(key);
        tools.Children.Add(new TextBlock { Text = "Value", VerticalAlignment = VerticalAlignment.Center }); tools.Children.Add(value);
        tools.Children.Add(rotation);
        rotation.SelectionChanged += (_, _) => { if (!updating && rotation.SelectedItem is RotationDisplayMode mode) { model.RotationDisplay = mode; Canvas.Fit(); } };
        key.ValueChanged += (_, _) => Change(); value.ValueChanged += (_, _) => Change();
        var root = new DockPanel();
        var hints = new TextBlock { Text = "Alt+click: add · Drag: move/select · Middle/right drag: pan · Wheel: zoom · F: frame · Esc: cancel", Margin = new(8) };
        DockPanel.SetDock(hints, Dock.Bottom); root.Children.Add(hints);
        var toolbar = new ScrollViewer { Content = tools, HorizontalScrollBarVisibility = global::Avalonia.Controls.Primitives.ScrollBarVisibility.Auto, VerticalScrollBarVisibility = global::Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled };
        DockPanel.SetDock(toolbar, Dock.Top); root.Children.Add(toolbar);
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("210,5,*") };
        grid.Children.Add(channels); var splitter = new GridSplitter { ResizeDirection = GridResizeDirection.Columns }; Grid.SetColumn(splitter, 1); grid.Children.Add(splitter);
        Grid.SetColumn(Canvas, 2); grid.Children.Add(Canvas); root.Children.Add(grid); Content = root;
        Canvas.SelectionChanged += (_, _) => Update(); model.Changed += Changed;
        AttachedToVisualTree += (_, _) => { Update(); Canvas.Fit(); };
        DetachedFromVisualTree += (_, _) => Canvas.Cancel();
        KeyDown += (_, e) =>
        {
            if (!e.KeyModifiers.HasFlag(KeyModifiers.Control) || e.Key is not (Key.Z or Key.Y) || e.Source is TextBox) return;
            if (e.Key == Key.Y || e.KeyModifiers.HasFlag(KeyModifiers.Shift)) model.Session.ActionService!.Redo(); else model.Session.ActionService!.Undo(); e.Handled = true;
        };
        Update();
    }
    private void Changed(object? sender, EventArgs e) => Update();
    private void Update()
    {
        if (updating) return; updating = true;
        try
        {
            channels.SelectedItem = model.SelectedChannel;
            rotation.IsVisible = model.SelectedChannel?.ValueType == typeof(Stride.Core.Mathematics.Quaternion) && model.SelectedChannel?.Editable == false;
            foreach (var button in editButtons) button.IsEnabled = model.SelectedChannel?.Editable == true;
            key.IsEnabled = value.IsEnabled = model.SelectedChannel?.Editable == true;
            if (model.SelectedChannel is { } channel && model.SelectedKeys.Count == 1)
            {
                var point = model.SelectedKeys.Single();
                key.Value = Decimal(channel.Key(point)); value.Value = Decimal(channel.Value(point));
            }
        }
        finally { updating = false; }
        Canvas.InvalidateVisual();
    }
    private static decimal? Decimal(double number) => double.IsFinite(number) && Math.Abs(number) <= 1e28 ? (decimal)number : null;
    private void Change()
    {
        if (updating || model.SelectedKeys.Count != 1 || key.Value == null || value.Value == null) return;
        model.MovePoints(new Dictionary<object, (double, double)> { [model.SelectedKeys.Single()] = ((double)key.Value.Value, (double)value.Value.Value) });
    }
    private void Navigate(bool last)
    {
        var keys = model.SelectedChannel?.Keys.OrderBy(k => model.SelectedChannel.Key(k)).ToArray(); if (keys?.Length is not > 0) return;
        model.SelectedKeys.Clear(); model.SelectedKeys.Add(last ? keys[^1] : keys[0]); Canvas.Fit(selected: true);
    }
    public void Detach() { model.Changed -= Changed; Canvas.Editor = null; foreach (var icon in icons) icon.Dispose(); icons.Clear(); }
}
