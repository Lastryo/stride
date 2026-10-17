// Copyright (c) .NET Foundation and Contributors (https://dotnetfoundation.org/ & https://stride3d.net) and Silicon Studio Corp. (https://www.siliconstudio.co.jp)
// Distributed under the MIT license. See the LICENSE.md file in the project root for more information.

using Avalonia;
using Avalonia.Controls;
using Stride.Core.Presentation.Commands;

namespace Stride.Core.Presentation.Avalonia.Controls;

public class TagControl : ContentControl
{
    /// <summary>
    /// Identifies the <see cref="CloseTagCommand"/> styled property.
    /// </summary>
    public static readonly StyledProperty<ICommandBase?> CloseTagCommandProperty =
        AvaloniaProperty.Register<TagControl, ICommandBase?>(nameof(CloseTagCommand));

    public ICommandBase? CloseTagCommand
    {
        get => GetValue(CloseTagCommandProperty);
        set => SetValue(CloseTagCommandProperty, value);
    }
}
