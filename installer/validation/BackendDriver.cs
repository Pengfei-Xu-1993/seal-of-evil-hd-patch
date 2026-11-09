using System;
using System.Web.Script.Serialization;
using SealHdInstaller;

// Private validation driver; never part of the end-user package.
internal static class BackendDriver
{
    private static int Main(string[] args)
    {
        try
        {
            if (args.Length < 2) throw new ArgumentException("Usage: BackendDriver inspect|install|restore <owned-fixture-game-directory> [1080|1440|2160]");
            object result;
            Action<InstallProgress> progress = p => Console.Error.WriteLine(p.Percent + "% " + p.Message);
            if (args[0] == "inspect") result = InstallerService.Inspect(args[1]);
            else if (args[0] == "restore") result = InstallerService.Restore(args[1], progress);
            else if (args[0] == "install")
            {
                int height = args.Length > 2 ? Int32.Parse(args[2]) : 1080;
                result = InstallerService.Install(new InstallOptions { GameDirectory = args[1], Width = height == 1440 ? 2560 : height == 2160 ? 3840 : 1920, Height = height, Borderless = true }, progress);
            }
            else throw new ArgumentException("Unknown driver operation.");
            Console.WriteLine(new JavaScriptSerializer().Serialize(result)); return 0;
        }
        catch (Exception error) { Console.Error.WriteLine(error.ToString()); return 1; }
    }
}
