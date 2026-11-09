using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;

namespace SealHdInstaller
{
    internal static class ManifestEditor
    {
        internal const string ModernManifest = "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>\r\n" +
            "<assembly xmlns=\"urn:schemas-microsoft-com:asm.v1\" manifestVersion=\"1.0\">" +
            "<assemblyIdentity version=\"1.0.0.0\" processorArchitecture=\"x86\" name=\"SealOfEvil.LocalCompatibility\" type=\"win32\"/>" +
            "<trustInfo xmlns=\"urn:schemas-microsoft-com:asm.v3\"><security><requestedPrivileges><requestedExecutionLevel level=\"asInvoker\" uiAccess=\"false\"/></requestedPrivileges></security></trustInfo>" +
            "<compatibility xmlns=\"urn:schemas-microsoft-com:compatibility.v1\"><application>" +
            "<supportedOS Id=\"{4a2f28e3-53b9-4441-ba9c-d69d4a4a6e38}\"/>" +
            "<supportedOS Id=\"{1f676c76-80e1-4239-95bb-83d0f6d0da78}\"/>" +
            "<supportedOS Id=\"{8e0f7a12-bfb3-4fe8-b9a5-48fd50a15a9a}\"/>" +
            "</application></compatibility><application xmlns=\"urn:schemas-microsoft-com:asm.v3\"><windowsSettings>" +
            "<dpiAware xmlns=\"http://schemas.microsoft.com/SMI/2005/WindowsSettings\">true</dpiAware>" +
            "</windowsSettings></application></assembly>";

        internal static void CreateModernCopy(string original, string output)
        {
            if (String.Equals(Path.GetFullPath(original), Path.GetFullPath(output), StringComparison.OrdinalIgnoreCase))
                throw new IOException("The compatibility manifest must be prepared in a separate file.");
            PeSnapshot sections = ReadPe(original);
            Dictionary<string, byte[]> resources = ReadResources(original);
            File.Copy(original, output, false);
            IntPtr update = BeginUpdateResource(output, false);
            if (update == IntPtr.Zero) throw NativeError("Cannot prepare the game's embedded compatibility manifest");
            try
            {
                foreach (string key in resources.Keys.Where(IsMainManifest))
                {
                    ushort lang = UInt16.Parse(key.Split('|')[2]);
                    if (!UpdateResource(update, new IntPtr(24), new IntPtr(1), lang, null, 0))
                        throw NativeError("Cannot replace the game's old embedded manifest");
                }
                byte[] manifest = new UTF8Encoding(false).GetBytes(ModernManifest);
                if (!UpdateResource(update, new IntPtr(24), new IntPtr(1), 0, manifest, (uint)manifest.Length))
                    throw NativeError("Cannot add the game's embedded compatibility manifest");
                IntPtr committing = update; update = IntPtr.Zero;
                if (!EndUpdateResource(committing, false)) throw NativeError("Cannot save the game's embedded compatibility manifest");
            }
            finally { if (update != IntPtr.Zero) EndUpdateResource(update, true); }
            VerifyPreserved(sections, resources, output);
        }

        internal static void VerifyPreserved(string original, string output)
        {
            VerifyPreserved(ReadPe(original), ReadResources(original), output);
        }
        private static void VerifyPreserved(PeSnapshot original, Dictionary<string, byte[]> before, string output)
        {
            PeSnapshot after = ReadPe(output);
            if (!original.Header.SequenceEqual(after.Header)) throw new InvalidDataException("Executable entry point or image layout changed while embedding the manifest.");
            if (!original.Sections.Keys.OrderBy(n => n).SequenceEqual(after.Sections.Keys.OrderBy(n => n)))
                throw new InvalidDataException("Executable sections changed while embedding the manifest.");
            foreach (string name in original.Sections.Keys)
                if (!original.Sections[name].SequenceEqual(after.Sections[name]))
                    throw new InvalidDataException("Executable section changed while embedding the manifest: " + name);
            var resources = ReadResources(output);
            string[] keysBefore = before.Keys.Where(k => !IsMainManifest(k)).OrderBy(k => k, StringComparer.Ordinal).ToArray();
            string[] keysAfter = resources.Keys.Where(k => !IsMainManifest(k)).OrderBy(k => k, StringComparer.Ordinal).ToArray();
            if (!keysBefore.SequenceEqual(keysAfter)) throw new InvalidDataException("The executable's other resources changed while embedding the manifest.");
            foreach (string key in keysBefore)
                if (!before[key].SequenceEqual(resources[key])) throw new InvalidDataException("An executable resource changed while embedding the manifest: " + key);
            byte[] expected = new UTF8Encoding(false).GetBytes(ModernManifest);
            string[] manifests = resources.Keys.Where(IsMainManifest).ToArray();
            if (manifests.Length != 1 || !resources[manifests[0]].SequenceEqual(expected))
                throw new InvalidDataException("The embedded compatibility manifest could not be verified.");
        }

