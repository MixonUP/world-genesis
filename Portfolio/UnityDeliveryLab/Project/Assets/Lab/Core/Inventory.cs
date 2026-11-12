using System;

namespace DeliveryLab
{
    [Serializable] public sealed class Slot { public string item; public int quantity = -1; public Slot Copy() => new Slot { item = item, quantity = quantity }; }
    [Serializable] public sealed class InventorySnapshot
    {
        public int schemaVersion = 1, stackLimit, coins = 125, health = 64, selectedSlot = -1, revision;
        public Slot[] slots;
        public InventorySnapshot Copy() { var c = (InventorySnapshot)MemberwiseClone(); if (slots != null) { c.slots = new Slot[slots.Length]; for (int i=0;i<slots.Length;i++) c.slots[i] = slots[i]?.Copy(); } return c; }
    }
    public sealed class Inventory
    {
        public const int Capacity = 12;
        public static readonly string[] Items = { "tonic", "shard", "key", "chip" };
        public int StackLimit { get; }
        InventorySnapshot state;
        public InventorySnapshot Snapshot => state.Copy();
        public Inventory(int stackLimit) { if (stackLimit<1 || stackLimit>999) throw new ArgumentOutOfRangeException(nameof(stackLimit)); StackLimit=stackLimit; Reset(); }
        public void Reset() { state = new InventorySnapshot { stackLimit=StackLimit, slots=new Slot[Capacity] }; for(int i=0;i<Capacity;i++) state.slots[i]=new Slot { item="", quantity=0 }; }
        public static bool Known(string id) => Array.IndexOf(Items,id)>=0;
        public static bool Validate(InventorySnapshot s,int limit,out string error)
        {
            error="";
            if(s==null || s.schemaVersion!=1) error="Unsupported or missing save schema.";
            else if(s.slots==null || s.slots.Length!=Capacity) error="Save must contain exactly 12 slots.";
            else if(s.stackLimit<1 || s.stackLimit>999 || s.coins<0 || s.coins>1000000 || s.health<0 || s.health>100 || s.revision<0 || s.selectedSlot < -1 || s.selectedSlot>=Capacity) error="Invalid state values.";
            else for(int i=0;i<Capacity;i++) { var v=s.slots[i]; if(v==null || v.item==null || v.quantity<0 || v.quantity>limit || (v.quantity==0 && v.item!="") || (v.quantity>0 && !Known(v.item))) { error="Invalid slot "+i+"."; break; } }
            return error.Length==0;
        }
        public bool Restore(InventorySnapshot candidate,out string error) { if(!Validate(candidate,StackLimit,out error)) return false; state=candidate.Copy(); state.stackLimit=StackLimit; return true; }
        // Revision is diagnostic, not a transaction ID. Saturation keeps any valid imported state saveable.
        void MarkChanged() { if(state.revision<int.MaxValue) state.revision++; }
        public bool Select(int slot) { if(slot<0 || slot>=Capacity) return false; state.selectedSlot=slot; MarkChanged(); return true; }
        public bool Add(string item,int amount,out string message)
        {
            message=""; if(!Known(item) || amount<=0 || amount>9999) { message="Rejected: item and positive quantity required."; return false; }
            int available=0; foreach(var slot in state.slots) if(slot.quantity==0) available+=StackLimit; else if(slot.item==item) available+=StackLimit-slot.quantity;
            if(available<amount) { message="Inventory full. Nothing was changed."; return false; }
            int left=amount;
            for(int pass=0;pass<2;pass++) for(int i=0;i<Capacity && left>0;i++) { var s=state.slots[i]; if((pass==0 && s.quantity>0 && s.item==item)||(pass==1 && s.quantity==0)) { int add=Math.Min(left,StackLimit-s.quantity); s.item=item; s.quantity+=add; left-=add; if(state.selectedSlot<0) state.selectedSlot=i; } }
            MarkChanged(); message="Added "+amount+" "+item+"."; return true;
        }
        public bool Remove(int index,int amount,out string message)
        {
            message="Nothing to remove.";
            if(index<0 || index>=Capacity || amount<=0 || state.slots[index].quantity<amount) return false;
            var s=state.slots[index]; s.quantity-=amount; if(s.quantity==0) s.item=""; MarkChanged(); message="Removed "+amount+" item."; return true;
        }
        public bool Use(int index,out string message)
        {
            message="Select an item first."; if(index<0 || index>=Capacity || state.slots[index].quantity==0) return false;
            string id=state.slots[index].item;
            if(id=="tonic" && state.health==100) { message="Health is full. Tonic was kept."; return false; }
            int reward=id=="shard"?5:id=="key"?10:id=="chip"?20:0;
            if(state.coins+reward>1000000) { message="Coin limit reached. Item was kept."; return false; }
            if(id=="tonic") state.health=Math.Min(100,state.health+20); else state.coins+=reward;
            Remove(index,1,out _); message=id=="tonic"?"Tonic used. Health restored.":"Item exchanged for "+reward+" coins."; return true;
        }
    }
}
