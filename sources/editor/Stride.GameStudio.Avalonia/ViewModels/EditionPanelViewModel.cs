// Copyright (c) .NET Foundation and Contributors (https://dotnetfoundation.org/ & https://stride3d.net)
// Distributed under the MIT license. See the LICENSE.md file in the project root for more information.

using System.Runtime.CompilerServices;
using Stride.Core.Presentation.Commands;
using Stride.Core.Presentation.ViewModels;
using Stride.Core.Settings;
using Stride.GameStudio.Avalonia.Settings;

namespace Stride.GameStudio.Avalonia.ViewModels;

public sealed class EditionPanelViewModel : DispatcherViewModel
{
    private bool sessionExplorerPanelVisible = true;
    private bool assetViewPanelVisible = true;
    private bool referencesPanelVisible = true;
    private bool assetPreviewPanelVisible = true;
    private bool propertyGridPanelVisible = true;
    private bool actionHistoryPanelVisible = true;
    private bool assetLogPanelVisible = true;
    private bool buildLogPanelVisible = true;

    public EditionPanelViewModel(IViewModelServiceProvider serviceProvider)
        : base(serviceProvider)
    {
        CloseSolutionExplorerCommand = new AnonymousCommand(serviceProvider, () => SessionExplorerPanelVisible = false);
        ClosePropertyGridCommand = new AnonymousCommand(serviceProvider, () => PropertyGridPanelVisible = false);
        CloseBottomCenterCommand = new AnonymousCommand(serviceProvider, CloseBottomCenterPanel);
        CloseBottomRightCommand = new AnonymousCommand(serviceProvider, CloseBottomRightPanel);

        LoadFromSettings();
    }

    public bool SessionExplorerPanelVisible
    {
        get => sessionExplorerPanelVisible;
        set
        {
            if (SetValue(ref sessionExplorerPanelVisible, value, GameStudioInternalSettings.SessionExplorerPanelVisible))
            {
                OnPropertyChanged(nameof(BottomRowVisible));
                OnPropertyChanged(nameof(SolutionExplorerSplitterVisible));
            }
        }
    }

    public bool AssetViewPanelVisible
    {
        get => assetViewPanelVisible;
        set
        {
            if (SetValue(ref assetViewPanelVisible, value, GameStudioInternalSettings.AssetViewPanelVisible))
            {
                OnPropertyChanged(nameof(BottomCenterPanelVisible));
                OnPropertyChanged(nameof(BottomRowVisible));
                OnPropertyChanged(nameof(SolutionExplorerSplitterVisible));
            }
        }
    }

    public bool ReferencesPanelVisible
    {
        get => referencesPanelVisible;
        set
        {
            if (SetValue(ref referencesPanelVisible, value, GameStudioInternalSettings.ReferencesPanelVisible))
            {
                OnPropertyChanged(nameof(BottomRightPanelVisible));
                OnPropertyChanged(nameof(InspectorColumnVisible));
                OnPropertyChanged(nameof(PropertyGridSplitterVisible));
            }
        }
    }

    public bool AssetPreviewPanelVisible
    {
        get => assetPreviewPanelVisible;
        set
        {
            if (SetValue(ref assetPreviewPanelVisible, value, GameStudioInternalSettings.AssetPreviewPanelVisible))
            {
                OnPropertyChanged(nameof(BottomRightPanelVisible));
                OnPropertyChanged(nameof(InspectorColumnVisible));
                OnPropertyChanged(nameof(PropertyGridSplitterVisible));
            }
        }
    }

    public bool PropertyGridPanelVisible
    {
        get => propertyGridPanelVisible;
        set
        {
            if (SetValue(ref propertyGridPanelVisible, value, GameStudioInternalSettings.PropertyGridPanelVisible))
            {
                OnPropertyChanged(nameof(InspectorColumnVisible));
                OnPropertyChanged(nameof(PropertyGridSplitterVisible));
            }
        }
    }

    public bool ActionHistoryPanelVisible
    {
        get => actionHistoryPanelVisible;
        set
        {
            if (SetValue(ref actionHistoryPanelVisible, value, GameStudioInternalSettings.ActionHistoryPanelVisible))
            {
                OnPropertyChanged(nameof(BottomRightPanelVisible));
                OnPropertyChanged(nameof(InspectorColumnVisible));
                OnPropertyChanged(nameof(PropertyGridSplitterVisible));
            }
        }
    }

    public bool AssetLogPanelVisible
    {
        get => assetLogPanelVisible;
        set
        {
            if (SetValue(ref assetLogPanelVisible, value, GameStudioInternalSettings.AssetLogPanelVisible))
            {
                OnPropertyChanged(nameof(BottomCenterPanelVisible));
                OnPropertyChanged(nameof(BottomRowVisible));
                OnPropertyChanged(nameof(SolutionExplorerSplitterVisible));
            }
        }
    }

