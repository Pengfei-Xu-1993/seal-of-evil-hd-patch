using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;

namespace SealSaveEditor
{
    public sealed class ItemGenerationRules
    {
        // Coefficients are the original generation inputs, not already rolled item stats.
        public int Grade { get; set; }
        public double? AttributeParameter { get; set; }
        public double? AttributeExponent { get; set; }
        public double? AttackCoefficient { get; set; }
        public double? DefenseCoefficient { get; set; }
        public double? LevelRequirementCoefficient { get; set; }
        public double? GenerationGrade { get; set; }
        public double? GenerationChance { get; set; }
        public double? PriceCoefficient { get; set; }
        public double? AttributeRequirementCoefficient { get; set; }
        public int SocketCount { get; set; }
        public int MinimumRandomCount { get; set; }
        public double MinimumRandomChance { get; set; }
        public int MaximumRandomCount { get; set; }
        public double MaximumRandomChance { get; set; }
    }

    public sealed class ItemAttributeRule
    {
        public string Alias { get; set; }
        public string Description { get; set; }
        public int AttributeId { get; set; }
        public string CalculationMode { get; set; }
        public int MinimumLevel { get; set; }
        public int MaximumLevel { get; set; }
        public double BaseValue { get; set; }
        public double Increment { get; set; }
        public double InitialLevelRequirement { get; set; }
        public double LevelRequirementIncrement { get; set; }
        public double InitialAttributeRequirement { get; set; }
        public double AttributeRequirementIncrement { get; set; }
        public double EvaluationCorrection { get; set; }
        public double EvaluationIncrement { get; set; }
        public double MaximumValue { get; set; }
        public double MaximumLevelRequirement { get; set; }
        public double MaximumAttributeRequirement { get; set; }
        public double MaximumPriceCorrection { get; set; }
        public string SourceFile { get; set; }
        public int SourceLine { get; set; }
        public string[] Columns { get; set; }
    }

    public sealed class ItemAttributeGroup
    {
        public string Alias { get; set; }
        public string Description { get; set; }
        public int Level { get; set; }
        public double Chance { get; set; }
        public int Element { get; set; }
        public List<int> AllowedItemTypes { get; set; }
        public List<string> AttributeAliases { get; set; }
        public List<ItemAttributeRule> Attributes { get; set; }
        public List<ItemAttributeGroup> ChildGroups { get; set; }
        public string SourceFile { get; set; }
        public int SourceLine { get; set; }
        public string[] Columns { get; set; }
    }

    public sealed class ItemAffixChoice
    {
        public string GroupAlias { get; set; }
        public double Chance { get; set; }
        public ItemAttributeGroup Group { get; set; }
    }

    public sealed class ItemSetBonusRule
    {
        public int SetId { get; set; }
        public string Description { get; set; }
        public int Element { get; set; }
        public int FullSetCount { get; set; }
        public int HalfSetCount { get; set; }
        public int MainPart { get; set; }
        public string AttributeGroupAlias { get; set; }
        public string SourceFile { get; set; }
        public int SourceLine { get; set; }
    }

    public sealed class SpecialItemDefinition
    {
        // s_Item is a separate illustration/description table, not an ordinary factory row.
        public string Alias { get; set; }
        public string Name { get; set; }
        public string Scene { get; set; }
        public int IconId { get; set; }
        public int LargeImageId { get; set; }
        public int DescriptionId { get; set; }
        public string Description { get; set; }
        public string SourceFile { get; set; }
        public int SourceLine { get; set; }
    }

    public sealed class ItemTemplate
    {
        public ItemDefinition Definition { get; set; }
        public string Alias { get { return Definition.Alias; } }
        public string Name { get { return Definition.Name; } }
        public int TypeId { get { return Definition.TypeId; } }
        public string TypeName { get { return Definition.TypeName; } }
        public string NativeTypeName { get { return Columns != null && Columns.Length > 1 ? Columns[1] : Definition.TypeName; } }
        public int Level { get { return Definition.Level; } }
        public int Element { get { return Definition.Element; } }
        public string SourceFile { get { return Definition.SourceFile; } }
        public int SourceLine { get { return Definition.SourceLine; } }
        public string[] Columns { get; set; }
        public string Note { get; set; }
        public int InfoId { get; set; }
        public int IntroductionId { get; set; }
        public int SetId { get; set; }
        public int CraftNameId { get; set; }
        public string IconKey { get; set; }
        public List<int> IconVariants { get; set; }
        public int GroundIconId { get; set; }
        public Dictionary<string, List<string>> EquipmentModels { get; set; }
        public string PrefixKey { get; set; }
        public List<string> PrefixNames { get; set; }
        public ItemGenerationRules Generation { get; set; }
        public List<ItemAffixChoice> FixedChoices { get; set; }
        public List<ItemAttributeGroup> RandomCandidates { get; set; }
        public ItemAttributeGroup HiddenGroup { get; set; }
        public ItemAttributeGroup SynergyGroup { get; set; }
        public List<ItemSetBonusRule> SetBonuses { get; set; }
        public override string ToString() { return Name + " [" + Alias + "]"; }
    }

