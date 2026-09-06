# Opt-in custody failure diagnostics — issue 459

This receipt records version 1. The separately selected version 2 is documented
in [configuration diagnostics](../configuration-diagnostics/README.md).

The first approved Azure probe ended with exit 1 and only `custody_probe_failed`.
Its console and existing system telemetry could not distinguish the intended
missing-retention refusal from another failure. The operator stopped before
changing retention or starting another execution. See the
[live receipt](https://github.com/SFHAJJI/lex/issues/459#issuecomment-5560554916).

Authority is governance `37b6f5763c1b15778522e16296194efd27799cc2`, S1-A04.
The product base is `a96de9a36fdb93cc5700fcd3e71bdab6eb3c3976`. The
[pre-edit reconciliation](https://github.com/SFHAJJI/lex/issues/459#issuecomment-5560564883)
retains the accepted provider, journal, retention, integrity, restore and replay
boundaries. This change repairs only the probe's loss of diagnostic information.

## Operator behavior

By default, failure still returns exit 1 and exactly the existing fixed stderr
marker. Set `LEX_V3_CUSTODY_DIAGNOSTICS=1` to append one JSON diagnostic line.
Other values do not enable it. Success output and cancellation behavior are
unchanged. The deployed image and deployment template are unchanged.

The report contains at most eight causes, in outer-to-inner order, with explicit
`truncated` state. Each cause contains a fixed `kind` and an HTTP status from
100 through 599, or null. Types have a closed mapping; unknown types remain
`unknown`. It never reads exception messages, stack traces or arbitrary metadata,
and never emits class names, URLs, environment values or credentials. Azure's
execution/replica log coordinates bind the line to the particular invocation.

For example, a `custody_required` cause followed by `http_request` status 403
preserves the authorization status already carried by the provider. A
`custody_policy` cause is **not** proof of a particular missing or insufficient
retention condition. `invalid_operation` is an exception category, not a claim
that a particular environment variable was wrong. No reader may infer evidence
hidden beyond a truncated chain. This diagnostic is private operational evidence,
not a portable custody receipt, public refusal vocabulary or acceptance decision.

## Personally executed verification

Windows, .NET SDK 10.0.400. Commands, raw outputs, source hashes and stopped-live
evidence are in [evidence.zip](evidence.zip). Every archive member was reopened
and checked against its byte length and SHA-256; source hashes match these files.

- Before implementation, the process-level opt-in test failed: one line instead
  of the required two. The three default/invalid-value cases passed.
- Focused probe and diagnostic tests: 36 passed, none skipped or failed.
- Seven temporary regressions each caused test assertions to fail: publishing
  messages (16 failures), removing status bounds (6), accepting a ninth cause (1),
  losing inner causes (9), publishing arbitrary type names (3), conflating policy
  and integrity (1), and removing opt-in (3). All mutations were restored.
- Locked solution restore and full build passed, with zero warnings/errors.
- Full solution suite: 2,727 passed, 6 skipped, 0 failed, 1m 15.063s. Skips are
  the existing five opt-in publisher checks and Windows symlink-permission case.
- A separate compiled probe process with identity/custody selectors removed,
  diagnostics enabled and a fake forbidden credential returned exit 1, empty
  stdout, the fixed marker and `invalid_operation`; the sentinel did not escape.

For mutation reproduction in a disposable checkout, extract the archive into
`artifacts/diagnostics` and run `python artifacts/diagnostics/run-mutants.py`
from the repository root. It temporarily changes only the two probe source files
and restores their original bytes in `finally`. It does not contact Azure.

## Limits and next gate

The old live failure remains unattributed. No modified probe was run on Azure,
and no new OCI image was built, pushed or substituted. The existing manual job
remains deployed and stopped; its original exact image is still pinned. No
retention lock or second execution occurred. Any diagnostic deployment needs a
new inspected exact image, cross-family review and a concrete continuation within
the owner's execution boundary. No accepted-Fact population, production custody
acceptance, Stage 1 completion or V3 promotion is established here.
