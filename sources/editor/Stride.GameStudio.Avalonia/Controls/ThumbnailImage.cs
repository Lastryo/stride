// Copyright (c) .NET Foundation and Contributors (https://dotnetfoundation.org/ & https://stride3d.net)
// Distributed under the MIT license. See the LICENSE.md file in the project root for more information.

using Avalonia;
using Avalonia.Media.Imaging;
using Stride.GameStudio.Avalonia.Converters;

namespace Stride.GameStudio.Avalonia.Controls;

public sealed class ThumbnailImage : global::Avalonia.Controls.Image
{
    public static readonly StyledProperty<Stride.Graphics.Image?> ImageProperty =
        AvaloniaProperty.Register<ThumbnailImage, Stride.Graphics.Image?>(nameof(Image));
    private Bitmap? bitmap;

    public Stride.Graphics.Image? Image
    {
        get => GetValue(ImageProperty);
        set => SetValue(ImageProperty, value);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == ImageProperty && VisualRoot != null) UpdateBitmap();
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        UpdateBitmap();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        Source = null;
        bitmap?.Dispose();
        bitmap = null;
        base.OnDetachedFromVisualTree(e);
    }

    private void UpdateBitmap()
    {
        var previous = bitmap;
        bitmap = Image is { } image ? StrideImage.CreateBitmap(image) : null;
        Source = bitmap;
        previous?.Dispose();
    }
}
