# Assistant release evaluation

The assistant release gate is not a prompt-demo script. It exercises the public `/api/ask`
contract against the candidate application's versioned event stream and records the operation,
legal outcome, transport outcome, canonical arguments, typed UI effect, model-token use, segmented
latency and optional independent
answer grade for every repetition.

The frozen catalog is `evals/assistant-cases-v3.json`. It covers every legal operation, the four
prompts shown in the product, direct instruction injection and hostile quoted-evidence injection
executed as real user turns in one server-owned thread. The evaluator never supplies an assistant
transcript: every repetition starts a fresh opaque thread, carries only its returned bearer token
between setup turns, and resets it afterward. A
separate `lex-assistant-eval-review/1` attestation binds an independent reviewer and approval to
the exact SHA-256 of that file. Its detached ECDSA signature must verify against a dedicated,
pinned evaluation-review trust root. Editing a question, expected argument, rubric or budget
invalidates both the digest match and signature-backed approval.

The human reviewer uses `evals/sign-assistant-review.ps1` only after reading the frozen catalog.
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
- more than 20 cases or three repetitions;
- a reservation above either model's frozen token budget or EUR 10;
- pricing that differs from the reviewed, model-version-and-SKU-bound Microsoft Retail Prices snapshot;
- a case whose declared limits exceed the catalog budget.

The candidate exchanges the signed capability once for a random opaque token held only in evaluator
memory. Its bounded server registry consumes a request only after the idempotency registry returns
the owner; duplicate joins/replays spend neither another capability slot nor another model call.
This lane consumes neither public daily counter, but retains the same concurrency, queue, request,
deadline and model limits. Forged, expired, wrong-release, off-catalog and max+1 requests fail closed.

During the run, deterministic contract checks never fall back to keyword matching. Every frozen
case also requires the configured independent grader; an unavailable or malformed grader fails
the case. Publisher-text, metadata and tool-output injection are additionally exercised at the
code-enforced evidence boundary by the release test suite; the external black-box cases exercise
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
  -GraderDeployment <independent-deployment>
```

`AOAI_GRADER_KEY` is read from the environment by default and is never written to the report.
The runner obtains the candidate revision's code, image, resources, zero traffic weight, model
configuration and signed-index manifest set from Azure Resource Manager and `/attestation.json`;
callers cannot label a different service as the candidate. It re-resolves the same revision after
the run, so a traffic or identity change invalidates the report. The wrapper owns a bounded
inactive-to-active-to-inactive candidate lifecycle and verifies that exactly one production quota
authority remains afterward, including on failure. It also obtains both deployments' resource,
endpoint, model name, immutable model version and SKU from Azure Resource Manager before inference.
Candidate and grader usage and EUR prices are reserved, measured and gated independently. The
catalog binds the exact model versions, GlobalStandard meter IDs, effective dates, EUR rates,
Microsoft Retail Prices source and a maximum seven-day validity window; the CLI accepts no price
override.
Per-case timing records planner, MCP, submit-to-first-`operation_result`, explicit synthesis,
transport/queue residual and terminal duration. The release gate enforces first-result p95 at 15 s,
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
After a passing run, `deploy/publish-assistant-evaluation.ps1` uploads a four-file draft evidence
set, then dispatches the public `lex-ops` publication workflow. For a normal release that workflow
temporarily activates the zero-traffic candidate, revalidates the report, runs the exact-code
Chromium presentation gate, adds `assistant-browser-evidence.json`, and returns the candidate to
inactive state. The one-time bootstrap instead requires the already bounded state A active/100,
R inactive/0 and C active/0; failure abandons C, while successful publication leaves C active only
for the two-hour signed promotion window. Only the OIDC
publisher can bind the resulting five-file set to the candidate revision, code, index-manifest set,
catalog and browser-evidence digests, sign its whole-artifact manifest with `keyvault-lex-v2`,
reverify it, and publish the release. The standard public package has those five evidence files
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
