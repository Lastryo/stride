// Copyright (c) .NET Foundation and Contributors (https://dotnetfoundation.org/ & https://stride3d.net)
// Distributed under the MIT license. See the LICENSE.md file in the project root for more information.

using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Stride.Assets.Editor.Avalonia.Views;
using Stride.Assets.Editor.SceneEditor;
using Stride.Assets.Editor.ViewModels;
using Stride.Assets.Models;
using Stride.Assets.Presentation.ViewModels;
using Stride.Core.Assets.Presentation.ViewModels;
using Stride.Core.Presentation.Avalonia.Controls;
using Stride.Core.Presentation.Avalonia.Windows;
using Stride.Engine;
using Stride.Core.Extensions;

internal static class EditorHierarchySmoke
{
    public static async Task Check(EntityHierarchyEditorViewModel editor, Window window)
    {
        var shell = (Control)window.Content!;
        var state = EditorLayout.GetState(shell)!;
        var originalWidth = state.HierarchyWidth;
        var hierarchy = shell.GetVisualDescendants().OfType<EntityHierarchyEditorView>().Single();
        var tree = hierarchy.FindControl<TreeView>("HierarchyTreeView")!;
        var solution = shell.GetVisualDescendants().OfType<Stride.GameStudio.Avalonia.Views.SolutionExplorerView>().Single().FindControl<TreeView>("SolutionTreeView")!;
        var expanded = new List<TreeViewItem>();
        try
        {
            foreach (var width in new[] { 210, 140, 300 })
            {
                state.HierarchyWidth = new GridLength(width);
                await Layout(window);
                var focusButtons = tree.GetVisualDescendants().OfType<Button>().Where(button => button.Classes.Contains("hierarchyFocus") && button.IsEffectivelyVisible).ToArray();
                if (focusButtons.Length == 0) throw new Exception("No entity focus buttons were realized.");
                foreach (var button in focusButtons)
                {
                    var position = button.TranslatePoint(default, tree)!.Value;
                    if (position.X < 0 || position.X + button.Bounds.Width > tree.Bounds.Width + 1)
                        throw new Exception($"The focus action is clipped at hierarchy width {width}.");
                    var row = button.GetVisualAncestors().OfType<TreeViewItem>().First();
                    if (button.Command != editor.Viewport!.FrameEntityCommand || !ReferenceEquals(button.CommandParameter, row.DataContext))
                        throw new Exception("The focus action lost its entity binding.");
                    var name = row.GetVisualDescendants().OfType<TextBlock>().First(text => text.Classes.Contains("hierarchyName"));
                    var oldText = name.Text;
                    try
                    {
                        name.SetCurrentValue(TextBlock.TextProperty, new string('W', 200));
                        await Layout(window);
                        var after = button.TranslatePoint(default, tree)!.Value;
                        if (Math.Abs(after.X - position.X) > 1 || name.Bounds.Right > button.TranslatePoint(default, (Visual)name.Parent!)!.Value.X + 1)
                            throw new Exception("A long entity name displaced the focus column.");
                    }
                    finally { name.SetCurrentValue(TextBlock.TextProperty, oldText); }
                }
                if (tree.GetVisualDescendants().OfType<Button>().Any(button => button.Classes.Contains("hierarchyUnavailableAction") && button.IsEnabled))
                    throw new Exception("An unavailable lock/load action is enabled.");
                Console.WriteLine($"PASS: hierarchy actions fit at {width} DIP, including long entity names.");
            }

            var project = editor.Session.AllPackages.OfType<ProjectViewModel>().First(package => !package.Package.IsSystem);
            if (!project.Content.Select(item => ((SessionObjectViewModel)item).Name).SequenceEqual(new[] { "Assets", "Code", "Dependencies" }))
                throw new Exception("The project tree does not contain Assets, Code and Dependencies in order.");
            if (!project.Dependencies.Content.Select(reference => reference.Name).Order().SequenceEqual(project.PackageContainer.DirectDependencies.Select(reference => reference.Name).Order()))
                throw new Exception("The dependency tree differs from the package's actual dependencies.");
            for (var i = 0; i < 3; i++)
            {
                foreach (var item in solution.GetVisualDescendants().OfType<TreeViewItem>().Where(item => !item.IsExpanded).ToArray())
                {
                    item.IsExpanded = true;
                    expanded.Add(item);
                }
                await Layout(window);
            }
            foreach (var model in new object[] { project.AssetMountPoint, project.Code, project.Dependencies })
                if (!solution.GetVisualDescendants().OfType<TreeViewItem>().Any(item => ReferenceEquals(item.DataContext, model) && item.IsEffectivelyVisible))
                    throw new Exception("An expanded project branch is absent from Solution explorer.");
            Console.WriteLine("PASS: expanded Solution explorer shows Assets, Code and the real dependency list.");

            var headerGrid = hierarchy.GetVisualDescendants().OfType<Grid>()
                .FirstOrDefault(g => g.Children.OfType<TextBlock>().Any(t => t.Text == "Entities"));
            if (headerGrid == null) throw new Exception("Entities column header grid is missing.");
            var headerIcons = headerGrid.Children.OfType<Image>().ToArray();
            if (headerIcons.Length != 3) throw new Exception($"Expected 3 column header icons (load, lock, focus), found {headerIcons.Length}.");
            if (headerIcons.Any(img => !img.IsEffectivelyVisible)) throw new Exception("Hierarchy column header icons are not visible.");
            Console.WriteLine("PASS: hierarchy column headers rendered with visibility/load/lock/focus icons.");

            var entityItem = editor.HierarchyRoot.InnerSubEntities.First();
            if (entityItem.IsLocked) throw new Exception("Entity should initially not be locked.");
            if (!entityItem.IsLoaded) throw new Exception("Entity should initially be loaded.");
            if (!entityItem.IsSelectable) throw new Exception("Entity should initially be selectable.");

            var lockButtons = tree.GetVisualDescendants().OfType<Button>().Where(b => b.Classes.Contains("hierarchyLock")).ToArray();
            var entityLockButton = lockButtons.First(b => ReferenceEquals(b.DataContext, entityItem));
            var lockImages = entityLockButton.GetVisualDescendants().OfType<Image>().ToArray();
            if (lockImages.Length != 2) throw new Exception($"Expected 2 images inside lock button panel, found {lockImages.Length}.");
            if (lockImages[0].IsVisible || !lockImages[1].IsVisible)
                throw new Exception("Initially unlocked entity button has incorrect icon visibility.");

            entityLockButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await Layout(window);
            if (!entityItem.IsLocked) throw new Exception("Clicking lock button failed to set IsLocked.");
            if (!lockImages[0].IsVisible || lockImages[1].IsVisible)
                throw new Exception("Lock button icon did not switch to closed lock when entity was locked.");

            entityLockButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await Layout(window);
            if (entityItem.IsLocked) throw new Exception("Clicking lock button a second time failed to unlock entity.");
            if (lockImages[0].IsVisible || !lockImages[1].IsVisible)
                throw new Exception("Lock button icon did not switch back to open lock when entity was unlocked.");

            var root = editor.HierarchyRoot;
            root.LockCommand.Execute(null);
            if (!root.IsLocked) throw new Exception("Scene root failed to lock.");
            if (!entityItem.IsLocked || entityItem.IsSelectable) throw new Exception("Scene root lock did not propagate to child entity.");

            root.LockCommand.Execute(null);
            if (root.IsLocked || entityItem.IsLocked || !entityItem.IsSelectable) throw new Exception("Scene root unlock did not propagate to child entity.");

            root.LoadCommand.Execute(null);
            if (root.IsLoaded) throw new Exception("Scene root failed to unload.");
            if (entityItem.IsLoaded || entityItem.IsSelectable) throw new Exception("Scene root unload did not propagate to child entity.");
            if (editor.Viewport?.Controller != null)
            {
                await editor.Viewport.Controller.InvokeAsync(() => { });
                var childrenCountUnloaded = await editor.Viewport.Controller.InvokeAsync(() => editor.Viewport.Controller.Game.ContentScene.Children.Count);
                if (childrenCountUnloaded != 0) throw new Exception("Unloaded scene must be detached from viewport ContentScene.");
            }

            root.LoadCommand.Execute(null);
            if (!root.IsLoaded || !entityItem.IsLoaded || !entityItem.IsSelectable) throw new Exception("Scene root load did not propagate to child entity.");
            if (editor.Viewport?.Controller != null)
            {
                await editor.Viewport.Controller.InvokeAsync(() => { });
                var childrenCountLoaded = await editor.Viewport.Controller.InvokeAsync(() => editor.Viewport.Controller.Game.ContentScene.Children.Count);
                if (childrenCountLoaded == 0) throw new Exception("Loaded scene must be reattached to viewport ContentScene.");
            }
            Console.WriteLine("PASS: hierarchy lock and load state toggles and propagates recursively, and attaches/detaches viewport scene.");

            if (editor.Viewport?.Controller != null)
            {
                var selectionService = await editor.Viewport.Controller.InvokeAsync(() => editor.Viewport.Controller.GetSelectionService());
                var originalSelected = editor.SelectedContent.ToArray();
                try
                {
                    editor.SelectedContent.Clear();
                    editor.SelectedContent.Add(entityItem);
                    await editor.Viewport.Controller.InvokeAsync(() => { });
                    var rootsBeforeLock = await editor.Viewport.Controller.InvokeAsync(() => selectionService.GetSelectedRootIds());
                    if (!rootsBeforeLock.Any(id => id.ObjectId == entityItem.Id.ObjectId))
                        throw new Exception("Entity was not selected in selection service when unlocked.");

                    entityItem.IsLocked = true;
                    await editor.Viewport.Controller.InvokeAsync(() => { });
                    var rootsAfterLock = await editor.Viewport.Controller.InvokeAsync(() => selectionService.GetSelectedRootIds());
                    if (rootsAfterLock.Any(id => id.ObjectId == entityItem.Id.ObjectId))
                        throw new Exception("Locked entity must be excluded from selection service root IDs.");
                    Console.WriteLine("PASS: locked entity is excluded from viewport selection service.");
                }
                finally
                {
                    entityItem.IsLocked = false;
                    editor.SelectedContent.Clear();
                    foreach (var item in originalSelected) editor.SelectedContent.Add(item);
                    await editor.Viewport.Controller.InvokeAsync(() => { });
                }
            }

            // Drag & Drop and Delete tests
            editor.SelectedContent.Clear();
            if (editor.CanDelete()) throw new Exception("CanDelete should be false when nothing is selected.");

            editor.SelectedContent.Add(editor.HierarchyRoot);
            if (editor.CanDelete()) throw new Exception("CanDelete should be false when root scene is selected.");

            solution.Focus();
            editor.CreateEntityInRootCommand.Execute(EntityFactoryKind.EmptyEntity);
            await Layout(window);

            var createdEntity = editor.HierarchyRoot.InnerSubEntities.FirstOrDefault(x => x.Name == "Entity");
            if (createdEntity == null) throw new Exception("Created entity not found in hierarchy root.");
            var createdId = createdEntity.Id;
            var targetId = entityItem.Id;
            await CheckViewportDeletion(editor, window, createdId);
            await CheckRootReordering(editor, window);
            var targetEntity = (EntityViewModel)editor.FindPartViewModel(targetId)!;
            createdEntity = (EntityViewModel)editor.FindPartViewModel(createdId)!;

            // 1. Test move entity into another entity (reparenting)
            editor.MoveItems(new[] { createdEntity }, targetEntity, DropPosition.Into);
            await Layout(window);

            createdEntity = (EntityViewModel)editor.FindPartViewModel(createdId)!;
            targetEntity = (EntityViewModel)editor.FindPartViewModel(targetId)!;
            if (createdEntity == null || !targetEntity.Children.Contains(createdEntity) || createdEntity.Parent != targetEntity)
                throw new Exception("Moving entity into parent entity failed.");

            // Verify cycle prevention
            if (editor.CanMoveItems(new[] { targetEntity }, createdEntity, DropPosition.Into, out _))
                throw new Exception("CanMoveItems should disallow parenting an entity under its own child.");

            // 2. Test move entity back to root
            editor.MoveItems(new[] { createdEntity }, editor.HierarchyRoot, DropPosition.Into);
            await Layout(window);

            createdEntity = editor.FindPartViewModel(createdId) as EntityViewModel;
            if (createdEntity == null || createdEntity.Parent != editor.HierarchyRoot)
                throw new Exception("Moving entity back to root failed.");

            Console.WriteLine("PASS: hierarchy drag & drop moves entities between parents and to scene root with cycle detection.");

            // 3. Test delete entity and undo
            editor.SelectedContent.Clear();
            editor.SelectedContent.Add(createdEntity);
            if (!editor.CanDelete()) throw new Exception("CanDelete should be true for created entity.");

            await editor.DeleteItems(new[] { createdEntity }, askConfirmation: false);
            await Layout(window);

            if (editor.FindPartViewModel(createdId) != null)
                throw new Exception("Entity was not deleted from hierarchy.");

            // Test Undo
            editor.Session.ActionService!.Undo();
            await Layout(window);

            var restoredEntity = editor.FindPartViewModel(createdId) as EntityViewModel;
            if (restoredEntity == null)
                throw new Exception("Undo failed to restore deleted entity.");

            // Clean up: delete the temporary entity again
            await editor.DeleteItems(new[] { restoredEntity }, askConfirmation: false);
            await Layout(window);

            Console.WriteLine("PASS: hierarchy delete and undo restore entity state in asset graph and tree.");

            // 4. Test delete confirmation settings
            var originalAsk = SceneEditorSettings.AskBeforeDeletingEntities.GetValue();
            SceneEditorSettings.AskBeforeDeletingEntities.SetValue(true);
            if (!SceneEditorSettings.AskBeforeDeletingEntities.GetValue())
                throw new Exception("AskBeforeDeletingEntities should be true.");

            SceneEditorSettings.AskBeforeDeletingEntities.SetValue(false);
            if (SceneEditorSettings.AskBeforeDeletingEntities.GetValue())
                throw new Exception("AskBeforeDeletingEntities should be false.");

            SceneEditorSettings.AskBeforeDeletingEntities.SetValue(originalAsk);
            Console.WriteLine("PASS: delete confirmation settings toggle correctly.");

            // 5. Test cross-asset drag & drop
            var modelAsset = editor.Session.AllAssets.FirstOrDefault(a => a.Asset is ModelAsset || a.Asset is IModelAsset);
            if (modelAsset != null)
            {
                if (!editor.CanDropAssets(new[] { modelAsset }, editor.HierarchyRoot, DropPosition.Into, out var msg))
                    throw new Exception($"CanDropAssets failed for model asset: {msg}");

                solution.Focus();
                var droppedEntities = editor.DropAssets(new[] { modelAsset }, editor.HierarchyRoot, DropPosition.Into);
                await Layout(window);

                if (droppedEntities.Count == 0)
                    throw new Exception("DropAssets did not create an entity.");

                var droppedEntity = droppedEntities[0];
                if (!droppedEntity.AssetSideEntity.Components.OfType<ModelComponent>().Any())
                    throw new Exception("Dropped model asset entity should contain a ModelComponent.");

                await CheckViewportDeletion(editor, window, droppedEntity.Id);
                droppedEntity = (EntityViewModel)editor.FindPartViewModel(droppedEntity.Id)!;

                if (editor.Viewport != null)
                {
                    await editor.Viewport.ReloadContent();
                    var gameEntity = await editor.Viewport.Controller.InvokeAsync(() => editor.Viewport.Controller.Game.FindSubEntity(Guid.Empty, droppedEntity.Id.ObjectId));
                    if (gameEntity == null)
                        throw new Exception("Dropped model asset entity was not found in 3D viewport scene.");
                    var modelComp = gameEntity.Get<ModelComponent>();
                    if (modelComp == null || modelComp.Model == null || modelComp.Model.Meshes.Count == 0)
                        throw new Exception("Dropped model entity in 3D viewport does not have loaded Model/Meshes.");
                }

                // Test Undo
                editor.Session.ActionService!.Undo();
                await Layout(window);

                if (editor.HierarchyRoot.Children.Contains(droppedEntity))
                    throw new Exception("Undo failed to remove dropped asset entity.");

                // Test Redo
                editor.Session.ActionService!.Redo();
                await Layout(window);

                var restoredDropped = editor.HierarchyRoot.Children.OfType<EntityViewModel>().FirstOrDefault(c => c.Id == droppedEntity.Id);
                if (restoredDropped == null)
                    throw new Exception("Redo failed to restore dropped asset entity.");

                // Clean up
                await editor.DeleteItems(new[] { restoredDropped }, askConfirmation: false);
                await Layout(window);
                if (editor.Viewport != null) await editor.Viewport.ReloadContent();

                Console.WriteLine("PASS: cross-asset drag & drop creates entity with component and supports undo/redo.");

                solution.Focus();
                var batch = editor.DropAssets(new[] { modelAsset, modelAsset }, editor.HierarchyRoot, DropPosition.Into);
                await Layout(window);
                if (batch.Count != 2 || batch.Any(entity => !ReferenceEquals(entity, editor.FindPartViewModel(entity.Id))
                    || !editor.SelectedContent.Contains(entity) || !editor.SelectedItems.Contains(entity))
                    || !editor.DeleteCommand.CanExecute(null) || !((Control)editor.Viewport!.EditorHost!).IsFocused)
                    throw new Exception("Dropping multiple assets did not activate a current selection for every new entity.");
                await editor.Viewport.ReloadContent();
                var batchSelectedIds = await editor.Viewport.Controller.InvokeAsync(() => editor.Viewport.Controller.GetSelectionService().GetSelectedRootIds());
                if (!batchSelectedIds.ToHashSet().SetEquals(batch.Select(entity => entity.Id)))
                    throw new Exception("The viewport selection differs from the newly dropped batch.");
                await editor.DeleteItems(batch, askConfirmation: false);
                await editor.Viewport.ReloadContent();
                Console.WriteLine("PASS: dropping multiple assets selects current tree items and game entities and activates Delete without reselection.");
            }
            await EditorSelectionToolsSmoke.Check(editor, window);
        }
        finally
        {
            foreach (var item in expanded) item.IsExpanded = false;
            state.HierarchyWidth = originalWidth;
            await Layout(window);
        }
    }

