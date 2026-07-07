using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;

namespace TTMulti.Input
{
    /// <summary>
    /// Framework-neutral owner of ALL global input capture, extracted verbatim from the WinForms main window
    /// (R5 of the WPF rebuild): RegisterHotKey registration for every hotkey ID, the WM_HOTKEY dispatch,
    /// key/mouse message pre-filtering into <see cref="Multicontroller.ProcessInput"/>, the four low-level
    /// hooks, the hook-death watchdog, the activation thread, the CMC fake-cursor ticker, and the
    /// admin-rights error polling. The hosting shell (WinForms today, WPF behind --newui) supplies only an
    /// HWND, UI-thread marshalling/timers, and user-facing notifications via <see cref="IInputShell"/>.
    /// Behavior contract: BEHAVIOR.md "Hotkeys &amp; triggers": every consume/pass-through condition here
    /// must match it.
    /// </summary>
    internal sealed class InputCaptureHost : IDisposable
    {
        private readonly IInputShell _shell;
        private readonly Multicontroller _controller;

        // The currently-hosted instance; the static LL hook procs reach state through this (they must be
        // static because the delegates are kept alive for the hook lifetime).
        private static InputCaptureHost _current;

        internal InputCaptureHost(IInputShell shell, Multicontroller controller)
        {
            _shell = shell ?? throw new ArgumentNullException(nameof(shell));
            _controller = controller ?? throw new ArgumentNullException(nameof(controller));
            _current = this;

            _fakeCursorTimer = shell.CreateTimer(16, FakeCursorTick);
            _hookWatchdogTimer = shell.CreateTimer(HookWatchdogIntervalMs, HookWatchdogTick);
            _hookWatchdogTimer.Start();

            // Input-capture-relevant engine events. UI-facing events (status text, colors) stay subscribed
            // in the shell; these are the ones whose handlers are pure input plumbing.
            _controller.ShouldActivate += Controller_ShouldActivate;
            _controller.WindowActivated += Controller_WindowActivated;
            _controller.AllWindowsInactive += Controller_AllWindowsInactive;
            _controller.GroupsChanged += Controller_GroupsChanged;
            _controller.ControlledMulticlickModeChanged += Controller_ControlledMulticlickModeChanged;
            _controller.InputCaptureFailed += Controller_InputCaptureFailed;
        }

        // ── Suspend state ───────────────────────────────────────────────────────────

        /// <summary>
        /// When true, global-style captures (RegisterHotKey 0-3, layout presets, minimize-unconnected global
        /// path, multiclick mouse hook) are off so keys reach the game; ID 4 (suspend toggle) stays registered.
        /// </summary>
        private bool _globalHotkeysSuspended = false;

        internal bool IsGlobalHotkeysSuspended => _globalHotkeysSuspended;

        /// <summary>Raised (on the UI thread) whenever the suspend state flips, so the shell can reflect it.</summary>
        internal event EventHandler SuspendStateChanged;

        /// <summary>Raised after the mode lock is toggled via its hotkey, so the shell can refresh visuals.</summary>
        internal event EventHandler ModeLockToggled;

        /// <summary>
        /// Raised when PostMessage to a game window has failed and the user should be offered an elevated
        /// relaunch. Held forwarded keys have already been released; the shell must defer its modal prompt
        /// onto the message loop (UX-09).
        /// </summary>
        internal event EventHandler AdminRightsPromptNeeded;

        // ── Message routing (called by the shell's message filter / WndProc equivalents) ──

        /// <summary>
        /// The IMessageFilter body: routes keyboard/mouse/SYSCOMMAND messages into the engine and decides
        /// which WM_HOTKEY IDs pass through to the window procedure. Returns true when the message must be
        /// consumed. The shell is responsible for its own dialog gating (ignoreMessages) before calling.
        /// </summary>
        internal bool PreFilterMessage(int msgCode, IntPtr wParam, IntPtr lParam)
        {
            bool ret = false;
            var msg = (Win32.WM)msgCode;

            switch (msg)
            {
                case Win32.WM.KEYDOWN:
                case Win32.WM.KEYUP:
                case Win32.WM.SYSKEYDOWN:
                case Win32.WM.SYSKEYUP:
                case Win32.WM.SYSCOMMAND:
                    ret = _controller.ProcessInput(msgCode, wParam, lParam);
                    break;
                case Win32.WM.LBUTTONDOWN:
                case Win32.WM.LBUTTONUP:
                case Win32.WM.RBUTTONDOWN:
                case Win32.WM.RBUTTONUP:
                case Win32.WM.MBUTTONDOWN:
                case Win32.WM.MBUTTONUP:
                case Win32.WM.MOUSEMOVE:
                case Win32.WM.MOUSEWHEEL:
                    // Intercept mouse messages and process them (especially important for switching mode)
                    ret = _controller.ProcessInput(msgCode, wParam, lParam);
                    break;
                case Win32.WM.HOTKEY:
                    // Let these hotkeys pass through to the window procedure (see HandleWindowMessage).
                    // Custom mode activation uses IDs CustomModeActivationStart..End, must not go through ProcessInput alone.
                    int hotkeyId = (int)wParam.ToInt64();
                    if (hotkeyId == HotkeyIds.ModeLockToggle || hotkeyId == HotkeyIds.SuspendGlobalsToggle
                        || hotkeyId == HotkeyIds.AutoFind || hotkeyId == HotkeyIds.MinimizeUnconnected
                        || (hotkeyId >= HotkeyIds.LayoutPresetStart && hotkeyId <= HotkeyIds.LayoutPresetEnd)
                        || (hotkeyId >= HotkeyIds.CustomModeActivationStart && hotkeyId <= HotkeyIds.CustomModeActivationEnd))
                    {
                        ret = false;
                    }
                    else
                    {
                        // Process other hotkeys normally
                        ret = _controller.ProcessInput(msgCode, wParam, lParam);
                    }
                    break;
            }

            CheckControllerErrors();

            return ret;
        }

        /// <summary>
        /// The ProcessCmdKey body: navigation keys are intercepted for game control only while actively
        /// controlling connected windows (UX-06); Alt is forwarded to the engine for switching mode.
        /// Returns true when the shell should report the key as handled; <paramref name="useDefault"/> is
        /// true when the shell must fall back to its own default processing.
        /// </summary>
        internal bool HandleCmdKey(int msgCode, IntPtr wParam, IntPtr lParam, Keys keyData, out bool useDefault)
        {
            useDefault = false;
            switch (keyData)
            {
                case Keys.Tab:
                case Keys.Up:
                case Keys.Down:
                case Keys.Left:
                case Keys.Right:
                    if (_controller.IsActive && _controller.AllControllersWithWindows.Any())
                        return true;
                    useDefault = true;
                    return false;
                case Keys.Alt:
                    // Forward Alt key to ProcessInput for switching mode handling; ProcessInput decides consumption.
                    return _controller.ProcessInput(msgCode, wParam, lParam);
                default:
                    useDefault = true;
                    return false;
            }
        }

