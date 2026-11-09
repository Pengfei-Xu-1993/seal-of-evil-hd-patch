using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;

namespace SealSaveEditor
{
    public sealed class SkillEditResult
    {
        public int AddedMainSkills { get; internal set; }
        public int AddedAssociatedSkills { get; internal set; }
        public int AddedComponents { get; internal set; }
        public int RaisedLevels { get; internal set; }
        public int SynchronizedAssociatedLevels { get; internal set; }
        public bool Changed { get { return AddedMainSkills + AddedAssociatedSkills + AddedComponents + RaisedLevels + SynchronizedAssociatedLevels > 0; } }
    }

    public sealed class SkillSnapshot
    {
        internal SaveDocument Document;
        internal CharacterRecord Owner;
        internal List<SaveNode> Roots;
        internal Dictionary<SaveNode, SkillNodeSnapshot> Nodes;
        internal Dictionary<SaveField, string> Values;
        internal HashSet<SaveField> CacheAuthorizations;
        internal Dictionary<SaveField, int> LevelAuthorizations;
    }

    internal sealed class SkillNodeSnapshot
    {
        internal List<SaveNode> Children;
        internal List<List<SaveNode>> Extra;
        internal int NodeIndex;
        internal RecordInfo Inspector;
    }

    public sealed partial class SaveDocument
    {
        private readonly HashSet<SaveField> skillCacheWrites = new HashSet<SaveField>();
        private readonly Dictionary<SaveField, int> skillLevelWrites = new Dictionary<SaveField, int>();
        private readonly Dictionary<SaveNode, RecordInfo> skillInspectors = new Dictionary<SaveNode, RecordInfo>();

        private void SkillRequireOwner(CharacterRecord owner)
        {
            if (owner == null || owner.Node == null || !Characters.Contains(owner) || !nodes.Contains(owner.Node)
                || (owner.Node.Type & 0x1001) != 0x1001 || owner.Node.Extra.Count != 3)
                throw new ArgumentException("Select a current party member belonging to this document.", "owner");
        }

        private static IEnumerable<SaveNode> SkillSubtree(SaveNode root)
        {
            yield return root;
            foreach (SaveNode child in root.Children) foreach (SaveNode node in SkillSubtree(child)) yield return node;
            foreach (List<SaveNode> list in root.Extra) foreach (SaveNode child in list) foreach (SaveNode node in SkillSubtree(child)) yield return node;
        }
        private static IEnumerable<SaveField> SkillFields(SaveNode node)
        { return node.Fields.Concat(new[] { node.NameField, node.AliasField }).Where(f => f != null).Distinct(); }
        private static SaveField SkillField(SaveNode node, string key)
        {
            SaveField field = node.Fields.SingleOrDefault(f => f.Modifier == null && f.Key == key);
            if (field == null) throw new InvalidDataException("The skill object has no verified " + key + " field.");
            return field;
        }
        private static string SkillParent(SaveNode node) { return SkillField(node, "string.SkillIndex").Text; }

        public SkillSnapshot CaptureSkills(CharacterRecord owner)
        {
            SkillRequireOwner(owner);
            var snapshot = new SkillSnapshot { Document = this, Owner = owner, Roots = new List<SaveNode>(owner.Node.Extra[1]),
                Nodes = new Dictionary<SaveNode, SkillNodeSnapshot>(), Values = new Dictionary<SaveField, string>(), CacheAuthorizations = new HashSet<SaveField>(),
                LevelAuthorizations = new Dictionary<SaveField, int>() };
            foreach (SaveNode node in snapshot.Roots.SelectMany(SkillSubtree).Distinct())
            {
                int index = nodes.IndexOf(node);
                if (index < 0 || index + 1 >= Inspector.Count) throw new InvalidOperationException("The skill inspector attachment is inconsistent.");
                RecordInfo entry;
                if (!skillInspectors.TryGetValue(node, out entry)) skillInspectors[node] = entry = Inspector[index + 1];
                snapshot.Nodes.Add(node, new SkillNodeSnapshot { Children = new List<SaveNode>(node.Children),
                    Extra = node.Extra.Select(l => new List<SaveNode>(l)).ToList(), NodeIndex = index, Inspector = entry });
                foreach (SaveField field in SkillFields(node))
                {
                    snapshot.Values[field] = field.Text;
                    if (skillCacheWrites.Contains(field)) snapshot.CacheAuthorizations.Add(field);
                    int authorizedLevel;
                    if (skillLevelWrites.TryGetValue(field, out authorizedLevel)) snapshot.LevelAuthorizations.Add(field, authorizedLevel);
                }
            }
            return snapshot;
        }

