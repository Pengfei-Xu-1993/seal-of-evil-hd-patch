using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace SealSaveEditor
{
    public sealed class SaveField
    {
        private string text;
        internal Action<string> Validator;
        internal SaveNode Node;
        internal int RelativeOffset;
        internal int StorageLength;
        internal bool OuterStorage;
        internal bool CanResize;
        internal bool Active = true;
        internal ItemModifier Modifier;
        internal int ModifierWord;
        internal bool ControlledCampaignWrite;
        public string Key { get; internal set; }
        public string Name { get; internal set; }
        public string Group { get; internal set; }
        public string Kind { get; internal set; }
        public string Evidence { get; internal set; }
        public bool Editable { get; internal set; }
        public int Offset { get; internal set; }
        public long Min { get; internal set; }
        public long Max { get; internal set; }
        public string OriginalText { get; internal set; }
        public long OriginalValue { get { long value; return Int64.TryParse(OriginalText, out value) ? value : 0; } }
        public long Value
        {
            get { long value; return Int64.TryParse(text, out value) ? value : 0; }
            set { Text = value.ToString(CultureInfo.InvariantCulture); }
        }
        public string Text
        {
            get { return text; }
            set
            {
                if (!Active) throw new InvalidOperationException("This field is no longer active in the current item state.");
                value = value ?? "";
                if (!Editable && OriginalText != null && value != OriginalText) throw new InvalidOperationException("This field is read only.");
                if (Kind == "Int32")
                {
                    long number;
                    if (!Int64.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out number) || number < Min || number > Max)
                        throw new ArgumentOutOfRangeException("value", "Enter an integer from " + Min + " to " + Max + ".");
                    value = number.ToString(CultureInfo.InvariantCulture);
                }
                else if (Kind == "Float32")
                {
                    float number;
                    if (!Single.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out number) || Single.IsNaN(number) || Single.IsInfinity(number))
                        throw new ArgumentException("Enter a finite 32-bit floating point number.");
                    value = number.ToString("R", CultureInfo.InvariantCulture);
                }
                if (Validator != null) Validator(value);
                text = value;
            }
        }
        public bool IsModified { get { return text != OriginalText; } }
        internal void Initialize(string value) { text = value; OriginalText = value; }
        internal void RestoreWorkingText(string value) { text = value; }
        public override string ToString() { return Name + " = " + Text; }
    }

    public sealed class CharacterRecord
    {
        internal SaveNode Node;
        public int Index { get; internal set; }
        public string Name { get { return Node.DisplayName; } }
        public List<SaveField> Fields { get; internal set; }
        public List<SaveField> Skills { get; internal set; }
        public List<ItemRecord> Items { get; internal set; }
        public override string ToString() { return Name; }
    }

    public sealed class ItemRecord
    {
        internal SaveNode Node;
        public int Index { get; internal set; }
        public string OwnerName { get; internal set; }
        public string Alias { get { return Node.Alias; } }
        public string Name { get { return Node.DisplayName; } }
        public List<SaveField> Fields { get; internal set; }
        public IList<ItemModifier> Modifiers { get { return Node.Modifiers.AsReadOnly(); } }
        public override string ToString() { return Name; }
    }

    public sealed class ItemModifier
    {
        internal SaveNode Node;
        internal int[] OriginalWords;
        internal int OriginalIndex;
        public int Index { get; internal set; }
        public int AttributeId { get { return (int)Fields.Single(f => f.ModifierWord == 4).Value; } }
        public float Value
        {
            get { return Single.Parse(Fields.Single(f => f.ModifierWord == 9).Text, CultureInfo.InvariantCulture); }
            set { Fields.Single(f => f.ModifierWord == 9).Text = value.ToString("R", CultureInfo.InvariantCulture); }
        }
        public List<SaveField> Fields { get; internal set; }
        public bool IsOriginal { get { return OriginalIndex >= 0; } }
        public bool IsEnabled { get { return Fields.Single(f => f.ModifierWord == 5).Value != 0; } }
        public int ActivationClass { get { return (int)Fields.Single(f => f.ModifierWord == 6).Value; } }
    }

    // A trusted, document-local undo token. It keeps field identities so existing
    // per-field history can coexist with add/remove modifier history.
    public sealed class ModifierSnapshot
    {
        internal SaveDocument Document;
        internal SaveNode Node;
        internal List<ItemModifier> Records;
        internal Dictionary<SaveField, string> Values;
    }

    // A pure result of the installed game's native item-generation tables.
    // The document validates and copies this plan before applying any changes.
    public sealed class ItemInitialState
    {
        public string Alias { get; set; }
        public string Name { get; set; }
        public string Path { get; set; }
        public int[] IntegerAttributes { get; set; }
        public Dictionary<string, string> StringAttributes { get; set; }
        public List<int[]> Modifiers { get; set; }
        public int BaseParameter { get; set; }
        public string GenerationSummary { get; set; }
    }

    public sealed class ItemSnapshot
    {
        internal SaveDocument Document;
        internal SaveNode Node;
        internal SaveNode State;
        internal Dictionary<SaveField, string> Values;
    }

    // Structural undo token for one newly created inventory object. It retains
    // the actual record/fields so ordinary edit and modifier history composes.
    public sealed class AddedItemToken
    {
        internal SaveDocument Document;
        internal CharacterRecord Owner;
        internal int ChildIndex, OwnerItemIndex, ItemIndex, NodeIndex;
        internal RecordInfo InspectorEntry;
        public ItemRecord Item { get; internal set; }
        public bool IsPresent { get; internal set; }
    }

    public sealed class RecordInfo
    {
        public string Name { get; internal set; }
        public int Offset { get; internal set; }
        public int Length { get; internal set; }
        public string Detail { get; internal set; }
    }

    internal sealed class SaveNode
    {
        internal byte[] Blob;
        internal int Offset, BlobOffset, OriginalLength, BaseSize, Type;
        internal string Path, OriginalName, OriginalAlias, Source;
        internal SaveField NameField, AliasField;
        internal List<SaveField> Fields = new List<SaveField>();
        internal List<SaveNode> Children = new List<SaveNode>();
        internal List<List<SaveNode>> Extra = new List<List<SaveNode>>();
        internal int ModifierCountOffset;
        internal List<ItemModifier> Modifiers = new List<ItemModifier>();
        internal List<ItemModifier> OriginalModifiers = new List<ItemModifier>();
        internal List<ItemModifier> AllModifiers = new List<ItemModifier>();
        internal bool TemplateReplaced;
        internal bool IsNewItem;
        internal string DisplayName { get { return NameField == null ? OriginalName : NameField.Text; } }
        internal string Alias { get { return AliasField == null ? OriginalAlias : AliasField.Text; } }
    }

    internal sealed class SaveRoom
    {
        internal int[] Table;
        internal byte[] EncodedName;
        internal string Name;
        internal SaveNode Root;
    }

    public sealed partial class SaveDocument
    {
        private static readonly Encoding GameEncoding = Encoding.GetEncoding(936, EncoderFallback.ExceptionFallback, DecoderFallback.ExceptionFallback);
        private readonly List<SaveNode> nodes = new List<SaveNode>();
        private readonly List<SaveField> fields = new List<SaveField>();
        private readonly List<SaveRoom> rooms = new List<SaveRoom>();
        private readonly Dictionary<int, string> fieldNames = new Dictionary<int, string>();
        private readonly Dictionary<int, string> effectNames = new Dictionary<int, string>();
        private readonly Dictionary<ItemRecord, AddedItemToken> addedItems = new Dictionary<ItemRecord, AddedItemToken>();
        private byte[] gameBytes, userBytes;
        private SaveNode userRoot;
        private int userTreeStart;
        public string FilePath { get; private set; }
        public string UserFilePath { get; private set; }
        public string GameDirectory { get; private set; }
        public string OriginalGamSha256 { get; private set; }
        public string OriginalUserSha256 { get; private set; }
        public int FormatVersion { get; private set; }
        public string Summary { get; private set; }
        public List<CharacterRecord> Characters { get; private set; }
        public List<ItemRecord> Items { get; private set; }
        public List<SaveField> Location { get; private set; }
        public List<RecordInfo> Inspector { get; private set; }
        public bool IsModified { get { return fields.Any(f => f.Active && f.IsModified) || nodes.Any(n => n.IsNewItem || n.TemplateReplaced || !n.Modifiers.SequenceEqual(n.OriginalModifiers)); } }

        private SaveDocument()
        {
            Characters = new List<CharacterRecord>(); Items = new List<ItemRecord>();
            Location = new List<SaveField>(); Inspector = new List<RecordInfo>();
        }

        public static SaveDocument Load(string gamPath)
        {
            string full = Path.GetFullPath(gamPath);
            DirectoryInfo game = Directory.GetParent(full);
            if (game == null || game.Parent == null) throw new InvalidDataException("A System/game save file is required.");
            string user = Path.Combine(game.Parent.FullName, "user", Path.GetFileNameWithoutExtension(full) + ".use");
            return Load(full, user);
        }

        public static SaveDocument Load(string gamPath, string userPath)
        {
            var document = new SaveDocument();
            document.FilePath = Path.GetFullPath(gamPath); document.UserFilePath = Path.GetFullPath(userPath);
            DirectoryInfo folder = Directory.GetParent(document.FilePath);
            document.GameDirectory = folder != null && folder.Parent != null && folder.Parent.Parent != null ? folder.Parent.Parent.FullName : "";
            string engine = Path.Combine(document.GameDirectory, "ERRelive.dll");
            if (File.Exists(engine))
            {
                foreach (var pair in GameCatalog.ReadFieldNames(engine)) document.fieldNames.Add(pair.Key, pair.Value);
            }
            else
            {
                // Verified one-based registration IDs from the original ERRelive.dll.
                string[] baseline = "IsActorNPC|Level|Exp|NextExp|Fame|Friend|Type|HPCount|StatusCount|Invincible|HP|HP_MAX|MP|MP_MAX|MD|MD_MAX|FE|Money|Gold|Wood|Water|Fire|Soil|Extra_Exp|Extra_Skill|AP|MDP1|MDP2|MDP3|MDP4|MDP5|HR|SR|MD_SPEED|B_C_HXYJ|B_BHGJ|B_S_JXGJ|B_FTSH|B_SSGJ|B_YJBS|HP_SPEED_PER|HP_SPEED_ABS|B_SDSM|B_S_ZDGJ|B_YDFH|B_XQSM|B_SQSM|MP_SPEED_PER|MP_SPEED_ABS|B_SDMF|B_S_BDGJ|B_S_CXSJ|B_BDFT|B_MXSH|B_XQMF|B_C_MOVE_SPEED|B_C_FIGHT_SPEED|B_GJJL|B_S_RSGJ|B_C_DBCG|B_C_JDMZ|B_QMGJ|B_S_XYGJ|B_C_DKCJ|B_S_ZTKX|B_SHJS_ABS|B_SHJS_PER|B_C_JTGJ|B_C_JTGJ2|B_C_VRANGE|B_C_SRANGE|HP_MAX0|MP_MAX0|MD_MAX0|Gold0|Wood0|Water0|Fire0|Soil0|AP0|MDP10|MDP20|MDP30|MDP40|MDP50|HR0|SR0|HP_SPEED_PER0|HP_SPEED_ABS0|MP_SPEED_PER0|MP_SPEED_ABS0|B_C_MOVE_SPEED0|B_C_FIGHT_SPEED0|B_C_DKCJ0|B_S_ZTKX0|B_C_VRANGE0|G_PosX|G_PosY|G_ScnInitID|G_InfoID|G_PicID|G_PicID2|G_AIGroup|G_AIGroup_Save|G_NPCActive|HP_TMP|MP_TMP|LSkill|RSkill|Visible|FightStatus|MD_SPEED0|Time|Find|CreatorID|IsCloned|StartPoint|Chapter|CurMap|Stone|GameCount|Chosen|Trading|Teaming|Making|Dying|G_WEA_GreenSky|G_WEA_BlueSky|G_WEA_YellowSky|KillPlayerCount|Bank|changjingid|PK|Prison|Timer|SinTmp|TimerEat|ChestPage|LastExp|ChestSent|Channel|Diff|Value|LevelPro|LevelGro|Part|Enable|Identify|can_get|can_eat|SlotMax|LevelReq|AttribReq|LevelReq0|AttribReq0|SuitID|LevelJud|IsGoldItem|ActiveSheng|ActiveSuit|UI_Pos|ValueUn|HitPoint|UI_PosOld|BelongsTo|EndureTmp|Amount|ArrowNum|istrap|isbuilding|deletekey|contain|LevelMax|LevelAdd|Operator|Status|EStatus|Attack|Associate|Series|AttackID|RangeMin|RangeMax|S_SUR|S_SPU|S_MPU|S_MDU|S_TLimit|S_BTLimit|S_BTLimit0|B_C_DKZD|B_C_HATE|G_InfoID2|UI_Cur|UI_AutoUse|UI_Shortcut|UIS_Repair|UIS_Produce|UIS_Addgem|UIS_Identify|G_Type|G_Time|G_Day|G_Night|G_WEA_BigRain|G_WEA_SmallRain|G_WEA_RedRain|G_WEA_BigSnow|G_WEA_SmallSnow|G_WEA_Hail|G_WEA_RedSky|G_WEA_PinkSky|G_WEA_WhiteSky|G_WEA_Gale|G_Ufo|G_Created|RandomExitCount|Map|DealPartner|DealLocking|DealMoney|Job|UIS_Teach|GoldReq|WoodReq|WaterReq|FireReq|SoilReq|ParamType|RecalcCountMax|RecalcCount|SkillIndex|DieType|S_AttackID0|S_RangeMax0|S_MPU0|S_MDU0|S_TLimit0".Split('|');
                for (int i = 0; i < baseline.Length; i++) document.fieldNames.Add(i + 1, baseline[i]);
            }
            document.gameBytes = ReadBounded(document.FilePath);
            document.userBytes = ReadBounded(document.UserFilePath);
            document.OriginalGamSha256 = Hash(document.gameBytes);
            document.OriginalUserSha256 = Hash(document.userBytes);
            // Verified item-effect IDs remain available when opening an exported
            // slot whose folder does not contain the original game's Data files.
            foreach (int id in new[] { 22, 24, 57, 58, 80, 82, 85, 100, 101, 102, 103, 104, 120, 122, 123, 140, 142, 143, 144, 160, 161, 163, 164, 180, 181, 182, 183, 184, 185, 200, 201, 202, 203 })
                document.effectNames[id] = GameCatalog.AttributeName(id);
            if (Directory.Exists(Path.Combine(document.GameDirectory, "Data")))
                foreach (AttributeDefinition attribute in GameCatalog.Load(document.GameDirectory).Attributes)
                    document.effectNames[attribute.Id] = attribute.Name;
            document.ParseGame(); document.ParseUser(); document.BuildModels();
            return document;
        }

        private static byte[] ReadBounded(string path)
        {
            var info = new FileInfo(path);
            if (!info.Exists) throw new FileNotFoundException("The matching save file was not found.", path);
            if (info.Length < 16 || info.Length > 128L * 1024 * 1024) throw new InvalidDataException("Invalid or excessively large save file.");
            return File.ReadAllBytes(path);
        }

        private void ParseGame()
        {
            if (gameBytes.Length < 84 || Encoding.ASCII.GetString(gameBytes, 0, 8) != "EASYRPG\0") throw new InvalidDataException("This is not an EASYRPG game save.");
            FormatVersion = BitConverter.ToInt32(gameBytes, 72);
            if (FormatVersion != 30 || BitConverter.ToInt32(gameBytes, 68) != 1) throw new InvalidDataException("Only the verified version-30 Seal of Evil save format is supported.");
            int position = 76;
            byte[] tableHead = ReadOuter(gameBytes, ref position, 8);
            int count = BitConverter.ToInt32(tableHead, 0), tableEnd = BitConverter.ToInt32(tableHead, 4);
            CheckCount(count, 4096, "rooms");
            if (tableEnd < 84 || tableEnd > gameBytes.Length) throw new InvalidDataException("Invalid room table boundary.");
            for (int i = 0; i < count; i++)
            {
                byte[] table = ReadOuter(gameBytes, ref position, 40);
                int[] values = new int[10]; for (int j = 0; j < 10; j++) values[j] = BitConverter.ToInt32(table, j * 4);
                CheckCount(values[3], 4096, "room name");
                byte[] name = ReadOuter(gameBytes, ref position, values[3]);
                rooms.Add(new SaveRoom { Table = values, Name = Decode(name), EncodedName = Outer(name) });
            }
            if (position != tableEnd) throw new InvalidDataException("Room table length does not match its header.");
            int expected = tableEnd;
            foreach (SaveRoom room in rooms)
            {
                long start = (long)tableEnd + room.Table[1], end = start + room.Table[2];
                if (start != expected || end > gameBytes.Length || end < start) throw new InvalidDataException("Room payload ranges are invalid or overlap.");
                int cursor = (int)start; room.Root = ParseNode(gameBytes, ref cursor, "GAM", 0);
                if (cursor != end) throw new InvalidDataException("Room payload length mismatch.");
                expected = cursor;
            }
            if (expected != gameBytes.Length) throw new InvalidDataException("Unrecognized bytes after the final room.");
        }

        private void ParseUser()
        {
            if (userBytes.Length < 17 || BitConverter.ToInt32(userBytes, 0) != 5 || Encoding.ASCII.GetString(userBytes, 4, 5) != "USER\0" || BitConverter.ToInt32(userBytes, 9) != 30)
                throw new InvalidDataException("This is not a verified version-30 USER save.");
            int position = 13;
            ReadOuterString(ref position, "Profile", false);
            ReadOuterString(ref position, "Account", false);
            ReadOuterString(ref position, "Root directory", false);
            ReadOuterInt(userBytes, ref position);
            SaveField room = ReadOuterString(ref position, "Current room", false);
            room.Group = "Map"; room.Evidence += " Map relocation also involves scene state and is displayed read only."; Location.Add(room);
            for (int i = 0; i < 4; i++) ReadOuterInt(userBytes, ref position);
            ReadOuterString(ref position, "Save menu label", false);
            ReadOuterString(ref position, "Saved at", false);
            userTreeStart = position; userRoot = ParseNode(userBytes, ref position, "USE", 0);
            int rootSize = ReadOuterInt(userBytes, ref position);
            if (rootSize != userRoot.OriginalLength || position != userBytes.Length) throw new InvalidDataException("USER root length or final file boundary mismatch.");
        }

        private SaveField ReadOuterString(ref int position, string name, bool editable)
        {
            int length = ReadOuterInt(userBytes, ref position); CheckCount(length, 16384, "USER string");
            int offset = position; byte[] value = ReadOuter(userBytes, ref position, length);
            var field = NewField("user." + name, name, "USER metadata", "String", Decode(value), editable && length > 0, offset, null, offset, length);
            field.OuterStorage = true;
            field.Evidence = "USER_Save/Load metadata, ERRelive 0xD0F7 and 0xD140. Original byte capacity: " + Math.Max(0, length - 1) + ".";
            field.Validator = MakeStringValidator(field, false);
            return field;
        }

        private SaveNode ParseNode(byte[] source, ref int position, string label, int depth)
        {
            if (depth > 100 || nodes.Count > 100000) throw new InvalidDataException("Save entity nesting is too deep.");
            int start = position, size = ReadOuterInt(source, ref position);
            if (size < 16 || size >= 0x4000) throw new InvalidDataException("Invalid entity memory payload size at 0x" + start.ToString("X") + ".");
            int blobOffset = position;
            var node = new SaveNode { Blob = ReadOuter(source, ref position, size), Offset = start, BlobOffset = blobOffset, Source = label };
            nodes.Add(node); ParseBase(node);
            int count = ReadOuterInt(source, ref position); CheckCount(count, 100000, "entity children");
            for (int i = 0; i < count; i++) node.Children.Add(ParseNode(source, ref position, label, depth + 1));
            int extras = (node.Type & 0x1001) == 0x1001 ? 3 : ((node.Type & 0x10001) == 0x10001 && (node.Type & 0x10003) != 0x10003 && (node.Type & 0x10005) != 0x10005 ? 1 : 0);
            for (int list = 0; list < extras; list++)
            {
                count = ReadOuterInt(source, ref position); CheckCount(count, 100000, "entity attachment list");
                var children = new List<SaveNode>(); node.Extra.Add(children);
                for (int i = 0; i < count; i++) children.Add(ParseNode(source, ref position, label, depth + 1));
            }
            int total = ReadOuterInt(source, ref position);
            if (total != position - start) throw new InvalidDataException("Entity length trailer mismatch at 0x" + start.ToString("X") + ".");
            node.OriginalLength = total; return node;
        }

        private void ParseBase(SaveNode node)
        {
            int position = 0;
            node.BaseSize = ReadInnerInt(node.Blob, ref position);
            if (node.BaseSize < 32 || node.BaseSize > node.Blob.Length) throw new InvalidDataException("Invalid base entity size.");
            node.Path = ReadInnerString(node, ref position, "Object path", false).Text;
            node.Type = ReadInnerInt(node.Blob, ref position);
            node.NameField = ReadInnerString(node, ref position, "Name", false); node.OriginalName = node.NameField.Text;
            node.AliasField = ReadInnerString(node, ref position, "Alias", false); node.OriginalAlias = node.AliasField.Text;
            int version = ReadInnerInt(node.Blob, ref position), count = ReadInnerInt(node.Blob, ref position);
            if (version != 30 || count != 256) throw new InvalidDataException("Unrecognized base attribute version or integer count.");
            for (int id = 0; id < count; id++)
            {
                int at = position, value = ReadInnerInt(node.Blob, ref position); string name;
                bool named = fieldNames.TryGetValue(id, out name);
                if (!named) name = "Unknown integer " + id;
                bool editable = named && SafeNumeric(id);
                if (node.Type == 0x10003)
                    editable = id == 152 || id >= 224 && id <= 228;
                SaveField field = NewField("attr." + id, name, FieldGroup(id), "Int32", value.ToString(CultureInfo.InvariantCulture), editable, node.BlobOffset + at, node, at, 4);
                field.Evidence = (named ? "Engine registration ID " + id + " = " + name + "; " : "Unregistered integer ID " + id + "; read only. ") + "ERRelive base loader 0x27A0; Int32 with nested XOR. " + node.Source + " payload+0x" + at.ToString("X") + ".";
                node.Fields.Add(field);
            }
            int stringCount = ReadInnerInt(node.Blob, ref position); CheckCount(stringCount, 4096, "string attributes");
            for (int i = 0; i < stringCount; i++)
            {
                string name = ReadInnerString(node, ref position, "String attribute key", false).Text;
                SaveField value = ReadInnerString(node, ref position, name, false);
                value.Key = "string." + name; value.Group = "String attributes";
                node.Fields.Add(value);
            }
            int parameterAt = position, parameter = ReadInnerInt(node.Blob, ref position);
            SaveField parameterField = NewField("base.parameter", "Base parameter (raw)", "Advanced (read only)", "Int32", parameter.ToString(CultureInfo.InvariantCulture), false, node.BlobOffset + parameterAt, node, parameterAt, 4);
            parameterField.Evidence = "Base loader 0x2C24 passes this integer to 0x2730. Its gameplay meaning is not established.";
            node.Fields.Add(parameterField);
            node.ModifierCountOffset = position;
            int modifierCount = ReadInnerInt(node.Blob, ref position); CheckCount(modifierCount, 4096, "attribute modifier records");
            for (int modifier = 0; modifier < modifierCount; modifier++)
            {
                int recordStart = position;
                int[] words = new int[13];
                for (int word = 0; word < 13; word++) words[word] = ReadInnerInt(node.Blob, ref position);
                if (words[0] != 52) throw new InvalidDataException("Unrecognized attribute modifier record size.");
                node.Modifiers.Add(CreateModifier(node, words, modifier, recordStart));
            }
            node.OriginalModifiers.AddRange(node.Modifiers);
            int trailerPosition = node.BaseSize - 4;
            if (trailerPosition != position || ReadInnerAt(node.Blob, trailerPosition) != node.BaseSize) throw new InvalidDataException("Base entity length trailer mismatch.");
            bool baseResizeVerified = node.BaseSize <= node.Blob.Length && (node.BaseSize == node.Blob.Length || ReadInnerAt(node.Blob, node.Blob.Length - 4) == node.Blob.Length - node.BaseSize);
            foreach (SaveField field in node.Fields.Where(f => f.Kind == "String"))
            {
                // Base size prefixes/trailers and derived tail-local lengths were checked on all reference entities.
                field.CanResize = baseResizeVerified;
                string key = field.Key.Substring("string.".Length);
                bool plainText = IsPlainTextAttribute(key);
                field.Editable = plainText && (field.CanResize || field.StorageLength > 1);
                field.Validator = MakeStringValidator(field, field.CanResize);
                field.Evidence = "Named string attribute; ERRelive CAttrStr loader 0x1460. " + (!plainText ? "Structured or unverified reference/state data; preserved read only until a validated picker is available." : field.CanResize ? "Base lengths and outer entity lengths are rebuilt; the derived tail is preserved." : "Original encoded byte capacity: " + Math.Max(0, field.StorageLength - 1) + ".");
            }
        }

        private ItemModifier CreateModifier(SaveNode node, int[] words, int originalIndex, int recordStart)
        {
            var record = new ItemModifier { Node = node, OriginalWords = (int[])words.Clone(), OriginalIndex = originalIndex, Index = node.Modifiers.Count, Fields = new List<SaveField>() };
            string effectName;
            if (!effectNames.TryGetValue(words[4], out effectName)) effectName = "attribute " + words[4];
            string group = "Modifier " + (record.Index + 1) + " / " + effectName;
            for (int word = 1; word < 13; word++)
            {
                int relative = recordStart < 0 ? -1 : recordStart + word * 4;
                bool itemObject = (node.Type & 0x101) == 0x101;
                int effectId = words[4];
                bool numericSkillEffect = effectNames.ContainsKey(effectId) || effectId == 441 || effectId == 442 || effectId == 443 || effectId == 446 || effectId == 500 || effectId == 501 || effectId == 508;
                bool known = word == 4 && itemObject && effectId > 0 || word == 9 && (itemObject || numericSkillEffect);
                string kind = word >= 9 ? "Float32" : "Int32";
                string text = word >= 9 ? BitConverter.ToSingle(BitConverter.GetBytes(words[word]), 0).ToString("R", CultureInfo.InvariantCulture) : words[word].ToString(CultureInfo.InvariantCulture);
                string name = word == 4 ? "Attribute ID" : word == 9 ? "Modifier value" : word == 10 ? "Growth coefficient (p1)" : word == 11 ? "Native parameter p2" : word == 12 ? "Fallback level (p3)" : word == 5 ? "Enabled state" : word == 6 ? "Activation class" : word == 8 ? "Application mode (0 absolute, 1 percent)" : "Raw word " + word;
                SaveField field = NewField("effect." + record.Index + "." + word, name, group, kind, text, known, relative < 0 ? -1 : node.BlobOffset + relative, node, relative, 4);
                field.Modifier = record; field.ModifierWord = word;
                field.Evidence = "52-byte modifier record, ERRelive loader 0x44E0; word " + word + ". "
                    + (word == 4 ? "Item-effect ID namespace, separate from the base integer IDs." : word == 9 ? "Finite IEEE-754 Float32 base amount p0." : word == 5 ? "Native apply path 0x56380 skips zero; ordinary constructor uses 1." : word == 6 ? "Ordinary constructor uses 0; values 2/4 have separate conditional activation paths." : word == 11 ? "Attribute-specific native parameter; defense 201 uses it as an elemental selector, not merely a growth offset." : "Native control parameter; preserved read only.");
                if (originalIndex < 0) field.Evidence += " New ordinary neutral modifier; native 0x4340/0x52CE0 defaults, no hidden activation state copied. Defense 201 with p2=0 adds equally to five defenses.";
                if (word == 4)
                {
                    int originalAttributeId = words[4];
                    field.Validator = delegate(string value)
                    {
                        int id = Int32.Parse(value, CultureInfo.InvariantCulture);
                        if (id != originalAttributeId && !effectNames.ContainsKey(id))
                            throw new ArgumentException("Choose a verified item-effect attribute ID. An unknown original ID may be kept unchanged.");
                    };
                }
                record.Fields.Add(field); node.Fields.Add(field);
            }
            node.AllModifiers.Add(record);
            return record;
        }

        private static bool IsPlainTextAttribute(string key)
        {
            // Model-part strings (G_EquipStr*), hidden-effect aliases (Sheng),
            // recipe/quest state and other engine references are not free text.
            return key == "Name" || key == "CustomName" || key == "ProduceName"
                || key == "info" || key == "long" || key == "G_Info" || key == "G_Info2" || key == "G_Info3";
        }

        private SaveField ReadInnerString(SaveNode node, ref int position, string name, bool editable)
        {
            int length = ReadInnerInt(node.Blob, ref position); CheckCount(length, 16384, "entity string");
            int at = position; byte[] value = ReadRange(node.Blob, ref position, length);
            InnerInPlace(value);
            SaveField field = NewField("meta." + name, name, "Identity", "String", Decode(value), editable, node.BlobOffset + at, node, at, length);
            field.Evidence = "Entity identity from the original serialized object; read only.";
            field.Validator = MakeStringValidator(field, false); return field;
        }

        private SaveField NewField(string key, string name, string group, string kind, string value, bool editable, int offset, SaveNode node, int relative, int length)
        {
            var field = new SaveField { Key = key, Name = name, Group = group, Kind = kind, Editable = editable, Offset = offset, Node = node, RelativeOffset = relative, StorageLength = length, Min = Int32.MinValue, Max = Int32.MaxValue };
            field.Initialize(value); fields.Add(field); return field;
        }

        private static Action<string> MakeStringValidator(SaveField field, bool resize)
        {
            return delegate(string value)
            {
                if (value.IndexOf('\0') >= 0) throw new ArgumentException("A string cannot contain a zero character.");
                int bytes = GameEncoding.GetByteCount(value);
                if (bytes > 4096 || (!resize && bytes >= field.StorageLength && value != field.OriginalText))
                    throw new ArgumentException("This field has space for " + Math.Max(0, field.StorageLength - 1) + " encoded bytes.");
            };
        }

        private static bool SafeNumeric(int id)
        {
            // Identity, runtime state, ownership, scene object IDs and script selectors remain read only.
            return (id >= 2 && id <= 5) || (id >= 10 && id <= 96) || id == 97 || id == 98 || id == 106 || id == 107 || id == 112
                || id == 139 || (id >= 143 && id <= 145) || id == 147 || id == 148 || (id >= 150 && id <= 155)
                || id == 157 || id == 158 || id == 162 || id == 163 || id == 166 || id == 167 || id == 168
                || id == 173 || id == 174 || (id >= 182 && id <= 192) || (id >= 224 && id <= 228) || (id >= 235 && id <= 238);
        }

        private static string FieldGroup(int id)
        {
            if (id == 97 || id == 98 || id == 119) return "Location";
            if (id >= 72 && id <= 96) return "Base attributes";
            if (id >= 143 && id <= 168) return "Item / equipment";
            if (id >= 173 && id <= 192 || id >= 234) return "Skill / effect";
            if (id <= 34) return "Character attributes";
            if (id <= 71) return "Bonuses / effects";
            return "Advanced (read only where unverified)";
        }

        private void BuildModels()
        {
            Inspector.Add(new RecordInfo {
                Name = "USER / Save metadata", Offset = 0, Length = userTreeStart,
                Detail = String.Join(Environment.NewLine, fields.Where(f => f.OuterStorage).Select(f => f.Name + " = " + f.Text).ToArray())
                    + Environment.NewLine + "The current room is read only; same-map coordinates are editable."
            });
            AddParty(userRoot);
            foreach (SaveNode node in nodes)
            {
                var detail = new StringBuilder(); detail.AppendLine(node.Source + " | type 0x" + node.Type.ToString("X") + " | " + node.Path);
                detail.AppendLine("Alias: " + node.Alias + "; base bytes: " + node.BaseSize + "; memory bytes: " + node.Blob.Length);
                detail.AppendLine("Children: " + node.Children.Count + "; extra lists: " + String.Join(", ", node.Extra.Select(e => e.Count.ToString()).ToArray()));
                foreach (SaveField field in node.Fields) detail.AppendLine(field.Name + " = " + field.Text);
                Inspector.Add(new RecordInfo { Name = node.Source + " / " + node.DisplayName + " [" + node.Alias + "]", Offset = node.Offset, Length = node.OriginalLength, Detail = detail.ToString() });
            }
            RefreshSummary();
        }

        private void RefreshSummary()
        { Summary = "Version " + FormatVersion + "; " + rooms.Count + " world rooms; " + Characters.Count + " party members; " + nodes.Count + " decoded entities. Player stats, inventory and skills are stored in the matching USER file."; }

        private void AddParty(SaveNode node)
        {
            if ((node.Type & 0x1001) != 0x1001) throw new InvalidDataException("USER root is not a player character.");
            var character = new CharacterRecord { Node = node, Index = Characters.Count, Fields = new List<SaveField>(node.Fields), Skills = new List<SaveField>(), Items = new List<ItemRecord>() };
            Characters.Add(character);
            character.Fields.Insert(0, node.AliasField); character.Fields.Insert(0, node.NameField);
            foreach (SaveField field in node.Fields.Where(f => f.Key == "attr.97" || f.Key == "attr.98")) { field.Group = node.DisplayName + " / Coordinates"; Location.Add(field); }
            foreach (SaveNode child in node.Children) AddItems(child, character, node.DisplayName);
            if (node.Extra.Count > 1) foreach (SaveNode skill in node.Extra[1]) AddSkillFields(character, skill, "Skill: " + skill.DisplayName);
            if (node.Extra.Count > 0) foreach (SaveNode companion in node.Extra[0])
                if ((companion.Type & 0x1001) == 0x1001) AddParty(companion);
        }

        private void AddItems(SaveNode node, CharacterRecord owner, string ownerName)
        {
            if ((node.Type & 0x101) == 0x101)
            {
                var item = new ItemRecord { Node = node, Index = Items.Count, OwnerName = ownerName, Fields = new List<SaveField>(node.Fields) };
                item.Fields.Insert(0, node.AliasField); item.Fields.Insert(0, node.NameField);
                Items.Add(item); owner.Items.Add(item);
            }
            foreach (SaveNode child in node.Children) AddItems(child, owner, ownerName);
        }

        private static void AddSkillFields(CharacterRecord owner, SaveNode node, string label)
        {
            // The inspector retains all 256 integers. The skills grid shows registered,
            // active values plus the common progression controls for every spell component.
            string[] always = { "Level", "Exp", "NextExp", "LevelMax", "LevelAdd" };
            foreach (SaveField field in node.Fields)
            {
                if (field.Kind == "Int32" && !always.Contains(field.Name) && (field.Text == "0" || field.Text == "-1" || field.Name.StartsWith("Unknown integer ", StringComparison.Ordinal))) continue;
                if (field.Kind == "String" && field.Text.Length == 0) continue;
                field.Group = label; owner.Skills.Add(field);
            }
            foreach (SaveNode child in node.Children) AddSkillFields(owner, child, label + " / " + child.DisplayName);
            foreach (List<SaveNode> list in node.Extra) foreach (SaveNode child in list) AddSkillFields(owner, child, label + " / " + child.DisplayName);
        }

        /// <summary>Add an ordinary, enabled, neutral actor-stat modifier to an existing item.</summary>
        public ItemModifier AddModifier(ItemRecord item, int effectId, float p0)
        {
            SaveNode node = RequireItem(item);
            if (effectId <= 0 || effectId >= 300 || !effectNames.ContainsKey(effectId))
                throw new ArgumentException("Choose a verified ordinary item-stat attribute ID.", "effectId");
            if (Single.IsNaN(p0) || Single.IsInfinity(p0)) throw new ArgumentOutOfRangeException("p0", "A modifier amount must be finite.");
            if (WriteBlob(node).Length + 52 >= 0x4000) throw new InvalidDataException("The new modifier would exceed the engine's 16 KiB serialization buffer.");
            // 0x4340 initializes enabled=1, activation=0, selector=0, source=-1,
            // amount mode=0 and p1/p2/p3=0; 0x52CE0 sets actor-stat type=0x11.
            // Defense 201 with neutral p2=0 adds p0 to every elemental defense.
            int[] words = { 52, 0x11, 0, -1, effectId, 1, 0, 0, 0, BitConverter.ToInt32(BitConverter.GetBytes(p0), 0), 0, 0, 0 };
            ItemModifier record = CreateModifier(node, words, -1, -1);
            node.Modifiers.Add(record); RefreshModifiers(item); return record;
        }

        public void RemoveModifier(ItemRecord item, int modifierIndex)
        {
            SaveNode node = RequireItem(item);
            if (modifierIndex < 0 || modifierIndex >= node.Modifiers.Count) throw new ArgumentOutOfRangeException("modifierIndex");
            node.Modifiers.RemoveAt(modifierIndex); RefreshModifiers(item);
        }

        public ModifierSnapshot CaptureModifiers(ItemRecord item)
        {
            SaveNode node = RequireItem(item);
            return new ModifierSnapshot {
                Document = this, Node = node, Records = new List<ItemModifier>(node.Modifiers),
                Values = node.Modifiers.SelectMany(m => m.Fields).ToDictionary(f => f, f => f.Text)
            };
        }

        public void RestoreModifiers(ItemRecord item, ModifierSnapshot snapshot)
        {
            SaveNode node = RequireItem(item);
            if (snapshot == null || snapshot.Document != this || snapshot.Node != node)
                throw new ArgumentException("The modifier snapshot belongs to a different document or item.", "snapshot");
            node.Modifiers.Clear(); node.Modifiers.AddRange(snapshot.Records);
            foreach (var pair in snapshot.Values) pair.Key.RestoreWorkingText(pair.Value);
            RefreshModifiers(item);
        }

        private SaveNode RequireItem(ItemRecord item)
        {
            if (item == null || !Items.Contains(item) || item.Node == null || (item.Node.Type & 0x101) != 0x101)
                throw new ArgumentException("Select an existing item belonging to this document.", "item");
            return item.Node;
        }

        internal SaveField GetPrimaryCampaignGameCountField()
        {
            if (userRoot == null || userRoot.Type != 0x1003 || userRoot.Source != "USE"
                || Characters.Count == 0 || Characters[0].Node != userRoot)
                throw new InvalidOperationException("This save has no verified primary-player campaign record.");
            SaveField field = userRoot.Fields.SingleOrDefault(f => f.Key == "attr.121" && f.Modifier == null);
            if (field == null || !field.Active || field.Node != userRoot || field.Kind != "Int32" || field.Name != "GameCount")
                throw new InvalidOperationException("This save has no verified primary-player GameCount field.");
            return field;
        }

        internal void SetPrimaryCampaignGameCount(SaveField expectedField, int value, bool authorizeWrite)
        {
            SaveField field = GetPrimaryCampaignGameCountField();
            if (field != expectedField) throw new ArgumentException("The campaign field belongs to a different player or document.", "expectedField");
            string text = value.ToString(CultureInfo.InvariantCulture);
            if (!authorizeWrite && text != field.OriginalText)
                throw new InvalidOperationException("A changed campaign field requires its document-local write authorization.");
            field.RestoreWorkingText(text); field.ControlledCampaignWrite = authorizeWrite;
        }

        private bool IsControlledCampaignWrite(SaveNode node, SaveField field)
        {
            return field.ControlledCampaignWrite && node == userRoot && node.Type == 0x1003
                && field.Node == node && field.Key == "attr.121" && field.Name == "GameCount"
                && field.Kind == "Int32" && field.Modifier == null;
        }

        /// <summary>Create one independent native item object in a selected party member's backpack.</summary>
        public ItemRecord AddItem(CharacterRecord owner, ItemInitialState initial)
        {
            if (owner == null || !Characters.Contains(owner) || owner.Node == null || !nodes.Contains(owner.Node))
                throw new ArgumentException("Select a party member belonging to this document.", "owner");
            ValidateInitialState(initial);
            int[] attributes = (int[])initial.IntegerAttributes.Clone();
            int position = AllocateItemPosition(owner, null, attributes[RegisteredId("Type")], -1, -1);
            attributes[RegisteredId("UI_Pos")] = attributes[RegisteredId("UI_PosOld")] = position;
            var strings = new Dictionary<string, string>(initial.StringAttributes, StringComparer.Ordinal);
            strings["G_Info"] = strings["G_Info2"] = strings["G_Info3"] = "";
            byte[] blob = BuildItemBlob(initial.Path ?? "", initial.Name, initial.Alias, attributes, strings,
                initial.Modifiers.Select(w => (int[])w.Clone()).ToList(), initial.BaseParameter);
            var node = new SaveNode { Blob = blob, Offset = -1, BlobOffset = -1, Source = owner.Node.Source, IsNewItem = true };
            int fieldStart = fields.Count;
            try
            {
                ParseBase(node);
                foreach (SaveField field in ItemStateFields(node)) field.Offset = -1;
                foreach (ItemModifier modifier in node.Modifiers) modifier.OriginalIndex = -1;
                node.OriginalLength = WriteNode(node).Length;
            }
            catch { fields.RemoveRange(fieldStart, fields.Count - fieldStart); throw; }
            var item = new ItemRecord { Node = node, Index = Items.Count, OwnerName = owner.Name, Fields = new List<SaveField>() };
            RefreshModifiers(item);
            var token = new AddedItemToken { Document = this, Owner = owner, Item = item, IsPresent = true,
                ChildIndex = owner.Node.Children.Count, OwnerItemIndex = owner.Items.Count, ItemIndex = Items.Count,
                NodeIndex = nodes.Count, InspectorEntry = MakeItemInspector(node) };
            owner.Node.Children.Add(node); owner.Items.Add(item); Items.Add(item); nodes.Add(node); Inspector.Add(token.InspectorEntry);
            addedItems.Add(item, token); RefreshSummary(); return item;
        }

        public AddedItemToken CaptureAddedItem(ItemRecord item)
        {
            AddedItemToken token;
            if (item == null || !addedItems.TryGetValue(item, out token))
                throw new ArgumentException("Only an item newly created by this document has an insertion token.", "item");
            return token;
        }

        public void DetachAddedItem(AddedItemToken token)
        {
            RequireAddedItemToken(token);
            if (!token.IsPresent) throw new InvalidOperationException("This added item is already removed.");
            ItemRecord item = token.Item; SaveNode node = RequireItem(item);
            token.ChildIndex = token.Owner.Node.Children.IndexOf(node); token.OwnerItemIndex = token.Owner.Items.IndexOf(item);
            token.ItemIndex = Items.IndexOf(item); token.NodeIndex = nodes.IndexOf(node);
            if (token.ChildIndex < 0 || token.OwnerItemIndex < 0 || token.NodeIndex < 0 || !Inspector.Contains(token.InspectorEntry))
                throw new InvalidOperationException("The added item's inventory attachment is inconsistent.");
            token.Owner.Node.Children.RemoveAt(token.ChildIndex); token.Owner.Items.RemoveAt(token.OwnerItemIndex);
            Items.RemoveAt(token.ItemIndex); nodes.RemoveAt(token.NodeIndex); Inspector.Remove(token.InspectorEntry);
            foreach (SaveField field in ItemStateFields(node)) field.Active = false;
            token.IsPresent = false; ReindexItems(); RefreshSummary();
        }

        public void ReattachAddedItem(AddedItemToken token)
        {
            RequireAddedItemToken(token);
            if (token.IsPresent) throw new InvalidOperationException("This added item is already present.");
            SaveNode node = token.Item.Node;
            int positionId = RegisteredId("UI_Pos");
            int position = Int32.Parse(node.Fields.Single(f => f.Key == "attr." + positionId).Text, CultureInfo.InvariantCulture);
            int type = Int32.Parse(node.Fields.Single(f => f.Key == "attr." + RegisteredId("Type")).Text, CultureInfo.InvariantCulture);
            if (!IsCompatibleBagPosition(position, PreferredItemPage(type)) || token.Owner.Node.Children.Any(n => Int32.Parse(n.Fields.Single(f => f.Key == "attr." + positionId).Text, CultureInfo.InvariantCulture) == position))
                throw new InvalidOperationException("The added item's backpack position is no longer free. Undo the intervening inventory change first.");
            int nodeIndex = Math.Min(token.NodeIndex, nodes.Count);
            token.Owner.Node.Children.Insert(Math.Min(token.ChildIndex, token.Owner.Node.Children.Count), node);
            token.Owner.Items.Insert(Math.Min(token.OwnerItemIndex, token.Owner.Items.Count), token.Item);
            Items.Insert(Math.Min(token.ItemIndex, Items.Count), token.Item); nodes.Insert(nodeIndex, node);
            Inspector.Insert(nodeIndex + 1, token.InspectorEntry);
            foreach (SaveField field in ItemStateFields(node)) field.Active = true;
            RefreshModifiers(token.Item); token.IsPresent = true; ReindexItems(); RefreshSummary();
        }

        private void RequireAddedItemToken(AddedItemToken token)
        {
            AddedItemToken actual;
            if (token == null || token.Document != this || token.Item == null || !addedItems.TryGetValue(token.Item, out actual) || actual != token || !Characters.Contains(token.Owner))
                throw new ArgumentException("The insertion token belongs to a different document or item.", "token");
        }

        private void ReindexItems()
        { for (int i = 0; i < Items.Count; i++) Items[i].Index = i; }

        private static RecordInfo MakeItemInspector(SaveNode node)
        {
            return new RecordInfo { Name = node.Source + " / " + node.DisplayName + " [" + node.Alias + "]",
                Offset = node.Offset, Length = node.OriginalLength,
                Detail = node.Source + " | type 0x" + node.Type.ToString("X") + " | " + node.Path + Environment.NewLine
                    + String.Join(Environment.NewLine, node.Fields.Select(f => f.Name + " = " + f.Text).ToArray()) };
        }

        private static void ValidateInitialState(ItemInitialState initial)
        {
            if (initial == null || initial.IntegerAttributes == null || initial.IntegerAttributes.Length != 256 || initial.StringAttributes == null || initial.Modifiers == null)
                throw new ArgumentException("A complete native item-generation plan is required.", "initial");
            if (String.IsNullOrEmpty(initial.Alias) || String.IsNullOrEmpty(initial.Name) || initial.IntegerAttributes[7] < 1)
                throw new ArgumentException("The generated item must have its native alias, name and gameplay type.", "initial");
        }

        public ItemSnapshot CaptureItem(ItemRecord item)
        {
            SaveNode node = RequireItem(item);
            return new ItemSnapshot { Document = this, Node = node, State = CopyItemState(node),
                Values = ItemStateFields(node).ToDictionary(f => f, f => f.Text) };
        }

        public void RestoreItem(ItemRecord item, ItemSnapshot snapshot)
        {
            SaveNode node = RequireItem(item);
            if (snapshot == null || snapshot.Document != this || snapshot.Node != node)
                throw new ArgumentException("The item snapshot belongs to a different document or item.", "snapshot");
            foreach (var pair in snapshot.Values) pair.Key.RestoreWorkingText(pair.Value);
            InstallItemState(item, snapshot.State);
        }

        /// <summary>Replace one existing item from a generated native template, placing it in its owner's backpack.</summary>
        public void ReplaceItem(ItemRecord item, ItemInitialState initial, bool preserveExistingModifiers)
        {
            SaveNode node = RequireItem(item);
            if (node.Type != 0x101 || node.BaseSize != node.Blob.Length || node.Children.Count != 0 || node.Extra.Count != 0)
                throw new InvalidOperationException("This item has unverified attached content and cannot safely be replaced.");
            ValidateInitialState(initial);
            CharacterRecord owner = Characters.FirstOrDefault(c => c.Items.Contains(item));
            if (owner == null || !owner.Node.Children.Contains(node))
                throw new InvalidOperationException("Only an item directly held by a party member can be replaced.");
            int[] attributes = (int[])initial.IntegerAttributes.Clone();
            // The ordinary child list is the actual owner relationship. BelongsTo is
            // a temporary native timer marker; neither it nor CreatorID is an actor ID.
            foreach (string name in new[] { "G_PosX", "G_PosY", "G_ScnInitID", "CreatorID", "BelongsTo" })
            {
                int id = RegisteredId(name); attributes[id] = Int32.Parse(node.Fields.Single(f => f.Key == "attr." + id).Text, CultureInfo.InvariantCulture);
            }
            int positionId = RegisteredId("UI_Pos"), previousId = RegisteredId("UI_PosOld");
            int current = Int32.Parse(node.Fields.Single(f => f.Key == "attr." + positionId).Text, CultureInfo.InvariantCulture);
            int previous = Int32.Parse(node.Fields.Single(f => f.Key == "attr." + previousId).Text, CultureInfo.InvariantCulture);
            int position = AllocateItemPosition(owner, node, attributes[RegisteredId("Type")], current, previous);
            attributes[positionId] = attributes[previousId] = position;
            var strings = new Dictionary<string, string>(initial.StringAttributes, StringComparer.Ordinal);
            // Generated descriptions are caches; force native regeneration after load.
            strings["G_Info"] = strings["G_Info2"] = strings["G_Info3"] = "";
            var modifiers = initial.Modifiers.Select(w => (int[])w.Clone()).ToList();
            if (preserveExistingModifiers) modifiers.AddRange(node.Modifiers.Select(ModifierWords));
            byte[] blob = BuildItemBlob(initial.Path ?? node.Path, initial.Name, initial.Alias, attributes, strings, modifiers, initial.BaseParameter);
            var candidate = new SaveNode { Blob = blob, Offset = node.Offset, BlobOffset = node.BlobOffset, Source = node.Source, OriginalLength = node.OriginalLength, TemplateReplaced = true };
            int fieldStart = fields.Count;
            try
            {
                ParseBase(candidate);
                foreach (SaveField field in ItemStateFields(candidate)) field.Offset = -1;
                foreach (ItemModifier modifier in candidate.Modifiers) modifier.OriginalIndex = -1;
                // Parsing and serialization have both succeeded before the existing
                // node, owner linkage or any visible field is changed.
                if (WriteBlob(candidate).Length >= 0x4000) throw new InvalidDataException("The generated item exceeds the native serialization buffer.");
            }
            catch { fields.RemoveRange(fieldStart, fields.Count - fieldStart); throw; }
            InstallItemState(item, candidate);
        }

        private int RegisteredId(string name)
        {
            foreach (var pair in fieldNames) if (pair.Value == name) return pair.Key;
            throw new InvalidDataException("The engine registration for " + name + " is unavailable.");
        }

        private int AllocateItemPosition(CharacterRecord owner, SaveNode excluded, int type, int current, int previous)
        {
            int positionId = RegisteredId("UI_Pos"), page = PreferredItemPage(type);
            var occupied = new HashSet<int>(owner.Node.Children.Where(n => n != excluded).Select(n => Int32.Parse(n.Fields.Single(f => f.Key == "attr." + positionId).Text, CultureInfo.InvariantCulture)));
            int position = IsCompatibleBagPosition(current, page) && !occupied.Contains(current) ? current
                : IsCompatibleBagPosition(previous, page) && !occupied.Contains(previous) ? previous : -1;
            // SOE 0x549150: general page plus the matching sixty-cell material page.
            if (position < 0 && page > 0)
                for (int cell = page * 60; cell < (page + 1) * 60; cell++) if (!occupied.Contains(cell)) { position = cell; break; }
            if (position < 0) for (int cell = 0; cell < 60; cell++) if (!occupied.Contains(cell)) { position = cell; break; }
            if (position < 0) throw new InvalidOperationException("The owner's matching backpack category and general page have no free position. Free one before adding or replacing this item.");
            return position;
        }

        private static int PreferredItemPage(int type)
        {
            if (type == 57 || type == 58 || type == 59) return 1;
            if (type == 51 || type == 52) return 2;
            if (type == 53 || type == 54) return 3;
            if (type == 55 || type == 56) return 4;
            if (type == 50) return 5;
            return 0;
        }

        private static bool IsCompatibleBagPosition(int position, int preferredPage)
        { return position >= 0 && position < 60 || preferredPage > 0 && position >= preferredPage * 60 && position < (preferredPage + 1) * 60; }

        private static IEnumerable<SaveField> ItemStateFields(SaveNode node)
        { return node.Fields.Concat(new[] { node.NameField, node.AliasField }).Where(f => f != null).Distinct(); }

        private static SaveNode CopyItemState(SaveNode node)
        {
            return new SaveNode { Blob = node.Blob, Offset = node.Offset, BlobOffset = node.BlobOffset,
                OriginalLength = node.OriginalLength, BaseSize = node.BaseSize, Type = node.Type,
                Path = node.Path, OriginalName = node.OriginalName, OriginalAlias = node.OriginalAlias, Source = node.Source,
                NameField = node.NameField, AliasField = node.AliasField, Fields = new List<SaveField>(node.Fields),
                ModifierCountOffset = node.ModifierCountOffset, Modifiers = new List<ItemModifier>(node.Modifiers),
                OriginalModifiers = new List<ItemModifier>(node.OriginalModifiers), AllModifiers = new List<ItemModifier>(node.AllModifiers),
                TemplateReplaced = node.TemplateReplaced };
        }

        private void InstallItemState(ItemRecord item, SaveNode state)
        {
            SaveNode node = item.Node;
            foreach (SaveField field in ItemStateFields(node)) field.Active = false;
            node.Blob = state.Blob; node.BaseSize = state.BaseSize; node.Type = state.Type; node.Path = state.Path;
            node.OriginalName = state.OriginalName; node.OriginalAlias = state.OriginalAlias;
            node.NameField = state.NameField; node.AliasField = state.AliasField; node.Fields = new List<SaveField>(state.Fields);
            node.ModifierCountOffset = state.ModifierCountOffset; node.Modifiers = new List<ItemModifier>(state.Modifiers);
            node.OriginalModifiers = new List<ItemModifier>(state.OriginalModifiers); node.AllModifiers = new List<ItemModifier>(state.AllModifiers);
            node.TemplateReplaced = state.TemplateReplaced;
            foreach (SaveField field in ItemStateFields(node)) { field.Node = node; field.Active = true; }
            foreach (ItemModifier modifier in node.AllModifiers) modifier.Node = node;
            RefreshModifiers(item);
            int inspectorIndex = nodes.IndexOf(node) + 1;
            if (inspectorIndex > 0 && inspectorIndex < Inspector.Count)
            {
                RecordInfo entry = Inspector[inspectorIndex];
                entry.Name = node.Source + " / " + node.DisplayName + " [" + node.Alias + "]";
                entry.Detail = node.Source + " | type 0x" + node.Type.ToString("X") + " | " + node.Path + Environment.NewLine
                    + String.Join(Environment.NewLine, node.Fields.Select(f => f.Name + " = " + f.Text).ToArray());
            }
        }

        private static int[] ModifierWords(ItemModifier modifier)
        {
            int[] words = (int[])modifier.OriginalWords.Clone();
            foreach (SaveField field in modifier.Fields)
                words[field.ModifierWord] = field.Kind == "Float32" ? BitConverter.ToInt32(BitConverter.GetBytes(Single.Parse(field.Text, CultureInfo.InvariantCulture)), 0) : Int32.Parse(field.Text, CultureInfo.InvariantCulture);
            return words;
        }

        private static byte[] BuildItemBlob(string path, string name, string alias, int[] attributes, Dictionary<string, string> strings, List<int[]> modifiers, int parameter)
        {
            if (strings.Count > 4096 || modifiers.Count > 256) throw new InvalidDataException("The item-generation plan is too large.");
            using (var stream = new MemoryStream())
            {
                WriteInner(stream, BitConverter.GetBytes(0)); WriteInnerString(stream, path); WriteInner(stream, BitConverter.GetBytes(0x101));
                WriteInnerString(stream, name); WriteInnerString(stream, alias);
                WriteInner(stream, BitConverter.GetBytes(30)); WriteInner(stream, BitConverter.GetBytes(256));
                foreach (int value in attributes) WriteInner(stream, BitConverter.GetBytes(value));
                WriteInner(stream, BitConverter.GetBytes(strings.Count));
                foreach (var pair in strings) { WriteInnerString(stream, pair.Key); WriteInnerString(stream, pair.Value); }
                WriteInner(stream, BitConverter.GetBytes(parameter)); WriteInner(stream, BitConverter.GetBytes(modifiers.Count));
                foreach (int[] words in modifiers)
                {
                    if (words == null || words.Length != 13 || words[0] != 52) throw new InvalidDataException("A generated native modifier must contain thirteen 32-bit words and the size 52.");
                    for (int p = 9; p < 13; p++) if (Single.IsNaN(BitConverter.ToSingle(BitConverter.GetBytes(words[p]), 0)) || Single.IsInfinity(BitConverter.ToSingle(BitConverter.GetBytes(words[p]), 0)))
                        throw new InvalidDataException("A generated modifier parameter is not finite.");
                    foreach (int word in words) WriteInner(stream, BitConverter.GetBytes(word));
                }
                int size = checked((int)stream.Length + 4);
                if (size >= 0x4000) throw new InvalidDataException("The generated item exceeds the native 16 KiB serialization buffer.");
                WriteInner(stream, BitConverter.GetBytes(size)); byte[] blob = stream.ToArray();
                byte[] prefix = BitConverter.GetBytes(size); InnerInPlace(prefix); Buffer.BlockCopy(prefix, 0, blob, 0, 4); return blob;
            }
        }

        private static void WriteInnerString(Stream stream, string text)
        {
            text = text ?? "";
            if (text.IndexOf('\0') >= 0 || GameEncoding.GetByteCount(text) > 4096) throw new InvalidDataException("A generated item string is invalid or too long.");
            byte[] value = GameEncoding.GetBytes(text + "\0"); WriteInner(stream, BitConverter.GetBytes(value.Length)); WriteInner(stream, value);
        }

        private void RefreshModifiers(ItemRecord item)
        {
            SaveNode node = item.Node;
            foreach (ItemModifier record in node.AllModifiers) foreach (SaveField field in record.Fields) field.Active = false;
            node.Fields.RemoveAll(f => f.Modifier != null);
            for (int index = 0; index < node.Modifiers.Count; index++)
            {
                ItemModifier record = node.Modifiers[index]; record.Index = index;
                string name; if (!effectNames.TryGetValue(record.AttributeId, out name)) name = "attribute " + record.AttributeId;
                foreach (SaveField field in record.Fields)
                {
                    field.Active = true; field.Key = "effect." + index + "." + field.ModifierWord;
                    field.Group = "Modifier " + (index + 1) + " / " + name; node.Fields.Add(field);
                }
            }
            item.Fields.Clear(); item.Fields.Add(node.NameField); item.Fields.Add(node.AliasField); item.Fields.AddRange(node.Fields);
        }

        public void SaveAs(string outputGamPath)
        {
            using (var stream = new MemoryStream())
            {
                stream.Write(gameBytes, 0, 76);
                int tableLength = 84 + rooms.Sum(r => 40 + r.EncodedName.Length);
                var payloads = rooms.Select(r => WriteNode(r.Root)).ToList();
                WriteOuter(stream, Combine(BitConverter.GetBytes(rooms.Count), BitConverter.GetBytes(tableLength)));
                int relative = 0;
                for (int i = 0; i < rooms.Count; i++)
                {
                    int[] table = (int[])rooms[i].Table.Clone(); table[1] = relative; table[2] = payloads[i].Length;
                    byte[] bytes = new byte[40]; for (int j = 0; j < 10; j++) Buffer.BlockCopy(BitConverter.GetBytes(table[j]), 0, bytes, j * 4, 4);
                    WriteOuter(stream, bytes); stream.Write(rooms[i].EncodedName, 0, rooms[i].EncodedName.Length); relative += payloads[i].Length;
                }
                foreach (byte[] payload in payloads) stream.Write(payload, 0, payload.Length);
                File.WriteAllBytes(outputGamPath, stream.ToArray());
            }
        }

        public void SaveUserAs(string outputUserPath)
        {
            byte[] header = new byte[userTreeStart]; Buffer.BlockCopy(userBytes, 0, header, 0, header.Length);
            foreach (SaveField field in fields.Where(f => f.OuterStorage && f.IsModified))
            {
                byte[] value = EncodeFixedString(field); value = Outer(value);
                Buffer.BlockCopy(value, 0, header, field.RelativeOffset, value.Length);
            }
            byte[] root = WriteNode(userRoot);
            using (var stream = new MemoryStream())
            {
                stream.Write(header, 0, header.Length); stream.Write(root, 0, root.Length); WriteOuter(stream, BitConverter.GetBytes(root.Length));
                File.WriteAllBytes(outputUserPath, stream.ToArray());
            }
        }

        private byte[] WriteNode(SaveNode node)
        {
            byte[] blob = WriteBlob(node);
            if (blob.Length >= 0x4000) throw new InvalidDataException("The edited entity exceeds the engine's 16 KiB serialization buffer.");
            using (var stream = new MemoryStream())
            {
                WriteOuter(stream, BitConverter.GetBytes(blob.Length)); WriteOuter(stream, blob);
                WriteList(stream, node.Children);
                foreach (List<SaveNode> extra in node.Extra) WriteList(stream, extra);
                WriteOuter(stream, BitConverter.GetBytes(checked((int)stream.Length + 4)));
                return stream.ToArray();
            }
        }

        private void WriteList(Stream stream, List<SaveNode> list)
        {
            WriteOuter(stream, BitConverter.GetBytes(list.Count));
            foreach (SaveNode child in list) { byte[] bytes = WriteNode(child); stream.Write(bytes, 0, bytes.Length); }
        }

        private byte[] WriteBlob(SaveNode node)
        {
            bool modifierChanged = !node.Modifiers.SequenceEqual(node.OriginalModifiers)
                || node.Modifiers.Any(m => m.Fields.Any(f => f.IsModified));
            var changes = node.Fields.Where(f => f.Modifier == null && f.IsModified).ToDictionary(f => f, f => f.Text);
            if (modifierChanged && (node.Type & 0x101) == 0x101)
            {
                // Native 0x39380 lazily regenerates empty G_Info via 0x56450/0x59140.
                // Invalidate generated descriptions without destroying an explicit text edit.
                foreach (SaveField field in node.Fields.Where(f => f.Key == "string.G_Info" || f.Key == "string.G_Info2" || f.Key == "string.G_Info3"))
                    if (!field.IsModified && field.Text.Length > 0) changes[field] = "";
            }
            if (!modifierChanged && changes.Count == 0) return node.Blob;
            using (var stream = new MemoryStream())
            {
                int cursor = 0;
                foreach (var change in changes.OrderBy(p => p.Key.RelativeOffset))
                {
                    SaveField field = change.Key;
                    if (field.IsModified && !field.Editable && !IsControlledCampaignWrite(node, field) && !IsControlledSkillWrite(node, field))
                        throw new InvalidOperationException("A read-only field was changed.");
                    int replaceStart = field.RelativeOffset, replaceLength = field.StorageLength;
                    byte[] value;
                    if (field.Kind == "Int32") value = BitConverter.GetBytes(Int32.Parse(change.Value, CultureInfo.InvariantCulture));
                    else if (field.Kind == "Float32") value = BitConverter.GetBytes(Single.Parse(change.Value, CultureInfo.InvariantCulture));
                    else if (field.CanResize)
                    {
                        value = GameEncoding.GetBytes(change.Value + "\0"); InnerInPlace(value);
                        byte[] length = BitConverter.GetBytes(value.Length); InnerInPlace(length);
                        value = Combine(length, value); replaceStart -= 4; replaceLength += 4;
                        stream.Write(node.Blob, cursor, replaceStart - cursor); stream.Write(value, 0, value.Length); cursor = replaceStart + replaceLength; continue;
                    }
                    else value = EncodeFixedString(field);
                    InnerInPlace(value);
                    stream.Write(node.Blob, cursor, replaceStart - cursor); stream.Write(value, 0, value.Length); cursor = replaceStart + replaceLength;
                }
                if (cursor > node.ModifierCountOffset) throw new InvalidDataException("An edited field overlaps the modifier section.");
                stream.Write(node.Blob, cursor, node.ModifierCountOffset - cursor);
                WriteInner(stream, BitConverter.GetBytes(node.Modifiers.Count));
                foreach (ItemModifier record in node.Modifiers)
                {
                    int[] words = (int[])record.OriginalWords.Clone();
                    foreach (SaveField field in record.Fields.Where(f => f.IsModified))
                    {
                        if (!field.Editable) throw new InvalidOperationException("A read-only modifier control was changed.");
                        words[field.ModifierWord] = field.Kind == "Float32"
                            ? BitConverter.ToInt32(BitConverter.GetBytes(Single.Parse(field.Text, CultureInfo.InvariantCulture)), 0)
                            : Int32.Parse(field.Text, CultureInfo.InvariantCulture);
                    }
                    foreach (int word in words) WriteInner(stream, BitConverter.GetBytes(word));
                }
                int newBaseSize = checked((int)stream.Length + 4);
                WriteInner(stream, BitConverter.GetBytes(newBaseSize));
                stream.Write(node.Blob, node.BaseSize, node.Blob.Length - node.BaseSize);
                byte[] result = stream.ToArray();
                byte[] baseLength = BitConverter.GetBytes(newBaseSize); InnerInPlace(baseLength);
                Buffer.BlockCopy(baseLength, 0, result, 0, 4);
                return result;
            }
        }

        private static byte[] EncodeFixedString(SaveField field)
        {
            byte[] value = GameEncoding.GetBytes(field.Text);
            if (value.Length >= field.StorageLength) throw new ArgumentException("The edited string exceeds its original capacity.");
            byte[] result = new byte[field.StorageLength]; Buffer.BlockCopy(value, 0, result, 0, value.Length); return result;
        }

        private static void CheckCount(int value, int maximum, string label)
        { if (value < 0 || value > maximum) throw new InvalidDataException("Invalid " + label + " count or length."); }
        private static byte[] ReadRange(byte[] source, ref int position, int length)
        {
            if (length < 0 || position < 0 || (long)position + length > source.Length) throw new InvalidDataException("Unexpected end of save file.");
            byte[] result = new byte[length]; Buffer.BlockCopy(source, position, result, 0, length); position += length; return result;
        }
        private static byte[] ReadOuter(byte[] source, ref int position, int length) { return Outer(ReadRange(source, ref position, length)); }
        private static int ReadOuterInt(byte[] source, ref int position) { return BitConverter.ToInt32(ReadOuter(source, ref position, 4), 0); }
        private static int ReadInnerInt(byte[] source, ref int position) { byte[] value = ReadRange(source, ref position, 4); InnerInPlace(value); return BitConverter.ToInt32(value, 0); }
        private static int ReadInnerAt(byte[] source, int position) { return ReadInnerInt(source, ref position); }
        private static string Decode(byte[] value) { int end = Array.IndexOf(value, (byte)0); return GameEncoding.GetString(value, 0, end < 0 ? value.Length : end); }
        private static byte[] Outer(byte[] value) { byte[] result = new byte[value.Length]; for (int i = 0; i < value.Length; i++) result[i] = (byte)(value[i] ^ (byte)(i + 0x59)); return result; }
        private static void InnerInPlace(byte[] value) { for (int i = 0; i < value.Length; i++) value[i] ^= (byte)(value.Length - i - 0x57); }
        private static void WriteOuter(Stream stream, byte[] value) { value = Outer(value); stream.Write(value, 0, value.Length); }
        private static void WriteInner(Stream stream, byte[] value) { InnerInPlace(value); stream.Write(value, 0, value.Length); }
        private static byte[] Combine(byte[] first, byte[] second) { byte[] result = new byte[first.Length + second.Length]; Buffer.BlockCopy(first, 0, result, 0, first.Length); Buffer.BlockCopy(second, 0, result, first.Length, second.Length); return result; }
        private static string Hash(byte[] bytes)
        {
            using (SHA256 sha = SHA256.Create()) return BitConverter.ToString(sha.ComputeHash(bytes)).Replace("-", "").ToLowerInvariant();
        }
    }
}
