using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Windows.Forms;

namespace SealSaveEditor
{
    public sealed class GameDataEditorForm : Form
    {
        private sealed class FileEntry
        {
            public string Path;
            public override string ToString() { return System.IO.Path.GetFileName(Path); }
        }

        private readonly string gameDirectory;
        private readonly bool chinese;
        private readonly ListBox files = new ListBox();
        private readonly DataGridView grid = new DataGridView();
        private readonly TextBox raw = new TextBox();
        private readonly TextBox description = new TextBox();
        private readonly TabControl tabs = new TabControl();
        private readonly TabPage tablePage = new TabPage();
        private readonly TabPage rawPage = new TabPage();
        private readonly Label status = new Label();
        private readonly Label documentLabel = new Label();
        private readonly Button save = new Button();
        private readonly Button reload = new Button();
        private readonly Button discard = new Button();
        private readonly Button restore = new Button();
        private readonly ComboBox backups = new ComboBox();
        private GameDataTextDocument document;
        private bool loading, rawDraftDirty;
        private string rawDisplayBaseline = "";
        private List<string> rawOriginalLines = new List<string>();
        private List<string> rawOriginalEndings = new List<string>();
        private FileEntry currentFile;

        public GameDataEditorForm(string installedGameDirectory, bool zh)
        {
            gameDirectory = Path.GetFullPath(installedGameDirectory); chinese = zh;
            Font = UiFonts.Create(); Text = T("游戏数据 TXT 编辑器", "Game Data TXT Editor");
            BackColor = Color.FromArgb(236, 232, 223); Size = new Size(1220, 820); MinimumSize = new Size(1000, 650);
            StartPosition = FormStartPosition.CenterParent;
            var root = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(14), ColumnCount = 1, RowCount = 3 };
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 55)); root.RowStyles.Add(new RowStyle(SizeType.Percent, 100)); root.RowStyles.Add(new RowStyle(SizeType.Absolute, 110));
            var heading = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 2, ColumnCount = 1 };
            documentLabel.Text = T("选择左侧 Data 文件", "Select a Data file on the left"); documentLabel.Dock = DockStyle.Fill; documentLabel.Font = UiFonts.Create(11F, FontStyle.Bold);
            heading.Controls.Add(documentLabel);
            heading.Controls.Add(new Label { Text = T("双击表格单元格修改。原始 TXT 保留全部行；保存会先备份，且需要关闭游戏。", "Double-click a table cell to edit. Original TXT contains every line. Saving creates a backup and requires the game to be closed."), Dock = DockStyle.Fill, AutoEllipsis = true });
            root.Controls.Add(heading, 0, 0);
            var split = new SplitContainer { Dock = DockStyle.Fill, Size = new Size(1180, 590), SplitterDistance = 220, Panel1MinSize = 160, Panel2MinSize = 600 };
            files.Name = "DataFileList"; files.Dock = DockStyle.Fill; files.IntegralHeight = false; files.BackColor = Color.FromArgb(248, 246, 240);
            files.SelectedIndexChanged += FileSelected; split.Panel1.Controls.Add(files);
            var editor = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2 };
            editor.RowStyles.Add(new RowStyle(SizeType.Percent, 100)); editor.RowStyles.Add(new RowStyle(SizeType.Absolute, 110));
            tabs.Name = "DataEditorTabs"; tabs.Dock = DockStyle.Fill; tablePage.Text = T("表格", "Table"); rawPage.Text = T("原始 TXT", "Original TXT");
            tabs.TabPages.Add(tablePage); tabs.TabPages.Add(rawPage); tabs.Selecting += SelectTab;
            grid.Name = "DataTable"; grid.Dock = DockStyle.Fill; grid.BackgroundColor = Color.White; grid.AllowUserToAddRows = false; grid.AllowUserToDeleteRows = false;
            grid.AllowUserToOrderColumns = false; grid.MultiSelect = false; grid.SelectionMode = DataGridViewSelectionMode.CellSelect;
            grid.EditMode = DataGridViewEditMode.EditOnKeystrokeOrF2; grid.RowHeadersWidth = 64; grid.RowTemplate.Height = 29;
            grid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.None; grid.EnableHeadersVisualStyles = false;
            grid.ColumnHeadersDefaultCellStyle.BackColor = Color.FromArgb(223, 216, 200); grid.ColumnHeadersDefaultCellStyle.ForeColor = Color.FromArgb(39, 37, 31);
            grid.DefaultCellStyle.SelectionBackColor = Color.FromArgb(209, 189, 145); grid.DefaultCellStyle.SelectionForeColor = Color.Black;
            grid.ColumnHeadersHeight = 42; grid.ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.DisableResizing;
            grid.CellValueChanged += CellChanged; grid.CellValidating += ValidateCell;
            grid.CellEndEdit += delegate(object sender, DataGridViewCellEventArgs e) { if (e.RowIndex >= 0) grid.Rows[e.RowIndex].ErrorText = ""; };
            grid.SelectionChanged += delegate { ShowFieldDescription(); };
            grid.DataError += delegate(object sender, DataGridViewDataErrorEventArgs e) { e.ThrowException = false; status.Text = T("当前单元格无法提交，请检查输入。", "The current cell could not be committed. Check its input."); };
            tablePage.Controls.Add(grid);
            raw.Name = "DataRawText"; raw.Dock = DockStyle.Fill; raw.Multiline = true; raw.WordWrap = false; raw.ScrollBars = ScrollBars.Both;
            raw.AcceptsTab = true; raw.AcceptsReturn = true; raw.MaxLength = Int32.MaxValue; raw.Font = UiFonts.Create(10F, FontStyle.Regular);
            raw.TextChanged += delegate
            {
                if (loading || document == null) return;
                rawDraftDirty = !String.Equals(raw.Text, rawDisplayBaseline, StringComparison.Ordinal); UpdateState();
            };
            rawPage.Controls.Add(raw); editor.Controls.Add(tabs, 0, 0);
            description.Name = "DataFieldDescription"; description.Dock = DockStyle.Fill; description.Multiline = true; description.ReadOnly = true;
            description.ScrollBars = ScrollBars.Vertical; description.BackColor = Color.FromArgb(248, 246, 240); editor.Controls.Add(description, 0, 1);
            split.Panel2.Controls.Add(editor); root.Controls.Add(split, 0, 1);
            var footer = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3, Padding = new Padding(0, 8, 0, 0) };
            footer.RowStyles.Add(new RowStyle(SizeType.Absolute, 34)); footer.RowStyles.Add(new RowStyle(SizeType.Absolute, 34)); footer.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            var actions = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false };
            save.Name = "DataSave"; save.Text = T("保存并备份", "Save with backup"); save.Width = 145; save.Height = 30; save.BackColor = Color.FromArgb(186, 153, 88);
            reload.Name = "DataReload"; reload.Text = T("从磁盘重新载入", "Reload from disk"); reload.Width = 165; reload.Height = 30;
            discard.Name = "DataDiscard"; discard.Text = T("放弃当前修改", "Discard changes"); discard.Width = 155; discard.Height = 30; discard.CausesValidation = false;
            save.Click += delegate { SaveCurrent(); }; reload.Click += delegate { ReloadCurrent(); }; discard.Click += delegate { DiscardDraft(); };
            actions.Controls.Add(save); actions.Controls.Add(reload); actions.Controls.Add(discard); footer.Controls.Add(actions, 0, 0);
            var backupRow = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false };
            backupRow.Controls.Add(new Label { Text = T("已有备份", "Backups"), AutoSize = true, Padding = new Padding(0, 5, 6, 0) });
            backups.Name = "DataBackups"; backups.DropDownStyle = ComboBoxStyle.DropDownList; backups.Width = 510;
            backups.SelectedIndexChanged += delegate { UpdateState(); };
            restore.Name = "DataRestoreBackup"; restore.Text = T("还原所选备份", "Restore selected backup"); restore.Width = 205; restore.Height = 29; restore.Click += delegate { RestoreSelected(); };
            backupRow.Controls.Add(backups); backupRow.Controls.Add(restore); footer.Controls.Add(backupRow, 0, 1);
            status.Name = "DataStatus"; status.Dock = DockStyle.Fill; status.AutoEllipsis = true; status.Padding = new Padding(0, 3, 0, 0); footer.Controls.Add(status, 0, 2);
            root.Controls.Add(footer, 0, 2); Controls.Add(root);
            FormClosing += delegate(object sender, FormClosingEventArgs e) { if (!MayLeaveCurrent()) e.Cancel = true; };
            Load += delegate { LoadFiles(); };
            UpdateState();
        }

        private string T(string zh, string en) { return chinese ? zh : en; }

        private void LoadFiles()
        {
            try
            {
                string data = Path.Combine(gameDirectory, "Data");
                loading = true;
                try
                {
                    files.Items.Clear();
                    foreach (string path in Directory.GetFiles(data, "*.txt", SearchOption.TopDirectoryOnly).OrderBy(p => p, StringComparer.OrdinalIgnoreCase)) files.Items.Add(new FileEntry { Path = path });
                }
                finally { loading = false; }
                if (files.Items.Count > 0) files.SelectedIndex = 0;
                else status.Text = T("此安装目录的 Data 文件夹中没有 TXT 文件。", "No TXT files were found in this installation's Data folder.");
            }
            catch (Exception e) { ShowError(e); }
        }

        private void FileSelected(object sender, EventArgs args)
        {
            if (loading) return;
            FileEntry selected = files.SelectedItem as FileEntry;
            if (selected == null || selected == currentFile) return;
            if (!MayLeaveCurrent()) { RestoreFileSelection(); return; }
            try
            {
                var next = GameDataTextDocument.Load(gameDirectory, selected.Path);
                document = next; currentFile = selected; RefreshViews();
            }
            catch (Exception e) { ShowError(e); RestoreFileSelection(); }
        }

        private void RestoreFileSelection()
        { loading = true; try { files.SelectedItem = currentFile; } finally { loading = false; } }

        private void RefreshViews()
        {
            loading = true;
            try
            {
                FillTable(); SetRawDisplay(); RefreshBackups();
                documentLabel.Text = "Data / " + document.FileName;
                if (!document.IsTabular) tabs.SelectedTab = rawPage;
                description.Text = document.IsTabular ? T("选择表格中的字段查看原表中文说明。灰色标题、注释及未识别行只读；可在原始 TXT 中修改结构。", "Select a field for its original Chinese table description. Gray headers, comments and unrecognized lines are read only here; edit structure in Original TXT.")
                    : T("未识别到原表表头，当前文件使用原始 TXT 模式。全部原文、编码与换行均保留。", "No original table header was recognized. Use Original TXT; all source text, encoding and line endings are preserved.");
            }
            finally { loading = false; }
            UpdateState(); ShowFieldDescription();
        }

        private void FillTable()
        {
            grid.SuspendLayout();
            try
            {
                grid.Rows.Clear(); grid.Columns.Clear(); grid.Enabled = document != null && document.IsTabular;
                if (document == null || !document.IsTabular) return;
                for (int c = 0; c < document.ColumnNames.Count; c++)
                {
                    var column = new DataGridViewTextBoxColumn { Name = "column" + c, HeaderText = document.ColumnNames[c], Width = c == 0 ? 215 : 130,
                        SortMode = DataGridViewColumnSortMode.NotSortable, ToolTipText = document.GetColumnDescription(c) };
                    grid.Columns.Add(column);
                }
                foreach (GameDataTextRow source in document.Rows)
                {
                    int index = grid.Rows.Add(); DataGridViewRow row = grid.Rows[index]; row.Tag = source.Index; row.HeaderCell.Value = source.LineNumber.ToString();
                    bool locked = source.IsHeader || source.IsComment || source.Cells.Count < 2;
                    for (int c = 0; c < grid.Columns.Count; c++)
                    {
                        row.Cells[c].Value = c < source.Cells.Count ? source.Cells[c] : null;
                        row.Cells[c].ReadOnly = locked || c >= source.Cells.Count;
                        if (row.Cells[c].ReadOnly) row.Cells[c].Style.BackColor = Color.FromArgb(238, 236, 231);
                    }
                }
            }
            finally { grid.ResumeLayout(); }
        }

        private void SetRawDisplay()
        {
            // Native EDIT controls do not render bare LF consistently. Normalize only the display,
            // and retain source line endings separately for actual edits.
            rawOriginalLines = document == null ? new List<string>() : document.Rows.Select(r => r.RawText).ToList();
            rawOriginalEndings = document == null ? new List<string>() : document.Rows.Select(r => r.Ending).ToList();
            raw.Text = document == null ? "" : String.Concat(document.Rows.Select(r => r.RawText + (r.Ending.Length == 0 ? "" : "\r\n")));
            rawDisplayBaseline = raw.Text; rawDraftDirty = false;
        }

        private string ReadRawDraft()
        {
            var lines = new List<string>(); var hasEnding = new List<bool>(); int start = 0;
            while (start < raw.Text.Length)
            {
                int end = start; while (end < raw.Text.Length && raw.Text[end] != '\r' && raw.Text[end] != '\n') end++;
                lines.Add(raw.Text.Substring(start, end - start)); hasEnding.Add(end < raw.Text.Length);
                if (end < raw.Text.Length) { char c = raw.Text[end++]; if (c == '\r' && end < raw.Text.Length && raw.Text[end] == '\n') end++; }
                start = end;
            }
            if (lines.Count == 0) { lines.Add(""); hasEnding.Add(false); }
            int[] original = Enumerable.Repeat(-1, lines.Count).ToArray();
            if (lines.Count == rawOriginalLines.Count)
                for (int i = 0; i < original.Length; i++) original[i] = i;
            else if ((long)lines.Count * rawOriginalLines.Count <= 2000000)
            {
                // Match unchanged lines across inserted/deleted lines, preserving their original endings.
                int[,] lengths = new int[rawOriginalLines.Count + 1, lines.Count + 1];
                for (int a = rawOriginalLines.Count - 1; a >= 0; a--)
                    for (int b = lines.Count - 1; b >= 0; b--)
                        lengths[a, b] = rawOriginalLines[a] == lines[b] ? 1 + lengths[a + 1, b + 1] : Math.Max(lengths[a + 1, b], lengths[a, b + 1]);
                int oldLine = 0, newLine = 0;
                while (oldLine < rawOriginalLines.Count && newLine < lines.Count)
                    if (rawOriginalLines[oldLine] == lines[newLine]) { original[newLine++] = oldLine++; }
                    else if (lengths[oldLine + 1, newLine] >= lengths[oldLine, newLine + 1]) oldLine++; else newLine++;
            }
            else
            {
                // Bound memory for unusually large user tables; unchanged common prefix/suffix remain exact.
                int first = 0; while (first < lines.Count && first < rawOriginalLines.Count && lines[first] == rawOriginalLines[first]) { original[first] = first; first++; }
                int a = rawOriginalLines.Count - 1, b = lines.Count - 1;
                while (a >= first && b >= first && rawOriginalLines[a] == lines[b]) { original[b--] = a--; }
            }
            var value = new System.Text.StringBuilder();
            for (int i = 0; i < lines.Count; i++)
            {
                string ending = original[i] >= 0 ? rawOriginalEndings[original[i]] : document.NewLine;
                if (!hasEnding[i]) ending = ""; else if (ending.Length == 0) ending = document.NewLine;
                value.Append(lines[i]); value.Append(ending);
            }
            return value.ToString();
        }

        private void RefreshBackups()
        {
            backups.Items.Clear();
            if (document == null) return;
            foreach (GameDataTextBackup backup in document.ListBackups()) backups.Items.Add(backup);
            if (backups.Items.Count > 0) backups.SelectedIndex = 0;
        }

        private void ValidateCell(object sender, DataGridViewCellValidatingEventArgs e)
        {
            if (loading || e.RowIndex < 0 || grid.Rows[e.RowIndex].Cells[e.ColumnIndex].ReadOnly) return;
            string value = Convert.ToString(e.FormattedValue) ?? "";
            if (value.IndexOfAny(new[] { '\t', '\r', '\n', '\0' }) >= 0)
            {
                grid.Rows[e.RowIndex].ErrorText = T("单元格中不能包含制表符或换行。请用原始 TXT 修改结构。", "A cell cannot contain tabs or line breaks. Use Original TXT to edit structure."); e.Cancel = true;
            }
        }

        private void CellChanged(object sender, DataGridViewCellEventArgs e)
        {
            if (loading || document == null || e.RowIndex < 0 || e.ColumnIndex < 0) return;
            DataGridViewRow row = grid.Rows[e.RowIndex]; int rowIndex = (int)row.Tag;
            try
            {
                document.SetCell(rowIndex, e.ColumnIndex, Convert.ToString(row.Cells[e.ColumnIndex].Value) ?? "");
                loading = true; try { SetRawDisplay(); } finally { loading = false; }
                UpdateState();
            }
            catch (Exception error)
            {
                loading = true; try { row.Cells[e.ColumnIndex].Value = document.Rows[rowIndex].Cells[e.ColumnIndex]; } finally { loading = false; }
                ShowError(error);
            }
        }

        private void SelectTab(object sender, TabControlCancelEventArgs e)
        {
            if (loading || document == null) return;
            if (!CommitPending()) { e.Cancel = true; return; }
            loading = true;
            try { if (e.TabPage == tablePage) FillTable(); else SetRawDisplay(); }
            finally { loading = false; }
            UpdateState();
        }

        private bool CommitPending()
        {
            try
            {
                if (grid.IsCurrentCellInEditMode && !grid.EndEdit()) return false;
                if (rawDraftDirty)
                {
                    document.Text = ReadRawDraft(); rawDisplayBaseline = raw.Text; rawDraftDirty = false;
                }
                return true;
            }
            catch (Exception e) { ShowError(e); return false; }
        }

        private bool SaveCurrent()
        {
            if (document == null || !CommitPending()) return false;
            try
            {
                string backup = document.AtomicSave(); RefreshViews();
                status.Text = backup == null ? T("文件未改变。", "The file is unchanged.") : T("已保存；原文件已备份：", "Saved. Original bytes backed up: ") + backup;
                return true;
            }
            catch (Exception e) { ShowError(e); return false; }
        }

        private bool MayLeaveCurrent()
        {
            if (document == null) return true;
            if (!document.IsModified && !rawDraftDirty && !grid.IsCurrentCellDirty) { if (grid.IsCurrentCellInEditMode) grid.CancelEdit(); return true; }
            DialogResult answer = MessageBox.Show(this,
                T(document.FileName + " 有未保存修改。\r\n选择“是”保存并备份，“否”放弃修改，“取消”继续编辑。", document.FileName + " has unsaved changes.\r\nYes: save with backup. No: discard changes. Cancel: keep editing."),
                T("未保存的修改", "Unsaved changes"), MessageBoxButtons.YesNoCancel, MessageBoxIcon.Question);
            if (answer == DialogResult.Cancel) return false;
            if (answer == DialogResult.Yes) return SaveCurrent();
            grid.CancelEdit(); document.ResetDraft(); rawDraftDirty = false; RefreshViews(); return true;
        }

        private void ReloadCurrent()
        {
            if (document == null || !MayLeaveCurrent()) return;
            try { document = GameDataTextDocument.Load(gameDirectory, document.FilePath); RefreshViews(); status.Text = T("已从磁盘重新载入。", "Reloaded from disk."); }
            catch (Exception e) { ShowError(e); }
        }

        private void DiscardDraft()
        {
            if (document == null) return;
            if (!document.IsModified && !rawDraftDirty && !grid.IsCurrentCellDirty) return;
            if (MessageBox.Show(this, T("放弃此文件尚未保存的修改？", "Discard this file's unsaved changes?"), T("放弃修改", "Discard changes"), MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
            grid.CancelEdit(); document.ResetDraft(); RefreshViews();
        }

        private void RestoreSelected()
        {
            var selected = backups.SelectedItem as GameDataTextBackup;
            if (document == null || selected == null) return;
            string path = selected.Path;
            if (!MayLeaveCurrent()) return;
            try
            {
                string recovery = document.RestoreBackup(path); RefreshViews();
                status.Text = recovery == null ? T("当前文件与所选备份相同。", "The current file matches this backup.")
                    : T("已还原；还原前内容已另行备份：", "Restored. Previous contents backed up: ") + recovery;
            }
            catch (Exception e) { ShowError(e); }
        }

        private void ShowFieldDescription()
        {
            if (loading || document == null || !document.IsTabular || tabs.SelectedTab != tablePage || grid.CurrentCell == null) return;
            int col = grid.CurrentCell.ColumnIndex;
            if (col >= 0 && col < document.ColumnNames.Count) description.Text = document.GetColumnDescription(col);
        }

        private void UpdateState()
        {
            bool dirty = document != null && (document.IsModified || rawDraftDirty);
            save.Enabled = dirty; reload.Enabled = document != null; discard.Enabled = dirty; restore.Enabled = document != null && backups.SelectedItem is GameDataTextBackup;
            Text = (dirty ? "* " : "") + T("游戏数据 TXT 编辑器", "Game Data TXT Editor") + (document == null ? "" : " — " + document.FileName);
            if (document != null)
                status.Text = document.EncodingName + (document.IsEncoded ? T(" · 原标记/XOR保留", " · original marker/XOR preserved") : T(" · 原始明文", " · original plain text"))
                    + (document.HasMixedNewLines ? T(" · 混合换行保留", " · mixed line endings preserved") : "")
                    + (dirty ? T(" · 尚未保存", " · unsaved") : T(" · 未修改", " · unchanged"));
        }

        private void ShowError(Exception error)
        { status.Text = error.Message; MessageBox.Show(this, error.Message, T("无法完成操作", "Operation could not be completed"), MessageBoxButtons.OK, MessageBoxIcon.Warning); }
    }
}
