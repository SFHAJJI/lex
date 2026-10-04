# Lex V3 boot

This repository is the Lex V3 product code. Work starts from `v3/integration`; `main` is the legacy line.

Read, in this order, and nothing else before working:

1. `STATUS.md`: the heads and the owner's pending decisions, and through it your lane's file
   (`STATUS-WEB.md` or `STATUS-DATA.md`): what is served, what is next, what is blocked.
2. `LAUNCH-CONTRACT.md`: the acceptance checklist for the V3 launch.

Working rules:

- Day-sized vertical slices on a branch from `v3/integration`; one pull request per slice.
- Evidence in the pull request description: the commands run, the counts, the scope.
- One review per pull request by the other model family (Decision 95; this lane runs
  `C:\lex-v3\lanes\review-by-codex.ps1 -Pr <n>`). Findings must be material and reproduced. One
  repair round, then merge on green CI.
- Update your lane's status file (`STATUS-WEB.md` or `STATUS-DATA.md`) in the same pull request;
  `STATUS.md` changes only for the heads and the owner's items.
- Keep `OWNER-BRIEF.md` current: the owner's one-page view, at most 40 lines of plain words and no
  hashes, updated in the first pull request of each day and whenever a run starts, ends or stops.
- Owner decisions are money, legal or public claims, credentials and going live (the owner's standing
  order of 2026-09-30), plus the per-run publisher-traffic authorisations STATUS.md lists. They are
  asked in the pull request or at the weekly checkpoint, never assumed. Everything else the driver
  decides and records in its lane's status file as a reversible driver decision.
- `https://github.com/SFHAJJI/lex-governance` holds the immutable specification pack and Decisions
  82 to 93, which still describe product scope. Its working model and stage protocol are retired.
