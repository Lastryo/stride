// Copyright (c) .NET Foundation and Contributors (https://dotnetfoundation.org/ & https://stride3d.net)
// Distributed under the MIT license. See the LICENSE.md file in the project root for more information.

using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using global::Avalonia;
using global::Avalonia.Controls;
using global::Avalonia.Input;
using global::Avalonia.Media;
using global::Avalonia.Media.Imaging;
using global::Avalonia.Platform;
using global::Avalonia.Threading;
using Stride.Editor.EditorGame.Game;
using Stride.Games;
using Stride.Graphics;
using Stride.Input;
using Vector2 = Stride.Core.Mathematics.Vector2;
using MouseButton = Stride.Input.MouseButton;
using PixelFormat = Stride.Graphics.PixelFormat;

namespace Stride.Editor.Avalonia.Engine;

/// <summary>A windowless Stride render target displayed and controlled by Avalonia.</summary>
public sealed partial class AvaloniaGameView : global::Avalonia.Controls.Image, global::Avalonia.Rendering.ICustomHitTest, IDisposable, IAsyncDisposable
{
    private readonly ConcurrentQueue<Action<MouseSimulated, KeyboardSimulated>> input = new();
    private readonly HashSet<Keys> keys = new();
    private readonly HashSet<MouseButton> buttons = new();
    private WriteableBitmap? bitmap;
    private TopLevel? topLevel;
    private EditorServiceGame? attachedGame;
    private Vector2 pointerPosition;
    private Vector2 rightClickPosition;
    private long requestedSize = (640L << 32) | 480;
    private int framePending;
    private bool? lastCursorVisible;
    private volatile bool attached;
    private volatile bool disposed;

    public AvaloniaGameView()
    {
        Focusable = true;
        ClipToBounds = true;
        Stretch = Stretch.Fill;
        MinWidth = MinHeight = 32;
        AttachedToVisualTree += (_, _) =>
        {
            attached = true;
            if (attachedGame?.Window != null) attachedGame.Window.Visible = true;
            topLevel = TopLevel.GetTopLevel(this);
            if (topLevel != null) topLevel.ScalingChanged += OnScalingChanged;
            UpdateSize();
            InitializeGpuInterop();
        };
        DetachedFromVisualTree += (_, _) =>
        {
            attached = false;
            if (attachedGame?.Window != null) attachedGame.Window.Visible = false;
            if (topLevel != null) topLevel.ScalingChanged -= OnScalingChanged;
            topLevel = null;
            ReleaseInput();
        };
        SizeChanged += (_, _) => UpdateSize();
        LostFocus += (_, _) => ReleaseInput();
        PointerCaptureLost += (_, _) => { if (buttons.Count > 0) ReleaseInput(); };
    }

    public GameContextHeadless Context { get; } = new(640, 480) { InitializeDatabase = false };
    public long PresentedFrameCount { get; private set; }
    public Vector2 GetMousePosition(bool lastRightClick) => lastRightClick ? rightClickPosition : pointerPosition;

    public void AttachGame(EditorServiceGame game)
    {
        attachedGame = game;
        game.WindowCreated += (_, _) => game.Window.Visible = attached;
        game.GraphicsDeviceManager.PreferredBackBufferFormat = PixelFormat.R8G8B8A8_UNorm;
        game.GameSystems.Add(new ViewSystem(this, game));
    }

    private void OnScalingChanged(object? sender, EventArgs e) => UpdateSize();

    private void UpdateSize()
    {
        var scaling = TopLevel.GetTopLevel(this)?.RenderScaling ?? 1;
        if (Bounds.Width > 0 && Bounds.Height > 0)
        {
            var width = Math.Max(1, (int)Math.Ceiling(Bounds.Width * scaling));
            var height = Math.Max(1, (int)Math.Ceiling(Bounds.Height * scaling));
            Interlocked.Exchange(ref requestedSize, ((long)width << 32) | (uint)height);
            if (gpuVisual != null)
            {
                var visualSize = new Vector(Bounds.Width, Bounds.Height);
                if (gpuVisual.Size != visualSize) gpuVisual.Size = visualSize;
            }
        }
    }

    bool global::Avalonia.Rendering.ICustomHitTest.HitTest(global::Avalonia.Point point) =>
        new global::Avalonia.Rect(default, Bounds.Size).Contains(point);

    protected override global::Avalonia.Size MeasureOverride(global::Avalonia.Size availableSize)
    {
        if (gpuRendering || Source is null) return new global::Avalonia.Size(MinWidth, MinHeight);
        return base.MeasureOverride(availableSize);
    }

