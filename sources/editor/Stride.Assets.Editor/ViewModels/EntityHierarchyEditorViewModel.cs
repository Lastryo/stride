// Copyright (c) .NET Foundation and Contributors (https://dotnetfoundation.org/ & https://stride3d.net) and Silicon Studio Corp. (https://www.siliconstudio.co.jp)
// Distributed under the MIT license. See the LICENSE.md file in the project root for more information.

using System.Collections.Specialized;
using Stride.Assets.Editor.EntityHierarchyEditor.ViewModels;
using Stride.Assets.Entities;
using Stride.Assets.Presentation.ViewModels;
using Stride.Core.Assets;
using Stride.Core.Assets.Analysis;
using Stride.Core.Extensions;
using Stride.Core.Assets.Editor.ViewModels;
using Stride.Core.Assets.Presentation.ViewModels;
using Stride.Core.Assets.Presentation.Quantum;
using Stride.Core.Assets.Quantum;
using Stride.Core.Mathematics;
using Stride.Core.Quantum;
using Stride.Core;
using Stride.Core.Presentation.Commands;
using Stride.Core.Presentation.Dirtiables;
using Stride.Core.Presentation.Services;
using Stride.Animations;
using Stride.Engine;
using Stride.Engine.Processors;
using Stride.Navigation;
using Stride.Particles;
using Stride.Particles.Components;
using Stride.Particles.Initializers;
using Stride.Particles.Modules;
using Stride.Particles.ShapeBuilders;
using Stride.Particles.Spawners;
using Stride.Particles.Updaters;
using Stride.Rendering;
using Stride.Rendering.Lights;
using Stride.Rendering.Voxels.VoxelGI;
using Stride.Assets.Editor.SceneEditor;
using Stride.Assets.Media;
using Stride.Assets.Models;
using Stride.Assets.Textures;
using Stride.Assets.UI;
using Stride.Audio;
using Stride.Core.Assets.Editor.Services;
using Stride.Core.Presentation.Windows;
using Stride.Media;
using Stride.Graphics;
using Stride.Rendering.Sprites;
using Stride.Video;

namespace Stride.Assets.Editor.ViewModels;

public enum EntityFactoryKind
{
    EmptyEntity,
    AudioEmitter,
    AudioListener,
    PerspectiveCamera,
    OrthographicCamera,
    DirectionalLight,
    PointLight,
    SpotLight,
    AmbientLight,
    VoxelLight,
    LightProbe,
    VoxelVolume,
    NavigationBoundingBox,
    EmptyParticleSystem,
    SimpleParticleSystem,
    FountainParticleSystem,
    RibbonParticleSystem,
    Sprite,
    Ui,
    Background,
    ModelInstance,
}

public enum DropPosition
{
    Into,
    Before,
    After,
}

public abstract partial class EntityHierarchyEditorViewModel : AssetCompositeHierarchyEditorViewModel<EntityDesign, Entity, EntityHierarchyViewModel, EntityHierarchyItemViewModel>
{
    private readonly Dictionary<Guid, EntityHierarchyElementChangePropagator> propagators = [];
    private SceneViewportViewModel? viewport;
    private int propertiesRefreshVersion;
    public SceneViewportViewModel? Viewport { get => viewport; private set => SetValue(ref viewport, value); }

    public async Task OpenViewport()
    {
        Viewport ??= new SceneViewportViewModel(this);
        await Viewport.Initialize();
        Viewport.ShowGame();
    }

    private string filterPattern = string.Empty;
    protected EntityHierarchyEditorViewModel(EntityHierarchyViewModel asset)
        : base(asset)
    {
        CreateEntityInRootCommand = new AnonymousTaskCommand<EntityFactoryKind>(ServiceProvider, kind => CreateEntity(false, kind));
        CreateEntityInSelectionCommand = new AnonymousTaskCommand<EntityFactoryKind>(ServiceProvider, kind => CreateEntity(true, kind));
        CreateFolderInRootCommand = new AnonymousCommand(ServiceProvider, () => CreateFolder(false));
        CreateFolderInSelectionCommand = new AnonymousCommand(ServiceProvider, () => CreateFolder(true));
        DeleteCommand = new AnonymousTaskCommand(ServiceProvider, DeleteSelection, CanDelete);
        CreatePrefabFromSelectionCommand = new AnonymousCommand(ServiceProvider, () => CreatePrefabFromSelection()) { IsEnabled = false };
        DuplicateCommand = new AnonymousCommand(ServiceProvider, () => DuplicateSelection()) { IsEnabled = false };
        InitializePrefabCommands();
        Asset.AssetHierarchyPropertyGraph.PartAdded += OnPartAdded;
        Asset.AssetHierarchyPropertyGraph.PartRemoved += OnPartRemoved;
    }

    public void RegisterAllPropagators()
    {
        if (Viewport?.Controller == null)
            return;

        foreach (var propagator in propagators.Values)
        {
            propagator.Destroy();
        }
        propagators.Clear();

        var allEntities = HierarchyRoot.Children.BreadthFirst(x => x.Children).OfType<EntityViewModel>().ToList();
        foreach (var entity in allEntities)
        {
            RegisterPropagator(entity);
            if (propagators.TryGetValue(entity.AssetSideEntity.Id, out var prop))
            {
                prop.NotifyGameSidePartAdded();
            }
        }
    }

    private void RegisterPropagator(EntityViewModel viewModel)
    {
        if (Viewport?.Controller == null)
            return;

        var partId = viewModel.AssetSideEntity.Id;
        if (propagators.ContainsKey(partId))
            return;

        var propagator = new EntityHierarchyElementChangePropagator(
            Viewport.Controller,
            Session.AssetNodeContainer,
            Dispatcher,
            viewModel,
            viewModel.AssetSideEntity);

        propagators[partId] = propagator;
    }

