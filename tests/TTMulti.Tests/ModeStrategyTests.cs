using TTMulti;
using Xunit;

namespace TTMulti.Tests
{
    /// <summary>
    /// Pins which controllers each mode forwards to (the active set), per the behavior contract: Group targets
    /// the current group; AllGroup/MirrorAll/Focused/Custom target every controller; the vestigial modes
    /// (Pair/MirrorGroup/MirrorIndividual) target none.
    /// </summary>
    /// <remarks>
    /// The enums are internal (visible here via InternalsVisibleTo) so they cannot appear in a public xUnit
    /// signature; InlineData passes their int values and the body casts back.
    /// </remarks>
    public class ModeStrategyTests
    {
        [Theory]
        [InlineData((int)MulticontrollerMode.Group, (int)ActiveControllerSet.CurrentGroup)]
        [InlineData((int)MulticontrollerMode.AllGroup, (int)ActiveControllerSet.AllControllers)]
        [InlineData((int)MulticontrollerMode.MirrorAll, (int)ActiveControllerSet.AllControllers)]
        [InlineData((int)MulticontrollerMode.Focused, (int)ActiveControllerSet.AllControllers)]
        [InlineData((int)MulticontrollerMode.Custom, (int)ActiveControllerSet.AllControllers)]
        [InlineData((int)MulticontrollerMode.Pair, (int)ActiveControllerSet.None)]
        [InlineData((int)MulticontrollerMode.MirrorGroup, (int)ActiveControllerSet.None)]
        [InlineData((int)MulticontrollerMode.MirrorIndividual, (int)ActiveControllerSet.None)]
        public void SelectFor_maps_mode_to_active_set(int mode, int expected)
        {
            Assert.Equal((ActiveControllerSet)expected, ModeStrategy.SelectFor((MulticontrollerMode)mode));
        }
    }
}
