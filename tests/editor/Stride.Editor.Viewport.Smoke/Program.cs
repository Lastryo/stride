// Copyright (c) .NET Foundation and Contributors (https://dotnetfoundation.org/ & https://stride3d.net)
// Distributed under the MIT license. See the LICENSE.md file in the project root for more information.

using System.Reflection;
using Stride.Graphics;
using Avalonia.Input;
using Avalonia.Media.Imaging;
using Stride.Editor.Avalonia.Engine;
using Stride.Core.Assets.Editor.Services;
using Stride.Editor.Avalonia.Preview;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Controls;
using Stride.Assets.Editor.Gizmos;
using Avalonia.Threading;
using Microsoft.Build.Locator;
using Stride.Assets.Editor.EntityHierarchyEditor.Game;
using Stride.Assets.Editor.GameEditor.Services;
using Stride.Assets.Editor.GameEditor;
using Stride.Core;
using Stride.Core.Extensions;
using Stride.Core.Mathematics;
using Stride.Assets.Editor.ViewModels;
using Stride.Assets.Presentation.ViewModels;
using Stride.Core.Assets.Editor.ViewModels;
using Stride.Core.Diagnostics;
using Stride.Engine;
using Stride.GameStudio.Avalonia;

if (args.Length == 0) throw new ArgumentException("Pass a temporary copy of a project's .sln file.");
MSBuildLocator.RegisterDefaults();
System.Runtime.CompilerServices.RuntimeHelpers.RunModuleConstructor(typeof(Stride.Assets.Models.ModelAsset).Module.ModuleHandle);
GlobalLogger.GlobalMessageLogged += message => Console.WriteLine(message);
AppBuilder.Configure<App>().UsePlatformDetect()
    .With(new X11PlatformOptions { RenderingMode = args.Contains("--glx-presentation")
        ? [X11RenderingMode.Glx, X11RenderingMode.Software]
        : [X11RenderingMode.Vulkan, X11RenderingMode.Glx, X11RenderingMode.Software] })
    .WithInterFont().LogToTrace()
    .AfterSetup(_ => Dispatcher.UIThread.Post(async () =>
    {
        try { await ViewportSmoke.CheckViewport(); }
        catch (Exception exception)
        {
            Console.Error.WriteLine("FAIL: " + exception);
            Environment.ExitCode = 1;
            var lifetime = (IClassicDesktopStyleApplicationLifetime)Application.Current!.ApplicationLifetime!;
            var model = lifetime.MainWindow?.DataContext;
            if (model?.GetType().GetProperty("Session")?.GetValue(model) is SessionViewModel session)
            {
                try { await session.CloseAsync().WaitAsync(TimeSpan.FromSeconds(15)); }
                catch (Exception cleanup) { Console.Error.WriteLine("Cleanup failed: " + cleanup); }
            }
            lifetime.Shutdown(1);
        }
    }))
    .StartWithClassicDesktopLifetime(args);

