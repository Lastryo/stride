// Copyright (c) .NET Foundation and Contributors (https://dotnetfoundation.org/ & https://stride3d.net) and Silicon Studio Corp. (https://www.siliconstudio.co.jp)
// Distributed under the MIT license. See the LICENSE.md file in the project root for more information.

using System.Runtime.InteropServices;
using Stride.Assets;
using Stride.Core;
using Stride.Core.Assets;
using Stride.Core.Assets.Compiler;
using Stride.Core.Assets.Editor.Services;
using Stride.Core.Assets.Presentation.ViewModels;
using Stride.Core.Diagnostics;
using Stride.Core.Extensions;
using Stride.Core.Presentation.Services;
using Stride.Editor.Avalonia.Engine;
using Stride.Editor.Build;
using Stride.Editor.Preview;
using Stride.Graphics;

namespace Stride.Editor.Avalonia.Preview;

public class GameStudioPreviewService : IAssetPreviewService, IPreviewBuilder
{
    public static bool DisablePreview = false;

    private readonly ISessionViewModel session;

    private readonly AvaloniaGameView host;
    private readonly IDebugPage? loggerDebugPage;
    private IAssetPreview currentPreview;
    private object previewView;

    private AssetViewModel previewBuildQueue;
    private bool previewChangePending;
    private readonly SemaphoreSlim previewChangeLock = new(1, 1);
    /// <summary>
    /// A lock used to access the <see cref="currentPreview"/> and <see cref="previewBuildQueue"/> fields safely.
    /// </summary>
    private readonly object previewLock = new();
    private readonly Thread previewGameThread;
    private volatile bool stopping;
    private readonly Dictionary<Type, AssetPreviewFactory> assetPreviewFactories = new();

    private readonly AssetCompilerContext previewCompileContext = new() { Platform = PlatformType.Windows };
    private readonly AssetDependenciesCompiler previewCompiler = new(typeof(PreviewCompilationContext));

    private readonly GameSettingsAsset previewGameSettings;
    private readonly GameSettingsProviderService gameSettingsProvider;

    public GameStudioPreviewService(ISessionViewModel session)
    {
        this.session = session;
        Dispatcher = session.Dispatcher;
        AssetBuilderService = session.ServiceProvider.Get<GameStudioBuilderService>();
        gameSettingsProvider = session.ServiceProvider.Get<GameSettingsProviderService>();

        Logger = GlobalLogger.GetLogger("Preview");
        loggerDebugPage = session.ServiceProvider.TryGet<IEditorDebugService>()?.CreateLogDebugPage(Logger, "Preview");

        previewGameSettings = GameSettingsFactory.Create();
        previewGameSettings.GetOrCreate<RenderingSettings>().DefaultGraphicsProfile = GraphicsProfile.Level_11_0;
        UpdateGameSettings(gameSettingsProvider.CurrentGameSettings);
        previewCompileContext.SetGameSettingsAsset(previewGameSettings);
        previewCompileContext.CompilationContext = typeof(PreviewCompilationContext);

        host = Dispatcher.Invoke(() => new AvaloniaGameView());
        previewGameThread = new Thread(SafeAction.Wrap(StrideUIThread)) { IsBackground = true, Name = "PreviewGame Thread" };
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            previewGameThread.SetApartmentState(ApartmentState.STA);
        }
        previewGameThread.Start();

