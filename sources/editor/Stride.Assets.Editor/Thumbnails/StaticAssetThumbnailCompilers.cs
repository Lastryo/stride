// Copyright (c) .NET Foundation and Contributors (https://dotnetfoundation.org/ & https://stride3d.net)
// Distributed under the MIT license. See the LICENSE.md file in the project root for more information.

using Stride.Assets.Entities;
using Stride.Assets.Rendering;
using Stride.Core.Assets.Compiler;
using Stride.Editor.Thumbnails;

namespace Stride.Assets.Editor.Thumbnails;

[AssetCompiler(typeof(SceneAsset), typeof(ThumbnailCompilationContext))]
public sealed class SceneThumbnailCompiler() : StaticThumbnailCompiler<SceneAsset>(ThumbnailResources.Load("Scene"));

[AssetCompiler(typeof(GameSettingsAsset), typeof(ThumbnailCompilationContext))]
public sealed class GameSettingsThumbnailCompiler() : StaticThumbnailCompiler<GameSettingsAsset>(ThumbnailResources.Load("GameSettings"));

[AssetCompiler(typeof(GraphicsCompositorAsset), typeof(ThumbnailCompilationContext))]
public sealed class GraphicsCompositorThumbnailCompiler() : StaticThumbnailCompiler<GraphicsCompositorAsset>(ThumbnailResources.Load("GraphicsCompositor"));

internal static class ThumbnailResources
{
    public static byte[] Load(string name)
    {
        using var stream = typeof(ThumbnailResources).Assembly.GetManifestResourceStream($"Stride.Assets.Editor.Resources.Thumbnails.{name}Thumbnail.png")
            ?? throw new InvalidOperationException($"Missing thumbnail resource for {name}.");
        using var buffer = new MemoryStream();
        stream.CopyTo(buffer);
        return buffer.ToArray();
    }
}
