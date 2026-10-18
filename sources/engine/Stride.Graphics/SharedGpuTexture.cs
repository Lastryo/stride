// Copyright (c) .NET Foundation and Contributors (https://dotnetfoundation.org/ & https://stride3d.net)
// Distributed under the MIT license. See the LICENSE.md file in the project root for more information.

using System;

namespace Stride.Graphics;

/// <summary>An offscreen GPU image and semaphore pair shared with another graphics device.</summary>
public abstract class SharedGpuTexture : IDisposable
{
    public abstract int Width { get; }
    public abstract int Height { get; }
    public abstract ulong MemorySize { get; }
    public abstract string HandleType { get; }
    public abstract bool IsReady { get; }
    public abstract IntPtr ExportMemoryHandle();
    public abstract IntPtr ExportReadySemaphore();
    public abstract IntPtr ExportAvailableSemaphore();

    /// <summary>Copies a finished frame on the GPU. Reuse only after the consumer signals availability.</summary>
    public abstract void CopyFrom(CommandList commandList, Texture source);
    public abstract void Dispose();

    public static SharedGpuTexture TryCreate(GraphicsDevice device, int width, int height, byte[] consumerDeviceUuid)
    {
#if STRIDE_GRAPHICS_API_VULKAN
        if (OperatingSystem.IsLinux() && VulkanSharedGpuTexture.IsSupported(device, consumerDeviceUuid))
            return new VulkanSharedGpuTexture(device, width, height);
#endif
        return null;
    }
}
