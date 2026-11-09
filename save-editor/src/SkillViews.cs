using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

namespace SealSaveEditor
{
    public sealed class SkillView
    {
        public string Id { get; internal set; }
        public string Name { get; internal set; }
        public string Role { get; internal set; }
        public string Description { get; internal set; }
        public string Alias { get; internal set; }
        public List<SaveField> Fields { get; internal set; }
        public List<SkillView> Children { get; internal set; }
        public bool IsVerified { get; internal set; }
        public Dictionary<string, string> FieldLabels { get; internal set; }
        public Dictionary<string, string> FieldDescriptions { get; internal set; }
        internal HashSet<string> BackendEditable;
        public override string ToString() { return Name; }
    }

    /// <summary>Views over actual serialized skill objects; no catalog-only objects are added.</summary>
    public static class SkillViews
    {
        private sealed class EffectMeaning
        {
            internal string Zh, En, UnitZh, UnitEn;
            internal bool Numeric;
            internal EffectMeaning(string zh, string en, string unitZh, string unitEn, bool numeric)
            { Zh = zh; En = en; UnitZh = unitZh; UnitEn = unitEn; Numeric = numeric; }
        }

        private static readonly Dictionary<int, EffectMeaning> Meanings = new Dictionary<int, EffectMeaning> {
            { 440, new EffectMeaning("攻击／投射物方案引用", "Attack / projectile scheme reference", "方案 ID", "scheme ID", false) },
            { 441, new EffectMeaning("最小施法距离", "Minimum casting distance", "游戏距离单位", "game distance units", true) },
            { 442, new EffectMeaning("最大施法距离", "Maximum casting distance", "游戏距离单位", "game distance units", true) },
            { 443, new EffectMeaning("效果持续时间", "Effect duration", "秒", "seconds", true) },
            { 446, new EffectMeaning("精气消耗", "Mana cost", "MP", "MP", true) },
            { 450, new EffectMeaning("施法速度／忙碌时间参数", "Casting speed / busy-time parameter", "单位未确认", "units unverified", false) },
            { 480, new EffectMeaning("技能显示名称引用", "Displayed skill name reference", "Name.dlg ID", "Name.dlg ID", false) },
            { 481, new EffectMeaning("动作方案引用", "Action scheme reference", "方案 ID", "scheme ID", false) },
            { 482, new EffectMeaning("技能图标引用", "Displayed skill icon reference", "资源 ID", "resource ID", false) },
            { 500, new EffectMeaning("技能攻击加成", "Skill attack bonus", "%", "%", true) },
            { 501, new EffectMeaning("吸取生命加成", "HP absorption bonus", "%", "%", true) },
            { 508, new EffectMeaning("驱魔攻击几率", "Exorcism attack chance", "%", "%", true) }
        };

        public static List<SkillView> Build(CharacterRecord character, GameCatalog catalog)
        { return Build(character, catalog, true); }

        public static List<SkillView> Build(CharacterRecord character, GameCatalog catalog, bool chinese)
        {
            var result = new List<SkillView>();
            if (character == null || character.Node == null || character.Node.Extra.Count < 2) return result;
            List<SaveNode> roots = character.Node.Extra[1];
            var byAlias = roots.GroupBy(n => n.Alias, StringComparer.Ordinal).ToDictionary(g => g.Key, g => g.ToList(), StringComparer.Ordinal);
            var views = new Dictionary<SaveNode, SkillView>();
            var parents = new Dictionary<SaveNode, SaveNode>();
            string actorKey = "party/" + Escape(character.Node.Alias);

            foreach (SaveNode root in roots)
            {
                string mainAlias = Text(root, "string.SkillIndex");
                if (String.IsNullOrEmpty(mainAlias) && catalog != null)
                {
                    List<SkillAssociationDefinition> links = catalog.SkillAssociations.Where(a => a.TargetSkillAlias == root.Alias).ToList();
                    if (links.Count == 1) mainAlias = links[0].MainSkillAlias;
                }
                List<SaveNode> matches;
                if (!String.IsNullOrEmpty(mainAlias) && mainAlias != root.Alias && byAlias.TryGetValue(mainAlias, out matches) && matches.Count == 1)
                    parents[root] = matches[0];
            }

            foreach (SaveNode root in roots)
            {
                bool associated = parents.ContainsKey(root);
                string key = actorKey + "/skill/" + Escape(root.Alias);
                // Aliases normally identify objects. Preserve identity if a malformed/older slot repeats one.
                if (byAlias[root.Alias].Count > 1) key += "/occurrence/" + byAlias[root.Alias].IndexOf(root);
                SkillView view = Create(root, key, catalog, chinese, false, associated);
                views[root] = view;
            }
            foreach (SaveNode root in roots)
            {
                SaveNode parent;
                if (parents.TryGetValue(root, out parent) && parent != root && !HasCycle(root, parents))
                    views[parent].Children.Add(views[root]);
                else result.Add(views[root]);
            }
            return result;
        }

