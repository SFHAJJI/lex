# Lex V3 status

Updated 2026-10-14 by the driver. This file holds the heads, the owner's open items and the pointers to the two
lane files (the standing order of 2026-10-01 13:50 UTC: STATUS.md was edited by nearly every pull request and was
the most frequent conflict). Each lane writes its progress in its own file, in the pull request that makes it:

- `STATUS-WEB.md`: the web lane (Claude): what is served, the API and the screens, the release path, the plan's
  items 4 to 8, the driver decisions, what waits on others, known defects.
- `STATUS-DATA.md`: the data lane (Codex): acquisition, custody, derivation, the populations, the plan's items 1
  to 3.

A feature pull request edits its lane file, not this one; this file changes for the heads and the owner's items.

## Heads

- `v3/integration`: `e5d0bd88` (2026-10-14, PR #958 merged: every page under its language, `/fr/` and `/en/`). The
  required checks are strict: `dotnet` (the whole solution, about 14 min on the runner), `web`, `canon-windows` and
  `journeys`. `journeys` walks J1 to J8 and the site's own addresses in Chrome over four fixture mounts (the fixture,
  licence-blocked, two-state and EU annex control mounts), by pointer and by keyboard on the pages the API serves.
- Driver: Claude Opus 5.5, the sole driver since 2026-10-02. The owner's order: no Codex, no other Claude session,
  full authorisation, and the owner is never asked. Each pull request is reviewed by an independent Claude subagent
  (STATUS-WEB.md, driver decisions). The data lane's file (`STATUS-DATA.md`) is kept by this driver too.
- Populations: the EU population is complete. EU run 16 (`C:\lex-v3\eu-population-20261004-16`) acquired it by
  2026-10-05 04:07 UTC, and its two offline derives were identical. The Luxembourg population: run 14
  (`C:\lex-v3\lu-population-20261009-14`) started on 2026-10-09 on the build with #937, #940 and #943, was stopped at the
  owner's request on 2026-10-10 after seven of nine families, and resumed from its journal as run 15 on 2026-10-11
  (`C:\lex-v3\lu-population-20261011-15`). Its acquisition completed on 2026-10-12 (260,235 records). The combined
  EU and Luxembourg mount was derived offline on 2026-10-13; its gates found Luxembourg search empty, a derivation
  defect #948 fixed, and the derive with the fix (`derive-c`) passes every gate (STATUS-DATA.md). The first signed
  release over it, `v3-20261013T214036Z-9a5f05d3d1b4`, passed every check on 2026-10-13 (STATUS-WEB.md). Runs 1 to 13 stopped on three defects those pull requests fix, and
  replays of run 13's journal found two more before the restart (#943); all their custody is kept (STATUS-DATA.md).
- Specification: the owner's goal since 2026-10-14 is an ideal public website to a legitimate, consistent
  specification. The specification of record, version 2 (`C:\lex-v3\SPEC-OF-RECORD-2026-10-14-v2.md`, written from
  the lex-governance pack, Decisions 82 to 95, the launch contract and 257 verified review findings, then revised after
  a 20-finding review), rules where they disagree, lists what must hold at launch (its L lines) and the owner's
  decisions O1 to O31 with recommended answers. The driver builds toward it; the owner adopts it (O1).
- Plan: `C:\lex-v3\PLAN-2026-10-14.md`, written from the specification of record on 2026-10-14 and revised the same day
  after a completeness and a feasibility review; the owner has it as a private page. Counted item by item, the launch
  set is about 230 driver-days. It recommends one public opening with the full launch set on Tuesday 2 February 2027
  (go/no-go on Wednesday 27 January), with a private preview for the owner on 7 November (the codes, the new home page
  and search) and the near-final site on 15 December. It supersedes `C:\lex-v3\V3-FINISH-PLAN-2026-09-27.md` and its
  2026-11-07 target. Decision 94 (one driver, one queue, one review per pull request) merged in lex-governance on
  2026-09-27.

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
  signing, deployment and promotion still wait for the owner. Suspended by the owner's order of 2026-10-02:
  one driver, Claude, runs both lanes (heads).

## For the weekly checkpoint