    /// <summary>Runtime reader of every installed item generation table. It contains no original game assets.</summary>
    public sealed class ItemTemplateCatalog
    {
        public string GameDirectory { get; private set; }
        public List<ItemTemplate> Templates { get; private set; }
        public List<SpecialItemDefinition> SpecialItems { get; private set; }
        public Dictionary<string, ItemAttributeGroup> AttributeGroups { get; private set; }
        public Dictionary<string, ItemAttributeRule> AttributeRules { get; private set; }
        public List<ItemSetBonusRule> SetBonusRules { get; private set; }
        public Dictionary<string, List<string>> ItemTypeGroups { get; private set; }
        public Dictionary<int, int> SourceRowCounts { get; private set; }
        public List<string> Warnings { get; private set; }

        private ItemTemplateCatalog()
        {
            Templates = new List<ItemTemplate>(); SpecialItems = new List<SpecialItemDefinition>();
            AttributeGroups = new Dictionary<string, ItemAttributeGroup>(StringComparer.OrdinalIgnoreCase);
            AttributeRules = new Dictionary<string, ItemAttributeRule>(StringComparer.OrdinalIgnoreCase);
            SetBonusRules = new List<ItemSetBonusRule>();
            ItemTypeGroups = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
            SourceRowCounts = new Dictionary<int, int>(); Warnings = new List<string>();
        }

        public static ItemTemplateCatalog Load(string gameDirectory)
        { return Load(gameDirectory, GameCatalog.Load(gameDirectory)); }

