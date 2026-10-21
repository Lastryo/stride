// Copyright (c) .NET Foundation and Contributors (https://dotnetfoundation.org/ & https://stride3d.net) and Silicon Studio Corp. (https://www.siliconstudio.co.jp)
// Distributed under the MIT license. See the LICENSE.md file in the project root for more information.

using System;

namespace Stride.Assets.Presentation.ViewModels;

[Flags]
public enum ResizingDirection
{
    Center = 1,
    Left = 2,
    Top = 4,
    Right = 8,
    Bottom = 16,
    TopLeft = Left | Top,
    TopRight = Right | Top,
    BottomLeft = Left | Bottom,
    BottomRight = Right | Bottom
}

public interface IResizingTarget
{
    void OnResizingCompleted(ResizingDirection direction, double horizontalChange, double verticalChange);
    void OnResizingDelta(ResizingDirection direction, double horizontalChange, double verticalChange);
    void OnResizingStarted(ResizingDirection direction);
}

