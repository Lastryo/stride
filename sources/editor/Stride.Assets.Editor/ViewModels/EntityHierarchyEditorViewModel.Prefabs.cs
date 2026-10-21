// Copyright (c) .NET Foundation and Contributors (https://dotnetfoundation.org/ & https://stride3d.net)
// Distributed under the MIT license. See the LICENSE.md file in the project root for more information.

using System.ComponentModel;
using Stride.Assets.Entities;
using Stride.Assets.Presentation.ViewModels;
using Stride.Core;
using Stride.Core.Assets;
using Stride.Core.Assets.Analysis;
using Stride.Core.Assets.Quantum;
using Stride.Core.Assets.Yaml;
using Stride.Core.IO;
using Stride.Core.Presentation.Commands;
using Stride.Core.Reflection;
using Stride.Engine;
using Stride.Engine.Design;

namespace Stride.Assets.Editor.ViewModels;

public abstract partial class EntityHierarchyEditorViewModel
{
    public ICommandBase CreatePrefabFromSelectionCommand { get; }

    private readonly HashSet<EntityViewModel> prefabCommandSelection = [];

    public ICommandBase OpenPrefabEditorCommand { get; private set; } = null!;
    public ICommandBase SelectPrefabCommand { get; private set; } = null!;
    public ICommandBase BreakLinkToPrefabCommand { get; private set; } = null!;

    private PrefabViewModel? SourcePrefab(EntityViewModel entity) => entity.EntityDesign.Base is { } basePart
        ? Session.GetAssetById(basePart.BasePartAsset.Id) as PrefabViewModel : null;

    private void InitializePrefabCommands()
    {
        OpenPrefabEditorCommand = new AnonymousCommand(ServiceProvider, () =>
        {
            foreach (var prefab in SelectedItems.OfType<EntityViewModel>().Select(SourcePrefab).OfType<PrefabViewModel>().Distinct().ToArray())
                Session.EditorCollection.OpenAssetEditor(prefab);
        }) { IsEnabled = false };
        SelectPrefabCommand = new AnonymousCommand(ServiceProvider, () =>
        {
            if (SelectedItems.OfType<EntityViewModel>().SingleOrDefault() is { } entity && SourcePrefab(entity) is { } prefab)
                Session.AssetCollection.SelectAssets([prefab]);
        }) { IsEnabled = false };
        BreakLinkToPrefabCommand = new AnonymousCommand(ServiceProvider, () =>
        {
            if (!BreakLinkToPrefabCommand.CanExecute(null)) return;
            using var transaction = Session.ActionService!.CreateTransaction();
            foreach (var group in SelectedItems.OfType<EntityViewModel>().ToArray().GroupBy(entity => (EntityHierarchyViewModel)entity.Asset))
                group.Key.AssetHierarchyPropertyGraph.BreakBasePartLinks(group.Select(entity => entity.EntityDesign));
            Session.ActionService.SetName(transaction, "Break link to prefab");
            UpdatePrefabCommands();
        }) { IsEnabled = false };
    }

