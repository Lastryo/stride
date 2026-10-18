// Copyright (c) .NET Foundation and Contributors (https://dotnetfoundation.org/ & https://stride3d.net)
// Distributed under the MIT license. See the LICENSE.md file in the project root for more information.

using Stride.Core.Mathematics;
using Stride.Games;
using Stride.Editor.EditorGame.Game;

namespace Stride.Assets.Editor.GameEditor.Services;

/// <summary>The UI adapter for an editor game. The renderer is owned by the game thread.</summary>
public interface IEditorGameView : IDisposable
{
    GameContext Context { get; }

    void AttachGame(EditorServiceGame game);

    /// <summary>Creates the UI host on the asset dispatcher thread.</summary>
    object CreateHost();

    /// <summary>Returns normalized viewport coordinates on the asset dispatcher thread.</summary>
    Vector2 GetMousePosition(bool lastRightClick);

    /// <summary>Detaches the host on the asset dispatcher thread before stopping the renderer.</summary>
    void DisposeHost();
}

public interface IEditorGameViewFactory
{
    /// <summary>Creates the view and its windowless context.</summary>
    IEditorGameView CreateView();
}
