// Copyright (c) .NET Foundation and Contributors (https://dotnetfoundation.org/ & https://stride3d.net) and Silicon Studio Corp. (https://www.siliconstudio.co.jp)
// Distributed under the MIT license. See the LICENSE.md file in the project root for more information.

using System.Globalization;
using Stride.Core.Presentation.Avalonia.Converters;
using Stride.Engine;

namespace Stride.Assets.Editor.Avalonia.Converters;

public sealed class EntityComponentToTransformLinkInfo : OneWayValueConverter<EntityComponentToTransformLinkInfo>
{
    public override object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not IEnumerable<EntityComponent> components)
            return null;

        var link = components.OfType<ModelNodeLinkComponent>().FirstOrDefault();
        if (string.IsNullOrEmpty(link?.NodeName))
            return null;

        var targetName = link.Target?.Entity?.Name;
        return !string.IsNullOrEmpty(targetName)
            ? $"(Link: {targetName}.{link.NodeName})"
            : $"(Link: {link.NodeName})";
    }
}
