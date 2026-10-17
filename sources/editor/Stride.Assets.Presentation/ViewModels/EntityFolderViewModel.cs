// Copyright (c) .NET Foundation and Contributors (https://dotnetfoundation.org/ & https://stride3d.net) and Silicon Studio Corp. (https://www.siliconstudio.co.jp)
// Distributed under the MIT license. See the LICENSE.md file in the project root for more information.

using Stride.Assets.Entities;
using Stride.Core;
using Stride.Core.Quantum;

namespace Stride.Assets.Presentation.ViewModels;

/// <summary>
/// Represents a virtual folder in the entity hierarchy. The path is stored on each entity design,
/// matching the WPF editor's folder representation.
/// </summary>
public sealed class EntityFolderViewModel : EntityHierarchyItemViewModel
{
    public const char FolderSeparator = '/';

    public EntityFolderViewModel(EntityHierarchyViewModel asset, string name, string path, IEnumerable<EntityDesign> entities, IEnumerable<string>? retainedFolders = null)
        : base(asset, entities, path, retainedFolders ?? Array.Empty<string>())
    {
        Name = name;
        FolderPath = path;
        Id = new AbsoluteId(asset.Id, Guid.NewGuid());
    }

    public override AbsoluteId Id { get; }

    public override string? Name { get; set; }

    internal override string FolderPath { get; }

    public string Path => FolderPath;

    public override IEnumerable<EntityViewModel> InnerSubEntities => Children.SelectMany(x => x.InnerSubEntities);

    public static string CombinePath(string? parentPath, string name)
    {
        return string.IsNullOrEmpty(parentPath) ? name : parentPath + FolderSeparator + name;
    }

    public override GraphNodePath GetNodePath()
    {
        return Parent?.GetNodePath() ?? throw new InvalidOperationException("A folder must be attached to a hierarchy item before resolving its node path.");
    }
}
