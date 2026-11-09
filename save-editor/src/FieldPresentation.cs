using System;
using System.Collections.Generic;
using System.Globalization;

namespace SealSaveEditor
{
    // Presentation only: never changes a field's value, validation or access.
    public static class FieldPresentation
    {
        private sealed class Meaning
        {
            public string Chinese, English, ChineseDescription, EnglishDescription, Category;
            public Meaning(string zh, string en, string zhDescription, string enDescription, string category)
            { Chinese = zh; English = en; ChineseDescription = zhDescription; EnglishDescription = enDescription; Category = category; }
        }

        private static readonly Dictionary<string, Meaning> Meanings = BuildMeanings();

        public static string Label(SaveField field, bool chinese)
        {
            if (field == null) return "";
            int modifier, word;
            if (EffectPart(field, out modifier, out word))
            {
                if (word == 4) return Text(chinese, "附加属性类型", "Bonus attribute");
                if (word == 9) return Text(chinese, "属性加成数值", "Bonus amount");
                if (word == 10) return Text(chinese, "加成成长系数（高级）", "Bonus growth coefficient (advanced)");
                if (word == 11) return Text(chinese, "加成等级参数（高级）", "Bonus level parameter (advanced)");
                if (word == 12) return Text(chinese, "加成后备等级（高级）", "Bonus fallback level (advanced)");
                if (word == 8) return Text(chinese, "加成计算方式（高级）", "Bonus application mode (advanced)");
                return Text(chinese, "未确认参数", "Unverified parameter");
            }
            Meaning meaning;
            if (Meanings.TryGetValue(field.Key ?? "", out meaning)) return chinese ? meaning.Chinese : meaning.English;
            if (field.Key == "meta.Name") return Text(chinese, "名称（存档标识）", "Name in save");
            if (field.Key == "meta.Alias") return Text(chinese, "内部标识", "Internal identifier");
            if ((field.Key ?? "").StartsWith("string.", StringComparison.Ordinal))
            {
                string key = field.Key.Substring(7);
                if (key == "Name") return Text(chinese, "显示名称", "Display name");
                if (key == "CustomName") return Text(chinese, "自定义名称", "Custom name");
                if (key == "ProduceName") return Text(chinese, "打造名称", "Crafted name");
                if (key == "info" || key == "long" || key == "G_Info" || key == "G_Info2" || key == "G_Info3")
                    return Text(chinese, "说明文字", "Description text");
                return Text(chinese, "内部引用或状态", "Internal reference or state");
            }
            if (field.Key == "user.Current room") return Text(chinese, "当前地图", "Current map");
            return Text(chinese, "未确认字段", "Unverified field");
        }

