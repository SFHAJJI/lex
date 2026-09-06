# Luxembourg plain-XML acceptance continuation

Governance: `37b6f5763c1b15778522e16296194efd27799cc2`.
Product base: `28271c22be2f63caf05dd0d5ca95cb1abd535114`.
This continues #420 after accepted PR #462; its frozen branch remains unchanged.
The scope is the missing complete plain-`xml` adapter proof, not whole Luxembourg acquisition,
production retention, release lineage, or Stage 1 closure.

## Clause reconciliation

| Clause | Classification at entry | Treatment |
|---|---|---|
| S1-A01 / #420 POST enumeration, publisher vocabulary and both AKN labels | Correct machinery retained; plain XML end-to-end evidence unaccepted | Preserve the repeated-enumeration executor and source-profile selectors. Add an opt-in public-adapter probe for Code Civil's plain-XML manifestation. The accepted `xml-akomantoso` Act evidence remains in the parent directory. |
| S1-A02 / original-Act qualification and per-work quarantine | Implemented and accepted | Retain the exact Act class, parent and type checks. The declared second range supplies the original Act's own assertions; it does not manufacture them. Existing exclusions and quarantine remain in force. |
| S1-A05 / complete bounded scope and proof provenance | Disjoint scope assembly missing; duplicate request identities incorrect | Require aligned S/A/G members under one plan, disjoint whole-subject ranges and matching covers. Reopen every member and retain every contributing proof. Reject duplicate family identities before traffic. |
| S1-A05 / same-run independent rights | Correct implementation; missing plain-XML live acceptance | Preserve SPARQL/in-file channel independence, checked XML reading, typed disagreements and final-manifest replay. Require a held plain-XML body, an observed publisher `userFormat` assertion, same-run CC-BY agreement and byte-for-byte final-manifest reconstruction. |
| S1-A03,A10 / custody before decode, literal robots, GET never HEAD | Implemented and accepted | Use the existing routed executor and fetch plan unchanged. No shared HTTP-session or EU implementation changes. |
| S1-A04 / production enforcement and accepted-Fact replay | External #459 dependency | Local `FileSystemCustodyStore` evidence reports unenforced retention and cannot establish production acceptance. |
| S1-A06..A08 / corpus contract and release boundaries | Implemented and accepted | Retain `corpus/6`, V3-only boundaries and the existing release prohibition. |

The publisher's retained `isMemberOf` value is the work root
`http://data.legilux.public.lu/eli/etat/leg/loi/1804/03/21/n1`.
The accepted resolver verifies its original `/jo` Act's own class, parent and type assertions.
The new canary declares two half-open whole-subject ranges:

- Code Civil: `.../code/civil/20251226` through, excluding, `.../code/civil/20251227`.
- Original Act metadata: `.../loi/1804/03/21/n1/jo` through, excluding, that exact IRI plus `!`.

The second range intentionally does not fetch unrelated original-Act manifestations. Absence or
failure of the required original assertions cannot be replaced with a synthesized legal assertion.

## Personally executed development checks

The final focused adapter suite passed **55 tests**. The construction/custody-conformance selection
passed **107 tests**. The complete Release solution passed **2,708 tests**, with **six tests
skipped** and **zero failures**: five opt-in publisher canaries and one symbolic-link test this
Windows host could not execute. The final Release build reported **zero warnings and zero errors**.
Exact logs are in `regression-logs/`.

```powershell
dotnet build Lex.V3.slnx --configuration Release --no-restore
dotnet test tests/Lex.V3.Ingest.Tests/Lex.V3.Ingest.Tests.csproj -c Release --no-restore --filter 'FullyQualifiedName~LuxembourgQueryExecutionAdapterTests'
dotnet test --solution Lex.V3.slnx -c Release --no-restore --filter 'FullyQualifiedName~ConstructionSurface|FullyQualifiedName~GuardedConstructionCensusTests|FullyQualifiedName~CustodyStoreConformanceTests'
dotnet test --solution Lex.V3.slnx --configuration Release --no-restore --no-build --minimum-expected-tests 1
```