        /// <summary>
        /// The WM_HOTKEY dispatch extracted from WndProc: handles the pass-through IDs (and re-handles 0/1/2
        /// with the modifier-passthrough rule). Callable from a WinForms WndProc override and from a WPF
        /// HwndSource hook. Returns true when the message was a WM_HOTKEY (handled).
        /// </summary>
        internal bool HandleWindowMessage(int msgCode, IntPtr wParam, IntPtr lParam)
        {
            if (msgCode != (int)Win32.WM.HOTKEY)
                return false;

            int hotkeyId = (int)wParam.ToInt64();

            if (hotkeyId == HotkeyIds.AutoFind)
            {
                _controller.AutoFindAndAssignWindows();
                if (_controller.IsActive || _controller.AllControllersWithWindows.Any(c => c.IsWindowActive))
                {
                    RegisterHotkey();
                }
                if (_controller.IsActive)
                {
                    RegisterAutoFindHotkey();
                    RegisterLayoutPresetHotkeys();
                }
            }
            else if (hotkeyId == HotkeyIds.MinimizeUnconnected)
            {
                _controller.ToggleMinimizeUnconnectedWindows();
            }
            else if (hotkeyId == HotkeyIds.SuspendGlobalsToggle)
            {
                _globalHotkeysSuspended = !_globalHotkeysSuspended;
                RefreshGlobalHotkeyRegistration();
                SuspendStateChanged?.Invoke(this, EventArgs.Empty);
            }
            else if (hotkeyId >= HotkeyIds.LayoutPresetStart && hotkeyId <= HotkeyIds.LayoutPresetEnd)
            {
                int presetIndex = hotkeyId - HotkeyIds.LayoutPresetStart;
                var file = LayoutPresetStorage.Load();
                if (file?.Presets != null && presetIndex < file.Presets.Count)
                {
                    _controller.ApplyLayoutPreset(file.Presets[presetIndex]);
                    Properties.Settings.Default.lastUsedLayoutPresetIndex = presetIndex;
                    Properties.Settings.Default.Save();
                    _shell.BeginInvoke(TryActivate);
                }
            }
            else if (hotkeyId == HotkeyIds.ModeLockToggle)
            {
                _controller.ToggleModeLock();
                ModeLockToggled?.Invoke(this, EventArgs.Empty);
            }
            else if (hotkeyId == HotkeyIds.Mode)
            {
                // Modifier passthrough: with Shift/Ctrl/Alt held, don't switch modes, convert the HOTKEY into
                // a synthetic KEYDOWN so the key forwards to the games instead.
                if (AreModifiersHeld())
                {
                    Keys keyCode = (Keys)Properties.Settings.Default.modeKeyCode;
                    _controller.ProcessInput((int)Win32.WM.KEYDOWN, (IntPtr)keyCode, IntPtr.Zero);
                }
                else
                {
                    _controller.ProcessInput(msgCode, wParam, lParam);
                }
            }
            else if (hotkeyId == HotkeyIds.InstantMultiClick)
            {
                if (AreModifiersHeld())
                {
                    Keys keyCode = (Keys)Properties.Settings.Default.replicateMouseKeyCode;
                    _controller.ProcessInput((int)Win32.WM.KEYDOWN, (IntPtr)keyCode, IntPtr.Zero);
                }
                else
                {
                    _controller.ProcessInput(msgCode, wParam, lParam);
                }
            }
            else if (hotkeyId >= HotkeyIds.CustomModeActivationStart && hotkeyId <= HotkeyIds.CustomModeActivationEnd)
            {
                if (_customModeActivationHotkeyIds.TryGetValue(hotkeyId, out string customModeId) && !string.IsNullOrEmpty(customModeId))
                {
                    if (AreModifiersHeld())
                    {
                        var file = CustomModeStorage.Load();
                        var mode = file.Modes?.FirstOrDefault(cm => string.Equals(cm.Id, customModeId, StringComparison.Ordinal));
                        if (mode != null && mode.ActivationHotkeyCode != 0)
                            _controller.ProcessInput((int)Win32.WM.KEYDOWN, (IntPtr)(Keys)mode.ActivationHotkeyCode, IntPtr.Zero);
                    }
                    else
                    {
                        if (!_controller.IsActive)
                            _shell.BeginInvoke(TryActivate);
                        _controller.ActivateCustomModeDefinition(customModeId);
                    }
                }
            }
            else if (hotkeyId == HotkeyIds.ZeroPowerThrow)
            {
                if (AreModifiersHeld())
                {
                    Keys keyCode = (Keys)Properties.Settings.Default.zeroPowerThrowKeyCode;
                    _controller.ProcessInput((int)Win32.WM.KEYDOWN, (IntPtr)keyCode, IntPtr.Zero);
                }
                else
                {
                    _controller.ProcessInput(msgCode, wParam, lParam);
                }
            }
            else
            {
                _controller.ProcessInput(msgCode, wParam, lParam);
            }

            CheckControllerErrors();
            return true;
        }

        private static bool AreModifiersHeld()
        {
            Keys currentModifiers = Win32.GetModifierKeys();
            return (currentModifiers & (Keys.Shift | Keys.Control | Keys.Alt)) != Keys.None;
        }

        // ── Admin-rights error polling ──────────────────────────────────────────────

        private bool _userPromptedForAdminRights = false;

        internal void CheckControllerErrors()
        {
            if (!_userPromptedForAdminRights && _controller.ErrorOccurredPostingMessage)
            {
                _userPromptedForAdminRights = true;

                // This runs at the tail of input dispatch. A modal dialog here would pump messages
                // mid-keystroke (stuck keys in the games), so release held forwarded keys now and let the
                // shell defer the prompt onto the message loop (UX-09).
                _controller.ReleaseAllHeldForwardedKeys();
                AdminRightsPromptNeeded?.Invoke(this, EventArgs.Empty);
            }
        }

        // ── Activation thread (CORR-01 semantics preserved verbatim) ────────────────

        private Thread _activationThread = null;
        private volatile bool _cancelActivation = false;
        private volatile bool _closing = false;

        internal void TryActivate()
        {
            if (_closing)
                return;

            _cancelActivation = false;

            // IsAlive (not ThreadState) is the correct liveness test: the thread is IsBackground, so its
            // ThreadState always carries the Background flag and never equals Running (CORR-01).
            if (_activationThread == null || !_activationThread.IsAlive)
            {
                _activationThread = new Thread(ActivationThreadFunc) { IsBackground = true };
                _activationThread.Start();
            }
        }

        /// <summary>Cancel any pending activation loop (the user deliberately focused another window).</summary>
        internal void CancelActivation()
        {
            _cancelActivation = true;
        }

        private void ActivationThreadFunc()
        {
            try
            {
                if (_cancelActivation)
                    return;

                IntPtr hWnd = IntPtr.Zero;
                if (!_shell.SafeInvoke(() => hWnd = _shell.Handle) || _cancelActivation || hWnd == IntPtr.Zero)
                    return;

                // Use AttachThreadInput so SetForegroundWindow isn't redirected to a taskbar flash: borrow the
                // current foreground thread's input queue, steal focus cleanly, then detach.
                IntPtr foregroundWnd = Win32.GetForegroundWindow();
                uint foregroundThread = foregroundWnd != IntPtr.Zero
                    ? Win32.GetWindowThreadProcessId(foregroundWnd, out _)
                    : 0;
                uint ourThread = Win32.GetCurrentThreadId();
                bool attached = false;

                try
                {
                    if (foregroundThread != 0 && foregroundThread != ourThread)
                        attached = Win32.AttachThreadInput(foregroundThread, ourThread, true);

                    Win32.BringWindowToTop(hWnd);
                    Win32.SetForegroundWindow(hWnd);

                    _shell.SafeInvoke(() =>
                    {
                        if (!_cancelActivation)
                            _shell.FinishActivation();
                    });
                }
                finally
                {
                    // Always detach the borrowed input queue, even if activation was cancelled or failed.
                    if (attached)
                        Win32.AttachThreadInput(foregroundThread, ourThread, false);
                }
            }
            catch
            {
                // Best-effort activation. An unhandled exception on this background thread would crash the
                // process (no global handler installed), so swallow it (CORR-01).
            }
        }

        // ── Hotkey registration failure reporting (UX-01 / WIN32-04) ────────────────

        // Failures already shown this settings-session, so each dead hotkey is reported at most once
        // (cleared in OnSettingsReloaded when settings change).
        private readonly HashSet<string> _reportedHotkeyFailures = new HashSet<string>();
        // Failures accumulated during the current RegisterHotkey() pass, before they are reported.
        private readonly List<string> _pendingHotkeyFailures = new List<string>();

