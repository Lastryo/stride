// Copyright (c) .NET Foundation and Contributors (https://dotnetfoundation.org/ & https://stride3d.net)
// Distributed under the MIT license. See the LICENSE.md file in the project root for more information.
using System.Text;
using System.Diagnostics;
using Avalonia.Threading;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.VisualTree;
using Avalonia.Input;
using Avalonia;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Stride.Assets.Editor.Avalonia.ScriptEditor;
using Microsoft.CodeAnalysis.Tags;
using Avalonia.Interactivity;
using Microsoft.CodeAnalysis;
using Stride.Assets.Editor.Avalonia.Views;
using Stride.Assets.Editor.ViewModels;
using Stride.Assets.Presentation.ViewModels;
using Stride.Core.Assets.Editor.ViewModels;
using Stride.Core.Diagnostics;
using Stride.Core.Presentation.Dirtiables;

internal static class EditorScriptSmoke
{
    public static async Task Check(SessionViewModel session, Window window)
    {
        var asset = session.LocalPackages.SelectMany(p => p.Assets).OfType<ScriptSourceFileViewModel>().First(a => a.AssetItem.FullPath.ToString().Contains(".Game/"));
        var path = Path.GetFullPath(asset.AssetItem.FullPath.ToString());
        Require(path.StartsWith(Path.GetTempPath(), StringComparison.Ordinal) || File.Exists(Path.Combine(Path.GetDirectoryName(path)!, ".stride-disposable-smoke-project")), "Use a disposable project copy.");
        var original = File.ReadAllBytes(path);
        var otherAsset = session.LocalPackages.SelectMany(p => p.Assets).OfType<ScriptSourceFileViewModel>().First(a => a != asset && a.AssetItem.FullPath.ToString().Contains(".Game/"));
        var otherPath = Path.GetFullPath(otherAsset.AssetItem.FullPath.ToString());
        var otherOriginal = File.ReadAllBytes(otherPath);
        var otherCode = "namespace CodeEditorSmoke; public class OtherFixture { public int Value => new Fixture().Member; }";
        var reload = session.HotReload!; reload.AutoReload = false;
        ScriptSourceFileEditorViewModel? model = null;
        var code = "using Stride.Engine;\r\nnamespace CodeEditorSmoke;\r\npublic class Fixture : SyncScript\r\n{\r\n    public int Member = 7;\r\n    public override void Update() { Member++; }\r\n}\r\n// кириллица\r\n";
        var logger = new LoggerResult();
        try
        {
            File.WriteAllText(otherPath, otherCode, new UTF8Encoding(false)); otherAsset.Asset.Text = otherCode; ((IDirtiable)otherAsset).UpdateDirtiness(false); otherAsset.AssetItem.IsDirty = false;
            File.WriteAllText(path, code, new UTF8Encoding(true)); asset.Asset.Text = code; ((IDirtiable)asset).UpdateDirtiness(false); asset.AssetItem.IsDirty = false;
            session.EditorCollection.OpenAssetEditor(asset);
            model = (ScriptSourceFileEditorViewModel)session.EditorCollection.ActiveEditor!;
            ScriptSourceFileEditorView? view = null;
            await Wait(() =>
            {
                view = ((IClassicDesktopStyleApplicationLifetime)global::Avalonia.Application.Current!.ApplicationLifetime!).Windows
                    .SelectMany(w => w.GetVisualDescendants().OfType<ScriptSourceFileEditorView>()).FirstOrDefault(v => ReferenceEquals(v.DataContext, model));
                return view != null;
            });
            Require(view!.Editor.Text == code && view.Editor.ShowLineNumbers && view.Editor.SyntaxHighlighting != null, "Text editor/highlighting initialization");
            foreach (var style in new[] { FontStyle.Normal, FontStyle.Italic })
            foreach (var weight in new[] { FontWeight.Normal, FontWeight.Bold })
            {
                var face = new Typeface(view.Editor.FontFamily, style, weight).GlyphTypeface;
                Require(face.FamilyName == "JetBrains Mono" && face.Style == style && face.Weight == weight, "Bundled JetBrains Mono face did not load: " + style + " " + weight);
            }
            foreach (var font in new[] { "Regular", "Italic", "Bold", "BoldItalic" })
                Require(global::Avalonia.Platform.AssetLoader.Exists(new Uri("avares://Stride.Assets.Editor.Avalonia/Resources/Fonts/JetBrainsMono/JetBrainsMono-" + font + ".ttf")), "Font was not embedded: " + font);
            var fontLicense = Path.Combine(AppContext.BaseDirectory, "Resources", "Fonts", "JetBrainsMono", "OFL.txt");
            Require(File.Exists(fontLicense) && File.ReadAllText(fontLicense).Contains("Copyright 2020 The JetBrains Mono Project Authors"), "Font license was not distributed with the editor");
            view.Editor.Document.Insert(0, "// edit\r\n");
            Require(model.IsModified && asset.IsDirty && model.Text == view.Editor.Text, "Unsaved text/dirty state");
            view.Editor.Undo(); Require(model.Text == code && !model.IsModified, "Native text Undo");
            view.Editor.Redo(); Require(model.IsModified, "Native text Redo"); view.Editor.Undo();
            await model.AnalyzeAsync().WaitAsync(TimeSpan.FromMinutes(2));
            Require(!model.Status.StartsWith("C# project unavailable"), model.Status);
            Require(!model.Diagnostics.Any(d => d.Severity == DiagnosticSeverity.Error), string.Join("\n", model.Diagnostics));
            await CheckTyping(model, view, code);
            var prefix = "using Stride.Engine; namespace CodeEditorSmoke; public class Fixture : SyncScript { public override void Update() { Entity.Transform.";
            model.Text = prefix + " } }";
            model.Text = prefix[..^1] + " } }";
            view.Editor.CaretOffset = prefix.Length - 1; view.Editor.Focus();
            view.Editor.TextArea.RaiseEvent(new TextInputEventArgs { RoutedEvent = InputElement.TextInputEvent, Text = "." });
            await Wait(() => view.CompletionPopup?.IsOpen == true);
            var popup = view.CompletionPopup!;
            await Wait(() => popup.CompletionList.GetVisualDescendants().OfType<ListBox>().Any(list => list.Bounds.Width > 0 && list.Bounds.Height > 0));
            var visibleOrder = popup.CompletionList.CurrentList.Select(i => i.Text).ToArray();
            var expectedOrder = await model.CompleteAsync(prefix.Length);
            Require(visibleOrder.SequenceEqual(expectedOrder.Select(i => i.Text)), "Completion UI changed the grouped order");
            Require(expectedOrder.Any(i => i.KindOrder == 0) && expectedOrder.Any(i => i.KindOrder == 1) && expectedOrder.Any(i => i.KindOrder == 3), "Completion grouping fixture requires properties, fields and methods");
            for (var index = 1; index < expectedOrder.Count; index++)
            {
                var previous = expectedOrder[index - 1]; var current = expectedOrder[index];
                Require(previous.KindOrder <= current.KindOrder, "Completion symbol groups are interleaved");
                if (previous.KindOrder == current.KindOrder) Require(StringComparer.OrdinalIgnoreCase.Compare(previous.Text, current.Text) <= 0, "Completion group is not alphabetical");
            }
            popup.CompletionList.SelectItem("P");
            Require(popup.CompletionList.CurrentList.Select(i => i.Text).SequenceEqual(expectedOrder.Where(i => popup.CompletionList.CurrentList.Any(filtered => filtered.Text == i.Text)).Select(i => i.Text)), "Filtering changed the grouped order");
            popup.CompletionList.SelectItem("Position");
            await Wait(() => popup.CompletionList.GetVisualDescendants().OfType<TextBlock>().Any(t => t.Text == "Position" && t.Bounds.Width > 0));
            Require(popup.CompletionList.SelectedItem?.Text == "Position", "Completion popup selection");
            Require(popup.CompletionList.GetVisualDescendants().OfType<Image>().Any(image => image.Source != null && image.Bounds.Width > 0), "Completion popup icons");
            using (var popupScreenshot = new RenderTargetBitmap(new PixelSize((int)Math.Ceiling(popup.CompletionList.Bounds.Width), (int)Math.Ceiling(popup.CompletionList.Bounds.Height)), new Vector(96, 96)))
            {
                popupScreenshot.Render(popup.CompletionList); popupScreenshot.Save(System.IO.Path.Combine(System.IO.Path.GetTempPath(), "stride-script-completion.png"));
            }
            view.Editor.TextArea.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Enter });
            await Wait(() => model.Text.Contains("Entity.Transform.Position"));
            Require(view.CompletionPopup == null || !view.CompletionPopup.IsOpen, "Completion popup did not close after acceptance");
            view.Editor.Undo(); Require(model.Text == prefix + " } }", "Completion insertion Undo");
            var completions = await model.CompleteAsync(prefix.Length).WaitAsync(TimeSpan.FromSeconds(45));
            var position = completions.FirstOrDefault(c => c.Text == "Position");
            Require(position != null, "Project-aware completion did not offer Transform.Position");
            var propertyIcon = ScriptEditorIcons.Completion(position!.Item.Tags);
            Require(propertyIcon.Size.Width == 16 && propertyIcon.Size.Height == 16, "Completion icon dimensions");
            Require(!ReferenceEquals(ScriptEditorIcons.Completion([WellKnownTags.Class]), ScriptEditorIcons.Completion([WellKnownTags.Method])), "Completion symbol glyph distinctions");
            Require(!ReferenceEquals(ScriptEditorIcons.Completion([WellKnownTags.Class, WellKnownTags.Private]), ScriptEditorIcons.Completion([WellKnownTags.Class])), "Completion access badge");
            Require(!ReferenceEquals(ScriptEditorIcons.Diagnostic(DiagnosticSeverity.Error), ScriptEditorIcons.Diagnostic(DiagnosticSeverity.Warning)), "Diagnostic severity glyphs");
            Require(view.ActionsButton.Content is Image { Source: not null, Width: 16, Height: 16 }, "WPF-style inline bulb resource");
            Require(view.Editor.FontSize == 12 && view.Editor.Bounds.Top == 0 && Math.Abs(view.Editor.Bounds.Height - view.Bounds.Height) < 1, "Editor-only WPF layout/font size");
            Require(view.Editor.ContextMenu!.Items.OfType<MenuItem>().Count(i => i.Icon is Image { Source: not null }) >= 10, "Command icons in context menu");
            model.Text = code;
            view.Search.Open(); view.Search.IsReplaceMode = true; view.Search.SearchPattern = "Member"; view.Search.ReplacePattern = "ReplacedMember";
            await Wait(() => view.GetVisualDescendants().OfType<TextBox>().Any(t => t.Name == "PART_searchTextBox"));
            var searchBox = view.GetVisualDescendants().OfType<TextBox>().First(t => t.Name == "PART_searchTextBox");
            Require(searchBox.Width == 150 && searchBox.Height == 24, "WPF search field dimensions");
            var searchButtons = view.GetVisualDescendants().OfType<Button>().Where(b => b.Name is "FindPreviousButton" or "FindNextButton").ToArray();
            Require(searchButtons.Length == 2 && searchButtons.All(b => b.Width == 24 && b.Height == 24 && b.Content is Image { Width: 16, Height: 16, Source: not null }), "WPF search icon buttons");
            await Task.Delay(100);
            using (var searchScreenshot = new RenderTargetBitmap(new PixelSize((int)view.Bounds.Width, (int)view.Bounds.Height), new Vector(96, 96)))
            {
                searchScreenshot.Render(view); searchScreenshot.Save(System.IO.Path.Combine(System.IO.Path.GetTempPath(), "stride-script-editor-search.png"));
            }
            view.Search.ReplaceAll(); Require(model.Text.Contains("ReplacedMember"), "Search/replace edits source");
            view.Editor.Undo(); Require(model.Text == code, "Search/replace native Undo");
            view.Search.Close(); model.Text = prefix + " } }";
            RenderIcons();
            var originalDocument = position!.Document;
            var repeated = await model.CompleteAsync(prefix.Length);
            Require(ReferenceEquals(originalDocument.Project.Solution, repeated.First(c => c.Text == "Position").Document.Project.Solution), "Unchanged requests invalidated the Roslyn solution cache");
            var originalText = await originalDocument.GetTextAsync();
            model.Text = prefix + "  } }";
            var incremental = await model.CompleteAsync(prefix.Length);
            var changedText = await incremental.First(c => c.Text == "Position").Document.GetTextAsync();
            var ranges = changedText.GetChangeRanges(originalText);
            Require(ranges.Count == 1 && ranges[0].Span.Length == 0 && ranges[0].NewLength == 1, "Single-character edit lost incremental Roslyn change ranges");
            model.Text = prefix + " } }";
            var change = await model.CompletionChangeAsync(position!);
            Require(change.Text.Contains("Position"), "Roslyn completion insertion");
            Require((await model.DescribeAsync(position!)).Contains("Position"), "Completion description");
            var signatureCode = code.Replace("Member++;", "Member++; Call(1, \"text\");").Replace("public int Member = 7;", "public int Member = 7; public void Call(int first, string second) { }");
            model.Text = signatureCode;
            var help = await model.SignatureHelpAsync(signatureCode.IndexOf("\"text\"", StringComparison.Ordinal));
            Require(help?.ArgumentIndex == 1 && help.Signatures.Any(s => s.Contains("[string second]")), "Signature help active argument");
            var incomplete = signatureCode[..signatureCode.IndexOf("\"text\"", StringComparison.Ordinal)];
            model.Text = incomplete;
            help = await model.SignatureHelpAsync(incomplete.Length);
            Require(help?.Signatures.Any(s => s.Contains("[string second]")) == true, "Incomplete call signature help while typing");
            var constructorCode = code.Replace("Member++;", "Member++; var list = new System.Collections.Generic.List<int>(5);");
            model.Text = constructorCode;
            help = await model.SignatureHelpAsync(constructorCode.IndexOf("(5)", StringComparison.Ordinal) + 1);
            Require(help?.Signatures.Any(s => s.Contains("[int capacity]")) == true, "Constructor signature help");
            var indexerCode = code.Replace("Member++;", "Member++; var list = new System.Collections.Generic.List<int>(); var item = list[0];");
            model.Text = indexerCode;
            help = await model.SignatureHelpAsync(indexerCode.IndexOf("[0]", StringComparison.Ordinal) + 1);
            Require(help?.Signatures.Any(s => s.Contains("[int index]")) == true, "Indexer signature help");
            var namedCode = signatureCode.Replace("Call(1, \"text\")", "Call(second: \"text\", first: 1)");
            model.Text = namedCode;
            help = await model.SignatureHelpAsync(namedCode.IndexOf("\"text\"", StringComparison.Ordinal));
            Require(help?.Signatures.Any(s => s.Contains("[string second]")) == true, "Named argument signature help");
            view.Editor.CaretOffset = namedCode.IndexOf("\"text\"", StringComparison.Ordinal);
            view.Editor.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Space, KeyModifiers = KeyModifiers.Control | KeyModifiers.Shift });
            await Wait(() => view.IsParameterHelpOpen);
            Require(view.ParameterHelp.SelectedItem?.ToString()?.Contains("[string second]") == true, "Signature help UI");
            model.Text = code;
            var rename = await model.PreviewRenameAsync(code.IndexOf("Member++", StringComparison.Ordinal), "Renamed");
            Require(rename.Documents.Count == 2 && model.Text == code && otherAsset.Asset.Text == otherCode, "Rename preview changed buffers or missed another file");
            otherAsset.Asset.Text += " // stale";
            var refused = false;
            try { model.ApplyPreview(rename); } catch (InvalidOperationException) { refused = true; }
            Require(refused && model.Text == code, "Stale multi-file preview was applied partially");
            otherAsset.Asset.Text = otherCode;
            File.WriteAllText(otherPath, otherCode + " // external", new UTF8Encoding(false));
            refused = false;
            try { model.ApplyPreview(rename); } catch (InvalidOperationException) { refused = true; }
            Require(refused && model.Text == code, "Externally changed multi-file preview was applied partially");
            File.WriteAllText(otherPath, otherCode, new UTF8Encoding(false));
            var conflictRefused = false;
            try { await model.PreviewRenameAsync(code.IndexOf("Member++", StringComparison.Ordinal), "Update"); } catch (InvalidOperationException) { conflictRefused = true; }
            Require(conflictRefused && model.Text == code, "Conflicting rename was not rejected");
            model.ApplyPreview(rename);
            Require(model.Text.Contains("Renamed++") && otherAsset.Asset.Text.Contains(".Renamed") && otherAsset.IsDirty && File.ReadAllText(otherPath) == otherCode, "Multi-file rename must edit buffers only");
            session.EditorCollection.OpenAssetEditor(otherAsset);
            var otherModel = (ScriptSourceFileEditorViewModel)session.EditorCollection.ActiveEditor!;
            ScriptSourceFileEditorView? otherView = null;
            await Wait(() => (otherView = Windows().SelectMany(w => w.GetVisualDescendants().OfType<ScriptSourceFileEditorView>()).FirstOrDefault(v => ReferenceEquals(v.DataContext, otherModel))) != null);
            otherView!.Editor.Undo(); Require(otherModel.Text == otherCode, "Rename Undo in previously closed source");
            otherView.Editor.Redo(); Require(otherModel.Text.Contains(".Renamed"), "Rename Redo in previously closed source");
            await session.EditorCollection.CloseEditorAsync(otherModel);
            session.EditorCollection.OpenAssetEditor(asset);
            await Wait(() => (view = Windows().SelectMany(w => w.GetVisualDescendants().OfType<ScriptSourceFileEditorView>()).FirstOrDefault(v => ReferenceEquals(v.DataContext, model))) != null);
            view.Editor.Undo(); Require(model.Text == code, "Rename native Undo"); view.Editor.Redo(); Require(model.Text.Contains("Renamed++"), "Rename native Redo");
            model.Text = code; otherAsset.Asset.Text = otherCode; ((IDirtiable)otherAsset).UpdateDirtiness(false); otherAsset.AssetItem.IsDirty = false;
            var fixCode = code.Replace("Member++;", "Member++; var builder = new StringBuilder();");
            model.Text = fixCode;
            var actions = await model.ContextActionsAsync(fixCode.IndexOf("StringBuilder", StringComparison.Ordinal), 0).WaitAsync(TimeSpan.FromSeconds(60));
            var addUsing = actions.FirstOrDefault(a => a.Title.Contains("using System.Text", StringComparison.Ordinal));
            Require(addUsing?.IsCodeFix == true, "Roslyn add-using code fix was not offered: " + string.Join("; ", actions.Select(a => a.Title)));
            view.Editor.Select(fixCode.IndexOf("StringBuilder", StringComparison.Ordinal), "StringBuilder".Length);
            await Wait(() => view.ActionsButton.IsVisible);
            view.ActionsButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await Wait(() => view.ActionMenu?.IsOpen == true);
            var actionItems = view.ActionMenu!.Items.Cast<MenuItem>().ToArray();
            Require(actionItems.All(i => i.Icon is Image { Source: not null, Width: 16, Height: 16 }), "Inline context action glyphs");
            var chosen = actionItems.First(i => i.Tag is ScriptContextAction action && action.Title.Contains("using System.Text", StringComparison.Ordinal));
            view.ActionMenu.Close(); chosen.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
            Window? previewWindow = null;
            await Wait(() => (previewWindow = Windows().FirstOrDefault(w => w.Title == addUsing!.Title)) != null);
            Require(model.Text == fixCode, "Action preview modified the source buffer");
            previewWindow!.GetVisualDescendants().OfType<Button>().Single(b => Equals(b.Content, "Apply to buffers")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await Wait(() => model.Text.Contains("using System.Text;"));
            Require(model.Text.Contains("using System.Text;"), "Add-using action application through UI");
            await model.AnalyzeAsync(); Require(!model.Diagnostics.Any(d => d.Id == "CS0246"), "Applied code fix did not resolve missing type");
            model.Text = code.Replace("Member++;", "Member++; var number = 1;");
            actions = await model.ContextActionsAsync(model.Text.IndexOf("var number", StringComparison.Ordinal), 0).WaitAsync(TimeSpan.FromSeconds(60));
            var convertNumber = actions.FirstOrDefault(a => a.Title.Contains("Convert to hex", StringComparison.OrdinalIgnoreCase));
            Require(convertNumber is { IsCodeFix: false }, "Roslyn number refactoring was not offered: " + string.Join("; ", actions.Select(a => a.Title)));
            model.ApplyPreview(await model.PreviewActionAsync(convertNumber!)); Require(model.Text.Contains("0x1"), "Refactoring application");
            model.Text = code;
            var metadataCode = code.Replace("Member++;", "Member++; System.Console.WriteLine(Member);");
            model.Text = metadataCode;
            await model.GoToDefinitionAsync(metadataCode.IndexOf("WriteLine", StringComparison.Ordinal));
            Require(model.MetadataText?.Contains("WriteLine") == true && model.MetadataText.Contains("Assembly:"), "Metadata declarations navigation");
            var metadataWindow = ((IClassicDesktopStyleApplicationLifetime)global::Avalonia.Application.Current!.ApplicationLifetime!).Windows.FirstOrDefault(w => w != window && w.Content is AvaloniaEdit.TextEditor);
            Require(metadataWindow?.Content is AvaloniaEdit.TextEditor { IsReadOnly: true }, "Read-only metadata window");
            Require(((AvaloniaEdit.TextEditor)metadataWindow!.Content!).FontFamily.Equals(view.Editor.FontFamily), "Metadata viewer font differs from code editor");
            metadataWindow!.Close();
            model.Text = code.Replace("Member++;", "Member++; MissingSymbol();");
            await model.AnalyzeAsync(); Require(model.Diagnostics.Any(d => d.Id == "CS0103"), "Semantic diagnostics");
            model.Text = code;
            Require((await model.QuickInfoAsync(code.IndexOf("Member++", StringComparison.Ordinal), CancellationToken.None))?.Contains("Member") == true, "Quick info");
            await model.GoToDefinitionAsync(code.IndexOf("Member++", StringComparison.Ordinal));
            Require(model.NavigationOffset == code.IndexOf("Member =", StringComparison.Ordinal), "Definition navigation");
            await model.AnalyzeAsync();
            await Task.Delay(100);
            using (var screenshot = new RenderTargetBitmap(new PixelSize((int)view.Bounds.Width, (int)view.Bounds.Height), new Vector(96, 96)))
            {
                screenshot.Render(view); screenshot.Save(System.IO.Path.Combine(System.IO.Path.GetTempPath(), "stride-script-editor-wpf-layout.png"));
            }
            model.Text = code.Replace("public int Member = 7;", "public    int    Member=7;");
            await model.FormatAsync(); Require(model.Text.Contains("int Member = 7;") && model.Text.Contains("\r\n"), "Formatting and line endings");
            Require(model.Save(), "Save failed"); Require(!model.IsModified && File.ReadAllText(path) == model.Text && File.ReadAllBytes(path).Take(3).SequenceEqual(new byte[] { 0xef, 0xbb, 0xbf }), "Save text/BOM/dirty state");
            var external = model.Text + "// external\r\n";
            await File.WriteAllTextAsync(path, external, new UTF8Encoding(true));
            await Wait(() => model.Text == external);
            model.Text += "// buffer\r\n";
            var disk = external + "// disk conflict\r\n";
            await File.WriteAllTextAsync(path, disk, new UTF8Encoding(true));
            await Wait(() => model.HasExternalConflict);
            Require(!session.Save(logger) && File.ReadAllText(path) == disk, "Session Save overwrote an external change");
            model.KeepChanges(); Require(model.Save() && File.ReadAllText(path) == model.Text, "Keep-buffer/save");
            model.Text += "// retained after tab close\r\n";
            var retained = model.Text;
            await session.EditorCollection.CloseEditorAsync(model); model = null;
            await File.WriteAllTextAsync(path, disk, new UTF8Encoding(true));
            Require(!session.Save(logger) && File.ReadAllText(path) == disk, "Closed dirty buffer overwrote the file");
            session.EditorCollection.OpenAssetEditor(asset); model = (ScriptSourceFileEditorViewModel)session.EditorCollection.ActiveEditor!;
            Require(model.Text == retained && model.HasExternalConflict, "Reopen lost dirty buffer/conflict");
            model.Reload(); Require(model.Text == disk && !model.IsModified && !model.HasExternalConflict, "Reload conflict resolution");
            model.Text += "// save closed buffer\r\n";
            retained = model.Text;
            await session.EditorCollection.CloseEditorAsync(model); model = null;
            Require(session.Save(logger) && File.ReadAllText(path) == retained && !asset.IsDirty && !asset.AssetItem.IsDirty, "Saving a closed buffer did not clear dirtiness");
            Console.WriteLine("PASS: C# editor/highlighting/native Undo/Redo, actual project semantic diagnostics, completion/insertion/description, quick info, definition navigation, metadata declarations, signature help, multi-file rename/preview/stale protection, code fixes/refactorings, formatting, BOM/newlines, session save, external changes/conflicts and closed-buffer retention.");
        }
        finally
        {
            if (model != null) await session.EditorCollection.CloseEditorAsync(model);
            if (session.EditorCollection.TryGetAssetEditor<ScriptSourceFileEditorViewModel>(otherAsset, out var remaining)) await session.EditorCollection.CloseEditorAsync(remaining);
            File.WriteAllBytes(path, original); asset.Asset.Text = File.ReadAllText(path); ((IDirtiable)asset).UpdateDirtiness(false); asset.AssetItem.IsDirty = false;
            File.WriteAllBytes(otherPath, otherOriginal); otherAsset.Asset.Text = File.ReadAllText(otherPath); ((IDirtiable)otherAsset).UpdateDirtiness(false); otherAsset.AssetItem.IsDirty = false;
            reload.AutoReload = false;
        }
    }
    private static async Task CheckTyping(ScriptSourceFileEditorViewModel model, ScriptSourceFileEditorView view, string code)
    {
        var large = code.Replace("public int Member = 7;", "public int Member = 7;\r\n" + string.Join("\r\n", Enumerable.Range(0, 1500).Select(i => $"    public int Method{i}(int value) {{ return value + {i}; }}")));
        model.Text = large + "// typing: ";
        await model.AnalyzeAsync(); await Task.Delay(200);
        var clock = Stopwatch.StartNew(); var last = clock.Elapsed.TotalMilliseconds; var maxGap = 0.0; var maxEdit = 0.0;
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(10) };
        timer.Tick += (_, _) => { var now = clock.Elapsed.TotalMilliseconds; maxGap = Math.Max(maxGap, now - last); last = now; };
        timer.Start();
        try
        {
            for (var batch = 0; batch < 6; batch++)
            {
                for (var i = 0; i < 5; i++)
                {
                    var edit = Stopwatch.StartNew();
                    view.Editor.Document.Insert(view.Editor.Document.TextLength, "x");
                    view.Editor.CaretOffset = view.Editor.Document.TextLength;
                    maxEdit = Math.Max(maxEdit, edit.Elapsed.TotalMilliseconds);
                    await Task.Delay(35);
                }
                await Task.Delay(350);
            }
            await model.AnalyzeAsync(); await Task.Delay(100);
        }
        finally { timer.Stop(); model.Text = code; }
        Console.WriteLine($"C# typing performance ({view.Editor.FontFamily.Name}): {large.Length} characters, maximum input handler {maxEdit:F1} ms, maximum UI heartbeat gap {maxGap:F1} ms.");
        Require(maxEdit < 200 && maxGap < 500, $"Typing blocked the UI: handler {maxEdit:F1} ms, heartbeat {maxGap:F1} ms");
    }
    private static void RenderIcons()
    {
        var keys = new[] { "GlyphClass", "GlyphStruct", "GlyphInterface", "GlyphEnum", "GlyphDelegate", "GlyphMethod", "GlyphExtensionMethod", "GlyphProperty", "GlyphField", "GlyphEvent", "GlyphLocal", "GlyphParameter", "GlyphNamespace", "GlyphKeyword", "Bulb", "ImageError", "ImageWarning" };
        var sheet = new StackPanel { Width = keys.Length * 56, Height = 112 };
        foreach (var dark in new[] { true, false })
        {
            var row = new StackPanel { Orientation = global::Avalonia.Layout.Orientation.Horizontal, Background = dark ? Brushes.DarkSlateGray : Brushes.White, Height = 56 };
            foreach (var key in keys)
            {
                var image = ScriptEditorIcons.Get(key);
                Require(image.Size.Width > 0 && image.Size.Height > 0, "Empty icon: " + key);
                row.Children.Add(new StackPanel { Width = 56, Margin = new Thickness(0, 8, 0, 0), Children = { new Image { Source = image, Width = 16, Height = 16 }, new TextBlock { Text = key.Replace("Glyph", "").Replace("Image", ""), FontSize = 8, Foreground = dark ? Brushes.White : Brushes.Black, HorizontalAlignment = global::Avalonia.Layout.HorizontalAlignment.Center } } });
            }
            sheet.Children.Add(row);
        }
        sheet.Measure(new Size(sheet.Width, sheet.Height)); sheet.Arrange(new Rect(0, 0, sheet.Width, sheet.Height));
        using var bitmap = new RenderTargetBitmap(new PixelSize((int)sheet.Width, (int)sheet.Height), new Vector(96, 96));
        bitmap.Render(sheet); bitmap.Save(System.IO.Path.Combine(System.IO.Path.GetTempPath(), "stride-script-icons.png"));
    }
    private static IEnumerable<Window> Windows() => ((IClassicDesktopStyleApplicationLifetime)global::Avalonia.Application.Current!.ApplicationLifetime!).Windows;
    private static async Task Wait(Func<bool> condition) { for (var i = 0; i < 100; i++) { if (condition()) return; await Task.Delay(100); } throw new TimeoutException("Script editor did not observe the external change."); }
    private static void Require(bool condition, string message) { if (!condition) throw new Exception("C# editor: " + message); }
}
