// Copyright (c) .NET Foundation and Contributors (https://dotnetfoundation.org/ & https://stride3d.net)
// Distributed under the MIT license. See the LICENSE.md file in the project root for more information.
#if STRIDE_GRAPHICS_API_VULKAN
using System;
using Vortice.Vulkan;

namespace Stride.Graphics;

internal sealed unsafe class VulkanSharedGpuTexture : SharedGpuTexture
{
    private readonly GraphicsDevice device;
    private VkImage image;
    private VkDeviceMemory memory;
    private VkSemaphore ready, available;
    private VkCommandPool pool;
    private VkCommandBuffer commands;
    private VkFence fence;
    private bool submitted;
    private bool disposed;
    public override int Width { get; }
    public override int Height { get; }
    public override ulong MemorySize { get; }
    public override string HandleType => "VulkanOpaquePosixFileDescriptor";
    public override bool IsReady => !submitted || device.NativeDeviceApi.vkGetFenceStatus(device.NativeDevice, fence) == VkResult.Success;

    public static bool IsSupported(GraphicsDevice device, byte[] uuid)
    {
        if (!device.SupportsExternalGpuMemory) return false;
        var id = new VkPhysicalDeviceIDProperties { sType = VkStructureType.PhysicalDeviceIdProperties };
        var properties = new VkPhysicalDeviceProperties2 { sType = VkStructureType.PhysicalDeviceProperties2, pNext = &id };
        device.NativeInstanceApi.vkGetPhysicalDeviceProperties2(device.NativePhysicalDevice, &properties);
        return uuid != null && uuid.Length == 16 && new ReadOnlySpan<byte>(id.deviceUUID, 16).SequenceEqual(uuid);
    }

    public VulkanSharedGpuTexture(GraphicsDevice device, int width, int height)
    {
        this.device = device;
        Width = width;
        Height = height;
        var api = device.NativeDeviceApi;
        try
        {
            var external = new VkExternalMemoryImageCreateInfo
            {
                sType = VkStructureType.ExternalMemoryImageCreateInfo,
                handleTypes = VkExternalMemoryHandleTypeFlags.OpaqueFD,
            };
            var info = new VkImageCreateInfo
            {
                sType = VkStructureType.ImageCreateInfo, pNext = &external,
                imageType = VkImageType.Image2D, format = VkFormat.R8G8B8A8Unorm,
                extent = new VkExtent3D((uint)width, (uint)height, 1), mipLevels = 1, arrayLayers = 1,
                samples = VkSampleCountFlags.Count1, tiling = VkImageTiling.Optimal,
                usage = VkImageUsageFlags.TransferDst | VkImageUsageFlags.TransferSrc | VkImageUsageFlags.Sampled,
                sharingMode = VkSharingMode.Exclusive, initialLayout = VkImageLayout.Undefined,
            };
            device.CheckResult(api.vkCreateImage(device.NativeDevice, &info, null, out image));
            api.vkGetImageMemoryRequirements(device.NativeDevice, image, out var requirements);
            MemorySize = requirements.size;
            var dedicated = new VkMemoryDedicatedAllocateInfo { sType = VkStructureType.MemoryDedicatedAllocateInfo, image = image };
            var export = new VkExportMemoryAllocateInfo
            {
                sType = VkStructureType.ExportMemoryAllocateInfo, pNext = &dedicated,
                handleTypes = VkExternalMemoryHandleTypeFlags.OpaqueFD,
            };
            device.NativeInstanceApi.vkGetPhysicalDeviceMemoryProperties(device.NativePhysicalDevice, out var types);
            uint memoryType = uint.MaxValue;
            for (uint i = 0; i < types.memoryTypeCount; i++)
                if ((requirements.memoryTypeBits & (1u << (int)i)) != 0 && (types.memoryTypes[(int)i].propertyFlags & VkMemoryPropertyFlags.DeviceLocal) != 0)
                { memoryType = i; break; }
            if (memoryType == uint.MaxValue) throw new NotSupportedException("No device-local memory for a shared viewport image.");
            var allocation = new VkMemoryAllocateInfo
            {
                sType = VkStructureType.MemoryAllocateInfo, pNext = &export,
                allocationSize = MemorySize, memoryTypeIndex = memoryType,
            };
            device.CheckResult(api.vkAllocateMemory(device.NativeDevice, &allocation, null, out memory));
            device.CheckResult(api.vkBindImageMemory(device.NativeDevice, image, memory, 0));
            ready = CreateSemaphore();
            available = CreateSemaphore();
            var poolInfo = new VkCommandPoolCreateInfo
            {
                sType = VkStructureType.CommandPoolCreateInfo, flags = VkCommandPoolCreateFlags.ResetCommandBuffer, queueFamilyIndex = 0,
            };
            device.CheckResult(api.vkCreateCommandPool(device.NativeDevice, &poolInfo, null, out pool));
            var bufferInfo = new VkCommandBufferAllocateInfo
            {
                sType = VkStructureType.CommandBufferAllocateInfo, commandPool = pool, level = VkCommandBufferLevel.Primary, commandBufferCount = 1,
            };
            fixed (VkCommandBuffer* pointer = &commands)
                device.CheckResult(api.vkAllocateCommandBuffers(device.NativeDevice, &bufferInfo, pointer));
            var fenceInfo = new VkFenceCreateInfo { sType = VkStructureType.FenceCreateInfo };
            device.CheckResult(api.vkCreateFence(device.NativeDevice, &fenceInfo, null, out fence));
        }
        catch { Dispose(); throw; }
    }

