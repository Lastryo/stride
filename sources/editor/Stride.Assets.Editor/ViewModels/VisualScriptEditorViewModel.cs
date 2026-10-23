// Copyright (c) .NET Foundation and Contributors (https://dotnetfoundation.org/ & https://stride3d.net)
// Distributed under the MIT license. See the LICENSE.md file in the project root for more information.
using System.Collections.ObjectModel;
using Stride.Assets.Editor.ScriptEditor;
using Microsoft.CodeAnalysis;
using Stride.Assets.Presentation.ViewModels;
using Stride.Assets.Scripts;
using Stride.Core.Assets.Editor.Annotations;
using Stride.Core.Assets.Editor.ViewModels;
using Stride.Core.Assets.Presentation.ViewModels;
using Stride.Core.Assets.Presentation.Components.Properties;
using Stride.Core.Presentation.Collections;
using Stride.Core.Presentation.Commands;
using Stride.Core.Presentation.Quantum;
using Stride.Core.Presentation.Services;
using Stride.Core.Presentation.ViewModels;
using Stride.Core.Quantum;
using Stride.Core.Mathematics;
using Stride.Core.Reflection;
using Stride.Core.Diagnostics;
using Stride.Core.Assets;

namespace Stride.Assets.Editor.ViewModels;

[AssetEditorViewModel<VisualScriptViewModel>]
public sealed partial class VisualScriptEditorViewModel : AssetEditorViewModel<VisualScriptViewModel>
{
    private VisualScriptMethodViewModel? selectedMethod;
    private bool refreshing;
    private readonly LoggerResult logger = new("Visual script");
    public IUndoRedoService UndoRedoService => ServiceProvider.Get<IUndoRedoService>();
    public ObservableList<VisualScriptSymbolViewModel> Properties { get; } = new();
    public ObservableList<VisualScriptMethodViewModel> Methods { get; } = new();
    public ObservableCollection<VisualScriptBlockViewModel> SelectedBlocks { get; } = new();
    public ObservableCollection<Link> SelectedLinks { get; } = new();
    public ObservableList<VisualScriptSymbolViewModel> SelectedProperties { get; } = new();
    public ObservableList<VisualScriptSymbolViewModel> SelectedParameters { get; } = new();
    public IReadOnlyList<VisualScriptBlockTemplate> BlockTemplates { get; }
    public ICommandBase AddNewPropertyCommand { get; }
    public ICommandBase RemoveSelectedPropertiesCommand { get; }
    public ICommandBase AddNewMethodCommand { get; }
    public ICommandBase RemoveSelectedMethodCommand { get; }
    public ICommandBase AddNewParameterCommand { get; }
    public ICommandBase RemoveSelectedParametersCommand { get; }
    public ICommandBase DeleteSelectionCommand { get; }
    public event EventHandler? GraphChanged;
    public string BaseType { get => Asset.Asset.BaseType ?? ""; set => Update(Asset.Asset, nameof(VisualScriptAsset.BaseType), value); }
    public VisualScriptMethodViewModel? SelectedMethod
    {
        get => selectedMethod;
        set
        {
            if (!SetValue(ref selectedMethod, value)) return;
            SelectedBlocks.Clear(); SelectedLinks.Clear(); SelectedParameters.Clear();
            GraphChanged?.Invoke(this, EventArgs.Empty);
        }
    }
    public VisualScriptEditorViewModel(VisualScriptViewModel asset) : base(asset)
    {
        AddNewPropertyCommand = new AnonymousCommand(ServiceProvider, () => Change("Add variable", () => Node(Asset.Asset)[nameof(VisualScriptAsset.Properties)].Target.Add(new Property("bool", UniqueName("Member", Asset.Asset.Properties.Select(p => p.Name))))));
        RemoveSelectedPropertiesCommand = new AnonymousCommand(ServiceProvider, () => Change("Delete variables", () =>
        {
            foreach (var item in SelectedProperties.ToArray()) RemoveSymbol(item.Symbol);
        }));
        AddNewMethodCommand = new AnonymousCommand<IMethodSymbol?>(ServiceProvider, symbol => AddMethod(symbol));
        RemoveSelectedMethodCommand = new AnonymousCommand(ServiceProvider, () =>
        {
            if (SelectedMethod is { } method) Change("Delete function", () => Node(Asset.Asset)[nameof(VisualScriptAsset.Methods)].Target.Remove(method.Method, new NodeIndex(Asset.Asset.Methods.IndexOf(method.Method))));
        });
        AddNewParameterCommand = new AnonymousCommand(ServiceProvider, () =>
        {
            if (SelectedMethod is { } method) Change("Add parameter", () => Node(method.Method)[nameof(Method.Parameters)].Target.Add(new Parameter("bool", UniqueName("variable", method.Method.Parameters.Select(p => p.Name)))));
        });
        RemoveSelectedParametersCommand = new AnonymousCommand(ServiceProvider, () => Change("Delete parameters", () =>
        {
            foreach (var item in SelectedParameters.ToArray()) RemoveSymbol(item.Symbol);
        }));
        DeleteSelectionCommand = new AnonymousCommand(ServiceProvider, DeleteSelection);
        BlockTemplates = AssemblyRegistry.FindAll().SelectMany(a => a.GetTypes())
            .Where(t => !t.IsAbstract && typeof(Block).IsAssignableFrom(t) && t.GetConstructor(Type.EmptyTypes) != null)
            .Where(t => !t.IsDefined(typeof(ObsoleteAttribute), true) && !t.IsDefined(typeof(Stride.Core.Annotations.NonInstantiableAttribute), true))
            .Select(t => new VisualScriptBlockTemplate(t, TypeDescriptorFactory.Default.AttributeRegistry.GetAttribute<Stride.Core.DisplayAttribute>(t)?.Category ?? "Misc"))
            .OrderBy(t => t.Category).ThenBy(t => t.Name).ToArray();
        foreach (var command in new[] { AddNewPropertyCommand, RemoveSelectedPropertiesCommand, AddNewMethodCommand, RemoveSelectedMethodCommand, AddNewParameterCommand, RemoveSelectedParametersCommand, DeleteSelectionCommand }) command.IsEnabled = asset.IsEditable;
        RepairSlotReferences();
        Session.OutputLog.AddLogger(logger);
        Session.AssetPropertiesChanged += AssetChanged;
        ScriptWorkspace.For(Session).SourceChanged += SourceChanged;
        if (Session.HotReload != null) Session.HotReload.Reloaded += ProjectReloaded;
        SelectedBlocks.CollectionChanged += SelectionChanged;
        Refresh();
        AnalysisTask = AnalyzeAsync();
    }
    public Task AnalysisTask { get; private set; } = Task.CompletedTask;
    private void RepairSlotReferences()
    {
        if (!Asset.IsEditable) return;
        // Older files can contain inline copies of link endpoints instead of slot references.
        using var transaction = UndoRedoService.CreateTransaction();
        foreach (var method in Asset.Asset.Methods)
        {
            var slots = method.Blocks.Values.SelectMany(b => b.Slots).ToDictionary(s => s.Id);
            foreach (var link in method.Links.Values)
            {
                if (link.Source != null && slots.TryGetValue(link.Source.Id, out var source) && !ReferenceEquals(source, link.Source)) Node(link)[nameof(Link.Source)].Update(source);
                if (link.Target != null && slots.TryGetValue(link.Target.Id, out var target) && !ReferenceEquals(target, link.Target)) Node(link)[nameof(Link.Target)].Update(target);
            }
        }
        UndoRedoService.SetName(transaction, "Repair visual script slot references");
    }
    private static string UniqueName(string prefix, IEnumerable<string> names)
    {
        var used = names.ToHashSet(); var index = 0;
        while (used.Contains(prefix + index)) index++;
        return prefix + index;
    }
    public IObjectNode Node(object value) => Session.AssetNodeContainer.GetOrCreateNode(value);
    public void Update(object value, string member, object? content)
    {
        if (Equals(Node(value)[member].Retrieve(), content)) return;
        Change("Edit " + member, () =>
        {
            Node(value)[member].Update(content);
            if (value is Block block && compilation != null && block.GetType().GetProperty(member)?.IsDefined(typeof(RegenerateSlotsAttribute), true) == true)
                RegenerateAllSlots(block);
        });
    }
    public void Change(string name, Action change)
    {
        if (!Asset.IsEditable || IsDestroyed) return;
        using (var transaction = UndoRedoService.CreateTransaction())
        {
            change();
            UndoRedoService.SetName(transaction, name);
        }
        Refresh();
    }
    private void RemoveSymbol(Symbol symbol)
    {
        if (symbol is Property property)
            Node(Asset.Asset)[nameof(VisualScriptAsset.Properties)].Target.Remove(property, new NodeIndex(Asset.Asset.Properties.IndexOf(property)));
        else if (symbol is Parameter parameter && SelectedMethod is { } method)
            Node(method.Method)[nameof(Method.Parameters)].Target.Remove(parameter, new NodeIndex(method.Method.Parameters.IndexOf(parameter)));
    }
    public VisualScriptMethodViewModel AddMethod(IMethodSymbol? symbol = null)
    {
        var method = new Method(symbol?.Name ?? UniqueName("Method", Methods.Select(m => m.Name)));
        if (symbol != null)
        {
            method.ReturnType = symbol.ReturnType.ToDisplayString();
            method.Accessibility = symbol.DeclaredAccessibility switch
            {
                Microsoft.CodeAnalysis.Accessibility.Public => Scripts.Accessibility.Public,
                Microsoft.CodeAnalysis.Accessibility.Internal => Scripts.Accessibility.Internal,
                Microsoft.CodeAnalysis.Accessibility.ProtectedOrInternal => Scripts.Accessibility.ProtectedOrInternal,
                _ => Scripts.Accessibility.Protected,
            };
            method.VirtualModifier = symbol.ContainingType.TypeKind == TypeKind.Interface ? VirtualModifier.Virtual : VirtualModifier.Override;
            foreach (var parameter in symbol.Parameters.Where(p => !p.IsThis))
                method.Parameters.Add(new Parameter(parameter.Type.ToDisplayString(), parameter.Name)
                {
                    RefKind = parameter.RefKind switch { RefKind.Out => ParameterRefKind.Out, RefKind.Ref => ParameterRefKind.Ref, _ => ParameterRefKind.None }
                });
        }
        Change("Add function", () => Node(Asset.Asset)[nameof(VisualScriptAsset.Methods)].Target.Add(method));
        return SelectedMethod = Methods.Single(m => m.Method == method);
    }
    public void AddBlock(Block block, Int2 position)
    {
        if (SelectedMethod is not { } method) return;
        block.Position = position;
        if (block.Slots.Count == 0) block.GenerateSlots(block.Slots, new SlotGeneratorContext(compilation));
        Change("Add block", () => Node(method.Method)[nameof(Method.Blocks)].Target.Add(block, new NodeIndex(block.Id)));
    }
    public void AddVariableBlock(Symbol symbol, bool setter, Int2 position)
    {
        AddBlock(setter ? new VariableSet { Name = symbol.Name } : new VariableGet { Name = symbol.Name }, position);
    }
    public bool CanConnect(Slot source, Slot target) => SelectedMethod is { } method && source != target && source.Owner != target.Owner
        && source.Direction == SlotDirection.Output && target.Direction == SlotDirection.Input && source.Kind == target.Kind
        && method.Method.Blocks.Values.Contains(source.Owner) && method.Method.Blocks.Values.Contains(target.Owner)
        && !method.Method.Links.Values.Any(l => l.Source == source && l.Target == target);
    public bool Connect(Slot source, Slot target)
    {
        if (!CanConnect(source, target)) return false;
        var link = new Link { Source = source, Target = target };
        Change("Connect slots", () => Node(SelectedMethod!.Method)[nameof(Method.Links)].Target.Add(link, new NodeIndex(link.Id)));
        return true;
    }
    public void MoveBlocks(IReadOnlyDictionary<Block, Int2> positions) => Change("Moved graph elements", () =>
    {
        foreach (var pair in positions)
            if (SelectedMethod?.Method.Blocks.Values.Contains(pair.Key) == true && pair.Key.Position != pair.Value)
                Node(pair.Key)[nameof(Block.Position)].Update(pair.Value);
    });
    private void DeleteSelection()
    {
        if (SelectedMethod is not { } method) return;
        Change("Deleting selected graph elements", () =>
        {
            var blocks = SelectedBlocks.Select(b => b.Block).ToHashSet();
            foreach (var link in method.Method.Links.Values.Where(l => SelectedLinks.Contains(l) || blocks.Contains(l.Source.Owner) || blocks.Contains(l.Target.Owner)).ToArray())
                Node(method.Method)[nameof(Method.Links)].Target.Remove(link, new NodeIndex(link.Id));
            foreach (var block in blocks) Node(method.Method)[nameof(Method.Blocks)].Target.Remove(block, new NodeIndex(block.Id));
        });
    }
    private void SourceChanged(object? sender, EventArgs e) => _ = Dispatcher.InvokeAsync(() =>
    {
        if (!IsDestroyed) AnalysisTask = AnalyzeAsync();
    });
    private async void ProjectReloaded(object? sender, EventArgs e)
    {
        try
        {
            await ScriptWorkspace.For(Session).ReloadProjectsAsync();
            await Dispatcher.InvokeAsync(() => { if (!IsDestroyed) AnalysisTask = AnalyzeAsync(); });
        }
        catch (Exception exception) { if (!IsDestroyed) logger.Error("Could not refresh the visual script project.", exception); }
    }
    private void AssetChanged(object? sender, AssetChangedEventArgs e)
    {
        if (!e.Assets.Contains(Asset)) return;
        // Session notifications run on a worker; Quantum changes and observable collections belong to the UI.
        _ = Dispatcher.InvokeAsync(() =>
        {
            if (IsDestroyed || regenerating) return;
            Refresh();
            AnalysisTask = AnalyzeAsync();
        });
    }
    public void Refresh()
    {
        if (refreshing || IsDestroyed) return;
        refreshing = true;
        try
        {
            Sync(Properties, Asset.Asset.Properties.Select(p => (Symbol)p), v => v.Symbol, s => new VisualScriptSymbolViewModel(this, s));
            Sync(Methods, Asset.Asset.Methods, v => v.Method, m => new VisualScriptMethodViewModel(this, m));
            foreach (var method in Methods) method.Refresh();
            foreach (var stale in SelectedBlocks.Where(b => SelectedMethod?.Blocks.Contains(b) != true).ToArray()) SelectedBlocks.Remove(stale);
            foreach (var stale in SelectedLinks.Where(l => SelectedMethod?.Method.Links.Values.Contains(l) != true).ToArray()) SelectedLinks.Remove(stale);
            foreach (var stale in SelectedProperties.Where(p => !Properties.Contains(p)).ToArray()) SelectedProperties.Remove(stale);
            foreach (var stale in SelectedParameters.Where(p => SelectedMethod?.Parameters.Contains(p) != true).ToArray()) SelectedParameters.Remove(stale);
            if (selectedMethod == null || !Methods.Contains(selectedMethod)) SelectedMethod = Methods.FirstOrDefault();
            foreach (var item in Properties) item.Refresh();
            OnPropertyChanged(nameof(BaseType));
            GraphChanged?.Invoke(this, EventArgs.Empty);
        }
        finally { refreshing = false; }
    }
    internal static void Sync<T, TModel>(IList<T> views, IEnumerable<TModel> values, Func<T, TModel> model, Func<TModel, T> create) where T : DispatcherViewModel
    {
        var list = values.ToArray();
        foreach (var old in views.Where(v => !list.Contains(model(v))).ToArray()) { views.Remove(old); old.Destroy(); }
        for (var i = 0; i < list.Length; i++)
        {
            var existing = views.FirstOrDefault(v => EqualityComparer<TModel>.Default.Equals(model(v), list[i]));
            if (existing == null) views.Insert(i, create(list[i]));
            else if (views.IndexOf(existing) != i) { views.Remove(existing); views.Insert(i, existing); }
        }
    }
    private async void SelectionChanged(object? sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e)
    {
        if (IsDestroyed) return;
        EditorProperties.UpdateTypeAndName(SelectedBlocks, _ => "Block", b => b.Title, "blocks");
        await EditorProperties.GenerateSelectionPropertiesAsync(SelectedBlocks);
        GraphChanged?.Invoke(this, EventArgs.Empty);
    }
    public override void Destroy()
    {
        if (IsDestroyed) return;
        analysisCancellation?.Cancel();
        Session.AssetPropertiesChanged -= AssetChanged;
        ScriptWorkspace.For(Session).SourceChanged -= SourceChanged;
        if (Session.HotReload != null) Session.HotReload.Reloaded -= ProjectReloaded;
        SelectedBlocks.CollectionChanged -= SelectionChanged;
        foreach (var method in Methods) method.Destroy();
        foreach (var property in Properties) property.Destroy();
        Session.OutputLog.RemoveLogger(logger);
        base.Destroy();
    }
    public override async Task DestroyAsync()
    {
        Destroy();
        try { await AnalysisTask; } catch (OperationCanceledException) { }
    }
}

