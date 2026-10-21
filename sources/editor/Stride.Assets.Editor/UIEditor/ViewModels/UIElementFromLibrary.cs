// Copyright (c) .NET Foundation and Contributors (https://dotnetfoundation.org/ & https://stride3d.net) and Silicon Studio Corp. (https://www.siliconstudio.co.jp)
// Distributed under the MIT license. See the LICENSE.md file in the project root for more information.

using System;
using Stride.Assets.Presentation.ViewModels;
using Stride.Assets.UI;
using Stride.Core.Assets;
using Stride.Core.Presentation.ViewModels;
using Stride.UI;

namespace Stride.Assets.Editor.UIEditor.ViewModels;

public sealed class UIElementFromLibrary : ViewModelBase, IUIElementFactory
{
    private readonly UILibraryViewModel library;
    public UIElementFromLibrary(IViewModelServiceProvider serviceProvider, UILibraryViewModel library, Guid id)
        : base(serviceProvider)
    {
        this.library = library;
        Id = id;
    }

    /// <summary>The library supplying this palette entry.</summary>
    public UILibraryViewModel Library => library;

    public Guid Id { get; }

    public string Name => library.Asset.PublicUIElements.TryGetValue(Id, out var name) ? name : "(undefined)";

    public Type? Type => library.Asset.Hierarchy.Parts.TryGetValue(Id, out var part) ? part.UIElement.GetType() : null;

    public string Category => library.Url;

    /// <summary>Checks that the export still exists and does not create circular inheritance.</summary>
    /// <param name="targetAsset">The asset that would receive the library instance.</param>
    /// <returns>Whether this export can be instantiated in the target asset.</returns>
    public bool CanCreateIn(UIAssetBase targetAsset) => !library.IsDeleted && library.Id != targetAsset.Id
        && library.Asset.PublicUIElements.ContainsKey(Id) && library.Asset.Hierarchy.RootParts.Any(root => root.Id == Id)
        && !library.GatherAllBasePartAssets().Any(asset => asset.Id == targetAsset.Id);

    public AssetCompositeHierarchyData<UIElementDesign, UIElement> Create(UIAssetBase targetAsset)
    {
        if (!CanCreateIn(targetAsset)) throw new InvalidOperationException("A UI library instance cannot create circular inheritance.");
        if (!library.Asset.Hierarchy.Parts.ContainsKey(Id))
            throw new InvalidOperationException("The corresponding UI Element could not be found in the library");

        var asset = (UILibraryAsset)library.Asset;
        return asset.CreateElementInstance(targetAsset, library.Url, Id);
    }
}

