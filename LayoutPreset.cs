using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

namespace TTMulti
{
    /// <summary>
    /// Represents the mode for setting region bounds
    /// </summary>
    internal enum LayoutRegionMode
    {
        Manual = 0,
        Display = 1
    }

    /// <summary>
    /// Represents a screen region for window placement
    /// </summary>
    internal class LayoutRegion
    {
        public int X { get; set; }
        public int Y { get; set; }
        public int Width { get; set; }
        public int Height { get; set; }
        public LayoutRegionMode Mode { get; set; }
        public int DisplayIndex { get; set; }

        public LayoutRegion()
        {
            X = 0;
            Y = 0;
            Width = 1920;
            Height = 1080;
            Mode = LayoutRegionMode.Manual;
            DisplayIndex = -1;
        }

        public LayoutRegion(int x, int y, int width, int height)
        {
            X = x;
            Y = y;
            Width = width;
            Height = height;
            Mode = LayoutRegionMode.Manual;
            DisplayIndex = -1;
        }

        public override string ToString()
        {
            return $"({X},{Y}) {Width}x{Height}";
        }
    }

    /// <summary>
    /// Represents a window layout preset with grid-based layout and hotkey configuration.
    /// Layout and DPI: The app is system-DPI aware (SetProcessDPIAware). GetWindowRect, SetWindowPos,
    /// and GetMonitorInfo use the same virtualized coordinate space. Display-mode regions are resolved
    /// at apply time via GetMonitorWorkAreaByIndex so work-area bounds match that space (fixes 125%/150% scaling).
    /// Manual regions use stored X,Y,Width,Height as-is (user responsibility to match their display).
    /// Frame thickness is taken from one sample window; if windows span monitors with different DPI, frame may vary slightly.
    /// Window origin: We place windows so the window rect (including frame) stays inside the region—first window at (rx, ry), not (rx - frame.Left, ry - frame.Top).
    /// That avoids the window's top-left landing on another monitor (e.g. -6 on a 100% monitor) which caused DWM to apply the wrong DPI. We inset the tiling area by the frame so client area starts at (rx + frame.Left, ry + frame.Top).
    /// </summary>
    internal class LayoutPreset
    {
        /// <summary>
        /// Whether this preset is enabled
        /// </summary>
        public bool Enabled { get; set; }

        /// <summary>
        /// Number of columns in the grid
        /// </summary>
        public int Columns { get; set; }

        /// <summary>
        /// Number of rows in the grid
        /// </summary>
        public int Rows { get; set; }

        /// <summary>
        /// List of regions to fill with windows (filled sequentially)
        /// </summary>
        public List<LayoutRegion> Regions { get; set; }

        /// <summary>
        /// Hotkey code (Keys enum value)
        /// </summary>
        public int HotkeyCode { get; set; }

        /// <summary>
        /// Hotkey modifiers (Alt, Ctrl, Shift)
        /// </summary>
        public Win32.KeyModifiers HotkeyModifiers { get; set; }

        public LayoutPreset()
        {
            Enabled = false;
            Columns = 4;
            Rows = 2;
            Regions = new List<LayoutRegion> { new LayoutRegion() }; // Start with 1 region
            HotkeyCode = 0;
            HotkeyModifiers = Win32.KeyModifiers.None;
        }

        /// <summary>
        /// Resolves a region to effective bounds (X, Y, Width, Height) in the same coordinate space as GetWindowRect/SetWindowPos.
        /// For Display mode, re-queries the monitor work area via Win32 so layout is correct under DPI scaling (100%, 125%, 150%, etc.).
        /// </summary>
        private static void GetRegionBounds(LayoutRegion region, out int x, out int y, out int width, out int height)
        {
            if (region.Mode == LayoutRegionMode.Display && region.DisplayIndex >= 0)
            {
                var work = Win32.GetMonitorWorkAreaByIndex(region.DisplayIndex);
                if (work.HasValue)
                {
                    var r = work.Value;
                    x = r.Left;
                    y = r.Top;
                    width = r.Right - r.Left;
                    height = r.Bottom - r.Top;
                    return;
                }
            }
            x = region.X;
            y = region.Y;
            width = region.Width;
            height = region.Height;
        }

