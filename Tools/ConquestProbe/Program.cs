using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using WorldGenesis.Simulation;
using WorldGenesis.Tests;
if(args.Length>0&&args[0]=="--export")return NativeExport.Run();
var json=new JsonSerializerOptions{IncludeFields=true};ConquestScenarios.Clone=w=>JsonSerializer.Deserialize<WorldState>(JsonSerializer.Serialize(w,json),json);ConquestScenarios.Snapshot=w=>JsonSerializer.Serialize(w,json);
Dictionary<string,string> Sources()=>Directory.GetFiles("WorldGenesis/Assets/_Project/Scripts/Simulation","*.cs",SearchOption.AllDirectories).OrderBy(x=>x).ToDictionary(p=>p.Replace('\\','/'),p=>Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(p))));
var before=Sources();var results=new List<object>();int failed=0;
foreach(var m in typeof(ConquestScenarios).GetMethods(BindingFlags.Public|BindingFlags.Static)){if(m.ReturnType!=typeof(void)||m.GetParameters().Length!=0)continue;try{m.Invoke(null,null);results.Add(new{name=m.Name,passed=true});Console.WriteLine("PASS "+m.Name);}catch(Exception ex){failed++;results.Add(new{name=m.Name,passed=false,error=(ex.InnerException??ex).ToString()});Console.WriteLine("FAIL "+m.Name+" "+(ex.InnerException??ex));}}
var after=Sources();bool unchanged=before.Count==after.Count&&before.All(p=>after.TryGetValue(p.Key,out var value)&&value==p.Value);var path=args.Length>0?args[0]:"Evidence/Simulation/conquest-probe.json";Directory.CreateDirectory(Path.GetDirectoryName(path));File.WriteAllText(path,JsonSerializer.Serialize(new{utc=DateTime.UtcNow,tests=results.Count,failures=failed,sourceUnchanged=unchanged,sourceSHA256=before,results},new JsonSerializerOptions{WriteIndented=true}));return failed==0&&unchanged?0:1;


