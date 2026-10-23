// Copyright (c) .NET Foundation and Contributors (https://dotnetfoundation.org/ & https://stride3d.net) and Silicon Studio Corp. (https://www.siliconstudio.co.jp)
// Distributed under the MIT license. See the LICENSE.md file in the project root for more information.

using Stride.Core.Assets.Editor.Services;
using Stride.Core.Assets.Editor.ViewModels;
using Stride.Core.Assets.Presentation.ViewModels;
using Stride.Core.Extensions;
using Stride.Core.Presentation.Quantum;
using Stride.Core.Presentation.Quantum.Presenters;

namespace Stride.Core.Assets.Editor.Quantum.NodePresenters.Commands;

public class PickupAssetCommand : ChangeValueWithPickerCommandBase
{
    /// <summary>
    /// The name of this command.
    /// </summary>
    public const string CommandName = "PickupAsset";
    /// <summary>
    /// The current session.
    /// </summary>
    protected readonly SessionViewModel Session;

    /// <summary>
    /// Initializes a new instance of the <see cref="PickupAssetCommand"/> class.
    /// </summary>
    /// <param name="session">The current session.</param>
    public PickupAssetCommand(SessionViewModel session)
    {
        Session = session;
    }

    /// <inheritdoc/>
    public override string Name => CommandName;

    /// <inheritdoc/>
    public override CombineMode CombineMode => CombineMode.CombineOnlyForAll;

    /// <inheritdoc/>
    public override bool CanAttach(INodePresenter nodePresenter)
    {
        if (nodePresenter.Descriptor == null) return false;
        var type = nodePresenter.Descriptor.GetInnerCollectionType();
        return AssetRegistry.CanBeAssignedToContentTypes(type, checkIsUrlType: true);
    }

    /// <inheritdoc/>
    public override bool CanExecute(IReadOnlyCollection<INodePresenter> nodePresenters, object? parameter) =>
        parameter is Type type && AssetRegistry.CanBeAssignedToContentTypes(type, checkIsUrlType: true)
        && nodePresenters.Count > 0 && nodePresenters.All(node => !node.IsReadOnly && node.Type == type);

    protected override async Task<PickerResult> ShowPicker(IReadOnlyCollection<INodePresenter> nodePresenters, object currentValue, object? parameter)
    {
        if (!CanExecute(nodePresenters, parameter) || parameter is not Type targetType) return default;
        var asset = GetCurrentTarget(currentValue);
        var picker = Session.Dispatcher.Invoke(() => new AssetPickerViewModel(Session)
        {
            Filter = candidate => FilterAsset(candidate, targetType),
            InitialAsset = asset,
            InitialLocation = asset?.Directory ?? Session.AssetCollection.SelectedAssets.FirstOrDefault()?.Directory,
            AllowMultiSelection = false,
        });
        picker.AcceptedTypes.AddRange(GetAssetTypes(targetType));
        try
        {
            var selection = await Session.ServiceProvider.Get<IEditorDialogService>().ShowAssetPickerAsync(picker);
            var selected = selection.FirstOrDefault();
            return new PickerResult
            {
                ProcessChange = selected != null && picker.IsAccepted(selected) && CanExecute(nodePresenters, parameter),
                NewValue = selected,
            };
        }
        finally { Session.Dispatcher.Invoke(picker.Destroy); }
    }

    protected override void ExecuteSync(INodePresenter nodePresenter, object? parameter, object? preExecuteResult)
    {
        // Cancellation must not write even the original value back into a combined selection.
        if (preExecuteResult is PickerResult { ProcessChange: true, NewValue: AssetViewModel selected } && !nodePresenter.IsReadOnly
            && GetCurrentTarget(nodePresenter.Value)?.Id != selected.Id)
            base.ExecuteSync(nodePresenter, parameter, preExecuteResult);
    }

    /// <inheritdoc/>
    protected override object ConvertPickerValue(object newValue, object? parameter)
    {
        var targetType = (Type)parameter!;
        return CreateReference((AssetViewModel)newValue, targetType)!;
    }

    /// <summary>
    /// Filters assets to display in the picker.
    /// </summary>
    /// <param name="asset">The asset to filter.</param>
    /// <param name="referenceType">The type of reference this command is targeting.</param>
    /// <returns>True if this asset should be displayed, False otherwise.</returns>
    protected virtual bool FilterAsset(AssetViewModel asset, Type referenceType)
    {
        return true;
    }

    protected virtual IEnumerable<Type> GetAssetTypes(Type contentType)
    {
        if (AssetRegistry.CanPropertyHandleAssets(contentType, out var types))
            return types;
        return [];
    }

    protected virtual AssetViewModel? GetCurrentTarget(object? currentValue)
    {
        return currentValue == null ? null : ContentReferenceHelper.GetReferenceTarget(Session, currentValue);
    }

    protected virtual object? CreateReference(AssetViewModel asset, Type referenceType)
    {
        return ContentReferenceHelper.CreateReference(asset, referenceType);
    }
}
