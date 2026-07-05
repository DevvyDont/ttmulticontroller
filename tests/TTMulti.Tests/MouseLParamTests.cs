using System;
using TTMulti;
using Xunit;

namespace TTMulti.Tests
{
    /// <summary>
    /// Pins the client-relative mouse-click lParam packing used by all click forwarding (the low word is X
    /// masked to 16 bits, the high word is Y). This is the exact bit layout the app has always posted; the tests
    /// lock it in before the forwarding code is extracted so any accidental change is caught.
    /// </summary>
    public class MouseLParamTests
    {
        [Theory]
        [InlineData(0, 0, 0x00000000)]
        [InlineData(1, 0, 0x00000001)]
        [InlineData(0, 1, 0x00010000)]
        [InlineData(10, 20, 0x0014000A)]
        [InlineData(640, 480, 0x01E00280)]
        [InlineData(0xFFFF, 0, 0x0000FFFF)]
        public void MakeMouseLParam_packs_x_low_y_high(int x, int y, int expected)
        {
            Assert.Equal((IntPtr)expected, Win32.MakeMouseLParam(x, y));
        }

        [Fact]
        public void MakeMouseLParam_masks_x_to_low_word_but_not_y()
        {
            // X above 16 bits is masked away (0x10000 -> low word 0); this is the historical behavior.
            Assert.Equal((IntPtr)0, Win32.MakeMouseLParam(0x10000, 0));
            // Y is shifted as-is, so its value lands in the high word.
            Assert.Equal((IntPtr)0x00030000, Win32.MakeMouseLParam(0, 3));
        }
    }
}
