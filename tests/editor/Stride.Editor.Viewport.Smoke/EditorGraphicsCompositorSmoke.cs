// Copyright (c) .NET Foundation and Contributors (https://dotnetfoundation.org/ & https://stride3d.net)
// Distributed under the MIT license. See the LICENSE.md file in the project root for more information.

using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Stride.Assets.Editor.Avalonia.Controls;
using Stride.Assets.Editor.Avalonia.Views;
using Stride.Assets.Editor.GraphicsCompositorEditor.ViewModels;
using Stride.Assets.Editor.ViewModels;
using Stride.Assets.Presentation.ViewModels;
using Stride.Assets.Rendering;
using Stride.Core.Assets;
using Stride.Core.Assets.Editor.ViewModels;
using Stride.Rendering;
using Stride.Rendering.Compositing;

internal static class EditorGraphicsCompositorSmoke
{
    public static async Task Check(SessionViewModel session, Window window)
    {
        var project = session.CurrentProject ?? throw new Exception("No current project.");
        var history = session.ActionService!;
        var stage = new RenderStage { Name = "Opaque", EffectSlotName = "Main" };
        var renderer = new ForwardRenderer { OpaqueRenderStage = stage };
        var compositor = new GraphicsCompositorAsset { Game = renderer };
        compositor.SharedRenderers.Add(renderer);
        compositor.RenderStages.Add(stage);
        GraphicsCompositorViewModel asset;
        using (history.CreateTransaction())
            asset = (GraphicsCompositorViewModel)project.AddAsset(new AssetItem("CompositorSmoke_" + Guid.NewGuid().ToString("N"), compositor), project.AssetMountPoint);
        var transactionCount = history.RetrieveAllTransactions().Count();
        session.EditorCollection.OpenAssetEditor(asset);
        var editor = session.EditorCollection.ActiveEditor as GraphicsCompositorEditorViewModel
            ?? throw new Exception("Opening a compositor did not create its editor.");
        await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Background);
        var view = window.GetVisualDescendants().OfType<GraphicsCompositorEditorView>().Single();
        if (view.FindControl<ListBox>("RenderStagesList")?.ItemCount != 1
            || !view.GetVisualDescendants().OfType<GraphicsCompositorGraph>().Any()
            || editor.Blocks.Count != 2)
            throw new Exception("The compositor panels or renderer graph were not populated.");

        // Realizing formatted labels must not write their text back or create undo transactions.
        if (stage.Name != "Opaque" || stage.EffectSlotName != "Main" || history.RetrieveAllTransactions().Count() != transactionCount)
            throw new Exception("Opening the compositor wrote display text back into the asset.");
        var stagesList = view.FindControl<ListBox>("RenderStagesList")!;
        if (!stagesList.GetVisualDescendants().OfType<TextBlock>()
            .Any(text => text.Inlines?.OfType<Run>().Any(run => run.Text == " (Slot Main)") == true))
            throw new Exception("The render stage effect slot label was not realized.");
        if (!ReferenceEquals(stagesList.SelectedItems, editor.SelectedRenderStages)
            || !ReferenceEquals(view.FindControl<ListBox>("RenderFeaturesList")!.SelectedItems, editor.SelectedRenderFeatures)
            || !ReferenceEquals(view.FindControl<ListBox>("CameraSlotsList")!.SelectedItems, editor.SelectedCameraSlots))
            throw new Exception("The compositor lists replaced the view model selection collections.");
        stagesList.SelectedItem = editor.RenderStages[0];
        if (!editor.SelectedRenderStages.Contains(editor.RenderStages[0]))
            throw new Exception("The stage list did not update the shared selection collection.");
        stagesList.SelectedItems!.Clear();

