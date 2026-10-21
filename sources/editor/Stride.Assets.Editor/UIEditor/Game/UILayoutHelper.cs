// Copyright (c) .NET Foundation and Contributors (https://dotnetfoundation.org/ & https://stride3d.net) and Silicon Studio Corp. (https://www.siliconstudio.co.jp)
// Distributed under the MIT license. See the LICENSE.md file in the project root for more information.

using System;
using System.Linq;
using Stride.Assets.Presentation.ViewModels;
using Stride.Core.Annotations;
using Stride.Core.Mathematics;
using Stride.UI;
using Stride.UI.Controls;
using Stride.UI.Panels;

namespace Stride.Assets.Editor.UIEditor.Game;

/// <summary>
/// Helper class for updating the layout of a <see cref="UIElement"/>. Supports moving and resizing.
/// </summary>
internal static class UILayoutHelper
{
    public static bool Move([NotNull] UIElement element, ref Vector3 delta, float magnetDistance, ref Vector3 resolution)
    {
        ArgumentNullException.ThrowIfNull(element);

        if (element.VisualParent is ContentControl)
            return false;

        var parameters = new LayoutParameters
        {
            Left = true,
            Right = true,
            Top = true,
            Bottom = true,
            CanResize = false,
        };
        UpdateElementLayout(element, ref delta, magnetDistance, ref resolution, parameters);
        return true;
    }

    public static bool Resize([NotNull] UIElement element, ResizingDirection resizingDirection, ref Vector3 delta, float magnetDistance, ref Vector3 resolution)
    {
        ArgumentNullException.ThrowIfNull(element);

        var parameters = new LayoutParameters
        {
            Left = resizingDirection.HasFlag(ResizingDirection.Left),
            Right = resizingDirection.HasFlag(ResizingDirection.Right),
            Top = resizingDirection.HasFlag(ResizingDirection.Top),
            Bottom = resizingDirection.HasFlag(ResizingDirection.Bottom),
            CanResize = true,
        };
        UpdateElementLayout(element, ref delta, magnetDistance, ref resolution, parameters);
        return true;
    }

    private class RectData
    {
        public RectangleF Container;
        public RectangleF Element;
        public RectangleF Parent;
        public RectangleF[] Siblings = [];
    }

    private struct LayoutParameters
    {
        public bool Left;
        public bool Right;
        public bool Top;
        public bool Bottom;
        public bool CanResize;
    }

    private static void AdjustAlignmentInContainer(ref RectangleF elementRect, ref RectangleF containerRect, out HorizontalAlignment horizontalAlignment, out VerticalAlignment verticalAlignment)
    {
        var containerCenter = containerRect.Center;
        horizontalAlignment = HorizontalAlignment.Stretch;
        if (elementRect.Width < 0.5f * containerRect.Width)
        {
            if (elementRect.Right < containerCenter.X)
                horizontalAlignment = HorizontalAlignment.Left;
            else if (elementRect.Left > containerCenter.X)
                horizontalAlignment = HorizontalAlignment.Right;
        }
        verticalAlignment = VerticalAlignment.Stretch;
        if (elementRect.Height < 0.5f * containerRect.Height)
        {
            if (elementRect.Bottom < containerCenter.Y)
                verticalAlignment = VerticalAlignment.Top;
            else if (elementRect.Top > containerCenter.Y)
                verticalAlignment = VerticalAlignment.Bottom;
        }
    }

