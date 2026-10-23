// Copyright (c) .NET Foundation and Contributors (https://dotnetfoundation.org/ & https://stride3d.net)
// Distributed under the MIT license. See the LICENSE.md file in the project root for more information.

using System.Reflection;
using Avalonia;
using Avalonia.Headless;
using Avalonia.Themes.Fluent;
using Avalonia.Threading;
using Dock.Avalonia.Controls;
using Dock.Avalonia.Themes.Fluent;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.VisualTree;
using Stride.GameStudio.Avalonia.Layout;
using Dock.Model.Controls;
using Dock.Model.Core;
using Dock.Model.Mvvm;
using Stride.Core.Presentation.ViewModels;
using Stride.GameStudio.Avalonia.ViewModels;
using Stride.GameStudio.Avalonia.Views;

AppBuilder.Configure<Application>().UseHeadless(new AvaloniaHeadlessPlatformOptions()).SetupWithoutStarting();
Application.Current!.Styles.Add(new FluentTheme());
Application.Current.Styles.Add(new DockFluentTheme());
Application.Current.DataTemplates.Add(new DockContentTemplate());

// Reproduce the crash with the same view requested by two live presenters.
var template = new DockContentTemplate();
foreach (var dockable in new IDockable[] { new Dock.Model.Mvvm.Controls.Tool(), new Dock.Model.Mvvm.Controls.Document() })
{
    var context = new object();
    var view = new UserControl { Content = new Grid(), DataContext = context };
    dockable.Context = view;
    Check(template.Match(dockable), "Dock content template must match both tools and documents.");
    var first = new ContentPresenter { Content = dockable, ContentTemplate = template };
    var second = new ContentPresenter { Content = dockable, ContentTemplate = template };
    first.UpdateChild();
    Check(ReferenceEquals(first.Child, view), "The first presenter must show the view.");
    second.UpdateChild();
    second.Measure(new Size(400, 300));
    Check(first.Child is null && ReferenceEquals(second.Child, view), "Moving must release the previous visual parent before attaching the view.");
    Check(ReferenceEquals(view.DataContext, context), "Moving must preserve the editor view model.");
    first.Content = dockable;
    first.UpdateChild();
    first.Measure(new Size(400, 300));
    Check(second.Child is null && ReferenceEquals(first.Child, view), "Moving back must reuse the same view safely.");
    first.Content = null;
    first.UpdateChild();
    Check(view.GetVisualParent() is null, "Closing the presenter must release the view.");
}
Check(!template.Match(new Dock.Model.Mvvm.Controls.RootDock()), "Layout containers must retain Dock's own templates.");

// Exercise the real integration without starting the renderer or loading a game project.
var assembly = typeof(MainView).Assembly;
var mainType = assembly.GetType("Stride.GameStudio.Avalonia.ViewModels.MainViewModel", true)!;
var layoutType = assembly.GetType("Stride.GameStudio.Avalonia.Layout.DockingLayoutManager", true)!;
var main = Activator.CreateInstance(mainType, ViewModelServiceProvider.NullServiceProvider)!;
var panels = (EditionPanelViewModel)mainType.GetProperty("Panels")!.GetValue(main)!;
panels.SetAllPanelVisible();
Factory Create(bool restore) => (Factory)Activator.CreateInstance(layoutType, main, (Func<string, Control>)(_ => new Border()), restore)!;
IRootDock Root(Factory factory) => (IRootDock)layoutType.GetProperty("Layout")!.GetValue(factory)!;
void Save(Factory factory) => layoutType.GetMethod("SaveLayout")!.Invoke(factory, null);
IDockable Find(Factory factory, string id) => ((IEnumerable<IDockable>)layoutType.GetMethod("Walk", BindingFlags.NonPublic | BindingFlags.Static)!.Invoke(null, [Root(factory)])!).Single(d => d.Id == id);
void Check(bool condition, string message) { if (!condition) throw new Exception(message); }

