// Copyright (c) .NET Foundation and Contributors (https://dotnetfoundation.org/ & https://stride3d.net)
// Distributed under the MIT license. See the LICENSE.md file in the project root for more information.
using System.ComponentModel;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Completion;
using Microsoft.CodeAnalysis.Tags;
using Microsoft.CodeAnalysis.Classification;
using Microsoft.CodeAnalysis.FindSymbols;
using Microsoft.CodeAnalysis.Formatting;
using Microsoft.CodeAnalysis.QuickInfo;
using Stride.Assets.Editor.ScriptEditor;
using Stride.Assets.Presentation.ViewModels;
using Stride.Core.Assets.Editor.Annotations;
using Stride.Core.Assets.Editor.ViewModels;
using Stride.Core.Assets.TextAccessors;
using Stride.Core.Diagnostics;
using Stride.Core.Presentation.Dirtiables;

namespace Stride.Assets.Editor.ViewModels;

public sealed record ScriptDiagnostic(string Id, string Message, DiagnosticSeverity Severity, int Offset, int Length, int Line, int Column)
{
    public override string ToString() => $"{Severity} {Id} ({Line},{Column}): {Message}";
}
public sealed record ScriptClassification(int Start, int Length, string Kind);
public sealed record ScriptCompletion(string Text, CompletionItem Item, Document Document)
{
    public int KindOrder => Item.Tags.Contains(WellKnownTags.Property) ? 0
        : Item.Tags.Contains(WellKnownTags.Field) ? 1
        : Item.Tags.Contains(WellKnownTags.Constant) || Item.Tags.Contains(WellKnownTags.EnumMember) ? 2
        : Item.Tags.Contains(WellKnownTags.ExtensionMethod) ? 4
        : Item.Tags.Contains(WellKnownTags.Method) || Item.Tags.Contains(WellKnownTags.Operator) ? 3
        : Item.Tags.Contains(WellKnownTags.Event) ? 5
        : Item.Tags.Contains(WellKnownTags.Local) ? 6
        : Item.Tags.Contains(WellKnownTags.Parameter) ? 7
        : Item.Tags.Contains(WellKnownTags.Class) ? 8
        : Item.Tags.Contains(WellKnownTags.Structure) ? 9
        : Item.Tags.Contains(WellKnownTags.Interface) ? 10
        : Item.Tags.Contains(WellKnownTags.Enum) ? 11
        : Item.Tags.Contains(WellKnownTags.Delegate) ? 12
        : Item.Tags.Contains(WellKnownTags.TypeParameter) ? 13
        : Item.Tags.Contains(WellKnownTags.Namespace) ? 14
        : Item.Tags.Contains(WellKnownTags.Keyword) || Item.Tags.Contains(WellKnownTags.Intrinsic) ? 15 : 16;
}