        public static string Description(SaveField field, bool chinese)
        {
            if (field == null) return "";
            int modifier, word;
            if (EffectPart(field, out modifier, out word))
            {
                string name = EffectName(field, chinese);
                if (word == 4) return Text(chinese, "这条加成影响的能力，例如攻击力、生命上限或防御力。当前：", "The ability affected by this bonus, such as attack, maximum health or defense. Current: ") + name;
                if (word == 9) return Text(chinese, "调整“", "Changes the starting bonus for ") + name + Text(chinese, "”的加成数值。若装备还有成长系数，最终加成会随等级变化。", ". If the item also has a growth coefficient, the final bonus varies with level.");
                return Text(chinese, "装备效果的内部计算参数。其含义或编辑方式尚未完全验证，默认隐藏并保留原值。", "An internal calculation parameter for this effect. Its meaning or editing behavior is not fully verified, so it is hidden by default and preserved.");
            }
            Meaning meaning;
            if (Meanings.TryGetValue(field.Key ?? "", out meaning)) return chinese ? meaning.ChineseDescription : meaning.EnglishDescription;
            if (field.Key == "meta.Name") return Text(chinese, "存档中记录的对象名称。它可能含有游戏自己的颜色标记。", "The object's stored name, which may include the game's own color markup.");
            if (field.Key == "meta.Alias") return Text(chinese, "游戏用来识别角色、技能或物品的内部名称。它不是可随意修改的显示名称。", "The internal name used to identify a character, skill or item. It is not a free-form display name.");
            if ((field.Key ?? "").StartsWith("string.", StringComparison.Ordinal))
            {
                if (field.Editable) return Text(chinese, "显示给玩家的名称或说明文字。修改说明文字不会自动改变装备的实际加成。", "A player-facing name or description. Changing description text does not change the item's actual bonuses.");
                return Text(chinese, "游戏内部的引用、模型组合、配方、任务或其他状态数据。保留供高级查看，暂不开放直接编辑。", "An internal reference, model combination, recipe, quest or other state value. Preserved for advanced inspection; direct editing is unavailable.");
            }
            if (field.Key == "user.Current room") return Text(chinese, "该存档当前所在的地图标识。跨地图传送的关联数据尚未完成验证，因此只读。", "The current map identifier. Related cross-map transition data is not fully verified, so this value is read only.");
            return Text(chinese, "此字段的实际游戏含义尚未确认。请在高级视图查看原始名称和读取证据。", "The gameplay meaning of this field is not established. Its raw name and reading evidence are available in advanced view.");
        }

        public static string Group(SaveField field, bool chinese)
        {
            if (field == null) return "";
            string existing = field.Group ?? "";
            const string coordinates = " / Coordinates";
            if (existing.EndsWith(coordinates, StringComparison.Ordinal))
                return existing.Substring(0, existing.Length - coordinates.Length) + Text(chinese, " / 坐标", " / Coordinates");
            int modifier, word;
            if (EffectPart(field, out modifier, out word))
            {
                string prefix = existing.StartsWith("Skill: ", StringComparison.Ordinal) ? SkillGroup(existing, chinese) + " / " : "";
                return prefix + Text(chinese, "附加属性 ", "Bonus ") + (modifier + 1).ToString(CultureInfo.InvariantCulture) + " / " + EffectName(field, chinese);
            }
            if (existing.StartsWith("Skill: ", StringComparison.Ordinal)) return SkillGroup(existing, chinese);
            Meaning meaning;
            if (Meanings.TryGetValue(field.Key ?? "", out meaning)) return Category(meaning.Category, chinese);
            if (field.Key == "meta.Name" || field.Key == "string.Name" || field.Key == "string.CustomName" || field.Key == "string.ProduceName")
                return Text(chinese, "名称", "Names");
            if ((field.Key ?? "").StartsWith("string.", StringComparison.Ordinal) && field.Editable) return Text(chinese, "说明文字", "Descriptions");
            if (field.Key == "user.Current room") return Text(chinese, "地图与坐标", "Map and coordinates");
            return Text(chinese, "高级数据", "Advanced data");
        }

        public static bool IsCommon(SaveField field, string context)
        {
            if (field == null) return false;
            context = (context ?? "").ToLowerInvariant();
            string key = field.Key ?? "";
            int modifier, word;
            if (EffectPart(field, out modifier, out word))
            {
                if (context != "item" && context != "skill") return false;
                if (word != 4 && word != 9) return false;
                int id;
                return TryEffectId(field, out id) && IsKnownEffect(id);
            }
            if (context == "location") return key == "attr.97" || key == "attr.98" || key == "user.Current room";
            if (key == "string.Name" || key == "string.CustomName" || key == "string.ProduceName")
                return field.Text.Length > 0 && (context == "item" || context == "character" || context == "skill");
            if (context == "item")
                return key == "attr.148" || key == "attr.151" || key == "attr.152" || key == "attr.153" || (key == "attr.167" && field.Value > 0);
            if (context == "skill") return key == "attr.2" || key == "attr.173" || ((key == "attr.3" || key == "attr.4") && field.Value > 0);
            if (context != "character") return false;
            switch (key)
            {
                case "attr.2": case "attr.3": case "attr.4": case "attr.11": case "attr.12": case "attr.13": case "attr.14":
                case "attr.18": case "attr.19": case "attr.20": case "attr.21": case "attr.22": case "attr.23": case "attr.24": case "attr.25":
                case "attr.26": case "attr.27": case "attr.28": case "attr.29": case "attr.30": case "attr.31": case "attr.32": case "attr.33":
                case "attr.72": case "attr.73": case "attr.75": case "attr.76": case "attr.77": case "attr.78": case "attr.79":
                case "attr.80": case "attr.81": case "attr.82": case "attr.83": case "attr.84": case "attr.85": case "attr.86": case "attr.87": return true;
                default: return false;
            }
        }

