// Copyright (c) .NET Foundation and Contributors (https://dotnetfoundation.org/ & https://stride3d.net)
// Distributed under the MIT license. See the LICENSE.md file in the project root for more information.
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Input;
using Avalonia.Input.Raw;
using Avalonia.Media.Imaging;
using Avalonia.VisualTree;
using Stride.Assets.Editor.Avalonia.Controls;
using Stride.Assets.Editor.Avalonia.Views;
using Stride.Assets.Editor.Avalonia.VisualScriptEditor;
using Stride.Assets.Editor.ViewModels;
using Stride.Assets.Presentation.ViewModels;
using Stride.Assets.Scripts;
using Stride.Core.Assets;
using Stride.Core.Assets.Templates;
using Stride.Core.Assets.Editor.ViewModels;
using Stride.Core.Assets.Serializers;
using Stride.Core.Mathematics;
using Point = Avalonia.Point;

internal static class EditorVisualScriptSmoke
{
    public static async Task Check(SessionViewModel session, Window window)
    {
        var source = session.AllAssets.OfType<ScriptSourceFileViewModel>().First(a => a.AssetItem.FullPath.ToString().Contains(".Game/"));
        Require(System.IO.Path.GetFullPath(source.AssetItem.FullPath).StartsWith(System.IO.Path.GetTempPath(), StringComparison.Ordinal), "Use a disposable project.");
        var data = new VisualScriptAsset { BaseType = "Stride.Engine.SyncScript" };
        data.Properties.Add(new Property("int", "Value"));
        var method = new Method("Update") { VirtualModifier = VirtualModifier.Override };
        var start = new FunctionStartBlock { Position = new Int2(32, 32) };
        var code = new CustomCodeBlock { Code = "System.Console.WriteLine(1);", Position = new Int2(256, 32) };
        start.GenerateSlots(start.Slots, new SlotGeneratorContext()); code.GenerateSlots(code.Slots, new SlotGeneratorContext());
        method.Blocks.Add(start); method.Blocks.Add(code); data.Methods.Add(method);
        VisualScriptViewModel asset;
        using (session.ActionService!.CreateTransaction()) asset = (VisualScriptViewModel)source.Directory.Package.AddAsset(new AssetItem("VisualScriptSmoke_" + Guid.NewGuid().ToString("N"), data), source.Directory);
        session.EditorCollection.OpenAssetEditor(asset);
        var editor = (VisualScriptEditorViewModel)session.EditorCollection.ActiveEditor!;
        await Wait(() => window.GetVisualDescendants().OfType<VisualScriptEditorView>().Any(v => ReferenceEquals(v.DataContext, editor)), "Visual Script tab");
        var view = window.GetVisualDescendants().OfType<VisualScriptEditorView>().Single(v => ReferenceEquals(v.DataContext, editor));
        await editor.AnalysisTask.WaitAsync(TimeSpan.FromSeconds(90));
        Require(editor.AnalysisError == null && editor.GeneratedSource != null, "Project-aware compilation: " + editor.AnalysisError);
        File.WriteAllText("/tmp/stride-visual-script-generated.cs", editor.GeneratedSource);
        Require(!Microsoft.CodeAnalysis.CSharp.CSharpSyntaxTree.ParseText(editor.GeneratedSource!).GetDiagnostics().Any(d => d.Severity == Microsoft.CodeAnalysis.DiagnosticSeverity.Error), "Generated C# contains syntax errors");
        Require(editor.Diagnostics.All(d => d.Severity != Microsoft.CodeAnalysis.DiagnosticSeverity.Error), "Initial diagnostics: " + string.Join(";", editor.Diagnostics.Select(d => d.Message)));
        Require(editor.BlockTemplates.Any(t => t.Type == typeof(MethodCallBlock)) && editor.BlockTemplates.Any(t => t.Type == typeof(ConditionalBranchBlock)), "Missing block factories");
        var refreshThreads = new System.Collections.Concurrent.ConcurrentBag<bool>();
        EventHandler observeRefresh = (_, _) => refreshThreads.Add(global::Avalonia.Threading.Dispatcher.UIThread.CheckAccess());
        editor.GraphChanged += observeRefresh;
        await Task.WhenAll(Enumerable.Range(0, 5).Select(_ => session.NotifyAssetPropertiesChangedAsync([asset])));
        await Task.Delay(100);
        editor.GraphChanged -= observeRefresh;
        Require(refreshThreads.Count > 0 && refreshThreads.All(onUi => onUi), "Background asset notification refreshed graph off the UI thread");
        var history = session.ActionService;
        var first = editor.Properties.Single(); first.Name = "Count";
        Require(data.Properties[0].Name == "Count" && asset.IsDirty, "Variable editing/dirty state");
        await history.TransactionCompletion; history.Undo(); Require(data.Properties[0].Name == "Value", "Variable Undo"); history.Redo();
        editor.AddNewParameterCommand.Execute(null); var parameter = editor.SelectedMethod!.Parameters.Single();
        parameter.Type = "int"; parameter.Name = "argument"; parameter.RefKind = ParameterRefKind.Ref;
        Require(method.Parameters[0].RefKind == ParameterRefKind.Ref && parameter.DisplayName.Contains("argument"), "Parameter editing");
        view.UpdateLayout();
        foreach (var listName in new[] { "VariablesList", "ParametersList" })
        {
            var list = view.FindControl<ListBox>(listName)!;
            var row = list.GetVisualDescendants().OfType<ListBoxItem>().First();
            var label = row.GetVisualDescendants().OfType<TextBlock>().First();
            var dragPointer = new Pointer(95, PointerType.Mouse, true);
            var position = label.TranslatePoint(new Point(2, 2), view)!.Value;
            label.RaiseEvent(new PointerPressedEventArgs(label, dragPointer, view, position, 0, new PointerPointProperties(RawInputModifiers.LeftMouseButton, PointerUpdateKind.LeftButtonPressed), KeyModifiers.None));
            var pending = typeof(VisualScriptEditorView).GetField("dragStart", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!;
            Require(pending.GetValue(view) != null, listName + " swallowed drag initiation during selection");
            label.RaiseEvent(new PointerReleasedEventArgs(label, dragPointer, view, position, 0, new PointerPointProperties(RawInputModifiers.None, PointerUpdateKind.LeftButtonReleased), KeyModifiers.None, MouseButton.Left));
            Require(pending.GetValue(view) == null, listName + " retained drag after release");
        }
        editor.SelectedParameters.Clear();
        editor.SelectedParameters.Add(parameter); editor.RemoveSelectedParametersCommand.Execute(null);
        Require(method.Parameters.Count == 0, "Parameter removal"); await history.TransactionCompletion; history.Undo(); Require(method.Parameters.Count == 1, "Parameter removal Undo"); history.Redo();
        var extra = editor.AddMethod(); extra.Name = "Helper"; extra.ReturnType = "void"; extra.Accessibility = ScriptsAccessibility(); extra.IsStatic = true;
        Require(data.Methods.Count == 2 && extra.Method.IsStatic, "Function editing");
        editor.RemoveSelectedMethodCommand.Execute(null); Require(data.Methods.Count == 1, "Function removal"); await history.TransactionCompletion; history.Undo(); Require(data.Methods.Count == 2, "Function removal Undo"); history.Redo();
        editor.SelectedMethod = editor.Methods.Single();
        editor.AddBlock(new ConditionalBranchBlock(), new Int2(512, 32)); var branch = method.Blocks.Values.OfType<ConditionalBranchBlock>().Single();
        Require(branch.Slots.Count == 4, "Branch slot generation");
        var sourceSlot = start.Slots.Single(); var targetSlot = code.Slots.First(s => s.Direction == SlotDirection.Input && s.Kind == SlotKind.Execution);
        Require(!editor.Connect(sourceSlot, branch.ConditionSlot), "Accepted execution/value mismatch");
        view.GraphControl.Rebuild(); await Task.Delay(200); view.UpdateLayout();
        var port = view.GraphControl.GetVisualDescendants().OfType<Ellipse>().Single(p => ReferenceEquals(p.Tag, sourceSlot));
        var targetPort = view.GraphControl.GetVisualDescendants().OfType<Ellipse>().Single(p => ReferenceEquals(p.Tag, targetSlot));
        var from = port.TranslatePoint(new Point(7.5, 7.5), view.GraphControl)!.Value;
        var to = targetPort.TranslatePoint(new Point(7.5, 7.5), view.GraphControl)!.Value;
        var pointer = new Pointer(94, PointerType.Mouse, true);
        port.RaiseEvent(new PointerPressedEventArgs(port, pointer, view.GraphControl, from, 0, new PointerPointProperties(RawInputModifiers.LeftMouseButton, PointerUpdateKind.LeftButtonPressed), KeyModifiers.None));
        view.GraphControl.RaiseEvent(new PointerReleasedEventArgs(view.GraphControl, pointer, view.GraphControl, to, 0, new PointerPointProperties(RawInputModifiers.None, PointerUpdateKind.LeftButtonReleased), KeyModifiers.None, MouseButton.Left));
        Require(method.Links.Count == 1, "Routed connector gesture");
        Require(!editor.Connect(sourceSlot, targetSlot), "Duplicate link accepted");
        await history.TransactionCompletion; history.Undo(); Require(method.Links.Count == 0, "Connect Undo"); history.Redo(); Require(method.Links.Count == 1, "Connect Redo");
        editor.MoveBlocks(new Dictionary<Block, Int2> { [code] = new(272, 80), [branch] = new(512, 160) });
        Require(code.Position == new Int2(272, 80), "Graph move"); await history.TransactionCompletion; history.Undo(); Require(code.Position == new Int2(256, 32), "Move Undo"); history.Redo();
        view.GraphControl.Rebuild(); await Task.Delay(150); view.UpdateLayout();
        var codeNode = view.GraphControl.GetVisualDescendants().OfType<Border>().Single(b => ReferenceEquals(b.Tag, code));
        var title = (Border)((StackPanel)codeNode.Child!).Children[0];
        var titlePoint = title.TranslatePoint(new Point(10, 5), view.GraphControl)!.Value;
        var movedPoint = new Point(titlePoint.X + 32, titlePoint.Y + 16);
        var previousPosition = code.Position;
        title.RaiseEvent(new PointerPressedEventArgs(title, pointer, view.GraphControl, titlePoint, 0, new PointerPointProperties(RawInputModifiers.LeftMouseButton, PointerUpdateKind.LeftButtonPressed), KeyModifiers.None));
        view.GraphControl.RaiseEvent(new PointerEventArgs(InputElement.PointerMovedEvent, view.GraphControl, pointer, view.GraphControl, movedPoint, 0, new PointerPointProperties(RawInputModifiers.LeftMouseButton, PointerUpdateKind.LeftButtonPressed), KeyModifiers.None));
        Require(code.Position == previousPosition, "Node drag changed the asset before release");
        view.GraphControl.RaiseEvent(new PointerReleasedEventArgs(view.GraphControl, pointer, view.GraphControl, movedPoint, 0, new PointerPointProperties(RawInputModifiers.None, PointerUpdateKind.LeftButtonReleased), KeyModifiers.None, MouseButton.Left));
        Require(code.Position == previousPosition + new Int2(32, 16), "Routed node move");
        await history.TransactionCompletion; history.Undo(); Require(code.Position == previousPosition, "Routed node move Undo");
        editor.SelectedBlocks.Clear();
        Require(VisualScriptGraph.Snap(25) == 32 && VisualScriptGraph.Snap(-25) == -32, "Grid snapping");
        editor.SelectedBlocks.Add(editor.SelectedMethod!.Blocks.Single(b => b.Block == code));
        await Task.Delay(250);
        Require(window.GetVisualDescendants().OfType<VisualScriptPropertyEditor>().Any(), "Custom code inspector editor");
        var propertyEditor = window.GetVisualDescendants().OfType<VisualScriptPropertyEditor>().First(p => p.Content is AvaloniaEdit.TextEditor);
        var codeEditor = (AvaloniaEdit.TextEditor)propertyEditor.Content!;
        var originalCode = code.Code;
        codeEditor.Text = "Entity.Transform"; codeEditor.CaretOffset = codeEditor.Document.TextLength; codeEditor.Focus();
        codeEditor.TextArea.RaiseEvent(new TextInputEventArgs { RoutedEvent = InputElement.TextInputEvent, Text = "." });
        await Wait(() => propertyEditor.CompletionPopup?.IsOpen == true, "Native block C# completion popup");
        var popup = propertyEditor.CompletionPopup!; popup.CompletionList.SelectItem("Position");
        await Wait(() => popup.CompletionList.GetVisualDescendants().OfType<TextBlock>().Any(t => t.Text == "Position" && t.Bounds.Width > 0), "Visible block completion");
        codeEditor.TextArea.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Enter });
        await Wait(() => codeEditor.Text == "Entity.Transform.Position", "Block completion insertion");
        codeEditor.Undo(); Require(codeEditor.Text == "Entity.Transform.", "Block completion native Undo");
        codeEditor.Text = originalCode;
        editor.DeleteSelectionCommand.Execute(null);
        Require(!method.Blocks.ContainsKey(code.Id) && method.Links.Count == 0, "Delete did not remove incident links");
        await history.TransactionCompletion; history.Undo(); Require(method.Blocks.ContainsKey(code.Id) && method.Links.Count == 1, "Delete Undo did not restore topology");
        await editor.AnalyzeAsync();
        var oldInputId = code.Slots.First(s => s.Direction == SlotDirection.Input).Id;
        editor.Update(code, nameof(CustomCodeBlock.Code), "var answer = 1;");
        Require(code.Slots.Any(s => s.Name == "answer") && method.Links.Values.Single().Target.Id == oldInputId, "Code slot regeneration lost links");
        await history.TransactionCompletion; history.Undo();
        Require(code.Code == "System.Console.WriteLine(1);" && code.Slots.All(s => s.Name != "answer") && method.Links.Count == 1, "Code edit and derived slots did not undo together");
        var completions = await editor.CompleteBlockAsync(editor.SelectedMethod!.Blocks.Single(b => b.Block == code), "Entity.Transform.", "Entity.Transform.".Length, false, CancellationToken.None);
        Require(completions.Items.Any(i => i.Text == "Position"), "Project-aware block C# completion");
        var instanceHelper = new Method("InstanceCompletionHelper") { ReturnType = "Stride.Core.Mathematics.Vector3" };
        var staticHelper = new Method("StaticCompletionHelper") { IsStatic = true };
        var staticProperty = new Property("int", "StaticCompletionValue") { IsStatic = true };
        editor.Change("Add completion fixtures", () =>
        {
            editor.Node(data)[nameof(VisualScriptAsset.Methods)].Target.Add(instanceHelper);
            editor.Node(data)[nameof(VisualScriptAsset.Methods)].Target.Add(staticHelper);
            editor.Node(data)[nameof(VisualScriptAsset.Properties)].Target.Add(staticProperty);
        });
        try
        {
            var codeView = editor.SelectedMethod!.Blocks.Single(b => b.Block == code);
            var instanceCompletion = await editor.CompleteBlockAsync(codeView, "", 0, false, CancellationToken.None);
            Require(instanceCompletion.Items.Any(i => i.Text == instanceHelper.Name) && instanceCompletion.Items.Any(i => i.Text == staticHelper.Name), "Own methods missing from instance completion");
            var helperText = instanceHelper.Name + "().";
            var resultCompletion = await editor.CompleteBlockAsync(codeView, helperText, helperText.Length, true, CancellationToken.None);
            Require(resultCompletion.Items.Any(i => i.Text == "X"), "Own method return type missing from expression completion");
            editor.Update(method, nameof(Method.IsStatic), true);
            foreach (var expression in new[] { false, true })
            {
                var staticCompletion = await editor.CompleteBlockAsync(codeView, "", 0, expression, CancellationToken.None);
                Require(staticCompletion.Items.Any(i => i.Text == staticHelper.Name) && staticCompletion.Items.Any(i => i.Text == staticProperty.Name), "Static members missing from completion");
                Require(staticCompletion.Items.All(i => i.Text != instanceHelper.Name && !(i.Text == "Entity" && i.Item.Tags.Contains("Property")) && i.Text != editor.Properties.First().Name), "Instance member offered in static completion");
            }
        }
        finally
        {
            editor.Update(method, nameof(Method.IsStatic), false);
            editor.Change("Remove completion fixtures", () =>
            {
                var methodNode = editor.Node(data)[nameof(VisualScriptAsset.Methods)].Target;
                methodNode.Remove(instanceHelper, new Stride.Core.Quantum.NodeIndex(data.Methods.IndexOf(instanceHelper)));
                methodNode.Remove(staticHelper, new Stride.Core.Quantum.NodeIndex(data.Methods.IndexOf(staticHelper)));
                editor.Node(data)[nameof(VisualScriptAsset.Properties)].Target.Remove(staticProperty, new Stride.Core.Quantum.NodeIndex(data.Properties.IndexOf(staticProperty)));
            });
        }
        var symbols = await editor.SearchMethodsAsync("System.Console.WriteLine", CancellationToken.None);
        Require(symbols.Any(s => s.Name == "WriteLine"), "Roslyn method search");
        editor.AddMethodCall(symbols.First(s => s.Parameters.Length == 1 && s.Parameters[0].Type.SpecialType == Microsoft.CodeAnalysis.SpecialType.System_Int32), new Int2(512, 320));
        var call = method.Blocks.Values.OfType<MethodCallBlock>().Single();
        Require(call.Slots.Count == 3 && call.MethodName == "System.Console.WriteLine", "Method-call factory/slots");
        await history.TransactionCompletion; history.Undo(); Require(!method.Blocks.ContainsKey(call.Id), "Method-call Undo");
        editor.Update(code, nameof(CustomCodeBlock.Code), "System.Console.NoSuchMethod();"); await editor.AnalyzeAsync();
        File.WriteAllText("/tmp/stride-visual-script-error-generated.cs", editor.GeneratedSource);
        await Wait(() => editor.Diagnostics.Any(d => d.BlockId == code.Id && d.Severity == Microsoft.CodeAnalysis.DiagnosticSeverity.Error), "Compiler diagnostic did not map to block: " + editor.AnalysisError + "; " + string.Join(";", editor.Diagnostics.Select(d => d.Message + " [" + d.BlockId + "]")));
        editor.Update(code, nameof(CustomCodeBlock.Code), "System.Console.WriteLine(1);"); await editor.AnalyzeAsync();
        await Wait(() => editor.Diagnostics.All(d => d.Severity != Microsoft.CodeAnalysis.DiagnosticSeverity.Error), "Diagnostics did not clear");
        var originalGlobalDiagnostics = editor.GlobalDiagnosticsText;
        editor.BaseType = "MissingVisualScriptBaseType";
        await editor.AnalyzeAsync();
        await Wait(() => editor.GlobalDiagnosticsText.Contains("MissingVisualScriptBaseType", StringComparison.Ordinal), "Global compiler diagnostic was not exposed");
        Require(editor.AnalysisError == null, "Compiler error was treated as an analysis failure");
        var globalDiagnostics = view.FindControl<TextBlock>("GlobalDiagnosticsText")!;
        await Wait(() => globalDiagnostics.Text == editor.GlobalDiagnosticsText && globalDiagnostics.GetVisualAncestors().OfType<Border>().First().IsVisible, "Global compiler diagnostic was not displayed");
        editor.BaseType = "Stride.Engine.SyncScript";
        await editor.AnalyzeAsync();
        await Wait(() => editor.GlobalDiagnosticsText == originalGlobalDiagnostics && globalDiagnostics.Text == originalGlobalDiagnostics
            && globalDiagnostics.GetVisualAncestors().OfType<Border>().First().IsVisible == (originalGlobalDiagnostics.Length > 0), "Resolved global compiler diagnostic remained visible: " + editor.GlobalDiagnosticsText);
        var getter = new VariableGet { Name = editor.Properties.Single().Name };
        getter.GenerateSlots(getter.Slots, new SlotGeneratorContext()); getter.ValueSlot.Type = "int"; getter.ValueSlot.Value = "42";
        editor.AddBlock(getter, new Int2(256, 240));
        editor.AddVariableBlock(editor.Properties.Single().Symbol, true, new Int2(512, 240));
        var setter = method.Blocks.Values.OfType<VariableSet>().Single();
        await editor.AnalyzeAsync();
        Require(getter.ValueSlot.Type == "int" && getter.ValueSlot.Value == "42" && setter.Name == getter.Name && setter.Slots.Count == 3, "Get/Set blocks or preserved slot hints/values");
        editor.SelectedBlocks.Clear();
        foreach (var block in editor.SelectedMethod!.Blocks.Where(b => b.Block == getter || b.Block == setter).ToArray()) editor.SelectedBlocks.Add(block);
        editor.DeleteSelectionCommand.Execute(null);
        using (var stream = new MemoryStream())
        {
            asset.PropertyGraph!.PrepareForSave(new Stride.Core.Diagnostics.LoggerResult(), asset.AssetItem);
            AssetFileSerializer.Save(stream, data, asset.AssetItem.YamlMetadata);
            File.WriteAllBytes("/tmp/stride-visual-script-roundtrip.sdvs", stream.ToArray()); stream.Position = 0;
            var loaded = AssetFileSerializer.Load<VisualScriptAsset>(stream, "Visual.sdvs").Asset;
            var loadedMethod = loaded.Methods.Single(); var link = loadedMethod.Links.Values.Single();
            Require(loadedMethod.Blocks[code.Id].Position == code.Position && ReferenceEquals(link.Source.Owner, loadedMethod.Blocks[start.Id]) && ReferenceEquals(link.Target.Owner, loadedMethod.Blocks[code.Id]), "Serialization lost positions or slot ownership");
        }
        await Task.Delay(250); view.UpdateLayout();
        using (var bitmap = new RenderTargetBitmap(new PixelSize((int)view.Bounds.Width, (int)view.Bounds.Height))) { bitmap.Render(view); bitmap.Save("/tmp/stride-visual-script-editor.png"); }
        var sourceEditor = new ScriptSourceFileEditorViewModel(source);
        var sourceBefore = sourceEditor.Text;
        try
        {
            sourceEditor.Text = "namespace VisualScriptLiveSmoke { public class Parent : Stride.Engine.SyncScript { public override void Update() {} public virtual void FirstAction() {} } }";
            editor.BaseType = "VisualScriptLiveSmoke.Parent";
            await Wait(() => editor.OverridableMethods.Any(m => m.Name == "FirstAction"), "Unsaved project source did not update overrides");
            sourceEditor.Text = sourceEditor.Text.Replace("FirstAction", "SecondAction");
            await Wait(() => editor.OverridableMethods.Any(m => m.Name == "SecondAction") && editor.OverridableMethods.All(m => m.Name != "FirstAction"), "Source edit did not refresh visual script compilation");
        }
        finally { editor.BaseType = "Stride.Engine.SyncScript"; sourceEditor.Text = sourceBefore; sourceEditor.Destroy(); }
        using (var legacyStream = new MemoryStream())
        {
            AssetFileSerializer.Save(legacyStream, data, null); legacyStream.Position = 0;
            var legacy = AssetFileSerializer.Load<VisualScriptAsset>(legacyStream, "Legacy.sdvs").Asset; legacy.Id = AssetId.New();
            VisualScriptViewModel legacyAsset;
            using (history.CreateTransaction()) legacyAsset = (VisualScriptViewModel)source.Directory.Package.AddAsset(new AssetItem("VisualScriptLegacy_" + Guid.NewGuid().ToString("N"), legacy), source.Directory);
            using var legacyEditor = new EditorScope(new VisualScriptEditorViewModel(legacyAsset));
            var legacyMethod = legacy.Methods.Single(); var legacyLink = legacyMethod.Links.Values.Single();
            Require(ReferenceEquals(legacyLink.Source, legacyMethod.Blocks[start.Id].Slots.Single()) && ReferenceEquals(legacyLink.Target.Owner, legacyMethod.Blocks[code.Id]), "Legacy inline endpoints were not repaired");
            await legacyEditor.Model.AnalysisTask.WaitAsync(TimeSpan.FromSeconds(60));
            await Wait(() => legacyEditor.Model.GeneratedSource != null || legacyEditor.Model.AnalysisError != null, "Legacy initial compilation");
            Require(legacyEditor.Model.GeneratedSource != null && legacyEditor.Model.AnalysisError == null, "Legacy graph does not compile: " + legacyEditor.Model.AnalysisError);
        }
        var readOnlyData = new VisualScriptAsset { BaseType = "Stride.Engine.SyncScript" };
        var readOnlyMethod = new Method("Update") { VirtualModifier = VirtualModifier.Override };
        var readOnlyStart = new FunctionStartBlock();
        var readOnlyCode = new CustomCodeBlock { Code = "System.Console.WriteLine(1);" };
        readOnlyStart.GenerateSlots(readOnlyStart.Slots, new SlotGeneratorContext());
        readOnlyCode.GenerateSlots(readOnlyCode.Slots, new SlotGeneratorContext());
        readOnlyMethod.Blocks.Add(readOnlyStart); readOnlyMethod.Blocks.Add(readOnlyCode);
        var readOnlyLink = new Link { Source = readOnlyStart.Slots.Single(), Target = readOnlyCode.Slots.First(s => s.Direction == SlotDirection.Input) };
        readOnlyMethod.Links.Add(readOnlyLink); readOnlyData.Methods.Add(readOnlyMethod);
        // Simulate a stored graph whose slots need regeneration after a source change.
        readOnlyCode.Code = "var readOnlyValue = 1;";
        var readOnlyItem = new AssetItem("VisualScriptReadOnly_" + Guid.NewGuid().ToString("N"), readOnlyData);
        AssetCollectionItemIdHelper.GenerateMissingItemIds(readOnlyData);
        session.GraphContainer.InitializeAsset(readOnlyItem, new Stride.Core.Diagnostics.LoggerResult());
        var readOnlyAsset = new ReadOnlyVisualScriptViewModel(new(readOnlyItem, source.Directory, false));
        readOnlyAsset.Initialize();
        var originalSlots = readOnlyCode.Slots.ToArray();
        var originalDirty = readOnlyAsset.IsDirty;
        using (var readOnlyEditor = new EditorScope(new VisualScriptEditorViewModel(readOnlyAsset)))
        {
            await readOnlyEditor.Model.AnalysisTask.WaitAsync(TimeSpan.FromSeconds(60));
            Require(readOnlyEditor.Model.GeneratedSource != null && readOnlyEditor.Model.AnalysisError == null, "Read-only visual script analysis failed");
            Require(readOnlyCode.Slots.SequenceEqual(originalSlots) && readOnlyMethod.Links.Values.Single() == readOnlyLink
                && ReferenceEquals(readOnlyLink.Source, readOnlyStart.Slots.Single()) && ReferenceEquals(readOnlyLink.Target, originalSlots[0])
                && readOnlyAsset.IsDirty == originalDirty, "Read-only analysis changed slots, links or dirty state");
        }
        using (history.CreateTransaction()) readOnlyAsset.IsDeleted = true;
        readOnlyAsset.Destroy();
        var template = session.FindTemplates(TemplateScope.Asset).OfType<TemplateAssetDescription>().Single(t => t.AssetTypeName == nameof(VisualScriptAsset));
        var created = (VisualScriptViewModel)(await session.CreateAssetFromTemplate(template, source.Directory))!;
        Require(created.Asset.BaseType == "Stride.Engine.SyncScript" && created.Asset.Methods.Single().Blocks.Values.Single() is FunctionStartBlock, "Visual script creation template");
        using (var templateEditor = new EditorScope(new VisualScriptEditorViewModel(created)))
        {
            await templateEditor.Model.AnalysisTask.WaitAsync(TimeSpan.FromSeconds(60));
            await Wait(() => templateEditor.Model.GeneratedSource != null || templateEditor.Model.AnalysisError != null, "Template initial compilation");
            Require(templateEditor.Model.GeneratedSource != null && templateEditor.Model.AnalysisError == null && templateEditor.Model.Diagnostics.All(d => d.Severity != Microsoft.CodeAnalysis.DiagnosticSeverity.Error), "New visual script does not compile: " + templateEditor.Model.AnalysisError);
        }
        Console.WriteLine("PASS: Visual Script tab, variables/functions/parameters, block factories, routed connections, mismatch/duplicate rejection, moves, deletion with links, Undo/Redo, project Roslyn compilation/search/diagnostics, code inspector and .sdvs round trip.");
        static Stride.Assets.Scripts.Accessibility ScriptsAccessibility() => Stride.Assets.Scripts.Accessibility.Private;
    }
    private sealed class ReadOnlyVisualScriptViewModel(Stride.Core.Assets.Presentation.ViewModels.AssetViewModel.ConstructorParameters parameters) : VisualScriptViewModel(parameters)
    {
        public override bool IsEditable => false;
    }
    private sealed class EditorScope(VisualScriptEditorViewModel model) : IDisposable
    {
        public VisualScriptEditorViewModel Model => model;
        public void Dispose() => model.Destroy();
    }
    private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
    private static async Task Wait(Func<bool> condition, string message)
    {
        for (var i = 0; i < 150; i++) { if (condition()) return; await Task.Delay(100); }
        throw new TimeoutException(message);
    }
}
