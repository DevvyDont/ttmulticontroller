using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;
using TTMulti;

namespace TTMulti.Forms
{
    /// <summary>
    /// Fullscreen overlay that shows layout region(s) as bordered rectangles and lets the user
    /// drag edges/corners to resize them. Shows grid lines so users see where windows will be placed.
    /// </summary>
    public class LayoutOverlayForm : Form
    {
        private const int HandleSize = 12;
        private const int MinRegionSize = 80;
        private static readonly Color OverlayBackColor = Color.FromArgb(140, 0, 0, 0);
        private static readonly Color RegionBorderColor = Color.FromArgb(255, 70, 130, 180);
        private static readonly Color RegionFillColor = Color.FromArgb(40, 70, 130, 180);
        private static readonly Color GridLineColor = Color.FromArgb(120, 255, 255, 255);
        private static readonly Color HandleFillColor = Color.FromArgb(220, 70, 130, 180);
        private static readonly Color HandleBorderColor = Color.White;
        private static readonly Color SlotBorderColor = Color.FromArgb(255, 100, 180, 100);
        private static readonly Color SlotFillColor = Color.FromArgb(50, 100, 180, 100);

        private readonly Rectangle _virtualScreen;
        private readonly List<RegionOverlayItem> _items;
        private int _draggingItemIndex = -1;
        private HandleKind _draggingHandle = HandleKind.None;
        private Point _dragStartScreen;

        private enum HandleKind
        {
            None,
            Left,
            Right,
            Top,
            Bottom,
            TopLeft,
            TopRight,
            BottomLeft,
            BottomRight
        }

        /// <summary>
        /// Mutable item: screen rectangle and grid dimensions. Rect is updated in place when user drags.
        /// When SlotIndex >= 0, this is a window slot (no grid drawn; label shows slot number).
        /// </summary>
        public class RegionOverlayItem
        {
            public Rectangle Rect; // screen coordinates
            public int Rows;
            public int Cols;
            /// <summary>When >= 0, this item is a window slot (1-based). When -1, it's a region.</summary>
            public int SlotIndex = -1;
            /// <summary>For region items, the 1-based slot number of the first cell (top-left). Used to label grid cells.</summary>
            public int StartSlotIndex = 1;
        }

        public LayoutOverlayForm(List<RegionOverlayItem> items)
        {
            _items = items ?? new List<RegionOverlayItem>();
            _virtualScreen = SystemInformation.VirtualScreen;
            FormBorderStyle = FormBorderStyle.None;
            Bounds = _virtualScreen;
            StartPosition = FormStartPosition.Manual;
            Location = new Point(_virtualScreen.X, _virtualScreen.Y);
            Size = new Size(_virtualScreen.Width, _virtualScreen.Height);
            TopMost = true;
            BackColor = Color.Black;
            Opacity = 0.0;
            DoubleBuffered = true;
            ShowInTaskbar = false;
            Cursor = Cursors.Default;
            Paint += LayoutOverlayForm_Paint;
            MouseDown += LayoutOverlayForm_MouseDown;
            MouseMove += LayoutOverlayForm_MouseMove;
            MouseUp += LayoutOverlayForm_MouseUp;
            KeyDown += LayoutOverlayForm_KeyDown;

            var doneBtn = new Button
            {
                Text = "Done",
                Size = new Size(100, 36),
                Location = new Point(_virtualScreen.Width - 120, _virtualScreen.Height - 50),
                Anchor = AnchorStyles.None,
                BackColor = Color.FromArgb(70, 130, 180),
                ForeColor = Color.White,
                FlatStyle = FlatStyle.Flat
            };
            doneBtn.FlatAppearance.BorderSize = 0;
            doneBtn.Click += (s, e) => { DialogResult = DialogResult.OK; Close(); };
            var cancelBtn = new Button
            {
                Text = "Cancel",
                Size = new Size(100, 36),
                Location = new Point(_virtualScreen.Width - 230, _virtualScreen.Height - 50),
                BackColor = Color.FromArgb(80, 80, 80),
                ForeColor = Color.White,
                FlatStyle = FlatStyle.Flat
            };
            cancelBtn.FlatAppearance.BorderSize = 0;
            cancelBtn.Click += (s, e) => { DialogResult = DialogResult.Cancel; Close(); };
            var hintLabel = new Label
            {
                Text = "Drag the white handles to resize. Press Esc to cancel.",
                AutoSize = true,
                ForeColor = Color.White,
                BackColor = Color.Transparent,
                Location = new Point(20, _virtualScreen.Height - 46)
            };
            Controls.Add(doneBtn);
            Controls.Add(cancelBtn);
            Controls.Add(hintLabel);
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            Opacity = 0.85;
        }

