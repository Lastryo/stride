// Copyright (c) .NET Foundation and Contributors (https://dotnetfoundation.org/ & https://stride3d.net)
// Distributed under the MIT license. See the LICENSE.md file in the project root for more information.

using System.Collections.Specialized;
using Stride.Assets.Editor.SpriteEditor;
using Stride.Assets.Presentation.ViewModels;
using Stride.Assets.Sprite;
using Stride.Core.Assets;
using Stride.Core.Assets.Editor.Annotations;
using Stride.Core.Assets.Editor.Components.CopyPasteProcessors;
using Stride.Core.Assets.Editor.Services;
using Stride.Core.Assets.Editor.ViewModels;
using Stride.Core.IO;
using Stride.Core.Mathematics;
using Stride.Core.Presentation.Collections;
using Stride.Core.Presentation.Commands;
using Stride.Core.Presentation.Quantum;
using Stride.Core.Presentation.Services;
using Stride.Core.Quantum;

namespace Stride.Assets.Editor.ViewModels;

public enum SpriteSheetEditorToolMode { Region, ColorPick, MagicWand, SpriteCenter, Borders }

[AssetEditorViewModel<SpriteSheetViewModel>]
public sealed class SpriteSheetEditorViewModel : AssetEditorViewModel<SpriteSheetViewModel>
{
    private readonly IObjectNode spritesNode;
    private readonly MemberGraphNodeBinding<Color> colorKey;
    private readonly MemberGraphNodeBinding<bool> colorKeyEnabled;
    private SpriteSheetEditorToolMode toolMode;
    private bool highlight = true;
    private bool useTransparency;
    private bool lockBorders;
    private Color selectionColor = Color.Red;

    public SpriteSheetEditorViewModel(SpriteSheetViewModel asset) : base(asset)
    {
        var root = Session.AssetNodeContainer.GetNode(asset.Asset);
        spritesNode = root[nameof(SpriteSheetAsset.Sprites)].Target;
        colorKey = new(root[nameof(SpriteSheetAsset.ColorKeyColor)], nameof(ColorKey), OnPropertyChanging, OnPropertyChanged, UndoRedoService);
        colorKeyEnabled = new(root[nameof(SpriteSheetAsset.ColorKeyEnabled)], nameof(ColorKeyEnabled), OnPropertyChanging, OnPropertyChanged, UndoRedoService);
        useTransparency = !asset.Asset.ColorKeyEnabled;
        foreach (var sprite in asset.Asset.Sprites) Sprites.Add(new(this, sprite));
        spritesNode.ItemChanged += SpritesChanged;
        SelectedSprites.CollectionChanged += SelectionChanged;
        ServiceProvider.Get<SelectionService>().RegisterSelectionScope(
            id => Sprites.FirstOrDefault(s => s.Id == id), item => (item as SpriteInfoViewModel)?.Id, SelectedSprites);
        AddCommand = new AnonymousCommand(ServiceProvider, () => AddSprites([new SpriteInfo { Name = UniqueName("New sprite") }], "Add sprite"));
        DeleteCommand = new AnonymousCommand(ServiceProvider, Delete);
        DuplicateCommand = new AnonymousCommand(ServiceProvider, () => AddSprites(SelectedSprites.Cast<SpriteInfoViewModel>().Select(s => AssetCloner.Clone(s.Sprite)).ToArray(), "Duplicate sprites"));
        MoveUpCommand = new AnonymousCommand(ServiceProvider, () => Move(-1));
        MoveDownCommand = new AnonymousCommand(ServiceProvider, () => Move(1));
        SheetPropertiesCommand = new AnonymousTaskCommand(ServiceProvider, async () =>
        {
            SelectedSprites.Clear();
            await EditorProperties.GenerateSelectionPropertiesAsync([Asset]);
        });
    }

