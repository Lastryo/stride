// Copyright (c) .NET Foundation and Contributors (https://dotnetfoundation.org/ & https://stride3d.net)
// Distributed under the MIT license. See the LICENSE.md file in the project root for more information.

using Stride.Assets.Editor.EntityHierarchyEditor.Game;
using Stride.Assets.Editor.GameEditor.Game;
using Stride.Assets.Editor.ViewModels;
using Stride.Assets.Entities;
using Stride.Assets.Presentation.ViewModels;
using Stride.Core;
using Stride.Core.Annotations;
using Stride.Core.Assets;
using Stride.Core.Assets.Presentation.ViewModels;
using Stride.Core.Quantum;
using Stride.Editor.Build;
using Stride.Editor.EditorGame.Game;
using Stride.Engine;

namespace Stride.Assets.Editor.EntityHierarchyEditor.Services;

public sealed class SceneViewportController : EntityHierarchyEditorController
{
    private readonly SceneViewportViewModel viewport;
    private Scene? loadedScene;
    private Prefab? loadedPrefab;
    private bool cameraInitialized;

    public SceneViewportController(AssetViewModel asset, SceneViewportViewModel editor)
        : base(asset, editor, (loaded, compiler, logPath) => new SceneViewportGame(loaded, compiler, logPath))
    {
        viewport = editor;
    }

    public new SceneViewportGame Game => (SceneViewportGame)base.Game;

    protected override void InitializeServices(EditorGameServiceRegistry services)
    {
        base.InitializeServices(services);
        services.Add(new EditorGameGraphicsCompositorService(this, viewport));
        services.Add(new EditorGameEntityCameraService());
        services.Add(new EditorGameGridService());
        services.Add(new EditorGameCameraOrientationService());
        services.Add(new EditorGameEntitySelectionService(this, viewport.Owner));
        services.Add(new EditorGameComponentGizmoService());
        services.Add(new EditorGameEntityTransformService(this, viewport));
        services.Add(new EditorGameRenderModeService());
    }

    // shortcut: rebuild the owning asset on edits; use incremental propagation when reloads become costly.
    public async Task ReloadContent()
    {
        EnsureAssetAccess();
        Asset.AssetItem.IsDirty = true;
        var database = Asset.ServiceProvider.Get<GameStudioDatabase>();
        await database.Build(Asset.AssetItem);
        await InvokeTask(async () =>
        {
            using (await database.MountInCurrentMicroThread())
            {
                var transform = Game.EditorServices.Get<EditorGameEntityTransformService>();
                while (transform?.IsControllingMouse == true)
                    await Game.Script.NextFrame();
                if (!Game.Content.Exists(Asset.AssetItem.Location))
                    throw new InvalidOperationException($"The compiled asset '{Asset.Url}' is unavailable. The previous scene was kept.");
                var oldScene = loadedScene;
                var oldPrefab = loadedPrefab;
                // Remove old instances before unloading their referenced GPU resources.
                Game.ReplaceContent(new Scene());
                if (oldScene != null) Game.Content.Unload(oldScene);
                if (oldPrefab != null) Game.Content.Unload(oldPrefab);
                loadedScene = null;
                loadedPrefab = null;
                if (Asset is PrefabViewModel)
                {
                    loadedPrefab = Game.Content.Load<Prefab>(Asset.AssetItem.Location);
                    var scene = new Scene();
                    scene.Entities.AddRange(loadedPrefab.Entities);
                    Game.ReplaceContent(scene);
                }
                else
                {
                    loadedScene = Game.Content.Load<Scene>(Asset.AssetItem.Location);
                    Game.ReplaceContent(loadedScene);
                }
                if (!viewport.Owner.HierarchyRoot.IsLoaded)
                    Game.SetContentLoaded(false);
                if (!cameraInitialized)
                {
                    Game.EditorServices.Get<EditorGameEntityCameraService>()!.Frame(Game.ContentScene);
                    cameraInitialized = true;
                }
            }
        });
    }

