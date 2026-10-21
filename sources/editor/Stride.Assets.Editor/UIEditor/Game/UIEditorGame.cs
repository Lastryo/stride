// Copyright (c) .NET Foundation and Contributors (https://dotnetfoundation.org/ & https://stride3d.net)
// Distributed under the MIT license. See the LICENSE.md file in the project root for more information.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Stride.Assets.Editor.EntityHierarchyEditor.Game;
using Stride.Engine;
using Stride.Shaders.Compiler;

namespace Stride.Assets.Editor.UIEditor.Game;

public sealed class UIEditorGame : EntityHierarchyEditorGame
{
    public UIEditorGame(TaskCompletionSource<bool> gameContentLoadedTaskSource, IEffectCompiler effectCompiler, string effectLogPath)
        : base(gameContentLoadedTaskSource, effectCompiler, effectLogPath)
    {
    }

    public override Entity? FindSubEntity(Guid sceneId, Guid entityId)
    {
        EnsureContentScene();
        return ContentScene.Entities.FirstOrDefault(e => e.Id == entityId);
    }

    public void LoadEntities(IEnumerable<Entity> entities)
    {
        ArgumentNullException.ThrowIfNull(entities);
        EnsureContentScene();
        ContentScene.Entities.AddRange(entities);
    }

    public void LoadEntity(Entity entity)
    {
        ArgumentNullException.ThrowIfNull(entity);
        EnsureContentScene();
        ContentScene.Entities.Add(entity);
    }

    public void UnloadEntities(IEnumerable<Entity> entities)
    {
        ArgumentNullException.ThrowIfNull(entities);
        EnsureContentScene();
        foreach (var entity in entities)
        {
            ContentScene.Entities.Remove(entity);
        }
    }
}

