using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
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
        private readonly GameIconImages iconImages;
        private readonly PictureBox inventoryIcon = new PictureBox();
        private readonly Label iconCaption = new Label();
        private bool iconResourcesDisposed;
        private readonly Label count = new Label();
        private readonly Button choose = new Button();
        private readonly Button generate = new Button();
        private readonly Button quick99 = new Button();
        private readonly Label addStatus = new Label();
        private readonly System.Windows.Forms.Timer previewTimer = new System.Windows.Forms.Timer { Interval = 220 };
        private readonly ItemPreviewQueue previewQueue = new ItemPreviewQueue();
        private Task<ItemInitialState> previewTask;
        private ItemTemplate previewTemplate;
        private int previewLevel, previewVersion, version, addedCount;
        private bool closed, applying;
        private readonly bool canGenerate;
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
        public Action<CharacterRecord, ItemInitialState> AddItemRequested { get; set; }
        public Action<IWin32Window, CharacterRecord> SkillBooksRequested { get; set; }

        public ItemTemplateBrowser(ItemTemplateCatalog definitions, bool zh, bool allowReplacement)
            : this(definitions, zh, allowReplacement ? ItemTemplateAction.Replace : ItemTemplateAction.Browse, null, null) { }

        public ItemTemplateBrowser(ItemTemplateCatalog definitions, bool zh, ItemTemplateAction action, IList<CharacterRecord> owners, CharacterRecord initialOwner)
        {
            catalog = definitions; chinese = zh;
            adding = action == ItemTemplateAction.Add;
            bool allowReplacement = action != ItemTemplateAction.Browse;
            canGenerate = allowReplacement;
            referenceCategory = zh ? "剧情物品图鉴（只读）" : "Story item references (read only)";
            Text = adding ? (zh ? "添加物品" : "Add item") : (zh ? "完整物品目录与生成模板" : "Complete item catalog and generation templates");
            Font = UiFonts.Create();
            EditorBranding.Apply(this);
            try { iconImages = new GameIconImages(catalog.GameDirectory); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
            catch (ArgumentException) { }
            Size = new Size(1160, 800); MinimumSize = new Size(950, 650);
            StartPosition = FormStartPosition.CenterParent;
            BackColor = Color.FromArgb(236, 232, 223);
            var root = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 3, ColumnCount = 1, Padding = new Padding(15) };
            root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 45));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, allowReplacement ? 170 : 48));
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
            if (iconImages != null) list.SmallImageList = iconImages.Images;
            list.Columns.Add(zh ? "物品" : "Item", 235);
            list.Columns.Add(zh ? "类别" : "Category", 125);
            list.Columns.Add(zh ? "模板等级" : "Template level", 95);
            list.SelectedIndexChanged += delegate
            {
                SelectedTemplate = list.SelectedItems.Count == 0 ? null : list.SelectedItems[0].Tag as ItemTemplate;
                InitialState = null;
                SpecialItemDefinition reference = list.SelectedItems.Count == 0 ? null : list.SelectedItems[0].Tag as SpecialItemDefinition;
                details.Text = SelectedTemplate != null ? catalog.Describe(SelectedTemplate, chinese) : reference != null ? DescribeReference(reference) : "";
                ShowInventoryIcon(0, SelectedTemplate);
                if (SelectedTemplate != null && !adding) level.Value = Math.Max(level.Minimum, Math.Min(level.Maximum, SelectedTemplate.Alias == "super_ww" ? 14 : SelectedTemplate.Level));
                SchedulePreview();
            };
            list.DoubleClick += delegate { if (adding) AddSelected(true); };
            details.Dock = DockStyle.Fill; details.Multiline = true; details.ReadOnly = true;
            details.ScrollBars = ScrollBars.Vertical; details.BackColor = Color.FromArgb(250, 248, 243);
            details.BorderStyle = BorderStyle.None;
            var description = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 2, ColumnCount = 1 };
            description.RowStyles.Add(new RowStyle(SizeType.Absolute, 80));
            description.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            var imageHeader = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false, Padding = new Padding(5) };
            inventoryIcon.Name = "TemplateInventoryIcon"; inventoryIcon.Size = new Size(64, 64);
            inventoryIcon.SizeMode = PictureBoxSizeMode.Zoom; inventoryIcon.BackColor = Color.FromArgb(250, 248, 243);
            iconCaption.Name = "TemplateInventoryIconCaption"; iconCaption.AutoSize = true;
            iconCaption.Padding = new Padding(8, 9, 0, 0);
            imageHeader.Controls.Add(inventoryIcon); imageHeader.Controls.Add(iconCaption);
            description.Controls.Add(imageHeader, 0, 0); description.Controls.Add(details, 0, 1);
            split.Panel1.Controls.Add(list); split.Panel2.Controls.Add(description);
            root.Controls.Add(split, 0, 1);
            var footer = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = allowReplacement ? 3 : 2, ColumnCount = 1 };
            footer.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
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
                level.Minimum = ItemFactory.MinimumGenerationLevel; level.Maximum = ItemFactory.MaximumGenerationLevel; level.Value = adding ? 99 : 1; level.Width = 90; level.Name = "TemplateLevel";
                level.ValueChanged += delegate { if (SelectedTemplate != null) details.Text = catalog.Describe(SelectedTemplate, chinese); SchedulePreview(); };
                preserve.Text = zh ? "保留当前物品的附加属性" : "Keep the current item's additional modifiers";
                preserve.AutoSize = true; preserve.Padding = new Padding(10, 4, 0, 0);
                options.Controls.Add(level); if (!adding) options.Controls.Add(preserve);
                footer.RowStyles.Add(new RowStyle(SizeType.Absolute, 40));
                footer.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
                footer.RowStyles.Add(new RowStyle(SizeType.Absolute, 28));
                footer.Controls.Add(options, 0, 0);
                addStatus.AutoSize = false; addStatus.Dock = DockStyle.Fill;
                addStatus.ForeColor = Color.FromArgb(105, 95, 75);
                addStatus.Text = adding ? (zh ? "选择物品后自动预览。双击物品可直接添加99级；添加后窗口保持打开。" : "Selecting an item previews it automatically. Double-click to add at level 99; the window stays open.") : "";
                footer.Controls.Add(addStatus, 0, 2);
            }
            else { footer.RowStyles.Add(new RowStyle(SizeType.Absolute, 0)); footer.RowStyles.Add(new RowStyle(SizeType.Percent, 100)); }
            var actions = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = true };
            generate.Name = "GenerateTemplatePreview"; generate.Text = zh ? "重新随机" : "Reroll";
            generate.AutoSize = true; generate.Visible = allowReplacement;
            generate.Click += delegate { version++; InitialState = null; GeneratePreview(); };
            choose.Name = "UseItemTemplate"; choose.Text = adding ? (zh ? "添加到背包" : "Add to backpack") : (zh ? "替换为预览中的物品" : "Replace with the previewed item");
            choose.AutoSize = true; choose.BackColor = Color.FromArgb(232, 190, 104); choose.Visible = allowReplacement;
            choose.Click += delegate { AddSelected(false); };
            quick99.Name = "QuickAddLevel99"; quick99.Text = zh ? "快捷添加99级" : "Quick add level 99";
            quick99.AutoSize = true; quick99.Visible = adding;
            quick99.Click += delegate { AddSelected(true); };
            var books = new Button { Name = "OpenScriptBooks", Text = zh ? "技能书／宝典…" : "Skill books / manuals...", AutoSize = true };
            books.Click += delegate { if (SkillBooksRequested != null) SkillBooksRequested(this, SelectedOwner); };
            var close = new Button { Name = "CloseItemBrowser", Text = zh ? "关闭" : "Close", DialogResult = DialogResult.Cancel, AutoSize = true };
            var export = new Button { Text = zh ? "导出完整目录与规则…" : "Export the complete catalog...", AutoSize = true };
            export.Click += delegate { ExportCatalog(); };
            count.AutoSize = true; count.Padding = new Padding(10, 7, 0, 0);
            actions.Controls.Add(choose); actions.Controls.Add(quick99); actions.Controls.Add(generate);
            actions.Controls.Add(books); actions.Controls.Add(export); actions.Controls.Add(close); actions.Controls.Add(count);
            footer.Controls.Add(actions, 0, 1); root.Controls.Add(footer, 0, 2);
            Controls.Add(root);
            AcceptButton = choose; CancelButton = close;
            previewTimer.Tick += delegate { previewTimer.Stop(); GeneratePreview(); };
            FormClosed += delegate { closed = true; version++; previewTimer.Stop(); };
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
                if (iconImages != null) row.ImageKey = TemplateIconKey(item);
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

        private bool UsableSelection()
        { return SelectedTemplate != null && !(SelectedTemplate.TypeId >= 45 && SelectedTemplate.TypeId <= 49); }

        private void UpdateActions()
        {
            bool available = UsableSelection() && !applying;
            choose.Enabled = available && (!adding || SelectedOwner != null);
            quick99.Enabled = available && SelectedOwner != null;
            generate.Enabled = available;
        }

        private void SchedulePreview()
        {
            version++; InitialState = null; previewTimer.Stop(); UpdateActions();
            if (!canGenerate || closed) return;
            ShowInventoryIcon(0, SelectedTemplate);
            if (UsableSelection()) previewTimer.Start();
            else if (SelectedTemplate != null)
                details.AppendText(chinese ? "\r\n\r\n这是未绑定技能的职业书籍模板。请点击“技能书／宝典”选择原版可使用的具体书籍。"
                    : "\r\n\r\nThis is an unbound class-book shell. Use Skill books / manuals to choose a concrete usable book.");
        }

        private Task<ItemInitialState> Plan(ItemTemplate template, int generationLevel, int requestVersion)
        {
            if (previewTask != null && !previewTask.IsFaulted && !previewTask.IsCanceled
                && previewTemplate == template && previewLevel == generationLevel && previewVersion == requestVersion) return previewTask;
            previewTemplate = template; previewLevel = generationLevel; previewVersion = requestVersion;
            previewTask = previewQueue.Run(delegate { return ItemFactory.Create(catalog, template, generationLevel, Environment.TickCount & Int32.MaxValue); },
                delegate { return !closed && version == requestVersion && SelectedTemplate == template; });
            return previewTask;
        }

        private async void GeneratePreview()
        {
            if (!UsableSelection() || closed || applying) return;
            ItemTemplate template = SelectedTemplate; int generationLevel = GenerationLevel, requestVersion = version;
            try
            {
                addStatus.Text = chinese ? "正在自动生成预览…" : "Generating preview...";
                ItemInitialState state = await Plan(template, generationLevel, requestVersion);
                if (closed || version != requestVersion || SelectedTemplate != template || GenerationLevel != generationLevel || state == null) return;
                InitialState = state; ShowPreview();
                addStatus.Text = addedCount > 0 ? (chinese ? "已添加 " + addedCount + " 件；可以继续选择并添加。" : addedCount + " items added; choose another item to continue.")
                    : (chinese ? "预览已就绪；回车或点击添加。双击可快捷添加99级。" : "Preview ready. Enter adds it; double-click quick-adds at level 99.");
            }
            catch (Exception ex)
            {
                if (closed || version != requestVersion) return;
                InitialState = null; addStatus.Text = ex.Message;
            }
            finally { if (!closed) UpdateActions(); }
        }

        private async void AddSelected(bool at99)
        {
            if (!UsableSelection() || applying || closed || (adding && SelectedOwner == null)) return;
            ItemTemplate template = SelectedTemplate; CharacterRecord owner = SelectedOwner;
            int generationLevel = at99 ? 99 : GenerationLevel, requestVersion = version;
            previewTimer.Stop(); applying = true; UpdateActions();
            list.Enabled = search.Enabled = category.Enabled = level.Enabled = recipient.Enabled = false;
            try
            {
                addStatus.Text = chinese ? "正在生成并添加…" : "Generating and adding...";
                ItemInitialState state = await Plan(template, generationLevel, requestVersion);
                if (closed || state == null) return;
                if (!adding || AddItemRequested == null)
                { InitialState = state; DialogResult = DialogResult.OK; Close(); return; }
                AddItemRequested(owner, state); addedCount++;
                addStatus.Text = (chinese ? "已添加到 " : "Added to ") + owner.Name + (chinese ? " 的背包；本次共 " : "'s backpack; ") + addedCount
                    + (chinese ? " 件。关闭后在主界面保存。" : " items this session. Save in the main window after closing.");
            }
            catch (Exception ex)
            {
                if (!closed) MessageBox.Show(this, ex.Message, chinese ? "无法添加物品" : "Cannot add item", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            finally
            {
                if (!closed)
                {
                    applying = false; list.Enabled = search.Enabled = category.Enabled = level.Enabled = recipient.Enabled = true;
                    SchedulePreview();
                }
            }
        }

        private void ShowPreview()
        {
                ShowInventoryIcon(InitialState.IntegerAttributes.Length > 102 ? InitialState.IntegerAttributes[102] : 0, SelectedTemplate);
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
                UpdateActions();
        }

        private string DescribeReference(SpecialItemDefinition item)
        {
            return catalog.DescribeSpecial(item, chinese);
        }

        private string TemplateType(ItemTemplate item) { return chinese ? item.NativeTypeName : item.TypeName; }

        private string TemplateIconKey(ItemTemplate template)
        {
            if (iconImages == null || template == null || template.IconVariants == null) return "";
            foreach (int id in template.IconVariants)
            { string key = iconImages.Key(id); if (key.Length != 0) return key; }
            return "";
        }

        private void ShowInventoryIcon(int actualId, ItemTemplate template)
        {
            if (closed || iconResourcesDisposed) return;
            Bitmap bitmap = null; bool actual = false;
            if (iconImages != null)
            {
                if (actualId > 0) { bitmap = iconImages.Preview(actualId); actual = bitmap != null; }
                if (bitmap == null && template != null && template.IconVariants != null)
                    foreach (int id in template.IconVariants) { bitmap = iconImages.Preview(id); if (bitmap != null) break; }
            }
            Image previous = inventoryIcon.Image; inventoryIcon.Image = bitmap;
            if (previous != null) previous.Dispose();
            iconCaption.Text = (chinese ? "背包图标" : "Inventory image") + Environment.NewLine
                + (bitmap == null ? (chinese ? "暂无可用图像" : "Image unavailable") : actual ? (chinese ? "已生成物品" : "Generated item") : (chinese ? "原版物品模板" : "Original item template"));
            if (actual && template != null)
                foreach (ListViewItem row in list.Items)
                    if (Object.ReferenceEquals(row.Tag, template)) { row.ImageKey = iconImages.Key(actualId); break; }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing && !iconResourcesDisposed)
            {
                closed = true; version++; previewTimer.Stop(); previewTimer.Dispose();
                Image previous = inventoryIcon.Image; inventoryIcon.Image = null;
                if (previous != null) previous.Dispose();
                list.SmallImageList = null;
                if (iconImages != null) iconImages.Dispose();
                iconResourcesDisposed = true;
            }
            base.Dispose(disposing);
        }
    }
}
