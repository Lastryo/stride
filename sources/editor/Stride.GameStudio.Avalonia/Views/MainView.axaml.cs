// Copyright (c) .NET Foundation and Contributors (https://dotnetfoundation.org/ & https://stride3d.net)
// Distributed under the MIT license. See the LICENSE.md file in the project root for more information.

using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Stride.Core.Presentation.Avalonia.Controls;
using Stride.GameStudio.Avalonia.Settings;
using Stride.GameStudio.Avalonia.ViewModels;

namespace Stride.GameStudio.Avalonia.Views;

public partial class MainView : UserControl
{
    private EditionPanelViewModel? currentPanels;

    public MainView()
    {
        InitializeComponent();
        RestoreLayout();
        EditorRows.SizeChanged += (_, _) => ConstrainHierarchyWidth();
        EditorLayout.GetState(this)!.PropertyChanged += (_, e) =>
        {
            if (e.Property == EditorLayout.HierarchyWidthProperty) ConstrainHierarchyWidth();
        };
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        if (currentPanels != null)
        {
            currentPanels.PropertyChanged -= OnPanelsPropertyChanged;
            currentPanels = null;
        }
        if (DataContext is MainViewModel mainVm)
        {
            currentPanels = mainVm.Panels;
            currentPanels.PropertyChanged += OnPanelsPropertyChanged;
            EnsureValidAssetTabSelection(currentPanels);
            EnsureValidPreviewTabSelection(currentPanels);
            UpdatePanelLayout();
        }
    }

    private void EnsureValidAssetTabSelection(EditionPanelViewModel panels)
    {
        if (AssetTabs.SelectedIndex == 0 && !panels.AssetViewPanelVisible)
        {
            if (panels.AssetLogPanelVisible) AssetTabs.SelectedIndex = 1;
            else if (panels.BuildLogPanelVisible) AssetTabs.SelectedIndex = 2;
        }
        else if (AssetTabs.SelectedIndex == 1 && !panels.AssetLogPanelVisible)
        {
            if (panels.AssetViewPanelVisible) AssetTabs.SelectedIndex = 0;
            else if (panels.BuildLogPanelVisible) AssetTabs.SelectedIndex = 2;
        }
        else if (AssetTabs.SelectedIndex == 2 && !panels.BuildLogPanelVisible)
        {
            if (panels.AssetViewPanelVisible) AssetTabs.SelectedIndex = 0;
            else if (panels.AssetLogPanelVisible) AssetTabs.SelectedIndex = 1;
        }
        else if (AssetTabs.SelectedIndex < 0)
        {
            if (panels.AssetViewPanelVisible) AssetTabs.SelectedIndex = 0;
            else if (panels.AssetLogPanelVisible) AssetTabs.SelectedIndex = 1;
            else if (panels.BuildLogPanelVisible) AssetTabs.SelectedIndex = 2;
        }
    }

    private void EnsureValidPreviewTabSelection(EditionPanelViewModel panels)
    {
        if (PreviewTabs.SelectedIndex == 0 && !panels.AssetPreviewPanelVisible)
        {
            if (panels.ReferencesPanelVisible) PreviewTabs.SelectedIndex = 2;
            else if (panels.ActionHistoryPanelVisible) PreviewTabs.SelectedIndex = 1;
        }
        else if (PreviewTabs.SelectedIndex == 1 && !panels.ActionHistoryPanelVisible)
        {
            if (panels.ReferencesPanelVisible) PreviewTabs.SelectedIndex = 2;
            else if (panels.AssetPreviewPanelVisible) PreviewTabs.SelectedIndex = 0;
        }
        else if (PreviewTabs.SelectedIndex == 2 && !panels.ReferencesPanelVisible)
        {
            if (panels.AssetPreviewPanelVisible) PreviewTabs.SelectedIndex = 0;
            else if (panels.ActionHistoryPanelVisible) PreviewTabs.SelectedIndex = 1;
        }
        else if (PreviewTabs.SelectedIndex < 0)
        {
            if (panels.ReferencesPanelVisible) PreviewTabs.SelectedIndex = 2;
            else if (panels.AssetPreviewPanelVisible) PreviewTabs.SelectedIndex = 0;
            else if (panels.ActionHistoryPanelVisible) PreviewTabs.SelectedIndex = 1;
        }
    }

