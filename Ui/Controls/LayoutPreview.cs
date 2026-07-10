using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Media;
using DrawRect = System.Drawing.Rectangle;

namespace TTMulti.Ui.Controls
{
    /// <summary>
    /// A scaled, read-only diagram of the virtual desktop for a <see cref="LayoutPreset"/>: each monitor is a
    /// faint labelled box and every window slot is drawn and numbered exactly as it will apply (computed from
    /// <see cref="LayoutPresetBuilder.BuildSlots"/> and the live monitor bounds). Bind <see cref="Rev"/> to the
    /// preset VM's PreviewRev so the diagram re-renders whenever the preset changes; the slots of the region at
    /// <see cref="SelectedRegionIndex"/> are highlighted.
    /// </summary>
    public sealed class LayoutPreview : FrameworkElement
    {
        private static readonly Brush MonitorFill = Frozen(Color.FromArgb(0x14, 0x80, 0x80, 0x80));
        private static readonly Pen MonitorPen = FrozenPen(Color.FromArgb(0x80, 0x80, 0x80, 0x80), 1);
        private static readonly Brush SlotFill = Frozen(Color.FromArgb(0x22, 0x4C, 0x8B, 0xF5));
        private static readonly Brush SlotFillSel = Frozen(Color.FromArgb(0x55, 0x4C, 0x8B, 0xF5));
        private static readonly Pen SlotPen = FrozenPen(Color.FromArgb(0xFF, 0x4C, 0x8B, 0xF5), 1.5);
        private static readonly Brush LabelBrush = Frozen(Color.FromArgb(0xB0, 0x80, 0x80, 0x80));
        private static readonly Brush NumberBrush = Brushes.White;
        private static readonly Typeface Face = new Typeface("Segoe UI");

        public static readonly DependencyProperty PresetProperty = DependencyProperty.Register(
            nameof(Preset), typeof(LayoutPreset), typeof(LayoutPreview),
            new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

        public static readonly DependencyProperty SelectedRegionIndexProperty = DependencyProperty.Register(
            nameof(SelectedRegionIndex), typeof(int), typeof(LayoutPreview),
            new FrameworkPropertyMetadata(-1, FrameworkPropertyMetadataOptions.AffectsRender));

        public static readonly DependencyProperty RevProperty = DependencyProperty.Register(
            nameof(Rev), typeof(int), typeof(LayoutPreview),
            new FrameworkPropertyMetadata(0, FrameworkPropertyMetadataOptions.AffectsRender));

        public LayoutPreset Preset { get => (LayoutPreset)GetValue(PresetProperty); set => SetValue(PresetProperty, value); }
        public int SelectedRegionIndex { get => (int)GetValue(SelectedRegionIndexProperty); set => SetValue(SelectedRegionIndexProperty, value); }
        public int Rev { get => (int)GetValue(RevProperty); set => SetValue(RevProperty, value); }

        protected override void OnRender(DrawingContext dc)
        {
            double w = ActualWidth, h = ActualHeight;
            if (w <= 0 || h <= 0) return;

            var monitors = Win32.GetAllMonitorWorkAreas()
                .Select(r => new DrawRect(r.Left, r.Top, r.Right - r.Left, r.Bottom - r.Top)).ToList();
            var slots = Preset != null ? LayoutPresetBuilder.BuildSlots(Preset) : new List<SlotApplyInfo>();

            // Virtual bounding box across monitors and slots.
            var boxes = monitors.Concat(slots.Select(s => s.Rect)).Where(r => r.Width > 0 && r.Height > 0).ToList();
            if (boxes.Count == 0)
            {
                DrawCentered(dc, "No monitors detected", w, h);
                return;
            }
            int minX = boxes.Min(r => r.Left), minY = boxes.Min(r => r.Top);
            int maxX = boxes.Max(r => r.Right), maxY = boxes.Max(r => r.Bottom);
            double bw = Math.Max(1, maxX - minX), bh = Math.Max(1, maxY - minY);

            const double pad = 6;
            double scale = Math.Min((w - 2 * pad) / bw, (h - 2 * pad) / bh);
            if (scale <= 0 || double.IsInfinity(scale)) return;
            double offX = pad + (w - 2 * pad - bw * scale) / 2;
            double offY = pad + (h - 2 * pad - bh * scale) / 2;

            Rect Map(DrawRect r) => new Rect(
                offX + (r.Left - minX) * scale, offY + (r.Top - minY) * scale,
                Math.Max(0, r.Width * scale), Math.Max(0, r.Height * scale));

            double dpi = VisualTreeHelper.GetDpi(this).PixelsPerDip;

            // Monitors.
            for (int i = 0; i < monitors.Count; i++)
            {
                var mr = Map(monitors[i]);
                dc.DrawRectangle(MonitorFill, MonitorPen, mr);
                var label = new FormattedText("Monitor " + (i + 1), CultureInfo.InvariantCulture,
                    FlowDirection.LeftToRight, Face, 11, LabelBrush, dpi);
                if (label.Width + 6 < mr.Width && label.Height + 4 < mr.Height)
                    dc.DrawText(label, new Point(mr.X + 4, mr.Y + 3));
            }

            // Slots, numbered, with the selected region highlighted.
            var slotRegion = MapSlotsToRegions(Preset, slots.Count);
            for (int i = 0; i < slots.Count; i++)
            {
                var sr = Map(slots[i].Rect);
                if (sr.Width < 2 || sr.Height < 2) continue;
                bool selected = SelectedRegionIndex >= 0 && i < slotRegion.Count && slotRegion[i] == SelectedRegionIndex;
                var inset = new Rect(sr.X + 1.5, sr.Y + 1.5, Math.Max(0, sr.Width - 3), Math.Max(0, sr.Height - 3));
                dc.DrawRectangle(selected ? SlotFillSel : SlotFill, SlotPen, inset);

                double fontSize = Math.Min(inset.Height * 0.5, 22);
                if (fontSize >= 8)
                {
                    var num = new FormattedText((i + 1).ToString(), CultureInfo.InvariantCulture,
                        FlowDirection.LeftToRight, Face, fontSize, NumberBrush, dpi);
                    dc.DrawText(num, new Point(inset.X + (inset.Width - num.Width) / 2, inset.Y + (inset.Height - num.Height) / 2));
                }
            }
        }

        /// <summary>Which region (0-based) each 1-based slot belongs to, following BuildSlots' region order.</summary>
        private static List<int> MapSlotsToRegions(LayoutPreset preset, int slotCount)
        {
            var map = new List<int>(slotCount);
            if (preset?.Regions == null) return map;
            for (int ri = 0; ri < preset.Regions.Count; ri++)
            {
                var region = preset.Regions[ri];
                int count = Math.Max(1, region.Rows) * Math.Max(1, region.Cols);
                for (int c = 0; c < count && map.Count < slotCount; c++)
                    map.Add(ri);
            }
            return map;
        }

        private void DrawCentered(DrawingContext dc, string text, double w, double h)
        {
            double dpi = VisualTreeHelper.GetDpi(this).PixelsPerDip;
            var t = new FormattedText(text, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, Face, 12, LabelBrush, dpi);
            dc.DrawText(t, new Point((w - t.Width) / 2, (h - t.Height) / 2));
        }

        private static Brush Frozen(Color c) { var b = new SolidColorBrush(c); b.Freeze(); return b; }
        private static Pen FrozenPen(Color c, double thickness) { var p = new Pen(Frozen(c), thickness); p.Freeze(); return p; }
    }
}
