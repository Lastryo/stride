// Copyright (c) .NET Foundation and Contributors (https://dotnetfoundation.org/ & https://stride3d.net) and Silicon Studio Corp. (https://www.siliconstudio.co.jp)
// Distributed under the MIT license. See the LICENSE.md file in the project root for more information.

using Stride.Core.Mathematics;
using Stride.UI;

namespace Stride.Assets.Editor.UIEditor.Adorners;

internal interface IAdornerBase<out TVisual>
    where TVisual : UIElement
{
    TVisual Visual { get; }

    void Disable();

    void Enable();

    void Hide();

    void Show();

    void Update(Vector3 position);
}

