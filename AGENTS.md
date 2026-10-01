# Lex V3 boot

This repository is the Lex V3 product code. Work starts from `v3/integration`; `main` is the legacy line.

Read, in this order, and nothing else before working:

1. `STATUS.md`: the heads and the owner's pending decisions, and through it your lane's file
   (`STATUS-WEB.md` or `STATUS-DATA.md`): what is served, what is next, what is blocked.
2. `LAUNCH-CONTRACT.md`: the acceptance checklist for the V3 launch.

Working rules:

- Day-sized vertical slices on a branch from `v3/integration`; one pull request per slice.
- Evidence in the pull request description: the commands run, the counts, the scope.
- One fresh-context review per pull request. Findings must be material and reproduced. One repair
  round, then merge.
- Update your lane's status file (`STATUS-WEB.md` or `STATUS-DATA.md`) in the same pull request;
  `STATUS.md` changes only for the heads and the owner's items.
- Owner decisions (product scope, legal or public claims, publisher traffic, credentials, money) are
  asked in the pull request or at the weekly checkpoint, never assumed.
- `https://github.com/SFHAJJI/lex-governance` holds the immutable specification pack and Decisions
  82 to 93, which still describe product scope. Its working model and stage protocol are retired.
