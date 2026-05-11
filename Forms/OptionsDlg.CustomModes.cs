using System;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using TTMulti;
using TTMulti.Controls;

namespace TTMulti.Forms
{
    public partial class OptionsDlg
    {
        CustomModeFile _customModeFile;
        ListBox _cmModesList;
        TextBox _cmNameText;
        ListBox _cmBindingsList;
        KeyPicker _cmInputKeyPicker;
        CheckBox _cmAlt;
        CheckBox _cmCtrl;
        CheckBox _cmShift;
        ComboBox _cmActionCombo;
        ComboBox _cmRoleCombo;
        KeyPicker _cmRawKeyPicker;
        ComboBox _cmTargetKindCombo;
        NumericUpDown _cmTargetNud;
        TextBox _cmListedIndicesText;
        Button _cmAddModeBtn;
        Button _cmRemoveModeBtn;
        Button _cmAddBindBtn;
        Button _cmRemoveBindBtn;
        CheckBox _cmCycleWithModeHotkeyChk;
        bool _cmSuppress;

        private void CreateCustomModesTab()
        {
            var tab = new TabPage("Custom Modes")
            {
                Name = "customModesTab",
                Padding = new Padding(10),
                UseVisualStyleBackColor = true,
                AutoScroll = true
            };
            tabControl1.TabPages.Add(tab);

            var help = new Label
            {
                Text = "Targets are 1-based: same ordering as instant multiclick (Multi-Click tab: controller order vs window position). " +
                       "Choose one toon, all toons, or a comma-separated list (e.g. 1,2,4). " +
                       "Send role uses a binding title from Multi-Mode Keys (e.g. Forward, Jump). First matching binding wins.",
                Location = new Point(10, 10),
                Size = new Size(720, 52),
                Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right
            };
            tab.Controls.Add(help);

            tab.Controls.Add(new Label { Text = "Modes", Location = new Point(10, 72), Size = new Size(80, 18) });
            _cmModesList = new ListBox { Location = new Point(10, 92), Size = new Size(240, 100) };
            _cmModesList.DisplayMember = "Name";
            _cmModesList.SelectedIndexChanged += CmModesList_SelectedIndexChanged;
            tab.Controls.Add(_cmModesList);

            _cmAddModeBtn = new Button { Text = "Add mode", Location = new Point(260, 92), Size = new Size(90, 26) };
            _cmAddModeBtn.Click += CmAddMode_Click;
            tab.Controls.Add(_cmAddModeBtn);
            _cmRemoveModeBtn = new Button { Text = "Remove", Location = new Point(260, 124), Size = new Size(90, 26) };
            _cmRemoveModeBtn.Click += CmRemoveMode_Click;
            tab.Controls.Add(_cmRemoveModeBtn);

            tab.Controls.Add(new Label { Text = "Mode name", Location = new Point(370, 72), Size = new Size(80, 18) });
            _cmNameText = new TextBox { Location = new Point(370, 92), Size = new Size(300, 23), Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right };
            _cmNameText.TextChanged += CmNameText_TextChanged;
            tab.Controls.Add(_cmNameText);

            _cmCycleWithModeHotkeyChk = new CheckBox
            {
                Text = "Include Custom mode when cycling with the mode hotkey",
                Location = new Point(10, 202),
                Size = new Size(420, 22)
            };
            tab.Controls.Add(_cmCycleWithModeHotkeyChk);

            tab.Controls.Add(new Label { Text = "Bindings", Location = new Point(10, 232), Size = new Size(80, 18) });
            _cmBindingsList = new ListBox { Location = new Point(10, 252), Size = new Size(340, 120) };
            _cmBindingsList.SelectedIndexChanged += CmBindingsList_SelectedIndexChanged;
            tab.Controls.Add(_cmBindingsList);

            _cmAddBindBtn = new Button { Text = "Add binding", Location = new Point(360, 252), Size = new Size(100, 26) };
            _cmAddBindBtn.Click += CmAddBinding_Click;
            tab.Controls.Add(_cmAddBindBtn);
            _cmRemoveBindBtn = new Button { Text = "Remove", Location = new Point(360, 284), Size = new Size(100, 26) };
            _cmRemoveBindBtn.Click += CmRemoveBinding_Click;
            tab.Controls.Add(_cmRemoveBindBtn);

            int ey = 388;
            tab.Controls.Add(new Label { Text = "Input key", Location = new Point(10, ey), Size = new Size(70, 18) });
            _cmInputKeyPicker = new KeyPicker { Location = new Point(82, ey - 2), Size = new Size(120, 24) };
            _cmInputKeyPicker.KeyChosen += CmEditor_KeyChosen;
            tab.Controls.Add(_cmInputKeyPicker);

            _cmAlt = new CheckBox { Text = "Alt", Location = new Point(220, ey), Size = new Size(45, 22) };
            _cmAlt.CheckedChanged += CmEditor_CheckChanged;
            tab.Controls.Add(_cmAlt);
            _cmCtrl = new CheckBox { Text = "Ctrl", Location = new Point(270, ey), Size = new Size(50, 22) };
            _cmCtrl.CheckedChanged += CmEditor_CheckChanged;
            tab.Controls.Add(_cmCtrl);
            _cmShift = new CheckBox { Text = "Shift", Location = new Point(325, ey), Size = new Size(55, 22) };
            _cmShift.CheckedChanged += CmEditor_CheckChanged;
            tab.Controls.Add(_cmShift);

            ey += 34;
            tab.Controls.Add(new Label { Text = "Action", Location = new Point(10, ey), Size = new Size(70, 18) });
            _cmActionCombo = new ComboBox { Location = new Point(82, ey - 2), Size = new Size(220, 24), DropDownStyle = ComboBoxStyle.DropDownList };
            _cmActionCombo.Items.AddRange(new object[] { "Send role (from key bindings)", "Send raw key", "Instant click" });
            _cmActionCombo.SelectedIndexChanged += CmActionCombo_SelectedIndexChanged;
            tab.Controls.Add(_cmActionCombo);

            ey += 34;
            tab.Controls.Add(new Label { Text = "Role", Location = new Point(10, ey), Size = new Size(70, 18) });
            _cmRoleCombo = new ComboBox { Location = new Point(82, ey - 2), Size = new Size(200, 24), DropDownStyle = ComboBoxStyle.DropDownList };
            _cmRoleCombo.SelectedIndexChanged += CmRoleCombo_SelectedIndexChanged;
            tab.Controls.Add(_cmRoleCombo);

            tab.Controls.Add(new Label { Text = "Raw key", Location = new Point(300, ey), Size = new Size(60, 18) });
            _cmRawKeyPicker = new KeyPicker { Location = new Point(365, ey - 2), Size = new Size(120, 24) };
            _cmRawKeyPicker.KeyChosen += CmEditor_KeyChosen;
            tab.Controls.Add(_cmRawKeyPicker);

            ey += 34;
            tab.Controls.Add(new Label { Text = "Target(s)", Location = new Point(10, ey), Size = new Size(70, 18) });
            _cmTargetKindCombo = new ComboBox { Location = new Point(82, ey - 2), Size = new Size(260, 24), DropDownStyle = ComboBoxStyle.DropDownList };
            _cmTargetKindCombo.Items.AddRange(new object[] { "One toon (# below)", "All toons", "Listed (comma-separated #s)" });
            _cmTargetKindCombo.SelectedIndex = 0;
            _cmTargetKindCombo.SelectedIndexChanged += CmTargetKindCombo_SelectedIndexChanged;
            tab.Controls.Add(_cmTargetKindCombo);

            ey += 34;
            tab.Controls.Add(new Label { Text = "Toon #", Location = new Point(10, ey), Size = new Size(50, 18) });
            _cmTargetNud = new NumericUpDown { Location = new Point(65, ey - 2), Size = new Size(56, 24), Minimum = 1, Maximum = 32, Value = 1 };
            _cmTargetNud.ValueChanged += CmTargetNud_ValueChanged;
            tab.Controls.Add(_cmTargetNud);
            tab.Controls.Add(new Label { Text = "Indices", Location = new Point(135, ey), Size = new Size(48, 18) });
            _cmListedIndicesText = new TextBox { Location = new Point(185, ey - 2), Size = new Size(320, 23), Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right };
            _cmListedIndicesText.TextChanged += CmListedIndicesText_TextChanged;
            tab.Controls.Add(_cmListedIndicesText);
        }

