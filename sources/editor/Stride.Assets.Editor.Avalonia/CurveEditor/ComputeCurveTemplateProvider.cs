using Stride.Assets.Editor.CurveEditor;
using Stride.Core.Assets.Editor.Avalonia.Views;
using Stride.Core.Presentation.Quantum.ViewModels;
namespace Stride.Assets.Editor.Avalonia.CurveEditor;
public sealed class ComputeCurveTemplateProvider : NodeViewModelTemplateProvider
{
    public override string Name => "ComputeCurve";
    public override bool MatchNode(NodeViewModel node) => CurveEditorViewModel.Supports(node.Type);
}
