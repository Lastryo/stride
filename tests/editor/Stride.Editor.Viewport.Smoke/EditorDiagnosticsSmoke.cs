// Copyright (c) .NET Foundation and Contributors (https://dotnetfoundation.org/ & https://stride3d.net)
// Distributed under the MIT license. See the LICENSE.md file in the project root for more information.

using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.VisualTree;
using Stride.Core.Assets.Editor.Components.Status;
using Stride.Core.Assets.Editor.ViewModels;
using Stride.Core.Diagnostics;
using Stride.Core.Assets.Presentation.ViewModels;
using Stride.Core.Presentation.Avalonia.Controls;
using Stride.Core.Presentation.Collections;

internal static class EditorDiagnosticsSmoke
{
    public static async Task Check(SessionViewModel session, Window window)
    {
        var view = (Control)window.Content!;
        var tabs = view.FindControl<TabControl>("AssetTabs")!;
        var originalTab = tabs.SelectedIndex;
        var marker = "DiagnosticsSmoke_" + Guid.NewGuid().ToString("N");
        var logger = GlobalLogger.GetLogger(marker);
        var worker = Task.Run(() =>
        {
            for (var index = 0; index < 100; index++) logger.Info(marker + index);
        });
        var completedWithoutUi = worker.Wait(TimeSpan.FromSeconds(2));
        await worker.WaitAsync(TimeSpan.FromSeconds(5));
        if (!completedWithoutUi) throw new Exception("Output logging blocked on the UI thread.");
        logger.Warning(marker + " warning");
        await Wait(() => session.OutputLog.Messages.Count(x => x.Text.Contains(marker)) == 101);
        tabs.SelectedIndex = 2;
        await Task.Delay(100);
        var output = view.FindControl<TextLogViewer>("OutputViewer")!;
        if (!ReferenceEquals(output.LogMessages, session.OutputLog.Messages)
            || !output.GetVisualDescendants().OfType<SelectableTextBlock>().Any(x => x.Inlines?.OfType<Run>().Any(run => run.Text?.Contains(marker) == true) == true))
            throw new Exception("Output did not render the messages from its bound logger.");

        var selection = session.AssetCollection.SelectedAssets.ToArray();
        var locations = session.AssetCollection.SelectedLocations.ToArray();
        var asset = session.AllAssets.First(x => x.IsEditable);
        var dependencyIds = asset.Dependencies.RecursiveReferencedAssets.Select(x => x.Id).ToHashSet();
        var other = session.AllAssets.First(x => x.IsEditable && x.Id != asset.Id && !dependencyIds.Contains(x.Id));
        var key = LogKey.Get(asset.Id, marker);
        var otherKey = LogKey.Get(other.Id, marker);
        var errors = new LoggerResult();
        errors.Info(marker + " info");
        errors.Error(marker + " error");
        session.AssetLog.AddLogger(key, errors);
        session.AssetLog.GetLogger(otherKey)!.Warning(marker + " other");
        try
        {
            Select(asset);
            await Wait(() => Has(" error") && !Has(" other"));
            if (Has(" info")) throw new Exception("Asset errors included an informational message.");
            errors.Warning(marker + " live");
            await Wait(() => Has(" live"));
            for (var index = 0; index < 8; index++) { Select(other); Select(asset); }
            await Task.Delay(700);
            await Wait(() => Has(" error") && Has(" live") && !Has(" other"));
            tabs.SelectedIndex = 1;
            await Task.Delay(100);
            var errorTab = view.FindControl<TabItem>("AssetErrorsTab")!;
            if ((string?)errorTab.Header != $"Asset errors ({session.AssetLog.ErrorCount})"
                || session.AssetLog.ErrorCount != session.AssetLog.FilteredMessages.Count
                || !ReferenceEquals(view.FindControl<TextLogViewer>("AssetErrorsViewer")!.LogMessages, session.AssetLog.FilteredMessages))
                throw new Exception("Asset errors tab, counter and viewer do not agree.");
            session.AssetLog.ClearMessages(key);
            await Wait(() => !Has(" error") && !Has(" live"));
            session.AssetCollection.SelectAssets([]);
            await Wait(() => Has(" other"));
            Console.WriteLine("PASS: live Output rendered 101 worker/UI messages without blocking; Asset errors tracks selection, live logs, clearing, all-assets view and its counter.");
        }
        finally
        {
            session.AssetLog.RemoveLogger(key);
            session.AssetLog.RemoveLogger(otherKey);
            session.AssetCollection.SelectAssets([]);
            session.AssetCollection.SelectAssets(selection);
            session.AssetCollection.SelectedLocations.Clear();
            foreach (var location in locations) session.AssetCollection.SelectedLocations.Add(location);
            tabs.SelectedIndex = originalTab;
        }

        var status = session.Main.Status;
        var tokens = new List<int>();
        int Start(string message, JobPriority priority, int count = -1)
        {
            var token = status.NotifyBackgroundJobStarted(message, priority, count);
            tokens.Add(token);
            return token;
        }
        try
        {
            Start("Background diagnostics", JobPriority.Background);
            Start("Editor diagnostics", JobPriority.Editor);
            var compile = Start("Diagnostics {0}", JobPriority.Compile, 5);
            var expected = status.CurrentJob;
            var newer = Start("Newer compile", JobPriority.Compile);
            status.NotifyBackgroundJobFinished(newer);
            if (status.CurrentJob != expected) throw new Exception("Finishing a job selected a lower-priority background task.");
            status.NotifyBackgroundJobProgress(compile, 3, true);
            await Task.Delay(100);
            var message = view.FindControl<TextBlock>("BackgroundJobMessage")!;
            var progress = view.FindControl<ProgressBar>("BackgroundJobProgress")!;
            if (message.Text != "Diagnostics 3" || !progress.IsVisible || progress.IsIndeterminate || progress.Value != 3 || progress.Maximum != 5
                || progress.Bounds.Width < 90)
                throw new Exception("Background job message/progress did not reach the status bar.");
            Console.WriteLine("PASS: status bar renders job message and progress; finishing a job retains the highest remaining priority.");
        }
        finally { foreach (var token in tokens) status.NotifyBackgroundJobFinished(token); }

        var clearedLogger = new LoggerResult();
        var cleared = new LoggerViewModel(session.ServiceProvider, clearedLogger);
        clearedLogger.Warning("Obsolete pending message");
        cleared.ClearMessages(clearedLogger);
        clearedLogger.Warning("Current message");
        await Wait(() => cleared.Messages.Count == 1);
        if (cleared.Messages.Single().Text != "Current message")
            throw new Exception("Clearing a logger resurrected an obsolete pending message.");
        cleared.Destroy();

        var pendingLogger = new LoggerResult();
        var disposed = new LoggerViewModel(session.ServiceProvider, pendingLogger);
        pendingLogger.Warning("Pending during disposal");
        disposed.Destroy();
        await Task.Delay(600);
        if (disposed.Messages.Count != 0) throw new Exception("A disposed logger published its delayed messages.");
        Select(asset);
        await session.CloseAsync().WaitAsync(TimeSpan.FromSeconds(45));
        var outputCount = session.OutputLog.Messages.Count;
        logger.Warning(marker + " after close");
        await Task.Delay(600);
        if (session.OutputLog.Messages.Count != outputCount) throw new Exception("Closed session still receives global Output messages.");
        Console.WriteLine("PASS: delayed logger updates stop at disposal; closed session detaches from global Output.");

        bool Has(string suffix) => session.AssetLog.FilteredMessages.Any(x => x.Text == marker + suffix);
        void Select(AssetViewModel selected) => session.AssetCollection.SelectAssets([selected]);
    }

    private static async Task Wait(Func<bool> condition)
    {
        for (var attempt = 0; attempt < 200; attempt++)
        {
            if (condition()) return;
            await Task.Delay(25);
        }
        throw new TimeoutException("Diagnostics did not reach the expected state.");
    }
}
