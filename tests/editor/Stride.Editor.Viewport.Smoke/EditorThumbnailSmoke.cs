// Copyright (c) .NET Foundation and Contributors (https://dotnetfoundation.org/ & https://stride3d.net)
// Distributed under the MIT license. See the LICENSE.md file in the project root for more information.

using Avalonia.Controls;
using Avalonia.VisualTree;
using Stride.Assets.Entities;
using Stride.Assets.Materials;
using Stride.Assets.Models;
using Stride.Assets.Rendering;
using Stride.Assets.Textures;
using Stride.Core.Assets.Editor.Services;
using Stride.Core.Assets.Editor.ViewModels;
using Stride.Core.Assets.Presentation.ViewModels;
using Stride.Core.Storage;
using Stride.Editor.Thumbnails;
using Stride.GameStudio.Avalonia.Converters;
using Stride.GameStudio.Avalonia.Controls;
using Stride.GameStudio.Avalonia.Views;
using Stride.Graphics;
using Image = Stride.Graphics.Image;

internal static class EditorThumbnailSmoke
{
    private static ThumbnailData[] retained = [];
    private static GameStudioThumbnailService? service;

    public static void CheckPixels()
    {
        foreach (var format in new[] { PixelFormat.R8G8B8A8_UNorm, PixelFormat.B8G8R8A8_UNorm, PixelFormat.R8G8B8A8_UNorm_SRgb, PixelFormat.B8G8R8A8_UNorm_SRgb })
        {
            var image = Image.New2D(3, 2, 1, format, rowStride: 32);
            var rgba = format == PixelFormat.R8G8B8A8_UNorm || format == PixelFormat.R8G8B8A8_UNorm_SRgb;
            var buffer = image.PixelBuffer[0];
            for (var y = 0; y < 2; y++)
                for (var x = 0; x < 3; x++)
                {
                    var pixel = new byte[] { (byte)(rgba ? 180 : 60), (byte)(40 + y * 80), (byte)(rgba ? 60 : 180), 128 };
                    System.Runtime.InteropServices.Marshal.Copy(pixel, 0, buffer.DataPointer + y * buffer.RowStride + x * 4, 4);
                }
            using var bitmap = StrideImage.CreateBitmap(image) ?? throw new Exception("A supported thumbnail format was rejected.");
            image.Dispose();
            using var stream = new MemoryStream();
            bitmap.Save(stream);
            stream.Position = 0;
            using var decoded = SkiaSharp.SKBitmap.Decode(stream);
            for (var y = 0; y < 2; y++)
            {
                var pixel = decoded.GetPixel(2, y);
                if (Math.Abs(pixel.Red - 180) > 2 || Math.Abs(pixel.Green - (40 + y * 80)) > 2 || Math.Abs(pixel.Blue - 60) > 2 || pixel.Alpha != 128)
                    throw new Exception($"Thumbnail stride/channel/alpha failure for {format}, row {y}: {pixel}.");
            }
        }
        using var unsupported = Image.New2D(2, 2, 1, PixelFormat.R16_Float);
        if (StrideImage.CreateBitmap(unsupported) != null) throw new Exception("An unsupported thumbnail format was accepted.");
        Console.WriteLine("PASS: thumbnail RGBA/BGRA, padded row stride, alpha and copied pixel ownership.");
    }

