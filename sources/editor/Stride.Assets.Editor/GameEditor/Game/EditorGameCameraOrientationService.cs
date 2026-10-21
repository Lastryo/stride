// Copyright (c) .NET Foundation and Contributors (https://dotnetfoundation.org/ & https://stride3d.net)
// Distributed under the MIT license. See the LICENSE.md file in the project root for more information.

using Stride.Assets.Editor.EntityHierarchyEditor.Game;
using Stride.Assets.Editor.Gizmos;
using Stride.Core.BuildEngine;
using Stride.Core.Mathematics;
using Stride.Editor.EditorGame.Game;
using Stride.Input;

namespace Stride.Assets.Editor.GameEditor.Game;

public sealed class EditorGameCameraOrientationService : EditorGameMouseServiceBase
{
    private SceneViewportGame game = null!;
    private CameraOrientationGizmo gizmo = null!;
    private SpaceMarker marker = null!;
    private Int3? clickedElement;

    public override bool IsControllingMouse { get; protected set; }
    public override IEnumerable<Type> Dependencies => [typeof(EditorGameEntityCameraService)];
    internal EditorGameEntityCameraService Camera => Services.Get<EditorGameEntityCameraService>()!;

    protected override Task<bool> Initialize(EditorServiceGame editorGame)
    {
        game = (SceneViewportGame)editorGame;
        MicrothreadLocalDatabases.MountCommonDatabase();
        gizmo = new CameraOrientationGizmo(this, game);
        gizmo.Initialize(game.Services, game.EditorScene);
        marker = new SpaceMarker(game);
        marker.Initialize(game.Services, game.EditorScene);
        game.Script.AddTask(Update);
        return Task.FromResult(true);
    }

    private async Task Update()
    {
        while (!IsDisposed)
        {
            gizmo.IsEnabled = marker.IsEnabled = IsActive;
            if (IsActive)
            {
                gizmo.Update();
                marker.Update();
            }
            var available = IsActive && game.IsActive && IsMouseAvailable;
            var hovered = available && gizmo.HasSelection;
            var input = game.Input;
            // Retain ownership on the release frame so selection cannot consume the same click.
            IsControllingMouse = available && (hovered || clickedElement.HasValue);
            if (hovered && input.IsMouseButtonPressed(MouseButton.Left)) clickedElement = gizmo.SelectedElement;
            if (input.IsMouseButtonReleased(MouseButton.Left))
            {
                if (hovered && clickedElement == gizmo.SelectedElement)
                {
                    if (gizmo.IsViewParallelToAxis && clickedElement.Value.LengthSquared() == 1)
                        Camera.IsOrthographic = !Camera.IsOrthographic;
                    else
                        Camera.ResetCamera(new Vector3(-clickedElement.Value.X, -clickedElement.Value.Y, -clickedElement.Value.Z));
                }
                clickedElement = null;
            }
            if (!available) clickedElement = null;
            await game.Script.NextFrame();
        }
    }

    public override async ValueTask DisposeAsync()
    {
        await base.DisposeAsync();
        gizmo?.Dispose();
        marker?.Dispose();
    }
}