    private void UnregisterPropagator(Guid partId)
    {
        if (propagators.Remove(partId, out var propagator))
        {
            propagator.Destroy();
        }
    }

    public EntityHierarchyRootViewModel HierarchyRoot => (EntityHierarchyRootViewModel)RootPart;

    public string FilterPattern
    {
        get => filterPattern;
        set
        {
            value ??= string.Empty;
            if (filterPattern == value)
                return;
            SetValue(ref filterPattern, value);
            UpdateVisibilities();
        }
    }

    public ICommandBase CreateEntityInRootCommand { get; }

    public ICommandBase CreateEntityInSelectionCommand { get; }

    public ICommandBase CreateFolderInRootCommand { get; }

    public ICommandBase CreateFolderInSelectionCommand { get; }

    public ICommandBase DeleteCommand { get; }
    public ICommandBase DuplicateCommand { get; }

    public event EventHandler? SelectionFocusRequested;

    private bool materialSelectionMode;
    private bool displaySelectionMask = true;
    private bool compilingAssets;

    public bool MaterialSelectionMode { get => materialSelectionMode; set => SetValue(ref materialSelectionMode, value); }
    public bool DisplaySelectionMask { get => displaySelectionMask; set => SetValue(ref displaySelectionMask, value); }
    public bool CompilingAssets { get => compilingAssets; set => SetValue(ref compilingAssets, value); }

    public override void Destroy()
    {
        foreach (var entity in prefabCommandSelection) entity.PropertyChanged -= PrefabSelectionChanged;
        prefabCommandSelection.Clear();
        Viewport?.Destroy();
        Asset.AssetHierarchyPropertyGraph.PartAdded -= OnPartAdded;
        Asset.AssetHierarchyPropertyGraph.PartRemoved -= OnPartRemoved;
        foreach (var propagator in propagators.Values)
        {
            propagator.Destroy();
        }
        propagators.Clear();
        base.Destroy();
    }

    public override async Task DestroyAsync()
    {
        Destroy();
        if (Viewport != null) await Viewport.Controller.GameStopped;
    }

    /// <inheritdoc />
    protected override async Task RefreshEditorProperties()
    {
        // Selection replacement clears and adds items in the same UI turn.
        var version = ++propertiesRefreshVersion;
        await Task.Yield();
        if (IsDestroyed || version != propertiesRefreshVersion) return;

        EditorProperties.UpdateTypeAndName(SelectedItems, x => "Entity", x => x.Name, "entities");
        await EditorProperties.GenerateSelectionPropertiesAsync(SelectedItems.OfType<EntityViewModel>());
    }

    /// <inheritdoc />
    protected override void SelectedContentCollectionChanged(NotifyCollectionChangedAction action)
    {
        SelectedItems.Clear();
        SelectedItems.AddRange(SelectedContent.Cast<EntityHierarchyItemViewModel>().SelectMany(x => x.InnerSubEntities));
        DeleteCommand.IsEnabled = CanDelete();
        UpdatePrefabCommands();
    }

    protected override void SelectedItemsCollectionChanged(NotifyCollectionChangedAction action)
    {
        base.SelectedItemsCollectionChanged(action);
        UpdatePrefabCommands();
    }

    private Task CreateEntity(bool inSelection, EntityFactoryKind kind)
    {
        var parent = inSelection ? SelectedContent.OfType<EntityHierarchyItemViewModel>().FirstOrDefault() ?? HierarchyRoot : HierarchyRoot;
        var parentEntity = GetOwnerEntity(parent);
        var entity = CreateEntity(kind);
        if (entity is null)
            return Task.CompletedTask;

        AssetCollectionItemIdHelper.GenerateMissingItemIds(entity);
        var design = new EntityDesign(entity) { Folder = parent is EntityFolderViewModel folder ? folder.Path : string.Empty };
        var collection = new AssetPartCollection<EntityDesign, Entity>();
        collection.Add(design);
        var insertIndex = parentEntity?.AssetSideEntity.Transform.Children.Count ?? Asset.Asset.Hierarchy.RootParts.Count;
        var transaction = Session.ActionService?.CreateTransaction();
        try
        {
            Asset.AssetHierarchyPropertyGraph.AddPartToAsset(collection, design, parentEntity?.AssetSideEntity, insertIndex);
            if (transaction != null)
                Session.ActionService?.SetName(transaction, $"Create entity '{entity.Name}'");
        }
        finally
        {
            transaction?.Dispose();
        }

        var newEntity = FindPartViewModel(new AbsoluteId(Asset.Id, entity.Id));
        if (newEntity != null)
        {
            SelectedContent.Clear();
            SelectedContent.Add(newEntity);
            SelectionFocusRequested?.Invoke(this, EventArgs.Empty);
        }

        return Task.CompletedTask;
    }

