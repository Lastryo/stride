// Copyright (c) .NET Foundation and Contributors (https://dotnetfoundation.org/ & https://stride3d.net)
// Distributed under the MIT license. See the LICENSE.md file in the project root for more information.
using Stride.Core.Presentation.Quantum.Presenters;
namespace Stride.Assets.Editor.CurveEditor;
public sealed class EditCurveCommand(Action<object, string> open) : SyncNodePresenterCommandBase
{
    public override string Name => "EditCurve";
    public override bool CanAttach(INodePresenter nodePresenter) => CurveEditorViewModel.Supports(nodePresenter.Type);
    public override bool CanExecute(IReadOnlyCollection<INodePresenter> nodes, object? parameter) => nodes.All(n => n.Value != null && !n.IsReadOnly);
    protected override void ExecuteSync(INodePresenter nodePresenter, object? parameter, object? preExecuteResult)
    { if (nodePresenter.Value is { } curve) open(curve, nodePresenter.Name); }
}
