using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Windows.Forms;
using System.Threading;
using System.Runtime.InteropServices;
using System.Drawing;
using TTMulti.Forms;

namespace TTMulti
{
    enum ControllerType
    {
        Left,
        Right
    }

    class ToontownController
    {
        /// <summary>
        /// The controlled window was activated
        /// </summary>
        public event EventHandler WindowActivated;

        /// <summary>
        /// The controlled window was deactivated
        /// </summary>
        public event EventHandler WindowDeactivated;

        /// <summary>
        /// The controlled window handle was changed
        /// </summary>
        public event EventHandler WindowHandleChanged;

        /// <summary>
        /// This controller should be activated (due to a mouse click)
        /// </summary>
        public event EventHandler ShouldActivate;

        /// <summary>Keys currently posted DOWN (not yet UP) to the game window, so they can be released on demand.</summary>
        readonly HashSet<Keys> _heldKeys = new HashSet<Keys>();

        IntPtr _windowHandle;

        /// <summary>
        /// The handle of the window being controlled.
        /// TODO: make sure the handle does not belong to a utility window of a controller.
        /// </summary>
        public IntPtr WindowHandle
        {
            get => _windowHandle;
            set
            {
                // Setting the handle cascades into the border window (WinForms) and the WindowWatcher list, both of
                // which are UI-thread affine.  The validation/keep-alive timers fire on ThreadPool threads, so marshal
                // the whole mutation to the UI thread (matching WindowWatcher's own SynchronizingObject) — CORR-07.
                var sync = WindowWatcher.Instance.SynchronizingObject;
                if (sync != null && sync.InvokeRequired)
                {
                    sync.BeginInvoke(new Action(() => WindowHandle = value), null);
                    return;
                }

                if (_windowHandle != value)
                {
                    // If we're removing the window handle, reset caption color to default before disconnecting
                    if (value == IntPtr.Zero && _windowHandle != IntPtr.Zero && Properties.Settings.Default.enableCaptionColor)
                    {
                        Win32.SetWindowCaptionColor(_windowHandle, null);
                    }
                    
                    if (_windowHandle != IntPtr.Zero)
                    {
                        WindowWatcher.Instance.StopWatchingWindow(_windowHandle);
                    }
                    
                    _windowHandle = value;
                    _captionColorApplied = false; // new window: force caption color re-apply (PERF-05 guard)

                    if (_windowHandle != IntPtr.Zero)
                    {
                        WindowWatcher.Instance.WatchWindow(_windowHandle);
                        // Border position will be updated by WindowWatcher events
                        // Force an immediate update to ensure borders are correct
                        UpdateBorderPosition();
                    }

                    WindowHandleChanged?.Invoke(this, EventArgs.Empty);

                    Refresh();
                }
            }
        }

        public bool HasWindow => WindowHandle != IntPtr.Zero;

        public Size WindowSize { get; private set; }

        /// <summary>
        /// The top-left of the game window's client area in screen coordinates.
        /// Kept in sync by WindowWatcher — no Win32 call needed at read time.
        /// </summary>
        public Point WindowClientAreaLocation => _borderWnd.Location;

        /// <summary>
        /// True when the game window is visible (not minimized).
        /// The border window is hidden whenever the game window is minimized.
        /// </summary>
        public bool IsBorderVisible => _borderWnd.Visible;

        /// <summary>
        /// Set true while the trigger-release multiclick key/button is held so that
        /// Refresh() does not clear the fake cursor during activation events.
        /// </summary>
        internal bool IsTriggerReleaseCursorActive = false;

        public bool ShowFakeCursor
        {
            get => _borderWnd.ShowFakeCursor;
            set
            {
                if (_borderWnd.ShowFakeCursor != value)
                {
                    _borderWnd.ShowFakeCursor = value;
                }
            }
        }

        public Point FakeCursorPosition
        {
            get => _borderWnd.FakeCursorPosition;
            set => _borderWnd.FakeCursorPosition = value;
        }

        /// <summary>
        /// Atomically update fake cursor visibility and position in a single repaint.
        /// </summary>
        public void UpdateFakeCursor(bool show, Point position)
            => _borderWnd.UpdateFakeCursor(show, position);

