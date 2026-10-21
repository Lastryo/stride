// Copyright (c) .NET Foundation and Contributors (https://dotnetfoundation.org/ & https://stride3d.net)
// Distributed under the MIT license. See the LICENSE.md file in the project root for more information.

using System.Diagnostics;
using Microsoft.VisualBasic.FileIO;

namespace Stride.Core.Assets.Editor.Services;

internal static class FileTrash
{
    public static void Move(string path)
    {
        try
        {
            if ((File.GetAttributes(path) & FileAttributes.Directory) != 0) throw new IOException($"Expected an asset file at '{path}', but found a directory.");
        }
        catch (FileNotFoundException) { return; }
        catch (DirectoryNotFoundException) { return; }
        if (OperatingSystem.IsWindows())
        {
            FileSystem.DeleteFile(path, UIOption.OnlyErrorDialogs, RecycleOption.SendToRecycleBin, UICancelOption.ThrowException);
            return;
        }
        if (!OperatingSystem.IsLinux())
            throw new PlatformNotSupportedException("Moving asset files to the system trash is not supported on this platform.");

        var start = new ProcessStartInfo("gio") { UseShellExecute = false, RedirectStandardError = true, CreateNoWindow = true };
        start.ArgumentList.Add("trash");
        start.ArgumentList.Add("--");
        start.ArgumentList.Add(Path.GetFullPath(path));
        using var process = Process.Start(start) ?? throw new IOException("Unable to start the system trash service (gio).");
        var error = process.StandardError.ReadToEndAsync();
        if (!process.WaitForExit(30_000))
        {
            process.Kill();
            throw new IOException($"Moving '{path}' to the system trash timed out.");
        }
        // Never fall back to permanent deletion when the trash is unavailable.
        if (process.ExitCode != 0) throw new IOException($"Unable to move '{path}' to the system trash: {error.GetAwaiter().GetResult()}");
    }
}
