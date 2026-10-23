// Copyright (c) .NET Foundation and Contributors (https://dotnetfoundation.org/ & https://stride3d.net)
// Distributed under the MIT license. See the LICENSE.md file in the project root for more information.
using Stride.Assets.Scripts;
using Stride.Core.Assets.Quantum;
using Stride.Core.Quantum;

namespace Stride.Assets.Presentation.Quantum;

[AssetPropertyGraphDefinition(typeof(VisualScriptAsset))]
public sealed class VisualScriptAssetPropertyGraphDefinition : AssetPropertyGraphDefinition
{
    public override bool IsMemberTargetObjectReference(IMemberNode member, object? value) =>
        value is Slot && member.Parent.Retrieve() is Link || base.IsMemberTargetObjectReference(member, value);
}
