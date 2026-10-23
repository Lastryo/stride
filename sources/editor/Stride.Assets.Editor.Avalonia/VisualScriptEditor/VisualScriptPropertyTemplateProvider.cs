// Copyright (c) .NET Foundation and Contributors (https://dotnetfoundation.org/ & https://stride3d.net)
// Distributed under the MIT license. See the LICENSE.md file in the project root for more information.
using Stride.Assets.Scripts;
using Stride.Core.Assets.Editor.Avalonia.Views;
using Stride.Core.Presentation.Quantum.Presenters;
using Stride.Core.Presentation.Quantum.ViewModels;

namespace Stride.Assets.Editor.Avalonia.VisualScriptEditor;

public sealed class VisualScriptPropertyTemplateProvider : NodeViewModelTemplateProvider
{
    public override string Name => "VisualScriptCodeOrVariable";
    public override bool MatchNode(NodeViewModel node) => node.Type == typeof(string) && node.NodePresenters.OfType<MemberNodePresenter>().Any(p =>
        p.MemberAttributes.OfType<ScriptCodeAttribute>().Any() || p.MemberAttributes.OfType<ScriptVariableReferenceAttribute>().Any()
        || p.MemberDescriptor.DeclaringType == typeof(CustomExpression) && p.Name == nameof(CustomExpression.Expression));
}
