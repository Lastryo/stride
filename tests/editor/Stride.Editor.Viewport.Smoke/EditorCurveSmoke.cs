// Copyright (c) .NET Foundation and Contributors (https://dotnetfoundation.org/ & https://stride3d.net)
// Distributed under the MIT license. See the LICENSE.md file in the project root for more information.
using Avalonia;
using Point = Avalonia.Point;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.VisualTree;
using Stride.Core.Presentation.Quantum;
using Stride.Core.Presentation.Quantum.ViewModels;
using Stride.Core.Quantum;
using Avalonia.Input;
using Avalonia.Media.Imaging;
using Stride.Animations;
using Stride.Assets.Editor.CurveEditor;
using Stride.Assets.Editor.Avalonia.CurveEditor;
using Stride.Assets.Entities;
using Stride.Core.Assets;
using Stride.Core.Assets.Editor.ViewModels;
using Stride.Core.Assets.Serializers;
using Stride.Core.Mathematics;
using Stride.Engine;
using Stride.Particles.Components;
using Stride.Particles;
using Stride.Particles.Updaters;

internal static class EditorCurveSmoke
{
    public static async Task Check(SessionViewModel session, Window owner)
    {
        var curve = new ComputeAnimationCurveFloat();
        curve.KeyFrames.Add(new AnimationKeyFrame<float> { Key = 0, Value = 0 });
        curve.KeyFrames.Add(new AnimationKeyFrame<float> { Key = 1, Value = 1 });
        var component = new ParticleSystemComponent();
        var emitter = new ParticleEmitter();
        emitter.Updaters.Add(new UpdaterSizeOverTime { SamplerMain = { Curve = curve } });
        component.ParticleSystem.Emitters.Add(emitter);
        var entity = new Entity("Curve fixture"); entity.Components.Add(component);
        var scene = new SceneAsset(); scene.Hierarchy.Parts.Add(new EntityDesign(entity)); scene.Hierarchy.RootParts.Add(entity);
        using (session.ActionService!.CreateTransaction()) session.CurrentProject!.AddAsset(new AssetItem("CurveSmoke", scene), session.CurrentProject.AssetMountPoint);
        var graph = await session.ActiveProperties.GenerateSelectionPropertiesAsync([new CurveProvider(session.AssetNodeContainer.GetOrCreateNode(emitter.Updaters[0]))]) ?? throw new Exception("Missing curve inspector graph");
        await Task.Delay(250);
        var curveNode = Nodes(graph.RootNode).First(n => ReferenceEquals(n.NodeValue, curve));
        var command = curveNode.Commands.Single(c => c.Name == "EditCurve");
        Require(command.CanExecute(null), "Inspector curve command is disabled");
        for (var i = 0; i < 4; i++)
        {
            foreach (var row in owner.GetVisualDescendants().OfType<Stride.Core.Presentation.Avalonia.Controls.PropertyViewItem>().ToArray()) row.IsExpanded = true;
            await Task.Delay(100);
        }
        var button = owner.GetVisualDescendants().OfType<Button>().FirstOrDefault(b => ReferenceEquals(b.Command, command));
        Require(button != null, "No Edit curve button in the inspector");
        command.Execute(null);
        await Task.Delay(100);
        var window = ((IClassicDesktopStyleApplicationLifetime)Application.Current!.ApplicationLifetime!).Windows.Single(w => w.Content is CurveEditorView);
        var view = (CurveEditorView)window.Content!;
        var model = (CurveEditorViewModel)view.DataContext!;
        try
        {
            await Task.Delay(300); view.Canvas.Fit();
            Require(curve.KeyFrames.Count == 2 && model.SelectedChannel?.Editable == true, "Initialization modified keys");
            var key = model.AddPoint(.5, .8)!;
            Require(curve.KeyFrames.Count == 3 && ReferenceEquals(curve.KeyFrames[1], key), "Sorted insertion");
            session.ActionService.Undo(); Require(curve.KeyFrames.Count == 2, "Undo insertion");
            session.ActionService.Redo(); Require(curve.KeyFrames.Count == 3, "Redo insertion");
            model.MovePoints(new Dictionary<object, (double, double)> { [key] = (.25, .7) });
            Require(Math.Abs(((AnimationKeyFrame<float>)key).Value - .7f) < .0001, "Quantum key move");
            session.ActionService.Undo(); Require(Math.Abs(((AnimationKeyFrame<float>)key).Value - .8f) < .0001, "Undo move");
            session.ActionService.Redo();
            model.SelectedKeys.Clear(); model.SelectedKeys.Add(key); model.DeleteSelected();
            Require(curve.KeyFrames.Count == 2, "Delete selection"); session.ActionService.Undo();
            // Routed Alt-click goes through the actual canvas coordinate transform.
            var point = view.Canvas.ToScreen(.6, .4);
            var pointer = new Pointer(91, PointerType.Mouse, true);
            view.Canvas.RaiseEvent(new PointerPressedEventArgs(view.Canvas, pointer, view.Canvas, point, 1,
                new PointerPointProperties(RawInputModifiers.LeftMouseButton, PointerUpdateKind.LeftButtonPressed), KeyModifiers.Alt));
            Require(curve.KeyFrames.Count == 4, "Alt-click did not add a point"); session.ActionService.Undo();
            view.Canvas.Fit();
            var beforeKey = ((AnimationKeyFrame<float>)key).Key;
            var beforeValue = ((AnimationKeyFrame<float>)key).Value;
            var from = view.Canvas.ToScreen(beforeKey, beforeValue); var to = new Point(from.X + 25, from.Y - 20);
            view.Canvas.RaiseEvent(new PointerPressedEventArgs(view.Canvas, pointer, view.Canvas, from, 0,
                new PointerPointProperties(RawInputModifiers.LeftMouseButton, PointerUpdateKind.LeftButtonPressed), KeyModifiers.None));
            view.Canvas.RaiseEvent(new PointerEventArgs(InputElement.PointerMovedEvent, view.Canvas, pointer, view.Canvas, to, 0,
                new PointerPointProperties(RawInputModifiers.LeftMouseButton, PointerUpdateKind.LeftButtonPressed), KeyModifiers.None));
            Require(((AnimationKeyFrame<float>)key).Key == beforeKey, "Drag mutated asset before release");
            view.Canvas.RaiseEvent(new PointerReleasedEventArgs(view.Canvas, pointer, view.Canvas, to, 0,
                new PointerPointProperties(RawInputModifiers.None, PointerUpdateKind.LeftButtonReleased), KeyModifiers.None, MouseButton.Left));
            Require(((AnimationKeyFrame<float>)key).Key > beforeKey && ((AnimationKeyFrame<float>)key).Value > beforeValue, "Routed drag did not commit");
            session.ActionService.Undo(); Require(((AnimationKeyFrame<float>)key).Key == beforeKey, "Gesture Undo");
            model.Clear(); Require(curve.KeyFrames.Count == 0, "Clear"); session.ActionService.Undo(); Require(curve.KeyFrames.Count == 3, "Undo clear");
            using (var stream = new MemoryStream())
            {
                AssetFileSerializer.Save(stream, scene, null); stream.Position = 0;
                var loaded = AssetFileSerializer.Load<SceneAsset>(stream, "Curve.sdscene").Asset;
                var restored = (ComputeAnimationCurveFloat)((UpdaterSizeOverTime)loaded.Hierarchy.RootParts[0].Get<ParticleSystemComponent>().ParticleSystem.Emitters[0].Updaters[0]).SamplerMain.Curve;
                Require(restored.KeyFrames.Count == 3 && restored.KeyFrames.Any(k => Math.Abs(k.Key - .25f) < .0001 && Math.Abs(k.Value - .7f) < .0001), "Scene curve serialization");
            }
            foreach (var sample in new object[] { new ComputeAnimationCurveVector2(), new ComputeAnimationCurveVector3(), new ComputeAnimationCurveVector4(), new ComputeAnimationCurveColor4(), new ComputeAnimationCurveQuaternion() })
            {
                using var disposable = new ModelScope(new CurveEditorViewModel(session, sample, "Components"));
                var other = disposable.Model;
                Require(other.Channels.Count >= 2, "Missing vector channels");
                foreach (var channel in other.Channels.ToArray())
                {
                    other.SelectedChannel = channel; var added = other.AddPoint(.3, .45)!;
                    Require(Math.Abs(channel.Value(added) - .45) < .0001, "Component value editing");
                }
            }
            using (var rotation = new ModelScope(new CurveEditorViewModel(session, new ComputeConstCurveQuaternion { Value = Quaternion.RotationYawPitchRoll(.2f, .3f, .1f) }, "Rotation")))
            {
                rotation.Model.RotationDisplay = RotationDisplayMode.Euler;
                Require(rotation.Model.Channels.Count == 3 && rotation.Model.Channels.All(c => double.IsFinite(c.Evaluate(.5f))), "Euler preview");
                rotation.Model.RotationDisplay = RotationDisplayMode.AxisAngle;
                Require(rotation.Model.Channels.Count == 4 && rotation.Model.Channels.All(c => double.IsFinite(c.Evaluate(.5f))), "Axis/angle preview");
            }
            await Task.Delay(200); view.UpdateLayout();
            Require(view.GetVisualDescendants().OfType<TextBlock>().Any(t => t.Text == model.SelectedChannel!.Name), "Channel name is not visible");
            using (var bitmap = new RenderTargetBitmap(new PixelSize((int)view.Bounds.Width, (int)view.Bounds.Height))) { bitmap.Render(view); bitmap.Save("/tmp/stride-curve-editor.png"); }
            Console.WriteLine("PASS: Curve Editor scalar/vector/color/quaternion channels, Quantum insertion/move/delete, Undo/Redo, routed Alt-click, serialization and Avalonia rendering.");
        }
        finally { window.Close(); }
    }
    private static IEnumerable<NodeViewModel> Nodes(NodeViewModel node) => new[] { node }.Concat(node.Children.SelectMany(Nodes));
    private sealed class CurveProvider(IObjectNode root) : IPropertyProviderViewModel
    {
        public bool CanProvidePropertiesViewModel => true;
        public IObjectNode GetRootNode() => root;
        public bool ShouldConstructMember(IMemberNode member) => true;
        public bool ShouldConstructItem(IObjectNode collection, NodeIndex index) => true;
    }
    private sealed class ModelScope(CurveEditorViewModel model) : IDisposable { public CurveEditorViewModel Model => model; public void Dispose() => model.Destroy(); }
    private static void Require(bool condition, string message) { if (!condition) throw new Exception("Curve editor: " + message); }
}
