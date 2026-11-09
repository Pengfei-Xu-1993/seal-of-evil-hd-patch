using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows.Forms;

namespace SealSaveEditor
{
    public enum ItemTemplateAction { Browse, Replace, Add }

    public sealed class ItemTemplateBrowser : Form
    {
        private readonly ItemTemplateCatalog catalog;
        private readonly bool chinese;
        private readonly TextBox search = new TextBox();
        private readonly ComboBox category = new ComboBox();
        private readonly ListView list = new ListView();
        private readonly TextBox details = new TextBox();
        private readonly Label count = new Label();
        private readonly Button choose = new Button();
        private readonly Button generate = new Button();
        private readonly NumericUpDown level = new NumericUpDown();
        private readonly CheckBox preserve = new CheckBox();
        private readonly ComboBox recipient = new ComboBox();
        private readonly bool adding;
        private readonly string referenceCategory;
        public ItemTemplate SelectedTemplate { get; private set; }
        public ItemInitialState InitialState { get; private set; }
        public bool PreserveExistingModifiers { get { return preserve.Checked; } }
        public int GenerationLevel { get { return (int)level.Value; } }
        public CharacterRecord SelectedOwner { get { return recipient.SelectedItem as CharacterRecord; } }

        public ItemTemplateBrowser(ItemTemplateCatalog definitions, bool zh, bool allowReplacement)
            : this(definitions, zh, allowReplacement ? ItemTemplateAction.Replace : ItemTemplateAction.Browse, null, null) { }