        private void LoadCustomModes()
        {
            _customModeFile = CustomModeStorage.Load();
            if (_customModeFile.Modes == null)
                _customModeFile.Modes = new System.Collections.Generic.List<CustomModeDefinition>();

            _cmSuppress = true;
            _cmModesList.Items.Clear();
            foreach (var m in _customModeFile.Modes)
                _cmModesList.Items.Add(m);
            _cmCycleWithModeHotkeyChk.Checked = Properties.Settings.Default.customModeCycleWithModeHotkey;
            RefreshCmRoleComboItems();
            if (_cmModesList.Items.Count > 0)
                _cmModesList.SelectedIndex = 0;
            else
            {
                _cmNameText.Enabled = false;
                ClearCmBindingEditors();
            }
            _cmSuppress = false;
        }

        private void RefreshCmRoleComboItems()
        {
            string prev = _cmSuppress ? null : (_cmRoleCombo.SelectedItem as string);
            _cmRoleCombo.Items.Clear();
            foreach (var t in Properties.SerializedSettings.Default.Bindings.Select(b => b.Title).Where(s => !string.IsNullOrEmpty(s)).Distinct())
                _cmRoleCombo.Items.Add(t);
            if (!string.IsNullOrEmpty(prev) && _cmRoleCombo.Items.Contains(prev))
                _cmRoleCombo.SelectedItem = prev;
            else if (_cmRoleCombo.Items.Count > 0)
                _cmRoleCombo.SelectedIndex = 0;
        }

