// Copyright (c) .NET Foundation and Contributors (https://dotnetfoundation.org/ & https://stride3d.net)
// Distributed under the MIT license. See the LICENSE.md file in the project root for more information.

using System.Reflection;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Stride.Assets.Editor.Avalonia.Views;
using Stride.Assets.Editor.EntityHierarchyEditor.Game;
using Stride.Assets.Editor.GameEditor;
using Stride.Assets.Editor.Gizmos;
using Stride.Assets.Editor.SceneEditor;
using Stride.Assets.Editor.ViewModels;
using Stride.Assets.Models;
using Stride.Assets.Presentation.ViewModels;
using Stride.Core;
using Stride.Core.Assets.Presentation.ViewModels;
using Stride.Core.Mathematics;
using Stride.Core.Serialization;
using Stride.Core.Presentation.Avalonia.Converters;
using Stride.Engine;
using Stride.Rendering;
using Matrix = Stride.Core.Mathematics.Matrix;

internal static class EditorSelectionToolsSmoke
{
    private const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.NonPublic;

    public static async Task Check(EntityHierarchyEditorViewModel editor, Window window)
    {
        var view = ((Control)window.Content!).GetVisualDescendants().OfType<EntityHierarchyEditorView>().Single();
        var tree = view.FindControl<TreeView>("HierarchyTreeView")!;
        var viewport = (Control)editor.Viewport!.EditorHost!;
        var originalAsk = SceneEditorSettings.AskBeforeDeletingEntities.GetValue();
        SceneEditorSettings.AskBeforeDeletingEntities.SetValue(false);
        try
        {
            CheckEnumSelection();
            // The empty folder is rebuilt when the sibling entity is added.
            editor.CreateFolderInRootCommand.Execute(null);
            var path = editor.SelectedContent.OfType<EntityFolderViewModel>().Single().Path;
            editor.CreateEntityInRootCommand.Execute(EntityFactoryKind.EmptyEntity);
            var id = editor.SelectedContent.OfType<EntityViewModel>().Single().Id;
            Select(Folder(path), Entity(id));
            PressKey(viewport, Key.Delete);
            await Layout();
            if (Folder(path) != null || editor.FindPartViewModel(id) != null) throw new Exception("Mixed Delete left a folder or entity behind.");
            editor.Session.ActionService!.Undo();
            if (Folder(path) == null || editor.FindPartViewModel(id) == null) throw new Exception("Mixed Delete undo lost the folder or entity.");
            editor.Session.ActionService.Redo();
            if (Folder(path) != null || editor.FindPartViewModel(id) != null) throw new Exception("Mixed Delete redo left a folder or entity behind.");
            editor.Session.ActionService.Undo();
            await editor.DeleteItems(new EntityHierarchyItemViewModel[] { Folder(path)!, Entity(id) }, false);

            editor.CreateFolderInRootCommand.Execute(null);
            path = editor.SelectedContent.OfType<EntityFolderViewModel>().Single().Path;
            editor.CreateFolderInSelectionCommand.Execute(null);
            var nestedPath = editor.SelectedContent.OfType<EntityFolderViewModel>().Single().Path;
            editor.CreateEntityInSelectionCommand.Execute(EntityFactoryKind.EmptyEntity);
            id = editor.SelectedContent.OfType<EntityViewModel>().Single().Id;
            await editor.DeleteItems(new[] { Folder(path)! }, false);
            if (Folder(path) != null || editor.FindPartViewModel(id) != null) throw new Exception("Deleting a folder did not delete its subtree.");
            editor.Session.ActionService.Undo();
            if (Folder(path) == null || Folder(nestedPath) == null || Entity(id).EntityDesign.Folder != nestedPath)
                throw new Exception("Folder undo did not restore its nested structure and entity membership.");
            editor.Session.ActionService.Redo();
            if (Folder(path) != null || editor.FindPartViewModel(id) != null) throw new Exception("Folder subtree redo failed.");
            Console.WriteLine("PASS: mixed folder/entity Delete, folder subtree deletion, undo and redo resolve current hierarchy items.");

            var beforeParts = editor.Asset.Asset.Hierarchy.Parts.Keys.ToHashSet();
            var modelAsset = editor.Session.AllAssets.First(asset => asset.Asset is ModelAsset);
            var parentId = editor.DropAssets(new[] { modelAsset }, editor.HierarchyRoot, DropPosition.Into).Single().Id;
            var resource = Entity(parentId).AssetSideEntity.Get<ModelComponent>()!.Model;
            editor.CreateEntityInSelectionCommand.Execute(EntityFactoryKind.EmptyEntity);
            var childId = editor.SelectedContent.OfType<EntityViewModel>().Single().Id;
            var rootCount = editor.Asset.Asset.Hierarchy.RootParts.Count;
            foreach (var target in new Control[] { tree, viewport })
            {
                Select(Entity(parentId), Entity(childId));
                PressKey(target, Key.D, KeyModifiers.Control);
                await Layout();
                var clone = editor.SelectedContent.OfType<EntityViewModel>().Single();
                var cloneId = clone.Id;
                var cloneChild = clone.AssetSideEntity.Transform.Children.Single().Entity;
                if (cloneId == parentId || cloneChild.Id == childId.ObjectId || editor.Asset.Asset.Hierarchy.RootParts.Count != rootCount + 1
                    || !editor.Asset.Asset.Hierarchy.Parts.ContainsKey(cloneChild.Id) || clone.Name == Entity(parentId).Name)
                    throw new Exception("Ctrl+D did not clone a selected parent/child subtree exactly once with new IDs and a unique root name.");
                if (AttachedReferenceManager.GetAttachedReference(clone.AssetSideEntity.Get<ModelComponent>().Model).Id
                    != AttachedReferenceManager.GetAttachedReference(resource).Id)
                    throw new Exception("Entity duplication changed its referenced model asset.");
                if (editor.Session.ActionService.GetName(editor.Session.ActionService.RetrieveAllTransactions().Last()) != "Duplicate entities")
                    throw new Exception("Entity duplication was not recorded as one undo transaction: " + string.Join(" | ", editor.Session.ActionService.RetrieveAllTransactions().TakeLast(5).Select(transaction => editor.Session.ActionService.GetName(transaction))));
                editor.Session.ActionService.Undo();
                if (editor.FindPartViewModel(cloneId) != null || editor.Asset.Asset.Hierarchy.Parts.ContainsKey(cloneChild.Id))
                    throw new Exception($"Duplicate undo left part of the cloned subtree behind: root={editor.FindPartViewModel(cloneId) != null}, child={editor.Asset.Asset.Hierarchy.Parts.ContainsKey(cloneChild.Id)}.");
                editor.Session.ActionService.Redo();
                if (editor.FindPartViewModel(cloneId) == null || !editor.Asset.Asset.Hierarchy.Parts.ContainsKey(cloneChild.Id))
                    throw new Exception("Duplicate redo did not restore the same subtree IDs.");
                await editor.DeleteItems(new[] { Entity(cloneId) }, false);
            }
            Select(Entity(parentId));
            var filter = view.GetVisualDescendants().OfType<TextBox>().First(box => ReferenceEquals(box.DataContext, editor));
            PressKey(filter, Key.D, KeyModifiers.Control);
            await Layout();
            if (editor.Asset.Asset.Hierarchy.RootParts.Count != rootCount) throw new Exception("Ctrl+D duplicated an entity while editing a text field.");
            await editor.DeleteItems(new[] { Entity(parentId) }, false);
            if (!editor.Asset.Asset.Hierarchy.Parts.Keys.ToHashSet().SetEquals(beforeParts)) throw new Exception("Duplicate checks did not restore the original scene parts.");
            Console.WriteLine("PASS: Ctrl+D in hierarchy and viewport clones subtrees once, preserves model references, selects copies and supports undo/redo.");

            await CheckAssets(editor, window);
            CheckPivotMath();
            var pivot = view.FindControl<ComboBox>("TransformationPivot")!;
            foreach (var mode in Enum.GetValues<TransformationPivot>())
            {
                pivot.SetCurrentValue(ComboBox.SelectedItemProperty, mode);
                if (editor.Viewport.Pivot != mode) throw new Exception("The Pivot/Center selector is not bound to the viewport.");
                await editor.Viewport.Controller.InvokeAsync(() =>
                {
                    var gizmos = (TransformationGizmo[])typeof(EditorGameEntityTransformService).GetField("gizmos", Hidden)!.GetValue(editor.Viewport.Controller.GetTransformService())!;
                    if (gizmos.Any(gizmo => gizmo.Pivot != mode)) throw new Exception("Pivot/Center did not reach all transform tools.");
                });
            }
            editor.Viewport.Pivot = TransformationPivot.Pivot;
            await editor.Viewport.ReloadContent();
        }
        finally { SceneEditorSettings.AskBeforeDeletingEntities.SetValue(originalAsk); }

        EntityViewModel Entity(AbsoluteId value) => (EntityViewModel)editor.FindPartViewModel(value)!;
        EntityFolderViewModel? Folder(string value) => editor.HierarchyRoot.Children.OfType<EntityFolderViewModel>()
            .SelectMany(folder => Descendants(folder)).FirstOrDefault(folder => folder.Path == value);
        static IEnumerable<EntityFolderViewModel> Descendants(EntityFolderViewModel folder) => new[] { folder }.Concat(folder.Children.OfType<EntityFolderViewModel>().SelectMany(Descendants));
        void Select(params EntityHierarchyItemViewModel?[] items)
        {
            editor.SelectedContent.Clear();
            foreach (var item in items) editor.SelectedContent.Add(item!);
        }
        async Task Layout() { await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Background); window.UpdateLayout(); }
    }

    private static async Task CheckAssets(EntityHierarchyEditorViewModel editor, Window window)
    {
        var collection = editor.Session.AssetCollection;
        var source = editor.Session.AllAssets.First(asset => asset.IsEditable && asset.Asset is ModelAsset);
        var originals = new[] { source, (AssetViewModel)editor.Asset };
        var disk = originals.ToDictionary(asset => asset.Id, asset => File.ReadAllBytes(asset.AssetItem.FullPath));
        collection.SelectAssets(originals);
        await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Background);
        var list = ((Control)window.Content!).GetVisualDescendants().OfType<Stride.GameStudio.Avalonia.Views.AssetExplorerView>().Single().GetVisualDescendants().OfType<ListBox>().Single();
        PressKey(list, Key.D, KeyModifiers.Control);
        await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Background);
        var copies = collection.SelectedAssets.ToArray();
        if (copies.Length != 2 || copies.Any(copy => originals.Any(original => copy.Id == original.Id || copy.Url == original.Url))
            || copies.Any(copy => !copy.Directory.Assets.Contains(copy) || editor.Session.GetAssetById(copy.Id) != copy || !copy.AssetItem.IsDirty))
            throw new Exception("Asset View Ctrl+D did not create and select distinct, registered, dirty asset copies.");
        var sceneCopy = copies.Single(asset => asset.Asset.GetType() == editor.Asset.Asset.GetType());
        if (((Stride.Assets.Entities.EntityHierarchyAssetBase)sceneCopy.Asset).Hierarchy.Parts.Keys.Intersect(editor.Asset.Asset.Hierarchy.Parts.Keys).Any())
            throw new Exception("A duplicated scene reused its original entity IDs.");
        editor.Session.ActionService!.Undo();
        if (copies.Any(copy => editor.Session.GetAssetById(copy.Id) != null || copy.Directory.Assets.Contains(copy)))
            throw new Exception("Asset duplicate undo left copies in the session or Asset View directory.");
        editor.Session.ActionService.Redo();
        if (copies.Any(copy => editor.Session.GetAssetById(copy.Id) != copy || !copy.Directory.Assets.Contains(copy)))
            throw new Exception("Asset duplicate redo did not restore its original copies.");
        using (editor.Session.ActionService.CreateTransaction())
            foreach (var copy in copies) copy.IsDeleted = true;
        foreach (var original in originals)
            if (!File.ReadAllBytes(original.AssetItem.FullPath).SequenceEqual(disk[original.Id])) throw new Exception("Asset duplication overwrote an original asset file.");
        collection.SelectAssets(new[] { source });
        Console.WriteLine("PASS: Asset View Ctrl+D duplicates model/scene assets with new IDs, preserves originals and supports directory/session undo/redo.");
    }

    private static void CheckPivotMath()
    {
        var first = new Entity { Transform = { Position = new Vector3(-2, 0, 0) }, Components = { new ModelComponent() } };
        var second = new Entity { Transform = { Position = new Vector3(6, 0, 0) } };
        first.Transform.UpdateWorldMatrix();
        second.Transform.UpdateWorldMatrix();
        var gizmo = new TranslationGizmo { AnchorEntity = second, ModifiedEntities = new[] { first, second } };
        var center = new Vector3(2, 0, 0);
        var position = typeof(TransformationGizmo).GetMethod("GetPivotPosition", Hidden)!;
        if ((Vector3)position.Invoke(gizmo, null)! != second.Transform.Position) throw new Exception("Pivot does not follow the active object.");
        gizmo.Pivot = TransformationPivot.Center;
        if ((Vector3)position.Invoke(gizmo, null)! != center) throw new Exception("Center is not the center of the selection.");

        var stateType = typeof(TransformationGizmo).GetNestedType("InitialTransformation", BindingFlags.NonPublic)!;
        var start = typeof(TransformationGizmo).GetField("StartWorldMatrix", Hidden)!;
        var capture = typeof(TransformationGizmo).GetMethod("OnTransformationStarted", Hidden)!;
        var apply = typeof(TransformationGizmo).GetMethod("ApplyTransformation", Hidden)!;
        foreach (var mode in Enum.GetValues<Transformation>())
        {
            first.Transform.Position = new Vector3(-2, 0, 0);
            second.Transform.Position = new Vector3(6, 0, 0);
            first.Transform.Scale = second.Transform.Scale = Vector3.One;
            first.Transform.Rotation = second.Transform.Rotation = Quaternion.Identity;
            start.SetValue(gizmo, Matrix.Translation(center));
            capture.Invoke(gizmo, new object[] { Vector2.Zero });
            var state = Activator.CreateInstance(stateType)!;
            stateType.GetField("Translation")!.SetValue(state, mode == Transformation.Translation ? new Vector3(3, 1, 0) : Vector3.Zero);
            stateType.GetField("Scale")!.SetValue(state, mode == Transformation.Scale ? new Vector3(2) : Vector3.One);
            stateType.GetField("Rotation")!.SetValue(state, mode == Transformation.Rotation ? Quaternion.RotationZ(MathUtil.PiOverTwo) : Quaternion.Identity);
            apply.Invoke(gizmo, new[] { state });
            var a = first.Transform.Position;
            var b = second.Transform.Position;
            if (mode == Transformation.Translation && (a != new Vector3(1, 1, 0) || b != new Vector3(9, 1, 0))) throw new Exception("Group translation changed relative positions.");
            if (mode == Transformation.Scale && (a != new Vector3(-6, 0, 0) || b != new Vector3(10, 0, 0))) throw new Exception("Center scaling did not scale offsets around the group center.");
            if (mode == Transformation.Rotation && (Math.Abs(a.X - 2) > .001 || Math.Abs(b.X - 2) > .001 || Math.Abs(Math.Abs(a.Y) - 4) > .001 || Math.Abs(a.Y + b.Y) > .001))
                throw new Exception("Center rotation did not orbit the group center.");
        }
        Console.WriteLine("PASS: Pivot/Center positioning and group move, rotate and scale operate around the chosen origin.");
    }

    private static void CheckEnumSelection()
    {
        var source = new EnumSource();
        var box = new ComboBox { ItemsSource = Enum.GetValues<RenderGroup>() };
        box.Bind(ComboBox.SelectedItemProperty, new Binding(nameof(EnumSource.Value))
        {
            Source = source, Mode = BindingMode.TwoWay, Converter = new EnumSelection(),
        });
        box.SetCurrentValue(ComboBox.SelectedItemProperty, null);
        if (source.Value != RenderGroup.Group3) throw new Exception("Refreshing an enum editor reset the asset's render group.");
        box.SetCurrentValue(ComboBox.SelectedItemProperty, RenderGroup.Group5);
        if (source.Value != RenderGroup.Group5) throw new Exception("The enum editor stopped accepting actual selections.");
        Console.WriteLine("PASS: enum editor initialization does not overwrite values; actual selection changes still commit.");
    }

    private sealed class EnumSource
    {
        public RenderGroup Value { get; set; } = RenderGroup.Group3;
    }

    private static void PressKey(Control target, Key key, KeyModifiers modifiers = KeyModifiers.None)
    {
        target.Focus();
        target.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = key, KeyModifiers = modifiers });
        target.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyUpEvent, Key = key, KeyModifiers = modifiers });
    }
}