        private static bool IsMainManifest(string key) { return key.StartsWith("i:24|i:1|", StringComparison.Ordinal); }
        private sealed class PeSnapshot
        {
            internal byte[] Header;
            internal Dictionary<string, byte[]> Sections = new Dictionary<string, byte[]>();
        }
        private static PeSnapshot ReadPe(string path)
        {
            byte[] data = File.ReadAllBytes(path);
            if (data.Length < 256 || data[0] != 'M' || data[1] != 'Z') throw new InvalidDataException("The game executable is not a valid PE image.");
            int pe = BitConverter.ToInt32(data, 60);
            if (pe < 64 || pe > data.Length - 24 || BitConverter.ToInt32(data, pe) != 0x4550) throw new InvalidDataException("The game executable's PE header is invalid.");
            int count = BitConverter.ToUInt16(data, pe + 6), optionalLength = BitConverter.ToUInt16(data, pe + 20), optional = pe + 24;
            if (optionalLength < 96 || optional > data.Length - optionalLength || BitConverter.ToUInt16(data, optional) != 0x10b)
                throw new InvalidDataException("The game executable must be a supported 32-bit PE image.");
            var result = new PeSnapshot();
            // Machine, entry point, image base, section/file alignment stay unchanged.
            result.Header = data.Skip(pe + 4).Take(2).Concat(data.Skip(optional + 16).Take(4))
                .Concat(data.Skip(optional + 28).Take(12)).ToArray();
            int table = optional + optionalLength;
            if (count < 1 || count > 96 || table > data.Length - count * 40) throw new InvalidDataException("The executable's section table is invalid.");
            for (int i = 0; i < count; ++i)
            {
                int entry = table + i * 40;
                string name = Encoding.ASCII.GetString(data, entry, 8).TrimEnd('\0');
                if (name == ".rsrc") continue;
                uint length = BitConverter.ToUInt32(data, entry + 16), offset = BitConverter.ToUInt32(data, entry + 20);
                if (length > data.Length || offset > data.Length - length || result.Sections.ContainsKey(name)) throw new InvalidDataException("The executable has an invalid protected section.");
                // Code/data must retain their layout. Other sections such as .reloc may
                // move when Win32 grows .rsrc, but their actual contents must not change.
                byte[] contents = data.Skip((int)offset).Take((int)length).ToArray();
                result.Sections.Add(name, name == ".text" || name == ".rdata" || name == ".data"
                    ? data.Skip(entry + 8).Take(12).Concat(data.Skip(entry + 36).Take(4)).Concat(contents).ToArray()
                    : contents);
            }
            if (!new[] { ".text", ".rdata", ".data" }.All(result.Sections.ContainsKey)) throw new InvalidDataException("The executable's expected code and data sections are missing.");
            return result;
        }

