using System;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Windows.Media;
using System.Windows.Threading;

namespace TTMulti.Ui.ViewModels
{
    /// <summary>
    /// Backs the compact WPF main window: projects the engine's live state (mode, current group, group count,
    /// mode-lock, the current group's first-pair window handles + toon accent colours) into bindable
    /// properties, and turns mode-button / crosshair interactions back into engine calls. All engine events
    /// are marshalled onto the UI dispatcher before touching bindings.
    /// </summary>
    internal sealed class MainViewModel : INotifyPropertyChanged, IDisposable
    {
        private readonly Multicontroller _controller;
        private readonly Dispatcher _dispatcher;

        private ToontownController _subscribedLeft;
        private ToontownController _subscribedRight;

        public event PropertyChangedEventHandler PropertyChanged;

        internal MainViewModel(Multicontroller controller, Dispatcher dispatcher)
        {
            _controller = controller;
            _dispatcher = dispatcher;

            _controller.ModeChanged += OnEngineChanged;
            _controller.GroupsChanged += OnEngineChanged;
            _controller.ActiveControllersChanged += OnEngineChanged;
            _controller.ActiveChanged += OnEngineChanged;
            _controller.SettingChanged += OnEngineChanged;
            _controller.ControlledMulticlickModeChanged += OnEngineChanged;

            Refresh();
        }

        // ── Bindable state ──────────────────────────────────────────────────────────

        private string _modeSummary = "";
        public string ModeSummary
        {
            get => _modeSummary;
            private set => Set(ref _modeSummary, value);
        }

        private string _groupCountText = "";
        public string GroupCountText
        {
            get => _groupCountText;
            private set => Set(ref _groupCountText, value);
        }

        private bool _isModeLocked;
        public bool IsModeLocked
        {
            get => _isModeLocked;
            private set
            {
                if (Set(ref _isModeLocked, value))
                    OnPropertyChanged(nameof(AreModeButtonsEnabled));
            }
        }

        public bool AreModeButtonsEnabled => !_isModeLocked;

        private bool _isSuspended;
        /// <summary>Set by the window from the input host's suspend-state event (not an engine property).</summary>
        public bool IsSuspended
        {
            get => _isSuspended;
            set => Set(ref _isSuspended, value);
        }

        public bool IsMultiMode
        {
            get => _controller.CurrentMode == MulticontrollerMode.Group;
            set { if (value) _controller.CurrentMode = MulticontrollerMode.Group; }
        }

        public bool IsMirrorMode
        {
            get => _controller.CurrentMode == MulticontrollerMode.MirrorAll;
            set { if (value) _controller.CurrentMode = MulticontrollerMode.MirrorAll; }
        }

        private IntPtr _leftWindowHandle;
        public IntPtr LeftWindowHandle
        {
            get => _leftWindowHandle;
            private set => Set(ref _leftWindowHandle, value);
        }

        private IntPtr _rightWindowHandle;
        public IntPtr RightWindowHandle
        {
            get => _rightWindowHandle;
            private set => Set(ref _rightWindowHandle, value);
        }

        private Brush _leftAccentBrush = Brushes.Transparent;
        public Brush LeftAccentBrush
        {
            get => _leftAccentBrush;
            private set => Set(ref _leftAccentBrush, value);
        }

        private Brush _rightAccentBrush = Brushes.Transparent;
        public Brush RightAccentBrush
        {
            get => _rightAccentBrush;
            private set => Set(ref _rightAccentBrush, value);
        }

        // ── Crosshair drops ─────────────────────────────────────────────────────────

        public void AssignLeftWindow(IntPtr handle) => AssignWindow(left: true, handle);
        public void AssignRightWindow(IntPtr handle) => AssignWindow(left: false, handle);

        private void AssignWindow(bool left, IntPtr handle)
        {
            int gi = _controller.CurrentGroupIndex;
            if (gi < 0 || gi >= _controller.ControllerGroups.Count)
                return;
            var group = _controller.ControllerGroups[gi];
            if (group.ControllerPairs.Count == 0)
                return;

            if (left)
                group.ControllerPairs[0].LeftController.WindowHandle = handle;
            else
                group.ControllerPairs[0].RightController.WindowHandle = handle;

            UpdateWindowHandles();
        }

        // ── Engine → VM ─────────────────────────────────────────────────────────────

        /// <summary>Force a full re-read of engine state (used after Options closes / mode-lock toggles).</summary>
        public void ForceRefresh() => OnEngineChanged(this, EventArgs.Empty);

        private void OnEngineChanged(object sender, EventArgs e)
        {
            if (_dispatcher.CheckAccess())
                Refresh();
            else
                _dispatcher.BeginInvoke(new Action(Refresh));
        }