        public void RestoreSkills(CharacterRecord owner, SkillSnapshot snapshot)
        {
            SkillRequireOwner(owner);
            if (snapshot == null || snapshot.Document != this || snapshot.Owner != owner)
                throw new ArgumentException("The skill snapshot belongs to a different document or party member.", "snapshot");
            var current = owner.Node.Extra[1].SelectMany(SkillSubtree).Distinct().ToList();
            var target = new HashSet<SaveNode>(snapshot.Nodes.Keys);
            foreach (SaveNode node in current)
            {
                foreach (SaveField field in SkillFields(node)) { skillCacheWrites.Remove(field); skillLevelWrites.Remove(field); }
                if (target.Contains(node)) continue;
                int index = nodes.IndexOf(node);
                RecordInfo entry;
                if (skillInspectors.TryGetValue(node, out entry)) Inspector.Remove(entry);
                else if (index >= 0 && index + 1 < Inspector.Count) Inspector.RemoveAt(index + 1);
                nodes.Remove(node);
                foreach (SaveField field in SkillFields(node)) field.Active = false;
            }
            foreach (var pair in snapshot.Nodes)
            {
                SaveNode node = pair.Key; SkillNodeSnapshot state = pair.Value;
                node.Children.Clear(); node.Children.AddRange(state.Children);
                while (node.Extra.Count > state.Extra.Count) node.Extra.RemoveAt(node.Extra.Count - 1);
                while (node.Extra.Count < state.Extra.Count) node.Extra.Add(new List<SaveNode>());
                for (int i = 0; i < state.Extra.Count; i++) { node.Extra[i].Clear(); node.Extra[i].AddRange(state.Extra[i]); }
            }
            owner.Node.Extra[1].Clear(); owner.Node.Extra[1].AddRange(snapshot.Roots);
            foreach (var pair in snapshot.Nodes.OrderBy(p => p.Value.NodeIndex))
            {
                SaveNode node = pair.Key; SkillNodeSnapshot state = pair.Value;
                if (!nodes.Contains(node))
                {
                    int index = Math.Min(state.NodeIndex, nodes.Count); nodes.Insert(index, node);
                    Inspector.Insert(index + 1, state.Inspector);
                }
                skillInspectors[node] = state.Inspector;
                foreach (SaveField field in SkillFields(node)) field.Active = true;
            }
            foreach (var pair in snapshot.Values) pair.Key.RestoreWorkingText(pair.Value);
            foreach (SaveField field in snapshot.CacheAuthorizations) skillCacheWrites.Add(field);
            foreach (var pair in snapshot.LevelAuthorizations) skillLevelWrites.Add(pair.Key, pair.Value);
            SkillRefreshModels(owner); RefreshSummary();
        }