[AssetEditorViewModel<ScriptSourceFileViewModel>]
public sealed partial class ScriptSourceFileEditorViewModel : AssetEditorViewModel<ScriptSourceFileViewModel>
{
    private readonly ScriptWorkspace workspace;
    private readonly LoggerResult logger = new("C# Editor");
    private FileSystemWatcher? watcher;
    private CancellationTokenSource? analysis;
    private CancellationTokenSource? diskChange;
    private string text;
    private string baseline;
    private string diskText;
    private string path;
    private string status = "Loading C# project…";
    private bool conflict;
    private bool settingDirty;
    private bool metadataDirty;
    private int revision;
    private readonly CancellationTokenSource lifetime = new();
    public ScriptSourceFileEditorViewModel(ScriptSourceFileViewModel asset) : base(asset)
    {
        path = Path.GetFullPath(asset.AssetItem.FullPath.ToString());
        diskText = File.Exists(path) ? File.ReadAllText(path) : asset.Asset.Text;
        workspace = ScriptWorkspace.For(Session);
        baseline = workspace.Register(asset, path, diskText);
        text = asset.IsDirty || asset.AssetItem.IsDirty ? asset.Asset.Text : diskText;
        InitialText = text;
        conflict = diskText != baseline && text != diskText && text != baseline;
        asset.Asset.TextAccessor = new BufferAccessor(text, ReadEncoding(path));
        metadataDirty = asset.IsDirty && text == baseline;
        Session.Saved += OnSaved;
        Session.Saving += OnSaving;
        asset.PropertyChanged += AssetChanged;
        Session.OutputLog.AddLogger(logger);
        Watch(); ScheduleAnalysis();
    }
    public string InitialText { get; }
    public string FilePath => path;
    public string Text
    {
        get => text;
        set
        {
            if (!Asset.IsEditable || text == value) return;
            text = value; revision++;
            Asset.Asset.Text = value;
            workspace.NotifySourceChanged();
            SetDirty(value != baseline);
            OnPropertyChanged(nameof(Text), nameof(IsModified));
            ScheduleAnalysis();
        }
    }
    public bool IsModified => text != baseline;
    public bool HasExternalConflict { get => conflict; private set => SetValue(ref conflict, value); }
    public string Status { get => status; private set => SetValue(ref status, value); }
    public IReadOnlyList<ScriptDiagnostic> Diagnostics { get; private set; } = [];
    public IReadOnlyList<ScriptClassification> Classifications { get; private set; } = [];
    public int NavigationOffset { get; private set; }
    public event EventHandler? NavigationRequested;
    private void SetDirty(bool value)
    {
        settingDirty = true;
        try { ((IDirtiable)Asset).UpdateDirtiness(value || metadataDirty); Asset.AssetItem.IsDirty = value || metadataDirty; }
        finally { settingDirty = false; }
    }
    private void OnSaving(object? sender, CancelEventArgs e)
    {
        if (File.Exists(path))
        {
            var latest = File.ReadAllText(path);
            if (latest != diskText && latest != text) { diskText = latest; HasExternalConflict = true; }
        }
        if (HasExternalConflict) { e.Cancel = true; Status = "Resolve the external source change with Reload or Keep changes before saving."; }
    }
    private void OnSaved(object? sender, EventArgs e)
    {
        metadataDirty = false; baseline = diskText = text; HasExternalConflict = false; SetDirty(false);
        OnPropertyChanged(nameof(IsModified));
    }
    public bool Save()
    {
        if (HasExternalConflict) { Status = "File changed on disk. Choose Reload or Keep changes before saving."; return false; }
        return Session.Save(logger);
    }
    public void KeepChanges() { workspace.AcceptExternal(path, diskText); baseline = diskText; HasExternalConflict = false; SetDirty(IsModified); OnPropertyChanged(nameof(IsModified)); ScheduleAnalysis(); }
    public void Reload()
    {
        if (!File.Exists(path)) { Status = "The source file no longer exists."; return; }
        try
        {
            var loaded = File.ReadAllText(path);
            var encoding = ReadEncoding(path);
            diskText = baseline = loaded;
            workspace.AcceptExternal(path, baseline);
            HasExternalConflict = false;
            text = baseline; revision++;
            Asset.Asset.TextAccessor = new BufferAccessor(text, encoding);
            SetDirty(false); OnPropertyChanged(nameof(Text), nameof(IsModified)); ScheduleAnalysis();
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException) { Status = exception.Message; }
    }
    private void Watch()
    {
        watcher?.Dispose(); watcher = null;
        if (!Directory.Exists(Path.GetDirectoryName(path))) return;
        watcher = new FileSystemWatcher(Path.GetDirectoryName(path)!, Path.GetFileName(path)) { NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.Size | NotifyFilters.FileName };
        watcher.Changed += ChangedOnDisk; watcher.Created += ChangedOnDisk; watcher.Deleted += ChangedOnDisk; watcher.Renamed += ChangedOnDisk;
        watcher.EnableRaisingEvents = true;
    }
    private void ChangedOnDisk(object sender, FileSystemEventArgs e) => _ = Dispatcher.InvokeAsync(() =>
    {
        if (IsDestroyed) return;
        diskChange?.Cancel(); diskChange?.Dispose(); diskChange = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);
        _ = CheckDiskAsync(diskChange.Token);
    });
    private async Task CheckDiskAsync(CancellationToken token)
    {
        try
        {
            await Task.Delay(300, token);
            if (!File.Exists(path)) { HasExternalConflict = true; Status = "Source file removed externally. Choose Keep changes, then Save to recreate it."; return; }
            var changed = await File.ReadAllTextAsync(path, token);
            if (changed == diskText) return;
            diskText = changed;
            if (!IsModified || text == changed) Reload();
            else { HasExternalConflict = true; Status = "Source changed externally. Reload discards your buffer; Keep changes allows saving it."; }
        }
        catch (OperationCanceledException) { }
        catch (IOException exception) { Status = exception.Message; }
    }
    private void AssetChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(Asset.IsDirty) && !settingDirty)
        {
            metadataDirty = Asset.IsDirty;
            if (IsModified && !Asset.IsDirty) SetDirty(true);
        }
        var current = Path.GetFullPath(Asset.AssetItem.FullPath.ToString());
        if (ScriptWorkspace.PathComparer.Equals(current, path)) return;
        workspace.Rename(path, current); path = current; Watch(); OnPropertyChanged(nameof(FilePath)); ScheduleAnalysis();
    }
    private void ScheduleAnalysis()
    {
        analysis?.Cancel(); analysis?.Dispose(); analysis = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);
        _ = AnalyzeAsync(analysis.Token);
    }
    public async Task AnalyzeAsync(CancellationToken token = default)
    {
        var version = revision; var snapshot = text;
        try
        {
            await Task.Delay(250, token);
            var document = await workspace.DocumentAsync(path, snapshot, token);
            var diagnostics = await Task.Run(async () =>
            {
                var semantic = await document.GetSemanticModelAsync(token);
                return semantic!.GetDiagnostics(cancellationToken: token).Where(d => d.Location.IsInSource && d.Severity >= DiagnosticSeverity.Warning).Select(d =>
                {
                    var line = d.Location.GetLineSpan().StartLinePosition;
                    return new ScriptDiagnostic(d.Id, d.GetMessage(), d.Severity, d.Location.SourceSpan.Start, d.Location.SourceSpan.Length, line.Line + 1, line.Character + 1);
                }).ToArray();
            }, token);
            var classified = await Task.Run(async () =>
            {
                var spans = await Classifier.GetClassifiedSpansAsync(document, new Microsoft.CodeAnalysis.Text.TextSpan(0, snapshot.Length), token);
                return spans.OrderBy(c => c.TextSpan.Start).Select(c => new ScriptClassification(c.TextSpan.Start, c.TextSpan.Length, c.ClassificationType)).ToArray();
            }, token);
            if (version != revision || IsDestroyed || token.IsCancellationRequested) return;
            Classifications = classified;
            OnPropertyChanged(nameof(Classifications));
            Diagnostics = diagnostics; OnPropertyChanged(nameof(Diagnostics));
            Status = HasExternalConflict ? "Source changed externally. Choose Reload or Keep changes before saving." : diagnostics.Length == 0 ? "C# project ready" : $"{diagnostics.Count(d => d.Severity == DiagnosticSeverity.Error)} errors, {diagnostics.Count(d => d.Severity == DiagnosticSeverity.Warning)} warnings";
        }
        catch (OperationCanceledException) { }
        catch (Exception exception) { if (!IsDestroyed && version == revision) Status = "C# project unavailable: " + exception.Message; }
    }
    public async Task<IReadOnlyList<ScriptCompletion>> CompleteAsync(int offset, CancellationToken token = default)
    {
        var document = await workspace.DocumentAsync(path, text, token);
        return await Task.Run(async () =>
        {
            var service = CompletionService.GetService(document);
            var list = service == null ? null : await service.GetCompletionsAsync(document, offset, cancellationToken: token);
            return (IReadOnlyList<ScriptCompletion>)(list?.ItemsList.Select(i => new ScriptCompletion(i.DisplayTextPrefix + i.DisplayText + i.DisplayTextSuffix, i, document))
                .OrderBy(i => i.KindOrder).ThenBy(i => i.Text, StringComparer.OrdinalIgnoreCase).ThenBy(i => i.Text, StringComparer.Ordinal).ToArray() ?? []);
        }, token);
    }
    public async Task<(int Start, int Length, string Text, int? Caret)> CompletionChangeAsync(ScriptCompletion item)
    {
        var change = await CompletionService.GetService(item.Document)!.GetChangeAsync(item.Document, item.Item, cancellationToken: lifetime.Token);
        return (change.TextChange.Span.Start, change.TextChange.Span.Length, change.TextChange.NewText ?? "", change.NewPosition);
    }
    public async Task<string> DescribeAsync(ScriptCompletion item)
    {
        var description = await CompletionService.GetService(item.Document)!.GetDescriptionAsync(item.Document, item.Item, lifetime.Token);
        return string.Concat(description.TaggedParts.Select(p => p.Text));
    }
    public async Task<string?> QuickInfoAsync(int offset, CancellationToken token)
    {
        var document = await workspace.DocumentAsync(path, text, token);
        return await Task.Run(async () =>
        {
            var service = QuickInfoService.GetService(document);
            var info = service == null ? null : await service.GetQuickInfoAsync(document, offset, token);
            return info == null ? null : string.Join(Environment.NewLine, info.Sections.Select(s => string.Concat(s.TaggedParts.Select(p => p.Text))));
        }, token);
    }
    public async Task FormatAsync()
    {
        var snapshot = text; var document = await workspace.DocumentAsync(path, snapshot, lifetime.Token);
        var options = document.Project.Solution.Options.WithChangedOption(FormattingOptions.NewLine, LanguageNames.CSharp, snapshot.Contains("\r\n") ? "\r\n" : "\n");
        var formatted = await Formatter.FormatAsync(document, options: options, cancellationToken: lifetime.Token);
        var result = (await formatted.GetTextAsync(lifetime.Token)).ToString();
        if (text == snapshot) Text = result;
    }
    public async Task GoToDefinitionAsync(int offset)
    {
        var document = await workspace.DocumentAsync(path, text, lifetime.Token);
        var symbol = await SymbolFinder.FindSymbolAtPositionAsync(document, offset, lifetime.Token);
        var location = symbol?.Locations.FirstOrDefault(l => l.IsInSource);
        if (location == null)
        {
            if (symbol != null && symbol.Locations.Any(l => l.IsInMetadata)) ShowMetadata(symbol);
            else Status = "Source definition is not available.";
            return;
        }
        var asset = Session.AllAssets.OfType<ScriptSourceFileViewModel>().FirstOrDefault(a => ScriptWorkspace.PathComparer.Equals(Path.GetFullPath(a.AssetItem.FullPath.ToString()), location.SourceTree!.FilePath));
        if (asset == null) { Status = "Definition source is not a script asset in this session."; return; }
        Session.EditorCollection.OpenAssetEditor(asset);
        if (Session.EditorCollection.ActiveEditor is ScriptSourceFileEditorViewModel editor)
        {
            editor.NavigationOffset = location.SourceSpan.Start; editor.NavigationRequested?.Invoke(editor, EventArgs.Empty);
        }
    }
    public override void Destroy()
    {
        lifetime.Cancel(); analysis?.Cancel(); diskChange?.Cancel(); watcher?.Dispose();
        Session.Saved -= OnSaved; Session.Saving -= OnSaving; Asset.PropertyChanged -= AssetChanged;
        workspace.Close(path); Session.OutputLog.RemoveLogger(logger);
        analysis?.Dispose(); diskChange?.Dispose();
        base.Destroy();
    }
    private static Encoding ReadEncoding(string path)
    {
        if (!File.Exists(path)) return new UTF8Encoding(false);
        using var reader = new StreamReader(path, Encoding.UTF8, true); _ = reader.Peek();
        if (reader.CurrentEncoding.CodePage != Encoding.UTF8.CodePage) return reader.CurrentEncoding;
        using var stream = File.OpenRead(path); var bom = new byte[3]; return new UTF8Encoding(stream.Read(bom) == 3 && bom.SequenceEqual(new byte[] { 0xef, 0xbb, 0xbf }));
    }
    private sealed class BufferAccessor(string text, Encoding encoding) : ITextAccessor
    {
        public string Get() => text;
        public void Set(string value) => text = value;
        public async Task Save(Stream stream) { using var writer = new StreamWriter(stream, encoding, 1024, true); await writer.WriteAsync(text); }
        public ISerializableTextAccessor GetSerializableVersion() => new StringTextAccessor { Text = text };
    }
}
