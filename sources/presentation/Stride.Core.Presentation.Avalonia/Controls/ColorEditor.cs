// Copyright (c) .NET Foundation and Contributors (https://dotnetfoundation.org/ & https://stride3d.net)
// Distributed under the MIT license. See the LICENSE.md file in the project root for more information.

using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Data;
using Avalonia.Input;
using Stride.Core.Presentation.Avalonia.Converters;
using Stride.Core.Mathematics;

namespace Stride.Core.Presentation.Avalonia.Controls;

public abstract class ColorEditor<T> : TemplatedControl
{
    public static readonly StyledProperty<T> ValueProperty =
        AvaloniaProperty.Register<ColorEditor<T>, T>(nameof(Value), defaultBindingMode: BindingMode.TwoWay);

    public static readonly StyledProperty<string?> PlaceholderTextProperty =
        AvaloniaProperty.Register<ColorEditor<T>, string?>(nameof(PlaceholderText));

    public static readonly StyledProperty<IReadOnlyDictionary<string, Color4>> PaletteProperty =
        AvaloniaProperty.Register<ColorEditor<T>, IReadOnlyDictionary<string, Color4>>(nameof(Palette));

    public static readonly StyledProperty<KeyValuePair<string, Color4>?> SelectedPaletteColorProperty =
        AvaloniaProperty.Register<ColorEditor<T>, KeyValuePair<string, Color4>?>(nameof(SelectedPaletteColor), defaultBindingMode: BindingMode.TwoWay);

    public static readonly StyledProperty<bool> IsMixedProperty =
        AvaloniaProperty.Register<ColorEditor<T>, bool>(nameof(IsMixed));

    public bool IsMixed { get => GetValue(IsMixedProperty); set => SetValue(IsMixedProperty, value); }

    private ColorPicker? picker;
    private Popup? popup;
    private TextBox? textBox;
    private bool synchronizing;

    protected override void OnApplyTemplate(TemplateAppliedEventArgs e)
    {
        if (picker != null) picker.ColorChanged -= PickerColorChanged;
        if (popup != null) popup.Opened -= PopupOpened;
        base.OnApplyTemplate(e);
        picker = e.NameScope.Find<ColorPicker>("PART_ColorPicker");
        popup = e.NameScope.Find<Popup>("PART_Popup");
        textBox = e.NameScope.Find<TextBox>("TextBox");
        if (picker != null)
        {
            picker.ShowAlpha = typeof(T) != typeof(Color3);
            picker.ColorChanged += PickerColorChanged;
        }
        if (popup != null) popup.Opened += PopupOpened;
        Synchronize();
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == ValueProperty || change.Property == PaletteProperty || change.Property == IsMixedProperty) Synchronize();
        if (change.Property == SelectedPaletteColorProperty && !synchronizing && SelectedPaletteColor is { } entry)
            SetCurrentValue(ValueProperty, ConvertColor(entry.Value));
    }

    private void Synchronize()
    {
        var color = Value switch { Color value => value.ToColor4(), Color3 value => value.ToColor4(), Color4 value => value, _ => Color4.Black };
        synchronizing = true;
        try
        {
            picker?.SetCurrentValue(ColorPicker.ColorProperty, color);
            var selected = Palette?.FirstOrDefault(entry => entry.Value == color || new Color(entry.Value) == new Color(color));
            SetCurrentValue(SelectedPaletteColorProperty, selected?.Key == null ? null : selected);
            textBox?.SetCurrentValue(TextBox.TextProperty, IsMixed ? null
                : (string?)new ColorConverter().Convert(Value, typeof(string), null, System.Globalization.CultureInfo.InvariantCulture));
        }
        finally { synchronizing = false; }
    }

    private static T ConvertColor(Color4 color) => (T)(typeof(T) == typeof(Color3) ? (object)color.ToColor3()
        : typeof(T) == typeof(Color) ? (object)(Color)color : color);

    private void PickerColorChanged(object? sender, EventArgs e)
    {
        if (picker != null) SetCurrentValue(ValueProperty, ConvertColor(picker.Color));
    }

    private void PopupOpened(object? sender, EventArgs e) => picker?.Focus();

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (!e.Handled && e.Key == Key.Escape && popup?.IsOpen == true)
        {
            picker?.CancelDrag();
            popup.SetCurrentValue(Popup.IsOpenProperty, false);
            e.Handled = true;
        }
    }

    public T Value
    {
        get => GetValue(ValueProperty);
        set => SetValue(ValueProperty, value);
    }

    public string? PlaceholderText
    {
        get => GetValue(PlaceholderTextProperty);
        set => SetValue(PlaceholderTextProperty, value);
    }

    public IReadOnlyDictionary<string, Color4> Palette
    {
        get => GetValue(PaletteProperty);
        set => SetValue(PaletteProperty, value);
    }

    public KeyValuePair<string, Color4>? SelectedPaletteColor
    {
        get => GetValue(SelectedPaletteColorProperty);
        set => SetValue(SelectedPaletteColorProperty, value);
    }
}

public sealed class ColorEditor : ColorEditor<Color>;
public sealed class Color3Editor : ColorEditor<Color3>;
public sealed class Color4Editor : ColorEditor<Color4>;