    public bool BuildLogPanelVisible
    {
        get => buildLogPanelVisible;
        set
        {
            if (SetValue(ref buildLogPanelVisible, value, GameStudioInternalSettings.BuildLogPanelVisible))
            {
                OnPropertyChanged(nameof(BottomCenterPanelVisible));
                OnPropertyChanged(nameof(BottomRowVisible));
                OnPropertyChanged(nameof(SolutionExplorerSplitterVisible));
            }
        }
    }

    public bool BottomCenterPanelVisible => AssetViewPanelVisible || AssetLogPanelVisible || BuildLogPanelVisible;

    public bool BottomRightPanelVisible => ReferencesPanelVisible || AssetPreviewPanelVisible || ActionHistoryPanelVisible;

    public bool BottomRowVisible => SessionExplorerPanelVisible || BottomCenterPanelVisible;

    public bool InspectorColumnVisible => PropertyGridPanelVisible || BottomRightPanelVisible;

    public bool SolutionExplorerSplitterVisible => SessionExplorerPanelVisible && BottomCenterPanelVisible;

    public bool PropertyGridSplitterVisible => PropertyGridPanelVisible && BottomRightPanelVisible;

    public ICommandBase CloseSolutionExplorerCommand { get; }
    public ICommandBase ClosePropertyGridCommand { get; }
    public ICommandBase CloseBottomCenterCommand { get; }
    public ICommandBase CloseBottomRightCommand { get; }

    public void CloseBottomCenterPanel()
    {
        AssetViewPanelVisible = false;
        AssetLogPanelVisible = false;
        BuildLogPanelVisible = false;
    }

    public void CloseBottomRightPanel()
    {
        ReferencesPanelVisible = false;
        AssetPreviewPanelVisible = false;
        ActionHistoryPanelVisible = false;
    }

    public void LoadFromSettings()
    {
        sessionExplorerPanelVisible = GameStudioInternalSettings.SessionExplorerPanelVisible.GetValue();
        assetViewPanelVisible = GameStudioInternalSettings.AssetViewPanelVisible.GetValue();
        referencesPanelVisible = GameStudioInternalSettings.ReferencesPanelVisible.GetValue();
        assetPreviewPanelVisible = GameStudioInternalSettings.AssetPreviewPanelVisible.GetValue();
        propertyGridPanelVisible = GameStudioInternalSettings.PropertyGridPanelVisible.GetValue();
        actionHistoryPanelVisible = GameStudioInternalSettings.ActionHistoryPanelVisible.GetValue();
        assetLogPanelVisible = GameStudioInternalSettings.AssetLogPanelVisible.GetValue();
        buildLogPanelVisible = GameStudioInternalSettings.BuildLogPanelVisible.GetValue();

        OnPropertyChanged(nameof(SessionExplorerPanelVisible));
        OnPropertyChanged(nameof(PropertyGridPanelVisible));
        OnPropertyChanged(nameof(BuildLogPanelVisible));
        OnPropertyChanged(nameof(AssetLogPanelVisible));
        OnPropertyChanged(nameof(AssetViewPanelVisible));
        OnPropertyChanged(nameof(ActionHistoryPanelVisible));
        OnPropertyChanged(nameof(AssetPreviewPanelVisible));
        OnPropertyChanged(nameof(ReferencesPanelVisible));
        OnPropertyChanged(nameof(BottomCenterPanelVisible));
        OnPropertyChanged(nameof(BottomRightPanelVisible));
        OnPropertyChanged(nameof(BottomRowVisible));
        OnPropertyChanged(nameof(InspectorColumnVisible));
        OnPropertyChanged(nameof(SolutionExplorerSplitterVisible));
        OnPropertyChanged(nameof(PropertyGridSplitterVisible));
    }

    public void SetAllPanelVisible()
    {
        SessionExplorerPanelVisible = true;
        PropertyGridPanelVisible = true;
        BuildLogPanelVisible = true;
        AssetLogPanelVisible = true;
        AssetViewPanelVisible = true;
        ActionHistoryPanelVisible = true;
        AssetPreviewPanelVisible = true;
        ReferencesPanelVisible = true;
        OnPropertyChanged(nameof(BottomRowVisible));
    }

    private bool SetValue(ref bool field, bool value, SettingsKey<bool> settingsKey, [CallerMemberName] string? propertyName = null)
    {
        if (SetValue(ref field, value, propertyName))
        {
            settingsKey.SetValue(value);
            return true;
        }
        return false;
    }
}
