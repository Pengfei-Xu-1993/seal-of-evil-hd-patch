using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;
using SealHdInstaller;

internal sealed class ReadOnlyWindowsLayers : ILayerStore
{
    private readonly WindowsLayerStore windows = new WindowsLayerStore();
    internal int Reads;
    public LayerValue Read(string hive, string view, string exe)
    {
        ++Reads;
        var value = windows.Read(hive, view, exe);
        if (value.Exists) throw new IOException("Unexpected Layers value for the never-launched private fixture. No changes were made.");
        return value;
    }
    public void EnsureWritable(LayerValue value, string exe) { throw new Exception("This fixture must never need a registry write."); }
    public void Write(LayerValue value, string exe) { throw new Exception("Real registry writes are forbidden in this private-desktop test."); }
}

internal static class RealBackendGuiTests
{
    private static string source, output, root;
    private static readonly List<string> log = new List<string>();
    private static int exit;
    private static T Control<T>(Form form, string name) where T : Control { return (T)form.Controls.Find(name, true).Single(); }
    private static void Assert(bool condition, string message) { if (!condition) throw new Exception(message); log.Add("PASS " + message); }
    private static async Task Wait(Func<bool> predicate, string label)
    {
        DateTime deadline = DateTime.UtcNow.AddSeconds(70);
        while (!predicate()) { if (DateTime.UtcNow > deadline) throw new TimeoutException(label); await Task.Delay(50); }
    }
    private static void Screenshot(Form form, string file)
    {
        using (var bitmap = new Bitmap(form.Width, form.Height)) { form.DrawToBitmap(bitmap, new Rectangle(Point.Empty, bitmap.Size)); bitmap.Save(Path.Combine(output, file)); }
    }
    private static async void Exercise(InstallerForm form)
    {
        try
        {
            await Wait(() => Control<Button>(form, "InstallerInstall").Enabled, "initial supported-folder inspection");
            Assert(Control<ComboBox>(form, "InstallerGameDirectory").Text == root, "real backend inspects the explicitly selected owned fixture");
            Assert(((ResolutionProfile)Control<ComboBox>(form, "InstallerResolution").SelectedItem).Id == 2, "real GUI defaults to 1080p");
            Assert(Control<Label>(form, "InstallerResult").Text.Contains("IV50"), "Chinese installer explains automatic video skipping before installation");
            Screenshot(form, "real-backend-before.png");
            Control<ComboBox>(form, "InstallerResolution").SelectedItem = ResolutionProfiles.Find(3840, 2160);
            Control<Button>(form, "InstallerInstall").PerformClick();
            await Wait(() => File.Exists(Path.Combine(root, InstallerFiles.StateName)) && Control<Button>(form, "InstallerClose").Enabled, "real backend installation completion");
            Assert(Control<ProgressBar>(form, "InstallerProgress").Value == 100, "GUI reaches 100 percent after actual backend writes");
            Assert(Control<Label>(form, "InstallerResult").Text.Contains("IV50") && Control<Label>(form, "InstallerResult").Text.Contains("安装完成"), "Chinese completion explains original videos are skipped without IV50");
            Assert(File.ReadAllText(Path.Combine(root, "SealHD.ini")).Contains("Width=3840") && File.ReadAllText(Path.Combine(root, "SealHD.ini")).Contains("Height=2160"), "GUI 4K choice is applied by the real backend");
            Assert(InstallerService.Inspect(root).Supported && InstallerService.Inspect(root).HasExistingPatch, "real backend verifies installed EXE and patch state");
            Screenshot(form, "real-backend-installed-zh.png");
            Control<ComboBox>(form, "InstallerLanguage").SelectedIndex = 1; form.Size = form.MinimumSize;
            await Task.Delay(150);
            Assert(Control<Label>(form, "InstallerResult").Text.Contains("Without IV50"), "English completion explains the same video fallback");
            Screenshot(form, "real-backend-installed-en-minimum.png");
            form.Close();
        }
        catch (Exception error) { log.Add(error.ToString()); exit = 1; form.Close(); }
    }
    [STAThread]
    private static int Main(string[] args)
    {
        source = args[0]; output = args[1]; Directory.CreateDirectory(output); root = Path.Combine(output, "game"); Directory.CreateDirectory(root); Directory.CreateDirectory(Path.Combine(root, "Ini"));
        var sourceHashes = new Dictionary<string, string>();
        foreach (string file in new[] { "SOE.EXE", "ERRelive.dll", "Gfx3d.dll", "Ini/Config.ini" })
        {
            sourceHashes[file] = InstallerFiles.Hash(Path.Combine(source, file.Replace('/', '\\')));
            File.Copy(Path.Combine(source, file.Replace('/', '\\')), Path.Combine(root, file.Replace('/', '\\')));
        }
        var registry = new ReadOnlyWindowsLayers(); InstallerService.TestLayerStore = registry;
        Application.EnableVisualStyles(); Application.SetCompatibleTextRenderingDefault(false);
        using (var form = new InstallerForm(root)) { form.Shown += delegate { Exercise(form); }; Application.Run(form); }
        try
        {
            if (File.Exists(Path.Combine(root, InstallerFiles.StateName))) InstallerService.Restore(root, null);
            foreach (var pair in sourceHashes)
            {
                Assert(InstallerFiles.Hash(Path.Combine(source, pair.Key.Replace('/', '\\'))) == pair.Value, "source unchanged: " + pair.Key);
                Assert(InstallerFiles.Hash(Path.Combine(root, pair.Key.Replace('/', '\\'))) == pair.Value, "private fixture restored: " + pair.Key);
            }
            Assert(registry.Reads > 0, "real Windows Layers values were read with zero writes");
        }
        catch (Exception error) { log.Add(error.ToString()); exit = 1; }
        log.Add("Real WinForms UI + real installer core + embedded payload. No game, probe, or elevated process was launched. Input desktop was not switched. Registry writes were prohibited.");
        File.WriteAllLines(Path.Combine(output, "RESULT.txt"), log); return exit;
    }
}
