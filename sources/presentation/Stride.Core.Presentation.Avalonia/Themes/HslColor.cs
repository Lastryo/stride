// Copyright (c) .NET Foundation and Contributors (https://dotnetfoundation.org/ & https://stride3d.net)
// Distributed under the MIT license. See the LICENSE.md file in the project root for more information.

using System;
using Avalonia.Media;

namespace Stride.Core.Presentation.Avalonia.Themes;

public struct HslColor
{
    public HslColor(double hue, double saturation, double luminosity, double alpha = 1.0)
    {
        Hue = Math.Clamp(hue, 0.0, 360.0);
        Saturation = Math.Clamp(saturation, 0.0, 1.0);
        Luminosity = Math.Clamp(luminosity, 0.0, 1.0);
        Alpha = Math.Clamp(alpha, 0.0, 1.0);
    }

    public double Hue { get; set; }
    public double Saturation { get; set; }
    public double Luminosity { get; set; }
    public double Alpha { get; set; }

    public static HslColor FromColor(Color color)
    {
        byte r = color.R;
        byte g = color.G;
        byte b = color.B;
        byte max = Math.Max(r, Math.Max(g, b));
        byte min = Math.Min(r, Math.Min(g, b));
        double delta = max - min;
        double rNorm = r / 255.0;
        double gNorm = g / 255.0;
        double bNorm = b / 255.0;
        double maxNorm = max / 255.0;
        double minNorm = min / 255.0;
        double hue = delta == 0 ? 0.0 :
            max == r ? (gNorm < bNorm ? 60.0 * (gNorm - bNorm) / (maxNorm - minNorm) + 360.0 : 60.0 * (gNorm - bNorm) / (maxNorm - minNorm)) :
            max == g ? 60.0 * (bNorm - rNorm) / (maxNorm - minNorm) + 120.0 :
                       60.0 * (rNorm - gNorm) / (maxNorm - minNorm) + 240.0;
        double alpha = color.A / 255.0;
        double luminosity = 0.5 * (maxNorm + minNorm);
        double saturation = delta == 0 ? 0.0 :
            luminosity > 0.5 ? (maxNorm - minNorm) / (2.0 - 2.0 * luminosity) : (maxNorm - minNorm) / (2.0 * luminosity);
        return new HslColor(hue, saturation, luminosity, alpha);
    }

    public Color ToColor()
    {
        double q = Luminosity < 0.5 ? Luminosity * (1.0 + Saturation) : Luminosity + Saturation - Luminosity * Saturation;
        double p = 2.0 * Luminosity - q;
        double num = Hue / 360.0;
        double tC1 = ModOne(num + 1.0 / 3.0);
        double tC2 = num;
        double tC3 = ModOne(num - 1.0 / 3.0);
        return Color.FromArgb(
            (byte)Math.Clamp(Math.Round(Alpha * 255.0), 0, 255),
            (byte)Math.Clamp(Math.Round(ComputeRGBComponent(p, q, tC1) * 255.0), 0, 255),
            (byte)Math.Clamp(Math.Round(ComputeRGBComponent(p, q, tC2) * 255.0), 0, 255),
            (byte)Math.Clamp(Math.Round(ComputeRGBComponent(p, q, tC3) * 255.0), 0, 255));
    }

    private static double ModOne(double value)
    {
        if (value < 0.0) return value + 1.0;
        if (value > 1.0) return value - 1.0;
        return value;
    }

    private static double ComputeRGBComponent(double p, double q, double tC)
    {
        if (tC < 1.0 / 6.0) return p + (q - p) * 6.0 * tC;
        if (tC < 0.5) return q;
        if (tC < 2.0 / 3.0) return p + (q - p) * 6.0 * (2.0 / 3.0 - tC);
        return p;
    }
}

