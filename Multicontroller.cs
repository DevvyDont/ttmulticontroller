using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Windows.Forms;
using System.Drawing;
using System.Threading;
using System.Diagnostics;
using System.Runtime.InteropServices;
using TTMulti.Forms;
using TTMulti.Controls;

namespace TTMulti
{
    internal enum MulticontrollerMode
    {
        /// <summary>
        /// Control all pairs of toons in the current group with separate left and right controls (default mode)
        /// </summary>
        Group,

        /// <summary>
        /// Control both toons in the current pair with separate left and right controls
        /// </summary>
        Pair,

        /// <summary>
        /// Control all groups of toons with separate left and right controls
        /// </summary>
        AllGroup,

        /// <summary>
        /// Mirror all input to all groups of toons
        /// </summary>
        MirrorAll,

        /// <summary>
        /// Mirror all input to all pairs of the current group
        /// </summary>
        MirrorGroup,

    /// <summary>
    /// Mirror all input to one controller
    /// </summary>
    MirrorIndividual,
    
    /// <summary>
    /// Focused mode - all input goes to all windows except directional movement keys
    /// </summary>
    Focused,

    /// <summary>
    /// User-defined mode: per-key routing from <see cref="CustomModeStorage"/>.
    /// </summary>
    Custom
}

    class Multicontroller
    {
        private static Multicontroller _instance = null;

        internal static Multicontroller Instance
        {
            get
            {
                if (_instance == null)
                {
                    _instance = new Multicontroller();

                    int numberOfGroups = Properties.Settings.Default.numberOfGroups;
                    // Ensure at least one group is always created
                    if (numberOfGroups <= 0)
                    {
                        numberOfGroups = 1;
                        Properties.Settings.Default.numberOfGroups = 1;
                        Properties.Settings.Default.Save();
                    }

                    for (int i = 0; i < numberOfGroups; i++)
                    {
                        _instance.AddControllerGroup();
                    }
                }

                return _instance;
            }
        }

        /// <summary>
        /// The multicontroller was activated or deactivated
        /// </summary>
        public event EventHandler ActiveChanged;

        /// <summary>
        /// The mode of the multicontroller changed
        /// </summary>
        public event EventHandler ModeChanged;

        /// <summary>
        /// The controllers that are active changed
        /// </summary>
        public event EventHandler ActiveControllersChanged;

        /// <summary>
        /// A group was added or removed
        /// </summary>
        public event EventHandler GroupsChanged;

        /// <summary>
        /// A misc. setting of the multicontroller was changed
        /// </summary>
        public event EventHandler SettingChanged;

        /// <summary>
        /// Controlled Multi-Click Mode was entered or exited
        /// </summary>
        public event EventHandler ControlledMulticlickModeChanged;

        /// <summary>
        /// The multicontroller should be actived (due to a hotkey)
        /// </summary>
        public event EventHandler ShouldActivate;

        /// <summary>
        /// A controlled window was activated
        /// </summary>
        public event EventHandler WindowActivated;

        /// <summary>
        /// All controlled windows are now inactive
        /// </summary>
        public event EventHandler AllWindowsInactive;
        
        internal List<ControllerGroup> ControllerGroups { get; } = new List<ControllerGroup>();

        internal IEnumerable<ToontownController> ActiveControllers
        {
            get
            {
                switch (CurrentMode)
                {
                    case MulticontrollerMode.Group:
                        if (ControllerGroups.Count > 0 && CurrentGroupIndex < ControllerGroups.Count)
                        {
                            return ControllerGroups[CurrentGroupIndex].AllControllers;
                        }
                        return new ToontownController[] { };
                    case MulticontrollerMode.AllGroup:
                    case MulticontrollerMode.MirrorAll:
                    case MulticontrollerMode.Focused:
                    case MulticontrollerMode.Custom:
                        return AllControllers;
                }

                return new ToontownController[] { };
            }
        }

        /// <summary>
        /// Whether a controller is in the active set. Mirrors <see cref="ActiveControllers"/> but avoids rebuilding
        /// and linearly scanning the whole all-controllers set on every controller's Refresh (the O(N^2) refresh
        /// storm): non-Group modes are O(1), Group mode only scans the active group (PERF-05).
        /// </summary>
        internal bool IsActiveController(ToontownController controller)
        {
            switch (CurrentMode)
            {
                case MulticontrollerMode.Group:
                    return ControllerGroups.Count > 0 && CurrentGroupIndex < ControllerGroups.Count
                        && ControllerGroups[CurrentGroupIndex].AllControllers.Contains(controller);
                case MulticontrollerMode.AllGroup:
                case MulticontrollerMode.MirrorAll:
                case MulticontrollerMode.Focused:
                case MulticontrollerMode.Custom:
                    return true; // ActiveControllers == AllControllers in these modes
                default:
                    return false;
            }
        }

        /// <summary>
        /// Returns only controllers that have a window and are not minimized (so input can be sent to them).
        /// </summary>
        private static IEnumerable<ToontownController> WhereNotMinimized(IEnumerable<ToontownController> controllers)
        {
            return controllers.Where(c => c.HasWindow && Win32.GetWindowShowState(c.WindowHandle) != Win32.ShowWindowCommands.ShowMinimized);
        }


        /// <summary>
        /// Performs instant multi-click: sends a left click at the current cursor position to all active, non-minimized windows.
        /// Used by both keyboard hotkey and mouse-button trigger.
        /// <para>
        /// When <paramref name="activateIfInactive"/> is <c>false</c> (trigger-on-release path), the MC
        /// is expected to have been activated on the preceding key/button press via
        /// <see cref="EnsureActiveForMultiClick"/>.  Skipping the ShouldActivate call on release prevents
        /// a second TryActivate attempt that would fight with game-window focus.
        /// </para>
        /// </summary>
        public void TriggerInstantMultiClick(bool activateIfInactive = true, Point? cursorOverride = null, bool separateLR = false)
        {
            // Use the cursor position captured at press time when provided — this prevents a
            // click miss when the cursor drifts between press and release (or MC activation moves
            // the cursor off the game window).
            Point cursorPos = cursorOverride ?? System.Windows.Forms.Control.MousePosition;
            int relativeX = 0;
            int relativeY = 0;
            bool foundCursorWindow = false;

            ControllerType? cursorSide = null;
            foreach (ToontownController c in WhereNotMinimized(AllControllersWithWindows))
            {
                Point clientAreaLocation = Win32.GetWindowClientAreaLocation(c.WindowHandle);
                Size clientAreaSize = c.WindowSize;
                if (cursorPos.X >= clientAreaLocation.X && cursorPos.X < clientAreaLocation.X + clientAreaSize.Width &&
                    cursorPos.Y >= clientAreaLocation.Y && cursorPos.Y < clientAreaLocation.Y + clientAreaSize.Height)
                {
                    relativeX = cursorPos.X - clientAreaLocation.X;
                    relativeY = cursorPos.Y - clientAreaLocation.Y;
                    cursorSide = c.Type;
                    foundCursorWindow = true;
                    break;
                }
            }

            if (!foundCursorWindow)
            {
                if (!IsActive && activateIfInactive)
                {
                    ShouldActivate?.Invoke(this, EventArgs.Empty);
                    if (!_isControlledMulticlickMode && !_modeLockEngaged)
                        CurrentMode = MulticontrollerMode.MirrorAll;
                }
                return;
            }

            if (!IsActive && activateIfInactive)
            {
                ShouldActivate?.Invoke(this, EventArgs.Empty);
                // Only force mirror mode for standalone instant multi-click.
                // When called from CMC mode the user's existing mode must be preserved.
                if (!_isControlledMulticlickMode && !_modeLockEngaged)
                    CurrentMode = MulticontrollerMode.MirrorAll;
            }

            IEnumerable<ToontownController> toClick = WhereNotMinimized(ActiveControllers);
            // separateLR: distinct L/R slots (not Mirror). Never applied in Custom mode.
            bool isMultiMode = CurrentMode == MulticontrollerMode.Group
                            || CurrentMode == MulticontrollerMode.AllGroup
                            || CurrentMode == MulticontrollerMode.Pair;
            if (separateLR && cursorSide.HasValue && isMultiMode)
                toClick = toClick.Where(c => c.Type == cursorSide.Value);
            if (Properties.Settings.Default.multiclickOrder == 1)
            {
                // Window order: sort by position (top to bottom, left to right) for consistent "Toon 1, 2, 3, 4" style order
                toClick = toClick.OrderBy(c => Win32.GetWindowClientAreaLocation(c.WindowHandle).Y)
                    .ThenBy(c => Win32.GetWindowClientAreaLocation(c.WindowHandle).X)
                    .ToList();
            }

            foreach (ToontownController c in toClick)
            {
                if (c.HasWindow)
                {
                    IntPtr clickLParam = (IntPtr)((relativeY << 16) | (relativeX & 0xFFFF));
                    c.PostMessage(Win32.WM.LBUTTONDOWN, (IntPtr)Win32.MK_LBUTTON, clickLParam);
                    c.PostMessage(Win32.WM.LBUTTONUP, IntPtr.Zero, clickLParam);
                }
            }
        }

        /// <summary>
        /// Sends a left-click at the current cursor position to only the game window under the cursor.
        /// Used by Controlled Multi-Click Mode's "regular click" bind.
        /// </summary>
        public void TriggerRegularClick()
        {
            Point cursorPos = System.Windows.Forms.Control.MousePosition;

            foreach (ToontownController c in WhereNotMinimized(AllControllersWithWindows))
            {
                Point clientAreaLocation = Win32.GetWindowClientAreaLocation(c.WindowHandle);
                Size clientAreaSize = c.WindowSize;
                if (cursorPos.X >= clientAreaLocation.X && cursorPos.X < clientAreaLocation.X + clientAreaSize.Width &&
                    cursorPos.Y >= clientAreaLocation.Y && cursorPos.Y < clientAreaLocation.Y + clientAreaSize.Height)
                {
                    int relativeX = cursorPos.X - clientAreaLocation.X;
                    int relativeY = cursorPos.Y - clientAreaLocation.Y;
                    IntPtr clickLParam = (IntPtr)((relativeY << 16) | (relativeX & 0xFFFF));
                    c.PostMessage(Win32.WM.LBUTTONDOWN, (IntPtr)Win32.MK_LBUTTON, clickLParam);
                    c.PostMessage(Win32.WM.LBUTTONUP, IntPtr.Zero, clickLParam);
                    break;
                }
            }
        }

        int currentGroupIndex = 0;

        /// <summary>
        /// The index of the group that is currently being controlled, if applicable in the current mode
        /// </summary>
        internal int CurrentGroupIndex
        {
            get
            {
                if (ControllerGroups.Count > 0 && currentGroupIndex >= ControllerGroups.Count)
                {
                    currentGroupIndex = 0;
                }

                return currentGroupIndex;
            }
            private set
            {
                if (currentGroupIndex != value)
                {
                    // Release held keys before the active group changes (CORR-05).
                    ReleaseAllHeldForwardedKeys();

                    currentGroupIndex = value;

                    ActiveControllersChanged?.Invoke(this, EventArgs.Empty);
                    TryReleaseKeysOnInactiveControllers();
                }
            }
        }

        int _currentPairIndex = 0;

        /// <summary>
        /// The index of the current pair that is being controlled (in pair mode)
        /// </summary>
        internal int CurrentPairIndex
        {
            get
            {
                if (_currentPairIndex >= AllControllerPairsWithWindows.Count())
                {
                    _currentPairIndex = 0;
                }

                return _currentPairIndex;
            }
            set
            {
                if (_currentPairIndex != value)
                {
                    // Release held keys before the active pair changes (CORR-05).
                    ReleaseAllHeldForwardedKeys();

                    _currentPairIndex = value;

                    ActiveControllersChanged?.Invoke(this, EventArgs.Empty);
                    TryReleaseKeysOnInactiveControllers();
                }
            }
        }

        int _currentIndividualControllerIndex = 0;

