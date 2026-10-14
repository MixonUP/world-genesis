using System.Security.Cryptography;
using System.Text.Json;
using WorldGenesis.Simulation;
using WorldGenesis.Tests;
internal static class NativeExport
{
 internal static int Run()
 {
  var json=new JsonSerializerOptions{IncludeFields=true};
  var e=ConquestScenarios.NativeShowcase();var w=e.World;var target=w.Settlements[1];var files=new List<object>();bool marching=false,occupying=false,occupied=false;
  string directory="Builds/ConquestShowcase";Directory.CreateDirectory(directory);
  var sourceBefore=Directory.GetFiles("WorldGenesis/Assets/_Project/Scripts/Simulation","*.cs",SearchOption.AllDirectories).OrderBy(x=>x).ToDictionary(p=>p,Hash);
  void Save(string name)
  {
   if(!w.Validate(out string error))throw new InvalidOperationException(error);
   string path=Path.Combine(directory,name+".save.json");File.WriteAllText(path,JsonSerializer.Serialize(new{Schema=1,Format="WorldGenesis",World=w},json));
   var clone=new SimulationEngine(JsonSerializer.Deserialize<WorldState>(JsonSerializer.Serialize(w,json),json));if(JsonSerializer.Serialize(clone.World,json)!=JsonSerializer.Serialize(w,json))throw new InvalidOperationException("Constructor changed export "+name);
   files.Add(new{name,path=Path.GetFullPath(path),sha256=Hash(path),bytes=new FileInfo(path).Length,tick=w.Tick,simulatedSeconds=w.SimulatedSeconds,homeId=w.Settlements[0].Id,targetId=target.Id,targetX=target.X,targetY=target.Y,target.SovereignSettlementId,target.OccupationSeconds,target.ConquestCount,population=w.Units.Count,warriors=w.Units.Where(u=>u.WarWeapon!=ItemType.None).Select(u=>new{u.Id,u.X,u.Y,u.SettlementId,u.WarWeapon,u.WarReturning,u.WarTargetSettlementId,u.InsideBuildingId}).ToArray()});
  }
  Save("start");
  for(int i=0;i<1800&&!occupied;i++)
  {
   e.Step(.1f);if(i%10==0&&!w.Validate(out string error))throw new InvalidOperationException(error);
   if(!marching&&w.Units.Count(u=>u.WarWeapon!=ItemType.None&&u.X>25&&u.X<43)>=3){Save("marching");marching=true;}
   if(!occupying&&target.OccupationSeconds>=5&&target.ConquestCount==0){Save("occupation");occupying=true;}
   if(target.ConquestCount==1){Save("occupied");occupied=true;}
  }
  bool unchanged=sourceBefore.All(p=>Hash(p.Key)==p.Value);bool pass=marching&&occupying&&occupied&&unchanged;
  File.WriteAllText("Evidence/Simulation/conquest-native-showcase.json",JsonSerializer.Serialize(new{utc=DateTime.UtcNow,pass,sourceUnchanged=unchanged,assistance=new[]{"Controlled flat64x64 map; ordinary Spawn creates14Human+8Orc residents","All design technologies granted to attacking town; actual6spears placed in Smithy","Aging/Births/FireSpread and automatic weather disabled; normal health/needs/movement remain","Defenders initially positioned away from Hall, remain alive; other civilian DecisionTimer10000; town expansion disabled","Public SetDiplomacy and TryStartWarParty; thereafter only ordinary Step(.1f)","No direct SovereignSettlementId assignment, no teleport or equipment grant after start"},sourceSHA256=sourceBefore,files},new JsonSerializerOptions{WriteIndented=true}));
  Console.WriteLine("Native conquest showcase "+pass+" "+files.Count+" saves, tick "+w.Tick);return pass?0:1;
 }
 static string Hash(string path)=>Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)));
}