        /// <summary>
        /// Scale frame thickness from sample monitor DPI to region monitor DPI so sizing/placement are correct when regions span different DPIs.
        /// </summary>
        private static Win32.FrameThickness ScaleFrameForRegion(Win32.FrameThickness frame, uint sampleDpiX, uint sampleDpiY, uint regionDpiX, uint regionDpiY)
        {
            if (sampleDpiX == 0) sampleDpiX = 96;
            if (sampleDpiY == 0) sampleDpiY = 96;
            return new Win32.FrameThickness
            {
                Left = (int)((long)frame.Left * regionDpiX / sampleDpiX),
                Right = (int)((long)frame.Right * regionDpiX / sampleDpiX),
                Top = (int)((long)frame.Top * regionDpiY / sampleDpiY),
                Bottom = (int)((long)frame.Bottom * regionDpiY / sampleDpiY)
            };
        }

        /// <summary>
        /// Calculate window size and positions for the given number of windows using grid layout across multiple regions.
        /// Frame and DPI are taken from the REGION's monitor only. Windows are placed so the window rect stays inside the region
        /// (first window at rx, ry)—we inset the tiling area by the frame so we don't place at (rx - frame.Left) which would put the origin on another monitor.
        /// </summary>
        public (Size[] windowSizes, Point[] positions) CalculateGridLayout(int windowCount, IntPtr sampleWindowHandle)
        {
            if (windowCount == 0 || Columns <= 0 || Rows <= 0 || Regions == null || Regions.Count == 0)
                return (Array.Empty<Size>(), Array.Empty<Point>());

            var frameSample = Win32.GetFrameThickness(sampleWindowHandle);
            var (sampleDpiX, sampleDpiY) = Win32.GetDpiForWindow(sampleWindowHandle);

            List<Point> positions = new List<Point>();
            List<Size> sizes = new List<Size>();
            int windowsPlaced = 0;
            int maxPerRegion = Columns * Rows;

            foreach (var region in Regions)
            {
                if (windowsPlaced >= windowCount)
                    break;

                GetRegionBounds(region, out int rx, out int ry, out int rw, out int rh);
                var (regionDpiX, regionDpiY) = Win32.GetDpiForRegion(region, rx, ry);
                var frame = ScaleFrameForRegion(frameSample, sampleDpiX, sampleDpiY, regionDpiX, regionDpiY);

                // Inset region by frame so window rect stays inside region (origin at rx, ry for first cell, not rx - frame.Left).
                int rwInset = Math.Max(0, rw - frame.Left - frame.Right);
                int rhInset = Math.Max(0, rh - frame.Top - frame.Bottom);
                int clientWidth = rwInset / Columns;
                int clientHeight = rhInset / Rows;
                Size regionWindowSize = new Size(
                    clientWidth + frame.Left + frame.Right,
                    clientHeight + frame.Top + frame.Bottom
                );

                int windowsInRegion = Math.Min(maxPerRegion, windowCount - windowsPlaced);

                for (int i = 0; i < windowsInRegion; i++)
                {
                    int row = i / Columns;
                    int col = i % Columns;

                    // Client area starts at (rx + frame.Left, ry + frame.Top) so window origin is (rx, ry) for first cell.
                    int clientX = rx + frame.Left + (col * clientWidth);
                    int clientY = ry + frame.Top + (row * clientHeight);
                    int windowX = clientX - frame.Left;
                    int windowY = clientY - frame.Top;

                    positions.Add(new Point(windowX, windowY));
                    sizes.Add(regionWindowSize);
                }

                windowsPlaced += windowsInRegion;
            }

            return (sizes.ToArray(), positions.ToArray());
        }

