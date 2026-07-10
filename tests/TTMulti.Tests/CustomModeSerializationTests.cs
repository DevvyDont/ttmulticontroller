using System.Collections.Generic;
using System.IO;
using System.Runtime.Serialization.Json;
using System.Text;
using TTMulti;
using Xunit;

namespace TTMulti.Tests
{
    /// <summary>
    /// Pins the frozen custom-modes.json schema (CLAUDE.md: persisted formats are frozen). The Custom Modes UI
    /// overhaul is presentation-only; this guards that the on-disk shape it reads and writes is unchanged, using
    /// the same DataContractJsonSerializer configuration as CustomModeStorage.
    /// </summary>
    public class CustomModeSerializationTests
    {
        private static readonly DataContractJsonSerializer Serializer =
            new DataContractJsonSerializer(typeof(CustomModeFile),
                new DataContractJsonSerializerSettings { UseSimpleDictionaryFormat = true });

        private static string Serialize(CustomModeFile file)
        {
            using (var ms = new MemoryStream())
            {
                Serializer.WriteObject(ms, file);
                return Encoding.UTF8.GetString(ms.ToArray());
            }
        }

        private static CustomModeFile Deserialize(string json)
        {
            using (var ms = new MemoryStream(Encoding.UTF8.GetBytes(json)))
                return (CustomModeFile)Serializer.ReadObject(ms);
        }

        private static CustomModeFile Sample() => new CustomModeFile
        {
            Modes = new List<CustomModeDefinition>
            {
                new CustomModeDefinition
                {
                    Id = "abc123",
                    Name = "Solo CJ",
                    ActivationHotkeyCode = (int)System.Windows.Forms.Keys.C,
                    ActivationHotkeyModifiers = (int)Win32.KeyModifiers.Alt,
                    ActivationHotkeyGlobal = true,
                    IncludeInModeHotkeyCycle = false,
                    LeftBorderColorArgb = unchecked((int)0xFF32CD32),
                    Bindings = new List<CustomModeBinding>
                    {
                        new CustomModeBinding { InputKey = (int)System.Windows.Forms.Keys.F, Action = CustomModeBindingAction.SendRole, RoleTitle = "Forward", TargetKind = CustomModeTargetKind.Single, TargetIndex = 2, RequireAlt = true },
                        new CustomModeBinding { InputKey = (int)System.Windows.Forms.Keys.G, Action = CustomModeBindingAction.SendRawKey, RawKey = (int)System.Windows.Forms.Keys.B, TargetKind = CustomModeTargetKind.All },
                        new CustomModeBinding { InputKey = (int)System.Windows.Forms.Keys.H, Action = CustomModeBindingAction.InstantClick, TargetKind = CustomModeTargetKind.Listed, ListedTargetIndices = new List<int> { 1, 3 }, ConsumeInput = false },
                    },
                },
            },
        };

        [Fact]
        public void Roundtrip_preserves_all_fields()
        {
            CustomModeFile back = Deserialize(Serialize(Sample()));

            Assert.Single(back.Modes);
            CustomModeDefinition m = back.Modes[0];
            Assert.Equal("abc123", m.Id);
            Assert.Equal("Solo CJ", m.Name);
            Assert.Equal((int)System.Windows.Forms.Keys.C, m.ActivationHotkeyCode);
            Assert.Equal((int)Win32.KeyModifiers.Alt, m.ActivationHotkeyModifiers);
            Assert.True(m.ActivationHotkeyGlobal);
            Assert.False(m.ShouldIncludeInModeHotkeyCycle());
            Assert.Equal(unchecked((int)0xFF32CD32), m.LeftBorderColorArgb);
            Assert.Equal(3, m.Bindings.Count);

            Assert.Equal(CustomModeBindingAction.SendRole, m.Bindings[0].Action);
            Assert.Equal("Forward", m.Bindings[0].RoleTitle);
            Assert.Equal(CustomModeTargetKind.Single, m.Bindings[0].TargetKind);
            Assert.Equal(2, m.Bindings[0].TargetIndex);
            Assert.True(m.Bindings[0].RequireAlt);

            Assert.Equal(CustomModeBindingAction.SendRawKey, m.Bindings[1].Action);
            Assert.Equal((int)System.Windows.Forms.Keys.B, m.Bindings[1].RawKey);
            Assert.Equal(CustomModeTargetKind.All, m.Bindings[1].TargetKind);

            Assert.Equal(CustomModeBindingAction.InstantClick, m.Bindings[2].Action);
            Assert.Equal(CustomModeTargetKind.Listed, m.Bindings[2].TargetKind);
            Assert.Equal(new List<int> { 1, 3 }, m.Bindings[2].ListedTargetIndices);
            Assert.False(m.Bindings[2].ConsumeInput);
        }

        [Fact]
        public void Json_uses_the_frozen_member_names_and_integer_enum_encoding()
        {
            string json = Serialize(Sample());

            // Object members (property renames would break existing files).
            foreach (string member in new[] { "Modes", "Id", "Name", "Bindings", "ActivationHotkeyCode",
                "ActivationHotkeyModifiers", "ActivationHotkeyGlobal", "InputKey", "Action", "RoleTitle", "RawKey",
                "TargetIndex", "TargetKind", "ListedTargetIndices", "ConsumeInput" })
                Assert.Contains("\"" + member + "\"", json);

            // DataContractJsonSerializer encodes enums as their integer value, not the [EnumMember] name.
            // The sample covers every action (0/1/2) and every target kind (0/1/2).
            foreach (string enc in new[] { "\"Action\":0", "\"Action\":1", "\"Action\":2",
                "\"TargetKind\":0", "\"TargetKind\":1", "\"TargetKind\":2" })
                Assert.Contains(enc, json);
        }
    }
}
