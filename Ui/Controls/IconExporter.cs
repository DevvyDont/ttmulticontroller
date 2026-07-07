using System.Collections.Generic;
using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace TTMulti.Ui.Controls
{
    /// <summary>
    /// Renders the app logo to a multi-resolution .ico file. Used two ways: the "--export-icon" startup switch
    /// bakes the fixed-default-colour file into the build (the exe's ApplicationIcon), and SaveUserIco writes a
    /// per-user file in the user's Multi/Mirror colours that TaskbarIconManager points the pinned shortcut at.
    /// </summary>
    internal static class IconExporter
    {
        private static readonly Brush DefaultLeft = new SolidColorBrush(Color.FromRgb(0x32, 0xCD, 0x32));   // green
        private static readonly Brush DefaultRight = new SolidColorBrush(Color.FromRgb(0xEE, 0x85, 0xA0));  // pink

        private static readonly int[] Sizes = { 16, 24, 32, 48, 64, 128, 256 };

        /// <summary>Write the .ico using the fixed default palette (the stable exe/Explorer icon).</summary>
        public static void SaveIco(string path) => SaveIco(path, DefaultLeft, DefaultRight);

        /// <summary>Write the .ico using the user's current Multi (front) and Mirror (back) mode colours.</summary>
        public static void SaveUserIco(string path) =>
            SaveIco(path, ToBrush(TTMulti.Colors.LeftGroup), ToBrush(TTMulti.Colors.AllGroups));

        public static void SaveIco(string path, Brush left, Brush right)
        {
            var pngs = new List<byte[]>();
            foreach (int size in Sizes)
            {
                BitmapSource bmp = AppLogo.RenderIcon(left, right, size);
                var encoder = new PngBitmapEncoder();
                encoder.Frames.Add(BitmapFrame.Create(bmp));
                using (var ms = new MemoryStream())
                {
                    encoder.Save(ms);
                    pngs.Add(ms.ToArray());
                }
            }

            string dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                Directory.CreateDirectory(dir);

            using (var fs = new FileStream(path, FileMode.Create, FileAccess.Write))
            using (var w = new BinaryWriter(fs))
            {
                // ICONDIR
                w.Write((short)0);              // reserved
                w.Write((short)1);              // type = icon
                w.Write((short)Sizes.Length);   // image count

                int offset = 6 + 16 * Sizes.Length;
                for (int i = 0; i < Sizes.Length; i++)
                {
                    int size = Sizes[i];
                    byte dim = (byte)(size >= 256 ? 0 : size);   // 0 means 256 in the ICO spec
                    w.Write(dim);                 // width
                    w.Write(dim);                 // height
                    w.Write((byte)0);             // palette count
                    w.Write((byte)0);             // reserved
                    w.Write((short)1);            // colour planes
                    w.Write((short)32);           // bits per pixel
                    w.Write(pngs[i].Length);      // bytes in resource
                    w.Write(offset);              // offset of image data
                    offset += pngs[i].Length;
                }

                foreach (byte[] png in pngs)
                    w.Write(png);
            }
        }

        private static Brush ToBrush(System.Drawing.Color c)
        {
            var b = new SolidColorBrush(Color.FromArgb(c.A, c.R, c.G, c.B));
            b.Freeze();
            return b;
        }
    }
}
