// Copyright (c) .NET Foundation and Contributors (https://dotnetfoundation.org/ & https://stride3d.net) and Silicon Studio Corp. (https://www.siliconstudio.co.jp)
// Distributed under the MIT license. See the LICENSE.md file in the project root for more information.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using Stride.Assets.Editor.UIEditor.Adorners;
using Stride.Assets.Editor.UIEditor.Services;
using Stride.Assets.Presentation.ViewModels;
using Stride.Core.Extensions;
using Stride.Core.Mathematics;
using Stride.Engine;
using Stride.Input;
using Stride.Rendering.UI;
using Stride.UI;

namespace Stride.Assets.Editor.UIEditor.Game;

partial class UIEditorGameAdornerService
{
    private const float MinimumDragDistance = 4.0f;

    // selection state
    private readonly HashSet<Guid> selectedAdorners = [];

    // dragging state
    private Vector3 currentPosition;
    private IResizingAdorner? dragAdorner;
    private bool isDragging;
    private bool isInProgress;
    private float snapValue;

    // shared
    private Vector2 originScreenPosition;
    private Vector3 originWorldPosition;

    private readonly IReadOnlyCollection<Guid> emptyIds = [];

    public IReadOnlyCollection<Guid> GetElementIdsAtPosition(ref Vector3 worldPosition)
    {
        var hitResults = GetAdornerVisualsAtPosition(ref worldPosition);
        if (hitResults == null)
            return emptyIds;

        var elementIds = new List<Guid>();
        foreach (var hit in hitResults.OrderBy(r => r.IntersectionPoint.Z))
        {
            var visual = hit.Element;
            if (visual != null && visual.DependencyProperties.TryGetValue(AssociatedElementIdPropertyKey, out var elementId))
            {
                elementIds.Add(elementId);
            }
        }

        return elementIds;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal void ApplyChanges(Guid elementId, IReadOnlyDictionary<string, object> changes)
    {
        var editor = Controller.Editor;
        editor.Dispatcher.InvokeAsync(() => { editor.UpdateProperties(elementId, changes); });
    }

    #region Event Handlers
    private void PreviewTouchDown(object? sender, TouchEventArgs e)
    {
        if (Game.Input == null || !Game.Input.IsMouseButtonDown(MouseButton.Left))
            return;

        originWorldPosition = currentPosition = e.WorldPosition;
        originScreenPosition = e.ScreenPosition;

        if (selectedAdorners.Count == 0)
            return;

        var visuals = GetAdornerVisualsAtPosition(ref originWorldPosition);
        if (visuals == null)
            return;

        var resizingAdorners = (
            from hit in visuals
            let elementId = hit.Element.DependencyProperties.Get(AssociatedElementIdPropertyKey)
            where selectedAdorners.Contains(elementId)
            orderby hit.IntersectionPoint.Z
            let r = GetAssociatedAdorner(hit.Element) as IResizingAdorner
            where r != null
            select r).ToList();

        if (resizingAdorners.Count == 0)
            return;

        dragAdorner = resizingAdorners[0];
        isInProgress = true;
    }

    private void PreviewTouchMove(object? sender, TouchEventArgs e)
    {
        var worldPosition = e.WorldPosition;
        DoHighlightingAtPosition(ref worldPosition);
    }

    private void TouchMove(object? sender, TouchEventArgs e)
    {
        if (isInProgress)
        {
            if (dragAdorner == null || Game.Input == null || !Game.Input.IsMouseButtonDown(MouseButton.Left))
            {
                CancelDrag();
                return;
            }
            if (!isDragging)
            {
                var delta = (e.ScreenPosition - originScreenPosition) * new Vector2(Game.GraphicsDevice.Presenter.BackBuffer.Width, Game.GraphicsDevice.Presenter.BackBuffer.Height);
                if (Math.Abs(delta.X) > MinimumDragDistance || Math.Abs(delta.Y) > MinimumDragDistance)
                {
                    isDragging = true;
                    Controller.ChangeCursor(dragAdorner.GetCursor());
                    snapValue = Controller.Editor.SnapValue;
                    if (snapValue > float.Epsilon)
                    {
                        currentPosition.X = MathF.Round(currentPosition.X / snapValue) * snapValue;
                        currentPosition.Y = MathF.Round(currentPosition.Y / snapValue) * snapValue;
                    }
                }
            }
            if (isDragging)
            {
                var position = e.WorldPosition;
                if (snapValue > float.Epsilon)
                {
                    position.X = MathF.Round(position.X / snapValue) * snapValue;
                    position.Y = MathF.Round(position.Y / snapValue) * snapValue;
                }
                var delta = position - currentPosition;
                const float magnetDistance = 10;
                if (delta.LengthSquared() > float.Epsilon)
                {
                    var resolution = Controller.GetEntityByName(UIEditorController.UIEntityName)!.Get<UIComponent>().Resolution;
                    if (dragAdorner.ResizingDirection == ResizingDirection.Center
                        ? UILayoutHelper.Move(dragAdorner.GameSideElement, ref delta, magnetDistance, ref resolution)
                        : UILayoutHelper.Resize(dragAdorner.GameSideElement, dragAdorner.ResizingDirection, ref delta, magnetDistance, ref resolution))
                    {
                        dragAdorner.OnResizingDelta(delta.X, delta.Y);
                        currentPosition += delta;
                    }
                }
            }
            e.Handled = true;
            return;
        }

        if (Game.Input == null || !Game.Input.IsMouseButtonDown(MouseButton.Left))
            return;
    }

    private void TouchUp(object? sender, TouchEventArgs e)
    {
        if (Game.Input == null || !Game.Input.IsMouseButtonReleased(MouseButton.Left))
            return;

        isInProgress = false;
        Controller.ChangeCursor(null);
        e.Handled = true;

        if (isDragging)
        {
            isDragging = false;
            if (dragAdorner != null)
            {
                var changes = new Dictionary<string, object>
                {
                    { nameof(UIElement.Margin), dragAdorner.GameSideElement.Margin },
                    { nameof(UIElement.Width), dragAdorner.GameSideElement.Width },
                    { nameof(UIElement.HorizontalAlignment), dragAdorner.GameSideElement.HorizontalAlignment },
                    { nameof(UIElement.Height), dragAdorner.GameSideElement.Height },
                    { nameof(UIElement.VerticalAlignment), dragAdorner.GameSideElement.VerticalAlignment },
                };
                ApplyChanges(dragAdorner.GameSideElement.Id, changes);
                dragAdorner.OnResizingCompleted();
            }
            dragAdorner = null;
            return;
        }

        var editor = Controller.Editor;
        var worldPosition = e.WorldPosition;

        if (!TryGetElementIdAtPosition(ref worldPosition, out var elementId))
        {
            editor.Dispatcher.InvokeAsync(() => editor.ClearSelection());
            return;
        }

        var delta = (e.ScreenPosition - originScreenPosition) * new Vector2(Game.GraphicsDevice.Presenter.BackBuffer.Width, Game.GraphicsDevice.Presenter.BackBuffer.Height);
        if (Math.Abs(delta.X) < MinimumDragDistance && Math.Abs(delta.Y) < MinimumDragDistance)
        {
            var additiveSelection = Game.Input.IsKeyDown(Keys.LeftCtrl) || Game.Input.IsKeyDown(Keys.RightCtrl);
            editor.Dispatcher.InvokeAsync(() => editor.Select(elementId, additiveSelection)).Forget();
        }
    }
    #endregion

    private void CancelDrag()
    {
        if (!isInProgress)
            return;

        isInProgress = false;
        Controller.ChangeCursor(null);

        if (!isDragging)
            return;

        dragAdorner?.OnResizingCompleted();
    }

    private void DoHighlightingAtPosition(ref Vector3 worldPosition)
    {
        if (!TryGetElementIdAtPosition(ref worldPosition, out var elementId) || selectedAdorners.Contains(elementId))
        {
            DoUnlitAllAdorners();
            return;
        }

        DoHighlightAdorner(elementId);
    }

    private ICollection<UIRenderFeature.HitTestResult>? GetAdornerVisualsAtPosition(ref Vector3 worldPosition)
    {
        var entity = Controller.GetEntityByName(UIEditorController.AdornerEntityName);
        if (entity == null) return null;
        var uiComponent = entity.Get<UIComponent>();
        if (uiComponent == null) return null;

        if (Math.Abs(worldPosition.X) > uiComponent.Resolution.X * 0.5f ||
            Math.Abs(worldPosition.Y) > uiComponent.Resolution.Y * 0.5f)
            return null;

        var rootElement = uiComponent.Page?.RootElement;
        if (rootElement == null)
            return null;

        var ray = new Ray(new Vector3(worldPosition.X, worldPosition.Y, uiComponent.Resolution.Z + 1), -Vector3.UnitZ);
        var worldViewProj = Matrix.Identity;
        return UIRenderFeature.GetElementsAtPosition(rootElement, ref ray, ref worldViewProj);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static IAdornerBase<UIElement>? GetAssociatedAdorner(UIElement visual)
    {
        visual.DependencyProperties.TryGetValue(AssociatedAdornerPropertyKey, out var adorner);
        return adorner;
    }

    private bool TryGetElementIdAtPosition(ref Vector3 worldPosition, out Guid elementId)
    {
        var hitResults = GetAdornerVisualsAtPosition(ref worldPosition);
        var visual = hitResults?.OrderBy(r => -r.IntersectionPoint.Z).FirstOrDefault()?.Element;
        if (visual == null || !visual.DependencyProperties.TryGetValue(AssociatedElementIdPropertyKey, out elementId))
        {
            elementId = Guid.Empty;
            return false;
        }
        return true;
    }
}

