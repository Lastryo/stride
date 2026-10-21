// Copyright (c) .NET Foundation and Contributors (https://dotnetfoundation.org/ & https://stride3d.net)
// Distributed under the MIT license. See the LICENSE.md file in the project root for more information.

using Avalonia;
using Point = Avalonia.Point;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Input.Raw;
using Stride.Editor.Avalonia.Engine;
using Avalonia.Threading;
using Stride.Assets.Editor.Avalonia.Views;
using Stride.Assets.Editor.ViewModels;
using Stride.Assets.Entities;
using Stride.Assets.Presentation.ViewModels;
using Stride.Core;
using Stride.Core.Assets;
using Stride.Core.Assets.Editor.ViewModels;
using Stride.Core.Assets.Quantum;
using Stride.Core.Assets.Serializers;
using Stride.Core.Assets.Templates;
using Stride.Core.Mathematics;
using Stride.Engine;
using Stride.Engine.Design;
using Stride.GameStudio.Avalonia.Views;

internal static class EditorPrefabSmoke
{
    public static async Task Check(SessionViewModel session, Window owner)
    {
        var history = session.ActionService!;
        var project = session.CurrentProject!;
        Check(Path.GetFullPath(project.Package.RootDirectory!.ToOSPath()).StartsWith(
            Path.TrimEndingDirectorySeparator(Path.GetTempPath()) + Path.DirectorySeparatorChar, StringComparison.Ordinal)
            || File.Exists(Path.Combine(project.Package.RootDirectory.ToOSPath(), ".stride-disposable-smoke-project")),
            "Prefab persistence checks require a disposable project copy under the system temporary directory or a .stride-disposable-smoke-project marker.");
        var suffix = Guid.NewGuid().ToString("N");
        var model = new ModelComponent();
        var parent = new Entity("Prefab / selection") { model };
        var child = new Entity("Nested child");
        parent.AddChild(child);
        var other = new Entity("Second root") { new ModelNodeLinkComponent { Target = model } };
        child.Transform.Position = new Vector3(1, 2, 3);
        var data = new SceneAsset();
        foreach (var entity in new[] { parent, child, other }) data.Hierarchy.Parts.Add(new EntityDesign(entity));
        data.Hierarchy.RootParts.Add(parent);
        data.Hierarchy.RootParts.Add(other);
        SceneViewModel scene;
        using (history.CreateTransaction())
            scene = (SceneViewModel)project.AddAsset(new AssetItem("PrefabSmoke_" + suffix, data), project.AssetMountPoint);
        var editor = new SceneEditorViewModel(scene);
        // Leave this view unloaded to inspect bindings without starting another renderer.
        var view = new EntityHierarchyEditorView { DataContext = editor };
        var treeMenu = view.FindControl<TreeView>("HierarchyTreeView")!.ContextMenu!;
        var viewportMenu = view.FindControl<ContentControl>("ViewportHostControl")!.ContextMenu!;
        treeMenu.DataContext = viewportMenu.DataContext = editor;
        await Flush();
        var action = treeMenu.Items.OfType<MenuItem>().Single(item => Equals(item.Header, "Create prefab from selection"));
        var viewportAction = viewportMenu.Items.OfType<MenuItem>().Single();
        Check(ReferenceEquals(action.Command, editor.CreatePrefabFromSelectionCommand)
            && ReferenceEquals(viewportAction.Command, editor.CreatePrefabFromSelectionCommand), "Both context menus must use the prefab command.");
        Check(!editor.CreatePrefabFromSelectionCommand.CanExecute(null), "Empty selection must disable prefab creation.");
        Check(!editor.DuplicateCommand.CanExecute(null) && !editor.OpenPrefabEditorCommand.CanExecute(null)
            && !editor.BreakLinkToPrefabCommand.CanExecute(null) && !editor.SelectPrefabCommand.CanExecute(null),
            "Empty selection must disable Action commands.");
        foreach (var (header, command) in new[] {
            ("Duplicate selected entities", editor.DuplicateCommand), ("Open prefab in editor", editor.OpenPrefabEditorCommand),
            ("Select prefab in asset view", editor.SelectPrefabCommand), ("Break link to prefab", editor.BreakLinkToPrefabCommand) })
            Check(ReferenceEquals(treeMenu.Items.OfType<MenuItem>().Single(item => Equals(item.Header, header)).Command, command),
                "Action menu must bind " + header);

        EntityViewModel Part(Guid id) => (EntityViewModel)editor.FindPartViewModel(new AbsoluteId(scene.Id, id))!;
        void Select(params Guid[] ids)
        {
            editor.SelectedContent.Clear();
            foreach (var id in ids) editor.SelectedContent.Add(Part(id));
        }
        var originalDataHome = Environment.GetEnvironmentVariable("XDG_DATA_HOME");
        if (OperatingSystem.IsLinux())
        {
            var trashRoot = Path.Combine(project.Package.RootDirectory!.ToOSPath(), "obj", "prefab-trash-smoke-" + suffix);
            Directory.CreateDirectory(trashRoot);
            Environment.SetEnvironmentVariable("XDG_DATA_HOME", trashRoot);
        }
        try
        {
            Select(parent.Id);
            Check(editor.DuplicateCommand.CanExecute(null) && editor.CreatePrefabFromSelectionCommand.CanExecute(null)
                && !editor.OpenPrefabEditorCommand.CanExecute(null) && !editor.BreakLinkToPrefabCommand.CanExecute(null),
                "Plain Entity must enable duplication and extraction but disable prefab actions.");
            Select(parent.Id, child.Id, other.Id);
            Check(editor.CreatePrefabFromSelectionCommand.CanExecute(null), "Entity selection must enable prefab creation.");
            await CheckViewportMenu();
            var before = project.Assets.Select(asset => asset.Id).ToHashSet();
            action.Command!.Execute(null);
            var prefab = project.Assets.OfType<PrefabViewModel>().Single(asset => !before.Contains(asset.Id));
            Check(prefab.Asset.Hierarchy.RootParts.Count == 2 && prefab.Asset.Hierarchy.Parts.Count == 3, "Parent + child must not duplicate the subtree.");
            Check(!prefab.Name.Contains('/') && session.AssetCollection.SelectedAssets.Contains(prefab), "The new prefab needs a safe name and Asset View selection.");
            var clones = prefab.Asset.Hierarchy.Parts.Values.Select(design => design.Entity).ToDictionary(entity => entity.Name);
            Check(clones.Values.All(entity => entity.Id != parent.Id && entity.Id != child.Id && entity.Id != other.Id), "Prefab entities must have new IDs.");
            Check(ReferenceEquals(clones[child.Name].Transform.Parent?.Entity, clones[parent.Name]), "Child hierarchy must survive cloning.");
            Check(ReferenceEquals(clones[other.Name].Get<ModelNodeLinkComponent>().Target, clones[parent.Name].Get<ModelComponent>()), "Internal component references must point into the prefab.");
            var instanceIds = new HashSet<Guid>();
            foreach (var design in data.Hierarchy.Parts.Values)
            {
                Check(design.Base?.BasePartAsset.Id == prefab.Id && prefab.Asset.Hierarchy.Parts.ContainsKey(design.Base.BasePartId), "Every original entity must link to its new base.");
                instanceIds.Add(design.Base!.InstanceId);
                Check(Part(design.Entity.Id).IsPrefabInstance, "Hierarchy prefab state must update after linking.");
            }
            Check(instanceIds.Count == 1, "One selection must become one prefab instance.");
            using (var stream = new MemoryStream())
            {
                AssetFileSerializer.Save(stream, data, null);
                stream.Position = 0;
                var loaded = AssetFileSerializer.Load<SceneAsset>(stream, "PrefabSmoke.sdscene").Asset;
                Check(loaded.Hierarchy.Parts[parent.Id].Base?.BasePartAsset.Id == prefab.Id,
                    "Scene serialization must preserve the prefab link.");
            }
            using (var stream = new MemoryStream())
            {
                AssetFileSerializer.Save(stream, prefab.Asset, null);
                stream.Position = 0;
                var loaded = AssetFileSerializer.Load<PrefabAsset>(stream, "PrefabSmoke.sdprefab").Asset;
                Check(loaded.Hierarchy.Parts.Count == 3 && loaded.Hierarchy.RootParts.Count == 2,
                    "Prefab serialization must preserve the selected hierarchy.");
            }
            await Flush();
            var saveLog = new Stride.Core.Diagnostics.LoggerResult();
            Check(session.Save(saveLog), "Saving prefab and scene must succeed: " + string.Join("; ", saveLog.Messages));
            Check(File.Exists(prefab.AssetItem.FullPath.ToOSPath()),
                $"Successful save must write the new prefab: {prefab.AssetItem.FullPath}; dirty={prefab.IsDirty}/{prefab.AssetItem.IsDirty}.");
            using (var stream = File.OpenRead(prefab.AssetItem.FullPath.ToOSPath()))
            {
                var diskPrefab = AssetFileSerializer.Load<PrefabAsset>(stream, prefab.AssetItem.FullPath).Asset;
                Check(diskPrefab.Id == prefab.Id && diskPrefab.Hierarchy.Parts.Count == 3,
                    "Saved prefab must preserve its ID and entities.");
            }
            Console.WriteLine("PASS: new prefab file is saved alongside the scene and can be deserialized from disk.");
            var prefabPath = prefab.AssetItem.FullPath.ToOSPath();
            var missingBackup = prefabPath + ".missing-smoke-backup";
            File.Move(prefabPath, missingBackup);
            try
            {
                Check(!prefab.IsDirty && !prefab.AssetItem.IsDirty, "Fixture must start at a clean save point.");
                Check(session.Save(saveLog) && File.Exists(prefabPath),
                    "Save must recreate a missing prefab even when dirty flags are clean.");
            }
            finally
            {
                File.Move(missingBackup, prefabPath, true);
            }
            Console.WriteLine("PASS: saving repairs a missing prefab file even at a clean save point.");

            var deletedPrefab = prefab;
            var deleteLog = new Stride.Core.Diagnostics.LoggerResult();
            Check(session.Save(deleteLog), "Save before prefab deletion must succeed: " + string.Join("; ", deleteLog.Messages));
            var deletedPath = deletedPrefab.AssetItem.FullPath.ToOSPath();
            Check(File.Exists(deletedPath), "Prefab file must exist before deletion.");
            session.AssetCollection.SelectAssets([deletedPrefab]);
            Check(await session.AssetCollection.DeleteSelection(askConfirmation: false), "Deleting the prefab asset must succeed.");
            Check(data.Hierarchy.Parts.Values.All(part => part.Base == null),
                "Deleting a prefab must detach scene instances before saving, like WPF.");
            Check(data.Hierarchy.Parts.Count == 3 && child.Transform.Position == new Vector3(1, 2, 3)
                && ReferenceEquals(other.Get<ModelNodeLinkComponent>().Target, parent.Get<ModelComponent>()),
                "Deleting the prefab must preserve scene entities, values and internal references.");
            history.Undo();
            Check(!deletedPrefab.IsDeleted && data.Hierarchy.Parts.Values.All(part => part.Base?.BasePartAsset.Id == deletedPrefab.Id),
                "Undo deletion must restore both prefab and instance links.");
            history.Redo();
            Check(deletedPrefab.IsDeleted && data.Hierarchy.Parts.Values.All(part => part.Base == null),
                "Redo deletion must detach the instances again.");
            Check(session.Save(deleteLog) && !File.Exists(deletedPath), "Save must remove the deleted prefab file: " + string.Join("; ", deleteLog.Messages));
            CheckSavedScene();
            history.Undo();
            Check(session.Save(deleteLog) && File.Exists(deletedPath)
                && data.Hierarchy.Parts.Values.All(part => part.Base?.BasePartAsset.Id == deletedPrefab.Id),
                "Undo after saving deletion must restore the prefab file and links on the next save.");
            history.Redo();
            Check(session.Save(deleteLog) && !File.Exists(deletedPath), "Redo after saving must remove the prefab again.");
            CheckSavedScene();
            history.Undo(); // Return to the original linked selection for the remaining checks.
            Console.WriteLine("PASS: create prefab, delete its asset, detach scene instances, save without dangling bases, Undo/Redo before and after Save.");

            void CheckSavedScene()
            {
                using var stream = File.OpenRead(scene.AssetItem.FullPath.ToOSPath());
                var loaded = AssetFileSerializer.Load<SceneAsset>(stream, scene.AssetItem.FullPath).Asset;
                Check(loaded.Hierarchy.Parts.Count == 3 && loaded.Hierarchy.Parts.Values.All(part => part.Base == null),
                    "Scene on disk must retain entities without references to the deleted prefab.");
            }

            history.Undo();
            Check(prefab.IsDeleted && data.Hierarchy.Parts.Values.All(design => design.Base == null)
                && !Part(parent.Id).IsPrefabInstance, "Undo must remove both asset and instance links.");
            history.Redo();
            Check(!prefab.IsDeleted && data.Hierarchy.Parts.Values.All(design => design.Base?.BasePartAsset.Id == prefab.Id), "Redo must restore asset and links.");
            Select(other.Id);
            Check(editor.OpenPrefabEditorCommand.CanExecute(null) && editor.BreakLinkToPrefabCommand.CanExecute(null)
                && editor.SelectPrefabCommand.CanExecute(null), "One prefab instance must enable all prefab actions.");
            editor.SelectPrefabCommand.Execute(null);
            Check(session.AssetCollection.SelectedAssets.Contains(prefab), "Select prefab must reveal its source asset.");
            Select(other.Id);
            editor.BreakLinkToPrefabCommand.Execute(null);
            Check(!Part(other.Id).IsPrefabInstance && !editor.OpenPrefabEditorCommand.CanExecute(null)
                && !editor.BreakLinkToPrefabCommand.CanExecute(null), "Breaking a link must immediately disable prefab actions.");
            Select(parent.Id, other.Id);
            Check(editor.OpenPrefabEditorCommand.CanExecute(null) && editor.BreakLinkToPrefabCommand.CanExecute(null)
                && !editor.SelectPrefabCommand.CanExecute(null), "Mixed selection must enable open/break but disable single-source selection.");
            editor.BreakLinkToPrefabCommand.Execute(null);
            Check(!Part(parent.Id).IsPrefabInstance && !Part(other.Id).IsPrefabInstance && Part(child.Id).IsPrefabInstance,
                "Break link must affect selected parts only, matching WPF.");
            history.Undo();
            Check(Part(parent.Id).IsPrefabInstance && editor.BreakLinkToPrefabCommand.CanExecute(null),
                "Undo must restore the link and command availability without changing selection.");
            history.Redo();
            Check(!Part(parent.Id).IsPrefabInstance && !editor.BreakLinkToPrefabCommand.CanExecute(null),
                "Redo must detach the part and refresh command availability.");
            history.Undo();
            history.Undo();
            Select(parent.Id, child.Id, other.Id);
            Console.WriteLine("PASS: Action bindings, empty/plain/prefab/mixed selections, break-link scope and command refresh on Undo/Redo.");
            Console.WriteLine("PASS: hierarchy/viewport menus, root deduplication, new IDs, child/component references, prefab links, state and Undo/Redo.");

            // Prefab edits must propagate to the original scene instance.
            var baseChild = clones[child.Name];
            using (history.CreateTransaction())
                session.AssetNodeContainer.GetOrCreateNode(baseChild)[nameof(Entity.Name)].Update("Edited in prefab");
            Check(child.Name == "Edited in prefab", "A prefab edit must propagate back to the scene instance.");
            history.Undo();
            Check(child.Name == "Nested child", "Undo of a prefab edit must propagate.");

            // Extract an overridden existing instance into another prefab.
            var sourceNode = session.AssetNodeContainer.GetOrCreateNode(child);
            using (history.CreateTransaction()) sourceNode[nameof(Entity.Name)].Update("Overridden instance");
            Check(((IAssetMemberNode)sourceNode[nameof(Entity.Name)]).IsContentOverridden(), "Fixture must contain an override.");
            Select(parent.Id);
            var derived = editor.CreatePrefabFromSelection()!;
            Check(derived.Asset.Hierarchy.Parts.Values.Any(design => design.Entity.Name == "Overridden instance"), "Extraction must preserve overridden values.");
            Check(derived.Asset.Hierarchy.Parts.Values.Any(design => design.Base?.BasePartAsset.Id == prefab.Id), "The new prefab must preserve its existing prefab bases.");
            Check(data.Hierarchy.Parts[parent.Id].Base?.BasePartAsset.Id == derived.Id, "Modified selection must link to the new prefab.");
            history.Undo();
            Check(derived.IsDeleted && data.Hierarchy.Parts[parent.Id].Base?.BasePartAsset.Id == prefab.Id
                && child.Name == "Overridden instance" && ((IAssetMemberNode)sourceNode[nameof(Entity.Name)]).IsContentOverridden(),
                "Undo extraction must restore the previous prefab link and override.");
            history.Undo(); // override
            history.Undo(); // original extraction
            Check(prefab.IsDeleted && data.Hierarchy.Parts.Values.All(design => design.Base == null), "Fixture must return to independent entities.");
            Console.WriteLine("PASS: prefab edit propagation and extraction of a modified instance with overrides, nested bases and Undo.");

            var description = session.FindTemplates(TemplateScope.Asset).OfType<TemplateAssetDescription>().First(t => t.AssetTypeName == nameof(PrefabAsset));
            Check(AddAssetTemplateItem.GetGroup(description) == "Prefabs", "Add asset must expose Prefabs > Prefab.");
            var empty = (PrefabViewModel)(await session.CreateAssetFromTemplate(description, project.AssetMountPoint))!;
            Check(empty.Asset.Hierarchy.Parts.Count == 0, "The Prefab template must create an empty asset.");
            session.AssetCollection.SelectAssets([empty]);
            session.EditSelectedContentCommand.Execute(null);
            Check(session.EditorCollection.ActiveEditor is PrefabEditorViewModel, "Opening the prefab asset must create the Prefab Editor.");
            var prefabEditor = (PrefabEditorViewModel)session.EditorCollection.ActiveEditor!;
            prefabEditor.CreateEntityInRootCommand.Execute(EntityFactoryKind.EmptyEntity);
            await Flush();
            Check(empty.Asset.Hierarchy.RootParts.Count == 1, "The Prefab Editor must support adding entities.");
            await session.EditorCollection.CloseEditorAsync(prefabEditor).WaitAsync(TimeSpan.FromSeconds(30));
            history.Undo(); // entity
            history.Undo(); // empty prefab
            Console.WriteLine("PASS: Prefabs > Prefab, empty creation, opening the Prefab Editor, adding an entity and Undo; saves affect only the temporary project.");

            Select(parent.Id, child.Id, other.Id);
            var unsavedPrefab = editor.CreatePrefabFromSelection()!;
            var unsavedPath = unsavedPrefab.AssetItem.FullPath.ToOSPath();
            Check(!File.Exists(unsavedPath), "The new prefab must not be saved before this deletion check.");
            session.AssetCollection.SelectAssets([unsavedPrefab]);
            Check(await session.AssetCollection.DeleteSelection(askConfirmation: false)
                && data.Hierarchy.Parts.Values.All(part => part.Base == null),
                "Deleting a newly created, never-saved prefab must also detach its instances.");
            var unsavedDeleteLog = new Stride.Core.Diagnostics.LoggerResult();
            Check(session.Save(unsavedDeleteLog) && !File.Exists(unsavedPath),
                "Create/delete/save must succeed without resurrecting the removed prefab: " + string.Join("; ", unsavedDeleteLog.Messages));
            CheckSavedScene();
            Console.WriteLine("PASS: create prefab, delete before its first save, save scene without dangling bases.");



        }
        finally
        {
            Environment.SetEnvironmentVariable("XDG_DATA_HOME", originalDataHome);
            view.DataContext = null;
            editor.Destroy();
        }

        async Task CheckViewportMenu()
        {
            var host = view.FindControl<ContentControl>("ViewportHostControl")!;
            var panel = (Panel)host.Parent!;
            panel.Children.Remove(host);
            using var gameHost = new AvaloniaGameView();
            host.Content = gameHost;
            var window = new Window { Title = "Prefab context menu check", Width = 400, Height = 240, Content = host };
            window.Show(owner);
            try
            {
                await Flush();
                var pointer = new Pointer(981, PointerType.Mouse, true);
                var point = new Point(100, 100);
                void Press() => gameHost.RaiseEvent(new PointerPressedEventArgs(gameHost, pointer, gameHost, point, 0,
                    new PointerPointProperties(RawInputModifiers.RightMouseButton, PointerUpdateKind.RightButtonPressed), KeyModifiers.None, 1));
                void Release() => gameHost.RaiseEvent(new PointerReleasedEventArgs(gameHost, pointer, gameHost, point, 0,
                    new PointerPointProperties(RawInputModifiers.None, PointerUpdateKind.RightButtonReleased), KeyModifiers.None, MouseButton.Right));
                Press();
                Release();
                await Flush();
                Check(viewportMenu.IsOpen && viewportAction.IsEnabled, "Right-click must open the viewport prefab menu even when the game handles the pointer.");
                viewportMenu.Close();
                Press();
                point = new Point(150, 150);
                gameHost.RaiseEvent(new PointerEventArgs(InputElement.PointerMovedEvent, gameHost, pointer, gameHost, point, 0,
                    new PointerPointProperties(RawInputModifiers.RightMouseButton, PointerUpdateKind.Other), KeyModifiers.None));
                Release();
                await Flush();
                Check(!viewportMenu.IsOpen, "A camera drag must not open the context menu.");
                gameHost.RaiseEvent(new ContextRequestedEventArgs { RoutedEvent = InputElement.ContextRequestedEvent });
                await Flush();
                Check(viewportMenu.IsOpen, "Keyboard context-menu requests must remain available.");
                Console.WriteLine("PASS: routed right-click and keyboard open the viewport menu; camera dragging does not.");
            }
            finally
            {
                viewportMenu.Close();
                window.Close();
                host.Content = null;
                await gameHost.DisposeAsync();
            }
        }

    }

    private static void Check(bool condition, string message) { if (!condition) throw new Exception(message); }
    private static async Task Flush()
    {
        await Task.Delay(150);
        await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Background);
    }
}
