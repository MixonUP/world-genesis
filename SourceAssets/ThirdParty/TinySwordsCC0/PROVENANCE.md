# Tiny Swords — old CC0, Update 010

Author: **Pixel Frog**. Official source: https://pixelfrog-assets.itch.io/tiny-swords . Retrieved 2026-10-12 from the explicitly separate download **TS_old version_CC0 Licensed**, upload date 2024-01-03, itch upload ID 9428013. Receipt: `Evidence/Development/tiny-swords-cc0-source.jpg`.

Archive: `TinySwords-CC0-Update010.zip`, 2,780,719 bytes, SHA256 `E4D79BD4514BAD153CFA1CC8F4C8D8BF10ED39BF69BD98B83BDC46C03D14BB9C`.

The archive root is `Tiny Swords (Update 010)`. It contains no separate LICENSE/README file. The rights evidence is the author's explicit **CC0 Licensed** download designation, not the current pack's general license. CC0 reference: https://creativecommons.org/publicdomain/zero/1.0/ . The newer Free/Enemy packs use different terms and are **not included**.

Selected files are unmodified PNG/Aseprite sheets from `Factions/Knights/Troops/{Pawn,Warrior}/{Blue,Red,Purple,Yellow}/`. Runtime copies are in `WorldGenesis/Assets/_Project/Art/Units/Resources/TinySwordsCC0/`. The original archive is retained for reproducibility; macOS metadata is not imported into Unity.

## Verified sheet contract

Read the actual PNGs and Aseprite frame/tag chunks. Each cell is 192×192 px; six columns; each animation frame lasts 100 ms. PNG exports omit Aseprite's first `Original` frame.

| Sheet | PNG | Rows, top to bottom |
|---|---|---|
| Pawn | 1152×1152 | Idle, Run, Build, Chop, Carry idle, Carry run |
| Warrior | 1152×1536 | Idle, Run, two side attacks, two down attacks, two up attacks |

Six frames per row. Idle and run are side/front views with horizontal mirroring; **full four-direction locomotion is absent**. Pawn's Build gesture can represent farm work in this game's presentation but is not an authored farming animation. Eat, rest and conversation use poses/status cues, not claimed dedicated source clips. Other fantasy races retain distinct original sprites until appropriate art exists.

Rendering may crop transparent margins and pack an in-memory atlas without changing source pixels. Work, movement and attack selection must follow simulation state; animation never applies damage or produces resources.

Visual selection is a provisional project decision. Technical import/native review and user artistic acceptance are recorded separately in the development report.
