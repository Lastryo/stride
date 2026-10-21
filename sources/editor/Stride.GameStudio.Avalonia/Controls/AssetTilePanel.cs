// Copyright (c) .NET Foundation and Contributors (https://dotnetfoundation.org/ & https://stride3d.net)
// Distributed under the MIT license. See the LICENSE.md file in the project root for more information.

using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;

namespace Stride.GameStudio.Avalonia.Controls;

/// <summary>Matches the slots and column spacing of WPF's AssetTileView.</summary>
public sealed class AssetTilePanel : Panel, INavigableContainer
{
    public static readonly StyledProperty<double> ThumbnailSizeProperty =
        AvaloniaProperty.Register<AssetTilePanel, double>(nameof(ThumbnailSize), 96,
            validate: value => double.IsFinite(value) && value >= 16);

    static AssetTilePanel() => AffectsMeasure<AssetTilePanel>(ThumbnailSizeProperty);

    public double ThumbnailSize { get => GetValue(ThumbnailSizeProperty); set => SetValue(ThumbnailSizeProperty, value); }

    private const double MinimumSpacing = 4;
    private Size SlotSize => new(ThumbnailSize + 18, ThumbnailSize + 64);

    private int Columns(double width) => double.IsPositiveInfinity(width)
        ? Math.Max(1, Children.Count)
        : Math.Max(1, Math.Min(Children.Count, (int)Math.Ceiling(width - MinimumSpacing) / (int)(SlotSize.Width + MinimumSpacing)));

    protected override Size MeasureOverride(Size availableSize)
    {
        foreach (var child in Children) child.Measure(SlotSize);
        var columns = Columns(availableSize.Width);
        var rows = (Children.Count + columns - 1) / columns;
        return new Size(double.IsPositiveInfinity(availableSize.Width)
                ? columns * (SlotSize.Width + MinimumSpacing) + MinimumSpacing : availableSize.Width,
            MinimumSpacing + rows * (SlotSize.Height + MinimumSpacing));
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        var columns = Columns(finalSize.Width);
        var spacing = columns <= 1 ? MinimumSpacing
            : Math.Clamp((finalSize.Width - columns * SlotSize.Width - 2 * MinimumSpacing) / (columns - 1),
                MinimumSpacing, ThumbnailSize * 0.375);
        for (var i = 0; i < Children.Count; i++)
            Children[i].Arrange(new Rect(
                new Point(MinimumSpacing + i % columns * (SlotSize.Width + spacing),
                    MinimumSpacing + i / columns * (SlotSize.Height + MinimumSpacing)), SlotSize));
        return finalSize;
    }

    public IInputElement? GetControl(NavigationDirection direction, IInputElement? from, bool wrap)
    {
        if (Children.Count == 0) return null;
        var index = from is Control control ? Children.IndexOf(control) : -1;
        if (index < 0) return direction is NavigationDirection.Last or NavigationDirection.Previous ? Children[^1] : Children[0];
        var width = Bounds.Width > 0 ? Bounds.Width : DesiredSize.Width;
        var columns = Columns(width);
        var target = direction switch
        {
            NavigationDirection.First => 0,
            NavigationDirection.Last => Children.Count - 1,
            NavigationDirection.Left or NavigationDirection.Previous => index > 0 ? index - 1 : (wrap ? Children.Count - 1 : -1),
            NavigationDirection.Right or NavigationDirection.Next => index + 1 < Children.Count ? index + 1 : (wrap ? 0 : -1),
            NavigationDirection.Up => index - columns >= 0 ? index - columns : -1,
            NavigationDirection.Down => index + columns < Children.Count ? index + columns : -1,
            _ => -1,
        };
        return target >= 0 && target < Children.Count ? Children[target] : null;
    }
}
