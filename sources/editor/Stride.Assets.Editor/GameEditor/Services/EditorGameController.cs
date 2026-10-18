// Copyright (c) .NET Foundation and Contributors (https://dotnetfoundation.org/ & https://stride3d.net)
// Distributed under the MIT license. See the LICENSE.md file in the project root for more information.

using System.Runtime.CompilerServices;
using Stride.Assets.Editor.GameEditor.Game;
using Stride.Assets.Editor.ViewModels;
using Stride.Core;
using Stride.Core.Assets;
using Stride.Core.Assets.Editor.Services;
using Stride.Core.Assets.Editor.ViewModels;
using Stride.Core.Assets.Presentation.ViewModels;
using Stride.Core.Assets.Quantum;
using Stride.Core.Diagnostics;
using Stride.Core.Mathematics;
using Stride.Core.Presentation.Services;
using Stride.Editor.Build;
using Stride.Editor.EditorGame.ContentLoader;
using Stride.Editor.EditorGame.Game;
using Stride.Editor.EditorGame.ViewModels;
using Stride.Shaders.Compiler;

namespace Stride.Assets.Editor.GameEditor.Services;

public delegate TEditorGame EditorGameFactory<out TEditorGame>(TaskCompletionSource<bool> gameContentLoadedTaskSource,
    IEffectCompiler effectCompiler, string effectLogPath) where TEditorGame : EditorServiceGame;

public abstract class EditorGameController<TEditorGame> : IEditorGameController where TEditorGame : EditorServiceGame
{
    protected readonly AssetViewModel Asset;
    protected readonly TEditorGame Game;
    private readonly Thread sceneGameThread;
    private readonly IEditorGameViewFactory viewFactory;
    private readonly GameThreadDispatcher gameDispatcher;
    private readonly IDebugPage? debugPage;
    private readonly TaskCompletionSource<bool> gameStarted = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource<bool> gameContentLoaded = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource<bool> gameStopped = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private EditorGameServiceRegistry? serviceRegistry;
    private IEditorGameView? view;
    private bool started;

    protected EditorGameController(AssetViewModel asset, GameEditorViewModel editor, EditorGameFactory<TEditorGame> gameFactory)
    {
        ArgumentNullException.ThrowIfNull(asset);
        ArgumentNullException.ThrowIfNull(editor);
        ArgumentNullException.ThrowIfNull(gameFactory);
        Asset = asset;
        Editor = editor;
        viewFactory = asset.ServiceProvider.Get<IEditorGameViewFactory>();
        GameSideNodeContainer = new SessionNodeContainer(asset.ServiceProvider) { NodeBuilder = { NodeFactory = new AssetNodeFactory() } };
        Logger = new LoggerResult();
        debugPage = asset.ServiceProvider.TryGet<IEditorDebugService>()?.CreateLogDebugPage(Logger, "Scene");
        var builder = asset.ServiceProvider.Get<GameStudioBuilderService>();
        Game = gameFactory(gameContentLoaded, builder.EffectCompiler, builder.EffectLogPath);
        Game.WindowCreated += (_, _) =>
        {
            if (IsDestroying)
                Game.Exit();
        };
        Game.ExceptionThrown += (_, args) =>
        {
            gameContentLoaded.TrySetException(args.Exception);
            if (!Editor.SceneInitialized)
                Game.Exit();
        };
        Game.PackageSettings = asset.ServiceProvider.Get<GameSettingsProviderService>();
        sceneGameThread = new Thread(SceneGameRunThread) { IsBackground = true, Name = $"EditorGameThread ({asset.Url})" };
        if (OperatingSystem.IsWindows())
            sceneGameThread.SetApartmentState(ApartmentState.STA);
        gameDispatcher = new GameThreadDispatcher((callback, priority) => { Game.Script.AddTask(callback, priority); }, CheckGameAccess);
        Debug = new EditorGameDebugService();
        Loader = new EditorContentLoader(this, Logger, editor, Game);
    }

    public IEditorContentLoader Loader { get; }
    public Logger Logger { get; }
    public EditorGameDebugService Debug { get; }
    public SessionNodeContainer GameSideNodeContainer { get; }
    public object? EditorHost { get; private set; }
    public Task GameContentLoaded => gameContentLoaded.Task;
    public Task GameStopped => gameStopped.Task;
    protected GameEditorViewModel Editor { get; }
    protected EditorGameRecoveryService? RecoveryService { get; private set; }
    protected bool IsDestroyed { get; private set; }
    protected bool IsDestroying { get; private set; }

    public async Task<bool> StartGame()
    {
        EnsureNotDestroyed();
        EnsureAssetAccess();
        if (IsDestroying)
            throw new InvalidOperationException("This controller is being disposed.");
        if (gameStopped.Task.IsCompleted)
            throw new InvalidOperationException("The editor game has stopped.");
        if (!started)
        {
            started = true;
            sceneGameThread.Start();
        }
        await gameStarted.Task;
        if (IsDestroying || IsDestroyed)
            return false;
        EditorHost ??= view!.CreateHost();
        return true;
    }

    public virtual void Destroy()
    {
        EnsureNotDestroyed();
        EnsureAssetAccess();
        if (IsDestroying)
            return;
        IsDestroying = true;
        Loader.Dispose();
        Asset.ServiceProvider.TryGet<IEditorDebugService>()?.UnregisterDebugPage(debugPage);
        view?.DisposeHost();
        EditorHost = null;
        gameDispatcher.Stop();
        if (started)
        {
            // Exit is thread-safe; cleanup runs in the game thread's finally block.
            if (Game.Window != null && !gameStopped.Task.IsCompleted)
                Game.Exit();
        }
        else
        {
            Game.Dispose();
            gameContentLoaded.TrySetCanceled();
            gameStopped.TrySetResult(true);
        }
        IsDestroyed = true;
    }

