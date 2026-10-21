// Copyright (c) .NET Foundation and Contributors (https://dotnetfoundation.org/ & https://stride3d.net)
// Distributed under the MIT license. See the LICENSE.md file in the project root for more information.

using Avalonia;
using Avalonia.Automation.Peers;
using Avalonia.Automation.Provider;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media.Imaging;
using Avalonia.Media;
using Avalonia.Platform;
using Stride.Core.IO;
using Stride.Assets.Textures;
using Avalonia.Threading;
using Stride.Assets.Entities;
using Stride.Core.Assets.Editor.ViewModels;
using Stride.Core.Assets.Templates;
using Stride.GameStudio.Avalonia.Views;

internal static class EditorAddAssetSmoke
{
    public static async Task Check(SessionViewModel session, Window owner)
    {
        var view = new AssetExplorerView { DataContext = session.AssetCollection };
        var window = new Window { Title = "Add asset browser check", Width = 900, Height = 500,
            Content = new UserControl { Classes = { "editorShell" }, Content = view } };
        window.Show(owner);
        var add = view.FindControl<Button>("AddAssetButton")!;
        var flyout = (Flyout)add.Flyout!;
        var browser = (AddAssetView)flyout.Content!;
        var search = browser.FindControl<TextBox>("SearchBox")!;
        var groups = browser.FindControl<ListBox>("CategoryList")!;
        var templates = browser.FindControl<ListBox>("TemplateList")!;
        var project = session.CurrentProject!;
        var history = session.ActionService!;
        var created = false;
        try
        {
            ((IInvokeProvider)new ButtonAutomationPeer(add)).Invoke();
            await Flush();
            if (!flyout.IsOpen || groups.ItemCount < 10 || templates.ItemCount < 20)
                throw new Exception($"Asset catalog did not load ({groups.ItemCount} categories, {templates.ItemCount} templates).");
            groups.SelectedItem = "Scene";
            await Flush();
            if (groups.ContainerFromIndex(0) is not ListBoxItem firstCategory || firstCategory.Bounds.Height > 24
                || groups.Background is not ISolidColorBrush { Color.A: 0 })
                throw new Exception("Category rows or background do not match the compact WPF browser.");
            var scene = templates.Items.OfType<AddAssetTemplateItem>().First(x => x.Template is TemplateAssetDescription t && t.AssetTypeName == nameof(SceneAsset));
            if (scene.Icon == null || scene.Description == null) throw new Exception("Scene icon or description was not loaded.");
            if (templates.Items.OfType<AddAssetTemplateItem>().Any(x => x.Group != "Scene")) throw new Exception("Category selection leaked templates.");
            search.Text = "normal map";
            await Flush();
            if (groups.IsEnabled || templates.ItemCount == 0
                || templates.Items.OfType<AddAssetTemplateItem>().Any(x => !x.Name.Contains("normal", StringComparison.OrdinalIgnoreCase)))
                throw new Exception("Global multiword search did not ignore the selected category.");
            search.Text = "__missing_template__";
            await Flush();
            if (templates.ItemCount != 0) throw new Exception("Unmatched search did not empty the list.");
            search.Text = "";
            groups.SelectedItem = "Scene";
            await Flush();
            using (var bitmap = new RenderTargetBitmap(new PixelSize(660, 344)))
            {
                bitmap.Render(browser);
                bitmap.Save(Path.Combine(Path.GetTempPath(), "stride-add-asset.png"));
            }
            Console.WriteLine($"PASS: Add asset flyout, {groups.ItemCount} WPF categories, icon/description, global search, empty results and render.");

            session.AssetCollection.SelectedLocations.Clear();
            session.AssetCollection.SelectedLocations.Add(project.AssetMountPoint);
            var before = project.Assets.Select(x => x.Id).ToHashSet();
            templates.SelectedItem = scene;
            // Enter uses the same activation path as a single pointer click.
            templates.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Enter });
            for (var i = 0; i < 100 && !project.Assets.Any(x => !before.Contains(x.Id)); i++) await Task.Delay(100);
            var added = project.Assets.Single(x => !before.Contains(x.Id));
            created = true;
            if (flyout.IsOpen || added.Asset is not SceneAsset || !session.AssetCollection.SelectedAssets.Contains(added))
                throw new Exception("Template activation did not create/select the scene and dismiss the flyout.");
            history.Undo();
            created = false;
            if (!added.IsDeleted || project.Package.Assets.Find(added.Id) != null) throw new Exception("Undo did not remove the scene.");
            history.Redo();
            created = true;
            if (added.IsDeleted || project.Package.Assets.Find(added.Id) == null) throw new Exception("Redo did not restore the scene.");
            history.Undo();
            created = false;
            Console.WriteLine("PASS: Enter activation, real scene creation/selection, popup dismissal and Undo/Redo.");
            flyout.ShowAt(add);
            await Flush();
            if (search.Text != "" || groups.SelectedIndex != -1 || !groups.IsEnabled) throw new Exception("Reopening did not reset the browser.");
            browser.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Escape });
            await Flush();
            if (flyout.IsOpen) throw new Exception("Escape did not dismiss the browser.");
            Console.WriteLine("PASS: browser reset and Escape dismissal; no project save.");

            var sourcePath = Path.Combine(Path.GetTempPath(), "stride-add-asset-" + Guid.NewGuid().ToString("N") + ".png");
            using (var image = AssetLoader.Open(new Uri("avares://Stride.GameStudio.Avalonia/Assets/reimport-24.png")))
            using (var file = File.Create(sourcePath)) image.CopyTo(file);
            session.Closing += (_, _) => File.Delete(sourcePath);
            await browser.ImportFiles([new UFile(sourcePath)]);
            await Flush();
            if (!flyout.IsOpen || templates.ItemCount < 2) throw new Exception("File import did not offer matching templates.");
            var color = templates.Items.OfType<AddAssetTemplateItem>().First(x => x.Name == "Color");
            if (templates.Items.OfType<AddAssetTemplateItem>().Any(x => x.Template is not TemplateAssetDescription { ImportSource: true }))
                throw new Exception("File import offered a template without source support.");
            before = project.Assets.Select(x => x.Id).ToHashSet();
            await browser.Create(color);
            var texture = project.Assets.Single(x => !before.Contains(x.Id));
            created = true;
            if (texture.Asset is not TextureAsset textureAsset || textureAsset.Source != new UFile(sourcePath) || flyout.IsOpen)
                throw new Exception("File import did not create the chosen texture with its source.");
            history.Undo();
            created = false;
            if (!texture.IsDeleted) throw new Exception("Undo did not remove the imported texture.");
            history.Redo();
            created = true;
            if (texture.IsDeleted) throw new Exception("Redo did not restore the imported texture.");
            history.Undo();
            created = false;
            Console.WriteLine("PASS: direct-file matching, template choice, texture creation with source and Undo/Redo.");
        }
        finally
        {
            if (created) history.Undo();
            flyout.Hide();
            window.Close();
        }
    }

    private static async Task Flush()
    {
        await Task.Delay(150);
        await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Background);
    }
}
