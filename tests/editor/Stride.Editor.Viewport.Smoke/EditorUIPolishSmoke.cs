// Copyright (c) .NET Foundation and Contributors (https://dotnetfoundation.org/ & https://stride3d.net)
// Distributed under the MIT license. See the LICENSE.md file in the project root for more information.

using Avalonia.Controls;
using Avalonia.VisualTree;
using Stride.Assets.Editor.Avalonia.Views;
using Stride.Assets.Editor.UIEditor;
using Stride.Assets.Editor.ViewModels;
using Stride.Assets.Presentation.ViewModels;
using Stride.Assets.UI;
using Stride.Core;
using Stride.Core.Assets;
using Stride.Core.Assets.Editor.Avalonia.Converters;
using Stride.Core.Assets.Editor.Services;
using Stride.Core.Assets.Editor.ViewModels;
using Stride.Core.Mathematics;
using Stride.Core.Presentation.Avalonia.Controls;
using Canvas = Stride.UI.Panels.Canvas;
using TextBlock = Stride.UI.Controls.TextBlock;

internal static class EditorUIPolishSmoke
{
    public static async Task Check(SessionViewModel session, Window window)
    {
        var clipboard = session.ServiceProvider.Get<IClipboardService>();
        var previous = await clipboard.GetTextAsync();
        try { await CheckEditor(session, window); }
        finally { await clipboard.SetTextAsync(previous); }
    }

