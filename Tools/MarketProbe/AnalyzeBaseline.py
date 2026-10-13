"""Read-only diagnosis of the saved 0.7 worlds. No game state is modified."""
import json, pathlib, hashlib, re, datetime
ROOT=pathlib.Path(__file__).resolve().parents[2]
def load(p): return json.loads((ROOT/p).read_text(encoding='utf-8-sig'))
state=(ROOT/'WorldGenesis/Assets/_Project/Scripts/Simulation/World/WorldState.cs').read_text(encoding='utf-8-sig')
def enum(text,name): return [x.strip().split('=')[0].strip() for x in re.search(r'public enum '+name+r'\s*\{([^}]+)\}',text).group(1).split(',')]
tasks=enum(state,'UnitTask');buildings=enum(state,'BuildingType')
itemtext=(ROOT/'WorldGenesis/Assets/_Project/Scripts/Simulation/World/ResourceData.cs').read_text(encoding='utf-8-sig');items=enum(itemtext,'ItemType')
agritext=(ROOT/'WorldGenesis/Assets/_Project/Scripts/Simulation/World/AgricultureData.cs').read_text(encoding='utf-8-sig');products=enum(agritext,'AgriculturalProduct')
indtext=(ROOT/'WorldGenesis/Assets/_Project/Scripts/Simulation/World/IndustryData.cs').read_text(encoding='utf-8-sig');recipes=enum(indtext,'IndustryRecipeType')
goals={'IronTools':('z02_ironworking','Smithy',{'IronIngot':1,'Plank':1,'Charcoal':1},{}),'IronIngot':('z02_ironworking','Smelter',{'IronOre':2,'Charcoal':1},{}),'Charcoal':('z01_charcoal','Smelter',{'Log':2},{}),'LinenClothes':('t02_linen','Tailor',{'LinenCloth':2},{}),'LinenCloth':('t02_linen','Weaver',{}, {'Fiber':3})}
receipt=load('Evidence/Simulation/production-interior-natural-final-r2.json')
results=[]
for seed in (1618033,271828):
 p=f'Builds/ProductionNatural/seed-{seed}-tick-18000.world.json';raw=(ROOT/p).read_bytes();w=json.loads(raw)['World'];r=next(x for x in receipt['results'] if x['seed']==seed);towns=[]
 for s in w['Settlements']:
  bs=[b for b in w['Buildings'] if b['SettlementId']==s['Id']];us=[u for u in w['Units'] if u['SettlementId']==s['Id']]
  stock={item:sum(b['Inventory'][i] for b in bs)+(s['Wood'] if item=='Plank' else 0) for i,item in enumerate(items) if i<len(w['LostResources'])}
  agri={item:sum(b['AgriculturalStock'][i] for b in bs) for i,item in enumerate(products)}
  gates={}
  for good,(tech,building,inputs,agri_inputs) in goals.items():
   gates[good]={'research_unlocked':tech in s['UnlockedTechnologies'],'required_technology':tech,'workshops':sum(buildings[b['Type']]==building for b in bs),'missing_materials':{k:round(v-stock.get(k,0),4) for k,v in inputs.items() if stock.get(k,0)<v},'missing_agriculture':{k:round(v-agri[k],4) for k,v in agri_inputs.items() if agri[k]<v}}
  towns.append({'id':s['Id'],'population':s['Population'],'food':s['Food'],'wood':s['Wood'],'active_technology':s['ActiveTechnology'],'research_progress':s['ResearchProgress'],'research_rate':s['ResearchRate'],'unlocked':s['UnlockedTechnologies'],'gates':gates,'workshops':[{'id':b['Id'],'type':buildings[b['Type']],'recipe':recipes[b['ActiveIndustryRecipe']],'worker':b['IndustryWorkerId'],'progress':b['IndustryProgress'],'batches':b['CompletedIndustryBatches']}for b in bs if 16<=b['Type']<=22],'tasks':{task:sum(tasks[u['Task']]==task for u in us)for task in tasks if any(tasks[u['Task']]==task for u in us)},'longest_current_jobs':[{'id':u['Id'],'task':tasks[u['Task']],'age_seconds':round((w['Tick']-u['TaskStartedTick'])/10,1),'destination':u['TargetBuildingId'],'source':u['SourceBuildingId'],'cargo':items[u['CargoKind']],'amount':u['CargoAmount'],'remaining_path':len(u['Path'])-u['PathCursor'],'hunger':u['Hunger'],'thirst':u['Thirst']}for u in sorted(us,key=lambda u:u['TaskStartedTick'])[:8]],'material_stock':{k:round(v,4)for k,v in stock.items()if v>0},'agriculture_stock':{k:round(v,4)for k,v in agri.items()if v>0}})
 results.append({'seed':seed,'saved_world':p,'sha256':hashlib.sha256(raw).hexdigest(),'tick':w['Tick'],'first_technology_tick':r['firstTechnologyTick'],'first_product_tick':r['firstProductTick'],'coarse_timeline':r['timeline'],'towns':towns})
out={'utc':datetime.datetime.now(datetime.timezone.utc).isoformat(),'kind':'Read-only baseline 0.7 diagnosis from immutable final receipt and unchanged exported worlds','limits':'Endpoint recipe gates and task ages are measured. Six 300-second interval snapshots are not continuous blocked-time measurements; no single causal share or permanent deadlock is inferred. Current IDs preserve 0.7 ordering. No 0.8 constructor or migration applied.','results':results}
path=ROOT/'Evidence/Simulation/market-baseline-0.7-diagnosis.json';path.write_text(json.dumps(out,ensure_ascii=False,indent=2),encoding='utf-8');print(path)
for r in results:
 print('SEED',r['seed'])
 for s in r['towns']:
  print('TOWN',s['id'],'pop',s['population'],'research',s['active_technology'],round(s['research_progress'],2),'rate',round(s['research_rate'],3),'tasks',s['tasks'])
  for k,v in s['gates'].items():print(' ',k,v)
