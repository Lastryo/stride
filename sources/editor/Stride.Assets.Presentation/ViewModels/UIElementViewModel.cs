// Copyright (c) .NET Foundation and Contributors (https://dotnetfoundation.org/ & https://stride3d.net) and Silicon Studio Corp. (https://www.siliconstudio.co.jp)
// Distributed under the MIT license. See the LICENSE.md file in the project root for more information.

using System;
using System.Collections.Generic;
using Stride.Assets.UI;
using Stride.Core;
using Stride.Core.Assets;
using Stride.Core.Assets.Presentation.Components.Properties;
using Stride.Core.Assets.Presentation.ViewModels;
using Stride.Core.Mathematics;
using Stride.Core.Presentation.Commands;
using Stride.Core.Presentation.Quantum;
using Stride.Core.Presentation.Services;
using Stride.Core.Quantum;
using Stride.UI;
using Stride.UI.Controls;
using Stride.UI.Panels;

namespace Stride.Assets.Presentation.ViewModels;

public class UIElementViewModel : UIHierarchyItemViewModel, IAssetPropertyProviderViewModel
{
    private readonly MemberGraphNodeBinding<string> nameNodeBinding;
    private readonly MemberGraphNodeBinding<BasePart> baseNodeBinding;
    private bool isEditing;

    public UIElementViewModel(UIBaseViewModel asset, UIElementDesign elementDesign)
        : base(asset, GetOrCreateChildPartDesigns((UIAssetBase)asset.Asset, elementDesign))
    {
        UIElementDesign = elementDesign;
        ElementType = AssetSideUIElement.GetType();

        var assetNode = asset.Session.AssetNodeContainer.GetOrCreateNode(elementDesign.UIElement);
        nameNodeBinding = new MemberGraphNodeBinding<string>(assetNode[nameof(UIElement.Name)], nameof(Name), OnPropertyChanging, OnPropertyChanged, ServiceProvider.TryGet<IUndoRedoService>());

        var designNode = asset.Session.AssetNodeContainer.GetOrCreateNode(elementDesign);
        baseNodeBinding = new MemberGraphNodeBinding<BasePart>(designNode[nameof(UIElementDesign.Base)], nameof(SourceLibrary), OnPropertyChanging, _ => OnPropertyChanged(nameof(SourceLibrary), nameof(Status)), null);

        RenameCommand = new AnonymousCommand(ServiceProvider, () => IsEditing = true) { IsEnabled = asset.IsEditable };
        PanelCommand = new AnonymousCommand<PanelCommandMode>(ServiceProvider, ExecutePanelCommand) { IsEnabled = asset.IsEditable };
        DependentProperties.Add(nameof(Name), new[] { nameof(ShowDisplayName) });
        DependentProperties.Add(nameof(IsEditing), new[] { nameof(ShowDisplayName) });
    }

    public UIElement AssetSideUIElement => UIElementDesign.UIElement;

    public Type ElementType { get; }

    public override AbsoluteId Id => new(Asset.Id, AssetSideUIElement.Id);

    public override string? Name
    {
        get => nameNodeBinding.Value;
        set { if (Asset.IsEditable) nameNodeBinding.Value = value; }
    }

    public bool IsEditing
    {
        get => isEditing;
        set => SetValue(ref isEditing, value);
    }

    public bool ShowDisplayName => !IsEditing && !string.IsNullOrWhiteSpace(Name);

    public ICommandBase RenameCommand { get; }

    public ICommandBase PanelCommand { get; }

    public bool CanDuplicate() => Parent is UILibraryRootViewModel or UIElementViewModel { AssetSideUIElement: Panel };

    public UILibraryViewModel? SourceLibrary => baseNodeBinding.Value is { } source ? Asset.Session.GetAssetById(source.BasePartAsset.Id) as UILibraryViewModel : null;