        editor.AddNewRenderStageCommand.Execute(null);
        if (compositor.RenderStages.Count != 2 || editor.RenderStages.Count != 2 || !asset.IsDirty)
            throw new Exception("Creating a render stage did not update the asset and editor.");
        history.Undo();
        if (editor.RenderStages.Count != 1) throw new Exception("Undo did not remove the created stage.");
        editor.RenderStages[0].Name = "Renamed";
        history.Undo();
        if (stage.Name != "Opaque" || editor.RenderStages[0].Name != "Opaque") throw new Exception("Stage renaming did not undo through Quantum.");

        editor.AddNewCameraSlotCommand.Execute(null);
        editor.CameraSlots[0].Name = "Camera";
        history.Undo();
        history.Undo();
        if (compositor.Cameras.Count != 0 || editor.CameraSlots.Count != 0) throw new Exception("Camera slot edits did not undo.");
        var feature = editor.RenderFeatureTypes.First(type => type.Type == typeof(MeshRenderFeature));
        editor.AddNewRenderFeatureCommand.Execute(feature);
        if (editor.RenderFeatures.Count != 1 || compositor.RenderFeatures.Count != 1) throw new Exception("Creating a render feature did not update both collections.");
        history.Undo();
        if (editor.RenderFeatures.Count != 0) throw new Exception("Undo did not remove the feature.");

        var entry = editor.Blocks.OfType<EntryPointBlockViewModel>().Single();
        var game = (GraphicsCompositorSlotViewModel)entry.OutputSlots.Single(slot => slot.Name == nameof(GraphicsCompositorAsset.Game));
        if (game.Links.Count != 1) throw new Exception("The game entry point did not resolve its renderer link.");
        game.ClearLink();
        if (compositor.Game != null || game.Links.Count != 0) throw new Exception("Disconnecting did not update the graph and asset.");
        history.Undo();
        if (!ReferenceEquals(compositor.Game, renderer) || game.Links.Count != 1) throw new Exception("Undo did not reconnect the entry point.");
        game.ClearLink();
        var input = (GraphicsCompositorSlotViewModel)editor.GetBlock(renderer)!.InputSlots.Single();
        if (!game.CanLinkTo(input)) throw new Exception("The compatible renderer input rejected a connection.");
        game.LinkTo(input);
        if (!ReferenceEquals(compositor.Game, renderer) || game.Links.Count != 1) throw new Exception("Connecting did not update the graph and asset.");
        history.Undo();
        history.Undo();

        editor.SelectedRenderStages.Add(editor.RenderStages[0]);
        editor.RemoveSelectedRenderStagesCommand.Execute(null);
        if (renderer.OpaqueRenderStage != null || editor.RenderStages.Count != 0) throw new Exception("Deleting a stage left a dangling renderer reference.");
        history.Undo();
        if (!ReferenceEquals(renderer.OpaqueRenderStage, stage) || editor.RenderStages.Count != 1) throw new Exception("Undo did not restore the stage reference.");
        editor.SelectedSharedRenderers.Add(editor.GetBlock(renderer)!);
        editor.DeleteSelectionCommand.Execute(null);
        if (compositor.Game != null || compositor.SharedRenderers.Count != 0 || editor.Blocks.Count != 1)
            throw new Exception("Deleting a renderer left a dangling entry point or block.");
        history.Undo();
        if (!ReferenceEquals(compositor.Game, renderer) || editor.GetBlock(renderer) == null || game.Links.Count != 1)
            throw new Exception("Undo did not restore the renderer and its connection.");
        await session.EditorCollection.CloseEditorAsync(editor);
        session.EditorCollection.OpenAssetEditor(asset);
        var reopened = (GraphicsCompositorEditorViewModel)session.EditorCollection.ActiveEditor!;
        if (reopened.Blocks.Count != 2 || reopened.RenderStages.Count != 1)
            throw new Exception("Reopening the compositor retained disposed view models or duplicated blocks.");
        await session.EditorCollection.CloseEditorAsync(reopened);
        history.Undo(); // Remove the temporary fixture asset; this check never saves the project.
        Console.WriteLine("PASS: compositor panels, stages, features, cameras, links, reference cleanup, undo and editor lifecycle.");
    }
}
