# MCP 2.0 migration

Lex MCP 2.0 makes the legal result contract explicit and closes every tool input. Requests that
sent unknown fields, strings or lists beyond the limits below, non-integer pagination, or invalid
enum values in 1.x are rejected in 2.0. HTTP, stdio and `server.json` all advertise `2.0.0`.

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

## Input limits

Every request body is at most 65,536 bytes. Every tool rejects unknown fields and non-scalar
arguments. Dates are exact `YYYY-MM-DD` strings. General strings are 1 to 1,000 characters;
publisher, jurisdiction and enum strings are at most 64; language is at most 16; an anchor is at
most 512. Comma-separated `anchors` and `works` contain at most 50 values; each anchor remains
subject to the 512-character ceiling.

Work-specific calls require a publisher-qualified `work`/`lex_id` such as
`eu-eurlex:32013r0575`, or an explicit `publisher` alongside a verbatim publisher identifier.
This prevents ambiguous cross-publisher lookup and keeps request-time database work bounded.

| Tool | Pagination and closed values |
|---|---|
| `as_of` | `mode`: `full`, `outline`, `select`; `select` requires anchors; anchors are valid only for `outline` or `select`. Optional `version_key` is an opaque string of at most 128 characters returned by `timeline` or an `ambiguous_version` choice. |
| `timeline` | limit 1..200, default 100; offset 0..100,000. |
| `in_force_on` | limit 1..100, default 50; offset 0..100,000. |
| `diff` | optional anchor is at most 512 characters. Optional `from_version_key` and `to_version_key` are opaque strings of at most 128 characters returned by `timeline` or an `ambiguous_version` choice. |
| `search` | limit 1..50, default 10; retrieval mode `keyword` or `hybrid`; time scope `all_versions` or `as_of`; fuzzy `auto` or `off`; `as_of` is required for the matching time scope. |
| `article_history` | no client pagination; output is bounded as described below. |
| `provenance` | no client pagination; output is bounded as described below. |
| `coverage` | optional publisher only. |
| `cited_by` | limit 1..100, default 50. |
| `changes_in_period` | limit 1..100, default 20; offset 0..100,000; order `by_date` or `by_churn`. |

Limits on collection-wide tools are response-wide across publishers, not repeated per mounted
index. At most eight publisher result envelopes are returned.

## Bounded output and overloads

- `as_of full` returns at most 2,000 provision rows, 250,000 UTF-8 legal-text bytes and 100
  citation rows across the response. `text_omitted`, `text_bytes`, `text_truncated`,
  `citations_truncated`, totals and permalinks make every omission explicit.
- `article_history` returns at most 500 states and 500 anchor events. `provenance` returns at most
  1,000 events and 1,000 observations. Coverage returns at most 100 document types, 100 languages
  and 100 build issues across publishers. `truncated` and the corresponding total fields identify
  a partial response.
- The public endpoint admits eight MCP executions, queues sixteen for at most two seconds, and
  admits two hybrid searches. Rolling limits are 120 calls per client and 600 globally per minute.
  Saturation returns HTTP 503 with JSON-RPC code `-32001` and `data.status=busy`; rolling-limit
  exhaustion returns HTTP 429 with code `-32002` and `data.status=rate_limited`. Neither condition
  is a tool result or a legal no-result status. Retry after load or the rolling window clears.
- Invalid arguments are bounded `isError` tool results. Unknown tools are JSON-RPC invalid-params
  errors; unexpected server failures are sanitized JSON-RPC internal errors. Exception messages
  and internal paths are never returned.

## Status mapping

| MCP status | Meaning |
|---|---|
| `ok` | The requested legal operation succeeded. |
| `no_result`, `no_changes_in_period` | The operation succeeded and the requested population is empty. |
| `profiles_differ` | Both versions exist, but their extraction profiles do not support a provision comparison. |
| `ambiguous_version` | More than one publisher-identified state is effective at the requested boundary. The response returns at most 20 exact opaque choices; retry with `version_key`, or `from_version_key`/`to_version_key`. |
| `unknown_work`, `unknown_anchor` | The requested work or provision identifier is not held. |
| `no_version_for_date`, `anchor_not_in_version`, `no_provision_history` | The requested legal state is not available in the held publisher history. |
| `text_not_available`, `text_withheld` | The record is held, but provision text cannot be served. |
| `unknown_publisher` | The `publisher` or `jurisdiction` filter names nothing this server mounts. The payload carries `requested_filter`, `requested_value`, `mounted_publishers` and `mounted_jurisdictions`. |
| `no_corpus_mounted` | The server has no verified legal index mounted. |

Behaviour change with `unknown_publisher`: `coverage`, `search`, `in_force_on` and
`changes_in_period` filtered by a publisher or jurisdiction this server does not mount used to
return a bare `[]`, which no client could distinguish from an empty corpus. They now return the
status object above. Callers that treated `[]` as "nothing is held" must read the status instead.

Unknown status values must fail closed. They must not be treated as empty success or transport
errors. Clients must inspect truncation fields before claiming that returned text or rows are
complete.

## Exact publisher-version coordinates

Publisher versions are identity units, not language rows and not dates alone. `timeline` returns
one version unit with its available language expressions nested beneath it. Every version unit has
an opaque `version_key`; exact permalinks and comparison links use that key. A friendly bare-date
web route remains canonical when exactly one publisher state is effective on that date.

Some publishers can expose two independently identified states with the same `valid_from`. Both
states cover the whole shared interval until the next later publisher boundary. A bare `as_of`,
`in_force_on`, web date route, or either date boundary of `diff` therefore returns
`ambiguous_version` anywhere in that interval, not only on its first day. Choices are bounded to 20
and carry exact keys. Supplying an exact key succeeds only when that state covers the requested
boundary; a key cannot select an unrelated date or work.

These fields are additive and backward-compatible for unambiguous histories. The one intentional
behaviour correction is that a request which 1.x/early 2.0 silently resolved to one of several
same-boundary publisher states now fails honestly with `ambiguous_version` and requires an exact
choice. Date-only callers must handle that typed clarification status.
