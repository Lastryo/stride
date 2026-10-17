// Copyright (c) .NET Foundation and Contributors (https://dotnetfoundation.org/ & https://stride3d.net) and Silicon Studio Corp. (https://www.siliconstudio.co.jp)
// Distributed under the MIT license. See the LICENSE.md file in the project root for more information.

using Stride.Assets.Entities;
using Stride.Core;
using Stride.Core.Assets;
using Stride.Core.Assets.Presentation.ViewModels;

namespace Stride.Assets.Presentation.ViewModels;

public abstract class EntityHierarchyItemViewModel : AssetCompositeItemViewModel<EntityHierarchyViewModel, EntityHierarchyItemViewModel>, IAssetPartViewModel
{
    private bool isVisible = true;
    protected EntityHierarchyItemViewModel(EntityHierarchyViewModel asset, IEnumerable<EntityDesign> childEntities)
        : this(asset, childEntities, string.Empty)
    {
    }

    protected EntityHierarchyItemViewModel(EntityHierarchyViewModel asset, IEnumerable<EntityDesign> childEntities, string folderPath)
        : this(asset, childEntities, folderPath, Array.Empty<string>())
    {
    }

    protected EntityHierarchyItemViewModel(EntityHierarchyViewModel asset, IEnumerable<EntityDesign> childEntities, string folderPath, IEnumerable<string> retainedFolders)
        : base(asset)
    {
        BuildChildren(childEntities, folderPath, retainedFolders.ToArray());
    }

    /// <inheritdoc />
    public abstract AbsoluteId Id { get; }

    public bool IsVisible { get => isVisible; set => SetValue(ref isVisible, value); }

    /// <summary>
    /// An enumeration of the items represented by this item.
    /// </summary>
    /// <remarks>
    /// In case of an <see cref="EntityViewModel"/> it is equivalent to <c>this</c>.
    /// </remarks>
    // FIXME: find a better name
    public abstract IEnumerable<EntityViewModel> InnerSubEntities { get; }

    internal virtual string FolderPath => string.Empty;

    protected EntityHierarchyAssetBase EntityHierarchy => (EntityHierarchyAssetBase)Asset.Asset;

    public void RefreshChildren()
    {
        var folders = EnumerateFolderPaths(Children).ToArray();
        var owner = this as EntityViewModel ?? FindOwnerEntity();
        var designs = owner is null
            ? EntityHierarchy.Hierarchy.EnumerateRootPartDesigns().ToArray()
            : owner.AssetSideEntity.Transform.Children.Select(child =>
                EntityHierarchy.Hierarchy.Parts.TryGetValue(child.Entity.Id, out var design) ? design : new EntityDesign(child.Entity)).ToArray();
        BuildChildren(designs, FolderPath, folders);
    }

    public EntityFolderViewModel CreateFolder(string name)
    {
        var path = EntityFolderViewModel.CombinePath(FolderPath, name);
        var folder = new EntityFolderViewModel((EntityHierarchyViewModel)Asset, name, path, Array.Empty<EntityDesign>());
        AddFolder(folder);
        return folder;
    }

    public void AddFolder(EntityFolderViewModel folder) => AddItem(folder);

    public void RemoveFolder(EntityFolderViewModel folder) => RemoveItem(folder);

    private void BuildChildren(IEnumerable<EntityDesign> source, string path, IReadOnlyCollection<string> retainedFolders)
    {
        var designs = source.ToArray();
        foreach (var child in Children.ToList())
        {
            RemoveItem(child);
            child.Destroy();
        }

        var normalizedPath = path.Trim('/');
        var folders = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var directEntities = new List<EntityDesign>();
        var prefix = string.IsNullOrEmpty(normalizedPath) ? string.Empty : normalizedPath + "/";

        foreach (var design in designs)
        {
            var designPath = design.Folder?.Trim('/') ?? string.Empty;
            if (string.Equals(designPath, normalizedPath, StringComparison.OrdinalIgnoreCase))
            {
                directEntities.Add(design);
                continue;
            }

            if (!designPath.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                continue;

            var remainder = designPath[prefix.Length..];
            var separator = remainder.IndexOf('/');
            if (separator >= 0)
                folders.Add(remainder[..separator]);
            else if (remainder.Length > 0)
                folders.Add(remainder);
        }

        foreach (var retainedPath in retainedFolders)
        {
            if (retainedPath.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                var remainder = retainedPath[prefix.Length..];
                var separator = remainder.IndexOf('/');
                if (remainder.Length > 0)
                    folders.Add(separator >= 0 ? remainder[..separator] : remainder);
            }
        }

        var viewModel = (EntityHierarchyViewModel)Asset;
        foreach (var folderName in folders.OrderBy(x => x, StringComparer.InvariantCultureIgnoreCase))
        {
            var folderPath = EntityFolderViewModel.CombinePath(normalizedPath, folderName);
            var folderDesigns = designs.Where(x =>
            {
                var designPath = x.Folder?.Trim('/') ?? string.Empty;
                return designPath.StartsWith(folderPath + "/", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(designPath, folderPath, StringComparison.OrdinalIgnoreCase);
            });
            var retainedChildren = retainedFolders.Where(x => x.StartsWith(folderPath + "/", StringComparison.OrdinalIgnoreCase));
            var folder = new EntityFolderViewModel(viewModel, folderName, folderPath, folderDesigns, retainedChildren);
            AddItem(folder);
        }

        AddItems(directEntities.Select(viewModel.CreatePartViewModel));
    }

    private EntityViewModel? FindOwnerEntity()
    {
        var current = Parent;
        while (current is not null)
        {
            if (current is EntityViewModel entity)
                return entity;
            current = current.Parent;
        }

        return null;
    }

    private static IEnumerable<string> EnumerateFolderPaths(IEnumerable<EntityHierarchyItemViewModel> items)
    {
        foreach (var folder in items.OfType<EntityFolderViewModel>())
        {
            yield return folder.FolderPath;
            foreach (var nested in EnumerateFolderPaths(folder.Children))
                yield return nested;
        }
    }
}