        private void LayoutOverlayForm_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Escape)
            {
                DialogResult = DialogResult.Cancel;
                Close();
            }
        }

        private Rectangle ScreenToClientRect(Rectangle screenRect)
        {
            return new Rectangle(
                screenRect.X - _virtualScreen.X,
                screenRect.Y - _virtualScreen.Y,
                screenRect.Width,
                screenRect.Height);
        }

        private Point ScreenToClient(Point screenPt)
        {
            return new Point(screenPt.X - _virtualScreen.X, screenPt.Y - _virtualScreen.Y);
        }

        private Point ClientToScreen(Point clientPt)
        {
            return new Point(clientPt.X + _virtualScreen.X, clientPt.Y + _virtualScreen.Y);
        }

        private static Rectangle GetHandleRect(Rectangle regionRect, HandleKind kind)
        {
            int h = HandleSize / 2;
            int x, y;
            switch (kind)
            {
                case HandleKind.TopLeft:     x = regionRect.Left;  y = regionRect.Top;    break;
                case HandleKind.Top:        x = regionRect.Left + regionRect.Width / 2;  y = regionRect.Top;    break;
                case HandleKind.TopRight:    x = regionRect.Right; y = regionRect.Top;    break;
                case HandleKind.Right:       x = regionRect.Right; y = regionRect.Top + regionRect.Height / 2; break;
                case HandleKind.BottomRight: x = regionRect.Right; y = regionRect.Bottom; break;
                case HandleKind.Bottom:      x = regionRect.Left + regionRect.Width / 2;  y = regionRect.Bottom; break;
                case HandleKind.BottomLeft:  x = regionRect.Left;  y = regionRect.Bottom; break;
                case HandleKind.Left:        x = regionRect.Left;  y = regionRect.Top + regionRect.Height / 2; break;
                default: return Rectangle.Empty;
            }
            return new Rectangle(x - h, y - h, HandleSize, HandleSize);
        }

        private static HandleKind[] AllHandles { get; } =
        {
            HandleKind.Left, HandleKind.Right, HandleKind.Top, HandleKind.Bottom,
            HandleKind.TopLeft, HandleKind.TopRight, HandleKind.BottomLeft, HandleKind.BottomRight
        };

        private (int itemIndex, HandleKind handle) HitTest(Point screenPoint)
        {
            for (int i = _items.Count - 1; i >= 0; i--)
            {
                var r = _items[i].Rect;
                foreach (var kind in AllHandles)
                {
                    if (GetHandleRect(r, kind).Contains(screenPoint))
                        return (i, kind);
                }
            }
            return (-1, HandleKind.None);
        }

        private void ApplyResize(int itemIndex, HandleKind handle, Point currentScreen)
        {
            if (itemIndex < 0 || itemIndex >= _items.Count) return;
            var item = _items[itemIndex];
            int left = item.Rect.Left, right = item.Rect.Right, top = item.Rect.Top, bottom = item.Rect.Bottom;
            int minW = MinRegionSize, minH = MinRegionSize;

            switch (handle)
            {
                case HandleKind.Left:
                    left = Math.Min(currentScreen.X, right - minW);
                    break;
                case HandleKind.Right:
                    right = Math.Max(currentScreen.X, left + minW);
                    break;
                case HandleKind.Top:
                    top = Math.Min(currentScreen.Y, bottom - minH);
                    break;
                case HandleKind.Bottom:
                    bottom = Math.Max(currentScreen.Y, top + minH);
                    break;
                case HandleKind.TopLeft:
                    left = Math.Min(currentScreen.X, right - minW);
                    top = Math.Min(currentScreen.Y, bottom - minH);
                    break;
                case HandleKind.TopRight:
                    right = Math.Max(currentScreen.X, left + minW);
                    top = Math.Min(currentScreen.Y, bottom - minH);
                    break;
                case HandleKind.BottomLeft:
                    left = Math.Min(currentScreen.X, right - minW);
                    bottom = Math.Max(currentScreen.Y, top + minH);
                    break;
                case HandleKind.BottomRight:
                    right = Math.Max(currentScreen.X, left + minW);
                    bottom = Math.Max(currentScreen.Y, top + minH);
                    break;
            }

            item.Rect = Rectangle.FromLTRB(left, top, right, bottom);
        }

        private Cursor CursorForHandle(HandleKind handle)
        {
            switch (handle)
            {
                case HandleKind.Left:
                case HandleKind.Right: return Cursors.SizeWE;
                case HandleKind.Top:
                case HandleKind.Bottom: return Cursors.SizeNS;
                case HandleKind.TopLeft:
                case HandleKind.BottomRight: return Cursors.SizeNWSE;
                case HandleKind.TopRight:
                case HandleKind.BottomLeft: return Cursors.SizeNESW;
                default: return Cursors.Default;
            }
        }

        private void LayoutOverlayForm_MouseDown(object sender, MouseEventArgs e)
        {
            var screenPt = ClientToScreen(e.Location);
            var (itemIndex, handle) = HitTest(screenPt);
            if (handle != HandleKind.None)
            {
                _draggingItemIndex = itemIndex;
                _draggingHandle = handle;
                _dragStartScreen = screenPt;
                Cursor = CursorForHandle(handle);
            }
        }

        private void LayoutOverlayForm_MouseMove(object sender, MouseEventArgs e)
        {
            var screenPt = ClientToScreen(e.Location);
            if (_draggingHandle != HandleKind.None)
            {
                ApplyResize(_draggingItemIndex, _draggingHandle, screenPt);
                Invalidate();
                return;
            }
            var (_, handle) = HitTest(screenPt);
            Cursor = handle == HandleKind.None ? Cursors.Default : CursorForHandle(handle);
        }

        private void LayoutOverlayForm_MouseUp(object sender, MouseEventArgs e)
        {
            if (_draggingHandle != HandleKind.None)
            {
                _draggingItemIndex = -1;
                _draggingHandle = HandleKind.None;
                Cursor = Cursors.Default;
            }
        }

        private void LayoutOverlayForm_Paint(object sender, PaintEventArgs e)
        {
            var g = e.Graphics;
            using (var brush = new SolidBrush(OverlayBackColor))
                g.FillRectangle(brush, 0, 0, _virtualScreen.Width, _virtualScreen.Height);

            foreach (var item in _items)
            {
                var cr = ScreenToClientRect(item.Rect);
                bool isSlot = item.SlotIndex >= 0;
                var fillColor = isSlot ? SlotFillColor : RegionFillColor;
                var borderColor = isSlot ? SlotBorderColor : RegionBorderColor;
                using (var fillBrush = new SolidBrush(fillColor))
                    g.FillRectangle(fillBrush, cr);
                using (var borderPen = new Pen(borderColor, 3))
                    g.DrawRectangle(borderPen, cr.X, cr.Y, cr.Width - 1, cr.Height - 1);

                if (isSlot)
                {
                    // Scale font to roughly 40% of the slot's shortest side, capped so it's always legible
                    float fontSize = Math.Max(18f, Math.Min(cr.Width, cr.Height) * 0.40f);
                    using (var font = new Font("Segoe UI", fontSize, FontStyle.Bold, GraphicsUnit.Pixel))
                    {
                        var label = item.SlotIndex.ToString();
                        g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAlias;
                        SizeF textSize = g.MeasureString(label, font);
                        float tx = cr.X + (cr.Width  - textSize.Width)  / 2f;
                        float ty = cr.Y + (cr.Height - textSize.Height) / 2f;

                        // Shadow / outline for legibility
                        using (var shadowBrush = new SolidBrush(Color.FromArgb(160, 0, 0, 0)))
                        {
                            g.DrawString(label, font, shadowBrush, tx + 2, ty + 2);
                        }
                        using (var labelBrush = new SolidBrush(Color.White))
                        {
                            g.DrawString(label, font, labelBrush, tx, ty);
                        }
                    }
                }
                else
                {
                    int rows = Math.Max(1, item.Rows);
                    int cols = Math.Max(1, item.Cols);
                    int cellW = cr.Width / cols;
                    int cellH = cr.Height / rows;

                    if (rows > 1 || cols > 1)
                    {
                        using (var gridPen = new Pen(GridLineColor, 1))
                        {
                            for (int i = 1; i < rows; i++)
                            {
                                int y = cr.Y + cr.Height * i / rows;
                                g.DrawLine(gridPen, cr.Left, y, cr.Right, y);
                            }
                            for (int j = 1; j < cols; j++)
                            {
                                int x = cr.X + cr.Width * j / cols;
                                g.DrawLine(gridPen, x, cr.Top, x, cr.Bottom);
                            }
                        }
                    }

                    // Draw slot number in center of each grid cell
                    float cellFontSize = Math.Max(12f, Math.Min(cellW, cellH) * 0.35f);
                    g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAlias;
                    using (var font = new Font("Segoe UI", cellFontSize, FontStyle.Bold, GraphicsUnit.Pixel))
                    using (var shadowBrush = new SolidBrush(Color.FromArgb(160, 0, 0, 0)))
                    using (var labelBrush = new SolidBrush(Color.White))
                    {
                        int slotNum = item.StartSlotIndex;
                        for (int row = 0; row < rows; row++)
                        {
                            for (int col = 0; col < cols; col++)
                            {
                                int cx = cr.X + cr.Width * col / cols;
                                int cy = cr.Y + cr.Height * row / rows;
                                int cw = cr.Width / cols;
                                int ch = cr.Height / rows;
                                var label = slotNum.ToString();
                                SizeF sz = g.MeasureString(label, font);
                                float tx = cx + (cw - sz.Width)  / 2f;
                                float ty = cy + (ch - sz.Height) / 2f;
                                g.DrawString(label, font, shadowBrush, tx + 2, ty + 2);
                                g.DrawString(label, font, labelBrush, tx, ty);
                                slotNum++;
                            }
                        }
                    }
                }

                foreach (var kind in AllHandles)
                {
                    var hr = GetHandleRect(item.Rect, kind);
                    var hcr = ScreenToClientRect(hr);
                    using (var hBrush = new SolidBrush(HandleFillColor))
                        g.FillRectangle(hBrush, hcr);
                    using (var hPen = new Pen(HandleBorderColor, 2))
                        g.DrawRectangle(hPen, hcr.X, hcr.Y, hcr.Width - 1, hcr.Height - 1);
                }
            }
        }
    }
}
