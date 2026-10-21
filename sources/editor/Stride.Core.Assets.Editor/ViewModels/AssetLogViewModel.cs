// Copyright (c) .NET Foundation and Contributors (https://dotnetfoundation.org/ & https://stride3d.net) and Silicon Studio Corp. (https://www.siliconstudio.co.jp)
// Distributed under the MIT license. See the LICENSE.md file in the project root for more information.

using System.Collections.Specialized;
using Stride.Core.Assets.Analysis;
using Stride.Core.Assets.Editor.Services;
using Stride.Core.Diagnostics;
using Stride.Core.Presentation.Collections;
using Stride.Core.Presentation.ViewModels;

namespace Stride.Core.Assets.Editor.ViewModels;

public sealed class AssetLogViewModel : LoggerViewModel
{
    private readonly SessionViewModel session;
    private readonly Dictionary<LogKey, Logger> loggers = [];
    private int refreshVersion;
    private int errorCount;

    public AssetLogViewModel(IViewModelServiceProvider serviceProvider, SessionViewModel session)
        : base(serviceProvider)
    {
        MinLevel = LogMessageType.Warning;
        this.session = session;
        FilteredMessages = [];
        session.AssetCollection.SelectedAssets.CollectionChanged += FilterSourceChanged;
        Messages.CollectionChanged += FilterSourceChanged;

        // Listen if IBuildService gets registered
        ServiceProvider.ServiceRegistered += ServiceRegistered;
        // Initial check, in case it is already registered
        CheckBuildService();
    }

    /// <inheritdoc />
    public override void Destroy()
    {
        refreshVersion++;
        session.AssetCollection.SelectedAssets.CollectionChanged -= FilterSourceChanged;
        Messages.CollectionChanged -= FilterSourceChanged;
        ServiceProvider.ServiceRegistered -= ServiceRegistered;
        if (ServiceProvider.TryGet<IBuildService>() is { } service)
        {
            service.AssetBuilt -= AssetBuilt;
        }
        base.Destroy();
    }

    public ObservableList<ILogMessage> FilteredMessages { get; private set; }

    public int ErrorCount { get { return errorCount; } set { SetValue(ref errorCount, value); } }

    public void AddLogger(LogKey key, Logger logger)
    {
        loggers.Add(key, logger);
        AddLogger(logger);
    }
    
    /// <inheritdoc />
    public override void AddLogger(Logger logger)
    {
        base.AddLogger(logger);
        if (logger is LoggerResult loggerResult)
        {
            var messages = (ObservableList<ILogMessage>)Messages;
            Loggers[logger].AddRange(loggerResult.Messages);
            messages.AddRange(loggerResult.Messages);
        }
    }

    public bool RemoveLogger(LogKey key)
    {
        if (GetLogger(key, false) is { } logger)
        {
            ClearMessages(logger);
            RemoveLogger(logger);
            loggers.Remove(key);
            RefreshFilteredMessages();
            return true;
        }
        return false;
    }

    public Logger? GetLogger(LogKey key, bool create = true)
    {
        if (!loggers.TryGetValue(key, out var logger) && create)
        {
            logger = GlobalLogger.GetLogger(key.ToString());
            AddLogger(key, logger);
        }
        return logger;
    }

    public void ClearMessages(LogKey key)
    {
        var logger = GetLogger(key, false);
        if (logger != null)
        {
            ClearMessages(logger);
        }
    }
    
    private void AssetBuilt(object? sender, AssetBuiltEventArgs e)
    {
        Dispatcher.InvokeAsync(() =>
        {
            if (!IsDestroyed && session.GetAssetById(e.AssetItem.Id) is not null)
            {
                var key = LogKey.Get(e.AssetItem.Id, "Build");
                ClearMessages(key);
                RemoveLogger(key);
                AddLogger(key, e.BuildLog);
            }
        });
    }
    
    private void CheckBuildService()
    {
        if (ServiceProvider.TryGet<IBuildService>() is { } service)
        {
            service.AssetBuilt += AssetBuilt;
            ServiceProvider.ServiceRegistered -= ServiceRegistered;
        }
    }

    private void FilterSourceChanged(object? sender, NotifyCollectionChangedEventArgs e) => RefreshFilteredMessages();

    private async void RefreshFilteredMessages()
    {
        var version = ++refreshVersion;
        await Task.Delay(200);
        if (IsDestroyed || version != refreshVersion) return;

        var selection = session.AssetCollection.SelectedAssets.ToList();
        var ids = await Task.Run(() =>
        {
            var result = new HashSet<AssetId>();
            foreach (var asset in selection)
            {
                result.Add(asset.Id);
                var dependencyManager = asset.AssetItem.Package.Session.DependencyManager;
                var dependencies = dependencyManager.ComputeDependencies(asset.AssetItem.Id, AssetDependencySearchOptions.Out | AssetDependencySearchOptions.Recursive, ContentLinkType.All);
                if (dependencies != null)
                {
                    foreach (var reference in dependencies.LinksOut)
                        result.Add(reference.Item.Id);
                }
            }
            return result;
        });
        if (IsDestroyed || version != refreshVersion) return;

        var selectedLoggers = loggers.Where(x => selection.Count == 0 || x.Key.AssetId == AssetId.Empty || ids.Contains(x.Key.AssetId));
        var messages = selectedLoggers.SelectMany(x => Loggers[x.Value])
            .Where(x => x.IsAtLeast(LogMessageType.Warning)).ToList();
        FilteredMessages.Clear();
        FilteredMessages.AddRange(messages);
        ErrorCount = messages.Count;
    }

    private void ServiceRegistered(object? sender, ServiceRegistrationEventArgs e)
    {
        CheckBuildService();
    }
}