        private static bool HasCycle(SaveNode node, Dictionary<SaveNode, SaveNode> parents)
        {
            var seen = new HashSet<SaveNode>();
            while (parents.ContainsKey(node)) { if (!seen.Add(node)) return true; node = parents[node]; }
            return false;
        }

        private static SkillView Create(SaveNode node, string id, GameCatalog catalog, bool zh, bool component, bool associated)
        {
            SkillDefinition definition = catalog == null ? null : catalog.FindSkill(node.Alias);
            SkillEnhancementDefinition enhancement = catalog == null ? null : catalog.FindSkillEnhancement(node.Alias);
            var view = new SkillView {
                Id = id, Alias = node.Alias, Fields = new List<SaveField>(node.Fields), Children = new List<SkillView>(),
                FieldLabels = new Dictionary<string, string>(StringComparer.Ordinal),
                FieldDescriptions = new Dictionary<string, string>(StringComparer.Ordinal),
                IsVerified = component ? enhancement != null : definition != null,
                BackendEditable = new HashSet<string>(node.Fields.Where(f => f.Editable).Select(f => f.Key), StringComparer.Ordinal)
            };
            // Keep all raw fields inspectable. Only fields whose role was established
            // in this node's actual context can be edited through the semantic view.
            foreach (SaveField field in node.Fields) field.Editable = false;
            if (node.Type != 0x10001 && node.Type != 0x10003)
            {
                view.Name = String.IsNullOrWhiteSpace(node.DisplayName) ? node.Alias : Plain(node.DisplayName);
                view.Role = Choose(zh, "尚未审核的附加记录", "Unaudited attached record");
                view.IsVerified = false;
                view.Description = Choose(zh, "此类型的执行作用尚未确认，因此不把它解释成可升级技能；全部原始字段只读。", "This type's execution role is unverified. It is not presented as a learnable skill; all raw fields are read only.")
                    + " type=0x" + node.Type.ToString("X") + " " + node.Alias;
                var attached = node.Children.Concat(node.Extra.SelectMany(x => x)).ToList();
                for (int i = 0; i < attached.Count; i++) view.Children.Add(Create(attached[i], id + "/attached/" + i, catalog, zh, attached[i].Type == 0x10003, false));
                return view;
            }
            view.Name = component ? ComponentName(node, enhancement, catalog, zh) : associated ? VariantName(node.Alias, zh) : Plain(node.DisplayName);
            if (String.IsNullOrWhiteSpace(view.Name)) view.Name = node.Alias;
            view.Role = component ? Choose(zh, "条件增强组件", "Conditional enhancement component") : associated ? Choose(zh, "主技能的关联变体", "Associated skill variant") : Choose(zh, "可学习的主技能", "Learnable main skill");

            var description = new StringBuilder();
            if (component)
            {
                description.AppendLine(Choose(zh,
                    "这是附属于技能的增强组件，不拥有独立的学习等级。原始 Level/LevelMax=-1 是不适用的占位值。引擎用所属技能的等级及角色五行属性判断其是否生效。",
                    "This is an enhancement attached to a skill, with no independent learned level. Raw Level/LevelMax=-1 are inapplicable placeholders. The engine gates it using its owning skill's level and the actor's five elements."));
                description.AppendLine(Choose(zh,
                    "相同组件定义可以在多个关联变体中各存一份。相同参数仍是同一种作用；这里按实际所属节点区分，编辑当前实例不会自动改写其它副本。",
                    "The same component definition can be stored separately under several variants. Equal parameters still have the same meaning; views distinguish actual owners, and editing this instance does not silently rewrite other copies."));
                ExplainConditions(view, node, description, zh);
                if (enhancement != null)
                {
                    if (!String.IsNullOrWhiteSpace(enhancement.Description)) description.AppendLine(Choose(zh, "定义说明：", "Definition note: ") + enhancement.Description);
                    if (catalog != null && enhancement.InfoId > 0)
                    {
                        string native = catalog.FindSkillDescription(enhancement.InfoId);
                        description.AppendLine(Choose(zh, "游戏原说明：", "Original game description: ") + (String.IsNullOrWhiteSpace(native) ? Choose(zh, "此说明记录为空。", "This description record is empty.") : native));
                    }
                    description.AppendLine("Data/skill_enhance.txt:" + enhancement.SourceLine);
                }
                if (!node.Fields.Any(f => f.Key.StartsWith("effect.", StringComparison.Ordinal)))
                    description.AppendLine(Choose(zh, "该存档节点没有数值效果记录；不能把它当作额外伤害或独立技能。", "This saved node has no modifier records; it must not be presented as extra damage or a separate spell."));
                SetReadOnly(node, "attr.2", "attr.173", "attr.147", "attr.230", "attr.231");
                view.FieldDescriptions["attr.2"] = view.FieldDescriptions["attr.173"] = Choose(zh, "本组件没有独立技能等级；-1 不表示未学习。", "This component has no independent skill rank; -1 does not mean unlearned.");
            }
            else
            {
                if (associated)
                {
                    description.AppendLine(Choose(zh,
                        "这是同一主技能的条件变体。加载器按 SkillIndex 别名绑定主技能；一键学习和满级会按原引擎规则同步关联等级，此单字段视图保持只读。",
                        "This is a conditional variant of the main skill. Loading binds it by the SkillIndex alias. The bulk Learn and Max actions synchronize associated ranks using the original engine's rules; this individual field remains read only."));
                    SetReadOnly(node, "attr.2", "attr.173");
                    Label(view, node, "attr.2", Choose(zh, "存储的变体等级（只读）", "Stored variant rank (read only)"), Choose(zh, "来自实际变体节点；不是组件等级。", "Read from this actual variant object; this is not a component rank."));
                    ExplainAssociation(node, catalog, description, zh);
                }
                else
                {
                    Label(view, node, "attr.2", Choose(zh, "学习等级", "Learned skill rank"), Choose(zh, "主技能自己的 Int32 等级。组件的门槛会读取所属技能等级。", "This main skill's own Int32 rank. Enhancement gates read their owning skill's rank."));
                    Label(view, node, "attr.173", Choose(zh, "技能等级上限", "Skill rank limit"), Choose(zh, "此主技能的 LevelMax；与组件的 -1 占位值不同。", "This main skill's LevelMax, distinct from component -1 placeholders."));
                    Enable(view, node, "attr.2"); Enable(view, node, "attr.173");
                }
                if (definition != null)
                {
                    string native = catalog.FindSkillDescription(definition.InfoId);
                    if (!String.IsNullOrWhiteSpace(native)) description.AppendLine(Choose(zh, "游戏原说明：", "Original game description: ") + native);
                    description.AppendLine("Data/" + System.IO.Path.GetFileName(definition.SourceFile) + ":" + definition.SourceLine);
                }
                // These are the skill's current derived/cached values. Effects below are the source parameters.
                string[] derived = { "attr.182", "attr.183", "attr.186", "attr.187", "attr.188", "attr.189", "attr.190" };
                string[] labelsZh = { "当前最小施法距离", "当前最大施法距离", "当前精气消耗", "当前体力消耗", "当前效果时限（原值）", "当前施法忙碌时间（原值）", "基础施法忙碌时间（原值）" };
                string[] labelsEn = { "Current minimum range", "Current maximum range", "Current mana cost", "Current stamina cost", "Current duration limit (raw)", "Current busy time (raw)", "Base busy time (raw)" };
                for (int i = 0; i < derived.Length; i++)
                {
                    SaveField field = Find(node, derived[i]);
                    if (field == null || field.Value < 0) continue;
                    field.Editable = false;
                    Label(view, node, derived[i], zh ? labelsZh[i] : labelsEn[i], Choose(zh, "这是引擎计算后的当前字段；请修改下方已确认的效果参数。", "This is a current engine-derived field; edit the verified source effect parameter below."));
                }
            }

            ExplainEffects(view, node, catalog, description, zh);
            string cached = Plain(Text(node, "string.G_Info"));
            if (!String.IsNullOrWhiteSpace(cached)) description.AppendLine(Choose(zh, "存档缓存说明（可能待引擎刷新）：", "Cached save description (may need engine refresh): ") + cached);
            description.AppendLine(Choose(zh, "实际节点：", "Actual object: ") + node.Alias + " | " + node.Source + " @0x" + node.Offset.ToString("X") + " | type 0x" + node.Type.ToString("X"));
            description.AppendLine(Choose(zh, "原始字段与未确认的运算/选择器保留在高级视图和存档检查器。", "Raw fields and unverified operators/selectors remain available in the advanced view and save inspector."));
            view.Description = description.ToString().Trim();

            // The skill's extra list holds the actual enhancement instances. Repeated aliases
            // under different variants remain separate instances, sharing a catalog definition.
            for (int list = 0; list < node.Extra.Count; list++)
                for (int index = 0; index < node.Extra[list].Count; index++)
                {
                    SaveNode child = node.Extra[list][index];
                    string childId = id + "/component/" + Escape(child.Alias) + "/" + list + "/" + index;
                    SkillView childView = Create(child, childId, catalog, zh, (child.Type & 0x10003) == 0x10003, false);
                    view.Children.Add(childView);
                }
            for (int index = 0; index < node.Children.Count; index++)
            {
                SaveNode child = node.Children[index];
                view.Children.Add(Create(child, id + "/child/" + Escape(child.Alias) + "/" + index, catalog, zh, (child.Type & 0x10003) == 0x10003, false));
            }
            return view;
        }

