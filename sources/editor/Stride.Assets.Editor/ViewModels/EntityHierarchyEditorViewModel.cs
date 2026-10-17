// Copyright (c) .NET Foundation and Contributors (https://dotnetfoundation.org/ & https://stride3d.net) and Silicon Studio Corp. (https://www.siliconstudio.co.jp)
// Distributed under the MIT license. See the LICENSE.md file in the project root for more information.

using System.Collections.Specialized;
using Stride.Assets.Entities;
using Stride.Assets.Presentation.ViewModels;
using Stride.Core.Assets;
using Stride.Core.Assets.Editor.ViewModels;
using Stride.Core.Assets.Quantum;
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

public abstract class EntityHierarchyEditorViewModel : AssetCompositeHierarchyEditorViewModel<EntityDesign, Entity, EntityHierarchyViewModel, EntityHierarchyItemViewModel>
{
    private string filterPattern = string.Empty;
    protected EntityHierarchyEditorViewModel(EntityHierarchyViewModel asset)
        : base(asset)
    {
        CreateEntityInRootCommand = new AnonymousTaskCommand<EntityFactoryKind>(ServiceProvider, kind => CreateEntity(false, kind));
        CreateEntityInSelectionCommand = new AnonymousTaskCommand<EntityFactoryKind>(ServiceProvider, kind => CreateEntity(true, kind));
        CreateFolderInRootCommand = new AnonymousCommand(ServiceProvider, () => CreateFolder(false));
        CreateFolderInSelectionCommand = new AnonymousCommand(ServiceProvider, () => CreateFolder(true));
        Asset.AssetHierarchyPropertyGraph.PartAdded += HierarchyPartChanged;
        Asset.AssetHierarchyPropertyGraph.PartRemoved += HierarchyPartChanged;
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

    public override void Destroy()
    {
        Asset.AssetHierarchyPropertyGraph.PartAdded -= HierarchyPartChanged;
        Asset.AssetHierarchyPropertyGraph.PartRemoved -= HierarchyPartChanged;
        base.Destroy();
    }

    /// <inheritdoc />
    protected override async Task RefreshEditorProperties()
    {
        EditorProperties.UpdateTypeAndName(SelectedItems, x => "Entity", x => x.Name, "entities");
        await EditorProperties.GenerateSelectionPropertiesAsync(SelectedItems.OfType<EntityViewModel>());
    }

    /// <inheritdoc />
    protected override void SelectedContentCollectionChanged(NotifyCollectionChangedAction action)
    {
        SelectedItems.Clear();
        SelectedItems.AddRange(SelectedContent.Cast<EntityHierarchyItemViewModel>().SelectMany(x => x.InnerSubEntities));
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
        var transaction = Session.ActionService?.CreateTransaction();
        try
        {
            Session.ActionService?.PushOperation(new AnonymousDirtyingOperation(
                Asset.Dirtiables,
                () => parent.RemoveFolder(folder),
                () => parent.AddFolder(folder)));
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

    private static EntityViewModel? GetOwnerEntity(EntityHierarchyItemViewModel item)
    {
        for (var current = item; current is not null; current = current.Parent)
        {
            if (current is EntityViewModel entity)
                return entity;
        }

        return null;
    }

    private void HierarchyPartChanged(object? sender, AssetPartChangeEventArgs args)
    {
        HierarchyRoot.RefreshChildren();
        UpdateVisibilities();
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
}