        public SkillEditResult ApplySkillPlan(CharacterRecord owner, SkillTreePlan plan)
        {
            SkillRequireOwner(owner);
            if (plan == null || plan.Skills == null || plan.Skills.Count == 0 || plan.Skills.Count > 256
                || SkillField(owner.Node, "attr.1").Value != plan.ClassId)
                throw new ArgumentException("A complete native skill plan for this character's class is required.", "plan");
            SkillSnapshot before = CaptureSkills(owner);
            int fieldStart = fields.Count;
            var staged = new Dictionary<string, SaveNode>(StringComparer.Ordinal);
            var planByAlias = new Dictionary<string, SkillNodePlan>(StringComparer.Ordinal);
            var result = new SkillEditResult();
            try
            {
                // Stage and validate all native payloads before touching an existing
                // skill, including all supplied derived ownership identities.
                foreach (SkillNodePlan row in plan.Skills)
                {
                    if (row == null || String.IsNullOrEmpty(row.Alias) || planByAlias.ContainsKey(row.Alias) || row.Components == null)
                        throw new InvalidDataException("Invalid or duplicate native skill identity.");
                    SaveNode node = SkillParseGenerated(row.BasePayload, owner.Node.Source, 0x10001, row.Alias);
                    if (SkillParent(node) != (row.MainSkillAlias ?? "") || SkillField(node, "attr.2").Value != row.Level
                        || SkillField(node, "attr.173").Value != row.NativeMaxLevel)
                        throw new InvalidDataException("Native skill payload identity/level differs from its plan.");
                    node.Extra.Add(new List<SaveNode>());
                    var componentAliases = new HashSet<string>(StringComparer.Ordinal);
                    foreach (SkillComponentPlan component in row.Components)
                    {
                        if (component == null || String.IsNullOrEmpty(component.Alias) || !componentAliases.Add(component.Alias))
                            throw new InvalidDataException("Invalid or duplicate native enhancement identity.");
                        SaveNode child = SkillParseGenerated(component.BasePayload, owner.Node.Source, 0x10003, component.Alias);
                        if (SkillParent(child) != row.Alias || SkillField(child, "attr.2").Value != -1 || SkillField(child, "attr.173").Value != -1)
                            throw new InvalidDataException("Native enhancement parent or placeholder level is invalid.");
                        node.Extra[0].Add(child);
                    }
                    staged.Add(row.Alias, node); planByAlias.Add(row.Alias, row);
                }
                foreach (SkillNodePlan row in plan.Skills)
                {
                    SkillNodePlan main;
                    if (row.IsAssociated && (!planByAlias.TryGetValue(row.MainSkillAlias, out main) || main.IsAssociated || row.Level != main.Level))
                        throw new InvalidDataException("The native skill plan has an invalid associated-root relationship.");
                }
                var existing = new Dictionary<string, SaveNode>(StringComparer.Ordinal);
                foreach (SaveNode node in owner.Node.Extra[1].Where(n => n.Type == 0x10001))
                {
                    if (existing.ContainsKey(node.Alias)) throw new InvalidOperationException("The selected character has duplicate skill aliases; resolve them before a bulk operation.");
                    existing.Add(node.Alias, node);
                }
                var attached = new HashSet<SaveNode>(); var refresh = new HashSet<SaveNode>();
                foreach (SkillNodePlan row in plan.Skills)
                {
                    SaveNode node;
                    if (!existing.TryGetValue(row.Alias, out node))
                    {
                        node = staged[row.Alias]; owner.Node.Extra[1].Add(node); existing.Add(row.Alias, node);
                        foreach (SaveNode added in SkillSubtree(node)) attached.Add(added);
                        if (row.IsAssociated) result.AddedAssociatedSkills++; else result.AddedMainSkills++;
                        result.AddedComponents += node.Extra[0].Count;
                    }
                    else
                    {
                        if (SkillParent(node) != (row.MainSkillAlias ?? "") || node.Extra.Count != 1)
                            throw new InvalidOperationException("An existing skill has a conflicting associated parent or unsupported attachment layout.");
                        var aliases = new HashSet<string>(node.Extra[0].Select(n => n.Alias), StringComparer.Ordinal);
                        foreach (SaveNode component in staged[row.Alias].Extra[0])
                            if (aliases.Add(component.Alias))
                            { node.Extra[0].Add(component); attached.Add(component); result.AddedComponents++; refresh.Add(node); }
                    }
                }
                // Every associated form inherits the main skill's actual level.
                // Main levels and custom fields are preserved by Learn All. Max
                // raises recognized roots without lowering an existing higher edit.
                foreach (SkillNodePlan row in plan.Skills.Where(r => !r.IsAssociated))
                {
                    SaveNode node = existing[row.Alias]; SaveField level = SkillField(node, "attr.2");
                    int desired = checked((int)level.Value);
                    if (plan.Maximum) desired = Math.Max(desired, row.NativeMaxLevel);
                    if (desired != level.Value) { SkillSetLevel(node, desired); result.RaisedLevels++; refresh.Add(node); }
                }
                foreach (SkillNodePlan row in plan.Skills.Where(r => r.IsAssociated))
                {
                    SaveNode node = existing[row.Alias]; SaveField level = SkillField(node, "attr.2");
                    int desired = checked((int)SkillField(existing[row.MainSkillAlias], "attr.2").Value);
                    bool isNew = attached.Contains(node);
                    if ((plan.Maximum || isNew) && level.Value != desired)
                    {
                        if (desired > level.Value) result.RaisedLevels++; else result.SynchronizedAssociatedLevels++;
                        SkillSetLevel(node, desired); refresh.Add(node);
                    }
                }
                // Dropping staged donor roots leaves only the new nodes actually
                // attached to this character in document-wide field/model lists.
                var stagedFields = fields.Skip(fieldStart).ToArray();
                foreach (SaveField field in stagedFields) if (!attached.Contains(field.Node)) field.Active = false;
                fields.RemoveRange(fieldStart, fields.Count - fieldStart);
                fields.AddRange(stagedFields.Where(f => attached.Contains(f.Node)));
                foreach (SaveNode node in attached)
                {
                    foreach (SaveField field in SkillFields(node)) field.Active = true;
                    node.OriginalLength = WriteNode(node).Length; nodes.Add(node);
                    RecordInfo entry = MakeItemInspector(node); skillInspectors[node] = entry; Inspector.Add(entry);
                }
                foreach (SaveNode node in refresh) SkillClearCaches(node);
                SkillRefreshModels(owner); RefreshSummary(); return result;
            }
            catch
            {
                RestoreSkills(owner, before);
                // Detached staged fields never become an alternate editable donor.
                foreach (SaveField field in fields.Skip(fieldStart)) field.Active = false;
                fields.RemoveRange(fieldStart, fields.Count - fieldStart);
                throw;
            }
        }

