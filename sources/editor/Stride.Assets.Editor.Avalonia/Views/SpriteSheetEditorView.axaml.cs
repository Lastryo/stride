// Copyright (c) .NET Foundation and Contributors (https://dotnetfoundation.org/ & https://stride3d.net)
// Distributed under the MIT license. See the LICENSE.md file in the project root for more information.

using System.ComponentModel;
using System.Diagnostics;
using Avalonia.Controls.Templates;
using Avalonia.Data;
using Avalonia.VisualTree;
using Stride.Core.Assets.Editor.Avalonia;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using Stride.Assets.Editor.Avalonia.SpriteEditor;
using Stride.Assets.Editor.SpriteEditor;
using Stride.Assets.Editor.ViewModels;
using Stride.Core.Assets.Editor.Annotations;
using Stride.Core.Assets.Editor.Editors;
using Stride.Core.IO;
using Stride.Core.Mathematics;
using Stride.Core.Presentation.Services;

namespace Stride.Assets.Editor.Avalonia.Views;

[AssetEditorView<SpriteSheetEditorViewModel>]
public partial class SpriteSheetEditorView : UserControl, IAssetEditorView
{
    private SpriteEditorImageCache? cache;
    private SpriteSheetEditorViewModel? editor;
    private SpriteInfoViewModel? watchedSprite;
    private FileSystemWatcher? watcher;
    private readonly DispatcherTimer reloadTimer = new() { Interval = TimeSpan.FromMilliseconds(150) };

    public SpriteSheetEditorView()
    {
        InitializeComponent();
        SpriteList.ItemTemplate = new FuncDataTemplate<SpriteInfoViewModel>((sprite, _) =>
        {
            if (sprite == null) return new Border();
            var row = new Grid { ColumnDefinitions = new ColumnDefinitions("40,*"), Margin = new Thickness(2) };
            row.Children.Add(new SpriteThumbnail(sprite, () => cache));
            var label = new TextBlock { VerticalAlignment = global::Avalonia.Layout.VerticalAlignment.Center, TextTrimming = global::Avalonia.Media.TextTrimming.CharacterEllipsis };
            label.Bind(TextBlock.TextProperty, new Binding(nameof(SpriteInfoViewModel.Name)) { Source = sprite, Mode = BindingMode.OneWay });
            Grid.SetColumn(label, 1); row.Children.Add(label);
            var input = new TextBox { IsVisible = false };
            Grid.SetColumn(input, 1); row.Children.Add(input);
            void Rename(bool commit)
            {
                if (!input.IsVisible) return;
                if (commit && editor?.Asset.IsEditable == true && editor.Sprites.Contains(sprite) && !string.IsNullOrWhiteSpace(input.Text)) sprite.Name = input.Text;
                input.IsVisible = false; label.IsVisible = true;
            }
            label.DoubleTapped += (_, e) =>
            {
                if (editor?.Asset.IsEditable != true) return;
                label.IsVisible = false; input.Text = sprite.Name; input.IsVisible = true; input.Focus(); input.SelectAll(); e.Handled = true;
            };
            input.LostFocus += (_, _) => Rename(true);
            input.KeyDown += (_, e) => { if (e.Key is Key.Enter or Key.Escape) { Rename(e.Key == Key.Enter); e.Handled = true; SpriteList.Focus(); } };
            return row;
        });
        SpriteList.AddHandler(PointerPressedEvent, ListPointerPressed, RoutingStrategies.Tunnel);
        SpriteList.AddHandler(PointerMovedEvent, ListPointerMoved);
        SpriteList.AddHandler(PointerReleasedEvent, (_, _) => listDragStart = null);
        ToolSelector.ItemsSource = Enum.GetValues<SpriteSheetEditorToolMode>();
        SpriteCanvas.StatusChanged += status => ImageStatus.Text = status;
        reloadTimer.Tick += (_, _) =>
        {
            reloadTimer.Stop(); SpriteCanvas.ReloadImage();
            foreach (var thumbnail in SpriteList.GetVisualDescendants().OfType<SpriteThumbnail>()) thumbnail.InvalidateVisual();
        };
        AttachedToVisualTree += (_, _) => Attach();
        DetachedFromVisualTree += (_, _) => Detach();
        AddHandler(DragDrop.DragOverEvent, DragOver);
        AddHandler(DragDrop.DropEvent, Drop);
        DragDrop.SetAllowDrop(this, true);
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        Detach();
        base.OnDataContextChanged(e);
        if (VisualRoot != null) Attach();
    }

    private void Attach()
    {
        if (editor != null || DataContext is not SpriteSheetEditorViewModel model) return;
        editor = model;
        cache = new();
        SpriteCanvas.Cache = cache;
        SpriteCanvas.DataContext = null;
        SpriteCanvas.DataContext = model;
        editor.PropertyChanged += EditorChanged;
        WatchSource();
    }

