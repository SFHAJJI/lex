# Luxembourg reconciliation evidence

This directory records development acceptance work for issue #420 against governance
`37b6f5763c1b15778522e16296194efd27799cc2`, based on integration
`e305791e66f82fef17dfed2da2185d68a3607385`. It is not release lineage, a release-grade corpus,
production retention acceptance, or Stage 1 closure. The live probes use the real routed executor
and `FileSystemCustodyStore`, whose retention is `RetainedUnenforced`.

## Reconciliation

| Applicable clause | Disposition before edits | Result |
|---|---|---|
| POST enumeration; held transport bytes before decoding | Retain accepted machinery; execute missing live evidence | Existing executor, proof, custody and strict-decoder boundaries retained. Assertion query repaired only after reproducible publisher timeouts. |
| Literal robots evaluation, S1-A10 / D83 | Repair | Removed transfer of unrequested derived/caller page prohibitions. Actual requested URL and fresh-policy refusal checks remain. EU redirect-target evaluation belongs to Claude's separate #421 lane. |
| Work topology and D58 body selection | Repair | Assemble forward WEMI evidence from the same proven census; include the exact original Act's own delivered assertions for consolidation qualification. Preserve resolver checks. Choose only body-accepted candidates, so withheld SCL XML cannot displace an accepted sibling. |
| E0 per-work quarantine | Repair | Unruled selector objects retain exact assertions and receive per-work dispositions. Pending channel two is typed; completed failed acquisition stays unproven. |
| Same-run independent rights and final corpus manifest | Missing production execution | Read complete, checked held AKN XML; retain actual SPARQL and in-file reading indexes; re-resolve and reopen the final manifest before corpus records. Missing, malformed, conflicting and unruled declarations cannot become agreement. |
| `corpus/6`, no V2 compatibility, no premature release | Retain | Existing writer and canonical readers remain the boundaries. No release artifacts or V2 compatibility are introduced. |
| Production retention / accepted-Fact replay, S1-A04 | External acceptance dependency | #459 remains open. Local custody probes do not establish its contract. |

## Query evidence

`2026-09-06-enumeration-probes.zip` contains complete retained members and the original run indexes
for four personally executed probes. Its sibling JSON lists every archive member's byte length
and SHA-256. The archive was reopened and all **546 members** verified after writing; archive
SHA-256 is `6bf8aff15e9a86006e6a19ea3073fb1c4f7c4fc3d5a1eb222c932e83da7ffe38`.

| Archive prefix | Personally observed result |
|---|---|
| `original-broad` | Code Civil prefix: S selected and delivered 7,235 rows in each pass. A count returned retained HTTP 500 (`Read timed out`). G unobserved. |
| `original-narrow` | 20251226 state: S selected and delivered 7 rows in each pass. A count returned retained HTTP 500. G unobserved. |
| `prefilter-only` | Same narrow partition: S selected and delivered 7 rows in each pass; A count still returned HTTP 500. G unobserved. |
| `ordered-prefilter` | Same narrow partition: both passes selected/delivered S=7, A=48 and G=0. Test passed. |

The last run used:

```powershell
$env:LEX_LU_ENUMERATION_CANARY='1'
dotnet test --project tests/Lex.V3.Ingest.Tests/Lex.V3.Ingest.Tests.csproj --configuration Release --no-restore --filter 'FullyQualifiedName~CodeCivilFamiliesAreEnumeratedTwiceThroughThePublisherRoute' --minimum-expected-tests 1 --output Detailed
```

These were unfrozen development probes. Their retained request plans and renderer bytes identify
the queries actually sent. Their base HEAD and dirty-path lists are context, not a claim that the
base commit contains the repairs. No broad assertion or full Luxembourg completeness follows
from the successful narrow run.

`query-templates-baseline.json`, `query-templates-current.json` and
`query-templates-ordered.json` are actual exports from the accepted baseline, subject-prefilter
version and final ordered version. `verify_exported_assertion_templates.py` executes the templates
over synthetic RDF: **84 queries, 34 comparisons, nine ranges**. Four wrong rewrites each change
both page and count results. All eight unrelated templates are byte-identical. The result JSON
records the exact export digests.

