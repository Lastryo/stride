// Copyright (c) .NET Foundation and Contributors (https://dotnetfoundation.org/ & https://stride3d.net)
// Distributed under the MIT license. See the LICENSE.md file in the project root for more information.

using Avalonia.Input;
using Stride.Assets.Editor.EntityHierarchyEditor.Game;
using Stride.Assets.Editor.ViewModels;
using Stride.Core.Mathematics;
using Stride.Editor.Avalonia.Engine;

internal static class EditorCameraWheelSmoke
{
    public static async Task Check(SceneViewportViewModel viewport, SceneViewportGame game)
    {
        var camera = game.EditorServices.Get<EditorGameEntityCameraService>()!;
        var view = (AvaloniaGameView)viewport.EditorHost!;
        var pointer = new Avalonia.Input.Pointer(103, PointerType.Mouse, true);
        var point = new Avalonia.Point(view.Bounds.Width / 2, view.Bounds.Height / 2);
        var original = await viewport.Controller.InvokeAsync(() => (camera.IsOrthographic, camera.Component.OrthographicSize));
        try
        {
            await viewport.Controller.InvokeAsync(() => camera.IsOrthographic = false);
            var before = await State();
            await Wheel(1);
            var forward = await State();
            var direction = await viewport.Controller.InvokeAsync(() => Matrix.Invert(camera.ViewMatrix).Forward);
            var distance = Vector3.Dot(forward.Position - before.Position, direction);
            // Subtracting world positions loses precision before the dot product.
            if (MathF.Abs(distance - 1.2f) > .0001f || forward.Fov != before.Fov)
                throw new Exception($"Perspective wheel must move by 1.2 without changing FOV; got distance={distance:R}, delta={forward.Position - before.Position}, FOV={before.Fov}->{forward.Fov}.");
            await Wheel(-1);
            if (Vector3.Distance((await State()).Position, before.Position) > .0001f) throw new Exception("Opposite wheel steps did not restore camera position.");
            await Wheel(.25);
            if (MathF.Abs(Vector3.Dot((await State()).Position - before.Position, direction) - .3f) > .0001f) throw new Exception("Fractional wheel delta was lost.");
            await Wheel(-.25);

            await viewport.Controller.InvokeAsync(() => { camera.IsOrthographic = true; camera.Component.OrthographicSize = 10; });
            before = await State();
            await Wheel(1);
            var zoomed = await State();
            if (!(zoomed.Size < before.Size) || zoomed.Position != before.Position || zoomed.Fov != before.Fov)
                throw new Exception("Orthographic wheel must reduce projection size without moving the camera or changing FOV.");
            await Wheel(-1);
            if (!MathUtil.NearEqual((await State()).Size, before.Size)) throw new Exception("Orthographic wheel out did not restore projection size.");
            await Wheel(1_000_000);
            if (!((await State()).Size > 0)) throw new Exception("Orthographic zoom crossed zero.");
            await Wheel(-1_000_000);
            if (!float.IsFinite((await State()).Size)) throw new Exception("Orthographic zoom became non-finite.");

            foreach (var orthographic in new[] { false, true })
            {
                await viewport.Controller.InvokeAsync(() => { camera.IsOrthographic = orthographic; camera.Component.OrthographicSize = 10; });
                view.RaiseEvent(new PointerPressedEventArgs(view, pointer, view, point, 0, new PointerPointProperties(RawInputModifiers.RightMouseButton, PointerUpdateKind.RightButtonPressed), KeyModifiers.None, 1));
                await Task.Delay(100);
                before = await State();
                await Wheel(1, true);
                var faster = await State();
                if (!(faster.Speed > before.Speed) || faster.Position != before.Position || faster.Size != before.Size || faster.Fov != before.Fov)
                    throw new Exception("RMB + wheel must increase flight speed without zooming.");
                await Wheel(-1, true);
                var slower = await State();
                if (!(slower.Speed < faster.Speed) || slower.Position != before.Position || slower.Size != before.Size)
                    throw new Exception("RMB + wheel must decrease flight speed without zooming.");
                Release();
                await Task.Delay(100);
            }
            Console.WriteLine("PASS: wheel perspective direction/fixed step/fractional deltas, orthographic projection zoom and positive limits, RMB speed changes in both projections without zoom.");
        }
        finally
        {
            Release();
            await Task.Delay(100);
            await viewport.Controller.InvokeAsync(() => { camera.IsOrthographic = original.IsOrthographic; camera.Component.OrthographicSize = original.OrthographicSize; camera.Frame(game.ContentScene); });
        }

        Task<(Vector3 Position, float Size, float Fov, float Speed)> State() => viewport.Controller.InvokeAsync(() => (camera.Position, camera.Component.OrthographicSize, camera.VerticalFieldOfView, camera.MoveSpeed));
        async Task Wheel(double delta, bool right = false)
        {
            view.RaiseEvent(new PointerWheelEventArgs(view, pointer, view, point, 0, new PointerPointProperties(right ? RawInputModifiers.RightMouseButton : RawInputModifiers.None, PointerUpdateKind.Other), KeyModifiers.None, new Avalonia.Vector(0, delta)));
            await Task.Delay(200);
        }
        void Release() => view.RaiseEvent(new PointerReleasedEventArgs(view, pointer, view, point, 0, new PointerPointProperties(RawInputModifiers.None, PointerUpdateKind.RightButtonReleased), KeyModifiers.None, Avalonia.Input.MouseButton.Right));
    }
}