        private static void ExplainConditions(SkillView view, SaveNode node, StringBuilder description, bool zh)
        {
            Label(view, node, "attr.152", Choose(zh, "所属技能最低等级", "Minimum owning-skill rank"), Choose(zh, "0x100303E0 比较所属技能的 Level；不读取组件自己的 Level。0 表示当前记录没有正等级门槛。", "Gate 0x100303E0 compares the owning skill's Level, not this component's Level. Zero means no positive rank threshold in this record."));
            Enable(view, node, "attr.152");
            string[] keys = { "attr.224", "attr.225", "attr.226", "attr.227", "attr.228" };
            string[] namesZh = { "金", "木", "水", "火", "土" }, namesEn = { "Metal", "Wood", "Water", "Fire", "Earth" };
            bool ratio = Number(node, "attr.229") != 0;
            var required = new List<string>();
            for (int i = 0; i < keys.Length; i++)
            {
                SaveField field = Find(node, keys[i]); if (field == null) continue;
                Label(view, node, keys[i], (zh ? namesZh[i] + "属性门槛" : namesEn[i] + " requirement") + (ratio ? " (%)" : ""),
                    ratio ? Choose(zh, "ParamType 非零：检查该属性占五行总和的百分比。", "Nonzero ParamType checks this element as a percentage of the total five elements.") : Choose(zh, "ParamType=0：检查角色该项属性的绝对数值。", "ParamType=0 checks the actor's absolute element value."));
                Enable(view, node, keys[i]);
                if (field.Value > 0) required.Add((zh ? namesZh[i] : namesEn[i]) + " >= " + field.Text + (ratio ? "%" : ""));
            }
            description.AppendLine(Choose(zh, "实际条件：所属技能等级 >= ", "Actual gate: owning skill rank >= ") + Number(node, "attr.152") + "; " + (required.Count == 0 ? Choose(zh, "无正的五行门槛。", "no positive element thresholds.") : String.Join(", ", required.ToArray()) + "."));
            Label(view, node, "attr.147", Choose(zh, "存储的生效状态（计算值）", "Stored enabled state (calculated)"), Choose(zh, "由门槛检查重算，不能当作学习等级或独立开关。", "Recomputed by the gate; this is not a learned rank or an independent toggle."));
            Label(view, node, "attr.231", Choose(zh, "存储的应用次数（计算值）", "Stored application count (calculated)"), Choose(zh, "RecalcCount 是由属性与 RecalcCountMax 计算的次数，不是技能等级。", "RecalcCount is a computed application count limited by RecalcCountMax, not a skill rank."));
        }

