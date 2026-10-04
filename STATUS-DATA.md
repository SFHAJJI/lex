# Lex V3 status: the data lane

Updated 2026-10-04.

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
