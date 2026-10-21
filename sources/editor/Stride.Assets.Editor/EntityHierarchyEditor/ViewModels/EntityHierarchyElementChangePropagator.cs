// Copyright (c) .NET Foundation and Contributors (https://dotnetfoundation.org/ & https://stride3d.net) and Silicon Studio Corp. (https://www.siliconstudio.co.jp)
// Distributed under the MIT license. See the LICENSE.md file in the project root for more information.

using System.Threading.Tasks;
using Stride.Assets.Entities;
using Stride.Assets.Editor.GameEditor.Services;
using Stride.Assets.Editor.GameEditor.ViewModels;
using Stride.Assets.Presentation.Quantum;
using Stride.Assets.Presentation.ViewModels;
using Stride.Core;
using Stride.Core.Annotations;
using Stride.Core.Assets.Quantum;
using Stride.Core.Presentation.Services;
using Stride.Core.Quantum;
using Stride.Engine;

namespace Stride.Assets.Editor.EntityHierarchyEditor.ViewModels;

public class EntityHierarchyElementChangePropagator : GameEditorChangePropagator<EntityDesign, Entity, EntityViewModel>
{
    public EntityHierarchyElementChangePropagator(
        [NotNull] IEditorGameController controller,
        [NotNull] AssetNodeContainer nodeContainer,
        [NotNull] IDispatcherService dispatcher,
        [NotNull] EntityViewModel owner,
        [NotNull] Entity assetSidePart)
        : base(controller, nodeContainer, dispatcher, owner, assetSidePart)
    {
    }

    protected override async Task<bool> PropagatePartReference(IGraphNode gameSideNode, object? value, INodeChangeEventArgs e)
    {
        var component = value as EntityComponent;
        var nodeIndex = (e as ItemChangeEventArgs)?.Index ?? NodeIndex.Empty;
        if (component != null && e.Node.Type != typeof(EntityComponentCollection) && !((EntityHierarchyPropertyGraph)Owner.Asset.PropertyGraph!).IsChildPartReference(e.Node, nodeIndex))
        {
            var index = component.Entity.Components.IndexOf(component);
            var partId = new AbsoluteId(Owner.Id.AssetId, component.Entity.Id);
            await controller.InvokeAsync(() =>
            {
                var gameSideEntity = (Entity?)controller.FindGameSidePart(partId);
                var gameSideComponent = gameSideEntity?.Components[index];
                UpdateGameSideContent(gameSideNode, gameSideComponent, e.ChangeType, nodeIndex);
            });
            return true;
        }
        return await base.PropagatePartReference(gameSideNode, value, e);
    }

    protected override object? CloneObjectForGameSide(object? assetSideObject, IAssetNode assetNode, IGraphNode gameSideNode)
    {
        if (gameSideNode.Type == typeof(EntityComponentCollection) && assetSideObject is TransformComponent)
        {
            var transformComponent = (TransformComponent)gameSideNode.Retrieve(new NodeIndex(0));
            transformComponent.Entity = null;
            return transformComponent;
        }

        var gameSideValue = base.CloneObjectForGameSide(assetSideObject, assetNode, gameSideNode);

        if (gameSideValue is EntityComponent entityComponent)
        {
            entityComponent.Entity = null;
        }

        return gameSideValue;
    }
}

