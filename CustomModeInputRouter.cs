using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Forms;
using TTMulti.Controls;

namespace TTMulti
{
    /// <summary>
    /// Dispatches keyboard messages for <see cref="MulticontrollerMode.Custom"/> using a <see cref="CustomModeDefinition"/>.
    /// </summary>
    internal static class CustomModeInputRouter
    {
        /// <summary>
        /// Virtual key to post for <see cref="CustomModeBindingAction.SendRole"/>.
        /// In Multi-Mode Keys, <see cref="KeyMapping.Key"/> is the "Toontown Key" (what gets injected into the game);
        /// <see cref="KeyMapping.LeftToonKey"/> / <see cref="KeyMapping.RightToonKey"/> are the controller-side triggers
        /// (see <see cref="Multicontroller.UpdateOptions"/> leftKeys/rightKeys). Group mode posts <c>Key</c>, not the trigger columns.
        /// </summary>
        static Keys ResolveRoleVirtualKey(KeyMapping map, ToontownController target)
        {
            if (map.Key != Keys.None)
                return map.Key;

            Keys leftK = map.LeftToonKey;
            Keys rightK = map.RightToonKey;
            bool leftSet = leftK != Keys.None;
            bool rightSet = rightK != Keys.None;
            return target.Type == ControllerType.Left
                ? (leftSet ? leftK : rightK)
                : (rightSet ? rightK : leftK);
        }

        static List<ToontownController> ResolveTargets(CustomModeBinding b, List<ToontownController> ordered)
        {
            var result = new List<ToontownController>();
            if (ordered == null || ordered.Count == 0)
                return result;

            switch (b.TargetKind)
            {
                case CustomModeTargetKind.All:
                    foreach (ToontownController c in ordered)
                    {
                        if (c != null && c.HasWindow)
                            result.Add(c);
                    }
                    return result;
                case CustomModeTargetKind.Listed:
                    if (b.ListedTargetIndices == null || b.ListedTargetIndices.Count == 0)
                        return result;
                    foreach (int idx in b.ListedTargetIndices)
                    {
                        if (idx < 1 || idx > ordered.Count)
                            continue;
                        ToontownController t = ordered[idx - 1];
                        if (t != null && t.HasWindow)
                            result.Add(t);
                    }
                    return result;
                default:
                    if (b.TargetIndex < 1 || b.TargetIndex > ordered.Count)
                        return result;
                    ToontownController one = ordered[b.TargetIndex - 1];
                    if (one != null && one.HasWindow)
                        result.Add(one);
                    return result;
            }
        }

        public static bool TryProcess(Multicontroller multicontroller, CustomModeDefinition definition, Win32.WM msg, IntPtr wParam, IntPtr lParam)
        {
            if (definition?.Bindings == null || definition.Bindings.Count == 0)
                return false;

            Keys keysPressed = (Keys)wParam;
            if (msg == Win32.WM.HOTKEY)
                keysPressed = (Keys)(lParam.ToInt32() >> 16);

            bool isDown = msg == Win32.WM.KEYDOWN || msg == Win32.WM.HOTKEY || msg == Win32.WM.SYSKEYDOWN;
            bool isUp = msg == Win32.WM.KEYUP || msg == Win32.WM.SYSKEYUP;
            if (!isDown && !isUp)
                return false;

            Keys mod = Control.ModifierKeys;
            foreach (CustomModeBinding b in definition.Bindings)
            {
                if ((Keys)b.InputKey != keysPressed)
                    continue;
                if (b.RequireAlt && (mod & Keys.Alt) == Keys.None)
                    continue;
                if (b.RequireControl && (mod & Keys.Control) == Keys.None)
                    continue;
                if (b.RequireShift && (mod & Keys.Shift) == Keys.None)
                    continue;

                var list = multicontroller.GetControllersInCustomModeOrder();
                List<ToontownController> targets = ResolveTargets(b, list);
                if (targets.Count == 0)
                    return b.ConsumeInput;

                if (b.Action == CustomModeBindingAction.InstantClick)
                {
                    if (isDown)
                    {
                        foreach (ToontownController target in targets)
                            multicontroller.TriggerInstantMultiClickSingleTarget(target);
                    }
                    return b.ConsumeInput;
                }

                Win32.WM outMsg = isUp ? Win32.WM.KEYUP : Win32.WM.KEYDOWN;

                KeyMapping roleMap = null;
                if (b.Action == CustomModeBindingAction.SendRole)
                {
                    roleMap = Properties.SerializedSettings.Default.Bindings
                        .FirstOrDefault(x => string.Equals(x.Title, b.RoleTitle, StringComparison.OrdinalIgnoreCase));
                    if (roleMap == null)
                        return b.ConsumeInput;
                }

                foreach (ToontownController target in targets)
                {
                    Keys vk = Keys.None;
                    if (b.Action == CustomModeBindingAction.SendRawKey)
                        vk = (Keys)b.RawKey;
                    else if (b.Action == CustomModeBindingAction.SendRole)
                        vk = ResolveRoleVirtualKey(roleMap, target);

                    if (vk == Keys.None)
                        continue;

                    IntPtr keyLParam = Win32.MakePostedKeyLParam(vk, isUp);
                    if (keyLParam == IntPtr.Zero && isUp)
                        keyLParam = (IntPtr)unchecked((int)0xC0000001u);

                    target.PostMessage(outMsg, (IntPtr)vk, keyLParam);
                }
                return b.ConsumeInput;
            }

            return false;
        }
    }
}