        internal int CurrentIndividualControllerIndex
        {
            get
            {
                if (AllControllersWithWindows.Count() > 0 && _currentIndividualControllerIndex >= AllControllersWithWindows.Count())
                {
                    _currentIndividualControllerIndex = 0;
                }

                return _currentIndividualControllerIndex;
            }
            private set
            {
                if (_currentIndividualControllerIndex != value)
                {
                    _currentIndividualControllerIndex = value;

                    ActiveControllersChanged?.Invoke(this, EventArgs.Empty);
                    TryReleaseKeysOnInactiveControllers();
                }
            }
        }

        /// <summary>
        /// Left controllers of the current group, or all groups if all groups are being controlled at once
        /// </summary>
        internal IEnumerable<ToontownController> LeftControllers
        {
            get
            {
                if (CurrentMode == MulticontrollerMode.AllGroup)
                {
                    return ControllerGroups.SelectMany(g => g.LeftControllers);
                }
                else
                {
                    if (ControllerGroups.Count > 0 && CurrentGroupIndex < ControllerGroups.Count)
                    {
                        return ControllerGroups[CurrentGroupIndex].LeftControllers;
                    }
                    return new ToontownController[] { };
                }
            }
        }

        /// <summary>
        /// Right controllers of the current group, or all groups if all groups are being controlled at once
        /// </summary>
        internal IEnumerable<ToontownController> RightControllers
        {
            get
            {
                if (CurrentMode == MulticontrollerMode.AllGroup)
                {
                    return ControllerGroups.SelectMany(g => g.RightControllers);
                }
                else if (CurrentMode == MulticontrollerMode.Pair)
                {
                    if (CurrentControllerPair != null)
                    {
                        return new[] { CurrentControllerPair?.RightController };
                    }
                    else
                    {
                        return new ToontownController[] { };
                    }
                }
                else
                {
                    if (ControllerGroups.Count > 0 && CurrentGroupIndex < ControllerGroups.Count)
                    {
                        return ControllerGroups[CurrentGroupIndex].RightControllers;
                    }
                    return new ToontownController[] { };
                }
            }
        }

        /// <summary>
        /// The current controller that is being controlled individually
        /// </summary>
        internal ToontownController CurrentIndividualController
        {
            get
            {
                if (CurrentIndividualControllerIndex < AllControllersWithWindows.Count())
                {
                    return AllControllersWithWindows.ElementAt(CurrentIndividualControllerIndex);
                }

                return null;
            }
        }

        internal IEnumerable<ToontownController> AllControllers
        {
            get
            {
                return ControllerGroups.SelectMany(g => g.ControllerPairs.SelectMany(p => new[] { p.LeftController, p.RightController }));
            }
        }

        internal IEnumerable<ToontownController> AllControllersWithWindows
        {
            get
            {
                return AllControllers.Where(c => c.HasWindow);
            }
        }

        internal IEnumerable<ControllerPair> AllControllerPairs
        {
            get
            {
                return ControllerGroups.SelectMany(g => g.ControllerPairs);
            }
        }

        internal IEnumerable<ControllerPair> AllControllerPairsWithWindows
        {
            get
            {
                return AllControllerPairs.Where(p => p.LeftController.HasWindow || p.RightController.HasWindow);
            }
        }

        internal ControllerPair CurrentControllerPair
        {
            get
            {
                if (AllControllerPairsWithWindows.Count() > 0)
                {
                    return AllControllerPairsWithWindows.ElementAt(CurrentPairIndex);
                }

                return null;
            }
        }

        /// <summary>
        /// Whether an error occurred when posting a message to a Toontown window.
        /// This usually indicated that we don't have enough privileges and need to run as administrator.
        /// </summary>
        public bool ErrorOccurredPostingMessage
        {
            get => ControllerGroups.Any(g => g.AllControllers.Any(c => c.ErrorOccurredPostingMessage));
        }

        private bool showAllBorders = false;
        public bool ShowAllBorders
        {
            get => showAllBorders;
            set
            {
                if (showAllBorders != value)
                {
                    showAllBorders = value;

                    SettingChanged?.Invoke(this, EventArgs.Empty);
                }
            }
        }

        private bool _isActive = false;
        internal bool IsActive
        {
            get { return _isActive; }
            set
            {
                if (_isActive != value)
                {
                    _isActive = value;

                    ActiveChanged?.Invoke(this, EventArgs.Empty);
                }
            }
        }

        MulticontrollerMode _currentMode = MulticontrollerMode.Group;
        MulticontrollerMode _modeBeforeAllGroup = MulticontrollerMode.Group;

        internal MulticontrollerMode CurrentMode
        {
            get { return _currentMode; }
            set
            {
                if (_currentMode != value)
                {
                    // Release any keys currently held down in the games before the routing changes.  Otherwise the
                    // pending KEYUP is translated/targeted under the NEW mode and never reaches the key that is
                    // actually held down under the old mode, leaving toons walking forever (CORR-05).
                    ReleaseAllHeldForwardedKeys();

                    _currentMode = value;

                    if (value == MulticontrollerMode.Custom)
                        EnsureValidActiveCustomModeId();
                    
                    // Clear focused controller when mode changes away from Focused
                    if (value != MulticontrollerMode.Focused)
                    {
                        _focusedController = null;
                    }
                    
                    ModeChanged?.Invoke(this, EventArgs.Empty);
                    ActiveControllersChanged?.Invoke(this, EventArgs.Empty);
                    TryReleaseKeysOnInactiveControllers();
                }
            }
        }
        
        Dictionary<Keys, List<Keys>> leftKeys = new Dictionary<Keys, List<Keys>>(),
            rightKeys = new Dictionary<Keys, List<Keys>>();
        
        bool zeroPowerThrowKeyPressed = false;

        // Track the focused window when entering Focused mode via Zero Power Throw
        private ToontownController _focusedController = null;
        
        /// <summary>
        /// Check if a controller is the focused controller in Focused mode
        /// </summary>
        internal bool IsFocusedController(ToontownController controller)
        {
            return CurrentMode == MulticontrollerMode.Focused && _focusedController == controller;
        }

        private bool _modeLockEngaged;

        /// <summary>
        /// When true, mode-changing hotkeys (mode cycle, group/mirror/all-group, group number keys, etc.) are ignored.
        /// Toggled via <see cref="ToggleModeLock"/> or the registered mode-lock hotkey.
        /// </summary>
        internal bool IsModeLockEngaged => _modeLockEngaged;

        internal void ToggleModeLock()
        {
            _modeLockEngaged = !_modeLockEngaged;
            SettingChanged?.Invoke(this, EventArgs.Empty);
        }

        string _activeCustomModeId = "";

        /// <summary>Id of the <see cref="CustomModeDefinition"/> used when <see cref="CurrentMode"/> is <see cref="MulticontrollerMode.Custom"/>.</summary>
        internal string ActiveCustomModeId
        {
            get => _activeCustomModeId ?? "";
            set => _activeCustomModeId = value ?? "";
        }

        internal void PersistActiveCustomModeIdToUserSettings()
        {
            _activeCustomModeId = _activeCustomModeId ?? "";
            Properties.Settings.Default.lastActiveCustomModeId = _activeCustomModeId;
            Properties.Settings.Default.Save();
        }

        internal void EnsureValidActiveCustomModeId()
        {
            CustomModeFile file = CustomModeStorage.LoadCached();
            if (file.Modes == null || file.Modes.Count == 0)
            {
                _activeCustomModeId = "";
                return;
            }
            if (string.IsNullOrEmpty(_activeCustomModeId) || !file.Modes.Any(m => string.Equals(m.Id, _activeCustomModeId, StringComparison.Ordinal)))
                _activeCustomModeId = file.Modes[0].Id;
        }

        internal CustomModeDefinition GetActiveCustomModeDefinition()
        {
            CustomModeFile file = CustomModeStorage.LoadCached();
            if (file.Modes == null || string.IsNullOrEmpty(_activeCustomModeId))
                return null;
            return file.Modes.FirstOrDefault(m => string.Equals(m.Id, _activeCustomModeId, StringComparison.Ordinal));
        }

        internal Color GetActiveCustomModeBorderColorFor(ControllerType slotType)
        {
            CustomModeDefinition def = GetActiveCustomModeDefinition();
            if (def == null)
                return Colors.AllGroups;
            return slotType == ControllerType.Left ? def.GetLeftBorderColor() : def.GetRightBorderColor();
        }

        /// <summary>Re-reads border appearance from settings / custom-modes.json (call after Options OK).</summary>
        internal void RefreshAllControllerBorders()
        {
            foreach (ToontownController c in AllControllers)
                c.Refresh();
        }

        /// <summary>
        /// Switch to a custom definition by id (updates borders when already in <see cref="MulticontrollerMode.Custom"/>).
        /// </summary>
        internal void ActivateCustomModeDefinition(string modeId)
        {
            if (string.IsNullOrEmpty(modeId))
                return;
            CustomModeFile file = CustomModeStorage.LoadCached();
            if (file.Modes == null || !file.Modes.Any(m => string.Equals(m.Id, modeId, StringComparison.Ordinal)))
                return;

            bool wasAlreadyCustom = _currentMode == MulticontrollerMode.Custom;
            _activeCustomModeId = modeId;
            PersistActiveCustomModeIdToUserSettings();

            if (!wasAlreadyCustom)
            {
                CurrentMode = MulticontrollerMode.Custom;
            }
            else
            {
                SettingChanged?.Invoke(this, EventArgs.Empty);
                RefreshAllControllerBorders();
            }
        }

        readonly struct ModeHotkeyCycleEntry
        {
            internal readonly MulticontrollerMode Mode;
            internal readonly string CustomModeId;

            internal ModeHotkeyCycleEntry(MulticontrollerMode mode, string customModeId)
            {
                Mode = mode;
                CustomModeId = customModeId ?? "";
            }
        }

        List<ModeHotkeyCycleEntry> BuildModeHotkeyCycleList()
        {
            var list = new List<ModeHotkeyCycleEntry>();
            if (Properties.Settings.Default.groupModeCycleWithModeHotkey)
                list.Add(new ModeHotkeyCycleEntry(MulticontrollerMode.Group, null));
            if (Properties.Settings.Default.mirrorModeCycleWithModeHotkey)
                list.Add(new ModeHotkeyCycleEntry(MulticontrollerMode.MirrorAll, null));
            if (Properties.Settings.Default.allGroupModeCycleWithModeHotkey)
                list.Add(new ModeHotkeyCycleEntry(MulticontrollerMode.AllGroup, null));
            if (Properties.Settings.Default.customModeCycleWithModeHotkey)
            {
                CustomModeFile cf = CustomModeStorage.LoadCached();
                if (cf.Modes != null)
                {
                    foreach (CustomModeDefinition m in cf.Modes.OrderBy(x => x.Name, StringComparer.OrdinalIgnoreCase))
                    {
                        if (m.ShouldIncludeInModeHotkeyCycle())
                            list.Add(new ModeHotkeyCycleEntry(MulticontrollerMode.Custom, m.Id));
                    }
                }
            }
            return list;
        }

        static int IndexOfCurrentModeInCycleList(List<ModeHotkeyCycleEntry> list, MulticontrollerMode currentMode, string activeCustomModeId)
        {
            for (int i = 0; i < list.Count; i++)
            {
                if (list[i].Mode != currentMode)
                    continue;
                if (currentMode != MulticontrollerMode.Custom)
                    return i;
                if (string.Equals(list[i].CustomModeId, activeCustomModeId ?? "", StringComparison.Ordinal))
                    return i;
            }
            return -1;
        }

        void ApplyModeHotkeyCycleEntry(ModeHotkeyCycleEntry entry)
        {
            if (entry.Mode == MulticontrollerMode.Custom)
                ActivateCustomModeDefinition(entry.CustomModeId);
            else
                CurrentMode = entry.Mode;
        }

        /// <summary>
        /// Controllers with windows, non-minimized, in the same order as instant multiclick (<c>multiclickOrder</c>).
        /// </summary>
        internal List<ToontownController> GetControllersInCustomModeOrder()
        {
            IEnumerable<ToontownController> src = WhereNotMinimized(AllControllersWithWindows);
            if (Properties.Settings.Default.multiclickOrder == 1)
            {
                src = src.OrderBy(c => Win32.GetWindowClientAreaLocation(c.WindowHandle).Y)
                    .ThenBy(c => Win32.GetWindowClientAreaLocation(c.WindowHandle).X);
            }
            return src.ToList();
        }

