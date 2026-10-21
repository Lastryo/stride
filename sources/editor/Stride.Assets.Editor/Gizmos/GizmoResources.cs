// Copyright (c) .NET Foundation and Contributors (https://dotnetfoundation.org/ & https://stride3d.net)
// Distributed under the MIT license. See the LICENSE.md file in the project root for more information.

namespace Stride.Assets.Editor.Gizmos;

internal static class GizmoResources
{
    public static readonly byte[] AmbientLightGizmo = Load(nameof(AmbientLightGizmo));
    public static readonly byte[] AudioEmitterGizmo = Load(nameof(AudioEmitterGizmo));
    public static readonly byte[] AudioListenerGizmo = Load(nameof(AudioListenerGizmo));
    public static readonly byte[] BackgroundGizmo = Load(nameof(BackgroundGizmo));
    public static readonly byte[] CameraGizmo = Load(nameof(CameraGizmo));
    public static readonly byte[] FallbackGizmo = Load(nameof(FallbackGizmo));
    public static readonly byte[] NodeLinkGizmo = Load(nameof(NodeLinkGizmo));
    public static readonly byte[] PointLightGizmo = Load(nameof(PointLightGizmo));
    public static readonly byte[] ScriptGizmo = Load(nameof(ScriptGizmo));
    public static readonly byte[] SkyboxLightGizmo = Load(nameof(SkyboxLightGizmo));
    public static readonly byte[] SpotLightGizmo = Load(nameof(SpotLightGizmo));
    public static readonly byte[] SunLightGizmo = Load(nameof(SunLightGizmo));
    public static readonly byte[] UIGizmo = Load(nameof(UIGizmo));

    private static byte[] Load(string name)
    {
        using var stream = typeof(GizmoResources).Assembly.GetManifestResourceStream($"Stride.Assets.Editor.Resources.Gizmos.{name}.png")
            ?? throw new InvalidOperationException($"Missing gizmo icon {name}.");
        using var data = new MemoryStream();
        stream.CopyTo(data);
        return data.ToArray();
    }
}
