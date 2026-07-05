using System.Collections.Generic;
using System.Windows.Forms;
using TTMulti.Controls;

namespace TTMulti
{
    /// <summary>
    /// Builds the per-side key-remap tables used by Group/AllGroup routing: a physical key a toon uses
    /// (LeftToonKey / RightToonKey) maps to the list of game keys (binding.Key) to post when it is pressed.
    /// Extracted from Multicontroller.UpdateOptions as pure logic so the remap can be characterization-tested.
    /// </summary>
    internal static class KeyRemap
    {
        /// <summary>
        /// Produces the left/right remap tables from <paramref name="bindings"/>. Preserves the historical edge
        /// behavior exactly: every LeftToonKey/RightToonKey gets a dictionary entry even when its binding has no
        /// game key (or the toon key is <see cref="Keys.None"/>), so the entry may be an empty list; a game key
        /// is appended only when both it and the toon key are non-None; and multiple bindings that share a toon
        /// key accumulate into the same list.
        /// </summary>
        internal static void Build(
            IReadOnlyList<KeyMapping> bindings,
            out Dictionary<Keys, List<Keys>> leftKeys,
            out Dictionary<Keys, List<Keys>> rightKeys)
        {
            leftKeys = new Dictionary<Keys, List<Keys>>();
            rightKeys = new Dictionary<Keys, List<Keys>>();
            if (bindings == null)
                return;

            foreach (KeyMapping binding in bindings)
            {
                if (!leftKeys.ContainsKey(binding.LeftToonKey))
                    leftKeys.Add(binding.LeftToonKey, new List<Keys>());

                if (!rightKeys.ContainsKey(binding.RightToonKey))
                    rightKeys.Add(binding.RightToonKey, new List<Keys>());

                if (binding.Key != Keys.None && binding.LeftToonKey != Keys.None)
                    leftKeys[binding.LeftToonKey].Add(binding.Key);

                if (binding.Key != Keys.None && binding.RightToonKey != Keys.None)
                    rightKeys[binding.RightToonKey].Add(binding.Key);
            }
        }
    }
}
