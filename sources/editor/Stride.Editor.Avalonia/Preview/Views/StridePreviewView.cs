// Copyright (c) .NET Foundation and Contributors (https://dotnetfoundation.org/ & https://stride3d.net) and Silicon Studio Corp. (https://www.siliconstudio.co.jp)
// Distributed under the MIT license. See the LICENSE.md file in the project root for more information.

using Avalonia.Controls;
using Avalonia.Controls.Metadata;
using Avalonia.Controls.Presenters;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Templates;
using Avalonia.Interactivity;
using Stride.Editor.Preview;
using Stride.Editor.Preview.Views;

namespace Stride.Editor.Avalonia.Preview.Views;

[TemplatePart(Name = "PART_StrideView", Type = typeof(ContentPresenter))]
public class StridePreviewView : TemplatedControl, IPreviewView
{
    private IPreviewBuilder? builder;
    private IAssetPreview? previewer;
    private ContentPresenter? presenter;

    static StridePreviewView()
    {
        TemplateProperty.OverrideDefaultValue<StridePreviewView>(
            new FuncControlTemplate<StridePreviewView>((_, scope) =>
                new ContentPresenter { Name = "PART_StrideView" }.RegisterInNameScope(scope)));
    }

    public void InitializeView(IPreviewBuilder previewBuilder, IAssetPreview assetPreview)
    {
        previewer = assetPreview;
        builder = previewBuilder;
        var viewModel = previewer.PreviewViewModel;
        if (viewModel != null)
        {
            viewModel.AttachPreview(previewer);
            DataContext = viewModel;
        }
        UpdateStrideView();

        Loaded += OnLoaded;
    }

    public void UpdateView(IAssetPreview assetPreview)
    {
        var viewModel = previewer?.PreviewViewModel;
        if (viewModel != null)
        {
            viewModel.AttachPreview(previewer!);
            DataContext = viewModel;
        }
        UpdateStrideView();
    }

    /// <inheritdoc />
    protected override void OnApplyTemplate(TemplateAppliedEventArgs e)
    {
        base.OnApplyTemplate(e);
        presenter = e.NameScope.Find<ContentPresenter>("PART_StrideView");
        UpdateStrideView();
    }

    private void OnLoaded(object? sender, RoutedEventArgs routedEventArgs)
    {
        previewer?.OnViewAttached();
    }

    private void UpdateStrideView()
    {
        if (presenter != null && builder != null)
        {
            var strideView = builder.GetStrideView();
            if ((strideView as Control)?.Parent is ContentPresenter oldPresenter && oldPresenter != presenter)
            {
                oldPresenter.Content = null;
            }
            presenter.Content = strideView;
        }
    }
}
