// Copyright (c) .NET Foundation and Contributors (https://dotnetfoundation.org/ & https://stride3d.net)
// Distributed under the MIT license. See the LICENSE.md file in the project root for more information.

using Avalonia.Controls;
using Avalonia.Interactivity;

namespace Stride.GameStudio.Avalonia.Views;

public partial class UpdatePlatformsWindow : Window
{
    public UpdatePlatformsWindow()
    {
        InitializeComponent();
    }

    private void ButtonOk_OnClick(object? sender, RoutedEventArgs e)
    {
        Close(true);
    }

    private void ButtonCancel_OnClick(object? sender, RoutedEventArgs e)
    {
        Close(false);
    }
}

