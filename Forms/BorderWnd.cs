using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Windows.Forms;
using System.Drawing.Imaging;
using System.IO;

namespace TTMulti.Forms
{
    /// <summary>
    /// This window is used to display a border around Toontown windows that are controlled
    /// by the multicontroller. The border is drawn manually.
    /// In Switching Mode it also draws a large centred number to identify each window.
    /// Displays a fake cursor to signify that mouse events will be replicated as well.
    /// </summary>
    internal partial class BorderWnd : Form, IWin32Window
    {
        private Color _borderColor = Color.Black;

        /// <summary>
        /// The color of the border displayed over a Toontown window.
        /// </summary>
        internal Color BorderColor
        {
            get => _borderColor;
            set
            {
                if (_borderColor == value)
                    return;
                _borderColor = value;
                Redraw();
            }
        }

        private int _borderWidth = 5;

        /// <summary>
        /// The width of the border displayed over a Toontown window.
        /// </summary>
        public int BorderWidth
        {
            get => _borderWidth;
            set
            {
                if (_borderWidth == value)
                    return;
                _borderWidth = value;
                Redraw();
            }
        }

        private int _cornerRadius;

        /// <summary>Corner radius of the border, in pixels. 0 = square corners (the default look).</summary>
        public int CornerRadius
        {
            get => _cornerRadius;
            set
            {
                if (_cornerRadius == value)
                    return;
                _cornerRadius = value;
                Redraw();
            }
        }

        private int groupNumber;

        /// <summary>
        /// The window's group number. Drawn on the overlay when <see cref="ShowGroupNumber"/> is set, and used to
        /// derive the Switching Mode number.
        /// </summary>
        public int GroupNumber
        {
            get => groupNumber;
            set
            {
                if (groupNumber != value)
                {
                    groupNumber = value;
                    Redraw();
                }
            }
        }

        private bool showGroupNumber = false;

        /// <summary>
        /// Whether to show the window group number or not.
        /// </summary>
        public bool ShowGroupNumber
        {
            get => showGroupNumber;
            set
            {
                if (showGroupNumber != value)
                {
                    showGroupNumber = value;
                    Redraw();
                }
            }
        }

        private bool _drawBorder = true;

        /// <summary>
        /// Whether to draw the coloured border. When false the overlay renders only the group number (used to
        /// keep a window's group number visible while it isn't actively controlled), leaving the frame transparent.
        /// </summary>
        public bool DrawBorder
        {
            get => _drawBorder;
            set
            {
                if (_drawBorder != value)
                {
                    _drawBorder = value;
                    Redraw();
                }
            }
        }

        private const int CursorSize = 44; // arrow (36) + logo badge overlapping the bottom-right corner

        private bool _showFakeCursor;

        /// <summary>
        /// Enable showing a fake cursor to signify that mouse events are being replicated.
        /// </summary>
        internal bool ShowFakeCursor
        {
            get => _showFakeCursor;
            set
            {
                if (_showFakeCursor != value)
                {
                    _showFakeCursor = value;
                    Redraw();
                }
            }
        }

        private Point _fakeCursorPosition;

        /// <summary>
        /// The position of the fake cursor.
        /// </summary>
        internal Point FakeCursorPosition
        {
            get => _fakeCursorPosition;
            set
            {
                if (_fakeCursorPosition != value)
                {
                    _fakeCursorPosition = value;
                    if (_showFakeCursor)
                        Redraw();
                }
            }
        }

        /// <summary>
        /// Atomically update both show-state and position in a single repaint cycle.
        /// </summary>
        internal void UpdateFakeCursor(bool show, Point position)
        {
            bool posChanged = _fakeCursorPosition != position;
            bool showChanged = _showFakeCursor != show;
            if (!posChanged && !showChanged) return;

            _fakeCursorPosition = position;
            _showFakeCursor = show;
            Redraw();
        }

        private bool _fakeCursorIsInvalid;

        /// <summary>
        /// Whether to display an invalid fake cursor, signifying that the size of the 
        /// window doesn't match the source of the mouse events.
        /// </summary>
        internal bool FakeCursorIsInvalid
        {
            get => _fakeCursorIsInvalid;
            set
            {
                if (_fakeCursorIsInvalid != value)
                {
                    _fakeCursorIsInvalid = value;
                    Redraw();
                }
            }
        }

