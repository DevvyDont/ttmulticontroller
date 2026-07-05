using System;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using TTMulti;
using TTMulti.Input;

namespace TTMulti.Forms
{
    /// <summary>
    /// The main window — now a thin UI shell (R5 of the WPF rebuild). All global input capture (hotkeys,
    /// low-level hooks, WM_HOTKEY dispatch, message filtering, activation, watchdog, fake cursors) lives in
    /// <see cref="InputCaptureHost"/>; this form supplies the HWND/marshalling via <see cref="IInputShell"/>
    /// and keeps only presentation: status, colors, mode buttons, crosshairs, and dialogs.
    /// </summary>
    internal partial class MulticontrollerWnd : Form, IMessageFilter, IInputShell
    {
        /// <summary>
        /// This flag is used to ignore input while a dialog is open.
        /// </summary>
        bool ignoreMessages = false;

        Multicontroller controller;
        InputCaptureHost inputHost;

        internal MulticontrollerWnd()
        {
            InitializeComponent();
            this.Icon = Properties.Resources.icon;

            // The crosshairs are custom controls with no text, so give them accessible names/roles for
            // assistive technology and screen readers (UX-06).
            leftToonCrosshair.AccessibleName = "Left toon window";
            leftToonCrosshair.AccessibleDescription = "Drag the crosshair onto a Toontown window to control it as the left toon.";
            rightToonCrosshair.AccessibleName = "Right toon window";
            rightToonCrosshair.AccessibleDescription = "Drag the crosshair onto a Toontown window to control it as the right toon.";
        }

        // ── IInputShell ─────────────────────────────────────────────────────────────

        void IInputShell.BeginInvoke(Action action)
        {
            if (IsDisposed || !IsHandleCreated)
                return;
            try
            {
                base.BeginInvoke(action);
            }
            catch (ObjectDisposedException) { }
            catch (InvalidOperationException) { }
        }

        bool IInputShell.SafeInvoke(Action action) => SafeInvoke(action);

        IUiTimer IInputShell.CreateTimer(int intervalMs, Action tick) => new WinFormsUiTimer(intervalMs, tick);

        void IInputShell.FinishActivation()
        {
            if (IsDisposed)
                return;
            this.TopMost = true;
            this.Activate();
            this.TopMost = Properties.Settings.Default.onTopWhenInactive;
        }

        void IInputShell.ShowWarning(string message, string title)
        {
            if (IsDisposed || !IsHandleCreated)
                return;
            BeginInvoke(new Action(() =>
            {
                if (!IsDisposed)
                    MessageBox.Show(this, message, title, MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }));
        }

        /// <summary>UI-thread timer over a WinForms Timer (the WPF shell uses a DispatcherTimer instead).</summary>
        private sealed class WinFormsUiTimer : IUiTimer
        {
            private readonly Timer _timer;

            internal WinFormsUiTimer(int intervalMs, Action tick)
            {
                _timer = new Timer { Interval = intervalMs };
                _timer.Tick += (s, e) => tick();
            }

            public bool Enabled => _timer.Enabled;
            public void Start() => _timer.Start();
            public void Stop() => _timer.Stop();
            public void Dispose() => _timer.Dispose();
        }

        /// <summary>
        /// Marshal <paramref name="action"/> to the UI thread, returning false instead of throwing if the
        /// form's handle is gone. Prevents a dispose race from crashing the process on the background
        /// activation thread (CORR-01).
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

        internal void TryActivate() => inputHost?.TryActivate();

        // ── Status display ──────────────────────────────────────────────────────────

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

        // ── Input delegation to the host ────────────────────────────────────────────

        /// <summary>
        /// Overrides keys that usually perform other functions (Tab, arrows, Alt) so they can be used for
        /// game control; the decision logic lives in the host.
        /// </summary>
        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            if (inputHost != null)
            {
                bool handled = inputHost.HandleCmdKey(msg.Msg, msg.WParam, msg.LParam, keyData, out bool useDefault);
                if (!useDefault)
                    return handled;
            }
            return base.ProcessCmdKey(ref msg, keyData);
        }

        /// <summary>
        /// IMessageFilter implementation: captures all keys sent to the window (including ones sent directly
        /// to child controls) and routes them through the host into the multicontroller.
        /// </summary>
        public bool PreFilterMessage(ref Message m)
        {
            if (ignoreMessages || inputHost == null)
            {
                return false;
            }

            return inputHost.PreFilterMessage(m.Msg, m.WParam, m.LParam);
        }

        protected override void WndProc(ref Message m)
        {
            inputHost?.HandleWindowMessage(m.Msg, m.WParam, m.LParam);
            base.WndProc(ref m);
        }

