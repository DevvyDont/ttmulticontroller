using System.Collections.Generic;
using System.IO;
using System.Windows.Forms;
using System.Xml.Serialization;
using TTMulti;
using Xunit;

namespace TTMulti.Tests
{
    /// <summary>
    /// Golden test pinning the exact XML that SerializedSettings writes into the keyBindings setting
    /// (XmlSerializer of List&lt;KeyMapping&gt; to a StringWriter). The on-disk format is frozen: any change to
    /// this output — from moving/renaming the KeyMapping class or its properties — would corrupt every user's
    /// saved key bindings. The expected string below was captured from the shipped implementation.
    /// </summary>
    public class KeyMappingSerializationTests
    {
        private static string Serialize(List<KeyMapping> list)
        {
            var serializer = new XmlSerializer(typeof(List<KeyMapping>));
            using (var sw = new StringWriter())
            {
                serializer.Serialize(sw, list);
                return sw.ToString();
            }
        }

        [Fact]
        public void Serialized_keyBindings_xml_is_frozen()
        {
            var list = new List<KeyMapping>
            {
                new KeyMapping("Forward", Keys.Up, Keys.W, Keys.Up, true),
                new KeyMapping("Custom Thing", Keys.F5, Keys.T, Keys.None, false),
            };

            string expected =
"<?xml version=\"1.0\" encoding=\"utf-16\"?>\r\n" +
"<ArrayOfKeyMapping xmlns:xsi=\"http://www.w3.org/2001/XMLSchema-instance\" xmlns:xsd=\"http://www.w3.org/2001/XMLSchema\">\r\n" +
"  <KeyMapping>\r\n" +
"    <Title>Forward</Title>\r\n" +
"    <Key>Up</Key>\r\n" +
"    <LeftToonKey>W</LeftToonKey>\r\n" +
"    <RightToonKey>Up</RightToonKey>\r\n" +
"    <ReadOnly>true</ReadOnly>\r\n" +
"  </KeyMapping>\r\n" +
"  <KeyMapping>\r\n" +
"    <Title>Custom Thing</Title>\r\n" +
"    <Key>F5</Key>\r\n" +
"    <LeftToonKey>T</LeftToonKey>\r\n" +
"    <RightToonKey>None</RightToonKey>\r\n" +
"    <ReadOnly>false</ReadOnly>\r\n" +
"  </KeyMapping>\r\n" +
"</ArrayOfKeyMapping>";

            Assert.Equal(expected, Serialize(list));
        }

        [Fact]
        public void Roundtrip_preserves_values()
        {
            var list = new List<KeyMapping> { new KeyMapping("Jump", Keys.ControlKey, Keys.G, Keys.RControlKey, true) };
            string xml = Serialize(list);

            var serializer = new XmlSerializer(typeof(List<KeyMapping>));
            using (var sr = new StringReader(xml))
            {
                var back = (List<KeyMapping>)serializer.Deserialize(sr);
                Assert.Single(back);
                Assert.Equal("Jump", back[0].Title);
                Assert.Equal(Keys.ControlKey, back[0].Key);
                Assert.Equal(Keys.G, back[0].LeftToonKey);
                Assert.Equal(Keys.RControlKey, back[0].RightToonKey);
                Assert.True(back[0].ReadOnly);
            }
        }
    }
}
