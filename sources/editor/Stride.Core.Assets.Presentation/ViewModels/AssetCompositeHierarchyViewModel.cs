// Copyright (c) .NET Foundation and Contributors (https://dotnetfoundation.org/ & https://stride3d.net) and Silicon Studio Corp. (https://www.siliconstudio.co.jp)
// Distributed under the MIT license. See the LICENSE.md file in the project root for more information.

using System.Collections.Specialized;
using Stride.Core.Assets.Quantum;
using Stride.Core.Quantum;

namespace Stride.Core.Assets.Presentation.ViewModels;

public abstract class AssetCompositeHierarchyViewModel<TAssetPartDesign, TAssetPart> : AssetCompositeViewModel<AssetCompositeHierarchy<TAssetPartDesign, TAssetPart>>
    where TAssetPartDesign : class, IAssetPartDesign<TAssetPart>
    where TAssetPart : class, IIdentifiable
{
    protected AssetCompositeHierarchyViewModel(ConstructorParameters parameters)
        : base(parameters)
    {
        Session.DeletedAssetsChanged += AssetsDeleted;
        AssetHierarchyPropertyGraph.Changed += BaseChanged;
    }

    public override void Destroy()
    {
        Session.DeletedAssetsChanged -= AssetsDeleted;
        AssetHierarchyPropertyGraph.Changed -= BaseChanged;
        base.Destroy();
    }

    private void AssetsDeleted(object? sender, NotifyCollectionChangedEventArgs? e)
    {
        if (e?.NewItems == null || UndoRedoService!.UndoRedoInProgress) return;
        foreach (var deleted in e.NewItems.OfType<AssetCompositeHierarchyViewModel<TAssetPartDesign, TAssetPart>>())
        {
            var instanceIds = AssetHierarchyPropertyGraph.GetInstanceIds(deleted.AssetHierarchyPropertyGraph).ToHashSet();
            var parts = Asset.Hierarchy.Parts.Values.Where(part => part.Base != null && instanceIds.Contains(part.Base.InstanceId)).ToArray();
            AssetHierarchyPropertyGraph.BreakBasePartLinks(parts);
        }
    }

    private void BaseChanged(object? sender, AssetMemberNodeChangeEventArgs e)
    {
        if (e.Member.Name != nameof(IAssetPartDesign<TAssetPart>.Base) || e.Member.Parent.Retrieve() is not TAssetPartDesign part) return;
        var node = (IAssetNode)Session.AssetNodeContainer.GetNode(part.Part)!;
        if (part.Base == null)
        {
            AssetHierarchyPropertyGraph.RefreshBase(node, null);
        }
        else if (Session.GetAssetById(part.Base.BasePartAsset.Id) is AssetCompositeHierarchyViewModel<TAssetPartDesign, TAssetPart> source
            && source.Asset.Hierarchy.Parts.TryGetValue(part.Base.BasePartId, out var basePart))
        {
            AssetHierarchyPropertyGraph.RefreshBase(node, (IAssetNode)Session.AssetNodeContainer.GetNode(basePart.Part)!);
        }
    }

    public AssetCompositeHierarchyPropertyGraph<TAssetPartDesign, TAssetPart> AssetHierarchyPropertyGraph => (AssetCompositeHierarchyPropertyGraph<TAssetPartDesign, TAssetPart>)PropertyGraph;

    public abstract AssetCompositeItemViewModel CreatePartViewModel(TAssetPartDesign partDesign);

    /// <summary>
    /// Gathers all base assets used in the composition of this asset, recursively.
    /// </summary>
    /// <returns></returns>
    public IEnumerable<AssetCompositeHierarchyViewModel<TAssetPartDesign, TAssetPart>> GatherAllBasePartAssets()
    {
        var baseAssets = new HashSet<AssetCompositeHierarchyViewModel<TAssetPartDesign, TAssetPart>>();
        var assetToProcess = new Stack<AssetCompositeHierarchyViewModel<TAssetPartDesign, TAssetPart>>();
        assetToProcess.Push(this);

        while (assetToProcess.Count > 0)
        {
            var asset = assetToProcess.Pop();
            if (asset != null)
            {
                foreach (var basePartAsset in asset.AssetHierarchyPropertyGraph.GetBasePartAssets())
                {
                    if (Session.GetAssetById(basePartAsset.Id) is AssetCompositeHierarchyViewModel<TAssetPartDesign, TAssetPart> viewModel && baseAssets.Add(viewModel))
                    {
                        assetToProcess.Push(viewModel);
                    }
                }
            }
        }

        return baseAssets;
    }

    /// <inheritdoc />
    protected override bool ShouldConstructPropertyItem(IObjectNode collection, NodeIndex index)
    {
        // Don't construct properties for item referencing child parts.
        if (AssetHierarchyPropertyGraph.IsChildPartReference(collection, index))
            return false;

        return base.ShouldConstructPropertyItem(collection, index);
    }

    /// <inheritdoc />
    protected override bool ShouldConstructPropertyMember(IMemberNode member)
    {
        // Don't construct properties of the Hierarchy object.
        if (member.MemberDescriptor.DeclaringType == typeof(AssetCompositeHierarchyData<TAssetPartDesign, TAssetPart>))
            return false;

        // Don't construct properties for member referencing child parts.
        if (AssetHierarchyPropertyGraph.IsChildPartReference(member, NodeIndex.Empty))
            return false;

        return base.ShouldConstructPropertyMember(member);
    }
}
