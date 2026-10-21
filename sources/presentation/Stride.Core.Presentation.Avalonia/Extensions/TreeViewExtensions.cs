// Copyright (c) .NET Foundation and Contributors (https://dotnetfoundation.org/ & https://stride3d.net)
// Distributed under the MIT license. See the LICENSE.md file in the project root for more information.

using Avalonia.Controls;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace Stride.Core.Presentation.Avalonia.Extensions;

public static class TreeViewExtensions
{
    /// <summary>
    /// Expands all ancestor nodes and brings the container for <paramref name="item"/> into view in the <paramref name="treeView"/>.
    /// </summary>
    /// <param name="treeView">The tree view.</param>
    /// <param name="item">The data item to bring into view.</param>
    /// <param name="getParent">A function that returns the parent object of a given item, or null if the item is a root.</param>
    /// <param name="isCancelled">An optional callback returning true if the operation has been superseded or cancelled.</param>
    /// <returns>True if the item container was found and brought into view; otherwise, false.</returns>
    public static async Task<bool> BringItemToView(
        this TreeView treeView,
        object item,
        Func<object, object?> getParent,
        Func<bool>? isCancelled = null)
    {
        if (treeView == null || item == null || getParent == null)
            return false;

        if (treeView.FindVisualRoot() == null)
            return false;

        var path = new List<object> { item };
        var parent = getParent(item);
        var visited = new HashSet<object> { item };

        while (parent != null && visited.Add(parent))
        {
            path.Add(parent);
            parent = getParent(parent);
        }

        path.Reverse();

        for (int i = 0; i < path.Count; i++)
        {
            if (isCancelled != null && isCancelled())
                return false;

            var node = path[i];
            TreeViewItem? container = null;

            for (int attempt = 0; attempt < 3; attempt++)
            {
                container = treeView.GetVisualDescendants()
                    .OfType<TreeViewItem>()
                    .FirstOrDefault(t => ReferenceEquals(t.DataContext, node));

                if (container != null)
                    break;

                treeView.UpdateLayout();
                await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Loaded);

                if (isCancelled != null && isCancelled())
                    return false;
            }

            if (container == null)
                return false;

            if (i < path.Count - 1)
            {
                if (!container.IsExpanded)
                {
                    container.BringIntoView();
                    container.IsExpanded = true;
                    container.UpdateLayout();
                    await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Loaded);
                }
            }
            else
            {
                if (treeView.SelectedItems != null && treeView.SelectedItems.Contains(node))
                    container.IsSelected = true;

                container.BringIntoView();
                return true;
            }
        }

        return false;
    }
}

