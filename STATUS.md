# Lex V3 status

Updated 2026-10-01 by the driver. This file replaces the issue-comment ledgers. It is rewritten in
every pull request that changes what is served, what is next or what is blocked.

## EU acquisition catalog review repair — PR #892

Claude reviewed a8840b84 with no material code defect; exact CI 36865495924 passed 5,690 tests with 22 live-publisher tests skipped, including all 24 new catalog cases. The repair makes foreign captures share the original renderer identities so the substitution test reaches run association checks, removes an assertion against an unreachable handler, documents the catalog members and removes byte-order marks. The catalog is retained but the live CLI does not yet surface its reference. Final merged parents, current-base reconciliation and fresh exact-head CI remain required before merge.

## Complete EU acquisition catalog (Codex, 2026-10-01)

Reversible driver decision: retain the successful EU acquisition's sorted seed scope, query and
Formex checkpoints, corpus-bound rights route and all six original renderer role references.
Reopening checks the nested query renderer roles, restores one checked query run, passes that
same instance to Formex reconciliation and binds rights to its corpus identity. Every source,
including the unused legacy legal-notice renderer, is retained. Failed catalog/source retention
prevents successful delivery. Offline receipt/body holds use the current store; no HTTP session
is constructed and archived renderer code is not executed.

Twenty-four draft cases cover two independent replay stores, preserved identities and same-run
association, missing components, rehashed bindings, three valid foreign-acquisition substitutions,
null roots, exact caller scope, weaker current protection, cancellation and failed root/source holds.
Initial CI36861251593 compiled and passed all24 new cases:5,684 passed/22 skipped, two inventory failures.
The exact compiled refusal/member rows are now transcribed, preserving literal pins. Fresh CI and
read-only Claude review remain required. Local
Release/fast/ingest checks remain deferred under the acquisition memory guard. This branch starts
from origin c9e5a40a and includes pending870/882/883/888 with their dependencies; final reviewed
merged sources must be included before merge. Tests use two stores in one process, not the two
independent mount processes required by S7-A03. LU acquisition and build-time/predecessor catalog
composition remain outstanding. No production action or full-LU fit claim.

## Offline rights-route reopening (Codex, 2026-10-01)

Retained EU rights evidence reopens under its original corpus run identity. The reader has no
transport and performs no writes. It verifies the route digest, every hop's original GET and
policy bytes, body receipt and actual body bytes, then repeats the existing route and legal-notice
gates. The route digest is the lookup anchor; the supplied resource identifier is not compared.
Capture times and receipts remain historical evidence, without a current retention claim.

All 12 cases passed at 47eb3e39: CI 36854051881 reported 5,310 passed and 22 skipped, with
required watch exit zero. Claude returned MERGE with no material code findings. The one repair
clarifies reopen refusals and digest lookup, makes the literal census entry readable without
changing its value, and includes integration 8c3f3801 with both STATUS sections preserved.
Fresh final-head CI gates merge. Local builds/tests remain deferred under the memory guard.

This restores one rights component. Complete acquisition catalogs and independent offline mounts
remain pending. The active EU runtime is unchanged; no publisher traffic is sent by this slice.

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

## Heads

