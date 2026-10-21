// Copyright (c) .NET Foundation and Contributors (https://dotnetfoundation.org/ & https://stride3d.net) and Silicon Studio Corp. (https://www.siliconstudio.co.jp)
// Distributed under the MIT license. See the LICENSE.md file in the project root for more information.

using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Threading.Tasks.Dataflow;
using Stride.Assets.Editor.GameEditor.Services;
using Stride.Core;
using Stride.Core.Annotations;
using Stride.Core.Assets;
using Stride.Core.Assets.Presentation.ViewModels;
using Stride.Core.Assets.Quantum;
using Stride.Core.Assets.Quantum.Visitors;
using Stride.Core.Extensions;
using Stride.Core.Presentation.Services;
using Stride.Core.Quantum;
using Stride.Core.Reflection;
using Stride.Core.Serialization;

namespace Stride.Assets.Editor.GameEditor.ViewModels;

public class GameEditorChangePropagator<TAssetPartDesign, TAssetPart, TItemViewModel>
    where TAssetPart : class, IIdentifiable
    where TItemViewModel : AssetCompositeItemViewModel, IAssetPartViewModel
    where TAssetPartDesign : class, IAssetPartDesign<TAssetPart>
{
    private const string GameSideContentKey = "Game";
    protected readonly IEditorGameController controller;
    protected readonly AssetNodeContainer nodeContainer;
    protected readonly IDispatcherService dispatcher;
    private readonly GraphNodeLinker gameObjectLinker;
    private readonly GraphNodeChangeListener listener;
    private readonly BufferBlock<INodeChangeEventArgs?> propagatorQueue = new();
    private readonly TaskCompletionSource<int> gameSideReady = new();
    private readonly TaskCompletionSource<int> initialized = new();
    private bool running = true;

    public GameEditorChangePropagator(
        [NotNull] IEditorGameController controller,
        [NotNull] AssetNodeContainer nodeContainer,
        [NotNull] IDispatcherService dispatcher,
        [NotNull] TItemViewModel owner,
        [NotNull] TAssetPart assetSidePart)
    {
        this.controller = controller ?? throw new ArgumentNullException(nameof(controller));
        this.nodeContainer = nodeContainer ?? throw new ArgumentNullException(nameof(nodeContainer));
        this.dispatcher = dispatcher ?? throw new ArgumentNullException(nameof(dispatcher));
        Owner = owner ?? throw new ArgumentNullException(nameof(owner));

        var propertyGraph = (AssetCompositeHierarchyPropertyGraph<TAssetPartDesign, TAssetPart>)owner.Asset.PropertyGraph!;
        var rootNode = (IAssetNode)nodeContainer.GetNode(assetSidePart);
        listener = new AssetGraphNodeChangeListener(rootNode, propertyGraph.Definition);
        listener.Initialize();
        listener.ValueChanged += AssetSideNodeChanged;
        listener.ItemChanged += AssetSideNodeChanged;
        gameObjectLinker = new AssetGraphNodeLinker(propertyGraph.Definition) { LinkAction = LinkNode };
        Task.Run(() => PropagatorTask(assetSidePart));
    }

    public Task Initialized => initialized.Task;

    [NotNull]
    protected TItemViewModel Owner { get; }

    public void Destroy()
    {
        listener.ValueChanged -= AssetSideNodeChanged;
        listener.ItemChanged -= AssetSideNodeChanged;

        running = false;
        propagatorQueue.Post(null);
    }

    public void NotifyGameSidePartAdded()
    {
        gameSideReady.TrySetResult(0);
    }

    private async Task Initialize(TAssetPart assetSideInstance)
    {
        var partId = new AbsoluteId(Owner.Id.AssetId, assetSideInstance.Id);
        var gameSideInstance = await controller.InvokeAsync(() => controller.FindGameSidePart(partId));
        if (gameSideInstance == null)
            throw new InvalidOperationException($"Unable to reach the game-side instance for {assetSideInstance.Id}");

        using (await DispatcherLock.Lock(true, controller, dispatcher))
        {
            controller.Logger.Debug($"Initial linking for part {assetSideInstance.Id}");
            var assetNode = (IAssetNode)nodeContainer.GetNode(assetSideInstance);
            var gameNode = controller.GameSideNodeContainer.GetOrCreateNode(gameSideInstance);
            gameObjectLinker.LinkGraph(assetNode, gameNode);
        }
    }

    private static void LinkNode(IGraphNode assetNode, IGraphNode gameNode)
    {
        var node = (IAssetNode)assetNode;
        node.SetContent(GameSideContentKey, gameNode);
    }

    private void AssetSideNodeChanged(object? sender, INodeChangeEventArgs e)
    {
        propagatorQueue.Post(e);
    }

    protected virtual async Task<bool> PropagatePartReference(IGraphNode gameSideNode, object? value, INodeChangeEventArgs e)
    {
        var index = (e as ItemChangeEventArgs)?.Index ?? NodeIndex.Empty;
        var propertyGraph = (AssetCompositeHierarchyPropertyGraph<TAssetPartDesign, TAssetPart>)Owner.Asset.PropertyGraph!;
        if (propertyGraph.IsChildPartReference(e.Node, index))
        {
            return true;
        }

        if (value is TAssetPart assetPart)
        {
            var id = new AbsoluteId(Owner.Id.AssetId, assetPart.Id);
            await controller.InvokeAsync(() =>
            {
                var instance = controller.FindGameSidePart(id);
                UpdateGameSideContent(gameSideNode, instance, e.ChangeType, index);
            });
            return true;
        }
        return false;
    }

    protected virtual object? CloneObjectForGameSide(object? assetSideObject, IAssetNode assetNode, IGraphNode gameSideNode)
    {
        if (assetSideObject == null)
            return null;
        return AssetCloner.Clone(assetSideObject, AssetClonerFlags.ReferenceAsNull);
    }

    private async Task PropagatorTask(TAssetPart assetSidePart)
    {
        await gameSideReady.Task;

        await Initialize(assetSidePart);
        var propertyGraph = (AssetCompositeHierarchyPropertyGraph<TAssetPartDesign, TAssetPart>)Owner.Asset.PropertyGraph!;
        var contentReferenceCollector = new ContentReferenceCollector(propertyGraph.Definition);
        await controller.InvokeTask(() =>
        {
            controller.Logger.Debug($"Registering reference to: {Owner.Id}");
            return controller.Loader.Manager.RegisterReferencer(Owner.Id);
        });
        var rootNode = (IAssetNode)nodeContainer.GetOrCreateNode(assetSidePart);
        contentReferenceCollector.Visit(rootNode);

        await controller.InvokeTask(async () =>
        {
            using (await controller.Loader.LockDatabaseAsynchronously())
            {
                foreach (var contentReference in contentReferenceCollector.ContentReferences)
                {
                    await controller.Loader.Manager.PushContentReference(Owner.Id, contentReference.ContentId, contentReference.Node, contentReference.Index);
                }
            }
        });

        initialized.TrySetResult(0);

        while (running)
        {
            var e = await propagatorQueue.ReceiveAsync();
            if (e == null)
                continue;

            var assetNode = (IAssetNode)e.Node;
            var gameSideNode = assetNode.GetContent(GameSideContentKey);
            if (gameSideNode == null)
                continue;

            var index = (e as ItemChangeEventArgs)?.Index ?? NodeIndex.Empty;
            var innerType = e.Node.Descriptor.GetInnerCollectionType();
            if (!AssetRegistry.CanBeAssignedToContentTypes(innerType, checkIsUrlType: false))
            {
                if (nodeContainer.NodeBuilder.PrimitiveTypeFilter.IsPrimitiveType(innerType))
                {
                    await controller.InvokeAsync(() => UpdateGameSideContent(gameSideNode, e.NewValue, e.ChangeType, index));
                }
                else
                {
                    var value = RetrieveValue(assetNode, e.NewValue, e.ChangeType, index);
                    if (value == null && assetNode.Descriptor.Category == DescriptorCategory.Set)
                    {
                        value = e.NewValue;
                    }
                    var isReference = await PropagatePartReference(gameSideNode, value, e);
                    if (!isReference)
                    {
                        if (e.OldValue != null)
                        {
                            var oldNode = nodeContainer.GetNode(e.OldValue);
                            var visitor = new AssetGraphVisitorBase(propertyGraph.Definition);
                            var removedNodes = new HashSet<IGraphNode>();
                            visitor.Visiting += (node, _) =>
                            {
                                var gNode = ((IAssetNode)node).GetContent(GameSideContentKey);
                                if (gNode != null)
                                    removedNodes.Add(gNode);
                            };

                            if (oldNode != null)
                            {
                                visitor.Visit(oldNode);
                            }

                            await controller.InvokeTask(async () =>
                            {
                                await controller.Loader.Manager.ClearContentReferencesFromNodes(Owner.Id, removedNodes);
                            });
                        }

                        await controller.InvokeAsync(() =>
                        {
                            var gameSideValue = CloneObjectForGameSide(value, assetNode, gameSideNode);
                            UpdateGameSideContent(gameSideNode, gameSideValue, e.ChangeType, index);
                        });
                    }
                }
            }
            else
            {
                await UpdateGameSideReference(controller, gameSideNode, e.ChangeType, e.OldValue, e.NewValue, index);
            }

            using (await DispatcherLock.Lock(true, controller, dispatcher))
            {
                gameObjectLinker.LinkGraph(assetNode, gameSideNode);
                contentReferenceCollector.Reset();
                if (e.ChangeType != ContentChangeType.CollectionRemove)
                {
                    contentReferenceCollector.Visit(assetNode, index);
                }
            }

            await controller.InvokeTask(async () =>
            {
                using (await controller.Loader.LockDatabaseAsynchronously())
                {
                    foreach (var contentReference in contentReferenceCollector.ContentReferences)
                    {
                        await controller.Loader.Manager.PushContentReference(Owner.Id, contentReference.ContentId, contentReference.Node, contentReference.Index);
                    }
                }

                controller.TriggerActiveRenderStageReevaluation();
            });
        }

        await controller.InvokeTask(() =>
        {
            controller.Logger.Debug($"Unregistering reference to: {Owner.Id}");
            return controller.Loader.Manager.RemoveReferencer(Owner.Id);
        });
    }

    private static object? RetrieveValue(IGraphNode node, object? value, ContentChangeType changeType, NodeIndex index)
    {
        return changeType switch
        {
            ContentChangeType.ValueChange => node.Retrieve(),
            ContentChangeType.CollectionUpdate => node.Retrieve(index),
            ContentChangeType.CollectionAdd or ContentChangeType.CollectionRemove => value,
            _ => throw new ArgumentOutOfRangeException(nameof(changeType)),
        };
    }

    protected static void UpdateGameSideContent(IGraphNode gameSideNode, object? value, ContentChangeType changeType, NodeIndex index)
    {
        switch (changeType)
        {
            case ContentChangeType.ValueChange:
                ((IMemberNode)gameSideNode).Update(value);
                break;
            case ContentChangeType.CollectionUpdate:
                ((IObjectNode)gameSideNode).Update(value, index);
                break;
            case ContentChangeType.CollectionAdd:
                ((IObjectNode)gameSideNode).Add(value, index);
                break;
            case ContentChangeType.CollectionRemove:
                var item = gameSideNode.Retrieve(index);
                ((IObjectNode)gameSideNode).Remove(item, index);
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(changeType));
        }
    }

    private async Task UpdateGameSideReference(IEditorGameController targetController, IGraphNode gameSideNode, ContentChangeType changeType, object? oldValue, object? newValue, NodeIndex index)
    {
        if (!AssetRegistry.CanBeAssignedToContentTypes(gameSideNode.Descriptor.GetInnerCollectionType(), checkIsUrlType: false))
            return;

        AttachedReference? reference = null;
        if (oldValue != null)
        {
            reference = AttachedReferenceManager.GetAttachedReference(oldValue);
        }

        await targetController.InvokeTask(() =>
        {
            switch (changeType)
            {
                case ContentChangeType.ValueChange:
                    ((IMemberNode)gameSideNode).Update(null);
                    break;
                case ContentChangeType.CollectionUpdate:
                    ((IObjectNode)gameSideNode).Update(null, index);
                    break;
                case ContentChangeType.CollectionAdd:
                    ((IObjectNode)gameSideNode).Add(null, index);
                    break;
                case ContentChangeType.CollectionRemove:
                    var oldValueGameSide = gameSideNode.Retrieve(index);
                    ((IObjectNode)gameSideNode).Remove(oldValueGameSide, index);
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(changeType), changeType, null);
            }

            if (oldValue == newValue)
                return Task.CompletedTask;

            if (reference != null)
            {
                return targetController.Loader.Manager.ClearContentReference(Owner.Id, reference.Id, gameSideNode, index);
            }
            return Task.CompletedTask;
        });
    }
}