        private bool TryRegisterGlobalHotKey(int id, Win32.KeyModifiers modifiers, Keys key, string featureName)
        {
            bool ok = Win32.RegisterHotKey(_shell.Handle, id, modifiers, key);
            if (!ok)
            {
                int err = Marshal.GetLastWin32Error();
                System.Diagnostics.Trace.WriteLine("RegisterHotKey failed for \"" + featureName + "\" (" + DescribeHotkey(modifiers, key) + "): Win32 error " + err);
                _pendingHotkeyFailures.Add(featureName + ": " + DescribeHotkey(modifiers, key));
            }
            return ok;
        }

        private IntPtr TryInstallLowLevelHook(int hookType, Win32.HookProc proc, string featureName)
        {
            IntPtr handle = Win32.SetWindowsHookEx(hookType, proc, Win32.GetModuleHandle(null), 0);
            if (handle == IntPtr.Zero)
            {
                int err = Marshal.GetLastWin32Error();
                System.Diagnostics.Trace.WriteLine("SetWindowsHookEx failed for \"" + featureName + "\": Win32 error " + err);
                _pendingHotkeyFailures.Add(featureName + " (input hook, Win32 error " + err + ")");
                ReportPendingHotkeyFailures();
            }
            return handle;
        }

        private static string DescribeHotkey(Win32.KeyModifiers modifiers, Keys key)
        {
            string prefix = "";
            if ((modifiers & Win32.KeyModifiers.Control) != 0) prefix += "Ctrl+";
            if ((modifiers & Win32.KeyModifiers.Alt) != 0) prefix += "Alt+";
            if ((modifiers & Win32.KeyModifiers.Shift) != 0) prefix += "Shift+";
            if ((modifiers & Win32.KeyModifiers.Windows) != 0) prefix += "Win+";
            return prefix + (key & Keys.KeyCode).ToString();
        }

        private void ReportPendingHotkeyFailures()
        {
            if (_pendingHotkeyFailures.Count == 0)
                return;

            var newFailures = _pendingHotkeyFailures.Where(f => _reportedHotkeyFailures.Add(f)).ToList();
            _pendingHotkeyFailures.Clear();

            if (newFailures.Count == 0)
                return;

            string message = "These global hotkeys or input hooks could not be registered; another program may "
                + "already be using them, or two features are assigned the same key:\n\n  "
                + string.Join("\n  ", newFailures)
                + "\n\nOpen Options and pick different keys to fix this.";

            _shell.ShowWarning(message, "Hotkey Not Registered");
        }

        private void Controller_InputCaptureFailed(object sender, string description)
        {
            _pendingHotkeyFailures.Add(description);
            ReportPendingHotkeyFailures();
        }

        // ── Hook-death watchdog ─────────────────────────────────────────────────────

        private readonly IUiTimer _hookWatchdogTimer;
        private const int HookWatchdogIntervalMs = 15000;
        private uint _lastWatchdogInputTick;

        private void HookWatchdogTick()
        {
            // A hook can only be dropped when it is invoked, i.e. when input occurs; skip re-arming when
            // nothing happened since the previous tick so idle machines never churn healthy hooks.
            uint inputTick = GetLastSystemInputTick();
            if (inputTick == _lastWatchdogInputTick)
                return;
            _lastWatchdogInputTick = inputTick;
            RearmInstalledLowLevelHooks();
        }

        private static uint GetLastSystemInputTick()
        {
            var lii = new Win32.LASTINPUTINFO { cbSize = (uint)Marshal.SizeOf(typeof(Win32.LASTINPUTINFO)) };
            return Win32.GetLastInputInfo(ref lii) ? lii.dwTime : 0;
        }

        private void RearmInstalledLowLevelHooks()
        {
            RearmLowLevelHook(ref _minimizeUnconnectedKeyboardHookHandle, _minimizeUnconnectedKeyboardHookProc, Win32.WH_KEYBOARD_LL, "Minimize unconnected windows key");
            RearmLowLevelHook(ref _multiclickMouseHookHandle, _multiclickMouseHookProc, Win32.WH_MOUSE_LL, "Instant Multi-Click mouse button");
            RearmLowLevelHook(ref _controlledMcKeyboardHookHandle, _controlledMcKeyboardHookProc, Win32.WH_KEYBOARD_LL, "Precise Click keys");
            RearmLowLevelHook(ref _controlledMcFocusBlockHookHandle, _controlledMcFocusBlockHookProc, Win32.WH_MOUSE_LL, "Precise Click focus block");
            _controller?.RearmSwitchingMouseHookIfInstalled();
        }

        private void RearmLowLevelHook(ref IntPtr handle, Win32.HookProc proc, int hookType, string featureName)
        {
            if (handle == IntPtr.Zero || proc == null)
                return;
            Win32.UnhookWindowsHookEx(handle);
            handle = Win32.SetWindowsHookEx(hookType, proc, Win32.GetModuleHandle(null), 0);
            System.Diagnostics.Trace.WriteLine("Hook watchdog: re-armed " + featureName + " (handle=" + handle + ")");
            if (handle == IntPtr.Zero)
            {
                int err = Marshal.GetLastWin32Error();
                System.Diagnostics.Trace.WriteLine("Hook watchdog: re-arm FAILED for " + featureName + " (Win32 error " + err + ")");
                _pendingHotkeyFailures.Add(featureName + " (input hook re-arm, Win32 error " + err + ")");
                ReportPendingHotkeyFailures();
            }
        }

        // ── Focus-driven hotkey registration (PERF-04 gating preserved) ─────────────

        private bool? _lastRegisteredActive;
        private bool _lastRegisteredSuspended;
        private bool _lastRegisteredAnyWindowActive;

        internal void RegisterFocusHotkeys(bool force = false)
        {
            bool active = _controller.IsActive;
            bool anyWindowActive = _controller.AllControllersWithWindows.Any(c => c.IsWindowActive);
            if (!force
                && _lastRegisteredActive == active
                && _lastRegisteredSuspended == _globalHotkeysSuspended
                && _lastRegisteredAnyWindowActive == anyWindowActive)
                return;

            _lastRegisteredActive = active;
            _lastRegisteredSuspended = _globalHotkeysSuspended;
            _lastRegisteredAnyWindowActive = anyWindowActive;

            UnregisterHotkey();
            UnregisterAutoFindHotkey();
            UnregisterLayoutPresetHotkeys();
            UnregisterMinimizeUnconnectedHotkey();

            RegisterHotkey();
            RegisterAutoFindHotkey();
            RegisterLayoutPresetHotkeys();
            RegisterMinimizeUnconnectedHotkey();
        }