- `v3/integration`: `c9e5a40a` (2026-10-01, PR #872 merged). Build 45 s. Fast lane
  (`eng/test-fast.ps1`): 3,077 tests, 3,076 pass, 1 skipped (the review of PR #828). Ingest suite: green on CI for PR #760
  (the CI `dotnet` job runs the whole solution on every pull request, about 7 min on the runner;
  green for PR #834);
  locally about 15 min. 995 web tests pass. The web job's "browser debugger never answered" failures
  (keyboard-walk, and paint-check since #811) are fixed by PR #822: each browser binds its own
  debugging port (`launchBrowser`) instead of a random one another browser starting at the same
  moment could hold.
- Driver: Claude Opus 5.5 since 2026-09-29 (the Fable 5.1 driver ran out of tokens on 2026-09-28
  after PR #757; the user default model is now `claude-opus-5-5`).
- Plan: `C:\lex-v3\V3-FINISH-PLAN-2026-09-27.md` (owner's copy). Decision 94 (one driver, one queue,
  one review per pull request) merged in lex-governance on 2026-09-27. Launch target 2026-11-07.

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
census and the web preview's copy. G1 (a replaced publisher file mints a new version and a
`file_replaced` event), G3 (nothing hard-deleted across builds) and G4 (as-observed answering;
`as_observed` and `knowable_on` are registered and not served) need predecessor chaining with
observation times, which follows the first mount (claimed by the web lane in PR #862); see the
event-log question (b) below. G2's
detached signature comes from the release pipeline (item 7). The mount is the fixture, so this
proves the path, not a corpus.

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

## Next, in order

Population preparation (data lane): `Lex.V3.Tool build --celex all` selects all 82 reviewed
Appendix A seeds; a comma-separated selection also binds one combined run. The entire selection
is validated before traffic, one Decision 95 receipt precedes the census, and all families share
one wire ceiling and corpus identity. Formex acquisition binds each original expression to its
own reviewed CELEX, including when a batch spans several works. An expression whose work is not
an Appendix A root receives `not_acquired / identity_not_admitted`; this does not add consolidated
wordings. The earlier 82 separate runs remain separate evidence. A complete combined live build
has not yet run; the bounded first mount above is complete.

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
   additive pull request.
3. A credential-free deployment kit, so the owner's go-live is one command (item 7). It holds the
   Azure definitions for the one-server container, and a deploy script that takes the subscription,
   the managed identity and the signing identity as parameters and never reads or stores a secret.
   It validates the templates offline, runs the zero-traffic probe against the deployed revision,
   removes the revision, and has a runbook here. No Azure login, deployment or production signing:
   those stay with the owner.
4. Standing: the real-mount journeys and gates on every new mount the data lane builds, and a
   review of the data lane's pull requests that touch the API when asked.

1. Data lane (Codex, Decision 95): bounded first mount and real-data resolve completed above.
   Finish PR #786 and population PR #785, then continue the population work below.
2. Data lane (Codex). Complete the EU and Luxembourg populations under the owner's 2026-09-30
   authorisation, with one typed outcome per discovered body, then acquire French EU bodies.
3. Data lane (Codex). French body acquisition merged in PR #808, using expression-keyed
   records with the exact language and receipt binding. PR #817 records the Decision 89 resolve
   rule above: offer held expressions and select one through its existing exact identifier.
   Prove the merged acquisition on a fresh bounded live run, then complete the EU population;
   every acquired main body feeds the EU index for temporal and search operations (item 5).
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
   `search` in one work served by PR #761, EU `dossier` by PR #762; the temporal operations,
   `provenance` and `evidence_bundle` wait on consolidation acquisition; the parity details are driver
   decisions (below). The EU permalink grammar is the web lane's since 2026-10-01 (the panel's answer
   to Q-20261001-0108-claude). It works in the API and the web; the EU index schema and the Ingest
   builders stay the data lane's. PR #850: EU hits carry a hash-pinned permalink, and EU `verify`
   is served over it. PR #853: the search screen reads and shows EU answers in one named work. PR
   #854: the journey searches the real mount's GDPR, and `verify` confirms all 61 EU citations the
   page prints. PR #856: the EU dossier pins each expression's wording, and the dossier screen and
   journey show it. The EU screens that need dated states (reading, history, compare, radar) wait on
   consolidation acquisition, as above.
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
   licence-blocked journey. Next: the
   owner's review of the French drafts (`node web/scripts/live-chrome-fr-draft.mjs`), then the
   reviewed table in `LIVE_CHROME` and `fr` in `REVIEWED_CHROME_LOCALES`, which builds `/fr/*.html`. French ships only once
   reviewed (Decision 41). Hosting (ruling 3): `Lex.V3.Api`
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
   immutability, G3 bitemporal completeness and G4 as-observed answering need predecessor
   chaining with observation times, so they follow the first mount and the event-log ruling
   (predecessor chaining is the web lane's since PR #862). What
   is left of the launch contract's machine-gates line after that was "V2 absent from the image",
   which the image rehearsal now checks (PR #831, item 7). Ruling 2's gates over the real mounted
   corpus: PR #838 derives the temporal set from any mount, PR #839 the refusal set, PR #842 the
   retrieval set, and PR #844 makes the release card the gates run over the rehearsal's own mount.

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

## Driver decisions (reversible)

Each is the driver's call under ruling 7 and can be reversed by a later pull request that says why.

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

## Waiting on others

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

## For the weekly checkpoint

- The French and English refusal sentences, one short list (ruling 4): PR #793. The 20 sentences the
  live pages say by refusal code, each page's two sentences for a refusal named only by its code,
  and the two hints a card that cannot be shown still carries (the date the history begins, the
  nearest article ids; review of #793), English as served and French as the driver's draft. Printed from the pages' own sentences by
  `node web/scripts/refusal-sentences.mjs`; a test holds every served sentence to one draft. Nothing
  French ships until the owner's reviewed wording replaces the drafts.
- The live pages' French interface copy, drafted for review (Decision 41: French chrome ships only
  once reviewed): every entry of the chrome table so far (each page's title, eyebrow, heading and
  introduction, the forms' labels and buttons), printed by `node web/scripts/live-chrome-fr-draft.mjs`
  (PR #800). Since then it has grown to every sentence of the live screens (PRs #803, #804, #806),
  the evaluation card (PR #807), the refusal card (PR #809) and Trust and Coverage (PR #810): the
  whole of the live pages' interface, as the chrome scan holds it. That includes the absence note
  ("This is what this service holds, and does not hold. It is not evidence that the instrument or the
  law does not exist.") and the live banner, both claims the owner reviews.

- A rights question the retrieval set surfaced (PR #842): on the licence-blocked mount, search
  still matches inside the text the licence withholds. It answers which articles hold a word, and
  shows none of the text: the licence-blocked journey finds no passage on any page. Whether a
  non-admitting licence should also keep its text out of search matching is the owner's call
  (rights). Nothing changes until then.

## Blocked on the owner

Only money, legal or public claims, credentials and going live (ruling 7):
- Azure production credentials and the signing identity, needed by week 5.
- The weekly 30-minute checkpoint slot.

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
## Renderer source identities from custody (Codex, 2026-10-01)

Reopen the six EU and two Luxembourg renderer-source artifacts from an exact role-to-reference
mapping. Snapshot caller input, require the declared role names, verify each body by digest and
preserve its resource ID. This avoids checkout reads and new UUIDs during reconstruction. It does
not execute archived source or independently prove that a containing acquisition used the mapping.

All 26 cases passed at 019f6eda: CI 36843293672 reported 5,304 passed and 22 skipped,
with required watch exit zero. Coverage includes independent and weaker custody, byte/ID equality,
mapping order/mutation, all eight missing bodies, exact roles/counts, wrong digests and cancellation.
Integration d56d0539 is now included; fresh CI and read-only Claude review remain required.
Local builds/tests are deferred under the acquisition memory guard. Complete catalogs and two
independent offline mount processes remain pending. No publisher traffic or production actions.

## Offline Formex population reconciliation (Codex, 2026-10-01)

Reversible driver decision: retain the complete ordered Formex population associations for a
checked EU run. Replay restores each served-language enumeration and selected package from
custody, uses the same eligibility, per-family outcome and whole-run reconciliation gates,
and consumes every checkpoint exactly once. It preserves the original renderer identities,
CELEX selection mode, annex identities and typed non-EN/FRA outcomes. A missing package or
population checkpoint prevents delivery. Replay sends no requests and introduces no new artifact digests. Enumeration receipt restoration
repeats checked local holds so current custody guarantees are not inferred from archived receipts.

All 32 new cases passed at 2f55bc45: CI 36846666698 reported 5,398 passed and 22 skipped,
with required watch exit zero. Cases cover copied/weaker custody, annex and expression identities,
language exclusions, automatic CELEX selection, missing evidence, changed associations and holds.
This branch now includes updated package parent af34feed and integration d56d0539. Fresh combined
CI and a read-only Claude review remain required; pending PR881 must merge with its final source
included. Local builds/tests remain deferred under the acquisition memory guard. This restores
Formex over an already checked run; complete acquisition composition and independent offline
mount builds remain outstanding.

## Offline Formex package and annex derivation (Codex, 2026-10-01)

Retain ordered ZIP/PDF attempts, original annex profile identities, checked expression/corpus/CELEX
input digest, renderer and final outcome digest. Replay selects addresses from the same enumeration
and repeats the existing inventory/annex core using checked retained routes. Consume every saved
fetch/profile and reproduce typed outcomes and identities. Unexecuted attempts remain operational
refusals; no new observation or publisher request is invented.

All 32 cases passed at e187837d: CI 36843830053 reported 5,366 passed and 22 skipped,
with required watch exit zero. Integration d56d0539 is included; pending PR876 and PR879 must
merge and their final source remain included before this slice merges. Fresh combined CI and
read-only Claude review remain required. Local builds/tests stay deferred under the memory guard.

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

## EU query acquisition replay (Codex, 2026-10-01)

Reversible driver decision: retain the complete successful query adapter's dependency catalog
before returning success. It binds ordered seeds, original plans/renderers, every census and
object-family checkpoint, paired expression/tripwire productions, document ladders, watermark
traversal and original manifest/run/corpus identities. A failed catalog hold has its own refusal.
Offline reopening independently checks both count templates, derives object batches from the
proven census closure, checks the paired producer's original P/X proofs, then uses the same
decode/reduction, document/witness reconciliation and corpus writer. Every catalog association
must be consumed exactly once. The paired producer reopens its families again through its own
checked door; this avoids accepting caller-assembled proof inputs. Its repeated read cost is not
measured. Changed current custody protection refuses the original-byte equality claim.

All 37 original cases passed at 3bd16571: CI 36853048719 reported 5,454 passed and 22 skipped,
with required watch exit zero. They cover separate replay stores, served/unserved bodies, original
identities, paired derivation equality, missing/changed dependencies, exact seed scope, weaker custody,
foreign valid acquisitions, cancellation and failed root/source holds. The catalog retains every
named renderer, including unused roles. Existing hold tests locate their target by its actual receipt.
An additional literal-null regression now checks a typed integrity refusal before any replay write,
bringing this slice to 38 cases. Fresh combined CI and read-only Claude review remain required;
local builds/tests stay deferred under the acquisition memory guard.

This branch includes merged PR873 and integration92c8df5a, reviewed witness parent1a228278,
updated ladder parent61fd5042 and corpus parentea5078c8. Pending877/886/887 and their879 prerequisite
must merge with final reviewed source included. This restores the query adapter; rights/Formex/LU
and mount-catalog composition remain separate. Two independent full mount processes are not yet
proved. The active6eb EU runtime/custody are unchanged; no full-LU fit is claimed.


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
## Retained EU watermark traversal (Codex, 2026-10-01)

Reversible driver decision: retain successful witness traversals with ordered batch descriptors,
original page evidence references, acquisition run, renderer, entry/evidence digests and historical
elapsed time. Offline replay uses the same opening, crossing, tie-set and double-terminal loop.
Each original query is independently rebound; request, response, receipt and retained body must
agree. Every saved page must be consumed once. There is no publisher session on the replay path.

All 25 cases passed at 9eba7749: CI 36856725210 reported 5,362 passed and 22 skipped,
with required watch exit zero. Claude found no material replay defect but required merged869,
current integration and updated evidence. The one repair includes197f0511, documents historical
elapsed time beside zero replay sends and explains failed checkpoint retention. Fresh combined
CI gates merge; local builds/tests remain deferred under the acquisition memory guard.

This reader checks historical receipts and writes nothing; it makes no current protection claim.
A containing acquisition catalog must retain the checkpoint/run references that the existing
adapter drops. Pending PR888 supplies that composition; full independent mounts remain outstanding.

## Retained EU document ladders (Codex, 2026-10-01)

Reversible driver decision: retain the ordered document attempts behind the scope manifest's
selected body rows. The shared live/offline loop keeps the original format fallback rules,
typed robots and unavailable outcomes, served format, and accounting for every minted row.
Replay checks the manifest and address ladders, restores original transport evidence, consumes
all saved attempts exactly once and repeats checked body holds for the current custody floor.
A successful adapter run exposes its document checkpoint; failed root retention has a distinct
DocumentCheckpointNotRetained refusal. Offline replay opens no publisher session.

All 26 new cases passed at 7377c0c4: CI 36849937717 reported 5,323 passed and 22 skipped,
with required watch exit zero. Cases cover copied/weaker custody, successful/refused bodies,
format fallback, missing evidence, changed row/order/representation, cancellation and failed holds.
Updated document parent 00e95ae6 and integration 8c3f3801 are included. Fresh combined CI and
read-only Claude review remain required; final merged PR879 source must remain included.
Local builds/tests stay deferred under the acquisition memory guard. The complete run coordinator,
acquisition catalog and independent offline mounts remain outstanding.

## Corpus rebuild identity (Codex, 2026-10-01)

The internal rebuild path takes the original corpus set reference and rederives records from
checked acquisition inputs. Before writing, it compares the newly derived domain-separated
canonical digest. A mismatch returns RebuildIdentityDisagrees. A match preserves the original
resource ID through normal checked holds and reopening, for inline and chunked storage. Normal
acquisition still creates a fresh identity; the retention floor comes from current writes.

All 16 cases passed at c712d2a6: CI 36849932332 reported 5,294 passed and 22 skipped,
with required watch exit zero. They cover separate stores, inline/chunked bytes, weaker custody,
changed digest/manifest/run/outcomes before writes, failed holds and cancellation. Integration
d56d0539 is now included. Fresh CI and read-only Claude review remain required; local builds/tests
stay deferred under the memory guard. Acquisition closure and independent full mount processes
remain outstanding. No publisher traffic or production action.

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
