using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.Windows.Forms;

namespace SealSaveEditor
{
    public sealed class ItemPresetDetailsDialog : Form
    {
        public ItemPresetDetailsDialog(IList<ItemBonusMaximum> values, bool chinese, bool advanced)
        {
            Text = chinese ? "装备强化数值对照" : "Equipment preset values";
            Font = UiFonts.Create();
            Size = new Size(940, 650); MinimumSize = new Size(720, 450);
            StartPosition = FormStartPosition.CenterParent;
            BackColor = Color.FromArgb(250, 248, 243);
            var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3, Padding = new Padding(16) };
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 78));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 44));
            layout.Controls.Add(new Label {
                Dock = DockStyle.Fill,
                Text = chinese
                    ? "极限强化按各属性的实际计算方式分别定值。部分为有效封顶值，部分为留有计算余量的强力预设，并非全部都是引擎硬上限。已有更高数值和特殊效果会保留；修改后需要保存，并在游戏中装备该物品。"
                    : "The extreme profile sets each effect separately. Some values reach an effective cap; others are strong operational presets with arithmetic headroom, not engine-wide hard limits. Existing higher and special bonuses are retained. Save the changes and equip the item in the game."
            }, 0, 0);
            var grid = new DataGridView {
                Name = "PresetValues", Dock = DockStyle.Fill, ReadOnly = true,
                AllowUserToAddRows = false, AllowUserToDeleteRows = false, RowHeadersVisible = false,
                SelectionMode = DataGridViewSelectionMode.FullRowSelect,
                AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
                BackgroundColor = Color.White, BorderStyle = BorderStyle.FixedSingle
            };
            grid.Columns.Add("Property", chinese ? "属性" : "Property");
            grid.Columns.Add("Extreme", chinese ? "极限强化值" : "Extreme value");
            grid.Columns.Add("Table", chinese ? "原表上限" : "Table maximum");
            grid.Columns.Add("Rule", chinese ? "数值依据" : "Value rule");
            grid.Columns.Add("Unit", chinese ? "单位" : "Unit");
            grid.Columns[0].FillWeight = 140; grid.Columns[3].FillWeight = 130;
            grid.Columns[4].FillWeight = 55;
            if (advanced) grid.Columns.Add("Evidence", chinese ? "计算依据（高级）" : "Evidence (advanced)");
            foreach (ItemBonusMaximum value in values)
            {
                string kind = value.LimitKind;
                if (chinese)
                    kind = kind == "EffectiveEngineLimit" ? "有效封顶值" : kind == "ConservativeOperationalLimit" ? "强力预设（保留余量）" : "原表上限";
                else kind = kind == "EffectiveEngineLimit" ? "Effective cap" : kind == "ConservativeOperationalLimit" ? "Strong operational preset" : "Original table value";
                int row = grid.Rows.Add(FieldPresentation.EffectName(value.AttributeId, value.Name, chinese),
                    value.Value.ToString("0.###", CultureInfo.InvariantCulture),
                    value.OriginalTableValue.ToString("0.###", CultureInfo.InvariantCulture), kind,
                    value.Unit == "Percent" ? "%" : value.Unit == "ChanceCoefficient" ? (chinese ? "触发系数" : "coefficient")
                        : chinese ? (value.Unit == "Rating" ? "数值" : "点") : value.Unit);
                if (advanced) grid.Rows[row].Cells[5].Value = value.Evidence;
            }
            layout.Controls.Add(grid, 0, 1);
            var buttons = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.RightToLeft };
            var close = new Button { Text = chinese ? "关闭" : "Close", DialogResult = DialogResult.Cancel, AutoSize = true };
            buttons.Controls.Add(close); layout.Controls.Add(buttons, 0, 2);
            Controls.Add(layout); CancelButton = close;
        }
    }
}
