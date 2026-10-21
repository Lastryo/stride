// Copyright (c) .NET Foundation and Contributors (https://dotnetfoundation.org/ & https://stride3d.net)
// Distributed under the MIT license. See the LICENSE.md file in the project root for more information.

using System;
using System.Threading.Tasks;
using Stride.Assets.Editor.EntityHierarchyEditor.Game;
using Stride.Assets.Editor.GameEditor;
using Stride.Assets.Editor.GameEditor.Game;
using Stride.Core.Mathematics;
using Stride.Editor.EditorGame.Game;
using Stride.Engine;
using Stride.Engine.Processors;
using Stride.Input;
using Stride.Rendering.Compositing;

namespace Stride.Assets.Editor.UIEditor.Game;

public sealed class UIEditorGameCameraService : EditorGameMouseServiceBase, IEditorGameCameraService
{
    public static readonly Vector3 DefaultPosition = new(0, 0, 500);
    public static readonly float DefaultPitch = 0.0f;
    public static readonly float DefaultYaw = 0.0f;
    public const float DefaultOrthographicSize = CameraComponent.DefaultOrthographicSize;

    private EntityHierarchyEditorGame game = null!;
    private float desiredYaw;
    private float desiredPitch;
    private float yaw;
    private float pitch;
    private Vector3 position = DefaultPosition;
    private Vector2 panStartMousePosition;
    private Vector3 panStartPosition;
    private bool isPanning;
    private UIEditorCameraSettings settings = new();

    /// <summary>A camera snapshot published by the game thread for saving on close.</summary>
    public UIEditorCameraSettings Settings => System.Threading.Volatile.Read(ref settings);

    /// <summary>Restores valid position and zoom on the game thread.</summary>
    /// <param name="value">The saved camera state.</param>
    public void ApplySettings(UIEditorCameraSettings value)
    {
        if (!float.IsFinite(value.Position.X) || !float.IsFinite(value.Position.Y) || !float.IsFinite(value.Position.Z)
            || !float.IsFinite(value.OrthographicSize) || value.OrthographicSize < .05f || value.OrthographicSize > 50000) return;
        position = value.Position;
        Component.OrthographicSize = value.OrthographicSize;
        UpdateViewMatrix();
    }

    public CameraComponent Component { get; private set; } = null!;
    public Matrix ViewMatrix => Component.ViewMatrix;
    public Matrix ProjectionMatrix { get { Component.Update(); return Component.ProjectionMatrix; } }
    public Vector3 Position => position;
    public bool IsMoving => isPanning;
    public override bool IsControllingMouse { get; protected set; }

    public bool IsOrthographic
    {
        get => Component.Projection == CameraProjectionMode.Orthographic;
        set => Component.Projection = value ? CameraProjectionMode.Orthographic : CameraProjectionMode.Perspective;
    }

    public float AspectRatio => Component.AspectRatio;
    public float VerticalFieldOfView => Component.VerticalFieldOfView;
    public float NearPlane => Component.NearClipPlane;
    public float FarPlane => Component.FarClipPlane;
    public float SceneUnit => 1.0f;
    public float MoveSpeed { get; set; } = 3.0f;

    public override bool IsActive
    {
        get => base.IsActive;
        set
        {
            base.IsActive = value;
            if (!value && isPanning)
            {
                isPanning = false;
                IsControllingMouse = false;
                game.Input.UnlockMousePosition();
                game.IsMouseVisible = true;
            }
        }
    }

    protected override Task<bool> Initialize(EditorServiceGame editorGame)
    {
        game = (EntityHierarchyEditorGame)editorGame;
        Component = new CameraComponent
        {
            UseCustomViewMatrix = true,
            UseCustomAspectRatio = true,
            Projection = CameraProjectionMode.Orthographic,
            NearClipPlane = 0.1f,
            FarClipPlane = 10000.0f,
            OrthographicSize = DefaultOrthographicSize,
        };

        var cameraEntity = new Entity("UI Editor camera") { Component };
        game.EditorScene.Entities.Add(cameraEntity);

        if (game.EditorSceneSystem.GraphicsCompositor?.Game is EditorTopLevelCompositor topLevel)
        {
            topLevel.ExternalCamera = Component;
        }

        if (game.SceneSystem.GraphicsCompositor?.Game is EditorTopLevelCompositor sceneTopLevel)
        {
            sceneTopLevel.ExternalCamera = Component;
        }

        Reset();
        game.Script.AddTask(UpdateCameraLoop);
        return Task.FromResult(true);
    }