The script checks separately that removing only `DEFINE sql:select-option "order"` from the ordered
export recovers the exact algebra it executes. RDFLib does not execute Virtuoso physical directives.
The directive's performance evidence is the live run above; its meaning is documented by
[OpenLink](https://docs.openlinksw.com/virtuoso/rdfperfcost/).
To reproduce the offline comparison, install RDFLib 7.1.4 in an isolated environment and run the
script. No RDFLib dependency is added to the product or CI.
`ExportQueryTemplates.cs.txt` and `ExportQueryTemplates.csproj.txt` preserve the actual exporter.
Copy them to an isolated directory as `Program.cs` and `query-export.csproj`, set the
`ContractsProject` MSBuild property to the absolute Contracts project path, and pass the output
JSON path after `--`. The synthetic scope/profile references only instantiate the plan; they are
not publisher observations. The exported fields are the product's actual query templates.

## Live adapter development probes

`2026-09-06-adapter-probes.zip` retains the complete `consolidation-without-original` and
`act-smoke` runs; its sibling JSON indexes every member. All **1,120 members** were reopened and
verified. Archive SHA-256:
`146fcc863fbf0f584d3083374c5faf93ca85d98cb50a6a0551ac28872b228bd2`.

Both runs freshly selected/delivered P=357, T=80, C=7,826 and O=1 in each pass. P/T/C covered their
whole declared key range; O covered the declared CC-BY range. No required vocabulary value was
missing, and no required value was manufactured from the policy table.

The Code Civil run proved its narrow families and produced a manifest and records, but failed
its held-body assertion because the exact original Act was outside that scope. It does not prove
live rights agreement. The 2017 Act smoke run passed: proven S=10, A=65, G=4, with one held
19,986-byte XML body at SHA-256
`9e43a99e4b9735e383d989989d4005fc9e1676f4094c2633f30b2f056d5e476d`.
Its checked in-file reading names the exact manifestation and CC-BY value; the test rebuilt the
rights-bearing final manifest byte-for-byte from the retained channels. Both runs capture their
starting source and assembly identities. They predate the final digest-link and reader audit
repairs; the final-source run is recorded separately, not inferred from this smoke result.

`2026-09-06-document-route-probe.zip` separately preserves the existing Xml-token acquisition
canary personally run at clean `627e068d268c51bcae1e83af3d6e6c970bf9d6d2`: two GET bodies held
(5,413,721 and 5,528,052 bytes). All **34 archive members** were reopened and hash-verified;
archive SHA-256 is `d1bdab8b1f2de9c54c0b8a6d3f065b273dc0cb69cc0b7782b20cc097e80e47bc`.
This is fetch-half evidence over a prebuilt manifest, not a live enumeration or rights proof.
Its original index explicitly discloses that boundary and contains historical external census
figures which this session does not claim to have measured. The fresh 2017 Act SPARQL evidence
instead selects `user-format/xml-akomantoso`, and that source-derived path held the XML body above.
Thus the two label paths are distinguished rather than calling the older two-expression canary
a two-label or end-to-end run.

## Final-source live adapter run

The final compiled source passed the opt-in public-adapter canary: **one passed, zero failed**,
8m 47.790s, from 2026-09-06 10:19:48 UTC. Fresh P/T/C/O counts remained 357/80/7,826/1 in
both passes; S/A/G proofs delivered 10/65/4. The same 19,986-byte body above was held, its
in-file declaration observed, and the final manifest replayed byte-for-byte from retained channels.
The final manifest custody SHA-256 is
`551d2fe04f854fc938d2302fd8ced24705e144de605ff6b354bd4d02943ae26f` (50,461 bytes).
The acquisition-manifest custody link was also independently reopened (50,275 bytes).

```powershell
$env:LEX_LU_ADAPTER_CANARY='1'
dotnet test --project tests/Lex.V3.Ingest.Tests/Lex.V3.Ingest.Tests.csproj --configuration Release --no-restore --no-build --filter 'FullyQualifiedName~LuxembourgLiveAdapterCanary' --minimum-expected-tests 1 --output Detailed
```

`2026-09-06-final-adapter.zip` retains this run separately. All **577 members** were reopened and
hash-verified. Archive SHA-256:
`70f8ebcbf815b3cfffaea4cfa3bbeb63f537031f7d381c9fe2cc7224fc5cb907`.
Its retained evidence-index SHA-256 is
`0a30c30382eeb44f0a722ca2f8091fc9ab4bfb2f80df8e2e312b078abb47d3a3`.
All **188 captured source hashes and four assembly hashes** matched the checkout after completion.
The run occurred before committing, with base HEAD and dirty paths disclosed; no code changed
between compilation, execution and packaging. This establishes this bounded source behavior,
not full Luxembourg completeness, production retention, or Stage 1 acceptance.

## Regression evidence

The tests exercise the public adapter with scripted HTTP and checked custody, separately from live
publisher evidence. They reopen both rights indexes and rebuild the final manifest byte-for-byte.
The consolidation regression failed with its exact original present before the graph repair; the
case without that original stayed withheld, even with an unrelated Act present.

Individual temporary mutations were personally executed and restored:

| Bypassed guard | Observed failure |
|---|---|
| DTD prohibition | The internal-entity case failed. |
| FRBR identity | Its mismatched-identity case failed. |
| JOLUX identity | Its mismatched-identity case failed. |
| Metadata-only licence selection | The declaration-in-body case failed. |
| AKN format restriction | Both PDF-route cases failed. |
| Recognized XML namespace | The lookalike-namespace case failed. |
| Single identification block | The duplicate-identification case failed. |
| Final rights phase | All six public-adapter final-manifest cases failed. |

Nested licence markup was also reproduced as a failing regression before its repair. Restored
reader and topology tests: **23 passed, zero failed**. Full Debug suite after the consolidation
repair: **2,662 total, 2,657 passed, five skipped, zero failed**. Skips were the Windows symlink
permission case and four opt-in publisher canaries.

The final audit added direct acquisition-manifest reopening, exact publisher item identity for
Unicode/escaped file names, and single document/metadata/manifestation containers. The missing
digest link failed six cases; normalized item identity failed two; extra containers failed three.
After repairs, all **28 reader/topology cases passed**. A follow-up review of the changed fragments
found no remaining material issue. This is an in-flight same-family check, not Claude's READY.
The SCL-versus-CC-BY conflict hypothesis was rejected against the authority: disagreement remains
typed conflict, and unknown-rights raw retention is distinct from serving. Existing conflict
semantics were retained.

Final source verification personally executed:

```powershell
dotnet build Lex.V3.slnx --configuration Release --no-restore
dotnet test --solution Lex.V3.slnx --configuration Release --no-restore --no-build --minimum-expected-tests 1
```

Build: **zero warnings/errors**. Tests: **2,667 total, 2,662 passed, five skipped, zero failed**.
The five skips remain the Windows symlink permission case and four opt-in canaries. Captured
commands/results are in `regression-logs/`; a skipped live test is never counted as live evidence.