        internal static Dictionary<string, byte[]> ReadResources(string path)
        {
            // DATAFILE | IMAGE_RESOURCE never executes the vendor executable or its DLLs.
            IntPtr module = LoadLibraryEx(path, IntPtr.Zero, 0x22);
            if (module == IntPtr.Zero) throw NativeError("Cannot inspect the game's executable resources");
            var result = new Dictionary<string, byte[]>(StringComparer.Ordinal);
            Exception failure = null;
            try
            {
                EnumType callback = delegate(IntPtr m, IntPtr type, IntPtr ignored)
                {
                    EnumName names = delegate(IntPtr mm, IntPtr t, IntPtr name, IntPtr ignored2)
                    {
                        EnumLanguage languages = delegate(IntPtr mmm, IntPtr tt, IntPtr nn, ushort language, IntPtr ignored3)
                        {
                            try
                            {
                                IntPtr resource = FindResourceEx(mmm, tt, nn, language);
                                if (resource == IntPtr.Zero) throw NativeError("Cannot find an executable resource");
                                uint length = SizeofResource(mmm, resource);
                                if (length > 64 * 1024 * 1024) throw new InvalidDataException("An executable resource is too large.");
                                IntPtr loaded = LoadResource(mmm, resource), data = LockResource(loaded);
                                if (data == IntPtr.Zero && length != 0) throw NativeError("Cannot read an executable resource");
                                byte[] bytes = new byte[length]; if (length > 0) Marshal.Copy(data, bytes, 0, (int)length);
                                result.Add(ResourceName(tt) + "|" + ResourceName(nn) + "|" + language, bytes);
                                return true;
                            }
                            catch (Exception e) { failure = e; return false; }
                        };
                        bool ok = EnumResourceLanguages(mm, t, name, languages, IntPtr.Zero);
                        GC.KeepAlive(languages);
                        if (!ok && failure == null) failure = NativeError("Cannot enumerate resource languages");
                        return failure == null;
                    };
                    bool namesOk = EnumResourceNames(m, type, names, IntPtr.Zero);
                    GC.KeepAlive(names);
                    if (!namesOk && failure == null) failure = NativeError("Cannot enumerate executable resources");
                    return failure == null;
                };
                bool typesOk = EnumResourceTypes(module, callback, IntPtr.Zero);
                GC.KeepAlive(callback);
                int error = Marshal.GetLastWin32Error();
                if (!typesOk && failure == null && error != 1813) failure = new Win32Exception(error, "Cannot enumerate executable resource types");
                if (failure != null) throw failure;
                return result;
            }
            finally { FreeLibrary(module); }
        }
        private static string ResourceName(IntPtr value)
        {
            long number = value.ToInt64();
            return number >= 0 && number <= 65535 ? "i:" + number : "s:" + Convert.ToBase64String(Encoding.Unicode.GetBytes(Marshal.PtrToStringUni(value)));
        }
        private static Exception NativeError(string message) { return new Win32Exception(Marshal.GetLastWin32Error(), message); }
        private delegate bool EnumType(IntPtr module, IntPtr type, IntPtr parameter);
        private delegate bool EnumName(IntPtr module, IntPtr type, IntPtr name, IntPtr parameter);
        private delegate bool EnumLanguage(IntPtr module, IntPtr type, IntPtr name, ushort language, IntPtr parameter);
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)] private static extern IntPtr LoadLibraryEx(string file, IntPtr reserved, uint flags);
        [DllImport("kernel32.dll", SetLastError = true)] private static extern bool FreeLibrary(IntPtr module);
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)] private static extern bool EnumResourceTypes(IntPtr module, EnumType callback, IntPtr parameter);
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)] private static extern bool EnumResourceNames(IntPtr module, IntPtr type, EnumName callback, IntPtr parameter);
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)] private static extern bool EnumResourceLanguages(IntPtr module, IntPtr type, IntPtr name, EnumLanguage callback, IntPtr parameter);
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)] private static extern IntPtr FindResourceEx(IntPtr module, IntPtr type, IntPtr name, ushort language);
        [DllImport("kernel32.dll", SetLastError = true)] private static extern uint SizeofResource(IntPtr module, IntPtr resource);
        [DllImport("kernel32.dll", SetLastError = true)] private static extern IntPtr LoadResource(IntPtr module, IntPtr resource);
        [DllImport("kernel32.dll")] private static extern IntPtr LockResource(IntPtr resource);
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)] private static extern IntPtr BeginUpdateResource(string file, bool deleteExistingResources);
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)] private static extern bool UpdateResource(IntPtr update, IntPtr type, IntPtr name, ushort language, byte[] data, uint length);
        [DllImport("kernel32.dll", SetLastError = true)] private static extern bool EndUpdateResource(IntPtr update, bool discard);
    }
}
