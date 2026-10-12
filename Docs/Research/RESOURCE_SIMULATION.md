# Resource simulation 0.3 — contract and implementation record

12.10.2026. Status: pure Simulation implementation complete and frozen for final native integration; latest .NET probe 68/68 PASS after the dated household/demography extension below. Native verification belongs to the integrator. Times below are proposed playable balance, not WorldBox reverse-engineering or the long-term design economy.

`ResourceType`: None0 Tree1 Stone2 IronOre3 CopperOre4 GoldOre5 SilverOre6 Coal7 Clay8 Sand9 Limestone10 Tin11.
`ResourceNodeData` struct: Type, Amount, Capacity, RegrowTicksRemaining. `World.ResourceNodes` is a grid array of Width*Height; cell index is identity. `GetResourceAt(x,y)` O(1). Current `ResourceVersion=2`, `ResourceRevision` updates on visible changes. Check `ResourceCatalog.IsCompatible(type,tile)` and BuildingId before drawing a node; incompatible terrain pauses exhausted recovery.

`ItemType`: None0 Log1 Plank2 Firewood3 RawStone4 StoneBlock5 StoneTile6 Clay7 Brick8 IronOre9 CopperOre10 GoldOre11 SilverOre12 Coal13 Sand14 Limestone15 TinOre16. String design IDs map Log→wood, Plank→plank, RawStone→stone; see design tables for other names.
`UnitData`: CargoKind, CargoAmount, CargoOwnerId, PickupKind, ResourceCell=-1, SourceBuildingId, TargetBuildingId, GroundStockIndex=-1. Carry capacity is now1 for the **combined** CargoAmount+CarriedFood+CarriedWood. PickupKind is a planned transfer item; before physical pickup CargoKind=None and CargoAmount=0. UnitTask0..10 unchanged, Process11 and Haul12 added. Gather harvests a node continuously (WorkTimer=0); Return delivers existing cargo; Haul approaches an inventory or ground pile to pick up actual stock before moving to the destination. PathCursor<Path.Count means movement, never work animation. TaskStartedTick changes when the task enum changes, not when another target is chosen for the same task.

`BuildingType`: Hall0 (campfire visual), House1 Farm2 Tower3 Sawmill4 Stoneworks5 Kiln6. `BuildingData.Inventory` float[17], WorkerId, ActiveRecipe, ProcessProgress seconds. One worker per workshop. `RecipeType`: None0 Plank1 Firewood2 StoneBlock3 StoneTile4 Brick5. WorkTimer during Process is remaining processing seconds on site; interruptions preserve workshop progress and inventory but release the worker slot.

- Hall: Log1→Plank1 in10s; Sawmill: same in3s.
- Hall: Log1→Firewood2 in6s; Sawmill: same in3s.
- Stoneworks: RawStone2→StoneBlock1 in5s, or RawStone2→StoneTile2 in6s.
- Kiln: Clay2+Firewood1→Brick2 in8s.

Plank has one authoritative stock: Settlement.Wood. Inventory[Plank] is always0, output is credited once after actual on-site work. Other materials live in actual building inventories (capacity120 total per building). `GetSettlementStock(id,item)` sums those inventories, excluding cargo/natural deposits. `GetResourceBalance(id,item)` returns ResourceBalance fields Stock,Target,Deficit,Surplus; these are actual available stocks against an explicit stockholding policy, not imaginary trade/research.

Regrowth after exhaustion: Tree90s, Stone150, Iron300, Copper270, Gold600, Silver480, Coal240, Clay120, Sand90, Limestone180, Tin360. Timing is saved as fixed-tick countdown. Tree capacity3..5 logs, minerals6..12 units; extraction Tree.75 units/s, Stone.55, Clay/Sand.65, other deposits.45. Remaining tree amount drives Vegetation and appearance, and the old independent conversion of generic vegetation into Wood is removed.

Deposits are generated in seeded16×16 regional clusters with scattered common stone and woodland. Metal/coal/clay availability is not guaranteed around each settlement. Resource renewal is a stated sandbox mechanic, not a geological-timescale claim. Regrowth is a source; clearing/burning and destruction are sinks recorded separately from real inventory. `World.LostResources[17]` records resource loss on death/building destruction/clearing; cargo does not magically return to storage.

