// Copyright (c) .NET Foundation and Contributors (https://dotnetfoundation.org/ & https://stride3d.net) and Silicon Studio Corp. (https://www.siliconstudio.co.jp)
// Distributed under the MIT license. See the LICENSE.md file in the project root for more information.

using System.Collections.Generic;
using Stride.Assets.Editor.UIEditor.ViewModels;
using Stride.Assets.Editor.ViewModels;
using Stride.Assets.Presentation.ViewModels;
using Stride.Assets.UI;
using Stride.Core.Assets.Presentation.ViewModels;
using Stride.Core.Diagnostics;
using Stride.UI;

namespace Stride.Assets.Editor.UIEditor.Services;

/// <summary>
/// Game controller for the UI page editor.
/// </summary>
public sealed class UIPageEditorController : UIEditorController
{
    public UIPageEditorController(AssetViewModel asset, UIViewportViewModel viewport)
        : base(asset, viewport)
    {
    }

    protected override bool ConstructRootElements(out ICollection<UIElement> rootElements, out UIAssetBase.UIDesign? editorSettings)
    {
        var uiAsset = (UIAssetBase)Asset.Asset;
        if (uiAsset.Hierarchy.RootParts.Count > 1)
        {
            Editor.Logger.Error(UIPageRootViewModel.OneRootOnly);
            rootElements = [];
            editorSettings = null;
            return false;
        }

        return base.ConstructRootElements(out rootElements, out editorSettings);
    }
}

