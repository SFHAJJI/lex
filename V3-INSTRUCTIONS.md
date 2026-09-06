# Lex V3 repository instructions

This repository contains product code. It does not define its own product authority.

## Authority

The canonical entry point is `SFHAJJI/lex-governance/BOOT.md`. The immutable architect pack,
numbered Decisions, requirement ledger, stage acceptance contracts, artifact registry and kickoff
packet live there. If this file conflicts with them, this file is stale.

`v3/integration` is the sole V3 implementation baseline. `main` is the legacy/operations line and
must not be used as a V3 template or merged merely to make branch history tidy.

## Existing implementation

Merged code is evidence of implementation, not automatic acceptance and not disposable scaffolding.
For every assigned requirement, inspect `v3/integration` first and classify it:

1. implemented and accepted: retain it and link its evidence;
2. implemented but unaccepted: execute the stage contract;
3. incorrect: repair the failing clause only;
4. missing: implement the smallest complete slice.

This protects the substantial Stage 1 work and partial Stage 2 and Stage 5 work from both false
closure and needless rebuilding.

## Work and review

- One writer owns a slice; the other seat reviews its pushed exact commit.
- Use the shared GitHub issue as the durable mailbox and the `REVIEW REQUEST` format in governance
  `BOOT.md`. The owner is not a courier.
- State only evidence and numbers personally produced, with exact ref and scope.
- An unopenable artifact is unverified. Unobserved, observed-absent and zero are different facts.
- A repair is a later commit, never a rewrite of a reviewed head.
- Integrate serially into `v3/integration`; do not promote partial contracts or evidence.

## Product boundary

Legal facts, deterministic operations, refusals, exports and supported journeys remain useful
without a model. V2 compatibility is not imported into V3. Synthetic previews remain incapable of
entering production lineage. Public or legal claims never exceed retained evidence and the active
stage contract.
