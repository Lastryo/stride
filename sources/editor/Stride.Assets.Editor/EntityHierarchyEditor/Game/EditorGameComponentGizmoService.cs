// Copyright (c) .NET Foundation and Contributors (https://dotnetfoundation.org/ & https://stride3d.net)
// Distributed under the MIT license. See the LICENSE.md file in the project root for more information.

using Stride.Assets.Editor.GameEditor.Game;
using Stride.Assets.Editor.Gizmos;
using Stride.Assets.Editor.SceneEditor;
using Stride.Assets.Editor.ViewModels;
using Stride.Core.BuildEngine;
using Stride.Core.Extensions;
using Stride.Editor.EditorGame.Game;
using Stride.Engine;
using Stride.Engine.Design;
using Stride.Engine.Gizmos;
using Stride.Rendering;
using Stride.Rendering.Compositing;
using Stride.Rendering.Sprites;

namespace Stride.Assets.Editor.EntityHierarchyEditor.Game;

public sealed class EditorGameComponentGizmoService : EditorGameServiceBase, IEditorGameComponentGizmoService
{
    private readonly Dictionary<Entity, EntityGizmo> gizmos = [];
    private readonly HashSet<Entity> selected = [];
    private SceneViewportGame game = null!;
    private PickingSceneRenderer picker = null!;
    private EditorGameEntitySelectionService selection = null!;
    private SceneInstance? sceneInstance;
    public override bool IsActive { get; set; } = true;
    public bool FixedSize { get; set; }
    public float SceneUnit => game.EditorServices.Get<IEditorGameCameraService>()!.SceneUnit;
    public override IEnumerable<Type> Dependencies => [typeof(EditorGameEntitySelectionService)];

    protected override Task<bool> Initialize(EditorServiceGame editorGame)
    {
        game = (SceneViewportGame)editorGame;
        selection = game.EditorServices.Get<EditorGameEntitySelectionService>()!;
        selection.SelectionUpdated += SelectionUpdated;
        game.ContentReloaded += Rebuild;

        var compositor = game.EditorSceneSystem.GraphicsCompositor;
        var stage = new RenderStage("Picking", "Picking");
        compositor.RenderStages.Add(stage);
        var mesh = compositor.RenderFeatures.OfType<MeshRenderFeature>().First();
        mesh.RenderFeatures.Add(new PickingRenderFeature());
        mesh.RenderStageSelectors.Add(new SimpleGroupToRenderStageSelector
        {
            EffectName = EditorGraphicsCompositorHelper.EditorForwardShadingEffect + ".Picking",
            RenderGroup = GizmoBase.DefaultGroupMask,
            RenderStage = stage,
        });
        compositor.RenderFeatures.OfType<SpriteRenderFeature>().First().RenderStageSelectors.Add(new SimpleGroupToRenderStageSelector
        {
            EffectName = "SelectedSprite.Picking", RenderGroup = GizmoBase.DefaultGroupMask, RenderStage = stage,
        });
        ((EditorTopLevelCompositor)compositor.Game).PostGizmoCompositors.Add(picker = new PickingSceneRenderer { PickingRenderStage = stage });
        MicrothreadLocalDatabases.MountCommonDatabase();
        Rebuild();
        game.Script.AddTask(Update);
        return Task.FromResult(true);
    }

    public Entity? GetContentEntityUnderMouse()
    {
        if (!IsActive) return null;
        var id = new OpaqueComponentId(picker.Pick().ComponentId);
        foreach (var gizmo in gizmos.Values)
            if (gizmo.IsEnabled && gizmo.HandlesComponentId(id, out var entity)) return entity;
        return null;
    }

    public void UpdateGizmoEntitiesSelection(Entity entity, bool isSelected)
    {
        if (isSelected) selected.Add(entity); else selected.Remove(entity);
        if (gizmos.TryGetValue(entity, out var gizmo)) gizmo.IsSelected = isSelected;
    }

