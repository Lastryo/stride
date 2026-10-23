// Copyright (c) .NET Foundation and Contributors (https://dotnetfoundation.org/ & https://stride3d.net)
// Distributed under the MIT license. See the LICENSE.md file in the project root for more information.
using Avalonia;
using Stride.Assets.Editor.Avalonia.ScriptEditor;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using AvaloniaEdit;
using AvaloniaEdit.Highlighting;
using Stride.Assets.Editor.ViewModels;

namespace Stride.Assets.Editor.Avalonia.Views;

public partial class ScriptSourceFileEditorView
{
    private async Task UpdateSignatures()
    {
        signatureRequest?.Cancel(); signatureRequest?.Dispose(); signatureRequest = new();
        var token = signatureRequest.Token; var snapshot = model; var offset = CodeEditor.CaretOffset;
        try
        {
            var help = snapshot == null ? null : await snapshot.SignatureHelpAsync(offset, token);
            if (token.IsCancellationRequested || model != snapshot || offset != CodeEditor.CaretOffset) return;
            var selected = SignatureList.SelectedIndex;
            SignatureList.ItemsSource = help?.Signatures;
            signaturePopup.IsOpen = help != null;
            PositionInlineControls();
            if (help != null) SignatureList.SelectedIndex = Math.Clamp(selected, 0, help.Signatures.Count - 1);
        }
        catch (OperationCanceledException) { }
        catch (Exception exception) { ToolTip.SetTip(CodeEditor, exception.Message); signaturePopup.IsOpen = false; }
    }

    private static TextEditor ReadOnlyEditor(string text) => new()
    {
        Text = text, IsReadOnly = true, ShowLineNumbers = true, FontFamily = new FontFamily("monospace"), FontSize = 14,
        SyntaxHighlighting = HighlightingManager.Instance.GetDefinition("C#"), Background = Brushes.Transparent,
        HorizontalScrollBarVisibility = global::Avalonia.Controls.Primitives.ScrollBarVisibility.Auto,
        VerticalScrollBarVisibility = global::Avalonia.Controls.Primitives.ScrollBarVisibility.Auto,
    };
    private Window EditorWindow(string title, Control content)
    {
        var window = new Window { Title = title, Width = 1000, Height = 650, MinWidth = 500, MinHeight = 300, WindowStartupLocation = WindowStartupLocation.CenterOwner, Content = content };
        window.Styles.Add(new global::Avalonia.Markup.Xaml.Styling.StyleInclude(new Uri("avares://Stride.Assets.Editor.Avalonia/")) { Source = new Uri("avares://AvaloniaEdit/Themes/Fluent/AvaloniaEdit.xaml") });
        return window;
    }
    private void MetadataRequested(object? sender, EventArgs e)
    {
        if (model?.MetadataText == null || TopLevel.GetTopLevel(this) is not Window owner) return;
        EditorWindow(model.MetadataTitle ?? "Metadata", ReadOnlyEditor(model.MetadataText)).Show(owner);
    }
    private async Task ShowActions()
    {
        if (model == null || CodeEditor.IsReadOnly || TopLevel.GetTopLevel(this) is not Window owner) return;
        var snapshot = model;
        var actions = await snapshot.ContextActionsAsync(CodeEditor.SelectionStart, CodeEditor.SelectionLength);
        if (snapshot != model) return;
        ShowActionMenu(snapshot, actions);
    }
    private async Task Rename()
    {
        if (model == null || CodeEditor.IsReadOnly || TopLevel.GetTopLevel(this) is not Window owner) return;
        var snapshot = model; var offset = CodeEditor.CaretOffset;
        var input = new TextBox { PlaceholderText = "New symbol name", Margin = new Thickness(8) };
        var button = new Button { Content = "Preview rename", Margin = new Thickness(8), HorizontalAlignment = HorizontalAlignment.Right };
        var panel = new StackPanel { Children = { input, button } };
        var window = new Window { Title = "Rename C# symbol", Width = 420, SizeToContent = SizeToContent.Height, WindowStartupLocation = WindowStartupLocation.CenterOwner, Content = panel };
        button.Click += (_, _) => { if (!string.IsNullOrWhiteSpace(input.Text)) window.Close(input.Text); };
        window.Opened += (_, _) => input.Focus();
        var name = await window.ShowDialog<string?>(owner);
        if (name != null && snapshot == model) await ShowPreview(snapshot, await snapshot.PreviewRenameAsync(offset, name));
    }
    private async Task ShowPreview(ScriptSourceFileEditorViewModel snapshot, ScriptEditPreview preview)
    {
        if (TopLevel.GetTopLevel(this) is not Window owner) return;
        var tabs = new TabControl();
        tabs.ItemsSource = preview.Documents.Select(edit =>
        {
            var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("*,*"), RowDefinitions = new RowDefinitions("Auto,*") };
            grid.Children.Add(new TextBlock { Text = "Before", Margin = new Thickness(6) });
            var label = new TextBlock { Text = "After", Margin = new Thickness(6) }; Grid.SetColumn(label, 1); grid.Children.Add(label);
            var before = ReadOnlyEditor(edit.Before); Grid.SetRow(before, 1); grid.Children.Add(before);
            var after = ReadOnlyEditor(edit.After); Grid.SetRow(after, 1); Grid.SetColumn(after, 1); grid.Children.Add(after);
            return new TabItem { Header = System.IO.Path.GetFileName(edit.Path), Content = grid, [ToolTip.TipProperty] = edit.Path };
        }).ToArray();
        var apply = new Button { Content = "Apply to buffers", IsEnabled = preview.Documents.Count > 0, Margin = new Thickness(6), HorizontalAlignment = HorizontalAlignment.Right };
        var panel = new DockPanel(); DockPanel.SetDock(apply, Dock.Bottom); panel.Children.Add(apply); panel.Children.Add(tabs);
        var window = EditorWindow(preview.Title, panel);
        apply.Click += (_, _) => window.Close(true);
        if (await window.ShowDialog<bool>(owner) && snapshot == model) snapshot.ApplyPreview(preview);
    }
}
