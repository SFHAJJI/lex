using System.Diagnostics;
using System.Text;
using System.Text.Json.Nodes;
using System.Security.Cryptography;
using Lex.Ask;
using Lex.Evaluation;
using Lex.Index;
using static Lex.Web.PageShell;
using static Lex.Web.Fragments;

namespace Lex.Web;

/// <summary>
/// The machine-facing surface: the public MCP endpoint, the assistant's two POST routes, and the two flat files a crawler and a load balancer ask for. Nothing here renders a page.
/// </summary>
public static class ApiEndpoints
{
    public static IEndpointRouteBuilder MapApi(this IEndpointRouteBuilder app, WebContext ctx)
    {
        var readers = ctx.Registry.All;
        var askService = ctx.Ask;
        var askRequests = ctx.AskRequests;
        var askThreads = ctx.AskThreads;
        var evaluationVerifier = ctx.EvaluationAdmissionVerifier;
        var evaluationAdmissions = ctx.EvaluationAdmissions;
        static string ClientAddress(HttpRequest request) =>
            request.Headers["X-Forwarded-For"].FirstOrDefault()?.Split(',')[^1].Trim()
            ?? request.HttpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";
        static string Fingerprint(
            byte[] body,
            string? threadToken,
            string purpose = "ask-v2",
            string? evaluationToken = null)
        {
            var evaluationScope = EvaluationOwnerScope(evaluationToken);
            var scope = Encoding.UTF8.GetBytes(evaluationScope is null
                ? $"{purpose}\n{threadToken ?? "-"}\n"
                : $"{purpose}\n{threadToken ?? "-"}\n{evaluationScope}\n");
            var committed = new byte[scope.Length + body.Length];
            Buffer.BlockCopy(scope, 0, committed, 0, scope.Length);
            Buffer.BlockCopy(body, 0, committed, scope.Length, body.Length);
            return Convert.ToHexStringLower(SHA256.HashData(committed));
        }
        static JsonArray HistoryFor(AskThreadLease thread, string message)
        {
            var history = thread.History.DeepClone().AsArray();
            history.Add(new JsonObject { ["role"] = "user", ["content"] = message });
            return history;
        }
        static string? AssistantReply(JsonObject body) =>
            body["clarification"]?["question"]?.GetValue<string>()
            ?? body["reply"]?.GetValue<string>();
        static void PrivateNoStore(HttpResponse response)
        {
            response.Headers.CacheControl = "private,no-store,max-age=0";
            response.Headers.Pragma = "no-cache";
            response.Headers.Expires = "0";
        }
        string Page(string title, string body, string? subtitle = null, string nav = "",
                    string? h1 = null, string? canonicalPath = null, string? jsonLd = null,
                    string? description = null, string? lang = null)
            => PageShell.Page(ctx.PublicBase, title, body, subtitle, nav, h1, canonicalPath,
                              jsonLd, description, lang);

        app.MapPost("/api/ask/evaluation/admission", async (
            HttpRequest req, HttpResponse res) =>
        {
            PrivateNoStore(res);
            if (req.ContentLength is > EvaluationAdmissionContract.MaximumBytes)
                return Results.Json(
                    new { error = "Evaluation admission is too large." }, statusCode: 413);
            var bytes = await BoundedRequestBody.ReadAsync(
                req.Body, EvaluationAdmissionContract.MaximumBytes,
                req.HttpContext.RequestAborted);
            if (bytes is null)
                return Results.Json(
                    new { error = "Evaluation admission is too large." }, statusCode: 413);
            if (!TryEvaluationSignature(req.Headers, out var signature))
                return Results.Json(
                    new { error = "Invalid evaluation admission signature." }, statusCode: 400);
            EvaluationAdmissionCapability capability;
            try
            {
                capability = evaluationVerifier.Verify(bytes, signature);
            }
            catch (Exception exception) when (exception is InvalidDataException
                                               or CryptographicException)
            {
                return Results.Json(
                    new { error = "Evaluation admission was not authorized." }, statusCode: 401);
            }
            var registered = evaluationAdmissions.Register(capability, bytes.Length);
            return registered.Kind switch
            {
                EvaluationAdmissionRegistrationKind.Registered => Results.Json(new
                {
                    evaluation_token = registered.Token,
                    expires_at = registered.ExpiresAt,
                    max_calls = capability.MaxCalls,
                }),
                EvaluationAdmissionRegistrationKind.Replayed => Results.Json(
                    new { error = "Evaluation admission was already exchanged." },
                    statusCode: 409),
                _ => Results.Json(
                    new { error = "Evaluation admission capacity is unavailable." },
                    statusCode: 503),
            };
        });

        // A crawler that is allowed everywhere still has to FIND everything. Without the
        // sitemap line it had to walk /browse fifty works at a time across twenty-nine pages.
        app.MapGet("/robots.txt", () => Results.Text(
            $"User-agent: *\nAllow: /\nSitemap: {ctx.PublicBase}/sitemap.xml\n"));

        // Every work, plus the pages worth indexing. Version URLs are emitted separately below;
        // counts always come from the mounted readers rather than a comment that goes stale.
        app.MapGet("/sitemap.xml", () =>
        {
            var sb = new StringBuilder();
            sb.Append("""<?xml version="1.0" encoding="UTF-8"?>""");
            sb.Append("""<urlset xmlns="http://www.sitemaps.org/schemas/sitemap/0.9">""");

            void Url(string path, string priority, string freq)
                => sb.Append($"<url><loc>{ctx.PublicBase}{path}</loc>"
                             + $"<changefreq>{freq}</changefreq><priority>{priority}</priority></url>");

            Url("/", "1.0", "daily");
            foreach (var p in new[] { "/browse", "/coverage", "/decisions", "/built",
                                      "/built/model", "/built/data", "/built/retrieval",
                                      "/built/assistant", "/built/release", "/built/decisions",
                                      "/built/incidents", "/built/limits", "/about",
                                      "/how-it-works", "/developers", "/verify",
                                      "/benchmarks", "/stories", "/find" })
                Url(p, "0.8", "weekly");

            // lastmod is when the PAGE last changed, so it can never be in the future.
            //
            // This first shipped using the work's latest valid_from, which is a different thing
            // entirely: valid_from is when a law takes effect, and 23 works in the corpus are
            // already published with a commencement date years out, one of them in 2030. Search
            // Console rejected every one of them as an invalid date.
            //
            // observed_from is the honest field: when a record for this work entered the corpus.
            // It also carries real signal, because it distinguishes the works the last nightly
            // run actually touched from the ones that have not moved since the first ingest.
            // A row without it simply omits the tag, which is allowed, rather than guessing.
            var today = ctx.Today;
            string Lastmod(string? observed) =>
                observed is { Length: >= 10 } o && TryIsoDate(o[..10], out var d) && d <= today
                    ? $"<lastmod>{d:yyyy-MM-dd}</lastmod>" : "";

            foreach (var r in ctx.Registry.Values)
            {
                var (rows, _) = r.Catalogue(new FilterSet(null, null, null, null), null,
                                            CatalogueOrder.Name, 20000, 0);
                foreach (var w in rows)
                    sb.Append($"<url><loc>{ctx.PublicBase}/{w.Collection}/{w.GroupKey}</loc>"
                              + Lastmod(w.LastObserved)
                              + "<changefreq>monthly</changefreq><priority>0.6</priority></url>");

                // The version pages, which is where the law actually is.
                //
                // These were left out at first as "near-duplicates of one another that the work
                // page links anyway". That was wrong twice over. They are not duplicates: each is
                // a distinct legal state with different text, and each already canonicalises to
                // the date its own interval starts, so the set below is exactly the set of
                // canonical addresses. And they are not incidental: a work page is 553 words of
                // navigation, while a version page is the tens of thousands of words of law that
                // a reader searched for. Submitting the index and withholding the content is the
                // wrong way round.
                foreach (var (collection, groupKey, versionKey, observed) in r.VersionPaths())
                    sb.Append($"<url><loc>{ctx.PublicBase}/{collection}/{groupKey}/{versionKey}</loc>"
                              + Lastmod(observed)
                              + "<changefreq>yearly</changefreq><priority>0.5</priority></url>");
            }
            sb.Append("</urlset>");
            return Results.Content(sb.ToString(), "application/xml");
        });

        // ---- /ask playground: chat over the MCP tools, grounded and capped ----
        app.MapGet("/ask", () => Results.Redirect("/", permanent: true));

        app.MapPost("/api/ask", async (HttpRequest req, HttpResponse res) =>
        {
            PrivateNoStore(res);
            if (req.ContentLength is > 65536) return Results.Json(new { error = "Request too large." }, statusCode: 413);
            var body = await BoundedRequestBody.ReadAsync(req.Body, 65536, req.HttpContext.RequestAborted);
            if (body is null) return Results.Json(new { error = "Request too large." }, statusCode: 413);
            JsonNode? parsed;
            try { parsed = JsonNode.Parse(body); }
            catch { return Results.Json(new { error = "Bad JSON." }, statusCode: 400); }
            if (!TryAskMessage(parsed, out var message, out var messageError))
                return Results.Json(new { error = messageError }, statusCode: 400);
            if (!TryThreadToken(req.Headers, out var threadToken))
                return Results.Json(new { error = "Invalid X-Lex-Thread-Token." }, statusCode: 400);
            if (!TryIdempotencyKey(req.Headers, out var idempotencyKey))
                return Results.Json(new { error = "Invalid Idempotency-Key." }, statusCode: 400);
            if (!TryEvaluationToken(req.Headers, out var evaluationToken))
                return Results.Json(
                    new { error = "Invalid X-Lex-Evaluation-Admission." }, statusCode: 400);
            var requestBodySha256 = Convert.ToHexStringLower(SHA256.HashData(body));
            if (evaluationToken is not null
                && !EvaluationInspectionAccepted(evaluationAdmissions.Inspect(
                    evaluationToken, idempotencyKey, requestBodySha256, threadToken),
                    out var evaluationStatus))
                return Results.Json(
                    new { error = "Evaluation request was not authorized." },
                    statusCode: evaluationStatus);
            // Last X-Forwarded-For element: appended by our ingress, not spoofable by the client
            // (the first element is client-controlled and would reset the per-IP cap).
            var ip = ClientAddress(req);
            var claim = askRequests.Claim(
                ip, idempotencyKey, Fingerprint(
                    body, threadToken, evaluationToken: evaluationToken));
            var requestId = claim.RequestId;
            res.Headers["X-Lex-Request-Id"] = requestId;
            if (claim.Kind != AskRequestClaimKind.Owner)
            {
                var replay = await claim.Completion.WaitAsync(req.HttpContext.RequestAborted);
                return Results.Content(replay.Body, "application/json", statusCode: replay.Status);
            }
            using var evaluationUse = evaluationToken is null
                ? null
                : evaluationAdmissions.Reserve(
                    evaluationToken, idempotencyKey, requestBodySha256, threadToken);
            if (evaluationToken is not null && evaluationUse is null)
            {
                const string denied = "{\"error\":\"Evaluation request was already used.\"}";
                claim.Complete(409, denied, retainForReplay: false);
                return Results.Content(denied, "application/json", statusCode: 409);
            }
            var acquired = await AcquireThreadAsync(
                askThreads, threadToken, req.HttpContext.RequestAborted,
                EvaluationOwnerScope(evaluationToken));
            if (acquired.Lease is not { } thread)
            {
                var json = acquired.Failure!.ToJsonString();
                claim.Complete(acquired.Status, json, retainForReplay: false);
                return Results.Content(json, "application/json", statusCode: acquired.Status);
            }
            await using (thread)
            try
            {
                var outcome = await askService.AskAsync(HistoryFor(thread, message), ip,
                    req.Host.Value ?? "law.soufien.lu", req.HttpContext.RequestAborted,
                    requestId: requestId, conversationContext: thread.Context,
                    admissionLane: evaluationToken is null
                        ? AskAdmissionLane.Public
                        : AskAdmissionLane.Evaluation);
                var (status, bodyJson) = outcome;
                var stored = status == 200 && bodyJson["error"] is null
                    && AssistantReply(bodyJson) is { } assistant
                    && thread.Commit(
                        message, assistant, outcome.ConversationContext,
                        outcome.ContextDisposition);
                if (stored || threadToken is not null)
                    bodyJson["thread_token"] = thread.Token;
                var json = bodyJson.ToJsonString();
                if (outcome.RetainForReplay)
                    evaluationUse?.Commit(stored ? thread.Token : null);
                claim.Complete(status, json, outcome.RetainForReplay);
                return Results.Content(json, "application/json", statusCode: status);
            }
            catch
            {
                const string failure = "{\"error\":\"Unexpected error in the playground.\"}";
                evaluationUse?.Commit();
                claim.Complete(500, failure, retainForReplay: true);
                return Results.Content(failure, "application/json", statusCode: 500);
            }
        });

        // ---- /api/ask/stream: the same answer, but the wait carries information.
        // A grounded answer takes 30-70s. CHI '26 (N=45, 26s and 45s waits) found that filling that
        // time with updates NAMING REAL OBJECTS beats a spinner on perceived speed, trust and load —
        // and the benefit grows with the wait. So this streams what the agent FOUND, never what it is
        // doing: "Code du travail — 3 articles as in force on 2019-03-01", not "searching…".
        app.MapPost("/api/ask/stream", async (HttpRequest req, HttpResponse res) =>
        {
            PrivateNoStore(res);
            var streamWatch = Stopwatch.StartNew();
            async Task Reject(int status, string error)
            {
                res.StatusCode = status;
                await res.WriteAsJsonAsync(new { error }, req.HttpContext.RequestAborted);
            }

            if (req.Headers["X-Lex-Stream-Version"].FirstOrDefault() != "1")
            {
                await Reject(400, "Unsupported assistant stream version.");
                return;
            }
            if (req.ContentLength is > 65536) { await Reject(413, "Request too large."); return; }
            var body = await BoundedRequestBody.ReadAsync(req.Body, 65536, req.HttpContext.RequestAborted);
            if (body is null) { await Reject(413, "Request too large."); return; }
            if (!TryIdempotencyKey(req.Headers, out var idempotencyKey))
            {
                await Reject(400, "Invalid Idempotency-Key.");
                return;
            }
            JsonNode? parsed;
            try { parsed = JsonNode.Parse(body); }
            catch { await Reject(400, "Bad JSON."); return; }
            if (!TryAskMessage(parsed, out var message, out var messageError))
            {
                await Reject(400, messageError);
                return;
            }
            if (!TryThreadToken(req.Headers, out var threadToken))
            {
                await Reject(400, "Invalid X-Lex-Thread-Token.");
                return;
            }
            if (!TryEvaluationToken(req.Headers, out var evaluationToken))
            {
                await Reject(400, "Invalid X-Lex-Evaluation-Admission.");
                return;
            }
            var requestBodySha256 = Convert.ToHexStringLower(SHA256.HashData(body));
            if (evaluationToken is not null
                && !EvaluationInspectionAccepted(evaluationAdmissions.Inspect(
                    evaluationToken, idempotencyKey, requestBodySha256, threadToken),
                    out var evaluationStatus))
            {
                await Reject(evaluationStatus, "Evaluation request was not authorized.");
                return;
            }
            var ip = ClientAddress(req);
            var claim = askRequests.Claim(
                ip, idempotencyKey, Fingerprint(
                    body, threadToken, evaluationToken: evaluationToken));
            var requestId = claim.RequestId;
            if (claim.Kind is AskRequestClaimKind.Conflict or AskRequestClaimKind.Busy
                or AskRequestClaimKind.ReplayUnavailable)
            {
                var rejected = await claim.Completion;
                res.StatusCode = rejected.Status;
                res.ContentType = "application/json";
                await res.WriteAsync(rejected.Body, req.HttpContext.RequestAborted);
                return;
            }

            AskThreadLease? thread = null;
            EvaluationAdmissionUse? evaluationUse = null;
            if (claim.Kind == AskRequestClaimKind.Owner)
            {
                if (evaluationToken is not null)
                {
                    evaluationUse = evaluationAdmissions.Reserve(
                        evaluationToken, idempotencyKey, requestBodySha256, threadToken);
                    if (evaluationUse is null)
                    {
                        const string denied =
                            "{\"error\":\"Evaluation request was already used.\"}";
                        claim.Complete(409, denied, retainForReplay: false);
                        res.StatusCode = 409;
                        res.ContentType = "application/json";
                        await res.WriteAsync(denied, req.HttpContext.RequestAborted);
                        return;
                    }
                }
                var acquired = await AcquireThreadAsync(
                    askThreads, threadToken, req.HttpContext.RequestAborted,
                    EvaluationOwnerScope(evaluationToken));
                if (acquired.Lease is not { } acquiredThread)
                {
                    evaluationUse?.Dispose();
                    var json = acquired.Failure!.ToJsonString();
                    claim.Complete(acquired.Status, json, retainForReplay: false);
                    res.StatusCode = acquired.Status;
                    res.ContentType = "application/json";
                    if (req.HttpContext.RequestAborted.IsCancellationRequested)
                        return;
                    await res.WriteAsync(json, req.HttpContext.RequestAborted);
                    return;
                }
                thread = acquiredThread;
            }

            void StreamHeaders()
            {
                res.Headers.ContentType = "text/event-stream";
                res.Headers["X-Accel-Buffering"] = "no";
                res.Headers["X-Lex-Request-Id"] = requestId;
            }
            StreamHeaders();

            var writes = new SemaphoreSlim(1, 1);
            var sequence = 0;
            async Task Send(string ev, JsonNode data)
            {
                await writes.WaitAsync(req.HttpContext.RequestAborted);
                try
                {
                    var envelope = new JsonObject
                    {
                        ["version"] = "1",
                        ["request_id"] = requestId,
                        ["sequence"] = ++sequence,
                        ["server_elapsed_ms"] = streamWatch.Elapsed.TotalMilliseconds,
                        ["payload"] = data,
                    };
                    await res.WriteAsync($"event: {ev}\ndata: {envelope.ToJsonString()}\n\n", req.HttpContext.RequestAborted);
                    await res.Body.FlushAsync(req.HttpContext.RequestAborted);
                }
                catch (OperationCanceledException) when (req.HttpContext.RequestAborted.IsCancellationRequested)
                { /* The reader left; AskAsync observes the same cancellation token. */ }
                catch (IOException)
                { /* The legal result remains authoritative when the SSE reader disconnects. */ }
                catch (ObjectDisposedException)
                { /* The response stream was closed after the request had already started. */ }
                finally { writes.Release(); }
            }

            if (claim.Kind == AskRequestClaimKind.Duplicate)
            {
                try
                {
                    var operationCount = 0;
                    await foreach (var operation in claim.OperationResults.ReadAllAsync(
                                       req.HttpContext.RequestAborted))
                    {
                        if (JsonNode.Parse(operation) is { } parsedOperation)
                        {
                            await Send("operation_result", parsedOperation);
                            operationCount++;
                        }
                    }
                    var replay = await claim.Completion.WaitAsync(req.HttpContext.RequestAborted);
                    var replayBody = JsonNode.Parse(replay.Body);
                    if (operationCount == 0 && replayBody?["operations"] is JsonArray operations)
                        foreach (var operation in operations)
                            if (operation is not null)
                                await Send("operation_result", operation.DeepClone());
                    if (replay.Status == 200)
                        await Send("done", replayBody ?? new JsonObject());
                    else
                        await Send("transport_error", new JsonObject
                        {
                            ["status"] = replay.Status,
                            ["error"] = replayBody?["error"]?.GetValue<string>()
                                ?? "The assistant request did not complete.",
                        });
                }
                finally
                {
                    claim.Unsubscribe();
                }
                return;
            }

            await using var ownedThread = thread
                ?? throw new InvalidOperationException("An owner requires one thread lease.");
            using var ownedEvaluationUse = evaluationUse;

            var steps = 0;
            double? firstOperationEmittedMilliseconds = null;
            var progress = new AskService.AskProgressCallbacks(
                Step: (step, _) =>
                {
                    Interlocked.Increment(ref steps);
                    return new ValueTask(Send("step", new JsonObject
                    {
                        ["kind"] = step.Kind,
                        ["text"] = step.Text,
                        ["work"] = step.Work,
                        ["date"] = step.Date,
                        ["anchor"] = step.Anchor,
                    }));
                },
                OperationResult: async (operation, _) =>
                {
                    firstOperationEmittedMilliseconds ??= streamWatch.Elapsed.TotalMilliseconds;
                    claim.ReportOperation(operation.ToJsonString());
                    await Send("operation_result", operation);
                },
                Synthesis: (status, _) => new ValueTask(Send("synthesis", new JsonObject
                {
                    ["status"] = status,
                })),
                Phase: (update, _) => new ValueTask(Send("phase", new JsonObject
                {
                    ["phase"] = update.Phase switch
                    {
                        AskService.AskPhase.Resolution => "resolution",
                        AskService.AskPhase.Planning => "planning",
                        AskService.AskPhase.Execution => "execution",
                        AskService.AskPhase.Composition => "composition",
                        _ => throw new ArgumentOutOfRangeException(nameof(update)),
                    },
                    ["status"] = update.Status switch
                    {
                        AskService.AskPhaseStatus.Started => "started",
                        AskService.AskPhaseStatus.Completed => "completed",
                        AskService.AskPhaseStatus.Unavailable => "unavailable",
                        _ => throw new ArgumentOutOfRangeException(nameof(update)),
                    },
                })));
            AskService.AskOutcome outcome;
            try
            {
                outcome = await askService.AskAsync(HistoryFor(ownedThread, message), ip,
                    req.Host.Value ?? "law.soufien.lu",
                    req.HttpContext.RequestAborted, progress, requestId,
                    ownedThread.Context,
                    evaluationToken is null
                        ? AskAdmissionLane.Public
                        : AskAdmissionLane.Evaluation);
            }
            catch (OperationCanceledException) when (req.HttpContext.RequestAborted.IsCancellationRequested)
            {
                const string cancelled = "{\"error\":\"The assistant request was cancelled.\"}";
                ownedEvaluationUse?.Commit();
                claim.Complete(499, cancelled, retainForReplay: true);
                return;
            }
            catch
            {
                const string failure = "{\"error\":\"Unexpected error in the playground.\"}";
                ownedEvaluationUse?.Commit();
                claim.Complete(500, failure, retainForReplay: true);
                await Send("transport_error", new JsonObject
                {
                    ["status"] = 500,
                    ["error"] = "Unexpected error in the playground.",
                });
                return;
            }
            var (status, bodyJson) = outcome;

            // Outcome-aware (the labor illusion REVERSES on a weak result): a transparent wait ending
            // in a poor answer scored below delivering that same answer instantly. A refusal therefore
            // keeps its steps out of the transcript.
            bodyJson["narrated"] = status == 200 && bodyJson["ui"]?["gap"] is null && steps > 0;
            if (bodyJson["timing"] is JsonObject timing)
                timing["operation_result_emitted_ms"] = firstOperationEmittedMilliseconds;
            var stored = status == 200 && bodyJson["error"] is null
                && AssistantReply(bodyJson) is { } assistant
                && ownedThread.Commit(
                    message, assistant, outcome.ConversationContext,
                    outcome.ContextDisposition);
            if (stored || threadToken is not null)
                bodyJson["thread_token"] = ownedThread.Token;
            if (outcome.RetainForReplay)
                ownedEvaluationUse?.Commit(stored ? ownedThread.Token : null);
            claim.Complete(status, bodyJson.ToJsonString(), outcome.RetainForReplay);
            if (status == 200)
                await Send("done", bodyJson);
            else
                await Send("transport_error", new JsonObject
                {
                    ["status"] = status,
                    ["error"] = bodyJson["error"]?.GetValue<string>()
                        ?? "The assistant request did not complete.",
                });
        });

        app.MapPost("/api/ask/thread/reset", async (HttpRequest req, HttpResponse res) =>
        {
            PrivateNoStore(res);
            var body = await BoundedRequestBody.ReadAsync(
                req.Body, 1_024, req.HttpContext.RequestAborted);
            if (body is null)
                return Results.Json(new { error = "Request too large." }, statusCode: 413);
            if (!TryThreadToken(req.Headers, out var threadToken) || threadToken is null)
                return Results.Json(
                    new { error = "Invalid X-Lex-Thread-Token." }, statusCode: 400);
            if (!TryIdempotencyKey(req.Headers, out var idempotencyKey))
                return Results.Json(new { error = "Invalid Idempotency-Key." }, statusCode: 400);
            if (!TryEvaluationToken(req.Headers, out var evaluationToken))
                return Results.Json(
                    new { error = "Invalid X-Lex-Evaluation-Admission." }, statusCode: 400);
            var ip = ClientAddress(req);
            var claim = askRequests.Claim(
                ip, idempotencyKey, Fingerprint(
                    body, threadToken, "ask-thread-reset-v1", evaluationToken));
            res.Headers["X-Lex-Request-Id"] = claim.RequestId;
            if (claim.Kind != AskRequestClaimKind.Owner)
            {
                var replay = await claim.Completion.WaitAsync(req.HttpContext.RequestAborted);
                return Results.Content(replay.Body, "application/json", statusCode: replay.Status);
            }
            var removed = askThreads.Reset(
                threadToken, EvaluationOwnerScope(evaluationToken));
            var status = removed ? 200 : 409;
            var response = removed
                ? new JsonObject { ["status"] = "reset" }
                : new JsonObject
                {
                    ["status"] = "thread_unavailable",
                    ["error"] = "This assistant thread is unavailable. Start a new conversation.",
                };
            var json = response.ToJsonString();
            claim.Complete(status, json, retainForReplay: removed);
            return Results.Content(json, "application/json", statusCode: status);
        });

        app.MapGet("/healthz", () => Results.Text("ok"));
        app.MapGet("/readyz", () =>
        {
            var report = ctx.Registry.Readiness(ctx.Options);
            return Results.Json(report, statusCode: report.Ready ? 200 : 503);
        });

        app.MapGet("/provenance/{*key}", (string key) =>
        {
            foreach (var r in readers.Values)
            {
                var d = r.ByKey(key);
                if (d is null) continue;
                var events = r.Events(key);
                var sb = new StringBuilder();
                sb.Append($"""
                    <div class="card"><table class="kv">
                    <tr><td>lex_id</td><td class="mono">{H(d.Key)}</td></tr>
                    <tr><td>work identifier</td><td class="mono">{H(d.GroupIdentifier)}</td></tr>
                    <tr><td>record sha256</td><td class="mono">{H(d.RecordSha)}</td></tr>
                    <tr><td>source</td><td><a href="{H(d.SourceUri)}">{H(d.SourceUri)}</a></td></tr>
                    <tr><td>first observed</td><td class="mono">{H(d.ObservedFrom)}</td></tr>
                    <tr><td>valid (publisher-asserted)</td><td class="mono">{Interval(d)}</td></tr>
                    </table></div>
                    <h2>Event chain</h2><div class="card"><table><tr><th>event</th><th>observed</th><th>detail</th></tr>
                    """);
                foreach (var e in events)
                    sb.Append($"<tr><td>{H(e.Event)}</td><td class=\"mono\">{H(e.ObservedFrom)}</td><td>{H(e.Detail)}</td></tr>");
                sb.Append("</table></div>");
                sb.Append("""
                    <p class="sub">The record hash covers the canonical metadata record (no body text is stored in this mode).
                    Published hashes are forward-verifiable commitments: the day text serving is enabled, body hashes join this chain.</p>
                    """);
                sb.Append(EnvelopeCard(r, false));
                return Results.Content(Page("Provenance", sb.ToString(), H(DocTitle(d))), "text/html");
            }
            return Results.Content(Page("Provenance", "<p>Unknown lex_id.</p>"), "text/html", statusCode: 404);
        });

        return app;
    }