        /// <summary>
        /// Get display name for this preset's hotkey
        /// </summary>
        public string GetHotkeyDisplayName()
        {
            if (HotkeyCode == 0)
            {
                return "None";
            }

            string modifiers = "";
            if ((HotkeyModifiers & Win32.KeyModifiers.Alt) != 0)
                modifiers += "Alt+";
            if ((HotkeyModifiers & Win32.KeyModifiers.Control) != 0)
                modifiers += "Ctrl+";
            if ((HotkeyModifiers & Win32.KeyModifiers.Shift) != 0)
                modifiers += "Shift+";

            Keys key = (Keys)HotkeyCode;
            return modifiers + key.ToString();
        }

        public override string ToString()
        {
            if (!Enabled)
                return "(Disabled)";

            string regionInfo = Regions != null && Regions.Count > 0
                ? Regions[0].ToString()
                : "No regions";
                
            if (Regions != null && Regions.Count > 1)
                regionInfo += $" +{Regions.Count - 1} more";

            return $"{Columns}x{Rows} grid @ {regionInfo} [{GetHotkeyDisplayName()}]";
        }

        /// <summary>
        /// Serialize regions to a string format: "x,y,w,h,mode,displayIndex;x,y,w,h,mode,displayIndex;..."
        /// mode: 0 = Manual, 1 = Display
        /// displayIndex: index of selected display (-1 if Manual)
        /// </summary>
        public static string SerializeRegions(List<LayoutRegion> regions)
        {
            if (regions == null || regions.Count == 0)
                return "0,0,1920,1080,0,-1"; // Default single region (Manual mode)

            return string.Join(";", regions.Select(r => 
            {
                int mode = r.Mode == LayoutRegionMode.Display ? 1 : 0;
                return $"{r.X},{r.Y},{r.Width},{r.Height},{mode},{r.DisplayIndex}";
            }));
        }

        /// <summary>
        /// Deserialize regions from string format: "x,y,w,h;x,y,w,h;..." (old) or "x,y,w,h,mode,displayIndex;..." (new)
        /// </summary>
        public static List<LayoutRegion> DeserializeRegions(string regionsString)
        {
            if (string.IsNullOrWhiteSpace(regionsString))
                return new List<LayoutRegion> { new LayoutRegion() };

            try
            {
                var regions = new List<LayoutRegion>();
                var regionStrings = regionsString.Split(';');

                foreach (var regionStr in regionStrings)
                {
                    var parts = regionStr.Split(',');
                    if (parts.Length >= 4 &&
                        int.TryParse(parts[0], out int x) &&
                        int.TryParse(parts[1], out int y) &&
                        int.TryParse(parts[2], out int w) &&
                        int.TryParse(parts[3], out int h))
                    {
                        var region = new LayoutRegion(x, y, w, h);
                        
                        // Check if new format with mode and display index (6 parts)
                        if (parts.Length >= 6 &&
                            int.TryParse(parts[4], out int mode) &&
                            int.TryParse(parts[5], out int displayIndex))
                        {
                            region.Mode = mode == 1 ? LayoutRegionMode.Display : LayoutRegionMode.Manual;
                            region.DisplayIndex = displayIndex;
                        }
                        // Old format (4 parts) defaults to Manual mode
                        
                        regions.Add(region);
                    }
                }

                return regions.Count > 0 ? regions : new List<LayoutRegion> { new LayoutRegion() };
            }
            catch
            {
                return new List<LayoutRegion> { new LayoutRegion() };
            }
        }

