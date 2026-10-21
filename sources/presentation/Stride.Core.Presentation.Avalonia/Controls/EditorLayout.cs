// Copyright (c) .NET Foundation and Contributors (https://dotnetfoundation.org/ & https://stride3d.net)
// Distributed under the MIT license. See the LICENSE.md file in the project root for more information.

using Avalonia;
using Avalonia.Controls;

namespace Stride.Core.Presentation.Avalonia.Controls;

/// <summary>
/// Shares the hierarchy column width between the shell and independently hosted asset editors.
/// </summary>
public sealed class EditorLayout : AvaloniaObject
{
    public static readonly AttachedProperty<EditorLayout?> StateProperty =
        AvaloniaProperty.RegisterAttached<EditorLayout, Control, EditorLayout?>("State", inherits: true);

    public static readonly StyledProperty<GridLength> HierarchyWidthProperty =
        AvaloniaProperty.Register<EditorLayout, GridLength>(nameof(HierarchyWidth), new GridLength(210));

    public GridLength HierarchyWidth
    {
        get => GetValue(HierarchyWidthProperty);
        set => SetValue(HierarchyWidthProperty, value);
    }

    public static EditorLayout? GetState(Control control) => control.GetValue(StateProperty);

    public static void SetState(Control control, EditorLayout? value) => control.SetValue(StateProperty, value);
}
