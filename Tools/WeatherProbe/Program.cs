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
        var json=new JsonSerializerOptions{IncludeFields=true,IgnoreReadOnlyProperties=true};
        WeatherScenarios.Clone=w=>JsonSerializer.Deserialize<WorldState>(JsonSerializer.Serialize(w,json),json);WeatherScenarios.Snapshot=w=>JsonSerializer.Serialize(w,json);
        var results=new List<object>();int failures=0;
        foreach(MethodInfo method in typeof(WeatherScenarios).GetMethods(BindingFlags.Public|BindingFlags.Static))
        {
            if(method.ReturnType!=typeof(void)||method.GetParameters().Length!=0)continue;
            try{method.Invoke(null,null);results.Add(new{name=method.Name,passed=true});Console.WriteLine("PASS "+method.Name);}
            catch(Exception ex){failures++;string error=(ex.InnerException??ex).ToString();results.Add(new{name=method.Name,passed=false,error});Console.WriteLine("FAIL "+method.Name+": "+error);}
        }
        string path=args.Length>0?args[0]:"Evidence/Simulation/weather-probe.json";Directory.CreateDirectory(Path.GetDirectoryName(path));
        File.WriteAllText(path,JsonSerializer.Serialize(new{utc=DateTime.UtcNow,tests=results.Count,failures,results},new JsonSerializerOptions{WriteIndented=true}));return failures==0?0:1;
    }
}