    private static async Task CheckEditor(SessionViewModel session, Window window)
    {
        var text = new TextBlock { Name = "Label", Text = "Hello" };
        var panel = new Canvas { Name = "Panel", Children = { text } };
        var root = new Canvas { Name = "Root", Children = { panel } };
        var page = new UIPageAsset();
        page.Hierarchy.RootParts.Add(root);
        foreach (var element in new Stride.UI.UIElement[] { root, panel, text })
            page.Hierarchy.Parts.Add(new UIElementDesign(element));
        UIPageViewModel asset;
        using (session.ActionService!.CreateTransaction())
            asset = (UIPageViewModel)session.CurrentProject!.AddAsset(new AssetItem("UIPolish_" + Guid.NewGuid().ToString("N"), page), session.CurrentProject.AssetMountPoint);
        var (editor, view) = await Open();
        async Task<(UIPageEditorViewModel, UIEditorView)> Open()
        {
            session.EditorCollection.OpenAssetEditor(asset);
            var owner = (UIPageEditorViewModel)session.EditorCollection.ActiveEditor!;
            await Wait(() => window.GetVisualDescendants().OfType<UIEditorView>().Any(v => v.DataContext == owner), "UI view");
            var control = window.GetVisualDescendants().OfType<UIEditorView>().Single(v => v.DataContext == owner);
            await control.EditorInitialization.WaitAsync(TimeSpan.FromSeconds(90));
            if (owner.Viewport!.LastException != null) throw owner.Viewport.LastException;
            return (owner, control);
        }
        var viewport = editor.Viewport!;
        var tree = view.FindControl<TreeView>("HierarchyTreeView")!;
        var palette = view.FindControl<TreeView>("FactoryListBox")!;
        if (palette.Items.Count == 0 || editor.FactoryGroups.Count == 0)
            throw new Exception("Palette groups missing.");
        foreach (var library in editor.FactoryGroups)
        {
            var categories = library.Children.Cast<Stride.Assets.Editor.UIEditor.ViewModels.UIElementFactoryGroup>().ToArray();
            if (!categories.Select(c => c.Name).SequenceEqual(categories.Select(c => c.Name).Order(StringComparer.OrdinalIgnoreCase)))
                throw new Exception("Palette categories not sorted.");
            foreach (var category in categories)
            {
                var factories = category.Children.Cast<Stride.Assets.Editor.UIEditor.ViewModels.IUIElementFactory>().ToArray();
                if (!factories.Select(f => f.Name).SequenceEqual(factories.Select(f => f.Name).Order(StringComparer.OrdinalIgnoreCase))
                    || factories.Any(f => TypeToResource.FetchResourceFromType(f.Type, true) == null))
                    throw new Exception("Palette names/icons missing.");
            }
        }
        await Wait(() => palette.GetVisualDescendants().OfType<Image>().Any(i => i.Source != null), "Palette icons");
        var rootItem = tree.GetVisualDescendants().OfType<TreeViewItem>().Single(i => i.DataContext is UIElementViewModel vm && vm.AssetSideUIElement == root);
        rootItem.IsExpanded = false;
        editor.ClearSelection();
        editor.Select(text.Id, false);
        await Wait(() => rootItem.IsExpanded && tree.GetVisualDescendants().OfType<TreeViewItem>().Any(i => i.IsEffectivelyVisible && i.DataContext is UIElementViewModel vm && vm.AssetSideUIElement == text), "Expand ancestors for viewport selection");
        var selected = editor.LastSelectedElement!;
        if (!selected.Status.Contains("Local element") || TypeToResource.FetchResourceFromType(selected.ElementType, true) == null)
            throw new Exception("Tree status/icon missing.");

        // Use the real color controls, including their binding back to the viewport.
        var settingsButton = view.GetVisualDescendants().OfType<Button>().Single(b => b.Flyout is Flyout);
        settingsButton.Flyout!.ShowAt(settingsButton);
        await Task.Delay(200);
        var popup = (Control)((Flyout)settingsButton.Flyout).Content!;
        foreach (var name in new[] { "Guideline", "Highlight", "Selection", "Sizing" })
        {
            var colorEditor = popup.GetVisualDescendants().OfType<ColorEditor>().Single(c => c.Name == name + "ColorEditor");
            colorEditor.Value = Color.Blue;
            if ((Color)viewport.GetType().GetProperty(name + "Color")!.GetValue(viewport)! != Color.Blue)
                throw new Exception(name + " color binding failed.");
        }
        settingsButton.Flyout.Hide();
        viewport.LastException = new InvalidOperationException("UI smoke recovery");
        var copy = view.FindControl<Button>("CopyErrorButton")!;
        copy.Command!.Execute(copy.CommandParameter);
        var clipboard = session.ServiceProvider.Get<IClipboardService>();
        for (var i = 0; i < 100 && !(await clipboard.GetTextAsync() ?? "").Contains("UI smoke recovery"); i++) await Task.Delay(50);
        if (!(await clipboard.GetTextAsync() ?? "").Contains("UI smoke recovery")) throw new Exception("Copy error failed.");
        var resume = view.FindControl<Button>("ResumeErrorButton")!;
        resume.Command!.Execute(resume.CommandParameter);
        await Wait(() => viewport.LastException == null, "Resume error");

        var state = new UIEditorCameraSettings { Position = new Vector3(42, -17, 500), OrthographicSize = 321 };
        await viewport.Controller.InvokeAsync(() => viewport.Controller.CameraService.ApplySettings(state));
        await viewport.Controller.InvokeAsync(() => viewport.Controller.CameraService.ApplySettings(new UIEditorCameraSettings { OrthographicSize = float.NaN }));
        if (viewport.Controller.CameraService.Settings.OrthographicSize != state.OrthographicSize) throw new Exception("Invalid camera settings accepted.");
        await session.EditorCollection.CloseEditorAsync(editor).WaitAsync(TimeSpan.FromSeconds(15));
        var profile = asset.Directory.Package.Package.UserSettings;
        var saved = profile.GetValue(UIEditorCameraSettings.Cameras);
        if (!saved.TryGetValue(asset.Id, out var camera) || camera.Position != state.Position || camera.OrthographicSize != state.OrthographicSize)
            throw new Exception("Camera settings were not saved by asset ID.");
        var disk = PackageUserSettings.SettingsContainer.LoadSettingsProfile(asset.Directory.Package.Package.FullPath + ".user", false)!;
        var persisted = UIEditorCameraSettings.Cameras.GetValue(disk, true)[asset.Id];
        if (persisted.Position != state.Position || persisted.OrthographicSize != state.OrthographicSize) throw new Exception("Camera settings did not serialize.");
        (editor, view) = await Open();
        var restored = editor.Viewport!.Controller.CameraService.Settings;
        if (restored.Position != state.Position || restored.OrthographicSize != state.OrthographicSize) throw new Exception("Camera was not restored on reopen.");
        editor.Viewport.ResetZoom();
        await Wait(() => editor.Viewport.Controller.CameraService.Settings.Position == new Vector3(0, 0, 500), "Camera reset");
        await session.EditorCollection.CloseEditorAsync(editor).WaitAsync(TimeSpan.FromSeconds(15));
        Console.WriteLine("PASS: UI palette grouping/icons, tree reveal/status, adorner color bindings, error copy/resume and camera persistence/reopen.");
    }

    private static async Task Wait(Func<bool> predicate, string operation)
    {
        for (var i = 0; i < 200; i++) { if (predicate()) return; await Task.Delay(50); }
        throw new TimeoutException(operation);
    }
}
