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
                this.Invalidate();
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
                this.Invalidate();
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
                this.Invalidate();
            }
        }

        private int groupNumber;

        /// <summary>
        /// The window's group number. Used to derive the Switching Mode number; not drawn on its own.
        /// </summary>
        public int GroupNumber
        {
            get => groupNumber;
            set => groupNumber = value;
        }

        private const int CursorSize = 32;
        private const int CursorPad = 12;

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
                    Invalidate(fakeCursorRect);
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
                    if (_showFakeCursor)
                        Invalidate(fakeCursorRect); // erase from old position
                    _fakeCursorPosition = value;
                    fakeCursorRect = new Rectangle(value.X - CursorPad, value.Y - CursorPad, CursorSize + CursorPad * 2, CursorSize + CursorPad * 2);
                    if (_showFakeCursor)
                        Invalidate(fakeCursorRect); // draw at new position
                }
            }
        }

        /// <summary>
        /// Atomically update both show-state and position in a single repaint cycle,
        /// avoiding the flicker caused by two separate Invalidate calls.
        /// </summary>
        internal void UpdateFakeCursor(bool show, Point position)
        {
            bool posChanged = _fakeCursorPosition != position;
            bool showChanged = _showFakeCursor != show;
            if (!posChanged && !showChanged) return;

            Invalidate(fakeCursorRect); // erase old position
            _fakeCursorPosition = position;
            _showFakeCursor = show;
            if (show)
            {
                fakeCursorRect = new Rectangle(position.X - CursorPad, position.Y - CursorPad, CursorSize + CursorPad * 2, CursorSize + CursorPad * 2);
                Invalidate(fakeCursorRect); // draw at new position
            }
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
                    Invalidate(fakeCursorRect);
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

        private Bitmap fakeCursorImage = Properties.Resources.dupcursor,
            fakeCursorImageInvalid = Properties.Resources.dupcursorx;

        // Keep track of the last region where the cursor was draw so it can be invalidated quicker
        Rectangle fakeCursorRect;

        private bool _switchingMode = false;
        private int _switchingNumber = 0;
        private bool _switchingSelected = false;
        private bool _switchingSwitched = false;
        private bool _switchingMarkedForRemoval = false;

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
                    this.Invalidate();
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
                    this.Invalidate();
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
                    this.Invalidate();
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
                    this.Invalidate();
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
                    this.Invalidate();
                }
            }
        }

        public BorderWnd()
        {
            InitializeComponent();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);

            Color borderColor = BorderColor;
            if (SwitchingMode)
            {
                // Priority: Selected (Yellow) > Marked for Removal (Black) > Switched (Orange) > Normal (Red)
                if (SwitchingSelected)
                {
                    borderColor = Colors.SwitchingSelected; // Yellow for selected windows
                }
                else if (SwitchingMarkedForRemoval)
                {
                    borderColor = Colors.SwitchingMarkedForRemoval; // Black for windows marked for removal
                }
                else if (SwitchingSwitched)
                {
                    borderColor = Colors.SwitchingSwitched; // Orange for switched windows
                }
                else
                {
                    borderColor = Colors.SwitchingMode; // Red for normal switching mode
                }
            }
            // When not in switching mode, use stored BorderColor (normal mode); no persistence of selected/switched colors
            // (BorderColor is updated by ToontownController.Refresh() when exiting switching mode)

            if (CornerRadius > 0)
            {
                // Round the INNER edge only: fill the whole overlay with the border colour, then punch a rounded
                // transparent hole. The outer edge stays square, flush with the game window's own square corners.
                // No anti-aliasing on purpose: AA against the chroma-key background leaves a faint coloured fringe
                // that lingers as a jarring outline, so we keep hard, fully-keyed edges instead.
                Rectangle inner = Rectangle.Inflate(this.ClientRectangle, -BorderWidth, -BorderWidth);
                var prevMode = e.Graphics.SmoothingMode;
                e.Graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.None;
                using (var band = new SolidBrush(borderColor))
                {
                    e.Graphics.FillRectangle(band, this.ClientRectangle);
                }
                using (var hole = new SolidBrush(Colors.ChromaKey))
                using (var holePath = RoundedRectPath(inner, CornerRadius))
                {
                    e.Graphics.FillPath(hole, holePath);
                }
                e.Graphics.SmoothingMode = prevMode;
            }
            else
            {
                ControlPaint.DrawBorder(e.Graphics, this.ClientRectangle,
                    borderColor, BorderWidth, ButtonBorderStyle.Solid,
                    borderColor, BorderWidth, ButtonBorderStyle.Solid,
                    borderColor, BorderWidth, ButtonBorderStyle.Solid,
                    borderColor, BorderWidth, ButtonBorderStyle.Solid);
            }

            if (SwitchingMode && SwitchingNumber > 0)
            {
                // Draw large number in center of window
                // Font size is slightly larger than half of window height
                // Account for DPI scaling to prevent oversized text on high-DPI displays
                float dpiScale = e.Graphics.DpiX / 96.0f; // 96 is standard DPI
                float fontSize = (this.ClientRectangle.Height / 1.7f) / dpiScale;
                using (Font switchingModeFont = new Font(FontFamily.GenericSansSerif, fontSize, FontStyle.Bold))
                {
                    e.Graphics.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAlias;
                    string numberText = SwitchingNumber.ToString();
                    SizeF textSize = e.Graphics.MeasureString(numberText, switchingModeFont);
                    float x = (this.ClientRectangle.Width - textSize.Width) / 2;
                    float y = (this.ClientRectangle.Height - textSize.Height) / 2;
                    
                    // Text color matches border color using Colors properties
                    Brush textBrush;
                    if (SwitchingSelected)
                    {
                        textBrush = new SolidBrush(Colors.SwitchingSelected);
                    }
                    else if (SwitchingMarkedForRemoval)
                    {
                        textBrush = new SolidBrush(Colors.SwitchingMarkedForRemoval);
                    }
                    else if (SwitchingSwitched)
                    {
                        textBrush = new SolidBrush(Colors.SwitchingSwitched);
                    }
                    else
                    {
                        textBrush = new SolidBrush(Colors.SwitchingMode);
                    }
                    
                    using (textBrush)
                    {
                        e.Graphics.DrawString(numberText, switchingModeFont, textBrush, x, y);
                    }
                }
            }

            if (ShowFakeCursor)
            {
                var drawRect = new Rectangle(FakeCursorPosition.X, FakeCursorPosition.Y, CursorSize, CursorSize);
                e.Graphics.DrawImage(FakeCursorIsInvalid ? fakeCursorImageInvalid : fakeCursorImage, drawRect);
            }
        }

        /// <summary>A rounded-rectangle path over <paramref name="bounds"/> (the transparent interior to punch out).
        /// Radius is clamped so it never exceeds the box; an empty box yields an empty path (nothing punched, so the
        /// border simply fills the whole overlay).</summary>
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
