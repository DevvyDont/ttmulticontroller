using System;
using System.ComponentModel;
using System.Threading;
using System.Windows.Threading;

namespace TTMulti.Threading
{
    /// <summary>
    /// Adapts a WPF <see cref="Dispatcher"/> to <see cref="ISynchronizeInvoke"/>, so the engine's UI-thread
    /// marshalling (WindowWatcher.SynchronizingObject, System.Timers.Timer.SynchronizingObject, the
    /// ToontownController.WindowHandle setter) keeps working unchanged when the shell is WPF instead of a
    /// WinForms Form (which implements ISynchronizeInvoke natively).
    /// </summary>
    internal sealed class DispatcherSynchronizeInvoke : ISynchronizeInvoke
    {
        private readonly Dispatcher _dispatcher;

        public DispatcherSynchronizeInvoke(Dispatcher dispatcher)
        {
            _dispatcher = dispatcher ?? throw new ArgumentNullException(nameof(dispatcher));
        }

        public bool InvokeRequired => !_dispatcher.CheckAccess();

        public IAsyncResult BeginInvoke(Delegate method, object[] args)
        {
            var operation = _dispatcher.BeginInvoke(method, args ?? Array.Empty<object>());
            return new DispatcherAsyncResult(operation);
        }

        public object EndInvoke(IAsyncResult result)
        {
            var asyncResult = (DispatcherAsyncResult)result;
            asyncResult.Operation.Wait();
            return asyncResult.Operation.Result;
        }

        public object Invoke(Delegate method, object[] args)
        {
            return _dispatcher.Invoke(method, args ?? Array.Empty<object>());
        }

        /// <summary>Wraps a DispatcherOperation as the IAsyncResult that ISynchronizeInvoke callers expect.</summary>
        private sealed class DispatcherAsyncResult : IAsyncResult
        {
            private readonly Lazy<ManualResetEvent> _waitHandle;

            internal DispatcherAsyncResult(DispatcherOperation operation)
            {
                Operation = operation;
                _waitHandle = new Lazy<ManualResetEvent>(() =>
                {
                    var handle = new ManualResetEvent(false);
                    if (IsCompleted)
                        handle.Set();
                    else
                        Operation.Completed += (s, e) => handle.Set();
                    return handle;
                });
            }

            internal DispatcherOperation Operation { get; }

            public object AsyncState => null;

            public WaitHandle AsyncWaitHandle => _waitHandle.Value;

            public bool CompletedSynchronously => false;

            public bool IsCompleted => Operation.Status == DispatcherOperationStatus.Completed
                || Operation.Status == DispatcherOperationStatus.Aborted;
        }
    }
}
