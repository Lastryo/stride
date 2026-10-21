// Copyright (c) .NET Foundation and Contributors (https://dotnetfoundation.org/ & https://stride3d.net)
// Distributed under the MIT license. See the LICENSE.md file in the project root for more information.

using Stride.Assets.Scripts;
using Stride.Core.Assets;
using Stride.Core.Assets.Templates;

namespace Stride.Assets.Editor.Templates;

internal sealed class ScriptAssetTemplateGenerator : AssetTemplateGenerator
{
    public override bool IsSupportingTemplate(TemplateDescription templateDescription) =>
        templateDescription is TemplateAssetDescription description && description.GetAssetType() == typeof(ScriptSourceFileAsset);

    protected override IEnumerable<AssetItem> CreateAssets(AssetTemplateGeneratorParameters parameters)
    {
        var location = GenerateLocation(parameters);
        var text = File.ReadAllText(Path.ChangeExtension(parameters.Description.FullPath, ".cs"))
            .Replace("##Namespace##", parameters.Namespace)
            .Replace("##Scriptname##", location.GetFileNameWithoutExtension());
        yield return new AssetItem(location, new ScriptSourceFileAsset
        {
            Id = SourceCodeAsset.GenerateIdFromLocation(parameters.Package.Meta.Name ?? "Game", location),
            Text = text,
        });
    }
}
