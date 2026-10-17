// Copyright (c) .NET Foundation and Contributors (https://dotnetfoundation.org/ & https://stride3d.net)
// Distributed under the MIT license. See the LICENSE.md file in the project root for more information.

using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using Stride.Assets.Editor.ViewModels;
using Stride.Core.Assets.Editor.Annotations;
using Stride.Core.Assets.Editor.Editors;

namespace Stride.Assets.Editor.Avalonia.Views;

[AssetEditorView<EntityHierarchyEditorViewModel>]
public partial class EntityHierarchyEditorView : UserControl, IAssetEditorView
{
    public EntityHierarchyEditorView()
    {
        InitializeComponent();
    }

    private void OnHierarchyContextMenuOpened(object? sender, RoutedEventArgs e)
    {
        if (sender is ContextMenu contextMenu)
            contextMenu.DataContext = DataContext;
    }

    private void ExpandAll_OnClick(object? sender, RoutedEventArgs e) => SetExpanded(true);

    private void CollapseAll_OnClick(object? sender, RoutedEventArgs e) => SetExpanded(false);

    private void SetExpanded(bool expanded)
    {
        foreach (var item in HierarchyTreeView.GetVisualDescendants().OfType<TreeViewItem>())
            item.IsExpanded = expanded;
    }
}