    internal static bool TryIdempotencyKey(IHeaderDictionary headers, out string key)
    {
        if (!headers.TryGetValue("Idempotency-Key", out var values))
        {
            key = Guid.NewGuid().ToString("N");
            return true;
        }
        var supplied = values.Count == 1 ? values[0] : null;
        if (string.IsNullOrEmpty(supplied)
            || supplied.Length > 128
            || supplied.Any(character =>
                !(char.IsAsciiLetterOrDigit(character) || character is '-' or '_' or '.')))
        {
            key = "";
            return false;
        }
        key = supplied;
        return true;
    }

    internal static bool TryThreadToken(
        IHeaderDictionary headers,
        out string? token)
    {
        if (!headers.TryGetValue("X-Lex-Thread-Token", out var values))
        {
            token = null;
            return true;
        }
        token = values.Count == 1 ? values[0] : null;
        return AskThreadRegistry.IsValidToken(token);
    }

    internal static bool TryEvaluationToken(
        IHeaderDictionary headers,
        out string? token)
    {
        if (!headers.TryGetValue("X-Lex-Evaluation-Admission", out var values))
        {
            token = null;
            return true;
        }
        token = values.Count == 1 ? values[0] : null;
        return EvaluationAdmissionRegistry.IsValidToken(token);
    }

