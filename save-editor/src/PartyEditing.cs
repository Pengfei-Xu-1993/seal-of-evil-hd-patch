using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;

namespace SealSaveEditor
{
    public sealed class CompanionAvailability
    {
        public bool CanAdd {get;internal set;}
        public string Reason {get;internal set;}
        public int PartyCount {get;internal set;}
        public int AvailablePosition {get;internal set;}
    }
    public sealed class AddedCompanionToken
    {
        internal SaveDocument Document;
        internal SaveNode Root,Parent;
        internal List<SaveNode> Nodes;
        internal List<ItemRecord> Items;
        internal List<RecordInfo> InspectorEntries;
        internal List<SaveField> Locations;
        internal Dictionary<SaveField,bool> ActiveStates;
        internal List<int> NodePositions,ItemPositions,InspectorPositions,LocationPositions;
        internal int ChildPosition,CharacterPosition;
        public CharacterRecord Character {get;internal set;}
        public bool IsPresent {get;internal set;}
    }
    public sealed partial class SaveDocument
    {
        private readonly Dictionary<CharacterRecord,AddedCompanionToken> partyAdded=new Dictionary<CharacterRecord,AddedCompanionToken>();

        public CompanionAvailability GetCompanionAvailability(int classId)
        {
            CompanionDefinition definition=CompanionFactory.Find(classId);
            var result=new CompanionAvailability {PartyCount=Characters.Count,AvailablePosition=-1};
            if(userRoot==null||userRoot.Type!=0x1003||userRoot.Source!="USE"||Characters.Count==0||Characters[0].Node!=userRoot||userRoot.Extra.Count!=3)
            {result.Reason="This save has no verified primary-player party.";return result;}
            if(Characters.Any(c=>PartyIdentityMatches(c.Node,definition)))
            {result.Reason="This character is already in the party, including its current main-player form.";return result;}
            if(nodes.Any(n=>n.Source!="USE"&&(n.Type&0x1001)==0x1001&&PartyIdentityMatches(n,definition)))
            {result.Reason="A matching or suspicious world character already exists. Its story instance is preserved.";return result;}
            if(Characters.Count>=5){result.Reason="The original party has room for one main player and four companions.";return result;}
            if(PartyInt(userRoot,161)!=1){result.Reason="The main player's party position is not the verified original position 1.";return result;}
            var used=new HashSet<int>();
            foreach(CharacterRecord member in Characters.Skip(1))
            {int position=PartyInt(member.Node,161);if(position<2||position>5||!used.Add(position)){result.Reason="Existing companion positions are inconsistent. No party data was changed.";return result;}}
            for(int position=2;position<=5;position++)if(!used.Contains(position)){result.AvailablePosition=position;result.CanAdd=true;result.Reason="A native level-1 companion can be added without changing story progress.";return result;}
            result.Reason="All four original companion positions are occupied.";return result;
        }

        public CharacterRecord AddCompanion(CompanionInitialState initial)
        {
            if(initial==null||initial.Definition==null||initial.Root==null)throw new ArgumentException("A complete native companion plan is required.","initial");
            CompanionDefinition definition=CompanionFactory.Find(initial.Definition.ClassId);
            var identity=CompanionPayload.Header(initial.Root.Blob);
            if(initial.Definition!=definition||identity.Type!=0x1001||identity.ClassId!=definition.ClassId||identity.Path!=definition.NativePath||identity.Alias!=definition.Alias||identity.Name!=definition.EnglishName
                ||initial.Root.Groups.Count!=4||initial.Root.Groups[1].Count!=0)
                throw new InvalidDataException("The native companion plan does not match an original playable character.");
            CompanionAvailability available=GetCompanionAvailability(definition.ClassId);
            if(!available.CanAdd)throw new InvalidOperationException(available.Reason);
            int fieldStart=fields.Count,nodeStart=nodes.Count,characterStart=Characters.Count,itemStart=Items.Count,locationStart=Location.Count,inspectorStart=Inspector.Count;
            var created=new List<SaveNode>();SaveNode root=null;
            try
            {
                root=PartyCreateNode(initial.Root,created,true,available.AvailablePosition);
                if(created.Count!=initial.NodeCount)throw new InvalidDataException("The native companion tree length changed.");
                nodes.AddRange(created);AddParty(root);
                if(Characters.Count!=characterStart+1)throw new InvalidDataException("A new companion cannot contain another party.");
                var entries=created.Select(MakeItemInspector).ToList();Inspector.AddRange(entries);
                CharacterRecord character=Characters[characterStart];
                var token=new AddedCompanionToken {Document=this,Root=root,Parent=userRoot,Nodes=created,Character=character,
                    Items=new List<ItemRecord>(character.Items),InspectorEntries=entries,Locations=Location.Skip(locationStart).ToList(),
                    ChildPosition=userRoot.Extra[0].Count,CharacterPosition=characterStart,IsPresent=true};
                userRoot.Extra[0].Add(root);partyAdded.Add(character,token);RefreshSummary();return character;
            }
            catch
            {
                if(root!=null)userRoot.Extra[0].Remove(root);
                fields.RemoveRange(fieldStart,fields.Count-fieldStart);nodes.RemoveRange(nodeStart,nodes.Count-nodeStart);
                Characters.RemoveRange(characterStart,Characters.Count-characterStart);Items.RemoveRange(itemStart,Items.Count-itemStart);
                Location.RemoveRange(locationStart,Location.Count-locationStart);Inspector.RemoveRange(inspectorStart,Inspector.Count-inspectorStart);throw;
            }
        }

