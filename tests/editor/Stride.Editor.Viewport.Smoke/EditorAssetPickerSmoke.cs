// Copyright (c) .NET Foundation and Contributors (https://dotnetfoundation.org/ & https://stride3d.net)
// Distributed under the MIT license. See the LICENSE.md file in the project root for more information.

using Avalonia;
using Avalonia.Controls;
using Avalonia.Automation.Peers;
using Avalonia.Automation.Provider;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Stride.Assets.Skyboxes;
using Stride.Assets.Textures;
using Stride.Core.Assets;
using Stride.Core.Assets.Editor.Quantum.NodePresenters.Commands;
using Stride.Core.Assets.Editor.Services;
using Stride.Core.Assets.Editor.ViewModels;
using Stride.Core.Assets.Presentation.ViewModels;
using Stride.Core.Presentation.Quantum.ViewModels;
using Stride.Core.Serialization;
using Stride.GameStudio.Avalonia.Views;
using Stride.Graphics;

internal static class EditorAssetPickerSmoke
{
    public static async Task Check(SessionViewModel session, Window owner)
    {
        var history = session.ActionService!;
        var textures = session.AllAssets.Where(a => a.Asset is TextureAsset && a.IsEditable).Take(2).ToArray();
        Require(textures.Length == 2, "The disposable project must contain two local texture assets.");
        var project = session.CurrentProject!;
        var folder = project.AssetMountPoint.GetOrCreateDirectory("PickerSmoke_" + Guid.NewGuid().ToString("N"));
        var firstData = new SkyboxAsset { CubeMap = ContentReferenceHelper.CreateReference<Texture>(textures[0])! };
        var secondData = new SkyboxAsset { CubeMap = ContentReferenceHelper.CreateReference<Texture>(textures[1])! };
        AssetViewModel first, second;
        using (history.CreateTransaction())
        {
            first = project.AddAsset(new AssetItem(folder.Path + "/First", firstData), folder);
            second = project.AddAsset(new AssetItem(folder.Path + "/Second", secondData), folder);
        }
        var fixtureTransactions = history.RetrieveAllTransactions().Count();
        var mainSelection = session.AssetCollection.SelectedAssets.ToArray();
        var mainLocations = session.AssetCollection.SelectedLocations.ToArray();
        var graph = await session.ActiveProperties.GenerateSelectionPropertiesAsync([first, second])
            ?? throw new Exception("No combined skybox property graph.");
        await Idle();
        var node = Nodes(graph.RootNode).Single(n => n.Name == nameof(SkyboxAsset.CubeMap));
        var command = node.Commands.Single(c => c.Name == PickupAssetCommand.CommandName);
        var originalFirst = firstData.CubeMap;
        var originalSecond = secondData.CubeMap;
        Require(command.CanExecute(typeof(Texture)), "Reference command disabled");

        // Open the real inspector button and cancel a combined, differing reference selection.
        var row = owner.GetVisualDescendants().OfType<Stride.Core.Presentation.Avalonia.Controls.PropertyViewItem>()
            .FirstOrDefault(r => ReferenceEquals(r.DataContext, node));
        var button = row?.GetVisualDescendants().OfType<Button>().FirstOrDefault(b => ReferenceEquals(b.Command, command));
        Require(button != null, "Inspector did not expose its picker button");
        Require(button!.CommandParameter as Type == typeof(Texture), "Inspector picker parameter");
        Click(button);
        var dialog = await Dialog();
        var model = (AssetPickerViewModel)dialog.DataContext!;
        Require(dialog.Owner == owner, "Picker is not modal");
        Require(model.AcceptedTypes.Count > 0 && model.Assets.All(a => typeof(Texture).IsAssignableFrom(AssetRegistry.GetContentType(a.AssetType))), "Compatible type restriction");
        Require(ReferenceEquals(model.InitialAsset, textures[0]) && model.SelectedAssets.Contains(textures[0]), "Initial target selection");
        dialog.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Escape });
        await WaitClosed(dialog);
        Require(ReferenceEquals(firstData.CubeMap, originalFirst) && ReferenceEquals(secondData.CubeMap, originalSecond), "Cancel changed differing reference values");
        Require(history.RetrieveAllTransactions().Count() == fixtureTransactions, "Cancel recorded an undo operation");
        CheckMainSelection();

        // Select one texture for both targets, then undo/redo the single Quantum transaction.
        var invocation = command.Invoke(typeof(Texture));
        dialog = await Dialog(); model = (AssetPickerViewModel)dialog.DataContext!;
        var list = dialog.FindControl<ListBox>("AssetList")!;
        Require(ReferenceEquals(list.SelectedItems, model.SelectedAssets), "Picker list selection binding");
        Click(dialog.FindControl<Button>("AllPackagesButton")!);
        await Idle();
        Require(model.SelectedLocation == null && model.Assets.All(a => typeof(Texture).IsAssignableFrom(AssetRegistry.GetContentType(a.AssetType))), "All packages bypassed fixed type restriction");
        dialog.FindControl<TextBox>("SearchBox")!.SetCurrentValue(TextBox.TextProperty, "__no_picker_match__");
        Require(model.Assets.Count == 0 && model.SelectedAssets.Count == 0 && !model.CanAccept, "Search retained a hidden selection");
        Require(dialog.FindControl<TextBlock>("EmptyMessage")!.IsVisible, "Empty result guidance");
        dialog.FindControl<TextBox>("SearchBox")!.SetCurrentValue(TextBox.TextProperty, Path.GetFileName(textures[1].Url));
        Require(model.Assets.Contains(textures[1]), "Search by asset name/path");
        list.SelectedItem = textures[1];
        Require(model.CanAccept, "Valid selection did not enable confirmation");
        await Idle();
        Require(list.GetVisualDescendants().OfType<ListBoxItem>().Any(item => ReferenceEquals(item.DataContext, textures[1]) && item.IsEffectivelyVisible), "Filtered asset row was not realized");
        using (var screenshot = new RenderTargetBitmap(PixelSize.FromSize(dialog.Bounds.Size, 1)))
        {
            screenshot.Render(dialog);
            screenshot.Save(Path.Combine(Path.GetTempPath(), "stride-asset-picker.png"));
        }
        dialog.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Enter });
        await invocation;
        Require(AttachedReferenceManager.GetAttachedReference(firstData.CubeMap)?.Id == textures[1].Id
            && ReferenceEquals(secondData.CubeMap, originalSecond), "Combined assignment or unchanged-reference preservation");
        history.Undo();
        Require(ReferenceEquals(firstData.CubeMap, originalFirst) && ReferenceEquals(secondData.CubeMap, originalSecond), "Assignment undo");
        history.Redo();
        Require(AttachedReferenceManager.GetAttachedReference(firstData.CubeMap)?.Id == textures[1].Id, "Assignment redo");
        history.Undo();
        CheckMainSelection();

        // Window-close must also cancel without writing references.
        invocation = command.Invoke(typeof(Texture));
        dialog = await Dialog();
        dialog.Close();
        await invocation;
        Require(ReferenceEquals(firstData.CubeMap, originalFirst) && ReferenceEquals(secondData.CubeMap, originalSecond), "Window-close cancellation");

        // Exercise the reusable service with multi-selection and an additional mandatory predicate.
        var picker = new AssetPickerViewModel(session)
        {
            AllowMultiSelection = true,
            InitialLocation = project.AssetMountPoint,
            Filter = a => a == first || a == second,
            Message = "Pick both temporary skyboxes."
        };
        picker.AcceptedTypes.Add(typeof(Asset));
        try
        {
            var task = session.ServiceProvider.Get<IEditorDialogService>().ShowAssetPickerAsync(picker);
            dialog = await Dialog();
            Require(picker.Assets.Count == 2 && picker.Assets.Contains(first) && picker.Assets.Contains(second), "Derived accepted types or custom predicate");
            picker.IncludeSubfolders = false;
            Require(picker.Assets.Count == 0, "Folder-only scope included descendants");
            picker.SelectedLocation = folder;
            Require(picker.Assets.Count == 2, "Folder scope");
            picker.SelectedAssets.Add(textures[0]);
            Require(!picker.CanAccept && picker.GetSelection().Count == 0, "Rejected asset passed validation");
            picker.SelectedAssets.Clear();
            list = dialog.FindControl<ListBox>("AssetList")!;
            list.SelectedItems!.Add(first); list.SelectedItems.Add(second);
            Require(picker.CanAccept && picker.GetSelection().Count == 2, "Multiple selection");
            Click(dialog.FindControl<Button>("SelectButton")!);
            var selected = await task;
            Require(selected.Count == 2 && selected.Contains(first) && selected.Contains(second), "Service result");
        }
        finally { picker.Destroy(); }

        // Even a direct invocation must not open the picker for read-only presenters.
        foreach (var presenter in node.NodePresenters) presenter.IsReadOnly = true;
        Require(!command.CanExecute(typeof(Texture)), "Read-only picker command enabled");
        await command.Invoke(typeof(Texture));
        foreach (var presenter in node.NodePresenters) presenter.IsReadOnly = false;
        Require(ReferenceEquals(firstData.CubeMap, originalFirst) && ReferenceEquals(secondData.CubeMap, originalSecond), "Read-only invocation changed references");
        var readOnly = new AssetPickerViewModel(session) { InitialLocation = project.AssetMountPoint };
        try
        {
            readOnly.AcceptedTypes.Add(typeof(TextureAsset));
            readOnly.Initialize();
            readOnly.SelectedAssets.Add(textures[0]);
            readOnly.SearchText = "NoSuchAsset";
            Require(!readOnly.CanAccept && readOnly.SelectedAssets.Count == 0, "Filter invalidation");
        }
        finally { readOnly.Destroy(); }
        await session.ActiveProperties.GenerateSelectionPropertiesAsync(Array.Empty<Stride.Core.Presentation.Quantum.IPropertyProviderViewModel>());
        history.Undo(); // Remove fixture assets; the disposable project is not saved.
        Console.WriteLine("PASS: Asset Picker inspector button, modal ownership, initial target, fixed types, all packages/folders/search, empty results, independent selection, cancellation, combined assignment, unchanged reference, Undo/Redo, predicate and multiple selection.");

        void CheckMainSelection()
        {
            Require(session.AssetCollection.SelectedAssets.SequenceEqual(mainSelection) && session.AssetCollection.SelectedLocations.SequenceEqual(mainLocations), "Picker changed the main asset view selection");
            Require(ReferenceEquals(session.ActiveProperties.ViewModel, graph), "Picker replaced the inspector graph");
        }
    }

    private static void Click(Button button) => ((IInvokeProvider)new ButtonAutomationPeer(button)).Invoke();
    private static IEnumerable<NodeViewModel> Nodes(NodeViewModel node) => new[] { node }.Concat(node.Children.SelectMany(Nodes));
    private static async Task<AssetPickerWindow> Dialog()
    {
        var lifetime = (IClassicDesktopStyleApplicationLifetime)Application.Current!.ApplicationLifetime!;
        for (var i = 0; i < 100; i++)
        {
            if (lifetime.Windows.OfType<AssetPickerWindow>().FirstOrDefault(w => w.IsVisible) is { } dialog)
            { await Idle(); return dialog; }
            await Task.Delay(50);
        }
        throw new TimeoutException("No Asset Picker window opened.");
    }
    private static async Task WaitClosed(Window dialog)
    {
        for (var i = 0; i < 100 && dialog.IsVisible; i++) await Task.Delay(50);
        await Idle();
        Require(!dialog.IsVisible, "Picker did not close");
    }
    private static async Task Idle() { await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Background); await Task.Delay(100); }
    private static void Require(bool condition, string message) { if (!condition) throw new Exception("Asset Picker: " + message); }
}