        /// <summary>
        /// Whether the controlled window's size is mismatched 
        /// </summary>
        public bool IsWindowSizeMismatched
        {
            get => _borderWnd.FakeCursorIsInvalid;
            set => _borderWnd.FakeCursorIsInvalid = value;
        }

        public int GroupNumber
        {
            get => _borderWnd.GroupNumber;
            set => _borderWnd.GroupNumber = value;
        }

        public int PairNumber { get; set; }

        private bool _isWindowActive = false;
        public bool IsWindowActive
        {
            get => _isWindowActive;
            private set
            {
                if (_isWindowActive != value)
                {
                    _isWindowActive = value;

                    if (_isWindowActive)
                    {
                        WindowActivated?.Invoke(this, EventArgs.Empty);
                    }
                    else
                    {
                        WindowDeactivated?.Invoke(this, EventArgs.Empty);
                    }
                }
            }
        }

        public ControllerType Type { get; }

        public bool ErrorOccurredPostingMessage { get; private set; } = false;

        BorderWnd _borderWnd = new BorderWnd();

        /// <summary>The border overlay window (exposed so Multicontroller's switching-mode display can set its
        /// state directly instead of reaching in via reflection) — PERF-09 / CORR-10.</summary>
        internal BorderWnd BorderWindow => _borderWnd;

        // This class is needed to queue background operations because handles can only be accessed on the UI thread
        private struct ReorderUtilityWindowsState
        {
            public IntPtr BorderWindowHandle;
        }

        // Coalesces the background z-order reorder. A single mode/setting/activation change fans out to Refresh()
        // many times per controller, and each visible-border Refresh would otherwise queue its own DeferWindowPos
        // batch against the slow Toontown window. 0 = idle, 1 = a reorder is already queued/running. (PERF-05)
        private int _reorderPending;

        // Timer to send keep-alive key presses
        System.Timers.Timer keepAliveTimer = new System.Timers.Timer()
        {
            AutoReset = false,
            Interval = 60000
        };

        // Timer to periodically validate window handles and detect ghost windows
        System.Timers.Timer windowValidationTimer = new System.Timers.Timer()
        {
            AutoReset = true,
            Interval = 1000 // Check every second
        };

        Multicontroller multicontroller = Multicontroller.Instance;

        public ToontownController(int groupNumber, int pairNumber, ControllerType type)
        {
            GroupNumber = groupNumber;
            PairNumber = pairNumber;
            Type = type;

            WindowWatcher.Instance.ActiveWindowChanged += WindowWatcher_ActiveWindowChanged;
            WindowWatcher.Instance.WindowClosed += WindowWatcher_WindowClosed;
            WindowWatcher.Instance.WindowClientAreaLocationChanged += WindowWatcher_WindowClientAreaLocationChanged;
            WindowWatcher.Instance.WindowClientAreaSizeChanged += WindowWatcher_WindowClientAreaSizeChanged;
            WindowWatcher.Instance.WindowShowStateChanged += WindowWatcher_WindowShowStateChanged;

            multicontroller.ModeChanged += Multicontroller_ModeChanged;
            multicontroller.ActiveControllersChanged += Multicontroller_ActiveControllersChanged;
            multicontroller.ActiveChanged += Multicontroller_ActiveChanged;
            multicontroller.SettingChanged += Multicontroller_SettingChanged;

            Properties.Settings.Default.PropertyChanged += Settings_PropertyChanged;


            keepAliveTimer.Elapsed += KeepAliveTimer_Elapsed;
            windowValidationTimer.Elapsed += WindowValidationTimer_Elapsed;
            windowValidationTimer.Start();
        }

        private void Multicontroller_SettingChanged(object sender, EventArgs e)
        {
            Refresh();
        }

        private void Multicontroller_ActiveChanged(object sender, EventArgs e)
        {
            Refresh();
        }

        private void Multicontroller_ActiveControllersChanged(object sender, EventArgs e)
        {
            Refresh();
        }

        private void Multicontroller_ModeChanged(object sender, EventArgs e)
        {
            Refresh();
        }

        private void Settings_PropertyChanged(object sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            Refresh();
        }

