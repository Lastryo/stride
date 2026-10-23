// Copyright (c) .NET Foundation and Contributors (https://dotnetfoundation.org/ & https://stride3d.net)
// Distributed under the MIT license. See the LICENSE.md file in the project root for more information.
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using AvaloniaEdit.Rendering;
using Stride.Assets.Editor.Avalonia.ScriptEditor;
using Stride.Assets.Editor.ViewModels;

namespace Stride.Assets.Editor.Avalonia.Views;

public partial class ScriptSourceFileEditorView
{
    private readonly ComboBox SignatureList = new() { Name = "SignatureList", MinWidth = 300, MaxWidth = 700, FontSize = 12 };
    private readonly Popup signaturePopup = new() { Placement = PlacementMode.AnchorAndGravity, IsLightDismissEnabled = false };
    private readonly Canvas actionMargin = new() { Width = 20, ClipToBounds = true };
    private readonly Button actionBulb = new() { Name = "ActionsButton", Width = 20, Height = 20, MinWidth = 0, MinHeight = 0, Padding = new Thickness(2), IsVisible = false, Focusable = false, Background = Brushes.Transparent };
    private CancellationTokenSource? actionRequest;
    private ContextMenu? actionMenu;
    private IReadOnlyList<ScriptContextAction> availableActions = [];
    private string? actionText;
    private int actionOffset;
    private int hoverOffset = -1;
    public Button ActionsButton => actionBulb;
    public ComboBox ParameterHelp => SignatureList;
    public bool IsParameterHelpOpen => signaturePopup.IsOpen;
    public global::AvaloniaEdit.Search.SearchPanel Search => search;
    public ContextMenu? ActionMenu => actionMenu;

