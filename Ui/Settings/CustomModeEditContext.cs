using System.Collections.Generic;
using System.ComponentModel;

namespace TTMulti.Ui.Settings
{
    /// <summary>A selectable toon target: a 1-based index (into the instant-multi-click / custom-mode order) plus
    /// a friendly label like "Toon 2 (Group 1, Right)". Built from the live controllers when Options opens.</summary>
    internal sealed class CustomModeToonOption
    {
        public int Index { get; }
        public string Label { get; }
        public CustomModeToonOption(int index, string label) { Index = index; Label = label; }
    }

    /// <summary>What a rule sends: a named role, one specific key, or a mouse click.</summary>
    internal enum SendKind { Role, RawKey, Click }

    /// <summary>One entry in a rule's "send" dropdown. Roles list first, then the two special entries.</summary>
    internal sealed class SendOption
    {
        public const string RawKeyLabel = "A specific key...";
        public const string ClickLabel = "A mouse click";

        public string Label { get; }
        public SendKind Kind { get; }
        public string RoleTitle { get; }

        public SendOption(string label, SendKind kind, string roleTitle = null)
        {
            Label = label;
            Kind = kind;
            RoleTitle = roleTitle;
        }
    }

    /// <summary>One entry in a rule's "to" dropdown: All toons, a single named toon, or the "Several toons" opener.</summary>
    internal sealed class TargetOption
    {
        public const string AllLabel = "All toons";
        public const string SeveralLabel = "Several toons...";

        public string Label { get; }
        public CustomModeTargetKind Kind { get; }
        public int Index { get; }

        public TargetOption(string label, CustomModeTargetKind kind, int index = 0)
        {
            Label = label;
            Kind = kind;
            Index = index;
        }
    }

    /// <summary>One checkable toon in a rule's "Several toons" list.</summary>
    internal sealed class ToonCheck : INotifyPropertyChanged
    {
        private bool _checked;

        public int Index { get; }
        public string Label { get; }
        internal System.Action Changed;

        public ToonCheck(int index, string label, bool isChecked)
        {
            Index = index;
            Label = label;
            _checked = isChecked;
        }

        public bool IsChecked
        {
            get => _checked;
            set
            {
                _checked = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsChecked)));
                Changed?.Invoke();
            }
        }

        public event PropertyChangedEventHandler PropertyChanged;
    }

    /// <summary>Shared material for building each rule's dropdowns: the role titles (from Multi-Mode Keys plus
    /// Zero Power Throw) and the current toon list. Created once by the editor and threaded to every rule VM.</summary>
    internal sealed class CustomModeEditContext
    {
        public IReadOnlyList<string> RoleTitles { get; }
        public IReadOnlyList<CustomModeToonOption> Toons { get; }

        public CustomModeEditContext(IReadOnlyList<string> roleTitles, IReadOnlyList<CustomModeToonOption> toons)
        {
            RoleTitles = roleTitles ?? new List<string>();
            Toons = toons ?? new List<CustomModeToonOption>();
        }
    }
}