    public IUndoRedoService UndoRedoService => ServiceProvider.Get<IUndoRedoService>();
    public ObservableList<SpriteInfoViewModel> Sprites { get; } = [];
    public ObservableList<object> SelectedSprites { get; } = [];
    public SpriteInfoViewModel? CurrentSprite => SelectedSprites.Count == 1 ? SelectedSprites[0] as SpriteInfoViewModel : null;
    public SpriteSheetEditorToolMode ToolMode { get => toolMode; set => SetValue(ref toolMode, value); }
    public bool SelectionHighlightEnabled { get => highlight; set => SetValue(ref highlight, value); }
    public bool MagicWandUseTransparency { get => useTransparency; set => SetValue(ref useTransparency, value); }
    public Color SelectionColor { get => selectionColor; set => SetValue(ref selectionColor, value); }
    public bool LockBorders { get => lockBorders; set => SetValue(ref lockBorders, value); }
    public Color ColorKey { get => colorKey.Value; set => colorKey.Value = value; }
    public bool ColorKeyEnabled { get => colorKeyEnabled.Value; set => colorKeyEnabled.Value = value; }
    public ICommandBase AddCommand { get; }
    public ICommandBase DeleteCommand { get; }
    public ICommandBase DuplicateCommand { get; }
    public ICommandBase MoveUpCommand { get; }
    public ICommandBase MoveDownCommand { get; }
    public ICommandBase SheetPropertiesCommand { get; }

    private string UniqueName(string name)
    {
        var candidate = name;
        for (var i = 1; Sprites.Any(s => s.Name == candidate); i++) candidate = $"{name} {i}";
        return candidate;
    }

    public void Select(SpriteInfoViewModel? sprite)
    {
        SelectedSprites.Clear();
        if (sprite != null) SelectedSprites.Add(sprite);
    }

    public void AddSprites(IEnumerable<SpriteInfo> images, string transactionName)
    {
        if (!Asset.IsEditable) return;
        var added = images.ToArray();
        using (var transaction = UndoRedoService.CreateTransaction())
        {
            foreach (var sprite in added)
            {
                sprite.Name = UniqueName(sprite.Name ?? "Sprite");
                spritesNode.Add(sprite);
            }
            UndoRedoService.SetName(transaction, transactionName);
        }
        SelectedSprites.Clear();
        foreach (var sprite in added) SelectedSprites.Add(Sprites.First(s => ReferenceEquals(s.Sprite, sprite)));
    }

    public void ImportFiles(IEnumerable<UFile> paths, Func<UFile, Size2?> getSize)
    {
        var images = paths.Select(path =>
        {
            var size = getSize(path);
            return new SpriteInfo { Name = Path.GetFileNameWithoutExtension(path), Source = path,
                TextureRegion = new Rectangle(0, 0, size?.Width ?? 0, size?.Height ?? 0) };
        }).ToArray();
        AddSprites(images, "Import sprite images");
    }

    public void Delete()
    {
        if (!Asset.IsEditable || SelectedSprites.Count == 0) return;
        var selected = SelectedSprites.Cast<SpriteInfoViewModel>().ToArray();
        var next = Sprites.IndexOf(selected[0]);
        using (var transaction = UndoRedoService.CreateTransaction())
        {
            foreach (var sprite in selected.OrderByDescending(s => Asset.Asset.Sprites.IndexOf(s.Sprite)))
                spritesNode.Remove(sprite.Sprite, new NodeIndex(Asset.Asset.Sprites.IndexOf(sprite.Sprite)));
            UndoRedoService.SetName(transaction, "Delete sprites");
        }
        Select(Sprites.Count > 0 ? Sprites[Math.Min(next, Sprites.Count - 1)] : null);
    }

    public void Move(int offset)
    {
        if (!Asset.IsEditable || offset is not (-1 or 1)) return;
        var selected = SelectedSprites.Cast<SpriteInfoViewModel>().Select(s => s.Sprite).ToArray();
        using (var transaction = UndoRedoService.CreateTransaction())
        {
            foreach (var sprite in selected.OrderBy(s => -offset * Asset.Asset.Sprites.IndexOf(s)))
            {
                var index = Asset.Asset.Sprites.IndexOf(sprite);
                var target = index + offset;
                if (target < 0 || target >= Sprites.Count || selected.Contains(Asset.Asset.Sprites[target])) continue;
                spritesNode.Remove(sprite, new NodeIndex(index));
                spritesNode.Add(sprite, new NodeIndex(target));
            }
            UndoRedoService.SetName(transaction, "Reorder sprites");
        }
        SelectedSprites.Clear();
        foreach (var sprite in selected) SelectedSprites.Add(Sprites.First(s => ReferenceEquals(s.Sprite, sprite)));
    }

