// Copyright (c) .NET Foundation and Contributors (https://dotnetfoundation.org/ & https://stride3d.net) and Silicon Studio Corp. (https://www.siliconstudio.co.jp)
// Distributed under the MIT license. See the LICENSE.md file in the project root for more information.

using Stride.Core.Assets.Tracking;
using Stride.Core.Presentation.Commands;
using Stride.Core.Diagnostics;
using System.Threading.Tasks.Dataflow;
using Stride.Core.Presentation.ViewModels;
using Stride.Core.Assets.Presentation.ViewModels;
using Stride.Core.Extensions;
using Stride.Core.Presentation.Collections;
using Stride.Core.IO;
using Stride.Core.Storage;

namespace Stride.Core.Assets.Editor.ViewModels;

public sealed class AssetSourceTrackerViewModel : DispatcherViewModel, IAssetSourceTrackerViewModel
{
    private readonly CancellationTokenSource cts = new();
    private readonly ObservableSet<AssetViewModel> assetsToUpdate = [];
    private bool updating;

    public AssetSourceTrackerViewModel(SessionViewModel session)
        : base(session.ServiceProvider)
    {
        Session = session;
        UpdateAllAssetsWithModifiedSourceCommand = new AnonymousTaskCommand(ServiceProvider, UpdateAllAssetsWithModifiedSource) { IsEnabled = false };

        // This task will listen to the AssetSourceTracker for source file change notifications and collect them
        Dispatcher.InvokeTask(async () =>
        {
            try { await PullSourceFileChangesAsync(cts.Token); }
            catch (OperationCanceledException) when (cts.IsCancellationRequested) { }
        });
    }

    public SessionViewModel Session { get; }

    public ICommandBase UpdateAllAssetsWithModifiedSourceCommand { get; }

    public IReadOnlyObservableCollection<AssetViewModel> AssetsToUpdate => assetsToUpdate;

    private async Task UpdateAllAssetsWithModifiedSource()
    {
        if (updating) return;
        updating = true;
        UpdateAllAssetsWithModifiedSourceCommand.IsEnabled = false;
        try
        {
            using var transaction = Session.ActionService!.CreateTransaction();
            foreach (var asset in assetsToUpdate.ToArray())
            {
                var logger = new LoggerResult();
                try
                {
                    await asset.Sources.UpdateAssetFromSource(logger);
                }
                catch (Exception e)
                {
                    logger.Error($"Unable to update {asset.Url} from source.", e);
                }
                foreach (var message in logger.Messages) GlobalLogger.GetLogger("Asset sources").Log(message);
            }
            Session.ActionService.SetName(transaction, "Update assets from source");
        }
        finally
        {
            updating = false;
            UpdateAllAssetsWithModifiedSourceCommand.IsEnabled = assetsToUpdate.Count > 0;
        }
    }

    /// <inheritdoc/>
    public override void Destroy()
    {
        base.Destroy();
        cts.Cancel();
    }

    public ObjectId GetCurrentHash(UFile file)
    {
        return Session.PackageSession.SourceTracker.GetCurrentHash(file);
    }

    public void UpdateAssetStatus(AssetViewModel asset)
    {
        if (asset.Sources.NeedUpdateFromSource && !asset.IsDeleted)
            assetsToUpdate.Add(asset);
        else
            assetsToUpdate.Remove(asset);

        UpdateAllAssetsWithModifiedSourceCommand.IsEnabled = !updating && assetsToUpdate.Count > 0;
    }

    private async Task PullSourceFileChangesAsync(CancellationToken token)
    {
        var sourceTracker = Session.PackageSession.SourceTracker;
        var bufferBlock = new BufferBlock<IReadOnlyList<SourceFileChangedData>>();
        var changedAssets = new HashSet<AssetViewModel>();
        var sourceFileChanges = new List<SourceFileChangedData>();

        using (sourceTracker.SourceFileChanged.LinkTo(bufferBlock))
        {
            sourceTracker.EnableTracking = true;
            while (!IsDestroyed)
            {
                // Await for source file changes
                await bufferBlock.OutputAvailableAsync(token);
                if (token.IsCancellationRequested)
                    return;

                // Try as much as possible to process all changes at once
                if (!bufferBlock.TryReceiveAll(out var changes))
                    continue;

                sourceFileChanges.AddRange(changes.SelectMany(x => x));
                if (sourceFileChanges.Count == 0)
                    continue;

                for (var i = 0; i < sourceFileChanges.Count; i++)
                {
                    var sourceFileChange = sourceFileChanges[i];
                    // Look first in the assets of the session, then in the list of deleted assets.
                    var asset = Session.GetAssetById(sourceFileChange.AssetId)
                                ?? Session.AllPackages.SelectMany(x => x.DeletedAssets).FirstOrDefault(x => x.Id == sourceFileChange.AssetId);
                    if (asset is null)
                        continue;

                    // We care only about changes that needs to update
                    if (sourceFileChange.NeedUpdate)
                    {
                        switch (sourceFileChange.Type)
                        {
                            case SourceFileChangeType.Asset:
                                // The asset itself has changed and now references a new source
                                asset.Sources.UpdateUsedHashes(sourceFileChange.Files);
                                break;
                            case SourceFileChangeType.SourceFile:
                                // A source file referenced by the asset has changed
                                asset.Sources.ComputeNeedUpdateFromSource();
                                break;
                            default:
                                throw new ArgumentOutOfRangeException();
                        }
                    }
                    // The asset has been updated, remove the change data from the list
                    sourceFileChanges.RemoveAt(i--);

                    if (!asset.IsDeleted)
                    {
                        // We will notify only for undeleted assets
                        changedAssets.Add(asset);
                    }
                }

                // Notify all changed assets at once.
                if (changedAssets.Count > 0)
                {
                    Session.NotifyAssetPropertiesChangedAsync(changedAssets.ToList()).Forget();
                    changedAssets.Clear();
                }
            }
        }
    }
}
