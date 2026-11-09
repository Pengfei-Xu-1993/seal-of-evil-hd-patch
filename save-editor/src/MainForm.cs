using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows.Forms;
using Microsoft.Win32;

namespace SealSaveEditor
{
    public sealed partial class MainForm : Form
    {
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr SendMessage(IntPtr window, int message, IntPtr wParam, string lParam);
        [DllImport("user32.dll")] private static extern bool IsWindowEnabled(IntPtr window);
        private SaveDocument document;
        private GameCatalog catalog;
        private ItemTemplateCatalog itemTemplates;
        private ScriptItemCatalog scriptBooks;
        private GameIconImages gameImages;
        private string gameDirectory;
        private string loadedGamHash;
        private string loadedUserHash;
        private bool binding;
        private bool chinese = true;
        private readonly string initialPath;
        private readonly Label subtitle = new Label();
        private readonly Label status = new Label();
        private readonly Label summary = new Label();
        private readonly ListBox saves = new ListBox();
        private readonly ComboBox character = new ComboBox();
        private readonly ComboBox skillCharacter = new ComboBox();
        private readonly TreeView skillTree = new TreeView();
        private readonly TextBox skillDescription = new TextBox();
        private SkillView selectedSkillView;
        private readonly CheckBox allFields = new CheckBox();
        private readonly TextBox fieldSearch = new TextBox();
        private readonly ComboBox language = new ComboBox();
        private readonly Dictionary<DataGridView, List<SaveField>> boundFields = new Dictionary<DataGridView, List<SaveField>>();
        private readonly ComboBox itemOwner = new ComboBox();
        private readonly TextBox itemSearch = new TextBox();
        private readonly ListView items = new ListView();
        private readonly DataGridView characterFields = NewFieldGrid();
        private readonly DataGridView skillFields = NewFieldGrid();
        private readonly DataGridView itemFields = NewFieldGrid();
        private readonly DataGridView locationFields = NewFieldGrid();
        private readonly ListView records = new ListView();
        private readonly TextBox recordDetail = new TextBox();
        private readonly Button saveButton = new Button();
        private readonly Button exportButton = new Button();
        private readonly Button campaignButton = new Button();
        private readonly Button dataButton = new Button();
        private readonly Button partyButton = new Button();
        private readonly Button learnSkillsButton = new Button();
        private readonly Button maxSkillsButton = new Button();
        private readonly Button skillBooksButton = new Button();
        private bool actorBusy;
        private readonly Button itemCatalogButton = new Button();
        private readonly Button addModifierButton = new Button();
        private readonly Button removeModifierButton = new Button();
        private readonly Button replaceItemButton = new Button();
        private readonly Button addItemButton = new Button();
        private readonly Button quickActionsButton = new Button();
        private readonly ContextMenuStrip quickActionsMenu = new ContextMenuStrip();
        private readonly ToolStripMenuItem extremeBonusesMenuItem = new ToolStripMenuItem();
        private readonly ToolStripMenuItem maxBonusesMenuItem = new ToolStripMenuItem();
        private readonly ToolStripMenuItem minimumRequirementsMenuItem = new ToolStripMenuItem();
        private readonly ToolStripMenuItem presetDetailsMenuItem = new ToolStripMenuItem();
        private readonly FieldInspector fieldInspector = new FieldInspector();
        private readonly Button undoButton = new Button();
        private readonly Button redoButton = new Button();
        private readonly Stack<FieldChange> undo = new Stack<FieldChange>();
        private readonly Stack<FieldChange> redo = new Stack<FieldChange>();
        private DataGridViewCell inspectedCell;
        private int activeCharacterIndex = -1, activeSkillCharacterIndex = -1, activeOwnerIndex;
        private ItemRecord activeItem;
        private string activeItemQuery = "";
        private readonly TabControl tabs = new TabControl();

        public MainForm(string path)
        {
            initialPath = path;
            Text = "Seal of Evil Save Editor";
            EditorBranding.Apply(this);
            Font = UiFonts.Create();
            BackColor = Color.FromArgb(236, 232, 223);
            ForeColor = Color.FromArgb(34, 44, 54);
            Size = new Size(1280, 880);
            MinimumSize = new Size(1050, 700);
            StartPosition = FormStartPosition.CenterScreen;
            AutoScaleMode = AutoScaleMode.Dpi;
            BuildLayout();
            AttachEvents();
            ApplyPresentation();
            Shown += delegate
            {
                gameDirectory = DetectGameDirectory(initialPath);
                LoadCatalog();
                RefreshSaves();
                if (!String.IsNullOrEmpty(initialPath) && File.Exists(initialPath)) OpenSave(initialPath);
                else if (saves.Items.Count > 0) saves.SelectedIndex = 0;
                UpdateState();
            };
            FormClosing += delegate(object sender, FormClosingEventArgs e) { if (!ConfirmDiscard()) e.Cancel = true; };
            Disposed += delegate { if (gameImages != null) gameImages.Dispose(); };
        }

