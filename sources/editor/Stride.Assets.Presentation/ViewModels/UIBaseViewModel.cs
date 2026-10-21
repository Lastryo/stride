// Copyright (c) .NET Foundation and Contributors (https://dotnetfoundation.org/ & https://stride3d.net) and Silicon Studio Corp. (https://www.siliconstudio.co.jp)
// Distributed under the MIT license. See the LICENSE.md file in the project root for more information.

using Stride.Assets.UI;
using Stride.Core.Assets;
using Stride.Core.Assets.Presentation.ViewModels;
using Stride.Core.Quantum;
using Stride.Core.Transactions;
using Stride.UI;
using Stride.UI.Controls;
using Stride.UI.Panels;

namespace Stride.Assets.Presentation.ViewModels;

/// <summary>
/// Base view model for <see cref="UIAssetBase"/>.
/// </summary>
public abstract class UIBaseViewModel : AssetCompositeHierarchyViewModel<UIElementDesign, UIElement>
{
    protected UIBaseViewModel(ConstructorParameters parameters)
        : base(parameters)
    {
    }

    /// <inheritdoc />
    public override void Initialize()
    {
        base.Initialize();
        if (UndoRedoService != null)
        {
            UndoRedoService.Undone += HistoryChanged;
            UndoRedoService.Redone += HistoryChanged;
        }
    }

    /// <inheritdoc />
    public override void Destroy()
    {
        if (UndoRedoService != null)
        {
            UndoRedoService.Undone -= HistoryChanged;
            UndoRedoService.Redone -= HistoryChanged;
        }
        base.Destroy();
    }

    private void HistoryChanged(object? sender, TransactionEventArgs e)
    {
        // Restoring Base metadata must also restore live inheritance, even with the editor closed.
        if (!IsDeleted && (AssetHierarchyPropertyGraph.GetBasePartAssets().Count > 0
            || ((UIAssetBase)Asset).Hierarchy.Parts.Values.Any(part => part.Base != null)))
            AssetHierarchyPropertyGraph.RefreshBase();
    }

    /// <inheritdoc />
    public override UIElementViewModel CreatePartViewModel(UIElementDesign elementDesign)
    {
        return new UIElementViewModel(this, elementDesign);
    }

    public void InsertUIElement(AssetPartCollection<UIElementDesign, UIElement> newElementCollection, UIElementDesign child, UIElement? parent, int index = -1)
    {
        index = ResolveInsertionIndex(parent, index);
        AssetHierarchyPropertyGraph.AddPartToAsset(newElementCollection, child, parent, index);
    }

    private int ResolveInsertionIndex(UIElement? parent, int index)
    {
        if (index >= 0)
            return index;

        if (parent is Panel panel)
            return panel.Children.Count;

        if (parent is ContentControl)
            return 0;

        return ((UIAssetBase)Asset).Hierarchy.RootParts.Count;
    }

    /// <inheritdoc />
    protected override GraphNodePath GetPathToPropertiesRootNode()
    {
        var path = base.GetPathToPropertiesRootNode();
        path.PushMember(nameof(UIAssetBase.Design));
        path.PushTarget();
        return path;
    }

    /// <inheritdoc />
    protected override IObjectNode? GetPropertiesRootNode()
    {
        return Session.AssetNodeContainer.GetNode(((UIAssetBase)Asset).Design);
    }
}