        private void SaveCustomModesSettings()
        {
            if (_customModeFile != null)
                CustomModeStorage.Save(_customModeFile);
            Properties.Settings.Default.customModeCycleWithModeHotkey = _cmCycleWithModeHotkeyChk.Checked;
        }

        private CustomModeDefinition CmSelectedMode => _cmModesList?.SelectedItem as CustomModeDefinition;

        private CustomModeBinding CmSelectedBinding => _cmBindingsList?.SelectedItem as CustomModeBinding;

        private void CmModesList_SelectedIndexChanged(object sender, EventArgs e)
        {
            if (_cmSuppress) return;
            _cmSuppress = true;
            var mode = CmSelectedMode;
            _cmNameText.Enabled = mode != null;
            _cmNameText.Text = mode?.Name ?? "";
            RefreshCmBindingsList();
            _cmSuppress = false;
        }

        private void RefreshCmBindingsList()
        {
            _cmBindingsList.Items.Clear();
            var mode = CmSelectedMode;
            if (mode?.Bindings == null) return;
            foreach (var b in mode.Bindings)
                _cmBindingsList.Items.Add(b);
            if (_cmBindingsList.Items.Count > 0)
                _cmBindingsList.SelectedIndex = 0;
            else
                ClearCmBindingEditors();
        }

        private void CmBindingsList_SelectedIndexChanged(object sender, EventArgs e)
        {
            if (_cmSuppress) return;
            var b = CmSelectedBinding;
            if (b == null)
            {
                ClearCmBindingEditors();
                return;
            }
            _cmSuppress = true;
            _cmInputKeyPicker.ChosenKey = (Keys)b.InputKey;
            _cmAlt.Checked = b.RequireAlt;
            _cmCtrl.Checked = b.RequireControl;
            _cmShift.Checked = b.RequireShift;
            _cmActionCombo.SelectedIndex = Math.Max(0, Math.Min(2, (int)b.Action));
            if (!string.IsNullOrEmpty(b.RoleTitle) && _cmRoleCombo.Items.Contains(b.RoleTitle))
                _cmRoleCombo.SelectedItem = b.RoleTitle;
            else if (_cmRoleCombo.Items.Count > 0)
                _cmRoleCombo.SelectedIndex = 0;
            _cmRawKeyPicker.ChosenKey = (Keys)b.RawKey;
            int kind = (int)b.TargetKind;
            if (kind < 0 || kind > 2) kind = 0;
            _cmTargetKindCombo.SelectedIndex = kind;
            _cmTargetNud.Value = Math.Max(1, Math.Min(32, b.TargetIndex));
            _cmListedIndicesText.Text = (b.ListedTargetIndices != null && b.ListedTargetIndices.Count > 0)
                ? string.Join(", ", b.ListedTargetIndices)
                : "";
            UpdateCmEditorEnableStates();
            _cmSuppress = false;
        }

