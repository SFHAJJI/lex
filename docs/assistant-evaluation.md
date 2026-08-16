# Assistant release evaluation

The assistant release gate is not a prompt-demo script. It exercises the public `/api/ask`
contract against the candidate application's versioned event stream and records the operation,
legal outcome, transport outcome, canonical arguments, typed UI effect, model-token use, segmented
latency and the separate release answer grade for every repetition.

The frozen catalog is `evals/assistant-cases-v3.json`. Catalog author: `Lex release engineering`.
Evaluation reviewer: **Soufien Hajji**. The reviewer is separate from the catalog-author
identity and uses a separate signing authority; this is project-owner review, not an external,
third-party or legal audit. The owner-reviewed and signed 25-case catalog covers every legal operation, the four
prompts shown in the product, direct instruction injection and hostile quoted-evidence injection
executed as real user turns in one server-owned thread. The evaluator never supplies an assistant
transcript: every repetition starts a fresh opaque thread, carries only its returned bearer token
between setup turns, and resets it afterward. A
separate `lex-assistant-eval-review/1` attestation binds that reviewer and approval to
the exact SHA-256 of that file. Its detached ECDSA signature must verify against a dedicated,
pinned evaluation-review trust root. Editing a question, expected argument, rubric or budget
invalidates both the digest match and signature-backed approval.

Soufien Hajji uses `evals/sign-assistant-review.ps1` only after reading the frozen catalog.
The script signs the exact catalog digest with the pinned, non-exportable review-key version and
immediately runs the embedded-root verifier; it does not authorize inference by itself. Immediately
before a release run, `evals/sign-assistant-admission.ps1` verifies that approval again and uses the
same non-exportable review key to sign a distinct 20-minute capability. That capability binds the
exact candidate revision, image, code commit, index-manifest set, catalog digest, maximum candidate
tokens/EUR, nonce and every allowed `(Idempotency-Key, request-body SHA-256)` pair. Signed invocation
and turn coordinates additionally require each repetition to start without a thread token and bind
its later setup/final turns to the one opaque server thread created by that first turn.

Before the first HTTP or model call, the runner rejects:

- an absent, unsigned, untrusted, self-authored, stale or digest-mismatched review;
- an incomplete code/index/model/resource identity;
- a grader deployment or HTTPS model resource shared with the candidate models;
- more than 25 cases or three repetitions;
- a reservation above either model's frozen token budget or the outer EUR 10 catalog ceiling;
- pricing that differs from the reviewed, model-version-and-SKU-bound Microsoft Retail Prices snapshot;
- a case whose declared limits exceed the catalog budget.

The candidate exchanges the signed capability once for a random opaque token held only in evaluator
memory. Its bounded server registry consumes a request only after the idempotency registry returns
the owner; duplicate joins/replays spend neither another capability slot nor another model call.
This lane consumes neither public daily counter, but retains the same concurrency, queue, request,
deadline and model limits. Forged, expired, wrong-release, off-catalog and max+1 requests fail closed.

During the run, deterministic contract checks never fall back to keyword matching. Reviewed common
arguments must all match, and a case with `argument_alternatives` must match one complete reviewed
alternative. The EU in-force case therefore accepts `publisher=eu-eurlex` or `jurisdiction=EU`, but
never an unscoped call. Alternative-owned values must also be consistent: exact redundant EU scope
is permitted, but `jurisdiction=EU` together with `publisher=lu-legilux` fails. A compound case
checks every typed operation and primary call in reviewed order. A setup turn that declares its own
expected contract is checked before the final continuation is allowed. Every frozen case is also
measured by the configured separate release grader, which compares the question and one relevance
rubric with the bounded final reply, typed operations and trace and returns a score of 1 to 5. That
score asks the only thing code cannot decide, whether the answer addressed the question that was
asked at the right scope, and it is reported rather than gated: the deterministic checks above
decide promotion on their own. Groundedness is deliberately not asked, because it is enforced
structurally and a judge re-checking it would measure the architecture rather than the answer. A
grader call that fails is recorded as an absent measurement naming its cause, never as a passing
score, while a run configured with no separate grader at all still fails closed. Publisher-text, metadata and tool-output injection are additionally exercised at the
code-enforced evidence boundary by the release test suite; the black-box cases exercise
same-thread user and quoted-evidence channels through the production opaque-thread API.
Measured assistant and grader usage must remain within both the per-case ceilings and aggregate
budget. The output report contains no prompts, legal text, credentials or raw tool payloads.
Free-form grader reasons are deliberately discarded rather than copied into release evidence.

