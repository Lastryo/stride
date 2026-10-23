// Copyright (c) .NET Foundation and Contributors (https://dotnetfoundation.org/ & https://stride3d.net)
// Distributed under the MIT license. See the LICENSE.md file in the project root for more information.

using System.Collections.Specialized;
using System.ComponentModel;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Input;
using Avalonia.Threading;
using Dock.Avalonia.Controls;
using Dock.Model;
using Dock.Model.Controls;
using Dock.Model.Core;
using Dock.Model.Mvvm;
using Dock.Model.Mvvm.Controls;
using Dock.Serializer;
using Stride.Assets.Editor.Avalonia;
using Stride.Core.Assets.Editor.ViewModels;
using Stride.Core.Presentation.Services;
using Stride.GameStudio.Avalonia.Settings;
using Stride.GameStudio.Avalonia.ViewModels;

namespace Stride.GameStudio.Avalonia.Layout;

/// <summary>Connects Dock's layout lifecycle to Stride's panels and asset editors.</summary>
internal sealed class DockingLayoutManager : Factory, IDisposable
{
    private readonly MainViewModel main;
    private readonly SessionViewModel? session;
    private readonly ProjectBuildViewModel? projectBuild;
    private readonly Dictionary<string, Panel> panels;
    private readonly Dictionary<AssetEditorViewModel, Tool> documents = new();
    private readonly DockSerializer serializer = new();
    private bool synchronizing;
    private bool disposed;
    private bool floatAvailabilityPending;
    private readonly HashSet<IDockWindow> closingWindows = new();

    private sealed class SavedLayout
    {
        public IRootDock? Root { get; set; }
        public Dictionary<string, IDockable> OriginalOwners { get; set; } = new();
    }

    private sealed record Panel(string Title, Func<bool> GetVisible, Action<bool> SetVisible);

    public DockingLayoutManager(MainViewModel main, Func<string, Control> createToolView, bool restore = true)
    {
        this.main = main;
        session = main.Session;
        projectBuild = main.ProjectBuild;
        HideToolsOnClose = true;
        DefaultHostWindowLocator = CreateHostWindow;
        var p = main.Panels;
        panels = new()
        {
            ["SessionExplorer"] = new("Solution explorer", () => p.SessionExplorerPanelVisible, v => p.SessionExplorerPanelVisible = v),
            ["AssetView"] = new("Asset view", () => p.AssetViewPanelVisible, v => p.AssetViewPanelVisible = v),
            ["AssetLog"] = new("Asset errors", () => p.AssetLogPanelVisible, v => p.AssetLogPanelVisible = v),
            ["BuildLog"] = new("Output", () => p.BuildLogPanelVisible, v => p.BuildLogPanelVisible = v),
            ["PropertyGrid"] = new("Property grid", () => p.PropertyGridPanelVisible, v => p.PropertyGridPanelVisible = v),
            ["AssetPreview"] = new("Asset preview", () => p.AssetPreviewPanelVisible, v => p.AssetPreviewPanelVisible = v),
            ["ActionHistory"] = new("Edit history", () => p.ActionHistoryPanelVisible, v => p.ActionHistoryPanelVisible = v),
            ["References"] = new("References", () => p.ReferencesPanelVisible, v => p.ReferencesPanelVisible = v)
        };
        ContextLocator = panels.Keys.ToDictionary(id => id, id => (Func<object?>)(() => createToolView(id)));
        Layout = restore ? RestoreLayout() ?? CreateLayout() : CreateLayout();
        InitLayout(Layout);
        RestoreEditors();
        SynchronizeEditors();
        ApplyPanelVisibility();
        main.Panels.PropertyChanged += PanelsChanged;
        if (session?.EditorCollection is { } editors)
        {
            editors.Editors.CollectionChanged += EditorsChanged;
            editors.PropertyChanged += EditorCollectionChanged;
        }
        if (projectBuild is { } build) build.PropertyChanged += BuildChanged;
        if (session?.AssetLog is { } log) log.PropertyChanged += AssetLogChanged;
        UpdateAssetLogTitle();
        DockableAdded += ScheduleFloatAvailability;
        DockableRemoved += ScheduleFloatAvailability;
        DockableDocked += ScheduleFloatAvailability;
        DockableMoved += ScheduleFloatAvailability;
        DockableHidden += ScheduleFloatAvailability;
        DockableRestored += ScheduleFloatAvailability;
        DockablePinned += ScheduleFloatAvailability;
        DockableUnpinned += ScheduleFloatAvailability;
        WindowAdded += ScheduleFloatAvailability;
        WindowRemoved += ScheduleFloatAvailability;
        UpdateFloatAvailability();
    }

