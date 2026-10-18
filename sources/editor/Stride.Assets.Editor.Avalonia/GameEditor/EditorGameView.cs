// Copyright (c) .NET Foundation and Contributors (https://dotnetfoundation.org/ & https://stride3d.net)
// Distributed under the MIT license. See the LICENSE.md file in the project root for more information.

using global::Avalonia.Threading;
using Stride.Assets.Editor.GameEditor.Services;
using Stride.Core.Mathematics;
using Stride.Editor.Avalonia.Engine;
using Stride.Editor.EditorGame.Game;
using Stride.Games;

namespace Stride.Assets.Editor.Avalonia.GameEditor;

public sealed class EditorGameViewFactory : IEditorGameViewFactory
{
    public IEditorGameView CreateView() => Dispatcher.UIThread.Invoke(() => (IEditorGameView)new EditorGameView());
}

internal sealed class EditorGameView : IEditorGameView
{
    private readonly AvaloniaGameView view = new();
    public GameContext Context => view.Context;
    public void AttachGame(EditorServiceGame game) => view.AttachGame(game);
    public object CreateHost() => view;
    public Vector2 GetMousePosition(bool lastRightClick) => view.GetMousePosition(lastRightClick);
    public void DisposeHost() => view.Dispose();
    public void Dispose() => view.Dispose();
}
