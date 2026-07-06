using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Wpf.Ui.Controls;

namespace TTMulti.Ui.Controls
{
    /// <summary>
    /// The app logo: two cute cat-head silhouettes overlapping like a pair of toons controlled together. The
    /// two heads are coloured independently — the front one with the Multi-mode colour, the back one with the
    /// Mirror-mode colour — so the logo reflects the user's own palette. Used as the About-window mark and,
    /// via <see cref="RenderIcon"/>, as the live window/taskbar icon.
    /// </summary>
    public partial class AppLogo : UserControl
    {
        public AppLogo()
        {
            InitializeComponent();
        }

        /// <summary>Fill of the front cat — the Multi-mode colour.</summary>
        public static readonly DependencyProperty LeftBrushProperty =
            DependencyProperty.Register(nameof(LeftBrush), typeof(Brush), typeof(AppLogo),
                new PropertyMetadata(new SolidColorBrush(Color.FromRgb(0x32, 0xCD, 0x32))));

        public Brush LeftBrush
        {
            get => (Brush)GetValue(LeftBrushProperty);
            set => SetValue(LeftBrushProperty, value);
        }

        /// <summary>Fill of the back cat — the Mirror-mode colour.</summary>
        public static readonly DependencyProperty RightBrushProperty =
            DependencyProperty.Register(nameof(RightBrush), typeof(Brush), typeof(AppLogo),
                new PropertyMetadata(new SolidColorBrush(Color.FromRgb(0xEE, 0x85, 0xA0))));

        public Brush RightBrush
        {
            get => (Brush)GetValue(RightBrushProperty);
            set => SetValue(RightBrushProperty, value);
        }

        /// <summary>Rim drawn around the front cat to separate it from the back one (usually the surface colour).</summary>
        public static readonly DependencyProperty RimBrushProperty =
            DependencyProperty.Register(nameof(RimBrush), typeof(Brush), typeof(AppLogo),
                new PropertyMetadata(Brushes.Transparent));

        public Brush RimBrush
        {
            get => (Brush)GetValue(RimBrushProperty);
            set => SetValue(RimBrushProperty, value);
        }

        // (The face colour — eyes/nose/whiskers — is derived from LeftBrush by LuminanceContrastConverter.)

        /// <summary>Whether the rendered cats wear a face. Set false for small icon renders (taskbar/title bar),
        /// where the fine detail just turns to mud; the plain silhouette reads far better small.</summary>
        public static readonly DependencyProperty ShowFaceProperty =
            DependencyProperty.Register(nameof(ShowFace), typeof(bool), typeof(AppLogo),
                new PropertyMetadata(true));

        public bool ShowFace
        {
            get => (bool)GetValue(ShowFaceProperty);
            set => SetValue(ShowFaceProperty, value);
        }

        /// <summary>Below this pixel size the face is dropped from rendered icons (whiskers/eyes just smear).</summary>
        public const int FaceMinSize = 56;

        /// <summary>
        /// Render the logo to a square bitmap for use as the window / taskbar icon. Runs off-screen on the UI
        /// thread; <paramref name="rim"/> defaults to transparent (an icon has no surface behind it). Pass
        /// <paramref name="showFace"/> = false for small renders where the face would only smear.
        /// </summary>
        public static BitmapSource RenderIcon(Brush left, Brush right, int size, Brush rim = null, bool showFace = true)
        {
            var logo = new AppLogo
            {
                LeftBrush = left,
                RightBrush = right,
                RimBrush = rim ?? Brushes.Transparent,
                ShowFace = showFace,
                Width = size,
                Height = size,
            };
            logo.Measure(new Size(size, size));
            logo.Arrange(new Rect(0, 0, size, size));
            logo.UpdateLayout();

            var rtb = new RenderTargetBitmap(size, size, 96, 96, PixelFormats.Pbgra32);
            rtb.Render(logo);
            rtb.Freeze();
            return rtb;
        }

        /// <summary>
        /// Render the logo icon using the user's current Multi (front) and Mirror (back) mode colours. The face
        /// is dropped automatically below <see cref="FaceMinSize"/> so small icons stay crisp.
        /// </summary>
        public static BitmapSource CreateAppIcon(int size = 256) =>
            RenderIcon(ToBrush(Colors.LeftGroup), ToBrush(Colors.AllGroups), size, showFace: size >= FaceMinSize);

        /// <summary>
        /// Apply the current logo as a window's icon everywhere it shows: the taskbar/alt-tab (Window.Icon) and,
        /// when a WPF-UI title bar is supplied, its in-chrome icon (sized by <paramref name="titleBarIconSize"/>).
        /// Both are always small on screen, so both render face-less at roughly their display size — this avoids
        /// the smeared face and the blur of downscaling one big 256px bitmap.
        /// </summary>
        public static void ApplyAppIcon(Window window, TitleBar titleBar = null, double titleBarIconSize = 17)
        {
            var left = ToBrush(Colors.LeftGroup);
            var right = ToBrush(Colors.AllGroups);

            // Taskbar / alt-tab: single face-less bitmap sized for typical taskbar/alt-tab pixels (~24–48).
            window.Icon = RenderIcon(left, right, 48, showFace: false);

            if (titleBar != null)
            {
                // Title bar shows ~17px; render face-less at 2× for a crisp downscale.
                var bar = RenderIcon(left, right, 40, showFace: false);
                titleBar.Icon = new ImageIcon { Source = bar, Width = titleBarIconSize, Height = titleBarIconSize };
            }
        }

        private static SolidColorBrush ToBrush(System.Drawing.Color c)
        {
            var b = new SolidColorBrush(Color.FromArgb(c.A, c.R, c.G, c.B));
            b.Freeze();
            return b;
        }
    }
}
