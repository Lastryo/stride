// Copyright (c) .NET Foundation and Contributors (https://dotnetfoundation.org/ & https://stride3d.net)
// Distributed under the MIT license. See the LICENSE.md file in the project root for more information.

using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Input;
using Avalonia.Interactivity;
using Stride.GameStudio.Avalonia.Layout;
using Stride.GameStudio.Avalonia.Settings;
using Stride.GameStudio.Avalonia.ViewModels;

namespace Stride.GameStudio.Avalonia.Views;

public partial class MainView : UserControl
{
    private MainViewModel? main;
    private DockingLayoutManager? docking;

    public MainView()
    {
        InitializeComponent();
        InitializeDocking();
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        if (main is not null)
        {
            main.PropertyChanged -= MainChanged;
            main.SessionClosing -= SessionClosing;
        }
        docking?.Dispose();
        docking = null;
        base.OnDataContextChanged(e);
        main = DataContext as MainViewModel;
        if (main is not null)
        {
            main.PropertyChanged += MainChanged;
            main.SessionClosing += SessionClosing;
        }
        InitializeDocking();
    }

    private void MainChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MainViewModel.Session))
        {
            docking?.Dispose();
            docking = null;
            WorkspaceDock.Layout = null;
            InitializeDocking();
        }
    }

    private void InitializeDocking(bool restore = true)
    {
        if (WorkspaceDock is null || main?.Session is null || docking is not null) return;
        docking = new DockingLayoutManager(main, id =>
        {
            var view = ((IDataTemplate)Resources[id + "Template"]!).Build(main)!;
            view.DataContext = main;
            return view;
        }, restore);
        WorkspaceDock.Layout = docking.Layout;
    }

    public void SaveLayout() => docking?.SaveLayout();

    private void SessionClosing(object? sender, EventArgs e) => SaveLayout();

    private void ResetLayout_OnClick(object? sender, RoutedEventArgs e)
    {
        docking?.Dispose();
        docking = null;
        GameStudioInternalSettings.DockLayout.SetValue(string.Empty);
        main?.Panels.SetAllPanelVisible();
        InitializeDocking(restore: false);
    }

    private void TogglePanel_OnClick(object? sender, RoutedEventArgs e)
    {
        if (sender is MenuItem { CommandParameter: string panel }) docking?.TogglePanel(panel);
    }

    public static KeyGesture? RedoGesture => Application.Current?.PlatformSettings?.HotkeyConfiguration.Redo.FirstOrDefault();
    public static KeyGesture? UndoGesture => Application.Current?.PlatformSettings?.HotkeyConfiguration.Undo.FirstOrDefault();
}
