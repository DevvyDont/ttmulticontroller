using System;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;

namespace TTMulti.Ui.Controls
{
    /// <summary>
    /// The WPF replacement for the WinForms SelectWindowCrosshair: the user presses on the finder icon and
    /// drags onto a Toontown window; on release the window under the cursor is resolved (in physical pixels,
    /// so it is DPI-safe) and reported via <see cref="WindowSelected"/>. When a window is assigned, the tile
    /// is tinted with the toon's accent colour, mirroring the old control's coloured background.
    /// </summary>
    public partial class WindowCrosshair : UserControl
    {
        /// <summary>Raised when a drag completes with the resolved window handle (IntPtr.Zero if none valid).</summary>
        public event EventHandler<IntPtr> WindowSelected;

        private static Cursor _searchCursor;
        private bool _dragging;

        public WindowCrosshair()
        {
            InitializeComponent();

            MouseLeftButtonDown += OnMouseLeftButtonDown;
            MouseLeftButtonUp += OnMouseLeftButtonUp;
        }

        private static Cursor SearchCursor =>
            _searchCursor ?? (_searchCursor = new Cursor(new MemoryStream(Properties.Resources.searchw)));

        // ── SelectedWindowHandle ────────────────────────────────────────────────────

        public static readonly DependencyProperty SelectedWindowHandleProperty =
            DependencyProperty.Register(nameof(SelectedWindowHandle), typeof(IntPtr), typeof(WindowCrosshair),
                new PropertyMetadata(IntPtr.Zero, (d, e) => ((WindowCrosshair)d).UpdateVisual()));

        public IntPtr SelectedWindowHandle
        {
            get => (IntPtr)GetValue(SelectedWindowHandleProperty);
            set => SetValue(SelectedWindowHandleProperty, value);
        }

        // ── AccentBrush (toon colour shown when a window is assigned) ────────────────

        public static readonly DependencyProperty AccentBrushProperty =
            DependencyProperty.Register(nameof(AccentBrush), typeof(Brush), typeof(WindowCrosshair),
                new PropertyMetadata(Brushes.Transparent, (d, e) => ((WindowCrosshair)d).UpdateVisual()));

        public Brush AccentBrush
        {
            get => (Brush)GetValue(AccentBrushProperty);
            set => SetValue(AccentBrushProperty, value);
        }

        private void UpdateVisual()
        {
            bool assigned = SelectedWindowHandle != IntPtr.Zero;
            rootBorder.BorderThickness = new Thickness(assigned ? 2 : 1);
            rootBorder.BorderBrush = assigned && AccentBrush != null
                ? AccentBrush
                : (Brush)FindResource("ControlStrokeColorDefaultBrush");
            // Colour the target icon with the toon's accent once a window is picked; muted until then.
            finderIcon.Foreground = assigned && AccentBrush != null
                ? AccentBrush
                : (Brush)FindResource("TextFillColorSecondaryBrush");
        }

        // ── Drag-to-pick ────────────────────────────────────────────────────────────

        private void OnMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            _dragging = true;
            CaptureMouse();
            finderIcon.Filled = true;
            Cursor = SearchCursor;
        }

        private void OnMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            if (!_dragging)
                return;

            _dragging = false;
            ReleaseMouseCapture();
            Cursor = Cursors.Hand;
            finderIcon.Filled = false;

            // Physical-pixel cursor position → the window directly under it. Reject our own window and any
            // non-top-level window (only a root game window is a valid target), matching the old control.
            System.Drawing.Point cursor = Win32.GetCursorPosition();
            IntPtr hWnd = Win32.WindowFromPoint(cursor);

            IntPtr ownHandle = OwnerHandle();
            if (hWnd == ownHandle || hWnd != Win32.GetAncestor(hWnd, Win32.GetAncestorFlags.GetRoot))
                hWnd = IntPtr.Zero;

            SelectedWindowHandle = hWnd;
            WindowSelected?.Invoke(this, hWnd);
        }

        private IntPtr OwnerHandle()
        {
            var window = Window.GetWindow(this);
            return window != null ? new WindowInteropHelper(window).Handle : IntPtr.Zero;
        }
    }
}
