namespace TTMulti.Input
{
    /// <summary>
    /// RegisterHotKey ID assignments. These IDs are part of the app's observable behavior (BEHAVIOR.md
    /// "Hotkeys &amp; triggers") — WM_HOTKEY routing branches on them, so keep values stable.
    /// </summary>
    internal static class HotkeyIds
    {
        internal const int Mode = 0;
        internal const int InstantMultiClick = 1;
        internal const int ZeroPowerThrow = 2;
        internal const int ModeLockToggle = 3;
        internal const int SuspendGlobalsToggle = 4;
        internal const int AutoFind = 7;
        internal const int MinimizeUnconnected = 9;
        internal const int LayoutPresetStart = 10;
        internal const int LayoutPresetEnd = 25;
        internal const int CustomModeActivationStart = 26;
        internal const int CustomModeActivationEnd = 57;
    }
}
