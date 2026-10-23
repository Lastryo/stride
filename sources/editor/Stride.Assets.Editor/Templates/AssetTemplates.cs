// Copyright (c) .NET Foundation and Contributors (https://dotnetfoundation.org/ & https://stride3d.net)
// Distributed under the MIT license. See the LICENSE.md file in the project root for more information.

using Stride.Core.Assets;
using Stride.Core.Assets.Templates;
using Stride.Core.Diagnostics;
using Stride.Core.Yaml;

namespace Stride.Assets.Editor.Templates;

internal static class AssetTemplates
{
    private static bool initialized;

    public static void Initialize(ILogger logger)
    {
        if (initialized) return;
        initialized = true;
        var directory = Path.Combine(AppContext.BaseDirectory, "Templates", "Assets");
        if (!Directory.Exists(directory))
        {
            logger.Warning($"Asset template directory '{directory}' was not found.");
            return;
        }
        var package = new Package { FullPath = Path.Combine(directory, "EditorTemplates.sdpkg") };
        foreach (var path in Directory.EnumerateFiles(directory, "*.sdtpl", SearchOption.AllDirectories))
        {
            try
            {
                var template = YamlSerializer.Load<TemplateDescription>(path);
                template.FullPath = path;
                package.Templates.Add(template);
            }
            catch (Exception e) { logger.Error($"Unable to load asset template '{path}'.", e); }
        }
        TemplateManager.RegisterPackage(package);
        TemplateManager.Register(AssetFactoryTemplateGenerator.Default);
        TemplateManager.Register(new SourceAssetTemplateGenerator());
        TemplateManager.Register(new ScriptAssetTemplateGenerator());
        TemplateManager.Register(new VisualScriptAssetTemplateGenerator());
        TemplateManager.Register(GraphicsCompositorTemplateGenerator.Default);
    }
}
