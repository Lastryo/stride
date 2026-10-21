// Copyright (c) .NET Foundation and Contributors (https://dotnetfoundation.org/ & https://stride3d.net)
// Distributed under the MIT license. See the LICENSE.md file in the project root for more information.

using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Stride.Assets.Editor.Avalonia.Views;
using Stride.Core.Presentation.Avalonia.Controls;
using Stride.GameStudio.Avalonia.ViewModels;
using Stride.GameStudio.Avalonia.Views;

internal static class EditorLayoutSmoke
{
    public static async Task CheckStartup(Window window)
    {
        await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Background);
        var shell = (MainView)window.Content!;
        foreach (var name in new[] { "AssetColumns", "PreviewTabs" })
        {
            var panel = shell.FindControl<Control>(name)!;
            var position = panel.TranslatePoint(default, shell)!.Value;
            Console.WriteLine($"STARTUP {name}: window={window.ClientSize}, shell={shell.Bounds}, panel={panel.Bounds}, origin={position}");
            if (!panel.IsEffectivelyVisible || panel.Bounds.Height < 100 || position.Y < 0 || position.Y + panel.Bounds.Height > shell.Bounds.Height + 1)
                throw new Exception($"Startup panel {name} is outside the client area. " + string.Join("; ", panel.GetVisualAncestors().OfType<Control>().Select(control => $"{control.GetType().Name}#{control.Name}: {control.Bounds}, desired={control.DesiredSize}")));
        }
        Console.WriteLine("PASS: both lower panels fit the startup layout without resetting it.");
    }

    public static async Task CheckStartupPresentation(Window window)
    {
        // An offscreen bitmap cannot detect a stale Vulkan presentation image.
        var locate = new System.Diagnostics.ProcessStartInfo("hyprctl") { RedirectStandardOutput = true };
        locate.ArgumentList.Add("clients");
        locate.ArgumentList.Add("-j");
        using var locator = System.Diagnostics.Process.Start(locate)!;
        var json = await locator.StandardOutput.ReadToEndAsync();
        await locator.WaitForExitAsync();
        if (locator.ExitCode != 0) throw new Exception("Window lookup failed.");
        using var clients = System.Text.Json.JsonDocument.Parse(json);
        var client = clients.RootElement.EnumerateArray().Single(item => item.GetProperty("pid").GetInt32() == Environment.ProcessId);
        var at = client.GetProperty("at");
        var size = client.GetProperty("size");
        var capture = new System.Diagnostics.ProcessStartInfo("grim") { RedirectStandardOutput = true };
        capture.ArgumentList.Add("-g");
        capture.ArgumentList.Add($"{at[0].GetInt32()},{at[1].GetInt32()} {size[0].GetInt32()}x{size[1].GetInt32()}");
        capture.ArgumentList.Add("-");
        using var process = System.Diagnostics.Process.Start(capture)!;
        using var stream = new MemoryStream();
        await process.StandardOutput.BaseStream.CopyToAsync(stream);
        await process.WaitForExitAsync();
        if (process.ExitCode != 0) throw new Exception("Screen capture failed.");
        stream.Position = 0;
        using var bitmap = new Avalonia.Media.Imaging.Bitmap(stream);
        bitmap.Save(Path.Combine(Path.GetTempPath(), "stride-startup-presentation.png"));
        var pixels = new byte[4];
        var point = new PixelRect(bitmap.PixelSize.Width / 4, bitmap.PixelSize.Height - (int)(10 * bitmap.PixelSize.Height / window.ClientSize.Height), 1, 1);
        unsafe
        {
            fixed (byte* pointer = pixels)
                bitmap.CopyPixels(point, (IntPtr)pointer, pixels.Length, 4);
        }
        // Both RGBA and BGRA have green in byte 1; the status bar is #007ACC.
        if (Math.Abs(pixels[1] - 122) > 8 || !pixels.Take(3).Any(value => value < 8) || !pixels.Take(3).Any(value => Math.Abs(value - 204) < 8))
            throw new Exception($"The startup status bar is missing from the native frame ({string.Join(",", pixels)}).");
        Console.WriteLine("PASS: the native startup frame includes the bottom status bar at its layout position.");
    }

    public static async Task Check(Window window)
    {
        var shell = (MainView)window.Content!;
        var hierarchy = window.GetVisualDescendants().OfType<EntityHierarchyEditorView>().Single();
        var sceneColumns = hierarchy.FindControl<Grid>("SceneColumns")!;
        var assetColumns = shell.FindControl<Grid>("AssetColumns")!;
        var sessionColumns = shell.FindControl<Grid>("SessionColumns")!;
        var editorRows = shell.FindControl<Grid>("EditorRows")!;
        var inspectorRows = shell.FindControl<Grid>("InspectorRows")!;
        var assetTabs = shell.FindControl<TabControl>("AssetTabs")!;
        var previewTabs = shell.FindControl<TabControl>("PreviewTabs")!;
        var originalWidth = shell.Width;
        var originalHeight = shell.Height;
        var originalHierarchy = EditorLayout.GetState(shell)!.HierarchyWidth;
        var originalColumns = sessionColumns.ColumnDefinitions.Select(column => column.Width).ToArray();
        var originalRows = new[] { editorRows, inspectorRows }.Select(grid => grid.RowDefinitions.Select(row => row.Height).ToArray()).ToArray();
        var originalAssetTab = assetTabs.SelectedIndex;
        var originalPreviewTab = previewTabs.SelectedIndex;
        try
        {
            // Exercise the same reset action exposed in View, without modifying the user's saved layout.
            var reset = shell.FindControl<MenuItem>("ResetLayoutMenuItem")!;
            reset.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
            foreach (var size in new[] { new Size(922, 1030), new Size(1280, 720), new Size(1920, 1080) })
            {
                shell.Width = size.Width;
                shell.Height = size.Height;
                await Layout(window);
                Near(sceneColumns.ColumnDefinitions[0].ActualWidth, assetColumns.ColumnDefinitions[0].ActualWidth, "Hierarchy and Solution explorer widths");
                Near(sceneColumns.TranslatePoint(default, shell)!.Value.X, assetColumns.TranslatePoint(default, shell)!.Value.X, "Document and asset pane left edges");
                var inspector = shell.GetVisualDescendants().OfType<PropertyGridView>().Single();
                var rightTabs = previewTabs.TranslatePoint(default, shell)!.Value;
                var inspectorPosition = inspector.TranslatePoint(default, shell)!.Value;
                Near(rightTabs.X, inspectorPosition.X, "Right pane alignment");
                Near(previewTabs.Bounds.Width, inspector.Bounds.Width, "Right pane widths");
                var assetPosition = assetTabs.TranslatePoint(default, shell)!.Value;
                if (rightTabs.Y >= assetPosition.Y) throw new Exception("References must start above Asset view.");
                if (sessionColumns.ColumnDefinitions.Sum(column => column.ActualWidth) > size.Width + 1)
                {
                    throw new Exception($"Editor columns overflow the {size.Width}-DIP client area: shell={shell.Bounds} width={shell.Width} min={shell.MinWidth}, bounds={sessionColumns.Bounds}, columns={string.Join(",", sessionColumns.ColumnDefinitions.Select(column => column.ActualWidth))}, scene desired={sceneColumns.DesiredSize}.");
                }
                if (previewTabs.SelectedIndex != 2) throw new Exception("Reset layout did not activate References.");
                Console.WriteLine($"PASS: aligned editor columns and independent pane dividers at {size.Width}x{size.Height}.");
            }

            sceneColumns.ColumnDefinitions[0].SetCurrentValue(ColumnDefinition.WidthProperty, new GridLength(245));
            await Layout(window);
            Near(assetColumns.ColumnDefinitions[0].ActualWidth, 245, "Hierarchy divider updates Solution explorer");
            assetColumns.ColumnDefinitions[0].SetCurrentValue(ColumnDefinition.WidthProperty, new GridLength(225));
            await Layout(window);
            Near(sceneColumns.ColumnDefinitions[0].ActualWidth, 225, "Solution explorer divider updates hierarchy");

            EditorLayout.GetState(shell)!.HierarchyWidth = new GridLength(500);
            shell.Width = 922;
            await Layout(window);
            if (assetColumns.ColumnDefinitions[0].ActualWidth + 245 > editorRows.Bounds.Width + 1)
                throw new Exception("A previously widened hierarchy leaves no room for the viewport after resizing.");
            EditorLayout.GetState(shell)!.HierarchyWidth = new GridLength(225);
            shell.Width = 1920;
            await Layout(window);

            var inspectorHeight = inspectorRows.RowDefinitions[0].ActualHeight;
            editorRows.RowDefinitions[0].Height = new GridLength(3, GridUnitType.Star);
            editorRows.RowDefinitions[2].Height = new GridLength(2, GridUnitType.Star);
            assetTabs.SelectedIndex = 1;
            previewTabs.SelectedIndex = 1;
            await Layout(window);
            Near(inspectorRows.RowDefinitions[0].ActualHeight, inspectorHeight, "Independent inspector divider");
            shell.SaveLayout();
            var restored = new MainView();
            Near(EditorLayout.GetState(restored)!.HierarchyWidth.Value, 225, "Restored hierarchy width");
            var restoredRows = restored.FindControl<Grid>("EditorRows")!.RowDefinitions;
            Near(restoredRows[0].Height.Value / (restoredRows[0].Height.Value + restoredRows[2].Height.Value), 0.6, "Restored asset divider", 0.01);
            if (restored.FindControl<TabControl>("AssetTabs")!.SelectedIndex != 1 || restored.FindControl<TabControl>("PreviewTabs")!.SelectedIndex != 1)
                throw new Exception("Selected dock tabs were not restored.");
            Console.WriteLine("PASS: hierarchy widths synchronize in both directions; independent dividers and selected tabs survive layout restoration.");

            // Verify panel visibility toggling, tab switching, and bottom row collapsing
            if (shell.DataContext?.GetType().GetProperty("Panels")?.GetValue(shell.DataContext) is EditionPanelViewModel panels)
            {
                panels.AssetViewPanelVisible = false;
                await Layout(window);
                if (assetTabs.SelectedIndex != 1 && assetTabs.SelectedIndex != 2)
                    throw new Exception("Hiding AssetView did not switch active tab to a visible tab.");
                var assetViewTab = (TabItem)assetTabs.Items[0]!;
                if (assetViewTab.IsVisible)
                    throw new Exception("AssetView TabItem is still visible when AssetViewPanelVisible is false.");

                // Hide remaining tabs in bottom center
                panels.AssetLogPanelVisible = false;
                panels.BuildLogPanelVisible = false;
                await Layout(window);
                var bottomCenterPanel = assetTabs.Parent as Control;
                if (bottomCenterPanel != null && bottomCenterPanel.IsVisible)
                    throw new Exception("BottomCenter panel should be hidden when all its tabs are hidden.");

                // Also hide solution explorer -> bottom row should collapse completely
                panels.SessionExplorerPanelVisible = false;
                await Layout(window);
                if (editorRows.RowDefinitions[2].Height.Value > 0)
                    throw new Exception("EditorRows row 2 should collapse to 0 height when bottom row is hidden.");

                // Restore all panels
                panels.SetAllPanelVisible();
                await Layout(window);
                if (!assetViewTab.IsVisible || assetTabs.SelectedIndex != 0)
                    throw new Exception("SetAllPanelVisible did not restore AssetView tab and selection.");
                Console.WriteLine("PASS: panel visibility toggling, tab switching, and bottom row collapsing verified.");
            }
        }
        finally
        {
            EditorLayout.GetState(shell)!.HierarchyWidth = originalHierarchy;
            for (var i = 0; i < originalColumns.Length; i++) sessionColumns.ColumnDefinitions[i].Width = originalColumns[i];
            var grids = new[] { editorRows, inspectorRows };
            for (var i = 0; i < grids.Length; i++)
                for (var j = 0; j < originalRows[i].Length; j++) grids[i].RowDefinitions[j].Height = originalRows[i][j];
            assetTabs.SelectedIndex = originalAssetTab;
            previewTabs.SelectedIndex = originalPreviewTab;
            shell.Width = originalWidth;
            shell.Height = originalHeight;
            await Layout(window);
            shell.SaveLayout();
        }
    }

    private static async Task Layout(Window window)
    {
        await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Background);
        window.UpdateLayout();
        var root = (Control)window.Content!;
        if (double.IsFinite(root.Width) && double.IsFinite(root.Height))
        {
            // Test client sizes independently of the native window's 960-DIP minimum width.
            root.InvalidateMeasure();
            root.Measure(new Size(root.Width, root.Height));
            root.InvalidateArrange();
            root.Arrange(new Rect(0, 0, root.Width, root.Height));
        }
    }

    private static void Near(double actual, double expected, string label, double tolerance = 2)
    {
        if (Math.Abs(actual - expected) > tolerance)
            throw new Exception($"{label}: expected {expected}, got {actual}.");
    }
}
