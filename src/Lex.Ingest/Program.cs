using Lex.Evaluation;
using Lex.Index;
using Lex.Ingest;

var args0 = Environment.GetCommandLineArgs().Skip(1).ToArray();
if (args0.Length == 0) { Usage(); return 1; }

string? Get(string name)
{
    var i = Array.IndexOf(args0, name);
    return i >= 0 && i + 1 < args0.Length ? args0[i + 1] : null;
}

IEnumerable<string> GetAll(string name)
{
    for (var i = 0; i < args0.Length - 1; i++)
        if (args0[i] == name) yield return args0[i + 1];
}

// Time enters as an injected parameter (F9); the wall clock is read only at this CLI boundary.
var now = Get("--now") is { } n ? DateTimeOffset.Parse(n) : DateTimeOffset.UtcNow;
var sourceAdapters = SourceAdapterRegistry.CreateDefault();

switch (args0[0])
{
    case "embedding-smoke":
    {
        var modelDir = Get("--model-dir") ?? throw new ArgumentException("--model-dir required");
        var text = Get("--text") ?? "protection des donnees personnelles";
        var batchSize = int.TryParse(Get("--batch-size"), out var parsedBatchSize) ? parsedBatchSize : 1;
        if (batchSize <= 0) throw new ArgumentOutOfRangeException("--batch-size");
        var intraOpThreads = int.TryParse(Get("--intra-op-threads"), out var parsedIntraOpThreads)
            ? parsedIntraOpThreads : (int?)null;
        var directMlDeviceId = int.TryParse(Get("--directml-device"), out var parsedDirectMlDeviceId)
            ? parsedDirectMlDeviceId : (int?)null;
        using var encoder = MultilingualE5Encoder.Open(modelDir, intraOpThreads, directMlDeviceId);
        var sw = System.Diagnostics.Stopwatch.StartNew();
        var padToTokens = int.TryParse(Get("--pad-to-tokens"), out var parsedPadding)
            ? parsedPadding : (int?)null;
        var vectors = encoder.EncodeBatch(
            Enumerable.Repeat(text, batchSize).ToArray(), EmbeddingInputKind.Query, padToTokens);
        sw.Stop();
        var vector = vectors[0];
        var quantized = vectors.Select(SemanticVectorReader.Int8).ToArray();
        var quantizedBytes = quantized[0].Select(value => unchecked((byte)value)).ToArray();
        Console.WriteLine(System.Text.Json.JsonSerializer.Serialize(new
        {
            encoder.ModelId,
            encoder.ModelRevision,
            encoder.Dimensions,
            Tokens = encoder.CountTokens("query: " + text),
            Norm = Math.Sqrt(vector.Sum(v => v * v)),
            BatchSize = batchSize,
            BatchInt8Parity = quantized.All(candidate => candidate.SequenceEqual(quantized[0])),
            VectorInt8Sha256 = Convert.ToHexStringLower(
                System.Security.Cryptography.SHA256.HashData(quantizedBytes)),
            ElapsedMs = sw.Elapsed.TotalMilliseconds,
            PerItemElapsedMs = sw.Elapsed.TotalMilliseconds / batchSize,
        }));
        return 0;
    }
    case "benchmark-cases":
    {
        var output = Get("--out") ?? throw new ArgumentException("--out required");
        var cases = RetrievalBenchmarkCatalog.Load(
            Get("--cases") ?? Path.Combine(AppContext.BaseDirectory, "retrieval-cases.json"));
        var bytes = System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(
            cases, new System.Text.Json.JsonSerializerOptions
            {
                PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.SnakeCaseLower,
                WriteIndented = true,
            });
        File.WriteAllBytes(output, bytes);
        Console.Error.WriteLine($"[lex] wrote {cases.Count} public retrieval cases to {output}");
        return 0;
    }
    case "benchmark":
    {
        var index = Get("--index") ?? throw new ArgumentException("--index required");
        var modelDir = Get("--model-dir") ?? throw new ArgumentException("--model-dir required");
        var vectors = Get("--vectors") ?? Path.ChangeExtension(index, ".vectors");
        var output = Get("--out") ?? throw new ArgumentException("--out required");
        var caseSet = RetrievalBenchmarkCatalog.LoadSet(
            Get("--cases") ?? Path.Combine(AppContext.BaseDirectory, "retrieval-cases.json"));
        var baseline = RetrievalBenchmarkCatalog.LoadBaseline(
            Path.Combine(AppContext.BaseDirectory, "retrieval-baseline-v2.json"));
        var load = System.Diagnostics.Stopwatch.StartNew();
        using var encoder = MultilingualE5Encoder.Open(modelDir);
        using var reader = LexIndexReader.Open(index, encoder, vectors);
        load.Stop();
        var cold = System.Diagnostics.Stopwatch.StartNew();
        _ = reader.SearchHybrid("protection des donnees personnelles", FilterSet.All, 10);
        cold.Stop();
        var memoryLimit = long.TryParse(Get("--memory-limit-bytes"), out var parsedMemory) ? parsedMemory : 0;
        var report = RetrievalBenchmarkRunner.Run(reader, caseSet, baseline, index, vectors,
            Get("--code-commit") ?? "uncommitted", Get("--manifest-id") ?? "unverified",
            Get("--machine") ?? Environment.MachineName,
            Get("--resource") ?? "not supplied", memoryLimit,
            load.Elapsed.TotalMilliseconds, cold.Elapsed.TotalMilliseconds, now,
            progress => Console.Error.WriteLine(
                $"[benchmark-progress] stage={progress.Stage} items={progress.Completed}/{progress.Total} "
                + $"percent={(progress.Total == 0 ? 100 : progress.Completed * 100d / progress.Total):0.0} "
                + $"elapsed={progress.Elapsed:hh\\:mm\\:ss} "
                + $"eta={(progress.EstimatedRemaining is null ? "calculating" : progress.EstimatedRemaining.Value.ToString(@"hh\:mm\:ss"))}"));
        File.WriteAllBytes(output, System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(report,
            new System.Text.Json.JsonSerializerOptions
            {
                PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.SnakeCaseLower,
                WriteIndented = true,
            }));
        Console.WriteLine(System.Text.Json.JsonSerializer.Serialize(report));
        return report.ActivationGatePassed ? 0 : 5;
    }
    case "assistant-eval":
    {
        var diagnostic = args0.Length > 1 && args0[1] == "diagnostic";
        if (args0.Length > 1 && EvalAdmissionCli.IsCommand(args0[1]))
            return EvalAdmissionCli.Run(args0[1..], now);
        if (args0.Length > 1 && args0[1] == "verify-cases")
        {
            var verifiedCases = AssistantEvaluationCatalog.Load(
                Get("--cases") ?? throw new ArgumentException("--cases required"),
                Get("--review-attestation")
                    ?? throw new ArgumentException("--review-attestation required"),
                Get("--review-signature")
                    ?? throw new ArgumentException("--review-signature required"));
            verifiedCases.EnsureReleaseReady();
            Console.WriteLine(System.Text.Json.JsonSerializer.Serialize(new
            {
                status = "approved",
                cases_sha256 = verifiedCases.Sha256,
                frozen_at = verifiedCases.Catalog.FrozenAt,
                reviewer_id = verifiedCases.Review!.ReviewerId,
                cases = verifiedCases.Catalog.Cases.Count,
            }));
            return 0;
        }
        if (args0.Length > 1 && args0[1] == "verify-bootstrap-equivalence")
        {
            var root = Path.GetFullPath(
                Get("--root") ?? throw new ArgumentException("--root required"));
            var containerAppResourceId = Get("--candidate-container-app-resource-id")
                ?? throw new ArgumentException(
                    "--candidate-container-app-resource-id required");
            var bootstrapCandidateRevision = Get("--candidate-revision")
                ?? throw new ArgumentException("--candidate-revision required");
            var bootstrapRollbackRevision = Get("--rollback-revision")
                ?? throw new ArgumentException("--rollback-revision required");
            var bootstrapLegacyAuthorityRevision = Get("--legacy-authority-revision")
                ?? throw new ArgumentException("--legacy-authority-revision required");
            var bootstrapCasesSha256 = Get("--cases-sha256")
                ?? throw new ArgumentException("--cases-sha256 required");
            var manifestPath = Get("--manifest")
                ?? throw new ArgumentException("--manifest required");
            var signaturePath = Get("--signature")
                ?? throw new ArgumentException("--signature required");
            var equivalencePath = Get("--equivalence")
                ?? throw new ArgumentException("--equivalence required");
            var evaluationRelease = Get("--evaluation-release")
                ?? throw new ArgumentException("--evaluation-release required");
            var canonicalTemplateDigest = Get("--canonical-template-digest")
                ?? throw new ArgumentException("--canonical-template-digest required");
            var imageDigest = Get("--image-digest")
                ?? throw new ArgumentException("--image-digest required");
            var trustRootsPath = Get("--trust-roots")
                ?? throw new ArgumentException("--trust-roots required");
            var artifactRoots = ArtifactManifests.ParseTrustRoots(File.ReadAllBytes(
                trustRootsPath));
            var establishedReleaseState = Array.IndexOf(
                args0, "--established-release-state") >= 0;
            var historicalSourcePackage = Array.IndexOf(
                args0, "--historical-source-package") >= 0;
            if (establishedReleaseState && historicalSourcePackage)
                throw new ArgumentException(
                    "bootstrap equivalence live and historical modes are mutually exclusive");
            if (historicalSourcePackage)
            {
                var expectedCodeCommit = Get("--expected-code-commit")
                    ?? throw new ArgumentException("--expected-code-commit required");
                var historicalEvidence = BootstrapEquivalenceVerifier.VerifyHistoricalPackage(
                    root, manifestPath, signaturePath, equivalencePath, artifactRoots,
                    containerAppResourceId, bootstrapLegacyAuthorityRevision,
                    bootstrapCandidateRevision, bootstrapRollbackRevision, evaluationRelease,
                    canonicalTemplateDigest, imageDigest, bootstrapCasesSha256,
                    expectedCodeCommit, now);
                Console.WriteLine(System.Text.Json.JsonSerializer.Serialize(new
                {
                    status = "passed",
                    schema = historicalEvidence.Schema,
                    candidate_revision = historicalEvidence.Candidate.RevisionName,
                    rollback_revision = historicalEvidence.Rollback.RevisionName,
                    legacy_authority_revision = historicalEvidence.LegacyAuthority.RevisionName,
                    evaluation_release = historicalEvidence.EvaluationRelease,
                    verification_mode = "historical_source_package",
                }));
                return 0;
            }
            BootstrapEquivalenceVerifier.ValidateInvocation(
                root, manifestPath, signaturePath, equivalencePath, artifactRoots,
                containerAppResourceId, bootstrapLegacyAuthorityRevision,
                bootstrapCandidateRevision, bootstrapRollbackRevision, evaluationRelease,
                canonicalTemplateDigest, imageDigest, bootstrapCasesSha256, now,
                establishedReleaseState);
            using var verificationHttp = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
            using var verificationDeadline = new CancellationTokenSource(TimeSpan.FromMinutes(2));
            var bootstrapResolver = new AzureModelDeploymentResolver(verificationHttp);
            var candidateTask = bootstrapResolver.ResolveContainerAppBootstrapRevisionAsync(
                containerAppResourceId, bootstrapCandidateRevision, verificationDeadline.Token);
            var rollbackTask = bootstrapResolver.ResolveContainerAppBootstrapRevisionAsync(
                containerAppResourceId, bootstrapRollbackRevision, verificationDeadline.Token);
            var routesTask = bootstrapResolver.ResolveContainerAppBootstrapRoutesAsync(
                containerAppResourceId, verificationDeadline.Token);
            Task<BootstrapRevisionLiveEvidence>? legacyTask = establishedReleaseState
                ? null
                : bootstrapResolver.ResolveContainerAppBootstrapRevisionAsync(
                    containerAppResourceId, bootstrapLegacyAuthorityRevision,
                    verificationDeadline.Token);
            var lookups = new List<Task> { candidateTask, rollbackTask, routesTask };
            if (legacyTask is not null) lookups.Add(legacyTask);
            await Task.WhenAll(lookups);
            var evidence = establishedReleaseState
                ? BootstrapEquivalenceVerifier.VerifyEstablishedFallback(
                    root, manifestPath, signaturePath, equivalencePath, artifactRoots,
                    containerAppResourceId, bootstrapLegacyAuthorityRevision,
                    bootstrapCandidateRevision, bootstrapRollbackRevision, evaluationRelease,
                    canonicalTemplateDigest, imageDigest, bootstrapCasesSha256,
                    await candidateTask, await rollbackTask, await routesTask, now)
                : BootstrapEquivalenceVerifier.Verify(
                    root, manifestPath, signaturePath, equivalencePath, artifactRoots,
                    containerAppResourceId, bootstrapLegacyAuthorityRevision,
                    bootstrapCandidateRevision, bootstrapRollbackRevision, evaluationRelease,
                    canonicalTemplateDigest, imageDigest, bootstrapCasesSha256,
                    await candidateTask, await rollbackTask, await legacyTask!,
                    await routesTask, now);
            Console.WriteLine(System.Text.Json.JsonSerializer.Serialize(new
            {
                status = "passed",
                schema = evidence.Schema,
                candidate_revision = evidence.Candidate.RevisionName,
                rollback_revision = evidence.Rollback.RevisionName,
                legacy_authority_revision = evidence.LegacyAuthority.RevisionName,
                evaluation_release = evidence.EvaluationRelease,
            }));
            return 0;
        }
        if (args0.Length > 1 && args0[1] == "verify-report")
        {
            var verifiedCaseSet = AssistantEvaluationCatalog.Load(
                Get("--cases") ?? throw new ArgumentException("--cases required"),
                Get("--review-attestation")
                    ?? throw new ArgumentException("--review-attestation required"),
                Get("--review-signature")
                    ?? throw new ArgumentException("--review-signature required"));
            using var verificationHttp = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
            var verificationResolver = new AzureModelDeploymentResolver(verificationHttp);
            var verifiedTarget = await verificationResolver.ResolveContainerAppRevisionAsync(
                Get("--candidate-container-app-resource-id")
                    ?? throw new ArgumentException(
                        "--candidate-container-app-resource-id required"),
                Get("--candidate-revision")
                    ?? throw new ArgumentException("--candidate-revision required"),
                CancellationToken.None);
            using var attestationHttp = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
            var verificationTarget = new AssistantEvaluationHttpTarget(
                attestationHttp,
                Get("--base-url") ?? throw new ArgumentException("--base-url required"));
            var verifiedAttestation = await verificationTarget.ReadAttestationAsync(
                verifiedTarget, CancellationToken.None);
            var verifiedReport = AssistantEvaluationReleaseVerifier.VerifyReport(
                Get("--report") ?? throw new ArgumentException("--report required"),
                Get("--admission") ?? throw new ArgumentException("--admission required"),
                Get("--admission-signature")
                    ?? throw new ArgumentException("--admission-signature required"),
                verifiedCaseSet, verifiedTarget, verifiedAttestation, now);
            await AssistantEvaluationReleaseVerifier.VerifyModelDeploymentsAsync(
                verificationResolver, verifiedReport, CancellationToken.None);
            Console.WriteLine(System.Text.Json.JsonSerializer.Serialize(new
            {
                status = "passed",
                revision = verifiedTarget.RevisionName,
                report_run_at = verifiedReport.RunAt,
                cases = verifiedReport.Results.Count,
            }));
            return 0;
        }
        if (args0.Length > 1 && args0[1] == "verify-release")
        {
            var root = Path.GetFullPath(
                Get("--root") ?? throw new ArgumentException("--root required"));
            var manifestPath = Get("--manifest")
                ?? throw new ArgumentException("--manifest required");
            var signaturePath = Get("--signature")
                ?? throw new ArgumentException("--signature required");
            var trustRootPath = Get("--trust-roots")
                ?? throw new ArgumentException("--trust-roots required");
            var artifactRoots = ArtifactManifests.ParseTrustRoots(
                File.ReadAllBytes(trustRootPath));
            var verifiedFiles = ArtifactManifests.VerifyDirectory(
                root, manifestPath, signaturePath, artifactRoots);
            if (verifiedFiles.Count != AssistantEvaluationReleaseVerifier.RequiredFiles.Count
                || AssistantEvaluationReleaseVerifier.RequiredFiles.Any(
                    file => !verifiedFiles.Contains(file)))
                throw new InvalidDataException(
                    "Signed assistant evaluation artifact set is incomplete or contains unexpected files.");
            string EvidencePath(string relative) => Path.Combine(
                root, relative.Replace('/', Path.DirectorySeparatorChar));
            var verifiedCaseSet = AssistantEvaluationCatalog.Load(
                EvidencePath(AssistantEvaluationReleaseVerifier.CasesFile),
                EvidencePath(AssistantEvaluationReleaseVerifier.ReviewFile),
                EvidencePath(AssistantEvaluationReleaseVerifier.ReviewSignatureFile));
            using var verificationHttp = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
            var verificationResolver = new AzureModelDeploymentResolver(verificationHttp);
            var verifiedTarget = await verificationResolver.ResolveContainerAppRevisionAsync(
                Get("--candidate-container-app-resource-id")
                    ?? throw new ArgumentException(
                        "--candidate-container-app-resource-id required"),
                Get("--candidate-revision")
                    ?? throw new ArgumentException("--candidate-revision required"),
                CancellationToken.None);
            using var attestationHttp = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
            var verificationTarget = new AssistantEvaluationHttpTarget(
                attestationHttp,
                Get("--base-url") ?? throw new ArgumentException("--base-url required"));
            var verifiedAttestation = await verificationTarget.ReadAttestationAsync(
                verifiedTarget, CancellationToken.None);
            var verifiedReport = AssistantEvaluationReleaseVerifier.VerifyReport(
                EvidencePath(AssistantEvaluationReleaseVerifier.ReportFile),
                EvidencePath(AssistantEvaluationReleaseVerifier.AdmissionFile),
                EvidencePath(AssistantEvaluationReleaseVerifier.AdmissionSignatureFile),
                verifiedCaseSet, verifiedTarget, verifiedAttestation, now,
                allowOlderPreviouslyPromotedEvidence:
                    Array.IndexOf(args0, "--allow-older-previously-promoted-evidence") >= 0);
            var verifiedBrowserEvidence =
                AssistantEvaluationReleaseVerifier.VerifyBrowserEvidence(
                    EvidencePath(AssistantEvaluationReleaseVerifier.BrowserEvidenceFile),
                    verifiedTarget, verifiedReport, now);
            await AssistantEvaluationReleaseVerifier.VerifyModelDeploymentsAsync(
                verificationResolver, verifiedReport, CancellationToken.None);
            var verifiedManifest = AssistantEvaluationReleaseVerifier.VerifyArtifactSet(
                root, manifestPath, signaturePath, artifactRoots,
                verifiedTarget, verifiedReport, verifiedBrowserEvidence);
            Console.WriteLine(System.Text.Json.JsonSerializer.Serialize(new
            {
                status = "passed",
                revision = verifiedTarget.RevisionName,
                image = verifiedTarget.Image,
                report_run_at = verifiedReport.RunAt,
                manifest_key_id = verifiedManifest.KeyId,
                cases = verifiedReport.Results.Count,
            }));
            return 0;
        }
        var admissionPath = Path.GetFullPath(
            Get("--admission") ?? throw new ArgumentException("--admission required"));
        var admissionSignaturePath = Path.GetFullPath(
            Get("--admission-signature")
                ?? throw new ArgumentException("--admission-signature required"));
        var admissionBytes = EvalAdmissionCli.ReadBounded(
            admissionPath, EvaluationAdmissionContract.MaximumBytes);
        var admissionSignature = EvalAdmissionCli.ReadBoundedSignature(
            admissionSignaturePath);
        var output = Get("--out") ?? throw new ArgumentException("--out required");
        var caseSet = AssistantEvaluationCatalog.Load(
            Get("--cases") ?? Path.Combine(AppContext.BaseDirectory, "assistant-cases-v3.json"),
            Get("--review-attestation")
                ?? throw new ArgumentException("--review-attestation required"),
            Get("--review-signature")
                ?? throw new ArgumentException("--review-signature required"));
        using var managementHttp = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
        var resolver = new AzureModelDeploymentResolver(managementHttp);
        var candidateContainerAppResourceId = Get("--candidate-container-app-resource-id")
            ?? throw new ArgumentException("--candidate-container-app-resource-id required");
        var candidateRevision = Get("--candidate-revision")
            ?? throw new ArgumentException("--candidate-revision required");
        var targetEvidence = await resolver.ResolveContainerAppRevisionAsync(
            candidateContainerAppResourceId,
            candidateRevision,
            CancellationToken.None);
        var candidateModel = await resolver.ResolveAsync(
            Get("--candidate-model-resource-id")
                ?? throw new ArgumentException("--candidate-model-resource-id required"),
            Get("--candidate-deployment")
                ?? throw new ArgumentException("--candidate-deployment required"),
            CancellationToken.None);
        var graderModel = await resolver.ResolveAsync(
            Get("--grader-model-resource-id")
                ?? throw new ArgumentException("--grader-model-resource-id required"),
            Get("--grader-deployment")
                ?? throw new ArgumentException("--grader-deployment required"),
            CancellationToken.None);
        using var targetHttp = new HttpClient { Timeout = TimeSpan.FromSeconds(90) };
        var target = new AssistantEvaluationHttpTarget(targetHttp,
            Get("--base-url") ?? throw new ArgumentException("--base-url required"),
            admissionBytes, admissionSignature);
        var targetAttestation = await target.ReadAttestationAsync(
            targetEvidence, CancellationToken.None);
        var identity = new AssistantEvaluationIdentity(
            targetEvidence, targetAttestation.IndexManifestIds,
            candidateModel, graderModel);
        AssistantEvaluationHttpGrader? grader = null;
        HttpClient? graderHttp = null;
        try
        {
            if (caseSet.Catalog.Cases.Any(item => item.Grading.Mode == "llm"))
            {
                var keyEnvironment = Get("--grader-key-env") ?? "AOAI_GRADER_KEY";
                var graderKey = Environment.GetEnvironmentVariable(keyEnvironment)
                    ?? throw new InvalidDataException(
                        $"Required grader credential environment '{keyEnvironment}' is absent.");
                graderHttp = new HttpClient { Timeout = TimeSpan.FromSeconds(90) };
                grader = new AssistantEvaluationHttpGrader(
                    graderHttp,
                    graderModel.Endpoint,
                    graderKey,
                    graderModel.Deployment);
            }
            if (diagnostic)
            {
                var diagnosticReport = await AssistantEvaluationDiagnosticRunner.RunAsync(
                    caseSet, target, grader, identity,
                    caseSet.Catalog.Pricing,
                    now, CancellationToken.None);
                var postDiagnosticTarget = await resolver.ResolveContainerAppRevisionAsync(
                    candidateContainerAppResourceId, candidateRevision,
                    CancellationToken.None);
                AssistantEvaluationRunner.EnsureStableTarget(
                    targetEvidence, postDiagnosticTarget);
                var diagnosticBytes =
                    System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(
                        diagnosticReport,
                        new System.Text.Json.JsonSerializerOptions
                        {
                            PropertyNamingPolicy =
                                System.Text.Json.JsonNamingPolicy.SnakeCaseLower,
                            WriteIndented = true,
                        });
                var absoluteDiagnosticOutput = Path.GetFullPath(output);
                Directory.CreateDirectory(
                    Path.GetDirectoryName(absoluteDiagnosticOutput)!);
                var diagnosticTemporary = absoluteDiagnosticOutput
                    + $".{Guid.NewGuid():N}.tmp";
                try
                {
                    File.WriteAllBytes(diagnosticTemporary, diagnosticBytes);
                    File.Move(
                        diagnosticTemporary, absoluteDiagnosticOutput, overwrite: true);
                }
                finally
                {
                    try { File.Delete(diagnosticTemporary); } catch { }
                }
                Console.WriteLine(
                    System.Text.Json.JsonSerializer.Serialize(diagnosticReport));
                return diagnosticReport.MeasurementCompleted ? 0 : 5;
            }
            var report = await AssistantEvaluationRunner.RunAsync(
                caseSet, target, grader, identity,
                caseSet.Catalog.Pricing,
                now, CancellationToken.None);
            var postRunTarget = await resolver.ResolveContainerAppRevisionAsync(
                candidateContainerAppResourceId, candidateRevision, CancellationToken.None);
            AssistantEvaluationRunner.EnsureStableTarget(targetEvidence, postRunTarget);
            var bytes = System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(report,
                new System.Text.Json.JsonSerializerOptions
                {
                    PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.SnakeCaseLower,
                    WriteIndented = true,
                });
            var absoluteOutput = Path.GetFullPath(output);
            Directory.CreateDirectory(Path.GetDirectoryName(absoluteOutput)!);
            var temporary = absoluteOutput + $".{Guid.NewGuid():N}.tmp";
            try
            {
                File.WriteAllBytes(temporary, bytes);
                File.Move(temporary, absoluteOutput, overwrite: true);
            }
            finally
            {
                try { File.Delete(temporary); } catch { }
            }
            Console.WriteLine(System.Text.Json.JsonSerializer.Serialize(report));
            return report.ActivationGatePassed ? 0 : 5;
        }
        finally
        {
            graderHttp?.Dispose();
        }
    }
    case "scope-preview":
    {
        var publisher = Get("--publisher") ?? "eu-eurlex";
        var previous = Get("--previous-scope");
        var preview = await sourceAdapters.PreviewScopeAsync(
            publisher, Get, previous, now, CancellationToken.None);
        Console.WriteLine(System.Text.Json.JsonSerializer.Serialize(preview,
            new System.Text.Json.JsonSerializerOptions
            {
                PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.SnakeCaseLower,
                WriteIndented = true,
            }));
        return 0;
    }
    case "ingest":
    {
        var publisher = Get("--publisher") ?? "lu-legilux";
        var corpus = Get("--corpus") ?? throw new ArgumentException("--corpus required");
        var ingesterCodeCommit = Lex.Temporal.CodeIdentity.RequireFullCommit(
            Get("--code-commit"), "--code-commit");
        var adapter = sourceAdapters.Resolve(publisher, Get);
        if (Array.IndexOf(args0, "--fresh") >= 0)
        {
            Console.Error.WriteLine(
                $"[lex] fresh ingest {publisher} -> disposable candidate {corpus}");
            await FreshCorpusMigration.RunAsync(corpus, publisher, adapter, now,
                ingesterCodeCommit, CancellationToken.None);
            return 0;
        }
        else
        {
            Console.Error.WriteLine($"[lex] ingest {publisher} -> {corpus}");
            var writer = new CorpusWriter(corpus, now, ingesterCodeCommit);
            await writer.WriteAsync(adapter, CancellationToken.None, requireComplete: true);
            return writer.Accepted ? 0 : 4;
        }
    }
    case "index":
    {
        var corpus = Get("--corpus") ?? throw new ArgumentException("--corpus required");
        var articles = Get("--articles");
        var outDb = Get("--out") ?? throw new ArgumentException("--out required");
        var keyFile = Get("--keyfile");
        var embeddingModelDir = Get("--embedding-model");
        string? keyPem = null;
        if (keyFile is not null)
        {
            if (!File.Exists(keyFile))
            {
                Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(keyFile))!);
                File.WriteAllText(keyFile, StampSigner.CreateKeyPem());
                Console.Error.WriteLine($"[lex] generated signing key at {keyFile}");
            }
            keyPem = File.ReadAllText(keyFile);
        }
        Console.Error.WriteLine($"[lex] index {corpus} (articles: {articles ?? "none"}) -> {outDb}");
        var embeddingIntraOpThreads = int.TryParse(Get("--embedding-intra-op-threads"), out var parsedEmbeddingThreads)
            ? parsedEmbeddingThreads : (int?)null;
        var embeddingDirectMlDeviceId = int.TryParse(Get("--embedding-directml-device"), out var parsedEmbeddingDevice)
            ? parsedEmbeddingDevice : (int?)null;
        using var encoder = embeddingModelDir is null ? null
            : MultilingualE5Encoder.Open(embeddingModelDir, embeddingIntraOpThreads, embeddingDirectMlDeviceId);
        var embeddingBatchSize = int.TryParse(Get("--embedding-batch-size"), out var parsedEmbeddingBatchSize)
            ? parsedEmbeddingBatchSize : 16;
        var embeddingMaxBatchTokens = int.TryParse(
            Get("--embedding-max-batch-tokens"), out var parsedEmbeddingMaxBatchTokens)
            ? parsedEmbeddingMaxBatchTokens : 32_768;
        var indexBudget = int.TryParse(Get("--time-budget-minutes"), out var parsedBudgetMinutes)
            ? TimeSpan.FromMinutes(parsedBudgetMinutes) : (TimeSpan?)null;
        if (indexBudget <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException("--time-budget-minutes", "Time budget must be positive.");
        var indexWatch = System.Diagnostics.Stopwatch.StartNew();
        var semantic = encoder is null ? null : new SemanticBuildOptions(
            encoder, Get("--vectors") ?? Path.ChangeExtension(outDb, ".vectors"),
            encoder.ModelSha256, encoder.TokenizerSha256,
            Progress: progress =>
            {
                var budgetRemaining = indexBudget is { } budget
                    ? budget - indexWatch.Elapsed : (TimeSpan?)null;
                var deadlineRisk = progress.EstimatedRemaining is { } phaseRemaining
                    && budgetRemaining is { } remaining && phaseRemaining > remaining;
                var etaConfidence = progress.EstimatedRemaining is null ? "calculating"
                    : progress.Elapsed < TimeSpan.FromMinutes(2)
                      || progress.Completed < Math.Min(100, progress.Total)
                        ? "warming" : "sampled";
                var stopRecommended = deadlineRisk && etaConfidence == "sampled";
                Console.Error.WriteLine(
                    $"  [index-progress] stage={progress.Stage.ToString().ToLowerInvariant()} " +
                    $"items={progress.Completed}/{progress.Total} " +
                    $"percent={progress.Percent:F1} phase_elapsed={FormatDuration(progress.Elapsed)} " +
                    $"phase_eta={(progress.EstimatedRemaining is { } eta ? FormatDuration(eta) : "calculating")} " +
                    $"eta_confidence={etaConfidence} " +
                    $"total_elapsed={FormatDuration(indexWatch.Elapsed)}" +
                    $"{(budgetRemaining is { } left ? $" budget_remaining={FormatDuration(left)}" : "")}" +
                    $"{(progress.EstimatedRemaining is { } finishIn ? $" phase_finish_utc={DateTimeOffset.UtcNow.Add(finishIn):yyyy-MM-ddTHH:mm:ssZ}" : "")}" +
                    $"{(progress.CurrentItem is { } item ? $" current={item} chars={progress.CurrentItemCharacters} item_elapsed={FormatDuration(progress.CurrentItemElapsed ?? TimeSpan.Zero)}" : "")}" +
                    $"{(progress.IsHeartbeat ? " heartbeat=true" : "")}" +
                    $" deadline_risk={deadlineRisk.ToString().ToLowerInvariant()}" +
                    $" stop_recommended={stopRecommended.ToString().ToLowerInvariant()}");
            },
            BatchSize: embeddingBatchSize,
            MaxBatchTokens: embeddingMaxBatchTokens,
            ExecutionProvider: encoder.ExecutionProvider,
            EmbeddingCachePath: Get("--embedding-cache"));
        var codeCommit = Get("--code-commit")
            ?? throw new ArgumentException("--code-commit required");
        var articlesCommitInput = Get("--articles-commit");
        if (articles is null && articlesCommitInput is not null)
            throw new ArgumentException("--articles-commit requires --articles");
        var articlesCommit = articles is null ? null : articlesCommitInput
            ?? throw new ArgumentException("--articles-commit required when --articles is supplied");
        var corpusCommit = Get("--corpus-commit")
            ?? throw new ArgumentException("--corpus-commit required");
        IndexFromCorpus.Build(corpus, articles, outDb, keyPem, now, semantic,
            codeCommit, articlesCommit, corpusCommit);
        return 0;
    }
    case "derive":
    {
        var publisher = Get("--publisher") ?? "lu-legilux";
        var corpus = Get("--corpus") ?? throw new ArgumentException("--corpus required");
        var outRoot = Get("--out") ?? throw new ArgumentException("--out required");
        var deriverCodeCommit = Lex.Temporal.CodeIdentity.RequireFullCommit(
            Get("--code-commit"), "--code-commit");
        var corpusCommit = Lex.Temporal.CodeIdentity.RequireFullCommit(
            Get("--corpus-commit"), "--corpus-commit");
        var deriverTreeId = Lex.Temporal.CodeIdentity.RequireFullGitObjectId(
            Get("--deriver-tree-id"), "--deriver-tree-id");
        Console.Error.WriteLine($"[lex] derive {publisher} {corpus} -> {outRoot}");
        var stats = Lex.Derive.DeriveWriter.Derive(
            corpus, outRoot, publisher, deriverCodeCommit, deriverTreeId,
            corpusCommit);
        Console.Error.WriteLine($"  [derive] works={stats.Works} versions={stats.Versions} provisions={stats.Provisions} empty_provisions={stats.EmptyProvisions} mostly_empty_versions={stats.MostlyEmpty?.Count ?? 0} skipped={stats.Skipped} errors={stats.Errors.Count}");
        // Listed rather than summarised: each line names one document whose profile failed on it,
        // which is the unit someone can go and fix. A corpus percentage names nothing.
        foreach (var version in stats.MostlyEmpty?.Take(20) ?? [])
            Console.Error.WriteLine($"  [derive] MOSTLY EMPTY {version}");
        foreach (var e in stats.Errors.Take(20)) Console.Error.WriteLine($"  [derive] ERROR {e}");
        return stats.Errors.Count == 0 ? 0 : 2;
    }
    case "verify":
    {
        // verify corpus --corpus X | verify stamp --db X
        //   [--expected-collection ID] [--expected-corpus-commit SHA]
        //   [--expected-code-commit SHA] [--corpus-manifest FILE]
        //   [--articles-generation FILE]
        // | verify derive --publisher P --corpus X --articles Y
        switch (args0.Length > 1 ? args0[1] : "")
        {
            case "corpus":
            {
                var corpus = Get("--corpus") ?? throw new ArgumentException("--corpus required");
                var report = CorpusIntegrity.Verify(corpus);
                Console.WriteLine(System.Text.Json.JsonSerializer.Serialize(report, new System.Text.Json.JsonSerializerOptions
                {
                    PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.SnakeCaseLower,
                    WriteIndented = true,
                }));
                return report.IsValid ? 0 : 6;
            }
            case "stamp":
            {
                var db = Get("--db") ?? throw new ArgumentException("--db required");
                var expectedCollection = Get("--expected-collection");
                var expectedCorpusCommit = Get("--expected-corpus-commit");
                var expectedCodeCommit = Get("--expected-code-commit");
                var expectedArticlesCommit = Get("--expected-articles-commit");
                var corpusManifest = Get("--corpus-manifest");
                var articlesGeneration = Get("--articles-generation");
                var expectedCorpusManifestSha256 = Get(
                    "--expected-corpus-manifest-sha256");
                var expectedIngesterCodeCommit = Get(
                    "--expected-ingester-code-commit");
                var expectedDeriverCodeCommit = Get(
                    "--expected-deriver-code-commit");
                var expectedDeriverTreeId = Get("--expected-deriver-tree-id");
                var expectedGenerationSha256 = Get("--expected-generation-sha256");
                var expectedProfilesSha256 = Get("--expected-profiles-sha256");
                var hasProvenanceEvidence = corpusManifest is not null
                    || articlesGeneration is not null
                    || expectedCorpusManifestSha256 is not null
                    || expectedIngesterCodeCommit is not null
                    || expectedDeriverCodeCommit is not null
                    || expectedDeriverTreeId is not null
                    || expectedGenerationSha256 is not null
                    || expectedProfilesSha256 is not null;
                if (expectedCollection is not null || expectedCorpusCommit is not null
                    || expectedCodeCommit is not null || expectedArticlesCommit is not null
                    || hasProvenanceEvidence)
                {
                    var strict = IndexStampVerifier.Verify(db,
                        new IndexStampVerificationInputs(
                            ExpectedCollection: expectedCollection,
                            ExpectedCorpusCommit: expectedCorpusCommit,
                            ExpectedCodeCommit: expectedCodeCommit,
                            ExpectedArticlesCommit: expectedArticlesCommit,
                            CorpusManifestPath: corpusManifest,
                            ArticlesGenerationPath: articlesGeneration,
                            ExpectedCorpusManifestSha256: expectedCorpusManifestSha256,
                            ExpectedIngesterCodeCommit: expectedIngesterCodeCommit,
                            ExpectedDeriverCodeCommit: expectedDeriverCodeCommit,
                            ExpectedDeriverTreeId: expectedDeriverTreeId,
                            ExpectedGenerationSha256: expectedGenerationSha256,
                            ExpectedProfilesSha256: expectedProfilesSha256,
                            RequireDerivedProvenance: expectedArticlesCommit is not null
                                || articlesGeneration is not null));
                    Console.WriteLine($"collection={strict.Collection} " +
                        $"corpus_commit={strict.CorpusCommit} " +
                        $"signature_valid={strict.SignatureValid} " +
                        $"content_digest={(strict.ContentDigestMatches ? "matches"
                            : strict.ContentDigestPresent ? "MISMATCH" : "absent")} " +
                        $"collection_matches={strict.CollectionMatches} " +
                        $"corpus_commit_matches={strict.CorpusCommitMatches} " +
                        $"code_commit={strict.CodeCommit ?? "absent"} " +
                        $"code_commit_matches={strict.CodeCommitMatches} " +
                        $"articles_commit={strict.ArticlesCommit ?? "absent"} " +
                        $"articles_commit_matches={strict.ArticlesCommitMatches} " +
                        $"derived_provenance={(strict.ProvenanceMatches
                            ? "matches" : "MISMATCH")}");
                    foreach (var error in strict.ProvenanceErrors)
                        Console.Error.WriteLine($"provenance error: {error}");
                    return strict.ExitCode;
                }
                using var r = Lex.Index.LexIndexReader.Open(db);
                // A valid signature over the metadata proves nothing about the text. Recompute
                // the content digest from what the database actually holds and compare it with
                // the signed value: that is what detects an edited article.
                var claimed = r.Stamp.GetValueOrDefault("content_digest") ?? "";
                var actual = r.ComputeContentDigest();
                var contentOk = claimed.Length > 0 && claimed == actual;
                Console.WriteLine($"collection={r.Collection} schema={r.Stamp.GetValueOrDefault("schema")} " +
                    $"algorithm={r.Stamp.GetValueOrDefault("algorithm")} corpus_commit={r.Stamp.GetValueOrDefault("corpus_commit")} " +
                    $"built_at={r.Stamp.GetValueOrDefault("built_at")} signature_valid={r.SignatureValid} " +
                    $"content_digest={(claimed.Length == 0 ? "absent (index predates content binding)" : contentOk ? "matches" : "MISMATCH — contents were altered")}");
                if (!r.SignatureValid) return 3;
                if (claimed.Length > 0 && !contentOk) return 4;
                return 0;
            }
            case "derive":
            {
                var publisher = Get("--publisher") ?? "lu-legilux";
                var corpus = Get("--corpus") ?? throw new ArgumentException("--corpus required");
                var articles = Get("--articles") ?? throw new ArgumentException("--articles required");
                var deriverCodeCommit = Lex.Temporal.CodeIdentity.RequireFullCommit(
                    Get("--code-commit"), "--code-commit");
                var corpusCommit = Lex.Temporal.CodeIdentity.RequireFullCommit(
                    Get("--corpus-commit"), "--corpus-commit");
                var deriverTreeId = Lex.Temporal.CodeIdentity.RequireFullGitObjectId(
                    Get("--deriver-tree-id"), "--deriver-tree-id");
                var onlyWork = Get("--work");
                var tmp = Path.Combine(Path.GetTempPath(), $"lex-verify-{Guid.NewGuid():N}");
                try
                {
                    // re-derive (optionally one work via a filtered shadow corpus) and byte-compare
                    var corpusToUse = corpus;
                    if (onlyWork is not null)
                    {
                        corpusToUse = Path.Combine(tmp, "corpus");
                        Directory.CreateDirectory(Path.Combine(corpusToUse, "works"));
                        File.Copy(Path.Combine(corpus, "manifest.json"), Path.Combine(corpusToUse, "manifest.json"));
                        CopyDir(Path.Combine(corpus, "works", onlyWork), Path.Combine(corpusToUse, "works", onlyWork));
                    }
                    var outDir = Path.Combine(tmp, "articles");
                    Lex.Derive.DeriveWriter.Derive(
                        corpusToUse, outDir, publisher, deriverCodeCommit,
                        deriverTreeId, corpusCommit);
                    int compared = 0, mismatched = 0, missing = 0;
                    foreach (var f in Directory.EnumerateFiles(Path.Combine(outDir, publisher), "*.*", SearchOption.AllDirectories))
                    {
                        var rel = Path.GetRelativePath(outDir, f);
                        var published = Path.Combine(articles, rel);
                        compared++;
                        if (!File.Exists(published)) { missing++; Console.Error.WriteLine($"MISSING in published layer: {rel}"); }
                        else if (!File.ReadAllBytes(f).SequenceEqual(File.ReadAllBytes(published)))
                        { mismatched++; Console.Error.WriteLine($"MISMATCH: {rel}"); }
                    }
                    Console.WriteLine($"verify derive: compared={compared} mismatched={mismatched} missing={missing}");
                    return mismatched == 0 && missing == 0 ? 0 : 3;
                }
                finally { try { Directory.Delete(tmp, true); } catch { } }
            }
            default:
                Console.Error.WriteLine("usage: lex verify corpus --corpus X | lex verify stamp --db X [--expected-collection ID] [--expected-corpus-commit SHA] [--expected-code-commit SHA] [--expected-articles-commit SHA] [--corpus-manifest FILE --articles-generation FILE] | lex verify derive --publisher P --corpus X --articles Y [--work slug]");
                return 1;
        }
    }
    case "repair":
    {
        if ((args0.Length > 1 ? args0[1] : "") != "checkout-line-endings")
            throw new ArgumentException("usage: lex repair checkout-line-endings --corpus X");
        var corpus = Get("--corpus") ?? throw new ArgumentException("--corpus required");
        var report = CheckoutLineEndings.Repair(corpus);
        Console.WriteLine(System.Text.Json.JsonSerializer.Serialize(report, new System.Text.Json.JsonSerializerOptions
        {
            PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.SnakeCaseLower,
            WriteIndented = true,
        }));
        return report.IsValid ? 0 : 6;
    }
    case "artifact":
    {
        switch (args0.Length > 1 ? args0[1] : "")
        {
            case "manifest":
            {
                var root = Get("--root") ?? throw new ArgumentException("--root required");
                var files = GetAll("--file").ToArray();
                if (files.Length == 0) throw new ArgumentException("at least one --file required");
                var manifestPath = Get("--manifest") ?? throw new ArgumentException("--manifest required");
                var signaturePath = Get("--signature");
                var keyFile = Get("--keyfile");
                var keyId = Get("--key-id") ?? throw new ArgumentException("--key-id required");
                var codeCommit = Get("--code-commit") ?? throw new ArgumentException("--code-commit required");
                if ((signaturePath is null) != (keyFile is null))
                    throw new ArgumentException("--signature and --keyfile must be supplied together; omit both for Key Vault signing");
                var sources = GetAll("--source").Select(s => s.Split('=', 2))
                    .ToDictionary(p => p[0], p => p.Length == 2 ? p[1] : "", StringComparer.Ordinal);
                var manifest = ArtifactManifests.Create(
                    root, files, keyId, now.ToString("yyyy-MM-ddTHH:mm:ssZ"), codeCommit, sources);
                var bytes = ArtifactManifests.Serialize(manifest);
                File.WriteAllBytes(manifestPath, bytes);
                if (signaturePath is not null && keyFile is not null)
                    File.WriteAllText(signaturePath,
                        ArtifactManifests.SignBase64(bytes, File.ReadAllText(keyFile)) + "\n",
                        new System.Text.UTF8Encoding(false));
                Console.WriteLine($"manifest={manifestPath} signature={signaturePath ?? "external"} files={manifest.Files.Count} key_id={keyId}");
                return 0;
            }
            case "verify":
            {
                var root = Get("--root") ?? throw new ArgumentException("--root required");
                var manifest = Get("--manifest") ?? throw new ArgumentException("--manifest required");
                var signature = Get("--signature") ?? throw new ArgumentException("--signature required");
                var roots = Get("--trust-roots") ?? throw new ArgumentException("--trust-roots required");
                var trusted = ArtifactManifests.ParseTrustRoots(File.ReadAllBytes(roots));
                var files = ArtifactManifests.VerifyDirectory(root, manifest, signature, trusted);
                Console.WriteLine($"artifact manifest valid: {files.Count} file(s)");
                return 0;
            }
            case "trust-root":
            {
                var keyFile = Get("--keyfile") ?? throw new ArgumentException("--keyfile required");
                var keyId = Get("--key-id") ?? throw new ArgumentException("--key-id required");
                var root = ArtifactManifests.TrustRoot(keyId, File.ReadAllText(keyFile));
                Console.WriteLine(System.Text.Encoding.UTF8.GetString(ArtifactManifests.SerializeTrustRoots([root])));
                return 0;
            }
            default:
                Console.Error.WriteLine("usage: lex artifact manifest|verify|trust-root ...");
                return 1;
        }
    }
    case "dataset":
    {
        // One JSON line per provision-version, per publisher: the AI-builder consumption file.
        var articles = Get("--articles") ?? throw new ArgumentException("--articles required");
        var outDir = Get("--out") ?? throw new ArgumentException("--out required");
        Directory.CreateDirectory(outDir);
        foreach (var pubDir in Directory.EnumerateDirectories(articles).Where(d => Directory.Exists(Path.Combine(d, "works"))).OrderBy(d => d, StringComparer.Ordinal))
        {
            var pub = Path.GetFileName(pubDir);
            var outPath = Path.Combine(outDir, $"{pub}-provisions.jsonl.gz");
            var rowCount = 0;
            await using var fs = File.Create(outPath);
            await using var gz = new System.IO.Compression.GZipStream(fs, System.IO.Compression.CompressionLevel.Optimal);
            await using var w = new StreamWriter(gz, new System.Text.UTF8Encoding(false));
            foreach (var jf in Directory.EnumerateFiles(pubDir, "*.json", SearchOption.AllDirectories)
                         .Where(f => Path.GetFileName(Path.GetDirectoryName(Path.GetDirectoryName(f))!) == "versions")
                         .OrderBy(f => f, StringComparer.Ordinal))
            {
                using var doc = System.Text.Json.JsonDocument.Parse(File.ReadAllText(jf));
                var root = doc.RootElement;
                string? S2(System.Text.Json.JsonElement e, string k) => e.TryGetProperty(k, out var v) && v.ValueKind == System.Text.Json.JsonValueKind.String ? v.GetString() : null;
                foreach (var p in root.GetProperty("provisions").EnumerateArray())
                {
                    var line = new System.Text.Json.Nodes.JsonObject
                    {
                        ["provision_id"] = S2(p, "provision_id"),
                        ["lex_id"] = S2(root, "lex_id"),
                        ["anchor"] = S2(p, "anchor"),
                        ["type"] = S2(p, "type"),
                        ["num"] = S2(p, "num"),
                        ["heading"] = S2(p, "heading"),
                        ["language"] = S2(root, "language"),
                        ["valid_from"] = S2(root, "valid_from"),
                        ["valid_to"] = S2(root, "valid_to"),
                        ["article_valid_from"] = S2(p, "article_valid_from"),
                        ["title"] = S2(root, "title"),
                        ["text_md"] = S2(p, "text_md"),
                        ["text_sha256"] = S2(p, "text_sha256"),
                        ["source_sha256"] = S2(root.GetProperty("derived_from"), "sha256"),
                        ["source_uri"] = S2(root.GetProperty("derived_from"), "source_uri"),
                        ["profile"] = S2(root.GetProperty("generator"), "profile"),
                        ["license"] = S2(root, "license"),
                        ["attribution"] = S2(root, "attribution"),
                    };
                    await w.WriteLineAsync(line.ToJsonString(new System.Text.Json.JsonSerializerOptions
                    { Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping }));
                    rowCount++;
                }
            }
            Console.Error.WriteLine($"  [dataset] {outPath}: {rowCount} provision rows");
        }
        return 0;
    }
    case "catalog":
    {
        var articles = Get("--articles") ?? throw new ArgumentException("--articles required");
        var s = Lex.Derive.CatalogBuilder.Build(articles);
        Console.Error.WriteLine($"  [catalog] works={s.Works} anchors={s.Anchors} history_states={s.HistoryStates}");
        return 0;
    }
    default:
        Usage();
        return 1;
}

