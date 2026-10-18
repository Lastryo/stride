// Copyright (c) .NET Foundation and Contributors (https://dotnetfoundation.org/ & https://stride3d.net)
// Distributed under the MIT license. See the LICENSE.md file in the project root for more information.

using System.Collections.Specialized;
using Stride.Assets.Editor.EntityHierarchyEditor.Services;
using Stride.Assets.Editor.GameEditor.Game;
using Stride.Assets.Editor.Gizmos;
using Stride.Assets.Editor.SceneEditor;
using Stride.Assets.Editor.ViewModels;
using Stride.Assets.Presentation.ViewModels;
using Stride.Core;
using Stride.Core.Mathematics;
using Stride.Editor.EditorGame.Game;
using Stride.Engine;
using Stride.Input;
using Stride.Rendering;
using Stride.Rendering.Compositing;
using Stride.Rendering.Sprites;

namespace Stride.Assets.Editor.EntityHierarchyEditor.Game;

public sealed class EditorGameEntitySelectionService(SceneViewportController controller, EntityHierarchyEditorViewModel editor)
    : EditorGameMouseServiceBase, IEditorGameEntitySelectionService
{
    private SceneViewportGame game = null!;
    private PickingSceneRenderer? picker;
    private AbsoluteId[] selectedIds = [];
    private Entity[] selectedRoots = [];
    public bool DisplaySelectionMask => true;
    public override bool IsControllingMouse { get; protected set; }
    public event EventHandler<EntitySelectionEventArgs>? SelectionUpdated;
    public IReadOnlyCollection<AbsoluteId> GetSelectedRootIds() => selectedRoots.Select(controller.GetAbsoluteId).ToArray();
    public override IEnumerable<Type> Dependencies => [typeof(IEditorGameCameraService)];

    protected override Task<bool> Initialize(EditorServiceGame editorGame)
    {
        game = (SceneViewportGame)editorGame;
        editor.Dispatcher.Invoke(() => editor.SelectedItems.CollectionChanged += SelectionChanged);
        game.ContentReloaded += ContentReloaded;
        game.Script.AddTask(Update, 2);
        return Task.FromResult(true);
    }

    public override void RegisterScene(Scene scene)
    {
        picker?.CacheScene(scene, true);
        editor.Dispatcher.Invoke(QueueSelection);
    }

    private void SelectionChanged(object? sender, NotifyCollectionChangedEventArgs e) => QueueSelection();

    private void QueueSelection()
    {
        var ids = editor.SelectedItems.OfType<EntityViewModel>().Select(item => item.Id).ToArray();
        _ = controller.InvokeAsync(() => { selectedIds = ids; RefreshSelection(); });
    }

    private void ContentReloaded()
    {
        picker?.ClearCache();
        picker?.CacheScene(game.ContentScene, true);
        RefreshSelection();
    }

    private void RefreshSelection()
    {
        var previous = selectedRoots;
        var all = selectedIds.Select(controller.FindGameSidePart).OfType<Entity>().ToHashSet();
        selectedRoots = all.Where(entity => !HasSelectedParent(entity, all)).ToArray();
        SelectionUpdated?.Invoke(this, new EntitySelectionEventArgs(previous, selectedRoots));
    }

    private static bool HasSelectedParent(Entity entity, HashSet<Entity> selected)
    {
        for (var parent = entity.Transform.Parent; parent != null; parent = parent.Parent)
            if (selected.Contains(parent.Entity)) return true;
        return false;
    }

    public override void UpdateGraphicsCompositor(EditorServiceGame editorGame)
    {
        var compositor = editorGame.SceneSystem.GraphicsCompositor;
        var stage = new RenderStage("Picking", "Picking");
        compositor.RenderStages.Add(stage);
        var mesh = compositor.RenderFeatures.OfType<MeshRenderFeature>().FirstOrDefault();
        if (mesh != null)
        {
            var wireframe = new WireframeRenderFeature();
            wireframe.RegisterSelectionService(this);
            mesh.RenderFeatures.Add(wireframe);
            mesh.RenderFeatures.Add(new PickingRenderFeature());
            mesh.RenderStageSelectors.Add(new SimpleGroupToRenderStageSelector
            {
                EffectName = EditorGraphicsCompositorHelper.EditorForwardShadingEffect + ".Picking", RenderStage = stage, RenderGroup = RenderGroupMask.All
            });
        }
        compositor.RenderFeatures.OfType<SpriteRenderFeature>().FirstOrDefault()?.RenderStageSelectors.Add(new SimpleGroupToRenderStageSelector
        {
            EffectName = "SelectedSprite.Picking", RenderStage = stage, RenderGroup = RenderGroupMask.All
        });
        ((EditorTopLevelCompositor)compositor.Game).PostGizmoCompositors.Add(picker = new PickingSceneRenderer { PickingRenderStage = stage });
        if (game.ContentScene != null) picker.CacheScene(game.ContentScene, true);
    }

    private async Task Update()
    {
        var distance = Vector2.Zero;
        while (!IsDisposed)
        {
            if (IsActive && game.IsActive)
            {
                var input = game.Input;
                if (input.IsMouseButtonPressed(MouseButton.Left)) distance = Vector2.Zero;
                var buffer = game.GraphicsDevice.Presenter.BackBuffer;
                distance += new Vector2(Math.Abs(input.MouseDelta.X * buffer.Width), Math.Abs(input.MouseDelta.Y * buffer.Height));
                if (IsMouseAvailable && input.IsMouseButtonReleased(MouseButton.Left) && !input.IsMouseButtonDown(MouseButton.Right)
                    && distance.Length() < TransformationGizmo.TransformationStartPixelThreshold)
                {
                    var entity = picker?.Pick().Entity;
                    var id = entity is null ? (AbsoluteId?)null : controller.GetAbsoluteId(entity);
                    var add = input.IsKeyDown(Keys.LeftCtrl) || input.IsKeyDown(Keys.RightCtrl);
                    await editor.Dispatcher.InvokeAsync(() =>
                    {
                        if (!add) editor.SelectedContent.Clear();
                        if (id is { } value && editor.FindPartViewModel(value) is EntityViewModel item)
                        {
                            if (add && editor.SelectedContent.Contains(item)) editor.SelectedContent.Remove(item);
                            else editor.SelectedContent.Add(item);
                        }
                    });
                }
            }
            await game.Script.NextFrame();
        }
    }

    public override ValueTask DisposeAsync()
    {
        editor.Dispatcher.Invoke(() => editor.SelectedItems.CollectionChanged -= SelectionChanged);
        game.ContentReloaded -= ContentReloaded;
        return base.DisposeAsync();
    }
}