    protected override global::Avalonia.Size ArrangeOverride(global::Avalonia.Size finalSize)
    {
        if (gpuRendering || Source is null) return finalSize;
        return base.ArrangeOverride(finalSize);
    }

    private void MovePointer(PointerEventArgs e)
    {
        var point = e.GetPosition(this);
        var position = new Vector2((float)(point.X / Math.Max(1, Bounds.Width)), (float)(point.Y / Math.Max(1, Bounds.Height)));
        var delta = position - pointerPosition;
        pointerPosition = position;
        input.Enqueue((mouse, _) =>
        {
            if (mouse.IsPositionLocked) mouse.MouseState.HandleMouseDelta(delta);
            else mouse.SetPosition(position);
        });
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        MovePointer(e);
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        Focus();
        MovePointer(e);
        var button = ConvertButton(e.GetCurrentPoint(this).Properties);
        if (button is not { } value) return;
        if (value == MouseButton.Right) rightClickPosition = pointerPosition;
        buttons.Add(value);
        input.Enqueue((mouse, _) => mouse.SimulateMouseDown(value));
        e.Pointer.Capture(this);
        e.Handled = true;
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        MovePointer(e);
        var button = ConvertButton(e.GetCurrentPoint(this).Properties)
            ?? ConvertMouseButton(e.InitialPressMouseButton);
        if (button is { } value)
        {
            buttons.Remove(value);
            input.Enqueue((mouse, _) => mouse.SimulateMouseUp(value));
        }
        else
        {
            foreach (var held in buttons.ToArray())
            {
                buttons.Remove(held);
                input.Enqueue((mouse, _) => mouse.SimulateMouseUp(held));
            }
        }
        if (buttons.Count == 0) e.Pointer.Capture(null);
        e.Handled = true;
    }

    protected override void OnPointerWheelChanged(PointerWheelEventArgs e)
    {
        base.OnPointerWheelChanged(e);
        MovePointer(e);
        var delta = (float)e.Delta.Y;
        input.Enqueue((mouse, _) => mouse.SimulateMouseWheel(delta));
        e.Handled = true;
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (!Enum.TryParse<Keys>(e.Key.ToString(), out var key)) return;
        if (keys.Add(key)) input.Enqueue((_, keyboard) => keyboard.SimulateDown(key));
        e.Handled = true;
    }

    protected override void OnKeyUp(KeyEventArgs e)
    {
        base.OnKeyUp(e);
        if (!Enum.TryParse<Keys>(e.Key.ToString(), out var key)) return;
        keys.Remove(key);
        input.Enqueue((_, keyboard) => keyboard.SimulateUp(key));
        e.Handled = true;
    }

    private static MouseButton? ConvertButton(PointerPointProperties props)
    {
        var kind = props.PointerUpdateKind;
        var button = kind switch
        {
            PointerUpdateKind.LeftButtonPressed or PointerUpdateKind.LeftButtonReleased => MouseButton.Left,
            PointerUpdateKind.MiddleButtonPressed or PointerUpdateKind.MiddleButtonReleased => MouseButton.Middle,
            PointerUpdateKind.RightButtonPressed or PointerUpdateKind.RightButtonReleased => MouseButton.Right,
            _ => (MouseButton?)null,
        };
        if (button.HasValue) return button;
        if (props.IsLeftButtonPressed) return MouseButton.Left;
        if (props.IsRightButtonPressed) return MouseButton.Right;
        if (props.IsMiddleButtonPressed) return MouseButton.Middle;
        return null;
    }

    private static MouseButton? ConvertMouseButton(global::Avalonia.Input.MouseButton button) => button switch
    {
        global::Avalonia.Input.MouseButton.Left => MouseButton.Left,
        global::Avalonia.Input.MouseButton.Middle => MouseButton.Middle,
        global::Avalonia.Input.MouseButton.Right => MouseButton.Right,
        _ => null,
    };

    private void ReleaseInput()
    {
        foreach (var key in keys.ToArray()) input.Enqueue((_, keyboard) => keyboard.SimulateUp(key));
        foreach (var button in buttons.ToArray()) input.Enqueue((mouse, _) => mouse.SimulateMouseUp(button));
        input.Enqueue((mouse, _) => mouse.UnlockPosition());
        keys.Clear();
        buttons.Clear();
    }

