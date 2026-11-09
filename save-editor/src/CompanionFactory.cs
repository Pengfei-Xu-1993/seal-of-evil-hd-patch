using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;

namespace SealSaveEditor
{
    public sealed class CompanionDefinition
    {
        public int ClassId { get; internal set; }
        public string EnglishName { get; internal set; }
        public string ChineseName { get; internal set; }
        public string NativePath { get; internal set; }
        public string Alias { get; internal set; }
        public override string ToString() { return EnglishName; }
    }
    public sealed class CompanionInitialState
    {
        internal CompanionNodePlan Root;
        public CompanionDefinition Definition { get; internal set; }
        public int Seed { get; internal set; }
        public int NodeCount { get; internal set; }
        public string GenerationSummary { get; internal set; }
    }
    internal sealed class CompanionNodePlan
    {
        internal byte[] Blob;
        internal List<List<CompanionNodePlan>> Groups=new List<List<CompanionNodePlan>>();
    }
    public static class CompanionFactory
    {
        private static readonly IList<CompanionDefinition> definitions=new List<CompanionDefinition> {
            new CompanionDefinition {ClassId=2,EnglishName="Lan Wei",ChineseName="蓝薇",Alias="wizard",NativePath="复活/主角/巫师蓝薇"},
            new CompanionDefinition {ClassId=5,EnglishName="Liang Hu",ChineseName="梁虎",Alias="lianghu",NativePath="复活/落霞村/NPC/伙伴梁虎"},
            new CompanionDefinition {ClassId=3,EnglishName="Yan Hong",ChineseName="岩洪",Alias="yanhong",NativePath="复活/落霞村/NPC/伙伴岩洪"},
            new CompanionDefinition {ClassId=4,EnglishName="Cai Xin",ChineseName="彩欣",Alias="caixin",NativePath="复活/古道/NPC/伙伴彩欣"},
            new CompanionDefinition {ClassId=1,EnglishName="Gai Nie",ChineseName="盖聂",Alias="gainie",NativePath="复活/榆次/NPC/伙伴盖聂"}
        }.AsReadOnly();
        public static IList<CompanionDefinition> Characters {get{return definitions;}}
        internal static CompanionDefinition Find(int classId)
        { CompanionDefinition result=definitions.SingleOrDefault(d=>d.ClassId==classId);if(result==null)throw new ArgumentOutOfRangeException("classId","Select one of the five original playable characters.");return result; }
        public static CompanionInitialState Create(string gameDirectory,int classId,int seed)
        {
            CompanionDefinition definition=Find(classId);int count=0;
            var state=new CompanionInitialState {Definition=definition,Seed=seed};
            ActorWorkerClient.Run(gameDirectory,2,delegate(BinaryWriter input){input.Write(classId);input.Write(seed);},delegate(BinaryReader output){
                if(output.ReadInt32()!=classId)throw new InvalidDataException("The companion response belongs to a different character.");
                state.Root=ReadNode(output,0,ref count);
            });
            var identity=CompanionPayload.Header(state.Root.Blob);
            if(identity.Type!=0x1001||identity.ClassId!=classId||identity.Alias!=definition.Alias||identity.Path!=definition.NativePath||identity.Name!=definition.EnglishName
                ||state.Root.Groups.Count!=4||state.Root.Groups[1].Count!=0)
                throw new InvalidDataException("The native companion has an unexpected identity or attached party.");
            int[] expected={0,37,31,25,50,18};if(count!=expected[classId])throw new InvalidDataException("The native companion's original item/skill tree is incomplete.");
            state.NodeCount=count;state.GenerationSummary=definition.EnglishName+": native level 1; "+state.Root.Groups[0].Count.ToString(CultureInfo.InvariantCulture)
                +" original items; "+state.Root.Groups[2].Count.ToString(CultureInfo.InvariantCulture)+" skill roots; "+state.Root.Groups[3].Count.ToString(CultureInfo.InvariantCulture)
                +" additional actor records; "+count.ToString(CultureInfo.InvariantCulture)+" total entities. Story progress is preserved; future story recruitment can still affect the party.";
            return state;
        }
        private static CompanionNodePlan ReadNode(BinaryReader input,int depth,ref int count)
        {
            if(depth>100||++count>10000)throw new InvalidDataException("The native companion tree is too large.");
            var node=new CompanionNodePlan {Blob=ActorWorkerClient.ReadPayload(input)};var identity=CompanionPayload.Header(node.Blob);
            int expected=1+((identity.Type&0x1001)==0x1001?3:((identity.Type&0x10001)==0x10001&&(identity.Type&0x10003)!=0x10003&&(identity.Type&0x10005)!=0x10005?1:0));
            if(input.ReadInt32()!=expected)throw new InvalidDataException("Unexpected native attachment layout.");
            for(int g=0;g<expected;g++){int size=input.ReadInt32();if(size<0||size>2048)throw new InvalidDataException("Invalid native attachment count.");var children=new List<CompanionNodePlan>();node.Groups.Add(children);for(int i=0;i<size;i++)children.Add(ReadNode(input,depth+1,ref count));}
            return node;
        }
    }
    internal sealed class CompanionPayload
    {
        internal string Path,Name,Alias;
        internal int Type,ClassId,AttributesOffset;
        private static readonly Encoding Gbk=Encoding.GetEncoding(936,EncoderFallback.ExceptionFallback,DecoderFallback.ExceptionFallback);
        internal static CompanionPayload Header(byte[] blob)
        {
            if(blob==null||blob.Length<32||blob.Length>=0x4000)throw new InvalidDataException("Invalid companion payload.");
            int at=0,baseSize=ReadInt(blob,ref at);if(baseSize<32||baseSize>blob.Length)throw new InvalidDataException("Invalid companion base size.");
            var result=new CompanionPayload();result.Path=ReadString(blob,ref at);result.Type=ReadInt(blob,ref at);result.Name=ReadString(blob,ref at);result.Alias=ReadString(blob,ref at);
            if(ReadInt(blob,ref at)!=30||ReadInt(blob,ref at)!=256||at+1024>baseSize)throw new InvalidDataException("Unrecognized companion attribute table.");
            result.AttributesOffset=at;int valueAt=at+4;result.ClassId=ReadInt(blob,ref valueAt);return result;
        }
        internal static int ReadInt(byte[] bytes,ref int at)
        {if(at<0||at+4>bytes.Length)throw new InvalidDataException("Truncated companion integer.");byte[] raw=new byte[4];for(int i=0;i<4;i++)raw[i]=(byte)(bytes[at+i]^((4-i-0x57)&255));at+=4;return BitConverter.ToInt32(raw,0);}
        private static string ReadString(byte[] bytes,ref int at)
        {int length=ReadInt(bytes,ref at);if(length<0||length>16384||at+length>bytes.Length)throw new InvalidDataException("Truncated companion string.");byte[] raw=new byte[length];for(int i=0;i<length;i++)raw[i]=(byte)(bytes[at+i]^((length-i-0x57)&255));at+=length;if(length==0)return "";if(raw[length-1]!=0)throw new InvalidDataException("Unterminated companion string.");return Gbk.GetString(raw,0,length-1);}
        internal static void WriteAttribute(byte[] blob,int id,int value)
        {int at=Header(blob).AttributesOffset+id*4;byte[] bytes=BitConverter.GetBytes(value);for(int i=0;i<4;i++)blob[at+i]=(byte)(bytes[i]^((4-i-0x57)&255));}
    }
}