        /// <summary>
        /// Overrides the default style so that the window is transparent to clicks and borderless.
        /// </summary>
        protected override CreateParams CreateParams
        {
            get
            {
                CreateParams createParams = base.CreateParams;
                createParams.ExStyle |= Win32.WS_EX_LAYERED | Win32.WS_EX_TRANSPARENT | Win32.WS_EX_TOOLWINDOW;

                return createParams;
            }
        }

        /// <summary>
        /// Allows the window to be shown without activating it. By default, the window is activated
        /// when show which would disrupt operation of the multicontroller.
        /// </summary>
        protected override bool ShowWithoutActivation => true;

        // The replicated-mouse cursor: the Toontown arrow (top-left, so its tip stays the hotspot) with the app
        // logo badge in the bottom-right. Built once and shared; both PNGs already carry per-pixel alpha.
        private static readonly Bitmap fakeCursorImage = BuildFakeCursor(invalid: false);
        private static readonly Bitmap fakeCursorImageInvalid = BuildFakeCursor(invalid: true);

        private static Bitmap BuildFakeCursor(bool invalid)
        {
            const int arrow = 36;   // toontown-cursor.png native size, drawn 1:1 at the top-left
            const int badge = 22;   // logo badge in the bottom-right corner
            var bmp = new Bitmap(CursorSize, CursorSize, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
            using (var g = Graphics.FromImage(bmp))
            {
                g.CompositingQuality = System.Drawing.Drawing2D.CompositingQuality.HighQuality;
                g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
                g.PixelOffsetMode = System.Drawing.Drawing2D.PixelOffsetMode.HighQuality;
                g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
                g.Clear(Color.Transparent);

                using (var arrowImg = Properties.Resources.toontowncursor)
                    g.DrawImage(arrowImg, new Rectangle(0, 0, arrow, arrow));

                var badgeRect = new Rectangle(CursorSize - badge, CursorSize - badge, badge, badge);
                using (var logo = Properties.Resources.iconnew)
                    g.DrawImage(logo, badgeRect);

                if (invalid)
                {
                    // Red circle-slash over the badge to signal a source/target size mismatch (the old "x" cursor).
                    using (var pen = new Pen(Color.FromArgb(235, 220, 30, 30), 3f))
                    {
                        g.DrawEllipse(pen, badgeRect.X + 1, badgeRect.Y + 1, badge - 3, badge - 3);
                        g.DrawLine(pen, badgeRect.X + 4, badgeRect.Bottom - 4, badgeRect.Right - 4, badgeRect.Y + 4);
                    }
                }
            }
            return bmp;
        }

        private bool _switchingMode = false;
        private int _switchingNumber = 0;
        private bool _switchingSelected = false;
        private bool _switchingSwitched = false;
        private bool _switchingMarkedForRemoval = false;
        private bool _isDormant = false;

        /// <summary>
        /// Whether switching mode is active
        /// </summary>
        internal bool SwitchingMode
        {
            get => _switchingMode;
            set
            {
                if (_switchingMode != value)
                {
                    _switchingMode = value;
                    Redraw();
                }
            }
        }

        /// <summary>
        /// The number to display in switching mode (1, 2, 3, etc.)
        /// </summary>
        internal int SwitchingNumber
        {
            get => _switchingNumber;
            set
            {
                if (_switchingNumber != value)
                {
                    _switchingNumber = value;
                    Redraw();
                }
            }
        }

        /// <summary>
        /// Whether this window is selected in switching mode (yellow highlight)
        /// </summary>
        internal bool SwitchingSelected
        {
            get => _switchingSelected;
            set
            {
                if (_switchingSelected != value)
                {
                    _switchingSelected = value;
                    Redraw();
                }
            }
        }

        /// <summary>
        /// Whether this window has been switched in switching mode (orange highlight)
        /// </summary>
        internal bool SwitchingSwitched
        {
            get => _switchingSwitched;
            set
            {
                if (_switchingSwitched != value)
                {
                    _switchingSwitched = value;
                    Redraw();
                }
            }
        }

        /// <summary>
        /// Whether this window is marked for removal in switching mode (black highlight)
        /// </summary>
        internal bool SwitchingMarkedForRemoval
        {
            get => _switchingMarkedForRemoval;
            set
            {
                if (_switchingMarkedForRemoval != value)
                {
                    _switchingMarkedForRemoval = value;
                    Redraw();
                }
            }
        }

        /// <summary>
        /// Whether this window is set Dormant (not receiving forwarded input). Persistent — kept in sync
        /// with the controller by ToontownController.Refresh, not just during switching mode — so the moon marker
        /// below can render in every mode.
        /// </summary>
        internal bool IsDormant
        {
            get => _isDormant;
            set
            {
                if (_isDormant != value)
                {
                    _isDormant = value;
                    Redraw();
                }
            }
        }

        public BorderWnd()
        {
            InitializeComponent();
        }

        // The overlay is a true per-pixel-alpha layered window: its pixels come entirely from UpdateLayeredWindow,
        // not from WM_PAINT, so nothing is painted the normal way.
        protected override void OnPaintBackground(PaintEventArgs e) { }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            Redraw();
        }

