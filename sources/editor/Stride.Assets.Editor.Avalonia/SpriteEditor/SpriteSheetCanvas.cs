// Copyright (c) .NET Foundation and Contributors (https://dotnetfoundation.org/ & https://stride3d.net)
// Distributed under the MIT license. See the LICENSE.md file in the project root for more information.

using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Stride.Assets.Editor.SpriteEditor;
using Stride.Assets.Editor.ViewModels;
using Vector4 = Stride.Core.Mathematics.Vector4;
using SRectangle = Stride.Core.Mathematics.Rectangle;
using SVector = Stride.Core.Mathematics.Vector2;

namespace Stride.Assets.Editor.Avalonia.SpriteEditor;

/// <summary>Pixel-space editor; gestures preview locally and commit once through Quantum on release.</summary>
public sealed class SpriteSheetCanvas : Control
{
    private SpriteSheetEditorViewModel? editor;
    private SpriteInfoViewModel? sprite;
    private Bitmap? bitmap;
    private double zoom = 1;
    private Point offset = new(20, 20);
    private Point pointerStart;
    private Point panStart;
    private SVector imageStart;
    private bool panning;
    private bool dragging;
    private int handle;
    private SRectangle initialRegion;
    private SRectangle previewRegion;
    private Vector4 initialBorders;
    private Vector4 previewBorders;
    private SVector previewCenter;
    private SpriteSheetEditorToolMode gestureTool;
    public SpriteEditorImageCache? Cache { get; set; }
    public event Action<string>? StatusChanged;

    public SpriteSheetCanvas()
    {
        RenderOptions.SetBitmapInterpolationMode(this, BitmapInterpolationMode.None);
        DetachedFromVisualTree += (_, _) => CancelGesture();
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        if (editor != null) editor.PropertyChanged -= EditorChanged;
        editor = DataContext as SpriteSheetEditorViewModel;
        if (editor != null) editor.PropertyChanged += EditorChanged;
        base.OnDataContextChanged(e);
        AttachSprite();
    }

