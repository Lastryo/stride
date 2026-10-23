// Copyright (c) .NET Foundation and Contributors (https://dotnetfoundation.org/ & https://stride3d.net)
// Distributed under the MIT license. See the LICENSE.md file in the project root for more information.
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Stride.Assets.Editor.ScriptEditor;
using Stride.Assets.Scripts;
using Stride.Core.Assets;
using Stride.Core.Presentation.Collections;
using Stride.Core.Quantum;

namespace Stride.Assets.Editor.ViewModels;

public sealed partial class VisualScriptEditorViewModel
{
    private CancellationTokenSource? analysisCancellation;
    private Compilation? compilation;
    private bool regenerating;
    private string? generatedSource;
    private string? analysisError;
    public string? GeneratedSource { get => generatedSource; private set => SetValue(ref generatedSource, value); }
    public string? AnalysisError { get => analysisError; private set => SetValue(ref analysisError, value); }
    public ObservableList<VisualScriptDiagnostic> Diagnostics { get; } = new();
    public ObservableList<IMethodSymbol> OverridableMethods { get; } = new();
    public async Task AnalyzeAsync()
    {
        if (IsDestroyed || regenerating) return;
        Dispatcher.EnsureAccess();
        analysisCancellation?.Cancel();
        var cancellation = analysisCancellation = new CancellationTokenSource();
        var token = cancellation.Token;
        try
        {
            await Task.Delay(250, token);
            var item = Asset.AssetItem.Clone(true);
            var result = await Task.Run(() => ((VisualScriptAsset)item.Asset).Compile(item), token);
            var document = await ScriptWorkspace.For(Session).DocumentAsync(result.SyntaxTree.FilePath, result.GeneratedSource, token, (Asset.AssetItem.Package.Container as SolutionProject)?.FullPath.ToString());
            var projectCompilation = (await document.Project.GetCompilationAsync(token))!;
            var oldTree = await document.GetSyntaxTreeAsync(token);
            result.SyntaxTree = result.SyntaxTree.WithRootAndOptions(result.SyntaxTree.GetRoot(token), oldTree!.Options);
            var analyzed = await Task.Run(() =>
            {
                var updated = projectCompilation.ReplaceSyntaxTree(oldTree!, result.SyntaxTree);
                var diagnostics = new List<VisualScriptDiagnostic>();
                var root = result.SyntaxTree.GetRoot(token);
                foreach (var diagnostic in updated.GetDiagnostics(token).Where(d => d.Location.SourceTree == result.SyntaxTree && d.Severity != DiagnosticSeverity.Hidden))
                {
                    var node = root.FindNode(diagnostic.Location.SourceSpan, getInnermostNodeForTie: true);
                    var annotated = node.AncestorsAndSelf().FirstOrDefault(n => n.HasAnnotations("Block") || n.HasAnnotations("Link"));
                    Guid? Id(string kind) => Guid.TryParse(annotated?.GetAnnotations(kind).FirstOrDefault()?.Data, out var id) ? id : null;
                    diagnostics.Add(new(diagnostic.GetMessage(), diagnostic.Severity, Id("Block"), Id("Link")));
                }
                foreach (var message in result.Messages)
                    diagnostics.Add(new(message.Text, message.Type == Stride.Core.Diagnostics.LogMessageType.Error ? DiagnosticSeverity.Error : DiagnosticSeverity.Warning, null, null));
                var typeNode = root.DescendantNodes().OfType<ClassDeclarationSyntax>().FirstOrDefault();
                var type = typeNode == null ? null : updated.GetSemanticModel(result.SyntaxTree).GetDeclaredSymbol(typeNode);
                var overrides = new List<IMethodSymbol>();
                for (var parent = type?.BaseType; parent != null; parent = parent.BaseType)
                {
                    foreach (var method in parent.GetMembers().OfType<IMethodSymbol>().Where(m => m.MethodKind == MethodKind.Ordinary && !m.IsSealed && (m.IsVirtual || m.IsAbstract || m.IsOverride)))
                    {
                        if (!updated.IsSymbolAccessibleWithin(method, type!) || type!.GetMembers(method.Name).OfType<IMethodSymbol>().Any(m => Overrides(m, method))) continue;
                        if (!overrides.Any(m => Overrides(m, method))) overrides.Add(method);
                    }
                }
                if (type != null)
                    overrides.AddRange(type.AllInterfaces.SelectMany(i => i.GetMembers().OfType<IMethodSymbol>()).Where(m => m.MethodKind == MethodKind.Ordinary && type.FindImplementationForInterfaceMember(m) == null));
                return (Compilation: updated, Diagnostics: diagnostics, Overrides: overrides);
            }, token);
            token.ThrowIfCancellationRequested();
            if (IsDestroyed) return;
            compilation = analyzed.Compilation;
            GeneratedSource = result.GeneratedSource;
            AnalysisError = null;
            Diagnostics.Clear(); Diagnostics.AddRange(analyzed.Diagnostics);
            OverridableMethods.Clear(); OverridableMethods.AddRange(analyzed.Overrides);
            RegenerateAllSlots();
            GraphChanged?.Invoke(this, EventArgs.Empty);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        catch (Exception exception)
        {
            if (!IsDestroyed && !token.IsCancellationRequested)
            {
                AnalysisError = exception.Message;
                logger.Error("Could not analyze visual script.", exception);
            }
        }
        finally { cancellation.Dispose(); if (ReferenceEquals(analysisCancellation, cancellation)) analysisCancellation = null; }
    }
    private static bool Overrides(IMethodSymbol method, IMethodSymbol other)
    {
        for (var current = method.OverriddenMethod; current != null; current = current.OverriddenMethod)
            if (SymbolEqualityComparer.Default.Equals(current, other)) return true;
        return false;
    }
    private void RegenerateAllSlots(Block? editedBlock = null)
    {
        regenerating = true;
        try
        {
            using var transaction = UndoRedoService.CreateTransaction();
            foreach (var method in Asset.Asset.Methods)
            foreach (var block in method.Blocks.Values.Where(b => editedBlock == null || b == editedBlock))
            {
                var generated = new List<Slot>();
                try { block.GenerateSlots(generated, new SlotGeneratorContext(compilation)); }
                catch (Exception exception) when (exception is ArgumentException or InvalidOperationException) { continue; }
                var retained = new HashSet<Slot>();
                for (var i = 0; i < generated.Count; i++)
                {
                    var slot = generated[i];
                    var previous = block.Slots.FirstOrDefault(s => !retained.Contains(s) && s.Direction == slot.Direction && s.Kind == slot.Kind && s.Name == slot.Name);
                    if (previous == null) continue;
                    retained.Add(previous);
                    slot.Type ??= previous.Type;
                    if (previous.Type == slot.Type && previous.Flags == slot.Flags) generated[i] = previous;
                    else { slot.Id = previous.Id; slot.Value = previous.Value ?? slot.Value; }
                }
                if (block.Slots.SequenceEqual(generated)) continue;
                var map = block.Slots.ToDictionary(s => s, s => generated.FirstOrDefault(n => n.Id == s.Id));
                var links = method.Links.Values.Where(l => map.ContainsKey(l.Source) || map.ContainsKey(l.Target)).ToArray();
                var linkNode = Node(method)[nameof(Method.Links)].Target;
                foreach (var link in links) linkNode.Remove(link, new NodeIndex(link.Id));
                var slotNode = Node(block)[nameof(Block.Slots)].Target;
                for (var i = block.Slots.Count - 1; i >= 0; i--) slotNode.Remove(block.Slots[i], new NodeIndex(i));
                foreach (var slot in generated) slotNode.Add(slot);
                foreach (var link in links)
                {
                    var source = map.TryGetValue(link.Source, out var s) ? s : link.Source;
                    var target = map.TryGetValue(link.Target, out var t) ? t : link.Target;
                    if (source == null || target == null) continue;
                    Node(link)[nameof(Link.Source)].Update(source);
                    Node(link)[nameof(Link.Target)].Update(target);
                    linkNode.Add(link, new NodeIndex(link.Id));
                }
            }
            UndoRedoService.SetName(transaction, "Regenerated slots (code update)");
        }
        finally { regenerating = false; }
        if (editedBlock == null) Refresh();
    }
    public async Task<IReadOnlyList<IMethodSymbol>> SearchMethodsAsync(string text, CancellationToken token)
    {
        var current = compilation;
        if (current == null) return [];
        return await Task.Run(() =>
        {
            var results = new List<IMethodSymbol>();
            var assemblies = new[] { current.Assembly }.Concat(current.SourceModule.ReferencedAssemblySymbols)
                .OrderBy(a => SymbolEqualityComparer.Default.Equals(a, current.Assembly) ? 0 : a.Name.Contains("Stride") ? 1 : a.Name.StartsWith("System", StringComparison.Ordinal) ? 3 : 2);
            foreach (var assembly in assemblies)
            {
                var stack = new Stack<INamespaceOrTypeSymbol>(); stack.Push(assembly.GlobalNamespace);
                while (stack.TryPop(out var node))
                {
                    token.ThrowIfCancellationRequested();
                    if (node is INamespaceSymbol ns) { foreach (var child in ns.GetNamespaceMembers()) stack.Push(child); foreach (var type in ns.GetTypeMembers()) stack.Push(type); }
                    else if (node is INamedTypeSymbol type)
                    {
                        foreach (var nested in type.GetTypeMembers()) stack.Push(nested);
                        if (type.DeclaredAccessibility != Microsoft.CodeAnalysis.Accessibility.Public && !SymbolEqualityComparer.Default.Equals(assembly, current.Assembly)) continue;
                        foreach (var method in type.GetMembers().OfType<IMethodSymbol>())
                        {
                            if (method.MethodKind is not (MethodKind.Ordinary or MethodKind.UserDefinedOperator or MethodKind.Conversion)) continue;
                            if (method.DeclaredAccessibility != Microsoft.CodeAnalysis.Accessibility.Public && !SymbolEqualityComparer.Default.Equals(assembly, current.Assembly)) continue;
                            var display = method.ToDisplayString();
                            if (!display.Contains(text, StringComparison.OrdinalIgnoreCase) && !new string(display.Where(char.IsUpper).ToArray()).Contains(text, StringComparison.OrdinalIgnoreCase)) continue;
                            results.Add(method);
                            if (results.Count == 100) return (IReadOnlyList<IMethodSymbol>)results;
                        }
                    }
                }
            }
            return results;
        }, token);
    }
    public void AddMethodCall(IMethodSymbol method, Stride.Core.Mathematics.Int2 position)
    {
        var format = new SymbolDisplayFormat(typeQualificationStyle: SymbolDisplayTypeQualificationStyle.NameAndContainingTypesAndNamespaces, memberOptions: SymbolDisplayMemberOptions.IncludeContainingType);
        var block = new MethodCallBlock { IsMemberCall = !method.IsStatic || method.IsExtensionMethod, MethodName = !method.IsStatic || method.IsExtensionMethod ? method.Name : method.ToDisplayString(format) };
        block.GenerateSlots(block.Slots, new SlotGeneratorContext(compilation));
        if (!method.IsStatic) block.Slots.Add(new Slot(SlotDirection.Input, SlotKind.Value, "this", type: method.ContainingType.ToDisplayString()));
        foreach (var parameter in method.Parameters) block.Slots.Add(new Slot(SlotDirection.Input, SlotKind.Value, parameter.Name, type: parameter.Type.ToDisplayString()));
        if (!method.ReturnsVoid) block.Slots.Add(new Slot(SlotDirection.Output, SlotKind.Value, type: method.ReturnType.ToDisplayString()));
        AddBlock(block, position);
    }
}
public sealed record VisualScriptDiagnostic(string Message, DiagnosticSeverity Severity, Guid? BlockId, Guid? LinkId);
