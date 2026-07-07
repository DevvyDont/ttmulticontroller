using System.Collections.Generic;
using System.IO;
using System.Runtime.Serialization.Json;
using System.Text;
using TTMulti;
using Xunit;

namespace TTMulti.Tests
{
    /// <summary>
    /// Pins the frozen layout-presets.json schema (CLAUDE.md: persisted formats are frozen). The Layout Presets
    /// UI rework is presentation-only; this guards that the on-disk shape it reads and writes is unchanged, using
    /// the same DataContractJsonSerializer configuration as LayoutPresetStorage.
    /// </summary>
    public class LayoutPresetSerializationTests
    {
        private static readonly DataContractJsonSerializer Serializer =
            new DataContractJsonSerializer(typeof(LayoutPresetFile),
                new DataContractJsonSerializerSettings { UseSimpleDictionaryFormat = true });

        private static string Serialize(LayoutPresetFile file)
        {
            using (var ms = new MemoryStream())
            {
                Serializer.WriteObject(ms, file);
                return Encoding.UTF8.GetString(ms.ToArray());
            }
        }

        private static LayoutPresetFile Deserialize(string json)
        {
            using (var ms = new MemoryStream(Encoding.UTF8.GetBytes(json)))
                return (LayoutPresetFile)Serializer.ReadObject(ms);
        }

        private static LayoutPresetFile Sample() => new LayoutPresetFile
        {
            Presets = new List<LayoutPreset>
            {
                new LayoutPreset
                {
                    Name = "4-Toons",
                    HotkeyCode = (int)System.Windows.Forms.Keys.C,
                    HotkeyModifiers = (int)Win32.KeyModifiers.Alt,
                    Regions = new List<LayoutRegion>
                    {
                        new LayoutRegion { Source = LayoutRegionSource.Monitor, MonitorIndex = 1, Rows = 2, Cols = 2 },
                        new LayoutRegion { Source = LayoutRegionSource.Custom, CustomX = 10, CustomY = 20, CustomWidth = 800, CustomHeight = 600, Rows = 1, Cols = 2, ColWeights = new double[] { 2, 1 } },
                    },
                    SlotOverrides = new List<SlotOverride>
                    {
                        new SlotOverride { SlotIndex = 2, Rect = new LayoutRect { X = 5, Y = 6, Width = 100, Height = 200 } },
                        new SlotOverride { SlotIndex = 3, Minimized = true },
                    },
                },
            },
        };

        [Fact]
        public void Roundtrip_preserves_all_fields()
        {
            LayoutPresetFile back = Deserialize(Serialize(Sample()));

            Assert.Single(back.Presets);
            var p = back.Presets[0];
            Assert.Equal("4-Toons", p.Name);
            Assert.Equal((int)System.Windows.Forms.Keys.C, p.HotkeyCode);
            Assert.Equal((int)Win32.KeyModifiers.Alt, p.HotkeyModifiers);
            Assert.Equal(2, p.Regions.Count);

            Assert.Equal(LayoutRegionSource.Monitor, p.Regions[0].Source);
            Assert.Equal(1, p.Regions[0].MonitorIndex);
            Assert.Equal(2, p.Regions[0].Rows);
            Assert.Equal(2, p.Regions[0].Cols);

            Assert.Equal(LayoutRegionSource.Custom, p.Regions[1].Source);
            Assert.Equal(800, p.Regions[1].CustomWidth);
            Assert.Equal(new double[] { 2, 1 }, p.Regions[1].ColWeights);

            Assert.Equal(2, p.SlotOverrides.Count);
            Assert.Equal(2, p.SlotOverrides[0].SlotIndex);
            Assert.NotNull(p.SlotOverrides[0].Rect);
            Assert.Equal(100, p.SlotOverrides[0].Rect.Width);
            Assert.Equal(3, p.SlotOverrides[1].SlotIndex);
            Assert.True(p.SlotOverrides[1].Minimized);
        }

        [Fact]
        public void Json_uses_the_frozen_member_names_and_integer_source_encoding()
        {
            string json = Serialize(Sample());

            foreach (string member in new[] { "Presets", "Name", "HotkeyCode", "HotkeyModifiers", "Regions",
                "Source", "MonitorIndex", "Rows", "Cols", "CustomX", "CustomWidth", "ColWeights",
                "SlotOverrides", "SlotIndex", "Rect", "Minimized" })
                Assert.Contains("\"" + member + "\"", json);

            // DataContractJsonSerializer encodes the LayoutRegionSource enum as its integer value.
            Assert.Contains("\"Source\":0", json); // Monitor
            Assert.Contains("\"Source\":1", json); // Custom
        }
    }
}