Migration: ResourceVersion0 is accepted then upgraded once; deterministic generation does not consume gameplay RNG or IDs. Old Wood and CarriedWood remain processed planks; old Stone moves to an existing physical building inventory once. No duplicate inventory mirror. Old legacy Gather orders must be reconsidered against nodes. Save/load retains node timers, cargo, routes, stock, recipe progress and worker assignment.

Migration limitation: legacy Stone that exceeds the capacity of surviving owned buildings, or has no surviving building, enters LostResources[RawStone]; migration does not create an invisible warehouse or new building. ResourceVersion becomes1 while schema/content/generator IDs remain1.

Diplomacy remains explicit through SetDiplomacy; different species do not start automatically at war. Actual deficits are exposed for future trade/conflict decisions, but caravans, barter, minting, smelting and research/equipment recruitment are not implied implemented by this slice. Civilians retain temporary local militia behavior, and no Guard is created.

## Completed review and verification — 2026-10-12

New settlements start with Wood=0; the existing starting food, campfire and farm remain. Ten planks must actually be processed at the campfire before a sawmill can consume them. The construction policy is sawmill → first house → stoneworks when at least2 stored RawStone exist → kiln when at least2 stored Clay exist → ordinary housing/farms. This avoids births indefinitely crowding out the first processing stations without supplying nonexistent local clay. A fed Forester can choose other industrial deficits even when plank reserves are sufficient.

Concrete review corrections: switching between two Process targets releases the old worker slot even when the enum is unchanged; fire interrupts work while preserving existing recipe inputs/progress; every positive natural deposit remainder remains mineable so interruption cannot prevent depletion/regrowth. Cargo blocked by a new water barrier remains carried, and delivery resumes after route repair. On-site processing consumes inputs only at completion and credits output once.

- `Evidence/Simulation/resource-pipeline-probe-final.json`: 58/58 pure C# checks, including11 resource groups. Controlled zero-plank fixture: first sawmill tick1100, both actual block and brick output by tick3176. These are explicit fixtures, not untouched natural gameplay or native rendering evidence.
- Nine resource NUnit methods added to `SimulationTests.cs`; native execution is delegated to the integrator. Expected complete EditMode suite is45 cases including the integrator's3 actual JsonUtility migration/persistence tests. Earlier snapshot's36/36 result is not the final snapshot verdict.
- Integrator natural opening audit `Evidence/Simulation/resource-natural-opening-v2.json`: ordinary seed1618033, 128×128, original four founding commands, no painted supply or stock grants. 45 validation checkpoints PASS through450 simulated seconds; all four towns build Stoneworks, three obtain kilns, one lacks clay. Earlier audit is preserved separately. This confirms the bounded slice for one seed, not a global balance proof.
- Source receipt: `Evidence/Simulation/resource-pipeline-source-receipt.json`. No Unity invocation or Git mutation was performed by the Simulation agent.

Remaining scope: ore smelting and crafts, inter-settlement trade, resource-driven war decisions, full wildlife and five construction tiers are design work, not features completed here. Timers are prototype balance. Native visual acceptance, actual final Unity suite, build and load profiling remain the integrator's next steps.

### Final integration correction — 2026-10-12

The integrator identified an old-save edge case: a cached EnemyId from implicit species wars could remain while Relations was empty. With a visitor near an abandoned town, UpdateSettlements could then capture peaceful territory. Reproduced before the fix in `Evidence/Simulation/resource-stale-enemy-before-capture.json` (one expected regression failure). Settlement updates now clear EnemyId when AtWar is false, including empty settlements, before the existing capture logic executes. No migration RNG or new diplomacy state is introduced.

Added one save-roundtrip regression that preserves peaceful building/territory ownership, clears both stale caches, and confirms capture still works after explicit SetDiplomacy(war=true). `Evidence/Simulation/resource-stale-enemy-fixed.json`:59/59 PASS. Expected final EditMode count is now46 (previous45 plus this regression). Updated source receipt is `Evidence/Simulation/resource-pipeline-source-receipt-v2.json`; previous receipts are preserved.

## Household, carrying and demographic extension — 2026-10-12

