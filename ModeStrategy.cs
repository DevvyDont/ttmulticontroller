namespace TTMulti
{
    /// <summary>Which controllers a mode forwards input to.</summary>
    internal enum ActiveControllerSet
    {
        /// <summary>No controllers (vestigial/unreachable modes: Pair, MirrorGroup, MirrorIndividual).</summary>
        None,
        /// <summary>Only the controllers of the current group (Group mode).</summary>
        CurrentGroup,
        /// <summary>Every controller across all groups (AllGroup, MirrorAll, Focused, Custom).</summary>
        AllControllers,
    }

    /// <summary>
    /// Pure per-mode routing decisions. Keeping the mode → active-set mapping in one place lets
    /// <see cref="Multicontroller.ActiveControllers"/> and <see cref="Multicontroller.IsActiveController"/>
    /// derive from the same source of truth (they must always agree) and makes the mapping characterization-testable.
    /// </summary>
    internal static class ModeStrategy
    {
        internal static ActiveControllerSet SelectFor(MulticontrollerMode mode)
        {
            switch (mode)
            {
                case MulticontrollerMode.Group:
                    return ActiveControllerSet.CurrentGroup;
                case MulticontrollerMode.AllGroup:
                case MulticontrollerMode.MirrorAll:
                case MulticontrollerMode.Focused:
                case MulticontrollerMode.Custom:
                    return ActiveControllerSet.AllControllers;
                default:
                    return ActiveControllerSet.None;
            }
        }
    }
}
