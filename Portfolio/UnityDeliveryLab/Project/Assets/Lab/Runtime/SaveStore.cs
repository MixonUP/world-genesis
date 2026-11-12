using System;
using System.IO;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using UnityEngine;

namespace DeliveryLab
{
    public sealed class SaveStore
    {
        public string PrimaryPath { get; }
        public string BackupPath => PrimaryPath+".bak";
        readonly int limit;
        // JsonUtility accepts fractional integers and defaults omitted fields. Preflight the
        // fixed v1 integer tokens, then overwrite deliberately invalid required-field values.
        static readonly Regex NumericProperty = new Regex("\"(schemaVersion|stackLimit|coins|health|selectedSlot|revision|quantity)\"\\s*:", RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(250));
        static readonly Regex IntegerToken = new Regex(@"\G\s*(-?(?:0|[1-9][0-9]*))\s*(?=[,}])", RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(250));
        static readonly Regex Property = new Regex("\"([^\"]*)\"\\s*:", RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(250));
        static bool IntegerTokensValid(string json)
        {
            // Our v1 writer emits ASCII field names and fixed item IDs, never escapes or
            // extension properties. Reject them instead of accepting ambiguous aliases.
            if(json.IndexOf('\\')>=0) return false;
            int items=0,slots=0;
            foreach(Match property in Property.Matches(json)) {
                string key=property.Groups[1].Value;
                if(key=="item") items++; else if(key=="slots") slots++;
                else if(key!="schemaVersion" && key!="stackLimit" && key!="coins" && key!="health" && key!="selectedSlot" && key!="revision" && key!="quantity") return false;
            }
            if(items!=Inventory.Capacity || slots!=1) return false;
            var fields=NumericProperty.Matches(json);
            if(fields.Count!=6+Inventory.Capacity) return false;
            var counts=new int[6]; string[] names={"schemaVersion","stackLimit","coins","health","selectedSlot","revision"};
            foreach(Match field in fields) {
                var token=IntegerToken.Match(json,field.Index+field.Length);
                if(!token.Success || !int.TryParse(token.Groups[1].Value,NumberStyles.AllowLeadingSign,CultureInfo.InvariantCulture,out _)) return false;
                int rootIndex=Array.IndexOf(names,field.Groups[1].Value); if(rootIndex>=0) counts[rootIndex]++;
            }
            foreach(int count in counts) if(count!=1) return false;
            return true;
        }
        public SaveStore(string directory,int stackLimit) { PrimaryPath=Path.Combine(directory,"inventory-v1.json"); limit=stackLimit; }
        public bool Save(InventorySnapshot snapshot,out string message)
        {
            if(!Inventory.Validate(snapshot,limit,out message)) return false;
            string temp=PrimaryPath+".tmp";
            try {
                Directory.CreateDirectory(Path.GetDirectoryName(PrimaryPath));
                byte[] bytes=new UTF8Encoding(false).GetBytes(JsonUtility.ToJson(snapshot,true));
                using(var fs=new FileStream(temp,FileMode.Create,FileAccess.Write,FileShare.None)) { fs.Write(bytes,0,bytes.Length); fs.Flush(true); }
                if(File.Exists(PrimaryPath)) { bool validPrevious=Load(false,out _,out _); File.Replace(temp,PrimaryPath,validPrevious?BackupPath:null,true); } else File.Move(temp,PrimaryPath);
                message="Saved. State committed to disk."; return true;
            } catch(Exception ex) { message="Save failed: "+ex.GetType().Name+". Previous commit kept."; return false; }
            finally { try { if(File.Exists(temp)) File.Delete(temp); } catch { } }
        }
        public bool Load(bool backup,out InventorySnapshot candidate,out string message)
        {
            candidate=null; string path=backup?BackupPath:PrimaryPath;
            try {
                if(!File.Exists(path)) { message=backup?"No backup yet. Save twice to create one.":"No saved session yet."; return false; }
                if(new FileInfo(path).Length>1024*1024) { message="Save rejected: oversized file."; return false; }
                string json=File.ReadAllText(path);
                if(!IntegerTokensValid(json)) { message="Load rejected: missing, duplicate or non-integer values. Running state kept."; return false; }
                var parsed=new InventorySnapshot {schemaVersion=-1,stackLimit=-1,coins=-1,health=-1,selectedSlot=int.MinValue,revision=-1,slots=null};
                JsonUtility.FromJsonOverwrite(json,parsed);
                if(!Inventory.Validate(parsed,limit,out message)) { message="Load rejected: "+message+" Running state kept."; return false; }
                candidate=parsed; message=backup?"Backup recovered. Running state restored.":"Loaded. Saved state restored."; return true;
            } catch(Exception ex) { message="Load rejected: "+ex.GetType().Name+". Running state kept."; return false; }
        }
    }
    [Serializable] public sealed class LabConfig { public int stackLimit=10; public string revision="baseline-10"; }
}
