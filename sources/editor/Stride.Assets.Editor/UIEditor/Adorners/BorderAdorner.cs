// Copyright (c) .NET Foundation and Contributors (https://dotnetfoundation.org/ & https://stride3d.net) and Silicon Studio Corp. (https://www.siliconstudio.co.jp)
// Distributed under the MIT license. See the LICENSE.md file in the project root for more information.

using Stride.Assets.Editor.UIEditor.Game;
using Stride.Core.Mathematics;
using Stride.UI;
using Stride.UI.Controls;

namespace Stride.Assets.Editor.UIEditor.Adorners;

/// <summary>
/// Represents an adorner with a <see cref="Border"/> visual.
/// </summary>
internal abstract class BorderAdorner : AdornerBase<Border>
{
    private float borderThickness;
    private Vector3 size;

    protected BorderAdorner(UIEditorGameAdornerService service, UIElement gameSideElement)
        : base(service, gameSideElement)
    {
        Visual = new Border
        {
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Top,
        };

        InitializeAttachedProperties();
    }

    public Color BackgroundColor
    {
        get => Visual.BackgroundColor;
        set => Visual.BackgroundColor = value;
    }

    public Color BorderColor
    {
        get => Visual.BorderColor;
        set => Visual.BorderColor = value;
    }

    public float BorderThickness
    {
        get => borderThickness;
        set
        {
            borderThickness = value;
            UpdateBorderThickness();
            UpdateSize();
        }
    }

    public Vector3 Size
    {
        get => size;
        set
        {
            size = value;
            UpdateSize();
        }
    }

    public sealed override Border Visual { get; }

    protected virtual void UpdateBorderThickness()
    {
        Visual.BorderThickness = Thickness.UniformCuboid(borderThickness);
    }

    protected virtual void UpdateSize()
    {
        Visual.Size = size + new Vector3(BorderThickness * 2);
    }
}

