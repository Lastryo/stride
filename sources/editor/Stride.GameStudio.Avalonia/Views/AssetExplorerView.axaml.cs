using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using Stride.Core.Assets.Editor.ViewModels;
using Stride.Core.Assets.Presentation.ViewModels;
using Stride.Core.Assets.Templates;

namespace Stride.GameStudio.Avalonia.Views;

public partial class AssetExplorerView : UserControl
{
    public static readonly StyledProperty<double> ThumbnailSizeProperty =
        AvaloniaProperty.Register<AssetExplorerView, double>(nameof(ThumbnailSize), 96,
            validate: value => double.IsFinite(value) && value >= 16 && value <= 128);

    public double ThumbnailSize { get => GetValue(ThumbnailSizeProperty); set => SetValue(ThumbnailSizeProperty, value); }

    private void ZoomIn_OnClick(object? sender, RoutedEventArgs e) => ThumbnailSize = Math.Min(128, ThumbnailSize + 16);

    private void ZoomOut_OnClick(object? sender, RoutedEventArgs e) => ThumbnailSize = Math.Max(16, ThumbnailSize - 16);

    protected override void OnPointerWheelChanged(PointerWheelEventArgs e)
    {
        if (e.KeyModifiers == KeyModifiers.Control && e.Delta.Y != 0)
        {
            ThumbnailSize = Math.Clamp(ThumbnailSize + Math.Sign(e.Delta.Y) * 16, 16, 128);
            e.Handled = true;
        }
        base.OnPointerWheelChanged(e);
    }

    public AssetExplorerView()
    {
        InitializeComponent();
    }

    private void AddAssetFlyout_OnOpening(object? sender, EventArgs e)
    {
        if (sender is Flyout { Content: AddAssetView view } flyout && DataContext is AssetCollectionViewModel collection)
            view.Open(collection, flyout, AddAssetButton);
    }

    private void AssetListContextMenu_OnOpened(object? sender, RoutedEventArgs e)
    {
        ContextAddAssetSubMenu.ItemsSource = BuildAddAssetMenuItems();
    }

    private List<MenuItem> BuildAddAssetMenuItems()
    {
        var result = new List<MenuItem>();
        if (DataContext is not AssetCollectionViewModel collection) return result;
        var session = collection.Session;
        var templates = session.FindTemplates(TemplateScope.Asset).ToList();
        var groups = templates.GroupBy(AddAssetTemplateItem.GetGroup)
                              .OrderBy(g => g.Key);

        foreach (var group in groups)
        {
            var children = new List<MenuItem>();
            foreach (var template in group.OrderBy(t => t.Order).ThenBy(t => t.Name))
            {
                var item = new MenuItem
                {
                    Header = template.Name,
                    Command = collection.CreateAssetFromTemplateCommand,
                    CommandParameter = template,
                };
                if (!string.IsNullOrWhiteSpace(template.Description))
                {
                    ToolTip.SetTip(item, template.Description);
                }
                children.Add(item);
            }
            var groupItem = new MenuItem
            {
                Header = group.Key,
                ItemsSource = children,
            };
            result.Add(groupItem);
        }
        return result;
    }

    private void OnAssetCardPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        var point = e.GetCurrentPoint(sender as Visual);
        if (point.Properties.IsRightButtonPressed && sender is StyledElement element && element.DataContext is { } item)
        {
            if (DataContext is AssetCollectionViewModel collection && !collection.SelectedContent.Contains(item))
            {
                collection.SelectContent(item);
            }
        }
    }

    private void OnAssetsKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Source is global::Avalonia.Visual source && source.FindAncestorOfType<TextBox>(true) != null) return;
        if (e.Key == Key.Delete && e.KeyModifiers == KeyModifiers.None && DataContext is AssetCollectionViewModel assets
            && assets.DeleteSelectionCommand.CanExecute(null))
        {
            e.Handled = true;
            assets.DeleteSelectionCommand.Execute(null);
            return;
        }
        if (e.Key == Key.D && e.KeyModifiers == KeyModifiers.Control && DataContext is AssetCollectionViewModel collection
            && collection.DuplicateSelectionCommand.CanExecute(null))
        {
            e.Handled = true;
            collection.DuplicateSelectionCommand.Execute(null);
            return;
        }
        if ((e.Key == Key.Enter || e.Key == Key.Return)
            && (e.KeyModifiers == KeyModifiers.None || e.KeyModifiers == KeyModifiers.Control)
            && DataContext is AssetCollectionViewModel assetCol
            && assetCol.Session.EditSelectedContentCommand.CanExecute(null))
        {
            e.Handled = true;
            assetCol.Session.EditSelectedContentCommand.Execute(null);
            return;
        }
        if (e.Key == Key.R && e.KeyModifiers == KeyModifiers.Control
            && DataContext is AssetCollectionViewModel sourceCollection
            && sourceCollection.Session.SourceTracker.UpdateAllAssetsWithModifiedSourceCommand.CanExecute(null))
        {
            e.Handled = true;
            sourceCollection.Session.SourceTracker.UpdateAllAssetsWithModifiedSourceCommand.Execute(null);
            return;
        }
        if ((e.Key == Key.I || e.Key == Key.Q) && e.KeyModifiers == KeyModifiers.Control)
        {
            e.Handled = true;
            AddAssetButton.Flyout?.ShowAt(AddAssetButton);
            return;
        }
    }
}