The duplicate-family regression failed against the prior behavior. The scoped cover/custody
regressions initially produced **four failures in 12 tests**; the early-refusal relation regression
produced **two failures in three tests**. Repairs retain incomplete relation state until the declared
scope's relation evidence has actually reopened. A later full run exposed **six stale construction
and test-store census pins**; their exact entries were transcribed from reflection, with no sweep
narrowing. The temporary reflection dumper was removed from the test assembly.

`scope-mutations-final.json` identifies **11 distinct development mutations caught by executed
tests**. The initial scope-count mutant survived because the existing missing-member case also
failed a different guard. Empty-scope and unassigned-family cases now discriminate that guard.
An initial relation-reopen mutant failed compilation and is explicitly excluded; its compilable
replacement failed the actual regression. Two interrupted/overlapping full-suite attempts are
excluded from acceptance. Mutation runs preceded the final early-refusal state ordering repair;
the final focused and full-suite runs cover that repair.

To repeat the mutation checks from the checkout root, run the retained
`run-scope-mutations.py`; it restores exact source bytes in `finally` and records each command,
exit code and log. It writes into `artifacts/issue-420-xml`, which must exist. Compiler failures
never count as caught mutants. It is destructive only to its temporary source mutations, so run it
with no concurrent build or live probe.

## Live command and evidence boundary

```powershell
$env:LEX_LU_XML_ADAPTER_CANARY='1'
dotnet test --project tests/Lex.V3.Ingest.Tests/Lex.V3.Ingest.Tests.csproj --configuration Release --no-restore --no-build --filter 'FullyQualifiedName~PlainXmlCodeCivilAndItsOriginalRunThroughThePublicAdapterWithSameRunRights' --minimum-expected-tests 1 --output Detailed
```

The canary records source and loaded-assembly identities before its first publisher request, holds
fresh P/T/C/O vocabulary evidence, and exports its full member index on success or failure. Its
scope, route receipts, assertion rows, acquired body, two rights indexes and final manifest are
retained in the same run. `archive_run.py` packages a completed run and reopens every archive member
against its length and SHA-256. The archive does not turn unenforced local retention into production
custody acceptance.

## Completed live result

The canary passed **one test, zero failures**, in **13m 39.382s** (test duration), starting
2026-09-06 at 13:37:45 UTC. Fresh vocabulary selected and delivered matching counts in both passes:
P=357, T=80, C=7,826 and O=1, with no missing required value. All six scoped families were proven:

| Declared member | S delivered | A delivered | G delivered |
|---|---:|---:|---:|
| Code Civil state | 7 | 48 | 0 |
| Original Act metadata | 1 | 14 | 1 |

The GET held **5,413,721 bytes**, SHA-256
`0b8b50652ea31f1cad7dcae09b7eda33b19a038dd88af9f67bfcc0bb992c073f`.
The retained in-file reading identifies exactly
`http://data.legilux.public.lu/eli/etat/leg/code/civil/20251226/fr/xml`
and `http://creativecommons.org/licenses/by/4.0/`. The test independently reopens the retained
channels, verifies the source-derived plain-`xml` label and same-run agreement, reconstructs the
final manifest byte-for-byte, and verifies that manifest against the corpus reference.

Final manifest: **41,945 bytes**, custody SHA-256
`77e58d62cc1c75d5418a4534efc907f9dcee07b4b22b33e459ccff7b45c2901a`;
canonical SHA-256 `ee43275193613b9123a0ba33cd8f5e7bb5e1cc3df69f474a2215849fec52779e`.
The two identities have distinct domains and are not interchangeable.

`2026-09-06-live-xml.zip` contains **714 independently reopened and hash-verified members**.
Archive SHA-256: `fceb3e783dc9cfe2e4d619f7f76e8774e648c1934a531dcf3a7b688b8339b6c0`.
Its sibling JSON lists every member's exact length and digest. The original 1,919,964-byte run index
has SHA-256 `4b569b59b01c8ae6371eee6df06d6d335912c46fac41cd9dd09472954b08e538`.
All **190 recorded source files and four loaded assemblies** were reopened and matched to the
run's pre-request provenance. SDK `10.0.400` was personally checked.

The archive helper also passed a synthetic valid-byte control and failed after bytes changed
under the retained digest filename. `archive-guard-result.json` records those commands and exit
codes; these are tool checks, not publisher observations. Scoped Git attributes preserve the exact
log and archive bytes and their recorded hashes.
