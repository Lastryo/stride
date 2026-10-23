// Copyright (c) .NET Foundation and Contributors (https://dotnetfoundation.org/ & https://stride3d.net)
// Distributed under the MIT license. See the LICENSE.md file in the project root for more information.

using System.Collections;
using System.Reflection;
using Stride.Core.Assets.Editor.ViewModels;
using Stride.Core.Assets.Presentation.ViewModels;
using Stride.Core.Assets.Quantum;
using Stride.Core.Assets.Quantum.Visitors;
using Stride.Core.Assets.Serializers;
using Stride.Core.Assets.Visitors;
using Stride.Core.Assets.Yaml;
using Stride.Core.Diagnostics;
using Stride.Core.Extensions;
using Stride.Core.Presentation.Dirtiables;
using Stride.Core.Quantum;
using Stride.Core.Reflection;
using Stride.Core.Serialization;
using Stride.Core.Yaml;
using Stride.Core.Yaml.Events;
using Stride.Core.Yaml.Serialization;

namespace Stride.Core.Assets.Editor.Services;

/// <summary>One reversible reload of game assemblies and the asset objects using their types.</summary>
public sealed class AssemblyReloadOperation : DirtyingOperation
{
    private sealed record AssemblyVersion(PackageLoadedAssembly Slot, string OldPath, string NewPath, Assembly Old, Assembly New);
    private readonly SessionViewModel session;
    private readonly ILogger log;
    private readonly List<AssemblyVersion> versions = [];
    private readonly Dictionary<AssetViewModel, List<ItemToReload>> parts = [];
    private readonly Action<IReadOnlyCollection<AssetViewModel>> changed;
    private readonly Dictionary<AssetViewModel, YamlAssetMetadata<OverrideType>> originalOverrides = [];

    public IReadOnlyCollection<AssetViewModel> AffectedAssets => parts.Keys;

    public AssemblyReloadOperation(SessionViewModel session, IReadOnlyDictionary<PackageLoadedAssembly, string> paths,
        string stagingDirectory, ILogger log, Action<IReadOnlyCollection<AssetViewModel>> changed)
        : base([])
    {
        this.session = session;
        this.log = log;
        this.changed = changed;
        var visitor = new UnloadingVisitor(log, paths.Keys.Select(p => p.Assembly!).ToHashSet());
        // Snapshot before loading modules: their initializers can update the YAML type registry.
        foreach (var asset in session.LocalPackages.SelectMany(p => p.Assets))
        {
            if (asset.PropertyGraph == null) continue;
            AssetCollectionItemIdHelper.GenerateMissingItemIds(asset.Asset);
            var items = visitor.Run(asset.PropertyGraph);
            if (items.Count == 0) continue;
            foreach (var item in items)
            {
                item.GraphPath = GraphNodePath.From(asset.PropertyGraph.RootNode, item.Path, out item.GraphPathIndex);
                item.OldObject = item.GraphPath.GetNode().Retrieve(item.GraphPathIndex)!;
                item.Present = true;
                var node = item.GraphPath.GetNode();
                if (!item.GraphPathIndex.IsEmpty && CollectionItemIdHelper.TryGetCollectionItemIds(node.Retrieve(), out var ids))
                    item.ItemId = ids[item.GraphPathIndex.Value!];
            }
            parts.Add(asset, items);
            originalOverrides.Add(asset, AssetPropertyGraph.GenerateOverridesForSerialization(asset.PropertyGraph.RootNode));
        }
        Directory.CreateDirectory(stagingDirectory);
        var container = new AssemblyContainer();
        try
        {
            // Module initializers register aliases, which must not collide with the previous generation.
            foreach (var entry in paths.Keys)
            {
                AssemblyRegistry.Unregister(entry.Assembly);
                DataSerializerFactory.UnregisterSerializationAssembly(entry.Assembly);
            }
            // Each generation gets its own resolver and files, retaining lazy dependencies for Undo/Redo.
            foreach (var entry in paths)
            {
                var source = System.IO.Path.GetFullPath(entry.Value);
                var destination = System.IO.Path.Combine(stagingDirectory, System.IO.Path.GetFileName(source));
                File.Copy(source, destination, true);
                foreach (var extension in new[] { ".pdb", ".deps.json" })
                {
                    var sidecar = System.IO.Path.ChangeExtension(source, extension);
                    if (File.Exists(sidecar)) File.Copy(sidecar, System.IO.Path.ChangeExtension(destination, extension), true);
                }
                foreach (var dependency in Directory.EnumerateFiles(System.IO.Path.GetDirectoryName(source)!, "*.dll"))
                    container.RegisterDependency(dependency);
            }
            foreach (var entry in paths)
            {
                var path = System.IO.Path.Combine(stagingDirectory, System.IO.Path.GetFileName(entry.Value));
                var assembly = container.LoadAssemblyFromPath(path, log) ?? throw new InvalidOperationException($"Unable to load {path}.");
                if (assembly.GetName().Name != entry.Key.Assembly.GetName().Name)
                    throw new InvalidOperationException($"Assembly identity changed: {entry.Value}.");
                _ = assembly.GetTypes();
                versions.Add(new(entry.Key, entry.Key.Path, entry.Value, entry.Key.Assembly, assembly));
            }
        }
        finally
        {
            // Staging must not leave a half-loaded generation active if validation fails.
            foreach (var loaded in container.LoadedAssemblies)
            {
                AssemblyRegistry.Unregister(loaded.Assembly);
                DataSerializerFactory.UnregisterSerializationAssembly(loaded.Assembly);
            }
            foreach (var entry in paths.Keys.Where(p => p.Assembly != null)) Register(entry.Assembly);
        }
    }

