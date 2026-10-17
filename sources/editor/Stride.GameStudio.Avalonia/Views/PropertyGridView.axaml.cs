// Copyright (c) .NET Foundation and Contributors (https://dotnetfoundation.org/ & https://stride3d.net)
// Distributed under the MIT license. See the LICENSE.md file in the project root for more information.

using Avalonia.Controls;
using Avalonia.Input;
using Stride.Core.Assets.Editor.Components.Properties;

namespace Stride.GameStudio.Avalonia.Views;

public partial class PropertyGridView : UserControl
{
    public PropertyGridView()
    {
        InitializeComponent();
    }

    private void NewTagTextBox_OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter || sender is not TextBox textBox)
            return;

        if (DataContext is SessionObjectPropertiesViewModel { Session.AssetTags.AddTagCommand: { } addTagCommand } &&
            !string.IsNullOrWhiteSpace(textBox.Text))
        {
            addTagCommand.Execute(textBox.Text);
            textBox.Clear();
            e.Handled = true;
        }
    }
}
