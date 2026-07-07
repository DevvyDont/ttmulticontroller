namespace TTMulti
{
    /// <summary>Which settings-backed colour a controller's border uses in a given active, non-switching mode.</summary>
    internal enum BorderColorSource
    {
        Left,
        Right,
        All,
        Custom,
        Focused,
        Unfocused,
        KeepCurrent,
    }

    /// <summary>
    /// Pure mapping of the active controller mode to the border colour source, extracted from the (previously
    /// duplicated) per-mode switch in <see cref="ToontownController.Refresh"/> so it can be unit-tested. Only the
    /// active, non-switching, non-show-all-borders case lives here; switching-mode and the show-all-borders /
    /// inactive shortcuts stay in the controller because they read live border and multicontroller state.
    /// </summary>
    internal static class BorderColorPolicy
    {
        internal static BorderColorSource SourceFor(MulticontrollerMode mode, ControllerType type, bool isFocused)
        {
            switch (mode)
            {
                case MulticontrollerMode.Group:
                case MulticontrollerMode.AllGroup:
                    return type == ControllerType.Left ? BorderColorSource.Left : BorderColorSource.Right;
                case MulticontrollerMode.MirrorAll:
                    return BorderColorSource.All;
                case MulticontrollerMode.Custom:
                    return BorderColorSource.Custom;
                case MulticontrollerMode.Focused:
                    return isFocused ? BorderColorSource.Focused : BorderColorSource.Unfocused;
                default:
                    return BorderColorSource.KeepCurrent;
            }
        }
    }
}
