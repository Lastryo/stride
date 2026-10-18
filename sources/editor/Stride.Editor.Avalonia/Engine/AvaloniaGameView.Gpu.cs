// Copyright (c) .NET Foundation and Contributors (https://dotnetfoundation.org/ & https://stride3d.net)
// Distributed under the MIT license. See the LICENSE.md file in the project root for more information.

using global::Avalonia;
using global::Avalonia.Input;
using global::Avalonia.Platform;
using global::Avalonia.Rendering.Composition;
using global::Avalonia.Threading;
using Stride.Graphics;

namespace Stride.Editor.Avalonia.Engine;

public sealed partial class AvaloniaGameView
{
    private readonly object gpuLock = new();
    private readonly List<GpuFrame> gpuFrames = new();
    private readonly List<Task> retiredGpuFrames = new();
    private readonly TaskCompletionSource gpuReleased = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private ICompositionGpuInterop? gpuInterop;
    private CompositionDrawingSurface? gpuSurface;
    private CompositionSurfaceVisual? gpuVisual;
    private volatile bool gpuProbeCompleted;
    private volatile bool gpuRendering;
    private int presentationPending;
    private bool gpuUnavailable;
    private bool gpuInitializing;
    private bool uiDisposed;
    private Exception? gpuError;

    public bool IsGpuRendering => gpuRendering;
    public Exception? GpuInteropError => gpuError;
    public long ReadbackFrameCount { get; private set; }

    private async void InitializeGpuInterop()
    {
        if (gpuInitializing || gpuProbeCompleted || disposed) return;
        gpuInitializing = true;
        try
        {
            var compositor = ElementComposition.GetElementVisual(this)!.Compositor;
            var interop = await compositor.TryGetCompositionGpuInterop();
            if (disposed) return;
            const string handleType = KnownPlatformGraphicsExternalImageHandleTypes.VulkanOpaquePosixFileDescriptor;
            if (OperatingSystem.IsLinux() && interop != null
                && interop.SupportedImageHandleTypes.Contains(handleType)
                && interop.SupportedSemaphoreTypes.Contains(handleType)
                && interop.GetSynchronizationCapabilities(handleType).HasFlag(CompositionGpuImportedImageSynchronizationCapabilities.Semaphores))
            {
                gpuSurface = compositor.CreateDrawingSurface();
                gpuVisual = compositor.CreateSurfaceVisual();
                gpuVisual.Surface = gpuSurface;
                gpuVisual.Size = new Vector(Bounds.Width, Bounds.Height);
                ElementComposition.SetElementChildVisual(this, gpuVisual);
                lock (gpuLock) gpuInterop = interop;
            }
        }
        catch (Exception exception) { gpuError = exception; }
        finally { gpuProbeCompleted = true; }
    }

