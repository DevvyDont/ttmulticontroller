using System.IO;
using System.Windows.Media.Imaging;

namespace TTMulti.Ui
{
    /// <summary>Bridges the WinForms <see cref="System.Drawing.Bitmap"/> resources (finder icons) into WPF.</summary>
    internal static class WpfImaging
    {
        /// <summary>
        /// Convert a GDI+ bitmap to a frozen <see cref="BitmapSource"/> via an in-memory PNG (preserves alpha,
        /// leaks no GDI handles — unlike the GetHbitmap/CreateBitmapSourceFromHBitmap route).
        /// </summary>
        internal static BitmapSource ToBitmapSource(System.Drawing.Bitmap bitmap)
        {
            using (var stream = new MemoryStream())
            {
                bitmap.Save(stream, System.Drawing.Imaging.ImageFormat.Png);
                stream.Position = 0;

                var image = new BitmapImage();
                image.BeginInit();
                image.CacheOption = BitmapCacheOption.OnLoad;
                image.StreamSource = stream;
                image.EndInit();
                image.Freeze();
                return image;
            }
        }
    }
}