    private void Detach()
    {
        if (editor != null) editor.PropertyChanged -= EditorChanged;
        if (watchedSprite != null) watchedSprite.PropertyChanged -= SpriteChanged;
        watchedSprite = null;
        editor = null;
        watcher?.Dispose(); watcher = null;
        reloadTimer.Stop();
        SpriteCanvas.Detach();
        SpriteCanvas.Cache = null;
        cache?.Dispose(); cache = null;
    }

    private void EditorChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(SpriteSheetEditorViewModel.CurrentSprite)) WatchSource();
    }

    private void SpriteChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(SpriteInfoViewModel.Source)) return;
        WatchSource();
        if (editor?.Asset.IsEditable == true && !editor.UndoRedoService.UndoRedoInProgress && watchedSprite is { } sprite
            && sprite.Region.Width == 0 && sprite.Region.Height == 0 && cache?.GetPixelSize(sprite.Source) is { } size)
            editor.SetRegion(sprite, new Rectangle(0, 0, size.Width, size.Height));
    }

    private void WatchSource()
    {
        if (watchedSprite != null) watchedSprite.PropertyChanged -= SpriteChanged;
        watchedSprite = editor?.CurrentSprite;
        if (watchedSprite != null) watchedSprite.PropertyChanged += SpriteChanged;
        watcher?.Dispose(); watcher = null;
        var path = watchedSprite?.Source?.ToString();
        if (string.IsNullOrEmpty(path)) return;
        var directory = Path.GetDirectoryName(path);
        if (!Directory.Exists(directory)) return;
        try
        {
            watcher = new FileSystemWatcher(directory, Path.GetFileName(path)) { NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.Size | NotifyFilters.FileName };
            watcher.Changed += SourceChanged;
            watcher.Created += SourceChanged;
            watcher.Deleted += SourceChanged;
            watcher.Renamed += SourceChanged;
            watcher.EnableRaisingEvents = true;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException)
        {
            ImageStatus.Text = $"Cannot watch image: {exception.Message}";
        }
    }

    private void SourceChanged(object sender, FileSystemEventArgs e) => Dispatcher.UIThread.Post(() =>
    {
        if (!ReferenceEquals(sender, watcher)) return;
        reloadTimer.Stop(); reloadTimer.Start();
    });

    private PointerPressedEventArgs? listDragStart;
    private global::Avalonia.Point listDragOrigin;
    private void ListPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        listDragStart = e.GetCurrentPoint(SpriteList).Properties.IsLeftButtonPressed && e.Source is Control source && source.FindAncestorOfType<TextBox>(true) == null ? e : null;
        listDragOrigin = e.GetPosition(SpriteList);
    }
    private async void ListPointerMoved(object? sender, PointerEventArgs e)
    {
        if (listDragStart == null || editor?.Asset.IsEditable != true || !e.GetCurrentPoint(SpriteList).Properties.IsLeftButtonPressed) return;
        var distance = e.GetPosition(SpriteList) - listDragOrigin;
        if (Math.Abs(distance.X) < 4 && Math.Abs(distance.Y) < 4) return;
        var start = listDragStart; listDragStart = null;
        EditorDragDrop.CurrentDragData = editor.SelectedSprites.Cast<SpriteInfoViewModel>().ToArray();
        try { await DragDrop.DoDragDropAsync(start, new DataTransfer(), DragDropEffects.Move); }
        finally { EditorDragDrop.CurrentDragData = null; }
    }
    private async void OpenImageClicked(object? sender, RoutedEventArgs e) => await OpenSource(false);
    private async void ShowFolderClicked(object? sender, RoutedEventArgs e) => await OpenSource(true);
    private async Task OpenSource(bool folder) => await Run(() =>
    {
        var source = editor?.CurrentSprite?.Source?.ToString();
        if (string.IsNullOrEmpty(source) || !File.Exists(source)) throw new FileNotFoundException("Select a sprite with an existing source image.", source);
        Process.Start(new ProcessStartInfo(folder ? Path.GetDirectoryName(source)! : source) { UseShellExecute = true });
        return Task.CompletedTask;
    });

    private async void ImportClicked(object? sender, RoutedEventArgs e)
    {
        if (editor?.Asset.IsEditable != true || TopLevel.GetTopLevel(this)?.StorageProvider is not { } storage) return;
        await Run(async () =>
        {
            var model = editor;
            var files = await storage.OpenFilePickerAsync(new FilePickerOpenOptions { Title = "Import sprite images", AllowMultiple = true,
                FileTypeFilter = [new FilePickerFileType("Images") { Patterns = ["*.png", "*.jpg", "*.jpeg", "*.bmp", "*.tga", "*.dds", "*.tif", "*.tiff"] }, FilePickerFileTypes.All] });
            if (editor != model || cache == null) return;
            model.ImportFiles(files.Select(f => f.TryGetLocalPath()).OfType<string>().Select(p => new UFile(p)), cache.GetPixelSize);
        });
    }

    private void WholeImageClicked(object? sender, RoutedEventArgs e)
    {
        if (editor?.Asset.IsEditable != true || cache == null) return;
        using var transaction = editor.UndoRedoService.CreateTransaction();
        foreach (var sprite in editor.SelectedSprites.Cast<SpriteInfoViewModel>())
            if (cache.GetPixelSize(sprite.Source) is { } size) editor.SetRegion(sprite, new Rectangle(0, 0, size.Width, size.Height));
        editor.UndoRedoService.SetName(transaction, "Use whole image");
    }

    private void FitClicked(object? sender, RoutedEventArgs e) => SpriteCanvas.Fit();
    private void RealSizeClicked(object? sender, RoutedEventArgs e) => SpriteCanvas.RealSize();
    private void ZoomInClicked(object? sender, RoutedEventArgs e) => SpriteCanvas.Zoom(1.25);
    private void ZoomOutClicked(object? sender, RoutedEventArgs e) => SpriteCanvas.Zoom(0.8);
    private void FrameClicked(object? sender, RoutedEventArgs e) => SpriteCanvas.Frame();
    private void SpriteDoubleTapped(object? sender, TappedEventArgs e) => SpriteCanvas.Frame();

    private async void EditorKeyDown(object? sender, KeyEventArgs e)
    {
        if (editor == null || e.Source is TextBox || TopLevel.GetTopLevel(this)?.FocusManager?.GetFocusedElement() is TextBox) return;
        var control = (e.KeyModifiers & (KeyModifiers.Control | KeyModifiers.Meta)) != 0;
        if (e.Key == Key.Escape) SpriteCanvas.CancelGesture();
        else if (control && e.Key == Key.C) await Run(editor.CopyAsync);
        else if (control && e.Key == Key.V) await Run(editor.PasteAsync);
        else if (control && e.Key == Key.D) editor.DuplicateCommand.Execute(null);
        else if (e.Key == Key.Delete) editor.Delete();
        else if (e.Key == Key.F) SpriteCanvas.Frame();
        else if (e.Key == Key.M) editor.ToolMode = editor.ToolMode == SpriteSheetEditorToolMode.MagicWand ? SpriteSheetEditorToolMode.Region : SpriteSheetEditorToolMode.MagicWand;
        else if (e.Key == Key.Space && editor.Sprites.Count > 0)
        {
            var index = editor.CurrentSprite == null ? -1 : editor.Sprites.IndexOf(editor.CurrentSprite);
            editor.Select(editor.Sprites[(index + 1) % editor.Sprites.Count]);
        }
        else return;
        e.Handled = true;
    }

    private void DragOver(object? sender, DragEventArgs e)
    {
        e.DragEffects = editor?.Asset.IsEditable == true && EditorDragDrop.CurrentDragData is SpriteInfoViewModel[] items && items.All(editor.Sprites.Contains)
            ? DragDropEffects.Move : editor?.Asset.IsEditable == true && e.DataTransfer.TryGetFiles()?.Any() == true ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
    }
    private async void Drop(object? sender, DragEventArgs e)
    {
        if (editor?.Asset.IsEditable != true || cache == null) return;
        if (EditorDragDrop.CurrentDragData is SpriteInfoViewModel[] items)
        {
            var row = SpriteList.GetVisualsAt(e.GetPosition(SpriteList)).OfType<Control>()
                .Select(c => c.FindAncestorOfType<ListBoxItem>(true)).FirstOrDefault(c => c != null);
            var index = row?.DataContext is SpriteInfoViewModel target ? editor.Sprites.IndexOf(target) + (e.GetPosition(row).Y > row.Bounds.Height / 2 ? 1 : 0) : editor.Sprites.Count;
            editor.Reorder(items, index);
            e.Handled = true;
            return;
        }
        await Run(() =>
        {
            editor.ImportFiles(e.DataTransfer.TryGetFiles()?.Select(f => f.TryGetLocalPath()).OfType<string>().Select(p => new UFile(p)) ?? [], cache.GetPixelSize);
            return Task.CompletedTask;
        });
        e.Handled = true;
    }

    private async Task Run(Func<Task> operation)
    {
        try { await operation(); }
        catch (Exception exception)
        {
            if (editor != null) await editor.ServiceProvider.Get<IDialogService>().MessageBoxAsync(exception.Message, MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }
}
