// Copyright (c) .NET Foundation and Contributors (https://dotnetfoundation.org/ & https://stride3d.net)
// Distributed under the MIT license. See the LICENSE.md file in the project root for more information.

using Stride.Assets.Editor.EntityHierarchyEditor.Game;
using Stride.Assets.Editor.EntityHierarchyEditor.Services;
using Stride.Assets.Editor.GameEditor;
using Stride.Assets.Presentation.ViewModels;
using Stride.Core;
using Stride.Core.Assets.Presentation.ViewModels;
using Stride.Core.Presentation.Commands;
using Stride.Core.Quantum;
using Stride.Engine;
using Stride.Assets.Materials;
using System.Runtime.CompilerServices;

namespace Stride.Assets.Editor.ViewModels;

public sealed class SceneViewportViewModel : GameEditorViewModel
{
    private readonly SemaphoreSlim reloadLock = new(1, 1);
    private bool destroyed;
    private bool reloadPending;
    private Transformation transformation;
    private TransformationSpace space;
    private TransformationPivot pivot;
    private bool snap;
    private bool gridVisible = true;
    private bool gizmosVisible = true;
    private float translationSnap = 1;
    private float rotationSnap = 15;
    private float scaleSnap = .1f;
    private ViewportCameraSettings cameraSettings = ViewportCameraSettings.Default;
    private EditorRenderMode renderMode = EditorRenderMode.DefaultEditor;
    private EditorGameEntityCameraService? cameraService;

    public SceneViewportViewModel(EntityHierarchyEditorViewModel owner)
        : base(owner.Asset, model => new SceneViewportController(owner.Asset, (SceneViewportViewModel)model))
    {
        Owner = owner;
        TranslateCommand = new AnonymousCommand(ServiceProvider, () => ActiveTransformation = Transformation.Translation);
        RotateCommand = new AnonymousCommand(ServiceProvider, () => ActiveTransformation = Transformation.Rotation);
        ScaleCommand = new AnonymousCommand(ServiceProvider, () => ActiveTransformation = Transformation.Scale);
        FrameSelectionCommand = new AnonymousTaskCommand(ServiceProvider, FrameSelection);
        FrameEntityCommand = new AnonymousTaskCommand<EntityViewModel>(ServiceProvider, FrameEntity);
        Session.AssetPropertiesChanged += AssetPropertiesChanged;
    }

    public EntityHierarchyEditorViewModel Owner { get; }
    public new SceneViewportController Controller => (SceneViewportController)base.Controller;
    public object? EditorHost => Controller.EditorHost;
    public ICommandBase TranslateCommand { get; }
    public ICommandBase RotateCommand { get; }
    public ICommandBase ScaleCommand { get; }
    public ICommandBase FrameSelectionCommand { get; }
    public ICommandBase FrameEntityCommand { get; }

    public async Task FrameEntity(EntityViewModel entity)
    {
        if (destroyed || !SceneInitialized || entity.Asset != Owner.Asset) return;
        await Controller.InvokeAsync(() => Controller.Frame([entity.Id]));
    }
    public TransformationSpace[] Spaces { get; } = Enum.GetValues<TransformationSpace>();
    public TransformationPivot[] Pivots { get; } = Enum.GetValues<TransformationPivot>();

    public TransformationPivot Pivot
    {
        get => pivot;
        set { if (SetValue(ref pivot, value)) UpdateTransformSettings(); }
    }

    public Transformation ActiveTransformation
    {
        get => transformation;
        set { if (SetValue(ref transformation, value)) UpdateTransformSettings(); }
    }

    public TransformationSpace Space
    {
        get => space;
        set { if (SetValue(ref space, value)) UpdateTransformSettings(); }
    }

    public bool Snap
    {
        get => snap;
        set { if (SetValue(ref snap, value)) UpdateTransformSettings(); }
    }