    private static RectData ExtractRects([NotNull] UIElement element, ref Vector3 resolution)
    {
        var rects = new RectData
        {
            Element = new RectangleF(element.RenderOffsets.X, element.RenderOffsets.Y, element.ActualWidth, element.ActualHeight)
        };
        var parent = element.VisualParent;
        if (parent is GridBase)
        {
            var rowIndex = element.GetGridRow();
            var rowSpan = element.GetGridRowSpan();
            var colIndex = element.GetGridColumn();
            var colSpan = element.GetGridColumnSpan();
            if (parent is Grid grid)
            {
                var actualColumnDefinitions = grid.ActualColumnDefinitions;
                var actualRowDefinitions = grid.ActualRowDefinitions;

                var accWidth = 0.0f;
                for (var i = 0; i < colIndex && i < actualColumnDefinitions.Count; i++)
                {
                    accWidth += actualColumnDefinitions[i].ActualSize;
                }
                var accHeight = 0.0f;
                for (var i = 0; i < rowIndex && i < actualRowDefinitions.Count; i++)
                {
                    accHeight += actualRowDefinitions[i].ActualSize;
                }
                rects.Parent = new RectangleF(-accWidth, -accHeight, parent.ActualWidth, parent.ActualHeight);
                accWidth = 0.0f;
                for (var i = colIndex; i < colIndex + colSpan && i < actualColumnDefinitions.Count; i++)
                {
                    accWidth += actualColumnDefinitions[i].ActualSize;
                }
                accHeight = 0.0f;
                for (var i = rowIndex; i < rowIndex + rowSpan && i < actualRowDefinitions.Count; i++)
                {
                    accHeight += actualRowDefinitions[i].ActualSize;
                }
                rects.Container = new RectangleF(0, 0, accWidth, accHeight);
            }
            if (parent is UniformGrid uniformGrid)
            {
                var cellWidth = uniformGrid.ActualWidth / uniformGrid.Columns;
                var cellHeight = uniformGrid.ActualHeight / uniformGrid.Rows;
                rects.Parent = new RectangleF(
                    -MathUtil.Clamp(colIndex, 0, uniformGrid.Columns - 1) * cellWidth,
                    -MathUtil.Clamp(rowIndex, 0, uniformGrid.Rows - 1) * cellHeight,
                    parent.ActualWidth, parent.ActualHeight);
                rects.Container = new RectangleF(
                    0, 0,
                    MathUtil.Clamp(uniformGrid.Columns - colIndex, 1, colSpan) * cellWidth,
                    MathUtil.Clamp(uniformGrid.Rows - rowIndex, 1, rowSpan) * cellHeight);
            }
            rects.Siblings = (from c in parent.VisualChildren where c != element && c.GetGridRow() == rowIndex && c.GetGridColumn() == colIndex select new RectangleF(c.RenderOffsets.X, c.RenderOffsets.Y, c.ActualWidth, c.ActualHeight)).ToArray();
        }
        else if (parent != null)
        {
            rects.Parent = new RectangleF(0, 0, parent.ActualWidth, parent.ActualHeight);
            rects.Container = rects.Parent;
            rects.Siblings = (from c in parent.VisualChildren where c != element select new RectangleF(c.RenderOffsets.X, c.RenderOffsets.Y, c.ActualWidth, c.ActualHeight)).ToArray();
        }
        else
        {
            rects.Parent = new RectangleF(0, 0, resolution.X, resolution.Y);
            rects.Container = rects.Parent;
            rects.Siblings = [];
        }
        return rects;
    }

    private static void MagnetizeBottom(ref RectangleF elementRect, ref RectangleF magnetRect, ref bool verticallyMagnetized, float magnetDistance, bool resize)
    {
        if (verticallyMagnetized)
            return;

        var diffTop = elementRect.Bottom - magnetRect.Top;
        if (Math.Abs(diffTop) < magnetDistance)
        {
            if (resize)
                elementRect.Height -= diffTop;
            else
                elementRect.Top = magnetRect.Top - elementRect.Height;
            verticallyMagnetized = true;
            return;
        }

        var diffBottom = elementRect.Bottom - magnetRect.Bottom;
        if (Math.Abs(diffBottom) < magnetDistance)
        {
            if (resize)
                elementRect.Height -= diffBottom;
            else
                elementRect.Top = magnetRect.Bottom - elementRect.Height;
            verticallyMagnetized = true;
        }
    }

