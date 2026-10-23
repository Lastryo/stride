// Copyright (c) .NET Foundation and Contributors (https://dotnetfoundation.org/ & https://stride3d.net)
// Distributed under the MIT license. See the LICENSE.md file in the project root for more information.

using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Stride.Assets.Editor.SpriteEditor;

namespace Stride.Assets.Editor.Avalonia.SpriteEditor;

public sealed class SpriteThumbnail : Control
{
    private readonly SpriteInfoViewModel sprite;
    private readonly Func<SpriteEditorImageCache?> cache;
    public SpriteThumbnail(SpriteInfoViewModel sprite, Func<SpriteEditorImageCache?> cache)
    {
        this.sprite = sprite;
        this.cache = cache;
        Width = Height = 36;
        RenderOptions.SetBitmapInterpolationMode(this, BitmapInterpolationMode.None);
        AttachedToVisualTree += (_, _) => sprite.PropertyChanged += Changed;
        DetachedFromVisualTree += (_, _) => sprite.PropertyChanged -= Changed;
    }
    private void Changed(object? sender, PropertyChangedEventArgs e) => InvalidateVisual();
    public override void Render(DrawingContext context)
    {
        base.Render(context);
        context.FillRectangle(Brushes.DimGray, new Rect(Bounds.Size));
        if (cache()?.RetrieveImage(sprite.Source) is not { } image) return;
        var r = sprite.Region;
        var source = new Rect(r.X, r.Y, Math.Max(0, r.Width), Math.Max(0, r.Height)).Intersect(new Rect(image.Size));
        if (source.Width <= 0 || source.Height <= 0) return;
        var scale = Math.Min(Bounds.Width / source.Width, Bounds.Height / source.Height);
        var size = source.Size * scale;
        context.DrawImage(image, source, new Rect(new Point((Bounds.Width - size.Width) / 2, (Bounds.Height - size.Height) / 2), size));
    }
}