internal static class ViewportSmoke
{
    public static async Task CheckViewport()
    {
        var startupLifetime = (IClassicDesktopStyleApplicationLifetime)Application.Current!.ApplicationLifetime!;
        if (startupLifetime.Args?.Contains("--color-picker") == true)
        {
            await EditorColorPickerSmoke.Check(startupLifetime.MainWindow!);
            startupLifetime.Shutdown();
            return;
        }
        if (startupLifetime.Args?.Contains("--startup-layout-only") == true)
        {
            await Task.Delay(1000);
            await EditorLayoutSmoke.CheckStartup(startupLifetime.MainWindow!);
            startupLifetime.Shutdown();
            return;
        }
        await CheckBackgroundLogging();
        CheckScenePreviewTemplate();
        var lifetime = (IClassicDesktopStyleApplicationLifetime)Application.Current!.ApplicationLifetime!;
        SessionViewModel? session = null;
        SceneViewModel? scene = null;
        for (var i = 0; i < 240 && scene is null; i++)
        {
            var model = lifetime.MainWindow?.DataContext;
            session = model?.GetType().GetProperty("Session")?.GetValue(model) as SessionViewModel;
            scene = session?.AllAssets.OfType<SceneViewModel>().FirstOrDefault();
            if (i % 20 == 0) Console.WriteLine($"Waiting for session: {session != null}; scene: {scene?.Url}");
            if (scene is null) await Task.Delay(250);
        }
        if (scene is null || session is null) throw new TimeoutException("No scene became available in the loaded project.");
        Console.WriteLine($"Loaded {session.AllPackages.Count()} packages and {session.AllAssets.Count()} assets.");
        if (lifetime.Args?.Contains("--scene-ui") == true)
        {
            await EditorSceneUISmoke.Check(session);
            await session.CloseAsync();
            lifetime.Shutdown();
            return;
        }
        if (lifetime.Args?.Contains("--ui-polish") == true)
        {
            await EditorUIPolishSmoke.Check(session, lifetime.MainWindow!);
            await session.CloseAsync();
            lifetime.Shutdown();
            return;
        }
        if (lifetime.Args?.Contains("--ui-library") == true)
        {
            await EditorUILibrarySmoke.Check(session, lifetime.MainWindow!);
            await session.CloseAsync();
            lifetime.Shutdown();
            return;
        }
        if (lifetime.Args?.Contains("--ui-page") == true)
        {
            await EditorUIPageSmoke.Check(session, lifetime.MainWindow!);
            await session.CloseAsync();
            lifetime.Shutdown();
            return;
        }
        if (lifetime.Args?.Contains("--graphics-compositor") == true)
        {
            await EditorGraphicsCompositorSmoke.Check(session, lifetime.MainWindow!);
            await session.CloseAsync();
            lifetime.Shutdown();
            return;
        }
        if (lifetime.Args?.Contains("--asset-build-status") == true)
        {
            await EditorAssetBuildStatusSmoke.Check(session, lifetime.MainWindow!);
            await session.CloseAsync();
            lifetime.Shutdown();
            return;
        }
        if (lifetime.Args?.Contains("--prefabs") == true)
        {
            await EditorPrefabSmoke.Check(session, lifetime.MainWindow!);
            await session.CloseAsync();
            lifetime.Shutdown();
            return;
        }
        if (lifetime.Args?.Contains("--add-asset") == true)
        {
            await EditorAddAssetSmoke.Check(session, lifetime.MainWindow!);
            await session.CloseAsync();
            lifetime.Shutdown();
            return;
        }
        if (lifetime.Args?.Contains("--asset-toolbar") == true)
        {
            await EditorAssetToolbarSmoke.Check(session, lifetime.MainWindow!);
            await session.CloseAsync();
            lifetime.Shutdown();
            return;
        }
        if (lifetime.Args?.Contains("--asset-cards") == true)
        {
            await EditorAssetCardSmoke.Check(session, lifetime.MainWindow!);
            await session.CloseAsync();
            lifetime.Shutdown();
            return;
        }
        if (lifetime.Args?.Contains("--startup-layout") == true)
        {
            await EditorLayoutSmoke.CheckStartup(lifetime.MainWindow!);
            if (lifetime.Args.Contains("--startup-presentation"))
                await EditorLayoutSmoke.CheckStartupPresentation(lifetime.MainWindow!);
        }
        Console.WriteLine("Opening scene: " + scene.Url);
        session.EditorCollection.OpenAssetEditor(scene);
        var editor = (EntityHierarchyEditorViewModel)session.EditorCollection.ActiveEditor!;
        await editor.OpenViewport().WaitAsync(TimeSpan.FromSeconds(90));
        var viewport = editor.Viewport!;
        if (!viewport.SceneInitialized || viewport.EditorHost is null)
            throw new Exception("The viewport did not initialize its scene and Avalonia image.");
        var field = typeof(EditorGameController<EntityHierarchyEditorGame>).GetField("Game", BindingFlags.NonPublic | BindingFlags.Instance)!;
        var game = (SceneViewportGame)field.GetValue(viewport.Controller)!;
        game.ExceptionThrown += (_, args) => Console.Error.WriteLine("Viewport game exception: " + args.Exception);
        Console.WriteLine("Viewport initialized.");
        if (lifetime.Args?.Contains("--project-build") == true)
        {
            await EditorProjectBuildSmoke.Check(session, lifetime.MainWindow!);
            lifetime.Shutdown();
            return;
        }
        if (lifetime.Args?.Contains("--diagnostics") == true)
        {
            await EditorDiagnosticsSmoke.Check(session, lifetime.MainWindow!);
            lifetime.Shutdown();
            return;
        }
        if (lifetime.Args?.Contains("--close-confirmation") == true)
        {
            await EditorCloseSmoke.Check(session, editor, lifetime.MainWindow!, lifetime,
                lifetime.Args.Contains("--discard"));
            lifetime.Shutdown();
            return;
        }
        if (lifetime.Args?.Contains("--save") == true)
        {
            await EditorSaveSmoke.Check(session, editor, lifetime.MainWindow!);
            await session.CloseAsync();
            lifetime.Shutdown();
            return;
        }
        if (lifetime.Args?.Contains("--camera-modes") == true)
            await EditorCameraModesSmoke.Check(editor, game, lifetime.MainWindow!).WaitAsync(TimeSpan.FromSeconds(90));
        if (lifetime.Args?.Contains("--property-type") == true)
            await EditorPropertyTypeSmoke.Check(editor, lifetime.MainWindow!);
        if (lifetime.Args?.Contains("--component-gizmos") == true)
        {
            await EditorTextValidationSmoke.Check((Control)viewport.EditorHost!);
            await EditorComponentGizmoSmoke.Check(editor, game).WaitAsync(TimeSpan.FromSeconds(90));
        }
        if (lifetime.Args?.Contains("--orientation") == true)
            await EditorOrientationSmoke.Check(editor, game).WaitAsync(TimeSpan.FromSeconds(30));
        if (lifetime.Args?.Contains("--viewport-tools") == true)
            await EditorViewportToolsSmoke.Check(editor, game, lifetime.MainWindow!).WaitAsync(TimeSpan.FromSeconds(30));
        if (lifetime.Args?.Contains("--thumbnails") == true)
            await EditorThumbnailSmoke.Check(session, lifetime.MainWindow!);
        if (lifetime.Args?.Contains("--startup-layout") == true)
            await EditorLayoutSmoke.CheckStartup(lifetime.MainWindow!);
        if (lifetime.Args?.Contains("--shell-layout") == true)
            await EditorLayoutSmoke.Check(lifetime.MainWindow!);
        if (lifetime.Args?.Contains("--hierarchy-layout") == true)
            await EditorHierarchySmoke.Check(editor, lifetime.MainWindow!);
        if (lifetime.Args?.Contains("--selection-tools") == true)
            await EditorSelectionToolsSmoke.Check(editor, lifetime.MainWindow!);
        if (lifetime.Args?.Contains("--asset-delete") == true)
            await EditorAssetDeleteSmoke.Check(session, lifetime.MainWindow!);
        if (lifetime.Args?.Contains("--presentation-stability") == true)
        {
            await Task.Delay(3000);
            await CheckPresentedFrames((AvaloniaGameView)viewport.EditorHost);
        }
        if (lifetime.Args?.Contains("--pipeline-gc-stress") == true)
            await CheckPipelineCreationDuringGc(viewport, game);
        var modelCount = await viewport.Controller.InvokeAsync(() => CountModels(game.ContentScene));
        if (modelCount == 0) throw new Exception("The scene loaded without any model meshes.");
        if (lifetime.Args?.Contains("--inspector-layout") == true)
        {
            await CheckInspectorWidth(editor, lifetime.MainWindow!, 922, 1030);
            await CheckInspectorWidth(editor, lifetime.MainWindow!, 1280, 720);
            await CheckInspectorWidth(editor, lifetime.MainWindow!, 1920, 1080);
        }
        if (lifetime.Args?.Contains("--inspector-performance") == true)
            await CheckInspectorPerformance(editor, lifetime.MainWindow!);
        var firstFrame = await viewport.Controller.InvokeAsync(() => game.DrawTime.FrameCount);
        var entityItem = editor.HierarchyRoot.InnerSubEntities.First(item => item.Components.OfType<ModelComponent>().Any());
        var assetModelBeforeSelection = entityItem.AssetSideEntity.Get<ModelComponent>()!.Model;
        var animationsBeforeSelection = entityItem.AssetSideEntity.Get<AnimationComponent>()?.Animations
            .Where(pair => pair.Value != null).Select(pair => pair.Key).ToArray() ?? [];
        if (assetModelBeforeSelection == null) throw new Exception("The asset has no model before selecting its entity.");
        editor.SelectedContent.Clear();
        editor.SelectedContent.Add(entityItem);
        await Task.Delay(1500);
        if (entityItem.AssetSideEntity.Get<ModelComponent>()!.Model == null)
            throw new Exception("Selecting the entity cleared the asset's model reference.");
        if (animationsBeforeSelection.Any(key => entityItem.AssetSideEntity.Get<AnimationComponent>()!.Animations[key] == null))
            throw new Exception("Selecting the entity cleared an animation asset reference.");
        var selectedModelCount = await viewport.Controller.InvokeAsync(() => CountModels(game.ContentScene));
        if (selectedModelCount != modelCount)
            throw new Exception($"Selecting the entity removed rendered models: {modelCount} -> {selectedModelCount}.");
        Console.WriteLine("PASS: selecting CharacterModel preserves its asset reference and rendered meshes.");
        if (lifetime.Args?.Contains("--capture-shell") == true)
        {
            var root = (Control)lifetime.MainWindow!.Content!;
            var scaling = TopLevel.GetTopLevel(root)!.RenderScaling;
            using var capture = new RenderTargetBitmap(new PixelSize((int)Math.Ceiling(root.Bounds.Width * scaling), (int)Math.Ceiling(root.Bounds.Height * scaling)), new global::Avalonia.Vector(96 * scaling, 96 * scaling));
            capture.Render(root);
            var capturePath = Path.Combine(Path.GetTempPath(), "stride-editor-shell.png");
            capture.Save(capturePath);
            Console.WriteLine("Editor shell capture: " + capturePath);
        }
        await viewport.Controller.InvokeAsync(() =>
        {
            var roots = game.EditorServices.Get<EditorGameEntitySelectionService>()!.GetSelectedRootIds();
            if (!roots.Contains(entityItem.Id)) throw new Exception("Hierarchy selection did not reach the viewport.");
        });
        foreach (var mode in Enum.GetValues<Transformation>())
        {
            viewport.ActiveTransformation = mode;
            await viewport.Controller.InvokeAsync(() =>
            {
                var active = (TransformationGizmo)typeof(EditorGameEntityTransformService).GetField("active", BindingFlags.Instance | BindingFlags.NonPublic)!
                    .GetValue(viewport.Controller.GetTransformService())!;
                var expected = mode switch { Transformation.Translation => typeof(TranslationGizmo), Transformation.Rotation => typeof(RotationGizmo), _ => typeof(ScaleGizmo) };
                if (active.GetType() != expected || !active.IsEnabled)
                    throw new Exception("The requested transform gizmo is not active for the selected entity.");
            });
        }
        viewport.ActiveTransformation = Transformation.Translation;
        Console.WriteLine("PASS: Move, Rotate and Scale modes accept the selected entity.");
        await CheckGizmoCapture(viewport, game);
        var original = new TransformationTRS(entityItem.AssetSideEntity.Transform);
        // Unique positions force compilation instead of reusing a previous run's cached scene.
        for (var move = 0; move < 2; move++)
        {
            var modified = original;
            modified.Position += new Vector3(1 + Random.Shared.NextSingle(), 0, 0);
            viewport.CommitTransformations(new Dictionary<AbsoluteId, TransformationTRS> { [entityItem.Id] = modified });
            if (entityItem.AssetSideEntity.Transform.Position != modified.Position || session.ActionService?.CanUndo != true)
                throw new Exception("The gizmo transformation was not recorded in the asset undo stack.");
            await viewport.ReloadContent().WaitAsync(TimeSpan.FromSeconds(60));
            await viewport.Controller.InvokeAsync(() =>
            {
                var entity = game.FindSubEntity(Guid.Empty, entityItem.AssetSideEntity.Id);
                if (entity == null || entity.Transform.Position != modified.Position || CountModels(game.ContentScene) != modelCount)
                    throw new Exception("Moving the selected entity lost its scene, model or new position during reload.");
            });
            if (entityItem.AssetSideEntity.Scene != null)
                throw new Exception("Compiling the scene attached live asset entities to a runtime scene.");
            Console.WriteLine("PASS: moved entity, model and new position survived scene reload without mutating the asset hierarchy.");
            session.ActionService.Undo();
            if (entityItem.AssetSideEntity.Transform.Position != original.Position)
                throw new Exception("Undo did not restore the asset transformation.");
            await viewport.ReloadContent().WaitAsync(TimeSpan.FromSeconds(60));
            await viewport.Controller.InvokeAsync(() =>
            {
                var entity = game.FindSubEntity(Guid.Empty, entityItem.AssetSideEntity.Id);
                if (entity == null || entity.Transform.Position != original.Position || CountModels(game.ContentScene) != modelCount)
                    throw new Exception("Undo did not restore the rendered entity after reload.");
            });
            Console.WriteLine("PASS: hierarchy selection and undoable asset transformation, including scene reload.");
        }
        await CheckEventRouting(viewport, game, lifetime.MainWindow!, session);
        await Task.Delay(3000);
        if (((Control)viewport.EditorHost).Bounds.Width <= 32) throw new Exception("The Avalonia image was not laid out in Scene view.");
        var bufferSize = await viewport.Controller.InvokeAsync(() => game.GraphicsDevice.Presenter.BackBuffer.Size);
        Console.WriteLine("Viewport backbuffer: " + bufferSize);
        var frame = await viewport.Controller.InvokeAsync(() => game.DrawTime.FrameCount);
        if (frame <= firstFrame) throw new Exception("The render loop did not advance.");
        if (viewport.LastException is { } exception) throw new Exception("The render loop faulted.", exception);
        Console.WriteLine($"PASS: Avalonia image, content scene and {modelCount} hydrated model components; render loop stayed alive.");
        if (lifetime.Args?.Contains("--document-tabs") == true)
            await EditorDocumentSmoke.Check(editor, game, lifetime.MainWindow!);
        if (lifetime.Args?.Contains("--keep-open") == true) return;
        await session.CloseAsync().WaitAsync(TimeSpan.FromSeconds(60));
        if (lifetime.Args?.Contains("--thumbnails") == true)
            EditorThumbnailSmoke.CheckReleased();
        await viewport.Controller.GameStopped.WaitAsync(TimeSpan.FromSeconds(10));
        Console.WriteLine("PASS: viewport game stopped and disposed.");
        lifetime.Shutdown();
    }