        public AddedCompanionToken CaptureAddedCompanion(CharacterRecord character)
        {AddedCompanionToken token;if(character==null||!partyAdded.TryGetValue(character,out token))throw new ArgumentException("Select a companion newly added by this document.","character");return token;}

        public void DetachAddedCompanion(AddedCompanionToken token)
        {
            PartyRequireToken(token);if(!token.IsPresent)throw new InvalidOperationException("The added companion is already removed.");
            if(!PartyWalk(token.Root).SequenceEqual(token.Nodes)||!token.Character.Items.SequenceEqual(token.Items))
                throw new InvalidOperationException("Undo later structural item or skill changes before removing this companion.");
            token.ChildPosition=userRoot.Extra[0].IndexOf(token.Root);token.CharacterPosition=Characters.IndexOf(token.Character);
            token.NodePositions=token.Nodes.Select(n=>nodes.IndexOf(n)).ToList();token.ItemPositions=token.Items.Select(i=>Items.IndexOf(i)).ToList();
            token.InspectorPositions=token.InspectorEntries.Select(i=>Inspector.IndexOf(i)).ToList();token.LocationPositions=token.Locations.Select(f=>Location.IndexOf(f)).ToList();
            if(token.ChildPosition<0||token.CharacterPosition<1||token.NodePositions.Any(i=>i<0)||token.ItemPositions.Any(i=>i<0)||token.InspectorPositions.Any(i=>i<0)||token.LocationPositions.Any(i=>i<0))
                throw new InvalidOperationException("The added companion's attachment is inconsistent.");
            var affected=new HashSet<SaveNode>(token.Nodes);token.ActiveStates=fields.Where(f=>affected.Contains(f.Node)).ToDictionary(f=>f,f=>f.Active);
            userRoot.Extra[0].RemoveAt(token.ChildPosition);Characters.Remove(token.Character);
            foreach(SaveNode node in token.Nodes)nodes.Remove(node);foreach(ItemRecord item in token.Items)Items.Remove(item);
            foreach(RecordInfo entry in token.InspectorEntries)Inspector.Remove(entry);foreach(SaveField field in token.Locations)Location.Remove(field);
            foreach(SaveField field in token.ActiveStates.Keys)field.Active=false;
            token.IsPresent=false;PartyReindex();RefreshSummary();
        }

        public void ReattachAddedCompanion(AddedCompanionToken token)
        {
            PartyRequireToken(token);if(token.IsPresent)throw new InvalidOperationException("The added companion is already present.");
            CompanionAvailability availability=GetCompanionAvailability(PartyInt(token.Root,1));
            if(!availability.CanAdd)throw new InvalidOperationException(availability.Reason);
            int position=PartyInt(token.Root,161);
            if(Characters.Skip(1).Any(c=>PartyInt(c.Node,161)==position))throw new InvalidOperationException("The original companion position is occupied. Undo the intervening party change first.");
            userRoot.Extra[0].Insert(Math.Min(token.ChildPosition,userRoot.Extra[0].Count),token.Root);
            Characters.Insert(Math.Min(token.CharacterPosition,Characters.Count),token.Character);
            PartyInsert(nodes,token.Nodes,token.NodePositions);PartyInsert(Items,token.Items,token.ItemPositions);
            PartyInsert(Inspector,token.InspectorEntries,token.InspectorPositions);PartyInsert(Location,token.Locations,token.LocationPositions);
            foreach(var pair in token.ActiveStates)pair.Key.Active=pair.Value;
            token.IsPresent=true;PartyReindex();RefreshSummary();
        }

