// Copyright (c) .NET Foundation and Contributors (https://dotnetfoundation.org/ & https://stride3d.net)
// Distributed under the MIT license. See the LICENSE.md file in the project root for more information.

using Stride.Assets.Editor.UIEditor.ViewModels;
using Stride.Core.Assets.Presentation.ViewModels;

namespace Stride.Assets.Editor.UIEditor.Services;

/// <summary>Game controller for a UI library, displaying its active root.</summary>
public sealed class UILibraryEditorController : UIEditorController
{
    /// <summary>Creates the controller for a library viewport.</summary>
    /// <param name="asset">The UI library asset.</param>
    /// <param name="viewport">The viewport displaying the library.</param>
    public UILibraryEditorController(AssetViewModel asset, UIViewportViewModel viewport) : base(asset, viewport)
    {
    }
}
