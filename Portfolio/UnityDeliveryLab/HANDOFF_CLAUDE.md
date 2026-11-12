# Technical handoff for Claude

The user asked for a branch where Claude can help. This branch contains the
isolated Unity portfolio sample and the offline orchestration checker, not the
private client-lead/economics package. World Genesis remains the original game.

## Start here

- Project/: complete editable Unity source, engine6000.0.45f1, Built-in/uGUI.
- PORTFOLIO_CASE.md: client-facing English case with explicit limitations.
- VERIFICATION.json: exact40-test/build result and selected capture metadata.
- Screenshots/: six real Player framebuffer captures, not image mockups.
- SourceAssets/provenance.csv: original procedural icons and runtime font.
- ../../Research/AI_Game_Studio/Orchestrator/runner/: offline decision checker,
  B2B arithmetic, tests and synthetic examples. It performs no API actions.
- ../../Research/AI_Game_Studio/Orchestrator/COMPONENT_REQUIREMENTS.json:
  component requirements; descriptions are not proof of implemented adapters.

Open Assets/Scenes/InventoryLab.unity only when GUI use is explicitly permitted.
The current user instruction forbids touching their mouse/windows. Do not launch
Unity or Player GUI on their machine as part of this handoff. Python unit tests
and source inspection can run without changing their windows.

## Existing evidence

Final source revision polished-20-f1:40/40 Unity EditMode tests passed; Windows
build succeeded with0errors/0warnings. Assisted state/save/corruption/backup
checks and a separate restart passed. Agent-operated ordinary UI verified
add/select/use/exchange/remove/save/load/theme/disabled states. A separate
1024×768 process restored the exact saved snapshot:130coins/100health/6shards.
These are historical results, not tests performed by the next contributor.

Independent review corrected maximum-revision overflow and malformed/escaped
JSON acceptance. Existing tests cover these regressions. Full-inventory visual,
light-theme1024, modifier-click input and long-duration stability remain unrun.
One later interactive process ended with orderly shutdown for an unknown cause;
do not infer either a crash or long-run reliability. Human artistic acceptance
has not been obtained. The HEALTH FULL label is a minor contrast issue.

## Useful first contribution

Inspect the disabled-label contrast and propose a bounded accessibility fix in
Project/Assets/Lab/Runtime/LabApp.cs. Keep the disabled control visibly inactive;
do not change state/save behavior. First explain the source-level change and
the verification possible under the no-GUI constraint. If visual validation is
not possible, leave it explicitly pending rather than claiming screenshot PASS.

Alternatively review one checker requirement/test under runner/. Do not implement
all components from the requirement catalog as an unbounded task. Coordinate
file ownership before overlapping with the Unity contributor.

## Collaboration and delivery

Base branch: codex/ai-game-studio-collaboration-20261112. Work on a child branch
and propose a small PR against it. Preserve the user's other uncommitted work.
Do not use git add . or git add Portfolio. Stage explicit owned source files.
The delivery archive and private economics remain local/authorized Space files.

The repository is public and readable. Pushing a commit requires the owner's
existing repository permission; this handoff creates no credentials or grants.
Do not send messages to clients or start paid services from this branch.
