// Copyright (c) .NET Foundation and Contributors (https://dotnetfoundation.org/ & https://stride3d.net)
// Distributed under the MIT license. See the LICENSE.md file in the project root for more information.

using System.Diagnostics;
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using ServiceWire.NamedPipes;
using Stride.Core.Assets.Editor.ViewModels;
using Stride.Core.Diagnostics;
using Stride.Core.Presentation.ViewModels;
using Stride.Debugger.Target;

namespace Stride.GameStudio.Avalonia.Debugging;

/// <summary>Owns the child game and sends coherent assembly generations over the existing debugger protocol.</summary>
// shortcut: transfer whole assemblies; use source groups if compilation latency becomes a problem.
internal sealed class LivePlayService : DispatcherViewModel
{
    private readonly SessionViewModel session;
    private readonly LoggerResult logger = new("LivePlay");
    private readonly Dictionary<Assembly, AssemblyImage> images = [];
    private Dictionary<Assembly, DebugAssembly> remoteAssemblies = [];
    private Process? process;
    private NpHost? pipe;
    private GameDebuggerHost? host;
    private IGameDebuggerTarget? target;
    private Action? gameExited;
    private CancellationTokenSource? playCancellation;
    private Task updateTask = Task.CompletedTask;
    private bool isRunning;
    private int epoch;

    public LivePlayService(SessionViewModel session) : base(session.ServiceProvider)
    {
        this.session = session;
        session.OutputLog.AddLogger(logger);
        session.HotReload!.Reloaded += AssembliesReloaded;
        session.Closing += SessionClosing;
    }

    public bool IsRunning { get => isRunning; private set => SetValue(ref isRunning, value); }
    public int? ProcessId => process?.Id;

    public async Task<bool> StartAsync(string executablePath)
    {
        if (IsRunning) return false;
        logger.Clear(); logger.HasErrors = false;
        var currentEpoch = ++epoch;
        playCancellation = new CancellationTokenSource();
        var token = playCancellation.Token;
        try
        {
            remoteAssemblies = [];
            var remoteMap = remoteAssemblies;
            var generation = CaptureGeneration();
            var address = "StrideDebugger-" + Guid.NewGuid().ToString("N");
            host = new GameDebuggerHost(logger);
            gameExited = () => _ = Dispatcher.InvokeAsync(() => { if (epoch == currentEpoch) Stop(); });
            host.GameExited += gameExited;
            pipe = new NpHost(address, null, null);
            pipe.AddService<IGameDebuggerHost>(host);
            pipe.Open();
            var entry = Assembly.GetEntryAssembly()!.Location;
            var start = new ProcessStartInfo("dotnet")
            {
                WorkingDirectory = Path.GetDirectoryName(executablePath)!,
                UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true,
            };
            start.ArgumentList.Add("exec");
            start.ArgumentList.Add("--runtimeconfig"); start.ArgumentList.Add(Path.ChangeExtension(entry, ".runtimeconfig.json"));
            start.ArgumentList.Add("--depsfile"); start.ArgumentList.Add(Path.ChangeExtension(entry, ".deps.json"));
            start.ArgumentList.Add(typeof(GameDebuggerTarget).Assembly.Location);
            start.ArgumentList.Add("--host=" + address);
            start.ArgumentList.Add("--data-path=" + Path.Combine(start.WorkingDirectory, "data"));
            start.ArgumentList.Add("--parent-id=" + Environment.ProcessId);
            process = new Process { StartInfo = start, EnableRaisingEvents = true };
            process.OutputDataReceived += (_, e) => { if (e.Data != null) logger.Info(e.Data); };
            process.ErrorDataReceived += (_, e) => { if (e.Data != null) logger.Warning(e.Data); };
            process.Exited += (_, _) => _ = Dispatcher.InvokeAsync(() => { if (epoch == currentEpoch) Stop(); });
            process.Start(); process.BeginOutputReadLine(); process.BeginErrorReadLine();
            IsRunning = true;
            var connected = host.Target;
            if (await Task.WhenAny(connected, process.WaitForExitAsync(token)).WaitAsync(TimeSpan.FromSeconds(20), token) != connected)
                throw new InvalidOperationException("The LivePlay host exited before connecting. See its output above.");
            target = await connected;
            if (currentEpoch != epoch) return false;
            var remote = target;
            var applied = await Task.Run(() => ApplyGeneration(remote, generation, remoteMap)).WaitAsync(token);
            if (currentEpoch != epoch) return false;
            if (!applied) throw new InvalidOperationException("Initial LivePlay assembly load failed.");
            await Task.Run(() => remote.GameLaunch(remote.GameEnumerateTypeNames()[0])).WaitAsync(token);
            if (currentEpoch != epoch) return false;
            logger.Info($"LivePlay started (PID {process.Id}). Save C# sources to update the running game.");
            return true;
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            logger.Info("LivePlay startup cancelled.");
            return false;
        }
        catch (Exception exception)
        {
            if (currentEpoch == epoch)
            {
                logger.Error("Unable to start LivePlay.", exception);
                Stop();
            }
            return false;
        }
    }

