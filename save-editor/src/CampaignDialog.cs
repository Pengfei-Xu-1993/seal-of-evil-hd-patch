using System;
using System.Drawing;
using System.Globalization;
using System.Windows.Forms;

namespace SealSaveEditor
{
    public sealed class CampaignDialog : Form
    {
        private readonly Func<CampaignProgressState> read;
        private readonly Action enable;
        private readonly bool chinese;
        private readonly Label heading = new Label();
        private readonly Label status = new Label();
        private readonly DataGridView checks = new DataGridView();
        private readonly Button second = new Button();

        public CampaignDialog(Func<CampaignProgressState> readState, Action enableSecond, bool zh)
        {
            read = readState; enable = enableSecond; chinese = zh;
            Text = zh ? "周目与隐藏结局条件" : "Playthrough and hidden-ending conditions";
            EditorBranding.Apply(this);
            Font = UiFonts.Create(); Size = new Size(880, 620); MinimumSize = new Size(760, 530);
            StartPosition = FormStartPosition.CenterParent; BackColor = Color.FromArgb(250, 248, 243);
            var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 5, Padding = new Padding(18) };
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 44));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 82));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 48));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 40));
            heading.Dock = DockStyle.Fill; heading.Font = UiFonts.Create(12F, FontStyle.Bold);
            layout.Controls.Add(heading, 0, 0);
            layout.Controls.Add(new Label { Dock = DockStyle.Fill, Text = zh
                ? "启用二周目资格会保留当前章节、地图、队伍和任务进度。已经开启的宝箱和已经结束的事件保持原状；错过的隐藏装备可通过添加物品功能单独补入。下表显示隐藏结局还需要的剧情条件。"
                : "Enabling second-playthrough eligibility retains your current chapter, map, party and story progress. Opened chests and completed events stay as they are; missed hidden equipment can be added separately. The table shows the additional story requirements for the hidden ending." }, 0, 1);
            checks.Name = "CampaignConditions"; checks.Dock = DockStyle.Fill; checks.ReadOnly = true;
            checks.AllowUserToAddRows = false; checks.AllowUserToDeleteRows = false; checks.RowHeadersVisible = false;
            checks.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
            checks.AutoSizeRowsMode = DataGridViewAutoSizeRowsMode.AllCells;
            checks.DefaultCellStyle.WrapMode = DataGridViewTriState.True;
            checks.BackgroundColor = Color.White; checks.BorderStyle = BorderStyle.FixedSingle;
            checks.Columns.Add("Condition", zh ? "条件" : "Condition");
            checks.Columns.Add("Status", zh ? "当前状态" : "Current status");
            checks.Columns.Add("Details", zh ? "说明" : "Details");
            checks.Columns[0].FillWeight = 100; checks.Columns[1].FillWeight = 65; checks.Columns[2].FillWeight = 180;
            layout.Controls.Add(checks, 0, 2);
            status.Dock = DockStyle.Fill; status.ForeColor = Color.FromArgb(105, 95, 75);
            layout.Controls.Add(status, 0, 3);
            var buttons = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false };
            second.Name = "EnableSecondPlaythrough"; second.AutoSize = true;
            second.Text = zh ? "启用二周目资格（保留进度）" : "Enable second playthrough (keep progress)";
            second.BackColor = Color.FromArgb(232, 190, 104);
            second.Click += delegate {
                try {
                    enable(); RefreshState();
                    status.Text = zh ? "已更新工作副本。关闭本窗口后，点击主界面的“保存（自动备份）”写入存档；也可以撤销。"
                        : "Working copy updated. Close this window and use Save with backup to write the slot. Undo is available.";
                } catch (Exception error) { MessageBox.Show(this, error.Message, Text, MessageBoxButtons.OK, MessageBoxIcon.Error); }
            };
            var close = new Button { Text = zh ? "关闭" : "Close", DialogResult = DialogResult.Cancel, AutoSize = true };
            buttons.Controls.Add(second); buttons.Controls.Add(close); layout.Controls.Add(buttons, 0, 4);
            Controls.Add(layout); CancelButton = close; RefreshState();
        }

        private void RefreshState()
        {
            CampaignProgressState state = read();
            heading.Text = state.PrimaryCharacterName + "  ·  " + (chinese
                ? (state.SecondPlaythroughEnabled ? "已具备二周目资格" : "首周目")
                : (state.SecondPlaythroughEnabled ? "Second-playthrough eligible" : "First playthrough"));
            checks.Rows.Clear();
            Add(chinese ? "周目资格" : "Playthrough eligibility", state.SecondPlaythroughEnabled,
                chinese ? "已记录完成周目数：" + state.CompletedPlaythroughs : "Recorded completed playthroughs: " + state.CompletedPlaythroughs);
            Add(chinese ? "剧情诱惑之戒" : "Story Ring of Temptation", state.HasYouhuozhijie,
                chinese ? "检查队伍携带的剧情戒指；仓储中的物品和同名普通模板不计入。" : "Checks the story ring carried by the party; bank items and similarly named base templates do not count.");
            AddFlag(chinese ? "兽王相关剧情" : "Beast-king storyline", state.ShouwangwujingFlag, 21,
                chinese ? "隐藏结局要求此剧情达到原版认可的完成状态。" : "The hidden ending requires the original completed state of this storyline.");
            AddFlag(chinese ? "方仲相关剧情" : "Fang Zhong storyline", state.JianfangzhongFlag, 3,
                chinese ? "隐藏结局要求方仲相关剧情完成。" : "The hidden ending requires the completed Fang Zhong storyline.");
            checks.Rows.Add(chinese ? "终战声望条件" : "Fame at the final branch", state.Fame.ToString(CultureInfo.InvariantCulture),
                chinese ? "最终分支判断时需至少130；击败变身蚩尤的该回调会先增加100，后续任务也会增加声望。"
                    : "At least 130 at the final branch. That transformed-Chi-You callback grants 100 before checking; later quests can also raise Fame.");
            second.Enabled = !state.SecondPlaythroughEnabled;
            if (status.Text.Length == 0) status.Text = chinese ? "此页检查当前存档条件。二周目资格本身不会替你完成剧情任务。"
                : "These checks describe the current save. Second-playthrough eligibility does not complete story quests.";
        }

        private void Add(string name, bool met, string detail)
        { checks.Rows.Add(name, chinese ? (met ? "满足" : "未满足") : (met ? "Met" : "Not met"), detail); }
        private void AddFlag(string name, int? value, int expected, string detail)
        {
            string label = !value.HasValue ? (chinese ? "尚未识别" : "Unrecognized") : value.Value < 0
                ? (chinese ? "尚未触发" : "Not reached") : value.Value == expected ? (chinese ? "已完成" : "Complete") : (chinese ? "进行中" : "In progress");
            checks.Rows.Add(name, label, detail);
        }
    }
}
