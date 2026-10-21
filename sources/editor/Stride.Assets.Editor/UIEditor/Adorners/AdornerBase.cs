// Copyright (c) .NET Foundation and Contributors (https://dotnetfoundation.org/ & https://stride3d.net) and Silicon Studio Corp. (https://www.siliconstudio.co.jp)
// Distributed under the MIT license. See the LICENSE.md file in the project root for more information.

using System.Runtime.CompilerServices;
using Stride.Assets.Editor.UIEditor.Game;
using Stride.Core.Mathematics;
using Stride.UI;

namespace Stride.Assets.Editor.UIEditor.Adorners;

/// <summary>
/// Base class for adorners used by the <see cref="UIEditorGameAdornerService"/>.
/// </summary>
internal abstract class AdornerBase<TVisual> : IAdornerBase<TVisual>
    where TVisual : UIElement
{
    protected AdornerBase(UIEditorGameAdornerService service, UIElement gameSideElement)
    {
        Service = service;
        GameSideElement = gameSideElement;
    }

    public UIElement GameSideElement { get; }

    public bool IsVisible => Visual.Visibility == Visibility.Visible;

    public abstract TVisual Visual { get; }

    protected UIEditorGameAdornerService Service { get; }

    public virtual void Disable()
    {
        Visual.CanBeHitByUser = false;
    }

    public virtual void Enable()
    {
        Visual.CanBeHitByUser = true;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Hide()
    {
        Visual.Visibility = Visibility.Hidden;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Show()
    {
        Visual.Visibility = Visibility.Visible;
    }

    public abstract void Update(Vector3 position);

    protected void InitializeAttachedProperties()
    {
        Visual.DependencyProperties.Set(UIEditorGameAdornerService.AssociatedAdornerPropertyKey, this);
        Visual.DependencyProperties.Set(UIEditorGameAdornerService.AssociatedElementIdPropertyKey, GameSideElement.Id);
    }
}