- The French and English refusal sentences, one short list (ruling 4): PR #793. The 26 sentences the
  live pages say by refusal code, each page's two sentences for a refusal named only by its code,
  and the two hints a card that cannot be shown still carries (the date the history begins, the
  nearest article ids; review of #793). The French was reviewed by Claude (AI reviewer) under the
  owner's delegation of 2026-10-02 and accepted by the owner in person on 2026-10-04 ("i did review
  it and its ok"); it ships on the French pages. The owner may still revise any sentence. Printed,
  English beside French, by `node web/scripts/refusal-sentences.mjs`; a test holds every served
  sentence to one French sentence. The review's record: `C:\lex-v3\lanes\fr-review\review.md`.
- The live pages' French interface copy (Decision 41): the whole chrome table, reviewed under the same
  delegation by the same AI reviewer (129 of 370 entries changed, each with its reason and source in
  the record), accepted by the owner on 2026-10-04 with the refusal sentences, and shipped as the
  French pages under `/fr/`, with a receipt that says so
  (`CHROME_REVIEWS` in `web/scripts/localization.mjs`). That includes the absence note ("Ce n’est pas
  un élément de preuve de l’inexistence de l’acte ou de la règle de droit.") and the live banner, both
  claims the owner may revise; `node web/scripts/refusal-sentences.mjs` prints every entry beside its
  English. The entries written after the review were reviewed in its addendum of 2026-10-03, so no
  French entry ships unreviewed (STATUS-WEB.md lists them).

- The rights question the retrieval set surfaced (PR #842) is decided under the owner's delegation of
  2026-10-02: a non-admitting licence keeps its text out of search matching too (STATUS-WEB.md, driver
  decisions).

## Blocked on the owner

Only money, legal or public claims, credentials and going live (ruling 7):
- Azure, for V3: a new subscription on 2026-10-13. V2's subscription stays disabled (since 2026-10-02), so V2 at
  law.soufien.lu stays down. The owner's Gmail account could not take V3 either: its billing profile is past due on a
  EUR 6.10 invoice the portal cannot take (only Azure Support can clear it). The owner signed up a third Microsoft account (a free trial; about 30 EUR a month accepted) and, at the owner's request,
  the driver set up, in France Central: a budget of 30 a month with alerts, resource group `rg-lex-v3`, registry
  `crlexv3soufien`, a pull identity, Container Apps environment `cae-lex-v3` and key vault `kv-lex-v3-soufien`,
  which holds the release signing key (P-256, identity "lex-v3-release (Soufien Hajji)"; only its public half is on
  disk). The owner upgraded the trial to pay-as-you-go on 2026-10-13.
- The zero-traffic Azure candidate of release `v3-20261013T214036Z-9a5f05d3d1b4`: the owner ran the deploy on
  2026-10-14 (app `ca-lex-v3`, reachable only from this machine's address, about 26 EUR a month). Since 2026-10-14 08:04
  UTC every Azure call from this machine fails with AADSTS530035 (blocked by the tenant's security defaults). On
  2026-10-14 the owner asked the driver to run the sign-in ("you run az login"): the driver starts a device-code
  sign-in when the owner says "sign in", and the owner enters its code on the phone, this PC on a normal connection.
  Two codes the driver started that day expired unanswered.
- Where DNS lives (O14): **decided by the owner on 2026-10-14.** Asked "Decide where soufien.lu's DNS lives. I recommend
  your registrar, outside Azure.", the owner answered "yes like v2 same url you run az login": the zone lives at the
  registrar, outside Azure (R117: V2 went down with its subscription), and the site keeps V2's address,
  `law.soufien.lu`. The owner points the registrar's name servers to the registrar's own DNS (they point at the Azure
  zone made on 2026-10-14) and adds two records there: `law` as a CNAME to the app's default host, and the `asuid.law`
  TXT verification record, whose value the driver gives. The driver then binds the name with a managed certificate,
  keeps the private copy's address list (it stays private until the opening), and deletes the Azure zone. O14's other
  half, the payment method, is not decided yet (below).
- The plan's decisions (its section 5: each with its recommended answer, its cost and what stops without it):
  - **Now, by Friday 16 October**: the Azure sign-in; the rest of O14 (R117): a valid payment method kept on the V3
    subscription, a backup one, and billing alerts on two channels, so a failed payment cannot take the site down as
    it took V2; O15, an encrypted external disk (1 to 2 TB), so custody gets a second copy before step 0 deletes
    anything; turning hibernation off (6.3 GB).
  - **The decision sitting, by Wednesday 21 October** (one reply approves the pre-filled answers, striking what the
    owner disagrees with; nothing is assumed from silence): O1, adopt the record and one opening with the full launch
    set on 2 February 2027, the four items of the plan's section 4 (tagged PDFs, the export cart across acts, the
    Developers try-it console, the attested cloud build) moved to dated items within six weeks after it; O3, where the
    delegation ends (the owner acts in person for the signing identity,
    production credentials, deployment, promotion, money and public legal claims; a deployment to the private copy is
    the driver's; the publisher runs the plan names are delegated); O2, Lex at `law.soufien.lu` after a name check;
    N1, one money envelope (a ceiling of 150 EUR a month, about 120 expected; the external disk; the rented machine; the
    people's checks on quotes, up to about 19,000 EUR); N2, the checks only people may do, by paid independent people,
    never agents (a Luxembourg lawyer, an accessibility auditor, a French-reading legal annotator, a session moderator
    and ten testers).
  - **By Friday 30 October**: O6, the Luxembourg licence rulings (recommended: L2, L1 and L1b yes for every file, each
    with a visible per-file disclosure; L3 yes in principle; L4 kept withheld; a letter to the SCL); O7, the population
    and its yield; O28, personal data; O5, the legal pages; O4, the lawyer's read as a condition of going live; O26, the
    usability sessions; O13, hosting and money; O16, the signing keys; N3, how outside reviewers reach the private copy.
  - **Later**: by 13 November O19, O10, O11, O20 and the attribution wording; by 27 November O12, O21, O27 and O29; by 11
    December O17, N4 and O18; only if needed, N5 (by 1 December) and N6; at the go/no-go on 27 January, O31 and the
    sign-offs.
- The weekly 30-minute checkpoint slot.
- The owner's rulings of 2026-10-04 (relayed by the audit session): the French copy accepted, the acquisition
  code frozen until the combined mount exists, the stage milestones closed, `OWNER-BRIEF.md` kept current.