static void CopyDir(string src, string dst)
{
    Directory.CreateDirectory(dst);
    foreach (var f in Directory.EnumerateFiles(src)) File.Copy(f, Path.Combine(dst, Path.GetFileName(f)));
    foreach (var d in Directory.EnumerateDirectories(src)) CopyDir(d, Path.Combine(dst, Path.GetFileName(d)));
}

static string FormatDuration(TimeSpan value)
{
    var sign = value < TimeSpan.Zero ? "-" : "";
    var absolute = value.Duration();
    return $"{sign}{(int)absolute.TotalHours:00}:{absolute.Minutes:00}:{absolute.Seconds:00}";
}

static void Usage() => Console.Error.WriteLine("""
    lex — point-in-time regulatory text pipeline
      lex embedding-smoke --model-dir PATH [--text TEXT] [--batch-size N]
      lex scope-preview [--publisher ID] [--scope FILE] [--previous-scope FILE] [--wave 1..4]
      lex ingest --publisher ID --corpus PATH --code-commit FULL_SHA [--scope FILE] [--wave 1..4] [--now ISO]
                 [--fresh]
      lex index  --corpus PATH [--articles PATH --articles-commit FULL_SHA] --out FILE.db [--keyfile KEY.pem] [--now ISO]
                 [--embedding-model PATH] [--vectors FILE] [--embedding-batch-size N]
                 [--time-budget-minutes N] --corpus-commit FULL_SHA --code-commit FULL_SHA
      lex derive --publisher ID --corpus PATH --out PATH --code-commit FULL_SHA
                 --deriver-tree-id FULL_GIT_TREE_ID --corpus-commit FULL_SHA
      lex verify corpus --corpus PATH
      lex verify stamp --db FILE.db --expected-collection ID --expected-corpus-commit FULL_SHA
                 --expected-code-commit FULL_SHA --expected-articles-commit FULL_SHA
                 --corpus-manifest FILE --articles-generation FILE
      lex repair checkout-line-endings --corpus PATH
      lex artifact manifest --root DIR --file RELATIVE [--file RELATIVE] --manifest FILE --signature FILE --keyfile KEY.pem --key-id ID --code-commit SHA [--source KEY=VALUE]
      lex assistant-eval --admission FILE --admission-signature FILE --base-url REVISION_URL --candidate-container-app-resource-id AZURE_ID --candidate-revision NAME --cases FILE --review-attestation FILE --review-signature FILE --out FILE --candidate-model-resource-id AZURE_ID --candidate-deployment ID --grader-model-resource-id AZURE_ID --grader-deployment ID [--grader-key-env NAME]
      lex assistant-eval diagnostic --admission FILE --admission-signature FILE --base-url REVISION_URL --candidate-container-app-resource-id AZURE_ID --candidate-revision NAME --cases FILE --review-attestation FILE --review-signature FILE --out FILE --candidate-model-resource-id AZURE_ID --candidate-deployment ID --grader-model-resource-id AZURE_ID --grader-deployment ID [--grader-key-env NAME]  # non-publishable; grader max_completion_tokens=8000
      lex assistant-eval verify-cases --cases FILE --review-attestation FILE --review-signature FILE
      lex assistant-eval verify-report --report FILE --cases FILE --review-attestation FILE --review-signature FILE --admission FILE --admission-signature FILE --base-url REVISION_URL --candidate-container-app-resource-id AZURE_ID --candidate-revision NAME
      lex assistant-eval verify-release --root DIR --manifest FILE --signature FILE --trust-roots FILE --base-url REVISION_URL --candidate-container-app-resource-id AZURE_ID --candidate-revision NAME
      lex assistant-eval verify-bootstrap-equivalence --root DIR --manifest FILE --signature FILE --trust-roots FILE --equivalence FILE --candidate-container-app-resource-id AZURE_ID --legacy-authority-revision NAME --candidate-revision NAME --rollback-revision NAME --evaluation-release TAG --canonical-template-digest SHA256 --image-digest SHA256 --cases-sha256 SHA256 [--established-release-state | --historical-source-package --expected-code-commit FULL_SHA]
      lex assistant-eval create-admission --cases FILE --review-attestation FILE --review-signature FILE --candidate-revision NAME --candidate-image IMAGE_DIGEST --code-commit FULL_SHA --artifact-manifest-set SHA256 --out FILE
      lex assistant-eval verify-admission --cases FILE --review-attestation FILE --review-signature FILE --candidate-revision NAME --candidate-image IMAGE_DIGEST --code-commit FULL_SHA --artifact-manifest-set SHA256 --admission FILE --signature FILE
      lex artifact verify --root DIR --manifest FILE --signature FILE --trust-roots FILE
      lex artifact trust-root --keyfile KEY.pem --key-id ID
    """);