        private SaveNode SkillParseGenerated(byte[] payload, string source, int expectedType, string alias)
        {
            if (payload == null || payload.Length < 32 || payload.Length >= 0x4000) throw new InvalidDataException("Invalid native skill payload.");
            var node = new SaveNode { Blob = (byte[])payload.Clone(), Offset = -1, BlobOffset = -1, Source = source, IsNewItem = true };
            ParseBase(node);
            if (node.Type != expectedType || node.BaseSize != node.Blob.Length || node.Alias != alias
                || SkillField(node, "attr.232").Value != -1) throw new InvalidDataException("The native skill payload has an unsupported type, identity or runtime handle.");
            foreach (SaveField field in SkillFields(node)) field.Offset = -1;
            return node;
        }

        private void SkillClearCaches(SaveNode node)
        {
            foreach (SaveField field in node.Fields.Where(f => f.Key == "string.G_Info" || f.Key == "string.G_Info2"))
                if (field.Text.Length > 0)
                {
                    // A custom explicit cache edit is preserved. Only native
                    // descriptions that were unchanged by the user are cleared.
                    if (field.IsModified && !skillCacheWrites.Contains(field)) continue;
                    skillCacheWrites.Add(field); field.RestoreWorkingText("");
                }
        }

        private bool IsControlledSkillWrite(SaveNode node, SaveField field)
        {
            if (field == null || node == null || field.Node != node || !field.Active || node.Type != 0x10001
                || !Characters.Any(c => c.Node.Extra.Count > 1 && c.Node.Extra[1].Contains(node))) return false;
            if (skillCacheWrites.Contains(field) && field.Kind == "String" && field.Text.Length == 0
                && (field.Key == "string.G_Info" || field.Key == "string.G_Info2")) return true;
            int target;
            return skillLevelWrites.TryGetValue(field, out target) && target >= 1 && target <= 9
                && field.Kind == "Int32" && field.Key == "attr.2" && field.Name == "Level"
                && field.Text == target.ToString(CultureInfo.InvariantCulture)
                && Object.ReferenceEquals(SkillField(node, "attr.2"), field);
        }

        private void SkillSetLevel(SaveNode node, int value)
        {
            if (value < 1 || value > 9) throw new InvalidOperationException("A controlled skill level must stay within the native 1..9 representation.");
            SaveField field = SkillField(node, "attr.2");
            skillLevelWrites[field] = value;
            field.RestoreWorkingText(value.ToString(CultureInfo.InvariantCulture));
        }

        private void SkillRefreshModels(CharacterRecord owner)
        {
            owner.Skills.Clear();
            foreach (SaveNode root in owner.Node.Extra[1]) AddSkillFields(owner, root, "Skill: " + root.DisplayName);
            foreach (SaveNode node in owner.Node.Extra[1].SelectMany(SkillSubtree).Distinct())
            {
                RecordInfo entry;
                if (!skillInspectors.TryGetValue(node, out entry)) continue;
                RecordInfo updated = MakeItemInspector(node);
                entry.Name = updated.Name; entry.Offset = updated.Offset; entry.Length = updated.Length; entry.Detail = updated.Detail;
            }
        }
    }
}
