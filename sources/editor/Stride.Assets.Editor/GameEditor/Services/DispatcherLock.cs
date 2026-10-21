// Copyright (c) .NET Foundation and Contributors (https://dotnetfoundation.org/ & https://stride3d.net) and Silicon Studio Corp. (https://www.siliconstudio.co.jp)
// Distributed under the MIT license. See the LICENSE.md file in the project root for more information.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Stride.Core.Annotations;
using Stride.Core.Extensions;
using Stride.Core.Presentation.Services;

namespace Stride.Assets.Editor.GameEditor.Services;

/// <summary>
/// An object that allows to lock several dispatchers at the same time to perform an operation in a thread-safe way.
/// </summary>
public sealed class DispatcherLock : IDisposable
{
    private struct DispatcherState
    {
        public DispatcherState(IDispatcherService dispatcher)
        {
            Dispatcher = dispatcher ?? throw new ArgumentNullException(nameof(dispatcher));
            if (dispatcher.CheckAccess())
                throw new InvalidOperationException("A dispatcher lock must be created from a different thread than the dispatchers it should lock.");
            Locked = new TaskCompletionSource<int>();
            Unlocked = new TaskCompletionSource<int>();
        }

        public readonly IDispatcherService Dispatcher;
        public readonly TaskCompletionSource<int> Locked;
        public readonly TaskCompletionSource<int> Unlocked;
    }

    private readonly List<DispatcherState> dispatcherStates;

    private DispatcherLock(IEnumerable<IDispatcherService> dispatchers)
    {
        dispatcherStates = dispatchers.Select(x => new DispatcherState(x)).ToList();
    }

    public void Dispose()
    {
        foreach (var dispatcher in dispatcherStates)
        {
            dispatcher.Unlocked.TrySetResult(0);
        }
    }

    public static Task<DispatcherLock> Lock(params IDispatcherService[] dispatchers)
    {
        return Lock(false, dispatchers);
    }

    public static async Task<DispatcherLock> Lock(bool lockSequentially, params IDispatcherService[] dispatchers)
    {
        ArgumentNullException.ThrowIfNull(dispatchers);

        var lok = new DispatcherLock(dispatchers);
        foreach (var dispatcher in lok.dispatcherStates)
        {
            dispatcher.Dispatcher.InvokeAsync(() =>
            {
                dispatcher.Locked.TrySetResult(0);
                dispatcher.Unlocked.Task.Wait();
            }).Forget();

            if (lockSequentially)
            {
                await dispatcher.Locked.Task;
            }
        }

        await Task.WhenAll(lok.dispatcherStates.Select(x => (Task)x.Locked.Task));
        return lok;
    }
}

