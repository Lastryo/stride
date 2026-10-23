// Copyright (c) .NET Foundation and Contributors (https://dotnetfoundation.org/ & https://stride3d.net)
// Distributed under the MIT license. See the LICENSE.md file in the project root for more information.

using Stride.Core.Assets.Editor.ViewModels;
using Stride.Core.Assets.Presentation.ViewModels;
using Stride.Core.Presentation.Services;

namespace Stride.Core.Assets.Editor.Services;

public interface IEditorDialogService : IDialogService
{
    /// <summary>Returns the validated selection, or an empty list on cancellation.</summary>
    Task<IReadOnlyList<AssetViewModel>> ShowAssetPickerAsync(AssetPickerViewModel picker);

    void ShowProgressWindow(WorkProgressViewModel workProgress);
}