    private VkSemaphore CreateSemaphore()
    {
        var export = new VkExportSemaphoreCreateInfo
        {
            sType = VkStructureType.ExportSemaphoreCreateInfo, handleTypes = VkExternalSemaphoreHandleTypeFlags.OpaqueFD,
        };
        var info = new VkSemaphoreCreateInfo { sType = VkStructureType.SemaphoreCreateInfo, pNext = &export };
        device.CheckResult(device.NativeDeviceApi.vkCreateSemaphore(device.NativeDevice, &info, null, out var semaphore));
        return semaphore;
    }

    public override IntPtr ExportMemoryHandle()
    {
        var info = new VkMemoryGetFdInfoKHR
        {
            sType = VkStructureType.MemoryGetFDInfoKHR, memory = memory, handleType = VkExternalMemoryHandleTypeFlags.OpaqueFD,
        };
        int fd;
        device.CheckResult(device.NativeDeviceApi.vkGetMemoryFdKHR(device.NativeDevice, &info, &fd));
        return fd;
    }

    private IntPtr ExportSemaphore(VkSemaphore semaphore)
    {
        var info = new VkSemaphoreGetFdInfoKHR
        {
            sType = VkStructureType.SemaphoreGetFDInfoKHR, semaphore = semaphore, handleType = VkExternalSemaphoreHandleTypeFlags.OpaqueFD,
        };
        int fd;
        device.CheckResult(device.NativeDeviceApi.vkGetSemaphoreFdKHR(device.NativeDevice, &info, &fd));
        return fd;
    }
    public override IntPtr ExportReadySemaphore() => ExportSemaphore(ready);
    public override IntPtr ExportAvailableSemaphore() => ExportSemaphore(available);

