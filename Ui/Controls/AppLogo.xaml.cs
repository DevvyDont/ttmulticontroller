using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Wpf.Ui.Controls;

namespace TTMulti.Ui.Controls
{
    /// <summary>
    /// The app logo: the yin-yang cats (<c>Resources/icon-new.png</c>), duotone-recoloured at runtime so it
    /// reflects the user's palette. The black cat takes the Mirror-mode colour (<see cref="RightBrush"/>), the
    /// white cat the Multi-mode colour (<see cref="LeftBrush"/>); each cat's details flip to the other colour.
    /// Used as the About-window mark and, via <see cref="RenderIcon"/>, as the live window/taskbar icon.
    /// </summary>
    public partial class AppLogo : UserControl
    {
        /// <summary>Resolution the logo is rasterised at for the on-screen control (the Image then scales it).</summary>
        private const int ControlRenderSize = 256;

        // Brand defaults, matching the Multi (green) and Mirror (pink) mode-colour defaults.
        private static readonly Color DefaultLight = Color.FromRgb(0x32, 0xCD, 0x32); // Multi
        private static readonly Color DefaultDark = Color.FromRgb(0xEE, 0x85, 0xA0);  // Mirror

        public AppLogo()
        {
            InitializeComponent();
            UpdateImage();
        }

        /// <summary>Colour of the white cat (the Multi-mode colour).</summary>
        public static readonly DependencyProperty LeftBrushProperty =
            DependencyProperty.Register(nameof(LeftBrush), typeof(Brush), typeof(AppLogo),
                new PropertyMetadata(new SolidColorBrush(DefaultLight), OnColorChanged));

        public Brush LeftBrush
        {
            get => (Brush)GetValue(LeftBrushProperty);
            set => SetValue(LeftBrushProperty, value);
        }

        /// <summary>Colour of the black cat (the Mirror-mode colour).</summary>
        public static readonly DependencyProperty RightBrushProperty =
            DependencyProperty.Register(nameof(RightBrush), typeof(Brush), typeof(AppLogo),
                new PropertyMetadata(new SolidColorBrush(DefaultDark), OnColorChanged));

        public Brush RightBrush
        {
            get => (Brush)GetValue(RightBrushProperty);
            set => SetValue(RightBrushProperty, value);
        }

        /// <summary>Retained for source compatibility; the raster logo has no separating rim (no-op).</summary>
        public static readonly DependencyProperty RimBrushProperty =
            DependencyProperty.Register(nameof(RimBrush), typeof(Brush), typeof(AppLogo),
                new PropertyMetadata(Brushes.Transparent));

        public Brush RimBrush
        {
            get => (Brush)GetValue(RimBrushProperty);
            set => SetValue(RimBrushProperty, value);
        }

        /// <summary>Retained for source compatibility; the raster logo always carries the cats' faces (no-op).</summary>
        public static readonly DependencyProperty ShowFaceProperty =
            DependencyProperty.Register(nameof(ShowFace), typeof(bool), typeof(AppLogo),
                new PropertyMetadata(true));

        public bool ShowFace
        {
            get => (bool)GetValue(ShowFaceProperty);
            set => SetValue(ShowFaceProperty, value);
        }

        private static void OnColorChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
            => ((AppLogo)d).UpdateImage();

        private void UpdateImage()
        {
            if (Img == null) return; // during construction, before the template is applied
            Img.Source = LogoImages.Render(ColorOf(RightBrush, DefaultDark), ColorOf(LeftBrush, DefaultLight), ControlRenderSize);
        }

        /// <summary>
        /// Render the logo to a square bitmap for use as the window / taskbar icon. <paramref name="left"/> is the
        /// Multi (white cat) colour, <paramref name="right"/> the Mirror (black cat) colour; <paramref name="rim"/>
        /// and <paramref name="showFace"/> are ignored (kept for source compatibility).
        /// </summary>
        public static BitmapSource RenderIcon(Brush left, Brush right, int size, Brush rim = null, bool showFace = true)
            => LogoImages.Render(ColorOf(right, DefaultDark), ColorOf(left, DefaultLight), size);

        /// <summary>Render the logo icon using the user's current Multi (white) and Mirror (black) mode colours.</summary>
        public static BitmapSource CreateAppIcon(int size = 256) =>
            RenderIcon(ToBrush(Colors.LeftGroup), ToBrush(Colors.AllGroups), size);

        /// <summary>
        /// Apply the current logo as a window's icon everywhere it shows: the taskbar/alt-tab (Window.Icon) and,
        /// when a WPF-UI title bar is supplied, its in-chrome icon (sized by <paramref name="titleBarIconSize"/>).
        /// </summary>
        public static void ApplyAppIcon(Window window, TitleBar titleBar = null, double titleBarIconSize = 17)
        {
            var left = ToBrush(Colors.LeftGroup);
            var right = ToBrush(Colors.AllGroups);

            window.Icon = RenderIcon(left, right, 48);

            if (titleBar != null)
            {
                var bar = RenderIcon(left, right, 40);
                titleBar.Icon = new ImageIcon { Source = bar, Width = titleBarIconSize, Height = titleBarIconSize };
            }
        }

        private static Color ColorOf(Brush b, Color fallback) => b is SolidColorBrush s ? s.Color : fallback;

        private static SolidColorBrush ToBrush(System.Drawing.Color c)
        {
            var b = new SolidColorBrush(Color.FromArgb(c.A, c.R, c.G, c.B));
            b.Freeze();
            return b;
        }
    }
}
