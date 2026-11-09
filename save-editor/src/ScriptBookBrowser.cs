using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace SealSaveEditor
{
    public sealed class ScriptBookBrowser : Form
    {
        private sealed class Filter { public int Id; public string Kind, Label; public override string ToString() { return Label; } }
        private readonly ScriptItemCatalog catalog;
        private readonly bool chinese;
        private readonly List<CharacterRecord> recipients;
        private readonly TextBox search = new TextBox();
        private readonly ComboBox classes = new ComboBox(), kinds = new ComboBox(), recipient = new ComboBox();
        private readonly ListView books = new ListView();
        private readonly TextBox details = new TextBox();
        private readonly Label count = new Label(), status = new Label(), title = new Label();
        private readonly Button add = new Button();
        private readonly CheckBox advanced = new CheckBox();
        private readonly PictureBox picture = new PictureBox();
        private readonly ImageList images = new ImageList();
        private readonly Dictionary<int, int> imageIndices = new Dictionary<int, int>();
        private readonly Dictionary<string, int> skillIcons = new Dictionary<string, int>(StringComparer.Ordinal);
        private readonly Dictionary<int, int> shellIcons = new Dictionary<int, int>();
        private readonly List<Bitmap> retainedImages = new List<Bitmap>();
        private readonly SemaphoreSlim workerGate = new SemaphoreSlim(1, 1);
        private readonly CancellationTokenSource closing = new CancellationTokenSource();
        private readonly List<Task> workers = new List<Task>();
        private GameIconLoader iconLoader;
        private ScriptBookDefinition selected;
        private ItemInitialState preview;
        private bool loading, preparing;
        private volatile bool closed;
        private int generation, pendingDoubleClick = -1;
        private CharacterRecord pendingRecipient;
        public event Action<CharacterRecord, ItemInitialState> AddItemRequested;

        public ScriptBookBrowser(ScriptItemCatalog definitions, IEnumerable<CharacterRecord> owners, CharacterRecord initialOwner, bool zh)
        {
            if (definitions == null) throw new ArgumentNullException("definitions");
            catalog = definitions; chinese = zh; recipients = owners == null ? new List<CharacterRecord>() : owners.Where(o => o != null).Distinct().ToList();
            Font = UiFonts.Create(); Text = T("技能书与职业宝典", "Skill Books and Class Manuals");
            BackColor = Color.FromArgb(236, 232, 223); Size = new Size(1190, 800); MinimumSize = new Size(980, 650); StartPosition = FormStartPosition.CenterParent;
            var root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3, Padding = new Padding(14) };
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 48)); root.RowStyles.Add(new RowStyle(SizeType.Percent, 100)); root.RowStyles.Add(new RowStyle(SizeType.Absolute, 112));
            var filters = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false };
            filters.Controls.Add(new Label { Text = T("搜索技能或书名", "Find a skill or book"), AutoSize = true, Padding = new Padding(0, 5, 3, 0) });
            search.Name = "BookSearch"; search.Width = 270;
            classes.Name = "BookClassFilter"; classes.DropDownStyle = ComboBoxStyle.DropDownList; classes.Width = 170;
            classes.Items.Add(new Filter { Id = 0, Label = T("全部职业", "All classes") });
            string[] nativeClassNames = { "", "游侠", "巫师", "祭司", "刀客", "兽人" };
            for (int i = 1; i <= 5; i++) classes.Items.Add(new Filter { Id = i, Label = zh ? nativeClassNames[i] : ScriptBookDefinition.ClassEnglish(i) });
            classes.Items.Add(new Filter { Id = 6, Label = T("打造（巫师）", "Crafting (Wizard)") });
            kinds.Name = "BookKindFilter"; kinds.DropDownStyle = ComboBoxStyle.DropDownList; kinds.Width = 190;
            kinds.Items.Add(new Filter { Kind = "", Label = T("全部书籍种类", "All book kinds") });
            foreach (string kind in new[] { "ClassManual", "Learn", "Upgrade", "LearnTier2", "LearnTier3", "Special" }) kinds.Items.Add(new Filter { Kind = kind, Label = ScriptBookDefinition.KindLabel(kind, zh) });
            count.Name = "BookCount"; count.AutoSize = true; count.Padding = new Padding(5, 5, 0, 0);
            filters.Controls.Add(search); filters.Controls.Add(classes); filters.Controls.Add(kinds); filters.Controls.Add(count); root.Controls.Add(filters, 0, 0);
            var split = new SplitContainer { Dock = DockStyle.Fill, Size = new Size(1150, 590), SplitterDistance = 555, Panel1MinSize = 380, Panel2MinSize = 330 };
            images.ImageSize = new Size(32, 32); images.ColorDepth = ColorDepth.Depth32Bit; images.TransparentColor = Color.Transparent;
            books.Name = "ScriptBooks"; books.Dock = DockStyle.Fill; books.View = View.Details; books.FullRowSelect = true; books.MultiSelect = false; books.HideSelection = false; books.ShowItemToolTips = true; books.SmallImageList = images;
            books.Columns.Add(T("技能书 / 宝典", "Skill book / manual"), 300); books.Columns.Add(T("职业", "Class"), 100); books.Columns.Add(T("种类", "Kind"), 130);
            books.SelectedIndexChanged += delegate { if (!loading) SelectBook(); };
            books.DoubleClick += delegate { AddSelected(true); }; split.Panel1.Controls.Add(books);
            var detailLayout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3, Padding = new Padding(10, 0, 0, 0) };
            detailLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 83)); detailLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100)); detailLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 30));
            var bookHeading = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1 };
            bookHeading.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 76)); bookHeading.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            picture.Name = "BookIcon"; picture.Dock = DockStyle.Fill; picture.SizeMode = PictureBoxSizeMode.Zoom; picture.Margin = new Padding(6); bookHeading.Controls.Add(picture, 0, 0);
            title.Name = "BookTitle"; title.Dock = DockStyle.Fill; title.Font = UiFonts.Create(12F, FontStyle.Bold); title.TextAlign = ContentAlignment.MiddleLeft; bookHeading.Controls.Add(title, 1, 0);
            detailLayout.Controls.Add(bookHeading, 0, 0);
            details.Name = "BookDetails"; details.Dock = DockStyle.Fill; details.Multiline = true; details.ReadOnly = true; details.ScrollBars = ScrollBars.Vertical; details.BackColor = Color.FromArgb(248, 246, 240);
            detailLayout.Controls.Add(details, 0, 1);
            advanced.Name = "BookAdvanced"; advanced.Dock = DockStyle.Fill; advanced.Text = T("显示原始对象路径与代号", "Show original object path and alias"); advanced.CheckedChanged += delegate { ShowDetails(); };
            detailLayout.Controls.Add(advanced, 0, 2); split.Panel2.Controls.Add(detailLayout); root.Controls.Add(split, 0, 1);
            var footer = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3, Padding = new Padding(0, 7, 0, 0) };
            footer.RowStyles.Add(new RowStyle(SizeType.Absolute, 24)); footer.RowStyles.Add(new RowStyle(SizeType.Absolute, 39)); footer.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            footer.Controls.Add(new Label { Text = T("选中自动预览；双击或点击“添加”可连续添加，窗口不关闭。原书等级固定为1。", "Selection prepares the native book automatically. Double-click or Add repeatedly; this window stays open. Original book level is fixed at 1."), Dock = DockStyle.Fill, AutoEllipsis = true }, 0, 0);
            var actions = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false };
            actions.Controls.Add(new Label { Text = T("接收角色", "Recipient"), AutoSize = true, Padding = new Padding(0, 6, 5, 0) });
            recipient.Name = "BookRecipient"; recipient.Width = 285; recipient.DropDownStyle = ComboBoxStyle.DropDownList;
            foreach (CharacterRecord owner in recipients) recipient.Items.Add(owner);
            if (recipients.Contains(initialOwner)) recipient.SelectedItem = initialOwner; else if (recipients.Count > 0) recipient.SelectedIndex = 0;
            recipient.SelectedIndexChanged += delegate { pendingDoubleClick = -1; pendingRecipient = null; UpdateAddState(); };
            add.Name = "AddScriptBook"; add.Text = T("添加到角色背包", "Add to recipient's backpack"); add.Width = 235; add.Height = 31; add.BackColor = Color.FromArgb(186, 153, 88); add.Click += delegate { AddSelected(false); };
            actions.Controls.Add(recipient); actions.Controls.Add(add); footer.Controls.Add(actions, 0, 1);
            status.Name = "BookStatus"; status.Dock = DockStyle.Fill; status.AutoEllipsis = true; footer.Controls.Add(status, 0, 2); root.Controls.Add(footer, 0, 2); Controls.Add(root);
            classes.SelectedIndex = 0; kinds.SelectedIndex = 0;
            if (initialOwner != null)
            {
                SaveField job = initialOwner.Fields.FirstOrDefault(f => f.Key == "attr.1");
                if (job != null && job.Value >= 1 && job.Value <= 5) classes.SelectedIndex = (int)job.Value;
            }
            search.TextChanged += delegate { RefreshList(); }; classes.SelectedIndexChanged += delegate { RefreshList(); }; kinds.SelectedIndexChanged += delegate { RefreshList(); };
            Load += delegate
            {
                try { iconLoader = new GameIconLoader(catalog.GameDirectory); ReadIconMappings(); } catch (IOException) { } catch (UnauthorizedAccessException) { } catch (ArgumentException) { }
                RefreshList();
            };
            FormClosed += delegate { CloseWorkers(); };
            UpdateAddState();
        }

        private string T(string zh, string en) { return chinese ? zh : en; }

        private void RefreshList()
        {
            if (closed) return;
            string query = search.Text.Trim(); Filter job = classes.SelectedItem as Filter, kind = kinds.SelectedItem as Filter;
            string oldPath = selected == null ? null : selected.Path;
            loading = true; books.BeginUpdate();
            try
            {
                books.Items.Clear();
                foreach (ScriptBookDefinition b in catalog.Books.Where(b => job == null || job.Id == 0 || job.Id == 6 && b.Category == "打造" || job.Id <= 5 && b.ClassId == job.Id)
                    .Where(b => kind == null || String.IsNullOrEmpty(kind.Kind) || b.BookKind == kind.Kind)
                    .Where(b => query.Length == 0 || (b.ChineseName + " " + b.Name + " " + b.Path + " " + b.Alias).IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0)
                    .OrderBy(b => b.IsManual ? 0 : 1).ThenBy(b => b.GetDisplayName(chinese), StringComparer.CurrentCultureIgnoreCase))
                {
                    var row = new ListViewItem(b.GetDisplayName(chinese)) { Tag = b, ToolTipText = b.Name + Environment.NewLine + b.ChineseName };
                    row.SubItems.Add(b.Category == "打造" ? T("巫师 / 打造", "Wizard / crafting") : chinese ? b.ClassName : ScriptBookDefinition.ClassEnglish(b.ClassId));
                    row.SubItems.Add(ScriptBookDefinition.KindLabel(b.BookKind, chinese)); row.ImageIndex = ImageIndex(IconId(b)); books.Items.Add(row);
                    if (b.Path == oldPath) row.Selected = true;
                }
                if (books.SelectedItems.Count == 0 && books.Items.Count > 0) books.Items[0].Selected = true;
                count.Text = books.Items.Count.ToString(CultureInfo.InvariantCulture) + " / " + catalog.Books.Count.ToString(CultureInfo.InvariantCulture);
            }
            finally { books.EndUpdate(); loading = false; }
            SelectBook();
        }

        private void SelectBook()
        {
            if (closed) return;
            selected = books.SelectedItems.Count == 0 ? null : books.SelectedItems[0].Tag as ScriptBookDefinition;
            preview = null; pendingDoubleClick = -1; pendingRecipient = null;
            int request = Interlocked.Increment(ref generation);
            ShowDetails(); UpdatePicture(selected == null ? 0 : IconId(selected)); UpdateAddState();
            preparing = selected != null;
            if (selected == null) { status.Text = T("没有匹配的技能书。", "No matching books."); return; }
            ScriptBookDefinition book = selected;
            status.ForeColor = SystemColors.ControlText; status.Text = T("正在准备原版技能书…", "Preparing the original skill book…");
            Task task = Task.Run(async delegate
            {
                ItemInitialState result = null; Exception error = null;
                try
                {
                    await workerGate.WaitAsync(closing.Token).ConfigureAwait(false);
                    try
                    {
                        if (closing.IsCancellationRequested || request != Volatile.Read(ref generation)) return;
                        result = ScriptItemFactory.Create(catalog, book, 12345);
                    }
                    finally { workerGate.Release(); }
                }
                catch (OperationCanceledException) { return; }
                catch (Exception e) { error = e; }
                if (closed || IsDisposed || !IsHandleCreated) return;
                try
                {
                    BeginInvoke((Action)delegate
                    {
                        if (closed || IsDisposed || request != generation || selected != book) return;
                        preparing = false;
                        if (error != null)
                        {
                            status.ForeColor = Color.Firebrick; status.Text = T("技能书预览失败：", "Book preview failed: ") + error.Message; preview = null;
                        }
                        else
                        {
                            preview = result; status.ForeColor = Color.FromArgb(40, 90, 45); status.Text = T("已准备好，可连续添加。角色职业与学习条件由原游戏检查。", "Ready for repeated addition. The original game checks class and learning requirements.");
                            int actualIcon = result.IntegerAttributes[102];
                            if (IconId(book) <= 0) UpdatePicture(actualIcon);
                        }
                        UpdateAddState();
                        if (preview != null && pendingDoubleClick == request && pendingRecipient == recipient.SelectedItem)
                        {
                            CharacterRecord target = pendingRecipient; pendingDoubleClick = -1; pendingRecipient = null; AddToRecipient(target, preview);
                        }
                    });
                }
                catch (InvalidOperationException) { }
            });
            workers.Add(task);
        }

        private void ShowDetails()
        {
            if (selected == null) { title.Text = ""; details.Text = ""; return; }
            title.Text = selected.GetDisplayName(chinese);
            string value = selected.Describe(chinese);
            if (!advanced.Checked)
            {
                string marker = chinese ? "原始对象信息（" : "Original object identity (";
                int at = value.IndexOf(marker, StringComparison.Ordinal); if (at >= 0) value = value.Substring(0, at).TrimEnd();
            }
            details.Text = value;
        }

        private void UpdateAddState() { add.Enabled = !closed && selected != null && preview != null && recipient.SelectedItem is CharacterRecord; }

        private void AddSelected(bool doubleClick)
        {
            var target = recipient.SelectedItem as CharacterRecord;
            if (closed || selected == null || target == null) return;
            if (preview == null)
            {
                if (doubleClick)
                {
                    if (!preparing) SelectBook();
                    pendingDoubleClick = generation; pendingRecipient = target;
                    status.Text = T("正在准备此书；完成后添加到当前选定角色。", "Preparing this book; it will be added to the selected recipient when ready.");
                }
                return;
            }
            AddToRecipient(target, preview);
        }

        private void AddToRecipient(CharacterRecord target, ItemInitialState state)
        {
            if (closed || target == null || !recipients.Contains(target) || state == null) return;
            try
            {
                Action<CharacterRecord, ItemInitialState> handler = AddItemRequested;
                if (handler == null) throw new InvalidOperationException(T("未连接到当前存档。", "This browser is not connected to a save document."));
                handler(target, state);
                status.ForeColor = Color.FromArgb(40, 90, 45);
                status.Text = T("已添加到", "Added to ") + target.Name + T("的背包草稿。可继续添加；最后保存存档。", "'s backpack draft. Continue adding, then save the main document.");
            }
            catch (Exception e) { status.ForeColor = Color.Firebrick; status.Text = e.Message; }
        }

        private void ReadIconMappings()
        {
            string data = Path.Combine(catalog.GameDirectory, "Data");
            foreach (string line in GameCatalog.DecodeDataFile(Path.Combine(data, "skill_role.txt")).Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
            {
                string[] row = line.Split('\t'); int id;
                if (row.Length > 5 && !String.IsNullOrEmpty(row[0]) && Int32.TryParse(row[5], out id) && id > 0) skillIcons[row[0]] = id;
            }
            var pictures = new Dictionary<string, int>(StringComparer.Ordinal);
            foreach (string line in GameCatalog.DecodeDataFile(Path.Combine(data, "item_pic.txt")).Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
            {
                string[] row = line.Split('\t'); int id;
                if (row.Length > 3 && Int32.TryParse(row[3], out id) && id > 0) pictures[row[0].Trim()] = id;
            }
            foreach (string line in GameCatalog.DecodeDataFile(Path.Combine(data, "item_6.txt")).Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
            {
                string[] row = line.Split('\t'); int type, id;
                if (row.Length > 39 && Int32.TryParse(row[0], out type) && type >= 45 && type <= 49 && pictures.TryGetValue(row[39].Trim(), out id)) shellIcons[type - 44] = id;
            }
        }

        private int IconId(ScriptBookDefinition book)
        {
            if (book == null) return 0; int id;
            if (!book.IsManual)
                foreach (string alias in book.SkillAliases) if (skillIcons.TryGetValue(alias, out id)) return id;
            return shellIcons.TryGetValue(book.ClassId, out id) ? id : 0;
        }

        private int ImageIndex(int id)
        {
            if (id <= 0 || iconLoader == null) return -1; int index;
            if (imageIndices.TryGetValue(id, out index)) return index;
            Bitmap bitmap = iconLoader.GetIcon(id);
            if (bitmap == null) { imageIndices[id] = -1; return -1; }
            retainedImages.Add(bitmap); index = images.Images.Count; images.Images.Add(bitmap); imageIndices[id] = index; return index;
        }

        private void UpdatePicture(int id)
        {
            Image old = picture.Image; picture.Image = iconLoader == null || id <= 0 ? null : iconLoader.GetIcon(id); if (old != null) old.Dispose();
        }

        private void CloseWorkers()
        {
            if (closed) return; closed = true; Interlocked.Increment(ref generation); closing.Cancel();
            pendingDoubleClick = -1; pendingRecipient = null;
            Image old = picture.Image; picture.Image = null; if (old != null) old.Dispose();
            images.Dispose(); foreach (Bitmap bitmap in retainedImages) bitmap.Dispose(); retainedImages.Clear();
            if (iconLoader != null) iconLoader.Dispose();
            Task.WhenAll(workers.ToArray()).ContinueWith(delegate { workerGate.Dispose(); closing.Dispose(); });
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) CloseWorkers();
            base.Dispose(disposing);
        }
    }
}
