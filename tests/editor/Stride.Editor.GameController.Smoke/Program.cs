// Copyright (c) .NET Foundation and Contributors (https://dotnetfoundation.org/ & https://stride3d.net)
// Distributed under the MIT license. See the LICENSE.md file in the project root for more information.

using Stride.Assets.Editor.GameEditor.Services;

var queue = new Queue<(Func<Task> Callback, int Priority)>();
var dispatcher = new GameThreadDispatcher((callback, priority) => { queue.Enqueue((callback, priority)); }, () => false);
var ran = false;
var result = dispatcher.Invoke(() => { ran = true; return Task.FromResult(42); }, 0, default, false);
if (ran || result.IsCompleted)
    throw new Exception("The callback must run on the game thread, not the caller.");
await queue.Dequeue().Callback();
if (await result != 42)
    throw new Exception("The result was lost.");

var error = new InvalidOperationException("game callback failed");
var failure = dispatcher.Invoke<int>(() => throw error, 0, default, false);
await queue.Dequeue().Callback();
try { await failure; throw new Exception("Callback failures must reach the caller."); }
catch (InvalidOperationException actual) when (ReferenceEquals(actual, error)) { }

using var cancellation = new CancellationTokenSource();
var cancelledRan = false;
var cancelled = dispatcher.Invoke(() => { cancelledRan = true; return Task.FromResult(0); }, 0, cancellation.Token, false);
cancellation.Cancel();
await ExpectCancelled(cancelled);
await queue.Dequeue().Callback();
if (cancelledRan)
    throw new Exception("Cancelled callbacks must not execute.");

var lowPriority = dispatcher.Invoke(() => Task.FromResult(1), int.MaxValue, default, false);
var item = queue.Dequeue();
if (item.Priority != int.MaxValue)
    throw new Exception("The requested microthread priority was lost.");
await item.Callback();
await lowPriority;

var pending = dispatcher.Invoke(() => Task.FromResult(1), 0, default, false);
dispatcher.Stop();
await ExpectCancelled(pending);
await queue.Dequeue().Callback();
await ExpectCancelled(dispatcher.Invoke(() => Task.FromResult(1), 0, default, false));
if (queue.Count != 0)
    throw new Exception("Calls after shutdown must not be queued.");

var inlineDispatcher = new GameThreadDispatcher((_, _) => throw new Exception("Game-thread calls should run inline."), () => true);
if (await inlineDispatcher.Invoke(() => Task.FromResult(7), 0, default, true) != 7)
    throw new Exception("Inline dispatch failed.");
var runningResult = new TaskCompletionSource<int>();
var running = inlineDispatcher.Invoke(() => runningResult.Task, 0, default, true);
inlineDispatcher.Stop();
await ExpectCancelled(running);
runningResult.SetResult(123);

var brokenQueue = new GameThreadDispatcher((_, _) => throw error, () => false);
try { await brokenQueue.Invoke(() => Task.FromResult(0), 0, default, false); throw new Exception("Queue failure was lost."); }
catch (InvalidOperationException actual) when (ReferenceEquals(actual, error)) { }
brokenQueue.Stop();
Console.WriteLine("PASS: dispatch, exception propagation, cancellation, shutdown, priority, inline calls, and queue failure.");

static async Task ExpectCancelled(Task task)
{
    try { await task.WaitAsync(TimeSpan.FromSeconds(2)); throw new Exception("Expected cancellation."); }
    catch (OperationCanceledException) { }
}