    public void Execute()
    {
        using var fixup = session.CreateAssetFixupContext();
        try
        {
            Clear();
            SwitchAssemblies(true);
            foreach (var asset in parts)
            {
                foreach (var item in asset.Value)
                {
                    var reader = new EventReader(new MemoryParser(item.ParsingEvents));
                    item.UpdatedObject = AssetYamlSerializer.Default.Deserialize(reader, null!, item.ExpectedType,
                        out var properties, new SerializerContextSettings { Logger = log });
                    var metadata = YamlAssetSerializer.CreateAndProcessMetadata(properties, item.UpdatedObject, false);
                    item.Overrides = metadata.RetrieveMetadata(AssetObjectSerializerBackend.OverrideDictionaryKey);
                    item.References = metadata.RetrieveMetadata(AssetObjectSerializerBackend.ObjectReferencesKey);
                }
            }
            Restore(true);
        }
        catch
        {
            Clear();
            SwitchAssemblies(false);
            Restore(false);
            throw;
        }
    }

    private static void Register(Assembly assembly)
    {
        AssemblyRegistry.Register(assembly, AssemblyCommonCategories.Engine, AssemblyCommonCategories.Assets);
        DataSerializerFactory.RegisterSerializationAssembly(assembly);
    }
    private void SwitchAssemblies(bool updated)
    {
        foreach (var version in versions.AsEnumerable().Reverse())
        {
            AssemblyRegistry.Unregister(version.Slot.Assembly);
            DataSerializerFactory.UnregisterSerializationAssembly(version.Slot.Assembly);
        }
        foreach (var version in versions)
        {
            version.Slot.Path = updated ? version.NewPath : version.OldPath;
            version.Slot.Assembly = updated ? version.New : version.Old;
            Register(version.Slot.Assembly);
        }
    }

    private void Clear()
    {
        foreach (var asset in parts)
            foreach (var item in asset.Value.AsEnumerable().Reverse().Where(i => i.Present))
            {
                var node = item.GraphPath.GetNode();
                if (!item.GraphPathIndex.IsEmpty) ((IObjectNode)node).Remove(node.Retrieve(item.GraphPathIndex), item.GraphPathIndex);
                else if (node is IMemberNode member) member.Update(null);
                else
                {
                    var placeholder = (Asset)UnloadableObjectInstantiator.CreateUnloadableObject(typeof(Asset), item.ExpectedType.Name,
                        "Reloading", "Reloading assembly", item.ParsingEvents);
                    placeholder.Id = asset.Key.Id;
                    asset.Key.UpdateAsset(placeholder, log);
                }
                item.Present = false;
            }
    }

