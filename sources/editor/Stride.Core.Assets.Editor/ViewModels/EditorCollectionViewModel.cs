// Copyright (c) .NET Foundation and Contributors (https://dotnetfoundation.org/ & https://stride3d.net)
// Distributed under the MIT license. See the LICENSE.md file in the project root for more information.

using System.Diagnostics.CodeAnalysis;
using Stride.Core.Assets.Editor.Services;
using Stride.Core.Assets.Presentation.ViewModels;
using Stride.Core.Presentation.Collections;
using Stride.Core.Presentation.Commands;
using Stride.Core.Presentation.Services;
using Stride.Core.Presentation.ViewModels;

namespace Stride.Core.Assets.Editor.ViewModels;

public sealed class EditorCollectionViewModel : DispatcherViewModel, IAssetEditorsManager
{
    private AssetEditorViewModel? activeEditor;
    private readonly ObservableList<AssetEditorViewModel> editors = [];
    private readonly Dictionary<AssetViewModel, AssetEditorViewModel> openedAssets = new();
    private readonly Dictionary<AssetEditorViewModel, Task> closingEditors = new();

    public EditorCollectionViewModel(SessionViewModel session)
        : base(session.ServiceProvider)
    {
        Session = session;
        CloseEditorCommand = new AnonymousTaskCommand<AssetEditorViewModel?>(ServiceProvider, CloseEditor, editor => editor is { IsClosing: false });
        ServiceProvider.RegisterService(this);
    }

    public async Task CloseEditorsAsync()
    {
        ActiveEditor = null;
        foreach (var editor in editors.ToArray())
            await CloseEditorAsync(editor);
    }

    public ICommandBase CloseEditorCommand { get; }

    /// <summary>Closes the editing surface; its asset and unsaved changes remain in the session.</summary>
    public Task CloseEditorAsync(AssetEditorViewModel editor)
    {
        if (closingEditors.TryGetValue(editor, out var pending)) return pending;
        if (!editors.Contains(editor)) return Task.CompletedTask;
        editor.IsClosing = true;
        var task = CloseEditorCore(editor);
        closingEditors.Add(editor, task);
        return task;
    }

    private async Task CloseEditorCore(AssetEditorViewModel editor)
    {
        // Register the task before a synchronous DestroyAsync can finish or a tab binding can re-enter.
        await Task.Yield();
        try
        {
            await editor.DestroyAsync();
            var index = editors.IndexOf(editor);
            var wasActive = ActiveEditor == editor;
            openedAssets.Remove(editor.Asset);
            editors.Remove(editor);
            if (wasActive)
                ActiveEditor = editors.Take(Math.Max(0, index)).LastOrDefault(item => !item.IsClosing)
                    ?? editors.FirstOrDefault(item => !item.IsClosing);
        }
        finally
        {
            editor.IsClosing = false;
            closingEditors.Remove(editor);
        }
    }

    private async Task CloseEditor(AssetEditorViewModel? editor)
    {
        if (editor is null) return;
        try { await CloseEditorAsync(editor); }
        catch (Exception exception)
        {
            await ServiceProvider.Get<IDialogService>().MessageBoxAsync($"Unable to close the editor: {exception.Message}", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    public override void Destroy()
    {
        EnsureNotDestroyed(nameof(EditorCollectionViewModel));
        foreach (var editor in editors.ToArray())
            editor.Destroy();
        editors.Clear();
        openedAssets.Clear();
        ActiveEditor = null;
        ServiceProvider.UnregisterService(this);
        base.Destroy();
    }

    public AssetEditorViewModel? ActiveEditor
    {
        get => activeEditor;
        set => SetValue(ref activeEditor, value);
    }

    public IReadOnlyObservableCollection<AssetEditorViewModel> Editors => editors;

    public SessionViewModel Session { get; }

    public void OpenAssetEditor(AssetViewModel asset)
    {
        if (openedAssets.TryGetValue(asset, out var editor))
        {
             if (!editor.IsClosing) ActiveEditor = editor;
             return;
        }

        var editorType = ServiceProvider.TryGet<IAssetsPluginService>()?.GetEditorViewModelType(asset.GetType());
        if (editorType is not null)
        {
            editor = (AssetEditorViewModel)Activator.CreateInstance(editorType, asset)!;
            openedAssets.Add(asset, editor);
            editors.Add(editor);
            ActiveEditor = editor;
        }
    }

    public bool TryGetAssetEditor<TEditor>(AssetViewModel asset, [MaybeNullWhen(false)] out TEditor assetEditor) where TEditor : AssetEditorViewModel
    {
        if (openedAssets.TryGetValue(asset, out var found) && found is TEditor editor)
        {
            assetEditor = editor;
            return true;
        }

        assetEditor = null;
        return false;
    }
}