        private void KeepAliveTimer_Elapsed(object sender, System.Timers.ElapsedEventArgs e)
        {
            if (!Properties.Settings.Default.disableKeepAlive
                && Properties.Settings.Default.keepAliveKeyCode != (int)Keys.None
                && HasWindow)
            {
                // Well-formed key lParams: a zero lParam clears the key-up transition bit and is misread as a
                // keypress by in-game chat (see Multicontroller.ReleaseMovementKeysOnControllers) — WIN32-03.
                Keys keepAliveKey = (Keys)Properties.Settings.Default.keepAliveKeyCode;
                PostMessage(Win32.WM.KEYDOWN, (IntPtr)keepAliveKey, Win32.MakePostedKeyLParam(keepAliveKey, false));
                Thread.Sleep(50);
                PostMessage(Win32.WM.KEYUP, (IntPtr)keepAliveKey, Win32.MakePostedKeyLParam(keepAliveKey, true));

                keepAliveTimer.Start();
            }
        }

        private void WindowValidationTimer_Elapsed(object sender, System.Timers.ElapsedEventArgs e)
        {
            // Periodically validate that the window handle is still valid
            if (HasWindow && !Win32.IsWindow(WindowHandle))
            {
                // Window is no longer valid - disconnect the controller
                WindowHandle = IntPtr.Zero;
            }
        }

        private void WindowWatcher_WindowShowStateChanged(object sender, Events.WindowShowStateChangedEventArgs e)
        {
            if (HasWindow && WindowHandle == e.WindowHandle)
            {
                switch (e.ShowState)
                {
                    case Win32.ShowWindowCommands.ShowMinimized:
                        _borderWnd.Hide();
                        break;
                    default:
                        // TODO: why is this needed?
                        _borderWnd.WindowState = FormWindowState.Normal;
                        break;
                }

                Refresh();
            }
        }

        private void WindowWatcher_WindowClientAreaSizeChanged(object sender, Events.WindowClientAreaSizeChangedEventArgs e)
        {
            if (HasWindow && WindowHandle == e.WindowHandle)
            {
                _borderWnd.Size = WindowSize = e.ClientAreaSize;
                
                // Clear switched controllers when window is resized (but not on initial detection)
                if (!e.PreviousClientAreaSize.IsEmpty)
                {
                    multicontroller.ClearSwitchedControllers();
                }
            }
        }

        private void WindowWatcher_WindowClientAreaLocationChanged(object sender, Events.WindowClientAreaLocationChangedEventArgs e)
        {
            if (HasWindow && WindowHandle == e.WindowHandle)
            {
                _borderWnd.Location = e.ClientAreaLocation;
            }
        }

        private void WindowWatcher_WindowClosed(object sender, Events.WindowClosedEventArgs e)
        {
            // Check if this closed window matches our window handle
            // Use a local copy to avoid race conditions if WindowHandle changes
            IntPtr currentWindowHandle = WindowHandle;
            
            if (e.ClosedWindowHandle == currentWindowHandle && currentWindowHandle != IntPtr.Zero)
            {
                // Verify the window is actually closed before disconnecting
                // This prevents issues if the event fires but the window is still valid
                if (!Win32.IsWindow(currentWindowHandle))
                {
                    WindowHandle = IntPtr.Zero;
                }
            }
        }

        private void WindowWatcher_ActiveWindowChanged(object sender, Events.WindowActivatedEventArgs e)
        {
            if (!HasWindow)
            {
                return;
            }

            if (e.ActiveWindowHandle == WindowHandle)
            {
                IsWindowActive = true;
            }
            else if (e.PreviousActiveWindowHandle == WindowHandle)
            {
                IsWindowActive = false;
            }
        }

        /// <summary>
        /// Darkens a color by multiplying RGB values by a factor (0.0 to 1.0).
        /// </summary>
        private static Color DarkenColor(Color color, float factor)
        {
            int r = (int)(color.R * factor);
            int g = (int)(color.G * factor);
            int b = (int)(color.B * factor);
            return Color.FromArgb(color.A, Math.Max(0, Math.Min(255, r)), Math.Max(0, Math.Min(255, g)), Math.Max(0, Math.Min(255, b)));
        }

        // Guards redundant cross-process DWM caption-color updates during refresh storms (PERF-05).
        // Reset whenever WindowHandle changes so a new window always gets its caption color re-applied.
        private bool _captionColorApplied;
        private Color? _appliedCaptionColor;