    private void Present(byte[] pixels, int width, int height, bool cursorVisible)
    {
        Dispatcher.UIThread.Post(() =>
        {
            try
            {
                if (disposed || gpuRendering) return;
                if (bitmap is null || bitmap.PixelSize != new PixelSize(width, height))
                {
                    var previous = bitmap;
                    bitmap = new WriteableBitmap(new PixelSize(width, height), new Vector(96, 96), global::Avalonia.Platform.PixelFormat.Rgba8888, AlphaFormat.Opaque);
                    Source = bitmap;
                    previous?.Dispose();
                }
                using (var frame = bitmap.Lock())
                {
                    for (var row = 0; row < height; row++)
                        Marshal.Copy(pixels, row * width * 4, frame.Address + row * frame.RowBytes, width * 4);
                }
                UpdateCursor(cursorVisible);
                InvalidateVisual();
                PresentedFrameCount++;
            }
            finally { Interlocked.Exchange(ref framePending, 0); }
        });
    }

    private void UpdateCursor(bool visible)
    {
        if (lastCursorVisible == visible) return;
        Cursor = new Cursor(visible ? StandardCursorType.Arrow : StandardCursorType.None);
        lastCursorVisible = visible;
    }

    public void Dispose()
    {
        disposed = true;
        if (!Dispatcher.UIThread.CheckAccess())
        {
            Dispatcher.UIThread.Post(Dispose);
            return;
        }
        if (uiDisposed) return;
        uiDisposed = true;
        ReleaseInput();
        _ = ReleaseGpuResourcesAsync();
        if (topLevel != null) topLevel.ScalingChanged -= OnScalingChanged;
        Source = null;
        bitmap?.Dispose();
        bitmap = null;
    }

    private sealed class ViewSystem : GameSystemBase
    {
        private readonly AvaloniaGameView view;
        private readonly EditorServiceGame game;
        private readonly InputSourceSimulated source = new();
        private MouseSimulated? mouse;
        private KeyboardSimulated? keyboard;
        private Texture? staging;
        private byte[] pixels = [];
        private long currentSize;

        public ViewSystem(AvaloniaGameView view, EditorServiceGame game) : base(game.Services)
        {
            this.view = view;
            this.game = game;
            Enabled = Visible = true;
            UpdateOrder = int.MinValue;
            DrawOrder = int.MaxValue;
        }

        public override void Update(GameTime gameTime)
        {
            game.Window.Visible = view.attached;
            if (mouse is null)
            {
                game.Input.Sources.Add(source);
                mouse = source.AddMouse();
                keyboard = source.AddKeyboard();
            }
            while (view.input.TryDequeue(out var action)) action(mouse, keyboard!);
            var size = Interlocked.Read(ref view.requestedSize);
            if (currentSize == size) return;
            game.GraphicsDeviceManager.PreferredBackBufferWidth = (int)(size >> 32);
            game.GraphicsDeviceManager.PreferredBackBufferHeight = (int)size;
            game.GraphicsDeviceManager.ApplyChanges();
            currentSize = size;
        }

        public override void Draw(GameTime gameTime)
        {
            if (view.disposed || !view.attached || !view.gpuProbeCompleted) return;
            if (view.TryPresentGpu(game.GraphicsDevice.Presenter.BackBuffer, game.GraphicsContext.CommandList, game.IsMouseVisible)) return;
            // CPU transport remains available on backends without compatible GPU interop.
            game.WindowMinimumUpdateRate.SetMaxFrequency(30);
            if (Interlocked.CompareExchange(ref view.framePending, 1, 0) != 0) return;
            try
            {
                var buffer = game.GraphicsDevice.Presenter.BackBuffer;
                if (staging is null || staging.Width != buffer.Width || staging.Height != buffer.Height || staging.Format != buffer.Format)
                {
                    staging?.Dispose();
                    staging = buffer.ToStaging();
                    pixels = new byte[checked(buffer.Width * buffer.Height * 4)];
                }
                buffer.GetData<byte>(game.GraphicsContext.CommandList, staging, pixels);
                view.ReadbackFrameCount++;
                view.Present(pixels, buffer.Width, buffer.Height, game.IsMouseVisible);
            }
            catch
            {
                Interlocked.Exchange(ref view.framePending, 0);
                throw;
            }
        }

        protected override void Destroy()
        {
            view.Dispose();
            view.gpuReleased.Task.GetAwaiter().GetResult();
            staging?.Dispose();
            base.Destroy();
        }
    }
}