    public IRootDock Layout { get; }

    private void ScheduleFloatAvailability(object? sender, EventArgs e)
    {
        if (disposed || floatAvailabilityPending) return;
        floatAvailabilityPending = true;
        // Dock events also fire while owners are being reassigned; inspect the final tree.
        Dispatcher.UIThread.Post(() =>
        {
            floatAvailabilityPending = false;
            if (!disposed) UpdateFloatAvailability();
        });
    }

    private void UpdateFloatAvailability()
    {
        var singlePanels = new HashSet<IDockable>();
        foreach (var window in Layout.Windows?.ToArray() ?? [])
        {
            if (window.Layout is not { } root) continue;
            var contents = VisibleContents(root).Take(2).ToArray();
            if (contents.Length == 1) singlePanels.Add(contents[0]);
        }
        foreach (var dockable in Walk(Layout))
            if (panels.ContainsKey(dockable.Id) || dockable.Id.StartsWith("Editor:", StringComparison.Ordinal))
                dockable.CanFloat = !singlePanels.Contains(dockable);
    }

    private static IEnumerable<IDockable> VisibleContents(IDock dock)
    {
        foreach (var child in dock.VisibleDockables?.ToArray() ?? [])
        {
            if (child is IDock nested)
            {
                foreach (var leaf in VisibleContents(nested)) yield return leaf;
            }
            else if (child is not ISplitter) yield return child;
        }
    }


    private IHostWindow CreateHostWindow()
    {
        var window = new HostWindow();
        if (session?.EditorCollection is { } collection)
        {
            var close = new KeyBinding { Gesture = new KeyGesture(Key.F4, KeyModifiers.Control), Command = collection.CloseEditorCommand };
            close.Bind(KeyBinding.CommandParameterProperty, new Binding(nameof(EditorCollectionViewModel.ActiveEditor)) { Source = collection });
            window.KeyBindings.Add(close);
        }
        window.KeyBindings.Add(new KeyBinding { Gesture = new KeyGesture(Key.S, KeyModifiers.Control), Command = main.SaveCommand });
        return window;
    }

    public override IRootDock CreateLayout()
    {
        Tool Tool(string id) => new() { Id = id, Title = panels[id].Title, CanClose = true, CanPin = true, CanFloat = true, CanDockAsDocument = true };
        ToolDock Tools(string id, double proportion, Alignment alignment, params string[] ids) => new()
        {
            Id = id, Proportion = proportion, Alignment = alignment,
            VisibleDockables = CreateList<IDockable>(ids.Select(Tool).ToArray()),
            ActiveDockable = null
        };
        var solution = Tools("SolutionTools", 0.25, Alignment.Left, "SessionExplorer");
        var assets = Tools("AssetTools", 0.75, Alignment.Bottom, "AssetView", "AssetLog", "BuildLog");
        var properties = Tools("PropertyTools", 2.0 / 3, Alignment.Right, "PropertyGrid");
        var preview = Tools("PreviewTools", 1.0 / 3, Alignment.Right, "AssetPreview", "ActionHistory", "References");
        foreach (var dock in new[] { solution, assets, properties }) dock.ActiveDockable = dock.VisibleDockables![0];
        preview.ActiveDockable = preview.VisibleDockables![2];
        var documentsDock = new DocumentDock { Id = "Documents", IsCollapsable = false, CanCreateDocument = false, VisibleDockables = CreateList<IDockable>() };
        ProportionalDock Split(Orientation orientation, double proportion, params IDockable[] children) => new()
        {
            Orientation = orientation, Proportion = proportion, VisibleDockables = CreateList(children)
        };
        var bottom = Split(Orientation.Horizontal, 1.0 / 4.3, solution, new ProportionalDockSplitter(), assets);
        documentsDock.Proportion = 3.3 / 4.3;
        var center = Split(Orientation.Vertical, 830.0 / 1230, documentsDock, new ProportionalDockSplitter(), bottom);
        var right = Split(Orientation.Vertical, 400.0 / 1230, properties, new ProportionalDockSplitter(), preview);
        var workspace = Split(Orientation.Horizontal, 1, center, new ProportionalDockSplitter(), right);
        var root = CreateRootDock();
        root.Id = "GameStudio";
        root.IsCollapsable = false;
        root.VisibleDockables = CreateList<IDockable>(workspace);
        root.ActiveDockable = root.DefaultDockable = workspace;
        return root;
    }

