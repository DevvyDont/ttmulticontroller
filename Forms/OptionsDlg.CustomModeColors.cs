using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using TTMulti;

namespace TTMulti.Forms
{
    public partial class OptionsDlg
    {
        FlowLayoutPanel _customModeBorderColorsPanel;
        readonly Dictionary<string, (Button LeftSwatch, Button RightSwatch)> _customModeBorderColorPickers =
            new Dictionary<string, (Button LeftSwatch, Button RightSwatch)>(StringComparer.Ordinal);

        void AddCustomModeBorderColorsSection(GroupBox parent, ref int yPos)
        {
            var header = new Label
            {
                Text = "Custom mode borders (per mode — left / right slots, same idea as Multi mode):",
                Location = new Point(10, yPos),
                Size = new Size(680, 32),
                Font = new Font(SystemFonts.DefaultFont, FontStyle.Bold)
            };
            parent.Controls.Add(header);
            yPos += 36;

            _customModeBorderColorsPanel = new FlowLayoutPanel
            {
                Location = new Point(8, yPos),
                Size = new Size(700, 220),
                FlowDirection = FlowDirection.TopDown,
                WrapContents = false,
                AutoScroll = true,
            };
            parent.Controls.Add(_customModeBorderColorsPanel);
            yPos += 224;
        }

        void RebuildCustomModeBorderColorRows()
        {
            if (_customModeBorderColorsPanel == null)
                return;

            _customModeBorderColorsPanel.Controls.Clear();
            _customModeBorderColorPickers.Clear();

            CustomModeFile file = _customModeFile ?? CustomModeStorage.Load();
            List<CustomModeDefinition> modes = file?.Modes;
            if (modes == null || modes.Count == 0)
            {
                _customModeBorderColorsPanel.Controls.Add(new Label
                {
                    Text = "No custom modes yet. Add modes on the Custom Modes tab.",
                    AutoSize = true,
                    Margin = new Padding(0, 6, 0, 6)
                });
                return;
            }

            Color defaultLeft = CustomModeDefinition.DefaultLeftBorderColor;
            Color defaultRight = CustomModeDefinition.DefaultRightBorderColor;

            foreach (CustomModeDefinition mode in modes.OrderBy(m => m.Name, StringComparer.OrdinalIgnoreCase))
            {
                if (string.IsNullOrEmpty(mode.Id))
                    continue;

                var row = new Panel
                {
                    Width = 680,
                    Height = 34,
                    Margin = new Padding(0, 2, 0, 2)
                };

                string displayName = string.IsNullOrWhiteSpace(mode.Name) ? "(unnamed)" : mode.Name;
                row.Controls.Add(new Label
                {
                    Text = displayName + ":",
                    Location = new Point(0, 9),
                    Size = new Size(150, 20),
                    TextAlign = ContentAlignment.MiddleLeft,
                });

                row.Controls.Add(new Label { Text = "Left", Location = new Point(158, 9), Size = new Size(32, 20), TextAlign = ContentAlignment.MiddleRight });
                Button leftSwatch = CreateColorSwatchButton(mode.GetLeftBorderColor());
                leftSwatch.Location = new Point(195, 4);
                row.Controls.Add(leftSwatch);
                Button leftChange = new Button { Text = "Change", Location = new Point(240, 4), Size = new Size(58, 26) };
                leftChange.Click += (s, e) => ShowColorDialog(leftSwatch, defaultLeft);
                row.Controls.Add(leftChange);
                Button leftReset = new Button { Text = "Reset", Location = new Point(302, 4), Size = new Size(50, 26) };
                leftReset.Click += (s, e) => { leftSwatch.BackColor = defaultLeft; };
                row.Controls.Add(leftReset);

                row.Controls.Add(new Label { Text = "Right", Location = new Point(365, 9), Size = new Size(36, 20), TextAlign = ContentAlignment.MiddleRight });
                Button rightSwatch = CreateColorSwatchButton(mode.GetRightBorderColor());
                rightSwatch.Location = new Point(405, 4);
                row.Controls.Add(rightSwatch);
                Button rightChange = new Button { Text = "Change", Location = new Point(450, 4), Size = new Size(58, 26) };
                rightChange.Click += (s, e) => ShowColorDialog(rightSwatch, defaultRight);
                row.Controls.Add(rightChange);
                Button rightReset = new Button { Text = "Reset", Location = new Point(512, 4), Size = new Size(50, 26) };
                rightReset.Click += (s, e) => { rightSwatch.BackColor = defaultRight; };
                row.Controls.Add(rightReset);

                _customModeBorderColorPickers[mode.Id] = (leftSwatch, rightSwatch);
                _customModeBorderColorsPanel.Controls.Add(row);
            }
        }

        static Button CreateColorSwatchButton(Color initial)
        {
            var b = new Button
            {
                Text = "",
                Size = new Size(40, 26),
                BackColor = initial,
                FlatStyle = FlatStyle.Flat,
                UseVisualStyleBackColor = false,
            };
            b.FlatAppearance.BorderSize = 1;
            b.FlatAppearance.BorderColor = Color.Gray;
            return b;
        }

        void PushCustomModeBorderColorsFromUiToModel()
        {
            if (_customModeFile?.Modes == null || _customModeBorderColorPickers.Count == 0)
                return;

            foreach (CustomModeDefinition mode in _customModeFile.Modes)
            {
                if (string.IsNullOrEmpty(mode.Id))
                    continue;
                if (!_customModeBorderColorPickers.TryGetValue(mode.Id, out var sw))
                    continue;
                mode.LeftBorderColorArgb = sw.LeftSwatch.BackColor.ToArgb();
                mode.RightBorderColorArgb = sw.RightSwatch.BackColor.ToArgb();
            }
        }
    }
}