Run the strict wrapper from PowerShell 7.2 or later. Each local evaluation and publication script
uses `#Requires -Version 7.2`, so Windows PowerShell 5.1 refuses before any candidate lifecycle or
release mutation begins.

Normal rollback uses the exact previously evaluated production revision retained by the preceding
promotion. The prior release-state receipt binds that exact revision and immutable image; evidence
signed for another revision is never interchangeable. Ordinary `lex-release-state-receipt/3`
records carry distinct target and rollback authorization records, including each revision's own
evidence-release tag. A successful transition swaps the former target authorization into the new
rollback slot, so rollback verification always downloads the retained target's evidence rather
than reusing the current release's evidence. The sole exception is the first official
legacy bootstrap: exact candidate C is evaluated, while a dedicated signed
`lex-first-release-equivalence/1` artifact binds fallback R and C to one immutable image and one
canonical Container Apps template digest excluding only `revisionSuffix`, plus the exact frozen
case-catalog SHA-256 from C's evaluation release. R is explicitly an
equivalent first-release fallback, not an independently evaluated release. An emergency C-to-R
move verifies exact evaluated C plus that signed equivalence and its successful, resource-bound
first-release receipt; it never runs C-bound `verify-release` as though R were C. Because R predates
C, a direct later promotion is refused: first move forward to exact evaluated C using the rollback
receipt emitted by the emergency transition. The exception then ends, and the next normal
promotion retains exact C.

```powershell
./evals/run-assistant-eval.ps1 `
  -BaseUrl https://candidate.example `
  -ReviewAttestation ./evals/assistant-cases-v3.review.json `
  -ReviewSignature ./evals/assistant-cases-v3.review.sig `
  -Admission ./artifacts/assistant-eval-admission.json `
  -AdmissionSignature ./artifacts/assistant-eval-admission.sig `
  -Output ./artifacts/assistant-eval-report.json `
  -CandidateContainerAppResourceId /subscriptions/<id>/resourceGroups/rg-platform/providers/Microsoft.App/containerApps/ca-lex-web `
  -CandidateRevision ca-lex-web--<immutable-suffix> `
  -CandidateModelResourceId /subscriptions/<id>/resourceGroups/<rg>/providers/Microsoft.CognitiveServices/accounts/<candidate> `
  -CandidateDeployment <candidate-deployment> `
  -GraderModelResourceId /subscriptions/<id>/resourceGroups/<rg>/providers/Microsoft.CognitiveServices/accounts/<grader> `
  -GraderDeployment <separate-grader-deployment>
