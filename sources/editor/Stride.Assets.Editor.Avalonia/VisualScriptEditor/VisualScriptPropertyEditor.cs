// Copyright (c) .NET Foundation and Contributors (https://dotnetfoundation.org/ & https://stride3d.net)
// Distributed under the MIT license. See the LICENSE.md file in the project root for more information.
using System.ComponentModel;
using Avalonia.Input;
using Avalonia.Interactivity;
using AvaloniaEdit.CodeCompletion;
using AvaloniaEdit.Document;
using AvaloniaEdit.Editing;
using Microsoft.CodeAnalysis.Completion;
using Stride.Assets.Editor.Avalonia.ScriptEditor;
using Avalonia.Controls;
using Avalonia.Media;
using AvaloniaEdit;
using AvaloniaEdit.Highlighting;
using Stride.Assets.Editor.ViewModels;
using Stride.Assets.Scripts;
using Stride.Core.Assets.Presentation.Quantum.NodePresenters;
using Stride.Core.Presentation.Quantum.Presenters;
using Stride.Core.Presentation.Quantum.ViewModels;

namespace Stride.Assets.Editor.Avalonia.VisualScriptEditor;

public sealed class VisualScriptPropertyEditor : UserControl
{
    private NodeViewModel? node;
    private bool syncing;
    private CompletionWindow? completion;
    private CancellationTokenSource? request;
    public CompletionWindow? CompletionPopup => completion;
    public VisualScriptPropertyEditor()
    {
        DataContextChanged += (_, _) => Bind();
        AttachedToVisualTree += (_, _) => Bind();
        DetachedFromVisualTree += (_, _) => { if (node != null) node.PropertyChanged -= Changed; node = null; completion?.Close(); request?.Cancel(); };
    }
    private void Bind()
    {
        if (ReferenceEquals(node, DataContext)) return;
        if (node != null) node.PropertyChanged -= Changed;
        node = DataContext as NodeViewModel;
        if (node == null) { Content = null; return; }
        node.PropertyChanged += Changed;
        if (node.NodePresenters.OfType<MemberNodePresenter>().Any(p => p.MemberAttributes.OfType<ScriptVariableReferenceAttribute>().Any()))
        {
            var owner = node.NodePresenters.OfType<IAssetNodePresenter>().Select(p => p.PropertyProvider).OfType<VisualScriptBlockViewModel>().FirstOrDefault();
            var box = new AutoCompleteBox { Text = node.NodeValue as string, MinimumPrefixLength = 0, FilterMode = AutoCompleteFilterMode.Contains, ItemsSource = owner?.Editor.GetVariableNames(owner.Method) ?? [] };
            box.PropertyChanged += (_, e) => { if (!syncing && e.Property == AutoCompleteBox.TextProperty && node != null) node.NodeValue = box.Text; };
            Content = box;
        }
        else
        {
            var code = new TextEditor { Text = node.NodeValue as string ?? "", MinHeight = 280, ShowLineNumbers = true, FontSize = 12,
                FontFamily = new FontFamily("avares://Stride.Assets.Editor.Avalonia/Resources/Fonts/JetBrainsMono#JetBrains Mono"),
                SyntaxHighlighting = HighlightingManager.Instance.GetDefinition("C#") };
            code.Options.ConvertTabsToSpaces = true; code.Options.IndentationSize = 4;
            var owner = node.NodePresenters.OfType<IAssetNodePresenter>().Select(p => p.PropertyProvider).OfType<VisualScriptBlockViewModel>().FirstOrDefault();
            code.TextChanged += (_, _) =>
            {
                if (syncing || node == null) return;
                if (owner != null) owner.Editor.Update(owner.Block, node.Name, code.Text);
                else node.NodeValue = code.Text;
            };
            async Task Complete()
            {
                if (owner == null || completion != null || node == null) return;
                var snapshot = code.Text; var offset = code.CaretOffset;
                request?.Cancel(); var cancellation = request = new CancellationTokenSource();
                try
                {
                    var result = await owner.Editor.CompleteBlockAsync(owner, snapshot, offset, owner.Block is CustomExpression, cancellation.Token);
                    if (code.Text != snapshot || code.CaretOffset != offset || TopLevel.GetTopLevel(this) == null || result.Items.Count == 0) return;
                    var popup = completion = new CompletionWindow(code.TextArea) { StartOffset = Math.Max(0, result.Items[0].Item.Span.Start - result.PrefixLength) };
                    foreach (var item in result.Items) popup.CompletionList.CompletionData.Add(new BlockCompletion(code, item, result.PrefixLength));
                    popup.Closed += (_, _) => { if (ReferenceEquals(completion, popup)) completion = null; };
                    popup.Show();
                }
                catch (OperationCanceledException) { }
                catch (Exception exception) { ToolTip.SetTip(code, exception.Message); }
                finally { if (ReferenceEquals(request, cancellation)) request = null; cancellation.Dispose(); }
            }
            code.TextArea.TextEntered += async (_, e) => { if (e.Text == ".") await Complete(); };
            code.TextArea.TextEntering += (_, e) => { if (completion != null && e.Text?.Length > 0 && !char.IsLetterOrDigit(e.Text[0]) && e.Text[0] != '_') completion.CompletionList.RequestInsertion(e); };
            code.AddHandler(KeyDownEvent, async (_, e) => { if (e.Key == Key.Space && e.KeyModifiers.HasFlag(KeyModifiers.Control)) { e.Handled = true; await Complete(); } }, RoutingStrategies.Tunnel);
            Content = code;
        }
    }
    private sealed class BlockCompletion(TextEditor editor, ScriptCompletion item, int prefix) : ICompletionData
    {
        public IImage Image => ScriptEditorIcons.Completion(item.Item.Tags);
        public string Text => item.Text;
        public object Content => Text;
        public double Priority => 0;
        public object Description
        {
            get
            {
                var text = new TextBlock { Text = Text, TextWrapping = TextWrapping.Wrap, MaxWidth = 500 };
                _ = Describe(text); return text;
            }
        }
        private async Task Describe(TextBlock text)
        {
            try
            {
                var description = await CompletionService.GetService(item.Document)!.GetDescriptionAsync(item.Document, item.Item);
                if (description != null) text.Text = string.Concat(description.TaggedParts.Select(p => p.Text));
            }
            catch (Exception) { }
        }
        public async void Complete(TextArea area, ISegment segment, EventArgs e)
        {
            var before = editor.Text;
            try
            {
                var change = await CompletionService.GetService(item.Document)!.GetChangeAsync(item.Document, item.Item);
                var start = change.TextChange.Span.Start - prefix;
                var length = Math.Max(change.TextChange.Span.Length, segment.EndOffset - start);
                if (editor.Text != before || start < 0 || start + length > area.Document.TextLength) return;
                area.Document.Replace(start, length, change.TextChange.NewText ?? "");
                if (change.NewPosition is { } caret) area.Caret.Offset = Math.Clamp(caret - prefix, 0, area.Document.TextLength);
            }
            catch (Exception) { }
        }
    }
    private void Changed(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(NodeViewModel.NodeValue)) return;
        syncing = true;
        try
        {
            var value = node?.NodeValue as string ?? "";
            if (Content is TextEditor code && code.Text != value) code.Text = value;
            else if (Content is AutoCompleteBox box && box.Text != value) box.Text = value;
        }
        finally { syncing = false; }
    }
}
