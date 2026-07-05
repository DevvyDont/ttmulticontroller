using System.Collections.Generic;

namespace TTMulti.Ui.Settings
{
    /// <summary>
    /// The Options transaction. Pages bind live to <c>Settings.Default</c> (matching the old dialog's
    /// OnPropertyChanged data-bindings), so OK just persists and Cancel reverts to the on-disk state:
    /// <see cref="Commit"/> = Save(), <see cref="Discard"/> = Reload() — identical user semantics to the
    /// WinForms dialog's write-through + OnFormClosing Reload() trick. Editors that own non-<c>Settings</c>
    /// state (the keybindings XML, the custom-mode and layout-preset files, which only touch disk on OK)
    /// register through <see cref="Register"/>; their Commit/Discard run inside this transaction.
    /// </summary>
    internal sealed class SettingsSession
    {
        private readonly List<ISettingsEditor> _editors = new List<ISettingsEditor>();
        private bool _completed;

        internal void Register(ISettingsEditor editor) => _editors.Add(editor);

        internal void Commit()
        {
            if (_completed) return;
            _completed = true;

            foreach (var editor in _editors)
                editor.Commit();

            Properties.Settings.Default.Save();
        }

        internal void Discard()
        {
            if (_completed) return;
            _completed = true;

            foreach (var editor in _editors)
                editor.Discard();

            // Revert every live-bound Settings edit back to the last-saved (on-disk) state.
            Properties.Settings.Default.Reload();
        }
    }

    /// <summary>An editor that owns state outside <c>Settings.Default</c> (files / serialized blobs).</summary>
    internal interface ISettingsEditor
    {
        /// <summary>Persist the edited state (called on OK, before Settings.Save()).</summary>
        void Commit();

        /// <summary>Drop the edited in-memory state without writing it (called on Cancel/X).</summary>
        void Discard();
    }
}
