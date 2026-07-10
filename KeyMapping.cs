using System;
using System.Windows.Forms;

namespace TTMulti
{
    /// <summary>
    /// One key-binding row: an in-game key (<see cref="Key"/>) and the physical keys the left/right toon press
    /// to trigger it. PERSISTENCE-CRITICAL: this type is XML-serialized (XmlSerializer of List&lt;KeyMapping&gt;)
    /// into the keyBindings string setting. The class name, property names, declaration order, and the
    /// <see cref="Keys"/> property type are FROZEN — changing any of them corrupts every user's saved bindings
    /// (guarded by KeyMappingSerializationTests). The CLR namespace is not part of the XML, so the type lives
    /// here in the engine rather than in the UI layer that originally declared it.
    /// </summary>
    [Serializable]
    public class KeyMapping
    {
        public string Title { get; set; }
        public Keys Key { get; set; }
        public Keys LeftToonKey { get; set; }
        public Keys RightToonKey { get; set; }
        public bool ReadOnly { get; set; }

        public KeyMapping() { }

        public KeyMapping(string title, Keys key, Keys leftToonKey, Keys rightToonKey, bool readOnly)
        {
            Title = title;
            Key = key;
            LeftToonKey = leftToonKey;
            RightToonKey = rightToonKey;
            ReadOnly = readOnly;
        }
    }
}
