# Integration-base reconciliation for #459 review

The prior preparation head 04e4ba034333b9ea625ccfd5470f6b3b1f1b6bb0 was merged with
reviewed integration 65bb24cc154581fc876b6ffa6c693fcff97a6f0c without conflict.
No replay C# or test change was required. The Contracts, Ingest and Ingest-test
subtrees compare byte-identically to that accepted integration head.

Personally reran on the combined source:

- `dotnet restore Lex.V3.slnx --locked-mode`: success, no lockfile change.
- `dotnet build Lex.V3.slnx --configuration Release --no-restore`: zero warnings/errors.
- `dotnet test --solution Lex.V3.slnx --configuration Release --no-restore --no-build --minimum-expected-tests 1`:
  2694 total, 2689 passed, five skipped, zero failed, 1m06.010s.

The skips are the Windows symlink permission case and four opt-in EU/LU live
canaries. No live probe was rerun for this disjoint integration transition.
The replay source is unchanged from the earlier eight guard mutations; their
retained results remain scoped to that source. The earlier full-suite count is
historical evidence at its stated base, not this combined tree's result.

The containing commit freezes this integration and its execution logs. The
REVIEW REQUEST names its exact head/tree and new base. Production limitations
remain unchanged. The deployment image still contains only the e305 probe and
is not a replay image; the owner operation still awaits its separate gates.