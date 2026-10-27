# Lex V3 status

Updated 2026-10-26 by the driver. This file holds the heads, the owner's open items and the pointers to the two
lane files (the standing order of 2026-10-01 13:50 UTC: STATUS.md was edited by nearly every pull request and was
the most frequent conflict). Each lane writes its progress in its own file, in the pull request that makes it:

- `STATUS-WEB.md`: the web lane (Claude): what is served, the API and the screens, the release path, the plan's
  items 4 to 8, the driver decisions, what waits on others, known defects.
- `STATUS-DATA.md`: the data lane (Codex): acquisition, custody, derivation, the populations, the plan's items 1
  to 3.

A feature pull request edits its lane file, not this one; this file changes for the heads and the owner's items.

## Heads

- `v3/integration`: `8958ec5f` (2026-10-26, #987 merged: the data big bang, the reader's interval rules over index /9,
  the catalogue's chain, dates by role, EU force dates, and D1's first fix; #989, the web big bang, and #990, the legal
  pages without a person, merged earlier the same day). Served: the release `v3-20261026T180319Z-8958ec5fa63f` over
  derive-c, on the private copy at law.soufien.lu since 2026-10-26 21:00 UTC (STATUS-WEB.md). The opening
  (`deploy.ps1 -Open`) waits on the owner's own command: the driver's safety classifier refuses it. The required checks are strict: `dotnet` (the whole solution, about 14 min on the runner),
  `web`, `canon-windows`, `journeys` and, since 2026-10-15, `image-conditions` (the API's tests inside the release's
  base image). `journeys` walks J1 to J8 and the site's own addresses in Chrome over four fixture mounts (the fixture,
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
- On 2026-10-17 the owner asked for days, not months. The driver's answer: a private beta of the law held today
  first, on the private copy; then the codes, once custody has its second copy. Opening a beta to the public before
  the lawyer's read (O4) and the audits is going live, so it is the owner's decision, asked at the decision sitting
  below. Until the owner answers O1, the opening of 2 February 2027 stays the plan of record.

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
- Azure credits: the subscription's trial credit (175.70 EUR left, seen on 2026-10-17) expires on 12 November 2026.
  From then the private copy of the site (about 26 EUR a month, below) is billed to the payment method on file,
  within the 30 EUR monthly budget the owner accepted; a support plan pays for support only. The owner checks the
  payment method before 12 November.
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
  Two codes the driver started that day expired unanswered. **Unblocked on 2026-10-17:** the owner created, in Cloud
  Shell, the service principal `lex-v3-driver` (Contributor on `rg-lex-v3` only, and Key Vault Secrets User for the
  signing key), which the driver signs in as with a certificate held only on this machine.
