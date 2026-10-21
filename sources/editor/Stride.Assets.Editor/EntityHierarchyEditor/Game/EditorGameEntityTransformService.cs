// Copyright (c) .NET Foundation and Contributors (https://dotnetfoundation.org/ & https://stride3d.net)
// Distributed under the MIT license. See the LICENSE.md file in the project root for more information.

using Stride.Assets.Editor.EntityHierarchyEditor.Services;
using Stride.Assets.Editor.GameEditor;
using Stride.Assets.Editor.GameEditor.Game;
using Stride.Assets.Editor.Gizmos;
using Stride.Assets.Editor.ViewModels;
using Stride.Core.BuildEngine;
using Stride.Editor.EditorGame.Game;
using Stride.Input;
using Stride.Rendering;
using Stride.Rendering.Compositing;

namespace Stride.Assets.Editor.EntityHierarchyEditor.Game;

public sealed class EditorGameEntityTransformService(SceneViewportController controller, SceneViewportViewModel viewport)
    : EditorGameMouseServiceBase
{
    private SceneViewportGame game = null!;
    private readonly TransformationGizmo[] gizmos = [new TranslationGizmo(), new RotationGizmo(), new ScaleGizmo()];
    private TransformationGizmo active = null!;
    private EditorGameEntitySelectionService selection = null!;
    private bool snapping;
    public override bool IsControllingMouse { get; protected set; }
    public override IEnumerable<Type> Dependencies => [typeof(IEditorGameEntitySelectionService)];

    protected override Task<bool> Initialize(EditorServiceGame editorGame)
    {
        game = (SceneViewportGame)editorGame;
        selection = game.EditorServices.Get<EditorGameEntitySelectionService>()!;
        var compositor = game.EditorSceneSystem.GraphicsCompositor;
        var opaque = new RenderStage("TransformGizmoOpaque", "Main");
        var transparent = new RenderStage("TransformGizmoTransparent", "Main") { SortMode = new BackToFrontSortMode() };
        compositor.RenderStages.Add(opaque);
        compositor.RenderStages.Add(transparent);
        var mesh = compositor.RenderFeatures.OfType<MeshRenderFeature>().First();
        mesh.RenderStageSelectors.Add(new SimpleGroupToRenderStageSelector { RenderGroup = TransformationGizmo.TransformationGizmoGroupMask });
        mesh.RenderStageSelectors.Add(new MeshTransparentRenderStageSelector
        {
            EffectName = EditorGraphicsCompositorHelper.EditorForwardShadingEffect,
            RenderGroup = TransformationGizmo.TransformationGizmoGroupMask,
            OpaqueRenderStage = opaque, TransparentRenderStage = transparent
        });
        mesh.PipelineProcessors.Add(new MeshPipelineProcessor { TransparentRenderStage = transparent });
        var top = (EditorTopLevelCompositor)compositor.Game;
        top.PostGizmoCompositors.Add(new ClearRenderer { ClearFlags = ClearRendererFlags.DepthOnly });
        top.PostGizmoCompositors.Add(new SingleStageRenderer { RenderStage = opaque, Name = "Transform gizmos" });
        top.PostGizmoCompositors.Add(new SingleStageRenderer { RenderStage = transparent, Name = "Transparent transform gizmos" });
        MicrothreadLocalDatabases.MountCommonDatabase();
        foreach (var gizmo in gizmos)
        {
            gizmo.Initialize(game.Services, game.EditorScene);
            gizmo.IsEnabled = false;
            gizmo.TransformationEnded += TransformationEnded;
            gizmo.SizeFactor = 1;
        }
        gizmos[0].SnapValue = 1;
        gizmos[1].SnapValue = 15;
        gizmos[2].SnapValue = .1f;
        active = gizmos[0];
        selection.SelectionUpdated += SelectionUpdated;
        game.Script.AddTask(Update, 1);
        return Task.FromResult(true);
    }

    public void SetSettings(Transformation mode, TransformationSpace space, bool snap, float translationStep = 1, float rotationStep = 15, float scaleStep = .1f, TransformationPivot pivot = TransformationPivot.Pivot)
    {
        active.IsEnabled = false;
        active = gizmos[(int)mode];
        snapping = snap;
        gizmos[0].SnapValue = translationStep;
        gizmos[1].SnapValue = rotationStep;
        gizmos[2].SnapValue = scaleStep;
        foreach (var gizmo in gizmos)
        {
            gizmo.Space = space;
            gizmo.Pivot = pivot;
            gizmo.UseSnap = snap;
        }
        active.IsEnabled = active.AnchorEntity != null;
    }

    private void SelectionUpdated(object? sender, EntitySelectionEventArgs e)
    {
        foreach (var gizmo in gizmos)
        {
            gizmo.AnchorEntity = e.NewSelection.LastOrDefault();
            gizmo.ModifiedEntities = e.NewSelection;
            if (e.NewSelection.Count == 0) gizmo.ClearTransformationAxes();
        }
        active.IsEnabled = active.AnchorEntity != null;
    }

    private async Task Update()
    {
        while (!IsDisposed)
        {
            if (IsActive && game.IsActive && IsMouseAvailable)
            {
                var input = game.Input;
                if (!input.IsMouseButtonDown(MouseButton.Right))
                {
                    var mode = input.IsKeyPressed(Keys.W) ? Transformation.Translation
                        : input.IsKeyPressed(Keys.E) ? Transformation.Rotation
                        : input.IsKeyPressed(Keys.R) ? Transformation.Scale : (Transformation?)null;
                    if (mode.HasValue)
                        await viewport.Dispatcher.InvokeAsync(() => viewport.ActiveTransformation = mode.Value);
                    if (input.IsKeyPressed(Keys.F)) controller.Frame(selection.GetSelectedRootIds().ToArray());
                }
                active.UseSnap = snapping || input.IsKeyDown(Keys.LeftShift);
                var wasTransforming = active.IsTransforming;
                await active.Update();
                // Hover must not block camera/reload; retain ownership on the release frame.
                IsControllingMouse = wasTransforming || active.IsTransforming || active.IsUnderMouse()
                    && (input.IsMouseButtonDown(MouseButton.Left) || input.IsMouseButtonReleased(MouseButton.Left));
            }
            else IsControllingMouse = false;
            await game.Script.NextFrame();
        }
    }

    private void TransformationEnded(object? sender, EventArgs e)
    {
        var transformations = selection.GetSelectedRootIds()
            .ToDictionary(id => id, id => new TransformationTRS(((Stride.Engine.Entity)controller.FindGameSidePart(id)!).Transform));
        _ = viewport.Dispatcher.InvokeAsync(() => viewport.CommitTransformations(transformations));
    }

    public override ValueTask DisposeAsync()
    {
        selection.SelectionUpdated -= SelectionUpdated;
        foreach (var gizmo in gizmos)
        {
            gizmo.TransformationEnded -= TransformationEnded;
            gizmo.Dispose();
        }
        return base.DisposeAsync();
    }
}
