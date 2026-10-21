// Copyright (c) .NET Foundation and Contributors (https://dotnetfoundation.org/ & https://stride3d.net)
// Distributed under the MIT license. See the LICENSE.md file in the project root for more information.

using Avalonia;
using Avalonia.Controls;
using Stride.Assets;
using Stride.Assets.Editor.ViewModels;
using Stride.Assets.Entities;
using Stride.Assets.Presentation.ViewModels;
using Stride.Assets.UI;
using Stride.Core.Assets;
using Stride.Core.Assets.Editor.ViewModels;
using Stride.Core.Assets.Editor.Services;
using Stride.Core.Mathematics;
using Stride.Engine;
using Stride.Games;
using Stride.Assets.Editor.EntityHierarchyEditor.Game;
using Stride.Rendering;
using Stride.Rendering.UI;
using Canvas = Stride.UI.Panels.Canvas;

internal static class EditorSceneUISmoke
{
    public static async Task Check(SessionViewModel session)
    {
        var settings = session.ServiceProvider.Get<Stride.Editor.Build.GameSettingsProviderService>().CurrentGameSettings;
        var expectedSettings = session.CurrentProject!.Package.GetGameSettingsAssetOrDefault();
        if (!ReferenceEquals(settings, expectedSettings))
            throw new Exception("Game settings provider did not follow the current project.");
        var square = new Canvas { Width = 160, Height = 160, BackgroundColor = Color.Lime,
            HorizontalAlignment = Stride.UI.HorizontalAlignment.Center, VerticalAlignment = Stride.UI.VerticalAlignment.Center };
        var root = new Stride.UI.Panels.Grid { BackgroundColor = Color.Red, Children = { square } };
        var page = new UIPageAsset();
        page.Hierarchy.RootParts.Add(root);
        page.Hierarchy.Parts.Add(new UIElementDesign(root));
        page.Hierarchy.Parts.Add(new UIElementDesign(square));
        var project = session.CurrentProject!;
        UIPageViewModel pageAsset;
        SceneViewModel sceneAsset;
        var suffix = Guid.NewGuid().ToString("N");
        using (session.ActionService!.CreateTransaction())
        {
            pageAsset = (UIPageViewModel)project.AddAsset(new AssetItem("SceneUIPage_" + suffix, page), project.AssetMountPoint);
            var entity = new Entity("Scene UI") { new UIComponent { Page = ContentReferenceHelper.CreateReference<UIPage>(pageAsset), IsFullScreen = true, ResolutionStretch = ResolutionStretch.FixedWidthFixedHeight } };
            var scene = new SceneAsset();
            scene.Hierarchy.RootParts.Add(entity);
            scene.Hierarchy.Parts.Add(new EntityDesign(entity));
            sceneAsset = (SceneViewModel)project.AddAsset(new AssetItem("SceneUI_" + suffix, scene), project.AssetMountPoint);
        }
        session.EditorCollection.OpenAssetEditor(sceneAsset);
        var editor = (EntityHierarchyEditorViewModel)session.EditorCollection.ActiveEditor!;
        await editor.OpenViewport().WaitAsync(TimeSpan.FromSeconds(90));
        var viewport = editor.Viewport!;
        viewport.GizmosVisible = false;
        viewport.GridVisible = false;
        var game = viewport.Controller.Game;
        var capture = new CaptureSystem(game);
        await viewport.Controller.InvokeAsync(() => game.GameSystems.Add(capture));
        var ui = await viewport.Controller.InvokeAsync(() => game.ActiveContentScene!.Entities.Single().Get<UIComponent>());
        if (ui.Page?.RootElement == null) throw new Exception("Scene UIComponent.Page asset was not loaded.", viewport.LastException);
        await CheckPixels("Group0 UI in Transparent stage");
        // The AnimatedModel sample routes Group1 UI to a dedicated stage used only by Game.
        await viewport.Controller.InvokeAsync(() => ui.RenderGroup = RenderGroup.Group1);
        await CheckPixels("Group1 UI from project compositor");
        await viewport.Controller.InvokeAsync(() => ui.Enabled = false);
        await Task.Delay(300);
        if (await RedPixels() > .7) throw new Exception("Disabled UI remained visible.");
        await viewport.Controller.InvokeAsync(() => ui.Enabled = true);
        await CheckPixels("Re-enabled scene UI");
        await CheckResize();
        if (viewport.LastException != null) throw viewport.LastException;
        await session.EditorCollection.CloseEditorAsync(editor).WaitAsync(TimeSpan.FromSeconds(15));
        Console.WriteLine("PASS: Scene UI Page asset loading, fullscreen Group0/Group1 pixels, disable/enable and close.");

        async Task CheckResize()
        {
            var host = (Control)viewport.EditorHost!;
            var originalWidth = host.Width;
            var originalHeight = host.Height;
            var resolution = await viewport.Controller.InvokeAsync(() => ui.Resolution);
            try
            {
                foreach (var mode in new[] { ResolutionStretch.FixedWidthAdaptableHeight, ResolutionStretch.FixedHeightAdaptableWidth, ResolutionStretch.FixedWidthFixedHeight })
                {
                    await viewport.Controller.InvokeAsync(() => ui.ResolutionStretch = mode);
                    foreach (var size in new[] { new Size(320, 240), new Size(240, 480), new Size(400, 240) })
                    {
                        host.Width = size.Width;
                        host.Height = size.Height;
                        var scaling = TopLevel.GetTopLevel(host)!.RenderScaling;
                        var ready = false;
                        for (var retry = 0; retry < 100; retry++)
                        {
                            await Task.Delay(50);
                            ready = await viewport.Controller.InvokeAsync(() => game.GraphicsDevice.Presenter.BackBuffer.Width == (int)Math.Ceiling(size.Width * scaling)
                                && game.GraphicsDevice.Presenter.BackBuffer.Height == (int)Math.Ceiling(size.Height * scaling));
                            if (ready) break;
                        }
                        if (!ready) throw new Exception("UI test viewport did not resize.");
                        await CheckPixels($"{mode} at {size}");
                        var actual = await viewport.Controller.InvokeAsync(() => ui.Page.RootElement.RenderSize);
                        var expected = new Vector2(resolution.X, resolution.Y);
                        if (mode == ResolutionStretch.FixedWidthAdaptableHeight) expected.Y = expected.X * (float)(size.Height / size.Width);
                        if (mode == ResolutionStretch.FixedHeightAdaptableWidth) expected.X = expected.Y * (float)(size.Width / size.Height);
                        if (Vector2.Distance(new Vector2(actual.X, actual.Y), expected) > .01f)
                            throw new Exception($"UI layout/projection disagree: {mode} at {size}, layout={actual}, expected={expected}.");
                        var bounds = capture.GreenBounds;
                        var expectedAspect = mode == ResolutionStretch.FixedWidthFixedHeight
                            ? (size.Width / resolution.X) / (size.Height / resolution.Y) : 1;
                        if (bounds.Width < 3 || bounds.Height < 3 || Math.Abs(bounds.Width / (double)bounds.Height / expectedAspect - 1) > .08)
                            throw new Exception($"UI square distorted: {mode} at {size}, pixels={bounds}, expected aspect={expectedAspect}.");
                        if (await viewport.Controller.InvokeAsync(() => ui.Resolution) != resolution)
                            throw new Exception("UI resize changed the component's configured resolution.");
                    }
                }
            }
            finally { host.Width = originalWidth; host.Height = originalHeight; }
            Console.WriteLine("PASS: UI resize layout and square pixels in both adaptive modes; fixed resolution behavior preserved.");
        }

        async Task CheckPixels(string label)
        {
            await viewport.Controller.InvokeAsync(() => capture.CapturePath = $"/tmp/stride-scene-ui-{ui.RenderGroup}.png");
            for (var i = 0; i < 50; i++)
            {
                await Task.Delay(100);
                if (await RedPixels() > .7) { Console.WriteLine("PASS: " + label); return; }
                if (viewport.LastException != null) throw viewport.LastException;
            }
            var state = await viewport.Controller.InvokeAsync(() => $"page={ui.Page?.RootElement?.GetType().Name}, group={ui.RenderGroup}, selectors={string.Join(';', game.SceneSystem.GraphicsCompositor.RenderFeatures.OfType<UIRenderFeature>().SelectMany(f => f.RenderStageSelectors.OfType<SimpleGroupToRenderStageSelector>()).Select(s => $"{s.RenderGroup}:{s.RenderStage?.Name}"))}");
            throw new Exception(label + " did not render. " + state);
        }
        async Task<double> RedPixels()
        {
            var pending = new TaskCompletionSource<double>(TaskCreationOptions.RunContinuationsAsynchronously);
            await viewport.Controller.InvokeAsync(() => capture.Pending = pending);
            return await pending.Task.WaitAsync(TimeSpan.FromSeconds(5));
        }
    }

