using System;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace SealSaveEditor
{
    public sealed partial class MainForm
    {
        private void ShowSkillBooks(IWin32Window owner, CharacterRecord recipient)
        {
            if (scriptBooks == null || document == null || !ApplyInspectorDraft()) return;
            using (var dialog = new ScriptBookBrowser(scriptBooks, document.Characters, recipient, chinese))
            {
                dialog.AddItemRequested += AddGeneratedItem;
                dialog.ShowDialog(owner);
            }
        }

        private void ShowGameData()
        {
            if (String.IsNullOrEmpty(gameDirectory) || !ApplyInspectorDraft()) return;
            using (var dialog = new GameDataEditorForm(gameDirectory, chinese)) dialog.ShowDialog(this);
            LoadCatalog(); BindSkills(); BindItems(); BindActiveInspector(); UpdateState();
        }

        private void ShowCompanions()
        {
            if (document == null || actorBusy || !ApplyInspectorDraft()) return;
            SaveDocument target = document;
            string folder = gameDirectory;
            using (var dialog = new PartyDialog(target.GetCompanionAvailability, async delegate(CompanionDefinition definition)
            {
                actorBusy = true; UpdateState();
                try
                {
                    var initial = await Task.Run(delegate { return CompanionFactory.Create(folder, definition.ClassId, Environment.TickCount); });
                    if (IsDisposed || document != target) throw new InvalidOperationException("The selected save changed; no companion was added.");
                    CharacterRecord added = target.AddCompanion(initial);
                    AddedCompanionToken token = target.CaptureAddedCompanion(added);
                    undo.Push(new FieldChange { UndoAction = delegate { target.DetachAddedCompanion(token); }, RedoAction = delegate { target.ReattachAddedCompanion(token); } });
                    redo.Clear(); fieldInspector.Bind(null, "", "", catalog, chinese, false);
                    RefreshPartyViews(); RefreshRecords(); UpdateState();
                    SetStatus((chinese ? "已加入队友：" : "Added companion: ") + added.Name + (chinese ? "。保存后写入存档。" : ". Save to write the slot."));
                }
                finally { actorBusy = false; if (!IsDisposed) UpdateState(); }
            }, chinese)) dialog.ShowDialog(this);
        }

        private async void ApplyAllSkills(bool maximum)
        {
            var recipient = skillCharacter.SelectedItem as CharacterRecord;
            if (actorBusy || document == null || recipient == null || catalog == null || scriptBooks == null || !ApplyInspectorDraft()) return;
            SaveDocument target = document; GameCatalog definitions = catalog; ScriptItemCatalog books = scriptBooks;
            int[] attributes;
            try { attributes = Enumerable.Range(0, 256).Select(i => checked((int)recipient.Fields.Single(f => f.Key == "attr." + i.ToString(CultureInfo.InvariantCulture)).Value)).ToArray(); }
            catch (Exception ex) { ShowError(ex.Message); return; }
            actorBusy = true; UpdateState();
            SetStatus(chinese ? "正在按原版规则生成本职业技能及关联分支…" : "Generating this class's original skills and associated branches...");
            try
            {
                SkillTreePlan plan = await Task.Run(delegate { return SkillFactory.Create(definitions, books, attributes, maximum); });
                if (IsDisposed || document != target || !target.Characters.Contains(recipient)) return;
                var before = target.CaptureSkills(recipient);
                SkillEditResult result;
                try { result = target.ApplySkillPlan(recipient, plan); }
                catch { target.RestoreSkills(recipient, before); throw; }
                if (result.Changed)
                {
                    var after = target.CaptureSkills(recipient);
                    undo.Push(new FieldChange { UndoAction = delegate { target.RestoreSkills(recipient, before); }, RedoAction = delegate { target.RestoreSkills(recipient, after); } }); redo.Clear();
                }
                fieldInspector.Bind(null, "", "", catalog, chinese, false); BindSkills(); RefreshRecords(); UpdateState();
                SetStatus(chinese ? recipient.Name + "：新增主技能 " + result.AddedMainSkills + "，关联形态 " + result.AddedAssociatedSkills + "，增强分支 " + result.AddedComponents + "，升级 " + result.RaisedLevels + "。保存后生效；可整体撤销。"
                    : recipient.Name + ": added " + result.AddedMainSkills + " main skills, " + result.AddedAssociatedSkills + " variants and " + result.AddedComponents + " enhancements; raised " + result.RaisedLevels + " ranks. Save to apply; one Undo restores the previous tree.");
            }
            catch (Exception ex) { if (!IsDisposed) ShowError(ex.Message); }
            finally { actorBusy = false; if (!IsDisposed) UpdateState(); }
        }

        private void RefreshPartyViews()
        {
            if (document == null) return;
            var selectedCharacter = character.SelectedItem as CharacterRecord;
            var selectedSkillOwner = skillCharacter.SelectedItem as CharacterRecord;
            string selectedItemOwner = itemOwner.SelectedIndex > 0 ? Convert.ToString(itemOwner.SelectedItem) : null;
            bool previous = binding; binding = true;
            character.Items.Clear(); skillCharacter.Items.Clear(); itemOwner.Items.Clear();
            itemOwner.Items.Add(chinese ? "全部持有人" : "All owners");
            foreach (CharacterRecord entry in document.Characters) { character.Items.Add(entry); skillCharacter.Items.Add(entry); }
            foreach (string name in document.Characters.Select(c => c.Name).Concat(document.Items.Select(i => i.OwnerName)).Distinct()) itemOwner.Items.Add(name);
            character.SelectedItem = selectedCharacter != null && document.Characters.Contains(selectedCharacter) ? selectedCharacter : document.Characters.FirstOrDefault();
            skillCharacter.SelectedItem = selectedSkillOwner != null && document.Characters.Contains(selectedSkillOwner) ? selectedSkillOwner : document.Characters.FirstOrDefault();
            itemOwner.SelectedIndex = selectedItemOwner != null && itemOwner.Items.Contains(selectedItemOwner) ? itemOwner.Items.IndexOf(selectedItemOwner) : 0;
            activeCharacterIndex = character.SelectedIndex; activeSkillCharacterIndex = skillCharacter.SelectedIndex; activeOwnerIndex = itemOwner.SelectedIndex;
            binding = previous; BindCharacter(); BindSkills(); BindItems(); BindFields(locationFields, document.Location); BindActiveInspector();
        }

        private static int IconAttribute(System.Collections.Generic.IEnumerable<SaveField> fields)
        {
            SaveField field = fields == null ? null : fields.FirstOrDefault(f => f.Key == "attr.102" && f.Value > 0);
            if (field == null && fields != null) field = fields.FirstOrDefault(f => f.Key == "attr.101" && f.Value > 0);
            return field == null ? 0 : checked((int)field.Value);
        }
        private int ItemIconId(ItemRecord item) { return item == null ? 0 : IconAttribute(item.Fields); }
        private int SkillIconId(SkillView view)
        {
            if (view == null) return 0;
            if (catalog != null)
            {
                var skill = catalog.FindSkill(view.Alias); if (skill != null && skill.IconId > 0) return skill.IconId;
                var enhancement = catalog.FindSkillEnhancement(view.Alias); if (enhancement != null && enhancement.IconId > 0) return enhancement.IconId;
            }
            return IconAttribute(view.Fields);
        }
        private void BindObjectIcon(DataGridView grid)
        {
            int id = grid == itemFields ? ItemIconId(SelectedItem()) : grid == skillFields ? SkillIconId(selectedSkillView) : 0;
            fieldInspector.SetIcon(gameImages == null ? null : gameImages.Preview(id));
        }
    }
}
