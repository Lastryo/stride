// Copyright (c) .NET Foundation and Contributors (https://dotnetfoundation.org/ & https://stride3d.net) and Silicon Studio Corp. (https://www.siliconstudio.co.jp)
// Distributed under the MIT license. See the LICENSE.md file in the project root for more information.

using Stride.Assets.UI;
using Stride.Core.Assets.Presentation.ViewModels;

using Stride.Core;

namespace Stride.Assets.Presentation.ViewModels;

public abstract class UIHierarchyItemViewModel : AssetCompositeItemViewModel<UIBaseViewModel, UIHierarchyItemViewModel>, IAssetPartViewModel
{
    protected UIHierarchyItemViewModel(UIBaseViewModel asset, IEnumerable<UIElementDesign> childElements)
        : base(asset)
    {
        AddItems(childElements.Select(asset.CreatePartViewModel));
    }

    public abstract AbsoluteId Id { get; }

    protected UIAssetBase UIAsset => (UIAssetBase)Asset.Asset;

    public void InsertChild(int index, UIHierarchyItemViewModel child)
    {
        if (index < 0 || index > Children.Count)
            AddItem(child);
        else
            InsertItem(index, child);
    }

    public bool RemoveChild(UIHierarchyItemViewModel child)
    {
        return RemoveItem(child);
    }
}
