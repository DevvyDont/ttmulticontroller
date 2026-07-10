using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Forms;

namespace TTMulti
{
    /// <summary>
    /// Records, per physical trigger key, exactly which (controller, posted key) pairs a KEYDOWN forwarded to
    /// the game windows, so the matching KEYUP releases exactly those pairs regardless of how the routing (mode,
    /// active group, focus, minimize state) changed while the key was held. This is the standard-mode counterpart
    /// to the per-trigger bookkeeping Custom mode already does (CustomModeInputRouter._heldTriggers), and it is
    /// what guarantees a physically released key is never left stuck on a window whose routing has since moved.
    ///
    /// Generic on the controller type purely so the logic can be unit-tested without constructing a real
    /// ToontownController (which owns WinForms windows). Reference equality identifies a controller.
    /// </summary>
    /// <remarks>All access is expected on the UI thread; the internal lock is a cheap safety net, mirroring
    /// ToontownController._heldKeys.</remarks>
    internal class ForwardedKeyLedger<TController> where TController : class
    {
        private readonly Dictionary<Keys, HashSet<(TController Controller, Keys PostedKey)>> _map
            = new Dictionary<Keys, HashSet<(TController, Keys)>>();
        private readonly object _gate = new object();

        /// <summary>
        /// Record the outputs a KEYDOWN of <paramref name="physical"/> forwarded. Autorepeat re-posts the same
        /// DOWN, and a key held across a routing change forwards to new targets on later repeats, so outputs are
        /// unioned rather than replaced. A KEYUP then releases everything the key ever pressed that is still down.
        /// </summary>
        public void RecordDown(Keys physical, IEnumerable<(TController Controller, Keys PostedKey)> outputs)
        {
            if (physical == Keys.None || outputs == null)
                return;

            lock (_gate)
            {
                if (!_map.TryGetValue(physical, out var set))
                {
                    set = new HashSet<(TController, Keys)>();
                    _map[physical] = set;
                }

                foreach (var o in outputs)
                {
                    if (o.Controller != null && o.PostedKey != Keys.None)
                        set.Add(o);
                }

                if (set.Count == 0)
                    _map.Remove(physical);
            }
        }

        /// <summary>Remove and return the outputs recorded for <paramref name="physical"/> (empty if none).</summary>
        public IReadOnlyList<(TController Controller, Keys PostedKey)> TakeUp(Keys physical)
        {
            lock (_gate)
            {
                if (_map.TryGetValue(physical, out var set))
                {
                    _map.Remove(physical);
                    return set.ToList();
                }
            }
            return Array.Empty<(TController, Keys)>();
        }

        /// <summary>
        /// Remove and return the outputs of every physical trigger that is no longer physically down. Used to
        /// recover keys whose KEYUP was never delivered (for example, swallowed by a window-drag modal loop),
        /// checking the physical trigger and not the posted/remapped key so it stays correct under remapping.
        /// </summary>
        public IReadOnlyList<(TController Controller, Keys PostedKey)> Reconcile(Func<Keys, bool> isPhysicallyDown)
        {
            if (isPhysicallyDown == null)
                return Array.Empty<(TController, Keys)>();

            var released = new List<(TController, Keys)>();
            lock (_gate)
            {
                Keys[] triggers = _map.Keys.ToArray();
                foreach (Keys trigger in triggers)
                {
                    if (isPhysicallyDown(trigger))
                        continue;
                    released.AddRange(_map[trigger]);
                    _map.Remove(trigger);
                }
            }
            return released;
        }

        /// <summary>Drop every recorded output that targets <paramref name="controller"/> (used when that
        /// controller's keys are flushed by a release-on-deactivate path, or it disconnects).</summary>
        public void RemoveController(TController controller)
        {
            if (controller == null)
                return;

            lock (_gate)
            {
                Keys[] triggers = _map.Keys.ToArray();
                foreach (Keys trigger in triggers)
                {
                    var set = _map[trigger];
                    set.RemoveWhere(o => ReferenceEquals(o.Controller, controller));
                    if (set.Count == 0)
                        _map.Remove(trigger);
                }
            }
        }

        /// <summary>Forget everything (used after a full flush of all held forwarded keys).</summary>
        public void Clear()
        {
            lock (_gate)
                _map.Clear();
        }

        /// <summary>True when nothing is recorded (test/diagnostic helper).</summary>
        public bool IsEmpty
        {
            get { lock (_gate) return _map.Count == 0; }
        }
    }
}
