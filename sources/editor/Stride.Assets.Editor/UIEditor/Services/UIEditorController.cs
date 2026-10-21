// Copyright (c) .NET Foundation and Contributors (https://dotnetfoundation.org/ & https://stride3d.net) and Silicon Studio Corp. (https://www.siliconstudio.co.jp)
// Distributed under the MIT license. See the LICENSE.md file in the project root for more information.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Stride.Assets.Editor.GameEditor.Services;
using Stride.Assets.Editor.UIEditor.Adorners;
using Stride.Assets.Editor.UIEditor.Game;
using Stride.Assets.Editor.ViewModels;
using Stride.Assets.Presentation.ViewModels;
using Stride.Assets.SpriteFont;
using Stride.Assets.SpriteFont.Compiler;
using Stride.Assets.UI;
using Stride.Core;
using Stride.Core.Annotations;
using Stride.Core.Assets;
using Stride.Core.Assets.Editor.ViewModels;
using Stride.Core.Assets.Presentation.ViewModels;
using Stride.Core.Assets.Quantum;
using Stride.Core.Assets.Quantum.Visitors;
using Stride.Core.Extensions;
using Stride.Core.Mathematics;
using Stride.Core.Quantum;
using Stride.Editor.EditorGame.Game;
using Stride.Assets.Editor.UIEditor.ViewModels;
using Stride.Engine;
using Stride.Graphics.Font;
using Stride.Rendering;
using Stride.UI;
using Stride.UI.Controls;
using Stride.UI.Panels;

namespace Stride.Assets.Editor.UIEditor.Services;

/// <summary>
/// Game controller for the UI editor.
/// </summary>
public abstract class UIEditorController : AssetCompositeHierarchyEditorController<UIEditorGame, UIElementDesign, UIElement, UIHierarchyItemViewModel>
{
    public const float DesignDensity = 100.0f;

    public const string AdornerEntityName = "AdornerEntity";
    public const string AdornerRootElementName = "AdornerRoot";
    public const string DesignAreaEntityName = "DesignAreaEntity";
    public const string DesignAreaRootElementName = "DesignAreaRoot";
    public const string UIEntityName = "UIEntity";

    private readonly Lazy<Graphics.SpriteFont?> defaultFontLazy;
    private Vector3 resolution;

    public event Action<UICursorType?>? CursorChanged;

    protected UIEditorController([NotNull] AssetViewModel asset, [NotNull] UIViewportViewModel viewport)
        : base(asset, viewport, (loaded, compiler, logPath) => new UIEditorGame(loaded, compiler, logPath))
    {
        defaultFontLazy = new Lazy<Graphics.SpriteFont?>(() =>
        {
            var fontItem = SignedDistanceFieldSpriteFontFactory.Create();
            fontItem.FontType.Size = 16.0f;
            return SignedDistanceFieldFontCompiler.Compile(Game.Services.GetService<IFontFactory>(), fontItem);
        });

        var resolutionNode = Asset.Session.AssetNodeContainer.GetNode(((UIAssetBase)Asset.Asset).Design)[nameof(UIAssetBase.UIDesign.Resolution)];
        resolutionNode.ValueChanged += ResolutionChanged;
        resolution = (Vector3)resolutionNode.Retrieve()!;
    }

    public UIEditorGameAdornerService AdornerService { get; private set; } = null!;
    public UIEditorGameCameraService CameraService { get; private set; } = null!;

    public Graphics.SpriteFont? DefaultFont => defaultFontLazy.Value;

    public UIViewportViewModel Viewport => (UIViewportViewModel)base.Editor;
    public new UIEditorBaseViewModel Editor => Viewport.Owner;

    public Dictionary<Guid, UIElement> RootElements { get; } = [];

    private UIAssetBase.UIDesign? uiDesign;

    public void ChangeCursor(UICursorType? cursor)
    {
        Asset.Dispatcher.InvokeAsync(() => CursorChanged?.Invoke(cursor)).Forget();
    }

