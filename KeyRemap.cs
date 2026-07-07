using System.Collections.Generic;
using System.Windows.Forms;

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
        /// Folds the left/right-specific modifier virtual-keys (VK_LSHIFT/RSHIFT, VK_LCONTROL/RCONTROL,
        /// VK_LMENU/RMENU) to their generic code (VK_SHIFT/CONTROL/MENU). WM_KEYDOWN/UP carry the generic code for
        /// a modifier press, so a toon key stored as a side-specific modifier (which the key picker used to
        /// capture) would never match a real Ctrl/Shift/Alt press. Non-modifier keys are returned unchanged.
        /// </summary>
        internal static Keys NormalizeModifier(Keys key)
        {
            switch (key)
            {
                case Keys.LShiftKey:
                case Keys.RShiftKey:
                    return Keys.ShiftKey;
                case Keys.LControlKey:
                case Keys.RControlKey:
                    return Keys.ControlKey;
                case Keys.LMenu:
                case Keys.RMenu:
                    return Keys.Menu;
                default:
                    return key;
            }
        }

        /// <summary>
        /// Produces the left/right remap tables from <paramref name="bindings"/>. Preserves the historical edge
        /// behavior exactly: every LeftToonKey/RightToonKey gets a dictionary entry even when its binding has no
        /// game key (or the toon key is <see cref="Keys.None"/>), so the entry may be an empty list; a game key
        /// is appended only when both it and the toon key are non-None; and multiple bindings that share a toon
        /// key accumulate into the same list. Side-specific modifier toon keys are folded to their generic code
        /// (see <see cref="NormalizeModifier"/>) so Ctrl/Shift/Alt binds match the generic key a press carries.
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
                Keys leftToonKey = NormalizeModifier(binding.LeftToonKey);
                Keys rightToonKey = NormalizeModifier(binding.RightToonKey);

                if (!leftKeys.ContainsKey(leftToonKey))
                    leftKeys.Add(leftToonKey, new List<Keys>());

                if (!rightKeys.ContainsKey(rightToonKey))
                    rightKeys.Add(rightToonKey, new List<Keys>());

                if (binding.Key != Keys.None && leftToonKey != Keys.None)
                    leftKeys[leftToonKey].Add(binding.Key);

                if (binding.Key != Keys.None && rightToonKey != Keys.None)
                    rightKeys[rightToonKey].Add(binding.Key);
            }
        }
    }
}