    private static void MagnetizeLeft(ref RectangleF elementRect, ref RectangleF magnetRect, ref bool horizontallyMagnetized, float magnetDistance, bool resize)
    {
        if (horizontallyMagnetized)
            return;

        var diffLeft = elementRect.Left - magnetRect.Left;
        if (Math.Abs(diffLeft) < magnetDistance)
        {
            elementRect.Left = magnetRect.Left;
            if (resize)
                elementRect.Width += diffLeft;
            horizontallyMagnetized = true;
            return;
        }

        var diffRight = elementRect.Left - magnetRect.Right;
        if (Math.Abs(elementRect.Left - magnetRect.Right) < magnetDistance)
        {
            elementRect.Left = magnetRect.Right;
            if (resize)
                elementRect.Width += diffRight;
            horizontallyMagnetized = true;
        }
    }

    private static void MagnetizeTop(ref RectangleF elementRect, ref RectangleF magnetRect, ref bool verticallyMagnetized, float magnetDistance, bool resize)
    {
        if (verticallyMagnetized)
            return;

        var diffTop = elementRect.Top - magnetRect.Top;
        if (Math.Abs(diffTop) < magnetDistance)
        {
            elementRect.Top = magnetRect.Top;
            if (resize)
                elementRect.Height += diffTop;
            verticallyMagnetized = true;
            return;
        }

        var diffBottom = elementRect.Top - magnetRect.Bottom;
        if (Math.Abs(diffBottom) < magnetDistance)
        {
            elementRect.Top = magnetRect.Bottom;
            if (resize)
                elementRect.Height += diffBottom;
            verticallyMagnetized = true;
        }
    }

    private static void MagnetizeRight(ref RectangleF elementRect, ref RectangleF magnetRect, ref bool horizontallyMagnetized, float magnetDistance, bool resize)
    {
        if (horizontallyMagnetized)
            return;

        var diffLeft = elementRect.Right - magnetRect.Left;
        if (Math.Abs(diffLeft) < magnetDistance)
        {
            if (resize)
                elementRect.Width -= diffLeft;
            else
                elementRect.Left = magnetRect.Left - elementRect.Width;
            horizontallyMagnetized = true;
            return;
        }

        var diffRight = elementRect.Right - magnetRect.Right;
        if (Math.Abs(diffRight) < magnetDistance)
        {
            if (resize)
                elementRect.Width -= diffRight;
            else
                elementRect.Left = magnetRect.Right - elementRect.Width;
            horizontallyMagnetized = true;
        }
    }

