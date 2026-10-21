// Copyright (c) .NET Foundation and Contributors (https://dotnetfoundation.org/ & https://stride3d.net)
// Distributed under the MIT license. See the LICENSE.md file in the project root for more information.

using Avalonia.Controls;
using Avalonia.Threading;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using Stride.Assets.Editor.EntityHierarchyEditor.Game;
using Stride.Assets.Editor.GameEditor;
using Stride.Assets.Editor.ViewModels;
using Stride.Core;
using Stride.Core.Assets.Editor.Services;
using Stride.Core.Assets.Editor.ViewModels;
using Stride.Core.Mathematics;
using Stride.Engine;

internal static class EditorDocumentSmoke
{
    public static async Task Check(EntityHierarchyEditorViewModel editor, SceneViewportGame game, Window window)
    {
        var shell = (Control)window.Content!;
        shell.FindControl<MenuItem>("PreviewTabsMenu2")!.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
        if (shell.FindControl<TabControl>("PreviewTabs")!.SelectedIndex != 2)
            throw new Exception("The panel header menu did not activate References.");
        shell.FindControl<MenuItem>("PreviewTabsMenu0")!.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
        if (shell.FindControl<TabControl>("PreviewTabs")!.SelectedIndex != 0)
            throw new Exception("The panel header menu did not activate Asset preview.");
        var collection = editor.Session.EditorCollection;
        var viewport = editor.Viewport!;
        var item = editor.HierarchyRoot.InnerSubEntities.First(entity => entity.Components.OfType<ModelComponent>().Any());
        var selection = editor.SelectedContent.ToArray();
        var expectedPosition = await viewport.Controller.InvokeAsync(() =>
        {
            var camera = game.EditorServices.Get<EditorGameEntityCameraService>()!;
            camera.Frame([game.FindSubEntity(Guid.Empty, item.AssetSideEntity.Id)!]);
            var expected = camera.Position;
            camera.Frame(game.ContentScene);
            return expected;
        });
        await viewport.FrameEntity(item);
        if (await viewport.Controller.InvokeAsync(() => game.EditorServices.Get<EditorGameEntityCameraService>()!.Position) != expectedPosition
            || !editor.SelectedContent.SequenceEqual(selection))
            throw new Exception("Entity focus did not frame the requested object without changing selection.");
        var focus = window.GetVisualDescendants().OfType<Button>().Single(button => ReferenceEquals(button.Command, viewport.FrameEntityCommand) && ReferenceEquals(button.CommandParameter, item));
        if (!focus.IsEnabled) throw new Exception("The entity focus button is disabled after scene initialization.");
        var close = window.GetVisualDescendants().OfType<Button>().Single(button => button.Classes.Contains("documentClose") && ReferenceEquals(button.DataContext, editor));
        if (!ReferenceEquals(close.Command, collection.CloseEditorCommand) || !ReferenceEquals(close.CommandParameter, editor))
            throw new Exception("The document close button does not target its own editor.");
        Console.WriteLine("PASS: entity focus targets its own object without changing selection; document close button binds to its editor.");

        var otherAsset = editor.Session.AllAssets.First(asset => asset != editor.Asset
            && editor.ServiceProvider.Get<IAssetsPluginService>().GetEditorViewModelType(asset.GetType()) != null);
        collection.OpenAssetEditor(otherAsset);
        var other = collection.ActiveEditor!;
        await collection.CloseEditorAsync(other).WaitAsync(TimeSpan.FromSeconds(20));
        if (collection.ActiveEditor != editor)
            throw new Exception("Closing the active document did not select its remaining neighbour.");
        collection.OpenAssetEditor(otherAsset);
        other = collection.ActiveEditor!;
        collection.ActiveEditor = editor;
        var firstClose = collection.CloseEditorAsync(other);
        if (!ReferenceEquals(firstClose, collection.CloseEditorAsync(other)))
            throw new Exception("Repeated close requests must await the same disposal task.");
        await firstClose.WaitAsync(TimeSpan.FromSeconds(20));
        if (collection.ActiveEditor != editor || collection.Editors.Contains(other))
            throw new Exception("Closing an inactive document switched or retained the wrong editor.");
        Console.WriteLine("PASS: inactive document closure preserves the active tab; repeated close requests share one disposal.");

        var asset = editor.Asset;
        var original = new TransformationTRS(item.AssetSideEntity.Transform);
        var modified = original;
        modified.Position += new Vector3(1.2345f, 0, 0);
        viewport.CommitTransformations(new Dictionary<AbsoluteId, TransformationTRS> { [item.Id] = modified });
        await viewport.ReloadContent().WaitAsync(TimeSpan.FromSeconds(60));
        var dirtyMarker = window.GetVisualDescendants().OfType<Button>().Single(button => button.Classes.Contains("documentClose") && ReferenceEquals(button.DataContext, editor))
            .GetVisualAncestors().OfType<StackPanel>().First().GetVisualDescendants().OfType<TextBlock>().Single(text => text.Text == "*");
        if (!asset.IsDirty || !dirtyMarker.IsVisible)
            throw new Exception("The document header did not mark its unsaved edit.");
        await collection.CloseEditorAsync(editor).WaitAsync(TimeSpan.FromSeconds(20));
        if (collection.ActiveEditor != null || collection.Editors.Count != 0 || !ReferenceEquals(editor.Session.GetAssetById(asset.Id), asset))
            throw new Exception("Closing the last document did not retain its asset in the session.");
        collection.OpenAssetEditor(asset);
        var reopened = (EntityHierarchyEditorViewModel)collection.ActiveEditor!;
        if (ReferenceEquals(reopened, editor)) throw new Exception("Reopening reused a disposed editor.");
        await reopened.OpenViewport().WaitAsync(TimeSpan.FromSeconds(90));
        var reopenedItem = reopened.HierarchyRoot.InnerSubEntities.Single(entity => entity.AssetSideEntity.Id == item.AssetSideEntity.Id);
        if (reopenedItem.AssetSideEntity.Transform.Position != modified.Position)
            throw new Exception("Closing and reopening the document lost its unsaved edit.");
        editor.Session.ActionService!.Undo();
        await reopened.Viewport!.ReloadContent().WaitAsync(TimeSpan.FromSeconds(60));
        if (reopenedItem.AssetSideEntity.Transform.Position != original.Position)
            throw new Exception("Undo history was lost across document closure.");
        await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Background);
        await collection.CloseEditorAsync(reopened).WaitAsync(TimeSpan.FromSeconds(20));
        Console.WriteLine("PASS: closing/reopening a dirty scene preserves unsaved transforms and Undo; closing the last tab stops its game and keeps the session.");
    }
}
