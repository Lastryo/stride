// Copyright (c) .NET Foundation and Contributors (https://dotnetfoundation.org/ & https://stride3d.net)
// Distributed under the MIT license. See the LICENSE.md file in the project root for more information.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Stride.Core;
using Stride.Core.Diagnostics;
using Stride.Core.Reflection;
using Stride.Core.Serialization;
using Stride.Core.Yaml;
using Stride.Core.Yaml.Events;
using Stride.Core.Yaml.Serialization;
using Stride.Debugger.Target;
using Stride.Engine;

namespace Stride.Debugger
{
    public static class LiveAssemblyReloader
    {
        /// <summary>Replaces live components while the caller holds the game's TickLock.</summary>
        public static void Reload(Game game, AssemblyContainer assemblyContainer, List<Assembly> assembliesToUnregister, List<Assembly> assembliesToRegister)
        {
            var components = game?.SceneSystem.SceneInstance?.SelectMany(e => e.Components
                .Select((component, index) => new ComponentEntry(e, index, component)))
                .Where(e => assembliesToUnregister.Contains(e.Old.GetType().Assembly)).ToList() ?? new List<ComponentEntry>();
            var logger = new LoggerResult();
            var settings = new SerializerContextSettings
            {
                MemberMask = DataMemberAttribute.DefaultMask | ScriptComponent.LiveScriptingMask,
                Logger = logger,
            };
            CloneReferenceSerializer.References = new List<object>();
            try
            {
                // Snapshot all components before any registry or entity mutation.
                foreach (var entry in components)
                    AssetYamlSerializer.Default.Serialize(new ParsingEventListEmitter(entry.Events), entry.Old, typeof(EntityComponent), settings);
                try
                {
                    Switch(assembliesToUnregister, assembliesToRegister);
                    // Instantiate every replacement before resolving cyclic component references.
                    foreach (var entry in components)
                    {
                        var oldType = entry.Old.GetType();
                        var assembly = assembliesToRegister.Single(a => a.GetName().Name == oldType.Assembly.GetName().Name);
                        var type = assembly.GetType(oldType.FullName, true);
                        entry.New = (EntityComponent)Activator.CreateInstance(type);
                    }
                    for (var i = 0; i < CloneReferenceSerializer.References.Count; i++)
                    {
                        var reference = CloneReferenceSerializer.References[i];
                        var replacement = components.FirstOrDefault(e => ReferenceEquals(e.Old, reference));
                        if (replacement != null) CloneReferenceSerializer.References[i] = replacement.New;
                    }
                    foreach (var entry in components)
                    {
                        var reader = new EventReader(new MemoryParser(entry.Events));
                        var restored = AssetYamlSerializer.Default.Deserialize(reader, entry.New, typeof(EntityComponent), out _, settings);
                        if (!ReferenceEquals(restored, entry.New) || logger.HasErrors)
                            throw new InvalidOperationException($"Cannot restore live component {entry.Old.GetType().FullName}.");
                    }
                    foreach (var entry in components)
                    {
                        if (entry.Old is ScriptComponent oldScript && entry.New is ScriptComponent newScript)
                            game.Script.LiveReload(oldScript, newScript);
                        entry.Entity.Components[entry.Index] = entry.New;
                    }
                }
                catch
                {
                    Switch(assembliesToRegister, assembliesToUnregister);
                    foreach (var entry in components)
                        if (!ReferenceEquals(entry.Entity.Components[entry.Index], entry.Old)) entry.Entity.Components[entry.Index] = entry.Old;
                    throw;
                }
                foreach (var entry in components)
                {
                    try { (entry.Old as ScriptComponent)?.Cancel(); }
                    catch (Exception exception) { GlobalLogger.GetLogger("LivePlay").Error("A replaced script threw during cancellation.", exception); }
                }
            }
            finally { CloneReferenceSerializer.References = null; }
        }

        private static void Switch(IEnumerable<Assembly> oldAssemblies, IEnumerable<Assembly> newAssemblies)
        {
            foreach (var assembly in oldAssemblies)
            {
                AssemblyRegistry.Unregister(assembly);
                DataSerializerFactory.UnregisterSerializationAssembly(assembly);
            }
            foreach (var assembly in newAssemblies)
            {
                ModuleRuntimeHelpers.RunModuleConstructor(assembly.ManifestModule);
                AssemblyRegistry.Register(assembly, AssemblyCommonCategories.Engine, AssemblyCommonCategories.Assets);
                DataSerializerFactory.RegisterSerializationAssembly(assembly);
            }
        }

        private sealed class ComponentEntry
        {
            public readonly Entity Entity;
            public readonly int Index;
            public readonly EntityComponent Old;
            public readonly List<ParsingEvent> Events = new List<ParsingEvent>();
            public EntityComponent New;
            public ComponentEntry(Entity entity, int index, EntityComponent component)
            { Entity = entity; Index = index; Old = component; }
        }
    }
}
