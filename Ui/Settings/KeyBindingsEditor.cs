using System.Collections.ObjectModel;
using System.Linq;
using WinFormsKeys = System.Windows.Forms.Keys;

namespace TTMulti.Ui.Settings
{
    /// <summary>
    /// Owns the in-memory Multi-Mode key-bindings list (the ControlsPicker's job in the old dialog). Loads
    /// from <c>SerializedSettings.Default.Bindings</c>, and on OK serializes back — only touching the
    /// keyBindings setting inside the transaction, so Cancel (Reload) discards edits. Registered with the
    /// <see cref="SettingsSession"/> as an <see cref="ISettingsEditor"/>.
    /// </summary>
    internal sealed class KeyBindingsEditor : ISettingsEditor
    {
        public ObservableCollection<KeyMappingRowViewModel> Rows { get; }

        internal KeyBindingsEditor()
        {
            Rows = new ObservableCollection<KeyMappingRowViewModel>(
                Properties.SerializedSettings.Default.Bindings.Select(m => new KeyMappingRowViewModel(m)));
        }

        internal void AddNew() =>
            Rows.Add(new KeyMappingRowViewModel(
                new KeyMapping("New binding", WinFormsKeys.None, WinFormsKeys.None, WinFormsKeys.None, false)));

        internal void Remove(KeyMappingRowViewModel row)
        {
            if (row != null && !row.IsReadOnly)
                Rows.Remove(row);
        }

        public void Commit() =>
            Properties.SerializedSettings.Default.Bindings = Rows.Select(r => r.ToModel()).ToList();

        public void Discard()
        {
            // Nothing to undo: the keyBindings setting was never touched during editing (only on Commit), and
            // Settings.Reload() reverts it anyway.
        }
    }
}