        private void Refresh()
        {
            ModeSummary = GetStatusModeSummaryText();
            int groups = _controller.ControllerGroups.Count;
            GroupCountText = groups + (groups == 1 ? " group" : " groups");
            IsModeLocked = _controller.IsModeLockEngaged;

            LeftAccentBrush = ToBrush(Colors.LeftGroup);
            RightAccentBrush = ToBrush(Colors.RightGroup);

            OnPropertyChanged(nameof(IsMultiMode));
            OnPropertyChanged(nameof(IsMirrorMode));

            ResubscribeFirstPair();
            UpdateWindowHandles();
        }

        private void UpdateWindowHandles()
        {
            LeftWindowHandle = _controller.LeftControllers.FirstOrDefault()?.WindowHandle ?? IntPtr.Zero;
            RightWindowHandle = _controller.RightControllers.FirstOrDefault()?.WindowHandle ?? IntPtr.Zero;
        }

        /// <summary>
        /// Keep a subscription on the CURRENT group's first-pair controllers so a window closing (cleared by
        /// the watcher) or an external assignment updates the crosshairs immediately, even without a broad
        /// engine event.
        /// </summary>
        private void ResubscribeFirstPair()
        {
            var left = _controller.LeftControllers.FirstOrDefault();
            var right = _controller.RightControllers.FirstOrDefault();

            if (!ReferenceEquals(left, _subscribedLeft))
            {
                if (_subscribedLeft != null) _subscribedLeft.WindowHandleChanged -= OnPairHandleChanged;
                _subscribedLeft = left;
                if (_subscribedLeft != null) _subscribedLeft.WindowHandleChanged += OnPairHandleChanged;
            }
            if (!ReferenceEquals(right, _subscribedRight))
            {
                if (_subscribedRight != null) _subscribedRight.WindowHandleChanged -= OnPairHandleChanged;
                _subscribedRight = right;
                if (_subscribedRight != null) _subscribedRight.WindowHandleChanged += OnPairHandleChanged;
            }
        }

        private void OnPairHandleChanged(object sender, EventArgs e)
        {
            if (_dispatcher.CheckAccess())
                UpdateWindowHandles();
            else
                _dispatcher.BeginInvoke(new Action(UpdateWindowHandles));
        }

        private string GetStatusModeSummaryText()
        {
            int g = _controller.CurrentGroupIndex + 1;
            switch (_controller.CurrentMode)
            {
                case MulticontrollerMode.Group: return "Multi Mode  ·  Group " + g;
                case MulticontrollerMode.MirrorAll: return "Mirror Mode";
                case MulticontrollerMode.AllGroup: return "All Groups Mode";
                case MulticontrollerMode.Focused: return "Focused Mode";
                case MulticontrollerMode.Custom:
                    var def = _controller.GetActiveCustomModeDefinition();
                    return def != null && !string.IsNullOrWhiteSpace(def.Name) ? def.Name : "Custom Mode";
                case MulticontrollerMode.Pair: return "Pair  ·  Group " + g;
                case MulticontrollerMode.MirrorGroup: return "Mirror Group  ·  Group " + g;
                case MulticontrollerMode.MirrorIndividual: return "Mirror One";
                default: return _controller.CurrentMode.ToString();
            }
        }

        private static SolidColorBrush ToBrush(System.Drawing.Color c)
        {
            var brush = new SolidColorBrush(System.Windows.Media.Color.FromArgb(c.A, c.R, c.G, c.B));
            brush.Freeze();
            return brush;
        }

        // ── INotifyPropertyChanged ──────────────────────────────────────────────────

        private bool Set<T>(ref T field, T value, [CallerMemberName] string name = null)
        {
            if (System.Collections.Generic.EqualityComparer<T>.Default.Equals(field, value))
                return false;
            field = value;
            OnPropertyChanged(name);
            return true;
        }

        private void OnPropertyChanged([CallerMemberName] string name = null) =>
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

        public void Dispose()
        {
            _controller.ModeChanged -= OnEngineChanged;
            _controller.GroupsChanged -= OnEngineChanged;
            _controller.ActiveControllersChanged -= OnEngineChanged;
            _controller.ActiveChanged -= OnEngineChanged;
            _controller.SettingChanged -= OnEngineChanged;
            _controller.ControlledMulticlickModeChanged -= OnEngineChanged;

            if (_subscribedLeft != null) _subscribedLeft.WindowHandleChanged -= OnPairHandleChanged;
            if (_subscribedRight != null) _subscribedRight.WindowHandleChanged -= OnPairHandleChanged;
        }
    }
}