        private SaveNode PartyCreateNode(CompanionNodePlan plan,List<SaveNode> created,bool root,int position)
        {
            byte[] blob=(byte[])plan.Blob.Clone();
            if(root)
            {
                CompanionPayload.WriteAttribute(blob,161,position);
                CompanionPayload.WriteAttribute(blob,103,PartyInt(userRoot,103));
                // Same-map placement only; no scene/world/quest state is moved.
                foreach(int id in new[]{97,98,99})CompanionPayload.WriteAttribute(blob,id,PartyInt(userRoot,id));
                CompanionPayload.WriteAttribute(blob,108,-1);CompanionPayload.WriteAttribute(blob,109,-1);
            }
            var node=new SaveNode {Blob=blob,Offset=-1,BlobOffset=-1,Source="USE",IsNewItem=true};ParseBase(node);created.Add(node);
            foreach(SaveField field in ItemStateFields(node))field.Offset=-1;
            foreach(ItemModifier modifier in node.Modifiers)modifier.OriginalIndex=-1;
            for(int g=0;g<plan.Groups.Count;g++)
            {
                var children=new List<SaveNode>();if(g>0)node.Extra.Add(children);
                foreach(CompanionNodePlan child in plan.Groups[g])children.Add(PartyCreateNode(child,created,false,position));
                if(g==0)node.Children.AddRange(children);
            }
            node.OriginalLength=WriteNode(node).Length;return node;
        }
        private bool PartyIdentityMatches(SaveNode node,CompanionDefinition definition)
        {
            if(PartyInt(node,1)==definition.ClassId)return true;
            if(String.Equals(node.Path,definition.NativePath,StringComparison.OrdinalIgnoreCase))return true;
            var aliases=new HashSet<string>(StringComparer.OrdinalIgnoreCase) {definition.Alias};
            if(definition.ClassId==1){aliases.Add("paladin");aliases.Add("1gainie");}
            if(definition.ClassId==2){aliases.Add("lanwei");}
            if(definition.ClassId==3){aliases.Add("priest");aliases.Add("chiying");aliases.Add("1yanhong");}
            if(definition.ClassId==4){aliases.Add("Blademan");aliases.Add("1caixin");}
            if(definition.ClassId==5){aliases.Add("beastman");aliases.Add("1lianghu");}
            if(aliases.Contains(node.Alias))return true;
            return node.Fields.Any(f=>f.Key=="string.OldAlias"&&aliases.Contains(f.Text));
        }
        private static int PartyInt(SaveNode node,int id)
        {SaveField field=node.Fields.SingleOrDefault(f=>f.Key=="attr."+id.ToString(CultureInfo.InvariantCulture)&&f.Modifier==null);if(field==null)throw new InvalidDataException("The party character's native attribute table is incomplete.");return checked((int)field.Value);}
        private static IEnumerable<SaveNode> PartyWalk(SaveNode node)
        {yield return node;foreach(SaveNode child in node.Children)foreach(SaveNode entry in PartyWalk(child))yield return entry;foreach(List<SaveNode> list in node.Extra)foreach(SaveNode child in list)foreach(SaveNode entry in PartyWalk(child))yield return entry;}
        private void PartyRequireToken(AddedCompanionToken token)
        {AddedCompanionToken actual;if(token==null||token.Document!=this||token.Parent!=userRoot||token.Character==null||!partyAdded.TryGetValue(token.Character,out actual)||actual!=token)throw new ArgumentException("The companion token belongs to a different document.","token");}
        private static void PartyInsert<T>(List<T> target,List<T> values,List<int> positions)
        {for(int i=0;i<values.Count;i++)target.Insert(Math.Min(positions[i],target.Count),values[i]);}
        private void PartyReindex()
        {for(int i=0;i<Characters.Count;i++)Characters[i].Index=i;ReindexItems();}
    }
}