    public void Reorder(IReadOnlyList<SpriteInfoViewModel> selection, int insertionIndex)
    {
        if (!Asset.IsEditable || selection.Count == 0 || selection.Any(s => !Sprites.Contains(s))) return;
        var images = selection.OrderBy(Sprites.IndexOf).Select(s => s.Sprite).ToArray();
        var target = Math.Clamp(insertionIndex, 0, Sprites.Count);
        target -= images.Count(s => Asset.Asset.Sprites.IndexOf(s) < target);
        using (var transaction = UndoRedoService.CreateTransaction())
        {
            foreach (var image in images.Reverse())
                spritesNode.Remove(image, new NodeIndex(Asset.Asset.Sprites.IndexOf(image)));
            foreach (var image in images) spritesNode.Add(image, new NodeIndex(target++));
            UndoRedoService.SetName(transaction, "Reorder sprites");
        }
        SelectedSprites.Clear();
        foreach (var image in images) SelectedSprites.Add(Sprites.First(s => ReferenceEquals(s.Sprite, image)));
    }

    public void SetRegion(SpriteInfoViewModel sprite, Rectangle region) => sprite.Region = region;

    public static Vector4 ClampBorders(Vector4 borders, Rectangle region)
    {
        borders.X = Math.Clamp(borders.X, 0, Math.Max(0, region.Width));
        borders.Y = Math.Clamp(borders.Y, 0, Math.Max(0, region.Height));
        borders.Z = Math.Clamp(borders.Z, 0, Math.Max(0, region.Width - borders.X));
        borders.W = Math.Clamp(borders.W, 0, Math.Max(0, region.Height - borders.Y));
        return borders;
    }

    public async Task CopyAsync()
    {
        if (SelectedSprites.Count == 0 || ServiceProvider.TryGet<ICopyPasteService>() is not { } copyPaste || ServiceProvider.TryGet<IClipboardService>() is not { } clipboard) return;
        var images = SelectedSprites.Cast<SpriteInfoViewModel>().Select(s => s.Sprite).ToList();
        await clipboard.SetTextAsync(copyPaste.CopyFromAsset(Asset.PropertyGraph, Asset.Id, images, false));
    }

    public async Task PasteAsync()
    {
        if (!Asset.IsEditable || ServiceProvider.TryGet<ICopyPasteService>() is not { } copyPaste || ServiceProvider.TryGet<IClipboardService>() is not { } clipboard) return;
        var text = await clipboard.GetTextAsync();
        if (IsDestroyed || string.IsNullOrEmpty(text)) return;
        var processor = new AssetPropertyPasteProcessor();
        copyPaste.RegisterProcessor(processor);
        try
        {
            if (copyPaste.CanPaste(text, typeof(SpriteSheetAsset), typeof(List<SpriteInfo>), typeof(List<SpriteInfo>)) &&
                copyPaste.DeserializeCopiedData(text, Asset.Asset, typeof(List<SpriteInfo>)).Items.FirstOrDefault()?.Data is List<SpriteInfo> images)
                AddSprites(images, "Paste sprites");
        }
        finally { copyPaste.UnregisterProcessor(processor); }
    }

    private void SpritesChanged(object? sender, ItemChangeEventArgs e)
    {
        var index = e.Index.Int;
        if (e.ChangeType is ContentChangeType.CollectionRemove or ContentChangeType.CollectionUpdate)
        {
            var old = Sprites[index];
            SelectedSprites.Remove(old);
            Sprites.RemoveAt(index);
            old.Destroy();
        }
        if (e.ChangeType is ContentChangeType.CollectionAdd or ContentChangeType.CollectionUpdate)
            Sprites.Insert(index, new(this, (SpriteInfo)e.NewValue));
    }

    private void SelectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        OnPropertyChanged(nameof(CurrentSprite));
        Dispatcher.InvokeAsync(async () =>
        {
            if (IsDestroyed) return;
            var selection = SelectedSprites.Cast<SpriteInfoViewModel>().ToArray();
            EditorProperties.UpdateTypeAndName(selection, _ => "Sprite", s => s.Name, "sprites");
            await EditorProperties.GenerateSelectionPropertiesAsync(selection.Cast<IPropertyProviderViewModel>());
        });
    }

    public override void Destroy()
    {
        ServiceProvider.Get<SelectionService>().UnregisterSelectionScope(SelectedSprites);
        spritesNode.ItemChanged -= SpritesChanged;
        SelectedSprites.CollectionChanged -= SelectionChanged;
        if (Session.ActiveProperties.Selection?.OfType<SpriteInfoViewModel>().Any(Sprites.Contains) == true)
            _ = EditorProperties.GenerateSelectionPropertiesAsync(Array.Empty<IPropertyProviderViewModel>());
        foreach (var sprite in Sprites) sprite.Destroy();
        colorKey.Dispose();
        colorKeyEnabled.Dispose();
        base.Destroy();
    }
}
