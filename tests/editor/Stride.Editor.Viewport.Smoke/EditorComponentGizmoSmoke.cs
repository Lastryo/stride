// Copyright (c) .NET Foundation and Contributors (https://dotnetfoundation.org/ & https://stride3d.net)
// Distributed under the MIT license. See the LICENSE.md file in the project root for more information.

using System.Reflection;
using Avalonia.Input;
using Stride.Assets.Editor.EntityHierarchyEditor.Game;
using Stride.Assets.Editor.Gizmos;
using Stride.Assets.Editor.ViewModels;
using Stride.Core.Mathematics;
using Stride.Editor.Avalonia.Engine;
using Stride.Engine;
using Stride.Rendering.Lights;

internal static class EditorComponentGizmoSmoke
{
    public static async Task Check(EntityHierarchyEditorViewModel editor, SceneViewportGame game)
    {
        var viewport = editor.Viewport!;
        var service = game.EditorServices.Get<EditorGameComponentGizmoService>()!;
        var camera = game.EditorServices.Get<EditorGameEntityCameraService>()!;
        var gizmos = (Dictionary<Entity, EntityGizmo>)typeof(EditorGameComponentGizmoService).GetField("gizmos", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(service)!;
        var view = (AvaloniaGameView)viewport.EditorHost!;
        var pointer = new Avalonia.Input.Pointer(104, PointerType.Mouse, true);
        var point = new Avalonia.Point(view.Bounds.Width / 2, view.Bounds.Height / 2);
        var selected = editor.SelectedContent.ToArray();
        var visible = viewport.GizmosVisible;
        var dirty = editor.Asset.IsDirty;
        var originalAsset = SerializeAsset();
        Entity? probe = null;
        try
        {
            viewport.GizmosVisible = true;
            editor.SelectedContent.Clear();
            var target = await viewport.Controller.InvokeAsync(() => camera.Position + Matrix.Invert(camera.ViewMatrix).Forward * 4);
            foreach (var item in editor.HierarchyRoot.InnerSubEntities.Where(item => item.AssetSideEntity.Get<CameraComponent>() != null || item.AssetSideEntity.Get<LightComponent>() != null))
            {
                var entity = await viewport.Controller.InvokeAsync(() => (Entity)viewport.Controller.FindGameSidePart(item.Id)!);
                var position = await viewport.Controller.InvokeAsync(() => entity.Transform.Position);
                try
                {
                    await viewport.Controller.InvokeAsync(() => { entity.Transform.Position = target; entity.Transform.UpdateWorldMatrix(); });
                    Move();
                    Entity? picked = null;
                    for (var retry = 0; retry < 40; retry++)
                    {
                        Move();
                        await Task.Delay(50);
                        picked = await viewport.Controller.InvokeAsync(service.GetContentEntityUnderMouse);
                        if (ReferenceEquals(picked, entity)) break;
                    }
                    if (!ReferenceEquals(picked, entity))
                    {
                        var state = await viewport.Controller.InvokeAsync(() => $"exists={gizmos.ContainsKey(entity)}, enabled={gizmos.GetValueOrDefault(entity)?.IsEnabled}, world={entity.Transform.WorldMatrix.TranslationVector}, root={gizmos.GetValueOrDefault(entity)?.GizmoRootEntity?.Transform.Position}, camera={camera.Position}, target={target}, active={service.IsActive}");
                        throw new Exception($"Component billboard picking failed for {entity.Name}: picked {picked?.Name}; {state}.", viewport.LastException);
                    }
                    view.RaiseEvent(new PointerPressedEventArgs(view, pointer, view, point, 0, new PointerPointProperties(RawInputModifiers.LeftMouseButton, PointerUpdateKind.LeftButtonPressed), KeyModifiers.None, 1));
                    await Task.Delay(100);
                    Release();
                    await Task.Delay(200);
                    if (!editor.SelectedItems.Contains(item)) throw new Exception("Clicking a component billboard did not select its asset entity.");
                    if (!await viewport.Controller.InvokeAsync(() => gizmos[entity].IsSelected)) throw new Exception("The selected component gizmo lost selection state.");
                    if (entity.Get<CameraComponent>() != null && !await viewport.Controller.InvokeAsync(() => gizmos[entity].GizmoRootEntity.Transform.Children.Count > 1))
                        throw new Exception("Selecting the camera did not display its frustum.");
                    viewport.GizmosVisible = false;
                    await Task.Delay(100);
                    if (await viewport.Controller.InvokeAsync(service.GetContentEntityUnderMouse) != null) throw new Exception("Hidden gizmos still participate in picking.");
                    viewport.GizmosVisible = true;
                    editor.SelectedContent.Clear();
                    await Task.Delay(100);
                }
                finally { await viewport.Controller.InvokeAsync(() => { entity.Transform.Position = position; entity.Transform.UpdateWorldMatrix(); }); }
            }

            await viewport.Controller.InvokeAsync(() =>
            {
                probe = new Entity("Component gizmo probe") { new CameraComponent() };
                probe.Transform.Position = target;
                game.ContentScene.Entities.Add(probe);
            });
            await Task.Delay(100);
            await viewport.Controller.InvokeAsync(() =>
            {
                if (gizmos[probe!] is not CameraGizmo) throw new Exception("Adding a runtime entity did not create its gizmo.");
                var old = gizmos[probe!];
                probe!.Components.Remove(probe.Get<CameraComponent>());
                probe.Add(new LightComponent { Type = new LightPoint() });
                if (gizmos[probe] is not DispatcherLightGizmo || !old.IsDisposed) throw new Exception("Replacing a component did not dispose/recreate its gizmo.");
            });
            foreach (var light in new ILight[] { new LightSpot(), new LightDirectional(), new LightAmbient(), new LightSkybox() })
            {
                await viewport.Controller.InvokeAsync(() => { probe!.Get<LightComponent>().Type = light; service.UpdateGizmoEntitiesSelection(probe, true); });
                await Task.Delay(100);
            }
            await viewport.Controller.InvokeAsync(() =>
            {
                var old = gizmos[probe!];
                game.ContentScene.Entities.Remove(probe!);
                if (gizmos.ContainsKey(probe!) || !old.IsDisposed) throw new Exception("Removing an entity did not dispose its gizmo.");
                probe = null;
            });
            var oldGizmos = await viewport.Controller.InvokeAsync(() => gizmos.Values.ToArray());
            await viewport.ReloadContent().WaitAsync(TimeSpan.FromSeconds(60));
            await viewport.Controller.InvokeAsync(() =>
            {
                if (oldGizmos.Any(gizmo => !gizmo.IsDisposed) || gizmos.Count == 0) throw new Exception("Scene reload retained old gizmos or lost all component icons.");
                if (!gizmos.Values.Any(gizmo => gizmo is UIGizmo) || !gizmos.Values.Any(gizmo => gizmo is ScriptGizmo)) throw new Exception("The sample's UI/script billboards are missing.");
            });
            if (dirty != editor.Asset.IsDirty || !originalAsset.SequenceEqual(SerializeAsset()))
                throw new Exception("Component gizmos modified the asset.");
            if (viewport.LastException != null) throw new Exception("Component gizmo rendering failed.", viewport.LastException);
            Console.WriteLine("PASS: component icon picking selects cameras/lights, selected camera frustum, Gizmos visibility, component replacement/light dispatch, entity removal and scene reload dispose old gizmos; UI/script icons and asset state survive.");
        }
        finally
        {
            Release();
            if (probe != null) await viewport.Controller.InvokeAsync(() => game.ContentScene.Entities.Remove(probe));
            viewport.GizmosVisible = visible;
            editor.SelectedContent.Clear();
            foreach (var item in selected) editor.SelectedContent.Add(item);
        }
        byte[] SerializeAsset()
        {
            using var stream = new MemoryStream();
            Stride.Core.Assets.AssetFileSerializer.Default.Save(stream, editor.Asset.AssetItem.Asset, null);
            return stream.ToArray();
        }
        void Move()
        {
            point = new Avalonia.Point(view.Bounds.Width / 2, view.Bounds.Height / 2);
            view.RaiseEvent(new PointerEventArgs(InputElement.PointerMovedEvent, view, pointer, view, point, 0, new PointerPointProperties(), KeyModifiers.None));
        }
        void Release() => view.RaiseEvent(new PointerReleasedEventArgs(view, pointer, view, point, 0, new PointerPointProperties(RawInputModifiers.None, PointerUpdateKind.LeftButtonReleased), KeyModifiers.None, Avalonia.Input.MouseButton.Left));
    }
}
