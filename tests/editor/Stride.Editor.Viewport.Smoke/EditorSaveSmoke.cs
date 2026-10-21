// Copyright (c) .NET Foundation and Contributors (https://dotnetfoundation.org/ & https://stride3d.net)
// Distributed under the MIT license. See the LICENSE.md file in the project root for more information.

using Avalonia.Controls;
using Avalonia.Input;
using Stride.Assets.Editor.ViewModels;
using Stride.Core.Assets.Editor.ViewModels;
using Stride.Core.Diagnostics;
using Stride.Core.Presentation.Commands;
using Stride.Engine;

internal static class EditorSaveSmoke
{
    public static async Task Check(SessionViewModel session, EntityHierarchyEditorViewModel editor, Window window)
    {
        var model = window.DataContext!;
        var save = model.GetType().GetMethod("SaveSessionAsync")!;
        var command = (ICommandBase)model.GetType().GetProperty("SaveCommand")!.GetValue(model)!;
        var view = (Control)window.Content!;
        var menu = view.FindControl<MenuItem>("SaveMenuItem")!;
        var button = view.FindControl<Button>("SaveButton")!;
        if (!command.CanExecute(null) || !ReferenceEquals(menu.Command, command) || !ReferenceEquals(button.Command, command)
            || menu.HotKey?.Key != Key.S || menu.HotKey.KeyModifiers != KeyModifiers.Control)
            throw new Exception("Save menu, toolbar or Ctrl+S is not connected.");
        var scene = editor.Asset;
        var item = editor.HierarchyRoot.InnerSubEntities.First();
        var node = session.AssetNodeContainer.GetOrCreateNode(item.AssetSideEntity)[nameof(Entity.Name)];
        var originalName = item.AssetSideEntity.Name;
        var name = "SaveSmoke_" + Guid.NewGuid().ToString("N");
        var path = (string)scene.AssetItem.FullPath;
        var backup = path + ".save-smoke-backup";
        async Task Save()
        {
            if (!await (Task<bool>)save.Invoke(model, null)!) throw new Exception("Saving through shell failed.");
        }
        try
        {
            using (var transaction = session.ActionService!.CreateTransaction()) node.Update(name);
            await Task.Delay(100);
            if (!scene.IsDirty || !scene.AssetItem.IsDirty) throw new Exception("Editing did not dirty both asset and view model.");
            await Save();
            if (scene.IsDirty || scene.AssetItem.IsDirty || !File.ReadAllText(path).Contains(name))
                throw new Exception("Save did not write the change or clear dirty state.");
            session.ActionService.Undo();
            if (item.AssetSideEntity.Name != originalName || !scene.IsDirty || !scene.AssetItem.IsDirty)
                throw new Exception("Undo after save did not restore the value and dirty state.");
            session.ActionService.Redo();
            if (item.AssetSideEntity.Name != name || scene.IsDirty || scene.AssetItem.IsDirty)
                throw new Exception("Redo to the save point did not restore clean state.");
            session.ActionService.Undo();
            var errors = new LoggerResult();
            File.Move(path, backup);
            Directory.CreateDirectory(path);
            try
            {
                if (session.Save(errors) || !errors.HasErrors || !scene.IsDirty || !scene.AssetItem.IsDirty)
                    throw new Exception("Failed save did not preserve dirty state and report an error.");
            }
            finally
            {
                Directory.Delete(path);
                File.Move(backup, path);
            }
            var saveError = errors.Messages.First(message => message.Type >= LogMessageType.Error);
            for (var attempt = 0; attempt < 100 && !session.OutputLog.Messages.Contains(saveError); attempt++)
                await Task.Delay(25);
            if (!session.OutputLog.Messages.Contains(saveError))
                throw new Exception("The save error did not reach Output.");
            await Save();
            if (scene.IsDirty || scene.AssetItem.IsDirty || File.ReadAllText(path).Contains(name))
                throw new Exception("Retry did not save the restored original value.");
            Console.WriteLine("PASS: Save menu/toolbar/Ctrl+S, disk contents, Undo/Redo save point, write failure preserves dirty state and retry succeeds.");
        }
        finally
        {
            if (File.Exists(backup))
            {
                if (Directory.Exists(path)) Directory.Delete(path);
                File.Move(backup, path, true);
            }
        }
    }
}