        internal void RegisterHotkey()
        {
            _pendingHotkeyFailures.Clear();
            UninstallMulticlickMouseHook();
            Win32.UnregisterHotKey(_shell.Handle, HotkeyIds.Mode);
            Win32.UnregisterHotKey(_shell.Handle, HotkeyIds.InstantMultiClick);
            Win32.UnregisterHotKey(_shell.Handle, HotkeyIds.ZeroPowerThrow);
            Win32.UnregisterHotKey(_shell.Handle, HotkeyIds.ModeLockToggle);
            Win32.UnregisterHotKey(_shell.Handle, HotkeyIds.SuspendGlobalsToggle);
            UnregisterCustomModeActivationHotkeys();

            // Suspend-global toggle (ID 4): always registered when configured, including while suspended,
            // so the user can turn globals back on.
            if (Properties.Settings.Default.suspendGlobalHotkeysToggleKeyCode != 0)
                TryRegisterGlobalHotKey(HotkeyIds.SuspendGlobalsToggle, Win32.KeyModifiers.None, (Keys)Properties.Settings.Default.suspendGlobalHotkeysToggleKeyCode, "Suspend global hotkeys toggle");

            if (_globalHotkeysSuspended)
            {
                ReportPendingHotkeyFailures();
                return;
            }

            // Mode/Activate (ID 0)
            bool modeGlobal = Properties.Settings.Default.modeHotkeyGlobal;
            if (modeGlobal || _controller.IsActive)
            {
                TryRegisterGlobalHotKey(HotkeyIds.Mode, Win32.KeyModifiers.None, (Keys)Properties.Settings.Default.modeKeyCode, "Mode / Activate");
            }

            // Instant Multi-Click (ID 1) - keyboard hotkey or mouse hook (RegisterHotKey does not support mouse buttons)
            if (Properties.Settings.Default.replicateMouseUseMouseButton)
            {
                int btn = Properties.Settings.Default.replicateMouseMouseButton;
                if (btn >= 0 && btn <= 2)
                {
                    bool multiGlobal = Properties.Settings.Default.replicateMouseHotkeyGlobal;
                    if (multiGlobal || _controller.IsActive)
                        InstallMulticlickMouseHook(btn);
                }
            }
            else if (Properties.Settings.Default.replicateMouseKeyCode != 0)
            {
                bool multiGlobal = Properties.Settings.Default.replicateMouseHotkeyGlobal;
                if (multiGlobal || _controller.IsActive)
                {
                    TryRegisterGlobalHotKey(HotkeyIds.InstantMultiClick, Win32.KeyModifiers.None, (Keys)Properties.Settings.Default.replicateMouseKeyCode, "Instant Multi-Click");
                }
            }

            // Zero Power Throw (ID 2)
            if (Properties.Settings.Default.zeroPowerThrowKeyCode != 0)
            {
                bool zeroGlobal = Properties.Settings.Default.zeroPowerThrowHotkeyGlobal;
                if (zeroGlobal || _controller.IsActive)
                {
                    TryRegisterGlobalHotKey(HotkeyIds.ZeroPowerThrow, Win32.KeyModifiers.None, (Keys)Properties.Settings.Default.zeroPowerThrowKeyCode, "Zero Power Throw");
                }
            }

            // Mode lock toggle (ID 3): always registered when set so it works from game windows
            if (Properties.Settings.Default.modeLockToggleKeyCode != 0)
                TryRegisterGlobalHotKey(HotkeyIds.ModeLockToggle, Win32.KeyModifiers.None, (Keys)Properties.Settings.Default.modeLockToggleKeyCode, "Mode lock toggle");
            // Note: ID 7 (auto-find), ID 10-25 (layout presets) handled separately

            RegisterCustomModeActivationHotkeys();

            ReportPendingHotkeyFailures();
        }

        private readonly Dictionary<int, string> _customModeActivationHotkeyIds = new Dictionary<int, string>();

        private void UnregisterCustomModeActivationHotkeys()
        {
            _customModeActivationHotkeyIds.Clear();
            for (int id = HotkeyIds.CustomModeActivationStart; id <= HotkeyIds.CustomModeActivationEnd; id++)
                Win32.UnregisterHotKey(_shell.Handle, id);
        }

        private void RegisterCustomModeActivationHotkeys()
        {
            if (_globalHotkeysSuspended)
                return;
            CustomModeFile file = CustomModeStorage.LoadCached();
            if (file.Modes == null)
                return;
            int hotkeyId = HotkeyIds.CustomModeActivationStart;
            foreach (CustomModeDefinition mode in file.Modes)
            {
                if (mode.ActivationHotkeyCode == 0)
                    continue;
                if (hotkeyId > HotkeyIds.CustomModeActivationEnd)
                    break;
                bool global = mode.ActivationHotkeyGlobal;
                if (!global && !_controller.IsActive)
                    continue;
                bool ok = Win32.RegisterHotKey(_shell.Handle, hotkeyId, (Win32.KeyModifiers)mode.ActivationHotkeyModifiers, (Keys)mode.ActivationHotkeyCode);
                if (ok)
                    _customModeActivationHotkeyIds[hotkeyId] = mode.Id;
                hotkeyId++;
            }
        }

        /// <summary>After the suspend toggle flips, re-apply registration so everything follows the new state.</summary>
        internal void RefreshGlobalHotkeyRegistration()
        {
            RegisterFocusHotkeys(force: true);
        }

        private bool _modalHotkeySuspendPrev;

        /// <summary>
        /// Unregister the global hotkeys and input hooks for the duration of a modal dialog (Options), so a key
        /// that is currently a global trigger (e.g. the Zero Power Throw key bound to Scroll Lock) reaches the
        /// key pickers to be re-bound instead of being swallowed by RegisterHotKey / the LL hooks. Pairs with
        /// <see cref="ResumeGlobalHotkeysAfterModal"/>; restores whatever suspend state was in effect before.
        /// </summary>
        internal void SuspendGlobalHotkeysForModal()
        {
            _modalHotkeySuspendPrev = _globalHotkeysSuspended;
            if (!_globalHotkeysSuspended)
            {
                _globalHotkeysSuspended = true;
                RefreshGlobalHotkeyRegistration();
            }
        }

        /// <summary>Restore the pre-modal hotkey suspend state (see <see cref="SuspendGlobalHotkeysForModal"/>).</summary>
        internal void ResumeGlobalHotkeysAfterModal()
        {
            if (_globalHotkeysSuspended != _modalHotkeySuspendPrev)
            {
                _globalHotkeysSuspended = _modalHotkeySuspendPrev;
                RefreshGlobalHotkeyRegistration();
            }
        }

        private void UnregisterHotkey()
        {
            Win32.UnregisterHotKey(_shell.Handle, HotkeyIds.Mode);
            Win32.UnregisterHotKey(_shell.Handle, HotkeyIds.InstantMultiClick);
            Win32.UnregisterHotKey(_shell.Handle, HotkeyIds.ZeroPowerThrow);
            Win32.UnregisterHotKey(_shell.Handle, HotkeyIds.ModeLockToggle);
            Win32.UnregisterHotKey(_shell.Handle, HotkeyIds.SuspendGlobalsToggle);
            UnregisterCustomModeActivationHotkeys();
            UninstallMulticlickMouseHook();
        }

        internal void RegisterAutoFindHotkey()
        {
            // Auto-find (ID 7) - NEVER global, only when the shell window is active
            if (Properties.Settings.Default.autoFindWindowsKeyCode != 0 && _controller.IsActive)
            {
                bool success = Win32.RegisterHotKey(_shell.Handle, HotkeyIds.AutoFind, (Win32.KeyModifiers)Properties.Settings.Default.autoFindWindowsKeyModifiers, (Keys)Properties.Settings.Default.autoFindWindowsKeyCode);
                if (!success)
                {
                    // Retry after unregistering; surface the failure if the retry also fails (WIN32-04)
                    Win32.UnregisterHotKey(_shell.Handle, HotkeyIds.AutoFind);
                    if (!TryRegisterGlobalHotKey(HotkeyIds.AutoFind, (Win32.KeyModifiers)Properties.Settings.Default.autoFindWindowsKeyModifiers, (Keys)Properties.Settings.Default.autoFindWindowsKeyCode, "Auto-find windows"))
                        ReportPendingHotkeyFailures();
                }
            }
        }

        private void UnregisterAutoFindHotkey()
        {
            Win32.UnregisterHotKey(_shell.Handle, HotkeyIds.AutoFind);
        }

        internal void RegisterLayoutPresetHotkeys()
        {
            if (_globalHotkeysSuspended || !_controller.IsActive) return;
            // Idempotent: unregister our preset IDs before re-registering so a second call while they're
            // already registered doesn't fail with ERROR_HOTKEY_ALREADY_REGISTERED and raise a spurious
            // warning. A genuine conflict with another program still fails (UnregisterHotKey only frees THIS
            // window's registration), so real conflicts are still surfaced.
            UnregisterLayoutPresetHotkeys();
            var file = LayoutPresetStorage.LoadCached();
            if (file?.Presets == null) return;
            for (int i = 0; i < file.Presets.Count && i <= HotkeyIds.LayoutPresetEnd - HotkeyIds.LayoutPresetStart; i++)
            {
                var p = file.Presets[i];
                if (p.HotkeyCode == 0) continue;
                string presetName = string.IsNullOrEmpty(p.Name) ? "#" + (i + 1) : p.Name;
                TryRegisterGlobalHotKey(HotkeyIds.LayoutPresetStart + i, (Win32.KeyModifiers)p.HotkeyModifiers, (Keys)p.HotkeyCode,
                    "Layout preset \"" + presetName + "\"");
            }
            ReportPendingHotkeyFailures();
        }

