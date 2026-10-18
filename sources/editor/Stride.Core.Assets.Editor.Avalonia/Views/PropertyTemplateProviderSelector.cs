// Copyright (c) .NET Foundation and Contributors (https://dotnetfoundation.org/ & https://stride3d.net)
// Distributed under the MIT license. See the LICENSE.md file in the project root for more information.

using System.Runtime.CompilerServices;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Stride.Core.Presentation.Avalonia.Views;
using Stride.Core.Presentation.Quantum.ViewModels;

namespace Stride.Core.Assets.Editor.Avalonia.Views;

public sealed class PropertyTemplateProviderSelector : TemplateProviderSelector, IRecyclingDataTemplate
{
    private readonly ConditionalWeakTable<NodeViewModel, List<string>> usedProviders = new();
    private readonly ConditionalWeakTable<NodeViewModel, WeakReference> lastControls = new();
    private readonly ConditionalWeakTable<Control, TemplateProviderBase> controlProviders = new();

    public override Control? Build(object? item) => Build(item, null);

    public Control? Build(object? item, Control? existing)
    {
        if (item is NodeViewModel node)
        {
            var usedProvidersForItem = usedProviders.GetOrCreateValue(node);

            var shouldClear = true;
            if (lastControls.TryGetValue(node, out var lastControl) && lastControl.IsAlive)
            {
                shouldClear = false;
            }
            // In any other case, we clear the list of used providers.
            if (shouldClear)
            {
                usedProvidersForItem.Clear();
            }
            lastControls.Remove(node);
            
            var availableSelectors = templateProviders.Where(x => x.Match(item)).ToList();
            var provider = existing != null && controlProviders.TryGetValue(existing, out var recycledProvider)
                && usedProvidersForItem.Contains(recycledProvider.Name) && recycledProvider.Match(item)
                ? recycledProvider : availableSelectors.FirstOrDefault(x => !usedProvidersForItem.Contains(x.Name));
            if (provider != null)
            {
                if (!usedProvidersForItem.Contains(provider.Name)) usedProvidersForItem.Add(provider.Name);
                if (existing != null && controlProviders.TryGetValue(existing, out var previous) && previous == provider)
                {
                    lastControls.Add(node, new WeakReference(existing));
                    return existing;
                }
                var newControl = provider.Template.Build(item);
                if (newControl != null) controlProviders.Add(newControl, provider);
                lastControls.Add(node, new WeakReference(newControl));
                return newControl;
            }

            return null;
        }

        return base.Build(item);
    }
}
