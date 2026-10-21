// Copyright (c) .NET Foundation and Contributors (https://dotnetfoundation.org/ & https://stride3d.net)
// Distributed under the MIT license. See the LICENSE.md file in the project root for more information.

using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using Stride.Assets.Editor.GraphicsCompositorEditor.ViewModels;
using Stride.Assets.Editor.ViewModels;
using Stride.Core.Assets.Editor.Annotations;
using Stride.Core.Assets.Editor.Editors;

namespace Stride.Assets.Editor.Avalonia.Views;

[AssetEditorView<GraphicsCompositorEditorViewModel>]
public partial class GraphicsCompositorEditorView : UserControl, IAssetEditorView
{
    public GraphicsCompositorEditorView()
    {
        InitializeComponent();
        AttachedToVisualTree += async (_, _) =>
        {
            if (DataContext is GraphicsCompositorEditorViewModel editor)
            {
                if (editor.SelectedSharedRenderers.Count == 0 &&
                    editor.SelectedRenderStages.Count == 0 &&
                    editor.SelectedRenderFeatures.Count == 0 &&
                    editor.SelectedCameraSlots.Count == 0 &&
                    editor.Blocks.Count > 0)
                {
                    editor.SelectedSharedRenderers.Add(editor.Blocks[0]);
                }
                await editor.RefreshPropertiesAsync();
            }
        };
    }

    private void AddRenderFeature(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button button || DataContext is not GraphicsCompositorEditorViewModel editor) return;
        var menu = new ContextMenu
        {
            ItemsSource = editor.RenderFeatureTypes.Select(type => new MenuItem
            {
                Header = type.DisplayValue, Command = editor.AddNewRenderFeatureCommand, CommandParameter = type
            }).ToArray()
        };
        button.ContextMenu = menu;
        menu.Open(button);
    }

    private void EditorKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Handled || e.Key != Key.Delete || e.Source is TextBox ||
            (e.Source as Control)?.FindAncestorOfType<TextBox>() != null) return;
        if (DataContext is GraphicsCompositorEditorViewModel editor)
        {
            editor.DeleteSelectionCommand.Execute(null);
            e.Handled = true;
        }
    }

    private void BeginRename(object? sender, TappedEventArgs e)
    {
        if (sender is TextBlock { Parent: Grid grid }) ShowRename(grid);
        e.Handled = true;
    }

    private void ListKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key != Key.F2 || sender is not ListBox list || list.SelectedItem == null) return;
        var item = list.ContainerFromItem(list.SelectedItem);
        var text = item?.GetVisualDescendants().OfType<TextBox>().FirstOrDefault();
        if (text?.Parent is Grid grid) ShowRename(grid);
        e.Handled = true;
    }

    private static void ShowRename(Grid grid)
    {
        var text = grid.Children.OfType<TextBox>().Single();
        text.Text = text.DataContext switch
        {
            RenderStageViewModel stage => stage.Name,
            GraphicsCompositorCameraSlotsViewModel camera => camera.Name,
            _ => string.Empty
        };
        grid.Children.OfType<TextBlock>().Single().IsVisible = false;
        text.IsVisible = true;
        text.Focus();
        text.SelectAll();
    }

    private static void FinishRename(TextBox text, bool commit)
    {
        if (!text.IsVisible) return;
        text.IsVisible = false;
        if (commit)
        {
            switch (text.DataContext)
            {
                case RenderStageViewModel stage: stage.Name = text.Text ?? string.Empty; break;
                case GraphicsCompositorCameraSlotsViewModel camera: camera.Name = text.Text ?? string.Empty; break;
            }
        }
        if (text.Parent is Grid grid) grid.Children.OfType<TextBlock>().Single().IsVisible = true;
    }

    private void RenameKeyDown(object? sender, KeyEventArgs e)
    {
        if (sender is TextBox text && e.Key is Key.Enter or Key.Escape)
        {
            FinishRename(text, e.Key == Key.Enter);
            e.Handled = true;
        }
    }

    private void RenameLostFocus(object? sender, RoutedEventArgs e)
    {
        if (sender is TextBox text) FinishRename(text, true);
    }
}