        private void UnregisterLayoutPresetHotkeys()
        {
            for (int id = HotkeyIds.LayoutPresetStart; id <= HotkeyIds.LayoutPresetEnd; id++)
                Win32.UnregisterHotKey(_shell.Handle, id);
        }

        internal void RegisterMinimizeUnconnectedHotkey()
        {
            // Minimize unconnected (ID 9). No modifier: RegisterHotKey doesn't work globally for single keys,
            // so a low-level keyboard hook is used instead. With a modifier: RegisterHotKey as normal.
            int keyCode = Properties.Settings.Default.minimizeUnconnectedKeyCode;
            int modifiers = Properties.Settings.Default.minimizeUnconnectedKeyModifiers;
            if (keyCode == 0)
            {
                UnregisterMinimizeUnconnectedHotkey();
                return;
            }
            if (_globalHotkeysSuspended)
            {
                UnregisterMinimizeUnconnectedHotkey();
                return;
            }
            bool shouldRegister = Properties.Settings.Default.minimizeUnconnectedHotkeyGlobal
                || _controller.IsActive
                || _controller.AllControllersWithWindows.Any(c => c.IsWindowActive);
            if (!shouldRegister)
            {
                UnregisterMinimizeUnconnectedHotkey();
                return;
            }
            bool noModifiers = (modifiers == 0 || modifiers == (int)Win32.KeyModifiers.None);
            if (noModifiers)
            {
                Win32.UnregisterHotKey(_shell.Handle, HotkeyIds.MinimizeUnconnected);
                InstallMinimizeUnconnectedKeyboardHook(keyCode);
            }
            else
            {
                UninstallMinimizeUnconnectedKeyboardHook();
                bool success = Win32.RegisterHotKey(_shell.Handle, HotkeyIds.MinimizeUnconnected, (Win32.KeyModifiers)modifiers, (Keys)keyCode);
                if (!success)
                {
                    // Retry after unregistering; surface the failure if the retry also fails (WIN32-04)
                    Win32.UnregisterHotKey(_shell.Handle, HotkeyIds.MinimizeUnconnected);
                    if (!TryRegisterGlobalHotKey(HotkeyIds.MinimizeUnconnected, (Win32.KeyModifiers)modifiers, (Keys)keyCode, "Minimize unconnected windows"))
                        ReportPendingHotkeyFailures();
                }
            }
        }

        private void UnregisterMinimizeUnconnectedHotkey()
        {
            Win32.UnregisterHotKey(_shell.Handle, HotkeyIds.MinimizeUnconnected);
            UninstallMinimizeUnconnectedKeyboardHook();
        }

        // ── LL hook: minimize-unconnected single key ────────────────────────────────

        private static IntPtr _minimizeUnconnectedKeyboardHookHandle = IntPtr.Zero;
        private static InputCaptureHost _minimizeUnconnectedHookHost = null;
        private static int _minimizeUnconnectedHookKeyCode = 0;
        private static Win32.HookProc _minimizeUnconnectedKeyboardHookProc = null;

        private void InstallMinimizeUnconnectedKeyboardHook(int keyCode)
        {
            if (_minimizeUnconnectedKeyboardHookHandle != IntPtr.Zero)
            {
                if (_minimizeUnconnectedHookKeyCode == keyCode)
                    return;
                UninstallMinimizeUnconnectedKeyboardHook();
            }
            _minimizeUnconnectedHookHost = this;
            _minimizeUnconnectedHookKeyCode = keyCode;
            if (_minimizeUnconnectedKeyboardHookProc == null)
                _minimizeUnconnectedKeyboardHookProc = MinimizeUnconnectedKeyboardHookProc;
            _minimizeUnconnectedKeyboardHookHandle = TryInstallLowLevelHook(Win32.WH_KEYBOARD_LL, _minimizeUnconnectedKeyboardHookProc, "Minimize unconnected windows key");
        }

        private void UninstallMinimizeUnconnectedKeyboardHook()
        {
            if (_minimizeUnconnectedKeyboardHookHandle != IntPtr.Zero)
            {
                Win32.UnhookWindowsHookEx(_minimizeUnconnectedKeyboardHookHandle);
                _minimizeUnconnectedKeyboardHookHandle = IntPtr.Zero;
            }
            _minimizeUnconnectedHookHost = null;
            _minimizeUnconnectedHookKeyCode = 0;
        }

        private static IntPtr MinimizeUnconnectedKeyboardHookProc(int nCode, IntPtr wParam, IntPtr lParam)
        {
            if (nCode < 0)
                return Win32.CallNextHookEx(_minimizeUnconnectedKeyboardHookHandle, nCode, wParam, lParam);
            int msg = (int)wParam.ToInt64();
            // WM_KEYDOWN = 0x100, WM_SYSKEYDOWN = 0x104
            if (msg != 0x100 && msg != 0x104)
                return Win32.CallNextHookEx(_minimizeUnconnectedKeyboardHookHandle, nCode, wParam, lParam);
            var host = _minimizeUnconnectedHookHost;
            if (host == null || _minimizeUnconnectedHookKeyCode == 0)
                return Win32.CallNextHookEx(_minimizeUnconnectedKeyboardHookHandle, nCode, wParam, lParam);
            var hookStruct = (Win32.KBDLLHOOKSTRUCT)Marshal.PtrToStructure(lParam, typeof(Win32.KBDLLHOOKSTRUCT));
            if ((uint)hookStruct.vkCode != _minimizeUnconnectedHookKeyCode)
                return Win32.CallNextHookEx(_minimizeUnconnectedKeyboardHookHandle, nCode, wParam, lParam);
            // No modifiers: Alt, Ctrl, Shift must not be pressed
            short alt = Win32.GetAsyncKeyState(Keys.Menu);
            short ctrl = Win32.GetAsyncKeyState(Keys.ControlKey);
            short shift = Win32.GetAsyncKeyState(Keys.ShiftKey);
            if ((alt & 0x8000) != 0 || (ctrl & 0x8000) != 0 || (shift & 0x8000) != 0)
                return Win32.CallNextHookEx(_minimizeUnconnectedKeyboardHookHandle, nCode, wParam, lParam);
            host._shell.BeginInvoke(() =>
            {
                _minimizeUnconnectedHookHost?._controller?.ToggleMinimizeUnconnectedWindows();
            });
            return (IntPtr)1; // Consume the key
        }

        // ── LL hook: instant multi-click mouse button ───────────────────────────────

        private static IntPtr _multiclickMouseHookHandle = IntPtr.Zero;
        private static InputCaptureHost _multiclickMouseHookHost = null;
        private static int _multiclickMouseHookButton = -1; // 0=Middle, 1=XButton1, 2=XButton2
        private static Win32.HookProc _multiclickMouseHookProc = null;

        private void InstallMulticlickMouseHook(int buttonIndex)
        {
            if (_multiclickMouseHookHandle != IntPtr.Zero)
            {
                if (_multiclickMouseHookButton == buttonIndex)
                    return;
                UninstallMulticlickMouseHook();
            }
            _multiclickMouseHookHost = this;
            _multiclickMouseHookButton = buttonIndex;
            if (_multiclickMouseHookProc == null)
                _multiclickMouseHookProc = MulticlickMouseHookProc;
            _multiclickMouseHookHandle = TryInstallLowLevelHook(Win32.WH_MOUSE_LL, _multiclickMouseHookProc, "Instant Multi-Click mouse button");
        }

