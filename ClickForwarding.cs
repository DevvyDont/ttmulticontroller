using System;
using System.Drawing;

namespace TTMulti
{
    /// <summary>
    /// Shared click-forwarding primitives used by every synthesized left-click path (instant multi-click,
    /// controlled-multiclick regular click, custom-mode single-target click). Centralizes the hit-test,
    /// screen→client coordinate conversion, and the WM_LBUTTONDOWN/UP posting that were duplicated inline.
    /// The geometry helpers are pure so they can be characterization-tested.
    /// </summary>
    internal static class ClickForwarding
    {
        /// <summary>
        /// True when <paramref name="screenPoint"/> is inside a window's client area. Left/top edges are
        /// inclusive, right/bottom edges are exclusive — the exact hit-test the click paths have always used.
        /// </summary>
        internal static bool ClientAreaContainsPoint(Point clientLocation, Size clientSize, Point screenPoint)
        {
            return screenPoint.X >= clientLocation.X && screenPoint.X < clientLocation.X + clientSize.Width
                && screenPoint.Y >= clientLocation.Y && screenPoint.Y < clientLocation.Y + clientSize.Height;
        }

        /// <summary>Converts a screen point to a point relative to a window's client-area origin.</summary>
        internal static Point ToClientRelative(Point clientLocation, Point screenPoint)
        {
            return new Point(screenPoint.X - clientLocation.X, screenPoint.Y - clientLocation.Y);
        }

        /// <summary>Posts a synthesized left click (down then up) at a client-relative point to one window.</summary>
        internal static void PostLeftClick(ToontownController controller, int relativeX, int relativeY)
        {
            IntPtr clickLParam = Win32.MakeMouseLParam(relativeX, relativeY);
            controller.PostMessage(Win32.WM.LBUTTONDOWN, (IntPtr)Win32.MK_LBUTTON, clickLParam);
            controller.PostMessage(Win32.WM.LBUTTONUP, IntPtr.Zero, clickLParam);
        }
    }
}
