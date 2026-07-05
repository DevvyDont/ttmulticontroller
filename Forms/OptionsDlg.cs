using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Windows.Forms;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Threading.Tasks;
using TTMulti.Controls;
using System.Net;
using System.Threading;
using System.Diagnostics;
using System.Reflection;
using System.IO;

namespace TTMulti.Forms
{
    public partial class OptionsDlg : Form
    {

        public OptionsDlg()
        {
            InitializeComponent();
            this.Icon = Properties.Resources.icon;

            // The dialog is resizable now (UX-09). Lock today's (DPI-scaled) layout size as the minimum so nothing
            // can be shrunk out of view, while letting users enlarge the window to see tab content that currently
            // requires scrolling. The tab control and buttons are already anchored, so growing lays out correctly.
            this.MinimumSize = this.Size;
        }

        [DataContract]
        private class GitHubRelease
        {
            [DataMember(Name = "tag_name")]
            public string TagName { get; set; }

            [DataMember(Name = "html_url")]
            public string HtmlUrl { get; set; }

            [DataMember(Name = "prerelease")]
            public bool Prerelease { get; set; }
        }

        /// <summary>
        /// Checks the latest published release on the project's GitHub repository (derived from homepageUrl) and,
        /// if it is newer than this build, offers to open its download page. Async with an 8s timeout — no
        /// Thread.Abort / DoEvents pump, and no dependency on ClickOnce (which the standalone build no longer uses).
        /// </summary>
        private async Task CheckForUpdatesAsync()
        {
            checkUpdateBtn.Enabled = false;
            this.UseWaitCursor = true;

            try
            {
                GitHubRelease release = await FetchLatestReleaseAsync();

                if (release == null || string.IsNullOrEmpty(release.TagName))
                {
                    MessageBox.Show(this,
                        "Could not check for updates. Please check your internet connection and try again later.",
                        "Update check failed", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }

                if (IsNewerVersion(release.TagName, Application.ProductVersion))
                {
                    string message = string.Format(
                        "An update is available: {0} (you have {1}).\n\nWould you like to open the download page?",
                        release.TagName, Application.ProductVersion);

                    if (MessageBox.Show(this, message, "Update available",
                            MessageBoxButtons.YesNo, MessageBoxIcon.Information) == DialogResult.Yes)
                    {
                        string target = !string.IsNullOrEmpty(release.HtmlUrl)
                            ? release.HtmlUrl
                            : Properties.Settings.Default.homepageUrl;

                        // UseShellExecute is required to launch a URL: it defaults to false on modern .NET.
                        try { Process.Start(new ProcessStartInfo(target) { UseShellExecute = true }); }
                        catch { /* No browser / blocked; nothing useful to do. */ }
                    }
                }
                else
                {
                    MessageBox.Show(this, "You already have the latest version.",
                        "No updates available", MessageBoxButtons.OK, MessageBoxIcon.Information);
                }
            }
            finally
            {
                this.UseWaitCursor = false;
                checkUpdateBtn.Enabled = true;
            }
        }

        private static async Task<GitHubRelease> FetchLatestReleaseAsync()
        {
            string apiUrl = BuildReleasesApiUrl(Properties.Settings.Default.homepageUrl);
            if (apiUrl == null)
                return null;

            try
            {
                // ServicePointManager/WebRequest/HttpWebRequest are obsolete (SYSLIB0014) on modern .NET but still
                // functional; the HttpClient rewrite lands in a follow-up (fix/update-check-httpclient) to keep this
                // retarget mechanical.
#pragma warning disable SYSLIB0014
                // GitHub's API requires TLS 1.2+ and a User-Agent; enable Tls12 without clobbering existing flags.
                ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;

                HttpWebRequest request = (HttpWebRequest)WebRequest.Create(apiUrl);
#pragma warning restore SYSLIB0014
                request.UserAgent = "ToontownMulticontroller";
                request.Accept = "application/vnd.github+json";

                Task<WebResponse> responseTask = request.GetResponseAsync();
                if (await Task.WhenAny(responseTask, Task.Delay(8000)) != responseTask)
                {
                    request.Abort();
                    return null;
                }

                using (HttpWebResponse response = (HttpWebResponse)await responseTask)
                using (StreamReader sr = new StreamReader(response.GetResponseStream()))
                {
                    string json = await sr.ReadToEndAsync();
                    return ParseRelease(json);
                }
            }
            catch
            {
                // No network, 404, DNS failure, aborted-on-timeout, malformed response — treat as "can't check".
                return null;
            }
        }

        private static GitHubRelease ParseRelease(string json)
        {
            if (string.IsNullOrWhiteSpace(json))
                return null;

            try
            {
                var serializer = new DataContractJsonSerializer(typeof(GitHubRelease));
                using (var ms = new MemoryStream(Encoding.UTF8.GetBytes(json)))
                {
                    return (GitHubRelease)serializer.ReadObject(ms);
                }
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// Turns the homepage URL (https://github.com/owner/repo) into the "latest release" API endpoint. Returns
        /// null if the homepage isn't a recognizable GitHub repo URL, so a misconfigured setting fails quietly.
        /// </summary>
        private static string BuildReleasesApiUrl(string homepageUrl)
        {
            if (string.IsNullOrWhiteSpace(homepageUrl))
                return null;

            const string prefix = "https://github.com/";
            string trimmed = homepageUrl.Trim().TrimEnd('/');
            if (!trimmed.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                return null;

            string ownerRepo = trimmed.Substring(prefix.Length);
            if (ownerRepo.Split('/').Length != 2)
                return null;

            return "https://api.github.com/repos/" + ownerRepo + "/releases/latest";
        }

        private static bool IsNewerVersion(string latestTag, string current)
        {
            Version latest = ParseVersion(latestTag);
            Version installed = ParseVersion(current);

            if (latest != null && installed != null)
                return latest > installed;

            // If either side can't be parsed as a version, fall back to a conservative "different means newer".
            return !string.Equals((latestTag ?? "").Trim(), (current ?? "").Trim(), StringComparison.OrdinalIgnoreCase);
        }

        private static Version ParseVersion(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return null;

            // Accept tags like "v1.4", "1.4.0", "1.4.0-beta": strip a leading v and any non-version suffix.
            string trimmed = text.Trim().TrimStart('v', 'V');
            int end = 0;
            while (end < trimmed.Length && (char.IsDigit(trimmed[end]) || trimmed[end] == '.'))
                end++;
            trimmed = trimmed.Substring(0, end);

            return Version.TryParse(trimmed, out Version v) ? v : null;
        }

        // Auto-find controls
        private GroupBox autoFindGroupBox;
        private KeyPicker autoFindKeyPicker;
        private CheckBox autoFindAltCheckBox;
        private CheckBox autoFindCtrlCheckBox;
        private CheckBox autoFindShiftCheckBox;
        private CheckBox autoFindPlacementOnAltReleaseCheckBox;
        private TextBox autoFindExecutablesTextBox;
        private Label autoFindExecutablesLabel;
        // Switching Mode controls
        private GroupBox switchingModeGroupBox;
        private CheckBox switchingModeEnabledCheckBox;
        private ComboBox switchingModeSwitchComboBox;
        private KeyPicker switchingModeSwitchKeyPicker;
        private KeyPicker switchingModeRemoveKeyPicker;
        private Label switchingModeDescriptionLabel;

        // Colors controls
        private TabPage colorsTabPage;
        private Button mirrorModeBorderColorButton;
        private Button multiModeLeftBorderColorButton;
        private Button multiModeRightBorderColorButton;
        private Button switchingModeColorButton;
        private Button switchingSelectedColorButton;
        private Button switchingSwitchedColorButton;
        private Button switchingRemovedColorButton;
        private Button focusedModeFocusedColorButton;
        private Button focusedModeUnfocusedColorButton;
        
        // Caption color control
        private CheckBox enableCaptionColorCheckBox;

        // Controlled Multi-Click controls
        private CheckBox controlledMcEnabledCheckBox;
        private Controls.KeyPicker controlledMcActivateKeyPicker;
        private RadioButton controlledMcToggleRadio;
        private RadioButton controlledMcHoldRadio;
        private CheckBox controlledMcActivateGlobalCheckBox;
        private Controls.KeyPicker controlledMcClickKeyPicker;
        private CheckBox controlledMcClickUseMouseCheckBox;
        private ComboBox controlledMcClickMouseCombo;
        private Controls.KeyPicker controlledMcRegularClickKeyPicker;
        private CheckBox controlledMcRegularClickUseMouseCheckBox;
        private ComboBox controlledMcRegularClickMouseCombo;
        private CheckBox controlledMcRegularClickTriggerOnReleaseCheckBox;
        private CheckBox controlledMcClickTriggerOnReleaseCheckBox;
        private CheckBox controlledMcClickSeparateLRCheckBox;

        // Minimize unconnected Toontown windows controls
        private GroupBox minimizeUnconnectedGroupBox;
        private KeyPicker minimizeUnconnectedKeyPicker;
        private CheckBox minimizeUnconnectedAltCheckBox;
        private CheckBox minimizeUnconnectedCtrlCheckBox;
        private CheckBox minimizeUnconnectedShiftCheckBox;
        private CheckBox minimizeUnconnectedHotkeyGlobalCheckBox;

        // Mode lock (Other tab)
        private GroupBox modeLockGroupBox;
        private KeyPicker modeLockToggleKeyPicker;

        // Suspend global hotkeys (Other tab)
        private GroupBox suspendGlobalHotkeysGroupBox;
        private KeyPicker suspendGlobalHotkeysToggleKeyPicker;

        // Layout presets
        private LayoutPresetFile _layoutPresetFile;
        private ListBox _layoutPresetsListBox;
        private CheckBox _layoutPriorityLeftsFirstCheckBox;

        private void OptionsDlg_Load(object sender, EventArgs e)
        {
            controlsPicker.KeyMappings = Properties.SerializedSettings.Default.Bindings;

            // The tabs below are built in code with coordinates calibrated for 120 DPI and are added AFTER the form
            // auto-scaled its designer controls, so they never get rescaled and misrender at 100% / 150% scaling.
            // Snapshot the existing (designer) controls now so we can scale only the runtime-added ones by the same
            // factor the designer controls received (UX-07).
            float dpiScale = AutoScaleDimensions.Width > 1f
                ? CurrentAutoScaleDimensions.Width / AutoScaleDimensions.Width
                : 1f;
            var designerControls = new HashSet<Control>();
            foreach (TabPage tp in tabControl1.TabPages)
                foreach (Control c in tp.Controls)
                    designerControls.Add(c);

            CreateSwitchingModeUI();
            LoadSwitchingModeSettings();

            CreateAutoFindTab();
            LoadAutoFindSettings();

            CreateLayoutPresetsTab();
            LoadLayoutPresets();

            CreateCustomModesTab();
            LoadCustomModes();

            CreateColorsTab();
            LoadColorsSettings();
            
            CreateCaptionColorUI();
            LoadCaptionColorSettings();

            CreateMinimizeUnconnectedUI();
            LoadMinimizeUnconnectedSettings();

            CreateModeLockUI();
            LoadModeLockSettings();

            CreateSuspendGlobalHotkeysUI();
            LoadSuspendGlobalHotkeysSettings();

            CreateControlledMulticlickTab();
            LoadControlledMulticlickSettings();

            LoadMulticlickMouseSettings();
            
            // Load Keep-Alive checkbox state
            // disableKeepAlive = True (default) means Keep-Alive is disabled, so checkbox should be unchecked
            // disableKeepAlive = False means Keep-Alive is enabled, so checkbox should be checked
            if (checkBox4 != null)
            {
                // Attach event handler after loading state to prevent it from firing during load
                checkBox4.CheckedChanged += checkBox4_CheckedChanged;
                checkBox4.Checked = !Properties.Settings.Default.disableKeepAlive;
            }
            
            // Scale everything added at runtime (i.e. not in the pre-build designer snapshot) so the runtime tabs
            // match the auto-scaled designer controls at the current DPI (UX-07).
            if (Math.Abs(dpiScale - 1f) > 0.01f)
            {
                foreach (TabPage tp in tabControl1.TabPages)
                    foreach (Control c in tp.Controls)
                        if (!designerControls.Contains(c))
                            c.Scale(new SizeF(dpiScale, dpiScale));
            }

            // Reorder tabs to match desired order (use TabPage.Name, not Text, so captions can change in the designer)
            var tabPage6 = tabControl1.TabPages.Cast<TabPage>().FirstOrDefault(t => t.Name == "tabPage6");
            var tabPage3 = tabControl1.TabPages.Cast<TabPage>().FirstOrDefault(t => t.Name == "tabPage3");
            var multiClickTab = tabControl1.TabPages.Cast<TabPage>().FirstOrDefault(t => t.Name == "multiClickTab");
            var tabPage1 = tabControl1.TabPages.Cast<TabPage>().FirstOrDefault(t => t.Name == "tabPage1");
            var autoFindTab = tabControl1.TabPages.Cast<TabPage>().FirstOrDefault(t => t.Name == "autoFindTab");
            var layoutPresetsTab = tabControl1.TabPages.Cast<TabPage>().FirstOrDefault(t => t.Name == "layoutPresetsTab");
            var customModesTab = tabControl1.TabPages.Cast<TabPage>().FirstOrDefault(t => t.Name == "customModesTab");
            var colorsTab = tabControl1.TabPages.Cast<TabPage>().FirstOrDefault(t => t.Name == "colorsTabPage");
            var tabPage2 = tabControl1.TabPages.Cast<TabPage>().FirstOrDefault(t => t.Name == "tabPage2");
            
            tabControl1.TabPages.Clear();
            // Order: Multi-Mode Keys, Hotkeys, Multi-Click, Controller Modes, Custom Modes, Auto-Find, Layout Presets, Colors, Other
            if (tabPage6 != null) tabControl1.TabPages.Add(tabPage6);
            if (tabPage3 != null) tabControl1.TabPages.Add(tabPage3);
            if (multiClickTab != null) tabControl1.TabPages.Add(multiClickTab);
            if (tabPage1 != null) tabControl1.TabPages.Add(tabPage1);
            if (customModesTab != null) tabControl1.TabPages.Add(customModesTab);
            if (autoFindTab != null) tabControl1.TabPages.Add(autoFindTab);
            if (layoutPresetsTab != null) tabControl1.TabPages.Add(layoutPresetsTab);
            if (colorsTab != null) tabControl1.TabPages.Add(colorsTab);
            if (tabPage2 != null) tabControl1.TabPages.Add(tabPage2);

            ConfigureOtherTabScrolling();
        }

        private void OptionsDlg_Shown(object sender, EventArgs e)
        {
            // Docked children do not extend scroll metrics until layout is complete; refresh after first display.
            ConfigureOtherTabScrolling();
        }

        /// <summary>
        /// Other tab stacks many docked group boxes; enable vertical scroll when content exceeds the tab height.
        /// </summary>
        private void ConfigureOtherTabScrolling()
        {
            var otherTab = tabControl1.TabPages.Cast<TabPage>().FirstOrDefault(t => t.Name == "tabPage2");
            if (otherTab == null)
                return;

            otherTab.AutoScroll = true;
            otherTab.PerformLayout();

            int maxBottom = otherTab.Padding.Top;
            foreach (Control c in otherTab.Controls)
            {
                if (!c.Visible)
                    continue;
                maxBottom = Math.Max(maxBottom, c.Bottom);
            }

            int contentHeight = maxBottom + otherTab.Padding.Bottom + otherTab.AutoScrollMargin.Height;
            contentHeight = Math.Max(contentHeight, otherTab.ClientSize.Height);
            otherTab.AutoScrollMinSize = new Size(0, contentHeight);
        }

        private void CreateAutoFindTab()
        {
            // Create a new tab page for Auto-Find
            var autoFindTab = new TabPage("Auto-Find");
            autoFindTab.Name = "autoFindTab";
            autoFindTab.AutoScroll = true;
            autoFindTab.Padding = new Padding(10);
            autoFindTab.UseVisualStyleBackColor = true;
            
            // Add the tab to the tab control
            tabControl1.TabPages.Add(autoFindTab);

            // Create main group box
            autoFindGroupBox = new GroupBox
            {
                Text = "Auto-Find Windows",
                Location = new Point(10, 10),
                Size = new Size(720, 225),
                Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right
            };

            // Description label
            var descLabel = new Label
            {
                Text = "Automatically find and assign windows from recognized game executables. " +
                       "Windows are assigned sequentially:\n Group 1 Left, Group 1 Right, Group 2 Left, Group 2 Right, etc.",
                Location = new Point(10, 25),
                Size = new Size(700, 50),
                Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right
            };
            autoFindGroupBox.Controls.Add(descLabel);

            // Executables label
            autoFindExecutablesLabel = new Label
            {
                Text = "Executables (semicolon-separated):",
                Location = new Point(10, 85),
                Size = new Size(300, 20)
            };
            autoFindGroupBox.Controls.Add(autoFindExecutablesLabel);

            // Executables text box
            autoFindExecutablesTextBox = new TextBox
            {
                Location = new Point(10, 105),
                Size = new Size(500, 23),
                Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right
            };
            autoFindGroupBox.Controls.Add(autoFindExecutablesTextBox);

            // Hotkey section label
            var hotkeySectionLabel = new Label
            {
                Text = "Hotkey Configuration:",
                Location = new Point(10, 170),
                Size = new Size(200, 20),
                Font = new Font(autoFindGroupBox.Font, FontStyle.Bold)
            };
            autoFindGroupBox.Controls.Add(hotkeySectionLabel);

            // Hotkey label
            var hotkeyLabel = new Label
            {
                Text = "Hotkey:",
                Location = new Point(10, 195),
                Size = new Size(60, 20)
            };
            autoFindGroupBox.Controls.Add(hotkeyLabel);

            // Hotkey picker
            autoFindKeyPicker = new KeyPicker
            {
                Location = new Point(80, 193),
                Size = new Size(120, 23)
            };
            autoFindGroupBox.Controls.Add(autoFindKeyPicker);

            // Modifier checkboxes
            autoFindAltCheckBox = new CheckBox
            {
                Text = "Alt",
                Location = new Point(210, 195),
                Size = new Size(50, 20)
            };
            autoFindGroupBox.Controls.Add(autoFindAltCheckBox);

            autoFindCtrlCheckBox = new CheckBox
            {
                Text = "Ctrl",
                Location = new Point(270, 195),
                Size = new Size(50, 20)
            };
            autoFindGroupBox.Controls.Add(autoFindCtrlCheckBox);

            autoFindShiftCheckBox = new CheckBox
            {
                Text = "Shift",
                Location = new Point(330, 195),
                Size = new Size(60, 20)
            };
            autoFindGroupBox.Controls.Add(autoFindShiftCheckBox);

            // Add group box to tab
            autoFindTab.Controls.Add(autoFindGroupBox);
        }

        private void CreateLayoutPresetsTab()
        {
            var tab = new TabPage("Layout Presets");
            tab.Name = "layoutPresetsTab";
            tab.AutoScroll = true;
            tab.Padding = new Padding(10);
            tab.UseVisualStyleBackColor = true;
            tabControl1.TabPages.Add(tab);

            var groupBox = new GroupBox
            {
                Text = "Layout Presets",
                Location = new Point(10, 10),
                Size = new Size(570, 300),
                Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right
            };
            tab.Controls.Add(groupBox);

            var listLabel = new Label { Text = "Presets (hotkey applies the layout to controller windows):", Location = new Point(10, 22), Size = new Size(450, 20) };
            groupBox.Controls.Add(listLabel);
            _layoutPresetsListBox = new ListBox
            {
                Location = new Point(10, 45),
                Size = new Size(350, 160),
                DisplayMember = "Name"
            };
            groupBox.Controls.Add(_layoutPresetsListBox);

            var addPresetBtn = new Button { Text = "Add", Location = new Point(370, 45), Size = new Size(75, 28) };
            addPresetBtn.Click += LayoutPresetAdd_Click;
            groupBox.Controls.Add(addPresetBtn);
            var editPresetBtn = new Button { Text = "Edit", Location = new Point(370, 78), Size = new Size(75, 28) };
            editPresetBtn.Click += LayoutPresetEdit_Click;
            groupBox.Controls.Add(editPresetBtn);
            var deletePresetBtn = new Button { Text = "Delete", Location = new Point(370, 111), Size = new Size(75, 28) };
            deletePresetBtn.Click += LayoutPresetDelete_Click;
            groupBox.Controls.Add(deletePresetBtn);

            _layoutPriorityLeftsFirstCheckBox = new CheckBox
            {
                Text = "Lefts first (controller order when applying preset)",
                Location = new Point(10, 215),
                Size = new Size(400, 20),
                Anchor = AnchorStyles.Top | AnchorStyles.Left
            };
            groupBox.Controls.Add(_layoutPriorityLeftsFirstCheckBox);
            var orderExplainLabel = new Label
            {
                Text = "When applying: Lefts first = G1P1L, G1P2L, G1P1R, G1P2R... ; Pairs first = G1P1L, G1P1R, G1P2L, G1P2R...",
                Location = new Point(26, 238),
                Size = new Size(540, 36),
                Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right,
                AutoSize = false,
                ForeColor = SystemColors.GrayText
            };
            groupBox.Controls.Add(orderExplainLabel);
        }

        private void LoadLayoutPresets()
        {
            _layoutPresetFile = LayoutPresetStorage.Load();
            if (_layoutPresetFile?.Presets == null) _layoutPresetFile = new LayoutPresetFile();
            _layoutPresetsListBox.Items.Clear();
            foreach (var p in _layoutPresetFile.Presets)
                _layoutPresetsListBox.Items.Add(p);
            _layoutPriorityLeftsFirstCheckBox.Checked = Properties.Settings.Default.layoutPriorityLeftsFirst;
        }

        private void SaveLayoutPresets()
        {
            Properties.Settings.Default.layoutPriorityLeftsFirst = _layoutPriorityLeftsFirstCheckBox.Checked;
            if (_layoutPresetFile != null)
                LayoutPresetStorage.Save(_layoutPresetFile);
        }

        private void LayoutPresetAdd_Click(object sender, EventArgs e)
        {
            var preset = new LayoutPreset { Name = "New Preset", Regions = new List<LayoutRegion> { new LayoutRegion { Source = LayoutRegionSource.Monitor, MonitorIndex = 0, Rows = 2, Cols = 2 } }, SlotOverrides = new List<SlotOverride>() };
            using (var editor = new LayoutPresetEditorForm(preset))
            {
                if (editor.ShowDialog(this) == DialogResult.OK)
                {
                    _layoutPresetFile.Presets.Add(editor.Preset);
                    _layoutPresetsListBox.Items.Add(editor.Preset);
                }
            }
        }

        private void LayoutPresetEdit_Click(object sender, EventArgs e)
        {
            var selected = _layoutPresetsListBox.SelectedItem as LayoutPreset;
            if (selected == null) { MessageBox.Show("Select a preset to edit."); return; }
            int index = _layoutPresetsListBox.SelectedIndex;
            var copy = new LayoutPreset
            {
                Name = selected.Name,
                HotkeyCode = selected.HotkeyCode,
                HotkeyModifiers = selected.HotkeyModifiers,
                Regions = selected.Regions.Select(r => new LayoutRegion
                {
                    Source = r.Source,
                    MonitorIndex = r.MonitorIndex,
                    CustomX = r.CustomX, CustomY = r.CustomY, CustomWidth = r.CustomWidth, CustomHeight = r.CustomHeight,
                    Rows = r.Rows, Cols = r.Cols,
                    RowWeights = r.RowWeights?.ToArray(),
                    ColWeights = r.ColWeights?.ToArray()
                }).ToList(),
                SlotOverrides = selected.SlotOverrides.Select(o => new SlotOverride { SlotIndex = o.SlotIndex, Rect = o.Rect != null ? new LayoutRect { X = o.Rect.X, Y = o.Rect.Y, Width = o.Rect.Width, Height = o.Rect.Height } : null, Minimized = o.Minimized }).ToList()
            };
            using (var editor = new LayoutPresetEditorForm(copy))
            {
                if (editor.ShowDialog(this) == DialogResult.OK)
                {
                    _layoutPresetFile.Presets[index] = editor.Preset;
                    _layoutPresetsListBox.Items[index] = editor.Preset;
                }
            }
        }

        private void LayoutPresetDelete_Click(object sender, EventArgs e)
        {
            int index = _layoutPresetsListBox.SelectedIndex;
            if (index < 0) { MessageBox.Show("Select a preset to delete."); return; }
            string presetName = _layoutPresetsListBox.Items[index]?.ToString() ?? "this preset";
            if (MessageBox.Show(this, $"Delete layout preset \"{presetName}\"?", "Delete Preset",
                    MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes)
                return;
            _layoutPresetFile.Presets.RemoveAt(index);
            _layoutPresetsListBox.Items.RemoveAt(index);
        }

        private void CreateSwitchingModeUI()
        {
            // Get the Controller Modes tab (tabPage1)
            var controllerModesTab = tabControl1.TabPages.Cast<TabPage>().FirstOrDefault(t => t.Name == "tabPage1");
            if (controllerModesTab == null)
                return;

            // Create main group box
            switchingModeGroupBox = new GroupBox
            {
                Text = "Switching Mode Configuration",
                Dock = DockStyle.Bottom,
                Padding = new Padding(10),
                Height = 330
            };

            // Enabled checkbox
            switchingModeEnabledCheckBox = new CheckBox
            {
                Text = "Enable Switching Mode",
                Location = new Point(10, 20),
                Size = new Size(200, 20),
                Checked = true
            };
            switchingModeGroupBox.Controls.Add(switchingModeEnabledCheckBox);

            // Description label
            switchingModeDescriptionLabel = new Label
            {
                Text = "Switching Mode allows you to reorganize windows by swapping their controller assignments.\n\n" +
                       "• Hold Alt to enter Switching Mode (all windows show red borders with numbers)\n" +
                       "• Use the keybinds below to select/switch windows or mark them for removal\n" +
                       "• Release Alt to exit Switching Mode and apply changes",
                Location = new Point(10, 45),
                Size = new Size(700, 80),
                Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right
            };
            switchingModeGroupBox.Controls.Add(switchingModeDescriptionLabel);

            // Switch keybind section
            var switchLabel = new Label
            {
                Text = "Switch/Select Keybind:",
                Location = new Point(10, 135),
                Size = new Size(200, 20),
                Font = new Font(switchingModeGroupBox.Font, FontStyle.Bold)
            };
            switchingModeGroupBox.Controls.Add(switchLabel);

            var switchDescLabel = new Label
            {
                Text = "Press this key (or click) on a window to select it. Press again on another window to swap them.",
                Location = new Point(10, 155),
                Size = new Size(700, 20)
            };
            switchingModeGroupBox.Controls.Add(switchDescLabel);

            var switchKeyLabel = new Label
            {
                Text = "Key:",
                Location = new Point(10, 180),
                Size = new Size(60, 20)
            };
            switchingModeGroupBox.Controls.Add(switchKeyLabel);

            // ComboBox for selecting mouse button or keyboard key
            switchingModeSwitchComboBox = new ComboBox
            {
                Location = new Point(80, 178),
                Size = new Size(150, 23),
                DropDownStyle = ComboBoxStyle.DropDownList
            };
            switchingModeSwitchComboBox.Items.AddRange(new object[] {
                "Left Mouse Button",
                "Right Mouse Button",
                "Middle Mouse Button",
                "Keyboard Key..."
            });
            switchingModeSwitchComboBox.SelectedIndexChanged += SwitchingModeSwitchComboBox_SelectedIndexChanged;
            switchingModeGroupBox.Controls.Add(switchingModeSwitchComboBox);

            // KeyPicker for keyboard key selection (initially hidden)
            switchingModeSwitchKeyPicker = new KeyPicker
            {
                Location = new Point(240, 178),
                Size = new Size(120, 23),
                Visible = false
            };
            switchingModeGroupBox.Controls.Add(switchingModeSwitchKeyPicker);

            // Remove keybind section
            var removeLabel = new Label
            {
                Text = "Remove Keybind:",
                Location = new Point(10, 210),
                Size = new Size(200, 20),
                Font = new Font(switchingModeGroupBox.Font, FontStyle.Bold)
            };
            switchingModeGroupBox.Controls.Add(removeLabel);

            var removeDescLabel = new Label
            {
                Text = "Press this key on a window to mark it for removal (black highlight). Release Alt to remove all marked windows.",
                Location = new Point(10, 230),
                Size = new Size(700, 20)
            };
            switchingModeGroupBox.Controls.Add(removeDescLabel);

            var removeKeyLabel = new Label
            {
                Text = "Key:",
                Location = new Point(10, 255),
                Size = new Size(60, 20)
            };
            switchingModeGroupBox.Controls.Add(removeKeyLabel);

            switchingModeRemoveKeyPicker = new KeyPicker
            {
                Location = new Point(80, 253),
                Size = new Size(120, 23)
            };
            switchingModeGroupBox.Controls.Add(switchingModeRemoveKeyPicker);

            // Auto-placement on Alt release
            autoFindPlacementOnAltReleaseCheckBox = new CheckBox
            {
                Text = "Exchange swapped windows' screen position and size when releasing Alt",
                Location = new Point(10, 283),
                Size = new Size(690, 20),
                Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right,
                Checked = Properties.Settings.Default.autoFindPlacementOnAltRelease
            };
            autoFindPlacementOnAltReleaseCheckBox.DataBindings.Add(new Binding("Checked", Properties.Settings.Default, "autoFindPlacementOnAltRelease", true, DataSourceUpdateMode.OnPropertyChanged));
            switchingModeGroupBox.Controls.Add(autoFindPlacementOnAltReleaseCheckBox);

            // Add group box to Controller Modes tab
            controllerModesTab.Controls.Add(switchingModeGroupBox);
        }

        private void SwitchingModeSwitchComboBox_SelectedIndexChanged(object sender, EventArgs e)
        {
            if (switchingModeSwitchComboBox == null)
                return;

            // Show/hide KeyPicker based on selection
            // Index 0-2: Mouse buttons, Index 3: Keyboard Key
            switchingModeSwitchKeyPicker.Visible = switchingModeSwitchComboBox.SelectedIndex == 3;
        }

        private void LoadSwitchingModeSettings()
        {
            if (switchingModeSwitchComboBox == null)
                return;

            // Load enabled checkbox
            switchingModeEnabledCheckBox.Checked = Properties.Settings.Default.switchingModeEnabled;

            int switchKeyCode = Properties.Settings.Default.switchingModeSwitchKeyCode;
            
            // Map key codes to ComboBox indices:
            // 1 = Left Mouse Button (LButton)
            // 2 = Right Mouse Button (RButton)
            // 4 = Middle Mouse Button (MButton)
            // Other = Keyboard Key
            
            if (switchKeyCode == 1)
            {
                switchingModeSwitchComboBox.SelectedIndex = 0; // Left Mouse Button
            }
            else if (switchKeyCode == 2)
            {
                switchingModeSwitchComboBox.SelectedIndex = 1; // Right Mouse Button
            }
            else if (switchKeyCode == 4)
            {
                switchingModeSwitchComboBox.SelectedIndex = 2; // Middle Mouse Button
            }
            else
            {
                switchingModeSwitchComboBox.SelectedIndex = 3; // Keyboard Key
                switchingModeSwitchKeyPicker.ChosenKey = (Keys)switchKeyCode;
                switchingModeSwitchKeyPicker.Visible = true;
            }
            
            // Load remove keybind (default: X key = 88)
            switchingModeRemoveKeyPicker.ChosenKey = (Keys)Properties.Settings.Default.switchingModeRemoveKeyCode;
        }

        private void SaveSwitchingModeSettings()
        {
            if (switchingModeSwitchComboBox == null)
                return;

            // Save enabled checkbox
            Properties.Settings.Default.switchingModeEnabled = switchingModeEnabledCheckBox.Checked;

            // Map ComboBox selection to key code
            int switchKeyCode;
            switch (switchingModeSwitchComboBox.SelectedIndex)
            {
                case 0: // Left Mouse Button
                    switchKeyCode = 1;
                    break;
                case 1: // Right Mouse Button
                    switchKeyCode = 2;
                    break;
                case 2: // Middle Mouse Button
                    switchKeyCode = 4;
                    break;
                case 3: // Keyboard Key
                default:
                    switchKeyCode = (int)switchingModeSwitchKeyPicker.ChosenKey;
                    break;
            }

            Properties.Settings.Default.switchingModeSwitchKeyCode = switchKeyCode;
            Properties.Settings.Default.switchingModeRemoveKeyCode = (int)switchingModeRemoveKeyPicker.ChosenKey;
        }

        private void CreateColorsTab()
        {
            // Create a new tab page for Colors
            colorsTabPage = new TabPage("Colors");
            colorsTabPage.Name = "colorsTabPage";
            colorsTabPage.AutoScroll = true;
            colorsTabPage.Padding = new Padding(10);
            colorsTabPage.UseVisualStyleBackColor = true;
            
            // Add the tab to the tab control
            tabControl1.TabPages.Add(colorsTabPage);

            // Create main group box
            var colorsGroupBox = new GroupBox
            {
                Text = "Border Colors",
                Location = new Point(10, 10),
                Size = new Size(720, 480),
                Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right
            };
            colorsTabPage.Controls.Add(colorsGroupBox);

            int yPos = 25;
            int labelWidth = 250;
            int buttonHeight = 30;
            int spacing = 40;

            // Mirror Mode Border Color
            var mirrorLabel = new Label
            {
                Text = "Mirror Mode Border:",
                Location = new Point(10, yPos),
                Size = new Size(labelWidth, 20)
            };
            colorsGroupBox.Controls.Add(mirrorLabel);

            // Color swatch button
            mirrorModeBorderColorButton = new Button
            {
                Text = "",
                Location = new Point(labelWidth + 20, yPos - 2),
                Size = new Size(40, buttonHeight),
                BackColor = Color.FromArgb(238, 130, 238), // Default RGB color
                FlatStyle = FlatStyle.Flat,
                UseVisualStyleBackColor = false
            };
            mirrorModeBorderColorButton.FlatAppearance.BorderSize = 1;
            mirrorModeBorderColorButton.FlatAppearance.BorderColor = Color.Gray;
            colorsGroupBox.Controls.Add(mirrorModeBorderColorButton);

            // Change button
            var mirrorChangeBtn = new Button
            {
                Text = "Change",
                Location = new Point(labelWidth + 70, yPos - 2),
                Size = new Size(70, buttonHeight)
            };
            mirrorChangeBtn.Click += (s, e) => ShowColorDialog(mirrorModeBorderColorButton, Color.FromArgb(238, 130, 238));
            colorsGroupBox.Controls.Add(mirrorChangeBtn);

            // Reset button
            var mirrorResetBtn = new Button
            {
                Text = "Reset",
                Location = new Point(labelWidth + 150, yPos - 2),
                Size = new Size(70, buttonHeight)
            };
            mirrorResetBtn.Click += (s, e) => { mirrorModeBorderColorButton.BackColor = Color.FromArgb(238, 130, 238); };
            colorsGroupBox.Controls.Add(mirrorResetBtn);

            yPos += spacing;

            // Multi Mode Left Border Color
            var multiLeftLabel = new Label
            {
                Text = "Multi Mode (Left) Border:",
                Location = new Point(10, yPos),
                Size = new Size(labelWidth, 20)
            };
            colorsGroupBox.Controls.Add(multiLeftLabel);

            multiModeLeftBorderColorButton = new Button
            {
                Text = "",
                Location = new Point(labelWidth + 20, yPos - 2),
                Size = new Size(40, buttonHeight),
                BackColor = Color.FromArgb(50, 205, 50), // Default RGB color
                FlatStyle = FlatStyle.Flat,
                UseVisualStyleBackColor = false
            };
            multiModeLeftBorderColorButton.FlatAppearance.BorderSize = 1;
            multiModeLeftBorderColorButton.FlatAppearance.BorderColor = Color.Gray;
            colorsGroupBox.Controls.Add(multiModeLeftBorderColorButton);

            var multiLeftChangeBtn = new Button
            {
                Text = "Change",
                Location = new Point(labelWidth + 70, yPos - 2),
                Size = new Size(70, buttonHeight)
            };
            multiLeftChangeBtn.Click += (s, e) => ShowColorDialog(multiModeLeftBorderColorButton, Color.FromArgb(50, 205, 50));
            colorsGroupBox.Controls.Add(multiLeftChangeBtn);

            var multiLeftResetBtn = new Button
            {
                Text = "Reset",
                Location = new Point(labelWidth + 150, yPos - 2),
                Size = new Size(70, buttonHeight)
            };
            multiLeftResetBtn.Click += (s, e) => { multiModeLeftBorderColorButton.BackColor = Color.FromArgb(50, 205, 50); };
            colorsGroupBox.Controls.Add(multiLeftResetBtn);

            yPos += spacing;

            // Multi Mode Right Border Color
            var multiRightLabel = new Label
            {
                Text = "Multi Mode (Right) Border:",
                Location = new Point(10, yPos),
                Size = new Size(labelWidth, 20)
            };
            colorsGroupBox.Controls.Add(multiRightLabel);

            multiModeRightBorderColorButton = new Button
            {
                Text = "",
                Location = new Point(labelWidth + 20, yPos - 2),
                Size = new Size(40, buttonHeight),
                BackColor = Color.FromArgb(0, 100, 0), // Default RGB color
                FlatStyle = FlatStyle.Flat,
                UseVisualStyleBackColor = false
            };
            multiModeRightBorderColorButton.FlatAppearance.BorderSize = 1;
            multiModeRightBorderColorButton.FlatAppearance.BorderColor = Color.Gray;
            colorsGroupBox.Controls.Add(multiModeRightBorderColorButton);

            var multiRightChangeBtn = new Button
            {
                Text = "Change",
                Location = new Point(labelWidth + 70, yPos - 2),
                Size = new Size(70, buttonHeight)
            };
            multiRightChangeBtn.Click += (s, e) => ShowColorDialog(multiModeRightBorderColorButton, Color.FromArgb(0, 100, 0));
            colorsGroupBox.Controls.Add(multiRightChangeBtn);

            var multiRightResetBtn = new Button
            {
                Text = "Reset",
                Location = new Point(labelWidth + 150, yPos - 2),
                Size = new Size(70, buttonHeight)
            };
            multiRightResetBtn.Click += (s, e) => { multiModeRightBorderColorButton.BackColor = Color.FromArgb(0, 100, 0); };
            colorsGroupBox.Controls.Add(multiRightResetBtn);

            yPos += spacing;

            // Switching Mode Color
            var switchingLabel = new Label
            {
                Text = "Switching Mode:",
                Location = new Point(10, yPos),
                Size = new Size(labelWidth, 20)
            };
            colorsGroupBox.Controls.Add(switchingLabel);

            switchingModeColorButton = new Button
            {
                Text = "",
                Location = new Point(labelWidth + 20, yPos - 2),
                Size = new Size(40, buttonHeight),
                BackColor = Color.FromArgb(245, 75, 80), // Default RGB color
                FlatStyle = FlatStyle.Flat,
                UseVisualStyleBackColor = false
            };
            switchingModeColorButton.FlatAppearance.BorderSize = 1;
            switchingModeColorButton.FlatAppearance.BorderColor = Color.Gray;
            colorsGroupBox.Controls.Add(switchingModeColorButton);

            var switchingChangeBtn = new Button
            {
                Text = "Change",
                Location = new Point(labelWidth + 70, yPos - 2),
                Size = new Size(70, buttonHeight)
            };
            switchingChangeBtn.Click += (s, e) => ShowColorDialog(switchingModeColorButton, Color.FromArgb(245, 75, 80));
            colorsGroupBox.Controls.Add(switchingChangeBtn);

            var switchingResetBtn = new Button
            {
                Text = "Reset",
                Location = new Point(labelWidth + 150, yPos - 2),
                Size = new Size(70, buttonHeight)
            };
            switchingResetBtn.Click += (s, e) => { switchingModeColorButton.BackColor = Color.FromArgb(245, 75, 80); };
            colorsGroupBox.Controls.Add(switchingResetBtn);

            yPos += spacing;

            // Selected Switch Color
            var selectedLabel = new Label
            {
                Text = "Selected Switch:",
                Location = new Point(10, yPos),
                Size = new Size(labelWidth, 20)
            };
            colorsGroupBox.Controls.Add(selectedLabel);

            switchingSelectedColorButton = new Button
            {
                Text = "",
                Location = new Point(labelWidth + 20, yPos - 2),
                Size = new Size(40, buttonHeight),
                BackColor = Color.FromArgb(244, 194, 140), // Default RGB color
                FlatStyle = FlatStyle.Flat,
                UseVisualStyleBackColor = false
            };
            switchingSelectedColorButton.FlatAppearance.BorderSize = 1;
            switchingSelectedColorButton.FlatAppearance.BorderColor = Color.Gray;
            colorsGroupBox.Controls.Add(switchingSelectedColorButton);

            var selectedChangeBtn = new Button
            {
                Text = "Change",
                Location = new Point(labelWidth + 70, yPos - 2),
                Size = new Size(70, buttonHeight)
            };
            selectedChangeBtn.Click += (s, e) => ShowColorDialog(switchingSelectedColorButton, Color.FromArgb(244, 194, 140));
            colorsGroupBox.Controls.Add(selectedChangeBtn);

            var selectedResetBtn = new Button
            {
                Text = "Reset",
                Location = new Point(labelWidth + 150, yPos - 2),
                Size = new Size(70, buttonHeight)
            };
            selectedResetBtn.Click += (s, e) => { switchingSelectedColorButton.BackColor = Color.FromArgb(244, 194, 140); };
            colorsGroupBox.Controls.Add(selectedResetBtn);

            yPos += spacing;

            // Pending Switch Color
            var pendingLabel = new Label
            {
                Text = "Pending Switch:",
                Location = new Point(10, yPos),
                Size = new Size(labelWidth, 20)
            };
            colorsGroupBox.Controls.Add(pendingLabel);

            switchingSwitchedColorButton = new Button
            {
                Text = "",
                Location = new Point(labelWidth + 20, yPos - 2),
                Size = new Size(40, buttonHeight),
                BackColor = Color.FromArgb(237, 152, 58), // Default RGB color
                FlatStyle = FlatStyle.Flat,
                UseVisualStyleBackColor = false
            };
            switchingSwitchedColorButton.FlatAppearance.BorderSize = 1;
            switchingSwitchedColorButton.FlatAppearance.BorderColor = Color.Gray;
            colorsGroupBox.Controls.Add(switchingSwitchedColorButton);

            var pendingChangeBtn = new Button
            {
                Text = "Change",
                Location = new Point(labelWidth + 70, yPos - 2),
                Size = new Size(70, buttonHeight)
            };
            pendingChangeBtn.Click += (s, e) => ShowColorDialog(switchingSwitchedColorButton, Color.FromArgb(237, 152, 58));
            colorsGroupBox.Controls.Add(pendingChangeBtn);

            var pendingResetBtn = new Button
            {
                Text = "Reset",
                Location = new Point(labelWidth + 150, yPos - 2),
                Size = new Size(70, buttonHeight)
            };
            pendingResetBtn.Click += (s, e) => { switchingSwitchedColorButton.BackColor = Color.FromArgb(237, 152, 58); };
            colorsGroupBox.Controls.Add(pendingResetBtn);

            yPos += spacing;

            // Removed Color
            var removedLabel = new Label
            {
                Text = "Removed:",
                Location = new Point(10, yPos),
                Size = new Size(labelWidth, 20)
            };
            colorsGroupBox.Controls.Add(removedLabel);

            switchingRemovedColorButton = new Button
            {
                Text = "",
                Location = new Point(labelWidth + 20, yPos - 2),
                Size = new Size(40, buttonHeight),
                BackColor = Color.FromArgb(105, 105, 105), // Default RGB color
                FlatStyle = FlatStyle.Flat,
                UseVisualStyleBackColor = false
            };
            switchingRemovedColorButton.FlatAppearance.BorderSize = 1;
            switchingRemovedColorButton.FlatAppearance.BorderColor = Color.Gray;
            colorsGroupBox.Controls.Add(switchingRemovedColorButton);

            var removedChangeBtn = new Button
            {
                Text = "Change",
                Location = new Point(labelWidth + 70, yPos - 2),
                Size = new Size(70, buttonHeight)
            };
            removedChangeBtn.Click += (s, e) => ShowColorDialog(switchingRemovedColorButton, Color.FromArgb(105, 105, 105));
            colorsGroupBox.Controls.Add(removedChangeBtn);

            var removedResetBtn = new Button
            {
                Text = "Reset",
                Location = new Point(labelWidth + 150, yPos - 2),
                Size = new Size(70, buttonHeight)
            };
            removedResetBtn.Click += (s, e) => { switchingRemovedColorButton.BackColor = Color.FromArgb(105, 105, 105); };
            colorsGroupBox.Controls.Add(removedResetBtn);

            yPos += spacing;

            // Focused Mode - Focused Window Color
            var focusedFocusedLabel = new Label
            {
                Text = "Focused Mode (Focused Window):",
                Location = new Point(10, yPos),
                Size = new Size(labelWidth, 20)
            };
            toolTip1.SetToolTip(focusedFocusedLabel, "Focused Mode: one window receives movement keys (WASD/arrows); all windows receive other keys. This color is for the window that has focus.");
            colorsGroupBox.Controls.Add(focusedFocusedLabel);

            focusedModeFocusedColorButton = new Button
            {
                Text = "",
                Location = new Point(labelWidth + 20, yPos - 2),
                Size = new Size(40, buttonHeight),
                BackColor = Color.FromArgb(123, 208, 223), // Default RGB color
                FlatStyle = FlatStyle.Flat,
                UseVisualStyleBackColor = false
            };
            focusedModeFocusedColorButton.FlatAppearance.BorderSize = 1;
            focusedModeFocusedColorButton.FlatAppearance.BorderColor = Color.Gray;
            colorsGroupBox.Controls.Add(focusedModeFocusedColorButton);

            var focusedFocusedChangeBtn = new Button
            {
                Text = "Change",
                Location = new Point(labelWidth + 70, yPos - 2),
                Size = new Size(70, buttonHeight)
            };
            focusedFocusedChangeBtn.Click += (s, e) => ShowColorDialog(focusedModeFocusedColorButton, Color.FromArgb(123, 208, 223));
            colorsGroupBox.Controls.Add(focusedFocusedChangeBtn);

            var focusedFocusedResetBtn = new Button
            {
                Text = "Reset",
                Location = new Point(labelWidth + 150, yPos - 2),
                Size = new Size(70, buttonHeight)
            };
            focusedFocusedResetBtn.Click += (s, e) => { focusedModeFocusedColorButton.BackColor = Color.FromArgb(123, 208, 223); };
            colorsGroupBox.Controls.Add(focusedFocusedResetBtn);

            yPos += spacing;

            // Focused Mode - Unfocused Windows Color
            var focusedUnfocusedLabel = new Label
            {
                Text = "Focused Mode (Unfocused Windows):",
                Location = new Point(10, yPos),
                Size = new Size(labelWidth, 20)
            };
            toolTip1.SetToolTip(focusedUnfocusedLabel, "Focused Mode: one window receives movement keys; all others receive other keys. This color is for the windows that do not have focus.");
            colorsGroupBox.Controls.Add(focusedUnfocusedLabel);

            focusedModeUnfocusedColorButton = new Button
            {
                Text = "",
                Location = new Point(labelWidth + 20, yPos - 2),
                Size = new Size(40, buttonHeight),
                BackColor = Color.FromArgb(95, 134, 207), // Default RGB color
                FlatStyle = FlatStyle.Flat,
                UseVisualStyleBackColor = false
            };
            focusedModeUnfocusedColorButton.FlatAppearance.BorderSize = 1;
            focusedModeUnfocusedColorButton.FlatAppearance.BorderColor = Color.Gray;
            colorsGroupBox.Controls.Add(focusedModeUnfocusedColorButton);

            var focusedUnfocusedChangeBtn = new Button
            {
                Text = "Change",
                Location = new Point(labelWidth + 70, yPos - 2),
                Size = new Size(70, buttonHeight)
            };
            focusedUnfocusedChangeBtn.Click += (s, e) => ShowColorDialog(focusedModeUnfocusedColorButton, Color.FromArgb(95, 134, 207));
            colorsGroupBox.Controls.Add(focusedUnfocusedChangeBtn);

            var focusedUnfocusedResetBtn = new Button
            {
                Text = "Reset",
                Location = new Point(labelWidth + 150, yPos - 2),
                Size = new Size(70, buttonHeight)
            };
            focusedUnfocusedResetBtn.Click += (s, e) => { focusedModeUnfocusedColorButton.BackColor = Color.FromArgb(95, 134, 207); };
            colorsGroupBox.Controls.Add(focusedUnfocusedResetBtn);

            yPos += spacing;
            AddCustomModeBorderColorsSection(colorsGroupBox, ref yPos);
            // The color swatches convey their value only through BackColor and have blank Text; name them so
            // assistive technology can identify each one (UX-06).
            mirrorModeBorderColorButton.AccessibleName = "Mirror mode border color";
            multiModeLeftBorderColorButton.AccessibleName = "Multi-mode left toon border color";
            multiModeRightBorderColorButton.AccessibleName = "Multi-mode right toon border color";
            switchingModeColorButton.AccessibleName = "Switching mode color";
            switchingSelectedColorButton.AccessibleName = "Switching selected color";
            switchingSwitchedColorButton.AccessibleName = "Switching switched color";
            switchingRemovedColorButton.AccessibleName = "Switching marked-for-removal color";
            focusedModeFocusedColorButton.AccessibleName = "Focused mode focused window color";
            focusedModeUnfocusedColorButton.AccessibleName = "Focused mode unfocused window color";

            colorsGroupBox.Size = new Size(720, Math.Max(480, yPos + 20));
            colorsTabPage.Enter += ColorsTabPage_Enter;
        }

        void ColorsTabPage_Enter(object sender, EventArgs e)
        {
            RebuildCustomModeBorderColorRows();
        }

        private void ShowColorDialog(Button colorButton, Color defaultColor)
        {
            using (ColorDialog colorDialog = new ColorDialog())
            {
                colorDialog.Color = colorButton.BackColor;
                colorDialog.FullOpen = true; // Show full color picker including custom colors
                colorDialog.AllowFullOpen = true;
                colorDialog.SolidColorOnly = false;
                
                if (colorDialog.ShowDialog() == DialogResult.OK)
                {
                    colorButton.BackColor = colorDialog.Color;
                }
            }
        }

        private void LoadColorsSettings()
        {
            if (mirrorModeBorderColorButton == null)
                return;

            // Define default RGB colors
            var defaultMirror = Color.FromArgb(238, 130, 238);
            var defaultMultiLeft = Color.FromArgb(50, 205, 50);
            var defaultMultiRight = Color.FromArgb(0, 100, 0);
            var defaultSwitching = Color.FromArgb(245, 75, 80);
            var defaultSelected = Color.FromArgb(244, 194, 140);
            var defaultPending = Color.FromArgb(237, 152, 58);
            var defaultRemoved = Color.FromArgb(105, 105, 105);
            var defaultFocused = Color.FromArgb(123, 208, 223);
            var defaultUnfocused = Color.FromArgb(95, 134, 207);

            // Only load from settings if they differ from defaults (user has customized them)
            // This prevents ARGB conversion issues from overwriting correct RGB-based defaults
            if (Properties.Settings.Default.mirrorModeBorderColor != defaultMirror.ToArgb())
                mirrorModeBorderColorButton.BackColor = Color.FromArgb(Properties.Settings.Default.mirrorModeBorderColor);
            
            if (Properties.Settings.Default.multiModeLeftBorderColor != defaultMultiLeft.ToArgb())
                multiModeLeftBorderColorButton.BackColor = Color.FromArgb(Properties.Settings.Default.multiModeLeftBorderColor);
            
            if (Properties.Settings.Default.multiModeRightBorderColor != defaultMultiRight.ToArgb())
                multiModeRightBorderColorButton.BackColor = Color.FromArgb(Properties.Settings.Default.multiModeRightBorderColor);
            
            if (Properties.Settings.Default.switchingModeColor != defaultSwitching.ToArgb())
                switchingModeColorButton.BackColor = Color.FromArgb(Properties.Settings.Default.switchingModeColor);
            
            if (Properties.Settings.Default.switchingSelectedColor != defaultSelected.ToArgb())
                switchingSelectedColorButton.BackColor = Color.FromArgb(Properties.Settings.Default.switchingSelectedColor);
            
            if (Properties.Settings.Default.switchingSwitchedColor != defaultPending.ToArgb())
                switchingSwitchedColorButton.BackColor = Color.FromArgb(Properties.Settings.Default.switchingSwitchedColor);
            
            if (Properties.Settings.Default.switchingRemovedColor != defaultRemoved.ToArgb())
                switchingRemovedColorButton.BackColor = Color.FromArgb(Properties.Settings.Default.switchingRemovedColor);
            
            if (Properties.Settings.Default.focusedModeFocusedColor != defaultFocused.ToArgb())
                focusedModeFocusedColorButton.BackColor = Color.FromArgb(Properties.Settings.Default.focusedModeFocusedColor);
            
            if (Properties.Settings.Default.focusedModeUnfocusedColor != defaultUnfocused.ToArgb())
                focusedModeUnfocusedColorButton.BackColor = Color.FromArgb(Properties.Settings.Default.focusedModeUnfocusedColor);

            RebuildCustomModeBorderColorRows();
        }

        private void SaveColorsSettings()
        {
            if (mirrorModeBorderColorButton == null)
                return;

            Properties.Settings.Default.mirrorModeBorderColor = mirrorModeBorderColorButton.BackColor.ToArgb();
            Properties.Settings.Default.multiModeLeftBorderColor = multiModeLeftBorderColorButton.BackColor.ToArgb();
            Properties.Settings.Default.multiModeRightBorderColor = multiModeRightBorderColorButton.BackColor.ToArgb();
            Properties.Settings.Default.switchingModeColor = switchingModeColorButton.BackColor.ToArgb();
            Properties.Settings.Default.switchingSelectedColor = switchingSelectedColorButton.BackColor.ToArgb();
            Properties.Settings.Default.switchingSwitchedColor = switchingSwitchedColorButton.BackColor.ToArgb();
            Properties.Settings.Default.switchingRemovedColor = switchingRemovedColorButton.BackColor.ToArgb();
            Properties.Settings.Default.focusedModeFocusedColor = focusedModeFocusedColorButton.BackColor.ToArgb();
            Properties.Settings.Default.focusedModeUnfocusedColor = focusedModeUnfocusedColorButton.BackColor.ToArgb();

            PushCustomModeBorderColorsFromUiToModel();
        }


        private void CreateCaptionColorUI()
        {
            // Get the Other tab (tabPage2)
            var otherTab = tabControl1.TabPages.Cast<TabPage>().FirstOrDefault(t => t.Name == "tabPage2");
            if (otherTab == null)
                return;

            // Create a new group box for caption color setting
            var captionColorGroupBox = new GroupBox
            {
                Text = "Title Bar Color",
                Dock = DockStyle.Top, // Dock to top so it positions right after Keep-Alive group box
                Size = new Size(734, 48)
            };

            // Create checkbox
            enableCaptionColorCheckBox = new CheckBox
            {
                Text = "Match title bar color to border color",
                Location = new Point(10, 20),
                Size = new Size(300, 20),
                Checked = true
            };
            captionColorGroupBox.Controls.Add(enableCaptionColorCheckBox);

            // Add to Other tab
            // When controls are docked to Top, they stack from top to bottom in the order they appear in the collection
            // The Designer adds: groupBox7 (Keep-Alive) -> groupBox6 (Compact) -> groupBox5 (Keep On Top)
            // So visual order is: Keep-Alive (top) -> Compact -> Keep On Top (bottom)
            // We want Title Bar Color to appear AFTER Keep-Alive, so it should be at index 0 (before groupBox7)
            // But actually, we want it at the bottom, so it should be added first (index 0) or last
            // Let's find groupBox7 and place Title Bar Color right after it in the collection
            otherTab.Controls.Add(captionColorGroupBox);
            
            // Find groupBox7 (Keep-Alive) and place Title Bar Color right after it
            // Since docked controls stack in collection order, placing it after groupBox7 means it appears below it
            var groupBox7 = otherTab.Controls.OfType<GroupBox>().FirstOrDefault(gb => gb.Text == "Keep-Alive");
            if (groupBox7 != null)
            {
                int groupBox7Index = otherTab.Controls.GetChildIndex(groupBox7);
                // Place it right after groupBox7 (so it appears below Keep-Alive)
                otherTab.Controls.SetChildIndex(captionColorGroupBox, groupBox7Index + 1);
            }
            else
            {
                // Fallback: place at index 0 (bottom of stack)
                otherTab.Controls.SetChildIndex(captionColorGroupBox, 0);
            }
        }

        private void CreateMinimizeUnconnectedUI()
        {
            var otherTab = tabControl1.TabPages.Cast<TabPage>().FirstOrDefault(t => t.Name == "tabPage2");
            if (otherTab == null)
                return;

            minimizeUnconnectedGroupBox = new GroupBox
            {
                Text = "Minimize Unconnected Toontown Windows",
                Dock = DockStyle.Top,
                Size = new Size(734, 80)
            };

            var descLabel = new Label
            {
                Text = "Minimize all game windows not connected to the multicontroller, or restore them. Uses the same executable list as Auto-Find.",
                Location = new Point(10, 20),
                Size = new Size(710, 32),
                AutoSize = false,
                Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right
            };
            minimizeUnconnectedGroupBox.Controls.Add(descLabel);

            var hotkeyLabel = new Label { Text = "Hotkey:", Location = new Point(10, 52), Size = new Size(45, 20) };
            minimizeUnconnectedGroupBox.Controls.Add(hotkeyLabel);

            minimizeUnconnectedKeyPicker = new KeyPicker
            {
                Location = new Point(60, 50),
                Size = new Size(120, 23)
            };
            minimizeUnconnectedGroupBox.Controls.Add(minimizeUnconnectedKeyPicker);

            minimizeUnconnectedAltCheckBox = new CheckBox { Text = "Alt", Location = new Point(190, 52), Size = new Size(45, 20) };
            minimizeUnconnectedGroupBox.Controls.Add(minimizeUnconnectedAltCheckBox);
            minimizeUnconnectedCtrlCheckBox = new CheckBox { Text = "Ctrl", Location = new Point(240, 52), Size = new Size(50, 20) };
            minimizeUnconnectedGroupBox.Controls.Add(minimizeUnconnectedCtrlCheckBox);
            minimizeUnconnectedShiftCheckBox = new CheckBox { Text = "Shift", Location = new Point(295, 52), Size = new Size(55, 20) };
            minimizeUnconnectedGroupBox.Controls.Add(minimizeUnconnectedShiftCheckBox);

            minimizeUnconnectedHotkeyGlobalCheckBox = new CheckBox
            {
                Text = "Global",
                Location = new Point(360, 52),
                Size = new Size(280, 20)
            };
            minimizeUnconnectedGroupBox.Controls.Add(minimizeUnconnectedHotkeyGlobalCheckBox);

            otherTab.Controls.Add(minimizeUnconnectedGroupBox);
            var captionColorGroupBox = otherTab.Controls.OfType<GroupBox>().FirstOrDefault(gb => gb.Text == "Title Bar Color");
            if (captionColorGroupBox != null)
            {
                int idx = otherTab.Controls.GetChildIndex(captionColorGroupBox);
                otherTab.Controls.SetChildIndex(minimizeUnconnectedGroupBox, idx + 1);
            }
        }

        private void LoadCaptionColorSettings()
        {
            if (enableCaptionColorCheckBox == null)
                return;

            enableCaptionColorCheckBox.Checked = Properties.Settings.Default.enableCaptionColor;
        }

        private void SaveCaptionColorSettings()
        {
            if (enableCaptionColorCheckBox == null)
                return;

            Properties.Settings.Default.enableCaptionColor = enableCaptionColorCheckBox.Checked;
        }

        private void LoadMinimizeUnconnectedSettings()
        {
            if (minimizeUnconnectedKeyPicker == null)
                return;
            minimizeUnconnectedKeyPicker.ChosenKey = (Keys)Properties.Settings.Default.minimizeUnconnectedKeyCode;
            minimizeUnconnectedAltCheckBox.Checked = ((Win32.KeyModifiers)Properties.Settings.Default.minimizeUnconnectedKeyModifiers & Win32.KeyModifiers.Alt) != 0;
            minimizeUnconnectedCtrlCheckBox.Checked = ((Win32.KeyModifiers)Properties.Settings.Default.minimizeUnconnectedKeyModifiers & Win32.KeyModifiers.Control) != 0;
            minimizeUnconnectedShiftCheckBox.Checked = ((Win32.KeyModifiers)Properties.Settings.Default.minimizeUnconnectedKeyModifiers & Win32.KeyModifiers.Shift) != 0;
            minimizeUnconnectedHotkeyGlobalCheckBox.Checked = Properties.Settings.Default.minimizeUnconnectedHotkeyGlobal;
        }

        private void SaveMinimizeUnconnectedSettings()
        {
            if (minimizeUnconnectedKeyPicker == null)
                return;
            Properties.Settings.Default.minimizeUnconnectedKeyCode = (int)minimizeUnconnectedKeyPicker.ChosenKey;
            Win32.KeyModifiers modifiers = Win32.KeyModifiers.None;
            if (minimizeUnconnectedAltCheckBox.Checked) modifiers |= Win32.KeyModifiers.Alt;
            if (minimizeUnconnectedCtrlCheckBox.Checked) modifiers |= Win32.KeyModifiers.Control;
            if (minimizeUnconnectedShiftCheckBox.Checked) modifiers |= Win32.KeyModifiers.Shift;
            Properties.Settings.Default.minimizeUnconnectedKeyModifiers = (int)modifiers;
            Properties.Settings.Default.minimizeUnconnectedHotkeyGlobal = minimizeUnconnectedHotkeyGlobalCheckBox.Checked;
        }

        private void CreateModeLockUI()
        {
            var otherTab = tabControl1.TabPages.Cast<TabPage>().FirstOrDefault(t => t.Name == "tabPage2");
            if (otherTab == null)
                return;

            modeLockGroupBox = new GroupBox
            {
                Text = "Mode Lock",
                Dock = DockStyle.Top,
                Size = new Size(734, 105)
            };

            // Same pattern as minimize-unconnected: constrained width + AutoSize so text wraps inside the group box.
            var desc = new Label
            {
                Text = "While Mode Lock is ON, hotkeys cannot change Multi vs Mirror vs All-group mode or the active group number. " +
                       "Press the toggle key again to unlock.",
                Location = new Point(10, 20),
                Size = new Size(710, 32),
                AutoSize = false,
                Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right
            };
            modeLockGroupBox.Controls.Add(desc);

            int keyRowTop = desc.Bottom + 10;
            var keyLabel = new Label { Text = "Hotkey:", Location = new Point(10, keyRowTop), Size = new Size(45, 20) };
            modeLockGroupBox.Controls.Add(keyLabel);

            modeLockToggleKeyPicker = new KeyPicker
            {
                Location = new Point(60, keyRowTop - 2),
                Size = new Size(120, 23)
            };
            modeLockGroupBox.Controls.Add(modeLockToggleKeyPicker);

            modeLockGroupBox.Height = modeLockToggleKeyPicker.Bottom + 16;

            otherTab.Controls.Add(modeLockGroupBox);
            if (minimizeUnconnectedGroupBox != null)
            {
                int idx = otherTab.Controls.GetChildIndex(minimizeUnconnectedGroupBox);
                otherTab.Controls.SetChildIndex(modeLockGroupBox, idx);
            }
        }

        private void LoadModeLockSettings()
        {
            if (modeLockToggleKeyPicker == null)
                return;
            modeLockToggleKeyPicker.ChosenKey = (Keys)Properties.Settings.Default.modeLockToggleKeyCode;
        }

        private void SaveModeLockSettings()
        {
            if (modeLockToggleKeyPicker == null)
                return;
            Properties.Settings.Default.modeLockToggleKeyCode = (int)modeLockToggleKeyPicker.ChosenKey;
        }

        private void CreateSuspendGlobalHotkeysUI()
        {
            var otherTab = tabControl1.TabPages.Cast<TabPage>().FirstOrDefault(t => t.Name == "tabPage2");
            if (otherTab == null)
                return;

            suspendGlobalHotkeysGroupBox = new GroupBox
            {
                Text = "Suspend global hotkeys",
                Dock = DockStyle.Top,
                Size = new Size(734, 130)
            };

            var desc = new Label
            {
                Text = "Press this key to temporarily turn off Global hotkeys. Press again to restore. " +
                       "It is recommended to choose a key you do not need in chat (e.g. Pause or Scroll Lock), or a modifier chord." +
                       "This key must not conflict with any of your Global hotkeys.",
                Location = new Point(10, 20),
                Size = new Size(710, 48),
                AutoSize = false,
                Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right
            };
            suspendGlobalHotkeysGroupBox.Controls.Add(desc);

            int keyRowTop = desc.Bottom + 2;
            var keyLabel = new Label { Text = "Hotkey:", Location = new Point(10, keyRowTop), Size = new Size(45, 20) };
            suspendGlobalHotkeysGroupBox.Controls.Add(keyLabel);

            suspendGlobalHotkeysToggleKeyPicker = new KeyPicker
            {
                Location = new Point(60, keyRowTop - 2),
                Size = new Size(120, 23)
            };
            suspendGlobalHotkeysGroupBox.Controls.Add(suspendGlobalHotkeysToggleKeyPicker);

            suspendGlobalHotkeysGroupBox.Height = suspendGlobalHotkeysToggleKeyPicker.Bottom + 16;

            otherTab.Controls.Add(suspendGlobalHotkeysGroupBox);
            if (modeLockGroupBox != null)
            {
                int idx = otherTab.Controls.GetChildIndex(modeLockGroupBox);
                otherTab.Controls.SetChildIndex(suspendGlobalHotkeysGroupBox, idx);
            }
        }

        private void LoadSuspendGlobalHotkeysSettings()
        {
            if (suspendGlobalHotkeysToggleKeyPicker == null)
                return;
            suspendGlobalHotkeysToggleKeyPicker.ChosenKey = (Keys)Properties.Settings.Default.suspendGlobalHotkeysToggleKeyCode;
        }

        private void SaveSuspendGlobalHotkeysSettings()
        {
            if (suspendGlobalHotkeysToggleKeyPicker == null)
                return;
            Properties.Settings.Default.suspendGlobalHotkeysToggleKeyCode = (int)suspendGlobalHotkeysToggleKeyPicker.ChosenKey;
        }

        private void CreateControlledMulticlickTab()
        {
            var tab = new TabPage("Multi-Click");
            tab.Name = "multiClickTab";
            tab.AutoScroll = true;
            tab.Padding = new Padding(3);
            tab.UseVisualStyleBackColor = true;
            tabControl1.TabPages.Add(tab);

            var gb = new GroupBox
            {
                Text = "Controlled Multi-Click Mode",
                Dock = DockStyle.Top,
                Height = 385
            };
            tab.Controls.Add(gb);

            var descLabel = new Label
            {
                Text = "Toggle into a mode where all windows display fake cursors showing your cursor position. " +
                       "Use the binds below to click one window or all windows at once.",
                Location = new Point(10, 22),
                Size = new Size(450, 36),
                AutoSize = false
            };
            gb.Controls.Add(descLabel);

            controlledMcEnabledCheckBox = new CheckBox
            {
                Text = "Enable Controlled Multi-Click Mode",
                Location = new Point(10, 65),
                Size = new Size(300, 21)
            };
            gb.Controls.Add(controlledMcEnabledCheckBox);

            // ── Activation Key ──────────────────────────────────────────────────────
            var activateTitleLabel = new Label
            {
                Text = "Activation Key:",
                Location = new Point(10, 98),
                Size = new Size(300, 17),
                Font = new Font(gb.Font, FontStyle.Bold)
            };
            gb.Controls.Add(activateTitleLabel);

            var activateDescLabel = new Label
            {
                Text = "Press this key to enter or exit Controlled Multi-Click Mode.",
                Location = new Point(10, 117),
                Size = new Size(500, 17)
            };
            gb.Controls.Add(activateDescLabel);

            // Row 1: Key [picker]  ○ Toggle  ● Hold
            var activateKeyLabel = new Label { Text = "Key:", Location = new Point(10, 142), Size = new Size(30, 20) };
            gb.Controls.Add(activateKeyLabel);

            controlledMcActivateKeyPicker = new Controls.KeyPicker
            {
                Location = new Point(45, 140),
                Size = new Size(120, 23)
            };
            gb.Controls.Add(controlledMcActivateKeyPicker);

            controlledMcToggleRadio = new RadioButton
            {
                Text = "Toggle",
                Location = new Point(180, 141),
                Size = new Size(70, 21),
                Checked = true
            };
            gb.Controls.Add(controlledMcToggleRadio);

            controlledMcHoldRadio = new RadioButton
            {
                Text = "Hold",
                Location = new Point(260, 141),
                Size = new Size(60, 21)
            };
            gb.Controls.Add(controlledMcHoldRadio);

            // Row 2: Global checkbox on its own line so it never clips
            controlledMcActivateGlobalCheckBox = new CheckBox
            {
                Text = "Global",
                Location = new Point(10, 167),
                Size = new Size(400, 21)
            };
            gb.Controls.Add(controlledMcActivateGlobalCheckBox);

            // ── Regular Click ───────────────────────────────────────────────────────
            var regularClickTitleLabel = new Label
            {
                Text = "Regular Click:",
                Location = new Point(10, 203),
                Size = new Size(300, 17),
                Font = new Font(gb.Font, FontStyle.Bold)
            };
            gb.Controls.Add(regularClickTitleLabel);

            var regularClickDescLabel = new Label
            {
                Text = "Sends a left-click to the game window currently under your cursor.",
                Location = new Point(10, 222),
                Size = new Size(600, 17)
            };
            gb.Controls.Add(regularClickDescLabel);

            controlledMcRegularClickUseMouseCheckBox = new CheckBox
            {
                Text = "Use mouse button",
                Location = new Point(10, 245),
                Size = new Size(140, 21)
            };
            controlledMcRegularClickUseMouseCheckBox.CheckedChanged += (s, e) => UpdateControlledMcRegularClickVisibility();
            gb.Controls.Add(controlledMcRegularClickUseMouseCheckBox);

            controlledMcRegularClickMouseCombo = new ComboBox
            {
                Location = new Point(155, 243),
                Size = new Size(155, 24),
                DropDownStyle = ComboBoxStyle.DropDownList,
                Visible = false
            };
            controlledMcRegularClickMouseCombo.Items.AddRange(new object[] { "Left click", "Right click", "Middle (wheel click)", "Mouse 4", "Mouse 5" });
            gb.Controls.Add(controlledMcRegularClickMouseCombo);

            var regularKeyLabel = new Label { Text = "Key:", Location = new Point(155, 245), Size = new Size(30, 20) };
            gb.Controls.Add(regularKeyLabel);

            controlledMcRegularClickKeyPicker = new Controls.KeyPicker
            {
                Location = new Point(190, 243),
                Size = new Size(120, 23)
            };
            gb.Controls.Add(controlledMcRegularClickKeyPicker);

            controlledMcRegularClickTriggerOnReleaseCheckBox = new CheckBox
            {
                Text = "Trigger on release",
                Location = new Point(325, 245),
                Size = new Size(140, 21)
            };
            gb.Controls.Add(controlledMcRegularClickTriggerOnReleaseCheckBox);

            // ── Multi-Click ─────────────────────────────────────────────────────────
            var clickTitleLabel = new Label
            {
                Text = "Multi-Click:",
                Location = new Point(10, 283),
                Size = new Size(300, 17),
                Font = new Font(gb.Font, FontStyle.Bold)
            };
            gb.Controls.Add(clickTitleLabel);

            var clickDescLabel = new Label
            {
                Text = "Sends a left-click to all game windows at once.",
                Location = new Point(10, 302),
                Size = new Size(600, 17)
            };
            gb.Controls.Add(clickDescLabel);

            controlledMcClickUseMouseCheckBox = new CheckBox
            {
                Text = "Use mouse button",
                Location = new Point(10, 325),
                Size = new Size(140, 21)
            };
            controlledMcClickUseMouseCheckBox.CheckedChanged += (s, e) => UpdateControlledMcClickVisibility();
            gb.Controls.Add(controlledMcClickUseMouseCheckBox);

            controlledMcClickMouseCombo = new ComboBox
            {
                Location = new Point(155, 323),
                Size = new Size(155, 24),
                DropDownStyle = ComboBoxStyle.DropDownList,
                Visible = false
            };
            controlledMcClickMouseCombo.Items.AddRange(new object[] { "Left click", "Right click", "Middle (wheel click)", "Mouse 4", "Mouse 5" });
            gb.Controls.Add(controlledMcClickMouseCombo);

            var clickKeyLabel = new Label { Text = "Key:", Location = new Point(155, 325), Size = new Size(30, 20) };
            gb.Controls.Add(clickKeyLabel);

            controlledMcClickKeyPicker = new Controls.KeyPicker
            {
                Location = new Point(190, 323),
                Size = new Size(120, 23)
            };
            gb.Controls.Add(controlledMcClickKeyPicker);

            controlledMcClickTriggerOnReleaseCheckBox = new CheckBox
            {
                Text = "Trigger on release",
                Location = new Point(325, 325),
                Size = new Size(140, 21)
            };
            gb.Controls.Add(controlledMcClickTriggerOnReleaseCheckBox);

            controlledMcClickSeparateLRCheckBox = new CheckBox
            {
                Text = "Same side only (L or R)",
                Location = new Point(10, 351),
                Size = new Size(185, 21)
            };
            toolTip1.SetToolTip(controlledMcClickSeparateLRCheckBox,
                "When enabled: if the cursor is on a Left controller, only Left controllers receive the click (and vice versa for Right).");
            gb.Controls.Add(controlledMcClickSeparateLRCheckBox);
        }

        private void UpdateControlledMcClickVisibility()
        {
            if (controlledMcClickUseMouseCheckBox == null) return;
            bool useMouse = controlledMcClickUseMouseCheckBox.Checked;
            controlledMcClickMouseCombo.Visible = useMouse;
            controlledMcClickKeyPicker.Visible = !useMouse;
        }

        private void UpdateControlledMcRegularClickVisibility()
        {
            if (controlledMcRegularClickUseMouseCheckBox == null) return;
            bool useMouse = controlledMcRegularClickUseMouseCheckBox.Checked;
            controlledMcRegularClickMouseCombo.Visible = useMouse;
            controlledMcRegularClickKeyPicker.Visible = !useMouse;
        }

        private void LoadControlledMulticlickSettings()
        {
            if (controlledMcEnabledCheckBox == null)
                return;

            controlledMcEnabledCheckBox.Checked = Properties.Settings.Default.controlledMulticlickEnabled;
            controlledMcActivateKeyPicker.ChosenKey = (Keys)Properties.Settings.Default.controlledMulticlickActivateKeyCode;

            bool hold = Properties.Settings.Default.controlledMulticlickActivateHold;
            controlledMcHoldRadio.Checked = hold;
            controlledMcToggleRadio.Checked = !hold;

            controlledMcActivateGlobalCheckBox.Checked = Properties.Settings.Default.controlledMulticlickActivateGlobal;

            // Regular click
            bool regularUseMouse = Properties.Settings.Default.controlledMulticlickRegularClickUseMouseButton;
            controlledMcRegularClickUseMouseCheckBox.Checked = regularUseMouse;
            controlledMcRegularClickMouseCombo.SelectedIndex = Math.Max(0, Math.Min(4, Properties.Settings.Default.controlledMulticlickRegularClickMouseButton));
            controlledMcRegularClickKeyPicker.ChosenKey = (Keys)Properties.Settings.Default.controlledMulticlickRegularClickKeyCode;
            controlledMcRegularClickTriggerOnReleaseCheckBox.Checked = Properties.Settings.Default.controlledMulticlickRegularClickTriggerOnRelease;
            UpdateControlledMcRegularClickVisibility();

            // Multi-click
            bool clickUseMouse = Properties.Settings.Default.controlledMulticlickClickUseMouseButton;
            controlledMcClickUseMouseCheckBox.Checked = clickUseMouse;
            controlledMcClickMouseCombo.SelectedIndex = Math.Max(0, Math.Min(4, Properties.Settings.Default.controlledMulticlickClickMouseButton));
            controlledMcClickKeyPicker.ChosenKey = (Keys)Properties.Settings.Default.controlledMulticlickClickKeyCode;
            controlledMcClickTriggerOnReleaseCheckBox.Checked = Properties.Settings.Default.controlledMulticlickClickTriggerOnRelease;
            controlledMcClickSeparateLRCheckBox.Checked = Properties.Settings.Default.controlledMulticlickClickSeparateLR;
            UpdateControlledMcClickVisibility();
        }

        private void SaveControlledMulticlickSettings()
        {
            if (controlledMcEnabledCheckBox == null)
                return;

            Properties.Settings.Default.controlledMulticlickEnabled = controlledMcEnabledCheckBox.Checked;
            Properties.Settings.Default.controlledMulticlickActivateKeyCode = (int)controlledMcActivateKeyPicker.ChosenKey;
            Properties.Settings.Default.controlledMulticlickActivateHold = controlledMcHoldRadio.Checked;
            Properties.Settings.Default.controlledMulticlickActivateGlobal = controlledMcActivateGlobalCheckBox.Checked;

            // Regular click
            Properties.Settings.Default.controlledMulticlickRegularClickUseMouseButton = controlledMcRegularClickUseMouseCheckBox.Checked;
            Properties.Settings.Default.controlledMulticlickRegularClickMouseButton = controlledMcRegularClickMouseCombo.SelectedIndex >= 0 ? controlledMcRegularClickMouseCombo.SelectedIndex : 0;
            Properties.Settings.Default.controlledMulticlickRegularClickKeyCode = (int)controlledMcRegularClickKeyPicker.ChosenKey;
            Properties.Settings.Default.controlledMulticlickRegularClickTriggerOnRelease = controlledMcRegularClickTriggerOnReleaseCheckBox.Checked;

            // Multi-click
            Properties.Settings.Default.controlledMulticlickClickUseMouseButton = controlledMcClickUseMouseCheckBox.Checked;
            Properties.Settings.Default.controlledMulticlickClickMouseButton = controlledMcClickMouseCombo.SelectedIndex >= 0 ? controlledMcClickMouseCombo.SelectedIndex : 0;
            Properties.Settings.Default.controlledMulticlickClickKeyCode = (int)controlledMcClickKeyPicker.ChosenKey;
            Properties.Settings.Default.controlledMulticlickClickTriggerOnRelease = controlledMcClickTriggerOnReleaseCheckBox.Checked;
            Properties.Settings.Default.controlledMulticlickClickSeparateLR = controlledMcClickSeparateLRCheckBox.Checked;
        }

        private void LoadMulticlickMouseSettings()
        {
            if (multiclickUseMouseCheckBox == null)
                return;
            multiclickUseMouseCheckBox.Checked = Properties.Settings.Default.replicateMouseUseMouseButton;
            int btn = Math.Max(0, Math.Min(2, Properties.Settings.Default.replicateMouseMouseButton));
            multiclickMouseButtonCombo.SelectedIndex = btn;
            if (multiclickOrderCombo != null)
            {
                multiclickOrderCombo.SelectedIndex = Math.Max(0, Math.Min(1, Properties.Settings.Default.multiclickOrder));
            }
            multiclickMouseButtonCombo.SelectedIndexChanged += (s, ev) =>
            {
                if (multiclickMouseButtonCombo.SelectedIndex >= 0)
                    Properties.Settings.Default.replicateMouseMouseButton = multiclickMouseButtonCombo.SelectedIndex;
            };
            multiclickUseMouseCheckBox_CheckedChanged(null, null);
        }

        private void multiclickUseMouseCheckBox_CheckedChanged(object sender, EventArgs e)
        {
            if (multiclickUseMouseCheckBox == null)
                return;
            bool useMouse = multiclickUseMouseCheckBox.Checked;
            multiclickKeyPicker.Visible = !useMouse;
            multiclickMouseButtonCombo.Visible = useMouse;
        }

        private void multiclickOrderCombo_SelectedIndexChanged(object sender, EventArgs e)
        {
            if (multiclickOrderCombo != null && multiclickOrderCombo.SelectedIndex >= 0)
                Properties.Settings.Default.multiclickOrder = multiclickOrderCombo.SelectedIndex;
        }

        private void LoadAutoFindSettings()
        {
            if (autoFindExecutablesTextBox == null)
                return;

            autoFindExecutablesTextBox.Text = Properties.Settings.Default.autoFindExecutables;
            autoFindKeyPicker.ChosenKey = (Keys)Properties.Settings.Default.autoFindWindowsKeyCode;
            autoFindAltCheckBox.Checked = ((Win32.KeyModifiers)Properties.Settings.Default.autoFindWindowsKeyModifiers & Win32.KeyModifiers.Alt) != 0;
            autoFindCtrlCheckBox.Checked = ((Win32.KeyModifiers)Properties.Settings.Default.autoFindWindowsKeyModifiers & Win32.KeyModifiers.Control) != 0;
            autoFindShiftCheckBox.Checked = ((Win32.KeyModifiers)Properties.Settings.Default.autoFindWindowsKeyModifiers & Win32.KeyModifiers.Shift) != 0;
            if (autoFindPlacementOnAltReleaseCheckBox != null)
                autoFindPlacementOnAltReleaseCheckBox.Checked = Properties.Settings.Default.autoFindPlacementOnAltRelease;
        }

        private void SaveAutoFindSettings()
        {
            if (autoFindExecutablesTextBox == null)
                return;

            Properties.Settings.Default.autoFindExecutables = autoFindExecutablesTextBox.Text;
            Properties.Settings.Default.autoFindWindowsKeyCode = (int)autoFindKeyPicker.ChosenKey;

            Win32.KeyModifiers modifiers = Win32.KeyModifiers.None;
            if (autoFindAltCheckBox.Checked)
                modifiers |= Win32.KeyModifiers.Alt;
            if (autoFindCtrlCheckBox.Checked)
                modifiers |= Win32.KeyModifiers.Control;
            if (autoFindShiftCheckBox.Checked)
                modifiers |= Win32.KeyModifiers.Shift;

            Properties.Settings.Default.autoFindWindowsKeyModifiers = (int)modifiers;
            if (autoFindPlacementOnAltReleaseCheckBox != null)
                Properties.Settings.Default.autoFindPlacementOnAltRelease = autoFindPlacementOnAltReleaseCheckBox.Checked;
        }

        private void okBtn_Click(object sender, EventArgs e)
        {
            Properties.SerializedSettings.Default.Bindings = controlsPicker.KeyMappings;

            SaveAutoFindSettings();

            SaveLayoutPresets();
            
            // Save switching mode settings
            SaveSwitchingModeSettings();
            
            // Save colors settings
            SaveColorsSettings();
            
            // Save caption color settings
            SaveCaptionColorSettings();

            // Save minimize unconnected settings
            SaveMinimizeUnconnectedSettings();

            SaveModeLockSettings();

            SaveSuspendGlobalHotkeysSettings();

            SaveCustomModesSettings();

            // Save controlled multi-click settings
            SaveControlledMulticlickSettings();

            Properties.Settings.Default.Save();
            DialogResult = DialogResult.OK;
            this.Close();
        }

        private void cancelBtn_Click(object sender, EventArgs e)
        {
            // The in-memory revert happens in OnFormClosing (which also covers the title-bar X / Alt+F4 / Esc).
            DialogResult = DialogResult.Cancel;
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            base.OnFormClosing(e);

            // Most controls are data-bound with DataSourceUpdateMode.OnPropertyChanged, so toggling one mutates
            // Settings.Default immediately.  Only OK persists (it calls Save); every other way of actually closing
            // the dialog — the title-bar X, Alt+F4, Esc, or Cancel — must revert those in-memory edits, otherwise a
            // later Settings.Default.Save() elsewhere would persist changes the user believed they discarded (UX-02).
            if (!e.Cancel && DialogResult != DialogResult.OK)
            {
                Properties.Settings.Default.Reload();
            }
        }

        private void aboutBtn_Click(object sender, EventArgs e)
        {
            new AboutWnd().ShowDialog(this);
        }

        private void checkBox4_CheckedChanged(object sender, EventArgs e)
        {
            // Invert the value: checkbox says "Enable Keep-Alive", so checked = disableKeepAlive = false
            Properties.Settings.Default.disableKeepAlive = !checkBox4.Checked;
        }

        private async void checkUpdateBtn_Click(object sender, EventArgs e)
        {
            await CheckForUpdatesAsync();
        }
        
        private void addBindingBtn_Click(object sender, EventArgs e)
        {
            AddKeyMappingDlg addKeyMappingDlg = new AddKeyMappingDlg();

            while (addKeyMappingDlg.ShowDialog() == DialogResult.OK)
            {
                var keyBindings = controlsPicker.KeyMappings;

                if (string.IsNullOrEmpty(addKeyMappingDlg.BindingName.Trim()))
                {
                    MessageBox.Show("Please enter a name for the binding.");
                }
                /*else if (addKeyMappingDlg.LeftToonKey != Keys.None && keyBindings.Any(t => t.LeftToonKey == addKeyMappingDlg.LeftToonKey))
                {
                    MessageBox.Show("Sorry, the key you picked for the left toon is already being used for another binding on the left toon.");
                }
                else if (addKeyMappingDlg.RightToonKey != Keys.None && keyBindings.Any(t => t.RightToonKey == addKeyMappingDlg.RightToonKey))
                {
                    MessageBox.Show("Sorry, the key you picked for the right toon is already being used for another binding on the right toon.");
                }*/
                else
                {
                    if (addKeyMappingDlg.LeftToonKey >= Keys.D0 && addKeyMappingDlg.LeftToonKey <= Keys.D9
                        || addKeyMappingDlg.LeftToonKey >= Keys.NumPad0 && addKeyMappingDlg.LeftToonKey <= Keys.NumPad9
                        || addKeyMappingDlg.RightToonKey >= Keys.D0 && addKeyMappingDlg.RightToonKey <= Keys.D9
                        || addKeyMappingDlg.RightToonKey >= Keys.NumPad0 && addKeyMappingDlg.RightToonKey <= Keys.NumPad9)
                    {
                        MessageBox.Show("Note: the number keys (0-9) and number pad keys are reserved for switching groups if there is more than 1 group.");
                    }

                    controlsPicker.AddMapping(new KeyMapping(addKeyMappingDlg.BindingName, addKeyMappingDlg.BindingKey, addKeyMappingDlg.LeftToonKey, addKeyMappingDlg.RightToonKey, false));
                    break;
                }
            }
        }

    }
}
