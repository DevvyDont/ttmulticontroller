using System;
using System.ComponentModel;
using System.Linq;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Threading;
using TTMulti.Forms;
using TTMulti.Input;
using TTMulti.Ui.ViewModels;
using Wpf.Ui.Controls;

namespace TTMulti.Ui
{
    /// <summary>
    /// The WPF main window (R6/R7 of the UI rebuild) — a compact FluentWindow that hosts the SAME
    /// <see cref="InputCaptureHost"/> the WinForms shell uses, feeding it an HWND and UI-thread services
    /// through <see cref="IInputShell"/>. The message plumbing that WinForms did with IMessageFilter /
    /// WndProc is replicated with <see cref="ComponentDispatcher.ThreadPreprocessMessage"/> (the pre-dispatch
    /// filter) and an <see cref="HwndSource"/> hook (the window procedure). Visible state and interaction are
    /// driven by <see cref="MainViewModel"/>.
    /// </summary>
    public partial class MainWindow : FluentWindow, IInputShell
    {
        private Multicontroller _controller;
        private InputCaptureHost _inputHost;
        private MainViewModel _viewModel;
        private HwndSource _hwndSource;
        private IntPtr _hwnd;

        /// <summary>Suppresses input filtering while a modal dialog (Options / Window Groups) is open.</summary>
        private bool _ignoreMessages = false;
        private bool _shownOnce = false;

        public MainWindow()
        {
            InitializeComponent();
            RestoreWindowPosition();
        }

        // ── IInputShell ─────────────────────────────────────────────────────────────

        IntPtr IInputShell.Handle => _hwnd;

        void IInputShell.BeginInvoke(Action action)
        {
            if (Dispatcher.HasShutdownStarted || Dispatcher.HasShutdownFinished)
                return;
            try
            {
                Dispatcher.BeginInvoke(action);
            }
            catch (InvalidOperationException) { }
        }

        bool IInputShell.SafeInvoke(Action action)
        {
            if (Dispatcher.HasShutdownStarted || Dispatcher.HasShutdownFinished)
                return false;
            try
            {
                Dispatcher.Invoke(action);
                return true;
            }
            catch (System.Threading.Tasks.TaskCanceledException)
            {
                return false;
            }
            catch (InvalidOperationException)
            {
                return false;
            }
        }

        IUiTimer IInputShell.CreateTimer(int intervalMs, Action tick) => new DispatcherUiTimer(intervalMs, tick);

        void IInputShell.FinishActivation()
        {
            // Mirror the WinForms shell's TopMost pulse: force to front, then restore the configured on-top
            // state so the window doesn't get stuck topmost.
            this.Topmost = true;
            this.Activate();
            this.Topmost = Properties.Settings.Default.onTopWhenInactive;
        }

        void IInputShell.ShowWarning(string message, string title)
        {
            if (Dispatcher.HasShutdownStarted || Dispatcher.HasShutdownFinished)
                return;
            Dispatcher.BeginInvoke(new Action(() =>
                System.Windows.MessageBox.Show(this, message, title, System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Warning)));
        }

        /// <summary>UI-thread timer over a <see cref="DispatcherTimer"/> (the WinForms shell uses a Forms.Timer).</summary>
        private sealed class DispatcherUiTimer : IUiTimer
        {
            private readonly DispatcherTimer _timer;

            internal DispatcherUiTimer(int intervalMs, Action tick)
            {
                _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(intervalMs) };
                _timer.Tick += (s, e) => tick();
            }

            public bool Enabled => _timer.IsEnabled;
            public void Start() => _timer.Start();
            public void Stop() => _timer.Stop();
            public void Dispose() => _timer.Stop();
        }

        // ── Message routing (IMessageFilter + WndProc equivalents) ──────────────────

        /// <summary>
        /// The IMessageFilter equivalent: fires for every message pulled from the queue before it is
        /// translated/dispatched. Routing a message into the engine and consuming it (setting handled) here
        /// exactly matches the WinForms PreFilterMessage path.
        /// </summary>
        private void OnThreadPreprocessMessage(ref MSG msg, ref bool handled)
        {
            if (_ignoreMessages || _inputHost == null)
                return;

            if (_inputHost.PreFilterMessage(msg.message, msg.wParam, msg.lParam))
                handled = true;
        }

