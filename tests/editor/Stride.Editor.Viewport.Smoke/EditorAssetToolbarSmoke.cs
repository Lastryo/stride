// Copyright (c) .NET Foundation and Contributors (https://dotnetfoundation.org/ & https://stride3d.net)
// Distributed under the MIT license. See the LICENSE.md file in the project root for more information.

using System.Reflection;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Interactivity;
using Avalonia.Automation.Peers;
using Avalonia.Automation.Provider;
using Avalonia.Platform;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Stride.Assets.Editor;
using Stride.Assets.Effect;
using Stride.Assets.Textures;
using Stride.Core.Assets;
using Stride.Core.Assets.Editor.ViewModels;
using Stride.Core.Assets.Presentation.ViewModels;
using Stride.Core.Assets.Tracking;
using Stride.Core.IO;
using Stride.Core.Serialization;
using Stride.Core.Storage;
using Stride.GameStudio.Avalonia.Views;
using Stride.Shaders.Compiler;

internal static class EditorAssetToolbarSmoke
{
    public static async Task Check(SessionViewModel session, Window owner)
    {
        var view = new AssetExplorerView { DataContext = session.AssetCollection };
        var window = new Window { Title = "Asset toolbar check", Width = 875, Height = 300,
            Content = new UserControl { Classes = { "editorShell" }, Content = view } };
        window.Show(owner);
        var history = session.ActionService!;
        var project = session.CurrentProject ?? throw new Exception("Select an editable project.");
        var sourcePath = Path.Combine(Path.GetTempPath(), "stride-toolbar-" + Guid.NewGuid().ToString("N") + ".png");
        using (var image = AssetLoader.Open(new Uri("avares://Stride.GameStudio.Avalonia/Assets/reimport-24.png")))
        using (var file = File.Create(sourcePath)) image.CopyTo(file);
        var transactions = history.RetrieveAllTransactions().Count();
        try
        {
            await Flush();
            var update = view.FindControl<Button>("UpdateSourcesButton")!;
            var effects = view.FindControl<Button>("ImportEffectsButton")!;
            var edit = view.FindControl<Button>("EditAssetButton")!;
            if (!ReferenceEquals(update.Command, session.SourceTracker.UpdateAllAssetsWithModifiedSourceCommand)
                || !ReferenceEquals(effects.Command, session.ImportEffectLogCommand)
                || !ReferenceEquals(edit.Command, session.EditSelectedContentCommand))
                throw new Exception("Toolbar commands are not bound to the session.");

            // A new texture with an outdated source hash exercises the real source tracker.
            var source = new UFile(sourcePath);
            var texture = new TextureAsset { Source = source };
            SourceHashesHelper.UpdateHashes(texture, new Dictionary<UFile, ObjectId> { [source] = ObjectId.Empty });
            AssetViewModel asset;
            using (history.CreateTransaction())
                asset = project.AddAsset(new AssetItem("ToolbarSource_" + Guid.NewGuid().ToString("N"), texture), project.AssetMountPoint);
            for (var i = 0; i < 100 && session.SourceTracker.GetCurrentHash(source) == ObjectId.Empty; i++)
                await Task.Delay(100);
            if (session.SourceTracker.GetCurrentHash(source) == ObjectId.Empty) throw new Exception("Source tracker did not hash the texture.");
            asset.Sources.UpdateUsedHashes([source]);
            await Flush();
            if (!asset.Sources.NeedUpdateFromSource || !update.IsEnabled) throw new Exception("Changed source did not enable refresh.");
            Click(update);
            for (var i = 0; i < 300 && asset.Sources.NeedUpdateFromSource; i++) await Task.Delay(100);
            await Flush();
            if (asset.Sources.NeedUpdateFromSource) throw new Exception("Refresh did not record the source hash.");
            history.Undo();
            if (!asset.Sources.NeedUpdateFromSource) throw new Exception("Undo did not restore the pending source update.");
            history.Redo();
            if (asset.Sources.NeedUpdateFromSource) throw new Exception("Redo did not restore the imported source hash.");
            Console.WriteLine("PASS: toolbar bindings, real source tracking, source refresh and Undo/Redo.");

            // Inject the same wire packet the router delivers, without requiring a running game.
            var serviceType = typeof(StrideEditorPlugin).Assembly.GetType("Stride.Assets.Editor.EffectCompilerServerSession")!;
            var service = session.ServiceProvider.Get(serviceType);
            var handler = serviceType.GetMethod("HandleEffectCompilerRequestedPacket", BindingFlags.NonPublic | BindingFlags.Instance)!;
            var request = new EffectCompileRequest("ToolbarSmoke_" + Guid.NewGuid().ToString("N"), new CompilerParameters());
            using var stream = new MemoryStream();
            var writer = new BinarySerializationWriter(stream);
            writer.Context.SerializerSelector = SerializerSelector.AssetWithReuse;
            writer.SerializeExtended(ref request, ArchiveMode.Serialize, null);
            var packet = new RemoteEffectCompilerEffectRequested { Request = stream.ToArray() };
            handler.Invoke(service, [packet, project]);
            await Flush();
            if (session.ImportEffectLogPendingCount != 1 || !effects.IsEnabled) throw new Exception("Remote effect did not enable import.");
            Click(effects);
            await Flush();
            var effect = session.AllAssets.First(x => x.Asset is EffectLogAsset && x.Name == EffectLogAsset.DefaultFile);
            if (!((EffectLogAsset)effect.Asset).Text.Contains(request.EffectName) || session.ImportEffectLogPendingCount != 0 || effects.IsEnabled)
                throw new Exception("Import did not update the effect library and reset the pending counter.");
            history.Undo();
            await Flush();
            if (session.ImportEffectLogPendingCount != 1 || !effects.IsEnabled) throw new Exception("Undo did not restore pending effects.");
            history.Redo();
            await Flush();
            handler.Invoke(service, [packet, project]);
            await Flush();
            if (session.ImportEffectLogPendingCount != 0) throw new Exception("An imported effect was offered again.");
            Console.WriteLine("PASS: remote effect packet, import, pending counter, duplicate suppression and Undo/Redo.");

            var options = view.FindControl<Button>("ViewOptionsButton")!;
            options.Flyout!.ShowAt(options);
            await Flush();
            var menu = (MenuFlyout)options.Flyout;
            var sortByType = menu.Items.OfType<MenuItem>().First(x => Equals(x.Header, "Type"));
            sortByType.Command!.Execute(sortByType.CommandParameter);
            if (session.AssetCollection.SortRule != SortRule.TypeOrderThenName) throw new Exception("View options did not sort assets.");
            var zoomIn = menu.Items.OfType<MenuItem>().First(x => Equals(x.Header, "Zoom in"));
            zoomIn.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
            await Flush();
            if (view.ThumbnailSize != 112 || view.GetVisualDescendants().OfType<Stride.GameStudio.Avalonia.Controls.AssetTilePanel>().Single().ThumbnailSize != 112
                || view.GetVisualDescendants().OfType<AssetCard>().Any(x => x.ThumbnailSize != 112))
                throw new Exception("Zoom did not resize the thumbnails and their slots together.");
            view.ThumbnailSize = 96;
            options.Flyout.Hide();
            session.AssetCollection.AssetFilterPattern = "ToolbarSource_";
            var filter = session.AssetCollection.AvailableAssetFilters.First(x => x.Category == FilterCategory.AssetName);
            session.AssetCollection.AddAssetFilterCommand.Execute(filter);
            if (!session.AssetCollection.CurrentAssetFilters.Contains(filter)) throw new Exception("Name filter was not added.");
            filter.ToggleIsActiveCommand.Execute(null);
            if (filter.IsActive) throw new Exception("Filter did not toggle off.");
            filter.RemoveFilterCommand.Execute(filter);
            if (session.AssetCollection.CurrentAssetFilters.Contains(filter)) throw new Exception("Filter was not removed.");
            session.AssetCollection.AssetFilterPattern = "";
            await Flush();
            using var bitmap = new RenderTargetBitmap(new Avalonia.PixelSize(875, 300));
            bitmap.Render(window);
            bitmap.Save(Path.Combine(Path.GetTempPath(), "stride-asset-toolbar.png"));
            Console.WriteLine("PASS: view menu, sorting, thumbnail zoom, name filter and toolbar render at 875 pixels.");
        }
        finally
        {
            var addedTransactions = history.RetrieveAllTransactions().Count() - transactions;
            for (var i = 0; i < addedTransactions && history.CanUndo; i++) history.Undo();
            window.Close();
            File.Delete(sourcePath);
        }
    }

    private static void Click(Button button) => ((IInvokeProvider)new ButtonAutomationPeer(button)).Invoke();
    private static async Task Flush()
    {
        await Task.Delay(100);
        await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Background);
    }
}