    public bool GridVisible
    {
        get => gridVisible;
        set
        {
            if (SetValue(ref gridVisible, value) && !destroyed && SceneInitialized)
                _ = Controller.InvokeAsync(() => Controller.GetGridService().IsActive = value);
        }
    }

    public bool GizmosVisible
    {
        get => gizmosVisible;
        set
        {
            if (SetValue(ref gizmosVisible, value) && !destroyed && SceneInitialized)
                _ = Controller.InvokeAsync(() => Controller.GetComponentGizmoService().IsActive = value);
        }
    }

    public IReadOnlyList<EditorRenderMode> RenderModes { get; } = new[] { EditorRenderMode.DefaultEditor, EditorRenderMode.DefaultGamePreview }
        .Concat(MaterialStreamFactory.GetAvailableStreams().Select(stream => new EditorRenderMode(stream))).ToArray();

    public EditorRenderMode RenderMode
    {
        get => renderMode;
        set
        {
            if (!RenderModes.Contains(value)) throw new ArgumentOutOfRangeException(nameof(value));
            if (SetValue(ref renderMode, value) && !destroyed && SceneInitialized)
                _ = Controller.InvokeAsync(() => Controller.GetRenderModeService().RenderMode = value);
        }
    }

    public bool CameraOrthographic { get => cameraSettings.Orthographic; set => SetCamera(cameraSettings with { Orthographic = value }); }
    public float CameraNearPlane { get => cameraSettings.NearPlane; set => SetCamera(cameraSettings with { NearPlane = value }); }
    public float CameraFarPlane { get => cameraSettings.FarPlane; set => SetCamera(cameraSettings with { FarPlane = value }); }
    public float CameraFieldOfView { get => cameraSettings.FieldOfView; set => SetCamera(cameraSettings with { FieldOfView = value }); }
    public float CameraOrthographicSize { get => cameraSettings.OrthographicSize; set => SetCamera(cameraSettings with { OrthographicSize = value }); }
    public float CameraMoveSpeed { get => cameraSettings.MoveSpeed; set => SetCamera(cameraSettings with { MoveSpeed = value }); }

    private void SetCamera(ViewportCameraSettings settings)
    {
        settings.Validate();
        if (cameraSettings == settings) return;
        ReceiveCameraSettings(settings);
        if (!destroyed && SceneInitialized) _ = Controller.InvokeAsync(() => Controller.GetCameraService().ApplySettings(settings));
    }

    public void ResetCameraOrientation(CameraOrientation orientation)
    {
        if (!destroyed && SceneInitialized)
            _ = Controller.InvokeAsync(() => Controller.GetCameraService().ResetCamera(orientation));
    }

    private void CameraSettingsChanged(ViewportCameraSettings settings) => _ = Dispatcher.InvokeAsync(() =>
    {
        if (!destroyed) ReceiveCameraSettings(settings);
    });

    private void ReceiveCameraSettings(ViewportCameraSettings settings)
    {
        var previous = cameraSettings;
        cameraSettings = settings;
        if (previous.Orthographic != settings.Orthographic) OnPropertyChanged(nameof(CameraOrthographic));
        if (previous.NearPlane != settings.NearPlane) OnPropertyChanged(nameof(CameraNearPlane));
        if (previous.FarPlane != settings.FarPlane) OnPropertyChanged(nameof(CameraFarPlane));
        if (previous.FieldOfView != settings.FieldOfView) OnPropertyChanged(nameof(CameraFieldOfView));
        if (previous.OrthographicSize != settings.OrthographicSize) OnPropertyChanged(nameof(CameraOrthographicSize));
        if (previous.MoveSpeed != settings.MoveSpeed) OnPropertyChanged(nameof(CameraMoveSpeed));
    }

    public float TranslationSnap { get => translationSnap; set => SetSnap(ref translationSnap, value); }
    public float RotationSnap { get => rotationSnap; set => SetSnap(ref rotationSnap, value); }
    public float ScaleSnap { get => scaleSnap; set => SetSnap(ref scaleSnap, value); }

