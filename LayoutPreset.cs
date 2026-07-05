using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Text;

namespace TTMulti
{
    /// <summary>
    /// How a region's rectangle is determined: a specific monitor's work area or custom coordinates.
    /// </summary>
    [DataContract]
    public enum LayoutRegionSource
    {
        [EnumMember]
        Monitor = 0,
        [EnumMember]
        Custom = 1
    }

    /// <summary>
    /// Rectangle for layout (serialization-friendly).
    /// </summary>
    [DataContract]
    public class LayoutRect
    {
        [DataMember]
        public int X { get; set; }
        [DataMember]
        public int Y { get; set; }
        [DataMember]
        public int Width { get; set; }
        [DataMember]
        public int Height { get; set; }

        public Rectangle ToRectangle() => new Rectangle(X, Y, Width, Height);
        public static LayoutRect FromRectangle(Rectangle r) => new LayoutRect { X = r.X, Y = r.Y, Width = r.Width, Height = r.Height };
    }

    /// <summary>
    /// One region in a preset: a rectangle (from monitor or custom) and an optional grid (rows × cols with optional weights).
    /// Slots are assigned to grid cells in order; if no grid, the region is one slot.
    /// </summary>
    [DataContract]
    public class LayoutRegion
    {
        [DataMember]
        public LayoutRegionSource Source { get; set; }
        [DataMember]
        public int MonitorIndex { get; set; }
        [DataMember]
        public int CustomX { get; set; }
        [DataMember]
        public int CustomY { get; set; }
        [DataMember]
        public int CustomWidth { get; set; }
        [DataMember]
        public int CustomHeight { get; set; }
        [DataMember]
        public int Rows { get; set; }
        [DataMember]
        public int Cols { get; set; }
        [DataMember]
        public double[] RowWeights { get; set; }
        [DataMember]
        public double[] ColWeights { get; set; }

        static LayoutRegion()
        {
            TypeDescriptor.AddProvider(new LayoutRegionTypeDescriptionProvider(), typeof(LayoutRegion));
        }

        public LayoutRegion()
        {
            Rows = 1;
            Cols = 1;
        }
    }

    /// <summary>
    /// Property order and conditional visibility for LayoutRegion in PropertyGrid:
    /// Source, MonitorIndex, Rows, Cols first; CustomX/Y/Width/Height and RowWeights/ColWeights only when Source is Custom.
    /// </summary>
    internal sealed class LayoutRegionTypeDescriptionProvider : TypeDescriptionProvider
    {
        public LayoutRegionTypeDescriptionProvider() : base(TypeDescriptor.GetProvider(typeof(LayoutRegion))) { }

        public override ICustomTypeDescriptor GetTypeDescriptor(Type objectType, object instance)
        {
            var baseDescriptor = base.GetTypeDescriptor(objectType, instance);
            return new LayoutRegionTypeDescriptor(instance as LayoutRegion, baseDescriptor);
        }
    }

    internal sealed class LayoutRegionTypeDescriptor : CustomTypeDescriptor
    {
        private static readonly string[] PropertyOrder = { "Source", "MonitorIndex", "Rows", "Cols", "CustomX", "CustomY", "CustomWidth", "CustomHeight", "RowWeights", "ColWeights" };
        private static readonly string[] CustomOnlyProps = { "CustomX", "CustomY", "CustomWidth", "CustomHeight", "RowWeights", "ColWeights" };
        private readonly LayoutRegion _instance;

        public LayoutRegionTypeDescriptor(LayoutRegion instance, ICustomTypeDescriptor parent) : base(parent)
        {
            _instance = instance;
        }

        public override PropertyDescriptorCollection GetProperties(Attribute[] attributes)
        {
            var all = base.GetProperties(attributes);
            var list = new List<PropertyDescriptor>();
            foreach (string name in PropertyOrder)
            {
                var pd = all.Find(name, false);
                if (pd == null) continue;
                if (Array.IndexOf(CustomOnlyProps, name) >= 0 && _instance?.Source != LayoutRegionSource.Custom)
                    continue;
                list.Add(pd);
            }
            return new PropertyDescriptorCollection(list.ToArray());
        }
    }

