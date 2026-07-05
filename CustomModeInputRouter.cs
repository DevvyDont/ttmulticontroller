using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Forms;
using TTMulti.Controls;

namespace TTMulti
{
    /// <summary>Outcome of routing a single keyboard message through the custom-mode bindings.</summary>
    internal enum CustomRouteResult
    {
        /// <summary>No binding matched the input.</summary>
        NotMatched,
        /// <summary>A binding matched and the trigger key should be consumed.</summary>
        Consumed,
        /// <summary>A binding matched but opted out of consuming the trigger key (ConsumeInput=false).</summary>
        PassThrough
    }

    /// <summary>
    /// Dispatches keyboard messages for <see cref="MulticontrollerMode.Custom"/> using a <see cref="CustomModeDefinition"/>.
    /// </summary>
    internal static class CustomModeInputRouter
    {
        /// <summary>Keys posted DOWN for a given trigger key, so the matching KEYUP releases exactly them.</summary>
        struct HeldTrigger
        {
            public List<KeyValuePair<ToontownController, Keys>> Outputs;
            public bool Consume;
        }

        /// <summary>
        /// Tracks, per physical trigger key, the (target, virtual-key) pairs posted DOWN.  The matching KEYUP
        /// releases exactly those regardless of the current modifier state, so releasing a required modifier
        /// before the main key can't strand the mapped key down in the games (CORR-03).
        /// </summary>
        static readonly Dictionary<Keys, HeldTrigger> _heldTriggers = new Dictionary<Keys, HeldTrigger>();

        /// <summary>Forget all tracked held triggers (call after the games' keys have been released elsewhere).</summary>
        public static void ResetHeldTriggers()
        {
            _heldTriggers.Clear();
        }
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

        public static CustomRouteResult TryProcess(Multicontroller multicontroller, CustomModeDefinition definition, Win32.WM msg, IntPtr wParam, IntPtr lParam)
        {
            Keys keysPressed = (Keys)wParam;
            if (msg == Win32.WM.HOTKEY)
                keysPressed = (Keys)(int)(lParam.ToInt64() >> 16);

            bool isDown = msg == Win32.WM.KEYDOWN || msg == Win32.WM.HOTKEY || msg == Win32.WM.SYSKEYDOWN;
            bool isUp = msg == Win32.WM.KEYUP || msg == Win32.WM.SYSKEYUP;
            if (!isDown && !isUp)
                return CustomRouteResult.NotMatched;

            // Key-up: release exactly what the matching key-down posted, regardless of the current modifier
            // state.  Releasing a required modifier before the main key must never leave the mapped key stuck
            // down in the games (CORR-03).  Handled here (not via the binding list) so it stays correct even
            // when several bindings share the same trigger key.
            if (isUp && _heldTriggers.TryGetValue(keysPressed, out HeldTrigger held))
            {
                foreach (KeyValuePair<ToontownController, Keys> output in held.Outputs)
                    output.Key.PostMessage(Win32.WM.KEYUP, (IntPtr)output.Value, Win32.MakePostedKeyLParam(output.Value, true));
                _heldTriggers.Remove(keysPressed);
                return held.Consume ? CustomRouteResult.Consumed : CustomRouteResult.PassThrough;
            }

            if (definition?.Bindings == null || definition.Bindings.Count == 0)
                return CustomRouteResult.NotMatched;

            Keys mod = Control.ModifierKeys;
            foreach (CustomModeBinding b in definition.Bindings)
            {
                if ((Keys)b.InputKey != keysPressed)
                    continue;
                // Modifier requirements gate the key-down trigger only; key-up release is handled above.
                if (isDown)
                {
                    if (b.RequireAlt && (mod & Keys.Alt) == Keys.None)
                        continue;
                    if (b.RequireControl && (mod & Keys.Control) == Keys.None)
                        continue;
                    if (b.RequireShift && (mod & Keys.Shift) == Keys.None)
                        continue;
                }

                CustomRouteResult consumeResult = b.ConsumeInput ? CustomRouteResult.Consumed : CustomRouteResult.PassThrough;

                var list = multicontroller.GetControllersInCustomModeOrder();
                List<ToontownController> targets = ResolveTargets(b, list);
                if (targets.Count == 0)
                    return consumeResult;

                if (b.Action == CustomModeBindingAction.InstantClick)
                {
                    if (isDown)
                    {
                        foreach (ToontownController target in targets)
                            multicontroller.TriggerInstantMultiClickSingleTarget(target);
                    }
                    return consumeResult;
                }

                if (b.Action == CustomModeBindingAction.SendRole
                    && string.Equals(b.RoleTitle, CustomModeWellKnownRoles.ZeroPowerThrow, StringComparison.OrdinalIgnoreCase))
                {
                    if (isDown)
                    {
                        var throwBinding = Properties.SerializedSettings.Default.Bindings
                            .FirstOrDefault(x => string.Equals(x.Title, "Throw", StringComparison.OrdinalIgnoreCase));
                        if (throwBinding != null)
                        {
                            foreach (ToontownController target in targets)
                            {
                                Keys throwKey = Keys.None;
                                if (target.Type == ControllerType.Left && throwBinding.LeftToonKey != Keys.None)
                                    throwKey = throwBinding.LeftToonKey;
                                else if (target.Type == ControllerType.Right && throwBinding.RightToonKey != Keys.None)
                                    throwKey = throwBinding.RightToonKey;
                                if (throwKey == Keys.None)
                                    continue;
                                // Instant 0% tap: well-formed lParams (a zero lParam is misread as a keypress by chat) — WIN32-03.
                                target.PostMessage(Win32.WM.KEYDOWN, (IntPtr)throwKey, Win32.MakePostedKeyLParam(throwKey, false));
                                target.PostMessage(Win32.WM.KEYUP, (IntPtr)throwKey, Win32.MakePostedKeyLParam(throwKey, true));
                            }
                        }
                    }
                    return consumeResult;
                }

                // SendRawKey / SendRole: post KEYDOWN now and remember the outputs so the matching KEYUP can
                // release them (handled by the _heldTriggers block above) even if the modifier is released first.
                KeyMapping roleMap = null;
                if (b.Action == CustomModeBindingAction.SendRole)
                {
                    roleMap = Properties.SerializedSettings.Default.Bindings
                        .FirstOrDefault(x => string.Equals(x.Title, b.RoleTitle, StringComparison.OrdinalIgnoreCase));
                    if (roleMap == null)
                        return consumeResult;
                }

                if (isDown)
                {
                    var outputs = new List<KeyValuePair<ToontownController, Keys>>();
                    foreach (ToontownController target in targets)
                    {
                        Keys vk = Keys.None;
                        if (b.Action == CustomModeBindingAction.SendRawKey)
                            vk = (Keys)b.RawKey;
                        else if (b.Action == CustomModeBindingAction.SendRole)
                            vk = ResolveRoleVirtualKey(roleMap, target);

                        if (vk == Keys.None)
                            continue;

                        target.PostMessage(Win32.WM.KEYDOWN, (IntPtr)vk, Win32.MakePostedKeyLParam(vk, false));
                        outputs.Add(new KeyValuePair<ToontownController, Keys>(target, vk));
                    }
                    // Overwrites any prior entry (e.g. auto-repeat), so the held set never accumulates.
                    _heldTriggers[keysPressed] = new HeldTrigger { Outputs = outputs, Consume = b.ConsumeInput };
                }

                return consumeResult;
            }

            return CustomRouteResult.NotMatched;
        }
    }
}
