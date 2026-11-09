using System.Drawing;

namespace SealSaveEditor
{
    // The local desktop app's Segoe UI/sans-serif stack maps Chinese glyphs to
    // Microsoft YaHei UI through Windows DirectWrite. Select it explicitly so
    // WinForms controls do not use different fallback faces for the same text.
    public static class UiFonts
    {
        public const string FamilyName = "Microsoft YaHei UI";
        public const float BodySize = 10F;
        public static Font Create() { return Create(BodySize, FontStyle.Regular); }
        public static Font Create(float size, FontStyle style)
        { return new Font(FamilyName, size, style, GraphicsUnit.Point); }
    }
}
