# Issue 459: bounded Fact replay evidence

Writer Codex; reviewer Claude. Governance head
`37b6f5763c1b15778522e16296194efd27799cc2`; product integration base
`e305791e66f82fef17dfed2da2185d68a3607385`. The separate deployment-only
preparation commit is `010eab6bf141d82a958bfff7f7b9766cb3595bbd`.
Tests below were personally executed over the subsequent dirty source. The
containing commit freezes it; `executed-source.json` binds all six changed C#
files and three executed Release assemblies. It does not claim the preparation
commit contained replay code.

The [pre-code reconciliation](https://github.com/SFHAJJI/lex/issues/459#issuecomment-5558745019)
classifies the executable population consumer as missing. Existing custody,
canonical HTTP evidence and typed Fact readers are retained. No Azure provider,
source adapter, Fact producer, accepted contract, release or promotion behavior
was rebuilt. The new internal probe consumer and command are documented in
[REPLAY.md](../../REPLAY.md).

The local fixtures intentionally pair typed Facts with synthetic three-byte
transport bodies. They exercise custody/provenance links, not extraction truth
or publisher authority. No live provider, accepted population, effective
production retention or Stage 1 closure is established here.

## Personally executed commands

All commands ran in `C:/lex-v3/worktrees/codex-stage1-custody-runner`.

| Command | Result |
|---|---|
| `dotnet test --project tests/Lex.V3.Tests/Lex.V3.Tests.csproj --configuration Debug --filter FullyQualifiedName~FactCustodyReplayTests --minimum-expected-tests 1` | RED: 10 failed, zero passed because the existing command parser did not support replay. An initial test compile needed the existing `Source.Core` namespace import; this retained RED log is the subsequent executable failure. |
| Same command with `--no-restore` after the implementation | First GREEN: 10 passed, zero failed. |
| `dotnet test --project tests/Lex.V3.Tests/Lex.V3.Tests.csproj --configuration Debug --no-restore --filter 'FullyQualifiedName~FactCustodyReplayTests\|FullyQualifiedName~AzureCustodyProbeContractTests\|FullyQualifiedName~Census\|FullyQualifiedName~CustodyStoreConformanceTests' --minimum-expected-tests 1` | 61 total, 56 passed, five census failures; after explicit inventory reconciliation, 61 passed. The backslashes in this Markdown table escape its pipe separators; actual filter characters are plain `\|` without the backslash in PowerShell. |
| `dotnet test --project tests/Lex.V3.Tests/Lex.V3.Tests.csproj --configuration Debug --no-restore --filter FullyQualifiedName~PrintReplayCensusForTranscription --minimum-expected-tests 1 --output Detailed` | Printed the actual closed-vocabulary inventory and the new static candidate; the throwaway transcription test was then removed. `replay-census.txt` retains the output. |
| `dotnet test --project tests/Lex.V3.Tests/Lex.V3.Tests.csproj --configuration Debug --no-restore --filter FullyQualifiedName~FactCustodyReplayTests --minimum-expected-tests 1` | Expanded suite: 27 passed, zero failed. |
| `dotnet restore Lex.V3.slnx --locked-mode` | Exit zero; no lockfile change. |
| `dotnet build Lex.V3.slnx --configuration Release --no-restore` | Exit zero; zero warnings and errors. |
| `dotnet test --solution Lex.V3.slnx --configuration Release --no-restore --no-build --minimum-expected-tests 1` | 2,659 total; 2,656 passed; three skipped; zero failed; 1m05.960s. |

The full-suite skips were the Windows symlink permission case and the two opt-in
EU/LU live canaries. None counts as a pass. The frozen #420 code is not in this
base, so its additional live canaries and test population are not part of these
numbers.

## Guard counterfactuals

`run-replay-mutations.ps1` was personally executed from the worktree root. It
temporarily replaces one guard at a time, runs the named focused test filter,
requires the test-failure exit code 2, and restores the original source in
`finally`. Logs and their hashes are retained. The final full Release suite ran
after restoration.

| Guard removed or bypassed | Targeted outcome |
|---|---|
| Receipt canonical digest | One failed, two passed |
| Receipt/body digest agreement | One failed, two passed |
| Receipt/body length agreement before body reopen | One failed, two passed |
| Refusal of non-derivable Fact references | Two failed, six passed |
| Inverse ontology observation | One failed |
| All inbound contributors after the first | One failed |
| Inbound scope byte reopen | One failed |
| Nonempty input population | One failed, seven passed |

Further negative fixtures exercise duplicate/missing observation identities,
missing and corrupted bodies, substituted receipt bytes, malformed/duplicate/
unknown JSON members, invalid UTF-8, oversized metadata and bounded distinct
input lists. Rejected and partial extra observations are still reopened, while
Fact references to them fail. Existing custody failure objects are preserved
and no partial result is emitted.

## Remaining acceptance boundary

This adds the missing executable read-only graph verifier. It does not supply
the authoritative population of accepted Facts; the source adapters at the
inspected base still produce scope/corpus outputs, while Stage 2 owns the live
Fact producers. Neither an empty population nor invented Facts can close the
requirement that every accepted Fact resolves to retained bytes.

The prepared Azure operation remains pinned to the old, inspected image. It has
not been deployed or executed and does not contain this new command. The owner
decision for new spending/irreversible retention, Claude exact-head review and
required CI remain gates. Any later image containing replay must be separately
built, inspected and reviewed. No second REVIEW REQUEST is implied by this
preparation evidence while #420 occupies the single review-wait slot under
work-model 14.1.
