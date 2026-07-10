using System;
using System.Threading;
using System.Windows.Threading;
using TTMulti.Threading;
using Xunit;

namespace TTMulti.Tests
{
    /// <summary>
    /// Exercises the Dispatcher-backed ISynchronizeInvoke adapter on a dedicated dispatcher thread — the shape
    /// the WPF shell uses for WindowWatcher.SynchronizingObject and System.Timers.Timer.SynchronizingObject.
    /// </summary>
    public class DispatcherSynchronizeInvokeTests : IDisposable
    {
        private readonly Thread _dispatcherThread;
        private readonly Dispatcher _dispatcher;
        private readonly DispatcherSynchronizeInvoke _sync;

        public DispatcherSynchronizeInvokeTests()
        {
            Dispatcher dispatcher = null;
            using (var ready = new ManualResetEventSlim())
            {
                _dispatcherThread = new Thread(() =>
                {
                    dispatcher = Dispatcher.CurrentDispatcher;
                    ready.Set();
                    Dispatcher.Run();
                })
                { IsBackground = true };
                _dispatcherThread.SetApartmentState(ApartmentState.STA);
                _dispatcherThread.Start();
                ready.Wait(5000);
            }
            _dispatcher = dispatcher;
            _sync = new DispatcherSynchronizeInvoke(_dispatcher);
        }

        public void Dispose()
        {
            _dispatcher.InvokeShutdown();
        }

        [Fact]
        public void InvokeRequired_is_true_off_thread_false_on_thread()
        {
            Assert.True(_sync.InvokeRequired);

            bool onThreadValue = (bool)_sync.Invoke(
                new Func<bool>(() => _sync.InvokeRequired), null);
            Assert.False(onThreadValue);
        }

        [Fact]
        public void Invoke_runs_on_dispatcher_thread_and_returns_result()
        {
            int dispatcherThreadId = (int)_sync.Invoke(
                new Func<int>(() => Thread.CurrentThread.ManagedThreadId), null);
            Assert.Equal(_dispatcherThread.ManagedThreadId, dispatcherThreadId);
        }

        [Fact]
        public void BeginInvoke_EndInvoke_completes_and_returns_result()
        {
            IAsyncResult ar = _sync.BeginInvoke(
                new Func<string>(() => "done-" + Thread.CurrentThread.ManagedThreadId), null);
            object result = _sync.EndInvoke(ar);
            Assert.Equal("done-" + _dispatcherThread.ManagedThreadId, result);
            Assert.True(ar.IsCompleted);
        }

        [Fact]
        public void AsyncWaitHandle_signals_on_completion()
        {
            IAsyncResult ar = _sync.BeginInvoke(new Action(() => Thread.Sleep(50)), null);
            Assert.True(ar.AsyncWaitHandle.WaitOne(5000), "wait handle never signalled");
        }

        [Fact]
        public void TimersTimer_with_SynchronizingObject_fires_on_dispatcher_thread()
        {
            // The engine's switching-mode timer (R3) relies on exactly this interaction.
            int elapsedThreadId = 0;
            using (var fired = new ManualResetEventSlim())
            using (var timer = new System.Timers.Timer(20) { AutoReset = false })
            {
                timer.SynchronizingObject = _sync;
                timer.Elapsed += (s, e) =>
                {
                    elapsedThreadId = Thread.CurrentThread.ManagedThreadId;
                    fired.Set();
                };
                timer.Start();
                Assert.True(fired.Wait(5000), "timer never fired");
            }
            Assert.Equal(_dispatcherThread.ManagedThreadId, elapsedThreadId);
        }
    }
}
