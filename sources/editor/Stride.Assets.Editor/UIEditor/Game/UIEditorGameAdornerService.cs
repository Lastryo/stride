// Copyright (c) .NET Foundation and Contributors (https://dotnetfoundation.org/ & https://stride3d.net) and Silicon Studio Corp. (https://www.siliconstudio.co.jp)
// Distributed under the MIT license. See the LICENSE.md file in the project root for more information.

using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using Stride.Assets.Editor.UIEditor.Adorners;
using Stride.Assets.Editor.UIEditor.Services;
using Stride.Assets.Presentation.ViewModels;
using Stride.Core;
using Stride.Core.Annotations;
using Stride.Core.Extensions;
using Stride.Core.Mathematics;
using Stride.Editor.EditorGame.Game;
using Stride.Editor.EditorGame.ViewModels;
using Stride.Engine;
using Stride.Rendering.Compositing;
using Stride.UI;
using Stride.UI.Panels;

namespace Stride.Assets.Editor.UIEditor.Game;

public sealed partial class UIEditorGameAdornerService : EditorGameServiceBase, IEditorGameViewModelService
{
    private class AdornerLayer
    {
        private readonly List<IAdornerBase<UIElement>> adorners = [];
        private readonly UIElement gameSideElement;
        private readonly Canvas canvas;
        private readonly Canvas parentCanvas;

        private Thickness prevMargin;
        private Guid? prevParentId;
        private Vector3 prevRenderSize;
        private Matrix prevWorldMatrix;

        private HighlightAdorner? highlightAdorner;

        public AdornerLayer(UIElement gameSideElement)
        {
            ArgumentNullException.ThrowIfNull(gameSideElement);

            this.gameSideElement = gameSideElement;
            canvas = new Canvas();
            parentCanvas = new Canvas
            {
                Children = { canvas },
                Name = $"Adorner layer of {gameSideElement.Name ?? gameSideElement.Id.ToString()}",
            };
        }

        public bool IsEnabled { get; private set; }

        public bool IsHighlighted => highlightAdorner?.IsHighlighted ?? false;

        public UIElement Visual => parentCanvas;

        public void Add(IAdornerBase<UIElement> adorner, bool addToParentLayer = false)
        {
            if (addToParentLayer)
            {
                parentCanvas.Children.Add(adorner.Visual);
            }
            else
            {
                canvas.Children.Add(adorner.Visual);
            }
            adorners.Add(adorner);
        }

        public bool CheckValidity()
        {
            if (prevMargin == gameSideElement.Margin &&
                prevParentId == gameSideElement.VisualParent?.Id &&
                prevRenderSize == gameSideElement.RenderSize &&
                prevWorldMatrix == gameSideElement.WorldMatrix)
            {
                return true;
            }
            prevMargin = gameSideElement.Margin;
            prevParentId = gameSideElement.VisualParent?.Id;
            prevRenderSize = gameSideElement.RenderSize;
            prevWorldMatrix = gameSideElement.WorldMatrix;
            return false;
        }

        public void Disable()
        {
            IsEnabled = false;
            foreach (var adorner in adorners)
            {
                adorner.Disable();
            }
            highlightAdorner?.Disable();
        }

        public void Enable()
        {
            IsEnabled = true;
            foreach (var adorner in adorners)
            {
                adorner.Enable();
            }
            highlightAdorner?.Enable();
        }