        private void UninstallMulticlickMouseHook()
        {
            if (_multiclickMouseHookHandle != IntPtr.Zero)
            {
                Win32.UnhookWindowsHookEx(_multiclickMouseHookHandle);
                _multiclickMouseHookHandle = IntPtr.Zero;
            }
            _multiclickMouseHookHost = null;
            _multiclickMouseHookButton = -1;
        }

        private static IntPtr MulticlickMouseHookProc(int nCode, IntPtr wParam, IntPtr lParam)
        {
            if (nCode < 0)
                return Win32.CallNextHookEx(_multiclickMouseHookHandle, nCode, wParam, lParam);
            var host = _multiclickMouseHookHost;
            if (host == null || _multiclickMouseHookButton < 0)
                return Win32.CallNextHookEx(_multiclickMouseHookHandle, nCode, wParam, lParam);

            int msg = (int)wParam.ToInt64();

            // Determine if this message is a DOWN or UP for the configured button
            bool isDown = false;
            if (_multiclickMouseHookButton == 0)
            {
                isDown = msg == (int)Win32.WM.MBUTTONDOWN;
            }
            else if (_multiclickMouseHookButton >= 1 &&
                     (msg == (int)Win32.WM.XBUTTONDOWN || msg == (int)Win32.WM.XBUTTONUP))
            {
                var hookStruct = (Win32.MSLLHOOKSTRUCT)Marshal.PtrToStructure(lParam, typeof(Win32.MSLLHOOKSTRUCT));
                int xButton = (int)(hookStruct.mouseData >> 16);
                bool buttonMatches = (_multiclickMouseHookButton == 1 && xButton == 1)
                                  || (_multiclickMouseHookButton == 2 && xButton == 2);
                if (buttonMatches)
                {
                    isDown = msg == (int)Win32.WM.XBUTTONDOWN;
                }
            }

            if (isDown)
            {
                Keys mods = Win32.GetModifierKeys();
                if ((mods & (Keys.Shift | Keys.Control | Keys.Alt)) != Keys.None)
                    return Win32.CallNextHookEx(_multiclickMouseHookHandle, nCode, wParam, lParam);

                bool isActive = host._controller?.IsActive ?? false;
                bool multiGlobal = Properties.Settings.Default.replicateMouseHotkeyGlobal;

                if (!isActive && !multiGlobal)
                    return Win32.CallNextHookEx(_multiclickMouseHookHandle, nCode, wParam, lParam);

                host._shell.BeginInvoke(() =>
                {
                    _multiclickMouseHookHost?._controller?.TriggerInstantMultiClick(separateLR: Properties.Settings.Default.replicateMouseSeparateLR);
                });
                return (IntPtr)1;
            }

            return Win32.CallNextHookEx(_multiclickMouseHookHandle, nCode, wParam, lParam);
        }

        // ── Precise Click Mode: keyboard dispatch (single hook, PERF-02) ──

        private static IntPtr _controlledMcKeyboardHookHandle = IntPtr.Zero;
        private static InputCaptureHost _controlledMcKeyboardHookHost = null;
        private static Win32.HookProc _controlledMcKeyboardHookProc = null;

        internal void RegisterControlledMulticlickHotkeys()
        {
            UnregisterControlledMulticlickHotkeys();

            // Precise Click Mode has no separate on/off setting: it is "on" precisely when an activation key is
            // bound (or we're already in the mode, e.g. a settings reload happened mid-mode, so the click keys
            // keep working). One dispatcher hook covers the activation key and the multi-click / regular-click
            // keys (which the proc only acts on while the mode is active). Leaving the activation key unset keeps
            // the mode off. (PERF-02)
            if (Properties.Settings.Default.controlledMulticlickActivateKeyCode != 0 || _controller.IsControlledMulticlickMode)
                InstallControlledMcKeyboardHook();
        }

        internal void UnregisterControlledMulticlickHotkeys()
        {
            UninstallControlledMcKeyboardHook();
        }

        private void InstallControlledMcKeyboardHook()
        {
            _controlledMcKeyboardHookHost = this;
            if (_controlledMcKeyboardHookHandle != IntPtr.Zero)
                return;
            if (_controlledMcKeyboardHookProc == null)
                _controlledMcKeyboardHookProc = ControlledMcKeyboardHookProc;
            _controlledMcKeyboardHookHandle = TryInstallLowLevelHook(Win32.WH_KEYBOARD_LL, _controlledMcKeyboardHookProc, "Precise Click keys");
        }

        private void UninstallControlledMcKeyboardHook()
        {
            if (_controlledMcKeyboardHookHandle != IntPtr.Zero)
            {
                Win32.UnhookWindowsHookEx(_controlledMcKeyboardHookHandle);
                _controlledMcKeyboardHookHandle = IntPtr.Zero;
            }
            _controlledMcKeyboardHookHost = null;
        }

        /// <summary>
        /// Single LL keyboard hook dispatching every Precise Click Mode key, in the precedence
        /// regular-click → multi-click → activation; the first that matches consumes the key (PERF-02).
        /// </summary>
        private static IntPtr ControlledMcKeyboardHookProc(int nCode, IntPtr wParam, IntPtr lParam)
        {
            if (nCode < 0)
                return Win32.CallNextHookEx(_controlledMcKeyboardHookHandle, nCode, wParam, lParam);

            var host = _controlledMcKeyboardHookHost;
            if (host == null)
                return Win32.CallNextHookEx(_controlledMcKeyboardHookHandle, nCode, wParam, lParam);

            int msg = (int)wParam.ToInt64();
            bool isDown = msg == 0x100 || msg == 0x104;
            bool isUp   = msg == 0x101 || msg == 0x105;
            if (!isDown && !isUp)
                return Win32.CallNextHookEx(_controlledMcKeyboardHookHandle, nCode, wParam, lParam);

            var hookStruct = (Win32.KBDLLHOOKSTRUCT)Marshal.PtrToStructure(lParam, typeof(Win32.KBDLLHOOKSTRUCT));
            uint vk = (uint)hookStruct.vkCode;

            var settings = Properties.Settings.Default;
            bool modeActive = host._controller?.IsControlledMulticlickMode == true;

            // ── Mirror / passthrough click keys (mode active, keyboard-bound) ──
            // Returns true (and we consume) when this key matches its bind.  Mirror clicks fire on press
            // only; passthrough clicks honour their own trigger-on-release setting.  If a click key doubles
            // as the hold-activation key, exit the mode on its release so the mode still ends.
            bool TryHandleClickKey(bool useMouse, int keyCode, bool triggerOnRelease, Action<Multicontroller> action)
            {
                if (!modeActive || useMouse || keyCode == 0 || vk != (uint)keyCode)
                    return false;
                bool shouldFire = triggerOnRelease ? isUp : isDown;
                if (shouldFire || isUp)
                {
                    host._shell.BeginInvoke(() =>
                    {
                        var c = _controlledMcKeyboardHookHost?._controller;
                        if (c == null) return;
                        if (shouldFire)
                            action(c);
                        if (isUp)
                        {
                            bool activateHold = Properties.Settings.Default.controlledMulticlickActivateHold;
                            uint activateKey  = (uint)Properties.Settings.Default.controlledMulticlickActivateKeyCode;
                            if (activateHold && activateKey == vk)
                                c.ExitControlledMulticlickMode();
                        }
                    });
                }
                return true;
            }

            if (TryHandleClickKey(settings.controlledMulticlickMirrorLeftUseMouseButton,
                    settings.controlledMulticlickMirrorLeftKeyCode, false,
                    c => c.TriggerInstantMultiClick(separateLR: false, rightButton: false)))
                return (IntPtr)1;
            if (TryHandleClickKey(settings.controlledMulticlickMirrorRightUseMouseButton,
                    settings.controlledMulticlickMirrorRightKeyCode, false,
                    c => c.TriggerInstantMultiClick(separateLR: false, rightButton: true)))
                return (IntPtr)1;
            if (TryHandleClickKey(settings.controlledMulticlickRegularClickUseMouseButton,
                    settings.controlledMulticlickRegularClickKeyCode, settings.controlledMulticlickRegularClickTriggerOnRelease,
                    c => c.TriggerRegularClick(false)))
                return (IntPtr)1;
            if (TryHandleClickKey(settings.controlledMulticlickRegularClickRightUseMouseButton,
                    settings.controlledMulticlickRegularClickRightKeyCode, settings.controlledMulticlickRegularClickRightTriggerOnRelease,
                    c => c.TriggerRegularClick(true)))
                return (IntPtr)1;

            // ── Activation key (toggle / hold; non-global gated on focus) ──
            uint activateKeyCode = (uint)settings.controlledMulticlickActivateKeyCode;
            if (activateKeyCode != 0 && vk == activateKeyCode)
            {
                bool isGlobal = settings.controlledMulticlickActivateGlobal;
                bool mcActive = host._controller?.IsActive ?? false;
                bool gameWindowActive = host._controller?.AllControllersWithWindows.Any(c => c.IsWindowActive) ?? false;

                // Non-global: only trigger when MC or a game window is focused
                if (!isGlobal && !mcActive && !gameWindowActive)
                    return Win32.CallNextHookEx(_controlledMcKeyboardHookHandle, nCode, wParam, lParam);

                bool holdMode = settings.controlledMulticlickActivateHold;

                if (isDown)
                {
                    // Suppress modifier-modified presses so e.g. Ctrl+key still passes through
                    short alt   = Win32.GetAsyncKeyState(Keys.Menu);
                    short ctrl  = Win32.GetAsyncKeyState(Keys.ControlKey);
                    short shift = Win32.GetAsyncKeyState(Keys.ShiftKey);
                    if ((alt & 0x8000) != 0 || (ctrl & 0x8000) != 0 || (shift & 0x8000) != 0)
                        return Win32.CallNextHookEx(_controlledMcKeyboardHookHandle, nCode, wParam, lParam);

                    host._shell.BeginInvoke(() =>
                    {
                        var c = _controlledMcKeyboardHookHost?._controller;
                        if (c == null) return;
                        if (holdMode)
                        {
                            if (!c.IsControlledMulticlickMode)
                                c.EnterControlledMulticlickMode();
                        }
                        else
                        {
                            // Toggle
                            if (c.IsControlledMulticlickMode)
                                c.ExitControlledMulticlickMode();
                            else
                                c.EnterControlledMulticlickMode();
                        }
                    });
                    return (IntPtr)1; // consume
                }

                if (isUp && holdMode)
                {
                    host._shell.BeginInvoke(() =>
                    {
                        var c = _controlledMcKeyboardHookHost?._controller;
                        if (c == null) return;

                        // A click key that doubles as the activation key is handled (and its click fired)
                        // by the click-key blocks above before we ever reach here, so just end the mode.
                        c.ExitControlledMulticlickMode();
                    });
                    return (IntPtr)1; // consume
                }

                return Win32.CallNextHookEx(_controlledMcKeyboardHookHandle, nCode, wParam, lParam);
            }

            return Win32.CallNextHookEx(_controlledMcKeyboardHookHandle, nCode, wParam, lParam);
        }