        /// <summary>
        /// Load a layout preset from settings
        /// </summary>
        public static LayoutPreset LoadFromSettings(int presetNumber)
        {
            if (presetNumber < 1 || presetNumber > 4)
                throw new ArgumentOutOfRangeException(nameof(presetNumber), "Preset number must be between 1 and 4");

            var settings = Properties.Settings.Default;
            var preset = new LayoutPreset();

            switch (presetNumber)
            {
                case 1:
                    preset.Enabled = settings.layoutPreset1Enabled;
                    preset.Columns = settings.layoutPreset1Columns;
                    preset.Rows = settings.layoutPreset1Rows;
                    preset.Regions = DeserializeRegions(settings.layoutPreset1Regions);
                    preset.HotkeyCode = settings.layoutPreset1HotkeyCode;
                    preset.HotkeyModifiers = (Win32.KeyModifiers)settings.layoutPreset1HotkeyModifiers;
                    break;
                case 2:
                    preset.Enabled = settings.layoutPreset2Enabled;
                    preset.Columns = settings.layoutPreset2Columns;
                    preset.Rows = settings.layoutPreset2Rows;
                    preset.Regions = DeserializeRegions(settings.layoutPreset2Regions);
                    preset.HotkeyCode = settings.layoutPreset2HotkeyCode;
                    preset.HotkeyModifiers = (Win32.KeyModifiers)settings.layoutPreset2HotkeyModifiers;
                    break;
                case 3:
                    preset.Enabled = settings.layoutPreset3Enabled;
                    preset.Columns = settings.layoutPreset3Columns;
                    preset.Rows = settings.layoutPreset3Rows;
                    preset.Regions = DeserializeRegions(settings.layoutPreset3Regions);
                    preset.HotkeyCode = settings.layoutPreset3HotkeyCode;
                    preset.HotkeyModifiers = (Win32.KeyModifiers)settings.layoutPreset3HotkeyModifiers;
                    break;
                case 4:
                    preset.Enabled = settings.layoutPreset4Enabled;
                    preset.Columns = settings.layoutPreset4Columns;
                    preset.Rows = settings.layoutPreset4Rows;
                    preset.Regions = DeserializeRegions(settings.layoutPreset4Regions);
                    preset.HotkeyCode = settings.layoutPreset4HotkeyCode;
                    preset.HotkeyModifiers = (Win32.KeyModifiers)settings.layoutPreset4HotkeyModifiers;
                    break;
            }

            return preset;
        }

        /// <summary>
        /// Save a layout preset to settings
        /// </summary>
        public void SaveToSettings(int presetNumber)
        {
            if (presetNumber < 1 || presetNumber > 4)
                throw new ArgumentOutOfRangeException(nameof(presetNumber), "Preset number must be between 1 and 4");

            var settings = Properties.Settings.Default;

            switch (presetNumber)
            {
                case 1:
                    settings.layoutPreset1Enabled = Enabled;
                    settings.layoutPreset1Columns = Columns;
                    settings.layoutPreset1Rows = Rows;
                    settings.layoutPreset1Regions = SerializeRegions(Regions);
                    settings.layoutPreset1HotkeyCode = HotkeyCode;
                    settings.layoutPreset1HotkeyModifiers = (int)HotkeyModifiers;
                    break;
                case 2:
                    settings.layoutPreset2Enabled = Enabled;
                    settings.layoutPreset2Columns = Columns;
                    settings.layoutPreset2Rows = Rows;
                    settings.layoutPreset2Regions = SerializeRegions(Regions);
                    settings.layoutPreset2HotkeyCode = HotkeyCode;
                    settings.layoutPreset2HotkeyModifiers = (int)HotkeyModifiers;
                    break;
                case 3:
                    settings.layoutPreset3Enabled = Enabled;
                    settings.layoutPreset3Columns = Columns;
                    settings.layoutPreset3Rows = Rows;
                    settings.layoutPreset3Regions = SerializeRegions(Regions);
                    settings.layoutPreset3HotkeyCode = HotkeyCode;
                    settings.layoutPreset3HotkeyModifiers = (int)HotkeyModifiers;
                    break;
                case 4:
                    settings.layoutPreset4Enabled = Enabled;
                    settings.layoutPreset4Columns = Columns;
                    settings.layoutPreset4Rows = Rows;
                    settings.layoutPreset4Regions = SerializeRegions(Regions);
                    settings.layoutPreset4HotkeyCode = HotkeyCode;
                    settings.layoutPreset4HotkeyModifiers = (int)HotkeyModifiers;
                    break;
            }

            settings.Save();
        }
    }
}