    public override void CopyFrom(CommandList commandList, Texture source)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        if (!IsReady) throw new InvalidOperationException("The shared viewport image is still in use.");
        if (source.Width != Width || source.Height != Height) throw new ArgumentException("Shared image dimensions do not match the source.");
        commandList.ResourceBarrierTransition(source, BarrierLayout.CopySource);
        commandList.Flush();
        commandList.Reset();
        var api = device.NativeDeviceApi;
        var copyCommands = commands;
        var copyFence = fence;
        if (submitted)
        {
            device.CheckResult(api.vkResetFences(device.NativeDevice, 1, &copyFence));
            device.CheckResult(api.vkResetCommandBuffer(commands, VkCommandBufferResetFlags.None));
        }
        var begin = new VkCommandBufferBeginInfo { sType = VkStructureType.CommandBufferBeginInfo, flags = VkCommandBufferUsageFlags.OneTimeSubmit };
        device.CheckResult(api.vkBeginCommandBuffer(commands, &begin));
        var range = new VkImageSubresourceRange(VkImageAspectFlags.Color, 0, 1, 0, 1);
        var acquire = new VkImageMemoryBarrier(image, range, VkAccessFlags.None, VkAccessFlags.TransferWrite,
            submitted ? VkImageLayout.TransferSrcOptimal : VkImageLayout.Undefined, VkImageLayout.TransferDstOptimal)
        {
            srcQueueFamilyIndex = submitted ? 0xfffffffeu : uint.MaxValue,
            dstQueueFamilyIndex = submitted ? 0u : uint.MaxValue,
        };
        api.vkCmdPipelineBarrier(commands, VkPipelineStageFlags.TopOfPipe, VkPipelineStageFlags.Transfer, VkDependencyFlags.None, 0, null, 0, null, 1, &acquire);
        var copy = new VkImageCopy
        {
            srcSubresource = new VkImageSubresourceLayers(VkImageAspectFlags.Color, 0, 0, 1),
            dstSubresource = new VkImageSubresourceLayers(VkImageAspectFlags.Color, 0, 0, 1),
            extent = new VkExtent3D((uint)Width, (uint)Height, 1),
        };
        api.vkCmdCopyImage(commands, source.NativeImage, VkImageLayout.TransferSrcOptimal, image, VkImageLayout.TransferDstOptimal, 1, &copy);
        var release = new VkImageMemoryBarrier(image, range, VkAccessFlags.TransferWrite, VkAccessFlags.None,
            VkImageLayout.TransferDstOptimal, VkImageLayout.TransferSrcOptimal)
        { srcQueueFamilyIndex = 0, dstQueueFamilyIndex = 0xfffffffeu };
        api.vkCmdPipelineBarrier(commands, VkPipelineStageFlags.Transfer, VkPipelineStageFlags.BottomOfPipe, VkDependencyFlags.None, 0, null, 0, null, 1, &release);
        device.CheckResult(api.vkEndCommandBuffer(commands));
        var wait = available;
        var signal = ready;
        var stage = VkPipelineStageFlags.Transfer;
        var submit = new VkSubmitInfo
        {
            sType = VkStructureType.SubmitInfo, commandBufferCount = 1, pCommandBuffers = &copyCommands,
            waitSemaphoreCount = submitted ? 1u : 0u, pWaitSemaphores = &wait, pWaitDstStageMask = &stage,
            signalSemaphoreCount = 1, pSignalSemaphores = &signal,
        };
        lock (device.QueueLock)
            device.CheckResult(api.vkQueueSubmit(device.NativeCommandQueue, 1, &submit, fence));
        submitted = true;
    }

    public override void Dispose()
    {
        if (disposed) return;
        disposed = true;
        var api = device.NativeDeviceApi;
        if (submitted)
        {
            var copyFence = fence;
            device.CheckResult(api.vkWaitForFences(device.NativeDevice, 1, &copyFence, true, ulong.MaxValue));
        }
        if (pool != VkCommandPool.Null) api.vkDestroyCommandPool(device.NativeDevice, pool, null);
        if (fence != VkFence.Null) api.vkDestroyFence(device.NativeDevice, fence, null);
        if (ready != VkSemaphore.Null) api.vkDestroySemaphore(device.NativeDevice, ready, null);
        if (available != VkSemaphore.Null) api.vkDestroySemaphore(device.NativeDevice, available, null);
        if (image != VkImage.Null) api.vkDestroyImage(device.NativeDevice, image, null);
        if (memory != VkDeviceMemory.Null) api.vkFreeMemory(device.NativeDevice, memory, null);
    }
}
#endif
