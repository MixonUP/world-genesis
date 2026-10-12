# Legacy resource migration fixture

`legacy-v020.json` was generated on 2026-10-12 from the immutable `citizens-release-v020` verification snapshot of commit 24bad580c55c186bf8cf9ea3257bf570f7034172, before resources were added. This uses the old production Simulation classes, a System.Text.Json field serializer and the existing save envelope. The Unity test reads it with the real SaveStore/JsonUtility path; missing fields are not simulated by resetting fields in a new object.

Commands: seed4128,16×16, paint Grass at8,8 radius7, spawn6Humans,87×.1s+.037s, set old town Stone7/Wood23 and first citizen CarriedWood2. Old World.Validate passed before serialization. Tick87,Rng3796401059,NextId15. These explicit stock changes are a migration test fixture, not a gameplay screenshot or economy-balance claim.

## ResourceVersion1 before the one-unit rule

`legacy-resources-v1.json` uses the frozen `resources-release-v030` verification snapshot (receipt `Evidence/Development/snapshot-resources-release-v030.json`), not current edited classes. It has ResourceVersion1 and no household economy/demography fields. The same seed/map/spawn/87ticks+.037s commands run, then first resident carries IronOre3 and the second Food2.8. The old validator passed. Tick87,Rng3539512653,NextId13; SHA256 F90DA1EBC1AA70AEA469C92959CC5D366DF9E12424A59C838511D9F9A26ED27E.

The Unity serializer test checks preserved total goods, one-unit carried capacity, excess left at the old carrier's exact coordinates, unchanged RNG/IDs, idempotent migration and identical continuation after save/load. This snapshot was a pre-release, not the published0.2 tag.
