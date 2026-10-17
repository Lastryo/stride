// Copyright (c) .NET Foundation and Contributors (https://dotnetfoundation.org/ & https://stride3d.net) and Silicon Studio Corp. (https://www.siliconstudio.co.jp)
// Distributed under the MIT license. See the LICENSE.md file in the project root for more information.

using System.Globalization;
using Stride.Assets.Presentation;
using Stride.Core.Assets.Editor.Avalonia.Converters;
using Stride.Core.Presentation.Avalonia.Converters;
using Stride.Engine;

namespace Stride.Assets.Editor.Avalonia.Converters;

public sealed class EntityComponentToResource : OneWayValueConverter<EntityComponentToResource>
{
    public override object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        StrideEditorViewPlugin.EnsureResourcesRegistered();
        if (value is not IEnumerable<EntityComponent> components)
            return TypeToResource.FetchResourceFromType(typeof(EntityComponent), true);

        var componentTypes = components.Select(x => x.GetType());
        var compToUse = StrideDefaultAssetsPlugin.GetHighestOrderComponent(componentTypes) ?? typeof(EntityComponent);
        return TypeToResource.FetchResourceFromType(compToUse, true);
    }
}