        /// <summary>
        /// Left-click at the cursor (or center of the client area) on a single window.
        /// </summary>
        internal void TriggerInstantMultiClickSingleTarget(ToontownController target)
        {
            if (target == null || !target.HasWindow)
                return;
            Point cursorPos = Control.MousePosition;
            Point clientLoc = Win32.GetWindowClientAreaLocation(target.WindowHandle);
            Size sz = target.WindowSize;
            int relX, relY;
            if (cursorPos.X >= clientLoc.X && cursorPos.X < clientLoc.X + sz.Width &&
                cursorPos.Y >= clientLoc.Y && cursorPos.Y < clientLoc.Y + sz.Height)
            {
                relX = cursorPos.X - clientLoc.X;
                relY = cursorPos.Y - clientLoc.Y;
            }
            else
            {
                relX = Math.Max(0, sz.Width / 2);
                relY = Math.Max(0, sz.Height / 2);
            }
            IntPtr clickLParam = (IntPtr)((relY << 16) | (relX & 0xFFFF));
            target.PostMessage(Win32.WM.LBUTTONDOWN, (IntPtr)Win32.MK_LBUTTON, clickLParam);
            target.PostMessage(Win32.WM.LBUTTONUP, IntPtr.Zero, clickLParam);
        }

        /// <summary>
        /// When mode lock is on, consume keys that would change <see cref="CurrentMode"/> or <see cref="CurrentGroupIndex"/>.
        /// Does not handle the mode-lock toggle key (handled earlier). Returns true if the message was consumed.
        /// </summary>
        private bool TryConsumeModeLockBlockedInput(Win32.WM msg, Keys keysPressed)
        {
            if (!_modeLockEngaged)
                return false;

            bool isMetaDown = msg == Win32.WM.KEYDOWN || msg == Win32.WM.HOTKEY || msg == Win32.WM.SYSKEYDOWN;

            // Mode/activate key: allow activation when inactive; when active, block mode changes (unless modifiers pass through)
            if (keysPressed == (Keys)Properties.Settings.Default.modeKeyCode)
            {
                if (!IsActive)
                    return false;
                Keys currentModifiers = System.Windows.Forms.Control.ModifierKeys;
                if ((currentModifiers & (Keys.Shift | Keys.Control | Keys.Alt)) != Keys.None)
                    return false;
                return true;
            }

            if (Properties.Settings.Default.groupModeKeyCode != 0
                && keysPressed == (Keys)Properties.Settings.Default.groupModeKeyCode
                && isMetaDown)
                return true;
            if (Properties.Settings.Default.mirrorModeKeyCode != 0
                && keysPressed == (Keys)Properties.Settings.Default.mirrorModeKeyCode
                && isMetaDown)
                return true;
            if (Properties.Settings.Default.controlAllGroupsKeyCode != 0
                && keysPressed == (Keys)Properties.Settings.Default.controlAllGroupsKeyCode
                && (msg == Win32.WM.HOTKEY || msg == Win32.WM.KEYDOWN))
                return true;

            if (CurrentMode == MulticontrollerMode.Group
                && !_switchingMode
                && ControllerGroups.Count > 1
                && (keysPressed >= Keys.D0 && keysPressed <= Keys.D9
                    || keysPressed >= Keys.NumPad0 && keysPressed <= Keys.NumPad9)
                && isMetaDown)
                return true;

            return false;
        }

        // Controlled Multi-Click Mode state
        private bool _isControlledMulticlickMode = false;

        /// <summary>
        /// Whether Controlled Multi-Click Mode is currently active.
        /// In this mode, fake cursors are shown on all game windows and the configured click
        /// key sends a left-click to every non-minimized window.
        /// </summary>
        internal bool IsControlledMulticlickMode => _isControlledMulticlickMode;

        /// <summary>
        /// Enters Controlled Multi-Click Mode.
        /// </summary>
        public void EnterControlledMulticlickMode()
        {
            if (_isControlledMulticlickMode) return;
            _isControlledMulticlickMode = true;
            // Bring the multicontroller window to the foreground so hotkeys work and
            // the mode state is correct before any click fires.
            if (!IsActive)
                ShouldActivate?.Invoke(this, EventArgs.Empty);
            ControlledMulticlickModeChanged?.Invoke(this, EventArgs.Empty);
            SettingChanged?.Invoke(this, EventArgs.Empty);
        }

        /// <summary>
        /// Exits Controlled Multi-Click Mode.
        /// </summary>
        public void ExitControlledMulticlickMode()
        {
            if (!_isControlledMulticlickMode) return;
            _isControlledMulticlickMode = false;
            ControlledMulticlickModeChanged?.Invoke(this, EventArgs.Empty);
            SettingChanged?.Invoke(this, EventArgs.Empty);
        }

        // Window switching mode state
        private bool _switchingMode = false;
        private ToontownController _firstSelectedController = null;
        private ToontownController _secondSelectedController = null;
        private System.Windows.Forms.Timer _switchingModeTimer = null;
        private HashSet<ToontownController> _switchedControllers = new HashSet<ToontownController>();
        private HashSet<ToontownController> _markedForRemoval = new HashSet<ToontownController>();

        /// <summary>
        /// Per-swap screen bounds (GetWindowRect) captured before HWND assignment is exchanged; applied on Alt release when enabled.
        /// </summary>
        private struct SwapScreenGeometryOp
        {
            internal IntPtr Hwnd1;
            internal IntPtr Hwnd2;
            internal Win32.RECT Rect1;
            internal Win32.RECT Rect2;
        }

        private readonly List<SwapScreenGeometryOp> _swapScreenGeometryOps = new List<SwapScreenGeometryOp>();

        // Global mouse hook for blocking clicks in switching mode
        private static IntPtr _mouseHookHandle = IntPtr.Zero;
        private static Multicontroller _hookInstance = null;
        private static Win32.HookProc _mouseHookProc = null;

        /// <summary>
        /// Whether switching mode is currently active
        /// </summary>
        internal bool IsSwitchingMode => _switchingMode;

        internal Multicontroller()
        {
            UpdateOptions();
            
            // Initialize switching mode timer for mouse tracking
            _switchingModeTimer = new System.Windows.Forms.Timer();
            _switchingModeTimer.Interval = 50; // Check every 50ms
            _switchingModeTimer.Tick += SwitchingModeTimer_Tick;
        }

        internal void UpdateOptions()
        {
            leftKeys.Clear();
            rightKeys.Clear();

            var keyBindings = Properties.SerializedSettings.Default.Bindings;

            for (int i = 0; i < keyBindings.Count; i++)
            {
                if (!leftKeys.ContainsKey(keyBindings[i].LeftToonKey))
                {
                    leftKeys.Add(keyBindings[i].LeftToonKey, new List<Keys>());
                }

                if (!rightKeys.ContainsKey(keyBindings[i].RightToonKey))
                {
                    rightKeys.Add(keyBindings[i].RightToonKey, new List<Keys>());
                }

                if (keyBindings[i].Key != Keys.None && keyBindings[i].LeftToonKey != Keys.None)
                {
                    leftKeys[keyBindings[i].LeftToonKey].Add(keyBindings[i].Key);
                }

                if (keyBindings[i].Key != Keys.None && keyBindings[i].RightToonKey != Keys.None)
                {
                    rightKeys[keyBindings[i].RightToonKey].Add(keyBindings[i].Key);
                }
            }
        }

        /// <summary>
        /// Check if a key is a directional movement key (Forward, Left, Backward, Right)
        /// </summary>
        private bool IsDirectionalKey(Keys key)
        {
            var keyBindings = Properties.SerializedSettings.Default.Bindings;
            
            // Check if this key maps to any directional movement action
            foreach (var binding in keyBindings)
            {
                if (binding.Key == key)
                {
                    string title = binding.Title.ToLower();
                    if (title == "forward" || title == "left" || title == "backward" || title == "right")
                    {
                        return true;
                    }
                }
            }
            
            return false;
        }

        private void SwitchingModeTimer_Tick(object sender, EventArgs e)
        {
            if (!_switchingMode)
            {
                _switchingModeTimer.Stop();
                return;
            }

            // Check if Alt key is still pressed
            // If Alt+Tab was used, Windows might consume the Alt key release event,
            // so we need to check the actual key state
            short altKeyState = Win32.GetAsyncKeyState(Keys.Menu);
            bool altIsPressed = (altKeyState & 0x8000) != 0;

            // If Alt is no longer pressed, exit switching mode
            // This handles the case where Alt+Tab consumes the Alt key release event
            if (!altIsPressed)
            {
                ExitSwitchingMode();
                return;
            }

            // Update switching mode display for all controllers
            // Don't trigger SettingChanged here - it's called when state actually changes
            UpdateSwitchingModeDisplay(false);
        }

        private void UpdateSwitchingModeDisplay(bool triggerRefresh = true)
        {
            // Calculate switching numbers based on group and type (Left/Right)
            // All controllers with the same group and type get the same number, regardless of pair
            // Numbering: Group 1 Left = 1, Group 1 Right = 2, Group 2 Left = 3, Group 2 Right = 4, etc.
            
            // Apply switching numbers to all controllers with windows
            foreach (var controller in AllControllersWithWindows)
            {
                var borderWnd = GetBorderWindow(controller);
                if (borderWnd != null)
                {
                    borderWnd.SwitchingMode = true;
                    
                    // Calculate switching number: (GroupNumber - 1) * 2 + (Left = 1, Right = 2)
                    int switchingNumber = (controller.GroupNumber - 1) * 2 + (controller.Type == ControllerType.Left ? 1 : 2);
                    borderWnd.SwitchingNumber = switchingNumber;
                    
                    // Selected windows (first or second selected) are Yellow
                    bool isSelected = (controller == _firstSelectedController || controller == _secondSelectedController);
                    // Switched windows (in _switchedControllers but not currently selected) are Orange
                    bool isSwitched = _switchedControllers.Contains(controller) && !isSelected;
                    // Marked for removal windows are Black
                    bool isMarkedForRemoval = _markedForRemoval.Contains(controller);
                    
                    borderWnd.SwitchingSelected = isSelected;
                    borderWnd.SwitchingSwitched = isSwitched;
                    borderWnd.SwitchingMarkedForRemoval = isMarkedForRemoval;
                }
            }
            
            // Trigger refresh on all controllers to update caption colors after switching mode properties are set
            // Only trigger if requested (not on every timer tick)
            if (triggerRefresh)
            {
                SettingChanged?.Invoke(this, EventArgs.Empty);
            }
        }
        

        private void ExitSwitchingMode()
        {
            _switchingMode = false;
            _switchingModeTimer.Stop();
            _firstSelectedController = null;
            _secondSelectedController = null;
            
            // Uninstall global mouse hook
            UninstallMouseHook();

            // Disconnect all controllers marked for removal
            foreach (var controller in _markedForRemoval.ToList())
            {
                if (controller != null && controller.HasWindow)
                {
                    controller.WindowHandle = IntPtr.Zero;
                }
            }
            _markedForRemoval.Clear();

            _switchedControllers.Clear();

            // Reset all border windows so switching colors clear when Alt is released
            foreach (var controller in AllControllersWithWindows)
            {
                var borderWnd = GetBorderWindow(controller);
                if (borderWnd != null)
                {
                    borderWnd.SwitchingMode = false;
                    borderWnd.SwitchingNumber = 0;
                    borderWnd.SwitchingSelected = false;
                    borderWnd.SwitchingSwitched = false;
                    borderWnd.SwitchingMarkedForRemoval = false;
                }
            }

            // Refresh all controllers so border color returns to normal (mirror/group) on every window
            SettingChanged?.Invoke(this, EventArgs.Empty);

            if (Properties.Settings.Default.autoFindPlacementOnAltRelease && _swapScreenGeometryOps.Count > 0)
            {
                try
                {
                    ApplyRecordedSwapScreenGeometry();
                }
                catch { /* ignore placement errors */ }
            }

            _swapScreenGeometryOps.Clear();
        }

