using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.Windows.Forms;

namespace SealSaveEditor
{
    public sealed class FieldInspector : UserControl
    {
        private enum EditorKind { Text, Number, Choice }
        private sealed class AttributeChoice
        {
            public string Value, Caption;
            public override string ToString() { return Caption; }
        }

        private static readonly Color Paper = Color.FromArgb(250, 248, 243);
        private static readonly Color Ink = Color.FromArgb(37, 35, 31);
        private static readonly Color Muted = Color.FromArgb(117, 110, 99);
        private static readonly Color Line = Color.FromArgb(221, 214, 201);
        private static readonly Color Gold = Color.FromArgb(232, 190, 104);
        private readonly FlowLayoutPanel content = new FlowLayoutPanel();
        private readonly Label heading = new Label();
        private readonly PictureBox icon = new PictureBox { Size = new Size(64, 64), SizeMode = PictureBoxSizeMode.Zoom, Visible = false, BackColor = Color.Transparent };
        private readonly Label description = new Label();
        private readonly Label original = new Label();
        private readonly Label current = new Label();
        private readonly Label inputLabel = new Label();
        private readonly Label state = new Label();
        private readonly NumericUpDown number = new NumericUpDown();
        private readonly TextBox text = new TextBox();
        private readonly ComboBox choice = new ComboBox();
        private readonly LinkLabel switchInput = new LinkLabel();
        private readonly Button apply = new Button();
        private readonly Button reset = new Button();
        private bool loading, chinese = true, canEdit;
        private EditorKind editorKind;
        private string boundLabel, boundDescription;
        private GameCatalog catalog;

        public SaveField Field { get; private set; }
        public event EventHandler ApplyRequested;
        public event EventHandler ResetRequested;
        public event EventHandler DraftChanged;

        public string DraftValue
        {
            get
            {
                if (Field == null) return "";
                if (editorKind == EditorKind.Choice)
                {
                    AttributeChoice selected = choice.SelectedItem as AttributeChoice;
                    return selected == null ? Field.Text : selected.Value;
                }
                if (editorKind == EditorKind.Number)
                {
                    // Read typed text as well as spin-button changes. Applying or
                    // saving while the input still has focus must not use an old Value.
                    decimal value;
                    if (Decimal.TryParse(number.Text, NumberStyles.Number, CultureInfo.CurrentCulture, out value))
                        return value.ToString("G29", CultureInfo.InvariantCulture);
                    return number.Text;
                }
                return text.Text;
            }
        }

        public bool HasPending
        {
            get { return Field != null && !SameValue(Field, Field.Text, DraftValue); }
        }

