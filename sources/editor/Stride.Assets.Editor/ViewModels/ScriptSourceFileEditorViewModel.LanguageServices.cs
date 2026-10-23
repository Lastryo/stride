// Copyright (c) .NET Foundation and Contributors (https://dotnetfoundation.org/ & https://stride3d.net)
// Distributed under the MIT license. See the LICENSE.md file in the project root for more information.
using System.Xml.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CodeRefactorings;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.FindSymbols;
using Microsoft.CodeAnalysis.Rename;
using Microsoft.CodeAnalysis.Text;
using Stride.Assets.Editor.ScriptEditor;
using Stride.Assets.Presentation.ViewModels;

namespace Stride.Assets.Editor.ViewModels;

public sealed record ScriptContextAction(string Title, CodeAction Action, Solution Original, bool IsCodeFix = false);
public sealed record ScriptDocumentEdit(string Path, string Before, string After, string? DiskText);
public sealed record ScriptEditPreview(string Title, IReadOnlyList<ScriptDocumentEdit> Documents);
public sealed record ScriptSignatureHelp(IReadOnlyList<string> Signatures, int ArgumentIndex);

public sealed partial class ScriptSourceFileEditorViewModel
{
    public string? MetadataText { get; private set; }
    public string? MetadataTitle { get; private set; }
    public event EventHandler? MetadataRequested;

    public async Task<ScriptSignatureHelp?> SignatureHelpAsync(int offset, CancellationToken token = default)
    {
        var document = await workspace.DocumentAsync(path, text, token);
        return await Task.Run(async () =>
        {
            var root = await document.GetSyntaxRootAsync(token);
            if (root == null || offset <= 0 || offset > root.FullSpan.End) return null;
            var arguments = root.FindToken(offset - 1).Parent?.AncestorsAndSelf().OfType<BaseArgumentListSyntax>()
                .FirstOrDefault(a =>
                {
                    var close = a switch { ArgumentListSyntax list => list.CloseParenToken, BracketedArgumentListSyntax list => list.CloseBracketToken, _ => default };
                    return a.Span.Start < offset && (close.IsMissing || offset <= close.SpanStart);
                });
            if (arguments == null) return null;
            var semantic = (await document.GetSemanticModelAsync(token))!;
            IEnumerable<ISymbol> symbols = arguments.Parent switch
            {
                InvocationExpressionSyntax call => semantic.GetMemberGroup(call.Expression, token),
                ObjectCreationExpressionSyntax creation => (semantic.GetTypeInfo(creation, token).Type as INamedTypeSymbol)?.InstanceConstructors.Cast<ISymbol>() ?? [],
                ImplicitObjectCreationExpressionSyntax creation => (semantic.GetTypeInfo(creation, token).Type as INamedTypeSymbol)?.InstanceConstructors.Cast<ISymbol>() ?? [],
                ElementAccessExpressionSyntax access => semantic.GetTypeInfo(access.Expression, token).Type?.GetMembers().OfType<IPropertySymbol>().Where(p => p.IsIndexer).Cast<ISymbol>() ?? [],
                _ => [],
            };
            var index = arguments.Arguments.GetSeparators().Count(s => s.SpanStart < offset);
            var named = arguments.Arguments.ElementAtOrDefault(index)?.NameColon?.Name.Identifier.ValueText;
            var signatures = new List<string>();
            foreach (var symbol in symbols.Distinct(SymbolEqualityComparer.Default))
            {
                var parameters = symbol switch { IMethodSymbol method => method.Parameters, IPropertySymbol property => property.Parameters, _ => [] };
                var selected = named == null ? index : parameters.IndexOf(parameters.FirstOrDefault(p => p.Name == named)!);
                if (selected >= parameters.Length && parameters.LastOrDefault()?.IsParams == true) selected = parameters.Length - 1;
                var prefix = symbol is IMethodSymbol m && m.MethodKind == MethodKind.Constructor ? m.ContainingType.Name : symbol.Name;
                var signature = prefix + "(" + string.Join(", ", parameters.Select((p, i) => i == selected ? "[" + p.ToDisplayString() + "]" : p.ToDisplayString())) + ")";
                var summary = Documentation(symbol);
                signatures.Add(signature + (summary.Length == 0 ? "" : "\n" + summary));
            }
            return signatures.Count == 0 ? null : new ScriptSignatureHelp(signatures, index);
        }, token);
    }

