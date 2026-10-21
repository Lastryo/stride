// Copyright (c) .NET Foundation and Contributors (https://dotnetfoundation.org/ & https://stride3d.net)
// Distributed under the MIT license. See the LICENSE.md file in the project root for more information.

using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.VisualTree;
using Stride.Assets.Editor.Avalonia.Views;
using Stride.Assets.Editor.EntityHierarchyEditor.Game;
using Stride.Assets.Editor.GameEditor;
using Stride.Assets.Editor.GameEditor.Game;
using Stride.Assets.Editor.SceneEditor;
using Stride.Assets.Editor.ViewModels;
using Stride.Core.Presentation.Avalonia.Controls;
using Stride.Rendering;
using Stride.Rendering.Compositing;
using Stride.Rendering.Materials;

internal static class EditorCameraModesSmoke
{
    public static async Task Check(EntityHierarchyEditorViewModel editor, SceneViewportGame game, Window window)
    {
        var viewport = editor.Viewport!;
        var original = await viewport.Controller.InvokeAsync(() => viewport.Controller.GetCameraService().GetSettings());
        var originalMode = viewport.RenderMode;
        var wasDirty = editor.Asset.IsDirty;
        var view = window.GetVisualDescendants().OfType<EntityHierarchyEditorView>().Single();
        var button = view.FindControl<Button>("CameraSettingsButton")!;
        var flyout = (Flyout)button.Flyout!;
        var host = (Control)viewport.EditorHost!;
        var pointer = new Avalonia.Input.Pointer(105, PointerType.Mouse, true);
        var point = new Avalonia.Point(host.Bounds.Width / 2, host.Bounds.Height / 2);
        try
        {
            flyout.ShowAt(button);
            await Task.Delay(100);
            var content = (Control)flyout.Content!;
            var fields = content.GetVisualDescendants().OfType<NumericTextBox>().ToDictionary(field => field.Name!);
            fields["CameraFovField"].SetCurrentValue(NumericTextBox.ValueProperty, 60d);
            fields["CameraSpeedField"].SetCurrentValue(NumericTextBox.ValueProperty, 7d);
            viewport.CameraNearPlane = .25f;
            viewport.CameraFarPlane = 2000;
            viewport.CameraOrthographic = true;
            viewport.CameraOrthographicSize = 20;
            await viewport.Controller.InvokeAsync(() =>
            {
                var settings = viewport.Controller.GetCameraService().GetSettings();
                if (settings != new ViewportCameraSettings(true, .25f, 2000, 60, 20, 7)) throw new Exception($"Camera popup/settings did not reach the engine: {settings}.");
            });
            for (var retry = 0; retry < 100 && (!viewport.CameraOrthographic || !fields["CameraSizeField"].IsVisible || fields["CameraFovField"].IsVisible); retry++) await Task.Delay(20);
            if (!viewport.CameraOrthographic || !fields["CameraSizeField"].IsVisible || fields["CameraFovField"].IsVisible) throw new Exception("Camera projection fields did not switch visibility.");
            await viewport.Controller.InvokeAsync(() => viewport.Controller.GetCameraService().ApplySettings(new ViewportCameraSettings(false, .5f, 3000, 70, 12, 5)));
            for (var retry = 0; retry < 100 && (viewport.CameraOrthographic || viewport.CameraFieldOfView != 70); retry++) await Task.Delay(20);
            if (viewport.CameraOrthographic || viewport.CameraFieldOfView != 70 || viewport.CameraMoveSpeed != 5 || fields["CameraSpeedField"].Value != 5)
                throw new Exception("Engine camera changes did not update the popup view model.");
            var beforeInvalid = await viewport.Controller.InvokeAsync(() => viewport.Controller.GetCameraService().GetSettings());
            foreach (var invalid in new[] { beforeInvalid with { NearPlane = 0 }, beforeInvalid with { NearPlane = 4000 }, beforeInvalid with { FarPlane = float.PositiveInfinity }, beforeInvalid with { FarPlane = float.MaxValue }, beforeInvalid with { FieldOfView = 180 }, beforeInvalid with { OrthographicSize = float.NaN }, beforeInvalid with { MoveSpeed = 0 } })
            {
                try { invalid.Validate(); throw new Exception("Invalid camera settings were accepted."); }
                catch (ArgumentOutOfRangeException) { }
            }
            try { viewport.CameraNearPlane = viewport.CameraFarPlane; throw new Exception("The view model accepted coincident clip planes."); }
            catch (ArgumentOutOfRangeException) { }
            flyout.Hide();
            Console.WriteLine("PASS: camera popup edits, engine-to-UI synchronization, projection fields and invalid settings validation.");

            if (viewport.RenderModes.Count < 3) throw new Exception("No material streams are registered for rendering modes.");
            foreach (var mode in viewport.RenderModes)
            {
                if (mode.PreviewGameGraphicsCompositor)
                {
                    host.RaiseEvent(new PointerPressedEventArgs(host, pointer, host, point, 0, new PointerPointProperties(RawInputModifiers.RightMouseButton, PointerUpdateKind.RightButtonPressed), KeyModifiers.None, 1));
                    await Task.Delay(100);
                    if (!await viewport.Controller.InvokeAsync(() => viewport.Controller.GetCameraService().IsControllingMouse)) throw new Exception("The camera capture prerequisite failed.");
                }
                viewport.RenderMode = mode;
                var applied = false;
                for (var retry = 0; retry < 100; retry++)
                {
                    await Task.Delay(20);
                    applied = await viewport.Controller.InvokeAsync(() =>
                    {
                        var top = (EditorTopLevelCompositor)game.SceneSystem.GraphicsCompositor.Game;
                        var filter = game.SceneSystem.GraphicsCompositor.RenderFeatures.OfType<MeshRenderFeature>().First().RenderFeatures.OfType<MaterialFilterRenderFeature>().Single().MaterialFilter;
                        return top.EnablePreviewGame == mode.PreviewGameGraphicsCompositor
                            && Equals(filter, mode.Mode == RenderMode.SingleStream ? mode.StreamDescriptor.Filter : null);
                    });
                    if (applied) break;
                }
                if (!applied) throw new Exception($"Rendering mode {mode.Name} did not apply its material filter.");
                var frame = await viewport.Controller.InvokeAsync(() => game.DrawTime.FrameCount);
                for (var retry = 0; retry < 100 && await viewport.Controller.InvokeAsync(() => game.DrawTime.FrameCount) < frame + 2; retry++) await Task.Delay(20);
                if (await viewport.Controller.InvokeAsync(() => game.DrawTime.FrameCount) < frame + 2) throw new Exception($"Rendering mode {mode.Name} did not render frames.");
                await viewport.Controller.InvokeAsync(() =>
                {
                    var top = (EditorTopLevelCompositor)game.SceneSystem.GraphicsCompositor.Game;
                    if (top.EnablePreviewGame != mode.PreviewGameGraphicsCompositor || game.EditorSceneSystem.GraphicsCompositor.Game.Enabled == mode.PreviewGameGraphicsCompositor)
                        throw new Exception($"Rendering mode {mode.Name} did not switch compositor/overlays.");
                    var cameraService = viewport.Controller.GetCameraService();
                    if (cameraService.IsActive == mode.PreviewGameGraphicsCompositor) throw new Exception("Game preview did not suspend/restore camera input.");
                    if (mode.PreviewGameGraphicsCompositor && (cameraService.IsControllingMouse || !game.IsMouseVisible)) throw new Exception("Game preview retained camera capture or hid the cursor.");
                    if (mode.PreviewGameGraphicsCompositor && game.SceneSystem.GraphicsCompositor.Cameras.Count == 0) throw new Exception("Game preview did not restore the project's camera slots.");
                    if (!mode.PreviewGameGraphicsCompositor && game.SceneSystem.GraphicsCompositor.Cameras.Count != 0) throw new Exception("Editor mode retained game camera slots.");
                });
                if (mode.PreviewGameGraphicsCompositor) Release();
                if (mode.PreviewGameGraphicsCompositor)
                {
                    await viewport.ReloadContent().WaitAsync(TimeSpan.FromSeconds(60));
                    if (!await viewport.Controller.InvokeAsync(() => ((EditorTopLevelCompositor)game.SceneSystem.GraphicsCompositor.Game).EnablePreviewGame))
                        throw new Exception("Reloading the scene lost Game preview mode.");
                }
                if (viewport.LastException != null) throw new Exception($"Rendering mode {mode.Name} failed.", viewport.LastException);
                Console.WriteLine("PASS RENDER MODE: " + mode.Name);
            }
            viewport.RenderMode = EditorRenderMode.DefaultEditor;
            await Task.Delay(150);
            if (await viewport.Controller.InvokeAsync(() => game.SceneSystem.GraphicsCompositor.RenderFeatures.OfType<MeshRenderFeature>().First().RenderFeatures.OfType<MaterialFilterRenderFeature>().Single().MaterialFilter != null))
                throw new Exception("Returning to Editor retained a material-stream filter.");
            var modeSelector = view.FindControl<ComboBox>("RenderModeSelector")!;
            if (!ReferenceEquals(modeSelector.SelectedItem, EditorRenderMode.DefaultEditor)) throw new Exception("Render-mode selector lost its binding.");
            if (editor.Asset.IsDirty != wasDirty) throw new Exception("Camera/render mode settings modified the asset dirty state.");
            Console.WriteLine("PASS: all material render modes, game-preview overlays/input suspension and return to Editor without dirtying the asset.");
        }
        finally
        {
            flyout.Hide();
            Release();
            viewport.RenderMode = originalMode;
            await Task.Delay(100);
            await viewport.Controller.InvokeAsync(() => viewport.Controller.GetCameraService().ApplySettings(original));
        }
        void Release() => host.RaiseEvent(new PointerReleasedEventArgs(host, pointer, host, point, 0, new PointerPointProperties(RawInputModifiers.None, PointerUpdateKind.RightButtonReleased), KeyModifiers.None, Avalonia.Input.MouseButton.Right));
    }
}
