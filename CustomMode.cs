using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Windows.Forms;

namespace TTMulti
{
    /// <summary>
    /// How a custom-mode binding transforms input.
    /// </summary>
    [DataContract]
    public enum CustomModeBindingAction
    {
        /// <summary>Send the left or right toon key for a named role from global Key Mappings (e.g. Forward).</summary>
        [EnumMember]
        SendRole = 0,
        /// <summary>Send a specific virtual key to the target window.</summary>
        [EnumMember]
        SendRawKey = 1,
        /// <summary>Left-click at cursor (or window center) on the target window only.</summary>
        [EnumMember]
        InstantClick = 2,
    }

    /// <summary>
    /// Which controller windows receive a custom-mode binding.
    /// </summary>
    [DataContract]
    public enum CustomModeTargetKind
    {
        /// <summary>Exactly one window: <see cref="CustomModeBinding.TargetIndex"/> (1-based).</summary>
        [EnumMember]
        Single = 0,
        /// <summary>Every open controller window, in custom-mode order.</summary>
        [EnumMember]
        All = 1,
        /// <summary>Windows listed in <see cref="CustomModeBinding.ListedTargetIndices"/> (1-based each).</summary>
        [EnumMember]
        Listed = 2,
    }

    /// <summary>
    /// One key binding inside a custom mode (first match wins).
    /// </summary>
    [DataContract]
    public class CustomModeBinding
    {
        [DataMember]
        public int InputKey { get; set; }

        [DataMember]
        public bool RequireAlt { get; set; }

        [DataMember]
        public bool RequireControl { get; set; }

        [DataMember]
        public bool RequireShift { get; set; }

        [DataMember]
        public CustomModeBindingAction Action { get; set; }

        /// <summary>For <see cref="CustomModeBindingAction.SendRole"/>: must match a <see cref="TTMulti.Controls.KeyMapping.Title"/> (e.g. Forward). The posted key is <see cref="TTMulti.Controls.KeyMapping.Key"/> (Toontown Key in Options).</summary>
        [DataMember]
        public string RoleTitle { get; set; }

        /// <summary>For <see cref="CustomModeBindingAction.SendRawKey"/>: virtual key to post.</summary>
        [DataMember]
        public int RawKey { get; set; }

        /// <summary>For <see cref="CustomModeTargetKind.Single"/>: 1-based index into the ordered controller list (same ordering as instant multiclick).</summary>
        [DataMember]
        public int TargetIndex { get; set; } = 1;

        /// <summary>How <see cref="TargetIndex"/> / <see cref="ListedTargetIndices"/> select windows.</summary>
        [DataMember]
        public CustomModeTargetKind TargetKind { get; set; } = CustomModeTargetKind.Single;

        /// <summary>For <see cref="CustomModeTargetKind.Listed"/>: 1-based indices (order preserved; duplicates allowed).</summary>
        [DataMember]
        public List<int> ListedTargetIndices { get; set; }

        [DataMember]
        public bool ConsumeInput { get; set; } = true;

        /// <summary>Parse comma/semicolon/space separated 1-based indices (for listed targets UI).</summary>
        public static List<int> ParseListedTargetIndices(string text, int maxIndex = 32)
        {
            var list = new List<int>();
            if (string.IsNullOrWhiteSpace(text))
                return list;
            foreach (string part in text.Split(new[] { ',', ';', ' ' }, StringSplitOptions.RemoveEmptyEntries))
            {
                if (int.TryParse(part.Trim(), out int n) && n >= 1 && n <= maxIndex)
                    list.Add(n);
            }
            return list;
        }

        public override string ToString()
        {
            string mod = (RequireAlt ? "Alt+" : "") + (RequireControl ? "Ctrl+" : "") + (RequireShift ? "Shift+" : "");
            string act = Action == CustomModeBindingAction.SendRole ? "Role:" + (RoleTitle ?? "")
                : Action == CustomModeBindingAction.SendRawKey ? "Raw:" + ((Keys)RawKey).ToString()
                : "Click";
            string tgt = TargetKind == CustomModeTargetKind.All ? "all"
                : TargetKind == CustomModeTargetKind.Listed
                    ? ((ListedTargetIndices != null && ListedTargetIndices.Count > 0)
                        ? "#" + string.Join(",#", ListedTargetIndices)
                        : "listed (empty)")
                : "#" + TargetIndex;
            return mod + ((Keys)InputKey) + " -> " + act + " -> " + tgt;
        }
    }

    /// <summary>
    /// A user-defined controller mode: name plus bindings.
    /// </summary>
    [DataContract]
    public class CustomModeDefinition
    {
        [DataMember]
        public string Id { get; set; }

        [DataMember]
        public string Name { get; set; }

        [DataMember]
        public List<CustomModeBinding> Bindings { get; set; }

        public CustomModeDefinition()
        {
            Id = Guid.NewGuid().ToString("N");
            Name = "New custom mode";
            Bindings = new List<CustomModeBinding>();
        }
    }

    [DataContract]
    public class CustomModeFile
    {
        [DataMember]
        public List<CustomModeDefinition> Modes { get; set; }

        public CustomModeFile()
        {
            Modes = new List<CustomModeDefinition>();
        }
    }

    /// <summary>
    /// Load/save custom modes from JSON next to the executable (same pattern as layout presets).
    /// </summary>
    public static class CustomModeStorage
    {
        private const string FileName = "custom-modes.json";
        private static readonly DataContractJsonSerializer Serializer = new DataContractJsonSerializer(typeof(CustomModeFile),
            new DataContractJsonSerializerSettings { UseSimpleDictionaryFormat = true });

        private static string GetFilePath()
        {
            try
            {
                string path = Assembly.GetExecutingAssembly().Location;
                if (!string.IsNullOrEmpty(path))
                    return Path.Combine(Path.GetDirectoryName(path), FileName);
            }
            catch { }
            try
            {
                string path = Application.ExecutablePath;
                if (!string.IsNullOrEmpty(path))
                    return Path.Combine(Path.GetDirectoryName(path), FileName);
            }
            catch { }
            return Path.Combine(AppDomain.CurrentDomain.BaseDirectory ?? ".", FileName);
        }

        public static CustomModeFile Load()
        {
            string path = GetFilePath();
            if (!File.Exists(path))
                return new CustomModeFile();
            try
            {
                using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
                {
                    return (CustomModeFile)Serializer.ReadObject(fs);
                }
            }
            catch
            {
                return new CustomModeFile();
            }
        }

        public static void Save(CustomModeFile data)
        {
            if (data == null) return;
            string path = GetFilePath();
            try
            {
                string dir = Path.GetDirectoryName(path);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                    Directory.CreateDirectory(dir);
                using (var fs = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None))
                {
                    Serializer.WriteObject(fs, data);
                }
            }
            catch { }
        }
    }
}