        private static void ExplainAssociation(SaveNode node, GameCatalog catalog, StringBuilder description, bool zh)
        {
            string main = Text(node, "string.SkillIndex");
            if (!String.IsNullOrWhiteSpace(main)) description.AppendLine(Choose(zh, "绑定的主技能：", "Bound main skill: ") + main);
            if (catalog == null) return;
            foreach (SkillAssociationDefinition link in catalog.SkillAssociations.Where(a => a.TargetSkillAlias == node.Alias))
            {
                string element = ElementName(link.AttackElement, zh);
                description.AppendLine(Choose(zh, "关联定义条件：攻击元素／武器属性 ", "Association definition: attack / weapon element ") + element
                    + (link.RequiredStateIds.Count > 0 ? Choose(zh, "；同时需要状态 ID ", "; required state IDs ") + String.Join(", ", link.RequiredStateIds.Select(x => x.ToString(CultureInfo.InvariantCulture)).ToArray()) : "")
                    + " (Data/skill_associate.txt:" + link.SourceLine + ").");
            }
        }

        private static void ExplainEffects(SkillView view, SaveNode node, GameCatalog catalog, StringBuilder description, bool zh)
        {
            List<SaveField> ids = node.Fields.Where(f => f.Key.StartsWith("effect.", StringComparison.Ordinal) && f.Key.EndsWith(".4", StringComparison.Ordinal)).ToList();
            foreach (SaveField idField in ids)
            {
                string prefix = idField.Key.Substring(0, idField.Key.Length - 1);
                int effectId = (int)idField.Value;
                SaveField value = Find(node, prefix + "9");
                if (value == null) continue;
                EffectMeaning meaning; bool audited = Meanings.TryGetValue(effectId, out meaning);
                string label = audited ? (zh ? meaning.Zh : meaning.En) : EffectFallback(catalog, effectId);
                string units = audited ? (zh ? meaning.UnitZh : meaning.UnitEn) : "";
                string effectLine = label + " = " + value.Text + (units.Length > 0 ? " " + units : "") + " [ID " + effectId + "]";
                description.AppendLine(effectLine);
                idField.Editable = false; // Changing a semantic field to another property's ID changes its meaning.
                if (audited && meaning.Numeric)
                {
                    Label(view, node, value.Key, label + (units == "%" ? " (%)" : units.Length > 0 ? " (" + units + ")" : ""),
                        Choose(zh, "这是该节点真实效果记录的 Float32 基础参数 p0；不是角色基础整数属性，也不是重复的技能等级。", "This is Float32 base parameter p0 in this object's actual modifier record, not a base actor integer or a repeated skill rank.") + " " + effectLine);
                    Enable(view, node, value.Key);
                    SaveField growth = Find(node, prefix + "10");
                    if (growth != null && growth.Text != "0")
                        Label(view, node, growth.Key, Choose(zh, "每级增长系数 p1", "Per-rank growth coefficient p1"), Choose(zh, "引擎公式：p0 + p1 × (p2 + L)。L 为引擎选择的等级；p2=-1 时从等级1计零次增长。此处保留只读。", "Engine formula: p0 + p1 * (p2 + L). L is the engine-selected level; p2=-1 counts zero growth at rank 1. Kept read only here."));
                }
                else
                {
                    value.Editable = false;
                    view.FieldDescriptions[value.Key] = Choose(zh, "引用／未完全确认单位的控制参数，只读高级字段。", "Reference or control parameter with unverified units; advanced read-only field.") + " " + effectLine;
                    if (!audited) view.IsVerified = false;
                }
                foreach (SaveField raw in node.Fields.Where(f => f.Key.StartsWith(prefix, StringComparison.Ordinal)))
                    if (raw.Key != value.Key) raw.Editable = false;
                if (effectId == 450) description.AppendLine(Choose(zh, "原说明确认这是攻击速度增强，但参数单位尚未独立确认，不能标成毫秒。", "The original description confirms an attack-speed enhancement, but its parameter units are unverified and must not be labeled milliseconds."));
                if (effectId == 440) description.AppendLine(Choose(zh, "攻击方案可能含视觉与执行逻辑；额外视觉触发次数不能直接解释成伤害倍数或弹丸数量。", "The attack scheme can contain rendering and execution logic; extra visual triggers do not establish damage multipliers or real projectile counts."));
            }
        }

