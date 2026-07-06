using System;
using System.ComponentModel;
using System.Windows;
using TTMulti.Ui.Controls;
using TTMulti.Ui.ViewModels;
using Wpf.Ui.Controls;

namespace TTMulti.Ui
{
    /// <summary>
    /// WPF replacement for WindowGroupsForm + ControllerGroupView/ControllerPairView. Edits the live
    /// controller groups (no OK/Cancel transaction — the OK button just closes). The main window shows all
    /// borders while this is open.
    /// </summary>
    public partial class GroupsWindow : FluentWindow
    {
        private readonly Multicontroller _controller;
        private readonly GroupsViewModel _viewModel;

        public GroupsWindow()
        {
            InitializeComponent();
            Controls.AppLogo.ApplyAppIcon(this, titleBar);
            _controller = Multicontroller.Instance;
            _viewModel = new GroupsViewModel(_controller);
            DataContext = _viewModel;

            var savedSize = Properties.Settings.Default.lastGroupsFormSize;
            if (savedSize != System.Drawing.Size.Empty)
            {
                Width = savedSize.Width;
                Height = savedSize.Height;
            }
        }

        private void LeftCrosshair_WindowSelected(object sender, IntPtr handle)
        {
            if ((sender as WindowCrosshair)?.DataContext is PairPanelViewModel pair)
                pair.AssignLeft(handle);
        }

        private void RightCrosshair_WindowSelected(object sender, IntPtr handle)
        {
            if ((sender as WindowCrosshair)?.DataContext is PairPanelViewModel pair)
                pair.AssignRight(handle);
        }

        private void AddGroup_Click(object sender, RoutedEventArgs e) => _viewModel.AddGroup();

        private void RemoveGroup_Click(object sender, RoutedEventArgs e)
        {
            if (!_viewModel.CanRemoveGroup)
                return;
            int groupNumber = _viewModel.Groups.Count; // the last group's 1-based number
            if (System.Windows.MessageBox.Show(this,
                    "Remove Group " + groupNumber + "? Its window pairs will be disconnected.",
                    "Remove Group", System.Windows.MessageBoxButton.YesNo, System.Windows.MessageBoxImage.Warning)
                == System.Windows.MessageBoxResult.Yes)
            {
                _viewModel.RemoveLastGroup();
            }
        }

        private void Ok_Click(object sender, RoutedEventArgs e) => Close();

        protected override void OnActivated(EventArgs e)
        {
            base.OnActivated(e);
            _controller.IsActive = true;
        }

        protected override void OnDeactivated(EventArgs e)
        {
            base.OnDeactivated(e);
            _controller.IsActive = false;
        }

        protected override void OnClosing(CancelEventArgs e)
        {
            base.OnClosing(e);
            Properties.Settings.Default.lastGroupsFormSize = new System.Drawing.Size((int)Width, (int)Height);
            Properties.Settings.Default.Save();
        }
    }
}
