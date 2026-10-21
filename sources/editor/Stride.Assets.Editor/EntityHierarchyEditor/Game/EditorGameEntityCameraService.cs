// Copyright (c) .NET Foundation and Contributors (https://dotnetfoundation.org/ & https://stride3d.net)
// Distributed under the MIT license. See the LICENSE.md file in the project root for more information.

using Stride.Assets.Editor.GameEditor;
using Stride.Assets.Editor.GameEditor.Game;
using Stride.Core.Extensions;
using Stride.Core.Mathematics;
using Stride.Editor.EditorGame.Game;
using Stride.Editor.Engine;
using Stride.Engine;
using Stride.Engine.Processors;
using Stride.Input;
using Stride.Rendering.Compositing;

namespace Stride.Assets.Editor.EntityHierarchyEditor.Game;

public sealed class EditorGameEntityCameraService : EditorGameMouseServiceBase, IEditorGameCameraService
{
    private static readonly float[] MovementSpeeds = [.05f, .1f, .5f, 1, 1.5f, 2.5f, 5, 10, 15, 30, 45, 60, 80, 100, 130, 180, 250, 400, 500, 700, 1000];
    private SceneViewportGame game = null!;
    private float yaw = MathUtil.PiOverFour;
    private float pitch = -MathUtil.Pi / 8;
    public CameraComponent Component { get; private set; } = null!;
    public Matrix ViewMatrix => Component.ViewMatrix;
    public Matrix ProjectionMatrix { get { Component.Update(); return Component.ProjectionMatrix; } }
    public Vector3 Position { get; private set; } = new(4, 3, 6);
    public bool IsMoving => IsControllingMouse;
    public override bool IsControllingMouse { get; protected set; }
    public override bool IsActive
    {
        get => base.IsActive;
        set
        {
            base.IsActive = value;
            if (!value && IsControllingMouse)
            {
                IsControllingMouse = false;
                game.Input.UnlockMousePosition();
                game.IsMouseVisible = true;
            }
        }
    }
    public bool IsOrthographic { get => Component.Projection == CameraProjectionMode.Orthographic; set => Component.Projection = value ? CameraProjectionMode.Orthographic : CameraProjectionMode.Perspective; }
    public float AspectRatio => Component.AspectRatio;
    public float VerticalFieldOfView => Component.VerticalFieldOfView;
    public float NearPlane => Component.NearClipPlane;
    public float FarPlane => Component.FarClipPlane;
    public float SceneUnit => 1;
    public float MoveSpeed { get; private set; } = 3;
    private ViewportCameraSettings publishedSettings;
    public event Action<ViewportCameraSettings>? SettingsChanged;

    public ViewportCameraSettings GetSettings() => new(IsOrthographic, NearPlane, FarPlane, VerticalFieldOfView, Component.OrthographicSize, MoveSpeed);

    public void ApplySettings(ViewportCameraSettings settings)
    {
        settings.Validate();
        IsOrthographic = settings.Orthographic;
        Component.NearClipPlane = settings.NearPlane;
        Component.FarClipPlane = settings.FarPlane;
        Component.VerticalFieldOfView = settings.FieldOfView;
        Component.OrthographicSize = settings.OrthographicSize;
        MoveSpeed = settings.MoveSpeed;
        Component.Update();
    }

    protected override Task<bool> Initialize(EditorServiceGame editorGame)
    {
        game = (SceneViewportGame)editorGame;
        Component = new CameraComponent { UseCustomViewMatrix = true, UseCustomAspectRatio = true, FarClipPlane = 10000 };
        game.EditorScene.Entities.Add(new Entity("Editor camera") { Component });
        ((EditorTopLevelCompositor)game.EditorSceneSystem.GraphicsCompositor.Game).ExternalCamera = Component;
        game.Script.AddTask(Update);
        UpdateView();
        return Task.FromResult(true);
    }

    public override void UpdateGraphicsCompositor(EditorServiceGame editorGame)
    {
        ((EditorTopLevelCompositor)editorGame.SceneSystem.GraphicsCompositor.Game).ExternalCamera = Component;
    }

    public void Frame(Scene scene) => Frame(scene.Yield().BreadthFirst(item => item.Children).SelectMany(item => item.Entities));

    public void Frame(IEnumerable<Entity> entities)
    {
        var spheres = entities.Select(entity => entity.CalculateBoundSphere()).ToArray();
        if (spheres.Length == 0) return;
        var center = spheres.Aggregate(Vector3.Zero, (sum, sphere) => sum + sphere.Center) / spheres.Length;
        var radius = spheres.Max(sphere => Vector3.Distance(sphere.Center, center) + sphere.Radius);
        var direction = Vector3.TransformNormal(-Vector3.UnitZ, Matrix.RotationYawPitchRoll(yaw, pitch, 0));
        Position = center - direction * Math.Max(radius * 3, 2);
        MoveSpeed = Math.Clamp(Math.Max(radius, 1) * 2, MovementSpeeds[0], MovementSpeeds[^1]);
        UpdateView();
    }

