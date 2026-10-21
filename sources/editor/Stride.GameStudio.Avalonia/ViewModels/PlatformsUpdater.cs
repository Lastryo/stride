// Copyright (c) .NET Foundation and Contributors (https://dotnetfoundation.org/ & https://stride3d.net)
// Distributed under the MIT license. See the LICENSE.md file in the project root for more information.

using System.Xml.Linq;
using Stride.Assets;
using Stride.Assets.Templates;
using Stride.Core;
using Stride.Core.Assets;
using Stride.Core.Assets.Templates;
using Stride.Core.Diagnostics;
using Stride.Graphics;

namespace Stride.GameStudio.Avalonia.ViewModels;

/// <summary>
/// Port of the WPF <c>UpdatePlatformsTemplateGenerator</c>: adds/removes the executable projects of a game package.
/// </summary>
public static class PlatformsUpdater
{
    private static string ResolveTemplatePath()
    {
        var local = Path.Combine(AppContext.BaseDirectory, "Templates", "Core", "UpdatePlatforms.sdtpl");
        if (File.Exists(local)) return local;

        var dev = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "Stride.Assets.Presentation.Wpf", "Templates", "Core", "UpdatePlatforms.sdtpl"));
        if (File.Exists(dev)) return dev;

        return local;
    }

    public static bool CanUpdate(SolutionProject? project)
        => project is { Platform: PlatformType.Shared } && project.Package.GetGameSettingsAsset() != null;

    public static IEnumerable<PlatformType> GetExistingPlatforms(SolutionProject gameProject)
        => AssetRegistry.SupportedPlatforms
            .Where(p => gameProject.Session.Projects.OfType<SolutionProject>().Any(x => x.FullPath == ProjectTemplateGeneratorHelper.GeneratePlatformProjectLocation(gameProject.Package.Meta.Name, gameProject.Package, p)))
            .Select(p => p.Type);

    /// <summary>
    /// Generates the selected platform projects and deletes the deselected ones, like WPF "Update platforms".
    /// </summary>
    public static bool Run(SolutionProject gameProject, IReadOnlyCollection<SolutionPlatform> platforms, bool forceRegeneration, LoggerResult logger)
    {
        var package = gameProject.Package;
        var gameSettings = package.GetGameSettingsAsset();
        if (gameSettings == null)
        {
            logger.Error($"Could not find game settings asset at location [{GameSettingsAsset.GameSettingsLocation}]");
            return false;
        }
        var templatePath = ResolveTemplatePath();
        if (!File.Exists(templatePath))
        {
            logger.Error($"Platform templates were not found at [{templatePath}].");
            return false;
        }

        var selected = platforms.Select(x => new SelectedSolutionPlatform(x, x.Templates.FirstOrDefault())).ToList();
        var parameters = new PackageTemplateGeneratorParameters
        {
            Name = package.Meta.Name,
            OutputDirectory = package.FullPath.GetFullDirectory(),
            Description = new TemplateDescription { FullPath = templatePath },
            Package = package,
            Logger = logger,
            Namespace = GetDefaultNamespace(gameProject),
        };
        var orientation = (DisplayOrientation)gameSettings.GetOrCreate<RenderingSettings>().DisplayOrientation;

        // Regenerate the TargetFrameworks of the game library for the selected platforms.
        ProjectTemplateGeneratorHelper.AddOption(parameters, "Platforms", selected.Select(x => x.Platform).ToList());
        ProjectTemplateGeneratorHelper.AddOption(parameters, "CurrentPlatform", PlatformType.Shared);
        ProjectTemplateGeneratorHelper.AddOption(parameters, "CurrentProfile", null!);
        ProjectTemplateGeneratorHelper.AddOption(parameters, "Orientation", orientation);
        var gameTemplate = ProjectTemplateGeneratorHelper.PrepareTemplate(parameters, package, "ProjectLibrary.Game/ProjectLibrary.Game.ttproj", PlatformType.Shared, null, ProjectType.Library);
        var targetFrameworks = gameTemplate.GeneratePart(@"..\Common.TargetFrameworks.targets.t4", logger, ProjectTemplateGeneratorHelper.GetOptions(parameters));
        PatchGameProject(targetFrameworks, gameProject.FullPath.ToOSPath());

        var addedProjects = ProjectTemplateGeneratorHelper.UpdatePackagePlatforms(parameters, selected, orientation, forceRegeneration);
        foreach (var project in addedProjects)
        {
            RestoreProject(project.FullPath.ToOSPath(), logger);
        }
        return !logger.HasErrors;
    }

    public static bool RestoreProject(string projectPath, ILogger? logger = null)
    {
        try
        {
            var psi = new System.Diagnostics.ProcessStartInfo
            {
                FileName = "dotnet",
                ArgumentList = { "restore", projectPath, "--nologo" },
                WorkingDirectory = Path.GetDirectoryName(projectPath)!,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            using var proc = System.Diagnostics.Process.Start(psi);
            if (proc == null) return false;
            proc.WaitForExit();
            if (proc.ExitCode != 0)
            {
                var stderr = proc.StandardError.ReadToEnd();
                logger?.Warning($"Restore warning for {Path.GetFileName(projectPath)}: {stderr.Trim()}");
                return false;
            }
            return true;
        }
        catch (Exception ex)
        {
            logger?.Warning($"Failed to restore {Path.GetFileName(projectPath)}: {ex.Message}");
            return false;
        }
    }

    private static string GetDefaultNamespace(SolutionProject gameProject)
    {
        var defaultNamespace = gameProject.Package.Meta.RootNamespace;
        if (string.IsNullOrWhiteSpace(defaultNamespace))
        {
            try
            {
                defaultNamespace = VSProjectHelper.LoadProject(gameProject.FullPath).GetPropertyValue("RootNamespace");
            }
            catch (Exception)
            {
                // Same fallback as WPF: use the package name below.
            }
        }
        return string.IsNullOrWhiteSpace(defaultNamespace) ? gameProject.Package.Meta.Name.Replace(' ', '_') : defaultNamespace;
    }

    // Copied from WPF UpdatePlatformsTemplateGenerator.PatchGameProject.
    private static void PatchGameProject(string partialContent, string filePath)
    {
        static bool IsTargetFramework(XElement x) => x.Name == "TargetFramework" || x.Name == "TargetFrameworks";

        var patch = XDocument.Parse("<Project><PropertyGroup>" + partialContent + "</PropertyGroup></Project>");
        var document = XDocument.Load(filePath);
        var replaced = false;
        foreach (var property in document.Root!.Elements().Where(x => x.Name.LocalName == "PropertyGroup").SelectMany(x => x.Elements()).ToList())
        {
            if (!IsTargetFramework(property)) continue;
            if (!replaced)
            {
                property.ReplaceWith(patch.Root!.Elements().Elements().Where(IsTargetFramework));
                replaced = true;
            }
            else property.Remove();
        }

        // Ensure Stride.Core.Assets.CompilerApp has buildTransitive so executable projects get asset compilation targets
        var compilerApp = document.Descendants("PackageReference")
            .FirstOrDefault(x => (string?)x.Attribute("Include") == "Stride.Core.Assets.CompilerApp");
        if (compilerApp != null)
        {
            compilerApp.SetAttributeValue("IncludeAssets", "build;buildTransitive");
            compilerApp.Attribute("PrivateAssets")?.Remove();
        }
        else if (document.Root != null)
        {
            var itemGroup = document.Root.Elements("ItemGroup").FirstOrDefault(x => x.Elements("PackageReference").Any())
                ?? document.Root.Elements("ItemGroup").FirstOrDefault();
            if (itemGroup != null)
            {
                var version = StrideConfig.GetLatestPackageDependency().Version;
                itemGroup.Add(new XElement("PackageReference",
                    new XAttribute("Include", "Stride.Core.Assets.CompilerApp"),
                    new XAttribute("Version", version),
                    new XAttribute("IncludeAssets", "build;buildTransitive")));
            }
        }
        document.Save(filePath);
    }
}

