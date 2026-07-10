using System;
using TTMulti;
using Xunit;

namespace TTMulti.Tests
{
    /// <summary>
    /// Guards the reference-counted watch set against the "release held keys on focus" swap-clobber bug: a
    /// Switching-Mode swap assigns both controllers' WindowHandle in sequence, so a handle is transiently owned
    /// by two controllers. With a plain set, the releasing controller's StopWatchingWindow dropped the handle
    /// the new owner still needed, leaving the swapped window unwatched — focusing it then fired no activation
    /// and no key release. Counting keeps it watched until every owner has released it.
    /// </summary>
    /// <remarks>
    /// Runs on an isolated WindowWatcher built via its private constructor (not the shared singleton) so the
    /// test touches no global state and installs no hooks (SynchronizingObject stays null). The handles are
    /// arbitrary non-window values: WatchWindow's seed step no-ops on them (Win32.IsWindow returns false), which
    /// is irrelevant to the counting under test.
    /// </remarks>
    public class WindowWatcherRefCountTests
    {
        private static WindowWatcher NewIsolatedWatcher() =>
            (WindowWatcher)Activator.CreateInstance(typeof(WindowWatcher), nonPublic: true);

        [Fact]
        public void Swap_keeps_both_handles_watched()
        {
            var w = NewIsolatedWatcher();
            IntPtr h1 = new IntPtr(0x1001);
            IntPtr h2 = new IntPtr(0x1002);

            // Steady state: controller1 owns h1, controller2 owns h2.
            w.WatchWindow(h1);
            w.WatchWindow(h2);

            // Switching-Mode swap: controller1.WindowHandle = h2, then controller2.WindowHandle = h1.
            // (A) controller1 releases h1 and takes h2.
            w.StopWatchingWindow(h1);
            w.WatchWindow(h2);
            // (B) controller2 releases h2 and takes h1.
            w.StopWatchingWindow(h2);
            w.WatchWindow(h1);

            // Both handles must remain watched — h2 in particular (the one the old code dropped).
            Assert.True(w.IsWatching(h1));
            Assert.True(w.IsWatching(h2));
        }

        [Fact]
        public void Handle_drops_only_after_last_owner_releases()
        {
            var w = NewIsolatedWatcher();
            IntPtr h = new IntPtr(0x2002);

            w.WatchWindow(h);   // owner 1
            w.WatchWindow(h);   // owner 2 (transient dual-ownership)
            Assert.True(w.IsWatching(h));

            w.StopWatchingWindow(h);   // owner 1 releases
            Assert.True(w.IsWatching(h));   // still owned by owner 2

            w.StopWatchingWindow(h);   // owner 2 releases
            Assert.False(w.IsWatching(h));
        }

        [Fact]
        public void StopWatching_unknown_handle_is_a_no_op()
        {
            var w = NewIsolatedWatcher();
            w.StopWatchingWindow(new IntPtr(0x3003));   // must not throw or underflow
            Assert.False(w.IsWatching(new IntPtr(0x3003)));
        }
    }
}
