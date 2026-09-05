using Lex.V3.Artifacts;
using Lex.V3.Contracts.Custody;
using Lex.V3.Contracts.Source.Core;
using Lex.V3.Contracts.Source.Corpus;
using Lex.V3.Contracts.Source.Europe;
using Lex.V3.Contracts.Source.Scope;
using Lex.V3.Ingest.Europe;

namespace Lex.V3.Ingest.Tests;

/// <summary>
/// The Stage 1 acceptance canary for the Union acquisition route, the EU twin of
/// <c>LuxembourgCodeCivilAcquisitionCanary</c>. Its standard is NOT parity with any deployed
/// service, which stays withdrawn: every object in the closure is either Held with a real receipt
/// or refused for exactly one of the four legitimate reasons, stated per object, with the accepted
/// fraction reported as a number.
/// </summary>
/// <remarks>
/// <para>
/// OPT IN, AND DELIBERATELY SO. It sends real requests to publications.europa.eu, so it is skipped
/// unless LEX_EU_CANARY=1 is set. A network test that ran by default would make the suite depend on
/// a third party's uptime and would send traffic nobody asked for. The test EXISTING and being
/// GATED is a fact about this repository; it is not evidence that any run passed, and the two are
/// reported separately.
/// </para>
/// <para>
/// It uses the REAL <see cref="FileSystemCustodyStore"/>, never a synthetic one: a canary that
/// proved bodies were held against a synthetic store would prove nothing. That store publishes
/// CustodyVerificationProfile.FileSystemUnenforced1 with CustodyProtection.NotEnforced, so every
/// artifact this run holds is RetainedUnenforced and says so, which is what Decision 71's
/// interpretation (RULING lex-event-20260904T212914634Z-f166f0b9e11b445795efd40c268bfbb8 and its
/// gate extension lex-event-20260904T213727510Z-671a8c2563684ab49048677997ceef1c) made possible.
/// Before those, this run refused before its first product request.
/// </para>
/// <para>
/// A TEST ASSEMBLED RUN, not a production path. Per RULING
/// lex-event-20260904T231236855Z-8c7a540fc4d2420f859f9d92fdfc733a, no shipped code chooses a
/// production implementation for any door <see cref="EuQueryExecutionAdapter.RunAsync"/> takes:
/// nothing in src constructs either acquisition adapter, and the first assembled production run is
/// Stage 6 R6-01. So every statement about which implementation a door uses is about THIS TEST's
/// assembly. Door by door: the custody store is the production
/// <see cref="FileSystemCustodyStore"/>; the executor is the production
/// <see cref="EuRepeatedEnumerationExecutor"/> on its live constructor; the plans
/// (<see cref="EuConsolidationDiscoveryPlan"/>, <see cref="EuObjectFactsDiscoveryPlan"/>) and the
/// bound requests through their own binders are production types; the renderer sources are
/// production <see cref="MachineQueryRendererSource"/> values minted from bytes this test retains;
/// and the evidence resolver has NO EU PRODUCTION IMPLEMENTATION AT ALL, which is residue R0.
/// </para>
/// <para>
/// THE BOUNDARY THIS RUN DOES NOT CROSS, stated as plainly as the LU canary states its own. The
/// evidence resolver is <see cref="CanaryPermissiveEvidenceResolver"/>, a TEST DOUBLE with the same
/// behaviour as the adapter tests' own. Its <c>IsSelectorObservationAdmitted</c>,
/// <c>IsSelectorNotApplicableAdmitted</c> and <c>IsRuleEvaluationAdmitted</c> admit ON SHA-256 SHAPE
/// ALONE: they check that the binding's digest fields are 64 lowercase hex characters and nothing
/// else. Its <c>IsCompleteEnumerationAdmitted</c> compares against the ref THE CALLER HANDED ITS
/// CONSTRUCTOR. So THIS RUN DOES NOT PROVE THE REDUCTION STEP. Any sentence saying the manifest was
/// reduced from the enumeration carries that qualification. What the run checks about the reduction
/// is its OUTPUT, against an independent census obtained outside this lane, which is a materially
/// different and weaker claim than a proven admission policy.
/// </para>
/// <para>
/// CONDITION TWO, AND WHAT IS ACTUALLY ASSERTED TODAY. The census of RULING
/// lex-event-20260904T232128757Z-e21b4aedbfc4412dba8e6533ab2499d0, measured at the endpoint outside
/// this lane, is carried as DATA on <see cref="Census"/>, per type, rather than as prose: the GDPR
/// at 24 expressions and 72 manifestations with fmx4 24, pdfa1a 24 and xhtml 24; 32003L0088 at 23
/// expressions and 71 manifestations with fmx4 1, html 20, pdf 22, pdfa1a 1, print 23 and xhtml 4.
/// The second seed is IRREGULAR, so nothing here assumes one manifestation of each type per
/// expression; that assumption would pass on the GDPR and be wrong on the other seed.
/// </para>
/// <para>
/// WHAT THIS TEST ASSERTS, today, as opposed to what it discusses. That the census table's own per
/// type counts sum to its row totals. That both census families PROVED and that each carries
/// <see cref="CustodyMembership.RetainedUnenforced"/>. And, WHEN THE RUN REACHES THE MANIFEST, the
/// reduced manifest's expression count against the census total. WHEN IT DOES NOT REACH THE
/// MANIFEST IT CALLS <c>Assert.Fail</c> WITH THE REFUSAL IN WORDS. There is no path through this
/// method that passes silently, and today it does not pass at all: the run stops at the
/// object-facts families, which is D1-05f.
/// </para>
/// <para>
/// ONE COMPARISON THE CENSUS MAKES POSSIBLE IS NOT YET ASSERTABLE, and it is failed loudly rather
/// than discussed. Family M as this route queries it returns the DISTINCT manifestation types per
/// work, not one row per manifestation, so the run observes three tokens for the GDPR where the
/// census counts 72 manifestations. Asserting 72 against a number this route cannot produce would
/// be a fabricated check, so the totals are carried as data and the gap is stated by a failing
/// assertion rather than by a sentence a reader may not reach. D1-05f decides whether the canary
/// enumerates manifestations or the comparison narrows to the token set.
/// </para>
/// <para>
/// The fetch half is separately corroborated by RULING
/// lex-event-20260904T232443443Z-e3a024f2bda04e1ba90ae14a02848068: three independent observations of
/// the publisher agree byte for byte on 32003L0088's text/html body,
/// 0d23ad4953be900de8a614fea4022aa46086e0bdc2fdfd6d0fde0cd84429e4b6 at 37,616 bytes. The PUBLISHER
/// is the authority; that agreement is evidence this route's fetch is correct and that the pinned
/// constant is the publisher's real bytes rather than a value recorded once and never rechecked. It
/// proves nothing about the reduction step, nothing about custody, and nothing about the other seed.
/// </para>
/// </remarks>
[TestClass]
[DoNotParallelize]
public sealed class EuStageOneAcquisitionCanary
{
    private const string EnableVariable = "LEX_EU_CANARY";

