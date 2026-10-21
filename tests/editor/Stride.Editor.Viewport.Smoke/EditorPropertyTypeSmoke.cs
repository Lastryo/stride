// Copyright (c) .NET Foundation and Contributors (https://dotnetfoundation.org/ & https://stride3d.net)
// Distributed under the MIT license. See the LICENSE.md file in the project root for more information.

using Avalonia.Controls;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Stride.Assets.Editor.ViewModels;
using Stride.Core.Presentation.Avalonia.Controls;
using Stride.Core.Presentation.Quantum.ViewModels;
using Stride.Engine;
using Stride.Rendering.Lights;

internal static class EditorPropertyTypeSmoke
{
    public static async Task Check(EntityHierarchyEditorViewModel editor, Window window)
    {
        var entity = editor.HierarchyRoot.InnerSubEntities.First(item => item.AssetSideEntity.Get<LightComponent>() != null);
        var light = entity.AssetSideEntity.Get<LightComponent>();
        var originalSelection = editor.SelectedContent.ToArray();
        var originalType = light.Type;
        var history = editor.Session.ActionService!;
        var changed = false;
        try
        {
            editor.SelectedContent.Clear();
            editor.SelectedContent.Add(entity);
            var graph = await editor.EditorProperties.GenerateSelectionPropertiesAsync([entity])
                ?? throw new Exception("The light entity did not produce a property graph.");
            await Layout();
            var node = Nodes(graph.RootNode).Single(item => item.Name == nameof(LightComponent.Type) && ReferenceEquals(item.NodeValue, originalType));
            var rowBefore = Row(node);
            AssertEditor(rowBefore);
            var replacement = originalType is LightPoint ? (ILight)new LightSpot() : new LightPoint();
            using (var transaction = history.CreateTransaction())
            {
                node.NodeValue = replacement;
                history.SetName(transaction, "Change light type smoke");
            }
            changed = true;
            await Layout();
            if (!ReferenceEquals(light.Type, replacement) || !ReferenceEquals(editor.EditorProperties.ViewModel, graph)
                || editor.SelectedContent.Count != 1 || !editor.SelectedContent.Contains(entity))
                throw new Exception("Changing a light type changed the selection or regenerated the entire property graph.");
            AssertEditor(Row(node));
            history.Undo();
            changed = false;
            await Layout();
            if (!ReferenceEquals(light.Type, originalType)) throw new Exception("Undo did not restore the original light type.");
            AssertEditor(Row(node));
            // The replaced row must still be alive when a fresh template is selected.
            GC.KeepAlive(rowBefore);
            Console.WriteLine("PASS: changing a light type and undo preserve its property editor without reselecting the entity.");
        }
        finally
        {
            if (changed) history.Undo();
            editor.SelectedContent.Clear();
            foreach (var item in originalSelection) editor.SelectedContent.Add(item);
        }

        PropertyViewItem Row(NodeViewModel node) => window.GetVisualDescendants().OfType<PropertyViewItem>()
            .Single(item => ReferenceEquals(item.DataContext, node));
        static void AssertEditor(PropertyViewItem row)
        {
            if (!row.IsEffectivelyVisible || !row.GetVisualDescendants().OfType<ComboBox>()
                .Any(combo => combo.Name == "InstanceTypeSelectionComboBox" && combo.IsEffectivelyVisible))
                throw new Exception("The light type selector disappeared from the Property Grid.");
        }
        static IEnumerable<NodeViewModel> Nodes(NodeViewModel node) => new[] { node }.Concat(node.Children.SelectMany(Nodes));
        static async Task Layout()
        {
            await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Background);
            await Task.Delay(100);
        }
    }
}