```

For the one-time first-official bootstrap, run the same command with
`-BootstrapFirstOfficial`. That mode accepts only the already-active candidate C at exactly zero
traffic, does not activate or deactivate it, and revalidates active/zero-traffic state after the
evaluation even when evaluation fails. Without the switch, the ordinary contract is unchanged:
the candidate must start inactive at zero traffic and the wrapper returns it to that state.

`AOAI_GRADER_KEY` is read from the environment by default and is never written to the report.
The runner obtains the candidate revision's code, image, resources, zero traffic weight, model
configuration and signed-index manifest set from Azure Resource Manager and `/attestation.json`;
callers cannot label a different service as the candidate. It re-resolves the same revision after
the run, so a traffic or identity change invalidates the report. In ordinary mode, the wrapper owns
a bounded inactive-to-active-to-inactive candidate lifecycle and verifies that exactly one
production quota authority remains afterward, including on failure. It also obtains both deployments' resource,
endpoint, model name, immutable model version and SKU from Azure Resource Manager before inference.
Candidate and grader usage and EUR prices are reserved, measured and gated separately. The current
25-case catalog reserves at most 928,000 candidate input tokens, 92,000 candidate output tokens,
815,104 grader input tokens and 384,000 grader output tokens. At the frozen meter prices that is
EUR 0.5356487, below the outer EUR 10 limit. Its signed admission plan contains 56 candidate HTTP
requests, 8 same-thread setup requests and 48 final requests, and a passing run makes 48 separate
release-grader requests. Preflight stops before inference when the reservation
cannot fit, and measured use is gated again afterward. The limit is not a live Azure billing
listener and cannot interrupt an in-flight model call; the signed call plan and per-call token
ceilings provide the hard execution bound. The
catalog binds the exact model versions, GlobalStandard meter IDs, effective dates, EUR rates,
Microsoft Retail Prices source and a maximum seven-day validity window; the CLI accepts no price
override.
Per-case timing records planner, MCP, submit-to-first-`operation_result`, explicit synthesis,
transport/queue residual and terminal duration. A multi-turn case sums setup and final terminal
durations; its planner, MCP, first-result and residual samples retain the slowest turn. Setup work
therefore counts toward both the per-case ceiling and aggregate latency gate. Best-effort thread
reset is cleanup rather than candidate inference. The release gate enforces first-result p95 at 15 s,
every first result at 25 s, synthesis p95 at 45 s, and the frozen residual/terminal envelopes. The
catalog contains both synthesis-required and synthesis-forbidden cases, so an absent synthesis
sample cannot pass as zero latency. Every streamed operation payload must also equal its terminal
typed operation; a fast placeholder cannot authorize a slower result. The residual is not
mislabelled as pure network time. Browser received-to-presented performance comes from five
independent Playwright operations against the exact revision FQDN, with no route mock; the HTTP
evaluator does not invent a paint duration. The dedicated
evaluation-review public key is embedded in the evaluator release; callers cannot replace its
trust root. The matching non-exportable private key is
`kv-lex-eval-review/lex-evaluation-review-v1`. Its RBAC-enabled vault grants no access to the
artifact publisher identity, so case approval and artifact publication are separate authorities.
The candidate returns the admission run identity during the signed-envelope exchange; the runner
records that server-confirmed identity and the exact admission SHA-256 in report schema `/3`.
Publication retains `assistant-eval-admission.json` and its detached signature as required assets.
Offline verification checks the owner signature and short-lived window at the report's historical
`run_at`, then reconstructs the complete request plan from the reviewed catalog and rejects any
drift in calls, request hashes, turns, token or cost budgets, or expiry. Report freshness remains a
separate check at promotion time.
The offline CLI contract is `lex assistant-eval verify-report --report FILE --cases FILE
--review-attestation FILE --review-signature FILE --admission FILE --admission-signature FILE ...`.
After a passing run, `deploy/publish-assistant-evaluation.ps1` uploads a six-file draft evidence
set, then dispatches the public `lex-ops` publication workflow. For a normal release that workflow
temporarily activates the zero-traffic candidate, revalidates the report, runs the exact-code
Chromium presentation gate, adds `assistant-browser-evidence.json`, and returns the candidate to
inactive state. The one-time bootstrap instead requires the already bounded state A active/100,
R inactive/0 and C active/0; failure abandons C, while successful publication leaves C active only
for the two-hour signed promotion window. The publication helper enables this path only when its
three equivalence arguments are supplied together:

```powershell
./deploy/publish-assistant-evaluation.ps1 `
  -Report ./artifacts/assistant-eval-report.json `
  -Cases ./evals/assistant-cases-v3.json `
  -ReviewAttestation ./evals/assistant-cases-v3.review.json `
  -ReviewSignature ./evals/assistant-cases-v3.review.sig `
  -Admission ./artifacts/assistant-eval-admission.json `
  -AdmissionSignature ./artifacts/assistant-eval-admission.sig `
  -CandidateRevision ca-lex-web--<candidate> `
  -BootstrapRollbackRevision ca-lex-web--<fallback> `
  -BootstrapCanonicalTemplateDigest sha256:<64-lowercase-hex> `
  -BootstrapExpectedImageDigest sha256:<64-lowercase-hex>
