// Copyright (c) .NET Foundation and Contributors (https://dotnetfoundation.org/ & https://stride3d.net)
// Distributed under the MIT license. See the LICENSE.md file in the project root for more information.

using Stride.Core.Assets;
using Stride.Core.Assets.Presentation;
using Stride.Core.Assets.Presentation.ViewModels;
using Stride.Core.Diagnostics;
using Stride.Core.Reflection;
using Stride.Engine;

namespace Stride.Assets.Presentation;

public sealed class StrideDefaultAssetsPlugin : AssetsPlugin
{
    private sealed class ComponentTypeComparer : EqualityComparer<Type>
    {
        public static new readonly ComponentTypeComparer Default = new();

        public override bool Equals(Type? x, Type? y)
        {
            if (x is null || y is null)
                return ReferenceEquals(x, y);
            return ReferenceEquals(x, y) || x.IsSubclassOf(y);
        }

        public override int GetHashCode(Type obj) => 1;
    }

    public static IReadOnlyList<(Type type, int order)> ComponentOrders { get; private set; } = [];

    public override void InitializePlugin(ILogger logger)
    {
        RegisterComponentOrders(logger);
    }

    public override void InitializeSession(ISessionViewModel session)
    {
        // nothing for now
    }

    public static Type? GetHighestOrderComponent(IEnumerable<Type> componentTypes)
    {
        return GetComponentsByOrder(componentTypes, false).FirstOrDefault();
    }

    public static Type? GetLowestOrderComponent(IEnumerable<Type> componentTypes)
    {
        return GetComponentsByOrder(componentTypes, true).FirstOrDefault();
    }

    public static IEnumerable<Type> GetComponentsByOrder(IEnumerable<Type> componentTypes, bool ascending)
    {
        if (ComponentOrders.Count == 0)
            RegisterComponentOrders(null);

        var types = componentTypes as Type[] ?? componentTypes.ToArray();
        var matched = ComponentOrders.Join(types, t => t.type, c => c, (t, _) => t, ComponentTypeComparer.Default).ToList();
        foreach (var type in types)
        {
            if (!matched.Any(m => ComponentTypeComparer.Default.Equals(type, m.type)))
            {
                var attrib = TypeDescriptorFactory.Default.AttributeRegistry.GetAttribute<ComponentOrderAttribute>(type, false);
                var order = attrib?.Order ?? (type == typeof(TransformComponent) ? 0 : 500);
                matched.Add((type, order));
            }
        }

        return (ascending ? matched.OrderBy(t => t.order) : matched.OrderByDescending(t => t.order)).Select(t => t.type);
    }

    private static void RegisterComponentOrders(ILogger? logger)
    {
        var hashSet = new HashSet<int>();
        var componentTypes = AssetRegistry.AssetAssemblies.SelectMany(x => x.GetTypes().Where(typeof(EntityComponent).IsAssignableFrom));
        var orders = new List<(Type, int)>();
        foreach (var type in componentTypes)
        {
            var attrib = TypeDescriptorFactory.Default.AttributeRegistry.GetAttribute<ComponentOrderAttribute>(type, false);
            if (attrib != null)
            {
                if (!hashSet.Add(attrib.Order))
                {
                    var other = orders.First(t => t.Item2 == attrib.Order);
                    logger?.Warning($"Two entity components have explicitly the same order value ({attrib.Order}): [{other.Item1}], [{type}]");
                }
                orders.Add((type, attrib.Order));
            }
        }
        orders.Reverse();
        ComponentOrders = orders;
    }
}