    /// <summary>The element type and its local or inherited status.</summary>
    public string Status => UIElementDesign.Base == null ? $"{ElementType.Name} — Local element"
        : SourceLibrary is { } library ? $"{ElementType.Name} — Library: {library.Url}"
        : $"{ElementType.Name} — Missing library: {UIElementDesign.Base.BasePartAsset.Location}";

    public bool IsParentCanvas => Parent is UIElementViewModel p && p.AssetSideUIElement is Canvas;
    public bool IsParentGrid => Parent is UIElementViewModel p && p.AssetSideUIElement is GridBase;
    public bool IsParentStackPanel => Parent is UIElementViewModel p && p.AssetSideUIElement is StackPanel;
    public bool IsPanel => AssetSideUIElement is Panel;

    public void ExecutePanelCommand(PanelCommandMode mode)
    {
        if (Parent is not UIElementViewModel parentViewModel || parentViewModel.AssetSideUIElement is not Panel parentPanel)
            return;

        var undoService = ServiceProvider.TryGet<IUndoRedoService>();
        using var transaction = undoService?.CreateTransaction();

        if (parentPanel is Canvas canvas)
        {
            var pinOrigin = AssetSideUIElement.DependencyProperties.Get(Canvas.PinOriginPropertyKey);
            switch (mode)
            {
                case PanelCommandMode.PinTopLeft: pinOrigin.X = 0.0f; pinOrigin.Y = 0.0f; break;
                case PanelCommandMode.PinTop: pinOrigin.X = 0.5f; pinOrigin.Y = 0.0f; break;
                case PanelCommandMode.PinTopRight: pinOrigin.X = 1.0f; pinOrigin.Y = 0.0f; break;
                case PanelCommandMode.PinLeft: pinOrigin.X = 0.0f; pinOrigin.Y = 0.5f; break;
                case PanelCommandMode.PinCenter: pinOrigin.X = 0.5f; pinOrigin.Y = 0.5f; break;
                case PanelCommandMode.PinRight: pinOrigin.X = 1.0f; pinOrigin.Y = 0.5f; break;
                case PanelCommandMode.PinBottomLeft: pinOrigin.X = 0.0f; pinOrigin.Y = 1.0f; break;
                case PanelCommandMode.PinBottom: pinOrigin.X = 0.5f; pinOrigin.Y = 1.0f; break;
                case PanelCommandMode.PinBottomRight: pinOrigin.X = 1.0f; pinOrigin.Y = 1.0f; break;
                case PanelCommandMode.PinFront: pinOrigin.Z = 1.0f; break;
                case PanelCommandMode.PinMiddle: pinOrigin.Z = 0.5f; break;
                case PanelCommandMode.PinBack: pinOrigin.Z = 0.0f; break;
            }
            AssetSideUIElement.DependencyProperties.Set(Canvas.PinOriginPropertyKey, pinOrigin);
            if (undoService != null && transaction != null)
                undoService.SetName(transaction, $"Change Pin Origin of {Name}");
        }
        else if (parentPanel is GridBase)
        {
            PropertyKey<int>? propertyKey = mode switch
            {
                PanelCommandMode.MoveUp or PanelCommandMode.MoveDown => GridBase.RowPropertyKey,
                PanelCommandMode.MoveLeft or PanelCommandMode.MoveRight => GridBase.ColumnPropertyKey,
                PanelCommandMode.MoveBack or PanelCommandMode.MoveFront => GridBase.LayerPropertyKey,
                _ => null
            };
            int offset = mode switch
            {
                PanelCommandMode.MoveUp or PanelCommandMode.MoveLeft or PanelCommandMode.MoveBack => -1,
                PanelCommandMode.MoveDown or PanelCommandMode.MoveRight or PanelCommandMode.MoveFront => 1,
                _ => 0
            };
            if (propertyKey != null)
            {
                var currentValue = AssetSideUIElement.DependencyProperties.Get(propertyKey);
                var newValue = Math.Max(0, currentValue + offset);
                AssetSideUIElement.DependencyProperties.Set(propertyKey, newValue);
                if (undoService != null && transaction != null)
                    undoService.SetName(transaction, $"Move {Name}");
            }
        }
        else if (parentPanel is StackPanel stackPanel)
        {
            var collection = stackPanel.Children;
            var index = collection.IndexOf(AssetSideUIElement);
            if (index >= 0)
            {
                var newIndex = mode switch
                {
                    PanelCommandMode.MoveUp => index - 1,
                    PanelCommandMode.MoveDown => index + 1,
                    _ => index
                };
                if (newIndex >= 0 && newIndex < collection.Count && newIndex != index)
                {
                    var node = Asset.Session.AssetNodeContainer.GetOrCreateNode(stackPanel)[nameof(Panel.Children)].Target!;
                    node.Remove(AssetSideUIElement, new NodeIndex(index));
                    node.Add(AssetSideUIElement, new NodeIndex(newIndex));
                    if (undoService != null && transaction != null)
                        undoService.SetName(transaction, $"Move {Name}");
                }
            }
        }
    }