    private void SetSnap(ref float field, float value, [CallerMemberName] string property = "")
    {
        if (!float.IsFinite(value) || value <= 0) throw new ArgumentOutOfRangeException(property, "Snap must be finite and positive.");
        if (SetValue(ref field, value, property)) UpdateTransformSettings();
    }

    protected override async Task OnGameContentLoaded()
    {
        await ReloadContent();
        OnPropertyChanged(nameof(EditorHost));
        UpdateTransformSettings();
        var initialCamera = await Controller.InvokeAsync(() =>
        {
            cameraService = Controller.GetCameraService();
            cameraService.SettingsChanged += CameraSettingsChanged;
            return cameraService.GetSettings();
        });
        ReceiveCameraSettings(initialCamera);
        await Controller.InvokeAsync(() => Controller.GetRenderModeService().RenderMode = renderMode);
        await Controller.InvokeAsync(() => Controller.GetGridService().IsActive = gridVisible);
        await Controller.InvokeAsync(() => Controller.GetComponentGizmoService().IsActive = gizmosVisible);
    }

    public async Task ReloadContent()
    {
        reloadPending = true;
        await reloadLock.WaitAsync();
        try
        {
            while (reloadPending && !destroyed)
            {
                reloadPending = false;
                await Controller.ReloadContent();
            }
            Owner.RegisterAllPropagators();
        }
        finally { reloadLock.Release(); }
    }

    public override void Destroy()
    {
        destroyed = true;
        if (cameraService != null) cameraService.SettingsChanged -= CameraSettingsChanged;
        Session.AssetPropertiesChanged -= AssetPropertiesChanged;
        base.Destroy();
    }

    public void CommitTransformations(IReadOnlyDictionary<AbsoluteId, TransformationTRS> transformations)
    {
        Dispatcher.EnsureAccess();
        if (destroyed) return;
        using var transaction = Session.ActionService?.CreateTransaction();
        foreach (var (id, transform) in transformations)
        {
            if (Owner.FindPartViewModel(id) is not EntityViewModel entity) continue;
            var node = Session.AssetNodeContainer.GetOrCreateNode(entity.AssetSideEntity.Transform);
            Update(node[nameof(TransformComponent.Position)], transform.Position);
            Update(node[nameof(TransformComponent.Rotation)], transform.Rotation);
            Update(node[nameof(TransformComponent.Scale)], transform.Scale);
        }
        if (transaction != null) Session.ActionService?.SetName(transaction, "Transform entities");
    }

    private static void Update<T>(IMemberNode node, T value)
    {
        if (!Equals(node.Retrieve(), value)) node.Update(value);
    }

    private void UpdateTransformSettings()
    {
        if (destroyed) return;
        var mode = transformation;
        var coordinates = space;
        var selectedPivot = pivot;
        var snapping = snap;
        var steps = (translationSnap, rotationSnap, scaleSnap);
        _ = Controller.InvokeAsync(() => Controller.GetTransformService().SetSettings(mode, coordinates, snapping, steps.translationSnap, steps.rotationSnap, steps.scaleSnap, selectedPivot));
    }

    private async Task FrameSelection()
    {
        var ids = Owner.SelectedItems.OfType<EntityViewModel>().Select(item => item.Id).ToArray();
        await Controller.InvokeAsync(() => Controller.Frame(ids));
    }

    private void AssetPropertiesChanged(object? sender, AssetChangedEventArgs e)
    {
        if (e.Assets.All(a => a == Owner.Asset)) return;

        _ = Dispatcher.InvokeTask(async () =>
        {
            if (destroyed || !SceneInitialized) return;
            try { await ReloadContent(); }
            catch (OperationCanceledException) { }
            catch (Exception exception) { LastException = exception; }
        });
    }
}
