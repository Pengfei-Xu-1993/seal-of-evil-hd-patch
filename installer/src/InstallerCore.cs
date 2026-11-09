using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Web.Script.Serialization;
using Microsoft.Win32;

namespace SealHdInstaller
{
    public sealed class InstallOptions
    {
        public string GameDirectory { get; set; }
        public int Width { get; set; }
        public int Height { get; set; }
        public bool Borderless { get; set; }
        public bool ReplaceConflictingLoader { get; set; }
    }
    public sealed class InstallInspection
    {
        public bool Supported { get; set; }
        public bool GameRunning { get; set; }
        public bool HasExistingPatch { get; set; }
        public bool ConflictingLoader { get; set; }
        public string Details { get; set; }
    }
    public sealed class InstallProgress { public int Percent { get; set; } public string Message { get; set; } }
    public sealed class InstallResult
    {
        public string GameDirectory { get; set; }
        public string BackupDirectory { get; set; }
        public string ReportPath { get; set; }
        public string Summary { get; set; }
    }
    internal sealed class BundleFile { public string Path { get; set; } public string Sha256 { get; set; } }
    internal sealed class BundleManifest
    {
        public string Version { get; set; }
        public List<BundleFile> Files { get; set; }
        public Dictionary<string, string> SupportedGameHashes { get; set; }
    }
    internal sealed class SavedFile
    {
        public string Path { get; set; }
        public bool Exists { get; set; }
        public string Sha256 { get; set; }
    }
    internal sealed class InstallerState
    {
        public int Schema { get; set; }
        public string GameDirectory { get; set; }
        public string Version { get; set; }
        public string BackupRelativeDirectory { get; set; }
        public List<SavedFile> Originals { get; set; }
        public Dictionary<string, string> InstalledHashes { get; set; }
        public List<LayerValue> OriginalLayers { get; set; }
        public List<LayerValue> InstalledLayers { get; set; }
    }
    internal sealed class FileChange
    {
        internal string Relative, BeforeHash, AfterHash, BeforeCopy, AfterCopy;
    }

    public static class InstallerService
    {
        internal static string TestBundlePath = null;
        internal static ILayerStore TestLayerStore = null;
        internal static Func<string, bool> TestRunningGame = null;
        internal static Action<string> TestFailure = null;
        private static readonly Dictionary<string, string> SupportedHashes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) {
            { "SOE.EXE", "452B135E827B11520564BD07A789542D19CB63CCAF0AE93FEA53248537D8C00B" },
            { "ERRelive.dll", "B22AD4D0630D39B4E376EEF3EFDDFE7EE363823AE0CB15AE41DE6BEFB2E8126F" },
            { "Gfx3d.dll", "1B93A54C2F3086C509B2B81AF0B863E6CB36EF96F9CE94DF9814FFAD202CD078" }
        };
        private static JavaScriptSerializer Json { get { return new JavaScriptSerializer { MaxJsonLength = 4 * 1024 * 1024, RecursionLimit = 32 }; } }
        private static ILayerStore Layers { get { return TestLayerStore ?? new WindowsLayerStore(); } }