    public abstract Task<bool> CreateScene();
    protected abstract object? FindPart(AbsoluteId partId);

    public object? FindGameSidePart(AbsoluteId partId)
    {
        EnsureNotDestroyed();
        EnsureGameAccess();
        return FindPart(partId);
    }

    public T? GetService<T>() where T : IEditorGameViewModelService
    {
        EnsureNotDestroyed();
        EnsureAssetAccess();
        return IsDestroying || serviceRegistry is null ? default : serviceRegistry.Get<T>();
    }

    public void OnHideGame() => Game.IsEditorHidden = true;
    public void OnShowGame() => Game.IsEditorHidden = false;

    public Task<Vector3> GetMousePositionInSceneAsync(bool lastRightClick, CancellationToken token = default)
    {
        EnsureNotDestroyed();
        EnsureAssetAccess();
        var position = view?.GetMousePosition(lastRightClick) ?? Vector2.Zero;
        return InvokeAsync(() => Game.GetPositionInScene(position), token);
    }

    public void TriggerActiveRenderStageReevaluation()
    {
        EnsureNotDestroyed();
        EnsureGameAccess();
        if (!IsDestroying)
            Game.TriggerActiveRenderStageReevaluation();
    }

    public Task InvokeAsync(Action callback, CancellationToken token = default) =>
        InvokeAsync(() => { callback(); return true; }, token);

    public Task<TResult> InvokeAsync<TResult>(Func<TResult> callback, CancellationToken token = default)
    {
        EnsureNotDestroyed();
        return gameDispatcher.Invoke(() => Task.FromResult(callback()), 0, token, true);
    }

    public Task LowPriorityInvokeAsync(Action callback, CancellationToken token = default)
    {
        EnsureNotDestroyed();
        return gameDispatcher.Invoke(() => { callback(); return Task.FromResult(true); }, int.MaxValue, token, false);
    }

    public Task InvokeTask(Func<Task> callback, CancellationToken token = default)
    {
        EnsureNotDestroyed();
        return gameDispatcher.Invoke(async () => { await callback(); return true; }, 0, token, false);
    }

    public Task<TResult> InvokeTask<TResult>(Func<Task<TResult>> callback, CancellationToken token = default)
    {
        EnsureNotDestroyed();
        return gameDispatcher.Invoke(callback, 0, token, false);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool CheckAssetAccess() => Asset.Dispatcher.CheckAccess();
    public void EnsureAssetAccess() => Asset.Dispatcher.EnsureAccess();
    public bool CheckGameAccess() => Thread.CurrentThread == sceneGameThread;
    public void EnsureGameAccess(bool inGameThread = true)
    {
        if (CheckGameAccess() != inGameThread)
            throw new InvalidOperationException(inGameThread ? "This code must be executed in the game thread." : "This code must not be executed in the game thread.");
    }

    bool IDispatcherService.CheckAccess() => CheckGameAccess();
    void IDispatcherService.EnsureAccess(bool inDispatcherThread) => EnsureGameAccess(inDispatcherThread);
    void IDispatcherService.Invoke(Action callback) => throw new NotSupportedException();
    TResult IDispatcherService.Invoke<TResult>(Func<TResult> callback) => throw new NotSupportedException();

    protected void EnsureNotDestroyed([CallerMemberName] string? name = null)
    {
        ObjectDisposedException.ThrowIf(IsDestroyed, name ?? GetType().Name);
    }

    protected virtual void InitializeServices(EditorGameServiceRegistry services)
    {
        services.Add(Debug);
        services.Add(RecoveryService = new EditorGameRecoveryService(Editor));
    }

    private void SceneGameRunThread()
    {
        try
        {
            view = viewFactory.CreateView();
            Asset.ServiceProvider.Get<GameStudioBuilderService>().WaitForShaders();
            serviceRegistry = new EditorGameServiceRegistry();
            InitializeServices(serviceRegistry);
            Game.RegisterServices(serviceRegistry);
            view.AttachGame(Game);
            gameStarted.TrySetResult(true);
            if (!IsDestroying)
                Game.Run(view.Context);
        }
        catch (Exception exception)
        {
            Logger.Error("The editor game stopped unexpectedly.", exception);
            gameStarted.TrySetException(exception);
            gameContentLoaded.TrySetException(exception);
            Asset.Dispatcher.InvokeAsync(() => Editor.LastException = exception);
        }
        finally
        {
            gameDispatcher.Stop();
            gameContentLoaded.TrySetCanceled();
            try
            {
                try
                {
                    if (serviceRegistry != null)
                        serviceRegistry.DisposeAsync().AsTask().GetAwaiter().GetResult();
                }
                finally
                {
                    try
                    {
                        Game.Dispose();
                    }
                    finally
                    {
                        view?.Dispose();
                        GameSideNodeContainer.Clear();
                    }
                }
                gameStopped.TrySetResult(true);
            }
            catch (Exception exception)
            {
                Logger.Error("Unable to dispose the editor game.", exception);
                gameStopped.TrySetException(exception);
            }
            finally
            {
                if (IsDestroying)
                    IsDestroyed = true;
            }
        }
    }
}
