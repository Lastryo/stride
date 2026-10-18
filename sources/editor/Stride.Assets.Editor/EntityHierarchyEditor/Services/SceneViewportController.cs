// Copyright (c) .NET Foundation and Contributors (https://dotnetfoundation.org/ & https://stride3d.net)
// Distributed under the MIT license. See the LICENSE.md file in the project root for more information.

using Stride.Assets.Editor.EntityHierarchyEditor.Game;
using Stride.Assets.Editor.ViewModels;
using Stride.Assets.Presentation.ViewModels;
using Stride.Core;
using Stride.Core.Assets.Presentation.ViewModels;
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

    private new SceneViewportGame Game => (SceneViewportGame)base.Game;

    protected override void InitializeServices(EditorGameServiceRegistry services)
    {
        base.InitializeServices(services);
        services.Add(new EditorGameGraphicsCompositorService(this, viewport));
        services.Add(new EditorGameEntityCameraService());
        services.Add(new EditorGameEntitySelectionService(this, viewport.Owner));
        services.Add(new EditorGameEntityTransformService(this, viewport));
    }

    // shortcut: rebuild the owning asset on edits; use incremental propagation when reloads become costly.
    public async Task ReloadContent()
    {
        EnsureAssetAccess();
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
                if (!cameraInitialized)
                {
                    Game.EditorServices.Get<EditorGameEntityCameraService>()!.Frame(Game.ContentScene);
                    cameraInitialized = true;
                }
            }
        });
    }

    public EditorGameEntityTransformService GetTransformService() => Game.EditorServices.Get<EditorGameEntityTransformService>()!;

    public void Frame(IReadOnlyList<AbsoluteId> ids)
    {
        var camera = Game.EditorServices.Get<EditorGameEntityCameraService>()!;
        var entities = ids.Select(FindPart).OfType<Entity>().ToArray();
        if (entities.Length > 0) camera.Frame(entities); else camera.Frame(Game.ContentScene);
    }

    public override AbsoluteId GetAbsoluteId(Entity entity) => new(Asset.Id, entity.Id);
    protected override object? FindPart(AbsoluteId id) => id.AssetId == Asset.Id ? Game.FindSubEntity(Guid.Empty, id.ObjectId) : null;

    // Compiling the current asset keeps resource references consistent for additions and removals.
    public override Task AddPart(EntityHierarchyItemViewModel parent, Entity part) => ReloadContent();
    public override Task RemovePart(EntityHierarchyItemViewModel parent, Entity part) => ReloadContent();
}