    private async void ResolutionChanged(object? sender, MemberNodeChangeEventArgs e)
    {
        resolution = Vector3.Max((Vector3)e.NewValue!, Vector3.One);
        await InvokeAsync(() =>
        {
            var size = resolution / DesignDensity;
            var uiComponent = GetEntityByName(DesignAreaEntityName)?.Get<UIComponent>();
            if (uiComponent != null)
            {
                uiComponent.Resolution = resolution;
                uiComponent.Size = size;
            }
            uiComponent = GetEntityByName(UIEntityName)?.Get<UIComponent>();
            if (uiComponent != null)
            {
                uiComponent.Resolution = resolution;
                uiComponent.Size = size;
            }
            uiComponent = GetEntityByName(AdornerEntityName)?.Get<UIComponent>();
            if (uiComponent != null)
            {
                uiComponent.Resolution = resolution * 2;
                uiComponent.Size = size * 2;
            }
            AdornerService.Refresh().Forget();
        });
    }

    public override async Task<bool> CreateScene()
    {
        if (!ConstructRootElements(out var rootElements, out uiDesign))
            return false;

        rootElements.ForEach(r => RootElements[r.Id] = r);

        var res = uiDesign!.Resolution;
        var size = res / DesignDensity;
        var rootEntity = new Entity(UIEntityName)
        {
            new UIComponent
            {
                Page = new UIPage { RootElement = rootElements.FirstOrDefault() },
                IsBillboard = false,
                IsFullScreen = false,
                Resolution = res,
                ResolutionStretch = ResolutionStretch.FixedWidthFixedHeight,
                Size = size,
            }
        };
        var designArea = new Entity(DesignAreaEntityName)
        {
            new UIComponent
            {
                Page = new UIPage
                {
                    RootElement = new Border
                    {
                        Name = DesignAreaRootElementName,
                        BackgroundColor = Color.WhiteSmoke * 0.5f,
                        BorderColor = Color.WhiteSmoke,
                        BorderThickness = Thickness.UniformCuboid(2.0f),
                    }
                },
                IsBillboard = false,
                IsFullScreen = false,
                Resolution = res,
                ResolutionStretch = ResolutionStretch.FixedWidthFixedHeight,
                Size = size,
            }
        };
        designArea.Transform.Position.Z = -10;
        var uiAdorners = new Entity(AdornerEntityName)
        {
            new UIComponent
            {
                Page = new UIPage
                {
                    RootElement = new Canvas
                    {
                        CanBeHitByUser = true,
                        Name = AdornerRootElementName,
                        Visibility = Visibility.Visible,
                    }
                },
                IsBillboard = false,
                IsFullScreen = false,
                Resolution = res * 2,
                ResolutionStretch = ResolutionStretch.FixedWidthFixedHeight,
                Size = size * 2,
            }
        };
        uiAdorners.Transform.Position.Z = 10;

        var entities = new[] { designArea, rootEntity, uiAdorners };
        await InvokeAsync(() =>
        {
            Game.InitializeContentScene();
            Game.LoadEntities(entities);
        });
        RecoveryService!.IsActive = true;
        return true;
    }

    protected virtual bool ConstructRootElements(out ICollection<UIElement> rootElements, out UIAssetBase.UIDesign? editorSettings)
    {
        var uiAsset = (UIAssetBase)AssetCloner.Clone(Asset.Asset, AssetClonerFlags.ReferenceAsNull);
        editorSettings = uiAsset.Design;
        var elements = new List<UIElement>();
        foreach (var part in uiAsset.Hierarchy.RootParts)
        {
            if (uiAsset.Hierarchy.Parts.TryGetValue(part.Id, out var elementDesign))
            {
                elements.Add(elementDesign.UIElement);
            }
        }

        rootElements = elements;
        return true;
    }

