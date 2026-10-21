// Copyright (c) .NET Foundation and Contributors (https://dotnetfoundation.org/ & https://stride3d.net)
// Distributed under the MIT license. See the LICENSE.md file in the project root for more information.

using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.Platform.Storage;
using Avalonia.VisualTree;
using Stride.Core.Assets.Editor.ViewModels;
using Stride.Core.Assets.Templates;
using Stride.Core.Diagnostics;
using Stride.Core.IO;
using Stride.Core.Presentation.Services;
using Stride.Core.Presentation.Windows;

namespace Stride.GameStudio.Avalonia.Views;

public sealed record AddAssetTemplateItem(TemplateDescription Template, IImage? Icon)
{
    public string Name => Template.Name;
    public string? Description => Template.Description;
    public string Group => GetGroup(Template);

    public static string GetGroup(TemplateDescription template) => template is TemplateAssetDescription { AssetTypeName: "PrefabAsset" }
        ? "Prefabs" : string.IsNullOrWhiteSpace(template.Group) ? "General" : template.Group.Split('/')[0];
}

public partial class AddAssetView : UserControl
{
    private List<AddAssetTemplateItem> templates = [];
    private IReadOnlyList<UFile> sourceFiles = [];
    private AssetCollectionViewModel? collection;
    private Flyout? flyout;
    private Control? placementTarget;
    private bool resumeAfterImport;

    public AddAssetView() => InitializeComponent();

    public void Open(AssetCollectionViewModel assets, Flyout owner, Control target)
    {
        collection = assets;
        flyout = owner;
        placementTarget = target;
        if (resumeAfterImport)
        {
            resumeAfterImport = false;
            Dispatcher.UIThread.Post(() => SearchBox.Focus(), DispatcherPriority.Input);
            return;
        }
        foreach (var item in templates) (item.Icon as IDisposable)?.Dispose();
        templates = assets.Session.FindTemplates(TemplateScope.Asset).OrderBy(t => t.Order).ThenBy(t => t.Name)
            .Select(t => new AddAssetTemplateItem(t, LoadIcon(t) ?? new Bitmap(global::Avalonia.Platform.AssetLoader.Open(new Uri("avares://Stride.GameStudio.Avalonia/Assets/default-template-icon.png"))))).ToList();
        sourceFiles = [];
        SearchBox.Text = "";
        CategoryList.ItemsSource = templates.Select(t => t.Group).Distinct().OrderBy(g => g).ToArray();
        CategoryList.SelectedIndex = -1;
        Refresh();
        Dispatcher.UIThread.Post(() => SearchBox.Focus(), DispatcherPriority.Input);
    }

    private static Bitmap? LoadIcon(TemplateDescription template)
    {
        if (template.Icon == null || template.TemplateDirectory == null) return null;
        var path = UPath.Combine(template.TemplateDirectory, template.Icon).ToOSPath();
        try { return File.Exists(path) ? new Bitmap(path) : null; }
        catch (Exception e) when (e is IOException or ArgumentException or NotSupportedException)
        {
            GlobalLogger.GetLogger("Asset templates").Warning($"Unable to load template icon '{path}'.", e);
            return null;
        }
    }

    private void Refresh()
    {
        var tokens = (SearchBox.Text ?? "").Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        CategoryList.IsEnabled = tokens.Length == 0;
        var group = CategoryList.SelectedItem as string;
        TemplateList.ItemsSource = templates.Where(t =>
            (tokens.Length > 0 || group == null || t.Group == group)
            && tokens.All(token => t.Name.Contains(token, StringComparison.OrdinalIgnoreCase))
            && (sourceFiles.Count == 0 || sourceFiles.Any(file => CanImport(t.Template, file)))).ToArray();
        TemplateList.SelectedIndex = -1;
    }

    private static bool CanImport(TemplateDescription template, UFile file) => template is TemplateAssetDescription { ImportSource: true } asset
        && asset.GetSupportedExtensions().Contains(file.GetFileExtension() ?? "");

    private void SearchChanged(object? sender, TextChangedEventArgs e)
    {
        if (CategoryList == null || TemplateList == null) return;
        if (!string.IsNullOrWhiteSpace(SearchBox.Text)) CategoryList.SelectedIndex = -1;
        Refresh();
    }

    private void CategoryChanged(object? sender, SelectionChangedEventArgs e) => Refresh();

    private void CategoryHovered(object? sender, PointerEventArgs e)
    {
        if (CategoryList.IsEnabled && sender is Control { DataContext: string group }) CategoryList.SelectedItem = group;
    }

