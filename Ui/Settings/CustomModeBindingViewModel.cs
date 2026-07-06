using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using WinFormsKeys = System.Windows.Forms.Keys;

namespace TTMulti.Ui.Settings
{
    /// <summary>
    /// Editable view of one <see cref="CustomModeBinding"/>, presented as a plain-language rule:
    /// "press KEY, send WHAT, to WHO". The Action+Role model fields collapse into one "send" dropdown
    /// (<see cref="SelectedSendOption"/>) and the TargetKind+index fields into one "to" dropdown
    /// (<see cref="SelectedTargetOption"/>); modifiers and pass-through live behind the per-row "more" toggle.
    /// <see cref="ToModel"/> still produces exactly the same on-disk <see cref="CustomModeBinding"/> fields.
    /// </summary>
    internal sealed class CustomModeBindingViewModel : INotifyPropertyChanged
    {
        private WinFormsKeys _inputKey;
        private bool _alt, _ctrl, _shift;
        private WinFormsKeys _rawKey;
        private bool _consume;
        private bool _moreOpen;
        private SendOption _send;
        private TargetOption _target;

        public IReadOnlyList<SendOption> SendOptions { get; }
        public IReadOnlyList<TargetOption> TargetOptions { get; }
        public ObservableCollection<ToonCheck> SeveralToons { get; }

        internal CustomModeBindingViewModel(CustomModeBinding b, CustomModeEditContext ctx)
        {
            _inputKey = (WinFormsKeys)b.InputKey;
            _alt = b.RequireAlt;
            _ctrl = b.RequireControl;
            _shift = b.RequireShift;
            _rawKey = (WinFormsKeys)b.RawKey;
            _consume = b.ConsumeInput;

            SendOptions = BuildSendOptions(ctx, b);
            TargetOptions = BuildTargetOptions(ctx, b);
            _send = MatchSend(b);
            _target = MatchTarget(b);

            var listed = b.ListedTargetIndices ?? new List<int>();
            SeveralToons = new ObservableCollection<ToonCheck>();
            foreach (var t in ctx.Toons)
            {
                var tc = new ToonCheck(t.Index, t.Label, listed.Contains(t.Index)) { Changed = RefreshSummary };
                SeveralToons.Add(tc);
            }
        }

        // ── Option list construction ────────────────────────────────────────────────

        private static List<SendOption> BuildSendOptions(CustomModeEditContext ctx, CustomModeBinding b)
        {
            var list = ctx.RoleTitles.Select(t => new SendOption(t, SendKind.Role, t)).ToList();
            // Preserve a role that is no longer in the current key bindings so the selection round-trips.
            if (b.Action == CustomModeBindingAction.SendRole && !string.IsNullOrEmpty(b.RoleTitle)
                && !list.Any(o => o.RoleTitle == b.RoleTitle))
                list.Add(new SendOption(b.RoleTitle, SendKind.Role, b.RoleTitle));
            list.Add(new SendOption(SendOption.RawKeyLabel, SendKind.RawKey));
            list.Add(new SendOption(SendOption.ClickLabel, SendKind.Click));
            return list;
        }

        private static List<TargetOption> BuildTargetOptions(CustomModeEditContext ctx, CustomModeBinding b)
        {
            var list = new List<TargetOption> { new TargetOption(TargetOption.AllLabel, CustomModeTargetKind.All) };
            list.AddRange(ctx.Toons.Select(t => new TargetOption(t.Label, CustomModeTargetKind.Single, t.Index)));
            // Preserve a single-target index beyond the current toon list so the selection round-trips.
            if (b.TargetKind == CustomModeTargetKind.Single && b.TargetIndex >= 1
                && !list.Any(o => o.Kind == CustomModeTargetKind.Single && o.Index == b.TargetIndex))
                list.Add(new TargetOption("Toon " + b.TargetIndex, CustomModeTargetKind.Single, b.TargetIndex));
            list.Add(new TargetOption(TargetOption.SeveralLabel, CustomModeTargetKind.Listed));
            return list;
        }

        private SendOption MatchSend(CustomModeBinding b)
        {
            switch (b.Action)
            {
                case CustomModeBindingAction.SendRawKey: return SendOptions.First(o => o.Kind == SendKind.RawKey);
                case CustomModeBindingAction.InstantClick: return SendOptions.First(o => o.Kind == SendKind.Click);
                default:
                    return SendOptions.FirstOrDefault(o => o.Kind == SendKind.Role && o.RoleTitle == b.RoleTitle)
                        ?? SendOptions.First(o => o.Kind == SendKind.Role);
            }
        }

