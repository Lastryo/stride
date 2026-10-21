// Copyright (c) .NET Foundation and Contributors (https://dotnetfoundation.org/ & https://stride3d.net)
// Distributed under the MIT license. See the LICENSE.md file in the project root for more information.

using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Stride.Assets.Editor.ViewModels;
using Stride.Core.Assets.Editor.ViewModels;
using Stride.Core.Diagnostics;
using Stride.Core.IO;
using Stride.Core.Presentation.Avalonia.Windows;
using Stride.Core.Presentation.Services;
using Stride.Engine;

internal static class EditorCloseSmoke
{
    public static async Task Check(SessionViewModel session, EntityHierarchyEditorViewModel editor, Window window,
        IClassicDesktopStyleApplicationLifetime lifetime, bool discard)
    {
        var model = window.DataContext!;
        var close = model.GetType().GetMethod("CloseSessionAsync")!;
        Task<bool> Close() => (Task<bool>)close.Invoke(model, null)!;
        var scene = editor.Asset;
        var node = session.AssetNodeContainer.GetOrCreateNode(editor.HierarchyRoot.InnerSubEntities.First().AssetSideEntity)[nameof(Entity.Name)];
        var path = (string)scene.AssetItem.FullPath;
        var backup = path + ".close-smoke-backup";
        if (!session.Save(new LoggerResult()) || session.HasUnsavedChanges)
            throw new Exception("A saved session reports unsaved changes.");
        var before = File.ReadAllText(path);
        var name = "CloseSmoke_" + Guid.NewGuid().ToString("N");
        using (var transaction = session.ActionService!.CreateTransaction()) node.Update(name);
        if (!session.HasUnsavedChanges) throw new Exception("Close cannot detect an edited asset.");

        Console.WriteLine("Close check: cancel/shared task");
        var cancelled = Close();
        var duplicate = Close();
        if (!ReferenceEquals(cancelled, duplicate)) throw new Exception("Concurrent close requests do not share a task.");
        (await Dialog()).ButtonCommand.Execute((int)MessageBoxResult.Cancel);
        if (await cancelled) throw new Exception("Cancel accepted session closure.");
        CheckOpen();

        // Exercise File -> Close without allowing its restart path to run.
        var fileClose = (Task)model.GetType().GetMethod("OnClose", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!.Invoke(model, null)!;
        (await Dialog()).Close();
        await fileClose;
        CheckOpen();

        var opening = (Task<bool?>)model.GetType().GetMethod("OpenSession")!.Invoke(model,
            new object?[] { new UFile(lifetime.Args![0]), CancellationToken.None })!;
        (await Dialog()).ButtonCommand.Execute((int)MessageBoxResult.Cancel);
        if (await opening != false) throw new Exception("Open project ignored cancellation.");
        CheckOpen();

        Console.WriteLine("Close check: window cancel");
        window.Close();
        var windowPrompt = await Dialog();
        window.Close();
        await Task.Delay(50);
        if (lifetime.Windows.OfType<MessageBox>().Count() != 1) throw new Exception("Repeated window close duplicated the prompt.");
        windowPrompt.ButtonCommand.Execute((int)MessageBoxResult.Cancel);
        await Task.Delay(100);
        CheckOpen();

        Console.WriteLine("Close check: failed save");
        File.Move(path, backup);
        Directory.CreateDirectory(path);
        try
        {
            var failed = Close();
            (await Dialog()).ButtonCommand.Execute((int)MessageBoxResult.Yes);
            var error = await Dialog();
            if (!error.ButtonsSource.Any(button => button.Result == (int)MessageBoxResult.OK))
                throw new Exception("Saving failure did not show an error dialog.");
            error.ButtonCommand.Execute((int)MessageBoxResult.OK);
            if (await failed) throw new Exception("Failed save accepted session closure.");
            CheckOpen();
        }
        finally
        {
            Directory.Delete(path);
            File.Move(backup, path);
        }
        if (File.ReadAllText(path) != before) throw new Exception("Cancelled/failed closure changed the asset file.");
        Console.WriteLine("Close check: final save/discard");
        var accepted = Close();
        (await Dialog()).ButtonCommand.Execute((int)(discard ? MessageBoxResult.No : MessageBoxResult.Yes));
        if (!await accepted.WaitAsync(TimeSpan.FromSeconds(45)) || model.GetType().GetProperty("Session")!.GetValue(model) != null)
            throw new Exception("Confirmed closure did not release the session.");
        if (discard ? File.ReadAllText(path) != before : !File.ReadAllText(path).Contains(name))
            throw new Exception("Save/discard closure wrote unexpected asset contents.");
        Console.WriteLine($"PASS: close cancellation, dialog dismissal, project-open cancellation, repeated window close, failed save retains session; {(discard ? "discard leaves disk unchanged" : "save writes changes before closing")}.");

        void CheckOpen()
        {
            var build = model.GetType().GetProperty("ProjectBuild")!.GetValue(model)!;
            if (!(bool)build.GetType().GetProperty("CanSelectProject")!.GetValue(build)!)
                throw new Exception("Cancelled closure left project selection/build locked.");
            if (!ReferenceEquals(model.GetType().GetProperty("Session")!.GetValue(model), session)
                || !session.EditorCollection.Editors.Contains(editor) || editor.Viewport!.Controller.GameStopped.IsCompleted
                || !window.IsVisible || !scene.IsDirty)
                throw new Exception("Cancellation or save failure destroyed the active session/editor.");
        }
        async Task<MessageBox> Dialog()
        {
            for (var attempt = 0; attempt < 200; attempt++)
            {
                if (lifetime.Windows.OfType<MessageBox>().FirstOrDefault(dialog => dialog.IsVisible) is { } dialog)
                    return dialog;
                await Task.Delay(25);
            }
            throw new TimeoutException("No close/error dialog appeared.");
        }
    }
}
