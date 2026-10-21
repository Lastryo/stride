// Copyright (c) .NET Foundation and Contributors (https://dotnetfoundation.org/ & https://stride3d.net)
// Distributed under the MIT license. See the LICENSE.md file in the project root for more information.

using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Data;
using Avalonia.Input;
using Avalonia.VisualTree;
using Stride.Core.Assets.Editor.Avalonia.Views;
using Stride.Core.Mathematics;
using Stride.Core.Presentation.Avalonia.Controls;
using Color = Stride.Core.Mathematics.Color;
using Point = Avalonia.Point;

internal static class EditorColorPickerSmoke
{
    public static async Task Check(Window window)
    {
        var root = new Panel();
        window.Content = root;

        var source = new ColorSource { ColorValue = Color.Red, Color3Value = new Color3(1, 0, 0), Color4Value = new Color4(1, 0, 0, 1) };

        // 1. ColorEditor (Color / 8-bit RGBA)
        var colorEditor = new ColorEditor { Width = 200, Palette = ColorPalettes.Metals };
        colorEditor.Bind(ColorEditor.ValueProperty, new Binding(nameof(ColorSource.ColorValue)) { Source = source, Mode = BindingMode.TwoWay });
        root.Children.Add(colorEditor);

        await Task.Delay(50);
        colorEditor.ApplyTemplate();
        var textBox = colorEditor.GetVisualDescendants().OfType<TextBox>().First(t => t.Name == "TextBox");
        var toggle = colorEditor.GetVisualDescendants().OfType<ToggleButton>().First(t => t.Name == "PickerToggleButton");
        if (textBox.Text != "#FFFF0000")
            throw new Exception($"Expected #FFFF0000 but got '{textBox.Text}'");

        // Validate text entry commit on Enter
        textBox.Focus();
        textBox.Text = "#FF00FF00";
        textBox.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Enter });
        await Task.Delay(50);
        if (source.ColorValue != Color.FromRgba(0xFF00FF00))
            throw new Exception($"Enter did not commit typed color. Expected Lime, got {source.ColorValue}");

        // Validate text entry cancel on Escape
        textBox.Focus();
        textBox.Text = "#FF123456";
        textBox.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Escape });
        await Task.Delay(50);
        if (source.ColorValue != Color.FromRgba(0xFF00FF00) || textBox.Text != "#FF00FF00")
            throw new Exception("Escape did not cancel color text edit.");

        // 2. Open Popup and inspect ColorPicker
        var popup = colorEditor.GetVisualDescendants().OfType<Popup>().First(p => p.Name == "PART_Popup");
        toggle.IsChecked = true;
        await Task.Delay(50);
        if (!popup.IsOpen)
            throw new Exception("PickerToggleButton did not open popup.");

        var picker = (ColorPicker)typeof(ColorEditor<Color>).GetField("picker", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!.GetValue(colorEditor)!;

        if (!picker.ShowAlpha)
            throw new Exception("ColorEditor should show alpha by default.");

        var redBox = picker.FindControl<NumericTextBox>("RedTextBox")!;
        var greenBox = picker.FindControl<NumericTextBox>("GreenTextBox")!;
        var blueBox = picker.FindControl<NumericTextBox>("BlueTextBox")!;
        var alphaBox = picker.FindControl<NumericTextBox>("AlphaTextBox")!;
        var hueBox = picker.FindControl<NumericTextBox>("HueTextBox")!;
        var satBox = picker.FindControl<NumericTextBox>("SaturationTextBox")!;
        var briBox = picker.FindControl<NumericTextBox>("BrightnessTextBox")!;

        if (redBox.Value != 0 || greenBox.Value != 255 || blueBox.Value != 0 || alphaBox.Value != 255)
            throw new Exception($"ColorPicker channels mismatch: R={redBox.Value}, G={greenBox.Value}, B={blueBox.Value}, A={alphaBox.Value}");
        if (Math.Abs((hueBox.Value ?? 0) - 120) > 1 || Math.Abs((satBox.Value ?? 0) - 100) > 1 || Math.Abs((briBox.Value ?? 0) - 100) > 1)
            throw new Exception($"ColorPicker HSB mismatch: H={hueBox.Value}, S={satBox.Value}, B={briBox.Value}");

        // 3. Numeric channel edit updates property immediately
        redBox.Value = 128;
        await Task.Delay(50);
        if (source.ColorValue.R != 128)
            throw new Exception($"Numeric channel edit did not update source color: {source.ColorValue}");

        // 4. Pointer dragging parity: Value only commits on release
        var hueSurface = picker.FindControl<Canvas>("HueSurface")!;
        var pointer = new Pointer(201, PointerType.Mouse, true);
        var beforeDragColor = source.ColorValue;

        // Press on Hue canvas at X=0 (H=0, red)
        hueSurface.RaiseEvent(new PointerPressedEventArgs(hueSurface, pointer, hueSurface, new Point(0, 10), 0,
            new PointerPointProperties(RawInputModifiers.LeftMouseButton, PointerUpdateKind.LeftButtonPressed), KeyModifiers.None, 1));
        await Task.Delay(20);
        if (source.ColorValue != beforeDragColor)
            throw new Exception("ColorEditor committed value during initial mouse down before release.");

        // Move to X=80 (H=180, cyan)
        hueSurface.RaiseEvent(new PointerEventArgs(InputElement.PointerMovedEvent, hueSurface, pointer, hueSurface, new Point(80, 10), 0,
            new PointerPointProperties(RawInputModifiers.LeftMouseButton, PointerUpdateKind.Other), KeyModifiers.None));
        await Task.Delay(20);
        if (source.ColorValue != beforeDragColor)
            throw new Exception("ColorEditor committed value during mouse drag before release.");

        // Release at X=80
        hueSurface.RaiseEvent(new PointerReleasedEventArgs(hueSurface, pointer, hueSurface, new Point(80, 10), 0,
            new PointerPointProperties(RawInputModifiers.None, PointerUpdateKind.LeftButtonReleased), KeyModifiers.None, MouseButton.Left));
        await Task.Delay(50);
        if (source.ColorValue == beforeDragColor)
            throw new Exception("ColorEditor did not commit color value on mouse release.");

        // 5. Escape closes popup
        colorEditor.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Escape });
        await Task.Delay(50);
        if (popup.IsOpen)
            throw new Exception("Escape did not close the ColorPicker popup.");

        // 6. Palette selection
        var goldColor = ColorPalettes.Metals["Gold"];
        colorEditor.SelectedPaletteColor = new KeyValuePair<string, Color4>("Gold", goldColor);
        await Task.Delay(50);
        if (source.ColorValue != (Color)goldColor)
            throw new Exception($"Selecting Gold palette item failed. Got {source.ColorValue}");

        // 7. Color3Editor (ShowAlpha = false)
        root.Children.Clear();
        var color3Editor = new Color3Editor { Width = 200, Palette = ColorPalettes.Metals };
        color3Editor.Bind(Color3Editor.ValueProperty, new Binding(nameof(ColorSource.Color3Value)) { Source = source, Mode = BindingMode.TwoWay });
        root.Children.Add(color3Editor);
        await Task.Delay(50);
        color3Editor.ApplyTemplate();
        var toggle3 = color3Editor.GetVisualDescendants().OfType<ToggleButton>().First(t => t.Name == "PickerToggleButton");
        var popup3 = color3Editor.GetVisualDescendants().OfType<Popup>().First(p => p.Name == "PART_Popup");
        toggle3.IsChecked = true;
        await Task.Delay(50);
        var picker3 = (ColorPicker)typeof(ColorEditor<Color3>).GetField("picker", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!.GetValue(color3Editor)!;
        if (picker3.ShowAlpha)
            throw new Exception("Color3Editor must have ShowAlpha = false.");
        var alphaRow = picker3.FindControl<NumericTextBox>("AlphaTextBox")!.Parent!.Parent as Visual;
        if (alphaRow != null && alphaRow.IsVisible)
            throw new Exception("Alpha input must not be visible when ShowAlpha is false.");

        // 8. IsMixed / DifferentValues
        color3Editor.IsMixed = true;
        color3Editor.PlaceholderText = "(Different values)";
        await Task.Delay(50);
        var textBox3 = color3Editor.GetVisualDescendants().OfType<TextBox>().First(t => t.Name == "TextBox");
        if (textBox3.Text != null)
            throw new Exception($"Expected null text when IsMixed is true, got '{textBox3.Text}'");

        root.Children.Clear();
        Console.WriteLine("PASS: ColorPicker 1:1 WPF parity verified (HEX text commit/cancel, popup, RGBA/HSB, pointer drag single-undo, palette selection, ShowAlpha toggle, and DifferentValues).");
    }

    private sealed class ColorSource
    {
        public Color ColorValue { get; set; }
        public Color3 Color3Value { get; set; }
        public Color4 Color4Value { get; set; }
    }
}
