using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Reflection;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Windows.Forms;

namespace TTMulti
{
    /// <summary>
    /// <see cref="CustomModeBindingAction.SendRole"/> titles that are not Key Mapping row names.
    /// </summary>
    public static class CustomModeWellKnownRoles
    {
        /// <summary>Instant tap (KEYDOWN+KEYUP) of each target's Throw left/right toon key — same as the Zero Power Throw hotkey.</summary>
        public const string ZeroPowerThrow = "Zero Power Throw";
    }

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

        /// <summary>For <see cref="CustomModeBindingAction.SendRole"/>: a <see cref="TTMulti.Controls.KeyMapping.Title"/> (e.g. Forward; posted key is <see cref="TTMulti.Controls.KeyMapping.Key"/>) or <see cref="CustomModeWellKnownRoles.ZeroPowerThrow"/>.</summary>
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

        /// <summary>Optional ARGB border color for left-slot controllers (same defaults as Multi mode left when unset).</summary>
        [DataMember(EmitDefaultValue = false)]
        public int? LeftBorderColorArgb { get; set; }

        /// <summary>Optional ARGB border color for right-slot controllers (same defaults as Multi mode right when unset).</summary>
        [DataMember(EmitDefaultValue = false)]
        public int? RightBorderColorArgb { get; set; }

        /// <summary>When <c>false</c>, this definition is skipped by the main mode hotkey cycle. <c>null</c> or <c>true</c> = include.</summary>
        [DataMember(EmitDefaultValue = false)]
        public bool? IncludeInModeHotkeyCycle { get; set; }

        /// <summary>Virtual key for a global hotkey that switches to this custom mode (0 = disabled).</summary>
        [DataMember]
        public int ActivationHotkeyCode { get; set; }

        /// <summary>Modifier flags (same encoding as Hotkeys: <c>Win32.KeyModifiers</c> as int) for <see cref="ActivationHotkeyCode"/>.</summary>
        [DataMember]
        public int ActivationHotkeyModifiers { get; set; }

        /// <summary>When true, activation hotkey is registered globally; when false, only while the multicontroller is active.</summary>
        [DataMember]
        public bool ActivationHotkeyGlobal { get; set; }

        public bool ShouldIncludeInModeHotkeyCycle() => IncludeInModeHotkeyCycle != false;

        /// <summary>Same default green as Multi Mode (Left) border in Options.</summary>
        public static readonly Color DefaultLeftBorderColor = Color.FromArgb(50, 205, 50);

        /// <summary>Same default as Multi Mode (Right) border in Options.</summary>
        public static readonly Color DefaultRightBorderColor = Color.FromArgb(0, 100, 0);

        public Color GetLeftBorderColor() =>
            LeftBorderColorArgb.HasValue ? Color.FromArgb(LeftBorderColorArgb.Value) : DefaultLeftBorderColor;

        public Color GetRightBorderColor() =>
            RightBorderColorArgb.HasValue ? Color.FromArgb(RightBorderColorArgb.Value) : DefaultRightBorderColor;

        public CustomModeDefinition()
        {
            Id = Guid.NewGuid().ToString("N");
            Name = "New custom mode";
            Bindings = new List<CustomModeBinding>();
            ActivationHotkeyGlobal = true;
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

        // Cache for LoadCached(), keyed by file path + last-write time.  Read-only hot paths share this instance;
        // editors call Load() for their own mutable copy. PERF-03 / CORR-08.
        private static CustomModeFile _cachedFile;
        private static string _cachedPath;
        private static DateTime _cachedWriteTimeUtc;

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
            catch (Exception ex)
            {
                // A corrupt/unreadable file is silently replaced with an empty set here; log it so the data loss is
                // at least diagnosable instead of vanishing without a trace. (CORR-10)
                System.Diagnostics.Trace.WriteLine("CustomModeStorage.Load failed: " + ex);
                return new CustomModeFile();
            }
        }

        /// <summary>
        /// Cached variant of <see cref="Load"/> for hot read paths (per-keystroke Custom-mode routing and border
        /// refresh).  Re-reads only when the file's last-write time changes.  Returns a SHARED instance, so callers
        /// must treat it as read-only; editors that mutate should use <see cref="Load"/>. PERF-03 / CORR-08.
        /// </summary>
        public static CustomModeFile LoadCached()
        {
            string path = GetFilePath();
            if (!File.Exists(path))
            {
                _cachedFile = null;
                return new CustomModeFile();
            }
            try
            {
                DateTime writeTime = File.GetLastWriteTimeUtc(path);
                if (_cachedFile != null && _cachedPath == path && _cachedWriteTimeUtc == writeTime)
                    return _cachedFile;

                CustomModeFile file = Load();
                _cachedFile = file;
                _cachedPath = path;
                _cachedWriteTimeUtc = writeTime;
                return file;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Trace.WriteLine("CustomModeStorage.LoadCached failed: " + ex);
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
                // Invalidate the read cache so LoadCached() re-reads the just-written file.
                _cachedFile = null;
            }
            catch (Exception ex)
            {
                // Save failure means the user's custom-mode edits were lost; surface it to the trace log rather
                // than swallowing it silently. (CORR-10)
                System.Diagnostics.Trace.WriteLine("CustomModeStorage.Save failed: " + ex);
            }
        }
    }
}
