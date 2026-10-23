// Copyright (c) .NET Foundation and Contributors (https://dotnetfoundation.org/ & https://stride3d.net)
// Distributed under the MIT license. See the LICENSE.md file in the project root for more information.

using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.VisualTree;
using Avalonia.Controls.Recycling;
using Avalonia.Controls.Templates;
using Dock.Model.Core;

namespace Stride.GameStudio.Avalonia.Layout;

public sealed class DockContentTemplate : IDataTemplate
{
    private readonly ControlRecyclingDataTemplate recycling = new();

    public bool Match(object? data) => data is IDockable { Context: Control };

    public Control? Build(object? data)
    {
        if (data is not IDockable { Context: Control view }) return null;
        // A closed floating window can leave its deferred presenter holding the view.
        if (view.GetVisualParent() is ContentPresenter presenter)
        {
            presenter.SetCurrentValue(ContentPresenter.ContentProperty, null);
            presenter.UpdateChild();
        }
        return recycling.Build(view);
    }
}