        session.AssetPropertiesChanged += OnAssetPropertyChanged;
        gameSettingsProvider.GameSettingsChanged += OnGameSettingsChanged;
    }

    /// <summary>
    /// Gets whether this instance of <see cref="GameStudioPreviewService"/> has been disposed.
    /// </summary>
    public bool IsDisposed { get; private set; }

    public GameStudioBuilderService AssetBuilderService { get; }

    public IDispatcherService Dispatcher { get; }

    public Logger Logger { get; }

    public PreviewGame PreviewGame { get; private set; }

    public event EventHandler<EventArgs> PreviewAssetUpdated;

    private Task? disposal;

    public void Dispose() => _ = DisposeAsync();

    public ValueTask DisposeAsync() => new(disposal ??= DisposeCoreAsync());

    private async Task DisposeCoreAsync()
    {
        if (!IsDisposed)
        {
            // Stop before the desktop tears down the graphics context, including startup races.
            stopping = true;
            previewBuildQueue = null;

            session.AssetPropertiesChanged -= OnAssetPropertyChanged;
            gameSettingsProvider.GameSettingsChanged -= OnGameSettingsChanged;

            if (PreviewGame?.IsRunning == true)
            {
                PreviewGame.Exit();
            }

            await host.DisposeAsync();
            // Imported images must be released before their Vulkan device is destroyed.
            await Task.Run(previewGameThread.Join);

            host.Dispose();
            previewCompileContext?.Dispose();

            session.ServiceProvider.TryGet<IEditorDebugService>()?.UnregisterDebugPage(loggerDebugPage);

            IsDisposed = true;
        }
    }

    private void StrideUIThread()
    {
        PreviewGame = new PreviewGame(AssetBuilderService.EffectCompiler);
        PreviewGame.WindowCreated += (_, _) =>
        {
            if (stopping) PreviewGame.Exit();
        };
        host.AttachGame(PreviewGame);
        var context = host.Context;

        // Wait for shaders to be loaded
        AssetBuilderService.WaitForShaders();

        // TODO: For now we stop if there is an exception
        // Ideally, we should try to recreate the game.
        try
        {
            if (!DisablePreview && !stopping)
            {
                PreviewGame.GraphicsDeviceManager.DeviceCreated += GraphicsDeviceManagerDeviceCreated;
                PreviewGame.Run(context);
            }
        }
        finally
        {
            PreviewGame.Dispose();
        }
    }

    private void OnGameSettingsChanged(object? sender, GameSettingsChangedEventArgs e)
    {
        UpdateGameSettings(e.GameSettings);
    }

    private void UpdateGameSettings(GameSettingsAsset currentGameSettings)
    {
        previewGameSettings.GetOrCreate<EditorSettings>().RenderingMode = currentGameSettings.GetOrCreate<EditorSettings>().RenderingMode;
        previewGameSettings.GetOrCreate<RenderingSettings>().ColorSpace = currentGameSettings.GetOrCreate<RenderingSettings>().ColorSpace;
    }

    private void OnAssetPropertyChanged(object? sender, AssetChangedEventArgs e)
    {
        lock (previewLock)
        {
            var allAssets = AssetViewModel.ComputeRecursiveReferencerAssets(e.Assets);
            allAssets.AddRange(e.Assets);
            if (currentPreview != null && allAssets.Contains(currentPreview.AssetViewModel))
            {
                PreviewGame.Script.AddTask(UpdatePreviewAsset);
            }
        }
    }

    private void GraphicsDeviceManagerDeviceCreated(object? sender, EventArgs e)
    {
        // Transmit actual GraphicsProfile to preview and thumbnail builder context
        var graphicsProfile = PreviewGame.GraphicsDeviceManager.GraphicsDevice.Features.CurrentProfile;
        //ThumbnailService.ThumbnailBuilderContext.GetGameSettingsAsset().Get<RenderingSettings>().DefaultGraphicsProfile = graphicsProfile;
        previewCompileContext.GetGameSettingsAsset().GetOrCreate<RenderingSettings>().DefaultGraphicsProfile = graphicsProfile;
    }

    public void SetAssetToPreview(AssetViewModel asset)
    {
        if (stopping || IsDisposed) return;
        lock (previewLock)
        {
            previewBuildQueue = asset;
            previewChangePending = true;
        }
        PreviewGame.Script.AddTask(ChangePreviewAsset);
    }

    public AssetCompilerResult Compile(AssetItem asset)
    {
        return previewCompiler.Prepare(previewCompileContext, asset);
    }

    public object GetStrideView()
    {
        return !IsDisposed ? host : null;
    }

    private async Task UpdatePreviewAsset()
    {
        if (!previewChangeLock.Wait(0))
            return;

        try
        {
            // copy currentPreview to a local variable because it might be modified in a different thread.
            var localPreview = currentPreview;
            if (localPreview != null)
            {
                await localPreview.Update();
            }
            Logger.Info("Preview updated following to a a property change.");
        }
        catch (Exception e)
        {
            Logger.Error("Unable to update the preview after a property change.", e);
        }
        finally
        {
            previewChangeLock.Release();
        }
    }

    private async Task ChangePreviewAsset()
    {
        if (stopping) return;
        await previewChangeLock.WaitAsync();

        AssetViewModel asset = null;
        IAssetPreview nextPreview = null;
        try
        {
            lock (previewLock)
            {
                if (stopping || !previewChangePending) return;
            }
            if (currentPreview != null)
            {
                IAssetPreview previousPreview;
                // Ensure that the current preview won't be disposed twice with a lock
                lock (previewLock)
                {
                    previousPreview = currentPreview;
                    currentPreview = null;
                }
                await previousPreview.IsInitialized();
                await previousPreview.Dispose();
                Logger.Info($"Unloaded previous preview of {previousPreview.AssetViewModel.Url}.");
            }

            lock (previewLock)
            {
                previewChangePending = false;
                if (previewBuildQueue != null)
                {
                    asset = previewBuildQueue;
                    nextPreview = GetPreviewForAsset(previewBuildQueue);
                    previewBuildQueue = null;
                }
                currentPreview = nextPreview;
            }

            if (asset != null && nextPreview != null)
            {
                previewView = await nextPreview.Initialize(asset, this);
                if (previewView != null)
                    Logger.Info($"Initialized preview of {nextPreview.AssetViewModel.Url}.");
            }
            else
            {
                previewView = null;
            }
        }
        catch (Exception e)
        {
            lock (previewLock)
            {
                currentPreview = null;
            }
            previewView = null;
            Logger.Error("An exception occurred while changing the previewed asset", e);
        }
        finally
        {
            previewChangeLock.Release();
        }

        // Notify that the previewed asset has changed, so the editor view can update its visual tree.
        PreviewAssetUpdated?.Invoke(this, EventArgs.Empty);
    }

    private IAssetPreview GetPreviewForAsset(AssetViewModel asset)
    {
        var assetType = asset.Asset.GetType();
        while (assetType is not null)
        {
            AssetPreviewFactory factory;
            if (assetPreviewFactories.TryGetValue(assetType, out factory))
            {
                var assetPreview = factory(this, PreviewGame, asset.AssetItem);
                return assetPreview;
            }
            assetType = assetType.BaseType;
        }
        return null;
    }

    public object GetCurrentPreviewView()
    {
        return previewView;
    }

    public void RegisterAssetPreviewFactories(IReadOnlyDictionary<Type, AssetPreviewFactory> factories)
    {
        factories.ForEach(x => assetPreviewFactories.Add(x.Key, x.Value));
    }

    public void OnShowPreview()
    {
        PreviewGame.IsEditorHidden = false;
    }

    public void OnHidePreview()
    {
        PreviewGame.IsEditorHidden = true;
    }

}