    public override Task AddPart([NotNull] UIHierarchyItemViewModel parent, UIElement assetSidePart)
    {
        EnsureAssetAccess();

        var gameSidePart = ClonePartForGameSide((UIAssetBase)parent.Asset.Asset, assetSidePart);
        var insertIndex = parent is UIElementViewModel { AssetSideUIElement: Panel assetPanel }
            ? assetPanel.Children.IndexOf(assetSidePart) : 0;
        return InvokeAsync(() =>
        {
            Logger.Debug($"Adding element {assetSidePart.Id} to game-side scene");

            var parentId = (parent as UIElementViewModel)?.Id;
            if (parentId == null)
            {
                RootElements[assetSidePart.Id] = gameSidePart;
                var uiComponent = GetUIComponent();
                if (uiComponent != null && uiComponent.Page.RootElement == null)
                {
                    uiComponent.Enabled = true;
                    uiComponent.Page.RootElement = gameSidePart;
                }
            }
            else
            {
                var parentElement = (UIElement?)FindPart(parentId.Value);
                if (parentElement == null)
                    throw new InvalidOperationException($"The given {nameof(parentId)} does not correspond to any existing part.");

                if (parentElement is Panel panel)
                {
                    GameSideNodeContainer.GetOrCreateNode(panel.Children).Add(gameSidePart, new NodeIndex(Math.Clamp(insertIndex, 0, panel.Children.Count)));
                }
                else if (parentElement is ContentControl contentControl)
                {
                    if (contentControl.Content != null)
                    {
                        throw new InvalidOperationException($"The control corresponding to the given {nameof(parentId)} is a ContentControl that already has a Content.");
                    }
                    GameSideNodeContainer.GetOrCreateNode(contentControl)[nameof(contentControl.Content)].Update(gameSidePart);
                }
            }
        });
    }

    public override Task RemovePart([NotNull] UIHierarchyItemViewModel parent, UIElement assetSidePart)
    {
        EnsureAssetAccess();

        return InvokeAsync(() =>
        {
            Logger.Debug($"Removing element {assetSidePart.Id} from game-side scene");
            var partId = new AbsoluteId(AssetId.Empty, assetSidePart.Id);
            var part = (UIElement?)FindPart(partId);
            if (part == null)
                throw new InvalidOperationException($"The given {nameof(assetSidePart.Id)} does not correspond to any existing part.");

            var parentId = (parent as UIElementViewModel)?.Id;
            if (parentId == null)
            {
                RemoveRootElement(assetSidePart.Id);
            }
            else
            {
                var parentElement = (UIElement?)FindPart(parentId.Value);
                if (parentElement == null)
                    throw new InvalidOperationException($"The given {nameof(parentId)} does not correspond to any existing part.");

                if (parentElement is Panel panel)
                {
                    var i = panel.Children.IndexOf(part);
                    GameSideNodeContainer.GetOrCreateNode(panel.Children).Remove(part, new NodeIndex(i));
                }
                else if (parentElement is ContentControl contentControl)
                {
                    GameSideNodeContainer.GetOrCreateNode(contentControl)[nameof(contentControl.Content)].Update(null);
                }
            }
        });
    }

    protected override object? FindPart(AbsoluteId elementId)
    {
        return RootElements.Keys.Select(rootId => FindElement(rootId, elementId.ObjectId)).FirstOrDefault(x => x != null);
    }

    [CanBeNull]
    public UIElement? FindElement(Guid rootId, Guid elementId)
    {
        return RootElements.TryGetValue(rootId, out var rootElement) ? FindElementDeep(rootElement, elementId) : null;
    }

    public void HideUI()
    {
        EnsureGameAccess();

        var uiComponent = GetUIComponent();
        if (uiComponent != null)
        {
            uiComponent.Enabled = false;
            uiComponent.Page.RootElement = null;
        }
    }

    public void RemoveRootElement(Guid rootId)
    {
        EnsureGameAccess();

        RootElements.Remove(rootId);
        var uiComponent = GetUIComponent();
        if (uiComponent?.Page.RootElement?.Id == rootId)
        {
            uiComponent.Enabled = false;
            uiComponent.Page.RootElement = null;
        }
    }

