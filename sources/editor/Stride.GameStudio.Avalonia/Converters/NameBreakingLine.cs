// Copyright (c) .NET Foundation and Contributors (https://dotnetfoundation.org/ & https://stride3d.net)
// Distributed under the MIT license. See the LICENSE.md file in the project root for more information.

using System.Globalization;
using System.Text.RegularExpressions;
using Stride.Core.Presentation.Avalonia.Converters;

namespace Stride.GameStudio.Avalonia.Converters;

/// <summary>Uses the same word wrap opportunities as WPF asset captions.</summary>
public sealed class NameBreakingLine : OneWayValueConverter<NameBreakingLine>
{
    private static readonly Regex Breaks = new(
        @"(?<BEFORE>[^0-9\p{L}\s\(\[\{])|(?<BEFORE>[\p{L}0-9])(?<AFTER>[\(\[\{])|(?<BEFORE>\p{Ll})(?<AFTER>[\p{Lu}0-9])|(?<BEFORE>\p{Lu})(?<AFTER>[0-9])");

    public override object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is string name
            ? Breaks.Replace(name, match => match.Groups["BEFORE"].Value + "\u200B" + match.Groups["AFTER"].Value)
            : value;
}
