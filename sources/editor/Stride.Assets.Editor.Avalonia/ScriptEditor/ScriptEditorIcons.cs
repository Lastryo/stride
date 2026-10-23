// Copyright (c) .NET Foundation and Contributors (https://dotnetfoundation.org/ & https://stride3d.net)
// Distributed under the MIT license. See the LICENSE.md file in the project root for more information.
using System.Collections.Immutable;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Tags;

namespace Stride.Assets.Editor.Avalonia.ScriptEditor;

public static class ScriptEditorIcons
{
    private static readonly ResourceDictionary Resources = (ResourceDictionary)AvaloniaXamlLoader.Load(new Uri("avares://Stride.Assets.Editor.Avalonia/ScriptEditor/ScriptEditorIcons.axaml"));
    public static IImage Get(string key) => Resources.TryGetResource(key, null, out var resource) && resource is IImage image
        ? image : throw new KeyNotFoundException("Script editor icon not found: " + key);

    public static IImage Completion(ImmutableArray<string> tags)
    {
        if (tags.IsDefault) tags = [];
        var access = tags.Contains(WellKnownTags.Private) ? "Private"
            : tags.Contains(WellKnownTags.Protected) ? "Protected"
            : tags.Contains(WellKnownTags.Internal) ? "Internal" : "Public";
        if (tags.Contains(WellKnownTags.Operator)) return Get("RoslynOperator");
        if (tags.Contains(WellKnownTags.Module)) return Get("RoslynModule" + (access == "Internal" ? "Public" : access));
        var kind = tags.Contains(WellKnownTags.ExtensionMethod) ? "ExtensionMethod"
            : tags.Contains(WellKnownTags.Class) ? "Class"
            : tags.Contains(WellKnownTags.Structure) ? "Structure"
            : tags.Contains(WellKnownTags.Interface) ? "Interface"
            : tags.Contains(WellKnownTags.EnumMember) ? "EnumMember"
            : tags.Contains(WellKnownTags.Enum) ? "Enum"
            : tags.Contains(WellKnownTags.Constant) ? "Constant"
            : tags.Contains(WellKnownTags.Delegate) ? "Delegate"
            : tags.Contains(WellKnownTags.Method) ? "Method"
            : tags.Contains(WellKnownTags.Property) ? "Property"
            : tags.Contains(WellKnownTags.Field) ? "Field"
            : tags.Contains(WellKnownTags.Event) ? "Event" : null;
        if (kind != null) return Get("Roslyn" + kind + access);
        // RoslynPad's WPF dictionary has no separate local or type-parameter drawings.
        return Get("Roslyn" + (tags.Contains(WellKnownTags.Local) ? "FieldPublic"
            : tags.Contains(WellKnownTags.TypeParameter) ? "ClassPublic"
            : tags.Contains(WellKnownTags.Parameter) ? "Parameter"
            : tags.Contains(WellKnownTags.Namespace) ? "Namespace"
            : tags.Contains(WellKnownTags.Keyword) || tags.Contains(WellKnownTags.Intrinsic) ? "Keyword"
            : tags.Contains(WellKnownTags.Label) ? "Label"
            : tags.Contains(WellKnownTags.Snippet) ? "Snippet"
            : tags.Contains(WellKnownTags.Assembly) ? "Assembly" : "Reference"));
    }

    public static IImage Diagnostic(DiagnosticSeverity severity) => Get(severity switch
    {
        DiagnosticSeverity.Error => "ImageError", DiagnosticSeverity.Warning => "ImageWarning", _ => "ImageInformation",
    });
}