        public static ItemTemplateCatalog Load(string gameDirectory, GameCatalog catalog)
        {
            if (catalog == null) throw new ArgumentNullException("catalog");
            var result = new ItemTemplateCatalog();
            result.GameDirectory = Path.GetFullPath(gameDirectory);
            string data = Path.Combine(result.GameDirectory, "Data");
            result.ReadAttributeRules(Path.Combine(data, "attribT.txt"));
            result.ReadAttributeGroups(Path.Combine(data, "attribU.txt"));
            result.ReadSetRules(Path.Combine(data, "attribS.txt"));
            Dictionary<string, List<string>> pictures = ReadVariants(Path.Combine(data, "item_pic.txt"));
            Dictionary<string, List<string>> models = ReadVariants(Path.Combine(data, "item_equipstr.txt"));
            Dictionary<string, List<string>> prefixes = ReadVariants(Path.Combine(data, "item_prefix.txt"));
            foreach (Row row in Rows(Path.Combine(data, "item_type.txt")))
            {
                if (row.Values.Length < 2 || row.Values[0] == "类型名" || String.IsNullOrWhiteSpace(row.Values[0])) continue;
                result.ItemTypeGroups[row.Values[0]] = row.Values.Skip(2).Where(x => !String.IsNullOrWhiteSpace(x)).ToList();
            }
            var aliases = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (string path in Directory.GetFiles(data, "item_*.txt").OrderBy(x => x, StringComparer.OrdinalIgnoreCase))
            {
                int table;
                if (!Int32.TryParse(Path.GetFileNameWithoutExtension(path).Substring(5), out table)) continue;
                int count = 0;
                foreach (Row row in Rows(path))
                {
                    int type;
                    if (!Int32.TryParse(At(row.Values, 0), out type) || String.IsNullOrWhiteSpace(At(row.Values, 3))) continue;
                    count++;
                    string alias = row.Values[3];
                    if (!aliases.Add(alias)) { result.Warnings.Add("Duplicate item alias " + alias + " at " + Path.GetFileName(path) + ":" + row.Line); continue; }
                    ItemDefinition definition = catalog.FindItem(alias);
                    if (definition == null)
                    {
                        int nameId = Int(At(row.Values, 4)); string name;
                        if (!catalog.LocalizedNames.TryGetValue(nameId, out name)) name = alias;
                        definition = new ItemDefinition { Alias = alias, Name = name, OriginalName = At(row.Values, 2),
                            NameId = nameId, TypeId = type, TypeName = At(row.Values, 1), Slot = "",
                            Level = Int(At(row.Values, 8)), Element = Int(At(row.Values, 11)),
                            SourceFile = Path.GetFileName(path), SourceLine = row.Line, RawFields = new Dictionary<string, string>() };
                        GameCatalog.ApplyChineseName(definition, At(row.Values, 2));
                    }
                    var item = new ItemTemplate { Definition = definition, Columns = Pad(row.Values, 46),
                        Note = At(row.Values, 2), InfoId = Int(At(row.Values, 5)), IntroductionId = Int(At(row.Values, 6)),
                        SetId = Int(At(row.Values, 21)), CraftNameId = Int(At(row.Values, 20)), IconKey = At(row.Values, 39),
                        GroundIconId = Int(At(row.Values, 40)), EquipmentModels = new Dictionary<string, List<string>>(),
                        PrefixKey = At(row.Values, 19), IconVariants = new List<int>(), PrefixNames = new List<string>(),
                        FixedChoices = new List<ItemAffixChoice>(), RandomCandidates = new List<ItemAttributeGroup>() };
                    item.Generation = new ItemGenerationRules { Grade = Int(item.Columns[7]),
                        AttributeParameter = Number(item.Columns[9]), AttributeExponent = Number(item.Columns[10]),
                        AttackCoefficient = Number(item.Columns[12]), DefenseCoefficient = Number(item.Columns[13]),
                        LevelRequirementCoefficient = Number(item.Columns[14]), GenerationGrade = Number(item.Columns[15]),
                        GenerationChance = Number(item.Columns[16]), PriceCoefficient = Number(item.Columns[17]),
                        AttributeRequirementCoefficient = Number(item.Columns[18]), SocketCount = Int(item.Columns[22]),
                        MinimumRandomCount = Int(item.Columns[23]), MinimumRandomChance = Number(item.Columns[24]) ?? 0,
                        MaximumRandomCount = Int(item.Columns[25]), MaximumRandomChance = Number(item.Columns[26]) ?? 0 };
                    List<string> variants;
                    if (pictures.TryGetValue(item.IconKey, out variants)) item.IconVariants = variants.Select(Int).ToList();
                    if (prefixes.TryGetValue(item.PrefixKey, out variants)) item.PrefixNames = variants.Select(x => NameOf(catalog, x)).ToList();
                    string[] classes = { "游侠", "巫师", "祭司", "刀客", "兽人" };
                    for (int c = 0; c < classes.Length; c++)
                        if (!String.IsNullOrEmpty(item.Columns[41 + c]) && models.TryGetValue(item.Columns[41 + c], out variants))
                            item.EquipmentModels[classes[c]] = new List<string>(variants);
                    item.HiddenGroup = result.FindGroup(item.Columns[27]); item.SynergyGroup = result.FindGroup(item.Columns[28]);
                    for (int c = 29; c <= 37; c += 2)
                        if (!String.IsNullOrEmpty(item.Columns[c])) item.FixedChoices.Add(new ItemAffixChoice {
                            GroupAlias = item.Columns[c], Chance = Number(item.Columns[c + 1]) ?? 0, Group = result.FindGroup(item.Columns[c]) });
                    item.RandomCandidates = result.AttributeGroups.Values.Where(g => g.Alias.StartsWith("Ur_", StringComparison.OrdinalIgnoreCase)
                        && g.AllowedItemTypes.Contains(item.TypeId)
                        && (g.Element == 0 || g.Element == 6 || item.Element == 6 || g.Element == item.Element)).ToList();
                    item.SetBonuses = result.SetBonusRules.Where(x => x.SetId == item.SetId && item.SetId != 0).ToList();
                    result.Templates.Add(item);
                }
                result.SourceRowCounts[table] = count;
            }
            foreach (Row row in Rows(Path.Combine(data, "s_Item.txt")))
            {
                int icon;
                if (!Int32.TryParse(At(row.Values, 1), out icon) || String.IsNullOrWhiteSpace(At(row.Values, 3))) continue;
                int info = Int(At(row.Values, 5)); string description;
                if (!catalog.SkillDescriptions.TryGetValue(info, out description)) description = "";
                result.SpecialItems.Add(new SpecialItemDefinition { Scene = At(row.Values, 0), IconId = icon, Name = At(row.Values, 2),
                    Alias = At(row.Values, 3), LargeImageId = Int(At(row.Values, 4)), DescriptionId = info,
                    Description = GameCatalog.PlainGameText(description), SourceFile = "s_Item.txt", SourceLine = row.Line });
            }
            foreach (ItemTemplate item in result.Templates)
            {
                foreach (ItemAffixChoice choice in item.FixedChoices)
                    if (choice.Group == null) result.Warnings.Add(item.Alias + ": missing attribute group " + choice.GroupAlias);
                if (item.IconVariants.Count == 0) result.Warnings.Add(item.Alias + ": missing icon variants " + item.IconKey);
            }
            return result;
        }