    public override void Destroy()
    {
        EnsureNotDestroyed(nameof(UIElementViewModel));
        nameNodeBinding.Dispose();
        baseNodeBinding.Dispose();
        base.Destroy();
    }

    /// <inheritdoc />
    public override GraphNodePath GetNodePath()
    {
        var node = new GraphNodePath(GetNode());
        node.PushMember(nameof(UIAsset.Hierarchy));
        node.PushTarget();
        node.PushMember(nameof(UIAsset.Hierarchy.Parts));
        node.PushTarget();
        node.PushIndex(new NodeIndex(Id.ObjectId));
        node.PushMember(nameof(UIElementDesign.UIElement));
        node.PushTarget();
        return node;
    }

    public UIElementDesign UIElementDesign { get; }

    AssetViewModel IAssetPropertyProviderViewModel.RelatedAsset => Asset;

    bool IPropertyProviderViewModel.CanProvidePropertiesViewModel => true;

    private static IEnumerable<UIElementDesign> GetOrCreateChildPartDesigns(UIAssetBase asset, UIElementDesign elementDesign)
    {
        switch (elementDesign.UIElement)
        {
            case ContentControl control:                
                if (control.Content != null)
                {
                    if (!asset.Hierarchy.Parts.TryGetValue(control.Content.Id, out var partDesign))
                    {
                        partDesign = new UIElementDesign(control.Content);
                    }
                    if (control.Content != partDesign.UIElement) throw new InvalidOperationException();
                    yield return partDesign;
                }
                break;

            case Panel panel:
                foreach (var child in panel.Children)
                {
                    if (!asset.Hierarchy.Parts.TryGetValue(child.Id, out var childDesign))
                    {
                        childDesign = new UIElementDesign(child);
                    }
                    if (child != childDesign.UIElement) throw new InvalidOperationException();
                    yield return childDesign;
                }
                break;
        }
    }

    public UIElementViewModel GetRootElementViewModel()
    {
        UIElementViewModel root = this;
        var source = this;
        while (source != null)
        {
            root = source;
            source = source.Parent as UIElementViewModel;
        }
        return root;
    }

    GraphNodePath IAssetPropertyProviderViewModel.GetAbsolutePathToRootNode()
    {
        return GetNodePath();
    }

    IObjectNode IPropertyProviderViewModel.GetRootNode()
    {
        return Asset.Session.AssetNodeContainer.GetOrCreateNode(AssetSideUIElement);
    }

    bool IPropertyProviderViewModel.ShouldConstructMember(IMemberNode member)
    {
        if (typeof(PropertyContainerClass).IsAssignableFrom(member.Type))
        {
            // Do not show property container and attached properties in the property grid.
            return false;
        }
        var assetPropertyProvider = (IPropertyProviderViewModel)Asset;
        return assetPropertyProvider.ShouldConstructMember(member);
    }

    bool IPropertyProviderViewModel.ShouldConstructItem(IObjectNode collection, NodeIndex index) => ((IPropertyProviderViewModel)Asset).ShouldConstructItem(collection, index);
}
