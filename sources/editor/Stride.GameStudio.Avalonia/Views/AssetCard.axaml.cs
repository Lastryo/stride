// Copyright (c) .NET Foundation and Contributors (https://dotnetfoundation.org/ & https://stride3d.net)
// Distributed under the MIT license. See the LICENSE.md file in the project root for more information.

using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.VisualTree;
using Stride.Core.Assets.Editor.Avalonia;
using Stride.Core.Assets.Presentation.ViewModels;

namespace Stride.GameStudio.Avalonia.Views;

public partial class AssetCard : UserControl
{
    public static readonly StyledProperty<double> ThumbnailSizeProperty = AvaloniaProperty.Register<AssetCard, double>(nameof(ThumbnailSize), 96);
    public double ThumbnailSize { get => GetValue(ThumbnailSizeProperty); set => SetValue(ThumbnailSizeProperty, value); }

    private Point dragStartPoint;
    private bool isPointerPressed;
    private PointerPressedEventArgs? pointerPressedArgs;

    public AssetCard() => InitializeComponent();

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed && DataContext is AssetViewModel)
        {
            if (e.Source is Visual visual && visual.FindAncestorOfType<Button>(true) != null)
                return;

            dragStartPoint = e.GetPosition(this);
            isPointerPressed = true;
            pointerPressedArgs = e;
        }
    }

    protected override async void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        if (!isPointerPressed || pointerPressedArgs == null || DataContext is not AssetViewModel asset)
            return;

        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            isPointerPressed = false;
            pointerPressedArgs = null;
            return;
        }

        var diff = e.GetPosition(this) - dragStartPoint;
        if (Math.Abs(diff.X) > 4 || Math.Abs(diff.Y) > 4)
        {
            isPointerPressed = false;
            var startArgs = pointerPressedArgs;
            pointerPressedArgs = null;

            var parentList = this.FindAncestorOfType<ListBox>();
            IReadOnlyList<AssetViewModel> itemsToDrag;
            if (parentList?.SelectedItems != null && parentList.SelectedItems.Contains(asset))
            {
                itemsToDrag = parentList.SelectedItems.OfType<AssetViewModel>().ToList();
            }
            else
            {
                itemsToDrag = [asset];
            }

            EditorDragDrop.CurrentDragData = itemsToDrag;
            try
            {
                await DragDrop.DoDragDropAsync(startArgs, new DataTransfer(), DragDropEffects.Copy | DragDropEffects.Move);
            }
            finally
            {
                EditorDragDrop.CurrentDragData = null;
            }
        }
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        isPointerPressed = false;
        pointerPressedArgs = null;
    }

    protected override void OnPointerCaptureLost(PointerCaptureLostEventArgs e)
    {
        base.OnPointerCaptureLost(e);
        isPointerPressed = false;
        pointerPressedArgs = null;
    }
}
