using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;

namespace TTMulti
{
    /// <summary>
    /// Global watcher that notifies when watched windows are activated, moved, resized, change show state, or close.
    /// Windows must be added to the watch list to be notified.
    ///
    /// Event-driven via SetWinEventHook (foreground / location-change / minimize / destroy) instead of a polling
    /// timer: no per-tick Win32 calls when nothing is happening, and OUT_OF_CONTEXT callbacks are delivered on the
    /// UI thread (the thread that installed the hooks), so all watcher state and events stay on the UI thread.
    /// </summary>
    class WindowWatcher
    {
        public static WindowWatcher Instance { get; } = new WindowWatcher();

        /// <summary>A watched window was activated or deactivated</summary>
        public event EventHandler<Events.WindowActivatedEventArgs> ActiveWindowChanged;

        /// <summary>A watched window was closed</summary>
        public event EventHandler<Events.WindowClosedEventArgs> WindowClosed;

        /// <summary>The client area size of a watched window was changed</summary>
        public event EventHandler<Events.WindowClientAreaSizeChangedEventArgs> WindowClientAreaSizeChanged;

        /// <summary>The client area location of a watched window was moved</summary>
        public event EventHandler<Events.WindowClientAreaLocationChangedEventArgs> WindowClientAreaLocationChanged;

        /// <summary>The show state (minimized, normal, maximized) of a watched window was changed</summary>
        public event EventHandler<Events.WindowShowStateChangedEventArgs> WindowShowStateChanged;

        private ISynchronizeInvoke _synchronizingObject;

        /// <summary>
        /// The UI thread's synchronizing object. Assigning it (on the UI thread, at startup) installs the WinEvent
        /// hooks so their callbacks are delivered on that thread. Also used by other components to marshal to the UI.
        /// </summary>
        public ISynchronizeInvoke SynchronizingObject
        {
            get => _synchronizingObject;
            set
            {
                _synchronizingObject = value;
                InstallHooks();
            }
        }

        private class WindowInfo
        {
            public Size ClientAreaSize { get; set; }
            public Point ClientAreaScreenLocation { get; set; }
            public Win32.ShowWindowCommands ShowState { get; set; }

            public WindowInfo(Size clientAreaSize, Point clientAreaScreenLocation, Win32.ShowWindowCommands showState)
            {
                ClientAreaSize = clientAreaSize;
                ClientAreaScreenLocation = clientAreaScreenLocation;
                ShowState = showState;
            }
        }

        private readonly HashSet<IntPtr> watchedWindowHandles = new HashSet<IntPtr>();
        private readonly Dictionary<IntPtr, WindowInfo> lastWindowInfos = new Dictionary<IntPtr, WindowInfo>();
        private IntPtr lastActiveWindowHandle = IntPtr.Zero;

        // Kept alive for the lifetime of the hooks so the unmanaged callback is not garbage-collected.
        private Win32.WinEventDelegate _winEventProc;
        private readonly List<IntPtr> _hookHandles = new List<IntPtr>();
        private bool _hooksInstalled;

        private WindowWatcher() { }

        private void InstallHooks()
        {
            if (_hooksInstalled)
                return;

            _winEventProc = WinEventProc;

            // Narrow, precise hooks minimize how often the (system-wide) callback is invoked for events we ignore.
            AddHook(Win32.EVENT_SYSTEM_FOREGROUND, Win32.EVENT_SYSTEM_FOREGROUND);
            AddHook(Win32.EVENT_SYSTEM_MINIMIZESTART, Win32.EVENT_SYSTEM_MINIMIZEEND);
            AddHook(Win32.EVENT_OBJECT_DESTROY, Win32.EVENT_OBJECT_DESTROY);
            AddHook(Win32.EVENT_OBJECT_LOCATIONCHANGE, Win32.EVENT_OBJECT_LOCATIONCHANGE);

            _hooksInstalled = true;
        }

        private void AddHook(uint eventMin, uint eventMax)
        {
            IntPtr h = Win32.SetWinEventHook(eventMin, eventMax, IntPtr.Zero, _winEventProc, 0, 0, Win32.WINEVENT_OUTOFCONTEXT);
            if (h != IntPtr.Zero)
                _hookHandles.Add(h);
        }

        /// <summary>Uninstall the WinEvent hooks (call on app shutdown so no callbacks arrive during teardown).</summary>
        public void Shutdown()
        {
            foreach (IntPtr h in _hookHandles)
                Win32.UnhookWinEvent(h);
            _hookHandles.Clear();
            _hooksInstalled = false;
        }

        /// <summary>Add a window handle to be notified when it is moved, resized, minimized, activated, or closed.</summary>
        public void WatchWindow(IntPtr windowHandle)
        {
            if (windowHandle == IntPtr.Zero || !watchedWindowHandles.Add(windowHandle))
                return;

            // Seed initial state and fire the initial events immediately (the poll used to do this on its next tick).
            SeedWindow(windowHandle);
        }

