using System.Collections.Generic;
using System.Windows.Forms;
using TTMulti;
using Xunit;

namespace TTMulti.Tests
{
    /// <summary>
    /// Pins the directional-key classification used by Focused mode (directional keys go only to the focused
    /// window; everything else mirrors to all). A key counts as directional when it is bound to a mapping whose
    /// title is Forward/Left/Backward/Right (case-insensitive), matching the shipped behavior.
    /// </summary>
    public class DirectionalKeyTests
    {
        private static List<KeyMapping> Bindings() => new List<KeyMapping>
        {
            new KeyMapping("Forward",  Keys.W, Keys.W, Keys.Up,    false),
            new KeyMapping("Left",     Keys.A, Keys.A, Keys.Left,  false),
            new KeyMapping("Backward", Keys.S, Keys.S, Keys.Down,  false),
            new KeyMapping("Right",    Keys.D, Keys.D, Keys.Right, false),
            new KeyMapping("Throw",    Keys.Delete, Keys.Delete, Keys.Delete, false),
            new KeyMapping("Jump",     Keys.ControlKey, Keys.ControlKey, Keys.ControlKey, false),
        };

        [Theory]
        [InlineData(Keys.W)]
        [InlineData(Keys.A)]
        [InlineData(Keys.S)]
        [InlineData(Keys.D)]
        public void Movement_keys_are_directional(Keys key)
        {
            Assert.True(Multicontroller.IsDirectionalKey(Bindings(), key));
        }

        [Theory]
        [InlineData(Keys.Delete)]   // Throw
        [InlineData(Keys.ControlKey)] // Jump
        [InlineData(Keys.Escape)]   // unbound
        public void Non_movement_keys_are_not_directional(Keys key)
        {
            Assert.False(Multicontroller.IsDirectionalKey(Bindings(), key));
        }

        [Fact]
        public void Title_match_is_case_insensitive()
        {
            var bindings = new List<KeyMapping> { new KeyMapping("FORWARD", Keys.W, Keys.W, Keys.W, false) };
            Assert.True(Multicontroller.IsDirectionalKey(bindings, Keys.W));
        }

        [Fact]
        public void Null_bindings_return_false()
        {
            Assert.False(Multicontroller.IsDirectionalKey(null, Keys.W));
        }
    }
}
