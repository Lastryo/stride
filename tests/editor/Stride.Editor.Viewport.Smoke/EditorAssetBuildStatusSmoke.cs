// Copyright (c) .NET Foundation and Contributors (https://dotnetfoundation.org/ & https://stride3d.net)
// Distributed under the MIT license. See the LICENSE.md file in the project root for more information.

using Avalonia;
using Avalonia.Automation.Peers;
using Avalonia.Automation.Provider;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Stride.Assets.Entities;
using Stride.Core.Assets;
using Stride.Core.Assets.Editor.ViewModels;
using Stride.Core.Assets.Presentation.ViewModels;
using Stride.Core.Serialization;
using Stride.Engine;
using Stride.GameStudio.Avalonia.Views;

internal static class EditorAssetBuildStatusSmoke
{
    public static async Task Check(SessionViewModel session, Window owner)
    {
        var project = session.CurrentProject ?? throw new Exception("No current project.");
        var history = session.ActionService!;
        var packageWasDirty = project.Package.IsDirty;
        AssetViewModel parent;
        AssetViewModel child;
        using (history.CreateTransaction())
        {
            var suffix = Guid.NewGuid().ToString("N");
            parent = project.AddAsset(new AssetItem("BuildStatusParent_" + suffix, new SceneAsset()), project.AssetMountPoint);
            child = project.AddAsset(new AssetItem("BuildStatusChild_" + suffix, new SceneAsset
            {
                Parent = AttachedReferenceManager.CreateProxyObject<Scene>(parent.Id, parent.Url),
            }), project.AssetMountPoint);
        }
        var parentCard = new AssetCard { DataContext = parent };
        var childCard = new AssetCard { DataContext = child };
        var panel = new StackPanel { Orientation = global::Avalonia.Layout.Orientation.Horizontal, Children = { childCard, parentCard } };
        var surface = new UserControl { Classes = { "editorShell" }, Content = panel };
        var window = new Window { Title = "Asset build status check", Content = surface, Width = 360, Height = 240 };
        window.Show(owner);
        try
        {
            await AssetDependenciesViewModel.NotifyAssetChanged(session, child);
            await Task.Delay(100);
            if (!child.Dependencies.ReferencedAssets.Contains(parent)) throw new Exception("The fixture dependency was not discovered.");
            await State(childCard, "ExcludedIndicator", "Excluded from build");
            await State(parentCard, "ExcludedIndicator", "Excluded from build");
            var transactionCount = history.RetrieveAllTransactions().Count();
            await Click(childCard);
            await Included();
            if (history.RetrieveAllTransactions().Count() != transactionCount + 1 || !project.IsDirty || !project.Package.IsDirty)
                throw new Exception("Changing a root did not create one dirty package transaction.");

            using (var stream = new MemoryStream())
            {
                AssetFileSerializer.Save(stream, project.Package, null);
                stream.Position = 0;
                var reloaded = AssetFileSerializer.Load<Package>(stream, project.PackagePath).Asset;
                if (!reloaded.RootAssets.Any(root => root.Id == child.Id))
                    throw new Exception("The root was not persisted in package serialization.");
            }
            history.Undo();
            await Excluded();
            history.Redo();
            await Included();
            await Click(parentCard);
            await State(parentCard, "RootIndicator", "Included in build as root");
            history.Undo();
            await State(parentCard, "DependencyIndicator", "Included in build as dependency");
            await Click(childCard);
            await Excluded();
            history.Undo();
            await Included();
            session.AssetCollection.SelectAssets([child]);
            if (!await session.AssetCollection.DeleteSelection(askConfirmation: false)) throw new Exception("Root asset deletion failed.");
            await AssetDependenciesViewModel.NotifyAssetChanged(session, child);
            if (!child.IsDeleted) throw new Exception("Deleting a root kept the asset alive.");
            await Excluded();
            history.Undo();
            await AssetDependenciesViewModel.NotifyAssetChanged(session, child);
            if (child.IsDeleted) throw new Exception("Undo did not restore the root asset.");
            await Included();
            history.Redo();
            await AssetDependenciesViewModel.NotifyAssetChanged(session, child);
            await Excluded();
            history.Undo();
            await AssetDependenciesViewModel.NotifyAssetChanged(session, child);
            await Included();
            var forced = session.AllAssets.First(asset => asset.Dependencies.ForcedRoot && project.IsInScope(asset));
            var forcedCard = new AssetCard { DataContext = forced };
            panel.Children.Add(forcedCard);
            await Task.Delay(50);
            var beforeForcedClick = history.RetrieveAllTransactions().Count();
            await Click(forcedCard);
            await State(forcedCard, "RootIndicator", "Included in build as root");
            if (history.RetrieveAllTransactions().Count() != beforeForcedClick) throw new Exception("A forced root created a toggle operation.");
            Console.WriteLine("PASS: gray/blue/green build dots, tooltip hit area, real button activation, recursive updates, package serialization, Undo/Redo, root deletion and forced roots.");
        }
        finally
        {
            window.Close();
            for (var i = 0; i < 10 && history.CanUndo && (!parent.IsDeleted || !child.IsDeleted); i++) history.Undo();
            project.Package.IsDirty = packageWasDirty;
            await AssetDependenciesViewModel.NotifyAssetChanged(session, child);
        }

        async Task Included()
        {
            if (!project.Package.RootAssets.Any(root => root.Id == child.Id)) throw new Exception("The package did not receive the root.");
            await State(childCard, "RootIndicator", "Included in build as root");
            await State(parentCard, "DependencyIndicator", "Included in build as dependency");
        }
        async Task Excluded()
        {
            if (project.Package.RootAssets.Any(root => root.Id == child.Id)) throw new Exception("The package retained a removed root.");
            await State(childCard, "ExcludedIndicator", "Excluded from build");
            await State(parentCard, "ExcludedIndicator", "Excluded from build");
        }
    }

    private static async Task Click(AssetCard card)
    {
        var button = card.FindControl<Button>("BuildStatusButton")!;
        ((IInvokeProvider)new ButtonAutomationPeer(button)).Invoke();
        await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Background);
    }

    private static async Task State(AssetCard card, string indicator, string tooltip)
    {
        await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Background);
        var visible = new[] { "RootIndicator", "DependencyIndicator", "ExcludedIndicator" }
            .Where(name => card.FindControl<global::Avalonia.Controls.Image>(name)!.IsVisible).ToArray();
        if (visible.Length != 1 || visible[0] != indicator) throw new Exception($"Expected only {indicator}, got {string.Join(",", visible)}.");
        var button = card.FindControl<Button>("BuildStatusButton")!;
        var hit = button.InputHitTest(new Point(2, 2)) as Visual;
        if (hit?.FindAncestorOfType<Button>(true) != button) throw new Exception("The build status padding cannot be clicked.");
        ToolTip.SetIsOpen(button, true);
        try
        {
            await Task.Delay(30);
            var tip = (StackPanel)ToolTip.GetTip(button)!;
            if (tip.Children.OfType<TextBlock>().Where(text => text.IsVisible).SingleOrDefault()?.Text != tooltip)
                throw new Exception("The build tooltip did not follow the current state.");
        }
        finally { ToolTip.SetIsOpen(button, false); }
    }
}