        private void ApplyCaptionColor(Color? color)
        {
            if (_captionColorApplied && _appliedCaptionColor == color)
                return;
            Win32.SetWindowCaptionColor(WindowHandle, color);
            _captionColorApplied = true;
            _appliedCaptionColor = color;
        }

        /// <summary>
        /// Refresh settings of the controller and its utility windows
        /// </summary>
        internal void Refresh()
        {
            bool isActiveController = multicontroller.IsActiveController(this);

            bool showBorderWindow = HasWindow && (
                (multicontroller.IsActive && (isActiveController || multicontroller.ShowAllBorders || multicontroller.IsSwitchingMode))
                || (multicontroller.IsControlledMulticlickMode && isActiveController));

            bool showMouseOverlayWindow = false; // Feature removed

            if (showBorderWindow && !_borderWnd.Visible)
            {
                // Sync position from Win32 before making the border visible so it
                // never flashes at a stale location from the previous poll tick.
                UpdateBorderPosition();
                _borderWnd.Show();
                // Re-apply position after Show() creates the HWND, because WinForms
                // only forwards Location sets to SetWindowPos once the handle exists.
                UpdateBorderPosition();
            }
            else if (!showBorderWindow && _borderWnd.Visible)
            {
                _borderWnd.Hide();
            }

            // Update caption color based on border visibility
            if (Properties.Settings.Default.enableCaptionColor && HasWindow)
            {
                if (showBorderWindow)
                {
                    // TODO: why is this needed?
                    _borderWnd.WindowState = FormWindowState.Normal;

                    Color borderColor;

                    // Switching mode colors (handled by Multicontroller)
                    if (_borderWnd.SwitchingMode)
                    {
                        // Priority: Selected > Marked for Removal > Switched > Normal
                        if (_borderWnd.SwitchingSelected)
                        {
                            borderColor = Colors.SwitchingSelected;
                        }
                        else if (_borderWnd.SwitchingMarkedForRemoval)
                        {
                            borderColor = Colors.SwitchingMarkedForRemoval;
                        }
                        else if (_borderWnd.SwitchingSwitched)
                        {
                            borderColor = Colors.SwitchingSwitched;
                        }
                        else
                        {
                            borderColor = Colors.SwitchingMode;
                        }
                    }
                    else
                    {
                        // Normal mode - set border colors based on mode
                        _borderWnd.ShowGroupNumber = multicontroller.IsActive
                            && (multicontroller.ShowAllBorders || multicontroller.ControllerGroups.Count > 1);

                        if (multicontroller.ShowAllBorders && multicontroller.IsActive)
                        {
                            borderColor = multicontroller.CurrentMode == MulticontrollerMode.Custom
                                ? multicontroller.GetActiveCustomModeBorderColorFor(Type)
                                : (Type == ControllerType.Left ? Colors.LeftGroup : Colors.RightGroup);
                        }
                        else if (multicontroller.IsActive)
                        {
                            switch (multicontroller.CurrentMode)
                            {
                                case MulticontrollerMode.Group:
                                case MulticontrollerMode.AllGroup:
                                    borderColor = Type == ControllerType.Left ? Colors.LeftGroup : Colors.RightGroup;
                                    break;
                                case MulticontrollerMode.MirrorAll:
                                    borderColor = Colors.AllGroups;
                                    break;
                                case MulticontrollerMode.Custom:
                                    borderColor = multicontroller.GetActiveCustomModeBorderColorFor(Type);
                                    break;
                                case MulticontrollerMode.Focused:
                                    // Use different colors for focused vs unfocused windows
                                    if (multicontroller.IsFocusedController(this))
                                    {
                                        borderColor = Colors.FocusedFocused;
                                    }
                                    else
                                    {
                                        borderColor = Colors.FocusedUnfocused;
                                    }
                                    break;
                                default:
                                    borderColor = _borderWnd.BorderColor; // Keep current color
                                    break;
                            }
                        }
                        else
                        {
                            borderColor = _borderWnd.BorderColor; // Keep current color
                        }
                    }
                    
                    // Set border color
                    _borderWnd.BorderColor = borderColor;
                    
                    // Darken the border color for caption (make it slightly darker)
                    Color captionColor = DarkenColor(borderColor, 0.75f);
                    ApplyCaptionColor(captionColor);

                    // Don't clear fake cursors while trigger-release is active or in controlled MC mode
                    if (!IsTriggerReleaseCursorActive && !multicontroller.IsControlledMulticlickMode)
                        _borderWnd.ShowFakeCursor = false;
                }
                else
                {
                    // No border shown - reset caption color to default
                    ApplyCaptionColor(null);
                }
            }
            else if (showBorderWindow)
            {
                // TODO: why is this needed?
                _borderWnd.WindowState = FormWindowState.Normal;

                Color borderColor;
                
                // Switching mode colors (handled by Multicontroller)
                if (_borderWnd.SwitchingMode)
                {
                    // Priority: Selected > Marked for Removal > Switched > Normal
                    if (_borderWnd.SwitchingSelected)
                    {
                        borderColor = Colors.SwitchingSelected;
                    }
                    else if (_borderWnd.SwitchingMarkedForRemoval)
                    {
                        borderColor = Colors.SwitchingMarkedForRemoval;
                    }
                    else if (_borderWnd.SwitchingSwitched)
                    {
                        borderColor = Colors.SwitchingSwitched;
                    }
                    else
                    {
                        borderColor = Colors.SwitchingMode;
                    }
                }
                else
                {
                    // Normal mode - set border colors based on mode
                    _borderWnd.ShowGroupNumber = multicontroller.IsActive
                        && (multicontroller.ShowAllBorders || multicontroller.ControllerGroups.Count > 1);

                    if (multicontroller.ShowAllBorders && multicontroller.IsActive)
                    {
                        borderColor = multicontroller.CurrentMode == MulticontrollerMode.Custom
                            ? multicontroller.GetActiveCustomModeBorderColorFor(Type)
                            : (Type == ControllerType.Left ? Colors.LeftGroup : Colors.RightGroup);
                    }
                    else if (multicontroller.IsActive)
                    {
                        switch (multicontroller.CurrentMode)
                        {
                            case MulticontrollerMode.Group:
                            case MulticontrollerMode.AllGroup:
                                borderColor = Type == ControllerType.Left ? Colors.LeftGroup : Colors.RightGroup;
                                break;
                            case MulticontrollerMode.MirrorAll:
                                borderColor = Colors.AllGroups;
                                break;
                            case MulticontrollerMode.Custom:
                                borderColor = multicontroller.GetActiveCustomModeBorderColorFor(Type);
                                break;
                            case MulticontrollerMode.Focused:
                                // Use different colors for focused vs unfocused windows
                                if (multicontroller.IsFocusedController(this))
                                {
                                    borderColor = Colors.FocusedFocused;
                                }
                                else
                                {
                                    borderColor = Colors.FocusedUnfocused;
                                }
                                break;
                            default:
                                borderColor = _borderWnd.BorderColor; // Keep current color
                                break;
                        }
                    }
                    else
                    {
                        borderColor = _borderWnd.BorderColor; // Keep current color
                    }
                }
                
                // Set border color
                _borderWnd.BorderColor = borderColor;

                // Don't clear fake cursors while trigger-release is active or in controlled MC mode
                if (!IsTriggerReleaseCursorActive && !multicontroller.IsControlledMulticlickMode)
                    _borderWnd.ShowFakeCursor = false;
            }

            if (showBorderWindow && Interlocked.CompareExchange(ref _reorderPending, 1, 0) == 0)
            {
                // Queue utility window reordering on background threads because operations involving a Toontown window
                // seem to take significantly longer and freeze the UI. Only queue when no reorder is already pending;
                // the proc clears the flag as soon as it starts, so a Refresh arriving after that re-queues and the
                // latest z-order is still applied — we only drop redundant duplicates from the same burst. (PERF-05)
                ThreadPool.QueueUserWorkItem(ReorderUtilityWindowsProc, new ReorderUtilityWindowsState
                {
                    BorderWindowHandle = _borderWnd.Handle
                });
            }

            if ((Properties.Settings.Default.disableKeepAlive || !HasWindow) && keepAliveTimer.Enabled)
            {
                keepAliveTimer.Stop();
            }
            else if (!Properties.Settings.Default.disableKeepAlive && HasWindow && !keepAliveTimer.Enabled)
            {
                keepAliveTimer.Start();
            }

            // Window validation timer should always run if we have a window
            if (!HasWindow && windowValidationTimer.Enabled)
            {
                windowValidationTimer.Stop();
            }
            else if (HasWindow && !windowValidationTimer.Enabled)
            {
                windowValidationTimer.Start();
            }
        }

