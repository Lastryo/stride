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

namespace Stride.Assets.Editor.ViewModels;

public sealed class SceneViewportViewModel : GameEditorViewModel
{
    private readonly SemaphoreSlim reloadLock = new(1, 1);
    private bool destroyed;
    private bool reloadPending;
    private Transformation transformation;
    private TransformationSpace space;
    private bool snap;

    public SceneViewportViewModel(EntityHierarchyEditorViewModel owner)
        : base(owner.Asset, model => new SceneViewportController(owner.Asset, (SceneViewportViewModel)model))
    {
        Owner = owner;
        TranslateCommand = new AnonymousCommand(ServiceProvider, () => ActiveTransformation = Transformation.Translation);
        RotateCommand = new AnonymousCommand(ServiceProvider, () => ActiveTransformation = Transformation.Rotation);
        ScaleCommand = new AnonymousCommand(ServiceProvider, () => ActiveTransformation = Transformation.Scale);
        FrameSelectionCommand = new AnonymousTaskCommand(ServiceProvider, FrameSelection);
        Session.AssetPropertiesChanged += AssetPropertiesChanged;
    }

    public EntityHierarchyEditorViewModel Owner { get; }
    public new SceneViewportController Controller => (SceneViewportController)base.Controller;
    public object? EditorHost => Controller.EditorHost;
    public ICommandBase TranslateCommand { get; }
    public ICommandBase RotateCommand { get; }
    public ICommandBase ScaleCommand { get; }
    public ICommandBase FrameSelectionCommand { get; }
    public TransformationSpace[] Spaces { get; } = Enum.GetValues<TransformationSpace>();

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

    protected override async Task OnGameContentLoaded()
    {
        await ReloadContent();
        OnPropertyChanged(nameof(EditorHost));
        UpdateTransformSettings();
    }

    public async Task ReloadContent()
    {
        reloadPending = true;
        if (!await reloadLock.WaitAsync(0))
            return;
        try
        {
            while (reloadPending && !destroyed)
            {
                reloadPending = false;
                await Controller.ReloadContent();
            }
        }
        finally { reloadLock.Release(); }
    }

    public override void Destroy()
    {
        destroyed = true;
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
        var snapping = snap;
        _ = Controller.InvokeAsync(() => Controller.GetTransformService().SetSettings(mode, coordinates, snapping));
    }

    private async Task FrameSelection()
    {
        var ids = Owner.SelectedItems.OfType<EntityViewModel>().Select(item => item.Id).ToArray();
        await Controller.InvokeAsync(() => Controller.Frame(ids));
    }

    private void AssetPropertiesChanged(object? sender, AssetChangedEventArgs e)
    {
        _ = Dispatcher.InvokeTask(async () =>
        {
            if (destroyed || !SceneInitialized) return;
            try { await ReloadContent(); }
            catch (OperationCanceledException) { }
            catch (Exception exception) { LastException = exception; }
        });
    }
}
