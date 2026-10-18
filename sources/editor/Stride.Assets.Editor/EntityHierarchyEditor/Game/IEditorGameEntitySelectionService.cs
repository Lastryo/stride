// Copyright (c) .NET Foundation and Contributors (https://dotnetfoundation.org/ & https://stride3d.net)
// Distributed under the MIT license. See the LICENSE.md file in the project root for more information.

using Stride.Core;
using Stride.Editor.EditorGame.Game;

namespace Stride.Assets.Editor.EntityHierarchyEditor.Game;

public interface IEditorGameEntitySelectionService : IEditorGameService
{
    bool DisplaySelectionMask { get; }
    event EventHandler<EntitySelectionEventArgs>? SelectionUpdated;
    IReadOnlyCollection<AbsoluteId> GetSelectedRootIds();
}
