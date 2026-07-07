using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Windows.Media.Imaging;
using WMColor = System.Windows.Media.Color;

namespace TTMulti.Ui.Controls
{
    /// <summary>
    /// Renders the app logo by duotone-recolouring the black/white yin-yang source art
    /// (<c>Resources/icon-new.png</c>): dark pixels take the <c>dark</c> colour, light pixels the <c>light</c>
    /// colour, and anti-aliased greys blend smoothly between the two, with per-pixel alpha preserved. This
    /// replaces the old vector <c>CatHead</c> logo while keeping the same two-colour theming.
    /// </summary>
    internal static class LogoImages
    {
        private static readonly object _lock = new object();
        private static WMColor _cachedDark, _cachedLight;
        private static Bitmap _recolored;        // full-resolution recoloured source, cached by (dark, light)
        private static Rectangle _contentBounds;  // tight non-transparent bounds of the art (source has wide margins)

        // Fraction of the icon left as breathing room around the artwork after cropping its transparent margins.
        // Kept at zero so the (already tightly-cropped) source fills the icon; the art carries its own margin.
        private const float Padding = 0f;

        /// <summary>
        /// A square <see cref="BitmapSource"/> of the logo recoloured with <paramref name="dark"/> (the black
        /// cat) and <paramref name="light"/> (the white cat), scaled to <paramref name="size"/> px. The result
        /// is frozen, so it is safe to use across threads.
        /// </summary>
        internal static BitmapSource Render(WMColor dark, WMColor light, int size)
        {
            if (size < 1) size = 1;
            lock (_lock)
            {
                Bitmap src = EnsureRecolored(dark, light);

                // Crop the source's wide transparent margins and scale the artwork to fill the icon (preserving
                // aspect, centred, with a little padding) so it does not read tiny in a taskbar/title-bar slot.
                Rectangle bb = _contentBounds;
                int pad = (int)(size * Padding);
                int avail = Math.Max(1, size - 2 * pad);
                float scale = Math.Min((float)avail / bb.Width, (float)avail / bb.Height);
                float dw = bb.Width * scale, dh = bb.Height * scale;
                var dest = new RectangleF((size - dw) / 2f, (size - dh) / 2f, dw, dh);

                using (var scaled = new Bitmap(size, size, PixelFormat.Format32bppArgb))
                {
                    using (var g = Graphics.FromImage(scaled))
                    {
                        g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                        g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                        g.CompositingQuality = CompositingQuality.HighQuality;
                        g.Clear(Color.Transparent);
                        g.DrawImage(src, dest, (RectangleF)bb, GraphicsUnit.Pixel);
                    }
                    return WpfImaging.ToBitmapSource(scaled); // returns a frozen BitmapSource
                }
            }
        }

        private static Bitmap EnsureRecolored(WMColor dark, WMColor light)
        {
            if (_recolored != null && _cachedDark == dark && _cachedLight == light)
                return _recolored;

            _recolored?.Dispose();
            using (var src = Properties.Resources.iconnew)
                _recolored = BuildDuotone(src, dark, light);
            _cachedDark = dark;
            _cachedLight = light;
            return _recolored;
        }

        private static Bitmap BuildDuotone(Bitmap source, WMColor dark, WMColor light)
        {
            int w = source.Width, h = source.Height;
            var result = new Bitmap(w, h, PixelFormat.Format32bppArgb);

            // Lerp table dark -> light indexed by luminance (0..255), computed once per recolour.
            byte[] rTab = new byte[256], gTab = new byte[256], bTab = new byte[256];
            for (int i = 0; i < 256; i++)
            {
                float t = i / 255f;
                rTab[i] = (byte)(dark.R + (light.R - dark.R) * t);
                gTab[i] = (byte)(dark.G + (light.G - dark.G) * t);
                bTab[i] = (byte)(dark.B + (light.B - dark.B) * t);
            }

            var rect = new Rectangle(0, 0, w, h);
            BitmapData sd = source.LockBits(rect, ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
            BitmapData dd = result.LockBits(rect, ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);
            try
            {
                int stride = Math.Abs(sd.Stride);
                int bytes = stride * h;
                byte[] buf = new byte[bytes];
                Marshal.Copy(sd.Scan0, buf, 0, bytes);
                int minX = w, minY = h, maxX = -1, maxY = -1;
                for (int p = 0; p < bytes; p += 4)
                {
                    // BGRA byte order; leave fully-transparent pixels as (0,0,0,0).
                    if (buf[p + 3] == 0) { buf[p] = buf[p + 1] = buf[p + 2] = 0; continue; }
                    int lum = (buf[p + 2] * 77 + buf[p + 1] * 151 + buf[p] * 28) >> 8; // 0.30R + 0.59G + 0.11B
                    buf[p] = bTab[lum];
                    buf[p + 1] = gTab[lum];
                    buf[p + 2] = rTab[lum];
                    // alpha (buf[p + 3]) preserved

                    if (buf[p + 3] > 12) // track the tight content bounds (ignore near-transparent AA fringe)
                    {
                        int col = (p % stride) >> 2, row = p / stride;
                        if (col < minX) minX = col; if (col > maxX) maxX = col;
                        if (row < minY) minY = row; if (row > maxY) maxY = row;
                    }
                }
                Marshal.Copy(buf, 0, dd.Scan0, bytes);
                _contentBounds = (maxX >= minX && maxY >= minY)
                    ? new Rectangle(minX, minY, maxX - minX + 1, maxY - minY + 1)
                    : new Rectangle(0, 0, w, h);
            }
            finally
            {
                source.UnlockBits(sd);
                result.UnlockBits(dd);
            }
            return result;
        }
    }
}
