// Copyright (c) .NET Foundation and Contributors (https://dotnetfoundation.org/ & https://stride3d.net)
// Distributed under the MIT license. See the LICENSE.md file in the project root for more information.

namespace Stride.Assets.Editor.UIEditor.ViewModels;

/// <summary>A library or category heading in the UI element palette.</summary>
/// <param name="Name">The displayed heading.</param>
/// <param name="Children">Its categories or element factories.</param>
public sealed record UIElementFactoryGroup(string Name, IReadOnlyList<object> Children);