    private async void TemplateReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (e.InitialPressMouseButton == MouseButton.Left && e.Source is global::Avalonia.Visual source
            && source.FindAncestorOfType<ListBoxItem>(true)?.DataContext is AddAssetTemplateItem item)
            await Create(item);
    }

    private async void OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            e.Handled = true;
            flyout?.Hide();
        }
        else if (e.Key == Key.Enter)
        {
            var item = TemplateList.SelectedItem as AddAssetTemplateItem
                ?? TemplateList.Items.OfType<AddAssetTemplateItem>().FirstOrDefault();
            if (item == null) return;
            e.Handled = true;
            await Create(item);
        }
        else if (e.Key == Key.Right && CategoryList.IsKeyboardFocusWithin && TemplateList.ItemCount > 0)
        {
            e.Handled = true;
            TemplateList.SelectedIndex = 0;
            TemplateList.Focus();
        }
        else if (e.Key == Key.Left && TemplateList.IsKeyboardFocusWithin && CategoryList.IsEnabled)
        {
            e.Handled = true;
            CategoryList.Focus();
        }
        else if (SearchBox.IsKeyboardFocusWithin && e.Key == Key.Down && TemplateList.ItemCount > 0)
        {
            e.Handled = true;
            TemplateList.SelectedIndex = 0;
            TemplateList.Focus();
        }
    }

    public async Task Create(AddAssetTemplateItem item)
    {
        if (collection == null) return;
        flyout?.Hide();
        try
        {
            IReadOnlyList<UFile> files = sourceFiles.Where(file => CanImport(item.Template, file)).ToArray();
            if (files.Count == 0 && item.Template is TemplateAssetDescription { ImportSource: true } description)
            {
                files = await PickFiles(description);
                while (files.Count == 0)
                {
                    var choice = await collection.ServiceProvider.Get<IDialogService>().MessageBoxAsync(
                        $"Do you want to create this {description.Name} without a source file?",
                        [new DialogButtonInfo { Content = "Create", Result = 1 },
                         new DialogButtonInfo { Content = "Select a source...", Result = 2, IsDefault = true },
                         new DialogButtonInfo { Content = "Cancel", Result = 3, IsCancel = true }]);
                    if (choice == 1) break;
                    if (choice != 2) return;
                    files = await PickFiles(description);
                }
            }
            await collection.Session.CreateAssetFromTemplate(item.Template, sourceFiles: files);
        }
        catch (Exception e) { GlobalLogger.GetLogger("Asset templates").Error($"Unable to create '{item.Name}'.", e); }
    }

    private async Task<IReadOnlyList<UFile>> PickFiles(TemplateAssetDescription? description = null)
    {
        var provider = TopLevel.GetTopLevel(placementTarget ?? this)?.StorageProvider;
        if (provider == null) return [];
        var options = new FilePickerOpenOptions { Title = "Import assets", AllowMultiple = true };
        if (description != null)
        {
            var extensions = description.GetSupportedExtensions();
            options.FileTypeFilter = [new FilePickerFileType(extensions.Description)
            {
                Patterns = extensions.SingleExtensions.Select(extension => "*" + extension).ToArray(),
            }, FilePickerFileTypes.All];
        }
        var files = await provider.OpenFilePickerAsync(options);
        return files.Select(f => f.Path.IsFile ? new UFile(f.Path.LocalPath) : null).OfType<UFile>().ToArray();
    }

    private async void ImportClicked(object? sender, RoutedEventArgs e)
    {
        flyout?.Hide();
        try
        {
            var files = await PickFiles();
            if (files.Count > 0) await ImportFiles(files);
        }
        catch (Exception exception) { GlobalLogger.GetLogger("Asset templates").Error("Unable to import files.", exception); }
    }

    public async Task ImportFiles(IReadOnlyList<UFile> files)
    {
        sourceFiles = files;
        SearchBox.Text = "";
        CategoryList.SelectedIndex = -1;
        Refresh();
        if (TemplateList.ItemCount == 0)
        {
            if (collection != null) await collection.ServiceProvider.Get<IDialogService>().MessageBoxAsync("These files aren't supported.");
        }
        else if (TemplateList.ItemCount == 1 && TemplateList.Items[0] is AddAssetTemplateItem item)
            await Create(item);
        else if (flyout != null && placementTarget != null)
        {
            resumeAfterImport = true;
            flyout.ShowAt(placementTarget);
        }
    }
}
