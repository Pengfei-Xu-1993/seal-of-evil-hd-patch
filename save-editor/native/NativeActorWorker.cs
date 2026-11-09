using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;

// Own x86 worker. The original engine is loaded only in this short-lived,
// save-free process. No world, renderer, or running game is accessed.
internal static class NativeActorWorker
{
    [DllImport("kernel32.dll")] private static extern uint SetErrorMode(uint mode);
    private static int Main(string[] args)
    {
        SetErrorMode(0x8003);
        try
        {
            if (IntPtr.Size != 4 || args.Length != 1) throw new InvalidDataException("The native actor worker requires x86 and one request file.");
            string request = Path.GetFullPath(args[0]), scratch = Path.GetDirectoryName(request);
            if (!File.Exists(Path.Combine(scratch, ".seal-actor-worker")) || Directory.Exists(Path.Combine(scratch, "System")))
                throw new InvalidDataException("An owned, save-free actor worker directory is required.");
            var info = new FileInfo(request);
            if (info.Length < 16 || info.Length > 4 * 1024 * 1024) throw new InvalidDataException("Invalid actor request size.");
            if (!Directory.Exists(Path.Combine(scratch, "Data")) || !File.Exists(Path.Combine(scratch, "Dlg", "Name.dlg")))
                throw new InvalidDataException("The copied native catalog is incomplete.");
            Directory.SetCurrentDirectory(scratch);
            using (var input = new BinaryReader(File.OpenRead(request), Encoding.UTF8))
            {
                if (input.ReadInt32() != 0x51415753 || input.ReadInt32() != 1) throw new InvalidDataException("Unrecognized actor request.");
                byte mode = input.ReadByte();
                if (mode != 1 && mode != 2) throw new InvalidDataException("Unsupported actor operation.");
                NativeActorRuntime.Initialize(input.ReadString());
                string partial = Path.Combine(scratch, "response.partial");
                using (var output = new BinaryWriter(File.Create(partial), Encoding.UTF8))
                {
                    output.Write(0x52415753); output.Write(1); output.Write(mode);
                    if (mode == 1) NativeSkillOperations.Run(input, output);
                    else NativeCompanionSupport.Run(input, output);
                    if (input.BaseStream.Position != input.BaseStream.Length) throw new InvalidDataException("Unexpected actor request trailer.");
                }
                File.Move(partial, Path.Combine(scratch, "response.bin"));
            }
            return 0;
        }
        catch (Exception error)
        {
            Console.Error.WriteLine(error.GetType().Name + ": " + error.Message);
            return 2;
        }
    }
}

