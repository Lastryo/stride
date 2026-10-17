// Copyright (c) .NET Foundation and Contributors (https://dotnetfoundation.org/ & https://stride3d.net)
// Distributed under the MIT license. See the LICENSE.md file in the project root for more information.

using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace Stride.Assets.Editor.Avalonia.Views;

public sealed class ImageDictionary : ResourceDictionary
{
    public ImageDictionary()
    {
        AvaloniaXamlLoader.Load(this);
    }
}