        // ── Precise Click Mode: focus-block mouse hook + fake cursors ─────

        private static IntPtr _controlledMcFocusBlockHookHandle = IntPtr.Zero;
        private static InputCaptureHost _controlledMcFocusBlockHookHost = null;
        private static Win32.HookProc _controlledMcFocusBlockHookProc = null;

        private readonly IUiTimer _fakeCursorTimer;

        private void Controller_ControlledMulticlickModeChanged(object sender, EventArgs e)
        {
            if (_controller.IsControlledMulticlickMode)
            {
                if (!_fakeCursorTimer.Enabled)
                    _fakeCursorTimer.Start();
                InstallControlledMcFocusBlockHook();
                // Ensure the shared keyboard dispatcher is present so the multi-click / regular-click keys
                // work while the mode is active (it may not be installed if no activation key is configured).
                InstallControlledMcKeyboardHook();
            }
            else
            {
                StopFakeCursors();
                UninstallControlledMcFocusBlockHook();
                // Keep the dispatcher installed if an activation key is still configured (needed to re-enter
                // the mode); otherwise there's nothing left for it to do, so remove it. (PERF-02)
                if (Properties.Settings.Default.controlledMulticlickActivateKeyCode == 0)
                    UninstallControlledMcKeyboardHook();
            }
        }

        private void InstallControlledMcFocusBlockHook()
        {
            if (_controlledMcFocusBlockHookHandle != IntPtr.Zero)
                return;
            _controlledMcFocusBlockHookHost = this;
            if (_controlledMcFocusBlockHookProc == null)
                _controlledMcFocusBlockHookProc = ControlledMcFocusBlockHookProc;
            _controlledMcFocusBlockHookHandle = TryInstallLowLevelHook(Win32.WH_MOUSE_LL, _controlledMcFocusBlockHookProc, "Precise Click focus block");
        }

        private void UninstallControlledMcFocusBlockHook()
        {
            if (_controlledMcFocusBlockHookHandle != IntPtr.Zero)
            {
                Win32.UnhookWindowsHookEx(_controlledMcFocusBlockHookHandle);
                _controlledMcFocusBlockHookHandle = IntPtr.Zero;
            }
            _controlledMcFocusBlockHookHost = null;
        }

        private static IntPtr ControlledMcFocusBlockHookProc(int nCode, IntPtr wParam, IntPtr lParam)
        {
            if (nCode < 0)
                return Win32.CallNextHookEx(_controlledMcFocusBlockHookHandle, nCode, wParam, lParam);

            var host = _controlledMcFocusBlockHookHost;
            if (host?._controller?.IsControlledMulticlickMode != true)
                return Win32.CallNextHookEx(_controlledMcFocusBlockHookHandle, nCode, wParam, lParam);

            int msg = (int)wParam.ToInt64();
            bool isButtonDown =
                msg == (int)Win32.WM.LBUTTONDOWN ||
                msg == (int)Win32.WM.RBUTTONDOWN ||
                msg == (int)Win32.WM.MBUTTONDOWN ||
                msg == (int)Win32.WM.XBUTTONDOWN;
            bool isButtonUp =
                msg == (int)Win32.WM.LBUTTONUP ||
                msg == (int)Win32.WM.RBUTTONUP ||
                msg == (int)Win32.WM.MBUTTONUP ||
                msg == (int)Win32.WM.XBUTTONUP;

            if (!isButtonDown && !isButtonUp)
                return Win32.CallNextHookEx(_controlledMcFocusBlockHookHandle, nCode, wParam, lParam);

            var hookStruct = (Win32.MSLLHOOKSTRUCT)Marshal.PtrToStructure(lParam, typeof(Win32.MSLLHOOKSTRUCT));

            // Resolve logical button index (0=Left,1=Right,2=Middle,3=X1,4=X2)
            int buttonIndex = -1;
            if (msg == (int)Win32.WM.LBUTTONDOWN || msg == (int)Win32.WM.LBUTTONUP) buttonIndex = 0;
            else if (msg == (int)Win32.WM.RBUTTONDOWN || msg == (int)Win32.WM.RBUTTONUP) buttonIndex = 1;
            else if (msg == (int)Win32.WM.MBUTTONDOWN || msg == (int)Win32.WM.MBUTTONUP) buttonIndex = 2;
            else if (msg == (int)Win32.WM.XBUTTONDOWN || msg == (int)Win32.WM.XBUTTONUP)
            {
                int xBtn = (int)(hookStruct.mouseData >> 16);
                buttonIndex = (xBtn == 1) ? 3 : 4;
            }

            // ── Mirror / passthrough click mouse binds ──
            // Mirror clicks send that button to every window (fire on press); passthrough clicks send it to
            // only the hovered window and honour their own trigger-on-release setting.  Consumes the matched
            // button (down and up) so the real click never reaches the game and steals focus.
            var s = Properties.Settings.Default;
            bool TryClickButton(bool useMouse, int button, bool triggerOnRelease, bool rightButton, bool mirror)
            {
                if (!useMouse || buttonIndex != button) return false;
                bool shouldFire = triggerOnRelease ? isButtonUp : isButtonDown;
                if (shouldFire)
                {
                    host._shell.BeginInvoke(() =>
                    {
                        var c = _controlledMcFocusBlockHookHost?._controller;
                        if (c == null) return;
                        if (mirror) c.TriggerInstantMultiClick(separateLR: false, rightButton: rightButton);
                        else c.TriggerRegularClick(rightButton);
                    });
                }
                return true;
            }

            if (TryClickButton(s.controlledMulticlickMirrorLeftUseMouseButton,        s.controlledMulticlickMirrorLeftMouseButton,        false,                                                            false, true))  return (IntPtr)1;
            if (TryClickButton(s.controlledMulticlickMirrorRightUseMouseButton,       s.controlledMulticlickMirrorRightMouseButton,       false,                                                            true,  true))  return (IntPtr)1;
            if (TryClickButton(s.controlledMulticlickRegularClickUseMouseButton,      s.controlledMulticlickRegularClickMouseButton,      s.controlledMulticlickRegularClickTriggerOnRelease,      false, false)) return (IntPtr)1;
            if (TryClickButton(s.controlledMulticlickRegularClickRightUseMouseButton, s.controlledMulticlickRegularClickRightMouseButton, s.controlledMulticlickRegularClickRightTriggerOnRelease, true,  false)) return (IntPtr)1;

            // Block left/right button down on game windows to prevent unwanted focus changes
            IntPtr hwndUnderCursor = Win32.WindowFromPoint(hookStruct.pt);
            bool isGameWindow = host._controller.AllControllersWithWindows
                .Any(c => c.WindowHandle == hwndUnderCursor);
            if (isGameWindow && isButtonDown && (buttonIndex == 0 || buttonIndex == 1))
                return (IntPtr)1; // consume: prevents focus change

            return Win32.CallNextHookEx(_controlledMcFocusBlockHookHandle, nCode, wParam, lParam);
        }

