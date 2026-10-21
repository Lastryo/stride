// Copyright (c) .NET Foundation and Contributors (https://dotnetfoundation.org/ & https://stride3d.net)
// Distributed under the MIT license. See the LICENSE.md file in the project root for more information.

using Stride.Assets.Editor.GameEditor.Game;
using Stride.Assets.Editor.Gizmos;
using Stride.Assets.Editor.ViewModels;
using Stride.Core.BuildEngine;
using Stride.Core.Mathematics;
using Stride.Editor.EditorGame.Game;
using Stride.Rendering;
using Stride.Rendering.Compositing;

namespace Stride.Assets.Editor.EntityHierarchyEditor.Game;

public sealed class EditorGameGridService : EditorGameServiceBase
{
    private readonly ViewportGridGizmo grid = new();
    private SceneViewportGame game = null!;
    public override bool IsActive { get; set; } = true;
    public int AxisMask { get; set; } = 2; // XZ plane, perpendicular to Y.
    public Color3 Color { get; set; } = new(180 / 255f);
    public float Alpha { get; set; } = .35f;
    public override IEnumerable<Type> Dependencies => [typeof(IEditorGameCameraService)];

    protected override Task<bool> Initialize(EditorServiceGame editorGame)
    {
        game = (SceneViewportGame)editorGame;
        var compositor = game.EditorSceneSystem.GraphicsCompositor;
        var stage = new RenderStage("GridGizmo", "Main") { SortMode = new BackToFrontSortMode() };
        compositor.RenderStages.Add(stage);
        var mesh = compositor.RenderFeatures.OfType<MeshRenderFeature>().First();
        mesh.RenderStageSelectors.Add(new SimpleGroupToRenderStageSelector
        {
            EffectName = EditorGraphicsCompositorHelper.EditorForwardShadingEffect,
            RenderGroup = GridGizmoBase.GridGroupMask,
            RenderStage = stage,
        });
        mesh.PipelineProcessors.Add(new AlphaBlendPipelineProcessor { RenderStage = stage });
        ((EditorTopLevelCompositor)compositor.Game).PostGizmoCompositors.Add(new SingleStageRenderer { RenderStage = stage });
        MicrothreadLocalDatabases.MountCommonDatabase();
        grid.Initialize(game.Services, game.EditorScene);
        game.Script.AddTask(Update);
        return Task.FromResult(true);
    }

    private async Task Update()
    {
        while (!IsDisposed)
        {
            grid.IsEnabled = IsActive;
            if (IsActive) grid.Update(Color, Alpha, AxisMask, game.EditorServices.Get<IEditorGameCameraService>()!.SceneUnit);
            await game.Script.NextFrame();
        }
    }

    public override async ValueTask DisposeAsync()
    {
        await base.DisposeAsync();
        grid.Dispose();
    }
}
