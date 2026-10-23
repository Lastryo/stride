// Copyright (c) .NET Foundation and Contributors (https://dotnetfoundation.org/ & https://stride3d.net)
// Distributed under the MIT license. See the LICENSE.md file in the project root for more information.

using System.Runtime.InteropServices;
using Avalonia;
using Point = Avalonia.Point;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Stride.Assets.Editor.Avalonia.SpriteEditor;
using Stride.Assets.Editor.Avalonia.Views;
using Stride.Assets.Editor.ViewModels;
using Stride.Assets.Presentation.ViewModels;
using Stride.Assets.Sprite;
using Stride.Core.Assets;
using Stride.Core.Assets.Serializers;
using Stride.Core.Assets.Editor.ViewModels;
using Stride.Core.IO;
using Stride.Core.Mathematics;
using SRectangle = Stride.Core.Mathematics.Rectangle;

internal static class EditorSpriteSheetSmoke
{
    public static async Task Check(SessionViewModel session, Window window)
    {
        var directory = Path.Combine(Path.GetTempPath(), "stride-sprite-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, "sprite.png");
        try
        {
            using (var image = new WriteableBitmap(new PixelSize(16, 16), new Avalonia.Vector(96, 96), PixelFormat.Rgba8888, AlphaFormat.Unpremul))
            {
                using (var buffer = image.Lock())
                {
                    var bytes = new byte[buffer.RowBytes * 16];
                    for (var y = 3; y < 9; y++) for (var x = 4; x < 9; x++)
                    {
                        var i = y * buffer.RowBytes + x * 4;
                        bytes[i] = 255; bytes[i + 3] = 255;
                    }
                    Marshal.Copy(bytes, 0, buffer.Address, bytes.Length);
                }
                image.Save(path);
            }
            using (var cache = new SpriteEditorImageCache())
            {
                Require(cache.GetPixelSize(path) == new Size2(16, 16), "Image size");
                Require(cache.PickPixelColor(path, new(5, 4)) == new Stride.Core.Mathematics.Color(255, 0, 0, 255), "Pixel color");
                Require(cache.PickPixelColor(path, new(-1, 0)) == null, "Pixel bounds");
                Require(cache.FindSpriteRegion(path, new(5, 4), true, default) == new SRectangle(4, 3, 5, 6), "Alpha magic wand");
                Require(cache.FindSpriteRegion(path, new(5, 4), false, new Stride.Core.Mathematics.Color(0, 0, 0, 0)) == new SRectangle(4, 3, 5, 6), "Color-key magic wand");
            }
            var project = session.CurrentProject!;
            var history = session.ActionService!;
            SpriteSheetViewModel asset;
            var sheet = new SpriteSheetAsset();
            sheet.Sprites.Add(new SpriteInfo { Source = path, Name = "First", TextureRegion = new(0, 0, 16, 16) });
            using (history.CreateTransaction()) asset = (SpriteSheetViewModel)project.AddAsset(new AssetItem("SpriteSmoke_" + Guid.NewGuid().ToString("N"), sheet), project.AssetMountPoint);
            var initialTransactions = history.RetrieveAllTransactions().Count();
            session.EditorCollection.OpenAssetEditor(asset);
            var editor = (SpriteSheetEditorViewModel)session.EditorCollection.ActiveEditor!;
            await Idle();
            SpriteSheetEditorView? view = null;
            for (var attempt = 0; attempt < 100 && view == null; attempt++)
            {
                view = window.GetVisualDescendants().OfType<SpriteSheetEditorView>().FirstOrDefault();
                if (view == null) await Task.Delay(100);
            }
            if (view == null) throw new Exception("Sprite Sheet view was not attached by the docking layout.");
            var list = view.FindControl<ListBox>("SpriteList")!;
            Require(ReferenceEquals(list.SelectedItems, editor.SelectedSprites), "Shared list selection");
            list.SelectedItem = editor.Sprites[0];
            await Idle();
            Require(editor.CurrentSprite?.Name == "First", "Selection");
            Require(history.RetrieveAllTransactions().Count() == initialTransactions, "Opening or selection mutated the sheet");
            Require(session.ActiveProperties.Selection.Contains(editor.CurrentSprite!), "Sprite inspector selection");
            editor.CurrentSprite!.Name = "Renamed";
            history.Undo();
            Require(editor.CurrentSprite.Name == "First", "Rename undo");
            editor.AddCommand.Execute(null);
            Require(sheet.Sprites.Count == 2 && editor.Sprites.Count == 2, "Add sprite");
            history.Undo();
            history.Redo();
            Require(editor.Sprites.Count == 2, "Add redo");
            history.Undo();
            editor.Select(editor.Sprites[0]);
            editor.DuplicateCommand.Execute(null);
            Require(sheet.Sprites.Count == 2 && !ReferenceEquals(sheet.Sprites[0], sheet.Sprites[1]), "Duplicate clone");
            editor.Reorder([editor.Sprites[1]], 0);
            Require(sheet.Sprites[0].Name != "First", "Drag/drop insertion reorder");
            history.Undo();
            editor.Select(editor.Sprites[1]);
            editor.Move(-1);
            Require(sheet.Sprites[0].Name != "First", "Reorder");
            history.Undo();
            editor.Select(editor.Sprites[1]);
            editor.Delete();
            Require(sheet.Sprites.Count == 1, "Delete");
            history.Undo();
            history.Undo(); // Duplicate.
            editor.Select(editor.Sprites[0]);
            await editor.CopyAsync();
            await editor.PasteAsync();
            Require(sheet.Sprites.Count == 2 && !ReferenceEquals(sheet.Sprites[0], sheet.Sprites[1]), "Clipboard paste");
            history.Undo();
            editor.Select(editor.Sprites[0]);
            await Idle();
            var canvas = view.FindControl<SpriteSheetCanvas>("SpriteCanvas")!;
            canvas.RealSize();
            canvas.Fit();
            // Frame the 16x16 region, then use its known center to exercise real Avalonia input.
            canvas.Frame();
            editor.ToolMode = SpriteSheetEditorToolMode.MagicWand;
            var pointer = new Avalonia.Input.Pointer(707, PointerType.Mouse, true);
            var center = new Point(canvas.Bounds.Width / 2, canvas.Bounds.Height / 2);
            Press(center);
            Release(center);
            Require(sheet.Sprites[0].TextureRegion == new SRectangle(4, 3, 5, 6), "Routed magic-wand gesture");
            editor.ToolMode = SpriteSheetEditorToolMode.Region;
            canvas.Frame();
            var step = Math.Clamp(Math.Min((canvas.Bounds.Width - 40) / 5, (canvas.Bounds.Height - 40) / 6), .01, 64);
            Press(center);
            var end = center + new Avalonia.Vector(step, step);
            canvas.RaiseEvent(new PointerEventArgs(PointerMovedEvent(), canvas, pointer, canvas, end, 0,
                new PointerPointProperties(RawInputModifiers.LeftMouseButton, PointerUpdateKind.Other), KeyModifiers.None));
            Require(sheet.Sprites[0].TextureRegion == new SRectangle(4, 3, 5, 6), "Gesture preview must not mutate the asset");
            Release(end);
            Require(sheet.Sprites[0].TextureRegion == new SRectangle(5, 4, 5, 6), "Routed region move: " + sheet.Sprites[0].TextureRegion);
            history.Undo();
            Require(sheet.Sprites[0].TextureRegion == new SRectangle(4, 3, 5, 6), "Gesture is one undo transaction");
            history.Undo();
            Require(sheet.Sprites[0].TextureRegion == new SRectangle(0, 0, 16, 16), "Magic-wand undo");
            Require(SpriteSheetCanvas.ResizeRegion(new(4, 3, 5, 6), new(4, 3), new(-10, -10), 0, 16, 16) == new SRectangle(0, 0, 9, 9), "Resize clamp");
            Require(SpriteSheetCanvas.ResizeRegion(new(4, 3, 5, 6), new(5, 4), new(40, 40), 8, 16, 16) == new SRectangle(11, 10, 5, 6), "Move clamp");
            Require(SpriteSheetEditorViewModel.ClampBorders(new(20, 20, 20, 20), new(0, 0, 5, 6)) == new Vector4(5, 6, 0, 0), "Non-overlapping borders");
            editor.CurrentSprite!.Center = new(2, -3);
            history.Undo();
            Require(sheet.Sprites[0].Center == Vector2.Zero, "Pivot undo");
            editor.CurrentSprite.Borders = new(1, 2, 3, 4);
            history.Undo();
            Require(sheet.Sprites[0].Borders == Vector4.Zero, "Borders undo");
            editor.LockBorders = true;
            editor.CurrentSprite.Borders = new(2, 2, 2, 2);
            editor.SetRegion(editor.CurrentSprite, new(1, 1, 14, 14));
            Require(sheet.Sprites[0].Borders == new Vector4(1, 1, 1, 1), "Locked borders retain image positions");
            history.Undo();
            Require(sheet.Sprites[0].TextureRegion == new SRectangle(0, 0, 16, 16) && sheet.Sprites[0].Borders == new Vector4(2, 2, 2, 2), "Locked border resize undo");
            history.Undo();
            editor.LockBorders = false;
            editor.AddCommand.Execute(null);
            await Idle();
            editor.CurrentSprite!.Source = path;
            await Idle();
            Require(editor.CurrentSprite.Region == new SRectangle(0, 0, 16, 16), "Assigning the first source initializes the whole image");
            history.Undo();
            Require(editor.CurrentSprite.Source == null && editor.CurrentSprite.Region == new SRectangle(), "Source and automatic region undo together");
            history.Undo();
            editor.Select(editor.Sprites[0]);
            editor.ImportFiles([new UFile(path)], p => new Size2(16, 16));
            Require(sheet.Sprites.Count == 2 && sheet.Sprites[1].TextureRegion == new SRectangle(0, 0, 16, 16), "Import whole image");
            history.Undo();
            using (var stream = new MemoryStream())
            {
                AssetFileSerializer.Save(stream, sheet, null);
                stream.Position = 0;
                var loaded = AssetFileSerializer.Load<SpriteSheetAsset>(stream, "SpriteSmoke.sdsheet").Asset;
                Require(loaded.Sprites.Count == 1 && loaded.Sprites[0].TextureRegion == sheet.Sprites[0].TextureRegion, "Serialized sheet round trip");
            }
            editor.Select(editor.Sprites[0]);
            await Idle();
            using (var screenshot = new RenderTargetBitmap(PixelSize.FromSize(view.Bounds.Size, 1)))
            {
                screenshot.Render(view);
                screenshot.Save(Path.Combine(Path.GetTempPath(), "stride-sprite-sheet.png"));
            }
            await session.EditorCollection.CloseEditorAsync(editor);
            session.EditorCollection.OpenAssetEditor(asset);
            var reopened = (SpriteSheetEditorViewModel)session.EditorCollection.ActiveEditor!;
            await Idle();
            Require(reopened.Sprites.Count == 1, "Reopen lifecycle");
            await session.EditorCollection.CloseEditorAsync(reopened);
            history.Undo(); // Remove the temporary fixture asset; the project is never saved.
            Console.WriteLine("PASS: Sprite Sheet image decoding, alpha/color-key wand, list/inspector selection, add/delete/duplicate/reorder, clipboard, region bounds, pivot/borders Undo/Redo, import and reopen.");

            void Press(Point p) => canvas.RaiseEvent(new PointerPressedEventArgs(canvas, pointer, canvas, p, 0,
                new PointerPointProperties(RawInputModifiers.LeftMouseButton, PointerUpdateKind.LeftButtonPressed), KeyModifiers.None, 1));
            void Release(Point p) => canvas.RaiseEvent(new PointerReleasedEventArgs(canvas, pointer, canvas, p, 0,
                new PointerPointProperties(RawInputModifiers.None, PointerUpdateKind.LeftButtonReleased), KeyModifiers.None, MouseButton.Left));
        }
        finally { Directory.Delete(directory, true); }
    }
    private static Avalonia.Interactivity.RoutedEvent<PointerEventArgs> PointerMovedEvent() => InputElement.PointerMovedEvent;
    private static async Task Idle() { await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Background); await Task.Delay(150); }
    private static void Require(bool condition, string message) { if (!condition) throw new Exception("Sprite Sheet: " + message); }
}
