using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text.Json;
using WorldGenesis.Simulation;
using WorldGenesis.Tests;
internal static class Program
{
    private static int Main(string[] args)
    {
        var json=new JsonSerializerOptions{IncludeFields=true};TransportBoundaryScenarios.Clone=w=>JsonSerializer.Deserialize<WorldState>(JsonSerializer.Serialize(w,json),json);TransportBoundaryScenarios.Snapshot=w=>JsonSerializer.Serialize(w,json);
        var results=new List<object>();int failed=0;
        foreach(MethodInfo m in typeof(TransportBoundaryScenarios).GetMethods(BindingFlags.Public|BindingFlags.Static))
        {if(m.ReturnType!=typeof(void)||m.GetParameters().Length!=0)continue;try{m.Invoke(null,null);results.Add(new{name=m.Name,passed=true});Console.WriteLine("PASS "+m.Name);}catch(Exception e){failed++;string error=(e.InnerException??e).ToString();results.Add(new{name=m.Name,passed=false,error});Console.WriteLine("FAIL "+m.Name+": "+error);}}
        string path=args.Length>0?args[0]:"Evidence/Simulation/transport-boundary-probe.json";Directory.CreateDirectory(Path.GetDirectoryName(path));File.WriteAllText(path,JsonSerializer.Serialize(new{utc=DateTime.UtcNow,tests=results.Count,failures=failed,results},new JsonSerializerOptions{WriteIndented=true}));
        Console.WriteLine((results.Count-failed)+"/"+results.Count+" PASS");return failed==0?0:1;
    }
}
