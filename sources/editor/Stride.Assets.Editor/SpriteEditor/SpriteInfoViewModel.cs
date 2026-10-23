// Copyright (c) .NET Foundation and Contributors (https://dotnetfoundation.org/ & https://stride3d.net)
// Distributed under the MIT license. See the LICENSE.md file in the project root for more information.

using Stride.Core;
using Stride.Assets.Editor.ViewModels;
using Stride.Assets.Sprite;
using Stride.Core.Assets.Presentation.Components.Properties;
using Stride.Core.Assets.Presentation.ViewModels;
using Stride.Core.IO;
using Stride.Core.Mathematics;
using Stride.Core.Presentation.Quantum;
using Stride.Core.Presentation.ViewModels;
using Stride.Core.Quantum;

namespace Stride.Assets.Editor.SpriteEditor;

public sealed class SpriteInfoViewModel : DispatcherViewModel, IAssetPropertyProviderViewModel
{
    private readonly SpriteSheetEditorViewModel editor;
    private readonly IObjectNode node;
    private readonly List<IDisposable> bindings = [];
    private readonly MemberGraphNodeBinding<string> name;
    private readonly MemberGraphNodeBinding<UFile> source;
    private readonly MemberGraphNodeBinding<Rectangle> region;
    private readonly MemberGraphNodeBinding<Vector2> center;
    private readonly MemberGraphNodeBinding<bool> centerFromMiddle;
    private readonly MemberGraphNodeBinding<Vector4> borders;

    public SpriteInfoViewModel(SpriteSheetEditorViewModel editor, SpriteInfo sprite) : base(editor.ServiceProvider)
    {
        this.editor = editor;
        Sprite = sprite;
        Id = new(editor.Asset.Id, Guid.NewGuid());
        node = editor.Session.AssetNodeContainer.GetOrCreateNode(sprite);
        name = Bind<string>(nameof(SpriteInfo.Name), nameof(Name));
        source = Bind<UFile>(nameof(SpriteInfo.Source), nameof(Source));
        region = Bind<Rectangle>(nameof(SpriteInfo.TextureRegion), nameof(Region));
        center = Bind<Vector2>(nameof(SpriteInfo.Center), nameof(Center));
        centerFromMiddle = Bind<bool>(nameof(SpriteInfo.CenterFromMiddle), nameof(CenterFromMiddle));
        borders = Bind<Vector4>(nameof(SpriteInfo.Borders), nameof(Borders));
        node[nameof(SpriteInfo.TextureRegion)].ValueChanged += RegionChanged;
    }

    private MemberGraphNodeBinding<T> Bind<T>(string member, string property)
    {
        var binding = new MemberGraphNodeBinding<T>(node[member], property, OnPropertyChanging, OnPropertyChanged, editor.UndoRedoService);
        bindings.Add(binding);
        return binding;
    }

    public AbsoluteId Id { get; }
    public SpriteInfo Sprite { get; }
    public string Name { get => name.Value ?? string.Empty; set => name.Value = value; }
    public UFile? Source { get => source.Value; set => source.Value = value; }
    public Rectangle Region { get => region.Value; set => region.Value = value; }
    public Vector2 Center { get => center.Value; set => center.Value = value; }
    public bool CenterFromMiddle { get => centerFromMiddle.Value; set => centerFromMiddle.Value = value; }
    public Vector4 Borders { get => borders.Value; set => borders.Value = value; }
    public bool CanProvidePropertiesViewModel => true;
    public AssetViewModel RelatedAsset => editor.Asset;
    public IObjectNode GetRootNode() => node;
    public bool ShouldConstructMember(IMemberNode member) => ((IPropertyProviderViewModel)editor.Asset).ShouldConstructMember(member);
    public bool ShouldConstructItem(IObjectNode collection, NodeIndex index) => ((IPropertyProviderViewModel)editor.Asset).ShouldConstructItem(collection, index);
    public GraphNodePath GetAbsolutePathToRootNode()
    {
        var path = new GraphNodePath(editor.Session.AssetNodeContainer.GetOrCreateNode(editor.Asset.Asset));
        path.PushMember(nameof(SpriteSheetAsset.Sprites));
        path.PushTarget();
        path.PushIndex(new NodeIndex(editor.Asset.Asset.Sprites.IndexOf(Sprite)));
        return path;
    }

    private void RegionChanged(object? sender, MemberNodeChangeEventArgs e)
    {
        if (!editor.LockBorders || editor.UndoRedoService.UndoRedoInProgress) return;
        if (e.OldValue is not Rectangle previous || e.NewValue is not Rectangle current) return;
        if (previous.Width == current.Width && previous.Height == current.Height) return;
        Borders = SpriteSheetEditorViewModel.ClampBorders(Borders + new Vector4(previous.Left - current.Left,
            previous.Top - current.Top, current.Right - previous.Right, current.Bottom - previous.Bottom), current);
    }

    public override void Destroy()
    {
        node[nameof(SpriteInfo.TextureRegion)].ValueChanged -= RegionChanged;
        foreach (var binding in bindings) binding.Dispose();
        base.Destroy();
    }
}
