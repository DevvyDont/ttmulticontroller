using System.Collections.Generic;
using System.Linq;
using System.Windows.Forms;
using TTMulti;
using Xunit;

namespace TTMulti.Tests
{
    /// <summary>
    /// Pins the forwarded-key ledger that guarantees a KEYUP (or a physical-state reconcile) releases exactly what
    /// the matching KEYDOWN forwarded, regardless of how routing moved while the key was held. Controllers are
    /// modelled as strings so the pure logic can be tested without constructing a real ToontownController.
    /// </summary>
    public class ForwardedKeyLedgerTests
    {
        private static ForwardedKeyLedger<string> New() => new ForwardedKeyLedger<string>();

        [Fact]
        public void TakeUp_returns_exactly_what_was_recorded_then_clears_it()
        {
            var led = New();
            led.RecordDown(Keys.W, new[] { ("cL", Keys.Up), ("cR", Keys.Up) });

            var outs = led.TakeUp(Keys.W);

            Assert.Equal(2, outs.Count);
            Assert.Contains(("cL", Keys.Up), outs);
            Assert.Contains(("cR", Keys.Up), outs);
            Assert.True(led.IsEmpty);                 // entry removed
            Assert.Empty(led.TakeUp(Keys.W));         // second up releases nothing
        }

        [Fact]
        public void Autorepeat_down_does_not_duplicate_outputs()
        {
            var led = New();
            led.RecordDown(Keys.W, new[] { ("cL", Keys.Up) });
            led.RecordDown(Keys.W, new[] { ("cL", Keys.Up) });   // autorepeat re-posts the same DOWN

            var outs = led.TakeUp(Keys.W);
            Assert.Single(outs);
            Assert.Equal(("cL", Keys.Up), outs[0]);
        }

        [Fact]
        public void Down_accumulates_new_targets_across_a_routing_change()
        {
            var led = New();
            led.RecordDown(Keys.W, new[] { ("group1", Keys.Up) });     // held while group 1 active
            led.RecordDown(Keys.W, new[] { ("group2", Keys.Up) });     // autorepeat after switching to group 2

            var outs = led.TakeUp(Keys.W);                             // release both on physical up
            Assert.Equal(2, outs.Count);
            Assert.Contains(("group1", Keys.Up), outs);
            Assert.Contains(("group2", Keys.Up), outs);
        }

        [Fact]
        public void Reconcile_releases_only_triggers_that_are_physically_up_and_checks_the_physical_key()
        {
            var led = New();
            // Remap: physical W -> posted Up (posted key differs from the physical trigger).
            led.RecordDown(Keys.W, new[] { ("cL", Keys.Up) });
            led.RecordDown(Keys.S, new[] { ("cL", Keys.Down) });

            var down = new HashSet<Keys> { Keys.S };                  // S still physically held, W released
            var released = led.Reconcile(k => down.Contains(k));

            Assert.Single(released);
            Assert.Equal(("cL", Keys.Up), released[0]);               // W's posted key, chosen by the physical key
            Assert.Empty(led.TakeUp(Keys.W));                        // W dropped
            Assert.Single(led.TakeUp(Keys.S));                       // S kept
        }

        [Fact]
        public void RemoveController_drops_only_that_controllers_outputs()
        {
            var led = New();
            led.RecordDown(Keys.W, new[] { ("cL", Keys.Up), ("cR", Keys.Up) });

            led.RemoveController("cL");

            var outs = led.TakeUp(Keys.W);
            Assert.Single(outs);
            Assert.Equal(("cR", Keys.Up), outs[0]);
        }

        [Fact]
        public void RemoveController_removes_the_trigger_entirely_when_it_was_its_last_output()
        {
            var led = New();
            led.RecordDown(Keys.W, new[] { ("cL", Keys.Up) });

            led.RemoveController("cL");

            Assert.True(led.IsEmpty);
        }

        [Fact]
        public void Clear_forgets_everything()
        {
            var led = New();
            led.RecordDown(Keys.W, new[] { ("cL", Keys.Up) });
            led.RecordDown(Keys.S, new[] { ("cR", Keys.Down) });

            led.Clear();

            Assert.True(led.IsEmpty);
        }

        [Fact]
        public void Empty_and_none_key_inputs_are_ignored()
        {
            var led = New();
            led.RecordDown(Keys.None, new[] { ("cL", Keys.Up) });     // no physical key
            led.RecordDown(Keys.W, new (string, Keys)[0]);            // nothing forwarded
            led.RecordDown(Keys.W, new[] { ("cL", Keys.None) });      // no posted key

            Assert.True(led.IsEmpty);
        }
    }
}
