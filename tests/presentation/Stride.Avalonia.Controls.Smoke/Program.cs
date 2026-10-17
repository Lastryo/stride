// Copyright (c) .NET Foundation and Contributors (https://dotnetfoundation.org/ & https://stride3d.net)
// Distributed under the MIT license. See the LICENSE.md file in the project root for more information.

using Stride.Core.Presentation.Avalonia.Controls;

// Run in separate processes to exercise both static property registration orders.
if (args.Contains("--timespan-first"))
    _ = new TimeSpanEditor();
else
    _ = new DateTimeEditor();

var date = new DateTimeEditor { PlaceholderText = "Date" };
var duration = new TimeSpanEditor { PlaceholderText = "Duration" };

if (TimeSpanEditor.PlaceholderTextProperty.OwnerType != typeof(TimeSpanEditor))
    throw new Exception("TimeSpanEditor must own its placeholder property.");
if (DateTimeEditor.PlaceholderTextProperty.OwnerType != typeof(DateTimeEditor))
    throw new Exception("DateTimeEditor must own its placeholder property.");
if (date.PlaceholderText != "Date" || duration.PlaceholderText != "Duration")
    throw new Exception("Placeholder values must be independent.");

Console.WriteLine("PASS: date and duration controls initialize and keep independent placeholders.");
