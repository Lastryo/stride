// Copyright (c) .NET Foundation and Contributors (https://dotnetfoundation.org/ & https://stride3d.net)
// Distributed under the MIT license. See the LICENSE.md file in the project root for more information.

using System.Collections.Specialized;
using Avalonia.Controls;
using Avalonia.Threading;
using Stride.Core.Assets.Editor.Services;
using Stride.Core.Assets.Editor.ViewModels;

namespace Stride.GameStudio.Avalonia.Views;

public partial class AssetPreviewView : UserControl
{
    private SessionViewModel? session;
    private IAssetPreviewService? previewService;

    public AssetPreviewView()
    {
        InitializeComponent();
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);

        if (session != null)
        {
            ((INotifyCollectionChanged)session.AssetCollection.SelectedAssets).CollectionChanged -= OnSelectedAssetsChanged;
        }
        if (previewService != null)
        {
            previewService.PreviewAssetUpdated -= OnPreviewAssetUpdated;
        }

        session = DataContext as SessionViewModel;
        previewService = session?.ServiceProvider.TryGet<IAssetPreviewService>();

        if (session != null && previewService != null)
        {
            previewService.PreviewAssetUpdated += OnPreviewAssetUpdated;
            ((INotifyCollectionChanged)session.AssetCollection.SelectedAssets).CollectionChanged += OnSelectedAssetsChanged;
            UpdateSelectedAsset();
        }
        else
        {
            PreviewPresenter.Content = null;
        }
    }

    private void OnSelectedAssetsChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        UpdateSelectedAsset();
    }

    private void UpdateSelectedAsset()
    {
        if (session == null || previewService == null)
            return;

        previewService.SetAssetToPreview(session.AssetCollection.SelectedAssets.Count == 1
            ? session.AssetCollection.SelectedAssets[0]
            : null);
    }

    private void OnPreviewAssetUpdated(object? sender, EventArgs e)
    {
        Dispatcher.UIThread.Post(() => PreviewPresenter.Content = previewService?.GetCurrentPreviewView());
    }
}
