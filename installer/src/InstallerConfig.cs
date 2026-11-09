using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace SealHdInstaller
{
    internal static class InstallerConfig
    {
        // Latin-1 maps every byte to one character, preserving unknown legacy text exactly.
        private static readonly Encoding ByteEncoding = Encoding.GetEncoding(28591);

        internal static byte[] PatchIni(byte[] current, byte[] defaults, InstallOptions options)
        {
            bool unicode = current != null && current.Length >= 2 && current[0] == 255 && current[1] == 254;
            Encoding encoding = unicode ? Encoding.Unicode : ByteEncoding;
            string source = current == null ? ByteEncoding.GetString(defaults) : encoding.GetString(current);
            string byteOrderMark = "";
            if (!unicode && source.StartsWith("\u00ef\u00bb\u00bf", StringComparison.Ordinal)) { byteOrderMark = source.Substring(0, 3); source = source.Substring(3); }
            string newline = source.Contains("\r\n") ? "\r\n" : "\n";
            var lines = source.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None).ToList();
            string section = "";
            foreach (string line in ByteEncoding.GetString(defaults).TrimStart('\u00ef', '\u00bb', '\u00bf').Split(new[] { "\r\n", "\n" }, StringSplitOptions.None))
            {
                var s = Regex.Match(line, @"^\s*\[([^\]]+)\]\s*$");
                if (s.Success) { section = s.Groups[1].Value; continue; }
                var key = Regex.Match(line, @"^\s*([^;#=\s][^=]*?)\s*=(.*)$");
                if (key.Success) Set(lines, section, key.Groups[1].Value.Trim(), key.Groups[2].Value, false);
            }
            Set(lines, "Compatibility", "ForceWindowed", "1", true);
            Set(lines, "Compatibility", "VirtualFullscreen", "1", true);
            Set(lines, "Display", "Width", options.Width.ToString(System.Globalization.CultureInfo.InvariantCulture), true);
            Set(lines, "Display", "Height", options.Height.ToString(System.Globalization.CultureInfo.InvariantCulture), true);
            int profile = options.Width == 1920 ? 2 : options.Width == 2560 ? 3 : 4;
            Set(lines, "Display", "ProfileIndex", profile.ToString(), true);
            Set(lines, "Display", "Borderless", options.Borderless ? "1" : "0", true);
            Set(lines, "Assets", "MainMenu", "1", true);
            return encoding.GetBytes(byteOrderMark + String.Join(newline, lines));
        }

        private static void Set(List<string> lines, string section, string key, string value, bool replace)
        {
            int start = -1, end = lines.Count, found = -1;
            string active = "";
            for (int i = 0; i < lines.Count; ++i)
            {
                var header = Regex.Match(lines[i].TrimStart('\uFEFF'), @"^\s*\[([^\]]+)\]\s*$");
                if (header.Success)
                {
                    active = header.Groups[1].Value.Trim();
                    if (String.Equals(active, section, StringComparison.OrdinalIgnoreCase))
                    {
                        if (start >= 0) throw new InvalidDataException("Duplicate configuration section: " + section);
                        start = i;
                    }
                    else if (start >= 0 && end == lines.Count) end = i;
                }
                if (!String.Equals(active, section, StringComparison.OrdinalIgnoreCase)) continue;
                var item = Regex.Match(lines[i], @"^(\s*" + Regex.Escape(key) + @"\s*=)(.*)$", RegexOptions.IgnoreCase);
                if (!item.Success) continue;
                if (found >= 0) throw new InvalidDataException("Duplicate configuration key: " + section + "/" + key);
                found = i;
            }
            if (found >= 0)
            {
                if (replace) lines[found] = Regex.Replace(lines[found], @"^(\s*[^=]+\s*=).*$", m => m.Groups[1].Value + value);
                return;
            }
            if (start < 0)
            {
                if (lines.Count > 0 && lines[lines.Count - 1].Length > 0) lines.Add("");
                lines.Add("[" + section + "]"); lines.Add(key + "=" + value);
            }
            else lines.Insert(end, key + "=" + value);
        }

        internal static byte[] PatchGameConfig(byte[] original)
        {
            byte[] decoded = (byte[])original.Clone();
            for (int i = 0; i < decoded.Length; ++i) decoded[i] ^= 0x78;
            string key = ByteEncoding.GetString(Encoding.GetEncoding(936).GetBytes("显示模式="));
            MatchCollection matches = Regex.Matches(ByteEncoding.GetString(decoded), "^" + Regex.Escape(key) + @"(?<mode>[012])(?=\r?$)", RegexOptions.Multiline);
            if (matches.Count != 1) throw new InvalidDataException("The game display-mode setting was not recognized. No game files were changed.");
            byte[] result = (byte[])original.Clone();
            result[matches[0].Groups["mode"].Index] = (byte)('2' ^ 0x78);
            return result;
        }
    }
}