        protected override void OnSizeChanged(EventArgs e)
        {
            base.OnSizeChanged(e);
            Redraw();
        }

        protected override void OnVisibleChanged(EventArgs e)
        {
            base.OnVisibleChanged(e);
            if (Visible) Redraw();
        }

        private Bitmap _surface;
        private volatile bool _renderQueued;

        /// <summary>
        /// Requests a re-render. Marshals onto the UI thread and coalesces a burst of state changes into a single
        /// render on the next message-loop iteration, mirroring how Invalidate()/WM_PAINT used to behave. This
        /// matters because the border's appearance comes from two independently-set sources (the stored BorderColor
        /// and the switching-mode flags), and the switching flags are driven from a background timer thread. Doing
        /// the UpdateLayeredWindow render synchronously in each setter would touch GDI/window state off the UI
        /// thread (so it would not composite) and would paint half-settled intermediate states, which is what left
        /// stale borders on screen. Deferring to one render that reads the final settled state fixes both.
        /// </summary>
        private void Redraw()
        {
            if (!IsHandleCreated || IsDisposed || Disposing) return;
            if (_renderQueued) return;
            _renderQueued = true;
            try
            {
                BeginInvoke((Action)(() =>
                {
                    _renderQueued = false;
                    RenderNow();
                }));
            }
            catch
            {
                _renderQueued = false; // handle went away between the guard and BeginInvoke
            }
        }

