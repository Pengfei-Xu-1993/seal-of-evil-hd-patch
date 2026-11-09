using System;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace SealHdInstaller
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
                Application.Run(new InstallerForm(args.Length > 0 ? args[0] : null));
            }
            catch (Exception error)
            {
                MessageBox.Show(error.Message, "Seal of Evil HD Patch", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }
    }
}