        private TargetOption MatchTarget(CustomModeBinding b)
        {
            switch (b.TargetKind)
            {
                case CustomModeTargetKind.All: return TargetOptions.First(o => o.Kind == CustomModeTargetKind.All);
                case CustomModeTargetKind.Listed: return TargetOptions.First(o => o.Kind == CustomModeTargetKind.Listed);
                default:
                    return TargetOptions.FirstOrDefault(o => o.Kind == CustomModeTargetKind.Single && o.Index == b.TargetIndex)
                        ?? TargetOptions.First();
            }
        }

        // ── Bound properties ────────────────────────────────────────────────────────

        public int InputKeyCode
        {
            get => (int)_inputKey;
            set { _inputKey = (WinFormsKeys)value; Changed(); RefreshSummary(); }
        }

        public SendOption SelectedSendOption
        {
            get => _send;
            set
            {
                _send = value;
                Changed();
                Changed(nameof(ShowRawKey));
                RefreshSummary();
            }
        }

        public bool ShowRawKey => _send?.Kind == SendKind.RawKey;
        public int RawKeyCode { get => (int)_rawKey; set { _rawKey = (WinFormsKeys)value; Changed(); RefreshSummary(); } }

        public TargetOption SelectedTargetOption
        {
            get => _target;
            set
            {
                _target = value;
                Changed();
                Changed(nameof(ShowSeveral));
                RefreshSummary();
            }
        }

        public bool ShowSeveral => _target?.Kind == CustomModeTargetKind.Listed;

        public bool RequireAlt { get => _alt; set { _alt = value; Changed(); RefreshSummary(); } }
        public bool RequireControl { get => _ctrl; set { _ctrl = value; Changed(); RefreshSummary(); } }
        public bool RequireShift { get => _shift; set { _shift = value; Changed(); RefreshSummary(); } }

        /// <summary>Positive phrasing of <see cref="CustomModeBinding.ConsumeInput"/>: checked = also let the key reach the game.</summary>
        public bool PassThrough { get => !_consume; set { _consume = !value; Changed(); } }

        /// <summary>Whether this row's advanced strip (modifiers + pass-through) is expanded.</summary>
        public bool IsMoreOpen { get => _moreOpen; set { _moreOpen = value; Changed(); } }

        /// <summary>A readable one-line description of the rule (used for accessibility and tests).</summary>
        public string Summary
        {
            get
            {
                string mods = (_alt ? "Alt+" : "") + (_ctrl ? "Ctrl+" : "") + (_shift ? "Shift+" : "");
                string key = _inputKey == WinFormsKeys.None ? "(no key)" : _inputKey.ToString();
                string what = _send == null ? "?"
                    : _send.Kind == SendKind.Role ? _send.RoleTitle
                    : _send.Kind == SendKind.RawKey ? "the " + ((WinFormsKeys)_rawKey) + " key"
                    : "a click";
                string who = _target == null ? "?"
                    : _target.Kind == CustomModeTargetKind.All ? "all toons"
                    : _target.Kind == CustomModeTargetKind.Listed ? "several toons"
                    : _target.Label;
                return "Press " + mods + key + " -> " + what + " -> " + who;
            }
        }

        internal CustomModeBinding ToModel()
        {
            CustomModeBindingAction action =
                _send?.Kind == SendKind.RawKey ? CustomModeBindingAction.SendRawKey :
                _send?.Kind == SendKind.Click ? CustomModeBindingAction.InstantClick :
                CustomModeBindingAction.SendRole;

            CustomModeTargetKind kind = _target?.Kind ?? CustomModeTargetKind.All;

            return new CustomModeBinding
            {
                InputKey = (int)_inputKey,
                RequireAlt = _alt,
                RequireControl = _ctrl,
                RequireShift = _shift,
                Action = action,
                RoleTitle = _send?.Kind == SendKind.Role ? (_send.RoleTitle ?? "") : "",
                RawKey = (int)_rawKey,
                TargetKind = kind,
                TargetIndex = kind == CustomModeTargetKind.Single ? (_target?.Index ?? 1) : 1,
                ListedTargetIndices = kind == CustomModeTargetKind.Listed
                    ? SeveralToons.Where(t => t.IsChecked).Select(t => t.Index).ToList()
                    : null,
                ConsumeInput = _consume,
            };
        }

        private void RefreshSummary() => Changed(nameof(Summary));

        public event PropertyChangedEventHandler PropertyChanged;

        private void Changed([CallerMemberName] string name = null) =>
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}
