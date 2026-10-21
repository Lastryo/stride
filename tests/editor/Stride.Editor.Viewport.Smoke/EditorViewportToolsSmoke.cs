// Copyright (c) .NET Foundation and Contributors (https://dotnetfoundation.org/ & https://stride3d.net)
// Distributed under the MIT license. See the LICENSE.md file in the project root for more information.

using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Automation.Peers;
using Avalonia.Automation.Provider;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Stride.Assets.Editor.Avalonia.Views;
using Stride.Assets.Editor.EntityHierarchyEditor.Game;
using Stride.Assets.Editor.GameEditor;
using Stride.Assets.Editor.Gizmos;
using Stride.Assets.Editor.ViewModels;
using Stride.Core.Presentation.Avalonia.Controls;
using Stride.Engine;
using Stride.GameStudio.Avalonia.Views;
using Matrix = Stride.Core.Mathematics.Matrix;

internal static class EditorViewportToolsSmoke
{
    public static async Task Check(EntityHierarchyEditorViewModel editor, SceneViewportGame game, Window window)
    {
        var viewport = editor.Viewport!;
        var view = window.GetVisualDescendants().OfType<EntityHierarchyEditorView>().Single();
        var toolbar = view.FindControl<Border>("ViewportToolbar")!;
        var oldWidth = toolbar.Width;
        var original = (viewport.ActiveTransformation, viewport.Space, viewport.Snap, viewport.TranslationSnap, viewport.RotationSnap, viewport.ScaleSnap, viewport.GridVisible);
        var wasDirty = editor.Asset.IsDirty;
        var parts = editor.HierarchyRoot.InnerSubEntities.Select(item => item.Id).ToArray();
        try
        {
            await Layout(window);
            var filter = view.FindControl<TextBox>("HierarchyFilter")!;
            if (filter.Bounds.Width != 120 || filter.Bounds.Height != 20 || toolbar.Bounds.Height != 28)
                throw new Exception($"WPF toolbar geometry differs: filter={filter.Bounds}, toolbar={toolbar.Bounds}.");
            var tools = new[] { "MoveTool", "RotateTool", "ScaleTool" }.Select(name => view.FindControl<RadioButton>(name)!).ToArray();
            if (tools.Any(tool => tool.Bounds.Size != new Size(28, 28) || tool.Margin != default))
                throw new Exception("WPF transform tools must be 28×28 without extra margins.");
            viewport.ActiveTransformation = Transformation.Translation;
            viewport.TranslationSnap = 1;
            await Layout(window);
            var snap = view.FindControl<ToggleButton>("SnapTool")!.GetVisualDescendants().OfType<NumericTextBox>().Single();
            foreach (var (name, expected) in new[] { ("PART_IncreaseButton", 2f), ("PART_DecreaseButton", 1f) })
            {
                var spin = snap.GetVisualDescendants().OfType<RepeatButton>().Single(button => button.Name == name);
                ((IInvokeProvider)new ButtonAutomationPeer(spin)).Invoke();
                await Layout(window);
                if (viewport.TranslationSnap != expected) throw new Exception("Snap spinner did not update the actual step.");
            }
            var compiling = editor.CompilingAssets;
            try
            {
                editor.CompilingAssets = true;
                await Layout(window);
                var documents = window.GetVisualDescendants().OfType<EditorCollectionView>().Single();
                var origin = toolbar.TranslatePoint(default, documents)!.Value;
                var size = new PixelSize((int)Math.Ceiling(documents.Bounds.Width * window.RenderScaling),
                    (int)Math.Ceiling((origin.Y + toolbar.Bounds.Height) * window.RenderScaling));
                using var bitmap = new RenderTargetBitmap(size, new Vector(96 * window.RenderScaling, 96 * window.RenderScaling));
                bitmap.Render(documents);
                bitmap.Save(Path.Combine(Path.GetTempPath(), "stride-scene-toolbar.png"));
            }
            finally { editor.CompilingAssets = compiling; }
            Console.WriteLine("PASS: WPF toolbar geometry, 120-DIP filter, contiguous tools, working snap spinner and toolbar screenshot.");
            foreach (var mode in Enum.GetValues<Transformation>())
            {
                viewport.ActiveTransformation = mode;
                await Layout(window);
                var buttons = new[] { "MoveTool", "RotateTool", "ScaleTool" }.Select(name => view.FindControl<RadioButton>(name)!).ToArray();
                if (buttons.Count(button => button.IsChecked == true) != 1 || buttons[(int)mode].IsChecked != true)
                    throw new Exception("The viewport toolbar does not reflect the current transformation.");
            }
            foreach (var space in viewport.Spaces)
            {
                viewport.Space = space;
                await Layout(window);
                if (!Equals(view.FindControl<ComboBox>("CoordinateSpace")!.SelectedItem, space))
                    throw new Exception("The coordinate-space selector lost its view-model binding.");
            }
            toolbar.Width = 240;
            await Layout(window);
            foreach (var control in toolbar.GetVisualDescendants().OfType<Control>().Where(control => control.Classes.Contains("viewportTool") || control.Name is "CoordinateSpace" or "RenderModeSelector" or "TransformationPivot"))
            {
                var position = control.TranslatePoint(default, toolbar)!.Value;
                if (position.X < 0 || position.Y < 0 || position.X + control.Bounds.Width > toolbar.Bounds.Width + 1 || position.Y + control.Bounds.Height > toolbar.Bounds.Height + 1)
                    throw new Exception($"Viewport toolbar control {control.Name} is clipped at 240 DIP.");
            }
            toolbar.Width = oldWidth;
            viewport.TranslationSnap = .5f;
            viewport.RotationSnap = 22.5f;
            viewport.ScaleSnap = .25f;
            viewport.Snap = true;
            await viewport.Controller.InvokeAsync(() =>
            {
                var gizmos = (TransformationGizmo[])typeof(EditorGameEntityTransformService).GetField("gizmos", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!.GetValue(viewport.Controller.GetTransformService())!;
                if (!gizmos.Select(gizmo => gizmo.SnapValue).SequenceEqual(new[] { .5f, 22.5f, .25f }))
                    throw new Exception("The snapping steps did not reach the transformation gizmos.");
            });
            foreach (var invalid in new[] { 0, -1, float.NaN, float.PositiveInfinity })
            {
                try { viewport.TranslationSnap = invalid; throw new Exception("An invalid snap step was accepted."); }
                catch (ArgumentOutOfRangeException) { }
            }
            var button = view.FindControl<Button>("SnapSettingsButton")!;
            var flyout = (Flyout)button.Flyout!;
            flyout.ShowAt(button);
            try
            {
                await Task.Delay(100);
                var fields = ((Control)flyout.Content!).GetVisualDescendants().OfType<NumericTextBox>().ToArray();
                if (!fields.Select(field => field.Value).SequenceEqual(new double?[] { .5, 22.5, .25 }))
                    throw new Exception("The snap settings popup lost its data context.");
                fields[0].SetCurrentValue(NumericTextBox.ValueProperty, .75);
                if (viewport.TranslationSnap != .75f) throw new Exception("Editing the snap popup did not update its view model.");
            }
            finally { flyout.Hide(); }
            Console.WriteLine("PASS: viewport selected tools, coordinate spaces, 240-DIP wrapping and validated snap settings.");

            if (viewport.LastException != null) throw new Exception("Viewport failed before the grid checks.", viewport.LastException);

            var root = await viewport.Controller.InvokeAsync(() => game.EditorScene.Entities.Single(entity => entity.Name == "Scene grid"));
            foreach (var visible in new[] { false, true })
            {
                viewport.GridVisible = visible;
                for (var i = 0; i < 100 && await viewport.Controller.InvokeAsync(() => game.EditorScene.Entities.Contains(root)) != visible; i++) await Task.Delay(20);
                if (await viewport.Controller.InvokeAsync(() => game.EditorScene.Entities.Contains(root)) != visible)
                    throw new Exception("The grid visibility control did not change the editor scene.");
            }
            var camera = game.EditorServices.Get<EditorGameEntityCameraService>()!;
            var cameraDirection = await viewport.Controller.InvokeAsync(() => Matrix.Invert(camera.ViewMatrix).Forward);
            var orthographic = camera.IsOrthographic;
            try
            {
                foreach (var orientation in new[] { CameraOrientation.Front, CameraOrientation.Top })
                {
                    await viewport.Controller.InvokeAsync(() => { camera.IsOrthographic = true; camera.ResetCamera(orientation); });
                    await Task.Delay(100);
                    await viewport.Controller.InvokeAsync(() =>
                    {
                        var entities = root.Transform.Children.Select(child => child.Entity).ToArray();
                        if (entities.Take(3).All(entity => !entity.Get<ModelComponent>().Enabled)) throw new Exception("No orthographic grid plane is enabled.");
                        if (entities.Any(entity => !float.IsFinite(entity.Transform.Rotation.LengthSquared()) || !float.IsFinite(entity.Transform.Position.LengthSquared())))
                            throw new Exception("The orthographic grid produced a non-finite transform.");
                    });
                }
            }
            finally { await viewport.Controller.InvokeAsync(() => { camera.IsOrthographic = orthographic; camera.ResetCamera(cameraDirection); camera.Frame(game.ContentScene); }); }
            if (editor.Asset.IsDirty != wasDirty || !editor.HierarchyRoot.InnerSubEntities.Select(item => item.Id).SequenceEqual(parts))
                throw new Exception("Editor grid/settings modified the asset hierarchy.");
            Console.WriteLine("PASS: the editor-only grid toggles without modifying the asset and remains finite in front/top orthographic views.");
        }
        finally
        {
            toolbar.Width = oldWidth;
            (viewport.ActiveTransformation, viewport.Space, viewport.Snap, viewport.TranslationSnap, viewport.RotationSnap, viewport.ScaleSnap, viewport.GridVisible) = original;
            await Layout(window);
        }
    }

    private static async Task Layout(Window window)
    {
        await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Background);
        window.UpdateLayout();
    }
}
