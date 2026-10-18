// Copyright (c) .NET Foundation and Contributors (https://dotnetfoundation.org/ & https://stride3d.net) and Silicon Studio Corp. (https://www.siliconstudio.co.jp)
// Distributed under the MIT license. See the LICENSE.md file in the project root for more information.

using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Layout;
using Avalonia.Media;
using Stride.Assets.Editor.Preview;
using Stride.Editor.Annotations;
using Stride.Editor.Avalonia.Preview.Views;

namespace Stride.Assets.Editor.Avalonia.Views.Preview;

[AssetPreviewView<ScenePreview>]
public class ScenePreviewView : StridePreviewView
{
    static ScenePreviewView()
    {
        // ScenePreview has no renderer; show the same guidance as the WPF editor.
        TemplateProperty.OverrideDefaultValue<ScenePreviewView>(new FuncControlTemplate<ScenePreviewView>((_, _) =>
            new TextBlock
            {
                Text = "Scenes can't be previewed. To see the scene, open it in the scene editor.",
                TextWrapping = TextWrapping.Wrap,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(12),
            }));
    }
}