    private static async Task CheckViewportDeletion(EntityHierarchyEditorViewModel editor, Window window, Stride.Core.AbsoluteId entityId)
    {
        var lifetime = (IClassicDesktopStyleApplicationLifetime)Application.Current!.ApplicationLifetime!;
        var viewport = (Control)editor.Viewport!.EditorHost!;
        var originalAsk = SceneEditorSettings.AskBeforeDeletingEntities.GetValue();
        SceneEditorSettings.AskBeforeDeletingEntities.SetValue(true);
        try
        {
            CheckSelection();
            await WaitForGame(() => editor.Viewport.Controller.GetSelectionService().GetSelectedRootIds().Contains(entityId),
                "A new entity did not become selected in the viewport.");
            CheckSelection();

            await PressDelete();
            var cancelled = await Dialog();
            if (cancelled.Owner != window || editor.FindPartViewModel(entityId) == null)
                throw new Exception("Viewport Delete did not wait for modal confirmation.");
            cancelled.ButtonCommand.Execute(2);
            await Layout(window);
            if (editor.FindPartViewModel(entityId) == null) throw new Exception("Cancelling viewport Delete removed the entity.");

            await PressDelete();
            (await Dialog()).ButtonCommand.Execute(1);
            for (var attempt = 0; attempt < 200 && editor.FindPartViewModel(entityId) != null; attempt++)
                await Task.Delay(25);
            if (editor.FindPartViewModel(entityId) != null) throw new Exception("Confirming viewport Delete did not remove the entity.");
            await WaitForGame(() => editor.Viewport.Controller.FindGameSidePart(entityId) == null,
                "Deleted entity remains in the viewport scene.");

            editor.Session.ActionService!.Undo();
            await Layout(window);
            if (editor.FindPartViewModel(entityId) == null) throw new Exception("Undo did not restore the entity deleted from the viewport.");
            Console.WriteLine("PASS: a new entity receives focus and viewport selection without reselection; Delete opens confirmation, cancel preserves it, confirm removes it, and undo restores it.");
        }
        finally { SceneEditorSettings.AskBeforeDeletingEntities.SetValue(originalAsk); }

        async Task WaitForGame(Func<bool> condition, string failure)
        {
            var timer = System.Diagnostics.Stopwatch.StartNew();
            while (timer.Elapsed < TimeSpan.FromSeconds(45))
            {
                if (await editor.Viewport.Controller.InvokeAsync(condition).WaitAsync(TimeSpan.FromSeconds(5))) return;
                await Task.Delay(25);
            }
            throw new TimeoutException(failure);
        }

        void CheckSelection()
        {
            var current = editor.FindPartViewModel(entityId);
            if (current == null || editor.SelectedContent.Count != 1 || !editor.SelectedContent.Contains(current)
                || editor.SelectedItems.Count != 1 || !editor.SelectedItems.Contains((EntityViewModel)current)
                || !editor.DeleteCommand.CanExecute(null))
                throw new Exception("A new entity is highlighted without an active, current selection and Delete command.");
            if (!viewport.IsFocused) throw new Exception("A new entity is selected, but keyboard focus remains outside the scene.");
        }

        async Task PressDelete()
        {
            for (var attempt = 0; attempt < 100 && !viewport.IsFocused; attempt++)
                await Task.Delay(25);
            if (!viewport.IsFocused) throw new Exception("The viewport did not receive keyboard focus.");
            viewport.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Delete });
            viewport.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyUpEvent, Key = Key.Delete });
        }
        async Task<CheckedMessageBox> Dialog()
        {
            for (var attempt = 0; attempt < 200; attempt++)
            {
                if (lifetime.Windows.OfType<CheckedMessageBox>().FirstOrDefault(dialog => dialog.IsVisible) is { } dialog)
                    return dialog;
                await Task.Delay(25);
            }
            throw new TimeoutException("Viewport Delete did not open a confirmation dialog.");
        }
    }

    private static async Task CheckRootReordering(EntityHierarchyEditorViewModel editor, Window window)
    {
        var originalOrder = editor.Asset.Asset.Hierarchy.RootParts.Select(entity => entity.Id).ToArray();
        if (originalOrder.Length < 2) throw new Exception("Reordering needs at least two root entities.");

        var firstId = editor.HierarchyRoot.InnerSubEntities.First(entity => entity.Id.ObjectId == originalOrder[0]).Id;
        var lastId = editor.HierarchyRoot.InnerSubEntities.First(entity => entity.Id.ObjectId == originalOrder[^1]).Id;
        foreach (var position in new[] { DropPosition.After, DropPosition.Before, DropPosition.Into })
        {
            var movedId = position == DropPosition.Before ? lastId : firstId;
            var target = position == DropPosition.Into
                ? editor.HierarchyRoot
                : (EntityHierarchyItemViewModel)editor.FindPartViewModel(position == DropPosition.Before ? firstId : lastId)!;
            var expectedOrder = position == DropPosition.Before
                ? new[] { originalOrder[^1] }.Concat(originalOrder.SkipLast(1)).ToArray()
                : originalOrder.Skip(1).Append(originalOrder[0]).ToArray();
            editor.MoveItems(new[] { (EntityViewModel)editor.FindPartViewModel(movedId)! }, target, position);
            await AssertOrder(expectedOrder);

            editor.Session.ActionService!.Undo();
            await AssertOrder(originalOrder);
            editor.Session.ActionService.Redo();
            await AssertOrder(expectedOrder);
            editor.Session.ActionService.Undo();
            await AssertOrder(originalOrder);
        }
        Console.WriteLine("PASS: root sibling reordering before/after and into root preserves order and supports undo/redo.");

        async Task AssertOrder(Guid[] expected)
        {
            await Layout(window);
            if (!editor.Asset.Asset.Hierarchy.RootParts.Select(entity => entity.Id).SequenceEqual(expected))
                throw new Exception("Root entity order differs from the expected order after moving or undo/redo.");
            if (!editor.HierarchyRoot.Children.OfType<EntityViewModel>().Select(entity => entity.Id.ObjectId).SequenceEqual(expected))
                throw new Exception("Hierarchy tree order differs from the asset root order after moving or undo/redo.");
        }
    }

    private static async Task Layout(Window window)
    {
        await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Background);
        window.UpdateLayout();
    }
}
