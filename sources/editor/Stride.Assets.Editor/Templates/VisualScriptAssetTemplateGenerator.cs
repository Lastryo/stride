// Copyright (c) .NET Foundation and Contributors (https://dotnetfoundation.org/ & https://stride3d.net)
// Distributed under the MIT license. See the LICENSE.md file in the project root for more information.
using Stride.Assets.Scripts;
using Stride.Core.Assets;
using Stride.Core.Assets.Templates;

namespace Stride.Assets.Editor.Templates;

internal sealed class VisualScriptAssetTemplateGenerator : AssetTemplateGenerator
{
    public override bool IsSupportingTemplate(TemplateDescription description) => description is TemplateAssetDescription asset && asset.GetAssetType() == typeof(VisualScriptAsset);
    protected override IEnumerable<AssetItem> CreateAssets(AssetTemplateGeneratorParameters parameters)
    {
        var asset = new VisualScriptAsset { BaseType = "Stride.Engine.SyncScript", Namespace = parameters.Namespace };
        var update = new Method("Update") { VirtualModifier = VirtualModifier.Override };
        var start = new FunctionStartBlock(); start.GenerateSlots(start.Slots, new SlotGeneratorContext());
        update.Blocks.Add(start); asset.Methods.Add(update);
        yield return new AssetItem(GenerateLocation(parameters), asset);
    }
}
