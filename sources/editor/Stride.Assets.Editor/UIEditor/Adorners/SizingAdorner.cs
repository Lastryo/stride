// Copyright (c) .NET Foundation and Contributors (https://dotnetfoundation.org/ & https://stride3d.net) and Silicon Studio Corp. (https://www.siliconstudio.co.jp)
// Distributed under the MIT license. See the LICENSE.md file in the project root for more information.

using System;
using Stride.Assets.Editor.UIEditor.Game;
using Stride.Assets.Presentation.ViewModels;
using Stride.Core.Mathematics;
using Stride.UI;
using Stride.UI.Panels;

namespace Stride.Assets.Editor.UIEditor.Adorners;

/// <summary>
/// Represents an adorner that can resize the associated <see cref="UIElement"/> in a specific direction.
/// </summary>
internal sealed class SizingAdorner : BorderAdorner, IResizingAdorner
{
    private bool isDragging;
    private ResizingDirection resizingDirection;

    public SizingAdorner(UIEditorGameAdornerService service, UIElement gameSideElement, ResizingDirection resizingDirection)
        : base(service, gameSideElement)
    {
        BackgroundColor = Color.Transparent;
        Visual.Name = $"[Sizing] {resizingDirection}";
        Visual.CanBeHitByUser = true;

        ResizingDirection = resizingDirection;
        Visual.MouseOverStateChanged += MouseOverStateChanged;
    }

    public ResizingDirection ResizingDirection
    {
        get => resizingDirection;
        set
        {
            resizingDirection = value;
            UpdateRelativePosition();
        }
    }

    public UICursorType GetCursor()
    {
        return ResizingDirection switch
        {
            ResizingDirection.Center => UICursorType.SizeAll,
            ResizingDirection.Left or ResizingDirection.Right => UICursorType.SizeWE,
            ResizingDirection.Top or ResizingDirection.Bottom => UICursorType.SizeNS,
            ResizingDirection.TopLeft or ResizingDirection.BottomRight => UICursorType.SizeNWSE,
            ResizingDirection.TopRight or ResizingDirection.BottomLeft => UICursorType.SizeNESW,
            _ => throw new ArgumentOutOfRangeException(),
        };
    }

    public override void Update(Vector3 position)
    {
        UpdateFromSettings();
    }

    private void UpdateFromSettings()
    {
        var editor = Service.Controller.Editor;

        BorderColor = editor.SizingColor;
        BorderThickness = editor.SizingThickness;
        Size = new Vector3(Math.Max(8, editor.SizingThickness * 4));
    }

    private void UpdateRelativePosition()
    {
        Vector3 relativePosition;
        switch (resizingDirection)
        {
            case ResizingDirection.Center:
                throw new InvalidOperationException();

            case ResizingDirection.TopLeft:
                relativePosition = new Vector3(0.0f, 0.0f, 0.5f);
                break;

            case ResizingDirection.Top:
                relativePosition = new Vector3(0.5f, 0.0f, 0.5f);
                break;

            case ResizingDirection.TopRight:
                relativePosition = new Vector3(1.0f, 0.0f, 0.5f);
                break;

            case ResizingDirection.Right:
                relativePosition = new Vector3(1.0f, 0.5f, 0.5f);
                break;

            case ResizingDirection.BottomRight:
                relativePosition = new Vector3(1.0f, 1.0f, 0.5f);
                break;

            case ResizingDirection.Bottom:
                relativePosition = new Vector3(0.5f, 1.0f, 0.5f);
                break;

            case ResizingDirection.BottomLeft:
                relativePosition = new Vector3(0.0f, 1.0f, 0.5f);
                break;

            case ResizingDirection.Left:
                relativePosition = new Vector3(0.0f, 0.5f, 0.5f);
                break;

            default:
                throw new ArgumentOutOfRangeException();
        }
        Visual.SetCanvasRelativePosition(relativePosition);
        Visual.SetCanvasPinOrigin(Vector3.One - relativePosition);
    }

    void IResizingAdorner.OnResizingDelta(float horizontalChange, float verticalChange)
    {
        isDragging = true;
    }

    void IResizingAdorner.OnResizingCompleted()
    {
        isDragging = false;
    }

    private void MouseOverStateChanged(object? sender, PropertyChangedArgs<MouseOverState> e)
    {
        if (isDragging)
            return;

        Service.Controller.ChangeCursor(e.NewValue != MouseOverState.MouseOverNone ? GetCursor() : null);
    }
}