    private void Restore(bool updated)
    {
        foreach (var asset in parts)
        {
            var references = new YamlAssetMetadata<Guid>();
            var overrides = new YamlAssetMetadata<OverrideType>();
            foreach (var item in asset.Value)
            {
                var value = updated ? item.UpdatedObject : item.OldObject;
                var node = item.GraphPath.GetNode();
                if (!item.GraphPathIndex.IsEmpty) ((IAssetObjectNode)node).Restore(value, item.GraphPathIndex, item.ItemId);
                else if (node is IMemberNode member) member.Update(value);
                else asset.Key.UpdateAsset((Asset)value, log);
                item.Present = true;
                if (!updated) continue;
                var basePath = YamlAssetPath.FromMemberPath(item.GraphPathIndex.IsEmpty ? item.Path : item.ParentPath, asset.Key.Asset);
                if (!item.GraphPathIndex.IsEmpty)
                {
                    if (item.ItemId == ItemId.Empty) basePath.PushIndex(item.GraphPathIndex.Value!);
                    else basePath.PushItemId(item.ItemId);
                }
                if (item.References != null)
                    foreach (var reference in item.References) references.Set(basePath.Append(reference.Key), reference.Value);
                if (item.Overrides != null)
                {
                    var path = item.GraphPath.Clone();
                    if (!item.GraphPathIndex.IsEmpty) path.PushIndex(item.GraphPathIndex);
                    var prefix = AssetNodeMetadataCollectorBase.ConvertPath(path);
                    foreach (var entry in item.Overrides) overrides.Set(prefix.Append(entry.Key), entry.Value);
                }
            }
            if (updated)
            {
                FixupObjectReferences.RunFixupPass(asset.Key.Asset, references, true, false, log);
            }
            AssetPropertyGraph.ApplyOverrides(asset.Key.PropertyGraph!.RootNode, updated ? overrides : originalOverrides[asset.Key]);
        }
    }

    protected override void Undo()
    {
        using (session.CreateAssetFixupContext()) { Clear(); SwitchAssemblies(false); Restore(false); }
        changed(AffectedAssets);
    }
    protected override void Redo()
    {
        using (session.CreateAssetFixupContext()) { Clear(); SwitchAssemblies(true); Restore(true); }
        changed(AffectedAssets);
    }
    protected override void FreezeContent() { parts.Clear(); versions.Clear(); originalOverrides.Clear(); }

    private abstract class ReloaderVisitorBase : AssetVisitorBase
    {
        protected readonly HashSet<Assembly> UnloadedAssemblies;
        protected readonly ILogger Log;
        protected List<ItemToReload> ItemsToReload = [];

        protected ReloaderVisitorBase(ILogger log, HashSet<Assembly> unloadedAssemblies)
        {
            Log = log;
            UnloadedAssemblies = unloadedAssemblies;
        }

        public override void VisitObject(object obj, ObjectDescriptor descriptor, bool visitMembers)
        {
            // For asset roots (not reference), we want the base type rather than the real one (which will be unloaded)
            var expectedType = descriptor.Type;
            if (obj is Asset)
                expectedType = typeof(Asset);

            if (ProcessObject(obj, expectedType)) return;

            base.VisitObject(obj, descriptor, visitMembers);
        }

        public override void VisitCollectionItem(IEnumerable collection, CollectionDescriptor descriptor, int index, object? item, ITypeDescriptor? itemDescriptor)
        {
            if (ProcessObject(item, descriptor.ElementType)) return;

            base.VisitCollectionItem(collection, descriptor, index, item, itemDescriptor);
        }

        public override void VisitArrayItem(Array array, ArrayDescriptor descriptor, int index, object? item, ITypeDescriptor? itemDescriptor)
        {
            if (ProcessObject(item, descriptor.ElementType)) return;

            base.VisitArrayItem(array, descriptor, index, item, itemDescriptor);
        }

        public override void VisitDictionaryKeyValue(object dictionary, DictionaryDescriptor descriptor, object key, ITypeDescriptor? keyDescriptor, object? value, ITypeDescriptor? valueDescriptor)
        {
            // TODO: CurrentPath is valid only for value, not key
            //if (ProcessObject(key, keyDescriptor.Type)) key = null;
            if (ProcessObject(value, descriptor.ValueType)) return;

            Visit(value, valueDescriptor);
            //base.VisitDictionaryKeyValue(dictionary, descriptor, key, keyDescriptor, value, valueDescriptor);
        }

