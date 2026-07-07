using System.Windows;

namespace TTMulti.Ui
{
    /// <summary>
    /// A small always-on-top, movable window showing the live layout diagram for the currently selected preset,
    /// so the user can position it to watch the preview while adjusting per-window overrides. Bound to the
    /// LayoutPresetsEditor so it follows preset selection and updates as the preset changes.
    /// </summary>
    public partial class LayoutPreviewWindow : Window
    {
        internal LayoutPreviewWindow(object editorDataContext)
        {
            InitializeComponent();
            DataContext = editorDataContext;
        }
    }
}