    private static async Task CheckPipelineCreationDuringGc(SceneViewportViewModel viewport, SceneViewportGame game)
    {
        if (GraphicsDevice.Platform != GraphicsPlatform.Vulkan) throw new NotSupportedException("This check requires Vulkan.");
        await Task.Delay(1000);
        using var stop = new CancellationTokenSource();
        var collector = Task.Run(() =>
        {
            while (!stop.IsCancellationRequested)
            {
                GC.Collect(2, GCCollectionMode.Forced, blocking: true, compacting: true);
                System.Threading.Thread.Sleep(1);
            }
        });
        try
        {
            await viewport.Controller.InvokeAsync(() =>
            {
                var states = (System.Collections.IDictionary)typeof(GraphicsDevice)
                    .GetField("CachedPipelineStates", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(game.GraphicsDevice)!;
                var field = typeof(PipelineState).GetField("Description", BindingFlags.Instance | BindingFlags.NonPublic)!;
                var descriptions = states.Values.Cast<PipelineState>()
                    .Select(state => (PipelineStateDescription)field.GetValue(state)!)
                    .Where(description => description.RootSignature != null && description.EffectBytecode?.Stages?.Length == 2).ToArray();
                var template = descriptions.FirstOrDefault(description => description.EffectBytecode!.HashSources?
                    .Any(source => source.Key.Contains("LuminanceLogShader")) == true) ?? descriptions.First();
                Console.WriteLine("GC stress shader: " + template.EffectBytecode!.Stages[0].Id);
                for (var i = 0; i < 48; i++)
                {
                    var description = template.Clone();
                    description.EffectBytecode = new Stride.Shaders.EffectBytecode
                    {
                        Reflection = template.EffectBytecode!.Reflection,
                        HashSources = template.EffectBytecode.HashSources,
                        Stages = template.EffectBytecode.Stages.Select(stage =>
                        {
                            var copy = stage.Clone();
                            var garbage = new byte[64 * 1024];
                            copy.EntryPoint = stage.EntryPoint.ToArray();
                            GC.KeepAlive(garbage);
                            return copy;
                        }).ToArray(),
                    };
                    description.RasterizerState.DepthBias = 1000 + i;
                    using var pipeline = PipelineState.New(game.GraphicsDevice, description);
                }
            }).WaitAsync(TimeSpan.FromSeconds(90));
            Console.WriteLine("PASS: 48 native Vulkan pipelines created during compacting GC.");
        }
        finally { stop.Cancel(); await collector; }
    }

    private static async Task CheckInspectorPerformance(EntityHierarchyEditorViewModel editor, Window window)
    {
        var items = editor.HierarchyRoot.InnerSubEntities.Where(item => item.Components.OfType<CameraComponent>().Any()).Take(2).ToArray();
        if (items.Length != 2) throw new Exception("The inspector performance sample needs two camera entities.");
        var originalTransforms = items.Select(item => new TransformationTRS(item.AssetSideEntity.Transform)).ToArray();
        editor.SelectedContent.Clear();
        editor.SelectedContent.Add(items[0]);
        await Task.Delay(500);
        var view = global::Avalonia.VisualTree.VisualExtensions.GetVisualDescendants(window)
            .OfType<Stride.Core.Presentation.Avalonia.Controls.PropertyView>().Single();
        var samples = new List<double>();
        var reused = new List<int>();
        for (var sample = 0; sample < 12; sample++)
        {
            var oldControls = global::Avalonia.VisualTree.VisualExtensions.GetVisualDescendants(view).OfType<Control>().ToHashSet();
            var oldGraph = editor.EditorProperties.ViewModel;
            var timer = System.Diagnostics.Stopwatch.StartNew();
            var preparation = 0.0;
            var swap = 0.0;
            var layouts = 0;
            var prepared = 0;
            var swapStart = 0.0;
            var partialFrame = false;
            var properties = editor.EditorProperties;
            System.ComponentModel.PropertyChangingEventHandler changing = (_, e) => { if (e.PropertyName == "ViewModel") { preparation = timer.Elapsed.TotalMilliseconds; swapStart = preparation; } };
            System.ComponentModel.PropertyChangedEventHandler changed = (_, e) => { if (e.PropertyName == "ViewModel") swap = timer.Elapsed.TotalMilliseconds - swapStart; };
            EventHandler layout = (_, _) =>
            {
                layouts++;
                if (properties.CanDisplayProperties)
                    partialFrame |= global::Avalonia.VisualTree.VisualExtensions.GetVisualDescendants(view).OfType<Control>()
                        .Any(control => control.IsEffectivelyVisible && control.DataContext is Stride.Core.Presentation.Quantum.ViewModels.NodeViewModel node
                            && !ReferenceEquals(node.Root, properties.ViewModel!.RootNode));
            };
            EventHandler<Stride.Core.Presentation.Avalonia.Controls.PropertyViewItemEventArgs> prepare = (_, _) => prepared++;
            properties.PropertyChanging += changing;
            properties.PropertyChanged += changed;
            view.LayoutUpdated += layout;
            view.PrepareItem += prepare;
            try
            {
                editor.SelectedContent.Clear();
                editor.SelectedContent.Add(items[(sample + 1) % 2]);
                for (var retry = 0; retry < 2000 && (ReferenceEquals(properties.ViewModel, oldGraph) || !properties.CanDisplayProperties); retry++) await Task.Delay(1);
                if (ReferenceEquals(properties.ViewModel, oldGraph) || !properties.CanDisplayProperties) throw new Exception("Inspector did not finish switching cameras.");
                await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Background);
                var rows = global::Avalonia.VisualTree.VisualExtensions.GetVisualDescendants(view)
                    .OfType<Stride.Core.Presentation.Avalonia.Controls.PropertyViewItem>().ToArray();
                if (partialFrame) throw new Exception("A rendered inspector layout mixed property graphs from two entities.");
                if (rows.Length < 15 || rows.Any(row => row.DataContext is not Stride.Core.Presentation.Quantum.ViewModels.NodeViewModel node
                    || !ReferenceEquals(node.Root, properties.ViewModel!.RootNode)))
                    throw new Exception("Reused inspector rows are missing or still reference the previous property graph.");
                var positionEditor = global::Avalonia.VisualTree.VisualExtensions.GetVisualDescendants(view)
                    .OfType<Stride.Core.Presentation.Avalonia.Controls.Vector3Editor>()
                    .Single(control => control.DataContext is Stride.Core.Presentation.Quantum.ViewModels.NodeViewModel { Name: nameof(TransformComponent.Position) });
                if (positionEditor.Value != items[(sample + 1) % 2].AssetSideEntity.Transform.Position)
                    throw new Exception($"Reused position editor value {positionEditor.Value} differs from entity {items[(sample + 1) % 2].AssetSideEntity.Transform.Position}, node={((Stride.Core.Presentation.Quantum.ViewModels.NodeViewModel)positionEditor.DataContext!).NodeValue}.");
                if (items.Where((item, index) => item.AssetSideEntity.Transform.Position != originalTransforms[index].Position
                    || item.AssetSideEntity.Transform.Rotation != originalTransforms[index].Rotation
                    || item.AssetSideEntity.Transform.Scale != originalTransforms[index].Scale).Any())
                    throw new Exception("Switching the inspector changed an entity's transform without an edit.");
                if (global::Avalonia.VisualTree.VisualExtensions.GetVisualDescendants(view).OfType<Control>()
                    .Any(control => control.DataContext is Stride.Core.Presentation.Quantum.ViewModels.NodeViewModel node
                        && !ReferenceEquals(node.Root, properties.ViewModel!.RootNode)))
                    throw new Exception("A reused editor still references the previous property graph.");
                var elapsed = timer.Elapsed.TotalMilliseconds;
                var retained = global::Avalonia.VisualTree.VisualExtensions.GetVisualDescendants(view).OfType<Control>().Count(oldControls.Contains);
                if (sample >= 2 && retained < oldControls.Count * 0.8)
                    throw new Exception("Compatible inspector templates were rebuilt instead of reused.");
                samples.Add(elapsed);
                reused.Add(retained);
                Console.WriteLine($"INSPECTOR PERF {sample}: total={elapsed:F2}ms prepare={preparation:F2}ms swap={swap:F2}ms layout/dispatch={elapsed - preparation - swap:F2}ms layouts={layouts} prepared={prepared} retained={retained}/{oldControls.Count}");
            }
            finally
            {
                properties.PropertyChanging -= changing;
                properties.PropertyChanged -= changed;
                view.LayoutUpdated -= layout;
                view.PrepareItem -= prepare;
            }
        }
        Console.WriteLine($"INSPECTOR PERF SUMMARY median={samples.Order().ElementAt(samples.Count / 2):F2}ms max={samples.Max():F2}ms retained-min={reused.Min()}");
        var transformRow = global::Avalonia.VisualTree.VisualExtensions.GetVisualDescendants(view)
            .OfType<Stride.Core.Presentation.Avalonia.Controls.PropertyViewItem>()
            .Single(row => row.DataContext is Stride.Core.Presentation.Quantum.ViewModels.NodeViewModel node && node.NodeValue is TransformComponent);
        transformRow.SetCurrentValue(Stride.Core.Presentation.Avalonia.Controls.ExpandableItemsControl.IsExpandedProperty, false);
        foreach (var item in items.Reverse())
        {
            var oldGraph = editor.EditorProperties.ViewModel;
            editor.SelectedContent.Clear();
            editor.SelectedContent.Add(item);
            for (var retry = 0; retry < 1000 && (ReferenceEquals(editor.EditorProperties.ViewModel, oldGraph) || !editor.EditorProperties.CanDisplayProperties); retry++) await Task.Delay(1);
            await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Background);
            var retainedTransform = global::Avalonia.VisualTree.VisualExtensions.GetVisualDescendants(view)
                .OfType<Stride.Core.Presentation.Avalonia.Controls.PropertyViewItem>()
                .Single(row => row.DataContext is Stride.Core.Presentation.Quantum.ViewModels.NodeViewModel node && node.NodeValue is TransformComponent);
            if (!ReferenceEquals(retainedTransform, transformRow) || retainedTransform.IsExpanded)
                throw new Exception("A compatible collapsed Transform row was replaced or reopened during selection.");
        }
        transformRow.SetCurrentValue(Stride.Core.Presentation.Avalonia.Controls.ExpandableItemsControl.IsExpandedProperty, true);
        await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Background);
        Console.WriteLine("PASS: common Transform row and its collapsed state survive selection changes.");
        var positionControl = global::Avalonia.VisualTree.VisualExtensions.GetVisualDescendants(view)
            .OfType<Stride.Core.Presentation.Avalonia.Controls.Vector3Editor>()
            .Single(control => control.DataContext is Stride.Core.Presentation.Quantum.ViewModels.NodeViewModel { Name: nameof(TransformComponent.Position) });
        positionControl.SetCurrentValue(Stride.Core.Presentation.Avalonia.Controls.Vector3Editor.XProperty, originalTransforms[0].Position.X + 0.125f);
        await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Background);
        if (items[0].AssetSideEntity.Transform.Position.X != originalTransforms[0].Position.X + 0.125f
            || items[1].AssetSideEntity.Transform.Position != originalTransforms[1].Position || editor.Session.ActionService?.CanUndo != true)
            throw new Exception("Editing a reused position field did not update only the selected entity with Undo support.");
        await editor.Viewport!.ReloadContent().WaitAsync(TimeSpan.FromSeconds(60));
        editor.Session.ActionService.Undo();
        await editor.Viewport.ReloadContent().WaitAsync(TimeSpan.FromSeconds(60));
        await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Background);
        if (items[0].AssetSideEntity.Transform.Position != originalTransforms[0].Position || positionControl.Value != originalTransforms[0].Position)
            throw new Exception("Undo did not restore the reused field and its selected entity.");
        Console.WriteLine("PASS: reused position field edits only the selected entity; Undo restores its value and scene.");

    }

    private static async Task CheckInspectorWidth(EntityHierarchyEditorViewModel editor, Window window, int width, int height)
    {
        var root = (Control)window.Content!;
        var oldWidth = root.Width;
        var oldHeight = root.Height;
        EventHandler? changed = null;
        var widths = new List<double>();
        var hiddenLayouts = 0;
        try
        {
            root.Width = width;
            root.Height = height;
            var items = editor.HierarchyRoot.InnerSubEntities.Take(8).ToArray();
            editor.SelectedContent.Clear();
            editor.SelectedContent.Add(items[0]);
            await Task.Delay(500);
            var properties = global::Avalonia.VisualTree.VisualExtensions.GetVisualDescendants(root)
                .OfType<Stride.Core.Presentation.Avalonia.Controls.PropertyView>().Single();
            var scroll = global::Avalonia.VisualTree.VisualExtensions.GetVisualDescendants(properties).OfType<ScrollViewer>().First();
            var panel = global::Avalonia.VisualTree.VisualExtensions.GetVisualDescendants(root)
                .OfType<Stride.GameStudio.Avalonia.Views.PropertyGridView>().Single();
            var expectedPanelBounds = panel.Bounds;
            var panelChanged = false;
            changed = (_, _) =>
            {
                panelChanged |= panel.Bounds != expectedPanelBounds;
                if (editor.SelectedContent.Count > 0 && !properties.IsEffectivelyVisible)
                    hiddenLayouts++;
                if (properties.IsEffectivelyVisible)
                    widths.Add(scroll.Viewport.Width);
            };
            root.LayoutUpdated += changed;
            foreach (var item in items.Concat(items))
            {
                editor.SelectedContent.Clear();
                editor.SelectedContent.Add(item);
                await Task.Delay(200);
            }
            if (panelChanged) throw new Exception($"Inspector panel bounds changed during selection at {width}x{height}.");
            if (hiddenLayouts > 0)
                throw new Exception($"Inspector disappeared during {hiddenLayouts} layout passes with a non-empty hierarchy selection at {width}x{height}.");
            if (widths.Count == 0 || widths.Max() - widths.Min() > 0.5)
                throw new Exception($"Inspector content width changed during hierarchy selection at {width}x{height}: {string.Join(", ", widths.Distinct())}.");
            foreach (var item in items)
            {
                editor.SelectedContent.Clear();
                editor.SelectedContent.Add(item);
                await Task.Yield();
            }
            for (var retry = 0; retry < 100 && (!editor.EditorProperties.CanDisplayProperties || editor.EditorProperties.Name != items[^1].Name); retry++) await Task.Delay(20);
            if (!editor.EditorProperties.CanDisplayProperties || editor.EditorProperties.Name != items[^1].Name || editor.EditorProperties.ViewModel == null
                || !ReferenceEquals(editor.EditorProperties.Selection?.SingleOrDefault(), items[^1]))
                throw new Exception("Rapid hierarchy selection did not leave the latest object's properties displayed.");
            if (hiddenLayouts > 0 || panelChanged) throw new Exception("Rapid hierarchy selection hid or resized the inspector.");
            editor.SelectedContent.Clear();
            for (var retry = 0; retry < 100 && editor.EditorProperties.ViewModel != null; retry++) await Task.Delay(20);
            if (editor.EditorProperties.ViewModel != null || editor.EditorProperties.CanDisplayProperties)
                throw new Exception("Clearing selection retained the previous object's property graph.");
            Console.WriteLine($"PASS: inspector stayed visible and {widths[0]} pixels wide across 16 selections at {width}x{height}; rapid selection and deselection worked.");
        }
        finally
        {
            if (changed != null) root.LayoutUpdated -= changed;
            root.Width = oldWidth;
            root.Height = oldHeight;
        }
        await Task.Delay(100);
    }

    private static async Task CheckGizmoCapture(SceneViewportViewModel viewport, SceneViewportGame game)
    {
        var view = (AvaloniaGameView)viewport.EditorHost!;
        var pointer = new Avalonia.Input.Pointer(101, PointerType.Mouse, true);
        view.RaiseEvent(new PointerEventArgs(InputElement.PointerMovedEvent, view, pointer, view, new global::Avalonia.Point(-100, -100), 0,
            new PointerPointProperties(), KeyModifiers.None));
        view.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.LeftCtrl });
        await Task.Delay(100);
        var initialized = typeof(TransformationGizmo).GetField("transformationInitialized", BindingFlags.Instance | BindingFlags.NonPublic)!;
        var axes = typeof(TransformationGizmo).GetProperty(nameof(TransformationGizmo.TransformationAxes))!;
        var updateAxes = typeof(TransformationGizmo).GetMethod("UpdateTransformationAxisBase", BindingFlags.Instance | BindingFlags.NonPublic)!;
        var transform = typeof(TransformationGizmo).GetMethod("TransformSceneEntityBase", BindingFlags.Instance | BindingFlags.NonPublic)!;
        foreach (var mode in Enum.GetValues<Transformation>())
        {
            viewport.ActiveTransformation = mode;
            await viewport.Controller.InvokeAsync(() =>
            {
                var gizmo = (TransformationGizmo)typeof(EditorGameEntityTransformService).GetField("active", BindingFlags.Instance | BindingFlags.NonPublic)!
                    .GetValue(viewport.Controller.GetTransformService())!;
                // Start at an already captured axis so the regression does not depend on screen geometry.
                axes.SetValue(gizmo, GizmoTransformationAxes.X);
                initialized.SetValue(gizmo, true);
                try
                {
                    updateAxes.Invoke(gizmo, null);
                    if (gizmo.TransformationAxes != GizmoTransformationAxes.X)
                        throw new Exception($"{mode} lost its captured axis when the pointer left the gizmo.");
                    ((Task)transform.Invoke(gizmo, null)!).GetAwaiter().GetResult();
                    if ((bool)initialized.GetValue(gizmo)!) throw new Exception($"{mode} retained capture after button release.");
                    updateAxes.Invoke(gizmo, null);
                    if (gizmo.IsUnderMouse()) throw new Exception($"{mode} did not resume hover picking after release.");
                }
                finally { initialized.SetValue(gizmo, false); gizmo.ClearTransformationAxes(); }
            });
        }
        viewport.ActiveTransformation = Transformation.Translation;
        view.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyUpEvent, Key = Key.LeftCtrl });
        var hovered = false;
        foreach (var offset in new[] { .25f, .5f, .75f })
        {
            var point = await viewport.Controller.InvokeAsync(() =>
            {
                var gizmo = (TransformationGizmo)typeof(EditorGameEntityTransformService).GetField("active", BindingFlags.Instance | BindingFlags.NonPublic)!
                    .GetValue(viewport.Controller.GetTransformService())!;
                var camera = game.EditorServices.Get<EditorGameEntityCameraService>()!;
                var projected = Vector3.TransformCoordinate(new Vector3(offset, 0, 0), gizmo.GizmoRootEntity.Transform.WorldMatrix * camera.ViewMatrix * camera.ProjectionMatrix);
                return new global::Avalonia.Point((projected.X * .5 + .5) * view.Bounds.Width, (.5 - projected.Y * .5) * view.Bounds.Height);
            });
            view.RaiseEvent(new PointerEventArgs(InputElement.PointerMovedEvent, view, pointer, view, point, 0, new PointerPointProperties(), KeyModifiers.None));
            await Task.Delay(100);
            hovered = await viewport.Controller.InvokeAsync(() =>
            {
                var service = viewport.Controller.GetTransformService();
                var gizmo = (TransformationGizmo)typeof(EditorGameEntityTransformService).GetField("active", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(service)!;
                if (!gizmo.IsUnderMouse()) return false;
                if (service.IsControllingMouse) throw new Exception("Hovering a transform axis without pressing a button captured the mouse and blocks camera/reload.");
                return true;
            });
            if (hovered) break;
        }
        if (!hovered) throw new Exception("The transform hover regression did not reach a visible axis.");
        Console.WriteLine("PASS: Move, Rotate and Scale retain their captured axis outside the gizmo and release capture even with Ctrl held.");
        Console.WriteLine("PASS: hovering a transform axis does not capture the mouse or block camera/reload.");
    }

    private static void CheckScenePreviewTemplate()
    {
        var preview = new Stride.Assets.Editor.Avalonia.Views.Preview.ScenePreviewView();
        var view = preview.Template!.Build(preview).Result;
        if (view is not TextBlock text || string.IsNullOrWhiteSpace(text.Text)
            || text.TextWrapping != Avalonia.Media.TextWrapping.Wrap)
            throw new Exception("Scene preview must show wrapped guidance instead of an unrendered native window.");
        var model = new Stride.Assets.Editor.Avalonia.Views.Preview.ModelPreviewView();
        var modelView = model.Template!.Build(model).Result;
        if (modelView is not Avalonia.Controls.Presenters.ContentPresenter)
            throw new Exception("Model preview lost its Avalonia rendering host template.");
        Console.WriteLine("PASS: Scene preview shows guidance; Model preview retains its Avalonia image.");
    }

    private static async Task CheckBackgroundLogging()
    {
        var services = new Stride.Core.Presentation.ViewModels.ViewModelServiceProvider(
            new object[] { new Stride.Core.Presentation.Avalonia.Services.DispatcherService(Dispatcher.UIThread) });
        var logger = new LoggerResult("Background logging regression");
        var viewModel = new LoggerViewModel(services, logger);
        try
        {
            var worker = Task.Run(() =>
            {
                for (var i = 0; i < 100; i++) logger.Info("Background message " + i);
            });
            // Keep the UI busy: background logging must finish without a dispatcher turn.
            var completedWithoutUi = worker.Wait(TimeSpan.FromSeconds(2));
            await worker.WaitAsync(TimeSpan.FromSeconds(5));
            if (!completedWithoutUi) throw new Exception("Background logging waited for the UI thread.");
            logger.Warning("UI message");
            for (var i = 0; i < 100 && viewModel.Messages.Count < 101; i++) await Task.Delay(20);
            if (viewModel.Messages.Count != 101 || !viewModel.HasWarnings)
                throw new Exception("Queued logging lost messages or failed to update warning state.");
            Console.WriteLine("PASS: background logging completed while UI was busy; all 101 messages reached Output.");
        }
        finally { viewModel.ClearLoggers(); }
    }

    private static async Task CheckEventRouting(SceneViewportViewModel viewport, SceneViewportGame game, Avalonia.Controls.Window mainWindow, SessionViewModel session)
    {
        var view = (AvaloniaGameView)viewport.EditorHost!;
        for (var i = 0; i < 100 && view.PresentedFrameCount < 3; i++) await Task.Delay(50);
        CheckImage(view);
        var camera = game.EditorServices.Get<EditorGameEntityCameraService>()!;
        var position = await viewport.Controller.InvokeAsync(() => camera.Position);
        var pointer = new Avalonia.Input.Pointer(100, PointerType.Mouse, true);
        var point = new Avalonia.Point(view.Bounds.Width / 2, view.Bounds.Height / 2);
        try
        {
            view.RaiseEvent(new PointerPressedEventArgs(view, pointer, view, point, 0,
                new PointerPointProperties(RawInputModifiers.RightMouseButton, PointerUpdateKind.RightButtonPressed), KeyModifiers.None, 1));
            view.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.W });
            await Task.Delay(500);
            var moved = await viewport.Controller.InvokeAsync(() => camera.IsControllingMouse && camera.Position != position);
            if (!moved) throw new Exception("Avalonia RMB + W did not capture the mouse and move the camera.");
            view.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyUpEvent, Key = Key.W });
            await Task.Delay(100);
            var beforeRotation = await viewport.Controller.InvokeAsync(() => camera.Component.ViewMatrix);
            view.RaiseEvent(new PointerEventArgs(InputElement.PointerMovedEvent, view, pointer, view,
                point + new Avalonia.Vector(40, 15), 1,
                new PointerPointProperties(RawInputModifiers.RightMouseButton, PointerUpdateKind.Other), KeyModifiers.None));
            await Task.Delay(150);
            if (await viewport.Controller.InvokeAsync(() => camera.Component.ViewMatrix == beforeRotation))
                throw new Exception("Avalonia mouse movement did not rotate the camera.");
            Console.WriteLine("PASS: Avalonia pointer movement rotated the camera.");
        }
        finally
        {
            view.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyUpEvent, Key = Key.W });
            view.RaiseEvent(new PointerReleasedEventArgs(view, pointer, view, point, 0,
                new PointerPointProperties(RawInputModifiers.None, PointerUpdateKind.RightButtonReleased), KeyModifiers.None, Avalonia.Input.MouseButton.Right));
        }
        await Task.Delay(100);
        await viewport.Controller.InvokeAsync(() =>
        {
            if (camera.IsControllingMouse) throw new Exception("The camera did not release the mouse.");
            camera.Frame(game.ContentScene);
        });
        Console.WriteLine("PASS: Avalonia RMB + W moved the camera and released mouse capture.");
        await EditorCameraWheelSmoke.Check(viewport, game).WaitAsync(TimeSpan.FromSeconds(30));
        var originalBuffers = await viewport.Controller.InvokeAsync(() => (game.GraphicsDevice.Presenter.BackBuffer, game.GraphicsDevice.Presenter.DepthStencilBuffer));
        if (view.IsGpuRendering)
        {
            var start = await viewport.Controller.InvokeAsync(() => game.DrawTime.FrameCount);
            var timer = System.Diagnostics.Stopwatch.StartNew();
            await Task.Delay(2000);
            var end = await viewport.Controller.InvokeAsync(() =>
            {
                if (game.WindowMinimumUpdateRate.MinimumElapsedTime != TimeSpan.Zero)
                    throw new Exception("The GPU viewport still has a frame-rate cap.");
                return game.DrawTime.FrameCount;
            });
            Console.WriteLine($"GPU scene render rate: {(end - start) / timer.Elapsed.TotalSeconds:F1} FPS (no fixed cap).");
        }
        var oldHeight = view.Height;
        view.Height = Math.Max(64, view.Bounds.Height - 50);
        var scaling = TopLevel.GetTopLevel(view)!.RenderScaling;
        var size = await viewport.Controller.InvokeAsync(() => game.GraphicsDevice.Presenter.BackBuffer.Size);
        for (var i = 0; i < 60 && size.Height != (int)Math.Ceiling(view.Bounds.Height * scaling); i++)
        {
            await Task.Delay(50);
            size = await viewport.Controller.InvokeAsync(() => game.GraphicsDevice.Presenter.BackBuffer.Size);
        }
        if (size.Height != (int)Math.Ceiling(view.Bounds.Height * scaling))
            throw new Exception($"Offscreen size {size} did not follow Avalonia bounds {view.Bounds} at scale {scaling}.");
        await viewport.Controller.InvokeAsync(() =>
        {
            if (!ReferenceEquals(originalBuffers.BackBuffer, game.GraphicsDevice.Presenter.BackBuffer)
                || !ReferenceEquals(originalBuffers.DepthStencilBuffer, game.GraphicsDevice.Presenter.DepthStencilBuffer))
                throw new Exception("Resizing replaced buffer objects retained by scene renderers.");
        });
        CheckImage(view);
        view.Height = oldHeight;
        Console.WriteLine("PASS: offscreen target and bitmap resized with the Avalonia control.");
        // References is the default dock tab; explicitly display Preview before checking its rendered image.
        ((Control)mainWindow.Content!).FindControl<TabControl>("PreviewTabs")!.SelectedIndex = 0;
        await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Background);
        var previewService = (GameStudioPreviewService)session.ServiceProvider.Get<IAssetPreviewService>();
        var previewHost = (AvaloniaGameView)previewService.GetStrideView();
        var modelItem = session.AllPackages.SelectMany(package => package.Package.Assets)
            .First(asset => asset.Asset is Stride.Assets.Models.ModelAsset);
        var modelAsset = session.GetAssetById(modelItem.Id) ?? throw new Exception("The model has no asset view model.");
        previewService.SetAssetToPreview(modelAsset);
        for (var i = 0; i < 600 && (previewHost.PresentedFrameCount < 3 || (!previewHost.IsGpuRendering && CountImageColors(previewHost) < 8)); i++) await Task.Delay(50);
        CheckImage(previewHost);
        if (previewService.PreviewGame.Faulted) throw new Exception("Asset Preview faulted.");
        Console.WriteLine("PASS: Model Asset Preview also renders into an Avalonia bitmap.");
        if (((IClassicDesktopStyleApplicationLifetime)Application.Current!.ApplicationLifetime!).Args?.Contains("--preview-switch-stress") == true)
        {
            var previewErrors = new System.Collections.Concurrent.ConcurrentQueue<ILogMessage>();
            void RecordPreviewError(ILogMessage message)
            {
                if (message.Module == "Preview" && message.Type >= LogMessageType.Error) previewErrors.Enqueue(message);
            }
            GlobalLogger.GlobalMessageLogged += RecordPreviewError;
            var assets = session.AllPackages.Where(package => !package.Package.IsSystem)
                .SelectMany(package => package.Package.Assets).Select(item => session.GetAssetById(item.Id))
                .Where(asset => asset != null).Cast<Stride.Core.Assets.Presentation.ViewModels.AssetViewModel>().ToArray();
            previewService.PreviewGame.ExceptionThrown += (_, e) => Console.WriteLine("PREVIEW FAULT: " + e.Exception);
            for (var cycle = 0; cycle < 6; cycle++)
            {
                foreach (var asset in assets)
                {
                    Console.WriteLine("SWITCH PREVIEW: " + asset.Url + " " + asset.Asset.GetType().Name);
                    previewService.SetAssetToPreview(asset);
                    await Task.Delay(cycle % 2 == 0 ? 150 : 20);
                }
            }
            previewService.SetAssetToPreview(modelAsset);
            var frame = previewHost.PresentedFrameCount;
            var currentField = typeof(GameStudioPreviewService).GetField("currentPreview", BindingFlags.Instance | BindingFlags.NonPublic)!;
            bool LatestPreviewReady() => currentField.GetValue(previewService) is Stride.Editor.Preview.IAssetPreview current
                && current.AssetViewModel == modelAsset && current.IsInitialized().IsCompletedSuccessfully;
            for (var i = 0; i < 600 && (!LatestPreviewReady() || previewHost.PresentedFrameCount < frame + 10); i++) await Task.Delay(50);
            if (!LatestPreviewReady() || previewService.PreviewGame.Faulted || previewHost.PresentedFrameCount < frame + 10)
                throw new Exception("Preview stopped rendering after rapid asset switches.");
            GlobalLogger.GlobalMessageLogged -= RecordPreviewError;
            if (!previewErrors.IsEmpty) throw new Exception("Preview logged errors during switching: " + string.Join("\n", previewErrors.Select(message => message.ToString())));
            CheckImage(previewHost);
            Console.WriteLine($"PASS: {assets.Length * 6} rapid Asset Preview switches kept rendering without preview errors; latest selection loaded.");
        }
        if (OperatingSystem.IsLinux())
        {
            var libraries = File.ReadLines("/proc/self/maps").Where(line => line.Contains("libSDL")).ToArray();
            if (libraries.Length != 0) throw new Exception("SDL was loaded by the editor: " + string.Join("\n", libraries));
            Console.WriteLine("PASS: no SDL library is loaded in the editor process.");
        }
    }

    private static int CountImageColors(AvaloniaGameView view)
    {
        if (view.Source is not WriteableBitmap bitmap) return 0;
        using var frame = bitmap.Lock();
        var pixels = new byte[frame.RowBytes * frame.Size.Height];
        Marshal.Copy(frame.Address, pixels, 0, pixels.Length);
        var colors = new HashSet<int>();
        for (var i = 0; i < pixels.Length - 3; i += 16) colors.Add(BitConverter.ToInt32(pixels, i));
        return colors.Count;
    }

    private static void CheckImage(AvaloniaGameView view)
    {
        if (((IClassicDesktopStyleApplicationLifetime)Application.Current!.ApplicationLifetime!).Args?.Contains("--require-gpu") == true
            && (!view.IsGpuRendering || view.ReadbackFrameCount != 0))
            throw new Exception($"Expected GPU presentation with no CPU readback, got GPU={view.IsGpuRendering}, readbacks={view.ReadbackFrameCount}.", view.GpuInteropError);
        if (view.IsGpuRendering)
        {
            if (view.PresentedFrameCount == 0 || view.GpuInteropError != null)
                throw new Exception("The GPU surface did not present frames.", view.GpuInteropError);
            Console.WriteLine($"PASS: GPU surface displayed {view.PresentedFrameCount} frames; CPU readbacks: {view.ReadbackFrameCount}.");
            return;
        }
        var colors = CountImageColors(view);
        if (view.PresentedFrameCount == 0 || colors < 8)
            throw new Exception("The offscreen image is empty or has no scene detail.");
        Console.WriteLine($"PASS: Avalonia displayed {view.PresentedFrameCount} frames, {view.Source!.Size}, {colors} sampled colors.");
    }

    private static async Task CheckPresentedFrames(AvaloniaGameView view)
    {
        if (!OperatingSystem.IsLinux()) throw new PlatformNotSupportedException("Presentation capture requires grim and hyprctl, and a visible test window.");
        var locate = new System.Diagnostics.ProcessStartInfo("hyprctl") { RedirectStandardOutput = true, RedirectStandardError = true };
        locate.ArgumentList.Add("clients");
        locate.ArgumentList.Add("-j");
        using var locator = System.Diagnostics.Process.Start(locate)!;
        var clientsJson = await locator.StandardOutput.ReadToEndAsync();
        await locator.WaitForExitAsync();
        if (locator.ExitCode != 0) throw new Exception("Window lookup failed: " + await locator.StandardError.ReadToEndAsync());
        using var clients = System.Text.Json.JsonDocument.Parse(clientsJson);
        var client = clients.RootElement.EnumerateArray().Single(item => item.GetProperty("pid").GetInt32() == Environment.ProcessId);
        var at = client.GetProperty("at");
        // XWayland's PointToScreen coordinates can differ from the compositor's capture coordinates.
        var origin = view.TranslatePoint(default, TopLevel.GetTopLevel(view)!)!.Value;
        var start = new PixelPoint(at[0].GetInt32() + (int)(origin.X + view.Bounds.Width * 0.15), at[1].GetInt32() + (int)(origin.Y + view.Bounds.Height * 0.15));
        var end = new PixelPoint(at[0].GetInt32() + (int)(origin.X + view.Bounds.Width * 0.85), at[1].GetInt32() + (int)(origin.Y + view.Bounds.Height * 0.85));
        Console.WriteLine($"Capturing scene presentation: {start} - {end}");
        for (var sample = 0; sample < 50; sample++)
        {
            var capture = new System.Diagnostics.ProcessStartInfo("grim") { RedirectStandardOutput = true, RedirectStandardError = true };
            capture.ArgumentList.Add("-g");
            capture.ArgumentList.Add($"{start.X},{start.Y} {end.X - start.X}x{end.Y - start.Y}");
            capture.ArgumentList.Add("-");
            using var process = System.Diagnostics.Process.Start(capture)!;
            using var stream = new MemoryStream();
            await process.StandardOutput.BaseStream.CopyToAsync(stream);
            await process.WaitForExitAsync();
            if (process.ExitCode != 0) throw new Exception("Screen capture failed: " + await process.StandardError.ReadToEndAsync());
            stream.Position = 0;
            using var bitmap = new Bitmap(stream);
            if (sample == 0) bitmap.Save(Path.Combine(Path.GetTempPath(), "stride-viewport-presented-frame.png"));
            var pixels = new byte[checked(bitmap.PixelSize.Width * bitmap.PixelSize.Height * 4)];
            unsafe
            {
                fixed (byte* pointer = pixels)
                    bitmap.CopyPixels(new PixelRect(bitmap.PixelSize), (IntPtr)pointer, pixels.Length, bitmap.PixelSize.Width * 4);
            }
            var colors = new HashSet<int>();
            for (var offset = 0; offset < pixels.Length; offset += 16) colors.Add(BitConverter.ToInt32(pixels, offset));
            if (colors.Count < 64)
            {
                bitmap.Save(Path.Combine(Path.GetTempPath(), "stride-viewport-empty-frame.png"));
                throw new Exception($"Presented scene frame {sample} is empty ({colors.Count} sampled colors), despite {view.PresentedFrameCount} reported presentations.");
            }
            await Task.Delay(35);
        }
        Console.WriteLine("PASS: all 50 screen captures contained scene detail; no empty presented frames.");
    }

    private static int CountModels(Scene scene) => scene.Entities.Sum(CountEntityModels) + scene.Children.Sum(CountModels);
    private static int CountEntityModels(Entity entity) => (entity.Get<ModelComponent>()?.Model?.Meshes.Count > 0 ? 1 : 0)
        + entity.Transform.Children.Sum(child => CountEntityModels(child.Entity));
}