        public FieldInspector()
        {
            Name = "FieldInspector";
            MinimumSize = new Size(300, 210);
            Size = new Size(340, 520);
            BackColor = Paper; ForeColor = Ink;
            Font = UiFonts.Create();
            AutoScaleMode = AutoScaleMode.Dpi;
            var layout = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 2, ColumnCount = 1, Margin = Padding.Empty, Padding = Padding.Empty, BackColor = Paper };
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 64));
            content.Dock = DockStyle.Fill; content.AutoScroll = true;
            content.FlowDirection = FlowDirection.TopDown; content.WrapContents = false;
            content.Padding = new Padding(16); content.Margin = Padding.Empty;
            content.BackColor = Paper;
            ConfigureLabel(heading, true); heading.Font = new Font(Font, FontStyle.Bold);
            ConfigureLabel(description, false);
            ConfigureLabel(original, false); original.Margin = new Padding(0, 8, 0, 7);
            ConfigureLabel(current, false); current.ForeColor = Ink;
            ConfigureLabel(inputLabel, false); inputLabel.ForeColor = Ink; inputLabel.Margin = new Padding(0, 12, 0, 7);
            number.Name = "InspectorNumeric"; number.AccessibleName = "Value input";
            number.ThousandsSeparator = false; number.TextAlign = HorizontalAlignment.Left;
            number.Margin = new Padding(0, 0, 0, 8); number.BackColor = Color.White; number.ForeColor = Ink;
            text.Name = "InspectorText"; text.AccessibleName = "Value input";
            text.Margin = new Padding(0, 0, 0, 8); text.BorderStyle = BorderStyle.FixedSingle;
            text.BackColor = Color.White; text.ForeColor = Ink;
            choice.Name = "InspectorChoice"; choice.AccessibleName = "Bonus attribute";
            choice.DropDownStyle = ComboBoxStyle.DropDownList;
            choice.Margin = new Padding(0, 0, 0, 8); choice.BackColor = Color.White; choice.ForeColor = Ink;
            switchInput.AutoSize = true; switchInput.LinkColor = Color.FromArgb(126, 88, 30);
            switchInput.Margin = new Padding(0, 0, 0, 10);
            ConfigureLabel(state, false); state.Margin = new Padding(0, 10, 0, 0);
            content.Controls.AddRange(new Control[] { icon, heading, description, original, current, inputLabel, number, text, choice, switchInput, state });
            layout.Controls.Add(content, 0, 0);
            var actions = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1, Padding = new Padding(12, 10, 12, 12), Margin = Padding.Empty, BackColor = Paper };
            actions.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 55)); actions.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 45));
            ConfigureButton(apply, "InspectorApply", true); ConfigureButton(reset, "InspectorReset", false);
            actions.Controls.Add(apply, 0, 0); actions.Controls.Add(reset, 1, 0); layout.Controls.Add(actions, 0, 1);
            Controls.Add(layout);
            number.ValueChanged += delegate { SignalDraft(); };
            number.TextChanged += delegate { SignalDraft(); };
            text.TextChanged += delegate { SignalDraft(); };
            choice.SelectedIndexChanged += delegate { SignalDraft(); };
            apply.Click += delegate { if (canEdit && HasPending) Raise(ApplyRequested); };
            reset.Click += delegate { if (HasPending) Raise(ResetRequested); };
            switchInput.LinkClicked += delegate { ToggleTextInput(); };
            content.SizeChanged += delegate { FitWidths(); };
            Bind(null, "", "", null, true, false);
            Disposed += delegate { if (icon.Image != null) { icon.Image.Dispose(); icon.Image = null; } };
        }

        public void SetIcon(Bitmap image)
        {
            Image previous = icon.Image; icon.Image = image; icon.Visible = image != null;
            if (previous != null) previous.Dispose();
        }

        private void ConfigureLabel(Label label, bool title)
        {
            label.AutoSize = true; label.ForeColor = title ? Ink : Muted;
            label.Margin = new Padding(0, 0, 0, title ? 10 : 8);
            label.UseMnemonic = false;
        }

        private static void ConfigureButton(Button button, string name, bool primary)
        {
            button.Name = name; button.Dock = DockStyle.Fill;
            button.FlatStyle = FlatStyle.Flat; button.UseVisualStyleBackColor = false;
            button.BackColor = primary ? Gold : Paper; button.ForeColor = Ink;
            button.FlatAppearance.BorderColor = primary ? Gold : Line;
            button.FlatAppearance.MouseOverBackColor = Color.FromArgb(243, 227, 191);
            button.Margin = new Padding(4, 0, 4, 0); button.Cursor = Cursors.Hand;
            button.UseMnemonic = false;
        }

        public void Bind(SaveField field, string label, string explanation, GameCatalog gameCatalog, bool useChinese, bool editable)
        {
            if (field == null) SetIcon(null);
            loading = true;
            try
            {
                Field = field; catalog = gameCatalog; chinese = useChinese;
                boundLabel = label ?? ""; boundDescription = explanation ?? "";
                canEdit = field != null && field.Editable && editable;
                heading.Text = field == null ? T("选择一项属性", "Select an attribute") : boundLabel;
                description.Text = field == null ? T("从左侧选择属性，在这里查看含义并修改数值。", "Select an attribute to review its meaning and edit its value here.") : boundDescription;
                original.Text = field == null ? "" : T("存档原值：", "Opened value: ") + DisplayValue(field.OriginalText);
                current.Text = field == null ? "" : T("当前工作值：", "Working value: ") + DisplayValue(field.Text);
                inputLabel.Text = field == null ? "" : canEdit ? T("修改为", "New value") : T("当前值（只读）", "Current value (read only)");
                apply.Text = T("应用当前值", "Apply value"); reset.Text = T("还原输入", "Reset input");
                number.Visible = text.Visible = choice.Visible = switchInput.Visible = false;
                editorKind = EditorKind.Text;
                if (field != null)
                {
                    if (canEdit && IsItemEffectType(field)) ConfigureChoices();
                    else if (canEdit && (field.Kind == "Int32" || field.Kind == "Float32") && ConfigureNumber(field.Text))
                    { editorKind = EditorKind.Number; number.Visible = true; }
                    else ConfigureText(field.Text);
                    switchInput.Visible = canEdit && field.Kind == "Float32";
                    switchInput.Text = editorKind == EditorKind.Number ? T("使用精确文本／科学计数输入", "Use exact text / scientific notation") : T("尝试数字输入控件", "Try the numeric input");
                }
                FitWidths();
            }
            finally { loading = false; }
            UpdateDraftState();
        }

        public void ResetDraft()
        {
            // This discards only pending input. Applied working changes remain.
            Bind(Field, boundLabel, boundDescription, catalog, chinese, canEdit);
            Raise(DraftChanged);
        }

        private bool ConfigureNumber(string value)
        {
            decimal parsed;
            if (!Decimal.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out parsed)) return false;
            decimal minimum, maximum;
            int places;
            if (Field.Kind == "Int32")
            {
                long integer;
                if (!Int64.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out integer)) return false;
                minimum = Field.Min; maximum = Field.Max; places = 0;
                if (minimum > maximum || parsed < minimum || parsed > maximum) return false;
            }
            else
            {
                float fromSave, fromDecimal;
                if (!Single.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out fromSave)
                    || Single.IsNaN(fromSave) || Single.IsInfinity(fromSave)
                    || !Single.TryParse(parsed.ToString("G29", CultureInfo.InvariantCulture), NumberStyles.Float, CultureInfo.InvariantCulture, out fromDecimal)
                    || BitConverter.ToInt32(BitConverter.GetBytes(fromSave), 0) != BitConverter.ToInt32(BitConverter.GetBytes(fromDecimal), 0)) return false;
                // Decimal conversion that underflows, rounds a significant float
                // or loses its sign uses the exact-text fallback instead.
                minimum = Decimal.MinValue; maximum = Decimal.MaxValue;
                places = Math.Max(8, (Decimal.GetBits(parsed)[3] >> 16) & 127);
            }
            number.Minimum = Decimal.MinValue; number.Maximum = Decimal.MaxValue;
            number.DecimalPlaces = places; number.Minimum = minimum; number.Maximum = maximum;
            number.Value = parsed;
            decimal step = 1M;
            int fractionalStep = Field.Kind == "Float32" ? Math.Min(places, 2) : 0;
            for (int i = 0; i < fractionalStep; i++) step /= 10M;
            number.Increment = step;
            number.Enabled = true;
            return true;
        }

        private void ConfigureText(string value)
        {
            editorKind = EditorKind.Text;
            text.Multiline = Field != null && Field.Kind == "String" && (value.IndexOf('\n') >= 0 || value.IndexOf('\r') >= 0 || value.Length > 90);
            text.Height = text.Multiline ? 110 : text.PreferredHeight;
            text.ScrollBars = text.Multiline ? ScrollBars.Vertical : ScrollBars.None;
            text.ReadOnly = !canEdit; text.BackColor = canEdit ? Color.White : Paper;
            text.Text = value; text.Visible = true;
        }

        private void ConfigureChoices()
        {
            choice.Items.Clear();
            var seen = new HashSet<int>();
            if (catalog != null)
                foreach (AttributeDefinition attribute in catalog.Attributes)
                {
                    if (GameCatalog.AttributeName(attribute.Id) == "Attribute " + attribute.Id.ToString(CultureInfo.InvariantCulture)) continue;
                    AddChoice(attribute.Id, attribute.Name); seen.Add(attribute.Id);
                }
            foreach (int id in new[] { 22, 24, 57, 58, 80, 82, 85, 100, 101, 102, 103, 104, 120, 122, 123, 140, 142, 143, 144, 160, 161, 163, 164, 180, 181, 182, 183, 184, 185, 200, 201, 202, 203 })
                if (!seen.Contains(id)) AddChoice(id, GameCatalog.AttributeName(id));
            AttributeChoice selected = null;
            foreach (AttributeChoice item in choice.Items) if (item.Value == Field.Text) { selected = item; break; }
            if (selected == null)
            {
                selected = new AttributeChoice { Value = Field.Text, Caption = T("原有未知属性（保留）", "Existing unknown attribute (keep)") };
                choice.Items.Add(selected);
            }
            choice.SelectedItem = selected; choice.Visible = true; choice.Enabled = canEdit;
            editorKind = EditorKind.Choice;
        }

        private void AddChoice(int id, string english)
        {
            choice.Items.Add(new AttributeChoice { Value = id.ToString(CultureInfo.InvariantCulture), Caption = FieldPresentation.EffectName(id, english, chinese) });
        }

        private bool IsItemEffectType(SaveField field)
        {
            return field.Name == "Attribute ID" && (field.Key ?? "").StartsWith("effect.", StringComparison.Ordinal)
                && (field.Node == null || (field.Node.Type & 0x101) == 0x101);
        }

        private string DisplayValue(string value)
        {
            if (Field != null && IsItemEffectType(Field))
            {
                int id;
                if (Int32.TryParse(value, out id)) return FieldPresentation.EffectName(id, GameCatalog.AttributeName(id), chinese);
            }
            return GameCatalog.PlainGameText(value ?? "");
        }

        private void ToggleTextInput()
        {
            if (Field == null || !canEdit || Field.Kind != "Float32") return;
            string value = DraftValue;
            loading = true;
            try
            {
                if (editorKind == EditorKind.Number) { number.Visible = false; ConfigureText(value); }
                else if (ConfigureNumber(value)) { text.Visible = false; editorKind = EditorKind.Number; number.Visible = true; }
                switchInput.Text = editorKind == EditorKind.Number ? T("使用精确文本／科学计数输入", "Use exact text / scientific notation") : T("尝试数字输入控件", "Try the numeric input");
            }
            finally { loading = false; }
            SignalDraft();
        }

        private void SignalDraft()
        {
            if (loading) return;
            UpdateDraftState(); Raise(DraftChanged);
        }

        private void UpdateDraftState()
        {
            bool pending = HasPending;
            apply.Enabled = canEdit && pending; reset.Enabled = pending;
            state.ForeColor = pending ? Color.FromArgb(139, 90, 30) : Muted;
            state.Text = Field == null ? "" : !canEdit ? T("只读：此项用于查看。", "Read only: this value is for inspection.")
                : pending ? T("输入待应用；保存前会先应用。", "Pending input; saving applies it first.")
                : Field.IsModified ? T("当前工作值已修改，尚未保存到存档。", "The working value has changed and is not yet saved.")
                : T("修改后点击“应用当前值”。", "Edit the value, then click Apply value.");
        }

        private static bool SameValue(SaveField field, string left, string right)
        {
            if (field.Kind == "Int32")
            {
                long a, b;
                return Int64.TryParse(left, NumberStyles.Integer, CultureInfo.InvariantCulture, out a)
                    && Int64.TryParse(right, NumberStyles.Integer, CultureInfo.InvariantCulture, out b) ? a == b : left == right;
            }
            if (field.Kind == "Float32")
            {
                float a, b;
                return Single.TryParse(left, NumberStyles.Float, CultureInfo.InvariantCulture, out a)
                    && Single.TryParse(right, NumberStyles.Float, CultureInfo.InvariantCulture, out b)
                    ? BitConverter.ToInt32(BitConverter.GetBytes(a), 0) == BitConverter.ToInt32(BitConverter.GetBytes(b), 0) : left == right;
            }
            return (left ?? "").Replace("\r\n", "\n").Replace('\r', '\n') == (right ?? "").Replace("\r\n", "\n").Replace('\r', '\n');
        }

        private void FitWidths()
        {
            int width = Math.Max(240, content.ClientSize.Width - content.Padding.Horizontal - SystemInformation.VerticalScrollBarWidth - 2);
            foreach (Label label in new[] { heading, description, original, current, inputLabel, state })
            { label.MaximumSize = new Size(width, 0); label.Width = width; }
            number.Width = text.Width = choice.Width = width;
            switchInput.MaximumSize = new Size(width, 0);
        }

        private string T(string zh, string en) { return chinese ? zh : en; }
        private void Raise(EventHandler handler) { if (handler != null) handler(this, EventArgs.Empty); }
    }
}