using (var lifetime = (IDisposable)Create(false))
{
    var factory = (Factory)lifetime;
    var root = Root(factory);
    Check(root.Windows is null || root.Windows.Count == 0, "The default layout must not contain floating windows.");
    var properties = Find(factory, "PropertyGrid");
    var propertyPane = (IDock)properties.Owner!;
    factory.CloseDockable(properties);
    Check(propertyPane.VisibleDockables!.Count == 0, "The property pane must be empty after closing its only tool.");
    propertyPane.ActiveDockable = null;
    var hiddenCount = root.HiddenDockables!.Count;
    // The chrome command can arrive again after ActiveDockable has become null.
    factory.CloseDockable(propertyPane.ActiveDockable!);
    Check(root.HiddenDockables.Count == hiddenCount, "Closing an empty pane must not change the layout.");
    panels.PropertyGridPanelVisible = true;
    Check(ReferenceEquals(properties.Owner, propertyPane), "A panel must still reopen after an empty close command.");
    var assets = Find(factory, "AssetView");
    var solution = Find(factory, "SessionExplorer");
    var owner = assets.Owner;
    factory.CloseDockable(assets);
    Check(!panels.AssetViewPanelVisible && root.HiddenDockables!.Contains(assets), "Close must hide the panel and update the View menu state.");
    panels.AssetViewPanelVisible = true;
    Check(ReferenceEquals(assets.Owner, owner) && !root.HiddenDockables!.Contains(assets), "Reopen must restore the original pane.");

    factory.PinDockable(solution);
    Check(factory.IsDockablePinned(solution), "Pin must put the panel in an auto-hide strip.");
    Check(panels.SessionExplorerPanelVisible, "Auto-hide must not mark the panel closed.");
    factory.PinDockable(solution);
    factory.MoveDockable((IDock)assets.Owner!, (IDock)solution.Owner!, assets, solution);
    Check(ReferenceEquals(assets.Owner, solution.Owner), "Moving a tab must change its pane.");

    factory.PinDockable(assets);
    panels.BuildLogPanelVisible = false;
    Save(factory);
}
using (var lifetime = (IDisposable)Create(true))
{
    var factory = (Factory)lifetime;
    var assets = Find(factory, "AssetView");
    Check(factory.IsDockablePinned(assets), "Auto-hide must survive serialization.");
    Check(!panels.BuildLogPanelVisible, "Hidden panel state must survive restoration.");
    var workspace = Root(factory).ActiveDockable;
    layoutType.GetMethod("Activate", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(factory, [assets]);
    Check(ReferenceEquals(Root(factory).ActiveDockable, workspace), "Activating an auto-hidden tool must preserve the workspace.");
    panels.BuildLogPanelVisible = true;
    Check(Find(factory, "BuildLog").Owner is IToolDock, "A restored hidden panel must return to its pane.");
    factory.PinDockable(assets);
    Check(assets.Owner is IToolDock { Id: "SolutionTools" }, "Unpin after restart must return to the original pane.");
    factory.HostWindowLocator![nameof(IDockWindow)] = () => null; // Exercise the floating model without a native display.
    factory.FloatDockable(assets);
    Check(Root(factory).Windows?.Count == 1, "Floating must create a registered window.");
    var window = Root(factory).Windows![0];
    window.X = 120; window.Y = 80; window.Width = 640; window.Height = 480;
    Save(factory);
    factory.CloseWindow(window);
    factory.RemoveWindow(window);
    Check(!panels.AssetViewPanelVisible, "Closing a floating window must update panel visibility.");
    panels.AssetViewPanelVisible = true;
    Check(ReferenceEquals(factory.FindRoot(Find(factory, "AssetView"), _ => true), Root(factory)), "A tool from a closed floating window must reopen in the workspace.");
    factory.DockAsDocument(assets);
    Check(assets.Owner is IDocumentDock, "Tool panels must also support docking as documents.");
}
// Avoid presenting native windows while checking the saved window tree.
var settings = assembly.GetType("Stride.GameStudio.Avalonia.Settings.GameStudioInternalSettings", true)!;
var key = (Stride.Core.Settings.SettingsKey<string>)settings.GetField("DockLayout")!.GetValue(null)!;
var json = key.GetValue();
var state = new Dock.Serializer.DockSerializer().Deserialize<object>(json)!;
var saved = (IRootDock)state.GetType().GetProperty("Root")!.GetValue(state)!;
Check(saved.Windows is { Count: 1 } && saved.Windows[0].Width == 640, "Floating window bounds must survive serialization.");
Check(json.Contains("AssetView") && !json.Contains("Avalonia.Controls.Border"), "Layouts must contain IDs, not UI instances.");
using (var lifetime = (IDisposable)Create(false))
{
    var factory = (Factory)lifetime;
    var assets = Find(factory, "AssetView");
    var context = new object();
    var view = new UserControl { DataContext = context, Content = new TextBlock { Text = "Asset content" } };
    assets.Context = view;
    var workspace = new Window { Content = new DockControl { Layout = Root(factory) }, Width = 1000, Height = 700 };
    workspace.Show();
    Dispatcher.UIThread.RunJobs();
    for (var attempt = 0; attempt < 3; attempt++)
    {
        // Exercise the underlying reparenting even though the UI now prevents redundant Float.
        assets.CanFloat = true;
        factory.FloatDockable(assets);
        Dispatcher.UIThread.RunJobs();
        AvaloniaHeadlessPlatform.ForceRenderTimerTick(1);
        Dispatcher.UIThread.RunJobs();
        Check(Root(factory).Windows is { Count: 1 }, "Repeated Float must replace the previous window without leaving orphan windows.");
        Check(ReferenceEquals(assets.Context, view) && ReferenceEquals(view.DataContext, context), "Repeated Float must preserve the asset view and its data context.");
        Check(TopLevel.GetTopLevel(view) is HostWindow { IsVisible: true } && ReferenceEquals(TopLevel.GetTopLevel(view), Root(factory).Windows![0].Host), "Repeated Float must display the asset view in the current visible host window.");
        Check(panels.AssetViewPanelVisible, "Repeated Float must not close AssetView.");
    }
    void CheckFloatMenu(bool visible)
    {
        AvaloniaHeadlessPlatform.ForceRenderTimerTick(1);
        Dispatcher.UIThread.RunJobs();
        var host = (Window)factory.FindRoot(assets, _ => true)!.Window!.Host!;
        var chrome = host.GetVisualDescendants().OfType<ToolChromeControl>()
            .First(item => ReferenceEquals(item.DataContext, assets.Owner));
        var menu = (MenuFlyout)chrome.ToolFlyout!;
        menu.ShowAt(chrome);
        Dispatcher.UIThread.RunJobs();
        Check(((MenuItem)menu.Items[0]!).IsVisible == visible, "The actual panel menu must reflect Float availability.");
        menu.Hide();
    }
    Check(!assets.CanFloat, "Float must be unavailable for the sole panel in a floating window.");
    CheckFloatMenu(false);
    var singleWindow = Root(factory).Windows![0];
    factory.FloatDockable(assets);
    Check(ReferenceEquals(singleWindow, Root(factory).Windows![0]), "A redundant Float must not create another window.");
    var logs = Find(factory, "BuildLog");
    factory.MoveDockable((IDock)logs.Owner!, (IDock)assets.Owner!, logs, assets);
    Dispatcher.UIThread.RunJobs();
    Check(assets.CanFloat && logs.CanFloat, "Float must remain available to separate tabs in a floating window.");
    CheckFloatMenu(true);
    factory.FloatDockable(logs);
    Dispatcher.UIThread.RunJobs();
    Check(Root(factory).Windows?.Count == 2 && !assets.CanFloat && !logs.CanFloat, "Separating the last two tabs must disable redundant Float in both windows.");
    var assetWindow = (Window)factory.FindRoot(assets, _ => true)!.Window!.Host!;
    factory.DockAsDocument(assets);
    Dispatcher.UIThread.RunJobs();
    Check(assets.Owner is IDocumentDock && ReferenceEquals(factory.FindRoot(assets, _ => true), Root(factory)), "Dock as tabbed Document must move a floating panel to the main document area.");
    Check(!assetWindow.IsVisible && Root(factory).Windows?.Count == 1, "Docking the last floating panel must close its old window.");
    Check(assets.CanFloat, "Docking back in the main workspace must restore Float.");
    var logWindow = (Window)factory.FindRoot(logs, _ => true)!.Window!.Host!;
    factory.CloseDockable(logs);
    Dispatcher.UIThread.RunJobs();
    Check(!logWindow.IsVisible && Root(factory).Windows?.Count == 0, "Closing the last floating tool must close its empty window.");
    panels.BuildLogPanelVisible = true;
    Dispatcher.UIThread.RunJobs();
    Check(ReferenceEquals(factory.FindRoot(logs, _ => true), Root(factory)) && Root(factory).Windows?.Count == 0, "View must reopen a closed floating panel in the main workspace.");
    workspace.Close();
}
// Older layouts may contain a floating window whose last panel was only hidden.
using (var lifetime = (IDisposable)Create(false))
{
    var factory = (Factory)lifetime;
    factory.HostWindowLocator![nameof(IDockWindow)] = () => null;
    var emptyWindow = factory.CreateDockWindow();
    emptyWindow.Layout = factory.CreateRootDock();
    factory.AddWindow(Root(factory), emptyWindow);
    Save(factory);
}
using (var lifetime = (IDisposable)Create(true))
    Check(Root((Factory)lifetime).Windows?.Count is null or 0, "Restoration must discard empty windows saved by older layouts.");
key.SetValue("{invalid layout");
using (var lifetime = (IDisposable)Create(true))
    Check(Find((Factory)lifetime, "AssetView") is ITool, "A corrupt saved layout must fall back to the default.");
Console.WriteLine("PASS: visual reparenting, view-model preservation, close/reopen, move, auto-hide, floating windows, repeated Float, layout round-trip and UI exclusion.");