```

Only the OIDC
publisher can bind the resulting seven-file set to the candidate revision, code, index-manifest set,
catalog, signed-admission run identity and digest, and browser-evidence digest, sign its
whole-artifact manifest with `keyvault-lex-v2`,
reverify it, and publish the release. The standard public package has those seven evidence files
plus its manifest and signature. The one-time bootstrap adds a separate three-file equivalence
package: `bootstrap-equivalence.json`, its dedicated one-file manifest and its signature. The
publisher re-reads the complete exact A/R/C Azure state, signs that package independently, invokes
`assistant-eval verify-bootstrap-equivalence`, uploads only to an exact draft, then re-reads and
reverifies A/R/C immediately before publication. Before the potentially ambiguous publish API call,
the publisher relinquishes automatic candidate-cleanup ownership so a successful public release can
never be followed by C deactivation and R purging. It then downloads every asset anonymously and
compares its SHA-256 and byte length. A public release is immutable to this workflow: an interrupted
public read-back requires explicit reconciliation and can never authorize `--clobber` on a retry.
Production promotion accepts only the appropriate fixed release shape and revalidates it against
Azure and the live candidate before changing traffic. Standard GitHub-hosted runners are free for
this public repository; no private-repository Actions minutes are consumed. Exit code `0`
means every repetition and budget gate passed; exit code `5` means evidence was produced but the
candidate is not authorized for promotion. Invalid catalog, review or release identity fails
before inference.

The public release dossier contains the field-by-field catalog glossary. The current owner-signed
25-case live map and the division between live large language model (LLM), deterministic
integration, browser and controlled transport coverage are recorded in
[`assistant-evaluation-scenario-matrix.md`](assistant-evaluation-scenario-matrix.md). Only the exact
catalog digest covered by `assistant-cases-v3.review.json` and its detached signature may authorize
inference; any catalog-byte change requires a new owner review and signature.

## One verdict is doing two jobs

`activation_gate_passed` currently answers two different questions at once: were the answers right,
and were they produced within budget. Both are legitimate release gates and neither should be
dropped, but they are not the same gate. A wrong law and an expensive answer fail identically today,
so the verdict cannot be diagnosed from the outside without reading the per-result reasons.

That is the same category error the runner made one layer down, where two bare catches collapsed
local validation rejections, contract-shape assertions and genuine network faults into one string
and 22 of 34 failures became unexplainable. For a product whose claim is that an answer can be
checked rather than trusted, a verdict you cannot check is off message.

The intended shape is two verdicts. Correctness answers whether every repetition met its typed
contract and its reviewed rubric. Budget answers what the run cost in tokens, latency and euros,
against its own thresholds and its own owner. Then a cost regression is visible without being
mistaken for a legal error, and a legal error is never excused by having been cheap.

Until that split lands, the per-case ceilings are sized at three times the worst value measured on
code whose typed contract passes, so that no ceiling can decide a correctness result. They remain
runaway guards, which is the only job a ceiling should have. The measurements behind those numbers
were taken against a local artifact mounting the candidate's exact signed index set: worst candidate
input 6,162 tokens, worst candidate output 4,881, worst latency 38,112 ms, and a largest grader
evidence payload of 49,547 characters. The previous 3,000 token output ceiling was below the only
case required to synthesise, and the previous 20,000 token grader input ceiling sat inside
measurement error of that largest payload.

## One case runs once, and why

`quoted-tool-evidence-remains-data` carries one repetition where every other case carries two. That
is a reduction in coverage and it is recorded here rather than left to be discovered.

Two official runs against the same candidate failed on its second repetition and only its second,
with the evaluator reporting `assistant evaluation target unavailable: InvalidDataException`. The
report row for that repetition shows zero candidate tokens and synthetic timings, which is what the
runner writes when the invocation throws before recording anything, so the elapsed figure in it
measures the whole conversation rather than any call.

The cause was narrowed and not established. The exception type is the evidence: every other throw on
that path carries a named cause, and the bare `InvalidDataException` remains only on the stream-shape
validations, which is what a candidate reply carrying `transport_error` instead of a terminal
`operation_result` produces. So the candidate returned 502 or 500 and the evaluator then validated a
body that was never sent. The elapsed time rules out a timeout. What returned the non-200 is upstream
of anything the report carries.

This case has the heaviest planner in the catalog, roughly twice the next, and fires three planner
calls per repetition, so its second repetition arrives immediately after the first has spent about
twelve thousand tokens. That is a hypothesis about quota, not a finding, and it is the first thing to
test.

Two things follow, and both are owed. The evaluator should treat `transport_error` as a terminal
outcome and report the HTTP status rather than a shape violation, so this failure names itself next
time. And the second repetition should return once the upstream cause is understood, because it is
the repetition that found this at all.
