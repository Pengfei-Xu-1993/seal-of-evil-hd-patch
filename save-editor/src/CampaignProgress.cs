using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;

namespace SealSaveEditor
{
    public sealed class CampaignProgressState
    {
        public int RawGameCount { get; internal set; }
        public int CompletedPlaythroughs { get; internal set; }
        public bool SecondPlaythroughEnabled { get; internal set; }
        public string PrimaryCharacterName { get; internal set; }
        public int Fame { get; internal set; }
        public bool HasYouhuozhijie { get; internal set; }
        public bool FlagsLayoutKnown { get; internal set; }
        public int? ShouwangwujingFlag { get; internal set; }
        public int? JianfangzhongFlag { get; internal set; }
    }

    public sealed class CampaignProgressSnapshot
    {
        internal SaveDocument Document;
        internal SaveNode Root;
        internal SaveField Field;
        internal int RawValue;
        internal bool Authorized;
    }

    /// <summary>Campaign eligibility for the primary player; it never resets story or scene state.</summary>
    public static class CampaignProgress
    {
        private static readonly Encoding GameEncoding = Encoding.GetEncoding(936,
            EncoderFallback.ExceptionFallback, DecoderFallback.ExceptionFallback);

        public static CampaignProgressState Read(SaveDocument document)
        {
            SaveField count = RequireField(document);
            SaveNode root = count.Node;
            Dictionary<string, int> flags;
            bool flagsKnown = TryReadFlags(root, out flags);
            return new CampaignProgressState {
                RawGameCount = (int)count.Value,
                // Native 0x100421C0 returns zero for a negative stored count.
                CompletedPlaythroughs = Math.Max(0, (int)count.Value),
                SecondPlaythroughEnabled = count.Value > 0,
                PrimaryCharacterName = document.Characters[0].Name,
                Fame = (int)root.Fields.Single(f => f.Key == "attr.5").Value,
                HasYouhuozhijie = document.Characters.SelectMany(c => c.Items)
                    .Any(i => String.Equals(i.Alias, "youhuozhijie", StringComparison.OrdinalIgnoreCase)
                        && i.Fields.Any(f => f.Name == "UI_Pos" && f.Kind == "Int32" && f.Value < 1000)),
                FlagsLayoutKnown = flagsKnown,
                ShouwangwujingFlag = ReadFlag(flagsKnown, flags, "shouwangwujing"),
                JianfangzhongFlag = ReadFlag(flagsKnown, flags, "jianfangzhong")
            };
        }

        public static CampaignProgressSnapshot Capture(SaveDocument document)
        {
            SaveField field = RequireField(document);
            return new CampaignProgressSnapshot { Document = document, Root = field.Node,
                Field = field, RawValue = (int)field.Value, Authorized = field.ControlledCampaignWrite };
        }

        public static bool EnableSecondPlaythrough(SaveDocument document)
        {
            SaveField field = RequireField(document);
            if (field.Value > 0) return false;
            // The native hidden-equipment branches test GameCount > 0. A value of
            // one meets that run-count condition without changing current progress.
            // This does not assert completion of the other quest/ending conditions.
            document.SetPrimaryCampaignGameCount(field, 1, true);
            return true;
        }

        public static void Restore(SaveDocument document, CampaignProgressSnapshot snapshot)
        {
            SaveField field = RequireField(document);
            if (snapshot == null || snapshot.Document != document || snapshot.Root != field.Node || snapshot.Field != field)
                throw new ArgumentException("The campaign snapshot belongs to a different document or primary player.", "snapshot");
            document.SetPrimaryCampaignGameCount(field, snapshot.RawValue, snapshot.Authorized);
        }

        /// <summary>Known missing flags have native value -1; an unknown tail layout returns false.</summary>
        public static bool TryGetPlayerFlag(SaveDocument document, string name, out int value)
        {
            if (String.IsNullOrEmpty(name)) throw new ArgumentException("A flag name is required.", "name");
            SaveField field = RequireField(document);
            Dictionary<string, int> flags;
            if (!TryReadFlags(field.Node, out flags)) { value = 0; return false; }
            if (!flags.TryGetValue(name, out value)) value = -1;
            return true;
        }

        private static SaveField RequireField(SaveDocument document)
        {
            if (document == null) throw new ArgumentNullException("document");
            return document.GetPrimaryCampaignGameCountField();
        }

        private static int? ReadFlag(bool known, Dictionary<string, int> flags, string name)
        {
            if (!known) return null;
            int value;
            // CERPerson::GetFlag at 0x1004F880 returns -1 for a missing name.
            return flags.TryGetValue(name, out value) ? value : -1;
        }

        private static bool TryReadFlags(SaveNode node, out Dictionary<string, int> flags)
        {
            flags = new Dictionary<string, int>(StringComparer.Ordinal);
            if (node.Type != 0x1003 || node.BaseSize < 0 || node.BaseSize >= node.Blob.Length) return false;
            int at = node.BaseSize;
            try
            {
                // Native 0x1004EC70/0x1004EE73: count, CAttrInt name/value
                // records, then tail-local byte length. Flags remain read only.
                int count = ReadInteger(node.Blob, ref at);
                if (count < 0 || count > 4096) return false;
                for (int i = 0; i < count; i++)
                {
                    int length = ReadInteger(node.Blob, ref at);
                    if (length < 1 || length > 4096 || at > node.Blob.Length - length) return false;
                    byte[] text = new byte[length];
                    for (int p = 0; p < length; p++) text[p] = (byte)(node.Blob[at + p] ^ (byte)(length - p - 0x57));
                    at += length;
                    if (text[length - 1] != 0 || Array.IndexOf(text, (byte)0) != length - 1) return false;
                    string name = GameEncoding.GetString(text, 0, length - 1);
                    if (name.Length == 0 || flags.ContainsKey(name)) return false;
                    flags.Add(name, ReadInteger(node.Blob, ref at));
                }
                int tailLength = ReadInteger(node.Blob, ref at);
                return at == node.Blob.Length && tailLength == node.Blob.Length - node.BaseSize;
            }
            catch (InvalidDataException) { return false; }
            catch (DecoderFallbackException) { return false; }
        }

        private static int ReadInteger(byte[] bytes, ref int at)
        {
            if (at < 0 || at > bytes.Length - 4) throw new InvalidDataException("Incomplete campaign flag data.");
            byte[] value = new byte[4];
            for (int i = 0; i < 4; i++) value[i] = (byte)(bytes[at + i] ^ (byte)(4 - i - 0x57));
            at += 4;
            return BitConverter.ToInt32(value, 0);
        }
    }
}
