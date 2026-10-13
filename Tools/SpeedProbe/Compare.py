import json,pathlib,statistics,datetime,sys
root=pathlib.Path(__file__).resolve().parents[2]
optimized=sys.argv[1] if len(sys.argv)>1 else 'speed-optimized-repeat'
output=sys.argv[2] if len(sys.argv)>2 else 'speed-comparison-final.json'
a=[json.loads((root/f'Evidence/Simulation/speed-baseline-repeat-{i}.json').read_text()) for i in range(1,4)]
b=[json.loads((root/f'Evidence/Simulation/{optimized}-{i}.json').read_text()) for i in range(1,4)]
rows=[]
for n in range(6):
 old=[x['results'][n] for x in a];new=[x['results'][n] for x in b]
 assert all(x['sourceUnchanged'] and x['failures']==0 for x in a+b)
 eq=all(o['initialSHA256']==v['initialSHA256'] and o['finalSHA256']==v['finalSHA256'] and o['micro']['pathHash']==v['micro']['pathHash'] for o,v in zip(old,new))
 before=statistics.median(x['simulationSecondsPerWallSecond'] for x in old);after=statistics.median(x['simulationSecondsPerWallSecond'] for x in new)
 row=dict(preset=old[0]['preset'],requestedPopulation=old[0]['requestedPopulation'],initialPopulation=old[0]['initialPopulation'],finalPopulation=old[0]['finalPopulation'],ticks=600,medianBaselineSimulationSecondsPerWallSecond=before,medianOptimizedSimulationSecondsPerWallSecond=after,ratio=after/before,stateAndPathHashesMatch=eq,medianBaselineP99TickMs=statistics.median(x['p99TickMs'] for x in old),medianOptimizedP99TickMs=statistics.median(x['p99TickMs'] for x in new))
 rows.append(row);print(row)
changed=[p for p in a[0]['sourceSHA256'] if a[0]['sourceSHA256'][p]!=b[0]['sourceSHA256'].get(p)]
report=dict(utc=datetime.datetime.now(datetime.timezone.utc).isoformat(),repeats=3,kind='Offline .NET Release fixed-tick comparison, not Unity FPS',environment={'DOTNET_TieredCompilation':'0','sharedDesktop':'Other project work may run concurrently; report medians and do not claim CPU-isolated timings.'},sourceDifference=changed,exactDeterministicComparisonPassed=all(x['stateAndPathHashesMatch'] for x in rows),scope='Frozen 0.8 source versus the same source with only GridNavigation.cs and Engine Begin/EndUnitBatch hooks. Fauna/placement changes excluded intentionally.',baselineReceipts=[f'Evidence/Simulation/speed-baseline-repeat-{i}.json' for i in range(1,4)],optimizedReceipts=[f'Evidence/Simulation/{optimized}-{i}.json' for i in range(1,4)],cases=rows)
(root/'Evidence/Simulation'/output).write_text(json.dumps(report,ensure_ascii=False,indent=2),encoding='utf-8')
