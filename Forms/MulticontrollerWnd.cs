using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Linq;
using System.Text;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;
using System.Runtime.Serialization.Formatters.Binary;
using System.Diagnostics;
using TTMulti;

namespace TTMulti.Forms
{
    /// <summary>
    /// The main window. This window captures all input sent to the window and child controls by 
    /// implementing IMessageFilter and overriding ProcessCmdKey(). All input is sent to the Multicontroller class.
    /// A low-level keyboard hook is also used to listen for the mode key when a Toontown window is active.
    /// </summary>
    internal partial class MulticontrollerWnd : Form, IMessageFilter
    {
        /// <summary>
        /// This flag is used to ignore input while a dialog is open.
        /// </summary>
        bool ignoreMessages = false;

        /// <summary>
        /// The thread used to work around activation issues.
        /// </summary>
        Thread activationThread = null;

        Multicontroller controller;

        bool hotkeyRegistered = false;
        bool userPromptedForAdminRights = false;

        /// <summary>
        /// When true, global-style captures (RegisterHotKey 0–3, layout presets, minimize-unconnected global path, multiclick mouse hook) are off so keys reach the game; id 4 (suspend toggle) stays registered.
        /// </summary>
        bool _globalHotkeysSuspended = false;

        // Low-level keyboard hook for minimize-unconnected when no modifier is set (RegisterHotKey doesn't work globally for single keys)
        private static IntPtr _minimizeUnconnectedKeyboardHookHandle = IntPtr.Zero;
        private static MulticontrollerWnd _minimizeUnconnectedHookForm = null;
        private static int _minimizeUnconnectedHookKeyCode = 0;
        private static Win32.HookProc _minimizeUnconnectedKeyboardHookProc = null;

        // Low-level mouse hook for instant multi-click when using a mouse button (RegisterHotKey does not support mouse)
        private static IntPtr _multiclickMouseHookHandle = IntPtr.Zero;
        private static MulticontrollerWnd _multiclickMouseHookForm = null;
        private static int _multiclickMouseHookButton = -1; // 0=Middle, 1=XButton1, 2=XButton2
        private static Win32.HookProc _multiclickMouseHookProc = null;

        // Low-level keyboard hook for Controlled Multi-Click Mode activation key
        private static IntPtr _controlledMcActivateHookHandle = IntPtr.Zero;
        private static MulticontrollerWnd _controlledMcActivateHookForm = null;
        private static int _controlledMcActivateHookKeyCode = 0;
        private static Win32.HookProc _controlledMcActivateHookProc = null;

        // Low-level keyboard hook for Controlled Multi-Click Mode: multi-click key
        private static IntPtr _controlledMcClickHookHandle = IntPtr.Zero;
        private static MulticontrollerWnd _controlledMcClickHookForm = null;
        private static int _controlledMcClickHookKeyCode = 0;
        private static Win32.HookProc _controlledMcClickHookProc = null;

        // Low-level keyboard hook for Controlled Multi-Click Mode: regular-click key
        private static IntPtr _controlledMcRegularClickHookHandle = IntPtr.Zero;
        private static MulticontrollerWnd _controlledMcRegularClickHookForm = null;
        private static int _controlledMcRegularClickHookKeyCode = 0;
        private static Win32.HookProc _controlledMcRegularClickHookProc = null;

        // Low-level mouse hook that blocks left-clicks from focusing game windows while in Controlled Multi-Click Mode
        private static IntPtr _controlledMcFocusBlockHookHandle = IntPtr.Zero;
        private static MulticontrollerWnd _controlledMcFocusBlockHookForm = null;
        private static Win32.HookProc _controlledMcFocusBlockHookProc = null;

        // Timer that updates fake-cursor positions on all game windows while in Controlled Multi-Click Mode
        private System.Windows.Forms.Timer _multiclickFakeCursorTimer;

        internal MulticontrollerWnd()
        {
            InitializeComponent();
            this.Icon = Properties.Resources.icon;
        }

        /// <summary>
        /// Activates the window.
        /// Works around an issue where sometimes calling Activate() doesn't activate the window.
        /// If calling Activate() doesn't work, this makes the window topmost and fakes a mouse event.
        /// </summary>
        // Set to true to cancel a running TryActivate loop (e.g. when the user deliberately focuses another window)
        private volatile bool _cancelActivation = false;

        // Set once the form starts closing so no new activation thread is spawned during teardown.
        private volatile bool _closing = false;

        internal void TryActivate()
        {
            if (_closing)
                return;

            _cancelActivation = false;

            // IsAlive (not ThreadState) is the correct liveness test: the thread is IsBackground, so its
            // ThreadState always carries the Background flag and never equals Running — the old check let
            // multiple activation threads run at once and interleave AttachThreadInput/TopMost (CORR-01).
            if (activationThread == null || !activationThread.IsAlive)
            {
                activationThread = new Thread(activationThreadFunc) { IsBackground = true };
                activationThread.Start();
            }
        }

        /// <summary>
        /// Marshal <paramref name="action"/> to the UI thread, returning false instead of throwing if the form's
        /// handle is gone.  Prevents a dispose race (form closed between the check and the Invoke) from throwing on
        /// the background activation thread, which — with no global handler installed — would crash the process (CORR-01).
        /// </summary>
        private bool SafeInvoke(Action action)
        {
            try
            {
                if (IsDisposed || !IsHandleCreated)
                    return false;
                Invoke(action);
                return true;
            }
            catch (ObjectDisposedException)
            {
                return false;
            }
            catch (InvalidOperationException)
            {
                return false;
            }
        }

        private void activationThreadFunc()
        {
            try
            {
                if (_cancelActivation)
                    return;

                IntPtr hWnd = IntPtr.Zero;
                if (!SafeInvoke(() => hWnd = this.Handle) || _cancelActivation || hWnd == IntPtr.Zero)
                    return;

                // Use AttachThreadInput so we can call SetForegroundWindow without Windows
                // redirecting it to a taskbar flash.  We borrow the current foreground thread's
                // input queue, steal focus cleanly, then detach.
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

                    SafeInvoke(() =>
                    {
                        if (!this.IsDisposed && !_cancelActivation)
                        {
                            this.TopMost = true;
                            this.Activate();
                            this.TopMost = Properties.Settings.Default.onTopWhenInactive;
                        }
                    });
                }
                finally
                {
                    // Always detach the input queue we borrowed, even if activation was cancelled or failed.
                    if (attached)
                        Win32.AttachThreadInput(foregroundThread, ourThread, false);
                }
            }
            catch
            {
                // Best-effort activation.  An unhandled exception on this background thread would crash the whole
                // process (no AppDomain/thread exception handler is installed), so swallow it (CORR-01).
            }
        }

        /// <summary>
        /// Short label for the status strip: mode name and current group when relevant (e.g. "Multi G2", "Mirror").
        /// </summary>
        private string GetStatusModeSummaryText()
        {
            int g = controller.CurrentGroupIndex + 1;
            switch (controller.CurrentMode)
            {
                case MulticontrollerMode.Group:
                    return "Multi Mode G" + g;
                case MulticontrollerMode.MirrorAll:
                    return "Mirror Mode";
                case MulticontrollerMode.AllGroup:
                    return "All Groups Mode";
                case MulticontrollerMode.Focused:
                    return "Focused Mode";
                case MulticontrollerMode.Custom:
                    var def = controller.GetActiveCustomModeDefinition();
                    return def != null && !string.IsNullOrWhiteSpace(def.Name) ? def.Name : "Custom";
                case MulticontrollerMode.Pair:
                    return "Pair G" + g;
                case MulticontrollerMode.MirrorGroup:
                    return "Mirror group G" + g;
                case MulticontrollerMode.MirrorIndividual:
                    return "Mirror one";
                default:
                    return controller.CurrentMode.ToString();
            }
        }

        /// <summary>
        /// Updates the window selectors and group status.
        /// This should be called when the current group or window selection changes.
        /// </summary>
        internal void UpdateWindowStatus()
        {
            leftToonCrosshair.SelectedWindowHandle = controller.LeftControllers.First().WindowHandle;
            rightToonCrosshair.SelectedWindowHandle = controller.RightControllers.First().WindowHandle;

            leftStatusLbl.Text = GetStatusModeSummaryText();
            rightStatusLbl.Text = controller.ControllerGroups.Count + " groups.";
            UpdateModeLockVisuals();

            if (!statusStrip1.Visible && controller.ControllerGroups.Count > 1 && controller.CurrentMode != MulticontrollerMode.AllGroup)
            {
                statusStrip1.Visible = true;
                this.Padding = new Padding(this.Padding.Left, this.Padding.Top, this.Padding.Right, this.Padding.Bottom + statusStrip1.Height);
            }
            else if (statusStrip1.Visible && (controller.ControllerGroups.Count == 1 || controller.CurrentMode == MulticontrollerMode.AllGroup))
            {
                this.Padding = new Padding(this.Padding.Left, this.Padding.Top, this.Padding.Right, this.Padding.Bottom - statusStrip1.Height);
                statusStrip1.Visible = false;
            }
        }

