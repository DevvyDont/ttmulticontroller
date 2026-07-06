using System.Collections.Generic;
using System.Linq;
using TTMulti;
using TTMulti.Ui.Settings;
using Xunit;
using WinFormsKeys = System.Windows.Forms.Keys;

namespace TTMulti.Tests
{
    /// <summary>
    /// Pins that the redesigned Custom Modes rule editor still maps its two plain-language dropdowns (the merged
    /// "send" choice and the named-toon "target" choice) back to the exact frozen CustomModeBinding fields.
    /// </summary>
    public class CustomModeBindingViewModelTests
    {
        private static CustomModeEditContext Context() => new CustomModeEditContext(
            new[] { "Forward", "Left", CustomModeWellKnownRoles.ZeroPowerThrow },
            new[]
            {
                new CustomModeToonOption(1, "Toon 1 (Group 1, Left)"),
                new CustomModeToonOption(2, "Toon 2 (Group 1, Right)"),
                new CustomModeToonOption(3, "Toon 3 (Group 2, Left)"),
            });

        private static CustomModeBindingViewModel New(CustomModeBinding b) => new CustomModeBindingViewModel(b, Context());

        [Fact]
        public void Role_to_all_maps_through()
        {
            var vm = New(new CustomModeBinding { InputKey = (int)WinFormsKeys.F, Action = CustomModeBindingAction.SendRole, RoleTitle = "Forward", TargetKind = CustomModeTargetKind.All });

            Assert.Equal(SendKind.Role, vm.SelectedSendOption.Kind);
            Assert.Equal("Forward", vm.SelectedSendOption.RoleTitle);
            Assert.False(vm.ShowRawKey);

            var m = vm.ToModel();
            Assert.Equal(CustomModeBindingAction.SendRole, m.Action);
            Assert.Equal("Forward", m.RoleTitle);
            Assert.Equal(CustomModeTargetKind.All, m.TargetKind);
        }

        [Fact]
        public void Switching_to_raw_key_and_a_named_toon_maps_through()
        {
            var vm = New(new CustomModeBinding { InputKey = (int)WinFormsKeys.G, Action = CustomModeBindingAction.SendRole, RoleTitle = "Forward", TargetKind = CustomModeTargetKind.All });

            vm.SelectedSendOption = vm.SendOptions.First(o => o.Kind == SendKind.RawKey);
            vm.RawKeyCode = (int)WinFormsKeys.B;
            vm.SelectedTargetOption = vm.TargetOptions.First(o => o.Kind == CustomModeTargetKind.Single && o.Index == 2);

            Assert.True(vm.ShowRawKey);
            var m = vm.ToModel();
            Assert.Equal(CustomModeBindingAction.SendRawKey, m.Action);
            Assert.Equal((int)WinFormsKeys.B, m.RawKey);
            Assert.Equal(CustomModeTargetKind.Single, m.TargetKind);
            Assert.Equal(2, m.TargetIndex);
        }

        [Fact]
        public void Several_toons_writes_checked_indices_as_listed()
        {
            var vm = New(new CustomModeBinding { InputKey = (int)WinFormsKeys.H, Action = CustomModeBindingAction.SendRole, RoleTitle = "Left", TargetKind = CustomModeTargetKind.All });

            vm.SelectedTargetOption = vm.TargetOptions.First(o => o.Kind == CustomModeTargetKind.Listed);
            Assert.True(vm.ShowSeveral);
            vm.SeveralToons.First(t => t.Index == 1).IsChecked = true;
            vm.SeveralToons.First(t => t.Index == 3).IsChecked = true;

            var m = vm.ToModel();
            Assert.Equal(CustomModeTargetKind.Listed, m.TargetKind);
            Assert.Equal(new List<int> { 1, 3 }, m.ListedTargetIndices);
        }

        [Fact]
        public void Mouse_click_maps_to_instant_click()
        {
            var vm = New(new CustomModeBinding { InputKey = (int)WinFormsKeys.J, Action = CustomModeBindingAction.SendRole, RoleTitle = "Forward", TargetKind = CustomModeTargetKind.All });

            vm.SelectedSendOption = vm.SendOptions.First(o => o.Kind == SendKind.Click);

            Assert.Equal(CustomModeBindingAction.InstantClick, vm.ToModel().Action);
        }

        [Fact]
        public void PassThrough_is_the_inverse_of_ConsumeInput()
        {
            var vm = New(new CustomModeBinding { InputKey = (int)WinFormsKeys.K, Action = CustomModeBindingAction.SendRole, RoleTitle = "Forward", ConsumeInput = true });

            Assert.False(vm.PassThrough);
            vm.PassThrough = true;
            Assert.False(vm.ToModel().ConsumeInput);
        }

        [Fact]
        public void Existing_single_index_selects_its_named_toon_option()
        {
            var vm = New(new CustomModeBinding { InputKey = (int)WinFormsKeys.L, Action = CustomModeBindingAction.SendRole, RoleTitle = "Forward", TargetKind = CustomModeTargetKind.Single, TargetIndex = 3 });

            Assert.Equal(CustomModeTargetKind.Single, vm.SelectedTargetOption.Kind);
            Assert.Equal(3, vm.SelectedTargetOption.Index);
        }

        [Fact]
        public void A_role_no_longer_in_the_key_bindings_is_preserved()
        {
            var vm = New(new CustomModeBinding { InputKey = (int)WinFormsKeys.M, Action = CustomModeBindingAction.SendRole, RoleTitle = "Backward", TargetKind = CustomModeTargetKind.All });

            Assert.Equal("Backward", vm.SelectedSendOption.RoleTitle);
            Assert.Equal("Backward", vm.ToModel().RoleTitle);
        }
    }
}
