"""Compare saved baseline/final ordinary worlds; never migrate or modify a world."""
import json, pathlib, hashlib, datetime, re
ROOT=pathlib.Path(__file__).resolve().parents[2]
def load(p):return json.loads((ROOT/p).read_text(encoding='utf-8-sig'))
def sha(p):return hashlib.sha256((ROOT/p).read_bytes()).hexdigest().upper()
basepath='Evidence/Simulation/production-interior-natural-final-r2.json'
newpath='Evidence/Simulation/market-natural-final.json'
base=load(basepath);new=load(newpath)
rows=[]
for seed in [1618033,271828]:
 a=next(x for x in base['results']if x['seed']==seed);b=next(x for x in new['results']if x['seed']==seed)
 worldpath=b['exportedWorld'];w=load(worldpath)['World'];assert sha(worldpath)==b['exportedWorldSHA256'].upper()
 # Recipes and output counters are stable across these two versions. No grants in either startup.
 recipes={'Charcoal':(3,{'Log':2},{}),'Leather':(2,{'Log':1},{'Hide':2}),'LinenCloth':(2,{},{'Fiber':3}),'LinenClothes':(1,{'LinenCloth':2},{}),'Furniture':(1,{'Plank':3},{}),'Rope':(2,{},{'Fiber':3})}
 consumed={};agri={};batches={}
 for item,(qty,inputs,harvest)in recipes.items():
  count=b['final']['produced'][item]/qty
  if not count:continue
  batches[item]=count
  for k,v in inputs.items():consumed[k]=consumed.get(k,0)+v*count
  for k,v in harvest.items():agri[k]=agri.get(k,0)+v*count
 assert sum(batches.values())==b['final']['batches']
 rows.append({'seed':seed,'baseline':{k:a['final'][k]for k in ['citizens','batches','workToolsIssued','clothesIssued']},'market':{k:b['final'][k]for k in ['citizens','batches','workToolsIssued','clothesIssued','marketTransactions','marketWages']},'firstTechnologyTick':b['firstTechnologyTick'],'firstProductTick':b['firstProductTick'],'firstClothesIssuedTick':b['firstClothesIssuedTick'],'firstToolIssuedTick':b['firstToolIssuedTick'],'worldValidations':b['worldValidations'],'carrierChecks':b['carrierChecks'],'adultResidentSeconds':b['adultResidentSeconds'],'hungryAdultResidentSeconds':b['hungryAdultResidentSeconds'],'thirstyAdultResidentSeconds':b['thirstyAdultResidentSeconds'],'shipsAtEnd':len(w.get('Ships',[])),'portsAtEnd':sum(x['Type']==23 for x in w['Buildings']),'portWarehousesAtEnd':sum(x['Type']==26 for x in w['Buildings']),'actualClothesWearersAtEnd':sum(x.get('Clothes',0)>0 for x in w['Units']),'batchCountsFromOutputLedger':batches,'recipeConsumptionDerivedFromCompletedBatches':{'materials':consumed,'agriculture':agri},'derivationLimit':'Only completed Industry batches; excludes construction, incomplete deliveries, food, death and baskets. Inputs derived from unchanged recipe and production counters, not a per-pickup trace.','exportedWorld':worldpath,'sha256':sha(worldpath)})
changed=[p for p,h in new['sourceSHA256'].items()if sha(p)!=h.upper()]
receipt={'utc':datetime.datetime.now(datetime.timezone.utc).isoformat(),'baselineReceipt':basepath,'baselineReceiptSHA256':sha(basepath),'marketReceipt':newpath,'marketReceiptSHA256':sha(newpath),'naturalSourceUnchangedDuringRun':new['sourceUnchanged'],'sourceChangedSinceNatural':changed,'comparisonLimit':'Same seeds/startup/duration, different deterministic revisions; not an isolated A/B effect or native performance result. No fleet/port at final snapshot; subsequent marine guards not retrospectively tested by this run.','rows':rows}
p='Evidence/Simulation/market-natural-comparison.json';(ROOT/p).write_text(json.dumps(receipt,ensure_ascii=False,indent=2),encoding='utf-8');print(p);print('Changed since run:',changed)
