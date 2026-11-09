using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Security.Cryptography;

namespace SealSaveEditor
{
    public static class SaveSlotStore
    {
        private sealed class SlotFile
        {
            public string RelativePath;
            public string Path;
            public string Hash;
        }

        private sealed class Replacement
        {
            public SlotFile Before;
            public string Temporary;
            public string ExpectedHash;
            public bool Changed;
        }

        public static string Hash(string path)
        {
            using (SHA256 hash = SHA256.Create())
            using (FileStream stream = File.OpenRead(IoPath(path)))
                return BitConverter.ToString(hash.ComputeHash(stream)).Replace("-", "");
        }

        // Windows Framework applications can still hit MAX_PATH in individual
        // file APIs even when longPathAware and AppContext switches are enabled.
        // Use the extended syntax only at the IO boundary; keep normal paths for
        // UI messages and containment checks.
        private static string IoPath(string path)
        {
            if (path.StartsWith(@"\\?\", StringComparison.Ordinal)) return path;
            string full = Path.GetFullPath(path);
            if (full.StartsWith(@"\\", StringComparison.Ordinal)) return @"\\?\UNC\" + full.Substring(2);
            return @"\\?\" + full;
        }

        private static bool FileExists(string path) { return File.Exists(IoPath(path)); }
        private static bool DirectoryExists(string path) { return Directory.Exists(IoPath(path)); }
        private static void CreateDirectory(string path) { Directory.CreateDirectory(IoPath(path)); }
        private static void CopyFile(string source, string target) { File.Copy(IoPath(source), IoPath(target), false); }
        private static void MoveFile(string source, string target) { File.Move(IoPath(source), IoPath(target)); }
        private static void ReplaceFile(string source, string target) { File.Replace(IoPath(source), IoPath(target), null); }
        private static void DeleteFile(string path) { File.Delete(IoPath(path)); }
        private static SaveDocument ReadPair(string gam, string user) { return SaveDocument.Load(IoPath(gam), IoPath(user)); }

        // Only these exact files belong to a slot. Other extensions and other slots
        // are never copied, deleted or replaced by a save/restore operation.
        private static List<SlotFile> SlotPaths(SaveDocument document)
        {
            string directory = Path.GetDirectoryName(Path.GetFullPath(document.FilePath));
            string stem = Path.GetFileNameWithoutExtension(document.FilePath);
            var files = new List<SlotFile>();
            foreach (string extension in new[] { ".gam", ".fog", ".log", ".map", ".qed", ".scn" })
                files.Add(new SlotFile { RelativePath = Path.Combine("System", "game", stem + extension), Path = Path.Combine(directory, stem + extension) });
            if (String.IsNullOrEmpty(document.UserFilePath)) throw new InvalidDataException("The complete GAM/USE save pair is required.");
            files.Add(new SlotFile { RelativePath = Path.Combine("System", "user", stem + ".use"), Path = Path.GetFullPath(document.UserFilePath) });
            return files;
        }

        public static Dictionary<string, string> Files(SaveDocument document)
        {
            var files = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (SlotFile file in SlotPaths(document)) if (FileExists(file.Path)) files.Add(file.RelativePath, file.Path);
            return files;
        }

        private static List<SlotFile> Capture(SaveDocument document)
        {
            List<SlotFile> files = SlotPaths(document);
            foreach (SlotFile file in files) file.Hash = FileExists(file.Path) ? Hash(file.Path) : null;
            if (files[0].Hash == null || files[files.Count - 1].Hash == null)
                throw new InvalidDataException("The complete GAM/USE save pair is required.");
            return files;
        }

        private static void CheckSnapshot(List<SlotFile> files)
        {
            foreach (SlotFile file in files)
            {
                string actual = FileExists(file.Path) ? Hash(file.Path) : null;
                if (!String.Equals(actual, file.Hash, StringComparison.Ordinal))
                    throw new IOException("A save slot file changed while the operation was being prepared. Reopen the slot and try again: " + file.Path);
            }
        }

        public static string BackupRoot(SaveDocument document)
        { return Path.Combine(Path.GetDirectoryName(document.FilePath), "SealSaveEditor-backups"); }

        private static string NewBackup(SaveDocument document, string marker)
        {
            return Path.Combine(BackupRoot(document), Path.GetFileNameWithoutExtension(document.FilePath) + marker + DateTime.Now.ToString("yyyyMMdd-HHmmss-fff") + "-" + Guid.NewGuid().ToString("N").Substring(0, 5));
        }

        private static void RequireClosed()
        {
            Process[] processes = Process.GetProcessesByName("SOE");
            try { if (processes.Length > 0) throw new InvalidOperationException("Exit Seal of Evil before writing, exporting edited data or restoring a slot."); }
            finally { foreach (Process process in processes) process.Dispose(); }
        }

        private static void CheckUnchanged(SaveDocument document, string gamHash, string userHash)
        {
            if (String.IsNullOrEmpty(gamHash) || String.IsNullOrEmpty(userHash)
                || !String.Equals(Hash(document.FilePath), gamHash, StringComparison.OrdinalIgnoreCase)
                || !String.Equals(Hash(document.UserFilePath), userHash, StringComparison.OrdinalIgnoreCase))
                throw new IOException("The save changed after it was opened. Reopen the slot before editing it.");
        }

        public static void Export(SaveDocument document, string destination, bool includeEdits)
        { ExportInternal(document, destination, includeEdits, null, null, false); }

        public static void ExportEdited(SaveDocument document, string destination, string expectedGamHash, string expectedUserHash)
        { ExportInternal(document, destination, true, expectedGamHash, expectedUserHash, true); }

        private static void ExportInternal(SaveDocument document, string destination, bool includeEdits, string expectedGamHash, string expectedUserHash, bool checkLoadedState)
        {
            if (checkLoadedState) { RequireClosed(); CheckUnchanged(document, expectedGamHash, expectedUserHash); }
            List<SlotFile> snapshot = Capture(document);
            destination = Path.GetFullPath(destination);
            string system = Path.Combine(destination, "System");
            if (DirectoryExists(system) || FileExists(system))
                throw new IOException("The destination already contains a System folder. Choose a new or empty folder.");
            CreateDirectory(destination);
            string staging = Path.Combine(destination, ".SealSaveEditor-" + Guid.NewGuid().ToString("N"));
            CreateDirectory(staging);
            try
            {
                string stagedGam = null, stagedUser = null;
                foreach (SlotFile file in snapshot)
                {
                    if (file.Hash == null) continue;
                    string target = Path.Combine(staging, file.RelativePath);
                    CreateDirectory(Path.GetDirectoryName(target));
                    bool isGam = String.Equals(file.Path, document.FilePath, StringComparison.OrdinalIgnoreCase);
                    bool isUser = String.Equals(file.Path, document.UserFilePath, StringComparison.OrdinalIgnoreCase);
                    if (isGam) { stagedGam = target; if (includeEdits) document.SaveAs(IoPath(target)); else CopyFile(file.Path, target); }
                    else if (isUser) { stagedUser = target; if (includeEdits) document.SaveUserAs(IoPath(target)); else CopyFile(file.Path, target); }
                    else CopyFile(file.Path, target);
                    if ((!includeEdits || (!isGam && !isUser)) && Hash(target) != file.Hash)
                        throw new IOException("A slot file changed while it was being copied: " + file.Path);
                }
                if (stagedGam == null || stagedUser == null) throw new InvalidDataException("The complete GAM/USE save pair is required.");
                ReadPair(stagedGam, stagedUser);
                CheckSnapshot(snapshot);
                if (checkLoadedState) { RequireClosed(); CheckUnchanged(document, expectedGamHash, expectedUserHash); }
                Directory.Move(IoPath(Path.Combine(staging, "System")), IoPath(system));
            }
            finally { RemoveStaging(destination, staging); }
        }

        public static string Save(SaveDocument document, string expectedGamHash, string expectedUserHash)
        {
            RequireClosed();
            CheckUnchanged(document, expectedGamHash, expectedUserHash);
            List<SlotFile> before = Capture(document);
            string backup = NewBackup(document, "-");
            Export(document, backup, false);
            CheckUnchanged(document, expectedGamHash, expectedUserHash);
            CheckSnapshot(before);
            var replacements = new List<Replacement>();
            string token = ".SealSaveEditor-" + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                var gam = new Replacement { Before = before[0], Temporary = document.FilePath + token };
                var user = new Replacement { Before = before[before.Count - 1], Temporary = document.UserFilePath + token };
                replacements.Add(gam); replacements.Add(user);
                document.SaveAs(IoPath(gam.Temporary)); document.SaveUserAs(IoPath(user.Temporary));
                gam.ExpectedHash = Hash(gam.Temporary); user.ExpectedHash = Hash(user.Temporary);
                ReadPair(gam.Temporary, user.Temporary);
                RequireClosed(); CheckUnchanged(document, expectedGamHash, expectedUserHash); CheckSnapshot(before);
                Apply(document, replacements, backup);
                return backup;
            }
            finally { foreach (Replacement replacement in replacements) TryDeleteFile(replacement.Temporary); }
        }

        // Restore the complete fixed-extension snapshot, including absence: a
        // current companion absent from the backup is removed. Other files remain.
        public static string Restore(SaveDocument current, string backupGamPath, string expectedGamHash, string expectedUserHash)
        {
            RequireClosed(); CheckUnchanged(current, expectedGamHash, expectedUserHash);
            SaveDocument backup = SaveDocument.Load(IoPath(backupGamPath));
            if (!String.Equals(Path.GetFileName(backup.FilePath), Path.GetFileName(current.FilePath), StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Select a backup for the same save slot.");
            List<SlotFile> before = Capture(current), desired = Capture(backup);
            string recovery = NewBackup(current, "-before-restore-");
            Export(current, recovery, false);
            CheckUnchanged(current, expectedGamHash, expectedUserHash);
            CheckSnapshot(before); CheckSnapshot(desired);
            string token = ".SealSaveEditor-" + Guid.NewGuid().ToString("N") + ".tmp";
            var replacements = new List<Replacement>();
            try
            {
                for (int i = 0; i < before.Count; i++)
                {
                    SlotFile original = before[i], source = desired[i];
                    if (!String.Equals(original.RelativePath, source.RelativePath, StringComparison.OrdinalIgnoreCase))
                        throw new InvalidDataException("The backup slot file layout does not match the current slot.");
                    if (original.Hash == source.Hash) continue;
                    var replacement = new Replacement { Before = original, ExpectedHash = source.Hash };
                    replacements.Add(replacement);
                    if (source.Hash != null)
                    {
                        replacement.Temporary = original.Path + token;
                        CreateDirectory(Path.GetDirectoryName(original.Path));
                        CopyFile(source.Path, replacement.Temporary);
                        if (Hash(replacement.Temporary) != source.Hash) throw new IOException("A backup file changed while it was being staged: " + source.Path);
                    }
                }
                CheckSnapshot(desired);
                RequireClosed(); CheckUnchanged(current, expectedGamHash, expectedUserHash); CheckSnapshot(before);
                Apply(current, replacements, recovery);
                return recovery;
            }
            finally { foreach (Replacement replacement in replacements) TryDeleteFile(replacement.Temporary); }
        }

        private static void Apply(SaveDocument current, List<Replacement> replacements, string recovery)
        {
            try
            {
                foreach (Replacement replacement in replacements)
                {
                    CheckSnapshot(new List<SlotFile> { replacement.Before });
                    if (replacement.ExpectedHash == null) DeleteFile(replacement.Before.Path);
                    else if (replacement.Before.Hash == null) MoveFile(replacement.Temporary, replacement.Before.Path);
                    else ReplaceFile(replacement.Temporary, replacement.Before.Path);
                    replacement.Changed = true;
                }
                ReadPair(current.FilePath, current.UserFilePath);
                foreach (Replacement replacement in replacements)
                {
                    string actual = FileExists(replacement.Before.Path) ? Hash(replacement.Before.Path) : null;
                    if (actual != replacement.ExpectedHash) throw new IOException("A restored slot file failed its content check: " + replacement.Before.Path);
                }
            }
            catch (Exception original)
            {
                var failures = new List<string>();
                for (int i = replacements.Count - 1; i >= 0; i--)
                {
                    Replacement replacement = replacements[i];
                    if (!replacement.Changed) continue;
                    try
                    {
                        if (replacement.Before.Hash == null) DeleteFile(replacement.Before.Path);
                        else
                        {
                            string source = Path.Combine(recovery, replacement.Before.RelativePath);
                            if (Hash(source) != replacement.Before.Hash) throw new IOException("The recovery file failed its content check.");
                            AtomicCopy(source, replacement.Before.Path);
                        }
                    }
                    catch (Exception rollback) { failures.Add(replacement.Before.Path + ": " + rollback.Message); }
                }
                if (failures.Count > 0)
                    throw new IOException("The operation could not complete. The original complete slot is backed up at " + recovery + ". Recovery errors: " + String.Join("; ", failures), original);
                throw new IOException("The operation could not complete; changes were rolled back. Complete original slot backup: " + recovery + ". " + original.Message, original);
            }
        }

        private static void AtomicCopy(string source, string destination)
        {
            string temporary = destination + ".SealSaveEditor-" + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                CopyFile(source, temporary);
                if (FileExists(destination)) ReplaceFile(temporary, destination);
                else MoveFile(temporary, destination);
            }
            finally { TryDeleteFile(temporary); }
        }

        private static void TryDeleteFile(string path)
        {
            if (String.IsNullOrEmpty(path)) return;
            try { if (FileExists(path)) DeleteFile(path); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }

        private static void RemoveStaging(string parent, string staging)
        {
            string prefix = Path.GetFullPath(parent).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            string resolved = Path.GetFullPath(staging);
            if (!resolved.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) || !Path.GetFileName(resolved).StartsWith(".SealSaveEditor-", StringComparison.Ordinal))
                throw new IOException("Temporary export folder is outside its destination.");
            // Cleanup failure must not misreport an already committed export.
            try { if (DirectoryExists(resolved)) Directory.Delete(IoPath(resolved), true); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }
}
