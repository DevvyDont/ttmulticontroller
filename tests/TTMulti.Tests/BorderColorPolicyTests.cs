using TTMulti;
using Xunit;

namespace TTMulti.Tests
{
    /// <summary>
    /// Pins the active-mode -> border-colour-source mapping extracted from ToontownController.Refresh so the
    /// dedup of the two (formerly duplicated) colour-selection branches cannot silently change which colour a
    /// controller's border uses. Group/AllGroup pick Left/Right by controller type; MirrorAll uses All; Custom
    /// uses Custom; Focused splits on the focused flag; every other mode keeps the current colour.
    /// </summary>
    /// <remarks>
    /// The enums are internal (visible here via InternalsVisibleTo) so they cannot appear in a public xUnit
    /// signature; InlineData passes their int values and the body casts back.
    /// </remarks>
    public class BorderColorPolicyTests
    {
        [Theory]
        // Group -> Left/Right by controller type (focused flag is irrelevant here)
        [InlineData((int)MulticontrollerMode.Group, (int)ControllerType.Left, false, (int)BorderColorSource.Left)]
        [InlineData((int)MulticontrollerMode.Group, (int)ControllerType.Right, false, (int)BorderColorSource.Right)]
        [InlineData((int)MulticontrollerMode.Group, (int)ControllerType.Left, true, (int)BorderColorSource.Left)]
        // AllGroup behaves like Group
        [InlineData((int)MulticontrollerMode.AllGroup, (int)ControllerType.Left, false, (int)BorderColorSource.Left)]
        [InlineData((int)MulticontrollerMode.AllGroup, (int)ControllerType.Right, false, (int)BorderColorSource.Right)]
        // MirrorAll -> All regardless of type
        [InlineData((int)MulticontrollerMode.MirrorAll, (int)ControllerType.Left, false, (int)BorderColorSource.All)]
        [InlineData((int)MulticontrollerMode.MirrorAll, (int)ControllerType.Right, false, (int)BorderColorSource.All)]
        // Custom -> Custom regardless of type
        [InlineData((int)MulticontrollerMode.Custom, (int)ControllerType.Left, false, (int)BorderColorSource.Custom)]
        [InlineData((int)MulticontrollerMode.Custom, (int)ControllerType.Right, true, (int)BorderColorSource.Custom)]
        // Focused splits on the focused flag, not the type
        [InlineData((int)MulticontrollerMode.Focused, (int)ControllerType.Left, true, (int)BorderColorSource.Focused)]
        [InlineData((int)MulticontrollerMode.Focused, (int)ControllerType.Right, true, (int)BorderColorSource.Focused)]
        [InlineData((int)MulticontrollerMode.Focused, (int)ControllerType.Left, false, (int)BorderColorSource.Unfocused)]
        [InlineData((int)MulticontrollerMode.Focused, (int)ControllerType.Right, false, (int)BorderColorSource.Unfocused)]
        // Vestigial / unhandled modes keep the current colour
        [InlineData((int)MulticontrollerMode.Pair, (int)ControllerType.Left, false, (int)BorderColorSource.KeepCurrent)]
        [InlineData((int)MulticontrollerMode.MirrorGroup, (int)ControllerType.Right, true, (int)BorderColorSource.KeepCurrent)]
        [InlineData((int)MulticontrollerMode.MirrorIndividual, (int)ControllerType.Left, false, (int)BorderColorSource.KeepCurrent)]
        public void SourceFor_maps_mode_to_colour_source(int mode, int type, bool isFocused, int expected)
        {
            Assert.Equal(
                (BorderColorSource)expected,
                BorderColorPolicy.SourceFor((MulticontrollerMode)mode, (ControllerType)type, isFocused));
        }
    }
}