        /// <summary>
        /// Move each swapped HWND to the other window's pre-swap GetWindowRect bounds (only windows still assigned to a controller).
        /// </summary>
        private void ApplyRecordedSwapScreenGeometry()
        {
            var assigned = new HashSet<IntPtr>(
                AllControllersWithWindows.Select(c => c.WindowHandle).Where(h => h != IntPtr.Zero));

            var validOps = new List<SwapScreenGeometryOp>();
            foreach (var op in _swapScreenGeometryOps)
            {
                if (op.Hwnd1 == IntPtr.Zero || op.Hwnd2 == IntPtr.Zero)
                    continue;
                if (!Win32.IsWindow(op.Hwnd1) || !Win32.IsWindow(op.Hwnd2))
                    continue;
                if (!assigned.Contains(op.Hwnd1) || !assigned.Contains(op.Hwnd2))
                    continue;
                validOps.Add(op);
            }

            if (validOps.Count == 0)
                return;

            foreach (var op in validOps)
            {
                foreach (IntPtr h in new[] { op.Hwnd1, op.Hwnd2 })
                {
                    if (Win32.GetWindowShowState(h) == Win32.ShowWindowCommands.ShowMinimized)
                        Win32.ShowWindow(h, Win32.ShowWindowCommands.Restore);
                }
            }

            var flags = Win32.SetWindowPosFlags.ShowWindow | Win32.SetWindowPosFlags.DoNotActivate;
            var toInvalidate = new HashSet<IntPtr>();

            IntPtr hdwp = Win32.BeginDeferWindowPos(validOps.Count * 2);
            if (hdwp != IntPtr.Zero)
            {
                foreach (var op in validOps)
                {
                    int w1 = op.Rect1.Right - op.Rect1.Left;
                    int h1 = op.Rect1.Bottom - op.Rect1.Top;
                    int w2 = op.Rect2.Right - op.Rect2.Left;
                    int h2 = op.Rect2.Bottom - op.Rect2.Top;
                    hdwp = Win32.DeferWindowPos(hdwp, op.Hwnd1, IntPtr.Zero, op.Rect2.Left, op.Rect2.Top, w2, h2, flags);
                    hdwp = Win32.DeferWindowPos(hdwp, op.Hwnd2, IntPtr.Zero, op.Rect1.Left, op.Rect1.Top, w1, h1, flags);
                    toInvalidate.Add(op.Hwnd1);
                    toInvalidate.Add(op.Hwnd2);
                }
                Win32.EndDeferWindowPos(hdwp);
            }
            else
            {
                foreach (var op in validOps)
                {
                    int w1 = op.Rect1.Right - op.Rect1.Left;
                    int h1 = op.Rect1.Bottom - op.Rect1.Top;
                    int w2 = op.Rect2.Right - op.Rect2.Left;
                    int h2 = op.Rect2.Bottom - op.Rect2.Top;
                    Win32.SetWindowPos(op.Hwnd1, IntPtr.Zero, op.Rect2.Left, op.Rect2.Top, w2, h2, flags);
                    Win32.SetWindowPos(op.Hwnd2, IntPtr.Zero, op.Rect1.Left, op.Rect1.Top, w1, h1, flags);
                    toInvalidate.Add(op.Hwnd1);
                    toInvalidate.Add(op.Hwnd2);
                }
            }

            System.Windows.Forms.Application.DoEvents();
            foreach (var c in AllControllersWithWindows)
            {
                if (toInvalidate.Contains(c.WindowHandle))
                    c.UpdateBorderPosition();
            }
        }

        /// <summary>
        /// Clear the list of switched controllers and reset their highlighting
        /// </summary>
        internal void ClearSwitchedControllers()
        {
            foreach (var controller in _switchedControllers)
            {
                var borderWnd = GetBorderWindow(controller);
                if (borderWnd != null)
                {
                    borderWnd.SwitchingSelected = false;
                    borderWnd.SwitchingSwitched = false;
                }
            }
            _switchedControllers.Clear();
        }