        // Also useful when rendering the attribute-type selection as a name rather
        // than exposing its numeric ID in the ordinary view.
        public static string EffectName(SaveField field, bool chinese)
        {
            int id;
            if (!TryEffectId(field, out id)) return Text(chinese, "未确认属性", "Unverified attribute");
            return EffectName(id, GameCatalog.AttributeName(id), chinese);
        }

        public static string EffectName(int id, string fallbackEnglish, bool chinese)
        {
            if (!IsKnownEffect(id)) return chinese ? "未确认属性" : String.IsNullOrEmpty(fallbackEnglish) ? "Unverified attribute" : fallbackEnglish;
            if (!chinese) return String.IsNullOrEmpty(fallbackEnglish) ? GameCatalog.AttributeName(id) : fallbackEnglish;
            switch (id)
            {
                case 22: return "生命上限"; case 24: return "精气上限"; case 57: return "闪避值"; case 58: return "命中值";
                case 80: return "杀气获得速度"; case 82: return "解除异常状态"; case 85: return "复活";
                case 100: return "会心一击几率"; case 101: return "背后攻击"; case 102: return "致命攻击"; case 103: return "反弹伤害"; case 104: return "受伤后攻击加成";
                case 120: return "生命恢复"; case 122: return "杀敌恢复生命"; case 123: return "中毒攻击";
                case 140: return "精气恢复"; case 142: return "杀敌恢复精气"; case 143: return "冰冻攻击"; case 144: return "异常状态持续时间";
                case 160: return "移动速度"; case 161: return "攻击速度"; case 163: return "燃烧攻击"; case 164: return "绝对闪避";
                case 180: return "驱魔攻击"; case 181: return "眩晕攻击"; case 182: return "冲击抵抗"; case 183: return "异常状态抵抗";
                case 184: return "伤害减免"; case 185: return "伤害减免（套装）"; case 200: return "攻击力"; case 201: return "防御力";
                case 202: return "恢复生命"; case 203: return "恢复精气"; default: return "未确认属性";
            }
        }

        private static bool IsKnownEffect(int id)
        { return GameCatalog.AttributeName(id) != "Attribute " + id.ToString(CultureInfo.InvariantCulture); }

        private static bool EffectPart(SaveField field, out int modifier, out int word)
        {
            modifier = word = -1;
            string[] parts = (field.Key ?? "").Split('.');
            return parts.Length == 3 && parts[0] == "effect" && Int32.TryParse(parts[1], out modifier) && Int32.TryParse(parts[2], out word);
        }

        private static bool TryEffectId(SaveField field, out int id)
        {
            id = 0; int modifier, word;
            if (!EffectPart(field, out modifier, out word)) return false;
            if (word == 4) return Int32.TryParse(field.Text, NumberStyles.Integer, CultureInfo.InvariantCulture, out id);
            if (field.Node == null) return false;
            string key = "effect." + modifier.ToString(CultureInfo.InvariantCulture) + ".4";
            foreach (SaveField other in field.Node.Fields)
                if (other.Key == key) return Int32.TryParse(other.Text, NumberStyles.Integer, CultureInfo.InvariantCulture, out id);
            return false;
        }

