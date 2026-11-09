using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace SealSaveEditor
{
    internal static class Program
    {
        [DllImport("user32.dll")] private static extern bool SetProcessDPIAware();

        [STAThread]
        private static void Main(string[] args)
        {
            try
            {
                AppContext.SetSwitch("Switch.System.IO.UseLegacyPathHandling", false);
                AppContext.SetSwitch("Switch.System.IO.BlockLongPaths", false);
                SetProcessDPIAware();
                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                Application.Run(new MainForm(args.Length > 0 ? args[0] : null));
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message, "Seal of Evil Save Editor", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }
    }
}
