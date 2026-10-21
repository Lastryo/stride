// Copyright (c) .NET Foundation and Contributors (https://dotnetfoundation.org/ & https://stride3d.net)
// Distributed under the MIT license. See the LICENSE.md file in the project root for more information.

using Stride.Core;
using Stride.Core.Assets;
using Stride.Core.Presentation.ViewModels;

namespace Stride.GameStudio.Avalonia.ViewModels;

internal sealed class PlatformItemViewModel : ViewModelBase
{
    private bool isSelected;

    public PlatformItemViewModel(IViewModelServiceProvider serviceProvider, SolutionPlatform platform, bool isInstalled)
        : base(serviceProvider)
    {
        Platform = platform;
        IsInstalled = isInstalled;
        isSelected = isInstalled;
    }

    public SolutionPlatform Platform { get; }

    public string Name => Platform.DisplayName ?? Platform.Name;

    public PlatformType Type => Platform.Type;

    public bool CanBeUnselected => Type != PlatformType.Windows;

    public bool IsInstalled { get; }

    public bool IsSelected
    {
        get => isSelected;
        set => SetValue(ref isSelected, value, () => OnPropertyChanged(nameof(MarkedToRemove)));
    }

    public bool MarkedToRemove => !IsSelected && IsInstalled;
}

internal sealed class UpdatePlatformsViewModel : ViewModelBase
{
    private bool forcePlatformRegeneration;

    public UpdatePlatformsViewModel(IViewModelServiceProvider serviceProvider, IEnumerable<PlatformType> installedPlatforms)
        : base(serviceProvider)
    {
        var installedSet = installedPlatforms.ToHashSet();
        Platforms = AssetRegistry.SupportedPlatforms
            .Select(p => new PlatformItemViewModel(serviceProvider, p, installedSet.Contains(p.Type)))
            .ToList();
    }

    public List<PlatformItemViewModel> Platforms { get; }

    public bool ForcePlatformRegeneration
    {
        get => forcePlatformRegeneration;
        set => SetValue(ref forcePlatformRegeneration, value);
    }

    public IEnumerable<SolutionPlatform> SelectedPlatforms => Platforms.Where(x => x.IsSelected).Select(x => x.Platform);
}

