using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Web.Script.Serialization;
using Microsoft.Win32;
using SealHdInstaller;

internal sealed class MemoryLayers : ILayerStore
{
    internal readonly Dictionary<string, LayerValue> Values = new Dictionary<string, LayerValue>();
    internal int Writes, FailWrite = -1;
    internal bool Deny, SharedViews;
    private string Key(string hive, string view) { return hive + "/" + (SharedViews ? "shared" : view); }
    public LayerValue Read(string hive, string view, string exe)
    {
        LayerValue value;
        var copy = Values.TryGetValue(Key(hive, view), out value) ? value.Copy() : new LayerValue();
        copy.Hive = hive; copy.View = view; return copy;
    }
    public void EnsureWritable(LayerValue value, string exe) { if (Deny) throw new UnauthorizedAccessException("Administrator permission is required (fixture preflight)."); }
    public void Write(LayerValue value, string exe)
    {
        ++Writes;
        if (Writes == FailWrite) throw new IOException("Injected registry write failure.");
        Values[Key(value.Hive, value.View)] = value.Copy();
    }
    internal void Seed(string hive, string view, string value)
    {
        Values[Key(hive, view)] = new LayerValue { Hive = hive, View = view, Exists = true, Value = value, Kind = (int)RegistryValueKind.String };
    }
}