    private IRootDock? RestoreLayout()
    {
        var json = GameStudioInternalSettings.DockLayout.GetValue();
        if (string.IsNullOrWhiteSpace(json)) return null;
        try
        {
            var saved = serializer.Deserialize<SavedLayout>(json);
            var root = saved?.Root;
            if (root is not null)
                foreach (var dockable in Walk(root))
                    if (saved!.OriginalOwners.TryGetValue(dockable.Id, out var owner)) dockable.OriginalOwner = owner;
            // Reject obsolete or incomplete layouts rather than silently losing panels.
            if (root is null) return null;
            foreach (var window in root.Windows?.ToArray() ?? [])
                if (window.Layout is null || !Walk(window.Layout).Any(d => d is ITool or IDocument))
                    root.Windows!.Remove(window);
            var all = Walk(root).ToArray();
            return panels.Keys.All(id => all.Count(d => d.Id == id) == 1) && all.Any(d => d is IDocumentDock) ? root : null;
        }
        catch (Newtonsoft.Json.JsonException)
        {
            return null;
        }
    }

    public void SaveLayout()
    {
        foreach (var window in Layout.Windows?.ToArray() ?? []) window.Save();
        GameStudioInternalSettings.DockLayout.SetValue(serializer.Serialize(new SavedLayout
        {
            Root = Layout,
            OriginalOwners = Walk(Layout).Where(d => d is ITool && (d.OriginalOwner is not null || IsDockablePinned(d)))
                .ToDictionary(d => d.Id, d => d.OriginalOwner ?? d.Owner!)
        }));
    }

    private void RestoreEditors()
    {
        if (session?.EditorCollection is not { } editors) return;
        var focusedId = Layout.FocusedDockable?.Id;
        var selectedId = focusedId?.StartsWith("Editor:", StringComparison.Ordinal) == true ? focusedId
            : Walk(Layout).OfType<IDocumentDock>().Select(d => d.ActiveDockable?.Id).FirstOrDefault(id => id is not null);
        var assets = session.AllAssets.ToDictionary(a => "Editor:" + a.Id);
        foreach (var document in Walk(Layout).OfType<Tool>().Where(d => d.Id.StartsWith("Editor:", StringComparison.Ordinal)).ToArray())
        {
            if (assets.TryGetValue(document.Id, out var asset)) editors.OpenAssetEditor(asset);
            if (!editors.Editors.Any(e => EditorId(e) == document.Id)) RemoveDockable(document, true);
        }
        if (editors.Editors.FirstOrDefault(e => EditorId(e) == selectedId) is { } selected) editors.ActiveEditor = selected;
    }

    private static string EditorId(AssetEditorViewModel editor) => "Editor:" + editor.Asset.Id;

