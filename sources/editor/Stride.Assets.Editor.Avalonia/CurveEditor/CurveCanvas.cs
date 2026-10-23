// Copyright (c) .NET Foundation and Contributors (https://dotnetfoundation.org/ & https://stride3d.net)
// Distributed under the MIT license. See the LICENSE.md file in the project root for more information.
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Stride.Animations;
using Stride.Assets.Editor.CurveEditor;

namespace Stride.Assets.Editor.Avalonia.CurveEditor;

public sealed class CurveCanvas : Control
{
    private CurveEditorViewModel? editor;
    private Point? start;
    private Point last;
    private bool pan;
    private IPointer? captured;
    private Rect? selection;
    private Dictionary<object, (double Key, double Value)>? originals;
    private Dictionary<object, (double Key, double Value)>? preview;
    private double minX = 0, maxX = 1, minY = -0.1, maxY = 1.1;
    public CurveCanvas() { Focusable = true; ClipToBounds = true; }
    public CurveEditorViewModel? Editor
    {
        get => editor;
        set { if (editor != null) editor.Changed -= Changed; editor = value; if (editor != null) editor.Changed += Changed; InvalidateVisual(); }
    }
    private void Changed(object? sender, EventArgs e) { if (start != null && !pan) Cancel(); InvalidateVisual(); }
    private Rect Plot => new(55, 15, Math.Max(1, Bounds.Width - 70), Math.Max(1, Bounds.Height - 50));
    public Point ToScreen(double x, double y) => new(Plot.Left + (x - minX) / (maxX - minX) * Plot.Width, Plot.Bottom - (y - minY) / (maxY - minY) * Plot.Height);
    public (double Key, double Value) ToCurve(Point p) => (minX + (p.X - Plot.Left) / Plot.Width * (maxX - minX), minY + (Plot.Bottom - p.Y) / Plot.Height * (maxY - minY));
    public void Fit(int axis = 0, bool selected = false)
    {
        if (editor?.SelectedChannel is not { } channel) return;
        var keys = channel.Keys.Where(k => !selected || editor.SelectedKeys.Contains(k)).ToArray();
        var x1 = keys.Length == 0 ? 0 : keys.Min(channel.Key); var x2 = keys.Length == 0 ? 1 : keys.Max(channel.Key);
        if (x2 - x1 < 0.001) { x1 -= .5; x2 += .5; }
        ((IComputeCurve)channel.Curve).UpdateChanges();
        var values = Enumerable.Range(0, 257).Select(i => channel.Evaluate((float)(x1 + (x2 - x1) * i / 256))).Where(double.IsFinite).ToArray();
        var y1 = keys.Length > 0 && selected ? keys.Min(channel.Value) : values.DefaultIfEmpty(0).Min();
        var y2 = keys.Length > 0 && selected ? keys.Max(channel.Value) : values.DefaultIfEmpty(1).Max();
        if (y2 - y1 < .001) { y1 -= .5; y2 += .5; }
        if (axis != 1) { minX = x1 - (x2 - x1) * .05; maxX = x2 + (x2 - x1) * .05; }
        if (axis != -1) { minY = y1 - (y2 - y1) * .1; maxY = y2 + (y2 - y1) * .1; }
        InvalidateVisual();
    }
    public override void Render(DrawingContext context)
    {
        context.FillRectangle(new SolidColorBrush(Color.Parse("#20242b")), new Rect(Bounds.Size));
        var grid = new Pen(new SolidColorBrush(Color.Parse("#414650")), 1);
        for (var i = 0; i <= 10; i++)
        {
            var x = Plot.Left + i * Plot.Width / 10; var y = Plot.Top + i * Plot.Height / 10;
            context.DrawLine(grid, new Point(x, Plot.Top), new Point(x, Plot.Bottom));
            context.DrawLine(grid, new Point(Plot.Left, y), new Point(Plot.Right, y));
            Label(context, (minX + i * (maxX - minX) / 10).ToString("0.###"), new Point(x - 12, Plot.Bottom + 4));
            Label(context, (maxY - i * (maxY - minY) / 10).ToString("0.###"), new Point(2, y - 7));
        }
        if (editor?.SelectedChannel is not { } active) return;
        using (context.PushClip(Plot))
        {
            foreach (var channel in editor.Channels.Where(c => ReferenceEquals(c.Curve, active.Curve)))
            {
                ((IComputeCurve)channel.Curve).UpdateChanges();
                var colors = new[] { Brushes.IndianRed, Brushes.LightGreen, Brushes.CornflowerBlue, Brushes.Plum };
                var pen = new Pen(channel.Component == active.Component ? Brushes.Gold : colors[channel.Component], channel.Component == active.Component ? 2 : 1);
                Point? previous = null;
                for (var i = 0; i <= 400; i++)
                {
                    var x = minX + (maxX - minX) * i / 400;
                    var y = channel.Evaluate((float)x);
                    if (!double.IsFinite(y)) { previous = null; continue; }
                    var p = ToScreen(x, y);
                    if (previous is { } before) context.DrawLine(pen, before, p);
                    previous = p;
                }
            }
            foreach (var key in active.Keys)
            {
                var p = preview?.TryGetValue(key, out var value) == true ? ToScreen(value.Key, value.Value) : ToScreen(active.Key(key), active.Value(key));
                context.DrawEllipse(editor.SelectedKeys.Contains(key) ? Brushes.Gold : Brushes.White, new Pen(Brushes.Black), p, 5, 5);
            }
            if (selection is { } rect) context.DrawRectangle(new SolidColorBrush(Color.FromArgb(45, 100, 160, 255)), new Pen(Brushes.CornflowerBlue), rect);
        }
    }
    private static double Distance(Point a, Point b) => Math.Sqrt((a.X - b.X) * (a.X - b.X) + (a.Y - b.Y) * (a.Y - b.Y));
    private static void Label(DrawingContext context, string text, Point point) => context.DrawText(new FormattedText(text, System.Globalization.CultureInfo.InvariantCulture, FlowDirection.LeftToRight, Typeface.Default, 10, Brushes.LightGray), point);
    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e); Focus();
        if (editor?.SelectedChannel is not { } channel) return;
        last = e.GetPosition(this); var properties = e.GetCurrentPoint(this).Properties;
        if (!Plot.Contains(last)) return;
        pan = properties.IsMiddleButtonPressed || properties.IsRightButtonPressed;
        if (!pan && !properties.IsLeftButtonPressed) return;
        if (!pan && e.KeyModifiers.HasFlag(KeyModifiers.Alt)) { var value = ToCurve(last); editor.AddPoint(value.Key, value.Value); e.Handled = true; return; }
        start = last;
        if (!pan)
        {
            var hit = channel.Keys.FirstOrDefault(k => Distance(ToScreen(channel.Key(k), channel.Value(k)), last) < 9);
            if (hit != null)
            {
                if (e.KeyModifiers.HasFlag(KeyModifiers.Control)) { if (!editor.SelectedKeys.Add(hit)) editor.SelectedKeys.Remove(hit); }
                else if (!editor.SelectedKeys.Contains(hit)) { editor.SelectedKeys.Clear(); editor.SelectedKeys.Add(hit); }
                originals = editor.SelectedKeys.ToDictionary(k => k, k => (channel.Key(k), channel.Value(k)), ReferenceEqualityComparer.Instance);
            }
            else { if (!e.KeyModifiers.HasFlag(KeyModifiers.Control)) editor.SelectedKeys.Clear(); selection = new Rect(last, last); }
        }
        captured = e.Pointer; e.Pointer.Capture(this); e.Handled = true; InvalidateVisual();
    }
    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        if (start is not { } origin) return;
        var p = e.GetPosition(this);
        if (pan)
        {
            var dx = (p.X - last.X) / Plot.Width * (maxX - minX); var dy = (p.Y - last.Y) / Plot.Height * (maxY - minY);
            minX -= dx; maxX -= dx; minY += dy; maxY += dy;
        }
        else if (originals != null)
        {
            var a = ToCurve(origin); var b = ToCurve(p);
            preview = originals.ToDictionary(v => v.Key, v => (v.Value.Key + b.Key - a.Key, v.Value.Value + b.Value - a.Value), ReferenceEqualityComparer.Instance);
        }
        else selection = new Rect(origin, p).Normalize();
        last = p; InvalidateVisual(); e.Handled = true;
    }
    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        if (start == null) return;
        var changes = preview;
        if (selection is { } rect && editor?.SelectedChannel is { } channel)
            foreach (var key in channel.Keys) if (rect.Contains(ToScreen(channel.Key(key), channel.Value(key)))) editor.SelectedKeys.Add(key);
        Cancel(); e.Pointer.Capture(null);
        if (changes != null && editor != null) editor.MovePoints(changes);
        SelectionChanged?.Invoke(this, EventArgs.Empty); e.Handled = true;
    }
    public event EventHandler? SelectionChanged;
    public void Cancel() { var pointer = captured; captured = null; start = null; originals = null; preview = null; selection = null; pan = false; pointer?.Capture(null); InvalidateVisual(); }
    protected override void OnPointerCaptureLost(PointerCaptureLostEventArgs e) { Cancel(); base.OnPointerCaptureLost(e); }
    protected override void OnPointerWheelChanged(PointerWheelEventArgs e)
    {
        base.OnPointerWheelChanged(e); var p = ToCurve(e.GetPosition(this)); var factor = Math.Pow(1.15, -e.Delta.Y);
        if (!e.KeyModifiers.HasFlag(KeyModifiers.Control)) { minX = p.Key + (minX - p.Key) * factor; maxX = p.Key + (maxX - p.Key) * factor; }
        if (!e.KeyModifiers.HasFlag(KeyModifiers.Shift)) { minY = p.Value + (minY - p.Value) * factor; maxY = p.Value + (maxY - p.Value) * factor; }
        InvalidateVisual(); e.Handled = true;
    }
    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e); if (editor == null) return;
        if (e.Key == Key.Escape) Cancel();
        else if (e.Key == Key.Delete) editor.DeleteSelected();
        else if (e.Key == Key.F) Fit(selected: editor.SelectedKeys.Count > 0);
        else if (e.Key == Key.A && e.KeyModifiers.HasFlag(KeyModifiers.Control)) editor.SelectAll();
        else return;
        SelectionChanged?.Invoke(this, EventArgs.Empty); e.Handled = true;
    }
}
