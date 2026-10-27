# Lex V3 status: the web lane

Updated 2026-10-27. The web lane's progress, split out of STATUS.md (which keeps the heads, the owner's open
items and the pointers) by the standing order of 2026-10-01 13:50 UTC. Every pull request of the web lane
updates this file, not STATUS.md.

## R77's records of the acts only the catalogue holds, and R57's wording on Compare (sole driver, 2026-10-27)

The audit of 2026-10-27 (`C:\lex-v3\AUDIT-2026-10-27.md`), gaps 1 and 2, as one pull request (`writer/catalogue-and-compare`,
on v3/integration a0958bb9, #1001's names record included; v3/integration 404bfc9a, #1002, merged before it was handed
back).
- **R77.** An act the Luxembourg catalogue holds and the index holds no version of answered `identifier_unknown`, with no
  record. It now answers `text_not_available`, the code R77 names (no registry change), with its record read from the
  catalogue's facts the index already holds (`work_facts`; `V3CorpusMount.CatalogueRecords.cs`): its identity and the address
  this site gives it (`/lu-legilux/{key}`, as the index would mint it), its type, its dates by role (dateDocument,
  publicationDate, dateEntryInForce, dateNoLongerInForce, each the publisher's, R21), its status in the publisher's word
  (the act's `inForceStatus`, never a consolidation's), its Mémorial issue, historicalLegalId, official PDF, title, its
  versions, and why no text of each is shown, by the reason Coverage counts it under (one rule, `V3TextYield.HeldFile` and
  `CatalogueNotHeldReason`): for a version whose file the corpus holds, that file's bucket (`file_withheld` with its rights
  disposition, `file_pdf_only`, `file_no_article_served` with its dispositions, `file_articles_no_state`), and for one of which
  no file is held, what the catalogue states (`catalogue_licence_scl`, `catalogue_cc_by_not_acquired`, …), never why the
  acquisition did not fetch it; and the publisher's own word for its type and status (`document_type_codes`,
  `in_force_status_codes`, the last segment of each authority IRI), which a page says. Every per-work operation (the dossier,
  reading and the rest), `resolve` and search's exact reference (a reference, or this site's address of the act) answer it;
  it needs no new derive and reads index /9 and /8 alike.
  - **R92 first:** an act the names record withholds answers `text_withheld` (`names_private_individuals`) with no title and
    no record, as every answer about it does (a test measures a catalogue act whose title grants naturalisation).
  - **Screens:** every screen says one sentence (`CATALOGUE_RECORD_SENTENCE`), and the card says why no text is held and each
    fact by its role, in English and French (`refusal-card.mjs`, `catalogueRecord`); the dossier shows the act and its title
    in its language, and links the record's page.
  - **Measured, read-only** (after the repair, by the act as published's reason, else the first version's): derive-r1, 22,049
    acts gain a record: 16,330 `catalogue_licence_scl`, 5,673 `file_withheld` (each `typed_quarantine_in_file_reading_rejected`),
    34 `file_no_article_served`, 10 `catalogue_cc_by_not_acquired`, 2 `catalogue_no_expression`; R92's record withholds 974 of
    the catalogue's acts (STATUS-DATA). derive-c, 22,891: 16,330, 5,675 `file_withheld`, 665 `file_no_article_served`, 209
    `file_articles_no_state`, 10, 2. Of derive-r1's, 22,040 state their dates, status and issue, 19,679 a historical
    identifier, 6,467 an end of force.
- **R57.** `diff` carries the served text of each changed, added or removed article, where that side's rights admit it, and
  null otherwise (`text_rule`; the digests still decide the status). The compare page shows each change inline, word by
  word (`word-diff.mjs`): the text deleted struck through, the text inserted underlined, each also said in hidden words,
  a jump list and the previous and next change; the digests, permalinks and date roles stay. A comparison has its own
  address (`/{lang}/compare.html?work=/lu-legilux/{key}&from=…&to=…[&language=…]`), which both servers render from `diff`
  (`SitePageFor`/`sitePageFor`, one table), so it reads without JavaScript. EU diff, article_history and changes_in_period
  stay typed refusals.
  - **Driver decision (reversible), the compare form and R109:** the first draft named the form's controls and made it a GET
    so a submit with no script would ask the comparison's address; the web suite's R109 hold ("no word a reader types
    reaches an address a server could record") refused it, and the decision is reversed. The form's controls carry no name,
    as every form's do. Once a comparison is asked, the screen writes its address into the page's query only when its work
    is this site's address of an act, in any form the API reads as one (`/lu-legilux/{key}`, under an interface language
    or this origin, written as `/lu-legilux/{key}`: `compareQueryOf`), with the two dates and the language: a law's address,
    never a word, pinned in `request-addresses.test.mjs`. The server renders a comparison's address for such a work alone.
    The page's introduction says so (the French review's supplement).
- **Pins moved.** The envelope, answer and refusal-payload samples re-rendered (`V3_RENDER_*`) and verified without: three
  new envelopes (the record by the dossier, reading and search), the payload census samples the record, the two-state
  diff carries its texts. The legacy base answers recorded again on Linux at ee44bf68 (9 of 53 rows, each by R77 or R57
  alone), at a9179b39 (the five schema-6 records by their two code members alone) and at caaff24d (the repair: all 38
  legacy rows and the 15 EU schema-5 rows by the registry digest, since diff's request schema gained `page`; the schema-8
  diff by its `text_page`; the five schema-6 records by F1's reason); Windows replays the same bytes.
- **French** (Decision 41): the 32 new or changed forms were reviewed by an independent reviewer (Claude, AI reviewer, not the
  author) in the addendum of 2026-10-27 "R77's catalogue records and R57's compare wording" (`C:\lex-v3\lanes\fr-review\review.md`):
  20 approved, 12 changed in French and 8 of them in English too, 5 blocking and each a truth finding (a compared side is named
  by its date's label, since the page does not order its dates; a comparison shown whole keeps the beginning and end its
  texts share; a licence reason names the XML and PDF files it reads, never every file; an entry into force is a labelled
  line, since two records carry two dates). Every final applied byte for byte and read back (0 differ); the English taken:
  the blocking finals, the recommended ones of 17 to 19 and the optional one of the intro. Not taken (reversible): ordering
  the two dates, setting the status token apart, a spoken end marker. Its supplement (the intro after R109, and the English
  "This article's wording", since "wording of" is the EU's) changed the intro in French with its recommended English, and its
  blocking point was taken by the preferred fix (every form of an act's site address written as the bare one). Its
  supplement 2 (the repair) reviewed 13 forms: 8 approved, 5 changed in French, 2 blocking in English ("The wording of the
  changes" spoke the EU's "wording of": "The wording that changed"); every final applied byte for byte, the optional English
  of A4 and A7 taken, and on its advice `refusalCard.textless` and `textlessNamed` say "shows" too.
- **For the data lane** (the review's point 3): 12,040 of the 16,330 expressions whose XML and PDF files are licenceSCL also
  carry an HTML or DOCX file under CC BY 4.0 in the catalogue. Whether Lex could admit those files is a question for R83 and
  the data lane; nothing here claims it.
- **Counts** (this PC, one heavy step at a time, after the repair, at c3c838c3 over v3/integration 404bfc9a): `dotnet build
  Lex.V3.slnx -c Release`, 0 warnings. Lex.V3.Tests, whole: 3,375, 3,369 pass, 6 skip. Lex.V3.Ingest.Tests, every mount class
  (`V3Corpus*`), the text yield, the three sample censuses, the machine gates, the EU predicate census, PublicRequestRecording,
  the index builder and #1002's release checks: 639, 634 pass, 5 skip (28 min). `npm run build`; `npm test`: 1,473, 1,470
  pass, 3 skip (the real-browser tests). `eng/verify-v3-tree.ps1`: 1,788 tracked paths verified. (Before the repair, at
  6d4c4c02: 3,375; 632; 1,471.)
- **Review and repair (#1003, one round; the review by the other model family).**
  - **F1 (truth):** the record said "not acquired" of acts whose file the corpus holds (derive-r1: 5,707 of them). The mount now
    reads the corpus's held Luxembourg files by work once, and the record gives such a version its file's bucket with its
    disposition, by the rule Coverage counts it with (`V3TextYield.HeldFile`, shared), and the held file's body receipt as
    `retained_transport_evidence`; `catalogue_*` reasons stay for versions of which no file is held. `what_would_answer`:
    `expanded_official_scope` for `catalogue_cc_by_not_acquired` alone, `new_official_observation` otherwise. A test holds the
    record and Coverage equal over every case (`V3TextYieldTests`), one the schema-6 fixture's act (acquired, its four
    articles held and no state: `file_articles_no_state`, as Coverage counts it with text). The card and the census sentence
    say "shows none of its text", not "holds".
  - **F2 (performance):** an address with no work's shape (`/lu-legilux/rgd`, a year, a day) is `identifier_unknown` before any
    read of the catalogue (`IsWorkShape`, R92's `LuxembourgWorkIriOf`), by the record and by the exact reference's catalogue
    names; a counter proves no range is read.
  - **F3 (truth):** a work with no act as published (a code) has empty act-level lists; `CatalogueRecordRule` says so.
  - **F4:** `diff`'s texts are cut into pages as a reading's (50 articles, 98,304 bytes of both sides' texts): `page` asks one
    (the request schema gained it, so the registry digest is `627879b6…`, was `befb867f…`; the envelope test's pin moved with it), `text_page` names it, and a page past the last is `anchor_not_in_version` (with no page cut,
    its nearest anchor is `page=1`). Compare names the page by its first and last article, links the pages around it
    (`page=N` in the comparison's address, which both servers render; a button where the comparison has no address), and says
    of a change on another page that its wording is there.
  - **Driver decisions (reversible):** `file_articles_no_state` is the record's name for Coverage's `files_with_text` where the
    index holds a file's articles and no state (derive-c: 209, derive-r1: none); Coverage still counts those articles as
    served, which is Coverage's wording and outside this round. The textless forms say "shows" too (the French review's point).
- **Not done:** R57's side-by-side view and its swap control; R56's gap rows on the reading's version list.
## Review and repair (#1004): treaties never out of the population, How to read Lex as narrow as index 8 (sole driver, 2026-10-27)

The independent review of #1004 (`writer/truth-and-coverage` at b02c978a) said REPAIR. The one repair round:
- **F1 (high).** Over derive-c, a held treaty (`12016E`, `CELEX:12016P`, `12012E`) and an article of one (`12016E101`) answered
  `out_of_corpus_scope`: the index holds a treaty by its "/TXT" part. A sector-1 CELEX is now never read as an act outside the
  population (`identifier_unknown`, as before this branch), and any act held by its "/TXT" part counts as held. The scope test
  holds five treaty forms over dossier, resolve and timeline.
- **F2 (medium).** How to read Lex said Lex names a textless version and why where it holds another version; index 8, which
  the live release mounts, names none. The sentence is dropped, and the last says "Lex never shows another version's text
  as that version's" (on index 8 an act held as published answers later dates, labelled as published): only what holds on
  every index, with or without R77's records.
- **F3.** The R80 reason names the population only ("The EU population Lex's rule selects is the N acts of its seed list
  (Decision 84), and X is not one of them."): 12 of derive-c's 82 seed acts hold no wording.
- **F4.** CODE_RECUEIL is no longer declared "not collected" (run 15 collected its catalogue records with the codes); RCSF and
  RBCL are admitted and awaiting a run, with no order claimed (O7, the owner's, is open); `admitted_first_awaiting_a_run` is
  retired, and the rule names no role.
- **F5.** The select and the checkbox stand in the controls' 44 px rule.
- **Also said in the PR**: every `out_of_corpus_scope` law page is a 404 in both servers, the out-of-scope list's classes
  included, and both still render the refusal card.
- **French**: the one changed form reviewed by one independent reviewer (the supplement "the review of #1004, F2" in the
  record), its recommended pair taken in both languages, byte for byte.
- **Counts**: `dotnet build Lex.V3.slnx -c Release` 0 warnings; `Lex.V3.Tests` whole 3,375 (3,369 passed, 0 failed, 6 skipped);
  the Ingest classes touched with every census class and the legacy replays 194 (191 passed, 0 failed, 3 skipped);
  `Lex.V3.Ingest.Tests` whole 3,592 (3,547 passed, 0 failed, 45 skipped); `npm run build`, `npm test` 1,459 (1,456 passed,
  0 failed, 3 skipped); `eng/verify-v3-tree.ps1` 1,776 tracked paths. No legacy row moved.

## Truth and coverage: the six overclaims narrowed, R80, R79, R78/R82/R12 on Coverage, R62 and R65 (sole driver, 2026-10-27)

The audit of 2026-10-27 (`C:\lex-v3\AUDIT-2026-10-27.md`, "Overclaims") found six public sentences that claimed more than the
code does. Branch `writer/truth-and-coverage`, from v3/integration a0958bb9 (#1001 merged in at the start).
- **The six overclaims**, each narrowed to what is true, in English and French:
  - EU dossier and search: the other-languages row no longer says French is not acquired; it says Lex acquires the English and
    French expressions of EU acts only (Decision 89) and EUR-Lex publishes an act's other language versions where they
    exist (`V3CorpusMount`).
  - Developers G3: the code can keep earlier builds by a retention rule; Lex does not keep them yet, the release holds only the
    build its server mounts, so a version is re-verifiable only while a kept build holds its text (R89; no funding claim).
  - Evaluation card: above its verdict, a line names the gates it holds (from the card) and what it does not hold: the data
    gates, answer quality, speed, two systems, and the refusal codes no operation sends (from the refusal census).
  - Privacy notice: the no-logging test runs on the server's own code before deployment; the hosting is set up from a template
    with no log store; its settings once deployed are not tested this way. Driver decision (reversible): narrowed rather than
    pinned in `main.bicep` with a probe check, which needs the managed environment in the template and Azure to test.
  - How to read Lex: no longer promises that a textless version is named, or why (the review's F2, above); it keeps the
    reasons a version can lack text and that Lex never shows another version's text as that version's.
  - Home page: history and comparison are said of a Luxembourg act of which more than one version is held.
- **R80**: an EU act the mounted EU population does not hold, named by its CELEX (as typed, after "CELEX:", in an EUR-Lex or
  Publications Office address; sectors 0 to 4; a consolidated version or corrigendum read as its act), answers
  `out_of_corpus_scope` (class `eu_act_outside_population`): the rule with the number of seed acts the index holds, the act's
  EUR-Lex page, the Publications Office as who to ask, no list or digest (`V3CorpusMount.Scope.cs`). Only the request's
  `identifier` is read; a sector-5 document stays `identifier_unknown`; a case stays the list's `eu_case_law`. Every screen
  says it in its own sentence (`OUTSIDE_EU_POPULATION_SENTENCE`), never "a kind Lex never holds". A law page whose answer is that refusal
  is a 404, as an unknown work's is, in both servers (`V3WebRoot.AnswersUnknownWork`, `serve-live.mjs`).
- **R79 (E3)**: the Luxembourg dossier on index 9 carries `force`, the work's and its act as published's own `inForceStatus`,
  `dateEntryInForce` and `dateNoLongerInForce`, verbatim (`V3CorpusMount.Force.cs`); the page says the status token, "Entry into
  force" and "End of force" with the publisher's record, and that this is not a reading for anyone's situation (R21 held).
- **R78, R82, R12**: coverage on index 9 carries `declared_scope`: every recognized Luxembourg class not held (the publisher's
  codes but CODE, LOI and RGD, with the Mémorial B and communal regulations), each with a closed disposition; the EU languages
  held of the 24 official ones; when Lex's own observations begin per publisher (the mounted log's first build, or the EU
  collection's end from the build report, or not stated). Shown in both renderers; R26's EU sentence names no language.
- **R62**: touch targets of 44 px (links in lists, summaries, controls, the info control). **R65**: a copy button under each
  citation form, rendered only after the script finds `navigator.clipboard.writeText`; the server renders none.
- **Not done**: the home page's robots marker per state in the deploy probe (#993's F2): `data/release-checks-ops` owns the
  probe, and the driver's instruction kept this slice out of `deploy-probe.mjs`. ARGD has no name on Coverage (token only).
- **Legacy answers**: the EU schema-5 answers recorded again on Linux (WSL Ubuntu 24.04.4, .NET 10.0.400, invariant
  globalization, UTC) at 81fc8ac1: 15 rows, 3 moved by the other-languages sentence alone. Schema 8 and 6 did not move (the new
  members are index 9's).
- **French** (Decision 41): one independent reviewer (Claude, AI reviewer, not the author), the addendum of 2026-10-27 "truth
  and coverage" and its supplement in `C:\lex-v3\lanes\fr-review\review.md`: 58 forms, 19 changed in French, applied byte for
  byte; the English changed for truth in three (EUR-Lex publishes the other languages only where they exist).
- **Counts** (this PC, Windows, no browser, no publisher traffic, no Azure). `dotnet build Lex.V3.slnx -c Release`: 0 warnings,
  0 errors. `Lex.V3.Tests`, whole: 3,375 (3,369 passed, 0 failed, 6 skipped). `Lex.V3.Ingest.Tests`, whole, once: 3,592 (3,536
  passed, 11 failed, 45 skipped); the eleven held an unheld EU CELEX to `identifier_unknown` and now hold R80's
  `out_of_corpus_scope`; then the classes touched or found failing and every census class: 203 (199 passed, 1 failed, 3
  skipped), that one updated likewise, and the seven EU identifier classes: 52 (51 passed, 0 failed, 1 skipped). `npm run
  build` (30 state pages); `npm test`: 1,459 (1,456 passed, 0 failed, 3 skipped). `eng/verify-v3-tree.ps1`: 1,776 tracked
  paths.

## Review and repair (#998): G2 exact, the change policy's digest, MCP failures, the out-of-scope forms (sole driver, 2026-10-27)

The review of #998 (`writer/api-gaps` at 0bb59e65, the other model family, Decision 95) said REPAIR, with its scripts in the
driver's scratch folder (`g2.ps1`, `oos.ps1`, `validate.ps1`, `legacy_diff.py`). The one repair round, and v3/integration at
05965f14 (#997: the screens' gaps, the deploy safeguards, the maintenance revision) merged in, both sides kept (the transport
failures hold #997's `ServiceInMaintenance` and not the retired `OperationNotServed`; the owner's brief is integration's).
- **F1 (medium), G2 overclaimed.** It said the same request gives the same bytes apart from `observed_at` and `request_ref`,
  but each channel's `age_days` and `stale` and the envelope's `stale` count to the moment asked, and a search that reads an
  exact reference naming no date reads it at today in Luxembourg, so the date, the version it selects and the readable
  address or permalink move with the day. G2 now names each of these, says a server built from the same commit
  (`serving_runtime_source_commit`), and that a request past its client's limit is refused `rate_limited`. Its test,
  `EveryServedOperationAnswersTheSameBytesFromTheSameSnapshotFromACopyOfItAndLater`, now runs on a mount whose build report
  dates its channel (20 days old at the first answer, 50 a month later, past the maximum) with a version whose date falls
  between the two answers' days, adds the undated reference and the home page box's date, copies the build report with the
  mount, and holds the members that move a month later to exactly the set G2 names.
- **F2 (low), the change policy.** "Any change to an operation" is now "an operation's definition"; a fix to how an
  operation behaves that leaves its definition as it is keeps the digest; the out-of-scope list is not part of the registry,
  and every `out_of_corpus_scope` refusal names its identity and digest (`scope_list`, `scope_list_sha256`).
- **F3 (low), MCP.** The instructions said every failure is a JSON-RPC error; an HTTP request the endpoint refuses or cannot
  answer (not a POST, a body over the limit, a failure of the server's, maintenance) is an HTTP status with a problem
  document. They now say both. An answer the server's own schema refuses on the way out was JSON-RPC -32602 "Invalid params"; it is now
  -32603 "Internal error" (`V3TransportResponse.IsServerFailure`: a kind REST answers 5xx), and a request its schema refuses
  stays -32602 (`V3McpJsonRpcTests`).
- **F4 (low), out-of-scope forms that slipped through.** An identifier is read as typed: the dashes U+2010 to U+2015 and the
  minus sign as the hyphen, a "CELEX:" before it or an EUR-Lex address as the CELEX it names, a closing full stop dropped.
  Recognised now: "Circ. CSSF", "telle que modifiée" / "as amended", CSSF-CPDI circulars, IML circulars (the CSSF's
  predecessor, published by the CSSF; the class's instrument and reason say so), "CSSF-Rundschreiben", "Délibération n° …
  de la CNPD", case names after the number, joined cases, "affaires jointes", a curia case address. Driver decisions: a
  numbered "Délibération n° 36FR/2021" that does not name the CNPD stays unrefused (the list names the issuing body, and a
  commune numbers its délibérations too); "C-1/16", which the review's script listed among held forms, is a Court of Justice
  case number and is refused as `eu_case_law`; a RESA reference is `RESA_{year}_{number}`, so "resa" and "RESA 2016" are no
  longer refused, and "RESA" alone (the recueil) still is. Every form of the review's script is in `V3OutOfScopeTests`, both
  ways; nothing held is refused. The list's digest moved (`d473256c…`), so the envelope and refusal censuses are
  re-rendered; the registry's digest did not, so no legacy answer moved (replays unchanged).
- **F5 (optional), the real validator.** `V3PlatformHostTests` holds the published payload schemas themselves refusing a
  payload that breaks its code's schema (who to ask in words, an http official place, no reason, `retry_after` 0, not
  retryable, no modes) over REST (500 `internal_response_invalid`, no envelope) and MCP (the host's failure), and passing the
  payload as produced.
- **French** (Decision 41): the two changed French forms (G2, `developers.changes.digest`) are drafts; one independent
  reviewer (Claude, AI reviewer, not the author) is reviewing them, and its finals and addendum are still to be applied and
  appended to `C:\lex-v3\lanes\fr-review\review.md`.

## The API's gaps: every operation routed, a schema per refusal payload, what Lex never holds, MCP ping, and the Developers page's guarantees and live examples (sole driver, 2026-10-27)

The owner's order of 2026-10-27 (finish V3 to the specification of record within 72 hours, at high quality): the plan's items
8.6, 6.10, 8.5 and 8.4, as one web pull request (`writer/api-gaps`, on v3/integration with `writer/help-and-developers`
merged at its start, since the Developers page it extends is that branch's, and v3/integration at 473a9fae, #995, the
per-client limit, merged before it was handed back: both sides kept).
- **8.6, the three operations served from no data** (`concepts`, `knowable_on`, `transposition`). The specification of record,
  §3, API and MCP: "All 27 registered operations are served, or refuse with the typed reason their capability manifest
  states." Each now has a REST route and an MCP tool (`V3RestRouteBinding.Refusing`; `Routed` is the 27) and answers a
  request inside a normal envelope with `retrieval_mode_unavailable` (an identifier the out-of-scope list names is refused
  out of scope first, and a request past its client's limit `rate_limited`): `requested_mode` (`r5_concepts`, `r6_knowable_on`,
  `r4_transposition`, the lanes of `34-ideal-architecture.md` §3), `available_modes` (the modes of the same lane this
  server serves) and `data_needed` (`V3UnservedOperations`). Attribution follows the identifier when one is named. With no
  corpus mounted they refuse `no_corpus_mounted`, as every operation does.
  - The transport failure `operation_not_served` is retired (`V3TransportFailureKind`): no registered name is a failure below
    the envelope; a path nothing names is `unknown_route`, as before.
  - Both index builders state the three with the new reason in every capability manifest they write; a manifest written
    before still reads (`V3UnservedOperation.OperationNotServed` is kept for reading). `coverage` names each with its
    `reason` and `requested_mode` beside `data_needed`, and its note says so; `refused_for_eu` leaves them out, since the
    rows above state them for every identifier.
- **6.10, the refusal registry completed.** The registry is raised to `lex-v3-operation-registry/2`:
  - each refusal code names its payload's schema, `lex-v3-{code}-payload/1`, and that schema's digest, which the registry's
    digest covers. The 20 schemas are generated from the registry (each requires exactly the code's mandated fields, in
    the registry's order, typed as every producer sends them, read from a survey of the 100 constructions in
    `src/Lex.V3.Api`; the four codes no operation produces say so in a `$comment`), published beside the envelope schemas
    (`/schemas/v3-platform/{code}-payload.json`), and embedded in the API, which checks each digest at load and validates
    every refusal's payload against its code's schema before it leaves (`V3PlatformSchemaDocuments.ValidateRefusal`). The
    refusal schema lists the 20 codes. The refusal census holds every payload it observes to the published file too.
    `rate_limited` (#995's per-client limit) is typed as the limit sends it: `retry_after` a whole number of seconds from 1,
    `retryable` true; the three operations served from no data are limited alike (the census drives all 27).
  - `out_of_corpus_scope` carries the four elements `36-ideal-evaluation.md` §4 mandates: `requested_identifier`,
    `official_source`, `reason` (one sentence) and `who_to_ask` (`{name, link, role}`, an institution or an official
    service and never a person: the owner's decision of 2026-10-19), with `out_of_scope_class`, `instrument`,
    `what_would_answer` (`expanded_official_scope`), `asserts_absence_of_law: false` and the list's identity and digest.
  - The out-of-scope list (`V3OutOfScope`, `lex-v3-out-of-scope/1`, published in the census's contract block and on the
    Developers page): CSSF circulars (the CSSF), CNPD decisions and opinions (the CNPD), EU court decisions by ECLI, sector-6
    CELEX or case number (the Court of Justice), and RESA publications (Luxembourg Business Registers), each matched whole
    by exact patterns, so a held act whose title names one of these bodies, or a CSSF regulation (admitted by R78), is
    never refused. Every operation that takes an identifier refuses such an identifier before the mount is read
    (`V3ApiHandler.ExecuteFor`), over REST and MCP alike. J5 ("Circulaire CSSF 20/747") now refuses
    `out_of_corpus_scope` naming the CSSF, and the dossier page's card shows the official place and who to ask as links on
    the list's hosts only (`OUT_OF_SCOPE_HOSTS`, held to the census).
  - Not listed yet, each said on the Developers page: communal regulations (no single official place; each commune
    publishes its own, so the entry waits for R42's list, N2); Luxembourg court decisions (no identifier names one exactly,
    and the Constitutional Court's are admitted later as ACC, R78); EU acts outside the EU population (R80: the EU index does
    not state its population's rule, so an unheld EU act still answers `identifier_unknown`). The official places go to the
    lawyer with the other named places (O4).
- **8.5, MCP basics.** `ping` answers an empty result; `initialize` carries `instructions` (the MCP specification,
  2025-06-18): every tool answers one envelope, a refusal is an answer and not an error (`isError` false), the two clocks
  are never merged, an absence is not evidence that a law does not exist, nothing here is advice. Every registered
  operation is a tool (27).
- **8.4, the Developers page.** New parts: live examples; replay guarantees G1 to G5 as this server supports each, with the
  `V3ReplayGuaranteesTests` method that holds it (G4 says no observation time is held, so `knowable_on` is not served; G5 says
  what an answer does not let a reader recompute on index /9); how the API changes (versioned identities, additive
  members, the registry digest, closed sets, no notice period, the registry's version history). The refusals part links
  each code's payload schema with its digest and lists what Lex never holds; the access part quotes the MCP instructions;
  each operation served from no data names its refusal and mode; every operation is an MCP tool.
  - Live examples from the release's own data: both servers render the Developers page, as they render the home page,
    from their own answer to `browse` with `limit` 1 (`V3WebRoot.SitePageFor`, `sitePageFor`, the site's table), and the
    page shows that request, the answer whole, and the next requests about the first act held (its dossier over REST, its
    text on its latest version's date over MCP). The renderer is handed the contracts as data at build time
    (`__LEX_DEVELOPERS__`), since it reads nothing. The page as built says it carries none; a render that fails serves it
    as built.
- **Pins moved, and why.** Every envelope names the registry: the registry digest is
  `befb867f362ad3a9f44fd4c88f7eb7a75a626307c06cc4f4d6a4247a5f5c90b7` (was `80dc8e29…`).
  - The four censuses were re-rendered with `V3_RENDER_*` and verified without it: `envelope-samples.json` (49 envelopes,
    three new: browse for the first act, `concepts` refused, J5's circular refused; the contract block gains each code's
    payload schema and the out-of-scope list), `refusal-payload-samples.json` (`out_of_corpus_scope` produced, by four
    operations; `retrieval_mode_unavailable` by three more; with #995's `rate_limited`, 16 codes produced and 4 not), `answer-samples.json` (the registry
    digest, coverage's rows) and the 20 schema files (`V3_RENDER_PLATFORM_SCHEMAS`, 76 documents).
  - **The legacy base answers moved, recorded on Linux only**: the schema-8, schema-6 and EU /5 answers, in WSL (Ubuntu
    24.04.4, .NET 10.0.11, `DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=1`, `TZ=UTC`), with the gated recorders, from an archive of
    9b28c5c3. A script checked all 53 rows (29, 9, 15): the same request, trace and status, and each body equal to the
    fixture's once the registry identity is put back, coverage's operations note and `not_served_data` aside;
    `refused_for_eu` did not move. Recorded again at 320c0f20, after the merge of #995 (the `rate_limited` payload schema
    moved the digest once more): every row by the registry digest alone. Windows replays the same bytes (12 of 12).
- **Tests.** New: `V3OutOfScopeTests` (3: every class's examples and J5, nothing held refused, institutions and links),
  `V3CorpusScopeMountTests` (3: the three refusing operations over REST and MCP with identical envelopes, on a Luxembourg and
  an EU-only mount; the circular refused by all 23 operations that take an identifier, identically over both), and in
  `V3PlatformSchemaTests` each payload schema held to the registry. Changed: `V3PlatformHostTests` (every registered name
  routed, the three refusing inside an envelope, `operation_not_served` retired), `V3McpJsonRpcTests` (instructions, ping,
  27 tools), `V3McpHttpEndpointTests` (all 27 tool calls identical to REST), the coverage, census, builder and vocabulary
  pins. Web: `reference-pages.test.mjs` (payload schemas and digests, the out-of-scope list, the instructions and the replay
  tests held to the C#, the live examples), `live-ssr.test.mjs` (the built renderer renders the page from browse), the
  coverage page's two new columns, J5's expectations, the card's out-of-scope payload. The refusal gates (the fixture card's
  and the mount's) gain the circular, its twin (a CSSF regulation, `identifier_unknown`) and an operation served from no data.
- **Counts** (this PC, targeted, as the lane's rules ask; CI runs the whole suites and the journeys). `dotnet build Lex.V3.slnx
  -c Release`: 0 warnings. Lex.V3.Tests, 131 of the classes this slice touches (registry, schemas, out-of-scope list, host,
  envelope JSON, every census, capability manifests, web root, the limiter, host answers): 131 pass. Lex.V3.Ingest.Tests:
  94 (scope, MCP, coverage, the limit, public request recording, locator, ask, as published, EU capability, both refusal
  gates and the card, the partition and vocabulary pins), the three censuses (7) and the legacy replays (12, the two
  recorders skipped): all pass. `npm run build`; `npm test`: 1,400 tests, 1,396 pass, 3 skip (the real-browser tests, CI
  only) and 1 fails here alone, `image-run.test.mjs`'s mount table, which spawns `awk`, absent from this PC's PATH (it fails
  so on writer/probe-search-engines too; CI's Linux has it). `eng/verify-v3-tree.ps1`: 1,737 tracked paths.
- **Driver decisions (reversible).**
  - `retrieval_mode_unavailable` is the typed reason for an operation served from no data, not a new code: the registry's
    set of 20 is closed (the launch contract's refusal line), and the code already means "this mode is not served here, these
    are", as it does for an EU identifier.
  - The out-of-scope check runs before the mount for every operation, so the same instrument gets the same refusal
    wherever it is asked; `search`'s text query is not matched (only its `identifier`), so a phrase is never refused.
  - Who to ask is the institution that issues and publishes the instrument (role `information`), not an adviser: R42's
    handoff list of advisers still waits for a person (N2).
  - The live examples are rendered by the server at request time rather than built into the release, so the page always
    shows what the serving release answers.
- **For the owner** (not asked, recorded): the four official places and institutions are public bodies' own sites, named
  from the driver's knowledge, with no site contacted; the lawyer's read (O4) covers them with the other named places.
- **French** (Decision 41): one independent reviewer (Claude, AI reviewer, not the author) under the owner's delegation, in
  the addendum of 2026-10-27 "the API's gaps" and its supplement, appended to `C:\lex-v3\lanes\fr-review\review.md` with the
  driver's note: 45 forms reviewed, 29 changed (the English too in 13), applied byte for byte. Its blocking points, both
  languages: G1 and G3 verify an earlier version only while a kept build holds its text, a replaced file is recorded whether
  or not its text changed, a referenced build is one listed as referenced (no permalink names a build), nothing is deleted
  "from the log"; G5 recomputes the version's own digest from the answers since S7a (the test's summary, which said
  otherwise, is fixed); in French, an operation without data is "non servie faute de données", never "servie sans
  données". Outside the forms, the MCP instructions now say a Luxembourg state's date is its applicability date or, by its
  date role, the act's entry into force, never an applicability date; and the change policy gains a form for the
  registry's version inside the envelope (`registry_schema`), which a new registry version changes without raising the
  envelope's own. `reference-pages.test.mjs`'s last test pins the review's points beside the C# they describe. Only the
  dossier has a sentence of its own for `out_of_corpus_scope`; another page names the code.

## Review and repair (writer/screens-gaps): the traffic read before a redeployment, the maintenance page styled, no copy left behind (sole driver, 2026-10-27)

The review of `writer/screens-gaps` at be1c6c22 (the other model family, Decision 95) said REPAIR, with its reproductions in
the driver's scratch folder (`rev/`). The one repair round, and v3/integration at 473a9fae (#995: the per-client limit, the
key set, the host headers, the monitor and the runbook) merged in, both sides kept.
- **F1 (high): the printed pre-build line promoted the candidate.** It named the new candidate as `-LiveRevision`, and the
  template keeps `-LiveRevision` at all the traffic: run before the promotion, it moved every reader to the unprobed
  candidate.
  - Every redeployment, the maintenance revision's included, now reads the app's traffic (and its latest revision, for the
    entry a first deployment leaves) before anything in Azure changes, and goes ahead only when `-LiveRevision` holds 100
    per cent; split traffic or a read that fails stops it with nothing changed.
  - The pre-build line names the revision serving as it is printed and says it is run after the promotion, naming the
    revision that serves then. "which keeps all the traffic" is said plainly.
  - And the gap before this pull request: on the open site (no address rule) a deployment without `-Public` would restrict
    it again. It is refused before any change ("The site is open: every deployment says -Public"), and a redeployment whose
    rules cannot be read is refused too.
  - Tests (`deploy-run`): the reviewer's case refused with no Azure write; the pre-build after the promotion goes ahead; split
    traffic, a failed read, the latest-revision entry, a first deployment and a plan run; the open site refused. `deploy-kit`
    holds the read before the image copy and the template.
- **F2 (medium): in maintenance the stylesheet and fonts answered the maintenance page.** The maintenance revision now serves
  the web root's files that are no page (any extension but `.html`) as always, so the page shows styled; and it sets the
  host's security headers on every answer, as #995's API does. `V3WebRootMaintenanceTests` serves `/styles.css` and a font;
  the probe's `MAINTENANCE_PROBES` asks both and holds them to the release image's bytes.
- **F3 (low-medium): `startApi` left its two temporary directories when the copy failed.** The setup is wrapped, so both go on
  any failure. The example searches' derivation links the mount into place (a junction on Windows, a symbolic link
  elsewhere) rather than copying 2.5 GB: Node's removal unlinks a link and never follows it (checked on this machine). The
  journeys keep copying. Test: the reviewer's case and a failure after the link was made leave nothing, the mount whole.
- **F4 (low): the R92 test asserted nothing** (its fixture lacked `short_titles`, the reading was refused). With a valid
  fixture it shows the generator would name the person from the title, and that exactly one example is derived, holding no
  title word.
- **F5 (low): the bundles depended on the build's day.** The examples are derived on the release's source date (the commit's
  time in Luxembourg, `examplesDay`), the reading asked and the citation composed on that day, never by the API's clock; two
  derivations on one day give one record, byte for byte (tested with two API clocks).
- **The open question (driver decision, reversible):** only an act the release's indexing record admits (R92) is featured;
  with no record, none. The journeys give a fixture mount a record of its one act (the test suite's own, naming no one).
- **CI's journeys on #997 (run 40452673971): the J7 walk failed, every schema link 404.** The walk asked the API's origin
  for the schemas; in the journeys whose pages the live server serves, the API holds no web root, while the page's own
  origin served every schema (the API-served runs passed). J7 now follows a link on the page's origin. The build, the
  release image and both servers held the schemas all along; the image probe and the deploy probe now also hold every
  schema a Developers page links to the image's own bytes (`schemaLinkFailures`), and `developers-schemas.test.mjs` holds
  the built directory to every linked schema, byte for byte the repository's, through the live server, with the CI fault
  reproduced.
- **#995 merged in**: `deploy-probe.mjs` reads both `--maintenance` and `--search-engines`; the probe's example searches go
  through `fetchWithinLimit` (the per-client limit); `V3TransportResponse` keeps both `service_in_maintenance` and the host
  headers; STATUS-WEB keeps both sides; OWNER-BRIEF keeps v3/integration's lines.
- **Measured** (Windows, no browser, no Azure, no publisher traffic): `dotnet build Lex.V3.slnx -c Release` once after the
  merge and the C# repair, 0 warnings, 0 errors (a second, incremental, changed nothing); targeted `node --test`, after the
  merge: 28 files (the examples, journeys' verdicts and walks, every deploy and image probe, the home page, the chrome
  tables and scans, the reference and legal pages, and #995's runbook, monitor, limit and client tests), 300 tests, 300
  passed; 18 files of the page renderers, 192 tests, 190 passed, 2 skipped (the real-browser tests, CI's);
  `deploy/validate.ps1` clean; `eng/verify-v3-tree.ps1` 1,725 tracked paths; disk above 5 GB throughout. Not run here: the
  whole `npm test`, the dotnet test suites, the journeys and the image rehearsal (CI's).

## The screens' last gaps: example searches, breadcrumbs, info controls, a print view, the maintenance revision, J6 and J7 (sole driver, 2026-10-27)

The owner's order of 2026-10-27 (finish V3 to the specification of record within 72 hours, at high quality): the gaps the
audit of the site against §3 found (the section below, "The L lines, against what the site serves"), closed as one web pull
request (`writer/screens-gaps`, from `v3/integration` with `writer/help-and-developers` merged in first, so the reference
pages exist).

- **R47, example searches that land on served text** (`home-examples.mjs`, `home-examples-run.mjs`, `LiveHome.jsx`). Derived
  at build from the mounted corpus through the API's own answers, never typed into copy, by a fixed rule:
  - the mount `coverage` counts, whose Luxembourg index's digest the record carries;
  - the Luxembourg acts the release's indexing record admits (R92's measurement), in its order (at most 24), each read on the
    derivation's day (`evidence_bundle`): the release's source date, or the journeys' day; with no record, none (the review's
    repair, below);
  - in each, the first article quoted with text whose usual citation the site's generator composes (`citationForms`, R65),
    the act named by its type and date alone, never by its title, since a title can name a person (R92);
  - that citation asked as the search page asks a search its address holds (strict lane, the text's language, no date), and
    kept only when the exact-reference phase opens Reading at that very article (`usualVerdict` "matches");
  - one per act, at most 3.

  The record (`lex-v3-home-examples/1`) is compiled into the bundles by `build-live.mjs` (`__LEX_HOME_EXAMPLES__`), so the
  page both servers render and the page the browser hydrates say the same; the coverage line now carries the index's digest,
  and the home page lists the examples under it only over that index (never over another mount or none). Each is a link to
  the search page with the search in its address (R109), in its text's language (`lang`). A build handed no record (the web
  suite's, CI's web job) shows none.
  - The journeys derive the record over their mount before they build the pages (an extra start of the API), the home step
    must list exactly it over the mount and none over no mount, and on the fixture mount the derivation must find one.
  - The release (`image-rehearsal.mjs`) publishes the API once more, starts it over the release's mount, derives the record,
    builds both images with it and reports it; the probes (`servedPageFailures`, the image's and the deploy probe's) ask each
    example a home page lists of the revision itself and hold it to the article the page names, and with the rehearsal's
    record to exactly its examples.
- **R48, breadcrumbs** (`breadcrumbs.mjs`, `Breadcrumbs.jsx`) on the dossier and reading screens, in the record's order:
  publisher (the law it publishes), act (work key or CELEX, a link to its dossier), the article the address names when the
  version shown holds it, and the version, said by its date's role (R21: a state applying from its date, an act as
  published dated by its entry into force; an EU wording by its own date). The page's own crumb is current and no link; a
  reading of several versions names none; a page with no answer has none.
- **R48, How to read Lex from each banner's info control** (`GuideLink.jsx`): the staleness banner and the freshness strip
  (to "How current Lex is"), the as-published label and the EU acknowledgement and authenticity statement (to "As
  published, and consolidated") each end with a link to that section, its name its visible words and the section's heading.
- **R62, a clean print view of Reading** (the stylesheet's one `@media print` block): the chrome goes (skip link, menu,
  languages, forms, buttons, pager, contents, info controls, the home page's links, the footer's menus); the text, every
  digest, source and permalink, the citation forms, the version headings with their dates by role, the banners, the
  breadcrumbs and the footer's lines (what Lex is not, which texts are authentic, where the texts come from) and how current
  the answer is stay; black on white in either scheme, full width, digests wrapped.
- **R60, the maintenance page during deployments** (a subagent's slice, reviewed and merged): the release's own signed image
  is the maintenance revision, with no hosting change.
  - The API: `LEX_V3_MAINTENANCE=on` (exactly) opens no preview and no mount and serves only the built maintenance page:
    every page request 503 with `Retry-After: 300` and a page's headers, in the path's language or the negotiated one; the
    readiness 204 only with a page in each language; everything else the new transport problem `service_in_maintenance`
    (503), which the Developers page lists (`V3WebRoot.Maintenance.cs`; the 404 page and it share one choice of page).
  - The template's `maintenance` flag: that switch, all three probes on `/health/ready`, and the revision at weight 0 under
    the label `maintenance` beside the live one; release revisions carry `LEX_V3_MAINTENANCE=off`.
  - The kit: `deploy.ps1 -Maintenance` builds it from a verified release beside `-LiveRevision` (refused without one),
    probes it (`deploy-probe.mjs --maintenance`: readiness, each page's status, headers and bytes against the image's own
    page, the API's problem, no cookie), deactivates it and keeps it (an inactive revision bills nothing), and prints the
    traffic moves that switch it on and off, never run: they are the owner's, as a promotion is (R108 pre-authorises the
    page once O17 is adopted). Every deployment's closing lines print the command that pre-builds its release's one.
- **R53, journeys J6 and J7 walk Get help and the Developers page** (`journey.mjs` `WALKS`, `runWalk`): from the home page, by
  pointer and by keyboard (Tab to the link, Enter, every stop showing its focus), over the fixture mount and a real mount.
  - J6: the reader's question typed into the box and sent (one request, dated today), the sentence under the box that says
    what Lex is not, then Get help, which says R50's sentence, sends the reader to a lawyer or another adviser of their
    choice and to the official texts, and asks nothing (no script, no form, no request to the API, noindex).
  - J7: the Developers page, which asks nothing, held to the API (`developersPageFailures`): its operations and which are
    served against the coverage answer, every schema it links served as JSON, and its REST example and MCP tools/call
    message, sent as printed, one envelope (`envelopeIdentityFailures`).
- **French** (Decision 41): one independent reviewer (Claude, AI reviewer, not the author) under the owner's
  delegation, in the addendum of 2026-10-27 of `C:\lex-v3\lanes\fr-review\review.md` ("breadcrumbs, the banners' info
  controls and the home page's example searches"): 11 forms reviewed, 1 changed (`home.examplesIntro`: "Chaque exemple lance
  la recherche d’un article …, désigné par sa citation usuelle"), 10 approved, nothing blocking. Its optional pair for
  `breadcrumbs.stateEntryIntoForce` ("(publisher)" / "(éditeur)", as the heading says it) is taken, and its material point
  outside the forms too: on the examples' path the search page does not go to the act by itself, so `search.opensReading`
  and `search.opensDossier` now say "This reference opens the reading: {link}" / "Cette référence ouvre la lecture : {link}"
  (and the dossier), never "Opening" / "Ouverture". The thirteen finals are applied byte for byte (checked by a script),
  pinned by `breadcrumbs.test.mjs`, and named with the new vocabulary in the header comment of `live-chrome-fr.mjs`; the
  addendum is appended with the driver's note.
- **Tests** (new: `breadcrumbs`, `print-view`, `home-examples`, `journey-walks`, `deploy-probe-maintenance`, and C#
  `V3WebRootMaintenanceTests`; changed: `freshness`, `date-speech`, `live-chrome`, `live-home`, `journey-verdict`,
  `deploy-kit`, `deploy-run`, `ClosedVocabularyCensusTests`). Measured (Windows, no browser, no publisher traffic, no Azure):
  - the final targeted run, 38 files (the new and changed ones, and every file that renders the pages, the journeys' verdicts
    and the deploy and image probes): 420 tests, 418 passed, 0 failed, 2 skipped (the real-browser tests, CI's);
  - before the French finals, three batches of 44 files the changes reach (search, reading, dossier, history, compare, radar,
    export, coverage, a11y, tokens, render, network gate and the rest; some also in the final run): 479 tests, 0 failed,
    1 skipped;
  - the maintenance slice's own: deploy-kit 17, deploy-run 19, deploy-probe 16, deploy-probe-maintenance 3, no-browser-probes
    11, reference-pages 9; `deploy/validate.ps1` clean (`bicep build` matches `main.json`, `bicep lint` clean);
  - `npm run build`: 30 state pages; `eng/verify-v3-tree.ps1`: 1,706 tracked paths.
- **Not run here** (the owner's order: the PC's disk and memory are scarce): the whole `npm test`; any `dotnet` (the C# of
  the maintenance revision was not compiled here: CI builds and tests it); the journeys and the image rehearsal (no browser
  and no release run on this machine).
- **Driver decisions** (reversible):
  - The examples are usual citations of an article, the form the exact-reference phase is built to open, so each lands on
    the article's text; never a title (R92) and never a word list typed into copy. Luxembourg only: no answer lists the EU
    acts an index holds.
  - An example is a link to the search page with its search in the address; that page shows the reference's link and does
    not go there by itself (the rule for a search read from an address, so Back never loops).
  - The release derives the examples by starting the API over its mount once more: about a minute, with the mount linked
    into place, not copied, and its indexes copied into the API's own temporary folder while it runs (about 2.3 GB for the
    population), removed when it closes.
  - Only an act the release's indexing record admits is featured, and with no record none is: an act whose text may name a
    person is never on the home page (the review's open question, decided here).
  - The maintenance page is served by a maintenance revision of the same app from the release's own image (the plan's item
    10.14's "pre-built maintenance revision"), not by a static host: no new resource and no cost while it is inactive.
  - Release revisions carry `LEX_V3_MAINTENANCE=off` explicitly, so the template's environment stays one list.
- **Not yet**:
  - The live site shows the breadcrumbs, info controls and print view from the next release, and the maintenance revision
    is pre-built from it too (`-Maintenance`). The example searches need more: no release served yet carries an indexing
    record (R92's measurement, the plan's item 1.11, writes it; every release from `data/names-r92` on carries one), so until
    one is served the home page shows none.
  - The Developers page's sentence on failures below the envelope ("a request the server cannot read or route") covers a
    maintenance revision's 503 only loosely; its wording waits for a French review of its own.
  - The JavaScript live server (`serve-live.mjs`, the journeys' and development's) has no maintenance mode; only the API,
    which production runs, has one.

## Review and repair (#995): the runbook's blocks stop at a failed step, renders hold a place, the ingress checked by hand (sole driver, 2026-10-27)

The one repair round of #995 (`writer/platform-ops` at ef99b6cc), after its one review: REPAIR, five findings. Each was
reproduced first.
- **F1 (medium), the runbook's blocks.** As written, the promotion lines (`traffic set`, then `revision deactivate $live`)
  ran on after a failed traffic move: PowerShell does not stop when a native command fails, the pattern of 2026-10-27.
  Reproduced with the reviewer's `promote-block.ps1` and its made-up `az`: the deactivation ran. The rollback read
  "Healthy" once instead of waiting.
  - **Repair** (`docs/INCIDENT-RUNBOOK.md`, section 3): every block that changes something is one `& { ... }` with an
    `if ($LASTEXITCODE -ne 0) { ...; return }` after each command, so pasted whole it stops at the first failure and says
    so. Moving traffic (a rollback, a forward move, a promotion) is three blocks: activate; wait until the target is active,
    running and healthy (ten minutes at most, every 15 seconds), move all traffic, and read it back through `ingress traffic
    show`; deactivate the revision left only when the read traffic is 100 per cent on the target and none on it. The
    deployment block stops when the revision serving now cannot be read.
  - **Test** (`web/test/runbook-blocks.test.mjs`, 9): the marked blocks are pasted into pwsh under a made-up `az` that holds
    the app's traffic, with the test's clock: a failed traffic move switches nothing off (the reviewer's repro); the normal
    promotion's order (three reads while the target warms, two waits, the move, the read-back, one deactivation); a target
    never healthy moves no traffic; a move that does not take is caught by the read-back; an unreadable revision or traffic
    stops its block; a failed activation stops; the deployment block passes `-LiveRevision`; the first half of the
    two-address check. A mutation that drops the read-back guard fails four of them. The reviewer's repro, re-run against
    the new blocks with its made-up `az`: "Moving the traffic ... failed: stop here" and "nothing deactivated", where the
    old lines ran the deactivation.
- **F2 (low-medium), server-rendered pages bypassed the concurrency cap.** `AdmitRender` took a token and no place, so a
  client could run any number of renders at once.
  - **Repair:** a render holds one of the client's four in-flight places while the mount is asked and the page rendered
    (`V3ClientLimiter.AdmitRender` returns an admission; `V3WebRoot.TryServeAsync` holds it, then gives it back). With four
    requests being answered, the page is served as built; a render in flight leaves three places for API requests.
  - **Tests:** `V3ClientLimiterTests.ARenderHoldsOneOfTheClientsFourPlaces…` and
    `V3HostAnswerTests.APageIsServedAsBuiltWhileTheClientHasFourRequestsBeingAnswered`; the census pins the two new doors.
  - **Text:** unchanged. The Developers page's "at most four being answered at once" and "past it [the budget], a page is
    sent as built; no page is refused" stay true, and so does the privacy notice; OPERATING.md says renders hold a place.
- **F3, the platform assumption.** No test of this pull request can see Container Apps' ingress, so whether it forwards
  each reader's own address is an assumption, now said plainly in OPERATING.md ("What the tests do not prove"), and checked
  by hand before each promotion: the runbook's two-address check spends one address's minute at the candidate's own
  address (a 429 must come; none means the limit counts no one), then asks once from a second address within the minute
  (a 200 must come; a 429 means one bucket for the whole site). The coordinator runs it against the candidate.
- **F4, the integration.** v3/integration at 81782325 merged in (#992 deploy-live-safe, #993 the probe's search engines,
  #994 the PDF door): OWNER-BRIEF.md and this file joined by hand, both kept. The runbook and OPERATING.md no longer say the
  deploy fix is unmerged; they give the revision name `ca-lex-v3--<the release's suffix>-<8 hex digits of its settings>`,
  and the kit's rules since #992 and #993 (a revision that exists is activated and probed by hand, a redeployment names
  `-LiveRevision`, `--search-engines` goes to the probe), with the by-hand probe's command.
- **F5:** `V3ClientLimitTests` now proves the mount is not asked: the mount is disposed before the refusals, which still
  come over REST and MCP, while another client's admitted request, which asks the disposed mount, is not answered.
  `V3MachineGatesTests`' summary of `ProducedCodes` is back on its method.
- **Counts** (this PC, at the repaired head; no browser, no publisher traffic, nothing sent to Azure):
  `dotnet build Lex.V3.slnx -c Release` 0 warnings; Lex.V3.Tests 3,348 (3,342 passed, 6 skipped); `V3ClientLimitTests` and
  `PublicRequestRecordingTests` 14 passed; `npm run build` and `npm test` (Git Bash) 1,398 (1,395 passed, 3 skipped, the
  browser tests CI runs); `eng/verify-v3-tree.ps1` 1,713 tracked paths.

## The per-client limit, the key set, the headers on every answer, the outside monitor and the runbook (sole driver, 2026-10-27)

The specification of record's platform and "running it" lines (R109, R46, R116, the shell line and the plan's item 10.7,
R107, R108, R8, R114), on `writer/platform-ops`, from v3/integration at db0a7b53. The site is open since 2026-10-27 01:10
UTC.
- **The per-client limit** (R109, R46; `src/Lex.V3.Api/V3ClientLimiter.cs`; driver decisions, reversible):
  - **Budgets.** The API (`/api/v3/*` served routes and `/mcp`): 60 requests a minute, a bucket of 60 refilled at one a
    second (R109's number; "re-sized by the load test"), and at most 4 of a client's requests answered at once (R109's
    concurrency cap, on every operation: on one vCPU each is a costly one, and a page asks one at a time). The pages the
    server renders from its own answer: 300 a minute (R46: well above), past which the page is served as built and asks
    for itself, so no page is ever refused. Every other file of the site, readiness, the key set and unknown routes are
    not counted.
  - **The refusal.** Past the API budget a well-formed request is answered with the typed refusal `rate_limited`
    (`{"retry_after":1,"retryable":true}`), in the mounted corpus's refusal context, before the mount is asked: REST
    with status 429 and `Retry-After`; MCP's tool call with the same envelope in a JSON-RPC result, status 200 (the
    dispatcher's rule) and `Retry-After`. A malformed request keeps its own transport problem. The web client reads a 429
    envelope only when it is that refusal, which the failure panel shows with its retry (`v3-client.mjs`).
  - **The client.** The connection's address; when the peer is inside the host's network (loopback, private, shared
    100.64/10, link-local: Container Apps' ingress), the last address of `X-Forwarded-For`, the one the ingress appended;
    with none, no client and no count (the platform's probes, the journeys, a release's image run: so no release probe or
    journey meets the limit). An IPv6 address counts by its /64. On the live host the TCP peer is the ingress, so keying on
    the connection alone would have made one bucket for every reader.
  - **What is held.** 16 bytes of HMAC-SHA-256 of the address under a 32-byte key drawn at random per process and replaced
    each UTC day (every entry dropped with it), beside the counts; the address itself is not kept; nothing is written. A
    sweep each minute forgets every entry whose buckets have filled again, so none outlives its client's last request by
    more than about two minutes. At most 100,000 entries; past that a new client is answered without one.
  - **The census of S4-A11** (`PublicRequestRecordingTests`): the address and the forwarded header are read in one place,
    `V3ApiHandler.ClientOf`, handed whole to the limit, and counted by a test of their own; the rules see through that one
    expression and nothing else. The behavioural test now runs every scenario directly and through the ingress, and past
    the limit to the 429, and finds no marked value anywhere.
  - **The refusal census:** `rate_limited` moves from `not_produced` to `produced`, by every served operation (24); five
    codes stay unproduced. The refusal gate's set gains one case, a client past its minute (19 cases; the evaluation card
    re-rendered).
  - **The probes:** the deploy probe asks a candidate through the ingress from one address and can ask about a hundred
    times in seconds (the accented-title check), so its API requests (`askOperation`, `verify`, a step's own request) go
    through `within-limit.mjs`: a 429 is waited out as its Retry-After says, then that origin is paced at two seconds a
    request; a local server is never waited for.
- **The privacy notice** (EN and FR): `privacy.intro` ("records nothing"), `reading.kept`, the new `reading.limit` (what
  is held, where, how long) and `reading.nothingHeld` ("later"); the French reviewed by an independent reviewer in the
  addendum of 2026-10-27 of `C:\lex-v3\lanes\fr-review\review.md` (4 forms, 2 changed, with two English changes taken);
  `LEGAL_TEXTS_UPDATED` is 2026-10-27. Points for the lawyer's read (O4): the fingerprint is pseudonymised personal data;
  the notice states no legal basis for it (the usual candidate art. 6(1)(f)); its first retention period is stated; an
  IPv6 /64 shares one count.
- **The key set** (R116): `src/Lex.V3.Api/well-known/lex-keys.json` (schema `lex-v3-keys/1`), embedded in the server
  and served at `/.well-known/lex-keys.json`, GET and HEAD; the release key, identity `lex-v3-release`, role `release`,
  status `active`, valid from 2026-10-13 to 2027-10-13, key id the SHA-256 of its SubjectPublicKeyInfo
  (`c4e97ce0…`), its PEM committed beside it (`lex-v3-release.pem`, the key release 1 is signed with). No second signer's
  key exists yet (O16).
- **Headers on every answer** (`V3HostHeaders`): before, on https://law.soufien.lu, the pages carried HSTS,
  `Referrer-Policy`, `nosniff` and their CSP with `frame-ancestors 'none'`, but readiness and the API's answers carried
  only `nosniff` and `no-store` (read on 2026-10-27). Now every answer carries HSTS, `Referrer-Policy: no-referrer`,
  `nosniff` and, when it is no page, `Content-Security-Policy: default-src 'none'; frame-ancestors 'none'`; a transport
  problem sets them again after its reset. A page keeps its own reviewed policy.
- **The outside monitor** (R107): `.github/workflows/v3-monitor.yml`, every five minutes on GitHub's runners, runs
  `web/scripts/site-monitor.mjs`: readiness 204, the French home page with its CSP and HSTS, one dated reading page
  (the GDPR in French at 2024-01-01, answer state `success`), `coverage` answered and not stale, and the certificate
  trusted and at least 21 days from its end; a failed check is asked again a minute later, and two failures fail the run
  and open or add to one issue (the run's own token, no secret), closed once the site answers. Run once from this PC
  against the live site: all five ok (certificate to 2027-04-18). It acts only once its file is on the default branch
  (`main`), the owner's choice.
- **The documents:** `docs/INCIDENT-RUNBOOK.md` (R108: classes, the first five minutes, rollback, forward, reactivating a
  live revision, redeploying, blocking a client, the known failure modes with the incident of 2026-10-27, the breach
  procedure, the cost ceiling's command) and `docs/OPERATING.md` (R8: who decides, the review rule, standing
  authorisations, the platform, the domain and certificate, how a refresh and a release are made, the deploy kit's rules,
  freshness and the 30 days, monitoring, money, what is not done). The tree's allowlist admits `docs/*.md` by name and the
  monitor's workflow.
- **The cost ceiling** (R114): the command is in the runbook, for the owner: an action group whose receiver is the Owner
  role, then `az consumption budget create-with-rg` over `rg-lex-v3`, 150 EUR a month (the plan's N1), alerts at 50, 80
  and 100 per cent to the Owner role, no e-mail address (the classic `create` takes no notifications); the JSON form of
  its notifications was parsed by the CLI's own argument code offline, and nothing was sent to Azure.
- **Tests:** `V3ClientLimiterTests` (10), `V3HostAnswerTests` (7), `V3ClientLimitTests` (1), the recording census's new
  count test and its ingress and limit paths, the refusal census and gate set, the Lex.V3.Tests census pins (two guarded
  types, two registries); web `within-limit.test.mjs` (3), `site-monitor.test.mjs` (6), `v3-client.test.mjs`'s 429 case,
  `legal-pages.test.mjs`'s pinned privacy forms and their evidence.
- **Combined with `writer/help-and-developers`** (the merge of 3d8e6f7b; one web pull request): the two 2026-10-27 sections
  of this file and the owner's brief joined by hand, everything else merged clean. The Developers page now says the limit
  as the server enforces it (R46: `developers.limits.rate` and the new `.pages`, their figures from `CLIENT_LIMIT` in
  `developers.mjs`, which `reference-pages.test.mjs` holds to `V3ClientLimiter`'s constants and the 429 of
  `HandleRateLimitedAsync`), its `anonymous` line says what the limit counts in memory, and its citing part names the key
  set (`developers.citing.keys`; its address as printed text, since the API serves it and the pages' own server does not);
  the French of those four forms, and of `developers.access.same`, which the review found contradicted by the 429 and which
  now says it, reviewed in the addendum of 2026-10-27 on the Developers page's limits and the key set (3 changed, the English
  with them: a release of Lex is a "mise en production"; "reconnu à", "refusée avec"; past the limit only an operation is
  refused, over MCP a tool call).
  The operating page stays a repository document (`docs/OPERATING.md`): R8 asks for a numbered page, not a public one, and
  a link from the site to the repository would print its owner's account name, which the decision of 2026-10-19 keeps off
  the pages (a driver decision, reversible).
- **Counts** (this PC, Windows, no browser, no publisher traffic, nothing sent to Azure; at the combined head):
  `dotnet build Lex.V3.slnx -c Release` 0 warnings; Lex.V3.Tests 3,346 (3,340 passed, 6 skipped); the Ingest classes this
  touches with the censuses and the samples (Census, envelope and answer samples, MCP, `PublicRequestRecordingTests`, the
  refusal census, `V3ClientLimitTests`, `V3MachineGatesTests`) 57 passed; the refusal census and the evaluation card
  rendered with their variables and verified without; `npm run build` and `npm test` 1,380 (1,377 passed, 3 skipped: the
  browser tests, run in CI), from Git Bash (from PowerShell the image run's awk test cannot find awk on that PATH);
  `eng/verify-v3-tree.ps1` 1,709 tracked paths; the monitor run once against https://law.soufien.lu, five checks ok.
- **Not done here:** a reviewed sentence for
  `rate_limited` on each screen (the panel says the code today); latency, error-rate and restart alerts; the load test
  that re-sizes the limit.

## Get help, How to read Lex and Developers: the launch set's three missing screens (sole driver, 2026-10-27)

The owner's order of 2026-10-27 (finish V3 to the ideal specification within 72 hours, at high quality): the screens the
specification of record's launch set names (§3, Screens; R42, R46, R48, R50, R55) and the site did not serve, built as one
web pull request (`writer/help-and-developers`). Each page is in French and English, under its language (R61), complete
from the server and readable with no script (R67), never indexed, its footer's freshness strip saying it carries no answer,
as the legal pages are (`scripts/reference-pages.mjs`, `app/ReferencePages.jsx`).
- **Get help** (`/{fr,en}/help.html`, R42, R50). What Lex is, R50's sentence word for word, and that for what a text means
  for a reader they should ask a lawyer or another adviser of their choice; what Lex does not do (apply the law, compute a
  deadline, predict, consolidate, hold case law, choose between versions or fill a gap, keep a record of readers); how to
  read an answer when Lex cannot give the one asked for (the plain sentence first, then what would answer and the official
  link; an answer about what Lex holds, never about the law; a service failure in R60's words); where the official texts
  are (the authenticity line, legilux.public.lu and eur-lex.europa.eu as links); that Lex names no adviser yet, since R42's
  list must first be checked by a person (N2); and, by the owner's decision of 2026-10-19, that Lex has no contact channel
  (no e-mail address, form, telephone or postal address, and no person), so it receives and keeps no message, the privacy
  notice naming where to complain about personal data.
- **How to read Lex** (`/{fr,en}/how-to-read.html`, R48, R21, R24, R58, R64, R65, R87). The two kinds of date (from when a
  text is applicable, in the publisher's words, a Luxembourg version's "applicable from … (publisher)" and an EU act's
  wordings; and when it was published and collected), never merged; each date with its role; as published and
  consolidated, with the authenticity line and the as-published label; versions without text and image-only annexes; gaps;
  two versions for one date; how current Lex is; permalinks, readable addresses and the three citation forms; the keyboard
  (no shortcuts of its own; the skip link). Then a glossary of the site's own words: each term is an entry of the chrome
  table named by its path (`GLOSSARY`), said as the page that prints it says it, a template's term its words before its
  first value, sorted in each language's order.
- **Developers** (`/{fr,en}/developers.html`, R46, R55; the plan's item 8.4). Generated from the contracts, never by hand
  (`scripts/developers.mjs`): the census's `contract` block, which `V3EnvelopeSamplesTests` reads from the reviewed
  registry (the operations, the refusal codes with the fields each payload must carry, the verdicts, the registry's
  digest); the census's coverage answer (which operations this server serves, and the data that would serve the three it
  does not); the request schemas (each operation's parameters) and the envelope schema (its members). REST and MCP and
  that both give one envelope; examples (curl, a tools/call message, an MCP client's configuration); every operation with
  its REST path, its MCP tool, its parameters, its answer's objects and its two schemas, linked and with their digests;
  the envelope, its context and the verdicts; the refusal codes, the two presentation results and the failures below the
  envelope; quoting and citing; the limits as they are (no key, no account, nothing recorded; the request body's ceiling;
  paging; the per-client limit as `writer/platform-ops` enforces it, since the two branches were combined; no availability promised); the events log (polling by
  cursor, at least once, no webhooks, the platform's own delivery statement); and the schemas. The facts only the API's
  code holds (the MCP protocol version and methods, the request ceiling, the transport failures) are named once in
  `developers.mjs` and held to the C# by `reference-pages.test.mjs`.
- **The schemas, published.** Every file of `schemas/v3-platform/*.schema.json` is published beside the pages, byte for
  byte, at `/schemas/v3-platform/{name}.json` (one extension per file, as both servers' path rules require), and the
  Developers page links each with its SHA-256, which the API checks against the registry's when it loads it.
- **Links.** Every page's footer has a "Help and reference" menu of the three, the current one marked; the home page links
  Changes, Trust and Coverage, Developers and Get help (§3, Shell and home); Get help links How to read Lex and the
  Developers page. The site's table (`site-paths.json`) names the six pages as files, which both servers serve
  (`V3WebRootTests`, `legal-pages.test.mjs`, `reference-pages.test.mjs`).
- **Tests** (`test/reference-pages.test.mjs`, 9 cases): the pages built in both languages with the site around them, no
  script, no form, never indexed, language links to the same page, the French saying none of the English's words; every
  internal link they hold served by the live server (pages and schemas, a schema's bytes); every footer's menu and the
  home page's links; the Developers page in sync with the registry (the C#'s `RequiredOperationIds`,
  `RequiredRefusalCodes`, `MandatoryRefusalPayloadFields` and `V3RestRouteBinding.Served` parsed and compared with the
  census, the chrome table and the rendered page; an operation, or a refusal code, the page has no words for fails the
  render); the API facts held to the C#; the glossary's terms the table's entries; R21's guard over every form and every
  rendered page in both languages; Get help's no-contact statement and no contact; every count the page says the
  contracts'; and the forms the French review pinned, beside the C# they describe. Changed tests: `live-chrome`, `live-home`, `legal-pages`, `freshness`, `no-literal-counts` (the new pages and
  sections), `V3WebRootTests` (16 file rows). Mutation: the `verify` description taken out of the English table failed 5 of
  the first 8 cases (the page refuses to render an operation it has no words for).
- **French** (Decision 41): one independent reviewer (Claude, AI reviewer, not the author) under the owner's delegation,
  in the addendum of 2026-10-27 of `C:\lex-v3\lanes\fr-review\review.md` ("the three reference pages, Get help, How to read
  Lex and its glossary, and Developers"): 242 forms reviewed, 59 changed (the English too in 31), 183 approved. Its blocking
  points, both languages: a Luxembourg version's end day ("until the day before the end date Legilux gives it", the end the
  publisher states being the first day not covered, `[from, to)`), and "never chooses between two versions", which holds only
  when their texts differ, since the platform serves one of several held with one text (`SameWording`). Its recommended
  points made twelve other forms say only what the code does (an article held without text is named apart; a permalink says
  first when it shows another text; the freshness strip is a region a screen reader names; the staleness banner stands only
  on a page with an answer; any other REST method gets a failure below the envelope; a refusal's code and payload follow its
  sentence). The finals are applied byte for byte (checked against the reviewer's own file), with its point 6's English for
  `developers.operations.notServed`; its point 8's tests are the ninth case of `reference-pages.test.mjs`; the 242 forms and
  the new vocabulary rows (conseiller, ce déploiement, Streamable HTTP, "sert de réponse pour", "intervalle sans version")
  are named in the header comment of `live-chrome-fr.mjs`; the addendum is appended with the driver's note.
- **Driver decisions** (reversible): the pages stand under each language as every page does (R61), so R55's "/developers"
  is `/{lang}/developers.html`, with no unprefixed redirect; the three are never indexed, as the legal pages are, until a
  release's switch is extended to them; the header menu is unchanged (the home page holds the audience entry points, R47,
  and every footer the three), which keeps the keyboard journeys' path to each form as it is; Get help names no adviser
  until R42's list is checked by a person; the Developers page says the rate limit as the server enforces it (since the combination with
  `writer/platform-ops`, above).
- **The L lines, against what the site serves** (the specification of record's §3 and the plan's "On opening day"): what each L line that names a screen, a page or an address asks, and
  whether the site serves it after this pull request. Each "missing" was looked for in `web/app`, `web/scripts`,
  `src/Lex.V3.Api` and `deploy/`, and is not there.
  - Served, each with its page: Search as the home page (one box, today's date, what Lex is and is not, the dated coverage
    line, the links §3 names), the Work dossier, Reading with its date, Provision history, Compare, Changes, Trust and
    Coverage, the Export composer, Developers, Get help, How to read Lex, the five legal pages, the 404 page and the
    failure panels, the German and Luxembourgish page, the freshness strip and staleness banner, robots.txt and the
    sitemap (with the switch), the evaluation card's JSON, `/api/v3` and `/mcp`, and now the published schemas.
  - Missing on the home page: example searches (R47; no answer names a title or a text a search would land on, so none is
    shown, `LiveHome.jsx`). Closed by the screens' gaps slice (`writer/screens-gaps`, the section at the top): derived from the mount through the API's answers.
  - Missing in the shell: breadcrumbs (publisher, act, article, version; R48); a print view of Reading (R62's fifth item,
    33 §7 rule 9; the stylesheet has no print rules); the maintenance page during a deployment (R60; built, not wired into
    the deploy kit). Closed by the screens' gaps slice (`writer/screens-gaps`, the section at the top): all three.
  - Missing in How to read Lex's reach: a link from each banner's info control (R48); the guide is linked from every
    footer, and no banner has an info control yet. Closed by the screens' gaps slice (`writer/screens-gaps`, the section at the top).
  - Missing in Get help: R42's versioned handoff list by theme (advisers, "where to complain"), and the help block on the
    dossier and reading pages of acts in high-stakes themes (R50). Both need the list a person writes and checks (N2).
  - Missing on Developers (the plan's item 8.4): replay guarantees G1 to G5 as supported, live examples from the
    release's own data, and a stated change policy beyond the schemas' versioned identities and the registry digest; the
    try-it console moves with O1. (Closed by `writer/api-gaps`, at the top of this file, but for the console.)
  - Missing in the API the page describes (each one the Developers page says as it is): a typed refusal inside a normal answer for the three unserved operations
    (`concepts`, `knowable_on`, `transposition` answer the transport failure `operation_not_served`; the plan's item 8.6);
    a published schema for each refusal code's payload (the plan's item 6.10; `refusal.schema.json` holds an open
    payload, and the mandatory fields are the registry's, now listed on the page); "out of scope" with its reason and who
    to ask, and the list of what Lex never holds (6.10, J5); MCP `ping` and server instructions (8.5); the per-client rate
    limiter (R109), which `writer/platform-ops` builds: when the two branches are combined, `developers.limits.rate` (which
    says no per-client limit is applied) must say the limit as that branch enforces it, in both languages, reviewed (done
    in the combination, above). (The typed refusal, the payload schemas, the out-of-scope list, ping and the instructions:
    closed by `writer/api-gaps`, at the top of this file.)
  - Missing addresses: `/.well-known/lex-keys.json`, the published key set (R116; served since the combination); `/.well-known/security.txt` with a
    disclosure policy (R74), which the owner's decision of 2026-10-19 (no contact of any kind) leaves to the owner; a page
    "Operating Lex after launch" with the hosting statement and the monthly ceiling (R8, R113, R114; `docs/OPERATING.md`
    since the combination, a repository document); R55's unprefixed
    `/developers` (the page stands under each language, R61).
  - Missing in the journeys: J6 through Get help and J7 through the Developers page (R53); the CI journeys walk neither
    page yet. Closed by the screens' gaps slice (`writer/screens-gaps`, the section at the top).
  - Not audited by this slice, and each the subject of its own L line: the dossier's relation tabs (R29), the inline diff
    (R57), Changes' export, density notice and watch scope (R54), Coverage's text yield per class, attribution rate and
    retention rule (R86, R29, R89), Reading's notices and in-act links (R17, R68, R71, R72).
- **Measured** (Windows, no browser, no publisher traffic, no Azure):
  - `npm test` (Git Bash), on 7808a5fa: 1,368 (1,365 passed, 0 failed, 3 skipped); before the French finals, 1,367 (1,364
    passed, 0 failed, 3 skipped). `node --test test/reference-pages.test.mjs`: 9 of 9.
  - `npm run build`: 30 state pages (the preview build, which this slice does not change), before the French finals.
  - `dotnet build Lex.V3.slnx -c Release`: 0 warnings, 0 errors; `Lex.V3.Tests` (Release): 3,329 (3,323 passed, 0 failed, 6
    skipped), on the tree of 95935ef7, whose C# (`V3WebRootTests`, the site-paths table) has not changed since.
  - `eng/verify-v3-tree.ps1`: 1,694 tracked paths.
  - Not run here: the Ingest suite and the journeys, which CI runs (no browser on this machine).
- **Follow-ups the review named** (driver points of the addendum): show a reading's `same_text_states` (the other versions
  held with the same text) under the text, so the guide's same-text sentence can say more; R28's collapsed details on the
  refusal card, open by default only on Developers (the card shows its code and payload inline today; the review gives the
  forms to use once it is built).
- **For the owner** (not asked, recorded): the site's host name carries a first name, which the Developers page prints in
  its examples and every page in its head links (the decision of 2026-10-19 names no person in the pages' words); Get help
  narrows "how each article changed" to Luxembourg acts, where `home.intro` still says it generally; the lawyer's read (O4)
  of "un avocat ou un autre conseiller de votre choix" and of R50's "conseil juridique".

## The live revision deactivated by a redeployment, 01:10 to 01:13 UTC, and the fix (sole driver, 2026-10-27)

- **What happened.** The site (the container app `ca-lex-v3`), open to everyone, served release
  `v3-20261026T180319Z-8958ec5fa63f` as the revision `ca-lex-v3--v3-20261026t180319z-8958ec5fa63f`, at 100 per cent. The
  driver deployed the same release again with `-Public -SearchEngines indexed -LiveRevision <that revision> -Apply`, to
  change only `LEX_V3_SEARCH_ENGINES` (O27). The change of setting did not deploy.
- **The cause: two defects in `deploy/deploy.ps1`.**
  - It named the revision from the release alone. The new revision took the live one's suffix, and Azure refused the
    deployment: "revision with suffix ... already exists".
  - Its failure step then deactivated the revision with the candidate's name without asking whether this deployment had
    made it. That revision was the live one.
- **The outage.** The site was down for about three minutes, 01:10 to 01:13 UTC, until the driver activated the revision
  again (`C:\lex-v3\release-20261026\deploy-public.log`).
- **The fix: each deployment makes its own revision.** The suffix is the release's version followed by 8 hex digits of a
  SHA-256 over the image, `searchEngines`, `openToPublic`, the allowed ranges and `deploy/main.bicep`. The incident's
  command now names `ca-lex-v3--v3-20261026t180319z-8958ec5fa63f-54d663c3` (52 characters; at this template).
- **Azure's naming limits.** The suffix has lower-case letters, digits and '-', a letter first, no '--', and at most 64
  characters (the Container Apps documentation, "Revisions"). The whole name is at most 63, one DNS label, since the probe
  asks the revision's own address. A name that cannot be formed is refused, and the plan prints the name.
- **The fix: nothing deactivated that the deployment did not create.** The app's revisions, inactive ones too, are read
  before anything in Azure changes.
  - A name the app holds, or -LiveRevision's, is refused there with nothing changed. A failed read stops a redeployment.
  - Every deactivation (failed deployment, candidate failed or never ready, failed probe) goes through one guard. It acts
    only on a revision absent from that read. If the revisions were not read (a first deployment), it deactivates
    nothing, says so, and names `-Remove`.
- **Tests.** `deploy-run.test.mjs`'s made-up Azure CLI now holds revisions as Azure does. Six new run tests cover the
  incident's command, the suffix rules, a collision refused with no Azure write, a failed deployment named as the live
  revision, the probe and provisioning failures, and the normal new-release path. `deploy-kit.test.mjs` gains a rule that
  the guard's is the only deactivation outside `-Remove`.
- **The tests fail on the old script.** With db0a7b53's script put back, the five defect run tests fail and the other
  eight pass, the normal-path test among them. Three kit rules fail: the new one and the two rewritten for the guard. The
  incident test replays the production log's line: "the deployment failed: deactivate the candidate
  ca-lex-v3--v3-20261026t180319z-8958ec5fa63f".
- **Counts.**
  - The web build is clean. `npm test` runs 1,366 tests (1,359 before): 1,363 pass, 0 fail, and 3 skip, the
    real-browser tests that run only in CI.
  - The deploy files all pass: deploy-kit 16, deploy-run 13, deploy-probe 16, no-browser-probes 11.
  - `eng/verify-v3-tree.ps1` verifies 1,689 tracked paths. `deploy/validate.ps1` is clean.
- **Driver decisions (reversible).**
  - A digest rather than an `-r2` counter: a plan run asks Azure nothing and still prints the exact name.
  - The template is in the digest, so a template change on the same release can still deploy.
  - The same release with the same settings and template is refused before any change; a revision that exists is
    activated by hand.

## Release 1 on the private copy, and the opening that waits on the owner's command (sole driver, 2026-10-26)

The release `v3-20261026T180319Z-8958ec5fa63f` (from v3/integration at 8958ec5f, #987 merged, over derive-c) was built
with `--no-browser` and checked:
- **Signed and reproduced.** It is signed by the release signer, now named `lex-v3-release` with no person's name (the
  owner's decision of 19 October), its key piped from Key Vault and never printed. A second build from scratch gave the
  same image (`sha256:93cdd51e…`).
- **Gates and probes.** The machine gates' card passes every gate over the mount, and every shuffled control was
  caught. 18 API journey probes, the served pages and the accented title found 0 failures. V2 is absent (566 entries,
  63 routes asked).
- **Privacy.** 152 requests: nothing written to output, no file events, no cookie, no other origin.
- **Freshness.** No promotion refusal: the data's age is within the 30 days.
- **Kept** at `C:\lex-v3\releases\` with its report.

The deployment and promotion:
- **Candidate probe.** `deploy.ps1 -Apply` (the driver's identity, through the private-copy wrapper) deployed it as a
  candidate and probed it at the revision's own address. It answered as the release holds, and its data's age allows
  promotion. The custom domain stayed bound, and ingress admits only the three named addresses.
- **Promotion.** All of the private copy's traffic then moved to it. The revision of 18 October was deactivated, so one
  replica is billed. law.soufien.lu answers ready.
- **The owner's new address.** The owner's address changed by 2026-10-26 and was added as a third rule,
  `private-copy-3`. The address rules are removed at the opening.

The opening:
- **The legal pages are ready.** The opening's check of the legal pages (`deploy-probe.mjs --opening`) was run read-only
  against https://law.soufien.lu: each of the 10 answers, sets no cookie and shows no e-mail address, mail or telephone
  link, placeholder or postcode.
- **The driver may not open.** The opening itself (`deploy.ps1 -Open`, which removes the address rules) was refused to
  the driver by its harness's safety classifier ("security weaken"), and it was not retried. The owner runs the one
  command: `pwsh -File <checkout>\deploy\deploy.ps1 -Subscription <id> -ResourceGroup rg-lex-v3 -AppName ca-lex-v3 -Open`.
- **Then indexing.** After it, the driver deploys the same release with `-Public -SearchEngines indexed` (O27, decided on
  18 October). The image is already in the registry, so only a new revision and its probe are needed.
- **Release 2 carries the codes.** Release 2 follows D1, the re-derivation with the codes (STATUS-DATA.md).

## Review and repair (combined #987): the cards' new reason and coordinate candidates, the closed end's day, dates by role on the radar and in the CSV (sole driver, 2026-10-26)

The web half of the one repair round of the combined data pull request #987 (STATUS-DATA.md, the section of the same day,
has the platform's half and the review). Each finding was reproduced with the review's probes and is held by a test that
fails on the reviewed head (f0dcf89c) and passes after: there, 8 of the new or changed web cases fail. Code at 5e311bb8.
- **1 (medium-high), `outside_named_version` on the card.** F2's reason was not in the card's closed set, so `reasonView`
  threw and a search with an ELI of a version outside its date showed no card. `reasons.outside_named_version` says it, in
  English and French, with the payload's date and the catalogue's read time; and the search page's head for that reason
  says the version named is held and does not cover the date (`LIVE_SEARCH_HELD_ACT_REFUSAL_SENTENCES["lu-legilux"].outside`),
  where "No state of this work that this index holds applies on that date" was false (the French review's point 1). Tests
  (`catalogue-chain.test.mjs`, `exact-reference.test.mjs`): the platform's real payload, from the refusal census's new
  scenario, through the search page in both languages and both renderers, its head and its named and covering states; and
  among the per-reason loop's payloads.
- **5 (low), a candidate naming no expression.** The platform names such a state by its stable coordinate (it has no
  permalink). The card reads a coordinate candidate by the digest, kind and role at its place in index 9's lists, says it
  with `refusalCard.candidateUnexpressed` ("{version}, hash {hash}: the publisher's catalogue names no expression of it,
  …"), its version named as the dossier names a version without text, with its coordinate as code in parentheses and no
  link, in both renderers. A coordinate without the lists, lists of another length, or a reading URL whose digest is not
  the one at its place is refused, after the choice's own rules (a list offering one state twice is still said to). The
  dated search's ambiguous works and the radar's candidates take a coordinate too. Test: the card in both renderers and
  languages (the French after a version named "l’acte tel que publié" too), and the four refusals.
- **6 (low), the closed end's day.** `reasons.after_closed_end` called the end the publisher gives the last day applicable
  ("applicable to {closed}", "jusqu’au {closed}"); `closed_on` is the first day the state does not cover. It now says "the
  version that ends last is applicable until the day before {closed} (publisher), and no version begins on or after
  {closed}" / "la version dont la fin est la plus tardive est applicable jusqu’à la veille du {closed} (éditeur), et aucune
  version ne commence le {closed} ni après", as the reviewed `history.breakTo` and the platform's F7 (after a nested
  version the last to begin is not the last to end). `dossier.overlap`'s exclusive end is said the same way. Test: both
  languages, the nested fixture's dates, never the old words.
- **8 (low, R21), the radar's row.** `radar.row` said "from {date}" of an act dated by its entry into force (the reader
  dropped `date_role`). `radar-answer.mjs` reads the role and entry into force (`readDateFacts`), and the row says
  `radar.rowEntryIntoForce` ("…, the act as published, dated by its entry into force on {from} (publisher): …"), as
  History's F5 forms do, its verdict `radar.notComparedAct` ("non comparé" after the act); its ambiguity "covers" the date
  rather than "applies" on it. `date-speech.test.mjs` says the radar too (seven screens, both languages) and its regex now
  catches a bare "from {date}" and "à partir du {date}"; it caught the radar's row on f0dcf89c. Test: the radar's row and
  verdict in both languages, and a schema-8 radar with no role.
- **3 (medium, R21), the CSV.** It said an entry into force as `applies_from` with no role. A `date_role` column is
  appended after `segments`, so every column before it keeps its place: `entry_into_force`, `applicability` or
  `applicability_in_file` on index 9, empty on schema 8 or 6. Test (`date-role.test.mjs`): a bundle dated by its entry into
  force, the census's own, and the schema-8 mount's recorded bundle; `content3-wording.test.mjs` pins the last two columns.
- **9 (latent, EU 3).** The EU force dates' reader refused an `xsd:date` with a timezone (2016-05-24Z), which the contract's
  `PublisherDate` admits. It takes a year, a year and month or a date with `Z` or a signed offset up to 14:00, and the open
  end in any of them. Test (`europe-force-dates.test.mjs`): three zoned dates read as served, three malformed refused, the
  zoned open end, and an unpinned code on the end-of-validity and signature properties read with its code.
- **10, tests that did not test what they claimed.** `date-role.test.mjs` and `catalogue-chain.test.mjs` held "an older
  index names no role / no chain" to a census answer of index 9 compared with itself; they now read the schema-8 mount's
  recorded answers (`base-answers.json`, which the platform replays byte for byte): its search, dossier, bundle and radar.
  `europe-force-dates.test.mjs`'s drift cases each assert their own rule's message, and "the code EV given no role" adds a
  not-pinned EV row of its own (it had also removed the application, so the reader refused it for other rules, and still
  did with the EV check taken out, which the new case does not: measured by removing the check).
- **The worked example** of `no_version_for_date` (`refusal-catalog.mjs`) shows a French version named
  (`no_expression_in_language`), since the census's richest payload now names states and an example must teach a list's
  rows.
- **French** (Decision 41): one independent reviewer (Claude, AI reviewer, not the author) under the owner's delegation, in
  the addendum of 2026-10-26 of `C:\lex-v3\lanes\fr-review\review.md` ("the closed end's day, an identifier outside its
  version, a candidate naming no expression and the radar's entry into force"): 4 forms reviewed and 1 added, 3 changed in
  review (the English too in 1): `after_closed_end` (the version that ends last), `outside_named_version` (an identifier
  "désigne"), `candidateUnexpressed` ("cette version", never "elle" after "l’acte tel que publié"; "propose"),
  `rowEntryIntoForce` approved, `notComparedAct` added. Its proposals taken as worded: the search card's head for
  `outside_named_version`, `dossier.overlap` and `radar.reason.ambiguous_version`; its platform point (the outside refusal's
  nearest dates) is fixed in the platform's half; its point 5 (the coordinate in parentheses) is taken. The finals are
  applied, the addendum is appended with the driver's note, and the forms are named in `live-chrome-fr.mjs`.
- **Measured** (Windows, no browser, no publisher traffic), on 5e311bb8:
  - `npm run build`: 30 state pages. `npm test`: 1,309 (1,306 passed, 0 failed, 3 skipped).
  - The journey unit tests (`node --test web/test/journey*.test.mjs`): 62 of 62.
  - Before the fix: the five changed test files on f0dcf89c's web with the new census sample: 47 (39 passed, 8 failed:
    each new or changed case); the unmasked EU drift case fails with the not-pinned check removed.
  - `eng/verify-v3-tree.ps1`: 1,666 tracked paths.

## EU force dates on the EU dossier and at the head of an EU reading (sole driver, 2026-10-19)

The web half of the plan's item 2.1 (STATUS-DATA.md, "EU force dates"; R79's E1, R21).
- **The answers.** Over an EU index /6, `dossier`, `timeline`, `as_of` and `evidence_bundle` of an EU act carry
  `force_dates` (`V3CorpusMount.EuropeForceDates.cs`): each date with its role (`entry_into_force`, `application`,
  `end_of_validity`, `deadline`, `signature`, `not_pinned`), the value exactly as served, the publisher's code, its note
  and the note in its codes (`publisher_note_codes`), the date property and the publisher's statement it was read from;
  the named roles not held; a conflict list for two entry-into-force dates; and the rules (role, note, not held,
  conflict). The not-held row `force_dates` then says what of them is still not held, and that none of them chooses a
  wording. Over an index /5 nothing changes (`V3CorpusEuropeSchema5MountTests`, byte for byte).
- **The pages.** `europe-force-dates.mjs` reads the member (null when absent) and throws on drift: a role no date takes,
  EV or MA under the wrong role, codes that are not the note's, an open end that is a date, roles not held that the dates
  contradict, dates out of order, a conflict the dates do not hold, another act's dates, a date said twice.
  `EuropeForceDates` (`LiveAnswer.jsx`) says them in the page's language: on the EU dossier after the expressions, and at
  the head of an EU reading, before its wordings. Each date is said with its role ("Entry into force: {date}"; an end of
  validity "in whole or in part", since 20 of run 16's 24 are partial; the open end as no date), the qualifier in
  parentheses after it, the publisher's note in its own codes, as code, marked `lang="zxx"` and never translated; the
  roles not held are listed; two entries into force are both said; and under at least one date, the reading adds that
  none of them chooses a wording, each wording carrying its own date.
- **French** (Decision 41): seventeen forms under `common.europeForceDates`, reviewed by an independent reviewer
  (Claude, AI reviewer, not the author) under the owner's delegation in the addendum of 2026-10-19 of
  `C:\lex-v3\lanes\fr-review\review.md` (17 forms, 11 changed in review, the English too in 5: the heading says what this
  site holds, "Les dates de l’éditeur que ce site détient pour cet acte", never "les dates de l’acte", which the wording
  headings use for the Formex date; MA is "mise en application"; an end of validity "totale ou partielle"; the qualifier
  is "la mention de l’éditeur, mot pour mot"; and the reading's last line says no date above chooses a wording).
  The R21 guard takes S7a's hunk byte for byte (the noun "entrée en vigueur" passes, "en vigueur" as a state stays
  refused), so the two branches merge without a conflict there. The EUR-Lex French labels are the reviewer's knowledge,
  not checked against the publisher (no request was made).
- **Tests.** `europe-force-dates.test.mjs` (reading, twelve drifts, the seventeen entries in both languages against the
  R21 guard, the census's EU dossier, and a reading led by the dates before its wordings); `date-speech.test.mjs`
  counts the seventeen entries as EU entries (no Luxembourg date word); `live-chrome.test.mjs` holds the roles and role
  names to the reader's lists. The answer and envelope censuses' EU mount holds the GDPR's three dated statements as
  run 16 holds them (entry into force 2016-05-24, application 2018-05-25, deadline 2020-05-25 "AU+TARD V ART 97"); both
  were re-rendered from the handler and verify without the variables.
- **Measured** (this branch, rebased on `v3/integration` at 0b908870), with no browser started:
  - `dotnet build Lex.V3.slnx -c Release --no-incremental`: 0 warnings, 0 errors.
  - `Lex.V3.Tests`: 3,321 (3,315 passed, 6 skipped).
  - `Lex.V3.Ingest.Tests`, the classes of the EU index, the EU time view, dossier, search, reference, evidence bundles and
    capabilities, the censuses, the samples and the evaluation card, privacy (public request recording, serving temporary
    storage), the legacy schema-8 and schema-6 mounts, the EU schema-5 mount, mount build files, the offline mount, the
    machine gates, the annex control case, EU permalinks, generation quotes, first-mount reuse, the consolidated first
    mount, the reified-axiom wiring and the journey mounts: 202 (192 passed, 10 skipped, all opt-in).
  - The web build; the web suite, 1,279 (1,275 passed, 3 skipped, 1 failed: `image-run.test.mjs`'s mount test spawns
    `awk`, which this PC's PATH lacks; it passes where awk is, as in CI).
  - `eng/verify-v3-tree.ps1`: 1,659 tracked paths.

## The catalogue between versions, a date said with its role everywhere, the cards' own reasons, the caveat and the translation label (sole driver, 2026-10-19)

Slices S7b and S7c of the Luxembourg data design (STATUS-DATA.md, the section of the same day), with what S7a (#987) left
them: the web of index 9's catalogue chain and continuity breaks, R21 on every page that still said an entry into force with
application words, the refusal cards' own sentences, the consolidation and code caveat (L5), the translation label (R18).
On an index of schema 8 or 6 every page says what it said before, except the coverage page's note, whose new words are true
of every index.
- **R21, the rest of S7a's list** (an entry into force is never said as an application):
  - Compare (`LiveCompare.jsx`, `compare-answer.mjs`): a side dated by its entry into force is "the act as published, dated by
    its entry into force on X (publisher)" (`compare.sideEntryIntoForce`, `…Next`); one state on both dates is said so
    (`compare.sameVersionEntryIntoForce`, `sameTextHeldEntryIntoForce`, `sameTextHeldOfSeveralEntryIntoForce`), and the
    reader holds the platform's note by role (`SAME_STATE_ENTRY_INTO_FORCE_SENTENCE`, `sameTextHeldNote`'s role).
  - History (`LiveHistory.jsx`, `history-answer.mjs`): a row dated by its entry into force answers the "Applies from" column
    as the dossier does (the reviewed `dossier.entryIntoForceDate` and its note, reused).
  - The export (`LiveExport.jsx`, `export-build.mjs`, `export-pdf.mjs`): `export.itemEntryIntoForce` and
    `excludedEntryIntoForce`; the PDF's heading, conflict line and exclusion line; the JSON names `date_role` on index 9.
  - One state heading by role (`StateHeadingWords`) for the reading, the export's pins and the cited article's source: the
    last two still said "the state applying from" of every state, which the per-role speech test found.
  - `reading.validityConflict` and `search.dated` are S7a's repair's (its F5), as the coordinator moved them there; rebased
    on S7a's final head, the per-role speech test finds no application word of an entry into force on any page.
  - The dossier's span (`dossier.statesEntryIntoForce`, `statesFromEntryIntoForce`, `statesInEntryIntoForce`,
    `statesInFromEntryIntoForce`): a first state dated by its entry into force is said as that.
  - The refusal card's candidate (`refusalCard.candidateEntryIntoForce`, `…WithdrawalNotStated`), from the platform's new
    `candidate_date_roles`.
- **The timeline speech test, per date role** (`date-speech.test.mjs`, the launch contract's evidence): every census
  Luxembourg answer said again with its first state dated by its entry into force (as index 9 dates an act as published),
  through search, dossier, reading, export (composed with every article pinned), history and compare, in English and in
  French: no application word stands right before that date; a borrowed heading and five application phrasings are caught,
  four right ones are not.
- **The refusal cards' own sentences** (`refusal-card.mjs`'s `reasonView`, both renderers, the page's copy): why no version
  covers the date for each of S7a's six reasons (`refusalCard.reasons.*`: the catalogue as read no later than its build, a
  hole's span "this site does not fill", a closed end "which says nothing of whether the law ends" with the base act's
  end-of-force date when the publisher's record gives one, an unclear end day given to neither, an invalid interval whose
  own link still opens it, no expression in the language asked); the textless version covering the date named by its kind
  and role and why its text is not held (`refusalCard.textless`, `textlessNamed`, `versions.*`, `textNotHeld.*`). A reason or
  a text status outside the platform's closed set is refused, so a new one is worded before it is shown.
- **The reading's caveat and label** (`VersionCaveats`): a consolidated version "is documentation without probative value.
  Only the texts published in the Journal officiel (Mémorial A) are authentic: the act as published, {act}, and the acts that
  amended it, whose list Lex does not yet hold." (R20's words; {act} its work's `/jo`, as the platform names a base act); a
  code's version "documentation without probative value: the authentic texts are the acts published in the Journal officiel
  (Mémorial A) that it brings together"; an expression in another language than French "the publisher's {language}
  expression of the act, beside its French expression {french}. The publisher's record does not state which language version
  is authentic." (R18; never "only the French text is authentic").
- **The dossier: the catalogue between versions** (`CatalogueChain`, `dossier-answer.mjs`'s `readCatalogueChain`): the versions
  the catalogue lists whose text this site does not hold, named as the card names them, each with its permalink (which
  refuses for its text) or its coordinate; each gap, overlap, same-date group, unclear end day and invalid interval in
  words with the answer's dates; and that a gap in the catalogue is not one in the law. A held state listed without text is
  drift, and refused.
- **The history's breaks** (`Breaks`): each run of dates no version with held text covers, in its language, with why
  (`history.breakReasons.*`), each row after one marked (`history.afterBreak`), and that an unchanged wording across one says
  nothing of it; the reader holds each row's mark to the breaks it names.
- **Coverage** (the row S4 left): the page's note says the history's build times are when each acquiring build read its
  acquisition (`COUNTS_PROVENANCE_NOTE`, `coverageAnswer.countsProvenance`), and the preview carries index 9's row.
- **The L5 sentences** (design §5; ruling 4's pattern: the drafts ship and the owner revises them in person): the
  consolidation and code caveat (`reading.consolidationCaveat`, in R20's words, `reading.codeCaveat`), the translation label with R18's
  sentence (`reading.translation`), and the refusal wordings for a catalogue hole, an unclear end day and an invalid interval
  (`refusalCard.reasons.catalogue_hole`, `.publisher_end_convention_unclear`, `.publisher_interval_invalid`), English beside
  French by `node web/scripts/refusal-sentences.mjs`. The other L5 sentences are not this slice's (the single-channel
  disclosure and the attribution line wait for the owner's L1 and L2; the coverage counts of withheld files; the struck
  wording shipped with S3b).
- **French** (Decision 41): 59 forms reviewed by an independent reviewer (Claude, AI reviewer, not the author) under the
  owner's delegation in the addendum of 2026-10-19 "the catalogue's chain, continuity breaks, dates by role elsewhere, index
  9's refusal sentences and the version caveats" of `C:\lex-v3\lanes\fr-review\review.md`; 10 changed in review, 49 approved,
  and one form its point 1 asked for (`refusalCard.publishedAct`, "publié le" of an act's candidate) reviewed with them.
  Changed: "ce site n’a pas acquis cette version" (it follows a version named "l’acte tel que publié"); "fait foi", never
  "authentique", and "un outil de documentation" in both caveats; a gap and a break named by their ends' roles, the break's
  end exclusive ("to the day before {to}", "à la veille du"), the English changed with these two; "les textes" after a
  plural; "un vide juridique"; an apposition's comma in two spans; "de l’acte, publiée par l’éditeur". Its points 2 and 4
  are done (the textless row starts with a capital; the uncounted one-state span only for one state). The forms are named in
  the header comment of `live-chrome-fr.mjs`. Its point 5 (`history.carried`, `carriedIn` and `absentRow` can carry an entry
  into force as "from {from}") goes to S7a's repair with `search.dated` and `reading.validityConflict`.
- **Tests.** `web/test/catalogue-chain.test.mjs` (new, 9): the card's six reasons and its textless versions in both renderers
  and in French, an unknown reason and status refused, an ambiguity's act by its role and a misaligned role list refused; the
  dossier's chain and its span by role, and drift refused; the history's breaks, its column by role and a row's mark held;
  the reading's caveat for a consolidation and a code and its translation label; Compare, the export (lines and JSON) by role;
  every added form held to the time-word guard. `date-speech.test.mjs` (+2): the per-role speech test and its control.
  Updated: `coverage.test.mjs`, `refusal-catalog.mjs`'s worked example. On the combined data branch (S7a's final head
  943cbd2d, S7b and S7c rebased on it, `data/eu-force-dates` merged): `npm run build` 30 state pages; `npm test` 1,304 tests,
  1,301 passed, 0 failed, 3 skipped (the browser tests, which run only with `LEX_BROWSER_TESTS=1`); the per-role speech test
  and its control pass (before the rebase they found the reading's validity-conflict line, S7a's F5).
- **Driver decisions (reversible).**
  - **The web's timeline is the dossier's**: the design's `Timeline.jsx` (holes hatched, overlaps stacked) is the pre-V3
    preview, served nowhere; the live dossier says the catalogue's chain in words, under the states table, which needs no
    client chart and reads the same to a screen reader.
  - **The textless version is named with the refusal card's words** (`refusalCard.versions`, `textNotHeld`) on the card and in
    the dossier, so a version is said one way wherever it is.
  - **The History table reuses the dossier's reviewed cell and note** for a row dated by its entry into force (same column).
  - **The caveat's act is the consolidation's work's `/jo`**, as the platform names a base act (`base_act_iri`); a code's
    caveat names no act (a code brings several together).
  - **The consolidation caveat ships in R20's words** ("Only the texts published in the Journal officiel (Mémorial A) are
    authentic: the act as published, {act}, and the acts that amended it, whose list Lex does not yet hold"), the specification
    of record's, which rules where the design's L5 draft ("the authentic text is the act in the Journal officiel") says less:
    the act as published lacks every amendment the consolidation carries (the French review's blocking point). The owner
    revises it with the other L5 sentences; for the lawyer's read (O4), older codes such as the Code civil were not first
    published in the Mémorial A.
- **Left.** The coverage page's state range (`coverageAnswer.stateRange`, "the first and last applicability dates") may begin
  at an entry into force on index 9; the platform names no role for an aggregate's end (STATUS-DATA.md). The radar's row
  ("from {date}") names no role and says no application word.

## Index /9 served: a date said with its role, a missing article said in place, the cards read the new refusals (sole driver, 2026-10-18)

Slice S7a of the Luxembourg data design (STATUS-DATA.md, the section of the same day): the reader's interval rules over
lex-v3-luxembourg-index/9, and with them R21's "every date said with its role" on the pages that say a Luxembourg state's
date. The server now serves a built mount of index 9.
- **The role** (`web/scripts/date-role.mjs`): `readDateRole`, `readEntryIntoForce` and `readDateFacts` read a state's
  `date_role` (`applicability`, `applicability_in_file`, `entry_into_force`) and its act's `entry_into_force`, hold the two to
  each other (a state dated by its entry into force names that date as its entry into force), refuse any other role, and give
  null on an index of schema 8 or 6, which names none, so those pages say what they said before.
- **Where it is said.**
  - The reading (`LiveReading.jsx`): a state dated by its entry into force is headed "{work}, {language}, the act as
    published, dated by its entry into force on {from} (publisher)" (`reading.stateHeadingEntryIntoForce`, and `…Next` with
    the next state held), never "the state applying from".
  - The dossier (`LiveDossier.jsx`): under "Applies from" such a row says "no date stated (entry into force: {date})", and
    once under the table what the mark means (`dossier.entryIntoForceDate`, `dossier.entryIntoForceNote`).
  - The search page (`LiveSearch.jsx`, #982's version line): "The act as published, entry into force on {date} (publisher)"
    (`search.versionAsPublishedEntryIntoForce`); each hit carries `dateRole` (`search-answer.mjs`), each version group its
    role (`actGroups`).
  - The citation generator (`citation.mjs`): the usual form "{citation}, as published, entry into force on {date}" and the
    evidence line "Version: the act as published, entry into force on {date} (publisher)"; the exact-reference phase reads
    the usual form back (STATUS-DATA.md).
  - The provenance page (`provenance.mjs`, `Provenance.jsx`, its preview): a state's thirteen row facts, state digest /2's
    inputs, in rows (null said as not stated), and its heading by its role.
- **Missing articles.** `reading-answer.mjs` reads each article's `document_position` and the state's `missing_articles`;
  the reading says, inside the next quoted article and before its heading, "At this point, the publisher's document has an
  article that this site does not hold in this version." (`reading.missingBefore`, `one` and `other`).
- **The undated fold** (STATUS-DATA.md): a passage folded from several versions says "The same wording is in {count} other
  version(s) this site searched." (`search.sameWording`).
- **The cards read index 9's refusals.** `refusal-card.mjs` declares their optional members (`INTERVAL_KEYS`: the reason,
  the named states, a hole's span, a closed end and the base act's facts; each ambiguous candidate's kind, interval, boundary
  and text status; a textless state's fields), so a card is shown as before, never refused for an undeclared member; the
  catalogue's worked examples carry them. Their own wording on the card is S7c's.
- **The time-word guard** (`publisher-vocabulary.mjs`, `FORBIDDEN_TIME_WORDS`): "en vigueur" stays refused as a state and
  over an interval; only the noun of the typed date, "entrée(s) en vigueur", gets through (the French review's point 1:
  "entré en vigueur", "rentrée en vigueur", "est en vigueur", "la version en vigueur" stay refused; ten refused controls and
  eight allowed ones in `compare-vocabulary.test.mjs`).
- **French** (Decision 41): 11 forms reviewed by an independent reviewer (Claude, AI reviewer, not the author) under the
  owner's delegation in the addendum of 2026-10-18 "dates by role and missing articles" of `C:\lex-v3\lanes\fr-review\review.md`;
  6 changed in review (the notice's "À cet endroit", since "ici" is this table's "on this site"; the dossier's cell answers its
  column with "aucune date indiquée" and its note classifies the value rather than saying when the act applies; a semicolon
  parts the act as published from its entry into force; "interrogées par ce site"), the English of four with them; 5 approved;
  the forms are named in the header comment of `live-chrome-fr.mjs`. Its blocking points (the guard; a run of one missing
  article threw) and point 3 (the exact references) are done; point 4 is S7b's and S7c's (below).
- **Tests.** `web/test/date-role.test.mjs` (8): the role read and refused; the reading's heading, the dossier's mark and
  note, the citation's two forms and the search's version line and folded line, in English, and the reading in French; every
  form of the slice held to the time-word guard in both languages; a missing article said in place, and a run that does not
  add up refused. Updated: `compare-vocabulary.test.mjs` (the guard's controls), `provenance.test.mjs` (the row facts' nulls).
  `npm test` (rebased on 24118293, #985): 1,280 tests, 1,277 passed, 0 failed, 3 skipped (the browser tests, which run only with `LEX_BROWSER_TESTS=1`).
- **Driver decisions (reversible).** The dossier's cell answers its column ("no date stated") rather than renaming the
  column, which is right for every other row. The provenance page's labels are English, as the rest of that page's are. The
  refusal payloads are flat rather than the card gaining structured members (STATUS-DATA.md).
- **Review and repair (#987, 2026-10-19).** The web's findings, each with its test in `web/test/date-role.test.mjs` (now 14):
  - **F3 (medium), today.** `luxembourgCurrent` (`citation.mjs`) and `answersToday` (`initial-answer.mjs`) took a state
    as today's by its next held state alone, so a state after a closed end, or followed only by textless states, was
    today's. `date-role.mjs` reads `applicable_to` (`readInterval`), and `holdsToday` asks both bounds: no later held state
    on or before today, and on index 9 an end after today or none. Test: a closed state is not today's, an open one is, an
    index 8 state is read as before; the citation's usual form of a closed state carries its version.
  - **F5 (medium), R21 on default pages.** `reading.validityConflict` said "its state applies from" an entry into force: a
    state so dated now gets `reading.validityConflictEntryIntoForce` on the reading, the pinned reading and the PDF (whose
    "applying from" lines say "dated by its entry into force on" too). `search.dated` says index 9's rule. Test: the census
    bundle's 49 conflicting articles rendered with the role; the repair's forms held to the guard in both languages.
  - **F6's reason.** `radar-answer.mjs` reads `no_held_baseline` (no baseline, no candidates); `radar.reason.no_held_baseline`
    says it. Test: the census radar row rendered with it, and refused if it names candidates.
  - **F7 (low), the provenance heading.** Test: `stateHeading` follows the role in both renderers.
  - **The History page (the coordinator's addition, from the S7b/S7c French review).** `history.carried`, `carriedIn` and
    `absentRow` said an entry into force as "from {from}". `history-answer.mjs` reads the role on rows and absent states;
    `LiveHistory.jsx` says the lineage's first row (whose date is `history_begins`) and an absent state so dated with
    `history.carriedEntryIntoForce`, `carriedInEntryIntoForce` and `absentRowEntryIntoForce`. Test: the census history
    rendered with the role on its row and an absent state, and as before without it. The table's date column stays S7b's.
  - **French** (Decision 41): six forms, reviewed by the same reviewer in the addendum of 2026-10-19 "S7a's repair round (the
    review of #987)" of `C:\lex-v3\lanes\fr-review\review.md`: the three of F5 and F6 changed in review (the English with
    them: "son" tied to the act; "les dates de l’éditeur" and "ne détient pas le texte"; "pas de version de référence", since
    an earlier held version may exist), the three of the History approved (5 strings; the reviewer's optional English "the
    first being" taken). Named in the header comment of `live-chrome-fr.mjs`. The reviewer's point that `search.severalOnDate`
    reads broader than what it counts on index 9 is left until index 9 serves such a case (not reached on derive-c).
  - Pinned anew: `live-home.test.mjs` (the dated search's sentence), `radar-answer.test.mjs` (the reasons).
  - `npm run build` and `npm test`: 1,288 tests, 1,285 passed, 0 failed, 3 skipped (the browser tests, which run only with
    `LEX_BROWSER_TESTS=1`).
- **Left to S7b and S7c** (the French review's point 4; the repair took `reading.validityConflict`, `search.dated`, the PDF's
  "applying from" and the History's summary and absent rows): the Compare card's same-version note and sides,
  the exports' item lines, the History table's column, and the dossier's state
  span, which still say a state dated by its entry into force with application words; the refusal cards' own sentences for
  the new reasons; the translation label on the reading.
## The legal pages name no person and give no contact: the owner's decision of 19 October (sole driver, 2026-10-19)

**The owner's legal decision of 2026-10-19, made, and recorded as the owner's** (STATUS.md, the owner's decisions of
2026-10-19). The owner's words that day: "i dont want to mention nobody name etc etc including me"; "the user is there to
have temporal feature over lux and eu laws thats it"; "why r we mentioning emails andadress". So the site names no person
and publishes no postal address, no e-mail address and no contact channel of any kind. It **departs from the
specification of record's R49** (the legal notice's editor, address, e-mail and director of publication; the privacy
notice's controller and contact, its legal bases, the retention of e-mailed reports, the requests a reader could make of
the publisher, the CNPD's postal address, and the objection contact for people named in acts) **and from O5's recommended answer** (the owner as editor and
controller, with a dedicated contact address; a security contact). With them go R62's contact in the accessibility
statement, R74's "Report a problem with this page" and its security contact (`/.well-known/security.txt`, never built),
and R92's objection contact; R92's other duty, keeping acts that name people away from search engines, stands, and the
privacy notice says it. One slice on `writer/legal-no-person`, stacked on `writer/web-bigbang` (#989).
- **What was removed:** `web/scripts/legal-publisher.mjs` (the publisher's name, postal address and contact e-mail, each
  still a placeholder, and `reportRetentionMonths`, the 12 months decided earlier that day) and everything that read it:
  the placeholders and their mark (`PUBLISHER_PLACEHOLDERS`, `data-legal-placeholder`), the build's refusal of a detail
  filled wrong (`malformedPublisherFailures` in `build-live.mjs`), the readiness checks (`publisherFailures`,
  `isUnfilled`, `placeholdersIn`) and `legal.months`. From the pages: the legal notice's
  publisher, contact and director of publication; the privacy notice's controller, its three legal bases, the e-mail
  section and its retention, the processors paragraph (the mailbox provider), the requests to the publisher (arts. 15 to
  18 and 21), the CNPD's postal address, and the objection; the terms' "Reporting an error"; the accessibility statement's "Reporting a
  barrier" and "published by a private person". With no contact, no e-mail is received or kept, so the 12 months apply to
  nothing (superseded, above).
- **What each page says now** (`live-chrome.mjs`, `live-chrome-fr.mjs`; "Last updated: 26 October 2026", since the
  review's qualifier below; 19 October before it):
  - **legal notice:** what Lex is (an independent, non-commercial tool for reading Luxembourg and EU law over time: for the
    texts and dates it holds, a text as it stood on the date you choose, and how it changed; free, no advertising, no
    account) and R50's sentence; no
    official status (not a service of the State, the Service central de législation or the Publications Office, none of
    which endorses it) and the authenticity line, then "where a text on Lex differs from its official publication, the
    official publication prevails"; the sources and licences (Legilux under CC BY 4.0, for the files whose own records
    give it, the official link otherwise; the EU texts with their acknowledgement "© European Union,
    https://eur-lex.europa.eu" and Decision 2011/833/EU; "extracts and normalises", each quote naming its source: the
    licences' duties, kept word for word from the reviewed terms); the hosting (Microsoft Azure, France Central, in
    France; no street address);
  - **privacy notice:** what a request carries (the device's IP address, the page's address, the browser's technical
    details), a search's words in the request's body and never in the request's address, a search kept in a link in the
    part after "#", which the browser never sends; the host carries each request and handles the connection data under
    its own terms; the server keeps nothing of a request (no access log, no record of what is read or searched, no log
    store or monitoring in the hosting; tested with marked values); no accounts, so nothing about a reader to show,
    correct or erase; no cookie, browser storage, tracker or other site, and links ask the browser to send no referrer;
    people named in published acts and the search-engine rule; and, under "Your rights", one sentence: the reader may
    lodge a complaint with the CNPD, its site linked (https://cnpd.public.lu/), no postal address (a driver decision, below);
  - **terms of use:** R50's sentence and the service with the authenticity line; anyone may read, search, cite and export
    the texts, free and with no account; each text keeps its source's licence (CC BY 4.0, the EU's reuse policy), the
    legal notice saying where the texts come from and the acknowledgement each carries, and the terms add no restriction;
    no warranty ("It does not guarantee", no publisher named); how to cite;
  - **accessibility statement:** the WCAG 2.2 AA target and "non-conformant" until an audit, entry 13's English and
    French unchanged (9c65c68a); what has been done and what has not been checked; the 2019 law binds public sector
    bodies and "Lex is not published by a public sector body"; how the statement was prepared; no feedback contact;
  - **cookies:** unchanged (none set, none through another site, how that is checked).
- **What the privacy notice rests on** (read in the code on 2026-10-19; no Azure resource touched, so the hosting's
  read-back is the one of 2026-10-18, above): nothing personal is kept by the code or the deploy kit.
  - No cookie or storage: the CSP (`csp.mjs`, every source `'self'` or `'none'`), no `document.cookie` write, storage or
    `indexedDB` in `web/app` (`legal-pages.test.mjs`), no sticky sessions in `deploy/main.bicep`, and the deployment
    probe and the image rehearsal fail on any `Set-Cookie` (`deploy-probe.mjs`, `image-rehearsal.mjs`).
  - Nothing kept of a request: the API clears its logging providers (`Program.cs`, `ClearProviders`) and writes to its
    error output only at startup (a refused mount or web root); `PublicRequestRecordingTests` plants query text, the
    connection's address, the agent, forwarding headers, a cookie, a bearer token and a referrer and finds none in the
    output, the emitted events, metrics and traces, the response or the files written; the image rehearsal's privacy
    record finds nothing on the output and no file on `/tmp` once the server first answered. The only files the API
    writes are its verified index copies in its private `/tmp` at startup (`V3StaleIndexCopies`).
  - The hosting: `main.bicep` and the deploy scripts configure no Log Analytics, Application Insights or diagnostic
    setting; the environment `cae-lex-v3` was read back on 2026-10-18 with no log destination. The ingress sees the
    caller's address before the server does (`PublicRequestRecordingTests` claims nothing of it), hence "handles the
    connection data this needs under its own terms".
  - A search's words: `askV3` (`v3-client.mjs`) posts to `/api/v3/{operation}` with the parameters in the body,
    `credentials: "omit"` and `referrerPolicy: "no-referrer"`; a search kept in a link is the fragment
    (`search-address.mjs`). No account exists: the API reads no credential (the bearer token above is never read).
- **The opening's check** (`openingFailures`, `builtLegalPageFailures`; driver decision, reversible): **kept, and turned
  into a check of the owner's decision** rather than removed. `deploy-probe.mjs --opening` (`deploy.ps1 -Open`, and every
  `-Public` deployment's candidate) still fails closed on a page it cannot read, a page that is not 200 with a page, or a
  cookie, and now refuses a page whose bytes show an e-mail address, a `mailto:` or `tel:` link, a placeholder in the
  «LIKE_THIS» form or a Luxembourg postcode (`SHOWN_NOWHERE`, `legal-pages.mjs`). Why: the opening is the moment the
  decision becomes public, and the check reads what the public would read, so a regression (a contact put back, a stale
  page or release) is caught on the served bytes, at no cost; removing it would leave the opening a cookie check only. It
  checks marks, not words: a word list at the opening would refuse a harmless sentence; the words (a contact, writing to
  someone, a postal address, a request or objection, the persons R49 named) are pinned at build time, in both languages,
  by `legal-pages.test.mjs`. A person's name cannot be recognised by a pattern; the pages hold no value that is not the
  table's, and the tests hold every placeholder a legal sentence may name to seven values, none of them a person's.
- **The rest of the site:** no other page gave a contact; the footer lists the five pages and nothing else. The asked
  words ("@", "contact", "e-mail", "email", "courriel", "adresse", "address", "écrire", "write to") were searched in
  `web/app`, `web/scripts`, `web/src` and the API's served texts (`src/Lex.V3.Api`, `src/Lex.V3.Contracts`): no e-mail
  address or mail link anywhere; "contact" and "e-mail" only in comments and in the check's own messages; "address" and
  "adresse" only for a web address, the IP address and the verb "s’adresse". The API serves no legal text of its own, so
  no answer or census sample moved.
- **French** (Decision 41): the 22 forms added, changed or moved were reviewed by an independent reviewer (Claude, AI
  reviewer, not the author) in the addendum of 2026-10-19 "the legal pages without a person or a contact" of
  `C:\lex-v3\lanes\fr-review\review.md`: 7 changed in review, 15 approved, all applied byte for byte, with the two English
  changes it recommended (entry 9, blocking: the part after "#" is what the browser never sends, never "the address";
  entry 12: the terms announce how to cite the texts, not Lex). New rows: "au fil du temps" (never "dans le temps"), "tel
  qu’il se présentait à la date de votre choix", "un texte que Lex montre" (never "sur Lex" or "de Lex"), "est libre de"
  for the terms' permission (an EU text has no export), "ne propose ni compte ni inscription". The reviewer's script ran
  6,301 checks on the 84 forms of the six sections (both tables' keys, placeholders and numbers in order, typography,
  the refused words, `NO_CONTACT_WORDS` and `SHOWN_NOWHERE`): all pass. Its section for the lawyer's read (O4) lists
  what the rewrite leaves out (a controller, legal bases, the rights and the CNPD route, an objection route, a feedback
  route for accessibility), as the owner's decision and not the reviewer's to revisit; the read has not been done. The
  CNPD route came back since, as one sentence (below).
- **Driver decisions** (reversible):
  - **The CNPD complaint line is kept**, one sentence in each language, under "Your rights" ("Vos droits"): "You may lodge
    a complaint with the Commission nationale pour la protection des données (CNPD), cnpd.public.lu.", the site a link
    (`CNPD_SITE`, https://cnpd.public.lu/) and no postal address. The right to complain to the supervisory authority (GDPR
    art. 77) is the reader's, not a contact of Lex's: the owner objected to Lex's own name, address and e-mail, not to
    naming the regulator. The first draft of this slice had removed the line with the requests; the coordinator reversed
    that, and this record follows. It is the reviewed line of 2026-10-18 (its entry 54) without the address and without
    "also" (the requests it followed are gone), so it was reviewed again, by one independent reviewer (Claude, AI reviewer, not the author), in the
    addendum of 2026-10-19 "the CNPD complaint line kept" of the French record: 2 forms, both approved, no English change
    (its script's 8,280 checks on the 86 forms of the six sections pass; its note for the lawyer's read adds which
    authority is competent under art. 77(1), and that the complaint is the page's one recourse). The opening's check passes over it
    (an https link: no e-mail address, no `mailto:` or `tel:`, no postcode), and `legal-pages.test.mjs` and
    `deploy-probe.test.mjs` hold it: one sentence, the link once, "CNPD" once, no "Belvaux", "Boulevard du Jazz",
    postcode or "4370", and no other legal page naming the regulator.
  - The legal bases go with the controller they named; the licences stand in full on the legal notice and in brief in
    the terms, which point to it.
- **Measured** (no browser was started on this machine, no publisher was asked and no Azure resource touched; on
  the code of this pull request, the CNPD line included): `npm run build` in `web`, then a plain `npm test`: 1,314 tests,
  1,311 passed, the 3 that start a browser skipped with their reason (the legal-pages, deploy-probe, deploy-kit and
  chrome tests alone: 55 of 55); `dotnet build Lex.V3.slnx -c Release --no-incremental`, 0
  warnings, 0 errors; `Lex.V3.Tests`, whole, 3,327, 3,321 passed, 6 skipped; in `Lex.V3.Ingest.Tests` the Census classes,
  `PublicRequestRecordingTests` and the answer, envelope and refusal-payload samples, 32, 31 passed, 1 skipped (the EU
  predicate census, by its variable: publisher traffic); no sample was rendered again, since no served answer changed;
  `deploy.ps1` parses with no error, and `deploy-kit.test.mjs` runs its `-Open` path with the check's new wording; the
  live pages built and searched: no e-mail address, mail link, placeholder, postcode, "contact", "e-mail", "courriel",
  "write to" or "postal" in any served page ("@" only in the evaluation card's "nDCG@10" and "recall@10", "écri" only in
  "n’écrit rien" and "ce qu’il écrit"); `eng/verify-v3-tree.ps1` passes (1,662 tracked paths: the data file removed). A
  whole `Lex.V3.Ingest.Tests` run was started for good measure and stopped unfinished after about 20 minutes, because it
  held the build's files; the classes above ran in its place, as the brief asks.
- **Review (#990)**, one round, by the other model family: MERGE, with four non-blocking notes, each taken with a test.
  Rebased first on #989's repair (1e8c4276): `deploy.ps1` kept this branch's wording and the repair's
  `@(if ($Public) { '--opening' })`; this file kept both sections; `deploy-run.test.mjs`'s F1 case now fails a `-Public`
  candidate on a legal page with an e-mail address (what `legalPageTextFailures` says of it), not a placeholder.
  - **N1:** the opening's fail-closed test adds a 302 to a clean legal page of the site and a 308 to another site.
  - **N2:** `SHOWN_NOWHERE` reads Unicode (`u`), and a `mailto:`/`tel:` or a postcode counts only at a word's edge in
    any script (lookarounds on letters, marks, digits and "_"): `u` alone keeps `\b` ASCII's, so "Hôtel:" still said
    "tel:". `shownOnPage("Hôtel: rue")` now finds nothing.
  - **N3:** the legal notice's "what Lex is" says "for the texts and dates it holds", as the terms do; the five texts'
    "Last updated" is 26 October 2026.
  - **N4:** the 404 page ships the search's script, which redraws the strip from an answer, so it keeps the as-built
    line; the pages that ship none (the ten legal pages, the two maintenance pages, `locale-de.html` and
    `locale-lb.html`) never have an answer, and their strip now says so (`freshness.noAnswer`, `Document`'s `answers`).
  - **French:** the 2 forms (N3, N4), by one independent reviewer (Claude, AI reviewer, not the author), in the
    addendum of 2026-10-26: both approved as drafted, no English change. Its note for the owner: "how it changed" reads,
    for an EU act, as its successive wordings (`home.intro` says the same).
  - **Measured** (no browser, no Azure, no publisher): `npm run build`, then a plain `npm test`: 1,324 tests, 1,321
    passed, 3 skipped (they start a browser); the deploy tests 49 of 49; `eng/verify-v3-tree.ps1` passes (1,665 tracked
    paths); `dotnet build Lex.V3.slnx -c Release`, 0 warnings, 0 errors; `Lex.V3.Tests` 3,327, 3,321 passed, 6 skipped.

## Four web slices in one pull request, and the owner's decisions of 19 October (sole driver, 2026-10-19)

The owner's order of 2026-10-19: "big bang" pull requests to finish V3 today, without sacrificing quality. Four finished
web slices, each reviewed for French, tested alone and rebased on `v3/integration` at 0b908870, are merged with `--no-ff`
into `writer/web-bigbang`, in this order: today in Luxembourg and the search after "#" (plan 3.4, 3.5), the five legal
pages (7.1), the EU authenticity statement by the act's date (the owner's decision of 2026-10-18), and freshness and the
commits (6.1, 6.5). Each slice's own section follows, newest first. Then three decisions the owner made on 2026-10-19
("do what you recommend", relayed by the coordinator), one commit each.
- **Conflicts, each resolved by keeping both sides' behaviour; no test dropped:**
  - `STATUS-WEB.md`: every top section kept, newest first.
  - `site-paths.json`: the `about` names both the `file` member (the legal pages) and a readable address's `date=` (3.4).
    Every row of both sides is kept: 158 = 137 + 11 + 10, none twice. The table is written by hand; no script generates it.
  - `live-chrome-fr.mjs`'s header: the four reviewed-forms paragraphs.
  - `Document.jsx`: the footer lists the legal pages, then the freshness strip, last. `Document` takes `localePage` and
    `freshness`; the header keeps `localePage`, and the staleness banner stands under it.
  - `styles.css`: both rule sets. `live-chrome.test.mjs`: `freshness` and `legal` are both shared sections.
  - `deploy-probe.mjs`: the candidate's probe takes `promotion` and `opening`; the command line reads `--opening` and
    `--promotion-check`. `deploy.ps1` runs the candidate's probe with `@openingArgs --promotion-check`, planned and applied.
    Since the review's repair (below), the probe receives `--opening` as one argument: a `-Public` candidate whose legal page
    still says a placeholder fails (exit 1, deactivated, no promotion command); one whose data is too old stays without
    promotion (exit 3). At 9af65085 this was not so: the probe received `--opening` one character at a time and passed
    such a candidate (F1). `deploy-run.test.mjs` runs the script whole and reads the arguments the probe receives;
    `deploy-kit.test.mjs` holds the form.
- **What the combination moved, and the fix:** the envelope census's sample of an EU act dated 2013-06-26 (the EU slice's)
  gained the freshness header and the commits (rendered from the handler, verified without the switch; nothing else
  moved). `no-browser-probes.test.mjs` reads the planned probe line with a private copy's empty `--opening`. The legacy
  answers did not move with the four (the other three slices add no member to a Luxembourg answer). The census holds 86
  registries. A legal page carries the footer's freshness strip as built (it ships no script and is served as its file).
- **The journeys** (CI's; none run here): no expectation needed a change. They walk no legal page; the staleness banner
  stands outside `main`, where the first-screen check looks; the fixture mounts state no completed collection, so they show
  no banner; the footer's new links and strip come after every control a step reaches.
- **The owner's decisions of 2026-10-19:**
  - **O11, the maximum data age: 30 days** (option A; `V3Freshness.MaximumDataAgeDays`, which held the specification's 10
    as a driver decision). Every answer states it; the pages, the banner and promotion's data-age check read it from the
    answer, and no form names a figure. Over derive-c (Luxembourg collected 2026-10-12T18:17:04Z, the EU
    2026-10-05T03:50:08Z), no page shows the banner today and promotion's data-age check allows a release, until the EU
    half passes 30 days on 2026-11-04 at 03:50 UTC (Luxembourg's on 2026-11-11). Tests: the boundary is 30 days exactly
    and a second more; the mounted context's EU channel is 37 days old; a bound report 32 days old is stale; the web's
    population fixture answers on 2026-11-06 (EU 32 days, stale; Luxembourg 24, not). The envelope census rendered again
    (46 envelopes, `maximum_data_age_days` 10 to 30, nothing else). The 38 legacy schema-8 and schema-6 answers recorded
    again on Linux at 496eeced (WSL Ubuntu 24.04, .NET SDK 10.0.400, invariant globalization, UTC, the gated recorder): a
    script checked each body moved by its maximum alone and equals the recording; Windows replays the same bytes.
  - **Superseded the same day** by the owner's decision that the site names no person and gives no contact (the section
    above), so no e-mail is received or kept. **E-mails kept 12 months** once their matter is closed
    (`reportRetentionMonths: "12"`, the driver's recommendation). The privacy notice said "12 months" and "12 mois". The
    publisher's name, postal address and contact e-mail stayed placeholders, and the opening refused the pages until they
    were filled.
  - **The accessibility status: non-conformant** until an audit, the French review's recommendation (entry 13 of the
    addendum of 2026-10-18), in place of R62's "partially conformant". The English and the French are entry 13's, word
    for word, so no sentence is new; the WCAG 2.2 AA target and "not audited" stay. The review record has the driver's note.
- **Measured** (no browser was started on this machine; at 9c65c68a, the code of this pull request): `npm run build` in
  `web`, then a plain `npm test`: 1,313 tests, 1,310 passed, the 3 that start a browser skipped with their reason;
  `dotnet build Lex.V3.slnx -c Release`, 0 warnings, 0 errors; `Lex.V3.Tests`, whole, 3,327, 3,321 passed, 6 skipped; in
  `Lex.V3.Ingest.Tests` the classes the four slices and the decisions touch (the Census classes,
  `PublicRequestRecordingTests`, the answer, envelope and refusal-payload samples, the legacy schema-8 and schema-6
  classes, freshness, the EU authenticity, time view and branch, evidence bundle and permalink classes, MCP) 123, 120
  passed, 3 skipped (each by its variable: a real mount's build report, the legacy recorder, a named mount);
  `eng/verify-v3-tree.ps1` passes (1,663 tracked paths). The EU time view's
  `EuAsOfAnswersTheWordingOfTheDateAndNeverADateBeforeTheHistory` failed once while three suites ran at once on a shared
  machine (a fixture acquisition's observation not executed); alone it passed 6 of 6, and every later run passed it.
- **Review and repair (#989)**, one round, by the other model family: two findings in `deploy/deploy.ps1`, both taken, and
  CI's red on Linux, fixed.
  - **F1 (high): `-Public` never held the candidate's legal pages to the opening's rule.** `$openingArgs = if ($Public) {
    @('--opening') } else { @() }` is the string `--opening`: PowerShell unrolls the one-element array an `if` returns, and
    splatting a string hands it to `node` one character at a time (`- - o p e n i n g`). The probe never read `--opening`,
    so a `-Public` candidate whose legal pages still said a placeholder passed and was offered for promotion.
    `$browserArgs` had the same form. Fix: every argument list is `@(if (...) { ... })`, an array whatever the branch:
    `$openingArgs`, `$browserArgs`, and `$nodeTrap` and `$signerProbe` (two elements, so not broken, but the same form).
    No other script in `deploy/` held the form; `eng/verify-v3-local-tests.ps1`'s `$testArguments` did, across lines,
    with two or four elements (not broken), and was given the same form. Tests: `deploy-run.test.mjs` (new, 7 tests) runs
    the real `deploy.ps1` under pwsh with `az`, `oras` and `node` made up as PowerShell functions, and reads the
    arguments the probe receives: with `-Public`, exactly the token `--opening`, just before `--promotion-check`; with
    `-BrowserProbe`, `--browser` whole too, without the trap; without `-Public`, no `--opening`; a `-Public` candidate
    whose probe fails on a placeholder is deactivated and offered for no promotion. Before the fix all 7 failed. And
    `deploy-kit.test.mjs` refuses the form in every `.ps1` of `deploy/` and `eng/`, read across lines.
  - **F2 (medium): nothing made `-Open` come before `-Public`.** A `-Public -Apply` on the still-private app deployed with
    `openToPublic` (main.bicep empties `ipSecurityRestrictions`), which removed the address rules before the candidate
    was probed; a failed probe deactivated only the candidate, and the live revision stayed open to everyone. Fix: with
    `-Public`, before anything in Azure changes (after the release is read back, before the image is copied), the app's
    `properties.configuration.ingress.ipSecurityRestrictions` is read; any rule at all, whatever it names, stops the
    deployment with "The site is private: open it with deploy.ps1 -Open first" and the rules it found, and a read that
    fails or is not JSON stops it too, having changed nothing. A plan run says it would check this and asks Azure nothing.
    Tests (`deploy-run.test.mjs`): with the private copy's rule, two rules (one naming no range), a failed read or a read
    that is not JSON, no image is copied, no template previewed or deployed and no revision or rule touched; on the open
    site (`[]`, `null` or nothing) the read comes before the copy and the deployment, which go on; the private copy's
    deployment asks nothing before it deploys.
  - **CI red on Linux (dotnet and image-conditions lanes, run 39214367267):** the envelope census's `package_sha256` of
    the EU act of 2013-06-26 (`7ac5d154...`, recorded on Windows) differed on Linux. `RewrittenPackage` re-zips the GDPR's
    package with `ZipArchive`, which writes the system into each central-directory header ("version made by": MS-DOS on
    Windows, Unix elsewhere) and that system's default external attributes. `PortableZip.Normalise` sets each header to
    what Windows writes (made by MS-DOS, the entry's own zip version kept, no attributes), in the four deterministic
    rewriters (`RewrittenPackage`, the synthetic French packages of the consolidated fixture and of the acquisition
    fixture, the main-body test's `RewriteEntry`); the other two zip writers in the tests stamp the time of the run, so
    no recording can hold their bytes. The deflated bytes are the same on both systems. `PortableZipTests` pins the four
    packages' digests and normalises a zip given Unix headers to Windows's bytes. Rendered again on Windows with
    `V3_RENDER_ENVELOPE_SAMPLES`, the census did not move; verified without the switch on Windows and in WSL Ubuntu 24.04
    (.NET SDK 10.0.400, UTC, with invariant globalization and without).
  - **Checked, no finding:** the conflict resolutions; the legacy fixture; the 30-day boundary; the probe's exit codes;
    privacy; the EU authenticity statement.
  - **Measured** (no browser started, no Azure call: `az`, `oras` and `node` made up in the deploy tests): `npm run build`
    in `web`, then a plain `npm test`: 1,321 tests, 1,318 passed, the 3 that start a browser skipped with their reason (8
    new: `deploy-run.test.mjs`'s 7 and the form's rule); `deploy-kit.test.mjs`, `deploy-run.test.mjs` and
    `no-browser-probes.test.mjs` together 33 of 33; `deploy/validate.ps1` passes; `dotnet build Lex.V3.slnx -c Release`, 0
    warnings, 0 errors. Windows (Romance Standard Time): `V3EnvelopeSamplesTests` and `PortableZipTests` 4 of 4; the
    Ingest classes the zip fix touches (the EU acquisition, consolidated, main-body, population, Europe index, annex
    control, time view, authenticity, machine and mounted gates classes and the three censuses) 291, 289 passed, 2
    skipped by their variables. WSL Ubuntu 24.04 (.NET SDK 10.0.400, UTC) at 41974eb0: `V3EnvelopeSamplesTests` and
    `PortableZipTests` 4 of 4 with invariant globalization and 4 of 4 without; the answer, envelope and refusal-payload
    censuses with the zip tests 10 of 10, invariant; the whole `Lex.V3.Ingest.Tests` with the language library, 3,414,
    3,377 passed, 37 skipped, none failed. `eng/verify-v3-tree.ps1` passes.
- **Waits on the owner:** nothing for the legal pages since the owner's decision of 2026-10-19 (the section above: no
  person, no contact; `web/scripts/legal-publisher.mjs` removed). Until then: the publisher's name, postal address and
  contact e-mail.

## Freshness on every page and in every answer, and the commits behind it (sole driver, 2026-10-18)

The plan's items 6.1 ("Freshness on every page and in every answer") and 6.5 ("The answers and the footer strip name the
commits, labelled 'index built from' and 'service running'"), to the specification of record's R87, R69, R107 and R23.
Facts only from what the mount and the server hold; every count is the answer's (R11).
- **Every answer** (REST and MCP alike, from the one host): the envelope's `context.freshness` keeps `observed_at` and
  `upstream_health` and gains `index_built_at` (the mounted Luxembourg log's newest build; null when it records none, an
  EU index records none), `maximum_data_age_days` (10; 30 since the owner's decision of 2026-10-19, above), `stale`, `channels` and `last_checked_against_publisher`. Each
  channel, one per publisher whose index is mounted, Luxembourg first, names `newest_completed_observation` (when its
  newest acquisition completed, its catalog retained, as the mount's `build-report.json` states it for a half acquired
  across an interruption, `resumption.{half}.catalogedAt`, read down to the second; null when nothing states it),
  `age_days` (whole days elapsed at the answer), `stale` (past the maximum), `lag_days` (null) and `lag_observations` (0):
  no index schema holds an observation time (index 8 and 9 require `observed_from` to be null; plan 1.12 has not run) and
  no refresh has run, so no lag is measured and none is stated. `last_checked_against_publisher` is null until the first
  monthly re-check (R69). `context.commits` names `index_builder_source_commit` (the report's `indexBuilderSource`, which
  the mount writer now records from its build's stamp, or a re-derived mount's `derivation.toolSource`; a full commit or
  null) and `serving_runtime_source_commit` (the commit the SDK stamped in the API assembly's informational version, named
  by the server's own host: `V3PlatformHost.StampedServingRuntimeSourceCommit`). The build report is held to the mount: an
  artifact it names that is not the mounted one refuses the mount, and a report naming no corpus states nothing.
- **Every page** (`Freshness.jsx`, `freshness.mjs`, `freshness-client.jsx`): the footer's last strip says "Index built on
  {date}.", each channel's "{channel}: last complete collection ended on {date}, {count} full days before the server
  answered on {today}; lag not yet measured." (or that its completion is not recorded), and "Index built from" and
  "Service running" with each commit whole, or "(not recorded)". Dates are the server's, in Luxembourg. Past the maximum
  age a banner under the header names each stale channel ("Some texts may be out of date", its sentences one step away so
  the home page keeps its box and the sentence under it on a phone's first screen, R47). A page the server renders from an
  answer (home, reading, dossier; both servers, through the one renderer) says them from that answer; a page as built says
  the strip's as-built line, and every answer `askV3` reads afterwards redraws strip and banner (outside the hydrated root),
  with no request of its own. The "last checked against the publisher" line is absent: the reader refuses any re-check, and
  any stated lag.
- **Promotion's data-age check** (`deploy-probe.mjs`, `dataAgeFailures`): a channel past the maximum, or one whose
  completion is not stated, refuses promotion. With `--promotion-check` (which `deploy.ps1` passes) a candidate that
  answers as its release holds exits 3 then: the kit keeps it, with no traffic, and prints no promotion or rollback
  command. The rehearsal records it beside its probes (`servedPages.promotion`), never as a failure. The deploy probe's
  home-page check sets the strip and the banner aside when it holds a rendered page to the page as built; the whole
  rendering still holds them.
- **On the population** (derive-c's report read as the server reads it, `ARealMountsBuildReportIsReadAsTheServerReadsIt`):
  Luxembourg collected 2026-10-12T18:17:04Z, the EU 2026-10-05T03:50:08Z, the index built 2026-10-12T18:17:05Z, and no
  index commit recorded. So on 2026-10-19 every page says Luxembourg 6 full days, the EU 14, shows the EU banner, says
  "Index built from (not recorded)", and **promotion refuses any release over derive-c**: its EU data is past the maximum.
  (With the owner's 30 days of 2026-10-19, above: no banner and no refusal until the EU half passes 30 days, 2026-11-04.)
  The private copy's live revision is untouched. The ways forward are an EU refresh (publisher traffic, the owner's per-run
  authorisation) or the owner's own maximum for O11.
- **French** (Decision 41): 15 forms reviewed by an independent reviewer (Claude, AI reviewer, not the author) in the
  addendum of 2026-10-19 "freshness and the commits" of `C:\lex-v3\lanes\fr-review\review.md`; 12 changed, all applied,
  the English with the four blocking ones: the count of whole days was printed between two calendar dates that can be a day
  further apart, so it is said before the server's answer; the commit labels are terms ("Révision du code ayant construit
  l'index"), the lag "le décalage entre publication et collecte". The forms are named in `live-chrome-fr.mjs`'s header.
- **Driver decisions (reversible):**
  - **Superseded on 2026-10-19** (the owner answered O11: 30 days, above). The declared maximum data age is 10 days, O11's
    recommended answer, until the owner answers O11
    (`V3Freshness.MaximumDataAgeDays`; every answer states it, so the pages never print a figure of their own).
  - A channel's completion is only the catalog time the build report states; a half acquired in one window is "not
    recorded" rather than dated by the index's build time, which bounds the Luxembourg half and says nothing of an EU half
    a build reused (derive-c's EU half is 7 days older than its build). Promotion refuses such a mount until the mount
    writer states each half's completion (next, data lane).
  - Unstated is refused at promotion, as stale is: a release must show its data's age is within the maximum.
  - The registry digest does not move: the published envelope schema keeps `context` an object, so the new members are
    held by `V3EnvelopeJson`'s closed sets and the web reader; describing `context` in the schema (which moves the digest)
    is next.
  - R23's other three fields (`corpus_commit`, `manifest_set_id`, `content_digest`) are not added: V3's corpus is
    content-addressed (`snapshot_sha256` names it) and the plan's item names the two commits; next.
  - A host made without a commit (tests, censuses) names none; the envelope samples name fixed commits (`e`×40 serving,
    `f`×40 for a mount the test build wrote) as they name fixed digests.
  - The staleness alert to the owner (R107) needs monitoring the private copy does not have: next, with R107.
- **Samples and legacy answers:** the envelope samples re-rendered from the handler (additive members only), then verified
  without the switch. The 38 recorded schema-8 and schema-6 answers all changed, legitimately and only in `context`:
  every envelope gains `commits` and the new `freshness` members; no result, refusal or registry digest moved (below).
- **Measured** (no browser was started on this machine; this branch on v3/integration 0b908870): `npm run build` in `web`,
  then a plain `npm test`: 1,282 tests, 1,279 passed, the 3 that start a browser skipped with their reason;
  `dotnet build Lex.V3.slnx -c Release`, 0 warnings, 0 errors; `Lex.V3.Tests` 3,326, 3,320 passed, 6 skipped (the
  vocabulary registry census and its partition total, which the first run failed on the new registry, re-run in their
  class: 30 of 30); in `Lex.V3.Ingest.Tests` the classes the slice touches and those named for it (the Census classes,
  `PublicRequestRecordingTests`, the answer, envelope and refusal-payload samples, the two legacy classes, freshness, MCP,
  index 9 serving, generation quotes, the offline mount, first mount build, mount build file and EU resume classes, the
  machine and mounted gates, journey mounts, coverage) 199, 189 passed, 10 skipped (each by its variable); the samples
  rendered from the handler and verified without the switch; the legacy answers recorded on Linux and replayed on Windows
  (11 of 11, 1 skipped by its variable); `eng/verify-v3-tree.ps1` passes (1,652 tracked paths).
- **Next:** each half's completion in the build report whether resumed or not (data lane); the lag, once observation
  times are carried (1.12) and the refresh runs (Phase 10), with its own reviewed wording; the re-check line (R69) after
  the first monthly re-check; the staleness alert (R107); the context's published schema; R23's remaining fields.

## The EU authenticity line by the act's date (sole driver, 2026-10-18)

The owner decided PR #975's question on 2026-10-18 (item O4, a legal claim; the owner's words: "yes yes go with what you
recommend"). Every EU text Lex serves, and every EU citation's evidence form, now ends with the statement the act's date
chooses, to the specification of record's R20 ("the electronic Official Journal is authentic since 1 July 2013
(Regulation (EU) No 216/2013), the printed edition before"). STATUS.md records the decision among the owner's items.
- **The two statements** (`V3CorpusMount.EuropeTextAuthenticity`, the owner's words, by basis):
  - `electronic_edition`, unchanged: "Only the Official Journal of the European Union published in electronic form is
    authentic and produces legal effects (Regulation (EU) No 216/2013, Article 1(2)); this text is a reproduction read from
    the Publications Office's Formex package, not the authentic edition."
  - `edition_by_publication_date`, new: "For an act published before 1 July 2013, the printed Official Journal of the
    European Union is authentic; the electronic edition is authentic since that date (Regulation (EU) No 216/2013). This text
    is a reproduction read from the Publications Office's Formex package, not the authentic edition."
- **The rule for the cut-off** (`EuropeAuthenticityRule`, served as `authenticity_rule`; a driver decision, reversible):
  - The EU index holds no Official Journal publication date, so the act's own date chooses.
  - The dates read (`authenticity_act_dates`, once each, in order) are those of the act the text belongs to, never a
    consolidation's: the Formex date of its original wording, when that text is held, and the publisher's
    `cdm:work_date_document` of the act's own work, from the census facts the index holds for every seed act.
  - `electronic_edition` when a date is held and the earliest is on or after 2013-07-01. An act is published on or after
    the date it bears, so it was published when only the electronic edition is authentic.
  - `edition_by_publication_date` otherwise, and when no date is held. Its sentence states which edition is authentic on
    each side of 1 July 2013 and not on which side the act was published. So it stays true for an act dated in June 2013
    and published in July. The two acts of 26 June 2013 (CRD IV and the CRR) say it; they were published on 27 June 2013.
  - A consolidated version says its act's statement: a consolidation of 2024 of an act of 2003 says the dated one.
- **What the data holds, checked** (derive-c, read-only): the original wording's date is the ACT entry's
  `BIB.INSTANCE/DATE`, the act's own date (the GDPR's 2016-04-27; it was published on 4 May 2016). The census's
  `work_date_document` is the act's date too; the two differ for one act (32022L2523, 14 and 15 December 2022), on the same
  side of the day. The Formex package's `.doc.xml` names the Official Journal issue's date (`PUBLICATION.REF/DATE`,
  2016-05-04 for the GDPR), but no index reads it: the producer reads only the ACT or CONS.ACT entry. So the act's date is
  read, as decided; reading the publication date is a data-lane slice (next).
- **The answer** (`V3CorpusMount.cs`, `V3CorpusMount.EuropeTime.cs`; additive, the request schemas unchanged, so the
  registry digest did not move): both EU evidence bundles, the time view's and the original wording's, carry
  `authenticity` (the chosen statement), `authenticity_basis`, `authenticity_act_dates` and `authenticity_rule`. The
  original-wording path reads the census's dates when a seed act's census holds the work (the same statement on both
  paths), else the work's own wording dates, and none for a sector-0 CELEX. MCP answers from the same handler; its tool
  descriptions name no statement, so none changed.
- **The pages and files**: `readEuropeEvidenceBundle` (`reading-answer.mjs`) holds the statement to its basis
  (`EUROPE_AUTHENTICITY_SENTENCES`, the platform's words) and the basis to the act's dates, and throws on drift. The
  reading's lead and the export's JSON, CSV and PDF say the answer's statement. The citation generator's evidence form
  says it in the cited text's language (`citation.europe.authenticity.electronic_edition` and
  `.edition_by_publication_date`, in both tables).
- **French**: both forms were reviewed by an independent reviewer (Claude, AI reviewer, not the author) in the addendum of
  2026-10-18 on the EU authenticity statement in `C:\lex-v3\lanes\fr-review\review.md`, and are named in
  `live-chrome-fr.mjs`'s header: 2 forms, none changed. The dated one: "Pour un acte publié avant le 1er juillet 2013,
  l’édition imprimée du Journal officiel de l’Union européenne fait foi ; l’édition électronique fait foi depuis cette date
  (règlement (UE) n° 216/2013). Ce texte est une reproduction …". The reviewer's optional points on the owner's English go
  to a lawyer's read (O4; the owner's choice, not before the opening), unchanged here: the gazette was "of the European
  Communities" before 1 February 2003 (Regulation (EC) No 1/2003); "is authentic since" could read "has been"; art. 3's
  exception is in neither sentence.
- **The population** (derive-c's EU index, read-only with `python -I`, under the rule above): 82 seed acts, 70 with a
  wording's text held. Of those 70, 54 say the electronic edition's statement and 16 the dated one (32003L0087 to
  32013R0575; #975's text counted 13 of 63 by another measure). 29 have their original's text held; the other 41 are dated
  by the census's date alone, and 31 of them say the electronic statement only because of it. No act's two dates fall on
  either side of the day. Through the handler (derive-c mounted read-only in a probe test that was not committed; every
  dated wording whose text is held, in each language): 538 bundles asked, 472 answered and 66 refused
  `ambiguous_version` (two different texts on one date, as before); 68 acts answered, 53 with the electronic statement
  and 15 with the dated one, and no act with both (32005L0029, dated, and 32023R1114 have only ambiguous wordings). Of the
  answers, 193 say the dated statement (181 of them consolidations of acts dated before the day) and 279 the electronic
  one; every statement is its basis's. The original-wording path, asked by each answer's first article, answered the
  same basis for all 58 originals (it serves original legal text only, so the 414 consolidations' articles are unknown
  to it, as before).
- **Tests**: `V3CorpusEuropeAuthenticityTests` (new): the owner's two sentences word for word; the day before, the day and
  the day after, June 2013, none held, two dates; the 16 acts; and, over a derived mount whose original is dated
  2013-06-30 or 2013-07-01 (a fixture's date in the GDPR's package) with a consolidation of 2024, the original and the
  consolidation in the time view and the original by an article identity. The time-view and evidence-bundle tests hold
  the GDPR's statement, and both paths alike. The envelope census gained an EU act dated 2013-06-26 (a fixture's date),
  which the web's citation tests read; the web tests cover the reader's rules, the reading's lead, the evidence form in
  English and French, and the JSON, CSV and PDF.
- **Measured** (rebased on `v3/integration` at #988's merge, 0b908870), with no browser started: `dotnet build Lex.V3.slnx
  -c Release` 0 warnings, 0 errors; `Lex.V3.Tests` 3,321 (3,315 passed, 6 skipped); the `Lex.V3.Ingest.Tests` classes
  touched (the authenticity, EU time view and branch, EU evidence bundle) with the samples (envelope, answer, refusal
  payload), the census classes, `PublicRequestRecordingTests`, MCP and the two legacy classes, 136 (133 passed, 3
  skipped: the recording gate and two live canaries); the envelope census rendered with `V3_RENDER_ENVELOPE_SAMPLES` and
  verified without it; the legacy schema-8 and schema-6 answers unchanged (LU-only mounts carry no EU statement, and the
  registry digest did not move), so not recorded again; the web build, and the web suite 1,277 (1,274 passed, 3 skipped;
  a first run failed the date-format check, which now leaves out the EU statement, and a locale build that lost its
  temporary folder under load); `eng/verify-v3-tree.ps1` 1,647 tracked paths.
- **Not in this slice** (next): the Official Journal's publication date read into the EU index, so the cut-off reads it
  (a data-lane slice); the regulation's art. 1(2) and art. 3 checked against its text, and the reviewer's three optional
  points, at a lawyer's read if the owner books one (O4; the opening does not wait for it, the owner's decision of
  18 October).

## The five legal pages, in French and English, and an opening that waits for them (sole driver, 2026-10-18)

The plan's item 7.1 ("Five footer pages, in French and English"), to the specification of record's R49, R50, R62, R70, R74
and R92 and the owner's items O5, O28 and O29, made as small as the opening needs: every statement one the code or the
deployment supports, every detail only the owner can give a marked placeholder in one data file.
- **The pages** (`web/app/LegalPage.jsx`, `web/scripts/legal-pages.mjs`; the words in `live-chrome.mjs` and
  `live-chrome-fr.mjs`, sections `legal`, `legalNotice`, `privacy`, `terms`, `accessibility` and `cookies`): `/{fr,en}/legal-notice.html`
  (the publisher, its address and contact, director of publication; independence from the State, the Service central de
  législation and the Publications Office; the host), `privacy.html` (the controller; what a visit sends and that nothing of it
  is kept; no cookie, tracker or other origin; e-mails, their use and retention; people named in published acts, that a search
  engine may index an act's page only once the indexing record (R92's check) lists it, and their objection; Microsoft as
  processor; the rights of arts. 15 to 18 and 21 and the CNPD complaint), `terms.html` (R50's sentence
  and the authenticity line, word for word; the texts' licences: Legilux CC BY 4.0 for the files the rights rule admits, "©
  European Union, https://eur-lex.europa.eu" and Commission Decision 2011/833/EU for EU texts; no restriction beyond the texts'
  own licences; no warranty, within what the law allows; how to cite; how to report an error), `accessibility.html` (WCAG 2.2 AA
  as the target, not audited, so "partially conformant" (R62); what has been done; what has not been checked; how to report a
  barrier; the loi du 28 mai 2019, which binds public sector bodies, not Lex) and `cookies.html` (none set, none through
  another site, how that is checked). Each is built under each interface language with no script, the site's header and
  footer, its language links pointing at the same page in the other language, hreflang alternates, and the never-indexed
  robots element (R67: not a law's page; whatever the switch). Every live page's footer now lists the five (`SiteFooter`,
  `legal.menu`), the page that is one of them marked current.
- **Superseded on 2026-10-19** (the owner's decision, above: the site names no person and gives no contact; the data file
  and its placeholders are removed). **What the owner fills** (`web/scripts/legal-publisher.mjs`, one value each, between the quotes; filling it publishes it on
  the site and in this public repository):
  - `publisherName`, «PUBLISHER_NAME»: the publisher, director of publication and controller (O5 recommends the owner);
  - `publisherAddress`, «PUBLISHER_ADDRESS»: a postal address on one line;
  - `contactEmail`, «CONTACT_EMAIL»: one dedicated contact address (O5), the pages' only contact for the publisher, errors,
    barriers, privacy requests and objections;
  - `reportRetentionMonths`, «REPORT_RETENTION_MONTHS»: how many months an e-mail is kept once its matter is closed (GDPR art.
    13(2)(a); R74's "stated short period"; the driver recommends 12; **the owner decided 12 on 2026-10-19**, above).
  A detail still unfilled is printed as its placeholder, marked `data-legal-placeholder`; one filled in a form the pages cannot
  print (an e-mail that is none, a retention that is no whole number of months from 1 to 99, a stray space or line break, an
  unknown field) stops the build (`malformedPublisherFailures`, `build-live.mjs`).
- **Changed on 2026-10-19** (above: the check now refuses a page showing an e-mail address, a mail or telephone link, a
  placeholder or a postcode). **The opening fails closed while a placeholder remains** (`openingFailures`, `deploy-probe.mjs --opening`): `deploy.ps1
  -Open` reads the app's address and asks each of the ten legal pages before it touches an address rule; any page that does not
  answer 200 with a page, sets a cookie, says a placeholder or marks one, or cannot be asked, stops the opening and the site
  stays private. Every `-Public` deployment passes `--opening` to its candidate's probe, so a release with an unfilled detail
  fails its probe once the site is open, is deactivated, and is never offered for promotion (true since the repair of #989:
  before it the probe received `--opening` one character at a time). A `-Public` deployment while the app still has an
  address rule is refused before anything in Azure changes (#989, F2). The candidate probe (`servedPageFailures`, also the image rehearsal's) now holds
  each legal page the release image holds to its own bytes, the page's CSP and no cookie, says beside a private copy's pass
  that the pages are not ready to publish, and fails a home page or legal page that sets a cookie.
- **What each statement rests on** (read or measured on 2026-10-18):
  - no cookie, no browser storage, no other origin: the CSP (`csp.mjs`: every source `'self'` or `'none'`), the journeys
    (`document.cookie` empty, both storages empty, no request off the page's origin, CI), the release's image probe (142
    requests on the release of 18 October, no answer setting a cookie, no other origin), the private copy's own answers read
    through Azure's ingress (`/`, `/fr/`, `/en/coverage.html`, a 404: no `Set-Cookie`), no `localStorage`, `sessionStorage`,
    `indexedDB` or cookie write in `web/app` or the built client bundles, no sticky-session affinity in `main.bicep`;
  - nothing kept of a request: `PublicRequestRecordingTests` (planted query text, addresses, agents and headers found in
    nothing the handler writes, emits or answers but the caller's own answer), the API's cleared logging providers
    (`Program.cs`), the image probe's privacy record (nothing written once the server first answered);
  - the hosting: `rg-lex-v3` in France Central (every resource but the DNS zone), the environment `cae-lex-v3` read back with
    no log destination (`appLogsConfiguration.destination` null), no Application Insights and no OpenTelemetry, and no
    diagnostic settings on the environment or the app (read-only `az` reads by the driver's identity); Microsoft as processor
    under its own terms (the contracting entity and its data protection addendum are not named: the owner's account holds them);
  - no rate limiter is named, because none ships yet (R109 is not built); the referrer: `Referrer-Policy: no-referrer` on every
    page (`V3WebRoot`); the texts' licences: rights admission policy /1 (two channels agreeing on CC BY 4.0) and Decision 95's
    acknowledgement (`V3CorpusMount.EuropeTextAcknowledgement`); the PDF not tagged (`export-pdf.mjs`);
  - the CNPD's name and address and the 2019 law's title, read from cnpd.public.lu and guichet.public.lu (no request to
    Legilux or EUR-Lex).
  `web/test/legal-pages.test.mjs` holds the pages to these where code can: R50's sentence on the terms page, the CSP, the
  referrer header, the cleared logging, the template's absences, the acknowledgement, the law's title marked French.
- **Both servers** serve the pages as the files they are: the site table (`site-paths.json`) gains ten rows with a `file`
  member, held by `V3WebRootTests` (the API: each served with a page's headers and noindex) and `legal-pages.test.mjs` (the live
  server over a real build: 200, noindex, no cookie, the built bytes).
- **French** (Decision 41): the 97 forms (every string of the six sections) were reviewed by an independent reviewer (Claude,
  AI reviewer, not the author) in the addendum of 2026-10-18 "the five legal pages" of `C:\lex-v3\lanes\fr-review\review.md`
  (with the driver's note of 2026-10-19); 14 changed in review, all applied: "l’éditeur de Lex" for the site's publisher,
  bare "éditeur" staying the law's publishers; "ne doit apparaître" and "ne sont autorisés à" where the draft said "cannot";
  the EU acknowledgement quoted in English, as the texts carry it (`{eurlexAcknowledgement}`, marked `lang="en"` on the French
  page); "le texte cité" for a quotation, "citation" for the reference; "Veuillez ne faire figurer"; "fournisseur de
  messagerie"; no "servir"; no comma before a list's closing "et". Its script's 2,249 checks pass on the forms as shipped.
  - **For the owner (a public claim; decided on 2026-10-19: non-conformant, above)**: the review recommends declaring the
    accessibility status "non-conformant" until an
    audit, as the Luxembourg State's statement generator (accessibilite.public.lu) declares an unaudited public-sector site.
    The page keeps R62's "partially conformant" (O5's recommended answer; a self-assessment with its known non-conformities
    listed) until the owner chooses; the other wording is one entry in each table (`accessibility.sections.status.text`).
  - The lawyer's read (O4) of these pages is the owner's choice and **has not been done**: the owner decided on 2026-10-18 to
    open without it. The addendum lists what it would cover: what a private publisher must disclose in Luxembourg (the loi du
    14 août 2000 relative au commerce électronique, the loi du 8 juin 2004 sur la liberté d’expression dans les médias) and
    whether a director of publication applies; the host named by its legal entity; transfers outside the EEA (art. 13(1)(f));
    legitimate interest for names in published acts and how an objection is handled; the warranty sentence against consumer
    law; no consent needed with nothing stored (loi modifiée du 30 mai 2005); whether the European Accessibility Act's
    transposition reaches the site.
- **Driver decisions** (reversible):
  - The fifth page is the cookies page, as this slice's brief asks; the plan's "sources and licences" page (R70, Reg53) is not
    built here: the terms carry the licences in brief.
  - The pages are never indexed, whatever the switch (O27 concerns the law's pages); they ask nothing and ship no script.
  - Addresses keep English file names under both languages (`/fr/privacy.html`), as `/fr/coverage.html` does.
  - "The publisher of Lex" names the site's publisher everywhere, so it is never read as Legilux or the Publications Office.
  - Retention is the owner's number, not the driver's (a commitment only the owner can keep).
  - The privacy notice names no processor the site does not use: no monitor, no fallback host and no rate limiter exist yet. The
    DNS zone (Azure DNS in `rg-lex-v3` since the owner's switch of 2026-10-18) is Microsoft's, as the host is, and answers
    resolvers, not readers, so it is not named apart.
  - Search engines (O27, answered yes on 2026-10-18): the privacy notice says what the code does, that an act's page is opened
    to them only once the release's indexing record lists the act (`search-engines.mjs`: R92's check, which every release
    runs since `data/names-r92`), and
    no more; when R92's measurement ships, its section on named people names the check.
- **Not in this slice** (next): the sources and licences page (R70, Reg53); the security contact (`/.well-known/security.txt`,
  R74) and "Report a problem with this page" (R74, O29); the mailbox's provider in the privacy notice (once the owner names the
  address); Lex's own data under CC BY 4.0 (O6 (5)); API terms; R92's measurement of acts that name people (O28); the
  incident and breach runbook (7.4, R108); the lawyer's read (O4, 7.5).
- **Measured** (no browser was started on this machine; this branch, rebased on `v3/integration` at 0b908870, #988's opening
  kit): `npm run build` in `web`, then a plain `npm test`: 1,282 tests, 1,279 passed, the 3 that start a browser skipped;
  `dotnet build Lex.V3.slnx -c Release`, 0 warnings, 0 errors; in `Lex.V3.Tests`, filtered, `V3WebRootTests` and
  `V3WebRootSearchEnginesTests`, 21 of 21; `deploy.ps1` parses with no error, and `deploy-kit.test.mjs` runs its `-Open` path
  with a stand-in Azure CLI and node (the check first, at the app's address, under the browser trap; a failed check or an
  unread address removes no rule); the French review's script, 2,249 checks on the forms as shipped; `eng/verify-v3-tree.ps1`
  passes. Before the rebase (from 24118293): the same web suite, 1,280 tests, 1,276 passed, 1 failed and fixed
  (`no-browser-probes.test.mjs` now expects the image probe's note that the stand-in image holds no legal page).

## Today in Luxembourg on every page and in the address, and a search kept after "#" (sole driver, 2026-10-18)

The plan's items 3.4 ("'Today' is the calendar date in Luxembourg. It is always printed, written into the address and the
trace, and shown beside the index date when the index is older. It uses 0.8's coded rule and the clock-change probe") and
3.5 ("A search lives in the address after '#', so it can be shared. The words travel in the request body, never in an
address that a server could log"), to the specification of record's R13 and R109. Branch `writer/today-and-search-address`.
- **Today, one rule** (`temporal.mjs`: `luxembourgDateOrNull`, `todayInLuxembourg`; the API's `LuxembourgCalendar`, and
  `V3ApiHandler.LuxembourgTodayAt` for a readable address): read at the instant the server answered when the page shows an
  answer (`context.freshness.observed_at`), so the page the server rendered and the page hydrated name one date; else this
  browser's clock once hydrated (`Today.jsx`, `useLuxembourgToday`); never the build's time; a clock outside the rule's years
  names no date.
- **Always printed** ("Today in Luxembourg: {date}.", the reviewed `form.today`): beside the date field of the home and
  search pages (as before), of the reading form (and the export composer, which shares it), and beside the two dates of
  Compare and of Changes; on its own line under the dossier's count of versions held. The 404 page's box is the home form.
- **Beside an older index's date**: the home page's coverage line and a search's population ("…, as of the index built on
  {date}; today in Luxembourg is {today}.", `home.coverageOlder`, `search.searchedOlder`) when the index's build date in
  Luxembourg is before today (`indexOlderThanToday`); an index built today says its date alone. The footer's build date is
  6.1's.
- **Written into the address**: an act's readable address keeps the date it was read at in its query, `?date=yyyy-mm-dd`.
  - Both servers render a readable address at the date its query keeps (its first `date` parameter, a calendar date; any
    other value is today), with or without a clock (`V3WebRoot.DateOfQuery`, `permalinks.mjs` `dateOfQuery`), held to the
    shared table (`site-paths.json`, eleven new rows). A permalink names its date in its path and a dossier none: neither
    reads one.
  - The page, once it has read the act at an address with no date, writes that date into its own address
    (`withDateQuery`, `history.replaceState`: no history entry, no history state), so a link a reader copies keeps it. The
    language menu's links carry the date and a reading's page (`sameAddressIn`), and the fragment.
  - A reading of another day than today says so first: "This page reads the act on {date}, as this site holds it for that
    date; today in Luxembourg is {today}." with the link to the act at today's date (`reading.notToday`, `NotToday`). Its
    whole act and its pages link to the version shown by its permalink; at an undated readable address they stay the act's
    own address (`readsAnotherDay`, decided from the answer alone: the date it was asked for against the day it was given
    on, so the server's render, which reads no query, and the browser's hydration say one thing, and a page asked just
    before midnight and answered after it is said truly).
- **The trace**: every request that answers as of today names its date (the home page's search, a readable address's
  reading), the answer echoes it (`requested_date`, or the reference phase's `today_in_luxembourg`), and its
  `observed_at` is the instant today is read from. No envelope member was added; the registry digest is unchanged.
- **The clock-change probe**: the date on both sides of each change, around 25 October 2026 and 28 March 2027, in the API
  (`AReadableAddressIsAskedAtTodayInLuxembourgOnBothSidesOfEachClockChangeOrAtTheDateItKeeps`), the live server
  (`live-ssr.test.mjs`, its clock moved across both changes) and on the pages (`today.test.mjs`: the dossier, the reading's
  form, the search's population and the home page's coverage line, at every instant of the shared table around a change);
  the deploy probe's own (0.8) is unchanged.
- **A search after "#"** (`search-address.mjs`): `#q={phrase}&lang={language}[&work={identifier}][&date={yyyy-mm-dd}]`,
  each value as `encodeURIComponent` writes it.
  - Written when the reader asks a search (the address replaced, never pushed); read once the page has hydrated, and on
    `hashchange`, and asked as the reader would; the words still go to the API in the request's body alone. The lane is not
    written (the site asks the strict lane) and neither is a later page's cursor (a link opens the first page).
  - A fragment with no `=` is a position (the skip link's `#main`): nothing is asked or said. A fragment written as a search
    that the form will not send (a key it does not know, a key twice, a value that does not decode, an empty value, no
    phrase, a language not offered, a date that is no calendar date, a work named on the home page, a phrase past the
    platform's ceilings, a fragment past 8,192 characters) is refused whole: nothing is asked and the page says "The search
    in this page's address could not be read, so nothing was searched." (`search.addressUnread`), repeating none of it.
  - A search read from the address that opens an act (an exact reference) shows the act's link and does not go there by
    itself, so the browser's Back never loops through it; a reader's own submit still goes there.
  - The search page's introduction now says where the phrase goes and where the page keeps it (`search.intro`).
- **No words in any request address**: `request-addresses.test.mjs` scans the import closure of every page's browser entry
  for every way to send a request or change an address (fetch, XMLHttpRequest, sendBeacon, EventSource, WebSocket, an
  image's address, a dynamic import, a navigation, the history, the page's own address, a form's own submit) and pins each
  of the six it finds, line for line, with why it carries no word; every live screen asked with words asks exactly
  `POST /api/v3/{operation}`, no query, no fragment, the words in the body; and no page's form has an action or a method, no
  control a name. The 404 page's box keeps nothing in its address (`data-address="none"`). `PublicRequestRecordingTests` is
  unchanged and passes.
- **The journeys** (CI's lane): a page ends at its own address with what it writes there (`endAddressesOf`: a search after
  "#", built from the very request the step must send; a readable address with `?date=`, on either of a run's two days), the
  language menu is held to that address, and a new fixture-mount run opens the search page at a kept search and must ask it
  once and stay there (`KEPT_SEARCH_STEP`).
- **French** (Decision 41): 5 forms (`search.intro`, changed; `search.searchedOlder`, `search.addressUnread`, `home.coverageOlder` and
  `reading.notToday`, new) were reviewed by an independent reviewer (Claude, AI reviewer, not the author) in the addendum of
  2026-10-19 "today beside the dates, and a search kept in the address" of `C:\lex-v3\lanes\fr-review\review.md`; 2
  changed in review, the English with them, both applied: the phrase is never in "the request's address" (the draft's "an
  address a server could record" was false of a shared link, which mail and chat servers can record) and the part after
  "#" is what the browser never sends (the clause had attached to the page's address); a reading of another day "porte
  sur" the act, true above a refusal too, as this site holds it "for that date" ("pour cette date"). Today inside a
  sentence is "au Luxembourg, nous sommes aujourd’hui le {today}"; the reviewed `form.today` is confirmed in its four new
  places. 154 checks by script; the forms are named in the header comment of `live-chrome-fr.mjs`.
- **Driver decisions** (reversible):
  - The date goes into a readable address's query, not its fragment: R64 keeps "#" for a position inside an article, and a
    server must render the dated reading (R67) and cannot read a fragment. A date is digits and dashes, no word (R109).
  - The dated address stays a readable address, never a permalink: it names no digest, so it says the act this site holds
    for its date (a version recorded later for that date is the one it then shows); the permalink still pins one version.
    Its canonical is the readable address with no date while the version shown is today's, else that version's permalink,
    so the release's switch never opens a superseded version (`readingAddressShown`).
  - The page writes the date into the address bar by replacing the address; the server never redirects an undated readable
    address (it stays the canonical page the sitemap lists, plan 3.6).
  - The date fields of the reading form, Compare and Changes stay empty, as before, with today said beside them: a field
    filled for the reader would be a date they did not choose (and the journeys type into those fields).
  - The dossier says today on its own line; it marks no version "applicable today" (an act held only as published may have
    been amended since: its readable address says so already).
  - "Today" is the one relative word a screen says, bound to its date: the accessibility test's date rule admits it only
    followed by the date ("Today in Luxembourg: ", "today in Luxembourg is ", and the French).
  - The 404 page's box keeps no search in its address: the page stands at an address this site has no page for.
  - A fragment with no language takes the form's first (French); the reader's link stays usable when written by hand.
- **Not in this slice** (next): the reading form's own readings at `reading.html`, the dossier, Compare, Changes and the
  provision history keeping their state in the address; the skip link writes `#main` over a kept search when pressed (it
  could move the focus without writing the fragment); a journey of a readable address opened with `?date=` (the canonical and
  focus checks need the query taught to them); `?date=` on a dossier's address.
- **Measured** (no browser was started on this machine): rebased on `v3/integration` at 0b908870 (#988): `npm run build`
  in `web`, then a plain `npm test`: 119 files, 1,292 tests, 1,289 passed, the 3 that start a browser skipped with their
  reason; `dotnet build Lex.V3.slnx -c Release`, 0 warnings, 0 errors; in `Lex.V3.Tests` the classes the slice touches and
  those named for it (`V3WebRootTests`, `LuxembourgCalendarTests`, the Census classes, the handler's), 47 of 47; in
  `Lex.V3.Ingest.Tests` the Census classes, `PublicRequestRecordingTests`, the answer, envelope and refusal-payload samples
  and `V3EuropePermalinkTests`, 60, 57 passed, 3 skipped (each by its variable: a live canary, a named mount); the censuses
  unchanged (no answer or envelope moved); `eng/verify-v3-tree.ps1` passes (1,652 tracked paths). The journeys are CI's.

## The opening kit: law.soufien.lu bound, search engines by deployment, the owner's decisions of 18 October (sole driver, 2026-10-18)

The owner decided on 2026-10-18: the EU authenticity wording for acts published before 1 July 2013 and search-engine
indexing (O27) as recommended ("yes yes go with what you recommend"), and the opening "once we finish v3".
- **The domain.** The registrar named the "-06" Azure name servers, which hold no zone for soufien.lu (V2's old zone),
  so every lookup failed; the zone in rg-lex-v3 is served by "-05". The owner switched the registrar to the "-05"
  servers on 2026-10-18. `deploy/bind-domain.ps1` (new) reads the app's host and verification id, sets or checks the
  `law` CNAME and `asuid.law` TXT in the Azure zone, waits until a public resolver answers both, then adds the hostname
  and binds it with a managed certificate validated by the CNAME, and reads the binding back. A plan run binds nothing.
  It never opens the site: the address rules still admit only the two named addresses. Run on 2026-10-18:
  law.soufien.lu is bound, SniEnabled, with a managed certificate, and answers over HTTPS.
- **Redeployments keep the binding.** The template takes `customDomains`; `deploy.ps1` reads the app's bound domains
  back before every deployment and passes them on (the template would otherwise set none). A plan run says so and asks
  Azure nothing.
- **Search engines by deployment.** The template takes `searchEngines` (`kept-out`, the default, or `indexed`) and sets
  `LEX_V3_SEARCH_ENGINES` for the API; `deploy.ps1 -SearchEngines indexed` sets it. It is set at the opening. Law pages
  still open only for the works the release's indexing record lists (R92), which every release carries since
  `data/names-r92`; a release before it opens only the home and Coverage pages.
- `deploy-kit.test.mjs` pins both: the parameter and its default, the variable the API reads, the domains read back and
  passed on, and the binding's DNS wait, CNAME validation and plan-only default.
- The review of #988 found that one failed read of the app's domains would have passed "none" to the template and
  unbound law.soufien.lu. A failed read now stops a redeployment (only a first deployment, with no live revision, has
  nothing to read), and the domains are read back after the deployment, which fails if one was lost. The switch's value
  is exact (`IgnoreCase = $false`), as the API reads it, so `Indexed` is refused rather than silently kept out. STATUS.md
  records that the domain is now served by the Azure zone, departing from O14, and the owner's decisions of 18 October;
  the owner's brief no longer asks for the registrar's name servers.

## The private copy on the release of 18 October, and a deployment that waits for its candidate (sole driver, 2026-10-18)

The release `v3-20261018T050235Z-f505b58cee2f` (from v3/integration at f505b58c, #984, over derive-c) was built with
`--no-browser`, signed by the release signer (its key piped from Key Vault, never printed), reproduced to the same image
(`sha256:9f8da279…`), and probed in WSL: 18 API journey probes, the served pages, 63 V2 routes absent, the server and
card, and privacy clean (142 requests, nothing written, no cookie, no other origin); 19 page steps were not walked
(no browser on this PC). It is kept at `C:\lex-v3\releases\` with its report.
- `deploy.ps1 -Apply`, run by the driver's identity, copied the image by digest and deployed it as a zero-traffic
  candidate, ingress admitting only the two named addresses. Its probe then held the live revision of 13 October to
  this release and failed, rightly: the candidate label follows the latest *ready* revision, and the new revision was
  still pulling its 2.7 GB image and verifying its indexes (about four minutes). The kit deactivated a healthy
  candidate; no traffic moved.
- The driver reactivated it, waited until it was the latest ready revision, and ran the same probe: it passed. Then,
  as a driver decision (reversible; the private copy stays restricted, so this is not the opening), all of the private
  copy's traffic moved to it and the revision of 13 October was deactivated, keeping the cost at one replica.
- `deploy.ps1` now probes the candidate at the revision's own address (`properties.fqdn`), never through the
  candidate label, once it is provisioned, active, running and healthy, waiting up to 20 minutes. A failed or inactive
  revision ends the wait at once; one that never becomes ready is deactivated, and nothing moves. A plan run says so.
  `deploy-kit.test.mjs` pins it. The review of #985 showed why the label is not enough even after a wait: the release
  card cannot tell two revisions apart in general (the two releases' cards differ only in six case digests, and both
  name the same corpus), so a routing lag could let the live revision answer a later probe; the revision's own address
  routes to the named revision whatever the label does.
- Measured: `deploy-kit.test.mjs` and `no-browser-probes.test.mjs` 23 of 23.

## Search results by act, and a plain no-hit card (sole driver, 2026-10-18)

The plan's items 4.3 ("Results grouped by act: the act's title once, with its passages nested below; a word-for-word
extract, with the matched words marked; Luxembourg dates worded as 'applicable from'; the act whose title matches exactly
on top, or the candidates listed when several match; the population behind the list") and 4.5 ("A plain no-hit card:
which searches ran; that only exact matching ran; the population searched; links to the Legilux and EUR-Lex search pages.
The looser search stays off until its own check passes"), to the specification of record's R59, R21, R11 and R90; and
4.2's next item, the phase's `anchor_not_in_version` sentences said of what this site holds.
- **The answer** (`V3CorpusMount.SearchResults.cs`; additive, the request schema unchanged, so the registry digest did not
  move; REST and MCP answer from the one handler, and on `derive-c` they answered byte for byte alike but the request's own
  fields):
  - each Luxembourg hit carries `extract`: a run of the article's searchable text (the bytes the lane matched, the text
    `evidence_bundle` quotes), word for word, around the first match, at most 80 characters before it and 200 after it, cut
    at whitespace (a word that alone passes a bound is cut at the bound); `parts` in order, a part `matched` when it is a
    whole match (the query as typed for a strict hit; a term, or overlapping terms, for a relaxed one; a match the run cuts
    is not marked); `byte_start` and `byte_end`, the run's UTF-8 offsets in the text whose SHA-256 is `text_sha256` (R59's
    "verbatim byte-range snippets"); `extract_rule` says it. A page's texts are read by article identity
    (`ResolveArticleSearchableTexts`, one primary-key lookup each), never a whole state;
  - `acts`: each act of the page once, in the order its hits first appear: its work key, its dossier, the publisher's title
    the index holds (the language searched first, then French; a title before a short title) or null, the as-published
    label when every state held of it is the act as first published (`HeldOnlyAsPublished`, the rule of #964's
    `as_published`), and its hits over the whole result (`acts_rule`);
  - `searched`: what the lanes matched against, from this index: the states in the language asked whose text the rights
    rule admits (with a date, the one state the date selects for each work; a work with several on it counted apart), their
    works and their articles with searchable text, within the work named when one is; the states the rights rule keeps out;
    and the build time the mounted log records (`index_built_at`). It is read once when the mount opens (`Warm`; one pass,
    `ResolveStateTextSources`, in a pinned order, below), and the rights decision every search applies per state now comes
    from that read (it asked `ResolveStateSources` of each state of the hits on every search);
  - each card of the title ladder names `work_key`, the work the index holds its expressions under, or null when it holds
    none (the title table also titles works with no held state: on `derive-c`, 168 of the 1,502 titles "règlement
    grand-ducal" matches, and "Loi sur le timbre");
  - a version's hits come in the reading's order of the publisher's article ids (#978's comparer, each id read into its
    parts once), so art_2 comes before art_10; the ranking sentence says so, and the cursor is unchanged.
- **The page** (`LiveSearch.jsx`, `live-search.mjs`, `search-answer.mjs`, `styles.css`): every search from the home box,
  the search page and the 404 page asks the strict lane alone (`mode: "strict"`, `SEARCH_MODE`): the relaxed lane is lane 2,
  which R90 keeps off until its gate passes. In this order:
  - the title ladder's answer on top: "The phrase is the title of an act: {title}" (or "begins the title of an act:", or
    "Every word of the phrase is in the title of an act:"), the title linked to the act's dossier, or said "(this site holds
    no version of it)"; several titles: "The phrase matches the titles of {count} acts:", the first ten shown and the rest
    one step away (`<details>`, "{count} more acts");
  - what was found: "“{query}” in {language}: {passages} with the exact phrase, in {acts}."; an answer in both lanes, as an
    API caller may ask it, still says each lane's count;
  - what was searched (R11, every count the answer's): "Searched: {acts} of Luxembourg law in {language}, in {versions}
    with {articles} of searchable text, as of the index built on {date}.", the build's date in Luxembourg, as the home
    page's coverage line dates its index; the versions the rights rule kept out and the acts with several versions on the
    date, when there are any; then the dated or undated sentence, as before;
  - the acts, each once: its title linked to its dossier (or its work key, "this index holds no title for this act"), the
    as-published label ("As published; may have been amended since; no consolidated text."), "{count} passages in this
    act." (over the whole result, so an act a page cuts says so), then each version's passages under "The act as published,
    applicable from {date} (publisher)" (or "Version applicable from {date} (publisher)" for a consolidated state, none held
    today): R21's words, the state's date being the publisher's `dateApplicability`; never "version of {date}";
  - each passage: its article id, linked to the reading at that article and version (the permalink that pins it), and the
    extract quoted (`<q>` in the text's language), each whole match in `<mark>` (bold as well as highlighted, so the mark
    does not rest on colour), an elision where the text goes on; a relaxed passage says its lane. No digest in a row.
- **The no-hit card** (a search with no passage): the no-hit sentence and the matching rule as before, then "What was
  searched": the exact-reference phase ("the phrase is no reference this site reads (an ELI, a CELEX number, a Mémorial
  reference, a permalink or a citation)"; not read when a work was named; or the cited title not held), the titles (none
  matches; the work resolution's own sentence when no title is held; the title or titles shown above; not searched when a
  work was named; and "Titles are matched with capitals and accents ignored." where the ladder ran), the text ("no article
  searched contains the phrase exactly as typed"; every word in any order when that lane was counted); "Only exact
  matching ran on the text: capitals, accents and every other character of the phrase count, and nothing is folded,
  stemmed or expanded. A looser search of the text stays off until its own check has passed." when no relaxed lane was
  counted; the population searched (said once, in the card); "The publishers' own search pages:" with Legilux's search
  (`https://legilux.public.lu/search`) and EUR-Lex's advanced search form
  (`https://eur-lex.europa.eu/advanced-search-form.html`), the platform's contract addresses (`PreviewRefusals.LuSearch`,
  `EuSearch`), held to the publisher-link policy, links only (nothing requests either, nothing goes to eur-lex.europa.eu);
  and the absence note. An EU work's card says the phase and the titles were not run, and counts no Luxembourg population.
- **4.2's next item, done**: the phase's `anchor_not_in_version` sentences said the version (or wording) holds no such
  article, while the index may lack an article the version has (the French review of #978, its point 3). They are now "This
  site holds no article with that number in the version the date selects, and no other article is opened in its place."
  and "… in the wording the date selects, …", with the French that review gave for this English.
- **French** (Decision 41): 36 forms (34 interface entries and the 2 phase sentences) were reviewed by an independent
  reviewer (Claude, AI reviewer, not the author) in the addendum of 2026-10-18 "search results by act and the plain no-hit
  card" of `C:\lex-v3\lanes\fr-review\review.md`; 10 changed in review, the English with them, all applied: the title
  stands last after "the title of an act:" (French puts no bare title after "de", and 1,597 of the 1,730 held titles end
  with their own full stop, which printed ".."); one title is said in the singular; the text alone is said to be matched
  exactly, since the titles are matched with case and accents folded ("reglement grand-ducal" matches 1,502 titles and no
  text), and the French no longer says "appliquée", a word R21 refuses; "no article searched"; a date's acts named with the
  date; the introduction's scope set inside its second member. Its optional points 4 to 7 were taken (how titles are
  matched, a full stop after an act's passages, the EUR-Lex link named as its advanced search, the no-titles sentence said
  once); 8 and 9 (digit grouping, a set of the shared entries held to neither publisher's date words) are next. 3,675
  checks by script; the forms are named in the header comment of `live-chrome-fr.mjs`. `namesWork`, `severalWorks`,
  `candidate` and `hit` were removed.
- **Samples**: the answer census samples the phrase in the strict lane alone and a word no article carries in it; the
  envelope census holds both, and the EU work in the strict lane with and without a hit; every Luxembourg search sample
  carries the new members (rendered from the handler with `V3_RENDER_ANSWER_SAMPLES` and `V3_RENDER_ENVELOPE_SAMPLES`,
  verified without them, with the refusal payload census, 7 of 7).
- **Measured on the population** (`derive-c` opened read-only in place through a junction; the API published from this
  branch and, for the comparison, from its merge base 5a659831, without the synthetic preview, its index copies in a
  private temporary folder removed after each stop; no browser; medians of 7 runs, the machine shared):
  - "congé": 138 passages in 71 acts, all on one page, 252 matches marked (an extract's median 274 characters), 12 titles
    on top (11 held), 224 KB; 105 ms (merge base: 96 ms, 92 KB); 15 runs: min 76, median 104, p90 186 ms. Dated today: the
    same, 115 ms.
  - "congé parental": 15 passages in 12 acts, 5 titles, 86 ms (base 71). "bail commercial": 2 passages in 1 act and the
    ladder's one act, 82 ms (base 77).
  - an act's title: "Loi sur le timbre" is the ladder's exact tier, one act this site holds no version of, no passage, 79 ms
    (base 63); "Loi du 12 novembre 2011 modifiant la loi du 9 mai 2008 relative aux chiens." is read by the phase as a
    type and date and opens the act, 17 ms (base 10).
  - "règlement grand-ducal": 932 passages in 470 acts, the first 200 in 51 acts on the page, 1,502 titles listed (1,334
    held), 1.1 MB (base 0.95 MB), 145 ms (base 203); "article": 2,977 passages in 1,048 acts, 200 on the page in 37 acts,
    103 ms (base 219): the rights decision now read once at open instead of per state on each search.
  - no hit: "combien de jours de congé j'ai le droit quand mon père est décédé" 89 ms (base 80), "zéphyrine" 78 ms (base
    64), each with the no-hit card's population: 1,730 acts in 1,730 versions with 7,877 articles of searchable text, as of
    the index built on 2026-10-12. Dated 2000-01-01: no act searched.
  - an EU act named ("personal data" in 32016R0679): 60 hits, 32 ms (base 32), unchanged.
  - every extract of "congé" (138) is the bytes `evidence_bundle` quotes at its article: the same text digest, and the byte
    range decoded to the parts joined; REST and MCP identical for "congé", "zéphyrine" and "bail commercial".
  - **A defect the measure found**: the population read, a plain join, took about 55 s at mount open on the engine the
    server ships (it read the articles by language for each state): the start took 86 s against the merge base's 34. Written
    with `CROSS JOIN` (`LuxembourgIndexQueries.StateTextSources`), the start took 29 s; `LuxembourgIndexQueryPlanTests` now
    holds its plan, and fails the plain join.
- **Driver decisions** (reversible):
  - The site asks the strict lane alone (`SEARCH_MODE`); the platform still answers both lanes to a caller who names no
    mode, as every answer says (`lanes`, and a lane's count null when not scanned). R90 keeps lane 2 off at launch unless its
    gate passes; making strict the API's default is one line and moves the samples (next, with the gate).
  - The extract is cut by the platform, not the page, so REST, MCP and the page show the same bytes, each verifiable against
    `evidence_bundle`'s text by digest and byte range. Its bounds (80 and 200 characters, whole words) are constants; only
    the first match's neighbourhood is shown, later matches in it marked, the others in the reading.
  - An extract is a pointer, marked as cut where the text goes on; the whole article, bracket tables and condition lists
    whole (product spec rule 11), is one link away, on Reading. The accessibility test's date rule reads an extract as the
    law's text, as it reads a quoted article.
  - A version's hits in the reading's order of article ids (#978's comparer); across acts the order stays the work key's,
    which is no rank (the answer says there is none).
  - The act of a title is on top as a link, not moved among the passages, which stay in the answer's order.
  - Several titles: the first ten shown, the rest one step away, all from the answer, which still lists every candidate the
    ladder finds; bounding that list in the API, with its count, is next.
  - The population counts what was matched against (on `derive-c`, every one of the 1,730 states), never what the
    publisher holds; it is dated by the mounted log's build, in Luxembourg.
  - The phase's `anchor_not_in_version` sentences take the French review of #978's renderings (subject "This site"), not
    the reading's ("This index holds … in this version"): the phase's other sentences say "This site holds", and that French
    was reviewed for this English.
  - An EU search keeps its one-act layout (the one wording pinned above its hits); its hits carry no extract yet.
- **Not in this slice** (next): an EU hit's extract (the EU answer says `text_served: false` and lists `article_text` as not
  held; served EU text carries its acknowledgement and authenticity statement, which the search page would then show);
  bounding the title ladder's candidates in the API; strict as the API's default (with R90's gate); digit grouping in both
  languages; the 4.4 items (capitals, accents and quotation marks in the text lanes, the byte budget, the 250 ms target,
  which every search measured here meets); the journeys (CI's lane: the home, search, 404 and J2 steps' bodies now name the
  strict lane, and their texts the new lines). The data lane's content /3 (S3b) changes what the searchable text holds
  (deleted wording never matched); an extract is cut from that text, so it follows. Index /9 (#980), before it is served
  (S7a): a hit carries only `applicability_date`, and the version line says "applicable from"; the line must branch on the
  state's `date_role` (an act dated by its entry into force is not "applicable from" it, R21), and the hit carry the role.
  On /9 the population counts held rows only (`SearchedRuleNine` says so): a version recorded without text (withheld by its
  rights, not available, not acquired) is in no count, `states_withheld_by_rights` included; counting /9's
  `withheld_rights` rows is S7a's.
- **The review of #982** (an independent reviewer; R21, R11, the extracts (138 + 200 + 5 checked byte for byte), escaping,
  the no-hit card, REST against MCP, the start (30.5 s) and speed found sound) and its repair, in one round:
  - F1, medium (latent): two versions of one act on one date had their passages interleaved: the order was work, date,
    article id, identity, state, and the page cut a new version section at each change of state, so one "applicable from"
    heading printed again and again and passages sat under the wrong version. Reproduced with two states added on one date.
    Repair: the order is work, date, state digest, article id, identity (the ranking sentence says so), and the page groups
    a version's passages by its state. Tests: `TwoVersionsOnOneDateKeepTheirHitsApartAndEachActAndCardNamesItsOwnWork` (two
    added states on one date: each state's hits one run) and the web test of one act in two versions, on one date and on
    two, with the hits alternating (one section per version, each passage under its own).
  - F2, medium (tests): two mutations passed every test: every act given the first act's title, every card linked to one
    held work (M3), and the page's cut by version dropped (W4, an R21 error). Tests: the mount test above holds two titled
    acts on one page and a ladder answer with two held cards and one not held (each act's title, each card's `work_key`);
    the web test fails W4.
  - F3, low: an extract was cut at a no-break space (`char.IsWhiteSpace` counts U+00A0), so "6 septembre 2021" could lose
    its day. Repair: U+00A0, U+202F and U+2007 are no break (`IsExtractBreak`), and the rule says so. Test:
    `AnExtractIsNeverCutAtANoBreakSpace`, a bound on the no-break space before and after the match.
  - CI on c789ad9b (image-conditions): `V3CorpusLegacySchema8MountTests` replays #980's recorded answers over the committed
    schema-8 and schema-6 mounts byte for byte. Of its 38 rows, 2 differ, both schema-8 searches (`schema8-13`, `schema8-14`),
    and only by this slice's additive members (`acts`, `acts_rule`, `extract_rule`, `searched`, each hit's `extract`) and the
    ranking sentence, which states the new order; no field was removed and no hit moved; the schema-6 search (a reference)
    is unchanged. Recorded again where production runs, with the new gated recorder
    (`TheAnswersAreRecordedWhereTheRecordingAsks`, `V3_RECORD_LEGACY_ANSWERS`): Linux in WSL (Ubuntu 24.04, .NET 10.0.11,
    `DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=1`, `TZ=UTC`), identical with ICU and on Windows; the two rows spliced in and
    `recorded_at_commit` b1b41312, the test pinning it.
  - Content /3 (#981): search matches and extracts the searchable text alone, which holds no `deleted_text` token; a test
    holds it (`DeletedWordingIsNeitherMatchedNorExtracted`: deleted wording in an article's tokens is neither matched nor
    in its extract).
  - The review's notes on #980: rebased on 8e6d3d27 (#980's index /9, #983 and #981's content /3); the samples re-rendered
    from the handler (identical once rebased, then verified without the switch);
    `StateTextSources` beside #980's queries in `LuxembourgIndexQueries` and the census of their names; the /9 population
    said in `SearchedRuleNine` (a /8 answer's rule unchanged); the version line's /9 dates recorded as S7a's
    (above); item 6 of the citation generator's next steps reconciled with the data lane's finding (below).
- **Measured** (no browser was started on this machine; this branch, from `v3/integration` at #978's merge, 5a659831):
  `npm run build` in `web`, then a plain `npm test`: 115 files, 1,262 tests, 1,259 passed, the 3 that start a browser skipped
  with their reason; `dotnet build Lex.V3.slnx -c Release`, 0 warnings, 0 errors; `Lex.V3.Tests` 3,321, 3,315 passed, 6
  skipped; in `Lex.V3.Ingest.Tests` the classes the slice touches and those named for it (the Census classes,
  `PublicRequestRecordingTests`, the answer, envelope and refusal-payload samples, the Search, EU Search, Results, Reference
  and EU Reference mount classes, the MCP classes, `LuxembourgIndexBuilderTests`, `LuxembourgIndexQueryPlanTests`, the
  machine and mounted gates, citation verification, ask, coverage, resolve and journey mounts) 312, 305 passed, 7 skipped
  (each by its variable: a live canary, a journey mount to write, a mount to evaluate); the censuses rendered from the
  handler and verified without the switch; `eng/verify-v3-tree.ps1` passes; the French review's 3,675 checks pass.
  After the review's repair, rebased on 8e6d3d27: `npm run build` and a plain `npm test`, 116 files, 1,270 tests, 1,267
  passed, the 3 that start a browser skipped; `dotnet build Lex.V3.slnx -c Release`, 0 warnings, 0 errors; `Lex.V3.Tests`
  3,321, 3,315 passed, 6 skipped; in `Lex.V3.Ingest.Tests`, filtered while a release ran (the search, results, EU search,
  reference, EU reference and resolve mount classes, the Census classes, the three sample censuses, the MCP classes,
  `LuxembourgIndexQueryPlanTests` and `PublicRequestRecordingTests`), 188, 186 passed, 2 skipped (live canaries); the
  censuses rendered and verified, 7 of 7; three mutations each fail the new mount test (the order before the repair, one
  title for every act, one work key for every card), and the page's cut by version dropped fails the new web test;
  `eng/verify-v3-tree.ps1` passes.

## The release probe's accented title, read by the exact-reference phase (sole driver, 2026-10-18)

The first release without a browser (from v3/integration at 4e37df90, over derive-c, signed by the release signer) built,
signed and reproduced its image, then failed one probe: the accented-title check searched "Loi du 3 décembre 2015
relative à certaines utilisations autorisées des oeuvres orphelines." and found no `work_resolution`. The search was
right: since 4.2 (#974) the exact-reference phase reads a title that begins with its act's type and date as a reference
to that act, opens it and searches no word. The probe was written before the phase, and CI never runs this check against
a real API (the fixture index holds no titles, so the check says "not checked" there), so only the population showed it.
- `accentedTitleFailures` (`deploy-probe.mjs`) now accepts the phase opening the very act whose title was asked
  (`reference.outcome` "opens", `recognised.act.work` the work's key), as well as the title ladder naming it alone. The
  title's own fold is still held by the three resolves before it (as published, in capitals, decomposed).
- A test holds the three cases: the phase opens the act (passes), opens another act or nothing (each fails, saying so).
- The review of #984 found the failure's wording blamed the phase when the ladder failed: every answer carries the
  phase's member, with outcome "no_reference" on the ladder's answers, and the stand-in left it out. The phase is now
  named only when it answered ("opens" or "not_opened"), and the stand-in answers the ladder's searches as the API does.
  It also confirmed that resolve never goes through the phase, so the three resolves still hold plan 0.8's fold.
- Measured: `deploy-probe.test.mjs` 12 of 12; `npm run build && npm test` 1,252 passed, 3 skipped (the browser tests).

## Content /3's wording on the reading and in the exports (sole driver, 2026-10-18)

Slice S3b of the Luxembourg data design (STATUS-DATA.md, the section of the same day): content /3's quoted wording,
deleted wording and unmarked notes, from the evidence bundle to the screens and the exports. Release 1's rule set runs
content /3, so these are on the pages once a re-derived mount is served.
- **The bundle.** A row whose wording holds a quotation or deleted wording carries `segments`: text segments (their
  texts concatenate to exactly `text`), `quotation_start` and `quotation_end` (the publisher's element, `marks_in_wording`,
  its own mark when it gives one) and `deleted_text`; the answer then carries `segments_rule`. An unmarked note is
  `{marker: null, text, unmarked: true}`. Every other row and answer keeps its bytes, so no census moved.
- **The reader** (`reading-answer.mjs`) reads the segments and refuses those that break the platform's rules (text
  segments other than the text, a quotation left open or closed by another, an unknown element or kind, empty deleted
  wording), and reads an unmarked note, never a note with no marker that does not say so.
- **The screens.** `ArticleWording` (`LiveReading.jsx`), used by the reading, the cited article and the export composer's
  pins:
  - quoted wording between « and » (U+00AB U+00A0 … U+00A0 U+00BB), or the publisher's own mark, unless the publisher
    writes the marks in its wording (`scl:hide-quote="true"`), or the wording already holds them: no opening mark when
    the nearest character of wording before it is already an opening mark, or when the quoted wording opens and closes
    with its own marks, and the closing mark mirrors it (the review of #981, F1); a mark that is added stands apart from
    the word before or after it with a space where the wording has none (`addedMark`, `web/scripts/quotation-marks.mjs`,
    shared with the PDF);
  - deleted wording struck through in a `del`, with `reading.deletedWordingStart` before it and
    `reading.deletedWordingEnd` after it, visually hidden and spaced apart from the words around them, so a screen reader
    says where it starts and ends;
  - `reading.deletedWordingNote` under an article that holds deleted wording, on the reading, the cited article and the
    export composer (`DeletedWordingNote`);
  - an unmarked note as `[reading.unmarkedNote] text`.
  The text quoted, cited, digested and searched is never changed: with the marks and the deleted wording taken out, the
  blockquote is the text, and its digest the one served (tested).
- **The exports** (`export-build.mjs`, `export-pdf.mjs`). JSON: `segments` on an item that has them, each quotation
  boundary with `marks_in_text` (the export never borrows the EU's "wording", which its test holds), and notes with
  `unmarked`. CSV: a last column, `segments`, the JSON of the segments or empty, so every column of `lex-v3-export/1`
  keeps its place (the review of #981, F4). PDF: the text with its quotation
  marks and a line that the text digest is of the text without them; the deleted wording listed after the text under
  "Wording the publisher marks deleted, in order; none of it is part of the text above:", never inside it; an unmarked
  note as `[no marker]`.
- **The French** of the four entries was reviewed by an independent reviewer (Claude, AI reviewer, not the author)
  under the owner's delegation, in the record's addendum of 2026-10-18 on deleted wording and unmarked notes: 4 forms, 1
  changed (`reading.deletedWordingNote`: the struck text is "celui que l’éditeur marque comme supprimé", never "un texte",
  an instrument in legal French; the text a search covers is "interrogé"), 3 approved, no English changed. Its points
  1 to 4 and 6 are applied (the labels spaced apart, the sentence in the composer too, no "in force" in the reader's
  refusal or the API's `segments_rule`, a space outside the added marks, the header comment); point 5 (copying a
  quotation picks up the hidden labels?) is unchecked without a browser and left for the accessibility audit.
- **Tests.** `web/test/content3-wording.test.mjs` (7): the reader and its refusals, the reading and the export composer in
  English and in French, the JSON, CSV and PDF exports (the PDF's text line exactly: the review of #981, F5), segments
  through pages put together, the marks' spacing, and no mark doubling the publisher's own (rgd/2020/09/02/a743/jo's and
  rgd/2017/08/21/a803/jo's shapes). `npm test`: 1,261 tests, 1,258 passed, 0 failed, 3 skipped (the
  browser tests, which run only with `LEX_BROWSER_TESTS=1`).
- **Driver decisions (reversible).** The deleted wording is shown in place, struck through and said, rather than left
  out: the publisher shows it, and a reader comparing with the official text needs it; it is never in what a quote,
  citation or digest takes. The PDF, whose fonts cannot strike through, lists it apart under its label. The CSV's new
  column is the last, so no column of `lex-v3-export/1` moves.
- **Review and repair (#981).** Doubled marks, measured over the 13,161 quotations of the 4,016 admitted articles holding
  one: an added mark beside a mark of the wording, or around wording that holds its own, 98 marks in 28 articles before the
  fix; after it none, the three left being an inner quotation's mark beside its outer quotation's (nesting, not a
  doubling).
- **Waits.** These screens first show content /3 once a re-derived mount is served (the dry run, D1); the handler-level
  test over a re-derived synthetic population is next (STATUS-DATA.md).

## A rooted path is an identifier on every system, as production reads it (sole driver, 2026-10-18)

CI's image lane on #980 found that `resolve` of a rooted path (`/lu-legilux/loi-2026-01-01-a1`, which no record answers)
was answered differently by the same code on Linux and on Windows. `LooksLikeIdentifier` (`V3CorpusMount.cs`) took any
value `Uri.TryCreate` reads as absolute: on Linux a path starting with `/` is an implicit file URI, so it was an
identifier (`identifier_unknown`, the answer production sends); on Windows it was words for the title ladder
(`retrieval_mode_unavailable`).
- A value is now an identifier when it is a rooted path, or an absolute URI written with its scheme. A drive path
  (`C:\...`) or a UNC path starting with a backslash is words on every system; Uri had read both as file URIs, on Linux
  too. The value is read after the whitespace Uri skips, so a path pasted with a leading space reads as the path does.
  The Linux recording of #980 (`base-answers.json`) is the answer on every system, and its Windows exception
  (`windows_answers`) is gone; the test refuses one coming back.
- What changes on Linux, as the review of #983 listed it: drive paths and backslash UNC paths, which no Lex identifier
  takes, now go to the title ladder instead of `identifier_unknown`. Every Lex identifier and address answers as before,
  with or without leading whitespace.
- Measured on Windows: the Ingest legacy schema 6 and 8, resolve, samples, census, reference and privacy classes, 244
  passed and 3 skipped; `dotnet build Lex.V3.slnx -c Release` 0 warnings. The review ran the legacy schema 8 and resolve
  classes on Linux too (WSL, invariant, UTC): 124 of 124. CI's Linux lanes run the rest.

## Reading by article: the cited article's page first, the contents, and the next articles (sole driver, 2026-10-17)

The plan's item 3.7 (`C:\lex-v3\PLAN-2026-10-14.md`), to the specification of record's R73 ("Reading renders the requested
article first (R64) and streams the rest") and R107 (R73's page budget, latency measured on the largest works), with R64
and R67. Release 1 brings the codes, which the population mount does not hold, so its largest held versions are the
proxies and a synthetic act of 3,000 articles is the stand-in.
- **What there was** (read from the code, measured on `v3/integration` at 32a9522e, built in a scratch copy):
  `evidence_bundle` answered every article of every served version in one answer, and the reading page rendered it whole
  and carried that answer beside it for hydration (about half the page). A reading of 3,000 articles at the census
  fixture's article size weighed 13.4 MB (its answer 6.5 MB) and rendered in 168 ms; the largest held version, CRR
  (32013R0575, its 2027-01-01 French wording, 804 provisions), a 6.2 MB page from a 3.6 MB answer, served in 250 to 290
  ms. And a Luxembourg state's articles were served in the order of their identity digests (every state lists them sorted
  by digest: 1,730 of 1,730 in `derive-c`, read-only), so a code would have been read in no order a reader knows.
- **The API reads by page and by article** (`V3CorpusMount.Reading.cs`; additive parameters on `evidence_bundle`, so the
  registry digest moved and every census pinned to it was rendered again from the handler with the `V3_RENDER_*`
  variables, then verified without them):
  - a version (a Luxembourg state or an EU wording) is cut into pages: at most 50 articles and at most 98,304 bytes of
    quoted text (UTF-8), an article whose text alone passes that bound a page of its own, in the version's order, so a
    page's number names the same articles on every asking (`page_rule` says it in the answer);
  - `page` (1 to 100,000) serves that page, `article` (a publisher id) the page that holds it, neither the first page,
    so a version that fits one page is answered whole as before; `contents: true` adds the table of contents: every
    article's position, publisher id, identity, EU heading and page, no text;
  - every version carries `page` (its number, the version's pages, its first and last positions, and the version's
    counts: articles, quoted, held without text, and a Luxembourg state's articles whose date differs from its own) and
    `pages` (each page's first and last article, EU headings, and count); every article and article held without text
    its `position`; `articles`, `articles_without_text` and `validity_conflict_count` are the page's;
  - on both publishers, at a permalink and by the work and a date (Luxembourg; the EU time view and the original-wording
    path); rights are enforced over the whole version before any page is cut, and a version with no text at all is still
    `text_not_available`;
  - refusals stay typed: an article a served version does not hold is `anchor_not_in_version`, with the ids nearest it and
    the payload every other anchor refusal carries (`article_history`, `citation`, `verify`); so is a page past a served
    version's last, each version cut on its own (`page=N`, the first and last pages as the nearest anchors, and
    `requested_page`, `pages` and the version's language; the review of #978, F2); a page and an article together, or a
    page or contents of the wrong type, is `request_schema_invalid`, as a cursor that names nothing is;
  - a Luxembourg state is read in the order of its publisher ids (`CompareLuxembourgArticleIds`: the numbers in an id as
    numbers of any length, `er` then bis, ter, quater and the Latin adverbs in their order, a roman numeral after `art_` or
    `article_`, alone or with `er` or an adverb (`art_ier`, "Ier", `art_iibis`), by its value, `premier` as 1, `art` written
    right before its number (`art1er`, `art12`) as `art_1er` and `art_12`, then the identity), since the index holds no
    document order; the EU order is the publisher's provision ids', as the
    wording digest reads them (zero-padded: `001`, `005A`, `325AO`);
  - MCP's `evidence_bundle` tool says it pages.
- **Reading renders the requested article first, with the contents, and loads the rest on demand** (`LiveReading.jsx`,
  `PinnedReading.jsx`, `reading-pages.mjs`):
  - an article's address (a permalink or a readable address) asks the page that holds its article: the cited article
    first, as before, then its page; a version's address asks its first page, and `?page=N` on it that page (both servers
    read the query, `V3WebRoot.SitePageFor` and `sitePageFor` held to one table, now with the queries' rows);
  - a version of more than one page says which pages it shows ("Page 2 of 26: … to …"), lists its pages under a collapsed
    "Contents" (each by its first and last article, a link to `?page=N` on the version's permalink, or on the act's readable
    address when the page stands at one, the shown ones marked
    `aria-current`), numbers each article by its place in the version, and ends with "Previous articles" and "Next
    articles", plain links to those pages' addresses, which the server renders with no script run;
  - with scripts, "Next articles" asks the next page in place (the same request at that page, `readNextPage`), adds its
    articles below the shown ones and gives the focus to the first of them; the link stays a link, opened apart or without
    scripts. The articles' list is `aria-live="off"` on a paged version, so a page added is not read out whole;
  - a page after the first names itself canonical with its page (`?page=N`), in the page's own language, and a page of the
    version its act's readable address shows today names that readable address with its page (both rules #976's, the plan's
    item 3.6); a page of today's version is kept out until the switch opens it, as its first page is, any other page is never
    opened, and the sitemap lists act-level addresses only; the cited article's address names the article;
  - an article the version does not hold: the refusal card ("This index holds no article with that id in this version",
    with the ids nearest it), the focus on it, and after it "The whole act at this version" (or the act today, at a readable
    address), where before the whole act stood under the article's address;
  - a page past the version's last: "This version is read here on 2 pages, so there is no page 3." (an EU wording's own
    sentence), with no card, whose note is about provisions, then links to its first page and its last, the focus on the
    refusal; never indexed (the review of #978, F2);
  - a version that fits one page is laid out as before: no contents, no pages line, no links, no numbers of its own.
- **The dossier and the exports ask by page.** The dossier asks no text (titles, states and their counts), and each version
  links to its reading's first page, so it is unchanged. The export composer pins from the page shown, and "Pin every
  article" reads the version's other pages first, one request each, and adds them to the reading: an export of a whole
  long act is put together from its pages and keeps every article and its evidence. The tests hold its JSON, CSV and PDF
  byte for byte against the same articles read as one page, for the census's long act (60 articles, two pages) and for
  the census's one-page act cut into three and put together again.
- **Speed checks** (`web/scripts/reading-weight.mjs`, no browser: the API's answer to the request the page sends, the page
  the API serves, and the server's render time, the median of five; the base build against this branch, `derive-c` mounted
  read-only through a junction, its listing unchanged before and after, the API's verified index copies in a scratch
  folder removed after each run):

  | Version | Before: page, answer, served in | After, its first page: page, answer, served in |
  |---|---|---|
  | CRR 32013R0575, 2027-01-01, fra, 804 provisions (26 pages) | 6,247,500 B, 3,598,388 B, 249 to 288 ms | 247,619 B, 141,266 B, 16 to 26 ms |
  | CRD 32013L0036, 2026-07-11, fra, 197 (7 pages) | 1,583,229 B, 914,450 B, 72 to 87 ms | 271,300 B, 156,723 B, 12 to 29 ms |
  | MiCA 32023R1114, 2024-01-09, fra, 150 (6 pages) | not readable (below), 772,667 B answer | 251,306 B, 144,929 B, 11 ms |
  | VAT 32006L0112, 2025-04-14, fra, 496 (10 pages) | 1,652,273 B, 943,421 B, 71 to 82 ms | 212,385 B, 121,509 B, 9 to 23 ms |
  | Luxembourg's largest, loi-2018-03-27-a221, 120 (3 pages) | 400,016 B, 220,244 B, 26 ms | 202,309 B, 113,273 B, 17 ms |
  | The stand-in, 3,000 synthetic articles through the real derivation (60 pages) | 6,394,661 B, 3,321,730 B, 263 to 300 ms | 143,208 B, 72,745 B, 76 to 118 ms |
  | 3,000 articles at the census's article size, the renderer alone | 13,385,667 B, 168 ms to render | 260,126 B, 5 ms |

  CRR's row is its 2027-01-01 French wording, the largest held; the wording that answers today (2026-06-26) holds 790
  provisions, read on 25 pages in French and 22 in English (the review of #978). CRR's other pages: page 2, 283,880 B; its Article 4 (66 KB of definitions) cited first, 323,402 B; Article 429's page
  (21 of 26), 300,042 B. The three largest held versions by bytes are CRR, CRD and MiCA (`derive-c`'s EU index, counted
  read-only); VAT is the next by provisions. CRD and MiCA answer their dates `ambiguous_version` (two held texts), so each
  was read at the permalink of its longer one, as the refusal names them.
- **Driver decisions** (reversible):
  - The page budget: a reading page weighs at most 400,000 bytes (about 3 per cent of 12.9 MB) unless one article alone
    passes 96 KiB, which is then a page of its own; measured at 143 to 323 KB on every version above. `reading-pages.test.mjs`
    holds the stand-in's first page to it (260,126 B).
  - The page bound is 50 articles and 98,304 bytes of text: the census's fixture act (49 articles, 63,575 B) fits one
    page, so an act that fits one page is answered, laid out and exported as before; CRR's first page holds 12 provisions
    (its Article 4 is 66 KB). A page is weighed by its articles' text alone: weighing the notes too meant parsing every
    article's tokens to cut one page (on the stand-in, a page took 110 to 162 ms before that change and 86 to 116 ms after).
  - With neither `page` nor `article` the API serves the first page, never the whole of a long version: every answer is
    bounded, and an old client asking a short act gets what it got.
  - A page's address is `?page=N` on the version's address, page 1 the address with no page: R64's last segment is the
    article, and a page is not a citation. A page links its pages by its version's permalink, and at an act's readable
    address by that address (`?page=N` there; changed in the review of #978, which found the links left the readable
    address for the permalink): the readable address's pages are of today's version, which is the one it shows.
  - A page past the last is refused as an anchor the version does not hold (`anchor_not_in_version`), not as a malformed
    request: the request is well formed and only the version decides (the review of #978, F2). No new refusal code: the
    registry's twenty stand, and the payload's three members are optional on the card's contract.
  - The page lists its pages, not every article: links to 3,000 articles would weigh about 400 KB; the API's `contents`
    lists every article for a client that asks, with identities, so one answer with its contents still lets a reader
    recompute the state's article identities digest (the replay guarantee).
  - The next articles are loaded by a control, not by scrolling: a link a keyboard and a screen reader follow, and the
    address a reader without scripts opens. Previous articles is a link only.
  - A Luxembourg state's order is its ids' (above): the export's items follow the reading's order, as before, so an
    export of several articles of one act lists them in that order, not the digests'.
  - An article address whose article the version does not hold is refused, not answered with the version (typed, R64).
    Its page is a 200, as every refusal but `identifier_unknown` is.
  - `as_of` is unchanged: it carries no text.
  - Found by the speed checks and repaired here: the EU reader refused a consolidated wording whose work names no CELEX of
    its own (`celex: null`; one of the two works the publisher holds of many a consolidation, MiCA's above), so that
    reading showed "the answer could not be read", before this slice too. It now reads, its CELEX not held; the evidence
    form then names the act's CELEX, as `citation.mjs` already fell back.
- **Journeys** (expectations in code; the browser runs are CI's): a fifth journey mount, the fixture act with 130 synthetic
  articles in three pages (`V3_WRITE_LONG_ACT_JOURNEY_MOUNT`), and three journeys on it, by pointer and by keyboard on
  the pages the API serves (a step of their own in the `journeys` lane): an article on the second page, cited first, with
  its page, the contents and the links before and after; the second page at the address the next-articles link names,
  shown with no script run; and the next articles asked in place, one request at page 2 and no navigation. The fixture's
  permalink and readable-address steps ask the page that holds their article, and the canonical check reads a page's query.
- **French**: the 13 new forms (the reading's pages, the export's pin-every-article, and the two refusal sentences for
  `anchor_not_in_version`) were reviewed by an independent reviewer in the addendum of 2026-10-18 on reading by page of
  `C:\lex-v3\lanes\fr-review\review.md`: 2 changed, 11 approved, and named in `live-chrome-fr.mjs`'s header.
  - The two refusal sentences say what the index holds, in both languages: "Cet index ne contient aucun article portant cet
    identifiant dans cette version" (and "dans ce libellé"; the English "This index holds no article with that id in this
    version"). The draft, "Cette version ne contient aucun article …", claimed of the version what only the index can say:
    in `derive-c`, 1,004 of the 1,730 held Luxembourg states hold fewer articles than the publisher's document (4,143 not
    admitted), and the review reproduced one (the loi du 16 mars 2020, its Art. 2 in the document and not in the index).
  - Taken from its points: the whole-act link after the card, which takes the focus, so "this version" follows the sentence
    that names it; a page of one article named once (`reading.contentsPageOne`, `reading.pageShownOne`, forms it gave); the
    export's progress "Reading every page: {done} of {pages} read." (`Lecture de toutes les pages ; pages lues : …`); the
    thirteen shared entries held to neither publisher's date words (`SHARED_LEAD_ENTRIES`).
  - "Sommaire" for the contents (not "Table des matières": the act's own table of its divisions), "{first} à {last}" for a
    run of articles, "Page {n} sur {N}", "Articles précédents" and "Articles suivants" were approved as drafted.
- **Review and repair (#978)**, one round, by the other model family: three findings and four notes, all taken.
  - F1 (medium): the reading order put `art_ier`, `art1er` and `art12` out of place (the roman numeral with `er`, and `art`
    written right before its number, were read as letters). Fixed in `ArticleIdParts`, with numbers of any length compared
    by their digits (the comment on ids of more than 18 digits was wrong). Tests: the order's cases, and an opt-in sweep
    over every state of a named index (`V3_READING_ORDER_INDEX`): over `derive-c`, read in place, 1,730 states, 986 with a
    first article, every one first, and 6,147 pairs of plain ids, none out of order; its listing unchanged.
  - F2 (low): a page past the last was a bare `request_schema_invalid`, judged across every version served, and the site
    showed the malformed request's panel. Now the typed refusal above, each version on its own, and on the site the sentence
    and the links to the first and last page, never indexed. Tests: the API's at a permalink, by the work with a language
    asked and none, the long act's fourth page of three, and the EU wording at its permalink and by its CELEX; the cut's
    unit test; the census pin of `RefusePageNotInVersion`; the refusal and envelope censuses; the page's sentence, links,
    pin and noindex render in `reading-pages.test.mjs`; the French census test, over a Luxembourg state's and an EU
    wording's page past the last. Its six French forms were reviewed independently in the addendum of 2026-10-18 on a page
    past a version's last (4 changed, 2 approved): the sentence says "so there is no page 3" ("il n’y a donc pas de page
    3"), the site's page, never that the version has none, since the publisher's text has printed pages of its own.
  - F3 (low): two web mutants survived. Tests: a superseded version's `?page=2` (a Luxembourg state and an EU wording)
    renders the plain noindex at its permalink with its page; a contents row on another page than the index names is
    refused, both ways.
  - Notes: a page asked in place, or an export's reading of every page, stopped by one asked for another version stayed
    loading and its control dead; it now goes back to its link (`withoutStopped`, tested). The links at a readable address
    (above). `PinnedLead`'s comment back above it. CRR's figures (above).
- **Not in this part** (next): the search phase's own `anchor_not_in_version` sentences (4.2's, approved there) make the claim
  the review changed here ("La version que la date retient ne comporte aucun article de ce numéro …"); the review gave
  renderings in the phase's own subject ("Ce site ne détient aucun article de ce numéro dans la version que la date retient,
  et aucun autre article n’est ouvert à sa place.", and "dans le libellé que la date retient" for the EU), for the slice that
  next touches them (done: search results by act, above). The API still reads every article of a state to cut its pages (76 to 118 ms a page of the
  stand-in on this machine, against 263 to 300 ms for the whole): a reader query of the ids and text lengths alone would
  read only the page's text, in the index reader, after index /9 (the data lane's S6, which changes it) merges. R73's LCP,
  INP and CLS measured in a browser at the 75th percentile, and R107's monitoring. The codes themselves, which wait on the
  disk. A page's sitemap entries and head markup (3.6). Previous articles in place, and a contents of every article on the
  page.
- **Measured** (this branch, from `v3/integration` at 32a9522e), with no browser started: the web build; the web suite over 111 of its 114 files (all but `keyboard-walk`, `launch-browser` and `paint-check`), 1,234 of 1,234; `dotnet build Lex.V3.slnx -c Release` 0 warnings, 0 errors; `Lex.V3.Tests` 3,321 (3,315 passed, 6 skipped); the `Lex.V3.Ingest.Tests` classes this slice touched with the brief's (`Census`, `PublicRequestRecordingTests`, the answer, envelope and refusal samples, MCP, the pinned permalinks, as_of, the evidence bundles of both publishers, the EU time view and permalinks, reading by page, resolve, the journey mounts, replay, verify, citation facts, as-published, articles not admitted, the annex control case and annexes, capability, locator, the mounted and machine gates, generation quote, dossiers, search, references, coverage and ask), 324 (317 passed, 7 skipped); the platform schemas, and the answer, envelope and refusal samples, rendered again from the handler with the `V3_RENDER_*` variables and verified without them; the long act's journey mount written once and mounted (`V3_WRITE_LONG_ACT_JOURNEY_MOUNT`), its journeys left to CI; `eng/verify-v3-tree.ps1` 1,615 tracked paths. Rebased on `v3/integration` at #977's merge (3b7913ec), through #974 and #976. The speed checks above were measured before the rebases, on this branch from 32a9522e; neither merge changes what the reading's answer and page carry. After the review's repair, rebased on `v3/integration` at #979's merge (21d3a3e2), with no browser started: the web build; the web suite over 112 of its 115 files (all but the three that start a browser), 1,250 of 1,250; `dotnet build Lex.V3.slnx -c Release` 0 warnings, 0 errors; `Lex.V3.Tests` 3,321 (3,315 passed, 6 skipped); the same `Lex.V3.Ingest.Tests` classes with every `Census`, `Privacy` and `Samples` class, 411 (400 passed, 11 skipped, the opt-in probes and mount writers); the answer, envelope and refusal censuses rendered again from the handler and verified without the variables; `eng/verify-v3-tree.ps1` 1,617 tracked paths.

## A release probed without a browser on this PC (sole driver, 2026-10-18)

On 2026-10-16 headless Chrome runs started by tests on the driver's machine made 263 failed Windows sign-ins and locked
the owner's account; no browser has run on it since. The release command walked the image's pages in a browser, so a
release from this machine could only skip every probe (`--no-probe`), the ones that need no browser with them.
- **What there was** (read from the code): `image-rehearsal.mjs` probed the image running in WSL through the journey's
  runners (`realMountRuns`, `fixtureMountRuns`), whose page steps start a browser (`findBrowser`, `launchBrowser`), then
  asked V2's routes, the server's readiness and the card on a second start. `--no-probe` skipped all of it: readiness,
  journeys J6 to J8 at the API, V2's routes, the card at its route and the privacy probe. The deployment probe's HTTP
  checks of a candidate (`deploy-probe.mjs`) were never asked of the image. `deploy.ps1 -SkipBrowserProbe` ran those
  checks only, and nothing it printed said that no page was walked.
- **`image-rehearsal.mjs --no-browser`**, in the mount form and the custody form: the image is built, verified, signed,
  built again from scratch (unless `--no-reproduce`) and run in WSL as before; then every probe that needs no browser
  runs on one start of it (`probeImage`):
  - journeys J6, J7 and J8 at its API (`apiJourneyRuns`): the contained assistant's card; each page's request, ask,
    events and answer drift through REST and MCP alike; the log polled, its first 25 permalinks verified on a real mount
    (`REAL_MOUNT_EVENTS_VERIFIED`), every one on the fixture's;
  - the deployment probe's checks (`servedPageFailures`, split out of `revisionFailures`, which keeps every check):
    `GET /` to `/fr/` with `Vary: Accept-Language`; the home page as built, or as the release's renderer renders the
    image's own coverage answer; its CSP with `frame-ancestors 'none'`, HSTS, `Referrer-Policy` and `nosniff`; the 404
    page byte for byte; the card at its stable route; coverage from the mount's corpus; the clock; an accented title
    (the check the image's language data section left for the rehearsal's real-mount probes, now asked there in both
    modes);
  - V2's 63 routes, readiness with the mount and no synthetic preview, and the card's bytes at its route;
  - privacy as far as it shows without a page: what the container recorded from its first answer to the last probe (its
    output and its `/tmp`: `processRecordFailures`, the check `journeyVerdict` held, now shared), no answer setting a
    cookie, and no request to another origin (`recordedRequests` wraps the global `fetch` while the probes run).
  - The page steps are skipped whole and named: the report says `browserProbes: "skipped: --no-browser"` and lists
    `stepsNotWalked` (over `derive-c` the 13 screens, J1, J2, J5 and the three EU steps, 19; over the fixture mount 13).
    Every report now says `browserProbes`: `"walked"`, `"skipped: --no-browser"` or `"skipped: --no-probe"`, so none reads
    as browser-probed when it was not. On that path no browser is looked for or started, and the command installs the
    browser trap itself before it runs anything (`armBrowserTrap`), and sees it hold; `probeImage`, `rehearse` and
    `rehearseFromCustody` take `browser` with no default, so a value lost on its way stops the release (the review of
    #979, below).
  - With the browser (no flag) the same probes run first, then the walk as before, each step on its own start; the API
    journeys are asked once, on the shared start. The probes' failures are reported together (`probeFailures`), not the
    first group's alone.
- **`deploy-probe.mjs` without `--browser`** never calls `findBrowser`, as before, and now says so beside its result: no
  page of the revision was walked in a browser (`NOT_WALKED_NOTE`), and its result line says "over HTTP only".
  `deploy/deploy.ps1` walks no page unless `-BrowserProbe` asks (since the review of #979; `-SkipBrowserProbe` is kept and
  changes nothing), runs both its probes with the trap loaded first, and its help lists what its HTTP probes hold and what
  is then walked nowhere.
- **The guard** (`web/scripts/browser-trap.mjs`, `web/test/no-browser-probes.test.mjs`): the trap puts traps in place of
  the launcher's `findBrowser` and `launchBrowser` (in-thread module hooks, `module.registerHooks`, Node 22.15 or later; a
  Node without them refuses to install it) and wraps every process start of `node:child_process` (strict: all of them;
  otherwise a command or script that names a browser or a page walker). The test installs it strict before it imports any
  script, then runs the rehearsal's probe phase against a stand-in image (a local server that answers as a release image
  does and exposes a container's record): over a real mount with an EU index, over the fixture mount, and over a stand-in
  that writes each query and the address on its output and a SQLite file on its `/tmp`, sets a cookie and serves another
  card, each caught with its own reason. Then the deployment probe, in this process and as `deploy.ps1` runs it without
  `-BrowserProbe`, as a process with the trap loaded first (`node --import ./scripts/browser-trap.mjs ...`, which arms it,
  as `LEX_BROWSER_TRAP=exit` does for a script that imports it: a touch throws, so the run's own clean-up still removes its container and work directory, and the run then ends with 97
  whatever it exits with, a caught error included). The trap is never touched. The controls: with the browser asked for,
  the probe phase and the command each stop at the trap before anything starts; a run whose own script walks pages
  (`journey.mjs`) is refused as the trap loads. The run's process check names every browser path the launcher knows (on
  Windows, Linux and macOS) and none of a release's own processes (`dotnet`, `git`, `wsl` with the container's script).
  Six mutations each fail the test: the walk run anyway, a browser looked for first, a process started, the deployment
  probe walking without `--browser`, no step named, the privacy record not held.
- **Over the real API**: the probe phase without a browser against `Lex.V3.Api` over the journey fixture mount (published
  from this branch, without the synthetic preview, as the image holds it), its pages served by the API, the trap loaded
  first: no failure in 2.8 s. 18 API journey runs, the served pages, 63 V2 routes, readiness, the card, and privacy over
  111 requests (no output after its first answer, no file event in its directory or temporary folder, no cookie); 13 page
  steps named as not walked. With the preview left in the publish output, readiness answered 503, as a server with the
  preview does; the image's check that the preview is absent stands before the probes.
- **What is then walked nowhere.** A release probed with `--no-browser` and a deployment without `-BrowserProbe` leave the
  page steps over the real mount unwalked: each page held to what its API answers, its hydration, console, paint and
  security headers, and every printed citation verified. Both walks go by pointer (`keyboard: false`); the keyboard path
  is CI's journeys', over the fixture mounts. CI's `journeys` walks the four fixture mounts only, never a
  population; the last walks over `derive-c` recorded here are the release of 2026-10-13 and the journeys of the same day
  (29 of 29 runs each). The pages changed since are walked over the fixture mounts only.
- **Driver decisions (reversible)**:
  - One start of the image for every probe that needs no browser, not one per group: the population image takes about
    50 s and 2.3 GB of `/tmp` per start. Its privacy record covers them all at once, so a write fails the run without
    naming the probe that caused it. Reversible: a start per group.
  - The page steps are skipped whole, not their server half: `--no-browser` does not ask each step's own request beside
    the walk's (`fromApi`), nor read a law page's bytes as sent (`mustRender`, `frontDoor`). J7 already asks every page's
    request, on the fixture's identifiers, through REST and MCP; the steps' own expectations stay the walk's. Next below.
  - The signed release manifest (`lex-v3-release/1`) does not record how the release was probed: it says what was
    released, and the report kept beside the release says how it was probed. Recording it in the manifest changes its
    schema and the deployment probe's read-back. Reversible with a manifest field.
  - The trap is a script (`web/scripts/`; the tree admits only `*.test.mjs` under `web/test/`), and a real run on this
    machine can load it first. A run's trap is not strict (the rehearsal starts `dotnet`, `wsl` and `git`): it refuses a
    process whose command or script names a browser or a page walker, and any call of the launcher.
  - The release with a browser now also runs the deployment probe's checks and the privacy record on the shared start
    (it ran neither on the image before), so its report gains `servedPages`, `cardRoute.failures` and `privacy`.
  - The tests that start a browser are opt-in by one variable (`LEX_BROWSER_TESTS=1`, set where a browser may run), each
    saying why it skips; `launch-browser`'s second test, node standing in for a browser, runs everywhere. A deployment
    walks pages only when `-BrowserProbe` says so. Reversible: the variable's default.
- **The review of #979** (an independent reviewer: the wiring correct, the guard incomplete): one medium and three low
  findings, repaired in one round, and two more paths made opt-in at the coordinator's direction, under the owner's rule.
  - F1, medium: the release command's own path was not guarded, and its defaults failed open. `probeImage` and `rehearse`
    took `browser = true`, so the rehearsal's call of `probeImage` leaving it out (M1) or the command setting it (M2) passed
    every test and would have found Chrome here; the trap loaded by `--import` without `LEX_BROWSER_TRAP` installed
    nothing, silently; and deleting the `--no-probe` report line (M3) failed nothing. Now with `--no-browser` the command
    installs the trap itself and sees it hold before it runs anything (`armBrowserTrap`); `browser` is required, as a
    boolean, by `probeImage`, `rehearse` and `rehearseFromCustody` (asked first there, before the derivations), so M1
    stops the release; the command's argument mapping is exported (`commandOptions`) and held, M2 failing it, and the
    command as a process is held to arming the trap before it reads its mount; loaded by `--import`, the trap arms itself
    and says so on standard error (`ARMED_NOTICE`); both CI rehearsal checks hold `.browserProbes == "skipped: --no-probe"`.
  - F2: `deploy.ps1`'s help and this section counted the keyboard path among what a walk adds (both walks run by pointer),
    and said the image run holds privacy, which a `--no-probe` release does not; both reworded, with `REMOTE_PRIVACY_NOTE`.
  - F3: the deployment probe's `--browser` control did not first see its process's trap armed (with the variable's
    install disabled it went past the launcher and stopped on an unrelated error). Every process the test starts now has
    node standing in for the browser (`LEX_BROWSER`), and the control runs only after a process loaded the same way is
    seen to hold the trap's launcher.
  - F4: `web/package.json` and its lock say Node 22.15, the first with `module.registerHooks`.
  - Opt-in, the deployment: `deploy.ps1` added `--browser` unless `-SkipBrowserProbe`, so an `-Apply` here without it
    would have started Chrome. It walks no page unless `-BrowserProbe` asks; `-SkipBrowserProbe` stays accepted and
    changes nothing; the two together are refused; without `-BrowserProbe` both probes run under the trap. A plan run,
    held by a test with stand-ins for `az` and `oras` first on the path (a plan runs neither), found the first version of
    this broken: PowerShell's names ignore case, so the new switch `-BrowserProbe` and the script's array `$browserProbe`
    were one variable, and every run of the script would have stopped before reading the release. The array is
    `$browserArgs`, and the kit's test refuses that spelling.
  - Opt-in, the tests: `npm test` started a browser in `keyboard-walk`, `launch-browser` and `paint-check`. They now run
    only with `LEX_BROWSER_TESTS=1`, which CI's `web` lane sets (the only lane that runs `npm test`; `journeys` runs
    `journey.mjs` itself). Checked here with the trap loaded and node as the browser: unset, each skips with its reason
    and touches nothing; set, each reaches the launcher and the trap stops it (97).
- **Not done here**: no image was built or run in WSL (the brief: stand-ins and the fixture mount only; the population
  image needs 2.6 GB and more, and the machine had about 2.4 GB of memory free); the WSL run of `--no-browser` is the
  next release's. Nothing was deployed, pushed or asked of Azure.
- **Measured** after the review's repairs (this branch, from `v3/integration` at #977's merge, 3b7913ec), with no browser
  started: the web build; the web suite over 111 of its 114 files (all but `keyboard-walk`, `launch-browser` and
  `paint-check`), 1,234 of 1,234 (`no-browser-probes` 11 of 11); a plain `npm test` over all 114, 1,238 tests, 1,235
  passed and the three that start a browser skipped with their reason, while a watcher of this machine's processes saw
  no browser start; seven mutations of the repairs each fail the guard or the kit's test; `eng/verify-v3-tree.ps1`
  1,610 tracked paths. No C# was touched; the journeys are CI's lane. Before the review: 1,231 of 1,231.

## Two tests that timed the runner, not the behaviour (sole driver, 2026-10-17)

#974's first `dotnet` run was cancelled at its 45-minute limit on a slow runner: every step took about eight times its
usual time, the build 6 min 18 s for 45 s. Two tests failed there on timing alone, and the re-run passed.
- `V3PageRendererTests.ARenderStoppedForItsTimeOrItsHeapLeavesTheProcessStandingAndItsLanguageAFreshEngine` held "stopped
  by its heap, before its time" to a renderer whose time was 3 s, and filling the heap took 3.5 s there. The heap is now
  asked of a second renderer with a 60 s time and must stop well before it (45 s); the loop is still stopped by the 3 s
  renderer's time. The second renderer renders once before the heap, so "render 1" after the stop shows a fresh
  engine whatever the heap render's place.
- `AzureCustodyProbeContractTests.LinuxSigtermCancelsWithoutReturningOutput` sent SIGTERM 250 ms after the probe
  started, and got the runtime's default 143 when the probe's own handler was not yet registered. The signal is now sent
  after 250 ms, then 2 s, then 8 s, while the answer is 143; the answer must be 130 and no attempt may write output, so
  a probe whose handler is gone still fails.
- Measured: `Lex.V3.Tests` filtered to both classes, 52 of 52 (the SIGTERM test runs on Linux only, in CI).
- The review of #977 found the test changes sound, and three faults in the owner's brief, repaired: "the private
  copy" named both the storage copy of custody and the private copy of the site, with two costs; the brief's plan
  and owner items were not in STATUS.md, which now records them (the plan line, the Azure credits, O15's storage
  option and the public beta question); and its time was local time labelled UTC.

## Search engines: kept out until O27, one switch, the sitemap and the Legislation markup (sole driver, 2026-10-17)

The plan's item 3.6 (`C:\lex-v3\PLAN-2026-10-14.md`), to the specification of record's R67 ("robots.txt allows dossiers and
reading pages, and disallows /api, /mcp, search results and exports. Search pages are noindex. A sitemap lists dossiers and
current reading pages, with lastmod from the build or acquisition time, never valid_from. Pages carry hreflang and schema.org
Legislation markup. Indexing follows R92's handling of names."), with R92, R49, R109 and R64, and the web lane's 3.2c item 3
(R67's "identical version pages point their canonical link there"). The owner has not answered O27 (search-engine indexing),
and the plan says: "If unanswered: the pages open but are kept out of search engines." So the whole mechanism is built and
the site stays out.
- **What there was** (read from the code): no robots.txt (both servers answered the 404 page), no sitemap, no robots element
  or header, no structured data; every law page named as canonical its address with no interface language, which answers a
  redirect by the reader's language (#967's decision, below).
- **One release-level switch**, `LEX_V3_SEARCH_ENGINES`, read by the API when it starts (`Program.cs`, `V3WebRoot.Open`) and
  by the live server when it is made (`createLiveServer`'s `searchEngines`, the variable by default): `indexed`, exactly, opens
  the site; anything else, its absence included, keeps it out. `deploy/deploy.ps1` and `deploy/main.bicep` are unchanged and
  set no such variable, so every release and the private copy are kept out.
  - Kept out: `/robots.txt` disallows `/api/`, `/mcp`, `/health/` and each language's `search.html` and `export.html`, allows
    the pages and names no sitemap (a crawler must fetch a page to read its noindex; review of #976, F1); every response of
    the web root (pages, files, redirects, robots.txt, the 404 page, a method refused) carries `X-Robots-Tag: noindex`;
    every live page names `<meta name="robots" content="noindex">`; the sitemap is an address the site does not hold (its
    404 page).
  - Indexed: robots.txt is the same and names `https://law.soufien.lu/sitemap.xml`; a page the switch opens is served
    without its kept-out marker and without the header; a redirect or a move carries no header (it is no page); every other
    response keeps both.
- **Which pages the switch may open**: each page names one of two robots elements, written by the pages' renderer
  (`Document.jsx`, `indexing`): `never` (`<meta name="robots" content="noindex"/>`) or the kept-out marker
  (`... data-search-engines="kept-out"/>`), which a server removes, once, when the switch is on and the page may be indexed
  (`servedPage`, `V3WebRoot.ServedPage`). The marker stands on a home page rendered from a coverage answer that counts the
  mounted law, Trust and Coverage, a dossier rendered from an answer, and a reading rendered from an answer that shows the
  version its act's readable address shows today; the home page and Trust and Coverage name themselves canonical in their
  language (`/{fr,en}/`, also at `index.html`, and `/{fr,en}/coverage.html`; review of #976, F2). Every other page is
  `never`: the home page as built or with no corpus mounted (F2), search, export, the provision history, compare and radar
  forms, a law's page as built (no law on it), a
  refusal (a work the index does not hold included), a version that is not today's (replaced since, or not yet applicable),
  the 404 and maintenance pages, the German and Luxembourgish pages. The built bytes are kept out: a server that forgot the
  switch serves pages search engines leave alone.
- **Names (R92, the owner's O28)**: a law's page is opened only for a work the release's indexing record lists
  (`lex-v3-indexing-record/1`; the API reads `v3-corpus/indexing-record.json` beside its mount, the live server a file its
  caller names) and the mounted corpus holds: the outcome of R92's measurement of which classes of acts name private
  individuals (the plan's item 1.11, in the data lane), each work it found to name none, with the text languages of its
  readable addresses and the build of the index it read. A record that breaks any rule is refused whole, whatever its size
  (`readIndexingRecord`, `V3WebRoot.ReadIndexingRecord`, held to 4 admitted and 15 refused records). With the site indexed,
  each server asks its API once (the API at startup, `V3ApiHandler.AnswerAtStartup`; the live server on its first need) the
  `dossier` of every listed work, and a work it does not answer is dropped, never opened nor listed (`searchEngineFacts`,
  `V3WebRoot.SearchEngineFacts`; review of #976, F5). Since `data/names-r92` (STATUS-DATA.md, R92) every release measures its
  own mount and carries the record beside it in its image: over derive-r1 it lists 2,642 works (2,572 Luxembourg, 70 EU; the
  sitemap would hold 10,711 addresses in one part), and the names record beside it withholds 974 acts the catalogue names
  only by a title; no served act names a private individual by the measurement. A work the names record withholds is never
  listed (the release checks the two records together), and its dossier answers `text_withheld`, which `SearchEngineFacts`
  drops in any case. The markup names no person (below).
- **R92 on the screens** (`data/names-r92`): a refusal `text_withheld` whose payload's `reason` is `names_private_individuals`
  is said on every screen that asks about one work (search, dossier, reading and export, provision history, compare, radar)
  in one sentence, `NAMES_WITHHELD_SENTENCE` (`live-refusals.mjs`, `tableRefusalSentence` reading the payload): "According to
  Lex's check, this act names private individuals, so Lex shows neither its text nor its titles: read it at the publisher,
  by following the official link." / "D’après la vérification de Lex, cet acte nomme des personnes privées ; Lex n’en
  communique donc ni le texte ni les intitulés : lisez-le chez l’éditeur, en suivant le lien officiel." No person and no
  contact; the card below gives the official link and the digests. French (Decision 41): one form reviewed by an independent
  reviewer (Claude, AI reviewer, not the author) in the addendum of 2026-10-27 "R92: an act that names private individuals" of
  `C:\lex-v3\lanes\fr-review\review.md`, changed in both languages ("personnes privées", the term fixed on 2026-10-18 for
  this check; the claim tied to the check; the link the act's), applied byte for byte and named in `live-chrome-fr.mjs`.
- **The sitemap** (`sitemapFiles`, `V3WebRoot.SitemapFiles`, byte for byte one table), with the site indexed: an index at
  `/sitemap.xml` naming its parts `/sitemap-1.xml` on, each at most the protocol's 50,000 addresses and 50 MiB (review of
  #976, F3: the population already lists about 29,000), built once, at `V3WebRoot.Open` and on the live server's first need
  (F4). Its entries: each interface language's home page (its `x-default` the root) and Trust and Coverage (its `x-default`
  the French page, as the pages' own alternates say), then each held work's dossier and its act's readable address in each
  listed text language, every one in both interface languages with `xhtml:link` alternates for `fr`, `en` and `x-default`
  (the address with no interface language); never a permalink, never search. `lastmod`, on every entry and part, is the
  mounted index's build time, the `built_at` of the snapshot the API's `coverage` names `mounted` (`mountedBuiltAt`; F5),
  never a version's date nor the record's; with none, there is none.
- **The canonical link, revisiting #967's decision**: a law's page names as canonical the address it shows in the page's own
  interface language (`https://law.soufien.lu/{fr,en}/...`), a page that answers itself, where it named the address with no
  interface language, which answers a redirect; the alternates are unchanged (`fr`, `en`, and `x-default` the address with no
  interface language, which sends each reader to theirs). At a permalink whose version is the one its act's readable address
  shows today (Luxembourg's date at the answer's own instant, `answersToday`: a Luxembourg state applicable on or before today
  with no later state applicable on or before it, an act held only as published included; an EU wording in the time view
  dated on or before today with no later wording dated on or before it), the canonical is that readable address, the
  article's when the version holds it (R67), on the server's render (`readingLawPath`) and once loaded (`LiveReading.jsx`). A
  version not today's names itself, as before. The permalink Lex prints and cites still names no interface language (R64).
- **The Legislation markup** (`structured-data.mjs`): JSON-LD in the head of a dossier and a reading rendered from an answer
  (a data block, as the answer's own: no Content-Security-Policy runs it), from the answer's facts only: `url` (the canonical
  link), `legislationIdentifier` (a Luxembourg state's legal resource ELI, the work's ELI on a dossier, or the wording's or
  act's CELEX), `name` (the one publisher's title held for the version's expression in its language, or on a Luxembourg dossier
  for the act as published; none when none or several are held, and never for the EU, whose index holds no title),
  `inLanguage` (BCP 47; a dossier's name in two languages is two values, each tagged with its own, never one language's in
  another's place: F6), `legislationJurisdiction` (`LU`, `EU`), `legislationDate` (a Luxembourg act's date as its work ELI
  names it), `legislationDateVersion` (the date the reading was asked for), `isBasedOn` (the official sources the version's
  quoted articles name) and `copyrightNotice` (an EU text's acknowledgement, as the answer gives it). Never a person (no author,
  `legislationPassedBy` or `legislationResponsible`), a legal force (R21: no in-force flag is held), a legal value (R18) or a
  relation to another act. A refusal carries none.
- **The shared table** (`site-paths.json`) gains its `searchEngines` member (the variable and 8 values, the two robots
  elements, robots.txt in both states, a record of three works, 4 admitted and 15 refused records, the API's 4 answers to the
  servers' own questions, the 2 works they hold and the mounted build's time, 7 coverage and 4 dossier envelopes each
  server reads the same way, the sitemap's files with and without the record and split into parts of 5, 15 page paths
  opened or not in each state, 4 redirects) and the rows `/robots.txt`, `/sitemap.xml`, `/sitemap-1.xml` and
  `/sitemap-2.xml` with both states (128 paths); its 9 canonical rows name the page's interface language, and the 4 at a
  permalink of the sample's state (today's
  version) its readable address. `search-engines.test.mjs` and `V3WebRootSearchEnginesTests` hold both servers to it over
  HTTP, in both states, with and without a record; the canonical rows are rendered by each interface language's built
  renderer, as both servers run it.
- **Journeys** (CI's lane; never run on this machine): the verdict holds the canonical rule (the page's own interface
  language; at a permalink, its own address or its act's readable address) and every page kept out: exactly one robots element,
  `noindex`, and, served by the API, `X-Robots-Tag: noindex`. A run against a release opened to search engines would say
  `searchEngines: "indexed"` and is not held to them.
- **The release probes**: the check that V2 answers none of its routes (the image rehearsal's and the deploy probe's,
  `v2RouteFailures`) required `/robots.txt` to answer 404; it now requires V3's own robots.txt in the release's switch state
  there (kept out unless it says `indexed`), and V3's sitemap index at `/sitemap.xml` once indexed (kept out, a 404 as
  before). The deploy probe's check of the home page reads its head without its robots element, which follows the facts.
- **French**: no new interface text. robots.txt, the sitemap, the robots elements and the markup are for machines, and the
  markup's values are the answer's, so no review addendum.
- **Driver decisions** (reversible):
  - The switch is read at startup from the environment, not built into the pages: a release is opened or closed by its
    deployment, with no rebuild. Only the exact value `indexed` opens; a typo keeps the site out.
  - Fail closed: the pages are built kept out and a server removes the marker, never the reverse; a law's page needs the
    indexing record besides the switch.
  - The canonical names the page's own interface language (revisiting #967): a canonical that answers a redirect is no page
    of its own, two language pages naming one redirecting address told a search engine they were duplicates of it, and a
    sitemap lists only canonical pages that answer 200. `x-default` keeps the address with no interface language, which is what
    it is for.
  - A version that is not today's (replaced since, or not yet applicable) is `noindex` and out of the sitemap, and names its own
    permalink: a search engine leads to the text that applies today; a historical version stays citable and reachable from its
    dossier and its banner.
  - The home page, the search box with no results (a search lives in the fragment, R109, which no crawler sends), is opened
    when rendered from a coverage answer that counts the law; `search.html`, the results screen under its own title, is
    `noindex` and disallowed, as `export.html` is. The robots.txt disallow means a compliant crawler never reads those pages'
    own `noindex`; both stand, as R67 says. The provision history, compare and radar pages show what a reader asks, by
    script: `noindex`, allowed.
  - Kept out, robots.txt is the indexed one without its sitemap, never `Disallow: /` (review of #976, F1): the header and the
    element keep the pages out, and only a page a crawler fetches tells it so; a blocked address can be listed bare.
  - The sitemap is always an index with numbered parts, one shape at every size (F3), and a part is cut at 50,000 addresses
    or 50 MiB, whichever comes first.
  - Which listed works a mount holds is what its API's `dossier` answers, asked once with the site indexed, the API at
    startup and the live server through its API (F5): one rule on both servers, and none of it is asked kept out. A live
    server whose API it cannot reach opens nothing, offers no sitemap, and asks again on the next request.
  - robots.txt also disallows `/health/`, the probes' routes.
  - The sitemap lists each act's readable address, not each article's: an article's page is the whole act with the article
    first until reading by article (3.7) pages it.
  - The indexing record stands beside the mount, read when a corpus is mounted, and binds works by their identity, not the
    index digest: a cleared work stays cleared across builds while the mount holds it, and a new work is kept out until it is
    measured.
  - No startup line says the switch's state: the privacy census pins the API's three startup writes, and `/robots.txt` says it.
- **Not in this part** (next, below): O27 (the owner's); the indexing record, from R92's measurement (the plan's item 1.11, O28,
  the data lane); the objection contact R92 and R49 ask for (O5's privacy notice, its section for people named in acts);
  the deploy parameter that sets the switch (one variable on the container app, once O27 says yes); each page's title and
  description in its head (3.1); the provision history and Coverage pages rendered by the server (3.1).
- **The review of #976** (an independent reviewer; it found the fail-closed core sound on both servers): one medium and five
  low findings, repaired in one round, each tested on both servers.
  - F1, medium: kept out, `Disallow: /` stopped compliant crawlers from ever fetching a page, so they never read its noindex,
    and a blocked address can still be listed bare when linked. Kept out, robots.txt is now the indexed rules without the
    `Sitemap:` line; the header and the element keep the site out.
  - F2: the home page opened at `/{fr,en}/` and `index.html`, and neither it nor Trust and Coverage named a canonical; with no
    corpus mounted the home page could open. Both now name themselves canonical in their language, and the home page opens
    only rendered from a coverage answer that counts the law.
  - F3: a record past 50,000 sitemap addresses was refused whole, and silently opened no law's page. The record is read
    whatever its size, and the sitemap is an index naming parts of at most 50,000 addresses and 50 MiB.
  - F4: the sitemap, 12.9 MB at the population's scale, was rebuilt on every GET and HEAD; it is built once.
  - F5: `lastmod` was the record's `built_at`, and the record was not checked against the mount, so a listed work the mount
    does not hold was listed as a 404 address. `lastmod` is the mounted index's build time, and a work the API's `dossier`
    does not answer is dropped, never opened nor listed.
  - F6 (tests): the JSON-LD's escaping of a `</script>` title, a name in the version's own language (and a dossier's in each),
    and `X-Robots-Tag` on the API's 405 are pinned.
  - Its notes: the live server's header comment no longer says every answer carries the header (a JSON problem does not, as
    the API's do not); the live server serves a file only at the build's own path (no final slash, percent-escape or
    capital), and answers a target of two slashes with its 404 page, as the API does.
  - Measured after the repairs (no rebase: `v3/integration` still at #975's merge), with no browser started: the web build; the web suite over 109 of its 112 files (all but the three that start a browser), 1,207 of 1,207; `dotnet build Lex.V3.slnx -c Release` 0 warnings, 0 errors; `Lex.V3.Tests` 3,186 (3,180 passed, 6 skipped; `V3WebRootSearchEnginesTests` 5); the `Lex.V3.Ingest.Tests` censuses, `PublicRequestRecordingTests` (its three startup writes stand) and the answer, envelope and refusal samples with the EU permalink tests, 58 (55 passed, 3 skipped); `eng/verify-v3-tree.ps1` 1,596 tracked paths.
  - Rebased on `v3/integration` at #974's merge (f9565335; one conflict, this file's top, both sections kept, this one
    first). The exact references' refusal cards (plan 4.2) are shown by script on the search screens, the search page and the
    404 page never opened and the home page as served carrying none; a test holds it. Nothing it changed moves a census or
    sample (they verify without the render switch). Measured again, with no browser started: the web build; the web suite over
    110 of its 113 files, 1,223 of 1,223; `dotnet build Lex.V3.slnx -c Release` 0 warnings, 0 errors; `Lex.V3.Tests` 3,321
    (3,315 passed, 6 skipped); the `Lex.V3.Ingest.Tests` censuses, `PublicRequestRecordingTests` and the samples with the EU
    permalink tests, 60 (57 passed, 3 skipped); `eng/verify-v3-tree.ps1` 1,608 tracked paths.
- **Measured** (this branch, from `v3/integration` at #975's merge, 32a9522e), with no browser started: the web build; the web suite over 109 of its 112 files (all but `keyboard-walk`, `launch-browser` and `paint-check`, which start a browser), 1,206 of 1,206; `dotnet build Lex.V3.slnx -c Release` 0 warnings, 0 errors; `Lex.V3.Tests` 3,185 (3,179 passed, 6 skipped; `V3WebRootSearchEnginesTests` 4, the web root's 20 with it); no `Lex.V3.Ingest` class was touched, and the `Lex.V3.Ingest.Tests` classes the brief names (the censuses, `PublicRequestRecordingTests`, whose three startup writes stand, and the answer, envelope and refusal samples) with the EU permalink tests, 58 (55 passed, 3 skipped); no census or sample was rendered again (no answer, envelope or registry changed); `eng/verify-v3-tree.ps1` 1,596 tracked paths. The journeys are CI's lane.

## Exact references first: the one box opens the act a reference names (sole driver, 2026-10-17)

The plan's item 4.2 ("Exact references first. ELI, CELEX, Mémorial citations, historical identifiers and permalinks open
the act. A citation such as 'Code du travail, article L. 121-6, 15 mars 2021' is read against a fixed table of article
numbers, then opens Reading at that article and date. In the table: 1 = 1er; L. 121-6 = L.121-6; 42 is never 42ter."),
to the specification of record's R59 ("An unambiguous citation opens Reading"), R90 (S4-A03's resolver-first order; no
protected token dropped; absent instruments refused with no list), R13, R21 and R22. Before it, the home box sent an empty
work identifier, so an ELI or a CELEX typed there was searched as Luxembourg words, and its line said so (#971, F3).
- **Where the recognition lives: the API** (driver decision). `search` with no work named reads its query as an exact
  reference before any word is searched (`ExactReferenceReader`, `V3CorpusMount.References.cs`), and every search answer
  reports the phase as `reference` (`phase: exact_reference`, `outcome`, `rule`, `kind`, `recognised`, `opens`, `reason`)
  beside `lanes_run`. REST and MCP answer from the one handler, so they agree; the request schema did not change, so the
  registry digest did not move. With a work named, the phase is not run (`not_run_identifier_given`); a query that is no
  reference is searched as before (`no_reference`).
- **The reference kinds** (each held to the mount in `V3CorpusReferenceMountTests` and `V3CorpusEuropeReferenceMountTests`):
  - a Luxembourg ELI as the publisher writes it (`http://data.legilux.public.lu/eli/etat/leg/loi/1991/08/10/n3`, the public
    site's `https://legilux.public.lu/eli/…`, `eli/etat/…`), at the work, a version (`/jo`, `/consolide/{yyyymmdd}`, a code's
    date) or an expression (`/jo/fr`);
  - a CELEX, consolidated forms included (`32016R0679`, `CELEX:32016R0679`, `02016R0679-20160504`), also inside an EUR-Lex or
    Publications Office address, an EU ELI (`http://data.europa.eu/eli/reg/2016/679/oj`), or an EU act's conventional name
    read to its CELEX (`Regulation (EU) 2016/679`, `règlement (CE) n° 261/2004`, `Directive 2006/112/EC`, `directive
    89/391/CEE`);
  - a Mémorial or Journal officiel A citation (`Mém. A n° 571 de 2021`, `Mémorial A n° 152 du 2 août 2011`, `Journal officiel
    du Grand-Duché de Luxembourg, Mémorial A, n° 571 du 26 juillet 2021`, `JO A 571/2021`), read against the catalogue's
    `isPartOf` facts (`…/eli/etat/leg/memorial/{year}/a{n}`); an issue's date, when written, must be its acts'
    `publicationDate`;
  - a historical identifier of the catalogue (`1790A0001Z`, `historicalLegalId`);
  - an address of this site for law in any form R64's reader accepts, with #972's readable addresses: a work, a permalink at
    an article or not, a dated coordinate, an EU wording's permalink (also of the shape before R64), an act's readable
    address, as a path, under `/fr/` or `/en/`, or under `https://law.soufien.lu`;
  - an act by its type and date (`loi du 10 août 1991`, `loi modifiée du …`, `règlement grand-ducal du …`, `arrêté
    grand-ducal du …`), its title words after the date naming it among those of that day;
  - a citation: an article of any of these acts, or of an act by its title (which must start with an instrument's word:
    code, constitution, loi, règlement, arrêté, convention, traité), with or without a date (`Code du travail, article L.
    121-6, 15 mars 2021`; `article 5 de la loi du 10 août 1991 au 15 mars 2021`; `Article 6 of Regulation (EU) 2016/679, as
    at 25 May 2018`; `32016R0679 art. 38l`), or an act and a date. A bare title is no reference: the title ladder answers it,
    as before.
- **What a reference opens** (no hit, no word searched): the dossier when it names no article and no date; else Reading at
  the article and date. With a date given (the reference's own, its identifier's, or a search date that is not today), the
  permalink of the version the date selects (R64); with none (the search's date is today's, or none), the act's readable
  address (#972, R67), which opens today's version, with the article read in today's version (driver decision, after #972
  merged). The answer's `opens` says the page, the address and its kind (`dossier`, `permalink`, `readable`).
- **What is refused, typed, with what was recognised** (the payload's `recognised_*` scalars: kind, act, held title,
  article, date, date source, and the catalogue's acts this site does not hold): only a strict locator says an act is
  absent (the review of #974, F1): an ELI, a CELEX, a Mémorial issue, a historical identifier, an EU act's name, or a type
  and date whose day holds no act in the index or the publisher's catalogue is `identifier_unknown` with no list. A title is
  no strict locator: a citation by a title the index holds no act under ("Code du travail" today, since codes come with
  Release 1) is searched as words, as typed (the phase's `reason: title_not_held`, said on the page). Several acts (a day,
  an issue or an ELI under which the catalogue names several, held or not; title words after a type and date that name
  none of the day's acts) or several articles of one number are `ambiguous_identifier` with every candidate (their titles,
  the titles' languages, and whether this site holds each), never a guess; a date before
  the held history is `no_version_for_date` (R22), several versions on it `ambiguous_version`; an article the version does
  not hold is `anchor_not_in_version` with its nearest ids (by key, so 42bis's neighbours are 42 and 42ter) and the
  do-not-fall-back rule; a language the act is not held in is `language_not_available`. A date written in a date's shape
  that names no calendar day, or two dates that disagree (a consolidated CELEX's and the citation's), opens nothing and
  searches no word (`not_opened`, its `reason`): the date is never dropped.
- **The fixed table of article numbers** (`src/Lex.V3.Api/article-numbers.json`, embedded; `ArticleNumbers`): the citation
  words, the forms of the first article, the Latin suffixes and their synonyms (nonies is novies), the code letters (L, R,
  D, A), and every shape of article id the indexes hold, each with examples. Keys: 1er, premier and 1 are `1`; L. 121-6,
  L.121-6, L 121-6 and `art_l_121-6`, `art_L.121-6`, `art_l121-6` are `L.121-6`; 42, 42bis, 42ter, 42-1 and XLII are five
  numbers; 3 is never III, 1er never Ier. A quoted article of an amended act (`art_4)_art_13`, `art__art_3bis`) is never
  opened; a grouped article (`art_141_à_144`, `art_632_et_633`, `art_559_560`) opens for a number it groups only when no
  article holds that number itself; a dated id (`art_37-1_20240201`) and a heading after the number are the number's. An EU
  provision id `038L` is `38l`. The table is held to every article id the population's two indexes hold (207 Luxembourg ids,
  1,141 EU ids, read read-only from `derive-c` into `tests/Lex.V3.Tests/Fixtures/ArticleNumbers/held-article-ids.json`) and to
  the AKN fixtures' 208 others (the Code de commerce's ranges among them); code-shaped citations are tested on a synthetic
  "Code du travail" fixture in two versions.
- **Dates** (`CitationDates`): `2021-03-15`, `15/03/2021`, `15.03.2021`, `15-03-2021` (the day first), `15 mars 2021`, `1er
  mars 2021`, `15 March 2021`, `March 15, 2021`, `15. März 2021`, read strictly (31 février, 2021-02-29 and 03/15/2021 are
  in a date's shape and name no day; `2012-1-5` is 5 January 2012); a date before a type and date's own date, after a
  comma or "le", is its title's when the title words then name an act of that day ("… signée à Genève, le 23 septembre
  1936"), else the date asked; after "au", "on" or "as at" it is the date asked. The page prints the date back
  in words in its language (`longDate`). The default is today in Luxembourg (R13, `LuxembourgCalendar`), said apart when
  the reference names no date.
- **The pages** (`LiveSearch.jsx`, `live-search.mjs`, `search-answer.mjs`): the home box and the search page route a
  reference. `opens`: "Recognised: {act}, article {article}, at {date}." (the held title when the index resolved one), then
  "Opening the reading: {address}" as a link, then the page goes there (`location.assign`). `not_opened`: the recognition and
  why ("“31 février 2024” names no day of the calendar, so nothing was opened and no word was searched."). A refusal: the
  recognition above its card, with the phase's own sentences (`LIVE_SEARCH_REFERENCE_REFUSAL_SENTENCES`, or for an EU act
  `LIVE_SEARCH_EUROPE_REFERENCE_REFUSAL_SENTENCES`), the acts a list names (a held one linked, one only the catalogue names
  said as not held), and under an absent act's card the catalogue's act that names it; a held act whose named version (or
  whose selected version's articles) this site does not hold is said as held. The refusal card declares the nine
  `recognised_*` keys (`RECOGNISED_KEYS`) on every code the phase refuses with a declared payload.
- **The EU in the same box** (repairs #971's F3 note): a CELEX, an EU ELI or an EU act's name opens the EU act; the line
  under the home page's box (and the 404 page's) now says what is true: "A search by words covers Luxembourg law. An ELI, a
  CELEX number, a Mémorial reference, a permalink, or the citation of an article of an act, with or without a date, opens
  that act, Luxembourg or EU, when this site holds it. To search within an EU act, name it by its CELEX number on the
  search page." / "La recherche par mots porte sur le droit luxembourgeois. Un ELI, un numéro CELEX, une référence au
  Mémorial, un permalien ou la citation d’un article d’un acte, avec ou sans date, ouvre cet acte, luxembourgeois ou de l’UE,
  lorsque ce site le détient. Pour interroger un acte de l’UE, désignez-le par son numéro CELEX sur la page de recherche."
  (`search.searchScope`, replacing `search.luxembourgOnly`; "when this site holds it" since the review of #974, F1).
  The dated and undated sentences ("Only Luxembourg acts were searched …") stay: they are said of a search by words.
- **French** (Decision 41): 29 forms (25 interface entries with the twelve months, and 4 refusal sentences) were reviewed by
  Claude (AI reviewer) under the owner's delegation in the addendum of 2026-10-17 ("exact references first") of
  `C:\lex-v3\lanes\fr-review\review.md`: one changed (the EU date sentence, "in one wording only", so it matches no EU date
  pattern), 28 approved, 336 checks. The repair's forms (4 interface entries, 2 refusal sentences, and the 3 sentences said
  of an act this site holds) were reviewed by an independent reviewer (Claude, AI reviewer, not the author) in the addendum
  "the repair of exact references (the review of #974)": `search.titleNotHeld` changed ("intitulé", "l’expression"), the
  list's sentence changed in both languages ("does not name a single act or article for certain": a list can hold one act),
  the held-act sentences written by the review, the rest approved; 509 and 188 checks. All are named in the header comment
  of `live-chrome-fr.mjs`.
- **Samples**: the answer census samples the four Luxembourg and two EU reference answers, and the citation generator's
  usual form read back (R65: `usualVerdict`); the refusal census drives eight refusals of the phase (a Mémorial issue's list
  of a held and an unheld act; an EU act outside the census at a date; an EU wording held in no served language among
  them). A code two producers send with different keys (diff's `bound`, the phase's `recognised_*`) keeps a sample per key
  set (`more_payloads`), so no key is in no sample, and the worked example's check reads every sample a code keeps.
- **Verify of the usual citation form** (the citation generator's next item, now that #975 is on the base): the usual form
  the generator composes ("Art. 15 de la loi du 10 août 1991, dans sa version applicable au 1er février 2024") is asked
  as a search's query by the answer census, the phase opens the article it cites at that version, and `usualVerdict`
  (`web/scripts/citation.mjs`) says `matches`, or why not (`another_act`, `another_version`, `another_article`,
  `refused`); an EU form at a date ("Article 99 of Regulation (EU) 2016/679, as at 27 April 2016") is held to its wording
  on the EU mount.
- **On the population** (read-only probes of `derive-c`, not committed; the reviewer's `run1` to `run7` and the repair's
  own, before and after, beside the reviewer's in the scratch folder): `loi du 12 novembre 2011` and `Mém. A n° 233 de 2011`
  list their acts (n1 held and linked; the catalogue's n2, and the règlement grand-ducal of the issue, said as not held);
  n2's ELI is `identifier_unknown` naming the catalogue's act; `loi du 21 septembre 2017` (one act that day) opens, and with
  an earlier date after a comma, "le" or "au" is `no_version_for_date` at that date (`history_begins` 2017-10-06);
  `Code du travail, article L. 121-6, 15 mars 2021` and the reviewer's eight other title forms are word searches
  (`title_not_held`); `règlement (UE) n° 2019/2088`, `Règlement (UE) n° 2016/679`, `Regulation (EU) No 2016/679`,
  `règlement (CE) 593/2008`, `Règlement (UE) 2016/679 du 27 avril 2016` and the expression ELI `…/oj/eng` open their
  dossiers; `2013A3498A` opens `rgd-2013-10-18-n1`; `Article 6 of Regulation (EU) 2016/679` opens
  `/eu-eurlex/32016R0679/eng/006`. Of the reviewer's 60 probes, 38 answer differently, each as the review asked. The
  server reads the EU census's identifiers and the catalogue's issues once when the mount opens (`Warm`, 2.8 s of a 50 s
  start, the rest the indexes' copy and check): the first Mémorial reference answers in 48 ms (was 492), the first EU
  reference in 56 ms (was 2,116), and a search beside it in 266 ms (was 1,877). An EU act's own timeline is still read on its
  first use (the VAT directive's in 3.4 s on a loaded machine, was 2.0 s; MiFIR's in 0.8 s, was 0.3 s), as every time-view
  answer reads it (next).
- **Review** (#974, by the other model family; one repair round; each finding reproduced on `derive-c` with the reviewer's
  own probes, and asked again after the repair):
  - **F1, a false absence (blocking).** A citation by a title the index holds no act under was refused `identifier_unknown`
    ("Code du travail, article L. 121-6, 15 mars 2021", "règlement intérieur article 12", "loi sur le bail commercial,
    article 3"), as were words after a type and date that named no act of the day ("loi du 3 février 2018 relative au bail
    commercial", "… ne s'applique pas") and an earlier date after a comma ("article 2 de la loi …, 1er janvier 2010").
    Repair: only a strict locator says an act is absent; a title the index holds no act under is searched as words, as
    typed, every protected token a term (the phase's `reason: title_not_held`, said on the page); title words that name none
    of a day's acts list them (`ambiguous_identifier`, even of one act); an earlier date after a comma or "le" is the date
    asked unless the title words then name an act of the day; the home line says "when this site holds it". After: the nine
    title forms are word searches; on a day of one act the earlier date is `no_version_for_date` after a comma, "le" and
    "au".
  - **F2, a guess among a day's acts (blocking).** A type and date, an ELI or a Mémorial issue opened the one act the index
    holds while the catalogue names others ("loi du 12 novembre 2011" opened n1; the catalogue names n2). Repair: the
    catalogue's subjects under the day's ELI prefix (`ResolveSubjectsUnder`) or the issue count with the index's works;
    several are `ambiguous_identifier` with every act (`candidates`, `candidate_titles`, `candidate_title_languages`,
    `candidate_held`), none opened; one only the catalogue names is `identifier_unknown` naming it
    (`recognised_catalogue_works`). After: the day lists n1 and n2, "loi du 3 février 2018, article 1er" lists a110 (held)
    and a112, a113 and a130 (not held), the issue lists the loi and the règlement grand-ducal.
  - **F3, EU names read in one order (blocking).** "règlement (UE) n° 2019/2088" was read as 32088R2019 and refused after
    16.5 s; "n° 2016/679" and "No 2016/679" were searched as words. Repair: year first with (UE)/(EU) or a first part from
    2015, else both orders, the first the EU index holds chosen; a name in the EU's shape is an EU name or no reference,
    never a Luxembourg title. After: each opens its dossier, in 3 to 34 ms.
  - **F4, an EU refusal in Luxembourg words.** Repair: `LIVE_SEARCH_EUROPE_REFERENCE_REFUSAL_SENTENCES`, chosen by the
    refusal context's publisher (the reading's EU sentences, and "The wording the date selects holds no article with that
    number, …"). After: "Regulation (EU) 2016/679, 1 January 1990" says "This index holds no wording of this EU act for that
    date." (was "No state of this work … applies on that date.").
  - **F5, cards that could not be shown.** `text_not_available` and `retrieval_mode_unavailable` refused what was
    recognised. Repair: `RECOGNISED_KEYS` optional on both (with the catalogue's acts and the held act), an EU
    `text_not_available` sentence, and the census drives both codes. After: the reviewer's three EU `text_not_available`
    probes show their cards (before: "its card cannot be shown"), as do the eleven refusals put through the screen.
  - **F6.** "2012-1-5" was said to name no day. Repair: an ISO date's month and day may have one digit.
  - **F7.** Repair: an expression's EU ELI (`…/oj/eng`, its language pinned), `eur-lex.europa.eu/eli/…`, a consolidated
    version's EU ELI date, "la loi du …", "art. 2, loi du …", an EU name with its body and its own date, and a part of an
    article ("art. 2 al. 3", "article 2, alinéa 3, de …"), which opens the article and is said with it; the MCP search
    tool's description says the phase first.
  - **Performance.** Repair: `Warm` when the mount opens (above); the EU time view compares texts only when a date holds
    several, and reads the answer's text only when a version without a usable date is held.
  - **The French review's material point** (not the reviewer's): "this site holds no act" was said of a held act whose named
    version is not held (154 held acts name 389 consolidated versions not held). Repair: `recognised_held_act` and
    `recognised_held_version` on every refusal of the phase, and three sentences for a held act.
- **Driver decisions** (reversible):
  - The recognition is in the API, reported as the search answer's `reference` phase, so REST and MCP agree; the page only
    routes.
  - A reference with no date opens the act's readable address (today's version, R67); with one, the permalink of the
    version the date selects (R64). The home box's own today is no date given; a search date that is not today is.
  - A bare ELI, CELEX, Mémorial or historical identifier, or an act by its type and date, opens the dossier; an ELI or a
    permalink of a version opens that version's reading.
  - A bare title stays the title ladder's (no reference), and the act of a citation by title is matched only by the
    ladder's exact and prefix tiers, never by words it shares, so "Code du travail" never opens an act amending it.
  - A grouped article opens for a number it groups only when no article holds that number; a quoted article never.
  - An arabic number never matches a roman one; Latin suffixes have their synonyms; nothing else is near.
  - A date before an act's own date after a comma or "le" is its title's when the title words then name an act of that
    day, else the date asked; after a preposition that asks for a version it is the date asked.
  - (The review of #974.) A day's, an issue's or an ELI's acts are the index's and the catalogue's together, so a list
    names acts this site does not hold, each marked; title words that name none of a day's acts list them even when the
    day holds one, since a guess would be the false absence F1 found in the other direction. An untitled candidate's title
    is null, never its IRI. A part of an article (an alinéa, a paragraph) opens the article and is said with it, as typed.
    An EU name's number and year are read year first with (UE)/(EU) or a first part from 2015, else in both orders, the
    first the EU index holds chosen. The server reads what the phase's first references would read once, when the mount
    opens, not in the background, so no test's mount is disposed under it.
  - The refusals of the phase carry what was recognised as `recognised_*` scalars beside the code's own payload; the samples
    keep a payload per key set.
  - The page shows the recognition and the address, then goes there; the asked date is not carried into Reading's address
    beyond the version it selects (the reading shows that version's own dates).
- **Not in this slice** (next): an OJ L citation (`OJ L 119, 4.5.2016, p. 1`), which needs a catalogue of OJ references the
  EU index does not hold; codes themselves (Release 1: the table and the logic are ready and tested on code-shaped ids); a
  title with a date inside it other than after a comma or "le"; the asked date written into Reading's address (the plan's
  3.5, the search in the address); results grouped by act (4.3); an EU act held outside the census answering a dated
  reference (refused `retrieval_mode_unavailable`, now driven in the census by an act outside it); an EU-shaped name that
  reads to no CELEX ("règlement (UE) n° 5/6") is no reference and is searched as words; an EU act's timeline read without
  its articles' text, so its first use is cheap (today it reads every article of the wordings it compares and pins); an
  `anchor_not_in_version` of an EU act outside the census, which reads no date, said without "the date selects" (the French
  review's optional point).
- **Measured** (no browser was started on this machine: the owner's order of 2026-10-16; CI's journeys lane is the browser
  evidence, and the journeys' home text changed with `search.searchScope` without being run here), after the rebase on
  `v3/integration` at 32a9522e (#975): `npm run build` in `web`, then `node --test` over every web test file but
  `keyboard-walk`, `launch-browser` and `paint-check`, 109 files, 1,213 of 1,213; `dotnet build Lex.V3.slnx -c Release`, 0
  warnings, 0 errors; `Lex.V3.Tests` 3,310 passed, 6 skipped; in `Lex.V3.Ingest.Tests` the classes the slice touches and
  those named for it (`LuxembourgIndex*`, the Census classes, `PublicRequestRecordingTests`, the answer, envelope and
  refusal-payload samples, the Resolve, Search and EU Search mount tests, the two reference mount classes, the machine
  gates, the MCP classes, the pinned-permalink, EU time view and citation classes) 480 passed, 4 skipped; the censuses
  and samples rendered from the handler and verified without the switch; `eng/verify-v3-tree.ps1` passes (1,603 paths); the
  French reviews' checks pass (336, then 509 and 188).
## One citation generator, in three forms (sole driver, 2026-10-17)

The plan's item 3.3 (`C:\lex-v3\PLAN-2026-10-14.md`), to the specification of record's R65 ("One citation generator for
copy, export and permalink, per publisher and language, in three forms ... The citation's language follows the quoted
text. `verify` resolves every form."), with R18 to R21 and R64.
- **What there was** (read from the code): no citation string anywhere. The reading printed each article's permalink in its
  evidence line, the export carried that permalink as `citation`, and nothing copied anything (no copy button exists). The
  API's `citation` operation is lane R4's references written in a state's text, not a citation of it; it is unchanged.
- **One generator** (`web/scripts/citation.mjs`): for one article of one version, (a) the usual form, (b) the usual form
  with its dated permalink under the site's origin, and (c) the evidence form, line by line, each in the language of the
  text cited (`fra`, `eng`), never the interface's. Its words are the chrome tables' `citation` section, read from the table
  of the text's language whatever the page's; a text in another language gets no form (its permalink cites it).
  - Luxembourg, French legal usage: "Art. 15 de la loi du 10 août 1991 sur la profession d’avocat, dans sa version
    applicable au 1er février 2024"; "Art. L. 121-6 du Code du travail". The act by its type and date read from its ELI (loi,
    rgd, agd, rmin, amin; its last segment `n{n}`, or `a{n}` for every act published since 2017), the title's words after
    them when the one title held for the text's expression starts with them and they are at most 200 characters on one line;
    a code by its one held title; the article's number by a fixed rule over the publisher's ids (`art_1er`, `art_iii`,
    `art_unique`, `art_30-2`, `art_l_121-6`, a number before a heading). Any other act type or id shape: no usual form, and
    the page says so ("Lex composes no usual citation for this article; its permalink cites it.").
  - EU, EUR-Lex's numbering read from the CELEX's type, year and number: "Article 33 of Regulation (EU) 2016/679",
    "Regulation (EU) No 575/2013", "Directive 2006/112/EC", "règlement (UE) n° 575/2013", a year before 1999 on two digits;
    the provision by its own heading when it reads "Article ...", else its id's number.
  - The version date, when the version is not known to be the one that answers today in Luxembourg: a Luxembourg state with a
    later held state applicable on or before today, any act held only as published (R76), an EU wording a later one answers
    today, and the original wording of a bundle that holds no consolidation. Said "dans sa version applicable au {date}"
    (R21's "applicable on"), and for the EU ", au {date}" / ", as at {date}", never an applicability date.
  - The evidence form: the usual form (or the permalink), "Permalien : …", the ELI of the resource quoted (or the wording's
    CELEX, a consolidation's own), the publisher's title, the version's or wording's date, the gazette reference (the act's
    Mémorial A issue, or that this index does not hold it; for the EU, that the Official Journal reference is not held), the
    publication date when exactly one is held, the text's SHA-256 (R64's long citation), "Consulted on Lex on {date}
    (date in Luxembourg)", an act held only as published's label, and the authenticity sentence: the as-published sentence of its
    case, the site's sentence on consolidated text, or the EU's statement (in French when the answer's is the table's word
    for word).
- **The facts it reads, from the API** (`V3CorpusMount.CitationFacts.cs`): a Luxembourg `evidence_bundle` carries
  `citation_facts`: the publisher's titles of the listed states' expressions (the dossier's shape, one helper for both) and
  the act as published (`{work}/jo`) with the publisher's `isPartOf` facts on it (the Mémorial A issue) and its
  `publicationDate` facts, verbatim. In the population every held state has one title and one Mémorial issue (1,730 of 1,730,
  counted in `derive-c` read-only); the fixture holds neither, which the forms say. `verify` at an article's (or an EU
  provision's, both EU paths) permalink answers `article_text_sha256`, the digest of the text as `evidence_bundle` serves it.
  No request schema changed (the registry digest did not move); the answer and envelope censuses were rendered again.
- **Wired**: the cited article at its permalink or readable address (`Citations` in `PinnedReading.jsx`) shows the three
  forms as text a reader copies, under "Cite this article", each marked in its text's language when it is not the page's;
  the export's JSON (`citation_forms` on every item and exclusion), CSV (`citation_conventional`, `citation_with_permalink`,
  `citation_evidence`) and PDF (each item's evidence form) keep them, and the export panel shows each item's usual citation.
  `citation` stays the article's hash-pinned permalink, which `verify` takes as it is.
- **verify resolves the forms**: (b) and (c) by the permalink they print (as a path or under `https://law.soufien.lu`), (c) by
  both its hashes, the version's and the text's (`evidenceVerdict`). The census holds `verify` at an article's and an EU
  provision's permalink from the mounts that sent its two bundles, and the web tests check the generator's forms against
  them; the journeys verify the cited article's evidence form on every permalink and readable-address step (CI's lane). The
  usual form is read by the exact-reference phase of the plan's item 4.2 (#974): asked as a search's query, it opens the
  article it cites (`usualVerdict`; the answer census asks it one).
- **French**: the 65 new forms (five reading labels, `export.usualCitation`, the `citation` section, the twelve months among
  them) were reviewed by an independent reviewer in the addendum of 2026-10-17 on the citation generator of
  `C:\lex-v3\lanes\fr-review\review.md`: 3 changed, 62 approved, and named in `live-chrome-fr.mjs`'s header.
  - The evidence form's label is "Avec ses éléments de vérification" (the English stays "Evidence form"): "Forme
    probante" claimed the probative value the form's own authenticity line denies ("il ne fait pas foi", R18 to R20),
    and "éléments de preuve", said of a citation, claims it too.
  - A Luxembourg sole article is "Art. unique", as the act prints it and as every other form says "Art.".
  - The date Lex was consulted is "(date au Luxembourg)", never a place a reader consulted it from; the English changed
    with it ("(date in Luxembourg)").
  - Approved as drafted, after the driver's questions: "dans sa version applicable au {date}" for Luxembourg, ", au
    {date}" for the EU (not "à la date du", which introduces the date of an event), "Publication : Mémorial A n° {number}
    de {year}", "Art. 1er" and "Art. Ier", and the EU's statement in French, word for word against the platform's
    English. The regulation's own French (216/2013, art. 1(2)) is not on this machine, so that check stays with O4.
  - The review found, in the generator, that 1,701 of the 1,730 held Luxembourg works (every act published since 2017,
    whose ELI ends `a{n}`) got no usual form, and that titles run to 3,229 characters, 289 of them with escaped line
    breaks. Both repaired here (driver decisions, below). A first review run under the same brief agreed on every
    entry but two (it gave the label as "Avec ses éléments de preuve" and kept "(Luxembourg)"); the addendum applied is
    the second run's, as the record's driver's note says.
- **Driver decisions** (reversible):
  - One source for each part: the platform serves the facts (`citation_facts`, `article_text_sha256`) and composes no
    citation; the web generator composes the forms and invents no field. A machine client composes from the same facts;
    minting the forms in the API too would be two sources of one string.
  - The citation's words sit in the chrome tables' `citation` section, in both languages, reviewed as the rest of the
    French is, and read by the text's language; the interface labels around them are the page's.
  - The version date is added whenever Lex cannot say the version answers today, including every act held only as
    published, since Lex holds no later version of it. "modifiée" is never written: the index does not hold whether an
    act was amended.
  - A title's words follow the act's type and date only when they are at most 200 characters and on one line: in
    `derive-c`, 662 of the 1,926 French titles that start with a type and date run past 200 characters after it (the
    longest 3,229, an omnibus act listing the acts it amends) and 289 works' titles hold escaped line breaks. Otherwise the
    type and date stand alone ("Art. 1er de la loi du 3 mars 2017"), as French usage cites an act, and the evidence form
    quotes the title whole, verbatim. A heading's no-break spaces are plain ones in the usual form, which is copied.
  - An EU act's domain is the numbering's for its year (EEC to 1992, EC 1994 to 2008, EU from 2010), and none is written for
    1993 and 2009, whose acts carry either. The index holds no EU title, author or domain, so a Euratom or CFSP act would be
    named as the Union's. Of the population's 82 EU acts (the census's seeds, `derive-c` read-only), 76 are regulations and
    directives of the Union or the Community, none Euratom's, and six are treaty texts (the TFEU, the TEU and the Charter,
    in their 2012 and 2016 versions, sector 1). Those six have empty timelines on `derive-c` (no wording of them is held to
    read), so they cannot be cited at all today, by any form; when a wording of one is held, it gets no usual form until
    the generator names treaties (next).
  - The date clauses are the ones the 4.2 reader reads at the end of a citation ("dans sa version applicable au", "au",
    "as at"), and Luxembourg's speaks R21's "applicable" and the EU's no applicability at all.
  - "Consulted on Lex" is the answer's own instant in Luxembourg's calendar: the index holds no time the publisher served
    the bytes (`observation_time` is not held), so the evidence form says when Lex answered, and nothing else.
  - The EU's Official Journal reference is said not held, and is not read from a Formex file's name.
  - The permalink is printed under `https://law.soufien.lu`, the origin of the pages' canonical links.
  - The three forms stand under the cited article, not under every article of a reading (each article's permalink opens its
    page, cited first with its forms; a 3,000-article reading stays light, R73). No clipboard button: it is a script-only
    control the journeys must walk first (next). The dossier names versions by their permalinks and cites no article, so
    it is unchanged.
  - The export's schemas (`lex-v3-export/1`, `-eu/1`) keep their names: the forms are added members and columns.
  - The PDF sets each text in its composed form (Unicode NFC) and says so when that changed anything ("Accents written as
    combining marks are set here as the accented letters they compose"); the JSON and CSV keep the bytes as served, and
    the evidence form quotes a title as served. The usual form is composed (NFC) and its no-break spaces, wide or narrow,
    are plain, since it is copied. A PDF refusal names the field that holds the characters ("article art_2's text",
    "article art_15's evidence form"), never the article's text in place of another field (review of #975, F1).
- **Not in this part** (next, below): R20's own sentence for a
  consolidated Luxembourg text (the act as published and the acts that amended it, the owner's item O4) in place of the
  site's sentence; R19's sentence for a consolidated EU wording, which the API states as it states the original's; the EU
  act's title and Official Journal reference, which no index holds; a consolidated Luxembourg state's title, which starts
  "Version consolidée applicable au …" and is not read into the usual form; the treaties' usual forms ("Article 6 TUE");
  the clipboard button. And a legal claim, the owner's under O4 (review of #975, F2): the EU authenticity line every EU
  evidence form ends with says that only the electronic Official Journal is authentic (Regulation (EU) No 216/2013), while
  R20 dates that: "the electronic Official Journal is authentic since 1 July 2013 (Regulation (EU) No 216/2013), the
  printed edition before". Thirteen of the 63 citable EU acts (`02003L0087` to `02013R0575`) were published before 1 July
  2013, so for them the line names the wrong edition. The platform's sentence (`V3CorpusMount.EuropeTextAuthenticity`) is
  not reworded here: its wording is the owner's, asked in the pull request.
- **Measured** (this branch, rebased on `v3/integration` at #973's merge, 51ca8059), with no browser started: the web build; the web suite over 108 of its 111 files (all but `keyboard-walk`, `launch-browser` and `paint-check`), 1,197 of 1,197 after the review of #975's repairs (1,196 after the French review's, 1,195 before them); the review's export run over `derive-c`'s 7,877 quoted Luxembourg articles, each exported alone, read-only through a local API (the mount's listing unchanged before and after): the PDF is refused for 8, every one of them for characters in the article's own text, which no encoding sets (Σ, ≤, ≥, ↑, and a combining accent with no composed letter), and every one refused before this slice too (23 were, under the encoder before), none newly; the JSON and CSV carry the generator's forms verbatim for all 7,877, and no usual form is in a decomposed form. Measured before the rebase: `dotnet build Lex.V3.slnx -c Release` (and `--no-incremental`) 0 warnings, 0 errors; `Lex.V3.Tests` 3,181 (3,175 passed, 6 skipped); the `Lex.V3.Ingest.Tests` classes touched with Census, `PublicRequestRecordingTests`, the answer, envelope and refusal samples, verify, citation, citation facts, as-published, dossier, evidence bundle, pinned permalink, EU time view, EU permalink, generation quote and MCP, 181 (178 passed, 3 skipped), and the mounted and machine gates, replay, events, locator, resolve, EU capability and journey mounts, 105 (99 passed, 6 skipped); the answer and envelope censuses rendered again and verified without the variables; `eng/verify-v3-tree.ps1` 1,591 tracked paths after the rebase. The journeys are CI's.

## The cited article first, what changed since, a stale permalink's current link, and readable addresses (sole driver, 2026-10-16)

The plan's item 3.2, second part (3.2b; `C:\lex-v3\PLAN-2026-10-14.md`), to the specification of record's R64 ("The server
renders the cited article first, with its own title and canonical URL and a link to the whole act. A permalink always
shows the cited bytes, with a banner when anything changed since ... If the text was later withheld (R83, R92), it
answers `text_withheld` with the hashes. A digest Lex no longer holds answers `pinned_digest_mismatch` with the current
link.") and R67 ("Every work and article has a readable address that resolves to its current version").
- **What the platform answered before this part** (read from the code): both refusals are in the registry, with their
  mandatory payloads (`text_withheld`: official identity, official link, content digest; `pinned_digest_mismatch`: the
  digest asked, the current digest, the stable coordinate and the current hash-pinned link). `verify` answered a mismatch
  at either publisher's permalink, and `resolve` at a Luxembourg state's; `evidence_bundle` answered `text_withheld` only
  when asked by the work and a date. `as_of`, `evidence_bundle` and `provenance` took no permalink (`identifier_unknown`),
  so the reading page asked by the work and the date and, when the state shown was not the pinned one, showed that state
  under a notice: another version in the pinned one's place.
- **`evidence_bundle`, `as_of` and `provenance` at a hash-pinned permalink** (`V3CorpusMount.Pinned.cs`): a state's (or an
  EU wording's) permalink given as the identifier, with its own date, answers the version it pins, read as `verify` reads
  it, for both publishers (the EU through the time view when the census holds the act, else through the original
  wording, as `verify` chooses; `provenance` is Luxembourg's only, as before). A digest the coordinate no longer carries in
  the permalink's language is `pinned_digest_mismatch` naming the current state, with the payload `verify` gives the same
  permalink; two states of its language there with neither the pinned one are `ambiguous_identifier`, and a permalink pins
  one of two states of one date without that date being ambiguous; another language asked beside it, or one the coordinate
  does not hold, is `language_not_available`; a coordinate holding no state is `identifier_unknown`; a date other than the
  permalink's own is `request_schema_invalid` (it would ask another version than the one pinned). The answer is in the
  permalink's language, asked or not. An article's permalink is not one of these identifiers (they name a state, as
  `resolve` does): the page asks by its state's. `text_withheld` names the state's (or wording's) digest beside the
  withholding body's (`state_sha256`, `wording_sha256`): the hashes R64 and R83 ask for, on the card. The scopes say the
  rule. No request schema changed, so the registry digest did not move.
- **The page at a permalink asks by the permalink** (`sitePageFor`, `V3WebRoot.SitePageFor`, `readingPinOf`): the server
  renders it from that answer, and the page as built asks it once loaded; the form keeps the work, the date and the
  language, so a reader asking another date asks the work.
- **The cited article first** (`PinnedReading.jsx`): at an address that names an article the version shown holds, the page
  shows it first, under its own title (the publisher's id, and an EU provision's heading in the text's language), with the
  version it is quoted from ("Quoted from {version}", the version's own heading), its text, own date and notes as the
  reading quotes them (`ArticleText`, now shared), its evidence line and its permalink, and a link to the whole act at that
  version (the state's permalink; at a readable address, the act's readable address). The page's own title is the
  article's ("{work}, {article} - Lex"), from the server and once loaded. An article held without text is named first,
  "Held without text in {version}.", never quoted empty nor said to be quoted. The whole act follows on the same page, the cited article in its place too: the API answers a whole
  version until reading by article (3.7) pages it. The page focuses what it says first, so a reader meets it before the
  text it is about.
- **What changed since** (R64's banner): a Luxembourg state that is not the latest held says "A newer version is applicable
  from {date} (publisher)." (R21's words; the date is the answer's next applicability date), and an EU wording "A later
  wording of this act is held: the wording of {date}." (never an applicability date), each with "Read this act at today's
  date", a link to the act's (or article's) readable address; a readable address is that page already, and links nothing.
  "The publisher replaced this file on {date}" is not said: the API's typed fact for it, a chained log's `file_replaced`
  event, holds no date of the replacement (driver decisions).
- **A stale permalink** (`pinned_digest_mismatch`): the page says "This link pins {pinned}, which is not the version this
  site holds for that date; the version it holds is {current}." (an EU wording's: "...which is not a wording this site holds
  for that date; the wording that answers that date on this site is {current}."), above the refusal card, which shows the
  payload; the notice is in view and has the focus, and the page names the current link canonical. The refusal card takes
  the EU payload's `digest_rule` (it refused it as undeclared). The reading's refusal sentence: "The version this permalink
  pins is not the one this index holds for its date." (EU: "The wording this permalink pins is not one this index holds for
  its date.").
- **Readable addresses** (R67): `/lu-legilux/{work}/{lng}[/{article}]` and `/eu-eurlex/{celex}/{lng}[/{provision}]` open the
  reading of the version that answers today in Luxembourg (R13), on both servers (each server's clock, `LuxembourgCalendar`
  and `luxembourgDate`; a page as built asks at the browser's), the cited article first; `/lu-legilux/{work}` stays the
  dossier. The third segment is a language, never a pin, so no readable address is a permalink; the article is never a
  calendar date, with or without a digest after it, so neither the EU stable coordinate (`/eu-eurlex/{celex}/{lng}/{date}`)
  nor the EU shape before R64 (moved) is read as one. Its canonical link is itself: the article's readable address when the
  version shown holds the article (escaped as the platform escapes it), else the act's.
- **The shared table** (`site-paths.json`) holds 124 paths, rebased on #971's (98 there, its two home pages' `index.html`
  rows among them): 33 served (9 readable, four of them naming their canonical), 14 sent, 7 moved (4 of them a
  consolidated version's readable address, the review of #972), 70 neither (the four home-page rows, rendered from
  `coverage`, among them; 9 readable shapes that are none: the EU stable coordinate, a date as an article, under a
  consolidated CELEX too, `FRA`, `fr`, an empty or `%2E%2E` segment, two segments, `/de/`), and the date it is held at
  (`today`); every served permalink row renders from the permalink itself.
- **French** (Decision 41): eleven new forms (nine reading entries and two refusal sentences), reviewed by an independent
  reviewer in the addendum of 2026-10-16 of `C:\lex-v3\lanes\fr-review\review.md` (1 changed: "Cité d’après {source}.", never
  "Extrait de", the table's word for Lex's own extraction), and a twelfth, `reading.citedWithoutText`, in the review's own
  French ("Détenu sans texte dans {source}."), for the article held without text the review found said to be "quoted";
  named in `live-chrome-fr.mjs`'s header. The four entries the lead says on both publishers' pages are held to neither
  publisher's date words (`date-speech.test.mjs`).
- **Journeys**: the permalink step asks by the permalink and holds the cited article first, in view and focused; the stale
  step is refused `pinned_digest_mismatch`, its notice in view and focused, linking a current version `verify` confirms,
  its canonical that version; a new step opens the work's readable address at the article. On the licence-blocked mount
  the stale step is refused `pinned_digest_mismatch` before any rights are read, and the readable address `text_withheld`.
- **Not in this part** (3.2c, next in order below): quoting the cited bytes of a state only a retained generation holds
  (it answers `pinned_digest_mismatch` naming the current state, while `verify` names the generation: the mount keeps no
  generation's corpus); "The publisher replaced this file on {date}", which needs the date of the replacement; and R67's
  "identical version pages point their canonical link there" (a permalink page showing the current version still names the
  permalink, the 967 decision, which 3.6 revisits with the sitemap).
- **Measured** (this branch, from `v3/integration` at #967's merge, 9673693a): `dotnet build Lex.V3.slnx -c Release` (from
  scratch, `--no-incremental`) 0 warnings, 0 errors; `Lex.V3.Tests` 3,181 (3,175 passed, 6 skipped; a first run missed one
  renderer timing bound, 3,016 ms for 3,000, the flake #967 recorded under load, and passed alone, 7 of 7, and in the
  rerun); the `Lex.V3.Ingest.Tests` classes this slice touched with the brief's (`Census`, `PublicRequestRecordingTests`,
  the answer, envelope and refusal samples, resolve, verify, the pinned permalinks, as_of, the EU time view, the evidence
  bundles, provenance, MCP, the machine and mounted gates and the journey mounts) 316 (309 passed, 7 skipped); the answer,
  envelope and refusal samples rendered again from the handler with the `V3_RENDER_*` variables, then verified without
  them; the web build, and the web suite 1,161 of 1,161 over 105 of its 108 files (the three that start a browser,
  `keyboard-walk`, `launch-browser` and `paint-check`, not run locally after the owner's order of 2026-10-16 that no agent
  starts Chrome on this machine; the full suite, run earlier on this branch, was 1,165 of 1,165); `eng/verify-v3-tree.ps1`
  1,575 tracked paths. The journeys ran once on this branch before that order, against the API published from c70a649f
  (before the French review's change and `provenance` at a permalink, neither of which the journeys read): the fixture
  mount 51 of 51 through the live server and 51 of 51 by keyboard served by the API, the licence-blocked mount 13 of 13 both
  ways, the two-state mount 2 of 2 and the EU annex control mount 3 of 3 both ways; at the final head they are left to CI's
  journeys lane, on Linux.
- **CI on #972 (bc1ffe26)** found one Ingest test on the contract before this part, in the `dotnet` and `image-conditions`
  jobs: `V3CorpusEuropeCapabilityMountTests.AnEuIdentifierIsAttributedToTheEuWhateverItsFormAndOnlyOneNoIndexHoldsIsUnknownToTheTimeView`
  asked `as_of` at an EU wording's permalink at another date and expected `identifier_unknown`. It now leaves the permalink
  out of `as_of`'s unknown identifiers, asserts that `as_of` at the permalink's own date answers the wording it pins (EU
  context), and at another date the `request_schema_invalid` problem (400); `timeline` still answers a permalink
  `identifier_unknown`, unchanged. Every other Ingest test that sends a permalink to `as_of`, `evidence_bundle` or
  `provenance` was looked for and run: the `V3CorpusEurope*`, `V3CorpusAsOf*`, `V3CorpusProvenance*`,
  `V3CorpusEvidenceBundle*` and pinned-permalink classes, with generation quotes, as_observed, citation verification, MCP,
  replay, article history, diff, timeline, citation, in_force_on, status_on, changes_in_period, articles not admitted and the
  EU annex control case: 229 of 229. That repair is db64aa58.
- **The review of #972** (an independent reviewer, its probes over HTTP against both servers, on derived EU mounts): two
  findings, both reproduced, repaired in one round.
  - F1, medium: the EU banner said "A later wording of this act is held: the wording of 2024-01-01" at the English
    permalink of a mount whose 2024-01-01 consolidation is held in French only, and its "Read this act at today's date"
    answered `text_not_available` on both servers: the page printed `next_date`, the selection's bound, which holds
    whether or not the next wording's text is held. The API now says it: `next_text_held` beside `next_date` in
    `evidence_bundle`'s, `as_of`'s and `timeline`'s EU wordings (null with no next wording; the pinned path too), and the
    reader requires it beside every next date. The page says the held form only when it is true, and otherwise "The
    publisher's record names, for this act, a later wording of {date}, whose text this site does not hold in this language."
    (French, reviewed in the addendum of 2026-10-17, which moved "for this act" before the wording in both languages: "Les
    données de l’éditeur nomment, pour cet acte, un libellé ultérieur du {date}, dont ce site ne détient pas le texte dans
    cette langue."), with no link to a text this site does not hold. The Luxembourg banner
    was checked for the same flaw: its date is the next state the index holds, which the sentence names as a version and of
    whose text it says nothing, so it stands. Tested on the branch fixture (`V3CorpusEuropeTimeViewBranchTests`, the API),
    whose English permalink answer joins the envelope census (its derived mount's per-run corpus, index and receipt digests
    replaced, as the census's note says), and in `pinned-reading.test.mjs` on that answer; the accessibility and chrome
    scans read the census's EU bundle.
  - F2, low to medium: on a mount with the consolidations 02016R0679-20240101 and 02016R0679-20250101,
    `/en/eu-eurlex/02016R0679-20240101/eng/005` quoted the wording of 2025-01-01 under the canonical
    `.../eu-eurlex/02016R0679-20240101/eng/005`: the readable branch of `readingAddressShown` built the canonical from the
    request, against #967's rule. A consolidated version's readable address (`/eu-eurlex/0{act}-{yyyymmdd}/{lng}[/{provision}]`)
    now answers `301` to its act's (`/eu-eurlex/3{act}/{lng}[/{provision}]`) on both servers, computed from the address
    alone, its interface language, provision and query kept; its dossier stands. A readable page's canonical and its
    whole-act link are built from the work key or CELEX the answer names. The table's served row for
    `/en/eu-eurlex/02016R0679-20160504/eng/047` is now moved, with three moved rows and one neither added.
  - Measured after the repairs, rebased on `v3/integration` at #971's merge (212ccd71; conflicts in the French header
    comment, this file's top, `LiveReading.jsx`'s R50 sentence, `sitePageFor`'s home page, the journeys' home and
    reading texts and the shared table, each keeping both), with no browser started (the owner's order of 2026-10-16):
    the web build; the web suite over its 107 files but `keyboard-walk`, `launch-browser` and `paint-check`, 1,182 of
    1,182; `dotnet build Lex.V3.slnx -c Release` 0 warnings, 0 errors; `Lex.V3.Tests` 3,181 (3,175 passed, 6 skipped);
    the `Lex.V3.Ingest.Tests` classes `V3CorpusEurope*`, `V3CorpusAsOf*`, `V3CorpusPinnedPermalink*`,
    `V3CorpusEvidenceBundle*`, `V3CorpusProvenance*`, `V3CorpusTimeline*`, `Census`, `PublicRequestRecordingTests` and the
    answer, envelope and refusal samples, 148 (146 passed, 2 skipped); the envelope census rendered again from the handler
    with `V3_RENDER_ENVELOPE_SAMPLES` and verified without it, twice, the answer census unchanged; `eng/verify-v3-tree.ps1`
    1,581 tracked paths. The journeys are CI's.

## The home page, and the sentence that says what Lex is not (sole driver, 2026-10-16)

The plan's items 4.1 (the home page) and 6.8 (the not-legal-advice sentence), to the specification of record's R47, R52,
R11, R13 and R50. PR #955 made the home page the search screen under the site's introduction; it had the search page's
three-field form, no date, no coverage line, and a footer whose advice line R50 replaces.
- **One box** (R52): the home page's form is the one box, placeholder word for word "Search by title, number, ELI or
  CELEX, or words from the text" (French: "Rechercher par intitulé, numéro, ELI ou CELEX, ou par des mots du texte"), a
  date field and the language; the box is the first control of the page's content. The search page keeps the optional
  work identifier, which is how an EU act is searched (`LiveSearch`'s `home` and `search` forms of one screen); the 404
  page offers the home page's form.
- **Today** (R13): the date field starts at the calendar date in Luxembourg, by the coded rule (`luxembourgDate`, plan
  0.8): when the server renders the page, of the instant the API answered (the answer's `context.freshness.observed_at`);
  on the page as built, of this browser's clock once hydrated (`todayInLuxembourg`); never the build's time. The line
  under the field says it ("Today in Luxembourg: {date}." / "Aujourd’hui au Luxembourg : {date}."). It is sent as the
  search's `date`, the platform's own parameter and the one date mechanism a search has (the reading form's date input,
  extended to search): each Luxembourg work then contributes the version that applies on that date, and a named work is
  answered as `as_of` answers it. The answer echoes it (`requested_date`) and the results print it, said of what this
  site holds (since the review, below): "Only Luxembourg acts were searched. For each, the latest version this site
  holds that is applicable from {date} or an earlier date (publisher) was searched, and an act with no such version was
  not. An act held only as published is searched as published, and may have been amended since.", or with no date
  "Only Luxembourg acts were searched, with no date: every version this site holds was searched, and each result names
  its version's date." The request and the answer's echo are the search's trace; the date in the address is the plan's
  item 3.5.
- **The home page is rendered by the server** (R67, as a law's page is): both servers ask `coverage` for `/fr/`, `/en/`
  and each one's `index.html` (`sitePageFor` in `permalinks.mjs`, `V3WebRoot.SitePageFor`, held to the shared table,
  `site-paths.json`), render the page from that envelope (`ssr-entry.jsx`, kind `home`) and write into it the facts it
  shows (`lex-home-facts`: today and the coverage line, a few values, never the envelope, whose capability cells run to
  thousands of rows on a population), which the browser hydrates the same tree from, so the page asks nothing as it
  loads. A page served as built (no renderer, or a render that failed) asks `coverage` once loaded. The deploy probe takes
  `/fr/` as the release's own home page as built, byte for byte, or rendered: the built page's head byte for byte, its
  script and its facts block (`isOwnHomePage`).
- **The coverage line** (R11): "This site holds {acts} of Luxembourg law, in {versions} with {articles}, as of the index
  built on {date}.", every count the answer's `totals` (works, states, articles), the date that of the build the mounted
  log records as `mounted` (`history.snapshots_with_text`), in Luxembourg's calendar as today is. A log that records no
  build says the counts and "the index it holds records no build date"; with no corpus mounted the line is the coverage
  page's own sentence ("This build has no index mounted."); an answer the coverage reader refuses shows no line. The
  coverage answer counts the Luxembourg mount only, so the line says nothing of EU counts, which no answer serves.
- **What Lex is and is not**: the site's reviewed introduction under its own heading, and R50's sentence directly under
  the box. **Links**: Changes (`radar.html`) and Trust and Coverage (`coverage.html`), by their pages' own names.
  Developers and Get help have no live page, so nothing links to them (R52). **Nothing unserved** (R52): no assistant,
  chip, toggle, basket or translation control; a test holds the page to it.
- **No example searches.** They would have to come from what the mount serves, and neither the coverage answer nor
  `browse` names a title or a text (browse names a work by its identifiers, "titles are served by dossier and resolve"),
  so no example could be shown to land on held text without a literal the mount may not hold. None is shown; they come
  when an answer serves them (next, below).
- **On phones** (R47): the box first and the sentence under the form in view. The site's header and the home page's
  heading step down under 30rem, and the home form sets its date and language side by side. Measured in Chrome with the
  fixture's coverage answer (the bottom of the box, then of the sentence, in CSS pixels): English 333 and 591 at 320 by
  640, 333 and 569 at 360 by 640 and at 390 by 844; French 387 and 623 at 320 by 640, 364 and 599 at 360 by 640. The
  journeys' home step holds it at 320 by 640.
- **R50's sentence** (6.8): "Lex shows the published text of the law. It does not tell you whether or how it applies to
  your situation and is not legal advice." and J6's French, "Lex montre le texte de la loi. Il ne dit pas ce qui
  s’applique à votre situation, et ce n’est pas un conseil juridique." (`site.notAdvice`), on the home page under the
  box, above the search results on the home and search pages, above the text in Reading (as built, and at a permalink
  as both servers render it), and first in the footer of every live page, where it replaces "Documentation, not legal
  advice."; the footer's authenticity sentence stands on its own line (`site.authenticity`, unchanged words; R20
  rewrites it). It stands outside the answer views, whose date-word test is about dates, not about applying the law.
- **The search page's dates**: its date field starts empty, so the search a reader asks there is unchanged; a named
  Luxembourg work and a date meet `as_of`'s refusals, said with the reading's reviewed sentences; an EU act and a date
  meet the platform's `retrieval_mode_unavailable`, said as "An EU act is searched in the wording this server holds,
  not at a date: leave the date empty to search it."
- **No count is a literal** (R11): no test held the interface's words to it (`no-hit-card.test.mjs` holds a population's
  count to its date). `no-literal-counts.test.mjs` refuses the pack's figures (1,402, 1,250, 24,579, 23,370, 24,622,
  1,252, 2026-08-27, "August 2026", in any grouping) in both tables, the French refusal sentences, every refusal sentence
  a screen says and every live page rendered; holds the tables to say a number only where its allowlist names a citation
  of law or a statistical label; and holds the coverage line to the answer's counts exactly, the pack's own figures
  included when an answer serves them.
- **The journeys**: the home step (`frontDoor`) must arrive rendered (its facts block in the page's own bytes), with its
  coverage line counted (with no mount, the no-mount line) and today in its date field; its search must send today
  (`TODAY_IN_LUXEMBOURG` in the step's body, filled with the run's today at its start or end, so a run across midnight in
  Luxembourg passes on either day); and at 320 by 640 the box must be the first control and it and the sentence under
  it in view. The home, search, reading and permalink steps expect R50's sentence, the home step its coverage line and
  its dated search, the search step its undated one. The 404 step keeps the home page's search, not its front door.
- **French** (Decision 41): the 17 new forms (16 interface entries and the EU refusal sentence) were reviewed by Claude
  (AI reviewer) under the owner's delegation in the addendum of 2026-10-16 of `C:\lex-v3\lanes\fr-review\review.md`:
  none changed, `site.notAdvice` approved as R50 fixes it with three points for O4. They are named in the header
  comment of `live-chrome-fr.mjs`.
- **Under O4, the lawyer's read** (R50, R22; recorded for the owner, not decided here): the dated search's sentence
  (above, in both languages) is a public claim about what a search covered, and goes to the same read. And: J6's
  "conseil juridique" ships as R50 specifies it, against the French review's term T10, "consultation juridique" (loi
  modifiée du 10 août 1991, art. 2(2)), which every other French entry follows; if O4 keeps T10, the sentence ends "et
  ce n’est pas une consultation juridique". The review's two other points for the same read: J6 does not say
  "published" ("le texte publié de la loi" would), and says "ce qui s’applique à votre situation" for "whether or how it
  applies to your situation".
- **Driver decisions** (reversible):
  - The home page is rendered by the server from the coverage answer, with the facts it shows, not the envelope, in the
    page; "server-rendered where the page is rendered, else on load" both hold.
  - Today on a rendered page is the API's instant, on a page as built the browser's clock; the coverage line's date is
    the mounted build's date in Luxembourg's calendar, so the two dates compare (R13: "When the index is older, both
    dates are shown").
  - The search page's date starts empty, the home page's at today: an EU act's search takes no date.
  - "The one-line statement" (R47) is read as R50's sentence directly under the box; the header's tagline stays in view
    on a phone too.
  - The box's placeholder names ELI and CELEX as R52 words it; until the plan's item 4.2 (exact references first), an
    identifier typed into the box is searched as words, and the search page's work identifier names a work.
  - No example searches, until an answer serves examples that land on held text.
  - The home page's box stays Luxembourg's (an empty identifier, as the 404 page's): its sentences say so, and a line
    under it leads to the search page for an EU act; routing an identifier typed in the box is item 4.2.
  - The deploy probe renders a served home page again with the release's own renderer, rather than recording that as
    next: it is one function (`renderedHomePage`) over files the probe already holds.
- **Review** (independent Claude subagent, PR #971): changes requested, three material findings, each reproduced, repaired
  in one round.
  - F1, the dated search's sentence overstated: "Searched the version of each act that applies on {date}" said a
    version applies, where the search takes the latest held state on or before the date (`SelectAtDate`) and every
    held Luxembourg act is held as first published, 273 of them with an end-of-force date before today (R21, R22, R76).
    Repaired: the sentence says the latest version this site holds applicable from the date or an earlier one
    (publisher), and that an act held only as published is searched as published and may have been amended since,
    in R21's words and the as-published label's; `search.undated` names Luxembourg too. Both French forms were
    reviewed (the addendum's section of 2026-10-17). The label on each hit is item 4.3's.
  - F2, the deploy probe's `isOwnHomePage` passed any body with the built head, a script tag and the facts block's
    opening tag, the facts never read: a swapped footer line, a root replaced by anything with empty facts, and a count
    typed into the coverage line all passed. Repaired: `homePageFailures` holds everything outside the root's content
    and the facts block to the built page byte for byte, reads the facts with `readHomeFactsValue`, and renders the page
    again with the release's own `ssr/fr.js` from the probe's coverage answer on the facts' day, which must equal the
    served page byte for byte; with no renderer or no answer it fails closed. Held on stand-ins (`deploy-probe.test.mjs`)
    and on the real built renderer (`live-ssr.test.mjs`), where the page it renders passes and the four the review
    found do not.
  - F3, the home page's box searches Luxembourg law only and nothing said so, under a heading of Luxembourg and EU law
    and a placeholder naming ELI and CELEX. Repaired: the dated and undated sentences say "Only Luxembourg acts were
    searched", the no-hit sentence of a search naming no work says "the Luxembourg text", and a line under the home
    page's box and the 404 page's says "This search covers Luxembourg law. To search within an EU act, name it by its
    CELEX number on the search page." (French: "Cette recherche porte sur le droit luxembourgeois. Pour interroger un
    acte de l’UE, désignez-le par son numéro CELEX sur la page de recherche."), below R50's sentence so the phone's
    first screen is unchanged. No identifier is routed (item 4.2).
  - The journeys' expectations follow the new wording (`homeTexts`, the search step); they were not run on this machine
    for the repair (no browser runs here since 2026-10-16), so CI's journeys lane is their evidence.
- **Not in this slice** (next): the as-published label on each hit of a search (item 4.3: the dated sentence says it
  once, above the hits); the help block on high-stakes acts and the Get help page (plan 6.7, R50); examples served
  by the API (coverage could name, per language, a held title the resolver maps to one work whose strict search finds
  text, computed at build); the search in the address after `#` (3.5); EU counts on the home page once an answer counts
  the EU index; today beside the index date on the other pages (3.4); exact references from the one box (4.2); a
  Developers page and its link (8.x).
- **Measured** (after the review's repair, with no browser run on this machine: since 2026-10-16 the owner's order keeps
  every browser off it, and CI's journeys lane is the browser evidence): `npm run build` in `web`, then `node --test` over
  every web test file but the three that start a browser (`keyboard-walk`, `launch-browser`, `paint-check`, which CI runs),
  106 files, 1,172 of 1,172; `dotnet build Lex.V3.slnx -c Release`, 0 warnings, 0 errors; `Lex.V3.Tests` 3,175 passed, 6
  skipped; in `Lex.V3.Ingest.Tests` the Census and `PublicRequestRecordingTests` classes 44 passed, 2 skipped (the slice
  touches no Ingest class); `eng/verify-v3-tree.ps1` passes; the French review's checks, 17 forms and 209 checks, then the
  repair's 5 forms and 64 checks, all pass. Before the review, on 2026-10-16, the journeys against an API published from
  this branch passed (the fixture mount 49 of 49 through the live server and 49 of 49 by keyboard on the pages the API
  serves, the licence-blocked mount 12 of 12, the two-state mount 2 of 2 and the EU annex mount 3 of 3 in both modes), and
  the home page measured in Chrome at 320 by 640 kept the box and the sentence under it in view (English 333 and 591 px,
  French 387 and 623 px); the repair adds its scope line below that sentence and changes no wording above it.

## One permalink shape: publisher, work, date and digest, language, article (sole driver, 2026-10-15)

The plan's item 3.2, first part (`C:\lex-v3\PLAN-2026-10-14.md`), to the specification of record's R64: "One permalink
shape: /{publisher}/{work}/{date}--{digest}/{language}/{anchor}, where the digest is that language's body. '#' is used
only for positions inside an article. ... The interface language never enters a permalink." The API minted two shapes:
a Luxembourg state's `/lu-legilux/{work}/{date}--{sha256}` with the article after `#` and no language, and an EU
wording's `/eu-eurlex/{celex}/{lng}/{date}--{sha256}` with the provision after `#`.
- **What the digest is, per publisher** (read from the code that mints it): neither is a digest of the publisher's
  bytes, and both are already per language.
  - Luxembourg's `state_sha256` (`LuxembourgIndexBuilder.StateSha256`, domain `lex-v3-luxembourg-expression-state/1`)
    digests one expression state: the work key, the applicability date, the expression, work and legal-resource IRIs,
    the language, the rule profiles and every article identity of that expression, sorted. An index state row is keyed by
    work, date, expression and language, so a state is one language's; each article identity is itself a digest of the
    article's text tokens (`lex-v3-luxembourg-akn-legal-content-article/1`), so the digest pins that language's text.
  - The EU `wording_sha256` (`EuropeWordingDigestRule`, domain `lex-v3-eu-wording/1`) digests the CELEX, the work and
    expression IRIs, the language, the wording date and every article identity of the expression in the publisher's
    order: one language's wording, its text pinned through the article identities.
- **The new shape, minted everywhere the API mints a permalink**: `/lu-legilux/{work}/{date}--{state sha256}/{lng}` and
  `/eu-eurlex/{celex}/{date}--{wording sha256}/{lng}`, `{lng}` the text's three letters (`fra`, `deu`, `eng`); an
  article's permalink adds the publisher's id as a last segment, escaped as .NET's `Uri.EscapeDataString` escapes it
  (`art_1er`, `Article%201%282%29`), where `#` stood before. Every answer that carries one changes with it: as_of,
  timeline, dossier, evidence_bundle (state and article permalinks), provenance, status_on, article_history, diff,
  changes_in_period, in_force_on, cited_by, citation, search (hits, candidates, the EU pinned wording), verify, events
  and answer_drift (`permalink`, `replaced_permalink`, `answered_before`, `answered_now`), as_observed, and the
  refusals that name one (`current_hash_pinned_url`, `candidates`); MCP answers through the same dispatch. The scope
  sentences that described the article "after #" now say "as a last segment". The stable coordinates
  (`/lu-legilux/{work}/{date}`, `/eu-eurlex/{celex}/{lng}/{date}`) are not permalinks and keep their shape.
- **The API reads the new shape and only it** (`PermalinkSegments`, one reader for both publishers: as a path, under
  `https://law.soufien.lu`, or under `/fr/` and `/en/`; no query, no `#`, no empty segment; a calendar date, a lower-case
  digest, three lower-case letters, and an article of RFC 3986's path characters, never one that is `.` or `..` once
  decoded, `%2E` and `.%2e` included; under the origin the path is read as written, so both forms name the same
  addresses). The language now counts: `resolve` and `verify` answer a Luxembourg permalink from its language's states
  only, so a German state's digest named under `fra` is `pinned_digest_mismatch` naming the French state, a coordinate
  with no state in that language is `language_not_available` to `verify` (`identifier_unknown` to `resolve`), and
  `ambiguous_identifier` names two states of one language at one date. A `language` asked beside a permalink that names
  another is `language_not_available`, offering the languages the coordinate holds (the permalink's own when held, else
  those held), as the EU answers; a coordinate that holds no state is `identifier_unknown`.
- **The site**: the reading page stands at `/{fr,en}/lu-legilux/{work}/{date}--{sha256}/{lng}[/{article}]` and
  `/{fr,en}/eu-eurlex/{celex}/{date}--{sha256}/{lng}[/{provision}]` on both servers (`permalinks.mjs`, `V3WebRoot`), and
  the server renders it from `evidence_bundle` asked in the permalink's language (a Luxembourg reading now asks in that
  language, as the EU one did). The page brings the article the path names into view; a search hit links its article
  as a segment; the export cites an EU article without text in the new shape. The citation checker, the UX URL module
  (`urls.mjs`, which the refusal card reads candidates with) and the previews speak the new shape.
- **The shapes before R64**: an EU wording's permalink names everything the new one does, so
  `/eu-eurlex/{celex}/{lng}/{date}--{sha256}` answers `301 Moved Permanently` to the new address, under the interface
  language it had (`/fr/...` to `/fr/...`, none to none), the query kept, the same answer for every reader (no `Vary`);
  a browser keeps its `#provision`, which the reading page still brings into view. A Luxembourg state's permalink of that
  time names no language, so no address can be computed from it alone: it is answered as any address the site does not
  hold (the API's routing; the live server's 404). Nothing outside this machine holds one: the site has never been public.
- **Canonical and hreflang**: a law's page under `/fr/` or `/en/` names as canonical an address with no interface
  language that the platform mints (`https://law.soufien.lu/lu-legilux/.../fra/art_1er`, the permalink Lex prints and
  cites, which sends each reader to theirs), with alternates for `fr`, `en` and `x-default` (the address itself). It is
  taken from the answer, never from the request (`readingAddressShown`): the article's permalink when the reading shows
  the state (or wording) the address pins and that state holds the article the path names, quoted or without text
  (`art%5F15` names `art_15`'s), else the state's; at a stale permalink the version shown, with no article; for a
  refusal the pinned state's permalink. The server's render writes them (`Document.jsx`, `lawPageLinks`); the page as
  built, which a server sends when it cannot render, writes the state's permalink once loaded (`client.jsx`,
  `addressBeforeAnswer`) and the answer's address once its reading arrives (`LiveReading.jsx`, `head-links.mjs`). Link
  elements only: the Content-Security-Policy is unchanged. The dossier at a work's address names that address.
- **The shared table** (`tests/Lex.V3.Tests/Fixtures/SitePaths/site-paths.json`) gains `moved` and `canonical` and holds
  96 paths: 24 served (anchors included, three percent-encoded: a space, an escape the platform does not write, and
  `%2E%2E%2E`), 10 sent to the reader's language, 3 moved (the EU shape before R64, with and without an interface
  language), 59 neither (the Luxembourg shape before R64, a two-letter or upper-case language, `.`, `..` and their
  percent-encoded forms, two anchor segments, a broken escape, a space, `#`, a final newline); `permalinks.test.mjs` and
  `V3WebRootTests` hold both servers to it, and to what each page is rendered from. Five served rows name the canonical
  link their page gets from the evidence_bundle sample (the article minted, `art%5F15`, `no-such-article`, none, and a
  stale digest), which `live-ssr.test.mjs` renders, before and after the answer, and for a refusal.
- **The journeys** open the permalink and stale steps in the new shape (`/en/lu-legilux/{work}/{date}--{sha256}/fra/art_15`),
  ask the reading in its language, and now check each rendered law page's canonical link and alternates in the browser,
  every link counted, a stale page's canonical with no article.
- **Not in this part** (3.2b, done on 2026-10-16, above): the cited article rendered first with its own title, the banner when
  anything changed since, `text_withheld` and `pinned_digest_mismatch` at a permalink, and readable current-version
  addresses.
- **Measured** (this branch, from `v3/integration` at #962's merge): web suite 1,115 of 1,115; `dotnet build
  Lex.V3.slnx -c Release` 0 warnings, 0 errors; `Lex.V3.Tests` 3,157 (3,156 passed, 1 skipped); `Lex.V3.Ingest.Tests`
  whole, 3,014 (2,985 passed, 26 skipped, 3 failed: two EU time-view assertions still on the old shape, repaired), then
  `V3FirstMountBuildTests` with `Census`, `PublicRequestRecordingTests` and `AnswerSamples` 91 (88 passed, 3 skipped);
  the answer, envelope and refusal samples and the evaluation card rendered again (the card's EU temporal
  case digests move with the permalinks they key) and verified; the journeys against the API published from this branch:
  the fixture mount through the live server 45 of 45 and by keyboard served by the API 45 of 45, the licence-blocked
  mount 12 of 12 both ways, the two-state mount 2 of 2 both ways, the EU annex control mount 3 of 3 both ways;
  `eng/verify-v3-tree.ps1` 1,505 tracked paths (before the rebase; the review's measures below are at the final head).
- **The review of #967** (an independent reviewer, its probes run against both servers over the journey's fixture
  mount): three findings, all reproduced, repaired in one round.
  - Medium: the canonical link and hreflang repeated any article segment, ones the platform never mints included:
    `/en/.../fra/no-such-article` answered 200 naming itself canonical, and `/fra/art%5F15` named `.../art%5F15` where the
    minted permalink is `.../art_15` (`readingAddressShown` returned the request's address whenever the pinned state was
    shown, and the page as built wrote the request's address). Now the canonical is taken from the answer: the minted
    permalink of the article whose publisher id is the segment decoded, quoted or without text, else the state's; the
    version shown with no article at a stale permalink; the pinned state's permalink for a refusal. The page as built
    names the state's permalink until its reading arrives (`addressBeforeAnswer`). Five table rows say each page's
    canonical, and `live-ssr.test.mjs` renders them; the journey counts every canonical and alternate link (a map of the
    alternates would have hidden a duplicate) and holds a stale page's canonical to no article.
  - Low: the API's web root served a percent-encoded dot segment (`/fra/%2E%2E`, `%2e`, `.%2E`) as an article, which the
    live server and a browser resolve away; `PermalinkSegments` read it as the article `..` given as a path and resolved
    it away given under the origin. A segment that is `.` or `..` once decoded (`%2e` in either case) is no article in
    `permalinks.mjs`, `urls.mjs`, `V3WebRoot` and `IsAnchorSegment`, and under `https://law.soufien.lu` the path is read as
    written rather than through `Uri.AbsolutePath`, so both forms name the same addresses (a backslash, or `/en/./`, is
    no permalink in either). Seven table rows, and tests of `parseObjectUrl` and `verify` in both forms.
  - Low: Luxembourg `verify` of `/.../deu` (a coordinate holding only French) asked with `language: eng` offered
    `["deu"]`. It now offers the languages the coordinate holds, the permalink's own when held, as the EU path does; a
    coordinate with no state (and no retained generation holding the digest) is `identifier_unknown`. Tested; the
    sentence above that said "as the EU already answered" now says what is offered.
  - Measured after the repairs, rebased on `v3/integration` at #964's merge: web suite 1,141 of 1,141; `dotnet build
    Lex.V3.slnx -c Release` 0 warnings, 0 errors; `Lex.V3.Tests` 3,176 (3,170 passed, 6 skipped; a first run beside
    another session's whole Ingest suite missed one renderer timing bound, 3,329 ms for 3,000, and passed alone and
    on a quiet machine); the `Lex.V3.Ingest.Tests` classes this slice and #964 touched, with `Census`,
    `PublicRequestRecordingTests`, the answer, envelope and refusal samples, `V3FirstMountBuildTests`, resolve, verify,
    the EU time view and the machine and mounted gates, 456 (451 passed, 5 skipped); the samples and the evaluation card
    rendered again from the handler (the answer samples changed: two permalinks #964's as-published scenarios wrote in
    the shape before R64) and verified without the render variables; the journeys against the API published from this
    head over the fixture mount, through the live server 45 of 45 and by keyboard served by the API 45 of 45; both
    servers asked the review's addresses again, answering alike. Rebased again on #969's merge (the data lane's
    producers only): the build 0 warnings, 0 errors; `Lex.V3.Tests` 3,176 (3,170 passed, 6 skipped); the web suite 1,141
    of 1,141; `Census`, the three samples, resolve and verify 103 (101 passed, 2 skipped); `eng/verify-v3-tree.ps1`
    1,561 tracked paths.

## A missing page, a failure and a deployment never look like a legal answer (sole driver, 2026-10-15)

The plan's item 6.9 (`C:\lex-v3\PLAN-2026-10-14.md`, Phase 6), from the specification of record's R60: "Service failures
are not refusals. A 404 page offers search. A failure says 'Lex could not complete this request. This is not an answer
about the law.', with a retry and official links. Failures are shown per panel, and a maintenance page shows during
deployments. The operational refusals (rate_limited, upstream_unreachable, no_corpus_mounted, snapshot_unknown) render
from their payloads with the same line. A failure never shows as an empty list, a zero or 'no version'." R52 (nothing
unserved is shown) and R61 (French and English under `/fr/` and `/en/`) hold on the new pages.
- **Before:** both servers answered an address nothing claims with a JSON problem, even a browser asking for
  `/fr/no-such-page` (the API `unknown_route`, since #963; the live server `not_found`). Each screen said a failure in
  one English sentence, such as "The search could not be reached (api_unreachable).", with no retry and no official
  links, and showed `no_corpus_mounted` as a refusal card, laid out like an answer.
- **The 404 page** (`live-not-found-page.jsx`; built per language as `not-found/en.html` and `not-found/fr.html`): "There is
  no page at this address", then "Check the address, or search the texts this site holds below. A page missing here says
  nothing about the law, only about this site's pages." It has a link to the home page and the home page's own search
  screen, hydrated by the same script, so a phrase still goes only into a request body. It carries the site's header and
  footer and the reviewed CSP. It names no address of its own (no canonical or alternate links), and its text never
  repeats the address that was asked; once loaded, its language menu leads to the same address in the other language.
- **Both servers answer with it** (`V3WebRoot.NotFound.cs` and `V3ApiHandler`; `not-found.mjs` and `serve-live.mjs`). A
  page request that nothing claims gets status 404, in the reader's language: the path's `/fr/` or `/en/`, else the
  language the root's redirect picks from Accept-Language (French by default), in which case the answer says
  `Vary: Accept-Language`. The API sends every header a page carries (CSP with `frame-ancestors 'none'`, HSTS,
  `Referrer-Policy`, `nosniff`, `no-store`); the live server sends what it sends for its other pages. Machine paths
  (`/api/`, `/mcp`, `/health/`) and every method but GET and HEAD keep their JSON problems. Neither the 404 pages nor the
  maintenance pages are served at their own address, any more than `ssr/` is. A web directory built before them leaves
  every unclaimed path to the unknown route, as before. A new shared table, `tests/Lex.V3.Tests/Fixtures/SitePaths/not-found.json`
  (25 requests), holds both servers to the same choice. The deploy probe now requires a revision to answer an address
  nothing claims (`/fr/no-such-page`) with the release image's own 404 page, byte for byte, with its CSP. The release
  probes' list of served paths leaves both pages out.
- **The failure panel** (`live-failure.mjs`; `FailureFrame` and `OfficialSites` in `LiveAnswer.jsx`) appears on all eight
  screens: search (and the home page and the 404 page, which carry it), reading, dossier, history, compare, radar, export
  and coverage. A failure is:
  - a transport failure: the network, or a problem the API or the live server sent (502 `api_unreachable`, 504
    `api_timeout`, a 500);
  - an answer the page cannot read, which is also what a 5xx without a problem body becomes;
  - a refusal whose code is operational.

  The panel shows, in order:
  - the line, in bold;
  - what the screen already said: its sentence, or the refusal card rendered from the refusal's payload;
  - a "Try again" button, which asks the same request once more. The sessions gained `retry()`. Coverage asks again
    through its effect. A page the server rendered from a failure asks the request its address names.
  - the publishers' official sites: Legilux "for Luxembourg law" and EUR-Lex "for EU law", their home pages.

  These are the first links a live page makes to another site. They are constants: the server renderer has no `URL`, so
  a test holds each one to the publisher-link policy (`publisherSourceUri`). `data-answer-state` is unchanged, so every
  other check still holds. A refusal about the law (`identifier_unknown`, `no_version_for_date`, …) stays an answer, and
  a request the form will not send stays the reader's to correct.
- **The audit, screen by screen, before the change:** each of the eight screens maps an answer through its own reader
  and said every failure as one sentence. None showed a failure as an empty list, a zero or "no version":
  - a reader that cannot account for an answer turns it into `invalid_envelope` rather than render part of it;
  - the export panel composes only from a successful reading;
  - coverage's evaluation card is the build's, not an answer.

  The gaps were the missing line, retry and official links, and operational refusals laid out as answers. All are
  closed on every screen, and a test holds each screen to each kind of failure.
  Only `no_corpus_mounted` reaches a live page today. `rate_limited` and `upstream_unreachable` are produced by no
  operation (the refusal census's `not_produced`), and `snapshot_unknown` only by `events`, `answer_drift` and
  `as_observed`, which no page asks. The tests render each of the four from the payload the registry names for it.
- **The maintenance page** (`live-maintenance-page.jsx`; `maintenance/en.html` and `maintenance/fr.html`): "Lex is being
  updated", "This site is being updated and cannot answer for the moment. This page is not an answer about the law. Try
  again shortly.", and the official sites. It has the site's header and footer, no script and no form.
- **Wording**, English and French:
  - the failure line: "Lex could not complete this request. This is not an answer about the law." / "Lex n’a pas pu
    mener à bien cette demande. Ce n’est pas une réponse sur le droit.";
  - the retry: "Try again" / "Réessayer";
  - the official sites: "The publishers' official sites:" / "Les sites officiels des éditeurs :", with "Legilux, for
    Luxembourg law" / "Legilux, pour le droit luxembourgeois" and "EUR-Lex, for EU law" / "EUR-Lex, pour le droit de
    l’UE";
  - the 404 page: "Page not found" / "Page introuvable" (title and eyebrow); "There is no page at this address" / "Il n’y
    a aucune page à cette adresse"; "Check the address, or search the texts this site holds below. A page missing here
    says nothing about the law, only about this site's pages." / "Vérifiez l’adresse ou recherchez ci-dessous dans les
    textes que ce site détient. Une page absente ici ne dit rien du droit, seulement des pages de ce site."; "Go to the
    home page" / "Aller à la page d’accueil";
  - the maintenance page: "Maintenance" (title and eyebrow, both languages); "Lex is being updated" / "Lex est en cours
    de mise à jour"; "This site is being updated and cannot answer for the moment. This page is not an answer about the
    law. Try again shortly." / "Ce site est en cours de mise à jour et ne peut pas répondre pour le moment. Cette page
    n’est pas une réponse sur le droit. Réessayez dans quelques instants."
- **French**: written to the review's terms and reviewed in its second addendum of 2026-10-15
  (`C:\lex-v3\lanes\fr-review\review.md`): 14 forms, 1 changed in review (no comma before "ou" in the 404 page's
  introduction), 13 approved. New rows: the reader's request is a "demande", as on the refusal card's retry line on the
  same panel ("requête" stays the request a page sends to the server); complete is "mener à bien"; an answer about the
  law is "une réponse sur le droit", not "réponse juridique"; page not found is "Page introuvable"; being updated is "en
  cours de mise à jour". The header comment of `live-chrome-fr.mjs` names the fourteen entries.
- **Driver decisions** (reversible; also under "Driver decisions (reversible)" below):
  - A page request is told from a machine's by a rule on the method and the path alone: a GET or HEAD whose path is not
    under `/api/`, `/mcp` or `/health/`. No header is read for it. The one header the API reads, Accept-Language, is
    read a second time, only to choose the 404 page's language when the path names none, and the privacy scan
    (`PublicRequestRecordingTests`) now holds it to exactly those two reads, each handed whole to its pure choice.
  - Every state that carries no answer because the request did not complete is a failure, an unreadable answer
    included, and so are the four operational refusals. A refusal about the law, and a request the form will not send,
    are not.
  - The 404 page offers the home page's live search screen, not a form that submits to the search page: a form a
    browser submits puts the phrase into an address, which this site never does.
  - The official links are the two publishers' home pages, not a page per work. A failure has no answer to take a
    work from, and these are the routes the get-help screen already names.
  - The 404 table is a new file beside `site-paths.json`, not rows in it: the permalink slice (#967) reshapes every
    row of that table, and a separate table merges cleanly with it.
- **Not yet**:
  - The maintenance page is built and tested, and nothing serves it. During a deployment Azure keeps the live revision
    serving while the candidate is probed (multiple revision mode), so there is no gap for it to fill except a first
    deployment or a planned outage. Putting a page in front of the app for those needs a front door or a static host,
    which is a hosting change with a cost: the owner's decision, not taken here. (Superseded on 2026-10-27: the release's
    own image serves it as a maintenance revision of the same app, with no hosting change; the screens' gaps section at
    the top.)
  - A `/de/` or `/lb/` path nothing claims gets the 404 page in the language Accept-Language picks between French and
    English, not a `localization_unavailable` page.
  - The retry is pressed in a browser for the search screen only (the journeys' "search retried"). The other screens'
    retries are held by their sessions' tests and by the views.
  - A law's address rendered with no index mounted still answers status 200 with the failure panel; the 503 the
    server-rendering slice listed is not done. A failure status would also put a browser console message on every
    no-mount journey step, which the journeys set aside today only for the 404 page.
- **Evidence**:
  - the web suite: 1,149 of 1,149;
  - `dotnet build Lex.V3.slnx -c Release`: 0 warnings, 0 errors;
  - `Lex.V3.Tests`: 3,181, of which 3,175 passed and 6 were skipped, the new `V3WebRootNotFoundTests` among them;
  - `Lex.V3.Ingest.Tests` filtered to `Census`, `PublicRequestRecordingTests` and `V3ApiReadinessTests`: 48, of which 46
    passed and 2 were skipped;
  - `eng/verify-v3-tree.ps1`: 1,566 tracked paths verified;
  - the journeys against an API published from this branch, over the journey fixture mount: through the live server, 47
    of 47 (32 browser runs and 15 API journeys); by keyboard on the pages the API serves, 47 of 47. Both modes include
    "not found" with the fixture mount and with none, and "search retried, with no mount" (two requests, the second
    the same as the first).
  - Chrome reports the 404 document in its console ("Failed to load resource: the server responded with a status of
    404"). The journey checks the status instead, and sets that one message aside for the 404 step only; a probe with
    the step's flag off shows the message.
- **The review of #970** (an independent Claude subagent): no material findings.
  - It ran its own checks:
    - both servers fuzzed over 58 paths and 7 methods;
    - retry pressed in a browser on the dossier, reading and coverage pages, by pointer and by keyboard;
    - both journey modes, 47 of 47.
  - Two notes are corrected here: the 404 page's language menu, and `as_observed` among `snapshot_unknown`'s operations.
  - Left as noted: the two servers answer a few paths no browser sends differently (`/fr/../x`, `/x/../api/...`,
    `/fr\x`), because the API routes on the raw target and the live server on the parsed path.

## An act held only as first published says so, and Compare stops saying "applied" (sole driver, 2026-10-15)

The plan's item 0.6, "two wording faults fixed now" (the specification of record's R76, R18, R20 and R21). Every
Luxembourg act the site holds is held as the publisher first published it, with no consolidated text, and no page said
so; and the Compare card said "the same version applied on both dates", which R21 never says.
- **What identifies an act held only as published**: every state the index holds of the work is an expression of the
  publisher's Journal officiel legal resource, the work IRI followed by `/jo` (expression `/jo/{language}`; the publisher
  types it `jolux:Act`). A consolidated version is a legal resource of its own, `/consolide/{date}` (`jolux:Consolidation`),
  and the index holds no state of one. Measured on `derive-c`'s Luxembourg index, read-only: 1,730 states of 1,730 works,
  every one `/jo/fr`. The index's own facts name 388 consolidated versions (each with one `dateApplicability`) for 154 of
  those acts, give a `dateNoLongerInForce` for 273, and a `publicationDate` for all 1,730: 29 before 2017, 1,701 since.
- **The platform says it** (`V3CorpusMount.AsPublished.cs`): `as_of`, `evidence_bundle`, `timeline`, `dossier` and `diff`
  carry `as_published`, null for a work whose consolidated state is held. It holds the label, the rule, the act as published
  (`journal_officiel_iri`), `later_version_facts` (the consolidated versions the publisher's facts name under the work's
  `/consolide/`, each with its applicability dates or null, the end-of-force facts verbatim, and one sentence said from
  them alone) and `authenticity` (its case, its sentence, the publication dates verbatim, and the publisher file each listed
  state was read from with the `legalValue` the publisher asserts on it). `dossier` and `evidence_bundle` end their
  not-held list with the row `later_versions`, in the same sentence. The consolidated versions are read by a new index
  query (`SubjectFactsUnder`, one range of the fact table's primary key, held to that plan). REST and MCP answer through
  the one dispatch, and a test holds the MCP dossier's `as_published` byte for byte to the REST one.
- **The pages say it** (`as-published.mjs` reads it and refuses an answer whose sentences are not its facts'; `AsPublished`
  in `LiveAnswer.jsx`): the reading, the dossier (whose state table is the act's timeline) and the export page show the
  label, "Later versions: not held." with the consolidated versions the publisher names and its end-of-force date when
  held, and the authenticity sentence with the act as published printed (no live page links out). The JSON export carries
  `as_published`, every CSV row three columns (`as_published`, `later_versions`, `authenticity`), and the PDF a paragraph.
  Compare's card shows the same block. The journeys expect the label on the dossier, the reading and Compare.
- **Compare**: one state on both dates of an act held only as published (every held Luxembourg act today) is "Both dates
  select the one text held, the act as published, applicable from {date} (publisher); later versions are not held"
  (`AsPublishedSameTextNote`), and the answer and the card then name, from the publisher's facts alone, each consolidated
  version whose applicability date falls after the earlier date and on or before the later one ("applicable from {from}
  to {to} (publisher)") and an end-of-force date that falls there (`later_version_facts_between_dates`); nothing is said of
  whether the text changed. Where the language holds another state too (none in the population), it is "the same text
  held", and says that no consolidated version is held. A work whose consolidated state is held (none today) keeps
  "Both dates fall within the same version, applicable from {date} (publisher)" (`SameVersionNote`). The page says each
  in its own language (`compare.sameTextHeld`,
  `compare.betweenNamed`, `compare.betweenEndOfForce`, `compare.sameVersion`), and the reader refuses a note, or facts
  between the dates, that are not the answer's facts'. The EU form, for when Compare serves EU versions (the plan's item
  2.5), is defined beside it: "Both dates fall within the same consolidated wording state (from {date})". The page's
  English introduction and idle sentence and the French idle sentence no longer say "applied" or "s’appliquaient". There
  was no one vocabulary check, only "in force" tests on a few screens: `assertTimeVocabulary` (`publisher-vocabulary.mjs`)
  now refuses "in force", "en vigueur" and "applied" with every past form of "appliquer", in any spelling (NFC, spaces
  and dashes folded, apostrophes folded), holds every Compare entry in both languages and the card for every census
  comparison (`compare-vocabulary.test.mjs`), and is shown failing on purpose: the old note as a string, as an answer
  (refused by the reader) and as a rendered card, and each variant the review found.
- **The wording**, drafts pending the lawyer's read (the owner's item O4; not legally reviewed):
  - label: "as published; may have been amended since; no consolidated text" / "tel que publié ; peut avoir été modifié
    depuis ; aucun texte consolidé détenu";
  - later versions: "Later versions: not held." / "Versions ultérieures : non détenues.", and when the publisher's record
    names consolidated versions, "The publisher's record names {count} consolidated versions of this act, whose text this
    site does not hold:" / "Les données de l’éditeur nomment {count} versions consolidées de cet acte, dont ce site ne
    détient pas les textes :", each "applicable from {from} to {to} (publisher)" / "applicable du {from} au {to}
    (éditeur)"; end of force: "The publisher's record gives {date} as this act's end-of-force date." / "Les données de
    l’éditeur indiquent le {date} comme date de fin de vigueur de cet acte.";
  - authenticity, published since 1 January 2017: "This text was extracted and normalised by Lex, for information; it is
    not authentic. Since 1 January 2017, only the Journal officiel du Grand-Duché de Luxembourg (Mémorial A) as published
    electronically is authentic (loi du 23 décembre 2016, art. 5). The act as published: {act}." / "Ce texte a été extrait
    et normalisé par Lex, à titre d’information ; il ne fait pas foi. Depuis le 1er janvier 2017, le Journal officiel du
    Grand-Duché de Luxembourg (Mémorial A) publié sous forme électronique fait seul foi (loi du 23 décembre 2016, art. 5).
    L’acte tel que publié : {act}.";
  - published before: "… For an act published before 1 January 2017, the printed Mémorial is authentic, and the
    publisher's PDF is its digital reproduction. …" / "… Pour un acte publié avant le 1er janvier 2017, le Mémorial imprimé
    fait foi, et le PDF de l’éditeur en est la reproduction numérique. …";
  - no single publication date: "… The publisher's record gives no single publication date for this act, so Lex does not
    say which edition is authentic. …" / "… Les données de l’éditeur n’indiquent aucune date de publication unique pour
    cet acte ; Lex n’indique donc pas quelle édition fait foi. …".
- **French**: written to the review's terms and reviewed in its addendum of 2026-10-15 (`C:\lex-v3\lanes\fr-review\review.md`):
  13 forms, 2 changed in review (a plural's "les textes"; the statute's own order, "fait seul foi"), 11 approved. New rows:
  the publisher's record is "les données de l’éditeur", a consolidated version "version consolidée", "(publisher)"
  "(éditeur)", amended "modifié", the end-of-force date "date de fin de vigueur". The repair round's five Compare forms
  (`compare.sameTextHeld`, `compare.sameTextHeldOfSeveral`, `compare.betweenNamed`, `compare.betweenEndOfForce`) were
  reviewed in the same addendum's section for the review of #964 (an independent Claude subagent under the same
  delegation): 3 changed ("le même texte détenu"; "pour cet acte" before the count, so the clause can only be the
  versions'), 2 approved; new rows: select "retenir" ("le texte retenu"), "after X and on or before Y" "après le X et au
  plus tard le Y". The driver took its two points on the English: beside another held state the sentence says "no
  consolidated version is held" ("aucune version consolidée n’est détenue", the reviewer's rendering), and
  `compare.betweenNamed` says "names, for this act, …".
- **Driver decisions** (reversible):
  - The French label says "aucun texte consolidé détenu": without "détenu" it says the act has none, which the index's
    facts contradict for 154 acts. The English "no consolidated text" is the specification's and can be read the same way;
    it goes to the lawyer as a question (O4).
  - The authenticity case is the publisher's `publicationDate` of the act as published, and nothing but the Journal
    officiel itself is called authentic: no file is, and Lex's extraction never is, whatever `legalValue` the publisher
    puts on the file (served verbatim). R20's rule for acts published before 2017 is to be checked against the earlier
    publication rules by the lawyer.
  - Both exports now carry a field named `authenticity` (the EU's, R19; Luxembourg's, R20).
- **Not yet**: search results, the provision history and the radar do not show the label (each links to the reading or
  the dossier, which do); `as_observed`, `status_on`, `provenance`, `classification` and the other answers do not carry
  `as_published`; the pre-V3 timeline legend (`LEGENDS` in `publisher-vocabulary.mjs`, on no live page) still says
  "applied"; the result schemas leave each answer's value open, so the answer and envelope censuses hold the new members.
  Live strings that still say "in force", kept because none says it of an interval and each names a typed publisher fact
  or a published contract name (R21's last clause: entry into force comes only from typed publisher dates, spoken as
  separate values): `status_on`'s reading rule and its member `asserted_in_force_on_date` ("does not say the work was in
  force", of the requested date, read from `dateEntryInForce` and `dateNoLongerInForce`), and the not-held rows of
  `classification` and `browse` ("no in-force status … is served here", the predicate `inForceStatus`); the operation id
  `in_force_on` and its route (its description already says "with a state applicable on a date"); and the EU
  `status_on` refusal's sentence naming the Publications Office's "in force" fact. Renaming a member, an operation or a
  route is a contract change, left to the slice that revisits `status_on`.
- **The review of #964** (each finding reproduced by the reviewer; this is the one repair round):
  - **Compare told a falsehood for an act held only as published.** Every held work has one state, its `/jo` text, so
    any two dates on or after it selected that state and the card said "Both dates fall within the same version", even
    where the publisher's own record names a consolidated version, or an end-of-force date, between the two dates
    (rgd-2020-04-08-a265 on 2020-04-10 and 2020-05-20, where `/consolide/20200511` applies from 2020-05-11; reproduced
    on `derive-c`'s index, read-only: 376 of the 388 named consolidated versions, in 149 acts, start after the held
    state's date, and 269 acts have an end-of-force date after it). Repaired as the driver decided, in **Compare** above:
    `diff` carries `as_published` and `later_version_facts_between_dates`, the note says "Both dates select the one text
    held, the act as published, applicable from {date} (publisher); later versions are not held" and names the versions
    and the end-of-force date dated after the earlier date and on or before the later one, the card shows the
    as-published block and names each, and the reader refuses a note or facts between the dates that are not the
    answer's. An index fixture reproduces the straddling case (`V3CorpusDiffMountTests`), and the answer census holds a
    real-handler comparison across dated versions.
  - **The R21 check let French and Unicode variants through** ("s’y appliquait", "qui s’y appliquaient", "appliquait"
    and "appliquaient" without "s’", "in force", "en vigueur", a decomposed "appliquée", "sʼappliquait"
    with U+02BC, "in-force"). `assertTimeVocabulary` now reads the text composed to NFC, its format characters taken out,
    its runs of spaces and dashes folded to one space and its apostrophes to one, and refuses every past form of
    "appliquer" as a word on its own (`(?<!\p{L})appliqu(?:é|ée|és|ées|ai|ais|ait|aient|a|as|âmes|âtes|èrent)(?!\p{L})`).
    R21 forbids "in force", "en vigueur" and "applied" on an interval and prescribes "applicable", so "applicable" and the
    present "s’applique", "applies", "applying" stay allowed (`compare.sideNext` and the refusal sentences say "s’applique
    à partir du" and "s’appliquent à cette date" of a date asked). Every probe is in `compare-vocabulary.test.mjs`; all
    373 entries of each interface table, the 150 strings of the refusal sentences and the 15 of the localisation table
    pass the stricter rule.
  - **Live MCP tool descriptions said "applied" of a date**: `as_of` now says "applicable on a date (publisher)", and
    `as_observed` "applicable on a date (publisher) as one build of the mounted log held it"; `status_on` names the
    publisher's force facts by their predicates. No census or golden pins `tools/list`; a new test
    (`NoToolDescriptionSaysAppliedInForceOrEnVigueurOfADate`) holds every description to the same check. The grep of
    `src/` and `web/` found no other live string saying "applied", "in force" or "en vigueur" of an interval; the
    strings kept are in **Not yet**.
  - **The tree count** said 1,509; it is 1,510 at this head.
  - **The journeys' CI failure** (before the review): the export step's page was 458 px wide at 320 px, from the export's
    as-published paragraph and the block's long addresses; repaired before review by wrapping them (`overflow-wrap:
    anywhere` on `[data-as-published] p`, `[data-as-published] li` and `[data-export-as-published]`, held by a test), and
    Compare's new list of versions wraps the same way (`[data-between-dates]`).
- **Measured**: at the slice's head, web suite 1,119 of 1,119; the solution built in Release with no warning;
  `Lex.V3.Tests` 3,156 passed, 1 skipped; the whole `Lex.V3.Ingest.Tests` 2,987 passed, 26 skipped (the live canaries and
  opt-in runs), 0 failed, in 43 minutes. At the repair's head: web suite 1,121 of 1,121 (`compare-vocabulary.test.mjs` 5,
  `compare-answer.test.mjs` 4, `as-published.test.mjs` 7); the solution built in Release, 0 warnings, 0 errors;
  `Lex.V3.Tests` 3,156 passed, 1 skipped; the `Lex.V3.Ingest.Tests` classes touched and the census, recording, sample,
  MCP and diff classes 176 passed, 2 skipped (two publisher canaries), 0 failed (`V3CorpusDiffMountTests` 14,
  `V3McpJsonRpcTests` 20, `V3CorpusAsPublishedMountTests` 5 among them); the whole Ingest suite was not run again; the
  answer and envelope censuses rendered from the real handler and verified without the render switch; the browser
  journeys on the fixture mount 44 of 44, and again with `--keyboard --served-by-api` 44 of 44 (headless Chrome, the API
  published in Release); `eng/verify-v3-tree.ps1` over 1,510 tracked paths.
- **After 0.8 merged** (#966): its `image-conditions` lane, which runs without the language library, failed the new MCP
  vocabulary test on one probe, a decomposed accent that only the library composes. That probe now runs only where the
  process can compose; the precomposed form and every tool description are checked in the lane too.

## The web suite's race: one bundle every live build shared (sole driver, 2026-10-15)

The web suite failed now and then on one of the live-page tests ("renderLiveLocaleUnavailablePage is not a function"),
and passed when that file was run alone. It happened four times on 15 October, on four branches.
- **The cause:** four test files run five builds of the live pages at once, in their own processes, each build with
  its own tag for its intermediate bundles. One bundle, the "language not available" page's, was written without the
  tag, so every build rewrote the same `live-locale-page.mjs`. A test that imported it mid-write found it empty.
- **The fix:** that bundle carries its build's tag too (`build-live.mjs`). A test holds every bundle call in the live build
  to a tagged name, and it fails on the old code, naming the untagged line.
- **Measured:** the whole web suite three times in a row, 1,110 of 1,110 each.
- **The review of #968** (an independent Claude subagent): no material findings. It reproduced the race on its own:
  five processes bundling to one name lost between 49 and 63 of 200 imports each, and none with a name per process.
  It corrected one count (four test files, five builds). Its sturdier option is not taken here: naming every output
  through one helper, or a folder per build. The test reads the build's source line by line, so a new untagged call
  written without `await`, or through another name, would pass it.
- **Also:** 0.8's `image-conditions` lane is now a required check of `v3/integration` (a repository setting, made on
  2026-10-15 after #966 merged; it runs on every pull request to the branch).

## The image's language data: accents and "today" without the operating system's (sole driver, 2026-10-15)

The plan's item 0.8, a driver decision reported to the owner: the image stays small. The release image
(`aspnet:10.0-noble-chiseled`) carries no language library (ICU) and no zone database: its own environment runs .NET
globalization-invariant, where `string.Normalize` returns its input unchanged, and with no zone files .NET knows UTC
alone, silently. So the deployed revision never found an accented title ("congé parental" found nothing): the title fold
asked the library, the server stopped removing the accents from what a reader typed, and every index, built on the
laptop with the library, holds its titles without them. On the release index (`derive-c`), 2,000 of its 2,021 titles
typed exactly as published found nothing without the library.
- **Accents folded by Lex's own table** (`TitleFolding`, in Contracts): the index's `normalized_title` column and every
  title a reader types are folded by one table, the 2,140 letters and digits of the Basic Multilingual Plane whose fold
  is not themselves, read from the library letter by letter: lowered, the canonical decomposition's non-spacing marks
  dropped (œ, æ, ß, ø, ł keep their letter, as the library kept them). `LuxembourgIndexBuilder.NormalizeTitle` delegates to
  it, so `resolve` and `search`'s title step fold the same with the library or without it, and nothing on that title
  path asks the library any more. The index's format is unchanged: on the release index (read only), the table folds
  all 2,021 titles to the stored column byte for byte, with the library and without it, and "congé parental" finds its 5
  works in both. A test holds the table to the library wherever one is present, every character of the plane; three
  differences are by design (U+FFFE and a lone surrogate, which made the library throw, separate words, as does a
  character beyond the plane; Hangul typed as separate jamo stays as typed).
- **"Today" in Luxembourg by a coded EU rule** (`LuxembourgCalendar`, in Contracts; its twin `luxembourgDate` in
  `web/scripts/temporal.mjs`): UTC+01:00, and UTC+02:00 from 01:00 UTC on the last Sunday of March to 01:00 UTC on the last
  Sunday of October (Directive 2000/84/EC, Articles 2 and 3), held from 1996 to 2099; no zone data, no `TimeZoneInfo`. One
  table (`tests/Lex.V3.Tests/Fixtures/LuxembourgToday/luxembourg-today.json`, written from the IANA zone database through
  Intl) holds both rules to the same dates and offsets at 191 instants, around every clock change and local midnight from
  2026 to 2040 and at the range's edges, and to 17 years of change instants; the web's test holds every row to the IANA
  zone database, and each side sweeps its host's zone database where there is one (the API's every hour from 1996 to 2099,
  the web's each night at 22:30 UTC and at each change), so a change of the rule fails CI. Nothing computes "today" yet:
  printing it everywhere is item 3.4, on this rule.
- **CI's `image-conditions` lane** (`v3-ci.yml`), beside `dotnet`: it builds as `dotnet` does, then runs the tests inside
  the pinned base image itself (its runtime 10.0.11, its environment and user; the checkout read-only, `/tmp` a tmpfs, no
  network): `Lex.V3.Tests` whole but the OCI verifier's and the SDK publish's classes (tools the image does not carry),
  then the request-time `Lex.V3.Ingest.Tests` classes (the mounts the API serves and its answers from them, Luxembourg's
  and the EU's, and the image-only annex control case). The two filters are written once
  (`tests/Lex.V3.TestSupport/image-conditions-lane-filters.json`), and each test app holds every term of its own to a
  test it has (`ImageConditionsLaneTests`). A canary (`ImageConditionsTests`, with `LEX_IMAGE_CONDITIONS=1`) asserts that
  the conditions held: no named culture, a normalization that does nothing, no Luxembourg zone, a local clock on UTC, UTC
  the only zone. The eight tests whose premise needs the library (switching to tr-TR, fr-FR or ar-SA, a culture's order,
  NFC composition) end inconclusive where there is none, saying so. `eng/test-fast.ps1 -ImageConditions` runs the lane's
  two runs on any machine, with the lane's filters, under two switches (the language library and the zone database taken
  away); the second, the Ingest classes, is where the title fault shows, and takes minutes. The API project sets
  `InvariantGlobalization` and `InvariantTimezone`, so every run of the API (the journeys', serve-live's) runs as the
  image does.
- **The deploy probe** (`deploy-probe.mjs`) adds two checks. An accented title the revision holds, chosen from its own
  answers (a work its search for "loi" lists; the first accented title of its dossier that resolves without its accents),
  must be found as published, in capitals and decomposed, by the same match, and a strict search for it must name that
  work alone; the deployed release fails exactly this. And the revision's clock: the instant `coverage` was observed at
  must stand within a minute of the probe's own; the probe says the revision's date in Luxembourg by the coded rule,
  whether the instant fell in the hour whose date the season's offset decides (22:00 to 23:00 UTC), and the next clock
  change. A revision answers only at its own instant, so the clock changes themselves are held where an instant can be
  chosen, the shared table in CI; once 3.4 writes "today" into answers, the probe checks it is the rule's date for the
  same answer's instant.
- **Measured**: on the release index (`derive-c`, read only), by a probe that folds a typed title as `resolve` does and
  looks it up: inside the release's own base layers at this branch's base (runtime 10.0.11, no ICU, no zone files),
  "congé parental" found nothing and 21 of the 2,021 titles typed as published found their work; with the table, on this
  machine with the library and without it, all 2,021 fold to the stored column and find their work, "congé parental" finds
  its 5 works and "Code pénal" its 10. The real first mount's title (`TheRealMountsTitleIsFoundHoweverItsAccentsAreTyped`):
  3 of its 5 forms refused without the library before the table, 5 of 5 found after, with the library and without. The
  lane, emulated in WSL at this branch's head (built on Linux as the lane builds, then run in the pinned base's own layers
  with its environment, as user 1654, in `/src` read-only, `/tmp` a tmpfs, loopback alone, the lane's arguments):
  `Lex.V3.Tests` 3,133 (3,123 passed; 10 inconclusive, the eight, the zone sweep and the table-to-library test; the
  canary's five passed) and the request-time `Lex.V3.Ingest.Tests` classes 645 (642 passed; 3 inconclusive, which need a
  named mount or chain; the EU capability, time-view and branch tests and the annex control case among the passed); at
  the base the lane's run then failed 8 and 7 (the eight; the title ladder's ambiguity, prefix and all-words tiers, the
  refusal census's "a title two works carry", and the machine gates' refusal set at 0.944, twice). On Windows: the whole
  solution 6,181 (6,149 passed, 32 inconclusive); `eng/test-fast.ps1 -ImageConditions` 3,133 (3,122 passed, 11
  inconclusive) and 645 (642 passed, 3 inconclusive), 26 minutes; the web suite 1,106 of 1,106.
- **The renderer in the image, after the rebase** (the plan's last line of 0.8): rebased on #965, the image rehearsal built
  the image from this branch, whose API runs without the language library and zone data as the image does, and ran it in
  WSL with its browser probes over the journey's fixture mount: 12 of 12 steps (the address, permalink and stale pages
  rendered by V8 in the image), 215 citations verified, V2 absent at its 63 routes, the readiness and preview check
  clean, the release read back. The rebase also combines the census partition's candidates (616: one from #959, one from
  this change).
- **Not yet** (driver decisions, reversible): the accented-title check in the image rehearsal's real-mount probes; a live look
  at the date during a real clock change (monitoring at 22:30 UTC on 2026-10-24 and 25, 2027-03-27 and 28, not a deploy
  gate); "today" printed (3.4); full-text matching that ignores accents (4.4, which can fold with this table at the
  index's next format); the one place the API still asks the
  library to normalize (the only `string.Normalize` in `src` the API reaches; no culture-sensitive call there either),
  the synthetic slice's check at startup (`SyntheticSliceArtifactVerifier`, through `SyntheticTextNormalizer`), which
  composes the source into NFC and compares it with the derived text: without the library, in the image and, since the
  API sets `InvariantGlobalization`, in every run of the API, that composition does nothing, so a synthetic source
  holding a decomposed accent, derived where the library is, would be refused at startup (`DerivedContentMismatch`) and
  the API would serve the slice as unavailable. The bundled source is ASCII, so nothing fails today, and the test that
  would show it (`DecomposedUnicodeBecomesNfc`) ends inconclusive in the lane; a derive-time refusal of a source that is
  not NFC, checked without the library, would close it.
- **The review** (before the pull request): six minor findings, two of them the same, all repaired in one round.
  - The web's date rule read the years 0096 to 0099 as 1996 to 1999 (`Date.UTC` adds 1900 to a year below 100) and
    answered a date where the API's rule refuses (0099-12-31T23:30:00Z gave 2000-01-01): the year is now checked as
    written, before `Date.UTC`, and the shared table refuses 0099-12-31T23:30:00Z and 0096-01-01T00:00:00Z, which both
    rules' tests hold (the old twin fails the web's).
  - The lane's Ingest filter named the annex control case by a word no test's name holds, so the image-only annex never
    ran; two of its exclusions named no class; and excluding `V3FirstMountBuildTests` also dropped the EU capability and
    time-view tests, request-time answers kept in its partial files. Those three are now classes of their own (as this
    file already named them), the annex term names its test, the dead exclusions are gone, the two filters are written
    once, and each test app holds every term of its filter to a test it has (the old filter fails that with exactly its
    three bad terms). The lane emulation runs 13 more tests, all passing.
  - `eng/test-fast.ps1 -ImageConditions` was said to reproduce the lane but ran `Lex.V3.Tests` alone, which no title test
    reaches, so it stayed green with the deployed title fault put back (found twice). It now runs both of the lane's runs
    with the lane's filters; with the base's title fold put back it fails (exit 2): `Lex.V3.Tests` passes as before, and
    10 of the 645 Ingest tests fail (the real mount's title in three forms, the ladder's prefix, ambiguity and all-words
    tiers, the plural title, the refusal census, the machine gates' refusal set and the evaluation card).
  - This section said nothing in the API asks the library any more; the synthetic slice's check at startup still does.
    The sentence now names the title path, and the check is under "Not yet".
  - The base's lane run was said to fail 9 `Lex.V3.Tests`: with the lane's own filter it fails 8, the eight (the SDK
    publish's class is outside the filter; the 9 came from an earlier run that excluded the OCI verifier alone).

## The server ships no synthetic preview, and says whether it holds the law (sole driver, 2026-10-15)

The plan's item 0.7 (`C:\lex-v3\PLAN-2026-10-14.md`). The one-server image carried the S0-05 synthetic preview and its
trust key, and its server started them: `/health/ready` answered 204 from the preview whether or not any law was
mounted, `/api/v3-preview/resolve` answered a synthetic text "with no legal authority", and every path nothing claimed
answered the preview's own "not found". The Azure readiness probe asked `/evaluation-card.json`, a static file, so a
replica whose mount was refused at startup still took traffic.
- **The release image ships neither the preview nor its key.** The build that names a corpus mount (the image
  rehearsal's, so every release) publishes no `preview-graph/` and no `preview-trust/` (`Lex.V3.Api.csproj`), and the
  server starts the preview only when its graph is there (`Program.cs`). Test builds and the S0-05 image keep both, so
  the preview's tests and the S0-05 lane's reproducible receipt run as before.
- **Readiness is whether the server holds the law** (`V3ApiHandler`, with no preview): `GET` or `HEAD /health/ready`
  answers 204 with a mount and 503 "No law is mounted." without one, never cached; any other method is 405. The Azure
  readiness probe now asks it (`deploy/main.bicep`), so a replica without the law takes no traffic, and monitoring can
  ask the same route.
- **Every path nothing claims is an unknown route** (the platform's `unknown_route` problem, as under `/api/v3/`),
  the preview's routes included; nothing names the preview or anything synthetic. The site's own 404 page is item 6.9.
- **Checked where it ships**: the image check fails an image whose app layer holds a preview file
  (`imageFailures`), and the image run and the deploy probe ask the server for itself (`serverFailures`): readiness
  204 and uncached, and the preview's route the 404 of an unknown route, naming nothing synthetic.
- **Evidence**:
  - the image rehearsal over the journey's fixture mount, with the WSL probes and without the second build: the image
    check passed with no preview file, all 12 browser probe steps passed (215 citations verified), V2 was absent (546
    entries scanned, 63 routes asked), and the server answered readiness 204 and the preview's route as unknown;
  - a publish naming a mount holds no `preview-graph` or `preview-trust`, and one without a mount still holds both;
  - `Lex.V3.Tests` 3,156 passed and 1 skipped; `Lex.V3.Ingest.Tests` 2,984 passed and 26 skipped (the new
    `V3ApiReadinessTests` among them); the web suite 1,109 of 1,109;
  - `deploy/validate.ps1` (main.json is main.bicep's build, lint clean); `eng/verify-v3-tree.ps1` 1,505 paths.
  - The first full web run failed one test this change caused: the image-reproducibility fixture's long name lay under
    `app/preview-graph/`, which the new check rightly rejects. It now lies under `app/runtimes/`. A paint test failed
    under load and passed alone.
- **The review of #963** (an independent Claude subagent, which ran the release server from a publish with a mount,
  with none and with a corrupt one, and `serverFailures` against each). One finding, repaired:
  - With readiness now honest, a candidate whose mount is refused never becomes ready. Azure then fails the deployment
    itself at its progress deadline, so `deploy.ps1` stopped at the deployment step. Its removal step ran only after a
    failed probe, so the candidate stayed active and billed. The traced path is exact; Azure's deadline behaviour is as
    Microsoft documents it, not exercised here.
  - The deployment step now catches that failure, deactivates the candidate if it is active, and stops, saying why. A
    static rule in `deploy-kit.test.mjs` holds it, and fails when the deactivation is taken out.
  - Noted: the signed release of 13 October still ships the preview, and the deploy probe now fails it on the preview's
    route. The next deployment needs a release built after this change.

## The private copy stays private on every deployment (sole driver, 2026-10-14)

The plan's item 0.5 (`C:\lex-v3\PLAN-2026-10-14.md`; its feasibility review found it): the deploy kit restricted ingress
to the owner's address on a first deployment only (`firstDeployment && !empty(probeSourceCidr)` in `deploy/main.bicep`),
so the next deployment, the release rehearsal's included, would have emptied the list and opened the private copy to
everyone.
- **Every deployment names the address ranges the private copy admits** (`deploy.ps1 -AllowedCidr`, the operator's
  address first): the template applies them on every deployment (`allowedSourceCidrs`), and a deployment that names
  none admits no one (one rule for a never-routed TEST-NET-1 address), so a slip fails closed, not open. The script
  refuses a deployment with no range, and a range that is not an IPv4 address with its prefix length.
- **Who ingress admits is read back after each deployment**, before the probe: exactly the named ranges, each `Allow`.
  If not, the script sets them again, reads them back once more, and stops if they still do not hold.
- **The opening is the owner's own step** (`deploy.ps1 -Open`, a parameter set of its own, never part of a
  deployment): it removes every address rule. Every deployment after it says `-Public` (`openToPublic`), so none
  restricts the site again by mistake; the script refuses `-Public` together with ranges.
- **The review of #960** (a review workflow: three independent reviewers, exposure, template and script semantics, and
  operations, each finding then put to a skeptic who tried to refute it; 7 confirmed, 5 refuted as not material). The
  confirmed findings, all repaired:
  - the opening could say "the site is open" after removing nothing (its listing's failure was never checked, and
    nothing read back): `-Open` now stops when it cannot list the rules, and says the site is open only after reading
    back that ingress admits everyone;
  - two ranges could not be named in the documented `pwsh -File` form: each value is split on commas;
  - any prefix was accepted, so `0.0.0.0/0` would have deployed as "the site stays private": a range must be /24 to
    /32;
  - the kit's test did not hold the read-back or the opening: the checks now live in `deploy/private-copy.ps1` and the
    web suite runs them under pwsh (exact ranges, an extra, a missing and a deny rule, `null` and `[]`; the opening
    with a failing listing and with a rule left), with the read-back's place under `-Apply` held too;
  - the owner's runbook in this file still named `-ProbeSourceCidr`: it now names `-AllowedCidr`, `-Open` and
    `-Public`.
- **Not yet** (driver decisions, reversible): a probe from an address outside the list needs a vantage point off this
  machine, such as a scheduled GitHub check against the private copy, and GitHub runs scheduled and dispatched
  workflows only from the default branch, which is still the legacy line until the branch switch; until then the
  read-back of the app's own configuration stands for it. The operator's current address is pinned again once the
  owner's Azure sign-in works.
- **Measured**: `deploy/validate.ps1` (`main.json` is `main.bicep`'s build, lint clean, the script parses); web suite 1,109 of 1,109 on #959's merge (`deploy-kit.test.mjs` 11 of 11, three of them running the checks under pwsh); plan runs of `deploy.ps1 -File` against release v3-20261013T214036Z-9a5f05d3d1b4: refused with no range, with `-Public` and a range, with `203.0.113.7` and with `0.0.0.0/0`; `-AllowedCidr 203.0.113.7/32,198.51.100.0/24` names both ranges as the template's parameter.

## A law's page arrives whole: the server renders it (sole driver, 2026-10-14)

The specification of record's R67: "Dossier, Reading, Provision history and Coverage pages are complete HTML from the
server, with title, description, canonical link and authenticity banner. They are readable without JavaScript, then
enhanced." A law's address served the page's empty form, which then asked the API: without scripts a reader saw no law,
and every visit waited for a second round trip before any text.
- **A law's address under a language is rendered from the API's own answer**: the dossier at `/{fr,en}/lu-legilux/{work}`
  and `/{fr,en}/eu-eurlex/{celex}`, the reading at a state's or an EU wording's permalink. The server asks itself what
  the page would ask (the same operation and parameters: `sitePageFor` in `permalinks.mjs`, `SitePageFor` in
  `V3WebRoot`, both held to the shared table), renders the page from that envelope with the pages' own React tree
  (`app/ssr-entry.jsx`), and writes the envelope beside the root as an inert JSON block (`initial-answer.mjs`; `<`, `>`,
  `&`, U+2028 and U+2029 escaped). The browser hydrates the same tree from it, so the page asks the API nothing as it
  loads. A work the API does not know is a 404 with its refusal on the page; with no corpus mounted, the page shows the
  refusal every request gets.
- **In the API, V8 runs the renderer** (`V3PageRenderer`, Microsoft.ClearScript 7.5.1.1 with its Linux and Windows
  natives): one engine per interface language, holding the built renderer (`ssr/{language}.js`, which neither server
  serves) and nothing of the process (no host object, file or network); one render at a time per language. A render is
  bounded at 5 seconds, at a soft heap limit of 224 MB that ClearScript samples (V8's hard limit of 320 MB, which would
  end the process, doubles on demand instead) and at 512 KB of stack; one stopped for its time or its heap retires its
  engine, and the next render makes a fresh one. A render that waits more than 2 seconds for its engine is not
  attempted. Whatever fails, the page is served as built and asks for itself. Measured on the built renderer: a reading
  of 3,000 articles, as large as the largest codes (a 12.9 MB page, half of it the envelope), renders in 110 to 170 ms
  within 64 MB of heap; one of 120 articles, the largest state the population mount holds, in under 20 ms.
- **The live server renders the same way** (`serve-live.mjs`, a Node `vm` context per language), so the journeys walk
  server-rendered pages on both servers.
- **Canonical and hreflang at a law's address**: the page names itself, in its language, as canonical, names the same
  address in each language, and the address with no language (which sends a reader to theirs) as `x-default`.
- **The journeys hold it**: the address, permalink and stale-permalink steps must arrive carrying the answer, make no
  API request as they load, and show the same answer state in the page's own bytes, read with no script run. A page
  that never loaded (the browser's error page) is now one named failure; it was a crash of the run.
- **The review of #959** (independent Claude subagent; it drove the real `V3PageRenderer` with the real bundle: 2,940
  articles in 310 ms, 12,250 in 886 ms, and 49,000 (a 117 MB answer) stopped by the soft heap limit with the process
  standing and the next render on a fresh engine; 60 concurrent law pages, none failed): two minor findings, repaired.
  - A host where V8 cannot be made (its native library missing for the platform) answered every law page with a 500:
    now any failure to make an engine, or to load the renderer within a render's time, leaves the language unrendered
    for the process's life, and the page is served as built (tested through a seam that makes V8 fail, and with a
    renderer that never finishes loading); anything else that fails in a render is no page either.
  - On Windows and macOS, whose file systems ignore case, the live server served the renderer at `/SSR/en.js`: its
    guard now compares the first segment without case (tested with `/SSR/` and `/Ssr/`).
  - Adopted from its notes: a stale permalink's page names the state it shows as canonical and in its alternates,
    never the digest it was opened at; three comments that described a no-corpus case the code does not have.
  - The API's privacy test pins the public process's packages: ClearScript (and Newtonsoft.Json, which it brings and the
    API does not call) is now named there, as a script engine that holds nothing of the process.
- **The renderer in the release image** (the plan's item 0.8 asks it before this merges): the image rehearsal built the
  image from this branch and ran it in WSL as the release runs it, with its browser probes over the fixture mount. The
  first run found a phone-layout fault outside this change: on Trust and Coverage the evaluation card a release carries
  names its mount by the corpus file's digest (`sha256:` and 64 hex, one word), and at 320 CSS pixels the page was 668
  pixels wide (the journeys' card names no digest there, so they never saw it). The card's paragraphs and list items
  now wrap a long token, held by a test. The second run passed every probe: 12 of 12, the address, permalink and stale
  steps arriving rendered by V8 in the image with no API request; V2 absent at its 63 routes; the card at its route;
  the release read back.
- **Not yet** (driver decisions, reversible): the provision history and coverage pages; each law's title and description
  in the page's head; R67's indexing half (schema.org Legislation markup, robots.txt, the sitemap, noindex on search);
  the address that resolves to a work's current version; a 503 for `no_corpus_mounted`, with the maintenance page
  (R60); a journey that opens an EU law's address with a real EU answer.
- **Measured**: web suite 1,104 of 1,104; `Lex.V3.Tests` 3,156 passed, 1 skipped (`V3PageRendererTests` 7, `V3WebRootTests` 11); in `Lex.V3.Ingest.Tests`, the privacy scan, the identifier and the census tests 72 passed, 3 skipped (CI runs the whole solution); the image rehearsal with its probes, as above, 12 of 12; before the review's repair, the journeys against an API published from this branch: the fixture mount through the live server 44 of 44 and served by the API with the keyboard 44 of 44 (each of the six law-address steps with no API request), the licence-blocked mount 12 of 12, the two-state mount 2 of 2, the EU annex control mount 3 of 3.

## Every page under its language: /fr/ and /en/ (sole driver, 2026-10-14)

The specification of record's R61: "Page URLs carry the interface language (/fr/, /en/), with hreflang. The start
language follows the browser's preference between the two, else French." English stood at the root and French under
`/fr/`, so an English page's address named no language and a reader with a French browser landed in English.
- **Every page stands under its language**: the build writes the English pages and their scripts under `en/`, beside
  `fr/` (`build-live.mjs`, `livePath`); the root keeps the shared assets (styles, fonts, icon, the evaluation card) and
  the German and Luxembourgish `localization_unavailable` pages. Each language's root (`/en/`, `/fr/`) is its home page
  on both servers.
- **The root and the API's own addresses go to the reader's language**: `/` (and `/index.html`), and a law's address as
  the API mints it with no language (`/lu-legilux/{work}/{date}--{sha256}`, `/eu-eurlex/{celex}`), answer a 302 to the
  same address under the language the browser prefers of French and English (`Accept-Language`: highest weight, the
  first listed on a tie, a weight of 0 refusing), else French; the query goes along, a browser keeps the `#article`, and
  the answer says `Vary: Accept-Language` so no cache hands one reader's language to another
  (`negotiateLanguage`/`redirectFor` in `permalinks.mjs`, `NegotiateLanguage`/`RedirectFor` in `V3WebRoot`). The shared
  table now holds 56 paths (13 served, 8 redirected) and 15 `Accept-Language` cases, which the API and the web are both
  held to.
- **hreflang**: each live page names itself in both languages (`https://law.soufien.lu/...`), and as `x-default` the
  home page's root (which sends a reader to their language) or, for any other page, its French page; the reading and
  dossier pages name none, as either may stand at a law's address the page file does not name (server rendering gives
  them their own).
- **The language menu keeps the reader on the same document**: built, its links lead to each language's home; once a
  page has loaded, they lead to the same page, the same law's address and the same article in the other language
  (`sameDocumentIn`).
- **The release probes follow**: the V2-route check now requires V3's exact redirect at `/`, `/lu-legilux/{work}` and
  `/eu-eurlex/{celex}` (a V2 page there, or a redirect V3 would not send, fails); the deploy probe requires `/` to send a
  reader naming no language to `/fr/`, with `Vary`, and `/fr/` to be the image's own page with its headers.
- **The journeys walk the English pages under `/en/`**, through the live server and the API.
- **The review of #958** (independent Claude subagent; differential run of both implementations over 4,101
  `Accept-Language` values and 3,483 path, query and language combinations): one blocker, repaired. The pull request's
  own CI was red: the API's privacy scan (S4-A11) trips on any read of a request header, and the redirect reads
  `Accept-Language`. The scan now lets that one read through, by name, and a test of its own holds it to exactly one,
  in `V3WebRoot`, handed straight to `RedirectFor`; any other header still trips it. The evidence now covers both test
  projects, as CI runs them. Five minor findings, all repaired:
  - a refusal card's candidate links (an ambiguous date's states) now stand under the page's language, as every other
    permalink link does (the English and French bundles tested);
  - the API reads a page's address under `/fr/` or `/en/` as the law the page shows (`dossier`, `verify`, Luxembourg
    and EU permalinks), so a reader who pastes a page's address into a form gets that law, not `identifier_unknown`;
    the permalinks it mints still name no language (R64);
  - hreflang and the language menu are tested: each built page's alternates, whole, to pages the build wrote; and the
    journeys check, on every page once loaded, that the menu leads to the same page in each language with its `#article`;
  - comments still describing pages at the root are corrected, and the root's redirect now keeps the query as the
    others do;
  - both `Accept-Language` readers trim only spaces and tabs, as HTTP says (JavaScript's and .NET's own trims disagreed
    on U+0085 and U+FEFF); the shared table holds 19 language cases.
- **Not yet** (driver decisions, reversible): the German and Luxembourgish pages still say `localization_unavailable`
  in English only (R61 asks for French and English: new French, so a French review); a journey that starts at `/`.
- **Measured**: web suite 1,094 of 1,094; the whole solution as CI runs it, 6,156 tests: 6,129 passed, 27 skipped (`Lex.V3.Tests` and `Lex.V3.Ingest.Tests`, the privacy scan's new test included); `eng/verify-v3-tree.ps1` 1,497 tracked paths. Before the review's repair, the journeys under `/en/` against an API published from this branch: the fixture mount through the live server 44 of 44 and served by the API with the keyboard 44 of 44, the licence-blocked mount 12 of 12, the two-state mount 2 of 2, the EU annex control mount 3 of 3; CI runs them again on the repaired head, with the language menu now checked on every page.

## Every answer fits a small phone; every permalink opens its page (sole driver, 2026-10-14)

The specification of record asks for WCAG 2.2 AA with layouts at 320, 768 and 1280 pixels (R62), and #955 measured only
the pages before an answer. The journeys now measure every page after its answer:
- **Reflow at 320 CSS pixels** (WCAG 2.2, 1.4.10): each journey step, once its page has answered, lays the page out again
  in a window 320 pixels wide and fails if the page scrolls sideways, naming what stands out past the window
  (`REFLOW_WIDTH`, measured last, so nothing else is read at that width). A table may scroll inside its own region.
  Measured first over the five journey configurations: three tables failed, the dossier's states (411 pixels in a
  305-pixel window), the EU dossier's expressions (301) and the provision history (413).
- **The three tables scroll in a region of their own** (`TableRegion`): in the tab order, so a keyboard can scroll it,
  and named by the sentence above the table (`aria-labelledby`), so no new words, and no new French, were needed.
- **Every printed permalink is a link** (`Permalink`, `isSiteAddress`): history, compare and the changes radar now
  link theirs, as the reading, dossier and search pages do since #956 (and the search page's list of candidate states);
  a permalink this site serves no page at stays text, so no link leads nowhere. The export composer is the exception:
  its pins last only while the page is open, so its permalinks stay text (since #956 its quotes' permalinks were links,
  through the reading's shared evidence line; no longer).
- **The notes of #956's review** (independent Claude subagent: merge, no material finding): the pin notice's spacing
  tokens had no fallback, so it rendered with no padding (fixed); an `#article` the pinned state does not hold now
  brings the state into view; the Luxembourg work pattern now matches the API's `IsWorkKey` (lower case), so
  `/lu-legilux/LOI-1` opens no page (the shared table has 44 paths). Left for later: an EU address and permalink step
  in the EU annex journey (the reviewer walked the EU path by hand on the annex and population mounts).
- **The review of #957** (independent Claude subagent): changes requested, repaired in one round. The export
  composer's JSON, once opened ("The JSON as it will be saved"), made the page 9,518 pixels wide at 320 and wider than
  the window at 768 and 1280, because the check measured with every disclosure closed: the JSON now wraps, and the check
  opens every disclosure in the answer before it measures, fails when the measurement was not taken at 320, and names a
  box whose own content overflows it. This file's first account said the export composer links its permalinks and that
  the API's code was unchanged; neither was true.
- **Measured**: web suite 1,091 of 1,091; the journeys against the API published from this branch's C# (the work pattern in `V3WebRoot` changed here), each page measured at 320 CSS pixels with every disclosure in its answer open: the fixture mount through the live server 44 of 44 and served by the API with the keyboard 44 of 44, the licence-blocked mount 12 of 12, the two-state mount 2 of 2, the EU annex control mount 3 of 3 by both servers.

## Every law address opens its page; search results open the text (sole driver, 2026-10-14)

The site printed permalinks and served nothing at them, and a search hit led nowhere (the review of #955: screens nothing
linked to). The UX specification's URLs (section 1: a dossier at `/{publisher}/{work}`, a reading at
`/{publisher}/{work}/{valid_from}--{hash}`, the article after `#`) are now the site's own addresses:
- **The addresses** (`web/scripts/permalinks.mjs`): a Luxembourg state's permalink
  `/lu-legilux/{work}/{date}--{sha256}` and an EU wording's `/eu-eurlex/{celex}/{lng}/{date}--{sha256}` open the reading
  page; a work's address, `/lu-legilux/{work}` or `/eu-eurlex/{celex}`, opens its dossier; each also under `/fr/`. The
  API (`V3WebRoot.PageForSitePath`) and the live server serve the page, and claim no other path. One table of 40 paths
  (13 served, 27 not claimed, `tests/Lex.V3.Tests/Fixtures/SitePaths/site-paths.json`) holds the API's pattern and the
  web's to the same answers.
- **The pages read their address.** The reading page asks the state's reading as it loads (one request, the one a submit
  would send), fills the form with it, and brings into view what the address names: the article after `#`, in the
  pinned state, or the state. The dossier page asks the work's dossier. An EU act is asked by its CELEX, which the EU
  index resolves (`/eu-eurlex/{celex}` is not an identifier it takes).
- **A link cannot silently drift** (UX spec section 1). When the version (or EU wording) the server answers for the
  date is not the one the link pins, a notice above the reading says so, and the page brings the notice, not the
  article, into view: "This link pins {permalink}, which is not the version this site serves for that date; the
  version it serves is shown below." (the EU's says "the wording that answers that date"). The notice lasts only while
  the reading shown is the link's own; a reading the reader asks next carries none.
- **Results lead to the text** (UX spec section 2): each search hit is a link to its reading at its article, and hit
  rows print no digest (the link carries the pin). Every permalink the reading, the dossier and the search print is a
  link to its page, under the page's interface language.
- **The journeys** gain three steps, walked in Chrome through the live server and through the API (CI's journeys, the
  image rehearsal and the deploy probe run every step):
  - `address`: a work's address opens its dossier;
  - `permalink`: the permalink the server's own dossier names for the date, opened at an article, shows that state
    with no notice and the article in view;
  - `stale`: a permalink whose digest pins nothing shows the state the date resolves to under the notice, in view, and
    `verify` must refuse the permalink the notice names with `pinned_digest_mismatch` (the notice is true only if the
    server says so too).

  A link to a reading now counts as a citation and is verified like a printed one; the permalink a notice names is not
  a citation.
- **V2's routes.** V2 showed its own page at `/lu-legilux/{work}` and `/eu-eurlex/{celex}`; the rehearsal's V2-route
  check now requires V3's dossier page there, and still a 404 at V2's dated and comparison addresses.
- **French**: the two notices went through a second addendum of 2026-10-14 of the French review (both approved; a link
  "épingle" what it pins) (`C:\lex-v3\lanes\fr-review\review.md`).
- **Measured**: web suite 1,088 of 1,088; Lex.V3.Tests 3,145 passed, 1 skipped; the journeys run locally against the API published from this branch: the fixture mount through the live server 44 of 44, served by the API with the keyboard 44 of 44, the licence-blocked mount 12 of 12 (each new step passing on each, and refusing `no_corpus_mounted` with no mount); Chrome at 390 and 1280 CSS pixels: a permalink scrolls to its article, a stale one to its notice, the French permalink to its article.
- **Next on this road** (driver decisions, reversible): after a reading resolves from the form, its
  permalink written into the address (UX spec section 4: "the resolved pair is written into the URL"); the interface
  language switch keeping the reader on the same document; the reading server-rendered at its permalink for readers
  without scripts (the specification of record's draft asks for pages readable without JavaScript).

## The public front door (sole driver, 2026-10-14)

The owner opened the deployed candidate and saw no product: the home page was the Trust and Coverage report, no page
linked to another, and every page carried a "Live development build ... not a release" banner in the Gateway's
monospace skin. The launch contract asks only that the screens exist and the journeys pass; the ideal UX
specification (35-ideal-ux.md, the product the pack calls TENEUR) was never the target. This slice is the first of
the work to that specification (a consolidated specification of record and a plan are being written from the pack).
- **Home is search** (UX spec section 2, Screen 1): `index.html` renders the search screen under the site's
  introduction (`renderLiveHomePage`, hydrated by the search script); Trust and Coverage moved to `coverage.html`;
  `search.html` stays. The journeys' coverage step is `/coverage.html`.
- **The site around every live screen** (`Document.jsx`): a header with the name, the menu (Search, Changes,
  Export, What this site holds; the current one marked) and the interface languages, carrying the live marker the
  banner carried; a footer with the product's statements (documentation, not legal advice; consolidated texts have
  no legal effect, the Journal officiel du Grand-Duché de Luxembourg (Mémorial A) and the Official Journal of the
  European Union are authentic; the sources). English and reviewed French.
- **The development banner is gone**; live pages use the Ask skin (comfortable density, Inter and Source Serif 4)
  instead of the Gateway's monospace; forms have one layout (label above field, full width, a primary button,
  visible focus, dark mode); the evaluation card's tables scroll inside labelled regions.
- **Measured** with Chrome's device emulation: home, search, coverage, dossier, reading, history, compare, radar,
  export and the French home fit 390 CSS pixels with no horizontal overflow (coverage did not: its card tables were
  519 px wide); the home page fits 320. Web suite 1,077 of 1,077.
- **The review of #955** (independent Claude subagent): changes requested, repaired in one round.
  - The French footer used "conseil juridique", "valeur légale" and "chaque citation", which broke the French review's
    own terms (T10: "consultation juridique"; "effet juridique"; a "citation" is the reference string). The French now
    says "Documentation, et non consultation juridique. Les textes consolidés n’ont aucun effet juridique", "chaque
    article cité", and "détient" for held. Every new French entry went through the French review's addendum of
    2026-10-14 (6 of 19 changed: "droit de l’UE", "acte", "Modifications", and the authentic Luxembourg publication named
    as the loi du 23 décembre 2016 names it, "Journal officiel du Grand-Duché de Luxembourg (Mémorial A)", in both
    languages) (`C:\lex-v3\lanes\fr-review\review.md`).
  - "Luxembourg and EU law, as it stood on any date" claimed more than the site holds (UX spec section 14: never imply
    coverage beyond what is disclosed). The tagline now reads "The Luxembourg and EU law this site holds, at a date you
    choose", and the home page adds "Where the site does not hold a text or a date, it says so". This is public wording,
    so it is a driver decision under the owner's authorisation of 2026-10-14, and the owner may revise it.
  - The home page promised screens nothing linked to: the menu now reaches every screen (Search, Read, Dossier, History,
    Compare, Changes, Export, What this site holds) until search hits and dossier rows link into a work's screens.
  - The Search entry is current only on the home page; a skip link leads past the menu (WCAG 2.4.1); the journeys gain a
    `home` step (the search step at `/`), so hydration, CSP, console and one-request checks run on the front door in
    CI, the image rehearsal and the deploy probe.
  - Two tests that built the live pages raced on shared bundles (failing about 7 runs in 10 when run together, on the
    base too); each now builds under its own tag.

## Replay G1, G3 and G4 over a real chained canary (sole driver, 2026-10-13)

- **The chain**: `C:\lex-v3\chained-canary-after-lu-2`, builds a and b (b chained to a with `--predecessor`), the tool
  built from 9a5f05d3 and frozen with its renderer sources, the GDPR and the Luxembourg act `loi-2017-08-29-a791`; both
  verified, 207 of 800 requests each (135 to the Publications Office, 72 to Legilux), 22:04 to 22:31 UTC. The first
  canary after the population chose `loi-2017-03-14-a439`, which holds no state (its AKN carries no expression
  applicability date, in the population too), so the chain could not show G1, G3 or G4 for Luxembourg; the canary
  runner now takes the act (`-LuxembourgWork`, driver decision: an act the population holds a state for), and both
  builds hold the act's state (2017-10-02, three articles), as the population does.
- **`V3ReplayOverChainTests` over it** (`V3_REPLAY_CHAIN` = a;b): 7 of 7 pass with the fixture chain's checks.
  Counts (`replay-counts.json` beside the chain): 2 builds, each the build the latest log records at its place (its
  own log id, build time and ancestors), 1 event in each log (b's carries a's unchanged: G3), 1 state named, every
  state verifying on the latest mount, 2 snapshots compared, 1 of them an earlier build's (a's state `as_observed` at
  a's snapshot, asked of b's mount, answers what a's mount answers: G4). The state is the same in both builds, so b
  answers it from its own text, not from a's kept copy. No file was replaced between the builds (the publisher changed
  nothing in the 13 minutes), so G1's replaced-version check holds vacuously here; the fixture chain shows it with a
  reworded text.
- **The review of #898 found the chain test too trusting**: it checked only the number of builds, so the latest build
  named twice, a copy of it, or an earlier build of another canary passed with the same counts, and its coverage check
  could be met by the latest build asked about itself. Each earlier mount is now held to the build the latest log
  records at its place, and at least one earlier build's state must be compared; those three chains now fail, and b's
  own kept copy of a passes in a's place.

## The first signed release over the population (sole driver, 2026-10-13)

- **`v3-20261013T214036Z-9a5f05d3d1b4`** (`image-rehearsal.mjs --mount derive-c` at 9a5f05d3, 21:40 to 22:01 UTC):
  - the mount's gates ran for its card;
  - the image was built, verified, **signed by the owner's release key** (Key Vault `kv-lex-v3-soufien`, piped to
    standard input; signer "lex-v3-release (Soufien Hajji)") and verified;
  - it was built again from scratch to **the same manifest digest**;
  - it was probed in WSL: **all 29 runs pass**, the privacy probe included (nothing written after the first answer),
    with one container at a time and its `/tmp` freed between starts (2,462 MiB);
  - V2 is absent: 543 entries scanned and 63 routes asked;
  - the card route serves the release's card;
  - its six assets were published and read back with no failure.

  Kept at `C:\lex-v3\releases\v3-20261013T214036Z-9a5f05d3d1b4` (with the rehearsal report and the gates' artifacts
  beside it).
- **The zero-traffic Azure candidate waits on the owner.**
  - `deploy/deploy.ps1` in plan mode verifies the release under the signing identity's public key and prints each
    step.
  - Its `-Apply` was refused by the driver's tool harness (its safety check on actions outside the machine), so
    nothing was sent to Azure. The driver does not route around such a refusal.
  - The owner either allows the deploy in the harness or runs it. The exact command, with the subscription and this
    machine's address filled in, is kept beside the release in
    `C:\lex-v3\releases\deploy-v3-20261013T214036Z-9a5f05d3d1b4.txt` (this repository is public). Its shape:
    `deploy/deploy.ps1 -Subscription <id> -ResourceGroup rg-lex-v3 -EnvironmentId <cae-lex-v3's id> -Registry
    crlexv3soufien.azurecr.io -IdentityResourceId <id-lex-v3's id> -SigningPublicKey
    C:\lex-v3\lanes\release-signing-public.pem -SignerIdentity "lex-v3-release (Soufien Hajji)" -Release
    C:\lex-v3\releases\v3-20261013T214036Z-9a5f05d3d1b4 -ProbeSourceCidr <this machine's address>/32 -Apply`, run
    from the checkout at 9a5f05d3.
  - Ready: the registry, the pull identity (AcrPull checked), the environment `cae-lex-v3`, and the probe-only
    restriction to this machine's address.

## The release from custody over the population: derived twice, equal; the image step in pieces (sole driver, 2026-10-13)

- **The first release rehearsal from custody** (`image-rehearsal.mjs --custody` over run 15's custody, the merge commit
  1bb13038's CI runtime as the tool, bound; signed by the owner's release key from Key Vault): the two independent
  derivations of the combined population **agree on all 6 files** (15:09 to about 17:40 UTC), and the gates ran over
  the mount for the card. It stopped at the image step: Node refuses one SHA-256 update over 2 GiB ("data is too long")
  and one `fs.readFile` of a file over 2 GiB, and the population image's app layer is 2.5 GB.
- **The release over the derived population mount** (`image-rehearsal.mjs --mount derive-c` at 19f140a4, signed by the
  release key): the image built, verified, signed and verified, and built again from scratch to the same manifest
  digest; then a start in WSL missed the 219 s deadline, with no output (the API's logging is cleared). Rerun at
  cf347cd2 with the container's state reported on a miss: WSL's memory was full (7.78 of 7.79 GB). **Cause: the image
  run never ended a container.** Each probe step starts one (about nineteen starts), and each holds its 2.2 GB `/tmp`
  of index copies in memory; `unshare --kill-child` ends the API with unshare only until the API changes its user
  (`chroot --userspec`), which clears that signal, so closing a start ended unshare and the `/tmp` watcher and left the
  API running. A replay of the probes with WSL sampled every 5 s showed it: three APIs alive after two closes, 4.5 GB of
  `/tmp` in memory, 1.5 GB left. The run's end (`endProcesses`) now ends every process whose root is the run's root
  filesystem, then unshare and the watcher, and returns once none is left (a container that cannot be ended fails the
  start's close); the live test starts its stand-in as the image is (bound root, `chroot --userspec`), and failed before.
  A missed start's report finds the API by its root (it matched unshare by command line). The rehearsal also lets the
  image's bytes go before the container starts (collected under `node --expose-gc`).
- **The server wrote SQLite temporary files while serving.** With every start answering (22 to 27 s each), the replay's
  privacy probe failed nine of 29 runs: two `etilqs_*` files in the container's `/tmp` each time, a sort or temporary
  index past SQLite's 2 MB cache (the counts behind coverage, which every page loads, and the first dossier; three
  files each over the population mount on Windows). Only the image's probe saw them: a journey here watched the API's
  directory, not its temporary folder. The readers now serve from connections whose temporary storage is in memory
  (`PRAGMA temp_store = MEMORY`, set once verification at open has passed, and on the search's and the counts' own
  connections); the verification keeps SQLite's default. Over the population mount: no file event in the temporary
  folder while the 14 step requests are served (6 SQLite files before), the same latencies, peak working set 763 MiB
  against 767. The journey now watches the API's temporary folder too (`watchApiFiles`).
- **The release path's bytes in pieces** (`large-bytes.mjs`): `sha256Hex` hashes a piece (1 GiB) at a time, and
  `readLargeFile` reads a file a piece at a time; the same digests and bytes. Used wherever the release path hashes or
  reads the image (image reproducibility, the rehearsal, the release assets, the deploy probe) and the derived mount.

## The deploy kit and the journeys over the population (sole driver, 2026-10-13)

- **The container is one vCPU and 2 GiB** (`deploy/main.bicep`, `main.json` rebuilt): the API opens the population mount
  with a 0.5 GB peak, and its private `/tmp` holds 2.15 GB of verified index copies, which a half-vCPU replica's 2 GiB of
  ephemeral storage cannot hold (one vCPU has 4 GiB). A startup probe (`/`, 10 s then 10 tries 30 s apart) gives the
  mount five minutes before the liveness probe can restart the container.
- **A restart finds room to mount again.** A replica's `/tmp` outlives a container restart and an ended process leaves
  its copies there (the review of #949): the bicep sets `LEX_V3_TEMP_IS_PRIVATE=1` and the API then removes an earlier
  start's copies before it mounts (`V3StaleIndexCopies`); without the variable nothing is removed.
- **The image run** sizes its private `/tmp` tmpfs from the mount it carries (every `.sqlite3` file plus 256 MiB; it was a
  fixed 256m) and allows its start 90 s plus a minute per GiB of indexes. The review of #950 found that a closed start
  kept its container (and its 2.4 GB tmpfs); `unshare --kill-child` did not end it (its stand-in kept its user; the API
  changes its user, which clears the signal), and the run's end now ends the container by its root (above).
- **The go-live browser probe** (`deploy-probe.mjs`) chooses its Luxembourg work from the candidate's own answers
  (`heldLuxembourgWorkFromAnswers`: the same rule, from a search for "loi" and the work's dossier; over the population it
  chooses what the index read chooses), since a deployed revision's index is not at hand; it asked for the fixture's work,
  so a population candidate would have failed J1 and been deactivated (the review of #950).
- **One billed replica.** Each active revision runs a replica (1 vCPU, 2 GiB: about 23 EUR a month idle, about 26 with the
  registry). The kit keeps every revision, so after a promotion `deploy.ps1` prints the command that deactivates the
  previous one (kept, stopped); a rollback activates it again (about a minute to mount) and moves the traffic. Keeping it
  active for an instant rollback would be about 49 EUR a month: the owner's choice (OWNER-BRIEF.md).
- **The journeys over the population mount pass in Chrome** (`journey.mjs --real-mount --served-by-api`, `derive-c`, the
  API at #949): all 29 runs. The eight screens (coverage; search, 14 citations verified; dossier; reading, 8; compare;
  radar; export, 8; provision history), J1 (refused `no_version_for_date`), J2, J5, the three EU steps (search 61
  citations verified, dossier, reading 200), J6, J7 (REST and MCP alike) and J8. Three journey defects were fixed on
  the way: the API was given a fixed 60 s to answer (the population mount takes about 50 s; now a minute plus a minute
  per GiB), the timeout was reported as an exit (the flag was read after the process was ended), and every ended API
  left 2.3 GB of copies in the shared temporary folder (each now has its own, removed at close). And the steps now ask
  for a Luxembourg work the mount holds (`heldLuxembourgWork`: `loi-2015-12-03-n1` on 2015-12-11 here); the fixture's
  work is not in the population, so its steps only ever met `identifier_unknown`.

## The API serves the combined population in a small server (sole driver, 2026-10-13)

- **What was measured.** The real API (`Lex.V3.Api`) over the combined EU and Luxembourg mount (`derive-c`, 2.52 GB), one
  core, the GC heap capped as a container would cap it. Before this change it needed a 9.8 GB peak to open the mount
  (46 s) and refused it with `OutOfMemoryException` under every cap up to 6 GiB, so no Azure consumption container (at
  most 8 GiB) could serve it; its first EU search then took 27 to 31 s.
- **Why.** Every check at open held whole tables: the corpus (207 MB, 260,235 members) was parsed twice and re-serialized
  whole for its canonical check, and kept as 1.5 GB of objects for the server's life; the EU index (1.4 GB) was read
  into memory twice; both indexes serialized every row into one byte array to hash it. And the EU articles table has
  no index on expression, work or CELEX, so lookups by them read every article's text.
- **What changed, every check kept.** The logical-row hashes are written into SHA-256 as the serializer writes them
  (the same bytes); the large tables are read once, as the hash reads them, and checked row by row; the EU index is
  copied and hashed as a stream; the corpus is verified member by member (`LexCorpus6MountView`: each member's exact
  canonical bytes at its place, the header's canonical bytes around them) and the mount keeps only the members an
  answer looks up; the EU reader knows, from the rows it verified, which member objects carry each expression, work and
  CELEX, and finds their articles through the existing object index.
- **Now.** The mount opens with a 0.52 GB peak and keeps 83 MB; the API serves it on one core under a 1 GiB container's
  heap cap, answering in 32 s from start (its private `/tmp` holds 2.15 GB of verified index copies). An EU search answers
  in 0.02 s, the first in 1.9 s; an EU dossier in 0.04 s. The gates over the mount give the same card.
- **For the deploy.** One vCPU and 2 GiB: the memory fits with room, and a replica's ephemeral storage is 4 GiB at one
  vCPU (2 GiB at half of one, less than the index copies). The deploy kit's sizing and a startup probe follow.

## The launch contract, line by line (2026-10-03)

This section says where each line of LAUNCH-CONTRACT.md stands. No box is ticked: the contract holds on the exact
tuple promoted to production, and that tuple is built from the complete populations. Four kinds of evidence appear
below:
- **CI:** the evidence runs on every pull request, over fixture mounts.
- **Real data:** a run on real retained custody (STATUS-DATA.md).
- **Bounded mount:** a run on the real bounded first mount (`C:\lex-v3\first-mount-decision95-restart-20260930`,
  EU only), in the release rehearsal of 2026-10-01 (PR #862).
- **Waits:** what the line still needs.

**Promises to the reader**
- **Every quote pinned, every citation verified.**
  - CI: the `journeys` job verifies every citation the pages print, over four fixture mounts (the Luxembourg fixture,
    licence-blocked, two-state, and the EU annex control).
  - Bounded mount: 61 EU search citations and 1 dossier citation verified.
  - Waits: the population mount.
- **No dated request silently gets another date.**
  - CI: the temporal set passes at 100 percent on the Luxembourg fixture mount (its `as_of` and `in_force_on`
    arms) and on an EU fixture mount (the original wording, two different texts on one later date, and a later
    consolidation; EU `as_of` in English, in French and with no language), each arm with its date-shift control
    caught (`V3MachineGatesTests`).
  - EU `in_force_on` is a typed refusal the EU manifest states: asserted at every case date, and measured in the
    refusal set.
  - The mounted gates derive EU cases from the EU index's states table (`V3MountedGatesTests.Europe.cs`).
  - Bounded mount: not measured, and it never can be for this line. It holds no Luxembourg state, and its EU index is
    schema 2, with no states table, so the EU arm has no case there (review of #923). A real mount with a schema-5 EU
    index, such as #907's rederivation, can run the EU arm.
  - Waits: the Luxembourg population, and the EU population mount.
- **Every absence typed.**
  - CI: the refusal set, and the registry digest in every envelope.
  - Bounded mount: 8 refusal cases pass (coverage and seven EU requests), with the shuffled control caught.
  - The six codes no operation produces are recorded as driver decisions.
  - Waits: the Luxembourg population. Every Luxembourg code is "not produced" on the bounded mount, so 9 of the 14
    produced codes are measured on fixtures only.
- **Image-only annexes stay out.** The control case holds at the API (#918), on the reading and dossier pages in a
  browser (#919), and in the export (#920). No real image-only EU annex is held yet.
- **Rights at compose time.** The evidence-bundle rights tests (`V3CorpusEvidenceBundleMountTests`, the refusal
  gates), and the licence-blocked journeys in CI, by pointer and by keyboard.
- **Luxembourg and EU dates never merged.** `date-speech.test.mjs`, per publisher, in English and in French (CI).
- **No model retrieves, reranks or authors text.**
  - `ask` answers `assistant_v3_unavailable` (J6 in CI, `AssistantContainmentTests`).
  - `NoModelDependencyCensusTests` checks every production reference and package manifest.
- **Byte-stable derivation.**
  - Real data: two independent `Lex.V3.Tool derive` processes over real bounded custody reproduced every mount file
    byte for byte. This happened twice on 2026-10-01: the March 2017 measurement, and #907's rederivation.
  - CI: #899's two-process tests, and the release command's `derive-twice.test.mjs` (#911).
  - The image: two builds give one manifest (#826; the bounded mount's rehearsal). That is image reproducibility,
    over an already-derived mount.
  - CI (2026-10-04): the release command's custody path derives twice with this commit's own tool, over fixture custody,
    and both processes reproduce the live build's files (below, Release path).
  - Waits: the complete populations' two derives. The EU population run derives twice and compares when its
    acquisition ends.

**Surface at launch**
- **All 27 operations served or refusing with a typed reason the manifest states:** Luxembourg, and EU (#913).
- **REST and MCP derive identical envelopes:** J7 compares 13 requests through both (CI).
- **Events:** append-only, cursor polling, `answer_drift`, no webhooks: J8 (CI).
- **The eight screens, and J1 to J8 in a real browser.**
  - CI, on fixture mounts, in Chrome, by pointer and by keyboard: J1 to J5 and J8's radar (#914 to #916), and the EU
    annex walks of #919 and #920.
  - CI, as API checks in the same job, with no browser:
    - J6: no live page asks `ask` while the assistant is contained, a reversible driver decision;
    - J7;
    - J8's event polling: no page polls events.

    Reading the browser line as covering these is a reading the owner has to accept at sign-off.
  - The real-mount mode (`--real-mount`) now walks every journey the fixture mounts walk (2026-10-04). The image
    rehearsal and the deploy probe use the same mode.
    - In the browser: the eight steps, J1's date before the history, J2's question, J5's law not held, and the three
      EU steps when an EU index is mounted.
    - J1 asks the fixture's work on 1900-01-01, before any state a population holds. Its reading must refuse
      `no_version_for_date`, saying no earlier state is held, whatever the API answers.
    - J5 must refuse `identifier_unknown` with the absence note, whatever the API answers.
    - J2 must show the absence note when the population carries no hit.
    - At the API of the same server: J6, J7 and J8's event polling. J8 follows the log's own cursor to the next page
      and checks it starts where the first ended, repeating none. It verifies the first 25 event permalinks, since a
      population log holds thousands.
    - A mount with no Luxembourg state, such as the bounded EU-only mount, cannot show J8: its log holds no event, and
      the run fails saying so.
  - Waits: the population mount, to run that mode.
- **Exports in PDF, JSON and CSV keep citations, rights, watermarks and exclusions:** Luxembourg (#789, #790) and EU
  (#920).
- **Chrome in French and English; German and Luxembourgish answer `localization_unavailable`; statute language
  marked:** #912 and the chrome scans (CI).
- **Accessibility.**
  - No meaning by colour alone, linear diff, explicit dates, bracket tables whole: #811 (`live-a11y.test.mjs` and the
    journey's paint check).
  - Keyboard and screen-reader paths: #802, and the keyboard journeys (CI).
- **Security headers on the real host; no query text, IP address or user agent recorded:**
  - CI: checked on the pages the API serves.
  - Waits: the deployment. The Azure subscription is disabled.

**Data at launch**
- **The complete EU population, English and French:** complete on 2026-10-05: acquired by EU runs 14 to 16 (two network
  stops resumed from the journal), derived twice with identical files (STATUS-DATA.md). Previously: EU run 14, from 2026-10-04 08:55 UTC on the resumable
  tool (#924). Runs 9 to 13 stopped before the end, and their custody is kept (STATUS-DATA.md).
- **The Luxembourg population:** run 11 proved all nine families for the first time and stopped at the census join on
  2026-10-09 (publisher drift during the run). #943 fixes that and two later defects that replays of its journal on the
  real custody found; a fourth replay reached the document phase. Run 14 started on 2026-10-09 on the #943 build, was stopped at the owner's request on 2026-10-10 after seven families, and resumed as run 15 on 2026-10-11, under a
  supervisor, reusing the EU population (`claude-lu-population-run.ps1`). Runs 1, 2, 10 and 11 to 13: STATUS-DATA.md.
- **Robots read per URL; the rights receipt fetched once per build on the Publications Office route; no request to
  eur-lex.europa.eu; transport bytes retained before decode:** by construction in the acquisition (STATUS-DATA.md).

**Release path**
- **One command from custody to signed, published, read-back assets.**
  - The rehearsal from an already-derived mount (`--mount`) passes with a rehearsal identity in CI's `image-rehearsal`
    job, on push, and passed on the bounded mount (#862).
  - With its probes, a rehearsal on the bounded mount now fails journey J8 (its log holds no event; #926). Its
    rehearsal of record is #862's.
  - The custody path (`--custody`, #911: derive twice from custody, compare every digest, then build, sign and publish)
    now runs end to end in the same job, with this commit's own derive tool (2026-10-04):
    - **The custody.** `V3_WRITE_RELEASE_CUSTODY` writes the fixture acquisition of both publishers, in the shapes the
      populations take: the EU original and consolidated wordings, and the Luxembourg legislative population scope. It
      is held brotli-encoded, with its derive checkpoint and the file digests of the mount the live build writes from
      the same acquisitions.
    - **The tool.** It is the dll the job built from this checkout. Its informational version must name the commit
      before it is laid out as the CI runtime artifact is, and the release command binds it to the checkout.
    - **The derivation.** Two `Lex.V3.Tool derive` processes must agree file for file. Their files must equal the live
      build's, and nothing may reach the proxy-variable trap.
    - **The image.** The image is then built twice and must be identical. It is verified, signed through the release
      signer's path with a key made in the job, published and read back.
    - **The card over that mount.** CI requires the three EU `as_of` arms, the refusal set and the retrieval set to
      have cases and pass every gate (review of #930: a card of `not_measured` arms passed before). On 2026-10-04 they
      did, in CI and locally: the EU arms 6 cases each with their shuffles caught, the refusal set 9 cases, the retrieval
      set 26.
    - **What it does not cover: Luxembourg text.** The Luxembourg half of this custody is the legislative scope's
      fixture. It holds one work, whose four members are unavailable or rights-withheld, so the derived Luxembourg
      index holds no state, article or relation (review of #930).
      - The two Luxembourg temporal arms are therefore not measured.
      - The refusal set leaves nine codes not produced, eight of them for want of a Luxembourg state held alone on its
        date.
      - Deriving Luxembourg documents and states from custody is held only by the act-range rows of
        `V3OfflineMountTests` (#899) until the population custody exists.
  - **The production signing entry point (2026-10-04).**
    - **The command.** `--signing-key <PEM file, or - for standard input> --signer <identity>` signs the image and the
      release with the owner's ECDSA P-256 key instead of a rehearsal key.
    - **The key.** It stays in the owner's key vault and can be piped in, so it touches no disk. It is read once and
      held only in that process. It is never written, logged or kept, and no error repeats it.
    - **The release.** It is published as a release (`lex-v3-release/1`, version `v3-...`) that names its signer.
      Reading back checks that the schema, the version, the manifest and both signatures say the same kind and signer.
    - **The deploy kit.** `deploy-probe.mjs --signer <identity>` refuses a rehearsal or another signer's release.
      `deploy.ps1 -Apply` now requires `-SignerIdentity`, so a deployment applies only a release-signer release; a
      plan run may still read a rehearsal.
    - **In CI.** CI's custody rehearsal signs through this path with a key made in the job, under an identity saying
      so, and checks that its report holds no private key.
  - Waits: the populations' custody for the real run, and the owner's signing key in their key vault.
- **Zero-traffic deploy, probes, promotion, a second revision, rollback, V2 retired:** wait for Azure.
- **Owner sign-off:** the owner's.

**Evaluation at launch**
- **Machine gates:**
  - Bounded mount: the refusal and retrieval gates pass, with their shuffled controls caught, and V2 is absent from
    the image.
  - Waits: the temporal set waits for Luxembourg states and the EU population mount, and the G1 to G5 replay waits
    for the chained canary, after the populations.
- **The evaluation card, with its statistical rows "not yet labelled":** on the Trust and Coverage page (#792; its
  words joined the chrome table in #807), and with the release assets (#833; since #844 its gates are the release's
  own mount's).

**Gaps this table found (the review of #922):**
1. Done (2026-10-04): a population-mount mode for J2, J5, J6, J7 and J8's events. It runs when the population mount
   exists.
2. Done in CI (2026-10-04) over fixture custody: the release command's custody path, end to end with this commit's own
   derive tool. The fixture's Luxembourg half derives no state or text (above), and the run on the populations' custody
   waits for that custody.

## Served today

REST at `/api/v3/`, from a mounted `v3-corpus` directory, **Luxembourg only**: `resolve`, `as_of`,
`timeline`, `article_history`, `diff`, `changes_in_period`, `in_force_on`, `search` (strict and relaxed
lanes), `coverage`, `provenance`, `dossier`, `citation`, `cited_by`, `verify` (PR #753: a
hash-pinned permalink verified against the state digest the index holds; a work identifier or a
stable coordinate answers the current digests), `relations` (PR #753: the one edge table in both
directions, `cites` edges only, what `citation` and `cited_by` serve as one ordered, paged list),
`evidence_bundle` (PR #755), `classification` and `manifestation` (PR #756), `status_on` and
`browse` (PR #757), `ask` as the contained assistant (PR #759), and `events` and `answer_drift`
over a genesis event log (PR #760). Without a mounted corpus every route answers
`no_corpus_mounted`. EU serves `resolve`, `search` in one EU work named by identifier (PR #761), and
`dossier` for an EU work (PR #762).

EU `dossier` (PR #762) answers from the EU index for an EU work named by any of its identifiers
(CELEX, work or expression IRI, provision, article identity): the work and its CELEX, every
expression the index holds with its language, the Formex act date of its one held wording
(`wording_dates`, with the same date semantics sentence as search), its article count (99 for the
GDPR) and the corpus members its articles were read from (object ref, outcome, content class), each
expression's identifier for `resolve`, `consolidations_held: false`, and fixed words for what is not
held (titles, later wordings, force dates, document type, corrigenda, other languages). Corrigendum
lines are in the EU index but are not joined to the work: the join between a corrected work root and
the work's publisher identifier is not established by any fixture, and the answer says exactly that
rather than calling them absent. Refusals with EU context: a language the work has no expression in,
an identifier naming more than one EU work, an unknown EU identifier, no EU index mounted (was a
mode refusal naming `r6_dossier`), and an identifier both indexes hold (`ambiguous_identifier`).
The routing is one helper, `LocateEuropeWork`, which `search` and `dossier` share. The Luxembourg
dossier's `first_observed` sentence now says what the event log's `first_sighting` does and does not
mean, as `coverage` and `provenance` do since PR #760.

EU `search` (PR #761) answers from the EU index when the named work is an EU work: a CELEX, a work
or expression IRI, or one of its provisions. It runs the Luxembourg search's two lanes (strict, then
relaxed; byte-exact; no ranker; paged with `continue_after`) over the one wording the EU index holds
of the work's expression in the language asked, with EU context (`eu-eurlex`, jurisdiction `eu`,
`official_consolidation_state`). Each hit names the article (CELEX, expression, the publisher's
article id and heading, article identity), its `wording_date` and the provision coordinate EU
`resolve` answers; no text or snippet is served. `wording_date` is the date the Formex package gives
the act (for the GDPR 2016-04-27), and the answer says it is not a publication, entry-into-force,
application or consolidation date and is never merged with a Luxembourg applicability date; no EU
answer carries `applicability_date` and no Luxembourg answer `wording_date` (tested). Refusals, all
with EU context: a language the work holds no expression in is `language_not_available` (French
answers `["eng"]` until Decision 89's expressions are acquired); two expressions in the language are
`ambiguous_identifier`; any `date` is `retrieval_mode_unavailable` (`requested_mode: r6_as_of`),
because one work on a date is `as_of`'s question and the index holds one original wording and no
consolidation, so no later date could be answered honestly; an unknown EU identifier is
`identifier_unknown`; an EU identifier on a mount with no EU index is `no_corpus_mounted`
(`required_corpus: eu`, where it was a mode refusal); an identifier both indexes hold is
`ambiguous_identifier`, as `resolve` answers it. A search that names no work is the Luxembourg
search, unchanged. `EuropeIndexReader` gained a work-scoped search on its own read-only connection,
and its older date-range `Search` and its counts now take the reader's lock (they used the shared
connection without it). Survey of 2026-09-29 (read-only): the EU index holds one original wording
per expression, no dated state, no consolidation, no article order and no permalink, so EU `as_of`,
`timeline`, `article_history`, `diff` and `changes_in_period` need consolidation acquisition and an
EU states table, `in_force_on` and `status_on` need Cellar force facts, and `evidence_bundle` and
`verify` need an EU permalink grammar (the parity details are driver decisions, below). EU `dossier` is the next slice that
needs none of those.

`events` and `answer_drift` (PR #760) read a new index table, `events`: the index's append-only
event log (`seq`, `scope`, `key`, `event`, `observed_from`, `detail_json`; schema
`lex-v3-luxembourg-index/6`, `user_version` 6, the fixed-input byte pin moved; `/7` since PR #864, below). A build is one
observation with no predecessor, so its log is a **genesis log**: one `first_sighting` per held
state, in the states table's key order, numbered from 1, keyed by the state's primary key and
carrying its digest, with `observed_from` null because no observation time reaches the index (the
body's HTTP evidence stays in the adapter, and the custody receipt's clock is not an observation
time, so none is invented). The log is a pure function of the states, so the reader recomputes it
and refuses any other rows. `events` takes `after` (a cursor `{log_id}:{seq}`, where the log id is
the index digest), `limit` (up to 200) and `event` (one of the mintable names); it answers the
events with their state's permalink and stable coordinate, `has_more` and `next_after` (always the
cursor to poll next), the log block (`basis: genesis`, `predecessor_index_sha256: null`,
`observations_compared: 0`, count, last seq), the delivery rule (cursor polling, at least once,
deduplicate by seq within log id, no push: Decision 93), what `first_sighting` does not say, that
silence is not upstream health, the twelve names this pipeline may mint (this build mints
`first_sighting` only; the others need a predecessor build) and those this log holds, and what is
not held. Append-only and "the same cursor answers the same events" hold while one index is
mounted, and the answer says so: a new build starts a new log whose cursors are new. A cursor from another log refuses `snapshot_unknown` (its first producer) rather than
being read against this one; a sequence number beyond the log's last is a request-schema failure.
`answer_drift` (optional `identifier`, refusing as `dossier` refuses; `after`; `limit`) enumerates
the past dated answers the log's `validity_revised` and `interval_closed` events invalidated; a
genesis log holds none, so it answers an empty list with its basis, `asserts_no_drift_in_law:
false`, `asserts_publisher_unrevised: false` and what would answer (a later build compared against
this one), never "nothing drifted". The code holds twelve event names (`V3EventRegistry`); the Stage
4 registry's thirteenth, the coverage event, is never minted (B42 finding 5.2; the scope-line gate
forbids production source naming it); the mintable registry stays at twelve (driver decision). The two "not held" sentences
that said no first-sighting event is held (`provenance`, `coverage`) now say no observation time is
held and what the log's `first_sighting` does and does not mean. Decisions taken by the driver,
reversible: events are scoped to states (no work-level events); the cursor names its log;
`answer_drift`'s future rows are date intervals per revising event (stated in the answer).
Deferred to the next index schema: predecessor chaining (the builder takes the previous verified
index, copies its log unchanged and appends comparison events), `observed_from` from the route
evidence, and every revision event. The web lane claimed predecessor chaining on 2026-10-01
(PR #862, the web lane's order under "Next, in order").

`ask` (PR #759) answers the containment and nothing else (Decisions 51 and 91, S4-A05): every
request, whatever the question, is a success envelope under the `point` verdict whose result is a
`handoff_card` carrying the typed presentation result `assistant_v3_unavailable` (not a refusal
code; the registry stays at twenty), the containment and its end condition (the `answer_dossier/1`
and advice-boundary slices reviewed and integrated), `model_gloss: disabled`, and the deterministic
operations that answer from held law, in resolver-first order: `resolve`, `search` (with the
languages this mount holds searchable text in), `as_of`, `evidence_bundle`, each with its route, the
parameters its reviewed request schema requires (read from the schema) and what it answers. The
request schema is `question` (a non-blank string) and nothing else; the question is not read, stored
or echoed, so the card is byte-identical for every question. No model is called and no publisher is
contacted. The same card over MCP (`tools/call ask`). The envelope verdict is `point`, a driver
decision under ruling 5 (review of PR #759: the catalogue's POINT delivers an instrument, an
official link, a reason and a human counter, and the card has only the reason; Decision 91 cites
Decision 63(a)'s presentation verdict as the precedent). Decisions taken by the driver, reversible:
the host takes the verdict from the result (every other operation still answers `answer`); `ask`
refuses `no_corpus_mounted` without the Luxembourg index, as every served route does;
`localization_unavailable` (the other Decision 91 result) is a surface concern of chrome locales and
is not produced by the API. The guards: `V3CorpusAskMountTests` puts the eighteen scope-line
questions (now one shared list, `tests/Lex.V3.TestSupport/ScopeLineQuestions.cs`, which
`ScopeLineQuestionTests` pins its cases to) and five containment questions to the mounted route over
REST and MCP and requires the builder's card, verdict and object type for each, so a route that
answered any of them otherwise fails there (the review's mutation, a question-dependent answer, is
caught); `AssistantContainmentTests.TheLegacyAssistantRouteIsStillDisabled`, which asserted that
`ask` had no route, and `ScopeLineQuestionTests` now pin the binding and the builder and say that the
route is the ingest suite's. The owner may read Decision 91 as "no route until S4-A04 and S4-A05"; if
so, say the word and the route is removed.

`status_on` and `browse` (PR #757) read the `work_facts` table too. `status_on` takes
`as_of`'s request (`identifier`, `date`, optional `language`), selects the state as `as_of` does and
refuses as `as_of` refuses, and beside that state serves the publisher's force assertions about the
work verbatim (`in_force_status` = jolux:inForceStatus tokens, `entry_into_force` =
jolux:dateEntryInForce, `no_longer_in_force` = jolux:dateNoLongerInForce, each with subject, value,
datatype and evidence digest) plus one fixed reading of the dates, `asserted_in_force_on_date`
(true when an entry date is on or before the requested date and no end date is; false when an end
date is on or before it or every entry date is after it; null when no dated fact is asserted, when
only an end date after the requested date is asserted, or when a lexical value is not exactly a
civil date `yyyy-MM-dd`), with the rule and the basis in the answer; when the publisher
asserted no force fact the absence is typed (`force_facts_held: false`, `what_would_answer`,
`asserts_absence_of_law: false`) and is never read as "not in force". The real 1991 act's envelope
asserts no force fact, so the test fixture asserts them through a new helper and holds the reading
on every side of the dates. `browse` lists the held works in work-key order, paged (`limit` up to
200, `after` = work key), each with its publisher identifiers, languages, first and latest state
dates, state count and the publisher's typeDocument and rdf:type facts; `type` filters by the
typeDocument IRI or its last segment, compared exactly and case-sensitively (a substring test, no
LIKE; a value that is neither an absolute IRI nor a bare token is a request-schema failure),
`language` by the states' language (a work selected by one language still lists them all); the listing scans the states in key order and cuts by limit, so a deep page costs
the rows before it. Decisions taken by the driver, reversible: the force reading is a civil-date
comparison and nothing more (an inForceStatus token is served, not read); `browse` answers
`work_record` rows rather than a `classification` tree; no capability cells for the fact table.

`classification` and `manifestation` (PR #756) read a new index table, `work_facts`: the
publisher's typed assertions from the Stage 3 envelope (`TypedAssertions`, all 26 admitted
predicates), verbatim, one row per assertion with the subject, the predicate and fact kind as the
closed vocabulary names them, the object as an IRI or a literal with its datatype and language tag,
and the observation digest. The index schema was `lex-v3-luxembourg-index/5` (`user_version` 5; the
stamp, the logical-rows hash and the reader's exact-schema check all cover the new table; the
fixed-input byte pin moved). `classification` takes `dossier`'s request (`identifier`, optional
`language`) and answers the facts on the work's own IRIs and the selected expressions grouped by
predicate: `document_types` (jolux:typeDocument), `resource_types` (rdf:type), `legal_values`,
`responsible_bodies`, `historical_identifiers`, `publication_dates`, `document_dates`, each with
subject, object kind, value and evidence digest, plus the count and the names of other predicates
held but not grouped; nothing is inferred and no subject term exists. `manifestation` answers, per
selected expression, the manifestations the publisher asserted (jolux:isEmbodiedBy) with their
formats (jolux:userFormat, the last IRI segment as the token) and items (jolux:isExemplifiedBy), marks
the manifestation whose body this corpus retained and carries the corpus manifest's body digest, and
lists the retained members; an optional `format` filters and, when no manifestation of the selected
expressions carries it, refuses `format_not_available` naming the formats held. Both refuse as
`dossier` refuses. Decisions taken by the driver, reversible: every admitted predicate is stored (the
table is the envelope's assertion list, not a selection); `status_on` and `browse` follow in the next
slice over the same table (the real act's envelope carries no in-force facts, so `status_on` needs a
fixture that does; done in this pull request).

`evidence_bundle` (PR #755) is the first operation that serves article text. It takes
`as_of`'s request (`identifier`, `date`, optional `language`), selects the state as `as_of` does and
refuses as `as_of` refuses; then, per selected state and before any text is read, it enforces the
launch contract's rights rule: every corpus member the state's articles come from must be
`acquired` under the rights disposition `agreed_same_run_cc_by`, or the whole request refuses
`text_withheld` naming the official identity, the official link and the retained body digest; a
state whose articles hold no text refuses `text_not_available` (official identity, official source,
the retained receipt digest, and the absence evidence `what_would_answer` /
`asserts_absence_of_law: false`). The answer carries, per state, the permalink, stable coordinate,
state digest, rule profiles, the sources as `provenance` names them and the distinct retained body
digests; per article, the identity, publisher id and wId, the article-level date, the text (the
publisher's wording: the text and reference tokens concatenated in publisher order, the same bytes
`search` matches), `text_sha256` over exactly those UTF-8 bytes, the `wording_sha256` the index
keeps, the notes (marker and body text, beside the text and outside the wording digest), the
language, the `body_sha256` of the corpus member the article was read from, the official source
and an article permalink (`permalink#publisher_id`; `verify` accepts that form and answers
`digest_matches` naming the article, or `anchor_not_in_version` when the pinned state does not hold
it; the language is pinned through the state digest, which is over the expression IRI and
language, and named beside the permalink). Articles are in the state's own order. An article whose
tokens carry no text (the index admits a marker-only article as evidence) is named under
`articles_without_text` with its reason and is not served as a quote; a state none of whose articles
holds text refuses `text_not_available`. Decisions taken by the driver, reversible: the text is the
searchable wording rather than a re-rendering of the token stream; rights are enforced per request
and not per article (one withholding source withholds the bundle); signature, observation time and
export formats are named as not held. Known limit, for the owner to rule on if it matters for
quotes: the text is the publisher's text and reference tokens joined with no separator, because
paragraph structure and whitespace-only nodes are not retained at ingest, so "Art. 1er.La
profession" runs together; the token stream the composer could render from is in the index
(`tokens_json`), the paragraph boundaries are not. The web refusal card now accepts the registry's `text_not_available` payload beside
the reading view's own provision-level one (two producers, two declared key sets) and the catalog's
two text examples show the producer's fields.

Registered and not served: `concepts`, `knowable_on`, `transposition` (`as_observed` is served by
build snapshot since PR #874). Since PR
#758 a request for one of them is the typed
transport failure `operation_not_served` (HTTP 404, `application/problem+json`, below the envelope
like every transport failure), which tells a registered operation with no route apart from a path
nothing names (`unknown_route`, still the answer for a typo, a query string, a trailing segment or
a case variant); an MCP `tools/call` naming one says "registered operation this mount does not
serve" in its invalid-params message; `coverage` names them and now says what a request returns.
Decision taken by the driver, reversible: a transport failure rather than an envelope refusal,
because the registry has no refusal code for "operation not served" and inventing one would change
the reviewed registry; the owner can ask for an envelope answer instead. Survey of 2026-09-28 (read-only): the index-table group of four
needed a new index table over Stage 3 inputs the ingest already holds (`TypedAssertions`: in-force status and dates, `typeDocument`, `isEmbodiedBy`,
`userFormat`), which bumps the index schema; `concepts`, `transposition`, `as_observed`,
`knowable_on`, `events` and `answer_drift` need data the ingest does not produce.

MCP: served over streamable HTTP at `POST /mcp` (PR #754), the request half of the
transport: one JSON-RPC message in, one JSON document out (`initialize`, `tools/list`, `tools/call`;
a notification answers 202 with no body; a JSON-RPC error is an HTTP 200; a GET is 405; a body over
the platform's 1 MiB ceiling is 413 before parsing; no server-initiated stream is offered). One tool
per served REST operation, named by its operation id, whose `inputSchema` is the reviewed request
schema's `parameters` shape, and whose call runs the same dispatch the REST route runs
(`V3ApiHandler.ExecuteFor`), so the two transports answer one envelope for one request; the
endpoint test proves it for all twenty-three. The launch-contract line "REST and MCP derive identical
envelopes from the registry" is the owner's to tick.

Web: 41 React components, 980 tests. Preview screens render fixtures. The V3 Luxembourg search
answer has a reader (PR #771). The answer census now samples `search` five ways from the real handler:
- a phrase with 4 strict hits and 1 relaxed hit;
- the same phrase one hit per page, with its cursor;
- the page that cursor leads to, 4 hits against a population of 5, because the population counts
  the whole result and not the page;
- the relaxed lane alone, where the strict count is null (not scanned), not zero;
- a word the held text does not carry: no hit is an answer.

`web/scripts/search-answer.mjs` (`readSearch`) reads that shape and throws on any answer that
breaks a rule the answer states about itself:
- strict hits before relaxed ones, and a hit's one reason is its lane's; with a mode, every hit is
  in that lane and the other lane's count is null;
- each article of a state appears once, and there are no more hits than the limit;
- a cursor exactly when the page is truncated, and the cursor names the last hit;
- the population holds all of an untruncated first page, and at least one hit more than a page
  after a cursor or before one;
- each hit's permalink is the work, date and state digest the hit names;
- `ambiguous_works` (a dated search's works with several applicable states, which contribute no
  hits) and `work_resolution` are read into the view.

An EU search answer (`publisher: "eu-eurlex"`) has another shape and is refused by this reader;
since PR #853 `readEuropeSearch` reads it, beside this one.
It reads no text snippet, because the answer carries none. The pre-V3 renderer
`search-results.mjs` (`lex_id`, `provision_num`, a row set) stays for the preview until the live
search screen replaces it.

The live search screen (PR #772) is built into `dist-live/search.html` beside Trust and Coverage,
with its own bundle `client-live-search.js`, which embeds the census contract and nothing else of
the census.
- The form sends only the phrase as typed (never trimmed or folded, at most 512 characters and 32
  distinct words, counted as the platform splits them) and a language from French, German and
  English. That list is a driver default: the platform answers a language it holds no text in
  with `language_not_available`. The form's controls carry no `name`, so a submit the browser
  performs itself (before hydration, or without the bundle) sends nothing, and the phrase never
  reaches the address bar, the history or a referrer.
- It sends one `POST /api/v3/search` when the reader submits, never while rendering. A new search
  cancels the one in flight, and "Next page" repeats the search with the cursor the page handed
  over (`createSearchSession`).
- An answer is read by `readSearch` and laid out: the population in both lanes, the work
  resolution, any ambiguous works, and "the first hits in the stated order, not the best hits".
  Each hit shows its article, work, version date, lane and printed permalink; the permalink is not
  a link yet, because this origin serves no reading page for it.
- The two refusals a request from this page can meet (`no_corpus_mounted`, `language_not_available`)
  are refusal cards. A transport failure, an unreadable envelope, or an answer the reader refuses
  is each a state with a sentence. A server that was reached and refused the request
  (`request_schema_invalid`, in practice a cursor from a result the server no longer holds) is said
  as refused, not as unreachable.
- The tests drive it with the census's real whole envelopes. The envelope census now holds a
  search answer with hits in both lanes and a search `no_corpus_mounted` refusal.

The V3 Luxembourg dossier answer has a reader (PR #774). The answer census now samples
`dossier` (the fixture work in `fra`: one state, no titles, 9 items not held).
`web/scripts/dossier-answer.mjs` (`readDossier`) reads that shape and throws on any answer that
breaks a rule the answer states about itself:
- the states are the work's own (its work key and publisher work IRI), in the language asked
  when one was asked, in date order, each listed once and holding at least one article;
- each state's stable coordinate names its work and date, and its permalink pins that coordinate
  and its digest;
- a state's next date is the next later date in its own language, or null;
- `state_count`, `history_begins` and `latest_applicability_date` are the states';
- each title group belongs to one of the states' expressions, in the language asked;
- `not_held` names each item once with its reason. It is carried whole: it is the V3 form of the
  pre-V3 screen's unfilled slots.

An EU dossier (`publisher: "eu-eurlex"`) has another shape and is refused by this reader; since PR
#856 `readEuropeDossier` reads it, beside this one. The fixture holds one
state and no titles, so the tests also read a work with three states in two languages and a
titled work, built from the captured answer the way `V3CorpusMount.Dossier` builds them. The
pre-V3 `dossier.mjs` renderer stays for the preview until the live dossier screen replaces it.

The live dossier screen and its journey step (PR #775). `dist-live/dossier.html` has its own
bundle `client-live-dossier.js`, which embeds the contract and nothing else of the census.
- The form sends the work identifier as typed, plus a language only when one is chosen (French,
  German, English, or "any held language", which sends none). The controls carry no `name`, as the
  search form's do.
- One `POST /api/v3/dossier` per submit, and a new request cancels the one in flight.
- The answer is read by `readDossier` and laid out: the work and its languages, its titles ("This
  index holds no title for this work" when none), the state count and range, a table of states
  (language, applicability date, next state, articles held and not admitted, printed permalink),
  and every item the dossier does not hold with its reason.
- An EU work's dossier is answered in another shape. Until PR #856 it was said as not shown on this
  screen; it is now laid out in its own words (the EU dossier paragraph below).
- The envelope census now holds the dossier answer and its four refusals from the real handler. A
  language not held, an EU identifier on a mount without the EU index (`no_corpus_mounted`,
  `required_corpus: "eu"`), and no mount are refusal cards. A `no_corpus_mounted` sentence names
  the missing index from its payload, on this screen and on search: "This build has no EU index
  mounted." or "This build has no Luxembourg index mounted." It never says "no index mounted",
  which is false on a server that holds the other publisher's index (review of #775).
- An unknown work (`identifier_unknown`) is said by its code without the card. `refusal-card.mjs`
  keeps its standing requirement that this card carry a `population_disclosure` (the size of what
  was searched, so "not found" is never read as "no such law"), and the platform's payload carries
  none yet. That disagreement is now visible on a live page: either the producer carries the
  disclosure, or the reader supplies it from its own census.
- `journey.mjs` now runs a third step. On `/dossier.html` it types the fixture work's identifier and
  submits with any language. With the mount the page ends in the dossier ("1 state, from 2024-02-01
  to 2024-02-01", the not-held list); without one, in the `no_corpus_mounted` card. All six runs
  pass: 6 requests each, exactly one to the API with exactly the typed body, the page at its own
  address, no history entry, history state or cookie, and hydration clean. A build whose request
  always added a language failed both dossier runs on the body check.

The reading screen's V3 source has a reader (PR #776). The text of a state, with what a quotation
needs, is the `evidence_bundle` answer, which the answer census already samples (the fixture state
in `fra`, 49 quoted articles). `web/scripts/reading-answer.mjs` (`readEvidenceBundle`) reads it
and throws on any answer that breaks a rule it states about itself:
- text only under the admitting rights disposition, stated by the bundle and by every source;
- one state per served language, in the languages' ordinal order: the one applying on the date
  asked, from on or before it to a next date after it or none, and in the language asked when one
  was asked;
- state coordinates and permalinks as the dossier's, and each article's permalink is the state's
  with the publisher's article id after `#`;
- each quoted article has text in its state's language, with the UTF-8 byte length stated, read
  from one of the state's sources (`body_sha256s` are exactly the sources' bodies) and pointing at
  the state's official source;
- each article's validity flag is exactly "its own date is stated and differs from the state's",
  and the state's count is theirs;
- articles without text are named apart (`no_text_tokens`), never quoted and never also listed as
  quoted; a state with no quoted article is refused, not answered.

The reader does not recompute text digests: it is synchronous, and the browser's digest is not.
Its tests check every captured text digest with Node's SHA-256, and `V3ReplayGuaranteesTests`
recomputes them from the publisher's file. The tests also read a bundle in two languages built as
the producer would send one: German before French, the German state from its own corpus member
with its own source, body and article identities (review of #776). The pre-V3 `reading.mjs` stays for the
preview until the live reading screen replaces it.

The live reading screen and its journey step (PR #777). `dist-live/reading.html` has its own bundle
`client-live-reading.js`.
- The form sends the work identifier as typed, a date written `yyyy-mm-dd` (a text field, checked as
  a calendar date), and a language only when one is chosen. The controls carry no `name`.
- One `POST /api/v3/evidence_bundle` per submit. The answer is read by `readEvidenceBundle` and laid
  out state by state:
  - the permalink, and the counts quoted, held without text and not admitted;
  - each article's text as a quotation in its state's language (`lang="fr"` from `fra`, never the
    interface's);
  - both dates where an article's own date differs from its state's;
  - its notes, its text digest and its permalink;
  - the articles held without text, named and never quoted empty;
  - the not-held list.

  The words "in force" never appear.
- The envelope census now holds the bundle answer and four reading refusals: a date before the
  history, an unknown work, an EU identifier on a mount without the EU index
  (`retrieval_mode_unavailable`), and no mount. It replaces each per-run `object_ref_sha256` with a
  fixed digest, as it does the corpus and index digests, because the corpus mints those per run and
  the bundle's sources carry them.
- `retrieval_mode_unavailable` and `no_corpus_mounted` are cards. So are `ambiguous_version`,
  `text_withheld`, `text_not_available` and `language_not_available`, checked against the payloads
  the refusal census records.
- Two absences are said by their code, without the card:
  - `identifier_unknown`, as on the dossier;
  - `no_version_for_date`, because the card's contract requires every absence to carry
    `what_would_answer` from its closed vocabulary (and `asserts_absence_of_law`), and the
    platform's payload names the nearest earlier and later dates instead. Its status line still
    carries the date to ask again: "The history this index holds for this work begins on …".
    `text_not_available`'s payload does carry both, so its card is shown.
- Found and fixed on the way: the React refusal card threw on the platform's own `ambiguous_version`
  payload. The platform sends its candidates as bare hash-pinned links, and the string card
  normalised them while the React card read the raw strings. Both now use one `candidateView`, which
  says "publication date not stated by the platform" and "withdrawal not stated by the platform". A
  test renders the real payload and holds the two cards to the same text; it failed on the old card.
- `journey.mjs` runs a fourth step: `/reading.html`, typing the fixture work's identifier and
  `2024-02-01` into the two fields. With the mount the page ends in the text ("49 articles quoted",
  "Art. 15."); without one, in the card. All eight runs pass. A build whose reading request always
  added a language failed both reading runs on the body check.

The provision history screen's V3 source has a reader (PR #778). The answer census now samples
`article_history` (the fixture work's `art_15` in `fra`: one row, no absence).
`web/scripts/history-answer.mjs` (`readArticleHistory`) reads it and recomputes everything the
answer derives from its rows, throwing where they disagree:
- rows and absent states together are the states in scope, each once, each in date order, each
  pinned by its permalink, in the language asked when one was asked;
- every article in a row carries the anchor asked for, and its validity flag follows its dates;
- `wording_changed` is true exactly when a row's wording digests differ from the previous row of
  its language; `wording_runs` are each language's first row and every change;
  `distinct_wordings` are each language's distinct digests; `history_begins` is the first row;
- a row's next date is the next later state of its language in scope, whether or not that state
  carries the anchor.

The tests also read a lineage with an absent state between two rows, a changed wording and a
second language, built the way `V3CorpusMount.ArticleHistory` builds one.

The live provision history screen and its journey step (PR #781). `dist-live/history.html` has its
own bundle `client-live-history.js`.
- The form sends the work identifier and the publisher's article id as typed, and a language only
  when one is chosen. The controls carry no `name`.
- One `POST /api/v3/article_history` per submit. The answer is read by `readArticleHistory` and laid
  out:
  - how many held states carry the id, from when, and how many do not;
  - each language's wording runs and distinct wordings;
  - a table of rows: language, applies from, next state, "first held wording", "wording changed" or
    "wording unchanged", the article's own date (marked where it differs from the state's), and the
    permalink;
  - the held states that do not carry the id;
  - the wording rule.

  Nothing is derived: no end date, no "in force", no diff text.
- The envelope census now holds the lineage answer and three refusals: an article id no held state
  carries (`anchor_not_in_version`, `art_44`), an unknown work, and no mount.
  `anchor_not_in_version` is the third absence the card rejects (driver decision: the producer will
  carry the fields); its status line carries the nearest ids to try, "art_4, art_40, art_41,
  art_42".
- `journey.mjs` runs a fifth step: `/history.html`, typing the fixture work's identifier and
  `art_15`. With the mount the page ends in the lineage ("Carried by 1 held state, from
  2024-02-01", "first held wording"); without one, in the card. All ten runs pass, 6 requests each,
  exactly one to the API.

The compare screen's V3 source has a reader (PR #782). The answer census now samples `diff` twice
from the real handler: the fixture's one state against itself, and a second fixture's two states a
year apart with `art_15` reworded and `art_16` renamed to `art_16-new`. The second fixture is its
own, so every other sample stays the one-state answer. `web/scripts/compare-answer.mjs`
(`readDiff`) reads both and recomputes what the comparison derives:
- each side is the state that applies on its date, pinned by its permalink, with its dated articles
  exactly its article identities and its validity flags and count following its dates;
- one comparison per compared language and one entry per language not compared (with its bound),
  each language once and in order, and together exactly the scope: the language asked, or every
  language the work is held in;
- the same state on both dates carries no articles and no counts;
- otherwise every article of each side appears in exactly one row, the rows are the ids in ordinal
  order, each row's status is its sides' (added, removed, unchanged when the ordered wording digests
  are equal, changed when they are not), and the counts are the rows'.

The live compare screen and its journey step (PR #783). `dist-live/compare.html` has its own bundle
`client-live-compare.js`.
- The form sends the work identifier, two dates written `yyyy-mm-dd`, and a language only when one is
  chosen. One `POST /api/v3/diff` per submit.
- The comparison is read by `readDiff` and laid out linearly: each side's state and permalink, the
  date the next held state applies from (said as that state's start, never as "until"), its
  article count and how many of its articles carry their own date differing from the state's, the
  platform's note ("nothing about legal effect is asserted"), the counts, then the changed, added
  and removed articles (each status a word, with the whole wording digests on each side). The
  unchanged articles are one disclosure away. The same state on both dates says so, with no counts.
  The wording rule and the validity conflict rule close the page.
- The envelope census now holds the same-state comparison and three refusals (a from date before the
  history, an unknown work, no mount). The two-state comparison from the answer census is rendered
  in the tests: `art_15` changed, `art_16` removed, `art_16-new` added, 47 unchanged.
  `ambiguous_version`, `profiles_differ` and `language_not_available` are cards, checked against the
  refusal census's payloads. `no_version_for_date` and `identifier_unknown` are said by their code,
  the former with the date the held history begins.
- `journey.mjs` runs a sixth step: `/compare.html`, typing the fixture work's identifier and
  `2024-02-01` twice. With the mount the page ends in "The same version applied on both dates";
  without one, in the card. All twelve runs pass.

The radar screen's V3 source has a reader (PR #784). The launch contract's Radar is the change radar,
`changes_in_period`. The answer census samples it twice from the real handler: a window holding the
one-state fixture's first held state, and a window holding both states of the two-state fixture.
`web/scripts/radar-answer.mjs` (`readChanges`) reads both and holds every rule the answer states
about its rows:
- each row's state lies in the window (the closed interval between the two dates), pinned by the
  permalink that `resolve` serves; rows are in date order, then work;
- a compared row has one baseline of its work and language, dated before it and followed by it,
  with the same rule profiles; its wording change is exactly "an article changed, was added or was
  removed"; it has its counts, and `diff` parameters that ask for that pair;
- an uncompared row carries one of four reasons: `first_held_state` (no baseline),
  `ambiguous_version` and `ambiguous_baseline` (each with its candidates, its own state among them
  for the first), or `profiles_differ` (with the baseline whose profiles differ);
- the page names the first date not served exactly when it is truncated, and an untruncated page
  holds every version the population counts.

The live radar screen and its journey step (PR #787). `dist-live/radar.html` has its own bundle
`client-live-radar.js`.
- The form sends two dates written `yyyy-mm-dd`, plus a work identifier and a language only when
  given. One `POST /api/v3/changes_in_period` per submit.
- The radar is read by `readChanges` and laid out: the window and its population; the platform's
  caveat (a version row asserts no wording change, legal effect or entry into force); and each row's
  work, language, date and permalink. A compared row says whether its wording changed from its
  baseline, with the counts and the baseline's permalink; an uncompared row says why in words (the
  first held state, an ambiguity, different rule profiles), an ambiguity lists its candidate states
  and a row whose profiles differ names its baseline, each by permalink. An empty window says
  whether it meets what is held.
- The envelope census now holds a one-row window, an empty window before anything held, and no
  mount. The two-state radar from the answer census is rendered in the tests (the later state
  "wording changed" from its baseline).
- `journey.mjs` runs a seventh step: `/radar.html`, typing `2024-02-01` twice. With the mount the page
  ends in "2024-02-01 to 2024-02-01: 1 state of 1 work, of 1 held" and the first-held-state reason;
  without one, in the card. All fourteen runs pass.

The API serves the live pages on its own origin (PR #788, ruling 3). `Lex.V3.Api` opens `v3-web`
beside it (the built `web/dist-live`) through `V3WebRoot` and serves exactly the files it held at
start, by GET or HEAD, with `/` as `index.html`. A path it does not hold reaches the API's routing as
before, and another method is 405. Every page response carries:
- the page's own reviewed Content-Security-Policy, read from `index.html` (entities decoded) so
  there is one source, plus `frame-ancestors 'none'`, which only a header can carry;
- `Strict-Transport-Security: max-age=31536000; includeSubDomains`;
- `Referrer-Policy: no-referrer`, `X-Content-Type-Options: nosniff` and `Cache-Control: no-store`.

A directory with a page lacking its policy, a file of a type it does not serve, or a name outside
the served pattern is refused, and the API runs without pages rather than serving them wrong. No
live page's text says "synthetic" (the live banner never does). `journey.mjs --served-by-api`
places the build as `v3-web` and loads every page from the API's origin, with no Node server, and
checks the headers the page arrived with. All fourteen runs pass that way too. `serve-live.mjs`
stays for local development.

Every citation the served answers emit verifies (PR #796, the launch contract's first promise:
"verify resolves every citation the product emitted"). `V3CitationVerificationTests` drives the
operations the live screens read on the answer census's two fixture mounts (search, dossier, as_of,
evidence_bundle, provenance, status_on, article_history, diff and changes_in_period, the last two on
both), walks each answer whole and takes every citation by its role (a `permalink` or `*_permalink`
property, a `resolve.identifier`), which must be a hash-pinned permalink (a malformed one fails,
review of #796), and any other permalink wherever it sits, and asks `verify` for each on the mount
that emitted it: each must answer `digest_matches` for the state its digest names, and an article
permalink must name its article. The walk is held to find a permalink in every screen's answer, at
least one article permalink, and nothing that is only prose. The journey's own pages add the browser half when the journey runs next.

The interface languages on the live pages (PR #797, the launch contract's "DE and LB answer
`localization_unavailable`"). Every live page carries a language list (`LocaleNav`): English,
Français, Deutsch, Lëtzebuergesch, each named in itself and tagged with its own `lang`, the page's
own language marked current, and each link's `hrefLang` the language of the page it leads to
(English for an unreviewed language; review of #797). Only English chrome is reviewed (`REVIEWED_CHROME_LOCALES`), so the
other three lead to `locale-fr.html`, `locale-de.html` and `locale-lb.html`, built into `dist-live`:
the `localization_unavailable` page, in English and labelled English, under the live banner, with
no script. It says exactly what the preview's page says (`localeUnavailableCopy`, now shared; the
preview pages are byte-identical after the change). French chrome replaces its page once its copy
is reviewed (Decision 41); German and Luxembourgish stay unavailable at launch.
- Statute language apart from the interface's: the interface is `<html lang="en">`, and every piece
  of publisher text a live page shows carries its own language. That was already true of the
  quotations and notes on the reading and export pages; the dossier's titles (each in its
  expression's language) and search's matched titles now carry it too. A matched title is marked in
  its own language, which the resolver's card now names (`matched_title_language`, from the title
  row): the resolver searches every language's titles, so the language searched in is not the
  title's (review of #797). History, compare and radar show digests and ids, no publisher text.

The live pages' interface copy starts to come from one table (PR #800, the first step toward French
chrome). `web/scripts/live-chrome.mjs` holds each live page's title, eyebrow, heading and
introduction and every form's labels and buttons, in English, and the eight pages and their forms
render from it; every page rendered before and after the move is byte-identical. `liveChrome(locale)` answers only for a reviewed language and throws
for any other, since serving it would be a substitution; a test holds each page to its entries and
the table to the reviewed languages. French is drafted beside it for review
(`live-chrome-fr-draft.mjs`, printed by `node web/scripts/live-chrome-fr-draft.mjs`): never
imported by the product, and held by a test to the English table's exact shape, so an English
entry without a draft is caught.

The API process records nothing while a browser asks it (PR #801, the launch contract's "no query
text, IP or user agent recorded", on the journey host). In source, `PublicRequestRecordingTests`
already bounds the process to three startup messages, cleared logging providers, a pinned package
set and no write-capable file open. At run time, every journey run now captures the process's
standard output and error from its first byte, and watches its directory from when it first
answers until the page has asked, listing every file at both ends: after the first answer it must
write nothing and touch no file under its directory, including a file written and deleted within
the run (the review of #801 found two listings alone could not see one), and its startup output
must carry none of the run's text (the query, the browser's user agent). The first run of the check caught the synthetic bootstrap's startup
diagnostic (`lex_v3_preview_bootstrap_failed reason=immutable_custody`), which is why startup is
judged apart rather than admitted by its text. With the check, all sixteen journey runs pass, and
all sixteen again with `--served-by-api` (run 2026-09-30 on the journey mount written again that
day: the data lane's corpus format change had made the earlier mount unreadable, "the corpus
manifest-set bytes are not one valid typed document").

Keyboard and screen-reader paths through the eight screens (PR #802, the launch contract's
accessibility line).
- Screen reader: every live screen writes its answer (the result, the refusal card, or the sentence
  a state carries) into a polite live region (`LiveAnswer`) that the server renders around the idle
  or loading state, so the region exists before the answer arrives and a screen reader hears it.
  The export composer's panel is a live region of its own, since pinning changes it and the reading
  above does not. Every journey run now holds the answer to that region, at load and at the end.
- Keyboard: `journey.mjs --keyboard` drives every form step by the keyboard alone: from the top of
  the page, Tab until each text field has focus, type key by key, Enter to submit; the export
  composer's pin is reached by Tab and checked with Space. Each character is its own key press, and
  the page counts the character keys it receives: a run whose text arrived without them fails (the
  review of #802 found CDP's `Input.insertText` sets a field with no key event at all). Every focus stop on the way must show a focus indicator
  (an outline or a shadow), and Tab must reach every field the step types into (a text or a search
  field). All sixteen runs pass with `--keyboard`, and all sixteen with `--keyboard --served-by-api`
  (run 2026-09-30); the first run found the search box is `type="search"`, which the keyboard path
  now types into as well.

The screens' sentences join the chrome table (PR #803, the second step toward French chrome).
- `live-chrome.mjs` now also holds each screen's idle sentence, the loading sentences, and every
  sentence the search, dossier, reading and history screens say about an answer: the counts, the
  headings, the table columns, the work resolution and the lines a row is made of.
- A sentence that carries values is a template with `{name}` placeholders, filled in place
  (`fillParts`, laid out by `Say` in `LiveAnswer.jsx`), so a translation can put a quotation, a
  date or a permalink where its grammar needs it. A placeholder without a value, or a value without
  a placeholder, throws.
- A sentence that counts is `{ one, other }`, chosen by the language's own plural rule
  (`Intl.PluralRules`): French counts zero as one ("0 version"), which a comparison with 1 would
  get wrong.
- The phrases the platform itself sends (a matching rule, a scope, why something is not held) stay
  the platform's English, shown as sent. Compare, radar, export and Trust and Coverage keep their
  answer sentences in their components for the next slice.
- Every census envelope of the four screens, rendered through its answer view before and after the
  move, and each idle page: 21 renders, byte-identical except one. History's per-row wording marker
  is now a language-free code (`data-wording="first"`, `"changed"`, `"unchanged"`), not the English
  label.
- The French draft covers every new entry. A test holds each draft template to its English
  template's placeholders and each counted entry to both forms. French still ships only once
  reviewed (Decision 41).

The compare, radar and export screens' sentences join the table (PR #804).
- Compare: the summary, each side, the counts, each row (its status now labelled from the table,
  `changed`, `added`, `removed`, `unchanged`, rather than printed as the platform's code), the
  unchanged articles' disclosure, and a language not compared, with its bound labelled the same
  way.
- Radar: the summary (two counted phrases, states and works), each row and its verdict, the four
  reasons a state is not compared, the baseline and candidates, the empty window, and the truncated
  list.
- Export: the reading line, the pins, the held-without-text note, the panel's counts, rights,
  snapshot, each item and each exclusion, the three save buttons, the nothing-pinned sentence and
  the two sentences a format refused or a failed composition says.
- Every census envelope of the three screens through its views, with the export panel empty and
  with every article pinned, and each idle page: 17 renders, all byte-identical before and after.
  The paths the census does not reach (two different states compared, a radar row with a baseline,
  a list of candidates) are held by the screens' existing tests to their exact markup.
- The French draft covers every new entry. Trust and Coverage's answer is laid out by the shared
  `Coverage` component, whose copy stays its own for now, and the refusal card's labels come next.

Every citation the journey's pages print verifies (PR #805). This is the launch contract's first
promise, with its stated evidence: "`verify` resolves every citation the product emitted in the
journey suite". PR #796 walked the served answers; this walks what the browser actually shows.
- Each run collects every permalink the answer prints (the code elements beginning with a slash,
  inside the live regions).
- Each permalink must be hash-pinned, in the grammar `V3CitationVerificationTests` uses.
- A step that cites (every screen but Trust and Coverage) must print at least one on an answer.
- Once the run's recording is closed, each permalink is asked of the API's `verify`. It must answer
  `digest_matches` for the very state the permalink pins, and for the article it names.
- Every quotation must carry, beside it, its text digest, its body digest, its official source and a
  permalink pinning its own article. The review of #805 found that the export composer quoted 49
  articles and cited only their state. The reading page and the export composer now share one
  evidence line under each quote (`QuoteEvidence`).
- The journey's summary line counts the citations verified in each run. All sixteen runs pass, and
  all sixteen with `--served-by-api` (2026-09-30). The fixture mount's runs verified 57 citations,
  each `digest_matches`: reading 50 (the state and its 49 articles), export 2, and one each for
  search, dossier, history, compare and radar. Search's five hits sit in one state, whose permalink
  is the answer's `resolve.identifier`.

No interface text on a live page bypasses the chrome table (PR #806, the completeness test the
French chrome plan asked for).
- `live-chrome-scan.test.mjs` compiles the pages a second time with the table swapped for a
  pseudo-locale, in which every letter of every entry becomes its fullwidth form. It then renders
  every page and every census answer of the seven answering screens, with the export panel fully
  pinned.
- Any ASCII letter left in a text node or a human-read attribute, once the answer's own data is
  taken out, is text a French table could never translate, and fails the test. Putting one heading
  back as a literal fails it.
- The first scan found:
  - the page title's suffix;
  - the live banner, a driver's draft that makes a claim ("not a release and not legal advice");
  - the locale navigation's label;
  - the language names in the selects ("French", "German", and "English", which the review of #806
    found hidden behind a page-wide exemption for the locale navigation's own language names);
  - the date and article-id placeholders.

  All of them are now in the table, and the French is drafted.
- Three surfaces keep their own copy and are listed in the test as the next slices: the refusal
  card (its sentences are the checkpoint list of #793), the `Coverage` component, and the evaluation
  card. An export's watermark and JSON are the file's content, not interface text.
- Headless Chrome no longer downloads components (`--disable-component-update`,
  `--disable-background-networking`) in the journey, `keyboard-walk.test.mjs` and the browser
  evidence harness. With a fresh profile per run, it had left 1,581 component packages (4.4 GB) in
  the temporary directory. That, with a reviewer's solution build, filled C: on 2026-09-30. With the
  flags, a 16-run journey leaves no package; the two journeys before it, without the flags, left 39.

The evaluation card's words join the chrome table (PR #807).
- `EvaluationCardView` now takes every word from the table's `card` section: its heading, the "run
  over" line, the clean and not-clean summaries (counted by the language's plural rule), the verdict
  and control words, each table's caption and columns, and the controls, statistical rows and
  negative results.
- The card's own values stay the card's: its target sentence, set and gate names, figures, reasons
  and notes.
- The card rendered clean, and failing with one gate failed and one control missed, is
  byte-identical before and after. The Trust and Coverage page differs only in React's hydration
  boundaries.
- The chrome scan now covers the card as well. It exempts the card's values but not its verdicts
  (review of #807: the card's own "pass" had let a hard-coded "pass" through). What it still leaves
  out is the refusal card and the `Coverage` component. The French is drafted. The card beside the release assets belongs to the
  release pipeline.

The refusal card's words join the chrome table (PR #809).
- `refusal-card.mjs` exports `REFUSAL_CARD_COPY`, the card's words built from its own constants,
  so there is still one English source for the string renderer and the live pages:
  - the token label;
  - the retry sentence;
  - the absence note and its heading;
  - the three route labels;
  - the per-code notes;
  - the declared-null sentences;
  - the description of an offered state.
- The string renderer now builds its candidate line from the same words.
- `live-chrome.mjs` takes `refusalCard` from that export, and the French is drafted beside it.
- `RefusalCard` takes an optional `copy` prop (English by default, so the previews are unchanged),
  and every live screen passes the table's. The card now receives the codes behind each label
  (route codes, declared-null rows, the raw publication date and withdrawal). No rule moved:
  `validateRefusal` decides as before.
- The scan now renders every census refusal of the eight screens through its view. Putting one
  heading back as a literal fails it. The sentence a refusal says is the checkpoint list's (#793)
  and counts as data. So do the payload's member names, which the card shows as sent (driver
  decision below).
- All 18 census refusals render byte-identically before and after. The chrome scan now leaves out
  only Trust and Coverage's `Coverage` component.

Trust and Coverage's answer joins the chrome table, and the scan exempts nothing (PR #810).
- `coverage.mjs` exports `COVERAGE_COPY`: the page's headings, fact labels, captions, column names
  and yes/no, its five notes, and templates for its five sentences (the gaps, the operations
  served, a narrowed answer, an absent capability, an unserved capability). `COVERAGE_COLUMNS`
  gives each table's column order.
- The string renderer and `Coverage.jsx` both take their words from it. The two copies of every
  heading and caption that stood in both renderers are gone.
- The sentence functions take an optional copy, English by default. `Coverage` takes a `copy`
  prop, and the live page passes the chrome table's `coverageAnswer`. The French is drafted.
- The census coverage answer through both renderers, and the live page, are byte-identical before
  and after.
- The chrome scan now renders Trust and Coverage's answer too and has no exemption left: every
  word on the eight live pages, answers and refusals included, comes from the table. The only
  exceptions are data and each language's own name in the locale navigation.

The launch contract's accessibility and scope line held on the live screens (PR #811): "no meaning
by colour alone, linear diff, explicit dates, bracket tables whole".
- `live-a11y.test.mjs` works over every census envelope of the eight screens and each page as
  served:
  - Explicit dates: no relative date ("today", "3 days ago"), no month written out beside a number,
    no local numeric format, and every ISO date a real calendar date. Quotations of the law are
    left out, since their dates are the publisher's.
  - A linear diff: each compared language is one list, one row per article, its status in the
    table's words, with no side-by-side table and no inline colour.
  - Bracket tables whole (the product spec's rule 11): every quotation on the reading and export
    screens is the publisher's whole article text, in order, none dropped or cut.
  - No control takes a personal fact: every form has only the declared, labelled text fields, with
    no number, range or choice that could filter a bracket.
  - Truncating a quote to 200 characters, or adding a "Seniority" number field, fails the test.
- `journey.mjs`, in the real browser: every element painted apart from what is behind it must say
  what it is, in words or an accessible name. Painted means a background image, a background colour
  other than the one it sits on, a visible border, an outline or a shadow. Borders, outlines and
  shadows count since the review of #811 found an empty red-bordered span passed. Each run prints how
  many painted elements it examined. A wordless red square injected beside each search hit failed
  the search run (8 painted, 5 wordless).
- `paint-check.test.mjs` proves the check in a real browser against two fixtures:
  - four marks that say what they are pass;
  - four wordless marks (a background, a border, an outline and a shadow) are all caught.

  Removing the border clause fails it.

A reviewed interface language builds its own live pages (PR #813), so French can ship the day its
table is reviewed, with no other change.
- `live-chrome.mjs` knows the language its bundle was built for (`CHROME_LOCALE`, from esbuild's
  `define` of `__LEX_CHROME_LOCALE__`, English otherwise). `liveChrome()`, the plural rule and
  `livePath` default to it.
- A live page is labelled in that language (`Document`'s `locale` and `copyLocale`), and loads its
  script from that language's path.
- `build-live.mjs` builds English at the root as before, and every other reviewed language under its
  own path (`/fr/`), each page and its hydrating script compiled together for it. The locale
  navigation links a reviewed language to its home by file (`/fr/index.html`): both servers map only
  the root to an index page, and the review of #813 found `/fr/` was a 404.
- None but English is reviewed, so the product build is unchanged: its HTML pages are byte-identical,
  and its scripts differ only by the locale code they now carry. The journey passes with them, plain
  and served.
- `live-locale-build.test.mjs` builds a stand-in second language from the French draft, which the
  product never imports or serves. It holds:
  - every page and script under `/fr/`, labelled `fr`, saying the table's words and loading its own
    script, and each served by the live server, the language's home included;
  - the English pages byte-identical whether or not a second language is built;
  - no `fr/` directory from a product build.

  Its builds are tagged (`buildTag`), so their intermediate bundles never collide with another test
  file's build.
- To ship French after the owner's review: turn the draft into the reviewed table beside English in
  `LIVE_CHROME`, and add `fr` to `REVIEWED_CHROME_LOCALES`.

The eight live screens against the real bounded mount (PR #815). The data lane's first mount
(`C:\lex-v3\first-mount-decision95-restart-20260930\v3-corpus`, built twice and equal) is the
first real build the pages have met; until now every journey ran on the test fixture's mount.
- `journey.mjs --real-mount` runs the eight steps against a real build. Its contents are not known in
  advance, so each step asks the API its page's own request first (`expectedFromEnvelope`). The page
  is then held to that answer (a success, or that refusal by its code) and to every invariant a run
  checks: requests, headers, recording, citations, quotes, live regions, colour and hydration. The
  coverage page must name the corpus and Luxembourg index by the digests the mount's
  `build-report.json` records.
- On this mount the Luxembourg side holds no state (10 members: 1 acquired, 9 unavailable), and the
  EU index holds 99 searchable English articles, which no live screen asks yet.
  - Trust and Coverage and the radar answer; the other six refuse (`language_not_available`,
    `identifier_unknown`) and show their cards.
  - Every web reader accepted the real build's answers.
  - All eight runs pass, and all eight with `--served-by-api`.
- The first run found a flaw in the citation rule of #805: the radar's empty window answered with
  nothing to cite, and the rule required one citation. Such an answer is now excused when the API's
  own answer holds nothing to cite (no search hit, no radar row) and the page says so. The page's
  word alone excuses nothing: the review of #815 showed a page could hide a hit behind "no hits".
  Now a page that says it is empty while the API's answer holds something fails, and every other
  answer of a citing step must still cite.

The release pipeline's image steps, rehearsed with no production credential (PR #821; item 7, which
the owner's proxy gave the web lane on 2026-09-30 while the data lane works on the populations).
`node web/scripts/image-rehearsal.mjs --mount <v3-corpus>` is one command that:
- builds the live pages;
- builds the one-server OCI image without a container daemon (`dotnet publish -t:PublishContainer`),
  with the API, the live pages under `v3-web` and the mount under `v3-corpus`, on the base image
  `Lex.V3.Api.csproj` pins by digest (chiseled Ubuntu, non-root user 1654). `LexImageWebRoot` and
  `LexImageMount` are inert unless a build names them;
- verifies what the image holds:
  - every blob named by the digest and size it carries;
  - the API, every live page file and every mount file in the app layer, byte for byte;
  - each file of the mount's build report at its recorded digest;
  - linux/amd64, a non-root user, the API as the entrypoint, and the base named by digest;
- signs the manifest digest with a rehearsal identity (an ECDSA P-256 key made for the run and never
  kept, over a container-signature payload that says it is a rehearsal) and verifies the signature,
  the digest and that label;
- removes the archive, its work directory and the publish directory, and records that they are gone.

On the real bounded mount: a 61 MB image of 6 layers (manifest
`sha256:1029adee894c12831e1abc2d5668af02f8ab4a85a13eb8d9d76159c05ecf7a0d`), 25 live page files and
6 mount files verified, the build report's 5 digests matched, the signature verified, everything
removed. `image-rehearsal.test.mjs` holds each check to fail on the image it must refuse:
- a layer whose bytes changed, a missing blob, and two manifests;
- a live page left out, a mount file that is not the one built from, and a digest that is not the
  build report's;
- a root user, another entrypoint, and a base not named by digest;
- another image's digest, a payload changed after signing, and another key.

Running the image and probing it (health, API, browser, privacy, security headers) is the next slice.

The image run and its zero-traffic probes (PR #825).
- The rehearsal's one command now runs the image before removing it, on this machine's WSL Ubuntu,
  with no daemon and nothing installed (`image-run.mjs`):
  - the layers are unpacked in the manifest's order into a root filesystem, whiteouts applied;
  - the API starts as the image says (user 1654, its environment, working directory and entrypoint);
  - it runs in a private mount namespace, its root filesystem read-only and `/tmp` a private tmpfs,
    as a hardened deployment runs it. `/proc` is read-only, and `/dev` is a read-only tmpfs holding
    only `null`, `zero`, `random` and `urandom`. Binding the host's whole `/dev` gave the container a
    writable `/dev/shm` the watcher did not see (review of #825). Before the API starts, the mount
    table under the root is checked: any writable mount but the watched `/tmp` and those devices
    refuses the start.
- The eight live screens are then probed against the image, served by the image itself, through the
  journey's real-mount steps (`journey.mjs` `run` now takes a server; the host API and the image are
  two). The probes:
  - health: it answers;
  - API: each page's request, answered from the mount it carries;
  - browser: every screen, hydration, live regions and colour;
  - security headers: CSP with `frame-ancestors 'none'`, HSTS, `Referrer-Policy: no-referrer`,
    `nosniff`;
  - privacy: nothing written after the first answer, on its output or its `/tmp`, which an inotify
    watcher in the same namespace records, so a file written and deleted counts. Nothing can be
    written outside `/tmp`: the start proves the root filesystem refuses a write.
- On the real bounded mount all eight probes pass (Trust and Coverage and the radar answer; six
  screens refuse and show their cards). At startup the API wrote 11 events on `/tmp`, none after its
  first answer. The container's root filesystem is removed with the rest.
- A deployment requirement it found: the image needs a writable, private `/tmp`. Mounting a corpus
  verifies its index into a private temporary file (`LuxembourgIndexBuilder`, deleted on dispose). On
  a fully read-only filesystem the mount is refused and every answer is `no_corpus_mounted`.
- Not yet reproducible then: two builds of the same sources and mount gave different manifest
  digests. PR #826 makes the image reproducible (below).

The reproducible image (PR #826).
- Why two builds differed: every file in them was byte for byte the same, but the .NET SDK stamps
  the config's `created`, the app layer's history entry and every file's modification time with the
  time it ran, and names each pax header after its own process id.
- `image-reproducible.mjs` rewrites only what the SDK stamped:
  - the app layer is written again from the same entries (path, kind, mode, owner, bytes), sorted by
    path, each at the source date, with no pax header unless a path needs one, and uncompressed;
  - the config's `created` and the app layer's history entry take the source date;
  - the manifest and index name the new blobs; the base layers are kept byte for byte.
  The source date is `SOURCE_DATE_EPOCH`, else the commit's time, and the report records the commit
  and whether the tree was clean. The repack refuses an entry that is neither a file nor a directory,
  a layer that does not match its diff id, and a history that does not map one entry to each layer.
- Each build restores and compiles every project afresh in `artifacts/image-rehearsal` inside the
  checkout, emptied before and after, with source paths mapped (`ContinuousIntegrationBuild`). The
  first layout gave each build its own directory outside the checkout, and the rehearsal's own check
  refused it: 8 assemblies and symbol files differed, because the compiler writes the paths of its
  generated sources and symbol files into each assembly and maps only paths under the checkout.
- The rehearsal builds the image a second time from scratch and requires the same manifest digest.
  `reproductionFailures` names what differs, down to the files; `--no-reproduce` skips the check.
- On the real bounded mount, at a clean commit: manifest
  `sha256:600bbf106510837f779fe86fb2c7a6ce4595979a80dd568641bc5c4fe0c3766c`, reproduced by the second
  build. A second run on the same commit gave the same digest: four builds, one image. The checks
  passed (25 live page files, 6 mount files, the report's 5 digests) and the signature verified. All
  8 probes pass against the reproducible image, and the work directory, the artifacts directory and
  the container were removed.

The image probed on the journey's fixture mount (PR #828). On the real bounded mount six of the
eight screens refuse, because Luxembourg holds no state there. The answers themselves (hits, quotes,
citations) had been probed only against the host API, never against the image.
- The rehearsal takes the fixture mount `V3JourneyMountTests` writes (`journey-mount.json`) as well
  as a real mount. `mountReport` reads either:
  - the fixture's Luxembourg index is held to the file digest it names;
  - its corpus digest is a snapshot digest (what the coverage answer shows), not the file's bytes, so
    the corpus file is held byte for byte to the mount, and its snapshot digest to the coverage probe.
- On a fixture mount the probes are the journey's fixture steps (`fixtureMountRuns`, the expectations
  `journey.mjs` now keeps in one list, `fixtureMountExpectations`): every screen must answer with the
  texts the fixture's one work gives it, and every citation must verify.
- Against the image: all eight screens answer, and 105 citations verify through the image's own
  `verify`, each `digest_matches`: reading 50, export 50, and one each for search, dossier, history,
  compare and radar. The image was reproduced by its second build
  (`sha256:441853466bf9945f7f61db5ea0ae86f97aecf11c6dc26e8f8c5de215aca3b044` at that commit), and
  everything was removed.
- A mutation: a fixture manifest naming another corpus digest fails the coverage probe ("the page
  does not show the mounted digest").

V2 absent from the image (PR #831), the launch contract's last machine-gates item that is the
release pipeline's. V2 is the retired product on `main` (`Lex.Ask` to `Lex.Web`).
- Before signing, the rehearsal checks that V2 is absent (`v2Failures`):
  - no layer holds a Lex assembly, symbol or documentation file that is not `Lex.V3.*`;
  - the API's dependency manifest (`Lex.V3.Api.deps.json`) names no Lex library that is not V3's, so
    a V3 assembly that referenced V2 would be caught;
  - a missing dependency manifest is itself a failure.
- With the probes, the running image is asked every route V2 served (`V2_ROUTES`, 63): each
  `MapGet` and `MapPost` of `Lex.Web` on `main`, the ten pages of its `/built` table, its diagrams,
  its publisher document routes, its `/mcp/{*rest}` fallback, its static files and its four assistant
  endpoints. Each must answer 404, except where V3 serves the same path from its live pages (`/`),
  which must answer V3's own file byte for byte. The first list left out `/built` and its table
  (review of #831).
- On the real bounded mount: 526 entries across the 6 layers read, no failure; all 63 routes asked,
  `/` answering V3's own page and every other 404; the 8 probes pass; the image reproduced by its
  second build; everything removed.

The release assets published, read back and verified, and the evaluation card at its stable route
(PR #833): the release path's first line without its production credential, ruling 2's card
beside the release assets, and 36 s6's card for machines at a stable route.
- The card is served as JSON at `/evaluation-card.json`, the same card the Trust and Coverage page
  renders, and the page links it ("The same card for machines, as JSON"; drafted in French). The
  live build writes it beside the pages, and the API's web root now serves `.json`
  (`application/json`). The probes fetch it from the running image and require the bytes the
  release carries and signs.
- Once everything else has held (build, verification, V2 absence, signature, reproduction, probes),
  the rehearsal publishes a release into a directory named by its version
  (`v3-rehearsal-<source date>-<commit>`), which is never overwritten (`release-assets.mjs`):
  - `lex-v3-image.oci.tar` (the image) and `lex-v3-image.sig.json` (its signature);
  - `evaluation-card.json`: the card the image serves at its route. The rehearsal reads the
    platform's card once, checks it by the page's rules, hands it to the live build and holds the
    served file to it;
  - `mount-report.json`: the mount's build report, or the fixture's own manifest;
  - `release-manifest.json`, naming the version, the source commit, the image's manifest digest, the
    corpus digest and each asset by size and SHA-256, and `release-manifest.sig.json`. One key for
    the run signs the image and the manifest.
- Reading back (`releaseFailures`) trusts nothing the directory says about itself:
  - the key is the caller's: a manifest or image signed by another key fails, even one re-signed
    after a change;
  - every asset is hashed again: nothing unlisted, nothing missing;
  - the image is read blob by blob and must be the digest the manifest names, and its signature must
    name that digest;
  - the card must be the very card the image serves (`app/v3-web/evaluation-card.json` in its app
    layer), and read by the page's rules;
  - the directory, the manifest's version and the source it signs must name each other.
  The review of #833 found the last two were checked alone. A release could carry a valid card the
  image does not serve, or a version naming another commit than its signed source.
- On the real bounded mount: release `v3-rehearsal-20260930T213833Z-23d939b6e63c`, six files, read
  back with no failure, after the image was reproduced, V2 found absent, the 8 probes passed and the
  image served the release's card at `/evaluation-card.json` (200, `application/json`, the same
  bytes).
  Tampering with a kept release was caught:
  - a card with a statistical row rewritten to `pass` failed its size, its hash and the card's rule
    (Decision 92);
  - one flipped image byte failed the asset's hash and the blob digest of layer 3.
- Not in this step: building the corpus from custody in the same command, which is the data lane's
  build (`Lex.V3.Tool build`), and publishing anywhere but a local directory, which needs the owner's
  release identity and storage.

The credential-free deployment kit (PR #890): the owner's go-live as one command, the panel's item 3.
Nothing here logs in, deploys or signs with a production identity; those stay with the owner.
- **`deploy/main.bicep`** (built to `deploy/main.json`) is the one-server container as an Azure
  Container App in an existing managed environment:
  - the release image by digest, pulled with the user-assigned managed identity the owner names, with
    no secret and no registry password;
  - one replica, with a private writable `/tmp` (an EmptyDir volume: the image run's finding that
    mounting a corpus writes its index copy there);
  - liveness on `/`, readiness on `/evaluation-card.json`, port 8080, HTTPS only;
  - every revision kept (multiple revision mode):
    - a new revision arrives as the `candidate` label with no traffic, while `liveRevision` keeps 100
      per cent;
    - on a first deployment there is no live revision, so the candidate takes the traffic;
  - until the owner opens the site, ingress admits only the ranges every deployment names
    (`allowedSourceCidrs`), and none at all when it names none; after the opening, `openToPublic`.
- **`deploy/deploy.ps1`** is the one command.
  - Without `-Apply` it only plans: it verifies the release and prints every command, changing nothing.
  - With `-Apply`, in order:
    1. it verifies the release under the signing identity's public key, and stops if it does not
       verify;
    2. it copies the image out of the release by its manifest digest with `oras`, and checks the
       pushed digest. The short-lived registry token goes from `az acr login --expose-token` on
       standard input to `oras login`, into a registry config in a fresh private directory, which
       `oras cp` reads and which is removed when the copy ends, whatever happens. It is never on a
       command line or printed. `oras cp` takes no password on stdin (review of #890); the custody
       probe's runbook did the same;
    3. it runs `az deployment group what-if`, then `create`, by digest, with the private copy's ranges
       (`-AllowedCidr`) or, after the opening, `-Public`;
    4. it reads back who ingress admits, sets the ranges again once if they differ, and stops if they
       still do;
    5. it probes the candidate's own URL;
    6. if the probe fails, it deactivates the candidate (the removal step) and exits non-zero.
  - Promotion is printed for the owner and never run: a traffic move.
  - `-Remove -Revision <name>` is the removal step on its own.
  - `-Open` is the owner's opening: it removes the address rules, reads back that ingress admits
    everyone, and only then says the site is open.
- **`web/scripts/deploy-probe.mjs`** is the zero-traffic probe. First it reads the release back
  under the signing identity's key (`releaseFailures`). Then, against the revision's URL:
  - `/` must be the release image's own page byte for byte, with CSP `frame-ancestors 'none'`, HSTS,
    `Referrer-Policy: no-referrer` and `nosniff`;
  - the card at `/evaluation-card.json` must be the release's, byte for byte, as JSON;
  - `coverage` must answer from the release's corpus;
  - every V2 route must answer 404.
  - Without `--origin` it only reads the release back, which `deploy.ps1` does before touching Azure.
- **`deploy/validate.ps1`** checks offline that `main.json` is `main.bicep`'s build, that `bicep
  lint` is clean, and that `deploy.ps1` parses. The static rules run in CI in
  `web/test/deploy-kit.test.mjs`:
  - no secure parameter, secret, key listing or registry password;
  - the identity pull, one server, `/tmp`, and the zero-traffic candidate;
  - the script never logs in, never puts a token or password on a command line, keeps the registry
    token only in the private session config it removes, and never promotes;
  - the release is verified before any Azure command, and the image is deployed by digest.
- **Evidence:**
  - the probe passes a stand-in revision that answers as its release holds, and fails nine ways a
    revision can differ, each with its own reason;
  - it is never run against a release under another key;
  - the probe command's exit codes (0, 1, 2) are tested as a process. The kit's first plan run found
    the command guard running nothing and exiting 0, which would have let an unverified release
    deploy;
  - a plan run of `deploy.ps1` against a published release printed the verified release, the digest
    copy, what-if and create by digest, and the probe. Under another key it now stops at
    verification;
  - mutations fail their checks: a promotion that runs fails the static rules, and an edited
    `main.json` fails validation.
- **Runbook, for the owner:**
  1. Provide what the kit does not create:
     - the managed environment;
     - the registry;
     - a user-assigned identity with AcrPull on it;
     - the production signing key (ECDSA P-256, in your key vault), the identity it signs as, and its public key in
       PEM. The release is built with `--signing-key - --signer <identity>` on the release command, the key piped
       from `az keyvault secret show ... --query value -o tsv`.
  2. `az login` in your own session, then `pwsh -File deploy/validate.ps1` (it needs the Bicep CLI;
     no Azure is touched).
  3. Plan: `pwsh -File deploy/deploy.ps1 -Subscription <id> -ResourceGroup <rg> -EnvironmentId <id>
     -Registry <name>.azurecr.io -IdentityResourceId <id> -SigningPublicKey <identity's public key,
     PEM> -SignerIdentity <the identity it signs as> -Release <release directory>`, with `-LiveRevision <the revision
     serving now>` (none on a first deployment), and until the opening `-AllowedCidr <your address>/32` (a reviewer's
     too, comma-separated, each /24 to /32); after the opening `-Public` instead. `-Apply` requires `-SignerIdentity`,
     so only a release signed by the release signer is deployed.
  4. The same command with `-Apply` deploys the candidate and probes it. On "answers as the release
     holds", promote with the command it prints. On a failed probe the candidate is already
     deactivated.
  5. Remove a revision at any time with `pwsh -File deploy/deploy.ps1 -Subscription <id>
     -ResourceGroup <rg> -Remove -Revision <name>`.
  6. On the opening day, and never otherwise: `pwsh -File deploy/deploy.ps1 -Subscription <id> -ResourceGroup <rg>
     -Open`, then `-Public` on every deployment after it.
- **Not covered:**
  - the production signing key itself (the owner's, in their key vault);
  - a custom domain, DNS and monitoring;
  - an Azure-side check of the template, since `what-if` needs the owner's session.

The deployment kit's browser probes and rollback (kit 2, PR #902, first opened as #901), toward the launch contract's lines "zero-traffic
deploy with health, API, browser, privacy and security probes" and "rollback and forward again".
- **`deploy-probe.mjs --browser`** runs the journey's real-mount steps (`realMountRuns`, the same ten
  steps the image run and the real-mount journeys run) through Chrome or Edge against the candidate's
  own URL, after the HTTP probes pass:
  - the release's mount report stands as the build report the steps read, from a temporary directory
    removed when they end;
  - each page is held to what the API answers its request, its hydration, console, paint and security
    headers, with every citation verified;
  - the revision is reached through a remote stand-in (`remoteRevision`) that exposes no process.
- **Privacy is stated, not passed.** A deployed revision's process output and files are not the probe's
  to watch, so its privacy checks would read nothing and prove nothing. The probe prints
  `REMOTE_PRIVACY_NOTE` with every browser run instead: the image run's privacy probe (nothing written
  after the first answer, no query text, address or user agent recorded) held the same image digest.
- **`deploy.ps1`** passes `--browser` unless `-SkipBrowserProbe`, and says so when skipped. With
  `-LiveRevision` it prints, after promotion, the rollback to the live revision (kept, since every
  revision is kept) and forward again to the candidate: traffic moves, the owner's, printed and never
  run.
- **Evidence:**
  - against the API started locally on the bounded EN/FR canary mount
    (`C:/lex-v3/bounded-en-fr-canary-20260930-1/v3-corpus`) with the built live pages, the browser
    probes passed all ten steps: coverage, search, dossier, reading, compare, radar, export, history,
    EU search and EU dossier, with 0 failures;
  - the tests hold that a failing step is reported with its label, no local API is started, the
    release's mount report is what the steps read and is removed afterwards, the revision serves its
    own pages (so the security headers are held), and the remote stand-in's empty privacy record comes
    with the note;
  - the static rules hold the `--browser` default, the stated skip, and rollback and forward printed
    inside `Write-Host` only.
- **Runbook, added:** step 4 now also runs the browser probes, which need Chrome or Edge on the owner's
  machine (or `-SkipBrowserProbe`, which runs only the HTTP probes and says so). After promotion,
  roll back or go forward again with the commands the script prints.
- **Not covered:** a second revision, promotion, rollback and V2's retirement on Azure itself, which are
  the owner's go-live; privacy on the deployed revision beyond the image digest it shares with the image
  run.

The licence-blocked journey (PR #834), the launch contract's "one licence-blocked journey" for its
line "rights are enforced at compose time".
- `V3JourneyMountTests` also writes a licence-blocked mount when `V3_WRITE_LICENCE_BLOCKED_MOUNT`
  names a directory. It is the fixture mount with its member's rights recorded as
  `non_admitting_licence_scl`, and its `journey-mount.json` names that disposition and passages
  covering every article's whole body: windows of 40 characters every 20 (2,950), so any leaked run
  of 60 characters or more holds a whole window. The label and first paragraph marker are left off.
- `journey.mjs` runs the eight steps on it (`licenceBlockedRuns`, chosen when the mount names a
  rights disposition):
  - each page is held to what the API answers its request;
  - reading and export must refuse `text_withheld` (`mustRefuse`), whatever the API answered, and
    show it on the page's refusal card (the code read off `.refusal-card .refusal-code`). Every
    refusal step is now held to the card's own code, not to a mention of it anywhere on the page;
  - no page may show any passage or carry one in its markup (`absentTexts`, the page's text and its
    HTML, whitespace collapsed).
  The review of #834 found that the first version checked only each article's opening, and took any
  mention of `text_withheld` for the card.
- On the host API all eight pass, plain and with `--served-by-api`:
  - reading and export refuse `text_withheld`;
  - coverage, search, dossier, compare, radar and history answer, and none shows or carries a
    passage;
  - the citations on the answering pages verify.
- The check is not blind: run against the normal fixture mount, all 2,950 windows are found on the
  reading and export pages. The normal journey's 16 runs pass under the card-code rule.

The EU permalink grammar and EU `verify` (PR #850), the first slice of the EU half of the launch
screens. Until now EU hits cited `expressionIRI#lex-provision=NNN`, which pins nothing, so the live
screens refused EU answers by design.
- The grammar is in Luxembourg's family, with the language part of it from the start, so the French
  expressions (Decision 89) need no second grammar:
  `/eu-eurlex/{celex}/{language}/{wording date}--{wording sha256}#{provision}`. The stable
  coordinate is the permalink without its digest; the provision is the publisher's id, escaped.
- The wording digest is computed in the API from what the EU index holds, which stores none
  (`EuropeWordingDigestRule`). It is the SHA-256, under `lex-v3-eu-wording/1`, of the CELEX, the
  work and expression IRIs, the language, the wording date and every article identity in the
  publisher's order, each field length-prefixed. Each EU article identity is itself the SHA-256 of
  the article's text and tokens (`EuFormexMainBodyLegalContentProducer.IdentityOf`), so the digest
  pins the whole held wording. An expression without exactly one wording date gets no permalink.
- EU `search` answers carry `pinned_wording` (wording date, digest, permalink, the rule) and each
  hit its provision permalink.
- `verify` routes an EU permalink to the EU index:
  - `digest_matches` with the CELEX, work and expression, language, wording date, digest, stable
    coordinate and the provision coordinate EU `resolve` answers;
  - otherwise `pinned_digest_mismatch` naming the current digest, `anchor_not_in_version`,
    `language_not_available`, `identifier_unknown` (a work or wording date not held), or
    `no_corpus_mounted`.
  - The CELEX slot holds the work's CELEX and nothing else. The review of #850 found an article
    identity there resolved the same work and verified. Only expressions of the work whose CELEX is
    exactly the slot's are considered, and the wording's CELEX is checked again.
- On the GDPR fixture a hit's permalink verifies (also under the product's https origin), and the
  digest recomputed from the index by the stated rule equals the API's. Each refusal is held to
  its code.
- On the real bounded first mount, all 60 hits for "personal data" carry a permalink, and every one
  verifies as `digest_matches`.

The search screen reads EU answers (PR #853), the second slice of the EU half of the launch
screens. Until now the screen refused every EU answer as not Luxembourg's.
- The form gains an optional work identifier, sent as typed (as the dossier screen sends it), and
  only when the field is not blank. An EU work is searched by naming it: the platform serves no
  search across EU works. The next page stays within the work named.
- `readEuropeSearch` (in `search-answer.mjs`, beside the Luxembourg reader, with the lane, page and
  population rules both share) holds an EU answer to the rules it states about itself:
  - one work, one expression and one language, and no date (`requested_date` null, no ambiguous
    work);
  - every hit in the one wording the answer pins: the wording permalink
    `/eu-eurlex/{celex}/{language}/{wording date}--{wording sha256}` agrees with the wording date,
    the digest and the language asked, and each hit's permalink is it with the provision, escaped as
    the platform escapes it (RFC 3986);
  - the EU cursor `lane.article`, the population scope echoing the request, and one work with hits;
  - the hits' own work and expression IRIs are one work and one expression. The review of #853 found
    a hit of a second work read as a one-work result, because only the population's count was
    checked.
- An answer with hits and no pinned wording is refused rather than shown unpinned: a hit this page
  shows must pin its wording (driver decision, below).
- The screen says the pinned wording once above the hits, with the answer's own sentence on what the
  wording date is. Each hit gives its heading (marked in its language), the CELEX and the wording
  date, never "version" or an applicability date, and the printed permalink. What the search does
  not cover (later wordings, corrigenda not applied, other languages, no article text) is listed as
  the answer lists it.
- The censuses now hold EU search answers driven on the GDPR fixture mount:
  - the answer census has four (both lanes for "joint controllers", the first one-hit page, the page
    its cursor leads to, and no hit);
  - the envelope census has three (the answer, a language the work is not held in, and an EU
    identifier the index does not hold).
  The refusal sentences for `identifier_unknown` and `ambiguous_identifier` have French drafts.
- The captured answer from the real bounded first mount (60 hits for "personal data", three on the
  page) reads too.

The EU search journey step (PR #854), the third slice of the EU half of the launch screens, and
the panel's "journey on the real mount's GDPR".
- A ninth step, `EU_SEARCH_STEP`, is the search page asked for the GDPR by its CELEX. It types
  "personal data" and `32016R0679` and chooses English in the form's language select, since the held
  wording is English. The request body must be exactly the phrase, the language and the work.
- The verdict reads EU citations. `pinnedCitation` accepts the EU permalink grammar beside
  Luxembourg's. `verify` must answer for the EU index, name the very wording the citation pins
  (`wording_sha256`) and the provision it names, unescaped.
- The keyboard path picks from the closed select as a keyboard user does: Tab to it, then type the
  option's first letter. Then Tab on to the submit button and press Enter. The select counts among
  the fields Tab must reach.
- Where it runs:
  - the fixture runs add "eu search, with the fixture mount", which must show the refusal card
    `no_corpus_mounted` saying the EU index is the one missing;
  - `--real-mount` adds the step when the build report names an EU index (`realMountSteps`).
- On the real bounded first mount, the EU step ends in the answer with 61 citations: the wording's
  permalink and the 60 hits' permalinks, every one `digest_matches` by `verify`. That holds in both
  runs, by pointer and by keyboard (24 of 24 characters typed by key). The other eight steps behave
  as before on that mount.
- The fixture journey passes 17 of 17 steps, by pointer and by keyboard.

The EU dossier on the live screen (PR #856), the fourth slice of the EU half of the launch
screens. Until now the dossier screen said an EU dossier was not shown.
- The API: each expression of an EU `dossier` answer carries `pinned_wording` (wording date, digest,
  permalink), the same wording EU search pins. It is null when the expression holds no single
  wording date. The answer carries the `digest_rule`. The EU index is untouched.
- `readEuropeDossier` (in `dossier-answer.mjs`, beside the Luxembourg reader; `readDossierAnswer`
  dispatches on the publisher):
  - every expression is in a language the work is held in, and in the language asked when one was;
  - each is listed once, counted, and resolvable by its own IRI;
  - each is pinned by `/eu-eurlex/{celex}/{language}/{wording date}--{digest}`, whose date is its
    one wording date. An expression that pins no wording is refused rather than shown unpinned, as
    in EU search.
- The screen shows the CELEX, the work IRI, and a table of expressions: language, wording date
  (never "applies from"), articles held, permalink. It then gives the answer's sentence on what the
  wording date is, and what the dossier does not hold. The `not_shown` state is gone. The new
  refusal sentence for `ambiguous_identifier` (one CELEX naming two works) has a French draft.
- The censuses hold the EU dossier: one answer in the answer census and one envelope. The EU
  members' object references move per run, as the Luxembourg ones do, so they are normalised; the
  double-run test found them.
- The journey gains `EU_DOSSIER_STEP` (the dossier page, the GDPR by its CELEX):
  - on the real bounded first mount it answers, and its one citation (the expression's wording
    permalink) verifies, by pointer and by keyboard; all 10 real-mount steps pass in both modes;
  - on the fixture mount it shows `no_corpus_mounted` naming the EU index; the fixture journey
    passes 18 of 18 steps, by pointer and by keyboard.

The data that would serve each unserved operation (PR #857), the driver decision recorded for
STATUS item 4.
- `coverage` answers `operations.not_served_data`: one row per registered operation with no
  route, in the order of `not_served_operations`, each naming the data that would serve it
  (`V3CorpusMount.NotServedDataNeeded`). The data are the specification's own (`33-product-spec.md`):
  - `as_observed`: observation times (`observed_from`), recorded by builds chained to their
    predecessors in the event log;
  - `knowable_on`: each state's publication date beside its observation time, never the
    publisher's valid-from date;
  - `concepts`: EuroVoc descriptors, EU directory codes and subject matters;
  - `transposition`: Legilux's transposes and draftTransposes assertions and the Publications
    Office's national implementing measures for Luxembourg, each kept as its publisher asserts it.
  None of these is produced by the ingest.
- A coverage test holds the table to exactly the registered operations the mount does not serve.
  A newly served operation leaves it, and a newly unserved one cannot be listed without saying what
  it needs.
- The coverage reader requires one row per unrouted operation, in the same order, each with a
  sentence.
- Trust and Coverage shows the rows in a table, in both renderers, captioned "The data that would
  serve each operation with no route". The words have French drafts. The preview answers carry the
  platform's sentences to the character, and the census holds them.

The timeline speech test per publisher (PR #860), the evidence the launch contract names for its
promise "Luxembourg applicability dates and EU wording-state dates are never merged, in text or in
speech". Before the EU screens there was nothing EU on a page to merge. `web/test/date-speech.test.mjs`
holds the two date kinds apart in three places:
- **What the platform sends:** no EU answer in either census carries a Luxembourg state's date
  fields (`applicability_date`, `next_applicability_date`, `latest_applicability_date`,
  `history_begins`, `state_sha256`), and no Luxembourg answer carries an EU wording's
  (`wording_date`, `wording_dates`, `pinned_wording`).
- **What the interface table says**, in English and in the French draft:
  - the entries the EU views use carry no Luxembourg date word ("applies", "version", "state",
    "s'applique");
  - no other entry, page introductions apart, carries an EU one ("wording of", "Wording date",
    "dated {date}", "libellé du", "Date du libellé").
  "Wording" and "libellé" alone stay Luxembourg words too: an article's wording changes from state
  to state.
- **What each screen says and a screen reader hears**, for every census answer on all eight
  screens: the text and the human-read attributes, with the answer's own values taken out, use only
  the publisher's own date words. Each publisher's screens are seen saying their own words. The
  platform's sentences are data here, so an EU answer saying its date is never merged with a
  Luxembourg applicability date is the promise kept.
- **Mutations:** each is caught.
  - An EU hit "version of" fails the table check, the screen check and the borrowed-render check.
  - A Luxembourg hit "wording of" fails the table and screen checks.
  - An `applicability_date` added to the EU dossier census answer fails the field check.

The launch contract's first promise over EU answers (PR #860): "verify resolves every citation the
product emitted". `V3CitationVerificationTests` walked only Luxembourg answers. It now also walks the
answers the EU screens read, on the GDPR fixture: search in one work, and the dossier.
- The citations by role (each `permalink`, and any string that is exactly an EU permalink) must be
  hash-pinned EU permalinks. Each must verify as `digest_matches` for the very wording it pins
  (`wording_sha256`), and for its provision, unescaped.
- An EU `resolve.identifier` is the coordinate EU `resolve` answers (a provision or an expression),
  not a pinned citation, so it is asked of `resolve` and must answer.
- A mutant that drops the dossier permalink's digest is caught ("not a hash-pinned EU permalink").

The refusal set measures EU `verify` (PR #860). On a mount with an EU index, when the work's
expression holds one wording date, the mounted refusal set derives three more requests. The wording
digest is recomputed from the index rows by the stated rule, so `verify` answering also checks the
API's digest:
- a pinned provision: answered;
- a digest the wording does not have: `pinned_digest_mismatch`;
- a provision the wording does not hold: `anchor_not_in_version`.
On the real bounded first mount the refusal set is 8 cases (5 before), passing, with the shuffled
control caught; those two codes are no longer listed as not produced there. A mutant that lets EU
`verify` answer a provision not held fails the gate.

The retrieval set measures EU resolver exactness (PR #860). For each sampled EU work whose
expression holds one wording date, the retrieval set adds:
- its first three provisions' permalinks (the digest recomputed by the stated rule): exact cases,
  each judged to resolve to exactly that provision (`verify` names the CELEX and the provision);
- a provision the wording does not hold: a near miss, judged to find nothing.
On the real bounded first mount the card's retrieval set is 13 cases, and every gate is measured
and passes: anchor nDCG@10 1 over 9, no-hit accuracy 1 over 4, resolver exactness 1 over 3. The
judgments control now applies and catches the shuffle. Only the temporal arms are not measured
there, since the mount holds no Luxembourg state. A mutant whose EU `verify` names the first
provision for every request fails the fixture's resolver gate.

The release rehearsal over the real bounded first mount (2026-10-01, recorded by PR #862),
`node web/scripts/image-rehearsal.mjs --mount C:/lex-v3/first-mount-decision95-restart-20260930/v3-corpus`
at the head of PR #860. Every step passed:
- the one-server image (72,561,664 bytes) holds the 26 live page files, the 6 mount files and the 5
  files the mount's report lists, byte for byte, with no image failure;
- two builds from scratch give one manifest, `sha256:b330577295f5a4017715420733ff7351e9134b8d36cc917a690db9e1288ff9d6`;
- V2 is absent: 527 entries scanned, and none of V2's 63 routes fails when asked of the image. A
  V2-only route answers 404, and a path V2 and V3 share (such as `/`) answers with V3's own bytes;
- the rehearsal signature verifies;
- the image runs in WSL and is probed on all ten journey steps:
  - coverage and radar answer;
  - the six Luxembourg screens show the refusals the API gives, since the mount holds no
    Luxembourg state;
  - the EU search answers with 61 citations verified, and the EU dossier with 1;
  - no step fails;
- the card served at `/evaluation-card.json` is the release card, the gates over this mount:
  - refusal: 8 cases, pass, the shuffle caught;
  - retrieval: 13 cases, every gate passing, the shuffle caught;
  - temporal: not measured, since the mount holds no Luxembourg state;
- the release (version `v3-rehearsal-20261001T033235Z-523f28f8e84b`, manifest
  `7e6a2f08fefc8699d8e5c317073a206fff9145f0d4c123534e00d1ab3cc34681`, six assets) is published,
  read back and verified with no failure;
- the work directory, the artifacts and the container are removed.

Predecessor chaining, the first slice (PR #864): Luxembourg index schema `lex-v3-luxembourg-index/7`,
`user_version` 7. It still holds a genesis log, now in the shape a chained log needs. The changes are
confined to the event-log parts of the builder and the reader.
- `events` drops `UNIQUE(scope, key, event)`, since a replaced file can recur for one state's key;
  `seq` stays the key.
- A new `observations` table: one row per build that wrote events. Each row holds:
  - its corpus;
  - the index it carried forward (null for the first observation; a check makes this exact);
  - the events it appended, by sequence number;
  - its observation time, which a check holds null because none is held.
  A genesis log has one observation numbering every event (an empty log numbers none: first 1,
  last 0).
- A new `log_stamp`: the log's own schema, `lex-v3-event-log/1`, and the digest of exactly its
  observations and events. A later build can then verify a predecessor's log, which it will carry
  forward unchanged, across a change to the index's other tables.
- Each `first_sighting` names the digests of the publisher bodies its state's articles were read
  from (`source_body_sha256`, sorted, each once). With them a later build can tell a replaced file
  from an unchanged one by the log alone. The index's own tables hold no body digest, so:
  - the reader recomputes the whole log from the states and the bodies the log names, and refuses
    any other row, observation or stamp;
  - whoever holds the corpus checks the bodies against it (`VerifyEventLogSources`): the build, the
    mount at open, and the mount writer's verification.
- Tests:
  - the genesis log's shape;
  - seven observation and log-stamp tampers, each refused with the logical-rows stamp recomputed,
    so only the new checks can refuse;
  - a body the state was not read from passes the index alone and is refused by the corpus, by the
    mount and by the mount writer;
  - a body named twice is not the genesis log.
- The fixed-input byte pin moves, as a schema change moves it. The schema version and the pin are
  shared with the data lane.

Predecessor chaining, the second slice (PR #866): a Luxembourg index build can carry its
predecessor's event log forward.
- `LuxembourgIndexPredecessor.TryRead` reads the previous build's index:
  - its bytes must be the digest its build report names;
  - it must hold a log of this log schema (`lex-v3-event-log/1`), whatever its other tables'
    schema. An index of schema 6 or before has no log stamp and refuses
    `predecessor_schema_differs`;
  - the log must match its own stamp and be numbered as observations appending events, or it
    refuses `predecessor_mismatch`.
- `TryBuild(envelope, predecessor)`:
  - copies the predecessor's observations and events unchanged, an exact prefix (G3a);
  - appends one observation naming the predecessor's digest, with an event for each state the log
    does not hold: `first_sighting`, or `expression_added` when the log holds the same work and date
    in another language;
  - leaves a state the log holds unchanged silent, and a state the log holds that this build lacks
    silent too (absence is not a withdrawal);
  - refuses `predecessor_state_changed` for a held state with another digest or other source bodies,
    until slice 3 adds `file_replaced`.
  The genesis log is now the special case: the events appended to no log.
- The reader checks any log:
  - it is numbered as observations appending events;
  - its last observation is of the index's corpus;
  - every state is held by the log at its own digest;
  - the last observation's events are exactly those it must append to the log before it;
  - every observation's events are exactly those it appends to the log before it, replayed from the
    log alone (the states it leaves held). The review of #866 found that a carried genesis event
    renamed `expression_added` passed both the predecessor read and the reader.
  That the log before it is the predecessor's is proven by the build, which copies it.
- `VerifyEventLogSources` checks the corpus's bodies against the log's last word on each state.
- Plumbing: `V3FirstMountBuild.ReadPredecessor(directory)` and a `RunAsync` overload, with both
  builds of the index taking the same predecessor. `Lex.V3.Tool build --predecessor <v3-corpus
  dir>` reads and verifies the predecessor before the first request.
- The API described every log as a genesis log (`basis`, the predecessor and the notes) until
  PR #867, which serves `events` and `answer_drift` across the chain.
- On the fixture, a chained build is byte-stable across two builds, chains again (three
  observations), appends `first_sighting` to a predecessor whose log lacks the state, and refuses
  a changed state.
- The real bounded first mount's Luxembourg index is schema 6 with no state, so it cannot be a
  predecessor; the first chain starts with the next build.

Predecessor chaining, the third slice (PR #867): comparison events. A chained build compares its
states with its predecessor's log, key by key, over the keys the log holds and the keys it holds.
A held state this build lacks stays held: absence is not a withdrawal.
- **`file_replaced`** (G1): a held state whose source bodies differ. It names the state and bodies
  it replaces (`replaced_state_sha256`, `replaced_source_body_sha256`).
  - The state's digest is the permalink's version id, and it changes with the text, not with the
    bytes alone.
  - Through the real pipeline: the act rebuilt from its publisher file with one article reworded is
    `file_replaced` with a new digest, so a new permalink.
  - Rebuilt with one byte added and the text unchanged, it is `file_replaced` with the digest, and
    so the version, unchanged. **Owner question, asked in PR #867:** should a replaced file whose
    text is unchanged mint a new version? That would change the published permalink scheme.
    **Answered** by the panel on the owner's behalf (recorded by PR #871): no. Version identity
    follows the legal text, and the replacement is recorded as built.
- **`interval_closed`** and **`validity_revised`**, for a state the log held whose applicability
  interval this build's states move. The end is the next later date in the work and language,
  which is this derivation's, never the publisher's, so each event is marked `derived: true` and
  names `applicable_from`, `previous_to` and `new_to`.
  - The latest state, followed by a later one, closes.
  - A state whose next moved earlier, because one was inserted, is revised.
  - A held state this build lacks closes too.
- **The same bodies with another digest** are this derivation's change, not the publisher's: the
  build refuses **`predecessor_derivation_differs`**, which replaces slice 2's interim
  `predecessor_state_changed`.
- The reader's history replay (#866) covers the new events with no change: every observation's
  events are recomputed from the log alone.
- `withdrawn_from_source` and `resighted` stay unminted. They need the three-run rule (31-v3-spec
  §91(b)) and a proof of complete enumeration.
- The review of #867 found that a chained log broke the served event operations: `answer_drift`
  threw on any revising event, and `events` called every log genesis. So the fourth slice is part
  of #867:
  - `events`' log block names the basis (`genesis` or `chained`), the predecessor, the number of
    builds compared, and the **ancestor logs** whose cursors it honours, each to the last event it
    held;
  - a cursor of an ancestor reads on in this log with the same numbers, and one from any other log
    still refuses `snapshot_unknown` (a driver decision, below);
  - each row carries the log's detail verbatim, and a `file_replaced` row also carries the replaced
    permalink;
  - a chained log says what it holds (`chained_note`) and what it does not (`withdrawal_events`);
  - `answer_drift` enumerates its `interval_closed` and `validity_revised` events a page at a time.
    Each row is the dates of one work and language, from the revision's new end to its old one
    (open for the latest), whose as_of answer moved from the state before to the states applying
    from the first of them, each by its permalink, marked derived.
  On a mount whose chained log holds the act at an earlier date (crafted predecessor, real build),
  `events` and `answer_drift` show the `interval_closed` and its moved dates.

Predecessor chaining, the fifth slice, first part (PR #871): each build's time, the bound that
`as_observed` will give.
- The panel answered the chain's three owner questions on the owner's behalf (ANSWERS.md,
  2026-10-01):
  - G1: a replaced file whose text is unchanged mints no new version. Version identity follows the
    wording and text digests, so published permalinks keep resolving. The replacement is still
    recorded as `file_replaced` with both body digests, as #867 builds it.
  - `as_observed` may ship identified by a build snapshot. The answer must name the snapshot, give
    that generation's build time as an upper bound ("observed no later than"), and never state or
    infer an observation time. A time the snapshots cannot place refuses with an existing typed
    code.
  - Generations are retained by the launch contract's retention line (S7-A09):
    - every generation a published permalink or evidence bundle references, indefinitely;
    - nightly generations for 90 days;
    - one complete monthly keeper, indefinitely;
    - the retained depth reported truthfully.
  - The slice-4 decision (an ancestor's cursor reads on) stands.
- Index schema `lex-v3-luxembourg-index/8`, event log `lex-v3-event-log/2`: each observation records
  `built_at`, UTC to the second. It is hashed into the log stamp and carried forward unchanged by
  every successor. `observed_from` stays null, because a build time is not an observation time.
- The build time is a build input, not read inside the builder (a driver decision, below):
  - `LuxembourgIndexBuilder.TryBuild` takes it;
  - it must be a UTC whole second, later than the predecessor's last build and no earlier than the
    corpus's EU capture (the one observation clock the corpus holds), or the build refuses
    `build_time_invalid`;
  - nothing can check it against the Luxembourg fetches, whose times the corpus does not hold;
  - the reader requires each observation's time to be a real UTC second later than the one before.
- `V3FirstMountBuild` reads its clock once, after every fetch and derivation:
  - it rounds the time up to the next second, since rounding down could land before the last fetch;
  - it passes that one time to both builds of the index, so the twice-built comparison holds;
  - the Tool uses the system clock.
- `events` now serves `log.built_at` and each ancestor's `built_at`. The sentences that said no
  build time is held now say what is held:
  - `coverage` states none, in its `not_held` row and in the Trust and Coverage page's counts note
    (English and the French draft);
  - a build's time is not upstream health.
- An index on each event's work (`events_work_seq`) serves the per-work fold that `as_observed`
  reads.
- A `/7` predecessor refuses `predecessor_schema_differs`. No real mount was chainable: the real
  bounded first mount's index is schema 6.
- The second part is PR #874, below.

Predecessor chaining, the fifth slice, second part (PR #874): `as_observed` by build snapshot (G4).
- `POST /api/v3/as_observed` and its MCP tool. The request takes `identifier`, `date`, optionally
  `language`, and exactly one of `snapshot` (an index digest) or `at` (a time).
- The snapshot is one build of the mounted chain, named by its index digest: the mounted index
  (`events`: `log.log_id`) or an ancestor its log carries forward (`log.ancestors[].log_id`).
- The log is folded up to that build's last event, by a new per-work query over the
  `events_work_seq` index (its plan checked, never a scan). The state applying on the date is
  selected as `as_of` selects. A state once held stays held (absence is not a withdrawal), and the
  next date is the next one held at that snapshot.
- The answer names the snapshot: its digest, its observation number, its corpus and whether it is
  mounted. It gives `observed_no_later_than`, the build's time, with
  `observation_time_held: false`, and it states no observation time.
- A state the mounted index still holds is served in full, as `as_of` serves it
  (`text_held: true`). A state only an earlier build held is named by its permalink, digest and
  source bodies from the log, without text (`text_held: false`), until the generation mount
  (slice 6) can quote it.
- Every request by time refuses `snapshot_unknown`, because upper bounds alone place no instant in
  a snapshot. So does a digest that is no snapshot of this log. A date before the snapshot's history
  is `no_version_for_date`, and a language the snapshot did not hold is `language_not_available`.
- On the chained fixture (a predecessor whose log held the work at an earlier date, and the real
  build chained to it):
  - the predecessor's snapshot answers the earlier state, without text, bounded by the
    predecessor's build time;
  - the mounted snapshot quotes the act's state, the same permalink `as_of` gives;
  - between the two dates, the mounted snapshot answers the earlier state the log still holds,
    which `as_of` cannot answer.
- 24 operations are served and three remain unserved (`knowable_on`, `concepts`, `transposition`).
  The sentence for `knowable_on` now says what it still needs: each Luxembourg body's capture time.
  A build's time bounds observation only from above.
- The `answer_drift` and `events` MCP descriptions no longer call every log genesis (stale since
  #867).
- The registry digest moves with the new request schema, to `c40e23fd…`.

Schema 6 served with its build record absent (PR #878).
- **The gap.** Since #864 the reader read only the newest schema. So the API refused every Luxembourg
  index of schema 6 ("the schema differs from the exact terminal schema"). Two such indexes exist:
  - the data lane's running EU population, frozen at `6eb1d9d9`;
  - the real bounded first mount, which the web lane's journeys and release rehearsal used.

  The data lane raised it (Q-20261001-0656-codex). CI did not catch it, because the image rehearsal
  job is skipped there.
- **The panel's answer**, on the owner's behalf: the web lane adds exact schema-6 read validation for
  serving.
  - The new observation and log evidence is explicitly absent, and the operations that need it
    refuse with their typed codes.
  - `LuxembourgIndexPredecessor.TryRead` keeps refusing schema 6.
  - No log stamp and no observation time is invented.
  - The data lane does not edit these files. Its next step is "acquire once, derive many", so a
    schema change no longer forces re-acquisition.
- **What the reader accepts**, by `user_version`, exactly two schemas: 8, and 6.
  - Schema 6's tables are schema 8's but for the event log: one `events` table of `first_sighting`
    only, unique per state, with no `observations` and no `log_stamp`.
  - Any other version refuses, and so do tables that are not exactly that version's.
  - The DDL is now the shared tables plus each version's event log. It is byte for byte the same,
    and the fixed-input pin holds.
- **A schema-6 index is checked as schema 6 checked it**:
  - its log is exactly its states' genesis log as schema 6 wrote it (state digests only);
  - its stamp names schema 6;
  - its logical rows match their stamp.
- **What a schema-6 mount answers:**
  - the reader reports `SchemaIdentity`, and `RecordsBuilds` is false;
  - `events` serves its genesis log with `log.built_at` null, a `legacy_note` and a `build_record`
    not-held row. Its genesis and silence notes and its upstream-health row are schema 6's own, and
    claim no build time (review of #878);
  - `as_observed` refuses `snapshot_unknown`, saying the index names no snapshot;
  - `coverage`'s build-time row says the index records no build time either;
  - the corpus check holds only the binding: no source bodies or build time are held to check.
- **Fixtures:** the real bounded first mount's Luxembourg index, manifest and corpus, byte for byte
  the files its build report names (`c7f40548…`, `ef03cacd…`, `312d4804…`).
  - The mount opens and is served with its build record absent.
  - A schema-6 index with states (the state fixture rewritten into schema 6's tables) serves its
    genesis log, `as_of` answers, and `as_observed` refuses.
  - Seven tampers are refused, among them schema 6's tables under `user_version` 7 or 8, an
    observations table, a stamp naming schema 8, and schema 8's tables under `user_version` 6.
- **The whole ingest suite also ran with `V3_EVALUATE_MOUNT` naming the real mount**, so its
  real-mount gates ran on it.

Predecessor chaining, the sixth slice, first part (PR #880): a chained build keeps its earlier
generations, by the retention line (G3b).
- **The line.** The panel applied the launch's retention line, Stage 7 S7-A09
  (`lex-governance/stages/STAGE-7.md:37-38`), to the generation mount:
  - every generation a published permalink or evidence bundle references, indefinitely;
  - nightlies for 90 days;
  - one complete monthly keeper, indefinitely;
  - the retained depth reported truthfully.
- **`V3GenerationRetention.Decide`** decides from the log alone. "Now" is the mounted build's own
  time, never the clock, so two builds of one chain decide alike. It keeps each held generation
  that is any of these:
  - referenced;
  - the last build of its UTC day (the mounted build's day included) within 90 days;
  - its UTC month's earliest held generation.

  It reports the rest as dropped, and those no longer held as absent: never claimed.
  - Nothing records which permalinks or bundles were published. So `referenced` comes from the new
    Tool option `--referenced <file>`, a JSON array of index digests. It means the generations
    promoted to production, which is empty until the owner promotes one.
  - Every verified generation counts as "complete".
- **The writer** handles a chained build (`--predecessor`):
  - it copies the kept generations whole out of the predecessor's directory (the predecessor itself,
    or a generation it kept) into `generations/{luxembourg index digest}/`: the five mount files and
    the build report;
  - it records the decision in `generations/retention.json` and lists the generations in the build
    report;
  - nothing is deleted anywhere, and the predecessor's directory is only read.
- **`VerifyAsync`** holds each generation to the mounted log:
  - its name is an index the log names as a predecessor;
  - it holds exactly a generation's six files and is itself a mount that verifies;
  - its Luxembourg index is that digest, and its corpus the one its observation names;
  - its log is the mounted log up to its observation;
  - `retention.json` is the line's decision over the generations the writer could copy: those it
    kept, held here, and those it recorded as dropped, which have no directory;
  - a chained mount must hold `generations/retention.json`, even when it keeps no generation, so a
    mount whose generations were all removed does not verify as one that never had any. A
    generation is verified as a generation: its own earlier builds are its mount's to record.
  - The review of #880 found both gaps. With only the held generations as input, a correctly dropped
    nightly read as absent and the writer's record was refused; and a chained mount with
    `generations/` deleted verified.
- **Tests:**
  - three real first-mount builds on three days, each chained to the last: the third keeps the
    first (its day's last, and October's keeper) and the second (nightly), copied byte for byte,
    and verifies;
  - a fourth build 120 days after the first keeps only October's keeper and records the two
    nightlies, now past 90 days, as dropped; it verifies;
  - five ways a generation can be wrong are refused: a missing file, a directory that is no earlier
    build, another build's files under a generation's name, no `generations/` at all, and a
    retention record the line did not decide;
  - the policy's own tests cover the 90-day edge, a referenced old build, an absent one, and an
    earlier build of the mounted build's day.
- **Not yet:** the API reads no generation. Next come the mount's own checks of them at open, the
  depth `coverage` and `events` report, and then `as_observed` and `verify` quoting a state from a
  retained generation. An image built from such a mount carries `generations/` too. No byte budget
  is enforced yet.

Predecessor chaining, the sixth slice, second part (PR #885): the mount holds its generations to
its log when it opens, and reports the history depth it keeps (S7-A09: "reported history depth is
truthful").
- **At open,** `V3CorpusMount.OpenAsync` runs `V3CorpusMountWriter.VerifyGenerationsAsync`, the
  checks the writer's verification runs, so a mount is held to the same checks where it is built
  and where it is served.
  - A generation that does not hold, a chained mount with no retention record, or generations
    beside a mount with no Luxembourg index all fail the mount closed.
  - A chained build written without its predecessor's directory records each earlier build as
    absent (`WriteRetentionRecordAsync`), never claimed.
- **`coverage` gains `history`:**
  - whether the log records builds, how many it records and since when;
  - the retention line that decided (its id, nightly days and evaluation time);
  - each snapshot whose text the mount holds (the mounted build, and each kept generation with why
    it is kept);
  - how many it does not hold, and a note that every time is a build's, never an observation time.
  - A schema-6 index records no build and says so.
- **The Trust and Coverage page renders it,** in English with a French draft. The page's tests hold
  every leaf of it to reach the page, both renderers to agree, and every string to be escaped.
- **`events`' ancestors gain `text_held` and `retained_as`.**
- **Tests:**
  - the chain test's third build mounts and reports three builds, all three with text;
  - its fourth build, 120 days later, reports four builds, two with text and two without;
  - each damaged copy that the writer's verification refuses, the mount refuses with the same
    reason;
  - a genesis mount reports one build with the mount's own text and no retention line applied;
  - a schema-6 mount reports no build recorded;
  - the crafted chained mounts of the events and `as_observed` tests carry a retention record that
    names their predecessor absent.
- **Next:** `as_observed` and `verify` quote a state from a retained generation.

Predecessor chaining, the sixth slice, third part (PR #889): a state only a retained generation
holds is answered from it, and the permalink the product emitted for it still verifies (G3, and the
launch contract's "`verify` resolves every citation the product emitted").
- **Readers at startup.** The mount opens one verified reader per retained generation, newest first,
  so no request opens one (the image's private `/tmp` holds every index copy from startup on). The
  readers are disposed with the mount.
- **`as_observed`:**
  - a state the mounted index lacks is served in full from the newest retained generation that holds
    it, with `text_held: true` and `text_from` naming that generation;
  - `text_from` is null for a state the mounted index holds;
  - a state no held build holds stays identity only, without text;
  - a work the mounted build no longer holds at all is found through the log by its stable work
    coordinate, and answered from its generation (review of #889). An identifier the log never held
    keeps `identifier_unknown`.
- **`verify`:** a pinned permalink whose digest a later build replaced at its coordinate is verified
  in the retained generation that holds it:
  - `digest_matches`, with `held_in` naming the generation and `superseded_by` the mounted state that
    replaced it;
  - `verified_by` is the generation's own corpus and index, and the sources are its own;
  - with the generation pruned, it is `pinned_digest_mismatch` naming the current state, as before.
  - The generation is asked before any refusal over the current states. So the permalink resolves
    when the coordinate holds another state, several (one per language), or none any more, and
    `superseded_by` is null in the last case (review of #889).
  - A driver decision: the verdict stays `digest_matches`, because the digest does match the retained
    text. The superseded state is named beside it, so a citation checker keeps working and a reader
    sees that the citation is no longer the current text.
- **On two real builds of the state fixture,** the second with one article reworded:
  - `as_observed` at the first build's snapshot serves the original state from the generation;
  - the permalink emitted before the rewording verifies there, naming the reworded state;
  - with the generation pruned, the same requests answer without text and as a mismatch;
  - removing the generation lookup from `verify` fails the test;
  - a later build that holds the work no longer at all (chained, from the complete envelope, which
    holds no Luxembourg state) answers `as_observed` from the generation by the work's coordinate,
    and verifies the old permalink with `superseded_by` null. Reverting either repair fails that
    test.
- **What is left of predecessor chaining** is the data lane's part: observation times
  (`observed_from`) once a Luxembourg body's capture time reaches the corpus, then `knowable_on` and
  withdrawal.

The live export composer and its journey step (PR #789), the eighth screen. `dist-live/export.html`
has its own bundle `client-live-export.js`.
- The page asks what the reading page asks: the same form (`ReadingForm`, now shared), the same one
  `POST /api/v3/evidence_bundle`, the same outcome mapping and refusal sentences. It lists each
  state's articles with a pin each (a checkbox with no `name`), the text quoted in its state's
  language, and the articles held without text, pinnable so their exclusion travels.
- Pinning composes the export in the page (`export-build.mjs`) and asks nothing. The panel shows,
  before anything is saved: the counts, the watermark (the composer's own sentence), the rights
  disposition and rule, the date read, the snapshot's observation time, the corpus, index and
  registry digests, each item's citation (the hash-pinned article permalink), whole text digest and
  official source, each exclusion with its reason, and the JSON itself. "Save as JSON" and "Save as
  CSV" hand over exactly `exportJson` or `exportCsv` of that model, from a Blob in the page, under
  `lex-v3-export-<identifier>-<date>`. A new reading clears the pins.
- JSON (`lex-v3-export/1`) and CSV (RFC 4180, CRLF, one row per pinned article, the excluded ones
  included with their status and no text, each row carrying its citation, rights, the watermark,
  the snapshot's observation time, the rights rule and the corpus, index and registry digests, so
  a row copied out alone loses none of it). An excluded article keeps its citation, the state's
  permalink and its article id, in both formats (review of #789). PDF is the next
  paragraph's.
- The reading and export pages say the next held state's date as its start ("the next state held
  applies from"), never "until", as the compare page does after its review.
- `journey.mjs` runs an eighth step: `/export.html`, typing the fixture work's identifier and
  `2024-02-01`, then pinning the first article. With the mount the page ends in the composed export
  ("1 article pinned: 1 exported with text, 0 excluded.", the watermark, the rights); without one, in
  the card. Composing sends no second request (the verdict still counts exactly one).

The PDF export (PR #790). `web/scripts/export-pdf.mjs` sets the same export model as pages, so the
three formats cannot disagree, and the export composer offers "Save as PDF" beside JSON and CSV.
- Every page carries the watermark at its head and "Page n of N" at its foot. The first page says
  what was asked, the snapshot's observation time, the rights served under with the platform's rule,
  the counts, and the corpus, index and registry digests. Each item carries its citation, state
  permalink, text and body digests and official source (monospaced, wrapped without losing a
  character), its rights, its own date where it differs, its notes and its whole text, every byte
  kept: its lines joined with nothing between them are the text (review of #790). Each exclusion
  carries its citation and reason. No heading ends a page alone.
- Written byte by byte with no library: PDF 1.4, A4, uncompressed streams, the standard Helvetica
  and Courier fonts in WinAnsiEncoding, no embedded font and no time of making, so the same model
  gives the same bytes. Typographic spaces and hyphens outside WinAnsi are set plainly, and the PDF
  says so when it did; any other character it cannot set refuses the PDF with the characters named
  (`pdfRefusal`), and the panel then offers JSON and CSV only and says why.
- The tests read the file back on its own terms (header, every cross-reference offset, trailer,
  page tree, stream lengths, each text run decoded from WinAnsi by the test's own table) and check
  the launch contract's citations, rights, watermark and exclusions on it, every line inside the
  text width, and page breaks losing nothing. Read independently with pypdf (strict) and PyMuPDF:
  the fixture's 49 articles make 24 pages, with the watermark, the page numbers and the accented
  text intact.
- The launch-contract line "Exports PDF, JSON, CSV preserve citations, rights, watermarks and
  exclusions" is the owner's to tick.

The evaluation card on the Trust and Coverage page (PR #792, ruling 2). The page carries the card
below the coverage answer, rendered by the server from the card the build is given (by default the
one the platform renders, `schemas/v3-platform/evaluation-card.json`; a release build will be handed
the release card), outside the hydrated tree, so it needs no script and the page still asks one
request.
- `web/scripts/evaluation-card.mjs` (`readEvaluationCard`) holds the card's rules and recomputes
  what it can: each gate's verdict in the closed vocabulary, exactly a not-measured gate carrying a
  reason and no value, a pass at or above its threshold and a fail below it, each rate's Wilson 95
  percent interval (recomputed from the value and the stratum; anchor nDCG, a graded mean, has
  none), the rule-of-three bound beside every 1 (recomputed), each set's own gates and nothing else
  (temporal exactness; the refusal verdict match; retrieval's three, in order), one shuffled control
  per case set in order and of that set's kind (a control over fewer cases than its set says why),
  and every statistical row `not_yet_labelled` (Decision 92). A card that breaks a rule is not
  printed. The live build takes the card it is handed (`buildLive(destination, { card })`).
- `EvaluationCardView` prints what the card was run over first, as the card says it ("THE MOUNT IS A
  FIXTURE ... this card is not a release card"), then whether every gate passes and every control
  caught its shuffle (or which do not), one captioned table per case set (verdicts as words, values,
  thresholds, cases, the interval, the bound), the controls, the statistical rows "not yet
  labelled", and the negative-results register.
- Since PR #833: served as JSON at `/evaluation-card.json` and beside the release assets, signed
  through the release manifest. Since PR #844, the release card is the gates run over the release's
  own mount.

**The search journey step passes (PR #773, run locally 2026-09-30).** `node scripts/journey.mjs`
now runs two steps, each with and without the fixture mount, and all four runs pass.
- The search step loads `/search.html` and waits for hydration. It types "assemblée générale"
  over the DevTools protocol (`Input.insertText`, so React's own change handler runs) and presses
  the submit button.
- With the mount, the page ends in the answer, showing "4 with the exact phrase, 1 with every
  word, in 1 work", `art_15` and the no-ranker sentence. Without one, it ends in the
  `no_corpus_mounted` card.
- Each run makes 6 requests, exactly one of them `POST /api/v3/search`. Its body, read from the
  request the browser sent, is exactly `{operation_id: "search", parameters: {query, language:
  "fra"}}`. The page ends at `/search.html` with the history length it had when it loaded and
  no history state, so the phrase is not in the address or the history. `document.cookie` is
  empty at the end and the API request carried no cookie. There is no referrer, no storage and
  nothing on the console, and hydration is clean.
- Shown to fail:
  - a live build whose request carried one extra field failed the body check, naming the field;
  - a build that pushed the phrase into the history and set it in a cookie after submitting (the
    review of #773 showed it passed before these checks) failed three checks: history grown from
    2 to 3 entries, history state written, cookie set.
- Not driven yet: the next page. On the fixture the phrase has 5 hits and the form asks the
  platform's default page size, so no page is truncated and no "Next page" button appears. The
  cursor-carrying request is covered only by the injected-fetch test of PR #772. Driving it needs
  a mount with more hits than one page, which the first real mount would give.

**The first browser journey step passes (PR #766, run locally 2026-09-30):** `node scripts/journey.mjs --api <Lex.V3.Api build
output> --mount <journey mount>` runs the real `Lex.V3.Api` from a copy of its build output with the
mount beside it, serves `dist-live/` through `serve-live.mjs`, and drives headless Chrome over the
DevTools protocol until `data-answer-state` settles. With the fixture mount (written by
`V3JourneyMountTests` when `V3_WRITE_JOURNEY_MOUNT` names a directory; skipped otherwise) the page
ends in the coverage answer showing the mounted corpus and index digests; with no mount it ends in
the `no_corpus_mounted` refusal card. Both runs: 6 requests, exactly one to the API
(`POST /api/v3/coverage`, no query string), every other a same-origin asset, the headers the browser
actually sent (`requestWillBeSentExtraInfo`) with no referrer and no cookie, nothing in storage,
nothing on the console and no uncaught exception or unhandled rejection (a page given either fails
both runs, shown with `--live-root` on a deliberately broken build), the reviewed CSP, hydration
clean. The first run caught the journey's own
over-strict check: under `no-referrer` Chrome reports a provisional `Referer: ""`, and the verdict now
reads the headers actually sent. The journey is a local run, not a CI step (it needs a browser and a
built API). PR #765 builds the first live
page, Trust and Coverage, into its own directory (`node scripts/build-live.mjs` writes
`dist-live/`, gitignored, apart from the preview `dist/` and its gates): the loading state under a
live banner (the synthetic banner's "describes no real legal record" would be false on a real mount;
the live banner's wording is the driver's; ruling 3: a live page never says "synthetic" on a real mount), hydrated by its own bundle
(`client-live.js`, which embeds the census contract and nothing else of the census; the preview
pages' `client.js` still carries no request). `scripts/serve-live.mjs` serves that directory and the
API on one origin for a local journey: static files with `nosniff` and `no-store`, and exactly
`POST /api/v3/{operation}` forwarded (no query string, case variant, deeper path, `/mcp` or other
method), carrying the body and its media type and nothing about the caller (no cookie, user agent,
referrer or forwarded address), a body over 1 MiB refused before it is forwarded, an unreachable API
a `502` problem, an API that never answers a `504` after 30 s; the traversal guard and the chunked
body ceiling are tested with raw requests, since `fetch` normalises dot segments and always declares
a length. PR #764
adds the first live screen as a component, `LiveCoverage` (Trust and Coverage): the server renders
its loading state, the browser asks `coverage` with no parameters in an effect through the client
module, and the answer is the `Coverage` page (read by `readCoverage`, so a served answer the reader
cannot account for is shown as unreadable, never rendered), the refusal card for a refusal (the one a
coverage request can meet, `no_corpus_mounted`, in the refusal catalog's words, held equal by a
test; a refusal whose card the card's own rules will not show is a status line naming its code,
never a render that throws), or a status line for a transport failure or an unreadable answer; the
mount's one request is `startLiveCoverage`, which asks once and whose cancel aborts it and silences
it (tested; the effect itself is proven by the next slice's browser journey); the root carries
`data-answer-state` for a browser run to wait on. No page mounts it yet (the page, the build that
embeds the contract, and the same-origin serving are the next slice). PR #763 added the first code
that can call the API: `web/scripts/v3-envelope.mjs`, a strict reader that mirrors
`V3EnvelopeJson.ParseAndVerify` (exact closed member sets, the reviewed registry digest, one branch
with a consistent verdict and context status, the result bound to the operation's schema and object
types, a registry refusal code with every mandatory payload field, the `anchor_not_in_version`
rule, the request reference's and snapshot identity's forms, a real observation instant that is not
the default, and the nesting limit of 32; it cannot see duplicate members or non-canonical bytes and
says so), and
`web/scripts/v3-client.mjs`, which asks one served operation by a same-origin POST whose parameters
travel in the body alone (no credentials, no cache, no referrer, redirects refused, no retry,
nothing logged) and returns the renderable states (`success`, `refusal`, `transport_failure` with
the problem code, or `network_error` when the connection fails before or while the body arrives,
`invalid_envelope` with the reason; a cancellation rejects as one). The reader holds no copy of the registry: it
reads the `contract` block of a new census, `schemas/v3-platform/envelope-samples.json`, which the
platform renders from the reviewed registry beside four whole envelopes the real handler sent
(coverage answer, the `ask` card, `no_corpus_mounted`, a refusal with a payload), each verified by
`ParseAndVerify` before it is recorded (render with `V3_RENDER_ENVELOPE_SAMPLES=1`). The client's
served list is pinned to the platform's own list in the coverage answer sample.

Machine gates (PRs #767, #768 and #769): the evaluation harness in `Lex.V3.Contracts.Evaluation`
(`TemporalEvaluation`, `VerdictEvaluation`, `ShuffledControls`), which nothing outside its own tests
called, now runs against the real handler on a mounted corpus (`V3MachineGatesTests`). The temporal
case set (8 cases on one work with a single first state, two states on one later date and a single
latest state: before history, the first day, inside, the last day before the twins, the twin day,
inside the twins, the latest day, after it) is asked four ways: `as_of` and `in_force_on` (the work
named), each with `language: "fra"` and with no language. A selected state is its digest and a
refusal is `refusal:{code}`. All four arms reach exactness 1.0 and pass gate `temporal_exactness`,
and each arm's date-shift control is caught: every case moves forward by the held interval and every
expectation breaks. The cases at the latest state cannot break under a forward shift, so by the
control's design they sit outside it. The refusal case set (18 requests over three fixture mounts)
covers each of the 14 codes the refusal census records as produced (`ambiguous_identifier`,
`ambiguous_version`, `anchor_not_in_version`, `format_not_available`, `identifier_unknown`,
`language_not_available`, `no_corpus_mounted`, `no_version_for_date`, `pinned_digest_mismatch`,
`profiles_differ`, `retrieval_mode_unavailable`, `snapshot_unknown`, `text_not_available`,
`text_withheld`), and the test pins that list to the census. Three requests must be answered. The
served operations reach exact match 1.0, the gate passes, and the verdict-shuffle control is caught.
The six codes nothing produces yet (`advice_boundary`, `derivation_refused`, `not_transposable`,
`out_of_corpus_scope`, `rate_limited`, `upstream_unreachable`) are outside the set. Two production
mutations were each caught: `as_of` choosing one of two states on an ambiguous date fails both
gates, and `in_force_on` answering the twin date for a named work instead of refusing fails the
temporal gate. The retrieval case set (PR #768) has 13 cases in one collection over two works of one date, and
the test writes every article's text. There are 6 judged searches: a strict hit graded 3 above a
relaxed hit graded 1, one query both works answer, and one search scoped by `identifier` to the
second work that must find that work's article and not the first's. There are 4 cases that must
find nothing: a word held nowhere; a word asked with the `identifier` scope of the other work, in
each direction; and a near-miss article permalink naming an anchor the state does not hold, which
`verify` must refuse `anchor_not_in_version`. The last 3 cases are exact article permalinks
(`permalink#anchor`), the form only `verify` accepts; it answers each held anchor with its work.
`verify` names the requested anchor back rather than serving a provision, so what the resolver
stratum measures is that each held permalink is accepted under its own work and a missing anchor
is refused. A refused search fails the test: search answers zero hits, never a refusal. Anchor
nDCG@10 is 1.0, and no-hit accuracy and resolver exactness are 1.0. The test declares the nDCG
threshold at 1.0 because the judgments are written from the corpus the test builds: it measures
the path's exactness, not retrieval quality, and the labelled statistical row stays "not yet
labelled". The judgments-shuffle control is caught: nDCG@10 falls below 0.15 and both invariant
gates stop passing. The launch contract's three shuffled controls are now each shown to fail on
the real handler. Three production mutations each fail the retrieval gate: `search` serving
relaxed hits before strict ones, a scoped search matching no work, and `verify` answering an
anchor the state does not hold. The cases come from the fixture, so the gates prove the path
until a real mount exists.

The temporal gate over any mount (PR #838), the first slice of ruling 2's machine gates over the
real mounted corpus. The fixture's gates build their own states; these derive their cases from the
mount they are given.
- `V3MountedGatesTests` reads the mount's Luxembourg index: each work's held states in one language,
  by applicability date, form a timeline. The timeline says what a dated request must select on any
  day:
  - nothing before the first date (`no_version_for_date`);
  - the one state of the latest date on or before it;
  - `ambiguous_version` where two states share that date.
- The cases, for a seeded sample of up to 40 works:
  - the day before the first date;
  - each date, a day inside and the last day of each window;
  - a day 1,000 days after the latest date.
- The arms are `as_of` and `in_force_on`, in each language the sample holds, and with no language
  for every work. With no language the mount selects in every language the work holds:
  `ambiguous_version` if any language's date holds two states, `no_version_for_date` if none holds
  one yet, and otherwise each language's state. The work's timelines together give that key (the
  sorted states). The review of #838 found that the first version left multilingual works out of
  the no-language arms.
- The date control shifts every case by the median gap between held dates. It runs over the cases
  whose selection the timeline says the shift changes, which always includes each day before a first
  date.
- An arm with no case is `not_measured` (`no_measurable_query`), and its control says there was no
  case to shift. An empty stratum is reported, never scored.
- With no variable it runs on the fixture: the fixture's state, two states on one date and a latest
  state give 9 cases per arm, all passing, and every control catches the shift. When
  `V3_EVALUATE_MOUNT` names a mount it runs there, and `V3_EVALUATION_CARD_OUT` receives the card
  sets.
- Over the real bounded first mount, which holds no Luxembourg state: not measured, and it says why.
  Over the journey mount, written by another test with one state: 3 cases per arm, all pass, and every
  control catches the shift.
- A French work with a German state 200 days later gives 6 no-language cases: French alone before
  the German date and both from it. All pass, and every control catches the shift.
- Two mutations:
  - a timeline that ignores two states on one date fails the timeline test and the fixture gate;
  - the first version's single-state arm used for no language fails the multilingual test.
- Next slices: the refusal and retrieval sets derived from a mount, then the card over the release's
  mount in the rehearsal.

The refusal set reads the EU index too (PR #846), so a mount that holds only the EU still measures
its refusals. From the EU index's first work in its first language that holds a usable word in any
article (scanned in order; the review of #846 found the first version read only the first article,
so a short one left the EU unmeasured):
- a word it holds, searched in it, must be answered;
- the same in a CELEX the index does not hold: `identifier_unknown`;
- in a language the work is not held in: `language_not_available`;
- with a date, which EU search does not serve: `retrieval_mode_unavailable`.
A code an EU request produces is no longer listed as not produced. Coverage's own rule is mirrored:
it refuses `no_corpus_mounted` when the mount holds no Luxembourg index. The first run on the
EU-only GDPR fixture expected an answer there.
- On the real bounded first mount the refusal set grows from 1 request to 5 (coverage and the four
  EU ones), all answered with their code. The verdict control now applies and catches the shuffle.
- On the GDPR fixture alone: every EU request is answered with its code, and the control catches
  the shuffle.
- The release-mount run requires the EU requests whenever the EU index holds a usable word.
- Two mutations:
  - the unknown CELEX given the gold `answer` fails;
  - reading only the first article fails the short-first-article test.

The refusal set over any mount (PR #839), the second slice of ruling 2's gates over the real
mounted corpus (`V3MountedGatesTests.Refusal.cs`). Each request is built from what the mount holds,
so the one code the registry says answers it follows from the mount's own data:
- a state held alone on its date anchors most requests:
  - that state and its permalink must be answered;
  - an unknown work gives `identifier_unknown`;
  - the day before the work's first date gives `no_version_for_date`;
  - an anchor the state lacks gives `anchor_not_in_version`;
  - a wrong digest gives `pinned_digest_mismatch`;
  - a language the work is not held in gives `language_not_available`;
- mode, cursor and format requests are refused `retrieval_mode_unavailable`, `snapshot_unknown` and
  `format_not_available`;
- data-dependent codes are derived only where the data holds them:
  - two states on one date: `ambiguous_version`;
  - a title two works carry, or a beginning two works' titles share (the resolver's own rule, exact
    first, then prefix): `ambiguous_identifier`;
  - two states whose rule profiles differ: `profiles_differ`;
  - a state whose text the evidence bundle withholds by its own rule: a source not acquired, or any
    rights disposition but the admitting `agreed_same_run_cc_by`, with the tokens taken from the
    contract: `text_withheld`. The review of #839 found that the first version guessed a
    `non_admitting` prefix and missed a disposition such as `conflict`;
  - a state whose text is admitted but holds none: `text_not_available`;
  - no EU index: `no_corpus_mounted`.
- A code the mount cannot produce is named on the card as not produced, with its reason, and never
  faked.
- On the fixture (two titled works, a later state with another rule profile, two states on one
  date): 16 requests, every one answered with its code; the verdict control catches the shuffle.
  `text_withheld` and `text_not_available` need other mounts. The withheld-licence fixture derives
  `text_withheld`.
- `TheGatesOverTheMountTheReleaseNames` now runs the temporal and refusal sets over the mount
  `V3_EVALUATE_MOUNT` names and writes both to the card:
  - the journey mount: 13 requests, all answered with their code, and the control catches the
    shuffle;
  - the licence-blocked mount: 14, adding `text_withheld`;
  - the real bounded first mount: 1 (coverage, answered). Every Luxembourg code is listed as not
    produced there, since there is no Luxembourg state. `no_corpus_mounted` is not produced either,
    since the EU index exists.
  EU refusal cases derived from the EU index came later: EU search's in PR #846, and EU `verify`'s
  in PR #860. The real bounded first mount now has 8 cases.
- Two mutations:
  - the day-before-history request given the gold `answer` fails the refusal tests;
  - the first version's `non_admitting` guess fails the `conflict` case.

The page reads a card over a mount the gates found nothing to ask of (PR #841). Over the real
bounded first mount the temporal no-language arms and the retrieval set have no case, and the page's
card reader refused a set of no case. `readEvaluationCard` now accepts one only as honest emptiness:
- a gate may count no case only when it is not measured for `no_measurable_query`;
- a set of no case must have every gate that way;
- a control over no case can only be `not_applicable`, with its reason;
- a set or control of no case carries the digest of no case, `EMPTY_CASES_SHA256` (the SHA-256 of
  `[]`, as the platform renders it). The review of #841 found another digest was accepted.
A scored gate over no case, another not-measured reason over no case, a control over no case that
"caught" something, and a no-case digest that is not the empty list's are refused.
The first mount's card reads and renders. At this head it has 3 sets (the two temporal no-language
arms and the refusal set) and says "2 gates do not pass", each named not measured. With #842's
retrieval set it has 4 sets and says 5, as the review of #841 counted. The journey and
licence-blocked mounts' cards and the platform card read too.
The retrieval set reads the EU index too (PR #845), so the real bounded first mount's card measures
retrieval. EU search is served in one work and states the same matching (`EuropeSearchMatching`: a
byte-exact substring). For a seeded sample of up to two of the EU index's works in each language it
holds (the review of #845 found the first version took two work-language pairs in all, which could
leave a language unsearched):
- words held by one to five provisions are judged to find exactly those (the CELEX and the
  publisher's provision id, as an EU hit names them), searched with the work as scope;
- strings the work holds nowhere find nothing.
EU `verify` was not served then, so the EU gave no exact-identifier case. On the GDPR fixture and on
the real first mount, anchor nDCG@10 was 1 over 6 EU word cases and no-hit accuracy 1 over 3.
Resolver exactness was not measured (no measurable query), so the judgments control did not apply:
its rule needs every required gate to pass first, and the card said so. Since PR #860 the EU gives
exact cases over the EU permalink grammar, and every retrieval gate is measured there (below). A French-held work gives French
cases, which pass. Two mutations:
- EU judgments naming another provision drop nDCG@10 to 0 and fail;
- the first version's sampling (three English works and a French one) leaves French out and fails
  the sampling test.

The retrieval set over any mount (PR #842), the third slice of ruling 2's gates over the real
mounted corpus (`V3MountedGatesTests.Retrieval.cs`). The judgments are computed from the mount's
index by the search's own stated matching (`SearchMatching`: a byte-exact substring of an article's
searchable text, nothing folded):
- words of the sampled states held by one to five articles (work and publisher article id) of the
  language, with every hit on the answer's one page: each is judged to find exactly those, which
  must all rank within the first ten;
- the same word scoped to its work finds that work's articles; scoped to a work held in the language
  that lacks it, nothing. A work not held in the language is refused `language_not_available`, so it
  is never asked;
- strings held nowhere find nothing;
- three article permalinks of each held state must be accepted under their own work and anchor,
  and one naming an anchor the state lacks is refused `anchor_not_in_version` (no hit). The cases are
  keyed by state, so two states of one work keep their own cases. The review of #842 found both of
  the last two;
- a refused search ranks a marker that matches no judgment, so it fails rather than passing as
  "found nothing".
Over the fixture (two works) and the journey and licence-blocked mounts (one state, 15 cases each),
all three gates pass at 1: anchor nDCG@10 (11 cases), no-hit accuracy (4) and resolver exactness
(3). The judgments control catches the shuffle. The real first mount has no case: all three gates
are not measured and the control says why. A mutation, word judgments naming another anchor,
drops nDCG@10 to 0.57 and fails. `TheGatesOverTheMountTheReleaseNames` now writes all three sets.

The image rehearsal runs in CI (PR #847): the job `image-rehearsal` in `v3-ci.yml`, on every push
to `v3/integration`. It also runs on a pull request whose branch name contains `rehearsal`, so a
change to the rehearsal proves the job before it merges.
- It writes the journey fixture mount (`V3JourneyMountTests`) and runs
  `web/scripts/image-rehearsal.mjs --no-probe`. The WSL probes need the driver's machine and stay
  local.
- `jq` then holds the report to:
  - the second build's digest;
  - no image failure and V2 absent;
  - the rehearsal signature verified;
  - the card being the gates run over that mount;
  - the release read back with no failure;
  - the work and artifacts directories removed.
- The report is uploaded.
Push only, so a pull request's wait is unchanged, while the image pipeline is checked after every
merge, the data lane's included.

The release card is the machine gates run over the release's own mount (PR #844), the last slice of
ruling 2's gates over the real mounted corpus. The image rehearsal first runs
`V3MountedGatesTests.TheGatesOverTheMountTheReleaseNames` over the mount it bakes:
- `V3_EVALUATE_MOUNT` names the mount and `V3_EVALUATION_CARD_OUT` receives the card, built in its
  own artifacts directory;
- the card is checked by the page's own rules;
- it is handed to the live build (the Trust and Coverage page renders it) and served at
  `/evaluation-card.json`, and the release carries it beside the image under the signed manifest.
A gate that fails fails the test, and so the rehearsal: no release goes out with a failing gate.
`--platform-card` keeps the platform's fixture card.
- On the real bounded first mount the card is the gates over that mount:
  - the two temporal no-language arms have no case and are not measured, and their controls say why;
  - the refusal set holds one request (coverage) and names every code the mount cannot produce;
  - there is no retrieval case, and the page reads all of it (PR #841).
  The image serves that card at `/evaluation-card.json`, byte for byte the release's. The image was
  reproduced, the 8 probes passed and the release read back clean.
- On the journey fixture mount, every gate passes and every control catches its shuffle:
  - 4 temporal arms of 3 cases;
  - 13 refusal requests;
  - 15 retrieval cases, all three gates at 1.
  The 8 probes verify 105 citations.
- A mutation, one derived gold changed, fails the gates step, so the rehearsal stops before anything
  is built, and its work directory is still removed. The first version computed the card before the
  work directory's `try` and left it behind on such a failure.
- The test platform takes `-m:1` and `-nodeReuse:false` for its own and then runs no test, so the
  gates' `dotnet test` passes neither.

Evaluation card (PR #769): `EvaluationCard` in `Lex.V3.Contracts.Evaluation` prints the machine
gates as the card of `36-ideal-evaluation.md` section 6 describes, as far as the launch contract
asks. Each case set and arm is a row with its case count and `cases_sha256`. Each gate carries
its verdict (`pass`, `fail` or `not_measured` with its reason), value, threshold and stratum `n`.
Every rate carries its Wilson 95 percent interval, and every value of exactly 1.0 carries the
rule-of-three bound on the failure rate, so a pass on a small set is never oversold. Anchor nDCG
is a graded mean, so it has no Wilson interval. Its 1.0 still carries the bound: each case scores
at most 1, so 1.0 means every case ranked perfectly. Each shuffled control result is printed like
any other number, with the count and digest of the cases it ran over. The date control runs over
6 of the 8 temporal cases, and its note says why: the cases at or after the latest held state
cannot break under a forward shift. The 8 statistical rows (D1 to D8 of section 2) are all
`not_yet_labelled` under Decision 92. The negative-results register holds its standing entry:
hybrid retrieval is not activated, and search refuses a ranked mode `retrieval_mode_unavailable`.
What would reverse that is the activation gate as section 2 (D6) and section 1.4 (repair 6) state
it. The machine gates test renders the
card to `schemas/v3-platform/evaluation-card.json` (census, `V3_RENDER_EVALUATION_CARD=1`) and
compares it byte for byte on every run. The fixture's cases give the same card run after run. As
held today: temporal 8 cases per arm, Wilson [0.6756, 1], rule of three 0.375; refusal 18 cases,
[0.8241, 1], 0.1667; retrieval nDCG n 9 (rule of three 0.3333), no-hit n 4 ([0.5101, 1], 0.75),
resolver n 3 ([0.4385, 1], 1, which bounds nothing). The card is not published and is not a release card: it
has no release, image or snapshot identity (item 7) and no signature. Ruling 2: it is served on the
Trust and Coverage page and beside the release assets, with the gates run over the real mounted
corpus.

Replay guarantees (PR #770): `V3ReplayGuaranteesTests` runs the two guarantees of
`33-product-spec.md` G1 to G5 that one build can prove against the real handler.
- G2, snapshot determinism: each of the 23 served operations (pinned to `V3RestRouteBinding.Served`,
  so a new operation fails until it has a case) answers the same canonical bytes to the same
  request asked again and to a mount opened from a byte copy of the directory. Asked 30 days later
  by another request, the answer is the same bytes apart from the two fields that belong to the
  request: `context.freshness.observed_at` and `request_ref`, the digest of the request's own
  trace identity. Every envelope is also verified canonical by `V3EnvelopeJson.ParseAndVerify`.
  `resolve` is asked with the fixture's permalink, so the check covers its answer path and not
  only a refusal.
- G5, independent verifiability: a reader holding one `evidence_bundle` answer and the
  publisher's file checks each served article's text against that file. The text of the article
  with that id is its non-blank text nodes in order, outside the publisher's `scl:` annotations
  and the authorial notes, and it equals the served text for every article. The reader then
  recomputes the digests whose derivations are published. The file hashes to the source's
  `body_sha256` and length, and each article's `text_sha256` and byte length are its text's.
  `article_identities_sha256` and `state_sha256` recompute from the derivation `provenance`
  publishes, and the permalink pins that digest. Not recomputed: `wording_sha256`, whose input
  is the stored token stream the bundle does not serve, and the article identities, rule-profile
  digests and body receipt, whose derivations are not published.

Running G5 found a defect: the published derivation put the domain tag outside "each as UTF-8
preceded by its length", while the builder length-prefixes it. A reader who followed the sentence
got another digest. The sentence now reads "over these values, each as UTF-8 preceded by its
length as four bytes big-endian: the domain tag lex-v3-luxembourg-expression-state/1, the
publisher, ...". The same wording is in the provenance answer, its test pin, the answer-samples
census and the web preview's copy. G2's detached signature comes from the release pipeline (item 7).
The mount is the fixture, so this proves the path, not a corpus.

Replay G1, G3 and G4 on the real handler (PR #897). Predecessor chaining (PRs #864 to #889) made the
other three guarantees provable, and each had been proven in parts: G1 at the builder
(`LuxembourgIndexBuilderTests`), G3 by the generation mount (`V3CorpusGenerationQuoteTests`), and G4
by `as_observed` (`V3CorpusAsObservedMountTests`). `V3ReplayGuaranteesTests` now runs all three
through the real handler, beside G2 and G5, so the launch contract's "replay G1 to G5" has one place.
- The chain: three real builds of the state fixture on three days, each chained to the one before.
  Each is written as the mount it would be, with the earlier builds kept beside it as generations and
  the retention line's record, and each verifies:
  - the first build reads the publisher's file as held;
  - the second reads it with one byte added and its text unchanged;
  - the third reads it with one article reworded.
- G1, version immutability:
  - the second build answers the same version id as the first (the panel's G1 answer: no new version
    for unchanged text), and the third answers a new one;
  - the latest log holds one `file_replaced` in each later build's events. Each names the version it
    replaced and the version it holds, and the bodies differ;
  - the first version id still verifies on the latest mount (`digest_matches`) and names the version
    that replaced it.
- G3, nothing hard-deleted:
  - each earlier build's `events` answer is the latest log's prefix, row for row, every field but the
    cursor (which names the log it was read from);
  - every state the log names, held or replaced, verifies on the latest mount: the original from a
    kept generation, the reworded one from the mounted index.
  - Which builds keep their text is the retention line's (S7-A09). A state no kept build holds is
    still named by the log, without text.
- G4, as-observed answering: `as_observed` at each build's snapshot, asked of the latest mount,
  answers the version that build's own mount answered, article for article by text digest:
  - quoted from that build's own generation, or from the mounted index for the latest;
  - bounded by that build's time (`observed_no_later_than`), claiming no observation time.
- The mounts are fixtures, so this proves the path, not a corpus. A real chained mount needs a second
  build from custody, which the data lane's offline derivation will provide.

EU text served: EU `evidence_bundle` and the reading screen over the original wording (PR #903; the
web lane's order 2, from the owner's proxy's journey corrections of 2026-10-01 14:30 UTC).
- **The answer.** EU `evidence_bundle` answers a work the mounted EU index holds from that index. It
  quotes, for each held expression in the served languages, every article of the one held wording:
  - the article's text, which is the text the index searches;
  - the digest of that text, held to the index's own stored text digest (a difference is a damaged
    index and throws, never a quote);
  - the corpus body digest, the official source, and an article permalink that EU `verify` accepts
    (the wording permalink plus the provision after `#`).
- **The date.** It answers on the wording's own Formex act date only. No consolidation is held, so a
  date before or after it refuses `no_version_for_date`, naming the held wording dates (the EU parity
  driver decision (b), PR #761). The original wording is never served as a later date's.
- **Decision 95.** Every EU answer carries the acknowledgement "© European Union,
  https://eur-lex.europa.eu" and the statement that only the Official Journal published in
  electronic form is authentic (Regulation (EU) No 216/2013, Article 1(2)).
- **Rights at compose time.** Every corpus member the articles come from must be acquired, which an
  EU build reaches only after retaining the Decision 95 receipt. Otherwise the answer refuses
  `text_withheld`.
- **Refusals.**
  - An ambiguous work or a language not held refuses as EU `dossier` does.
  - With no EU index mounted, an EU identifier keeps the refusal it had, `retrieval_mode_unavailable`,
    which the refusal census pins; EU `dossier` answers `no_corpus_mounted` there. Aligning the two is
    a follow-up, once the samples can be re-rendered.
- **The screens.** The reading screen reads the EU bundle (`readEuropeEvidenceBundle`, holding it to
  its own rules) and shows:
  - the acknowledgement and authenticity statement above the text;
  - each wording headed by its Formex act date, never an applicability date;
  - each quote with its evidence line.
  The export composer shows the EU text and says it is not composed: it pins Luxembourg states'
  articles only, so it offers no file (the EU export, below, now composes it). The chrome table gains
  the EU heading and counts and the export sentence, each with a French draft, and the chrome scan
  renders both EU views.
- **Evidence:**
  - `V3CorpusEuropeEvidenceBundleMountTests` on the retained GDPR fixture, through the real handler:
    - every article with text is quoted, in the index's order, with the index's own text;
    - each `text_sha256` is recomputed;
    - every article permalink verifies as `digest_matches`;
    - the day before and five years after are each refused, and German refuses;
    - with no language, the French-only fixture's one held wording is quoted under its own permalink,
      and English refuses `language_not_available` naming French. No fixture holds two languages of one
      work: a bundle with two wordings (their order, `served_languages`, the web reader's language rule)
      is exercised only by the journey's EU reading step on the real bilingual canary mount, not yet run.
  - The EU citation walk (`V3CitationVerificationTests`) now verifies the bundle's permalinks too.
  - Web: `reading-europe.test.mjs` covers the reader, fourteen broken rules, the escaped provision, the
    reading screen and the export composer. The chrome scan passes with both EU views.
  - Review of #903 (an independent Claude review; the owner's order of 2026-10-02 excludes Codex):
    - the journey's quote check read Luxembourg permalinks only, so every EU quote failed it; it now reads
      either publisher's (`pinnedCitation`), and `journey-verdict.test.mjs` holds EU quotes to it;
    - an EU refusal on the reading and export screens was said in Luxembourg's words ("applies on that
      date"); EU refusals now have their own sentences (`LIVE_READING_EUROPE_REFUSAL_SENTENCES`, French
      drafted in `refusal-sentences.mjs`), and an EU refusal card's date hints speak of wordings
      (`europeNullSentences`), Luxembourg's of states; a test holds every EU refusal sentence and card
      to no applicability, state or version word;
    - the served scope now says what `body_sha256` is (the corpus member's retained manifestation) and
      that the text is read from the Formex package (`package_sha256`, `source_entry_sha256`).
- **Not yet:**
  - the journey's EU reading step on the real canary mount (GDPR in English and French) in a browser;
  - an EU bundle captured in the answer census, which replaces the web tests' hand-built answer.
  Local builds, test runs and browser runs wait for free memory above 4 GB (standing order of 10:35
  UTC); CI runs the whole solution.

## The EU time view (PR #909, sole driver, 2026-10-02)

The owner's main use case ("users primarily want a temporal view of EU texts") over the EU index's states
table (schema 5, PR #905): every work the publisher's census discovered for a seed act, the original and
each consolidated version, with the publisher's consolidation date or a typed reason it has none.
- `timeline`, `as_of` and `evidence_bundle` at any date answer EU acts from it (`V3CorpusMount.EuropeTime.cs`);
  `verify` verifies a consolidated wording's permalink; `dossier` gains `wording_timeline`, and a
  consolidated version's work IRI, CELEX or expression names its act's dossier. `consolidations_held` and the
  `later_wordings` rows of dossier and search say what the mount holds. An EU index with no states table
  keeps the original-wording path (PR #903) and the mode refusals.
- The reading screen reads the time view's bundle: each wording headed as the original or the consolidated
  wording of its date, with the dates it answers ("to the day before {next}") or that it is the latest held.
- Driver decision (reversible), the selection rule: per language, the latest dated wording at or before the
  date answers until the next; works sharing a date answer only if every text the corpus holds for that date
  is the same (the work whose CELEX names that version represents them); two different held texts are
  refused `ambiguous_version`; a same-date or undated version whose text is not held is disclosed beside the
  answer; an undated version makes the dates ambiguous only when a held text of it differs; a date after the
  latest consolidation is answered by it as dated, and `not_held` says an amendment the publisher has not yet
  consolidated is not seen. An EU date is a wording date, never an applicability date.
- Real data that shaped it: the real consolidated GDPR mount (`C:\lex-v3\lu-consolidated-offline-20261001-3`)
  holds the original (2016-04-27) and four works dated 2016-05-04, one with CELEX `02016R0679-20160504`, two
  holding byte-identical English and French text; the GDPR from 2016-05-04 answers with the CELEX-designated
  consolidation and discloses the other three.
- Evidence: `V3CorpusEuropeTimeViewTests` on a mount derived from the consolidated fixture (the timeline by
  CELEX and by consolidated work, as_of between, after and before the wordings, the bundle and its verified
  permalinks, the original pin, a tampered digest, the dossier by consolidated CELEX); the EU mount and mode
  tests restated; the answer and envelope samples re-rendered (only the EU dossier changes).
- Review of #909 (independent Claude review), repaired:
  - the bundle now discloses, beside each wording, the works sharing its date and the versions with no
    usable date (`same_date_works`, `unplaced_versions`), and the reading screen says them;
  - the time view serves its own statements: the bundle's scope (`EuropeTimeEvidenceBundleScope`), the digest
    rule naming the seed act's CELEX (`EuropeStateDigestRule`), the dossier timeline's own date semantics, and
    the bundle's and verify's full not-held rows; the reading copy no longer calls the next wording "held";
  - `text_held` says whether a text is held for the date (it was false on a date holding two different texts);
    verify on such a date refuses `ambiguous_version` rather than naming one text as current;
  - the EU `text_not_available` payload is Luxembourg's shape, which the reading card admits, and an EU
    `ambiguous_version` carries the registry's fields with its candidates pinned (an undated one by its
    expression); the reading screen says an EU ambiguity in its EU sentence and shows no Luxembourg card;
  - `V3CorpusEuropeTimeViewBranchTests` cover the branches on mounts from a generalised consolidated fixture
    (`EuConsolidatedWorksFixture.cs`): works sharing a date with one text (the CELEX-designated work answers,
    the others disclosed), with two texts (ambiguous, none chosen, not by verify either), the latest wording
    with no text in a language (`text_not_available` there, the other language answering), and undated
    versions (disclosed when their text is the same, ambiguous at every date when it differs).
- The GDPR outcome above was read from the real consolidated mount's index by inspection, not by a test; a
  real-mount test over the full EU population follows when it lands.
- Not yet: EU `article_history`, `diff` and `changes_in_period` (the history, compare and radar screens'
  EU paths).

## The French interface (sole driver, 2026-10-03)

The French interface ships (branch `writer/french-chrome`, on the EU time view's PR #909; the launch
contract's line "Chrome in FR and EN; DE and LB answer `localization_unavailable`").
- **The review.** The French was reviewed by the AI reviewer under the owner's delegation of 2026-10-02
  (the receipt is dated 2026-10-03), and accepted by the owner on 2026-10-04, who reviewed it in person
  (`C:\lex-v3\lanes\STANDING-ORDERS.md` section 8: "i did review it and its ok"). Its record, every changed entry
  with its reason and source, is `C:\lex-v3\lanes\fr-review\review.md`: 129 of 370 entries changed
  (61 in wording, 68 in typography). The receipt is `CHROME_REVIEWS.fr` in `localization.mjs`:
  `reviewed_by` "Claude (AI reviewer), under the owner's delegation of 2026-10-02", `reviewed_on`
  2026-10-03. `provenanceOf` now calls a review `review` (it said `human_review`), and the receipt
  names who reviewed.
- **Terminology** (the review's decisions, sourced from Legilux's own French labels and the
  Interinstitutional Style Guide): a work is an "acte" and its title an "intitulé"; a Luxembourg state
  is a "version", "applicable à partir du"; a wording (an EU act's, or a Luxembourg article's from
  state to state) is a "libellé", and an EU consolidated wording a "libellé consolidé", not EUR-Lex's
  "version consolidée", because "version" is the Luxembourg state's word and the date rule keeps them
  apart; an EU date is never "applicable". Held is "détenu" (never "disponible"), searchable
  "interrogeable", served "communiqué", evidence "élément de preuve", legal advice "consultation
  juridique", a build "déploiement", a release "mise en production", a gate "critère", the outcome
  token "disposition" a "classement", EU "de l’UE". A no-break space (U+00A0) stands before
  : ; ! ? % and » and after «, and the apostrophe is ’.
- **What ships.** The draft module became the reviewed table (`web/scripts/live-chrome-fr.mjs`,
  `LIVE_CHROME_FR`, with the refusal French as `REFUSALS_FR`). `LIVE_CHROME` holds `en` and `fr`,
  and `fr` is in `REVIEWED_CHROME_LOCALES`, so the live build writes the eight pages and their
  scripts under `/fr/`. The language list links Français to `/fr/index.html`, and no
  `locale-fr.html` is built; German and Luxembourgish still answer `localization_unavailable`.
- **Refusals in French.** A French page says the 26 refusal sentences by code (EU refusals
  included), each screen's two sentences for a refusal named only by its code, and the two hints in
  the reviewed French, keyed by the English the screens hold. A sentence with no French throws
  (`refusalSentence`) rather than being said in English, and `refusal-sentences.test.mjs` holds every
  served sentence to one French sentence. English pages and English sentences are unchanged.
- **The English the review raised, fixed with its French:** `reading.idle` no longer says "applied"
  before the publisher is known (the review's own pair); `reading.intro` reads an EU work in the
  wording that answers the date, original or consolidated; `dossier.euExpressions`, and for the same
  claim `dossier.intro` and `search.euWording`, say "original wording", no longer "the one wording
  held". An EU refusal card offers wordings by their dates ("wording of {date}", "libellé du {date}")
  and its `ambiguous_version` note says "ranks neither wording" (`refusalCardCopyFor`, now used by
  every live screen). The date-speech test now holds all 24 EU-only entries, and the EU refusal
  sentences in both languages.
- **Separators.** A label's colon and a list's separator are table entries (`dossier.titleGroup`,
  `search.ambiguousWork`, `common.listSeparator`), used by the dossier's titles, search's ambiguous
  works, history's runs and own dates, and the evaluation card's lists, so French sets a no-break
  space before ":" and ";".
- **English marked English.** On a French page, the platform's English carries `lang="en"`
  (`ENGLISH_LANG`, `inEnglish`): the not-held reasons, the rules, scopes, notes, caveats and date
  semantics, the EU acknowledgement and authenticity statement, a refusal payload's values, the
  evaluation card's reasons, notes and results, the export's watermark and rights rule. The
  sentences with no reviewed French (a transport failure, an answer a page cannot read, a request
  it will not send, the card's own reason a refusal card cannot be shown, the export's reasons) are
  said in English and marked English (`englishRun`, `statement`, `StatusSentence`). English pages
  carry no new attribute.
- **Reviewed in an addendum (2026-10-03).** The review of #912 found entries written after the
  review shipping under its receipt unreviewed. Decision 41 lets French ship only after review, so the
  same AI reviewer, under the same delegation, reviewed every one (the record's addendum, 21 forms: 17
  approved, 4 changed):
  - the driver's earlier entries: `reading.idle` (the review's proposal), `reading.intro`,
    `dossier.intro`, `dossier.euExpressions`, `search.euWording`, `dossier.titleGroup`,
    `search.ambiguousWork`, `common.listSeparator`, `refusalCard.europeCandidate`,
    `refusalCard.europeCandidateWithdrawalNotStated`, `refusalCard.europePublished` and
    `refusalCard.europeNotes.ambiguous_version`;
  - the time view's (#909): `reading.europeHoldsUntil`, and `reading.europeSameDateWorks` and
    `reading.europeUnplaced`, whose counts now read as labelled counts ("avec ce même texte détenu
    ici : {held}") because "{held} dont ce même texte…" misreads after a number; a publisher's Cellar
    work is an "œuvre de l’éditeur" (the FRBR term), never "acte" or "version";
  - the search page's under the rights rule (#910): `search.intro`, `search.idle` and `search.noHit`
    (with U+00A0 inside the guillemets).
  - `common.europeAnnexes` (the EU annex line, #919), reviewed by the same AI reviewer, under the same delegation, in
    the review of #919 (the record's addendum: both forms changed, "interrogée" for the text a search covers, as in
    `search.intro`, and "sous forme de texte" for "as text").
  No French entry ships unreviewed. `node web/scripts/refusal-sentences.mjs` prints every French
  sentence and entry beside its English.
- **Evidence.** Every live page and every census answer and refusal (with its card shown and not
  shown), each transport failure and unreadable answer, the EU reading and the evaluation card,
  rendered in English before and after: 123 renders, 10 differ, each as intended (the French link in
  the language list on the 8 pages, `search.euWording` and `dossier.euExpressions`; the two intros
  behind the list). New tests: `live-french.test.mjs` (the French pages' refusals, the unshown
  sentence's English reason, the platform's English marked on every screen, the EU card's wordings,
  the separators, the English-only states); the chrome scan's French pass (the pages compiled for
  French with the French table and refusal French pseudo-localised: no text bypasses them, and none
  of their words is marked English); `live-locale-build.test.mjs` (the product build's `/fr/` pages
  say the French table and none of the English, English as the English bundle renders it);
  `live-chrome.test.mjs` (shape, typography, the receipt). Review of #912: `live-french.test.mjs` also
  holds the evaluation card's and the export panel's English marking (removing a mark fails it), and
  the typography test requires the no-break space rather than only refusing an ordinary one. On the
  head merged with #910, the files this slice and #910 touch (`node --test --test-concurrency=1` over
  the chrome, French, search, locale, reading, date-speech, refusal and localization tests, memory
  being short during the acquisition) pass 281 of 281; CI's web job runs every file, the browser
  tests included.
- **Driver decisions (reversible).** The receipt's wording, under the owner's delegation (the review
  left it to the owner as a public claim). The review's own choices, recorded as it asked: U+00A0
  everywhere (U+202F before ; ! ? would be the Imprimerie nationale refinement); "libellé consolidé"
  rather than EUR-Lex's "version consolidée"; the compare sides "Date de départ" and "Date
  d’arrivée"; both "contient" and "détient" for "holds". The English-only status sentences are
  marked English rather than translated without review. `build-live.mjs` lost its stand-in `tables`
  hook: French is a product language now. Every live script carries both tables (a bundle cannot
  leave the other language's out of `LIVE_CHROME`).
- **Not yet:**
  - French for the sentences that have none (above), reviewed as the rest was;
  - a browser journey over the `/fr/` pages (hydration, keyboard): browsers do not run on this
    machine during the acquisition, and the journey visits English pages only;
  - the platform's English stays English: if the EU authenticity statement is ever localized, the
    review's term is "fait foi".

## The journeys in CI (sole driver, 2026-10-03)

The launch line "Journeys J1 to J8 pass in a real browser" was evidenced only by runs on the driver's machine, which
cannot start a browser while a population acquisition holds its memory. CI's new `journeys` job runs the suite on
every pull request: the API published from the commit, the journey fixture and licence-blocked mounts written by
their tests, and `web/scripts/journey.mjs` over each in the runner's Chrome, every citation a page prints verified
by the API:
- by pointer, on pages served by the Node test server (`serve-live.mjs`): the fixture mount's eight steps, the
  same with no mount, and the two EU steps, which end in `no_corpus_mounted` because the fixture mount holds no
  EU index (so CI exercises no EU answer, citation or acknowledgement); the licence-blocked mount's eight steps;
- by keyboard alone, on the pages the API itself serves (`--keyboard --served-by-api`), for both mounts: Tab
  reaches every field, every character arrives by a key press, every focus stop shows where focus is, and the
  served pages carry their security headers (review of #914).
It became a required check of `v3/integration` on 2026-10-03, once it had passed on the base (push run
37089873768): the required checks are `dotnet`, `web`, `canon-windows` and `journeys`. The new journey steps the
specification's J1 to J8 ask for (a search with no hit, a law the corpus does not hold, the contained assistant,
MCP and events) follow, each proved in this job.

The specification's journeys (the review pack's `05-user-journeys.md`) mapped onto the suite (sole driver,
2026-10-03): J1 is the reading step (every article permalink verified), J3 compare, J4 export, J8's radar
the radar step. On the fixture mount, which holds one state, J3 and J8's radar are walked in their no-change
form (the compare step asks one date twice, "The same version applied on both dates."; the radar's one-day
window shows that state "not compared: the first state this index holds"). The two-state journey mount
(`TheTwoStateJourneyMountHoldsALaterStateWithOneArticleAmended`: the fixture's work with a later state dated
2025-01-01 in which one article is amended) walks them with a real change, by pointer and by keyboard: J3
compares the two states ("1 changed, 0 added, 0 removed", the amended article named as changed) and J8's radar
lists the later state compared with the one it replaced ("wording changed from the state of 2024-02-01").
What the eight steps did not walk now runs on the fixture mount too, in the browser
(J1's refusal, J2, J5) or as API checks in the same job (J6, J7, J8's events):
- J1's refusal: the reading asked for 2019-03-15, before the work's one state, refuses `no_version_for_date`,
  saying no earlier state is held and where the history begins;
- J2: a citizen's question searched as typed ("combien de jours de congé j'ai le droit quand mon père est
  décédé") answers with no hit, and the search page now says, under its no-hit sentence, the refusal card's
  absence note ("It is not evidence that the instrument or the law does not exist."), in the reviewed French
  on the French pages;
- J4: the export pins three articles, not one ("3 articles pinned: 3 exported with text, 0 excluded.");
- J5: the dossier asked for "Circulaire CSSF 20/747" refuses `identifier_unknown` with the population this
  build searched and the absence note.
- J7 and J8 at the API, on the fixture mount (no page asks MCP or polls events): every request the journeys'
  pages make, and `events` and `answer_drift`, asked through REST and as an MCP tool call of one API process
  must answer one envelope, apart from the two fields that name the request and the moment it was answered
  (`request_ref`, `context.freshness.observed_at`), with the MCP text equal to its structured content and the
  protocol revision stated; the event log polled twice answers the same events, nothing more from its own
  `next_after`, `snapshot_unknown` for another log's cursor, every event permalink verifies, and
  `answer_drift` on the genesis log names no invalidated answer and does not assert the absence of drift.
- J6 at the API: `ask`, put a question that must be refused as legal advice, answers the contained
  assistant's card (Decision 91): the verdict `point`, `assistant_v3_unavailable`, the question not read, the
  model gloss disabled, and the reader handed to `resolve`, `as_of` and `evidence_bundle` (and `search`) on
  their served routes, nothing of the question in the card; `ask` is also in J7's REST and MCP comparison.
Driver decisions (reversible): no live page asks `ask` and none offers J2's "ask in your own words" hand-off
while the assistant is contained, since a page whose one answer is that no question is answered would add a
route to nothing; J6 is held at the API, where the containment is decided. The publishers' own search pages are
not offered on the `identifier_unknown` card: the handoff registry admits no real host yet (`HANDOFF_HOSTS`), and
adding one is an editorial decision recorded when it is made.


A failed file watch is a failure the run states, never a crash. The base's push run of 67bae40e lost its `journeys`
job to an unhandled `ENOENT ... scandir` from the recursive watch on the API's temporary directory. That directory
was removed while the watch was still open. The API journeys never stopped their watch, and a run that threw closed
the API before stopping it. Now:
- the watch records an error as an `["error", message]` event;
- the verdict says "the file watch failed, so the run cannot say the API touched no file";
- `stop()` answers the same events however often it is called;
- the API's `close()` stops the watch before removing its directory.
## EU annexes in the answers (sole driver, 2026-10-03)

An EU annex never becomes an article of the EU index, so no annex text is searched, quoted or exported. Until now
no answer said so, and no answer could show an image-only annex as "text not available, officially linked" (the
launch contract's annex line). EU `dossier` (per expression) and EU `evidence_bundle` (per wording, original or
dated) now list `annexes_not_served`: the annexes the corpus classified on the members the expression's articles
were read from. These are the Stage 3 outcomes of domain `europe_annex_body`, which the EU index copies into its
member rows. Each disposition has one row, and each row carries:
- the number of annexes and their semantic identities;
- `served_as: text_not_available`;
- the expression as `official_identity`;
- its official source: the Publications Office address of the package its articles were read from, or the work;
- a fixed reason per disposition:
  - `annex_text_not_available`: every PDF page the annex maps to is an image with no text layer (image-only);
  - `annex_body_contains_text`: a mapped page has a text layer, so the annex is not image-only. Its text is still
    not served, because annexes are not indexed;
  - `annex_body_contains_no_image`, `annex_mapped_page_outside_document` and `annex_mapping_unresolved`: the body
    was not classified as image-only.

Each of these answers' `not_held` gains an `annexes` row. It says that no annex text is held, and that an empty
list says only that the corpus classified no annex on those members, never that the act has none.

A read of the build (2026-10-03) backs that wording:
- The corpus inventories only the root `ANNEX` units of an acquired Formex package.
- A package whose annex chain fails is not acquired, and its expression has no articles.
- So, by construction, the list for an expression whose articles are served holds each root `ANNEX` unit of its
  package.
- An annex inside the main document is not listed, and neither is an annex of a package that was not acquired.

The annex's own PDF address is not carried into the index, so the link is the expression's.

The GDPR fixture holds no annex: its dossier and both bundles answer the empty list, and the samples carry it. The
grouping, the reasons and the link are held by unit tests (`V3CorpusEuropeAnnexTests`).

The end-to-end control case (#918, `EuAnnexControlCaseTests`) is the launch contract's named evidence:
- **Source.** The real 2026/1965 Formex package and work XHTML, acquired through the Formex package producer over the
  GDPR's seed. The annex title is rewritten to a sentinel in both, and the publisher's PDF is made image-only (seven
  labelled pages, each an image with no text layer).
- **Corpus and index.** The annex is classified `annex_text_not_available` on the member the articles are read from,
  and the EU index copies that outcome. No article holds the annex's text ("Hambali", a word only the annex holds)
  or its title.
- **Answers.** Search finds the main body and none of the annex. Dossier and both evidence_bundle paths list the
  annex with the official source the articles cite. No answer carries the annex's text or title.

The annex on the pages, and in a browser:
- **The pages.** The EU reading page (per wording) and the EU dossier page (per expression) say the annex line,
  `common.europeAnnexes`. For example: "1 annex of the English wording is not served as text, and is never
  searched, quoted or exported: {the platform's reason, marked English}. Official source {the source, printed, not
  linked}."
- **The reader** (`web/scripts/europe-annexes.mjs`) throws on a row it must not show:
  - one served as anything but `text_not_available`;
  - a disposition outside the corpus's five;
  - one disposition listed twice;
  - a count its digests are not;
  - a missing official source.
- **The browser walk.** CI's `journeys` job writes the control case's mount
  (`TheEuAnnexJourneyMountIsTheControlCaseWrittenWhereTheJourneyAsks`, `V3_WRITE_EU_ANNEX_MOUNT`) and walks the EU
  reading and dossier pages over it, by pointer and by keyboard on the pages the API serves.
  - Each page must say the annex line.
  - Each citation it prints must verify.
  - Neither the annex's text nor its title may appear anywhere in its text or markup (`europeAnnexExpectations`).

The launch contract's box stays unticked until the production build holds it.

## The EU export (sole driver, 2026-10-03)

The export composer now composes EU text (the launch contract's lines "Exports PDF, JSON, CSV preserve citations,
rights, watermarks and exclusions" and the annex line). Until now it showed an EU reading with no pin and said its
export was not composed.
- **What is composed.** An EU reading is pinned from as the reading screen shows it: the acknowledgement and
  authenticity statement above the text, each wording headed by its wording date, the annex line beside it, and a pin
  for each quoted article and each article held without text (keyed by the wording's digest and the provision id).
  `composeEuropeExport` (`web/scripts/export-build.mjs`) composes the pins into a model of its own,
  `lex-v3-export-eu/1`, never through the Luxembourg one (`lex-v3-export/1`, unchanged).
- **What an EU export carries.**
  - Each item: its CELEX, language, wording kind and wording date (never an applicability date), the article id and
    heading, its citation (the article permalink, which pins the wording), the wording permalink, the text with its
    digest and byte length, the body digest, the official source, Decision 95's acknowledgement and the authenticity
    statement (each item, so an item taken out of the file alone still says them; review of #920).
  - The export: the watermark, when the answering snapshot was observed, the acknowledgement, the authenticity
    statement, the platform's rights rule and its statement of what a wording date is, and the corpus, index and
    registry digests (the registry digest is the envelope's, which `readingOutcome` now passes on: the EU bundle names
    no registry).
  - JSON writes these under their own members (`wording_date`, `wording_kind`, `acknowledgement`, `authenticity`,
    `annexes_not_served`), never `applies_from`, `state_permalink` or `rights_disposition`; no EU file says
    `agreed_same_run_cc_by`. CSV (`EUROPE_CSV_COLUMNS`) repeats the watermark, acknowledgement, authenticity
    statement, observation time, rights rule and three digests on every row.
  - PDF: the acknowledgement, authenticity statement, rule and date statement on the first page; each item headed
    "001 {heading} (eng, original wording of {date})" (or "consolidated"), with its acknowledgement and authenticity
    statement; the
    acknowledgement at the foot of every page, above the page number. Its refusal (a character the standard fonts
    cannot set) is read off its own layout, so the panel never offers a PDF that then fails.
- **What it excludes.**
  - A pinned article held without text: cited (the wording permalink and the escaped provision), with the reason
    `articles_without_text` (the bundle's own list name; the API sends no reason code).
  - Every annex row of a wording an article is pinned from: the disposition, count, identities,
    `served_as: text_not_available`, the platform's reason and the official source. There is no text field to
    carry. In CSV an annex row reads `excluded: <disposition>`, with no article, citation or text.
- **The panel** says "Text served with the acknowledgement {acknowledgement}, which every exported article carries,
  and the authenticity statement below.", each item "({language}, wording of {date})", each exclusion, and the annex
  line under `data-export-annexes`.
- **Chrome.** New: `export.europeRights`, `export.europeItem`, `export.europeExcluded`. Changed: `export.intro` names
  EU text. Removed: `export.europeNotComposed`. The French was reviewed under Decision 41 in this pull request's
  review (the record's addendum: all four forms approved, with two new terminology rows, "mention de la source" for the
  acknowledgement and "déclaration sur l’édition qui fait foi" for the authenticity statement); `date-speech.test.mjs`
  holds the three new entries to no Luxembourg date word, in both languages.
- **Evidence.**
  - `export-build.test.mjs`, `export-pdf.test.mjs`, `live-export.test.mjs` and `reading-europe.test.mjs`:
    - citations, digests, language, wording date, acknowledgement and authenticity statement on each EU item and row;
    - the exclusions and annex rows, with no text;
    - no Luxembourg date field or disposition in the EU files, and no EU wording field in Luxembourg's;
    - composing refuses a pin the reader was not shown, and an export with no observation time or registry;
    - the PDF's watermark and acknowledgement on every page, its own words (the data taken out) holding no
      Luxembourg date word, the refusal per field, and the same model giving the same bytes;
    - the page's pins, the panel, the JSON shown equal to the JSON saved, the file names and the saved bytes, through
      the census envelope carrying the EU sample and the real envelope reader.
  - The chrome scan (English and French) and `live-french.test.mjs` scan the EU export view and panel with every
    article pinned; `date-speech.test.mjs` renders the EU and Luxembourg export composer that way, each in its own
    date words.
  - The hand-built EU bundle, which two test files copied, is one module, `web/scripts/europe-bundle-sample.mjs`,
    now with an article without text, an annex row, an observation time and a registry digest.
  - The Luxembourg export is unchanged: the JSON, CSV and PDF of the 14 models the existing export tests compose (12
    PDFs; the other two are refused) are byte-identical before and after this change, and so is the export page's
    markup for the census answer (26 renders, English and French).
  - The journey: `europeAnnexExpectations` gains the export step on the EU annex control mount. It pins both articles,
    and the page must show the composed panel with the annex excluded, the counts, the watermark, the acknowledgement
    and "Save as PDF", with neither the annex's text nor its title in its text or markup (the JSON shown included).
    CI's `journeys` job runs it with the other two, by pointer and by keyboard; it was not run on this machine, where
    no browser runs during the acquisition. `journey-verdict.test.mjs` holds the step's expectations.
  - `npm test` in `web/`: 1,058 of 1,058 pass (1,042 before).
- **Driver decisions (reversible).** The exclusion reason `articles_without_text`; an annex row travels with any
  export that pins an article of its wording; the date statement on the PDF's first page; the acknowledgement on its
  own footer line.
- **Not yet:**
  - an EU bundle captured in the answer census (the export's tests read the hand-built one);
  - the EU export over a real EU mount in a browser (the control case is a fixture);
  - the PDF stays untagged: the statute language is marked in the page's markup and carried per item in JSON and
    CSV, not in the PDF.

The launch contract's export and annex boxes stay unticked until the production build holds them.

## EU cases in the temporal set (sole driver, 2026-10-03)

The first gap the review of #922 found: the contract's named evidence for "no dated request silently gets another
date" now measures EU dated selection, the owner's main use case.
- **The machine gates** (`V3MachineGatesTests`) gain three arms, EU `as_of` in English, in French and with no
  language, on an EU fixture mount from the consolidated fixture: the GDPR's original wording (2016-04-27, its Formex
  date, checked from the index), two consolidated versions on 2017-06-01 whose texts differ (one with the CELEX the
  publisher gives that version, one with none), and one on 2018-07-06. Each arm asks the Luxembourg set's eight days,
  passes at 100 percent, and its date-shift control (400 days, over the six cases before the latest wording) is caught.
  - A wording is keyed by its kind and permalink (`original_wording:/eu-eurlex/...`); EU `as_of` serves no state
    digest. The digest is recomputed from the index by the stated rule. The arm fails a case whose permalink is not the
    act's wording in the language, date and digest the answer states, or whose wording postdates the request.
  - EU `in_force_on` is no arm: it refuses `retrieval_mode_unavailable` on every date, so no shift could break it and
    its control could never be caught. The gate asserts that refusal, with EU context, at every case date.
- **The mounted gates** derive EU cases from a mount's states table by the time view's stated rule, restated from the
  tables (`V3MountedGatesTests.Europe.cs`). On the machine gates' fixture the derived timelines select what its
  hand-written cases expect. The gate also passes on the three branch mounts of `V3CorpusEuropeTimeViewBranchTests` and
  on the EU-only fixture, whose census holds the original wording alone. A mount with no EU index reports its EU arm
  not measured, with the reason. The release card's target names the mount's Luxembourg and EU indexes.
- **Fixed: a consolidated version with no CELEX broke the mounted refusal and retrieval sets.** Since schema 5 such a
  version's articles hold a NULL `publisher_work_celex`, and both sets read every article's CELEX as text, so they threw
  before any card was written. They now read only the articles EU `search` and the original-wording `verify` answer by
  CELEX (the EU index reader's own condition): a CELEX, and on an index with a states table an original legal text. A
  consolidated version's CELEX sorts before its act's and is refused `identifier_unknown` by EU search, so a set derived
  from it would fail on a correct mount. `AnEuWorkWithNoCelexNeitherBreaksTheRefusalAndRetrievalSetsNorEntersThem` holds
  both, and runs both sets' EU half on that mount. The refusal set gains `eu-in-force-on`.
- **The evaluation card** grows from 6 to 9 sets, rendered in CI (`V3_RENDER_EVALUATION_CARD=1`).
  `evaluation-card.test.mjs` no longer assumes 6 sets or the refusal set's place, and holds for both cards.
- **Evidence.** `npm test` in `web/`: 1,060 of 1,060 pass. The C# was not built on this machine, where the EU
  acquisition holds the memory; CI builds and runs it.
- **Driver decisions (reversible).** EU `in_force_on` is guarded, not an arm. A request with no language is keyed by
  the answering languages' keys, sorted and joined. The mounted EU sample is the Luxembourg one: 40 acts, seed 20260930.
- **Not yet:** the EU population mount. The bounded mount cannot measure it: its EU index is schema 2, with no states
  table (review of #923).

## Next, in order (web lane; the data lane's items 1 to 3 are in STATUS-DATA.md)

A release probed without a browser on this PC (done on 2026-10-18, above), what is left:
1. A browser walk of the private candidate from a machine where a browser is allowed, before the opening:
   `deploy.ps1 ... -BrowserProbe` there, or `deploy-probe.mjs --origin <the candidate's URL> --release <its release>
   --public-key <the signing key> --browser`: the journey's real-mount steps over the population, which no release from
   this PC walks and CI never walks (its journeys walk the fixture mounts). Where it runs is the owner's to say: the walk
   asks nothing but the candidate.
2. The next release from this PC with `--no-browser` (`node --expose-gc scripts/image-rehearsal.mjs --mount ...
   --no-browser`, which installs the browser trap itself), its WSL probes then measured on the population image for the
   first time.
3. The server half of each page step without a browser: the step's own request held to the step's expectations (J1's and
   J5's refusals on the held work) and a law page's bytes as sent (its answer state, the home page's facts), so the walk
   is the only part left to a browser.

The plan's item 3.6, what is left after the switch, the sitemap and the markup (done on 2026-10-17, above):
1. The owner's O27, asked in the pull request: when it is answered yes, the deploy kit sets `LEX_V3_SEARCH_ENGINES=indexed` on
   the container app (a parameter of `deploy/main.bicep`, its default kept out), and the deploy probe's journeys say
   `searchEngines: "indexed"`.
2. Done in `data/names-r92` (STATUS-DATA.md, R92). The indexing record, R92's outcome: the data lane's measurement of the
   classes of acts that name private individuals (the
   plan's item 1.11, O28) writes `v3-corpus/indexing-record.json` beside the mount it measured, listing each work found to name
   none and the text languages its readable address answers a reading in; until then no law's page is opened.
3. The objection contact R92 and R49 ask for: not built. The owner's order of 2026-10-19 gives no contact channel, so R92
   points an act that names private individuals to the publisher instead (`data/names-r92`, STATUS-DATA.md).
4. Each article's readable address in the sitemap, once reading by article (3.7) gives an article a page of its own (the
   sitemap index past 50,000 addresses was done in the review of #976).

The plan's item 3.3, what is left after the generator (done on 2026-10-17, above):
1. Done in #974's repair: the usual form is read by the exact-reference phase to the article it cites (`usualVerdict`).
   Next of 4.2: an EU act's timeline read without its articles' text, so an act's first reference is cheap.
2. R20's own sentence for a consolidated Luxembourg text (the act as published, linked, and the acts that amended it, or that
   Lex does not yet hold their list), with the owner's item O4, in place of the site's sentence; and R19's sentence for a
   consolidated EU wording, served by the API.
3. A copy button beside each form (a script-only control, so the journeys walk its focus and keyboard first), and the
   treaties' usual forms ("Article 6 TUE", "article 267 TFUE").
4. An EU act's title, author and Official Journal reference, and a Euratom or CFSP act's domain, when an index holds them.
5. Done on 2026-10-18 (above, "The EU authenticity line by the act's date"), in the owner's words of that day: the EU
   authenticity line's date (review of #975, F2; the owner's item O4), R20's dated clause in place of the platform's
   undated sentence for an act dated before 1 July 2013, and its French, reviewed. Left: the Official Journal's own
   publication date read into the EU index, and the regulation's art. 1(2) and 3(3) checked against its text (the
   lawyer's read, O4, which the opening does not wait for).
6. The data lane's note on Luxembourg dates (review of #975), as the data lane's finding and the slice "search results by
   act" (above) state it: on `derive-c` every state's date is the publisher file's own `dateApplicability` (inventory /3's
   coordinate), equal to the catalogue's `dateEntryInForce` for 1,725 of the 1,730 states (the five that differ are listed
   in STATUS-DATA.md), so "applicable from … (publisher)" and "dans sa version applicable au …" name the date the file
   calls applicability. Index /9 dates an act whose file states none by its entry into force (`date_role`
   `entry_into_force`, S6); before /9 is served (S7a), the reader, the search's version line and the generator must say
   each role in its own words (R21: entry into force and application are separate values).

The plan's item 3.2, what is left after its second part (3.2b, done on 2026-10-16, above), as 3.2c:
1. The cited bytes of a state only a retained generation holds, quoted from that generation (the mount opens the
   generation's corpus beside its index), with `held_in` and the state that replaced it; it answers
   `pinned_digest_mismatch` today. No mount holds a generation yet.
2. "The publisher replaced this file on {date}", once the log holds when the replacement was observed (a chained log's
   `file_replaced` event carries no date; a build's time only bounds it).
3. Done on 2026-10-17 with the plan's item 3.6 (above): a permalink page showing the version its act's readable address
   shows today names that readable address as canonical, and every canonical names the page's own interface language.

The web lane's order since the owner's proxy's journey corrections (2026-10-01 14:30 UTC,
`C:\lex-v3\lanes\STANDING-ORDERS.md` section 7, overriding the queues below):
1. Pay the merge debt: this STATUS split (PR #902: the lane files and the tree allowlist), with the deployment
   kit's browser probes (#901) folded in; #893 rebased and merged; #891 and #897 merged.
2. Serve EU text now from what the EU index holds: EU `evidence_bundle` and the EU reading screen over
   the original wording, carrying "© European Union, https://eur-lex.europa.eu" and the statement that
   only the electronic Official Journal is authentic (Decision 95), with every journey citation
   verified.
3. The EU time view (`as_of`, `timeline`, history, compare, radar) on the data lane's EU states table
   as it lands, the API and screens built against its schema early.
4. When the queue is clear: the image-only annex control case (search, quote and export keep it out,
   officially linked), and the J1 to J8 mapping (done above, 2026-10-03). The 2024/1620 annexes the
   three-seed retry retained are structured text, not image-only (`C:\lex-v3\lanes\image-only-annex-handoff.md`,
   `annex-2024-1620-control-audit.json`), so they are no candidate; no real image-only EU annex is held on this
   machine yet. EU dossier and evidence_bundle now list each annex the corpus classified as
   `annexes_not_served`: `text_not_available`, linked to the expression's official source. The synthetic
   end-to-end control case holds that through the acquisition (#918), and the EU reading and dossier
   pages say the annex line, walked in a browser over the control case's mount (above, 2026-10-03). The
   EU export lists the annex as excluded, never its text, and its journey step joins those walks (the EU
   export, above).
A pull request that does not move a launch-contract line, or directly unblock one, waits. Merges take
turns through `C:\lex-v3\lanes\MERGE-LOCK`, and related work goes in one bigger pull request.

The web lane's next order, from the panel's answer to Q-20261001-1316-claude (2026-10-01 13:20 UTC),
which follows the items below:
1. **Real chained canary builds, authorised.** They run after the data lane's active EU acquisition
   ends:
   - bounded EN/FR canary scope through `Lex.V3.Tool` (GDPR, `32016R0679`, and the Luxembourg act
     range a439), at most 800 publisher requests per build, under every standing rule;
   - the 2026-09-30 canary mount is schema 6, which `LuxembourgIndexPredecessor.TryRead` refuses as a
     predecessor, so build A is a fresh build from current integration and build B is chained to it:
     two runs, at most 1,600 requests;
   - both mounts and their custody are kept as evidence;
   - the journeys, the gates, G1/G3/G4, `events`, `answer_drift`, `as_observed` and a release
     rehearsal then run over the chained mount.
   The launcher `C:\lex-v3\lanes\claude-chained-canary.ps1` is waiting for the local heavy-job slot.
   It builds the tool once from a clean `origin/v3/integration`, freezes it with the renderer sources
   and their digests, then runs both builds into `C:\lex-v3\chained-canary-20261001\{a,b}`. Its
   ledger is `C:\lex-v3\lanes\claude-web-notes.md`.
2. **The release path's custody half (done, 2026-10-02):** `node web/scripts/image-rehearsal.mjs --custody
   <custody> --checkpoint <mount-inputs.json> --tool <Lex.V3.Tool.dll>` runs the data lane's offline
   `derive` twice in separate processes (`derive-twice.mjs`), compares every mount file byte for byte,
   and only then builds, rehearsal-signs, probes and publishes the image over the first derivation. The
   tool must be this commit's CI runtime artifact (its `source-head.txt` equals the checkout the image is
   built from; `--allow-unbound-tool` runs a rehearsal that says otherwise), and the report carries the
   tool's and the checkpoint's digests, every file's digest and both executions. That derive sends no
   publisher request rests on its code path (derive takes only the custody store and constructs no
   publisher session, and every replay door refuses when any wire budget was spent); a proxy-variable
   trap is a narrower second check, which does not see
   the publisher client (built with `UseProxy = false`), raw sockets or DNS, and the report says so
   (review of #911). `derive-twice.test.mjs` holds it to a stand-in tool that agrees, differs in one file,
   or connects through the proxy variables, the binding to the CI artifact's stamp, and the release's
   refusal of a tool that is not bound (re-review of #911). The real run
   waits for the complete populations' custody. Originally:
   **Then the release path's custody half**, once the data lane names its offline derive command:
   the release command runs it twice, compares every digest, then images, signs with the rehearsal
   identity, publishes and reads back. If the command is not named by the time item 1 is done, the web
   lane asks in QUESTIONS.md.

The web lane's order, from the panel's answer to Q-20261001-0345-claude (2026-10-01 03:50 UTC),
recorded by PR #862:
1. **Predecessor chaining, claimed by the web lane** (item 4). It unblocks G1, G3, G4, `as_observed`
   and `knowable_on`. The web lane changes only the event-log and chaining parts of the builders,
   rebases onto the data lane's merges before every pull request, and gets a Codex review of each.
   Any overlap with an open data-lane pull request goes to QUESTIONS.md rather than being edited
   around. The planned slices, one pull request each:
   - index schema `/7` with the genesis log, an `observations` table and a log stamp (PR #864);
   - the predecessor as a build input (`--predecessor`), its log carried forward as an exact
     prefix (G3a) (PR #866);
   - comparison events: `first_sighting` and `expression_added` for new keys, `file_replaced` when a
     source body changes (G1), and the derived `interval_closed` and `validity_revised` (PR #867);
   - `events` and `answer_drift` across the chain (folded into PR #867 by its review);
   - each build's time in the log (PR #871), then `as_observed` by build snapshot (G4) (PR #874);
   - a generation mount (G3b), retained by S7-A09: generations written and verified (PR #880),
     held to the log by the mount with the depth reported (PR #885), and quoted by `as_observed`
     and `verify` (PR #889).
   `observed_from` stays null until a Luxembourg body's capture time reaches the corpus (data lane);
   `knowable_on` and withdrawal follow it. The owner questions (G1 when a file changes but its text
   does not, "as observed" identified by snapshot with no time, retaining every generation) were
   answered by the panel on the owner's behalf; PR #871 records the answers.
2. The index capability manifest's per-operation rows for the unserved operations: one small
   additive pull request (PR #891).
3. A credential-free deployment kit, so the owner's go-live is one command (item 7; PR #890). It holds the
   Azure definitions for the one-server container, and a deploy script that takes the subscription,
   the managed identity and the signing identity as parameters and never reads or stores a secret.
   It validates the templates offline, runs the zero-traffic probe against the deployed revision,
   removes the revision, and has a runbook here. No Azure login, deployment or production signing:
   those stay with the owner. Kit 2 adds the browser probes against the candidate (`--browser`, the
   journey's ten real-mount steps), states that privacy is not observable on a remote revision, and
   prints rollback and forward again.
4. Standing: the real-mount journeys and gates on every new mount the data lane builds, and a
   review of the data lane's pull requests that touch the API when asked.
   - 2026-10-01 (PR #893): the two data-lane mounts newer than the real bounded first mount. Both
     are schema 6, and #878 serves them again.
     - Mounts:
       - `C:\lex-v3\eu-three-seed-retry-20260930-1\v3-corpus`: 58 members; its EU index holds the
         SFDR, 32019R2088;
       - `C:\lex-v3\bounded-en-fr-canary-20260930-1\v3-corpus`: 18 members; the GDPR in English and
         French.
     - The mounted gates (`V3MountedGatesTests`) and the EU permalink check pass 21 of 21 on each,
       and on the first mount.
     - The EU permalink check searched the GDPR in English, which only some mounts hold. It now takes
       a work, its language and a word from the named mount's own EU index, and verifies every hit's
       permalink in that language. On the three-seed mount that is a French SFDR search.
     - The real-mount journey (`journey.mjs --real-mount --served-by-api`):
       - three-seed mount: 10 of 10 steps pass. Coverage and radar answer, and the other screens
         show their refusal cards; the two EU steps ask for the GDPR, which this mount does not hold;
       - canary mount: 10 of 10 steps pass; coverage and radar answer, the Luxembourg screens show their refusal cards, EU search answers with 61 citations verified, and EU dossier with 2, one per held expression (English and French).
   Reversible driver decision, 2026-10-01: with items 2 and 3 in review, the web lane runs replay G1,
   G3 and G4 on the real handler (PR #897), the launch contract's machine-gates line. The custody
   half of the release path's first line waits for the data lane's offline derivation.

The plan, in order (its items 1 to 3 are the data lane's, in STATUS-DATA.md):

4. Serve the unserved operations for Luxembourg (`verify` and `relations` served by PR #753, MCP
   over streamable HTTP by PR #754, `evidence_bundle` by PR #755, `classification` and
   `manifestation` by PR #756, `status_on` and `browse` by PR #757; a request to an unserved
   operation answers the transport failure `operation_not_served`, PR #758; `ask` answers the
   contained `assistant_v3_unavailable` card, PR #759; `events` and `answer_drift` over a genesis
   log, PR #760; `as_observed` by build snapshot, PR #874). Three remain (`knowable_on`,
   `concepts`, `transposition`), all needing data the ingest does not produce; they keep `operation_not_served`, and since PR #857
   the coverage answer names, for each, the data that would serve it (driver decision, below), which
   Trust and Coverage shows. The event log's next step, predecessor chaining with observation
   times, is the web lane's since 2026-10-01 and is claimed by PR #862 (the web lane's order,
   above).
5. EU parity: every temporal and search operation from the EU index; French expressions. EU
   `search` in one work served by PR #761, EU `dossier` by PR #762. EU `evidence_bundle` and the EU
   reading screen over the original wording do not wait on consolidation: the EU index already holds
   the original wording, so they are the web lane's next slice (the owner's proxy, 2026-10-01 14:30
   UTC, correcting this item, which said they waited on consolidation acquisition). The EU time view
   (`as_of`, `timeline`, history, compare, radar) waits on the data lane's EU states table. The parity
   details are driver decisions (below). The EU permalink grammar is the web lane's since 2026-10-01 (the panel's answer
   to Q-20261001-0108-claude). It works in the API and the web; the EU index schema and the Ingest
   builders stay the data lane's. PR #850: EU hits carry a hash-pinned permalink, and EU `verify`
   is served over it. PR #853: the search screen reads and shows EU answers in one named work. PR
   #854: the journey searches the real mount's GDPR, and `verify` confirms all 61 EU citations the
   page prints. PR #856: the EU dossier pins each expression's wording, and the dossier screen and
   journey show it. The EU screens that need dated states (history, compare, radar, and reading at a
   date) wait on the data lane's EU states table, as above.
6. Wire the eight launch screens to `/api/v3`; journeys J1 to J8 in a real browser. PR #763: the
   envelope reader and the client module; PR #764: the live Trust and Coverage component; PR #765:
   its page, the live build and the one-origin server; PR #766: the first browser journey step,
   passing against the real API with and without a mount; PR #771: the V3 search reader
   `readSearch`, held to five search answers the census now samples; PR #772: the live search
   screen and its page; PR #773: the search journey step (type, submit, the answer; the next page
   waits for a mount with more hits than one page); PR #774: the V3 dossier reader `readDossier`,
   held to the dossier answer the census now samples; PR #775: the live dossier screen, its page
   and journey step; PR #776: the reading screen's reader `readEvidenceBundle` over
   `evidence_bundle`; PR #777: the live reading screen, its page and journey step. The four launch
   screens with V3 readers (coverage, search, dossier, reading) are now live and journeyed. PR
   #778: the provision history reader `readArticleHistory`; PR #781: the live provision history
   screen and journey step; PR #782: the compare reader `readDiff`; PR #783: the live compare screen
   and journey step; PR #784: the radar reader `readChanges` over `changes_in_period`; PR #787: the
   live radar screen and journey step; PR #788: the API serves the live pages with the security
   headers (ruling 3); PR #789: the live export composer and its journey step. All eight of the
   launch contract's screens are live and journeyed; PR #790: the PDF export; PR #792: the
   evaluation card on the Trust and Coverage page (ruling 2); PR #793: the refusal sentence list
   for the checkpoint (ruling 4); PR #794: the absence refusals carry the card's evidence, so the
   live pages show them as cards; PR #796: every citation the served answers emit verifies; PR #797:
   the interface languages, German and Luxembourgish (and French until reviewed) answering
   `localization_unavailable`; PR #800: the live pages' chrome in one table; PR #801: the API process
   records nothing while a browser asks it; PR #802: the keyboard and screen-reader paths (live
   regions, and the journey's keyboard mode); PR #803: the search, dossier, reading and history
   screens' sentences in the chrome table, with French drafted beside them; PR #804: the compare,
   radar and export screens' sentences; PR #805: every citation the journey's pages print verifies;
   PR #806: no interface text on a live page bypasses the chrome table (a pseudo-locale scan); PR
   #807: the evaluation card's words in the table; PR #809: the refusal card's words in the table;
   PR #810: Trust and Coverage's words in the table, so the chrome scan exempts nothing; PR #811:
   the accessibility and scope line held on the live screens; PR #813: a reviewed language builds
   its own pages; PR #815: the eight screens against the real bounded mount; PR #834: the
   licence-blocked journey. The French interface ships (branch `writer/french-chrome`): reviewed by
   the AI reviewer under the owner's delegation of 2026-10-02 and accepted by the owner on 2026-10-04, the reviewed table in
   `LIVE_CHROME` and `fr` in `REVIEWED_CHROME_LOCALES`, which builds `/fr/*.html` (Decision 41); the
   owner may revise any entry. Hosting (ruling 3): `Lex.V3.Api`
   serves the live pages on the API's origin with `frame-ancestors`, HSTS and `Referrer-Policy`,
   and a live page never shows the synthetic banner on a real mount. J1 to J8 are restated as V3
   steps by the driver (they exist only in the pre-V3 pack, `05-user-journeys.md`).
7. Release pipeline: build, sign, image, zero-traffic deploy, probes. Then acceptance and promotion.
   The steps that need no production credential are the web lane's since 2026-09-30 (the owner's proxy).
   PR #821: one command builds, verifies, rehearsal-signs and removes the one-server image; PR #825:
   it runs the image (WSL, read-only root, private /tmp) and probes the eight screens against it; PR
   #826: the image is reproducible (two builds from scratch, one manifest digest); PR #828: the
   image probed on the journey's fixture mount, where every screen answers and 105 citations verify;
   PR #831: V2 absent from the image (no V2 assembly or dependency; of V2's 63 routes, a V2-only
   one answers 404 and a path both share answers with V3's own bytes); PR #833:
   the release assets (the image, its signature, the evaluation card, the mount's report, a signed
   manifest) published under a version, read back and verified, and the card served at
   `/evaluation-card.json`. The credential-free release steps
   are rehearsed end to end, and since 2026-10-01 over the real bounded first mount too (PR #862).
   Next in the web lane's order: a credential-free deployment kit. Production signing, credentials
   and deployment stay with the owner.
8. Machine gates (launch contract, Evaluation): the temporal, refusal and retrieval case sets run
   against the real handler, and all three shuffled controls are caught (PRs #767 and #768). The
   evaluation card is rendered from them with the statistical rows `not_yet_labelled` (PR #769).
   Ruling 2: it is served on the Trust and Coverage page (PR #792) and beside the release assets, and the
   launch card carries the machine gates run over the real mounted corpus, so the gates gain a
   mounted-corpus run once the first mount exists. Replay G1 to G5 (`33-product-spec.md`): G2 snapshot
   determinism and G5 independent verifiability run on the real handler (PR #770). G1 version
   immutability, G3 bitemporal completeness and G4 as-observed answering run on the real handler
   over a three-build chain (PR #897), now that predecessor chaining is merged. What
   is left of the launch contract's machine-gates line after that was "V2 absent from the image",
   which the image rehearsal now checks (PR #831, item 7). Ruling 2's gates over the real mounted
   corpus: PR #838 derives the temporal set from any mount, PR #839 the refusal set, PR #842 the
   retrieval set, and PR #844 makes the release card the gates run over the rehearsal's own mount.

## Driver decisions (reversible)

Each is the driver's call under ruling 7 and can be reversed by a later pull request that says why.

- The permalink's digest (R64, 2026-10-15): "that language's body" is read as the digest each publisher already mints
  per language, Luxembourg's `state_sha256` and the EU `wording_sha256`. Both cover one expression in one language and
  pin its text through every article identity, a digest of the article's text tokens; no new digest is minted. The
  reason: a digest of the publisher's raw bytes would change with a re-wrapped file whose text did not change, and
  minting one would re-derive every index and break every pin for no reader-visible gain. What the reading differs in,
  said here so it is not taken for more: both digests also cover the expression's identifiers and the date, and
  Luxembourg's covers its rule profiles (a re-derivation under a new profile changes it) and sorts the article identities
  (an order change alone does not). Reversible by minting a body digest and a new permalink generation, with the moves
  below.
- The shapes before R64 (2026-10-15): an EU wording's old permalink moves for good (`301`) to the new one, under the
  interface language it had and with its query, the same answer for every reader; it is computed from the old address
  alone. A Luxembourg state's old permalink names no language, so nothing is computed from it alone, and it is answered
  as an unknown address, as the task's rule says; the index could find the language by the digest, but a redirect that
  reads the corpus would answer differently per release. No public link exists to break: the site has never been open.
  The API's operations read only the new shape. Reversible: a move for the Luxembourg shape through the index.
- The text language in the address (2026-10-15): three letters, the publisher's own code (`fra`, `deu`, `eng`), as the
  EU permalink already wrote it and the API's `language` parameter takes it; a Luxembourg reading at a permalink now asks
  in that language, so a page shows the language the permalink pins and not every language held on the date.
- A permalink's page asks by the permalink (2026-10-16, the plan's 3.2b): `evidence_bundle`, `as_of` and `provenance` take
  a state's (or an EU wording's) hash-pinned permalink as the identifier, with its own date, and answer the version it pins
  or refuse (`pinned_digest_mismatch` with the current link, as `verify`), never another version in its place; the reading
  page asks that, and the stale page of #967 (another version under a notice) becomes the refusal with the current link.
  The reason: R64 says a digest Lex no longer holds "answers `pinned_digest_mismatch`", and one request, the server's and
  the browser's alike, cannot both show another version and refuse. Another date beside the permalink is
  `request_schema_invalid` rather than a refusal: the request names two versions. An article's permalink is not taken (it
  names an article), as `resolve` does not take it. Reversible: a `digest` parameter beside the work and date (it moves
  the registry digest).
- The rest of the act after the cited article (2026-10-16): it follows, whole, on the same page, the cited article in its
  place too, rather than being linked. The API answers a whole version until reading by article (3.7) pages it, so a page
  carrying only the article would still carry the whole answer in its data block; the cited block links the whole act at
  its own address too. Reversible with 3.7, which renders the article first and loads the rest on demand.
- "The publisher replaced this file on {date}" is not said (2026-10-16): the typed fact exists (a chained log's
  `file_replaced` event, which `events` serves with `replaced_permalink`), but it holds no date of the replacement
  (`observed_from` is null; a build's time only bounds observation from above), and R64's sentence states one. A stale
  permalink is refused with the current link instead. Reversible when the log records when a replacement was observed.
- A state only a retained generation holds (2026-10-16): `evidence_bundle` at its permalink answers
  `pinned_digest_mismatch` naming the current state, while `verify` verifies it there (`held_in`): the mount keeps a
  generation's index, not its corpus, and a bundle names each body's digest from the corpus. The two answers are about
  different questions (is the digest one Lex verified once; which version this site holds now). Reversible (3.2c): keep
  each generation's corpus view and quote from it.
- The banner (2026-10-16): "A newer version is applicable from {date} (publisher)." for a Luxembourg state that is not
  the latest held, R21's words for the publisher's applicability date, rather than R64's "applies from", which R21 does not
  forbid but which states the applicability without its attribution; "A later wording of this act is held: the wording of
  {date}." for an EU wording, whose date is never an applicability date. Each links the act at today's date (its readable
  address), never a newer version's permalink, which the answer does not name.
- Whether the next EU wording's text is held (2026-10-17, the review of #972): `next_text_held`, a boolean beside
  `next_date` (null with none), rather than the date of the next held wording: the next wording bounds the one shown
  whether or not its text is held, and a reader told only of a later held one would not learn that an unheld one stands
  between. An unheld next wording is said as the publisher's record names it, with no link to the act today. Reversible.
- A consolidated version's readable address (2026-10-17, the review of #972): it moves for good (`301`) to its act's
  readable address, computed from the address alone (EUR-Lex writes a consolidated version's CELEX as its act's in sector 0
  with the consolidation's date), so every reader gets the same answer and no address names one wording while showing
  another. The version itself stays citable by its permalink, and its dossier address stands. Reversible: a readable
  address per wording, with its own date.
- The readable address (2026-10-16, R67): `/lu-legilux/{work}/{lng}[/{article}]` and `/eu-eurlex/{celex}/{lng}[/{provision}]`,
  the text's language in the place a permalink's pin stands, at today in Luxembourg; the act with no language stays its
  dossier. The language is the address's, so a reader's link opens the text in the language they cited. An article segment
  that is a calendar date, with or without a digest, is none (an EU stable coordinate, the EU shape before R64). Its page
  names itself canonical. Reversible: another segment order, with moves.
- The cited article's title (2026-10-16): "{work}, {article}" (the work key and the publisher's id): the reading's answer
  names no act title, and the dossier's titles are another answer. Reversible when the bundle carries the title.
- The canonical link (2026-10-15, the plan's 3.2): every law page under `/fr/` or `/en/`, the reading and the dossier
  alike, names the address with no interface language as canonical, the permalink Lex prints and cites, and each
  language's page and `x-default` as alternates. A canonical that answers a redirect is a weaker signal to a search
  engine than one that answers 200; the plan chose the address Lex cites, and the sitemap and indexing (3.6) revisit it.
  Revisited on 2026-10-17 (3.6, the top of this file): the canonical names the page's own interface language, and a
  permalink of today's version its act's readable address; the alternates stand.

- The sole-driver period (the owner's order of 2026-10-02: one Claude driver, no Codex, no other
  Claude, full authorisation, never ask the owner): each pull request is reviewed by an independent
  Claude subagent with a fresh context, read-only, reporting only material and reproduced findings
  (`C:\lex-v3\lanes\claude-review-instructions.md`); one repair round, then merge on green CI. The
  owner's open questions are decided by the driver under that delegation and recorded here.
- Documentation-only pull requests while a population run is active (2026-10-04). Such a pull request changes
  `OWNER-BRIEF.md` and the status files only: no code, CI or tests. It merges on green CI without the subagent
  review, and the next reviewed pull request reviews it. Code pull requests still wait for their review.
  - The reason: on 2026-10-04 a review subagent twice drove the machine's commit headroom to 0.4-0.5 GB, the EU
    runner's stop line.
  - The owner's ruling wants the brief current whenever a run starts, ends or stops.
- EU annexes (2026-10-03): dossier and evidence_bundle list the annexes the corpus classified under
  `annexes_not_served`. Every one is served as `text_not_available`, whatever its disposition, and linked to its
  expression's official source. No annex text is served even where the publisher's PDF has a text layer; that
  waits until annexes are indexed as units of their own, with their own rights rule and citations.
- The six refusal codes no operation produced at launch (five since 2026-10-27; the refusal census's `not_produced` list),
  each unreachable by construction, decided under that delegation (2026-10-02). The launch contract's
  refusal line holds them in the closed registry with their payloads; the refusal case set measures the
  fourteen produced codes.
  - `advice_boundary`: no model plans an answer (`ask` answers the containment card
    `assistant_v3_unavailable`, PR #759), so no question is ever classed as legal advice; a future
    assistant's planner produces it.
  - `derivation_refused`: the API serves no derived text and no operation derives at request time;
    derivation happens at build time, where a refusal is a failed build, never a served answer.
  - `not_transposable`: `transposition` is unserved (`operation_not_served`, with the data that would
    serve it in `coverage`, PR #857); a served transposition answers it for an instrument that is not
    a directive.
  - `out_of_corpus_scope`: an identifier neither index holds answers `identifier_unknown`, naming this
    service's own `search` as the way forward (`official_search_actions`); a list of publishers known to
    be out of scope (a CSSF circular, a court decision) would produce it, and none is kept at launch.
  - `rate_limited`: **reversed on 2026-10-27** (R109 reverses it by name): the per-client limit ships, in memory, keyed by
    a keyed hash of the address, and every served operation produces `rate_limited` past it (the section "The per-client
    limit, the key set, the headers on every answer, the outside monitor and the runbook", above). The decision it
    replaces: no per-client limit at launch, since the host kept no IP address or user agent, with cost under abuse
    bounded by one replica per revision. Five codes stay unproduced.
  - `upstream_unreachable`: serving never contacts a publisher: the API holds no HTTP client, and the
    only publisher client (`RoutedHttpAcquisitionSession`) is built by acquisition, which the API never
    references; a mount is served from its own files, so no upstream can be unreachable at request time.
- The rights question PR #842 surfaced, decided under that delegation (2026-10-02): a licence that does
  not admit a text keeps it out of search matching too. A search hit says which articles hold a word,
  which is information read from the text; `search` now matches only states whose text
  `evidence_bundle` would quote (every source acquired and admitted by its rights), and states that
  rule (`SearchRightsRule`). It counts nothing about the text it does not match, since a count of
  withheld matches would say the same thing. The search page says the same: its intro, its prompt and
  its no-hit sentence speak of the text this server holds and may search, and the intro says text its
  rights withhold is not searched (review of #910: on the licence-blocked journey the old no-hit
  sentence said the held text lacked a phrase it holds). The French draft says the same.

- The image rehearsal (PR #821):
  - It builds with the .NET SDK's container support, needing no daemon: this machine has no
    container runtime, and the base image stays pinned by digest in the project.
  - It bakes the mount into the image, so the image digest pins the corpus it serves.
  - It signs with a key made for the run and never kept, labelled a rehearsal. The release identity
    is the owner's.
  - Running the image comes next: the image's own filesystem under this machine's WSL Ubuntu, with
    no install and no daemon. The alternative, a CI runner with Docker, is kept for when a hosted
    run is wanted.
- The reproducible image (PR #826):
  - The app layer is stored uncompressed, so its digest depends only on its tar and not on the
    compressor a machine has (gzip output depends on the zlib build). The cost is size: 17 MB against
    the SDK's 6 MB gzip on the bounded mount. A pinned compressor can replace it if the full corpus
    makes the size matter.
  - The source date is the commit's time (the `SOURCE_DATE_EPOCH` convention): the image says when
    its sources were committed, not when it was built. The product reads no file modification time
    (`V3WebRoot` holds the pages in memory), so the choice changes no answer.

- The EU search screen (PR #853): an EU answer whose hits pin no wording is said as unreadable, not
  shown unpinned, since every citation the live screens show must pin its digest. An EU hit is a
  "wording of" its date, and the French draft calls the one held wording a "libellé".
- The EU permalink grammar (PR #850): language as a path segment, not only a parameter, so one
  grammar serves English and French expressions. The wording digest is computed in the API (the
  panel's boundary), not stored in the EU index. If the data lane later stores a wording digest, the
  API can read it instead, as long as the value stays the same.

- The refusal card's payload rows keep the payload's own member names (`requested_identifier`,
  `asserts_absence_of_law`) as their labels, in every interface language (PR #809). They are the
  contract's names, shown with the refusal code beside them, and a reader who quotes them can find
  them in the envelope. A labelled form would be a translation of the contract, which the owner can
  ask for.

- Absence refusals (PRs #775, #777, #778): option (a), the producer carries them. The web refusal card
  (`refusal-card.mjs` `ABSENCE_CODES`, from `35-ideal-ux`) requires every absence refusal to carry
  `what_would_answer` from its closed vocabulary (`corrected_identifier`, `new_official_observation`,
  `expanded_official_scope`) and `asserts_absence_of_law`, and `identifier_unknown` to carry a
  `population_disclosure`. The reviewed registry requires `what_would_answer` (as free text) for
  `identifier_unknown` only, and neither `asserts_absence_of_law` nor `population_disclosure` for any
  code. Three produced refusals therefore fail the card (`refusal-payload-samples.test.mjs`
  `KNOWN_BREAKS`): `identifier_unknown` (free-text `what_would_answer`, no disclosure),
  `no_version_for_date` (the nearest dates, neither absence field) and `anchor_not_in_version`
  (`nearest_anchors`, neither absence field). `text_not_available` conforms. Done in PR #794: the
  mount adds the fields to all three, because the rule protects the product's oldest invariant (an
  absence of a record is not an absence of law) and must travel over MCP too.
  - `what_would_answer` is the vocabulary list: `identifier_unknown` corrected identifier and
    expanded scope, `no_version_for_date` a new official observation, `anchor_not_in_version` a
    corrected identifier and a new observation. `asserts_absence_of_law: false` on each.
  - `identifier_unknown`'s prose moves to `what_would_answer_detail` ("a hash-pinned permalink of a
    state this index holds at that stable coordinate" and the like), and it carries a
    `population_disclosure` counted from the mounted index ("This build's Luxembourg index holds 1
    Luxembourg work, with states dated from 2024-02-01 to 2026-04-11"; the EU index counts its
    members).
  - The registry lists mandatory field names only and the refusal schema leaves payloads open, so
    the registry digest and the contract are unchanged; the refusal-payload and envelope censuses
    are rendered again. `KNOWN_BREAKS` is empty, and the live pages show those refusals as cards.
- EU parity (PR #761): (a) the Formex act date (`wording_date`) is the EU wording-state date, and
  `official_consolidation_state` fits an answer over an original wording; (b) the original wording
  does not answer EU `as_of` for dates after it when no consolidation is held; consolidation
  acquisition follows the first mount; (c) EU search stays scoped to one work at launch (no
  `jurisdiction` parameter); (d) moot under ruling 1 (EU text is shown).
- The event log (PR #760): (a) the mintable registry is twelve names (the coverage event is not
  minted); (b) launch may ship genesis-only logs (append-only within a log; a rebuild or rollback
  starts a new log with new cursors), with predecessor chaining after the first mount; (c) a cursor
  from a retired log refuses `snapshot_unknown`. Amended by PR #867: a build chained to a log carries
  it forward with the same numbers, so a cursor of an ancestor the mounted log names
  (`log.ancestors`) reads on; a log not chained to the mounted one is still retired.
- The build time of the event log (PR #871) is an explicit build input, which the first-mount
  build reads once from its clock and passes to both index builds. The builder never reads it
  itself.
  - The launch contract's two-build line still holds: two independent executions given the same
    inputs, the build time among them, give the same bytes. A build that must reproduce another
    takes the other's time.
  - Rejected: the predecessor's build-report time. It is not hashed, anyone can edit it, and it
    would leave the mounted generation with no bound.
- `ask`'s containment card keeps the `point` verdict.
- The operations with no data (`knowable_on`, `concepts`, `transposition`; `as_observed` until PR #874) keep
  the typed transport failure `operation_not_served`, and the platform states, per operation, that
  it is not served and which data would serve it. PR #857 states it in the `coverage` answer
  (`operations.not_served_data`), which the API owns, rather than in the index capability manifest,
  which the data lane's builders write. The data named are the specification's own
  (`33-product-spec.md`). The index manifest can carry the same rows later without changing them.
  PR #891 makes it so: the table is `V3UnservedOperations` in Contracts, which the API's `coverage`
  reads and both index builders state in every capability manifest they write (`not_served`).
  - Each row states the operation, the typed reason a request for it answers (`operation_not_served`,
    the transport failure; any other reason is refused) and the data that would serve it. The reason
    was added by the review of #891, since the launch line asks for the reason to be in the manifest.
  - The manifest schema stays `/1`: the field is additive and written only when stated, so an
    earlier manifest keeps its exact bytes and reads back with none. That covers the real bounded
    first mount's manifest and the data lane's running EU population's.
  - The reader's canonical re-render holds either shape exact. Malformed rows refuse
    `malformed_not_served`, and an empty list or rows out of order are not canonical.
  - A manifest's rows are what the platform did not serve when it was built; `coverage` keeps
    reporting today's routes.
  - This made the launch line "all 27 registered names, each either served or refusing with a typed
    reason its capability manifest states" hold for the three unserved operations only. It did not
    hold for EU identifiers: 13 routed operations refuse an EU identifier `retrieval_mode_unavailable`,
    and no manifest stated it (found by the sole driver's audit, 2026-10-03; the earlier statement
    here that the line held on a fresh build was wrong). The capability slice below states them.
- **The EU capability rows (sole driver, 2026-10-03).** Each EU capability manifest now states the
  operations an EU identifier is refused, so the launch line holds for EU identifiers: the 13 refuse
  every EU-shaped identifier the mode, held or not, since they serve none; the operations that serve
  EU acts (`as_of` and `timeline` where the EU index holds a census, `dossier`, `search` and
  `evidence_bundle`) answer an EU-shaped identifier the EU index does not hold `identifier_unknown`
  with EU context:
  - The manifest's `not_served` rows may carry a second reason, `retrieval_mode_unavailable` (the
    registry's refusal for an operation routed for another publisher's identifiers only), beside
    `operation_not_served`; any other reason stays `malformed_not_served`. The row shape is
    unchanged, so the schema stays `/1`.
  - `V3EuropeRefusedOperations` (Contracts) lists the 13: `answer_drift`, `article_history`,
    `as_observed`, `changes_in_period`, `citation`, `cited_by`, `classification`, `diff`,
    `in_force_on`, `manifestation`, `provenance`, `relations` and `status_on`, each with what would
    serve it. The EU index builder states it beside `V3UnservedOperations` (16 rows); the Luxembourg
    manifest keeps its 3. Several are data held but not served, and the rows say so (review of #913):
    the Formex references are extracted with each article and held as reference tokens (`citation`,
    `relations`), and the census acquires the force facts and date axioms into custody without any
    build indexing them (`in_force_on`, `status_on`).
  - `coverage` reports the mounted EU manifest's refusals (`operations.refused_for_eu`, with a note)
    when an EU index is mounted; a mount with no EU index answers the block exactly as before, so
    the answer and envelope samples (a Luxembourg-only mount) do not change.
  - `V3CorpusEuropeCapabilityMountTests` holds it on the time-view mount: every stated row is the
    refusal an EU identifier receives (code, EU context, the registry's fields), the seven operations
    the time view serves for an EU act answer (`as_of`, `timeline`, `evidence_bundle`, `verify` of
    a pinned EU permalink, `dossier`, `search` in one EU work, `resolve`), and the 27 registered
    operations are each accounted for once (the four left take no work identifier: `ask`,
    `browse`, `coverage`, `events`).
  - Attribution follows the identifier (reviews of #913): this service's own EU coordinates
    (`/eu-eurlex/…`, as a path or under its origin) are EU-shaped, so a refused operation refuses
    them the mode with EU context and `resolve` answers them with EU context (they were answered
    "no publisher shape" with Luxembourg context); `as_observed` by time refuses an EU identifier
    the mode, as by snapshot (it answered `snapshot_unknown` with Luxembourg context).
  - `as_of` and `timeline`, where the EU index holds a census: an identifier the census does not
    list answers through its act when the EU index resolves it (another form of the original
    wording's identifier, or a provision coordinate that resolution checks against its articles) or
    when it is a provision coordinate of an expression the census lists whose articles hold that
    provision (a consolidated expression's); any other EU-shaped identifier is `identifier_unknown`
    with EU context, as `dossier` and `search` answer it: an unknown CELEX, a provision the
    expression does not hold, and this service's own coordinates, which name a wording rather than
    an act (as Luxembourg's `as_of` refuses its own permalinks). On an EU index with no census (an
    empty states table, or a schema before 5) they keep the mode refusal.
  - Not covered by a manifest row, by design: request variants that refuse within a served
    operation (`verify` of a bare EU identifier rather than a pinned permalink; `search` in an EU
    work at a date, since the search reads the original wording only). Real EU mounts built before
    this slice keep a manifest that states the three unserved operations only, and `coverage`
    reports what the mounted manifest states, so it lists no EU refusal there (a test rewrites a
    mount's manifest to that earlier form); the final mount is derived from custody by the release
    command, so it carries the 16 rows.
- The web hosting shape of ruling 3 is `Lex.V3.Api` serving the built live pages beside `/api/v3` and
  `/mcp`, with the security headers, rather than an ingress split.
- Exports (PRs #789 and #790): JSON, CSV and PDF are written from one model, so they cannot
  disagree. The PDF uses the standard fonts only (no embedded font), so a text holding a character
  outside WinAnsi, other than a typographic space or hyphen, is refused as PDF with the characters
  named rather than set with a substitute; the PDF is not tagged, so it does not mark each text's
  statute language (the JSON carries the language per item). The export's time is
  the envelope's `context.freshness.observed_at`, the snapshot's observation, labelled as such,
  because the envelope carries no time of answering. The CSV is UTF-8 without a byte-order mark,
  exactly what the tests parse.

- Data acquisitions: the owner's 2026-09-30 data-lane instruction authorises the bounded first
  mount, complete EU and Luxembourg populations, and French EU bodies. Production signing,
  deployment and promotion remain outside that authorisation.
- The server image and its language data (plan 0.8, 2026-10-15, reported to the owner): the image stays the small
  chiseled one, with no language library and no zone database. Titles are folded by Lex's own table, read from the
  library once and held to it by a test; "today" in Luxembourg is the EU summer-time rule coded in the API and in the
  pages, held to one table; a CI lane runs the API's tests inside the image's base. The `-extra` image (ICU and zone data,
  tens of megabytes more) would hand both back to the operating system, whose answers then move with the image's version.
- The 404 page, the failure panel and the maintenance page (plan 6.9, R60, 2026-10-15; the section at the top):
  - A page request is told from a machine's by a rule on the method and the path alone: a GET or HEAD whose path is not
    under `/api/`, `/mcp` or `/health/`. No header is read for it. Accept-Language, the one header the API reads, is read
    a second time, only to choose the 404 page's language when the path names none; the privacy scan holds it to those
    two reads.
  - Every state that carries no answer because the request did not complete is a failure (an unreadable answer and a
    5xx without a problem body included), as are the four operational refusals. A refusal about the law, and a request
    the form will not send, are not.
  - The 404 page carries the home page's live search screen rather than a form that submits to the search page: a
    form a browser submits puts the phrase into an address.
  - The failure panel's official links are the two publishers' home pages, Legilux and EUR-Lex, the routes the get-help
    screen already names.
  - The 404 table is its own file beside `site-paths.json` (`not-found.json`), held by both servers' tests.
  - The maintenance page is built into the release (`maintenance/{language}.html`) and served by nothing: wiring it in
    front of the app is a hosting change with a cost, the owner's. (Superseded on 2026-10-27 by the maintenance revision:
    the screens' gaps section at the top.)

## Waiting on others

- The owner's item O4, the lawyer's read: R50's French as J6 gives it, "ce n’est pas un conseil juridique", against the
  French review's term "consultation juridique" (T10), and the review's two other points on that sentence (the home
  page, above, 2026-10-15). It ships as R50 specifies it until the read settles it.
- The bounded real mount is available at
  `C:\lex-v3\first-mount-decision95-restart-20260930\v3-corpus`; the web lane's journeys run on it
  (PR #815). Its Luxembourg index is schema 6. The reader refused it from #864 until PR #878, which
  serves it with its build record absent. The machine gates derive their cases from any mount (PRs #838, #839, #842, #845, #846)
  and the release card is the gates run over the rehearsal's mount (PR #844). The real mount holds
  no Luxembourg state yet, so its temporal set is not measured, and it will be once the data lane's
  full populations land.
- EU search on the live search screen waits on an EU permalink grammar (item 5). The real mount's
  EU index answers `search` in one work (for example `32016R0679`, 60 hits for "personal data"), and
  the web search reader refuses the answer by design, reading Luxembourg's only. Each EU hit cites
  its expression IRI and provision (`…/3e485e15-…-01aa75ed71a1.0006#lex-provision=001`), not a
  hash-pinned permalink. A screen showing those citations would break the launch contract's first
  promise, which the journey holds. Once the grammar exists: the search screen gains the optional
  work identifier, the reader reads the EU answer, and the journey searches the EU work on the real
  mount.

## Housekeeping done 2026-09-27 (owner's instruction)

- 219 registered worktrees with clean trees removed, 5 stale registrations pruned, 332 local
  branches already merged into `v3/integration` deleted. `C:\lex-v3\scratch` (probe outputs whose
  useful bytes are checked-in fixtures; it also held 17 registered probe worktrees with uncommitted
  files from 2026-08-28 to 2026-09) and the retired seat-protocol droppings under `C:\lex-v3`
  deleted. Free space went from 0.2 GB to 10.0 GB. Kept: the main checkout, this worktree, and 18
  worktrees with uncommitted changes outside scratch (`git -C C:\lex worktree list`). Not touched:
  `C:\lex-v3\eu-population-run-1..9` (6.0 GB; runs 1 to 8 are 5.3 GB of partial runs superseded
  by run 9) and the other run and evidence directories under `C:\lex-v3`.

## Known defects carried

- The corrigendum tripwire classifies a French corrigendum as `within_served_body_languages` while no
  French body is served (Decision 89 section 4). True once the French expressions land.