    private void SynchronizeEditors()
    {
        if (session?.EditorCollection is not { } editors) return;
        foreach (var editor in documents.Keys.Except(editors.Editors).ToArray())
        {
            editor.Asset.PropertyChanged -= AssetChanged;
            editor.PropertyChanged -= EditorChanged;
            RemoveDockable(documents[editor], true);
            documents.Remove(editor);
        }
        foreach (var editor in editors.Editors.Where(e => !documents.ContainsKey(e)))
        {
            var document = Walk(Layout).OfType<Tool>().FirstOrDefault(d => d.Id == EditorId(editor));
            if (document is null)
            {
                document = new Tool { Id = EditorId(editor), CanFloat = true, CanClose = true, CanPin = true, CanDockAsDocument = true };
                var dock = Walk(Layout).OfType<IDocumentDock>().First();
                AddDockable(dock, document);
            }
            document.Context = CreateEditorView(editor);
            documents.Add(editor, document);
            editor.Asset.PropertyChanged += AssetChanged;
            editor.PropertyChanged += EditorChanged;
        }
        UpdateDocumentTitles();
        if (editors.ActiveEditor is { } active && documents.TryGetValue(active, out var selected))
            Activate(selected);
    }

    private Control? CreateEditorView(AssetEditorViewModel editor)
    {
        var view = new EditorViewSelector { Session = session }.Build(editor);
        if (view is not null)
        {
            view.Classes.Add("editorShell");
            view.DataContext = editor;
        }
        return view;
    }

    private void UpdateDocumentTitles()
    {
        foreach (var (editor, document) in documents)
        {
            document.Title = $"/{editor.Asset.Directory.Package.Name}/{editor.Asset.Url}{(editor.Asset.IsDirty ? "*" : "")}";
            document.CanClose = !editor.IsClosing;
        }
    }

    public override bool OnWindowClosing(IDockWindow? window)
    {
        if (disposed || window?.Layout is null) return base.OnWindowClosing(window);
        var contained = Walk(window.Layout).ToHashSet();
        var editors = documents.Where(pair => contained.Contains(pair.Value)).Select(pair => pair.Key).ToArray();
        if (editors.Length == 0) return base.OnWindowClosing(window);
        if (closingWindows.Add(window)) _ = CloseFloatingEditors(window, editors);
        return false;
    }

