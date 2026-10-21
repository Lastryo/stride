// Copyright (c) .NET Foundation and Contributors (https://dotnetfoundation.org/ & https://stride3d.net)
// Distributed under the MIT license. See the LICENSE.md file in the project root for more information.

using Stride.Core.Extensions;
using Stride.Engine;
using Stride.Shaders.Compiler;

namespace Stride.Assets.Editor.EntityHierarchyEditor.Game;

public sealed class SceneViewportGame(TaskCompletionSource<bool> loaded, IEffectCompiler compiler, string logPath)
    : EntityHierarchyEditorGame(loaded, compiler, logPath)
{
    private Scene? activeContentScene;
    public Scene? ActiveContentScene => activeContentScene;
    public event Action? ContentReloaded;

    public override Entity? FindSubEntity(Guid sceneId, Guid entityId) =>
        ContentScene.Yield().BreadthFirst(scene => scene.Children)
            .SelectMany(scene => scene.Entities).BreadthFirst(entity => entity.Transform.Children.Select(child => child.Entity))
            .FirstOrDefault(entity => entity.Id == entityId);

    public void ReplaceContent(Scene scene)
    {
        activeContentScene = scene;
        ContentScene.Children.Clear();
        ContentScene.Entities.Clear();
        ContentScene.Children.Add(scene);
        ContentReloaded?.Invoke();
    }

    public void SetContentLoaded(bool isLoaded)
    {
        if (activeContentScene == null) return;
        if (isLoaded)
        {
            if (!ContentScene.Children.Contains(activeContentScene))
            {
                ContentScene.Children.Add(activeContentScene);
                ContentReloaded?.Invoke();
            }
        }
        else
        {
            if (ContentScene.Children.Contains(activeContentScene))
            {
                ContentScene.Children.Remove(activeContentScene);
                ContentReloaded?.Invoke();
            }
        }
    }
}
