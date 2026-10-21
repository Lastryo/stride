// Copyright (c) .NET Foundation and Contributors (https://dotnetfoundation.org/ & https://stride3d.net)
// Distributed under the MIT license. See the LICENSE.md file in the project root for more information.

namespace Stride.Assets.Editor.GameEditor;

public readonly record struct ViewportCameraSettings(bool Orthographic, float NearPlane, float FarPlane, float FieldOfView, float OrthographicSize, float MoveSpeed)
{
    public static readonly ViewportCameraSettings Default = new(false, .1f, 10000, 45, 10, 3);

    public void Validate()
    {
        if (!float.IsFinite(NearPlane) || !float.IsFinite(FarPlane) || NearPlane <= 0 || FarPlane <= NearPlane || FarPlane > 1_000_000
            || !float.IsFinite(FieldOfView) || FieldOfView < 1 || FieldOfView > 179
            || !float.IsFinite(OrthographicSize) || OrthographicSize < .001f || OrthographicSize > 1_000_000
            || !float.IsFinite(MoveSpeed) || MoveSpeed < .05f || MoveSpeed > 1000)
            throw new ArgumentOutOfRangeException(nameof(ViewportCameraSettings), "Camera requires 0 < near < far ≤ 1000000, FOV 1–179°, positive bounded size and speed 0.05–1000.");
    }
}
