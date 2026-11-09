using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace SealSaveEditor
{
    internal sealed class GameIconImages : IDisposable
    {
        private readonly GameIconLoader loader;
        private readonly HashSet<int> known = new HashSet<int>();
        private readonly List<Bitmap> owned = new List<Bitmap>();
        public ImageList Images { get; private set; }
        public GameIconImages(string gameDirectory)
        {
            loader = new GameIconLoader(gameDirectory);
            Images = new ImageList { ColorDepth = ColorDepth.Depth32Bit, ImageSize = new Size(32, 32) };
        }
        public string Key(int id)
        {
            if (id <= 0) return "";
            string key = id.ToString(System.Globalization.CultureInfo.InvariantCulture);
            if (known.Contains(id)) return Images.Images.ContainsKey(key) ? key : "";
            known.Add(id);
            using (Bitmap original = loader.GetIcon(id))
            {
                if (original == null) return "";
                var bitmap = new Bitmap(32, 32, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
                using (Graphics graphics = Graphics.FromImage(bitmap))
                {
                    graphics.Clear(Color.Transparent); graphics.InterpolationMode = InterpolationMode.NearestNeighbor;
                    graphics.PixelOffsetMode = PixelOffsetMode.Half;
                    float scale = Math.Min(32F / original.Width, 32F / original.Height);
                    int width = Math.Max(1, (int)(original.Width * scale)), height = Math.Max(1, (int)(original.Height * scale));
                    graphics.DrawImage(original, new Rectangle((32 - width) / 2, (32 - height) / 2, width, height));
                }
                owned.Add(bitmap); Images.Images.Add(key, bitmap);
            }
            return key;
        }
        public Bitmap Preview(int id) { return id > 0 ? loader.GetIcon(id) : null; }
        public void Dispose()
        {
            Images.Dispose(); foreach (Bitmap image in owned) image.Dispose(); loader.Dispose();
        }
    }
}