    private void OnPanelsPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (DataContext is not MainViewModel mainVm) return;

        switch (e.PropertyName)
        {
            case nameof(EditionPanelViewModel.AssetViewPanelVisible):
                if (mainVm.Panels.AssetViewPanelVisible)
                    AssetTabs.SelectedIndex = 0;
                else
                    EnsureValidAssetTabSelection(mainVm.Panels);
                break;
            case nameof(EditionPanelViewModel.AssetLogPanelVisible):
                if (mainVm.Panels.AssetLogPanelVisible)
                    AssetTabs.SelectedIndex = 1;
                else
                    EnsureValidAssetTabSelection(mainVm.Panels);
                break;
            case nameof(EditionPanelViewModel.BuildLogPanelVisible):
                if (mainVm.Panels.BuildLogPanelVisible)
                    AssetTabs.SelectedIndex = 2;
                else
                    EnsureValidAssetTabSelection(mainVm.Panels);
                break;
            case nameof(EditionPanelViewModel.AssetPreviewPanelVisible):
                if (mainVm.Panels.AssetPreviewPanelVisible)
                    PreviewTabs.SelectedIndex = 0;
                else
                    EnsureValidPreviewTabSelection(mainVm.Panels);
                break;
            case nameof(EditionPanelViewModel.ActionHistoryPanelVisible):
                if (mainVm.Panels.ActionHistoryPanelVisible)
                    PreviewTabs.SelectedIndex = 1;
                else
                    EnsureValidPreviewTabSelection(mainVm.Panels);
                break;
            case nameof(EditionPanelViewModel.ReferencesPanelVisible):
                if (mainVm.Panels.ReferencesPanelVisible)
                    PreviewTabs.SelectedIndex = 2;
                else
                    EnsureValidPreviewTabSelection(mainVm.Panels);
                break;
        }

