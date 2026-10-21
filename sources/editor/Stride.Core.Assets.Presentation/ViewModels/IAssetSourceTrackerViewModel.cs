// Copyright (c) .NET Foundation and Contributors (https://dotnetfoundation.org/ & https://stride3d.net) and Silicon Studio Corp. (https://www.siliconstudio.co.jp)
// Distributed under the MIT license. See the LICENSE.md file in the project root for more information.

using Stride.Core.IO;
using Stride.Core.Storage;

namespace Stride.Core.Assets.Presentation.ViewModels;

public interface IAssetSourceTrackerViewModel
{
    Stride.Core.Presentation.Commands.ICommandBase UpdateAllAssetsWithModifiedSourceCommand { get; }

    Stride.Core.Presentation.Collections.IReadOnlyObservableCollection<AssetViewModel> AssetsToUpdate { get; }

    ObjectId GetCurrentHash(UFile file);

    void UpdateAssetStatus(AssetViewModel asset);
}