    public EditorGameEntityTransformService GetTransformService() => Game.EditorServices.Get<EditorGameEntityTransformService>()!;
    public EditorGameEntitySelectionService GetSelectionService() => Game.EditorServices.Get<EditorGameEntitySelectionService>()!;
    public EditorGameGridService GetGridService() => Game.EditorServices.Get<EditorGameGridService>()!;
    public EditorGameComponentGizmoService GetComponentGizmoService() => Game.EditorServices.Get<EditorGameComponentGizmoService>()!;
    public EditorGameEntityCameraService GetCameraService() => Game.EditorServices.Get<EditorGameEntityCameraService>()!;
    public EditorGameRenderModeService GetRenderModeService() => Game.EditorServices.Get<EditorGameRenderModeService>()!;

    public void Frame(IReadOnlyList<AbsoluteId> ids)
    {
        var camera = Game.EditorServices.Get<EditorGameEntityCameraService>()!;
        var entities = ids.Select(FindPart).OfType<Entity>().ToArray();
        if (entities.Length > 0) camera.Frame(entities); else camera.Frame(Game.ContentScene);
    }

    public override AbsoluteId GetAbsoluteId(Entity entity) => new(Asset.Id, entity.Id);
    protected override object? FindPart(AbsoluteId id) =>
        id.AssetId == Asset.Id || id.AssetId == AssetId.Empty ? Game.FindSubEntity(Guid.Empty, id.ObjectId) : null;

    public override Task AddPart([NotNull] EntityHierarchyItemViewModel parent, Entity assetSidePart)
    {
        EnsureAssetAccess();

        var gameSidePart = ClonePartForGameSide((EntityHierarchyAssetBase)parent.Asset.Asset, assetSidePart);
        return InvokeAsync(() =>
        {
            Logger.Debug($"Adding entity {assetSidePart.Id} to game-side scene");

            var parentEntity = (parent as EntityViewModel)?.AssetSideEntity;
            if (parentEntity == null)
            {
                var activeScene = Game.ActiveContentScene ?? Game.ContentScene.Children.FirstOrDefault() ?? Game.ContentScene;
                activeScene.Entities.Add(gameSidePart);
            }
            else
            {
                var gameParent = (Entity?)FindPart(parent.Id);
                if (gameParent == null)
                    throw new InvalidOperationException($"The given {nameof(parent.Id)} does not correspond to any existing part.");

                gameParent.Transform.Children.Add(gameSidePart.Transform);
                GameSideNodeContainer.GetOrCreateNode(gameParent.Transform.Children).Add(gameSidePart.Transform);
            }
        });
    }

    public override Task RemovePart([NotNull] EntityHierarchyItemViewModel parent, Entity assetSidePart)
    {
        EnsureAssetAccess();

        return InvokeAsync(() =>
        {
            Logger.Debug($"Removing entity {assetSidePart.Id} from game-side scene");
            var partId = new AbsoluteId(Asset.Id, assetSidePart.Id);
            var part = (Entity?)FindPart(partId);
            if (part == null)
                return;

            var parentEntity = (parent as EntityViewModel)?.AssetSideEntity;
            if (parentEntity == null)
            {
                var activeScene = part.Scene ?? Game.ActiveContentScene ?? Game.ContentScene.Children.FirstOrDefault() ?? Game.ContentScene;
                activeScene.Entities.Remove(part);
            }
            else
            {
                var gameParent = (Entity?)FindPart(parent.Id);
                if (gameParent != null)
                {
                    var i = gameParent.Transform.Children.IndexOf(part.Transform);
                    if (i >= 0)
                    {
                        GameSideNodeContainer.GetOrCreateNode(gameParent.Transform.Children).Remove(part.Transform, new NodeIndex(i));
                    }
                    gameParent.Transform.Children.Remove(part.Transform);
                }
            }
        });
    }
}
