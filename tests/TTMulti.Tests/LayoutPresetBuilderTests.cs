using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using TTMulti;
using Xunit;

namespace TTMulti.Tests
{
    /// <summary>
    /// Pins how a layout preset turns regions + grids + overrides into the ordered, numbered slots that windows
    /// fill. Uses Custom regions (absolute rects) so the math is deterministic and independent of the machine's
    /// monitors, matching the live-preview diagram and the apply path.
    /// </summary>
    public class LayoutPresetBuilderTests
    {
        private static LayoutRegion Custom(int x, int y, int w, int h, int rows, int cols) => new LayoutRegion
        {
            Source = LayoutRegionSource.Custom,
            CustomX = x, CustomY = y, CustomWidth = w, CustomHeight = h,
            Rows = rows, Cols = cols,
        };

        [Fact]
        public void Single_grid_tiles_row_major_and_numbers_from_one()
        {
            var preset = new LayoutPreset { Regions = new List<LayoutRegion> { Custom(0, 0, 400, 400, 2, 2) } };

            var slots = LayoutPresetBuilder.BuildSlots(preset);

            Assert.Equal(4, slots.Count);
            Assert.Equal(new Rectangle(0, 0, 200, 200), slots[0].Rect);     // slot 1: top-left
            Assert.Equal(new Rectangle(200, 0, 200, 200), slots[1].Rect);   // slot 2: top-right
            Assert.Equal(new Rectangle(0, 200, 200, 200), slots[2].Rect);   // slot 3: bottom-left
            Assert.Equal(new Rectangle(200, 200, 200, 200), slots[3].Rect); // slot 4: bottom-right
        }

        [Fact]
        public void Slot_numbering_continues_across_regions_in_order()
        {
            var preset = new LayoutPreset
            {
                Regions = new List<LayoutRegion>
                {
                    Custom(0, 0, 200, 200, 1, 2),      // slots 1,2
                    Custom(0, 500, 200, 200, 2, 1),    // slots 3,4
                },
            };

            var slots = LayoutPresetBuilder.BuildSlots(preset);

            Assert.Equal(4, slots.Count);
            Assert.Equal(new Rectangle(0, 0, 100, 200), slots[0].Rect);
            Assert.Equal(new Rectangle(100, 0, 100, 200), slots[1].Rect);
            Assert.Equal(new Rectangle(0, 500, 200, 100), slots[2].Rect);
            Assert.Equal(new Rectangle(0, 600, 200, 100), slots[3].Rect);
        }

        [Fact]
        public void Column_weights_split_proportionally()
        {
            var region = Custom(0, 0, 400, 100, 1, 2);
            region.ColWeights = new double[] { 3, 1 };
            var preset = new LayoutPreset { Regions = new List<LayoutRegion> { region } };

            var slots = LayoutPresetBuilder.BuildSlots(preset);

            Assert.Equal(new Rectangle(0, 0, 300, 100), slots[0].Rect);   // 3/4 of 400
            Assert.Equal(new Rectangle(300, 0, 100, 100), slots[1].Rect); // last cell absorbs the remainder
        }

        [Fact]
        public void Slot_overrides_replace_rect_and_set_minimized()
        {
            var preset = new LayoutPreset
            {
                Regions = new List<LayoutRegion> { Custom(0, 0, 400, 400, 2, 2) },
                SlotOverrides = new List<SlotOverride>
                {
                    new SlotOverride { SlotIndex = 2, Rect = new LayoutRect { X = 1000, Y = 1000, Width = 50, Height = 60 } },
                    new SlotOverride { SlotIndex = 3, Minimized = true },
                },
            };

            var slots = LayoutPresetBuilder.BuildSlots(preset);

            Assert.Equal(new Rectangle(1000, 1000, 50, 60), slots[1].Rect); // slot 2 overridden
            Assert.False(slots[1].Minimized);
            Assert.True(slots[2].Minimized);                                 // slot 3 minimized
            Assert.Equal(new Rectangle(0, 200, 200, 200), slots[2].Rect);    // its computed rect is kept
        }

        [Fact]
        public void Rows_and_cols_below_one_are_clamped_to_a_single_cell()
        {
            var preset = new LayoutPreset { Regions = new List<LayoutRegion> { Custom(0, 0, 300, 300, 0, 0) } };

            var slots = LayoutPresetBuilder.BuildSlots(preset);

            Assert.Single(slots);
            Assert.Equal(new Rectangle(0, 0, 300, 300), slots[0].Rect);
        }
    }
}
