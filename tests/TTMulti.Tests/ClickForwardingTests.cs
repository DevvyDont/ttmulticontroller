using System.Drawing;
using TTMulti;
using Xunit;

namespace TTMulti.Tests
{
    /// <summary>
    /// Pins the click-forwarding geometry shared by every synthesized left-click path: the client-area hit-test
    /// (left/top inclusive, right/bottom exclusive) and the screen→client coordinate conversion.
    /// </summary>
    public class ClickForwardingTests
    {
        private static readonly Point Origin = new Point(100, 50);
        private static readonly Size Size640 = new Size(640, 480);

        [Theory]
        [InlineData(100, 50, true)]    // top-left corner — inclusive
        [InlineData(300, 200, true)]   // interior
        [InlineData(739, 529, true)]   // just inside the bottom-right (loc + size - 1)
        [InlineData(740, 200, false)]  // right edge — exclusive (100 + 640)
        [InlineData(300, 530, false)]  // bottom edge — exclusive (50 + 480)
        [InlineData(99, 200, false)]   // left of the window
        [InlineData(300, 49, false)]   // above the window
        public void ClientAreaContainsPoint_hit_test(int x, int y, bool expected)
        {
            Assert.Equal(expected, ClickForwarding.ClientAreaContainsPoint(Origin, Size640, new Point(x, y)));
        }

        [Theory]
        [InlineData(100, 50, 0, 0)]      // at the origin -> (0,0)
        [InlineData(300, 200, 200, 150)] // interior offset
        public void ToClientRelative_subtracts_origin(int sx, int sy, int expX, int expY)
        {
            Point rel = ClickForwarding.ToClientRelative(Origin, new Point(sx, sy));
            Assert.Equal(new Point(expX, expY), rel);
        }
    }
}
