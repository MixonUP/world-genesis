# Inventory and Save Lab collaboration

This is an isolated portfolio demonstration, not a replacement for World Genesis.
Read HANDOFF_CLAUDE.md, README.md and VERIFICATION.json before changes. Source is
Project/. Unity6000.0.45f1, Built-in, uGUI, Windows x64. Do not upgrade engine or
packages automatically. Preserve .meta GUIDs and the pinned package lock.

The user explicitly requires **no control of their mouse or windows**. Do not
launch or raise GUI apps, drive native UI or close their processes without a new
explicit instruction. File work and truly headless checks are allowed. Mark UI
checks not-run if they cannot be performed under this constraint.

One state owner: UI reads Inventory; persistence validates before replacement.
Do not weaken save/backup validation to pass a test. The fixed ASCII v1 contract
and single-writer save directory are intentional. Revision is a saturating
diagnostic counter, not a transaction ID.

You are not alone in the repository. Claim a small file scope, inspect current
changes, never revert another author's work, and coordinate before changing
shared files. Prefer a child branch and PR against the collaboration branch.
Do not force-push. Do not stage the whole workspace: Library, raw evidence,
test UserData, private financial research and account details stay excluded.

Do not spend money, contact clients, publish further material or install external
tools merely because a requirement card describes those steps. A checked gate
does not prove revenue. Record measured, prepared and not-run states separately.
