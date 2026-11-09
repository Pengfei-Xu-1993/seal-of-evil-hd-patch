using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;

// Separate x86 process: the editor never loads the original engine into its own process.
// No original game binary or catalog is included in this program.
internal static class NativeItemWorker
{
    private const string EngineSha256 = "B22AD4D0630D39B4E376EEF3EFDDFE7EE363823AE0CB15AE41DE6BEFB2E8126F";
    private const uint RequestMagic = 0x51454953; // SIEQ
    private const uint ResponseMagic = 0x52454953; // SIER
    private static readonly Encoding Gbk = Encoding.GetEncoding(936, EncoderFallback.ExceptionFallback, DecoderFallback.ExceptionFallback);
    private static IntPtr engine;

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)] private static extern IntPtr LoadLibrary(string path);
    [DllImport("kernel32.dll")] private static extern uint SetErrorMode(uint mode);
    [DllImport("msvcrt.dll", CallingConvention = CallingConvention.Cdecl)] private static extern void srand(uint seed);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate void VoidCall();
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate void FactoryCall(IntPtr item, int minimumLevel, int maximumLevel, int type, IntPtr alias);
    [UnmanagedFunctionPointer(CallingConvention.ThisCall)] private delegate IntPtr ConstructorCall(IntPtr item);
    [UnmanagedFunctionPointer(CallingConvention.ThisCall)] private delegate void ThisVoidCall(IntPtr item);
    [UnmanagedFunctionPointer(CallingConvention.ThisCall)] private delegate void ThisStringCall(IntPtr item, IntPtr value);

    private static T Function<T>(int rva)
    { return (T)(object)Marshal.GetDelegateForFunctionPointer(IntPtr.Add(engine, rva), typeof(T)); }

    private sealed class Request
    {
        public string Alias;
        public int Level;
        public int Seed;
    }

    private static int Main(string[] args)
    {
        SetErrorMode(0x0001 | 0x0002 | 0x8000); // No critical-error, crash or missing-file windows.
        try
        {
            if (IntPtr.Size != 4 || args.Length != 1) throw new InvalidOperationException("The native catalog worker requires x86 and one request file.");
            string requestPath = Path.GetFullPath(args[0]);
            string scratch = Path.GetDirectoryName(requestPath);
            if (!File.Exists(Path.Combine(scratch, ".seal-item-worker"))) throw new InvalidDataException("The request is not in an owned worker directory.");
            string enginePath;
            var requests = new List<Request>();
            using (var input = new BinaryReader(File.OpenRead(requestPath), Encoding.UTF8))
            {
                if (input.ReadUInt32() != RequestMagic || input.ReadInt32() != 1) throw new InvalidDataException("Unrecognized worker request.");
                enginePath = Path.GetFullPath(input.ReadString());
                int count = input.ReadInt32();
                if (count < 1 || count > 4096) throw new InvalidDataException("Invalid generation request count.");
                for (int i = 0; i < count; i++)
                {
                    var request = new Request { Alias = input.ReadString(), Level = input.ReadInt32(), Seed = input.ReadInt32() };
                    if (String.IsNullOrWhiteSpace(request.Alias) || request.Alias.IndexOf('\0') >= 0 || Gbk.GetByteCount(request.Alias) > 255)
                        throw new InvalidDataException("Invalid original item alias.");
                    // The original experience_item table has 200 progression entries.
                    if (request.Level < 1 || request.Level > 200) throw new InvalidDataException("Generation level must be 1..200.");
                    requests.Add(request);
                }
                if (input.BaseStream.Position != input.BaseStream.Length) throw new InvalidDataException("Unexpected request trailer.");
            }
            if (!String.Equals(Path.GetFileName(enginePath), "ERRelive.dll", StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("Select the installed original engine.");
            string hash;
            using (var algorithm = SHA256.Create()) using (var file = File.OpenRead(enginePath))
                hash = BitConverter.ToString(algorithm.ComputeHash(file)).Replace("-", "");
            if (!String.Equals(hash, EngineSha256, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("This engine build has not been verified for native item generation.");
            if (!Directory.Exists(Path.Combine(scratch, "Data")) || !File.Exists(Path.Combine(scratch, "Dlg", "Name.dlg")))
                throw new InvalidDataException("The copied original catalog is incomplete.");
            if (Directory.Exists(Path.Combine(scratch, "System"))) throw new InvalidDataException("Worker directories must not contain saves or a System directory.");
            Directory.SetCurrentDirectory(scratch);
            engine = LoadLibrary(enginePath);
            if (engine == IntPtr.Zero) throw new InvalidOperationException("Could not load the installed original engine (" + Marshal.GetLastWin32Error() + ").");
            // These are the exact three initialization steps required by the pure data factory.
            // ER_Init additionally initializes the game world and is deliberately not called.
            Function<ThisVoidCall>(0x170600)(IntPtr.Add(engine, 0x4DCF98)); // Native allocation pools.
            Function<VoidCall>(0x3330)(); // Native field-name registration.
            Function<VoidCall>(0x6A3B0)(); // Original Data/*.txt and Dlg/Name.dlg readers.
            if (Marshal.ReadInt32(IntPtr.Add(engine, 0x46A640)) < 657 || Marshal.ReadInt32(IntPtr.Add(engine, 0x4D66C8)) < 243)
                throw new InvalidDataException("The native catalog did not initialize completely.");

            string temporary = Path.Combine(scratch, "response.partial");
            using (var output = new BinaryWriter(File.Create(temporary), Encoding.UTF8))
            {
                output.Write(ResponseMagic); output.Write(1); output.Write(requests.Count);
                foreach (Request request in requests) Generate(request, output);
            }
            File.Move(temporary, Path.Combine(scratch, "response.bin"));
            return 0;
        }
        catch (Exception error)
        {
            // Do not rely on native crash recovery. The parent enforces a timeout and checks exit/output.
            Console.Error.WriteLine(error.GetType().Name + ": " + error.Message);
            return 2;
        }
    }

    private static void Generate(Request request, BinaryWriter output)
    {
        IntPtr item = Marshal.AllocHGlobal(0x470), alias = IntPtr.Zero;
        bool initialized = false;
        try
        {
            for (int i = 0; i < 0x470; i++) Marshal.WriteByte(item, i, 0);
            Function<ConstructorCall>(0x4DA80)(item);
            initialized = true;
            Function<ThisVoidCall>(0x4DB10)(item);
            byte[] bytes = Gbk.GetBytes(request.Alias + "\0");
            alias = Marshal.AllocHGlobal(bytes.Length); Marshal.Copy(bytes, 0, alias, bytes.Length);
            Function<ThisStringCall>(0x1C00)(item, alias);
            srand(unchecked((uint)request.Seed));
            // An exact alias bypasses the random template selector. Level is an explicit min=max.
            Function<FactoryCall>(0x6D490)(item, request.Level, request.Level, -1, alias);
            int[] values = new int[256]; Marshal.Copy(IntPtr.Add(item, 0x30), values, 0, values.Length);
            string actualAlias = ReadString(Marshal.ReadIntPtr(item, 0x2C));
            string name = ReadString(Marshal.ReadIntPtr(item, 0x28));
            if (!String.Equals(actualAlias, request.Alias, StringComparison.Ordinal) || values[7] < 1 || String.IsNullOrWhiteSpace(name))
                throw new InvalidDataException("The native factory could not generate " + request.Alias + ".");
            output.Write(actualAlias); output.Write(name); output.Write(ReadString(Marshal.ReadIntPtr(item, 0x24)));
            output.Write(request.Level); output.Write(request.Seed);
            output.Write(256); foreach (int value in values) output.Write(value);
            int stringCount = Marshal.ReadInt32(item, 0x430);
            if (stringCount < 0 || stringCount > 4096) throw new InvalidDataException("Invalid native string count.");
            output.Write(stringCount);
            IntPtr node = Marshal.ReadIntPtr(item, 0x434);
            var stringNodes = new HashSet<IntPtr>();
            for (int i = 0; i < stringCount; i++)
            {
                if (node == IntPtr.Zero || !stringNodes.Add(node)) throw new InvalidDataException("Invalid native string list.");
                output.Write(ReadString(Marshal.ReadIntPtr(node, 8))); output.Write(ReadString(Marshal.ReadIntPtr(node, 12)));
                node = Marshal.ReadIntPtr(node);
            }
            if (node != IntPtr.Zero) throw new InvalidDataException("Native string list has an unexpected trailer.");
            output.Write(Marshal.ReadInt32(item, 0x46C));
            int modifierCount = Marshal.ReadInt32(item, 0x458);
            if (modifierCount < 0 || modifierCount > 256) throw new InvalidDataException("Invalid native modifier count.");
            output.Write(modifierCount);
            node = Marshal.ReadIntPtr(item, 0x45C);
            var modifierNodes = new HashSet<IntPtr>();
            int[] offsets = { 0x04, 0x08, 0x14, 0x18, 0x1C, 0x20, 0x24, 0x28, 0x2C, 0x30, 0x34, 0x38 };
            for (int i = 0; i < modifierCount; i++)
            {
                if (node == IntPtr.Zero || !modifierNodes.Add(node)) throw new InvalidDataException("Invalid native modifier list.");
                IntPtr modifier = Marshal.ReadIntPtr(node, 8);
                if (modifier == IntPtr.Zero) throw new InvalidDataException("Empty native modifier.");
                output.Write(52);
                foreach (int offset in offsets) output.Write(Marshal.ReadInt32(modifier, offset));
                node = Marshal.ReadIntPtr(node);
            }
            if (node != IntPtr.Zero) throw new InvalidDataException("Native modifier list has an unexpected trailer.");
        }
        finally
        {
            if (initialized) Function<ThisVoidCall>(0x4DB00)(item);
            if (alias != IntPtr.Zero) Marshal.FreeHGlobal(alias);
            Marshal.FreeHGlobal(item);
        }
    }

    private static string ReadString(IntPtr pointer)
    {
        if (pointer == IntPtr.Zero) return "";
        var bytes = new List<byte>();
        for (int i = 0; i <= 4096; i++)
        {
            byte value = Marshal.ReadByte(pointer, i);
            if (value == 0) return Gbk.GetString(bytes.ToArray());
            bytes.Add(value);
        }
        throw new InvalidDataException("A native string exceeds the save format limit.");
    }
}
