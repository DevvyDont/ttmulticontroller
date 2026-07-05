using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace TTMulti.Ui.Controls
{
    /// <summary>
    /// A clickable colour swatch bound to an ARGB <see cref="int"/> setting (the same
    /// <c>Color.ToArgb()</c> value the old dialog stored). Clicking opens the standard colour picker; the
    /// selected colour is written back as ARGB so the on-disk value stays byte-compatible.
    /// </summary>
    public partial class ColorSwatch : UserControl
    {
        public ColorSwatch()
        {
            InitializeComponent();
            UpdateFill();
        }

        public static readonly DependencyProperty ColorArgbProperty =
            DependencyProperty.Register(nameof(ColorArgb), typeof(int), typeof(ColorSwatch),
                new FrameworkPropertyMetadata(0, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault,
                    (d, e) => ((ColorSwatch)d).UpdateFill()));

        /// <summary>The colour as a packed ARGB int (== System.Drawing.Color.ToArgb()).</summary>
        public int ColorArgb
        {
            get => (int)GetValue(ColorArgbProperty);
            set => SetValue(ColorArgbProperty, value);
        }

        private void UpdateFill()
        {
            int argb = ColorArgb;
            var color = Color.FromArgb((byte)(argb >> 24), (byte)(argb >> 16), (byte)(argb >> 8), (byte)argb);
            fill.Background = new SolidColorBrush(color);
        }

        private void SwatchButton_Click(object sender, RoutedEventArgs e)
        {
            using (var dlg = new System.Windows.Forms.ColorDialog
            {
                Color = System.Drawing.Color.FromArgb(ColorArgb),
                FullOpen = true,
                AnyColor = true,
            })
            {
                if (dlg.ShowDialog() == System.Windows.Forms.DialogResult.OK)
                    ColorArgb = dlg.Color.ToArgb();
            }
        }
    }
}
