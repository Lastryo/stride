// Copyright (c) .NET Foundation and Contributors (https://dotnetfoundation.org/ & https://stride3d.net) and Silicon Studio Corp. (https://www.siliconstudio.co.jp)
// Distributed under the MIT license. See the LICENSE.md file in the project root for more information.

using Stride.Assets.Models;
using Stride.Core.Assets.Presentation.Annotations;
using Stride.Core.Assets.Presentation.ViewModels;
using Stride.Core.Assets;

namespace Stride.Assets.Presentation.ViewModels;

/// <summary>
/// View model for <see cref="AnimationAsset"/>.
/// </summary>
[AssetViewModel<AnimationAsset>]
public sealed class AnimationViewModel : ImportedAssetViewModel<AnimationAsset>
{
    public AnimationViewModel(ConstructorParameters parameters)
        : base(parameters)
    {
    }

    protected override IAssetImporter? GetImporter()
    {
        return AssetRegistry.FindImporterForFile(Asset.Source).OfType<ModelAssetImporter>().FirstOrDefault();
    }

    protected override void UpdateAssetFromSource(AnimationAsset assetToMerge)
    {
        AssetRootNode![nameof(AnimationAsset.AnimationTimeMaximum)].Update(assetToMerge.AnimationTimeMaximum);
        AssetRootNode![nameof(AnimationAsset.AnimationTimeMinimum)].Update(assetToMerge.AnimationTimeMinimum);
    }
}