    public static async Task Check(SessionViewModel session, Window window)
    {
        CheckPixels();
        await CheckQueue(session.ServiceProvider.Get<AssetBuilderService>());
        using (var stream = new MemoryStream([1, 2, 3]))
        {
            var invalid = new BitmapThumbnailData(ObjectId.Empty, stream);
            await invalid.PrepareForPresentation(session.Dispatcher);
            if (!invalid.HasError || invalid.Presenter != null || stream.CanRead)
                throw new Exception("An invalid thumbnail did not report failure and release its stream.");
            invalid.Destroy();
        }
        var failed = new ResourceThumbnailData(ObjectId.Empty, "Stride.Editor.Resources.appbar.checkmark.cross.png") { HasError = true };
        await failed.PrepareForPresentation(session.Dispatcher);
        if (!failed.HasError || failed.Presenter is not Image) throw new Exception("The error thumbnail could not be presented.");
        failed.Destroy();
        Console.WriteLine("PASS: invalid image streams report errors; the failure icon is presentable and resources are released.");
        service = session.ServiceProvider.Get<IThumbnailService>() as GameStudioThumbnailService
            ?? throw new Exception("The thumbnail service was not registered.");
        if (service.HasStaticThumbnail(typeof(ModelAsset)) || service.HasStaticThumbnail(typeof(TextureAsset)) || service.HasStaticThumbnail(typeof(MaterialAsset)))
            throw new Exception("A dynamic asset fell back to the generic static thumbnail compiler.");
        var assets = new[] { typeof(SceneAsset), typeof(Stride.Assets.GameSettingsAsset), typeof(GraphicsCompositorAsset), typeof(ModelAsset), typeof(TextureAsset), typeof(MaterialAsset) }
            .Select(type => session.AllAssets.FirstOrDefault(asset => type.IsInstanceOfType(asset.Asset))
                ?? throw new Exception($"Missing asset for type {type.Name}; available types: {string.Join(", ", session.AllAssets.Select(a => a.Asset.GetType().Name).Distinct())}"))
            .ToArray();
        for (var i = 0; i < 600 && assets.Any(asset => asset.ThumbnailData?.Presenter == null); i++)
        {
            if (assets.Any(asset => asset.ThumbnailData?.HasError == true))
                throw new Exception("Thumbnail generation failed: " + string.Join(", ", assets.Where(asset => asset.ThumbnailData?.HasError == true).Select(asset => asset.Url)));
            if (i % 50 == 0) Console.WriteLine($"Waiting for thumbnails: {service.PendingThumbnailCount}; missing {string.Join(", ", assets.Where(asset => asset.ThumbnailData?.Presenter == null).Select(asset => asset.Url))}");
            await Task.Delay(200);
        }
        foreach (var asset in assets)
        {
            if (asset.ThumbnailData?.HasError != false || asset.ThumbnailData.Presenter is not Image image)
                throw new Exception($"No ready thumbnail for {asset.Url}.");
            using var bitmap = StrideImage.CreateBitmap(image) ?? throw new Exception($"Unsupported thumbnail format for {asset.Url}: {image.Description.Format}.");
            bitmap.Save(Path.Combine(Path.GetTempPath(), "stride-thumbnail-" + asset.AssetType.Name + ".png"));
        }
        Console.WriteLine("PASS: Scene, GameSettings, GraphicsCompositor, Model, Texture and Material thumbnails reached presentation.");
        var scene = assets[0];
        var previous = scene.ThumbnailData;
        var wasDirty = scene.IsDirty;
        var history = session.ActionService!.RetrieveAllTransactions().Select(transaction => transaction.Id).ToArray();
        session.Thumbnails.ForceRefreshThumbnails([scene]);
        for (var i = 0; i < 200 && (ReferenceEquals(scene.ThumbnailData, previous) || scene.ThumbnailData?.Presenter == null); i++) await Task.Delay(100);
        if (ReferenceEquals(scene.ThumbnailData, previous) || scene.ThumbnailData?.Presenter == null || previous!.Presenter != null)
            throw new Exception("Refreshing a thumbnail failed to replace and release its previous image.");
        if (scene.IsDirty != wasDirty || !session.ActionService.RetrieveAllTransactions().Select(transaction => transaction.Id).SequenceEqual(history))
            throw new Exception("Refreshing a thumbnail changed the asset or polluted Undo history.");
        session.AssetCollection.SelectAssetCommand.Execute(scene);
        await Task.Delay(200);
        var references = session.References.DisplayedReferences;
        var reference = references.Assets.FirstOrDefault() ?? throw new Exception("The scene reference list is empty.");
        var referenceCount = references.FilteredAssets.Count;
        references.AddAssetFilterCommand.Execute(new AssetFilterViewModel(references, FilterCategory.AssetName, reference.Name, reference.Name));
        if (references.FilteredAssets.Count == 0 || references.FilteredAssets.Any(asset => !asset.Name.Contains(reference.Name, StringComparison.OrdinalIgnoreCase)))
            throw new Exception("The References name filter did not filter its own asset collection.");
        references.ClearAssetFiltersCommand.Execute();
        if (references.FilteredAssets.Count != referenceCount) throw new Exception("Clearing References filters lost assets.");
        var shell = (Control)window.Content!;
        var list = shell.GetVisualDescendants().OfType<AssetExplorerView>().Single().GetVisualDescendants().OfType<ListBox>().Single();
        list.SelectedItem = scene;
        await Task.Delay(100);
        if (list.ContainerFromItem(scene) is not ListBoxItem { IsSelected: true } || !session.AssetCollection.SelectedAssets.Contains(scene))
            throw new Exception("The asset card selection is not synchronized with the asset collection.");
        shell.FindControl<TabControl>("PreviewTabs")!.SelectedIndex = 2;
        await Task.Delay(200);
        if (!shell.GetVisualDescendants().OfType<AssetCard>().Any() || !shell.GetVisualDescendants().OfType<ThumbnailImage>().Any(image => image.Source != null))
            throw new Exception("No asset card rendered a thumbnail.");
        retained = session.AllAssets.Select(asset => asset.ThumbnailData).OfType<ThumbnailData>().ToArray();
        Console.WriteLine("PASS: refresh releases the old image; shared cards display bitmaps; References filters clear without losing assets.");
    }