    private async Task CloseFloatingEditors(IDockWindow window, AssetEditorViewModel[] editors)
    {
        try
        {
            if (session?.EditorCollection is not { } collection) return;
            foreach (var editor in editors) await collection.CloseEditorAsync(editor);
            if (!disposed && window.Owner is not null)
            {
                CloseWindow(window);
                RemoveWindow(window);
            }
        }
        catch (Exception exception)
        {
            await main.ServiceProvider.Get<IDialogService>().MessageBoxAsync($"Unable to close the editor: {exception.Message}", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally { closingWindows.Remove(window); }
    }

    public override void DockAsDocument(IDockable dockable)
    {
        if (disposed || dockable is null || dockable.Owner is not IDock source) return;
        if (!DockCapabilityResolver.IsEnabled(dockable, DockCapability.DockAsDocument, DockCapabilityResolver.ResolveOperationDock(dockable))) return;
        var target = FindDockable(Layout, d => d is IDocumentDock) as IDock;
        if (target is null || ReferenceEquals(source, target)) return;
        MoveDockable(source, target, dockable, target.VisibleDockables?.LastOrDefault());
        Activate(dockable);
    }

    public override void CloseDockable(IDockable? dockable)
    {
        // The chrome command can outlive its active tab during a layout update.
        if (dockable is null || disposed) return;
        base.CloseDockable(dockable);
    }

    public override bool OnDockableClosing(IDockable? dockable)
    {
        if (disposed) return false;
        var editor = documents.FirstOrDefault(pair => ReferenceEquals(pair.Value, dockable)).Key;
        if (editor is not null && session?.EditorCollection is { } editors)
        {
            // Stride owns asynchronous teardown and error reporting; remove only after it succeeds.
            if (editors.CloseEditorCommand.CanExecute(editor)) editors.CloseEditorCommand.Execute(editor);
            return false;
        }
        return base.OnDockableClosing(dockable);
    }

    public override void OnActiveDockableChanged(IDockable? dockable)
    {
        base.OnActiveDockableChanged(dockable);
        if (disposed || synchronizing) return;
        var editor = documents.FirstOrDefault(pair => ReferenceEquals(pair.Value, dockable)).Key;
        if (editor is not null && session?.EditorCollection is { } editors)
            editors.ActiveEditor = editor;
    }

    public override void HideDockable(IDockable dockable)
    {
        var sourceRoot = FindRoot(dockable, _ => true);
        base.HideDockable(dockable);
        // A floating root is discarded with its window; keep closed tools in the workspace.
        if (dockable.Owner is IRootDock root && !ReferenceEquals(root, Layout) && root.HiddenDockables?.Remove(dockable) == true)
        {
            Layout.HiddenDockables ??= CreateList<IDockable>();
            Layout.HiddenDockables.Add(dockable);
            dockable.Owner = Layout;
        }
        if (sourceRoot?.Window is { } window && !Walk(sourceRoot).Any(d => d is ITool or IDocument))
        {
            // Drop empty containers before Exit triggers the native window's close callback.
            sourceRoot.VisibleDockables?.Clear();
            sourceRoot.ActiveDockable = sourceRoot.DefaultDockable = null;
            RemoveWindow(window);
        }
    }

    private IDock GetToolDock()
    {
        // Use a main-window pane when the original floating window no longer exists.
        var mainDock = Walk(Layout).OfType<IToolDock>().FirstOrDefault(d => ReferenceEquals(FindRoot(d, _ => true), Layout));
        if (mainDock is not null) return mainDock;
        var dock = CreateToolDock();
        dock.VisibleDockables = CreateList<IDockable>();
        SplitToDock(Walk(Layout).OfType<IDocumentDock>().First(), dock, DockOperation.Bottom);
        return dock;
    }

    public override void OnDockableHidden(IDockable? dockable)
    {
        base.OnDockableHidden(dockable);
        if (!disposed && !synchronizing && dockable is not null && panels.TryGetValue(dockable.Id, out var panel))
            panel.SetVisible(false);
    }

    private void ApplyPanelVisibility()
    {
        if (synchronizing || disposed) return;
        synchronizing = true;
        try
        {
            foreach (var (id, panel) in panels)
            {
                var tool = Walk(Layout).FirstOrDefault(d => d.Id == id);
                if (tool is null) continue;
                var hidden = (tool.Owner as IRootDock)?.HiddenDockables?.Contains(tool) == true;
                if (panel.GetVisible() && hidden)
                {
                    if (tool.OriginalOwner is not IDock owner || !Walk(Layout).Contains(owner))
                        tool.OriginalOwner = GetToolDock();
                    RestoreDockable(tool);
                }
                else if (!panel.GetVisible() && !hidden) HideDockable(tool);
            }
        }
        finally { synchronizing = false; }
    }

    public void TogglePanel(string id)
    {
        if (!panels.TryGetValue(id, out var panel)) return;
        panel.SetVisible(!panel.GetVisible());
        if (panel.GetVisible()) ShowPanel(id);
    }

    private void ShowPanel(string id)
    {
        if (!panels.TryGetValue(id, out var panel)) return;
        panel.SetVisible(true);
        ApplyPanelVisibility();
        if (Walk(Layout).FirstOrDefault(d => d.Id == id) is { } tool) Activate(tool);
    }

    private void Activate(IDockable dockable)
    {
        if (synchronizing) return;
        synchronizing = true;
        try
        {
            if (IsDockablePinned(dockable)) PreviewPinnedDockable(dockable);
            SetActiveDockable(dockable);
            SetFocusedDockable(dockable.Owner as IDock ?? Layout, dockable);
            FindRoot(dockable, _ => true)?.Window?.SetActive();
        }
        finally { synchronizing = false; }
    }

    private void PanelsChanged(object? sender, PropertyChangedEventArgs e)
    {
        ApplyPanelVisibility();
        var id = e.PropertyName switch
        {
            nameof(EditionPanelViewModel.SessionExplorerPanelVisible) => "SessionExplorer",
            nameof(EditionPanelViewModel.AssetViewPanelVisible) => "AssetView",
            nameof(EditionPanelViewModel.AssetLogPanelVisible) => "AssetLog",
            nameof(EditionPanelViewModel.BuildLogPanelVisible) => "BuildLog",
            nameof(EditionPanelViewModel.PropertyGridPanelVisible) => "PropertyGrid",
            nameof(EditionPanelViewModel.AssetPreviewPanelVisible) => "AssetPreview",
            nameof(EditionPanelViewModel.ActionHistoryPanelVisible) => "ActionHistory",
            nameof(EditionPanelViewModel.ReferencesPanelVisible) => "References",
            _ => null
        };
        if (id is not null && panels[id].GetVisible() && !synchronizing) ShowPanel(id);
    }

    private void EditorsChanged(object? sender, NotifyCollectionChangedEventArgs e) => SynchronizeEditors();
    private void EditorCollectionChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(EditorCollectionViewModel.ActiveEditor)) SynchronizeEditors();
    }
    private void AssetChanged(object? sender, PropertyChangedEventArgs e) => UpdateDocumentTitles();
    private void EditorChanged(object? sender, PropertyChangedEventArgs e) => UpdateDocumentTitles();
    private void BuildChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == "IsBusy" && projectBuild?.IsBusy == true) ShowPanel("BuildLog");
    }
    private void AssetLogChanged(object? sender, PropertyChangedEventArgs e) => UpdateAssetLogTitle();
    private void UpdateAssetLogTitle()
    {
        if (Walk(Layout).FirstOrDefault(d => d.Id == "AssetLog") is { } log)
            log.Title = $"Asset errors ({session?.AssetLog.ErrorCount ?? 0})";
    }

    internal static IEnumerable<IDockable> Walk(IDockable dockable)
    {
        var visited = new HashSet<IDockable>();
        return Visit(dockable);
        IEnumerable<IDockable> Visit(IDockable item)
        {
            if (!visited.Add(item)) yield break;
            yield return item;
            if (item is IDock dock)
                foreach (var child in dock.VisibleDockables?.ToArray() ?? [])
                    foreach (var descendant in Visit(child)) yield return descendant;
            if (item is not IRootDock root) yield break;
            foreach (var list in new[] { root.HiddenDockables, root.LeftPinnedDockables, root.RightPinnedDockables, root.TopPinnedDockables, root.BottomPinnedDockables })
                foreach (var child in list?.ToArray() ?? [])
                    foreach (var descendant in Visit(child)) yield return descendant;
            foreach (var window in root.Windows?.ToArray() ?? [])
                if (window.Layout is { } layout)
                    foreach (var descendant in Visit(layout)) yield return descendant;
        }
    }

    public void Dispose()
    {
        disposed = true;
        main.Panels.PropertyChanged -= PanelsChanged;
        if (session?.EditorCollection is { } editors)
        {
            editors.Editors.CollectionChanged -= EditorsChanged;
            editors.PropertyChanged -= EditorCollectionChanged;
        }
        if (projectBuild is { } build) build.PropertyChanged -= BuildChanged;
        if (session?.AssetLog is { } log) log.PropertyChanged -= AssetLogChanged;
        foreach (var editor in documents.Keys)
        {
            editor.Asset.PropertyChanged -= AssetChanged;
            editor.PropertyChanged -= EditorChanged;
        }
        foreach (var window in Layout.Windows?.ToArray() ?? []) window.Exit();
        foreach (var dockable in Walk(Layout))
        {
            if (dockable.Context is Control view) view.DataContext = null;
            dockable.Context = null;
        }
    }
}