    private static Entity CreateEntity(EntityFactoryKind kind)
    {
        var entity = new Entity();
        switch (kind)
        {
            case EntityFactoryKind.EmptyEntity:
                entity.Name = "Entity";
                break;
            case EntityFactoryKind.AudioEmitter:
                entity.Name = "AudioEmitter";
                entity.Components.Add(new AudioEmitterComponent());
                break;
            case EntityFactoryKind.AudioListener:
                entity.Name = "AudioListener";
                entity.Components.Add(new AudioListenerComponent());
                break;
            case EntityFactoryKind.PerspectiveCamera:
                entity.Name = "Camera";
                entity.Components.Add(new CameraComponent { Projection = CameraProjectionMode.Perspective });
                break;
            case EntityFactoryKind.OrthographicCamera:
                entity.Name = "Camera";
                entity.Components.Add(new CameraComponent { Projection = CameraProjectionMode.Orthographic });
                break;
            case EntityFactoryKind.DirectionalLight:
                entity.Name = "Directional light";
                entity.Transform.Position = new(0, 2, 0);
                entity.Transform.Rotation = Stride.Core.Mathematics.Quaternion.RotationX(Stride.Core.Mathematics.MathUtil.DegreesToRadians(-70));
                entity.Components.Add(new LightComponent());
                break;
            case EntityFactoryKind.PointLight:
                entity.Name = "Point light";
                entity.Transform.Position = new(0, 2, 0);
                entity.Components.Add(new LightComponent { Type = new LightPoint() });
                break;
            case EntityFactoryKind.SpotLight:
                entity.Name = "Spot light";
                entity.Transform.Position = new(0, 2, 0);
                entity.Transform.Rotation = Stride.Core.Mathematics.Quaternion.RotationX(Stride.Core.Mathematics.MathUtil.DegreesToRadians(-90));
                entity.Components.Add(new LightComponent { Type = new LightSpot() });
                break;
            case EntityFactoryKind.AmbientLight:
                entity.Name = "Ambient light";
                entity.Components.Add(new LightComponent { Type = new LightAmbient() });
                break;
            case EntityFactoryKind.VoxelLight:
                entity.Name = "Voxel light";
                entity.Components.Add(new LightComponent { Type = new LightVoxel() });
                break;
            case EntityFactoryKind.LightProbe:
                entity.Name = "Light probe";
                entity.Components.Add(new LightProbeComponent());
                break;
            case EntityFactoryKind.VoxelVolume:
                entity.Name = "Voxel volume";
                entity.Components.Add(new Stride.Rendering.Voxels.VoxelVolumeComponent { Attributes = { new Stride.Rendering.Voxels.VoxelAttributeEmissionOpacity() } });
                break;
            case EntityFactoryKind.NavigationBoundingBox:
                entity.Name = "Navigation bounding box";
                entity.Components.Add(new NavigationBoundingBoxComponent { Size = new(1.0f) });
                break;
            case EntityFactoryKind.EmptyParticleSystem:
                entity.Name = "Empty particle system";
                entity.Components.Add(new ParticleSystemComponent());
                break;
            case EntityFactoryKind.SimpleParticleSystem:
                entity.Name = "Simple particle system";
                entity.Components.Add(CreateSimpleParticleSystem());
                break;
            case EntityFactoryKind.FountainParticleSystem:
                entity.Name = "Smoke particle system";
                entity.Components.Add(CreateFountainParticleSystem());
                break;
            case EntityFactoryKind.RibbonParticleSystem:
                entity.Name = "Ribbon particle system";
                entity.Components.Add(CreateRibbonParticleSystem());
                break;
            case EntityFactoryKind.Sprite:
                entity.Name = "Sprite";
                entity.Components.Add(new SpriteComponent());
                break;
            case EntityFactoryKind.Ui:
                entity.Name = "UI";
                entity.Components.Add(new UIComponent());
                break;
            case EntityFactoryKind.Background:
                entity.Name = "Background";
                entity.Components.Add(new BackgroundComponent());
                break;
            case EntityFactoryKind.ModelInstance:
                entity.Name = "Instance";
                entity.Components.Add(new InstanceComponent());
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(kind), kind, null);
        }

