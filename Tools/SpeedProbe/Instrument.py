import pathlib,re,shutil
root=pathlib.Path(__file__).resolve().parents[2]
source=root/'Builds/FaunaBaselineSources/Simulation'
target=root/'Builds/SpeedProfileSources/Simulation'
shutil.copytree(source,target,dirs_exist_ok=True)
names={'GetUnit','GetBuilding','GetSettlement','Find','CanStep','GetStepCost','IsWalkable','GetTerrainSpeed','BuildingNavigationCell','UpdateUnit','UpdateEnvironment','UpdateResources','UpdateCampfireSupply','UpdateMarkets','UpdateMaritime','UpdateSettlements','UpdateDevelopment','UpdateAgriculture','UpdateWildlife','UpdateCaravans','UpdateSocial','Tick'}
for path in target.rglob('*.cs'):
    s=path.read_text(encoding='utf-8-sig')
    matches=list(re.finditer(r'\b(?:public|private|internal)\s+(?:static\s+)?[\w<>\[\]]+\s+(\w+)\([^;{}]*\)\s*\{',s))
    for m in reversed(matches):
        if m.group(1) not in names:continue
        start=m.end();depth=1;end=start
        while depth:
            if s[end]=='{':depth+=1
            if s[end]=='}':depth-=1
            end+=1
        label=path.stem+'.'+m.group(1)
        s=s[:start]+' long speedTraceStart=global::SpeedProbeTrace.Begin(); try {'+s[start:end-1]+' } finally { global::SpeedProbeTrace.End("'+label+'",speedTraceStart); } '+s[end-1:]
    path.write_text(s,encoding='utf-8')
print(target)