        public ItemTemplateBrowser(ItemTemplateCatalog definitions, bool zh, ItemTemplateAction action, IList<CharacterRecord> owners, CharacterRecord initialOwner)
        {
            catalog = definitions; chinese = zh;
            adding = action == ItemTemplateAction.Add;
            bool allowReplacement = action != ItemTemplateAction.Browse;
            referenceCategory = zh ? "剧情物品图鉴（只读）" : "Story item references (read only)";
            Text = adding ? (zh ? "添加物品" : "Add item") : (zh ? "完整物品目录与生成模板" : "Complete item catalog and generation templates");
            Font = UiFonts.Create();
            Size = new Size(1160, 800); MinimumSize = new Size(950, 650);
            StartPosition = FormStartPosition.CenterParent;
            BackColor = Color.FromArgb(236, 232, 223);
            var root = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 3, ColumnCount = 1, Padding = new Padding(15) };
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 45));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, allowReplacement ? 95 : 48));
            var filter = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false };
            filter.Controls.Add(new Label { Text = zh ? "搜索名称或物品代号" : "Find a name or alias", AutoSize = true, Padding = new Padding(0, 5, 4, 0) });
            search.Width = 330; search.Name = "TemplateSearch";
            category.Width = 230; category.DropDownStyle = ComboBoxStyle.DropDownList;
            category.Items.Add(zh ? "全部类别" : "All categories");
            foreach (string type in catalog.Templates.Select(TemplateType).Distinct().OrderBy(x => x)) category.Items.Add(type);
            category.Items.Add(referenceCategory);
            category.SelectedIndex = 0;
            search.TextChanged += delegate { RefreshList(); };
            category.SelectedIndexChanged += delegate { RefreshList(); };
            filter.Controls.Add(search); filter.Controls.Add(category);
            root.Controls.Add(filter, 0, 0);
            var split = new SplitContainer { Size = new Size(1100, 600), Dock = DockStyle.Fill, SplitterDistance = 485, Panel1MinSize = 320, Panel2MinSize = 340 };
            list.Name = "ItemTemplates"; list.Dock = DockStyle.Fill;
            list.View = View.Details; list.FullRowSelect = true; list.MultiSelect = false; list.HideSelection = false; list.ShowItemToolTips = true;
            list.Columns.Add(zh ? "物品" : "Item", 235);
            list.Columns.Add(zh ? "类别" : "Category", 125);
            list.Columns.Add(zh ? "模板等级" : "Template level", 95);
            list.SelectedIndexChanged += delegate
            {
                SelectedTemplate = list.SelectedItems.Count == 0 ? null : list.SelectedItems[0].Tag as ItemTemplate;
                InitialState = null; choose.Enabled = false; generate.Enabled = SelectedTemplate != null;
                SpecialItemDefinition reference = list.SelectedItems.Count == 0 ? null : list.SelectedItems[0].Tag as SpecialItemDefinition;
                details.Text = SelectedTemplate != null ? catalog.Describe(SelectedTemplate, chinese) : reference != null ? DescribeReference(reference) : "";
                if (SelectedTemplate != null) level.Value = Math.Max(level.Minimum, Math.Min(level.Maximum, SelectedTemplate.Alias == "super_ww" ? 14 : SelectedTemplate.Level));
            };
            details.Dock = DockStyle.Fill; details.Multiline = true; details.ReadOnly = true;
            details.ScrollBars = ScrollBars.Vertical; details.BackColor = Color.FromArgb(250, 248, 243);
            details.BorderStyle = BorderStyle.None;
            split.Panel1.Controls.Add(list); split.Panel2.Controls.Add(details);
            root.Controls.Add(split, 0, 1);
            var footer = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 2, ColumnCount = 1 };
            if (allowReplacement)
            {
                var options = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false };
                if (adding)
                {
                    options.Controls.Add(new Label { Text = zh ? "放入谁的背包" : "Add to backpack", AutoSize = true, Padding = new Padding(0, 5, 0, 0) });
                    recipient.Name = "TemplateOwner"; recipient.Width = 200;
                    recipient.DropDownStyle = ComboBoxStyle.DropDownList; recipient.DisplayMember = "Name";
                    if (owners != null) foreach (CharacterRecord owner in owners) recipient.Items.Add(owner);
                    if (recipient.Items.Count > 0) recipient.SelectedItem = initialOwner != null && recipient.Items.Contains(initialOwner) ? initialOwner : recipient.Items[0];
                    options.Controls.Add(recipient);
                }
                options.Controls.Add(new Label { Text = zh ? "生成级别" : "Generation level", AutoSize = true, Padding = new Padding(0, 5, 0, 0) });
                level.Minimum = ItemFactory.MinimumGenerationLevel; level.Maximum = ItemFactory.MaximumGenerationLevel; level.Value = 1; level.Width = 90; level.Name = "TemplateLevel";
                level.ValueChanged += delegate { InitialState = null; choose.Enabled = false; if (SelectedTemplate != null) details.Text = catalog.Describe(SelectedTemplate, chinese); };
                preserve.Text = zh ? "保留当前物品的附加属性" : "Keep the current item's additional modifiers";
                preserve.AutoSize = true; preserve.Padding = new Padding(10, 4, 0, 0);
                options.Controls.Add(level); if (!adding) options.Controls.Add(preserve);
                footer.RowStyles.Add(new RowStyle(SizeType.Absolute, 40));
                footer.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
                footer.Controls.Add(options, 0, 0);
            }
            else { footer.RowStyles.Add(new RowStyle(SizeType.Absolute, 0)); footer.RowStyles.Add(new RowStyle(SizeType.Percent, 100)); }
            var actions = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false };
            generate.Name = "GenerateTemplatePreview"; generate.Text = zh ? "生成属性预览 / 重掷" : "Generate preview / reroll";
            generate.AutoSize = true; generate.Visible = allowReplacement;
            generate.Click += delegate { GeneratePreview(); };
            choose.Name = "UseItemTemplate"; choose.Text = adding ? (zh ? "添加预览中的物品" : "Add the previewed item") : (zh ? "替换为预览中的物品" : "Replace with the previewed item");
            choose.AutoSize = true; choose.BackColor = Color.FromArgb(232, 190, 104); choose.Visible = allowReplacement;
            choose.Click += delegate { if (InitialState != null && (!adding || SelectedOwner != null)) { DialogResult = DialogResult.OK; Close(); } };
            var export = new Button { Text = zh ? "导出完整目录与规则…" : "Export the complete catalog...", AutoSize = true };
            export.Click += delegate { ExportCatalog(); };
            count.AutoSize = true; count.Padding = new Padding(10, 7, 0, 0);
            actions.Controls.Add(generate); actions.Controls.Add(choose); actions.Controls.Add(export); actions.Controls.Add(count);
            footer.Controls.Add(actions, 0, 1); root.Controls.Add(footer, 0, 2);
            Controls.Add(root);
            RefreshList();
        }

        private void RefreshList()
        {
            string query = search.Text.Trim();
            list.BeginUpdate(); list.Items.Clear();
            foreach (ItemTemplate item in catalog.Templates)
            {
                string original = item.Definition == null ? "" : item.Definition.OriginalName;
                string knownNames = item.Alias == "super_ww" ? "蓝薇 隐藏装备 隐藏武器 Lan Wei secret hidden weapon" : item.Alias == "ore5" ? "铁矿石 iron ore" : "";
                if (query.Length > 0 && (item.Definition.ChineseName + " " + item.Name + " " + original + " " + item.Alias + " " + item.TypeName + " " + item.NativeTypeName + " " + item.Note + " " + knownNames).IndexOf(query, StringComparison.OrdinalIgnoreCase) < 0) continue;
                if (category.SelectedIndex > 0 && TemplateType(item) != Convert.ToString(category.SelectedItem)) continue;
                var row = new ListViewItem(item.Definition.GetDisplayName(chinese));
                row.ToolTipText = item.Definition.GetNameDescription(chinese);
                row.SubItems.Add(TemplateType(item)); row.SubItems.Add(item.Level.ToString(CultureInfo.InvariantCulture)); row.Tag = item;
                list.Items.Add(row);
            }
            if (category.SelectedIndex == 0 || Convert.ToString(category.SelectedItem) == referenceCategory)
                foreach (SpecialItemDefinition item in catalog.SpecialItems)
                {
                    if (query.Length > 0 && (item.Name + " " + item.Alias + " " + item.Scene + " " + item.Description).IndexOf(query, StringComparison.OrdinalIgnoreCase) < 0) continue;
                    var row = new ListViewItem(item.Name);
                    row.SubItems.Add(referenceCategory); row.SubItems.Add(chinese ? "只读" : "Read only"); row.Tag = item;
                    list.Items.Add(row);
                }
            list.EndUpdate();
            count.Text = list.Items.Count + " / " + (catalog.Templates.Count + catalog.SpecialItems.Count);
            if (list.Items.Count > 0) list.Items[0].Selected = true;
        }

        private void ExportCatalog()
        {
            using (var dialog = new SaveFileDialog { Filter = "Text (*.txt)|*.txt", FileName = "Seal-of-Evil-item-generation-catalog.txt" })
            {
                if (dialog.ShowDialog(this) != DialogResult.OK) return;
                var output = new StringBuilder();
                output.AppendLine(chinese ? "Seal of Evil 全物品原始定义与生成规则" : "Seal of Evil - complete item definitions and generation rules");
                foreach (ItemTemplate item in catalog.Templates)
                {
                    output.AppendLine(); output.AppendLine("============================================================");
                    output.AppendLine(catalog.Describe(item, chinese));
                }
                foreach (SpecialItemDefinition item in catalog.SpecialItems)
                {
                    output.AppendLine(); output.AppendLine("============================================================");
                    output.AppendLine(DescribeReference(item));
                }
                File.WriteAllText(dialog.FileName, output.ToString(), new UTF8Encoding(true));
            }
        }

        private void GeneratePreview()
        {
            if (SelectedTemplate == null) return;
            try
            {
                Cursor = Cursors.WaitCursor; generate.Enabled = false; choose.Enabled = false;
                InitialState = ItemFactory.Create(catalog, SelectedTemplate, GenerationLevel, Environment.TickCount & Int32.MaxValue);
                var text = new StringBuilder();
                text.AppendLine(chinese ? "生成结果预览" : "Generated item preview");
                text.AppendLine(SelectedTemplate.Definition.GetDisplayName(chinese));
                text.AppendLine((chinese ? "游戏生成名称：" : "Native generated name: ") + GameCatalog.PlainGameText(InitialState.Name));
                text.AppendLine(InitialState.GenerationSummary);
                int[] ids = { 2, 151, 152, 153, 167 };
                foreach (int id in ids)
                {
                    int value = InitialState.IntegerAttributes[id];
                    if (value < 0) continue;
                    text.AppendLine(FieldPresentation.Label(new SaveField { Key = "attr." + id }, chinese) + ": " + value.ToString(CultureInfo.InvariantCulture));
                }
                text.AppendLine(chinese ? "实际附加属性：" : "Actual modifiers:");
                foreach (int[] words in InitialState.Modifiers)
                {
                    text.Append("  " + FieldPresentation.EffectName(words[4], GameCatalog.AttributeName(words[4]), chinese) + ": ");
                    text.Append(BitConverter.ToSingle(BitConverter.GetBytes(words[9]), 0).ToString("R", CultureInfo.InvariantCulture));
                    if (words[10] != 0) text.Append((chinese ? "；成长系数 " : "; Growth ") + BitConverter.ToSingle(BitConverter.GetBytes(words[10]), 0).ToString("R", CultureInfo.InvariantCulture));
                    if (words[5] == 0) text.Append(chinese ? "（等待激活条件）" : " (conditional; inactive)");
                    text.AppendLine();
                }
                text.AppendLine();
                text.AppendLine(adding ? (chinese ? "新物品放入所选人物背包的空位，可在保存前撤销。" : "The new item goes into a free slot in the selected character's backpack. Undo is available before saving.")
                    : (chinese ? "替换后物品放入原持有人的背包，可在保存前撤销。" : "The replacement goes into the same owner's backpack. Undo is available before saving."));
                text.AppendLine(); text.AppendLine(catalog.Describe(SelectedTemplate, chinese));
                details.Text = text.ToString(); details.SelectionStart = 0; details.ScrollToCaret();
                choose.Enabled = !adding || SelectedOwner != null;
            }
            catch (Exception ex)
            {
                InitialState = null;
                MessageBox.Show(this, ex.Message, chinese ? "无法生成物品预览" : "Cannot generate item preview", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            finally { Cursor = Cursors.Default; generate.Enabled = SelectedTemplate != null; }
        }

        private string DescribeReference(SpecialItemDefinition item)
        {
            return catalog.DescribeSpecial(item, chinese);
        }

        private string TemplateType(ItemTemplate item) { return chinese ? item.NativeTypeName : item.TypeName; }
    }
}