        private static DataGridView NewFieldGrid()
        {
            DataGridView grid = new DataGridView();
            grid.Dock = DockStyle.Fill;
            grid.AllowUserToAddRows = false;
            grid.AllowUserToDeleteRows = false;
            grid.AllowUserToResizeRows = false;
            grid.RowHeadersVisible = false;
            grid.BackgroundColor = Color.White;
            grid.BorderStyle = BorderStyle.None;
            grid.AutoSizeRowsMode = DataGridViewAutoSizeRowsMode.AllCells;
            grid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
            grid.MultiSelect = false;
            grid.EditMode = DataGridViewEditMode.EditProgrammatically;
            grid.ReadOnly = true;
            grid.EnableHeadersVisualStyles = false;
            grid.ColumnHeadersDefaultCellStyle.BackColor = Color.FromArgb(232, 238, 242);
            grid.ColumnHeadersDefaultCellStyle.ForeColor = Color.FromArgb(35, 54, 68);
            grid.ColumnHeadersHeight = 44;
            grid.DefaultCellStyle.Padding = new Padding(6, 5, 6, 5);
            grid.DefaultCellStyle.SelectionBackColor = Color.FromArgb(243, 228, 190);
            grid.DefaultCellStyle.SelectionForeColor = Color.FromArgb(20, 48, 53);
            grid.AlternatingRowsDefaultCellStyle.BackColor = Color.FromArgb(248, 250, 252);
            grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Group", HeaderText = "Group", Width = 125, ReadOnly = true });
            grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Field", HeaderText = "Field", AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill, FillWeight = 125, ReadOnly = true });
            grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Value", HeaderText = "Value", AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill, FillWeight = 95 });
            grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Original", HeaderText = "Original", AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill, FillWeight = 85, ReadOnly = true });
            grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "RawKey", HeaderText = "Raw field", Width = 150, ReadOnly = true, Visible = false });
            grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Offset", HeaderText = "Offset", Width = 100, ReadOnly = true, Visible = false });
            return grid;
        }

        private static Button ButtonFor(string text, EventHandler action)
        {
            Button button = new Button { Text = text, AutoSize = true, Height = 36, Padding = new Padding(9, 3, 9, 3), FlatStyle = FlatStyle.Flat, Margin = new Padding(0, 0, 8, 0), BackColor = Color.White };
            button.FlatAppearance.BorderColor = Color.FromArgb(199, 210, 216);
            button.Click += action;
            return button;
        }

        private static void FitToolRow(FlowLayoutPanel panel, TableLayoutPanel table, int row, int minimum)
        {
            bool pending = false;
            Action queue = delegate
            {
                if (pending || panel.IsDisposed || !panel.IsHandleCreated) return;
                pending = true;
                panel.BeginInvoke((Action)delegate
                {
                    pending = false;
                    if (panel.IsDisposed || !panel.IsHandleCreated || table.IsDisposed) return;
                    int bottom = panel.Controls.Cast<Control>().Select(c => c.Bottom + c.Margin.Bottom).DefaultIfEmpty(0).Max();
                    int preferred = panel.GetPreferredSize(new Size(Math.Max(1, panel.ClientSize.Width), 0)).Height;
                    int height = Math.Max(minimum, Math.Max(bottom, preferred) + panel.Padding.Bottom + 4);
                    if (Math.Abs(table.RowStyles[row].Height - height) > 0.5F) table.RowStyles[row].Height = height;
                });
            };
            panel.Layout += delegate { queue(); };
            panel.HandleCreated += delegate { queue(); };
        }

        private void BuildLayout()
        {
            TableLayoutPanel root = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 5, ColumnCount = 1, Padding = new Padding(18) };
            root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 90));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 108));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 52));
            Controls.Add(root);

            TableLayoutPanel header = new TableLayoutPanel { Size = new Size(1200, 76), Dock = DockStyle.Fill, RowCount = 2, ColumnCount = 2 };
            header.RowStyles.Add(new RowStyle(SizeType.Absolute, 39));
            header.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            header.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            header.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 490));
            Label heading = new Label { Text = "SEAL OF EVIL  /  SAVE EDITOR", Font = UiFonts.Create(19F, FontStyle.Bold), Dock = DockStyle.Fill, Margin = new Padding(0), AutoSize = false };
            subtitle.Text = "Characters, skills, equipment and world data";
            subtitle.Dock = DockStyle.Fill;
            subtitle.AutoSize = false;
            subtitle.AutoEllipsis = true;
            subtitle.ForeColor = Color.FromArgb(93, 109, 120);
            subtitle.TextAlign = ContentAlignment.MiddleLeft;
            header.Controls.Add(heading, 0, 0);
            header.SetColumnSpan(heading, 2);
            header.Controls.Add(subtitle, 0, 1);
            FlowLayoutPanel fieldTools = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false, Padding = new Padding(0) };
            fieldSearch.Width = 180;
            fieldSearch.AccessibleName = "Filter fields";
            fieldSearch.HandleCreated += delegate { SendMessage(fieldSearch.Handle, 0x1501, new IntPtr(1), chinese ? "搜索项目或数值" : "Search properties or values"); };
            allFields.Text = "Show all fields";
            allFields.AutoSize = true;
            allFields.Margin = new Padding(8, 3, 0, 0);
            fieldTools.Controls.Add(fieldSearch);
            fieldTools.Controls.Add(allFields);
            language.Width = 100;
            language.DropDownStyle = ComboBoxStyle.DropDownList;
            language.Items.AddRange(new object[] { "中文说明", "English" });
            language.SelectedIndex = 0;
            fieldTools.Controls.Add(language);
            header.Controls.Add(fieldTools, 1, 1);
            root.Controls.Add(header, 0, 0);

            FlowLayoutPanel toolbar = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = true };
            toolbar.Controls.Add(ButtonFor("Open save...", delegate { BrowseSave(); }));
            saveButton.Text = "Save with backup";
            saveButton.AutoSize = true;
            saveButton.Height = 36;
            saveButton.Padding = new Padding(10, 3, 10, 3);
            saveButton.Margin = new Padding(0, 0, 8, 0);
            saveButton.FlatStyle = FlatStyle.Flat;
            saveButton.BackColor = Color.FromArgb(232, 190, 104);
            saveButton.ForeColor = Color.FromArgb(37, 35, 31);
            saveButton.Click += delegate { SaveCurrent(); };
            exportButton.Text = "Export copy...";
            exportButton.AutoSize = true;
            exportButton.Height = 36;
            exportButton.Padding = new Padding(9, 3, 9, 3);
            exportButton.Margin = new Padding(0, 0, 8, 0);
            exportButton.Click += delegate { ExportCopy(); };
            toolbar.Controls.Add(ButtonFor("Restore backup...", delegate { RestoreBackup(); }));
            toolbar.Controls.Add(ButtonFor("Readable data...", delegate { ExportReport(); }));
            toolbar.Controls.Add(ButtonFor("Game folder...", delegate { ChooseGameFolder(); }));
            campaignButton.Text = "Playthrough..."; campaignButton.AutoSize = true;
            campaignButton.Height = 36; campaignButton.Padding = new Padding(9, 3, 9, 3);
            campaignButton.Margin = new Padding(0, 0, 8, 0); campaignButton.FlatStyle = FlatStyle.Flat;
            campaignButton.BackColor = Color.White; campaignButton.Click += delegate { ShowCampaign(); };
            toolbar.Controls.Add(campaignButton);
            dataButton.Name = "OpenGameData"; dataButton.Text = "Game data TXT..."; dataButton.AutoSize = true;
            dataButton.Height = 36; dataButton.Padding = new Padding(9, 3, 9, 3); dataButton.Margin = new Padding(0, 0, 8, 5);
            dataButton.Click += delegate { ShowGameData(); };
            toolbar.Controls.Add(dataButton);
            FitToolRow(toolbar, root, 1, 50);
            root.Controls.Add(toolbar, 0, 1);

            SplitContainer body = new SplitContainer { Size = new Size(1200, 650), Dock = DockStyle.Fill, FixedPanel = FixedPanel.Panel1, SplitterDistance = 190, Panel1MinSize = 160, Panel2MinSize = 700, BackColor = Color.FromArgb(236, 232, 223) };
            root.Controls.Add(body, 0, 2);
            TableLayoutPanel sidebar = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 4, ColumnCount = 1, Padding = new Padding(0, 0, 12, 0) };
            sidebar.RowStyles.Add(new RowStyle(SizeType.Absolute, 32));
            sidebar.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            sidebar.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));
            sidebar.RowStyles.Add(new RowStyle(SizeType.Absolute, 155));
            sidebar.Controls.Add(new Label { Text = "SAVE SLOTS", Font = UiFonts.Create(10F, FontStyle.Bold), Dock = DockStyle.Fill }, 0, 0);
            saves.Dock = DockStyle.Fill;
            saves.IntegralHeight = false;
            saves.BorderStyle = BorderStyle.None;
            saves.ItemHeight = 28;
            sidebar.Controls.Add(saves, 0, 1);
            sidebar.Controls.Add(ButtonFor("Refresh slots", delegate { RefreshSaves(); }), 0, 2);
            summary.Dock = DockStyle.Fill;
            summary.ForeColor = Color.FromArgb(82, 101, 115);
            sidebar.Controls.Add(summary, 0, 3);
            body.Panel1.Controls.Add(sidebar);

            tabs.Dock = DockStyle.Fill;
            tabs.Padding = new Point(14, 8);
            SplitContainer workspace = new SplitContainer { Size = new Size(1000, 620), Dock = DockStyle.Fill, FixedPanel = FixedPanel.Panel2, SplitterDistance = 660, Panel1MinSize = 390, Panel2MinSize = 280 };
            workspace.Panel1.Controls.Add(tabs);
            fieldInspector.Dock = DockStyle.Fill;
            workspace.Panel2.Controls.Add(fieldInspector);
            body.Panel2.Controls.Add(workspace);
            AddFieldTab("Characters", character, characterFields);
            BuildSkillsTab();
            BuildItemsTab();
            TabPage location = NewTab("Location");
            location.Controls.Add(locationFields);
            TabPage inspector = NewTab("Records");
            SplitContainer recordSplit = new SplitContainer { Size = new Size(900, 600), Dock = DockStyle.Fill, Orientation = Orientation.Horizontal, SplitterDistance = 280 };
            records.Dock = DockStyle.Fill;
            records.View = View.Details;
            records.FullRowSelect = true;
            records.Columns.Add("Record", 220);
            records.Columns.Add("Bytes", 95);
            records.Columns.Add("Details", 620);
            recordDetail.Dock = DockStyle.Fill;
            recordDetail.Multiline = true;
            recordDetail.ReadOnly = true;
            recordDetail.ScrollBars = ScrollBars.Both;
            recordDetail.Font = UiFonts.Create();
            recordDetail.BackColor = Color.White;
            recordSplit.Panel1.Controls.Add(records);
            recordSplit.Panel2.Controls.Add(recordDetail);
            inspector.Controls.Add(recordSplit);

            status.Dock = DockStyle.Fill;
            status.TextAlign = ContentAlignment.MiddleLeft;
            status.ForeColor = Color.FromArgb(71, 90, 105);
            root.Controls.Add(status, 0, 3);
            FlowLayoutPanel footer = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false };
            undoButton.Text = "Undo"; undoButton.AutoSize = true; undoButton.Height = 38;
            redoButton.Text = "Redo"; redoButton.AutoSize = true; redoButton.Height = 38;
            undoButton.Click += delegate { UndoChange(false); };
            redoButton.Click += delegate { UndoChange(true); };
            footer.Controls.Add(undoButton);
            footer.Controls.Add(redoButton);
            footer.Controls.Add(saveButton);
            footer.Controls.Add(exportButton);
            root.Controls.Add(footer, 0, 4);
        }

        private TabPage NewTab(string text)
        {
            TabPage page = new TabPage(text) { Padding = new Padding(10), BackColor = Color.White };
            tabs.TabPages.Add(page);
            return page;
        }

        private void AddFieldTab(string text, ComboBox selector, DataGridView grid)
        {
            TabPage page = NewTab(text);
            TableLayoutPanel layout = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 2, ColumnCount = 1 };
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 54));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            selector.DropDownStyle = ComboBoxStyle.DropDownList;
            selector.Width = 200;
            FlowLayoutPanel selectors = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false };
            selectors.Controls.Add(selector);
            if (selector == character)
            {
                partyButton.Name = "AddPartyMember"; partyButton.Text = "Add companion..."; partyButton.AutoSize = true;
                partyButton.Click += delegate { ShowCompanions(); };
                selectors.Controls.Add(partyButton);
            }
            layout.Controls.Add(selectors, 0, 0);
            layout.Controls.Add(grid, 0, 1);
            page.Controls.Add(layout);
        }

        private void BuildSkillsTab()
        {
            TabPage page = NewTab("Skills");
            TableLayoutPanel layout = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 2, ColumnCount = 1 };
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 142));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            skillCharacter.Width = 300;
            skillCharacter.DropDownStyle = ComboBoxStyle.DropDownList;
            var skillTools = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = true };
            skillTools.Controls.Add(skillCharacter); skillTools.SetFlowBreak(skillCharacter, true);
            learnSkillsButton.Name = "LearnAllSkills"; learnSkillsButton.Text = "Learn class skills"; learnSkillsButton.AutoSize = true;
            maxSkillsButton.Name = "MaxAllSkills"; maxSkillsButton.Text = "Max class skills"; maxSkillsButton.AutoSize = true;
            skillBooksButton.Name = "OpenSkillBooks"; skillBooksButton.Text = "Skill books..."; skillBooksButton.AutoSize = true;
            learnSkillsButton.Click += delegate { ApplyAllSkills(false); };
            maxSkillsButton.Click += delegate { ApplyAllSkills(true); };
            skillBooksButton.Click += delegate { ShowSkillBooks(this, skillCharacter.SelectedItem as CharacterRecord); };
            skillTools.Controls.AddRange(new Control[] { learnSkillsButton, maxSkillsButton, skillBooksButton });
            FitToolRow(skillTools, layout, 0, 85);
            layout.Controls.Add(skillTools, 0, 0);
            SplitContainer split = new SplitContainer { Size = new Size(940, 550), Dock = DockStyle.Fill, SplitterDistance = 260, Panel1MinSize = 200, Panel2MinSize = 400 };
            skillTree.Dock = DockStyle.Fill;
            skillTree.HideSelection = false;
            skillTree.FullRowSelect = true;
            skillTree.ShowNodeToolTips = true;
            skillTree.BorderStyle = BorderStyle.None;
            skillTree.ItemHeight = 34;
            split.Panel1.Controls.Add(skillTree);
            TableLayoutPanel details = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 2, ColumnCount = 1 };
            details.RowStyles.Add(new RowStyle(SizeType.Absolute, 145));
            details.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            skillDescription.Dock = DockStyle.Fill;
            skillDescription.Multiline = true;
            skillDescription.ReadOnly = true;
            skillDescription.ScrollBars = ScrollBars.Vertical;
            skillDescription.BackColor = Color.FromArgb(245, 249, 250);
            skillDescription.BorderStyle = BorderStyle.None;
            details.Controls.Add(skillDescription, 0, 0);
            details.Controls.Add(skillFields, 0, 1);
            split.Panel2.Controls.Add(details);
            layout.Controls.Add(split, 0, 1);
            page.Controls.Add(layout);
        }

        private void BuildItemsTab()
        {
            TabPage page = NewTab("Items & equipment");
            TableLayoutPanel layout = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 3, ColumnCount = 1 };
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 98));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 100));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            FlowLayoutPanel filters = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = true };
            itemOwner.Width = 150;
            itemOwner.DropDownStyle = ComboBoxStyle.DropDownList;
            itemSearch.Width = 160;
            itemSearch.AccessibleName = "Search items";
            itemCatalogButton.Text = "Item catalog...";
            itemCatalogButton.AutoSize = true;
            itemCatalogButton.Click += delegate { ShowCatalog(); };
            filters.Controls.Add(itemOwner);
            filters.Controls.Add(itemSearch);
            filters.Controls.Add(itemCatalogButton);
            FitToolRow(filters, layout, 0, 52);
            TableLayoutPanel actions = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 2, ColumnCount = 1, Margin = Padding.Empty };
            actions.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            actions.RowStyles.Add(new RowStyle(SizeType.Percent, 50));
            actions.RowStyles.Add(new RowStyle(SizeType.Percent, 50));
            FlowLayoutPanel itemActions = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false, Margin = Padding.Empty };
            FlowLayoutPanel modifierActions = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false, Margin = Padding.Empty };
            addItemButton.Text = "Add item..."; addItemButton.AutoSize = true;
            addItemButton.BackColor = Color.FromArgb(232, 190, 104);
            addItemButton.Click += delegate { AddNewItem(); };
            addModifierButton.Text = "Add modifier..."; addModifierButton.AutoSize = true;
            removeModifierButton.Text = "Remove modifier"; removeModifierButton.AutoSize = true;
            addModifierButton.Click += delegate { AddItemModifier(); };
            removeModifierButton.Click += delegate { RemoveItemModifier(); };
            replaceItemButton.Text = "Replace item..."; replaceItemButton.AutoSize = true;
            replaceItemButton.Click += delegate { ReplaceSelectedItem(); };
            quickActionsButton.Text = "Shortcuts"; quickActionsButton.AutoSize = true;
            quickActionsMenu.Font = UiFonts.Create();
            extremeBonusesMenuItem.ShortcutKeyDisplayString = "Ctrl+Alt+A";
            maxBonusesMenuItem.ShortcutKeyDisplayString = "Ctrl+Alt+T";
            minimumRequirementsMenuItem.ShortcutKeyDisplayString = "Ctrl+Alt+1";
            extremeBonusesMenuItem.Click += delegate { RunItemPreset(2); };
            maxBonusesMenuItem.Click += delegate { RunItemQuickAction(true); };
            minimumRequirementsMenuItem.Click += delegate { RunItemQuickAction(false); };
            presetDetailsMenuItem.Click += delegate { ShowItemPresetDetails(); };
            quickActionsMenu.Items.Add(extremeBonusesMenuItem); quickActionsMenu.Items.Add(maxBonusesMenuItem);
            quickActionsMenu.Items.Add(minimumRequirementsMenuItem); quickActionsMenu.Items.Add(new ToolStripSeparator());
            quickActionsMenu.Items.Add(presetDetailsMenuItem);
            quickActionsButton.Click += delegate { quickActionsMenu.Show(quickActionsButton, new Point(0, quickActionsButton.Height)); };
            itemActions.Controls.Add(addItemButton); itemActions.Controls.Add(replaceItemButton);
            itemActions.Controls.Add(quickActionsButton);
            modifierActions.Controls.Add(addModifierButton); modifierActions.Controls.Add(removeModifierButton);
            actions.Controls.Add(itemActions, 0, 0); actions.Controls.Add(modifierActions, 0, 1);
            SplitContainer split = new SplitContainer { Size = new Size(900, 600), Dock = DockStyle.Fill, Orientation = Orientation.Horizontal, SplitterDistance = 240 };
            items.Dock = DockStyle.Fill;
            items.View = View.Details;
            items.FullRowSelect = true;
            items.HideSelection = false;
            items.ShowItemToolTips = true;
            items.MultiSelect = false;
            items.Columns.Add("Item", 300);
            items.Columns.Add("Owner / container", 190);
            items.Columns.Add("Alias", 195);
            split.Panel1.Controls.Add(items);
            split.Panel2.Controls.Add(itemFields);
            layout.Controls.Add(filters, 0, 0);
            layout.Controls.Add(actions, 0, 1);
            layout.Controls.Add(split, 0, 2);
            page.Controls.Add(layout);
        }

        private void AttachEvents()
        {
            fieldInspector.ApplyRequested += delegate { ApplyInspectorDraft(); };
            fieldInspector.ResetRequested += delegate { fieldInspector.ResetDraft(); UpdateState(); };
            fieldInspector.DraftChanged += delegate { UpdateState(); };
            tabs.Selecting += delegate(object sender, TabControlCancelEventArgs e) { if (!ApplyInspectorDraft()) e.Cancel = true; };
            tabs.SelectedIndexChanged += delegate { if (!binding) BindActiveInspector(); };
            skillTree.BeforeSelect += delegate(object sender, TreeViewCancelEventArgs e) { if (!binding && !ApplyInspectorDraft()) e.Cancel = true; };
            saves.SelectedIndexChanged += delegate
            {
                if (binding || saves.SelectedItem == null) return;
                SaveEntry entry = (SaveEntry)saves.SelectedItem;
                if (document == null || !String.Equals(document.FilePath, entry.Path, StringComparison.OrdinalIgnoreCase)) OpenSave(entry.Path);
            };
            character.SelectedIndexChanged += delegate
            {
                if (binding) return;
                if (!ApplyInspectorDraft()) { RestoreCombo(character, activeCharacterIndex); return; }
                activeCharacterIndex = character.SelectedIndex; BindCharacter();
            };
            skillCharacter.SelectedIndexChanged += delegate
            {
                if (binding) return;
                if (!ApplyInspectorDraft()) { RestoreCombo(skillCharacter, activeSkillCharacterIndex); return; }
                activeSkillCharacterIndex = skillCharacter.SelectedIndex; BindSkills();
            };
            skillTree.AfterSelect += delegate { if (!binding) BindSkillValues(); };
            allFields.CheckedChanged += delegate { ApplyPresentation(); RefreshFieldViews(); };
            language.SelectedIndexChanged += delegate { chinese = language.SelectedIndex == 0; ApplyPresentation(); BindSkills(); RefreshFieldViews(); BindItems(); };
            fieldSearch.TextChanged += delegate { RefreshFieldViews(); };
            itemOwner.SelectedIndexChanged += delegate
            {
                if (binding) return;
                if (!ApplyInspectorDraft()) { RestoreCombo(itemOwner, activeOwnerIndex); return; }
                activeOwnerIndex = itemOwner.SelectedIndex; BindItems();
            };
            itemSearch.TextChanged += delegate
            {
                if (binding) return;
                if (!ApplyInspectorDraft()) { binding = true; itemSearch.Text = activeItemQuery; binding = false; return; }
                activeItemQuery = itemSearch.Text; BindItems();
            };
            items.SelectedIndexChanged += delegate
            {
                if (binding) return;
                if (!ApplyInspectorDraft())
                {
                    binding = true;
                    foreach (ListViewItem row in items.Items) row.Selected = row.Tag == activeItem;
                    binding = false; return;
                }
                if (items.SelectedItems.Count > 0) { activeItem = (ItemRecord)items.SelectedItems[0].Tag; BindFields(itemFields, activeItem.Fields); }
                else BindFields(itemFields, null);
            };
            records.SelectedIndexChanged += delegate
            {
                if (records.SelectedItems.Count > 0)
                {
                    RecordInfo record = (RecordInfo)records.SelectedItems[0].Tag;
                    recordDetail.Text = record.Name + Environment.NewLine + "Offset: 0x" + record.Offset.ToString("X") + "    Bytes: " + record.Length + Environment.NewLine + Environment.NewLine + record.Detail;
                }
            };
            foreach (DataGridView grid in new[] { characterFields, skillFields, itemFields, locationFields })
            {
                grid.CellEnter += delegate(object sender, DataGridViewCellEventArgs e)
                {
                    DataGridView active = (DataGridView)sender;
                    if (binding || e.RowIndex < 0 || !active.Visible || tabs.SelectedTab == null || !tabs.SelectedTab.Contains(active)) return;
                    SaveField field = active.Rows[e.RowIndex].Tag as SaveField;
                    if (field != null) BindInspector(active, active.Rows[e.RowIndex].Cells[2]);
                };
                grid.DataError += delegate(object sender, DataGridViewDataErrorEventArgs e) { e.ThrowException = false; SetStatus("The value could not be accepted."); };
            }
        }

        private void ValidateField(object sender, DataGridViewCellValidatingEventArgs e)
        {
            if (binding || e.ColumnIndex != 2 || e.RowIndex < 0) return;
            DataGridView grid = (DataGridView)sender;
            SaveField field = (SaveField)grid.Rows[e.RowIndex].Tag;
            if (!field.Editable) return;
            try
            {
                DataGridViewComboBoxEditingControl choice = grid.EditingControl as DataGridViewComboBoxEditingControl;
                field.Text = choice != null && grid.CurrentCell is DataGridViewComboBoxCell ? Convert.ToString(choice.SelectedValue, CultureInfo.InvariantCulture) : Convert.ToString(e.FormattedValue, CultureInfo.InvariantCulture);
                grid.Rows[e.RowIndex].ErrorText = "";
            }
            catch (Exception ex)
            {
                e.Cancel = true;
                grid.Rows[e.RowIndex].ErrorText = ex.Message;
                SetStatus(ex.Message);
            }
        }

        private void FieldEdited(object sender, DataGridViewCellEventArgs e)
        {
            if (binding || e.RowIndex < 0 || e.ColumnIndex != 2) return;
            DataGridView grid = (DataGridView)sender;
            SaveField field = (SaveField)grid.Rows[e.RowIndex].Tag;
            grid.Rows[e.RowIndex].Cells[2].Value = field.Text;
            grid.Rows[e.RowIndex].Cells[2].Style.Font = new Font(grid.Font, field.Text != field.OriginalText ? FontStyle.Bold : FontStyle.Regular);
            UpdateState();
        }

        private void BindFields(DataGridView grid, IEnumerable<SaveField> fields)
        {
            if (!binding && !ApplyInspectorDraft()) return;
            List<SaveField> source = fields == null ? new List<SaveField>() : fields.ToList();
            if (grid == itemFields) source = source.OrderBy(x => x.Key.StartsWith("meta.", StringComparison.Ordinal) ? 0 : x.Key.StartsWith("effect.", StringComparison.Ordinal) ? 1 : x.Group == "Item / equipment" ? 2 : x.Group == "String attributes" ? 3 : 4).ToList();
            boundFields[grid] = source;
            bool previous = binding;
            binding = true;
            grid.Rows.Clear();
            if (source != null)
            {
                foreach (SaveField field in source)
                {
                    string context = grid == characterFields ? "character" : grid == itemFields ? "item" : grid == skillFields ? "skill" : "location";
                    bool skillSemantic = grid == skillFields && selectedSkillView != null && selectedSkillView.FieldLabels.ContainsKey(field.Key);
                    if (!allFields.Checked && (grid == skillFields && selectedSkillView != null ? !skillSemantic : !FieldPresentation.IsCommon(field, context))) continue;
                    if (!allFields.Checked && context == "skill" && field.Kind == "Int32" && field.Value < 0) continue;
                    if (!allFields.Checked && context == "skill" && field.Kind == "String" && String.IsNullOrEmpty(field.Text)) continue;
                    if (!allFields.Checked && context == "item" && field.Kind == "Int32" && field.Value < 0 && !field.Key.StartsWith("effect.", StringComparison.Ordinal)) continue;
                    string query = fieldSearch.Text.Trim();
                    string label = skillSemantic ? selectedSkillView.FieldLabels[field.Key] : FieldPresentation.Label(field, chinese);
                    if (query.Length > 0 && (label + " " + field.Name + " " + field.Key + " " + field.Group + " " + field.Text + " " + FieldPresentation.Label(field, !chinese)).IndexOf(query, StringComparison.OrdinalIgnoreCase) < 0) continue;
                    int index = grid.Rows.Add(FieldPresentation.Group(field, chinese), label, field.Text, field.OriginalText, field.Key, field.Offset < 0 ? (chinese ? "待保存" : "Unsaved") : "0x" + field.Offset.ToString("X"));
                    DataGridViewRow row = grid.Rows[index];
                    row.Tag = field;
                    if (grid == locationFields && !field.Editable && field.Name == "Current room" && catalog != null)
                    {
                        MapDefinition map = catalog.Maps.FirstOrDefault(x => String.Equals(x.Alias, field.Text, StringComparison.OrdinalIgnoreCase));
                        if (map != null) { row.Cells[2].Value = map.Name; row.Cells[3].Value = map.Name; }
                    }
                    if (field.Key.StartsWith("effect.", StringComparison.Ordinal) && field.Name == "Attribute ID" && field.Editable && catalog != null)
                    {
                        if (!allFields.Checked && grid == itemFields)
                        {
                            AttributeDefinition definition = catalog.FindAttribute((int)field.Value);
                            row.Cells[2].Value = FieldPresentation.EffectName((int)field.Value, definition == null ? "" : definition.Name, chinese);
                        }
                    }
                    bool editable = field.Editable && (grid != skillFields || selectedSkillView == null || skillSemantic);
                    row.Cells[2].ReadOnly = !editable;
                    if (!editable) row.Cells[2].Style.ForeColor = Color.FromArgb(124, 135, 145);
                    foreach (DataGridViewCell cell in row.Cells) cell.ToolTipText = allFields.Checked ? SkillFieldDescription(grid, field) + "\r\n" + field.Evidence : SkillFieldDescription(grid, field);
                }
            }
            binding = previous;
            DataGridViewRow preferred = grid.Rows.Cast<DataGridViewRow>().FirstOrDefault(r => ((SaveField)r.Tag).Key == "attr.18" && grid == characterFields)
                ?? grid.Rows.Cast<DataGridViewRow>().FirstOrDefault(r => CanEdit(grid, (SaveField)r.Tag));
            if (preferred != null) grid.CurrentCell = preferred.Cells[2];
            if (!binding && grid.Visible && grid.CurrentCell != null) BindInspector(grid, grid.CurrentCell.OwningRow.Cells[2]);
        }

        private bool CanEdit(DataGridView grid, SaveField field)
        { return field.Editable && (grid != skillFields || selectedSkillView == null || selectedSkillView.FieldLabels.ContainsKey(field.Key)); }

        private void RestoreCombo(ComboBox box, int index)
        { bool previous = binding; binding = true; box.SelectedIndex = index; binding = previous; }

        private void BindInspector(DataGridView grid, DataGridViewCell cell)
        {
            SaveField field = cell.OwningRow.Tag as SaveField;
            if (field == null) return;
            if (fieldInspector.Field == field && fieldInspector.HasPending) { inspectedCell = cell; return; }
            if (fieldInspector.Field != field && !ApplyInspectorDraft())
            {
                if (inspectedCell != null && inspectedCell.DataGridView != null)
                {
                    bool previous = binding; binding = true;
                    inspectedCell.DataGridView.CurrentCell = inspectedCell;
                    binding = previous;
                }
                return;
            }
            inspectedCell = cell;
            string label;
            if (grid != skillFields || selectedSkillView == null || !selectedSkillView.FieldLabels.TryGetValue(field.Key, out label)) label = FieldPresentation.Label(field, chinese);
            fieldInspector.Bind(field, label, SkillFieldDescription(grid, field), catalog, chinese, CanEdit(grid, field));
            BindObjectIcon(grid);
            UpdateState();
            SetStatus(SkillFieldDescription(grid, field));
        }

        private void BindActiveInspector()
        {
            if (tabs.SelectedTab == null) return;
            foreach (DataGridView grid in new[] { characterFields, skillFields, itemFields, locationFields })
            {
                if (!tabs.SelectedTab.Contains(grid)) continue;
                if (grid.CurrentCell != null) BindInspector(grid, grid.CurrentCell.OwningRow.Cells[2]);
                else fieldInspector.Bind(null, "", "", catalog, chinese, false);
                return;
            }
            fieldInspector.Bind(null, "", "", catalog, chinese, false);
        }

        private bool ApplyInspectorDraft()
        {
            if (!fieldInspector.HasPending || fieldInspector.Field == null) return true;
            try
            {
                SaveField field = fieldInspector.Field;
                string before = field.Text;
                field.Text = fieldInspector.DraftValue;
                if (field.Text != before) { undo.Push(new FieldChange { Field = field, Before = before, After = field.Text }); redo.Clear(); }
                fieldInspector.ResetDraft();
                RefreshAppliedValues();
                UpdateState();
                return true;
            }
            catch (Exception ex) { ShowError(ex.Message); return false; }
        }

        private void RefreshAppliedValues()
        {
            bool previous = binding; binding = true;
            foreach (DataGridView grid in new[] { characterFields, skillFields, itemFields, locationFields })
                foreach (DataGridViewRow row in grid.Rows)
                {
                    SaveField field = row.Tag as SaveField;
                    if (field == null || !field.Editable) continue;
                    row.Cells[0].Value = FieldPresentation.Group(field, chinese);
                    if (!allFields.Checked && grid == itemFields && field.Name == "Attribute ID")
                        row.Cells[2].Value = FieldPresentation.EffectName(field, chinese);
                    else row.Cells[2].Value = field.Text;
                    foreach (DataGridViewCell cell in row.Cells) cell.ToolTipText = allFields.Checked ? SkillFieldDescription(grid, field) + "\r\n" + field.Evidence : SkillFieldDescription(grid, field);
                    row.Cells[2].Style.Font = new Font(grid.Font, field.IsModified ? FontStyle.Bold : FontStyle.Regular);
                }
            binding = previous;
        }

        private void UndoChange(bool redoing)
        {
            if (fieldInspector.HasPending) { fieldInspector.ResetDraft(); UpdateState(); return; }
            Stack<FieldChange> source = redoing ? redo : undo, target = redoing ? undo : redo;
            if (source.Count == 0) return;
            TryAction(delegate
            {
                FieldChange change = source.Peek();
                if (change.UndoAction != null)
                {
                    if (redoing) change.RedoAction(); else change.UndoAction();
                    fieldInspector.Bind(null, "", "", catalog, chinese, false);
                    RefreshPartyViews();
                    RefreshRecords();
                }
                else change.Field.Text = redoing ? change.After : change.Before;
                source.Pop(); target.Push(change);
                fieldInspector.ResetDraft();
                RefreshAppliedValues(); UpdateState();
            });
        }

        private ItemRecord SelectedItem()
        {
            ItemRecord selected = items.SelectedItems.Count > 0 ? items.SelectedItems[0].Tag as ItemRecord : activeItem;
            return document != null && selected != null && document.Items.Contains(selected) ? selected : null;
        }

        private void AddNewItem()
        {
            if (document == null || itemTemplates == null || document.Characters.Count == 0 || !ApplyInspectorDraft()) return;
            CharacterRecord preferred = itemOwner.SelectedIndex > 0 ? document.Characters.FirstOrDefault(c => c.Name == Convert.ToString(itemOwner.SelectedItem)) : null;
            if (preferred == null) preferred = character.SelectedItem as CharacterRecord ?? document.Characters[0];
            using (var dialog = new ItemTemplateBrowser(itemTemplates, chinese, ItemTemplateAction.Add, document.Characters, preferred))
            {
                dialog.AddItemRequested = AddGeneratedItem;
                dialog.SkillBooksRequested = ShowSkillBooks;
                dialog.ShowDialog(this);
            }
        }

        private void AddGeneratedItem(CharacterRecord recipient, ItemInitialState initialState)
        {
                    if (document == null || recipient == null || initialState == null || !document.Characters.Contains(recipient))
                        throw new InvalidOperationException("Select a recipient from the current save.");
                    SaveDocument owner = document;
                    ItemRecord added = owner.AddItem(recipient, initialState);
                    AddedItemToken token = owner.CaptureAddedItem(added);
                    undo.Push(new FieldChange { UndoAction = delegate { owner.DetachAddedItem(token); }, RedoAction = delegate { owner.ReattachAddedItem(token); } });
                    redo.Clear(); fieldInspector.Bind(null, "", "", catalog, chinese, false);
                    bool previous = binding; binding = true; itemSearch.Clear(); activeItemQuery = "";
                    itemOwner.SelectedIndex = itemOwner.Items.IndexOf(recipient.Name);
                    activeOwnerIndex = itemOwner.SelectedIndex; binding = previous;
                    BindItems();
                    foreach (ListViewItem row in items.Items) row.Selected = row.Tag == added;
                    activeItem = added; BindFields(itemFields, added.Fields); UpdateState();
                    RefreshRecords();
                    SetStatus((chinese ? "已将新物品添加到 " : "Added the new item to ") + recipient.Name
                        + (chinese ? " 的背包，保存后写入存档。" : "'s backpack. Save to write the slot."));
        }

        private void AddItemModifier()
        {
            ItemRecord item = SelectedItem();
            if (document == null || item == null || !ApplyInspectorDraft()) return;
            if (catalog == null) { ShowError(chinese ? "请先选择游戏目录以读取真实属性类型。" : "Choose the game directory to read its attribute definitions."); return; }
            using (AddModifierDialog dialog = new AddModifierDialog(catalog, DisplayItemName(item), chinese))
            {
                if (dialog.ShowDialog(this) != DialogResult.OK) return;
                TryAction(delegate
                {
                    SaveDocument owner = document;
                    ModifierSnapshot before = owner.CaptureModifiers(item);
                    ItemModifier added = owner.AddModifier(item, dialog.AttributeId, dialog.ModifierValue);
                    ModifierSnapshot after = owner.CaptureModifiers(item);
                    undo.Push(new FieldChange { UndoAction = delegate { owner.RestoreModifiers(item, before); }, RedoAction = delegate { owner.RestoreModifiers(item, after); } });
                    redo.Clear();
                    fieldInspector.Bind(null, "", "", catalog, chinese, false);
                    BindItems();
                    SelectModifierValue(added);
                    SetStatus(chinese ? "已新增实际属性记录，保存后写入存档。" : "Added a real modifier record. Save to write the slot.");
                    UpdateState();
                });
            }
        }

        private void RemoveItemModifier()
        {
            ItemRecord item = SelectedItem();
            SaveField selected = fieldInspector.Field;
            if (document == null || item == null || selected == null || selected.Modifier == null || !item.Modifiers.Contains(selected.Modifier) || !ApplyInspectorDraft()) return;
            TryAction(delegate
            {
                SaveDocument owner = document;
                ModifierSnapshot before = owner.CaptureModifiers(item);
                owner.RemoveModifier(item, selected.Modifier.Index);
                ModifierSnapshot after = owner.CaptureModifiers(item);
                undo.Push(new FieldChange { UndoAction = delegate { owner.RestoreModifiers(item, before); }, RedoAction = delegate { owner.RestoreModifiers(item, after); } });
                redo.Clear();
                fieldInspector.Bind(null, "", "", catalog, chinese, false);
                BindItems(); UpdateState();
                SetStatus(chinese ? "已移除此条属性，可撤销；保存后写入存档。" : "Removed this modifier. Undo is available; save to write the slot.");
            });
        }

        private void SelectModifierValue(ItemModifier modifier)
        {
            SaveField value = modifier.Fields.FirstOrDefault(f => f.Key.EndsWith(".9", StringComparison.Ordinal));
            if (value == null) return;
            foreach (DataGridViewRow row in itemFields.Rows)
                if (row.Tag == value) { itemFields.CurrentCell = row.Cells[2]; itemFields.FirstDisplayedScrollingRowIndex = row.Index; BindInspector(itemFields, row.Cells[2]); break; }
        }

        private void ReplaceSelectedItem()
        {
            ItemRecord item = SelectedItem();
            if (document == null || item == null || itemTemplates == null || !ApplyInspectorDraft()) return;
            using (var dialog = new ItemTemplateBrowser(itemTemplates, chinese, true))
            {
                dialog.Text = (chinese ? "替换物品：" : "Replace item: ") + DisplayItemName(item);
                if (dialog.ShowDialog(this) != DialogResult.OK || dialog.InitialState == null) return;
                TryAction(delegate
                {
                    SaveDocument owner = document;
                    ItemSnapshot before = owner.CaptureItem(item);
                    owner.ReplaceItem(item, dialog.InitialState, dialog.PreserveExistingModifiers);
                    ItemSnapshot after = owner.CaptureItem(item);
                    undo.Push(new FieldChange { UndoAction = delegate { owner.RestoreItem(item, before); }, RedoAction = delegate { owner.RestoreItem(item, after); } });
                    redo.Clear(); fieldInspector.Bind(null, "", "", catalog, chinese, false);
                    bool previous = binding; binding = true; itemSearch.Clear(); binding = previous;
                    BindItems(); UpdateState();
                    RefreshRecords();
                    SetStatus(chinese ? "已按原版模板替换物品并放入背包，保存后写入存档。" : "Replaced the item from its original template and placed it in the backpack. Save to write the slot.");
                });
            }
        }

        private void RunItemQuickAction(bool maximumBonuses)
        { RunItemPreset(maximumBonuses ? 1 : 0); }

        private void RunItemPreset(int profile)
        {
            bool maximumBonuses = profile != 0;
            bool extreme = profile == 2;
            ItemRecord item = SelectedItem();
            if (document == null || item == null || items.SelectedItems.Count != 1 || !ApplyInspectorDraft()) return;
            if (maximumBonuses && itemTemplates == null) return;
            TryAction(delegate
            {
                SaveDocument owner = document;
                ItemSnapshot before = owner.CaptureItem(item);
                QuickActionResult result = !maximumBonuses ? null : extreme
                    ? ItemQuickActions.ApplyExtremeBonuses(owner, item, itemTemplates)
                    : ItemQuickActions.ApplyMaxBonuses(owner, item, itemTemplates);
                int changed = maximumBonuses ? result.ChangedCount : ItemQuickActions.SetRequirementsToOne(owner, item);
                if (changed > 0)
                {
                    ItemSnapshot after = owner.CaptureItem(item);
                    undo.Push(new FieldChange { UndoAction = delegate { owner.RestoreItem(item, before); }, RedoAction = delegate { owner.RestoreItem(item, after); } });
                    redo.Clear(); fieldInspector.Bind(null, "", "", catalog, chinese, false);
                    BindItems(); RefreshRecords(); UpdateState();
                }
                if (extreme)
                    SetStatus(chinese ? (changed == 0 ? "物品已达到本档强化值，未重复添加。" : "已极限强化 " + changed + " 项装备属性；保留已有更高数值和特殊效果。保存后写入存档。") : result.Summary);
                else if (maximumBonuses)
                    SetStatus(chinese ? (changed == 0 ? "装备属性已达到原表上限，未重复添加。" : "已按原表上限处理 " + changed + " 项装备属性；保留更高数值，略过 " + result.SkippedCount + " 种消耗品使用效果。保存后写入存档。") : result.Summary);
                else SetStatus(chinese ? (changed == 0 ? "等级要求和五行属性要求已经都是 1。" : "已将当前及基础等级要求、五行属性要求设为 1。保存后写入存档。") : "Current and base level/element requirements are 1. Save to write the slot.");
            });
        }

        private void ShowItemPresetDetails()
        {
            if (itemTemplates == null) return;
            TryAction(delegate
            {
                using (var dialog = new ItemPresetDetailsDialog(ItemQuickActions.GetExtremeBonuses(itemTemplates), chinese, allFields.Checked))
                    dialog.ShowDialog(this);
            });
        }

        protected override bool ProcessCmdKey(ref Message message, Keys keyData)
        {
            // WinForms routes commands from the focused control to its owning form.
            // Disabled modal owners and hidden forms must not apply item presets.
            if (tabs.SelectedIndex == 2 && Enabled && Visible && IsWindowEnabled(Handle))
            {
                if (keyData == (Keys.Control | Keys.Alt | Keys.A)) { RunItemPreset(2); return true; }
                if (keyData == (Keys.Control | Keys.Alt | Keys.T)) { RunItemQuickAction(true); return true; }
                if (keyData == (Keys.Control | Keys.Alt | Keys.D1)) { RunItemQuickAction(false); return true; }
            }
            return base.ProcessCmdKey(ref message, keyData);
        }

        private void BindCharacter()
        {
            CharacterRecord selected = character.SelectedItem as CharacterRecord;
            BindFields(characterFields, selected == null ? null : selected.Fields);
        }

        private void BindSkills()
        {
            CharacterRecord selected = skillCharacter.SelectedItem as CharacterRecord;
            string selectedId = selectedSkillView == null ? null : selectedSkillView.Id;
            binding = true;
            skillTree.BeginUpdate();
            skillTree.Nodes.Clear();
            TreeNode restore = null;
            if (selected != null)
                foreach (SkillView view in SkillViews.Build(selected, catalog, chinese)) skillTree.Nodes.Add(BuildSkillNode(view, selectedId, ref restore));
            if (skillTree.Nodes.Count > 0) skillTree.SelectedNode = restore ?? skillTree.Nodes[0];
            skillTree.EndUpdate();
            binding = false;
            BindSkillValues();
        }

        private TreeNode BuildSkillNode(SkillView view, string selectedId, ref TreeNode restore)
        {
            TreeNode node = new TreeNode(view.Name) { Tag = view, ToolTipText = view.Role };
            if (gameImages != null) node.ImageKey = node.SelectedImageKey = gameImages.Key(SkillIconId(view));
            if (view.Id == selectedId) restore = node;
            foreach (SkillView child in view.Children) node.Nodes.Add(BuildSkillNode(child, selectedId, ref restore));
            return node;
        }

        private void BindSkillValues()
        {
            if (!ApplyInspectorDraft()) return;
            selectedSkillView = skillTree.SelectedNode == null ? null : skillTree.SelectedNode.Tag as SkillView;
            skillDescription.Text = selectedSkillView == null ? "" : selectedSkillView.Role + Environment.NewLine + Environment.NewLine + selectedSkillView.Description;
            BindFields(skillFields, selectedSkillView == null ? null : selectedSkillView.Fields);
        }

        private string SkillFieldDescription(DataGridView grid, SaveField field)
        {
            string result;
            if (grid == skillFields && selectedSkillView != null && selectedSkillView.FieldDescriptions.TryGetValue(field.Key, out result)) return result;
            return FieldPresentation.Description(field, chinese);
        }

        private void RefreshFieldViews()
        {
            if (!CommitGridEdit()) return;
            foreach (var pair in boundFields.ToArray()) BindFields(pair.Key, pair.Value);
        }

        private void ApplyPresentation()
        {
            if (fieldSearch.IsHandleCreated) SendMessage(fieldSearch.Handle, 0x1501, new IntPtr(1), chinese ? "搜索项目或数值" : "Search properties or values");
            allFields.Text = chinese ? "高级字段" : "Advanced fields";
            extremeBonusesMenuItem.Text = chinese ? "一键极限强化（强力属性）" : "Extreme equipment bonuses";
            maxBonusesMenuItem.Text = chinese ? "添加全部装备属性（原表上限）" : "Add all equipment bonuses at table maxima";
            minimumRequirementsMenuItem.Text = chinese ? "等级、五行属性要求设为 1" : "Set level and element requirements to 1";
            presetDetailsMenuItem.Text = chinese ? "查看强化数值对照…" : "View preset values...";
            if (items.Columns.Count >= 3)
            {
                items.Columns[0].Text = chinese ? "物品" : "Item";
                items.Columns[1].Text = chinese ? "持有人 / 容器" : "Owner / container";
                items.Columns[2].Text = chinese ? "物品代号" : "Alias";
            }
            foreach (DataGridView grid in new[] { characterFields, skillFields, itemFields, locationFields })
            {
                grid.Columns[0].HeaderText = chinese ? "类别" : "Category";
                grid.Columns[1].HeaderText = chinese ? "项目" : "Property";
                grid.Columns[2].HeaderText = chinese ? "当前值" : "Value";
                grid.Columns[3].HeaderText = chinese ? "原值" : "Original";
                grid.Columns[4].HeaderText = chinese ? "原始字段" : "Raw field";
                grid.Columns[5].HeaderText = chinese ? "文件偏移" : "Offset";
                grid.Columns[4].Visible = allFields.Checked;
                grid.Columns[5].Visible = allFields.Checked;
                grid.Columns[3].Visible = allFields.Checked;
                if (grid == skillFields) grid.Columns[0].Visible = allFields.Checked;
            }
            TranslateControls(this);
            UpdateState();
        }

        private void TranslateControls(Control parent)
        {
            Dictionary<string, string> translations = new Dictionary<string, string>
            {
                { "Open save...", "打开存档…" }, { "Save with backup", "保存（自动备份）" },
                { "Export copy...", "导出修改副本…" }, { "Restore backup...", "恢复备份…" },
                { "Readable data...", "导出数据说明…" }, { "Game folder...", "游戏目录…" },
                { "Refresh slots", "刷新存档" }, { "Item catalog...", "物品图鉴…" },
                { "Playthrough...", "周目与结局…" },
                { "Game data TXT...", "游戏数据 TXT…" }, { "Add companion...", "添加队友…" },
                { "Learn class skills", "一键学习本职业全部技能" }, { "Max class skills", "一键全部技能满级" },
                { "Skill books...", "技能书／宝典…" },
                { "Characters", "人物" }, { "Skills", "技能" }, { "Items & equipment", "背包与装备" },
                { "Location", "地图与位置" }, { "Records", "高级数据" }, { "SAVE SLOTS", "存档列表" },
                { "Undo", "撤销" }, { "Redo", "重做" }
                , { "Add modifier...", "添加属性…" }, { "Remove modifier", "移除所选属性" }
                , { "Replace item...", "替换物品…" }
                , { "Add item...", "添加物品…" }
                , { "Shortcuts", "快捷操作" }
            };
            foreach (Control control in parent.Controls)
            {
                if (control is Button || control is TabPage || control is Label && control.Text == "SAVE SLOTS" || control.Tag is string && translations.ContainsKey((string)control.Tag))
                {
                    if (control.Tag == null && translations.ContainsKey(control.Text)) control.Tag = control.Text;
                    string key = control.Tag as string;
                    if (key != null && translations.ContainsKey(key)) control.Text = chinese ? translations[key] : key;
                }
                TranslateControls(control);
            }
        }

        private void BindItems()
        {
            ItemRecord selected = items.SelectedItems.Count == 0 ? null : items.SelectedItems[0].Tag as ItemRecord;
            items.BeginUpdate();
            items.Items.Clear();
            if (document != null)
            {
                string owner = itemOwner.SelectedIndex <= 0 ? null : Convert.ToString(itemOwner.SelectedItem);
                string search = itemSearch.Text.Trim();
                foreach (ItemRecord item in document.Items)
                {
                    if (owner != null && item.OwnerName != owner) continue;
                    string displayName = DisplayItemName(item);
                    ItemDefinition definition = catalog == null ? null : catalog.FindItem(item.Alias);
                    string searchNames = definition == null ? "" : definition.Name + " " + definition.ChineseName + " " + definition.OriginalName;
                    if (search.Length > 0 && (displayName + " " + searchNames + " " + item.Name + " " + item.Alias + " " + item.OwnerName).IndexOf(search, StringComparison.OrdinalIgnoreCase) < 0) continue;
                    ListViewItem row = new ListViewItem(displayName);
                    if (gameImages != null) row.ImageKey = gameImages.Key(ItemIconId(item));
                    if (definition != null) row.ToolTipText = definition.GetNameDescription(chinese);
                    row.SubItems.Add(item.OwnerName);
                    row.SubItems.Add(item.Alias);
                    row.Tag = item;
                    items.Items.Add(row);
                    if (item == selected) row.Selected = true;
                }
            }
            items.EndUpdate();
            if (items.SelectedItems.Count == 0 && items.Items.Count > 0) items.Items[0].Selected = true;
            if (items.Items.Count == 0) BindFields(itemFields, null);
        }

        private string DisplayItemName(ItemRecord item)
        {
            ScriptBookDefinition book = scriptBooks == null ? null : scriptBooks.Find(item.Node.Path);
            if (book != null) return book.GetDisplayName(chinese);
            if (catalog != null)
            {
                ItemDefinition definition = catalog.FindItem(item.Alias);
                if (definition != null && !String.IsNullOrEmpty(definition.Name)) return definition.GetDisplayName(chinese);
            }
            return String.IsNullOrEmpty(item.Name) ? item.Alias : item.Name;
        }

        private void BindDocument()
        {
            fieldInspector.Bind(null, "", "", catalog, chinese, false);
            undo.Clear(); redo.Clear(); inspectedCell = null;
            loadedGamHash = document.OriginalGamSha256;
            loadedUserHash = document.OriginalUserSha256;
            binding = true;
            character.Items.Clear();
            skillCharacter.Items.Clear();
            character.DisplayMember = "Name";
            skillCharacter.DisplayMember = "Name";
            foreach (CharacterRecord entry in document.Characters)
            {
                character.Items.Add(entry);
                skillCharacter.Items.Add(entry);
            }
            if (character.Items.Count > 0) character.SelectedIndex = 0;
            if (skillCharacter.Items.Count > 0) skillCharacter.SelectedIndex = 0;
            itemOwner.Items.Clear();
            itemOwner.Items.Add("All owners");
            foreach (string owner in document.Characters.Select(c => c.Name).Concat(document.Items.Select(i => i.OwnerName)).Distinct()) itemOwner.Items.Add(owner);
            itemOwner.SelectedIndex = 0;
            activeCharacterIndex = character.SelectedIndex;
            activeSkillCharacterIndex = skillCharacter.SelectedIndex;
            activeOwnerIndex = itemOwner.SelectedIndex;
            activeItem = null;
            activeItemQuery = itemSearch.Text;
            RefreshRecords();
            binding = false;
            BindCharacter();
            BindSkills();
            BindItems();
            BindFields(locationFields, document.Location);
            recordDetail.Text = document.Summary;
            subtitle.Text = Path.GetFileName(document.FilePath) + "  |  " + document.Characters.Count + " party members  |  " + document.Items.Count + " items";
            BindActiveInspector();
            UpdateState();
        }

        private bool CommitGridEdit()
        {
            return ApplyInspectorDraft();
        }

        private void RefreshRecords()
        {
            records.BeginUpdate(); records.Items.Clear();
            if (document != null) foreach (RecordInfo record in document.Inspector)
            {
                ListViewItem row = new ListViewItem(record.Name);
                row.SubItems.Add(record.Length.ToString("N0"));
                row.SubItems.Add(record.Detail.Replace("\r", " ").Replace("\n", " "));
                row.Tag = record; records.Items.Add(row);
            }
            records.EndUpdate();
            recordDetail.Text = document == null ? "" : document.Summary;
        }

        private bool ConfirmDiscard()
        {
            if (document != null && !CommitGridEdit()) return false;
            if (document == null || !document.IsModified) return true;
            return MessageBox.Show(this, "Discard the unsaved changes?", Text, MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes;
        }

        private void BrowseSave()
        {
            using (OpenFileDialog dialog = new OpenFileDialog { Filter = "Seal of Evil save (*.gam)|*.gam|All files (*.*)|*.*", InitialDirectory = SaveDirectory(), Title = "Open a saved game" })
                if (dialog.ShowDialog(this) == DialogResult.OK) OpenSave(dialog.FileName);
        }

        private void OpenSave(string path)
        {
            if (!ConfirmDiscard()) { SelectCurrentSave(); return; }
            TryAction(delegate
            {
                SaveDocument loaded = SaveDocument.Load(path);
                document = loaded;
                string detected = DetectGameDirectory(path);
                if (!String.IsNullOrEmpty(detected) && detected != gameDirectory) { gameDirectory = detected; LoadCatalog(); }
                BindDocument();
                SetStatus("Loaded " + path + ". Changes remain in the editor until you save.");
            });
            SelectCurrentSave();
        }

        private void SelectCurrentSave()
        {
            binding = true;
            saves.SelectedIndex = -1;
            if (document != null)
                for (int i = 0; i < saves.Items.Count; i++)
                    if (String.Equals(((SaveEntry)saves.Items[i]).Path, document.FilePath, StringComparison.OrdinalIgnoreCase)) { saves.SelectedIndex = i; break; }
            binding = false;
        }

        private void LoadCatalog()
        {
            catalog = null;
            itemTemplates = null;
            scriptBooks = null;
            items.SmallImageList = null; skillTree.ImageList = null;
            if (gameImages != null) { gameImages.Dispose(); gameImages = null; }
            if (String.IsNullOrEmpty(gameDirectory)) return;
            try { catalog = GameCatalog.Load(gameDirectory); itemTemplates = ItemTemplateCatalog.Load(gameDirectory, catalog); }
            catch (Exception ex) { SetStatus("Game definitions: " + ex.Message); }
            try { scriptBooks = ScriptItemCatalog.Load(gameDirectory); }
            catch (Exception ex) { SetStatus("Skill books: " + ex.Message); }
            try { gameImages = new GameIconImages(gameDirectory); items.SmallImageList = gameImages.Images; skillTree.ImageList = gameImages.Images; }
            catch (Exception ex) { SetStatus("Game icons: " + ex.Message); }
        }

        private string SaveDirectory()
        {
            string folder = String.IsNullOrEmpty(gameDirectory) ? Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments) : Path.Combine(gameDirectory, "System", "game");
            return Directory.Exists(folder) ? folder : Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
        }

        private void RefreshSaves()
        {
            binding = true;
            saves.Items.Clear();
            string folder = SaveDirectory();
            if (Directory.Exists(folder))
                foreach (string path in Directory.GetFiles(folder, "player_*.gam").OrderBy(x => x, StringComparer.OrdinalIgnoreCase)) saves.Items.Add(new SaveEntry(path));
            binding = false;
            if (saves.Items.Count == 0) SetStatus("Use Open save to select a .gam file, or choose the game folder.");
        }

        private void ChooseGameFolder()
        {
            using (FolderBrowserDialog dialog = new FolderBrowserDialog { Description = "Select the Seal of Evil folder containing SOE.EXE", SelectedPath = gameDirectory ?? "" })
            {
                if (dialog.ShowDialog(this) != DialogResult.OK) return;
                if (!File.Exists(Path.Combine(dialog.SelectedPath, "SOE.EXE"))) { ShowError("This folder does not contain SOE.EXE."); return; }
                gameDirectory = dialog.SelectedPath;
                LoadCatalog();
                RefreshSaves();
                if (document != null) BindItems();
            }
        }

        private static string DetectGameDirectory(string savePath)
        {
            if (!String.IsNullOrEmpty(savePath))
            {
                DirectoryInfo parent = new FileInfo(Path.GetFullPath(savePath)).Directory;
                for (int i = 0; parent != null && i < 5; i++, parent = parent.Parent)
                    if (File.Exists(Path.Combine(parent.FullName, "SOE.EXE"))) return parent.FullName;
            }
            List<string> steamPaths = new List<string>();
            using (RegistryKey key = Registry.CurrentUser.OpenSubKey(@"Software\Valve\Steam"))
                if (key != null && key.GetValue("SteamPath") is string) steamPaths.Add((string)key.GetValue("SteamPath"));
            steamPaths.Add(@"D:\Steam");
            steamPaths.Add(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Steam"));
            foreach (string steam in steamPaths.ToArray())
            {
                string libraries = Path.Combine(steam, "steamapps", "libraryfolders.vdf");
                if (!File.Exists(libraries)) continue;
                foreach (Match match in Regex.Matches(File.ReadAllText(libraries), "\"path\"\\s+\"([^\"]+)\"")) steamPaths.Add(match.Groups[1].Value.Replace("\\\\", "\\"));
            }
            foreach (string steam in steamPaths.Distinct(StringComparer.OrdinalIgnoreCase))
            {
                string candidate = Path.Combine(steam, "steamapps", "common", "Seal of Evil");
                if (File.Exists(Path.Combine(candidate, "SOE.EXE"))) return candidate;
            }
            return null;
        }

        private void RequireGameClosed()
        {
            if (Process.GetProcessesByName("SOE").Length > 0) throw new InvalidOperationException("Exit Seal of Evil before saving or restoring a slot, so the game can load the edited data.");
        }

        private IEnumerable<KeyValuePair<string, string>> SlotFiles()
        {
            string gameFolder = Path.GetDirectoryName(document.FilePath);
            string stem = Path.GetFileNameWithoutExtension(document.FilePath);
            foreach (string source in Directory.GetFiles(gameFolder, stem + ".*"))
                yield return new KeyValuePair<string, string>(source, Path.Combine("System", "game", Path.GetFileName(source)));
            DirectoryInfo system = Directory.GetParent(gameFolder);
            if (system != null)
            {
                string user = Path.Combine(system.FullName, "user", stem + ".use");
                if (File.Exists(user)) yield return new KeyValuePair<string, string>(user, Path.Combine("System", "user", Path.GetFileName(user)));
            }
        }

        private string BackupRoot()
        {
            return Path.Combine(Path.GetDirectoryName(document.FilePath), "SealSaveEditor-backups");
        }

        private void SaveCurrent()
        {
            if (document == null || !CommitGridEdit()) return;
            TryAction(delegate
            {
                if (!document.IsModified) { UpdateState(); SetStatus("There are no changes to save."); return; }
                ItemRecord selected = SelectedItem();
                string ownerName = selected == null ? null : selected.OwnerName;
                string alias = selected == null ? null : selected.Alias;
                string position = selected == null ? null : selected.Fields.First(f => f.Key == "attr.161").Text;
                string original = document.FilePath;
                string backup = SaveSlotStore.Save(document, loadedGamHash, loadedUserHash);
                document = SaveDocument.Load(original);
                BindDocument();
                ItemRecord restored = selected == null ? null : document.Items.FirstOrDefault(i => i.OwnerName == ownerName && i.Alias == alias && i.Fields.First(f => f.Key == "attr.161").Text == position);
                if (restored != null) foreach (ListViewItem row in items.Items) row.Selected = row.Tag == restored;
                SetStatus("Saved. Original slot backup: " + backup);
            });
        }

        private void CopySlot(string destination, bool includeEdits)
        {
            SaveSlotStore.Export(document, destination, includeEdits);
        }

        private void ExportCopy()
        {
            if (document == null || !CommitGridEdit()) return;
            using (FolderBrowserDialog dialog = new FolderBrowserDialog { Description = "Choose a new or empty folder for the edited slot and its companion files", ShowNewFolderButton = true })
            {
                if (dialog.ShowDialog(this) != DialogResult.OK) return;
                TryAction(delegate { SaveSlotStore.ExportEdited(document, dialog.SelectedPath, loadedGamHash, loadedUserHash); SetStatus("Exported edited slot to " + dialog.SelectedPath); });
            }
        }

        private void RestoreBackup()
        {
            if (document == null || !ConfirmDiscard()) return;
            string root = BackupRoot();
            if (!Directory.Exists(root)) { ShowError("There are no backups for this save location yet."); return; }
            using (OpenFileDialog dialog = new OpenFileDialog { Title = "Choose a backed-up .gam from a timestamped backup folder", Filter = "Saved game (*.gam)|*.gam", InitialDirectory = root })
            {
                if (dialog.ShowDialog(this) != DialogResult.OK) return;
                TryAction(delegate
                {
                    string currentPath = document.FilePath;
                    string backup = SaveSlotStore.Restore(document, dialog.FileName, loadedGamHash, loadedUserHash);
                    document = SaveDocument.Load(currentPath);
                    BindDocument();
                    SetStatus("Restored the saved game. The previous state was backed up to " + backup);
                });
            }
        }

        private void ExportReport()
        {
            if (document == null || !CommitGridEdit()) return;
            using (SaveFileDialog dialog = new SaveFileDialog { Filter = "Readable save report (*.txt)|*.txt", FileName = Path.GetFileNameWithoutExtension(document.FilePath) + "-readable.txt" })
            {
                if (dialog.ShowDialog(this) != DialogResult.OK) return;
                TryAction(delegate
                {
                    StringBuilder text = new StringBuilder();
                    text.AppendLine("Seal of Evil - readable save data");
                    text.AppendLine(document.Summary);
                    text.AppendLine("Field\tValue\tAccess\tEvidence");
                    foreach (CharacterRecord entry in document.Characters)
                    {
                        text.AppendLine("\r\nCHARACTER: " + entry.Name);
                        AppendFields(text, entry.Fields);
                        text.AppendLine("SKILLS");
                        AppendFields(text, entry.Skills);
                    }
                    text.AppendLine("\r\nLOCATION");
                    AppendFields(text, document.Location);
                    foreach (ItemRecord entry in document.Items)
                    {
                        text.AppendLine("\r\nITEM: " + DisplayItemName(entry) + " / " + entry.OwnerName + " / " + entry.Alias);
                        AppendFields(text, entry.Fields);
                    }
                    text.AppendLine("\r\nRECORDS");
                    foreach (RecordInfo entry in document.Inspector) text.AppendLine(entry.Name + "\t" + entry.Offset + "\t" + entry.Length + "\t" + entry.Detail);
                    File.WriteAllText(dialog.FileName, text.ToString(), new UTF8Encoding(true));
                    SetStatus("Exported readable data to " + dialog.FileName);
                });
            }
        }

        private static void AppendFields(StringBuilder text, IEnumerable<SaveField> fields)
        {
            foreach (SaveField field in fields) text.AppendLine(FieldPresentation.Group(field, false) + "/" + FieldPresentation.Label(field, false) + "\t" + field.Text + "\t" + (field.Editable ? "editable" : "read only") + "\t" + field.Key + "\t" + FieldPresentation.Description(field, false));
        }

        private void ShowCatalog()
        {
            if (itemTemplates == null) { ShowError(chinese ? "请先选择游戏目录以读取原版物品模板。" : "Choose the game folder to read its original item templates."); return; }
            using (var dialog = new ItemTemplateBrowser(itemTemplates, chinese, false))
            { dialog.SkillBooksRequested = ShowSkillBooks; dialog.ShowDialog(this); }
        }

        private void ShowCampaign()
        {
            if (document == null || !ApplyInspectorDraft()) return;
            using (var dialog = new CampaignDialog(delegate { return CampaignProgress.Read(document); }, delegate {
                SaveDocument owner = document;
                CampaignProgressSnapshot before = CampaignProgress.Capture(owner);
                if (!CampaignProgress.EnableSecondPlaythrough(owner)) return;
                CampaignProgressSnapshot after = CampaignProgress.Capture(owner);
                undo.Push(new FieldChange { UndoAction = delegate { CampaignProgress.Restore(owner, before); }, RedoAction = delegate { CampaignProgress.Restore(owner, after); } });
                redo.Clear(); fieldInspector.Bind(null, "", "", catalog, chinese, false);
                RefreshFieldViews(); RefreshRecords(); UpdateState();
                SetStatus(chinese ? "已启用此存档的二周目资格，保留当前剧情进度。保存后写入存档。" : "Second-playthrough eligibility enabled; current story progress retained. Save to write the slot.");
            }, chinese)) dialog.ShowDialog(this);
        }

        private void UpdateState()
        {
            bool loaded = document != null;
            saveButton.Enabled = loaded && (document.IsModified || fieldInspector.HasPending);
            undoButton.Enabled = undo.Count > 0 || fieldInspector.HasPending;
            redoButton.Enabled = redo.Count > 0;
            exportButton.Enabled = loaded;
            campaignButton.Enabled = loaded;
            dataButton.Enabled = !actorBusy && !String.IsNullOrEmpty(gameDirectory);
            partyButton.Enabled = loaded && !actorBusy;
            learnSkillsButton.Enabled = maxSkillsButton.Enabled = loaded && !actorBusy && catalog != null && scriptBooks != null;
            skillBooksButton.Enabled = loaded && scriptBooks != null;
            itemCatalogButton.Enabled = itemTemplates != null;
            ItemRecord selectedItem = SelectedItem();
            addModifierButton.Enabled = loaded && selectedItem != null && catalog != null;
            addItemButton.Enabled = loaded && document.Characters.Count > 0 && itemTemplates != null;
            replaceItemButton.Enabled = loaded && selectedItem != null && itemTemplates != null;
            quickActionsButton.Enabled = loaded && selectedItem != null && items.SelectedItems.Count == 1;
            extremeBonusesMenuItem.Enabled = quickActionsButton.Enabled && itemTemplates != null;
            maxBonusesMenuItem.Enabled = quickActionsButton.Enabled && itemTemplates != null;
            minimumRequirementsMenuItem.Enabled = quickActionsButton.Enabled;
            presetDetailsMenuItem.Enabled = itemTemplates != null;
            removeModifierButton.Enabled = loaded && selectedItem != null && fieldInspector.Field != null && fieldInspector.Field.Modifier != null && selectedItem.Modifiers.Contains(fieldInspector.Field.Modifier);
            if (!loaded) { summary.Text = "Open a save to inspect its characters, items, skills and location."; return; }
            subtitle.Text = Path.GetFileName(document.FilePath) + "  |  " + document.Characters.Count + (chinese ? " 位队伍人物  |  " : " party members  |  ") + document.Items.Count + (chinese ? " 件物品" : " items");
            summary.Text = document.Characters.Count + (chinese ? " 位队伍人物\r\n" : " characters\r\n") + document.Items.Count + (chinese ? " 件物品\r\n" : " items\r\n") + document.Inspector.Count + (chinese ? " 条记录\r\n\r\n" : " records\r\n\r\n") + (fieldInspector.HasPending ? (chinese ? "有待应用输入" : "Pending input") : document.IsModified ? (chinese ? "有未保存修改" : "Unsaved changes") : (chinese ? "没有未保存修改" : "No unsaved changes"));
            Text = (document.IsModified ? "* " : "") + "Seal of Evil Save Editor - " + Path.GetFileName(document.FilePath);
        }

        private void TryAction(Action action)
        {
            try { Cursor = Cursors.WaitCursor; action(); }
            catch (Exception ex) { ShowError(ex.Message); }
            finally { Cursor = Cursors.Default; }
        }

        private void ShowError(string message)
        {
            SetStatus(message);
            MessageBox.Show(this, message, "Seal of Evil Save Editor", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }

        private void SetStatus(string message) { status.Text = message; }

        private static string DisplayGroup(string group)
        {
            if (group == "Character attributes") return "Character";
            if (group == "Item / equipment") return "Item";
            if (group == "Base attributes") return "Base stats";
            if (group == "Bonuses / effects") return "Bonuses";
            if (group == "String attributes") return "Properties";
            return group;
        }

        private static string DisplayFieldName(string name)
        {
            switch (name)
            {
                case "HP": return "Health";
                case "HP_MAX": return "Maximum health";
                case "MP": return "Mana";
                case "MP_MAX": return "Maximum mana";
                case "Exp": return "Experience";
                case "NextExp": return "Next-level experience";
                case "Gold": return "Metal element";
                case "Wood": return "Wood element";
                case "Water": return "Water element";
                case "Fire": return "Fire element";
                case "Soil": return "Earth element";
                case "Gold0": return "Base metal";
                case "Wood0": return "Base wood";
                case "Water0": return "Base water";
                case "Fire0": return "Base fire";
                case "Soil0": return "Base earth";
                case "AP": return "Attack power";
                case "AP0": return "Base attack power";
                case "G_PosX": return "X coordinate";
                case "G_PosY": return "Y coordinate";
                case "Amount": return "Quantity";
                default: return name;
            }
        }

        private sealed class SaveEntry
        {
            public string Path;
            public SaveEntry(string path) { Path = path; }
            public override string ToString() { return System.IO.Path.GetFileNameWithoutExtension(Path) + "   " + File.GetLastWriteTime(Path).ToString("MM-dd HH:mm", CultureInfo.InvariantCulture); }
        }

        private sealed class AttributeChoice
        {
            public string Id { get; set; }
            public string Label { get; set; }
        }

        private sealed class FieldChange
        {
            public SaveField Field;
            public string Before, After;
            public Action UndoAction, RedoAction;
        }
    }
}
