// Copyright (c) .NET Foundation and Contributors (https://dotnetfoundation.org/ & https://stride3d.net)
// Distributed under the MIT license. See the LICENSE.md file in the project root for more information.

using Avalonia.Media.Imaging;
using Stride.Core.IO;
using Stride.Core.Mathematics;
using Stride.Graphics;
using Stride.TextureConverter;

namespace Stride.Assets.Editor.Avalonia.SpriteEditor;

/// <summary>Owns both the display bitmap and the decoded texture used by the pixel tools.</summary>
public sealed class SpriteEditorImageCache : IDisposable
{
    private readonly List<Entry> entries = [];
    private sealed record Entry(UFile Path, DateTime Modified, TexImage Texture, Bitmap Bitmap) : IDisposable
    {
        public void Dispose() { Bitmap.Dispose(); Texture.Dispose(); }
    }

    public Bitmap? RetrieveImage(UFile? path)
    {
        if (path == null || !File.Exists(path)) return null;
        var modified = File.GetLastWriteTimeUtc(path);
        var existing = entries.FirstOrDefault(e => e.Path == path);
        if (existing != null)
        {
            if (existing.Modified == modified) return existing.Bitmap;
            entries.Remove(existing);
            existing.Dispose();
        }
        TexImage? texture = null;
        try
        {
            using var tool = new TextureTool();
            texture = tool.Load(path, false);
            tool.Decompress(texture, texture.Format.IsSRgb);
            if (texture.Format == PixelFormat.R16G16B16A16_UNorm) tool.Convert(texture, PixelFormat.R8G8B8A8_UNorm);
            using var image = tool.ConvertToStrideImage(texture);
            using var stream = new MemoryStream();
            image.Save(stream, ImageFileType.Png);
            stream.Position = 0;
            var bitmap = new Bitmap(stream);
            entries.Add(new(path, modified, texture, bitmap));
            texture = null;
            if (entries.Count > 30) { entries[0].Dispose(); entries.RemoveAt(0); }
            return bitmap;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or InvalidOperationException or TextureToolsException)
        {
            return null;
        }
        finally { texture?.Dispose(); }
    }

    public Size2? GetPixelSize(UFile? path) => RetrieveImage(path) is { } bitmap ? new(bitmap.PixelSize.Width, bitmap.PixelSize.Height) : null;

    private TexImage? GetTexture(UFile? path, Vector2 point)
    {
        var bitmap = RetrieveImage(path);
        if (bitmap == null || point.X < 0 || point.Y < 0 || point.X >= bitmap.PixelSize.Width || point.Y >= bitmap.PixelSize.Height) return null;
        return entries.First(e => e.Path == path).Texture;
    }

    public Color? PickPixelColor(UFile? path, Vector2 point)
    {
        if (GetTexture(path, point) is not { } texture) return null;
        using var tool = new TextureTool();
        return tool.PickColor(texture, new Int2((int)point.X, (int)point.Y));
    }

    public Rectangle? FindSpriteRegion(UFile? path, Vector2 point, bool transparency, Color mask)
    {
        if (GetTexture(path, point) is not { } texture) return null;
        using var tool = new TextureTool();
        var pixel = new Int2((int)point.X, (int)point.Y);
        return transparency ? tool.FindSpriteRegion(texture, pixel) : tool.FindSpriteRegion(texture, pixel, mask, 0x00ffffff);
    }

    public void Dispose()
    {
        foreach (var entry in entries) entry.Dispose();
        entries.Clear();
    }
}
