// Copyright (c) .NET Foundation and Contributors (https://dotnetfoundation.org/ & https://stride3d.net)
// Distributed under the MIT license. See the LICENSE.md file in the project root for more information.
using System.Collections;
using System.Collections.ObjectModel;
using System.Reflection;
using Stride.Animations;
using Stride.Core.Assets.Editor.ViewModels;
using Stride.Core.Mathematics;
using Stride.Core.Presentation.ViewModels;
using Stride.Core.Quantum;

namespace Stride.Assets.Editor.CurveEditor;

public enum RotationDisplayMode { Quaternion, Euler, AxisAngle }

public sealed class CurveEditorViewModel : DispatcherViewModel
{
    private readonly GraphNodeChangeListener listener;
    private CurveChannel? selectedChannel;
    private bool refreshing;
    private RotationDisplayMode rotationDisplay;
    public RotationDisplayMode RotationDisplay { get => rotationDisplay; set { rotationDisplay = value; Refresh(); } }
    public CurveEditorViewModel(SessionViewModel session, object curve, string name) : base(session.ServiceProvider)
    {
        Session = session;
        RootCurve = curve;
        Title = name;
        listener = new GraphNodeChangeListener(session.AssetNodeContainer.GetOrCreateNode(curve));
        listener.ValueChanged += OnChanged;
        listener.ItemChanged += OnChanged;
        listener.Initialize();
        Refresh();
    }
    public SessionViewModel Session { get; }
    public object RootCurve { get; }
    public string Title { get; }
    public ObservableCollection<CurveChannel> Channels { get; } = [];
    public HashSet<object> SelectedKeys { get; } = new(ReferenceEqualityComparer.Instance);
    public CurveChannel? SelectedChannel
    {
        get => selectedChannel;
        set { if (ReferenceEquals(value, selectedChannel)) return; selectedChannel = value; SelectedKeys.Clear(); OnPropertyChanged(nameof(SelectedChannel)); Changed?.Invoke(this, EventArgs.Empty); }
    }
    public event EventHandler? Changed;
    public static bool Supports(Type type) => type.GetInterfaces().Append(type).Any(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IComputeCurve<>) && SupportedTypes.Contains(i.GenericTypeArguments[0]));
    private static readonly Type[] SupportedTypes = [typeof(float), typeof(Vector2), typeof(Vector3), typeof(Vector4), typeof(Color4), typeof(Quaternion)];
    private void OnChanged(object? sender, EventArgs e) => Refresh();
    public void Refresh()
    {
        if (refreshing || IsDestroyed) return;
        refreshing = true;
        try
        {
            var previous = selectedChannel;
            var channels = new List<CurveChannel>();
            Visit(RootCurve, Title, new HashSet<object>(ReferenceEqualityComparer.Instance));
            Channels.Clear(); foreach (var channel in channels) Channels.Add(channel);
            selectedChannel = channels.FirstOrDefault(c => ReferenceEquals(c.Curve, previous?.Curve) && c.Component == previous.Component) ?? channels.FirstOrDefault();
            SelectedKeys.RemoveWhere(k => selectedChannel?.Keys.Contains(k) != true);
            OnPropertyChanged(nameof(SelectedChannel));
            Changed?.Invoke(this, EventArgs.Empty);
            void Visit(object curve, string path, HashSet<object> visited)
            {
                if (!visited.Add(curve) || !Supports(curve.GetType())) return;
                var valueType = curve.GetType().GetInterfaces().First(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IComputeCurve<>)).GenericTypeArguments[0];
                var names = valueType == typeof(float) ? new[] { "Value" } : valueType == typeof(Color4) ? new[] { "R", "G", "B", "A" } : valueType == typeof(Vector2) ? new[] { "X", "Y" } : valueType == typeof(Vector3) ? new[] { "X", "Y", "Z" } : new[] { "X", "Y", "Z", "W" };
                if (valueType == typeof(Quaternion) && curve.GetType().GetProperty("KeyFrames") == null && RotationDisplay == RotationDisplayMode.Euler) names = ["X", "Y", "Z"];
                for (var i = 0; i < names.Length; i++) channels.Add(new CurveChannel(curve, valueType, names[i], i, path + (names.Length == 1 ? "" : "." + names[i])) { RotationDisplay = RotationDisplay });
                foreach (var property in curve.GetType().GetProperties().Where(p => p.CanRead && p.GetIndexParameters().Length == 0 && Supports(p.PropertyType)))
                    if (property.GetValue(curve) is { } child) Visit(child, path + "/" + property.Name, visited);
            }
        }
        finally { refreshing = false; }
    }
    public void Edit(string name, Action action)
    {
        using var transaction = Session.ActionService!.CreateTransaction();
        action(); Session.ActionService.SetName(transaction, name);
        Refresh();
    }
    public object? AddPoint(double key, double value)
    {
        var channel = SelectedChannel;
        if (channel?.Editable != true || !double.IsFinite(key) || !double.IsFinite(value)) return null;
        object? point = null;
        Edit("Add curve key", () =>
        {
            var frameType = typeof(AnimationKeyFrame<>).MakeGenericType(channel.ValueType);
            point = Activator.CreateInstance(frameType)!;
            frameType.GetProperty("Key")!.SetValue(point, (float)key);
            ((IComputeCurve)channel.Curve).UpdateChanges();
            frameType.GetProperty("Value")!.SetValue(point, channel.WithComponent(channel.Sample((float)key), value));
            var index = channel.Keys.TakeWhile(k => channel.Key(k) <= key).Count();
            KeyNode(channel).Add(point, new NodeIndex(index));
        });
        SelectedKeys.Clear(); SelectedKeys.Add(point!); Changed?.Invoke(this, EventArgs.Empty);
        return point;
    }
    public void MovePoints(IReadOnlyDictionary<object, (double Key, double Value)> values)
    {
        var channel = SelectedChannel;
        if (channel?.Editable != true || values.Any(v => !double.IsFinite(v.Value.Key) || !double.IsFinite(v.Value.Value))) return;
        Edit("Move curve keys", () =>
        {
            foreach (var entry in values.Where(e => channel.Keys.Contains(e.Key)))
            {
                var node = Session.AssetNodeContainer.GetOrCreateNode(entry.Key);
                node["Key"].Update((float)entry.Value.Key);
                node["Value"].Update(channel.WithComponent(node["Value"].Retrieve()!, entry.Value.Value));
            }
        });
    }
    public void DeleteSelected()
    {
        var channel = SelectedChannel; if (channel?.Editable != true) return;
        Edit("Delete curve keys", () =>
        {
            var node = KeyNode(channel);
            for (var i = channel.Keys.Count - 1; i >= 0; i--) if (SelectedKeys.Contains(channel.Keys[i])) node.Remove(channel.Keys[i], new NodeIndex(i));
        });
        SelectedKeys.Clear(); Changed?.Invoke(this, EventArgs.Empty);
    }
    public void Clear() { if (SelectedChannel is not { Editable: true } channel) return; SelectedKeys.Clear(); foreach (var key in channel.Keys) SelectedKeys.Add(key); DeleteSelected(); }
    public void SelectAll() { SelectedKeys.Clear(); if (SelectedChannel is { } channel) foreach (var key in channel.Keys) SelectedKeys.Add(key); Changed?.Invoke(this, EventArgs.Empty); }
    private IObjectNode KeyNode(CurveChannel channel) => Session.AssetNodeContainer.GetOrCreateNode(channel.Curve)["KeyFrames"].Target!;
    public override void Destroy() { listener.Dispose(); Channels.Clear(); SelectedKeys.Clear(); base.Destroy(); }
}

