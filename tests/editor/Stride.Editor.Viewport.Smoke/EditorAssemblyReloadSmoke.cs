// Copyright (c) .NET Foundation and Contributors (https://dotnetfoundation.org/ & https://stride3d.net)
// Distributed under the MIT license. See the LICENSE.md file in the project root for more information.

using System.Reflection;
using Avalonia.Controls;
using Avalonia.Threading;
using Stride.Assets.Entities;
using Stride.Assets.Presentation.ViewModels;
using Stride.Core.Assets;
using Stride.Core.Assets.Editor.ViewModels;
using Stride.Core.Assets.Serializers;
using Stride.Core.Presentation.Quantum;
using Stride.Core.Reflection;
using Stride.Engine;

internal static class EditorAssemblyReloadSmoke
{
    public static async Task Check(SessionViewModel session, Window window)
    {
        var project = session.CurrentProject!;
        var reload = session.HotReload ?? throw new Exception("No assembly reload service.");
        reload.AutoReload = false;
        var reloadLogger = (Stride.Core.Diagnostics.LoggerResult)typeof(Stride.Core.Assets.Editor.Services.AssemblyHotReloadService).GetField("logger", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(reload)!;
        reloadLogger.MessageLogged += (_, e) => { if (e.Message.Type >= Stride.Core.Diagnostics.LogMessageType.Info) Console.WriteLine("[HotReload] " + e.Message); };
        var slot = session.LocalPackages.SelectMany(p => p.PackageContainer.Package.LoadedAssemblies)
            .First(a => a.Assembly != null && a.ProjectReference != null && a.ProjectReference.Location.ToString().Contains(".Game"));
        var directory = Path.GetDirectoryName(slot.ProjectReference.Location)!;
        Require(Path.GetFullPath(directory).StartsWith(Path.GetTempPath(), StringComparison.Ordinal)
            || File.Exists(Path.Combine(directory, ".stride-disposable-smoke-project")), "Use a disposable project copy under /tmp.");
        var suffix = Guid.NewGuid().ToString("N");
        var name = "ReloadSmoke" + suffix;
        var source = Path.Combine(directory, name + ".cs");
        var typeName = "HotReloadFixture." + name;
        var history = session.ActionService!;
        try
        {
            await File.WriteAllTextAsync(source, Code(1));
            Require(await reload.ReloadAsync().WaitAsync(TimeSpan.FromMinutes(3)), "Initial compilation and load");
            var firstType = slot.Assembly.GetType(typeName, true)!;
            var script = (EntityComponent)Activator.CreateInstance(firstType)!;
            var entity = new Entity("Reload fixture");
            var target = new Entity("Referenced target");
            firstType.GetProperty("Target")!.SetValue(script, target);
            entity.Components.Add(script);
            var data = new SceneAsset();
            foreach (var item in new[] { entity, target })
            {
                data.Hierarchy.Parts.Add(new EntityDesign(item));
                data.Hierarchy.RootParts.Add(item);
            }
            SceneViewModel asset;
            using (history.CreateTransaction()) asset = (SceneViewModel)project.AddAsset(new AssetItem(name, data), project.AssetMountPoint);
            var propertyNode = session.AssetNodeContainer.GetOrCreateNode(script);
            using (history.CreateTransaction()) propertyNode["Value"].Update(42);
            var componentId = script.Id;
            await session.ActiveProperties.GenerateSelectionPropertiesAsync([asset]);
            var selectedAssets = session.AssetCollection.SelectedAssets.ToArray();

            await File.WriteAllTextAsync(source, Code(2));
            Require(await reload.ReloadAsync().WaitAsync(TimeSpan.FromMinutes(3)), "Reloading version 2");
            var updated = Component();
            Require(updated.GetType() != firstType && Version(updated) == 2, "The scene retained the old script type or method body");
            Require((int)updated.GetType().GetProperty("Value")!.GetValue(updated)! == 42, "Unsaved script value was lost");
            Require(updated.Id == componentId && ReferenceEquals(updated.GetType().GetProperty("Target")!.GetValue(updated), target), "Component ID or entity reference was lost");
            Require((string)updated.GetType().GetProperty("Added")!.GetValue(updated)! == "new field", "New serialized field default");
            Require(session.AssetCollection.SelectedAssets.SequenceEqual(selectedAssets), "Reload changed the asset selection");
            history.Undo();
            Require(ReferenceEquals(Component(), script) && Version(Component()) == 1 && ReferenceEquals(slot.Assembly, firstType.Assembly), "Undo did not restore the old assembly and original component");
            history.Redo();
            Require(ReferenceEquals(Component(), updated) && Version(Component()) == 2, "Redo did not restore the reloaded component");
            using (var stream = new MemoryStream())
            {
                asset.PropertyGraph!.PrepareForSave(new Stride.Core.Diagnostics.LoggerResult(), asset.AssetItem);
                AssetFileSerializer.Save(stream, data, null);
                stream.Position = 0;
                var loaded = AssetFileSerializer.Load<SceneAsset>(stream, "HotReload.sdscene").Asset;
                var loadedScript = loaded.Hierarchy.RootParts.First(e => e.Name == entity.Name).Components.First(c => c.GetType().FullName == typeName);
                Require(Version(loadedScript) == 2 && (int)loadedScript.GetType().GetProperty("Value")!.GetValue(loadedScript)! == 42, "Reloaded scene serialization");
            }

            var retainedAssembly = slot.Assembly;
            await File.WriteAllTextAsync(source, "this is invalid C#;");
            Require(!await reload.ReloadAsync().WaitAsync(TimeSpan.FromMinutes(3)), "Compiler errors were not reported");
            Require(ReferenceEquals(slot.Assembly, retainedAssembly) && ReferenceEquals(Component(), updated), "Compilation failure changed the active generation");

            // Exercise actual source watching and debounce, without invoking the command.
            reload.AutoReload = true;
            await File.WriteAllTextAsync(source, Code(3));
            for (var i = 0; i < 720 && (Version(Component()) != 3 || reload.IsBusy); i++) await Task.Delay(250);
            Require(Version(Component()) == 3 && !reload.IsBusy, "Source watcher did not apply version 3");
            Require((int)Component().GetType().GetProperty("Value")!.GetValue(Component())! == 42, "Automatic reload lost state");
            reload.AutoReload = false;
            // A timestamp-only binary event must not add another reload generation.
            var count = reload.ReloadCount;
            File.SetLastWriteTimeUtc(slot.Path, DateTime.UtcNow);
            Require(await reload.ReloadAsync(false), "Binary no-op check");
            Require(reload.ReloadCount == count, "An unchanged DLL was reloaded");
            await session.ActiveProperties.GenerateSelectionPropertiesAsync(Array.Empty<IPropertyProviderViewModel>());
            Console.WriteLine("PASS: assembly source compilation, staged reload, changed script method/new field, unsaved values, component IDs/entity references, Undo/Redo, serialization, compiler-error retention, file watching/debounce and unchanged-binary suppression.");

            EntityComponent Component() => entity.Components.First(c => c.GetType().FullName == typeName);
        }
        finally
        {
            reload.AutoReload = false;
            if (File.Exists(source)) File.Delete(source);
        }
        string Code(int version) => $$"""
            using Stride.Core;
            using Stride.Engine;
            namespace HotReloadFixture;
            [DataContract("{{name}}")]
            public class {{name}} : SyncScript
            {
                [DataMember(10)] public int Value { get; set; } = 7;
                [DataMember(20)] public Entity Target { get; set; }
                {{(version >= 2 ? "[DataMember(30)] public string Added { get; set; } = \"new field\";" : "")}}
                public int CodeVersion() => {{version}};
                public override void Update() { }
            }
            """;
    }
    private static int Version(object script) => (int)script.GetType().GetMethod("CodeVersion")!.Invoke(script, null)!;
    private static void Require(bool condition, string message) { if (!condition) throw new Exception("Hot Reload: " + message); }
}