        /// <summary>
        /// The WndProc equivalent: WM_HOTKEY that the preprocess filter let through (pass-through IDs, and
        /// IDs 0/1/2 when ProcessInput didn't consume them) are handled here — identical to the WinForms
        /// WndProc override. Left unhandled so default window processing still runs.
        /// </summary>
        private IntPtr WndProcHook(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            _inputHost?.HandleWindowMessage(msg, wParam, lParam);
            return IntPtr.Zero;
        }

        /// <summary>
        /// The ProcessCmdKey equivalent: while actively controlling connected windows, Tab and the arrow keys
        /// must not drive WPF focus navigation (UX-06). The keystroke itself is forwarded to the games by the
        /// preprocess filter; here we just stop WPF from also acting on it.
        /// </summary>
        protected override void OnPreviewKeyDown(KeyEventArgs e)
        {
            base.OnPreviewKeyDown(e);
            if (_controller != null && _controller.IsActive && _controller.AllControllersWithWindows.Any())
            {
                if (e.Key == Key.Tab || e.Key == Key.Up || e.Key == Key.Down || e.Key == Key.Left || e.Key == Key.Right
                    || e.SystemKey == Key.LeftAlt || e.SystemKey == Key.RightAlt)
                {
                    e.Handled = true;
                }
            }
        }

        // ── Lifecycle ───────────────────────────────────────────────────────────────

        protected override void OnSourceInitialized(EventArgs e)
        {
            base.OnSourceInitialized(e);

            var helper = new WindowInteropHelper(this);
            _hwnd = helper.EnsureHandle();
            _hwndSource = HwndSource.FromHwnd(_hwnd);
            _hwndSource?.AddHook(WndProcHook);
            ComponentDispatcher.ThreadPreprocessMessage += OnThreadPreprocessMessage;

            // Apply the current OS theme to the app resources up front (Watch alone only reacts to later
            // changes), then keep following live OS light/dark switches + Mica backdrop.
            Wpf.Ui.Appearance.ApplicationThemeManager.ApplySystemTheme();
            Wpf.Ui.Appearance.SystemThemeWatcher.Watch(this);

            InitializeController();
        }

        /// <summary>The WinForms MulticontrollerWnd_Load equivalent (HWND already exists here).</summary>
        private void InitializeController()
        {
            _controller = Multicontroller.Instance;

            _inputHost = new InputCaptureHost(this, _controller);
            _inputHost.SuspendStateChanged += (s, a) => OnSuspendStateChanged();
            _inputHost.ModeLockToggled += (s, a) => _viewModel?.ForceRefresh();
            _inputHost.AdminRightsPromptNeeded += InputHost_AdminRightsPromptNeeded;

            if (_controller.ControllerGroups.Count == 0)
                _controller.AddControllerGroup();
            if (_controller.ControllerGroups[0].ControllerPairs.Count == 0)
                _controller.ControllerGroups[0].AddPair();

            _controller.CurrentMode = Properties.Settings.Default.defaultModeOnLaunch
                ? MulticontrollerMode.MirrorAll
                : MulticontrollerMode.Group;

            ReloadOptions();

            _controller.ActiveCustomModeId = Properties.Settings.Default.lastActiveCustomModeId ?? "";
            _controller.EnsureValidActiveCustomModeId();

            _viewModel = new MainViewModel(_controller, Dispatcher);
            DataContext = _viewModel;

            leftCrosshair.WindowSelected += (s, handle) => _viewModel.AssignLeftWindow(handle);
            rightCrosshair.WindowSelected += (s, handle) => _viewModel.AssignRightWindow(handle);

            OnSuspendStateChanged();
        }

        private void ReloadOptions()
        {
            this.Topmost = Properties.Settings.Default.onTopWhenInactive;
            _controller.UpdateOptions();
            _controller.RefreshAllControllerBorders();

            // Reset suspension, allow re-reporting hotkey conflicts, and rebuild all registrations.
            _inputHost.OnSettingsReloaded();
            _viewModel?.ForceRefresh();
        }

        /// <summary>Reflect the global-hotkeys-suspended state in the chip and the taskbar title (UX-08).</summary>
        private void OnSuspendStateChanged()
        {
            bool suspended = _inputHost != null && _inputHost.IsGlobalHotkeysSuspended;
            if (_viewModel != null)
                _viewModel.IsSuspended = suspended;
            this.Title = suspended ? "Toontown Multicontroller — Hotkeys Suspended" : "Toontown Multicontroller";
        }