        public void Hide()
        {
            foreach (var adorner in adorners)
            {
                adorner.Hide();
            }
            highlightAdorner?.Show();
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Highlight()
        {
            highlightAdorner?.Highlight();
        }

        public void Invalidate()
        {
            prevMargin = Thickness.UniformCuboid(float.NaN);
            prevParentId = null;
            prevRenderSize = new Vector3(float.NaN);
            prevWorldMatrix = new Matrix(float.NaN);
        }

        public void SetHighlightAdorner(HighlightAdorner value)
        {
            if (highlightAdorner != null)
            {
                canvas.Children.Remove(highlightAdorner.Visual);
            }
            highlightAdorner = value;
            if (highlightAdorner != null)
            {
                canvas.Children.Add(highlightAdorner.Visual);
            }
        }

        public void Show()
        {
            foreach (var adorner in adorners)
            {
                adorner.Show();
            }
            highlightAdorner?.Hide();
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Unlit()
        {
            highlightAdorner?.Unlit();
        }

        public void Update(ref Vector3 availableSize)
        {
            Vector3 parentPosition;
            Vector3 parentSize;
            Matrix parentMatrixInv;
            var parent = gameSideElement.VisualParent;
            if (parent != null)
            {
                var parentMatrix = parent.WorldMatrix;
                parentPosition = parentMatrix.TranslationVector + availableSize * 0.5f;
                parentSize = parent.RenderSize;
                parentMatrixInv = Matrix.Invert(parent.WorldMatrix);
            }
            else
            {
                parentPosition = availableSize * 0.5f;
                parentSize = availableSize;
                parentMatrixInv = Matrix.Identity;
            }
            parentCanvas.Size = parentSize;
            parentCanvas.SetCanvasAbsolutePosition(parentPosition);
            parentCanvas.SetCanvasPinOrigin(0.5f * Vector3.One);

            var diffMatrix = Matrix.Multiply(parentMatrixInv, gameSideElement.WorldMatrix);
            var position = diffMatrix.TranslationVector + parentSize * 0.5f;
            canvas.Size = gameSideElement.RenderSize;
            canvas.Margin = new Thickness(0, 0, 0, 0, 0, -1 * (gameSideElement.DepthBias + 1));

            canvas.SetCanvasAbsolutePosition(position);
            canvas.SetCanvasPinOrigin(0.5f * Vector3.One);

            adorners.ForEach(a => a.Update(position));
            highlightAdorner?.Update(position);
        }
    }

    internal static readonly PropertyKey<IAdornerBase<UIElement>> AssociatedAdornerPropertyKey = DependencyPropertyFactory.RegisterAttached(nameof(AssociatedAdornerPropertyKey), typeof(UIEditorGameAdornerService), (IAdornerBase<UIElement>)null!);
    internal static readonly PropertyKey<Guid> AssociatedElementIdPropertyKey = DependencyPropertyFactory.RegisterAttached(nameof(AssociatedElementIdPropertyKey), typeof(UIEditorGameAdornerService), Guid.Empty);

    private readonly Dictionary<Guid, AdornerLayer> adornerLayers = [];

    public UIEditorGameAdornerService(UIEditorController controller)
    {
        Controller = controller;
    }

    internal EditorServiceGame Game { get; private set; } = null!;

    internal UIEditorController Controller { get; }

    public override ValueTask DisposeAsync()
    {
        EnsureNotDestroyed(nameof(UIEditorGameAdornerService));

        adornerLayers.Clear();
        GetAdornerCanvas()?.Children.Clear();

        return base.DisposeAsync();
    }

    public Task AddAdorner(Guid elementId, bool enable = false)
    {
        return Controller.InvokeAsync(() =>
        {
            DoAddAdorner(elementId);
            if (enable)
            {
                DoEnableAdorner(elementId);
            }
        });
    }

    public Task ClearSelection()
    {
        return Controller.InvokeAsync(DoHideAllAdorners);
    }

    public Task DisableAdorner(Guid elementId)
    {
        return Controller.InvokeAsync(() => DoDisableAdorner(elementId));
    }

    public Task EnableAdorner(Guid elementId)
    {
        return Controller.InvokeAsync(() => DoEnableAdorner(elementId));
    }

    public Task HighlightAdorner(Guid elementId)
    {
        return Controller.InvokeAsync(() => DoHighlightAdorner(elementId));
    }

    public Task Refresh()
    {
        return Controller.InvokeAsync(() =>
        {
            adornerLayers.Values.ForEach(l => l.Invalidate());
        });
    }

    public Task RemoveAdorner(Guid elementId)
    {
        return Controller.InvokeAsync(() => DoRemoveAdorner(elementId));
    }

    public Task SelectAdorner(Guid elementId) => SelectElement(elementId);

    public Task SelectElement(Guid elementId)
    {
        return Controller.InvokeAsync(() =>
        {
            DoHideAllAdorners();
            DoSelectElement(elementId);
        });
    }

    public Task SelectElements(IEnumerable<Guid> elementIds)
    {
        return Controller.InvokeAsync(() =>
        {
            DoHideAllAdorners();
            foreach (var elementId in elementIds)
            {
                DoSelectElement(elementId);
            }
        });
    }

    public Task UnlitAllAdorners()
    {
        return Controller.InvokeAsync(DoUnlitAllAdorners);
    }

    protected override Task<bool> Initialize(EditorServiceGame editorGame)
    {
        Game = editorGame;
        Game.Script.AddTask(Update);
        return Task.FromResult(true);
    }

    private AdornerLayer? CreateLayer(UIElement gameSideElement)
    {
        var canvas = GetAdornerCanvas();
        if (canvas == null)
            return null;

        var font = Controller.DefaultFont;
        var layer = new AdornerLayer(gameSideElement);

        layer.Add(new MarginAdorner(this, gameSideElement, MarginEdge.Left, font), true);
        layer.Add(new MarginAdorner(this, gameSideElement, MarginEdge.Right, font), true);
        layer.Add(new MarginAdorner(this, gameSideElement, MarginEdge.Top, font), true);
        layer.Add(new MarginAdorner(this, gameSideElement, MarginEdge.Bottom, font), true);

        layer.SetHighlightAdorner(new HighlightAdorner(this, gameSideElement));
        layer.Add(new MoveAdorner(this, gameSideElement));

        layer.Add(new SizingAdorner(this, gameSideElement, ResizingDirection.TopLeft));
        layer.Add(new SizingAdorner(this, gameSideElement, ResizingDirection.Top));
        layer.Add(new SizingAdorner(this, gameSideElement, ResizingDirection.TopRight));
        layer.Add(new SizingAdorner(this, gameSideElement, ResizingDirection.Right));
        layer.Add(new SizingAdorner(this, gameSideElement, ResizingDirection.BottomRight));
        layer.Add(new SizingAdorner(this, gameSideElement, ResizingDirection.Bottom));
        layer.Add(new SizingAdorner(this, gameSideElement, ResizingDirection.BottomLeft));
        layer.Add(new SizingAdorner(this, gameSideElement, ResizingDirection.Left));

        adornerLayers.Add(gameSideElement.Id, layer);
        canvas.Children.Add(layer.Visual);

        layer.Invalidate();
        return layer;
    }

    private void DoAddAdorner(Guid elementId)
    {
        if (!adornerLayers.TryGetValue(elementId, out var layer))
        {
            var element = (UIElement?)Controller.FindGameSidePart(new AbsoluteId(Controller.Editor.Asset.Id, elementId));
            if (element == null) return;
            layer = CreateLayer(element);
        }

        layer?.Hide();
    }

    private void DoDisableAdorner(Guid elementId)
    {
        if (!adornerLayers.TryGetValue(elementId, out var layer))
            return;

        layer.Hide();
        layer.Disable();
    }

    private void DoEnableAdorner(Guid elementId)
    {
        if (!adornerLayers.TryGetValue(elementId, out var layer))
            return;

        layer.Invalidate();
        layer.Enable();
    }

    private void DoHideAllAdorners()
    {
        adornerLayers.Values.ForEach(l => l.Hide());
        selectedAdorners.Clear();
    }

    private void DoHighlightAdorner(Guid elementId)
    {
        if (!adornerLayers.TryGetValue(elementId, out var layer))
            return;

        if (layer.IsHighlighted)
            return;

        DoUnlitAllAdorners();
        layer.Invalidate();
        layer.Highlight();
    }

    private void DoRemoveAdorner(Guid elementId)
    {
        if (!adornerLayers.TryGetValue(elementId, out var layer))
            return;

        adornerLayers.Remove(elementId);
        var canvas = GetAdornerCanvas();
        canvas?.Children.Remove(layer.Visual);
    }

    private void DoSelectElement(Guid elementId)
    {
        if (!adornerLayers.TryGetValue(elementId, out var layer))
        {
            DoAddAdorner(elementId);
            if (!adornerLayers.TryGetValue(elementId, out layer))
                return;
        }

        selectedAdorners.Add(elementId);
        layer.Invalidate();
        layer.Show();
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void DoUnlitAllAdorners()
    {
        adornerLayers.Values.ForEach(l => l.Unlit());
    }

    private Canvas? GetAdornerCanvas()
    {
        return Controller.GetEntityByName(UIEditorController.AdornerEntityName)?.Get<UIComponent>()?.Page?.RootElement as Canvas;
    }

    private async Task Update()
    {
        Canvas? adornerCanvas;
        while (true)
        {
            if (IsDisposed)
                return;
            await Game.Script.NextFrame();

            adornerCanvas = GetAdornerCanvas();
            if (adornerCanvas == null)
                continue;

            adornerCanvas.PreviewTouchDown += PreviewTouchDown;
            adornerCanvas.PreviewTouchMove += PreviewTouchMove;
            adornerCanvas.TouchMove += TouchMove;
            adornerCanvas.TouchUp += TouchUp;
            break;
        }

        var compositor = Game.SceneSystem.GraphicsCompositor;
        if (compositor?.Game is EditorTopLevelCompositor topLevel)
        {
            topLevel.PostGizmoCompositors.Add(new DelegateSceneRenderer(context =>
            {
                if (adornerCanvas == null) return;
                var canvasRenderSize = adornerCanvas.RenderSize;
                foreach (var layer in adornerLayers.Values)
                {
                    if (!layer.IsEnabled || layer.CheckValidity())
                        continue;

                    layer.Update(ref canvasRenderSize);
                }
            }));
        }
    }
}

