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
    /// Displays the group number and an icon to indicate that the multicontroller is active.
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

        private int groupNumber;

        /// <summary>
        /// The window group number displayed on the top left of the window.
        /// </summary>
        public int GroupNumber
        {
            get => groupNumber;
            set
            {
                if (groupNumber != value)
                {
                    groupNumber = value;
                    this.Invalidate();
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
                    this.Invalidate();
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
                    this.Invalidate();
                }
            }
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

        private Font textFont = new Font(FontFamily.GenericSansSerif, 10, FontStyle.Regular);

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

            if (DrawBorder)
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
            else if (ShowGroupNumber)
            {
                e.Graphics.TextRenderingHint = System.Drawing.Text.TextRenderingHint.SingleBitPerPixel;
                e.Graphics.DrawString(GroupNumber.ToString(), textFont, Brushes.White, 12, 160);
            }

            if (ShowFakeCursor)
            {
                var drawRect = new Rectangle(FakeCursorPosition.X, FakeCursorPosition.Y, CursorSize, CursorSize);
                e.Graphics.DrawImage(FakeCursorIsInvalid ? fakeCursorImageInvalid : fakeCursorImage, drawRect);
            }
        }
    }
}
