// Copyright (c) .NET Foundation and Contributors (https://dotnetfoundation.org/ & https://stride3d.net)
// Distributed under the MIT license. See the LICENSE.md file in the project root for more information.

using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Stride.Core.Assets.Editor.ViewModels;
using Stride.Core.Assets.Presentation.ViewModels;
using Stride.Core.Presentation.Dirtiables;
using Stride.GameStudio.Avalonia.Controls;
using Stride.GameStudio.Avalonia.Converters;
using Stride.GameStudio.Avalonia.Views;

internal static class EditorAssetCardSmoke
{
    public static async Task Check(SessionViewModel session, Window owner)
    {
        var converter = new NameBreakingLine();
        foreach (var (name, expected) in new[] { ("GameSettings", "Game\u200BSettings"), ("Model2", "Model\u200B2"),
                     ("UI2", "UI\u200B2"), ("my_asset", "my_\u200Basset"), ("lowercase", "lowercase") })
            if (!Equals(converter.Convert(name, typeof(string), null, CultureInfo.InvariantCulture), expected))
                throw new Exception($"Unexpected WPF name wrapping for {name}.");

        var panel = new AssetTilePanel();
        for (var i = 0; i < 5; i++) panel.Children.Add(new Border());
        Layout(panel, 400);
        Near(panel.Children[0].Bounds, new Rect(4, 4, 114, 160), "first slot");
        Near(panel.Children[1].Bounds, new Rect(143, 4, 114, 160), "distributed column gap");
        Near(panel.Children[3].Bounds, new Rect(4, 168, 114, 160), "second row");
        var navigation = (INavigableContainer)panel;
        if (navigation.GetControl(NavigationDirection.Right, panel.Children[0], false) != panel.Children[1]
            || navigation.GetControl(NavigationDirection.Down, panel.Children[0], false) != panel.Children[3]
            || navigation.GetControl(NavigationDirection.First, panel.Children[3], false) != panel.Children[0]
            || navigation.GetControl(NavigationDirection.Last, null, false) != panel.Children[4]
            || navigation.GetControl(NavigationDirection.Next, panel.Children[0], false) != panel.Children[1])
            throw new Exception("Tile arrow navigation did not follow the visual rows and columns.");
        Layout(panel, 1000);
        Near(panel.Children[1].Bounds, new Rect(154, 4, 114, 160), "maximum column gap");
        Layout(panel, 80);
        Near(panel.Children[1].Bounds, new Rect(4, 168, 114, 160), "narrow panel");
        panel.ThumbnailSize = 68;
        Layout(panel, 300);
        Near(panel.Children[3].Bounds, new Rect(4, 140, 86, 132), "resized thumbnail slot");
        panel.Children.Clear();
        Layout(panel, 300);
        Console.WriteLine("PASS: WPF name breaks, tile slots, wrapping, bounded column gaps and empty lists.");

        var assets = session.AllAssets.Take(12).ToArray();
        if (assets.Length < 3) throw new Exception("Use a project with at least three assets.");
        var list = new ListBox
        {
            Classes = { "studioAssetList" },
            ItemsSource = assets,
            ItemsPanel = new FuncTemplate<Panel?>(() => new AssetTilePanel()),
            ItemTemplate = new FuncDataTemplate<AssetViewModel>((_, _) => new AssetCard()),
            SelectionMode = SelectionMode.Multiple,
        };
        var surface = new UserControl { Classes = { "editorShell" }, Content = list };
        var window = new Window { Title = "Asset card parity check", Content = surface, Width = 640, Height = 540 };
        window.Show(owner);
        try
        {
            foreach (var width in new[] { 320, 640 })
            {
                window.Width = width;
                await Task.Delay(200);
                await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Background);
                var cards = list.GetVisualDescendants().OfType<AssetCard>().ToArray();
                if (cards.Length != assets.Length) throw new Exception("Some asset cards were not generated.");
                foreach (var card in cards)
                {
                    var thumbnail = card.FindControl<ThumbnailImage>("Thumbnail")!;
                    Near(thumbnail.Bounds.Size.Width, 96, "thumbnail width");
                    Near(thumbnail.Bounds.Size.Height, 96, "thumbnail height");
                    Near(card.FindControl<StackPanel>("Caption")!.Bounds.Height, 48, "caption height");
                    var name = card.FindControl<TextBlock>("AssetName")!;
                    var asset = (AssetViewModel)card.DataContext!;
                    if (!Equals(name.Text, converter.Convert(asset.Name, typeof(string), null, CultureInfo.InvariantCulture)))
                        throw new Exception("Asset caption did not use the WPF word breaks.");
                }
                list.SelectedItem = assets[0];
                await Task.Delay(100);
                var item = (ListBoxItem)list.ContainerFromItem(assets[0])!;
                if (item.Background is not ISolidColorBrush { Color: var color } || color != Color.Parse("#007ACC"))
                    throw new Exception("Selection did not use the WPF background.");
                Near(item.Bounds.Width, 114, "selected tile width");
                Near(item.Bounds.Height, 160, "selected tile height");
                var type = item.GetVisualDescendants().OfType<TextBlock>().Single(text => text.FontSize == 10);
                Near(type.Opacity, 0.5, "selected type opacity");
                var hovered = (ListBoxItem)list.ContainerFromItem(assets[1])!;
                ((IPseudoClasses)hovered.Classes).Add(":pointerover");
                if (hovered.Background is not ISolidColorBrush hoverBrush || hoverBrush.Color != Color.Parse("#3E3E40"))
                    throw new Exception("Hover did not use the WPF background.");
                ((IPseudoClasses)hovered.Classes).Remove(":pointerover");
                var wasDirty = assets[0].IsDirty;
                try
                {
                    ((IDirtiable)assets[0]).UpdateDirtiness(!wasDirty);
                    await Task.Delay(50);
                    var marker = item.GetVisualDescendants().OfType<AssetCard>().Single().FindControl<TextBlock>("DirtyMarker")!;
                    if (marker.IsVisible != !wasDirty) throw new Exception("The dirty marker did not follow the asset state.");
                }
                finally { ((IDirtiable)assets[0]).UpdateDirtiness(wasDirty); }
                var bitmapSize = PixelSize.FromSize(window.ClientSize, window.RenderScaling);
                using var bitmap = new RenderTargetBitmap(bitmapSize, new Vector(96 * window.RenderScaling, 96 * window.RenderScaling));
                bitmap.Render(surface);
                bitmap.Save(Path.Combine(Path.GetTempPath(), $"stride-asset-cards-{width}.png"));
            }
        }
        finally { window.Close(); }
        Console.WriteLine("PASS: real asset captions, square thumbnails, fixed captions, WPF selection/hover, live dirty markers and screenshots at 320/640 DIP.");
    }

    private static void Layout(AssetTilePanel panel, double width)
    {
        panel.Measure(new Size(width, double.PositiveInfinity));
        panel.Arrange(new Rect(0, 0, width, panel.DesiredSize.Height));
    }

    private static void Near(Rect actual, Rect expected, string label)
    {
        Near(actual.X, expected.X, label + " X");
        Near(actual.Y, expected.Y, label + " Y");
        Near(actual.Width, expected.Width, label + " width");
        Near(actual.Height, expected.Height, label + " height");
    }

    private static void Near(double actual, double expected, string label)
    {
        if (Math.Abs(actual - expected) > 0.1) throw new Exception($"{label}: expected {expected}, got {actual}.");
    }
}
