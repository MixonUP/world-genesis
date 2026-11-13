# Research registry and offline traceability validator (Claude, claude-20261113)

**Base:** `codex/ai-game-studio-collaboration-20261112` @ `3fdbd6f8fa60b56b4bfa26dbcce0daed66c4262c`
**Head:** `codex/claude-research-architecture-20261113`
**Scope:** adds files under `Research/AI_Game_Studio/ClaudeResearch/claude-20261113/` only. No changes to runner, requirement catalog, Unity portfolio, STATE/index or Pages.

## What this adds

- Research-loop architecture R00–R11 with four layers (knowledge, decisions, production, verification), three synthetic end-to-end walkthroughs and Mermaid diagrams (`ARCHITECTURE.md`).
- Versioned data contracts `ags.research-registry/1`: 9 entities, 9 edge types with allowed endpoints and semantics (`CONTRACTS.md`, `schemas/`). JSON is canonical; Markdown is generated.
- `validator.py` (Python 3.10+, stdlib only, offline). Subcommands: `check`, `trace`, `render-trace`, `query` (why / impact / requirement-gaps / declared-only / promises), `sources`.
- `COMPONENT_TRACEABILITY.json/.md`: all 49 requirements of 16 components mapped. Totals: 11 module_check, 23 interface_only, 14 out_of_scope, 1 no_data. Interfaces are not presented as integrations.
- A real bounded portfolio chain (`examples/portfolio_chain.real.json`) and `PORTFOLIO_AUDIT.md`. With historical records imported (not rerun), C07-R03, C08-R02 and C05-R03 stay OPEN.
- `SOURCE_REGISTER.json` (14 sources actually opened), `CAPABILITY_MATRIX.md`, `RESEARCH_GAPS.md`, `INTEGRATION_PROPOSAL.md` (read-only runner hook; not implemented).

## Tests run

See `Evidence/TEST_RUN.md` for exact commands and counts:
- existing runner suite, unchanged: 33 tests, OK;
- new validator suite: 56 tests, OK, 1 skipped (symlink creation not permitted on this Windows host; resolved-path containment is covered by a separate test). 14 of the 56 are regressions for findings of an independent read-only review (4 high, 6 medium and 4 low findings fixed; 3 low findings documented as limitations in CONTRACTS.md);
- CLI smoke checks, a reproducibility check and an input-immutability check.

**Not run:** Unity EditMode/PlayMode, builds, Player, any GUI. All Unity results cited are historical.

## Review notes

- `Research/AI_Game_Studio/Orchestrator/COMPONENT_REQUIREMENTS.json` shows as modified right after checkout. Its blob has CRLF while `.gitattributes` says `eol=lf`. This PR does **not** include that file. A renormalisation is proposed in `HANDOFF_TO_CODEX.md`.
- The validator authenticates nothing. A requirement PASS means only that the linked records satisfy the rules for the stated revision.

## Follow-up (owner: Codex)

1. Optional `--registry-report` input for runner (`INTEGRATION_PROPOSAL.md`).
2. Portfolio packets: contrast fix → unrun visual states → human art acceptance; EditMode rerun.
3. Sync `OBSIDIAN_PENDING.md`.

🤖 Generated with [Claude Code](https://claude.com/claude-code)
