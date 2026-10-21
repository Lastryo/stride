// Copyright (c) .NET Foundation and Contributors (https://dotnetfoundation.org/ & https://stride3d.net)
// Distributed under the MIT license. See the LICENSE.md file in the project root for more information.

using Stride.Assets.Models;
using Stride.Core.Assets;
using Stride.Core.Assets.Templates;
using Stride.Core.IO;

namespace Stride.Assets.Editor.Templates;

internal sealed class SourceAssetTemplateGenerator : AssetFactoryTemplateGenerator
{
    public override bool IsSupportingTemplate(TemplateDescription templateDescription) =>
        templateDescription is TemplateAssetFactoryDescription { ImportSource: true };

    protected override IEnumerable<AssetItem> CreateAssets(AssetTemplateGeneratorParameters parameters)
    {
        if (parameters.SourceFiles.Count == 0) return base.CreateAssets(parameters);
        var description = (TemplateAssetFactoryDescription)parameters.Description;
        var type = description.GetAssetType();
        var result = new List<AssetItem>();
        foreach (var file in parameters.SourceFiles)
        {
            if (type == typeof(ModelAsset) || type == typeof(SkeletonAsset) || type == typeof(AnimationAsset))
            {
                var importer = AssetRegistry.FindImporterForFile(file).OfType<ModelAssetImporter>().FirstOrDefault()
                    ?? throw new InvalidOperationException($"No model importer supports '{file}'.");
                var importParameters = new AssetImporterParameters(type) { Logger = parameters.Logger };
                if (type == typeof(ModelAsset))
                {
                    importParameters.SelectedOutputTypes[typeof(Stride.Assets.Materials.MaterialAsset)] = true;
                    importParameters.SelectedOutputTypes[typeof(Stride.Assets.Textures.TextureAsset)] = true;
                    importParameters.SelectedOutputTypes[typeof(SkeletonAsset)] = true;
                }
                result.AddRange(importer.Import(file, importParameters)
                    .Select(item => new AssetItem(UPath.Combine(parameters.TargetLocation, item.Location), item.Asset)));
            }
            else
            {
                parameters.Name = file.GetFileNameWithoutExtension() ?? parameters.Name;
                var asset = description.GetFactory()!.New();
                if (asset is not IAssetWithSource sourced)
                    throw new InvalidOperationException($"'{description.Name}' does not support a source file.");
                sourced.Source = file;
                result.Add(new AssetItem(GenerateLocation(parameters), asset));
            }
        }
        return result;
    }
}