        protected override void OnActivated(EventArgs e)
        {
            base.OnActivated(e);
            if (_controller != null)
            {
                _controller.IsActive = true;
                _inputHost?.RegisterFocusHotkeys();
            }
            if (!_shownOnce)
            {
                _shownOnce = true;
                _inputHost?.OnShellShown();
            }
        }

        protected override void OnDeactivated(EventArgs e)
        {
            base.OnDeactivated(e);
            // The user deliberately focused another window — cancel any pending activation loop.
            _inputHost?.CancelActivation();
            if (_controller != null)
                _controller.IsActive = false;
            _inputHost?.RegisterFocusHotkeys();
        }

        protected override void OnClosing(CancelEventArgs e)
        {
            base.OnClosing(e);

            ComponentDispatcher.ThreadPreprocessMessage -= OnThreadPreprocessMessage;
            _hwndSource?.RemoveHook(WndProcHook);

            // Shut down all input capture before the HWND is destroyed so no orphaned low-level hook keeps
            // processing system input.
            _inputHost?.Dispose();
            _viewModel?.Dispose();

            WindowWatcher.Instance.Shutdown();

            SaveWindowPosition();
        }

        // ── Admin-rights prompt (raised by the host) ────────────────────────────────

        private void InputHost_AdminRightsPromptNeeded(object sender, EventArgs e)
        {
            // Held forwarded keys were already released by the host; defer the modal prompt onto the loop.
            Dispatcher.BeginInvoke(new Action(PromptForAdminRights));
        }

        private void PromptForAdminRights()
        {
            _ignoreMessages = true;
            try
            {
                var result = System.Windows.MessageBox.Show(this,
                    "There was an error controlling a Toontown window. You may need to run the multicontroller as administrator.\n\nDo you want to re-launch as administrator?",
                    "Error", System.Windows.MessageBoxButton.YesNo, System.Windows.MessageBoxImage.Warning);

                if (result == System.Windows.MessageBoxResult.Yes)
                {
                    Properties.Settings.Default.runAsAdministrator = true;
                    Properties.Settings.Default.Save();

                    if (Program.TryRunAsAdmin(includeNewUi: true))
                        System.Windows.Application.Current.Shutdown();
                    else
                        System.Windows.MessageBox.Show(this, "Failed to re-launch as administrator.", "Error");
                }
            }
            finally
            {
                _ignoreMessages = false;
            }
        }

        // ── Dialogs (still the WinForms dialogs until R8/R9) ────────────────────────

        private void OptionsButton_Click(object sender, RoutedEventArgs e)
        {
            var settings = new TTMulti.Ui.Settings.SettingsWindow { Owner = this };
            _ignoreMessages = true;
            bool? result = settings.ShowDialog();
            _ignoreMessages = false;

            if (result == true)
            {
                ReloadOptions();
                _controller.EnsureValidActiveCustomModeId();
            }
        }

        private void GroupsButton_Click(object sender, RoutedEventArgs e)
        {
            _controller.ShowAllBorders = true;
            _ignoreMessages = true;
            new WindowGroupsForm().ShowDialog(new Win32WindowOwner(_hwnd));
            _ignoreMessages = false;
            _controller.ShowAllBorders = false;
        }

        /// <summary>Lets a WinForms modal dialog use the WPF window as its owner (via HWND).</summary>
        private sealed class Win32WindowOwner : System.Windows.Forms.IWin32Window
        {
            public Win32WindowOwner(IntPtr handle) { Handle = handle; }
            public IntPtr Handle { get; }
        }

        // ── Window position (shared lastLocation setting) ───────────────────────────

        private void RestoreWindowPosition()
        {
            var saved = Properties.Settings.Default.lastLocation;
            if (saved == System.Drawing.Point.Empty)
                return;

            // lastLocation is stored in physical pixels (the WinForms shell writes it that way). Only restore
            // if the point falls on a connected screen so the window can't open off-screen.
            foreach (var screen in System.Windows.Forms.Screen.AllScreens)
            {
                if (screen.Bounds.Contains(saved))
                {
                    this.WindowStartupLocation = WindowStartupLocation.Manual;
                    this.Left = saved.X;
                    this.Top = saved.Y;
                    return;
                }
            }
        }

        private void SaveWindowPosition()
        {
            Properties.Settings.Default.lastLocation = new System.Drawing.Point((int)this.Left, (int)this.Top);
            Properties.Settings.Default.Save();
        }
    }
}
