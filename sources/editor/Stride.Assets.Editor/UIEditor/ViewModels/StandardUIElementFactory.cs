// Copyright (c) .NET Foundation and Contributors (https://dotnetfoundation.org/ & https://stride3d.net)
// Distributed under the MIT license. See the LICENSE.md file in the project root for more information.

using System;
using Stride.Assets.UI;
using Stride.Core.Assets;
using Stride.UI;
using Stride.UI.Controls;
using Stride.UI.Panels;

namespace Stride.Assets.Editor.UIEditor.ViewModels;

public sealed class StandardUIElementFactory : IUIElementFactory
{
    private readonly Func<UIElement> createFunc;

    public StandardUIElementFactory(string name, string category, Type type, Func<UIElement> createFunc)
    {
        Name = name;
        Category = category;
        Type = type;
        this.createFunc = createFunc;
    }

    public string Name { get; }

    public string Category { get; }

    public Type Type { get; }

    public AssetCompositeHierarchyData<UIElementDesign, UIElement> Create(UIAssetBase targetAsset)
    {
        var element = createFunc();
        var design = new UIElementDesign(element);
        var hierarchy = new AssetCompositeHierarchyData<UIElementDesign, UIElement>();
        hierarchy.Parts.Add(element.Id, design);
        hierarchy.RootParts.Add(element);
        return hierarchy;
    }

    public static StandardUIElementFactory[] CreateStandardFactories()
    {
        return
        [
            // Panels
            new StandardUIElementFactory("Canvas", "Panel", typeof(Canvas), () => new Canvas()),
            new StandardUIElementFactory("Grid", "Panel", typeof(Grid), () => new Grid()),
            new StandardUIElementFactory("StackPanel", "Panel", typeof(StackPanel), () => new StackPanel()),
            new StandardUIElementFactory("UniformGrid", "Panel", typeof(UniformGrid), () => new UniformGrid()),

            // Controls
            new StandardUIElementFactory("Border", "Control", typeof(Border), () => new Border()),
            new StandardUIElementFactory("Button", "Control", typeof(Button), () => new Button { Content = new TextBlock { Text = "Button" } }),
            new StandardUIElementFactory("EditText", "Control", typeof(EditText), () => new EditText()),
            new StandardUIElementFactory("ImageElement", "Control", typeof(ImageElement), () => new ImageElement()),
            new StandardUIElementFactory("ModalElement", "Control", typeof(ModalElement), () => new ModalElement()),
            new StandardUIElementFactory("ScrollViewer", "Control", typeof(ScrollViewer), () => new ScrollViewer()),
            new StandardUIElementFactory("Slider", "Control", typeof(Slider), () => new Slider()),
            new StandardUIElementFactory("TextBlock", "Control", typeof(TextBlock), () => new TextBlock { Text = "TextBlock" }),
            new StandardUIElementFactory("ToggleButton", "Control", typeof(ToggleButton), () => new ToggleButton()),
        ];
    }
}

