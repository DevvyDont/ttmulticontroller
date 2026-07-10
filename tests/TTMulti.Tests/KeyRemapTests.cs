using System.Collections.Generic;
using System.Windows.Forms;
using TTMulti;
using Xunit;

namespace TTMulti.Tests
{
    /// <summary>
    /// Pins the Group/AllGroup key-remap table construction: physical toon keys map to the game keys posted when
    /// pressed, including the historical edge behavior around None keys, empty entries, and accumulation.
    /// </summary>
    public class KeyRemapTests
    {
        private static KeyMapping KM(Keys key, Keys left, Keys right)
            => new KeyMapping("t", key, left, right, false);

        [Fact]
        public void Maps_toon_keys_to_posted_game_keys()
        {
            // "Forward": pressing W (left toon) or Up (right toon) posts Up in the game.
            var bindings = new List<KeyMapping> { KM(Keys.Up, Keys.W, Keys.Up) };
            KeyRemap.Build(bindings, out var left, out var right);

            Assert.Equal(new[] { Keys.Up }, left[Keys.W]);
            Assert.Equal(new[] { Keys.Up }, right[Keys.Up]);
        }

        [Fact]
        public void Multiple_bindings_on_same_toon_key_accumulate()
        {
            var bindings = new List<KeyMapping>
            {
                KM(Keys.A, Keys.W, Keys.NumPad1),
                KM(Keys.B, Keys.W, Keys.NumPad2),
            };
            KeyRemap.Build(bindings, out var left, out _);

            Assert.Equal(new[] { Keys.A, Keys.B }, left[Keys.W]);
        }

        [Fact]
        public void Toon_key_gets_an_entry_even_when_game_key_is_None()
        {
            // Key == None: the toon key still gets a dictionary entry, but it stays empty.
            var bindings = new List<KeyMapping> { KM(Keys.None, Keys.W, Keys.Up) };
            KeyRemap.Build(bindings, out var left, out var right);

            Assert.True(left.ContainsKey(Keys.W));
            Assert.Empty(left[Keys.W]);
            Assert.True(right.ContainsKey(Keys.Up));
            Assert.Empty(right[Keys.Up]);
        }

        [Fact]
        public void None_toon_key_creates_entry_but_receives_no_game_key()
        {
            var bindings = new List<KeyMapping> { KM(Keys.Up, Keys.None, Keys.Down) };
            KeyRemap.Build(bindings, out var left, out var right);

            Assert.True(left.ContainsKey(Keys.None));
            Assert.Empty(left[Keys.None]);          // None left toon key: no game key appended
            Assert.Equal(new[] { Keys.Up }, right[Keys.Down]); // right side still maps
        }

        [Fact]
        public void Null_bindings_produce_empty_tables()
        {
            KeyRemap.Build(null, out var left, out var right);
            Assert.Empty(left);
            Assert.Empty(right);
        }

        [Theory]
        [InlineData(Keys.LControlKey, Keys.ControlKey)]
        [InlineData(Keys.RControlKey, Keys.ControlKey)]
        [InlineData(Keys.LShiftKey, Keys.ShiftKey)]
        [InlineData(Keys.RShiftKey, Keys.ShiftKey)]
        [InlineData(Keys.LMenu, Keys.Menu)]
        [InlineData(Keys.RMenu, Keys.Menu)]
        [InlineData(Keys.W, Keys.W)]                 // non-modifier unchanged
        [InlineData(Keys.ControlKey, Keys.ControlKey)]
        public void NormalizeModifier_folds_side_specific_modifiers(Keys input, Keys expected)
        {
            Assert.Equal(expected, KeyRemap.NormalizeModifier(input));
        }

        [Fact]
        public void Side_specific_modifier_toon_key_is_matchable_by_the_generic_key()
        {
            // A right-toon jump bound to the right Ctrl key must fire when a Ctrl press (generic VK_CONTROL) arrives.
            var bindings = new List<KeyMapping> { KM(Keys.Up, Keys.None, Keys.RControlKey) };
            KeyRemap.Build(bindings, out _, out var right);

            Assert.True(right.ContainsKey(Keys.ControlKey));
            Assert.Equal(new[] { Keys.Up }, right[Keys.ControlKey]);
            Assert.False(right.ContainsKey(Keys.RControlKey));
        }
    }
}