        private void ClearCmBindingEditors()
        {
            _cmInputKeyPicker.ChosenKey = Keys.None;
            _cmAlt.Checked = _cmCtrl.Checked = _cmShift.Checked = false;
            _cmActionCombo.SelectedIndex = 0;
            _cmRawKeyPicker.ChosenKey = Keys.None;
            _cmTargetKindCombo.SelectedIndex = 0;
            _cmTargetNud.Value = 1;
            _cmListedIndicesText.Text = "";
            UpdateCmEditorEnableStates();
        }

        private void UpdateCmEditorEnableStates()
        {
            bool hasMode = CmSelectedMode != null;
            bool hasBind = CmSelectedBinding != null;
            _cmBindingsList.Enabled = hasMode;
            _cmAddBindBtn.Enabled = hasMode;
            _cmRemoveBindBtn.Enabled = hasMode && hasBind;
            _cmInputKeyPicker.Enabled = hasBind;
            _cmAlt.Enabled = _cmCtrl.Enabled = _cmShift.Enabled = hasBind;
            _cmActionCombo.Enabled = hasBind;
            int a = _cmActionCombo.SelectedIndex;
            _cmRoleCombo.Enabled = hasBind && a == 0;
            _cmRawKeyPicker.Enabled = hasBind && a == 1;
            int tk = _cmTargetKindCombo?.SelectedIndex ?? 0;
            _cmTargetKindCombo.Enabled = hasBind;
            _cmTargetNud.Enabled = hasBind && tk == 0;
            _cmListedIndicesText.Enabled = hasBind && tk == 2;
        }

        private void PushCmEditorToBinding()
        {
            var b = CmSelectedBinding;
            if (b == null || _cmSuppress) return;
            b.InputKey = (int)_cmInputKeyPicker.ChosenKey;
            b.RequireAlt = _cmAlt.Checked;
            b.RequireControl = _cmCtrl.Checked;
            b.RequireShift = _cmShift.Checked;
            b.Action = (CustomModeBindingAction)_cmActionCombo.SelectedIndex;
            b.RoleTitle = _cmRoleCombo.SelectedItem as string ?? "";
            b.RawKey = (int)_cmRawKeyPicker.ChosenKey;
            b.TargetKind = (CustomModeTargetKind)Math.Max(0, Math.Min(2, _cmTargetKindCombo.SelectedIndex));
            b.TargetIndex = (int)_cmTargetNud.Value;
            if (b.TargetKind == CustomModeTargetKind.Listed)
                b.ListedTargetIndices = CustomModeBinding.ParseListedTargetIndices(_cmListedIndicesText.Text);
            else
                b.ListedTargetIndices = null;
            // ListBox.RefreshItems() is protected; re-insert the item so the display string (ToString) updates.
            int idx = _cmBindingsList.SelectedIndex;
            if (idx >= 0 && idx < _cmBindingsList.Items.Count)
            {
                _cmSuppress = true;
                _cmBindingsList.Items.RemoveAt(idx);
                _cmBindingsList.Items.Insert(idx, b);
                _cmBindingsList.SelectedIndex = idx;
                _cmSuppress = false;
            }
        }