        UpdatePanelLayout();
    }

    private void UpdatePanelLayout()
    {
        if (DataContext is not MainViewModel mainVm) return;
        var panels = mainVm.Panels;

        // SessionColumns: Inspector column (Col 2)
        if (!panels.InspectorColumnVisible)
        {
            SessionColumns.ColumnDefinitions[0].Width = new GridLength(1, GridUnitType.Star);
            SessionColumns.ColumnDefinitions[1].Width = new GridLength(0);
            SessionColumns.ColumnDefinitions[2].Width = new GridLength(0);
            SessionColumns.ColumnDefinitions[2].MinWidth = 0;
        }
        else
        {
            SessionColumns.ColumnDefinitions[1].Width = new GridLength(4);
            SessionColumns.ColumnDefinitions[2].MinWidth = 280;
            SetInspectorRatio(GameStudioInternalSettings.InspectorWidthRatio.GetValue());
        }

        // EditorRows: 3D scene (Row 0) vs Bottom Row (Row 2)
        if (!panels.BottomRowVisible)
        {
            EditorRows.RowDefinitions[0].Height = new GridLength(1, GridUnitType.Star);
            EditorRows.RowDefinitions[1].Height = new GridLength(0);
            EditorRows.RowDefinitions[2].Height = new GridLength(0);
            EditorRows.RowDefinitions[2].MinHeight = 0;
        }
        else
        {
            EditorRows.RowDefinitions[1].Height = new GridLength(4);
            EditorRows.RowDefinitions[2].MinHeight = 140;
            SetRowRatio(EditorRows, GameStudioInternalSettings.EditorHeightRatio.GetValue(), 3.3 / 4.3);
        }

        // AssetColumns: SolutionExplorer (Col 0) vs BottomCenter (Col 2)
        if (!panels.SessionExplorerPanelVisible && !panels.BottomCenterPanelVisible)
        {
            AssetColumns.ColumnDefinitions[0].Width = new GridLength(0);
            AssetColumns.ColumnDefinitions[0].MinWidth = 0;
            AssetColumns.ColumnDefinitions[1].Width = new GridLength(0);
            AssetColumns.ColumnDefinitions[2].Width = new GridLength(0);
            AssetColumns.ColumnDefinitions[2].MinWidth = 0;
        }
        else if (!panels.SessionExplorerPanelVisible)
        {
            AssetColumns.ColumnDefinitions[0].Width = new GridLength(0);
            AssetColumns.ColumnDefinitions[0].MinWidth = 0;
            AssetColumns.ColumnDefinitions[1].Width = new GridLength(0);
            AssetColumns.ColumnDefinitions[2].Width = new GridLength(1, GridUnitType.Star);
            AssetColumns.ColumnDefinitions[2].MinWidth = 240;
        }
        else if (!panels.BottomCenterPanelVisible)
        {
            AssetColumns.ColumnDefinitions[0].Width = new GridLength(1, GridUnitType.Star);
            AssetColumns.ColumnDefinitions[0].MinWidth = 140;
            AssetColumns.ColumnDefinitions[1].Width = new GridLength(0);
            AssetColumns.ColumnDefinitions[2].Width = new GridLength(0);
            AssetColumns.ColumnDefinitions[2].MinWidth = 0;
        }
        else
        {
            AssetColumns.ColumnDefinitions[0].Width = EditorLayout.GetState(this)!.HierarchyWidth;
            AssetColumns.ColumnDefinitions[0].MinWidth = 140;
            AssetColumns.ColumnDefinitions[1].Width = new GridLength(5);
            AssetColumns.ColumnDefinitions[2].Width = new GridLength(1, GridUnitType.Star);
            AssetColumns.ColumnDefinitions[2].MinWidth = 240;
        }

        // InspectorRows: PropertyGrid (Row 0) vs BottomRight (Row 2)
        if (!panels.PropertyGridPanelVisible && panels.BottomRightPanelVisible)
        {
            InspectorRows.RowDefinitions[0].Height = new GridLength(0);
            InspectorRows.RowDefinitions[0].MinHeight = 0;
            InspectorRows.RowDefinitions[1].Height = new GridLength(0);
            InspectorRows.RowDefinitions[2].Height = new GridLength(1, GridUnitType.Star);
            InspectorRows.RowDefinitions[2].MinHeight = 140;
        }
        else if (panels.PropertyGridPanelVisible && !panels.BottomRightPanelVisible)
        {
            InspectorRows.RowDefinitions[0].Height = new GridLength(1, GridUnitType.Star);
            InspectorRows.RowDefinitions[0].MinHeight = 200;
            InspectorRows.RowDefinitions[1].Height = new GridLength(0);
            InspectorRows.RowDefinitions[2].Height = new GridLength(0);
            InspectorRows.RowDefinitions[2].MinHeight = 0;
        }
        else if (panels.PropertyGridPanelVisible && panels.BottomRightPanelVisible)
        {
            InspectorRows.RowDefinitions[0].MinHeight = 200;
            InspectorRows.RowDefinitions[1].Height = new GridLength(4);
            InspectorRows.RowDefinitions[2].MinHeight = 140;
            SetRowRatio(InspectorRows, GameStudioInternalSettings.InspectorHeightRatio.GetValue(), 2.0 / 3.0);
        }
    }

    private void ConstrainHierarchyWidth()
    {
        if (EditorRows.Bounds.Width <= 0) return;
        var layout = EditorLayout.GetState(this)!;
        var maximum = Math.Clamp(EditorRows.Bounds.Width - 245, 140, 500);
        if (layout.HierarchyWidth.Value > maximum)
            layout.HierarchyWidth = new GridLength(maximum);
    }

    private void RestoreLayout()
    {
        EditorLayout.GetState(this)!.HierarchyWidth = new GridLength(Math.Clamp(GameStudioInternalSettings.HierarchyWidth.GetValue(), 140, 500));
        SetInspectorRatio(GameStudioInternalSettings.InspectorWidthRatio.GetValue());
        SetRowRatio(EditorRows, GameStudioInternalSettings.EditorHeightRatio.GetValue(), 3.3 / 4.3);
        SetRowRatio(InspectorRows, GameStudioInternalSettings.InspectorHeightRatio.GetValue(), 2.0 / 3.0);
        AssetTabs.SelectedIndex = Math.Clamp(GameStudioInternalSettings.AssetTab.GetValue(), 0, 2);
        PreviewTabs.SelectedIndex = Math.Clamp(GameStudioInternalSettings.PreviewTab.GetValue(), 0, 2);
        UpdatePanelLayout();
    }

    public void SaveLayout()
    {
        if (Bounds.Width <= 0 || Bounds.Height <= 0) return;
        if (DataContext is not MainViewModel mainVm) return;

        if (mainVm.Panels.SessionExplorerPanelVisible && mainVm.Panels.BottomCenterPanelVisible)
            GameStudioInternalSettings.HierarchyWidth.SetValue((int)AssetColumns.ColumnDefinitions[0].ActualWidth);
        if (mainVm.Panels.InspectorColumnVisible)
            GameStudioInternalSettings.InspectorWidthRatio.SetValue(SessionColumns.ColumnDefinitions[2].ActualWidth / (SessionColumns.ColumnDefinitions[0].ActualWidth + SessionColumns.ColumnDefinitions[2].ActualWidth));
        if (mainVm.Panels.BottomRowVisible)
            GameStudioInternalSettings.EditorHeightRatio.SetValue(GetRowRatio(EditorRows));
        if (mainVm.Panels.PropertyGridPanelVisible && mainVm.Panels.BottomRightPanelVisible)
            GameStudioInternalSettings.InspectorHeightRatio.SetValue(GetRowRatio(InspectorRows));
        GameStudioInternalSettings.AssetTab.SetValue(AssetTabs.SelectedIndex);
        GameStudioInternalSettings.PreviewTab.SetValue(PreviewTabs.SelectedIndex);
    }

    private void ResetLayout_OnClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is MainViewModel mainVm)
        {
            mainVm.Panels.SetAllPanelVisible();
        }
        EditorLayout.GetState(this)!.HierarchyWidth = new GridLength(210);
        SetInspectorRatio(400.0 / 1230);
        SetRowRatio(EditorRows, 3.3 / 4.3, 3.3 / 4.3);
        SetRowRatio(InspectorRows, 2.0 / 3.0, 2.0 / 3.0);
        AssetTabs.SelectedIndex = 0;
        PreviewTabs.SelectedIndex = 2;
        UpdatePanelLayout();
    }

    private void ActivateDockTab_OnClick(object? sender, RoutedEventArgs e)
    {
        if (sender is not MenuItem { Tag: TabControl tabs, CommandParameter: int index })
            return;

        if (DataContext is MainViewModel mainVm)
        {
            if (tabs == AssetTabs)
            {
                switch (index)
                {
                    case 0: mainVm.Panels.AssetViewPanelVisible = true; break;
                    case 1: mainVm.Panels.AssetLogPanelVisible = true; break;
                    case 2: mainVm.Panels.BuildLogPanelVisible = true; break;
                }
            }
            else if (tabs == PreviewTabs)
            {
                switch (index)
                {
                    case 0: mainVm.Panels.AssetPreviewPanelVisible = true; break;
                    case 1: mainVm.Panels.ActionHistoryPanelVisible = true; break;
                    case 2: mainVm.Panels.ReferencesPanelVisible = true; break;
                }
            }
        }

        tabs.SelectedIndex = index;
    }

    private void CloseBottomCenter_OnClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not MainViewModel mainVm) return;
        switch (AssetTabs.SelectedIndex)
        {
            case 0: mainVm.Panels.AssetViewPanelVisible = false; break;
            case 1: mainVm.Panels.AssetLogPanelVisible = false; break;
            case 2: mainVm.Panels.BuildLogPanelVisible = false; break;
        }
    }

    private void CloseBottomRight_OnClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not MainViewModel mainVm) return;
        switch (PreviewTabs.SelectedIndex)
        {
            case 0: mainVm.Panels.AssetPreviewPanelVisible = false; break;
            case 1: mainVm.Panels.ActionHistoryPanelVisible = false; break;
            case 2: mainVm.Panels.ReferencesPanelVisible = false; break;
        }
    }

    private void TogglePanel_OnClick(object? sender, RoutedEventArgs e)
    {
        if (sender is MenuItem { CommandParameter: string panel } && DataContext is MainViewModel mainVm)
        {
            switch (panel)
            {
                case "SessionExplorer":
                    mainVm.Panels.SessionExplorerPanelVisible = !mainVm.Panels.SessionExplorerPanelVisible;
                    break;
                case "AssetView":
                    mainVm.Panels.AssetViewPanelVisible = !mainVm.Panels.AssetViewPanelVisible;
                    break;
                case "References":
                    mainVm.Panels.ReferencesPanelVisible = !mainVm.Panels.ReferencesPanelVisible;
                    break;
                case "AssetPreview":
                    mainVm.Panels.AssetPreviewPanelVisible = !mainVm.Panels.AssetPreviewPanelVisible;
                    break;
                case "PropertyGrid":
                    mainVm.Panels.PropertyGridPanelVisible = !mainVm.Panels.PropertyGridPanelVisible;
                    break;
                case "ActionHistory":
                    mainVm.Panels.ActionHistoryPanelVisible = !mainVm.Panels.ActionHistoryPanelVisible;
                    break;
                case "AssetLog":
                    mainVm.Panels.AssetLogPanelVisible = !mainVm.Panels.AssetLogPanelVisible;
                    break;
                case "BuildLog":
                    mainVm.Panels.BuildLogPanelVisible = !mainVm.Panels.BuildLogPanelVisible;
                    break;
            }
        }
    }

    private void SetInspectorRatio(double ratio)
    {
        ratio = double.IsFinite(ratio) ? Math.Clamp(ratio, 0.2, 0.55) : 400.0 / 1230;
        SessionColumns.ColumnDefinitions[0].Width = new GridLength(1 - ratio, GridUnitType.Star);
        SessionColumns.ColumnDefinitions[2].Width = new GridLength(ratio, GridUnitType.Star);
    }

    private static void SetRowRatio(Grid grid, double ratio, double fallback)
    {
        ratio = double.IsFinite(ratio) ? Math.Clamp(ratio, 0.2, 0.85) : fallback;
        grid.RowDefinitions[0].Height = new GridLength(ratio, GridUnitType.Star);
        grid.RowDefinitions[2].Height = new GridLength(1 - ratio, GridUnitType.Star);
    }

    private static double GetRowRatio(Grid grid)
    {
        var top = grid.RowDefinitions[0].ActualHeight;
        var bottom = grid.RowDefinitions[2].ActualHeight;
        return top + bottom > 0 ? top / (top + bottom) : grid.RowDefinitions[0].Height.Value;
    }

    /// <summary>
    /// Gets a platform-specific <see cref="KeyGesture"/> for the Redo action
    /// </summary>
    public static KeyGesture? RedoGesture => Application.Current?.PlatformSettings?.HotkeyConfiguration.Redo.FirstOrDefault();

    /// <summary>
    /// Gets a platform-specific <see cref="KeyGesture"/> for the Undo action
    /// </summary>
    public static KeyGesture? UndoGesture => Application.Current?.PlatformSettings?.HotkeyConfiguration.Undo.FirstOrDefault();
}