        public ItemTemplate Find(string alias)
        { return Templates.FirstOrDefault(x => String.Equals(x.Alias, alias, StringComparison.OrdinalIgnoreCase)); }

        public ItemInitialState CreateInitialState(ItemTemplate template, int generationLevel, int seed)
        { return ItemFactory.Create(this, template, generationLevel, seed); }

        public ItemAttributeGroup FindGroup(string alias)
        { ItemAttributeGroup group; return alias != null && AttributeGroups.TryGetValue(alias, out group) ? group : null; }

        public string Describe(ItemTemplate item, bool chinese)
        {
            if (item == null) return "";
            var text = new StringBuilder();
            text.AppendLine(item.Definition.GetDisplayName(chinese));
            text.AppendLine(item.Definition.GetNameDescription(chinese));
            text.AppendLine((chinese ? "类别：" : "Category: ") + (chinese ? item.NativeTypeName : GameCatalog.TypeName(item.TypeId)));
            text.AppendLine((chinese ? "基础等级：" : "Base level: ") + item.Level + (chinese ? "；五行：" : "; Element: ") + DescribeElement(item.Element, chinese));
            text.AppendLine(chinese ? "此处显示原版模板与生成规则。随机抽取、隐藏属性、相生属性和套装效果分别列出，并非全部同时生效。"
                : "These are the original template and generation rules. Random rolls, hidden bonuses, elemental synergy and set effects are listed separately; they are not all active at once.");
            text.AppendLine();
            text.AppendLine(chinese ? "基础生成参数" : "Base generation inputs");
            AppendRaw(text, chinese ? "宝石孔数量" : "Gem socket count", At(item.Columns, 22), chinese);
            Append(text, chinese ? "攻击力生成系数" : "Attack generation coefficient", item.Generation.AttackCoefficient);
            Append(text, chinese ? "防御力生成系数" : "Defense generation coefficient", item.Generation.DefenseCoefficient);
            Append(text, chinese ? "等级需求生成系数" : "Level requirement coefficient", item.Generation.LevelRequirementCoefficient);
            Append(text, chinese ? "五行属性需求生成系数" : "Element requirement coefficient", item.Generation.AttributeRequirementCoefficient);
            Append(text, chinese ? "价格生成系数" : "Price coefficient", item.Generation.PriceCoefficient);
            Append(text, chinese ? "属性参数" : "Attribute parameter", item.Generation.AttributeParameter);
            Append(text, chinese ? "属性指数" : "Attribute exponent", item.Generation.AttributeExponent);
            Append(text, chinese ? "生成级别参数" : "Generation grade input", item.Generation.GenerationGrade);
            Append(text, chinese ? "生成概率参数" : "Generation chance input", item.Generation.GenerationChance);
            text.AppendLine(chinese ? "以上系数是生成器的输入，不等于物品生成后的最终面板数值。" : "These coefficients are generator inputs, not the item's final rolled statistics.");

            text.AppendLine();
            text.AppendLine(chinese ? "固定属性抽取（按原表从候选组中抽取一项）" : "Fixed bonus roll (one candidate group is selected by the original rules)");
            if (item.FixedChoices.Count == 0) text.AppendLine(chinese ? "  原表未指定。" : "  None specified in the source table.");
            foreach (ItemAffixChoice choice in item.FixedChoices)
                DescribeGroup(text, choice.Group, (chinese ? "抽取概率 " : "Selection chance ") + Format(choice.Chance) + "%", chinese);
            if (item.HiddenGroup != null)
            {
                text.AppendLine();
                DescribeGroup(text, item.HiddenGroup, chinese ? "隐藏属性（条件效果）" : "Hidden bonuses (conditional effects)", chinese);
            }
            if (item.SynergyGroup != null)
            {
                text.AppendLine();
                DescribeGroup(text, item.SynergyGroup, chinese ? "五行相生属性（条件效果）" : "Elemental synergy (conditional effects)", chinese);
            }

            text.AppendLine();
            text.AppendLine(chinese ? "随机属性生成规则" : "Random bonus generation rules");
            text.AppendLine("  " + (chinese ? "最少属性数输入：" : "Minimum count input: ") + AtOrBlank(item.Columns, 23, chinese)
                + (chinese ? "；对应概率输入：" : "; chance input: ") + AtOrBlank(item.Columns, 24, chinese));
            text.AppendLine("  " + (chinese ? "最多属性数输入：" : "Maximum count input: ") + AtOrBlank(item.Columns, 25, chinese)
                + (chinese ? "；对应概率输入：" : "; chance input: ") + AtOrBlank(item.Columns, 26, chinese));
            text.AppendLine("  " + (chinese ? "匹配物品类型的候选组：" : "Candidate groups matching the item type: ") + item.RandomCandidates.Count);
            text.AppendLine("  " + (chinese ? "候选组仍受五行、属性级别及概率约束，不代表必定生成。" : "Candidates remain subject to element, attribute-level and probability rules; their presence does not guarantee a roll."));
            foreach (ItemAttributeGroup group in item.RandomCandidates.OrderBy(x => x.Alias, StringComparer.OrdinalIgnoreCase))
                DescribeGroup(text, group, chinese ? "随机候选" : "Random candidate", chinese);

            if (item.SetBonuses.Count > 0)
            {
                text.AppendLine();
                text.AppendLine(chinese ? "套装条件与效果" : "Set conditions and effects");
                foreach (ItemSetBonusRule rule in item.SetBonuses)
                {
                    text.AppendLine("  " + (chinese ? "全套数量：" : "Full set count: ") + rule.FullSetCount
                        + (chinese ? "；半套数量：" : "; partial set count: ") + rule.HalfSetCount
                        + (chinese ? "；五行要求：" : "; element: ") + DescribeElement(rule.Element, chinese)
                        + (chinese ? "；主部位参数：" : "; main-part input: ") + rule.MainPart);
                    if (chinese && !String.IsNullOrWhiteSpace(rule.Description)) text.AppendLine("  " + rule.Description);
                    DescribeGroup(text, FindGroup(rule.AttributeGroupAlias), chinese ? "套装效果" : "Set effects", chinese);
                }
            }

            text.AppendLine();
            text.AppendLine(chinese ? "外观与名称模板" : "Appearance and name templates");
            text.AppendLine("  " + (chinese ? "背包图标：" : "Inventory icons: ") + DescribeVariants(item.IconVariants.Select(x => x.ToString(CultureInfo.InvariantCulture)).ToList()));
            text.AppendLine("  " + (chinese ? "地面图像：" : "Ground image: ") + item.GroundIconId);
            if (item.PrefixNames.Count > 0) text.AppendLine("  " + (chinese ? "名称前缀：" : "Name prefixes: ") + DescribeVariants(item.PrefixNames));
            foreach (var model in item.EquipmentModels)
                text.AppendLine("  " + DescribeClass(model.Key, chinese) + (chinese ? "装备外观：" : " equipment model: ") + DescribeVariants(model.Value));
            text.AppendLine();
            text.AppendLine(chinese ? "技术信息与原始表格字段" : "Technical information and source fields");
            text.AppendLine((chinese ? "物品代号：" : "Item alias: ") + item.Alias);
            text.AppendLine((chinese ? "来源：" : "Source: ") + item.SourceFile + ":" + item.SourceLine);
            string[] labels = (chinese
                ? "物品类型|类型名称|原表说明|物品代号|名称文字ID|信息文字ID|介绍文字ID|级别|基础等级|属性参数|属性指数|五行参数|攻击力系数|防御力系数|等级限制系数|生成级别|生成概率|价格系数|属性限制系数|前缀表代号|打造名称ID|套装ID|宝石孔数量|最少随机属性数|最少随机数概率|最多随机属性数|最多随机数概率|隐藏属性组|相生属性组|固定候选1|候选1概率|固定候选2|候选2概率|固定候选3|候选3概率|固定候选4|候选4概率|固定候选5|候选5概率|图标表代号|地面图像ID|游侠模型表|巫师模型表|祭司模型表|刀客模型表|兽人模型表"
                : "Item type|Original type name|Original note|Alias|Name text ID|Info text ID|Introduction text ID|Grade|Base level|Attribute parameter|Attribute exponent|Element input|Attack coefficient|Defense coefficient|Level requirement coefficient|Generation grade|Generation chance|Price coefficient|Attribute requirement coefficient|Prefix key|Craft name ID|Set ID|Gem sockets|Minimum random count|Minimum count chance|Maximum random count|Maximum count chance|Hidden group|Synergy group|Fixed candidate 1|Candidate 1 chance|Fixed candidate 2|Candidate 2 chance|Fixed candidate 3|Candidate 3 chance|Fixed candidate 4|Candidate 4 chance|Fixed candidate 5|Candidate 5 chance|Icon key|Ground image ID|Ranger model key|Wizard model key|Priest model key|Warrior model key|Beast model key").Split('|');
            for (int i = 0; i < labels.Length; i++) text.AppendLine("  " + labels[i] + ": " + AtOrBlank(item.Columns, i, chinese));
            return text.ToString().TrimEnd();
        }

