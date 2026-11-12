# Inventory & Save Lab

**Self-initiated Unity UI and persistence demonstration.** This is a portfolio sample, not a client commission or a complete game.

The brief: make a small inventory whose visible UI, item actions and saved session agree. The delivery contains editable Unity source, a Windows x64 Player, original procedural icons, two visual themes, a reproducible verification workflow and selected real Player captures.

## What it does

A 12-slot inventory supports four item types, selection, adding, using or exchanging, and removal. Tonic restores health; other items exchange for coins. The inspector explains the selected action. At full health, tonic use is disabled. Save is explicit: unsaved changes remain in the current session.

The save boundary validates the version-1 schema, required fields, integer values, item identifiers, quantities and state ranges before replacing the running model. A failed load leaves the current session intact. Atomic replacement keeps the preceding valid commit as a backup; recovery is an explicit action. This small format deliberately rejects unknown fields and escaped property names; it is not a general-purpose JSON interchange API. The diagnostic revision counter saturates safely and is not a unique transaction ID.

Plain and Studio themes use the same hierarchy and state. A controlled revision changed the stack limit from 10 to 20 through one configuration file, before later visual and validation fixes. This demonstrates a bounded change with a recorded baseline.

## Open and try

Extract the Windows archive completely, then run `InventoryLab.exe` from a writable directory. Click the visible item and action controls. Save, make a change, then Load to restore the committed state. Close and reopen to resume that saved session. `New Session` resets the running session; saving is required to replace the disk state.

Keys: F5 Save; F9 Load; Tab switches themes; F12 captures the actual Player framebuffer. Shift plus an add-button click adds one stack. The local `UserData` folder is created beside the Player. The clean delivery contains no verification saves.

Open `Project/` in Unity **6000.0.45f1**, then `Assets/Scenes/InventoryLab.unity`. Built-in renderer and uGUI are pinned to the included package manifest/lock. Tests are under `Assets/Lab/Tests`; the accompanying PowerShell scripts run EditMode checks and build a named revision. No engine upgrade or purchased asset is required by this source package.

## Verification and provenance

`VERIFICATION.json` records the exact released revision, test count, build result, capture paths and independent review outcome. Captures in `Screenshots/` are real native Player frames. Assisted callback proofs are labelled separately from agent-operated mouse/keyboard input through the normal UI. Neither is presented as an independent human playtest or customer acceptance.

The review found and corrected a maximum-revision overflow and malformed-save acceptance, with before/after test evidence retained internally. Visual review improved save wording, contextual action labels and light-theme item contrast. The original procedural C# icons, layout and palette are listed in `SourceAssets/provenance.csv`; the font comes from the installed Unity runtime.

## Scope and limitations

The shipped scope has 40 passing EditMode tests and a successful Windows build with zero reported errors or warnings. Ordinary UI input and restart persistence were observed. Full-inventory visual inspection, the light theme at 1024×768, modifier-click input, full keyboard navigation and long-duration stability were not completed. A later interactive process ended with orderly subsystem shutdown for an unknown reason after its successful restart capture; no crash cause or exit code was established. The dim HEALTH FULL label remains a minor visual improvement.

The sample demonstrates a bounded Windows UI/state/save delivery. It does not establish mobile support, networking, a complete game's feel, a prior monitoring-dashboard reskin, editable Figma work, AI character production, commercial revenue or delivery time on an unfamiliar client project. Full keyboard navigation and accessibility certification are outside this sample. Human artistic acceptance and machine-to-machine performance testing are separate.

For a client task, the first deliverable would be an agreed screen or small module with exact acceptance criteria, supported project version, target resolutions and a limited revision scope. The existing project and specification determine the quotation.