    public void ShowUI(Guid rootId)
    {
        EnsureGameAccess();

        var uiComponent = GetUIComponent();
        if (uiComponent != null && RootElements.TryGetValue(rootId, out var element))
        {
            uiComponent.Enabled = true;
            uiComponent.Page.RootElement = element;
        }
    }

    public override void Destroy()
    {
        EnsureNotDestroyed(nameof(UIEditorController));

        var resolutionNode = Asset.Session.AssetNodeContainer.GetNode(((UIAssetBase)Asset.Asset).Design)[nameof(UIAssetBase.UIDesign.Resolution)];
        resolutionNode.ValueChanged -= ResolutionChanged;

        base.Destroy();
    }

    public Vector3 GetMousePositionInUI()
    {
        var scenePosition = GetMousePositionInScene(false);

        return SceneToUI(scenePosition);
    }

    /// <summary>Converts a normalized viewport pointer position to UI coordinates on the game thread.</summary>
    public Vector3 GetPositionInUI(Vector2 position) => SceneToUI(Game.GetPositionInScene(position));

    /// <summary>Converts UI coordinates to a container's local top-left coordinates on the game thread.</summary>
    public Vector3 GetContainerPosition(Guid rootId, Guid parentId, Vector3 position)
    {
        var parent = FindElement(rootId, parentId);
        if (parent == null) return Vector3.Zero;
        var inverse = Matrix.Invert(parent.WorldMatrix);
        return Vector3.TransformCoordinate(position, inverse) + parent.RenderSize * 0.5f;
    }

    private Vector3 SceneToUI(Vector3 scenePosition)
    {
        var uiEntity = GetEntityByName(AdornerEntityName);
        if (uiEntity == null)
            return Vector3.Zero;

        var uiComponent = uiEntity.Get<UIComponent>();
        if (uiComponent == null)
            return Vector3.Zero;

        var worldMatrix = Matrix.Scaling(uiComponent.Size / uiComponent.Resolution) * uiEntity.Transform.WorldMatrix;
        worldMatrix.Row2 = -worldMatrix.Row2;
        worldMatrix.Row3 = -worldMatrix.Row3;

        Matrix.Invert(ref worldMatrix, out var matrixInv);
        Vector3.TransformCoordinate(ref scenePosition, ref matrixInv, out var result);
        return result;
    }

    protected override void InitializeServices(EditorGameServiceRegistry services)
    {
        base.InitializeServices(services);

        services.Add(CameraService = new UIEditorGameCameraService());
        services.Add(AdornerService = new UIEditorGameAdornerService(this));
    }

    protected override Dictionary<Guid, IIdentifiable> CollectIdentifiableObjects()
    {
        var allElements = RootElements.Values.BreadthFirst(x => x.VisualChildren);
        var definition = AssetQuantumRegistry.GetDefinition(Asset.Asset.GetType());
        var identifiableObjects = new Dictionary<Guid, IIdentifiable>();
        foreach (var entityNode in allElements.Select(x => GameSideNodeContainer.GetOrCreateNode(x)))
        {
            foreach (var identifiable in IdentifiableObjectCollector.Collect(definition!, entityNode))
            {
                identifiableObjects.Add(identifiable.Key, identifiable.Value);
            }
        }
        return identifiableObjects;
    }

    [CanBeNull]
    public Entity? GetEntityByName(string name)
    {
        return Game.ContentScene?.Entities.FirstOrDefault(e => e.Name == name);
    }

    [CanBeNull]
    private UIComponent? GetUIComponent()
    {
        return GetEntityByName(UIEntityName)?.Get<UIComponent>();
    }

    [CanBeNull]
    private static UIElement? FindElementDeep([NotNull] UIElement element, Guid elementId)
    {
        if (element.Id == elementId)
            return element;

        return element.VisualChildren.BreadthFirst(e => e.VisualChildren).FirstOrDefault(e => e.Id == elementId);
    }
}

