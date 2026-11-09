using System;
using System.IO;
using System.Collections.Generic;
using System.Runtime.InteropServices;

// Construct original companion subclasses in the save-free native worker only.
internal static class NativeCompanionSupport
{
    [DllImport("msvcrt.dll", CallingConvention=CallingConvention.Cdecl)] static extern void srand(uint seed);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate void VoidCall();
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate IntPtr Allocator();
    [UnmanagedFunctionPointer(CallingConvention.ThisCall)] delegate void ThisVoid(IntPtr self);
    [UnmanagedFunctionPointer(CallingConvention.ThisCall)] delegate void ThisInt(IntPtr self,int value);
    [UnmanagedFunctionPointer(CallingConvention.ThisCall)] delegate void ThisString(IntPtr self,IntPtr value);
    [UnmanagedFunctionPointer(CallingConvention.ThisCall)] delegate void FlagAction(IntPtr self,IntPtr name,int value);
    [UnmanagedFunctionPointer(CallingConvention.ThisCall)] delegate int Register(IntPtr manager,IntPtr self,int id);
    [UnmanagedFunctionPointer(CallingConvention.ThisCall)] delegate IntPtr Lookup(IntPtr manager,int id);
    [UnmanagedFunctionPointer(CallingConvention.ThisCall)] delegate int Size(IntPtr self);
    [UnmanagedFunctionPointer(CallingConvention.ThisCall)] delegate int Save(IntPtr self,IntPtr buffer,int capacity);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate void RoleAction(IntPtr self,int value);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate void ObjectAction(IntPtr self);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate int AddMember(IntPtr owner,IntPtr member,int notify);
    static IntPtr Engine { get { return NativeActorRuntime.Engine; } }
    static T F<T>(int rva){return P<T>(IntPtr.Add(Engine,rva));}
    static T P<T>(IntPtr function){return (T)(object)Marshal.GetDelegateForFunctionPointer(function,typeof(T));}
    static void Attribute(IntPtr obj,int id,int value){Marshal.WriteInt32(obj,0x30+id*4,value);}