        private static string ComponentName(SaveNode node, SkillEnhancementDefinition definition, GameCatalog catalog, bool zh)
        {
            string alias = node.Alias;
            if (alias == "巫师_能量弹_1") return Choose(zh, "基础技能说明", "Base skill description");
            if (alias == "巫师_能量弹_1_木神诀激活") return Choose(zh, "木弹说明记录（原文为空）", "Wood-bolt description (empty source text)");
            if (alias == "巫师_能量弹_1_火神诀激活") return Choose(zh, "火球说明记录（原文为空）", "Fireball description (empty source text)");
            if (alias == "巫师_能量弹_2") return Choose(zh, "第二形态：攻击加成 +" + ModifierValue(node, 500) + "% 及名称／图标", "Second form: +" + ModifierValue(node, 500) + "% attack bonus and name / icon");
            if (alias == "巫师_能量弹_3") return Choose(zh, "第三形态：攻击加成 +" + ModifierValue(node, 500) + "% 及名称／图标", "Third form: +" + ModifierValue(node, 500) + "% attack bonus and name / icon");
            if (alias.EndsWith("木神诀加强", StringComparison.Ordinal))
                return Choose(zh, "吸血增强（木属性门槛 " + Number(node, "attr.225") + "）", "HP absorption boost (Wood requirement " + Number(node, "attr.225") + ")");
            if (alias.EndsWith("火神诀加强", StringComparison.Ordinal))
                return Choose(zh, "攻击速度增强（火属性门槛 " + Number(node, "attr.227") + "）", "Attack-speed boost (Fire requirement " + Number(node, "attr.227") + ")");
            if (alias.StartsWith("巫师_能量弹_2_", StringComparison.Ordinal) || alias.StartsWith("巫师_能量弹_3_", StringComparison.Ordinal))
            {
                string element = alias.Substring(alias.LastIndexOf('_') + 1);
                string stage = alias.StartsWith("巫师_能量弹_2_", StringComparison.Ordinal) ? "II" : "III";
                return Choose(zh, "形态 " + stage + " 攻击方案（" + element + "）", "Form " + stage + " attack scheme (" + TranslateElement(element) + ")");
            }
            if (definition != null && !String.IsNullOrWhiteSpace(definition.Description)) return definition.Description;
            return Choose(zh, "技能组件：", "Skill component: ") + alias;
        }