public sealed record VisualScriptBlockTemplate(Type Type, string Category)
{
    public string Name => Type.Name;
    public override string ToString() => Name;
}

public sealed class VisualScriptSymbolViewModel(VisualScriptEditorViewModel editor, Symbol symbol) : DispatcherViewModel(editor.ServiceProvider)
{
    public Symbol Symbol => symbol;
    public string Name { get => symbol.Name; set => editor.Update(symbol, nameof(Symbol.Name), value); }
    public string Type { get => symbol.Type; set => editor.Update(symbol, nameof(Symbol.Type), value); }
    public ParameterRefKind RefKind { get => symbol is Parameter parameter ? parameter.RefKind : ParameterRefKind.None; set { if (symbol is Parameter parameter) editor.Update(parameter, nameof(Parameter.RefKind), value); } }
    public string RefPrefix => RefKind == ParameterRefKind.None ? "" : RefKind.ToString().ToLowerInvariant() + " ";
    public string DisplayName => (RefKind == ParameterRefKind.None ? "" : RefKind.ToString().ToLowerInvariant() + " ") + Name + " (" + Type + ")";
    internal void Refresh() { OnPropertyChanged(nameof(Name), nameof(Type), nameof(RefKind), nameof(RefPrefix), nameof(DisplayName)); }
}

