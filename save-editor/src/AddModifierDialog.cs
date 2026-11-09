using System;
using System.Drawing;
using System.Globalization;
using System.Linq;
using System.Windows.Forms;

namespace SealSaveEditor
{
    public sealed class AddModifierDialog : Form
    {
        private readonly ComboBox effect = new ComboBox();
        private readonly TextBox value = new TextBox();
        private readonly Label help = new Label();
        private readonly GameCatalog catalog;
        private readonly bool chinese;
        public int AttributeId { get; private set; }
        public float ModifierValue { get; private set; }

        public AddModifierDialog(GameCatalog definitions, string itemName, bool zh)
        {
            catalog = definitions; chinese = zh;
            Text = zh ? "添加物品属性" : "Add item modifier";
            Font = UiFonts.Create();
            Size = new Size(550, 360);
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false; MinimizeBox = false;
            StartPosition = FormStartPosition.CenterParent;
            BackColor = Color.FromArgb(250, 248, 243);
            var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 7, Padding = new Padding(20) };
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 34));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 28));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 38));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 28));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 38));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 44));
            layout.Controls.Add(new Label { Text = itemName, Dock = DockStyle.Fill, AutoEllipsis = true, Font = new Font(Font, FontStyle.Bold) }, 0, 0);
            layout.Controls.Add(new Label { Text = zh ? "属性类型" : "Attribute", Dock = DockStyle.Fill }, 0, 1);
            effect.Dock = DockStyle.Fill; effect.DropDownStyle = ComboBoxStyle.DropDownList;
            effect.Name = "AddModifierType";
            effect.DisplayMember = "Name";
            foreach (var attribute in definitions.Attributes.OrderBy(x => FieldPresentation.EffectName(x.Id, x.Name, zh)))
                effect.Items.Add(new Choice { Id = attribute.Id, Name = FieldPresentation.EffectName(attribute.Id, attribute.Name, zh) });
            effect.SelectedIndexChanged += delegate { UpdateHelp(); };
            layout.Controls.Add(effect, 0, 2);
            layout.Controls.Add(new Label { Text = zh ? "固定加成数值（允许负数和小数）" : "Fixed bonus value (signed numbers allowed)", Dock = DockStyle.Fill }, 0, 3);
            value.Name = "AddModifierValue"; value.Text = "10"; value.Dock = DockStyle.Fill;
            layout.Controls.Add(value, 0, 4);
            help.Dock = DockStyle.Fill; help.ForeColor = Color.FromArgb(117, 110, 99);
            layout.Controls.Add(help, 0, 5);
            var buttons = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.RightToLeft };
            var cancel = new Button { Text = zh ? "取消" : "Cancel", DialogResult = DialogResult.Cancel, AutoSize = true };
            var add = new Button { Name = "AddModifierConfirm", Text = zh ? "添加到当前物品" : "Add to this item", AutoSize = true, BackColor = Color.FromArgb(232, 190, 104) };
            add.Click += delegate
            {
                float number;
                if (effect.SelectedItem == null || !Single.TryParse(value.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out number) || Single.IsNaN(number) || Single.IsInfinity(number))
                { MessageBox.Show(this, zh ? "请选择属性，并输入有效的数值。" : "Choose an attribute and enter a finite number."); return; }
                AttributeId = ((Choice)effect.SelectedItem).Id; ModifierValue = number;
                DialogResult = DialogResult.OK; Close();
            };
            buttons.Controls.Add(cancel); buttons.Controls.Add(add);
            layout.Controls.Add(buttons, 0, 6);
            Controls.Add(layout); AcceptButton = add; CancelButton = cancel;
            if (effect.Items.Count > 0) effect.SelectedIndex = 0;
        }

        private void UpdateHelp()
        {
            Choice selected = effect.SelectedItem as Choice;
            if (selected == null) return;
            var definition = catalog.FindAttribute(selected.Id);
            help.Text = selected.Id == 201
                ? (chinese ? "新增的防御加成等额作用于五行防御。现有属性的五行分配保持不变。" : "New defense bonuses apply equally to all five elemental defenses. Existing distributions are retained.")
                : (definition == null ? "" : definition.Description);
        }

        private sealed class Choice
        {
            public int Id { get; set; }
            public string Name { get; set; }
        }
    }
}