        public string DescribeSpecial(SpecialItemDefinition item, bool chinese)
        {
            if (item == null) return "";
            var text = new StringBuilder();
            text.AppendLine(item.Name);
            text.AppendLine(chinese ? "剧情图文资料条目；此表不提供普通物品的生成模板。" : "Story illustration/reference entry; this table does not define an ordinary item-generation template.");
            if (!String.IsNullOrWhiteSpace(item.Description)) { text.AppendLine(); text.AppendLine(item.Description); }
            text.AppendLine();
            text.AppendLine((chinese ? "关联场景（原表）：" : "Associated scene (source text): ") + item.Scene);
            text.AppendLine((chinese ? "图标ID：" : "Icon ID: ") + item.IconId);
            text.AppendLine((chinese ? "大图ID：" : "Large image ID: ") + item.LargeImageId);
            text.AppendLine((chinese ? "文字ID：" : "Description text ID: ") + item.DescriptionId);
            text.AppendLine((chinese ? "资料代号：" : "Reference alias: ") + item.Alias);
            text.AppendLine((chinese ? "来源：" : "Source: ") + item.SourceFile + ":" + item.SourceLine);
            return text.ToString().TrimEnd();
        }

        private static void DescribeGroup(StringBuilder text, ItemAttributeGroup group, string title, bool chinese)
        { DescribeGroup(text, group, title, chinese, "  ", new HashSet<string>(StringComparer.OrdinalIgnoreCase)); }

