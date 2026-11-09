using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace SealSaveEditor
{
    public enum ItemChineseNameKind { Unavailable, InstalledName, TableAnnotation, ReferenceTranslation }

    public sealed class ItemDefinition
    {
        public string Alias { get; set; }
        public string Name { get; set; }
        public string OriginalName { get; set; }
        public string ChineseName { get; internal set; }
        public ItemChineseNameKind ChineseNameKind { get; internal set; }
        public string ChineseNameSource { get; internal set; }
        public bool ChineseNameIsReference { get { return ChineseNameKind == ItemChineseNameKind.ReferenceTranslation; } }
        public int NameId { get; set; }
        public int TypeId { get; set; }
        public string TypeName { get; set; }
        public string Slot { get; set; }
        public int Level { get; set; }
        public int Element { get; set; }
        public string SourceFile { get; set; }
        public int SourceLine { get; set; }
        public Dictionary<string, string> RawFields { get; set; }
        public string GetDisplayName(bool chinese)
        { return chinese && !String.IsNullOrEmpty(ChineseName) ? ChineseName : Name; }
        public string GetNameDescription(bool chinese)
        {
            string source = ChineseNameKind == ItemChineseNameKind.InstalledName ? (chinese ? "安装版名称表" : "Installed name table")
                : ChineseNameKind == ItemChineseNameKind.TableAnnotation ? (chinese ? "原表中文标注" : "Original table annotation")
                : ChineseNameKind == ItemChineseNameKind.ReferenceTranslation ? (chinese ? "修改器参考译名，未经原中文版名称表确认" : "Editor reference translation; not verified against the original Chinese release")
                : (chinese ? "暂无可靠中文名称" : "Chinese name unavailable");
            return (chinese ? "中文名：" : "Chinese name: ") + (ChineseName ?? "") + Environment.NewLine
                + (chinese ? "英文 / 安装版名称：" : "English / installed name: ") + Name + Environment.NewLine
                + (chinese ? "名称来源：" : "Name source: ") + source
                + (String.IsNullOrEmpty(ChineseNameSource) ? "" : " (" + ChineseNameSource + ")");
        }
        public override string ToString() { return Name + " [" + Alias + "]"; }
    }

    public sealed class AttributeDefinition
    {
        public int Id { get; set; }
        public string Name { get; set; }
        public string Description { get; set; }
        public override string ToString() { return Id.ToString(CultureInfo.InvariantCulture) + " - " + Name; }
    }

    public sealed class AffixTemplate
    {
        public string Alias { get; set; }
        public string Name { get; set; }
        public string Description { get; set; }
        public int AttributeId { get; set; }
        public int MinLevel { get; set; }
        public int MaxLevel { get; set; }
        public double BaseValue { get; set; }
        public double Increment { get; set; }
        public Dictionary<string, string> RawFields { get; set; }
        public override string ToString() { return Name + " [" + Alias + "]"; }
    }

    public sealed class SkillDefinition
    {
        public string Alias { get; set; }
        public string Name { get; set; }
        public string OriginalName { get; set; }
        public int NameId { get; set; }
        public int BriefInfoId { get; set; }
        public int InfoId { get; set; }
        public int ActionId { get; set; }
        public int IconId { get; set; }
        public int TypeId { get; set; }
        public int MaxLevel { get; set; }
        public int RequiredLevel { get; set; }
        public int LevelIncrement { get; set; }
        public int SkillPointCost { get; set; }
        public int Element { get; set; }
        public string SourceFile { get; set; }
        public int SourceLine { get; set; }
        public Dictionary<string, string> RawFields { get; set; }
        public override string ToString() { return Name + " [" + Alias + "]"; }
    }

    public sealed class SkillEnhancementEffect
    {
        public int AttributeId { get; set; }
        public double Value { get; set; }
        public bool TargetsEnemy { get; set; }
        public int Order { get; set; }
    }

    public sealed class SkillEnhancementDefinition
    {
        public string Alias { get; set; }
        public string TargetSkillAlias { get; set; }
        public string Description { get; set; }
        public int InfoId { get; set; }
        public int IconId { get; set; }
        public int SortOrder { get; set; }
        public int SkillLevelRequirement { get; set; }
        public int SkillLevelIncrement { get; set; }
        public bool HasLevelRequirement { get; set; }
        public Dictionary<string, int> ElementRequirements { get; set; }
        public List<SkillEnhancementEffect> Effects { get; set; }
        public string SourceFile { get; set; }
        public int SourceLine { get; set; }
        public Dictionary<string, string> RawFields { get; set; }
    }

    public sealed class SkillAssociationDefinition
    {
        public string MainSkillAlias { get; set; }
        public string TargetSkillAlias { get; set; }
        public int AttackElement { get; set; }
        public List<int> RequiredStateIds { get; set; }
        public string SourceFile { get; set; }
        public int SourceLine { get; set; }
    }

    public sealed class CharacterClassDefinition
    {
        // This is the source column index, not a claimed serialized class ID.
        public int Index { get; set; }
        public string Name { get; set; }
        public string OriginalName { get; set; }
        public Dictionary<string, double> Values { get; set; }
        public override string ToString() { return Name; }
    }

    public sealed class MapEntrance
    {
        public int SourceSceneId { get; set; }
        public double X { get; set; }
        public double Y { get; set; }
        public int Direction { get; set; }
    }

    public sealed class MapDefinition
    {
        public string Alias { get; set; }
        public string Name { get; set; }
        public string MapFile { get; set; }
        public string SourceFile { get; set; }
        public List<MapEntrance> Entrances { get; set; }
        public Dictionary<string, string> RawFields { get; set; }
        public override string ToString() { return Name == Alias ? Alias : Name + " [" + Alias + "]"; }
    }

    /// <summary>Reads definitions from the user's own game. No original catalog is embedded.</summary>
    public sealed class GameCatalog
    {
        public string GameDirectory { get; private set; }
        public List<ItemDefinition> Items { get; private set; }
        public List<AttributeDefinition> Attributes { get; private set; }
        public List<AffixTemplate> AttributeTemplates { get; private set; }
        public List<SkillDefinition> Skills { get; private set; }
        public List<SkillEnhancementDefinition> SkillEnhancements { get; private set; }
        public List<SkillAssociationDefinition> SkillAssociations { get; private set; }
        public Dictionary<int, string> SkillDescriptions { get; private set; }
        public List<CharacterClassDefinition> CharacterClasses { get; private set; }
        public List<MapDefinition> Maps { get; private set; }
        public Dictionary<int, string> LocalizedNames { get; private set; }
        public Dictionary<int, string> FieldNames { get; private set; }
        public List<string> Warnings { get; private set; }
        private static readonly Encoding Gbk = Encoding.GetEncoding(936);

        private GameCatalog()
        {
            Items = new List<ItemDefinition>();
            Attributes = new List<AttributeDefinition>();
            AttributeTemplates = new List<AffixTemplate>();
            Skills = new List<SkillDefinition>();
            SkillEnhancements = new List<SkillEnhancementDefinition>();
            SkillAssociations = new List<SkillAssociationDefinition>();
            SkillDescriptions = new Dictionary<int, string>();
            CharacterClasses = new List<CharacterClassDefinition>();
            Maps = new List<MapDefinition>();
            LocalizedNames = new Dictionary<int, string>();
            FieldNames = new Dictionary<int, string>();
            Warnings = new List<string>();
        }

        public static GameCatalog Load(string gameDirectory)
        {
            if (String.IsNullOrWhiteSpace(gameDirectory)) throw new ArgumentException("Select the game directory.", "gameDirectory");
            var result = new GameCatalog();
            result.GameDirectory = Path.GetFullPath(gameDirectory);
            string data = Path.Combine(result.GameDirectory, "Data");
            if (!Directory.Exists(data)) throw new DirectoryNotFoundException("The selected game directory has no Data folder.");
            result.TryRead("localized names", delegate { result.LocalizedNames = ReadDlgNames(Path.Combine(result.GameDirectory, "Dlg", "Name.dlg")); });
            result.TryRead("skill descriptions", delegate { result.SkillDescriptions = ReadDlgNames(Path.Combine(result.GameDirectory, "Dlg", "info.dlg")); });
            result.TryRead("serialized field names", delegate { result.FieldNames = ReadFieldNames(Path.Combine(result.GameDirectory, "ERRelive.dll")); });
            for (int number = 1; number <= 7; number++)
            {
                string path = Path.Combine(data, "item_" + number + ".txt");
                result.TryRead(Path.GetFileName(path), delegate { result.ReadItems(path); });
            }
            result.TryRead("attributes", delegate { result.ReadAttributes(Path.Combine(data, "attribT.txt")); });
            result.TryRead("player skills", delegate { result.ReadSkills(Path.Combine(data, "skill_role.txt")); });
            result.TryRead("skill enhancements", delegate { result.ReadSkillEnhancements(Path.Combine(data, "skill_enhance.txt")); });
            result.TryRead("skill associations", delegate { result.ReadSkillAssociations(Path.Combine(data, "skill_associate.txt")); });
            result.TryRead("character classes", delegate { result.ReadClasses(Path.Combine(data, "role.txt")); });
            string scenes = Path.Combine(result.GameDirectory, "scene");
            if (Directory.Exists(scenes))
                foreach (string path in Directory.GetFiles(scenes, "*.Scn"))
                    result.TryRead(Path.GetFileName(path), delegate { result.ReadMap(path); });
            result.TryRead("map display names", delegate {
                Dictionary<string, string> names = ReadMapNames(Path.Combine(result.GameDirectory, "ERRelive.dll"), result.Maps.Select(x => x.Alias));
                foreach (MapDefinition map in result.Maps) { string name; if (names.TryGetValue(map.Alias, out name)) map.Name = name; }
            });
            return result;
        }

        private void TryRead(string category, Action action)
        {
            try { action(); }
            catch (IOException e) { Warnings.Add(category + ": " + e.Message); }
            catch (InvalidDataException e) { Warnings.Add(category + ": " + e.Message); }
            catch (ArgumentException e) { Warnings.Add(category + ": " + e.Message); }
        }

        public ItemDefinition FindItem(string alias)
        { return Items.FirstOrDefault(x => String.Equals(x.Alias, alias, StringComparison.OrdinalIgnoreCase)); }

        public AttributeDefinition FindAttribute(int id)
        { return Attributes.FirstOrDefault(x => x.Id == id); }

        public SkillDefinition FindSkill(string alias)
        { return Skills.FirstOrDefault(x => String.Equals(x.Alias, alias, StringComparison.OrdinalIgnoreCase)); }

        public SkillEnhancementDefinition FindSkillEnhancement(string alias)
        { return SkillEnhancements.FirstOrDefault(x => String.Equals(x.Alias, alias, StringComparison.OrdinalIgnoreCase)); }

        public string FindSkillDescription(int infoId)
        { string text; return SkillDescriptions.TryGetValue(infoId, out text) ? PlainGameText(text) : ""; }

        public static string PlainGameText(string text)
        {
            if (String.IsNullOrEmpty(text)) return "";
            text = Regex.Replace(text, @"&\d{5,}", "\u2022 ");
            bool styles = Regex.IsMatch(text, @"\[(?:B\(|[A-Za-z0-9])");
            text = Regex.Replace(text, @"\[B\((.*?)\)\]", "$1", RegexOptions.Singleline);
            text = Regex.Replace(text, @"\[[A-Za-z0-9]", "");
            if (styles) text = text.Replace("]", "");
            text = Regex.Replace(text, @"[\x00-\x08\x0B\x0C\x0E-\x1F]", "");
            text = text.Replace("\r\n", "\n").Replace('\r', '\n');
            text = String.Join(Environment.NewLine, text.Split('\n').Select(x => Regex.Replace(x.Trim(), @"[ \t]{2,}", " ")));
            return Regex.Replace(text, @"(?:\r?\n){3,}", Environment.NewLine + Environment.NewLine).Trim();
        }

        public static string DecodeDataFile(string path)
        {
            byte[] bytes = File.ReadAllBytes(path);
            // The unencrypted leading 0x80 marker is not part of the GBK text.
            int start = bytes.Length > 0 && bytes[0] == 0x80 ? 1 : 0;
            for (int i = start; i < bytes.Length; i++) bytes[i] ^= 0x78;
            return Gbk.GetString(bytes, start, bytes.Length - start);
        }

        // The engine registers the integer-field names in serialization order.
        // These IDs are ONE-BASED and differ from attribT item-effect IDs.
        public static Dictionary<int, string> ReadFieldNames(string enginePath)
        {
            byte[] bytes = File.ReadAllBytes(enginePath);
            if (bytes.Length < 128 || bytes[0] != 'M' || bytes[1] != 'Z') throw new InvalidDataException("Invalid engine PE file.");
            int pe = BitConverter.ToInt32(bytes, 60);
            if (pe < 0 || pe + 84 > bytes.Length || BitConverter.ToUInt32(bytes, pe) != 0x4550) throw new InvalidDataException("Invalid PE header.");
            int count = BitConverter.ToUInt16(bytes, pe + 6);
            int optionalSize = BitConverter.ToUInt16(bytes, pe + 20);
            if (BitConverter.ToUInt16(bytes, pe + 24) != 0x10B) throw new InvalidDataException("The catalog reader expects the original 32-bit engine.");
            uint imageBase = BitConverter.ToUInt32(bytes, pe + 52);
            int sections = pe + 24 + optionalSize;
            if (sections + count * 40L > bytes.Length) throw new InvalidDataException("Invalid PE sections.");
            Func<uint, int> addressToOffset = delegate(uint address) {
                if (address < imageBase) return -1;
                uint rva = address - imageBase;
                for (int s = 0; s < count; s++)
                {
                    int p = sections + s * 40; uint va = BitConverter.ToUInt32(bytes, p + 12);
                    uint rawSize = BitConverter.ToUInt32(bytes, p + 16), raw = BitConverter.ToUInt32(bytes, p + 20);
                    if (rva >= va && rva - va < rawSize && raw + (long)(rva - va) < bytes.Length) return (int)(raw + rva - va);
                }
                return -1;
            };
            Func<int, string> readString = delegate(int position) {
                if (position < 0) return null;
                int end = position;
                while (end < bytes.Length && end - position < 128 && bytes[end] >= 32 && bytes[end] <= 126) end++;
                return end < bytes.Length && bytes[end] == 0 ? Encoding.ASCII.GetString(bytes, position, end - position) : null;
            };
            for (int p = 0; p + 45 < bytes.Length; p++)
            {
                if (bytes[p] != 0x68 || bytes[p + 5] != 0xB9 || bytes[p + 10] != 0xE8) continue;
                if (readString(addressToOffset(BitConverter.ToUInt32(bytes, p + 1))) != "IsActorNPC") continue;
                uint dictionary = BitConverter.ToUInt32(bytes, p + 6);
                int callDestination = p + 15 + BitConverter.ToInt32(bytes, p + 11);
                var names = new Dictionary<int, string>();
                int entry = p;
                while (entry + 15 <= bytes.Length && names.Count < 256 && bytes[entry] == 0x68 && bytes[entry + 5] == 0xB9 && bytes[entry + 10] == 0xE8
                    && BitConverter.ToUInt32(bytes, entry + 6) == dictionary && entry + 15 + BitConverter.ToInt32(bytes, entry + 11) == callDestination)
                {
                    string name = readString(addressToOffset(BitConverter.ToUInt32(bytes, entry + 1)));
                    if (String.IsNullOrEmpty(name)) break;
                    names.Add(names.Count + 1, name); entry += 15;
                }
                if (names.Count >= 200 && names[2] == "Level" && names[3] == "Exp" && names[12] == "HP_MAX") return names;
            }
            throw new InvalidDataException("The engine field registration table was not recognized.");
        }

        public static Dictionary<string, string> ReadMapNames(string enginePath, IEnumerable<string> aliases)
        {
            byte[] bytes = File.ReadAllBytes(enginePath);
            if (bytes.Length < 128 || bytes[0] != 'M' || bytes[1] != 'Z') throw new InvalidDataException("Invalid engine PE file.");
            int pe = BitConverter.ToInt32(bytes, 60);
            if (pe < 0 || pe + 84 > bytes.Length || BitConverter.ToUInt32(bytes, pe) != 0x4550) throw new InvalidDataException("Invalid PE header.");
            int count = BitConverter.ToUInt16(bytes, pe + 6), optionalSize = BitConverter.ToUInt16(bytes, pe + 20);
            if (BitConverter.ToUInt16(bytes, pe + 24) != 0x10B) throw new InvalidDataException("The catalog reader expects the original 32-bit engine.");
            uint imageBase = BitConverter.ToUInt32(bytes, pe + 52);
            int sections = pe + 24 + optionalSize;
            if (sections + count * 40L > bytes.Length) throw new InvalidDataException("Invalid PE sections.");
            Func<uint, int> resolve = delegate(uint address) {
                if (address < imageBase) return -1;
                uint rva = address - imageBase;
                for (int s = 0; s < count; s++) {
                    int p = sections + s * 40; uint va = BitConverter.ToUInt32(bytes, p + 12), size = BitConverter.ToUInt32(bytes, p + 16), raw = BitConverter.ToUInt32(bytes, p + 20);
                    if (rva >= va && rva - va < size && raw + (long)(rva - va) < bytes.Length) return (int)(raw + rva - va);
                }
                return -1;
            };
            Func<uint, string> read = delegate(uint address) {
                int start = resolve(address); if (start < 0) return null;
                int end = start; while (end < bytes.Length && end - start < 512 && bytes[end] != 0) end++;
                return end < bytes.Length && bytes[end] == 0 ? Gbk.GetString(bytes, start, end - start) : null;
            };
            var known = new HashSet<string>(aliases, StringComparer.OrdinalIgnoreCase);
            var names = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            // Entity constructors set path, display name, then alias via three consecutive calls.
            // Accept only aliases that are independently present as .Scn files in this install.
            for (int p = 24; p + 12 <= bytes.Length; p++) {
                if (bytes[p] != 0x68 || bytes[p + 5] != 0x8B || bytes[p + 6] != 0xCE || bytes[p + 7] != 0xE8) continue;
                string alias = read(BitConverter.ToUInt32(bytes, p + 1));
                if (alias == null || !known.Contains(alias)) continue;
                if (bytes[p - 12] != 0x68 || bytes[p - 7] != 0x8B || bytes[p - 6] != 0xCE || bytes[p - 5] != 0xE8
                    || bytes[p - 24] != 0x68 || bytes[p - 19] != 0x8B || bytes[p - 18] != 0xCE || bytes[p - 17] != 0xE8) continue;
                string name = read(BitConverter.ToUInt32(bytes, p - 11));
                if (!String.IsNullOrWhiteSpace(name)) names[alias] = name;
            }
            return names;
        }

        private string ResolveName(int id, string fallback)
        {
            string name;
            return LocalizedNames.TryGetValue(id, out name) && !String.IsNullOrWhiteSpace(name) ? name : fallback;
        }

        // Name.dlg has a sparse index: missing IDs reuse another record's offset.
        // Validate the serialized record ID before accepting an index entry.
        public static Dictionary<int, string> ReadDlgNames(string path)
        {
            byte[] bytes = File.ReadAllBytes(path);
            if (bytes.Length < 96 || Encoding.ASCII.GetString(bytes, 0, 7) != "DLGTEXT")
                throw new InvalidDataException("Not a supported DLGTEXT name file.");
            uint offset = BitConverter.ToUInt32(bytes, 80);
            uint count = BitConverter.ToUInt32(bytes, 88);
            if (count > 1000000 || offset + (long)count * 8 > bytes.Length)
                throw new InvalidDataException("Invalid DLGTEXT index bounds.");
            var names = new Dictionary<int, string>();
            for (uint i = 0; i < count; i++)
            {
                int entry = checked((int)(offset + i * 8));
                uint id = BitConverter.ToUInt32(bytes, entry);
                uint record = BitConverter.ToUInt32(bytes, entry + 4);
                if (id != i || record + 16L > bytes.Length) continue;
                uint recordId = BitConverter.ToUInt32(bytes, checked((int)record + 12)) ^ 0xAAABACADU;
                if (recordId != id) continue;
                uint length = BitConverter.ToUInt32(bytes, (int)record);
                long end = record + 4L + length;
                if (length > 1000000 || end > bytes.Length) continue;
                for (int p = (int)record + 4; p + 8 <= end; p++)
                {
                    if (bytes[p] != 0xC4 || bytes[p + 1] != 0xAC || bytes[p + 2] != 0xAB || bytes[p + 3] != 0xAA) continue;
                    uint textLength = BitConverter.ToUInt32(bytes, p + 4) ^ 0xAAABACADU;
                    if (textLength < 1 || textLength > 32768 || p + 8L + textLength > end) continue;
                    byte[] text = new byte[textLength];
                    for (int j = 0; j < text.Length; j++)
                        text[j] = (byte)(bytes[p + 8 + j] ^ ((0xAA + text.Length - 1 - j) & 255));
                    if (text[text.Length - 1] != 0) continue;
                    names[(int)id] = Gbk.GetString(text, 0, text.Length - 1);
                    break;
                }
            }
            return names;
        }

        private void ReadItems(string path)
        {
            string[] lines = Lines(DecodeDataFile(path));
            string[] header = lines.Length > 1 ? lines[1].Split('\t') : new string[0];
            for (int line = 2; line < lines.Length; line++)
            {
                string[] row = lines[line].Split('\t');
                int type;
                if (row.Length < 5 || !Int32.TryParse(row[0], out type) || String.IsNullOrWhiteSpace(row[3])) continue;
                int id = Int(row, 4);
                string original = Field(row, 2);
                if (original == "name" || original.Length == 0) original = Field(row, 39);
                var definition = new ItemDefinition {
                    // Native factory matching is exact. One original quest alias has a trailing space.
                    Alias = row[3], NameId = id, OriginalName = original,
                    Name = ResolveName(id, original.Length == 0 ? Field(row, 3) : original),
                    TypeId = type, TypeName = TypeName(type), Slot = SlotName(type),
                    Level = Int(row, 8), Element = Int(row, 11),
                    SourceFile = Path.GetFileName(path), SourceLine = line + 1,
                    RawFields = Fields(header, row)
                };
                ApplyChineseName(definition, Field(row, 2));
                Items.Add(definition);
            }
        }

        internal static void ApplyChineseName(ItemDefinition item, string originalNote)
        {
            if (HasChinese(item.Name))
            {
                item.ChineseName = item.Name; item.ChineseNameKind = ItemChineseNameKind.InstalledName;
                item.ChineseNameSource = "Dlg/Name.dlg #" + item.NameId.ToString(CultureInfo.InvariantCulture);
            }
            else if (ItemNativeChineseNotes.IsTrusted(item.Alias) && HasChinese(originalNote))
            {
                item.ChineseName = originalNote.Trim(); item.ChineseNameKind = ItemChineseNameKind.TableAnnotation;
                item.ChineseNameSource = "Data/" + item.SourceFile + ":" + item.SourceLine.ToString(CultureInfo.InvariantCulture);
            }
            else
            {
                item.ChineseName = ItemChineseNames.Lookup(item.NameId);
                item.ChineseNameKind = String.IsNullOrEmpty(item.ChineseName) ? ItemChineseNameKind.Unavailable : ItemChineseNameKind.ReferenceTranslation;
                item.ChineseNameSource = String.IsNullOrEmpty(item.ChineseName) ? "" : "NameId " + item.NameId.ToString(CultureInfo.InvariantCulture);
            }
        }

        private static bool HasChinese(string value)
        { return !String.IsNullOrEmpty(value) && value.Any(c => c >= '\u3400' && c <= '\u9fff'); }

        private void ReadAttributes(string path)
        {
            string[] lines = Lines(DecodeDataFile(path));
            string[] header = lines.Length > 0 ? lines[0].Split('\t') : new string[0];
            var descriptions = new Dictionary<int, List<string>>();
            for (int line = 1; line < lines.Length; line++)
            {
                string[] row = lines[line].Split('\t');
                int id;
                if (row.Length < 8 || !Int32.TryParse(row[2], out id) || Field(row, 0).Length == 0) continue;
                string description = Field(row, 1);
                if (!descriptions.ContainsKey(id)) descriptions[id] = new List<string>();
                if (!descriptions[id].Contains(description)) descriptions[id].Add(description);
                AttributeTemplates.Add(new AffixTemplate {
                    Alias = Field(row, 0), Name = AttributeName(id), Description = description, AttributeId = id,
                    MinLevel = Int(row, 4), MaxLevel = Int(row, 5), BaseValue = Number(row, 6), Increment = Number(row, 7),
                    RawFields = Fields(header, row)
                });
            }
            foreach (int id in descriptions.Keys.OrderBy(x => x))
                Attributes.Add(new AttributeDefinition { Id = id, Name = AttributeName(id), Description = String.Join("; ", descriptions[id]) });
        }

        private void ReadSkills(string path)
        {
            string[] lines = Lines(DecodeDataFile(path));
            string[] header = lines.Length > 1 ? lines[1].Split('\t') : new string[0];
            for (int line = 2; line < lines.Length; line++)
            {
                string[] row = lines[line].Split('\t');
                int nameId;
                if (row.Length < 9 || Field(row, 0).Length == 0 || !Int32.TryParse(row[2], out nameId)) continue;
                Skills.Add(new SkillDefinition {
                    Alias = Field(row, 0), OriginalName = Field(row, 1), NameId = nameId,
                    BriefInfoId = Int(row, 3), InfoId = Int(row, 4), IconId = Int(row, 5), ActionId = Int(row, 6),
                    Name = ResolveName(nameId, Field(row, 1).Length == 0 ? Field(row, 0) : Field(row, 1)),
                    TypeId = Int(row, 7), MaxLevel = Int(row, 8), RequiredLevel = Int(row, 9),
                    LevelIncrement = Int(row, 10), SkillPointCost = Int(row, 11), Element = Int(row, 13),
                    SourceFile = Path.GetFileName(path), SourceLine = line + 1, RawFields = Fields(header, row)
                });
            }
        }

        private void ReadSkillEnhancements(string path)
        {
            string[] lines = Lines(DecodeDataFile(path));
            string[] header = lines.Length > 1 ? lines[1].Split('\t') : new string[0];
            string[] elements = { "Gold", "Wood", "Water", "Fire", "Soil" };
            for (int line = 2; line < lines.Length; line++)
            {
                string[] row = lines[line].Split('\t');
                if (row.Length < 3 || Field(row, 0).Length == 0 || Field(row, 2).Length == 0) continue;
                var requirements = new Dictionary<string, int>(StringComparer.Ordinal);
                for (int i = 0; i < elements.Length; i++) requirements[elements[i]] = Int(row, 11 + i);
                var effects = new List<SkillEnhancementEffect>();
                for (int i = 0; i < 8; i++)
                {
                    int column = 16 + i * 4, id = Int(row, column);
                    if (id == 0) continue;
                    effects.Add(new SkillEnhancementEffect { AttributeId = id, Value = Number(row, column + 1), TargetsEnemy = Int(row, column + 2) != 0, Order = Int(row, column + 3) });
                }
                SkillEnhancements.Add(new SkillEnhancementDefinition {
                    Alias = Field(row, 2), TargetSkillAlias = Field(row, 0), Description = Field(row, 1),
                    InfoId = Int(row, 3), IconId = Int(row, 4), SortOrder = Int(row, 6),
                    SkillLevelRequirement = Int(row, 7), SkillLevelIncrement = Int(row, 8), HasLevelRequirement = Field(row, 7).Length > 0,
                    ElementRequirements = requirements, Effects = effects, SourceFile = Path.GetFileName(path), SourceLine = line + 1, RawFields = Fields(header, row)
                });
            }
        }

        private void ReadSkillAssociations(string path)
        {
            string[] lines = Lines(DecodeDataFile(path));
            for (int line = 2; line < lines.Length; line++)
            {
                string[] row = lines[line].Split('\t');
                if (row.Length < 8 || Field(row, 0).Length == 0 || Field(row, 7).Length == 0) continue;
                var states = new List<int>();
                for (int i = 2; i <= 6; i++) if (Field(row, i).Length > 0) states.Add(Int(row, i));
                SkillAssociations.Add(new SkillAssociationDefinition { MainSkillAlias = Field(row, 0), TargetSkillAlias = Field(row, 7),
                    AttackElement = Int(row, 1), RequiredStateIds = states, SourceFile = Path.GetFileName(path), SourceLine = line + 1 });
            }
        }

        private void ReadClasses(string path)
        {
            string[] lines = Lines(DecodeDataFile(path));
            if (lines.Length == 0) return;
            string[] header = lines[0].Split('\t');
            // English class names are the original manual's five party classes.
            string[] english = { "Paladin", "Witch", "Wizard", "Assassin", "Beastman", "Chief", "Archmage", "Unknown" };
            for (int c = 1; c < header.Length; c++)
            {
                var values = new Dictionary<string, double>();
                for (int l = 1; l < lines.Length; l++)
                {
                    string[] row = lines[l].Split('\t');
                    double value;
                    if (row.Length > c && Double.TryParse(row[c], NumberStyles.Float, CultureInfo.InvariantCulture, out value)) values[Field(row, 0)] = value;
                }
                CharacterClasses.Add(new CharacterClassDefinition { Index = c - 1, Name = c <= english.Length ? english[c - 1] : header[c], OriginalName = header[c], Values = values });
            }
        }

        private void ReadMap(string path)
        {
            string[] lines = Lines(Gbk.GetString(File.ReadAllBytes(path)));
            var map = new MapDefinition { Alias = Path.GetFileNameWithoutExtension(path), Name = Path.GetFileNameWithoutExtension(path),
                SourceFile = Path.GetFileName(path), Entrances = new List<MapEntrance>(), RawFields = new Dictionary<string, string>() };
            MapEntrance entrance = null;
            for (int line = 0; line < lines.Length; line++)
            {
                int equals = lines[line].IndexOf('=');
                if (equals < 0) continue;
                string key = lines[line].Substring(0, equals).Trim(), value = lines[line].Substring(equals + 1).Trim();
                map.RawFields[key + " [" + (line + 1) + "]"] = value;
                if (line == 0 && value.EndsWith(".MAP", StringComparison.OrdinalIgnoreCase)) map.MapFile = value;
                // Each entrance is a verified four-row block: previous scene ID, x, y, direction.
                if (line >= 4 && line + 3 < lines.Length && lines[line + 1].StartsWith("x=", StringComparison.OrdinalIgnoreCase) && lines[line + 2].StartsWith("y=", StringComparison.OrdinalIgnoreCase))
                {
                    int source, direction; double x, y;
                    string fourth = lines[line + 3]; int directionEquals = fourth.IndexOf('=');
                    if (Int32.TryParse(value, out source) && Double.TryParse(lines[line + 1].Substring(2), NumberStyles.Float, CultureInfo.InvariantCulture, out x)
                        && Double.TryParse(lines[line + 2].Substring(2), NumberStyles.Float, CultureInfo.InvariantCulture, out y)
                        && directionEquals >= 0 && Int32.TryParse(fourth.Substring(directionEquals + 1), out direction))
                    { entrance = new MapEntrance { SourceSceneId = source, X = x, Y = y, Direction = direction }; map.Entrances.Add(entrance); }
                }
            }
            Maps.Add(map);
        }

        public static string SlotName(int type)
        {
            if (type >= 1 && type <= 5) return "Weapon";
            if (type >= 10 && type <= 15) return "Armor";
            if (type >= 20 && type <= 25) return "Helmet";
            if (type >= 30 && type <= 35) return "Boots";
            if (type == 41) return "Necklace";
            if (type == 42) return "Ring";
            return "Inventory";
        }

        public static string TypeName(int type)
        {
            string[] classes = { "Common", "Paladin", "Witch", "Wizard", "Assassin", "Beastman" };
            if (type >= 1 && type <= 5) return classes[type] + " weapon";
            if (type >= 10 && type <= 15) return classes[type - 10] + " armor";
            if (type >= 20 && type <= 25) return classes[type - 20] + " helmet";
            if (type >= 30 && type <= 35) return classes[type - 30] + " boots";
            if (type >= 45 && type <= 49) return classes[type - 44] + " skill book";
            switch (type)
            {
                case 41: return "Necklace"; case 42: return "Ring"; case 43: return "Medicine";
                case 50: return "Herb"; case 51: return "Weapon material"; case 53: return "Armor material";
                case 55: return "Accessory material"; case 57: return "Common material"; case 59: return "Gem";
                case 60: return "Money"; case 61: return "Quest / miscellaneous item"; case 62: return "Special item";
                case 63: return "Stone tablet"; case 64: return "Revival medicine"; default: return "Type " + type;
            }
        }

        public static string AttributeName(int id)
        {
            // IDs and meanings come from the installed game's attribT definitions.
            switch (id)
            {
                case 22: return "Maximum health"; case 24: return "Maximum energy"; case 57: return "Evasion"; case 58: return "Accuracy";
                case 80: return "Fury gain speed"; case 82: return "Dispel abnormal status"; case 85: return "Revival";
                case 100: return "Critical hit chance"; case 101: return "Back attack"; case 102: return "Deadly attack";
                case 103: return "Reflected damage"; case 104: return "Attack after taking damage";
                case 120: return "Health regeneration"; case 122: return "Health restored on kill"; case 123: return "Poison attack";
                case 140: return "Energy regeneration"; case 142: return "Energy restored on kill"; case 143: return "Freeze attack";
                case 144: return "Abnormal status duration"; case 160: return "Movement speed"; case 161: return "Attack speed";
                case 163: return "Burn attack"; case 164: return "Absolute evasion"; case 180: return "Exorcism attack";
                case 181: return "Stun attack"; case 182: return "Impact resistance"; case 183: return "Abnormal status resistance";
                case 184: return "Damage reduction"; case 185: return "Damage reduction (set)";
                case 200: return "Attack power"; case 201: return "Defense power"; case 202: return "Restore health"; case 203: return "Restore energy";
                default: return "Attribute " + id.ToString(CultureInfo.InvariantCulture);
            }
        }

        private static string[] Lines(string text) { return text.Replace("\r\n", "\n").Split('\n'); }
        private static string Field(string[] row, int column) { return column < row.Length ? row[column].Trim() : ""; }
        private static int Int(string[] row, int column) { int value; return Int32.TryParse(Field(row, column), NumberStyles.Integer, CultureInfo.InvariantCulture, out value) ? value : 0; }
        private static double Number(string[] row, int column) { double value; return Double.TryParse(Field(row, column), NumberStyles.Float, CultureInfo.InvariantCulture, out value) ? value : 0; }
        private static Dictionary<string, string> Fields(string[] header, string[] row)
        {
            var result = new Dictionary<string, string>();
            for (int i = 0; i < row.Length; i++)
            {
                string name = i < header.Length && header[i].Trim().Length > 0 ? header[i].Trim() : "Column " + i;
                if (result.ContainsKey(name)) name += " [" + i + "]";
                result[name] = row[i].Trim();
            }
            return result;
        }
    }
}