    public void ResetCamera(Vector3 direction)
    {
        yaw = MathF.Atan2(-direction.X, -direction.Z);
        pitch = MathF.Atan2(direction.Y, new Vector2(direction.X, direction.Z).Length());
        UpdateView();
    }

    public void ResetCamera(CameraOrientation orientation) => ResetCamera(orientation switch
    {
        CameraOrientation.Front => Vector3.UnitZ, CameraOrientation.Back => -Vector3.UnitZ,
        CameraOrientation.Top => -Vector3.UnitY, CameraOrientation.Bottom => Vector3.UnitY,
        CameraOrientation.Left => Vector3.UnitX, _ => -Vector3.UnitX
    });

    private async Task Update()
    {
        while (!IsDisposed)
        {
            if (IsActive)
            {
                var input = game.Input;
                var right = input.IsMouseButtonDown(MouseButton.Right);
                var middle = input.IsMouseButtonDown(MouseButton.Middle);
                var controlling = (right || middle) && IsMouseAvailable && game.IsActive;
                if (controlling != IsControllingMouse)
                {
                    IsControllingMouse = controlling;
                    if (controlling) input.LockMousePosition(); else input.UnlockMousePosition();
                    game.IsMouseVisible = !controlling;
                }
                var rotation = Matrix.RotationYawPitchRoll(yaw, pitch, 0);
                if (IsControllingMouse)
                {
                    if (right)
                    {
                        if (input.MouseWheelDelta > 0)
                            MoveSpeed = MovementSpeeds.FirstOrDefault(speed => speed > MoveSpeed, MovementSpeeds[^1]);
                        else if (input.MouseWheelDelta < 0)
                            MoveSpeed = MovementSpeeds.LastOrDefault(speed => speed < MoveSpeed, MovementSpeeds[0]);
                        yaw -= input.MouseDelta.X * MathUtil.TwoPi;
                        pitch = MathUtil.Clamp(pitch - input.MouseDelta.Y * MathUtil.TwoPi, -1.55f, 1.55f);
                        var movement = new Vector3((input.IsKeyDown(Keys.D) ? 1 : 0) - (input.IsKeyDown(Keys.A) ? 1 : 0),
                            (input.IsKeyDown(Keys.E) ? 1 : 0) - (input.IsKeyDown(Keys.Q) ? 1 : 0),
                            (input.IsKeyDown(Keys.S) ? 1 : 0) - (input.IsKeyDown(Keys.W) ? 1 : 0));
                        if (movement.LengthSquared() > 0) movement.Normalize();
                        Position += Vector3.TransformNormal(movement, rotation) * MoveSpeed * (float)game.UpdateTime.Elapsed.TotalSeconds
                            * (input.IsKeyDown(Keys.LeftShift) ? 4 : 1);
                    }
                    else Position += Vector3.TransformNormal(new Vector3(-input.MouseDelta.X, input.MouseDelta.Y, 0), rotation) * MoveSpeed;
                }
                else if (IsMouseAvailable && game.IsActive && float.IsFinite(input.MouseWheelDelta) && input.MouseWheelDelta != 0)
                {
                    if (IsOrthographic)
                    {
                        var step = MoveSpeed * .2f * (input.IsKeyDown(Keys.LeftShift) || input.IsKeyDown(Keys.RightShift) ? 10 : 1);
                        Component.OrthographicSize = Math.Clamp(Component.OrthographicSize - input.MouseWheelDelta * step, .001f, 1_000_000f);
                    }
                    else
                        Position += Vector3.TransformNormal(-Vector3.UnitZ, rotation) * input.MouseWheelDelta * 1.2f;
                }
                UpdateView();
                var settings = GetSettings();
                if (settings != publishedSettings)
                {
                    publishedSettings = settings;
                    SettingsChanged?.Invoke(settings);
                }
            }
            await game.Script.NextFrame();
        }
    }

    private void UpdateView()
    {
        if (game.GraphicsDevice.Presenter?.BackBuffer is { } buffer)
            Component.AspectRatio = buffer.Width / (float)Math.Max(buffer.Height, 1);
        Component.ViewMatrix = Matrix.Translation(-Position) * Matrix.RotationQuaternion(Quaternion.Invert(Quaternion.RotationYawPitchRoll(yaw, pitch, 0)));
    }

    public override ValueTask DisposeAsync()
    {
        if (IsControllingMouse) game.Input.UnlockMousePosition();
        game.IsMouseVisible = true;
        return base.DisposeAsync();
    }
}
