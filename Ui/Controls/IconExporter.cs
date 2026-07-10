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
        // The static exe / Explorer / file-properties icon stays plain black-and-white (not the user's live
        // Multi/Mirror colours): the white cat stays white, the black cat stays black. RenderIcon maps
        // left -> light (white cat) and right -> dark (black cat), so left = White, right = Black.
        private static readonly Brush DefaultLeft = System.Windows.Media.Brushes.White;
        private static readonly Brush DefaultRight = System.Windows.Media.Brushes.Black;

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