    private static void UpdateElementLayout([NotNull] UIElement element, ref Vector3 delta, float magnetDistance, ref Vector3 resolution, LayoutParameters parameters)
    {
        var rects = ExtractRects(element, ref resolution);
        var currentElementRect = rects.Element;
        if (parameters.Left)
        {
            rects.Element.X += delta.X;
            rects.Element.Width -= delta.X;
        }
        if (parameters.Right)
            rects.Element.Width += delta.X;
        if (parameters.Top)
        {
            rects.Element.Y += delta.Y;
            rects.Element.Height -= delta.Y;
        }
        if (parameters.Bottom)
            rects.Element.Height += delta.Y;

        var horizontallyMagnetized = false;
        var verticallyMagnetized = false;
        var contentControl = element.VisualParent as ContentControl;
        if (contentControl == null || !float.IsNaN(contentControl.Width))
        {
            if (parameters.Left)
                MagnetizeLeft(ref rects.Element, ref rects.Parent, ref horizontallyMagnetized, magnetDistance, parameters.CanResize);
            if (parameters.Right)
                MagnetizeRight(ref rects.Element, ref rects.Parent, ref horizontallyMagnetized, magnetDistance, parameters.CanResize);
        }
        if (contentControl == null || !float.IsNaN(contentControl.Height))
        {
            if (parameters.Top)
                MagnetizeTop(ref rects.Element, ref rects.Parent, ref verticallyMagnetized, magnetDistance, parameters.CanResize);
            if (parameters.Bottom)
                MagnetizeBottom(ref rects.Element, ref rects.Parent, ref verticallyMagnetized, magnetDistance, parameters.CanResize);
        }
        if (rects.Parent != rects.Container)
        {
            if (parameters.Left)
                MagnetizeLeft(ref rects.Element, ref rects.Container, ref horizontallyMagnetized, magnetDistance, parameters.CanResize);
            if (parameters.Right)
                MagnetizeRight(ref rects.Element, ref rects.Container, ref horizontallyMagnetized, magnetDistance, parameters.CanResize);
            if (parameters.Top)
                MagnetizeTop(ref rects.Element, ref rects.Container, ref verticallyMagnetized, magnetDistance, parameters.CanResize);
            if (parameters.Bottom)
                MagnetizeBottom(ref rects.Element, ref rects.Container, ref verticallyMagnetized, magnetDistance, parameters.CanResize);
        }
        for (var i = 0; i < rects.Siblings.Length; i++)
        {
            if (parameters.Left)
                MagnetizeLeft(ref rects.Element, ref rects.Siblings[i], ref horizontallyMagnetized, magnetDistance, parameters.CanResize);
            if (parameters.Right)
                MagnetizeRight(ref rects.Element, ref rects.Siblings[i], ref horizontallyMagnetized, magnetDistance, parameters.CanResize);
            if (parameters.Top)
                MagnetizeTop(ref rects.Element, ref rects.Siblings[i], ref verticallyMagnetized, magnetDistance, parameters.CanResize);
            if (parameters.Bottom)
                MagnetizeBottom(ref rects.Element, ref rects.Siblings[i], ref verticallyMagnetized, magnetDistance, parameters.CanResize);
        }

        var margin = new Thickness
        {
            Left = rects.Element.Left,
            Top = rects.Element.Top
        };
        var horizontalAlignment = element.HorizontalAlignment;
        var verticalAlignment = element.VerticalAlignment;
        if (element.VisualParent is Canvas)
        {
            horizontalAlignment = HorizontalAlignment.Left;
            verticalAlignment = VerticalAlignment.Top;
        }
        else if (element.VisualParent is ContentControl)
        {
            margin = element.Margin;
        }
        else
        {
            margin.Right = rects.Container.Right - rects.Element.Right;
            margin.Bottom = rects.Container.Bottom - rects.Element.Bottom;

            if (element.VisualParent is StackPanel stackPanel)
            {
                if (stackPanel.Orientation == Orientation.Horizontal)
                    horizontalAlignment = HorizontalAlignment.Left;
                else
                    verticalAlignment = VerticalAlignment.Top;
            }
            else if (rects.Container == rects.Parent)
            {
                AdjustAlignmentInContainer(ref rects.Element, ref rects.Container, out horizontalAlignment, out verticalAlignment);
            }

            switch (horizontalAlignment)
            {
                case HorizontalAlignment.Left:
                    var overLeft = rects.Container.Width - (margin.Left + rects.Element.Width);
                    margin.Right = Math.Min(overLeft, 0);
                    break;
                case HorizontalAlignment.Center:
                    horizontalAlignment = HorizontalAlignment.Stretch;
                    break;
                case HorizontalAlignment.Right:
                    var overRight = rects.Container.Width - (margin.Right + rects.Element.Width);
                    margin.Left = Math.Min(overRight, 0);
                    break;
            }
            switch (verticalAlignment)
            {
                case VerticalAlignment.Bottom:
                    var overBottom = rects.Container.Height - (margin.Bottom + rects.Element.Height);
                    margin.Top = Math.Min(overBottom, 0);
                    break;
                case VerticalAlignment.Center:
                    verticalAlignment = VerticalAlignment.Stretch;
                    break;
                case VerticalAlignment.Top:
                    var overTop = rects.Container.Height - (margin.Top + rects.Element.Height);
                    margin.Bottom = Math.Min(overTop, 0);
                    break;
            }
        }

        element.Margin = margin;
        element.HorizontalAlignment = horizontalAlignment;
        element.VerticalAlignment = verticalAlignment;
        element.Width = MathUtil.Clamp(rects.Element.Width, element.MinimumWidth, element.MaximumWidth);
        element.Height = MathUtil.Clamp(rects.Element.Height, element.MinimumHeight, element.MaximumHeight);

        delta = new Vector3
        {
            X = parameters.Left ? rects.Element.Left - currentElementRect.Left : rects.Element.Right - currentElementRect.Right,
            Y = parameters.Top ? rects.Element.Top - currentElementRect.Top : rects.Element.Bottom - currentElementRect.Bottom,
            Z = 0
        };
    }
}