        private static void DescribeGroup(StringBuilder text, ItemAttributeGroup group, string title, bool chinese, string indent, HashSet<string> ancestors)
        {
            if (group == null) { text.AppendLine(indent + title + (chinese ? "：未找到原始属性组。" : ": source group not found.")); return; }
            text.AppendLine(indent + title + (chinese && !String.IsNullOrWhiteSpace(group.Description) ? ": " + group.Description : "") + " [" + group.Alias + "]");
            if (!ancestors.Add(group.Alias)) { text.AppendLine(indent + (chinese ? "  原表存在循环引用，停止展开。" : "  Source reference cycle; expansion stopped.")); return; }
            if (group.Element == 6)
                text.AppendLine(indent + "  " + (chinese ? "按物品五行选取对应的一项子属性；下列为所有分支，不会同时全部应用。" : "The item's element selects one corresponding child bonus; the branches below are alternatives, not simultaneous bonuses."));
            if (group.Alias.StartsWith("Ur_", StringComparison.OrdinalIgnoreCase))
                text.AppendLine(indent + "  " + (chinese ? "候选属性级别：" : "Candidate attribute level: ") + group.Level
                    + (chinese ? "；生成概率输入：" : "; chance input: ") + Format(group.Chance)
                    + (chinese ? "；五行约束：" : "; element constraint: ") + DescribeElement(group.Element, chinese));
            foreach (ItemAttributeRule rule in group.Attributes)
            {
                string effect = FieldPresentation.EffectName(rule.AttributeId, GameCatalog.AttributeName(rule.AttributeId), chinese);
                text.AppendLine(indent + "  " + effect + " [" + rule.Alias + "]");
                if (chinese && !String.IsNullOrWhiteSpace(rule.Description)) text.AppendLine(indent + "    " + rule.Description);
                text.AppendLine(indent + "    " + (chinese ? "修正值输入：" : "Modifier input: ") + Format(rule.BaseValue)
                    + (chinese ? "；增量输入：" : "; increment input: ") + Format(rule.Increment)
                    + (chinese ? "；最高修正值输入：" : "; maximum modifier input: ") + AtOrBlank(rule.Columns, 14, chinese));
                text.AppendLine(indent + "    " + (chinese ? "属性级别范围：" : "Attribute level range: ") + rule.MinimumLevel + ".." + rule.MaximumLevel
                    + (chinese ? "；原表计算方式：" : "; source calculation mode: ") + AtOrBlank(rule.Columns, 3, chinese));
                string levelMode = rule.CalculationMode == "1"
                    ? (chinese ? "属性级别从 1 到最高级别随机抽取。" : "Attribute level is rolled from 1 through the maximum level.")
                    : rule.CalculationMode == "2"
                        ? (chinese ? "属性级别采用物品等级。" : "Attribute level uses the item's level.")
                        : (chinese ? "属性级别采用属性组传入级别，并限制在最低与最高级别之间。" : "Attribute level uses the incoming group level, clamped to the minimum and maximum.");
                text.AppendLine(indent + "    " + levelMode);
                text.AppendLine(indent + "    " + (chinese ? "等级需求输入（初值 / 增量 / 上限）：" : "Level requirement inputs (initial / increment / maximum): ")
                    + Format(rule.InitialLevelRequirement) + " / " + Format(rule.LevelRequirementIncrement) + " / " + AtOrBlank(rule.Columns, 15, chinese));
                text.AppendLine(indent + "    " + (chinese ? "属性需求输入（初值 / 增量 / 上限）：" : "Element requirement inputs (initial / increment / maximum): ")
                    + Format(rule.InitialAttributeRequirement) + " / " + Format(rule.AttributeRequirementIncrement) + " / " + AtOrBlank(rule.Columns, 16, chinese));
                text.AppendLine(indent + "    " + (chinese ? "评价修正输入（初值 / 增量 / 价格上限）：" : "Evaluation inputs (initial / increment / maximum price correction): ")
                    + Format(rule.EvaluationCorrection) + " / " + Format(rule.EvaluationIncrement) + " / " + AtOrBlank(rule.Columns, 17, chinese));
                text.AppendLine(indent + "    " + (chinese ? "属性ID：" : "Effect ID: ") + rule.AttributeId + "; " + rule.SourceFile + ":" + rule.SourceLine);
            }
            if (group.ChildGroups != null)
                foreach (ItemAttributeGroup child in group.ChildGroups) DescribeGroup(text, child, chinese ? "子属性组" : "Child bonus group", chinese, indent + "  ", ancestors);
            ancestors.Remove(group.Alias);
        }