        private static string Category(string category, bool chinese)
        {
            switch (category)
            {
                case "progress": return Text(chinese, "等级与经验", "Level and experience");
                case "resources": return Text(chinese, "生命与精气", "Health and mana");
                case "money": return Text(chinese, "金钱", "Money");
                case "elements": return Text(chinese, "当前五行属性", "Effective elemental attributes");
                case "base-elements": return Text(chinese, "基础五行属性", "Base elemental attributes");
                case "combat": return Text(chinese, "当前战斗能力", "Effective combat attributes");
                case "base-combat": return Text(chinese, "基础战斗能力", "Base combat attributes");
                case "item": return Text(chinese, "物品与装备", "Item and equipment");
                case "coordinates": return Text(chinese, "地图与坐标", "Map and coordinates");
                case "skill": return Text(chinese, "技能成长", "Skill progression");
                default: return Text(chinese, "高级数据", "Advanced data");
            }
        }

        private static string Text(bool chinese, string zh, string en) { return chinese ? zh : en; }
        private static string SkillGroup(string existing, bool chinese)
        { return chinese ? "技能：" + existing.Substring("Skill: ".Length) : existing; }

        private static Dictionary<string, Meaning> BuildMeanings()
        {
            var map = new Dictionary<string, Meaning>(StringComparer.Ordinal);
            Add(map, 2, "等级", "Level", "当前等级。调整经验后，游戏可能重新计算等级与升级条件。", "The current level. The game may recalculate progression after experience changes.", "progress");
            Add(map, 3, "经验值", "Experience", "当前记录的经验值。", "The currently stored experience value.", "progress");
            Add(map, 4, "升级还需经验", "Experience still needed", "到下一等级仍需要的经验。", "Experience still needed to reach the next level.", "progress");
            Add(map, 11, "当前生命", "Current health", "角色目前的生命值。", "The character's current health points.", "resources");
            Add(map, 12, "生命上限", "Maximum health", "包含当前属性与装备影响的生命上限。", "Maximum health including the current attributes and equipment.", "resources");
            Add(map, 13, "当前精气（法力）", "Current mana", "施放技能可消耗的精气。原游戏英文界面称为 Magic Points。", "Current magic points available for skills.", "resources");
            Add(map, 14, "精气上限", "Maximum mana", "包含当前属性与装备影响的精气上限。", "Maximum magic points including current attributes and equipment.", "resources");
            Add(map, 18, "金钱", "Money", "角色持有的货币。五行中的“金”是另一项属性。", "Currency held by the character. Metal is a separate elemental attribute.", "money");
            Add(map, 24, "未分配属性点", "Unspent attribute points", "可在角色界面分配到金、木、水、火、土的点数。", "Points available to allocate among the five elemental attributes.", "progress");
            Add(map, 25, "未分配技能点", "Unspent skill points", "可用于提升已学技能等级的点数。", "Points available to raise learned skill levels.", "progress");
            string[] zhElements = { "金", "木", "水", "火", "土" }, enElements = { "Metal", "Wood", "Water", "Fire", "Earth" };
            string[] zhEffects = { "影响攻击与攻击技能。", "影响生命与生命恢复。", "影响精气与辅助技能。", "影响命中与闪避。", "影响防御。" };
            string[] enEffects = { "Affects attack and offensive skills.", "Affects health and health recovery.", "Affects mana and secondary skills.", "Affects strike and dodge.", "Affects defense." };
            for (int i = 0; i < 5; i++)
            {
                Add(map, 19 + i, zhElements[i] + "（当前）", enElements[i] + " (effective)", "当前生效的五行属性，包含装备或效果的影响。" + zhEffects[i], "The effective elemental value, including equipment or effects. " + enEffects[i], "elements");
                Add(map, 75 + i, zhElements[i] + "（基础）", enElements[i] + " (base)", "五行属性的基础值，游戏以此结合装备与效果计算当前值。" + zhEffects[i], "The base elemental value used with equipment and effects to calculate the effective value. " + enEffects[i], "base-elements");
                Add(map, 27 + i, zhElements[i] + "系防御", enElements[i] + " defense", "当前对" + zhElements[i] + "系攻击的防御值。", "Current defense against " + enElements[i].ToLowerInvariant() + " attacks.", "combat");
                Add(map, 81 + i, zhElements[i] + "系基础防御", "Base " + enElements[i].ToLowerInvariant() + " defense", "参与最终防御计算的基础分量，可以为负数。", "The base component used to calculate final defense; it can be negative.", "base-combat");
            }
            Add(map, 26, "当前攻击力", "Effective attack power", "存档记录的当前攻击力。游戏可根据基础值、五行和装备重新计算。", "Stored effective attack power. The game can recalculate it from base values, elements and equipment.", "combat");
            Add(map, 32, "闪避值", "Dodge", "降低受到攻击时被命中的机会。这是闪避值，并非直接以百分比表示。", "Affects the ability to avoid incoming strikes. This is a rating, not a direct percentage.", "combat");
            Add(map, 33, "命中值", "Strike", "影响攻击命中敌人的机会。这是命中值，并非直接以百分比表示。", "Affects the chance of hitting an enemy. This is a rating, not a direct percentage.", "combat");
            Add(map, 72, "基础生命上限", "Base maximum health", "用于计算最终生命上限的基础值。", "The base value used to calculate maximum health.", "base-combat");
            Add(map, 73, "基础精气上限", "Base maximum mana", "用于计算最终精气上限的基础值。", "The base value used to calculate maximum magic points.", "base-combat");
            Add(map, 80, "基础攻击力", "Base attack power", "用于计算当前攻击力的基础值。", "The base value used to calculate effective attack power.", "base-combat");
            Add(map, 86, "基础闪避值", "Base dodge", "计算当前闪避时使用的基础值。", "The base component used to calculate dodge.", "base-combat");
            Add(map, 87, "基础命中值", "Base strike", "计算当前命中时使用的基础值。", "The base component used to calculate strike.", "base-combat");
            Add(map, 97, "横坐标", "Horizontal coordinate", "当前地图上的角色横坐标；此数值不代表屏幕像素。", "The character's horizontal position on the current map, not screen pixels.", "coordinates");
            Add(map, 98, "纵坐标", "Vertical coordinate", "当前地图上的角色纵坐标；此数值不代表屏幕像素。", "The character's vertical position on the current map, not screen pixels.", "coordinates");
            Add(map, 148, "鉴定状态", "Identification status", "物品是否已经鉴定的内部状态。数值含义在高级视图保留。", "The item's identification state. Raw numeric details remain available in advanced view.", "item");
            Add(map, 151, "可镶嵌数量", "Socket capacity", "物品记录的镶嵌容量。已镶嵌内容由其他记录管理。", "The stored socket capacity. Existing socketed contents are managed by other records.", "item");
            Add(map, 152, "使用等级要求", "Required character level", "使用或装备此物品所需的角色等级。", "The character level required to use or equip this item.", "item");
            Add(map, 153, "五行属性要求", "Required elemental attribute", "使用或装备此物品所需的五行属性数值。", "The elemental attribute value required to use or equip this item.", "item");
            Add(map, 167, "堆叠数量", "Stack amount", "仅适用于可堆叠物品。非堆叠物品可将此内部值记为零，并不代表物品不存在。", "Applies to stackable items. Non-stackable items can store zero here without being absent.", "item");
            Add(map, 173, "最高技能等级", "Maximum skill level", "该技能记录的等级上限。", "The maximum level stored for this skill.", "skill");
            return map;
        }

        private static void Add(Dictionary<string, Meaning> map, int id, string zh, string en, string zhDescription, string enDescription, string category)
        { map.Add("attr." + id.ToString(CultureInfo.InvariantCulture), new Meaning(zh, en, zhDescription, enDescription, category)); }
    }
}
