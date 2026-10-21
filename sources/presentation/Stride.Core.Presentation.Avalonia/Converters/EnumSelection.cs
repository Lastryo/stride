// Copyright (c) .NET Foundation and Contributors (https://dotnetfoundation.org/ & https://stride3d.net)
// Distributed under the MIT license. See the LICENSE.md file in the project root for more information.

using System.Globalization;
using Avalonia.Data;

namespace Stride.Core.Presentation.Avalonia.Converters;

public sealed class EnumSelection : ValueConverterBase<EnumSelection>
{
    public override object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) => value;

    // ComboBox clears its selection while its items or data context are being replaced.
    public override object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is Enum ? value : BindingOperations.DoNothing;
}
