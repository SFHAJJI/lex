# MCP 2.0 migration

Lex MCP 2.0 makes the legal result contract explicit. Normal request shapes remain compatible.
Successful envelopes now include a bounded `artifact` object, and `changes_in_period` includes
the exact selected population. HTTP and stdio advertise the same version from
`McpSdkBridge.ServerVersion`.

## What changed

- The legal status vocabulary is closed. Clients can validate every value against
  `McpStatus.All`.
- `outside_observed_window` was removed. Lex never emitted it. Use `no_version_for_date` for a
  held work with no publisher version covering the requested date, then inspect `coverage` and
  `history_begins` when the observed population matters.
- Legal outcomes and transport outcomes are separate in the application adapter. A timeout,
  cancellation, upstream failure, or quota rejection cannot be presented as a legal result.
- Every envelope carries signed artifact coordinates: the verified manifest-set identity when
  the host has one, plus the index content digest, build code commit and index format.
- `changes_in_period` counts publisher version dates. Its `population` object gives the
  distinct non-withdrawn works in the selected publisher and legal-metadata scope, the expected
  inventory where signed, and known exclusions.
- The application freezes a complete, server-normalized operation plan before state calls. This
  internal contract does not add fields to MCP requests.

## Status mapping

| MCP status | Meaning |
|---|---|
| `ok` | The requested legal operation succeeded. |
| `no_result`, `no_changes_in_period` | The operation succeeded and the requested population is empty. |
| `profiles_differ` | Both versions exist, but their extraction profiles do not support a provision comparison. |
| `unknown_work`, `unknown_anchor` | The requested work or provision identifier is not held. |
| `no_version_for_date`, `anchor_not_in_version`, `no_provision_history` | The requested legal state is not available in the held publisher history. |
| `text_not_available`, `text_withheld` | The record is held, but provision text cannot be served. |
| `no_corpus_mounted` | The server has no verified legal index mounted. |

Unknown status values must fail closed. They must not be treated as empty success or transport
errors. Existing clients may ignore the new additive envelope and population fields.
