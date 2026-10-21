// Copyright (c) .NET Foundation and Contributors (https://dotnetfoundation.org/ & https://stride3d.net)
// Distributed under the MIT license. See the LICENSE.md file in the project root for more information.

using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Stride.Assets.Models;
using Stride.Core.Assets;
using Stride.Core.Assets.Editor.ViewModels;
using Stride.Core.Diagnostics;
using Stride.Core.Presentation.Avalonia.Windows;

internal static class EditorAssetDeleteSmoke
{
    public static async Task Check(SessionViewModel session, Window window)
    {
        var collection = session.AssetCollection;
        var source = session.AllAssets.First(asset => asset.IsEditable && asset.Asset is ModelAsset);
        var sourceBytes = File.ReadAllBytes(source.AssetItem.FullPath);
        collection.SelectAssets(new[] { source });
        var copies = collection.DuplicateSelection();
        var copy = copies.Single();
        var package = copy.Directory.Package;
        package.Package.RootAssets.Add(new AssetReference(copy.Id, copy.Url));
        package.RootAssets.Add(copy);
        var path = (string)copy.AssetItem.FullPath;
        var trashRoot = Path.Combine((string)package.RootDirectory, "obj", "trash-smoke-" + Guid.NewGuid().ToString("N"));
        var originalDataHome = Environment.GetEnvironmentVariable("XDG_DATA_HOME");
        if (OperatingSystem.IsLinux())
        {
            Directory.CreateDirectory(trashRoot);
            Environment.SetEnvironmentVariable("XDG_DATA_HOME", trashRoot);
        }
        try
        {
            Save();
            var copyBytes = File.ReadAllBytes(path);
            await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Background);
            var explorer = ((Control)window.Content!).GetVisualDescendants().OfType<Stride.GameStudio.Avalonia.Views.AssetExplorerView>().Single();
            var list = explorer.GetVisualDescendants().OfType<ListBox>().Single();
            var locations = collection.SelectedLocations.ToArray();
            Press(list, Key.Delete);
            var dialog = await Dialog();
            if (dialog.Owner != window || copy.IsDeleted) throw new Exception("Asset Delete did not wait for a modal confirmation.");
            dialog.ButtonCommand.Execute(2);
            await Task.Delay(100);
            if (copy.IsDeleted || !File.Exists(path)) throw new Exception("Cancel removed an asset or its file.");

            Press(list, Key.Delete);
            (await Dialog()).ButtonCommand.Execute(1);
            for (var i = 0; i < 100 && !copy.IsDeleted; i++) await Task.Delay(25);
            CheckDeleted();
            await CheckList(false);
            if (!collection.SelectedLocations.SequenceEqual(locations)) throw new Exception("Deleting an asset changed the selected folder.");
            if (!File.Exists(path)) throw new Exception("An asset was moved to trash before Save.");
            session.ActionService!.Undo();
            CheckRestored();
            await CheckList(true);
            session.ActionService.Redo();
            CheckDeleted();
            await CheckList(false);

            // A failed trash operation must preserve pending deletion for a later save.
            var backup = path + ".trash-smoke-backup";
            File.Move(path, backup);
            Directory.CreateDirectory(path);
            try
            {
                var errors = new LoggerResult();
                if (session.Save(errors) || !errors.HasErrors || !copy.IsDirty)
                    throw new Exception("A failed trash operation was reported as a successful save or cleared dirty state.");
            }
            finally { Directory.Delete(path); File.Move(backup, path); }
            Save();
            if (File.Exists(path)) throw new Exception("Save did not remove the deleted asset file.");
            if (OperatingSystem.IsLinux())
            {
                var info = Directory.GetFiles(Path.Combine(trashRoot, "Trash", "info"), "*.trashinfo").Single();
                var trashed = Path.Combine(trashRoot, "Trash", "files", Path.GetFileNameWithoutExtension(info));
                if (!File.ReadAllBytes(trashed).SequenceEqual(copyBytes) || !File.ReadAllText(info).Contains("Path="))
                    throw new Exception("The system trash did not retain the asset contents and restore metadata.");
            }
            session.ActionService.Undo();
            CheckRestored();
            await CheckList(true);
            Save();
            if (!File.ReadAllBytes(path).SequenceEqual(copyBytes)) throw new Exception("Undo after Save did not recreate the asset file.");

            collection.SelectAssets(copies);
            var filter = explorer.GetVisualDescendants().OfType<TextBox>().First();
            Press(filter, Key.Delete);
            await Task.Delay(100);
            if (copy.IsDeleted) throw new Exception("Delete in a text field deleted an asset.");
            collection.SelectAssets(session.AllAssets.Where(asset => !asset.IsEditable).Take(1));
            if (collection.DeleteSelectionCommand.CanExecute(null)) throw new Exception("Read-only assets can be deleted.");
            collection.SelectAssets(copies);
            await collection.DeleteSelection(false);
            Save();
            if (!File.ReadAllBytes(source.AssetItem.FullPath).SequenceEqual(sourceBytes)) throw new Exception("Deletion changed the original source asset.");
            collection.SelectAssets(new[] { source });
            Console.WriteLine("PASS: Asset View Delete confirmation/cancel, undo/redo, trash on Save, failed-trash retry, undo after Save and read-only/text-input guards.");

            async Task CheckList(bool visible)
            {
                await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Background);
                window.UpdateLayout();
                if (collection.FilteredAssets.Contains(copy) != visible || list.Items.Contains(copy) != visible
                    || list.GetRealizedContainers().Any(container => ReferenceEquals(container.DataContext, copy)) != visible)
                    throw new Exception($"Asset View did not update immediately after Delete/Undo/Redo (expected visible={visible}).");
            }
        }
        finally
        {
            if (OperatingSystem.IsLinux()) Environment.SetEnvironmentVariable("XDG_DATA_HOME", originalDataHome);
        }

        void Save()
        {
            var log = new LoggerResult();
            if (!session.Save(log)) throw new Exception("Asset deletion smoke Save failed: " + string.Join(" | ", log.Messages));
        }
        void CheckDeleted()
        {
            if (!copy.IsDeleted || session.GetAssetById(copy.Id) != null || copy.Directory.Assets.Contains(copy) || collection.SelectedAssets.Contains(copy)
                || package.RootAssets.Contains(copy) || package.Package.RootAssets.Any(root => root.Id == copy.Id))
                throw new Exception("Deleted asset remains in the session, directory or selection.");
        }
        void CheckRestored()
        {
            if (copy.IsDeleted || session.GetAssetById(copy.Id) != copy || !copy.Directory.Assets.Contains(copy)
                || !package.RootAssets.Contains(copy) || !package.Package.RootAssets.Any(root => root.Id == copy.Id))
                throw new Exception("Undo did not restore the deleted asset.");
        }
        async Task<MessageBox> Dialog()
        {
            var lifetime = (IClassicDesktopStyleApplicationLifetime)Application.Current!.ApplicationLifetime!;
            for (var i = 0; i < 200; i++)
            {
                if (lifetime.Windows.OfType<MessageBox>().FirstOrDefault(dialog => dialog.IsVisible) is { } dialog) return dialog;
                await Task.Delay(25);
            }
            throw new TimeoutException("Asset View Delete did not open confirmation.");
        }
        static void Press(Control target, Key key)
        {
            target.Focus();
            target.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = key });
            target.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyUpEvent, Key = key });
        }
    }
}
