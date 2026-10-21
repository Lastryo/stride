// Copyright (c) .NET Foundation and Contributors (https://dotnetfoundation.org/ & https://stride3d.net)
// Distributed under the MIT license. See the LICENSE.md file in the project root for more information.

using System;
using System.Collections.Specialized;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using Stride.Core.Assets.Editor.ViewModels;
using Stride.Core.Assets.Presentation.ViewModels;
using Stride.Core.Presentation.Avalonia.Extensions;
using Stride.GameStudio.Avalonia.ViewModels;

namespace Stride.GameStudio.Avalonia.Views;

public partial class SolutionExplorerView : UserControl
{
    private SessionViewModel? currentSession;
    private int bringToViewToken;

    public SolutionExplorerView()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }

    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        HookSession(DataContext as SessionViewModel);
    }

    private void OnLoaded(object? sender, RoutedEventArgs e)
    {
        HookSession(DataContext as SessionViewModel);
    }

    private void OnUnloaded(object? sender, RoutedEventArgs e)
    {
        UnhookSession();
    }

    private void HookSession(SessionViewModel? session)
    {
        if (ReferenceEquals(currentSession, session))
            return;

        UnhookSession();

        currentSession = session;
        if (currentSession != null)
        {
            currentSession.AssetCollection.SelectedLocations.CollectionChanged += OnSelectedLocationsChanged;
            if (currentSession.AssetCollection.SelectedLocations.Count > 0)
            {
                var last = currentSession.AssetCollection.SelectedLocations.Last();
                if (last != null)
                    _ = BringItemToView(last);
            }
        }
    }

    private void UnhookSession()
    {
        if (currentSession != null)
        {
            currentSession.AssetCollection.SelectedLocations.CollectionChanged -= OnSelectedLocationsChanged;
            currentSession = null;
        }
    }

    private void OnSelectedLocationsChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.Action != NotifyCollectionChangedAction.Remove)
        {
            object? target = null;
            if (e.NewItems != null && e.NewItems.Count > 0)
            {
                target = e.NewItems[e.NewItems.Count - 1];
            }
            else if (e.Action == NotifyCollectionChangedAction.Reset && currentSession?.AssetCollection.SelectedLocations.Count > 0)
            {
                target = currentSession.AssetCollection.SelectedLocations.Last();
            }

            if (target != null)
            {
                _ = BringItemToView(target);
            }
        }
    }

    public Task<bool> BringItemToView(object item)
    {
        var token = ++bringToViewToken;
        return SolutionTreeView.BringItemToView(item, ResolveParent, () => token != bringToViewToken);
    }

    public static object? ResolveParent(object? item)
    {
        return item switch
        {
            DirectoryViewModel dir => (object?)dir.Parent ?? dir.Package,
            MountPointViewModel mount => mount.Package,
            PackageViewModel pkg => pkg.Session?.PackageCategories?.Values?.FirstOrDefault(c => c.Content.Contains(pkg)),
            AssetViewModel asset => asset.Directory,
            IChildViewModel child => child.GetParent(),
            _ => null
        };
    }

    private void ExpandAll_OnClick(object? sender, RoutedEventArgs e) => SetExpanded(true);

    private void CollapseAll_OnClick(object? sender, RoutedEventArgs e) => SetExpanded(false);

    private void SetExpanded(bool expanded)
    {
        foreach (var item in SolutionTreeView.GetVisualDescendants().OfType<TreeViewItem>())
            item.IsExpanded = expanded;
    }

    private void ClosePanel_OnClick(object? sender, RoutedEventArgs e)
    {
        if (this.FindAncestorOfType<MainView>()?.DataContext is MainViewModel mainVm)
            mainVm.Panels.SessionExplorerPanelVisible = false;
    }

    private void OnSolutionContextMenuOpened(object? sender, RoutedEventArgs e)
    {
        if (sender is ContextMenu contextMenu)
            contextMenu.DataContext = DataContext;
    }

    private void OnTreeViewPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        var point = e.GetCurrentPoint(SolutionTreeView);
        if (point.Properties.IsRightButtonPressed)
        {
            var treeItem = (e.Source as Visual)?.FindAncestorOfType<TreeViewItem>(true);
            if (treeItem?.DataContext != null && DataContext is SessionViewModel session)
            {
                if (!session.AssetCollection.SelectedLocations.Contains(treeItem.DataContext))
                {
                    session.AssetCollection.SelectedLocations.Clear();
                    session.AssetCollection.SelectedLocations.Add(treeItem.DataContext);
                }
            }
        }
    }
}