public sealed class VisualScriptMethodViewModel(VisualScriptEditorViewModel editor, Method method) : DispatcherViewModel(editor.ServiceProvider)
{
    public Method Method => method;
    public ObservableList<VisualScriptSymbolViewModel> Parameters { get; } = new();
    public ObservableList<VisualScriptBlockViewModel> Blocks { get; } = new();
    public string Name { get => method.Name; set => editor.Update(method, nameof(Method.Name), value); }
    public string ReturnType { get => method.ReturnType; set => editor.Update(method, nameof(Method.ReturnType), value); }
    public Scripts.Accessibility Accessibility { get => method.Accessibility; set => editor.Update(method, nameof(Method.Accessibility), value); }
    public VirtualModifier VirtualModifier { get => method.VirtualModifier; set => editor.Update(method, nameof(Method.VirtualModifier), value); }
    public bool IsStatic { get => method.IsStatic; set => editor.Update(method, nameof(Method.IsStatic), value); }
    internal void Refresh()
    {
        VisualScriptEditorViewModel.Sync(Parameters, method.Parameters.Select(p => (Symbol)p), v => v.Symbol, s => new VisualScriptSymbolViewModel(editor, s));
        VisualScriptEditorViewModel.Sync(Blocks, method.Blocks.Values, v => v.Block, b => new VisualScriptBlockViewModel(editor, this, b));
        foreach (var parameter in Parameters) parameter.Refresh();
        OnPropertyChanged(nameof(Name), nameof(ReturnType), nameof(Accessibility), nameof(VirtualModifier), nameof(IsStatic));
    }
    public override void Destroy() { foreach (var p in Parameters) p.Destroy(); foreach (var b in Blocks) b.Destroy(); base.Destroy(); }
}

