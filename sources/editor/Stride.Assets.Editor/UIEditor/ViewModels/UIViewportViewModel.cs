// Copyright (c) .NET Foundation and Contributors (https://dotnetfoundation.org/ & https://stride3d.net) and Silicon Studio Corp. (https://www.siliconstudio.co.jp)
// Distributed under the MIT license. See the LICENSE.md file in the project root for more information.

using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Stride.Assets.Editor.GameEditor;
using Stride.Assets.Editor.GameEditor.Services;
using Stride.Assets.Editor.UIEditor.Game;
using Stride.Assets.Editor.UIEditor.Services;
using Stride.Assets.Editor.ViewModels;
using Stride.Assets.Presentation.ViewModels;
using Stride.Core.Assets;
using Stride.Core.Mathematics;
using Stride.Core.Extensions;
using Stride.Core.Diagnostics;
using Stride.Core.Presentation.Commands;
using Stride.Engine;

namespace Stride.Assets.Editor.UIEditor.ViewModels;

public sealed class UIViewportViewModel : GameEditorViewModel
{
    private Color guidelineColor = Color.Red;
    private float guidelineThickness = 2.0f;
    private Color highlightColor = Color.Pink;
    private float highlightThickness = 2.0f;
    private Color selectionColor = Color.LimeGreen;
    private float selectionThickness = 2.0f;
    private Color sizingColor = Color.Cyan;
    private float sizingThickness = 2.0f;
    private float snapValue = 1.0f;

    public UIViewportViewModel(UIEditorBaseViewModel owner, Func<GameEditorViewModel, IEditorGameController> controllerFactory)
        : base(owner.Asset, controllerFactory)
    {
        Owner = owner;

        ResetZoomCommand = new AnonymousCommand(ServiceProvider, ResetZoom);
        ZoomInCommand = new AnonymousCommand(ServiceProvider, () => Zoom(-1));
        ZoomOutCommand = new AnonymousCommand(ServiceProvider, () => Zoom(+1));
    }

    public UIEditorBaseViewModel Owner { get; }
    public new UIEditorController Controller => (UIEditorController)base.Controller;
    public object? EditorHost => Controller.EditorHost;

    protected override async Task OnGameContentLoaded()
    {
        OnPropertyChanged(nameof(EditorHost));
        var saved = Asset.Directory.Package.Package.UserSettings.GetValue(UIEditorCameraSettings.Cameras);
        if (saved != null && saved.TryGetValue(Asset.Id, out var camera) && camera != null)
            await Controller.InvokeAsync(() => Controller.CameraService.ApplySettings(camera));
        Owner.RegisterAllPropagators();
        await base.OnGameContentLoaded();
    }

    public ICommandBase ResetZoomCommand { get; }
    public ICommandBase ZoomInCommand { get; }
    public ICommandBase ZoomOutCommand { get; }

    public Color GuidelineColor
    {
        get => guidelineColor;
        set { if (SetValue(ref guidelineColor, value)) RefreshAdorners(); }
    }

    public float GuidelineThickness
    {
        get => guidelineThickness;
        set { if (SetValue(ref guidelineThickness, value)) RefreshAdorners(); }
    }

    public Color HighlightColor
    {
        get => highlightColor;
        set { if (SetValue(ref highlightColor, value)) RefreshAdorners(); }
    }

    public float HighlightThickness
    {
        get => highlightThickness;
        set { if (SetValue(ref highlightThickness, value)) RefreshAdorners(); }
    }

    public Color SelectionColor
    {
        get => selectionColor;
        set { if (SetValue(ref selectionColor, value)) RefreshAdorners(); }
    }

    public float SelectionThickness
    {
        get => selectionThickness;
        set { if (SetValue(ref selectionThickness, value)) RefreshAdorners(); }
    }

    public Color SizingColor
    {
        get => sizingColor;
        set { if (SetValue(ref sizingColor, value)) RefreshAdorners(); }
    }

    public float SizingThickness
    {
        get => sizingThickness;
        set { if (SetValue(ref sizingThickness, value)) RefreshAdorners(); }
    }

    public float SnapValue
    {
        get => snapValue;
        set => SetValue(ref snapValue, value);
    }

    private void RefreshAdorners()
    {
        if (SceneInitialized) Controller.AdornerService.Refresh().Forget();
    }

    /// <inheritdoc />
    public override void Destroy()
    {
        if (SceneInitialized && Asset.Directory.Package.IsEditable)
        {
            try
            {
                var profile = Asset.Directory.Package.Package.UserSettings;
                var cameras = new Dictionary<AssetId, UIEditorCameraSettings>(
                    profile.GetValue(UIEditorCameraSettings.Cameras) ?? []);
                cameras[Asset.Id] = Controller.CameraService.Settings;
                profile.SetValue(UIEditorCameraSettings.Cameras, cameras);
                if (!profile.Save()) Controller.Logger.Warning("Could not save UI camera settings.");
            }
            catch (Exception exception) { Controller.Logger.Warning("Could not save UI camera settings.", exception); }
        }
        base.Destroy();
    }

    public void ResetZoom()
    {
        if (SceneInitialized) Controller.InvokeAsync(() => Controller.CameraService.ResetZoom()).Forget();
    }

    public void Zoom(float amount)
    {
        if (!SceneInitialized) return;
        Controller.InvokeAsync(() =>
        {
            if (amount < 0) Controller.CameraService.ZoomIn();
            else Controller.CameraService.ZoomOut();
        }).Forget();
    }
}