    private void PrefabSelectionChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(EntityViewModel.IsPrefabInstance)) UpdatePrefabCommands();
    }

    private void UpdatePrefabCommands()
    {
        foreach (var entity in prefabCommandSelection) entity.PropertyChanged -= PrefabSelectionChanged;
        prefabCommandSelection.Clear();
        prefabCommandSelection.UnionWith(SelectedItems.OfType<EntityViewModel>());
        foreach (var entity in prefabCommandSelection) entity.PropertyChanged += PrefabSelectionChanged;

        var hasPrefab = prefabCommandSelection.Any(entity => SourcePrefab(entity) != null);
        OpenPrefabEditorCommand.IsEnabled = hasPrefab;
        SelectPrefabCommand.IsEnabled = hasPrefab && SelectedItems.Count == 1;
        BreakLinkToPrefabCommand.IsEnabled = hasPrefab;
        CreatePrefabFromSelectionCommand.IsEnabled = Asset.IsEditable && prefabCommandSelection.Count > 0;
        DuplicateCommand.IsEnabled = GetCommonRoots(prefabCommandSelection.ToArray()).Count > 0;
    }

    public PrefabViewModel? CreatePrefabFromSelection()
    {
        var selection = SelectedItems.OfType<EntityViewModel>().ToArray();
        if (selection.Length == 0 || !Asset.IsEditable) return null;

        using var transaction = Session.ActionService!.CreateTransaction();
        var hierarchy = new AssetCompositeHierarchyData<EntityDesign, Entity>();
        var mappings = new Dictionary<EntityHierarchyViewModel, Dictionary<Guid, Guid>>();
        foreach (var group in selection.GroupBy(entity => (EntityHierarchyViewModel)entity.Asset))
        {
            var source = group.Key;
            var sourceAsset = (EntityHierarchyAssetBase)source.Asset;
            var overrides = new Dictionary<Guid, YamlAssetMetadata<OverrideType>>();
            foreach (var part in sourceAsset.Hierarchy.Parts.Values)
            {
                var node = Session.AssetNodeContainer.GetOrCreateNode(part.Entity);
                overrides.Add(part.Entity.Id, AssetPropertyGraph.GenerateOverridesForSerialization(node));
            }

            var roots = GetCommonRoots(group.ToArray());
            var clone = AssetCompositeHierarchyPropertyGraph<EntityDesign, Entity>.CloneSubHierarchies(
                Session.AssetNodeContainer, sourceAsset, roots.Select(entity => entity.Id.ObjectId),
                SubHierarchyCloneFlags.CleanExternalReferences | SubHierarchyCloneFlags.GenerateNewIdsForIdentifiableObjects,
                out var mapping);
            AssetPartsAnalysis.GenerateNewBaseInstanceIds(clone);
            hierarchy.MergeInto(clone);

            // Transfer overrides before the new graph reconciles existing prefab bases.
            foreach (var (sourceId, cloneId) in mapping)
            {
                if (!hierarchy.Parts.TryGetValue(cloneId, out var part)) continue;
                var node = (IAssetNode)Session.AssetNodeContainer.GetOrCreateNode(part.Entity);
                AssetPropertyGraph.ApplyOverrides(node, overrides[sourceId]);
            }
            mappings.Add(source, mapping);
        }

        var name = hierarchy.RootParts.FirstOrDefault()?.Name;
        name = Utilities.BuildValidFileName(string.IsNullOrWhiteSpace(name) ? "Prefab" : name);
        var package = Asset.Directory.Package;
        var used = package.Package.Assets.Select(item => (string)item.Location)
            .Concat(package.DeletedAssets.Select(item => item.Url)).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var location = NamingHelper.ComputeDuplicateName((string)UFile.Combine(Asset.Directory.Path, name), used.Contains);
        var item = new AssetItem(location, new PrefabAsset { Hierarchy = hierarchy });
        var prefab = (PrefabViewModel)package.AddAsset(item, Asset.Directory);

        foreach (var (source, mapping) in mappings)
        {
            var instanceId = Guid.NewGuid();
            var sourceAsset = (EntityHierarchyAssetBase)source.Asset;
            foreach (var (sourceId, cloneId) in mapping)
            {
                if (!sourceAsset.Hierarchy.Parts.TryGetValue(sourceId, out var part)) continue;
                Session.AssetNodeContainer.GetOrCreateNode(part)[nameof(EntityDesign.Base)]
                    .Update(new BasePart(new AssetReference(item.Id, item.Location), cloneId, instanceId));
            }
            source.AssetHierarchyPropertyGraph.RefreshBase();
            foreach (var sourceId in mapping.Keys)
            {
                if (!sourceAsset.Hierarchy.Parts.TryGetValue(sourceId, out var part)) continue;
                ((IAssetObjectNode)Session.AssetNodeContainer.GetOrCreateNode(part.Entity)).ResetOverrideRecursively();
            }
        }

        Session.AssetCollection.SelectAssets([prefab]);
        Session.ActionService.SetName(transaction, $"Create {prefab.Name} from selection");
        return prefab;
    }
}
