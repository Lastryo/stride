// Copyright (c) .NET Foundation and Contributors (https://dotnetfoundation.org/ & https://stride3d.net) and Silicon Studio Corp. (https://www.siliconstudio.co.jp)
// Distributed under the MIT license. See the LICENSE.md file in the project root for more information.

using Stride.Core.Assets;
using Stride.Core.Extensions;

namespace Stride.Assets.Presentation.ViewModels;

public sealed class UIPageRootViewModel : UIRootViewModel
{
    public const string OneRootOnly = "A UI page can only have one root element.";

    public UIPageRootViewModel(UIPageViewModel asset)
        : base(asset, asset.Asset.Hierarchy.EnumerateRootPartDesigns().SingleOrDefault() is { } root ? [root] : [])
    {
    }

    public UIElementViewModel? RootElement => Children.Count > 0 ? (UIElementViewModel)Children[0] : null;

    public override string? Name { get => "UIPageRoot"; set => throw new NotSupportedException($"Cannot change the name of a {nameof(UIPageRootViewModel)} object."); }
}