    /// <summary>
    /// Override for a single slot: optional custom rect, optional minimized state.
    /// </summary>
    [DataContract]
    public class SlotOverride
    {
        [DataMember]
        public int SlotIndex { get; set; }
        [DataMember]
        public LayoutRect Rect { get; set; }
        [DataMember]
        public bool? Minimized { get; set; }
    }

    /// <summary>
    /// A named layout preset: regions (with optional grids) define default slot rects; slot overrides customize per-slot rect and minimized state.
    /// </summary>
    [DataContract]
    public class LayoutPreset
    {
        [DataMember]
        public string Name { get; set; }
        [DataMember]
        public int HotkeyCode { get; set; }
        [DataMember]
        public int HotkeyModifiers { get; set; }
        [DataMember]
        public List<LayoutRegion> Regions { get; set; }
        [DataMember]
        public List<SlotOverride> SlotOverrides { get; set; }

        public LayoutPreset()
        {
            Regions = new List<LayoutRegion>();
            SlotOverrides = new List<SlotOverride>();
        }
    }

    /// <summary>
    /// Root container for the layout presets file.
    /// </summary>
    [DataContract]
    public class LayoutPresetFile
    {
        [DataMember]
        public List<LayoutPreset> Presets { get; set; }

        public LayoutPresetFile()
        {
            Presets = new List<LayoutPreset>();
        }
    }

    /// <summary>
    /// Load/save layout presets from a JSON file in the exe directory.
    /// </summary>
    public static class LayoutPresetStorage
    {
        private const string FileName = "layout-presets.json";
        private static readonly DataContractJsonSerializer Serializer = new DataContractJsonSerializer(typeof(LayoutPresetFile),
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
                string path = System.Windows.Forms.Application.ExecutablePath;
                if (!string.IsNullOrEmpty(path))
                    return Path.Combine(Path.GetDirectoryName(path), FileName);
            }
            catch { }
            return Path.Combine(AppDomain.CurrentDomain.BaseDirectory ?? ".", FileName);
        }

        public static LayoutPresetFile Load()
        {
            string path = GetFilePath();
            if (!File.Exists(path))
                return new LayoutPresetFile();
            try
            {
                using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
                {
                    return (LayoutPresetFile)Serializer.ReadObject(fs);
                }
            }
            catch (Exception ex)
            {
                // A corrupt/unreadable file is silently replaced with an empty set here; log it so the data loss is
                // at least diagnosable instead of vanishing without a trace. (CORR-10)
                System.Diagnostics.Trace.WriteLine("LayoutPresetStorage.Load failed: " + ex);
                return new LayoutPresetFile();
            }
        }

        // Cache for LoadCached(), keyed by file path + last-write time. Read-only hot paths (e.g. hotkey
        // registration on focus changes) share this instance; editors call Load() for their own mutable copy.
        private static LayoutPresetFile _cachedFile;
        private static string _cachedPath;
        private static DateTime _cachedWriteTimeUtc;

        /// <summary>
        /// Cached variant of <see cref="Load"/> for hot read paths. Re-reads only when the file's last-write time
        /// changes; returns a SHARED instance, so callers must treat it as read-only.
        /// </summary>
        public static LayoutPresetFile LoadCached()
        {
            string path = GetFilePath();
            if (!File.Exists(path))
            {
                _cachedFile = null;
                return new LayoutPresetFile();
            }
            try
            {
                DateTime writeTime = File.GetLastWriteTimeUtc(path);
                if (_cachedFile != null && _cachedPath == path && _cachedWriteTimeUtc == writeTime)
                    return _cachedFile;

                LayoutPresetFile file = Load();
                _cachedFile = file;
                _cachedPath = path;
                _cachedWriteTimeUtc = writeTime;
                return file;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Trace.WriteLine("LayoutPresetStorage.LoadCached failed: " + ex);
                return new LayoutPresetFile();
            }
        }