    private bool TryPresentGpu(Texture buffer, CommandList commandList, bool cursorVisible)
    {
        lock (gpuLock)
        {
            if (gpuInterop == null || gpuUnavailable) return false;
            if (disposed || !attached) return true;
            if (gpuError != null) throw new InvalidOperationException("GPU viewport presentation failed.", gpuError);
            if (presentationPending != 0) return true;

            foreach (var frame in gpuFrames.Where(frame => frame.Native.Width != buffer.Width || frame.Native.Height != buffer.Height).ToArray())
            {
                gpuFrames.Remove(frame);
                retiredGpuFrames.Add(RetireGpuFrame(frame));
            }
            retiredGpuFrames.RemoveAll(task => task.IsCompletedSuccessfully);
            var next = gpuFrames.Count < 3 ? null : gpuFrames.FirstOrDefault(frame => frame.Presented.IsCompletedSuccessfully && frame.Native.IsReady);
            if (next == null && gpuFrames.Count < 3)
            {
                var native = SharedGpuTexture.TryCreate(buffer.GraphicsDevice, buffer.Width, buffer.Height, gpuInterop.DeviceUuid!);
                if (native == null)
                {
                    gpuUnavailable = true;
                    Dispatcher.UIThread.Post(() => ElementComposition.SetElementChildVisual(this, null));
                    return false;
                }
                next = new GpuFrame(native);
                gpuFrames.Add(next);
            }
            if (next == null) return true; // Keep rendering; the compositor takes the next available frame.
            Interlocked.Exchange(ref presentationPending, 1);
            gpuFrames.Remove(next);
            gpuFrames.Add(next);
            var completed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            next.Presented = completed.Task;
            try { next.Native.CopyFrom(commandList, buffer); }
            catch (Exception exception)
            {
                Interlocked.Exchange(ref presentationPending, 0);
                completed.TrySetException(exception);
                throw;
            }
            gpuRendering = true;
            var captured = next;
            Dispatcher.UIThread.Post(async () =>
            {
                try
                {
                    if (!disposed)
                    {
                        captured.Import(gpuInterop);
                        if (Source != null)
                        {
                            Source = null;
                            bitmap?.Dispose();
                            bitmap = null;
                        }
                        var visualSize = new Vector(Bounds.Width, Bounds.Height);
                        if (gpuVisual!.Size != visualSize)
                            gpuVisual.Size = visualSize;

                        await gpuSurface!.UpdateWithSemaphoresAsync(captured.Image!, captured.Ready!, captured.Available!);
                        UpdateCursor(cursorVisible);
                        PresentedFrameCount++;
                    }
                    completed.TrySetResult();
                }
                catch (Exception exception) { gpuError = exception; completed.TrySetException(exception); }
                finally { Interlocked.Exchange(ref presentationPending, 0); }
            });
            return true;
        }
    }

    private static Task RetireGpuFrame(GpuFrame frame)
    {
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        Dispatcher.UIThread.Post(async () =>
        {
            try { await frame.DisposeAsync(); completion.TrySetResult(); }
            catch (Exception exception) { completion.TrySetException(exception); }
        });
        return completion.Task;
    }

    private async Task ReleaseGpuResourcesAsync()
    {
        try
        {
            Task[] pending;
            lock (gpuLock)
            {
                pending = retiredGpuFrames.Concat(gpuFrames.Select(RetireGpuFrame)).ToArray();
                gpuFrames.Clear();
            }
            await Task.WhenAll(pending);
            ElementComposition.SetElementChildVisual(this, null);
            gpuSurface?.Dispose();
            gpuReleased.TrySetResult();
        }
        catch (Exception exception) { gpuReleased.TrySetException(exception); }
    }

    public ValueTask DisposeAsync()
    {
        Dispose();
        return new ValueTask(gpuReleased.Task);
    }

    private sealed class GpuFrame(SharedGpuTexture native) : IAsyncDisposable
    {
        public SharedGpuTexture Native { get; } = native;
        public Task Presented { get; set; } = Task.CompletedTask;
        public ICompositionImportedGpuImage? Image { get; private set; }
        public ICompositionImportedGpuSemaphore? Ready { get; private set; }
        public ICompositionImportedGpuSemaphore? Available { get; private set; }

        public void Import(ICompositionGpuInterop interop)
        {
            if (Image != null) return;
            Image = interop.ImportImage(new PlatformHandle(Native.ExportMemoryHandle(), Native.HandleType), new PlatformGraphicsExternalImageProperties
            {
                Width = Native.Width, Height = Native.Height, MemorySize = Native.MemorySize,
                Format = PlatformGraphicsExternalImageFormat.R8G8B8A8UNorm, TopLeftOrigin = true,
            });
            Ready = interop.ImportSemaphore(new PlatformHandle(Native.ExportReadySemaphore(), Native.HandleType));
            Available = interop.ImportSemaphore(new PlatformHandle(Native.ExportAvailableSemaphore(), Native.HandleType));
        }

        public async ValueTask DisposeAsync()
        {
            try { await Presented; }
            catch { /* The imports must also be released after a failed presentation. */ }
            try
            {
                if (Image != null) await Image.DisposeAsync();
                if (Ready != null) await Ready.DisposeAsync();
                if (Available != null) await Available.DisposeAsync();
            }
            finally { await Task.Run(Native.Dispose); }
        }
    }
}
