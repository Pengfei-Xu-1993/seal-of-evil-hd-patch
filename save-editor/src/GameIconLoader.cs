using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;

namespace SealSaveEditor
{
    /// <summary>Read-only original LIB/XBM icon decoder. It never loads a game DLL, renderer or game window.</summary>
    public sealed class GameIconLoader : IDisposable
    {
        private sealed class Bank { public string Path; public int Base, Span; }
        private sealed class Cached { public Bitmap Image; public long Used; public long Bytes; }
        private readonly List<Bank> banks = new List<Bank>();
        private readonly Dictionary<int, Cached> cache = new Dictionary<int, Cached>();
        private readonly object sync = new object();
        private long counter, cachedBytes;
        private bool disposed;
        public string LastError { get; private set; }
        public string GameDirectory { get; private set; }

        public GameIconLoader(string gameDirectory)
        {
            if (String.IsNullOrWhiteSpace(gameDirectory)) throw new ArgumentNullException("gameDirectory");
            GameDirectory = Path.GetFullPath(gameDirectory);
            string res = Path.Combine(GameDirectory, "res");
            byte[] raw = File.ReadAllBytes(Path.Combine(res, "Combination.ini"));
            if (raw.Length > 1024 * 1024) throw new InvalidDataException("The original resource-bank manifest is too large.");
            byte[] plain;
            if (raw.Length > 0 && raw[0] == 0x80) { plain = new byte[raw.Length - 1]; for (int i = 0; i < plain.Length; i++) plain[i] = (byte)(raw[i + 1] ^ 0x78); }
            else plain = raw;
            string text = Encoding.GetEncoding(936, EncoderFallback.ExceptionFallback, DecoderFallback.ExceptionFallback).GetString(plain);
            string name = null; int minimum = -1;
            foreach (string rawLine in text.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
            {
                int equal = rawLine.IndexOf('='); if (equal < 0) continue;
                string key = rawLine.Substring(0, equal).Trim(), value = rawLine.Substring(equal + 1).Trim();
                if (key == "标识") { name = value; minimum = -1; }
                else if (key == "最小值") { if (!Int32.TryParse(value, out minimum)) minimum = -1; }
                else if (key == "最大值")
                {
                    int span;
                    if (String.IsNullOrEmpty(name) || !name.All(c => c >= 'A' && c <= 'Z' || c >= 'a' && c <= 'z' || c >= '0' && c <= '9' || c == '_')
                        || minimum < 0 || !Int32.TryParse(value, out span) || span < 1 || span > 1048576) continue;
                    string library = Path.Combine(res, name + ".Lib");
                    if (File.Exists(library)) banks.Add(new Bank { Path = library, Base = minimum, Span = span });
                    name = null; minimum = -1;
                }
            }
            if (banks.Count == 0) throw new InvalidDataException("No supported original resource banks were found.");
        }

        /// <summary>Returns a caller-owned clone at original resolution, or null when the resource is unknown/unsupported.</summary>
        public Bitmap GetIcon(int resourceId)
        {
            lock (sync)
            {
                if (disposed) throw new ObjectDisposedException("GameIconLoader");
                Cached saved;
                if (cache.TryGetValue(resourceId, out saved)) { saved.Used = ++counter; return saved.Image == null ? null : (Bitmap)saved.Image.Clone(); }
                Bitmap image = null; LastError = null;
                try { image = DecodeResource(resourceId, new HashSet<int>(), 0); }
                catch (IOException e) { LastError = e.Message; }
                catch (UnauthorizedAccessException e) { LastError = e.Message; }
                catch (ArgumentException e) { LastError = e.Message; }
                catch (ExternalException e) { LastError = e.Message; }
                long bytes = image == null ? 0 : (long)image.Width * image.Height * 4;
                while (cache.Count >= 512 || cache.Count > 0 && cachedBytes + bytes > 32L * 1024 * 1024)
                {
                    int oldest = cache.OrderBy(kv => kv.Value.Used).First().Key; Cached evicted = cache[oldest];
                    if (evicted.Image != null) evicted.Image.Dispose(); cachedBytes -= evicted.Bytes; cache.Remove(oldest);
                }
                cache.Add(resourceId, new Cached { Image = image, Used = ++counter, Bytes = bytes }); cachedBytes += bytes;
                return image == null ? null : (Bitmap)image.Clone();
            }
        }

        public bool TryGetIcon(int resourceId, out Bitmap image) { image = GetIcon(resourceId); return image != null; }

        private Bitmap DecodeResource(int id, HashSet<int> visited, int depth)
        {
            if (id < 0 || depth > 8 || !visited.Add(id)) return null;
            Bank bank = banks.FirstOrDefault(b => id >= b.Base && (long)id < (long)b.Base + b.Span);
            if (bank == null) return null;
            int index = id - bank.Base; byte[] data;
            using (var file = new FileStream(bank.Path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
            using (var reader = new BinaryReader(file))
            {
                if (file.Length < 256) return null;
                byte[] header = reader.ReadBytes(20);
                if (Encoding.ASCII.GetString(header, 0, 8) != "Lib 1.01") return null;
                int count = BitConverter.ToInt32(header, 16);
                if (count < 0 || count > bank.Span || index >= count || 256L + count * 8L > file.Length) return null;
                file.Position = 256L + index * 8L; int offset = reader.ReadInt32(), size = reader.ReadInt32();
                if (offset < 256L + count * 8L || size < 4 || size > 16 * 1024 * 1024 || offset > file.Length - size) return null;
                file.Position = offset; data = reader.ReadBytes(size);
                if (data.Length != size) return null;
            }
            if (data.Length >= 64 && data[0] == 'x' && data[1] == 'b' && data[2] == 'm' && data[3] == 0) return DecodeBitmap(data);
            // Verified native UI/item wrappers: first static state, or first frame of an animated icon.
            if (data.Length >= 44 && BitConverter.ToInt32(data, 0) > 0 && BitConverter.ToInt32(data, 0) <= 256
                && BitConverter.ToInt32(data, 4) > 0 && BitConverter.ToInt32(data, 4) <= 4096
                && BitConverter.ToInt32(data, 8) == 4 && BitConverter.ToInt32(data, 12) == 7
                && BitConverter.ToInt32(data, 20) == 4)
                return DecodeResource(BitConverter.ToInt32(data, 24), visited, depth + 1);
            return null;
        }

        private static Bitmap DecodeBitmap(byte[] data)
        {
            int width = BitConverter.ToInt32(data, 20), height = BitConverter.ToInt32(data, 24);
            int bpp = data[28], compression = data[30];
            if (width < 1 || height < 1 || width > 2048 || height > 2048 || bpp != 16 && bpp != 15 || compression != 1
                || 64L + height * 4L > data.Length) return null;
            byte[] pixels = new byte[checked(width * height * 4)]; ushort colorKey = BitConverter.ToUInt16(data, 32);
            for (int y = 0; y < height; y++)
            {
                int relative = BitConverter.ToInt32(data, 64 + y * 4);
                int next = y + 1 == height ? data.Length - 64 : BitConverter.ToInt32(data, 68 + y * 4);
                if (relative < height * 4 || next < relative || (long)next + 64 > data.Length) return null;
                int cursor = 64 + relative, end = 64 + next, xBytes = 0;
                while (cursor < end)
                {
                    // A row ends with its final transparent-skip word, without a following copy word.
                    if (end - cursor == 2)
                    {
                        int trailingSkip = BitConverter.ToUInt16(data, cursor);
                        if ((trailingSkip & 1) != 0 || trailingSkip > width * 2 - xBytes) return null;
                        cursor = end; break;
                    }
                    if (end - cursor < 4) return null;
                    int skip = BitConverter.ToUInt16(data, cursor), copy = BitConverter.ToUInt16(data, cursor + 2); cursor += 4;
                    if ((skip & 1) != 0 || (copy & 1) != 0 || skip > width * 2 - xBytes || copy > width * 2 - xBytes - skip || copy > end - cursor) return null;
                    xBytes += skip;
                    for (int i = 0; i < copy; i += 2)
                    {
                        ushort value = BitConverter.ToUInt16(data, cursor + i);
                        if (value == colorKey) continue;
                        int target = (y * width + (xBytes + i) / 2) * 4;
                        pixels[target] = (byte)((value & 31) * 255 / 31);
                        pixels[target + 1] = (byte)(((value >> 5) & (bpp == 16 ? 63 : 31)) * 255 / (bpp == 16 ? 63 : 31));
                        pixels[target + 2] = (byte)(((value >> (bpp == 16 ? 11 : 10)) & 31) * 255 / 31);
                        pixels[target + 3] = 255;
                    }
                    cursor += copy; xBytes += copy;
                }
            }
            var bitmap = new Bitmap(width, height, PixelFormat.Format32bppArgb);
            try
            {
                BitmapData locked = bitmap.LockBits(new Rectangle(0, 0, width, height), ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);
                try
                {
                    if (locked.Stride == width * 4) Marshal.Copy(pixels, 0, locked.Scan0, pixels.Length);
                    else for (int y = 0; y < height; y++) Marshal.Copy(pixels, y * width * 4, IntPtr.Add(locked.Scan0, y * locked.Stride), width * 4);
                }
                finally { bitmap.UnlockBits(locked); }
                return bitmap;
            }
            catch { bitmap.Dispose(); throw; }
        }

        public void Dispose()
        {
            lock (sync)
            {
                if (disposed) return; disposed = true;
                foreach (Cached entry in cache.Values) if (entry.Image != null) entry.Image.Dispose();
                cache.Clear(); cachedBytes = 0;
            }
        }
    }
}