    private static string Documentation(ISymbol symbol)
    {
        var xml = symbol.GetDocumentationCommentXml();
        if (string.IsNullOrEmpty(xml)) return "";
        try { return string.Join(" ", (XElement.Parse(xml).Element("summary")?.Value ?? "").Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)); }
        catch (System.Xml.XmlException) { return ""; }
    }
    private void ShowMetadata(ISymbol symbol)
    {
        var type = symbol as INamedTypeSymbol ?? symbol.ContainingType;
        MetadataTitle = symbol.ToDisplayString();
        var lines = new List<string> { "// Metadata declarations (method bodies are not available)", "// Assembly: " + symbol.ContainingAssembly?.Identity, "// " + Documentation(symbol), "", symbol.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat), "" };
        if (type != null)
        {
            lines.Add(type.ToDisplayString(SymbolDisplayFormat.CSharpErrorMessageFormat));
            lines.Add("{");
            lines.AddRange(type.GetMembers().Where(m => !m.IsImplicitlyDeclared && m.DeclaredAccessibility == Accessibility.Public && m is not IMethodSymbol { AssociatedSymbol: not null })
                .Select(m => "    " + m.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat) + ";"));
            lines.Add("}");
        }
        MetadataText = string.Join(Environment.NewLine, lines);
        MetadataRequested?.Invoke(this, EventArgs.Empty);
    }

    public async Task<IReadOnlyList<ScriptContextAction>> ContextActionsAsync(int start, int length, CancellationToken token = default)
    {
        var document = await workspace.DocumentAsync(path, text, token);
        return await Task.Run(async () =>
        {
            var span = new TextSpan(start, length);
            var actions = new List<ScriptContextAction>();
            void Add(CodeAction action, bool isCodeFix, string prefix = "")
            {
                if (action.NestedActions.Length > 0) foreach (var child in action.NestedActions) Add(child, isCodeFix, prefix + action.Title + " → ");
                else actions.Add(new(prefix + action.Title, action, document.Project.Solution, isCodeFix));
            }
            var semantic = (await document.GetSemanticModelAsync(token))!;
            var diagnostics = semantic.GetDiagnostics(cancellationToken: token).Where(d => d.Location.IsInSource && d.Location.SourceSpan.IntersectsWith(span)).ToArray();
            foreach (var provider in workspace.CodeFixes)
            {
                if (!provider.GetType().GetCustomAttributes(typeof(ExportCodeFixProviderAttribute), false).Cast<ExportCodeFixProviderAttribute>().Any(a => a.Languages.Contains(LanguageNames.CSharp))) continue;
                foreach (var diagnostic in diagnostics.Where(d => provider.FixableDiagnosticIds.Contains(d.Id)))
                {
                    try { await provider.RegisterCodeFixesAsync(new CodeFixContext(document, diagnostic, (action, _) => Add(action, true), token)); }
                    catch (OperationCanceledException) { throw; }
                    catch (Exception exception) { logger.Debug($"Code fix {provider.GetType().Name}: {exception.Message}"); }
                }
            }
            foreach (var provider in workspace.Refactorings)
            {
                if (!provider.GetType().GetCustomAttributes(typeof(ExportCodeRefactoringProviderAttribute), false).Cast<ExportCodeRefactoringProviderAttribute>().Any(a => a.Languages.Contains(LanguageNames.CSharp))) continue;
                try { await provider.ComputeRefactoringsAsync(new CodeRefactoringContext(document, span, action => Add(action, false), token)); }
                catch (OperationCanceledException) { throw; }
                catch (Exception exception) { logger.Debug($"Refactoring {provider.GetType().Name}: {exception.Message}"); }
            }
            return (IReadOnlyList<ScriptContextAction>)actions.DistinctBy(a => a.Title).OrderBy(a => a.Title).ToArray();
        }, token);
    }

    public async Task<ScriptEditPreview> PreviewActionAsync(ScriptContextAction action)
    {
        var operations = await action.Action.GetOperationsAsync(lifetime.Token);
        if (operations.Length != 1 || operations[0] is not ApplyChangesOperation apply)
            throw new InvalidOperationException("This action requires operations other than editing existing source files.");
        return await PreviewAsync(action.Title, action.Original, apply.ChangedSolution);
    }
    public async Task<ScriptEditPreview> PreviewRenameAsync(int offset, string name)
    {
        if (!SyntaxFacts.IsValidIdentifier(name)) throw new ArgumentException("Enter a valid C# identifier.");
        var document = await workspace.DocumentAsync(path, text, lifetime.Token);
        var symbol = await SymbolFinder.FindSymbolAtPositionAsync(document, offset, lifetime.Token);
        if (symbol == null || !symbol.Locations.Any(l => l.IsInSource)) throw new InvalidOperationException("Place the caret on a source symbol to rename it.");
        var renamed = await Renamer.RenameSymbolAsync(document.Project.Solution, symbol, new SymbolRenameOptions(), name, lifetime.Token);
        await Task.Run(async () =>
        {
            foreach (var project in renamed.GetChanges(document.Project.Solution).GetProjectChanges())
            {
                static string Key(Diagnostic diagnostic) => diagnostic.Id + ":" + diagnostic.Location.SourceTree?.FilePath + ":" + diagnostic.GetMessage();
                var before = (await project.OldProject.GetCompilationAsync(lifetime.Token))!.GetDiagnostics(lifetime.Token)
                    .Where(d => d.Severity == DiagnosticSeverity.Error).GroupBy(Key).ToDictionary(g => g.Key, g => g.Count());
                var after = (await project.NewProject.GetCompilationAsync(lifetime.Token))!.GetDiagnostics(lifetime.Token)
                    .Where(d => d.Severity == DiagnosticSeverity.Error).GroupBy(Key);
                var introduced = after.FirstOrDefault(g => g.Count() > before.GetValueOrDefault(g.Key));
                if (introduced != null) throw new InvalidOperationException("Rename introduces a compiler error: " + introduced.First().GetMessage());
            }
        }, lifetime.Token);
        return await PreviewAsync("Rename " + symbol.Name + " to " + name, document.Project.Solution, renamed);
    }
    private async Task<ScriptEditPreview> PreviewAsync(string title, Solution original, Solution changed)
    {
        var changes = changed.GetChanges(original);
        if (changes.GetAddedProjects().Any() || changes.GetRemovedProjects().Any()) throw new InvalidOperationException("Project changes are not supported.");
        var edits = new Dictionary<string, ScriptDocumentEdit>(ScriptWorkspace.PathComparer);
        foreach (var project in changes.GetProjectChanges())
        {
            if (!Equals(project.OldProject.CompilationOptions, project.NewProject.CompilationOptions)
                || !Equals(project.OldProject.ParseOptions, project.NewProject.ParseOptions)
                || project.OldProject.Name != project.NewProject.Name
                || project.GetAddedAnalyzerReferences().Any()
                || project.GetRemovedAnalyzerReferences().Any()
                || project.GetAddedDocuments().Any()
                || project.GetRemovedDocuments().Any()
                || project.GetAddedAdditionalDocuments().Any()
                || project.GetRemovedAdditionalDocuments().Any()
                || project.GetChangedAdditionalDocuments().Any()
                || project.GetAddedAnalyzerConfigDocuments().Any()
                || project.GetRemovedAnalyzerConfigDocuments().Any()
                || project.GetChangedAnalyzerConfigDocuments().Any()
                || project.GetAddedMetadataReferences().Any()
                || project.GetRemovedMetadataReferences().Any()
                || project.GetAddedProjectReferences().Any()
                || project.GetRemovedProjectReferences().Any())
                throw new InvalidOperationException("This action requires project changes or creating/removing files; only existing source edits are supported.");
            foreach (var id in project.GetChangedDocuments())
            {
                var before = original.GetDocument(id)!; var after = changed.GetDocument(id)!;
                var root = await after.GetSyntaxRootAsync(lifetime.Token);
                if (root?.GetAnnotatedNodesAndTokens(ConflictAnnotation.Kind).Any() == true) throw new InvalidOperationException("The action has unresolved symbol conflicts. Choose another name or action.");
                if (before.FilePath == null || after.FilePath != before.FilePath || before.Name != after.Name) throw new InvalidOperationException("Renaming files is not supported.");
                var file = Path.GetFullPath(before.FilePath);
                if (!File.Exists(file)) throw new InvalidOperationException("A source file no longer exists: " + file);
                var edit = new ScriptDocumentEdit(file, (await before.GetTextAsync(lifetime.Token)).ToString(), (await after.GetTextAsync(lifetime.Token)).ToString(), File.Exists(file) ? File.ReadAllText(file) : null);
                if (edits.TryGetValue(file, out var existing) && existing.After != edit.After) throw new InvalidOperationException("The action produced conflicting edits for a linked file.");
                if (FindScript(file) == null) throw new InvalidOperationException("The action changes a file outside the session's source assets: " + file);
                edits[file] = edit;
            }
        }
        return new(title, edits.Values.ToArray());
    }
    private ScriptSourceFileViewModel? FindScript(string file) => Session.AllAssets.OfType<ScriptSourceFileViewModel>().FirstOrDefault(a => ScriptWorkspace.PathComparer.Equals(Path.GetFullPath(a.AssetItem.FullPath.ToString()), file));

    public void ApplyPreview(ScriptEditPreview preview)
    {
        Dispatcher.EnsureAccess();
        ObjectDisposedException.ThrowIf(IsDestroyed, this);
        var targets = preview.Documents.Select(edit => (Edit: edit, Asset: FindScript(edit.Path) ?? throw new InvalidOperationException("A source asset was removed."))).ToArray();
        // Validate every target before changing any buffer; previews can outlive edits and external file changes.
        foreach (var (edit, asset) in targets)
            if (!asset.IsEditable || asset.IsDeleted || asset.Asset.Text != edit.Before || (edit.DiskText != edit.Before && !workspace.MatchesBaseline(edit.Path, edit.DiskText)) || (File.Exists(edit.Path) ? File.ReadAllText(edit.Path) : null) != edit.DiskText)
                throw new InvalidOperationException("A source changed after this preview was created. Request the action again.");
        var active = Session.EditorCollection.ActiveEditor;
        try
        {
            // Create all editors before any edit, so constructor/read failures leave every buffer intact.
            foreach (var (_, asset) in targets)
            {
                if (!Session.EditorCollection.TryGetAssetEditor<ScriptSourceFileEditorViewModel>(asset, out _)) Session.EditorCollection.OpenAssetEditor(asset);
                if (!Session.EditorCollection.TryGetAssetEditor<ScriptSourceFileEditorViewModel>(asset, out var target) || target.IsClosing)
                    throw new InvalidOperationException("A source editor is unavailable or closing.");
            }
            foreach (var (edit, asset) in targets)
                if (Session.EditorCollection.TryGetAssetEditor<ScriptSourceFileEditorViewModel>(asset, out var target)) target.Text = edit.After;
        }
        finally { Session.EditorCollection.ActiveEditor = active; }
        Status = $"{preview.Title}: updated {targets.Length} source file(s). Save to write changes to disk.";
    }
}