        private void StopFakeCursors()
        {
            _fakeCursorTimer?.Stop();
            if (_controller == null) return;
            foreach (var c in _controller.AllControllersWithWindows)
                c.ShowFakeCursor = false;
        }

        /// <summary>
        /// Shows the fake cursor on every game window except the one the real cursor is over, broadcasting
        /// the hovered window's local position to all. Runs while Precise Click Mode is active.
        /// </summary>
        private void FakeCursorTick()
        {
            if (_controller?.IsControlledMulticlickMode != true)
            {
                StopFakeCursors();
                return;
            }

            System.Drawing.Point screenCursor = Win32.GetCursorPosition();

            var activeControllers = _controller.ActiveControllers
                .Where(c => c.HasWindow && Win32.GetWindowShowState(c.WindowHandle) != Win32.ShowWindowCommands.ShowMinimized)
                .ToList();

            // Phase 1: find which active window the real cursor is over and its local position.
            ToontownController hoveredController = null;
            System.Drawing.Point hoveredLocalPos = System.Drawing.Point.Empty;
            foreach (var c in activeControllers)
            {
                System.Drawing.Point loc = Win32.GetWindowClientAreaLocation(c.WindowHandle);
                System.Drawing.Size size = c.WindowSize;
                if (screenCursor.X >= loc.X && screenCursor.X < loc.X + size.Width
                    && screenCursor.Y >= loc.Y && screenCursor.Y < loc.Y + size.Height)
                {
                    hoveredController = c;
                    hoveredLocalPos = new System.Drawing.Point(screenCursor.X - loc.X, screenCursor.Y - loc.Y);
                    break;
                }
            }

            // Phase 2: broadcast that local position to every other active window; hide fake cursors on all
            // non-active controllers. (activeControllers already filtered to non-minimized, PERF-02.)
            var activeSet = new HashSet<ToontownController>(activeControllers);
            foreach (var c in _controller.AllControllersWithWindows)
            {
                if (!activeSet.Contains(c) || c == hoveredController)
                {
                    c.ShowFakeCursor = false;
                    continue;
                }

                if (hoveredController != null)
                    c.UpdateFakeCursor(true, hoveredLocalPos);
                else
                    c.ShowFakeCursor = false; // cursor isn't over any active game window
            }
        }

        // ── Engine event handlers (input-capture concerns only) ─────────────────────

        private void Controller_ShouldActivate(object sender, EventArgs e)
        {
            TryActivate();
        }

        private void Controller_WindowActivated(object sender, EventArgs e)
        {
            // Re-register hotkeys when a game window becomes active (only if the focus state changed).
            RegisterFocusHotkeys();
        }

        private void Controller_AllWindowsInactive(object sender, EventArgs e)
        {
            RegisterFocusHotkeys();
        }

        private void Controller_GroupsChanged(object sender, EventArgs e)
        {
            // Re-register hotkeys if the multicontroller is active (groups may have been added/removed)
            if (_controller.IsActive || _controller.AllControllersWithWindows.Any(c => c.IsWindowActive))
            {
                RegisterHotkey();
            }

            // Auto-find hotkey only registers when the shell window is active
            if (_controller.IsActive)
            {
                RegisterAutoFindHotkey();
                RegisterLayoutPresetHotkeys();
            }
            RegisterMinimizeUnconnectedHotkey();
        }

        // ── Lifecycle ───────────────────────────────────────────────────────────────

        /// <summary>Registration to perform when the shell window is first shown already focused.</summary>
        internal void OnShellShown()
        {
            RegisterAutoFindHotkey();
            RegisterLayoutPresetHotkeys();
            RegisterMinimizeUnconnectedHotkey();
        }

        /// <summary>
        /// Settings were reloaded (Options OK): reset suspension, allow re-reporting hotkey conflicts (UX-01),
        /// and rebuild all registrations for the new settings.
        /// </summary>
        internal void OnSettingsReloaded()
        {
            _globalHotkeysSuspended = false;
            SuspendStateChanged?.Invoke(this, EventArgs.Empty);
            _reportedHotkeyFailures.Clear();

            UnregisterControlledMulticlickHotkeys();
            RegisterFocusHotkeys(force: true);
            RegisterControlledMulticlickHotkeys();
        }

        /// <summary>
        /// Remove hooks, hotkeys, and timers before the shell HWND is destroyed so input is not processed by
        /// orphaned low-level hooks (avoids cursor lag or erratic movement after exit).
        /// </summary>
        public void Dispose()
        {
            _closing = true;
            _cancelActivation = true;

            // Stop the watchdog first so it can't re-arm a hook being uninstalled during teardown.
            _hookWatchdogTimer?.Stop();

            StopFakeCursors();

            UnregisterControlledMulticlickHotkeys();
            UninstallControlledMcFocusBlockHook();

            UninstallMulticlickMouseHook();
            UninstallMinimizeUnconnectedKeyboardHook();

            UnregisterAutoFindHotkey();
            UnregisterLayoutPresetHotkeys();
            UnregisterMinimizeUnconnectedHotkey();

            UnregisterHotkey();

            _controller?.ShutdownUninstallSwitchingMouseHook();

            _controller.ShouldActivate -= Controller_ShouldActivate;
            _controller.WindowActivated -= Controller_WindowActivated;
            _controller.AllWindowsInactive -= Controller_AllWindowsInactive;
            _controller.GroupsChanged -= Controller_GroupsChanged;
            _controller.ControlledMulticlickModeChanged -= Controller_ControlledMulticlickModeChanged;
            _controller.InputCaptureFailed -= Controller_InputCaptureFailed;

            _fakeCursorTimer?.Dispose();
            _hookWatchdogTimer?.Dispose();

            if (_current == this)
                _current = null;
        }
    }
}