        public static void Save(LayoutPresetFile data)
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
                _cachedFile = null; // invalidate so LoadCached() re-reads the just-written file
            }
            catch (Exception ex)
            {
                // Save failure means the user's layout presets were lost; surface it to the trace log rather than
                // swallowing it silently. (CORR-10)
                System.Diagnostics.Trace.WriteLine("LayoutPresetStorage.Save failed: " + ex);
            }
        }
    }

    /// <summary>
    /// Result for one slot: rectangle (for position/size) and whether the window should be minimized.
    /// </summary>
    public struct SlotApplyInfo
    {
        public Rectangle Rect;
        public bool Minimized;
    }

    /// <summary>
    /// Builds the list of slot rects and minimized flags from a preset (resolves monitors, grids, overrides).
    /// </summary>
    public static class LayoutPresetBuilder
    {
        public static List<SlotApplyInfo> BuildSlots(LayoutPreset preset)
        {
            var slots = new List<SlotApplyInfo>();
            if (preset?.Regions == null) return slots;

            var overrideByIndex = (preset.SlotOverrides ?? new List<SlotOverride>())
                .Where(o => o.SlotIndex >= 1)
                .ToDictionary(o => o.SlotIndex, o => o);

            foreach (var region in preset.Regions)
            {
                Rectangle regionRect = ResolveRegionRect(region);
                int rows = Math.Max(1, region.Rows);
                int cols = Math.Max(1, region.Cols);
                var cells = ComputeGridCells(regionRect, rows, cols, region.RowWeights, region.ColWeights);
                foreach (var cell in cells)
                {
                    int slotIndex = slots.Count + 1;
                    var info = new SlotApplyInfo { Rect = cell, Minimized = false };
                    if (overrideByIndex.TryGetValue(slotIndex, out var ov))
                    {
                        if (ov.Rect != null)
                            info.Rect = ov.Rect.ToRectangle();
                        if (ov.Minimized.HasValue)
                            info.Minimized = ov.Minimized.Value;
                    }
                    slots.Add(info);
                }
            }

            return slots;
        }

        /// <summary>
        /// Resolves a region to its current screen rectangle (for overlay editor and display).
        /// </summary>
        public static Rectangle GetRegionRect(LayoutRegion region)
        {
            return ResolveRegionRect(region);
        }

        private static Rectangle ResolveRegionRect(LayoutRegion region)
        {
            if (region.Source == LayoutRegionSource.Monitor)
            {
                var work = Win32.GetMonitorWorkAreaByIndex(region.MonitorIndex);
                if (work.HasValue)
                {
                    var r = work.Value;
                    return new Rectangle(r.Left, r.Top, r.Right - r.Left, r.Bottom - r.Top);
                }
                return new Rectangle(0, 0, 1920, 1080);
            }
            return new Rectangle(region.CustomX, region.CustomY, region.CustomWidth, region.CustomHeight);
        }

        private static List<Rectangle> ComputeGridCells(Rectangle r, int rows, int cols, double[] rowWeights, double[] colWeights)
        {
            var result = new List<Rectangle>();
            if (rows <= 0 || cols <= 0) return result;

            double[] rw = NormalizeWeights(rowWeights, rows);
            double[] cw = NormalizeWeights(colWeights, cols);

            int y = r.Y;
            for (int i = 0; i < rows; i++)
            {
                int cellHeight = (int)Math.Round(r.Height * rw[i]);
                if (i == rows - 1) cellHeight = r.Bottom - y;
                int x = r.X;
                for (int j = 0; j < cols; j++)
                {
                    int cellWidth = (int)Math.Round(r.Width * cw[j]);
                    if (j == cols - 1) cellWidth = r.Right - x;
                    result.Add(new Rectangle(x, y, cellWidth, cellHeight));
                    x += cellWidth;
                }
                y += cellHeight;
            }
            return result;
        }

        private static double[] NormalizeWeights(double[] weights, int count)
        {
            if (weights != null && weights.Length == count)
            {
                double sum = weights.Sum();
                if (sum > 0) return weights.Select(w => w / sum).ToArray();
            }
            double equal = 1.0 / count;
            return Enumerable.Range(0, count).Select(_ => equal).ToArray();
        }
    }
}