        /// <summary>Stop notifications for a window.</summary>
        public void StopWatchingWindow(IntPtr windowHandle)
        {
            watchedWindowHandles.Remove(windowHandle);
            lastWindowInfos.Remove(windowHandle);
        }

        private void WinEventProc(IntPtr hWinEventHook, uint eventType, IntPtr hwnd,
            int idObject, int idChild, uint dwEventThread, uint dwmsEventTime)
        {
            // Only the window object itself, not child objects / caret / cursor, etc.
            if (idObject != Win32.OBJID_WINDOW || idChild != Win32.CHILDID_SELF)
                return;

            switch (eventType)
            {
                case Win32.EVENT_SYSTEM_FOREGROUND:
                    HandleForegroundChanged(hwnd);
                    break;

                case Win32.EVENT_OBJECT_DESTROY:
                    if (watchedWindowHandles.Contains(hwnd))
                        HandleWindowClosed(hwnd);
                    break;

                case Win32.EVENT_OBJECT_LOCATIONCHANGE:
                case Win32.EVENT_SYSTEM_MINIMIZESTART:
                case Win32.EVENT_SYSTEM_MINIMIZEEND:
                    if (watchedWindowHandles.Contains(hwnd))
                        RefreshWindowInfo(hwnd);
                    break;
            }
        }

        private void HandleForegroundChanged(IntPtr activeWindowHandle)
        {
            if (activeWindowHandle == lastActiveWindowHandle)
                return;

            // Only notify when a watched window gained or lost the foreground.
            if (watchedWindowHandles.Contains(lastActiveWindowHandle) || watchedWindowHandles.Contains(activeWindowHandle))
                ActiveWindowChanged?.Invoke(this, new Events.WindowActivatedEventArgs(lastActiveWindowHandle, activeWindowHandle));

            lastActiveWindowHandle = activeWindowHandle;
        }

        private void HandleWindowClosed(IntPtr windowHandle)
        {
            WindowClosed?.Invoke(this, new Events.WindowClosedEventArgs(windowHandle));
            watchedWindowHandles.Remove(windowHandle);
            lastWindowInfos.Remove(windowHandle);
        }

        private void SeedWindow(IntPtr windowHandle)
        {
            if (!TryGetWindowState(windowHandle, out Size size, out Point location, out Win32.ShowWindowCommands showState))
                return;

            lastWindowInfos[windowHandle] = new WindowInfo(size, location, showState);

            WindowShowStateChanged?.Invoke(this, new Events.WindowShowStateChangedEventArgs(windowHandle, Win32.ShowWindowCommands.Hide, showState));
            WindowClientAreaSizeChanged?.Invoke(this, new Events.WindowClientAreaSizeChangedEventArgs(windowHandle, Size.Empty, size));
            WindowClientAreaLocationChanged?.Invoke(this, new Events.WindowClientAreaLocationChangedEventArgs(windowHandle, Point.Empty, location));
        }

        private void RefreshWindowInfo(IntPtr windowHandle)
        {
            if (!TryGetWindowState(windowHandle, out Size size, out Point location, out Win32.ShowWindowCommands showState))
            {
                if (!Win32.IsWindow(windowHandle))
                    HandleWindowClosed(windowHandle);
                return;
            }

            if (!lastWindowInfos.TryGetValue(windowHandle, out WindowInfo last))
            {
                SeedWindow(windowHandle);
                return;
            }

            if (last.ShowState != showState)
            {
                WindowShowStateChanged?.Invoke(this, new Events.WindowShowStateChangedEventArgs(windowHandle, last.ShowState, showState));
                last.ShowState = showState;
            }

            if (last.ClientAreaSize != size)
            {
                WindowClientAreaSizeChanged?.Invoke(this, new Events.WindowClientAreaSizeChangedEventArgs(windowHandle, last.ClientAreaSize, size));
                last.ClientAreaSize = size;
            }

            if (last.ClientAreaScreenLocation != location)
            {
                WindowClientAreaLocationChanged?.Invoke(this, new Events.WindowClientAreaLocationChangedEventArgs(windowHandle, last.ClientAreaScreenLocation, location));
                last.ClientAreaScreenLocation = location;
            }
        }

        private static bool TryGetWindowState(IntPtr windowHandle, out Size size, out Point location, out Win32.ShowWindowCommands showState)
        {
            size = Size.Empty;
            location = Point.Empty;
            showState = Win32.ShowWindowCommands.Hide;

            if (!Win32.IsWindow(windowHandle))
                return false;

            try
            {
                size = Win32.GetWindowClientAreaSize(windowHandle);
                location = Win32.GetWindowClientAreaLocation(windowHandle);
                showState = Win32.GetWindowShowState(windowHandle);
            }
            catch
            {
                return false;
            }

            // The window may have closed between the IsWindow check and reading its info.
            return Win32.IsWindow(windowHandle);
        }
    }
}
