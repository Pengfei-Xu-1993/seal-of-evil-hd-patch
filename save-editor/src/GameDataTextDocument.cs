using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace SealSaveEditor
{
    public sealed class GameDataTextRow
    {
        public int Index { get; internal set; }
        public int LineNumber { get { return Index + 1; } }
        public string RawText { get; internal set; }
        public IList<string> Cells { get; internal set; }
        public bool IsHeader { get; internal set; }
        public bool IsComment { get; internal set; }
        internal string Ending;
    }

    public sealed class GameDataTextBackup
    {
        public string Path { get; internal set; }
        public DateTime CreatedUtc { get; internal set; }
        public long Length { get; internal set; }
        public string Sha256 { get; internal set; }
        public override string ToString() { return CreatedUtc.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture) + " / " + Length.ToString(CultureInfo.InvariantCulture) + " bytes"; }
    }

    /// <summary>Lossless editor for one explicitly selected installed Data/*.txt file. No game data is bundled.</summary>
    public sealed class GameDataTextDocument
    {
        private const string BackupMagic = "SealSaveEditor Data backup v1";
        private static readonly Encoding Gbk = Encoding.GetEncoding(936, EncoderFallback.ExceptionFallback, DecoderFallback.ExceptionFallback);
        private byte[] originalBytes;
        private string originalText, text;
        private List<GameDataTextRow> rows;
        private List<string> columnNames, columnDescriptions;
        private readonly Func<bool> gameRunning;
        public string GameDirectory { get; private set; }
        public string FilePath { get; private set; }
        public string FileName { get { return System.IO.Path.GetFileName(FilePath); } }
        public string OriginalSha256 { get; private set; }
        public string EncodingName { get { return "GBK (Windows-936)"; } }
        public string NewLine { get; private set; }
        public bool HasMixedNewLines { get; private set; }
        public bool IsEncoded { get; private set; }
        public bool IsModified { get { return !String.Equals(text, originalText, StringComparison.Ordinal); } }
        public bool IsTabular { get; private set; }
        public IList<GameDataTextRow> Rows { get { return rows.AsReadOnly(); } }
        public IList<string> ColumnNames { get { return columnNames.AsReadOnly(); } }
        public string Text
        {
            get { return text; }
            set
            {
                if (value == null) throw new ArgumentNullException("value");
                if (value.IndexOf('\0') >= 0) throw new ArgumentException("Game data text cannot contain a NUL character.", "value");
                text = value; ParseRows();
            }
        }

        private GameDataTextDocument(string gameDirectory, string path, Func<bool> processProbe)
        {
            GameDirectory = System.IO.Path.GetFullPath(gameDirectory);
            FilePath = System.IO.Path.GetFullPath(path);
            gameRunning = processProbe;
            ValidateSelectedPath();
            SetBaseline(File.ReadAllBytes(IoPath(FilePath)));
        }

        public static GameDataTextDocument Load(string gameDirectory, string dataFilePath)
        { return new GameDataTextDocument(gameDirectory, dataFilePath, IsSoeRunning); }

        // Only the isolated test assembly supplies this probe; the production Load always checks real SOE processes.
        internal static GameDataTextDocument LoadForTesting(string gameDirectory, string dataFilePath, Func<bool> processProbe)
        {
            if (processProbe == null) throw new ArgumentNullException("processProbe");
            return new GameDataTextDocument(gameDirectory, dataFilePath, processProbe);
        }

        public string GetColumnDescription(int columnIndex)
        {
            if (columnIndex < 0 || columnIndex >= columnDescriptions.Count) throw new ArgumentOutOfRangeException("columnIndex");
            return columnDescriptions[columnIndex];
        }

        public void SetCell(int rowIndex, int columnIndex, string value)
        {
            if (!IsTabular) throw new InvalidOperationException("This file has no recognized table header. Use the original-text view.");
            if (rowIndex < 0 || rowIndex >= rows.Count) throw new ArgumentOutOfRangeException("rowIndex");
            GameDataTextRow row = rows[rowIndex];
            if (row.IsHeader || row.IsComment || row.Cells.Count < 2)
                throw new InvalidOperationException("Headers, comments and unrecognized lines must be edited deliberately in the original-text view.");
            if (columnIndex < 0 || columnIndex >= row.Cells.Count) throw new ArgumentOutOfRangeException("columnIndex", "This original row has no such column. Use the original-text view to change its structure.");
            value = value ?? "";
            if (value.IndexOfAny(new[] { '\t', '\r', '\n', '\0' }) >= 0)
                throw new ArgumentException("A table cell cannot contain tab, line-break or NUL characters.", "value");
            string[] cells = row.Cells.ToArray(); cells[columnIndex] = value;
            row.RawText = String.Join("\t", cells); row.Cells = Array.AsReadOnly(cells);
            text = String.Concat(rows.Select(r => r.RawText + r.Ending));
            ParseRows();
        }

        public void ResetDraft() { text = originalText; ParseRows(); }

        public byte[] ToBytes()
        {
            if (!IsModified) return (byte[])originalBytes.Clone();
            byte[] plain;
            try { plain = Gbk.GetBytes(text); }
            catch (EncoderFallbackException e) { throw new InvalidDataException("The edited text contains characters that the game's GBK encoding cannot store.", e); }
            if (!IsEncoded) return plain;
            byte[] encoded = new byte[plain.Length + 1]; encoded[0] = 0x80;
            for (int i = 0; i < plain.Length; i++) encoded[i + 1] = (byte)(plain[i] ^ 0x78);
            return encoded;
        }

        /// <summary>Back up the current bytes and atomically replace the selected Data text. Returns the backup path, or null when unchanged.</summary>
        public string AtomicSave()
        {
            byte[] bytes = ToBytes();
            ValidateSelectedPath();
            byte[] current = ReadExpectedCurrent();
            if (bytes.SequenceEqual(current)) return null;
            return CommitBytes(current, bytes, "save");
        }

        /// <summary>Restore one verified backup for this exact file, backing up the current bytes first.</summary>
        public string RestoreBackup(string backupPath)
        {
            if (IsModified) throw new InvalidOperationException("Save or discard the current text draft before restoring a backup.");
            GameDataTextBackup backup = ReadBackup(backupPath);
            byte[] bytes = File.ReadAllBytes(IoPath(backup.Path));
            if (bytes.LongLength != backup.Length || Hash(bytes) != backup.Sha256) throw new InvalidDataException("The selected backup no longer matches its recorded fingerprint.");
            // Validate the restored encoding before making any backup or replacing the target.
            Decode(bytes);
            ValidateSelectedPath(); byte[] current = ReadExpectedCurrent();
            if (current.SequenceEqual(bytes)) return null;
            return CommitBytes(current, bytes, "restore");
        }

        public IList<GameDataTextBackup> ListBackups()
        {
            string folder = BackupFolder(); var result = new List<GameDataTextBackup>();
            if (!Directory.Exists(IoPath(folder))) return result.AsReadOnly();
            ValidateBackupDirectories(false);
            foreach (string physicalPath in Directory.GetFiles(IoPath(folder), "*.bak", SearchOption.TopDirectoryOnly))
            {
                string path = NormalPath(physicalPath);
                try { result.Add(ReadBackup(path)); }
                catch (InvalidDataException) { }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }
            }
            return result.OrderByDescending(b => b.CreatedUtc).ToList().AsReadOnly();
        }

        private string CommitBytes(byte[] current, byte[] replacement, string reason)
        {
            RequireGameClosed();
            string backup = CreateBackup(current, reason);
            string temporary = System.IO.Path.Combine(System.IO.Path.GetDirectoryName(FilePath), FileName + ".seal-data-" + Guid.NewGuid().ToString("N") + ".tmp");
            try
            {
                // The backup can take time: do not commit over an intervening game/tool edit.
                RequireGameClosed(); ValidateSelectedPath(); ReadExpectedCurrent();
                WriteNewFile(temporary, replacement);
                RequireGameClosed(); ValidateSelectedPath(); ReadExpectedCurrent();
                File.Replace(IoPath(temporary), IoPath(FilePath), null);
                SetBaseline(replacement);
                return backup;
            }
            finally
            {
                // Only this new GUID filename in the already validated selected Data directory is removed.
                if (File.Exists(IoPath(temporary))) File.Delete(IoPath(temporary));
            }
        }

        private byte[] ReadExpectedCurrent()
        {
            byte[] current = File.ReadAllBytes(IoPath(FilePath));
            if (Hash(current) != OriginalSha256)
                throw new InvalidOperationException("This Data file changed on disk after it was opened. Reload it before saving or restoring.");
            return current;
        }

        private string CreateBackup(byte[] bytes, string reason)
        {
            ValidateBackupDirectories(true);
            DateTime now = DateTime.UtcNow;
            string path = System.IO.Path.Combine(BackupFolder(), now.ToString("yyyyMMdd-HHmmss-fff", CultureInfo.InvariantCulture) + "-" + Guid.NewGuid().ToString("N") + ".bak");
            string sha = Hash(bytes);
            WriteNewFile(path, bytes);
            string[] metadata = { BackupMagic, FileName, sha, bytes.LongLength.ToString(CultureInfo.InvariantCulture), now.Ticks.ToString(CultureInfo.InvariantCulture),
                Convert.ToBase64String(Encoding.UTF8.GetBytes(FilePath)), reason };
            try { WriteNewFile(path + ".meta", new UTF8Encoding(false).GetBytes(String.Join("\n", metadata) + "\n")); }
            catch { File.Delete(IoPath(path)); throw; }
            return path;
        }

        private GameDataTextBackup ReadBackup(string selected)
        {
            if (String.IsNullOrWhiteSpace(selected)) throw new ArgumentNullException("selected");
            string path = System.IO.Path.GetFullPath(NormalPath(selected));
            if (!String.Equals(System.IO.Path.GetDirectoryName(path), BackupFolder(), StringComparison.OrdinalIgnoreCase)
                || !String.Equals(System.IO.Path.GetExtension(path), ".bak", StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("Select a backup created for this exact installed Data file.");
            ValidateBackupDirectories(false); RejectReparse(path); RejectReparse(path + ".meta");
            string[] lines = File.ReadAllLines(IoPath(path + ".meta"), Encoding.UTF8);
            long length, ticks;
            if (lines.Length < 7 || lines[0] != BackupMagic || lines[1] != FileName || !Int64.TryParse(lines[3], NumberStyles.None, CultureInfo.InvariantCulture, out length)
                || !Int64.TryParse(lines[4], NumberStyles.None, CultureInfo.InvariantCulture, out ticks) || length < 0 || ticks < DateTime.MinValue.Ticks || ticks > DateTime.MaxValue.Ticks)
                throw new InvalidDataException("Invalid Data backup metadata.");
            string source;
            try { source = Encoding.UTF8.GetString(Convert.FromBase64String(lines[5])); }
            catch (FormatException e) { throw new InvalidDataException("Invalid Data backup source identity.", e); }
            if (!String.Equals(source, FilePath, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("This backup belongs to another installed Data file.");
            byte[] bytes = File.ReadAllBytes(IoPath(path));
            if (bytes.LongLength != length || Hash(bytes) != lines[2]) throw new InvalidDataException("The Data backup was changed or is incomplete.");
            return new GameDataTextBackup { Path = path, Length = length, Sha256 = lines[2], CreatedUtc = new DateTime(ticks, DateTimeKind.Utc) };
        }

        private string BackupFolder()
        { return System.IO.Path.Combine(GameDirectory, "SealSaveEditor-DataBackups", FileName); }

        private void ValidateBackupDirectories(bool create)
        {
            string root = System.IO.Path.Combine(GameDirectory, "SealSaveEditor-DataBackups"), folder = BackupFolder();
            if (Directory.Exists(IoPath(root))) RejectReparse(root);
            if (Directory.Exists(IoPath(folder))) RejectReparse(folder);
            if (create) Directory.CreateDirectory(IoPath(folder));
            if (!Directory.Exists(IoPath(root)) || !Directory.Exists(IoPath(folder))) throw new DirectoryNotFoundException("The Data backup directory does not exist.");
            RejectReparse(root); RejectReparse(folder);
        }

        private void ValidateSelectedPath()
        {
            string data = System.IO.Path.Combine(GameDirectory, "Data");
            if (!String.Equals(System.IO.Path.GetDirectoryName(FilePath), data, StringComparison.OrdinalIgnoreCase)
                || !String.Equals(System.IO.Path.GetExtension(FilePath), ".txt", StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("Select one .txt file directly inside this installation's Data directory.");
            if (!Directory.Exists(IoPath(data)) || !File.Exists(IoPath(FilePath))) throw new FileNotFoundException("The selected installed Data text does not exist.", FilePath);
            RejectReparse(data); RejectReparse(FilePath);
        }

        private static void RejectReparse(string path)
        { if ((File.GetAttributes(IoPath(path)) & FileAttributes.ReparsePoint) != 0) throw new InvalidDataException("Linked Data files and backup directories cannot be edited through this tool."); }

        private void RequireGameClosed()
        { if (gameRunning()) throw new InvalidOperationException("Close Seal of Evil (SOE.EXE) before saving or restoring game Data text."); }

        private static bool IsSoeRunning()
        {
            Process[] processes = Process.GetProcessesByName("SOE");
            try { foreach (Process process in processes) { try { if (!process.HasExited) return true; } catch (InvalidOperationException) { } } return false; }
            finally { foreach (Process process in processes) process.Dispose(); }
        }

        private void SetBaseline(byte[] bytes)
        {
            string decoded = Decode(bytes);
            originalBytes = (byte[])bytes.Clone(); OriginalSha256 = Hash(bytes);
            IsEncoded = bytes.Length > 0 && bytes[0] == 0x80;
            originalText = text = decoded; ParseRows();
        }

        private static string Decode(byte[] bytes)
        {
            bool encoded = bytes.Length > 0 && bytes[0] == 0x80;
            byte[] plain;
            if (encoded) { plain = new byte[bytes.Length - 1]; for (int i = 0; i < plain.Length; i++) plain[i] = (byte)(bytes[i + 1] ^ 0x78); }
            else plain = bytes;
            try
            {
                string result = Gbk.GetString(plain);
                if (result.IndexOf('\0') >= 0) throw new InvalidDataException("This selected file is not supported GBK game data text (NUL byte found).");
                return result;
            }
            catch (DecoderFallbackException e) { throw new InvalidDataException("This selected Data file cannot be decoded losslessly as the game's GBK text.", e); }
        }

        private void ParseRows()
        {
            rows = new List<GameDataTextRow>(); var endings = new HashSet<string>(StringComparer.Ordinal); int begin = 0;
            while (begin < text.Length)
            {
                int end = begin;
                while (end < text.Length && text[end] != '\r' && text[end] != '\n') end++;
                string raw = text.Substring(begin, end - begin), ending = "";
                if (end < text.Length) { ending = text[end++].ToString(); if (ending == "\r" && end < text.Length && text[end] == '\n') { ending += "\n"; end++; } }
                if (ending.Length > 0) endings.Add(ending);
                string trimmed = raw.TrimStart();
                rows.Add(new GameDataTextRow { Index = rows.Count, RawText = raw, Cells = Array.AsReadOnly(raw.Split(new[] { '\t' }, StringSplitOptions.None)), Ending = ending,
                    IsComment = trimmed.StartsWith("//", StringComparison.Ordinal) || trimmed.StartsWith("#", StringComparison.Ordinal) || trimmed.StartsWith(";", StringComparison.Ordinal) });
                begin = end;
            }
            if (rows.Count == 0) rows.Add(new GameDataTextRow { Index = 0, RawText = "", Cells = Array.AsReadOnly(new[] { "" }), Ending = "" });
            NewLine = rows.Where(r => r.Ending.Length > 0).Select(r => r.Ending).FirstOrDefault() ?? Environment.NewLine;
            HasMixedNewLines = endings.Count > 1;
            columnNames = new List<string>(); columnDescriptions = new List<string>();
            GameDataTextRow header = rows.Take(12).FirstOrDefault(r => !r.IsComment && r.Cells.Count > 1 && IsHeaderStart(r.Cells[0]));
            IsTabular = header != null;
            if (header == null) return;
            int columns = rows.Max(r => r.Cells.Count);
            foreach (GameDataTextRow row in rows.Where(r => r.Index <= header.Index)) row.IsHeader = true;
            for (int col = 0; col < columns; col++)
            {
                string label = col < header.Cells.Count ? header.Cells[col].Trim() : "";
                if (label.Length == 0) label = "第" + (col + 1).ToString(CultureInfo.InvariantCulture) + "列（原表未标注）";
                string group = "";
                foreach (GameDataTextRow row in rows.Take(header.Index).Where(r => !r.IsComment))
                {
                    string current = "";
                    for (int k = 0; k <= col && k < row.Cells.Count; k++) if (!String.IsNullOrWhiteSpace(row.Cells[k])) current = row.Cells[k].Trim();
                    if (current.Length > 0) group = current;
                }
                columnNames.Add(label);
                columnDescriptions.Add("原表字段：" + label + (group.Length > 0 ? Environment.NewLine + "原表分组：" + group : "")
                    + Environment.NewLine + "第" + (col + 1).ToString(CultureInfo.InvariantCulture) + "列。此说明来自安装版原表标题，不推测未确认的单位或公式。空值、末尾空列与其他行均保留。" );
            }
        }

        private static bool IsHeaderStart(string first)
        {
            string value = first.Trim();
            string[] known = { "道具类型", "索引名", "属性名称", "角色名称", "图片ID", "类型名", "前缀名", "前缀ID", "套装ID", "场景", "怪物名称",
                "装备类型", "属性索引ID", "装备串ID", "索引ID", "场景别名", "人物索引名", "打造技能", "类型号", "主技能索引名", "对应技能索引名",
                "ID", "id", "Alias", "alias", "Name", "name", "Type", "type" };
            return known.Contains(value, StringComparer.Ordinal);
        }

        private static string Hash(byte[] bytes)
        { using (var algorithm = SHA256.Create()) return BitConverter.ToString(algorithm.ComputeHash(bytes)).Replace("-", "").ToLowerInvariant(); }

        private static void WriteNewFile(string path, byte[] bytes)
        { using (var stream = new FileStream(IoPath(path), FileMode.CreateNew, FileAccess.Write, FileShare.None)) { stream.Write(bytes, 0, bytes.Length); stream.Flush(true); } }

        private static string NormalPath(string path)
        {
            if (path.StartsWith("\\\\?\\UNC\\", StringComparison.OrdinalIgnoreCase)) return "\\\\" + path.Substring(8);
            return path.StartsWith("\\\\?\\", StringComparison.Ordinal) ? path.Substring(4) : path;
        }

        private static string IoPath(string path)
        {
            string full = System.IO.Path.GetFullPath(NormalPath(path));
            return full.StartsWith("\\\\", StringComparison.Ordinal) ? "\\\\?\\UNC\\" + full.Substring(2) : "\\\\?\\" + full;
        }
    }
}
