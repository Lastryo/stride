// Copyright (c) .NET Foundation and Contributors (https://dotnetfoundation.org/ & https://stride3d.net)
// Distributed under the MIT license. See the LICENSE.md file in the project root for more information.

using System;
using System.Runtime.CompilerServices;
using Avalonia.Controls;
using Avalonia.Media;

namespace Stride.Core.Presentation.Avalonia.Themes;

public static class ImageThemingUtilities
{
    private const double DarkBackgroundLuminosity = 0.065; // Matches WPF IconTheme("Dark", Color.FromRgb(16, 16, 17))
    private const double LightBackgroundLuminosity = 0.96;

    private static readonly ConditionalWeakTable<Drawing, object?> ThemedDrawings = new();

    public static void TransformDrawing(Drawing drawing, bool isDark = true)
    {
        if (drawing == null) return;
        lock (ThemedDrawings)
        {
            if (ThemedDrawings.TryGetValue(drawing, out _))
                return;
            ThemedDrawings.Add(drawing, null);
        }

        TransformParts(drawing, isDark ? DarkBackgroundLuminosity : LightBackgroundLuminosity, isDark);
    }

    public static void TransformDictionary(ResourceDictionary dictionary, bool isDark = true)
    {
        if (dictionary == null) return;
        foreach (var key in dictionary.Keys)
        {
            if (dictionary[key] is DrawingImage drawingImage && drawingImage.Drawing != null)
            {
                TransformDrawing(drawingImage.Drawing, isDark);
            }
        }
    }

    private static void TransformParts(Drawing drawing, double backgroundLuminosity, bool isDark)
    {
        if (drawing is GeometryDrawing gd)
        {
            if (gd.Brush is ISolidColorBrush scb)
            {
                var color = scb.Color;
                if (color.A > 0)
                {
                    var hsl = HslColor.FromColor(color);
                    // In VS/WPF vector icons, the halo backing layer is almost white (#F6F6F6, luminosity >= 82/85) with low saturation.
                    // On dark theme, this backing halo must become transparent so the dark background/button shines through cleanly.
                    if (isDark && hsl.Luminosity >= 82.0 / 85.0 && hsl.Saturation < 0.2)
                    {
                        gd.Brush = new SolidColorBrush(Color.FromArgb(0, 0, 0, 0));
                    }
                    else
                    {
                        var newL = TransformLuminosity(hsl, backgroundLuminosity);
                        var newColor = new HslColor(hsl.Hue, hsl.Saturation, newL, hsl.Alpha).ToColor();
                        gd.Brush = new SolidColorBrush(newColor);
                    }
                }
            }
            if (gd.Pen?.Brush is ISolidColorBrush penScb)
            {
                var color = penScb.Color;
                if (color.A > 0)
                {
                    var hsl = HslColor.FromColor(color);
                    var newL = TransformLuminosity(hsl, backgroundLuminosity);
                    var newColor = new HslColor(hsl.Hue, hsl.Saturation, newL, hsl.Alpha).ToColor();
                    gd.Pen = new Pen(new SolidColorBrush(newColor), gd.Pen.Thickness, gd.Pen.DashStyle, gd.Pen.LineCap, gd.Pen.LineJoin, gd.Pen.MiterLimit);
                }
            }
        }
        else if (drawing is DrawingGroup dg)
        {
            foreach (var child in dg.Children)
            {
                TransformParts(child, backgroundLuminosity, isDark);
            }
        }
    }

    private static double TransformLuminosity(HslColor hsl, double backgroundLuminosity)
    {
        var hue = hsl.Hue;
        var saturation = hsl.Saturation;
        var luminosity = hsl.Luminosity;
        if (backgroundLuminosity < 0.5)
        {
            if (luminosity >= 82.0 / 85.0)
                return backgroundLuminosity * (luminosity - 1.0) / (-3.0 / 85.0);
            double val2 = saturation >= 0.2 ? (saturation <= 0.3 ? 1.0 - (saturation - 0.2) / 0.1 : 0.0) : 1.0;
            double num1 = Math.Max(Math.Min(1.0, Math.Abs(hue - 37.0) / 20.0), val2);
            double num2 = ((backgroundLuminosity - 1.0) * 0.66 / (82.0 / 85.0) + 1.0) * num1 + 0.66 * (1.0 - num1);
            if (luminosity < 0.66)
                return (num2 - 1.0) / 0.66 * luminosity + 1.0;
            return (num2 - backgroundLuminosity) / (-259.0 / 850.0) * (luminosity - 82.0 / 85.0) + backgroundLuminosity;
        }
        if (luminosity < 82.0 / 85.0)
            return luminosity * backgroundLuminosity / (82.0 / 85.0);
        return (1.0 - backgroundLuminosity) * (luminosity - 1.0) / (3.0 / 85.0) + 1.0;
    }
}