User expansion implemented before release: one-unit loads, real free/paid exchange governed by the ruler, natural tax, women/men/children, and a separate ruler decision on adolescent light work. This is a bounded layer above communal production; it does not privatize all settlement construction, implement coin wallets, monetary prices or distant trade.

### Physical goods and migration

`ResourceCatalog.CarryCapacity=1`; `SimulationEngine.CarriedLoad(unit)` is the sum of resource cargo, food and old processed planks. Harvesting is continuous, so a partially extracted log/ore batch can be fractional up to1. Farming also stops at the combined limit. Eating consumes food at the destination or existing carried food and never adds a second load alongside a log.

`World.GroundStocks` contains `GroundStockData { OwnerId, Item, Amount, X, Y, IsFood }`. A food pile uses IsFood=true/Item=None; legacy processed wood uses Item=Plank. ResourceVersion0/1 allows the historical carrying limit before engine migration. The engine keeps at most1 and places **every excess unit at the former carrier's exact coordinates**, with no RNG draw, new entity ID, credit to a distant warehouse or discarded remainder. Saved ground-pile indices remain stable; zero-amount entries are not rendered. Adults recover their own or unowned piles through real paths, one trip at a time. No route means the pile stays on the ground. A dead owner's piles become unowned; children dropping forbidden cargo leave public piles at their current location. Death of a carrier still records actual carried material in LostResources; this is a stated destruction sink, not a remote deposit.

`DemographyVersion=1`, `EconomyVersion=1`, `ResourceVersion=2`; main schema/content/generator IDs are unchanged. Existing inventories remain public on migration, private and tax stocks start empty, and migration is idempotent. Legacy JsonUtility null fields are explicitly tolerated during old stone transfer before new containers are initialized.

### Exchange, ownership and natural taxation

80% of a **newly harvested** batch's first delivery enters ordinary building Inventory;20% enters that adult owner's `Building.PersonalStocks`, a list of `OwnedGoodsData { OwnerId, Item, Amount }` at that same building. Existing communal hauls and migrated cargo do not split again. The communal contribution is a fixed production rule, explicitly separate from tax on paid personal exchanges. Food and legacy processed planks continue their existing communal flow.

`Building.TaxStocks[17]` stores actual natural tax at that building. Inventory, PersonalStocks and TaxStocks are disjoint physical quantities whose **combined capacity is120**. GetSettlementStock counts only public production inventory (Plank still means Settlement.Wood). `GetCitizenStock(unitId,item)` sums actual personal lots, and `GetTreasuryStock(settlementId,item)` sums physical tax reserves. Neither is a coin wallet or money balance.

The workshop's saved adult WorkerId can acquire only missing inputs for its current recipe, and only while physically at that workshop. Owners' stored personal lots act as standing offers; the seller need not stand beside the buyer. FreeDistribution transfers another adult's real lot as a Gift. Market performs **barter1:1 in another real material**: the buyer's personal payment lot decreases, the seller receives payment minus the natural tax, and that tax enters TaxStocks. The acquired input enters the local public production buffer once. Own inputs enter as Contribution. No imaginary credit, loan, starting cash or silent free private sale substitutes for an insolvent buyer; that adult can continue gathering public inputs and building personal stock.

One preflight considers all recipe inputs and a single shared payment budget. Necessary fuel/input kinds are excluded from tender, own inputs are used first, and insufficient total payment commits no acquisition. This prevents buying clay with the very firewood needed for a brick recipe, double taxation while buying it back, and spending one lot twice across two shortages. The regular recipe completion remains the only conversion transaction.

`World.Trades` retains the last128 `TradeRecord` rows: Tick, BuildingId, SellerId, BuyerId, Kind, Item, Amount, PaymentItem, PaymentAmount, TaxAmount. `TradeKind`: Contribution0, Gift1, Barter2. This is a bounded recent audit, not a complete lifetime economic journal. Recipe continuation and ledger survive save/load without repeated payment. Dead owners' personal lots become public at the same building. Building destruction sends public/private/tax material to explicit loss accounting. Capturing an abandoned building makes its remaining private goods public locally; stale private owner references are not retained across settlement ownership.

### Ruler decisions