    public override void UpdateGraphicsCompositor(EditorServiceGame editorGame)
    {
        if (editorGame.SceneSystem.GraphicsCompositor?.Game is EditorTopLevelCompositor topLevel)
        {
            topLevel.ExternalCamera = Component;
        }
    }

    public void Reset()
    {
        position = DefaultPosition;
        yaw = DefaultYaw;
        pitch = DefaultPitch;
        desiredYaw = DefaultYaw;
        desiredPitch = DefaultPitch;
        if (Component != null)
        {
            Component.OrthographicSize = DefaultOrthographicSize;
        }
        UpdateViewMatrix();
    }

    public void ResetCamera(Vector3 viewDirection)
    {
        Reset();
    }

    public void ResetCamera(CameraOrientation orientation)
    {
        Reset();
    }

    public void Zoom(float factor)
    {
        if (Component == null) return;
        var newSize = Component.OrthographicSize * factor;
        if (newSize > 0.05f && newSize < 50000.0f)
        {
            Component.OrthographicSize = newSize;
            UpdateViewMatrix();
        }
    }

    public void ZoomIn() => Zoom(0.8f);

    public void ZoomOut() => Zoom(1.25f);

    public void ResetZoom(float targetHeight = DefaultOrthographicSize)
    {
        if (Component == null) return;
        Component.OrthographicSize = Math.Max(targetHeight, 0.1f);
        position = DefaultPosition;
        UpdateViewMatrix();
    }

    private async Task UpdateCameraLoop()
    {
        while (game.IsRunning)
        {
            UpdateCamera();
            await game.Script.NextFrame();
        }
    }

    private void UpdateCamera()
    {
        if (!IsActive || game.Input == null) return;

        // Middle mouse button or Alt+LeftButton pan
        var panRequested = game.Input.IsMouseButtonDown(MouseButton.Middle) ||
                           (game.Input.IsKeyDown(Keys.LeftAlt) && game.Input.IsMouseButtonDown(MouseButton.Left));

        if (panRequested)
        {
            if (!isPanning)
            {
                isPanning = true;
                IsControllingMouse = true;
                panStartMousePosition = game.Input.MousePosition;
                panStartPosition = position;
            }
            else
            {
                var delta = game.Input.MouseDelta;
                // Move position proportionally to orthographic size and screen aspect
                var speedFactor = Component.OrthographicSize;
                position.X -= delta.X * speedFactor * Component.AspectRatio;
                position.Y += delta.Y * speedFactor;
                UpdateViewMatrix();
            }
        }
        else if (isPanning)
        {
            isPanning = false;
            IsControllingMouse = false;
        }

        // Mouse wheel zoom
        if (Math.Abs(game.Input.MouseWheelDelta) > MathUtil.ZeroTolerance)
        {
            var zoomMultiplier = game.Input.MouseWheelDelta > 0 ? 0.9f : 1.111f;
            Zoom(zoomMultiplier);
        }
    }

    private void UpdateViewMatrix()
    {
        if (Component == null) return;

        if (game?.GraphicsDevice?.Presenter?.BackBuffer is { } buffer)
            Component.AspectRatio = buffer.Width / (float)Math.Max(buffer.Height, 1);

        Component.ViewMatrix = Matrix.Translation(-position) * Matrix.RotationQuaternion(Quaternion.Invert(Quaternion.RotationYawPitchRoll(yaw, pitch, 0)));
        Component.Update();
        System.Threading.Volatile.Write(ref settings, new UIEditorCameraSettings { Position = position, OrthographicSize = Component.OrthographicSize });
    }
}