        return entity;
    }

    private static ParticleSystemComponent CreateSimpleParticleSystem()
    {
        var component = new ParticleSystemComponent();
        var emitter = new ParticleEmitter { ParticleLifetime = new(1, 2) };
        emitter.Spawners.Add(new SpawnerPerSecond());
        emitter.Initializers.Add(new InitialSizeSeed { RandomSize = new(0.05f, 0.1f) });
        emitter.Initializers.Add(new InitialPositionSeed());
        emitter.Initializers.Add(new InitialVelocitySeed { VelocityMin = new(-0.5f, 1f, -0.5f), VelocityMax = new(0.5f, 3f, 0.5f) });
        var gravity = new UpdaterForceField { EnergyConservation = 1f, FieldShape = null };
        gravity.FieldFalloff.StrengthInside = gravity.FieldFalloff.StrengthOutside = 1f;
        gravity.ForceFixed = new(0, -9.81f, 0);
        gravity.ForceDirected = gravity.ForceRepulsive = gravity.ForceVortex = 0f;
        emitter.Updaters.Add(gravity);
        component.ParticleSystem.Emitters.Add(emitter);
        return component;
    }

    private static ParticleSystemComponent CreateFountainParticleSystem()
    {
        var component = new ParticleSystemComponent();
        var emitter = new ParticleEmitter { ParticleLifetime = new(1, 2) };
        emitter.Spawners.Add(new SpawnerPerSecond { SpawnCount = 20 });
        emitter.Initializers.Add(new InitialSizeSeed { RandomSize = new(0.35f, 0.55f) });
        emitter.Initializers.Add(new InitialPositionSeed { PositionMin = new(-0.2f, 0, -0.2f), PositionMax = new(0.2f, 0, 0.2f) });
        emitter.Initializers.Add(new InitialVelocitySeed { VelocityMin = new(-0.5f, 1f, -0.5f), VelocityMax = new(0.5f, 3f, 0.5f) });
        component.ParticleSystem.Emitters.Add(emitter);
        return component;
    }

    private static ParticleSystemComponent CreateRibbonParticleSystem()
    {
        var component = new ParticleSystemComponent();
        var emitter = new ParticleEmitter { ParticleLifetime = new(2, 2) };
        emitter.Spawners.Add(new SpawnerPerSecond { SpawnCount = 30 });
        emitter.ShapeBuilder = new ShapeBuilderRibbon
        {
            SmoothingPolicy = SmoothingPolicy.Best,
            Segments = 15,
            TextureCoordinatePolicy = TextureCoordinatePolicy.Stretched,
            TexCoordsFactor = 1f,
        };
        emitter.Initializers.Add(new InitialVelocitySeed { VelocityMin = new(-0.15f, 3f, -0.15f), VelocityMax = new(0.15f, 3f, 0.15f) });
        emitter.Initializers.Add(new InitialSpawnOrder());
        var curve = new ComputeAnimationCurveFloat();
        curve.KeyFrames.Add(new AnimationKeyFrame<float> { Key = 0, Value = 0.1f });
        curve.KeyFrames.Add(new AnimationKeyFrame<float> { Key = 0.9f, Value = 0f });
        emitter.Updaters.Add(new UpdaterSizeOverTime { SamplerMain = { Curve = curve } });
        emitter.SortingPolicy = EmitterSortingPolicy.ByOrder;
        component.ParticleSystem.Emitters.Add(emitter);
        return component;
    }

    private void CreateFolder(bool inSelection)
    {
        var parent = inSelection ? SelectedContent.OfType<EntityHierarchyItemViewModel>().FirstOrDefault() ?? HierarchyRoot : HierarchyRoot;
        const string baseName = "Folder";
        var existingNames = parent.Children.OfType<EntityFolderViewModel>().Select(x => x.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var name = baseName;
        for (var suffix = 1; existingNames.Contains(name); suffix++)
            name = $"{baseName} {suffix}";

        var folder = parent.CreateFolder(name);
        var ownerId = GetOwnerEntity(parent)?.Id;
        var folderPath = folder.Path;
        var transaction = Session.ActionService?.CreateTransaction();
        try
        {
            Session.ActionService?.PushOperation(new AnonymousDirtyingOperation(
                Asset.Dirtiables,
                () => RemoveFolder(ownerId, folderPath),
                () => RestoreFolder(ownerId, folderPath)));
            if (transaction is not null)
                Session.ActionService?.SetName(transaction, $"Create folder '{name}'");
        }
        finally
        {
            transaction?.Dispose();
        }

        SelectedContent.Clear();
        SelectedContent.Add(folder);
    }

    private static EntityViewModel? GetOwnerEntity(EntityHierarchyItemViewModel? item)
    {
        for (var current = item; current is not null; current = current.Parent)
        {
            if (current is EntityViewModel entity)
                return entity;
        }

        return null;
    }

    private async void OnPartAdded(object? sender, AssetPartChangeEventArgs e)
    {
        HierarchyRoot.RefreshChildren();
        Asset.AssetItem.IsDirty = true;
        UpdateVisibilities();

        if (Viewport?.SceneInitialized == true && Viewport.Controller != null)
        {
            try
            {
                if (!Asset.Asset.Hierarchy.Parts.TryGetValue(e.PartId, out var partDesign))
                    return;

                var partViewModel = FindPartViewModel(new AbsoluteId(Asset.Id, e.PartId)) as EntityViewModel;
                if (partViewModel == null)
                    return;

                var parent = partViewModel.Parent as EntityHierarchyItemViewModel ?? HierarchyRoot;
                var allNewEntities = partViewModel.Children.BreadthFirst(x => x.Children).OfType<EntityViewModel>().Concat([partViewModel]).ToList();

                foreach (var elem in allNewEntities)
                {
                    RegisterPropagator(elem);
                }

                await Viewport.Controller.AddPart(parent, partDesign.Entity);

                foreach (var elem in allNewEntities)
                {
                    if (propagators.TryGetValue(elem.AssetSideEntity.Id, out var prop))
                    {
                        prop.NotifyGameSidePartAdded();
                    }
                }
            }
            catch (Exception ex)
            {
                Viewport.Controller.Logger.Error("Failed to add part to viewport", ex);
            }
        }
    }

    private void OnPartRemoved(object? sender, AssetPartChangeEventArgs e)
    {
        var partId = new AbsoluteId(Asset.Id, e.PartId);
        var viewModel = FindPartViewModel(partId) as EntityViewModel;

        List<EntityViewModel> entitiesToRemove;
        EntityHierarchyItemViewModel parent;
        Entity? assetSideEntity = null;

        if (viewModel != null)
        {
            entitiesToRemove = viewModel.Children.BreadthFirst(x => x.Children).OfType<EntityViewModel>().Concat([viewModel]).ToList();
            parent = viewModel.Parent as EntityHierarchyItemViewModel ?? HierarchyRoot;
            assetSideEntity = viewModel.AssetSideEntity;

            if (SelectedContent.Contains(viewModel))
            {
                SelectedContent.Remove(viewModel);
                SelectedItems.Remove(viewModel);
            }
        }
        else
        {
            entitiesToRemove = [];
            parent = HierarchyRoot;
            if (Asset.Asset.Hierarchy.Parts.TryGetValue(e.PartId, out var design))
            {
                assetSideEntity = design.Entity;
            }
        }

        foreach (var entity in entitiesToRemove)
        {
            UnregisterPropagator(entity.AssetSideEntity.Id);
        }
        UnregisterPropagator(e.PartId);

        HierarchyRoot.RefreshChildren();
        Asset.AssetItem.IsDirty = true;
        UpdateVisibilities();

        if (Viewport?.SceneInitialized == true && Viewport.Controller != null && assetSideEntity != null)
        {
            Viewport.Controller.RemovePart(parent, assetSideEntity).Forget();
        }
    }

    private void UpdateVisibilities()
    {
        var tokens = filterPattern.Split(" \t\n\r".ToCharArray(), StringSplitOptions.RemoveEmptyEntries);
        foreach (var item in HierarchyRoot.Children)
            UpdateVisibility(item, tokens);
    }

    private static bool UpdateVisibility(EntityHierarchyItemViewModel item, IReadOnlyCollection<string> tokens)
    {
        var hasVisibleChild = false;
        foreach (var child in item.Children)
            hasVisibleChild |= UpdateVisibility(child, tokens);

        var matches = tokens.Count == 0 || tokens.All(token => item.Name.Contains(token, StringComparison.OrdinalIgnoreCase));
        item.IsVisible = hasVisibleChild || matches;
        return item.IsVisible;
    }

    public bool CanDelete()
    {
        return SelectedContent.Count > 0
            && !SelectedContent.Contains(HierarchyRoot)
            && SelectedContent.Any(x => x is EntityViewModel or EntityFolderViewModel);
    }

    public async Task DeleteSelection()
    {
        await DeleteItems(SelectedContent.OfType<EntityHierarchyItemViewModel>().ToArray());
    }

    public async Task DeleteItems(IReadOnlyCollection<EntityHierarchyItemViewModel> items, bool askConfirmation = true)
    {
        var roots = GetCommonRoots(items.Where(item => item is EntityViewModel or EntityFolderViewModel).ToArray());
        var entitiesToDelete = GetCommonRoots(roots.SelectMany(item => item.InnerSubEntities).ToArray());
        var foldersToDelete = roots.OfType<EntityFolderViewModel>().Select(folder => (
            OwnerId: GetOwnerEntity(folder.Parent)?.Id,
            Path: folder.Path,
            Paths: new[] { folder }.BreadthFirst(item => item.Children.OfType<EntityFolderViewModel>()).Select(item => item.Path).ToArray())).ToArray();

        if (entitiesToDelete.Count == 0 && foldersToDelete.Length == 0)
            return;

        var ask = SceneEditorSettings.AskBeforeDeletingEntities.GetValue();
        var dialogService = ServiceProvider.TryGet<IDialogService>();
        if (askConfirmation && ask && dialogService?.HasMainWindow == true)
        {
            var count = entitiesToDelete.Count + foldersToDelete.Length;
            var confirmMessage = count > 1
                ? $"Are you sure you want to delete these {count} items?"
                : "Are you sure you want to delete this item?";
            var checkedMessage = "Always delete without asking";
            var deleteButton = new DialogButtonInfo { Content = "Delete", Result = 1, IsDefault = true };
            var cancelButton = new DialogButtonInfo { Content = "Cancel", Result = 2, IsCancel = true };
            var result = await dialogService.CheckedMessageBoxAsync(confirmMessage, false, checkedMessage, [deleteButton, cancelButton], MessageBoxImage.Question);
            if (result.Result != 1)
                return;

            if (result.IsChecked == true)
            {
                SceneEditorSettings.AskBeforeDeletingEntities.SetValue(false);
                SceneEditorSettings.Save();
            }
        }

        var transaction = Session.ActionService?.CreateTransaction();
        try
        {
            SelectedContent.Clear();
            SelectedItems.Clear();

            // 1. Delete entities
            var entitiesPerAsset = entitiesToDelete.GroupBy(x => (EntityHierarchyViewModel)x.Asset);
            foreach (var group in entitiesPerAsset)
            {
                group.Key.AssetHierarchyPropertyGraph.DeleteParts(group.Select(x => x.EntityDesign), out var mapping);
                if (mapping != null && mapping.Count > 0)
                {
                    var propertyGraph = group.Key.AssetHierarchyPropertyGraph;
                    Session.ActionService?.PushOperation(new AnonymousDirtyingOperation(
                        group.Key.Dirtiables,
                        () => propertyGraph.UntrackDeletedInstanceParts(mapping),
                        () => propertyGraph.TrackDeletedInstanceParts(mapping)));
                }
            }

            // 2. Delete folders
            foreach (var folder in foldersToDelete)
            {
                Session.ActionService?.PushOperation(new AnonymousDirtyingOperation(
                    Asset.Dirtiables,
                    () => { foreach (var path in folder.Paths) RestoreFolder(folder.OwnerId, path); },
                    () => RemoveFolder(folder.OwnerId, folder.Path)));
                RemoveFolder(folder.OwnerId, folder.Path);
            }

            if (transaction != null)
                Session.ActionService?.SetName(transaction, entitiesToDelete.Count + foldersToDelete.Length > 1 ? "Delete entities" : "Delete entity");
        }
        finally
        {
            transaction?.Dispose();
        }
    }

    private EntityHierarchyItemViewModel? FindFolder(AbsoluteId? ownerId, string path)
    {
        EntityHierarchyItemViewModel? current = ownerId.HasValue ? FindPartViewModel(ownerId.Value) as EntityViewModel : HierarchyRoot;
        foreach (var name in path.Split('/', StringSplitOptions.RemoveEmptyEntries))
            current = current?.Children.OfType<EntityFolderViewModel>().FirstOrDefault(folder => string.Equals(folder.Name, name, StringComparison.OrdinalIgnoreCase));
        return current;
    }

    private void RestoreFolder(AbsoluteId? ownerId, string path)
    {
        var current = FindFolder(ownerId, string.Empty);
        if (current == null) return;
        foreach (var name in path.Split('/', StringSplitOptions.RemoveEmptyEntries))
            current = current.Children.OfType<EntityFolderViewModel>().FirstOrDefault(folder => string.Equals(folder.Name, name, StringComparison.OrdinalIgnoreCase)) ?? current.CreateFolder(name);
    }

    private void RemoveFolder(AbsoluteId? ownerId, string path)
    {
        if (FindFolder(ownerId, path) is EntityFolderViewModel folder)
            folder.Parent?.RemoveFolder(folder);
    }

    public IReadOnlyList<EntityViewModel> DuplicateSelection()
    {
        var roots = GetCommonRoots(SelectedItems.OfType<EntityViewModel>().ToArray());
        if (roots.Count == 0) return [];
        var hierarchy = AssetCompositeHierarchyPropertyGraph<EntityDesign, Entity>.CloneSubHierarchies(
            Asset.Session.AssetNodeContainer, Asset.Asset, roots.Select(entity => entity.Id.ObjectId),
            SubHierarchyCloneFlags.GenerateNewIdsForIdentifiableObjects, out var mapping);
        AssetPartsAnalysis.GenerateNewBaseInstanceIds(hierarchy);
        using (var transaction = Session.ActionService?.CreateTransaction())
        {
            foreach (var source in roots)
            {
                var parent = source.AssetSideEntity.Transform.Parent?.Entity;
                var parentVm = parent == null ? null : FindPartViewModel(new AbsoluteId(Asset.Id, parent.Id)) as EntityViewModel;
                var design = hierarchy.Parts[mapping[source.Id.ObjectId]];
                design.Entity.Name = ComputeNewEntityName(source.AssetSideEntity.Name, parentVm, isDuplicate: true);
                var index = parent == null ? Asset.Asset.Hierarchy.RootParts.IndexOf(source.AssetSideEntity)
                    : parent.Transform.Children.IndexOf(source.AssetSideEntity.Transform);
                Asset.AssetHierarchyPropertyGraph.AddPartToAsset(hierarchy.Parts, design, parent, index + 1);
            }
            if (transaction != null) Session.ActionService?.SetName(transaction, "Duplicate entities");
        }
        var result = hierarchy.RootParts.Select(entity => FindPartViewModel(new AbsoluteId(Asset.Id, entity.Id))).OfType<EntityViewModel>().ToList();
        SelectedContent.Clear();
        foreach (var entity in result) SelectedContent.Add(entity);
        SelectionFocusRequested?.Invoke(this, EventArgs.Empty);
        return result;
    }

    public bool CanMoveItems(IReadOnlyCollection<EntityHierarchyItemViewModel> items, EntityHierarchyItemViewModel? target, DropPosition position, out string message)
    {
        message = string.Empty;
        if (items == null || items.Count == 0 || target == null)
            return false;

        if (items.Any(x => x is SceneRootViewModel or PrefabRootViewModel))
        {
            message = "Cannot move root scene or prefab.";
            return false;
        }

        if (items.Contains(target))
        {
            message = "Cannot move an item to itself.";
            return false;
        }

        if (items.Any(item => IsDescendantOf(target, item)))
        {
            message = "Cannot move an item into its own descendant.";
            return false;
        }

        if (position is DropPosition.Before or DropPosition.After)
        {
            if (target is SceneRootViewModel or PrefabRootViewModel)
            {
                message = "Cannot insert before or after the scene root.";
                return false;
            }
        }

        return true;
    }

    public void MoveItems(IReadOnlyCollection<EntityHierarchyItemViewModel> items, EntityHierarchyItemViewModel target, DropPosition position)
    {
        if (!CanMoveItems(items, target, position, out _))
            return;

        var entitiesToMove = GetCommonRoots(items.OfType<EntityViewModel>().ToArray());
        var foldersToMove = items.OfType<EntityFolderViewModel>()
            .Where(f => !entitiesToMove.Any(e => IsDescendantOf(f, e)))
            .ToArray();

        if (entitiesToMove.Count == 0 && foldersToMove.Length == 0)
            return;

        EntityViewModel? targetParentEntity = null;
        string targetFolderPath = string.Empty;
        int insertIndex = 0;

        if (position == DropPosition.Into)
        {
            if (target is EntityFolderViewModel folder)
            {
                targetParentEntity = GetOwnerEntity(folder);
                targetFolderPath = folder.Path;
                insertIndex = targetParentEntity != null
                    ? targetParentEntity.AssetSideEntity.Transform.Children.Count
                    : Asset.Asset.Hierarchy.RootParts.Count;
            }
            else if (target is EntityViewModel entityTarget)
            {
                targetParentEntity = entityTarget;
                targetFolderPath = string.Empty;
                insertIndex = entityTarget.AssetSideEntity.Transform.Children.Count;
            }
            else
            {
                targetParentEntity = null;
                targetFolderPath = string.Empty;
                insertIndex = Asset.Asset.Hierarchy.RootParts.Count;
            }
        }
        else
        {
            var targetParentItem = target.Parent;
            targetParentEntity = GetOwnerEntity(targetParentItem);
            targetFolderPath = (targetParentItem as EntityFolderViewModel)?.Path ?? string.Empty;

            if (targetParentEntity != null)
            {
                var children = targetParentEntity.AssetSideEntity.Transform.Children;
                var targetIndex = target is EntityViewModel ev ? children.IndexOf(ev.AssetSideEntity.Transform) : -1;
                if (targetIndex < 0) targetIndex = children.Count;
                insertIndex = position == DropPosition.Before ? targetIndex : targetIndex + 1;
            }
            else
            {
                var rootParts = Asset.Asset.Hierarchy.RootParts;
                var targetIndex = target is EntityViewModel ev ? rootParts.IndexOf(ev.AssetSideEntity) : -1;
                if (targetIndex < 0) targetIndex = rootParts.Count;
                insertIndex = position == DropPosition.Before ? targetIndex : targetIndex + 1;
            }
        }

        var transaction = Session.ActionService?.CreateTransaction();
        try
        {
            foreach (var entity in entitiesToMove)
            {
                // Maintain world transformation
                var childTransform = entity.AssetSideEntity.Transform;
                childTransform.UpdateWorldMatrix();
                childTransform.GetWorldTransformation(out var worldPos, out var worldRot, out var worldScale);

                if (targetParentEntity != null)
                {
                    targetParentEntity.AssetSideEntity.Transform.UpdateWorldMatrix();
                    targetParentEntity.AssetSideEntity.Transform.WorldToLocal(ref worldPos, ref worldRot, ref worldScale);
                }

                // Adjust index if entity is being reordered within the same parent
                if (childTransform.Parent == targetParentEntity?.AssetSideEntity.Transform)
                {
                    int oldIndex;
                    if (targetParentEntity != null)
                        oldIndex = targetParentEntity.AssetSideEntity.Transform.Children.IndexOf(entity.AssetSideEntity.Transform);
                    else
                        oldIndex = Asset.Asset.Hierarchy.RootParts.IndexOf(entity.AssetSideEntity);

                    if (oldIndex >= 0 && oldIndex < insertIndex)
                        insertIndex--;
                }

                var hierarchy = AssetCompositeHierarchyPropertyGraph<EntityDesign, Entity>.CloneSubHierarchies(
                    Asset.Session.AssetNodeContainer,
                    Asset.Session.AssetNodeContainer,
                    Asset.Asset,
                    new[] { entity.Id.ObjectId },
                    SubHierarchyCloneFlags.None,
                    out _);

                var movedDesign = hierarchy.Parts[entity.Id.ObjectId];
                movedDesign.Folder = targetFolderPath;
                movedDesign.Entity.Transform.Position = worldPos;
                movedDesign.Entity.Transform.Rotation = worldRot;
                movedDesign.Entity.Transform.Scale = worldScale;

                Asset.AssetHierarchyPropertyGraph.RemovePartFromAsset(entity.EntityDesign);
                Asset.AssetHierarchyPropertyGraph.AddPartToAsset(hierarchy.Parts, movedDesign, targetParentEntity?.AssetSideEntity, Math.Max(0, insertIndex));
                insertIndex++;
            }

            foreach (var folder in foldersToMove)
            {
                var oldPath = folder.Path;
                var newPath = EntityFolderViewModel.CombinePath(targetFolderPath, folder.Name ?? "Folder");
                if (oldPath != newPath)
                {
                    foreach (var entity in folder.InnerSubEntities)
                    {
                        var entityRelativePath = entity.EntityDesign.Folder.StartsWith(oldPath)
                            ? entity.EntityDesign.Folder.Substring(oldPath.Length)
                            : string.Empty;
                        var updatedEntityFolder = newPath + entityRelativePath;

                        var node = Asset.Session.AssetNodeContainer.GetOrCreateNode(entity.EntityDesign)[nameof(EntityDesign.Folder)];
                        var oldValue = node.Retrieve();
                        node.Update(updatedEntityFolder);
                        Session.ActionService?.PushOperation(new ContentValueChangeOperation(
                            node, ContentChangeType.ValueChange, NodeIndex.Empty, oldValue, updatedEntityFolder, Asset.Dirtiables));
                    }
                }
            }

            if (transaction != null)
                Session.ActionService?.SetName(transaction, "Move hierarchy items");
        }
        finally
        {
            transaction?.Dispose();
        }

        var movedIds = entitiesToMove.Select(x => x.Id).ToHashSet();
        SelectedContent.Clear();
        foreach (var id in movedIds)
        {
            if (FindPartViewModel(id) is EntityHierarchyItemViewModel vm)
                SelectedContent.Add(vm);
        }
    }

    public bool CanDropAssets(IReadOnlyCollection<AssetViewModel> assets, EntityHierarchyItemViewModel? target, DropPosition position, out string message)
    {
        if (assets.Count == 0)
        {
            message = "Empty selection";
            return false;
        }

        var parentName = target is null or EntityHierarchyRootViewModel ? "root level" : target.Name;
        foreach (var asset in assets)
        {
            if (asset is PrefabViewModel prefabVm)
            {
                if (prefabVm == Asset || prefabVm.GatherAllBasePartAssets().Contains(Asset))
                {
                    message = "This prefab depends on this asset and can't be added.";
                    return false;
                }
                if (prefabVm.Asset.Hierarchy.Parts.Count == 0)
                {
                    message = "This prefab is empty and can't be added.";
                    return false;
                }
            }
            else if (asset.Asset is IModelAsset or ModelAsset or TextureAsset or UIPageAsset or SoundAsset or VideoAsset)
            {
                // Supported
            }
            else
            {
                message = $"Can't add {asset.AssetType.Name}s here";
                return false;
            }
        }

        message = $"Add to {parentName}";
        return true;
    }

    public IReadOnlyList<EntityViewModel> DropAssets(IReadOnlyCollection<AssetViewModel> assets, EntityHierarchyItemViewModel? target, DropPosition position, Vector3 rootPosition = default)
    {
        if (assets.Count == 0) return [];

        target ??= HierarchyRoot;
        EntityViewModel? targetParentEntity;
        string targetFolderPath;
        int insertIndex;

        if (position == DropPosition.Into)
        {
            if (target is EntityFolderViewModel folder)
            {
                targetParentEntity = GetOwnerEntity(folder);
                targetFolderPath = folder.Path;
                insertIndex = targetParentEntity != null
                    ? targetParentEntity.AssetSideEntity.Transform.Children.Count
                    : Asset.Asset.Hierarchy.RootParts.Count;
            }
            else if (target is EntityViewModel entityTarget)
            {
                targetParentEntity = entityTarget;
                targetFolderPath = string.Empty;
                insertIndex = entityTarget.AssetSideEntity.Transform.Children.Count;
            }
            else
            {
                targetParentEntity = null;
                targetFolderPath = string.Empty;
                insertIndex = Asset.Asset.Hierarchy.RootParts.Count;
            }
        }
        else
        {
            var targetParentItem = target.Parent;
            targetParentEntity = GetOwnerEntity(targetParentItem);
            targetFolderPath = (targetParentItem as EntityFolderViewModel)?.Path ?? string.Empty;

            if (targetParentEntity != null)
            {
                var children = targetParentEntity.AssetSideEntity.Transform.Children;
                var targetIndex = target is EntityViewModel ev ? children.IndexOf(ev.AssetSideEntity.Transform) : -1;
                if (targetIndex < 0) targetIndex = children.Count;
                insertIndex = position == DropPosition.Before ? targetIndex : targetIndex + 1;
            }
            else
            {
                var rootParts = Asset.Asset.Hierarchy.RootParts;
                var targetIndex = target is EntityViewModel ev ? rootParts.IndexOf(ev.AssetSideEntity) : -1;
                if (targetIndex < 0) targetIndex = rootParts.Count;
                insertIndex = position == DropPosition.Before ? targetIndex : targetIndex + 1;
            }
        }

        var newEntities = new AssetCompositeHierarchyData<EntityDesign, Entity>();
        foreach (var asset in assets)
        {
            if (asset.Asset is PrefabAsset prefabAsset)
            {
                var instance = prefabAsset.CreatePrefabInstance(asset.Url);
                var prefabName = ComputeNewEntityName(asset.Name, targetParentEntity);
                var prefabRoot = new EntityDesign(new Entity(prefabName), targetFolderPath);
                prefabRoot.Entity.Transform.Position = rootPosition;
                foreach (var rootPart in instance.RootParts)
                {
                    prefabRoot.Entity.Transform.Children.Add(rootPart.Transform);
                }
                foreach (var part in instance.Parts)
                {
                    newEntities.Parts.Add(part.Value);
                }
                newEntities.Parts.Add(prefabRoot);
                newEntities.RootParts.Add(prefabRoot.Entity);
            }
            else
            {
                var component = CreateComponentFromAsset(asset);
                if (component != null)
                {
                    var entityName = ComputeNewEntityName(asset.Name, targetParentEntity);
                    var newEntity = new Entity(entityName)
                    {
                        Components = { component }
                    };
                    newEntity.Transform.Position = rootPosition;
                    var design = new EntityDesign(newEntity, targetFolderPath);
                    newEntities.Parts.Add(design);
                    newEntities.RootParts.Add(newEntity);
                }
            }
        }

        if (newEntities.RootParts.Count == 0) return [];

        AssetCollectionItemIdHelper.GenerateMissingItemIds(newEntities);

        var transaction = Session.ActionService?.CreateTransaction();
        try
        {
            foreach (var entity in newEntities.RootParts)
            {
                Asset.AssetHierarchyPropertyGraph.AddPartToAsset(newEntities.Parts, newEntities.Parts[entity.Id], targetParentEntity?.AssetSideEntity, insertIndex++);
            }

            Asset.AssetItem.IsDirty = true;

            if (transaction != null)
                Session.ActionService?.SetName(transaction, $"Add {assets.Count} asset(s) to hierarchy");
        }
        finally
        {
            transaction?.Dispose();
        }

        var result = newEntities.RootParts.Select(entity => FindPartViewModel(new AbsoluteId(Asset.Id, entity.Id)))
            .OfType<EntityViewModel>().ToList();
        SelectedContent.Clear();
        foreach (var vm in result)
        {
            SelectedContent.Add(vm);
        }
        SelectionFocusRequested?.Invoke(this, EventArgs.Empty);

        return result;
    }

    private static EntityComponent? CreateComponentFromAsset(AssetViewModel asset)
    {
        if (asset.Asset is IModelAsset or ModelAsset)
        {
            return new ModelComponent
            {
                Model = ContentReferenceHelper.CreateReference<Model>(asset)
            };
        }
        if (asset.Asset is TextureAsset)
        {
            return new SpriteComponent
            {
                SpriteProvider = new SpriteFromTexture
                {
                    Texture = ContentReferenceHelper.CreateReference<Texture>(asset)
                }
            };
        }
        if (asset.Asset is UIPageAsset)
        {
            return new UIComponent
            {
                Page = ContentReferenceHelper.CreateReference<UIPage>(asset)
            };
        }
        if (asset.Asset is SoundAsset)
        {
            var emitter = new AudioEmitterComponent();
            var sound = ContentReferenceHelper.CreateReference<Sound>(asset);
            if (sound != null)
                emitter.Sounds.Add(asset.Name, sound);
            return emitter;
        }
        if (asset.Asset is VideoAsset)
        {
            return new VideoComponent
            {
                Source = ContentReferenceHelper.CreateReference<Stride.Video.Video>(asset)
            };
        }

        return null;
    }

    private string ComputeNewEntityName(string baseName, EntityViewModel? parentEntity, bool isDuplicate = false)
    {
        var existingNames = parentEntity != null
            ? parentEntity.AssetSideEntity.Transform.Children.Select(c => c.Entity.Name).ToHashSet(StringComparer.OrdinalIgnoreCase)
            : Asset.Asset.Hierarchy.RootParts.Select(p => p.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);

        if (!isDuplicate && !existingNames.Contains(baseName))
            return baseName;

        return NamingHelper.ComputeDuplicateName(baseName, existingNames.Contains);
    }

    public static bool IsDescendantOf(EntityHierarchyItemViewModel? possibleDescendant, EntityHierarchyItemViewModel ancestor)
    {
        for (var current = possibleDescendant?.Parent; current != null; current = current.Parent)
        {
            if (ReferenceEquals(current, ancestor))
                return true;
        }
        return false;
    }

    public static ISet<TViewModel> GetCommonRoots<TViewModel>(IReadOnlyCollection<TViewModel> items)
        where TViewModel : EntityHierarchyItemViewModel
    {
        var hashSet = new HashSet<TViewModel>(items);
        foreach (var item in items)
        {
            for (var parent = item.Parent; parent != null; parent = parent.Parent)
            {
                if (parent is TViewModel vm && hashSet.Contains(vm))
                {
                    hashSet.Remove(item);
                    break;
                }
            }
        }
        return hashSet;
    }
}