    internal static void Run(BinaryReader input,BinaryWriter output)
    {
        int classId=input.ReadInt32(),seed=input.ReadInt32();
        int[] allocators={0,0x8B510,0xB31B0,0x830F0,0x85FD0,0x83090};
        int[] vtables={0,0x191F8C,0x1AD65C,0x18C218,0x18E9A0,0x18C1C8};
        if(classId<1||classId>5)throw new InvalidDataException("Select one of the five original playable characters.");
        // Expression/stat services, without a world, renderer or save.
        F<VoidCall>(0x27CB0)();F<VoidCall>(0x23FC0)();
        Marshal.WriteInt32(IntPtr.Add(Engine,0x3E780C),1);srand(unchecked((uint)seed));
        IntPtr actor=F<Allocator>(allocators[classId])();
        if(actor==IntPtr.Zero||Marshal.ReadIntPtr(actor)!=IntPtr.Add(Engine,vtables[classId]))
            throw new InvalidDataException("The original companion allocator returned an unexpected subclass.");
        F<Register>(0x4820)(IntPtr.Add(Engine,0x367798),actor,-1);
        Marshal.WriteIntPtr(IntPtr.Add(Engine,0x3E77FC),actor);
        IntPtr table=Marshal.ReadIntPtr(actor);
        P<ThisVoid>(Marshal.ReadIntPtr(table,0x0C))(actor);P<ThisInt>(Marshal.ReadIntPtr(table,0x20))(actor,0);
        if(Marshal.ReadInt32(actor,0x1C)!=0x1001||Marshal.ReadInt32(actor,0x34)!=classId)
            throw new InvalidDataException("The original companion identity did not initialize correctly.");
        // Original newobject tail 28A92/28A9B, then SetObject level initialization.
        F<RoleAction>(0x41450)(actor,classId);F<ObjectAction>(0x57EF0)(actor);F<RoleAction>(0x247E0)(actor,1);
        if(classId==2){IntPtr name=Marshal.StringToHGlobalAnsi("Lan Wei");try{F<ThisString>(0x1BF0)(actor,name);}finally{Marshal.FreeHGlobal(name);}}
        // Exercise AddMember against a temporary native player. Only its member
        // subtree is exported; the editor supplies the real owner and position.
        IntPtr owner=Marshal.AllocHGlobal(0x788);for(int i=0;i<0x788;i++)Marshal.WriteByte(owner,i,0);
        F<ThisVoid>(0x4FBA0)(owner);F<Register>(0x4820)(IntPtr.Add(Engine,0x367798),owner,-1);
        P<ThisVoid>(Marshal.ReadIntPtr(Marshal.ReadIntPtr(owner),0x0C))(owner);
        Attribute(owner,1,2);Attribute(owner,6,3);Attribute(owner,103,10001);
        Marshal.WriteIntPtr(IntPtr.Add(Engine,0x3E77FC),owner);
        if(F<AddMember>(0x31980)(owner,actor,1)!=1||Children(owner,0x4DC).Count!=1)
            throw new InvalidDataException("The native companion could not join its isolated temporary party.");
        IntPtr flag=Marshal.StringToHGlobalAnsi("jiaruduiwu");
        try{F<FlagAction>(0x4F700)(actor,flag,-1);F<FlagAction>(0x4F840)(actor,flag,1);}finally{Marshal.FreeHGlobal(flag);}
        // ER_LoadObjWithChild 092E5..09300/094E4..094FF restores selections from
        // each loaded skill's UI_Cur, using its new process-local handle.
        Attribute(actor,108,-1);Attribute(actor,109,-1);
        if(Marshal.ReadInt32(actor,0x30+12*4)<=0||Marshal.ReadInt32(actor,0x30+14*4)<=0)
            throw new InvalidDataException("The original companion resource defaults are invalid.");
        output.Write(classId);Node(output,actor,new HashSet<IntPtr>());
        // This process serves one request; every native object remains private.
    }
    static byte[] Blob(IntPtr obj)
    {
        IntPtr table=Marshal.ReadIntPtr(obj);int size=P<Size>(Marshal.ReadIntPtr(table,0x1C))(obj);
        if(size<1000||size>=0x4000)throw new InvalidDataException("Invalid native companion payload size.");
        IntPtr buffer=Marshal.AllocHGlobal(size);
        try{if(P<Save>(Marshal.ReadIntPtr(table,0x18))(obj,buffer,size)!=size)throw new InvalidDataException("Native companion serialization failed.");byte[] bytes=new byte[size];Marshal.Copy(buffer,bytes,0,size);return bytes;}
        finally{Marshal.FreeHGlobal(buffer);}
    }
    static List<IntPtr> Children(IntPtr obj,int offset)
    {
        IntPtr list=IntPtr.Add(obj,offset);int count=Marshal.ReadInt32(list,0x0C);
        if(count<0||count>2048)throw new InvalidDataException("Invalid native child count.");
        var result=new List<IntPtr>();var visited=new HashSet<IntPtr>();IntPtr next=Marshal.ReadIntPtr(list,0x10);
        for(int i=0;i<count;i++){if(next==IntPtr.Zero||!visited.Add(next))throw new InvalidDataException("Invalid native child list.");IntPtr child=F<Lookup>(0x49E0)(IntPtr.Add(Engine,0x367798),Marshal.ReadInt32(next,8));if(child==IntPtr.Zero)throw new InvalidDataException("Missing native child.");result.Add(child);next=Marshal.ReadIntPtr(next);}
        if(next!=IntPtr.Zero)throw new InvalidDataException("Unexpected native list trailer.");return result;
    }
    static void Node(BinaryWriter output,IntPtr obj,HashSet<IntPtr> visited)
    {
        if(!visited.Add(obj)||visited.Count>10000)throw new InvalidDataException("Duplicate or cyclic native companion tree.");
        byte[] blob=Blob(obj);output.Write(blob.Length);output.Write(blob);int type=Marshal.ReadInt32(obj,0x1C);
        var offsets=new List<int>();offsets.Add(0);
        if((type&0x1001)==0x1001){offsets.Add(0x4DC);offsets.Add(0x4F8);offsets.Add(0x514);}
        else if((type&0x10001)==0x10001&&(type&0x10003)!=0x10003&&(type&0x10005)!=0x10005)offsets.Add(0x470);
        output.Write(offsets.Count);
        foreach(int offset in offsets){List<IntPtr> children=Children(obj,offset);output.Write(children.Count);foreach(IntPtr child in children)Node(output,child,visited);}
    }
}
