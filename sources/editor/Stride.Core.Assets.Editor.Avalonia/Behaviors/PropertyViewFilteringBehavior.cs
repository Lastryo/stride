// Copyright (c) .NET Foundation and Contributors (https://dotnetfoundation.org/ & https://stride3d.net) and Silicon Studio Corp. (https://www.siliconstudio.co.jp)
// Distributed under the MIT license. See the LICENSE.md file in the project root for more information.

using Avalonia;
using Avalonia.Xaml.Interactivity;
using Stride.Core.Presentation.Avalonia.Controls;
using Stride.Core.Presentation.Quantum.ViewModels;

namespace Stride.Core.Assets.Editor.Avalonia.Behaviors;

public sealed class PropertyViewFilteringBehavior : StyledElementBehavior<PropertyView>
{
    public static readonly StyledProperty<string?> FilterTokenProperty =
        AvaloniaProperty.Register<PropertyViewFilteringBehavior, string?>(nameof(FilterToken));

    static PropertyViewFilteringBehavior()
    {
        FilterTokenProperty.Changed.AddClassHandler<PropertyViewFilteringBehavior, string?>(OnFilterTokenChanged);
    }

    public string? FilterToken
    {
        get => GetValue(FilterTokenProperty);
        set => SetValue(FilterTokenProperty, value);
    }

    protected override void OnAttached()
    {
        base.OnAttached();
        if (AssociatedObject is { } propertyView)
        {
            propertyView.PrepareItem += OnPrepareItem;
            ApplyFilter();
        }
    }

    protected override void OnDetaching()
    {
        if (AssociatedObject is { } propertyView)
        {
            propertyView.PrepareItem -= OnPrepareItem;
        }
        base.OnDetaching();
    }

    private static void OnFilterTokenChanged(PropertyViewFilteringBehavior sender, AvaloniaPropertyChangedEventArgs<string?> e)
    {
        sender.ApplyFilter();
    }

    private void OnPrepareItem(object? sender, PropertyViewItemEventArgs e)
    {
        ApplyFilterToItem(e.Container, FilterToken);
    }

    private void ApplyFilter()
    {
        if (AssociatedObject is not { } propertyView)
            return;

        var token = FilterToken;
        foreach (var item in propertyView.Properties)
        {
            ApplyFilterToItem(item, token);
        }
    }

    private static void ApplyFilterToItem(PropertyViewItem item, string? token)
    {
        if (item.DataContext is NodeViewModel node)
        {
            item.IsVisible = string.IsNullOrWhiteSpace(token) || Match(node, token);
        }

        foreach (var child in item.Properties)
        {
            ApplyFilterToItem(child, token);
        }
    }

    private static bool Match(NodeViewModel node, string token)
    {
        return node.DisplayName.Contains(token, StringComparison.CurrentCultureIgnoreCase) ||
               node.Children.Any(x => Match(x, token));
    }
}
