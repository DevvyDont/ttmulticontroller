using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;
using TTMulti;

namespace TTMulti.Forms
{
    /// <summary>
    /// Fullscreen overlay that shows all monitors with numbers. User clicks a monitor to select it;
    /// the selected 0-based index is returned via SelectedMonitorIndex.
    /// </summary>
    public class MonitorPickerForm : Form
    {
        private static readonly Color OverlayBackColor = Color.FromArgb(160, 0, 0, 0);
        private static readonly Color MonitorBorderColor = Color.FromArgb(255, 70, 130, 180);
        private static readonly Color MonitorFillColor = Color.FromArgb(50, 70, 130, 180);
        private static readonly Color NumberBackColor = Color.FromArgb(220, 70, 130, 180);
        private static readonly Color NumberForeColor = Color.White;

        private readonly Rectangle _virtualScreen;
        private readonly List<Rectangle> _monitorRects = new List<Rectangle>();

        public int SelectedMonitorIndex { get; private set; } = -1;

        public MonitorPickerForm()
        {
            for (int i = 0; ; i++)
            {
                var work = Win32.GetMonitorWorkAreaByIndex(i);
                if (!work.HasValue) break;
                var r = work.Value;
                _monitorRects.Add(new Rectangle(r.Left, r.Top, r.Right - r.Left, r.Bottom - r.Top));
            }

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
            Cursor = Cursors.Hand;
            Paint += MonitorPickerForm_Paint;
            MouseDown += MonitorPickerForm_MouseDown;
            KeyDown += MonitorPickerForm_KeyDown;

            var cancelBtn = new Button
            {
                Text = "Cancel",
                Size = new Size(100, 36),
                Location = new Point(_virtualScreen.Width - 120, _virtualScreen.Height - 50),
                BackColor = Color.FromArgb(80, 80, 80),
                ForeColor = Color.White,
                FlatStyle = FlatStyle.Flat
            };
            cancelBtn.FlatAppearance.BorderSize = 0;
            cancelBtn.Click += (s, e) => { DialogResult = DialogResult.Cancel; Close(); };
            var hintLabel = new Label
            {
                Text = "Click a monitor to select it. Press Esc to cancel.",
                AutoSize = true,
                ForeColor = Color.White,
                BackColor = Color.Transparent,
                Location = new Point(20, _virtualScreen.Height - 46)
            };
            Controls.Add(cancelBtn);
            Controls.Add(hintLabel);
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            Opacity = 0.85;
        }

        private void MonitorPickerForm_KeyDown(object sender, KeyEventArgs e)
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

        private Point ClientToScreen(Point clientPt)
        {
            return new Point(clientPt.X + _virtualScreen.X, clientPt.Y + _virtualScreen.Y);
        }

        private void MonitorPickerForm_MouseDown(object sender, MouseEventArgs e)
        {
            var screenPt = ClientToScreen(e.Location);
            for (int i = 0; i < _monitorRects.Count; i++)
            {
                if (_monitorRects[i].Contains(screenPt))
                {
                    SelectedMonitorIndex = i;
                    DialogResult = DialogResult.OK;
                    Close();
                    return;
                }
            }
        }

        private void MonitorPickerForm_Paint(object sender, PaintEventArgs e)
        {
            var g = e.Graphics;
            using (var brush = new SolidBrush(OverlayBackColor))
                g.FillRectangle(brush, 0, 0, _virtualScreen.Width, _virtualScreen.Height);

            using (var font = new Font("Segoe UI", 48, FontStyle.Bold))
            {
                for (int i = 0; i < _monitorRects.Count; i++)
                {
                    var rect = _monitorRects[i];
                    var cr = ScreenToClientRect(rect);
                    using (var fillBrush = new SolidBrush(MonitorFillColor))
                        g.FillRectangle(fillBrush, cr);
                    using (var borderPen = new Pen(MonitorBorderColor, 4))
                        g.DrawRectangle(borderPen, cr.X, cr.Y, cr.Width - 1, cr.Height - 1);

                    string label = (i + 1).ToString();
                    var size = g.MeasureString(label, font);
                    float cx = cr.X + (cr.Width - size.Width) / 2;
                    float cy = cr.Y + (cr.Height - size.Height) / 2;
                    var numberRect = new RectangleF(cx - 8, cy - 8, size.Width + 16, size.Height + 16);
                    using (var numBrush = new SolidBrush(NumberBackColor))
                        g.FillRectangle(numBrush, numberRect);
                    using (var numPen = new Pen(MonitorBorderColor, 2))
                        g.DrawRectangle(numPen, numberRect.X, numberRect.Y, numberRect.Width, numberRect.Height);
                    using (var textBrush = new SolidBrush(NumberForeColor))
                        g.DrawString(label, font, textBrush, cx, cy);
                }
            }
        }
    }
}