        // ── Admin-rights prompt (raised by the host, shown here) ────────────────────

        private void InputHost_AdminRightsPromptNeeded(object sender, EventArgs e)
        {
            // Held forwarded keys were already released by the host; defer the modal prompt onto the message
            // loop with ignoreMessages gating our own filter while it's up (UX-09).
            BeginInvoke(new Action(PromptForAdminRights));
        }

        private void PromptForAdminRights()
        {
            ignoreMessages = true;
            try
            {
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
            finally
            {
                ignoreMessages = false;
            }
        }

        internal void SaveWindowPosition()
        {
            Properties.Settings.Default.lastLocation = this.Location;
            Properties.Settings.Default.Save();
        }

        private void ReloadOptions()
        {
            this.TopMost = Properties.Settings.Default.onTopWhenInactive;
            panel1.Visible = !Properties.Settings.Default.compactUI;
            controller.UpdateOptions();
            controller.RefreshAllControllerBorders();

            // Update UI colors to reflect any changes
            UpdateUIColors();

            // Reset suspension, allow re-reporting hotkey conflicts, and rebuild all registrations (the
            // host raises SuspendStateChanged, which refreshes the title's suspend indicator).
            inputHost.OnSettingsReloaded();
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

        // ── Form lifecycle ──────────────────────────────────────────────────────────

        private void MulticontrollerWnd_Load(object sender, EventArgs e)
        {
            controller = Multicontroller.Instance;

            // The input-capture host owns hotkeys, hooks, watchdog, activation, and fake cursors.
            inputHost = new InputCaptureHost(this, controller);
            inputHost.SuspendStateChanged += (s, args) => UpdateSuspendIndicator();
            inputHost.ModeLockToggled += (s, args) => UpdateModeLockVisuals();
            inputHost.AdminRightsPromptNeeded += InputHost_AdminRightsPromptNeeded;

            // UI-facing engine events (input-capture events are subscribed inside the host).
            controller.ControlledMulticlickModeChanged += Controller_ControlledMulticlickModeChanged;
            controller.ModeChanged += Controller_ModeChanged;
            controller.GroupsChanged += Controller_GroupsChanged;
            controller.ActiveControllersChanged += Controller_ActiveControllersChanged;
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
                inputHost.OnShellShown();
            }
        }

        private void MainWnd_FormClosing(object sender, FormClosingEventArgs e)
        {
            // Remove the message filter and shut down all input capture before the HWND is destroyed so
            // mouse/keyboard input is not processed by orphaned low-level hooks.
            try
            {
                Application.RemoveMessageFilter(this);
            }
            catch { }

            inputHost?.Dispose();

            WindowWatcher.Instance.Shutdown();

            SaveWindowPosition();
        }

        // ── Crosshair / status wiring ───────────────────────────────────────────────

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

        private void Controller_GroupsChanged(object sender, EventArgs e)
        {
            this.UpdateWindowStatus();
        }

        private void Controller_ActiveControllersChanged(object sender, EventArgs e)
        {
            UpdateWindowStatus();
        }

        private void Controller_ControlledMulticlickModeChanged(object sender, EventArgs e)
        {
            UpdateCaptionColor();
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

        private const string BaseWindowTitle = "Toontown Multicontroller";

        /// <summary>
        /// Reflects the global-hotkeys-suspended state in the window title so the toggle has visible feedback
        /// and the reset when Options closes is announced (UX-08).
        /// </summary>
        private void UpdateSuspendIndicator()
        {
            bool suspended = inputHost?.IsGlobalHotkeysSuspended ?? false;
            this.Text = suspended ? BaseWindowTitle + " — Hotkeys Suspended" : BaseWindowTitle;
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

        // ── Caption color ───────────────────────────────────────────────────────────

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
                        borderColor = BlendColors(Colors.FocusedFocused, Colors.FocusedUnfocused);
                        break;
                    default:
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

            // Darken the border color for the caption; same factor as ToontownController for consistency.
            Color captionColor = DarkenColor(borderColor, 0.85f);
            Win32.SetWindowCaptionColor(this.Handle, captionColor);
        }

        // ── Buttons / dialogs ───────────────────────────────────────────────────────

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
            inputHost?.RegisterFocusHotkeys();
        }

        private void MulticontrollerWnd_Deactivate(object sender, EventArgs e)
        {
            // Cancel any pending TryActivate loop — the user has deliberately focused another window.
            inputHost?.CancelActivation();
            controller.IsActive = false;

            inputHost?.RegisterFocusHotkeys();
        }
    }
}