        private void ReorderUtilityWindowsProc(object state)
        {
            // Clear the coalescing flag first so any Refresh() after this point queues a fresh reorder, guaranteeing
            // the final z-order is applied even though redundant mid-burst duplicates were dropped. (PERF-05)
            Interlocked.Exchange(ref _reorderPending, 0);

            /*
            * Order the windows in the following z-order:
            * 1 - border window
            * 2 - Toontown window
            *
            * The border window is above the multicontroller window at first, so we move it underneath the Toontown
            * window first, then bring the Toontown window under it, to keep the multicontroller window from ending
            * up in the back.
            */

            // Validate window is still valid before reordering
            if (!HasWindow || !Win32.IsWindow(WindowHandle))
            {
                // Window is no longer valid - disconnect the controller
                // Note: We can't directly set WindowHandle here as we're on a background thread,
                // but the validation timer will catch it soon
                return;
            }

            ReorderUtilityWindowsState handles = (ReorderUtilityWindowsState)state;

            IntPtr wndPosInfo = Win32.BeginDeferWindowPos(2);

            // Move border window immediately underneath the Toontown window in the z-order
            wndPosInfo = Win32.DeferWindowPos(wndPosInfo, handles.BorderWindowHandle, WindowHandle, 0, 0, 0, 0, Win32.SetWindowPosFlags.DoNotActivate | Win32.SetWindowPosFlags.IgnoreMove | Win32.SetWindowPosFlags.IgnoreResize);

            // Move Toontown window immediately underneath the border window in the z-order
            wndPosInfo = Win32.DeferWindowPos(wndPosInfo, WindowHandle, handles.BorderWindowHandle, 0, 0, 0, 0, Win32.SetWindowPosFlags.DoNotActivate | Win32.SetWindowPosFlags.IgnoreMove | Win32.SetWindowPosFlags.IgnoreResize);

            Win32.EndDeferWindowPos(wndPosInfo);
        }

