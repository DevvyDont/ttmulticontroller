using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace TTMulti.Ui.Controls
{
    /// <summary>
    /// A single cute cat-head silhouette (traced from the reference art) with a face — eyes, nose and fanned,
    /// curved whiskers. The face auto-colours for contrast against <see cref="Fill"/>. Composed twice by
    /// <see cref="AppLogo"/> to make the two-toon mark.
    /// </summary>
    public partial class CatHead : UserControl
    {
        public CatHead()
        {
            InitializeComponent();
        }

        /// <summary>The cat's body colour.</summary>
        public static readonly DependencyProperty FillProperty =
            DependencyProperty.Register(nameof(Fill), typeof(Brush), typeof(CatHead),
                new PropertyMetadata(new SolidColorBrush(Color.FromRgb(0x32, 0xCD, 0x32))));

        public Brush Fill
        {
            get => (Brush)GetValue(FillProperty);
            set => SetValue(FillProperty, value);
        }

        /// <summary>Rim stroked around the head to separate it from an overlapping cat (usually the surface colour).</summary>
        public static readonly DependencyProperty RimBrushProperty =
            DependencyProperty.Register(nameof(RimBrush), typeof(Brush), typeof(CatHead),
                new PropertyMetadata(Brushes.Transparent));

        public Brush RimBrush
        {
            get => (Brush)GetValue(RimBrushProperty);
            set => SetValue(RimBrushProperty, value);
        }

        /// <summary>Whether to draw the face (eyes/nose/whiskers). Hidden at small icon sizes where the fine
        /// detail just turns to mud; the plain coloured silhouette reads far better there.</summary>
        public static readonly DependencyProperty ShowFaceProperty =
            DependencyProperty.Register(nameof(ShowFace), typeof(bool), typeof(CatHead),
                new PropertyMetadata(true));

        public bool ShowFace
        {
            get => (bool)GetValue(ShowFaceProperty);
            set => SetValue(ShowFaceProperty, value);
        }
    }
}
