using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows.Forms;
using System.Xml.Serialization;
using TTMulti.Controls;

namespace TTMulti.Properties
{
    internal class SerializedSettings
    {
        public static SerializedSettings Default { get; } = new SerializedSettings();

        XmlSerializer keyMappingSerializer = new XmlSerializer(typeof(List<KeyMapping>));

        // Cache the deserialized bindings keyed by the raw setting string, so the per-keystroke read path does not
        // run a full XmlSerializer.Deserialize on every access (PERF-06).  Re-parses whenever the underlying
        // keyBindings string changes, including an external Settings.Reload, so it never returns stale data.
        List<KeyMapping> _cachedBindings;
        string _cachedBindingsSource;

        public List<KeyMapping> Bindings
        {
            get
            {
                string source = Properties.Settings.Default.keyBindings;
                if (_cachedBindings != null && source == _cachedBindingsSource)
                    return _cachedBindings;

                List<KeyMapping> result;
                using (StringReader sr = new StringReader(source ?? string.Empty))
                {
                    try
                    {
                        result = keyMappingSerializer.Deserialize(sr) as List<KeyMapping>;
                    }
                    catch
                    {
                        result = new List<KeyMapping>()
                        {
                            new KeyMapping("Forward", Keys.Up, (Keys)Properties.Settings.Default.leftForwardKeyCode, (Keys)Properties.Settings.Default.rightForwardKeyCode, true),
                            new KeyMapping("Left", Keys.Left, (Keys)Properties.Settings.Default.leftLeftKeyCode, (Keys)Properties.Settings.Default.rightLeftKeyCode, true),
                            new KeyMapping("Backward", Keys.Down, (Keys)Properties.Settings.Default.leftBackKeyCode, (Keys)Properties.Settings.Default.rightBackKeyCode, true),
                            new KeyMapping("Right", Keys.Right, (Keys)Properties.Settings.Default.leftRightKeyCode, (Keys)Properties.Settings.Default.rightRightKeyCode, true),
                            new KeyMapping("Jump", Keys.ControlKey, (Keys)Properties.Settings.Default.leftJumpKeyCode, (Keys)Properties.Settings.Default.rightJumpKeyCode, true),
                            new KeyMapping("Throw", Keys.Delete, (Keys)Properties.Settings.Default.leftThrowKeyCode, (Keys)Properties.Settings.Default.rightThrowKeyCode, true),
                            new KeyMapping("Open Book", Keys.Escape, (Keys)Properties.Settings.Default.leftEscapeKeyCode, (Keys)Properties.Settings.Default.rightEscapeKeyCode, true)
                        };
                    }
                }

                _cachedBindings = result;
                _cachedBindingsSource = source;
                return result;
            }
            set
            {
                using (StringWriter sw = new StringWriter())
                {
                    keyMappingSerializer.Serialize(sw, value);
                    Properties.Settings.Default.keyBindings = sw.ToString();
                }
                // Invalidate so the next read reflects the value just written.
                _cachedBindings = null;
                _cachedBindingsSource = null;
            }
        }
    }
}
