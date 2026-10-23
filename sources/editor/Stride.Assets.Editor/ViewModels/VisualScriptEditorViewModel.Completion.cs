// Copyright (c) .NET Foundation and Contributors (https://dotnetfoundation.org/ & https://stride3d.net)
// Distributed under the MIT license. See the LICENSE.md file in the project root for more information.
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Completion;
using Stride.Assets.Editor.ScriptEditor;
using Stride.Assets.Scripts;
using Stride.Core.Assets;
using Stride.Core.Assets.TextAccessors;

namespace Stride.Assets.Editor.ViewModels;

public sealed partial class VisualScriptEditorViewModel
{
    public async Task<(IReadOnlyList<ScriptCompletion> Items, int PrefixLength)> CompleteBlockAsync(VisualScriptBlockViewModel block, string text, int offset, bool expression, CancellationToken token)
    {
        if (IsDestroyed || GeneratedSource == null) return ([], 0);
        var path = Asset.AssetItem.GetGeneratedAbsolutePath().ToOSPath();
        var document = await ScriptWorkspace.For(Session).DocumentAsync(path, GeneratedSource, token, (Asset.AssetItem.Package.Container as SolutionProject)?.FullPath.ToString());
        var method = block.Method.Method;
        var prefix = string.Join("\n", Asset.Asset.UsingDirectives.Select(u => "using " + u + ";")) + "\n";
        var ns = Asset.Asset.Namespace ?? Asset.AssetItem.Package.RootNamespace;
        if (!string.IsNullOrWhiteSpace(ns)) prefix += "namespace " + ns + ";\n";
        prefix += "class __VisualScriptSnippet_" + block.Block.Id.ToString("N") + (string.IsNullOrWhiteSpace(BaseType) ? "" : " : " + BaseType) + " {\n";
        prefix += string.Join("\n", Properties.Select(p => "public " + p.Type + " " + p.Name + ";"));
        prefix += "\n" + (expression ? "object" : method.ReturnType) + " __Snippet(" + string.Join(", ", method.Parameters.Select(p => (p.RefKind == ParameterRefKind.None ? "" : p.RefKind.ToString().ToLowerInvariant() + " ") + p.Type + " " + p.Name)) + ") {\n";
        if (expression) prefix += "return (";
        // The snippet lives only in this immutable project fork, never in the workspace or on disk.
        var snippet = document.Project.AddDocument("VisualScriptSnippet.cs", prefix + text + (expression ? ");" : "") + "\n} }");
        var position = prefix.Length + Math.Clamp(offset, 0, text.Length);
        var items = await Task.Run(async () =>
        {
            var service = CompletionService.GetService(snippet);
            var results = service == null ? null : await service.GetCompletionsAsync(snippet, position, cancellationToken: token);
            return (IReadOnlyList<ScriptCompletion>)(results?.ItemsList.Select(i => new ScriptCompletion(i.DisplayTextPrefix + i.DisplayText + i.DisplayTextSuffix, i, snippet))
                .OrderBy(i => i.KindOrder).ThenBy(i => i.Text, StringComparer.OrdinalIgnoreCase).ToArray() ?? []);
        }, token);
        return (items, prefix.Length);
    }
    public IReadOnlyList<string> GetVariableNames(VisualScriptMethodViewModel method)
    {
        var names = Properties.Select(p => p.Name).Concat(method.Parameters.Select(p => p.Name)).ToList();
        var type = compilation?.GetTypeByMetadataName((Asset.Asset.Namespace ?? Asset.AssetItem.Package.RootNamespace) + "." + Asset.Name);
        for (var current = type?.BaseType; current != null; current = current.BaseType)
            names.AddRange(current.GetMembers().Where(m => m is IFieldSymbol or IPropertySymbol && compilation!.IsSymbolAccessibleWithin(m, type!)).Select(m => m.Name));
        return names.Distinct().OrderBy(n => n, StringComparer.OrdinalIgnoreCase).ToArray();
    }
}