        public override void VisitSetItem(IEnumerable set, SetDescriptor descriptor, object? item, ITypeDescriptor? itemDescriptor)
        {
            if (ProcessObject(item, descriptor.ElementType)) return;

            base.VisitSetItem(set, descriptor, item, itemDescriptor);
        }

        protected abstract bool ProcessObject(object? obj, Type expectedType);
    }

    private class UnloadingVisitor : ReloaderVisitorBase
    {
        private AssetPropertyGraph propertyGraph = null!;

        public UnloadingVisitor(ILogger log, HashSet<Assembly> unloadedAssemblies)
            : base(log, unloadedAssemblies)
        {
        }


        public List<ItemToReload> Run(AssetPropertyGraph assetPropertyGraph)
        {
            if (assetPropertyGraph == null) throw new ArgumentNullException(nameof(assetPropertyGraph));
            Reset();
            propertyGraph = assetPropertyGraph;
            var result = ItemsToReload = new List<ItemToReload>();
            Visit(assetPropertyGraph.RootNode.Retrieve());
            ItemsToReload = [];
            return result;
        }

        protected override bool ProcessObject(object? obj, Type expectedType)
        {
            // TODO: More advanced checks if IUnloadable is supposed to be a type from the unloaded assembly (this would avoid processing unecessary IUnloadable)
            if (obj != null && (UnloadedAssemblies.Contains(obj.GetType().Assembly) || obj is IUnloadable))
            {
                NodeIndex index;
                var settings = new SerializerContextSettings(Log);
                var path = GraphNodePath.From(propertyGraph.RootNode, CurrentPath, out index);

                var overrides = AssetPropertyGraph.GenerateOverridesForSerialization(path.GetNode());
                overrides = RemoveFirstIndexInYamlPath(overrides, index);
                if (overrides != null)
                    settings.Properties.Add(AssetObjectSerializerBackend.OverrideDictionaryKey, overrides);

                var objectReferences = propertyGraph.GenerateObjectReferencesForSerialization(path.GetNode());
                objectReferences = RemoveFirstIndexInYamlPath(objectReferences, index);
                if (objectReferences != null)
                    settings.Properties.Add(AssetObjectSerializerBackend.ObjectReferencesKey, objectReferences);

                var parsingEvents = new List<ParsingEvent>();
                AssetYamlSerializer.Default.Serialize(new ParsingEventListEmitter(parsingEvents), obj, expectedType, settings);

                // Build parent path
                var parentPath = CurrentPath.Clone();
                parentPath.Pop();

                ItemsToReload.Add(new ItemToReload(parentPath, CurrentPath.Clone(), parsingEvents, expectedType));

                // Don't recurse inside
                return true;
            }
            return false;
        }

        private static YamlAssetMetadata<T>? RemoveFirstIndexInYamlPath<T>(YamlAssetMetadata<T>? metadata, NodeIndex index)
        {
            if (metadata != null)
            {
                // If we had an index we need to remove it from our override paths
                if (index != NodeIndex.Empty)
                {
                    var fixedMetadata = new YamlAssetMetadata<T>();
                    foreach (var entry in metadata)
                    {
                        // We're filling a new dictionary because we are destroying hash codes by mutating the keys
                        var newPath = entry.Key.Elements.Count > 0 ? new YamlAssetPath(entry.Key.Elements.Skip(1)) : entry.Key;
                        fixedMetadata.Set(newPath, entry.Value);
                    }
                    metadata = fixedMetadata;
                }
            }
            return metadata;
        }
    }


    private sealed class ItemToReload
    {
        public readonly MemberPath ParentPath;
        public readonly MemberPath Path;
        public readonly List<ParsingEvent> ParsingEvents;
        public readonly Type ExpectedType;
        public GraphNodePath GraphPath = null!;
        public NodeIndex GraphPathIndex;
        public ItemId ItemId;
        public object OldObject = null!;
        public object UpdatedObject = null!;
        public bool Present;
        public YamlAssetMetadata<OverrideType>? Overrides;
        public YamlAssetMetadata<Guid>? References;
        public ItemToReload(MemberPath parent, MemberPath path, List<ParsingEvent> events, Type expected)
        { ParentPath = parent; Path = path; ParsingEvents = events; ExpectedType = expected; }
    }
}