        /// <summary>
        /// Overrides keys that usually perform other functions like tab, arrow keys, etc. so that
        /// we can use them for control. After getting intercepted, they are caught by the message filter.
        /// </summary>
        /// <param name="msg"></param>
        /// <param name="keyData"></param>
        /// <returns>
        /// Returns true when the key should be intercepted so they don't perform their usual function.
        /// </returns>
        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            switch (keyData)
            {
                case Keys.Tab:
                case Keys.Up:
                case Keys.Down:
                case Keys.Left:
                case Keys.Right:
                    return true;
                case Keys.Alt:
                    // Forward Alt key to ProcessInput for switching mode handling
                    // Don't consume it here - let ProcessInput decide
                    return controller.ProcessInput(msg.Msg, msg.WParam, msg.LParam);
                default:
                    break;
            }

            return base.ProcessCmdKey(ref msg, keyData);
        }

        /// <summary>
        /// IMessageFilter function implementation. This captures all keys sent to the window, including ones
        /// that are sent directly to child controls, and sends them to the multicontroller.
        /// </summary>
        /// <param name="m"></param>
        /// <returns>
        /// Returns true when the key should be stopped from getting to its destination.
        /// </returns>
        public bool PreFilterMessage(ref Message m)
        {
            if (ignoreMessages)
            {
                return false;
            }

            bool ret = false;

            var msg = (Win32.WM)m.Msg;

            switch (msg)
            {
                case Win32.WM.KEYDOWN:
                case Win32.WM.KEYUP:
                case Win32.WM.SYSKEYDOWN:
                case Win32.WM.SYSKEYUP:
                case Win32.WM.SYSCOMMAND:
                    ret = controller.ProcessInput(m.Msg, m.WParam, m.LParam);
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
                    ret = controller.ProcessInput(m.Msg, m.WParam, m.LParam);
                    break;
                case Win32.WM.HOTKEY:
                    // Let these hotkeys pass through to WndProc (see WndProc). Others are handled via ProcessInput.
                    // Custom mode activation uses IDs CustomModeActivationHotkeyIdStart..End — must not go through ProcessInput alone.
                    int hotkeyId = m.WParam.ToInt32();
                    if (hotkeyId == 3 || hotkeyId == 4 || hotkeyId == 7 || hotkeyId == 9 || (hotkeyId >= 10 && hotkeyId <= 25)
                        || (hotkeyId >= CustomModeActivationHotkeyIdStart && hotkeyId <= CustomModeActivationHotkeyIdEnd))
                    {
                        ret = false;
                    }
                    else
                    {
                        // Process other hotkeys normally
                        ret = controller.ProcessInput(m.Msg, m.WParam, m.LParam);
                    }
                    break;
            }
            
            CheckControllerErrors();

            return ret;
        }

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == (int)Win32.WM.HOTKEY)
            {
                int hotkeyId = m.WParam.ToInt32();
                
                // Check if this is auto-find windows hotkey (ID 7)
                if (hotkeyId == 7)
                {
                    controller.AutoFindAndAssignWindows();
                    if (controller.IsActive || controller.AllControllersWithWindows.Any(c => c.IsWindowActive))
                    {
                        RegisterHotkey();
                    }
                    if (controller.IsActive)
                    {
                        RegisterAutoFindHotkey();
                        RegisterLayoutPresetHotkeys();
                    }
                }
                // Check if this is minimize unconnected Toontown windows hotkey (ID 9)
                else if (hotkeyId == 9)
                {
                    controller.ToggleMinimizeUnconnectedWindows();
                }
                else if (hotkeyId == 4)
                {
                    _globalHotkeysSuspended = !_globalHotkeysSuspended;
                    RefreshGlobalHotkeyRegistration();
                }
                // Layout preset hotkeys (ID 10-25)
                else if (hotkeyId >= 10 && hotkeyId <= 25)
                {
                    int presetIndex = hotkeyId - 10;
                    var file = LayoutPresetStorage.Load();
                    if (file?.Presets != null && presetIndex < file.Presets.Count)
                    {
                        controller.ApplyLayoutPreset(file.Presets[presetIndex]);
                        Properties.Settings.Default.lastUsedLayoutPresetIndex = presetIndex;
                        Properties.Settings.Default.Save();
                        BeginInvoke(new Action(() => TryActivate()));
                    }
                }
                else if (hotkeyId == 3)
                {
                    controller.ToggleModeLock();
                    UpdateModeLockVisuals();
                }
                else if (hotkeyId == 0)
                {
                    // Mode switching hotkey (ID 0)
                    // Check if any modifiers are currently pressed - if so, don't switch modes, let it pass through to games
                    Keys currentModifiers = Control.ModifierKeys;
                    bool hasModifiers = (currentModifiers & (Keys.Shift | Keys.Control | Keys.Alt)) != Keys.None;
                    
                    if (hasModifiers)
                    {
                        // Modifiers are pressed - don't switch modes, let the key pass through to games
                        // Convert HOTKEY message to KEYDOWN so it gets processed normally
                        Keys keyCode = (Keys)Properties.Settings.Default.modeKeyCode;
                        controller.ProcessInput((int)Win32.WM.KEYDOWN, (IntPtr)keyCode, IntPtr.Zero);
                    }
                    else
                    {
                        // No modifiers - handle as mode switch
                        controller.ProcessInput(m.Msg, m.WParam, m.LParam);
                    }
                }
                else if (hotkeyId == 1)
                {
                    // Instant Multi-Click hotkey (ID 1)
                    // Check if any modifiers are currently pressed - if so, don't execute multi-click, let it pass through to games
                    Keys currentModifiers = Control.ModifierKeys;
                    bool hasModifiers = (currentModifiers & (Keys.Shift | Keys.Control | Keys.Alt)) != Keys.None;
                    
                    if (hasModifiers)
                    {
                        // Modifiers are pressed - don't execute multi-click, let the key pass through to games
                        // Convert HOTKEY message to KEYDOWN so it gets processed normally
                        Keys keyCode = (Keys)Properties.Settings.Default.replicateMouseKeyCode;
                        controller.ProcessInput((int)Win32.WM.KEYDOWN, (IntPtr)keyCode, IntPtr.Zero);
                    }
                    else
                    {
                        // No modifiers - handle as multi-click
                        controller.ProcessInput(m.Msg, m.WParam, m.LParam);
                    }
                }
                else if (hotkeyId >= CustomModeActivationHotkeyIdStart && hotkeyId <= CustomModeActivationHotkeyIdEnd)
                {
                    if (_customModeActivationHotkeyIds.TryGetValue(hotkeyId, out string customModeId) && !string.IsNullOrEmpty(customModeId))
                    {
                        Keys currentModifiers = Control.ModifierKeys;
                        bool hasModifiers = (currentModifiers & (Keys.Shift | Keys.Control | Keys.Alt)) != Keys.None;
                        if (hasModifiers)
                        {
                            var file = CustomModeStorage.Load();
                            var mode = file.Modes?.FirstOrDefault(cm => string.Equals(cm.Id, customModeId, StringComparison.Ordinal));
                            if (mode != null && mode.ActivationHotkeyCode != 0)
                                controller.ProcessInput((int)Win32.WM.KEYDOWN, (IntPtr)(Keys)mode.ActivationHotkeyCode, IntPtr.Zero);
                        }
                        else
                        {
                            if (!controller.IsActive)
                                BeginInvoke(new Action(TryActivate));
                            controller.ActivateCustomModeDefinition(customModeId);
                        }
                    }
                }
                else if (hotkeyId == 2)
                {
                    // Zero Power Throw hotkey (ID 2)
                    // Check if any modifiers are currently pressed - if so, don't execute zero power throw, let it pass through to games
                    Keys currentModifiers = Control.ModifierKeys;
                    bool hasModifiers = (currentModifiers & (Keys.Shift | Keys.Control | Keys.Alt)) != Keys.None;
                    
                    if (hasModifiers)
                    {
                        // Modifiers are pressed - don't execute zero power throw, let the key pass through to games
                        // Convert HOTKEY message to KEYDOWN so it gets processed normally
                        Keys keyCode = (Keys)Properties.Settings.Default.zeroPowerThrowKeyCode;
                        controller.ProcessInput((int)Win32.WM.KEYDOWN, (IntPtr)keyCode, IntPtr.Zero);
                    }
                    else
                    {
                        // No modifiers - handle as zero power throw
                        controller.ProcessInput(m.Msg, m.WParam, m.LParam);
                    }
                }
                else
                {
                    controller.ProcessInput(m.Msg, m.WParam, m.LParam);
                }
                
                CheckControllerErrors();
            }

            base.WndProc(ref m);
        }

        internal void CheckControllerErrors()
        {
            if (!userPromptedForAdminRights && controller.ErrorOccurredPostingMessage)
            {
                userPromptedForAdminRights = true;

                if (MessageBox.Show(
                    "There was an error controlling a Toontown window. You may need to run the multicontroller as administrator.\n\nDo you want to re-launch as administrator?",
                    "Error",
                    MessageBoxButtons.YesNo) == DialogResult.Yes)
                {
                    Properties.Settings.Default.runAsAdministrator = true;
                    Properties.Settings.Default.Save();

                    if (Program.TryRunAsAdmin())
                    {
                        Application.Exit();
                    }
                    else
                    {
                        MessageBox.Show("Failed to re-launch as administrator.", "Error");
                    }
                }
            }
        }

        internal void SaveWindowPosition()
        {
            Properties.Settings.Default.lastLocation = this.Location;
            Properties.Settings.Default.Save();
        }
        
        private void ReloadOptions()
        {
            _globalHotkeysSuspended = false;
            this.TopMost = Properties.Settings.Default.onTopWhenInactive;
            panel1.Visible = !Properties.Settings.Default.compactUI;
            controller.UpdateOptions();
            controller.RefreshAllControllerBorders();

            // Update UI colors to reflect any changes
            UpdateUIColors();
            
            // Unregister all hotkeys
            UnregisterHotkey();
            UnregisterAutoFindHotkey();
            UnregisterLayoutPresetHotkeys();
            UnregisterMinimizeUnconnectedHotkey();
            UnregisterControlledMulticlickHotkeys();
            
            // Re-register all hotkeys based on current settings and state
            RegisterHotkey();

            if (controller.IsActive)
            {
                RegisterAutoFindHotkey();
                RegisterLayoutPresetHotkeys();
            }
            RegisterMinimizeUnconnectedHotkey();
            RegisterControlledMulticlickHotkeys();

        }
        
        /// <summary>
        /// Updates the colors of the mode buttons and crosshair controls to reflect current color settings.
        /// </summary>
        private void UpdateUIColors()
        {
            // Update Multi-Mode button colors
            multiModeRadio.FlatAppearance.BorderColor = Colors.LeftGroup;
            multiModeRadio.FlatAppearance.CheckedBackColor = Colors.LeftGroup;
            
            // Update Mirror Mode button colors
            mirrorModeRadio.FlatAppearance.BorderColor = Colors.AllGroups;
            mirrorModeRadio.FlatAppearance.CheckedBackColor = Colors.AllGroups;
            
            // Update crosshair colors
            leftToonCrosshair.SelectedBorderColor = Colors.LeftGroup;
            rightToonCrosshair.SelectedBorderColor = Colors.RightGroup;
            
            // Force a repaint of the buttons to show the updated colors
            multiModeRadio.Invalidate();
            mirrorModeRadio.Invalidate();
        }

        private void RegisterHotkey()
        {
            UninstallMulticlickMouseHook();
            Win32.UnregisterHotKey(this.Handle, 0);
            Win32.UnregisterHotKey(this.Handle, 1);
            Win32.UnregisterHotKey(this.Handle, 2);
            Win32.UnregisterHotKey(this.Handle, 3);
            Win32.UnregisterHotKey(this.Handle, 4);
            UnregisterCustomModeActivationHotkeys();

            // Suspend-global toggle (ID 4) — always registered when configured, including while suspended, so the user can turn globals back on.
            if (Properties.Settings.Default.suspendGlobalHotkeysToggleKeyCode != 0)
                Win32.RegisterHotKey(this.Handle, 4, Win32.KeyModifiers.None, (Keys)Properties.Settings.Default.suspendGlobalHotkeysToggleKeyCode);

            if (_globalHotkeysSuspended)
                return;

            // Mode/Activate (ID 0)
            bool modeGlobal = Properties.Settings.Default.modeHotkeyGlobal;
            if (modeGlobal)
            {
                Win32.RegisterHotKey(this.Handle, 0, Win32.KeyModifiers.None, (Keys)Properties.Settings.Default.modeKeyCode);
            }
            else if (controller.IsActive)
            {
                Win32.RegisterHotKey(this.Handle, 0, Win32.KeyModifiers.None, (Keys)Properties.Settings.Default.modeKeyCode);
            }

            // Instant Multi-Click (ID 1) - keyboard hotkey or mouse hook (RegisterHotKey does not support mouse buttons)
            if (Properties.Settings.Default.replicateMouseUseMouseButton)
            {
                int btn = Properties.Settings.Default.replicateMouseMouseButton;
                if (btn >= 0 && btn <= 2)
                {
                    bool multiGlobal = Properties.Settings.Default.replicateMouseHotkeyGlobal;
                    if (multiGlobal || controller.IsActive)
                        InstallMulticlickMouseHook(btn);
                }
            }
            else if (Properties.Settings.Default.replicateMouseKeyCode != 0)
            {
                bool multiGlobal = Properties.Settings.Default.replicateMouseHotkeyGlobal;
                if (multiGlobal || controller.IsActive)
                {
                    Win32.RegisterHotKey(this.Handle, 1, Win32.KeyModifiers.None, (Keys)Properties.Settings.Default.replicateMouseKeyCode);
                }
            }

            // Zero Power Throw (ID 2)
            if (Properties.Settings.Default.zeroPowerThrowKeyCode != 0)
            {
                bool zeroGlobal = Properties.Settings.Default.zeroPowerThrowHotkeyGlobal;
                if (zeroGlobal)
                {
                    Win32.RegisterHotKey(this.Handle, 2, Win32.KeyModifiers.None, (Keys)Properties.Settings.Default.zeroPowerThrowKeyCode);
                }
                else if (controller.IsActive)
                {
                    Win32.RegisterHotKey(this.Handle, 2, Win32.KeyModifiers.None, (Keys)Properties.Settings.Default.zeroPowerThrowKeyCode);
                }
            }

            // Mode lock toggle (ID 3) — always registered when set so it works from game windows
            if (Properties.Settings.Default.modeLockToggleKeyCode != 0)
                Win32.RegisterHotKey(this.Handle, 3, Win32.KeyModifiers.None, (Keys)Properties.Settings.Default.modeLockToggleKeyCode);
            // Note: ID 7 (auto-find), ID 10-25 (layout presets) handled separately

            RegisterCustomModeActivationHotkeys();
        }

        void UnregisterCustomModeActivationHotkeys()
        {
            _customModeActivationHotkeyIds.Clear();
            for (int id = CustomModeActivationHotkeyIdStart; id <= CustomModeActivationHotkeyIdEnd; id++)
                Win32.UnregisterHotKey(this.Handle, id);
        }

        void RegisterCustomModeActivationHotkeys()
        {
            if (_globalHotkeysSuspended)
                return;
            CustomModeFile file = CustomModeStorage.Load();
            if (file.Modes == null)
                return;
            int hotkeyId = CustomModeActivationHotkeyIdStart;
            foreach (CustomModeDefinition mode in file.Modes)
            {
                if (mode.ActivationHotkeyCode == 0)
                    continue;
                if (hotkeyId > CustomModeActivationHotkeyIdEnd)
                    break;
                bool global = mode.ActivationHotkeyGlobal;
                if (!global && !controller.IsActive)
                    continue;
                bool ok = Win32.RegisterHotKey(this.Handle, hotkeyId, (Win32.KeyModifiers)mode.ActivationHotkeyModifiers, (Keys)mode.ActivationHotkeyCode);
                if (ok)
                    _customModeActivationHotkeyIds[hotkeyId] = mode.Id;
                hotkeyId++;
            }
        }

        /// <summary>
        /// After toggling <see cref="_globalHotkeysSuspended"/>, re-apply hotkey registration so globals, layout presets, and minimize-unconnected follow the new state.
        /// </summary>
        private void RefreshGlobalHotkeyRegistration()
        {
            UnregisterLayoutPresetHotkeys();
            UnregisterMinimizeUnconnectedHotkey();
            UnregisterHotkey();
            RegisterHotkey();
            if (controller.IsActive)
            {
                RegisterAutoFindHotkey();
                if (!_globalHotkeysSuspended)
                    RegisterLayoutPresetHotkeys();
            }
            RegisterMinimizeUnconnectedHotkey();
        }

        private void UnregisterHotkey()
        {
            Win32.UnregisterHotKey(this.Handle, 0);
            Win32.UnregisterHotKey(this.Handle, 1);
            Win32.UnregisterHotKey(this.Handle, 2);
            Win32.UnregisterHotKey(this.Handle, 3);
            Win32.UnregisterHotKey(this.Handle, 4);
            UnregisterCustomModeActivationHotkeys();
            UninstallMulticlickMouseHook();
        }

        private void RegisterAutoFindHotkey()
        {
            // Register auto-find windows hotkey (ID 7) - NEVER global, only when multicontroller window is active
            if (Properties.Settings.Default.autoFindWindowsKeyCode != 0 && controller.IsActive)
            {
                bool success = Win32.RegisterHotKey(this.Handle, 7, (Win32.KeyModifiers)Properties.Settings.Default.autoFindWindowsKeyModifiers, (Keys)Properties.Settings.Default.autoFindWindowsKeyCode);
                if (!success)
                {
                    // Hotkey registration failed - might be already registered or invalid combination
                    // Try unregistering first, then re-registering
                    Win32.UnregisterHotKey(this.Handle, 7);
                    Win32.RegisterHotKey(this.Handle, 7, (Win32.KeyModifiers)Properties.Settings.Default.autoFindWindowsKeyModifiers, (Keys)Properties.Settings.Default.autoFindWindowsKeyCode);
                }
            }
        }

        private void UnregisterAutoFindHotkey()
        {
            // Unregister auto-find hotkey (ID 7)
            Win32.UnregisterHotKey(this.Handle, 7);
        }

        private const int LayoutPresetHotkeyIdStart = 10;
        private const int LayoutPresetHotkeyIdEnd = 25;
        private const int CustomModeActivationHotkeyIdStart = 26;
        private const int CustomModeActivationHotkeyIdEnd = 57;

        readonly System.Collections.Generic.Dictionary<int, string> _customModeActivationHotkeyIds =
            new System.Collections.Generic.Dictionary<int, string>();

        private void RegisterLayoutPresetHotkeys()
        {
            if (_globalHotkeysSuspended || !controller.IsActive) return;
            var file = LayoutPresetStorage.Load();
            if (file?.Presets == null) return;
            for (int i = 0; i < file.Presets.Count && i <= LayoutPresetHotkeyIdEnd - LayoutPresetHotkeyIdStart; i++)
            {
                var p = file.Presets[i];
                if (p.HotkeyCode == 0) continue;
                Win32.RegisterHotKey(this.Handle, LayoutPresetHotkeyIdStart + i, (Win32.KeyModifiers)p.HotkeyModifiers, (Keys)p.HotkeyCode);
            }
        }

        private void UnregisterLayoutPresetHotkeys()
        {
            for (int id = LayoutPresetHotkeyIdStart; id <= LayoutPresetHotkeyIdEnd; id++)
                Win32.UnregisterHotKey(this.Handle, id);
        }

        private void RegisterMinimizeUnconnectedHotkey()
        {
            // Register minimize unconnected Toontown windows hotkey (ID 9)
            // When no modifier: RegisterHotKey doesn't work globally for single keys on Windows, so use a low-level keyboard hook instead.
            // When modifier is set: use RegisterHotKey as normal.
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
                || controller.IsActive
                || controller.AllControllersWithWindows.Any(c => c.IsWindowActive);
            if (!shouldRegister)
            {
                UnregisterMinimizeUnconnectedHotkey();
                return;
            }
            bool noModifiers = (modifiers == 0 || modifiers == (int)Win32.KeyModifiers.None);
            if (noModifiers)
            {
                Win32.UnregisterHotKey(this.Handle, 9);
                InstallMinimizeUnconnectedKeyboardHook(keyCode);
            }
            else
            {
                UninstallMinimizeUnconnectedKeyboardHook();
                bool success = Win32.RegisterHotKey(this.Handle, 9, (Win32.KeyModifiers)modifiers, (Keys)keyCode);
                if (!success)
                {
                    Win32.UnregisterHotKey(this.Handle, 9);
                    Win32.RegisterHotKey(this.Handle, 9, (Win32.KeyModifiers)modifiers, (Keys)keyCode);
                }
            }
        }

        private void UnregisterMinimizeUnconnectedHotkey()
        {
            Win32.UnregisterHotKey(this.Handle, 9);
            UninstallMinimizeUnconnectedKeyboardHook();
        }

        private void InstallMinimizeUnconnectedKeyboardHook(int keyCode)
        {
            if (_minimizeUnconnectedKeyboardHookHandle != IntPtr.Zero)
            {
                if (_minimizeUnconnectedHookKeyCode == keyCode)
                    return;
                UninstallMinimizeUnconnectedKeyboardHook();
            }
            _minimizeUnconnectedHookForm = this;
            _minimizeUnconnectedHookKeyCode = keyCode;
            if (_minimizeUnconnectedKeyboardHookProc == null)
                _minimizeUnconnectedKeyboardHookProc = MinimizeUnconnectedKeyboardHookProc;
            IntPtr hModule = Win32.GetModuleHandle(null);
            _minimizeUnconnectedKeyboardHookHandle = Win32.SetWindowsHookEx(Win32.WH_KEYBOARD_LL, _minimizeUnconnectedKeyboardHookProc, hModule, 0);
        }

        private void UninstallMinimizeUnconnectedKeyboardHook()
        {
            if (_minimizeUnconnectedKeyboardHookHandle != IntPtr.Zero)
            {
                Win32.UnhookWindowsHookEx(_minimizeUnconnectedKeyboardHookHandle);
                _minimizeUnconnectedKeyboardHookHandle = IntPtr.Zero;
            }
            _minimizeUnconnectedHookForm = null;
            _minimizeUnconnectedHookKeyCode = 0;
        }

        private static IntPtr MinimizeUnconnectedKeyboardHookProc(int nCode, IntPtr wParam, IntPtr lParam)
        {
            if (nCode < 0)
                return Win32.CallNextHookEx(_minimizeUnconnectedKeyboardHookHandle, nCode, wParam, lParam);
            int msg = wParam.ToInt32();
            // WM_KEYDOWN = 0x100, WM_SYSKEYDOWN = 0x104
            if (msg != 0x100 && msg != 0x104)
                return Win32.CallNextHookEx(_minimizeUnconnectedKeyboardHookHandle, nCode, wParam, lParam);
            if (_minimizeUnconnectedHookForm == null || _minimizeUnconnectedHookKeyCode == 0)
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
            _minimizeUnconnectedHookForm.BeginInvoke(new Action(() =>
            {
                if (_minimizeUnconnectedHookForm != null && _minimizeUnconnectedHookForm.controller != null)
                    _minimizeUnconnectedHookForm.controller.ToggleMinimizeUnconnectedWindows();
            }));
            return (IntPtr)1; // Consume the key
        }

        private void InstallMulticlickMouseHook(int buttonIndex)
        {
            if (_multiclickMouseHookHandle != IntPtr.Zero)
            {
                if (_multiclickMouseHookButton == buttonIndex)
                    return;
                UninstallMulticlickMouseHook();
            }
            _multiclickMouseHookForm = this;
            _multiclickMouseHookButton = buttonIndex;
            if (_multiclickMouseHookProc == null)
                _multiclickMouseHookProc = MulticlickMouseHookProc;
            IntPtr hModule = Win32.GetModuleHandle(null);
            _multiclickMouseHookHandle = Win32.SetWindowsHookEx(Win32.WH_MOUSE_LL, _multiclickMouseHookProc, hModule, 0);
        }

        private void UninstallMulticlickMouseHook()
        {
            if (_multiclickMouseHookHandle != IntPtr.Zero)
            {
                Win32.UnhookWindowsHookEx(_multiclickMouseHookHandle);
                _multiclickMouseHookHandle = IntPtr.Zero;
            }
            _multiclickMouseHookForm = null;
            _multiclickMouseHookButton = -1;
        }

        // ── Controlled Multi-Click Mode: Activation Key Hook ──────────────────────

        private void RegisterControlledMulticlickHotkeys()
        {
            UnregisterControlledMulticlickHotkeys();
            if (!Properties.Settings.Default.controlledMulticlickEnabled) return;

            int activateKey = Properties.Settings.Default.controlledMulticlickActivateKeyCode;
            if (activateKey != 0)
                InstallControlledMcActivateHook(activateKey);

            // Multi-click key: only install keyboard hook when not using a mouse button
            if (!Properties.Settings.Default.controlledMulticlickClickUseMouseButton)
            {
                int clickKey = Properties.Settings.Default.controlledMulticlickClickKeyCode;
                if (clickKey != 0)
                    InstallControlledMcClickHook(clickKey);
            }

            // Regular-click key: only install keyboard hook when not using a mouse button
            if (!Properties.Settings.Default.controlledMulticlickRegularClickUseMouseButton)
            {
                int regularClickKey = Properties.Settings.Default.controlledMulticlickRegularClickKeyCode;
                if (regularClickKey != 0)
                    InstallControlledMcRegularClickKeyboardHook(regularClickKey);
            }
        }

        private void UnregisterControlledMulticlickHotkeys()
        {
            UninstallControlledMcActivateHook();
            UninstallControlledMcClickHook();
            UninstallControlledMcRegularClickKeyboardHook();
        }

        private void InstallControlledMcActivateHook(int keyCode)
        {
            if (_controlledMcActivateHookHandle != IntPtr.Zero)
            {
                if (_controlledMcActivateHookKeyCode == keyCode) return;
                UninstallControlledMcActivateHook();
            }
            _controlledMcActivateHookForm = this;
            _controlledMcActivateHookKeyCode = keyCode;
            if (_controlledMcActivateHookProc == null)
                _controlledMcActivateHookProc = ControlledMcActivateHookProc;
            IntPtr hModule = Win32.GetModuleHandle(null);
            _controlledMcActivateHookHandle = Win32.SetWindowsHookEx(Win32.WH_KEYBOARD_LL, _controlledMcActivateHookProc, hModule, 0);
        }

        private void UninstallControlledMcActivateHook()
        {
            if (_controlledMcActivateHookHandle != IntPtr.Zero)
            {
                Win32.UnhookWindowsHookEx(_controlledMcActivateHookHandle);
                _controlledMcActivateHookHandle = IntPtr.Zero;
            }
            _controlledMcActivateHookForm = null;
            _controlledMcActivateHookKeyCode = 0;
        }

        /// <summary>
        /// LL keyboard hook for the Controlled Multi-Click Mode activation key.
        /// Toggle mode: KEYDOWN toggles the mode.
        /// Hold mode: KEYDOWN enters the mode, KEYUP exits the mode.
        /// The key is consumed so it never reaches games or the MC window.
        /// Non-global: only activates when MC or a game window is focused.
        /// </summary>
        private static IntPtr ControlledMcActivateHookProc(int nCode, IntPtr wParam, IntPtr lParam)
        {
            if (nCode < 0)
                return Win32.CallNextHookEx(_controlledMcActivateHookHandle, nCode, wParam, lParam);
            if (_controlledMcActivateHookForm == null || _controlledMcActivateHookKeyCode == 0)
                return Win32.CallNextHookEx(_controlledMcActivateHookHandle, nCode, wParam, lParam);

            int msg = wParam.ToInt32();
            bool isDown = msg == 0x100 || msg == 0x104;
            bool isUp   = msg == 0x101 || msg == 0x105;
            if (!isDown && !isUp)
                return Win32.CallNextHookEx(_controlledMcActivateHookHandle, nCode, wParam, lParam);

            var hookStruct = (Win32.KBDLLHOOKSTRUCT)Marshal.PtrToStructure(lParam, typeof(Win32.KBDLLHOOKSTRUCT));
            if ((uint)hookStruct.vkCode != _controlledMcActivateHookKeyCode)
                return Win32.CallNextHookEx(_controlledMcActivateHookHandle, nCode, wParam, lParam);

            var form = _controlledMcActivateHookForm;
            bool isGlobal = Properties.Settings.Default.controlledMulticlickActivateGlobal;
            bool mcActive = form?.controller?.IsActive ?? false;
            bool gameWindowActive = form?.controller?.AllControllersWithWindows.Any(c => c.IsWindowActive) ?? false;

            // Non-global: only trigger when MC or a game window is focused
            if (!isGlobal && !mcActive && !gameWindowActive)
                return Win32.CallNextHookEx(_controlledMcActivateHookHandle, nCode, wParam, lParam);

            bool holdMode = Properties.Settings.Default.controlledMulticlickActivateHold;

            if (isDown)
            {
                // Suppress modifier-modified presses so e.g. Ctrl+key still passes through
                short alt   = Win32.GetAsyncKeyState(Keys.Menu);
                short ctrl  = Win32.GetAsyncKeyState(Keys.ControlKey);
                short shift = Win32.GetAsyncKeyState(Keys.ShiftKey);
                if ((alt & 0x8000) != 0 || (ctrl & 0x8000) != 0 || (shift & 0x8000) != 0)
                    return Win32.CallNextHookEx(_controlledMcActivateHookHandle, nCode, wParam, lParam);

                if (form != null)
                {
                    form.BeginInvoke(new Action(() =>
                    {
                        var c = _controlledMcActivateHookForm?.controller;
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
                    }));
                }
                return (IntPtr)1; // consume
            }

            if (isUp && holdMode)
            {
                if (form != null)
                {
                    form.BeginInvoke(new Action(() =>
                    {
                        var c = _controlledMcActivateHookForm?.controller;
                        if (c == null) return;

                        // If the click key or regular-click key shares this key with trigger-on-release,
                        // fire the click BEFORE exiting so the action isn't lost.
                        uint vk = (uint)_controlledMcActivateHookKeyCode;

                        bool multiClickTor  = Properties.Settings.Default.controlledMulticlickClickTriggerOnRelease;
                        bool multiClickMouse = Properties.Settings.Default.controlledMulticlickClickUseMouseButton;
                        uint multiClickKey  = (uint)Properties.Settings.Default.controlledMulticlickClickKeyCode;
                        if (multiClickTor && !multiClickMouse && multiClickKey == vk && c.IsControlledMulticlickMode)
                            c.TriggerInstantMultiClick(separateLR: Properties.Settings.Default.controlledMulticlickClickSeparateLR);

                        bool regClickTor   = Properties.Settings.Default.controlledMulticlickRegularClickTriggerOnRelease;
                        bool regClickMouse = Properties.Settings.Default.controlledMulticlickRegularClickUseMouseButton;
                        uint regClickKey   = (uint)Properties.Settings.Default.controlledMulticlickRegularClickKeyCode;
                        if (regClickTor && !regClickMouse && regClickKey == vk && c.IsControlledMulticlickMode)
                            c.TriggerRegularClick();

                        c.ExitControlledMulticlickMode();
                    }));
                }
                return (IntPtr)1; // consume
            }

            return Win32.CallNextHookEx(_controlledMcActivateHookHandle, nCode, wParam, lParam);
        }

        // ── Controlled Multi-Click Mode: Click Key Hook ────────────────────────────

        private void InstallControlledMcClickHook(int keyCode)
        {
            if (_controlledMcClickHookHandle != IntPtr.Zero)
            {
                if (_controlledMcClickHookKeyCode == keyCode) return;
                UninstallControlledMcClickHook();
            }
            _controlledMcClickHookForm = this;
            _controlledMcClickHookKeyCode = keyCode;
            if (_controlledMcClickHookProc == null)
                _controlledMcClickHookProc = ControlledMcClickHookProc;
            IntPtr hModule = Win32.GetModuleHandle(null);
            _controlledMcClickHookHandle = Win32.SetWindowsHookEx(Win32.WH_KEYBOARD_LL, _controlledMcClickHookProc, hModule, 0);
        }

        private void UninstallControlledMcClickHook()
        {
            if (_controlledMcClickHookHandle != IntPtr.Zero)
            {
                Win32.UnhookWindowsHookEx(_controlledMcClickHookHandle);
                _controlledMcClickHookHandle = IntPtr.Zero;
            }
            _controlledMcClickHookForm = null;
            _controlledMcClickHookKeyCode = 0;
        }

        /// <summary>
        /// LL keyboard hook for the Controlled Multi-Click Mode click key.
        /// Only fires when Controlled Multi-Click Mode is active.
        /// Passes through when the mode is inactive so the key works normally.
        /// </summary>
        private static IntPtr ControlledMcClickHookProc(int nCode, IntPtr wParam, IntPtr lParam)
        {
            if (nCode < 0)
                return Win32.CallNextHookEx(_controlledMcClickHookHandle, nCode, wParam, lParam);
            if (_controlledMcClickHookForm == null || _controlledMcClickHookKeyCode == 0)
                return Win32.CallNextHookEx(_controlledMcClickHookHandle, nCode, wParam, lParam);

            if (_controlledMcClickHookForm.controller?.IsControlledMulticlickMode != true)
                return Win32.CallNextHookEx(_controlledMcClickHookHandle, nCode, wParam, lParam);

            int msg = wParam.ToInt32();
            bool isDown = msg == 0x100 || msg == 0x104;
            bool isUp   = msg == 0x101 || msg == 0x105;
            if (!isDown && !isUp)
                return Win32.CallNextHookEx(_controlledMcClickHookHandle, nCode, wParam, lParam);

            var hookStruct = (Win32.KBDLLHOOKSTRUCT)Marshal.PtrToStructure(lParam, typeof(Win32.KBDLLHOOKSTRUCT));
            if ((uint)hookStruct.vkCode != _controlledMcClickHookKeyCode)
                return Win32.CallNextHookEx(_controlledMcClickHookHandle, nCode, wParam, lParam);

            bool triggerOnRelease = Properties.Settings.Default.controlledMulticlickClickTriggerOnRelease;
            bool shouldFire = triggerOnRelease ? isUp : isDown;

            var form = _controlledMcClickHookForm;
            if (shouldFire || isUp)
            {
                form?.BeginInvoke(new Action(() =>
                {
                    var c = _controlledMcClickHookForm?.controller;
                    if (c == null) return;
                    if (shouldFire)
                        c.TriggerInstantMultiClick(separateLR: Properties.Settings.Default.controlledMulticlickClickSeparateLR);
                    // If this key is also the hold-activation key, exit CMC mode on release.
                    if (isUp)
                    {
                        bool activateHold = Properties.Settings.Default.controlledMulticlickActivateHold;
                        uint activateKey  = (uint)Properties.Settings.Default.controlledMulticlickActivateKeyCode;
                        if (activateHold && activateKey == (uint)_controlledMcClickHookKeyCode)
                            c.ExitControlledMulticlickMode();
                    }
                }));
            }
            return (IntPtr)1; // consume both down and up
        }

        // ── Controlled Multi-Click Mode: Regular-Click Keyboard Hook ──────────────

        private void InstallControlledMcRegularClickKeyboardHook(int keyCode)
        {
            if (_controlledMcRegularClickHookHandle != IntPtr.Zero)
            {
                if (_controlledMcRegularClickHookKeyCode == keyCode) return;
                UninstallControlledMcRegularClickKeyboardHook();
            }
            _controlledMcRegularClickHookForm = this;
            _controlledMcRegularClickHookKeyCode = keyCode;
            if (_controlledMcRegularClickHookProc == null)
                _controlledMcRegularClickHookProc = ControlledMcRegularClickKeyboardHookProc;
            IntPtr hModule = Win32.GetModuleHandle(null);
            _controlledMcRegularClickHookHandle = Win32.SetWindowsHookEx(Win32.WH_KEYBOARD_LL, _controlledMcRegularClickHookProc, hModule, 0);
        }

        private void UninstallControlledMcRegularClickKeyboardHook()
        {
            if (_controlledMcRegularClickHookHandle != IntPtr.Zero)
            {
                Win32.UnhookWindowsHookEx(_controlledMcRegularClickHookHandle);
                _controlledMcRegularClickHookHandle = IntPtr.Zero;
            }
            _controlledMcRegularClickHookForm = null;
            _controlledMcRegularClickHookKeyCode = 0;
        }

        private static IntPtr ControlledMcRegularClickKeyboardHookProc(int nCode, IntPtr wParam, IntPtr lParam)
        {
            if (nCode < 0)
                return Win32.CallNextHookEx(_controlledMcRegularClickHookHandle, nCode, wParam, lParam);
            if (_controlledMcRegularClickHookForm == null || _controlledMcRegularClickHookKeyCode == 0)
                return Win32.CallNextHookEx(_controlledMcRegularClickHookHandle, nCode, wParam, lParam);

            if (_controlledMcRegularClickHookForm.controller?.IsControlledMulticlickMode != true)
                return Win32.CallNextHookEx(_controlledMcRegularClickHookHandle, nCode, wParam, lParam);

            int msg = wParam.ToInt32();
            bool isDown = msg == 0x100 || msg == 0x104;
            bool isUp   = msg == 0x101 || msg == 0x105;
            if (!isDown && !isUp)
                return Win32.CallNextHookEx(_controlledMcRegularClickHookHandle, nCode, wParam, lParam);

            var hookStruct = (Win32.KBDLLHOOKSTRUCT)Marshal.PtrToStructure(lParam, typeof(Win32.KBDLLHOOKSTRUCT));
            if ((uint)hookStruct.vkCode != _controlledMcRegularClickHookKeyCode)
                return Win32.CallNextHookEx(_controlledMcRegularClickHookHandle, nCode, wParam, lParam);

            bool triggerOnRelease = Properties.Settings.Default.controlledMulticlickRegularClickTriggerOnRelease;
            bool shouldFire = triggerOnRelease ? isUp : isDown;

            var form = _controlledMcRegularClickHookForm;
            if (shouldFire || isUp)
            {
                form?.BeginInvoke(new Action(() =>
                {
                    var c = _controlledMcRegularClickHookForm?.controller;
                    if (c == null) return;
                    if (shouldFire)
                        c.TriggerRegularClick();
                    // If this key is also the hold-activation key, exit CMC mode on release.
                    if (isUp)
                    {
                        bool activateHold = Properties.Settings.Default.controlledMulticlickActivateHold;
                        uint activateKey  = (uint)Properties.Settings.Default.controlledMulticlickActivateKeyCode;
                        if (activateHold && activateKey == (uint)_controlledMcRegularClickHookKeyCode)
                            c.ExitControlledMulticlickMode();
                    }
                }));
            }
            return (IntPtr)1; // consume both down and up
        }

        // ── Controlled Multi-Click Mode: event handler ─────────────────────────────

        private void Controller_ControlledMulticlickModeChanged(object sender, EventArgs e)
        {
            if (controller.IsControlledMulticlickMode)
            {
                if (!_multiclickFakeCursorTimer.Enabled)
                    _multiclickFakeCursorTimer.Start();
                InstallControlledMcFocusBlockHook();
            }
            else
            {
                StopFakeCursors();
                UninstallControlledMcFocusBlockHook();
            }
            UpdateCaptionColor();
        }

        private void InstallControlledMcFocusBlockHook()
        {
            if (_controlledMcFocusBlockHookHandle != IntPtr.Zero)
                return;
            _controlledMcFocusBlockHookForm = this;
            if (_controlledMcFocusBlockHookProc == null)
                _controlledMcFocusBlockHookProc = ControlledMcFocusBlockHookProc;
            IntPtr hModule = Win32.GetModuleHandle(null);
            _controlledMcFocusBlockHookHandle = Win32.SetWindowsHookEx(Win32.WH_MOUSE_LL, _controlledMcFocusBlockHookProc, hModule, 0);
        }

        private void UninstallControlledMcFocusBlockHook()
        {
            if (_controlledMcFocusBlockHookHandle != IntPtr.Zero)
            {
                Win32.UnhookWindowsHookEx(_controlledMcFocusBlockHookHandle);
                _controlledMcFocusBlockHookHandle = IntPtr.Zero;
            }
            _controlledMcFocusBlockHookForm = null;
        }

        /// <summary>
        /// Intercepts mouse button events while Controlled Multi-Click Mode is active:
        ///  - Fires TriggerInstantMultiClick / TriggerRegularClick for configured mouse binds.
        ///  - Blocks left/right clicks on game windows from stealing focus otherwise.
        /// </summary>
        private static IntPtr ControlledMcFocusBlockHookProc(int nCode, IntPtr wParam, IntPtr lParam)
        {
            if (nCode < 0)
                return Win32.CallNextHookEx(_controlledMcFocusBlockHookHandle, nCode, wParam, lParam);

            var form = _controlledMcFocusBlockHookForm;
            if (form?.controller?.IsControlledMulticlickMode != true)
                return Win32.CallNextHookEx(_controlledMcFocusBlockHookHandle, nCode, wParam, lParam);

            int msg = wParam.ToInt32();
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

            // Check multi-click mouse bind
            bool multiClickUseMouse      = Properties.Settings.Default.controlledMulticlickClickUseMouseButton;
            int  multiClickButton        = Properties.Settings.Default.controlledMulticlickClickMouseButton;
            bool multiClickTriggerOnRel  = Properties.Settings.Default.controlledMulticlickClickTriggerOnRelease;
            if (multiClickUseMouse && buttonIndex == multiClickButton)
            {
                bool shouldFire = multiClickTriggerOnRel ? isButtonUp : isButtonDown;
                if (shouldFire)
                {
                    form.BeginInvoke(new Action(() =>
                    {
                        _controlledMcFocusBlockHookForm?.controller?.TriggerInstantMultiClick(separateLR: Properties.Settings.Default.controlledMulticlickClickSeparateLR);
                    }));
                }
                return (IntPtr)1; // consume both down and up
            }

            // Check regular-click mouse bind
            bool regularClickUseMouse     = Properties.Settings.Default.controlledMulticlickRegularClickUseMouseButton;
            int  regularClickButton       = Properties.Settings.Default.controlledMulticlickRegularClickMouseButton;
            bool regularClickTriggerOnRel = Properties.Settings.Default.controlledMulticlickRegularClickTriggerOnRelease;
            if (regularClickUseMouse && buttonIndex == regularClickButton)
            {
                bool shouldFire = regularClickTriggerOnRel ? isButtonUp : isButtonDown;
                if (shouldFire)
                {
                    form.BeginInvoke(new Action(() =>
                    {
                        _controlledMcFocusBlockHookForm?.controller?.TriggerRegularClick();
                    }));
                }
                return (IntPtr)1; // consume both down and up
            }

            // Block left/right button down on game windows to prevent unwanted focus changes
            IntPtr hwndUnderCursor = Win32.WindowFromPoint(hookStruct.pt);
            bool isGameWindow = form.controller.AllControllersWithWindows
                .Any(c => c.WindowHandle == hwndUnderCursor);
            if (isGameWindow && isButtonDown && (buttonIndex == 0 || buttonIndex == 1))
                return (IntPtr)1; // consume — prevents focus change

            return Win32.CallNextHookEx(_controlledMcFocusBlockHookHandle, nCode, wParam, lParam);
        }

        /// <summary>
        /// Stops the fake-cursor timer and hides fake cursors on all controllers.
        /// Called when exiting Controlled Multi-Click Mode.
        /// </summary>
        private void StopFakeCursors()
        {
            _multiclickFakeCursorTimer?.Stop();
            if (controller == null) return;
            foreach (var c in controller.AllControllersWithWindows)
                c.ShowFakeCursor = false;
        }

        /// <summary>
        /// Shows the fake cursor on every game window except the one the real cursor is over.
        /// All windows receive the cursor at the SAME local (client-area-relative) position as the
        /// hovered window — one position is broadcast to all.
        /// Runs while Controlled Multi-Click Mode is active.
        /// </summary>
        private void MulticlickFakeCursorTimer_Tick(object sender, EventArgs e)
        {
            if (controller?.IsControlledMulticlickMode != true)
            {
                StopFakeCursors();
                return;
            }
            if (controller == null) return;

            Point screenCursor = Control.MousePosition;

            var activeControllers = controller.ActiveControllers
                .Where(c => c.HasWindow && Win32.GetWindowShowState(c.WindowHandle) != Win32.ShowWindowCommands.ShowMinimized)
                .ToList();

            // Phase 1: find which active window the real cursor is over and its local position.
            ToontownController hoveredController = null;
            Point hoveredLocalPos = Point.Empty;
            foreach (var c in activeControllers)
            {
                Point loc = Win32.GetWindowClientAreaLocation(c.WindowHandle);
                Size size = c.WindowSize;
                if (screenCursor.X >= loc.X && screenCursor.X < loc.X + size.Width
                    && screenCursor.Y >= loc.Y && screenCursor.Y < loc.Y + size.Height)
                {
                    hoveredController = c;
                    hoveredLocalPos = new Point(screenCursor.X - loc.X, screenCursor.Y - loc.Y);
                    break;
                }
            }

            // Phase 2: broadcast that local position to every other active window;
            //          hide fake cursors on all non-active controllers.
            var activeSet = new HashSet<ToontownController>(activeControllers);
            foreach (var c in controller.AllControllersWithWindows)
            {
                if (!activeSet.Contains(c) || c == hoveredController
                    || Win32.GetWindowShowState(c.WindowHandle) == Win32.ShowWindowCommands.ShowMinimized)
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

        private static IntPtr MulticlickMouseHookProc(int nCode, IntPtr wParam, IntPtr lParam)
        {
            if (nCode < 0)
                return Win32.CallNextHookEx(_multiclickMouseHookHandle, nCode, wParam, lParam);
            if (_multiclickMouseHookForm == null || _multiclickMouseHookButton < 0)
                return Win32.CallNextHookEx(_multiclickMouseHookHandle, nCode, wParam, lParam);

            int msg = wParam.ToInt32();

            // Determine if this message is a DOWN or UP for the configured button
            bool isDown = false, isUp = false;
            if (_multiclickMouseHookButton == 0)
            {
                isDown = msg == (int)Win32.WM.MBUTTONDOWN;
                isUp   = msg == (int)Win32.WM.MBUTTONUP;
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
                    isUp   = msg == (int)Win32.WM.XBUTTONUP;
                }
            }

            if (isDown)
            {
                Keys mods = Control.ModifierKeys;
                if ((mods & (Keys.Shift | Keys.Control | Keys.Alt)) != Keys.None)
                    return Win32.CallNextHookEx(_multiclickMouseHookHandle, nCode, wParam, lParam);

                bool isActive    = _multiclickMouseHookForm?.controller?.IsActive ?? false;
                bool multiGlobal = Properties.Settings.Default.replicateMouseHotkeyGlobal;

                if (!isActive && !multiGlobal)
                    return Win32.CallNextHookEx(_multiclickMouseHookHandle, nCode, wParam, lParam);

                var hookForm = _multiclickMouseHookForm;
                if (hookForm != null)
                {
                    hookForm.BeginInvoke(new Action(() =>
                    {
                        hookForm.controller?.TriggerInstantMultiClick(separateLR: Properties.Settings.Default.replicateMouseSeparateLR);
                    }));
                }
                return (IntPtr)1;
            }

            return Win32.CallNextHookEx(_multiclickMouseHookHandle, nCode, wParam, lParam);
        }

        private void MulticontrollerWnd_Load(object sender, EventArgs e)
        {
            controller = Multicontroller.Instance;

            _multiclickFakeCursorTimer = new System.Windows.Forms.Timer { Interval = 16 };
            _multiclickFakeCursorTimer.Tick += MulticlickFakeCursorTimer_Tick;

            controller.ControlledMulticlickModeChanged += Controller_ControlledMulticlickModeChanged;
            controller.ModeChanged += Controller_ModeChanged;
            controller.GroupsChanged += Controller_GroupsChanged;
            controller.ActiveControllersChanged += Controller_ActiveControllersChanged;
            controller.ShouldActivate += Controller_ShouldActivate;
            controller.WindowActivated += Controller_WindowActivated;
            controller.AllWindowsInactive += Controller_AllWindowsInactive;
            controller.ActiveChanged += Controller_ActiveChanged;
            controller.SettingChanged += Controller_SettingChanged;

            // Ensure at least one group exists before accessing it
            if (controller.ControllerGroups.Count == 0)
            {
                controller.AddControllerGroup();
            }

            // Ensure the first group has at least one pair
            if (controller.ControllerGroups[0].ControllerPairs.Count == 0)
            {
                controller.ControllerGroups[0].AddPair();
            }

            controller.ControllerGroups[0].ControllerPairs[0].LeftController.WindowHandleChanged += LeftController_WindowHandleChanged;
            controller.ControllerGroups[0].ControllerPairs[0].RightController.WindowHandleChanged += RightController_WindowHandleChanged;

            // Apply default mode on launch (Mirror vs Multi)
            if (Properties.Settings.Default.defaultModeOnLaunch)
                controller.CurrentMode = MulticontrollerMode.MirrorAll;
            else
                controller.CurrentMode = MulticontrollerMode.Group;

            // Removes the extra padding on the right side of the status strip.
            // Apparently this is "not relevant for this class" but still has an effect.
            statusStrip1.Padding = new Padding(statusStrip1.Padding.Left, statusStrip1.Padding.Top, statusStrip1.Padding.Left, statusStrip1.Padding.Bottom);

            // Set up the IMessageFilter so we receive all messages for child controls
            Application.AddMessageFilter(this);
            
            // Restore the saved position of the window, making sure that it's not offscreen
            if (Properties.Settings.Default.lastLocation != Point.Empty)
            {
                var location = Properties.Settings.Default.lastLocation;
                var isNotOffScreen = false;

                foreach (var screen in Screen.AllScreens)
                {
                    if (screen.Bounds.Contains(location))
                    {
                        isNotOffScreen = true;
                        break;
                    }
                }

                if (isNotOffScreen)
                {
                    this.Location = Properties.Settings.Default.lastLocation;
                }
            }

            ReloadOptions();

            controller.ActiveCustomModeId = Properties.Settings.Default.lastActiveCustomModeId ?? "";
            controller.EnsureValidActiveCustomModeId();

            // Multicontroller could have loaded groups
            UpdateWindowStatus();
            
            // Set initial caption color
            UpdateCaptionColor();

        }
        
        private void MulticontrollerWnd_Shown(object sender, EventArgs e)
        {
            // When window is first shown, check if it's active and register hotkeys
            if (this.ContainsFocus || Win32.GetForegroundWindow() == this.Handle)
            {
                controller.IsActive = true;
                RegisterAutoFindHotkey();
                RegisterLayoutPresetHotkeys();
                RegisterMinimizeUnconnectedHotkey();
            }
        }

        private void RightController_WindowHandleChanged(object sender, EventArgs e)
        {
            int gi = controller.CurrentGroupIndex;
            if (gi < controller.ControllerGroups.Count &&
                controller.ControllerGroups[gi].ControllerPairs.Count > 0)
            {
                leftToonCrosshair.SelectedWindowHandle = controller.ControllerGroups[gi].ControllerPairs[0].LeftController.WindowHandle;
            }
        }

        private void LeftController_WindowHandleChanged(object sender, EventArgs e)
        {
            int gi = controller.CurrentGroupIndex;
            if (gi < controller.ControllerGroups.Count &&
                controller.ControllerGroups[gi].ControllerPairs.Count > 0)
            {
                rightToonCrosshair.SelectedWindowHandle = controller.ControllerGroups[gi].ControllerPairs[0].RightController.WindowHandle;
            }
        }

        private void Controller_AllWindowsInactive(object sender, EventArgs e)
        {
            // Unregister all hotkeys first
            UnregisterHotkey();
            UnregisterAutoFindHotkey();
            UnregisterLayoutPresetHotkeys();
            UnregisterMinimizeUnconnectedHotkey();
            
            if (controller.IsActive)
            {
                RegisterHotkey();
                RegisterAutoFindHotkey();
                RegisterLayoutPresetHotkeys();
            }
            else
            {
                RegisterHotkey();
            }
            RegisterMinimizeUnconnectedHotkey();
        }

        private void Controller_WindowActivated(object sender, EventArgs e)
        {
            // Re-register all hotkeys (both global and non-global) when a Toontown window becomes active
            RegisterHotkey();
            // Also register auto-find hotkey when multicontroller window is active
            if (controller.IsActive)
            {
                RegisterAutoFindHotkey();
                RegisterLayoutPresetHotkeys();
            }
            RegisterMinimizeUnconnectedHotkey();
        }

        private void MainWnd_FormClosing(object sender, FormClosingEventArgs e)
        {
            // Signal the activation thread to stop and stop any queued TryActivate from spawning a new one.
            // Don't Thread.Abort: it can fire mid-P/Invoke and leave the borrowed input queue attached, and it
            // doesn't prevent a respawn.  The thread is IsBackground and SafeInvoke fails once we're disposed, so
            // it exits on its own — and we must not Join here, since it may be blocked in Invoke on this UI thread (CORR-01).
            _closing = true;
            _cancelActivation = true;

            ShutdownAllInputCapture();

            _multiclickFakeCursorTimer?.Dispose();

            SaveWindowPosition();
        }

        /// <summary>
        /// Remove hooks, hotkeys, and message filter before the main HWND is destroyed so mouse/keyboard
        /// input is not processed by orphaned WH_MOUSE_LL / WH_KEYBOARD_LL hooks (avoids cursor lag or erratic movement after exit).
        /// </summary>
        private void ShutdownAllInputCapture()
        {
            try
            {
                Application.RemoveMessageFilter(this);
            }
            catch { }

            StopFakeCursors();

            UnregisterControlledMulticlickHotkeys();
            UninstallControlledMcFocusBlockHook();

            UninstallMulticlickMouseHook();
            UninstallMinimizeUnconnectedKeyboardHook();

            UnregisterAutoFindHotkey();
            UnregisterLayoutPresetHotkeys();
            UnregisterMinimizeUnconnectedHotkey();

            UnregisterHotkey();

            controller?.ShutdownUninstallSwitchingMouseHook();
        }

        private void Controller_GroupsChanged(object sender, EventArgs e)
        {
            this.UpdateWindowStatus();
            
            // Re-register hotkeys if multicontroller is active (groups may have been added/removed)
            if (controller.IsActive || controller.AllControllersWithWindows.Any(c => c.IsWindowActive))
            {
                RegisterHotkey();
            }
            
            // Auto-find hotkey only registers when multicontroller window is active
            if (controller.IsActive)
            {
                RegisterAutoFindHotkey();
                RegisterLayoutPresetHotkeys();
            }
            RegisterMinimizeUnconnectedHotkey();
        }

        private void Controller_ActiveControllersChanged(object sender, EventArgs e)
        {
            UpdateWindowStatus();
        }

        private void Controller_ShouldActivate(object sender, EventArgs e)
        {
            this.TryActivate();
        }

        private void Controller_ModeChanged(object sender, EventArgs e)
        {
            switch (controller.CurrentMode)
            {
                case MulticontrollerMode.Group:
                    multiModeRadio.Checked = true;
                    mirrorModeRadio.Checked = false;
                    break;
                case MulticontrollerMode.MirrorAll:
                    mirrorModeRadio.Checked = true;
                    multiModeRadio.Checked = false;
                    break;
                default:
                    multiModeRadio.Checked = false;
                    mirrorModeRadio.Checked = false;
                    break;
            }

            UpdateWindowStatus();
            UpdateCaptionColor();
        }

        private void UpdateModeLockVisuals()
        {
            bool locked = controller.IsModeLockEngaged;
            multiModeRadio.Enabled = !locked;
            mirrorModeRadio.Enabled = !locked;
        }
        
        private void Controller_ActiveChanged(object sender, EventArgs e)
        {
            UpdateCaptionColor();
        }
        
        private void Controller_SettingChanged(object sender, EventArgs e)
        {
            UpdateCaptionColor();
            UpdateModeLockVisuals();
            UpdateWindowStatus();
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
        
        /// <summary>
        /// Blends two colors by averaging their RGB components.
        /// </summary>
        private static Color BlendColors(Color color1, Color color2)
        {
            int r = (color1.R + color2.R) / 2;
            int g = (color1.G + color2.G) / 2;
            int b = (color1.B + color2.B) / 2;
            return Color.FromArgb(color1.A, r, g, b);
        }
        
        /// <summary>
        /// Updates the multicontroller window's caption color to match the current mode and sync with toontown windows.
        /// </summary>
        private void UpdateCaptionColor()
        {
            if (!Properties.Settings.Default.enableCaptionColor)
            {
                // Reset to default if caption color is disabled
                Win32.SetWindowCaptionColor(this.Handle, null);
                return;
            }
            
            Color borderColor;
            
            // Check if switching mode is active
            if (controller.IsSwitchingMode)
            {
                borderColor = Colors.SwitchingMode;
            }
            else if (controller.IsActive)
            {
                // Normal mode - set border colors based on mode
                switch (controller.CurrentMode)
                {
                    case MulticontrollerMode.Group:
                    case MulticontrollerMode.AllGroup:
                        // Blend left and right group colors to represent both sides
                        // This creates a middle color since DWM doesn't support split colors
                        borderColor = BlendColors(Colors.LeftGroup, Colors.RightGroup);
                        break;
                    case MulticontrollerMode.MirrorAll:
                        borderColor = Colors.AllGroups;
                        break;
                    case MulticontrollerMode.Custom:
                        {
                            var def = controller.GetActiveCustomModeDefinition();
                            borderColor = def != null
                                ? BlendColors(def.GetLeftBorderColor(), def.GetRightBorderColor())
                                : Colors.AllGroups;
                        }
                        break;
                    case MulticontrollerMode.Focused:
                        // Blend focused and unfocused colors to represent both types of windows
                        // This creates a middle color since DWM doesn't support split colors
                        borderColor = BlendColors(Colors.FocusedFocused, Colors.FocusedUnfocused);
                        break;
                    default:
                        // Keep current color or use a default
                        borderColor = Colors.LeftGroup;
                        break;
                }
            }
            else
            {
                // Not active - reset to default
                Win32.SetWindowCaptionColor(this.Handle, null);
                return;
            }
            
            // Darken the border color for caption (make it slightly darker)
            // Use the same factor as ToontownController for consistency
            Color captionColor = DarkenColor(borderColor, 0.85f);
            Win32.SetWindowCaptionColor(this.Handle, captionColor);
        }

        private void optionsBtn_Click(object sender, EventArgs e)
        {
            OptionsDlg optionsDlg = new OptionsDlg();

            ignoreMessages = true;

            if (optionsDlg.ShowDialog(this) == DialogResult.OK)
            {
                ReloadOptions();
                controller.EnsureValidActiveCustomModeId();
            }

            ignoreMessages = false;

            UpdateWindowStatus();
        }

        private void windowGroupsBtn_Click(object sender, EventArgs e)
        {
            controller.ShowAllBorders = true;
            ignoreMessages = true;
            new WindowGroupsForm().ShowDialog(this);
            ignoreMessages = false;
            controller.ShowAllBorders = false;

            UpdateWindowStatus();
        }

        private void leftToonCrosshair_WindowSelected(object sender, IntPtr handle)
        {
            int gi = controller.CurrentGroupIndex;
            if (gi < controller.ControllerGroups.Count &&
                controller.ControllerGroups[gi].ControllerPairs.Count > 0)
            {
                controller.ControllerGroups[gi].ControllerPairs[0].LeftController.WindowHandle = handle;
            }
        }

        private void rightToonCrosshair_WindowSelected(object sender, IntPtr handle)
        {
            int gi = controller.CurrentGroupIndex;
            if (gi < controller.ControllerGroups.Count &&
                controller.ControllerGroups[gi].ControllerPairs.Count > 0)
            {
                controller.ControllerGroups[gi].ControllerPairs[0].RightController.WindowHandle = handle;
            }
        }

        private void multiModeRadio_Click(object sender, EventArgs e)
        {
            controller.CurrentMode = MulticontrollerMode.Group;
        }

        private void mirrorModeRadio_Clicked(object sender, EventArgs e)
        {
            controller.CurrentMode = MulticontrollerMode.MirrorAll;
        }

        private void MulticontrollerWnd_Activated(object sender, EventArgs e)
        {
            controller.IsActive = true;
            RegisterHotkey();
            RegisterAutoFindHotkey();
            RegisterLayoutPresetHotkeys();
            RegisterMinimizeUnconnectedHotkey();
        }

        private void MulticontrollerWnd_Deactivate(object sender, EventArgs e)
        {
            // Cancel any pending TryActivate loop — the user has deliberately focused another window.
            _cancelActivation = true;
            controller.IsActive = false;

            UnregisterHotkey();
            UnregisterAutoFindHotkey();
            UnregisterLayoutPresetHotkeys();
            UnregisterMinimizeUnconnectedHotkey();

            RegisterHotkey();
            RegisterMinimizeUnconnectedHotkey();
        }
    }
}
