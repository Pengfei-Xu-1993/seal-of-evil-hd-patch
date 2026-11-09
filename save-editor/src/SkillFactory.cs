using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace SealSaveEditor
{
    // Exact native serialized base payloads. No existing save node is a donor.
    public sealed class SkillComponentPlan
    {
        public string Alias { get; internal set; }
        public byte[] BasePayload { get; internal set; }
    }

    public sealed class SkillNodePlan
    {
        public string Alias { get; internal set; }
        public string MainSkillAlias { get; internal set; }
        public bool IsAssociated { get { return !String.IsNullOrEmpty(MainSkillAlias); } }
        public int Level { get; internal set; }
        public int NativeMaxLevel { get; internal set; }
        public byte[] BasePayload { get; internal set; }
        public List<SkillComponentPlan> Components { get; internal set; }
    }

    public sealed class SkillTreePlan
    {
        public int ClassId { get; internal set; }
        public bool Maximum { get; internal set; }
        public List<SkillNodePlan> Skills { get; internal set; }
        public string Summary { get; internal set; }
    }

    public static class SkillFactory
    {
        public static SkillTreePlan Create(GameCatalog catalog, ScriptItemCatalog books, CharacterRecord character, bool maximum)
        {
            if (catalog == null || books == null || character == null) throw new ArgumentNullException("catalog/books/character");
            int[] attributes = new int[256];
            for (int i = 0; i < attributes.Length; i++)
            {
                SaveField field = character.Fields.FirstOrDefault(f => f.Key == "attr." + i.ToString(CultureInfo.InvariantCulture));
                if (field == null) throw new InvalidDataException("The selected character has an incomplete attribute snapshot.");
                attributes[i] = checked((int)field.Value);
            }
            return Create(catalog, books, attributes, maximum);
        }

        public static SkillTreePlan Create(GameCatalog catalog, ScriptItemCatalog books, int[] actorAttributes, bool maximum)
        {
            if (catalog == null || books == null || actorAttributes == null) throw new ArgumentNullException("catalog/books/actorAttributes");
            if (actorAttributes.Length != 256) throw new ArgumentException("A complete 256-integer actor snapshot is required.", "actorAttributes");
            if (!String.Equals(Path.GetFullPath(catalog.GameDirectory).TrimEnd('\\'), Path.GetFullPath(books.GameDirectory).TrimEnd('\\'), StringComparison.OrdinalIgnoreCase))
                throw new ArgumentException("The skill and book catalogs must use the same installed game.");
            int[] snapshot = (int[])actorAttributes.Clone();
            int classId = snapshot[1];
            if (classId < 1 || classId > 5) throw new InvalidOperationException("Only the five original playable classes have a verified complete skill plan.");
            for (int i = 19; i <= 23; i++)
                if (snapshot[i] < 0 || snapshot[i] > 1000000) throw new InvalidOperationException("The selected character's element values are outside the verified native calculation range (0..1000000).");
            var bookRows = books.Books.Where(b => b.ClassId == classId).ToList();
            var aliases = new HashSet<string>(bookRows.SelectMany(b => b.SkillAliases), StringComparer.Ordinal);
            var roots = catalog.Skills.Where(s => aliases.Contains(s.Alias)).ToList();
            if (roots.Count != aliases.Count || roots.Any(s => s.MaxLevel < 1 || s.MaxLevel > 9))
                throw new InvalidDataException("The native book/skill root catalog is incomplete or has an unsupported maximum.");
            int[] expectedCounts = { 0, 11, 16, 11, 11, 12 };
            if (roots.Count != expectedCounts[classId]) throw new InvalidDataException("The installed class skill catalog does not match the verified original build.");
            var mainByAlias = roots.ToDictionary(s => s.Alias, s => "", StringComparer.Ordinal);
            foreach (SkillAssociationDefinition row in catalog.SkillAssociations.Where(a => aliases.Contains(a.MainSkillAlias)))
            {
                if (mainByAlias.ContainsKey(row.TargetSkillAlias)) throw new InvalidDataException("Duplicate associated skill definition.");
                mainByAlias.Add(row.TargetSkillAlias, row.MainSkillAlias);
            }
            var enhancements = bookRows.SelectMany(b => b.EnhancementAliases).Distinct(StringComparer.Ordinal).OrderBy(a => a, StringComparer.Ordinal).ToList();
            var plan = new SkillTreePlan { ClassId = classId, Maximum = maximum, Skills = new List<SkillNodePlan>() };
            ActorWorkerClient.Run(catalog.GameDirectory, 1, delegate(BinaryWriter input)
            {
                input.Write(classId); input.Write(maximum); input.Write(256);
                foreach (int value in snapshot) input.Write(value);
                input.Write(roots.Count);
                foreach (SkillDefinition root in roots) { input.Write(root.Alias); input.Write(root.MaxLevel); }
                input.Write(enhancements.Count); foreach (string alias in enhancements) input.Write(alias);
                input.Write(mainByAlias.Count);
                foreach (var row in mainByAlias.OrderBy(p => p.Key, StringComparer.Ordinal)) { input.Write(row.Key); input.Write(row.Value); }
            }, delegate(BinaryReader output)
            {
                if (output.ReadInt32() != classId || output.ReadBoolean() != maximum) throw new InvalidDataException("Native skill response context mismatch.");
                int count = output.ReadInt32();
                if (count != mainByAlias.Count) throw new InvalidDataException("The native skill tree is incomplete.");
                var seen = new HashSet<string>(StringComparer.Ordinal);
                for (int i = 0; i < count; i++)
                {
                    var node = new SkillNodePlan { Alias = output.ReadString(), MainSkillAlias = output.ReadString(), Level = output.ReadInt32(), NativeMaxLevel = output.ReadInt32(), Components = new List<SkillComponentPlan>() };
                    string parent;
                    if (!mainByAlias.TryGetValue(node.Alias, out parent) || parent != node.MainSkillAlias || !seen.Add(node.Alias))
                        throw new InvalidDataException("Unexpected or duplicate native skill identity.");
                    SkillDefinition root = roots.Single(r => r.Alias == (parent.Length == 0 ? node.Alias : parent));
                    if (node.Level != (maximum ? root.MaxLevel : 1) || node.NativeMaxLevel != (parent.Length == 0 ? root.MaxLevel : 9))
                        throw new InvalidDataException("Native main/associated skill levels do not match the requested plan.");
                    node.BasePayload = ActorWorkerClient.ReadPayload(output);
                    int components = output.ReadInt32();
                    if (components < 0 || components > 256) throw new InvalidDataException("Invalid native enhancement count.");
                    var componentAliases = new HashSet<string>(StringComparer.Ordinal);
                    for (int c = 0; c < components; c++)
                    {
                        string alias = output.ReadString();
                        if (!componentAliases.Add(alias)) throw new InvalidDataException("Duplicate native enhancement identity.");
                        node.Components.Add(new SkillComponentPlan { Alias = alias, BasePayload = ActorWorkerClient.ReadPayload(output) });
                    }
                    plan.Skills.Add(node);
                }
            });
            plan.Summary = roots.Count.ToString(CultureInfo.InvariantCulture) + " class skills, "
                + (plan.Skills.Count - roots.Count).ToString(CultureInfo.InvariantCulture) + " associated forms, "
                + plan.Skills.Sum(s => s.Components.Count).ToString(CultureInfo.InvariantCulture) + " enhancement records; "
                + (maximum ? "each main skill at its own native maximum" : "new main skills begin at level 1")
                + ". Existing save content must be merged by identity. Element activation is calculated from the selected character's snapshot.";
            return plan;
        }
    }

    // Shared transport for skill and companion plans. The worker is an isolated
    // x86 process with copied catalog inputs and deliberately no System folder.
    internal static class ActorWorkerClient
    {
        internal static byte[] ReadPayload(BinaryReader input)
        {
            int size = input.ReadInt32();
            if (size < 32 || size >= 0x4000) throw new InvalidDataException("Invalid native object payload size.");
            byte[] bytes = input.ReadBytes(size); if (bytes.Length != size) throw new EndOfStreamException(); return bytes;
        }

        internal static void Run(string gameDirectory, byte mode, Action<BinaryWriter> writeBody, Action<BinaryReader> readBody)
        {
            string game = Path.GetFullPath(gameDirectory), engine = Path.Combine(game, "ERRelive.dll");
            using (var hash = SHA256.Create()) using (var source = File.OpenRead(engine))
                if (BitConverter.ToString(hash.ComputeHash(source)).Replace("-", "") != ScriptItemCatalog.EngineSha256)
                    throw new InvalidDataException("This engine build has not been verified for native actor generation.");
            string worker = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "SealActorWorker.exe");
            if (!File.Exists(worker)) throw new FileNotFoundException("The native actor worker must be beside the editor executable.", worker);
            string scratch = Path.Combine(Path.GetTempPath(), "seal-actor-worker-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(scratch);
            try
            {
                File.WriteAllText(Path.Combine(scratch, ".seal-actor-worker"), "Owned native actor worker scratch.");
                Directory.CreateDirectory(Path.Combine(scratch, "Data")); Directory.CreateDirectory(Path.Combine(scratch, "Dlg"));
                foreach (string file in Directory.GetFiles(Path.Combine(game, "Data"), "*.txt")) File.Copy(file, Path.Combine(scratch, "Data", Path.GetFileName(file)));
                File.Copy(Path.Combine(game, "Dlg", "Name.dlg"), Path.Combine(scratch, "Dlg", "Name.dlg"));
                string request = Path.Combine(scratch, "request.bin");
                using (var input = new BinaryWriter(File.Create(request), Encoding.UTF8))
                { input.Write(0x51415753); input.Write(1); input.Write(mode); input.Write(engine); writeBody(input); }
                var start = new ProcessStartInfo(worker, "\"" + request + "\"") { WorkingDirectory = scratch, UseShellExecute = false, CreateNoWindow = true, RedirectStandardError = true, RedirectStandardOutput = true };
                string error;
                using (Process process = Process.Start(start))
                {
                    if (!process.WaitForExit(30000)) { process.Kill(); process.WaitForExit(); throw new TimeoutException("The native actor worker did not finish within 30 seconds."); }
                    error = process.StandardError.ReadToEnd();
                    if (process.ExitCode != 0) throw new InvalidOperationException("Native actor generation failed: " + (String.IsNullOrWhiteSpace(error) ? "worker exit " + process.ExitCode : error.Trim()));
                }
                using (var output = new BinaryReader(File.OpenRead(Path.Combine(scratch, "response.bin")), Encoding.UTF8))
                {
                    if (output.ReadInt32() != 0x52415753 || output.ReadInt32() != 1 || output.ReadByte() != mode) throw new InvalidDataException("Unrecognized native actor worker response.");
                    readBody(output);
                    if (output.BaseStream.Position != output.BaseStream.Length) throw new InvalidDataException("Unexpected native actor response trailer.");
                }
            }
            finally
            {
                try
                {
                    // Only remove the exact directory we just created and marked.
                    string resolved = Path.GetFullPath(scratch), temp = Path.GetFullPath(Path.GetTempPath()).TrimEnd('\\') + "\\";
                    if (resolved.StartsWith(temp, StringComparison.OrdinalIgnoreCase) && Path.GetFileName(resolved).StartsWith("seal-actor-worker-", StringComparison.Ordinal)
                        && File.Exists(Path.Combine(resolved, ".seal-actor-worker"))) Directory.Delete(resolved, true);
                }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }
            }
        }
    }
}