internal static class NativeActorRuntime
{
    internal const string EngineSha256 = "B22AD4D0630D39B4E376EEF3EFDDFE7EE363823AE0CB15AE41DE6BEFB2E8126F";
    public static IntPtr Engine { get; private set; }
    public static readonly Encoding Gbk = Encoding.GetEncoding(936, EncoderFallback.ExceptionFallback, DecoderFallback.ExceptionFallback);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)] private static extern IntPtr LoadLibrary(string path);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] public delegate void VoidCall();
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] public delegate IntPtr AllocateCall(int length);
    [UnmanagedFunctionPointer(CallingConvention.ThisCall)] public delegate void ThisVoidCall(IntPtr self);
    [UnmanagedFunctionPointer(CallingConvention.ThisCall)] public delegate IntPtr ConstructorCall(IntPtr self);
    [UnmanagedFunctionPointer(CallingConvention.ThisCall)] public delegate int RegisterCall(IntPtr manager, IntPtr obj, int identity);
    [UnmanagedFunctionPointer(CallingConvention.ThisCall)] public delegate int SizeCall(IntPtr self);
    [UnmanagedFunctionPointer(CallingConvention.ThisCall)] public delegate int SaveCall(IntPtr self, IntPtr buffer, int length);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] public delegate IntPtr IteratorCall(IntPtr owner, ref IntPtr cursor);
    public static T Function<T>(int rva) { return Pointer<T>(IntPtr.Add(Engine, rva)); }
    public static T Pointer<T>(IntPtr address) { return (T)(object)Marshal.GetDelegateForFunctionPointer(address, typeof(T)); }

    public static void Initialize(string path)
    {
        path = Path.GetFullPath(path);
        if (!String.Equals(Path.GetFileName(path), "ERRelive.dll", StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("Select the installed original engine.");
        using (var a = SHA256.Create()) using (var f = File.OpenRead(path))
            if (BitConverter.ToString(a.ComputeHash(f)).Replace("-", "") != EngineSha256) throw new InvalidDataException("Unverified original engine build.");
        Engine = LoadLibrary(path);
        if (Engine == IntPtr.Zero) throw new InvalidOperationException("The original engine could not be loaded (" + Marshal.GetLastWin32Error() + ").");
        Function<ThisVoidCall>(0x170600)(IntPtr.Add(Engine, 0x4DCF98));
        Function<VoidCall>(0x3330)(); Function<VoidCall>(0x6A3B0)(); Function<VoidCall>(0x82160)();
        if (Marshal.ReadInt32(IntPtr.Add(Engine, 0x46A640)) < 657 || Marshal.ReadInt32(IntPtr.Add(Engine, 0x4D66C8)) < 243)
            throw new InvalidDataException("The original catalog did not initialize completely.");
        // Suppress ordinary UI/network notifications, in this worker only.
        Marshal.WriteInt32(IntPtr.Add(Engine, 0x3E780C), 1);
    }

    public static IntPtr Text(string value)
    {
        if (String.IsNullOrEmpty(value) || value.IndexOf('\0') >= 0 || Gbk.GetByteCount(value) > 255) throw new InvalidDataException("Invalid native alias.");
        byte[] bytes = Gbk.GetBytes(value + "\0"); IntPtr result = Marshal.AllocHGlobal(bytes.Length); Marshal.Copy(bytes, 0, result, bytes.Length); return result;
    }
    public static string ReadString(IntPtr pointer)
    {
        if (pointer == IntPtr.Zero) return "";
        var result = new List<byte>();
        for (int i = 0; i < 65536; i++) { byte b = Marshal.ReadByte(pointer, i); if (b == 0) return Gbk.GetString(result.ToArray()); result.Add(b); }
        throw new InvalidDataException("Native string is not terminated.");
    }
    public static string StringAttribute(IntPtr obj, string key)
    {
        int count = Marshal.ReadInt32(obj, 0x430); IntPtr node = Marshal.ReadIntPtr(obj, 0x434);
        if (count < 0 || count > 4096) throw new InvalidDataException("Invalid native string attribute count.");
        var seen = new HashSet<IntPtr>();
        for (int i = 0; i < count; i++)
        {
            if (node == IntPtr.Zero || !seen.Add(node)) throw new InvalidDataException("Invalid native string attribute list.");
            if (ReadString(Marshal.ReadIntPtr(node, 8)) == key) return ReadString(Marshal.ReadIntPtr(node, 12));
            node = Marshal.ReadIntPtr(node);
        }
        return "";
    }
    public static int Attribute(IntPtr obj, int id) { return Marshal.ReadInt32(obj, 0x30 + id * 4); }
    public static void Attribute(IntPtr obj, int id, int value) { Marshal.WriteInt32(obj, 0x30 + id * 4, value); }
    public static void RegisterObject(IntPtr obj)
    {
        int identity = Function<RegisterCall>(0x4820)(IntPtr.Add(Engine, 0x367798), obj, -1);
        if ((identity & 0xFFFFFF) >= 0x20000) throw new InvalidDataException("The isolated native object could not be registered.");
    }
    public static IntPtr AllocatePerson()
    {
        IntPtr person = Function<AllocateCall>(0x170330)(0x728);
        if (person == IntPtr.Zero) throw new OutOfMemoryException("The isolated native actor could not be allocated.");
        for (int i = 0; i < 0x728; i++) Marshal.WriteByte(person, i, 0);
        Function<ConstructorCall>(0x4E0B0)(person); Function<ThisVoidCall>(0x4E560)(person); RegisterObject(person);
        Marshal.WriteIntPtr(IntPtr.Add(Engine, 0x3E77FC), person);
        return person;
    }
    public static List<IntPtr> Enumerate(IntPtr owner, int firstRva, int nextRva, int maximum)
    {
        IntPtr cursor = IntPtr.Zero, obj = Function<IteratorCall>(firstRva)(owner, ref cursor);
        var result = new List<IntPtr>(); var seen = new HashSet<IntPtr>();
        while (obj != IntPtr.Zero)
        {
            if (result.Count >= maximum || !seen.Add(obj)) throw new InvalidDataException("Invalid native object list.");
            result.Add(obj); obj = Function<IteratorCall>(nextRva)(owner, ref cursor);
        }
        return result;
    }
    public static byte[] SerializeObject(IntPtr obj)
    {
        IntPtr vt = Marshal.ReadIntPtr(obj);
        int size = Pointer<SizeCall>(Marshal.ReadIntPtr(vt, 0x1C))(obj);
        if (size < 32 || size >= 0x4000) throw new InvalidDataException("Invalid native serialized object length.");
        IntPtr buffer = Marshal.AllocHGlobal(size);
        try
        {
            if (Pointer<SaveCall>(Marshal.ReadIntPtr(vt, 0x18))(obj, buffer, size) != size) throw new InvalidDataException("Native serialized object length mismatch.");
            byte[] bytes = new byte[size]; Marshal.Copy(buffer, bytes, 0, size); return bytes;
        }
        finally { Marshal.FreeHGlobal(buffer); }
    }
    public static void WritePayload(BinaryWriter output, IntPtr obj)
    { byte[] bytes = SerializeObject(obj); output.Write(bytes.Length); output.Write(bytes); }
}

internal static class NativeSkillOperations
{
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate IntPtr LearnCall(IntPtr actor, IntPtr alias, int level);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate IntPtr EnhanceCall(IntPtr actor, IntPtr alias);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate IntPtr FindCall(IntPtr actor, IntPtr alias);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int UpgradeCall(IntPtr actor, IntPtr alias, int delta, int messageFlag);
    private sealed class Root { public string Alias; public int Maximum; }

    private static int Count(BinaryReader input, int min, int max)
    { int n = input.ReadInt32(); if (n < min || n > max) throw new InvalidDataException("Invalid native skill request count."); return n; }
    private static bool ClassAlias(string alias, int classId)
    {
        string[] prefixes = { "", "游侠_", "巫师_", "祭司_", "刀客_", "兽人_" };
        return alias.StartsWith(prefixes[classId], StringComparison.Ordinal) || classId == 2 && alias.StartsWith("打造_", StringComparison.Ordinal);
    }

    public static void Run(BinaryReader input, BinaryWriter output)
    {
        int job = input.ReadInt32(); bool maximum = input.ReadBoolean();
        if (job < 1 || job > 5 || Count(input, 256, 256) != 256) throw new InvalidDataException("Invalid actor class/snapshot.");
        int[] snapshot = new int[256]; for (int i = 0; i < snapshot.Length; i++) snapshot[i] = input.ReadInt32();
        if (snapshot[1] != job) throw new InvalidDataException("Actor class snapshot mismatch.");
        for (int i = 19; i <= 23; i++) if (snapshot[i] < 0 || snapshot[i] > 1000000) throw new InvalidDataException("Actor element values are outside the verified calculation range.");
        var roots = new List<Root>(); var rootAliases = new HashSet<string>(StringComparer.Ordinal);
        int count = Count(input, 1, 64);
        for (int i = 0; i < count; i++)
        {
            var root = new Root { Alias = input.ReadString(), Maximum = input.ReadInt32() };
            if (!ClassAlias(root.Alias, job) || root.Maximum < 1 || root.Maximum > 9 || !rootAliases.Add(root.Alias)) throw new InvalidDataException("Invalid root skill identity/maximum.");
            roots.Add(root);
        }
        var enhancements = new List<string>(); var enhancementAliases = new HashSet<string>(StringComparer.Ordinal);
        count = Count(input, 0, 1024);
        for (int i = 0; i < count; i++) { string a = input.ReadString(); if (!ClassAlias(a, job) || !enhancementAliases.Add(a)) throw new InvalidDataException("Invalid enhancement identity."); enhancements.Add(a); }
        var lookup = new Dictionary<string, string>(StringComparer.Ordinal); count = Count(input, roots.Count, 256);
        for (int i = 0; i < count; i++)
        {
            string a = input.ReadString(), parent = input.ReadString();
            if (!ClassAlias(a, job) || parent.Length == 0 && !rootAliases.Contains(a) || parent.Length > 0 && !rootAliases.Contains(parent) || lookup.ContainsKey(a))
                throw new InvalidDataException("Invalid associated skill identity.");
            lookup.Add(a, parent);
        }
        IntPtr actor = NativeActorRuntime.AllocatePerson();
        NativeActorRuntime.Attribute(actor, 1, job);
        // The bulk command explicitly learns/maxes skills without spending role
        // levels or books. Actor level 200 satisfies the original upgrade gate;
        // selected current elements still control enhancement activation.
        NativeActorRuntime.Attribute(actor, 2, 200);
        for (int i = 19; i <= 23; i++) { NativeActorRuntime.Attribute(actor, i, snapshot[i]); NativeActorRuntime.Attribute(actor, i + 56, snapshot[i]); }
        foreach (Root root in roots)
        {
            IntPtr alias = NativeActorRuntime.Text(root.Alias);
            try
            {
                IntPtr skill = NativeActorRuntime.Function<LearnCall>(0x45C40)(actor, alias, 1);
                if (skill == IntPtr.Zero || NativeActorRuntime.Attribute(skill, 173) != root.Maximum) throw new InvalidDataException("Native root creation/maximum mismatch: " + root.Alias);
            }
            finally { Marshal.FreeHGlobal(alias); }
        }
        foreach (string a in enhancements)
        {
            IntPtr alias = NativeActorRuntime.Text(a);
            try { if (NativeActorRuntime.Function<EnhanceCall>(0x45DE0)(actor, alias) == IntPtr.Zero) throw new InvalidDataException("Native enhancement creation failed: " + a); }
            finally { Marshal.FreeHGlobal(alias); }
        }
        if (maximum)
            foreach (Root root in roots)
            {
                IntPtr alias = NativeActorRuntime.Text(root.Alias);
                try
                {
                    // Native propagation increments associated forms by one.
                    // A single large delta leaves their levels behind.
                    for (int level = 1; level < root.Maximum; level++)
                        if (NativeActorRuntime.Function<UpgradeCall>(0x46190)(actor, alias, 1, 0) == 0) throw new InvalidDataException("Native skill upgrade failed: " + root.Alias);
                }
                finally { Marshal.FreeHGlobal(alias); }
            }
        List<IntPtr> actual = NativeActorRuntime.Enumerate(actor, 0x2F970, 0x2F9A0, 256);
        if (actual.Count != lookup.Count) throw new InvalidDataException("Native skill count differs from the verified requested class tree.");
        output.Write(job); output.Write(maximum); output.Write(lookup.Count);
        foreach (var row in lookup)
        {
            IntPtr alias = NativeActorRuntime.Text(row.Key), skill;
            try { skill = NativeActorRuntime.Function<FindCall>(0x2F8C0)(actor, alias); }
            finally { Marshal.FreeHGlobal(alias); }
            if (skill == IntPtr.Zero || NativeActorRuntime.StringAttribute(skill, "SkillIndex") != row.Value) throw new InvalidDataException("Native associated skill binding mismatch: " + row.Key);
            int level = NativeActorRuntime.Attribute(skill, 2), cap = NativeActorRuntime.Attribute(skill, 173);
            Root main = roots.Find(r => r.Alias == (row.Value.Length == 0 ? row.Key : row.Value));
            if (level != (maximum ? main.Maximum : 1) || cap != (row.Value.Length == 0 ? main.Maximum : 9)) throw new InvalidDataException("Native skill/associated level mismatch.");
            output.Write(row.Key); output.Write(row.Value); output.Write(level); output.Write(cap);
            // Runtime object-manager handles cannot be transplanted into a save.
            // The native loader binds the preserved string SkillIndex by alias.
            NativeActorRuntime.Attribute(skill, 232, -1);
            NativeActorRuntime.WritePayload(output, skill);
            List<IntPtr> components = NativeActorRuntime.Enumerate(skill, 0x30A20, 0x30200, 256);
            output.Write(components.Count);
            foreach (IntPtr component in components)
            {
                if (NativeActorRuntime.Attribute(component, 2) != -1 || NativeActorRuntime.Attribute(component, 173) != -1
                    || NativeActorRuntime.StringAttribute(component, "SkillIndex") != row.Key) throw new InvalidDataException("Invalid native enhancement ownership or placeholder level.");
                output.Write(NativeActorRuntime.ReadString(Marshal.ReadIntPtr(component, 0x2C)));
                NativeActorRuntime.Attribute(component, 232, -1);
                NativeActorRuntime.WritePayload(output, component);
            }
        }
    }
}