    private static string? EvaluationOwnerScope(string? token) => token is null
        ? null
        : Convert.ToHexStringLower(SHA256.HashData(Encoding.ASCII.GetBytes(token)));

    private static bool TryEvaluationSignature(
        IHeaderDictionary headers,
        out string signature)
    {
        var values = headers["X-Lex-Evaluation-Admission-Signature"];
        signature = values.Count == 1 ? values[0] ?? "" : "";
        return signature.Length is > 0
            and <= EvaluationAdmissionContract.MaximumSignatureCharacters;
    }

    private static bool EvaluationInspectionAccepted(
        EvaluationAdmissionAuthorizationKind inspection,
        out int status)
    {
        status = inspection switch
        {
            EvaluationAdmissionAuthorizationKind.RequestNotAllowed => 403,
            EvaluationAdmissionAuthorizationKind.AlreadyUsed => 200,
            EvaluationAdmissionAuthorizationKind.Allowed => 200,
            _ => 401,
        };
        return inspection is EvaluationAdmissionAuthorizationKind.Allowed
            or EvaluationAdmissionAuthorizationKind.AlreadyUsed;
    }

    private static bool TryAskMessage(
        JsonNode? parsed,
        out string message,
        out string error)
    {
        if (parsed is not JsonObject body
            || body.Count != 1
            || body["message"] is not JsonValue value
            || !value.TryGetValue<string>(out message!))
        {
            message = "";
            error = "Body must be {\"message\": \"...\"}.";
            return false;
        }
        if (string.IsNullOrWhiteSpace(message) || message.Length > 1_000)
        {
            error = "Questions must contain 1 to 1,000 characters.";
            return false;
        }
        error = "";
        return true;
    }