        private void CmNameText_TextChanged(object sender, EventArgs e)
        {
            var m = CmSelectedMode;
            if (m == null || _cmSuppress) return;
            m.Name = _cmNameText.Text;
            int i = _cmModesList.SelectedIndex;
            _cmSuppress = true;
            _cmModesList.Items[i] = m;
            _cmModesList.SelectedIndex = i;
            _cmSuppress = false;
        }

        private void CmAddMode_Click(object sender, EventArgs e)
        {
            var m = new CustomModeDefinition();
            _customModeFile.Modes.Add(m);
            _cmModesList.Items.Add(m);
            _cmModesList.SelectedIndex = _cmModesList.Items.Count - 1;
        }

        private void CmRemoveMode_Click(object sender, EventArgs e)
        {
            var m = CmSelectedMode;
            if (m == null) return;
            if (MessageBox.Show("Remove this custom mode?", "Confirm", MessageBoxButtons.YesNo) != DialogResult.Yes)
                return;
            int i = _cmModesList.SelectedIndex;
            _customModeFile.Modes.Remove(m);
            _cmModesList.Items.RemoveAt(i);
            if (_cmModesList.Items.Count > 0)
                _cmModesList.SelectedIndex = Math.Min(i, _cmModesList.Items.Count - 1);
            else
                ClearCmBindingEditors();
        }

        private void CmAddBinding_Click(object sender, EventArgs e)
        {
            var m = CmSelectedMode;
            if (m == null) return;
            if (m.Bindings == null)
                m.Bindings = new System.Collections.Generic.List<CustomModeBinding>();
            var b = new CustomModeBinding
            {
                InputKey = (int)Keys.None,
                Action = CustomModeBindingAction.SendRole,
                TargetIndex = 1,
                TargetKind = CustomModeTargetKind.Single,
                ListedTargetIndices = null,
                RoleTitle = Properties.SerializedSettings.Default.Bindings.FirstOrDefault()?.Title ?? "Forward"
            };
            m.Bindings.Add(b);
            _cmSuppress = true;
            _cmBindingsList.Items.Add(b);
            _cmBindingsList.SelectedIndex = _cmBindingsList.Items.Count - 1;
            _cmSuppress = false;
            CmBindingsList_SelectedIndexChanged(null, EventArgs.Empty);
        }

        private void CmRemoveBinding_Click(object sender, EventArgs e)
        {
            var m = CmSelectedMode;
            var b = CmSelectedBinding;
            if (m?.Bindings == null || b == null) return;
            int i = _cmBindingsList.SelectedIndex;
            m.Bindings.Remove(b);
            _cmBindingsList.Items.RemoveAt(i);
            if (_cmBindingsList.Items.Count > 0)
                _cmBindingsList.SelectedIndex = Math.Min(i, _cmBindingsList.Items.Count - 1);
            else
                ClearCmBindingEditors();
            UpdateCmEditorEnableStates();
        }

        private void CmEditor_KeyChosen(KeyPicker chooser, Keys keyChosen)
        {
            PushCmEditorToBinding();
            UpdateCmEditorEnableStates();
        }

        private void CmEditor_CheckChanged(object sender, EventArgs e)
        {
            PushCmEditorToBinding();
        }

        private void CmActionCombo_SelectedIndexChanged(object sender, EventArgs e)
        {
            if (_cmSuppress) return;
            PushCmEditorToBinding();
            UpdateCmEditorEnableStates();
        }

        private void CmRoleCombo_SelectedIndexChanged(object sender, EventArgs e)
        {
            if (_cmSuppress) return;
            PushCmEditorToBinding();
        }

        private void CmTargetNud_ValueChanged(object sender, EventArgs e)
        {
            if (_cmSuppress) return;
            PushCmEditorToBinding();
        }

        private void CmTargetKindCombo_SelectedIndexChanged(object sender, EventArgs e)
        {
            if (_cmSuppress) return;
            PushCmEditorToBinding();
            UpdateCmEditorEnableStates();
        }

        private void CmListedIndicesText_TextChanged(object sender, EventArgs e)
        {
            if (_cmSuppress) return;
            PushCmEditorToBinding();
        }
    }
}
