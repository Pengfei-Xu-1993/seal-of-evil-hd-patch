using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Win32.SafeHandles;

namespace SealHdInstaller
{
    internal static class InstallerFiles
    {
        internal static readonly string[] PayloadPaths = {
            "dinput8.dll", "D3D8.dll", "DDraw.dll", "D3DImm.dll", "dgVoodoo.conf", "SealHD.ini",
            "SealHD-assets/intro-zoom.png", "SealHD-assets/intro-object.png", "SealHD-assets/intro-strategy.png",
            "SealHD-assets/mainmenu.png", "SealHD-assets/opening-title.png", "SealHD-bin/SealVideoProbe.exe"
        };
        internal static readonly string[] ManagedPaths = PayloadPaths.Concat(new[] { "SOE.EXE", "Ini/Config.ini" }).ToArray();
        internal const string StateName = "SealHD-installer-state.json", ReportName = "SealHD-install-report.txt", BackupName = "SealHD-installer-backups";

        internal static string Hash(string file)
        {
            using (var stream = File.Open(file, FileMode.Open, FileAccess.Read, FileShare.Read))
            using (var hash = SHA256.Create()) return Hex(hash.ComputeHash(stream));
        }
        internal static string HashBytes(byte[] data) { using (var hash = SHA256.Create()) return Hex(hash.ComputeHash(data)); }
        private static string Hex(byte[] data) { return BitConverter.ToString(data).Replace("-", ""); }
        internal static bool IsHash(string value) { return value != null && System.Text.RegularExpressions.Regex.IsMatch(value, "^[0-9A-Fa-f]{64}$"); }

        internal static string Canonical(string path)
        {
            string absolute = Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            if (!File.Exists(absolute) && !Directory.Exists(absolute)) return absolute;
            using (SafeFileHandle handle = CreateFile(absolute, 0, 7, IntPtr.Zero, 3, 0x02000000, IntPtr.Zero))
            {
                if (handle.IsInvalid) throw new Win32Exception(Marshal.GetLastWin32Error(), "Cannot resolve the selected game path.");
                var buffer = new StringBuilder(32768);
                uint length = GetFinalPathNameByHandle(handle, buffer, (uint)buffer.Capacity, 0);
                if (length == 0 || length >= buffer.Capacity) throw new Win32Exception(Marshal.GetLastWin32Error(), "Cannot resolve the selected game path.");
                string result = buffer.ToString();
                if (result.StartsWith(@"\\?\UNC\", StringComparison.OrdinalIgnoreCase)) result = @"\\" + result.Substring(8);
                else if (result.StartsWith(@"\\?\", StringComparison.Ordinal)) result = result.Substring(4);
                return result.TrimEnd(Path.DirectorySeparatorChar);
            }
        }

        internal static string Target(string root, string relative)
        {
            if (String.IsNullOrWhiteSpace(relative) || Path.IsPathRooted(relative) || relative.Contains(":") || relative.Contains("\\") || relative.Split('/').Any(p => p.Length == 0 || p == "." || p == ".."))
                throw new InvalidDataException("An installation path is invalid: " + relative);
            string basis = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            string target = Path.GetFullPath(Path.Combine(basis, relative.Replace('/', Path.DirectorySeparatorChar)));
            if (!target.StartsWith(basis, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("An installation path leaves the selected folder.");
            string current = basis.TrimEnd(Path.DirectorySeparatorChar);
            if ((Directory.Exists(current) || File.Exists(current)) && (File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                throw new IOException("A linked installation folder is not supported: " + current);
            foreach (string part in relative.Split('/'))
            {
                current = Path.Combine(current, part);
                if ((Directory.Exists(current) || File.Exists(current)) && (File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                    throw new IOException("A linked installation file or folder is not supported: " + current);
            }
            return target;
        }
        internal static bool EqualHash(string a, string b) { return String.Equals(a, b, StringComparison.OrdinalIgnoreCase); }
        internal static void RequireHash(string path, string expected)
        {
            if (expected == null)
            {
                if (File.Exists(path) || Directory.Exists(path)) throw new IOException("A file was added after inspection: " + path);
            }
            else if (!File.Exists(path) || !EqualHash(Hash(path), expected)) throw new IOException("A file changed after inspection: " + path);
        }
        internal static void Replace(string root, string relative, string source, string expectedCurrent)
        {
            string target = Target(root, relative);
            RequireHash(target, expectedCurrent);
            Directory.CreateDirectory(Path.GetDirectoryName(target));
            string sibling = Path.Combine(Path.GetDirectoryName(target), ".SealHD-" + Guid.NewGuid().ToString("N") + ".tmp");
            try
            {
                File.Copy(source, sibling, false);
                if (!EqualHash(Hash(source), Hash(sibling))) throw new IOException("A staged installation file could not be verified.");
                // Replacing the directory entry also avoids modifying other hard links to an existing file.
                RequireHash(Target(root, relative), expectedCurrent);
                if (expectedCurrent == null) File.Move(sibling, target);
                else File.Replace(sibling, target, null, true);
            }
            finally { if (File.Exists(sibling)) File.Delete(sibling); }
        }
        internal static void Remove(string root, string relative, string expectedCurrent)
        {
            string target = Target(root, relative);
            RequireHash(target, expectedCurrent);
            if (File.Exists(target)) File.Delete(target);
        }

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)] private static extern SafeFileHandle CreateFile(string name, uint access, uint share, IntPtr security, uint creation, uint flags, IntPtr template);
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)] private static extern uint GetFinalPathNameByHandle(SafeFileHandle file, StringBuilder name, uint size, uint flags);
    }

    internal sealed class OwnedTemporaryDirectory : IDisposable
    {
        internal string Path { get; private set; }
        private readonly string parent, marker;
        internal OwnedTemporaryDirectory()
        {
            parent = System.IO.Path.GetFullPath(System.IO.Path.Combine(System.IO.Path.GetTempPath(), "SealHDInstaller"));
            Directory.CreateDirectory(parent);
            if ((File.GetAttributes(parent) & FileAttributes.ReparsePoint) != 0) throw new IOException("The installer temporary folder is a link.");
            Path = System.IO.Path.Combine(parent, Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path);
            marker = Guid.NewGuid().ToString("N"); File.WriteAllText(System.IO.Path.Combine(Path, ".owner"), marker);
        }
        public void Dispose()
        {
            try
            {
                string resolved = InstallerFiles.Canonical(Path), resolvedParent = InstallerFiles.Canonical(parent) + System.IO.Path.DirectorySeparatorChar;
                Guid leaf;
                if (!resolved.StartsWith(resolvedParent, StringComparison.OrdinalIgnoreCase) ||
                    !Guid.TryParseExact(System.IO.Path.GetFileName(resolved), "N", out leaf) ||
                    (File.GetAttributes(resolved) & FileAttributes.ReparsePoint) != 0 ||
                    File.ReadAllText(System.IO.Path.Combine(resolved, ".owner")) != marker) return;
                if (Directory.EnumerateFileSystemEntries(resolved, "*", SearchOption.AllDirectories).Any(p => (File.GetAttributes(p) & FileAttributes.ReparsePoint) != 0)) return;
                Directory.Delete(resolved, true);
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }
}
