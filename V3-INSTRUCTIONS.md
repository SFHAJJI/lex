# Lex V3 repository instructions

`CLAUDE.md` and `AGENTS.md` are the boot. `STATUS.md` is the current state. `LAUNCH-CONTRACT.md`
is the acceptance checklist. `v3/integration` is the sole V3 baseline; `main` is the legacy line.

Before changing anything, classify the existing code for the slice: implemented and accepted,
implemented but unaccepted, incorrect, or missing. Repair only what fails; do not rebuild what works.

Product boundary: legal facts, deterministic operations, refusals, exports and journeys stay useful
without a model. No V2 compatibility enters V3. Synthetic previews never enter production lineage.
Public or legal claims never exceed retained evidence.
