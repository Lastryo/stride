// Copyright (c) .NET Foundation and Contributors (https://dotnetfoundation.org/ & https://stride3d.net)
// Distributed under the MIT license. See the LICENSE.md file in the project root for more information.
using System.Reflection;
using System.Composition;
using System.Composition.Hosting;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CodeRefactorings;
using System.ComponentModel;
using Stride.Assets.Presentation.ViewModels;
using Stride.Core.Diagnostics;
using Stride.Core.Presentation.Dirtiables;
using System.Runtime.CompilerServices;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Host.Mef;
using Microsoft.CodeAnalysis.MSBuild;
using Microsoft.CodeAnalysis.Text;
using Stride.Core.Assets.Editor.ViewModels;

namespace Stride.Assets.Editor.ScriptEditor;

/// <summary>One project workspace per session. Immutable document overlays never write files through MSBuildWorkspace.</summary>
internal sealed class ScriptWorkspace
{
    private static readonly ConditionalWeakTable<SessionViewModel, ScriptWorkspace> Workspaces = new();
    private readonly SessionViewModel session;
    private readonly SemaphoreSlim gate = new(1);
    private readonly Dictionary<string, string> buffers = new(PathComparer);
    private readonly Dictionary<DocumentId, string> appliedTexts = new();
    private MSBuildWorkspace? workspace;
    private CompositionHost? composition;
    public IEnumerable<CodeFixProvider> CodeFixes => composition!.GetExports<CodeFixProvider>();
    public IEnumerable<CodeRefactoringProvider> Refactorings => composition!.GetExports<CodeRefactoringProvider>();
    private Solution? solution;
    private bool disposed;
    private readonly Dictionary<string, SourceState> sources = new(PathComparer);
    private readonly LoggerResult logger = new("C# source files");
    public static StringComparer PathComparer => OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
    private ScriptWorkspace(SessionViewModel session)
    {
        this.session = session;
        session.OutputLog.AddLogger(logger);
        session.Saving += BeforeSave;
        session.Saved += (_, _) => { foreach (var entry in sources.Values) { entry.Baseline = entry.Asset.Asset.Text; ((IDirtiable)entry.Asset).UpdateDirtiness(false); entry.Asset.AssetItem.IsDirty = false; } };

        session.Closing += (_, _) => { session.Saving -= BeforeSave; session.OutputLog.RemoveLogger(logger); disposed = true; _ = DisposeAsync(); };
    }
    public string Register(ScriptSourceFileViewModel asset, string path, string diskText)
    {
        if (!sources.TryGetValue(path, out var state)) sources[path] = state = new(asset, diskText);
        else if (!asset.IsDirty && !asset.AssetItem.IsDirty) state.Baseline = diskText;
        return state.Baseline;
    }
    public void Rename(string previous, string current)
    {
        if (sources.Remove(previous, out var state)) sources[current] = state;
        Close(previous);
    }
    public bool MatchesBaseline(string path, string? diskText) => sources.TryGetValue(path, out var source) && source.Baseline == diskText;
    public void AcceptExternal(string path, string text) { if (sources.TryGetValue(path, out var state)) state.Baseline = text; }
    private void BeforeSave(object? sender, CancelEventArgs e)
    {
        foreach (var entry in sources)
        {
            if (entry.Value.Asset.IsDeleted || !File.Exists(entry.Key)) continue;
            try
            {
                var disk = File.ReadAllText(entry.Key);
                if (disk == entry.Value.Baseline || disk == entry.Value.Asset.Asset.Text) continue;
                if (entry.Value.Asset.Asset.Text == entry.Value.Baseline)
                { entry.Value.Asset.Asset.Text = disk; entry.Value.Baseline = disk; continue; }
                e.Cancel = true; logger.Error($"{entry.Key} changed externally. Open the script and choose Reload or Keep changes before saving.");
            }
            catch (IOException exception) { e.Cancel = true; logger.Error("Cannot verify the source before saving.", exception); }
        }
    }
    private sealed class SourceState(ScriptSourceFileViewModel asset, string baseline)
    { public ScriptSourceFileViewModel Asset => asset; public string Baseline = baseline; }
    public event EventHandler? SourceChanged;
    public void NotifySourceChanged() => SourceChanged?.Invoke(this, EventArgs.Empty);
    public async Task ReloadProjectsAsync()
    {
        await gate.WaitAsync();
        try { workspace?.CloseSolution(); solution = null; appliedTexts.Clear(); }
        finally { gate.Release(); }
    }
    public static ScriptWorkspace For(SessionViewModel session) => Workspaces.GetValue(session, s => new(s));
    public async Task<Document> DocumentAsync(string path, string text, CancellationToken token, string? projectFile = null)
    {
        // Capture project paths on the dispatcher; loading and compilation run on the worker.
        var projects = session.LocalPackages.SelectMany(p => p.PackageContainer.Package.LoadedAssemblies)
            .Select(a => a.ProjectReference?.Location.ToString()).OfType<string>().Distinct().ToArray();
        var sourceBuffers = sources.Select(s => (s.Key, Text: s.Value.Asset.Asset.Text)).ToArray();
        return await Task.Run(async () =>
        {
            await gate.WaitAsync(token);
            try
            {
                ObjectDisposedException.ThrowIf(disposed, this);
                token.ThrowIfCancellationRequested();
                path = Path.GetFullPath(path);
                foreach (var source in sourceBuffers) buffers[source.Key] = source.Text;
                buffers[path] = text;
                if (workspace == null)
                {
                    var assemblies = MefHostServices.DefaultAssemblies
                        .Add(Assembly.Load("Microsoft.CodeAnalysis.CSharp.Workspaces"))
                        .Add(Assembly.Load("Microsoft.CodeAnalysis.Features"))
                        .Add(Assembly.Load("Microsoft.CodeAnalysis.CSharp.Features"));
                    composition = new ContainerConfiguration().WithAssemblies(assemblies.Distinct()).CreateContainer();
                    workspace = MSBuildWorkspace.Create(new Dictionary<string, string> { ["Configuration"] = "Debug", ["Platform"] = "AnyCPU" }, MefHostServices.Create(composition));
                    workspace.LoadMetadataForReferencedProjects = true;
                }
                var projectPath = projectFile ?? projects.Where(p => Within(path, Path.GetDirectoryName(p)!)).OrderByDescending(p => p.Length).FirstOrDefault()
                    ?? throw new InvalidOperationException("This script does not belong to a loaded local code project.");
                if (!workspace.CurrentSolution.Projects.Any(p => PathComparer.Equals(p.FilePath, projectPath)))
                {
                    await workspace.OpenProjectAsync(projectPath, cancellationToken: token);
                    solution = workspace.CurrentSolution;
                    appliedTexts.Clear();
                }
                solution ??= workspace.CurrentSolution;
                var project = solution.Projects.First(p => PathComparer.Equals(p.FilePath, projectPath));
                foreach (var buffer in buffers)
                {
                    var ids = solution.GetDocumentIdsWithFilePath(buffer.Key);
                    if (ids.Count() == 0 && (Within(buffer.Key, Path.GetDirectoryName(projectPath)!) || PathComparer.Equals(buffer.Key, path)))
                    {
                        var added = solution.AddDocument(DocumentId.CreateNewId(project.Id), Path.GetFileName(buffer.Key), SourceText.From(buffer.Value), filePath: buffer.Key);
                        solution = added;
                        ids = solution.GetDocumentIdsWithFilePath(buffer.Key);
                    }
                    foreach (var id in ids) await UpdateTextAsync(id, buffer.Value, token);
                }
                return solution.GetDocument(solution.GetDocumentIdsWithFilePath(path).First())!;
            }
            finally { gate.Release(); }
        }, token);
    }
    private async Task UpdateTextAsync(DocumentId id, string value, CancellationToken token)
    {
        if (appliedTexts.TryGetValue(id, out var applied) && applied == value) return;
        var previousText = await solution!.GetDocument(id)!.GetTextAsync(token);
        var previous = previousText.ToString();
        if (previous != value)
        {
            // Preserve SourceText change ranges so Roslyn can reuse the unchanged syntax and method bodies.
            var prefix = 0;
            while (prefix < previous.Length && prefix < value.Length && previous[prefix] == value[prefix]) prefix++;
            var suffix = 0;
            while (suffix < previous.Length - prefix && suffix < value.Length - prefix && previous[previous.Length - suffix - 1] == value[value.Length - suffix - 1]) suffix++;
            var change = new TextChange(TextSpan.FromBounds(prefix, previous.Length - suffix), value.Substring(prefix, value.Length - prefix - suffix));
            solution = solution.WithDocumentText(id, previousText.WithChanges(change), PreservationMode.PreserveIdentity);
        }
        appliedTexts[id] = value;
    }
    public void Close(string path) { _ = RemoveAsync(path); }
    private async Task RemoveAsync(string path)
    {
        await gate.WaitAsync();
        try
        {
            buffers.Remove(path);
            if (solution != null && sources.TryGetValue(path, out var source))
                foreach (var id in solution.GetDocumentIdsWithFilePath(path))
                    await UpdateTextAsync(id, source.Asset.Asset.Text, CancellationToken.None);
        }
        finally { gate.Release(); }
    }
    private async Task DisposeAsync()
    {
        await gate.WaitAsync();
        try { workspace?.Dispose(); composition?.Dispose(); composition = null; workspace = null; solution = null; buffers.Clear(); appliedTexts.Clear(); }
        finally { gate.Release(); }
    }
    private static bool Within(string path, string directory) => Path.GetRelativePath(directory, path) is { } relative && relative != ".." && !relative.StartsWith(".." + Path.DirectorySeparatorChar) && !Path.IsPathRooted(relative);
}
