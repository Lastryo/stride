// Copyright (c) .NET Foundation and Contributors (https://dotnetfoundation.org/ & https://stride3d.net)
// Distributed under the MIT license. See the LICENSE.md file in the project root for more information.

using System.Reflection;
using Avalonia.Controls;
using Avalonia.Input;
using Stride.Assets.Editor.EntityHierarchyEditor.Game;
using Stride.Assets.Editor.GameEditor.Game;
using Stride.Assets.Editor.Gizmos;
using Stride.Assets.Editor.ViewModels;
using Stride.Core.Mathematics;
using Stride.Editor.Avalonia.Engine;
using Stride.Engine;
using Point = Avalonia.Point;
using Vector3 = Stride.Core.Mathematics.Vector3;

internal static class EditorOrientationSmoke
{
    public static async Task Check(EntityHierarchyEditorViewModel editor, SceneViewportGame game)
    {
        var viewport = editor.Viewport!;
        var view = (AvaloniaGameView)viewport.EditorHost!;
        var service = game.EditorServices.Get<EditorGameCameraOrientationService>()!;
        var camera = game.EditorServices.Get<EditorGameEntityCameraService>()!;
        var gizmo = (CameraOrientationGizmo)typeof(EditorGameCameraOrientationService).GetField("gizmo", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(service)!;
        var renderer = typeof(CameraOrientationGizmo).GetField("gizmoViewportRenderer", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(gizmo)!;
        var selected = editor.SelectedContent.ToArray();
        var wasDirty = editor.Asset.IsDirty;
        var original = await viewport.Controller.InvokeAsync(() => (Direction: Matrix.Invert(camera.ViewMatrix).Forward, Orthographic: camera.IsOrthographic));
        var pointer = new Avalonia.Input.Pointer(102, PointerType.Mouse, true);
        Point point = default;
        try
        {
            editor.SelectedContent.Clear();
            editor.SelectedContent.Add(editor.HierarchyRoot.InnerSubEntities.First());
            var expectedSelection = editor.SelectedContent.ToArray();
            foreach (var element in new[] { new Int3(1, 0, 0), new Int3(-1, 0, 0), new Int3(0, 1, 0), new Int3(0, -1, 0), new Int3(0, 0, 1), new Int3(0, 0, -1), new Int3(1, 1, 1), new Int3(-1, 1, -1), new Int3(1, 0, 1) })
            {
                var direction = Vector3.Normalize(new Vector3(-element.X, -element.Y, -element.Z));
                await viewport.Controller.InvokeAsync(() => { camera.IsOrthographic = false; camera.ResetCamera(Vector3.Normalize(direction + new Vector3(.03f, .02f, .01f))); });
                await Task.Delay(100);
                point = await viewport.Controller.InvokeAsync(() =>
                {
                    var rect = (Stride.Graphics.Viewport)renderer.GetType().GetField("Viewport")!.GetValue(renderer)!;
                    var size = (Vector2)renderer.GetType().GetField("OutputSize")!.GetValue(renderer)!;
                    if (rect.X < 0 || rect.Y < 0 || rect.X + rect.Width > size.X + 1 || rect.Y + rect.Height > size.Y + 1)
                        throw new Exception("The orientation cube is outside the viewport.");
                    return new Point((rect.X + rect.Width / 2) / size.X * view.Bounds.Width, (rect.Y + rect.Height / 2) / size.Y * view.Bounds.Height);
                });
                Move(point);
                await Task.Delay(100);
                if (!await viewport.Controller.InvokeAsync(() => gizmo.HasSelection && gizmo.SelectedElement == element && service.IsControllingMouse))
                    throw new Exception($"The orientation cube did not pick element {element} at {point}.");
                await Click();
                var actual = await viewport.Controller.InvokeAsync(() => Matrix.Invert(camera.ViewMatrix).Forward);
                if (Vector3.Dot(actual, direction) < .99999f) throw new Exception("The cube selected the wrong camera direction.");
                if (await viewport.Controller.InvokeAsync(() => camera.IsOrthographic)) throw new Exception("The first cube click should align the camera before toggling projection.");
                if (element.LengthSquared() == 1)
                {
                    await Click();
                    if (!await viewport.Controller.InvokeAsync(() => camera.IsOrthographic)) throw new Exception("Clicking an aligned cube face did not enable orthographic projection.");
                    await Click();
                    if (await viewport.Controller.InvokeAsync(() => camera.IsOrthographic)) throw new Exception("Clicking the cube face again did not restore perspective projection.");
                }
                if (!editor.SelectedContent.SequenceEqual(expectedSelection)) throw new Exception("A cube click changed scene selection.");
                await viewport.Controller.InvokeAsync(() =>
                {
                    foreach (var name in new[] { "View Gizmo", "Space Marker" })
                    {
                        var overlayCamera = game.EditorScene.Entities.Single(entity => entity.Name == name).Get<CameraComponent>();
                        var overlayDirection = Matrix.Invert(overlayCamera.ViewMatrix).Forward;
                        if (Vector3.Dot(overlayDirection, direction) < .999f) throw new Exception($"{name} stopped following the scene camera.");
                    }
                });
            }
            var beforeCancelledClick = await viewport.Controller.InvokeAsync(() => camera.ViewMatrix);
            view.RaiseEvent(new PointerPressedEventArgs(view, pointer, view, point, 0, new PointerPointProperties(RawInputModifiers.LeftMouseButton, PointerUpdateKind.LeftButtonPressed), KeyModifiers.None, 1));
            await Task.Delay(100);
            point = new Point(view.Bounds.Width / 2, view.Bounds.Height / 2);
            Move(point);
            await Task.Delay(100);
            Release();
            await Task.Delay(100);
            if (await viewport.Controller.InvokeAsync(() => service.IsControllingMouse)) throw new Exception("The orientation cube retained the mouse after leaving its viewport.");
            if (await viewport.Controller.InvokeAsync(() => camera.ViewMatrix != beforeCancelledClick) || !editor.SelectedContent.SequenceEqual(expectedSelection))
                throw new Exception("Releasing a cube click outside the cube changed camera orientation or scene selection.");
            if (editor.Asset.IsDirty != wasDirty) throw new Exception("Camera orientation changed the asset dirty state.");
            if (viewport.LastException != null) throw new Exception("Orientation rendering failed.", viewport.LastException);
            Console.WriteLine("PASS: orientation cube picks six faces, two corners and an edge; projection toggles, overlays follow the camera and clicks preserve scene selection.");
        }
        finally
        {
            Release();
            Move(new Point(view.Bounds.Width / 2, view.Bounds.Height / 2));
            editor.SelectedContent.Clear();
            foreach (var item in selected) editor.SelectedContent.Add(item);
            await viewport.Controller.InvokeAsync(() => { camera.IsOrthographic = original.Orthographic; camera.ResetCamera(original.Direction); camera.Frame(game.ContentScene); });
        }

        void Move(Point position) => view.RaiseEvent(new PointerEventArgs(InputElement.PointerMovedEvent, view, pointer, view, position, 0, new PointerPointProperties(), KeyModifiers.None));
        void Release() => view.RaiseEvent(new PointerReleasedEventArgs(view, pointer, view, point, 0, new PointerPointProperties(RawInputModifiers.None, PointerUpdateKind.LeftButtonReleased), KeyModifiers.None, Avalonia.Input.MouseButton.Left));
        async Task Click()
        {
            view.RaiseEvent(new PointerPressedEventArgs(view, pointer, view, point, 0, new PointerPointProperties(RawInputModifiers.LeftMouseButton, PointerUpdateKind.LeftButtonPressed), KeyModifiers.None, 1));
            await Task.Delay(100);
            Release();
            await Task.Delay(100);
        }
    }
}
