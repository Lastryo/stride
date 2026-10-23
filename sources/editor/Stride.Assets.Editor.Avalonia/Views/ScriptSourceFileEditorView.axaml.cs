// Copyright (c) .NET Foundation and Contributors (https://dotnetfoundation.org/ & https://stride3d.net)
// Distributed under the MIT license. See the LICENSE.md file in the project root for more information.
using System.ComponentModel;
using Avalonia;
using Avalonia.Controls.Primitives;
using Stride.Assets.Editor.Avalonia.ScriptEditor;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using AvaloniaEdit.CodeCompletion;
using AvaloniaEdit.Document;
using AvaloniaEdit.Editing;
using AvaloniaEdit.Highlighting;
using AvaloniaEdit.Indentation.CSharp;
using AvaloniaEdit.Rendering;
using AvaloniaEdit.Search;
using Stride.Assets.Editor.ViewModels;
using Stride.Core.Assets.Editor.Annotations;
using Stride.Core.Assets.Editor.Editors;

namespace Stride.Assets.Editor.Avalonia.Views;

[AssetEditorView<ScriptSourceFileEditorViewModel>]
public partial class ScriptSourceFileEditorView : UserControl, IAssetEditorView
{
    private static FontFamily? editorFont;
    private ScriptSourceFileEditorViewModel? model;
    private ScriptSourceFileEditorViewModel? initializedModel;
    private bool syncing;
    private CompletionWindow? completion;
    private readonly SearchPanel search;
    private readonly DiagnosticRenderer markers;
    private CancellationTokenSource? request;
    private CancellationTokenSource? signatureRequest;
    public ScriptSourceFileEditorView()
    {
        InitializeComponent();
        CodeEditor.FontFamily = editorFont ??= ResolveEditorFont();
        CodeEditor.SyntaxHighlighting = HighlightingManager.Instance.GetDefinition("C#");
        CodeEditor.Options.ConvertTabsToSpaces = true; CodeEditor.Options.IndentationSize = 4;
        CodeEditor.TextArea.IndentationStrategy = new CSharpIndentationStrategy(CodeEditor.Options);
        search = SearchPanel.Install(CodeEditor);
        CodeEditor.TextArea.TextView.LineTransformers.Add(new SemanticColors(() => model?.Classifications ?? [], () => CodeEditor.ActualThemeVariant == global::Avalonia.Styling.ThemeVariant.Dark));
        markers = new DiagnosticRenderer(CodeEditor.TextArea.TextView, () => model?.Diagnostics ?? []);
        CodeEditor.TextArea.TextView.BackgroundRenderers.Add(markers);
        CodeEditor.TextChanged += (_, _) => { if (!syncing && model != null) model.Text = CodeEditor.Text; };
        CodeEditor.TextArea.TextEntered += async (_, e) => { if (e.Text == ".") await Complete(); if (e.Text is "(" or "," or "[") await UpdateSignatures(); };
        CodeEditor.TextArea.TextEntering += (_, e) => { if (completion != null && e.Text?.Length > 0 && !char.IsLetterOrDigit(e.Text[0]) && e.Text[0] != '_') completion.CompletionList.RequestInsertion(e); };
        CodeEditor.TextArea.Caret.PositionChanged += (_, _) =>
        {
            if (signaturePopup.IsOpen) _ = UpdateSignatures();
            _ = UpdateActionBulb();
            PositionInlineControls();
        };
        SetupWpfSurface();
        DataContextChanged += (_, _) => BindModel();
        DetachedFromVisualTree += (_, _) => { completion?.Close(); request?.Cancel(); signatureRequest?.Cancel(); CloseInlineControls(); Unbind(); };
        AttachedToVisualTree += (_, _) => BindModel();
        CodeEditor.AddHandler(KeyDownEvent, HandleKeys, RoutingStrategies.Tunnel);
    }
    private static FontFamily ResolveEditorFont()
    {
        // A fallback chain beginning with an absent font makes Skia repeat native font matching for every text run.
        var installed = FontManager.Current.SystemFonts.ToArray();
        foreach (var name in new[] { "Consolas", "DejaVu Sans Mono", "Liberation Mono", "Cascadia Mono", "Menlo" })
        {
            var family = installed.FirstOrDefault(font => string.Equals(font.Name, name, StringComparison.OrdinalIgnoreCase));
            if (family != null) return family;
        }
        return new FontFamily("monospace");
    }
    public AvaloniaEdit.TextEditor Editor => CodeEditor;
    public CompletionWindow? CompletionPopup => completion;
    private void BindModel()
    {
        if (ReferenceEquals(model, DataContext)) return;
        Unbind(); model = DataContext as ScriptSourceFileEditorViewModel;
        if (model == null) return;
        model.PropertyChanged += ModelChanged; model.NavigationRequested += Navigate; model.MetadataRequested += MetadataRequested;
        syncing = true;
        if (!ReferenceEquals(initializedModel, model))
        {
            CodeEditor.Text = model.InitialText; CodeEditor.Document.UndoStack.ClearAll(); initializedModel = model;
        }
        if (CodeEditor.Text != model.Text)
        {
            CodeEditor.Document.Replace(0, CodeEditor.Document.TextLength, model.Text);
            if (!model.IsModified) CodeEditor.Document.UndoStack.ClearAll();
        }
        syncing = false;
        CodeEditor.IsReadOnly = !model.Asset.IsEditable;
        ProjectProblem.IsVisible = model.Status.StartsWith("C# project unavailable");
        _ = UpdateActionBulb();
        if (model.NavigationOffset > 0) Navigate(model, EventArgs.Empty);
    }
    private void Unbind() { if (model != null) { model.PropertyChanged -= ModelChanged; model.NavigationRequested -= Navigate; model.MetadataRequested -= MetadataRequested; model = null; } }
    private void ModelChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (model == null) return;
        if (e.PropertyName == nameof(model.Text) && CodeEditor.Text != model.Text)
        {
            var offset = CodeEditor.CaretOffset;
            syncing = true; CodeEditor.Document.Replace(0, CodeEditor.Document.TextLength, model.Text); syncing = false;
            CodeEditor.CaretOffset = Math.Min(offset, CodeEditor.Document.TextLength);
            if (!model.IsModified) CodeEditor.Document.UndoStack.ClearAll();
        }
        if (e.PropertyName == nameof(model.Classifications)) CodeEditor.TextArea.TextView.Redraw();
        if (e.PropertyName == nameof(model.Diagnostics)) { CodeEditor.TextArea.TextView.InvalidateLayer(KnownLayer.Selection); _ = UpdateActionBulb(); }
        if (e.PropertyName == nameof(model.Status)) ProjectProblem.IsVisible = model.Status.StartsWith("C# project unavailable");
    }
    private void Navigate(object? sender, EventArgs e) { if (model != null) GoTo(model.NavigationOffset); }
    private void GoTo(int offset) { CodeEditor.CaretOffset = Math.Clamp(offset, 0, CodeEditor.Document.TextLength); CodeEditor.ScrollToLine(CodeEditor.Document.GetLineByOffset(CodeEditor.CaretOffset).LineNumber); CodeEditor.Focus(); }
    private async Task Complete()
    {
        if (model == null || CodeEditor.IsReadOnly || completion != null) return;
        var snapshot = model.Text; var offset = CodeEditor.CaretOffset;
        try
        {
            var items = await model.CompleteAsync(offset);
            if (model.Text != snapshot || offset != CodeEditor.CaretOffset || !IsAttachedToVisualTree()) return;
            if (items.Count == 0) return;
            var popup = completion = new CompletionWindow(CodeEditor.TextArea);
            popup.StartOffset = items[0].Item.Span.Start;
            foreach (var item in items) popup.CompletionList.CompletionData.Add(new CompletionData(model, item));
            popup.Closed += (_, _) => { if (ReferenceEquals(completion, popup)) completion = null; };
            popup.Show();
        }
        catch (Exception) { /* Project load failures are shown above the editor. */ }
    }
    private bool IsAttachedToVisualTree() => TopLevel.GetTopLevel(this) != null;
    private async void HandleKeys(object? sender, KeyEventArgs e)
    {
        if (model == null) return;
        if (e.KeyModifiers.HasFlag(KeyModifiers.Control) && e.Key == Key.S) { e.Handled = true; model.Save(); }
        else if (e.KeyModifiers.HasFlag(KeyModifiers.Control) && e.KeyModifiers.HasFlag(KeyModifiers.Shift) && e.Key == Key.Space) { e.Handled = true; await UpdateSignatures(); }
        else if (e.KeyModifiers.HasFlag(KeyModifiers.Control) && e.Key == Key.OemPeriod) { e.Handled = true; await Run(ShowActions); }
        else if (e.Key == Key.F2) { e.Handled = true; await Run(Rename); }
        else if (e.Key == Key.Escape && signaturePopup.IsOpen) { e.Handled = true; signaturePopup.IsOpen = false; signatureRequest?.Cancel(); }
        else if (e.KeyModifiers.HasFlag(KeyModifiers.Control) && e.Key == Key.Space) { e.Handled = true; await Complete(); }
        else if (e.Key == Key.F12) { e.Handled = true; await Run(() => model.GoToDefinitionAsync(CodeEditor.CaretOffset)); }
    }
    private async Task Run(Func<Task> action) { try { await action(); } catch (OperationCanceledException) { } catch (Exception exception) { ToolTip.SetTip(CodeEditor, exception.Message); ToolTip.SetIsOpen(CodeEditor, true); } }
    private void ReloadClick(object? sender, RoutedEventArgs e) => model?.Reload();
    private void KeepClick(object? sender, RoutedEventArgs e) => model?.KeepChanges();
    private sealed class CompletionData(ScriptSourceFileEditorViewModel model, ScriptCompletion item) : ICompletionData
    {
        public IImage Image => ScriptEditorIcons.Completion(item.Item.Tags);
        public string Text => item.Text;
        public object Content => item.Text;
        private TextBlock? description;
        public object Description
        {
            get
            {
                if (description != null) return description;
                description = new TextBlock { Text = item.Text, TextWrapping = TextWrapping.Wrap, MaxWidth = 550 };
                _ = LoadDescription();
                return description;
            }
        }
        private async Task LoadDescription()
        {
            try { description!.Text = await model.DescribeAsync(item); }
            catch (OperationCanceledException) { }
            catch (Exception) { /* Keep the item label when project services are unavailable. */ }
        }
        public double Priority => 0;
        public async void Complete(TextArea area, ISegment segment, EventArgs e)
        {
            var snapshot = model.Text;
            try
            {
                var change = await model.CompletionChangeAsync(item);
                if (model.Text != snapshot) return;
                area.Document.Replace(change.Start, Math.Max(change.Length, segment.EndOffset - change.Start), change.Text);
                if (change.Caret is { } caret) area.Caret.Offset = caret;
            }
            catch (OperationCanceledException) { }
            catch (Exception exception) { ToolTip.SetTip(area, exception.Message); }
        }
    }
    private sealed class SemanticColors(Func<IReadOnlyList<ScriptClassification>> spans, Func<bool> dark) : DocumentColorizingTransformer
    {
        private static readonly IBrush[] Dark = [Brush.Parse("#569cd6"), Brush.Parse("#ce9178"), Brush.Parse("#6a9955"), Brush.Parse("#4ec9b0"), Brush.Parse("#dcdcaa"), Brush.Parse("#b5cea8"), Brush.Parse("#d4d4d4")];
        private static readonly IBrush[] Light = [Brush.Parse("#0000ff"), Brush.Parse("#a31515"), Brush.Parse("#008000"), Brush.Parse("#267f99"), Brush.Parse("#795e26"), Brush.Parse("#098658"), Brush.Parse("#202020")];
        protected override void ColorizeLine(DocumentLine line)
        {
            var classified = spans(); var colors = dark() ? Dark : Light;
            var low = 0; var high = classified.Count;
            while (low < high)
            {
                var middle = low + (high - low) / 2;
                if (classified[middle].Start < line.Offset) low = middle + 1; else high = middle;
            }
            while (low > 0 && classified[low - 1].Start + classified[low - 1].Length > line.Offset) low--;
            for (var i = low; i < classified.Count && classified[i].Start < line.EndOffset; i++)
            {
                var span = classified[i];
                var start = Math.Max(line.Offset, span.Start); var end = Math.Min(line.EndOffset, span.Start + span.Length);
                if (start >= end) continue;
                var index = span.Kind switch
                {
                    "keyword" or "control keyword" => 0,
                    "string" or "verbatim string" => 1,
                    "comment" => 2,
                    "class name" or "struct name" or "interface name" or "enum name" or "delegate name" or "type parameter name" => 3,
                    "method name" or "extension method name" => 4,
                    "number" => 5,
                    _ => 6,
                };
                ChangeLinePart(start, end, element => element.TextRunProperties.SetForegroundBrush(colors[index]));
            }
        }
    }
    private sealed class DiagnosticRenderer(TextView view, Func<IReadOnlyList<ScriptDiagnostic>> diagnostics) : IBackgroundRenderer
    {
        public KnownLayer Layer => KnownLayer.Selection;
        public void Draw(TextView textView, DrawingContext context)
        {
            if (!view.VisualLinesValid) return;
            foreach (var diagnostic in diagnostics().Take(200))
            {
                if (diagnostic.Offset >= view.Document.TextLength) continue;
                var segment = new TextSegment { StartOffset = diagnostic.Offset, Length = Math.Max(1, Math.Min(diagnostic.Length, view.Document.TextLength - diagnostic.Offset)) };
                foreach (var rect in BackgroundGeometryBuilder.GetRectsForSegment(view, segment))
                    context.DrawLine(new Pen(diagnostic.Severity == Microsoft.CodeAnalysis.DiagnosticSeverity.Error ? Brushes.IndianRed : Brushes.Goldenrod, 1.5), rect.BottomLeft, rect.BottomRight);
            }
        }
    }
}