`EconomicPolicy {FreeDistribution0,Market1}`, `ChildLaborPolicy {Forbidden0,LightWork1}`, `ResourceFocus {Balanced0,Timber1,Masonry2,Metals3}`. Settlement fields: EconomyPolicy, TaxRate, ChildLabor, ResourceFocus, PolicyRulerId, NextPolicyReviewTick, LastDecreeTick, LastDecreeReason. Public command `SetEconomicPolicy(settlementId,policy,taxRate,childLabor,focus)` validates the values and immediately enforces child-work restrictions.

Adult traits Prudence/Ambition/Generosity in0..1 are deterministic seed/ID values. A real ruler reviews laws on succession and every120 simulated seconds. Food below2×population selects free distribution and adolescent help. Otherwise generosity≥.55 selects free distribution; less generous rulers choose barter, with10% tax or30% below generosity.25. High ambition>.65 with generosity<.4 may permit light work even without immediate food shortage. Prudent rulers select a deficit-oriented wood/masonry focus; less prudent rulers can favour a less useful material. Focus changes actual natural-resource search ordering, retaining the initial ten-plank bootstrap and real stock-deficit checks. No adult ruler means free distribution, no tax, forbidden child work and balanced resources. The last reason is saved and visible. No automatic war, fabricated army or unimplemented decree catalog is activated.

### Sex, children and learning

`Sex {Female0,Male1}`, `Unit.Sex`, derived IsChild = Age<16. Names, sex and ruler traits are assigned deterministically; legacy names are migrated to the corresponding name pool without consuming gameplay RNG. Adults of either sex receive the same civilian profession choices. Birth requires at least one living adult of each sex near the settlement centre, plus the existing food/housing conditions. This is demographic reproduction without parentage/genetics records.

Forbidden means no child production. LightWork permits ages12–15 to help at farms and carry at most1 food, firewood or legacy plank; it does not permit logs, ore, stone, clay, mining, workshop processing, trading, combat or rulership. Under12 never works. Changing the law cancels prohibited work, releases any old station/path and deposits existing prohibited cargo locally without deleting it. Adult maturation restores the ordinary profession rule.

UnitTask Play13 and Learn14 provide bounded movement near the home centre. A Learn action increases saved Education only on site with a living adult of the same settlement within3 tiles. Education grows at.002/s up to1; adult farm/harvest/processing work receives at most10% speed bonus. This is simplified supervised learning and play, not a school curriculum, teacher profession or trained neural model.

### Verification and remaining limits

- `Evidence/Simulation/civil-economy-probe-freeze.json`:68/68 PASS; nine new household/demography groups extend all prior59 scenarios. They cover real on-site gift/barter/tax, no remote transfer, save continuation/no second payment, insolvency, reserved fuel/shared budget, combined load and migration, opposite-sex births, child laws/revocation/heavy loads, actual supervised learning, owner death/storage destruction, ruler decisions and null legacy containers.
- Nine corresponding NUnit methods added. Source counts: SimulationTests47 + SaveStoreTests5 + ResourcePersistenceTests5 =57 expected EditMode cases. Final native results belong to the integrator and are not inferred from .NET PASS.
- Prior fixture cargo2/3 became legal cargo1 where the test is about fire/routes/destruction; explicit historical overload remains in versioned migration tests. The new conservation test preserves3 ore+2.8 food+2 planks as load+ground=7.8, then verifies separate real collection trips.
- Controlled four-worker resource fixture now reaches sawmill tick1120 and actual block plus brick output by tick4038. These are prepared fixtures, not a guarantee for every natural map.
- Integrator's first natural audit `Evidence/Simulation/households-natural-opening-first.json` reports45 validation checkpoints,237268 carrying checks and actual10 barter/30 gift/105 contribution records over450 simulated seconds. This is preliminary evidence from one seed; final audit/source snapshot is recorded separately.

Source receipt: `Evidence/Simulation/civil-economy-source-receipt.json`. Unity/build/native captures are the integrator's responsibility. Obsidian synchronization is handed to the integrator with this card and receipts; this agent has not written the external vault. Balance and throughput across more seeds, long-run tax storage, advanced goods, coin minting, full schools and inter-settlement trade remain further work.
