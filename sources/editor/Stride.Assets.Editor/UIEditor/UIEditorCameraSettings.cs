// Copyright (c) .NET Foundation and Contributors (https://dotnetfoundation.org/ & https://stride3d.net)
// Distributed under the MIT license. See the LICENSE.md file in the project root for more information.

using Stride.Core;
using Stride.Core.Assets;
using Stride.Core.Mathematics;
using Stride.Core.Settings;

namespace Stride.Assets.Editor.UIEditor;

/// <summary>Per-asset UI camera settings in the package's user profile.</summary>
[DataContract(nameof(UIEditorCameraSettings))]
public sealed class UIEditorCameraSettings
{
    /// <summary>The camera position in UI scene coordinates.</summary>
    public Vector3 Position { get; set; } = new(0, 0, 500);
    /// <summary>The orthographic height of the visible area.</summary>
    public float OrthographicSize { get; set; } = Stride.Engine.CameraComponent.DefaultOrthographicSize;

    /// <summary>The package key, kept separate from WPF scene settings.</summary>
    public static readonly SettingsKey<Dictionary<AssetId, UIEditorCameraSettings>> Cameras = new(
        "UIEditor/Cameras", PackageUserSettings.SettingsContainer, () => new Dictionary<AssetId, UIEditorCameraSettings>());
}