    private const string Gdpr = "32016R0679";
    private const string WorkingTime = "32003L0088";

    /// <summary>
    /// The independent census, RULING lex-event-20260904T232128757Z-e21b4aedbfc4412dba8e6533ab2499d0,
    /// measured at the endpoint outside this lane. Carried as DATA, per type, so the comparison is a
    /// check rather than a sentence. Manifestations is the row total and must equal the sum of the
    /// per type counts, which the test asserts of the table itself before using it.
    /// </summary>
    private static readonly (string Celex, int Expressions, int Manifestations,
        (string Token, int Count)[] Types)[] Census =
    [
        (Gdpr, 24, 72, [("fmx4", 24), ("pdfa1a", 24), ("xhtml", 24)]),
        (WorkingTime, 23, 71,
            [("fmx4", 1), ("html", 20), ("pdf", 22), ("pdfa1a", 1), ("print", 23), ("xhtml", 4)]),
    ];

    [TestMethod]
    public async Task BothCanarySeedsAreHeldOrRefusedForALegitimateReason()
    {
        if (Environment.GetEnvironmentVariable(EnableVariable) != "1")
        {
            Assert.Inconclusive(
                $"Live publisher canary. Set {EnableVariable}=1 to run it; it is skipped by default "
                + "so the suite does not depend on a third party's uptime or send unasked traffic.");
            return;
        }

        foreach (var (celex, expressions, manifestations, types) in Census)
        {
            // The table checks itself before anything is compared against it: a census whose per
            // type counts do not sum to its own row total is a transcription error, and finding that
            // out here is better than finding it out as a mismatch against the run.
            Assert.AreEqual(
                manifestations,
                types.Sum(static entry => entry.Count),
                $"{celex}'s census per type counts must sum to its own manifestation total.");
            Console.WriteLine(
                $"CANARY|census|{celex}|expressions={expressions}|manifestations={manifestations}|"
                + string.Join(",", types.Select(static entry => $"{entry.Token}={entry.Count}")));
        }

        // Retained where a later slice can read it. LEX_EU_CANARY_ROOT lets a run keep its
        // custody store somewhere durable; the default stays under the system temp directory so no
        // path literal is committed.
        var root = Environment.GetEnvironmentVariable("LEX_EU_CANARY_ROOT")
            ?? Path.Combine(Path.GetTempPath(), "lex-eu-canary-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        Console.WriteLine($"CANARY|custodyRoot|{root}");

        var store = new FileSystemCustodyStore(root);
        var executor = new EuRepeatedEnumerationExecutor(store, TimeProvider.System);
        var adapter = new EuQueryExecutionAdapter(store, executor);

        var seeds = EuAppendixASeedMap.SeedsInCelexOrder
            .Where(seed => seed.Celex == Gdpr || seed.Celex == WorkingTime)
            .ToArray();
        Assert.HasCount(2, seeds, "both canary seeds must be Appendix A members.");

        var roots = seeds
            .Select(seed => EuPackRootCanonicalForm.TryCanonicalize(seed.WorkRoot, out _)
                ?? throw new AssertFailedException($"{seed.Celex}'s own seed root failed to canonicalize."))
            .OrderBy(static value => value, StringComparer.Ordinal)
            .ToArray();
        foreach (var (seed, index) in seeds.Select(static (seed, index) => (seed, index)))
        {
            Console.WriteLine($"CANARY|seed|{index}|{seed.Celex}|{seed.WorkRoot}");
        }

        var censusRequests = seeds
            .Select(seed =>
            {
                var (plan, planId) = EuAcquisitionTestFixture.BuildCensusPlan();
                return (
                    Request: new EuCensusPartitionRunRequest(
                        plan, planId, seed.Celex, EuAcquisitionTestFixture.BuildRendererSource(6100)),
                    Witness: EuAcquisitionTestFixture.SourceWitness());
            })
            .ToArray();

        var objectFactsRequests = new[]
            {
                EuObjectFactsQuerySet.ObjectFacts,
                EuObjectFactsQuerySet.ExpressionFacts,
                EuObjectFactsQuerySet.RootWatermark,
                EuObjectFactsQuerySet.ManifestationFacts,
            }
            .Select(set =>
            {
                var (plan, planId) = EuAcquisitionTestFixture.BuildObjectFactsPlan();
                return (
                    Request: new EuObjectFactsPartitionRunRequest(
                        plan, planId, set, roots, EuAcquisitionTestFixture.BuildRendererSource(6200)),
                    Witness: EuAcquisitionTestFixture.SourceWitness());
            })
            .ToArray();

        var completeEnumerationRef = new SourceArtifactRef(
            $"urn:uuid:{Guid.NewGuid():D}",
            System.Convert.ToHexStringLower(
                System.Security.Cryptography.SHA256.HashData(
                    System.Text.Encoding.UTF8.GetBytes("eu-canary-complete-enumeration"))));

        var result = await adapter.RunAsync(
            censusRequests,
            objectFactsRequests,
            EuAcquisitionTestFixture.BuildRendererSource(6300),
            EuAcquisitionTestFixture.SourceWitness(),
            EuAcquisitionTestFixture.BuildRendererSource(6400),
            EuAcquisitionTestFixture.DocumentFetchSourceWitness(),
            new CanaryPermissiveEvidenceResolver(completeEnumerationRef),
            CancellationToken.None);

        Console.WriteLine($"CANARY|refusal|{result.Refusal?.Code}|{result.Refusal?.Detail}");
        Console.WriteLine($"CANARY|completion|{result.Completion}");
        Console.WriteLine($"CANARY|observedObjects|{result.ObservedObjectCount}");
        Console.WriteLine($"CANARY|observedExpressions|{result.ObservedExpressionCount}");
        Console.WriteLine($"CANARY|decodeRefusal|{result.DecodeRefusal}|{result.DecodeOffendingIri}");

        foreach (var outcome in result.FamilyOutcomes)
        {
            // Every field of the executor's refusal, because this run's retained evidence is the
            // only place D1-05f's scoping inputs exist and whoever opens it should not have to
            // re-run a live enumeration to find out what it is fixing. OffendingKey carries the
            // cursor that did not advance; ResponseBodySha256 carries the digest of a malformed
            // page body, whose bytes are under the custody root printed above.
            var refusal = outcome.ExecutorRefusal;
            Console.WriteLine(
                $"CANARY|family|{outcome.FamilyKey}|{outcome.Kind}|floor={outcome.RetainedFloor}|"
                + $"executorRefusal={refusal?.Code}|proof={outcome.ProofRefusal}|"
                + $"offendingKey={refusal?.OffendingKey}|"
                + $"responseBodySha256={refusal?.ResponseBodySha256}|"
                + $"terminalStatus={refusal?.TerminalStatus}|"
                + $"observedMediaType={refusal?.ObservedMediaType}|"
                + $"observedCount={refusal?.ObservedCount}|"
                + $"requestOrdinal={refusal?.RequestOrdinal}|"
                + $"attemptOrdinalReached={refusal?.AttemptOrdinalReached}|"
                + $"detail={refusal?.CoreRefusalDetail}");
        }

        if (result.DocumentAcquisitionOutcomesByOrdinal is { } bodies)
        {
            var held = 0;
            foreach (var (ordinal, outcome) in bodies.OrderBy(static entry => entry.Key))
            {
                var floor = outcome.Receipt is null
                    ? null
                    : (CustodyMembership?)CorpusBodyRecord.Held(outcome.Receipt).Floor;
                Console.WriteLine(
                    $"CANARY|body|{ordinal}|held={outcome.Receipt is not null}|floor={floor}|"
                    + $"digest={outcome.Receipt?.Reference.ContentSha256}|"
                    + $"bytes={outcome.Receipt?.Reference.ByteLength}|refusal={outcome.Refusal}");
                if (outcome.Receipt is not null)
                {
                    held++;
                }
            }

            var fraction = bodies.Count == 0 ? 0d : (double)held / bodies.Count;
            Console.WriteLine($"CANARY|acceptedFraction|{held}/{bodies.Count}|{fraction:F4}");
        }

        if (result.DocumentLadderResultsByOrdinal is { } ladders)
        {
            foreach (var (ordinal, ladder) in ladders.OrderBy(static entry => entry.Key))
            {
                Console.WriteLine(
                    $"CANARY|ladder|{ordinal}|attempted={string.Join(",", ladder.Attempted)}|"
                    + $"served={ladder.Served}");
            }
        }

        if (result.CorpusRecordSet is { } set)
        {
            var records = set.Set.Records;
            Console.WriteLine(
                $"CANARY|records|{records.Count}|"
                + $"held={records.Count(static r => r.Body.Kind == CorpusBodyRecordKind.Held)}");
            foreach (var record in records)
            {
                Console.WriteLine(
                    $"CANARY|record|{record.ObjectRef.CanonicalKey}|{record.Body.Kind}|"
                    + $"floor={record.Body.Floor}|notHeld={record.Body.NotHeldReason}|"
                    + $"pending={record.Body.PendingAcquisitionReason}");
            }
        }

        await WriteEvidenceIndexAsync(store, root, result, CancellationToken.None);

        // CONDITION TWO, first half, reachable today and asserted unconditionally: both census
        // families proved, and each says which of the three custody classes its run was.
        var censusProofs = result.FamilyOutcomes
            .Where(static outcome => outcome.Kind == EuFamilyEnumerationOutcomeKind.Proven)
            .ToArray();
        Assert.HasCount(
            Census.Length,
            censusProofs,
            "one proved census family per seed is the least this run must establish; got "
            + string.Join(", ", result.FamilyOutcomes.Select(
                static outcome => $"{outcome.Kind}/{outcome.ExecutorRefusal?.Code}")));
        foreach (var proven in censusProofs)
        {
            Assert.AreEqual(
                CustodyMembership.RetainedUnenforced,
                proven.RetainedFloor,
                $"family {proven.FamilyKey} ran over a filesystem store and must say so.");
        }

        // Either the run reached the manifest and the census comparison happens, or it did not and
        // this FAILS IN WORDS. There is no third path and none of them is a silent pass.
        if (result.Refusal is { } wholeRunRefusal)
        {
            Assert.Fail(
                "the run did not reach the manifest, so the census comparison never ran. It refused as "
                + $"{wholeRunRefusal.Code}: {wholeRunRefusal.Detail} Per family: "
                + string.Join("; ", result.FamilyOutcomes.Select(
                    static outcome => $"{outcome.FamilyKey} {outcome.Kind} "
                        + $"{outcome.ExecutorRefusal?.Code} body={outcome.ExecutorRefusal?.ResponseBodySha256}"))
                + " This is D1-05f.");
        }

        Assert.AreEqual(
            Census.Sum(static row => row.Expressions),
            result.ObservedExpressionCount,
            "the reduced manifest's expression count must equal the independent census total.");

        Assert.Fail(
            "the run reached the manifest, and the per type totals are still not comparable: family M "
            + "returns DISTINCT manifestation types per work rather than one row per manifestation, so "
            + "this route cannot produce the census totals "
            + string.Join("; ", Census.Select(
                static row => $"{row.Celex} {row.Manifestations} across "
                    + string.Join(",", row.Types.Select(static entry => $"{entry.Token}={entry.Count}"))))
            + ". Asserting them against a number the route cannot produce would be a fabricated check, "
            + "so this fails rather than passing on the half it can reach. D1-05f decides whether the "
            + "canary enumerates manifestations or the comparison narrows to the token set.");
    }

    /// <summary>
    /// The run's own EVIDENCE INDEX: a manifest of what it retained, BY ROLE, written into custody
    /// as a content-addressed artifact and beside the store as plain JSON.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A content-addressed store is files named by their own digests and nothing else, so a later
    /// reader can verify any byte they can already name and cannot discover WHICH byte is which.
    /// Filling an evidence event by pattern-matching a directory would be presenting a guess as a
    /// digest. The index is therefore built BY THE RUN, which knows the roles, and never
    /// reconstructed afterwards.
    /// </para>
    /// <para>
    /// It records the run's own git sha and whether the tree was clean, because for the first
    /// retained run that pair had to be reconstructed from file timestamps, and a reconstruction is
    /// not evidence. A run that cannot say which source produced it has retained bytes without
    /// retaining their provenance.
    /// </para>
    /// <para>
    /// WHAT IT CANNOT YET REACH, recorded IN the index rather than quietly omitted. Two roles are
    /// not on <see cref="EuQueryExecutionResult"/> at all: each family's pass A and pass B page
    /// bodies with their cursor values, which live on the delivery receipt that
    /// <see cref="EuFamilyEnumerationOutcome"/> does not carry, and the robots bootstrap artifact,
    /// which the routed session writes inside the executor. D1-05f has to surface both before the
    /// index can carry them, and the file says so where a reader will see it.
    /// </para>
    /// </remarks>
    private static async Task WriteEvidenceIndexAsync(
        FileSystemCustodyStore store,
        string root,
        EuQueryExecutionResult result,
        CancellationToken cancellationToken)
    {
        var dirty = TryGit("status --porcelain");
        var index = new System.Text.Json.Nodes.JsonObject
        {
            ["schema"] = "lex-eu-canary-evidence-index/1",
            ["runGitSha"] = TryGit("rev-parse HEAD"),
            ["runTreeClean"] = dirty is null ? null : dirty.Length == 0,
            ["runTreeDirtyPaths"] = dirty,
            ["custodyClassSegment"] = "nightly-floor-90d",
            ["wholeRunRefusalCode"] = result.Refusal?.Code.ToString(),
            ["wholeRunRefusalDetail"] = result.Refusal?.Detail,
            ["completion"] = result.Completion?.ToString(),
            ["observedObjectCount"] = result.ObservedObjectCount,
            ["observedExpressionCount"] = result.ObservedExpressionCount,
        };

        var families = new System.Text.Json.Nodes.JsonArray();
        foreach (var outcome in result.FamilyOutcomes)
        {
            var refusal = outcome.ExecutorRefusal;
            families.Add(new System.Text.Json.Nodes.JsonObject
            {
                ["role"] = "family",
                ["familyKey"] = outcome.FamilyKey,
                ["kind"] = outcome.Kind.ToString(),
                ["retainedFloor"] = outcome.RetainedFloor?.ToString(),
                ["proofDeliveredRowCount"] = outcome.Proof?.DeliveredRowCount,
                ["proofCanonicalKeyDigest"] = outcome.Proof?.CanonicalKeyDigest,
                ["proofAcquisitionRunSha256"] = outcome.Proof?.AcquisitionRunRef.Sha256,
                ["proofInterpretationProfileSha256"] = outcome.Proof?.InterpretationProfileRef.Sha256,
                ["proofSourceProfileSha256"] = outcome.Proof?.SourceProfileRef.Sha256,
                ["refusalKind"] = refusal?.Code.ToString(),
                ["refusedBodySha256"] = refusal?.ResponseBodySha256,
                ["refusedBodyTerminalStatus"] = refusal?.TerminalStatus,
                ["refusedBodyObservedMediaType"] = refusal?.ObservedMediaType,
                ["countAnswerItShouldHaveMatched"] = refusal?.ObservedCount,
                ["offendingKey"] = refusal?.OffendingKey,
                ["requestOrdinal"] = refusal?.RequestOrdinal,
                ["proofRefusal"] = outcome.ProofRefusal?.ToString(),
            });
        }

        index["families"] = families;

        index["scopeManifest"] = new System.Text.Json.Nodes.JsonObject
        {
            ["role"] = "scopeManifest",
            ["receiptContentSha256"] = result.ScopeManifestReceipt?.Reference.ContentSha256,
            ["receiptByteLength"] = result.ScopeManifestReceipt?.Reference.ByteLength,
            ["canonicalSha256"] = result.ScopeManifestCanonicalSha256,
        };

        index["corpusRecordSet"] = new System.Text.Json.Nodes.JsonObject
        {
            ["role"] = "corpusRecordSet",
            ["setRefSha256"] = result.CorpusRecordSetRef?.Sha256,
        };

        var bodies = new System.Text.Json.Nodes.JsonArray();
        var outcomes = result.DocumentAcquisitionOutcomesByOrdinal
            ?? new Dictionary<int, CorpusAcquisitionOutcome>();
        foreach (var entry in outcomes.OrderBy(static entry => entry.Key))
        {
            bodies.Add(new System.Text.Json.Nodes.JsonObject
            {
                ["role"] = "documentBody",
                ["manifestRowOrdinal"] = entry.Key,
                ["heldContentSha256"] = entry.Value.Receipt?.Reference.ContentSha256,
                ["heldByteLength"] = entry.Value.Receipt?.Reference.ByteLength,
                ["refusalReason"] = entry.Value.Refusal?.ToString(),
            });
        }

        index["documentBodies"] = bodies;

        index["rolesThisIndexCannotYetCarry"] = new System.Text.Json.Nodes.JsonArray
        {
            new System.Text.Json.Nodes.JsonObject
            {
                ["role"] = "familyPassBodiesAndCursors",
                ["why"] = "EuFamilyEnumerationOutcome carries the proof but not the delivery receipt, "
                    + "so pass A and pass B page bodies and their cursor values are not reachable "
                    + "from EuQueryExecutionResult. D1-05f must surface the receipt to record them.",
            },
            new System.Text.Json.Nodes.JsonObject
            {
                ["role"] = "countAnswerBesideARefusedPage",
                ["why"] = "countAnswerItShouldHaveMatched comes from EuEnumerationRefusalDetail"
                    + ".ObservedCount, which this refusal path leaves null, so the count a refused "
                    + "page should have matched is not stated beside it. Measured out of band by "
                    + "direct probes of the four families: ObjectFacts 41, ExpressionFacts 166, "
                    + "RootWatermark 2, ManifestationFacts 9. D1-05f should populate it on the "
                    + "refusal so the index carries it rather than a reader importing it.",
            },
            new System.Text.Json.Nodes.JsonObject
            {
                ["role"] = "robotsBootstrapArtifact",
                ["why"] = "The routed session writes robots inside the executor and the adapter's "
                    + "result does not name it, so its digest cannot be stated by role from here.",
            },
        };

        var bytes = System.Text.Encoding.UTF8.GetBytes(
            index.ToJsonString(new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));

        var receipt = await store.CreateAsync(bytes, CustodyClass.NightlyFloor90d, cancellationToken)
            .ConfigureAwait(false);
        var beside = Path.Combine(root, "evidence-index.json");
        await File.WriteAllBytesAsync(beside, bytes, cancellationToken).ConfigureAwait(false);

        Console.WriteLine(
            $"CANARY|evidenceIndex|sha256={receipt.Reference.ContentSha256}|bytes={bytes.Length}"
            + $"|beside={beside}");
    }

    /// <summary>
    /// One git invocation, for the run's own sha and tree state. Returns null when git is not
    /// reachable, because provenance recorded as unknown is honest and a fabricated one is not.
    /// </summary>
    private static string? TryGit(string arguments)
    {
        try
        {
            using var process = System.Diagnostics.Process.Start(
                new System.Diagnostics.ProcessStartInfo
                {
                    FileName = "git",
                    Arguments = arguments,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                });
            if (process is null)
            {
                return null;
            }

            var output = process.StandardOutput.ReadToEnd();
            process.WaitForExit(30_000);
            return process.ExitCode == 0 ? output.Trim() : null;
        }
        catch (Exception exception)
            when (exception is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            return null;
        }
    }

    /// <summary>
    /// The same permissive shape as <c>EuQueryExecutionAdapterTests.PermissiveEvidenceResolver</c>,
    /// declared here because that one is private to its own test class. A TEST DOUBLE, and the
    /// reason this canary cannot claim the reduction step: the three admission questions below
    /// answer on SHA-256 SHAPE ALONE, and the fourth compares against the ref this constructor was
    /// handed. Residue R0 is the EU production resolver that would answer them against evidence the
    /// run independently holds, the way
    /// <c>LuxembourgProductionScopeReductionEvidenceResolver</c> already does for Luxembourg.
    /// </summary>
    private sealed class CanaryPermissiveEvidenceResolver(SourceArtifactRef completeEnumerationRef)
        : IScopeReductionEvidenceResolver
    {
        public SourceArtifactRef CompleteEnumerationRef { get; } = completeEnumerationRef;

        public bool IsSelectorObservationAdmitted(ScopeSelectorObservationBinding binding) =>
            IsSha256(binding.ObjectRefSha256) && IsSha256(binding.SelectorEvidenceSha256);

        public bool IsSelectorNotApplicableAdmitted(ScopeSelectorNotApplicableBinding binding) =>
            IsSha256(binding.ObjectRefSha256);

        public bool IsRuleEvaluationAdmitted(ScopeRuleEvaluationBinding binding) =>
            IsSha256(binding.ObjectRefSha256) &&
            IsSha256(binding.SelectorSetSha256) &&
            IsSha256(binding.RuleEvaluationSha256);

        public bool IsCompleteEnumerationAdmitted(ScopeCompleteEnumerationBinding binding) =>
            binding.CompleteEnumerationRef == CompleteEnumerationRef;

        private static bool IsSha256(string value) =>
            value.Length == 64 &&
            value.All(static character => character is (>= '0' and <= '9') or (>= 'a' and <= 'f'));
    }
}
