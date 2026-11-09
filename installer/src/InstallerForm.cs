using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace SealHdInstaller
{
    public sealed class InstallerForm : Form
    {
        private readonly string initialDirectory;
        private readonly Label heading = new Label(), introduction = new Label(), folderLabel = new Label();
        private readonly Label resolutionLabel = new Label(), resolutionHelp = new Label(), inspectionStatus = new Label();
        private readonly Label status = new Label(), resultSummary = new Label();
        private readonly ComboBox directory = new ComboBox(), resolution = new ComboBox(), language = new ComboBox();
        private readonly Button browse = new Button(), detect = new Button(), install = new Button(), restore = new Button(), close = new Button();
        private readonly CheckBox borderless = new CheckBox();
        private readonly ProgressBar progress = new ProgressBar();
        private readonly LinkLabel showDetails = new LinkLabel(), openBackup = new LinkLabel(), openReport = new LinkLabel();
        private readonly TextBox details = new TextBox();
        private readonly System.Windows.Forms.Timer inspectTimer = new System.Windows.Forms.Timer { Interval = 350 };
        private bool chinese = true, busy, scanning, loading, inspecting, showTechnicalDetails, completed, restored;
        private int inspectionVersion, operationVersion;
        private string inspectedDirectory, latestDetails = "", backupDirectory, reportPath;
        private InstallInspection inspection;

        public InstallerForm(string gameDirectory = null)
        {
            initialDirectory = gameDirectory;
            Name = "SealHdInstallerForm";
            Text = "Seal of Evil HD Patch";
            Font = UiFonts.Create(); AutoScaleMode = AutoScaleMode.Dpi;
            BackColor = Color.FromArgb(242, 239, 232); ForeColor = Color.FromArgb(35, 43, 48);
            Size = new Size(900, 735); MinimumSize = new Size(800, 680);
            StartPosition = FormStartPosition.CenterScreen;
            try { Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); }
            catch (ArgumentException) { }
            BuildLayout(); ApplyLanguage();
            directory.TextChanged += delegate { if (!loading) ScheduleInspection(); };
            language.SelectedIndexChanged += delegate { chinese = language.SelectedIndex != 1; ApplyLanguage(); };
            browse.Click += delegate { BrowseDirectory(); };
            detect.Click += async delegate { await DetectDirectories(); };
            install.Click += async delegate { await RunOperation(false); };
            restore.Click += async delegate { await RunOperation(true); };
            close.Click += delegate { Close(); };
            inspectTimer.Tick += async delegate { inspectTimer.Stop(); await InspectDirectory(); };
            showDetails.LinkClicked += delegate { showTechnicalDetails = !showTechnicalDetails; details.Visible = showTechnicalDetails; ApplyLanguage(); };
            openBackup.LinkClicked += delegate { OpenResultPath(backupDirectory, true); };
            openReport.LinkClicked += delegate { OpenResultPath(reportPath, false); };
            FormClosing += delegate(object sender, FormClosingEventArgs e)
            {
                if (busy) { e.Cancel = true; status.Text = T("正在完成文件操作，请稍候。", "Please wait for the file operation to finish."); }
            };
            FormClosed += delegate { ++inspectionVersion; inspectTimer.Stop(); inspectTimer.Dispose(); };
            Shown += async delegate { await DetectDirectories(); };
        }

        private void BuildLayout()
        {
            var root = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(24), ColumnCount = 1, RowCount = 7 };
            root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 94));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 93));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 110));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 76));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 54));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 64));
            Controls.Add(root);

            var header = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 2, Margin = Padding.Empty };
            header.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100)); header.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 120));
            header.RowStyles.Add(new RowStyle(SizeType.Absolute, 47)); header.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            heading.Dock = DockStyle.Fill; heading.Font = UiFonts.Create(21F, FontStyle.Bold); heading.Margin = Padding.Empty;
            heading.TextAlign = ContentAlignment.MiddleLeft;
            language.Name = "InstallerLanguage"; language.DropDownStyle = ComboBoxStyle.DropDownList; language.Dock = DockStyle.Top;
            language.Items.AddRange(new object[] { "中文", "English" }); language.SelectedIndex = 0; language.Margin = new Padding(0, 8, 0, 0);
            introduction.Dock = DockStyle.Fill; introduction.ForeColor = Color.FromArgb(83, 98, 107); introduction.Margin = Padding.Empty;
            header.Controls.Add(heading, 0, 0); header.Controls.Add(language, 1, 0); header.Controls.Add(introduction, 0, 1); header.SetColumnSpan(introduction, 2);
            root.Controls.Add(header, 0, 0);

            var folder = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 3, RowCount = 2, Margin = Padding.Empty };
            folder.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100)); folder.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 110)); folder.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 120));
            folder.RowStyles.Add(new RowStyle(SizeType.Absolute, 31)); folder.RowStyles.Add(new RowStyle(SizeType.Absolute, 45));
            folderLabel.Dock = DockStyle.Fill; folderLabel.Font = UiFonts.Create(10F, FontStyle.Bold); folderLabel.Margin = Padding.Empty;
            directory.Name = "InstallerGameDirectory"; directory.Dock = DockStyle.Top; directory.DropDownStyle = ComboBoxStyle.DropDown;
            directory.Margin = new Padding(0, 3, 10, 0); directory.IntegralHeight = false; directory.DropDownHeight = 200;
            ConfigureButton(browse, "InstallerBrowseDirectory", false); ConfigureButton(detect, "InstallerDetectDirectories", false);
            folder.Controls.Add(folderLabel, 0, 0); folder.SetColumnSpan(folderLabel, 3); folder.Controls.Add(directory, 0, 1); folder.Controls.Add(browse, 1, 1); folder.Controls.Add(detect, 2, 1);
            root.Controls.Add(folder, 0, 1);

            var display = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 3, Margin = Padding.Empty };
            display.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 290)); display.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            display.RowStyles.Add(new RowStyle(SizeType.Absolute, 29)); display.RowStyles.Add(new RowStyle(SizeType.Absolute, 39)); display.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            resolutionLabel.Dock = DockStyle.Fill; resolutionLabel.Font = UiFonts.Create(10F, FontStyle.Bold); resolutionLabel.Margin = Padding.Empty;
            resolution.Name = "InstallerResolution"; resolution.DropDownStyle = ComboBoxStyle.DropDownList; resolution.Dock = DockStyle.Top; resolution.Margin = new Padding(0, 3, 20, 0);
            resolution.IntegralHeight = false; resolution.DropDownHeight = 360; resolution.DropDownWidth = 300;
            borderless.Name = "InstallerBorderless"; borderless.Checked = true; borderless.AutoSize = true; borderless.Margin = new Padding(0, 5, 0, 0);
            resolutionHelp.Dock = DockStyle.Fill; resolutionHelp.Margin = Padding.Empty; resolutionHelp.ForeColor = Color.FromArgb(83, 98, 107);
            display.Controls.Add(resolutionLabel, 0, 0); display.SetColumnSpan(resolutionLabel, 2); display.Controls.Add(resolution, 0, 1); display.Controls.Add(borderless, 1, 1); display.Controls.Add(resolutionHelp, 0, 2); display.SetColumnSpan(resolutionHelp, 2);
            root.Controls.Add(display, 0, 2);

            var check = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2, Padding = new Padding(12, 8, 12, 8), Margin = new Padding(0, 0, 0, 10), BackColor = Color.FromArgb(232, 237, 237) };
            check.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100)); check.RowStyles.Add(new RowStyle(SizeType.Percent, 100)); check.RowStyles.Add(new RowStyle(SizeType.Absolute, 24));
            inspectionStatus.Name = "InstallerInspectStatus"; inspectionStatus.Dock = DockStyle.Fill; inspectionStatus.Margin = Padding.Empty; inspectionStatus.AutoEllipsis = true;
            showDetails.Name = "InstallerShowDetails"; showDetails.AutoSize = true; showDetails.LinkColor = Color.FromArgb(80, 103, 108); showDetails.Margin = Padding.Empty;
            check.Controls.Add(inspectionStatus, 0, 0); check.Controls.Add(showDetails, 0, 1); root.Controls.Add(check, 0, 3);

            var result = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2, Margin = Padding.Empty };
            result.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100)); result.RowStyles.Add(new RowStyle(SizeType.Absolute, 72)); result.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            resultSummary.Name = "InstallerResult"; resultSummary.Dock = DockStyle.Fill; resultSummary.Margin = new Padding(0, 2, 0, 4); resultSummary.AutoEllipsis = true;
            details.Name = "InstallerInspectDetails"; details.Dock = DockStyle.Fill; details.Multiline = true; details.ReadOnly = true; details.ScrollBars = ScrollBars.Vertical; details.BackColor = Color.White; details.Visible = false; details.BorderStyle = BorderStyle.FixedSingle;
            result.Controls.Add(resultSummary, 0, 0); result.Controls.Add(details, 0, 1); root.Controls.Add(result, 0, 4);

            var progressArea = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2, Margin = Padding.Empty };
            progressArea.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100)); progressArea.RowStyles.Add(new RowStyle(SizeType.Absolute, 27)); progressArea.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            status.Name = "InstallerStatus"; status.Dock = DockStyle.Fill; status.Margin = Padding.Empty; status.AutoEllipsis = true;
            progress.Name = "InstallerProgress"; progress.Dock = DockStyle.Top; progress.Height = 12; progress.Margin = new Padding(0, 0, 0, 5);
            progressArea.Controls.Add(status, 0, 0); progressArea.Controls.Add(progress, 0, 1); root.Controls.Add(progressArea, 0, 5);

            var actions = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 4, RowCount = 1, Margin = Padding.Empty };
            actions.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 190)); actions.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 175)); actions.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100)); actions.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 104));
            ConfigureButton(install, "InstallerInstall", true); ConfigureButton(restore, "InstallerRestore", false); ConfigureButton(close, "InstallerClose", false);
            var links = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = true, Margin = Padding.Empty, Padding = new Padding(4, 4, 0, 0) };
            openBackup.Name = "InstallerOpenBackup"; openReport.Name = "InstallerOpenReport";
            foreach (var link in new[] { openBackup, openReport }) { link.AutoSize = true; link.Font = UiFonts.Create(9F, FontStyle.Regular); link.LinkColor = Color.FromArgb(80, 103, 108); link.Visible = false; link.Margin = new Padding(3, 4, 9, 0); links.Controls.Add(link); }
            actions.Controls.Add(install, 0, 0); actions.Controls.Add(restore, 1, 0); actions.Controls.Add(links, 2, 0); actions.Controls.Add(close, 3, 0); root.Controls.Add(actions, 0, 6);
            AcceptButton = install; CancelButton = close;
        }

        private static void ConfigureButton(Button button, string name, bool primary)
        {
            button.Name = name; button.Dock = DockStyle.Top; button.Height = 39; button.Margin = new Padding(0, 4, 9, 0);
            button.FlatStyle = FlatStyle.Flat; button.FlatAppearance.BorderColor = Color.FromArgb(196, 199, 193);
            button.BackColor = primary ? Color.FromArgb(231, 185, 96) : Color.White;
            button.UseVisualStyleBackColor = false; button.UseMnemonic = false;
        }

        private string T(string zh, string en) { return chinese ? zh : en; }

        private void ApplyLanguage()
        {
            Text = T("复活 高清补丁安装器", "Seal of Evil HD Patch Installer");
            heading.Text = T("复活  /  高清补丁", "SEAL OF EVIL  /  HD PATCH");
            introduction.Text = T("一次安装兼容修复、高清画面与高分辨率支持。", "Compatibility fixes, HD artwork and resolutions in one installer.");
            folderLabel.Text = T("游戏安装目录", "Game installation folder"); browse.Text = T("浏览…", "Browse"); detect.Text = T("重新查找", "Find");
            resolutionLabel.Text = T("游戏分辨率", "Game resolution"); borderless.Text = T("无边框全屏", "Borderless fullscreen");
            resolutionHelp.Text = T("支持 16:9／16:10，默认推荐 1080p。", "16:9 / 16:10 supported. Default: 1080p.");
            var selected = resolution.SelectedItem as ResolutionProfile ?? ResolutionProfiles.Default;
            resolution.Items.Clear(); resolution.Items.AddRange(ResolutionProfiles.Ordered.Cast<object>().ToArray());
            resolution.SelectedItem = ResolutionProfiles.Ordered.FirstOrDefault(p => p.Id == selected.Id) ?? ResolutionProfiles.Default;
            showDetails.Text = showTechnicalDetails ? T("收起检查详情", "Hide check details") : T("查看检查详情", "Show check details");
            restore.Text = T("恢复原文件", "Restore"); close.Text = T("关闭", "Close");
            openBackup.Text = T("查看备份", "Open backup"); openReport.Text = T("安装记录", "Install record");
            if (completed) resultSummary.Text = restored ? T("已恢复安装前的文件。", "The files from before installation have been restored.") : T("安装完成。现在可以从 Steam 启动游戏。\r\n缺少 IV50 解码时会自动跳过原开头／结尾视频，原文件保留。", "Installed. You can now start the game through Steam.\r\nWithout IV50, intro/ending videos skip automatically; files are kept.");
            else if (!busy) resultSummary.Text = T("安装会备份替换的文件，保留存档。\r\n缺少 IV50 解码时自动跳过原开头／结尾视频，原文件保留。", "Replaced files are backed up; saved games are preserved.\r\nWithout IV50, intro/ending videos skip automatically; files are kept.");
            if (busy) status.Text = T("正在处理，请稍候…", "Working, please wait...");
            else if (completed) status.Text = T("完成", "Complete");
            else if (!scanning && !inspecting) status.Text = T("准备就绪", "Ready");
            UpdateControls();
        }

        private void UpdateControls()
        {
            bool exact = inspection != null && String.Equals(inspectedDirectory, directory.Text.Trim(), StringComparison.OrdinalIgnoreCase);
            bool ready = !busy && !scanning && !inspecting && exact;
            install.Text = exact && inspection.HasExistingPatch ? T("更新完整补丁", "Update patch") : T("安装完整补丁", "Install patch");
            install.Enabled = ready && inspection.Supported && !inspection.GameRunning;
            restore.Enabled = ready && inspection.HasExistingPatch && !inspection.GameRunning;
            directory.Enabled = browse.Enabled = detect.Enabled = resolution.Enabled = borderless.Enabled = !busy && !scanning;
            close.Enabled = language.Enabled = !busy;
            if (busy) return;
            if (scanning) inspectionStatus.Text = T("正在查找 Steam 游戏目录…", "Finding Steam game folders...");
            else if (inspecting) inspectionStatus.Text = T("正在检查所选游戏目录…", "Checking the selected game folder...");
            else if (!exact) inspectionStatus.Text = T("请选择 Seal of Evil 的安装目录。", "Select your Seal of Evil installation folder.");
            else if (inspection.GameRunning) inspectionStatus.Text = T("游戏正在运行。请先保存进度并退出游戏。", "The game is running. Save and exit before continuing.");
            else if (!inspection.Supported) inspectionStatus.Text = T("此目录未通过检查。请确认选中了受支持的游戏目录。", "Unsupported folder. Choose the Seal of Evil game folder.");
            else if (inspection.ConflictingLoader) inspectionStatus.Text = T("检测到其他加载器；安装时可以选择先备份并替换。", "Another loader was found. You can back it up and replace it.");
            else inspectionStatus.Text = inspection.HasExistingPatch ? T("已找到现有高清补丁，可以更新完整安装。", "An existing HD patch was found. Ready to update.") : T("已找到支持的游戏，可以安装完整补丁。", "A supported game was found. Ready to install.");
        }

        private void ScheduleInspection()
        {
            ++inspectionVersion; inspection = null; inspectedDirectory = null; completed = false;
            resultSummary.Text = T("安装会备份替换的文件，保留存档。\r\n缺少 IV50 解码时自动跳过原开头／结尾视频，原文件保留。", "Replaced files are backed up; saved games are preserved.\r\nWithout IV50, intro/ending videos skip automatically; files are kept.");
            openBackup.Visible = openReport.Visible = false;
            inspectTimer.Stop(); inspecting = directory.Text.Trim().Length > 0;
            if (inspecting) inspectTimer.Start();
            UpdateControls();
        }

        private async Task DetectDirectories()
        {
            if (busy || scanning) return;
            inspectTimer.Stop(); ++inspectionVersion; scanning = true; inspecting = false; UpdateControls();
            string previous = directory.Text.Trim();
            try
            {
                IList<string> detected = await Task.Run(delegate { return InstallerService.DetectGameDirectories(); });
                if (IsDisposed) return;
                loading = true; directory.Items.Clear();
                foreach (string path in detected.Distinct(StringComparer.OrdinalIgnoreCase)) directory.Items.Add(path);
                string candidate = !String.IsNullOrWhiteSpace(previous) ? previous : !String.IsNullOrWhiteSpace(initialDirectory) ? initialDirectory : detected.FirstOrDefault();
                directory.Text = candidate ?? "";
            }
            catch (Exception error) { if (!IsDisposed) { latestDetails = error.Message; details.Text = latestDetails; } }
            finally
            {
                loading = false; scanning = false;
                if (!IsDisposed) { UpdateControls(); ScheduleInspection(); }
            }
        }

        private async Task InspectDirectory()
        {
            string path = directory.Text.Trim(); int version = inspectionVersion;
            if (busy || path.Length == 0) return;
            inspecting = true; UpdateControls();
            try
            {
                InstallInspection value = await Task.Run(delegate { return InstallerService.Inspect(path); });
                if (IsDisposed || version != inspectionVersion || path != directory.Text.Trim()) return;
                inspection = value; inspectedDirectory = path; latestDetails = value.Details ?? ""; details.Text = latestDetails;
            }
            catch (Exception error)
            {
                if (!IsDisposed && version == inspectionVersion) { inspection = null; latestDetails = error.Message; details.Text = latestDetails; status.Text = T("目录检查失败；可查看详情或重新选择目录。", "Folder check failed. View details or select another folder."); }
            }
            finally { if (!IsDisposed && version == inspectionVersion) { inspecting = false; UpdateControls(); } }
        }

        private void BrowseDirectory()
        {
            using (var picker = new FolderBrowserDialog { Description = T("选择包含 SOE.EXE 的游戏目录", "Select the game folder containing SOE.EXE"), ShowNewFolderButton = false })
            {
                if (Directory.Exists(directory.Text.Trim())) picker.SelectedPath = directory.Text.Trim();
                if (picker.ShowDialog(this) == DialogResult.OK) directory.Text = picker.SelectedPath;
            }
        }

        private async Task RunOperation(bool recovering)
        {
            if (busy || directory.Text.Trim().Length == 0) return;
            string path = directory.Text.Trim();
            var profile = resolution.SelectedItem as ResolutionProfile ?? ResolutionProfiles.Default;
            var options = new InstallOptions { GameDirectory = path, Width = profile.Width, Height = profile.Height, Borderless = borderless.Checked, ReplaceConflictingLoader = false };
            busy = true; completed = false; restored = recovering; ++inspectionVersion; inspecting = false; inspectTimer.Stop();
            int currentOperation = ++operationVersion; progress.Value = 0; backupDirectory = reportPath = null; openBackup.Visible = openReport.Visible = false;
            details.Text = ""; resultSummary.Text = T("正在检查并准备文件…", "Checking and preparing files..."); ApplyLanguage();
            try
            {
                var fresh = await Task.Run(delegate { return InstallerService.Inspect(path); });
                if (fresh.GameRunning) throw new InvalidOperationException(T("请保存游戏进度并退出游戏后再安装。", "Save your progress and exit the game before continuing."));
                if (!recovering && !fresh.Supported) throw new InvalidOperationException(String.IsNullOrWhiteSpace(fresh.Details) ? T("所选目录不是支持的游戏安装。", "The selected folder is not a supported game installation.") : fresh.Details);
                if (recovering)
                {
                    if (MessageBox.Show(this, T("恢复安装前的文件并移除本补丁？游戏存档会保留。", "Restore the files from before installation and remove this patch? Saved games will be preserved."), Text, MessageBoxButtons.YesNo, MessageBoxIcon.Question, MessageBoxDefaultButton.Button2) != DialogResult.Yes)
                    { resultSummary.Text = T("已取消，未更改任何文件。", "Canceled. No files were changed."); return; }
                }
                else if (fresh.ConflictingLoader)
                {
                    if (MessageBox.Show(this, T("检测到其他 dinput8.dll 加载器。继续会先备份它，再安装本补丁。\r\n\r\n是否备份并替换？", "Another dinput8.dll loader is installed. Continuing backs it up before installing this patch.\r\n\r\nBack up and replace it?"), Text, MessageBoxButtons.YesNo, MessageBoxIcon.Question, MessageBoxDefaultButton.Button2) != DialogResult.Yes)
                    { resultSummary.Text = T("已取消，未更改任何文件。", "Canceled. No files were changed."); return; }
                    options.ReplaceConflictingLoader = true;
                }
                Action<InstallProgress> report = delegate(InstallProgress update)
                {
                    if (IsDisposed || !IsHandleCreated) return;
                    try { BeginInvoke((Action)delegate
                    {
                        if (!busy || currentOperation != operationVersion || IsDisposed) return;
                        progress.Value = Math.Max(0, Math.Min(100, update.Percent));
                        status.Text = (recovering ? T("正在恢复…", "Restoring...") : T("正在安装…", "Installing...")) + " " + progress.Value + "%";
                        if (!String.IsNullOrWhiteSpace(update.Message)) details.AppendText(update.Message + Environment.NewLine);
                    }); }
                    catch (InvalidOperationException) { if (!IsDisposed && IsHandleCreated) throw; }
                };
                InstallResult result = await Task.Run(delegate { return recovering ? InstallerService.Restore(path, report) : InstallerService.Install(options, report); });
                completed = true; progress.Value = 100; backupDirectory = result.BackupDirectory; reportPath = result.ReportPath;
                if (!String.IsNullOrWhiteSpace(result.Summary)) details.AppendText(result.Summary + Environment.NewLine);
                openBackup.Visible = !String.IsNullOrWhiteSpace(backupDirectory); openReport.Visible = !String.IsNullOrWhiteSpace(reportPath);
            }
            catch (Exception error)
            {
                resultSummary.Text = T("未完成。请查看错误信息后重试。", "Not completed. Review the error and try again.");
                details.AppendText(error.Message + Environment.NewLine); showTechnicalDetails = true; details.Visible = true;
                showDetails.Text = T("收起检查详情", "Hide check details");
                MessageBox.Show(this, error.Message, Text, MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            finally
            {
                busy = false; ++operationVersion; status.Text = completed ? T("完成", "Complete") : T("已停止", "Stopped");
                if (completed) ApplyLanguage(); else UpdateControls();
                InspectDirectoryAfterOperation(path);
            }
        }

        private async void InspectDirectoryAfterOperation(string path)
        {
            int version = ++inspectionVersion;
            try
            {
                var value = await Task.Run(delegate { return InstallerService.Inspect(path); });
                if (IsDisposed || version != inspectionVersion || directory.Text.Trim() != path) return;
                inspection = value; inspectedDirectory = path;
            }
            catch { inspection = null; }
            finally { if (!IsDisposed) UpdateControls(); }
        }

        private void OpenResultPath(string path, bool folder)
        {
            try
            {
                if (String.IsNullOrWhiteSpace(path) || (folder ? !Directory.Exists(path) : !File.Exists(path))) return;
                Process.Start(new ProcessStartInfo { FileName = path, UseShellExecute = true });
            }
            catch (Exception error) { MessageBox.Show(this, error.Message, Text, MessageBoxButtons.OK, MessageBoxIcon.Error); }
        }
    }
}
