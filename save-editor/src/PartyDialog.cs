using System;
using System.Drawing;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace SealSaveEditor
{
    public sealed class PartyDialog : Form
    {
        private readonly Func<int, CompanionAvailability> availability;
        private readonly Func<CompanionDefinition, Task> add;
        private readonly bool chinese;
        private readonly ListView list = new ListView();
        private readonly Label detail = new Label();
        private readonly Button addButton = new Button();
        private readonly Button close = new Button();
        private bool busy;

        public PartyDialog(Func<int, CompanionAvailability> read, Func<CompanionDefinition, Task> addMember, bool zh)
        {
            availability = read; add = addMember; chinese = zh;
            Text = zh ? "添加队友" : "Add companion"; EditorBranding.Apply(this); Font = UiFonts.Create();
            Size = new Size(780, 560); MinimumSize = new Size(680, 470); StartPosition = FormStartPosition.CenterParent;
            var layout = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(16), ColumnCount = 1, RowCount = 4 };
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100)); layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 75));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 68)); layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 40));
            list.Name = "CompanionList"; list.Dock = DockStyle.Fill; list.View = View.Details; list.FullRowSelect = true; list.MultiSelect = false; list.HideSelection = false;
            list.Columns.Add(zh ? "人物" : "Character", 190); list.Columns.Add(zh ? "状态" : "Status", 470);
            detail.Name = "CompanionStatus"; detail.Dock = DockStyle.Fill;
            var warning = new Label { Dock = DockStyle.Fill, Text = zh ? "原版初始等级与装备，放到主角身边；队伍最多5人。提前加入不会完成招募剧情，后续剧情仍可能重新招募或替换该角色。每次添加都可在主窗口撤销，保存后生效。" : "Adds the original level-1 character and starting equipment beside the main character; the party limit is five. Early joining does not complete recruitment quests, and later story events may recruit or replace the character. Each addition can be undone in the main window. Save to apply." };
            var actions = new FlowLayoutPanel { Dock = DockStyle.Fill };
            addButton.Name = "AddSelectedCompanion"; addButton.Text = zh ? "加入队伍" : "Add to party"; addButton.AutoSize = true;
            close.Text = zh ? "关闭" : "Close"; close.AutoSize = true; close.DialogResult = DialogResult.Cancel;
            actions.Controls.Add(addButton); actions.Controls.Add(close);
            layout.Controls.Add(list, 0, 0); layout.Controls.Add(detail, 0, 1); layout.Controls.Add(warning, 0, 2); layout.Controls.Add(actions, 0, 3); Controls.Add(layout);
            list.SelectedIndexChanged += delegate { UpdateSelection(); };
            addButton.Click += async delegate { await AddSelected(); };
            list.DoubleClick += async delegate { await AddSelected(); };
            FormClosing += delegate(object sender, FormClosingEventArgs e) { if (busy) e.Cancel = true; };
            AcceptButton = addButton; CancelButton = close; RefreshList();
        }

        private void RefreshList()
        {
            list.Items.Clear();
            foreach (CompanionDefinition character in CompanionFactory.Characters)
            {
                var state = availability(character.ClassId);
                var row = new ListViewItem(chinese ? character.ChineseName : character.EnglishName) { Tag = character };
                row.SubItems.Add(state.CanAdd ? (chinese ? "可以添加" : "Available") : Explain(state.Reason));
                list.Items.Add(row);
            }
            if (list.Items.Count > 0) list.Items[0].Selected = true;
            UpdateSelection();
        }
        private string Explain(string reason)
        {
            if (!chinese) return reason;
            if (reason.IndexOf("already", StringComparison.OrdinalIgnoreCase) >= 0) return "队伍中已有该人物或职业";
            if (reason.IndexOf("full", StringComparison.OrdinalIgnoreCase) >= 0 || reason.IndexOf("five", StringComparison.OrdinalIgnoreCase) >= 0) return "队伍已满（最多5人）";
            return "暂时不能添加：" + reason;
        }
        private void UpdateSelection()
        {
            var selected = list.SelectedItems.Count == 0 ? null : list.SelectedItems[0].Tag as CompanionDefinition;
            if (selected == null) { addButton.Enabled = false; return; }
            var state = availability(selected.ClassId);
            addButton.Enabled = !busy && state.CanAdd;
            detail.Text = (chinese ? "当前队伍：" : "Party: ") + state.PartyCount + "/5\r\n" + (state.CanAdd ? (chinese ? "可以添加原版 " + selected.ChineseName + "，位置 " : "Add original " + selected.EnglishName + " at position ") + state.AvailablePosition : Explain(state.Reason));
        }
        private async Task AddSelected()
        {
            if (busy || list.SelectedItems.Count == 0) return;
            var selected = list.SelectedItems[0].Tag as CompanionDefinition;
            if (!availability(selected.ClassId).CanAdd) return;
            busy = true; list.Enabled = close.Enabled = addButton.Enabled = false;
            detail.Text = chinese ? "正在生成原版人物及其装备、技能…" : "Generating the original character, equipment and skills...";
            try { await add(selected); RefreshList(); }
            catch (Exception ex) { MessageBox.Show(this, ex.Message, Text, MessageBoxButtons.OK, MessageBoxIcon.Error); }
            finally { busy = false; list.Enabled = close.Enabled = true; UpdateSelection(); }
        }
    }
}
