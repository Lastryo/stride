// Copyright (c) .NET Foundation and Contributors (https://dotnetfoundation.org/ & https://stride3d.net)
// Distributed under the MIT license. See the LICENSE.md file in the project root for more information.

using System.Globalization;
using Avalonia;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Stride.Core.Presentation.Avalonia.Converters;
using Stride.Graphics;
using Image = Stride.Graphics.Image;
using PixelFormat = Stride.Graphics.PixelFormat;

namespace Stride.GameStudio.Avalonia.Converters;

public sealed class StrideImage : OneWayValueConverter<StrideImage>
{
    public override object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is Image image ? CreateBitmap(image) ?? (object)AvaloniaProperty.UnsetValue : AvaloniaProperty.UnsetValue;

    public static Bitmap? CreateBitmap(Image image)
    {
        var format = image.Description.Format switch
        {
            PixelFormat.R8G8B8A8_UNorm or PixelFormat.R8G8B8A8_UNorm_SRgb => PixelFormats.Rgba8888,
            PixelFormat.B8G8R8A8_UNorm or PixelFormat.B8G8R8A8_UNorm_SRgb => PixelFormats.Bgra8888,
            _ => (global::Avalonia.Platform.PixelFormat?)null,
        };
        if (format is null) return null;
        var buffer = image.PixelBuffer[0];
        return new Bitmap(format.Value, AlphaFormat.Unpremul, buffer.DataPointer,
            new PixelSize(buffer.Width, buffer.Height), new Vector(96, 96), buffer.RowStride);
    }
}
