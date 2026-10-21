// Copyright (c) .NET Foundation and Contributors (https://dotnetfoundation.org/ & https://stride3d.net) and Silicon Studio Corp. (https://www.siliconstudio.co.jp)
// Distributed under the MIT license. See the LICENSE.md file in the project root for more information.

using System.Collections.Generic;
using Stride.Core;
using Stride.Core.Annotations;
using Stride.Core.Assets;
using Stride.Core.Assets.Quantum;
using Stride.Core.Assets.Quantum.Visitors;
using Stride.Core.Extensions;
using Stride.Core.Quantum;
using Stride.Core.Serialization;

namespace Stride.Assets.Editor.GameEditor.ViewModels;

public class ContentReferenceCollector : AssetGraphVisitorBase
{
    public struct ContentReferenceAccessor
    {
        public readonly AssetId ContentId;
        public readonly IGraphNode Node;
        public readonly NodeIndex Index;

        public ContentReferenceAccessor(AssetId contentId, IGraphNode node, NodeIndex index)
        {
            ContentId = contentId;
            Node = node;
            Index = index;
        }
    }

    public ContentReferenceCollector([NotNull] AssetPropertyGraphDefinition propertyGraphDefinition)
        : base(propertyGraphDefinition)
    {
    }

    public bool Registering { get; set; } = true;

    public List<ContentReferenceAccessor> ContentReferences { get; } = [];

    public void Reset()
    {
        ContentReferences.Clear();
    }

    public void Visit([NotNull] IAssetNode node, NodeIndex index)
    {
        if (index == NodeIndex.Empty)
        {
            Visit(node);
        }
        else if (node.IsReference)
        {
            var target = ((IObjectNode)node).IndexedTarget(index);
            if (target != null)
            {
                Visit(target);
            }
        }
        else if (AssetRegistry.CanBeAssignedToContentTypes(node.Descriptor.GetInnerCollectionType(), checkIsUrlType: false))
        {
            var gameContent = node.GetContent("Game");
            if (gameContent != null)
            {
                var id = AttachedReferenceManager.GetAttachedReference(node.Retrieve(index))?.Id ?? AssetId.Empty;
                CollectContentReference(id, gameContent, index);
            }
        }
    }

    protected override void VisitNode(IGraphNode node)
    {
        var assetNode = (IAssetNode)node;
        var gameContent = assetNode.GetContent("Game");
        if (gameContent != null)
        {
            if (node is IMemberNode memberContent)
            {
                if (AssetRegistry.CanBeAssignedToContentTypes(memberContent.Type, checkIsUrlType: false))
                {
                    var id = AttachedReferenceManager.GetAttachedReference(memberContent.Retrieve())?.Id ?? AssetId.Empty;
                    CollectContentReference(id, gameContent, NodeIndex.Empty);
                }
            }

            if (node is IObjectNode objectNode && objectNode.Indices != null)
            {
                if (AssetRegistry.CanBeAssignedToContentTypes(objectNode.Descriptor.GetInnerCollectionType(), checkIsUrlType: false))
                {
                    foreach (var index in objectNode.Indices)
                    {
                        var id = AttachedReferenceManager.GetAttachedReference(objectNode.Retrieve(index))?.Id ?? AssetId.Empty;
                        CollectContentReference(id, gameContent, index);
                    }
                }
            }
        }
        base.VisitNode(node);
    }

    protected virtual void CollectContentReference(AssetId contentId, IGraphNode content, NodeIndex index)
    {
        if (contentId == AssetId.Empty)
            return;

        ContentReferences.Add(new ContentReferenceAccessor(contentId, content, index));
    }
}