    public static void CheckReleased()
    {
        if (retained.Any(data => data.Presenter != null) || service?.PendingThumbnailCount != 0)
            throw new Exception("Closing the session retained thumbnail images or queued work.");
        Console.WriteLine("PASS: closing the session released thumbnail images and queued work.");
    }

    private static async Task CheckQueue(AssetBuilderService builder)
    {
        using var entered = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        var running = new GateBuildUnit(entered, release) { PriorityMajor = int.MinValue };
        var runningNode = builder.PushBuildUnit(running);
        try
        {
            for (var i = 0; i < 200 && !entered.IsSet; i++) await Task.Delay(50);
            if (!entered.IsSet) throw new Exception("The queue regression test did not start.");
            if (builder.TryRemoveBuildUnit(runningNode)) throw new Exception("A dequeued build unit was reported as removable.");
            var queued = builder.PushBuildUnit(new GateBuildUnit(null, null) { PriorityMajor = int.MinValue + 1 });
            var next = typeof(AssetBuilderService).GetMethod("GetNextBuildStep", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!;
            if (next.Invoke(builder, [int.MinValue]) != null || queued.Index < 0)
                throw new Exception("Priority rejection removed the build unit from the queue.");
            if (!builder.TryRemoveBuildUnit(queued)) throw new Exception("A queued build unit could not be removed.");
        }
        finally { release.Set(); }
        await running.Wait().WaitAsync(TimeSpan.FromSeconds(10));
        Console.WriteLine("PASS: priority rejection retains queued work; removal distinguishes queued and running build units.");
    }

    private sealed class GateBuildUnit(ManualResetEventSlim? entered, ManualResetEventSlim? release)
        : AssetBuildUnit(new AssetBuildUnitIdentifier(Guid.NewGuid(), default))
    {
        protected override Stride.Core.BuildEngine.ListBuildStep Prepare()
        {
            entered?.Set();
            if (release != null && !release.Wait(TimeSpan.FromSeconds(15))) throw new TimeoutException("Queue gate was not released.");
            return new Stride.Core.BuildEngine.ListBuildStep();
        }
    }
}
