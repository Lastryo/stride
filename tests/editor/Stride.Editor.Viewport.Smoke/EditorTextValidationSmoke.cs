// Copyright (c) .NET Foundation and Contributors (https://dotnetfoundation.org/ & https://stride3d.net)
// Distributed under the MIT license. See the LICENSE.md file in the project root for more information.

using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Input;
using Avalonia.Xaml.Interactivity;
using Stride.Core.Assets.Editor.Avalonia.Converters;
using Stride.Core.Presentation.Avalonia.Behaviors;

internal static class EditorTextValidationSmoke
{
    public static async Task Check(Control target)
    {
        var source = new TextSource();
        var box = new TextBox { Width = 160 };
        box.Bind(TextBox.TextProperty, new Binding(nameof(TextSource.Value))
        {
            Source = source, Mode = BindingMode.TwoWay, UpdateSourceTrigger = UpdateSourceTrigger.Explicit,
            Converter = new DifferentValuesToString(),
        });
        Interaction.GetBehaviors(box).Add(new TextBoxValidationBehavior());
        var flyout = new Flyout { Content = box };
        flyout.ShowAt(target);
        try
        {
            await Focus();
            KeyPress(Key.Enter);
            if (source.Value != null) throw new Exception("Focusing/confirming an unchanged nullable string converted null to empty.");
            await Focus();
            box.SetCurrentValue(TextBox.TextProperty, "Camera name");
            if (source.Value != null) throw new Exception("The nullable string was committed before validation.");
            KeyPress(Key.Enter);
            if (source.Value != "Camera name") throw new Exception("Enter did not commit the edited string.");
            await Focus();
            box.SetCurrentValue(TextBox.TextProperty, "Cancelled name");
            KeyPress(Key.Escape);
            if (source.Value != "Camera name" || box.Text != "Camera name") throw new Exception("Escape did not cancel the string edit.");
            await Focus();
            box.SetCurrentValue(TextBox.TextProperty, string.Empty);
            KeyPress(Key.Enter);
            if (source.Value != string.Empty) throw new Exception("Clearing a non-empty string was not committed.");
            Console.WriteLine("PASS: nullable string remains null on focus/confirmation; edits commit on Enter, cancel on Escape and allow clearing.");
        }
        finally { flyout.Hide(); }
        async Task Focus() { box.Focus(); await Task.Delay(50); }
        void KeyPress(Key key) => box.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = key });
    }

    private sealed class TextSource
    {
        public string? Value { get; set; }
    }
}