    private void SelectionUpdated(object? sender, EntitySelectionEventArgs args)
    {
        foreach (var entity in selected.ToArray()) UpdateGizmoEntitiesSelection(entity, false);
        foreach (var entity in args.NewSelection) UpdateGizmoEntitiesSelection(entity, true);
    }

    private void Rebuild()
    {
        AttachScene();
        foreach (var entity in gizmos.Keys.ToArray()) Remove(entity);
        if (game.ContentScene == null) return;
        foreach (var scene in game.ContentScene.Yield().BreadthFirst(scene => scene.Children))
            foreach (var entity in scene.Entities.BreadthFirst(entity => entity.GetChildren())) Add(entity);
    }

    public override void RegisterScene(Scene scene) => Rebuild();

    private void AttachScene()
    {
        var current = game.SceneSystem.SceneInstance;
        if (ReferenceEquals(sceneInstance, current)) return;
        DetachScene();
        sceneInstance = current;
        sceneInstance.EntityAdded += EntityAdded;
        sceneInstance.EntityRemoved += EntityRemoved;
        sceneInstance.ComponentChanged += ComponentChanged;
    }

    private void DetachScene()
    {
        if (sceneInstance == null) return;
        sceneInstance.EntityAdded -= EntityAdded;
        sceneInstance.EntityRemoved -= EntityRemoved;
        sceneInstance.ComponentChanged -= ComponentChanged;
        sceneInstance = null;
    }

    private void EntityAdded(object? sender, Entity entity) => Add(entity);
    private void EntityRemoved(object? sender, Entity entity) { Remove(entity); selected.Remove(entity); }
    private void ComponentChanged(object? sender, EntityComponentEventArgs args) { Remove(args.Entity); Add(args.Entity); }

    private void Add(Entity entity)
    {
        if (gizmos.ContainsKey(entity) || entity.Tags.Get(GizmoBase.NoGizmoKey)) return;
        // One main billboard per entity; camera/light/UI take precedence over scripts.
        EntityGizmo? gizmo = entity.Get<CameraComponent>() is { } camera ? new CameraGizmo(camera)
            : entity.Get<LightComponent>() is { } light ? new DispatcherLightGizmo(light)
            : entity.Get<UIComponent>() is { } ui ? new UIGizmo(ui)
            : entity.Get<BackgroundComponent>() is { } background ? new BackgroundGizmo(background)
            : entity.Get<AudioEmitterComponent>() is { } emitter ? new AudioEmitterGizmo(emitter)
            : entity.Get<AudioListenerComponent>() is { } listener ? new AudioListenerGizmo(listener)
            : entity.Get<ScriptComponent>() is { } script ? new ScriptGizmo(script)
            : entity.Get<ModelNodeLinkComponent>() is { } link ? new NodeLinkGizmo(link)
            : entity.Get<ModelComponent>() == null && entity.Get<SpriteComponent>() == null ? new FallbackGizmo(entity.Transform)
            : null;
        if (gizmo == null) return;
        gizmo.Initialize(game.Services, game.EditorScene);
        gizmo.IsSelected = selected.Contains(entity);
        gizmo.IsEnabled = IsActive;
        gizmo.Update();
        gizmos.Add(entity, gizmo);
    }

    private void Remove(Entity entity)
    {
        if (gizmos.Remove(entity, out var gizmo)) gizmo.Dispose();
    }

    private async Task Update()
    {
        while (!IsDisposed)
        {
            foreach (var gizmo in gizmos.Values)
            {
                gizmo.IsEnabled = IsActive;
                if (IsActive) gizmo.Update();
            }
            await game.Script.NextFrame();
        }
    }

    public override async ValueTask DisposeAsync()
    {
        await base.DisposeAsync();
        if (game == null) return;
        game.ContentReloaded -= Rebuild;
        DetachScene();
        selection.SelectionUpdated -= SelectionUpdated;
        foreach (var entity in gizmos.Keys.ToArray()) Remove(entity);
        selected.Clear();
        picker?.Dispose();
    }
}
