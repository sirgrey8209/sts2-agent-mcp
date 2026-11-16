using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading.Tasks;
using Godot;

namespace Sts2AgentMcp.Host;

/// <summary>
/// Runs work on the Godot main thread. All game state access must go through here.
/// Every ProcessFrame: drain the whole work queue, then evaluate frame waiters.
/// </summary>
internal static class MainThread
{
    private static readonly ConcurrentQueue<Action> Queue = new();
    private static readonly List<IFrameWaiter> Waiters = new(); // main thread only

    public static long Frame { get; private set; }

    public static void Install()
    {
        var tree = (SceneTree)Engine.GetMainLoop();
        tree.Connect(SceneTree.SignalName.ProcessFrame, Callable.From(Pump));
    }

    private static void Pump()
    {
        Frame++;

        // Drain everything queued so far. Items enqueued while draining run next frame.
        int count = Queue.Count;
        for (int i = 0; i < count && Queue.TryDequeue(out var work); i++)
        {
            try { work(); }
            catch (Exception ex) { Log.Error($"main-thread work failed: {ex}"); }
        }

        for (int i = Waiters.Count - 1; i >= 0; i--)
        {
            bool done;
            try { done = Waiters[i].Tick(); }
            catch (Exception ex)
            {
                Log.Error($"frame waiter failed: {ex}");
                Waiters[i].Fail(ex);
                done = true;
            }
            if (done) Waiters.RemoveAt(i);
        }
    }

    public static Task<T> Run<T>(Func<T> func)
    {
        var tcs = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        Queue.Enqueue(() =>
        {
            try { tcs.SetResult(func()); }
            catch (Exception ex) { tcs.SetException(ex); }
        });
        return tcs.Task;
    }

    public static Task Run(Action action) => Run(() => { action(); return true; });

    /// <summary>
    /// Evaluates <paramref name="step"/> once per frame on the main thread until it returns a non-null result
    /// or the timeout expires (then <paramref name="onTimeout"/> provides the result).
    /// </summary>
    public static Task<T> Poll<T>(Func<T?> step, TimeSpan timeout, Func<T> onTimeout) where T : class
    {
        var waiter = new FrameWaiter<T>(step, DateTime.UtcNow + timeout, onTimeout);
        Queue.Enqueue(() => Waiters.Add(waiter));
        return waiter.Task;
    }

    private interface IFrameWaiter
    {
        bool Tick();
        void Fail(Exception ex);
    }

    private sealed class FrameWaiter<T> : IFrameWaiter where T : class
    {
        private readonly Func<T?> _step;
        private readonly DateTime _deadline;
        private readonly Func<T> _onTimeout;
        private readonly TaskCompletionSource<T> _tcs = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public FrameWaiter(Func<T?> step, DateTime deadline, Func<T> onTimeout)
        {
            _step = step;
            _deadline = deadline;
            _onTimeout = onTimeout;
        }

        public Task<T> Task => _tcs.Task;

        public bool Tick()
        {
            var result = _step();
            if (result != null)
            {
                _tcs.TrySetResult(result);
                return true;
            }
            if (DateTime.UtcNow >= _deadline)
            {
                _tcs.TrySetResult(_onTimeout());
                return true;
            }
            return false;
        }

        public void Fail(Exception ex) => _tcs.TrySetException(ex);
    }
}
