using System.Drawing;

namespace SealHdInstaller
{
    internal static class UiFonts
    {
        public static Font Create() { return Create(10F, FontStyle.Regular); }
        public static Font Create(float size, FontStyle style)
        { return new Font("Microsoft YaHei UI", size, style, GraphicsUnit.Point); }
    }
}
