using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Windows.Media;

namespace TTMulti.Ui.ViewModels
{
    /// <summary>
    /// Backs the Window Groups editor. Operates LIVE on <see cref="Multicontroller.Instance"/> (there is no
    /// OK/Cancel transaction — the old WinForms dialog applied changes immediately too). Up to 10 groups; each
    /// group keeps exactly one empty trailing pair (as the old ControllerGroupView did).
    /// </summary>
    internal sealed class GroupsViewModel : INotifyPropertyChanged
    {
        private readonly Multicontroller _controller;

        public ObservableCollection<GroupPanelViewModel> Groups { get; } = new ObservableCollection<GroupPanelViewModel>();

        internal GroupsViewModel(Multicontroller controller)
        {
            _controller = controller;
            for (int i = 0; i < _controller.ControllerGroups.Count; i++)
                Groups.Add(new GroupPanelViewModel(_controller.ControllerGroups[i], i + 1));
            RaiseCan();
        }

        public bool CanAddGroup => Groups.Count < 10;
        public bool CanRemoveGroup => Groups.Count > 1;

        public void AddGroup()
        {
            if (!CanAddGroup)
                return;
            var group = _controller.AddControllerGroup();
            Groups.Add(new GroupPanelViewModel(group, Groups.Count + 1));
            RaiseCan();
        }

        /// <summary>Removes the LAST group (matching the old dialog, which only removed the trailing group).</summary>
        public void RemoveLastGroup()
        {
            if (!CanRemoveGroup)
                return;
            Groups.RemoveAt(Groups.Count - 1);
            _controller.RemoveControllerGroup(_controller.ControllerGroups.Count - 1);
            RaiseCan();
        }

        private void RaiseCan()
        {
            OnPropertyChanged(nameof(CanAddGroup));
            OnPropertyChanged(nameof(CanRemoveGroup));
        }

        public event PropertyChangedEventHandler PropertyChanged;

        private void OnPropertyChanged([CallerMemberName] string name = null) =>
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }

    /// <summary>One group card: its number and its pairs, with the "always one empty trailing pair" upkeep.</summary>
    internal sealed class GroupPanelViewModel
    {
        private readonly ControllerGroup _group;

        public int GroupNumber { get; }
        public ObservableCollection<PairPanelViewModel> Pairs { get; } = new ObservableCollection<PairPanelViewModel>();

        internal GroupPanelViewModel(ControllerGroup group, int number)
        {
            _group = group;
            GroupNumber = number;
            foreach (var pair in _group.ControllerPairs)
                AddPairVm(pair);
            AdjustPairs();
        }

        private void AddPairVm(ControllerPair pair)
        {
            var vm = new PairPanelViewModel(pair);
            vm.Changed += (s, e) => AdjustPairs();
            Pairs.Add(vm);
        }

        /// <summary>Keep exactly one empty pair at the end: trim extra empties, or add one if the last is filled.</summary>
        private void AdjustPairs()
        {
            var trailingEmpty = Pairs.Reverse().TakeWhile(p => !p.HasWindow).ToList();
            if (trailingEmpty.Count > 0)
            {
                for (int i = 0; i < trailingEmpty.Count - 1; i++)
                    RemoveLastPair();
            }
            else
            {
                AddPair();
            }
        }

        private void AddPair()
        {
            if (_group.ControllerPairs.Count < 10)
                AddPairVm(_group.AddPair());
        }

        private void RemoveLastPair()
        {
            if (_group.ControllerPairs.Count > 1)
            {
                _group.RemoveLastPair();
                Pairs.RemoveAt(Pairs.Count - 1);
            }
        }
    }

    /// <summary>One pair: two crosshair-bound window handles for the left/right toon controllers.</summary>
    internal sealed class PairPanelViewModel : INotifyPropertyChanged
    {
        private readonly ControllerPair _pair;
        private IntPtr _left;
        private IntPtr _right;

        /// <summary>Raised after either window handle is assigned, so the group can re-balance its pairs.</summary>
        public event EventHandler Changed;

        internal PairPanelViewModel(ControllerPair pair)
        {
            _pair = pair;
            _left = pair.LeftController.WindowHandle;
            _right = pair.RightController.WindowHandle;
        }

        public IntPtr LeftWindowHandle { get => _left; private set { _left = value; OnPropertyChanged(); } }
        public IntPtr RightWindowHandle { get => _right; private set { _right = value; OnPropertyChanged(); } }

        public bool HasWindow => _pair.LeftController.HasWindow || _pair.RightController.HasWindow;

        public Brush LeftAccent { get; } = ToBrush(Colors.LeftGroup);
        public Brush RightAccent { get; } = ToBrush(Colors.RightGroup);

        public void AssignLeft(IntPtr handle)
        {
            _pair.LeftController.WindowHandle = handle;
            LeftWindowHandle = handle;
            Changed?.Invoke(this, EventArgs.Empty);
        }

        public void AssignRight(IntPtr handle)
        {
            _pair.RightController.WindowHandle = handle;
            RightWindowHandle = handle;
            Changed?.Invoke(this, EventArgs.Empty);
        }

        private static SolidColorBrush ToBrush(System.Drawing.Color c)
        {
            var b = new SolidColorBrush(System.Windows.Media.Color.FromArgb(c.A, c.R, c.G, c.B));
            b.Freeze();
            return b;
        }

        public event PropertyChangedEventHandler PropertyChanged;

        private void OnPropertyChanged([CallerMemberName] string name = null) =>
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}
