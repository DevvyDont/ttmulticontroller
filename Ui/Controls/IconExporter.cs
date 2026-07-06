using System.Collections.Generic;
using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace TTMulti.Ui.Controls
{
    /// <summary>
    /// Renders the app logo to a multi-resolution .ico file for the static exe/Explorer icon (ApplicationIcon).
    /// Uses fixed default Multi/Mirror colours so the file icon is stable regardless of the user's palette (the
    /// live window/taskbar icon still recolours at runtime). Invoked via the "--export-icon" startup switch.
    /// </summary>
    internal static class IconExporter
    {
        private static readonly Brush DefaultLeft = new SolidColorBrush(Color.FromRgb(0x32, 0xCD, 0x32));   // green
        private static readonly Brush DefaultRight = new SolidColorBrush(Color.FromRgb(0xEE, 0x85, 0xA0));  // pink

        private static readonly int[] Sizes = { 16, 24, 32, 48, 64, 128, 256 };

        public static void SaveIco(string path)
        {
            var pngs = new List<byte[]>();
            foreach (int size in Sizes)
            {
                // Drop the face on the small frames (16–48) where it just smears; keep it on the large ones
                // (64–256) that Explorer shows in its big-icon views.
                BitmapSource bmp = AppLogo.RenderIcon(DefaultLeft, DefaultRight, size, showFace: size >= AppLogo.FaceMinSize);
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
    }
}