        /// <summary>
        /// Force update border window position and size to match the current window
        /// </summary>
        internal void UpdateBorderPosition()
        {
            if (!HasWindow)
                return;

            // Validate window is still valid before accessing it
            if (!Win32.IsWindow(WindowHandle))
            {
                // Window is no longer valid - disconnect the controller
                WindowHandle = IntPtr.Zero;
                return;
            }

            Win32.RECT clientRect;
            if (Win32.GetClientRect(WindowHandle, out clientRect))
            {
                Size clientSize = new Size(clientRect.Right - clientRect.Left, clientRect.Bottom - clientRect.Top);
                WindowSize = clientSize;
                
                Point clientLocation = Point.Empty;
                if (Win32.ClientToScreen(WindowHandle, ref clientLocation))
                {
                    _borderWnd.Size = clientSize;
                    _borderWnd.Location = clientLocation;
                }
            }
            else
            {
                // GetClientRect failed - window might be closing
                // Verify with IsWindow
                if (!Win32.IsWindow(WindowHandle))
                {
                    // Window closed - disconnect the controller
                    WindowHandle = IntPtr.Zero;
                }
            }
        }

        /// <summary>Keys that must never be posted to game windows (OS captures / Start menu, etc.).</summary>
        static bool IsOsKeyBlockedFromGameForwarding(Win32.WM msg, IntPtr wParam)
        {
            switch (msg)
            {
                case Win32.WM.KEYDOWN:
                case Win32.WM.KEYUP:
                case Win32.WM.SYSKEYDOWN:
                case Win32.WM.SYSKEYUP:
                    int vk = (int)wParam.ToInt64() & 0xFFFF;
                    return vk == (int)Keys.LWin || vk == (int)Keys.RWin;
                default:
                    return false;
            }
        }

