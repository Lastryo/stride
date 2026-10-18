// Copyright (c) .NET Foundation and Contributors (https://dotnetfoundation.org/ & https://stride3d.net)
// Distributed under the MIT license. See the LICENSE.md file in the project root for more information.

namespace Stride.Graphics;

/// <summary>
///   A graphics presenter that renders to an offscreen render target without a window or swap chain.
///   Used for headless testing with software renderers.
/// </summary>
internal class HeadlessGraphicsPresenter : GraphicsPresenter
{
    private readonly Texture backBuffer;

    public HeadlessGraphicsPresenter(GraphicsDevice device, PresentationParameters presentationParameters)
        : base(device, presentationParameters)
    {
        backBuffer = Texture.New2D(device, Description.BackBufferWidth, Description.BackBufferHeight,
            Description.BackBufferFormat, TextureFlags.ShaderResource | TextureFlags.RenderTarget);
    }

    public override Texture BackBuffer => backBuffer;

    public override object NativePresenter => null;

    public override bool IsFullScreen { get; set; }

    public override void Present() { }

    protected override void ResizeBackBuffer(int width, int height, PixelFormat format)
    {
        ResizeTexture(backBuffer, width, height, format);
    }

    protected override void ResizeDepthStencilBuffer(int width, int height, PixelFormat format)
    {
        ResizeTexture(DepthStencilBuffer, width, height, format);
    }

    private void ResizeTexture(Texture texture, int width, int height, PixelFormat format)
    {
        // Renderers retain these objects and their views across frames, like swap-chain buffers.
        var children = DestroyChildrenTextures(texture);
        var description = texture.Description;
        description.Width = width;
        description.Height = height;
        description.Format = format;
        texture.OnDestroyed(true);
        texture.InitializeFrom(description);
        foreach (var child in children)
            child.InitializeFrom(texture, child.ViewDescription);
    }

    protected override void Destroy()
    {
        backBuffer?.Dispose();
        base.Destroy();
    }
}