        /// <summary>Renders the overlay onto its alpha surface and pushes it via UpdateLayeredWindow. UI thread only.</summary>
        private void RenderNow()
        {
            if (!IsHandleCreated || IsDisposed || Disposing) return;

            int w = ClientSize.Width, h = ClientSize.Height;
            if (w <= 0 || h <= 0) return;

            if (_surface == null || _surface.Width != w || _surface.Height != h)
            {
                _surface?.Dispose();
                _surface = new Bitmap(w, h, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
            }

            using (var g = Graphics.FromImage(_surface))
            {
                g.Clear(Color.Transparent);
                DrawContent(g, new Rectangle(0, 0, w, h));
            }

            PushLayeredSurface(_surface);
        }

        /// <summary>Blits the premultiplied ARGB surface onto this layered window, keeping its current position.</summary>
        private void PushLayeredSurface(Bitmap bmp)
        {
            IntPtr screenDc = Win32.GetDC(IntPtr.Zero);
            IntPtr memDc = Win32.CreateCompatibleDC(screenDc);
            IntPtr hBitmap = IntPtr.Zero;
            IntPtr oldBitmap = IntPtr.Zero;
            try
            {
                hBitmap = bmp.GetHbitmap(Color.FromArgb(0)); // premultiplied ARGB, as ULW_ALPHA expects
                oldBitmap = Win32.SelectObject(memDc, hBitmap);

                Size size = new Size(bmp.Width, bmp.Height);
                Point src = Point.Empty;
                Point dst = this.Location; // keep the position ToontownController already set
                var blend = new Win32.BLENDFUNCTION((byte)Win32.AC_SRC_OVER, 0, 255, (byte)Win32.AC_SRC_ALPHA);
                Win32.UpdateLayeredWindow(this.Handle, screenDc, ref dst, ref size, memDc, ref src, 0, ref blend, (uint)Win32.ULW_ALPHA);
            }
            finally
            {
                if (oldBitmap != IntPtr.Zero) Win32.SelectObject(memDc, oldBitmap);
                if (hBitmap != IntPtr.Zero) Win32.DeleteObject(hBitmap);
                Win32.DeleteDC(memDc);
                Win32.ReleaseDC(IntPtr.Zero, screenDc);
            }
        }

        /// <summary>
        /// Renders the border, the Switching Mode number, and the fake cursor onto <paramref name="g"/>, which draws
        /// into a fully transparent alpha surface. Anti-aliasing is used freely: UpdateLayeredWindow composites the
        /// result with real per-pixel alpha, so smooth edges blend against transparency without a colour-key fringe.
        /// </summary>
        private void DrawContent(Graphics g, Rectangle clientRect)
        {
            g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;

            Color borderColor = BorderColor;
            if (SwitchingMode)
            {
                // Priority: Selected (Yellow) > Marked for Removal (Black) > Dormant > Switched (Orange) > Normal (Red)
                if (SwitchingSelected)
                    borderColor = Colors.SwitchingSelected;
                else if (SwitchingMarkedForRemoval)
                    borderColor = Colors.SwitchingMarkedForRemoval;
                else if (IsDormant)
                    borderColor = Colors.SwitchingDormant;
                else if (SwitchingSwitched)
                    borderColor = Colors.SwitchingSwitched;
                else
                    borderColor = Colors.SwitchingMode;
            }
            // Otherwise the stored BorderColor is used (set by ToontownController.Refresh()).

            // Draw the coloured border only while actively controlling this window. DrawBorder is false for the
            // number-only overlay (a switched-away group whose number is kept visible), which leaves the frame clear.
            if (DrawBorder)
            {
                // Border as a ring: the outer rectangle minus an inner (optionally rounded) rectangle. The outer
                // edge stays square, flush with the game window; only the inner edge rounds. One even-odd fill lets
                // the inner curve anti-alias cleanly against the transparent interior. The outer rectangle bleeds
                // 1px past every edge so its AA seam is clipped off-surface (otherwise AA fades the boundary row
                // into a faint transparent gap, most visible along the top edge). Border thickness is unchanged.
                Rectangle outer = Rectangle.Inflate(clientRect, 1, 1);
                Rectangle inner = Rectangle.Inflate(clientRect, -BorderWidth, -BorderWidth);
                using (var ring = new System.Drawing.Drawing2D.GraphicsPath())
                using (var innerPath = RoundedRectPath(inner, CornerRadius))
                using (var brush = new SolidBrush(borderColor))
                {
                    ring.AddRectangle(outer);
                    if (innerPath.PointCount > 0)
                        ring.AddPath(innerPath, false); // FillMode.Alternate (default) punches the hole
                    g.FillPath(brush, ring);
                }
            }

            if (SwitchingMode && SwitchingNumber > 0 && clientRect.Height > 4)
            {
                // Large centred number identifying the window. Drawn as a filled glyph path so its edges get real
                // anti-aliased alpha; plain text rendering does not composite cleanly onto a transparent surface.
                float emSize = clientRect.Height / 1.3f;
                Color numberColor = SwitchingSelected ? Colors.SwitchingSelected
                    : SwitchingMarkedForRemoval ? Colors.SwitchingMarkedForRemoval
                    : IsDormant ? Colors.SwitchingDormant
                    : SwitchingSwitched ? Colors.SwitchingSwitched
                    : Colors.SwitchingMode;
                using (var text = new System.Drawing.Drawing2D.GraphicsPath())
                using (var brush = new SolidBrush(numberColor))
                {
                    text.AddString(SwitchingNumber.ToString(), FontFamily.GenericSansSerif, (int)FontStyle.Bold,
                        emSize, PointF.Empty, StringFormat.GenericDefault);
                    RectangleF b = text.GetBounds();
                    var state = g.Save();
                    g.TranslateTransform((clientRect.Width - b.Width) / 2f - b.X, (clientRect.Height - b.Height) / 2f - b.Y);
                    g.FillPath(brush, text);
                    g.Restore(state);
                }
            }

            // A window set Dormant shows a crescent-moon marker in the top-left corner — where its group number
            // would be — in every mode, so you can tell at a glance which windows are asleep. The crescent is a disc
            // with an offset disc excluded from it; a dark drop shadow keeps it legible on any game background.
            if (IsDormant && clientRect.Height > 4)
            {
                float size = Math.Max(14f, Math.Min(clientRect.Height / 6f, 40f));
                float left = BorderWidth + 6;
                // Nudge the moon down about a marker-height below the top edge so it doesn't blend into the game's
                // top-left HUD element (which sits right in the corner).
                float top = BorderWidth + 6 + size * 1.2f;
                RectangleF disc = new RectangleF(left, top, size, size);
                RectangleF carve = new RectangleF(left + size * 0.42f, top - size * 0.24f, size, size);
                using (var discPath = new System.Drawing.Drawing2D.GraphicsPath())
                using (var carvePath = new System.Drawing.Drawing2D.GraphicsPath())
                using (var moonBrush = new SolidBrush(Colors.SwitchingDormant))
                using (var shadow = new SolidBrush(Color.FromArgb(170, 0, 0, 0)))
                {
                    discPath.AddEllipse(disc);
                    carvePath.AddEllipse(carve);
                    using (var crescent = new Region(discPath))
                    {
                        crescent.Exclude(carvePath);
                        var state = g.Save();
                        g.TranslateTransform(1.5f, 1.5f);
                        g.FillRegion(shadow, crescent);
                        g.Restore(state);
                        g.FillRegion(moonBrush, crescent);
                    }
                }
            }

            // Group number in the top-left corner, when the number-visibility feature asks for it and we are not
            // showing the large Switching Mode number (or the moon marker above). Filled glyph path with a dark
            // shadow so it stays legible on any game background.
            if (ShowGroupNumber && !SwitchingMode && !IsDormant && clientRect.Height > 4)
            {
                float emSize = Math.Max(14f, Math.Min(clientRect.Height / 6f, 40f));
                int pad = BorderWidth + 6;
                using (var text = new System.Drawing.Drawing2D.GraphicsPath())
                using (var shadow = new SolidBrush(Color.FromArgb(170, 0, 0, 0)))
                {
                    text.AddString(GroupNumber.ToString(), FontFamily.GenericSansSerif, (int)FontStyle.Bold,
                        emSize, new PointF(pad, pad), StringFormat.GenericDefault);
                    var state = g.Save();
                    g.TranslateTransform(1.5f, 1.5f);
                    g.FillPath(shadow, text);
                    g.Restore(state);
                    g.FillPath(Brushes.White, text);
                }
            }

            if (ShowFakeCursor)
            {
                var drawRect = new Rectangle(FakeCursorPosition.X, FakeCursorPosition.Y, CursorSize, CursorSize);
                g.DrawImage(FakeCursorIsInvalid ? fakeCursorImageInvalid : fakeCursorImage, drawRect);
            }
        }

        /// <summary>A rounded-rectangle path over <paramref name="bounds"/> (the inner hole of the border ring).
        /// Radius is clamped so it never exceeds the box; an empty box yields an empty path (no hole, so the border
        /// simply fills the whole overlay).</summary>
        private static System.Drawing.Drawing2D.GraphicsPath RoundedRectPath(Rectangle bounds, int radius)
        {
            var path = new System.Drawing.Drawing2D.GraphicsPath();
            if (bounds.Width <= 0 || bounds.Height <= 0)
            {
                return path;
            }
            int d = Math.Min(radius * 2, Math.Min(bounds.Width, bounds.Height));
            if (d <= 0)
            {
                path.AddRectangle(bounds);
                return path;
            }
            path.AddArc(bounds.X, bounds.Y, d, d, 180, 90);
            path.AddArc(bounds.Right - d, bounds.Y, d, d, 270, 90);
            path.AddArc(bounds.Right - d, bounds.Bottom - d, d, d, 0, 90);
            path.AddArc(bounds.X, bounds.Bottom - d, d, d, 90, 90);
            path.CloseFigure();
            return path;
        }
    }
}
