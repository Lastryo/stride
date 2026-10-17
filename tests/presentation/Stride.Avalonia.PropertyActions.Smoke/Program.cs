// Copyright (c) .NET Foundation and Contributors (https://dotnetfoundation.org/ & https://stride3d.net)
// Distributed under the MIT license. See the LICENSE.md file in the project root for more information.

using System.Xml.Linq;
using Avalonia.Controls;
using Avalonia.Xaml.Interactions.Core;
using Stride.Core.Presentation.Avalonia.Controls;

// ChangePropertyAction resolves property names at runtime, beyond AXAML's build checks.
// Replay the actual mixed-value placeholder actions against their production controls.
var templates = XDocument.Load(Path.Combine(AppContext.BaseDirectory, "DefaultPropertyTemplateProviders.axaml"));
var count = 0;
var checkedVector3 = false;
foreach (var trigger in templates.Descendants().Where(e => e.Name.LocalName == "DataTriggerBehavior"
             && ((string?)e.Attribute("Value"))?.Contains("NodeViewModel.DifferentValues") == true))
{
    var behaviors = trigger.Parent;
    if (behaviors?.Name.LocalName != "Interaction.Behaviors")
        continue;
    var target = behaviors.Parent ?? throw new Exception("Behavior has no owning control.");
    if (args.Contains("--vector3-only") && target.Name.LocalName != nameof(Vector3Editor))
        continue;
    var type = target.Name.NamespaceName == "https://github.com/avaloniaui"
        ? typeof(TextBox).Assembly.GetType($"Avalonia.Controls.{target.Name.LocalName}")
        : typeof(Vector3Editor).Assembly.GetType($"Stride.Core.Presentation.Avalonia.Controls.{target.Name.LocalName}");

    foreach (var source in trigger.Elements().Where(e => e.Name.LocalName == "ChangePropertyAction"
                 && ((string?)e.Attribute("PropertyName")) is "Watermark" or "PlaceholderText"
                 && ((string?)e.Attribute("Value")) is "≠" or "×" or "{sd:LocalizeString (Different values)}"))
    {
        var control = Activator.CreateInstance(type ?? throw new Exception($"Unknown control: {target.Name}"))!;
        var propertyName = (string?)source.Attribute("PropertyName") ?? throw new Exception("Missing PropertyName.");
        var placeholder = (string)source.Attribute("Value")!;
        if (placeholder.StartsWith('{'))
            placeholder = "(Different values)";

        var action = new ChangePropertyAction { PropertyName = propertyName, Value = placeholder };
        action.Execute(control, null);
        var property = control.GetType().GetProperty(propertyName);
        if (!Equals(property?.GetValue(control), placeholder))
            throw new Exception($"{target.Name.LocalName}.{propertyName} did not receive its mixed-value placeholder.");

        checkedVector3 |= control is Vector3Editor;
        count++;
    }
}

if (!checkedVector3 || count == 0)
    throw new Exception("The Vector3 mixed-value action must be exercised.");

Console.WriteLine($"PASS: {count} mixed-value actions execute against their production controls, including Vector3Editor.");