        /// <summary>
        /// Post a message asynchronously to the Toontown window
        /// </summary>
        public void PostMessage(Win32.WM msg, IntPtr wParam, IntPtr lParam)
        {
            if (IsOsKeyBlockedFromGameForwarding(msg, wParam))
                return;

            if (WindowHandle != IntPtr.Zero)
            {
                // Validate window is still valid before posting
                if (!Win32.IsWindow(WindowHandle))
                {
                    // Window is no longer valid - disconnect the controller
                    WindowHandle = IntPtr.Zero;
                    return;
                }

                if (!Win32.PostMessage(WindowHandle, (uint)msg, wParam, lParam))
                {
                    // PostMessage failed - check if window is still valid
                    // If window is invalid, disconnect; otherwise just mark error
                    if (!Win32.IsWindow(WindowHandle))
                    {
                        // Window closed - disconnect the controller
                        WindowHandle = IntPtr.Zero;
                    }
                    else
                    {
                        ErrorOccurredPostingMessage = true;
                    }
                }
                else
                {
                    TrackHeldKey(msg, wParam);
                }
            }
        }

        /// <summary>
        /// Record/clear a forwarded key's held state so <see cref="ReleaseAllHeldKeys"/> can release it later.
        /// Locks because the keep-alive timer posts from a ThreadPool thread while forwarding runs on the UI thread.
        /// </summary>
        void TrackHeldKey(Win32.WM msg, IntPtr wParam)
        {
            Keys key = (Keys)wParam & Keys.KeyCode;
            if (key == Keys.None)
                return;

            lock (_heldKeys)
            {
                if (msg == Win32.WM.KEYDOWN || msg == Win32.WM.SYSKEYDOWN)
                    _heldKeys.Add(key);
                else if (msg == Win32.WM.KEYUP || msg == Win32.WM.SYSKEYUP)
                    _heldKeys.Remove(key);
            }
        }

        /// <summary>
        /// Posts KEYUP for every key currently held down in the game window and clears the held set.  Used when the
        /// routing changes (mode / group / pair) so a key held across the change doesn't stay stuck (CORR-05).
        /// </summary>
        public void ReleaseAllHeldKeys()
        {
            Keys[] snapshot;
            lock (_heldKeys)
            {
                if (_heldKeys.Count == 0)
                    return;
                snapshot = new Keys[_heldKeys.Count];
                _heldKeys.CopyTo(snapshot);
                _heldKeys.Clear();
            }

            foreach (Keys key in snapshot)
                PostMessage(Win32.WM.KEYUP, (IntPtr)key, Win32.MakePostedKeyLParam(key, true));
        }

        public void Shutdown()
        {
            // Clear the handle first so any in-flight keep-alive tick sees HasWindow == false and neither posts
            // nor reschedules itself.  Then stop both timers.  Without this a removed controller kept firing the
            // 60s keep-alive into the live game window and left a stale handle in the WindowWatcher poll list (CORR-06).
            if (_windowHandle != IntPtr.Zero)
            {
                if (Properties.Settings.Default.enableCaptionColor)
                    Win32.SetWindowCaptionColor(_windowHandle, null);
                WindowWatcher.Instance.StopWatchingWindow(_windowHandle);
                _windowHandle = IntPtr.Zero;
            }

            keepAliveTimer.Stop();
            keepAliveTimer.Dispose();

            windowValidationTimer.Stop();
            windowValidationTimer.Dispose();

            _borderWnd.Close();

            WindowWatcher.Instance.ActiveWindowChanged -= WindowWatcher_ActiveWindowChanged;
            WindowWatcher.Instance.WindowClosed -= WindowWatcher_WindowClosed;
            WindowWatcher.Instance.WindowClientAreaLocationChanged -= WindowWatcher_WindowClientAreaLocationChanged;
            WindowWatcher.Instance.WindowClientAreaSizeChanged -= WindowWatcher_WindowClientAreaSizeChanged;
            WindowWatcher.Instance.WindowShowStateChanged -= WindowWatcher_WindowShowStateChanged;

            multicontroller.ModeChanged -= Multicontroller_ModeChanged;
            multicontroller.ActiveControllersChanged -= Multicontroller_ActiveControllersChanged;
            multicontroller.ActiveChanged -= Multicontroller_ActiveChanged;
            multicontroller.SettingChanged -= Multicontroller_SettingChanged;

            Properties.Settings.Default.PropertyChanged -= Settings_PropertyChanged;
        }
    }
}
