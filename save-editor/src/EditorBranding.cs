using System;
using System.Drawing;
using System.Windows.Forms;

namespace SealSaveEditor
{
    internal static class EditorBranding
    {
        public static void Apply(Form form)
        {
            try { form.Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); }
            catch (ArgumentException) { }
        }
    }
}
