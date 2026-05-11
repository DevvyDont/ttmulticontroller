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
        KeyPicker _cmModeActivationKeyPicker;
        CheckBox _cmActAlt;
        CheckBox _cmActCtrl;
        CheckBox _cmActShift;
        CheckBox _cmActHotkeyGlobalChk;
        CheckBox _cmIncludeInCycleChk;
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
                       "Send role uses a Multi-Mode Keys binding title (e.g. Forward, Jump) or \"Zero Power Throw\" (instant 0% throw per target). First matching binding wins.",
                Location = new Point(10, 10),
                Size = new Size(720, 58),
                Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right
            };
            tab.Controls.Add(help);

            const int mainTop = 74;
            const int mainRowHeight = 188;
            const int topAfterMainRow = mainTop + mainRowHeight + 12;

            var modesGroup = new GroupBox
            {
                Text = "Modes",
                Location = new Point(10, mainTop),
                Size = new Size(272, mainRowHeight),
                Anchor = AnchorStyles.Top | AnchorStyles.Left
            };
            tab.Controls.Add(modesGroup);

            _cmModesList = new ListBox
            {
                Location = new Point(10, 22),
                Size = new Size(175, 148),
                Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left,
                DisplayMember = "Name"
            };
            _cmModesList.SelectedIndexChanged += CmModesList_SelectedIndexChanged;
            modesGroup.Controls.Add(_cmModesList);

            _cmAddModeBtn = new Button { Text = "Add mode", Location = new Point(192, 22), Size = new Size(72, 26), Anchor = AnchorStyles.Top | AnchorStyles.Right };
            _cmAddModeBtn.Click += CmAddMode_Click;
            modesGroup.Controls.Add(_cmAddModeBtn);
            _cmRemoveModeBtn = new Button { Text = "Remove", Location = new Point(192, 54), Size = new Size(72, 26), Anchor = AnchorStyles.Top | AnchorStyles.Right };
            _cmRemoveModeBtn.Click += CmRemoveMode_Click;
            modesGroup.Controls.Add(_cmRemoveModeBtn);

            var selectedModeGroup = new GroupBox
            {
                Text = "Selected mode",
                Location = new Point(292, mainTop),
                Size = new Size(448, mainRowHeight),
                Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right
            };
            tab.Controls.Add(selectedModeGroup);

            var selectedModeLayout = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 1,
                RowCount = 6,
                Padding = new Padding(10, 8, 10, 8)
            };
            selectedModeLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
            selectedModeLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 20f));
            selectedModeLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 28f));
            selectedModeLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 20f));
            selectedModeLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 30f));
            selectedModeLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            selectedModeLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            selectedModeGroup.Controls.Add(selectedModeLayout);

            var modeNameLabel = new Label
            {
                Text = "Mode Name",
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.BottomLeft,
                Font = new Font(tab.Font, FontStyle.Bold)
            };
            selectedModeLayout.Controls.Add(modeNameLabel, 0, 0);

            _cmNameText = new TextBox { Dock = DockStyle.Fill, Margin = new Padding(0, 0, 0, 4) };
            _cmNameText.TextChanged += CmNameText_TextChanged;
            selectedModeLayout.Controls.Add(_cmNameText, 0, 1);

            var activationHeader = new Label
            {
                Text = "Activation hotkey (optional)",
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.BottomLeft
            };
            selectedModeLayout.Controls.Add(activationHeader, 0, 2);

            _cmModeActivationKeyPicker = new KeyPicker { Dock = DockStyle.Left, Width = 200, Height = 24, Margin = new Padding(0, 0, 0, 2) };
            _cmModeActivationKeyPicker.KeyChosen += CmModeActivationKeyPicker_KeyChosen;
            selectedModeLayout.Controls.Add(_cmModeActivationKeyPicker, 0, 3);

            var actModifiersFlow = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                AutoSize = true,
                WrapContents = true,
                FlowDirection = FlowDirection.LeftToRight,
                Margin = new Padding(0, 0, 0, 4)
            };
            _cmActAlt = new CheckBox { Text = "Alt", AutoSize = true, Margin = new Padding(0, 2, 12, 0) };
            _cmActAlt.CheckedChanged += CmModeActivationModifier_Changed;
            _cmActCtrl = new CheckBox { Text = "Ctrl", AutoSize = true, Margin = new Padding(0, 2, 12, 0) };
            _cmActCtrl.CheckedChanged += CmModeActivationModifier_Changed;
            _cmActShift = new CheckBox { Text = "Shift", AutoSize = true, Margin = new Padding(0, 2, 12, 0) };
            _cmActShift.CheckedChanged += CmModeActivationModifier_Changed;
            _cmActHotkeyGlobalChk = new CheckBox { Text = "Global hotkey", AutoSize = true, Margin = new Padding(0, 2, 0, 0) };
            _cmActHotkeyGlobalChk.CheckedChanged += CmModeActivationModifier_Changed;
            actModifiersFlow.Controls.Add(_cmActAlt);
            actModifiersFlow.Controls.Add(_cmActCtrl);
            actModifiersFlow.Controls.Add(_cmActShift);
            actModifiersFlow.Controls.Add(_cmActHotkeyGlobalChk);
            selectedModeLayout.Controls.Add(actModifiersFlow, 0, 4);

            _cmIncludeInCycleChk = new CheckBox
            {
                Text = "Include this mode in the mode-key cycle",
                AutoSize = true,
                Margin = new Padding(0, 2, 0, 0)
            };
            _cmIncludeInCycleChk.CheckedChanged += CmIncludeInCycleChk_CheckedChanged;
            selectedModeLayout.Controls.Add(_cmIncludeInCycleChk, 0, 5);

            _cmCycleWithModeHotkeyChk = new CheckBox
            {
                Text = "Allow custom modes in the mode-key cycle (uncheck to remove all custom steps)",
                Location = new Point(10, topAfterMainRow),
                Size = new Size(720, 22),
                Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right
            };
            tab.Controls.Add(_cmCycleWithModeHotkeyChk);

            const int bindingsTop = topAfterMainRow + 30;
            tab.Controls.Add(new Label { Text = "Bindings", Location = new Point(10, bindingsTop), Size = new Size(80, 18) });
            _cmBindingsList = new ListBox
            {
                Location = new Point(10, bindingsTop + 20),
                Size = new Size(350, 110),
                Anchor = AnchorStyles.Top | AnchorStyles.Left
            };
            _cmBindingsList.SelectedIndexChanged += CmBindingsList_SelectedIndexChanged;
            tab.Controls.Add(_cmBindingsList);

            _cmAddBindBtn = new Button { Text = "Add binding", Location = new Point(368, bindingsTop + 20), Size = new Size(100, 26), Anchor = AnchorStyles.Top | AnchorStyles.Left };
            _cmAddBindBtn.Click += CmAddBinding_Click;
            tab.Controls.Add(_cmAddBindBtn);
            _cmRemoveBindBtn = new Button { Text = "Remove", Location = new Point(368, bindingsTop + 52), Size = new Size(100, 26), Anchor = AnchorStyles.Top | AnchorStyles.Left };
            _cmRemoveBindBtn.Click += CmRemoveBinding_Click;
            tab.Controls.Add(_cmRemoveBindBtn);

            int ey = bindingsTop + 20 + 110 + 16;
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
            _cmSuppress = false;
            if (_cmModesList.Items.Count > 0)
                _cmModesList.SelectedIndex = 0;
            else
            {
                _cmNameText.Enabled = false;
                ClearCmBindingEditors();
                _cmSuppress = true;
                LoadCmModeActivationFromDefinition();
                _cmSuppress = false;
            }
        }

        private void RefreshCmRoleComboItems()
        {
            string prev = _cmSuppress ? null : (_cmRoleCombo.SelectedItem as string);
            _cmRoleCombo.Items.Clear();
            foreach (var t in Properties.SerializedSettings.Default.Bindings.Select(b => b.Title).Where(s => !string.IsNullOrEmpty(s)).Distinct())
                _cmRoleCombo.Items.Add(t);
            if (!_cmRoleCombo.Items.Contains(CustomModeWellKnownRoles.ZeroPowerThrow))
                _cmRoleCombo.Items.Add(CustomModeWellKnownRoles.ZeroPowerThrow);
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
            LoadCmModeActivationFromDefinition();
            _cmSuppress = false;
            RefreshCmBindingsList();
            UpdateCmEditorEnableStates();
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
            _cmModeActivationKeyPicker.Enabled = hasMode;
            _cmActAlt.Enabled = _cmActCtrl.Enabled = _cmActShift.Enabled = hasMode;
            _cmActHotkeyGlobalChk.Enabled = hasMode;
            _cmIncludeInCycleChk.Enabled = hasMode;
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
            {
                ClearCmBindingEditors();
                _cmSuppress = true;
                LoadCmModeActivationFromDefinition();
                _cmSuppress = false;
            }
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
            if (chooser == _cmInputKeyPicker || chooser == _cmRawKeyPicker)
            {
                PushCmEditorToBinding();
                UpdateCmEditorEnableStates();
            }
        }

        private void LoadCmModeActivationFromDefinition()
        {
            var m = CmSelectedMode;
            if (m == null)
            {
                _cmModeActivationKeyPicker.ChosenKey = Keys.None;
                _cmActAlt.Checked = _cmActCtrl.Checked = _cmActShift.Checked = false;
                _cmActHotkeyGlobalChk.Checked = false;
                _cmIncludeInCycleChk.Checked = true;
                return;
            }

            _cmModeActivationKeyPicker.ChosenKey = m.ActivationHotkeyCode != 0 ? (Keys)m.ActivationHotkeyCode : Keys.None;
            var mods = (Win32.KeyModifiers)m.ActivationHotkeyModifiers;
            _cmActAlt.Checked = (mods & Win32.KeyModifiers.Alt) != 0;
            _cmActCtrl.Checked = (mods & Win32.KeyModifiers.Control) != 0;
            _cmActShift.Checked = (mods & Win32.KeyModifiers.Shift) != 0;
            _cmActHotkeyGlobalChk.Checked = m.ActivationHotkeyGlobal;
            _cmIncludeInCycleChk.Checked = m.ShouldIncludeInModeHotkeyCycle();
        }

        private void PushCmModeActivationToDefinition()
        {
            var m = CmSelectedMode;
            if (m == null || _cmSuppress)
                return;
            m.ActivationHotkeyCode = (int)_cmModeActivationKeyPicker.ChosenKey;
            Win32.KeyModifiers mods = Win32.KeyModifiers.None;
            if (_cmActAlt.Checked)
                mods |= Win32.KeyModifiers.Alt;
            if (_cmActCtrl.Checked)
                mods |= Win32.KeyModifiers.Control;
            if (_cmActShift.Checked)
                mods |= Win32.KeyModifiers.Shift;
            m.ActivationHotkeyModifiers = (int)mods;
            m.ActivationHotkeyGlobal = _cmActHotkeyGlobalChk.Checked;
            m.IncludeInModeHotkeyCycle = _cmIncludeInCycleChk.Checked ? (bool?)null : false;
        }

        private void CmModeActivationKeyPicker_KeyChosen(KeyPicker chooser, Keys keyChosen)
        {
            PushCmModeActivationToDefinition();
        }

        private void CmModeActivationModifier_Changed(object sender, EventArgs e)
        {
            if (_cmSuppress)
                return;
            PushCmModeActivationToDefinition();
        }

        private void CmIncludeInCycleChk_CheckedChanged(object sender, EventArgs e)
        {
            if (_cmSuppress)
                return;
            PushCmModeActivationToDefinition();
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