public sealed class VisualScriptBlockViewModel(VisualScriptEditorViewModel editor, VisualScriptMethodViewModel method, Block block) : DispatcherViewModel(editor.ServiceProvider), IAssetPropertyProviderViewModel
{
    public VisualScriptEditorViewModel Editor => editor;
    public VisualScriptMethodViewModel Method => method;
    public Block Block => block;
    public string Title => block.Title ?? block.GetType().Name;
    public bool CanProvidePropertiesViewModel => true;
    public AssetViewModel RelatedAsset => editor.Asset;
    public IObjectNode GetRootNode() => editor.Node(block);
    public bool ShouldConstructMember(IMemberNode member) => true;
    public bool ShouldConstructItem(IObjectNode collection, NodeIndex index) => true;
    public GraphNodePath GetAbsolutePathToRootNode()
    {
        var path = new GraphNodePath(editor.Node(editor.Asset.Asset));
        path.PushMember(nameof(VisualScriptAsset.Methods)); path.PushTarget(); path.PushIndex(new NodeIndex(editor.Asset.Asset.Methods.IndexOf(method.Method))); path.PushTarget();
        path.PushMember(nameof(Method.Blocks)); path.PushTarget(); path.PushIndex(new NodeIndex(block.Id)); path.PushTarget();
        return path;
    }
}