- Where DNS lives (O14): **decided by the owner on 2026-10-14.** Asked "Decide where soufien.lu's DNS lives. I recommend
  your registrar, outside Azure.", the owner answered "yes like v2 same url you run az login": the zone lives at the
  registrar, outside Azure (R117: V2 went down with its subscription), and the site keeps V2's address,
  `law.soufien.lu`. The owner points the registrar's name servers to the registrar's own DNS (they point at the Azure
  zone made on 2026-10-14) and adds two records there: `law` as a CNAME to the app's default host, and the `asuid.law`
  TXT verification record, whose value the driver gives. The driver then binds the name with a managed certificate,
  keeps the private copy's address list (it stays private until the opening), and deletes the Azure zone. O14's other
  half, the payment method, is not decided yet (below).
  **Changed on 2026-10-18.** The registrar named the "-06" Azure name servers, which held no zone for soufien.lu (V2's
  old one), so every lookup failed. The owner, who said that day "build everything on azure i can handle more payments
  if necessary", pointed the registrar at the Azure zone's own servers (ns1-05 to ns4-05). So the zone in rg-lex-v3 now
  serves soufien.lu, departing from O14's "at the registrar": R117's risk (the name goes down with the subscription, as
  V2's did) is accepted while the subscription stays funded. law.soufien.lu is bound to the app with a managed
  certificate (`deploy/bind-domain.ps1`); the site stays private until the opening. Moving the zone to the registrar
  later stays possible: the two records are the CNAME `law` and the TXT `asuid.law`.
- The owner's decisions of 2026-10-18 (the owner's words: "yes yes go with what you recommend", "Opening to the public
  once we finish v3", "build everything on azure i can handle more payments if necessary ill start paying after my
  credit end", "you have 24 hours max to finish v3"):
  - the EU authenticity wording for acts published before 1 July 2013, as #975 recommended (the act's own date
    chooses, since the EU index holds no Official Journal publication date: STATUS-WEB.md, "The EU authenticity line
    by the act's date");
  - O27, search-engine indexing: yes, from the opening, law pages only for the acts R92's measurement clears;
  - the opening: when V3 is finished, without waiting for the lawyer's read (O4) or the audits;
  - money: Azure storage for the custody's second copy and Azure machines for heavy builds, paid after the credits end.
- The owner's decisions of 2026-10-19 (the owner's words: "do what you recommend", relayed by the coordinator; one
  commit each in `writer/web-bigbang`, STATUS-WEB.md): O11, the maximum data age, 30 days (option A); e-mails to the
  contact address kept 12 months once their matter is closed; the accessibility statement declares Lex non-conformant
  until an audit, not partially conformant. The e-mails' 12 months were superseded the same day by the bullet below.
  - The legal pages: the site names no person and publishes no postal address, no e-mail address and no contact channel
    of any kind (the owner's words: "i dont want to mention nobody name etc etc including me"; "the user is there to have
    temporal feature over lux and eu laws thats it"; "why r we mentioning emails andadress"). The owner's legal decision,
    made; it departs from the specification of record's R49 (an editor, an address, an e-mail, a director of
    publication, a controller and a contact, an objection contact; and with it R62's and R74's contacts) and from O5's
    recommended answer (the owner as editor and controller, with a dedicated contact address). With no contact, no
    e-mail is received or kept. `web/scripts/legal-publisher.mjs` is removed: nothing waits on the owner for the legal
    pages, and the opening's check now refuses a page that shows an e-mail address, a contact link, a placeholder or a
    postcode (`writer/legal-no-person`, STATUS-WEB.md).
- The plan's decisions (its section 5: each with its recommended answer, its cost and what stops without it):
  - **Now, by Friday 16 October**: the Azure sign-in; the rest of O14 (R117): a valid payment method kept on the V3
    subscription, a backup one, and billing alerts on two channels, so a failed payment cannot take the site down as
    it took V2; O15, a second copy of custody before step 0 deletes anything: an encrypted external disk (1 to 2 TB),
    or, offered on 2026-10-17, a private Azure storage container in `rg-lex-v3` (custody measures about 16 GB, under
    0.50 EUR a month in the cool tier; money, so it waits for the owner's yes); turning hibernation off (6.3 GB).
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
    and ten testers); and, asked on 2026-10-17, whether a beta may open to the public before the lawyer's read and
    the audits (going live; recommended: no, a private beta first).
  - **By Friday 30 October**: O6, the Luxembourg licence rulings (recommended: L2, L1 and L1b yes for every file, each
    with a visible per-file disclosure; L3 yes in principle; L4 kept withheld; a letter to the SCL). Built and switched
    off on 2026-10-27; measured over run 15 under content /3: L2 adds 1,055 files and 64,094 articles, L1 2,196 and
    21,041, L1b 31 and 583; with option (b), the three together add 1,911 and 16,891 (STATUS-DATA.md); O7, the population
    and its yield; O28, personal data; O5, the legal pages (answered on 2026-10-19: no person, no contact, above); O4,
    the lawyer's read as a condition of going live; O26, the usability sessions; O13, hosting and money; O16, the
    signing keys; N3, how outside reviewers reach the private copy.
  - **Later**: by 13 November O19, O10, O11, O20 and the attribution wording; by 27 November O12, O21, O27 and O29; by 11
    December O17, N4 and O18; only if needed, N5 (by 1 December) and N6; at the go/no-go on 27 January, O31 and the
    sign-offs.
- The weekly 30-minute checkpoint slot.
- The owner's rulings of 2026-10-04 (relayed by the audit session): the French copy accepted, the acquisition
  code frozen until the combined mount exists, the stage milestones closed, `OWNER-BRIEF.md` kept current.