    private void EditorChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(SpriteSheetEditorViewModel.CurrentSprite)) AttachSprite();
        InvalidateVisual();
    }

    private void AttachSprite()
    {
        CancelGesture();
        if (sprite != null) sprite.PropertyChanged -= SpriteChanged;
        var previousSource = sprite?.Source;
        sprite = editor?.CurrentSprite;
        if (sprite != null) sprite.PropertyChanged += SpriteChanged;
        ReloadImage();
        if (previousSource != sprite?.Source) Fit();
    }

    private void SpriteChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(SpriteInfoViewModel.Source)) ReloadImage();
        InvalidateVisual();
    }

    public void ReloadImage()
    {
        bitmap = Cache?.RetrieveImage(sprite?.Source);
        StatusChanged?.Invoke(sprite == null ? "Select one sprite to edit its image." : bitmap == null
            ? $"Image unavailable: {sprite.Source}. Set Source in the property inspector or import an image."
            : $"{sprite.Source} · {bitmap.PixelSize.Width} × {bitmap.PixelSize.Height}");
        InvalidateVisual();
    }

    public void Fit()
    {
        if (bitmap == null || Bounds.Width <= 40 || Bounds.Height <= 40) return;
        zoom = Math.Clamp(Math.Min((Bounds.Width - 40) / bitmap.PixelSize.Width, (Bounds.Height - 40) / bitmap.PixelSize.Height), 0.01, 64);
        offset = new((Bounds.Width - bitmap.PixelSize.Width * zoom) / 2, (Bounds.Height - bitmap.PixelSize.Height * zoom) / 2);
        InvalidateVisual();
    }

    public void Frame()
    {
        if (sprite == null || sprite.Region.Width <= 0 || sprite.Region.Height <= 0) return;
        zoom = Math.Clamp(Math.Min(Math.Max(1, Bounds.Width - 40) / sprite.Region.Width, Math.Max(1, Bounds.Height - 40) / sprite.Region.Height), 0.01, 64);
        offset = new(Bounds.Width / 2 - (sprite.Region.X + sprite.Region.Width / 2.0) * zoom,
            Bounds.Height / 2 - (sprite.Region.Y + sprite.Region.Height / 2.0) * zoom);
        InvalidateVisual();
    }

    public void Zoom(double factor) => ChangeZoom(zoom * factor, new(Bounds.Width / 2, Bounds.Height / 2));
    public void RealSize() => ChangeZoom(1, new(Bounds.Width / 2, Bounds.Height / 2));
    private void ChangeZoom(double value, Point anchor)
    {
        value = Math.Clamp(value, 0.01, 64);
        offset = new(anchor.X - (anchor.X - offset.X) * value / zoom, anchor.Y - (anchor.Y - offset.Y) * value / zoom);
        zoom = value;
        InvalidateVisual();
    }
    private Point Screen(double x, double y) => new(offset.X + x * zoom, offset.Y + y * zoom);
    private SVector ImagePoint(Point point) => new((float)((point.X - offset.X) / zoom), (float)((point.Y - offset.Y) / zoom));
    private Rect ScreenRect(SRectangle r) => new(Screen(r.X, r.Y), new Size(r.Width * zoom, r.Height * zoom));
    private SVector CenterPoint(SRectangle r, SVector center) => new(r.X + center.X + (sprite!.CenterFromMiddle ? r.Width / 2f : 0.5f), r.Y + center.Y + (sprite.CenterFromMiddle ? r.Height / 2f : 0.5f));

    public override void Render(DrawingContext context)
    {
        base.Render(context);
        context.FillRectangle(new SolidColorBrush(Color.FromRgb(35, 35, 38)), new Rect(Bounds.Size));
        bitmap = Cache?.RetrieveImage(sprite?.Source);
        if (bitmap == null || sprite == null) return;
        var imageRect = new Rect(offset, new Size(bitmap.PixelSize.Width * zoom, bitmap.PixelSize.Height * zoom));
        using (context.PushClip(imageRect))
        {
            const int tile = 12;
            for (var y = 0; y < Bounds.Height; y += tile)
                for (var x = 0; x < Bounds.Width; x += tile)
                    context.FillRectangle(((x / tile + y / tile) & 1) == 0 ? Brushes.LightGray : Brushes.Gray, new Rect(x, y, tile, tile));
            context.DrawImage(bitmap, new Rect(bitmap.Size), imageRect);
        }
        var region = dragging && gestureTool == SpriteSheetEditorToolMode.Region ? previewRegion : sprite.Region;
        var rect = ScreenRect(region);
        if (editor?.SelectionHighlightEnabled == true)
        {
            var shade = new SolidColorBrush(Color.FromArgb(125, 0, 0, 0));
            using (context.PushClip(imageRect))
            {
                context.FillRectangle(shade, new Rect(imageRect.Left, imageRect.Top, imageRect.Width, Math.Max(0, rect.Top - imageRect.Top)));
                context.FillRectangle(shade, new Rect(imageRect.Left, rect.Bottom, imageRect.Width, Math.Max(0, imageRect.Bottom - rect.Bottom)));
                context.FillRectangle(shade, new Rect(imageRect.Left, rect.Top, Math.Max(0, rect.Left - imageRect.Left), rect.Height));
                context.FillRectangle(shade, new Rect(rect.Right, rect.Top, Math.Max(0, imageRect.Right - rect.Right), rect.Height));
            }
        }
        var color = editor?.SelectionColor ?? Stride.Core.Mathematics.Color.Red;
        context.DrawRectangle(null, new Pen(new SolidColorBrush(Color.FromArgb(color.A, color.R, color.G, color.B)), 1.5), rect);
        if (editor?.ToolMode == SpriteSheetEditorToolMode.Region)
            foreach (var point in Handles(rect)) context.DrawRectangle(Brushes.White, new Pen(Brushes.Black), new Rect(point.X - 3, point.Y - 3, 6, 6));
        if (editor?.ToolMode == SpriteSheetEditorToolMode.Borders)
        {
            var borders = dragging ? previewBorders : sprite.Borders;
            var pen = new Pen(Brushes.LimeGreen, 1.5);
            context.DrawLine(pen, Screen(region.Left + borders.X, region.Top), Screen(region.Left + borders.X, region.Bottom));
            context.DrawLine(pen, Screen(region.Right - borders.Z, region.Top), Screen(region.Right - borders.Z, region.Bottom));
            context.DrawLine(pen, Screen(region.Left, region.Top + borders.Y), Screen(region.Right, region.Top + borders.Y));
            context.DrawLine(pen, Screen(region.Left, region.Bottom - borders.W), Screen(region.Right, region.Bottom - borders.W));
        }
        var center = CenterPoint(region, dragging && gestureTool == SpriteSheetEditorToolMode.SpriteCenter ? previewCenter : sprite.Center);
        var centerScreen = Screen(center.X, center.Y);
        var centerPen = new Pen(Brushes.Cyan, 1.5);
        context.DrawLine(centerPen, centerScreen - new global::Avalonia.Vector(8, 0), centerScreen + new global::Avalonia.Vector(8, 0));
        context.DrawLine(centerPen, centerScreen - new global::Avalonia.Vector(0, 8), centerScreen + new global::Avalonia.Vector(0, 8));
    }

    // Clockwise from the top-left; the edge handles share the same resize calculation.
    private static Point[] Handles(Rect r) => [r.TopLeft, new(r.Center.X, r.Top), r.TopRight, new(r.Right, r.Center.Y), r.BottomRight, new(r.Center.X, r.Bottom), r.BottomLeft, new(r.Left, r.Center.Y)];

    protected override void OnPointerWheelChanged(PointerWheelEventArgs e)
    {
        base.OnPointerWheelChanged(e);
        if (!dragging) ChangeZoom(zoom * (e.Delta.Y > 0 ? 1.25 : 0.8), e.GetPosition(this));
        e.Handled = true;
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        Focus();
        pointerStart = e.GetPosition(this);
        if (e.GetCurrentPoint(this).Properties.IsMiddleButtonPressed)
        {
            panning = true;
            panStart = offset;
            e.Pointer.Capture(this);
            e.Handled = true;
            return;
        }
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed || sprite == null || bitmap == null || editor?.Asset.IsEditable != true) return;
        imageStart = ImagePoint(pointerStart);
        if (imageStart.X < 0 || imageStart.Y < 0 || imageStart.X >= bitmap.PixelSize.Width || imageStart.Y >= bitmap.PixelSize.Height) return;
        gestureTool = editor.ToolMode;
        if (gestureTool is SpriteSheetEditorToolMode.ColorPick or SpriteSheetEditorToolMode.MagicWand)
        {
            using var transaction = editor.UndoRedoService.CreateTransaction();
            if (gestureTool == SpriteSheetEditorToolMode.ColorPick && Cache?.PickPixelColor(sprite.Source, imageStart) is { } color)
            {
                editor.ColorKey = color;
                editor.ColorKeyEnabled = true;
            }
            else if (gestureTool == SpriteSheetEditorToolMode.MagicWand && Cache?.FindSpriteRegion(sprite.Source, imageStart, editor.MagicWandUseTransparency, editor.ColorKey) is { } region)
                editor.SetRegion(sprite, region);
            editor.UndoRedoService.SetName(transaction, gestureTool == SpriteSheetEditorToolMode.ColorPick ? "Pick sprite color key" : "Find sprite region");
            e.Handled = true;
            return;
        }
        initialRegion = previewRegion = sprite.Region;
        initialBorders = previewBorders = sprite.Borders;
        previewCenter = sprite.Center;
        handle = -1;
        if (gestureTool == SpriteSheetEditorToolMode.Region)
        {
            var rect = ScreenRect(initialRegion);
            var handles = Handles(rect);
            for (var i = 0; i < handles.Length; i++) if (((global::Avalonia.Vector)(handles[i] - pointerStart)).Length <= 8) { handle = i; break; }
            if (handle == -1 && rect.Contains(pointerStart)) handle = 8;
        }
        else if (gestureTool == SpriteSheetEditorToolMode.Borders)
        {
            var distances = new[] { Math.Abs(imageStart.X - initialRegion.Left - initialBorders.X), Math.Abs(imageStart.Y - initialRegion.Top - initialBorders.Y),
                Math.Abs(imageStart.X - initialRegion.Right + initialBorders.Z), Math.Abs(imageStart.Y - initialRegion.Bottom + initialBorders.W) };
            handle = Array.IndexOf(distances, distances.Min());
        }
        dragging = true;
        e.Pointer.Capture(this);
        UpdateGesture(pointerStart);
        e.Handled = true;
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        if (panning) { offset = panStart + (e.GetPosition(this) - pointerStart); InvalidateVisual(); }
        if (dragging) UpdateGesture(e.GetPosition(this));
    }

    private void UpdateGesture(Point point)
    {
        if (bitmap == null || sprite == null) return;
        var current = ImagePoint(point);
        var dx = (int)Math.Round(current.X - imageStart.X, MidpointRounding.AwayFromZero);
        var dy = (int)Math.Round(current.Y - imageStart.Y, MidpointRounding.AwayFromZero);
        if (gestureTool == SpriteSheetEditorToolMode.Region)
            previewRegion = ResizeRegion(initialRegion, imageStart, current, handle, bitmap.PixelSize.Width, bitmap.PixelSize.Height);
        else if (gestureTool == SpriteSheetEditorToolMode.SpriteCenter)
        {
            previewCenter = new((float)Math.Round(current.X - initialRegion.X - (sprite.CenterFromMiddle ? initialRegion.Width / 2f : 0.5f)),
                (float)Math.Round(current.Y - initialRegion.Y - (sprite.CenterFromMiddle ? initialRegion.Height / 2f : 0.5f)));
        }
        else if (gestureTool == SpriteSheetEditorToolMode.Borders)
        {
            var borders = initialBorders;
            if (handle == 0) borders.X += dx;
            if (handle == 1) borders.Y += dy;
            if (handle == 2) borders.Z -= dx;
            if (handle == 3) borders.W -= dy;
            previewBorders = SpriteSheetEditorViewModel.ClampBorders(borders, initialRegion);
        }
        InvalidateVisual();
    }

    public static SRectangle ResizeRegion(SRectangle initial, SVector start, SVector end, int handle, int width, int height)
    {
        var dx = (int)Math.Round(end.X - start.X, MidpointRounding.AwayFromZero);
        var dy = (int)Math.Round(end.Y - start.Y, MidpointRounding.AwayFromZero);
        if (handle == 8)
            return new(Math.Clamp(initial.X + dx, 0, Math.Max(0, width - initial.Width)), Math.Clamp(initial.Y + dy, 0, Math.Max(0, height - initial.Height)), Math.Min(initial.Width, width), Math.Min(initial.Height, height));
        int left, top, right, bottom;
        if (handle == -1)
        {
            left = (int)Math.Floor(Math.Min(start.X, end.X)); top = (int)Math.Floor(Math.Min(start.Y, end.Y));
            right = (int)Math.Ceiling(Math.Max(start.X, end.X)); bottom = (int)Math.Ceiling(Math.Max(start.Y, end.Y));
        }
        else
        {
            left = initial.Left; top = initial.Top; right = initial.Right; bottom = initial.Bottom;
            if (handle is 0 or 6 or 7) left = Math.Min(right, left + dx);
            if (handle is 2 or 3 or 4) right = Math.Max(left, right + dx);
            if (handle is 0 or 1 or 2) top = Math.Min(bottom, top + dy);
            if (handle is 4 or 5 or 6) bottom = Math.Max(top, bottom + dy);
        }
        left = Math.Clamp(left, 0, width); right = Math.Clamp(right, left, width);
        top = Math.Clamp(top, 0, height); bottom = Math.Clamp(bottom, top, height);
        return new(left, top, right - left, bottom - top);
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        if (dragging && sprite != null && editor != null)
        {
            UpdateGesture(e.GetPosition(this));
            using var transaction = editor.UndoRedoService.CreateTransaction();
            if (gestureTool == SpriteSheetEditorToolMode.Region) editor.SetRegion(sprite, previewRegion);
            if (gestureTool == SpriteSheetEditorToolMode.SpriteCenter) sprite.Center = previewCenter;
            if (gestureTool == SpriteSheetEditorToolMode.Borders) sprite.Borders = previewBorders;
            editor.UndoRedoService.SetName(transaction, gestureTool switch
            {
                SpriteSheetEditorToolMode.SpriteCenter => "Move sprite center",
                SpriteSheetEditorToolMode.Borders => "Update sprite borders",
                _ => "Update sprite region"
            });
        }
        CancelGesture();
        e.Pointer.Capture(null);
    }

    protected override void OnPointerCaptureLost(PointerCaptureLostEventArgs e) { CancelGesture(); base.OnPointerCaptureLost(e); }
    public void CancelGesture() { dragging = false; panning = false; InvalidateVisual(); }
    public void Detach()
    {
        CancelGesture();
        if (editor != null) editor.PropertyChanged -= EditorChanged;
        if (sprite != null) sprite.PropertyChanged -= SpriteChanged;
        editor = null; sprite = null; bitmap = null;
    }
}
