// Copyright (c) .NET Foundation and Contributors (https://dotnetfoundation.org/ & https://stride3d.net)
// Distributed under the MIT license. See the LICENSE.md file in the project root for more information.

using Avalonia;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Input;
using Avalonia.Media;
using Stride.Core.Mathematics;
using Stride.Core.Presentation.Avalonia.Extensions;
using Color = Stride.Core.Mathematics.Color;
using Point = Avalonia.Point;
using Rectangle = Avalonia.Controls.Shapes.Rectangle;

namespace Stride.Core.Presentation.Avalonia.Controls;

public partial class ColorPicker : UserControl
{
    public static readonly StyledProperty<Color4> ColorProperty =
        AvaloniaProperty.Register<ColorPicker, Color4>(nameof(Color), Color4.Black, defaultBindingMode: BindingMode.TwoWay);
    public static readonly StyledProperty<bool> ShowAlphaProperty =
        AvaloniaProperty.Register<ColorPicker, bool>(nameof(ShowAlpha), true);

    private bool updating;
    private Control? dragSurface;
    private IPointer? dragPointer;
    private Color4 dragStart;

    public Color4 Color { get => GetValue(ColorProperty); set => SetValue(ColorProperty, value); }
    public bool ShowAlpha { get => GetValue(ShowAlphaProperty); set => SetValue(ShowAlphaProperty, value); }

    /// <summary>Raised after a numeric edit or a completed pointer gesture, as in WPF.</summary>
    public event EventHandler? ColorChanged;

    public ColorPicker()
    {
        InitializeComponent();
        Synchronize(false);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == ColorProperty && !updating && RedTextBox != null) Synchronize(false);
    }

    private void Synchronize(bool preserveHsv)
    {
        updating = true;
        try
        {
            var rgba = new Color(Color);
            RedTextBox.SetCurrentValue(NumericTextBox.ValueProperty, (double)rgba.R);
            GreenTextBox.SetCurrentValue(NumericTextBox.ValueProperty, (double)rgba.G);
            BlueTextBox.SetCurrentValue(NumericTextBox.ValueProperty, (double)rgba.B);
            AlphaTextBox.SetCurrentValue(NumericTextBox.ValueProperty, (double)rgba.A);
            if (!preserveHsv)
            {
                var hsv = ColorHSV.FromColor(rgba.ToColor4());
                // Keep the selected hue while changing black/gray, where hue is undefined.
                if (hsv.S > 0) HueTextBox.SetCurrentValue(NumericTextBox.ValueProperty, (double)hsv.H);
                SaturationTextBox.SetCurrentValue(NumericTextBox.ValueProperty, (double)hsv.S * 100);
                BrightnessTextBox.SetCurrentValue(NumericTextBox.ValueProperty, (double)hsv.V * 100);
            }
            var hue = HueTextBox.Value ?? 0;
            ColorSurface.Background = new SolidColorBrush(new ColorHSV((float)(hue % 360), 1, 1, 1).ToAvaloniaColor());
            ColorPreview.Fill = new SolidColorBrush(rgba.ToAvaloniaColor());
            Canvas.SetLeft(HueSelector, hue / 360 * 160);
            Canvas.SetLeft(ColorSelector, (SaturationTextBox.Value ?? 0) / 100 * 160);
            Canvas.SetTop(ColorSelector, (BrightnessTextBox.Value ?? 0) / 100 * 160);
            foreach (var name in new[] { "Red", "Green", "Blue", "Alpha", "Hue", "Saturation", "Brightness" })
            {
                var field = this.FindControl<NumericTextBox>(name + "TextBox")!;
                var ratio = this.FindControl<Rectangle>(name + "Ratio")!;
                ((ScaleTransform)ratio.RenderTransform!).ScaleX = (field.Value ?? 0) / field.Maximum;
            }
        }
        finally { updating = false; }
    }

    private void ChannelChanged(object? sender, NumericTextBoxValueChangedEventArgs e)
    {
        if (updating || ColorSurface == null) return;
        UpdateChannels(sender == HueTextBox || sender == SaturationTextBox || sender == BrightnessTextBox);
    }

    private void UpdateChannels(bool hsv)
    {
        var color = hsv
            ? new Color(new ColorHSV((float)((HueTextBox.Value ?? 0) % 360), (float)(SaturationTextBox.Value ?? 0) / 100,
                (float)(BrightnessTextBox.Value ?? 0) / 100, (float)(AlphaTextBox.Value ?? 255) / 255).ToColor()).ToColor4()
            : new Color((byte)(RedTextBox.Value ?? 0), (byte)(GreenTextBox.Value ?? 0),
                (byte)(BlueTextBox.Value ?? 0), (byte)(AlphaTextBox.Value ?? 255)).ToColor4();
        updating = true;
        SetCurrentValue(ColorProperty, color);
        updating = false;
        Synchronize(hsv);
        if (dragSurface == null) ColorChanged?.Invoke(this, EventArgs.Empty);
    }

    private void SurfacePressed(object? sender, PointerPressedEventArgs e)
    {
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed || sender is not Control surface) return;
        Focus();
        dragStart = Color;
        dragSurface = surface;
        dragPointer = e.Pointer;
        e.Pointer.Capture(surface);
        UpdateFromPointer(surface, e.GetPosition(surface));
        e.Handled = true;
    }

    private void SurfaceMoved(object? sender, PointerEventArgs e)
    {
        if (dragSurface is { } surface && e.GetCurrentPoint(surface).Properties.IsLeftButtonPressed)
            UpdateFromPointer(surface, e.GetPosition(surface));
    }

    private void UpdateFromPointer(Control surface, Point point)
    {
        updating = true;
        if (surface == HueSurface) HueTextBox.SetCurrentValue(NumericTextBox.ValueProperty, Math.Clamp(point.X / 160, 0, 1) * 360);
        else
        {
            SaturationTextBox.SetCurrentValue(NumericTextBox.ValueProperty, Math.Clamp(point.X / 160, 0, 1) * 100);
            BrightnessTextBox.SetCurrentValue(NumericTextBox.ValueProperty, Math.Clamp(point.Y / 160, 0, 1) * 100);
        }
        updating = false;
        UpdateChannels(true);
    }

    private void SurfaceReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (dragSurface == null) return;
        UpdateFromPointer(dragSurface, e.GetPosition(dragSurface));
        FinishDrag();
        e.Handled = true;
    }

    private void SurfaceCaptureLost(object? sender, PointerCaptureLostEventArgs e) => FinishDrag();

    private void FinishDrag()
    {
        if (dragSurface == null) return;
        dragSurface = null;
        var pointer = dragPointer;
        dragPointer = null;
        pointer?.Capture(null);
        if (Color != dragStart) ColorChanged?.Invoke(this, EventArgs.Empty);
    }

    public void CancelDrag()
    {
        if (dragSurface == null) return;
        SetCurrentValue(ColorProperty, dragStart);
        dragSurface = null;
        dragPointer?.Capture(null);
        dragPointer = null;
    }
}