    private void SetupWpfSurface()
    {
        search.TemplateApplied += (_, e) =>
        {
            var options = e.NameScope.Find<Button>("SearchOptions");
            if (options == null) return;
            var content = new StackPanel { Spacing = 3 };
            CheckBox Option(string label, bool value, Action<bool> change)
            {
                var box = new CheckBox { Content = label, IsChecked = value, FontSize = 12 };
                box.IsCheckedChanged += (_, _) => change(box.IsChecked == true); return box;
            }
            content.Children.Add(Option("Match case", search.MatchCase, value => search.MatchCase = value));
            content.Children.Add(Option("Match whole words", search.WholeWords, value => search.WholeWords = value));
            content.Children.Add(Option("Use regular expressions", search.UseRegex, value => search.UseRegex = value));
            options.Flyout = new Flyout { Content = content };
        };
        actionBulb.Content = new Image { Source = ScriptEditorIcons.Get("Bulb"), Width = 16, Height = 16 };
        ToolTip.SetTip(actionBulb, "Quick actions (Ctrl+.)");
        global::Avalonia.Automation.AutomationProperties.SetName(actionBulb, "Quick actions");
        actionBulb.Click += async (_, _) => await Run(ShowActions);
        actionMargin.Children.Add(actionBulb); CodeEditor.TextArea.LeftMargins.Insert(0, actionMargin);
        signaturePopup.Child = SignatureList; signaturePopup.PlacementTarget = CodeEditor.TextArea;
        ((ISetLogicalParent)signaturePopup).SetParent(this);
        CodeEditor.TextArea.TextView.ScrollOffsetChanged += (_, _) => PositionInlineControls();
        CodeEditor.TextChanged += (_, _) => { if (model != null) _ = UpdateActionBulb(); };
        CodeEditor.AddHandler(PointerWheelChangedEvent, (_, e) =>
        {
            if (!e.KeyModifiers.HasFlag(KeyModifiers.Control)) return;
            CodeEditor.FontSize = Math.Clamp(CodeEditor.FontSize + Math.Sign(e.Delta.Y), 8, 72);
            e.Handled = true;
        }, global::Avalonia.Interactivity.RoutingStrategies.Tunnel);
        CodeEditor.PointerMoved += async (_, e) =>
        {
            var position = CodeEditor.GetPositionFromPoint(e.GetPosition(CodeEditor));
            if (position == null || model == null) return;
            var offset = CodeEditor.Document.GetOffset(position.Value.Location);
            if (offset == hoverOffset) return;
            hoverOffset = offset;
            request?.Cancel(); request?.Dispose(); request = new(); var token = request.Token; var snapshot = model;
            var diagnostic = model.Diagnostics.FirstOrDefault(d => offset >= d.Offset && offset < d.Offset + Math.Max(1, d.Length));
            if (diagnostic != null)
            {
                var content = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, Children = { new Image { Source = ScriptEditorIcons.Diagnostic(diagnostic.Severity), Width = 16, Height = 16 }, new TextBlock { Text = diagnostic.ToString(), TextWrapping = TextWrapping.Wrap, MaxWidth = 600 } } };
                ToolTip.SetTip(CodeEditor, content); return;
            }
            ToolTip.SetTip(CodeEditor, null);
            try
            {
                await Task.Delay(400, token);
                var tip = await snapshot.QuickInfoAsync(offset, token);
                if (!token.IsCancellationRequested && snapshot == model) ToolTip.SetTip(CodeEditor, tip);
            }
            catch (OperationCanceledException) { }
            catch (Exception) { /* Unavailable project services have an explicit status banner. */ }
        };
        CodeEditor.PointerExited += (_, _) => { request?.Cancel(); hoverOffset = -1; ToolTip.SetTip(CodeEditor, null); };
        var commands = new ContextMenu();
        MenuItem Command(string title, string icon, string shortcut, Func<Task> action)
        {
            var item = new MenuItem { Header = title, Icon = new Image { Source = ScriptEditorIcons.Get(icon), Width = 16, Height = 16 }, InputGesture = string.IsNullOrEmpty(shortcut) ? null : KeyGesture.Parse(shortcut) };
            item.Click += async (_, _) => await Run(action); return item;
        }
        commands.Items.Add(Command("Save", "ImageSave", "Ctrl+S", () => { model?.Save(); return Task.CompletedTask; }));
        commands.Items.Add(new Separator());
        commands.Items.Add(Command("Quick actions…", "Bulb", "Ctrl+OemPeriod", ShowActions));
        commands.Items.Add(Command("Rename…", "ImageRename", "F2", Rename));
        commands.Items.Add(Command("Go to definition", "GlyphMethod", "F12", () => model?.GoToDefinitionAsync(CodeEditor.CaretOffset) ?? Task.CompletedTask));
        commands.Items.Add(Command("Parameter help", "GlyphParameter", "Ctrl+Shift+Space", UpdateSignatures));
        commands.Items.Add(Command("Format document", "ImageCodeEditor", "", () => model?.FormatAsync() ?? Task.CompletedTask));
        commands.Items.Add(new Separator());
        commands.Items.Add(Command("Undo", "ImageUndo", "Ctrl+Z", () => { CodeEditor.Undo(); return Task.CompletedTask; }));
        commands.Items.Add(Command("Redo", "ImageRedo", "Ctrl+Y", () => { CodeEditor.Redo(); return Task.CompletedTask; }));
        commands.Items.Add(Command("Find", "ImageFind", "Ctrl+F", () => { search.IsReplaceMode = false; search.Open(); search.Reactivate(); return Task.CompletedTask; }));
        commands.Items.Add(Command("Replace", "ImageFind", "Ctrl+H", () => { search.IsReplaceMode = true; search.Open(); search.Reactivate(); return Task.CompletedTask; }));
        var diagnostics = new MenuItem { Header = "Errors and warnings" };
        commands.Items.Add(diagnostics);
        commands.Opening += (_, _) =>
        {
            diagnostics.Items.Clear();
            foreach (var diagnostic in model?.Diagnostics ?? [])
            {
                var item = new MenuItem { Header = diagnostic.ToString(), Icon = new Image { Source = ScriptEditorIcons.Diagnostic(diagnostic.Severity), Width = 16, Height = 16 } };
                item.Click += (_, _) => GoTo(diagnostic.Offset); diagnostics.Items.Add(item);
            }
            diagnostics.IsEnabled = diagnostics.Items.Count > 0;
        };
        CodeEditor.ContextMenu = commands;
    }
    private async Task UpdateActionBulb()
    {
        actionRequest?.Cancel(); actionRequest?.Dispose(); actionRequest = new();
        var token = actionRequest.Token; var snapshot = model; var text = model?.Text; var offset = CodeEditor.CaretOffset; var length = CodeEditor.SelectionLength; var start = CodeEditor.SelectionStart;
        availableActions = []; actionBulb.IsVisible = false; actionMenu?.Close();
        if (snapshot == null || CodeEditor.IsReadOnly) return;
        try
        {
            await Task.Delay(500, token);
            var actions = await snapshot.ContextActionsAsync(start, length, token);
            if (token.IsCancellationRequested || snapshot != model || text != model.Text || offset != CodeEditor.CaretOffset) return;
            availableActions = actions; actionText = text; actionOffset = offset; PositionInlineControls();
        }
        catch (OperationCanceledException) { }
        catch (Exception) { /* Project load failures are reported by the view model. */ }
    }
    private void PositionInlineControls()
    {
        var view = CodeEditor.TextArea.TextView;
        if (!view.VisualLinesValid || !view.VisualLines.Any(line => line.FirstDocumentLine.LineNumber <= CodeEditor.TextArea.Caret.Line && line.LastDocumentLine.LineNumber >= CodeEditor.TextArea.Caret.Line))
        { actionBulb.IsVisible = false; return; }
        var point = view.GetVisualPosition(CodeEditor.TextArea.Caret.Position, VisualYPosition.LineTop) - view.ScrollOffset;
        Canvas.SetTop(actionBulb, point.Y);
        actionBulb.IsVisible = availableActions.Count > 0 && point.Y >= 0 && point.Y < view.Bounds.Height && actionText == model?.Text && actionOffset == CodeEditor.CaretOffset;
        var caret = view.TranslatePoint(point, CodeEditor.TextArea) ?? point;
        signaturePopup.PlacementRect = new Rect(caret.X, caret.Y + CodeEditor.FontSize * 1.5, 1, 1);
    }
    private void ShowActionMenu(ScriptSourceFileEditorViewModel snapshot, IReadOnlyList<ScriptContextAction> actions)
    {
        actionMenu?.Close(); actionMenu = new ContextMenu();
        foreach (var action in actions)
        {
            var item = new MenuItem { Header = action.Title, Tag = action, Icon = new Image { Source = ScriptEditorIcons.Get(action.IsCodeFix ? "Bulb" : "ImageCodeEditor"), Width = 16, Height = 16 } };
            item.Click += async (_, _) => await Run(async () => { if (snapshot == model) await ShowPreview(snapshot, await snapshot.PreviewActionAsync(action)); });
            actionMenu.Items.Add(item);
        }
        if (actions.Count == 0) actionMenu.Items.Add(new MenuItem { Header = "No actions available", IsEnabled = false });
        actionMenu.Open(actionBulb.IsVisible ? actionBulb : CodeEditor);
    }
    private void CloseInlineControls()
    {
        actionRequest?.Cancel(); actionMenu?.Close(); signaturePopup.IsOpen = false; actionBulb.IsVisible = false;
    }
}