        public static IList<string> DetectGameDirectories()
        {
            var steam = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { @"D:\Steam", @"C:\Program Files (x86)\Steam", @"C:\Program Files\Steam" };
            foreach (RegistryHive hive in new[] { RegistryHive.CurrentUser, RegistryHive.LocalMachine })
                foreach (RegistryView view in Environment.Is64BitOperatingSystem ? new[] { RegistryView.Registry32, RegistryView.Registry64 } : new[] { RegistryView.Registry32 })
                    try
                    {
                        using (var basis = RegistryKey.OpenBaseKey(hive, view))
                        using (var key = basis.OpenSubKey(@"Software\Valve\Steam"))
                        {
                            if (key == null) continue;
                            foreach (string name in new[] { "SteamPath", "InstallPath" }) { string path = key.GetValue(name) as string; if (!String.IsNullOrWhiteSpace(path)) steam.Add(path); }
                        }
                    }
                    catch (System.Security.SecurityException) { }
                    catch (UnauthorizedAccessException) { }
            var libraries = new HashSet<string>(steam, StringComparer.OrdinalIgnoreCase);
            foreach (string root in steam.ToArray())
                try
                {
                    string vdf = Path.Combine(root, "steamapps", "libraryfolders.vdf");
                    if (!File.Exists(vdf) || new FileInfo(vdf).Length > 4 * 1024 * 1024) continue;
                    foreach (Match item in Regex.Matches(File.ReadAllText(vdf), "\"(?:path|[0-9]+)\"\\s+\"([^\"]+)\"", RegexOptions.IgnoreCase))
                    {
                        string path = item.Groups[1].Value.Replace("\\\\", "\\");
                        if (Path.IsPathRooted(path)) libraries.Add(path);
                    }
                }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }
            var found = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (string library in libraries)
                try
                {
                    foreach (string name in new[] { "Seal of Evil", "Seal of Evil English", "SealOfEvil" })
                    {
                        string candidate = Path.Combine(library, "steamapps", "common", name);
                        if (File.Exists(Path.Combine(candidate, "SOE.EXE"))) found.Add(InstallerFiles.Canonical(candidate));
                    }
                    string manifest = Path.Combine(library, "steamapps", "appmanifest_1726410.acf");
                    if (File.Exists(manifest))
                    {
                        var install = Regex.Match(File.ReadAllText(manifest), "\"installdir\"\\s+\"([^\"]+)\"", RegexOptions.IgnoreCase);
                        if (install.Success && install.Groups[1].Value.IndexOfAny(new[] { '/', '\\', ':' }) < 0)
                        {
                            string candidate = Path.Combine(library, "steamapps", "common", install.Groups[1].Value);
                            if (File.Exists(Path.Combine(candidate, "SOE.EXE"))) found.Add(InstallerFiles.Canonical(candidate));
                        }
                    }
                }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }
            return found.OrderBy(p => p, StringComparer.OrdinalIgnoreCase).ToList();
        }

        public static InstallInspection Inspect(string directory)
        {
            var result = new InstallInspection();
            var details = new StringBuilder();
            try
            {
                string root = ResolveRoot(directory);
                result.GameRunning = IsRunning(root);
                InstallerState state = LoadState(root);
                ValidateSupported(root, state);
                result.Supported = true;
                string loader = InstallerFiles.Target(root, "dinput8.dll");
                bool known = File.Exists(loader) && IsKnownLoader(loader);
                result.HasExistingPatch = state != null || (known && File.Exists(InstallerFiles.Target(root, "SealHD.ini")));
                result.ConflictingLoader = File.Exists(loader) && !known;
                details.AppendLine("Game: " + root);
                details.AppendLine("Supported game files: SHA-256 verified.");
                details.AppendLine("Running from this folder: " + result.GameRunning);
                details.AppendLine("Existing HD patch: " + result.HasExistingPatch);
                details.AppendLine("Other input loader: " + result.ConflictingLoader);
                details.AppendLine("The original intro/ending videos are skipped automatically if IV50 decoding is unavailable; video files are preserved.");
            }
            catch (Exception error) { details.AppendLine(error.Message); }
            result.Details = details.ToString(); return result;
        }

        public static InstallResult Install(InstallOptions options, Action<InstallProgress> progress)
        {
            if (options == null) throw new ArgumentNullException("options");
            if (!((options.Width == 1920 && options.Height == 1080) || (options.Width == 2560 && options.Height == 1440) || (options.Width == 3840 && options.Height == 2160)))
                throw new ArgumentException("Choose 1920 x 1080, 2560 x 1440, or 3840 x 2160.");
            string root = ResolveRoot(options.GameDirectory);
            using (var mutex = Acquire(root))
            using (var temporary = new OwnedTemporaryDirectory())
            {
                Notify(progress, 2, "Checking the game folder...");
                InstallerState prior = LoadState(root);
                ValidateSupported(root, prior); RequireStopped(root);
                string loader = InstallerFiles.Target(root, "dinput8.dll");
                if (File.Exists(loader) && !IsKnownLoader(loader) && !options.ReplaceConflictingLoader)
                    throw new IOException("Another dinput8.dll loader is installed. Choose back up and replace to continue.");
                var store = Layers;
                string exe = InstallerFiles.Target(root, "SOE.EXE");
                List<LayerValue> layersBefore = CompatibilityLayers.ReadAll(store, exe), layersAfter = CompatibilityLayers.Clean(layersBefore);
                PreflightLayers(store, exe, layersBefore, layersAfter);
                Notify(progress, 7, "Verifying the complete patch package...");
                string payload = Path.Combine(temporary.Path, "payload");
                BundleManifest bundle = ExtractBundle(payload);
                var snapshot = Capture(root, InstallerFiles.ManagedPaths, Path.Combine(temporary.Path, "before"));
                string newExe = Path.Combine(temporary.Path, "modern-SOE.EXE");
                Notify(progress, 25, "Preparing Windows compatibility...");
                ManifestEditor.CreateModernCopy(exe, newExe);
                if (!InstallerFiles.EqualHash(InstallerFiles.Hash(exe), snapshot.Single(f => f.Relative == "SOE.EXE").BeforeHash))
                    throw new IOException("The game executable changed while preparing the installation.");
                File.WriteAllBytes(Path.Combine(payload, "SealHD.ini"), InstallerConfig.PatchIni(ReadExisting(root, "SealHD.ini"), File.ReadAllBytes(Path.Combine(payload, "SealHD.ini")), options));
                string config = Path.Combine(temporary.Path, "game-config.ini");
                File.WriteAllBytes(config, InstallerConfig.PatchGameConfig(File.ReadAllBytes(InstallerFiles.Target(root, "Ini/Config.ini"))));
                foreach (var change in snapshot)
                {
                    change.AfterCopy = change.Relative == "SOE.EXE" ? newExe : change.Relative == "Ini/Config.ini" ? config : InstallerFiles.Target(payload, change.Relative);
                    change.AfterHash = InstallerFiles.Hash(change.AfterCopy);
                }
                string backupRelative = InstallerFiles.BackupName + "/" + DateTime.UtcNow.ToString("yyyyMMdd-HHmmss") + "-" + Guid.NewGuid().ToString("N");
                string backup = InstallerFiles.Target(root, backupRelative);
                Notify(progress, 35, "Backing up the files that will be replaced...");
                SaveBackup(root, backupRelative, snapshot);
                File.WriteAllText(InstallerFiles.Target(root, backupRelative + "/compatibility-before.json"), Json.Serialize(layersBefore), new UTF8Encoding(false));
                File.WriteAllText(InstallerFiles.Target(root, backupRelative + "/compatibility-planned.json"), Json.Serialize(layersAfter), new UTF8Encoding(false));
                var state = new InstallerState {
                    Schema = 1, GameDirectory = root, Version = bundle.Version,
                    BackupRelativeDirectory = prior == null ? backupRelative : prior.BackupRelativeDirectory,
                    Originals = prior == null ? snapshot.Select(f => new SavedFile { Path = f.Relative, Exists = f.BeforeHash != null, Sha256 = f.BeforeHash }).ToList() : prior.Originals,
                    OriginalLayers = prior == null ? layersBefore.Select(v => v.Copy()).ToList() : prior.OriginalLayers,
                    InstalledLayers = layersAfter.Select(v => v.Copy()).ToList(),
                    InstalledHashes = snapshot.ToDictionary(f => f.Relative, f => f.AfterHash, StringComparer.OrdinalIgnoreCase)
                };
                string stateFile = Path.Combine(temporary.Path, "new-state.json"); File.WriteAllText(stateFile, Json.Serialize(state), new UTF8Encoding(false));
                string reportFile = Path.Combine(temporary.Path, "new-report.txt");
                File.WriteAllText(reportFile, BuildReport("Installed", root, bundle.Version, backup, state.BackupRelativeDirectory, snapshot, layersBefore, layersAfter), new UTF8Encoding(false));
                var controls = Capture(root, new[] { InstallerFiles.StateName, InstallerFiles.ReportName }, Path.Combine(temporary.Path, "control-before"));
                controls[0].AfterCopy = stateFile; controls[1].AfterCopy = reportFile;
                foreach (var file in controls) file.AfterHash = InstallerFiles.Hash(file.AfterCopy);
                snapshot.AddRange(controls);
                Execute(root, snapshot, store, layersBefore, layersAfter, progress);
                Notify(progress, 100, "Installation complete.");
                return new InstallResult { GameDirectory = root, BackupDirectory = backup, ReportPath = InstallerFiles.Target(root, InstallerFiles.ReportName),
                    Summary = "The complete HD patch and local Windows compatibility fix were installed. If IV50 decoding is unavailable, original intro/ending videos are skipped automatically. Video files and saved games are preserved." };
            }
        }

        public static InstallResult Restore(string directory, Action<InstallProgress> progress)
        {
            string root = ResolveRoot(directory);
            using (var mutex = Acquire(root))
            using (var temporary = new OwnedTemporaryDirectory())
            {
                Notify(progress, 3, "Checking the installed patch and original backup...");
                RequireStopped(root);
                InstallerState state = LoadState(root);
                if (state == null) throw new IOException("No backup made by this installer was found. The game files were not changed.");
                ValidateSupported(root, state);
                var store = Layers; string exe = InstallerFiles.Target(root, "SOE.EXE");
                CompatibilityLayers.Verify(store, exe, state.InstalledLayers);
                PreflightLayers(store, exe, state.InstalledLayers, state.OriginalLayers);
                foreach (var file in state.Originals) InstallerFiles.RequireHash(InstallerFiles.Target(root, file.Path), state.InstalledHashes[file.Path]);
                var changes = Capture(root, InstallerFiles.ManagedPaths, Path.Combine(temporary.Path, "before"));
                foreach (var change in changes)
                {
                    SavedFile original = state.Originals.Single(f => f.Path == change.Relative);
                    change.AfterHash = original.Sha256;
                    change.AfterCopy = original.Exists ? InstallerFiles.Target(root, state.BackupRelativeDirectory + "/files/" + original.Path) : null;
                }
                var controls = Capture(root, new[] { InstallerFiles.StateName, InstallerFiles.ReportName }, Path.Combine(temporary.Path, "control-before"));
                controls[0].AfterCopy = null; controls[0].AfterHash = null;
                string report = Path.Combine(temporary.Path, "restore-report.txt");
                File.WriteAllText(report, BuildReport("Restored", root, state.Version, InstallerFiles.Target(root, state.BackupRelativeDirectory), state.BackupRelativeDirectory, changes, state.InstalledLayers, state.OriginalLayers), new UTF8Encoding(false));
                controls[1].AfterCopy = report; controls[1].AfterHash = InstallerFiles.Hash(report); changes.AddRange(controls);
                Execute(root, changes, store, state.InstalledLayers, state.OriginalLayers, progress);
                Notify(progress, 100, "Original files restored.");
                return new InstallResult { GameDirectory = root, BackupDirectory = InstallerFiles.Target(root, state.BackupRelativeDirectory), ReportPath = InstallerFiles.Target(root, InstallerFiles.ReportName), Summary = "The files and game-specific compatibility settings from before this installer were restored. Saved games were preserved." };
            }
        }

        private static void Execute(string root, List<FileChange> changes, ILayerStore store, List<LayerValue> layersBefore, List<LayerValue> layersAfter, Action<InstallProgress> progress)
        {
            string exe = InstallerFiles.Target(root, "SOE.EXE");
            var attempted = new List<FileChange>(); bool registryAttempted = false;
            foreach (var change in changes) InstallerFiles.RequireHash(InstallerFiles.Target(root, change.Relative), change.BeforeHash);
            CompatibilityLayers.Verify(store, exe, layersBefore); RequireStopped(root);
            try
            {
                registryAttempted = true;
                CompatibilityLayers.Apply(store, exe, layersBefore, layersAfter);
                Fail("after-registry");
                for (int i = 0; i < changes.Count; ++i)
                {
                    var change = changes[i];
                    Notify(progress, 42 + i * 50 / changes.Count, "Installing: " + change.Relative);
                    RequireStopped(root);
                    attempted.Add(change);
                    if (change.AfterCopy == null) InstallerFiles.Remove(root, change.Relative, change.BeforeHash);
                    else InstallerFiles.Replace(root, change.Relative, change.AfterCopy, change.BeforeHash);
                    Fail("after-file:" + change.Relative);
                }
                foreach (var change in changes) InstallerFiles.RequireHash(InstallerFiles.Target(root, change.Relative), change.AfterHash);
                CompatibilityLayers.Verify(store, exe, layersAfter);
                Fail("after-verification");
            }
            catch (Exception failure)
            {
                var rollbackErrors = new List<string>();
                foreach (var change in attempted.AsEnumerable().Reverse())
                    try
                    {
                        string target = InstallerFiles.Target(root, change.Relative);
                        string current = File.Exists(target) ? InstallerFiles.Hash(target) : null;
                        if (InstallerFiles.EqualHash(current, change.BeforeHash)) continue;
                        if (!InstallerFiles.EqualHash(current, change.AfterHash)) throw new IOException("A later external change was preserved: " + change.Relative);
                        if (change.BeforeCopy == null) InstallerFiles.Remove(root, change.Relative, current);
                        else InstallerFiles.Replace(root, change.Relative, change.BeforeCopy, current);
                    }
                    catch (Exception error) { rollbackErrors.Add(error.Message); }
                if (registryAttempted)
                    try
                    {
                        var current = CompatibilityLayers.ReadAll(store, exe);
                        for (int i = 0; i < current.Count; ++i)
                            if (!CompatibilityLayers.Equal(current[i], layersBefore[i]) && !CompatibilityLayers.Equal(current[i], layersAfter[i]))
                                throw new IOException("A later external compatibility-setting change was preserved.");
                        for (int i = 0; i < current.Count; ++i)
                            if (!CompatibilityLayers.Equal(current[i], layersBefore[i])) store.Write(layersBefore[i], exe);
                        CompatibilityLayers.Verify(store, exe, layersBefore);
                    }
                    catch (Exception error) { rollbackErrors.Add(error.Message); }
                if (rollbackErrors.Count > 0) throw new IOException(failure.Message + "\r\nAutomatic rollback needs attention: " + String.Join("; ", rollbackErrors) + "\r\nThe original backups remain in " + Path.Combine(root, InstallerFiles.BackupName) + ".", failure);
                throw new IOException(failure.Message + "\r\nAny installation changes were rolled back. Original backups were preserved.", failure);
            }
        }

        private static List<FileChange> Capture(string root, IEnumerable<string> paths, string copyRoot)
        {
            Directory.CreateDirectory(copyRoot); var result = new List<FileChange>();
            foreach (string relative in paths)
            {
                string target = InstallerFiles.Target(root, relative);
                if (Directory.Exists(target)) throw new IOException("A directory occupies an installation file path: " + relative);
                var change = new FileChange { Relative = relative };
                if (File.Exists(target))
                {
                    change.BeforeHash = InstallerFiles.Hash(target); change.BeforeCopy = InstallerFiles.Target(copyRoot, relative);
                    Directory.CreateDirectory(Path.GetDirectoryName(change.BeforeCopy)); File.Copy(target, change.BeforeCopy, false);
                    if (!InstallerFiles.EqualHash(InstallerFiles.Hash(change.BeforeCopy), change.BeforeHash)) throw new IOException("A file changed while taking a backup: " + relative);
                }
                result.Add(change);
            }
            return result;
        }
        private static void SaveBackup(string root, string backupRelative, List<FileChange> files)
        {
            string directory = InstallerFiles.Target(root, backupRelative);
            if (Directory.Exists(directory) || File.Exists(directory)) throw new IOException("The backup folder already exists.");
            Directory.CreateDirectory(directory);
            foreach (var file in files)
            {
                InstallerFiles.RequireHash(InstallerFiles.Target(root, file.Relative), file.BeforeHash);
                if (file.BeforeCopy == null) continue;
                string backup = InstallerFiles.Target(root, backupRelative + "/files/" + file.Relative);
                Directory.CreateDirectory(Path.GetDirectoryName(backup)); File.Copy(file.BeforeCopy, backup, false);
                InstallerFiles.RequireHash(backup, file.BeforeHash);
            }
            File.WriteAllText(InstallerFiles.Target(root, backupRelative + "/backup-manifest.json"), Json.Serialize(files.Select(f => new SavedFile { Path = f.Relative, Exists = f.BeforeHash != null, Sha256 = f.BeforeHash }).ToList()), new UTF8Encoding(false));
            foreach (string name in new[] { InstallerFiles.StateName, InstallerFiles.ReportName })
            {
                string source = InstallerFiles.Target(root, name);
                if (File.Exists(source)) File.Copy(source, InstallerFiles.Target(root, backupRelative + "/" + name), false);
            }
        }

        private static InstallerState LoadState(string root)
        {
            string path = InstallerFiles.Target(root, InstallerFiles.StateName);
            if (!File.Exists(path)) return null;
            if (new FileInfo(path).Length > 1024 * 1024) throw new InvalidDataException("The installer state file is too large.");
            InstallerState state;
            try { state = Json.Deserialize<InstallerState>(File.ReadAllText(path)); }
            catch (Exception e) { throw new InvalidDataException("The installer state could not be read. Preserve the backup folder before continuing.", e); }
            if (state == null || state.Schema != 1 || !String.Equals(state.GameDirectory, root, StringComparison.OrdinalIgnoreCase) ||
                state.BackupRelativeDirectory == null || !Regex.IsMatch(state.BackupRelativeDirectory, "^" + Regex.Escape(InstallerFiles.BackupName) + @"/[0-9]{8}-[0-9]{6}-[0-9a-f]{32}$") ||
                state.Originals == null || state.InstalledHashes == null || state.OriginalLayers == null || state.InstalledLayers == null)
                throw new InvalidDataException("The installer state does not belong to this game folder.");
            string[] expected = InstallerFiles.ManagedPaths.OrderBy(p => p, StringComparer.Ordinal).ToArray();
            if (!state.Originals.Select(f => f.Path).OrderBy(p => p, StringComparer.Ordinal).SequenceEqual(expected) ||
                !state.InstalledHashes.Keys.OrderBy(p => p, StringComparer.Ordinal).SequenceEqual(expected))
                throw new InvalidDataException("The installer state has an unexpected file list.");
            foreach (var file in state.Originals)
            {
                if (file.Exists != (file.Sha256 != null) || (file.Exists && !InstallerFiles.IsHash(file.Sha256)) || !InstallerFiles.IsHash(state.InstalledHashes[file.Path]))
                    throw new InvalidDataException("The installer state contains invalid file hashes.");
                if (file.Exists) InstallerFiles.RequireHash(InstallerFiles.Target(root, state.BackupRelativeDirectory + "/files/" + file.Path), file.Sha256);
            }
            if (!InstallerFiles.EqualHash(state.Originals.Single(f => f.Path == "SOE.EXE").Sha256, SupportedHashes["SOE.EXE"]))
                throw new InvalidDataException("The original game executable backup is not supported.");
            ValidateLayers(state.OriginalLayers); ValidateLayers(state.InstalledLayers);
            return state;
        }
        private static void ValidateLayers(List<LayerValue> values)
        {
            string[] keys = values.Select(v => v.Hive + "/" + v.View).OrderBy(v => v, StringComparer.Ordinal).ToArray();
            string[] expected = (from hive in new[] { "HKCU", "HKLM" } from view in Environment.Is64BitOperatingSystem ? new[] { "32", "64" } : new[] { "32" } select hive + "/" + view).OrderBy(v => v, StringComparer.Ordinal).ToArray();
            if (!keys.SequenceEqual(expected) || values.Any(v => v.Exists && (v.Value == null || (v.Kind != (int)RegistryValueKind.String && v.Kind != (int)RegistryValueKind.ExpandString))))
                throw new InvalidDataException("The installer state contains an invalid compatibility-setting list.");
            values.Sort((a, b) => String.CompareOrdinal(a.Hive + a.View, b.Hive + b.View));
        }

        private static void ValidateSupported(string root, InstallerState state)
        {
            foreach (var pair in SupportedHashes)
            {
                string path = InstallerFiles.Target(root, pair.Key);
                if (!File.Exists(path)) throw new FileNotFoundException("A required original game file is missing: " + pair.Key);
                string hash = InstallerFiles.Hash(path);
                if (InstallerFiles.EqualHash(hash, pair.Value)) continue;
                if (pair.Key == "SOE.EXE" && state != null && InstallerFiles.EqualHash(hash, state.InstalledHashes["SOE.EXE"])) continue;
                throw new InvalidDataException("Unsupported or modified game file: " + pair.Key + ". Verify the original Steam game files, then run the installer again.");
            }
            if (!File.Exists(InstallerFiles.Target(root, "Ini/Config.ini"))) throw new FileNotFoundException("The original Ini/Config.ini is missing.");
        }
        private static string ResolveRoot(string directory)
        {
            if (String.IsNullOrWhiteSpace(directory)) throw new ArgumentException("Select the Seal of Evil installation folder.");
            if (!Directory.Exists(directory)) throw new DirectoryNotFoundException("The selected game folder does not exist.");
            string root = InstallerFiles.Canonical(directory);
            if (Path.GetPathRoot(root).TrimEnd('\\', '/') == root.TrimEnd('\\', '/')) throw new IOException("Select the game's own folder, not a drive root.");
            InstallerFiles.Target(root, "SOE.EXE"); return root;
        }
        private static bool IsKnownLoader(string file)
        {
            string hash = InstallerFiles.Hash(file);
            if (InstallerFiles.EqualHash(hash, "54A78C9003CF8B373EFCA9A5C6187E360060B3DBD9AD22E5D5D1B29F90DEAB74")) return true;
            using (var stream = OpenBundle()) using (var archive = new ZipArchive(stream, ZipArchiveMode.Read))
                return InstallerFiles.EqualHash(hash, ReadBundleManifest(archive).Files.Single(f => f.Path == "dinput8.dll").Sha256);
        }
        internal static bool IsRunning(string root)
        {
            if (TestRunningGame != null) return TestRunningGame(root);
            string expected = InstallerFiles.Canonical(InstallerFiles.Target(root, "SOE.EXE"));
            foreach (var process in Process.GetProcessesByName("SOE"))
                using (process)
                    try
                    {
                        string path = process.MainModule.FileName;
                        if (String.Equals(InstallerFiles.Canonical(path), expected, StringComparison.OrdinalIgnoreCase)) return true;
                    }
                    catch (InvalidOperationException) { }
                    catch (System.ComponentModel.Win32Exception) { }
            return false;
        }
        private static void RequireStopped(string root) { if (IsRunning(root)) throw new IOException("The game is running from this folder. Save and exit it before installing or restoring the patch."); }
        private sealed class OwnedMutex : IDisposable
        {
            private readonly Mutex mutex;
            internal OwnedMutex(Mutex value) { mutex = value; }
            public void Dispose() { mutex.ReleaseMutex(); mutex.Dispose(); }
        }
        private static OwnedMutex Acquire(string root)
        {
            var mutex = new Mutex(false, "Local\\SealHDInstaller-" + InstallerFiles.HashBytes(Encoding.Unicode.GetBytes(root.ToUpperInvariant())));
            bool entered;
            try { entered = mutex.WaitOne(0); }
            catch (AbandonedMutexException) { entered = true; }
            if (!entered) { mutex.Dispose(); throw new IOException("Another installer is already working on this game folder."); }
            return new OwnedMutex(mutex);
        }
        private static byte[] ReadExisting(string root, string relative) { string path = InstallerFiles.Target(root, relative); return File.Exists(path) ? File.ReadAllBytes(path) : null; }
        private static void PreflightLayers(ILayerStore store, string exe, List<LayerValue> before, List<LayerValue> after)
        {
            for (int i = 0; i < before.Count; ++i) if (!CompatibilityLayers.Equal(before[i], after[i])) store.EnsureWritable(after[i], exe);
        }
        private static void Notify(Action<InstallProgress> callback, int percent, string message)
        {
            if (callback != null) callback(new InstallProgress { Percent = percent, Message = message });
        }
        private static void Fail(string stage) { if (TestFailure != null) TestFailure(stage); }

        private static Stream OpenBundle()
        {
            if (TestBundlePath != null) return File.OpenRead(TestBundlePath);
            Stream result = Assembly.GetExecutingAssembly().GetManifestResourceStream("SealHD.Payload.zip");
            if (result == null) throw new InvalidDataException("The complete patch package is missing from this installer. Download the complete installer again.");
            return result;
        }
        private static BundleManifest ReadBundleManifest(ZipArchive archive)
        {
            ZipArchiveEntry entry = archive.GetEntry("bundle-manifest.json");
            if (entry == null || entry.Length > 1024 * 1024) throw new InvalidDataException("The patch package manifest is missing or invalid.");
            BundleManifest manifest;
            using (var reader = new StreamReader(entry.Open(), new UTF8Encoding(false, true))) manifest = Json.Deserialize<BundleManifest>(reader.ReadToEnd());
            if (manifest == null || String.IsNullOrWhiteSpace(manifest.Version) || manifest.Version.Length > 128 || manifest.Files == null || manifest.SupportedGameHashes == null ||
                !manifest.Files.Select(f => f.Path).OrderBy(p => p, StringComparer.Ordinal).SequenceEqual(InstallerFiles.PayloadPaths.OrderBy(p => p, StringComparer.Ordinal)) ||
                manifest.Files.Any(f => !InstallerFiles.IsHash(f.Sha256)) || manifest.SupportedGameHashes.Count != SupportedHashes.Count)
                throw new InvalidDataException("The patch package has an unexpected manifest or file list.");
            foreach (var pair in SupportedHashes)
            {
                string value;
                if (!manifest.SupportedGameHashes.TryGetValue(pair.Key, out value) || !InstallerFiles.EqualHash(value, pair.Value)) throw new InvalidDataException("The patch package targets a different game build.");
            }
            return manifest;
        }
        private static BundleManifest ExtractBundle(string destination)
        {
            Directory.CreateDirectory(destination);
            using (var stream = OpenBundle())
            using (var archive = new ZipArchive(stream, ZipArchiveMode.Read))
            {
                BundleManifest manifest = ReadBundleManifest(archive);
                var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                var expected = new HashSet<string>(InstallerFiles.PayloadPaths.Concat(new[] { "bundle-manifest.json" }), StringComparer.Ordinal);
                long total = 0;
                foreach (var entry in archive.Entries)
                {
                    string relative = entry.FullName;
                    if (relative.EndsWith("/", StringComparison.Ordinal))
                    {
                        if (relative != "SealHD-assets/" && relative != "SealHD-bin/") throw new InvalidDataException("Unexpected package directory: " + relative);
                        if (!seen.Add(relative)) throw new InvalidDataException("Duplicate package directory: " + relative);
                        continue;
                    }
                    if (!expected.Contains(relative) || !seen.Add(relative)) throw new InvalidDataException("Unexpected or duplicate package file: " + relative);
                    if (entry.Length < 0 || entry.Length > 64 * 1024 * 1024 || (total += entry.Length) > 256 * 1024 * 1024) throw new InvalidDataException("The patch package exceeds its size limit.");
                    if (relative == "bundle-manifest.json") continue;
                    string target = InstallerFiles.Target(destination, relative);
                    Directory.CreateDirectory(Path.GetDirectoryName(target));
                    using (Stream input = entry.Open())
                    using (var output = new FileStream(target, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                    {
                        var buffer = new byte[65536]; long copied = 0; int read;
                        while ((read = input.Read(buffer, 0, buffer.Length)) != 0)
                        {
                            copied += read; if (copied > entry.Length) throw new InvalidDataException("A package file exceeds its declared size.");
                            output.Write(buffer, 0, read);
                        }
                        if (copied != entry.Length) throw new InvalidDataException("A package file is incomplete.");
                    }
                    string hash = manifest.Files.Single(f => f.Path == relative).Sha256;
                    if (!InstallerFiles.EqualHash(InstallerFiles.Hash(target), hash)) throw new InvalidDataException("A patch package file failed SHA-256 verification: " + relative);
                }
                if (expected.Any(path => !seen.Contains(path))) throw new InvalidDataException("The patch package is incomplete.");
                return manifest;
            }
        }
        private static string BuildReport(string action, string root, string version, string backup, string originalBackup, List<FileChange> files, List<LayerValue> before, List<LayerValue> after)
        {
            var report = new StringBuilder();
            report.AppendLine("Seal of Evil HD Patch - " + action); report.AppendLine("UTC: " + DateTime.UtcNow.ToString("O"));
            report.AppendLine("Package: " + version); report.AppendLine("Game: " + root); report.AppendLine("This operation's backup: " + backup);
            report.AppendLine("Restore baseline: " + originalBackup); report.AppendLine();
            foreach (var file in files) report.AppendLine(file.Relative + " | before=" + (file.BeforeHash ?? "absent") + " | after=" + (file.AfterHash ?? "absent"));
            report.AppendLine(); report.AppendLine("Only this SOE.EXE path's compatibility Layers values were considered.");
            for (int i = 0; i < before.Count; ++i) report.AppendLine(before[i].Hive + " " + before[i].View + " | before=" + (before[i].Exists ? before[i].Value : "absent") + " | after=" + (after[i].Exists ? after[i].Value : "absent"));
            report.AppendLine(); report.AppendLine(action == "Installed" ? "The game executable has a locally embedded Windows 8/8.1/10/11 compatibility manifest; its code, data, and other resources are verified unchanged." : "The executable and game-specific compatibility settings were restored to the captured pre-installation state.");
            if (action == "Installed") report.AppendLine("The HD main menu is enabled. Unsupported rendering paths safely use the original artwork.");
            report.AppendLine(action == "Installed" ? "If IV50 decoding is unavailable, original intro/ending videos are skipped automatically. No global codec is installed; video files and saved games are preserved." : "No movie files, saved games, or global codec settings were changed by this restore.");
            return report.ToString();
        }
    }
}
