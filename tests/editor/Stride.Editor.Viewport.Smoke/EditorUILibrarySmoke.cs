// Copyright (c) .NET Foundation and Contributors (https://dotnetfoundation.org/ & https://stride3d.net)
// Distributed under the MIT license. See the LICENSE.md file in the project root for more information.

using Avalonia.Controls;
using Avalonia.VisualTree;
using Stride.Assets.Editor.Avalonia.Views;
using Stride.Assets.Editor.UIEditor.ViewModels;
using Stride.Assets.Editor.ViewModels;
using Stride.Assets.Presentation.ViewModels;
using Stride.Assets.UI;
using Stride.Core;
using Stride.Core.Assets;
using Stride.Core.Assets.Editor.ViewModels;
using Stride.Core.Assets.Serializers;
using Stride.Core.Mathematics;
using Stride.UI;
using Canvas = Stride.UI.Panels.Canvas;
using TextBlock = Stride.UI.Controls.TextBlock;

internal static class EditorUILibrarySmoke
{
    public static async Task Check(SessionViewModel session, Window window)
    {
        var history = session.ActionService!;
        var project = session.CurrentProject!;
        var text = new TextBlock { Name = "Label", Text = "Original", Width = 120, Height = 40 };
        var widget = new Canvas { Name = "Widget", Width = 300, Height = 200, Children = { text } };
        var root = new Canvas { Name = "Source", Width = 800, Height = 600, Children = { widget } };
        var page = new UIPageAsset { Design = new UIAssetBase.UIDesign { Resolution = new Vector3(800, 600, 100) } };
        page.Hierarchy.RootParts.Add(root);
        foreach (var element in new UIElement[] { root, widget, text }) page.Hierarchy.Parts.Add(new UIElementDesign(element));
        UIPageViewModel asset;
        using (history.CreateTransaction())
            asset = (UIPageViewModel)project.AddAsset(new AssetItem("UILibrarySmoke_" + Guid.NewGuid().ToString("N"), page), project.AssetMountPoint);
        session.EditorCollection.OpenAssetEditor(asset);
        var editor = (UIPageEditorViewModel)session.EditorCollection.ActiveEditor!;
        await Initialized(editor);
        UIElementViewModel Item(UIEditorBaseViewModel owner, Guid id) => (UIElementViewModel)owner.FindPartViewModel(new AbsoluteId(owner.Asset.Id, id))!;
        void Select(UIEditorBaseViewModel owner, Guid id) { owner.ClearSelection(); owner.Select(id, false); }
        void Check(bool condition, string message) { if (!condition) throw new Exception(message); }
        async Task<UIEditorView> Initialized(UIEditorBaseViewModel owner)
        {
            await Wait(() => window.GetVisualDescendants().OfType<UIEditorView>().Any(x => x.DataContext == owner), "UI editor view");
            var view = window.GetVisualDescendants().OfType<UIEditorView>().Single(x => x.DataContext == owner);
            await view.EditorInitialization.WaitAsync(TimeSpan.FromSeconds(90));
            if (owner.Viewport?.LastException != null) throw owner.Viewport.LastException;
            Check(owner.Viewport?.SceneInitialized == true, "UI viewport did not initialize.");
            return view;
        }
        async Task ActiveRoot(UILibraryEditorViewModel owner, Guid id)
        {
            await Task.Delay(300);
            var shown = await owner.Viewport!.Controller.InvokeAsync(() => owner.Viewport.Controller
                .GetEntityByName(Stride.Assets.Editor.UIEditor.Services.UIEditorController.UIEntityName)!
                .Get<Stride.Engine.UIComponent>().Page.RootElement?.Id);
            Check(owner.ActiveRoot?.Id.ObjectId == id && shown == id, "Library active root and game page differ.");
        }

        Check(!editor.CreatePageCommand.CanExecute(null) && !editor.CreateLibraryCommand.CanExecute(null), "Empty selection enables creation.");
        Select(editor, widget.Id);
        editor.CreatePageCommand.Execute(null);
        var createdPage = (UIPageViewModel)session.AssetCollection.SelectedAssets.Single();
        Check(createdPage != asset && createdPage.Asset.Hierarchy.RootParts.Single().Id != widget.Id
            && createdPage.Asset.Hierarchy.Parts.Count == 2 && createdPage.Asset.Design.Resolution == page.Design.Resolution
            && page.Hierarchy.Parts.Values.All(x => x.Base == null), "Page creation was not an independent subtree copy.");
        history.Undo();
        Check(createdPage.IsDeleted, "Page creation Undo failed.");
        history.Redo();
        Check(!createdPage.IsDeleted, "Page creation Redo failed.");

        Select(editor, widget.Id);
        editor.CreateLibraryCommand.Execute(null);
        var library = (UILibraryViewModel)session.AssetCollection.SelectedAssets.Single();
        var libraryRootId = library.Asset.Hierarchy.RootParts.Single().Id;
        var libraryTextId = page.Hierarchy.Parts[text.Id].Base!.BasePartId;
        Select(editor, widget.Id);
        Check(library.Asset.PublicUIElements[libraryRootId] == "Widget"
            && library.Asset.Hierarchy.Parts.Count == 2 && library.Asset.Design.Resolution == page.Design.Resolution
            && page.Hierarchy.Parts[widget.Id].Base?.BasePartAsset.Id == library.Id
            && Item(editor, widget.Id).SourceLibrary == library && editor.BreakLinkToLibraryCommand.CanExecute(null), $"Library creation or source linking failed. Export={library.Asset.PublicUIElements.GetValueOrDefault(libraryRootId)}; Parts={library.Asset.Hierarchy.Parts.Count}; Resolution={library.Asset.Design.Resolution}; source base={page.Hierarchy.Parts[widget.Id].Base?.BasePartAsset.Id}; expected={library.Id}; VM library={Item(editor, widget.Id).SourceLibrary?.Id}; command={editor.BreakLinkToLibraryCommand.CanExecute(null)}.");
        history.Undo();
        Check(library.IsDeleted && page.Hierarchy.Parts[widget.Id].Base == null && page.Hierarchy.Parts[text.Id].Base == null
            && !editor.OpenLibraryEditorCommand.CanExecute(null), "Library creation Undo did not restore source links/commands.");
        history.Redo();
        Select(editor, widget.Id);
        Check(!library.IsDeleted && Item(editor, widget.Id).SourceLibrary == library && editor.OpenLibraryEditorCommand.CanExecute(null), "Library creation Redo failed.");
        editor.SelectLibraryCommand.Execute(null);
        Check(session.AssetCollection.SelectedAssets.Single() == library, "Select library did not select the asset.");
        Select(editor, widget.Id);
        editor.OpenLibraryEditorCommand.Execute(null);
        var libraryEditor = (UILibraryEditorViewModel)session.EditorCollection.ActiveEditor!;
        Check(libraryEditor.Asset == library, "Open library command opened the wrong asset.");
        var libraryView = await Initialized(libraryEditor);
        Check(libraryView.FindControl<TreeView>("HierarchyTreeView")!.ItemCount == 1, "Library hierarchy was not populated.");
        await ActiveRoot(libraryEditor, libraryRootId);

        Item(libraryEditor, libraryRootId).Name = "RenamedWidget";
        Check(library.Asset.PublicUIElements[libraryRootId] == "RenamedWidget"
            && editor.Factories.OfType<UIElementFromLibrary>().Single(x => x.Library == library && x.Id == libraryRootId).Name == "RenamedWidget",
            "Renaming a root did not update its export and other editors' palettes.");
        history.Undo();
        Check(library.Asset.PublicUIElements[libraryRootId] == "Widget" && Item(libraryEditor, libraryRootId).Name == "Widget", "Export rename Undo failed.");
        history.Redo();
        Check(library.Asset.PublicUIElements[libraryRootId] == "RenamedWidget", "Export rename Redo failed.");
        history.Undo();
        Item(libraryEditor, libraryTextId).Name = "PrivateChild";
        Check(!library.Asset.PublicUIElements.ContainsKey(libraryTextId), "A non-root element became an export.");
        history.Undo();
        var factory = new StandardUIElementFactory("Extra", "Control", typeof(TextBlock), () => new TextBlock { Name = "Extra", Text = "Extra" });
        libraryEditor.InsertFactory(factory, libraryEditor.HierarchyRoot, -1);
        var extra = library.Asset.Hierarchy.RootParts.Last();
        Check(library.Asset.PublicUIElements[extra.Id] == "Extra" && editor.Factories.OfType<UIElementFromLibrary>().Any(x => x.Id == extra.Id), "Adding a library root did not export it.");
        await ActiveRoot(libraryEditor, extra.Id);
        var staleEntry = editor.Factories.OfType<UIElementFromLibrary>().Single(x => x.Id == extra.Id);
        Item(libraryEditor, extra.Id).Name = " ";
        Check(!library.Asset.PublicUIElements.ContainsKey(extra.Id) && !editor.Factories.OfType<UIElementFromLibrary>().Any(x => x.Id == extra.Id)
            && !staleEntry.CanCreateIn(page), "Blank root name did not unexport it or invalidate its old palette entry.");
        history.Undo();
        Check(library.Asset.PublicUIElements[extra.Id] == "Extra", "Unexport Undo failed.");
        libraryEditor.MoveElements([Item(libraryEditor, extra.Id)], Item(libraryEditor, libraryRootId), -1);
        Check(!library.Asset.PublicUIElements.ContainsKey(extra.Id), "Reparented root remained exported.");
        history.Undo();
        Check(library.Asset.PublicUIElements[extra.Id] == "Extra", "Reparent Undo failed to restore the export.");
        Select(libraryEditor, extra.Id);
        Check(libraryEditor.DuplicateSelectionCommand.CanExecute(null), "Library root cannot be duplicated.");
        libraryEditor.DuplicateSelectionCommand.Execute(null);
        Check(library.Asset.Hierarchy.RootParts.Count == 3 && library.Asset.PublicUIElements.Count == 3, "Root duplication did not create a new export.");
        history.Undo();
        Select(libraryEditor, extra.Id);
        libraryEditor.DeleteCommand.Execute(null);
        Check(library.Asset.Hierarchy.RootParts.Count == 1 && !library.Asset.PublicUIElements.ContainsKey(extra.Id), "Root deletion did not remove its export.");
        await ActiveRoot(libraryEditor, libraryRootId);
        history.Undo();
        Check(library.Asset.PublicUIElements[extra.Id] == "Extra", "Root deletion Undo failed.");
        history.Redo();
        Check(!library.Asset.PublicUIElements.ContainsKey(extra.Id), "Root deletion Redo failed.");
        history.Undo();
        Select(libraryEditor, libraryRootId);
        await ActiveRoot(libraryEditor, libraryRootId);

        var entry = editor.Factories.OfType<UIElementFromLibrary>().Single(x => x.Library == library && x.Id == libraryRootId);
        Check(!entry.CanCreateIn(library.Asset), "Self-referencing library instances are allowed.");
        editor.InsertFactory(entry, Item(editor, root.Id), -1);
        var instance = root.Children.Last();
        Check(instance.Id != widget.Id && instance.Id != libraryRootId && Item(editor, instance.Id).SourceLibrary == library,
            "Palette instantiation did not create an independent linked subtree.");
        history.Undo();
        Select(editor, widget.Id);
        editor.BreakLinkToLibraryCommand.Execute(null);
        Check(page.Hierarchy.Parts[widget.Id].Base == null && page.Hierarchy.Parts[text.Id].Base == null
            && Item(editor, widget.Id).SourceLibrary == null && !editor.OpenLibraryEditorCommand.CanExecute(null), "Break link did not detach the whole subtree.");
        libraryEditor.UpdateProperties(libraryTextId, new Dictionary<string, object> { [nameof(TextBlock.Text)] = "Changed" });
        Check(((TextBlock)page.Hierarchy.Parts[text.Id].UIElement).Text == "Original", "Detached subtree still inherits library changes.");
        history.Undo();
        history.Undo();
        Check(Item(editor, widget.Id).SourceLibrary == library && page.Hierarchy.Parts[text.Id].Base != null
            && editor.OpenLibraryEditorCommand.CanExecute(null), "Break link Undo failed.");
        libraryEditor.UpdateProperties(libraryTextId, new Dictionary<string, object> { [nameof(TextBlock.Text)] = "Inherited" });
        Check(((TextBlock)page.Hierarchy.Parts[text.Id].UIElement).Text == "Inherited", "Undo did not restore inheritance.");
        history.Undo();
        using (var stream = new MemoryStream())
        {
            AssetFileSerializer.Default.Save(stream, library.Asset, null);
            stream.Position = 0;
            var serialized = (UILibraryAsset)AssetFileSerializer.Default.Load(stream, null, null, false, out _, out _);
            Check(serialized.PublicUIElements.Count == 2 && serialized.PublicUIElements[libraryRootId] == "Widget", "Library exports did not serialize.");
        }

        Select(libraryEditor, libraryRootId);
        libraryEditor.Select(extra.Id, true);
        Check(!libraryEditor.CreatePageCommand.CanExecute(null) && libraryEditor.CreateLibraryCommand.CanExecute(null), "Multiple roots have incorrect creation commands.");
        libraryEditor.CreateLibraryCommand.Execute(null);
        var nestedLibrary = (UILibraryViewModel)session.AssetCollection.SelectedAssets.Single();
        Check(nestedLibrary.Asset.Hierarchy.RootParts.Count == 2 && nestedLibrary.Asset.PublicUIElements.Count == 2, "Creating a library from multiple roots failed.");
        var cycle = new UIElementFromLibrary(session.ServiceProvider, library, libraryRootId);
        Check(!cycle.CanCreateIn(nestedLibrary.Asset), "Circular library inheritance was not rejected.");
        history.Undo();
        Check(nestedLibrary.IsDeleted, "Nested library creation Undo failed.");
        await session.EditorCollection.CloseEditorAsync(libraryEditor).WaitAsync(TimeSpan.FromSeconds(15));
        session.EditorCollection.OpenAssetEditor(library);
        var reopened = (UILibraryEditorViewModel)session.EditorCollection.ActiveEditor!;
        await Initialized(reopened);
        await ActiveRoot(reopened, libraryRootId);
        Select(editor, widget.Id);
        editor.BreakLinkToLibraryCommand.Execute(null);
        await session.EditorCollection.CloseEditorAsync(editor).WaitAsync(TimeSpan.FromSeconds(15));
        history.Undo();
        reopened.UpdateProperties(libraryTextId, new Dictionary<string, object> { [nameof(TextBlock.Text)] = "ClosedEditorInheritance" });
        Check(((TextBlock)page.Hierarchy.Parts[text.Id].UIElement).Text == "ClosedEditorInheritance", "Undo did not restore inheritance with the source editor closed.");
        history.Undo();
        await session.EditorCollection.CloseEditorAsync(reopened).WaitAsync(TimeSpan.FromSeconds(15));
        Console.WriteLine("PASS: UI Library viewport, creation/linking, exports/palettes, root operations, inheritance, Undo/Redo, serialization and reopen/close.");
    }

    private static async Task Wait(Func<bool> condition, string operation)
    {
        for (var i = 0; i < 200; i++) { if (condition()) return; await Task.Delay(50); }
        throw new TimeoutException(operation);
    }
}
