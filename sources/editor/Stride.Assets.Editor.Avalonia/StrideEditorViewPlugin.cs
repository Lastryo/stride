// Copyright (c) .NET Foundation and Contributors (https://dotnetfoundation.org/ & https://stride3d.net)
// Distributed under the MIT license. See the LICENSE.md file in the project root for more information.

using System.Reflection;
using global::Avalonia.Threading;
using Stride.Assets.Editor.Avalonia.Views;
using Stride.Assets.Editor.Avalonia.GameEditor;
using Stride.Core.Assets.Editor;
using Stride.Core.Assets.Editor.Services;
using Stride.Core.Assets.Presentation.ViewModels;
using Stride.Core.Diagnostics;
using Stride.Core.Presentation.Avalonia.Views;
using Stride.Core.Presentation.Views;
using Stride.Editor.Annotations;
using Stride.Editor.Avalonia.Preview;
using Stride.Editor.Preview;
using Stride.Editor.Preview.Views;

namespace Stride.Assets.Editor.Avalonia;

public sealed class StrideEditorViewPlugin : AssetsEditorPlugin
{
    private static bool resourcesRegistered;

    public override void InitializePlugin(ILogger logger)
    {
        // Note: InitializePlugin is called on a background thread during session loading.
        // UI resources (AvaloniaObject) must be registered on the UI thread (see RegisterTemplateProviders).
    }

    internal static void EnsureResourcesRegistered()
    {
        if (resourcesRegistered)
            return;

        if (Dispatcher.UIThread.CheckAccess())
        {
            RegisterResources();
        }
        else
        {
            Dispatcher.UIThread.Invoke(RegisterResources);
        }

        static void RegisterResources()
        {
            if (resourcesRegistered)
                return;

            resourcesRegistered = true;
            foreach (var (key, value) in new ImageDictionary())
            {
                if (key is Type type && value is not null)
                {
                    TypeImages[type] = value;
                }
            }
        }
    }

    public override void InitializeSession(ISessionViewModel session)
    {
        session.ServiceProvider.RegisterService(new EditorGameViewFactory());
        if (session is Stride.Core.Assets.Editor.ViewModels.SessionViewModel curveSession)
        {
            var windows = new Dictionary<object, global::Avalonia.Controls.Window>(ReferenceEqualityComparer.Instance);
            curveSession.ActiveProperties.RegisterNodePresenterCommand(new Stride.Assets.Editor.CurveEditor.EditCurveCommand((curve, name) =>
            {
                if (windows.TryGetValue(curve, out var existing)) { existing.Activate(); return; }
                var model = new Stride.Assets.Editor.CurveEditor.CurveEditorViewModel(curveSession, curve, name);
                var view = new CurveEditor.CurveEditorView(model);
                var window = new global::Avalonia.Controls.Window { Title = "Curve Editor — " + name, Width = 1100, Height = 650, Content = view };
                windows.Add(curve, window);
                window.Closed += (_, _) => { windows.Remove(curve); view.Detach(); model.Destroy(); };
                var owner = (global::Avalonia.Application.Current?.ApplicationLifetime as global::Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime)?.MainWindow;
                if (owner != null) window.Show(owner); else window.Show();
            }));
            curveSession.Closing += (_, _) => { foreach (var window in windows.Values.ToArray()) window.Close(); };
        }
        var pluginService = session.ServiceProvider.Get<IAssetsPluginService>();
        var previewFactories = new Dictionary<Type, AssetPreviewFactory>();
        foreach (var stridePlugin in pluginService.Plugins.OfType<AssetsEditorPlugin>())
        {
            var pluginAssembly = stridePlugin.GetType().Assembly;
            foreach (var type in pluginAssembly.GetTypes())
            {
                if (typeof(IAssetPreview).IsAssignableFrom(type) &&
                    type.GetCustomAttribute<AssetPreviewAttribute>() is { } attribute)
                {
                    previewFactories.Add(attribute.AssetType, (_, _, _) => (IAssetPreview)Activator.CreateInstance(type)!);
                }
            }
        }

        var previewService = new GameStudioPreviewService(session);
        previewService.RegisterAssetPreviewFactories(previewFactories);
        session.ServiceProvider.RegisterService(previewService);
    }

    public override void RegisterAssetPreviewViewModelTypes(IDictionary<Type, Type> assetPreviewViewModelTypes)
    {
        // nothing for now
    }

    public override void RegisterAssetPreviewViewTypes(IDictionary<Type, Type> assetPreviewViewTypes)
    {
        var pluginAssembly = GetType().Assembly;
        foreach (var type in pluginAssembly.GetTypes())
        {
            if (typeof(IPreviewView).IsAssignableFrom(type))
            {
                foreach (var attribute in type.GetCustomAttributes<AssetPreviewViewAttribute>())
                {
                    assetPreviewViewTypes.Add(attribute.AssetPreviewType, type);
                }
            }
        }
    }

    public override void RegisterPrimitiveTypes(ICollection<Type> primitiveTypes)
    {
        // nothing for now
    }

    public override void RegisterTemplateProviders(ICollection<ITemplateProvider> templateProviders)
    {
        EnsureResourcesRegistered();

        foreach (var (_, value) in new EntityPropertyTemplateProviders())
        {
            if (value is TemplateProviderBase provider)
            {
                templateProviders.Add(provider);
            }
        }
    }
}