        private static void Append(StringBuilder text, string label, double? value)
        { if (value.HasValue) text.AppendLine("  " + label + ": " + Format(value.Value)); }

        private static string Format(double value) { return value.ToString("G", CultureInfo.InvariantCulture); }
        private static string AtOrBlank(string[] columns, int index, bool chinese)
        { string value = columns == null ? "" : At(columns, index); return value.Length == 0 ? (chinese ? "未指定（原表为空）" : "unspecified (blank in source)") : value; }
        private static void AppendRaw(StringBuilder text, string label, string value, bool chinese)
        { text.AppendLine("  " + label + ": " + (String.IsNullOrWhiteSpace(value) ? (chinese ? "未指定（原表为空）" : "unspecified (blank in source)") : value)); }
        private static string DescribeElement(int element, bool chinese)
        {
            string[] zh = { "未指定", "金", "木", "水", "火", "土", "随机金、木、水、火、土之一" };
            string[] en = { "unspecified", "Metal", "Wood", "Water", "Fire", "Earth", "random Metal, Wood, Water, Fire or Earth" };
            return element >= 0 && element < zh.Length ? (chinese ? zh[element] : en[element]) : (chinese ? "原表参数 " : "source input ") + element;
        }
        private static string DescribeVariants(IList<string> values)
        { return values == null || values.Count == 0 ? "-" : String.Join("; ", values.Select((value, index) => index.ToString(CultureInfo.InvariantCulture) + "=" + (String.IsNullOrEmpty(value) ? "-" : value))); }
        private static string DescribeClass(string value, bool chinese)
        {
            if (chinese) return value;
            switch (value) { case "游侠": return "Ranger"; case "巫师": return "Wizard"; case "祭司": return "Priest"; case "刀客": return "Warrior"; case "兽人": return "Beast"; default: return value; }
        }

        private void ReadAttributeRules(string path)
        {
            foreach (Row row in Rows(path))
            {
                int id;
                if (!Int32.TryParse(At(row.Values, 2), out id) || String.IsNullOrWhiteSpace(At(row.Values, 0))) continue;
                string[] c = Pad(row.Values, 18);
                AttributeRules[c[0]] = new ItemAttributeRule { Alias = c[0], Description = c[1], AttributeId = id, CalculationMode = c[3],
                    MinimumLevel = Int(c[4]), MaximumLevel = Int(c[5]), BaseValue = Number(c[6]) ?? 0, Increment = Number(c[7]) ?? 0,
                    InitialLevelRequirement = Number(c[8]) ?? 0, LevelRequirementIncrement = Number(c[9]) ?? 0,
                    InitialAttributeRequirement = Number(c[10]) ?? 0, AttributeRequirementIncrement = Number(c[11]) ?? 0,
                    EvaluationCorrection = Number(c[12]) ?? 0, EvaluationIncrement = Number(c[13]) ?? 0,
                    MaximumValue = Number(c[14]) ?? 0, MaximumLevelRequirement = Number(c[15]) ?? 0,
                    MaximumAttributeRequirement = Number(c[16]) ?? 0, MaximumPriceCorrection = Number(c[17]) ?? 0,
                    SourceFile = Path.GetFileName(path), SourceLine = row.Line, Columns = c };
            }
        }

