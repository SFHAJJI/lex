# Configuration-failure attribution — issue 459

The approved diagnostic execution returned `invalid_operation` without locating
its source. See [the terminal receipt](https://github.com/SFHAJJI/lex/issues/459#issuecomment-5560972438).
This extension makes the probe's own configuration refusals attributable. It does
not identify the cause of that past execution or change an authentication guard.

Authority: governance `37b6f5763c1b15778522e16296194efd27799cc2`, S1-A03/A04.
Base: integration `2ad37ed221ded7636d2a31e99424a6a081c3dd97`.
The [pre-edit reconciliation](https://github.com/SFHAJJI/lex/issues/459#issuecomment-5561010617)
retains the reviewed provider, custody ordering, retention enforcement, restore,
replay and credential/source guards. Live acceptance remains outstanding.

## Operator contract

Set `LEX_V3_CUSTODY_DIAGNOSTICS=2` explicitly to select
`lex-v3-custody-probe-diagnostic/2`. The fixed stderr marker still comes first,
followed by one JSON line on failure. Each cause adds `configuration_guard`,
either null or an object containing exactly `kind` and `setting`.

The five guard kinds are `secret_credential`, `alternate_identity_source`,
`missing_setting`, `invalid_identity_source`, and `invalid_guid`. Setting names
are restricted to the probe's fixed environment vocabulary and normalized to
uppercase. Values never appear. The combined identity endpoint/header guard
reports a null setting because it does not establish which operand failed.
Unrecognized internal guard values report `unknown`; unrecognized names report
null. A missing annotation means **unattributed**, not that configuration passed.

Attribution comes from a private metadata key attached at the five existing
configuration throw sites. Exceptions remain exactly `InvalidOperationException`.
The serializer accesses only this private key, and only on that exact standard
exception type. It never enumerates metadata, reads external subtype `Data`, or
reads messages, stacks, arbitrary class names, URLs or credentials. A lookalike
message or string-keyed metadata cannot create attribution. Other failures,
including URI/options validation and SDK failures, remain unattributed.

Version 1 retains its exact field set and bytes for these failures. Default and
invalid selector values retain marker-only output. Both versions retain the
eight-cause limit, outer-to-inner order, explicit truncation and bounded HTTP
status. Success output and cancellation remain unchanged. This is private
operator evidence, not a portable receipt or a production acceptance verdict.

## Evidence

The accompanying `evidence.zip` contains personally executed commands, logs,
TRX results, mutations, compiled-process receipts and source hashes. It also
preserves the complete earlier terminal Azure evidence archive. No operation in
this code slice contacted Azure or a publisher.

The new opt-in first failed its process assertion (one failure, five passes).
The final probe/diagnostic/census selection passed 79 tests. The first full run
exposed three census failures; its output is retained. The enum pin was derived
with the existing census renderer, and the static private key is explicitly
accounted for as state rather than a token registry. The census scope and
reflection sweep were not narrowed.

Final verification on Windows with .NET SDK 10.0.400:

- Locked solution restore passed; Release build passed with zero warnings/errors.
- Full solution suite: 2,758 total, 2,752 passed, 6 skipped, 0 failed,
  1m 04.384s. Skips are the five existing opt-in publisher checks and the
  Windows symlink-permission case.
- Eight restored mutations failed: lost attribution (22 failures), message
  leakage (2), unknown setting leakage (1), conflated guard (4), admitted external
  identity endpoint (2), removed version 2 opt-in (1), external subtype metadata
  access (1), and an added unreviewed enum member (1).
- Four compiled-process cases passed: default, version 1, version 2 and invalid
  selector. All used a fake forbidden credential, returned exit 1 and empty
  stdout, and emitted no fake credential value.

For mutation reproduction, extract the archive to `artifacts/config-diagnostics`
and run `python artifacts/config-diagnostics/mutate.py` in a disposable checkout.
The script restores source bytes in `finally`. Compiled-process checks use only
fake forbidden credentials and are reproduced by `console-probe.py` after a
Release build. No live execution is part of either script.

## Limits and next gate

No new image was built, uploaded or deployed, and no Azure execution, resource,
role, policy, retention lock or cleanup was submitted. Further live work needs
an inspected exact image, cross-family exact-head review, green required CI,
serial integration and a separately authorized operation. The previous one-start
authorization is consumed. Production retention, restore, accepted-Fact replay,
Stage 1 completion and V3 promotion remain unproved by this slice.