internal static class BackendTests
{
    private static string source, output, bundle;
    private static int assertions;
    private static readonly List<string> log = new List<string>();
    private static void Assert(bool condition, string message) { if (!condition) throw new Exception("ASSERT: " + message); ++assertions; log.Add("PASS " + message); }
    private static T Throws<T>(Action action, string message) where T : Exception
    {
        try { action(); } catch (T e) { Assert(true, message); return e; }
        throw new Exception("Expected " + typeof(T).Name + ": " + message);
    }
    private static string Fixture(string name)
    {
        string root = Path.Combine(output, name); Directory.CreateDirectory(root); Directory.CreateDirectory(Path.Combine(root, "Ini"));
        foreach (string file in new[] { "SOE.EXE", "ERRelive.dll", "Gfx3d.dll", "Ini/Config.ini" }) File.Copy(Path.Combine(source, file.Replace('/', '\\')), Path.Combine(root, file.Replace('/', '\\')));
        Directory.CreateDirectory(Path.Combine(root, "System")); Directory.CreateDirectory(Path.Combine(root, "Movie"));
        File.WriteAllText(Path.Combine(root, "System", "save-sentinel.bin"), "never touch this save");
        File.WriteAllText(Path.Combine(root, "Movie", "video-sentinel.bin"), "never touch this movie");
        return root;
    }
    private static Dictionary<string, string> Snapshot(string root)
    {
        return InstallerFiles.ManagedPaths.Concat(new[] { "ERRelive.dll", "Gfx3d.dll", "System/save-sentinel.bin", "Movie/video-sentinel.bin", InstallerFiles.StateName }).ToDictionary(p => p, p => File.Exists(InstallerFiles.Target(root, p)) ? InstallerFiles.Hash(InstallerFiles.Target(root, p)) : null);
    }
    private static void Same(Dictionary<string, string> before, string root, string message)
    {
        var after = Snapshot(root);
        Assert(before.All(p => InstallerFiles.EqualHash(p.Value, after[p.Key])), message);
    }
    private static InstallOptions Options(string root, int height = 1080) { return new InstallOptions { GameDirectory = root, Width = height == 1440 ? 2560 : height == 2160 ? 3840 : 1920, Height = height, Borderless = true, ReplaceConflictingLoader = true }; }
    private static MemoryLayers NormalLayers()
    {
        var store = new MemoryLayers();
        store.Seed("HKCU", "32", "$ DWM8And16BitMitigation");
        store.Seed("HKCU", "64", "~ DWM8And16BitMitigation WINXPSP3");
        store.Seed("HKLM", "32", "~ $ DWM8And16BitMitigation");
        store.Seed("HKLM", "64", "~ DWM8And16BitMitigation HIGHDPIAWARE");
        return store;
    }
    private static void InstallRestore()
    {
        string root = Fixture("install-update-restore");
        File.WriteAllBytes(Path.Combine(root, "dinput8.dll"), Encoding.ASCII.GetBytes("unrelated loader fixture"));
        File.WriteAllText(Path.Combine(root, "dgVoodoo.conf"), "existing renderer config fixture");
        File.WriteAllText(Path.Combine(root, "SealHD.ini"), "[Display]\r\nWidth=800\r\nHeight=600\r\nProfileIndex=0\r\nBorderless=0\r\n[User]\r\nCustom=preserved\r\n[Assets]\r\nMainMenu=0\r\nCustomAsset=preserved\r\n", Encoding.UTF8);
        var before = Snapshot(root); var store = NormalLayers(); InstallerService.TestLayerStore = store;
        var layersBefore = CompatibilityLayers.ReadAll(store, Path.Combine(root, "SOE.EXE"));
        var inspect = InstallerService.Inspect(root);
        Assert(inspect.Supported && inspect.ConflictingLoader && !inspect.GameRunning, "original supported fixture and conflicting loader detected");
        var denied = Options(root); denied.ReplaceConflictingLoader = false;
        Throws<IOException>(() => InstallerService.Install(denied, null), "conflicting loader requires explicit replacement"); Same(before, root, "conflict rejection leaves files unchanged");
        var result = InstallerService.Install(Options(root), null);
        Assert(Directory.Exists(result.BackupDirectory) && File.Exists(result.ReportPath), "successful install returns backup and report");
        Assert(File.Exists(Path.Combine(root, "SealHD-bin", "SealVideoProbe.exe")), "complete embedded payload includes portable video probe");
        Assert(InstallerService.Inspect(root).HasExistingPatch && !InstallerService.Inspect(root).ConflictingLoader, "installed patch recognized without another conflict prompt");
        ManifestEditor.VerifyPreserved(Path.Combine(source, "SOE.EXE"), Path.Combine(root, "SOE.EXE")); Assert(true, "real vendor PE protected sections and other resources preserved");
        string ini = File.ReadAllText(Path.Combine(root, "SealHD.ini"));
        Assert(ini.Contains("Width=1920") && ini.Contains("Height=1080") && ini.Contains("ProfileIndex=2") && ini.Contains("Custom=preserved"), "1080p selected and unknown INI settings retained");
        Assert(System.Text.RegularExpressions.Regex.Matches(ini, @"\[Display\]").Count == 1 && File.ReadAllBytes(Path.Combine(root, "SealHD.ini")).Take(3).SequenceEqual(new byte[] { 239, 187, 191 }), "UTF-8 BOM retained without duplicate sections");
        Assert(ini.Contains("ForceWindowed=1") && ini.Contains("VirtualFullscreen=1"), "required fullscreen compatibility settings merged");
        Assert(ini.Contains("MainMenu=1") && ini.Contains("CustomAsset=preserved"), "old diagnostic MainMenu=0 becomes 1 while unknown Assets settings are retained");
        Assert(!store.Read("HKCU", "32", "").Exists && !store.Read("HKLM", "32", "").Exists, "dollar/tilde-only Layers values deleted");
        Assert(store.Read("HKCU", "64", "").Value == "~  WINXPSP3" && store.Read("HKLM", "64", "").Value == "~  HIGHDPIAWARE", "unrelated compatibility flags and spacing retained");
        byte[] originalConfig = File.ReadAllBytes(Path.Combine(source, "Ini", "Config.ini")), installedConfig = File.ReadAllBytes(Path.Combine(root, "Ini", "Config.ini"));
        Assert(originalConfig.Length == installedConfig.Length && originalConfig.Zip(installedConfig, (a, b) => a == b ? 0 : 1).Sum() <= 1, "legacy game config changes at most one byte");
        File.AppendAllText(Path.Combine(root, "SealHD.ini"), "\r\n[Personal]\r\nKeepThis=1\r\n");
        var update = InstallerService.Install(Options(root, 1440), null);
        Assert(update.BackupDirectory != result.BackupDirectory, "update keeps an independent current-file backup");
        ini = File.ReadAllText(Path.Combine(root, "SealHD.ini"));
        Assert(ini.Contains("Width=2560") && ini.Contains("Height=1440") && ini.Contains("ProfileIndex=3") && ini.Contains("KeepThis=1"), "in-place update selects 1440p and retains user settings");
        byte[] installedIni = File.ReadAllBytes(Path.Combine(root, "SealHD.ini"));
        File.AppendAllText(Path.Combine(root, "SealHD.ini"), ";later edit");
        var changed = Snapshot(root);
        Throws<IOException>(() => InstallerService.Restore(root, null), "restore refuses to overwrite a later user edit"); Same(changed, root, "blocked restore leaves all files unchanged");
        File.WriteAllBytes(Path.Combine(root, "SealHD.ini"), installedIni);
        InstallerService.Restore(root, null); Same(before, root, "restore returns exact original bytes and original absence after update");
        CompatibilityLayers.Verify(store, Path.Combine(root, "SOE.EXE"), layersBefore); Assert(true, "restore returns all compatibility values exactly");
        Assert(Directory.Exists(result.BackupDirectory), "original backup preserved after restore");
    }
    private static void RollbackTests()
    {
        foreach (string stage in new[] { "after-registry", "after-file:dinput8.dll", "after-file:SOE.EXE", "after-file:SealHD-installer-state.json", "after-verification" })
        {
            string root = Fixture("rollback-" + stage.Replace(':', '-'));
            var store = NormalLayers(); InstallerService.TestLayerStore = store;
            var layers = CompatibilityLayers.ReadAll(store, ""); var before = Snapshot(root);
            InstallerService.TestFailure = current => { if (current == stage) throw new IOException("Injected " + stage); };
            Throws<IOException>(() => InstallerService.Install(Options(root), null), "injected failure caught at " + stage);
            InstallerService.TestFailure = null;
            Same(before, root, "complete file rollback at " + stage);
            CompatibilityLayers.Verify(store, "", layers); Assert(true, "complete registry rollback at " + stage);
        }
        {
            string root = Fixture("rollback-second-registry-write"); var before = Snapshot(root);
            var store = NormalLayers(); store.FailWrite = 2; InstallerService.TestLayerStore = store;
            var layers = CompatibilityLayers.ReadAll(store, "");
            Throws<IOException>(() => InstallerService.Install(Options(root), null), "second registry write failure caught");
            Same(before, root, "second registry write failure leaves game files unchanged");
            CompatibilityLayers.Verify(store, "", layers); Assert(true, "first registry write rolled back after second fails");
        }
        {
            string root = Fixture("permission-preflight"); var before = Snapshot(root); var store = NormalLayers(); store.Deny = true; InstallerService.TestLayerStore = store;
            var e = Throws<UnauthorizedAccessException>(() => InstallerService.Install(Options(root), null), "missing registry permission rejected in preflight");
            Assert(e.Message.Contains("Administrator"), "permission error clearly requests administrator");
            Assert(store.Writes == 0 && !Directory.Exists(Path.Combine(root, InstallerFiles.BackupName)), "permission failure occurs before registry writes and target backup creation");
            Same(before, root, "permission failure leaves target files untouched");
        }
        {
            string root = Fixture("shared-registry-views"); var store = new MemoryLayers { SharedViews = true }; InstallerService.TestLayerStore = store;
            store.Seed("HKCU", "32", "$ DWM8And16BitMitigation"); store.Seed("HKLM", "32", "~ DWM8And16BitMitigation HIGHDPIAWARE");
            var before = CompatibilityLayers.ReadAll(store, "");
            InstallerService.Install(Options(root), null); Assert(!store.Read("HKCU", "64", "").Exists, "shared physical registry views clean successfully");
            InstallerService.Restore(root, null); CompatibilityLayers.Verify(store, "", before); Assert(true, "shared physical registry views restore successfully");
        }
    }
    private static void GuardsAndPackageTests()
    {
        string root = Fixture("guards"); var before = Snapshot(root); InstallerService.TestLayerStore = new MemoryLayers();
        InstallerService.TestRunningGame = path => path == InstallerFiles.Canonical(root);
        Assert(InstallerService.Inspect(root).GameRunning, "same-folder running game detected");
        Throws<IOException>(() => InstallerService.Install(Options(root), null), "running game blocks install"); InstallerService.TestRunningGame = null;
        Same(before, root, "running-game rejection leaves files unchanged");
        Throws<ArgumentException>(() => InstallerService.Install(new InstallOptions { GameDirectory = root, Width = 800, Height = 600 }, null), "unsupported screen mode rejected");
        File.AppendAllText(Path.Combine(root, "Gfx3d.dll"), "modified");
        Assert(!InstallerService.Inspect(root).Supported, "modified vendor DLL rejected");
        File.Copy(Path.Combine(source, "Gfx3d.dll"), Path.Combine(root, "Gfx3d.dll"), true);
        foreach (string malformed in new[] { "traversal", "duplicate", "corrupt", "missing" })
        {
            string bad = Path.Combine(output, "bad-" + malformed + ".zip"); File.Copy(bundle, bad);
            using (var zip = ZipFile.Open(bad, ZipArchiveMode.Update))
            {
                if (malformed == "traversal") { using (var writer = new StreamWriter(zip.CreateEntry("../outside.txt").Open())) writer.Write("outside"); }
                if (malformed == "duplicate") { using (var writer = new StreamWriter(zip.CreateEntry("dinput8.dll").Open())) writer.Write("duplicate"); }
                if (malformed == "corrupt") { var entry = zip.GetEntry("D3D8.dll"); entry.Delete(); using (var writer = new StreamWriter(zip.CreateEntry("D3D8.dll").Open())) writer.Write("bad hash"); }
                if (malformed == "missing") zip.GetEntry("DDraw.dll").Delete();
            }
            InstallerService.TestBundlePath = bad;
            Throws<InvalidDataException>(() => InstallerService.Install(Options(root), null), "invalid package rejected: " + malformed);
            InstallerService.TestBundlePath = null; Same(before, root, "invalid package never changes game: " + malformed);
        }
        var values = new List<LayerValue> { new LayerValue { Hive = "HKCU", View = "32", Exists = true, Value = "", Kind = 1 } };
        Assert(CompatibilityLayers.Clean(values)[0].Exists, "unrelated empty Layers value is preserved");
        Throws<InvalidDataException>(() => InstallerFiles.Target(root, "../outside.txt"), "target path traversal rejected");
    }
    private static int Main(string[] args)
    {
        source = args[0]; output = args[1]; bundle = args[2]; Directory.CreateDirectory(output);
        var sourceHashes = new[] { "SOE.EXE", "ERRelive.dll", "Gfx3d.dll", "Ini/Config.ini" }.ToDictionary(p => p, p => InstallerFiles.Hash(Path.Combine(source, p.Replace('/', '\\'))));
        try
        {
            InstallRestore(); RollbackTests(); GuardsAndPackageTests();
            foreach (var pair in sourceHashes) Assert(InstallerFiles.Hash(Path.Combine(source, pair.Key.Replace('/', '\\'))) == pair.Value, "real source remains unchanged: " + pair.Key);
            log.Add("PASS TOTAL " + assertions + " assertions. Vendor files were only copied/read. No game or probe executed. Registry was in-memory only.");
            File.WriteAllLines(Path.Combine(output, "RESULT.txt"), log, Encoding.UTF8); Console.WriteLine(log.Last()); return 0;
        }
        catch (Exception error)
        {
            log.Add(error.ToString()); File.WriteAllLines(Path.Combine(output, "RESULT.txt"), log, Encoding.UTF8); Console.Error.WriteLine(error); return 1;
        }
    }
}