    private static (int Status, JsonObject Body) ThreadFailure(AskThreadAcquireKind kind) =>
        kind switch
        {
            AskThreadAcquireKind.NotFound => (409, new JsonObject
            {
                ["status"] = "thread_unavailable",
                ["error"] = "This assistant thread expired or is unavailable. Start a new conversation.",
            }),
            AskThreadAcquireKind.Busy => (429, new JsonObject
            {
                ["status"] = "thread_busy",
                ["error"] = "This assistant thread already has too many waiting turns.",
            }),
            AskThreadAcquireKind.Capacity => (503, new JsonObject
            {
                ["status"] = "thread_capacity",
                ["error"] = "Assistant conversation memory is full. Try again shortly.",
            }),
            _ => throw new ArgumentOutOfRangeException(nameof(kind)),
        };

    private static async ValueTask<(AskThreadLease? Lease, int Status, JsonObject? Failure)>
        AcquireThreadAsync(
            AskThreadRegistry registry,
            string? token,
            CancellationToken cancellationToken,
            string? ownerScope)
    {
        try
        {
            var acquired = await registry.AcquireAsync(
                token, cancellationToken, ownerScope);
            if (acquired.Lease is { } lease) return (lease, 0, null);
            var (status, failure) = ThreadFailure(acquired.Kind);
            return (null, status, failure);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return (null, 499, new JsonObject
            {
                ["status"] = "cancelled",
                ["error"] = "The assistant request was cancelled.",
            });
        }
        catch
        {
            return (null, 500, new JsonObject
            {
                ["status"] = "thread_failure",
                ["error"] = "Assistant conversation memory is temporarily unavailable.",
            });
        }
    }
}