        private static string VariantName(string alias, bool zh)
        {
            if (alias == "巫师_木弹") return Choose(zh, "木弹变体：木系武器＋木咒状态", "Wood bolt: Wood weapon + Wood Cantrip state");
            if (alias == "巫师_火球") return Choose(zh, "火球变体：火系武器＋火咒状态", "Fireball: Fire weapon + Fire Cantrip state");
            string[] names = { "金", "木", "水", "火", "土" };
            foreach (string element in names)
                if (alias == "巫师_能量弹_" + element) return Choose(zh, element + "属性攻击变体", TranslateElement(element) + " attack-element variant");
            return Choose(zh, "关联变体：", "Associated variant: ") + alias;
        }

        private static void Label(SkillView view, SaveNode node, string key, string label, string detail)
        { if (Find(node, key) != null) { view.FieldLabels[key] = label; view.FieldDescriptions[key] = detail; } }
        private static void Enable(SkillView view, SaveNode node, string key)
        { SaveField field = Find(node, key); if (field != null && view.BackendEditable.Contains(key)) field.Editable = true; }
        private static string ModifierValue(SaveNode node, int attribute)
        {
            SaveField id = node.Fields.FirstOrDefault(f => f.Key.StartsWith("effect.", StringComparison.Ordinal) && f.Key.EndsWith(".4", StringComparison.Ordinal) && f.Value == attribute);
            if (id == null) return "?";
            return Text(node, id.Key.Substring(0, id.Key.Length - 1) + "9");
        }
        private static void SetReadOnly(SaveNode node, params string[] keys)
        { foreach (string key in keys) { SaveField field = Find(node, key); if (field != null) field.Editable = false; } }
        private static SaveField Find(SaveNode node, string key) { return node.Fields.FirstOrDefault(f => f.Key == key); }
        private static long Number(SaveNode node, string key) { SaveField field = Find(node, key); return field == null ? -1 : field.Value; }
        private static string Text(SaveNode node, string key) { SaveField field = Find(node, key); return field == null ? "" : field.Text; }
        private static string Choose(bool zh, string chinese, string english) { return zh ? chinese : english; }
        private static string Escape(string text) { return Uri.EscapeDataString(text ?? ""); }
        private static string Plain(string text) { return GameCatalog.PlainGameText(text ?? ""); }
        private static string EffectFallback(GameCatalog catalog, int id)
        { AttributeDefinition item = catalog == null ? null : catalog.FindAttribute(id); return item == null ? "Attribute " + id : item.Name; }
        private static string TranslateElement(string element)
        { switch (element) { case "金": return "Metal"; case "木": return "Wood"; case "水": return "Water"; case "火": return "Fire"; case "土": return "Earth"; default: return element; } }
        private static string ElementName(int element, bool zh)
        { string[] cn = { "未限定", "金", "木", "水", "火", "土", "其他／无属性" }, en = { "unspecified", "Metal", "Wood", "Water", "Fire", "Earth", "other / no element" }; return element >= 0 && element < cn.Length ? (zh ? cn[element] : en[element]) : element.ToString(CultureInfo.InvariantCulture); }
    }
}