        private BorderWnd GetBorderWindow(ToontownController controller)
        {
            // Use reflection to access the private _borderWnd field
            var field = typeof(ToontownController).GetField("_borderWnd", 
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            return field?.GetValue(controller) as BorderWnd;
        }

        private ToontownController GetControllerUnderCursor()
        {
            Point cursorPos;
            if (!Win32.GetCursorPos(out cursorPos))
                return null;

            return GetControllerAtPoint(cursorPos);
        }

        /// <summary>Find which controller's game window contains the given screen point (null if none).</summary>
        private ToontownController GetControllerAtPoint(Point screenPoint)
        {
            foreach (var controller in AllControllersWithWindows)
            {
                Point clientAreaLocation = Win32.GetWindowClientAreaLocation(controller.WindowHandle);
                Size clientAreaSize = controller.WindowSize;

                if (screenPoint.X >= clientAreaLocation.X && screenPoint.X < clientAreaLocation.X + clientAreaSize.Width &&
                    screenPoint.Y >= clientAreaLocation.Y && screenPoint.Y < clientAreaLocation.Y + clientAreaSize.Height)
                {
                    return controller;
                }
            }

            return null;
        }

        /// <summary>
        /// Get the window handle under the cursor that isn't already assigned to a controller
        /// </summary>
        private IntPtr GetUnassignedWindowHandleUnderCursor()
        {
            Point cursorPos;
            if (!Win32.GetCursorPos(out cursorPos))
                return IntPtr.Zero;

            // Get the window at the cursor position
            IntPtr hWnd = Win32.WindowFromPoint(cursorPos);
            if (hWnd == IntPtr.Zero)
                return IntPtr.Zero;

            // Get the root window (top-level window)
            IntPtr rootWnd = Win32.GetAncestor(hWnd, Win32.GetAncestorFlags.GetRoot);
            if (rootWnd == IntPtr.Zero)
                return IntPtr.Zero;

            // Check if this window is already assigned to a controller
            var currentlyAssignedHandles = new HashSet<IntPtr>(
                AllControllersWithWindows.Select(c => c.WindowHandle).Where(h => h != IntPtr.Zero)
            );

            if (currentlyAssignedHandles.Contains(rootWnd))
                return IntPtr.Zero;

            // Verify the window is visible and valid
            if (!Win32.IsWindowVisible(rootWnd) || !Win32.IsWindow(rootWnd))
                return IntPtr.Zero;

            return rootWnd;
        }

        private void SwitchWindows(ToontownController controller1, ToontownController controller2)
        {
            if (controller1 == null || controller2 == null || controller1 == controller2)
                return;

            // Get current window handles
            IntPtr handle1 = controller1.WindowHandle;
            IntPtr handle2 = controller2.WindowHandle;

            if (handle1 == IntPtr.Zero || handle2 == IntPtr.Zero)
                return;

            if (Properties.Settings.Default.autoFindPlacementOnAltRelease
                && Win32.IsWindow(handle1) && Win32.IsWindow(handle2)
                && Win32.GetWindowShowState(handle1) != Win32.ShowWindowCommands.ShowMinimized
                && Win32.GetWindowShowState(handle2) != Win32.ShowWindowCommands.ShowMinimized)
            {
                if (Win32.GetWindowRect(handle1, out Win32.RECT r1) && Win32.GetWindowRect(handle2, out Win32.RECT r2))
                {
                    _swapScreenGeometryOps.Add(new SwapScreenGeometryOp
                    {
                        Hwnd1 = handle1,
                        Hwnd2 = handle2,
                        Rect1 = r1,
                        Rect2 = r2
                    });
                }
            }

            // Only swap window handle assignments (group/pair assignments)
            // Don't move or resize windows here; optional screen exchange runs on Alt release
            controller1.WindowHandle = handle2;
            controller2.WindowHandle = handle1;

            // Show switched (blue) during switching mode; color clears to normal when Alt is released
            _switchedControllers.Add(controller1);
            _switchedControllers.Add(controller2);

            // Update border positions after switching.  This runs on the UI thread (via the deferred hook
            // continuation or ProcessMouseInput), so no DoEvents/Sleep pump is needed; WindowWatcher ticks
            // refine the positions afterward.  Pumping here re-entered the low-level hook (CORR-02 / WIN32-01).
            controller1.UpdateBorderPosition();
            controller2.UpdateBorderPosition();
        }

        internal ControllerGroup AddControllerGroup()
        {
            ControllerGroup group = new ControllerGroup(ControllerGroups.Count + 1);

            group.ControllerWindowActivated += Controller_WindowActivated;
            group.ControllerWindowDeactivated += Controller_WindowDeactivated;
            group.ControllerWindowHandleChanged += Controller_WindowHandleChanged;
            group.ControllerShouldActivate += Controller_ShouldActivate;
            group.MouseEvent += Controller_MouseEvent;

            ControllerGroups.Add(group);
            
            // Update and save numberOfGroups setting to persist groups between sessions
            Properties.Settings.Default.numberOfGroups = ControllerGroups.Count;
            Properties.Settings.Default.Save();
            
            GroupsChanged?.Invoke(this, EventArgs.Empty);

            return group;
        }

        private void Controller_ShouldActivate(object sender, EventArgs e)
        {
            ToontownController controller = sender as ToontownController;

            if (!ActiveControllers.Contains(controller))
            {
                switch (CurrentMode)
                {
                    case MulticontrollerMode.Group:
                        ControllerGroup group = ControllerGroups.First(g => g.AllControllers.Contains(controller));

                        CurrentGroupIndex = ControllerGroups.IndexOf(group);
                        break;
                }
            }
        }

        private void Controller_MouseEvent(object sender, Message m)
        {
            ProcessInput(m.Msg, m.WParam, m.LParam, sender as ToontownController);
        }

        internal void RemoveControllerGroup(int index)
        {
            ControllerGroup controllerGroup = ControllerGroups[index];
            controllerGroup.Dispose();
            ControllerGroups.Remove(controllerGroup);
            
            // Update and save numberOfGroups setting to persist groups between sessions
            // Ensure at least one group remains
            if (ControllerGroups.Count > 0)
            {
                Properties.Settings.Default.numberOfGroups = ControllerGroups.Count;
                Properties.Settings.Default.Save();
            }
            
            GroupsChanged?.Invoke(this, EventArgs.Empty);
        }

        /// <summary>
        /// The main input processor. All input to the multicontroller window ends up here.
        /// </summary>
        /// <returns>Whether the input is discarded (doesn't reach its intended destination)</returns>
        internal bool ProcessInput(int msgCode, IntPtr wParam, IntPtr lParam, ToontownController sourceController = null) 
        {
            Win32.WM msg = (Win32.WM)msgCode;
            bool isKeyboardInput = false;
            bool isMouseInput = false;
            Keys keysPressed = Keys.None;

            switch (msg)
            {
                case Win32.WM.KEYDOWN:
                case Win32.WM.KEYUP:
                case Win32.WM.SYSKEYDOWN:
                case Win32.WM.SYSKEYUP:
                    isKeyboardInput = true;
                    keysPressed = (Keys)wParam;
                    break;
                case Win32.WM.HOTKEY:
                    isKeyboardInput = true;
                    keysPressed = (Keys)(int)(lParam.ToInt64() >> 16);
                    break;
                case Win32.WM.MOUSEMOVE:
                case Win32.WM.LBUTTONDOWN:
                case Win32.WM.LBUTTONUP:
                case Win32.WM.RBUTTONDOWN:
                case Win32.WM.RBUTTONUP:
                case Win32.WM.MBUTTONDOWN:
                case Win32.WM.MBUTTONUP:
                case Win32.WM.MOUSEHOVER:
                case Win32.WM.MOUSEWHEEL:
                case Win32.WM.MOUSELEAVE:
                    isMouseInput = true;
                    break;
            }

            if (isMouseInput)
            {
                return ProcessMouseInput(msg, wParam, lParam, sourceController);
            }
            else if (isKeyboardInput)
            {
                return ProcessMetaKeyboardInput(msg, keysPressed)
                    || ProcessKeyboardInput(msg, wParam, lParam);
            }

            return false;
        }

        /// <summary>
        /// Process keyboard input for meta actions (hotkeys, changing groups, etc.)
        /// </summary>
        /// <returns>True the input was handled as a meta input</returns>
        private bool ProcessMetaKeyboardInput(Win32.WM msg, Keys keysPressed)
        {
            int modeLockToggleCode = Properties.Settings.Default.modeLockToggleKeyCode;
            if (modeLockToggleCode != 0 && keysPressed == (Keys)modeLockToggleCode)
            {
                if (msg == Win32.WM.KEYDOWN || msg == Win32.WM.HOTKEY)
                {
                    ToggleModeLock();
                    return true;
                }
            }

            if (TryConsumeModeLockBlockedInput(msg, keysPressed))
                return true;

            if (keysPressed == (Keys)Properties.Settings.Default.modeKeyCode)
            {
                if (msg == Win32.WM.HOTKEY || msg == Win32.WM.KEYDOWN)
                {
                    // Check if any modifiers are currently pressed - if so, don't switch modes, let it pass through to games
                    Keys currentModifiers = System.Windows.Forms.Control.ModifierKeys;
                    bool hasModifiers = (currentModifiers & (Keys.Shift | Keys.Control | Keys.Alt)) != Keys.None;
                    
                    if (hasModifiers)
                    {
                        // Modifiers are pressed - don't switch modes, return false to let it pass through to ProcessKeyboardInput
                        return false;
                    }
                    
                    if (IsActive)
                    {
                        // Special case: If in Focused mode, switch to Mirror mode
                        if (CurrentMode == MulticontrollerMode.Focused)
                        {
                            CurrentMode = MulticontrollerMode.MirrorAll;
                        }
                        else
                        {
                            List<ModeHotkeyCycleEntry> cycle = BuildModeHotkeyCycleList();
                            if (cycle.Count > 0)
                            {
                                int currentModeIndex = IndexOfCurrentModeInCycleList(cycle, CurrentMode, ActiveCustomModeId);

                                if (currentModeIndex >= 0)
                                {
                                    int next = (currentModeIndex + 1) % cycle.Count;
                                    ApplyModeHotkeyCycleEntry(cycle[next]);
                                }
                                else
                                    ApplyModeHotkeyCycleEntry(cycle[0]);
                            }
                        }
                    }
                    else
                    {
                        ShouldActivate?.Invoke(this, EventArgs.Empty);
                    }
                }
            }
            else if (keysPressed == (Keys)Properties.Settings.Default.groupModeKeyCode)
            {
                // Only switch on key-down/hotkey (never on key-up), like every other mode key.  Acting on
                // key-up let a held group key silently revert AllGroup and bypass mode lock (CORR-04).
                if (msg == Win32.WM.HOTKEY || msg == Win32.WM.KEYDOWN)
                    CurrentMode = MulticontrollerMode.Group;
            }
            else if (keysPressed == (Keys)Properties.Settings.Default.mirrorModeKeyCode)
            {
                if (msg == Win32.WM.HOTKEY || msg == Win32.WM.KEYDOWN)
                    CurrentMode = MulticontrollerMode.MirrorAll;
            }
            else if (keysPressed == (Keys)Properties.Settings.Default.controlAllGroupsKeyCode)
            {
                if (msg == Win32.WM.HOTKEY || msg == Win32.WM.KEYDOWN)
                {
                    if (CurrentMode == MulticontrollerMode.AllGroup)
                        CurrentMode = _modeBeforeAllGroup;
                    else
                    {
                        _modeBeforeAllGroup = CurrentMode;
                        CurrentMode = MulticontrollerMode.AllGroup;
                    }
                }
            }
            else if (keysPressed == (Keys)Properties.Settings.Default.replicateMouseKeyCode
                && Properties.Settings.Default.replicateMouseKeyCode != 0)
            {
                // If any modifiers are down, treat this as a normal key so combinations like Shift+F6 pass through
                Keys currentModifiers = System.Windows.Forms.Control.ModifierKeys;
                bool hasModifiers = (currentModifiers & (Keys.Shift | Keys.Control | Keys.Alt)) != Keys.None;
                if (hasModifiers)
                {
                    return false;
                }

                // Instant Multi-Click: Send a click to all windows at current cursor position
                if (msg == Win32.WM.KEYDOWN || msg == Win32.WM.HOTKEY)
                {
                    // When trigger-on-release is enabled the keyboard hook owns the click (fires on
                    // key-up).  If a raw KEYDOWN reaches here it means MC is the active window and
                    // the hook suppressed the key before posting it — but just in case, guard here too.
                    if (Properties.Settings.Default.multiclickTriggerOnRelease && msg == Win32.WM.KEYDOWN)
                        return true; // consume without firing; hook fires on release
                    TriggerInstantMultiClick(separateLR: Properties.Settings.Default.replicateMouseSeparateLR);
                    return true;
                }
            }
            else if (keysPressed == Keys.Menu) // Alt key
            {
                // Handle Alt key for switching mode (only if enabled)
                if (!Properties.Settings.Default.switchingModeEnabled)
                {
                    return false; // Don't handle Alt if switching mode is disabled
                }
                
                if (msg == Win32.WM.SYSKEYDOWN || msg == Win32.WM.KEYDOWN)
                {
                    if (!_switchingMode)
                    {
                        // Enter switching mode (allow even when not active or no windows are connected)
                        // Activate multicontroller if not already active
                        if (!IsActive)
                        {
                            ShouldActivate?.Invoke(this, EventArgs.Empty);
                        }
                        
                        _switchingMode = true;
                        _firstSelectedController = null;
                        _secondSelectedController = null;
                        _markedForRemoval.Clear(); // Clear removal marks when entering switching mode
                        _swapScreenGeometryOps.Clear();
                        _switchingModeTimer.Start();
                        
                        // Install global mouse hook to block clicks during switching mode
                        InstallMouseHook();
                        
                        // Update switching mode display first (sets properties on BorderWnd)
                        // This will trigger refresh to update caption colors
                        UpdateSwitchingModeDisplay(true);
                        return true;
                    }
                }
                else if (msg == Win32.WM.SYSKEYUP || msg == Win32.WM.KEYUP)
                {
                    if (_switchingMode)
                    {
                        // Exit switching mode
                        ExitSwitchingMode();
                        return true;
                    }
                }
            }
            else if (_switchingMode && keysPressed == (Keys)Properties.Settings.Default.switchingModeRemoveKeyCode)
            {
                // Handle remove key in switching mode - toggle removal mark on the controller under cursor
                if (msg == Win32.WM.KEYDOWN || msg == Win32.WM.SYSKEYDOWN)
                {
                    var controllerUnderCursor = GetControllerUnderCursor();
                    if (controllerUnderCursor != null && controllerUnderCursor.HasWindow)
                    {
                        // Toggle removal mark
                        if (_markedForRemoval.Contains(controllerUnderCursor))
                        {
                            // Remove from removal list (unmark)
                            _markedForRemoval.Remove(controllerUnderCursor);
                        }
                        else
                        {
                            // Add to removal list (mark for removal)
                            _markedForRemoval.Add(controllerUnderCursor);
                            
                            // Clear selection if this controller was selected
                            if (_firstSelectedController == controllerUnderCursor)
                            {
                                _firstSelectedController = null;
                            }
                            if (_secondSelectedController == controllerUnderCursor)
                            {
                                _secondSelectedController = null;
                            }
                        }
                        
                        UpdateSwitchingModeDisplay();
                    }
                    return true;
                }
            }
            else if (_switchingMode && keysPressed == (Keys)Properties.Settings.Default.switchingModeSwitchKeyCode)
            {
                // Handle switch/select key in switching mode
                if (msg == Win32.WM.KEYDOWN || msg == Win32.WM.SYSKEYDOWN)
                {
                    var controllerUnderCursor = GetControllerUnderCursor();
                    if (controllerUnderCursor != null && controllerUnderCursor.HasWindow)
                    {
                        // If clicking on a window marked for removal, unmark it first
                        if (_markedForRemoval.Contains(controllerUnderCursor))
                        {
                            _markedForRemoval.Remove(controllerUnderCursor);
                        }
                        
                        if (_firstSelectedController == null)
                        {
                            // Select first window
                            _firstSelectedController = controllerUnderCursor;
                            UpdateSwitchingModeDisplay();
                        }
                        else if (_secondSelectedController == null && controllerUnderCursor != _firstSelectedController)
                        {
                            // Select second window and switch
                            _secondSelectedController = controllerUnderCursor;
                            SwitchWindows(_firstSelectedController, _secondSelectedController);
                            
                            // Reset selection state but keep switching mode active (Alt is still held)
                            _firstSelectedController = null;
                            _secondSelectedController = null;
                            UpdateSwitchingModeDisplay();
                        }
                        else if (controllerUnderCursor == _firstSelectedController)
                        {
                            // Pressing the same window again deselects it
                            _firstSelectedController = null;
                            UpdateSwitchingModeDisplay();
                        }
                        else
                        {
                            // Just update display if we unmarked a removal
                            UpdateSwitchingModeDisplay();
                        }
                    }
                    return true;
                }
            }
            else if (_switchingMode && (keysPressed >= Keys.D1 && keysPressed <= Keys.D9
                || keysPressed >= Keys.NumPad1 && keysPressed <= Keys.NumPad9))
            {
                // Handle number keys in switching mode to assign windows to specific numbers
                if (msg == Win32.WM.KEYDOWN || msg == Win32.WM.SYSKEYDOWN)
                {
                    IntPtr windowHandle = IntPtr.Zero;
                    
                    // First try to get an already-assigned controller under cursor
                    var controllerUnderCursor = GetControllerUnderCursor();
                    if (controllerUnderCursor != null && controllerUnderCursor.HasWindow)
                    {
                        windowHandle = controllerUnderCursor.WindowHandle;
                    }
                    else
                    {
                        // If no assigned controller, try to find an unassigned window under cursor
                        windowHandle = GetUnassignedWindowHandleUnderCursor();
                    }

                    if (windowHandle != IntPtr.Zero)
                    {
                        // First, remove this window from all existing controllers
                        foreach (var controller in AllControllers)
                        {
                            if (controller.WindowHandle == windowHandle)
                            {
                                controller.WindowHandle = IntPtr.Zero;
                            }
                        }

                        // Convert key to number (1-9)
                        int number;
                        if (keysPressed >= Keys.D1 && keysPressed <= Keys.D9)
                        {
                            number = keysPressed - Keys.D0;
                        }
                        else
                        {
                            number = keysPressed - Keys.NumPad0;
                        }

                        // Calculate group and type from number
                        // Number 1 = Group 1 Left, Number 2 = Group 1 Right, Number 3 = Group 2 Left, etc.
                        int groupNumber = ((number - 1) / 2) + 1;
                        ControllerType targetType = (number % 2 == 1) ? ControllerType.Left : ControllerType.Right;

                        // Find or create the group
                        ControllerGroup targetGroup = ControllerGroups.FirstOrDefault(g => g.GroupNumber == groupNumber);
                        if (targetGroup == null)
                        {
                            // Create new groups until we have the target group
                            while (ControllerGroups.Count < groupNumber)
                            {
                                AddControllerGroup();
                            }
                            targetGroup = ControllerGroups[groupNumber - 1];
                        }

                        // Find the first unused controller of the target type in this group
                        ToontownController targetController = null;
                        foreach (var pair in targetGroup.ControllerPairs.OrderBy(p => p.PairNumber))
                        {
                            var candidate = (targetType == ControllerType.Left) ? pair.LeftController : pair.RightController;
                            if (!candidate.HasWindow)
                            {
                                targetController = candidate;
                                break;
                            }
                        }

                        // If no unused controller found, create a new pair
                        if (targetController == null)
                        {
                            var newPair = targetGroup.AddPair();
                            targetController = (targetType == ControllerType.Left) ? newPair.LeftController : newPair.RightController;
                        }

                        // Assign the window to the target controller
                        if (targetController != null)
                        {
                            targetController.WindowHandle = windowHandle;
                            UpdateSwitchingModeDisplay();
                        }
                    }
                    return true;
                }
            }
            else if (CurrentMode == MulticontrollerMode.Group
                && !_switchingMode  // Don't handle group switching when in switching mode
                && ControllerGroups.Count > 1
                && (keysPressed >= Keys.D0 && keysPressed <= Keys.D9
                    || keysPressed >= Keys.NumPad0 && keysPressed <= Keys.NumPad9))
            {
                // Change groups while in group mode
                int index;

                if (keysPressed >= Keys.D0 && keysPressed <= Keys.D9)
                {
                    index = 9 - (Keys.D9 - keysPressed);
                }
                else
                {
                    index = 9 - (Keys.NumPad9 - keysPressed);
                }

                index = index == 0 ? 9 : index - 1;

                if (ControllerGroups.Count > index)
                {
                    CurrentGroupIndex = index;
                }
            }
            else if (keysPressed == (Keys)Properties.Settings.Default.zeroPowerThrowKeyCode 
                && Properties.Settings.Default.zeroPowerThrowKeyCode != 0)
            {
                // If any modifiers are down, treat this as a normal key so combinations pass through
                Keys currentModifiers = System.Windows.Forms.Control.ModifierKeys;
                bool hasModifiers = (currentModifiers & (Keys.Shift | Keys.Control | Keys.Alt)) != Keys.None;
                if (hasModifiers)
                {
                    return false;
                }

                // Mode lock: inactive zero-power paths change MulticontrollerMode — block them entirely
                if (_modeLockEngaged && !IsActive)
                {
                    if (msg == Win32.WM.KEYDOWN || msg == Win32.WM.HOTKEY)
                        return true;
                    if (msg == Win32.WM.KEYUP)
                    {
                        zeroPowerThrowKeyPressed = false;
                        return true;
                    }
                }

                // Handle Zero Power Throw Hotkey
                if (msg == Win32.WM.KEYDOWN || msg == Win32.WM.HOTKEY)
                {
                    // Prevent key repeat - only trigger on initial press
                    if (zeroPowerThrowKeyPressed)
                    {
                        return true;
                    }
                    zeroPowerThrowKeyPressed = true;
                    
                    // Find the Throw key from bindings
                    var keyBindings = Properties.SerializedSettings.Default.Bindings;
                    var throwBinding = keyBindings.FirstOrDefault(b => b.Title == "Throw");
                    
                    if (throwBinding != null)
                    {
                        if (IsActive)
                        {
                            // Multicontroller is active: Send to all active controllers (skip minimized)
                            IEnumerable<ToontownController> affectedControllers = WhereNotMinimized(ActiveControllers);
                            
                            // Send instant tap of the throw key to all active controllers
                            affectedControllers.ToList().ForEach(c => PostZeroPowerThrow(c, throwBinding));
                        }
                        else
                        {
                            // Multicontroller is NOT active
                            // Check if focus mode is enabled and a window is focused
                            if (Properties.Settings.Default.zeroPowerThrowEnableFocusMode)
                            {
                                // Find the currently active/focused window
                                IntPtr activeWindowHandle = Win32.GetForegroundWindow();
                                var focusedController = AllControllersWithWindows.FirstOrDefault(c => c.WindowHandle == activeWindowHandle);
                                
                                if (focusedController != null)
                                {
                                    // Activate controller and enter Focused mode
                                    ShouldActivate?.Invoke(this, EventArgs.Empty);
                                    CurrentMode = MulticontrollerMode.Focused;
                                    _focusedController = focusedController;
                                    
                                    // Send throw to all windows (as per normal behavior); skip minimized
                                    foreach (var controller in WhereNotMinimized(AllControllersWithWindows))
                                        PostZeroPowerThrow(controller, throwBinding);
                                }
                                else
                                {
                                    // No focused window found, use normal behavior (MirrorAll)
                                    ShouldActivate?.Invoke(this, EventArgs.Empty);
                                    CurrentMode = MulticontrollerMode.MirrorAll;
                                    _focusedController = null;

                                    foreach (var controller in WhereNotMinimized(AllControllersWithWindows))
                                        PostZeroPowerThrow(controller, throwBinding);
                                }
                            }
                            else
                            {
                                // Focus mode disabled: activate in MirrorAll mode and send throw to all windows; skip minimized
                                ShouldActivate?.Invoke(this, EventArgs.Empty);
                                CurrentMode = MulticontrollerMode.MirrorAll;
                                _focusedController = null;

                                foreach (var controller in WhereNotMinimized(AllControllersWithWindows))
                                    PostZeroPowerThrow(controller, throwBinding);
                            }
                        }
                    }
                }
                else if (msg == Win32.WM.KEYUP)
                {
                    // Reset flag when key is released
                    zeroPowerThrowKeyPressed = false;
                }
            }
            else
            {
                return false;
            }

            return true;
        }

        /// <summary>
        /// Process mouse input
        /// </summary>
        /// <returns>True if the input was handled</returns>
        private bool ProcessMouseInput(Win32.WM msg, IntPtr wParam, IntPtr lParam, ToontownController sourceController)
        {
            // Handle mouse clicks in switching mode for window selection/switching
            // Only handle mouse clicks if the switch key is set to a mouse button
            int switchKeyCode = Properties.Settings.Default.switchingModeSwitchKeyCode;
            bool isMouseButtonSwitch = switchKeyCode == 1 || switchKeyCode == 2 || switchKeyCode == 4;
            bool isMatchingMouseButton = (msg == Win32.WM.LBUTTONDOWN && switchKeyCode == 1) ||
                                        (msg == Win32.WM.RBUTTONDOWN && switchKeyCode == 2) ||
                                        (msg == Win32.WM.MBUTTONDOWN && switchKeyCode == 4);
            
            if (_switchingMode && isMatchingMouseButton)
            {
                var controllerUnderCursor = GetControllerUnderCursor();
                if (controllerUnderCursor != null)
                {
                    // If clicking on a window marked for removal, unmark it first
                    if (_markedForRemoval.Contains(controllerUnderCursor))
                    {
                        _markedForRemoval.Remove(controllerUnderCursor);
                    }
                    
                    if (_firstSelectedController == null)
                    {
                        // Select first window
                        _firstSelectedController = controllerUnderCursor;
                        UpdateSwitchingModeDisplay();
                    }
                    else if (_secondSelectedController == null && controllerUnderCursor != _firstSelectedController)
                    {
                        // Select second window and switch
                        _secondSelectedController = controllerUnderCursor;
                        SwitchWindows(_firstSelectedController, _secondSelectedController);
                        
                        // Reset selection state but keep switching mode active (Alt is still held)
                        _firstSelectedController = null;
                        _secondSelectedController = null;
                        UpdateSwitchingModeDisplay();
                    }
                    else if (controllerUnderCursor == _firstSelectedController)
                    {
                        // Clicking the same window again deselects it
                        _firstSelectedController = null;
                        UpdateSwitchingModeDisplay();
                    }
                    else
                    {
                        // Just update display if we unmarked a removal
                        UpdateSwitchingModeDisplay();
                    }
                }
                // Consume the click so it doesn't get sent to the games
                return true;
            }
            
            // Block all mouse input in switching mode (don't send clicks to games)
            if (_switchingMode)
            {
                return true;
            }
            
            // Mouse input processing removed - multiclick is now instant via hotkey
            return false;
        }

        /// <summary>
        /// Process keyboard input
        /// </summary>
        /// <returns>True if the input was handled</returns>
        private bool ProcessKeyboardInput(Win32.WM msg, IntPtr wParam, IntPtr lParam)
        {
            // Block normal input processing when in switching mode
            if (_switchingMode)
            {
                return true; // Consume all input in switching mode
            }

            Keys keysPressed = (Keys)wParam;
            if (msg == Win32.WM.HOTKEY)
                keysPressed = (Keys)(int)(lParam.ToInt64() >> 16);

            if (CurrentMode == MulticontrollerMode.Custom && IsActive)
            {
                CustomModeDefinition def = GetActiveCustomModeDefinition();
                if (def == null)
                {
                    CurrentMode = MulticontrollerMode.Group;
                    return false;
                }
                CustomRouteResult result = CustomModeInputRouter.TryProcess(this, def, msg, wParam, lParam);
                // Unmapped keys are swallowed in Custom mode (as in Group mode); the only pass-through is a
                // matched binding that explicitly opts out of consuming its trigger key (ConsumeInput=false).
                return result != CustomRouteResult.PassThrough;
            }

            if (IsActive)
            {
                IEnumerable<ToontownController> affectedControllers = ActiveControllers;
                List<Keys> keysToPress = new List<Keys>();
                
                if (CurrentMode == MulticontrollerMode.Group 
                    || CurrentMode == MulticontrollerMode.AllGroup)
                {
                    if (leftKeys.ContainsKey(keysPressed) && !rightKeys.ContainsKey(keysPressed))
                    {
                        affectedControllers = affectedControllers.Where(c => c.Type == ControllerType.Left);

                        keysToPress.AddRange(leftKeys[keysPressed]);
                    }
                    else if (!leftKeys.ContainsKey(keysPressed) && rightKeys.ContainsKey(keysPressed))
                    {
                        affectedControllers = affectedControllers.Where(c => c.Type == ControllerType.Right);

                        keysToPress.AddRange(rightKeys[keysPressed]);
                    }
                    else if (leftKeys.ContainsKey(keysPressed) && rightKeys.ContainsKey(keysPressed))
                    {
                        keysToPress.AddRange(leftKeys[keysPressed]);
                        keysToPress.AddRange(rightKeys[keysPressed]);
                    }
                }

                // Materialize once: WhereNotMinimized runs a GetWindowPlacement per controller, and the branches
                // below (and the per-key loop) would otherwise re-run it for every mapped key (PERF-10).
                var affectedList = WhereNotMinimized(affectedControllers).ToList();

                if (CurrentMode == MulticontrollerMode.MirrorAll)
                {
                    foreach (ToontownController c in affectedList)
                        c.PostMessage(msg, wParam, lParam);
                }
                else if (CurrentMode == MulticontrollerMode.Focused)
                {
                    // In Focused mode, check if this is a directional key
                    bool isDirectionalKey = IsDirectionalKey(keysPressed);
                    bool focusedNotMinimized = _focusedController != null && _focusedController.HasWindow
                        && Win32.GetWindowShowState(_focusedController.WindowHandle) != Win32.ShowWindowCommands.ShowMinimized;

                    if (isDirectionalKey && focusedNotMinimized)
                    {
                        // Directional keys only go to the focused window
                        _focusedController.PostMessage(msg, wParam, lParam);
                    }
                    else if (!isDirectionalKey)
                    {
                        // All non-directional keys go to all windows
                        foreach (ToontownController c in affectedList)
                            c.PostMessage(msg, wParam, lParam);
                    }
                    // If isDirectionalKey is true but _focusedController is null/invalid/minimized, don't send to anyone
                }
                else
                {
                    // Group/AllGroup: the physical trigger key is remapped to actualKey, so post an lParam built for
                    // actualKey's own scan code instead of forwarding the trigger key's lParam (WIN32-05).
                    bool isKeyUp = msg == Win32.WM.KEYUP || msg == Win32.WM.SYSKEYUP;
                    foreach (Keys actualKey in keysToPress)
                    {
                        IntPtr keyLParam = Win32.MakePostedKeyLParam(actualKey, isKeyUp);
                        foreach (ToontownController c in affectedList)
                            c.PostMessage(msg, (IntPtr)actualKey, keyLParam);
                    }
                }

                return true;
            }

            return false;
        }

        private void Controller_WindowHandleChanged(object sender, EventArgs e)
        {
            if (!AllControllersWithWindows.Any(c => c.IsWindowActive))
            {
                AllWindowsInactive?.Invoke(this, EventArgs.Empty);
            }
        }

        private void Controller_WindowActivated(object sender, EventArgs e)
        {
            if (Properties.Settings.Default.releaseKeysOnWindowFocus && sender is ToontownController focused)
                ReleaseMovementKeysOnOtherWindows(focused);
            WindowActivated?.Invoke(this, EventArgs.Empty);
        }

        /// <summary>
        /// Sends KEYUP for movement keys to the given controllers (e.g. so they stop moving when focus/active group changes).
        /// Releases both LeftToonKey and RightToonKey for every controller so all modes are covered — in MirrorAll the
        /// raw key is sent to all windows regardless of type, so both sets must be released.  Sending KEYUP for a key
        /// that is not currently pressed is harmless.
        /// </summary>
        private void ReleaseMovementKeysOnControllers(IEnumerable<ToontownController> controllers)
        {
            if (controllers == null) return;

            var movementTitles = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
                { "forward", "left", "backward", "right", "jump" };
            var allMovementKeys = new HashSet<Keys>();
            foreach (var binding in Properties.SerializedSettings.Default.Bindings)
            {
                if (!movementTitles.Contains(binding.Title)) continue;
                if (binding.LeftToonKey != Keys.None) allMovementKeys.Add(binding.LeftToonKey);
                if (binding.RightToonKey != Keys.None) allMovementKeys.Add(binding.RightToonKey);
            }

            // lParam for WM_KEYUP: bit 31 = transition state (1 = releasing), bit 30 = previous state (1 = was down), repeat count = 1.
            // Sending lParam=0 incorrectly signals a key-down transition (bit 31=0), which causes chat boxes to interpret it as a keypress.
            IntPtr keyUpLParam = (IntPtr)unchecked((int)0xC0000001u);

            foreach (ToontownController c in controllers)
            {
                if (c == null || !c.HasWindow) continue;
                foreach (Keys k in allMovementKeys)
                {
                    c.PostMessage(Win32.WM.KEYUP, (IntPtr)k, keyUpLParam);
                }
            }
        }

        /// <summary>
        /// Sends KEYUP for movement keys to all controlled windows except the focused one.
        /// Used when releaseKeysOnWindowFocus is enabled: when user focuses a window by clicking (or switches group), other windows get key-up so they don't keep moving.
        /// </summary>
        internal void ReleaseMovementKeysOnOtherWindows(ToontownController focusedController)
        {
            if (focusedController == null) return;
            var others = WhereNotMinimized(AllControllersWithWindows).Where(c => c != focusedController);
            ReleaseMovementKeysOnControllers(others);
        }

        /// <summary>
        /// When releaseKeysOnWindowFocus is enabled, release movement keys on controllers that are no longer active (e.g. after switching group).
        /// </summary>
        private void TryReleaseKeysOnInactiveControllers()
        {
            if (!Properties.Settings.Default.releaseKeysOnWindowFocus) return;
            var inactive = WhereNotMinimized(AllControllersWithWindows).Except(ActiveControllers);
            ReleaseMovementKeysOnControllers(inactive);
        }

        /// <summary>
        /// Posts KEYUP for every key each controller currently holds down in its game window.  Called before the
        /// active routing changes (mode / group / pair) so a key held across the change is released against the
        /// window that actually has it down, instead of being stranded (CORR-05).
        /// </summary>
        private void ReleaseAllHeldForwardedKeys()
        {
            foreach (ToontownController c in AllControllersWithWindows)
                c.ReleaseAllHeldKeys();
            CustomModeInputRouter.ResetHeldTriggers();
        }

        /// <summary>
        /// Posts an instant 0%-power throw (KEYDOWN immediately followed by KEYUP) to a controller using well-formed
        /// key lParams.  A zero lParam clears the key-up transition bit, which the games misread as a keypress
        /// (see <see cref="ReleaseMovementKeysOnControllers"/>) — WIN32-03.
        /// </summary>
        private void PostZeroPowerThrow(ToontownController controller, KeyMapping throwBinding)
        {
            Keys throwKey = Keys.None;
            if (controller.Type == ControllerType.Left && throwBinding.LeftToonKey != Keys.None)
                throwKey = throwBinding.LeftToonKey;
            else if (controller.Type == ControllerType.Right && throwBinding.RightToonKey != Keys.None)
                throwKey = throwBinding.RightToonKey;

            if (throwKey == Keys.None)
                return;

            controller.PostMessage(Win32.WM.KEYDOWN, (IntPtr)throwKey, Win32.MakePostedKeyLParam(throwKey, false));
            controller.PostMessage(Win32.WM.KEYUP, (IntPtr)throwKey, Win32.MakePostedKeyLParam(throwKey, true));
        }

        private void Controller_WindowDeactivated(object sender, EventArgs e)
        {
            if (!AllControllersWithWindows.Any(c => c.IsWindowActive))
            {
                AllWindowsInactive?.Invoke(this, EventArgs.Empty);
                
                // Exit switching mode when all windows become inactive
                // This handles cases where Alt+Tab switches away from all controlled windows
                if (_switchingMode)
                {
                    ExitSwitchingMode();
                }

                // When the multicontroller goes to the background (no game window is focused),
                // release movement keys on every window so no toon keeps moving indefinitely.
                if (Properties.Settings.Default.releaseKeysOnWindowFocus)
                    ReleaseMovementKeysOnControllers(WhereNotMinimized(AllControllersWithWindows));
            }
        }

        /// <summary>
        /// Automatically find and assign windows from recognized game executables
        /// </summary>
        public void AutoFindAndAssignWindows()
        {
            var executableNames = Properties.Settings.Default.autoFindExecutables
                .Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(e => e.Trim())
                .Where(e => !string.IsNullOrEmpty(e))
                .ToList();

            if (executableNames.Count == 0)
                return;

            // Get currently assigned window handles to check if we're adding new ones
            var currentlyAssignedHandles = new HashSet<IntPtr>(
                AllControllersWithWindows.Select(c => c.WindowHandle).Where(h => h != IntPtr.Zero)
            );

            // Find all processes matching the executable names and get their main windows
            var foundWindows = new List<IntPtr>();
            var processNames = new HashSet<string>(executableNames, StringComparer.OrdinalIgnoreCase);
            
            // Also create a set without .exe extension for matching
            var processNamesNoExt = new HashSet<string>(
                executableNames.Select(e => System.IO.Path.GetFileNameWithoutExtension(e)), 
                StringComparer.OrdinalIgnoreCase
            );
            
            foreach (var process in Process.GetProcesses())
            {
                try
                {
                    // Check if this process matches one of our executable names (with or without .exe)
                    bool matches = processNames.Contains(process.ProcessName) || 
                                   processNamesNoExt.Contains(process.ProcessName);
                    
                    if (matches)
                    {
                        // Get the main window handle for this process
                        IntPtr mainWindowHandle = process.MainWindowHandle;
                        
                        // If MainWindowHandle is zero, try to find the window using EnumWindows
                        if (mainWindowHandle == IntPtr.Zero)
                        {
                            // Find the first visible window for this process
                            Win32.EnumWindows((hWnd, lParam) =>
                            {
                                uint processId;
                                Win32.GetWindowThreadProcessId(hWnd, out processId);
                                if (processId == process.Id && Win32.IsWindowVisible(hWnd))
                                {
                                    mainWindowHandle = hWnd;
                                    return false; // Stop enumeration
                                }
                                return true; // Continue enumeration
                            }, IntPtr.Zero);
                        }
                        
                        // Only add if it's a valid window handle and the window is visible
                        if (mainWindowHandle != IntPtr.Zero && Win32.IsWindowVisible(mainWindowHandle))
                        {
                            // Verify the window is still valid
                            if (Win32.IsWindow(mainWindowHandle))
                            {
                                foundWindows.Add(mainWindowHandle);
                            }
                        }
                    }
                }
                catch
                {
                    // Process might have exited or we don't have access, ignore
                }
            }

            if (foundWindows.Count == 0)
                return;

            // Filter out windows that are already assigned
            var newWindows = foundWindows.Where(h => !currentlyAssignedHandles.Contains(h)).ToList();

            // If no new windows to add, do nothing
            if (newWindows.Count == 0)
                return;

            // Assign windows to controllers in order: Group 1 Left, Group 1 Right, Group 2 Left, Group 2 Right, etc.
            // Only use the first pair (PairNumber == 1) in each group
            int newWindowIndex = 0;
            
            // Iterate through all groups in order
            foreach (var group in ControllerGroups.OrderBy(g => g.GroupNumber))
            {
                // Only use the first pair in each group (PairNumber == 1)
                var firstPair = group.ControllerPairs.FirstOrDefault(p => p.PairNumber == 1);
                if (firstPair == null)
                {
                    // If no first pair exists, create one
                    firstPair = group.AddPair();
                }

                // Try to assign to Left controller first
                if (!firstPair.LeftController.HasWindow && newWindowIndex < newWindows.Count)
                {
                    firstPair.LeftController.WindowHandle = newWindows[newWindowIndex];
                    newWindowIndex++;
                }

                // Then try to assign to Right controller
                if (!firstPair.RightController.HasWindow && newWindowIndex < newWindows.Count)
                {
                    firstPair.RightController.WindowHandle = newWindows[newWindowIndex];
                    newWindowIndex++;
                }

                // If we've assigned all windows, stop
                if (newWindowIndex >= newWindows.Count)
                    break;
            }

            // If there are still new windows to assign, create new groups and assign them
            while (newWindowIndex < newWindows.Count)
            {
                // Create a new group
                var newGroup = AddControllerGroup();
                var firstPair = newGroup.ControllerPairs[0]; // New groups always have at least one pair

                // Assign to Left controller
                firstPair.LeftController.WindowHandle = newWindows[newWindowIndex];
                newWindowIndex++;

                // If there are more windows, assign to Right controller
                if (newWindowIndex < newWindows.Count)
                {
                    firstPair.RightController.WindowHandle = newWindows[newWindowIndex];
                    newWindowIndex++;
                }
            }

            // Force update all border positions after assignment
            // This ensures borders are correctly positioned even if windows haven't been moved yet
            // Process window messages to allow window assignment to complete
            System.Windows.Forms.Application.DoEvents();
            System.Threading.Thread.Sleep(10); // Small delay to ensure windows are ready
            System.Windows.Forms.Application.DoEvents();
            foreach (var controller in AllControllersWithWindows)
            {
                controller.UpdateBorderPosition();
            }

            // Automatically set to mirror mode (unless mode lock prevents mode changes)
            if (!_modeLockEngaged)
                CurrentMode = MulticontrollerMode.MirrorAll;
        }

        /// <summary>
        /// Toggle minimize/restore for all Toontown windows that are not connected to the multicontroller.
        /// Uses the same executable list as auto-find. Minimized windows are restored; others are minimized.
        /// </summary>
        public void ToggleMinimizeUnconnectedWindows()
        {
            var executableNames = Properties.Settings.Default.autoFindExecutables
                .Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(e => e.Trim())
                .Where(e => !string.IsNullOrEmpty(e))
                .ToList();

            if (executableNames.Count == 0)
                return;

            var connectedHandles = new HashSet<IntPtr>(
                AllControllersWithWindows.Select(c => c.WindowHandle).Where(h => h != IntPtr.Zero)
            );

            var processNames = new HashSet<string>(executableNames, StringComparer.OrdinalIgnoreCase);
            var processNamesNoExt = new HashSet<string>(
                executableNames.Select(e => System.IO.Path.GetFileNameWithoutExtension(e)),
                StringComparer.OrdinalIgnoreCase
            );

            foreach (var process in Process.GetProcesses())
            {
                try
                {
                    bool matches = processNames.Contains(process.ProcessName) ||
                                   processNamesNoExt.Contains(process.ProcessName);
                    if (!matches)
                        continue;

                    IntPtr mainWindowHandle = process.MainWindowHandle;
                    if (mainWindowHandle == IntPtr.Zero)
                    {
                        Win32.EnumWindows((hWnd, lParam) =>
                        {
                            uint processId;
                            Win32.GetWindowThreadProcessId(hWnd, out processId);
                            if (processId == process.Id && Win32.IsWindowVisible(hWnd))
                            {
                                mainWindowHandle = hWnd;
                                return false;
                            }
                            return true;
                        }, IntPtr.Zero);
                    }

                    if (mainWindowHandle == IntPtr.Zero || !Win32.IsWindow(mainWindowHandle))
                        continue;
                    if (connectedHandles.Contains(mainWindowHandle))
                        continue;

                    var showState = Win32.GetWindowShowState(mainWindowHandle);
                    if (showState == Win32.ShowWindowCommands.ShowMinimized)
                        Win32.ShowWindow(mainWindowHandle, Win32.ShowWindowCommands.Restore);
                    else
                        Win32.ShowWindow(mainWindowHandle, Win32.ShowWindowCommands.Minimize);
                }
                catch
                {
                    // Process may have exited or we don't have access
                }
            }
        }

        /// <summary>
        /// Offset applied to X when auto-placing so the left edge aligns with snapped position (e.g. top-left quadrant starts at -7).
        /// </summary>
        private const int AutoPlacementPositionOffset = 7;
        /// <summary>
        /// Extra pixels added to width when auto-placing so adjacent windows meet and the rightmost reaches the monitor edge (frame/shadow gap).
        /// </summary>
        private const int AutoPlacementWidthOverlap = 14;
        /// <summary>
        /// Extra pixels added to height when auto-placing to compensate for bottom frame/shadow gap.
        /// </summary>
        private const int AutoPlacementHeightOverlap = 7;

        /// <summary>
        /// Per-window invisible-frame offsets so placed windows meet edge-to-edge. Uses the real DWM frame
        /// thickness (accurate per window, and DPI-correct — including automatically once Per-Monitor V2 is
        /// enabled) instead of the fixed 100%-DPI constants, falling back to the constants if the DWM query
        /// returns implausible values (e.g. a window still mid-restore) — WIN32-06.
        /// </summary>
        private static void GetPlacementFrameOffsets(IntPtr hwnd, out int xOffset, out int wOverlap, out int hOverlap)
        {
            xOffset = AutoPlacementPositionOffset;
            wOverlap = AutoPlacementWidthOverlap;
            hOverlap = AutoPlacementHeightOverlap;

            Win32.FrameThickness f = Win32.GetFrameThickness(hwnd);
            // Sane range for an invisible resize border across DPI scales (~7px @100% ... ~16px @200%).
            if (f.Left >= 0 && f.Left <= 40 && f.Right >= 0 && f.Right <= 40 && f.Bottom >= 0 && f.Bottom <= 40)
            {
                xOffset = f.Left;
                wOverlap = f.Left + f.Right;
                hOverlap = f.Bottom;
            }
        }

        /// <summary>
        /// Apply a layout preset: order controllers by layout priority, then assign slot rects and minimized state.
        /// Extra windows (beyond slot count) are left unchanged. Extra slots (beyond window count) are ignored.
        /// When onlyControllers is non-null, only those controllers are moved (e.g. after a swap so only the swapped windows are repositioned).
        /// </summary>
        public void ApplyLayoutPreset(LayoutPreset preset, IReadOnlyCollection<ToontownController> onlyControllers = null)
        {
            if (preset == null) return;
            var controllers = AllControllersWithWindows.ToList();
            if (controllers.Count == 0) return;

            bool leftsFirst = Properties.Settings.Default.layoutPriorityLeftsFirst;
            var ordered = leftsFirst
                ? controllers.OrderBy(c => c.GroupNumber).ThenBy(c => c.Type).ThenBy(c => c.PairNumber).ToList()
                : controllers.OrderBy(c => c.GroupNumber).ThenBy(c => c.PairNumber).ThenBy(c => c.Type).ToList();

            var slots = LayoutPresetBuilder.BuildSlots(preset);
            int applyCount = Math.Min(slots.Count, ordered.Count);

            // Build list of windows to place. If onlyControllers is set, only include those (e.g. just the 2 swapped windows).
            var toMove = new List<(ToontownController controller, SlotApplyInfo info)>();
            var toMinimizeAfter = new List<ToontownController>();
            for (int i = 0; i < applyCount; i++)
            {
                var controller = ordered[i];
                if (onlyControllers != null && onlyControllers.Count > 0 && !onlyControllers.Contains(controller))
                    continue;
                var info = slots[i];
                toMove.Add((controller, info));
                if (info.Minimized)
                    toMinimizeAfter.Add(controller);
            }

            if (toMove.Count == 0)
            {
                System.Windows.Forms.Application.DoEvents();
                return;
            }

            // Unminimizing: restore first so we can position.
            foreach (var (controller, info) in toMove)
            {
                if (Win32.GetWindowShowState(controller.WindowHandle) == Win32.ShowWindowCommands.ShowMinimized)
                    Win32.ShowWindow(controller.WindowHandle, Win32.ShowWindowCommands.Restore);
            }

            // Place all windows (position/size). Frame offsets are computed per window (WIN32-06).
            IntPtr hdwp = Win32.BeginDeferWindowPos(toMove.Count);
            if (hdwp != IntPtr.Zero)
            {
                foreach (var (controller, info) in toMove)
                {
                    GetPlacementFrameOffsets(controller.WindowHandle, out int xOffset, out int wOverlap, out int hOverlap);
                    hdwp = Win32.DeferWindowPos(hdwp, controller.WindowHandle, IntPtr.Zero,
                        info.Rect.X - xOffset, info.Rect.Y, info.Rect.Width + wOverlap, info.Rect.Height + hOverlap,
                        Win32.SetWindowPosFlags.ShowWindow | Win32.SetWindowPosFlags.DoNotActivate);
                    SetWindowLayoutAttributes(controller.WindowHandle);
                }
                Win32.EndDeferWindowPos(hdwp);
            }
            else
            {
                foreach (var (controller, info) in toMove)
                {
                    GetPlacementFrameOffsets(controller.WindowHandle, out int xOffset, out int wOverlap, out int hOverlap);
                    Win32.SetWindowPos(controller.WindowHandle, IntPtr.Zero,
                        info.Rect.X - xOffset, info.Rect.Y, info.Rect.Width + wOverlap, info.Rect.Height + hOverlap,
                        Win32.SetWindowPosFlags.ShowWindow | Win32.SetWindowPosFlags.DoNotActivate);
                    SetWindowLayoutAttributes(controller.WindowHandle);
                }
            }

            // Minimizing: do after placement so windows are positioned first, then minimized.
            foreach (var controller in toMinimizeAfter)
                Win32.ShowWindow(controller.WindowHandle, Win32.ShowWindowCommands.ShowMinimized);

            System.Windows.Forms.Application.DoEvents();
            foreach (var (c, _) in toMove)
                c.UpdateBorderPosition();
        }

        private static void SetWindowLayoutAttributes(IntPtr hWnd)
        {
            Win32.SetWindowAttribute(hWnd, Win32.WindowAttributeTypes.RoundedEdges, Win32.WindowAttributeValues.DWMWCP_DONOTROUND);
            Win32.SetWindowAttribute(hWnd, Win32.WindowAttributeTypes.DropShadow, Win32.WindowAttributeValues.DWMWA_NCRENDERING_POLICY);
            Win32.SetWindowAttribute(hWnd, Win32.WindowAttributeTypes.WindowBorderColor, 0x000000);
        }

        /// <summary>
        /// Install global low-level mouse hook to block clicks during switching mode
        /// </summary>
        private void InstallMouseHook()
        {
            if (_mouseHookHandle != IntPtr.Zero)
                return; // Hook already installed
            
            _hookInstance = this;
            if (_mouseHookProc == null)
            {
                _mouseHookProc = MouseHookProc;
            }
            
            IntPtr hModule = Win32.GetModuleHandle(null);
            _mouseHookHandle = Win32.SetWindowsHookEx(
                Win32.WH_MOUSE_LL,
                _mouseHookProc,
                hModule,
                0
            );
        }
        
        /// <summary>
        /// Uninstall global low-level mouse hook
        /// </summary>
        private void UninstallMouseHook()
        {
            if (_mouseHookHandle != IntPtr.Zero)
            {
                Win32.UnhookWindowsHookEx(_mouseHookHandle);
                _mouseHookHandle = IntPtr.Zero;
            }
            _hookInstance = null;
        }

        /// <summary>
        /// Ensures switching-mode <see cref="WH_MOUSE_LL"/> hook is removed on shutdown so Windows does not
        /// keep routing mouse input through a dying process (fixes cursor lag / jitter after exit).
        /// </summary>
        internal void ShutdownUninstallSwitchingMouseHook()
        {
            UninstallMouseHook();
        }
        
        /// <summary>
        /// Handle a switching-mode selection click on the UI thread.  Deferred out of the low-level mouse hook
        /// callback (CORR-02 / WIN32-01).  Re-checks switching mode because Alt may have been released between the
        /// click and this continuation running.
        /// </summary>
        private void HandleSwitchingModeClick(Point clickPoint)
        {
            if (!_switchingMode)
                return;

            var controllerUnderCursor = GetControllerAtPoint(clickPoint);
            if (controllerUnderCursor == null)
                return;

            // If clicking on a window marked for removal, unmark it first
            if (_markedForRemoval.Contains(controllerUnderCursor))
                _markedForRemoval.Remove(controllerUnderCursor);

            if (_firstSelectedController == null)
            {
                // Select first window
                _firstSelectedController = controllerUnderCursor;
            }
            else if (_secondSelectedController == null && controllerUnderCursor != _firstSelectedController)
            {
                // Select second window and switch
                _secondSelectedController = controllerUnderCursor;
                SwitchWindows(_firstSelectedController, _secondSelectedController);

                // Reset selection state but keep switching mode active (Alt is still held)
                _firstSelectedController = null;
                _secondSelectedController = null;
            }
            else if (controllerUnderCursor == _firstSelectedController)
            {
                // Clicking the same window again deselects it
                _firstSelectedController = null;
            }

            UpdateSwitchingModeDisplay();
        }

        /// <summary>
        /// Low-level mouse hook procedure - blocks clicks during switching mode
        /// </summary>
        private static IntPtr MouseHookProc(int nCode, IntPtr wParam, IntPtr lParam)
        {
            // If nCode is less than zero, we must pass the message to CallNextHookEx
            if (nCode < 0)
            {
                return Win32.CallNextHookEx(_mouseHookHandle, nCode, wParam, lParam);
            }
            
            // Check if switching mode is active
            if (_hookInstance != null && _hookInstance._switchingMode)
            {
                int msg = (int)wParam.ToInt64();
                
                // Block mouse clicks (left, right, middle button down)
                int switchKeyCode = Properties.Settings.Default.switchingModeSwitchKeyCode;
                bool isMatchingMouseButton = (msg == (int)Win32.WM.LBUTTONDOWN && switchKeyCode == 1) ||
                                            (msg == (int)Win32.WM.RBUTTONDOWN && switchKeyCode == 2) ||
                                            (msg == (int)Win32.WM.MBUTTONDOWN && switchKeyCode == 4);
                
                if (msg == (int)Win32.WM.LBUTTONDOWN ||
                    msg == (int)Win32.WM.RBUTTONDOWN ||
                    msg == (int)Win32.WM.MBUTTONDOWN)
                {
                    // Process matching mouse button clicks for selection/switching
                    if (isMatchingMouseButton)
                    {
                        // Capture the click point and defer all selection/switch work to the UI thread.  The
                        // work touches WinForms and does DWM/border/window-swap operations; running it inside this
                        // low-level mouse hook callback stalled system-wide mouse input and re-entered the hook via
                        // DoEvents (CORR-02 / WIN32-01).  BeginInvoke posts it to run after this callback returns.
                        Win32.MSLLHOOKSTRUCT hookStruct = (Win32.MSLLHOOKSTRUCT)System.Runtime.InteropServices.Marshal.PtrToStructure(
                            lParam, typeof(Win32.MSLLHOOKSTRUCT));
                        Point clickPoint = hookStruct.pt;

                        Multicontroller instance = _hookInstance;
                        var sync = WindowWatcher.Instance.SynchronizingObject;
                        if (instance != null && sync != null)
                            sync.BeginInvoke(new Action(() => instance.HandleSwitchingModeClick(clickPoint)), null);
                    }

                    // Block the click from reaching the game window
                    return (IntPtr)1; // Return non-zero to block the message
                }
            }
            
            // Pass the message to the next hook
            return Win32.CallNextHookEx(_mouseHookHandle, nCode, wParam, lParam);
        }
    }

    
}
