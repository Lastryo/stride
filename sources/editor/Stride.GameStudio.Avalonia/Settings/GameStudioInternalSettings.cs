// Copyright (c) .NET Foundation and Contributors (https://dotnetfoundation.org/ & https://stride3d.net)
// Distributed under the MIT license. See the LICENSE.md file in the project root for more information.

using Avalonia;
using Stride.Core.Assets.Editor.Settings;
using Stride.Core.Presentation.Avalonia.Extensions;
using Stride.Core.Presentation.Avalonia.Services;
using Stride.Core.Settings;

namespace Stride.GameStudio.Avalonia.Settings;

internal static class GameStudioInternalSettings
{
    public static SettingsContainer SettingsContainer => InternalSettings.SettingsContainer;

    public static readonly SettingsKey<bool> WindowMaximized = new("Internal/WindowMaximized", SettingsContainer, false);
    public static readonly SettingsKey<int> WindowWidth = new("Internal/WindowWidth", SettingsContainer, (int)WorkArea.Value.Width);
    public static readonly SettingsKey<int> WindowHeight = new("Internal/WindowHeight", SettingsContainer, (int)WorkArea.Value.Height);
    public static readonly SettingsKey<int> WorkAreaWidth = new("Internal/WorkAreaWidth", SettingsContainer, (int)WorkArea.Value.Width);
    public static readonly SettingsKey<int> WorkAreaHeight = new("Internal/WorkAreaHeight", SettingsContainer, (int)WorkArea.Value.Height);

    public static readonly SettingsKey<int> HierarchyWidth = new("Internal/Layout/HierarchyWidth", SettingsContainer, 210);
    public static readonly SettingsKey<double> InspectorWidthRatio = new("Internal/Layout/InspectorWidthRatio", SettingsContainer, 400.0 / 1230);
    public static readonly SettingsKey<double> EditorHeightRatio = new("Internal/Layout/EditorHeightRatio", SettingsContainer, 3.3 / 4.3);
    public static readonly SettingsKey<double> InspectorHeightRatio = new("Internal/Layout/InspectorHeightRatio", SettingsContainer, 2.0 / 3.0);
    public static readonly SettingsKey<int> AssetTab = new("Internal/Layout/AssetTab", SettingsContainer, 0);
    public static readonly SettingsKey<int> PreviewTab = new("Internal/Layout/PreviewTab", SettingsContainer, 2);

    public static readonly SettingsKey<bool> SessionExplorerPanelVisible = new("Internal/Layout/SessionExplorerPanelVisible", SettingsContainer, true);
    public static readonly SettingsKey<bool> AssetViewPanelVisible = new("Internal/Layout/AssetViewPanelVisible", SettingsContainer, true);
    public static readonly SettingsKey<bool> ReferencesPanelVisible = new("Internal/Layout/ReferencesPanelVisible", SettingsContainer, true);
    public static readonly SettingsKey<bool> AssetPreviewPanelVisible = new("Internal/Layout/AssetPreviewPanelVisible", SettingsContainer, true);
    public static readonly SettingsKey<bool> PropertyGridPanelVisible = new("Internal/Layout/PropertyGridPanelVisible", SettingsContainer, true);
    public static readonly SettingsKey<bool> ActionHistoryPanelVisible = new("Internal/Layout/ActionHistoryPanelVisible", SettingsContainer, true);
    public static readonly SettingsKey<bool> AssetLogPanelVisible = new("Internal/Layout/AssetLogPanelVisible", SettingsContainer, true);
    public static readonly SettingsKey<bool> BuildLogPanelVisible = new("Internal/Layout/BuildLogPanelVisible", SettingsContainer, true);

    private static Lazy<Rect> WorkArea => new(() =>
    {
        if (DialogService.MainWindow is { } mainWindow)
        {
            return mainWindow.GetWorkingArea().area;
        }
        
        // fallback to a reasonable value
        return new Rect(0, 0, 800, 600);
    });
}
