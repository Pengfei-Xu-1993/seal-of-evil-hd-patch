using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace SealSaveEditor
{
    /// <summary>Runs the installed original game's item factory in a separate, short-lived x86 worker.</summary>
    public static class ItemFactory
    {
        private const string EngineSha256 = "B22AD4D0630D39B4E376EEF3EFDDFE7EE363823AE0CB15AE41DE6BEFB2E8126F";
        public const int MinimumGenerationLevel = 1;
        public const int MaximumGenerationLevel = 200;

        public static ItemInitialState Create(ItemTemplateCatalog catalog, ItemTemplate template, int generationLevel, int seed)
        {
            if (catalog == null) throw new ArgumentNullException("catalog");
            if (template == null || !Object.ReferenceEquals(template, catalog.Find(template.Alias)))
                throw new ArgumentException("Select an original item template from this catalog.", "template");
            if (generationLevel < MinimumGenerationLevel || generationLevel > MaximumGenerationLevel)
                throw new ArgumentOutOfRangeException("generationLevel", "The original item progression table covers levels 1..200.");
            if (template.TypeId >= 45 && template.TypeId <= 49)
                throw new InvalidOperationException("This is a class-book base shell. Select a bound learning book, upgrade book or class manual from the skill-book catalog.");
            return CreateNative(catalog.GameDirectory, template.Alias, template.Alias, template.TypeId, null, generationLevel, seed);
        }

        internal static ItemInitialState CreateScript(string gameDirectory, ScriptBookDefinition definition, int seed)
        {
            return CreateNative(gameDirectory, definition.Path, definition.Alias, definition.TypeId, definition.Path, 1, seed);
        }

        private static ItemInitialState CreateNative(string gameDirectory, string requestIdentity, string expectedAlias, int expectedType, string expectedPath, int generationLevel, int seed)
        {
            string worker = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "SealItemWorker.exe");
            if (!File.Exists(worker)) throw new FileNotFoundException("The native item worker is missing. Reinstall the complete editor package.", worker);
            string enginePath = Path.Combine(gameDirectory, "ERRelive.dll");
            using (var algorithm = SHA256.Create()) using (var file = File.OpenRead(enginePath))
                if (!String.Equals(BitConverter.ToString(algorithm.ComputeHash(file)).Replace("-", ""), EngineSha256, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException("This installed engine build has not been verified for native item generation.");

            string parent = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "SealSaveEditorNative"));
            string scratch = Path.Combine(parent, Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(scratch);
            try
            {
                File.WriteAllText(Path.Combine(scratch, ".seal-item-worker"), "Seal Save Editor native generation workspace", Encoding.UTF8);
                string data = Path.Combine(scratch, "Data"), dialog = Path.Combine(scratch, "Dlg");
                Directory.CreateDirectory(data); Directory.CreateDirectory(dialog);
                // Copies, not links: the worker can never write through to original data or saves.
                foreach (string source in Directory.GetFiles(Path.Combine(gameDirectory, "Data"), "*.txt"))
                    File.Copy(source, Path.Combine(data, Path.GetFileName(source)), false);
                File.Copy(Path.Combine(gameDirectory, "Dlg", "Name.dlg"), Path.Combine(dialog, "Name.dlg"), false);
                string request = Path.Combine(scratch, "request.bin");
                using (var writer = new BinaryWriter(File.Create(request), Encoding.UTF8))
                {
                    writer.Write(0x51454953u); writer.Write(1); writer.Write(enginePath); writer.Write(1);
                    writer.Write(requestIdentity); writer.Write(generationLevel); writer.Write(seed);
                }
                var errors = new StringBuilder();
                var start = new ProcessStartInfo(worker, "\"" + request + "\"") {
                    UseShellExecute = false, CreateNoWindow = true, WorkingDirectory = scratch,
                    RedirectStandardError = true, RedirectStandardOutput = true };
                using (Process process = new Process { StartInfo = start })
                {
                    DataReceivedEventHandler collect = delegate(object sender, DataReceivedEventArgs e) {
                        if (e.Data == null) return;
                        lock (errors) { if (errors.Length < 8192) errors.AppendLine(e.Data.Substring(0, Math.Min(2048, e.Data.Length))); }
                    };
                    process.ErrorDataReceived += collect; process.OutputDataReceived += collect;
                    process.Start(); process.BeginErrorReadLine(); process.BeginOutputReadLine();
                    if (!process.WaitForExit(15000))
                    {
                        try { process.Kill(); process.WaitForExit(2000); } catch (InvalidOperationException) { }
                        throw new InvalidOperationException("The original item factory timed out. The save has not been changed.");
                    }
                    process.WaitForExit(); // Flush asynchronous redirected output after the bounded process wait.
                    if (process.ExitCode != 0)
                        throw new InvalidOperationException("The original item factory failed. The save has not been changed. " + errors.ToString().Trim());
                }
                string response = Path.Combine(scratch, "response.bin");
                if (!File.Exists(response) || new FileInfo(response).Length > 1024 * 1024) throw new InvalidDataException("The native factory returned no valid generation plan.");
                ItemInitialState state;
                using (var reader = new BinaryReader(File.OpenRead(response), Encoding.UTF8))
                {
                    if (reader.ReadUInt32() != 0x52454953u || reader.ReadInt32() != 1 || reader.ReadInt32() != 1)
                        throw new InvalidDataException("Unrecognized native factory response.");
                    state = ReadState(reader, expectedAlias, expectedType, expectedPath, generationLevel, seed);
                    if (reader.BaseStream.Position != reader.BaseStream.Length) throw new InvalidDataException("Unexpected native factory response trailer.");
                }
                return state;
            }
            finally
            {
                // The complete path is a new GUID child of our dedicated temporary root, never a game/user path.
                string full = Path.GetFullPath(scratch);
                if (String.Equals(Path.GetDirectoryName(full), parent, StringComparison.OrdinalIgnoreCase)
                    && File.Exists(Path.Combine(full, ".seal-item-worker")))
                {
                    try { Directory.Delete(full, true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
                }
            }
        }

        private static ItemInitialState ReadState(BinaryReader reader, string expectedAlias, int expectedType, string expectedPath, int level, int seed)
        {
            var state = new ItemInitialState { Alias = reader.ReadString(), Name = reader.ReadString(), Path = reader.ReadString(),
                StringAttributes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase), Modifiers = new List<int[]>() };
            if (reader.ReadInt32() != level || reader.ReadInt32() != seed || state.Alias != expectedAlias || String.IsNullOrWhiteSpace(state.Name)
                || (expectedPath != null && state.Path != expectedPath))
                throw new InvalidDataException("The native factory response does not match this preview.");
            if (reader.ReadInt32() != 256) throw new InvalidDataException("Invalid native attribute count.");
            state.IntegerAttributes = new int[256];
            for (int i = 0; i < 256; i++) state.IntegerAttributes[i] = reader.ReadInt32();
            if (state.IntegerAttributes[7] != expectedType) throw new InvalidDataException("The native item type does not match the original template.");
            int count = reader.ReadInt32();
            if (count < 0 || count > 4096) throw new InvalidDataException("Invalid native string count.");
            for (int i = 0; i < count; i++)
            {
                string key = reader.ReadString(), value = reader.ReadString();
                if (String.IsNullOrWhiteSpace(key) || key.Length > 128 || value.Length > 4096 || state.StringAttributes.ContainsKey(key))
                    throw new InvalidDataException("Invalid native string field.");
                state.StringAttributes.Add(key, value);
            }
            state.BaseParameter = reader.ReadInt32();
            count = reader.ReadInt32();
            if (count < 0 || count > 256) throw new InvalidDataException("Invalid native modifier count.");
            for (int i = 0; i < count; i++)
            {
                int[] words = new int[13]; for (int j = 0; j < words.Length; j++) words[j] = reader.ReadInt32();
                if (words[0] != 52) throw new InvalidDataException("Invalid native modifier record.");
                for (int j = 9; j < 13; j++)
                {
                    float value = BitConverter.ToSingle(BitConverter.GetBytes(words[j]), 0);
                    if (Single.IsNaN(value) || Single.IsInfinity(value)) throw new InvalidDataException("A generated modifier is not finite.");
                }
                state.Modifiers.Add(words);
            }
            if (String.IsNullOrEmpty(state.Path)) state.Path = null; // The owning save tree supplies the item path.
            state.GenerationSummary = "Generated by the installed original game engine. Level " + level.ToString(CultureInfo.InvariantCulture)
                + ", seed " + seed.ToString(CultureInfo.InvariantCulture) + "; " + state.Modifiers.Count.ToString(CultureInfo.InvariantCulture)
                + " native modifiers, including original activation conditions. This preview is the exact plan applied by Replace.";
            if (expectedPath != null)
                state.GenerationSummary = "Created through the installed game's original registered skill-book subclass. The exact object path, bound skill use callback and native level-1 defaults are preserved. Original class and learning requirements apply; the book does not bypass them.";
            return state;
        }
    }
}
