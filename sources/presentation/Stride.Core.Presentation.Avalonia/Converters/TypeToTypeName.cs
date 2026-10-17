// Copyright (c) .NET Foundation and Contributors (https://dotnetfoundation.org/ & https://stride3d.net) and Silicon Studio Corp. (https://www.siliconstudio.co.jp)
// Distributed under the MIT license. See the LICENSE.md file in the project root for more information.

using System.Globalization;

namespace Stride.Core.Presentation.Avalonia.Converters;

/// <summary>
/// Converts the type to its type name.
/// </summary>
public sealed class TypeToTypeName : OneWayValueConverter<TypeToTypeName>
{
    /// <inheritdoc/>
    public override object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not Type type)
            return null;

        if (parameter is null)
            return type.Name;

        return parameter.ToString()?.Replace("$", type.Name);
    }
}
