// Copyright (c) .NET Foundation and Contributors (https://dotnetfoundation.org/ & https://stride3d.net)
// Distributed under the MIT license. See the LICENSE.md file in the project root for more information.

namespace Stride.Assets.Editor.GameEditor.Services;

/// <summary>Completes queued calls even if their callback fails or the game stops before executing them.</summary>
internal sealed class GameThreadDispatcher(Action<Func<Task>, int> enqueue, Func<bool> checkAccess)
{
    private readonly CancellationTokenSource stopped = new();

    public void Stop() => stopped.Cancel();

    public Task<T> Invoke<T>(Func<Task<T>> callback, int priority, CancellationToken token, bool inline)
    {
        if (stopped.IsCancellationRequested || token.IsCancellationRequested)
            return Task.FromCanceled<T>(new CancellationToken(true));

        var completion = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        var cancellation = CancellationTokenSource.CreateLinkedTokenSource(stopped.Token, token);
        var registration = cancellation.Token.Register(() => completion.TrySetCanceled(cancellation.Token));
        _ = completion.Task.ContinueWith(_ => { registration.Dispose(); cancellation.Dispose(); },
            CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);

        async Task Run()
        {
            if (completion.Task.IsCompleted)
                return;
            try
            {
                completion.TrySetResult(await callback());
            }
            catch (OperationCanceledException exception)
            {
                completion.TrySetCanceled(exception.CancellationToken);
            }
            catch (Exception exception)
            {
                completion.TrySetException(exception);
            }
        }

        try
        {
            if (inline && checkAccess())
                _ = Run();
            else
                enqueue(Run, priority);
        }
        catch (Exception exception)
        {
            completion.TrySetException(exception);
        }
        return completion.Task;
    }
}