public sealed class CurveChannel
{
    private readonly PropertyInfo? frames;
    private readonly FieldInfo? componentField;
    private readonly MethodInfo evaluate;
    public CurveChannel(object curve, Type valueType, string componentName, int component, string name)
    {
        Curve = curve; ValueType = valueType; Component = component; Name = name;
        frames = curve.GetType().GetProperty("KeyFrames");
        componentField = valueType == typeof(float) ? null : valueType.GetField(componentName);
        evaluate = typeof(IComputeCurve<>).MakeGenericType(valueType).GetMethod("Evaluate")!;
    }
    public object Curve { get; }
    public Type ValueType { get; }
    public int Component { get; }
    public string Name { get; }
    public RotationDisplayMode RotationDisplay { get; init; }
    public bool Editable => frames != null && typeof(ComputeAnimationCurve<>).MakeGenericType(ValueType).IsInstanceOfType(Curve);
    public IReadOnlyList<object> Keys => frames?.GetValue(Curve) is IEnumerable keys ? keys.Cast<object>().ToArray() : [];
    public double Key(object key) => Convert.ToDouble(key.GetType().GetProperty("Key")!.GetValue(key));
    public double Value(object key) => GetComponent(key.GetType().GetProperty("Value")!.GetValue(key)!);
    public double GetComponent(object value)
    {
        if (value is Quaternion q && !Editable && RotationDisplay != RotationDisplayMode.Quaternion)
        {
            if (q.LengthSquared() < MathUtil.ZeroTolerance) q = Quaternion.Identity;
            q.Normalize();
            if (RotationDisplay == RotationDisplayMode.AxisAngle)
            {
                var axis = q.Axis;
                if (axis.LengthSquared() > MathUtil.ZeroTolerance) axis.Normalize(); else axis = Vector3.UnitX;
                return Component switch { 0 => axis.X, 1 => axis.Y, 2 => axis.Z, _ => MathUtil.RadiansToDegrees(q.Angle) };
            }
            var matrix = Matrix.RotationQuaternion(q);
            var rotY = MathUtil.IsOne(Math.Abs(matrix.M13)) ? Math.Acos(Math.Clamp(Math.Max(Math.Abs(matrix.M11), Math.Abs(matrix.M33)), 0, 1)) : Math.Asin(Math.Clamp(-matrix.M13, -1, 1));
            var y = MathUtil.IsOne(Math.Abs(matrix.M13)) ? MathF.Sign(-matrix.M13) * rotY : rotY;
            var x = Math.Cos(rotY) > MathUtil.ZeroTolerance ? MathF.Atan2(matrix.M23, matrix.M33) : 0;
            var z = Math.Cos(rotY) > MathUtil.ZeroTolerance ? MathF.Atan2(matrix.M12, matrix.M11) : (matrix.M13 >= 0 ? 1 : -1) * MathF.Atan2(-matrix.M32, matrix.M22);
            return MathUtil.RadiansToDegrees((float)(Component switch { 0 => x, 1 => y, _ => z }));
        }
        return Convert.ToDouble(componentField?.GetValue(value) ?? value);
    }
    public object WithComponent(object value, double component) { if (componentField == null) return (float)component; componentField.SetValue(value, (float)component); return value; }
    public object Sample(float location) => evaluate.Invoke(Curve, [location])!;
    public double Evaluate(float location) => GetComponent(Sample(location));
    public override string ToString() => Name;
}