        private void ReadAttributeGroups(string path)
        {
            foreach (Row row in Rows(path))
            {
                string[] c = Pad(row.Values, 16);
                if (!c[0].StartsWith("U", StringComparison.Ordinal)) continue;
                var aliases = c.Skip(11).Where(x => !String.IsNullOrWhiteSpace(x)).ToList();
                var attributes = new List<ItemAttributeRule>();
                foreach (string alias in aliases)
                {
                    ItemAttributeRule rule;
                    if (AttributeRules.TryGetValue(alias, out rule)) attributes.Add(rule);
                    else if (!alias.StartsWith("U", StringComparison.Ordinal)) Warnings.Add(c[0] + ": missing attribute rule " + alias);
                }
                AttributeGroups[c[0]] = new ItemAttributeGroup { Alias = c[0], Description = c[1], Level = Int(c[2]),
                    Chance = Number(c[3]) ?? 0, Element = Int(c[4]), AllowedItemTypes = c.Skip(5).Take(6).Select(Int).Where(x => x != 0).ToList(),
                    AttributeAliases = aliases, Attributes = attributes, ChildGroups = new List<ItemAttributeGroup>(), SourceFile = Path.GetFileName(path), SourceLine = row.Line, Columns = c };
            }
            foreach (ItemAttributeGroup group in AttributeGroups.Values)
                foreach (string alias in group.AttributeAliases.Where(x => x.StartsWith("U", StringComparison.Ordinal)))
                {
                    ItemAttributeGroup child;
                    if (AttributeGroups.TryGetValue(alias, out child)) group.ChildGroups.Add(child);
                    else Warnings.Add(group.Alias + ": missing child group " + alias);
                }
        }

        private void ReadSetRules(string path)
        {
            foreach (Row row in Rows(path))
            {
                int id;
                if (!Int32.TryParse(At(row.Values, 0), out id)) continue;
                SetBonusRules.Add(new ItemSetBonusRule { SetId = id, Description = At(row.Values, 1), Element = Int(At(row.Values, 2)),
                    FullSetCount = Int(At(row.Values, 3)), HalfSetCount = Int(At(row.Values, 4)), MainPart = Int(At(row.Values, 5)),
                    AttributeGroupAlias = At(row.Values, 6), SourceFile = Path.GetFileName(path), SourceLine = row.Line });
            }
        }

        private static Dictionary<string, List<string>> ReadVariants(string path)
        {
            var result = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
            foreach (Row row in Rows(path))
            {
                int maximum;
                if (!Int32.TryParse(At(row.Values, 2), out maximum) || String.IsNullOrWhiteSpace(At(row.Values, 0))) continue;
                result[row.Values[0].Trim()] = Pad(row.Values, 3 + maximum + 1).Skip(3).Take(maximum + 1).ToList();
            }
            return result;
        }

        private sealed class Row { public string[] Values; public int Line; }
        private static IEnumerable<Row> Rows(string path)
        {
            if (!File.Exists(path)) throw new FileNotFoundException("An original item-generation table is missing.", path);
            string[] lines = GameCatalog.DecodeDataFile(path).Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
            for (int i = 0; i < lines.Length; i++)
                if (!String.IsNullOrWhiteSpace(lines[i])) yield return new Row { Values = lines[i].Split('\t'), Line = i + 1 };
        }
        private static string At(string[] values, int index) { return index < values.Length ? values[index].Trim() : ""; }
        private static string[] Pad(string[] values, int length)
        { var result = new string[Math.Max(length, values.Length)]; for (int i = 0; i < result.Length; i++) result[i] = At(values, i); return result; }
        private static int Int(string value)
        { int result; return Int32.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out result) ? result : 0; }
        private static double? Number(string value)
        { double result; return Double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out result) ? (double?)result : null; }
        private static string NameOf(GameCatalog catalog, string raw)
        { int id; string name; return Int32.TryParse(raw, out id) && catalog.LocalizedNames.TryGetValue(id, out name) ? GameCatalog.PlainGameText(name) : raw; }
    }
}
