// Copyright (c) .NET Foundation and Contributors (https://dotnetfoundation.org/ & https://stride3d.net) and Silicon Studio Corp. (https://www.siliconstudio.co.jp)
// Distributed under the MIT license. See the LICENSE.md file in the project root for more information.

using Stride.Core.Presentation.Services;
using Stride.Core.Presentation.ViewModels;
using Stride.Core.Storage;

namespace Stride.Core.Assets.Presentation.ViewModels;

public abstract class ThumbnailData : ViewModelBase
{
    protected readonly ObjectId thumbnailId;
    private object? presenter;
    private bool preparing;
    private bool hasError;

    protected ThumbnailData(ObjectId thumbnailId) : base(ViewModelServiceProvider.NullServiceProvider)
    {
        this.thumbnailId = thumbnailId;
    }

    public bool HasError { get => hasError; set => SetValue(ref hasError, value); }

    public object? Presenter
    {
        get => presenter;
        private set => SetValue(ref presenter, value);
    }

    public async Task PrepareForPresentation(IDispatcherService dispatcher)
    {
        if (IsDestroyed) return;
        preparing = true;
        try
        {
            var result = await Task.Run(BuildImageSource);
            dispatcher.Invoke(() =>
            {
                if (IsDestroyed) (result as IDisposable)?.Dispose();
                else
                {
                    HasError |= result == null;
                    Presenter = result;
                }
            });
        }
        finally { FreeBuildingResources(); }
    }

    public override void Destroy()
    {
        base.Destroy();
        if (!preparing) FreeBuildingResources();
        var previous = Presenter;
        Presenter = null;
        (previous as IDisposable)?.Dispose();
    }

    protected abstract object? BuildImageSource();
    protected abstract void FreeBuildingResources();
}
