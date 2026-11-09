using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace SealSaveEditor
{
    public sealed class ScriptBookDefinition
    {
        public string Path { get; internal set; }
        public string Alias { get; internal set; }
        public string Name { get; internal set; }
        public string ChineseName { get; internal set; }
        public int ClassId { get; internal set; }
        public int TypeId { get { return ClassId + 44; } }
        public string ClassName { get; internal set; }
        public string Category { get; internal set; }
        public string BookKind { get; internal set; }
        public List<string> SkillAliases { get; internal set; }
        public List<string> EnhancementAliases { get; internal set; }
        public bool IsManual { get { return BookKind == "ClassManual"; } }
        internal int AllocatorRva, CreateRva, UseRva, VtableRva;
        internal Dictionary<string, string> EnglishSkills;

        public string GetDisplayName(bool chinese) { return chinese ? ChineseName : Name; }

        public string Describe(bool chinese)
        {
            var text = new StringBuilder();
            text.AppendLine(GetDisplayName(chinese));
            text.AppendLine(chinese ? "原版内置技能书；通过完整原生对象路径生成。" : "Original built-in skill book, created through its complete native object path.");
            text.AppendLine((chinese ? "适用职业：" : "Class: ") + (chinese ? ClassName : ClassEnglish(ClassId)));
            if (Category == "打造") text.AppendLine(chinese ? "分类：打造技能（仅巫师职业）" : "Category: crafting skills (Wizard class only)");
            text.AppendLine((chinese ? "种类：" : "Kind: ") + KindLabel(BookKind, chinese));
            text.AppendLine();
            if (IsManual)
                text.AppendLine(chinese ? "尝试学会下列尚未掌握的根技能，初始等级为1，并加入原宝典指定的增强分支。已学技能不会因此直接升至满级。" : "Attempts to learn the listed unknown root skills at level 1 and add the enhancement branches specified by this manual. It does not max already learned skills.");
            else if (BookKind == "Upgrade" || BookKind == "LearnTier2" || BookKind == "LearnTier3")
                text.AppendLine(chinese ? "将已学会的对应根技能提升1级；需要未达到技能上限，且角色等级满足原技能的升级要求。" : "Raises the corresponding existing root skill by one level, subject to its native maximum and character-level requirement.");
            else
                text.AppendLine(chinese ? "尝试以1级学会对应根技能，并加入此书指定的增强分支；已学会时按原版条件处理。" : "Attempts to learn the corresponding root skill at level 1 and add this book's specified enhancement branches, subject to the original already-known checks.");
            if (BookKind == "LearnTier2" || BookKind == "LearnTier3")
                text.AppendLine(chinese ? "升级成功后，还会加入此书指定阶段的增强分支；不是任意设置技能阶段。" : "A successful increase also adds this book's specified stage enhancement branches; it does not assign an arbitrary skill stage.");
            text.AppendLine(chinese ? "分支的实际生效仍受原版职业、五行及技能条件约束；不保证正常游戏中可直接掉落此书。" : "Actual branch activation still depends on original class, element and skill conditions. This catalog does not claim that every book is available as ordinary loot.");
            if (IsManual && ClassId == 2)
                text.AppendLine(chinese ? "巫师宝典不包含朱雀或四种打造技能；它们有独立技能书。" : "The Wizard manual excludes Red Bird and the four crafting skills, which have separate books.");
            text.AppendLine(); text.AppendLine(chinese ? "对应根技能：" : "Affected root skills:");
            foreach (string alias in SkillAliases)
            {
                string name;
                if (chinese) name = alias.IndexOf('_') >= 0 ? alias.Substring(alias.IndexOf('_') + 1) : alias;
                else if (EnglishSkills == null || !EnglishSkills.TryGetValue(alias, out name)) name = alias;
                text.AppendLine("  " + name);
            }
            text.AppendLine((chinese ? "原回调指定增强数：" : "Enhancements specified by native callback: ") + EnhancementAliases.Count.ToString(CultureInfo.InvariantCulture));
            text.AppendLine(); text.AppendLine(chinese ? "原始对象信息（用于保留真实使用行为）：" : "Original object identity (preserves its real use behavior):");
            text.AppendLine("Path: " + Path); text.AppendLine("Alias: " + Alias); text.AppendLine("Native English name: " + Name);
            return text.ToString();
        }

        public static string KindLabel(string kind, bool chinese)
        {
            switch (kind)
            {
                case "ClassManual": return chinese ? "职业宝典" : "Class manual";
                case "Learn": return chinese ? "学习书" : "Learning book";
                case "Upgrade": return chinese ? "升级书" : "Upgrade book";
                case "LearnTier2": return chinese ? "二阶学习书" : "Second-stage book";
                case "LearnTier3": return chinese ? "三阶学习书" : "Third-stage book";
                default: return chinese ? "独立特殊技能书" : "Special skill book";
            }
        }

        public static string ClassEnglish(int id)
        { string[] names = { "", "Ranger", "Wizard", "Priest", "Warrior", "Beastman" }; return id > 0 && id < names.Length ? names[id] : "Unknown"; }
        public override string ToString() { return ChineseName + " / " + Name; }
    }

    /// <summary>Reads verified concrete skill-book classes from the user's own installed engine. No original catalog is bundled.</summary>
    public sealed class ScriptItemCatalog
    {
        public const string EngineSha256 = "B22AD4D0630D39B4E376EEF3EFDDFE7EE363823AE0CB15AE41DE6BEFB2E8126F";
        public string GameDirectory { get; private set; }
        public List<ScriptBookDefinition> Books { get; private set; }
        private static readonly Encoding Gbk = Encoding.GetEncoding(936, EncoderFallback.ExceptionFallback, DecoderFallback.ExceptionFallback);
        private ScriptItemCatalog() { Books = new List<ScriptBookDefinition>(); }
        public ScriptBookDefinition Find(string path) { return Books.FirstOrDefault(b => String.Equals(b.Path, path, StringComparison.Ordinal)); }

        public static ScriptItemCatalog Load(string gameDirectory)
        {
            if (String.IsNullOrWhiteSpace(gameDirectory)) throw new ArgumentNullException("gameDirectory");
            var result = new ScriptItemCatalog { GameDirectory = System.IO.Path.GetFullPath(gameDirectory) };
            byte[] bytes = File.ReadAllBytes(System.IO.Path.Combine(result.GameDirectory, "ERRelive.dll"));
            using (var algorithm = SHA256.Create())
                if (!String.Equals(BitConverter.ToString(algorithm.ComputeHash(bytes)).Replace("-", ""), EngineSha256, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException("This original engine build has not been verified for script-book generation.");
            var image = new EngineImage(bytes);
            byte[] prefix = Gbk.GetBytes("复活/技能书/");
            var paths = new Dictionary<int, string>();
            for (int i = 0; i <= bytes.Length - prefix.Length; i++)
            {
                if (!Matches(bytes, i, prefix)) continue;
                string path = image.StringAtOffset(i);
                if (path.Length > 255 || path.IndexOf('\0') >= 0) throw new InvalidDataException("Invalid native book path.");
                paths.Add(image.Address(i), path); i += Gbk.GetByteCount(path);
            }
            if (paths.Count != 218) throw new InvalidDataException("The original script-book catalog is incomplete.");
            var registered = new HashSet<string>(StringComparer.Ordinal);
            for (int i = 5; i < bytes.Length - 14; i++)
            {
                // Original register(allocator, exactPath, 2, 0) calls only.
                if (bytes[i - 5] != 0x6A || bytes[i - 4] != 0 || bytes[i - 3] != 0x6A || bytes[i - 2] != 2
                    || bytes[i - 1] != 0x68 || bytes[i + 4] != 0x68 || bytes[i + 9] != 0xE8) continue;
                string path; if (!paths.TryGetValue(BitConverter.ToInt32(bytes, i), out path)) continue;
                if (image.Address(i + 14) + BitConverter.ToInt32(bytes, i + 10) != 0x100820D0) continue;
                if (!registered.Add(path)) throw new InvalidDataException("Duplicate native book registration.");
                int allocator = BitConverter.ToInt32(bytes, i + 5), allocatorAt = image.Offset(allocator);
                int vtable = 0; bool ordinarySize = false, ordinaryConstructor = false;
                for (int a = allocatorAt; a < allocatorAt + 100; a++)
                {
                    if (bytes[a] == 0x68 && BitConverter.ToInt32(bytes, a + 1) == 0x470) ordinarySize = true;
                    if (bytes[a] == 0xE8 && image.Address(a + 5) + BitConverter.ToInt32(bytes, a + 1) == 0x1004DA80) ordinaryConstructor = true;
                    if (bytes[a] == 0xC7 && bytes[a + 1] == 6) { vtable = BitConverter.ToInt32(bytes, a + 2); break; }
                }
                if (!ordinarySize || !ordinaryConstructor || vtable == 0) throw new InvalidDataException("Unverified native book allocation layout.");
                int vt = image.Offset(vtable), create = BitConverter.ToInt32(bytes, vt + 0x0C), use = BitConverter.ToInt32(bytes, vt + 0x34);
                if (BitConverter.ToInt32(bytes, vt + 8) != 0x1004DE30 || BitConverter.ToInt32(bytes, vt + 0x14) != 0x100027A0
                    || BitConverter.ToInt32(bytes, vt + 0x18) != 0x10002E10 || BitConverter.ToInt32(bytes, vt + 0x20) != 0x10050BD0)
                    throw new InvalidDataException("Unverified native book serialization layout.");
                int c = image.Offset(create);
                int[] callOffsets = { 3, 15, 32, 47, 59 };
                int[] targets = { 0x1004DB10, 0x10001BD0, 0x10041BD0, 0x10001BF0, 0x10001C00 };
                for (int k = 0; k < callOffsets.Length; k++)
                {
                    int at = c + callOffsets[k];
                    if (bytes[at] != 0xE8 || image.Address(at + 5) + BitConverter.ToInt32(bytes, at + 1) != targets[k])
                        throw new InvalidDataException("The native book constructor has an unverified operation.");
                }
                if (bytes[c + 8] != 0x68 || bytes[c + 20] != 0x68 || bytes[c + 42] != 0x68 || bytes[c + 52] != 0x68
                    || bytes[c + 64] != 0x5E || bytes[c + 65] != 0xC3
                    || !Matches(bytes, c + 25, new byte[] { 0x6A, 0xFF, 0x6A, 1, 0x6A, 1, 0x56 }))
                    throw new InvalidDataException("The native book constructor has an unverified shape.");
                if (image.String(BitConverter.ToInt32(bytes, c + 9)) != path) throw new InvalidDataException("The book's constructor path differs from its registry key.");
                string shell = image.String(BitConverter.ToInt32(bytes, c + 21)); int classId;
                if (!shell.StartsWith("skillbook_", StringComparison.Ordinal) || !Int32.TryParse(shell.Substring(10), out classId) || classId < 1 || classId > 5)
                    throw new InvalidDataException("The original book class shell is unverified.");
                string[] segments = path.Split('/'); string leaf = segments[segments.Length - 1];
                string[] classNames = { "", "游侠", "巫师", "祭司", "刀客", "兽人" };
                var book = new ScriptBookDefinition {
                    Path = path, Alias = image.String(BitConverter.ToInt32(bytes, c + 53)), Name = image.String(BitConverter.ToInt32(bytes, c + 43)),
                    ClassId = classId, ClassName = classNames[classId], Category = segments[2],
                    ChineseName = segments.Length > 4 ? segments[segments.Length - 2] + "·" + leaf : leaf,
                    BookKind = leaf.EndsWith("宝典", StringComparison.Ordinal) ? "ClassManual" : leaf == "学习书" ? "Learn" : leaf == "升级书" ? "Upgrade"
                        : leaf == "二阶学习书" ? "LearnTier2" : leaf == "三阶学习书" ? "LearnTier3" : "Special",
                    SkillAliases = new List<string>(), EnhancementAliases = new List<string>(),
                    AllocatorRva = allocator - 0x10000000, CreateRva = create - 0x10000000, UseRva = use - 0x10000000, VtableRva = vtable - 0x10000000 };
                if (String.IsNullOrWhiteSpace(book.Alias) || String.IsNullOrWhiteSpace(book.Name)) throw new InvalidDataException("Incomplete native book identity.");
                result.Books.Add(book);
            }
            if (result.Books.Count != 218) throw new InvalidDataException("Not every book path has its verified original registration.");
            int[] creates = result.Books.Select(b => b.CreateRva + 0x10000000).OrderBy(v => v).ToArray();
            foreach (ScriptBookDefinition book in result.Books)
            {
                int begin = book.UseRva + 0x10000000;
                int end = creates.FirstOrDefault(v => v > begin);
                int at = image.Offset(begin);
                if (end == 0)
                {
                    int stop = -1;
                    for (int j = at; j < at + 0x3000; j++)
                        if (Matches(bytes, j, new byte[] { 0xC2, 8, 0, 0x90 })) { stop = j + 3; break; }
                    if (stop < 0) throw new InvalidDataException("Unbounded final skill-book callback.");
                    end = image.Address(stop);
                }
                int last = image.Offset(end);
                for (int p = at; p < last - 4; p++)
                {
                    if (bytes[p] != 0xE8) continue;
                    int target = image.Address(p + 5) + BitConverter.ToInt32(bytes, p + 1);
                    if (target != 0x10045C40 && target != 0x10045DE0 && target != 0x10046190) continue;
                    if (p < 6 || bytes[p - 6] != 0x68 || bytes[p - 1] < 0x50 || bytes[p - 1] > 0x57)
                        throw new InvalidDataException("Unverified skill-book use argument binding.");
                    string alias = image.String(BitConverter.ToInt32(bytes, p - 5));
                    if (String.IsNullOrEmpty(alias) || alias.IndexOf('_') < 1) throw new InvalidDataException("Invalid bound native skill alias.");
                    List<string> list = target == 0x10045DE0 ? book.EnhancementAliases : book.SkillAliases;
                    if (!list.Contains(alias)) list.Add(alias);
                }
                if (book.SkillAliases.Count == 0) throw new InvalidDataException("The book has no verified root-skill use binding.");
            }
            var english = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (ScriptBookDefinition b in result.Books.Where(b => b.BookKind == "Learn" || b.BookKind == "Special"))
                foreach (string skill in b.SkillAliases) if (!english.ContainsKey(skill)) english.Add(skill, b.Name);
            foreach (ScriptBookDefinition b in result.Books) b.EnglishSkills = english;
            return result;
        }

        private static bool Matches(byte[] data, int at, byte[] pattern)
        { if (at < 0 || at > data.Length - pattern.Length) return false; for (int i = 0; i < pattern.Length; i++) if (data[at + i] != pattern[i]) return false; return true; }

        private sealed class EngineImage
        {
            private readonly byte[] data;
            private readonly List<int[]> sections = new List<int[]>();
            public EngineImage(byte[] bytes)
            {
                data = bytes;
                if (bytes.Length < 4096 || bytes[0] != 0x4D || bytes[1] != 0x5A) throw new InvalidDataException("Invalid original engine image.");
                int pe = BitConverter.ToInt32(bytes, 60), count = BitConverter.ToUInt16(bytes, pe + 6), optional = BitConverter.ToUInt16(bytes, pe + 20);
                int start = pe + 24 + optional;
                for (int i = 0; i < count; i++)
                { int at = start + i * 40; sections.Add(new[] { BitConverter.ToInt32(bytes, at + 12), BitConverter.ToInt32(bytes, at + 16), BitConverter.ToInt32(bytes, at + 20) }); }
            }
            public int Offset(int address)
            {
                int rva = address - 0x10000000;
                foreach (int[] s in sections) if (rva >= s[0] && rva < s[0] + s[1]) return s[2] + rva - s[0];
                throw new InvalidDataException("The original book points outside the verified image.");
            }
            public int Address(int offset)
            { foreach (int[] s in sections) if (offset >= s[2] && offset < s[2] + s[1]) return 0x10000000 + s[0] + offset - s[2]; throw new InvalidDataException("The native book record is outside a mapped section."); }
            public string String(int address) { return StringAtOffset(Offset(address)); }
            public string StringAtOffset(int start)
            {
                int end = start;
                while (end < data.Length && end - start <= 4096 && data[end] != 0) end++;
                if (end == data.Length || end - start > 4096) throw new InvalidDataException("Invalid native book string.");
                return Gbk.GetString(data, start, end - start);
            }
        }
    }
}