    private AssemblyImage[] CaptureGeneration() => session.LocalPackages.SelectMany(p => p.PackageContainer.Package.LoadedAssemblies)
        .Where(a => a.Assembly != null).Select(a =>
        {
            if (images.TryGetValue(a.Assembly, out var image)) return image;
            var pe = File.ReadAllBytes(a.Path);
            using var reader = new PEReader(new MemoryStream(pe));
            var metadata = reader.GetMetadataReader();
            if (metadata.GetGuid(metadata.GetModuleDefinition().Mvid) != a.Assembly.ManifestModule.ModuleVersionId)
                throw new InvalidOperationException($"The file for {a.Assembly.GetName().Name} no longer matches the active editor generation. Rebuild before starting LivePlay.");
            var pdb = Path.ChangeExtension(a.Path, ".pdb");
            return images[a.Assembly] = new(a.Assembly, pe, File.Exists(pdb) ? File.ReadAllBytes(pdb) : []);
        }).ToArray();

    private bool ApplyGeneration(IGameDebuggerTarget remote, AssemblyImage[] generation, Dictionary<Assembly, DebugAssembly> remoteAssemblies)
    {
        var removed = remoteAssemblies.Where(p => !generation.Any(a => a.Assembly == p.Key)).Select(p => p.Value).ToList();
        var added = new Dictionary<Assembly, DebugAssembly>();
        foreach (var image in generation.Where(a => !remoteAssemblies.ContainsKey(a.Assembly)))
        {
            var handle = remote.AssemblyLoadRaw(image.PE, image.PDB);
            if (handle.Equals(DebugAssembly.Empty)) return false;
            added.Add(image.Assembly, handle);
        }
        if (removed.Count == 0 && added.Count == 0) return true;
        if (!remote.AssemblyUpdate(removed, added.Values.ToList())) return false;
        foreach (var old in remoteAssemblies.Keys.Where(a => !generation.Any(g => g.Assembly == a)).ToArray()) remoteAssemblies.Remove(old);
        foreach (var entry in added) remoteAssemblies.Add(entry.Key, entry.Value);
        logger.Info("Applied assembly changes to the running game.");
        return true;
    }

    private void AssembliesReloaded(object? sender, EventArgs e)
    {
        if (!IsRunning || target == null) return;
        try
        {
            var generation = CaptureGeneration();
            var remote = target;
            var currentEpoch = epoch;
            var previous = updateTask;
            var remoteMap = remoteAssemblies;
            updateTask = Task.Run(async () =>
            {
                await previous;
                if (currentEpoch != epoch) return;
                try
                {
                    if (!ApplyGeneration(remote, generation, remoteMap)) logger.Error("LivePlay update failed; the previous game components remain active.");
                }
                catch (Exception exception) { logger.Error("LivePlay connection failed.", exception); }
            });
        }
        catch (Exception exception) { logger.Error("Cannot capture assemblies for LivePlay.", exception); }
    }

    private void SessionClosing(object? sender, EventArgs e) => Stop();

    public void Stop()
    {
        ++epoch;
        playCancellation?.Cancel();
        playCancellation?.Dispose();
        playCancellation = null;
        IsRunning = false;
        target = null;
        if (host != null) host.GameExited -= gameExited;
        gameExited = null;
        // Kill our own child before closing IPC, so a blocked RPC cannot hold editor shutdown.
        if (process != null)
        {
            try { if (!process.HasExited) process.Kill(true); }
            catch (InvalidOperationException) { }
            process.Dispose(); process = null;
        }
        try { pipe?.Close(); }
        catch (IOException) { /* The child may already have exited. */ }
        pipe = null;
        host?.Dispose(); host = null;
        // A running update may still hold these maps; it finishes after its pipe disconnects.
        images.Clear();
    }

    public override void Destroy()
    {
        Stop();
        session.HotReload!.Reloaded -= AssembliesReloaded;
        session.Closing -= SessionClosing;
        session.OutputLog.RemoveLogger(logger);
        base.Destroy();
    }

    private sealed record AssemblyImage(Assembly Assembly, byte[] PE, byte[] PDB);
}
