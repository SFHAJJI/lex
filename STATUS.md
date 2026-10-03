# Lex V3 status

Updated 2026-10-01 by the driver. This file holds the heads, the owner's open items and the pointers to the two
lane files (the standing order of 2026-10-01 13:50 UTC: STATUS.md was edited by nearly every pull request and was
the most frequent conflict). Each lane writes its progress in its own file, in the pull request that makes it:

- `STATUS-WEB.md`: the web lane (Claude): what is served, the API and the screens, the release path, the plan's
  items 4 to 8, the driver decisions, what waits on others, known defects.
- `STATUS-DATA.md`: the data lane (Codex): acquisition, custody, derivation, the populations, the plan's items 1
  to 3.

A feature pull request edits its lane file, not this one; this file changes for the heads and the owner's items.

## Heads

- `v3/integration`: `a90ab742` (2026-10-01, PR #897 merged). Build 45 s. Fast lane
  (`eng/test-fast.ps1`): 3,077 tests, 3,076 pass, 1 skipped (the review of PR #828). Ingest suite: green on CI for PR #760
  (the CI `dotnet` job runs the whole solution on every pull request, about 7 min on the runner;
  green for PR #834);
  locally about 15 min. 1,008 web tests pass. The web job's "browser debugger never answered" failures
  (keyboard-walk, and paint-check since #811) are fixed by PR #822: each browser binds its own
  debugging port (`launchBrowser`) instead of a random one another browser starting at the same
  moment could hold.
- Driver: Claude Opus 5.5 since 2026-09-29 (the Fable 5.1 driver ran out of tokens on 2026-09-28
  after PR #757; the user default model is now `claude-opus-5-5`).
- Plan: `C:\lex-v3\V3-FINISH-PLAN-2026-09-27.md` (owner's copy). Decision 94 (one driver, one queue,
  one review per pull request) merged in lex-governance on 2026-09-27. Launch target 2026-11-07.

## Owner rulings, 2026-09-30

Posted on PR #777 (comment 5905743344, the owner's account, relayed by the VS Code panel session).
The owner's words: "choose whatever you recommended piloted by the high quality asap driving to v3";
on EUR-Lex: "yes choose b but if you can find a recipt else where i dont really care as long as we
show text (users primarly want a temporal view of eu texts and dont really care about reciepts)".

1. EUR-Lex legal notice: option (b). EU text in EN and FR is shown. The rights receipt may come
   from any legitimate official source that states reuse is authorised; withholding EU text
   (option (a)) is not wanted. The panel session verifies which source answers an automated client
   and posts the URL, digest and a code-scope note as a second comment on PR #777. No request goes
   to eur-lex.europa.eu meanwhile. The panel opens a lex-governance PR recording these rulings as
   Decision 95, which the driver merges.
2. Evaluation card: served on the Trust and Coverage page and beside the release assets. The launch
   card carries machine gates run over the real mounted corpus, not fixture-only scores.
3. Web hosting: one server delivers the web bundle and the API (`/api/v3`, `/mcp`) on one origin,
   so the current CSP and no-CORS stand. That server sends `frame-ancestors`, HSTS and
   `Referrer-Policy`. A live page's banner never says "synthetic" on a real mount.
4. French and English refusal sentences: the owner reviews them in person. The driver prepares one
   short list for the weekly checkpoint and does not block on it.
5. EU parity details, the event log, the `ask` card verdict and the four operations with no data:
   the driver's stated defaults (below).
6. Codex: a weekly cold read of the week's merges, and a second sign-off at the two release gates
   when available.
7. Standing order: the owner is asked only about money, legal or public claims, credentials and
   going live. Everything else the driver decides, records here as a reversible driver decision,
   and keeps driving toward V3 at high quality.

Decision 95 (lex-governance PR #9, merged 2026-09-30) records these rulings and adds two things:
- The EU rights receipt is Commission Decision 2011/833/EU, fetched on the admitted Publications
  Office route (`https://publications.europa.eu/resource/celex/32011D0833`, `Accept:
  application/xhtml+xml`, `Accept-Language: eng`; observed 2026-09-30: 303 then 200, 48,730 bytes,
  sha256 `2d5bc877...`). Decision 88's one-GET exception for `eur-lex.europa.eu` is withdrawn: no
  request goes to that host. Served EU text carries "© European Union, https://eur-lex.europa.eu"
  and the statement that only the electronic Official Journal is authentic.
- Two lanes (amends Decision 94). Codex drives the data lane (the EU rights receipt, the first real
  mount, the populations, French EU bodies, EU parity, release-pipeline pieces without production
  credentials) in `C:\lex-v3\worktrees\codex-data`. This driver keeps the web lane. Every pull
  request is reviewed by the other model family before merge (`C:\lex-v3\lanes\review-by-codex.ps1`
  for this lane's pull requests), with one repair round and a merge on green CI. Production
  signing, deployment and promotion still wait for the owner.

## For the weekly checkpoint

- The French and English refusal sentences, one short list (ruling 4): PR #793. The 26 sentences the
  live pages say by refusal code, each page's two sentences for a refusal named only by its code,
  and the two hints a card that cannot be shown still carries (the date the history begins, the
  nearest article ids; review of #793). The French is no longer a draft: under the owner's
  delegation of 2026-10-02 it was reviewed by Claude (AI reviewer), not by a person, and it ships on
  the French pages (branch `writer/french-chrome`). The owner may revise any sentence. Printed,
  English beside French, by `node web/scripts/refusal-sentences.mjs`; a test holds every served
  sentence to one French sentence. The review's record: `C:\lex-v3\lanes\fr-review\review.md`.
- The live pages' French interface copy (Decision 41): the whole chrome table, reviewed under the same
  delegation by the same AI reviewer (129 of 370 entries changed, each with its reason and source in
  the record) and shipped as the French pages under `/fr/`, with a receipt that says so
  (`CHROME_REVIEWS` in `web/scripts/localization.mjs`). That includes the absence note ("Ce n’est pas
  un élément de preuve de l’inexistence de l’acte ou de la règle de droit.") and the live banner, both
  claims the owner may revise; `node web/scripts/refusal-sentences.mjs` prints every entry beside its
  English. The entries written after the review were reviewed in its addendum of 2026-10-03, so no
  French entry ships unreviewed (STATUS-WEB.md lists them).

- A rights question the retrieval set surfaced (PR #842): on the licence-blocked mount, search
  still matches inside the text the licence withholds. It answers which articles hold a word, and
  shows none of the text: the licence-blocked journey finds no passage on any page. Whether a
  non-admitting licence should also keep its text out of search matching is the owner's call
  (rights). Nothing changes until then.

## Blocked on the owner

Only money, legal or public claims, credentials and going live (ruling 7):
- Azure production credentials and the signing identity, needed by week 5.
- The weekly 30-minute checkpoint slot.
