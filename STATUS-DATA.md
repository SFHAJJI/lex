# Lex V3 status: the data lane

Updated 2026-10-26.

## D1's second refusal: a code's title dated by each of its articles; the index build now runs over run 15 on this PC first (sole driver, 2026-10-26)

- **What stopped D1 again.** D1's rerun at 3503de99 (inputs `mount-inputs-dc3f4f86…`, `derived_at` 2026-10-26T15:41:19Z, a
  64 GiB node) got past the article dates and exited 3 after 79 minutes, seconds into the Luxembourg index's first write:
  `LuxembourgIndexRefused: IndexInvalid: SQLite Error 19: 'UNIQUE constraint failed: work_titles.work_identifier,
  work_titles.expression_iri, work_titles.language, work_titles.title, work_titles.title_kind, work_titles.evidence_sha256'`.
  Its peak RSS, 30,948,920 KiB (29.5 GiB) at minute 40.5, is R0's replay again; the index build ran at about 18 GiB.
- **The cause.** A code state's titles are the catalogue's `title` and `titleShort` on its expression
  (`…/code/civil/20180401/fr`). The catalogue states no `dateDocument` for a consolidation or a code state (run 15: 24,604
  `dateDocument`, every one on an act's legal resource). The projection reaches a title through each article of its
  expression, and `TitleDocumentDate` fell back to the article's own applicability date, so a code's title made one row per
  distinct article date, all under one key. Before the fix the probe below finds 5,148 title rows, of which 2,217 are refused
  by that key, every example a code's (`code-civil … 20180401/fr | title | Code civil`: document dates 2017-04-01 and
  2018-04-01, its articles' own). Index 8 never met it: none of its articles had a date of its own, and no code was admitted
  before Release 1. The same title, subject, kind and evidence is one fact; the rows differed only by that stand-in date.
- **The fix** (`LuxembourgIndexBuilder.TitleDocumentDate`): a title's document date is the publisher's `dateDocument` of the
  title's subject, else of the article's work identifier, else of the expression's legal resource, else none; never an
  article's own date. The rows that differed only by it are one row again (the projection's `Distinct`). Acts' titles keep
  their legal resource's date (2,689, all dated); the 242 code titles have none. No schema change; no rule digest moved, so
  D1's inputs stay valid.
- **The probe** (`LuxembourgIndexNineBuildRun15Probe`, new, opt-in `LEX_LU_INDEX9_BUILD_PROBE=1`, read-only, never a CI
  test): the index /9 build over run 15 under Release 1, from the projection to the file read back, in about 10 minutes and
  3.8 GB on this PC instead of 80 minutes on Azure.
  - **What it runs.** The acquisition's own corpus record set, read from custody by its retained chain (inputs /2, the
    Luxembourg catalog, the query checkpoint, `CorpusRecordSetReader`: 260,235 records); inventory /4 and content /3 over the
    11,205 held bodies as R1 runs them (content /3 in chunks; an article kept only for an acquired body); the corpus's
    Luxembourg members from derive-c with each held member's legal-content outcomes replaced by content /3's
    (`LexCorpus6Builder.AknDisposition`, the corpus's order); the typed assertions as derive-c's 1,512,390 `work_facts` rows.
    Then the build's own code: `TryProjectRows`, `WriteFile` (the exact DDL; with the new collector every row the database
    refuses is named and the next inserted, so one pass finds every primary key, unique, not-null, check and foreign-key
    refusal), the reader's file door (`OpenAndVerifyFileAsync`: every validation the reader runs, the logical-rows hash, the
    capability manifest), its check of the log's source bodies against the corpus, and a second write that must be the same
    bytes (the build's double build).
  - **What it does not cover.** The R0 replay and R1 exactly as the derive runs them (the probe's held population is S3a's
    synthetic one over the real bodies, so the content outcomes' semantic identities, which hash the population and the
    object's canonical key, are not the derive's: their uniqueness and the stage-3 check run, they are not reproduced); the
    corpus build under R1 (derive-c's members stand in, with R1's content outcomes; their PDF outcomes are derive-c's); the
    build-time check against the corpus; the typed assertions' original order (the facts come sorted, so title rows that tie
    on the table's order may come in another order); the EU index /6 (its force dates' projection and reader check were
    measured over run 16 by `EuForceDatesRun16Probe`; its whole write is not run here), the mount writer and the seal.
  - **Measured** (run 15, `probe.json` naming the custody, base inputs `b306a972…`, derive-c, built 2026-10-12T18:17:05Z,
    derived 2026-10-26T15:41:19Z): inventory /4 10,644 inventoried, 368 not AKN, 178 rejected, 15 naming another state;
    content /3 298,617 admitted, 9,904 unsupported shapes, 561 upstream not inventoried; the projection 260,235 members,
    137,632 articles (116,406 with a date), 3,312 states (2,690 held), 752 intervals, 1,512,390 work facts.
    - Before the fix (the refactor with the old rule): 5,148 title rows, 2,217 refused (`work_titles` key), nothing else refused.
    - After: 2,931 title rows (code 242, act 2,689; dated: the 2,689 acts'), no row refused, the index 1,378,668,544 bytes
      with 1,544 capability cells, the reader verifies it and the log's source bodies, the second write the same bytes;
      0 failures.
- **The refactor that lets the probe run the build's code** (no output change): `TryProjectRows` reads `ProjectionInputs`
  (the members, the records, the content and inventory outcomes with their object reference digests, the typed assertions
  as `work_facts` rows), which `ProjectionInputs.Of` reads from the envelope and corpus as before; `TryProjectRowsCatching`
  holds the build's `InvalidDataException` rule; `WriteFile` and `BuildDatabase` take an optional refused-row collector
  (without one, the first refusal stops the build, as before). The content and inventory inputs are holders, not positional
  records, so they open no door to the guarded types (the construction census pin did not move).
- **Driver decisions (reversible):**
  - **An article's own applicability date is never a title's document date.** It is another date of another thing (R21: a
    date is said with its role); the catalogue states no document date for a consolidation or a code state, so their titles
    carry none. No fact is dropped: every article keeps its own date, and the title rows that differed only by the stand-in
    are one fact (one assertion, subject, title, kind and evidence).
  - **The probe stands in for R0 with derive-c's corpus and work facts and R1's own content outcomes**, as written above,
    and says what that leaves out; D1 reruns on Azure only once the probe is clean at the head that runs.
  - **The writer's refused-row collector exists for the probe only**; a build never passes one.
- **Tests.** `LuxembourgTitleDocumentDateTests` (new): the derive's own path (the synthetic consolidation acquired with a
  title and a short title on its expression and two articles stating 2026-01-15 and 2026-02-01, re-derived under
  `lu-release-1`, built, written and verified: one row per title, no document date, each article's own date kept); with the
  old fallback put back it refuses with D1's sentence (run once to check). The unit test of `TitleDocumentDate` takes the
  new rule (`ATitleIsDatedByItsSubjectThenItsWorkThenItsLegalResourceAndNeverByItsArticle`). No census pin moved.
- **Measured** (Windows, no browser, no publisher traffic, no Azure, custody read only; rebased on 8958ec5f, #987 merged):
  - `dotnet build Lex.V3.slnx -c Release --no-incremental` at 7152de65: 0 warnings, 0 errors.
  - `Lex.V3.Tests` whole: 3,329 (3,323 passed, 0 failed, 6 skipped).
  - `Lex.V3.Ingest.Tests` whole, once: 3,493 (3,452 passed, 0 failed, 41 skipped: every one an opt-in probe, a live canary or
    an env-gated writer). It holds the censuses, the three samples classes (unchanged, so not re-rendered) and the legacy
    schema 8 and 6 mounts (their recorded answers replayed unchanged).
  - The probe over run 15 at 7152de65: the same rows and the same index as before the rebase (`f407c05c…`, 1,378,668,544
    bytes, 1,544 cells), no row refused, the reader and the log's source bodies verified, the second write byte-identical,
    0 failures; 11.4 minutes, peak working set 4.4 GiB.
  - `eng/verify-v3-tree.ps1`: 1,689 tracked paths.
- **Not done here.** D1's rerun (on Azure at 7152de65, started once the probe was clean; it succeeded, derive A equal to B,
  sealed; what it wrote into custody is under "For the dry run" in R1's section below, corrected); index /10; any memory
  change (both D1 runs peaked in R0's replay at minute 39 to 41).

## #987 brought up to date with v3/integration, and the D1 fix folded in (sole driver, 2026-10-26)

- **What.** Two merges on the repaired head: `origin/v3/integration` at e504d188 (#989, the web big bang, and #990, the
  legal pages naming no person), so the branch protection's integration head is in #987; then `data/applicability-dates`
  at 3503de99 (D1's refusal, the section below). **#987 now also carries the D1 fix, which gets its own independent
  review in this pull request.**
- **The integration merge.** Kept both sides of STATUS-WEB.md, `LiveDossier.jsx` (#987's date roles and catalogue chain
  beside the integration's today line), `initial-answer.mjs` (the integration's imports and #987's `holdsToday`) and
  `live-chrome-fr.mjs`'s header. The answer and refusal-payload samples re-render to the textual merge byte for byte; the
  envelope samples differ from it in one envelope, the integration's new "an EU act dated before 1 July 2013", which over
  the EU index /6 now carries #987's `force_dates` and its not-held reason. The legacy schema-8 and schema-6 base answers
  are the integration's Linux recording, which the merged code answers byte for byte. The EU schema-5 base answers moved:
  they were recorded at 24118293, before #989 gave every answer its freshness and commits context, so they were recorded
  again, as their test says a change of base does, at e504d188 (the code without force dates) with only the schema-5 test
  class and its fixtures added, on Linux; every row moved by the integration's changes alone (the context; the two
  bundles' authenticity by the act's date), and the merged code answers them byte for byte.
- **The D1 merge.** `LuxembourgIndexBuilder.cs` keeps both changes (D1's `ArticleApplicability`; #987's reader
  `catalogue: true` queries). STATUS-DATA keeps both sections. OWNER-BRIEF.md is the integration's, with D1's rebuild
  facts folded into its "still thin" line, #987 said as in review, and the stale "turn on Azure Compute" item removed:
  the rebuild runs on Azure Container Apps.

## Review and repair (combined #987): the reader's ambiguities, holes and in_force_on, the EU force dates (sole driver, 2026-10-26)

- **What.** The one repair round of the combined data pull request #987 (S7a, S7b, S7c and EU 2.1), after its one review by
  the other model family (Decision 95). Each finding was reproduced (the review's C# reproductions, added to the interval
  fixture, and its web and EU probes), fixed, and held by a permanent test that fails on the reviewed head (f0dcf89c) and
  passes after: on f0dcf89c with the new tests, 7 of the new C# cases fail (the rest of their classes, 22, pass), and the
  web's 8 new or changed cases fail (STATUS-WEB.md, the same section, which has the web's findings). Code at 76203cea and
  5e311bb8.
- **The platform** (`V3CorpusMount.Intervals.cs`, `.Continuity.cs`, `.cs`, `.References.cs`, `.EuropeForceDates.cs`;
  `LuxembourgIndexReader`; `EuropeIndexForceDates.cs`):
  - **2 (medium), an ambiguity of states none of which holds text.** It counted as held text, so an article's history
    showed no break across it and the radar named its states as an ambiguous baseline. `HoldsText` counts an ambiguity as
    held only when one of its states' text is held; otherwise `continuity_breaks` names the run, with each state's text
    status as its reasons and the states named, and the next row says `after_break`; the radar's row has
    `no_held_baseline`. `ContinuityRule` says so. Test: `AnAmbiguityOfStatesNoneOfWhichHoldsTextIsAContinuityBreakAndLeavesTheRadarNoBaseline`
    (an act held, a withheld and a PDF consolidation of 2010, a held one of 2015; and an ambiguity with held text, no break).
  - **4 (low-medium), in_force_on across works.** It read its works from the held states on or before the date, so a work
    whose only such state holds no text (an act whose file is a PDF, before a held consolidation) gave no row, while as_of
    refused the date for that state's text. On index 9 the works are read from the catalogue's rows, held or textless (in
    French the rows naming no expression too), of a work holding a state in the scope (`ResolveWorkKeysWithStateOnOrBefore`
    and `CountWorksWithStateOnOrBefore` take `catalogue: true`; schema 8 and 6 read as before): the work is a row naming
    the state and why, and is counted covered, as F8 counts it (`works_with_a_state_on_date`, `works_not_covered_on_date`
    and `works_beginning_later` still add up to the works in scope). Test:
    `InForceOnAcrossWorksNamesAWorkWhoseOnlyStateOnOrBeforeTheDateHoldsNoTextAndCountsItCovered` (with and without a
    language, and paged).
  - **5 (low), an ambiguity with a state naming no expression.** Such a state has no language, so its candidate was a
    permalink ending in "/", which the candidate lists then skipped, misaligned. Every ambiguity now names it by its stable
    coordinate (`StateUrl`, as `RowAddress` did; the radar's, the refusal's and as_observed's lists no longer drop it, so a
    pair never reads as one state), and `CandidateStates` gives each coordinate the row of that date naming no expression;
    the lists are given only when every candidate has its row, so they are never short. `AmbiguousCandidatesRule` says it.
    Test: `AnAmbiguityNamesAStateTheCatalogueNamesNoExpressionOfByItsCoordinateWithItsListsAligned` (as_of, the bundle,
    in_force_on named and across works, the dated search's ambiguous works and the radar). The web card reads it.
  - **7 (low), a hole's span after a nested state.** `CoverAt` took the end of the state that begins last before the date;
    a state nested in an earlier one's interval ends first. It now takes the furthest end of the states before the date
    (the hole's `hole_from`, and a closed end's `closed_on`), and names that state. The dossier's and the timeline's `gaps`
    and `overlaps` are computed reader-side from the chain's rows the same way (`GapsOf`, `OverlapsOf`): a gap from the
    furthest end before it to the next start, and an overlap of two states of different dates from the later start to the
    earlier of their ends; `CatalogueChainRule` says so, and that a transition's span is between its two states alone. Test:
    `AHoleOrAClosedEndAfterANestedStateBeginsWhereTheLastStateCoveringEnds` (as_of, the dossier and the timeline, the
    history's break, and the same work with no later state). **The builder's intervals are wrong the same way, not changed
    here** (data/applicability-dates is changing `LuxembourgIndexBuilder` now): `CompleteIntervals` writes each transition's
    span from one state's end to the next date's start, pairwise, so a nested state's transition is a "hole" from its own
    end (2021's 2022 end to 2025, where the 2020 state covers to 2023) and the overlap before it runs to the outer state's
    end (2021 to 2023, where both cover 2021 only); every state of one date gets a transition of its own, so a same-date
    pair can give two holes. The reader no longer reads `gaps` or `overlaps` from them; `transitions` still serves them as
    the index holds them, as each pair's own relation.
  - **9 (latent, EU 2.1).** EU 1: a code the accepted table does not pin (PE) on the end-of-validity or signature property
    threw in `ForceDateRoleOf` (the contract types it as no role stated), so one such statement refused the whole EU index;
    those properties now give their own role, the code kept, as this file's EU 2.1 rule says. EU 2: a `comment_on_date`
    the publisher delivered as an IRI or a blank node is retained among the statement's properties while the decode reads
    no note from it, and the build's own read-back refused it; `ValidateForceDateEvidence` now reopens a row whose note is
    null beside a retained comment, and still refuses a note that is not the retained comment. `EuropeForceDateNoteRule`
    says a non-literal comment is no note. Tests (`EuropeIndexForceDatesTests`): the two properties with PE (a data row
    each), and the IRI comment reopened, a changed note refused.
  - **11 (a note).** `V3CorpusMount.References.cs`'s comment said a version an ELI pins opens at its own date; it is asked
    there and opens only where the rules serve it, and is refused there when that date is ambiguous or its interval
    invalid. The comment and this file's S7a bullet ("A scope a caller narrows") are corrected, and S7a's in_force_on driver
    decision says it is superseded by 4. Test: `AnEliThatNamesOneVersionOpensItOnlyWhereTheRulesServeItAndElseRefusesWithTheirReason`
    adds the three ELIs (the 2021 act and its consolidation, ambiguous; the 2023 rgd consolidation, invalid) and the 2005
    act at its own date, opened.
  - **`outside_named_version`'s dates (found by the French review of this round, its point 2).** `RefuseOutsideScope`
    gave `history_begins` and the nearest dates over the named version alone, so the card's declared null said "No later
    state is held" where a later consolidation is, and the nearest earlier date was the act's where the covering state is
    nearer. They are now the work's, over its chain in the language, as `CoverAt` gives them for every other reason, and
    `OutsideScopeRule` says so. Test: `AnIdentifierOutsideItsVersionGivesTheWorksFirstAndNearestDatesNotTheVersionsOwn`
    (an act and two consolidations; the ELI of the act between them) and the ELI test's dates; both fail with the scope's
    dates put back.
  - **1, the platform's half.** The refusal census (`V3RefusalPayloadSamplesTests`) gains the scenario the review
    reproduced: a search whose ELI names the act as published, at a date a later consolidation covers, so the platform's
    real `outside_named_version` payload is a sample (it is now `no_version_for_date`'s richest; before_history moved to
    `more_payloads`), and the web's card is held to it.
  - **10, the platform's half.** `EuropeIndexBuilderTests` compared a force date's work with itself; it now compares each
    row's work with the work its decoded statement names, and that with the seed's. `V3CorpusIntervalMountTests`' dated
    search compared a hit's state digest with itself; it now holds every hit of the 2018 act to the act as published's
    digest from the dossier (found in the same pass).
- **Served answers.** On index 9 only: the rule texts above (`ContinuityRule`, `CatalogueChainRule`,
  `AmbiguousCandidatesRule`, `OutsideScopeRule`, the EU note rule over an EU index /6) and the refusal census's new sample;
  the answer, envelope and refusal-payload samples re-rendered with `V3_RENDER_*` and verified without them. The legacy
  schema-8 and schema-6 base answers and the EU schema-5 base answers are unchanged (`V3CorpusLegacySchema8MountTests`,
  `V3CorpusEuropeSchema5MountTests`, byte for byte).
- **Driver decisions (reversible):**
  - **A state naming no expression is named by its stable coordinate in every ambiguity**, not filtered out: filtered, an
    act held beside such a consolidation was a one-candidate ambiguity, which the card refuses, or (on the radar) one
    state compared as if the rules served it. The coordinate is the address the refusals already give such a state.
  - **The gaps and overlaps are the reader's, not the index's transitions**, so they agree with as_of's hole and the
    history's breaks without a builder change; the builder's fix is left to the branch that owns it.
  - **An overlap is listed for every two states of different dates whose intervals meet**, not only for consecutive
    dates, with the earlier of the two ends: "the span two states both cover".
  - **A non-literal `comment_on_date` is no note**, as the accepted decode reads it, rather than its IRI as a note: a
    blank node's label is no publisher's word. The statement keeps it, and the identity covers it.
  - **`outside_named_version`'s nearest dates are the work's**, as every reason's, rather than the named version's with a
    rule saying so: a card reads them as the work's.
- **Measured** (Windows, no browser, no publisher traffic, no custody read, no Azure), on 5e311bb8:
  - `dotnet build Lex.V3.slnx -c Release`: 0 warnings, 0 errors.
  - `Lex.V3.Tests`: 3,323 (3,317 passed, 0 failed, 6 skipped).
  - `Lex.V3.Ingest.Tests`, the whole project, unfiltered: 3,431 (3,393 passed, 0 failed, 38 skipped: the opt-in probes,
    env-gated writers and recorders). `V3CorpusIntervalMountTests` 22 of 22; `EuropeIndexForceDatesTests` 6 methods.
  - The answer, envelope and refusal-payload samples re-rendered with `V3_RENDER_*`, then verified without them; the legacy
    schema-8 and schema-6 base answers and the EU schema-5 base answers unchanged.
  - Linux (WSL Ubuntu 24.04, .NET 10.0.400, `DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=1`, `TZ=UTC`, a fresh checkout of
    5e311bb8): the envelope, answer and refusal-payload sample classes, the legacy schema-8 and schema-6 mounts, the EU
    schema-5 mount, the interval, continuity and EU force-date classes: 47 (45 passed, 0 failed, 2 skipped: the env-gated
    recorders); the build's only warnings are NETSDK1188 (a package's resource locales, under invariant globalization).
  - Before the fix: f0dcf89c with the new tests, the interval and EU force-date classes and the EU builder's force-date
    test: 29 (22 passed, 7 failed: each new case); the outside dates' two tests fail with the scope's dates put back.
  - `eng/verify-v3-tree.ps1`: 1,666 tracked paths. The web's counts are in STATUS-WEB.md.

## D1's refusal: one article's own date written `yyyyMMdd`; the index reads both ISO 8601 forms and no article's date refuses a build (sole driver, 2026-10-26)

- **What stopped D1.** On 2026-10-19 an Azure job ran D1 at d9917b27 (#987): `rederive-inputs` (rule set `lu-release-1`,
  policy `a477f4f2…`, base `mount-inputs-b306a972…`), then `derive --custody … --checkpoint mount-inputs-e8fd04ce….json
  --custody-encoding brotli` over run 15's custody (324,873 files). It exited 3 after 72 minutes (peak RSS 29,107,840
  KiB, 27.8 GiB): `LuxembourgIndexRefused: DerivationMismatch: A publisher applicability value is not an exact civil
  date.` The index builder read each admitted article's own date through `ApplicabilityDate`, which refused the whole
  build on any value not written `yyyy-MM-dd`.
- **Where the value comes from.** `LuxembourgAknArticleCoordinate.PublisherApplicability`: the trimmed text of the
  `scl:jolux scl:name="dateApplicability"` the article itself owns (in its `scl:JOLUXWork/scl:JOLUXLegalResource`; the
  nearest article above it is that article), kept verbatim by inventory /4 (rule line
  `applicability=scl-jolux-scl-name-dateApplicability-article-owned-single`) and hashed into the article's identity by
  content /3. Only code and consolidation files write one: derive-c (index 8, inventory /3) held 8,854 articles, all of
  acts and none with a date of its own, so no build met such a value until Release 1 admitted the codes.
- **Measured over run 15's custody** (`LuxembourgArticleApplicabilityRun15Probe`, new, opt-in, read-only, never a CI test:
  inventory /4 over the 11,205 held bodies, content /3 over the 2,721 inventoried acquired ones; custody read by digest
  through S2's read-only decorator, its 324,873 files untouched; no derive, no network; 92 s and 20 s, peak working set
  1,597 MiB):
  - Every main-body article of the 10,644 inventoried bodies: 308,521 articles, 78,807 with no value, 229,711 written
    `yyyy-MM-dd` (2,646 bodies: 153 code states, 2,493 consolidations) and 3 written `yyyyMMdd`: `art_l_010-1` of the
    Code de la consommation's states 20220101, 20220528 and 20221125, each `20220101`. No other form, no empty value, no
    time, time zone or partial date.
  - What the derive reads (content /3 admitted or marker-only, in an acquired body): 137,632 articles (2,008 refused
    shapes not counted), 21,226 with no value, 116,404 `yyyy-MM-dd` (122 code states) and 2 `yyyyMMdd` (states 20220101
    and 20220528; 20221125's rights are withheld). These two refused D1.
  - In those bodies, 36 other articles of the same code state `2022-01-01` in the extended form; the article before
    `art_l_010-1` states none and the one after `2018-07-01`.
  - The catalogue's typed dates the builder also reads (run 15's `work_facts` in derive-c): 24,604 `dateDocument`, 21,828
    `dateApplicability`, 24,606 `dateEntryInForce`, 2,660 `dateEndApplicability` and 28,605 `publicationDate`, every one
    an exact `xsd:date`.
- **The rule** (`LuxembourgIndexBuilder.ArticleApplicability`): a complete calendar date in either of ISO 8601's forms is
  that date, stored `yyyy-MM-dd`: the extended form (`2017-09-05`) and the basic form (`20220101`, eight digits that are
  exactly one day of the calendar). Any other value (a partial date, a day the calendar does not have, a time, a time
  zone, another notation) gives the article no date and never refuses the build. The value as written stays in custody
  and in the article's identity. The catalogue's document dates keep refusing a value that is not exact (typed
  `xsd:date` assertions, a different door), under their own sentence now: "A publisher document date is not an exact
  civil date."
- **No schema bump, no rule digest moved.** The index stays `lex-v3-luxembourg-index/9` with its exact DDL; the reader is
  unchanged (it still refuses a stored article date that is not exact, and none is). Inventory /4, content /3, state
  digest /2 and `lu-release-1` are unchanged, so D1's unsealed inputs (`mount-inputs-e8fd04ce…`) stay valid: D1 reruns
  from them. Served answers on the committed mounts do not move (their article values are all extended).
- **Driver decisions (reversible):**
  - **The basic form is read as its date.** ISO 8601 writes a complete date in two forms; the publisher writes the dates
    of its own ELI paths in the basic one (`…/code/consommation/20220101`); `20220101` reads the same either way its day
    and month are taken; and 36 articles of the same state write `2022-01-01`. Reading it invents nothing, while
    storing no date would say the publisher stated none.
  - **Any other form gives no date and is not stored.** The index's article row has no column for a value as written,
    so an unclear value reads as no date (`article_valid_from` null, no validity conflict), as a missing one does. Saying
    it as unclear needs that column: index /10, not made, because run 15 holds no such value. The probe counts every
    value's form before a derive; a form it finds unread is the trigger for index /10.
  - **A time or a time zone is not read**, though an `xsd:date` with a zone names its day: run 15 holds none, and
    dropping the zone would leave nothing in the index to say it was there.
  - **The catalogue's typed dates still refuse**: they are `xsd:date` assertions, run 15's are all exact, and a malformed
    one is a different defect than an article's own value.
- **Tests.** `LuxembourgArticleApplicabilityTests` (new, 21): the reading of each value (19 rows: run 15's two forms, a
  second basic date, none, and fifteen forms that give no date: partial, impossible days in both forms, unpadded, short
  and long digit runs, a zone, `Z`, a time, other notations, full-width digits, empty); the derive's own path end to end
  (a synthetic consolidation whose articles carry `2017-09-05`, `20220101`, none, `2022-01`, `20220230` and
  `2019-01-01+01:00`, acquired, re-derived under `lu-release-1`, built, written, verified and opened: the rows hold
  2017-09-05, 2022-01-01 and four nulls, the reader's article dates the same; with the old reading this build refuses
  with D1's exact sentence, run once to show it); `as_of` through the real handler on a verified mount (a code state of
  2022-05-28 whose articles carry `20220101`, `2018-07-01`, none and `2022-01`: `article_valid_from` 2022-01-01,
  2018-07-01, null, null; conflicts true, true, false, false; the rule's sentence unchanged).
  `LuxembourgIndexCatalogueStatesTests.Harness.Held` takes each article's value as written (optional). The census pin
  that moves: `VocabularyRegistryCensusTests` (the static token registries): `LuxembourgIndexBuilder` gains
  `ArticleDateBasic`, `ArticleDateExtended`, `ArticleDateNone` and `ArticleDateNotRead`, its entry re-transcribed with
  `ClosedSurfaceCensus.RenderForTranscription` from a throwaway test. No other pin moved.
- **Measured** (Windows, no browser, no publisher traffic, no Azure; custody read only):
  - `dotnet build Lex.V3.slnx -c Release --no-incremental`: 0 warnings, 0 errors.
  - `Lex.V3.Tests`: 3,323 (3,317 passed, 6 skipped).
  - `Lex.V3.Ingest.Tests` filtered to the new class, the index builder, the catalogue states, the interval, continuity,
    as_of, evidence bundle, article history, diff, timeline, provenance and dossier mounts, the re-derivation, content /3's
    mount, index-9 serving, the legacy schema 6 and 8 mounts (their recorded answers replayed unchanged), the three samples
    classes (unchanged, so not re-rendered), the censuses, public request recording, the first mount build, the offline
    mount, replay guarantees and the two run-15 probes: 334 (330 passed, 0 failed, 4 skipped: the two probes and the two
    env-gated writers). The run before the census pin was re-transcribed had that pin as its one failure.
  - The probe over run 15, before and after the change, as above; `eng/verify-v3-tree.ps1`: 1,668 tracked paths.
- **D1's memory (peak RSS 29,107,840 KiB: 27.8 GiB on the 32 GiB node), read from its 5-second samples**
  (`derive-a.resources.jsonl`; the phases mapped from the code order and the read pattern, not from logs, so they are
  inferences):
  - The peak, 27.76 GiB RSS (27.66 GiB anonymous, 2.0 GiB available) at minute 38.9, is in R0's replay, not the index
    build: from minute 29 to 39 the process read nothing and grew from 9 to 27.8 GiB on one core, then fell to 14.9 GiB by
    minute 41.5. That is the acquisition's post-document step replayed (`LuxembourgQueryExecutionAdapter.RunCoreAsync`: the
    in-file rights read, then a second full profile resolution, resolver and scope reduction while the first resolution
    and manifests are still referenced), the step the acquisition itself measured at 20.2 GB private on 2026-10-12 and
    derive-c's replay at 15.2 GiB working set with paging (S4b above).
  - From minute 47.7 to 62.2 the process read and wrote nothing at a flat 14.96 GiB on one core: most likely the held-body
    population's final-rights lookup (`LuxembourgHeldBodyDerivationPopulation.cs:236`), a linear scan of the final
    resolution's resources for each held record (about 11,000 × 260,000 record comparisons), then the query result's
    streamed digest.
  - The index build never ran: the staging directory appeared at minute 72.3 and the refusal followed within seconds, at
    17.3 GiB RSS. It starts from that baseline (R0's query result, R1's inventory /4 and content /3 populations, the EU
    and corpus results); S4b measured its increment only on synthetic data. Whether it stays under the 27.8 GiB the replay
    reached is D1's rerun to measure.
  - **Cheap reductions, not made here** (none is needed to rerun D1, whose 32 GiB node carried the peak with 2 GiB to
    spare; each needs its own byte-identity check): drop the first resolution, its resolver and the first manifests
    before the second resolution in `RunCoreAsync`; build the final-rights lookup once (saves about 15 minutes, not
    memory); share one validated rejected-manifestation list across observations instead of copying it per observation.
    No GC setting is configured anywhere (Workstation GC, no heap limit); `DOTNET_GCConserveMemory` or a heap hard limit
    are untested knobs. Streaming the index's article rows is the larger change S4b names.

## EU force dates: the EU index /6 holds each act's dates by role, from custody (sole driver, 2026-10-19)

- **What.** The plan's item 2.1 (R79's E1, R21; branch `data/eu-force-dates`). The EU index gains schema 6
  (`lex-v3-europe-index/6`): a `force_dates` table holds each act's dates as the publisher records them in its dated
  statements, each with its role and its source. Release 2 carries it; derive-c's index /5 is read exactly as before.
  - **Where the facts live in custody.** The acquisition already holds them: family A of the object facts (the
    reified `owl:Axiom` statements on a CDM date property of each census work: `resource_legal_date_entry-into-force`,
    `_deadline`, `_end-of-validity`, `_signature`). Every property of each statement is retained (annotated source,
    property and target, the type-of-date code `ann:type_of_date` and the note `ann:comment_on_date`, both literals in
    the publisher's `{CODE|IRI}` form). The derive reopens them from custody with the query run
    (`EuQueryExecutionAdapter.ReopenAsync`), and the accepted E1 contract decodes them (`EuReifiedAxiomDecode`,
    `EuDateAxiomBinding`) onto `EuQueryExecutionResult.DateAxioms`. No build read them until now; no acquisition is
    added.
  - **The build** (`EuropeIndexForceDates.cs`, called by `TryWriteFile`): one row per statement, keyed by a digest of
    its work, statement IRI and properties, so a statement two seeds' closures both delivered is one row. The role is
    read from the property and the publisher's code alone: EV `entry_into_force`, MA `application`; the end-of-validity,
    deadline and signature properties their own role; any other code on the entry-into-force property, or none,
    `not_pinned` (the code kept). Each row keeps the value, datatype and open end (9999-12-31), the code and the note
    exactly as served, the label the accepted table pins (Lex's, never the publisher's), the contract's own role, every
    property in delivery order, and the delivered batch it was read from. A statement on a work outside the census, or
    one identity with two values, refuses the build.
  - **The reader** checks schema 6 against its exact DDL, reopens every row through the accepted contract against its
    own statement (role, value, code, note, open end, contract role, identity, census work) and includes the rows in
    the logical-row hash. Schema 5 keeps its own DDL, stamp and hash, and its answers do not move.
- **Measured over EU run 16's custody** (`EuForceDatesRun16Probe`, opt-in, read-only, never a CI test; the catalog
  `767ceba4…`, the query checkpoint `37a649de…`, derive-c's states read-only):
  - **Statements.** 24 family-A batches restored, 3,394 rows verified, 417 statements decoded, 417 rows: 79
    `entry_into_force` (EV), 79 `application` (MA), 24 `end_of_validity`, 221 `deadline`, 9 `signature`, 5 `not_pinned`
    (MA/PART). The contract reads 226 as role not stated: the 221 deadlines, which carry no type-of-date code (their
    qualifier is the note: AU+TARD in 19, AU+TOT in 1, B-19.12 in 51, the rest V ART …), and the 5 MA/PART. 61 statements
    carry a property the contract does not model (such as `build_info`), kept verbatim. Every value is an `xsd:date`.
  - **Acts.** Every date is on one of the 82 seed roots (1,170 census works; no consolidated version carries a dated
    statement). Acts with each role: entry into force 78, application 36, end of validity 12, deadline 61, signature 9,
    not pinned 3. Directives (40): 40, 3, 6, 31, 2, 0. Regulations (36): 34, 33, 5, 30, 1, 3; 85 of the 221 deadlines are
    on directives. No entry into force is held for 12016E/TXT, 12016M/TXT (no date of the four roles at all),
    32007R0864 (Rome II: MA and MA/PART only) and 32014R0651.
  - **Conflicts.** None: no act has two entry-into-force dates. Six acts carry two statements of one role and date
    (12016P/TXT's entry into force 2009-12-01; five deadlines), each said. 23 acts have several application dates
    (staggered) and three several ends of validity (32003R0001, 32005L0029, 32018R1999, five each): 20 of the 24 ends of
    validity are partial by their note (FIN/VAL/PART), and one is the open end (12012E/TXT, 9999-12-31).
  - 9.8 s, peak working set 201 MiB; the custody's 93,457 files are the same before and after, derive-c's index untouched.
- **The schema-5 answers.** `V3CorpusEuropeSchema5MountTests` commits a schema-5 EU mount written by the code at
  24118293 (the retained GDPR envelope with its synthetic French package: no publisher text) with its fifteen answers
  recorded there (coverage, resolve, dossier twice, timeline, as_of three times, evidence_bundle twice, search twice,
  verify, in_force_on, status_on). This branch answers every one byte for byte.
- **Driver decisions (reversible).**
  - The deadline property is the publisher's deadline whatever its code: run 16's 221 deadlines carry no type-of-date
    code, so the accepted contract reads each as role not stated. The index keeps that reading (`fact_role`) beside the
    role; the qualifier is the note, said word for word in its codes. Never "transposition" (R79).
  - A code the accepted table does not pin (MA/PART, 5 in run 16) is `not_pinned`, said with its code and given no role
    (the decision of 2026-10-01: no invented label).
  - The note's codes are never translated: `publisher_note_codes` is the note with each `{CODE|IRI}` carrier written as
    its code, nothing else changed.
  - Two dates of one role are both said, none chosen; only two entry-into-force dates are a conflict (several
    application or end-of-validity dates are staggered or partial, as their notes say).
  - A role with no date is "not held", never "none": a date the publisher records without a dated statement is not
    acquired.
  - The dates are the act's original work's (all of run 16's are on the 82 seed roots); a consolidated version's
    wording date is none of them, and no wording is selected by them.
  - The index /5 answers stay byte for byte: the member and the not-held row change only over an index /6.
  - The answers name each date's statement (the publisher's IRI) and its identity digest, not the delivered batch it came
    in: that coordinate is minted per acquisition, so it stays in the index, and two derives of one custody answer alike.
- **The answers** (the web lane's file has the pages): over an index /6, EU `dossier`, `timeline`, `as_of` and
  `evidence_bundle` carry `force_dates` (each date with its role, the publisher's code, its note and the note in its
  codes, the date property and the statement; the named roles not held; a conflict list; the rules), and the not-held row
  `force_dates` says what of them is still not held. `in_force_on` and `status_on`'s manifest rows say what index /6
  holds; both stay refused for EU acts.
- **Tests.** `EuropeIndexBuilderTests` (the pin of the fixed index bytes moves with schema 6; schema 3, 4 and 5 stay
  readable without inventing what they lack; the GDPR's three statements indexed by role and reopened; nine hostile
  changes and four logical restamps refused), `EuropeIndexForceDatesTests` (each role from its property and code, one
  statement delivered twice is one row, two statements of one date are two, a work outside the census refused, a stored
  row its statement does not say refused), `V3CorpusEuropeForceDatesMountTests` (the four answers, the act with no
  statement, the note's codes), `V3CorpusEuropeSchema5MountTests` (above). The EU restamp helpers of
  `V3CorpusResolveMountTests` hash the force dates; the census pins `EuropeForceDateRoles` and the index's new constants.
  The fixture `EuForceDatesFixture` writes family A's rows in run 16's shape, with the GDPR's three dates, codes and
  notes as run 16 holds them (publisher metadata, no text).
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
- **Not done here.** A derive of the population with this build (the re-derivation runs later, on an Azure machine,
  and Release 2 carries it). R79's dossier sentences ("In force since …", "Not yet in force") are not said: R21 and the
  guard refuse them, and a status sentence needs its own reviewed form.

## The catalogue's chain where a history is read, the dates by role, and the refusals in the site's own words (sole driver, 2026-10-19)

- **What.** Slices S7b and S7c of the Luxembourg data design (`C:\lex-v3\LU-DATA-DESIGN-2026-10-14-v2.md` §3.2, §3.4 and the
  S7b and S7c rows), with what S7a (#987) left them. On lex-v3-luxembourg-index/9 only; on an index 8 or 6 every answer is
  the one the code before sent, byte for byte (`base-answers.json`, schema 8 and 6, replayed unchanged: no row re-recorded).
  Stacked on S7a (#987); it is combined with the EU force-dates slice into one data pull request.
- **The platform** (`V3CorpusMount.Continuity.cs`, new; `V3CorpusMount.cs`, `.Intervals.cs`, `.AsPublished.cs`):
  - `timeline` and `dossier` name the catalogue's chain as the publisher left it (`CatalogueChainRule`): `textless_states`
    (each catalogue state in the language scope whose text the index does not hold, as the refusals name them, never served
    in another's place), `transitions` (the index's `intervals` rows with both states' dates), `gaps` (holes), `overlaps`,
    `same_date` groups, `unclear_end_days` (a suspected inclusive end's day) and `invalid_intervals` (an end on or before the
    start). The dossier's not-held list replaces `coverage_gaps` ("no gap between states is listed") with
    `catalogue_completeness` (a state the catalogue did not list is not here; a gap in the catalogue is not one in the law).
  - `article_history` rows carry their state's catalogue facts (the date's role among them) and `after_break`; the answer
    names `continuity_breaks` per language (`ContinuityRule`): each run of dates from the language's first held state on that
    no state with held text covers by the interval rules as_of applies (a textless state by its text status, a hole, an
    unclear end day, an invalid interval, no expression in the language), from its first date to the date held text covers
    again (null when none does), with the states each reason names. The interval rules' answer is constant between the dates
    a state of the chain starts or ends, so each such date is asked once. A date after a closed end is the end of the
    catalogue's states, not a break; an overlap or an ambiguity holds text, so it is no break. `anchor_rule` states the limit
    the design names: an id the publisher minted with a date suffix (`art_2_20211219`) is another id.
  - R21 in the platform's own sentences: `diff`'s same-state note of an act dated by its entry into force says "the act as
    published, dated by its entry into force on X (publisher)" (`SameVersionNote`, `AsPublishedSameTextNote`), never
    "applicable from X"; `ambiguous_version` names `candidate_date_roles` beside the other candidate lists.
  - `events` (S7c's "events page for textless rows"; the site has no events page, so it is the answer's): a state the
    catalogue names no expression of has no permalink (it made `/lu-legilux/{work}/{date}--{digest}/` before), and
    `permalink_rule` says a textless state's permalink is refused for its text, naming it.
  - `coverage` (the row S4 left, "history gives when each build ran"): on index 9 the build-time row says a build's time is
    when its acquiring build read its acquisition, which a derivation from custody keeps, and that `events`' `log.derived_at`
    is when the mounted index was derived (`CoverageBuildTimeRowNine`); index 8 and 6 keep their rows.
- **Driver decisions (reversible):**
  - **The timeline's chain is served as lists beside the states, not as states**: `states` stays the held states (every
    reader holds a state to its articles), and `textless_states` names the others as the refusals name them.
  - **The transitions are the French chain's** (index 9's `intervals`), served with any language asked: a translation takes
    its French state's interval, so the French chain's gaps are its gaps.
  - **A continuity break is a run of dates, not a list of states**: the history is a walk of held states, so what breaks it
    is a stretch of dates no held text covers, with the reasons and states that stretch holds. An overlap of one wording and
    an ambiguity hold text, so neither breaks it; `wording_changed` keeps comparing two held states across a break, and
    `after_break` says one lies between.
  - **The dossier's not-held row `coverage_gaps` becomes `catalogue_completeness`** on index 9: the gaps are listed now, and
    what the answer cannot say is what the catalogue did not list.
  - **The events answer, not a web page, is "the events page"**: the site has no events page; a state naming no
    expression gets a null permalink rather than a malformed one.
- **Tests.** `V3CorpusContinuityMountTests` (new, 5): the handler on the verified mount of S7a's multi-body fixture: the
  timeline's textless states and every transition kind (a code's run of misfiled, PDF and not-acquired states; a hole after a
  withheld consolidation; an overlap and two invalid intervals; an unclear end day; a same-date pair), the dossier's chain
  member for member the timeline's; an article's continuity breaks (after an act's withheld consolidation, a hole and a state
  not acquired; an unclear end day between two held rows; an invalid interval; a code's textless run; a closed end, no break)
  and each row's `after_break`; `diff`'s note of an act dated by its entry into force, held only as published and with a later
  consolidation held, and an applicability state's note unchanged; an event naming no expression and a withheld state's
  event permalink; coverage's build-time row. Updated: the dossier's property list and not-held list, the as_of and diff
  ambiguity property lists. The answer, envelope and refusal samples re-rendered from the handler and verified without the
  switches.
- **Measured** (Windows, no browser, no publisher traffic, no custody read): `dotnet build Lex.V3.slnx -c Release` 0 warnings,
  0 errors; Lex.V3.Tests 3,323 (3,317 passed, 0 failed, 6 skipped); Lex.V3.Ingest.Tests filtered (the continuity class,
  interval, timeline, article history, diff, dossier, as_of, events, events chain, coverage, index-9 serving, replay
  guarantees, the three sample censuses, every census class, legacy schema 8 and 6, request recording, temporary storage,
  as_observed, in_force_on, provenance, evidence bundle, search, search results, catalogue states): 313 (309 passed, 1 failed,
  3 skipped), the failure coverage's pinned build-time row, fixed, and its class rerun with the continuity class, 27 of 27;
  `eng/verify-v3-tree.ps1` 1,647 tracked paths.
- **Combined into one data pull request (#987), 2026-10-19.** Rebased on S7a's final head (943cbd2d, on `v3/integration`
  0b908870): `reading.validityConflict`, `search.dated` and the PDF's lines are S7a's repair's, the history's absent states
  keep S7a's facts, and both slices' History forms stand side by side. `data/eu-force-dates` (EU 2.1, 6f98b90c) merged in one
  merge commit (the dossier reader's imports and the status files' top sections were the conflicts). The whole Ingest project
  then found that the custody conformance census did not declare EU 2.1's opt-in run-16 probe store
  (`EuForceDatesRun16Probe+HeldOnlyCustody`); it is declared exempt with its reason (swept 34, exempt 25).
- **Measured on the combined branch** (Windows, no browser, no publisher traffic, no custody read): `dotnet build
  Lex.V3.slnx -c Release` 0 warnings, 0 errors; Lex.V3.Tests 3,323 (3,317 passed, 0 failed, 6 skipped); Lex.V3.Ingest.Tests,
  the whole project: 3,423 (3,383 passed, 2 failed, 38 skipped: the conformance census above), then after its fix 3,423
  (3,385 passed, 0 failed, 38 skipped: the opt-in probes, env-gated writers and recorders); the answer, envelope and
  refusal-payload samples re-rendered from the handler with `V3_RENDER_*` were the merged files byte for byte, and verify
  without the switches (7 of 7); `base-answers.json` (schema 8 and 6) and the EU schema-5 base answers unchanged; `npm run
  build` 30 state pages; `npm test` 1,304 (1,301 passed, 0 failed, 3 skipped); `eng/verify-v3-tree.ps1` 1,666 tracked paths.
- **Not done here, and where it goes.** Coverage's per-language state range names no date role: on index 9 its first date may
  be an act's entry into force, which `coverageAnswer.stateRange` calls an applicability date (the platform names no role for
  an aggregate's end; a later slice serves one). F3 (Release 2) and S8 (gates and journeys over D1) as before.

## The reader's interval rules over index /9: a date's state, its refusals, the row facts, document order, the undated fold (sole driver, 2026-10-18)

- **What.** Slice S7a of the Luxembourg data design (`C:\lex-v3\LU-DATA-DESIGN-2026-10-14-v2.md` §3.2, §3.3, §3.4 and the S7a
  row), with what S6 (#980) and #982 handed it. On lex-v3-luxembourg-index/9 every dated operation selects a work's state by
  the interval rules over the index's states and intervals tables; each served state carries its row's catalogue facts, so
  state digest /2 is recomputed from the answer alone (G5); a bundle's articles come in their document's order, a place the
  state holds no article at said; an undated search folds one wording into one hit. The server serves a built mount of index
  9: `V3CorpusMount.ServesIndexNine` and its refusal sentence are gone (`Served` stays the server's one door). On an index 8
  or 6 every answer is the one the code before sent, byte for byte (`base-answers.json`, recorded on Linux at b1b41312 over
  the committed schema-8 mount and the schema-6 mount, replayed unchanged: no row re-recorded). No mount over real data is
  served with it yet: D1 is the next machine step.
- **The rules** (`V3CorpusMount.Intervals.cs`, `CoverAt`):
  - A state covers a date D when `from ≤ D < to`: `from` its date, `to` its `applicable_to` (the publisher's end, else the
    next dated state's start, derived), none when there is neither; an `invalid` boundary covers nothing. The French chain
    holds the French rows and the rows naming no expression; another language's rows take their French state's interval, as
    the index records it.
  - None covers D: `no_version_for_date`, adding on index 9 `reason` and `reason_rule`: `before_history`; `catalogue_hole`
    (`hole_from`, `hole_to`: the publisher's end to the next start, never bridged); `after_closed_end` (`closed_on`, and only
    there the base act's `inForceStatus` and `dateNoLongerInForce` verbatim with `base_act_rule`: the end of the catalogue's
    states is not said to be an end of the law); `publisher_end_convention_unclear` (D is the stated end of a state that ends
    the day before the next begins: both named, given to neither); `publisher_interval_invalid` (D falls where a state whose
    end is on or before its start would apply, up to the next start; its own permalink still opens it);
    `no_expression_in_language` (a French state covers D and none of its expressions in the language asked is held; when no
    French state covers D either, the French chain's reason). Beside them `named_states` (permalinks, or stable coordinates for
    a state naming no expression) and `named_state_sha256s`, `language`, `catalogue_read_no_later_than` (the build time the
    mounted log records), and `nearest_earlier`, now the latest state date on or before D (index 9 knows it; an invalid one
    included, so a date stands on each side whenever the history holds one).
  - One covering state with text is served; one without text is `text_withheld` (`withheld_rights`; the official identity and
    link are the state's legal resource, and `content_sha256` is the state digest, the index binding no body to a textless
    row) or `text_not_available` (`not_available_pdf`, `_derivation`, `_misfiled`, `not_acquired`; transport evidence "none"),
    each naming the state, its status, reason, dates, end and boundary, and saying it asserts no absence of law. A textless
    state's own permalink refuses the same way.
  - Several covering states with one held wording (the same rule profiles; the same articles by publisher id and wording
    digest) are served once: the act as published when exactly one is, else the one that begins last; the others under
    `same_text_states` (basis `identical_text`). Otherwise `ambiguous_version`, adding each candidate's kind, interval, its
    end's source, boundary and text status in lists aligned on `candidate_state_sha256s`, with `candidate_states_rule`.
  - Every refusal member is a scalar or a list of scalars, as the web's refusal card reads payloads.
  - `next_applicability_date` keeps its meaning (the next held state's date in the language), bounded by the date asked.
- **Per operation.** `as_of`, `provenance`, `evidence_bundle`, `status_on`, `citation`, `relations`, the exact references,
  `search` with a date and `diff` select through it (`SelectAt`; the refusals through the shared builders, so every
  operation refuses a date as `as_of` does). `diff` refuses a textless bound for that bound and lists a language not compared
  with its reason. `in_force_on`: the interval's `until` is the state's end with `until_source`; a covering state without text
  is a row naming it and why, with no `resolve`; with a work named and no state covering the date it is `as_of`'s refusal; its
  derivation states the rule (`InForceOnRuleNine`, `InForceOnUntilNine`; "end of validity" is consulted now). `as_observed`
  selects over the snapshot's own rows: the log /3 fold with its textless rows and logged facts (`ResolveObservedStates(...,
  includeTextless: true)`), each row's facts from the index that holds it, else from the log, the chain recomputed over them
  (`Rechain`, the builder's rule, held equal to the index's own by a test).
- **The row facts** (`WithCatalogueFacts`): `state_kind`, `date_role`, `entry_into_force`, `applicable_to`,
  `applicable_to_source`, `boundary`, `publication_date`, `text_status`, `text_status_reason`, `in_file_identity`,
  `in_file_date`, `translation_of`, `provisional` on every state of `as_of`, `provenance`, the bundle, `diff`, `timeline`,
  `dossier`, `status_on`, `in_force_on`, `changes_in_period` and `as_observed`; `date_role` and `entry_into_force` on every
  search hit. `as_of` and the bundle add `catalogue_rule` and `states_without_text` (the French state covering the date when
  it holds no text and a translation is served alone: `loi/2017/07/20/a674`'s shape). Provenance's derivation /9 says each
  state carries those facts; G5 (`V3ReplayGuaranteesTests`) recomputes state digest /2 from the bundle's state and holds that
  copy to the mounted row. The dossier's not-held list on index 9 (`DossierNotHeldNine`) no longer says the publication date,
  the entry into force or the states' ends are not held; the bundle's rights rule on index 9 says the textless refusals.
- **Document order and missing articles.** On index 9 `ResolveStateArticles` reads `document_position`
  (`LuxembourgIndexQueries.StateArticlesNine`, planned on the built index and kept off schema 8's plan) and the bundle serves
  the articles in that order (#978's natural order stays for an index of schema 8 or 6), each with its `document_position`,
  and each run of places the state holds no article at under `missing_articles` (`from_position`, `to_position`, `count`,
  `after_publisher_id`, `before_publisher_id`) with `missing_articles_rule`. An article missing after the last one held is
  not seen by place; the rule says `articles_not_admitted` counts every one.
- **The undated search fold.** On index 9 a search with no date serves one hit per work, publisher article id and wording
  digest: the latest state's that holds it, the others under `same_wording_states` (their date, role, digest, article and
  permalink), and `fold` gives the hits before folding (`SearchFoldRule`; `ResolveArticleWordings` reads each hit's tokens by
  primary key, as #982's texts are read). With a date nothing is folded. #982's population: `states_withheld_by_rights` now
  also counts the versions index 9 records as withheld by their rights (with a date, when the interval rules select one); a
  version not available or not acquired is in no count (`SearchedRuleNine`).
- **R21 in the exact references.** The phase reads the citation generator's usual form of a version dated by its entry into
  force ("…, dans sa version publiée, entrée en vigueur le …", "…, as published, entry into force on …") as a date asked.
- **Driver decisions (reversible):**
  - **Several covering states with one wording designate the one that begins last, not the earliest** (the design's
    "otherwise the earliest state"): the readers hold a served state's next held date to be after the date asked, and the
    state that begins last is the one the rule before index 9 served. The act as published still comes first.
  - **A publisher-issued expression in another language is served in its own language and in bundles**, labelled with
    `translation_of` and a rule that says the publisher's record does not state which language version is authentic (R18,
    R41: publisher-issued German or English texts are expressions, and "only the French text is authentic" is said only of
    Lex's own translation); the design's "never in an evidence bundle" and its French-only note are superseded where the
    specification of record disagrees. It is never selected for a French request.
  - **A language with no held state is still `language_not_available`** (available languages the held ones); the French
    state covering the date of a translation served alone is named under `states_without_text` instead.
  - **`no_expression_in_language`**, a reason the design does not list, says a date the French chain covers and no
    translation does, naming the French state.
  - **`content_sha256` of a withheld textless state is its state digest**, and its official link and source its own legal
    resource: the index binds no body to a textless row.
  - **Every dated operation, not only the five the design names, selects by the interval rules**, so none answers a date
    another refuses.
  - **`in_force_on` across works lists a textless covering state as a row** (named, with why, no `resolve`), and a work no
    state covers on the date gives no row; its population counts stay the index's held-state counts (superseded in the
    combined repair of #987: the works are read from the catalogue's rows, so a work whose only state on or before the date
    holds no text is a row, and counted covered).
  - **The base act's force facts ride on `after_closed_end` only**, verbatim; the in_force_on vocabulary test sets aside the
    typed date `entry_into_force` and nothing else.
  - **Refusal payloads are flat** (scalars and lists of scalars) rather than the nested candidate rows the design sketches:
    the web's refusal card refuses an object member, by its own rule against "[object Object]".
  - **The mount tests' second language is a translation** (`AddTranslationAsync`): on index 9 a German text of a French state
    takes its interval, so the tests that held German independent of the French chain hold it as the builder makes it.
  - **The undated fold keeps the latest state's hit**, so a wording is read under the last version that holds it, and counts
    only the versions searched.
  - **A scope a caller narrows is selected by the rules over the work's whole chain** (corrected in the combined repair of
    #987: this said an ELI's version opens at its date, as before index 9). An exact reference whose ELI names one version is
    asked at that version's own date and opens there only where the interval rules serve it: at its own date it is refused
    when that date is ambiguous (another state of another wording covers it) or its interval is invalid, and on a date
    another version covers it is refused with `outside_named_version` (F2).
- **Tests.** `V3CorpusIntervalMountTests` (new, 12): the handler on a verified mount of the multi-body fixture (S6's, with two
  closed ends, a same-date pair that differs, a provisional state and a document that skips a place, bound to the shared
  acquisition's corpus, which holds none of its bodies, so its log names none): the dates of design §3.4 (before the history,
  the start, a day inside, the last day, a hole, the inclusive-end day, an invalid interval, a closed end with the base act in
  force and no longer in force, an overlap, the same-date identical and differing pairs, textless states, a date after an
  act's withheld consolidation, a translation); textless permalinks; date roles and `provisional`; the bundle's document order
  and missing article; diff's textless bound; as_observed at the mounted snapshot against as_of; the dated and undated search
  population; an ELI naming one version opening it though the rules designate another; the API's chain equal to the index's
  own. Updated to index 9: as_of (the German translation's interval, the
  refusal property lists), diff (five tests whose German text is now a translation of a French state), in_force_on (twins as
  translations, the derivation), dossier (shared fields, not-held list, property list), provenance, timeline (a row is as_of's
  on its own date), as_observed, search (the fold in two tests; two keep their states apart by their wordings),
  `V3IndexNineServingTests` (a built index-9 mount served), G5 from the answer alone, the query plans (`StateArticlesNine`),
  `ExactReferenceReaderTests` (two rows). The answer, envelope and refusal samples re-rendered from the handler and verified
  without the switches; the registry digest did not move.
- **Measured** (Windows, no browser, no publisher traffic, no custody read): `dotnet build Lex.V3.slnx -c Release` 0 warnings,
  0 errors; Lex.V3.Tests 3,323 (3,317 passed, 0 failed, 6 skipped); Lex.V3.Ingest.Tests whole, once, at f8174e0f (the commit before the narrowed-scope repair): 3,379
  (3,345 passed, 0 failed, 34 skipped: the opt-in probes and env-gated writers); after the narrowed-scope repair the filtered
  set (index, legacy schema 6 and 8, as_of, in_force_on, as_observed, evidence bundle, diff, timeline, provenance, replay,
  census, samples, privacy, search, reading pages, citation, MCP, dossier, references, pinned, status_on, changes, article
  history, verify, relations, resolve, journey mounts, gates, content /3 and the interval class): 624 (612 passed, 0 failed,
  12 skipped); `npm test` on 24118293 (#985): 1,280 (1,277 passed, 0 failed, 3 skipped); `eng/verify-v3-tree.ps1` 1,644 tracked paths.
- **Review and repair (#987, 2026-10-19).** One review by the other model family (Decision 95); eight findings, each
  reproduced and fixed, with its test in `V3CorpusIntervalMountTests` (now 17) unless said:
  - **F1 (high), in_force_on across works.** It read limit + 1 work keys as if each gave a row; on index 9 a work with a
    state before the date and none covering it gives none, so a page ended short and `continue_after` skipped works. Keys are
    now read on, a batch at a time, until the page is filled or none remain. Test: every row of the unlimited answer, in
    order, paged at limits 1 and 2 on three dates; REST and MCP give the same page.
  - **F2 (high), an ELI that names one version.** A narrowed scope served a state the date's interval does not hold. CoverAt
    now runs over the whole work; a member opens only if it is the covering state or one of its same-text states, else the
    rules' refusal, or `no_version_for_date` with the new reason `outside_named_version` (the covering and the named states
    listed, flat). Test: the 2005 act named after its work closed, on a date the 2010 consolidation covers, and on its own.
  - **F4 (medium), a code's base act.** `base_act_iri` was the code's work + "/jo", which does not exist. A code's base act
    is now the one its state's `isMemberOf` names, else none is named (`closed_on` stays). Test: two closed codes, one naming
    its base act and one not.
  - **F6 (low), changes_in_period by the rules.** A state served under a same-text sibling is that sibling's row; a date is
    ambiguous only where the rules find several wordings; the baseline is the state the rules serve the day before, and a
    textless or uncovered day after the history began gives the new reason `no_held_baseline` (no baseline, no candidates).
    Test: the same-date pair one row, the unclear end day and the invalid interval without a baseline, an overlap's baseline,
    the differing pair ambiguous.
  - **F8 (low), the in_force_on population.** `works_with_a_state_on_date` counted works in a hole or after a closed end;
    it now counts covered works only, and `works_not_covered_on_date` counts the others (index 9 only). Test: with F1's.
  - **The history's facts (the coordinator's addition, from the S7b/S7c French review).** On index 9 `article_history`'s
    rows and absent states carry their catalogue facts, so the History page says each date with its role (STATUS-WEB.md).
    Test: a row and an absent state of the 2005 act carry the dossier's facts for the same states.
  - F3, F5 and F7 are the web's (STATUS-WEB.md).
  - **Driver decisions (reversible):** `outside_named_version` and `no_held_baseline` are new reason codes rather than
    reusing `catalogue_hole` or `first_held_state`, which would say something false of the case; `works_not_covered_on_date`
    is a new member rather than a changed meaning of `works_beginning_later`, which keeps its index 8 meaning; a code naming
    no base act names none rather than its own work.
  - **Measured after the repair** (rebased on 0b908870): `dotnet build Lex.V3.slnx -c Release` 0 warnings, 0 errors; the
    filtered set (interval, in_force_on, references, search, changes, provenance, census, samples, privacy, legacy schema 6
    and 8, article history, index-9 serving, as_of, dossier, pinned, replay): 260 (257 passed, 0 failed, 3 skipped); Lex.V3.Tests 3,323 (3,317 passed, 0 failed, 6 skipped; the construction census pins the new door RefuseOutsideScope); the answer
    and envelope samples re-rendered (the history's rows gain their facts) and verified without the switches; `npm test`
    1,288 (1,285 passed, 0 failed, 3 skipped); `eng/verify-v3-tree.ps1` 1,650 tracked paths. The whole Ingest project was not
    rerun (other lanes' heavy jobs share the machine).
- **Not done here, and where it goes.** S7b: the timeline's gaps, overlaps and same-date groups, `article_history` across
  textless states and holes, and the speech roles (the Compare card's same-version note, `reading.validityConflict`, the
  exports' and the PDF's "applying from", the History table's column, `search.dated` and the dossier's state span still say a
  state dated by its entry into force with application words; the French review listed each; the timeline speech test).
  S7c: the refusal cards' own wording for textless states, unclear ends, invalid intervals and the closed end (the cards show
  today's fields), the translation label on the reading, the consolidation and code caveat, the events page for textless
  rows. The search hits' order within a version stays #982's reading order of ids (a hit carries no document position yet).
  F3 (Release 2) as before.

## S4b: the double builds compare file digests; no index is held in memory (sole driver, 2026-10-18)

- **What.** Slice S4b of the Luxembourg data design (`C:\lex-v3\LU-DATA-DESIGN-2026-10-14-v2.md` §2.5 and the S4b row). No
  rule and no output changes: the mount files are the same bytes (measured below), and no digest pin moved (the
  censuses gain the new types, under Tests).
  - **The double builds.** `V3FirstMountBuild` builds each index twice, to two files in its own staging directory
    (`lex-v3-mount-build-*` under the temporary folder).
    - It compares the two files' SHA-256, read from disk in order, and their capability manifests byte for byte.
    - The refusal texts are unchanged ("two builds of the Luxembourg index differ: … then …").
    - The first file is verified by the reader. The second is not read back: it is held to the first file's digest, so
      it is either the first file's verified bytes or a refusal. It is removed as soon as it is compared.
    - The writer moves the first file into the mount (a rename on one volume). It measures the digest again and refuses
      a file that is not the one the two builds compared.
    - A refused build removes its staging directory. A delivered build owns it until it is disposed:
      `V3FirstMountBuildResult` is `IDisposable`, and the tool, the offline derive and the tests dispose theirs.
    - Before, the build held each index as a byte array, twice: the first build's bytes, then the second's. The
      Luxembourg verification made a third copy, and the writer copied each file again as it wrote it.
    - The corpus is still compared in memory (digest, then bytes), because the writer needs its bytes anyway. Its second
      build is released as soon as it is compared.
  - **Inside the builders** (`LuxembourgIndexBuilder.TryBuildFileAsync`, `EuropeIndexBuilder.TryBuildFileAsync`):
    - Each builds from the corpus the mount build built and compared, so the four corpus rebuilds inside the index builds
      are gone.
    - The projected rows live in one frame and are released before the file is read back.
    - The file is verified through the reader's own file door (`OpenAndVerifyFileAsync`: a private copy, hashed and read
      back), with the same checks as the bytes door. No reader code changed (S7a's area).
    - The public `TryBuild` methods go through the same writer (`WriteFile`) and return the same bytes.
  - **The writer and the derive:**
    - Held files are written straight from their bytes, with no copy.
    - The Luxembourg log is read from a private copy of the file (`TryReadLogFile`; `TryReadFile` for a predecessor),
      not from an array of over a gigabyte. The generations are copied file to file.
    - `V3OfflineMount.DeriveAsync` releases the populations before it verifies the written mount, and writes a restored
      predecessor's files without a second copy.
    - The derive tool now prints `process peak_working_set_bytes=… peak_private_bytes=…` (on Windows, the private
      commit) beside `publisher_requests`, for the dry run.
- **Measured without the population.** The population: the acquisition fixture with its Luxembourg consolidation replaced
  by N invented articles of about 1,200 bytes each, every byte written by the probe. The probe is `V3DeriveMemoryProbe`
  (opt-in, never a CI test). The machine was shared; another session's tests ran during the second round.
  - **Through the offline tool, in a process of its own: the dry run's whole path.**
    - The probe writes the population's custody and inputs /3 (Release 1 over inputs /1).
    - `Lex.V3.Tool derive` then runs over that same custody with the integration build (`8e6d3d27`; #982 and #984 since
      then change no derive code) and with this slice's build.
    - A wrapper samples the child process every 100 ms, as the runner's guard does.

    | Articles (body, Luxembourg index) | Peak private commit, before → after | Peak working set, before → after |
    |---|---|---|
    | 20,000 (26.0 MiB, 85.4 MiB) | 1,067–1,150 → 606–651 MiB (3 runs each; −40 to −45%) | 1,063–1,141 → 550–633 MiB |
    | 40,000 (52.0 MiB, 170.7 MiB) | 2,033–2,680 → 1,341–1,416 MiB (2 runs each; −34 to −50%) | 1,953–2,535 → 1,110–1,125 MiB |

    - The old build's peak varies more from run to run: much of it is dead arrays waiting for a collection.
    - The five mount files are identical, byte for byte, at both sizes. At 40,000: Luxembourg index `296b0454…`, corpus
      `f8ea14c7…`, EU index `c4338705…`, and both capability manifests.
    - The re-derivation record is identical too (`3d406f06…` at 40,000, `3f8046f7…` at 20,000), and every derive
      measured 0 network operations.
    - The build report differs in one line only: `toolSource`, the commit that ran it.
  - **The late phase in process** (the build, the write and the verification, sampled per phase after a full collection
    once R1 is done; measured before the read-back of the second build was dropped):
    - 10,000 articles: process peak commit 799 → 415 MiB; the late phase above the baseline, 432 → 61 MiB.
    - 20,000 articles: 1,383 → 889 MiB; the late phase above the baseline, 829 → 307 MiB.
    - Each in-process run acquires afresh, and an acquisition mints its own identities, so these runs' digests are not
      compared. The runs through the tool compare digests.
  - In the synthetic runs, the saving is 4 to 8 times the Luxembourg index's size. Three effects add up: two or three
    copies of each index's bytes, the corpus rebuilds, and the rows held while the file was read back.
  - **Disk.** In the late phase, the staging directory holds the first build of each index and one file being built:
    about L + 2E. Before, it held about 2E: a build's file and its verification copy. For the population that is about
    1.3 GB more disk at the late phase's peak, against about 4 GB less commit, which the page file no longer has to grow
    into.
- **Extrapolated to the population, honestly.** Here L is the Luxembourg index (1.2–1.4 GB under Release 1) and E the EU
  index (1.4 GB).
  - **The late phase.** By the code, each of the old build's worst late moments held about 3.6–4.2 GB of index bytes:
    - the second Luxembourg build's verification: three copies of L;
    - the second EU build: L and two copies of E;
    - the write: both indexes, and a copy of the file being written.

    The new build holds none of this. It also no longer rebuilds the corpus inside each index build (four times: a
    0.21 GB array and its two parsed sets). So the late phase frees at least about 4 GB, likely 4.5–5 GB: the design's
    "about 4.6 GB".
  - **What R1 adds to that phase**, by the design, is about the same amount: content tokens for up to about 217,000
    articles held through the builds (1.5–2 GB in Release 1), and a larger Luxembourg index and its rows.
  - So the builds should end no lower than derive-c's 0.27 GiB, probably 1–2 GiB higher. That is not a proven 2 GiB.
  - **R0's replay is untouched.** In derive-c it reached 0.4–0.8 GiB of headroom (minutes 37 to 53; peak working set 15.2
    GiB; the page file grew 18.6 GiB). D1 runs the same replay.
  - **So D1 is not expected to keep 2 GiB at its low point under derive-c's conditions** (6.53 GiB of commit headroom at
    the start). After S4b, the binding phase is R0's replay, not the builds.
  - **What the dry run must still be given:**
    - The exclusive window: no IDE, browser, assistant apps, journeys or other sessions' test runs. On 14 October, three
      stray `grep` processes alone held about 2.5 GB.
    - About 8 GiB of commit headroom at the start: derive-c's 6.53 GiB plus the 1.2–1.6 GiB that R0's replay took below
      2 GiB.
    - Free disk for the page file: Step 0's 40 GiB. Derive-c went from 43.5 to 22.6 GiB free. C: holds 11.8 GiB free now
      (06:52 UTC; a 14.2 GiB page file allocated), so as things stand the dry run would stop on disk in R0's replay.
  - The dry run's resource log, with the tool's new peak line, decides. S4b is judged there, on real data.
- **Review and repair (#986).** One independent review found nothing high or medium. It reproduced the byte identity
  (the five mount files and the record over the 20,000-article custody, both tools, twice each), the measurement
  (−42 to −56%), and the double build's independence: a later build time, and a reversed insertion order in either
  second build, were each refused `NotByteStable`, with nothing left in the temporary folder. Three low findings,
  repaired:
  - **L1.** No test exercised the refusal when the two builds of an index differ. A test seam
    (`V3FirstMountBuild.SecondBuildForTest`, null outside tests) makes the second build another file or another
    manifest. For each index, the build refuses `NotByteStable` with both digests and leaves no staging directory. A
    build that compared a first file with itself would deliver there.
  - **L2.** A write that failed after moving a staged index deleted the build's only copy, so a retry threw
    `FileNotFoundException`. A failed write now moves every staged index it took back to the staging directory: from
    its temporary name, or from its place in the unfinished mount.
    - Tested with a tampered staged file: the write is refused with no half-written file, and the file is back in
      staging.
    - A retry gives the same clear refusal, because the tamper is in the staged bytes themselves (the chosen
      behaviour).
    - Once the bytes are restored, the build writes, the mount verifies, and the file is byte-identical to the first
      mount's.
  - **L3.** A hard-killed derive left `%TEMP%\lex-v3-mount-build-*` (up to about 4 GB at the population) on the binding
    disk. Each staging directory now holds an owner file open until its build is disposed. Before it reads anything,
    the derive tool (and the acquisition command, before its build) calls `V3FirstMountBuild.SweepStaleStaging` and
    prints `swept N stale mount-build staging directories under … (bytes): names`. The sweep removes a directory only
    when all of these hold:
    - its name is exactly `lex-v3-mount-build-` and 32 hex digits;
    - it is not a link;
    - it was last written before the process started (a directory's last write moves with every file made or removed in
      it, on every system);
    - no live build holds its owner file.

    Tested: two stale directories (with and without an owner file) are removed. A live build's, a recent one, one named
    otherwise and an unrelated one are kept. The tool, given its own temporary folder, sweeps a planted directory and
    names it.
  - The staging test now uses its own directory as the staging root, so a long temporary path stays under 260
    characters.
- **Tests.**
  - `V3MountBuildFileTests` (5, the repair's three included):
    - Each index built to a file is the bytes its public builder returns: the genesis Luxembourg index, a chained one
      with a derivation time, and the EU index. A predecessor read from the file equals one read from the bytes. A
      refused build leaves no file.
    - The staging lifecycle: only the two first builds' files are staged. The writer moves them and holds them to their
      digests. A second write copies byte-identical files, and both mounts verify. Disposing a build removes its staging
      directory, written or not. A tampered staged file is refused, with no half-written file left. A refused build
      leaves nothing.
  - The tests that build mounts now dispose their builds. `V3FirstMountBuildTests` compares two builds' files with
    `ReadAllBytes`.
  - The guarded-construction census pins `V3BuiltIndexFile` (and the seam's door), `V3CorpusMountFile`, `V3MountStaging`
    and the predecessor's file doors. The partition totals move: 272 → 275 candidates, 143 → 146 guarded types.
- **Driver decisions (reversible):**
  - The indexes are compared by file digest. The corpus keeps its byte comparison, because its bytes are in memory for
    the writer anyway.
  - Both index builds take the compared corpus. Each index's double build still proves that its own projection and
    writer reproduce; the corpus's own double build proves the corpus.
  - The second build is not read back. Equal digests make it the first file's verified bytes.
  - The first build is verified through the reader's file door, a private copy on disk, so the transient is one index,
    as before. Opening the staged file in place would save that copy, but it needs a new reader door, and the reader is
    S7a's file.
  - The writer moves the staged file. A second write of the same build copies it from the first mount.
  - The build's public shape changed: `LuxembourgIndex` and `EuropeIndex` are now `V3BuiltIndexFile`;
    `V3CorpusMountFile` is a class with `ReadAllBytes()`; `V3FirstMountBuildResult.Success` is internal. Only the tool
    and the tests used them.
- **Not done; each its own slice if the dry run names it:**
  - content tokens as UTF-8, or spilled to a temporary file (design §2.5, step 4; about a day);
  - the Luxembourg reader's verification, which reads every article row into memory (`ReadArticles`): the reader is
    S7a's area;
  - the corpus's 0.21 GB of canonical bytes, still held through the index builds;
  - R0's replay;
  - §2.5's other S4b item, "R0 releases its /3 and /1 populations once they match", is deferred: this slice neither
    measured nor changed it. `DeriveAsync` already hands R0's result straight to R1 and holds it nowhere (S4). Whether
    those populations are freed early enough is for the dry run's resource log to show.

## Content /3: quoted wording, deleted wording, layout markers and unmarked notes; Release 1 runs it (sole driver, 2026-10-18)

- **What.** Slice S3b of the Luxembourg data design (`C:\lex-v3\LU-DATA-DESIGN-2026-10-14-v2.md` §1.4, "Content
  profiles /2 and /3", and the S3b row). `LuxembourgAknLegalContentProfile3Producer` reads inventory /4's articles
  with /2's index-term rule and the shapes of the design's /3 table. It sits beside /1 and /2, which keep their rule
  texts, pins and outputs: /1 `dd32d9a5…` (R0 re-verifies `content_sha256 abc0a299…` in the dry run; the frozen replay
  passes unchanged), /2 `bf3fea5a…`. /1's producer class is unchanged; the shared types beside it gain /3's kinds,
  marks and identity layout, their /1 and /2 branches hashing as before; /2's index-term reader is made internal so /3
  calls it. Rule profile /3: sha256 `33bb002cd33e066f98e238e371b56e60e62ce37736c86cd141173c1018b7be47` (22 lines,
  2,889 bytes, recomputed in Python from the text written out line by line; it was `68ae0756…` before the review of
  #981 added the attribute-marked deletion rule). It names inventory /4's and content /2's
  digests.
- **The rules.**
  - `eop`, `eol` and `br` make no token. Where one sits between two characters of wording that are not white space, one
    text token of a single U+0020 stands at the marker, so words never fuse in the searchable text. Inside deleted wording
    that space is deleted wording too. A marker that holds anything is a gap.
  - `sub`, `def`, `abbr`, `em`, `term`, `inline` and a `span` without a class are containers, as `b` and `i` are.
  - `u` outside quoted and amending structures is a container; its text, reference and deleted tokens carry
    `underline`. A `u` inside `quotedStructure`, `embeddedStructure`, `quotedText`, `embeddedText` or `mod` is a gap.
  - The four quotation elements become `quotation_start` and `quotation_end` around their tokens. Each names its element,
    carries `startQuote`/`endQuote` verbatim as its marker (1 to 64 characters) and says `marks_in_wording` when
    `scl:hide-quote="true"`. The wording inside is the act's own: searchable, quotable and cited. `book` to `alinea` (and
    `subpart`) are containers only inside a quotation.
  - `del`, and a `span` of class `strike`, are deleted wording: each text is a `deleted_text` token, and a reference
    inside is a `deleted_text` token keeping its target, never a `reference`, so it emits no `cites` row. Inside: text,
    the inline containers, `u`, layout markers, nested deleted wording, empty modification boundaries and note references.
    Anything else is a gap. A `del` with a class is a gap.
  - A `span` of any other class is a gap until reviewed.
  - A deletion an attribute marks (`style="del"` on any element, `class="strike"` on anything but a `span`, the article
    itself included) is a gap unless every character of wording inside it is already deleted wording (the review of
    #981, F2): the design's table reads deletion from `del` and struck spans only, so such wording is never read as in
    force.
  - A `noteRef` with no `marker`, empty and bound by its id to exactly one note, is a note reference with `unmarked` and
    no marker. A note body holds no quotation and no deleted wording, so a note's text is never deleted or quoted wording.
  - Still gaps: tables, a modification span across articles and a non-empty `mod`, MathML and every other namespace,
    images, processing instructions, embedded documents, and every element the profile does not name (`docNumber`).
- **The token model.** Three kinds (`quotation_start` 6, `quotation_end` 7, `deleted_text` 8) and four init-only marks on
  `LuxembourgAknLegalContentToken` (`Underline`, `Unmarked`, `Quotation`, `MarksInWording`). A /1 or /2 article refuses a
  token carrying any of them, and a population holds /3 articles only under /3's digest.
  - Article identity layout `/3`: layout /2's fields with each token's four marks after its marker.
  - `tokens_json`: a token without a /3 mark is written as before, byte for byte (pinned by a test); one with a mark adds
    only the marks it carries (`underline`, `unmarked`, `quotation` with `marks_in_wording`).
  - The wording digest (`LuxembourgIndexReader.WordingSha256`) adds deleted wording as `["deleted_text", text, target]`
    (consecutive deleted text merged as text is) and each quotation boundary as `[kind, null, null]`. A stream with
    neither digests as before (pinned against the old algorithm).
  - `searchable_text` and the `cites` projection are unchanged code: they read text and reference tokens only, so deleted
    wording never reaches them.
- **Through the reader, the exports and the web.**
  - The evidence bundle gives a row whose wording holds a quotation or deleted wording its `segments` (text segments,
    whose texts concatenate to exactly `text`; quotation boundaries; deleted wording) and the answer a `segments_rule`. A
    row and an answer without them keep their bytes. An unmarked note is `{marker: null, text, unmarked: true}`. A stored
    stream whose text segments are not its searchable text is refused.
  - `text`, `text_sha256`, quotes, citations, search and `cites` never hold deleted wording.
  - Web, exports: STATUS-WEB.md, the section of the same day.
- **Driver decisions (reversible):**
  - **Release 1 runs content /3.** Rule set `lu-release-1` is now inventory /4 `8a011d87…`, content /3 `33bb002c…` and
    state digest /2 (S6, #980), in that order, policy /1 `a477f4f2…` (`LuxembourgRederivation.RulesOf`, `ApplyAsync`). The design placed /3 in Release 2; the owner
    wants the fullest correct text as soon as possible, and nothing argues for waiting:
    - no inputs are sealed under `lu-release-1` yet (the dry run has not run), so the change costs no machine run;
    - /3 reads every article /2 admits to the same stored tokens, but 18 whose wording an attribute marks deleted and
      which /2 reads as in force (0 disagreements over run 15, below);
    - it adds 5,945 articles to the bodies admitted today, of the 7,704 S6 measured missing from their held states;
    - this slice carries the new kinds through the reader, the exports and the web.
    Inputs that name content /2 now refuse ("no rule digest is substituted"), tested.
  - **What a re-derivation must be run with.** The same command as before, and the rules it writes are the build's:
    `rederive-inputs --custody C:\lex-v3\lu-population-20261011-15\custody --base <run 15's mount-inputs-*.json>
    --rules lu-release-1 --policy a477f4f2e6e0468bc9069da742f6f34f248ec831e01277a40552cb1e5bad2abb --derived-at <utc>`,
    then `derive`, then `rederive-seal` with the record it prints. The inputs name inventory /4, content /3 and state
    digest /2. Inputs naming content /2, or written before index 9, refuse (tested).
  - Deleted references keep their target (as `deleted_text`), so no evidence is lost and no `cites` row is made.
  - A note reference inside deleted wording stays a note reference (5,123 in run 15's consolidations): the note is the
    publisher's apparatus on the deletion, and its references are in-note `cites` rows, as every note's are.
  - An unmarked note has no marker even when the note element carries one (`**` on 344 notes of acts): the reference wrote
    none.
  - `scl:hide-quote="true"` means the publisher writes the marks in the wording. Measured over the admitted acts: 2,030 of
    2,809 such structures begin with «, against 48 of the 3,721 without it. The reader adds « » for the others.
  - A `span` without a class is a container whatever else it carries (`style="li_symbol"` 637 times, `scl:leader`,
    `scl:background-color`). In the admitted bodies the only class is `strike`; the withheld consolidations also carry
    `h2`, `li_symbol`, `p` and `s1` to `s10`, which stay gaps.
  - The hierarchy inside a quotation is the observed set; `subFlow`, `doc`, `meta` and `longTitle` stay gaps.
  - The reference rule stays /1's (a label of text, `b`, `i`, `sup`); 106 articles held today stay gaps on it.
  - `docNumber` is not in the design's /3 table and stays a gap: 168 code articles held today. It is the next shape to
    review.
  - A deletion an attribute marks is a gap, not read as deleted (the review of #981, F2): reading `style="del"` as
    deletion is not in the design's table, and the gap is the conservative reading. 86 articles carry the reason in run
    15 (all in withheld bodies but one): 18 that /2 admits and reads as in force, among them 16 articles of consolidations
    whose whole `article` carries `style="del"`, and 68 that /2 refused too, 44 of which /3 admitted before the rule.
- **Measured: /2 and /3 over run 15** (`LuxembourgAknContent3Run15Probe`, opt-in with `LEX_LU_CONTENT3_PROBE=1` and
  `LEX_LU_CONTENT3_PROBE_ROOT`, over S3a's probe inputs; custody read by digest through S2's read-only decorator and never
  written; no network; no R0; never a CI test). Inventory /4 over all 11,205 held inputs, then /2 and /3 over its 308,521
  main-body articles, in chunks of 32 bodies. Rerun after the review of #981 on rule `33bb002c…`: 36 s and 75 s (the
  first run, before the repair: 209 s and 127 s), peak working set 1,837 MiB.
  - **The S6 figure: /3 recovers 5,945 of the 7,704.** In the bodies admitted today that hold an article /2 admits (the
    held states' documents), /2 refuses exactly S6's 7,704. Recovered, by /2's shape: `embeddedStructure` 1,846,
    `embeddedText` 1,554, a note reference without a marker 1,288, `eop` 482, `u` 480, `span` 126, `br` 64, `del` 61,
    `sub` 34, `def` 9, `eol` 1.
  - **Still refused there: 1,759**, by /3's reason: an underline inside quoted wording 724, `table` 652, `docNumber` 168,
    a reference label of another shape 106, a non-empty `mod` 33, `img` 28, another namespace or a processing instruction
    25, `reference lacks a bounded target or publisher label` 11, a deletion an attribute marks 1, and 11 other elements
    (`part` 4, `doc` 2, five of one).
  - **All of run 15:** /3 admits 298,617 of 308,521 articles (/2: 270,552), 28,083 that /2 refused: codes 3,090 (149,098
    of 149,542), acts 14,311 (62,984 of 68,049), consolidations 10,682 (86,535 of 90,930). 9,904 stay gaps, 18 of them
    articles /2 admits (F2, above). Before the repair: 298,679, 28,127, 9,842.
  - **0 disagreements:** every article /2 admits reads under /3 to the same disposition and the same stored tokens, but the
    18 F2 withdraws, which the probe counts apart.
  - **Independent check.** A Python reading of /3 written from the rule text (lxml; it shares no code with the C#), with
    the F2 rule, over the same bodies matches all 308,521 articles: the /2 and /3 verdicts and details, and for the 298,617
    admitted the searchable text's digest, the deleted wording's digest, the quotations and the unmarked notes. 0
    differences (also 0 before the repair, over 298,679).
  - The design's estimate was "8,000–13,000 articles to the Release 2 figures"; the measured 28,083 is over every
    admission class, and 5,945 of it is in the bodies admitted today.
- **Excerpts (legal care).** Two byte-exact excerpts of bodies run 15 acquired under the agreed CC BY reading, cut as S2
  and S3a cut theirs (whole element ranges removed, every byte kept the publisher's, in order), in
  `Fixtures/LuAknLegalContent3/`, a guard holding the folder to them:
  - `act-loi-2019-12-20-a873-quotations-and-struck-spans.bin` (13,530 bytes, sha256 `c5749008…`) of
    `loi/2019/12/20/a873/jo` (body `3236529c…`, 37,957,824 bytes; its images among the 4 ranges removed): quoted terms
    and structures, a structure whose marks the publisher writes, the struck old numbers of `art_13`, and `art_2`, whose
    struck span sits beside an underline inside a quoted structure (a gap).
  - `code-consommation-20220101-deleted-wording.bin` (21,533 bytes, sha256 `b5adf8cc…`) of `code/consommation/20220101`
    (body `9ade6eb0…`, 1,749,582 bytes): deleted wording, deleted wording holding two references, a note reference
    without a marker and an underlined number.
  - No consolidation is admitted in run 15, so the consolidation's `del` and the struck span of
    `loi/2012/03/21/n1/consolide/20220614` are synthetic, in the publisher's shape. The withheld fixtures get no use.
  - The nine excerpt articles' identities, tokens and searchable texts equal the Python reading's.
- **Tests.** `LuxembourgAknLegalContentProfile3ProducerTests` (49): the pins, both excerpts, every /3 shape synthetic,
  the deletion an attribute marks (F2),
  the still-typed gaps, the two /1 tests the design names, the door, the identities and the shared types, the stored
  form and wording digest of a /2 stream unchanged. `LuxembourgContent3MountTests` (1): a synthetic consolidation
  acquired, re-derived under `lu-release-1`, built, written, verified and opened; its index's searchable text holds no
  deleted wording, `cites` holds the quoted reference and not the deleted one, and the bundle's composition over the
  index's own rows gives the segments and the unmarked note. `LuxembourgRederivationTests` move to content /3 and refuse
  inputs naming content /2. The closed-vocabulary and guarded-construction censuses gain the new kinds and door.
- **Review and repair (#981).** One independent review found nothing high or medium (deleted wording stays out of
  `searchable_text`, quotes, citations, `cites` and the exports' text; the pins and the probe reproduce; 14 of 15 mutations
  caught). Repaired: F1, quotation marks doubled where the wording already holds them (STATUS-WEB.md); F2, a deletion an
  attribute marks, now a gap (above; the rule digest moved and was re-pinned, the probe rerun); F4, the CSV's `segments`
  column moved last; F5, the PDF's text line asserted exactly, which the surviving mutation now fails. Rebased on
  dc06b414 (#980): `lu-release-1` is inventory /4, content /3, state digest /2.
- **Next.**
  - **F3 (Release 2).** 1,552 consolidation articles whose whole wording is deleted wording are named under
    `articles_without_text` with `no_text_tokens` (`V3CorpusMount.cs`, the bundle's quoted and without-text split), so
    their struck wording is never shown. Planned: a distinct reason (`deleted_wording_only`) with the deleted segments
    served beside it, never as a quote; Release 2, as consolidations are withheld in Release 1.
  - The bundle served through the handler for a re-derived population: index /9 (S6, #980) is in, but the re-derived
    index of the shared synthetic acquisition holds this consolidation's articles and no state row (measured on dc06b414),
    so a handler-level test needs a fixture whose catalogue names the consolidation's states; the mount test runs the
    bundle's composition over the index's rows meanwhile.
  - S8's independent oracle must read the /3 kinds. The dry run, D1 and the gates as planned.

## Index /9: every catalogue state of a served work, held or textless, dated by role, with ends, boundaries and intervals (Claude, 2026-10-17)

- **What.** Slice S6 of the Luxembourg data design (`C:\lex-v3\LU-DATA-DESIGN-2026-10-14-v2.md` §3.1, §3.3, §3.4), with the two
  things S4 deferred to it: state digest /2 in rule set `lu-release-1`, and `log.derived_at`. The builder writes
  `lex-v3-luxembourg-index/9` (`user_version` 9) with event log `lex-v3-event-log/3`; the reader opens /9, /8 and /6, each
  exactly. On an index 8 or 6 every answer is the one the code before index 9 sent, byte for byte (held by a committed
  schema-8 mount that code wrote, and the real schema-6 mount: the review of #980). On an index 9 every operation that
  existed before reads the held rows only, until the reader's interval rules land (S7a), and the server refuses a built
  mount of index 9 until S7a turns one switch (below). No mount over real data has been built with it: the dry run is the
  next machine step.
- **Review and repair (#980).** The independent review found the core sound (digest /2 and its profile recomputed, the
  probe's counts reproduced, the tampers refused, derive-c's real /8 read identically through base and head over 8,681
  reader answers) and asked for four repairs and a minor one, all made:
  - F1: no test read an index 8 this code did not write. `Fixtures/LegacySchema8Mount` holds a schema-8 mount built and
    verified at `3b7913ec` from a synthetic act (its XML beside it), pinned by digest; `V3CorpusLegacySchema8MountTests`
    reads it with schema 8's checks, and the query-plan tests plan schema 8's own texts of the three state queries on it.
    Two fields swapped in `LegacyStateRow8.Of` now fail; they passed before.
  - F2: a schema-8 or schema-6 answer had gained `log.derived_at`, the "per state row" scope, the genesis note's
    derivation sentence, as_observed's "/8 or /9" and the MCP events description. Each is index 9's only now, and
    `base-answers.json` (the integration head's whole envelopes for 29 requests over the schema-8 mount and 9 over the
    schema-6 one, recorded at `5a659831`) is replayed byte for byte. The answers are recorded on Linux, as the release
    image runs them (globalization-invariant, UTC; with the language library the bytes are the same): the answers
    production sends. One request answers otherwise on Windows with the same code, base and head alike: resolve of
    `/lu-legilux/loi-2026-01-01-a1` is `identifier_unknown` on Linux, where `Uri.TryCreate(UriKind.Absolute)` reads a
    rooted path as a file URI and `LooksLikeIdentifier` takes it, and `retrieval_mode_unavailable` (title discovery) on
    Windows. CI's image-conditions lane found it. The platform difference was the base's `LooksLikeIdentifier`, not this
    slice's; since #983 a rooted path is an identifier on every system, as production reads it, and every system is
    held to the Linux recording. Provenance's digest /2 sentence on schema 8 now fails.
  - F3: the fixture has an end on its start (the real `code/procedure_penale/20211214`): an invalid boundary and
    transition. `<= 0` made `< 0` now fails.
  - F4: `V3CorpusMount.ServesIndexNine` is the one switch S7a turns. Until then the server refuses a built mount of index 9
    (one with its build report, as a release's mount is) with `IndexNineNotServedSentence`; a test fixture mount, with no
    build report, is served, so the journeys and samples run. G5 from the answer alone is an expected gap on index 9:
    `V3IndexNineServingTests` and the G5 case of `V3ReplayGuaranteesTests` fail when the switch turns, until S7a updates them.
  - Minor: a textless row took the in-file facts of another expression's file (`loi/2017/07/20/a674`'s French row carried
    the English file's); it takes its own expression's only. A catalogue state naming no expression adds no row beside a
    held row of the same state.
- **The states table** keeps schema 8's nine columns and adds `state_kind` (`as_published`, `consolidation`, `code`),
  `date_role` (`applicability`, `applicability_in_file`, `entry_into_force`: the role names the date's source),
  `applicable_to` and `applicable_to_source` (`publisher` or `derived`), `boundary` (`half_open`, `inclusive_suspected`,
  `invalid`, `derived`), `publication_date`, `text_status` (`held`, `withheld_rights`, `not_available_pdf`,
  `not_available_derivation`, `not_available_misfiled`, `not_acquired`) and `text_status_reason`, `in_file_identity`,
  `in_file_date`, `translation_of`, `provisional`, and `entry_into_force` (beyond the design; the driver decisions). Its CHECKs:
  an end and its source come together; a row is held exactly when it lists articles, and exactly when it gives no reason; an
  empty expression comes exactly with an empty language, and only on a `not_acquired` row.
- **The articles table gains `document_position`** (#978's finding: every state lists its articles by digest, and the
  index held no document order): an admitted article's place among its publisher document's main-body top-level
  articles, from 1, in the publisher's order, which inventory /4 lists (its rule profile's `publisher-order`; /3 lists
  them in document order too). The build takes it from the content outcome's own inventory and refuses an admitted
  article its inventory does not list; an article not admitted keeps its place, so a body's positions can skip (the gap
  is the missing article's place). No digest but the logical rows' covers it: it is a fact of the file, not of the
  state or the wording. The reader checks a body's articles hold distinct places; S7a reads a state's articles in that
  order, replacing #978's stopgap (the natural order of their ids).
- **The `intervals` table** (work key, ordinal, kind, from and to state digests, span): each work's French chain in date
  order, one row from each state to the first state of the next date (`contiguous`, `derived`, `inclusive_suspected`,
  `hole` and `overlap` with their spans, `invalid`), and one between consecutive states of one date (`same_date`).
- **Observations gain `derived_at`** (log /3): the time a derivation from custody states it ran (its inputs' `derived_at`),
  null for the acquiring build's own derivations; a UTC second no earlier than `built_at`. `V3FirstMountBuild` passes the
  acquisition result's `DerivedAt` to both index builds. The events answer serves `log.derived_at`, and its genesis note
  says what it is: "log.derived_at is when this index was derived from custody, the time its derivation's inputs state, and
  null when its derivations are the acquiring build's own or when its schema records none (lex-v3-luxembourg-index/8)".
- **How a row is made** (`LuxembourgIndexBuilder.ProjectCatalogueStates`, a pure function of the catalogue's facts (the
  `work_facts` rows), the held bodies with what inventory and content made of them, and the corpus records; the envelope's
  build and the test harness both reach it, then one writer, `WriteIndex`):
  - A held row is a held body's selected expression with its admitted articles (as schema 8's states were); its language
    is the selected WEMI's, refused if the catalogue states another for the expression.
  - A catalogue state is a `work_facts` subject that spells an act (`…/jo`), a consolidation (`…/consolide/yyyymmdd`) or a
    code state (`…/code/name/yyyymmdd`); its work is that IRI less the suffix, keyed by `WorkKeyOf` (a code by the code:
    `code-penal`). A served work is one with at least one held row. Every catalogue state of a served work that has no
    held row gets a textless row for each of its French catalogue expressions, or one with an empty expression and
    language when the catalogue names none.
  - Dates by role: a consolidation or code state by its catalogue `dateApplicability` (`applicability`); an act by its
    file's own `dateApplicability` (`applicability_in_file`: inventory /4's evidence, or inventory /3's coordinate date)
    else its catalogue `dateEntryInForce` (`entry_into_force`). One exact value or the state gets no row (its articles stay
    in the index in no state, as derive-c's undated acts' did).
  - The act's catalogue entry into force is kept in `entry_into_force` whatever its date's role, so the file's
    applicability and the entry into force are two values the reader can speak apart (R21).
  - The publisher's end is the catalogue's `dateEndApplicability` (one exact value; consolidations and codes only); the
    publication date is the catalogue's `publicationDate`.
  - Text status, rights first: a held body withheld for rights is `withheld_rights` (its rights disposition, then any
    content-consistency findings: S2's R8); an acquired PDF `not_available_pdf`; a file inventory /4 refused
    `not_available_misfiled` (the checks and the states they name); an acquired XML with no admitted article, or rejected,
    `not_available_derivation`; no body: `not_acquired`, with the record's member outcome and body axis, or
    `not_a_corpus_member`, or "another expression of this state was acquired".
  - A translation (a language other than French) takes its French state's date, role and interval, and names its French
    expression (`translation_of`) when its legal resource has exactly one French row.
  - Over each work's French chain (French rows and rows naming no expression, by date, then act before consolidation before
    code): the end is the publisher's when stated, else the next later date's start (`derived`), else none; the boundary is
    `invalid` for an end on or before its start, `inclusive_suspected` for an end the day before the next start,
    `half_open` for any other stated end, `derived` without one. `provisional` is a date after `built_at`'s day.
  - The work check refuses a contradiction: an act or consolidation whose `isMemberOf` names another work, or a code
    state whose work the catalogue types and not as `jolux:ComplexWork`. Two rows of one key refuse.
- **State digest /2** (`StateSha256Two`): the prefix `lex-v3-luxembourg-expression-state/2`, the publisher, the key, the
  date, its role, the kind, the expression, the work and legal resource, the language, the publisher's end, the
  publication date, the text status and reason, the in-file identity and date, the entry into force, `translation_of`,
  the count and each profile, the count and each article identity, each length-prefixed UTF-8. The derived end, the
  boundary and `provisional` are not in it: they move with the work's other states and the build time, so a chained build
  that adds a state leaves an unchanged state's digest. Its rule profile (`lex-v3-luxembourg-expression-state/2`, 16
  lines, 1,596 bytes, sha256 `943c77afa168d4f7d10aab5e11b46ed5443ee4a1a70353dbe1a134eb399459c2`, recomputed in Python) is
  the third rule of `lu-release-1`, after inventory /4 and content /2: inputs /3 written before S6 name two rules and
  refuse (`RequireImplemented`), so no build runs them under a state rule they did not name.
- **The event log /3.** One `first_sighting` per state row, held or textless; each sighting (and `file_replaced`) carries
  the end the publisher states (`applicable_to`, `applicable_to_source` `publisher`, or both null: a derived end is not the
  state's own and is reported by `interval_closed` and `validity_revised`), `text_status` and `publication_date`; a textless
  row names no source body. The chain's ends (`ChainEnds`) are the publisher's when stated, so a chained build emits no
  `validity_revised` for a publisher end that did not change; a translation has no interval events (its French state's
  cover it). `LoggedState` and `FoldLog` carry the three facts (an interval event keeps its key's last sighting's).
- **Index 8 is never carried forward.** `LuxembourgIndexPredecessor.TryRead` (a build's predecessor) refuses log /2
  `PredecessorSchemaDiffers`, so an index 9 build over an index 8 starts a new genesis log (design §3.3, decision 10);
  `TryReadLog` reads and verifies log /2 or /3 for the mount writer's verification of a mount and its generations.
- **The reader** (`LuxembourgIndexReader`):
  - Opens /9, /8 and /6 by `user_version`, each against its exact DDL. /8 and /6 are checked as before: state digest /1,
    log /2 (or /6's genesis log), schema 8's logical-rows digest (`HashLegacyLogicalRows8`, the old JSON shape). /9:
    `ValidateCatalogueStates` (each row's own facts canonical; a held row binds exactly its expression's articles, a
    textless one none; digest /2; the chain fields and the intervals recomputed from the rows and compared), each body's
    articles in distinct places of its document, then log /3 and the logical rows with the intervals.
  - `RecordsBuilds` is true for /8 and /9; `RecordsCatalogueStates` for /9 only. `ResolveObservations` gives `DerivedAt`.
  - Every state query not keyed by a digest takes `text_status='held'` on /9 (`HeldOnly`, and the `HeldWorksNine`,
    `StatesOfExpressionsNine` and `WorkRecordsNine` texts), #974's `ResolveWorksUnder` among them (the exact references'
    "held works" under a prefix); the as_observed fold keeps held states. New:
    `ResolveCatalogueStates(workKey)` and `ResolveIntervals(workKey)`, each a range of its table's primary key.
  - Provenance on an index 9 states digest /2 (`ProvenanceDerivationNine`), and says the row facts after the language are
    not carried in the answer yet, so a caller cannot recompute it from the answer alone (S7a serves them); /8 and /6 keep
    the /1 sentence.
- **Tests.**
  - `LuxembourgIndexCatalogueStatesTests` (new, 10 methods, 19 cases): a multi-body harness that states each body's member,
    record, inventory and content outcome and the catalogue's facts, and builds through the builder's projection and writer
    (`WriteIndex`, the code `TryBuild` runs after reading its envelope). The fixture of §3.4: a code (its base-act
    `isMemberOf`, an accented id `art_141_à_144`, a shared-file pair refused as misfiled, a PDF state, a catalogued state
    with no expression); an act dated by entry into force with a withheld consolidation and a hole; an act whose file
    applies it before its entry into force; an act and its same-date consolidation, then a state not acquired; an end
    before its start and an overlap; a suspected inclusive end; an English translation of an act whose French expression was
    not acquired; a work with no held state; an end on its start (F3). Its tests: every row by role (21 rows, 12 held, 9
    textless) with each textless reason; each work's transitions with their kinds and spans (14); the pre-/9 operations reading held rows only; the
    genesis log's sightings with their publisher facts and the observation's derivation time; a chained build emitting
    `interval_closed` and a `first_sighting` and no `validity_revised` for unchanged publisher ends; ten tampers refused
    (provisional, boundary, a derived end for a stated one, a translation's French state, an entry into force on a
    consolidation or off its role's date, a kind, a reason, an interval removed, a span moved); the work check and the
    catalogue language refusing; an undated state getting no row; each article's place in its document (the
    publisher's order, not the ids', and two articles of one body in one place refused); and state digest /2 and its rule
    profile pinned to a Python recomputation (`1c1c403a…` for the code's first state; `943c77af…`).
  - `LuxembourgIndexBuilderTests`: the schema at /9; the fixed-input index (one held act, a textless consolidation, an
    interval, a derivation time) re-pinned to `7cfd9988…`; the state tampers recomputed under digest /2; `Schema8Of`, a
    test-side rewrite of a built index 9 into schema 8's tables (held rows, digest /1, log /2), and
    `ASchema8IndexIsStillReadExactlyAsSchema8CheckedIt` over the multi-body fixture's index 9, every byte synthetic (seven
    tampers refused with schema 8's own reasons, and the rewrite opens as schema 8 with no derivation time, its held states
    only and none of index 9's fields); the log /2 unit tests kept on log /2's projection, which reading an index 8 still
    runs.
  - `LuxembourgIndexQueryPlanTests`: the three held-row variants planned on the index as built, and the two work ranges
    (`CatalogueStatesOfWork`, `IntervalsOfWork`) held to a primary-key search, with no statistics and with statistics shaped
    like a real index; the reader is held to running exactly those texts; and schema 8's own texts of the three state
    queries planned on the committed schema-8 index, both ways (F1).
  - `V3CorpusLegacySchema8MountTests` (F1, F2): the committed schema-8 mount read with schema 8's checks (state digest /1
    `084f8088…`, log /2, the logical rows), never a predecessor; every recorded request over it and over the schema-6
    mount answered as the code without index 9 answers it, byte for byte: recorded at `5a659831`, the integration head
    this branch is based on, over the committed files (`LegacyMountAnswers` is the same code at both commits; 5a659831's
    answers are 3b7913ec's but for the registry digest and #978's evidence bundle).
  - `V3IndexNineServingTests` (F4): a synthetic index-9 mount refused when built and served as a fixture; a built schema-8
    mount served; the switch off.
  - `V3CorpusLegacySchema6MountTests`: schema 6 made from schema 8 made from the built index 9; the real bounded first
    mount's schema-6 files still open and serve as before.
  - `LuxembourgRederivationTests`: `lu-release-1` names three rules; inputs naming the two rules from before S6, or an
    altered state rule, refuse; the record names the three profiles.
  - `V3CorpusProvenanceMountTests`: digest /2 recomputed from the table's row facts; the /9 sentence pinned verbatim.
    `V3ReplayGuaranteesTests` (G5): the evidence bundle and the publisher file still give each article's text, the body,
    text and identities digests; state digest /2 is recomputed from the bundle's values and the row facts after the
    language, which the test reads from the mounted index until S7a carries them in the answer. The mount fixture's state edits (`AddStateAsync`, the German state, the profile edit) go through a
    shared `RewriteStates`, which completes each row as the builder would and recomputes the chain.
  - Census: the registries, the new guarded type (`CatalogueFacts`) and `TryReadLog`; candidates 272, guarded types 143.
  - The answer, envelope and refusal samples re-rendered (state digests /2, `log.derived_at`, the derivation sentence); the
    site paths table follows the fixture state's new digest.
- **Measured: index 9's projection over run 15, Release 1's rules** (`LuxembourgIndexNineRun15Probe`, opt-in with
  `LEX_LU_INDEX9_PROBE=1`, `LEX_LU_INDEX9_PROBE_ROOT` (S3a's `run15-held.jsonl` and `csharp_content2.jsonl`, and S2's
  per-body /4 output, or the reference implementation's `ref_v4.jsonl`, which S2 matched on all 11,205 bodies: this run read
  it) and `LEX_LU_INDEX9_PROBE_FACTS` (derive-c's Luxembourg index, read-only, whose `work_facts` are run
  15's typed assertions); no derive, no custody read, no network; never a CI test). The production projection runs over
  bodies made from those outputs; a body is acquired when its rights are `agreed_same_run_cc_by`. An independent Python
  reference written from the rule profile's text (`ref9.py`, in the scratch folder) gives the same counts, one boundary
  apart: the probe counts the translation's, which the reference leaves out of the chains.
  - Held rows 2,543: 2,417 acts (one of them the English translation `loi/2017/07/20/a674/jo/en`, whose French expression
    was not acquired) and 126 code states; no consolidation, as Release 1 changes no rights. 2,425 works have a held row; 7
    of them (the codes but instruction criminelle) have two or more French held states. 131,106 admitted articles.
  - The 8 codes hold 126 states (civil 16, pénal 31, procédure pénale 30, procédure civile 16, consommation 12, travail 14,
    commerce 6, instruction criminelle 1): the design's figures. With their 64 textless rows they are the design's 190
    catalogue states, and their chains have the design's shape: 152 ends meet the next start, 25 holes and one end on its
    start (`code/procedure_penale/20211214`, kind `invalid`; the design counts it among 26 holes), 4 overlaps.
  - Textless rows 622: 557 consolidations of served acts, 64 code states and the French row of the English-held act (the
    design's 620: 556 and 64). The 557 are 557 distinct states (no state has two textless rows); the one more than the
    design's comes with the 75 more served works the measured /4 and /2 outputs give (2,425 against the design's 2,350),
    whose per-work list the design does not give, so it cannot be named from the design's text. By status: 609 `withheld_rights`, 12 `not_acquired` (11 naming no expression, among them
    `code/procedure_penale/20270301`), 1 `not_available_misfiled`. The probe's inputs hold no record of a body not held, so
    its `not_acquired` reasons name none; the build names the record's member outcome and body axis.
  - Roles of the held rows: 2,166 `applicability_in_file`, 251 `entry_into_force`, 126 `applicability`; 7 held acts are dated
    by a file date that is not their entry into force.
  - Every row: boundaries `derived` 2,631, `half_open` 532, `invalid` 2 (no suspected inclusive end among Release 1's served
    works); transitions `contiguous` 459, `derived` 232, `hole` 33, `overlap` 7, `invalid` 1, `same_date` 20; 2 rows
    `provisional` (dated after 2026-10-12).
  - Against the design: its 2,350 works and 2,469 texts came from its scripts' estimate of content /2; the measured /4 and
    /2 outputs admit 74 more acts (2,543 texts, 2,425 works). Everything else above is the design's figure or explained.
  - Articles missing from held states (#978's French review found Art. 2 of the loi du 16 mars 2020 missing; derive-c,
    under inventory /3 and content /1: 1,004 of its 1,730 states): under Release 1's rules 1,365 of the 2,543 held rows
    (1,255 acts, 110 code states) hold fewer articles than their document's main body, 118,355 of those documents'
    126,059. Every one of the 7,704 missing is content /2's `unsupported_content_shape`: `embeddedStructure` 3,126,
    `embeddedText` 1,839, a non-empty note reference 1,288, `eop` 596, `u` 484, `table` 452, `docNumber` 168, `span`
    137 (Art. 2 of `loi/2020/03/16/a195` among them), and smaller shapes. The index serves what content admits; which
    shapes are admitted is the content rules' (a later content version), and with `document_position` and the member's
    article outcomes the reader can say a state lacks an article and where (S7a).
  - 9.7 s, peak working set 465 MiB. The summary is written to the probe root (`index9-probe.summary.json`). No derive ran;
    the dry run measures index 9 over the real population.
- **derive-c's 209 expressions with articles and no state** (977 articles; the 1798 "Loi sur le timbre" among them, slice
  4.2's finding): all 209 are acts whose file states no date (`in_date` null under /4 too) and whose catalogue states exactly
  one `dateEntryInForce`: the set the design's act fallback dates. Under Release 1's rules index 9 gives 207 of them a held
  state dated `entry_into_force`. The other two, `loi/1948/05/15/n1/jo` and `n2/jo`, hold no text under inventory /4 (their
  only article is quoted from another instrument: S2's R2) and no other state of their works is held, so their works are
  not served and they get no row; they leave the index by S2's rule, not by this one.
- **derive-c's five acts not dated by their entry into force** (the coordinator's note on #975): `loi-2011-11-12-n1`
  (2011-11-20; entry into force 2011-11-21), `loi-2019-03-05-a129` (2019-01-01; 2019-03-12), `rgd-2015-12-18-n10`
  (2015-12-28, its publication date; 2014-06-16), `rgd-2018-06-20-a507` (2018-06-26; 2018-01-01), `rgd-2019-09-12-a609`
  (2018-10-01; 2018-01-01). All 1,730 of derive-c's dates are the files' own `dateApplicability` (inventory /4's in-file
  date agrees on every one), which equals the catalogue's entry into force on 1,725. Index 9 dates the five by the file
  with `date_role` `applicability_in_file` and keeps the catalogue's entry into force beside it in `entry_into_force`; over
  Release 1 seven held acts carry a date that is not their entry into force. The reader can tell the roles apart; S7a and
  S7c word them (R21).
- **Driver decisions (reversible):**
  - **One textless row per French catalogue expression**, not per legal resource; a catalogue state that names no
    expression gets one row with an empty expression and language (`not_acquired`, "the catalogue names no expression of
    this state"), in its work's French chain and never a translation. Release 1 has 11 such rows in served works, among them
    `code/procedure_penale/20270301` (the design's "catalogued but not held" state).
  - **A translation gets a row only when held**: textless rows are made for French expressions; a held translation takes
    its French state's date, role, end and boundary, and names its French expression (`translation_of`, the expression IRI
    rather than its digest, so its own digest does not move when the French row's text status does). Release 1 holds one
    (`loi/2017/07/20/a674`, an English file whose French expression was not acquired: a French textless row stands for it).
  - **`entry_into_force` beside the date** (beyond the design, from the coordinator's note on #975): an act's catalogue
    `dateEntryInForce` is kept on its row whatever its date's role, and is in digest /2, so the file's applicability and
    the entry into force are two values (R21; S7a and S7c word them). `date_role` names the source.
  - **An act's file date is read from its own inventory evidence whatever its rights** (a withheld act's file still dates
    its textless row): a date, never text.
  - **No end for an act**: `dateNoLongerInForce` is the act's force, not an applicability end, and is not read here.
  - **Digest /2 leaves out the derived end, the boundary and `provisional`**: each moves with the work's other rows or the
    build's time, never with the state, so a chained build that adds a state leaves an unchanged state's digest and the log
    raises no false `PredecessorDerivationDiffers`. The reader recomputes all three from the rows at open.
  - **A chained build refuses when the catalogue moved a state's own facts** (its end, publication date or text status)
    with the same source bodies: no event of log /3 records that change; before go-live each release starts a genesis log
    (design §2.3), and the derivation-change and metadata event path comes after.
  - **Text status of a body the scope quarantined**: the corpus record carries the body axis (`typed_quarantine`), not
    the scope's reason, so such a state reads `not_acquired` with "member unavailable, body typed_quarantine" even when the
    catalogue's licence was the reason; S7a or S5 may give it `withheld_rights` from the catalogue's licence facts.
  - **The work check refuses a contradiction only**: an act or consolidation the catalogue names no membership for is
    keyed by its IRI's work (none in run 15: all 11,016 and 189 check, design §3.1).
  - **Until S7a, the reader's operations from before index 9 read held rows only** (`text_status='held'` on every state
    query not keyed by a digest, and the as_observed fold): the minimal reader change that opens /9 without serving a
    textless row as a state. `ResolveCatalogueStates` and `ResolveIntervals` read all rows.
  - **Provenance on index 9 states digest /2 and does not yet carry its row facts.** Carrying them (`digest_inputs`) is a
    leaf the web's provenance page must render and its preview teach, in French and English: S7a serves the state rows'
    new fields and S7c renders them. Until then the sentence says a caller cannot recompute the digest from the answer
    alone; the mount's reader still recomputes it at open, and no index 9 is served before Release 1.
  - **The genesis log sights every row**, textless ones included; the events answer serves them as the log holds them
    (their permalinks refuse until S7a serves textless rows), and the scope sentence says "one first_sighting per state row
    (on lex-v3-luxembourg-index/9 a row may be a state whose text is not held)".
  - **The multi-body harness builds through the projection and the writer, not through an envelope**: the shared
    acquisition fixture serves one body per acquisition, and a many-body SPARQL fixture is a slice of its own. The harness
    states each body's member, record, inventory and content outcome and the catalogue's facts; `WriteIndex` is the code
    `TryBuild` runs after its rows are read.
  - **`document_position` in index 9 rather than later**: the order is a fact the build already holds (the inventory),
    and carrying it now spares S7a an index 10; it is outside state digest /2, so it moves no digest.
  - Titles are keyed by the work key of the held expression's legal resource (a code's titles are one work); a title of
    an expression whose legal resource spells no state keeps index 8's key.
- **Not yet.** S7a turns `V3CorpusMount.ServesIndexNine` with its reader rules, and updates the two tests that fail then
  (G5 from the answer alone among them). S7a: a state's articles in their document's order (`document_position`, replacing #978's natural-order
  stopgap) and the articles a state lacks, said as such; the interval selection for `as_of`, `in_force_on`, `as_observed`, `evidence_bundle` and `diff`, the
  refusals for textless states, unclear ends and invalid intervals, `provisional`, translations' labels, the state rows'
  new fields (and with them a provenance chain a caller can recompute again: G5 from the answer alone), and the
  undated-search fold; S7b: timelines'
  gaps and speech roles; S7c: the web, including the coverage row "history gives when each build ran" and the events page
  for textless rows; S8: gates and journeys over D1; S4b; the dry run, which measures index 9 over the real population.

## R1 for the profiles: inventory /4 and content /2 re-derived from an acquisition's own custody (Claude, 2026-10-15)

- **What.** Slice S4 of the Luxembourg data design (`C:\lex-v3\LU-DATA-DESIGN-2026-10-14-v2.md` §2.2 to §2.4), Release 1's
  R1 only: inventory /4 (S2) and content /2 (S3a) run over R0's own population, rights unchanged. Nothing served changes
  yet: no mount has been derived with it; the dry run over run 15 is the next machine step, waiting on disk.
- **R1.** `LuxembourgRederivation.ApplyAsync` takes R0's verified result (delivered, catalogued, under inventory /3 and
  content /1, its derivations over its own held-body population) and a request (rule set, rule digests, policy,
  `derived_at`, the sealed record or none).
  - It runs `LuxembourgAknArticleInventoryProducerV4.RunAsync(run)` (the production door: R0's own population object,
    dated by R0's own catalogue) and `LuxembourgAknLegalContentProfile2Producer.RunAsync` over it.
  - It mints `LuxembourgFirstMountAcquisitionResult.Success` on R0's own run, profile and vocabulary evidence, then keeps
    R0's vocabulary checkpoint, catalog, resumption and scope-manifest references (`With*`), and the record and
    `derived_at` (`WithRederivation`, once). The envelope's identity checks hold because the run and its population are
    R0's objects: the end-to-end test asserts `AreSame` on the run, the population and the inventory's source.
  - R0's result is passed straight in and R1 drops its references to it, the frozen inventory and the frozen content once
    it has taken what the record names (the design's "drop R0's /3 and /1 once their digests match"; R0 matched them
    against its catalog when it reopened).
  - It holds no transport: its whole surface is a custody store, an acquisition result and a request. That is a property
    of its code, which no signature check proves; zero traffic is measured (the bullet "Zero wire, measured" below).
- **The record `lex-lu-rederivation/1`**, held in custody; its resource id is `ContentDerivedIdentity` over its bytes. It
  names no tool commit and no clock (a test re-derives at another `derived_at` and gets the same bytes and id).
  - `acquisition`: the catalog, the query checkpoint, the record set, the observed set, the final manifests, run
    identities and rights-bound run identities the records name (sorted arrays), and the scope manifest (null for an act
    range).
  - `verified_under`: the inventory /3 and content /1 rule digests and R0's two derivation identities (run 15's
    `854891e3…` and `abc0a299…` in the dry run).
  - `rules`: the rule set `lu-release-1`, its profiles by name and digest (inventory /4 `8a011d87…`, content /2
    `bf3fea5a…`), the admission policy, `rights_resolution: as_acquired`.
  - `outputs`: the /4 and /2 population identities. `counts`: held bodies, rights dispositions, inventory dispositions,
    content-consistency refusals, content dispositions, admitted articles. `publisher_session: none_on_this_path`: a
    statement of what the path is, not a count (a count belongs to a process; the offline tool measures its own).
  - The fixture record (one synthetic consolidation) is 2,045 bytes; its digest moves with each fixture acquisition,
    whose catalog names fresh URNs.
- **Wiring.**
  - `V3OfflineMount.DeriveAsync` reads inputs `lex-v3-offline-mount-inputs/3` (and /1 and /2 unchanged): the base inputs
    /1 or /2 by digest, the rule set and its digests, the policy, `derived_at`, `expected_rederivation`. It refuses an
    unimplemented rule or policy before reopening anything, then runs R0 and R1. The build time stays the base's.
  - The profile envelope carries the record (`WithLuxembourgRederivation`, refused when it names other populations), so
    the corpus and both index builds write one header.
  - `LexCorpus6Builder.ProfileIdentities` takes the content profile from the population, not the /1 constant, and adds
    the inventory's rule digest when it is not /3; a population under a later profile without a record cannot be built.
  - The corpus header gains `luxembourg_rederivation` and `luxembourg_rights_admission_policy`, written only when set, so a
    corpus from the acquisition's own derivations keeps its bytes; both or neither, the policy registered.
- **Tool steps** (`src/Lex.V3.Tool/RederiveCommands.cs`): `rederive-inputs --custody … --base <mount-inputs.json> --rules
  lu-release-1 --policy <digest> --derived-at <utc>` and `rederive-seal --custody … --inputs … --record <sha256>`, each
  holding the inputs in custody and writing a `mount-inputs-<sha>.json` pointer beside it, as `build` does; `derive`
  prints `luxembourg_rederivation=<sha>` for inputs /3.
- **Clock and texts.** The served genesis, silence, upstream-health and as-observed texts no longer say built_at is when
  the build ran: built_at is the time the acquiring build read once its acquisition ended, an upper bound on observation,
  which a derivation from custody keeps. The build report gains a `derivation` block: a statement naming the catalog,
  for a resumed acquisition its window and both spends ("the resumed run had spent N publisher request(s) when it
  catalogued, after at least M by the earlier run(s)": `WireSpent` is the resumed run's alone, the earlier runs' floor is
  `PreviousWireSpentAtLeast`, and neither is added to the other), the rules and policy, "at the derivation time its inputs
  state" (`derivedAt` is the inputs' stated time, never a clock read), and that the path holds no publisher session; then
  the rule set and rules, the policy, the record, the tool source and `publisherSession: none_on_this_path`.
- **Tamper checks.** R1 refuses an unimplemented rule set or digest, or a policy the set does not name; a held body the
  frozen inventory read that no longer reads as its digest; a record other than the sealed one; and R0 results that are
  refused, uncatalogued or already re-derived. No record is held for a refusal.
- **Zero wire, measured** (the review's F1). A proxy trap sees only clients that use the default proxy, and the code base's
  own session builds its handler with `UseProxy = false` (`RoutedHttpAcquisitionSession.CreatePinnedHandler`), so the
  trap could not see the production transport, and the old signature check could not see a method body.
  - `V3NetworkActivityListener` (Ingest) counts every socket connect (`System.Net.Sockets` `ConnectStart`), name resolution
    (`System.Net.NameResolution` `ResolutionStart`) and HTTP request (`System.Net.Http` `RequestStart`) the process
    starts, on any transport. A sentinel test proves it is not vacuous: a `UseProxy = false` client to a loopback stand-in
    counts a connect and a request, and a resolution of `localhost` counts.
  - In process, the end-to-end test runs `ApplyAsync`, and the inputs /3 test runs each `DeriveAsync`, inside the listener,
    and requires zero.
  - `V3OfflineProcess` is a process-wide guard (an `AppContext` switch): once entered, the session's one constructor,
    through which every session on every transport is built, refuses. The tool's offline commands (`derive`,
    `rederive-inputs`, `rederive-seal`) enter it before any work and run inside a listener; each prints
    `offline_guard=on; network measured: …; publisher_requests=<n>` with `n` the measured total, and refuses with exit 5
    when `n > 0` (a derive removes the directory it wrote; a rederive step removes its pointer). A test enters the guard
    and shows the test transport refused before it sends anything.
  - The separate-process tests now require the guard on and every count at zero in each command's own output. The older
    derive trap tests (`V3OfflineMountTests` and `EuFirstMountAcquisitionResumeTests`) shared the trap's blind spot; they
    read the same measured line now, through `RunOfflineCommandAsync`.
- **Tests** (`LuxembourgRederivationTests`, 14, and one in `V3OfflineMountTests`), over a synthetic Akoma Ntoso
  consolidation served by the shared Luxembourg fixture in place of its PDF (every byte written in the test; no publisher
  file and none of the withheld fixtures). Content /1 reads one of its two articles (the other carries index terms); R1
  admits both.
  - `V3FirstMountBuild` end to end over the re-derived population: the header names the record and policy /1, the profile
    identities name inventory /4, content /2 and their populations and not content /1, every EU member is byte-identical
    to R0's build, the Luxembourg index holds 2 articles under content /2 and `built_at` stays the acquisition's, the report
    states the derivation, and the API's own mount opens it.
  - Tamper: an altered body (a byte flipped in file custody), seven unimplemented rule or policy requests, and a sealed
    record mismatch each refuse, holding nothing; R1 over a re-derived or refused result refuses.
  - Inputs /3 in process over an act range (/1) and over a population scope (/2, the dry run's shape): derive, seal,
    derive again, the same files as an in-process build over R1; six malformed inputs /3 refuse before writing anything.
  - Through the tool, each step in its own process with every proxy at a local trap: `rederive-inputs`, `derive`,
    `rederive-seal`, `derive`, a wrong seal refused (exit 3), usage errors (exit 2); each command prints the guard on and a
    measured zero, and no connection reaches the trap.
  - A resumed acquisition (the legislative population replayed from a complete journal): R1 keeps R0's resumption, catalog
    and scope manifest, and the report's statement is pinned with both spends.
  - R0's pins hold: the frozen rule digests (`70e92611…`, `dd32d9a5…`) are asserted, `LuxembourgAknFrozenReplayTests`
    passes unchanged, and a corpus built from the acquisition's own derivations writes no new header field.
  - Censuses: one new guarded type (the record) and its doors, `WithRederivation`, `WithLuxembourgRederivation`,
    `ApplyAsync`, `ReopenLuxembourgAsync`; two new registries in Ingest (`LuxembourgRederivation`, `V3OfflineMount`, which
    leaves the declined list) and one in Contracts (`LuxembourgRightsAdmissionPolicies`). The answer samples move by the
    four reworded texts.
- **Driver decisions (reversible):**
  - **Release 1's policy digest** is policy /1's: the canonical text `lex-lu-rights-admission-policy/1` (admits exactly
    `agreed_same_run_cc_by`, no single-channel admission, no ruling), 246 bytes, sha256
    `a477f4f2e6e0468bc9069da742f6f34f248ec831e01277a40552cb1e5bad2abb`, recomputed in Python. It is today's admission (the
    builder admits `ChannelsAgreeOnAdmittingLicence` only). It sits in Contracts as `LuxembourgRightsAdmissionPolicies`
    with that one entry; S1 adds the registry's type rules and policy A. Rule set `lu-release-1` names this policy and no
    other.
  - **State digest /2 is deferred to S6.** No code defines it (it is index /9's state digest, §3.1), and neither the
    record nor `profile_identities` names a rule the build does not run. S6 adds it to `lu-release-1`, which changes the
    set's digests, so inputs written before S6 refuse under an S6 build: no rule is substituted.
  - **`derived_at` on the observations row is deferred to S6.** The observations table is exact-schema-checked (the
    reader accepts index 8 and 6 only), hashed into `log_stamp` and carried by chained builds, so a column means an index
    schema change; S6 changes the index to /9 anyway. Until then `derived_at` is in inputs /3, on the acquisition result
    and in the build report; the served texts say only what is true without it, and S6 adds `log.derived_at` and its
    sentence.
  - **Coverage's row "history gives when each build ran"** stays for S7c: the web holds it verbatim (the coverage
    preview, a web test, `COUNTS_PROVENANCE_NOTE` and the timeline preview), and no re-derived mount is served before
    Release 1.
  - The base may be inputs /1 or /2 (the design names /2); a base that is inputs /3 refuses. `derived_at` is a whole UTC
    second, not before the base's build time.
  - Inputs /3 and their seal have content-derived resource ids, so the same arguments give the same pointer. Sealed
    inputs are never sealed again; the unsealed inputs stay in custody.
  - A body check compares with R0: a body /4 cannot read by its digest refuses unless /3 could not read it either (run 15:
    none); any body content /2 cannot read refuses.
  - `V3OfflineMount` now holds two schema identifiers, so it moves from the census's declined list to the registry pin;
    `V3OfflineProcess` (one switch name) joins the declined list.
  - **`verified_under` names no in-file rights reading /1.** The design lists reading /1's digest among the rules R0
    verified under, but the reading has no rule profile and no digest in code (`LuxembourgInFileRightsReader.ReadAsync` is
    pinned by its tests and by R0's query replay, not by a digest). The record does not mint one; S1 gives reading /2 a
    profile and its digest, and S4r's record names it.
  - **`profile_identities` carries no policy digest.** The design lists the policy among the profile identities; the
    policy is named once, in the header's `luxembourg_rights_admission_policy`, beside the record it belongs to, and a
    corpus without a re-derivation names none (it reads as policy /1).
  - **The record and the report state the path, not a count** (`publisher_session`, `publisherSession`): both are written
    in whatever process derives, and a count is a measurement of one process. The tool's `publisher_requests=<n>` is the
    measurement.
- **Review of #973** (one round; two material findings, both reproduced by the reviewer, both repaired in the review
  commits; each mutation below was run in this worktree on the repaired code, then restored):
  - F1, zero wire not measured: the reviewer's M1 (a GET built as the session builds its handler, `UseProxy = false`, from
    inside `ApplyAsync`) passed every test. Now it fails 5 of 15: the end-to-end and resumed tests' listeners (socket
    connects 1, HTTP requests 1), both inputs /3 rows, and the tool round trip, where `derive` exits 5 with
    `publisher_requests=2`. Two guard mutations fail too: the constructor without its refusal fails
    `AnOfflineProcessBuildsNoPublisherSession`, and the tool not entering the guard fails the tool round trip (exit 5,
    `offline_guard=off`).
  - F2, a resumed acquisition's spend understated: run 15's report would have said 21,972 requests where the acquisition
    spent at least 25,092. The statement now names both spends apart. The reviewer's m3 (R1 dropping R0's resumption)
    passed every test; now it fails `AReDerivationOfAResumedAcquisitionKeepsItsResumptionAndStatesBothSpends` ("R1 keeps
    R0's resumption").
  - The review's notes: the report gives the derivation time as the one the inputs state; the two deviations from the
    design (`verified_under` without reading /1, `profile_identities` without the policy) are recorded as driver
    decisions above.
- **For the dry run.** R1 parses each held XML body twice more (/4, /2), after R0's two, and holds content /2's tokens for
  about 270,000 articles through the builds; the dry run measures that. What a re-derivation writes into the custody it
  reads was said here as "two small objects"; D1 at 7152de65 (2026-10-26) measured it: 1,291 new files, 58,163,753 bytes,
  none existing changed (corrected 2026-10-26, from the objects themselves, read-only):
  - its own objects: the unsealed and the sealed inputs /3 and the record `lex-lu-rederivation/1`, and the two pointer files
    `mount-inputs-506ffda3…` and `mount-inputs-067ec72d…` in the custody's root;
  - each derive's hold receipts, which every offline derive of run 15 writes, re-derivation or not: R0's replay holds the
    acquisition's chunked artefacts again (the two scope manifests and the corpus record set) through
    `ChunkedDerivedArtifact.WriteAsync`; their 4 MiB chunks are already held, but each chunk's receipt evidence
    (`lex-v3-durable-blob-write-receipt/2`, which carries the hold's `observed_at`) and so each root are new every run: 619
    receipts and 3 roots a derive (1,238 and 6 for derives A and B; the R0 derives of 2026-10-13 wrote 622 each). Neither
    the mount, the sealed inputs nor the record names them (the replay holds each artefact to its canonical digest); they
    are evidence of that run's holds, not needed to verify the mount, and grow custody by about 0.2 MB a derive;
  - 22 PDF layout artefacts (`LXPDFL1`, 55,959,023 bytes stored) and the 20 text-layer artefacts made from them
    (`LXPDTL1`, 1,729,565 bytes), which the build's PDF producers hold by design (content-addressed; the corpus's PDF
    outcomes name them through their identities). They are new because the layout is not read the same on Linux as on
    Windows. For each of the 22 sources, custody already held the artefact the acquisition's Windows build wrote on
    2026-10-12, with the same rule and source and other bytes. Source `cdbb6a99…`: 2,206,811 bytes both, 90 of 27,783
    glyphs with other bounding boxes (up to 7.6 points; the first glyphs, "Arial,Bold", 2.7 points wide on Windows and 14.1
    on Linux). PdfPig 0.1.11 reads a font the PDF does not embed from the host's installed fonts, so those glyphs' metrics
    are the machine's. Derives A and B (both Linux) agree; a Windows derive of the same custody names other PDF outcomes
    and builds another corpus. **Known defect, not fixed here**: the PDF layout depends on the host's fonts. **Driver
    decision (reversible)**: D1's artefacts stand, and the population is derived and released on Linux only (as D1 and the
    release image are) until the layout reads no host font.
- **Not yet.** Release 2's R1 (S4r: reading /2, the re-resolution, the licence-page index, `measure-lu-rights`); state digest
  /2 and `log.derived_at` (S6); the coverage row and its web copies (S7c); S4b; the dry run.

## Content /2: a Luxembourg article's index terms are metadata, never wording (Claude, 2026-10-15)

- **What.** Slice S3a of the Luxembourg data design (`C:\lex-v3\LU-DATA-DESIGN-2026-10-14-v2.md` §1.4). Under content
  /1 an article's index terms (`scl:indexterms`, a direct child of the article) are a node outside the AKN namespace, so
  the article is a typed gap: 126,397 of the 154,136 main-body code articles /4 reads in run 15 carry them (the design's
  figures exactly). `LuxembourgAknLegalContentProfile2Producer` sits beside the frozen /1 producer, changes that one
  thing and reuses /1's output types. Nothing served changes yet: no derive calls /2 until S4 (R1).
- **How.**
  - The block is skipped as `scl:JOLUXWork` is, and its terms are kept on `LuxembourgAknLegalContentArticle.IndexTerms`
    (a tree of labels, verbatim, in document order), never as tokens. So they never reach `tokens_json`, the wording
    digest, `searchable_text`, a quote or a bundle, which all read the tokens.
  - The shape is exact, and is the shape of every one of the 582,197 terms (397,179 at the top level, nested up to four
    levels) in the main-body articles of the 10,659 run-15 bodies /4 reads: `scl:indexterm` elements only; each one
    `scl:label` of character data, then at most one nested block; no attribute; only white space between elements. Any
    other shape is a typed gap for that article (`publisher index terms are not the reviewed shape: …`).
  - Every other shape is /1's: the containers, the token kinds, every typed gap and its detail. The tokenizer is copied
    from /1 so that /1's class stays byte-identical.
  - It reads only an inventory /4 population (any other is refused), tests the disposition first so a body /4 refused
    as `publisher_file_names_other_state` is never tokenized, and re-reads each body through /4's own parse (`TryLoad`,
    now internal, so the custody-bound ceiling is /4's), `TryDocument` and `MainBodyArticles` (R10 of S2).
  - Profile `lex-v3-luxembourg-akn-legal-content-profile/2`, 13 lines, 1,191 bytes, sha256
    `bf3fea5a5229d1a9f035d8d56816bd054abee30591370dfc6fe62f154dfaef2d`, recomputed in Python. It names inventory /4's
    digest (`8a011d87…`), so a change to the inventory it reads changes its digest too.
  - The shared types, as S2 did for /4: the article gains `IndexTerms` (null under /1) and identity layout `/2` (token
    count, tokens, term count, terms); the population gains `RuleProfileSha256`, an optional versioned digest and
    identity layout `/2` (the digest, the source inventory population's identity, then /1's per-outcome fields). A
    versioned population refuses an article without terms or under another digest, and an inventory /3 source; an
    unversioned one refuses an article with terms.
- **The frozen verifiers stay frozen.** The /1 class is byte-identical to `c05a54eb` (compared as text from its summary
  to the end of the file) and its digest stays `dd32d9a5…`; the shared types' /1 branches hash as before and their new
  checks cannot fire on /1. `LuxembourgAknFrozenReplayTests` passes unchanged. Only R0 in the dry run re-verifies the
  checkpoint's `abc0a299…`.
- **The code-article test.** A byte-exact excerpt
  (`Fixtures/LuAknLegalContent2/code-procedure-penale-20251219-index-terms.bin`, 14,942 bytes, sha256 `9448fcc2…`) of
  `code/procedure_penale/20251219` (body `bddb7a57…`, 2,242,711 bytes, fetched as `xml`, `agreed_same_run_cc_by`,
  acquired today), cut as S2 cut its excerpts (16 whole element ranges removed; every byte kept is the publisher's, in
  order). `art_56`'s terms nest four levels and are kept; its tokens equal what /1 reads from the same bytes with the
  block taken out, and its /2 article identity equals an independent Python computation;
  `art_102` (terms and a `span`) stays /1's `span` gap; `art_283` (no terms) reads as under /1. Every other shape is
  synthetic. A legal-care guard holds the folder to that one file. No test uses the withheld fixtures (R4 of S2).
- **Measured: /2 over run 15** (`LuxembourgAknContent2Run15Probe`, opt-in with `LEX_LU_CONTENT2_PROBE=1` and
  `LEX_LU_CONTENT2_PROBE_ROOT`, over S2's probe inputs; custody read by digest and never written; no network; no R0;
  never a CI test). Inventory /4 over all 11,205 held inputs, then /2 and, as a measuring device only, /1 over /4's
  10,644 inventoried bodies (308,521 main-body articles), in chunks of 32 bodies.
  - /2 admits 270,552 of the 308,521 articles and leaves 37,969 typed gaps. Codes: 146,008 of 149,542 (97.6%; /1's rules
    admit 25,760, 17.2%, the design's "17% to 97.7%"); consolidations 75,870 of 90,930; acts 48,674 of 68,049.
  - **Beyond /1: 121,053 articles**, all through the index terms: 121,047 admitted with at least one kept term (codes
    120,248, consolidations 799) and 6 whose block is the publisher's empty template (consolidations 4, acts 2).
  - By admission class: codes today 98,604, policy A 1,179, L2 20,465; consolidations: the 799 with terms are all in
    bodies admitted under no ruling ("never"), and the 4 empty templates are 2 policy A and 2 L2; acts: 1 today
    (`loi/2021/02/05/a121/jo` `art_11`) and 1 L1. The design's figure of 360 consolidation articles is not reproduced:
    over /4's articles the count is 803, and none of the 799 with terms is an admission candidate.
  - Against /1 over the same articles, wherever /1's own selection agrees with /4's (10,304 bodies): 0 disagreements.
    Every article /1 admits reads under /2 to the same disposition and tokens with no term, and every /1 gap other
    than the namespace gap is /2's gap with the same detail. Of the /1 namespace gaps in those bodies, /2 admits 97,578
    (codes 96,773, consolidations 803, acts 2) and 962 stay gaps on another shape.
  - 128 s for /4, 94 s for /2 and /1, sequential, peak working set 1.85 GB; a re-run on the final code gave the same
    summary (117 s, 69 s, 2.01 GB). The per-article output (`csharp_content2.jsonl`, 77.7 MB) stays in the scratch
    probe root. The probe reads through S2's read-only custody decorator, now internal, so the custody conformance
    sweep still counts 33 implementations.
- **Driver decisions (reversible):**
  - The publisher's empty template (an `scl:indexterm` whose label is white space only and keeps no nested term) keeps
    nothing rather than making the article a gap: 8 articles in run 15, 6 of which /2 then admits (the other 2 stay gaps
    on another shape); one is in a body admitted today (`loi/2021/02/05/a121/jo`, `art_11`).
  - Labels are kept verbatim: no trimming (164 run-15 labels have white space at an end) and no normalization.
  - Any other index-term shape is a typed gap: the terms could not be kept as the publisher wrote them, and no run-15
    term has another shape.
  - The index terms are in the article identity (layout `/2`) and not in the wording: two articles with one wording and
    different terms have one wording digest and two identities.
  - The /2 population identity binds its source inventory population's identity; the outcome identity is /1's layout,
    so a typed gap's outcome identity does not name the content rule, which the population identity does.
  - The name is `LuxembourgAknLegalContentProfile2Producer`, not `…ProducerV2` after `…ProducerV4`:
    `RetiredGenerationBoundaryTests` refuses any compiled identifier or source path in the V3 line that contains "v2"
    (the retired generation). The tests, the fixture folder (`LuAknLegalContent2`) and the probe's variables follow.
  - The placement and number of an article's direct-child `scl:indexterms` blocks are not constrained (a review
    observation): two blocks are read into one list, in document order; a block anywhere among the article's direct
    children is skipped; an empty or white-space-only block is admitted with no terms. The review measured exactly one
    block per run-15 article, always immediately before `akn:num`, so none of this changes a run-15 result, and wherever
    a block sits its terms never become wording.
  - A label made only of U+00A0 (no-break space) is white space to .NET, so it is dropped as the empty template (a
    review observation). No run-15 label is one.
- **Review** (one round; one material finding, reproduced by the reviewer, repaired in `6919a58b`; the two observations
  above are recorded as driver decisions):
  - The index-term guard's names could be loosened with all 38 /2 tests green: the article's block to any `scl`
    element, and the term, the label and the nested block to their local names. That admits five typed-gap shapes.
    Three rows of `AnyOtherIndexTermShapeIsATypedGapForThatArticleOnly` (an `indexterm`, a `label` and a nested
    `indexterms` in another namespace) and the new `AnyOtherDirectChildStaysContent1sGapForThatArticleOnly` (an
    `scl:index` wrapping terms of the reviewed shape, an empty `scl:index`, an AKN `indexterms`, each /1's gap with /1's
    detail under /1 and /2) hold them, each with its exact detail.
  - The door's /4-only check could be replaced by `if (false)` with every /2 test green, because the population also
    refuses a /3 source. `TheDoorAloneRefusesAPopulationUnderAnyOtherInventoryDigest` holds the door's parameter name
    and detail for a /3 population, and a population under a third inventory digest, which only the door refuses.
  - The proof, in a scratch worktree at `6919a58b`: the reviewer's mutant fails 5 of the 45 /2 tests; each of its four
    lines alone fails its row (line 253 the two `scl:index` rows); line 253 by local name only fails the AKN row;
    `if (false)` fails the door test, and so does its third-digest case alone; restored, 45 of 45. /2 tests 38 → 45.
- **Not yet.** S4 (R1 runs `new LuxembourgAknLegalContentProfile2Producer(store).RunAsync(inventory4, ct)` over the /4
  population and records the /2 digest and population identity); S6 (index /9 stores `IndexTerms`; no reader or page
  shows them yet); the dry run (watches the 175,837,349-byte body, which /2 reads like /4, and the memory of one whole /2
  population, about 270,000 admitted articles' tokens at once, which the probe never held: it read in chunks of 32
  bodies; S4b answers it if the dry run names it).

## Inventory /4: a Luxembourg file's own identification is evidence, and six checks refuse a file that is another state's (Claude, 2026-10-15)

- **What.** Slice S2 of the Luxembourg data design (`C:\lex-v3\LU-DATA-DESIGN-2026-10-14-v2.md` §1.3), built to its
  implementation brief, which reconciled the design with measurements over run 15. /3 rejected every code and 1,473
  consolidations over identity checks, and any non-ASCII id rejected a whole document; /4 accepts valid Legilux files and
  still refuses one that is another state's. `LuxembourgAknArticleInventoryProducerV4` sits beside the frozen /3 producer
  and reuses its outcome types. Nothing served changes yet: no derive calls /4 until S4 (R1).
- **How.**
  - It reads only the document's own identification block (`<document>/meta/identification`) and never rejects a file
    over it. The block becomes evidence: the states its manifestations name, one of seven identity classes, its legal
    resource and its in-file date. The state coordinate is never minted from the file (`Publisher*` stays null); S6 dates
    states from the catalogue.
  - Only the main body's top-level articles count. Annex, component and quoted articles are counted and never read. An id
    or wId is a publisher token: 1 to 512 UTF-16 units, any scalar but Unicode White_Space, controls, the zero-width set
    and the bidi controls, never normalized (1,658 non-ASCII ids such as `art_141_à_144` are admitted).
  - Six content-consistency checks (shared file, later state, other work, date ahead, copied state, copied act) run in a
    second pass over what the first read, so no body is read twice. Every check that fires is recorded; any of them gives
    the new disposition `publisher_file_names_other_state`, which keeps the inventory as evidence.
  - Profile `lex-v3-luxembourg-akn-article-inventory-profile/4`, sha256 `8a011d87…`, 2,050 bytes (re-pinned after the
    review from the brief's `40d79090…`, 2,019 bytes, because check (c)'s title designation now names its own ends; see
    the driver decisions). The title grammar of check (c) is one constant, behind both the profile line and the matcher.
  - The production door takes the run and dates states from the run's own `dateApplicability` facts; no caller supplies
    a catalogue. A test holds that projection through the door. Over run 15 the probe builds the same catalogue (3,752
    consolidation and code states) from the same facts with `CatalogueStarts`, without the door, so S4's R1 is the door's
    first run over real data.
- **The frozen verifiers stay frozen.** The /3 class and its profile are byte-identical, content /1 is not edited, and
  their digests stay `70e92611…` and `dd32d9a5…`. The checkpoint and its reopen are untouched, and the reopen still
  recomputes /3 and /1 (`854891e3…`, `abc0a299…`), but through shared constructors this slice changed (inventory,
  outcome and population): their /3 branches hash byte for byte as before, as the frozen replay and the a439 inventory and
  population pins show, and their new validations cannot fire on /3 outcomes. Only R0 in the dry run re-verifies the two
  checkpoint digests. `LuxembourgAknFrozenReplayTests` pins both rule digests and replays /3 and /1 over 19 admitted
  run-15 bodies at their real ordinals. The content disposition multisets, the admitted article identities and the 7
  state rows equal what derive-c sealed per body (R0-verified). The /3 replay fields (disposition, detail, coordinate,
  article order, inventory identity) and the sample's population identities (/3 `914f9f49…`, /1 `89704509…`) equal an
  independent Python replay of the frozen rules; derive-c sealed none of them. The frozen code reproduced both kinds in
  the slice's first commit, before any production edit. The sample holds the four /3 rejection kinds that admitted bodies
  small enough to commit show; /3's `XmlException` at its 64M ceiling and its wId and applicability readers are held by
  synthetic tests instead (none of derive-c's 8,854 sealed articles has a wId or an applicability date).
- **Measured: C# /4 over all of run 15** (`LuxembourgAknInventoryV4Run15Probe`, opt-in with `LEX_LU_INVENTORY_V4_PROBE=1`
  and `LEX_LU_INVENTORY_V4_PROBE_ROOT`; custody read by digest and never written; no network; no R0).
  - All 11,205 held inputs: inventoried 10,644 (acts 7,912, consolidations 2,575, codes 157);
    `publisher_file_names_other_state` 15 (consolidations 13, codes 2); `xml_rejected` 178 (177 "no main-body article",
    1 `XmlException`); `not_akn` 368; `retained_bytes_unavailable` 0.
  - Main-body articles in inventoried bodies: 308,521 (codes 149,542, consolidations 90,930, acts 68,049). Excluded
    articles 10,437 in 339 bodies. In-file date present in 5,121 bodies.
  - Identity classes over the 10,659 bodies that read as inventories: own 6,582, template_empty 1,953, same_work_act
    1,754, same_work_other_state 233, tc_or_trep 129, other_work 8, absent 0.
  - Checks, as bodies fired: shared_file 10, later_state 1, other_work 2, date_ahead 2, copied_state 4, copied_act 1.
  - By admission class (inventoried / refused / rejected / not AKN): today 2,721 / 1 / 3 / 0, with 139,640 articles
    (codes 120,111, acts 19,529; /3 inventoried 2,102 of these bodies); policy A 702 / 1 / 0 / 0; L2 1,050 / 5 / 0 / 0;
    L1 2,195 / 0 / 1 / 0; L1b 31 / 0 / 0 / 0; never 3,945 / 8 / 174 / 368. So 126 of the 127 acquired code states
    inventory, 32 withheld code states inventory, and 2,588 of the 2,591 XML consolidations read as inventories.
  - The 7 refused admission candidates: `code/civil/20190428` (a); `code/civil/20181101` (a, b, d and e, all naming
    20190428); `rgd/1991/01/24/n2/consolide/20230710` (d, naming `/20250413`); `rgd/2006/02/03/n3/consolide/20221227`
    (e, `/20220201`); `loi/2018/08/01/a681/consolide/20241215` (e, `/20190312`); `loi/2005/12/09/n1/consolide/20180904`
    (e, `/20180901`); `loi/1802/04/08/n1/consolide/20180501` (f, its `/jo`). The other 8 refusals are 4 shared XML pairs,
    none admissible; two of them also fire (c) (`rgd/2014/08/01/n9`, `rgd/2012/03/16/n14`).
  - Against the reference implementation's per-body results: 0 mismatches over 11,205 bodies (disposition, class, named
    states, legal resource, in-file date, article and excluded counts, findings, rejection details); the wording digest
    equal on all 10,659 inventoried bodies; every aggregate above equal to the brief's acceptance counts.
  - 401 s for /4 and 177 s to re-read the wording, sequential, peak working set 1.58 GB (156 s and 67 s on the re-run
    after the review). The /4 population identity over run 15 as the probe builds it is `2c09daa6…` under profile
    `8a011d87…` (it was `9c9d307e…` under the brief's `40d79090…`: the rule digest is in every inventory identity); S4's R1
    should reproduce it from the same custody and catalogue. The re-run's per-body output (`csharp_v4.jsonl`) is byte
    for byte the one before the review: the title boundary changes no run-15 body.
- **Driver decisions (reversible), from the brief's reconciliation with run 15:**
  - Checks (e) and (f) compare the articles plus the annexes, not the articles alone: 22 of the design's 31 (e) and (f)
    states differ from the state they name only in their annexes, by 7 to 9,010 words, so they are distinct publications.
  - Check (d) has no heading guard: machine fields decide, and text parsing stays limited to (c). Its cost is one policy-A
    state, `rgd/1991/01/24/n2/consolide/20230710`, whose file declares 2025-04-13.
  - Check (c) uses the leftmost title grammar with qualifiers; the start-of-title grammar misfired on 8 of 10 files.
  - A code that names another work always refuses (a code's title has no dated designation). No run-15 code names another
    work, so this rule and the reference's code exemption agree on every body.
  - Articles inside a quotation outside any article are another instrument's (R2 below).
  - Every fired check is recorded, not only the first (`code/civil/20181101` fires a, b, d and e).
  - Seven identity-class tokens: an earlier consolidation and another code date are both `same_work_other_state`.
  - The forbidden id set adds U+061C, U+200E and U+200F to the design's (bidi controls; no run-15 effect).
  - The CI replay compares the outputs derive-c sealed per body, and an independent replay's /3 fields and sample pins;
    population digests hash every member, so no sample reproduces the checkpoint's.
  - Result: 7 refusals among admission candidates, not the design's 34.
  - **The document ceiling (this slice's own decision).** /4 reads up to the custody object bound
    (`CustodyBounds.MaxObjectBytes`, 268,435,456 characters) where /3 stops at 64M characters. Two run-15 bodies hold an
    image in one attribute and exceed 64M characters: `rgd/2021/06/09/a444/jo` (175,837,349 bytes, admitted today,
    13,034 characters of text, 7 articles) and `rgd/2023/11/10/a762/jo` (110,151,932 bytes, L1, 8 articles). /3 rejects
    both as `XmlException`; the reference reads them, and the counts above include them. With /3's ceiling they stay
    rejected: inventoried 10,642, rejected 180, today 2,720 / 1 / 4 / 0, L1 2,194 / 0 / 2 / 0. S3a must read them with the
    same ceiling or their 15 articles stay typed gaps; the dry run watches the transient memory of the 175 MB body.
    Profile /4 does not name the ceiling (/3's does not name its own either), so the /4 digest does not cover it:
    `ADocumentAboveThreesCeilingIsReadUpToTheCustodyBound` holds it, and S3a must reuse
    `LuxembourgAknArticleInventoryProducerV4.MaximumXmlCharacters`, not a copy of its value.
  - **Check (c)'s title designation names its own ends (this slice's own decision, after the review).** The brief's
    pattern ended in `\b`, so C# and the reference each used their own engine's word characters, and five synthetic
    titles got different answers (a superscript or a fraction after the year or before the type; a combining mark or a
    ZWJ after the year). The ends are now `(?<![\p{L}\p{N}_])` and `(?![\p{L}\p{N}_])`, the reference's word set over the
    Basic Multilingual Plane (R9: the C# side is fixed, never the reference). Profile /4 moves from the brief's
    `40d79090…` to `8a011d87…`; no run-15 body changes (the probe below).
- **Risks R1 to R12, with the decision taken:**
  - R1. Refusals fall from 34 to 7, and the P0 question's "30 are then refused as misfiled" under L2 becomes 5. Corrected
    for the owner in the pull request; public wording describes the evidence and never says "misfiled".
  - R2. Two acts served today become textless, `loi/1948/05/15/n1/jo` and `n2/jo`, one article each: their only article
    is quoted from another instrument (for n1, the Constitution's Article 52), which /3 served as the law's own, losing
    the amending sentence. Adopted; disclosed in the pull request and the release notes.
  - R3. Index /8 over /4 has no dated Luxembourg state until S6, because `Publisher*` is null: S4's end-to-end test and the
    dry run check members, articles and resources; dated-state counts are S6's acceptance.
  - R4. Withheld bytes already in the repository (`LuAknLegalContent/loi-1991-08-10-n3--2024-02-01--fr.bin`,
    `loi-1984-02-24-n1--2020-09-01--fr.bin`, `LuDocumentFetch/lu-pdf-invisible-1987-12-23-n5.bin`): the owner's decision
    (legal), asked in the pull request. Recommended: replace them in a separate pull request (`rgd-2021-12-17-a896-jo.bin`
    for /3's mutation tests, synthetic fixtures for the token shapes); a history rewrite is the owner's call. This slice
    adds no use of them.
  - R5. The 512 KB code body stays: it is the only admitted real-bytes case of /3's complex-work branch if R4's fixtures go.
  - R6. The title grammar cannot separate two instruments of one type and date, and an unparsed title refuses; no
    candidate is affected. Accepted.
  - R7. A shared XML pair across two works also refuses the true owner: 4 pairs, none admissible. Accepted.
  - R8. Rights versus consistency: for a rights-withheld state that consistency also refuses, S6 states the rights reason
    first and records both.
  - R9. A text-rule divergence between C# and the reference is fixed on the C# side, never in the reference. The wording
    pins and the probe found none; the review found one in check (c)'s title boundary on synthetic titles, fixed above.
    Over 126,988 synthetic titles (every BMP code point before the type and after the year), 2,911 differed with `\b`,
    and 21 still differ, none plausible in a title: the eight letters Unicode 16 added (the reference runs Unicode 15.1),
    U+0130 directly before the type (the reference's full lower-casing makes it `i` and a combining dot), and a letter or
    number outside the BMP touching the designation (.NET's regex reads UTF-16 units). Accepted.
  - R10. S3a must select articles with `MainBodyArticles` (/1's selection would make every body with annex articles an
    `ArticleCoordinatesMismatch`); its acceptance includes a body with excluded articles.
  - R11. Effort about 1 to 1.25 driver-days against 0.75 planned; one pull request.
  - R12. S1 may reuse `LuxembourgAknInFileIdentityClass` and the named-state rule unchanged; the design's §1.1 counts
    differ because they cover withheld XML only.
- **Review** (one round; 11 findings, all minor, each reproduced by the reviewer; all repaired, in commits `c0b3104d`,
  `89e7c7f2` and this one). Each finding and its repair:
  - The document ceiling was in no digest and no test, so reverting it to 64M kept CI green under the same /4 digest
    (found twice): `MaximumXmlCharacters` is internal, and a test holds it to `CustodyBounds.MaxObjectBytes` and holds a
    synthetic body with one 64Mi-character attribute to /3 `XmlException` and /4 inventoried. The ceiling bullet above
    says the digest does not cover it and S3a reuses the constant.
  - The provenance of the replay's expectations: this file, the replay's remarks and the pins' summary said derive-c
    sealed the /3 replay fields and the population pins; an independent Python replay computed them. All three now say
    which values are sealed and which are replayed. Commit `67b1c870`'s message says the same wrong thing and stays as
    written (no history rewrite); the pull request corrects it.
  - The pull request draft said the checkpoint's code paths were unchanged; it now says, as above, that the reopen runs
    shared constructors this slice changed, whose /3 branches hash as before, and that only R0 re-verifies the checkpoint.
  - Check (c)'s `\b` meant different characters in C# and the reference: explicit ends, profile /4 re-pinned to
    `8a011d87…`, nine titles held to the reference's answers, the probe re-run (decision above, R9).
  - Check (b)'s act branch, (e)'s dated-root guard and (f)'s "names the act" condition had no test and no run-15 instance
    (found twice): three synthetic facts, one per condition.
  - The sample's legal-care guard read only the `.bin` and `.json` files the build copied: the project now copies the
    folder whole and the guard lists every file below it; a stand-in `.xml`, a hidden file and a file in a subfolder each
    fail it.
  - The publisher-token tests held neither "never normalized" nor the forbidden ranges' ends: an id and a wId written
    decomposed come back as written; the forbidden rows cover every range's ends (33 rows), and 20 code points beside the
    ranges are admitted.
  - No test ran the production door's catalogue: a test fires (d) through `RunAsync(run)` from the run's own facts and
    serves the same file without them.
  - The replay's coverage claims: narrowed to the four /3 rejection kinds the sample holds, naming the two synthetic
    tests that hold /3's ceiling and its wId and applicability readers.
  - Every mutant the review used to show a gap now fails a test, and so do a widened and a narrowed forbidden set (10 of
    10). /4 tests 66 → 115.
- **Not yet.** S3a (content /2 over `TryDocument` and `MainBodyArticles`, refusing a population whose
  `RuleProfileSha256` is not /4's, with the disposition test first so disposition 5 is never tokenized); S4 (R1 runs
  `new LuxembourgAknArticleInventoryProducerV4(store).RunAsync(r0.Run, ct)` and records the /4 digest and population
  identity, never pairing content /1 with /4); S6 (index /9 reads `Evidence` and `ConsistencyFindings`); the dry run.

## The combined mount is derived; its gates found Luxembourg search empty (Claude, 2026-10-13)

- **The combined mount exists.** One offline derive of run 15's custody (`derive-a`, the run's own runtime, every proxy
  at a local trap) wrote the six files: 2.52 GB, of which the EU index 1.40 GB, the Luxembourg index 0.91 GB and the
  corpus 0.21 GB; peak working set 12.7 GB, 06:02 to 07:46 UTC. The second derive was stopped by its resource guard (commit headroom below 512 MB for a
  minute as the page file grew), so byte stability is shown by the release rehearsal's two derives instead.
- **The mounted gates over it** (`V3MountedGatesTests`, 23 of 24 pass): temporal exactness 1.0 on every arm (Luxembourg
  4 x 120 cases, EU 3 x 562), each date shuffle caught; but refusal 20 of 21 and retrieval anchor nDCG@10 0.846 (n 52).
  A local trace of each case named the misses.
- **Luxembourg search answered nothing.** The capability manifest's search cells were measured from each article's own
  publisher date, and none of the population's 8,854 articles has one: the publisher dates the journal document, which
  the state carries. With no cell, search said French was not searchable and returned no hit, and coverage counted 0
  searchable articles. Every EU search case passed. Now an article counts at its own date where it has one, else at its
  state's date, which is where search serves it. The reader accepts a manifest measured by either rule, so a mount built
  before (the canary chain, `derive-a`) still opens and serves what its manifest says.
- **No title had a document date** (0 of 2,021): the publisher puts `dateDocument` on the legal resource (`.../jo`) and
  the title on the expression (`.../jo/fr`). A title's date now falls back to its expression's legal resource.
- **The refusal miss was the gate's.** Its `format_not_available` case asked for `docx`, which Legilux publishes for most
  acts (22,908 docx manifestations); the handler rightly answered. The case now asks for a format the work's own
  manifestations do not carry.
- These are derivation and reader changes; the acquisition code stays frozen. The third derive with the fixed builder
  (`derive-c`, 09:48 to 11:17 UTC on 2026-10-13) passes every gate at 1.0 with every shuffled control caught; the
  population journeys pass (29 of 29), and the first signed release is built over it (STATUS-WEB.md).
- **The chained canary after the population** (`C:\lex-v3\chained-canary-after-lu-1`, tool frozen at the integration
  head): builds a and b both verified, 207 of 800 requests each (135 to the Publications Office for the GDPR, 72 to
  Legilux).

## The Luxembourg population is acquired; the combined derive waits for disk (Claude, 2026-10-12)

- **The acquisition is complete.** Run 15 (`C:\lex-v3\lu-population-20261011-15`, resuming run 14 in the #943 build)
  proved all nine families (rgd-a at 09:12 and rgd-g at 09:17 UTC on 2026-10-12), passed the census join live,
  journaled 11,206 document fetches by 14:08 UTC, retained the in-file rights evidence, the final manifest, the gazette
  phase and the corpus records: 260,235 records, 11,145 held bodies (2.95 GB), 21,972 of 300,000 requests spent.
  The offline-inputs pointer `mount-inputs-b306a972...json` is in custody.
- **The post-document step peaks at about 20 GB private.** The final resolution and scope reduction reached 20.2 GB
  (14:27 UTC, commit headroom 0.3 GB) and released; no replay could reach this step before.
- **The build process stopped while writing the combined mount** (18:29 UTC, runner resource stop, reason disk: free
  disk 398 MB, commit headroom 82 MB, peak working set 14.9 GB). The page file grew into the disk the owner's game
  install had left. No mount file was written; the acquisition is unaffected. The supervisor and the after-LU waiter
  were stopped, so they would not resume into the same wall.
- **What follows.** The two offline derives (`Lex.V3.Tool derive --custody ... --checkpoint mount-inputs-b306a972...json`,
  no publisher traffic), one at a time under a resource guard, once about 35 to 40 GB of disk (or more free memory) is
  available; then the chained canary, #898, the population-mount journeys and gates, and the release rehearsal.

## Publisher drift between the census and the later families: a third defect that stops the run (Claude, 2026-10-09)

- **The stop.** Luxembourg run 11 (`C:\lex-v3\lu-population-20261006-11`, the #940 build) proved all nine families for
  the first time: code-s, code-a, code-g, loi-s, loi-a, loi-g, rgd-s, rgd-a and rgd-g. At 02:00 UTC on 2026-10-09 it was
  refused at the census join: `RunRefused: ObservationSubjectNotInDeliveredCensus`, naming
  `.../eli/etat/leg/loi/1985/07/25/n1/jo/fr/docx`.
  - The supervisor's resumes 12 and 13 replayed the journal, spending 4 requests each, and were refused the same way.
    The supervisor was stopped before its third resume. The custody of runs 11, 12 and 13 is kept.
  - On 7 October the laptop ran on battery: it entered standby at 20:56 UTC, slept on critical battery at 20:59 UTC, and
    woke at 05:08 UTC on 8 October. The run paused for 8 hours and continued by itself.
- **The cause.** An offline scan of run 11's custody compared the two sets.
  - It held 4,257 assertion pages with 207,016 assertion subjects in the legislative ranges, and 260,132 census-like keys.
  - 28 assertion subjects are outside the census (3 loi, 25 rgd), and 1 of 22,112 relation subjects.
  - Every one of them is a resource the publisher added after the census read its range:
    - `rgd/2026/07/24/a488` and its manifestations;
    - the consolidations `consolide/20261011` of `rgd/2004/03/05/n15`, `rgd/2010/05/12/n6` and `rgd/2011/02/27/n10`,
      with their manifestations;
    - the docx, html and xml manifestations of `loi/1985/07/25/n1`, whose census held only the pdf.
  - Their pages were written between 08:06 UTC on 7 October and 01:45 UTC on 9 October, after loi-s (03:04 UTC,
    7 October) and rgd-s (19:38 UTC, 7 October) had read the census.
  - The families are enumerated hours apart over a live store. The join already accepted the reverse case, a census
    subject with no assertions, but refused this one.
- **The fix.** It is one rule in `BuildResourceObservations` (STANDING-ORDERS.md section 8, item 2's exception, recorded
  there).
  - An assertion row whose subject the census did not deliver is excluded and counted (`subject_not_in_census`), and so
    is a relation row (`relation_subject_not_in_census`). The census is the run's population: such a row is never
    admitted under an identity the census did not prove, and the next run's census names the resource.
  - The refusal codes `observation_subject_not_in_delivered_census` and `relation_row_subject_not_in_census` stay as
    reserved wire names, so a retained refusal still reads. The two private outcome kinds that produced them are removed.
  - The census-join test and the relation-row case now assert the exclusion: no refusal, a scope manifest, observations
    for the census subjects only, and one counted row. Three inventory pins name the new members. 101 of 101 tests in the
    five affected classes pass locally.
- **A replay on the real custody found a second defect behind the first.** Before spending two days on a fresh run, the
  driver replayed run 13's journal locally with the fix: a build of the branch stamped with run 13's source so the
  journal was admitted, in a scratch root of hard links (`C:\lex-v3\lu-census-drift-replay-1`), with a wire ceiling of
  12. Run 13's file list, sizes and journal digest were unchanged afterwards. Such a build is a local experiment only and is never
  retained as evidence.
  - The replay passed the census join and then failed minting an observation (exit 1): `Publisher identities must be
    exact absolute HTTP(S) URIs without userinfo, query, or fragment`.
  - One census key carries an unescaped "à": the Code du travail article
    `.../eli/etat/leg/code/travail/art._l._542-4_à_l._542-6./20201101`. Every `SourceObjectRef` requires an exact
    ASCII HTTP(S) URI. A scan of every IRI in run 11's custody against that rule found this key and its 5 assertion
    rows, and 15 relation targets naming `.../eli/état/...` (a publisher misspelling). Objects are carried as
    strings and minted into no identity.
  - The second fix: such a census key, and the assertion and relation rows that name it, are excluded and counted
    (`subject_not_a_publisher_uri`); the relation-only path gets the same guard. The Ingest copy of the publisher-URI
    rule gains a predicate form, so there is still one rule, pinned to the Contracts rule by a test.
  - The review of #943 found the key must also pass the scope resolver's exact resource-IRI rule, which refuses the
    whole run otherwise: a backslash passes the publisher-URI rule and fails that one. The exclusion now applies both,
    through `VerifiedLuxembourgSourceProfile.AdmitsObservationIdentity`, the resolver's own rule. It judges only
    subjects under `http://data.legilux.public.lu/`; a subject outside that prefix is a fault of the enumeration, and
    the resolver still refuses the run for it, as before.
- **A third replay found a scale limit.** With both fixes the replay passed the join and the observation build,
  retained all 1,017 rights batches and the 670 MB selection manifest, and then threw `OutOfMemoryException` with the
  process at 4.4 GB private (the commit charge was not exhausted). A replay that printed stack traces and sampled
  memory every 15 seconds pinned it.
  - `DocumentInputDigest` (the document phase's checkpoint input) serialized the complete scope manifest into one .NET
    string to hash it. At the Luxembourg population's scale that is longer than the largest string the runtime can hold.
  - The query result's digest (`AcquisitionCheckpoint`), which carries every typed assertion, uses the same helper and
    would have failed the same way after the document phase, about 30 hours into a fresh run.
  - The third fix: `ContractJson.Sha256` hashes exactly the UTF-8 bytes whose string `ContractJson.Serialize` returns,
    while the serializer writes them, and the checkpoint helper uses it, for the document, gazette and query-result
    digests. Every digest is unchanged: a unit test pins the streamed digest to the string's for non-ASCII text, nested
    objects and a document long enough to flush many times.
  - The scope manifest and the corpus record set already stream in 4 MiB chunks, and the other checkpoint documents on
    the path carry references, not the population.
- **The fourth replay reached the document phase.** With all three fixes, replay 4 (`C:\lex-v3\lu-census-drift-replay-4`)
  passed the join, the observations, the typed facts, scope resolution, the selection manifest and the streamed
  digest, and entered the document phase. Its 12-request ceiling stopped it there as intended:
  `DocumentFetchSessionNotStarted ... WireBudgetExhausted` (exit 3). The process peaked at 4.4 GB private. A replay
  cannot reach the document phase at scale or what follows it; the fresh run is the first to exercise them.
- **What the exclusions do not yet show.** They are carried in the acquisition result and its retained digest, but not
  printed and not written as records. The driver measures them from custody after the run and records them here.
  Carrying them into the mount's coverage view is a follow-up after the combined mount; until then the one excluded
  article is the launch contract's "every discovered body with one typed outcome" gap, recorded here.
- **Tests at the final head.** Lex.V3.Ingest.Tests: 203 of 206 in the adapter, census, construction-surface,
  scope-resolution, source-profile, transposition, document-checkpoint and publisher-identity classes (3 live canaries
  skipped). Lex.V3.Tests: 837 of 837 in the Luxembourg, scope, census and `ContractJson` classes. CI runs the full
  solution.
- **Publisher traffic of the replays.** Each replay ran `Lex.V3.Tool`, built locally from this branch, and spent its
  12-request ceiling (48 requests in all): the EU rights notice on the Publications Office route, robots and a few
  Legilux document attempts.
- **No cross-build resume (driver decision).** The tool and the runner both hold that a journal resumes only in the build
  that wrote it. Loosening that would change the frozen resume path beyond the smallest fix, so the Luxembourg run
  restarts fresh on the merged build.
- **What follows.** Luxembourg run 14 (`C:\lex-v3\lu-population-20261009-14`) started fresh at 14:35 UTC on 2026-10-09 on
  the #943 build: tool `5ac13b3c` from push CI 37943609391, whose runtime artifact expires 2026-10-12 14:34 UTC (the
  runner keeps its copy, and a supervised resume falls back to it). It reuses the EU population's custody (no EU
  traffic), under the supervisor (`-Next 15`), with the after-LU waiter and the keep-awake helper. It takes about
  three days with the machine awake.
  - Run 14 was stopped on 2026-10-10 at about 14:50 UTC at the owner's request (disk space). Its journal ends at
    `legislative-rgd-a` leaf 9: seven families proven (code s/a/g, loi s/a/g, rgd-s), 3,120 requests spent. On
    2026-10-11 at 12:27 UTC it was resumed in the same build as run 15 (`C:\lex-v3\lu-population-20261011-15`:
    122,491 objects hard-linked, journal verified through seq 72), under the supervisor (`-Next 16`), with the after-LU
    waiter and the keep-awake helper. Remaining: the rest of rgd-a, rgd-g, the census join and about 11,905 documents.
  - The same day the driver removed 31 unused git worktrees after saving their unpushed work to `backup/20261010/*`
    branches (`C:\lex-v3\lanes\worktree-backups-20261010.md`); no custody or corpus was touched.
  - Its two offline derives cover the EU
  and Luxembourg populations together; the EU derive alone peaked at about 14 GB private, so they may need the
  machine's memory more than the acquisition does. They are offline and can be re-run without publisher traffic.

## A Luxembourg title longer than a cursor key: a second defect that stops the run (Claude, 2026-10-06)

- **The stop.** Luxembourg run 10 (`C:\lex-v3\lu-population-20261005-10`, the build with #937's fix) proved code-s,
  code-a, code-g, loi-s and loi-g. The family #937 unblocked, `legislative-loi-a`, was refused at about 19:04 UTC on
  2026-10-06: `DeliveredKeyNotRepresentable`.
- **The cause.** The refused page, read from custody, has a row for `.../eli/etat/leg/loi/2005/06/21/n2/jo/fr` whose `key_4`,
  the object's string, is a `jolux:title` of 2,678 UTF-8 bytes. A cursor key part is at most 2,047 bytes
  (`LuxembourgQueryText.MaximumKeyPartByteLength`), so the page cannot be resumed from, on any run. This is the residual
  risk #937's review named. The 1,714 pages held before had no title key over 326 bytes.
- **The fix.** It is one line (STANDING-ORDERS.md section 8, item 2's exception, recorded there):
  `BIND(IF(isIRI(?object) || isLiteral(?object), SHA256(STR(?object)), "") AS ?key_4)`.
  - The key orders rows within one subject, predicate and object kind and resumes the cursor. Nothing reads the object
    from it: the row's `object` column carries the term. Scoped ranges split on whole subjects (`key_1`).
  - The 64-character digest is `LuxembourgDraftGraphDiscoveryPlan`'s measured `key_4` pattern on the same engine. That
    engine hashes non-ASCII text double UTF-8 encoded; that is harmless here, because the key is only returned to the
    engine, never recomputed.
  - `LuxembourgAssertionRowsTemplateTests` holds the page and count templates' key and the cursor refusing 2,678 bytes.
  - The plan's canonical bytes went from 79,844 to 79,860 (two copies of `SHA256(` and `)`). The plan digest and the
    retained count policy's digest were re-pinned from CI.
- **The scan.** Run 10's custody held 1,703 pages (1,304,182 rows, 1,213 of them assertion pages). The 2,678-byte key
  was the only refusable shape: no missing or unbound key or column, no term that is not one string, no unknown
  `object_kind`, and no key part other than `key_4` over 124 bytes. A cover stops at its first refused leaf, so the loi-a subjects
  after `.../loi/2005/06/21/n2` were never read; the restart reads them first.
- **The engine's digest, measured.** #940's review named one residual risk: Legilux's `SHA256()` had not been run live on
  a literal over 2 KB. One query asked for it on that title, sent by the driver as a single HTTPS POST with curl,
  outside `Lex.V3.Tool`. STANDING-ORDERS.md section 2 authorises publisher traffic through the product's acquisition code
  and `Lex.V3.Tool`, so it is recorded here and not repeated. The engine bound a 64-character lowercase hex digest,
  equal to the double-UTF-8 digest that `LuxembourgDraftGraphProducer` documents.
- **The restart (driver decision, reversible).** #940 merged (`ee725b66`; its push CI 37530912003 is green, and its
  runtime artifact expires 2026-10-09 21:16 UTC). Run 10 was stopped at 21:18 UTC, with its custody kept, before it
  read rgd-a. Letting it finish rgd-a first would have delayed the restart by 8 to 10 hours. The fresh run reaches rgd-a
  about 15 hours in, and run 10's 1.3 million rows held one defect.
  - Luxembourg run 11 (`C:\lex-v3\lu-population-20261006-11`) started at 21:18 UTC on the #940 build. It reuses the EU
    population's custody (no EU traffic) and has a wire ceiling of 300,000.
  - The Luxembourg supervisor resumes it after a publisher or resource stop (`-Next 12`). The after-LU waiter then builds
    the chained canary. The keep-awake helper holds while the runner and the supervisor live.
  - With no stop it takes about 64 hours, so it should end about 2026-10-09 13:00 UTC.
  - At 12:40 UTC on 2026-10-07 run 11 delivered `legislative-loi-a` leaf 23. Its subject range runs from `.../loi/20/` to
    `.../loi/201`, so it holds `.../loi/2005/06/21/n2`, the leaf run 10 was refused on. #940's key holds on the live
    publisher. Commit headroom on the machine was about 0.9 GB at the time; the runner's guard watches it.

## The Luxembourg assertion pages' language-tagged titles: a defect that stopped the run (Claude, 2026-10-05)

- **The stops.** Luxembourg run 2 resumed run 1 and proved seven of the nine families (code-s, code-a, code-g, loi-s,
  loi-g, rgd-s, rgd-g). It was refused at 16:03 UTC because `legislative-loi-a` and `legislative-rgd-a` could not be
  proven. Both ended `CoverRefused` with `page_decode_failed_on_our_side`, and run 1 had refused them the same way,
  behind its code-a failure.
- **The cause.** A retained page of each family was read from custody.
  - 12 of 997 and 8 of 997 rows had no `datatype_iri` and no `key_5`. Every one of them, and only those, was a
    language-tagged literal (`jolux:title`, `xml:lang` "fr").
  - Legilux's engine does not answer `DATATYPE()` with `rdf:langString` for such a literal, as SPARQL 1.1 specifies.
    The behaviour is documented for Cellar (`EuPageDecodeClassificationTests`' retained page, which
    `LuxembourgDraftGraphDiscoveryPlan` cites); these two pages are its first measurement on Legilux.
  - So the assertion-rows template's `STR(DATATYPE(?object))` errored and left the column and the key unbound.
  - The strict page decoder demands all six keys. It refused the page, deterministically, so no resume could pass it.
- **The fix.** It is one line, the smallest change (STANDING-ORDERS.md section 8, item 2's exception, recorded there):
  `BIND(IF(isLiteral(?object), COALESCE(STR(DATATYPE(?object)), ""), "") AS ?datatype_iri)`.
  - The column is made total, not only the key, because it is a canonical-key component that the delivery proof requires
    bound and that the decoder refuses unbound.
  - Nothing is lost: `language_tag` is non-empty for exactly those literals, and `LuxembourgLiteralCanonicalizer` reads
    an empty datatype with a language tag as `rdf:langString`.
  - `LuxembourgAssertionRowsTemplateTests` holds the template and the reading. It fails with the old line.
  - 1,235 of 1,248 Luxembourg, census and surface tests pass with the change; the other 13 are canaries and writers,
    skipped by default.
  - Two pins name the plan's bytes and were re-pinned (CI of #937). The plan's canonical bytes went from 79,812 to
    79,844: two copies of the 16 escaped bytes, in the page template and its count template. The plan digest and the
    retained count policy's digest changed with them.
  - The restart reads most of loi-a and rgd-a for the first time, and another row shape could still stop it. The one
    the review named is a title longer than the 2,047-byte cursor-key limit. Across the 1,714 Luxembourg pages runs 1
    and 2 held (1.34 million rows), the longest title key is 326 bytes.
- **What follows.** A new build cannot resume the old journal. The Luxembourg supervisor was stopped before its pointless
  resume, and the Luxembourg run restarts on the merged build, reusing the EU population's custody (no EU traffic).

## The EU population is complete; the Luxembourg run started (Claude, 2026-10-05)

- **The EU population.** Run 16 (`C:\lex-v3\eu-population-20261004-16`, tool ab44847e, resuming run 15) acquired to the end with
  no further stop.
  - Its journal holds 2,647 units: the adapter, about 1,330 Formex enumerations, about 1,260 packages, and the bounded
    Luxembourg companion.
  - Custody holds 93,454 objects. Acquisition ended with exit 0 at 04:07 UTC on 2026-10-05.
  - The runner's two independent offline derives were equal file for file (6 files), and equal to the acquired mount
    (05:11 UTC).
  - Each derive peaked at about 14 GB private memory; the machine's commit headroom fell below 1 GB until the page file
    grew. The combined mount's derives are likely larger.
- **The Luxembourg run.** The EU supervisor started it at 05:11 UTC: `C:\lex-v3\lu-population-20261005-1`, tool da61f5a5,
  CI run 37194419173.
  - It copied the EU custody, reopened the EU population from it (no EU traffic), and began acquiring at 05:36 UTC.
  - A Luxembourg supervisor (`C:\lex-v3\lanes\claude-lu-supervise.ps1`, the EU supervisor's pattern, no runner or tool
    change) resumes it from its journal after a publisher refusal or a resource stop, up to 8 times.
  - The after-LU waiter then builds the chained canary.
- **Luxembourg run 1 stopped; run 2 resumed it.** Run 1 ended at 10:47 UTC with exit 3 after 5 hours 11 minutes and 944
  requests.
  - `legislative-code-a` was not proven: a cover leaf's request got HTTP 500 from Legilux, with the gateway body
    `"<host>:8890 failed to respond"` (`code: error.unknown`), a backend outage.
  - The #925 retry covers only the 200-status "Read timed out" envelope, so this 500 refused the leaf, its family and
    the run.
  - The Luxembourg supervisor resumed it at 11:17 UTC as `C:\lex-v3\lu-population-20261005-2`, with the same build. 3
    query families and 26 cover leaves were already journaled.
  - Treating "failed to respond" like the read-timeout envelope needs a new build, which cannot resume this journal. It
    is decided by how often the stop recurs.

## EU population run 14 stopped on a network failure and resumed as run 15 (Claude, 2026-10-04)

- **The stop.** Run 14 (`C:\lex-v3\eu-population-20261004-14`, tool ab44847e) ended at 14:20 UTC after 5 hours 25 minutes,
  exit 3. It was not a resource stop: 5 GB of memory was available at the end.
  - On the way to the Publications Office, 49 robots bootstraps were refused before any product request, and one
    expression's enumeration was not executed (`NetworkFailure/TransportBeforeHeaders`, observed 3 times).
  - That expression's family refused eligibility, and so the whole Formex half.
  - 8,259 of the 45,000-request ceiling were spent.
  - The armed Luxembourg runner and the canary waiter stood down, as designed.
- **The resume.** Under the owner's ruling (STANDING-ORDERS.md section 8, item 2), the run was resumed from its journal
  in the build that wrote it.
  - Run 15 (`C:\lex-v3\eu-population-20261004-15`, the same tool and CI run, `-ResumeFrom` run 14) started at
    14:34 UTC.
  - It hard-linked run 14's 37,290 custody objects. Run 14's root is unchanged.
  - The resume verified run 14's journal through entry 460: the adapter phase, 214 Formex enumerations and 163
    packages.
  - The Luxembourg runner and the canary waiter were re-armed on the new roots.
- **The fragility.** One transient network failure still stops the whole EU population, and a robots bootstrap is
  never retried. The acquisition code is frozen until the combined mount exists, so this is changed only if it stops
  the run again (the ruling's exception for a defect that stops a run).
- **It stopped the run again.** Run 15 ended at 15:26 UTC with exit 3, after 51 minutes, the same way: another
  expression of the same family failed with `NetworkFailure/TransportBeforeHeaders`, and 12 robots bootstraps were
  refused, in 252 requests.
- **The smallest change that keeps the build.** A tool fix would need a new build, and a journal resumes only in the
  build that wrote it. So the change is a supervisor around the unchanged runner, `C:\lex-v3\lanes\claude-eu-supervise.ps1`
  (no runner or tool change):
  - after a run ends on a publisher refusal (exit 3) or a resource stop (exit -1), it waits 30, then 60, then 120
    minutes, then resumes from the journal into a fresh root with the same build, up to 8 times;
  - when the EU run completes with both derives, it starts the Luxembourg runner and the canary waiter;
  - any other ending stands it down, with a note in the driver's ledger.
- **Run 16.** It resumed run 15 at 16:12 UTC. At 16:42 its journal held 576 units (the adapter, 292 Formex
  enumerations, 282 packages), and it was fetching.
- **After the populations.** The tool fix (retrying a transport failure and a robots bootstrap within the run) is
  for after the populations, under a new build.

## An interrupted Luxembourg cover resumes after its delivered leaves (Claude, 2026-10-04)

Reversible driver decision, for the Luxembourg legislative population run.

**The problem.** The resume (#924) journals a Luxembourg family only once its whole cover is proven. A run stopped while a family's cover was in progress therefore restarted that family from its first leaf. A stop in hour 20 of the regulations' assertions (about 22-23.5 hours) lost 20 hours. A breaker stop (#925) is exactly the stop its own message says to resume after.

**The change: each leaf is journaled as it is delivered.**
- **The journal.** Each leaf an adaptive cover proves and retains is journaled as `lu-cover-leaf`, keyed `family#index`. The line holds:
  - the family's set, range, plan and renderer;
  - the chain's split history at that moment;
  - the leaf, its enumeration checkpoint, and the run and profile it was proven under.
- **The resume.** It restores the leaves in index order as far as each still binds and proves:
  - its record names this family and its index;
  - its split history, replayed from the root, places it there, with the leaves accepted before it still first;
  - its checkpoint restores under its run and the cover's one profile;
  - its query bounds are its leaf's, and it proves that leaf;
  - its held count queries are what this request's plan, set and renderer render for that leaf (the family resume's
    check, review of #929), so a line naming this run's renderer over another renderer's checkpoint is not admitted.
- **The prefix.** The first leaf that fails ends the prefix, and the cover goes on from there on a session of the resumed run. The accepted leaves are journaled again, so a resume of a resumed run finds them. Nothing is admitted on the journal's word.
- **Across runs.** The cover then spans the interrupted run's leaves and the resumed run's, as contiguous blocks (#927), under the interrupted run's interpretation profile reference, checked against the plan's profile.
- **A family record that does not restore** is rebuilt from its delivered leaves when they restore. The resumption summary counts replayed and live leaves under `lu-cover-leaf`.

**Unchanged:**
- the family journal line and each family's catalog entry (a family's run is still its first leaf's);
- a run that is not resumed, except that it writes the leaf lines; its catalog is unchanged. A resumed run's catalog
  gains the `lu-cover-leaf` phase in its resumption block;
- the census: the cover's callback is a method group on a small helper, not a new lambda in the adapter's pinned method.

The pinned `RunCoverCoreAsync` signature gains the resume point and the leaf callback.

**Tests:**
- `AResumedCoverSendsNothingForItsDeliveredLeavesAndProvesAcrossBothRuns`: a cover stopped at its second leaf resumes from the reported leaf with only that leaf's four requests sent, and the cover reconciles over both runs. A resume point under another profile is refused before anything is sent.
- `AFamilyWhoseRecordDoesNotRestoreIsRebuiltFromItsDeliveredLeaves`: two families without their records are rebuilt from their leaves with no request, and the leaves are journaled again.
- The existing "enumerated again" test now strips those families' leaf lines too.
- `ALeafLineThatDoesNotBindEndsThePrefixAndItsFamilyIsEnumeratedAgain`: two families' first leaf checkpoints exchanged;
  both prefixes end at zero, only those families go back to the publisher, and their leaves are journaled live.
- `ALeafWhoseHeldQueriesAnotherRendererRenderedEndsThePrefix`: a resume under a second query renderer, with leaf lines
  forged to name it, restores no leaf.
- The rebuild test also reopens the resumed catalog on its own, and resumes the resumed run a third time from its
  journal: the leaves are found there and nothing is sent.
- Partial restore (k of n leaves, then live) is tested at the executor, from a hand-built resume point, not through the
  adapter.

CI compiles and runs these: local memory is reserved for the EU population run.

## One session per Luxembourg document phase, replaced hourly (Claude, 2026-10-04)

Reversible driver decision, for the Luxembourg legislative population run.

**The problem.** Each Luxembourg document and Gazette GET opened a session of its own, so each made its own robots.txt fetch. The population's documents are about 41,600 GETs, so that is about 41,600 robots fetches. At the host's 1.5-second pacing that is about 17 hours of the run's ~81. Each robots fetch is also a run-level failure point: a failed bootstrap is never retried, and it stops the phase.

**The change:**
- **One session per phase.** A document phase (the selected documents, then the Gazette listings) sends its GETs through one session at a time (`DocumentGetBatch`), replaced every hour. One robots fetch serves up to an hour of documents, and the policy any GET relied on is never more than an hour old, far inside the session's own 24-hour gate.
- **Every URL is still evaluated literally (Decision 83).**
  - The bootstrap evaluates the first document's own URL.
  - Every later document is evaluated by the session (`OpenPlanItemAdmittedByRobots`), against the robots bytes its own bootstrap fetched and held, before any request ordinal is allocated.
  - A disallowed path is that document's own refusal, and nothing is sent for it. A policy that cannot be interpreted for it is a run-level refusal, as at a bootstrap.
  - `OpenPlanItem` itself now refuses any URL other than the session's witness that the admitted policy does not allow, so no door can skip the evaluation.
- **The robots fetch is retained.**
  - Before a session's first document, its robots route is written to custody and reopened by digest. Every result of that session names it.
  - A per-document session never retained its robots fetch, and its readback "does not establish a fresh robots verdict".
- **Checkpoints.**
  - The document and Gazette checkpoints become `lex-lu-selected-documents-checkpoint/2` and `lex-lu-gazette-checkpoint/2`, and each fetch names its robots route. `/1` checkpoints are still read under their own rules.
  - A fetch record's bytes are unchanged when it names none: the new member is omitted when null.
- **Replay and resume.**
  - `LuxembourgDocumentFetchRouteReader.ReopenAdmittedAsync` reopens a shared session's GET: any product request of its run, not only the first.
  - It re-derives the verdict offline. The robots route must reopen as the profile's robots route, reproducing its bytes from the held receipts and bodies, and belong to the same run. Its policy must allow the document's exact path, and it must have been younger than 24 hours when the GET started.
  - `ReopenRobotsRefusalAsync` re-derives a refusal the same way.
  - A resumed run journals and holds each GET's robots route with it.

**Saved:** about 41,600 robots fetches become about one per phase-hour (about 20). The document and Gazette phases shrink from about 35 hours to about 17.5, and the run from about 81 hours to about 64. There are as many fewer custody objects, and as many fewer points of failure.

**Unchanged:**
- the source profile and its digest;
- the route schema (`lex-license-http-evidence/4`);
- the acquisition catalog, the corpus and the indexes;
- the single-GET door `RunDocumentGetAsync`, which no production path calls any more; its tests keep it, and its retained routes still reopen through `ReopenAsync`.

**Census:** the producers of `LuxembourgDocumentGetAttemptResult` gain the batch, the shared attempt loop and the new reader doors, and the pin is updated.

**Tests** (`LuxembourgDocumentSessionTests` and the existing suites):
- **One session for three documents.** One robots fetch, request ordinals 1-3 in one run, and each GET reopens under the retained verdict. The per-document door refuses a later product request.
- **A disallowed document.** A document the shared policy disallows is refused with nothing sent, and its refusal reopens. An allowed document does not reopen as a refusal.
- **Hourly replacement.** An hour between documents gives each its own session and robots route, and a GET does not reopen under another run's policy.
- **Existing suites.** The Gazette checkpoint and resume tests now run through the shared session and the `/2` checkpoints.
- **The guard.** A session opens no URL its admitted policy disallows, by either door, and allocates no ordinal for it.
- **`/1` checkpoints.** A `/1` document checkpoint (an executed GET and a robots refusal) and a `/1` Gazette checkpoint still replay. A `/1` fetch naming a robots route, or a `/2` fetch without one, does not read.
- **Untested, as defence in depth that a real run cannot reach:** the reader's 24-hour freshness check and its exact-path re-derivation (the session refuses such a GET first), and the batch's in-session UnsafeToInterpret branch.

## A Luxembourg partition cover longer than one robots generation (Claude, 2026-10-04)

Reversible driver decision, for the Luxembourg legislative population run.

**The problem.** A session's robots permission lasts 24 hours from its robots fetch: the profile's maximum robots policy age, checked by UTC and monotonic time at every send. Each family's partition cover ran on one session. The largest family, the regulations' assertions, takes about 22-23.5 hours, and the gateway retries of #925 lengthen it. At expiry:
- the send gate refuses the next request, and the leaf refuses `ObservationNotExecuted`;
- the cover and the family are lost, and the run refuses;
- a resume (#924) starts the family again from its first leaf, into the same wall.

**The fix: one session per robots generation, in the executor.**
- **Rotation.** `RunCoverCoreAsync` replaces its session between leaves once less than half the maximum age (12 hours) remains. The session gains `RobotsPolicyRemaining`, computed by the same rule as its send gate.
  - Each new session fetches and evaluates robots for itself (Decision 83).
  - A robots refusal at a replacement refuses every remaining leaf without sending (Decision 67).
- **Restart.** A leaf whose session expired while it ran is restarted whole on a fresh session, once; a second expiry refuses.
- **Leaves never span runs.** A leaf's two passes always share one run.

**The cover proof.**
- `LuxembourgPartitionCover.TryCreate` now accepts several runs when they form contiguous blocks of leaves: a run that reappears after another refuses `leaf_run_identity_differs`.
- A cover with a root count stays one run.
- One interpretation profile is still required, which implies one source profile (each dialect derives exactly one).
- `RunIdentity` is the first leaf's run.

**The checkpoint.**
- A cover of one run is written as `lex-lu-partition-cover-checkpoint/1`, byte for byte as before.
- A cover over several runs is written as `/2`, which names its run blocks in order. Each leaf is restored under its own block's run. The first block must be the caller's run, and the blocks must be distinct and add up to the leaves.
- The adapter, the journal and the catalog are unchanged; a family's run is still its first leaf's.

**Unchanged:**
- the profile and its digest;
- the send gate's rule, now shared with `RobotsPolicyRemaining`;
- the construction-surface pins: the cover's two refusal lambdas keep their ordinals, now applied to the leaves not yet run;
- the census pins: the checkpoint class stays declined as a record-version holder, its reason now naming both schema identifiers.

**Next:** journal each delivered leaf (`lu-cover-leaf`), so a resume continues an unfinished cover rather than starting the family again (design in the driver's notes).

**Tests:**
- `LuxembourgCoverRotationTests`:
  - the session's remaining validity falls with its clock and is zero at 24 hours;
  - a cover outlasting half a generation replaces its session between leaves. That takes two robots fetches, two runs and a `/2` checkpoint that round-trips; the wrong first run is refused;
  - a leaf that expires mid-run is restarted whole, once;
  - a second expiry refuses rather than looping;
  - a robots 503 at a replacement refuses the remaining leaves with nothing sent.
- `LuxembourgPartitionCoverTests`: two runs in contiguous blocks make one cover; a run that reappears refuses; two runs beside a root count refuse.

**Local:**
- the ingest suite's Luxembourg, census, surface, resume and journal tests: 1,193 total, 0 failed, 12 live canaries skipped;
- the contract cover, surface and census tests pass.

## Legilux gateway read timeouts: retry, then split, then stop (Claude, 2026-10-04)

Reversible driver decision, for the Luxembourg legislative population run.

**The problem.** The gateway in front of the Legilux SPARQL endpoint cuts a query at about 45 seconds. It answers HTTP 500 with one 120-byte JSON envelope: `{"meta":"error","id":"<uuid>","title":"Read timed out","code":"error.unknown","data":null}`, with a fresh lowercase id in every response. The 2026-09-30 count probe and population preflight custody retained it; one example is object `79282f1f…`. Until now, only a first-pass COUNT was split on it. A page or a second-pass COUNT that timed out refused the leaf, the cover and the run. In a run of about 5,900 SPARQL requests at a median of 25 seconds, the root count of the largest family took 42.4 seconds, so one slow page could lose the run.

**The policy** (`LuxembourgGatewayTimeouts`, the shared glue, the Luxembourg executor):
- **Retry.** A page (either pass) or a second-pass COUNT that answers exactly this envelope is sent again as the same plan item, at most three sends in all.
  - Each retry first waits the profile's own maximum retry delay (30 s) on the run's clock. The session then adds its own backoff and origin pacing.
  - Every send is reserved from the run's one wire budget before it goes out.
  - Every attempt is retained as its own route before the retry is decided, as for the Publications Office retries.
- **No retry for a first-pass COUNT.** It is split at once, as before: a split costs a few cheap counts, while a retry repeats a 45-second query.
- **Split.** When the sends are spent, the leaf asks for smaller leaves (`PartitionRequired`), and the adaptive cover splits it through the existing chain.
  - Each child proves its own two passes.
  - The abandoned leaf's partial passes stay in custody, referenced by no leaf checkpoint or cover.
- **Stop.** A breaker, one per run, counts envelopes since the last admitted page. At eight, the leaf refuses and the cover stops, and every later pass of the run refuses without sending. The run ends with a typed refusal, to be resumed (#924) after the publisher recovers.

**Only that envelope.** Any other body, status or endpoint refuses exactly as before, including:
- the loose timeout shape without an id, which first-pass COUNTs still split on;
- 502, 503 and 504 carrying the same body.

**What does not change:**
- the profile and its digest;
- `RoutedHttpAcquisitionSession`; the envelope check reads custody in the glue, outside the session (Decision 71);
- checkpoint schemas, the construction-surface pins and the closed-vocabulary census;
- the EU retries.

**Limits:**
- **Pages.** Only COUNT timeouts were retained; that pages answer the same envelope is inferred from the same gateway. A page timeout in another shape refuses as before.
- **Waiting.** The 30-second wait is a judgement, not a measurement.
- **Wire ceiling.** Size `--wire-ceiling` with about 10% headroom over the ~5,900 SPARQL requests, for retries and re-enumerated leaves.
- **24 hours.** A cover's single session still ages out after 24 hours (the largest family runs about 22-23.5 hours). That is a separate slice.

**Tests** (`LuxembourgGatewayTimeoutRetryTests`):
- `TheRetainedGatewayTimeoutIsRecognizedByItsTemplate`: the retained bytes, pinned by digest, and a fresh id.
- `AGatewayTimeoutIsRetriedAsTheSamePlanItemThenAdmitted`: a first-pass page, the second-pass COUNT or a second-pass page, timing out once or twice. It is admitted with the expected sends, budget and product count, one 30-second wait per retry, then the session's backoff.
- `ARequestThatKeepsTimingOutIsSplitAfterThreeSends`: a page, and a second-pass COUNT after a completed first pass. No fourth send; two children prove; the cover holds.
- `WithoutSplittingAPageThatKeepsTimingOutAsksForSmallerLeaves`
- `EightGatewayTimeoutsWithoutAnAdmittedPageStopTheRun`: a later cover of the same run sends only its robots fetch.
- `ABodyThatIsNotExactlyTheGatewayTimeoutIsRefusedOnItsFirstSend`: thirteen near misses, each sent once with no wait.

Existing tests:
- `AdaptiveCoverSplitsRetainedInitialCountCapacityFailures` gains the exact envelope: still split, never retried.
- The loose-shape test is renamed `AnUnrecognizedTimeoutShapeAfterTheFirstPassStopsRatherThanSplitting`.

**Local evidence:** 182 executor, retry, pin and census tests pass (2 live canaries skipped). With the retry disabled, all nine retry and split cases fail.

## Resuming an interrupted acquisition: the journal, the EU half and the Luxembourg half (Claude, 2026-10-03)

Every `build` now writes a progress journal beside its custody (`acquisition-progress-<utc>-<id>.jsonl`): append-only JSON lines chained by SHA-256, one per unit, each appended only after the unit's custody holds returned and naming the unit's existing checkpoint record and the objects it depends on. The first line names the source head, the digest of the arguments (seeds, Luxembourg selection, encoding, `--eu-checkpoint`), the renderer references and the journal it resumed, if any. The journal only says where to look: a resumed run admits each unit through the checked reader a full replay uses.

`build --resume-from <journal>` verifies everything before any request: the chain (an unterminated last line is dropped, any other fault refuses), the first line against this build's source and these arguments, and every object any line names, read back from `--custody`. It holds the journal's exact bytes with a `lex-v3-acquisition-resume/1` record, reopens the renderer sources from custody by the journaled references once the checkout's bytes are shown to be the same, and starts a journal of its own naming the one it resumed. A journal that does not verify exits 2 with nothing spent.

EU half: a journaled adapter run reopens through `EuQueryExecutionAdapter.ReopenAsync`, and journaled Formex enumerations and packages through their own reopen paths, all before the rights request (which every build renews, Decision 95). The population walk takes them by expression and acquires the rest live, recording both in walk order, so the population checkpoint reopens exactly as an uninterrupted run's. A journaled EU catalog is reused through `ReuseAsync`. A unit the walk would not reach, a foreign adapter run, swapped checkpoints or a changed renderer refuse before any request. A resumed run's EU catalog is `lex-eu-first-mount-acquisition/2` with its resumption (resume record, the earlier runs' window and spend bounds, replayed and live units per phase, this run's spend and time); every other run writes `/1` unchanged, and readers take both. A resumed build records a resumption for the EU half only when the interrupted run acquired that population itself; when the interrupted run only renewed a retained population this build also names (the Luxembourg runner's case), the EU half keeps the provenance of the run that acquired it, and the interruption belongs to the Luxembourg half's resumption alone. A unit in flight at the stop is redone; the earlier runs' spend is reported as at least their last journaled spends and at most their ceilings.

Chains and reports: a run interrupted while it was itself resuming re-journals the units it replayed under its own lines, so a resume of its journal reads the resume record that journal names (checked to describe the journal and line the header names, under the same source and arguments) and folds in the runs before it: the window of the replayed units starts at the first run's start and ends at the interrupted run's last line, the spend bounds are the runs' sums, and `PreviousRuns` counts them. Only the given journal's units are replayed: a unit an earlier journal named that the interrupted resume never reached again is acquired again. `build-report.json` gains a `resumption` block only for a resumed population, stating each half's own resumption (its journal, earlier runs, window, spend bounds, resume time and per-phase tallies) and no window for the mount as a whole, since the halves can come from different journals or one from none; `derive` rebuilds it from the catalogs.

Luxembourg half: the journal names the scope reference and plan identity (before the first request), the vocabulary checkpoint, each proven family (the query catalog's own family record, with its plan and renderer bytes held first) and each executed document or Gazette GET (the phase checkpoint's own fetch record, keyed by manifest row and address and bound to the selection's input digest). A resumed run takes its interrupted run's scope and plan identity when the scope bytes are the same, reopens the vocabulary through `ReopenVocabularyAsync` when its checkpoint is for this plan, restores each family through the restore a full replay uses (`RestoreFamilyLegsAsync`, now shared with `ReopenAcquisitionAsync`) when its record names this run's set, range, plan and renderer, and reopens each GET through `LuxembourgDocumentFetchRouteReader` when its selection digest matches. Unlike the EU half, a Luxembourg unit that does not bind or reopen is acquired again rather than refused: these are checked lazily, inside the run, and refusing there would waste the spend already made. Robots refusals and GETs that did not execute are not journaled. A resumed run's catalog is `lex-lu-first-mount-acquisition/3` (act) or `/4` (population): the uninterrupted run's record wrapped with the resumption; `/1` and `/2` are written unchanged otherwise, and both readers take both shapes. No Luxembourg catalog unit is journaled: a run killed after its catalog replays every Luxembourg unit without a request and writes the catalog again.

Tests: `AcquisitionJournalTests` (read-back, torn tail dropped, ten faults, invocation and held-object checks), `V3FirstMountBuildTests.AResumeJournalIsVerifiedBeforeAnyRequest` (the tool as a process behind a proxy trap: every tampered journal exits 2), `EuFirstMountAcquisitionTests.AnInterruptedPopulationResumesReplayingWhatItsJournalNamesAndAcquiringOnlyTheRest` (interrupted mid-family; on resume no adapter request, only the remaining Formex requests, the old root unchanged, projections without URNs equal to an uninterrupted run's, the mount equal to two derive processes), `EuFirstMountAcquisitionTests.AJournalThatDoesNotDescribeThisAcquisitionRefusesBeforeAnyRequest`, `LexV3ToolProgramTests.AResumeJournalThatDoesNotReadExitsTwoBeforeAnyWork`, `LuxembourgFirstMountAcquisitionTests.AnInterruptedPopulationResumesFetchingOnlyWhatItsJournalDoesNotName` (the two-work population stopped at its second document GET; on resume no SPARQL and one GET, the `/3` catalog reopening with its resumption) `LuxembourgFirstMountAcquisitionTests.AJournaledFamilyThatDoesNotRestoreForItsRangeIsEnumeratedAgain` (the legislative population with two families' covers exchanged: exactly those two are enumerated again, the tallies account for every GET, the `/4` catalog reopens), and from the review's repair: `EuFirstMountAcquisitionTests.AResumeOfAnInterruptedResumeDatesItsReplayedUnitsFromTheFirstRun` (run A stopped, run B resuming it stopped in turn, run C resuming B: each run an hour after the one before; C's resumption names two earlier runs, A's start, B's last line and both spends, and a header naming a resume record for another line refuses), `EuFirstMountAcquisitionTests.AResumeOfARunThatOnlyRenewedARetainedPopulationLeavesItToTheRunThatAcquiredIt` (only the rights request goes out and the EU half names no resumption), `EuFirstMountAcquisitionTests.AResumedMountsReportStatesEachHalfsOwnResumption` (two halves from different journals each keep their own) and `LuxembourgGazetteAcquisitionTests.AJournaledGazetteGetIsReplayedWithoutARequest` (a journaled document GET and Gazette listing GET both reopen with no request and the same Gazette result).

EU runs 9 and 10 ran tool b5239b6f, which predates the journal: they cannot be resumed, and neither can any run on a tool built before this slice. The runner change that uses it (`-ResumeFrom <old root>`: a fresh root, custody objects hard-linked except `*.partial`, only the original `--eu-checkpoint` pointer, never the journal, `--resume-from` passed, the old root's snapshot verified after, and the interrupted run's own verified runtime when CI's three-day artifact has expired) is installed in the lane runners since 2026-10-04 (driver decision; the runners are not in this repository). EU run 13 (2026-10-04, root `C:\lex-v3\eu-population-20261004-13`) runs b5239b6f because its runtime was still downloadable and the resume tool was not merged; runs from the merged tool on journal and resume.

## Luxembourg legislative population command (Codex, 2026-10-01)

Reversible driver decision under standing order section 7: `build --lu-population legislative`
selects the disjoint publisher URI ranges `code/..code0`, `loi/..loi0`, and `rgd/..rgd0` beneath
`http://data.legilux.public.lu/eli/etat/leg/`. It shares one observed vocabulary, proves each
S/A/G family with the adaptive cover, and keeps gaps outside the requested scope. Consolidated
and expression/manifestation descendants inside those ranges keep the existing individual
scope, source, transport and rights gates. Prefix membership alone does not establish a legal
family or authorize a body. The legacy bounded range and whole-IRI commands remain compatible.

A retained family manifest lists every observed typeDocument IRI once, explicitly stating that
its type family was not independently enumerated. Objects found in selected URI ranges retain
their individual outcomes; outside-range and unobserved-family complements are explicitly
unenumerated. This is URI-range coverage, not a claim that the prefixes exhaust every legal
family. The CLI writes a manifest pointer. The population catalog binds that declaration to
every family plan and regenerates the manifest from verified vocabulary during offline restore.
`derive` restores either the old bounded catalog or the new population catalog without HTTP.

Validation added: nine-family replay from two copied stores; range/policy/manifest tampering;
missing scope custody; excluded-gap behavior; raw and Brotli complete mounts in two independent
CLI processes (including predecessor and consolidated EU); accepted/mixed CLI arguments. Local
`pwsh -File eng/test-fast.ps1` and touched ingest tests are deferred under the standing 4 GiB
free-memory rule. Initial CI reported 5,971 passed, one stale method-signature pin failed, and
22 skipped. Claude reproduced that sole blocker. The repair updates the pin and uses the
existing advancing fixture clock for mocked LU captures; fresh full CI gates merge. No full live population or memory
fit is claimed. Measured March2017 partition and representative counts below remain the sizing
evidence; the proposed full run must preserve the resource stop and shared wire ceiling.

### Completed count and real-replay evidence (19:09 UTC)

The declared URI ranges now have nine independently rehashed single-count observations, using
18 wire requests: code S21,231 / A112,276 / G0; loi S93,161 / A580,389 / G32,121; rgd
S145,703 / A914,752 / G63,225. Totals: S260,095 / A1,607,417 / G95,346. Evidence:
`C:\lex-v3\lanes\lu-legislative-nine-counts-readback.json` and each named root's
`count-readback.json`. Each two-wire budget intentionally stops after its first COUNT. This
is sizing evidence, not repeated-enumeration proof or body counts. At the existing 100,000-row
leaf ceiling those observed counts need at least 25 leaves; actual split shape, overhead and
future counts can differ. Regulation and dated civil-code body-cost measurements are prepared
for the next free acquisition slot, using checked EU reuse and 800 requests each.

PR907's merged runtime independently rederived the real bounded custody twice at
`C:\lex-v3\lu-consolidated-offline-20261001-3`, completed 19:01:31 UTC. Every one of the six
mount file digests matched, with zero proxy-trap connections. The EU index has 594 articles:
297 ENG and 297 FRA across six expressions, 99 each, including all four real CONS.ACT packages.
Both executions exited zero, peaking at 288,542,720 and 292,675,584 bytes. Independent Python
hash and SQLite readback is retained there. LU article/state/member content is unchanged;
only its complete-corpus references and observation-log digest change with the new EU content.

## Measured Luxembourg partition and full-run sizing decision

The bounded March 2017 law acquisition completed at 18:03:53 UTC, exit 0, from exact CI runtime `614a9e1509f28f53d77dfdf8a69da35339407da5` (green workflow 36897978670). Evidence: `C:\lex-v3\lu-legislative-measurement-20261001-1\summary.json`, `measurement.json`, `sizing-scenarios.json` and `verified-digests.txt`. The acquisition used 276 requests (EU 135, LU 141) within 800, with proven LU S=219, A=1,414, G=114. Combined elapsed time was 20m01.8s; it is not isolated LU timing.

LU has 219 records: 22 held bodies totaling 3,239,169 logical bytes, 171 point records and 26 typed quarantines. Of the 22 held bodies, 12 have corpus outcome `acquired` and 10 `rights_withheld` with `typed_quarantine_in_file_reading_rejected`; held bytes do not imply permission to serve. The EU subset has six acquired package records and two typed quarantines; only two packages admit a main body, producing 99 English and 99 French GDPR articles. Four acquired packages have `formex_main_body_missing`. Five EU states are retained: one original and four ambiguous (three without CELEX). No complete consolidated-body coverage is claimed.

Two independent `Lex.V3.Tool derive` processes completed at 18:08 UTC, each with zero proxy-trap connections. All six files match the acquired mount byte for byte; an independent Python SHA-256/file-set comparison confirmed this. Corpus content identity is `dd3b9c18eb7cb4fefd1c9fb59b36f8a85f28ad9a5fce4af3df58a89d95b874ce`; EU index SHA-256 `4aabc56fbe67837e47fb42706ab293f8719c23d08b3dbe714f171a389cbe99ea`; LU index SHA-256 `738902acebc4dcaa00226b8190c49f866bbc3b48fb5fdee95d6302cc7b31ece0`.

Compressed custody is 4,534,940 bytes. Sampled acquisition peak RSS was 254,136,320 bytes; offline derives peaked at 203,833,344 and 207,671,296 bytes and took about 12 seconds each. Acquisition retained its original 2 GiB start / 1 GiB stop guard. A separate offline continuation used 1.5 GiB start / 1 GiB stop after measuring the actual build. Every phase preserved the disk guard (3 GiB floor plus 512 MiB stop margin); none was resource-stopped. These are sampled process measurements, not whole-population bounds.

Reversible sizing decision: do not multiply this sample into a claim that the whole population fits. The three observed law/regulation/code prefixes total 260,095 subjects. A same-density scenario yields about 167,459 LU requests, 26,129 held bodies, 3.85 GB logical bodies and 5.39 GB compressed custody including repeated GDPR overhead. Those figures are planning scenarios only: this sample contains fixed vocabulary cost, other families differ, and consolidated descendants must not be counted twice. Next acquire representative regulation/code ranges, prove their S/A/G counts, and close the legislative family scope manifest before committing the full LU ceiling. Excluded families receive a typed family disposition. Full-run memory fit remains unproven.

The EU language decision remains: enumerate original and consolidated EN/FRA expressions; retain the contract's explicit language-out-of-scope outcome for other languages without Formex enumeration. All 24-language enumeration would spend traffic on bodies outside the served scope. The next full EU run uses fresh custody, checkpoint writers and the merged gateway recovery; the old refused run stays retained.

Validation: repaired code `3d75dc0d` passed workflow 36902789017 (5,953 passed, 0 failed, 22 skipped). Local `pwsh -File eng/test-fast.ps1` and touched ingest suites remain deferred under the standing 4 GiB memory guard; CI ran the complete solution. Claude's original code review returned MERGE; scoped confirmation of the renderer-binding repair, hidden-file guard and measured evidence returned MERGE. PR #904 merged as 6f42f296 after exact 576697df CI36905375699 (5,953 passed, 0 failed, 22 skipped). The independent Python comparison is now retained as independent-python-comparison.json. The measured immutable 614a9e15 runtime used direct acquisition, so it did not execute the later repaired reuse path.


## EU population refusal and recovery

The frozen EU run at `C:\lex-v3\eu-population-20261001-4` ended at 16:35 UTC with exit 3 after 10,424 wire requests and 685 manifestation enumeration results. It produced no mount. The failed expression is `http://publications.europa.eu/resource/cellar/ecdb2904-4c60-11ec-91ac-01aa75ed71a1.0010`. Its first COUNT is retained, immediately followed by a 122-byte gateway response (SHA-256 `880c929020d4b79bf1995656d21d9a6859aab3a9460f941eb0b1a6e5502ee4cc`). The old code omitted rejected routes, so timing alone does not prove that response's request or HTTP status.

The recovery slice retains all executed enumeration routes before rejection. It permits only this exact gateway body with HTTP 502 at the Publications Office SPARQL endpoint to use the existing bounded retry allowance, backoff and shared wire budget. Other gateway/challenge bytes and Luxembourg responses remain refused. Formex diagnostic version 2 lists every failed enumeration and its underlying detail while leaving an unclosed population's total unknown. No old custody is rewritten or imported.

Local `pwsh -File eng/test-fast.ps1` and touched ingest tests are deferred under the standing memory guard (free RAM below 4 GiB). PR #906 merged as c8683373 after CI36895673765 (5,937 passed, 0 failed, 22 skipped) and Claude MERGE. A bounded live proof is pending. No new full acquisition has started. PR #904 has completed its measured partition and two independent derives; see the current evidence above.

## Bounded Luxembourg acquisition and independent derivation

PR #904 reuses a complete checked EU population when requested, renews the rights notice once, and reports LU family proofs, record outcomes and unique held-body bytes. Tests cover consolidated EN/FRA packages with nullable CELEX through reuse and two complete offline derives. It now imports merged #906, including retained refused routes and bounded gateway retry. Refusal diagnostics also name the terminal HTTP status and retained body digest when available, so the exact response can be located without scanning custody by time.

Claude returned MERGE for the code at 614a9e15 while explicitly excluding the pending live measurement. The repair requires current document-fetch source bytes at the reuse boundary and refuses a mismatch before rights traffic, with library and real CLI regressions. CI rejects hidden runtime paths before upload so the file manifest and artifact cannot diverge silently. The active direct-acquisition runtime remains frozen to the reviewed head; it does not call reuse.

The same PR retains the Tool runtime already built and tested in CI for three days, with exact source head, SDK information and every file digest. The frozen runner checked the successful workflow, file manifest, assembly source stamps, Windows native SQLite and Git renderer bytes before the completed acquisition. The obsolete local-compile queue was stopped before publisher traffic. Final measurement and sizing are recorded above. No local compilation or test suite runs below the standing 4 GiB floor.

## EU consolidated bodies and states: merged foundation

The next checkpoint-bearing EU capture binds consolidated EN/FRA packages to the proven Cellar work/expression and its census relationship to a reviewed seed. An observed work CELEX is optional and never synthesized from the seed/date. A checked retained response (a2da37994258ed7249ca7957e7709f90a5484d7de908e21c7a7f1f2079d49052) confirms many dated works explicitly lack CELEX and also carries the treaty form `02016M/TXT-20151225`. Original seed identities remain separate. An unproven work still keeps `IdentityNotAdmitted`; other languages retain their existing unenumerated typed outcome.

The EU index is schema `lex-v3-europe-index/5`. Its `states` table has one row per census seed/work, including works with absent, unusable or ambiguous dates. `EuropeIndexReader.HasStates`, `ReadStates(seedCelex)` and `ReadStateExpressions(seedCelex)` expose the population and held expressions. Schema 5 article CELEX values may be null. The legacy resolver, expression reader and expression search expose only original legal text in schema 5. Consolidated bodies, with or without CELEX, remain available through the state-aware surface with their seed/Cellar identities. This prevents the existing API from resolving a work that its legacy expression reader cannot return. Original wording has no inferred consolidation date; a publisher consolidation date is not an applicability date. Same-date works remain ambiguous. Raw RDF terms, each P batch's evidence and the census evidence remain in `facts_json`. Existing article wording dates keep their original meaning. Exact schema 2/3/4 readers remain available, with `HasStates=false`.

Tests cover original and consolidated EN/FRA acquisition, checked replay, two separate offline CLI processes, whole-mount digest comparisons, multi-date ambiguity, and historical index/checkpoint compatibility. CI36889767805 at 0c985896 passed 5,923 tests with 0 failures and 22 skipped, including the prior query 2, population 1 and package 1 replay cases. The schema 5 fixed-input byte pin is `cb2b04fc0c50aaaaaacedd1261d38829bea8ce54667676a96552090baa7a9f7e`. The subsequent cross-family repair passed exact-head CI and merged, as recorded below. No live consolidated-population claim is made. Query checkpoint 3 and Formex population checkpoint 2 pin the new facts; older versions verify their original digest and acquisition policy.

PR #904's Luxembourg candidate passed CI36883780049: 5,904 passed, 0 failed, 22 skipped. It remains draft until a bounded partition is acquired, derived twice and measured. PR #904 has since imported merged #905 at e95ba92e; it passed CI36893963644 (5,936 passed, 0 failed, 22 skipped). Local fast/ingest tests remain deferred under the memory guard.

Claude's read-only review found the legacy resolver/reader mismatch and the API's fixed statements that no consolidated version is held. The one repair filters the three legacy identifier/expression surfaces to original text in schema 5 and tests every identifier entry (work, CELEX, expression, article and qualified provision) for consolidated fixtures with and without CELEX. Original EN/FRA resolution and search, state-aware expressions, and held source evidence remain tested. PR #905 merged as 256bef5b after exact 9fff439e CI36891324272 (5,924 passed, 0 failed, 22 skipped) and scoped Claude MERGE confirmation.

Cross-lane mount gate: capture and offline derivation of consolidated bodies may proceed, but a consolidated capture must not be served until the web lane replaces the API's fixed original-only statements and connects the state-aware reader. The current original-only EU rehearsal and historical indexes are unaffected. API/web changes remain owned by the web lane.

## Current data lane: complete offline mount command

`Lex.V3.Tool derive --custody <directory> --checkpoint <mount-inputs.json> --out <empty-directory> [--custody-encoding raw|brotli]`

`build` retains the input catalog and prints its reference-file path. `derive` reopens the complete EU and Luxembourg acquisitions, rebuilds the corpus and both indexes, writes the manifests, report and retained generations, and verifies the mount. The original acquisition clock and checked historical receipts preserve output identity; current custody holds are checked independently. No publisher session is opened.

PR #899's separate-process tests cover a raw first mount and a Brotli chained mount. Each compares every output digest with the original fixture derivation and a second process, traps network attempts, and verifies the exact file set. The predecessor directory is removed before replay. The read-only Claude repair confirmation returned MERGE. Final e576991e passed CI36882291778 (5,894 passed, 0 failed, 22 skipped), and PR #899 merged as 7a6c3d04.

The completed EU retry6 used its frozen runtime and predates these catalogs; it ended with the refusal recorded above. The next fresh capture must include consolidated EN/FRA packages: the active frozen runtime only admits original seed roots; the draft above adds proven consolidated identities. Non-EN/FRA expressions keep their typed, unenumerated language-out-of-scope outcome.

Luxembourg launch scope follows the owner's legislative-body family decision. Counts observed on 2026-10-01 are 93,161 subjects under the law prefix, 145,703 under grand-ducal regulations, 21,231 under codes, and 816,645 under the broader legislation prefix. These are IRI-prefix subject counts, not a legal-family census or body count. The candidate March 2017 law partition has S=219, A=1,414, G=114 from six bounded requests, independently reopened by digest. Acquire, derive twice and verify that partition before full-run sizing. Every excluded family still needs an explicit typed scope disposition.

Local fast/ingest tests remain deferred while the EU acquisition occupies the machine and free memory is below 4 GB. GitHub CI runs the full solution. Production signing, deployment and promotion remain outside this lane's authorization.

## Earlier component evidence

The entries below record earlier component checkpoints. Their statements about pending complete offline derivation are superseded by the current command above.

## Offline Formex package and annex derivation (Codex, 2026-10-01)

Retain ordered ZIP/PDF attempts, original annex profile identities, checked expression/corpus/CELEX
input digest, renderer and final outcome digest. Replay selects addresses from the same enumeration
and repeats the existing inventory/annex core using checked retained routes. Consume every saved
fetch/profile and reproduce typed outcomes and identities. Unexecuted attempts remain operational
refusals; no new observation or publisher request is invented.

All 32 new cases passed at reviewed 853b1906: CI 36863759397 reported 5,491 passed
and 22 skipped. Claude returned MERGE with no material findings. The one editorial repair
restores spacing and records that parent PR 879 is merged as 749e88a3. Fresh current-base
CI gates merge; local tests remain deferred while the long acquisition holds the heavy slot.
The input digest currently serializes the corpus for each expression; avoiding that repeated
work remains a measured performance follow-up. If checkpoint retention fails, acquisition is
reported as checkpoint_not_retained and carries no annex classification, even after a successful
fetch. This explicit refusal preserves the replay requirement.

Successful populations expose immutable package results tied to their exact outcome objects.
Complete acquisition composition and same-run object restoration still gate independent mounts.

## Retained Formex manifestation enumeration (Codex, 2026-10-01)

The retained checkpoint binds each expression to its original enumeration, renderer, plan and
result digest. Reopening checks both passes with current custody receipts and independently
rebinds the count queries. It preserves the original observation identities and sends no requests.
Page requests remain checked against retained evidence and the caller's run pin.

Exact f80bdee6 passed CI 36856566025 with 5,354 passed and 22 skipped, including all 17 original
cases. Claude found the production code sound but required the merged parent and current base.
One repair includes merged869/872 at c9e5a40a, checks a single pass value in 1..2 before conversion,
and refuses a literal-null root with an integrity exception. One new before-writes regression
brings this slice to 18 cases. Fresh combined CI gates merge; local builds/tests remain deferred
under the acquisition memory guard. The offline budget records zero sends with a synthetic ceiling
of two, not the historical wire allowance. Full population/acquisition and mount replay are separate.
## Offline EU document routes (Codex, 2026-10-01)

Reopen a retained document route using its original acquisition run, logical-request digest and
independently selected address. Check each hop's GET, representation, original write receipt and
exact body; reopen policy bytes by digest and require agreement across hops. The existing constructor repeats receipt, redirect and completion gates;
the complete canonical route must match the original bytes. No transport or observation is created.
Non-200 and incomplete routes retain their original outcomes, without a current retention claim.

All 19 cases passed at 00e95ae6: CI36856918011 reported5,336 passed and22 skipped, with
required watch exit zero. Claude returned MERGE with no material defect; the one repair includes
merged873/integration92c8df5a, clarifies digest-only policy reopening and original retained-route
scope. Fresh combined CI gates merge; local builds/tests remain deferred under the acquisition
memory guard. A containing catalog must preserve route references; complete offline mounts remain
outstanding. No current retention floor or population completion is claimed.
## Retained EU enumeration checkpoints (Codex, 2026-10-01)

Each delivered EU enumeration retains its comparison inputs in checked custody. Offline
reopening checks the original run/profile, both counts, all pages, request bytes, the binder
and complete two-pass comparison. The reader has no HTTP client and performs no writes.
Receipt restoration separately re-holds every receipt member to obtain the current store
floor. The request body and checkpoint are checked reads but are not receipt members; the
receipt floor alone does not establish future reopening of the entire checkpoint closure.

All 20 new cases passed at 101e362d. CI 36853853290 reported 5,318 passed and 22 skipped;
the required watch exited zero. Claude returned MERGE with no material code findings.
The one repair clarifies the receipt-member boundary and includes integration d874273b,
preserving both STATUS sections. Fresh final-head CI gates merge. Local Release/fast/ingest
checks remain deferred under the acquisition memory guard.

No saved success or protection flag is trusted as a proof. Complete acquisition composition,
Formex/rights/body outcomes and two independent offline mount processes remain follow-on work.
The active EU acquisition uses its frozen runtime and remains untouched.

## Compact Luxembourg object identities (Codex, 2026-10-01)

Object identity sets retain sorted 32-byte digests. Binary search preserves exact membership,
duplicate-set semantics and canonical bytes. Checked reopening enforces the original digest,
byte equality, ordering and refusal precedence. No publisher or custody admission changes.

All 27 new cases passed at b222687c: CI 36851145863 reported 5,305 passed and 22 skipped,
with 259 candidates and 139 guarded types after merging integration 65922f24. The required
CI watch exited 0. Claude reviewed that exact head and requested corrected documentation
counts; this is the one repair round. The code review found no material defect.

Integration d874273b is now included. Combined CI 36853595395 passed 5,324 tests with 22 skipped; only the candidate total
failed (260 versus 259). Its complete member pins passed. The candidate total is now
260; the passing literal pin contains 140 guarded types. Fresh CI gates merge. Local Release build, required fast tests, affected ingest checks and the synthetic
measurement remain deferred under the acquisition memory guard. No full-LU fit is claimed.
The remaining observation, scope and corpus graphs still require bounded derivation evidence.

## Streaming scope verification from source inputs (Codex, 2026-10-01)

The retained manifest can be verified by independently replaying its original source inputs
through the existing streaming reducer and canonical writer. Acceptance checks the pinned
digest, UTF-8, complete byte comparison, both source passes and writer admission/accounting.
It returns the existing digest/count receipt without materializing a second manifest graph.
The source factory and evidence resolver must come from original evidence; deriving either
from the manifest under test would be circular.

PR head b3cafcab passed CI 36850031289: 5,289 tests passed, 22 skipped, including all eleven
new scope cases. Claude returned MERGE with no material findings. The one editorial repair
clarifies exception precedence and working buffers in the API remarks and records the review.
Integration a74fece1 is included; final-head CI gates merge under the 10:35 standing order.
No local Release/fast/ingest run or synthetic source-replay measurement is claimed.

The measurement remains required before claiming a full Luxembourg memory fit. Working
storage includes five projection bytes per object, the evidence table, current input, the
JSON writer buffer and a 4 KiB comparison buffer. An individual JSON element may exceed the
writer's 64 KiB flush threshold. Factories and resolvers can retain more. This method is not
wired into the LU adapter and does not provide a bounded downstream manifest view.


## Async Luxembourg assertion snapshots (Codex, 2026-10-01)

The snapshot writer now consumes asynchronous verified rows through the bounded small-chunk
channel. Failed custody cancels the producer's linked token, including a source awaiting its
next row. Both writer paths share the record encoding and canonical digest. The source remains
responsible for publisher proofs, census membership, admission and subject grouping.

Eight cases cover equal synchronous/asynchronous bytes, empty input, independently reopened
small chunks with duplicates and literal metadata, failure during an awaiting source, absence
of a root after source failure, wrong observations, null rows and early cancellation. Exact
1f16080f passed CI 36852404133: 5,306 passed, 22 skipped; the required watch exited zero.
Claude returned MERGE with no material findings. The one editorial repair clarifies the
caller's proof checks and records this evidence. PR858 is merged as a74fece1 and included.
Final-head CI still gates merge.
No local build/test was run under the 10:35 low-memory rule. Adapter integration, complete
bounded derivation and full-LU memory measurement remain outstanding.


## LU assertion snapshot storage (Codex, 2026-10-01)

The immutable assertion snapshot uses the checked chunk reader. Opening validates the complete
retained sequence and builds a compact subject-digest/offset index. Lookups reopen and recheck
chunks, preserving row order, duplicates, literal metadata and cross-range dependencies. The
header binds the run, observation and ordered census/assertion proof references. Integration
must independently replay those proofs and check census membership before admitting rows.

Each serialized record, including the complete proof-reference header, is limited to 4 MiB.
Opening refuses noncontiguous repeated subject groups and digest collisions. The writer does
not detect repeated groups early. Integration must measure header size and proof count before
launch; an oversized header refuses without truncation. This remains unwired storage support.
Scope and corpus graphs still materialize, so no full-population memory or disk fit is claimed.

At 168fbe90, the Release build passed with zero warnings/errors, required fast tests passed
(3,105 passed, one skipped), and affected ingests passed (82 passed, two skipped). Twenty new
cases cover row preservation, malformed and corrupted storage, binding changes and cancellation.
CI 36850526856 passed exact 12be52d4 with 5,298 tests passed and 22 skipped. Its member-level
census and the combined totals of 259 candidates and 139 guarded types all passed.

Claude's read-only cross-family review returned MERGE with no material findings. The one
editorial repair records that evidence, removes two added byte-order marks and records the
header-size and writer-order limitations above. Integration c27012f4 is included; merge still
requires successful CI on the final head. The 10:35 UTC standing order permits that CI-backed
review during acquisition while memory is below 4 GB. No additional local build was run.

The asynchronous snapshot now uses the bounded small-chunk channel introduced by PR #855.
No publisher traffic, production action or completed population is claimed by this slice.


## Retained custody storage consolidation (Codex, 2026-10-01)

Reversible driver decision: preserve every cited historical custody path while consolidating
byte-identical files onto NTFS hardlinks to retained run 9. Eight bounded files passed first;
the expanded operation completed 971 more files at 03:56 UTC with no publisher traffic.
Every target/master was rehashed and independently read through the frozen product custody
reader before and after replacement. A flushed per-file journal records each intent and result.
An independent audit confirms all 971 file identities, path lengths and exact journal coverage.

Evidence: C:/lex-v3/lanes/old-eu-hardlink-expanded-20261001-result.json, its .jsonl journal,
old-eu-hardlink-expanded-fileids.json, and the bounded2 result and file-ID audit. The expanded
operation shared 2,754,376,458 logical bytes; free space rose from 4,960,497,664 to 6,416,429,056
bytes during the operation, with unrelated host writes possible. All original custody paths
and bytes remain. This does not enforce retention or provide redundant physical copies.
The next EU launch retains the full 6,374,424,214-byte allowance, checked again after freezing.

## EU escaped qualifier authority (Codex, 2026-10-01)

EU retry 5 at C:/lex-v3/eu-population-20261001-3 ended at 03:08 UTC with exit 3 after
1,358 of 20,000 wire requests. ReifiedAxiomDecodeRefused names seed 32007R0864 and
QualifierAuthorityDisagreesWithItsCode for `{MA/PART|http://publications.europa.eu/resource/authority/fd_335/MA%2FPART}`.
The frozen source is eed2b485. The guard did not stop the run: minimum sampled free space was
5,770,530,816 bytes and peak working set was 532,279,296 bytes. No complete population was built.

Offline reads through the frozen product custody reader found the exact type_of_date carrier in
held bodies 628d74398542b6e078e06262f56dd138e6ae4d0ddaa17d646ebd05d3fca8d7ac and
fcbf08f683ac984207f694f254e9b9216baba941a82f47867e30b077d1cb489f. The readback audit is
C:/lex-v3/lanes/eu-retry5-qualifier-type-held-rows.json; it is not a complete family replay.

Reversible driver decision: accept the exact existing authority form or the same fixed authority
base followed by the code escaped as one URI path segment. Keep the literal code and original
carrier unchanged. MA/PART receives no invented label or date role; the accepted table still
controls interpretation. Wrong bases, other concepts, double escaping and extra URI parts refuse.
Eleven contract cases cover the two forms and refusals. At c9d23643, Release build passed with
no warnings or errors; required fast tests passed (3,116 succeeded, one skipped), and affected
EU ingest checks passed (19 succeeded). The offline replay at C:/lex-v3/eu-qualifier-replay-20261001-2
used the product's strict parser on 24 retained date-axiom families, compared 3,390 first-pass rows
exactly with the second pass, and decoded all 1,166 parents without refusal. Five MA/PART bindings
kept the unrecognised-code outcome. No publisher requests were made. This diagnostic checks
retained row equality and decoding; it does not mint new enumeration proofs or a population result.
The first diagnostic accidentally selected amendment families too; its 44 wrong-family refusals
are retained in replay-1. The corrected selector matches the exact date-predicate VALUES block.

PR #859 received cross-family MERGE with no material findings. The reviewer reproduced the
build, fast tests and all 29 decoder cases, including a counterfactual where the old decoder fails
the two escaped cases. The one editorial repair records the exact ingest filter:
`FullyQualifiedName~EuObjectFacts|FullyQualifiedName~EuProduction|FullyQualifiedName~EuRepeatedEnumeration`.
The AU+TARD escaped case is constructed; MA/PART was observed. Literal matching retains priority,
and lowercase percent escapes still refuse safely. The next full run requires this change merged
and frozen in a fresh custody directory, with the measured storage allowance and disk guard.


## EU retry 5 and LU lookup storage (Codex, 2026-10-01)

PRs #848, #849, #851 and #852 are merged after cross-family reviews, one repair round and green
CI on their exact final heads. EU retry 5 ended at C:/lex-v3/eu-population-20261001-3,
with runtime and renderers frozen at eed2b485. It keeps all 82 seeds, EN/FRA Formex enumeration,
explicit outcomes for other languages, the 20,000-wire ceiling and bounded LU a439 companion.
The run uses the measured Brotli mode and the 6,374,424,214-byte starting allowance. Its owned
process guard retained the 3 GiB floor and 512 MiB stop margin. It ended at 03:08 UTC with exit 3
after 1,358 wire requests: ReifiedAxiomDecodeRefused for seed 32007R0864,
QualifierAuthorityDisagreesWithItsCode at MA/PART versus the fd_335 authority ending MA%2FPART.
Minimum sampled free space was 5,770,530,816 bytes; peak working set was 532,279,296 bytes.
The guard did not stop it. No completed population is claimed; retained bytes need a decoder audit.

Reversible driver decision for the next LU storage step: use existing digest-checked chunk
storage for random observation lookups. The draft adds a separate v2 root with exactly 64 KiB
chunks; the v1 writer keeps its 4 MiB format. Both readers verify the complete ordered sequence
before returning a stream and recheck custody on each chunk load. A seek drops the previous cache.
The canonical payload digest and per-chunk retention evidence remain separate from the root receipt.

The seven existing chunk obligations now run under both profiles, with nine additional cases for
profile admission, unread-tail corruption, an independent compressed-store reader, legacy root
bytes and the custody bytes loaded by 100 short random reads. At 873600fa, the local Release
build passed, required fast tests passed (3,105 succeeded, one skipped), and affected ingest
checks passed (170 succeeded, two skipped). CI 36808709344 also passed on that exact head.
The first CI run found three census omissions; explicit entries now account for the test wrapper
and the two new receipt-returning methods. No receipt constructor changed. No census assertion was loosened; two expected counts rose by
one for the new test store.
PR #855 received cross-family CHANGES REQUESTED for six existing STATUS lines damaged by
incorrect text decoding. The one repair restores those exact lines from the base and corrects
the census-count wording. The reviewer independently reproduced the build, fast tests and all
23 chunk cases. Existing readers now admit v2 roots, with the same content and retention checks;
only tests write that profile in this slice. No full-LU fit claim is made. Subject indexes, a checked
streaming scope-resolution door, independent bounded manifest reopening and corpus assembly
still need implementation.

## EU object-facts continuation range (Codex, 2026-10-01)

Full EU retry 4, frozen at source 18b53941 in C:/lex-v3/eu-population-20261001-2, refused with
ObjectFactsFamilyNotProven after 1,359 wire requests. In retained batch c4968f32, both COUNTs
are 678. Pass A has 678 rows; pass B has 613 + 72, including seven repeated keys below its cursor.
Both unique key sets are equal. The retained continuation query 88def2e2 explicitly excludes
those earlier rows. The proof correctly refused; no complete population or mount was produced.
Minimum sampled free space was 5,981,491,200 bytes. The disk guard did not stop the run.

Reversible driver decision: retain the original query predicate and add an equivalent nested
comparison that selects the first unequal cursor component. All six components are total strings.
Publisher rows are never discarded or deduplicated locally to make an invalid delivery prove.
The executor also checks the first continuation row against the prior cursor, so an overlapping
prefix refuses immediately. Three offline cases cover earlier, equal and valid later prefixes.
At 0ddc4795, the clean Release build passed with no warnings or errors. Required fast tests passed
(3,085 succeeded, one skipped), as did affected ingest tests (86 succeeded, two skipped).

The fresh production-executor probe C:/lex-v3/eu-object-facts-cursor-20261001-1 completed with
11 of 60 allowed wire requests. Both COUNTs are 678. Pass A has 678 rows; pass B has 613 + 65,
strictly ordered with no overlap. The family proof and independent retained-row reopen passed.
A separate audit rehashed frozen inputs, logical requests, rendered queries and response bodies.
Both complete row sequences are equal and match every binding from the failed run's pass A.
Minimum sampled free space was 5,411,053,568 bytes; peak sampled working set was 104,214,528 bytes.
The run contains retained-cursor-audit.json. Its summary SHA-256 is
`31d5912623f5376da63b0ea162f5265cbbc6dbd72ef99f7bfd787e337b82f06e`.
This proves the correction on the failing batch. Full population acquisition remains unfinished.
Cross-family review returned MERGE with no material findings and reproduced the local checks.
The other five query families retain their existing filter; a similar publisher fault there
would still refuse through the shared cursor and proof checks. Final head CI remains required.

The proposed raw-custody retry 5 keeps all 82 seeds, EN/FRA Formex enumeration, explicit outcomes
for other languages, the 20,000-request ceiling and the bounded LU a439 companion. It raises index
headroom from 25 to 60 percent for schema 4 source and digest rows: 1,274,491,700 bytes. Add held
bodies (656,160,664), packages (564,415,560) and metadata/runtime/other data (500,000,000): the
working allowance is 2,995,067,924 bytes. With the 3 GiB reserve and 512 MiB stop margin,
6,753,164,308 bytes must be free before and after freeze. This estimate uses retained physical
samples and fixed-index growth from 61,440 to 77,824 bytes; it is not a population size bound.
The plan and input hashes are in
C:/lex-v3/lanes/eu-population-retry5-sizing-proposed-20261001.json; it is not launch-ready.
The measured compressed-custody decision below supersedes this raw proposal for the next run.
Its required free space is 6,374,424,214 bytes, with the same reserve and stop margin.
No full rerun has started. Its source must include the reviewed cursor and schema 4 changes.

## Compressed local custody for future Luxembourg partitions (Codex, 2026-10-01)

Reversible driver decision: add an explicit Brotli mode to the create-only local custody store and
`--custody-encoding raw|brotli` to the build tool. Raw remains the default. Compressed objects use
`<original-sha256>.br` in the existing retention-class directory. Their 16-byte header contains
`LEXBR01\n` followed by the original byte length as a signed big-endian 64-bit integer; one Brotli
stream follows. Every receipt, reference, digest-only lookup and readback still names the exact
original bytes. Reopening this root requires selecting the same mode. No existing custody is
converted, and no retention enforcement is claimed.

Writes retain the bounded private input copy, flushed temporary file, atomic create-only publish
and independently decoded readback before any receipt. Reads bound the original and encoded sizes,
decode with a 64 KiB input buffer, reject incomplete/trailing/concatenated encodings, and check the
original SHA-256. The decoder uses the consumed/written counts and completion status documented by
[Microsoft](https://learn.microsoft.com/en-us/dotnet/api/system.io.compression.brotlidecoder.decompress?view=net-10.0).
The public read contract still returns one whole admitted object in memory. Compression reduces
retained storage; it does not make the global Luxembourg scope graph disk-backed.

The change adds both custody-obligation checks for both retention classes, empty/random/repetitive
round trips, concurrent/idempotent creates, nine corruption cases through both read doors and
create, cross-lane corruption, caller mutation/cancellation, occupied paths, CLI admission and an
offline two-work Luxembourg acquisition/corpus rebuild with a separate compressed-store reader.
Clean Release build passed with zero warnings/errors at 630485e4. Required fast tests passed
(3,105 succeeded, one skipped), and affected ingest passed (42 succeeded, two skipped), including
the separate-reader two-work acquisition/corpus rebuild. Cross-family review returned MERGE, reproduced these checks and added 248 independent size/pattern
round trips, including a 256 MiB object. Final exact-head green CI remains required. Full LU partition assembly and population sizing remain unfinished.

A separate generic Brotli measurement reopened the retained 65-row LU response
ff180f054a3f9bf6a8782b6cee3735ca95303f97565a07b1bab3e9f39e4b4dc9:
65,175 source bytes became 2,411 Brotli bytes (2,427 including the proposed header), and restoration
reproduced its original hash. Evidence: C:/lex-v3/lanes/lu-retained-response-brotli-sample-20261001.json.
This is one sample, not a population storage bound or an end-to-end partition measurement. The previous EU run ended with a cursor refusal; it produced no complete population.

The offline production-store measurement at C:/lex-v3/eu-brotli-sizing-20261001-1 reopened all
394 retained English bodies byte-for-byte through a separate reader. Their 536,661,019 original
bytes became 110,968,228 framed bytes. No publisher requests were made. Peak sampled working set
was 179,286,016 bytes; minimum sampled free space was 5,985,570,816 bytes.

Reversible driver decision: use this explicit mode for fresh EU retry 5 after the cursor and
schema 4 changes merge. Body allowance is 277,420,570 bytes (twice the measured English sample
for EN/FRA, plus 25 percent headroom). Keep packages at 564,415,560 bytes, index allowance at
1,274,491,700 and metadata/runtime/other at 500,000,000. Total working allowance is 2,616,327,830
bytes. With the 3 GiB reserve and 512 MiB stop margin, 6,374,424,214 bytes must remain free before
and after freeze. This historical sample is an estimate, not a population storage bound. No ZIP
or index compression savings are claimed. The guard cannot bound other processes' write bursts.
The plan and input hashes are in C:/lex-v3/lanes/eu-population-retry5-brotli-sizing-proposed-20261001.json.
It remains unready until required merges, source comparisons and free-space checks pass.

## EU XML-entry and article-text byte digests (Codex, 2026-10-01)

Reversible driver decision: the Formex producer hashes the exact decompressed ACT entry bytes,
including encoding, BOM and comments, and separately hashes its existing SearchableText as UTF-8
without a BOM or added newline. The bounded source-entry read checks cancellation and the declared
length. Semantic article identity remains unchanged. The ZIP digest remains a separate package
coordinate; source-entry digest is not taken from the separately acquired XHTML body.

Schema 4 stores one digest row for each indexed article and binds the rows in the logical hash.
Reopen verifies exact coverage, lowercase SHA-256 values, each text digest against stored text, and
consistent source-entry digests for articles from the same package and entry. Reader access exposes
these named coordinates without adding public quote, citation or verify operations. Reopening an
index does not independently reopen its source ZIP; that remains the original custody/replay chain.
Schema 2 and 3 mounts keep their exact schema, logical hash, provenance and capability checks;
the new digest capability is unavailable for those versions. Full EU retry 4 at frozen source 18b53941
refused ObjectFactsFamilyNotProven after 1,359 wire requests and produced no complete population index.
The next full run should include reviewed schema 4 directly; no coordinate is invented for old rows.

Tests add exact source-byte mutations (comments, BOM and line endings), an annex-only ZIP change,
an article text change, English/French producer-to-index binding, schema 3 LF/CRLF compatibility,
missing/substituted/version-mixed digests, and hostile logical restamping. The actual schema 2
bilingual fixture is retained. The synthetic mount ambiguity fixtures explicitly populate their
own synthetic source/digest rows. Two fixed-input schema 4 builds produced identical 77,824-byte
indexes with SHA-256 `44a6158077203b3729c2103f3efeb007e3b8d10cd59b6616ee1c50194b205f1c`.
The clean Release build passed with no warnings or errors. Required fast tests passed
(3,085 succeeded, one skipped), as did affected ingest tests (126 succeeded, three skipped)
at 0a65ede4.
Cross-family review and exact-head green CI remain required; no full EU completion is claimed.

## Luxembourg dependency lookup for partition derivation (Codex, 2026-10-01)

Reversible driver decision: separate one resource's WEMI/original-Act traversal from the current
in-memory dictionary. A run-wide subject lookup supplies the same breadth-first graph and original
assertion objects. Forward WEMI links may cross acquisition ranges; consolidation qualification
reads its unique original Act's own assertions. Missing targets remain missing, unrelated relations
are not traversed, and cycles do not duplicate evidence. A lookup that substitutes the requested
subject or run observation is rejected.

The acquisition adapter now uses this component. Eight new cases cover range boundaries, cycles,
shared descendants, literal/non-WEMI links, missing targets, original-Act qualification and lookup
substitution. Clean Release build passed with no warnings/errors; required fast tests passed
(3,085 succeeded, one skipped), and affected ingest tests passed (133 succeeded, two skipped),
including the existing population acquisition/corpus cases. This creates a dependency boundary
for disk-backed derivation; dictionaries and the complete scope graph are still retained. It does not establish full Luxembourg memory fit or
permit separate range corpora to be concatenated without complete enumeration verification.

## EU retry 4 evidence and continuation repair (Codex, 2026-10-01)

Full EU retry 4 ended with ObjectFactsFamilyNotProven after 1,359 wire requests from frozen
source 18b53941 in C:/lex-v3/eu-population-20261001-2. It requested all 82 seeds with a
20,000-request ceiling and produced no complete population. One retained object-facts batch
has equal COUNT values of 678 and equal unique row sets, but its second pass repeats seven rows
across the continuation boundary. PR #852 carries the correction and separate bounded proof.
The disk guard did not stop retry 4; minimum sampled free space was 5,981,491,200 bytes.

The preceding three-family maintenance census passed with 22 of 60 allowed wire requests.
Every attempt was zero, so that probe did not exercise a live retry. All proof rows and retained
HTTP body hashes were independently reopened. PRs #840 and #843 merged after cross-family
review and green CI on their exact heads. Retry 4 started with 6,653,554,688 free bytes against
the recorded 6,474,369,248-byte estimate, including a 3 GiB floor and 512 MiB stop margin.
The resource logs, refusal evidence and frozen input snapshots are retained.

## EU article source coordinates (Codex, 2026-10-01)

Reversible driver decision: schema 3 preserves the admitted Formex package SHA-256 and checked
original request URI for every indexed article. The logical index hash binds these source rows;
reopen requires exact article coverage. The reader returns work, expression, article, wording
date, language and package entry with those fields. Package SHA identifies the complete ZIP.
Article text/body digests, new public API operations and custody replay remain future work.

Schema 2 mounts retain their original file/logical-hash and capability checks, with source-coordinate
support unavailable. Schema comparison normalizes only CRLF/LF SQL spelling; new schema SQL uses
LF on every checkout. Tests include the exact 198-article bilingual schema 2 canary and a CRLF variant,
producer-bound source fields, hostile digest/URI/coverage/version changes, and complete synthetic
source rows in the existing ambiguity fixtures. The fixed-input index was derived twice identically:
61,440 bytes, SHA-256 153b5852a3c55ab85bf5064757638051a9f67c2109cdd16bedef6fce9aab7123.

After correcting the reproduced initial fixture/schema/census failures, validation at 53ceaffe passed
a clean build (39.43 s), required fast (3,085 passed, one platform skip, 73.580 s), and 105 affected
ingest tests (three skips: two live EU tests and the owner-named mounted temporal gate, 134.765 s).
Exact commands, initial failures and successful results remain under C:/lex-v3/lanes/eu-source-*.
Required Claude review at 7893b2e7 returned MERGE with no material findings. It reproduced fast,
the deterministic bytes and 108 ingest passes with three skips after the #839 integration refresh.
The single repair restores prose spacing. The reader accepts hash-bound absolute HTTP/HTTPS URIs;
the current producer only emits its checked Publications Office HTTPS route. Initial CI passed;
fresh exact-head CI remains required after this documentation repair and any base refresh.

Population checkpoint: EU retry 3 ended after 490 of 20,000 allowed requests, with 79 of 82 census
families proven and three retained HTTP 503 maintenance responses. PR #840 narrowly retries that
exact body within the existing four-attempt limit. It merged at a1752729 after final green CI.
A fresh 60-request three-family proof/reopen
must precede the next full run. The previous storage plan reserved only 1 GB; the panel's new
3 GB floor requires updated sizing and runtime guards before launch. No new full run has started. The new governor checks free space every 100 ms and stops only
its owned process tree below 3.5 GiB; scheduling delays and foreign writes remain limits. Both normal
child exit and an induced safe resource stop were tested. The current conservative retry-4 proposal
requires 8,026,491,262 free bytes before and after freeze; this host does not yet meet it.
Reclaimed 894,621,742 bytes of inactive ignored legacy Ingest/test build output; preserved source,
web, Git data, custody and frozen evidence. Further measured sizing and recovery remain required.
Offline Luxembourg run 2 measured 70,855,000 additional retained bytes for 9,000 synthetic subjects
and 61,000 admitted assertions through semantic/typed/scope stages. It excludes later stages and
relations and does not establish whole-population fit.

Panel answer Q-20261001-0049-codex: no spending or new worker; finish EU on this host first.
The completed scope audit checked Stage 1 S1-A01/S1-A02, accepted Luxembourg Candidate 6 and
issue #420's bounded acceptance (not a PR). Candidate 6 sections 1 and 6 require a resource row for
every finite-universe resource, including metadata/relations when bodies are excluded; its subject
universe cannot be defined by admitted records. Reversible decision: retain the whole metadata
census, and apply the existing never-ingest, pointer, quarantine and admission gates to bodies.
The measured S=1,986,924, A=9,672,378 and G=221,852 remain metadata planning counts, not acquired
body counts. Audit and authority hashes: C:/lex-v3/lanes/lu-launch-scope-audit-20261001.md.

Next Luxembourg work is bounded partition acquisition, derivation and independent verification,
followed by disk-backed assembly with global relation/original-Act dependencies preserved. Measure
one partition end to end first. Partitioning is not implemented yet. Use 3 GiB as the conservative
free-space floor and stop our own run with a margin before it. No production signing, credentials,
deployment or promotion.

## Retained EU maintenance response retry (Codex, 2026-10-01)

The fresh full EU retry ended after 490 of 20,000 requests with 79 of 82 census families proven.
Three refusals bind HTTP 503 to the same retained 2,005-byte maintenance page for
12016E/TXT, 32022L2555 and 32023R1115. Its SHA-256 is
e7fab335ce5367cfe359f9f7e0ad6ce1838bec9189a216bc3faf437ce169d404.
The run remains refused; custody and resource samples are preserved at
C:/lex-v3/eu-population-20261001-1. It produced no Formex or full population result.

Reversible driver decision: permit the existing four-attempt retry only for a complete 503 from
the exact Publications Office SPARQL endpoint with that independently reopened body digest.
Retain each failed route, charge every wire attempt, and keep the session's 1, 2 and 4 second
backoff. This adds no Retry-After handling. Longer maintenance still refuses after the existing
attempt allowance. Different bodies, challenges, statuses and publishers keep their refusal paths.
The 503 test double labels the body text/plain; recognition depends on the complete body digest,
not media type. No recovery during a real maintenance window is claimed.

Validation at 8fad46c9 passed a clean build (44.91 s), required fast suite (3,085 passed, one
platform skip, 65.046 s), and 137 affected ingest tests (two live EU skips, 51.204 s). The ten new
cases cover count/page recovery, attempt lineage and route custody, exhaustion, wire budget,
changed bytes, wrong statuses and the exact page from Luxembourg. Claude's required review of
5cfba613 returned MERGE with no material findings, reproduced these checks and caught four
mutations when the digest guard was removed and two when the endpoint guard was removed.
The one repair corrects prose and records the retry limits; production and test code are unchanged.
Initial CI36792824118 passed; the refreshed head still requires green CI before exact-head merge.
Commands, results and review remain under C:/lex-v3/lanes/eu-maintenance-* and reviews/claude-pr-840-*.

Before another full attempt, prove and independently reopen the three affected census families in
fresh custody with a shared 60-request ceiling. Keep at least 3 GB free during runs, as the panel
ordered. No production signing, credentials, deployment or promotion.

The separate offline Luxembourg diagnostic at merged 7144666c used no publisher traffic. Its
largest synthetic case retained 70,855,000 additional managed bytes across semantic, typed and
scope results for 9,000 subjects and 61,000 admitted assertions; OS peak working set was
160,976,896 bytes. Evidence: C:/lex-v3/lu-combined-graph-20261001-2. It repeats one assertion
pattern, excludes relations and later stages, and establishes no full population fit. The initial
probe's unadmitted-predicate refusal remains preserved with its frozen source and runtime in run1.

## Luxembourg identity canonical output (Codex, 2026-09-30)

The previous observed-object identity writer built a full canonical buffer and copied it again for the
schema-domain digest. Reversible driver decision: write directly to the caller's stream and hash
incrementally, with periodic JSON flushes. Preserve exact ordering, escaping, newline, digest,
caller ownership and destination prefixes. Document partial writes and flush failures explicitly.
Persistence and canonical round-trip comparison use their owned MemoryStream backing memory
instead of copying it with ToArray. The independent reader and persistence store remain buffered;
this does not establish full-run fit.

Two regressions cover 3,000 identities on a nonseekable stream, maximum observed write size,
independent canonical bytes/hash/readback, prefix handling and stream ownership. This slice is
prepared from current integration using the preserved draft. Validation at fb30816a passed a clean
Release build (43.22 s), required fast (3,085 passed, one platform skip, 65.379 s), and 124 affected
ingest tests (two live EU tests skipped, 81.681 s). Exact commands and results are retained under
C:\lex-v3\lanes\lu-identity-stream-*. Required Claude review returned MERGE with no material
findings. It reproduced the fast and affected suites; adding the third caller, Stage3EvidenceLineage,
passed 134 ingest tests with two live EU skips. Restoring the base writer made the new bounded-write
regression fail as intended. The documentation repair now states that every JSON flush forwards
Flush to the caller's stream, including the last JSON flush before the newline. All production
callers currently own MemoryStreams. No production behavior changed after review; final CI remains
a merge gate. No measured whole-run memory-fit claim is made.

## Retained EU transaction deadlock retry (Codex, 2026-10-01)

The fresh full-EU retry2 refused after 494 requests with 81 of 82 census families proven.
The retained HTTP 500 body for 32023R2854 names a Virtuoso 40001 transaction deadlock
(SHA-256 70769075fe4617e11288eda6ac3120c1b10b7f5e64d90149ff9e8c28431b3627).
Reversible driver decision: retry only complete 500 responses from the exact Publications Office
SPARQL endpoint whose hash-checked first line carries that signature. Use the existing plan item's
four-attempt limit, session backoff and shared wire budget. Keep every recognized failed route in
custody, including the last attempt, and require ordinary full proof for the successful response.

Other status/body failures, malformed successful replies, capacity errors and challenges retain
their refusal paths. The draft includes the actual retained deadlock fixture and ten new test cases for
count/page recovery, same-request attempt lineage, failed-route custody, exhaustion, budget and
nonretry cases, including the same signature from Luxembourg. Validation at 4a37dc2b passed clean build (39.51 s), required fast (3,085 passed,
one platform skip, 66.989 s), and 126 affected ingest tests (two live canaries skipped, 53.799 s).
Exact commands/results are retained under C:\lex-v3\lanes\eu-deadlock-*. Required Claude review
returned MERGE with no material findings and reproduced the focused tests and fast suite. The one
repair adds the missing Luxembourg endpoint regression and records that failed-route digests are
currently discoverable by scanning custody; they are not linked from the final refusal or receipt.
The review stopped its optional full local ingest run after 21 minutes because another lane's
review was competing for memory: 1,774 passed, four skipped, no failed tests, incomplete suite.
Initial exact-head CI36784354147 passed 5,105 tests with 19 skips. The repair build, required fast and affected ingest validation passed; final
exact-head CI passed and PR837 merged at 40004a69. The prior run remains refused and its custody is preserved. Fresh acquisition follows the
bounded census and storage gates recorded in the lane notes.

The next recovery run will use fresh custody and a shared 20-request ceiling to prove/reopen only
32023R2854's census. The following full EU attempt keeps the 20,000-wire ceiling and original
4,999,959,422-byte free-space allowance before and after freeze. A byte-verified transparent
compression pass over local SDK/tool files is queued after reviews; no cited evidence is deleted.
Prepared launchers enforce those gates and preserve any refused run. Full Luxembourg still lacks a measured whole-process/storage fit.

## Publisher annex IDs and subtitles (Codex, 2026-10-01)

The completed three-seed run retains English and French 2024/1620 XHTML with Roman annex IDs
(including a nonbreaking space) and two title paragraphs. The previous parser admitted only numeric
IDs and one paragraph. Reversible driver decision: recognize canonical uppercase Roman IDs with
the observed optional nonbreaking space, preserve the exact publisher ID, and concatenate a title
and subtitle in publisher order as the independently retained Formex TITLE.Value does. The binder
must still match complete package entries and exact titles; no annex or PDF admission is bypassed.

The draft includes four exact retained XHTML/ZIP fixtures with SHA-256 pins and tests comparing
both languages' titles against their Formex entries, plus valid/invalid identifier cases. Validation
at 752f6ff3 passed clean build (41.41 s), required fast (3,085 plus one platform skip, 97.513 s),
and 106 affected ingest tests (two live skips, 44.695 s). Commands and results are retained under
C:\lex-v3\lanes\eu-annex-publisher-*. Required Claude review returned MERGE with no
material findings. It independently reproduced the 106 ingest passes and fast suite, and exercised
both production inventory producers on the retained English and French fixtures: exact titles
agreed in both languages. Empty or whitespace-only title paragraphs now refuse explicitly; the
joined title is used for binding and identity, not display. PR836 merged at 24c59015 after all
exact-head checks passed (CI36785179485).
This fixes a concrete parser limit;
it does not claim to explain all 32 prior package-not-acquired outcomes or complete the annex chain.

## Corpus persistence in bounded custody objects (Codex, 2026-09-30)

The corpus writer still held one complete canonical byte buffer after the stream-reader change.
Reversible driver decision: sets larger than 4 MiB use the existing ordered chunk closure; small
sets keep inline storage. Both paths preserve the same domain-separated logical set digest.
The public reader independently reopens legacy inline sets and chunked sets, verifies closure and
canonical bytes, and retains its three typed refusal categories. The storage root receipt identifies
the root; the write result's retention class takes the weakest observed class across data chunks,
retained receipt-evidence objects and the root. It does not infer the closure floor from the root.

Validation passed at 9fa26e4e: clean solution build (86.96 s), required fast (3,083 passed,
one platform skip, 64.814 s), and 93 affected ingest tests (two live canaries skipped, 79.674 s).
Six new cases cover exact bytes and independent public reopen, bounded custody writes, data /
receipt-evidence / root retention differences, missing/reordered/substituted chunks, wrong set
references, unavailable custody and failed holds. Independent reflection preserves 137 guarded
Ingest types and 86 receipt producers; the pins add the private chunk writer and the existing
helper's receipt observer parameter. The six declined types are unchanged.

The exact commands and results are retained in C:\lex-v3\lanes\corpus-chunk-validation-commands.json
and corpus-chunk-validation.json. Required cross-family review and green exact-head CI remain
merge gates. Typed records and completion remain materialized; this is not a full Luxembourg
fit or population-completeness claim. No publisher traffic was sent by the validation.

Required Claude review #832 returned MERGE after a clean build, fast (3,083 passed, one
platform skip), and 140 broader ingest passes (two live canaries skipped). Its two findings are
addressed in the single repair: intact foreign chunk roots now return RetainedBytesAreNotThisSet,
matching foreign inline objects, with paired regression coverage; STATUS is strict UTF-8 again.
The code/test repair is commit 816d4f09; its title mentioned the encoding repair prematurely,
which is completed in this follow-up after a failed text-edit script.

Large roots include retained receipt digests and write times, so their physical addresses may
vary between executions. Canonical record-set bytes and the logical SetRef.Sha256 remain stable;
the existing raw-storage equality test explicitly covers small inline sets. Opening and independent
verification reread chunks, and all writes include a canonical sizing pass. This adds I/O and
requires measurement before any full Luxembourg fit claim. Repair validation and exact-head CI
remain required; no production Azure operation or publisher traffic is part of these checks.

Repair validation at 45de557f passed clean build (75.17 s), required fast (3,085 passed,
one platform skip, 65.608 s), and 140 broader ingest tests (two live skips, 87.773 s).
The subsequent full CI run 36782023846 found two custody-store census failures: the new
ChunkReadFaultStore test double was not declared exempt. The affected local filter and review
had omitted that census. This follow-up names its constructor-configured corruption/timeout
behavior and updates the observed inventory from 28 to 29 stores (nine driven, 20 exempt).
Production behavior is unchanged. Full CI passed after the test-inventory correction; PR832
merged at 5d6055ca after the final base refresh and green exact-head checks.
The live all-82-seed EU retry began at 21:51 UTC from merged 7e90e943, in fresh custody at
C:\lex-v3\eu-population-20260930-2. Its 20,000-wire ceiling, frozen inputs and unchanged
storage allowance were verified. It ended with exit 3 after 494 requests and 81 of 82 proven
census families: seed 32023R2854 returned a retained Virtuoso 40001 transaction deadlock. PR837
adds a narrowly bounded retry and has a MERGE review; a fresh bounded census run must precede the
next full attempt. Full Luxembourg remains resource-gated.

## Luxembourg scope input reuse (Codex, 2026-09-30)

Scope resolution retains four rule-evaluation objects and multiple not-applicable selectors per
resource even when their values are identical. Reversible driver decision: reuse only immutable
values with no object identity or observation/evidence ordinal, within one profile resolution.
The table is bounded by four axes and seven terminal states (at most 28 projections), plus four
not-applicable selectors. Evidence-bearing selectors retain their existing per-object construction.
No static cache crosses runs or profiles; projection rules, evidence admission and ordering stay
unchanged.

Validation: clean Release build (85.99 s), `pwsh -File eng/test-fast.ps1` (3,075 passed,
one platform skip), and 97 affected Luxembourg ingest tests passed. Two regressions cover 128
objects sharing immutable values, isolation between resolutions, repeated serialization and mixed
missing/accepted dispositions with exact evidence. Independent reflection kept the global census
at 211 types and supplied the changed private producer signature pin.

The frozen comparison at `C:\lex-v3\lanes\lu-scope-input-comparison-20260930\comparison.json`
checks actual loaded assembly hashes and source stamps for baseline 66371006 and candidate
6209ce9d. For the same 10,000 synthetic empty subjects, retained scope memory after GC fell from
27,854,264 to 19,280,128 bytes (8,574,136 fewer, about 30.8%). Both diagnostic JSON files have
SHA-256 `c03f7c39759b4043581cddb58e69e60a789ff2978d4576f4ec6ebb9d0653f643`.
Projection instances fell from 40,000 to four; not-applicable selectors from 100,000 to three.
This measures retained synthetic scope results, not transient peaks or full-process fit. Real
assertion graphs, reduction, corpus persistence and indexes remain outside this measurement.

The full Luxembourg run remains gated on retained-state and persistence sizing. The new disk
standing order requires space recovery before a large run; cited custody and evidence remain
preserved. Required cross-family review and green exact-head CI remain merge gates.

Required Claude review #829 returned MERGE with no blocking defect. It checked the cache keys,
immutability and per-resolution isolation, reproduced the memory figures and identical JSON
hashes, and made the disposition regression fail by mutating the cache key. Its clean build and
fast suite passed; its broader offline ingest selection passed 137 tests. A trial integration merge
also passed build and fast (3,078 passed, one platform skip).

One follow-up preserves both STATUS sections during the current base merge. The earlier 97-test
log lacks its filter string, so that historical count alone is not a reproducible command record.
The explicit affected ingest rerun passed 101 tests, zero failures/skips, in 74.919 s at
2678415d. Its command, source and result are retained under C:\lex-v3\lanes\pr-829-explicit-ingest-*.
The exact filter was:
`FullyQualifiedName~LuxembourgQueryExecutionAdapterTests|FullyQualifiedName~LuxembourgProductionTopologyTests|FullyQualifiedName~GuardedConstructionCensusTests|FullyQualifiedName~CustodyStoreConformanceTests`.
The result is recorded in the PR. The final base refresh includes merged #827 and #830, with
both STATUS sections retained. PR #829 merged at 21:33 UTC after green exact-head CI.

## Formex package outcome diagnostics (Codex, 2026-09-30)

The successful three-seed mount holds 34 EU XHTML members but admits only two Formex article
expressions; 32 members report package_not_acquired. Reversible driver decision: print the existing
reconciliation as one retained CLI JSON line, with every expression's identity, language, declared
wire outcome/reason codes, observed status and detail. Acquired packages include the retained ZIP
digest. A refused population has no reconciled per-expression outcomes: its diagnostic records
the refusal, detail and observed enumeration count, with unknown totals/outcomes null. It does not
reconstruct individual package outcomes after reconciliation fails.

Initial validation at a53ae771 passed a clean build (93.27 s), fast (3,076 plus one platform skip),
and all 29 Formex population/first-mount ingest tests. Required Claude review #830 returned MERGE:
clean build, the same fast and 29 ingest passes, and two expected failures after mutating unknown
totals and package-refusal projection. No request, retry, admission or proof behavior changes.

The single repair uses declared wire codes, adds explicit 404/unavailable diagnostic assertions,
and corrects the test description: the annex case lacks the publisher's annex XHTML convention;
it is not a missing main-body case. The 500 case tests route_refused. Direct acquisition_refusal
projection remains unexercised by these scripted cases. Existing checks also cover acquired,
ineligible, language-out-of-scope, invalid-package and escaped refusal detail. Repair build
(81.91 s), fast (3,083 plus one skip) and 29 ingest checks passed; #830 merged at 21:23 UTC
after green exact-head CI. The full EU retry remains subject to its
storage allowance; cited custody and evidence are preserved.

## Corpus record-set canonical streaming and readback (Codex, 2026-09-30)

The full Luxembourg path still creates a complete corpus record set. Its canonical writer used
a whole MemoryStream/ToArray copy, and independent readback added a full UTF-8 string and another
canonical buffer. Reversible driver decision: write records through an incremental domain hash
and reopen through a strict stream reader. Preserve field order, newline, digest, destination
prefix and caller stream ownership. Flush at record boundaries after64KiB pending, allowing
one-record overshoot; this is not a fixed bound on arbitrary records.

The new reader checks the original digest before strict UTF-8, uses the same typed constructors,
compares canonical output against a fresh read, requires EOF and pins the final digest. Both
inline custody reader and writer wrap existing retained arrays when possible. Independent
readback still constructs a new typed record set; it does not trust a graph from the writer.

Validation: clean solution build (0warnings/errors,100.31s); required fast3,080passed plus one
platform skip (64.133s); all40 affected CorpusRecordSetWriter and EU/LU/V3 FirstMount ingest tests
passed (49.780s). Seven new regressions cover a3,000-record nonseekable destination, independent
hash/readback, destination prefix, short reads/ownership, wrong digest/noncanonical/UTF-8/EOF,
typed ordinal invariants, canonical substitution and stream preconditions. Existing fixture
byte/digest pins remain unchanged. Independent reflection confirms211guarded types, with only
ParseAndVerifyStream added to VerifiedCorpusRecordSet's global/per-type construction pins.

The required Claude review returned MERGE at a4d6fbc9 with no material defect. It reproduced
the clean build, fast suite and all 40 affected tests. It passed 52 tests with broader readback
and lineage coverage, and compared readers over 15,039 inputs with zero disagreements (247
accepted by both). A 5,000-record set also produced
identical canonical output. The one follow-up uses the persistence MemoryStream backing memory
instead of ToArray, documents partial output/flush on failure, and refreshes through merged824.
Repair validation passed build/fast (3,083 plus one skip)/40 affected ingest; #827 merged at
21:10 UTC after green exact-head CI. Persistence still buffers one complete
canonical set and retains reopened bytes; typed records/completion also remain. Chunked record-set
persistence and measured downstream resources remain required before full Luxembourg. No full
population-fit claim, publisher traffic or production operation follows this change.
## DATA review and fresh population evidence (Codex, 2026-09-30)

The required Claude review of #824 at99a8e29d returned MERGE, with no blocking finding.
It independently rebuilt (0 warnings/errors,68s), passed fast (3,076 plus one platform skip,68s)
and all97 affected Luxembourg ingest checks (69s). Forcing the body-role bit true failed the new
partition regression; the reviewer restored source and rebuilt. This one follow-up corrects the
adapter comment and lists both stream entry points in the verified manifest documentation.

Readback tradeoff: hold-time verification compares custody bytes with the held immutable graph
and repeats evidence admission; independent deserialization happens on later custody replay.
Both independent parser doors remain available, and the retained-run topology replay passed.
No writer/parser disagreement was reproduced; no full-process memory claim is made.

The fresh three-seed retry (32019R2088,32024L1760,32024R1620 plus Luxembourg a439) completed
at20:07 UTC with506/600 requests in38m39s. It proved399EU expressions:34Formex enumerations
eligible and365explicit language-out-of-scope outcomes. Luxembourg produced10records. All five
mount files were built twice identically and reopened. The read-only audit checked their hashes,
rights batches and323retained HTTP observations on Publications Office/Legilux only.

Coverage remains explicit:34EU XHTML bodies were held, but only32019R2088ENG/FRA has admitted
Formex articles (20 each). The other32 held bodies carry formex_main_body_package_not_acquired;
those outcomes are not article-index coverage. The six retained ZIP candidates include the two
2024 acts' annex files; their exact package refusal causes are not reconstructed by this audit.
Evidence: C:\lex-v3\eu-three-seed-retry-20260930-1, report SHA
`e15f7a247c7e813d7b0ab5d918c9f17c68c4ae53339a5db988a52aa3c8d270a4`.

Reversible driver decision: prepare a fresh all82-seed EU retry with the existing20,000-request
ceiling, EN/FRA-only Formex enumeration and typed outcomes for every other language. Use the
larger package sample107,385bytes with the original4x factor, retain the larger earlier index
sample, and require4,999,959,422free bytes including reserve before/after runtime freeze. The
plan in lanes/eu-population-retry-sizing-proposed-20260930.json remains unlaunched until queued
heavy work finishes and clean merged source/storage guards pass. These factors are estimates,
not bounds; neither full population is yet proved. No served mount or production setting changes.

## Reuse verified scope objects during retained readback (Codex, 2026-09-30)

Even after chunked storage, parsing scope JSON again constructs a second complete typed graph.
The writer already holds a verified scope. Reversible driver decision: compare retained canonical
bytes against that object while re-running the reducer's complete enumeration, selector and rule
evidence checks. Initial digest/strict UTF-8 checks, exact canonical byte comparison and final digest
pin stay shared with the independent parsing reader. The returned wrapper shares the immutable
manifest, and the independent reader remains available when no verified object is already held.

This change builds on merged PR #820 and compiles with zero warnings/errors (71.99s).
Regressions cover object sharing, fresh evidence refusal, foreign canonical bytes,
noncanonical whitespace and substitution of another valid document between read passes. Construction
pins were independently reflected: the verified type adds one public comparison door and one
private shared comparison helper. Its global type count remains 211. The reducer's private
scratch-layout constant makes it a census candidate, explicitly classified as not a token registry
or vocabulary (610 candidates, 53 declined). All 97 affected Luxembourg ingest checks passed
(67.582s). The initial fast run failed four census checks; corrected fast passed all 3,076 tests plus
one platform skip (57.694s). Required review and exact-head green CI remain merge gates.
The original typed scope graph and corpus record-set persistence remain full-Luxembourg constraints.

The same draft also replaces the verifier's retained per-row axis-result objects with five bytes
per row (four dispositions and the exact accepted-body-role bit). Accounting compares streamed
ordinal sequences, avoiding whole expected-partition arrays. A 32-object mixed-disposition fixture
cross-checks all 16 partitions against the unchanged builder, role-gated body membership and a
corrupted partition refusal. No measured savings or full-process memory bound is claimed.
## EU population census refusal diagnostics (Codex, 2026-09-30)

The fresh all-82-seed EU run at `C:\lex-v3\eu-population-20260930-1` ended with
`CensusFamilyNotProven` after 487 of 20,000 wire requests. Custody remains intact. Offline inspection
found complete four-input sequences for 79 seeds, an interrupted sequence for 32019R2088, and no
query inputs for 32024L1760 or 32024R1620. Retained service-unavailable and maintenance pages
suggest transient publisher failures, but the old CLI summary did not preserve exact per-seed
refusals. No complete EU population or full Luxembourg result is claimed.

Reversible driver decision: include each requested CELEX and the executor/proof refusal in the
existing run detail, with available request/attempt ordinals, status, retained body digest, media
type, count, offending key and underlying detail. Bind the seed before an early robots refusal
loses that association. Keep the existing refusal code and all proof, robots, budget and transport
rules. No automatic retries are added. The Tool already retains this detail in its build log.

Regressions cover two denied seeds before any query, unequal proof passes and the count-cap
refusal. After review and merge, the next live check uses only 32019R2088, 32024L1760 and
32024R1620, fresh custody and a 600-request diagnostic cap. It keeps the conservative complete-EU
storage allowance (4,965,958,358 free bytes including reserve). This cap does not guarantee
completion. Preserve and diagnose another refusal; do not add automatic retries. A successful
bounded run may enable a fresh all-seed run with the existing 20,000-request plan. Source/runtime
freeze, evidence hashes, storage checks and the single-heavy-job rule remain launch gates.
Validation: solution build has zero warnings/errors (74.76s), required fast has 3,071 passed
plus one platform skip (67.094s), and all 67 affected EU adapter/first-mount tests passed
(66.964s). Claude's required review returned MERGE at 346909cb with no blocking or should-fix
findings. It reproduced all checks; replacing the implementation with the base version failed
all three diagnostic regressions. The one review follow-up records that evidence and refreshes
through merged #820. Exact-head green CI remains the merge gate. Empty refusal arrays can
honestly accompany a duplicate family-key shortfall, which the requested/proven counts explain.
Long JSON diagnostics and escaped characters remain parseable retained log output.

## Large derived scope artifacts (Codex, 2026-09-30)

The required fresh-context Claude review returned MERGE at efa4092b. Its one documentation
finding is repaired below. Independent review rebuilt cleanly, passed fast (3,073 plus one
platform skip), all 29 focused checks including the corrected census partition totals, and
106 adapter/resolver/Stage 3 checks. PR #820 merged at 5d060767 after green CI on 4955f953.
Receipt-evidence retention and a public production reopen API remain future release work;
this slice does not claim a retention floor for the complete artifact closure.

Full Luxembourg acquisition is sized at 1,986,924 subjects and 9,672,378 assertion rows, but
population delivery remains unproved. Until this change, scope JSON was held as one custody object, whose
contract limit is 256 MiB. Reversible driver decision: preserve the complete canonical scope while
storing its bytes in ordered 4 MiB custody chunks above a measured 4 MiB inline threshold.
Small scope documents retain their current custody representation and logical identities.

The writer uses a bounded channel between canonical serialization and asynchronous custody.
Each payload and its actual canonical write receipt are held and digest-checked before publishing
the root. The root binds order, byte lengths, complete raw digest and canonical identity. Reopening
checks every receipt/payload binding and complete ordered content before exposing a seekable
stream. Each new read pass checks custody again; PR #819 supplies semantic and exact canonical
readback. A root's retention class never substitutes for the independently retained chunk receipts.
Content-derived UUIDs use incremental hashing with the same scope separator and UUID format.

Regressions cover chunk boundaries, equal repeated chunks, missing/reordered chunks,
foreign receipt substitution, a failed consumer releasing its blocked writer, custody loss on a
new pass and mixed retention classes. A 3,003-subject production fixture must replay both scope
manifests and both rights channels. The draft compiled with zero warnings/errors (40.76s), and
fast passed 3,073 tests with one platform skip (64.773s). It includes PR #819's repaired stream
reader. All 108 affected ingest checks passed: 27 storage/topology/census checks (39.493s) and
81 adapter/resolver checks (57.322s). Two independently transcribed census pins add one guarded
type and three receipt holders, with no removed entries. The large fixture declares xml-akomantoso;
its initial plain-XML-specific assertion argument was corrected. Both rights channels and complete
canonical byte replay pass. Full CI found one further stale census total: the independently
reflected new guarded type raises candidates from 254 to 255 and guarded types from 136 to 137.
Those totals are corrected; other partitions are unchanged. CI recorded 5,081 passes and 19 skips
with that one failure. Local census revalidation remains queued behind acquisition.
The required cross-family review follows the full EU acquisition to
respect the one-heavy-job limit; the draft will not merge before that review and exact-head CI.
Record-set persistence and retained typed scope objects remain further limits; this is not a
full-run memory or storage bound and does not authorize a population-completeness claim.

## Scope manifest stream readback (Codex, 2026-09-30)

Full Luxembourg sizing exposed both retained scope objects and complete serialized buffers.
Reversible driver decision: add a strict seekable-stream reader and use it for Luxembourg scope
readback. It checks the original domain-separated digest before deserialization, admits the same
complete-enumeration and selector/rule evidence, and compares freshly written canonical bytes
against the retained stream with fixed read buffers. Canonical output flushes after array
elements once 64 KiB is pending; one element or scalar can exceed that threshold. A second digest pin rejects a different valid
canonical document substituted between passes. The existing span reader remains available.

The production custody restore already freezes its byte array; a read-only stream reuses that array.
This removes the complete UTF-16 JSON string and canonical reserialization copies from this path.
It still materializes typed scope objects and custody bytes. Individual custody objects remain
limited to 256 MiB; segmented derived-artifact persistence and further retained-object sizing are
still required before the full Luxembourg run. No launch or memory-fit claim follows this change.

Regression drafts cover short reads, identical canonical output, caller ownership of the stream,
digest failure precedence, malformed UTF-8, noncanonical spacing/trailing bytes, changed canonical
input between passes and independent evidence refusal. Validation passed: solution build with zero
warnings/errors (39.09s), all 97 affected ingest checks (65.434s), and fast 3,070 passed/one platform
skip (51.563s). The independently reflected construction census adds the new verified-reader door;
its initial stale pin was corrected before the successful fast run.

The required cross-family review reproduced a material writer-buffer issue: Utf8JsonWriter held
an entire 4,098,427-byte rewrite until its final flush. The one repair flushes population arrays,
including ordinal arrays and both canonical writer paths, while preserving exact bytes/digests.
A 2,000-object regression checks write size, equality between writers, and strict stream reopening.
Fresh repair build passed with zero warnings/errors (10.09s); fast passed 3,071 tests with one
platform skip (55.629s), and all 98 affected ingest checks passed (66.556s). Removing the
periodic flushes makes the regression fail with a 4,088,207-byte write. An initial repair run
reused that mutation assembly due to a restored source timestamp; its failure log is retained
and excluded. The successful run rebuilt the restored source. One census line change only rewraps the same renderer string.

## Bilingual canary and full EU launch decision (Codex, 2026-09-30)

The fresh bounded GDPR/Luxembourg canary completed at 18:18:34 UTC, exit 0, in 18m08s with
191 of 800 wire requests. Its merged source is b0923cb0. It proved 91 EU expressions: six had
Formex enumeration and 85 retained `not_enumerated_language_out_of_scope` with unknown
eligibility. The mount holds one English and one French expression with 99 articles each.
Luxembourg contributed ten records. All five outputs built twice identically and reopened.
The retained-route audit found no EUR-Lex requests. This proves the bounded canary only.
Evidence: `C:\lex-v3\bounded-en-fr-canary-20260930-1`; build-report SHA256
`dc7d5b5bee57090a6ab544e4065b78e2890bf8e7175141d33e0e913a3b83e492`.

Reversible driver decision: launch a fresh all-82-seed EU union run after the stream-reader
repair is merged and private bilingual API verification passes. Keep Luxembourg bounded to a439
in that run. The retained sizing plan allows 20,000 wire requests and requires 4,965,958,358 free
bytes including a 1 GB reserve, checked before and after freezing the merged runtime. Historical
1,314 served expressions imply 7,884 Formex enumeration requests; all other languages keep the
typed outcome above. Counts, package sizes and index sizes are planning samples, not bounds.
The new run must prove its own union and every discovered outcome. Custody will be fresh;
this decision does not change the served mount or authorize production promotion.
Sizing evidence: `C:\lex-v3\lanes\eu-population-request-budget-ready-20260930.json`.
Full Luxembourg remains gated on retained-object and derived-artifact persistence scaling.

## Luxembourg typed assertion memory (Codex, 2026-09-30)

The measured assertion count is 9,672,378. Typed projection previously built eight whole-population
sort-key arrays and allocated an immutable predicate/evidence disposition for every distinct row.
Reversible driver decision: sort the distinct array in place using the same complete ordinal tuple,
and reuse dispositions by the exact predicate and evidence reference within one projection.
Date facts retain their separate accepted contracts and each assertion retains its own evidence.

A focused projection regression pins every ordering key, including UTF-16 ordinal ordering,
duplicate removal, evidence resource-id/digest separation and cache lifetime. Existing acquired-row
and date-refusal tests remain in scope. Validation passed: clean solution build (zero warnings/errors,
38.01s), fast 3,066 passed/one platform skip (60.946s), and all 97 affected ingest checks (64.676s).

An offline comparison of the actual projection on 100,000 synthetic assertions retained identical
71,400,001-byte output (SHA256 f954e733f20457b2d8702ffe39b3ee9ec44f90f116a3de90877a71956fc9acea).
Allocations fell from 22,871,728 to 12,469,696 bytes; retained output from 8,802,024 to 5,602,016 bytes.
This one-predicate/one-evidence sample shares one disposition instead of 100,000. The runtime
assembly digests were checked: the initial paired build had incorrectly reused the baseline and
is excluded from this comparison. Evidence: `C:\lex-v3\lanes\lu-typed-projection-comparison-verified.json`.
The sample excludes dates and scope/persistence stages; it does not establish full Luxembourg
memory fit or population proof.

The required cross-family review returned MERGE with no blockers or should-fix findings. It
independently passed the clean build, 3,066 fast tests/one skip, all 97 affected ingest checks and
400 randomized ordering comparisons. Removing the final digest key made the new regression fail.
The one documentation repair records that current acquisition shares one profile observation ref,
so its cache is bounded by the 26 recognized predicates. The cache still keys complete evidence
identity for future inputs. The probe's single elapsed sample rose from 125.5 to 138.4 ms; this
slice claims reduced allocations, with no speed claim. The reflection regression fails loudly if
its private target is renamed or overloaded.

## EU Work resolution across languages (Codex, 2026-09-30)

French acquisition merged in PR #808. Before serving a mount that holds both languages,
Decision 89 requires an explicit resolution rule. Reversible driver decision: a Work/CELEX naming
one EU work with one held expression per language returns an ordered list of expression identifiers
and their languages. The response says expression selection is required. A caller selects a
language with the existing exact-expression resolve request; no default language is assumed.
An identifier with only one held expression still resolves directly, including a French-only work.
Several works, several expressions in the same language, or a Luxembourg/EU collision remain
`ambiguous_identifier`. Expression and provision coordinates keep their exact identity.

API regressions cover bilingual choices and following each expression identifier, a French-only
mount built from the synthetic French package, and different works whose languages differ.
Existing same-language and cross-publisher ambiguity tests remain in scope. Validation passed:
clean solution build (zero warnings/errors, 39.43s), fast lane 3,066 passed/one platform skip
(63.566s), and all 53 affected resolve/Europe mount ingest checks (91.839s). The served mount and
complete-population acceptance remain separate work.

The required cross-family review returned MERGE and independently passed the clean build,
3,066 fast tests/one platform skip and 57 resolve/Europe/answer-sample checks. Its one repair
updates the two obsolete language-rule passages below and identifies the French fixture as
synthetic in the PR evidence. Product code is unchanged by this documentation repair.

## Luxembourg rights-evidence payloads (Codex, 2026-09-30)

The semantic memory sample produced 108,361,751 bytes of observation JSON for 100,035
repeated assertion rows. It is not a population-wide estimate, but it exposes another avoidable
whole-population allocation: the previous adapter serialized all observations into one byte array.
Reversible driver decision: retain ordered payloads of at most 256 observations each. A version 3
rights-evidence root names every batch, the total observation count and all contributing deliveries.
Delivery metadata is retained once. Each observation continues to cite the complete root, so batches
containing only unselected subjects remain reachable for complete-population replay.

Every batch must pass custody write and digest-checked readback before the root can be retained.
The replay checks each referenced batch's digest, schema, ordinal, count and unique subject identity,
then requires the final manifest to rebuild byte for byte. Historical version 2 roots remain readable
by the retained-run replay helper. A 257-subject test crosses the batch boundary; an unreadable
second batch must refuse before publishing a manifest. The combined source with PR #812 passed
a clean solution build (zero warnings/errors), all 20 focused topology/custody tests, and the fast
lane (3,066 passed, one platform skip). The full ingest suite passed 1,995 tests with 19 opt-in
skips and zero failures in 22m 22.463s. PR #812 merged at
`9849ab09` after its review, one repair and green CI. No population completeness or full-process memory bound is claimed:
typed observations, individual large observations, the root metadata and downstream manifests still
need resource allowance.
A separate zero-request diagnostic with 100,000 distinct synthetic subjects and no assertions
retained 61,647,824 managed bytes (about 616 bytes per subject). Together with the earlier
assertion sample, this confirms that subject diversity needs its own allowance. These samples
exclude downstream scope resolution and do not establish whole-run memory fit. Frozen evidence:
`C:\lex-v3\lanes\lu-subject-memory-sample\measurement.json`.

Reversible run decision: validate the merged EN/FRA acquisition and new rights evidence on a
fresh GDPR/a439 mount before the full EU union run. The bounded plan retains the prior 800-request
ceiling (the older all-language run used 700), with three times that retained mount's logical bytes
plus 1 GB reserve as a storage allowance. This is a planning allowance, not a bound. Publisher
traffic is covered by the owner's 2026-09-30 standing authorisation for population and French-body
runs (`C:\lex-v3\lanes\STANDING-ORDERS.md`, section 2). Launch follows
this slice's review/merge, a clean runtime freeze and resource checks. The prior successful custody
stays intact. Evidence: `C:\lex-v3\lanes\bounded-en-fr-launch-sizing-proposed.json`.

The cross-family review of PR #814 returned MERGE with no blocking or should-fix findings.
It independently reproduced the build, fast lane, all 1,995 ingest passes/19 opt-in skips and tree
verification. Its mutation removing checked batch readback made the negative test fail. The one
repair adds a batch-specific refusal assertion and a legacy inline-observation replay regression,
and corrects the historical constraint/traffic-authority wording. Repair validation passed the fast
lane (3,066 passed, one platform skip; 63.735s) and all 21 topology/custody checks (29.332s).

A further zero-request diagnostic retained 27,887,016 bytes of additional scope resolution for
10,000 empty synthetic subjects (about 2,789 bytes each). It excludes later reduction/serialization
and is not a population bound. The production manifest reader also materializes complete bytes,
text and canonical readback. Full Luxembourg resource fit remains unresolved after this slice.
Evidence: `C:\lex-v3\lanes\lu-scope-memory-sample\measurement.json` and
`C:\lex-v3\lanes\lu-retained-scope-size-samples.json`.

## Luxembourg population memory prerequisite (Codex, 2026-09-30)

The four bounded COUNT diagnostics now cover all assertion ranges: 9,672,378 A rows
across 29 measured leaves. Together with prior S=1,986,924 and G=221,852, the estimated
page-enumeration floor is 31,456 requests before split COUNTs, vocabulary, bodies and retries.
These independently timed counts size the run; they do not prove a delivered population.
Evidence: `C:\lex-v3\lu-assertion-sizing-20260930-4\observations\summary.json` and
`C:\lex-v3\lanes\lu-population-sizing-progress.json`.

A zero-network measurement of the actual parser retained 205,171,464 managed bytes for
100,035 rows from one digest-checked 65-row page (about 2,051 bytes per row). Applying
that sample size to A alone gives about 19.8 GB, before typed observations or indexes.
The sample is not a population-wide memory bound. Available commit was about 5 GB.
Reversible driver decision: reduce raw-row retention before launching the complete run.

The adapter verifies every S/A leaf before semantic decoding, then reopens and verifies each
leaf again while accumulating typed observations. Raw S/A unions are no longer retained.
Subject and predicate strings reuse the exact census/vocabulary values. Adaptive acquisition
targets at most 100,000 rows per leaf; cover and repeated-pass proof requirements are unchanged.
The relation union, typed observations, rights-index serialization and downstream outputs still
consume memory. Full-run resource sizing remains pending; no complete population is claimed.
A second offline diagnostic called the actual semantic builder with independently parsed copies
of that page and a synthetic nine-subject census: 100,035 input rows retained 26,399,312 managed
bytes in typed results, with zero surviving sampled raw rows. The observation JSON alone measured
108,361,751 bytes. This repeated small sample is not representative of all subject counts or graph
shapes, and excludes proof, relation and downstream allocations. The single rights-index byte array
was the next constraint identified by that measurement; the batched-evidence section above now
addresses it. Downstream scope reduction and whole-manifest readback still need sizing.
Evidence: `C:\lex-v3\lanes\lu-typed-memory-sample\measurement.json` (0 requests).

Validation: clean full solution build; fast 3,066 passed/1 platform skip. Broader Luxembourg/census
checks passed 833 and skipped 12, with one stale construction-surface pin. Its independently
reflected diff adds exactly the new private async builder; both focused construction tests passed. S/A changed
custody and cancellation after preliminary verification are covered.

The cross-family review of PR #812 returned MERGE and independently passed all 834 affected
checks with 12 opt-in skips. Its repair corrects the older leaf-target wording below and adds adapter
boundary tests at 100,000/100,001 rows. Full CI also found an unregistered fault-injection test store;
the repair records its explicit custody-conformance exemption and the observed implementation count.
Repair validation passed 3,066 fast tests/1 platform skip (70.717s) and all 84 focused tests
(68.363s), including custody conformance and the adapter threshold. Final CI remains required.
The extra local S/A verification pass remains an intentional
CPU/read cost for lower retained memory; the in-memory compatibility wrapper performs no I/O.

## French EU expression bodies (Codex, 2026-09-30)

The acquisition adapter now projects French body candidates from this run's proven expression
population. Each candidate keeps its exact expression identity and language evidence; the FRA
route is selected explicitly. Original Work metadata remains in the corpus, including French-only
Works with an observed absence of an English expression. A missing French response cannot reuse
an English receipt. Annex binding checks expression lineage even when two held bodies have the
same bytes. Formex main-body admission also requires the package language to match the expression.
Its interpretation profile advances to version 3 so that this acceptance rule has its own digest;
new corpus/index derivations carry that profile.

Offline cases cover bilingual and French-only acquisition, missing French bodies, shared-receipt
annex binding, and repeatable French corpus/index construction. The French positive Formex fixture
is synthetic; the retained English GDPR package supplies the language-mismatch negative case.
These changes prepare live French acquisition. The completed bounded mount still contains the
previously acquired bodies; no new live French population or serving claim is made here.

Validation: Release solution build had zero warnings/errors; full ingest passed 1,984 with
19 opt-in skips and zero failures (12m39.548s). After the profile-version update, fast tests passed
3,066 with one Windows skip (48.870s), and affected ingest passed 194 with three opt-in skips
(31.873s). Exact-head CI also passed on 12d58871.

Reversible sequencing decision: merge this acquisition prerequisite before the expensive fresh
all-seed EU build, so one union run can retain English and French bodies. This avoids repeating a
whole acquisition merely to add the second served language. Population completion remains the
next live objective. The full EU run is prepared with a 20,000-wire planning cap, separately from
the full Luxembourg run; storage and frozen-source checks still precede launch.
PR #817 closes the Decision 89 resolution prerequisite for a French-bearing mount: a Work/CELEX
with one expression per language offers the exact expression choices, as recorded above.
The caller selects a language through the existing expression-specific request.

The cross-family review at 12d58871 reported MERGE with two should-fix findings. The repair counts
French expression objects in the population total and tests closure for bilingual and French-only
runs, records the resolve prerequisite above, and adds an absent-outcome negative case. That case reaches the earlier complete-population
guard; the per-member binding guard is unchanged.
Repair validation passed 3,066 fast tests with one Windows skip (58.382s) and 204 affected ingest
tests with three opt-in skips (80.074s). The PR records final exact-head CI.

## EU Formex language scope (Codex, 2026-09-30)

Reversible driver decision under standing order section 5: enumerate Formex manifestations for
English and French expressions only. Every other observed expression remains in the reconciled
population as `not_enumerated_language_out_of_scope`, with eligibility explicitly unknown and no
manifestation or package request. A digest-checked audit of historical run9 responses found
15,706 unique expression language assertions: 1,314 EN/FRA and 14,392 other languages, with no
conflicting language assignments. At the prior six-request enumeration estimate this is 7,884
rather than 94,236 requests, before packages and the rest of acquisition. These historical counts
are sizing evidence only; run9 used per-seed runs and a test-double scope resolver. A fresh union
run must still establish the current population. Audit: `C:\lex-v3\lanes\eu-historical-language-sizing.json`.

The guarded eligibility door requires proven EN/FRA enumerations and records the other expressions
separately. Closure still requires one outcome per expression, rejects missing/duplicate outcomes,
and cannot label an unenumerated expression ineligible. The older all-language door remains strict.
Main-body processing preserves the typed source outcome and maps it to package-not-acquired.
French body acquisition is implemented above; a fresh live run must still retain the bodies.

## Luxembourg COUNT follow-up (Codex, 2026-09-30)

The first whole-population preflight ended at 12:15 UTC with exit 3, using 53/100 wire
requests. Vocabulary was delivered; the S/A/G root COUNTs returned HTTP 500 timeout or
Virtuoso SR319 row-width errors. Custody remains in
`C:\lex-v3\lu-population-preflight-20260930-1`; no population completeness is claimed.
Compact COUNT projections retain the distinct RDF tuples and identical traversal/range filters,
while omitting redundant projected strings. A six-request diagnostic observed S=1,986,924 and
G=221,852; A still returned a retained timeout. These are observations for sizing.

Reversible driver decision: split only explicit retained initial COUNT capacity errors (HTTP 500
with the observed timeout JSON or Virtuoso SR319 prefix), alongside saturated counts. Every child
still needs both enumeration passes and a tiled cover proof. Other statuses, malformed errors and
challenges stop the cover. The shared wire ceiling includes failed parent attempts. An optional
smaller leaf target supports bounded live verification; the publisher threshold remains 1,000,000.
Single-partition callers also report these initial COUNT capacity errors as PartitionRequired;
they still refuse unless a supplied or automatic cover proves every leaf. Compact counts do not
remove the page projection: a page containing an over-wide row can still fail with SR319. The
fresh preflight will retain and inspect page failures before sizing a complete run.
The bounded a439 S cover completed at 13:00:09 UTC: 12 proven leaves (including empty ranges),
10 delivered rows and 10 rows reopened from custody, using 68/150 wire requests in 30 minutes.
Its five-row target exercised automatic splitting and the existing leaf-tiling proof; it proves
only that bounded S range. Evidence: `C:\lex-v3\lu-cover-probe-20260930-1\summary.json`,
`routes-final.json`, `counts-final.json`, and `runtime-audit.json`. The lexical strategy's empty
leaves cost requests too; full Luxembourg sizing must include this measured overhead. The fresh
whole-population preflight follows merge; no production credentials are used.

Validation: solution build passed with zero warnings/errors; fast lane 3,066 passed and one
Windows symlink skip. Affected ingest/census and request-policy tests passed: 840 passed, 12
opt-in live skips. Initial CI found only the two expected identity/surface pin changes; both were
regenerated or transcribed from the observed candidate and corrected. Review and final exact-head
CI evidence are recorded in the pull request before merge.

## Data

- **Decision 95 rights receipt (Codex data lane, 2026-09-30).** Governance PR #9 is merged.
  The live receipt now requests Commission Decision 2011/833/EU at
  `https://publications.europa.eu/resource/celex/32011D0833`, negotiating English XHTML through
  the existing document-fetch profile. No request goes to EUR-Lex. Evidence schema
  `lex-eu-legal-notice-evidence/3` records the closed `source` value; the legacy notice URI keeps
  its profile identity. Retained legacy routes can be reconstructed into /3; serialized /2 receipts
  are not accepted by the /3 reader.
  The receipt is acquired before the adapter or Formex population. A refused receipt spends one
  logical product request (plus robots and any admitted redirect hops), then stops. Its retained
  hops are rebound under the adapter's eventual corpus identity without another request.
  Live check through the production session: 303 then 200 XHTML, 48,730 bytes, SHA-256
  `2d5bc877b9a5aad948af21c680aca1d3409f41df5dd0dcc57ef9b1b225f60982`, matching the verified plan.
  Custody and canonical evidence: `C:\lex-v3\lanes\rights-probe`; log: `rights-probe.log` beside it.
  This receipt is the Commission policy the notice cites; Decision 95 records the accepted limit
  for Parliament and Council documents. When EU evidence bundles serve text, they must carry
  `© European Union, https://eur-lex.europa.eu` and the statement that only the electronic Official
  Journal is authentic. EU text bundles are still the parity slice; this change acquires the receipt.
  Next: rerun the bounded first mount with Lex.V3.Tool, using PR #750's command and the Codex
  checkout. The historical failed attempts below remain evidence of the old route.

- EU: complete 82-seed English population on disk at `C:\lex-v3\eu-population-run-9` (filesystem
  custody, 29,207 files, report `population-report.json`, `isCompletePopulation: true`, 1.9 h run).
  French expressions not acquired (Decision 89).
- Luxembourg: live per-work rights proof and adapter canaries only. **No bulk Luxembourg population run
  found on disk or in the repository.** The Luxembourg population scope and its run are an open item.
- No corpus, index or capability manifest has ever been built outside tests. The three builders take a
  Stage 3 envelope that only tests compose.
- **Run 9 cannot feed the builders** (investigated 2026-09-27). The envelope is an in-memory object
  graph with reference-identity checks, built in one process from one run identity; nothing in it was
  serialised and no reader exists. Run 9 is 82 runs with 82 identities. Two mandatory EU inputs were
  never acquired: Formex packages (the only source of EU articles for the index) and the EUR-Lex
  legal-notice evidence. A Luxembourg delivered run is mandatory in every envelope. Building the
  corpus therefore means a fresh, live, one-process acquisition run.
- **Historical Decision 88 producer (PR #746), superseded by Decision 95 and PR #780.**
  It used the `european_union_legal_notice` profile on EUR-Lex. The old route's robots response
  and its eventual empty HTTP 202 are recorded below as evidence of the failed attempts.
  The current producer uses the existing Publications Office document-fetch profile, captures the
  Decision receipt first, and rebinds it after the adapter completes. Redirects remain subject to
  the profile, literal robots evaluation and the one wire ceiling.
- The session now exposes the custody write receipt of every hop an executed attempt sealed
  (`HopWriteReceiptsByObservationId`), which the Formex ZIP binding needs as well.
- **Formex package population.** `EuFormexPackagePopulationProducer` runs the real manifestation
  enumeration for English and French expressions only (PR #799), with repeated-enumeration proofs
  and the shared wire budget. Every other language is `not_enumerated_language_out_of_scope`, with
  eligibility unknown. For the enumerated expressions it closes
  eligibility, then acquires every eligible package the corpus can serve
  (`EuFormexPackageAcquisitionProducer`): one GET of the exact `fmx4` manifestation the enumeration
  delivered, on the route the office serves (manifestation URI, 303 to `{manifestation}/zip`,
  200 ZIP; observed 2026-09-04 on GDPR, pinned by the reachability tests), through the acquisition
  session with its own robots bootstrap and the run's one ceiling; the retained route is bound as a
  package transport to WEMI references rebuilt from the run's own expression identities
  (`EuFormexAnnexTransportBinding`'s new manifestation-level constructor: no Cellar Item is named,
  because the office serves the package by manifestation and no item enumeration is needed), read
  into the annex inventory under one fixed interpretation profile, and `acquired`. Proven offline
  on the real GDPR package through the real session: the corpus states
  `formex_main_body_admitted` for the held EU member and the main-body producer parses its 99
  articles. What is not acquired is stated as its own outcome, never as a transport refusal:
  `not_acquired` with a reason (`body_not_held`: the run holds no body for that expression; French candidates now have their
  own acquisition path, and the corpus binds every acquired Formex outcome to one held body; `language_not_addressable`, `manifestation_not_singular`, `identity_not_admitted` for works
  outside the reviewed seed-root map, including consolidated expressions; and the
  four annex reasons below),
  `route_refused` with the status for any answer but 200 or 404 and for a 200 reached on a route
  that does not bind (a hop off the manifestation's path; review repair on this pull request),
  `package_rejected` with the inventory refusal for a 200 that is not a Formex package,
  `unavailable` for 404. Three corpus
  stage 3 dispositions were added for them (`formex_main_body_package_not_acquired`,
  `_route_refused`, `_package_rejected`), so the corpus file now tells a deferred or rejected
  package from a transport refusal (the builder's domain compatibility check admits them; a
  read-only survey of the annex chain on this pull request found it ranged over the old members
  only, which would have thrown inside `TryBuild` for a held member with a package not acquired;
  fixed and pinned by a corpus built from the annex-bearing package).
- **The annex chain in production (this pull request), fixtures only.** A package whose inventory
  names annexes goes on through the chain the reference tests composed and production never ran:
  the held work body is read as the publisher's XHTML annex inventory, the work's PDF is fetched on
  the document-fetch route (`GET cellar/{work}` with the `pdfa2a` or `pdf` accept the expression's
  enumeration lists; the office's 303 to the PDF item, then the 200), the three are bound as one
  annex population with the PDF page labels (`EuAnnexEvidenceBinder.BindTransportAsync`, the
  manifestation-level form: no Cellar Item, the work CELEX and language from the run), the bound
  members are classified against the PDF route, and the classification travels with the run into
  `EuFormexAnnexClassificationReconciliation`. Proven offline on the three real 2026 specimens
  (XHTML held by the run, Formex package, PDF/A) through the real session: acquired, the corpus
  states `formex_main_body_admitted` and the annex outcome for the held member. One extra GET per
  annex-bearing expression, in its own robots session. Two source checks written against
  synthetic test requests were widened to the session's real shape and say so in the code: the
  classifier admits the profile's `user-agent` beside the address's two headers, and the office's
  `;charset=UTF-8` on the PDF content type. What does not close is `not_acquired` with a reason and
  the ZIP retained: `annex_xhtml_not_inventoried` (the held body carries no publisher annex
  convention; no PDF request is sent), `annex_pdf_not_served`, `annex_evidence_not_bound`,
  `annex_body_not_classified`.
- Decision taken by the driver, reversible: the annex chain reads the held work body, so an
  annex-bearing package whose held body is not XHTML with the publisher's `*.fmx` unit wrappers is
  `not_acquired` rather than acquired without its annexes (the corpus would otherwise carry a main
  body whose annexes it cannot state). The PDF accept is the one the enumeration proves listed,
  `pdfa2a` before `pdf`, and the PDF is requested at work level with the expression's language, as
  the reference chain pinned it; the manifestation is read from the office's 303 and must descend
  from the expression.
- Decision taken by the driver, reversible: the package request goes to the manifestation, not the
  work, so content negotiation cannot pick another expression; the terminal must stay on that
  manifestation's own path. The item-level package types (`EuFormexPackage`, `EuFormexItemSet`,
  `EuFormexStreamName`) remain for the test-composed annex binder and are not on the production
  path; both real packages on disk use original-act stream naming that those types refuse.

- **EU side of the first mount: composed (this pull request), fixtures only.**
  `EuFirstMountAcquisition` (Ingest) acquires one Appendix A work end to end under one wire ceiling:
  the adapter run with production plans and publicly bound witnesses (the SPARQL witness is the
  census count query of the work itself, which selects the source profile and the path robots is
  checked against; the document-fetch witness the adapter's signature requires is a GET of the
  work's Cellar root, because a CELEX such as `12012E/TXT` is not an admitted resource path, and
  document-fetch sessions start from the request they send anyway), the Formex population, and the
  legal-notice route under the run's corpus identity. Renderer sources come from the checkout's six
  Europe renderer files, held in the run's custody (`EuRendererSources.FromCheckoutAsync`); until
  now every renderer source in the repository was a test placeholder and every SPARQL witness an
  internal fixture. Proven on the
  scripted transport through the envelope helper and `LexCorpus6Builder`: the built corpus's rights
  matrix names the real notice route. What the survey of 2026-09-27 found and this slice worked
  around: production code had no renderer sources, no SPARQL witnesses and no Luxembourg
  vocabulary snapshot (all live only in tests and canaries); `Lex.V3.Tool` would have no access to
  the internal transport seams, so the composition roots live in Ingest and the tool will be a thin
  program over them.
- Carried: a deployed build without a checkout needs the renderer bytes from another carrier (an
  embedded resource); the release pipeline slice decides.
- **Luxembourg side of the first mount: composed (this pull request), fixtures only.**
  `LuxembourgFirstMountAcquisition` (Ingest) is the Luxembourg composition root the code base never
  had in production (the live adapter canary built it inline; `TestSupport/LuxembourgProfiles`
  records "the LU composition root is Stage 6"). For one act selected by an ELI key range it holds
  the declared scope as the plan's scope document, enumerates the P, T, C and O vocabulary
  partitions with proofs, reopens and classifies them into the publisher's own values, opens the
  `VerifiedLuxembourgSourceProfile` from that observation (required values are expectations, never
  a source), runs the S, A and G families through the adapter and its Gazette loop, then the Akoma
  Ntoso inventory and legal-content producers. Renderer sources come from the checkout's two
  Luxembourg renderer files. `LuxembourgQueryPlan.CreateDefaultGraph(scope)` is the one new
  Contracts door: until now only tests could name the Luxembourg SPARQL profile, so no production
  code could create the plan. Proven offline on a transport that answers by what each query asks
  (each set's SPARQL shape is distinct), then built into a corpus through the envelope helper and
  `LexCorpus6Builder`.

- **The build and the tool: composed (this pull request), fixtures only.** `V3FirstMountBuild`
  (Ingest) takes the two acquisitions and runs the chain the corpus builder's reference test
  composes: the Stage 3 evidence envelope with the real notice route, the body composition, the
  Luxembourg publisher-PDF derivation chain, the derivation profile envelope, then the corpus, the
  Luxembourg index and the Europe index, each built twice and refused as `not_byte_stable` if the
  pair differs. `V3CorpusMountWriter` writes the five files `Lex.V3.Api` mounts (temporary name,
  then move) plus `build-report.json`, and reads a directory back through the public verifiers.
  `src/Lex.V3.Tool` is the `build` program over it: one custody root, one wire ceiling, renderer
  sources from the checkout, the system clock; exit codes 0 built and verified, 2 usage, 3 typed
  refusal, 4 written directory did not verify. Proven offline end to end: the two scripted
  acquisitions into one store, the build twice with equal bytes, the five files verified, and the
  directory opened by the API's own `V3CorpusMount`. Review repair on this pull request: the tool
  crashed on every exit after start-up (a process-exit hook cancelled an already disposed token
  source, so no run could have returned 0, 3 or 4) and checked the Luxembourg act range only
  after the EU side had spent the budget. Now every argument is checked before the first request
  (`LuxembourgActRange` binds its three family ranges at construction), an unexpected failure
  exits 1 with its message, and process tests in the fast lane pin the exit codes without traffic
  (the refused case names a CELEX that is not a seed, refused before any request). The first real
  mount now needs only the bounded live run.
- **The bounded live run, attempt 1 (2026-09-27 21:26 UTC):** killed after 94 s by the Claude Code
  harness for system memory pressure (1.1 GB free of 15.7 GB, held by other applications); 473
  custody files, no milestone, nothing to record.
- **The bounded live run, attempt 2 (2026-09-29 17:27:20 to 18:19:39 UTC): refused at the EUR-Lex
  legal notice.** Same command as PR #750's comment, the Release tool built in the driver's worktree
  at 17:21 UTC from PR #758's source, custody `C:\lex-v3\first-mount-run-1\custody` (attempt 1's
  files reused, content-addressed). Result, from `build.log`: `refused: europe: LegalNoticeRefused:
  NoticeRouteInvalid ... (spent 619 of 800)`, exit 3, no directory written. The EU adapter run and
  the Formex population returned without a refusal; the one GET of
  `https://eur-lex.europa.eu/content/legal-notice/legal-notice.html?locale=en`, the last EU step,
  answered **HTTP 202, `Content-Type: text/html; charset=UTF-8`, `Content-Length: 0`, an empty body,
  `Cache-Control: no-store`**, in 20 ms (retained: route evidence
  `09a1443d1be3deae02630d292d34d39e0387f0c9450b7b3e87f660fbe85a651d` in that custody). An empty 202
  on that page is consistent with the EUR-Lex bot-protection challenge Decision 23 anticipated, so the
  route is refused as designed and no retry or second request was sent. Custody from this attempt
  holds 409 logical requests and 402 HTTP evidence documents with 408 hops: 401 answered 200 and 6
  answered 303 by `publications.europa.eu`, 1 answered 202 by `eur-lex.europa.eu`; plus 106
  acquisition sessions, each of which opens with one robots fetch whose evidence is not among those
  documents. The tool's 619 is the budget's reservation count, which the budget holds equal to sends;
  408 hops and 106 robots fetches make 514, and the remaining 105 are not reconciled here (custody is
  content-addressed, so its file counts are not request counts). Memory was not a problem this time (the
  process stayed near 90 MB). Because the build is one process, the acquisition is spent and nothing
  was written; the Luxembourg act was never reached.
- **Attempt 2's blocker is resolved by Decision 95 and PR #780.** Both required changes are
  implemented: the Publications Office Decision receipt replaces the challenged notice, and it is
  fetched before population traffic. Rebinding its retained hops after acquisition avoids a second
  rights request. The successful live receipt check is recorded above; the completed mount is below.
- **Attempt 3 was interrupted during the IDE restart.** Started 2026-09-30 at 08:32:21 UTC,
  using PR #750's GDPR/Luxembourg command and the unchanged 800-request ceiling. Its isolated tool,
  custody and log are under `C:\lex-v3\first-mount-decision95`. The Decision receipt completed
  303 -> 200 at 08:32:25 UTC, 48,730 bytes with the SHA-256 recorded above, before census traffic.
  No exit receipt was written. Its custody remains intact.

- **First real mount completed, 2026-09-30 11:19 UTC (Codex).** The restart used the same isolated
  runtime from `1774a774`, GDPR/Luxembourg selection and 800-request ceiling in
  `C:\lex-v3\first-mount-decision95-restart-20260930`. It exited 0 after 1 h 21 m 54 s,
  spending 700 requests. EU: 91 expressions enumerated, 91 Formex-eligible; Luxembourg: 10 corpus
  records. The combined corpus has 15 members. Corpus, both indexes and both capability manifests
  built twice with equal bytes; all five written files verified on read-back. Corpus identity:
  `02d19a2a68cd6a5b37fe9ccf79a21bd0359b1f8d232c578fb129d86ec7e8d313`.
  The Decision receipt completed 303 -> 200 before census traffic. No EUR-Lex request was sent.
  At 11:19:23 UTC the local verifier returned HTTP 200/answer for coverage, EU `32016R0679` and
  Luxembourg `http://data.legilux.public.lu/eli/etat/leg/loi/2017/03/14/a439/jo/fr`, checking their
  contracts, identifiers and corpus/index digests. Evidence: `v3-corpus/build-report.json`,
  `smoke-1/smoke-report.json`, raw envelopes and API logs under the restart root. This is a bounded
  local mount; full populations and production deployment remain outstanding.
  PR #786's cross-family findings are repaired: the verifier has an exact tree-allowlist entry,
  its Luxembourg default names the `/jo/fr` expression, and snapshot/coverage digests are checked.

- **Luxembourg population selector (Codex data lane).** `Lex.V3.Tool build --lu-population all`
  selects every absolute publisher IRI for the existing S, A and G families, from `A` to U+FFFF.
  An IRI scheme starts with an ASCII letter; blank-node subjects have an empty assertion key
  and remain outside the range, matching S's IRI-only subject universe. It is an alternative to
  the three `--lu-name`, `--lu-start`, `--lu-end` arguments; mixing them or naming an unknown population is rejected
  before acquisition. The selected range is retained in the scope receipt before traffic.
  Vocabulary proofs, source-profile classification, rights decisions, typed body outcomes and
  the one wire ceiling remain on the acquisition path. This is a selector for discovered
  publisher data, not a claim that every discovered object is authoritative or redistributable.
  A two-work offline acquisition exercises both bodies and builds one corpus. The review repair
  proves that blank-node assertions refuse under the old empty bound and stay outside the new one.
  No complete live Luxembourg population has been acquired yet. The bulk run follows the bounded first mount
  and EU population preparation (#786 then #785), under an explicit shared wire ceiling.

## Next, in order (data lane; the plan's items 4 to 8 are in STATUS-WEB.md)

Population preparation (data lane): `Lex.V3.Tool build --celex all` selects all 82 reviewed
Appendix A seeds; a comma-separated selection also binds one combined run. The entire selection
is validated before traffic, one Decision 95 receipt precedes the census, and all families share
one wire ceiling and corpus identity. Formex acquisition binds each original expression to its
own reviewed CELEX, including when a batch spans several works. An expression whose work is not
an Appendix A root receives `not_acquired / identity_not_admitted`; this does not add consolidated
wordings. The earlier 82 separate runs remain separate evidence. A complete combined live build
has not yet run; the bounded first mount above is complete.

1. Data lane (Codex, Decision 95): bounded first mount and real-data resolve completed above.
   Finish PR #786 and population PR #785, then continue the population work below.
2. Data lane (Codex). Complete the EU and Luxembourg populations under the owner's 2026-09-30
   authorisation, with one typed outcome per discovered body, then acquire French EU bodies.
3. Data lane (Codex). French body acquisition merged in PR #808, using expression-keyed
   records with the exact language and receipt binding. PR #817 records the Decision 89 resolve
   rule above: offer held expressions and select one through its existing exact identifier.
   Prove the merged acquisition on a fresh bounded live run, then complete the EU population;
   every acquired main body feeds the EU index for temporal and search operations (item 5).

## Luxembourg population partitioning (2026-09-30)

The whole-population acquisition now drives adaptive covers for S, A and G when the existing
executor reports `PartitionRequired`: at the publisher's 1,000,000-row delivery ceiling or
above the adapter's lower 100,000-row memory target (PR #812). It splits six-part
cursor ranges, retains empty leaves, and runs all leaves of a family in one session and under the
same wire budget. The adapter reconciles each cover and independently reopens every leaf before
scope reduction, body acquisition or corpus construction. Explicit act ranges keep their current
path. This adds no publisher traffic by itself. The bounded mount completed; full-population
acquisition follows the reviewed selector and adaptive-cover changes.
Adaptive covers use midpoint boundaries, the 100,000-row adapter target and the publisher ceiling. The query
plan still records a legacy 900-row accumulated-slice rule that this executor does not apply;
its renderer bytes were unchanged in that slice. PR #798 subsequently added compact COUNTs and
splitting for narrowly recognized, retained initial COUNT capacity errors. The live bounded S
cover proved 12 leaves and reopened all 10 rows using 68 wire requests. This proves the bounded
range only. Fresh whole-population preflight2 used its 100-request ceiling on S splitting and
refused before A/G; it did not prove the population. Review repair stops at the first unprovable
leaf and records later leaves as not attempted, with no further requests for that cover.
Separate COUNT diagnostics have measured 5,366,756 assertion rows in 22 ranges. The remaining
range is `[http://data.legilux.public.lu/eli/etat/a, http://data.legilux.public.lu/eli/etat/b)`. These independent observations size a future run; they are not a same-instant
population proof. The full run must also allow for retained pages, decoded rows and derived files.
Validation: solution build with zero warnings/errors; fast lane 3,065 pass / 1 Windows skip;
153 initial affected ingest tests pass, including a two-work corpus through split S/A/G families.
The one review repair updates all four construction census pins and adds the stop-after-failure
regression. Full unfiltered ingest: 1,964 pass, 19 opt-in skips, zero failures (1,983 total).
Repair fast lane: 3,065 pass / 1 Windows skip; repair build: zero warnings/errors.

## DATA lane disk and launch checkpoint (2026-09-30)

Disk checkpoint: all 3,652 selected large files in old EU runs 1-8b were transparently compressed
and checked for identical length and SHA-256 before/after. They total 1,828,552,185 logical bytes;
no custody was deleted. Evidence: C:\lex-v3\lanes\old-eu-largest-compression-result.json and its
verified-file manifest (SHA-256 a0c9bd9c8a773df60be98c6dcaad6926897c0256ccc6f06a3e0c163c068f1e73).
Observed free space rose from 4,015,509,504 to 5,163,757,568 bytes. The abandoned whole-tree
hashing attempt changed no source files. The fresh EU retry still needs final local-job completion
and a current storage check before and after its runtime is frozen; it has not started.

PRs #827, #829 and #830 are merged. The all-82-seed EU retry remains prepared, with a
4,999,959,422-byte launch allowance and checks before and after freezing its runtime.
## Retained Luxembourg enumeration checkpoints (Codex, 2026-10-01)

This slice depends on pending PR869. The LU executor retains its closed invariant plan wire
representation and both enumeration passes. Restoration checks the original plan, renderer,
run and profile, then regenerates each count/page request with the existing template binder
and original artifact IDs. Retained request bytes must match the regenerated request. The
complete comparison and current custody receipt checks run again without publisher traffic.
EU and LU share the actual-receipt restoration helper; no saved proof or floor is trusted.

Nineteen cases cover empty, nonempty and multi-page enumeration, Unicode cursors, independent
custody, weaker protection, missing inputs, caller pins, substitutions and cancellation. Exact
d834ae22 passed CI 36856207562: 5,356 passed and 22 skipped; the required watch exited zero.
Claude found no material LU code defect and requested the current parent/base and combined census
correction. The one repair includes reviewed PR869 source 60c2589c and integration d56d0539,
sets the union to 262 candidates/140 guarded types, preserves all member pins and copies each
regenerated request body once. PR869 is merged as 1a63b61a and included; final current-base CI gates this merge.
Local builds/tests remain deferred under the acquisition memory guard.

Full acquisition catalog restoration and two independent offline mount derivations remain
outstanding. This does not establish full Luxembourg memory or disk fit. The active EU runtime,
queued source heads, LU index builder/reader and web lane are unchanged.

## Retained expression production pairings (Codex, 2026-10-01)

This slice depends on PR869. It retains the expression producer's own paired enumerations
and the corrigendum tripwire built from them. Reopening verifies those enumerations, rebinds
count templates to original batches and artifact IDs, and repeats the existing private
production and tripwire fold. Expression derivation, episode, tripwire and lineage must
match the held originals byte for byte. Both production orders retain checkpoints.

The original 33 new cases passed at 392567c6: CI 36856266903 reported 5,370 passed and
22 skipped; the required watch exited zero. Claude found no material production defect but
requested the merged parent/base and combined census fix. The one repair includes merged869
1a63b61a and the 261/140/7 candidate/guarded/declined union. It adds explicit null-root integrity
refusals in all three readers, validates one pass value in 1..2 before conversion, and adds three
null-root regression cases. The repaired head 39b0f98f passed CI 36858496167. Merged LU parent c9e5a40a is now included,
with the explicit EU null-root guard preserved. Fresh combined CI gates merge; local builds/tests remain deferred under
the acquisition memory guard. No complete offline mount process is claimed.

The current store's protection is checked again. Changed protection refuses the original
byte claim because protection is part of derivation identity. Replay reports zero publisher
requests. Complete offline acquisition composition and independent mount builds remain pending.
## Retained Luxembourg cover history (Codex, 2026-10-01)

Reversible driver decision: retain the actual successful split history and every leaf's
checkpoint before reporting a proven cover. Restoration replays those splits, checks the
caller's root/run/profile, reopens both passes of every leaf, compares all six query bounds,
and repeats the existing leaf and cover gates. It keeps the LeafTilingOnly basis and current
custody protection. A failed checkpoint hold produces a typed cover refusal.

The chain owns immutable copies of its leaves and history. All 22 original cases passed at
96e5cb49: CI 36859308947 reported 5,405 passed and 22 skipped; required watch exited zero.
Claude returned MERGE with no material defect. One repair includes197f0511, adds explicit null-root
integrity refusals to cover and leaf readers with two before-writes regressions (24 new cases),
and fixes the whole-checkpoint partition-ID comment. Fresh combined CI gates merge; local
builds/tests remain deferred under the acquisition memory guard. PR869 and PR872 are merged.

This supplies the cover part of the offline acquisition catalog. Complete catalog restoration,
two independent offline mount derivations and bounded full LU fit remain outstanding.

## Retained Luxembourg vocabulary proof (Codex, 2026-10-01)

Reversible driver decision: retain all four original P/T/C/O enumeration checkpoint associations
and the resulting vocabulary observation reference. Live and offline paths share the row-proof
and classification code. Replay independently rebinds both count queries to the required ranges,
restores every enumeration with current custody checks, reproduces the original observation
bytes and reopens the verified source profile. Required vocabulary remains an expectation;
missing observations cannot be supplied from that expectation. Failed checkpoint retention
stops before population traffic. The checkpoint remains reachable on later run refusals.

All 23 cases passed at reviewed 99fd13f9: CI 36859809816 reported 5,406 passed and
22 skipped. Claude found no production defect; its two blocking findings were invalid UTF-8
in inherited STATUS lines and a stale merge base. One repair restores current integration's
UTF-8 sections and includes merged PRs 873, 876 and 879 (749e88a3). Strict UTF-8 decoding
now succeeds. Fresh full CI gates merge; local tests remain deferred under the acquisition
memory guard. The checkpoint reference still needs a containing LU acquisition catalog or
CLI output to make it discoverable from a real run. Full LU acquisition restoration and
independent offline mounts remain outstanding; no full-population fit is claimed.

## Real consolidated Formex main-body support (2026-10-01)

The real bounded GDPR capture retained six Formex packages. Two originals contain ACT roots and already yield 99 articles each. Four consolidated packages contain CONS.ACT roots with 99 articles each; the previous parser reported main_body_missing because it only recognized ACT. This is reproduced by reopening the held package checkpoints, routes and bodies with no HTTP requests. Evidence is C:\lex-v3\lu-legislative-measurement-20261001-1\formex-structure-audit.json. The selected XHTML corpus body is a separate representation and did not cause this Formex gap.

The parser now admits a CONS.ACT only through one CONS.DOC, its own single BIB.INSTANCE and one ENACTING.TERMS. Amendment-history languages/dates and articles outside operative text do not enter the result. The document bibliographic date keeps its existing article meaning; CONSLEG.DATE is not substituted or treated as applicability. Profile version 4 records the new scope. Exact retained EN/FRA packages are regression fixtures with route/body provenance and SHA-256 pins; mutations cover duplicate or absent document coordinates, wrong language and excluded text. Original ACT behavior remains covered.

Local fast/ingest suites are deferred under the standing 4 GiB memory guard. Exact-head CI 36907840460 passed (5,965 passed / 0 failed / 22 skipped); Claude returned MERGE and PR907 merged as 58502d13. After merging, rederive the real bounded mount from retained custody and compare two independent runs. No new publisher capture is needed for this parser correction, and full EU/LU population completion is still outstanding.