    private sealed class CaptureSystem(SceneViewportGame game) : GameSystemBase(game.Services)
    {
        public TaskCompletionSource<double>? Pending;
        public string? CapturePath;
        public Stride.Core.Mathematics.Rectangle GreenBounds;

        public override void Initialize()
        {
            Visible = true;
            DrawOrder = int.MaxValue - 1;
        }

        public override void Draw(GameTime gameTime)
        {
            if (Pending is not { } pending) return;
            Pending = null;
            try
            {
                var buffer = game.GraphicsDevice.Presenter.BackBuffer;
                using var staging = buffer.ToStaging();
                var pixels = new Color[buffer.Width * buffer.Height];
                buffer.GetData(game.GraphicsContext.CommandList, staging, pixels);
                var minX = buffer.Width;
                var minY = buffer.Height;
                var maxX = -1;
                var maxY = -1;
                for (var y = 0; y < buffer.Height; y++)
                for (var x = 0; x < buffer.Width; x++)
                {
                    var pixel = pixels[y * buffer.Width + x];
                    if (pixel.G < 180 || pixel.R > 30 || pixel.B > 30) continue;
                    minX = Math.Min(minX, x); minY = Math.Min(minY, y);
                    maxX = Math.Max(maxX, x); maxY = Math.Max(maxY, y);
                }
                GreenBounds = new Stride.Core.Mathematics.Rectangle(minX, minY, Math.Max(0, maxX - minX + 1), Math.Max(0, maxY - minY + 1));
                var fraction = pixels.Count(c => c.R > 180 && c.G < 30 && c.B < 30) / (double)pixels.Length;
                if (fraction > .7 && CapturePath is { } path)
                {
                    CapturePath = null;
                    using var stream = System.IO.File.Create(path);
                    buffer.Save(game.GraphicsContext.CommandList, stream, staging, Stride.Graphics.ImageFileType.Png);
                }
                pending.TrySetResult(fraction);
            }
            catch (Exception exception) { pending.TrySetException(exception); }
        }
    }
}
