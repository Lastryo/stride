// Copyright (c) .NET Foundation and Contributors (https://dotnetfoundation.org/ & https://stride3d.net)
// Distributed under the MIT license. See the LICENSE.md file in the project root for more information.

using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Controls.Templates;
using Stride.Core.Presentation.Avalonia.Views;
using Stride.Core.Presentation.Quantum.ViewModels;

namespace Stride.Core.Assets.Editor.Avalonia.Views;

public sealed class PropertyTemplateProviderSelector : TemplateProviderSelector, IRecyclingDataTemplate
{
    private readonly PropertyTemplateProviderSelector? rootSelector;
    private readonly HashSet<string>? ignoredProviders;
    private readonly ConditionalWeakTable<Control, TemplateProviderBase> controlProviders;

    public PropertyTemplateProviderSelector()
    {
        controlProviders = new ConditionalWeakTable<Control, TemplateProviderBase>();
    }

    private PropertyTemplateProviderSelector(PropertyTemplateProviderSelector root, HashSet<string> ignored)
    {
        rootSelector = root;
        ignoredProviders = ignored;
        controlProviders = root.controlProviders;
    }

    public override bool Match(object? data)
    {
        var effectiveRoot = rootSelector ?? this;
        return effectiveRoot.templateProviders.Any(x =>
            (ignoredProviders == null || !ignoredProviders.Contains(x.Name)) && x.Match(data));
    }

    public override Control? Build(object? item) => Build(item, null);

    public Control? Build(object? item, Control? existing)
    {
        var effectiveRoot = rootSelector ?? this;

        if (item is NodeViewModel node)
        {
            var providers = effectiveRoot.templateProviders;

            var provider = existing != null && controlProviders.TryGetValue(existing, out var recycledProvider)
                && (ignoredProviders == null || !ignoredProviders.Contains(recycledProvider.Name))
                && recycledProvider.Match(item)
                ? recycledProvider
                : providers.FirstOrDefault(x => (ignoredProviders == null || !ignoredProviders.Contains(x.Name)) && x.Match(item));

            if (provider != null)
            {
                if (existing != null && controlProviders.TryGetValue(existing, out var previous) && previous == provider)
                {
                    return existing;
                }

                var newControl = provider.Template.Build(item);
                if (newControl != null)
                {
                    controlProviders.AddOrUpdate(newControl, provider);

                    var nextIgnored = ignoredProviders != null
                        ? new HashSet<string>(ignoredProviders) { provider.Name }
                        : new HashSet<string> { provider.Name };

                    ConfigureChildContentControls(newControl, effectiveRoot, nextIgnored);
                }

                return newControl;
            }

            return null;
        }

        return effectiveRoot.templateProviders.FirstOrDefault(x => x.Match(item))?.Template.Build(item);
    }

    private static void ConfigureChildContentControls(Control root, PropertyTemplateProviderSelector rootSelector, HashSet<string> ignored)
    {
        var queue = new Queue<Control>();
        queue.Enqueue(root);

        while (queue.Count > 0)
        {
            var current = queue.Dequeue();

            if (current is ContentControl cc && (ReferenceEquals(cc.ContentTemplate, rootSelector) ||
                (cc.ContentTemplate is PropertyTemplateProviderSelector childSel && ReferenceEquals(childSel.rootSelector, rootSelector))))
            {
                cc.ContentTemplate = new PropertyTemplateProviderSelector(rootSelector, ignored);
                continue;
            }

            if (current is ContentPresenter cp && (ReferenceEquals(cp.ContentTemplate, rootSelector) ||
                (cp.ContentTemplate is PropertyTemplateProviderSelector childSel2 && ReferenceEquals(childSel2.rootSelector, rootSelector))))
            {
                cp.ContentTemplate = new PropertyTemplateProviderSelector(rootSelector, ignored);
                continue;
            }

            if (current is Panel panel)
            {
                foreach (var child in panel.Children)
                {
                    queue.Enqueue(child);
                }
            }
            else if (current is Decorator decorator && decorator.Child != null)
            {
                queue.Enqueue(decorator.Child);
            }
            else if (current is ContentControl contentControl && contentControl.Content is Control contentChild)
            {
                queue.Enqueue(contentChild);
            }
        }
    }
}
