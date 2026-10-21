// Copyright (c) .NET Foundation and Contributors (https://dotnetfoundation.org/ & https://stride3d.net)
// Distributed under the MIT license. See the LICENSE.md file in the project root for more information.

using Avalonia.Controls;
using Stride.Core.Assets;
using Stride.Core.Assets.Editor.ViewModels;
using Stride.Core.Diagnostics;
using Stride.Core.Presentation.Commands;

internal static class EditorProjectBuildSmoke
{
    public static async Task Check(SessionViewModel session, Window window)
    {
        var main = window.DataContext!;
        var build = main.GetType().GetProperty("ProjectBuild")!.GetValue(main)!;
        var type = build.GetType();
        var runProject = session.Projects.Single(x => x.ProjectPath.GetFileName() == "Launch.csproj");
        var library = session.Projects.First(x => x.Project.Type == ProjectType.Library);
        var view = (Control)window.Content!;
        var selector = view.FindControl<ComboBox>("BuildProjectSelector")!;
        var buildCommand = (ICommandBase)type.GetProperty("BuildProjectCommand")!.GetValue(build)!;
        var runCommand = (ICommandBase)type.GetProperty("RunProjectCommand")!.GetValue(build)!;
        var cancelCommand = (ICommandBase)type.GetProperty("CancelBuildCommand")!.GetValue(build)!;
        Task<bool> Build(bool run) => (Task<bool>)type.GetMethod("BuildAsync")!.Invoke(build, [run])!;
        bool Busy() => (bool)type.GetProperty("IsBusy")!.GetValue(build)!;
        int? ProcessId() => (int?)type.GetProperty("LastStartedProcessId")!.GetValue(build);
        string Assembly() => (string)type.GetProperty("LastBuiltAssemblyPath")!.GetValue(build)!;
        if (session.CurrentProject != runProject) throw new Exception("Startup did not prefer the executable for the local platform.");
        foreach (var pair in new[] { ("BuildProject", buildCommand), ("RunProject", runCommand), ("CancelBuild", cancelCommand) })
        {
            if (!ReferenceEquals(view.FindControl<MenuItem>(pair.Item1 + "MenuItem")!.Command, pair.Item2)
                || !ReferenceEquals(view.FindControl<Button>(pair.Item1 + "Button")!.Command, pair.Item2))
                throw new Exception("Build/run/cancel menu and toolbar bindings do not agree.");
        }
        selector.SelectedItem = library;
        if (session.CurrentProject != library || !buildCommand.IsEnabled || runCommand.IsEnabled || cancelCommand.IsEnabled)
            throw new Exception("Library selection did not enable Build and disable Run.");
        if (!await Build(false).WaitAsync(TimeSpan.FromSeconds(60)) || !File.Exists(Assembly()))
            throw new Exception("The selected library did not build to disk.");
        selector.SelectedItem = runProject;
        if (session.CurrentProject != runProject || !runCommand.IsEnabled) throw new Exception("Executable selection did not enable Run.");
        type.GetProperty("IsClosing")!.SetValue(build, true);
        if (buildCommand.IsEnabled || runCommand.IsEnabled || await Build(false))
            throw new Exception("A new build was accepted while the session was closing.");
        type.GetProperty("IsClosing")!.SetValue(build, false);
        if (!await Build(true).WaitAsync(TimeSpan.FromSeconds(60))) throw new Exception("The executable did not build/run.");
        var marker = Path.Combine(Path.GetDirectoryName(Assembly())!, "run-marker.txt");
        await Wait(() => File.Exists(marker));
        if (File.ReadAllText(marker) != ProcessId()?.ToString()) throw new Exception("Run did not start the expected process in a path containing spaces.");
        File.Delete(marker);
        var directory = Path.GetDirectoryName((string)runProject.ProjectPath)!;
        var props = Path.Combine(directory, "Directory.Build.props");
        try
        {
            File.WriteAllText(props, "<Project><PropertyGroup><UseAppHost>false</UseAppHost></PropertyGroup></Project>");
            File.Delete(Path.ChangeExtension(Assembly(), OperatingSystem.IsWindows() ? ".exe" : null));
            if (!await Build(true).WaitAsync(TimeSpan.FromSeconds(60))) throw new Exception("Run without an apphost did not use dotnet.");
            await Wait(() => File.Exists(marker));
            if (File.ReadAllText(marker) != ProcessId()?.ToString()) throw new Exception("The dotnet fallback did not start the expected process.");
            File.Delete(marker);
        }
        finally { File.Delete(props); }
        var originalProcess = ProcessId();
        var broken = Path.Combine(directory, "BrokenSmoke.cs");
        var gate = Path.Combine(directory, "cancel-gate.txt");
        var targets = Path.Combine(directory, "Directory.Build.targets");
        try
        {
            File.WriteAllText(broken, "public class {");
            if (await Build(true).WaitAsync(TimeSpan.FromSeconds(60))) throw new Exception("A C# compilation failure was reported as successful.");
            if (ProcessId() != originalProcess || File.Exists(marker)) throw new Exception("Run launched after a failed build.");
            await Wait(() => session.OutputLog.Messages.Any(x => x.Type >= LogMessageType.Error && x.Module?.Contains("BrokenSmoke.cs") == true));
            File.Delete(broken);
            File.WriteAllText(targets, """
                <Project><Target Name="SmokeBuildGate" BeforeTargets="CoreCompile">
                  <WriteLinesToFile File="$(MSBuildProjectDirectory)/cancel-gate.txt" Lines="entered" Overwrite="true" />
                  <Exec Command="sleep 30" />
                </Target></Project>
                """);
            view.FindControl<TabControl>("AssetTabs")!.SelectedIndex = 0;
            var cancelled = Build(false);
            await Wait(() => File.Exists(gate));
            if (!Busy() || buildCommand.IsEnabled || runCommand.IsEnabled || !cancelCommand.IsEnabled || selector.IsEnabled
                || view.FindControl<TabControl>("AssetTabs")!.SelectedIndex != 2)
                throw new Exception("Build command states/project selector do not reflect an active build.");
            if (await Build(false)) throw new Exception("A second overlapping build was accepted.");
            cancelCommand.Execute(null);
            if (await cancelled.WaitAsync(TimeSpan.FromSeconds(15)) || Busy() || !buildCommand.IsEnabled || cancelCommand.IsEnabled)
                throw new Exception("Cancel did not stop MSBuild or restore commands.");
            Console.WriteLine("PASS: project selector and command bindings, library build, local apphost/dotnet Run with spaced path, failure prevents launch and reaches Output, active states, duplicate rejection and MSBuild cancellation.");
            File.Delete(gate);
            var interrupted = Build(false);
            await Wait(() => File.Exists(gate));
            var closing = (Task<bool>)main.GetType().GetMethod("CloseSessionAsync")!.Invoke(main, null)!;
            if (!await closing.WaitAsync(TimeSpan.FromSeconds(45)) || await interrupted || Busy())
                throw new Exception("Closing the project did not cancel and await the active build.");
            if (main.GetType().GetProperty("Session")!.GetValue(main) != null || buildCommand.IsEnabled || runCommand.IsEnabled)
                throw new Exception("Closed session retained active project commands.");
            Console.WriteLine("PASS: closing a session cancels/awaits its build and releases project commands.");
        }
        finally
        {
            File.Delete(broken);
            File.Delete(targets);
            File.Delete(gate);
        }
    }

    private static async Task Wait(Func<bool> condition)
    {
        for (var attempt = 0; attempt < 400; attempt++)
        {
            if (condition()) return;
            await Task.Delay(25);
        }
        throw new TimeoutException("Build/run smoke did not reach the expected state.");
    }
}
