// Copyright (c) .NET Foundation and Contributors (https://dotnetfoundation.org/ & https://stride3d.net)
// Distributed under the MIT license. See the LICENSE.md file in the project root for more information.

using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Stride.Core.Assets.Editor.ViewModels;
using Stride.Core.Assets.Presentation.ViewModels;
using Stride.Core.Presentation.Avalonia.Extensions;

namespace Stride.GameStudio.Avalonia.Views;

public partial class AssetPickerWindow : Window
{
    public AssetPickerWindow()
    {
        InitializeComponent();
        Opened += OnOpened;
    }

    private AssetPickerViewModel Picker => (AssetPickerViewModel)DataContext!;

    private async void OnOpened(object? sender, EventArgs e)
    {
        Picker.Initialize();
        AssetList.SelectionMode = Picker.AllowMultiSelection ? SelectionMode.Multiple : SelectionMode.Single;
        var location = Picker.SelectedLocation is AssetMountPointViewModel root ? root.Package : Picker.SelectedLocation;
        DirectoryTree.SetCurrentValue(TreeView.SelectedItemProperty, location);
        if (location != null)
            await DirectoryTree.BringItemToView(location, GetTreeParent, () => !IsVisible);
        if (!IsVisible) return;
        if (Picker.InitialAsset != null) AssetList.ScrollIntoView(Picker.InitialAsset);
        SearchBox.Focus();
        SearchBox.SelectAll();
    }

    private object? GetTreeParent(object item) => item switch
    {
        DirectoryViewModel directory => directory.Parent is AssetMountPointViewModel root ? root.Package : directory.Parent,
        PackageViewModel package => Picker.Session.PackageCategories.Values.FirstOrDefault(c => c.Content.Contains(package)),
        _ => null
    };

    private void AllPackagesClicked(object? sender, RoutedEventArgs e) => DirectoryTree.SetCurrentValue(TreeView.SelectedItemProperty, null);
    private void SelectClicked(object? sender, RoutedEventArgs e) => AcceptSelection();
    private void CancelClicked(object? sender, RoutedEventArgs e) => Close(false);
    private void AcceptSelection()
    {
        Picker.Refresh();
        if (Picker.CanAccept) Close(true);
    }
    private void AssetDoubleTapped(object? sender, TappedEventArgs e)
    {
        if (e.Source is Control source && source.FindAncestorOfType<ListBoxItem>(true)?.DataContext is AssetViewModel asset
            && Picker.SelectedAssets.Contains(asset)) AcceptSelection();
    }
    private void WindowKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape) { Close(false); e.Handled = true; }
        else if (e.Key == Key.Enter && Picker.CanAccept) { AcceptSelection(); e.Handled = true; }
    }
}
