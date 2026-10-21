// Copyright (c) .NET Foundation and Contributors (https://dotnetfoundation.org/ & https://stride3d.net)
// Distributed under the MIT license. See the LICENSE.md file in the project root for more information.

using System.Globalization;
using Stride.Assets.Editor.GameEditor;
using Stride.Core.Presentation.Avalonia.Converters;

namespace Stride.Assets.Editor.Avalonia.Converters;

public sealed class TransformationSpaceLabel : OneWayValueConverter<TransformationSpaceLabel>
{
    public override object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) => value switch
    {
        TransformationSpace.WorldSpace => "World",
        TransformationSpace.ObjectSpace => "Local",
        TransformationSpace.ViewSpace => "View",
        _ => null,
    };
}
