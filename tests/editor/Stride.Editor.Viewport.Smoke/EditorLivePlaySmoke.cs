// Copyright (c) .NET Foundation and Contributors (https://dotnetfoundation.org/ & https://stride3d.net)
// Distributed under the MIT license. See the LICENSE.md file in the project root for more information.

using System.Diagnostics;
using Avalonia.Controls;
using Stride.Core.Assets.Editor.ViewModels;
using Stride.Core.Presentation.Commands;

internal static class EditorLivePlaySmoke
{
    public static async Task Check(SessionViewModel session, Window window)
    {
        var main = window.DataContext!;
        var build = main.GetType().GetProperty("ProjectBuild")!.GetValue(main)!;
        var type = build.GetType();
        var live = type.GetProperty("LivePlay")!.GetValue(build)!;
        var liveType = live.GetType();
        foreach (var owner in new[] { build, live, session.HotReload! })
        {
            var logger = (Stride.Core.Diagnostics.LoggerResult)owner.GetType().GetField("logger", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!.GetValue(owner)!;
            logger.MessageLogged += (_, e) => { if (e.Message.Type is Stride.Core.Diagnostics.LogMessageType.Info or >= Stride.Core.Diagnostics.LogMessageType.Error) Console.WriteLine("[LivePlaySmoke] " + e.Message); };
        }
        var slot = session.LocalPackages.SelectMany(p => p.PackageContainer.Package.LoadedAssemblies)
            .First(a => a.ProjectReference?.Location.ToString().Contains(".Game") == true);
        var directory = Path.GetDirectoryName(slot.ProjectReference.Location)!;
        Require(Path.GetFullPath(directory).StartsWith(Path.GetTempPath(), StringComparison.Ordinal)
            || File.Exists(Path.Combine(directory, ".stride-disposable-smoke-project")), "Use a disposable project copy.");
        var source = Path.Combine(directory, "LivePlaySmoke.cs");
        Require(!File.Exists(source), "Fixture path is already occupied");
        var proof = Path.Combine(Path.GetTempPath(), "stride-liveplay-" + Guid.NewGuid().ToString("N"));
        var reload = session.HotReload!;
        reload.AutoReload = false;
        var stop = (ICommandBase)type.GetProperty("StopLivePlayCommand")!.GetValue(build)!;
        try
        {
            await File.WriteAllTextAsync(source, Code(1));
            var command = (ICommandBase)type.GetProperty("LivePlayCommand")!.GetValue(build)!;
            Require(ReferenceEquals(((Control)window.Content!).FindControl<MenuItem>("LivePlayMenuItem")!.Command, command), "LivePlay menu binding");
            var start = (Task<bool>)type.GetMethod("StartLivePlayAsync")!.Invoke(build, null)!;
            Require(await start.WaitAsync(TimeSpan.FromMinutes(5)), "LivePlay failed to start");
            var initial = await WaitForVersion(1);
            var pid = (int)liveType.GetProperty("ProcessId")!.GetValue(live)!;
            Require(initial[0] == pid.ToString() && stop.IsEnabled, "Wrong child process or disabled stop command");
            Require(!await ((Task<bool>)type.GetMethod("StartLivePlayAsync")!.Invoke(build, null)!), "Second LivePlay session was accepted");
            reload.AutoReload = true;
            await File.WriteAllTextAsync(source, Code(2));
            var second = await WaitForVersion(2);
            Require(second[0] == initial[0] && second[2] == initial[2] && second[3] == "41" && second[5] == "True", "PID, component ID, runtime state or cyclic component/entity references changed");
            Require(int.Parse(second[4]) >= int.Parse(initial[4]) && second[6] == "added", "Live-only field or added property default was lost");
            reload.AutoReload = false;
            await File.WriteAllTextAsync(source, "this is invalid C#;");
            Require(!await reload.ReloadAsync().WaitAsync(TimeSpan.FromMinutes(3)), "Compilation failure was accepted");
            Require((await WaitForVersion(2))[0] == pid.ToString(), "Compilation failure replaced the game process");
            reload.AutoReload = true;
            await File.WriteAllTextAsync(source, Code(3));
            Require((await WaitForVersion(3))[0] == pid.ToString(), "Recovery restarted the game process");
            reload.AutoReload = false;
            liveType.GetMethod("Stop")!.Invoke(live, null);
            await Wait(() => Exited(pid));
            Require(!(bool)liveType.GetProperty("IsRunning")!.GetValue(live)! && !stop.IsEnabled, "Stop did not release the LivePlay state");
            var restarting = (Task<bool>)type.GetMethod("StartLivePlayAsync")!.Invoke(build, null)!;
            await Wait(() => (bool)liveType.GetProperty("IsRunning")!.GetValue(live)! || restarting.IsCompleted);
            type.GetProperty("IsClosing")!.SetValue(build, true);
            await ((Task)type.GetMethod("CancelAndWaitAsync")!.Invoke(build, null)!).WaitAsync(TimeSpan.FromSeconds(30));
            await restarting;
            Require(!(bool)liveType.GetProperty("IsRunning")!.GetValue(live)!, "Closing left a starting LivePlay child active");
            type.GetProperty("IsClosing")!.SetValue(build, false);
            Console.WriteLine("PASS: LivePlay menu/start, real child game, automatic code updates without PID changes, runtime state/live fields, component IDs/cyclic references, new properties, compiler-error retention/recovery stop, restart and close during startup.");
        }
        finally
        {
            reload.AutoReload = false;
            liveType.GetMethod("Stop")!.Invoke(live, null);
            File.Delete(source); File.Delete(proof);
        }

        async Task<string[]> WaitForVersion(int version)
        {
            string[] result = [];
            await Wait(() =>
            {
                Require((bool)liveType.GetProperty("IsRunning")!.GetValue(live)!, "The game exited before reaching version " + version);
                try { result = File.ReadAllText(proof).Split('|'); return result.Length == 7 && result[1] == version.ToString() && int.Parse(result[4]) > 5; }
                catch (IOException) { return false; }
                catch (FormatException) { return false; }
            });
            return result;
        }
        string Code(int version) => $$"""
            using System;
            using System.IO;
            using System.Threading.Tasks;
            using Stride.Core;
            using Stride.Engine;
            namespace LivePlayFixture;
            public class FixtureGame : Game
            {
                protected override async Task LoadContent()
                {
                    await base.LoadContent();
                    var first = new Entity("first");
                    var second = new Entity("second");
                    var a = new FixtureScript(); var b = new FixtureScript();
                    a.Friend = b; b.Friend = a; a.Target = second; b.Target = first;
                    first.Components.Add(a); second.Components.Add(b);
                    SceneSystem.SceneInstance.RootScene.Entities.Add(first);
                    SceneSystem.SceneInstance.RootScene.Entities.Add(second);
                }
            }
            [DataContract("LivePlayFixtureScript")]
            public class FixtureScript : SyncScript
            {
                [DataMember(10)] public int Value { get; set; } = {{(version == 1 ? 41 : 999)}};
                [DataMember(20)] public FixtureScript Friend { get; set; }
                [DataMember(30)] public Entity Target { get; set; }
                [DataMember(40, Mask = LiveScriptingMask)] public int Ticks { get; set; }
                {{(version >= 2 ? "[DataMember(50)] public string Added { get; set; } = \"added\";" : "")}}
                public override void Update()
                {
                    Ticks++;
                    if (Entity.Name == "first" && Ticks % 10 == 0)
                        File.WriteAllText({{System.Text.Json.JsonSerializer.Serialize(proof)}},
                            $"{Environment.ProcessId}|{{version}}|{Id}|{Value}|{Ticks}|{ReferenceEquals(Friend.Friend, this) && ReferenceEquals(Target, Friend.Entity)}|{{(version >= 2 ? "{Added}" : "original")}}");
                }
            }
            """;
    }
    private static bool Exited(int pid) { try { using var p = Process.GetProcessById(pid); return p.HasExited; } catch (ArgumentException) { return true; } }
    private static async Task Wait(Func<bool> condition)
    {
        for (var i = 0; i < 720; i++) { if (condition()) return; await Task.Delay(250); }
        throw new TimeoutException("LivePlay fixture did not reach the expected state.");
    }
    private static void Require(bool condition, string message) { if (!condition) throw new Exception("LivePlay: " + message); }
}
