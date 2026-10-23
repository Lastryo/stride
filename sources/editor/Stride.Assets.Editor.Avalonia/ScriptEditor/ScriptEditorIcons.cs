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
    private static readonly Dictionary<string, IImage> CompletionImages = new();
    public static IImage Get(string key) => Resources.TryGetResource(key, null, out var resource) && resource is IImage image
        ? image : throw new KeyNotFoundException("Script editor icon not found: " + key);

    public static IImage Completion(ImmutableArray<string> tags)
    {
        if (tags.IsDefault) tags = [];
        var glyph = tags.Contains(WellKnownTags.ExtensionMethod) ? "GlyphExtensionMethod"
            : tags.Contains(WellKnownTags.Class) ? "GlyphClass"
            : tags.Contains(WellKnownTags.Structure) ? "GlyphStruct"
            : tags.Contains(WellKnownTags.Interface) ? "GlyphInterface"
            : tags.Contains(WellKnownTags.Enum) ? "GlyphEnum"
            : tags.Contains(WellKnownTags.EnumMember) || tags.Contains(WellKnownTags.Constant) ? "GlyphConstant"
            : tags.Contains(WellKnownTags.Delegate) ? "GlyphDelegate"
            : tags.Contains(WellKnownTags.Method) || tags.Contains(WellKnownTags.Operator) ? "GlyphMethod"
            : tags.Contains(WellKnownTags.Property) ? "GlyphProperty"
            : tags.Contains(WellKnownTags.Field) ? "GlyphField"
            : tags.Contains(WellKnownTags.Event) ? "GlyphEvent"
            : tags.Contains(WellKnownTags.Local) ? "GlyphLocal"
            : tags.Contains(WellKnownTags.Parameter) ? "GlyphParameter"
            : tags.Contains(WellKnownTags.Namespace) ? "GlyphNamespace"
            : tags.Contains(WellKnownTags.Keyword) || tags.Contains(WellKnownTags.Intrinsic) ? "GlyphKeyword"
            : tags.Contains(WellKnownTags.TypeParameter) ? "GlyphTypeParameter" : "GlyphSymbol";
        var badge = tags.Contains(WellKnownTags.Private) ? "GlyphPrivate"
            : tags.Contains(WellKnownTags.Protected) ? "GlyphProtected"
            : tags.Contains(WellKnownTags.Internal) ? "GlyphInternal" : null;
        if (badge == null) return Get(glyph);
        var key = glyph + badge;
        if (!CompletionImages.TryGetValue(key, out var image))
        {
            var drawing = new DrawingGroup();
            drawing.Children.Add(((DrawingImage)Get(glyph)).Drawing!);
            drawing.Children.Add(((DrawingImage)Get(badge)).Drawing!);
            CompletionImages[key] = image = new DrawingImage { Drawing = drawing };
        }
        return image;
    }
    public static IImage Diagnostic(DiagnosticSeverity severity) => Get(severity switch
    {
        DiagnosticSeverity.Error => "ImageError", DiagnosticSeverity.Warning => "ImageWarning", _ => "ImageInformation",
    });
}
