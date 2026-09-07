using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Lex.V3.Artifacts;
using Lex.V3.Contracts.Custody;
using Lex.V3.Contracts.Source.Core;
using Lex.V3.Contracts.Source.Corpus;
using Lex.V3.Contracts.Source.Europe;
using Lex.V3.Contracts.Source.Scope;
using Lex.V3.Ingest.Europe;

namespace Lex.V3.Ingest.Tests;

/// <summary>
/// S1-A09: the COMPLETE 82-seed EU population run. Decision 84 makes this population mandatory at
/// Stage 1 exit and names <see cref="EuStageOneAcquisitionCanary"/>'s two seeds prerequisite
/// evidence that "never closes the stage".
/// </summary>
/// <remarks>
/// <para>
/// WHY THIS IS EIGHTY-TWO SEPARATE RUNS AND NOT ONE RUN OF EIGHTY-TWO SEEDS, stated first because
/// it is the choice a reviewer should check rather than infer.
/// <see cref="EuQueryExecutionAdapter.RunAsync"/> accepts a LIST of census families and would take
/// all 82 in one call, but its first gate is
/// <c>if (censusByFamilyKey.Count != censusFamilies.Count) return Refused(CensusFamilyNotProven)</c>:
/// ONE seed failing its census refuses the WHOLE run and no other seed's disposition is ever
/// computed. Decision 84 requires the opposite order -- "run the population, count what passes, and
/// decide on the result rather than in advance", and "a run in which eighty pass and two fail is a
/// different decision from one in which forty pass". A combined run cannot produce that number. So
/// each seed gets its own adapter run, its own custody sub-root and its own typed disposition, and
/// the population count is an accounting over 82 measured results rather than one boolean.
/// </para>
/// <para>
/// WHAT THAT COSTS, stated rather than left for a reader to discover: this establishes each seed's
/// OWN end-to-end route, and it does NOT establish a single whole-population manifest over the
/// union of all 82 closures. Those are different artifacts. A Stage 1 exit receipt that needs one
/// manifest naming the whole population needs a further run this class does not perform, and
/// <see cref="PopulationLimitations"/> carries that sentence into the report so it travels with the
/// numbers rather than only with this file.
/// </para>
/// <para>
/// OPT IN, for the same reason the canary is: it sends real requests to publications.europa.eu, 82
/// runs' worth, so it is skipped unless LEX_EU_POPULATION=1. The test EXISTING and being GATED is a
/// fact about this repository and is not evidence that any run passed; the two are reported
/// separately.
/// </para>
/// <para>
/// THE SAME DOORS AS THE CANARY, deliberately. Real <see cref="FileSystemCustodyStore"/>, real
/// <see cref="EuRepeatedEnumerationExecutor"/>, real <see cref="EuQueryExecutionAdapter"/>, and the
/// same <see cref="EuAcquisitionTestFixture"/> plans, renderer sources and bound witnesses. The
/// evidence resolver is the same TEST DOUBLE the canary uses and carries the same boundary: it
/// admits on SHA-256 shape alone, so THIS RUN DOES NOT PROVE THE REDUCTION STEP for any seed. That
/// limitation is the canary's residue R0 and this class inherits it unchanged; it does not weaken
/// it and it must not be read as having closed it.
/// </para>
/// <para>
/// THE POPULATION GATE IS PINNED FROM A MEASUREMENT, never guessed. <see cref="ExpectedReaching"/>
/// and <see cref="ExpectedRefusals"/> are the observed result of the run recorded in the review
/// request for this item. Pinning them is what makes this a guard rather than a report: a seed that
/// silently stops reaching the manifest, or starts refusing for a different typed reason, fails
/// here by name. Re-pinning them is a reviewed act, not maintenance.
/// </para>
/// </remarks>
[TestClass]
[DoNotParallelize]
public sealed class EuStageOnePopulationRun
{
    private const string EnableVariable = "LEX_EU_POPULATION";
    private const string RootVariable = "LEX_EU_POPULATION_ROOT";

    /// <summary>
    /// A DIAGNOSTIC subset, comma-separated CELEX. A subset can never establish the population, so
    /// a run that sets it ends <see cref="Assert.Inconclusive(string)"/> rather than green: Decision
    /// 84's own rule is that "a sample states what it cannot establish", and a green sample would
    /// state the opposite.
    /// </summary>
    private const string SeedSubsetVariable = "LEX_EU_POPULATION_SEEDS";

    /// <summary>
    /// How many of the 82 seeds reach a written, reopened manifest AND a written corpus record set.
    /// Measured, then pinned. See this class's own remarks for why this is a pin and not a target.
    /// </summary>
    private const int ExpectedReaching = 82;

    /// <summary>
    /// Every seed that does NOT reach, with the exact typed
    /// <see cref="EuQueryExecutionRefusal"/> it refused with. Empty when every seed reaches. Keyed
    /// by CELEX so a diff names the seed rather than a count.
    /// </summary>
    private static readonly (string Celex, EuQueryExecutionRefusal Refusal)[] ExpectedRefusals = [];

    [TestMethod]
    public async Task EverySeedOfAppendixASOwnEightyTwoIsRunAndCarriesOneTypedDisposition()
    {
        if (Environment.GetEnvironmentVariable(EnableVariable) != "1")
        {
            Assert.Inconclusive(
                $"Live publisher population run. Set {EnableVariable}=1 to run it; it is skipped by "
                + "default so the suite does not depend on a third party's uptime or send 82 runs' "
                + "worth of unasked traffic.");
            return;
        }

        // ---- The population is Appendix A's own, proven by its own digest before anything runs. ----
        //
        // Touching PackRoots forces EuAppendixASeedMap's static validation, which reconstructs the
        // canonical serialization from the embedded lines and refuses unless it hashes to
        // AppendixASha256. A population run whose seed list had drifted would otherwise measure the
        // wrong 82 things and report a number about them.
        // The literal 82 is Decision 84's own number, written here rather than read from
        // EuAppendixASeedMap.SeedCount: a population gate that took its population size from the
        // code it measures would follow that constant wherever it moved and still pass.
        Assert.HasCount(82, EuAppendixASeedMap.PackRoots, "the pack roots must be Appendix A's own 82.");
        Assert.HasCount(82, EuAppendixASeedMap.SeedsInCelexOrder, "the seed map must be Appendix A's own 82.");
        Assert.AreEqual(
            EuAppendixASeedMap.AppendixASha256,
            EuAppendixASeedMap.SeedMapRef.Sha256,
            "the seed map must bind under Appendix A's own digest.");

        // The file-name mapping must stay injective over the population, or two seeds silently
        // overwrite one another's retained results and the report counts one of them twice.
        Assert.HasCount(
            EuAppendixASeedMap.SeedsInCelexOrder.Count,
            EuAppendixASeedMap.SeedsInCelexOrder
                .Select(static seed => FileNameFor(seed.Celex))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray(),
            "two seeds map to one file name, so one would overwrite the other's retained result.");

        var subset = ParseSubset();
        var seeds = subset is null
            ? EuAppendixASeedMap.SeedsInCelexOrder.ToArray()
            : EuAppendixASeedMap.SeedsInCelexOrder.Where(seed => subset.Contains(seed.Celex)).ToArray();
        if (subset is not null)
        {
            Assert.HasCount(
                subset.Count,
                seeds,
                $"{SeedSubsetVariable} named a CELEX that is not an Appendix A seed.");
        }

        var root = Environment.GetEnvironmentVariable(RootVariable)
            ?? Path.Combine(Path.GetTempPath(), "lex-eu-population-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var seedDirectory = Path.Combine(root, "seeds");
        Directory.CreateDirectory(seedDirectory);
        Console.WriteLine($"POPULATION|root|{root}");

        var reportPath = Path.Combine(root, "population-report.json");
        var seedReports = new JsonArray();
        var faults = new List<string>();
        var violations = new List<string>();
        var reaching = new List<string>();
        var secondAttempts = new List<string>();
        var refused = new List<(string Celex, EuQueryExecutionRefusal Refusal)>();

        // ---- Pass one: every seed once, in Appendix A's own order. ----
        var populationStopwatch = Stopwatch.StartNew();
        var records = new List<SeedRecord>(seeds.Length);
        foreach (var (seed, ordinal) in seeds.Select(static (seed, ordinal) => (seed, ordinal)))
        {
            records.Add(await RunSeedAsync(seed, ordinal, seeds.Length, 1, null, root, seedDirectory, faults)
                .ConfigureAwait(false));
        }

        // ---- Pass two: the deferred second attempt, for publisher unavailability only. ----
        //
        // THE SECOND ATTEMPT IS DEFERRED TO THE END OF THE RUN RATHER THAN TAKEN INLINE, and that
        // is the whole point of it being a second pass. It was inline first, and the complete run
        // that measured it showed why that is worth almost nothing: seeds 32019L2121 and
        // 32019R1111 each refused on a terminal 502, retried immediately, and met 502 AGAIN. A
        // publisher's bad minute is not independent of itself, so an immediate retry re-samples
        // the same outage and a run loses seeds to a window it could simply have waited out. Every
        // other seed in the population now runs between the two attempts, which costs no extra
        // request and makes the second observation actually independent of the first.
        //
        // WHAT KEEPS THIS FROM BEING A LOOPHOLE, unchanged by the deferral. It fires ONLY when
        // every refused family, or the witness, carries a 5xx (IsPublisherUnavailable), so no
        // Lex-attributable refusal is ever retried. It is capped at exactly one extra attempt.
        // The first attempt's whole evidence index is retained beside the second, and the count of
        // seeds needing a second attempt is its own reported field, because if that number were
        // ever large it would be a finding about our own traffic rather than the publisher's.
        // BY POSITION, NEVER BY VALUE SEARCH. This selected records and then wrote each result
        // back with `records[records.IndexOf(record)]`. SeedRecord is a `record`, so IndexOf is an
        // equality search rather than an identity one, and the count of seeds that took a second
        // attempt came out lower than the number of second attempts actually run: run 8 wrote five
        // seed files carrying attempts=2 while the report claimed three. The population outcome was
        // unaffected -- every seed still reached -- but the retry counter is the one field that
        // exists so an inflated retry rate would be visible as a finding about our own traffic, and
        // a counter that undercounts is worse than no counter. Carrying the index removes the
        // search entirely.
        var secondAttemptIndices = await RunDeferredSecondAttemptsAsync(
                records,
                SelectDeferredIndices(records.ToArray()),
                (record, _) => RunSeedAsync(
                    record.Seed, record.Ordinal, seeds.Length, 2, record.Index,
                    root, seedDirectory, faults))
            .ConfigureAwait(false);

        populationStopwatch.Stop();

        // ---- The accounting, over each seed's FINAL record. ----
        foreach (var record in records)
        {
            if (record.Fault is null && record.Result is { } result)
            {
                CheckOneSeedsRunTruth(record.Seed.Celex, result, record.Reached, violations);
                if (record.Reached)
                {
                    reaching.Add(record.Seed.Celex);
                }
                else if (result.Refusal is { } refusal)
                {
                    refused.Add((record.Seed.Celex, refusal.Code));
                }
            }

            if (record.Attempts > 1)
            {
                secondAttempts.Add(record.Seed.Celex);
            }

            seedReports.Add(record.Report);
        }


        var report = new JsonObject
        {
            ["schema"] = "lex-eu-stage1-population-report/1",
            ["appendixASha256"] = EuAppendixASeedMap.AppendixASha256,
            ["appendixAByteCount"] = EuAppendixASeedMap.AppendixAByteCount,
            ["populationSeedCount"] = EuAppendixASeedMap.SeedCount,
            ["seedsAttempted"] = seeds.Length,
            ["isCompletePopulation"] = subset is null,
            ["seedsReachingManifestAndRecordSet"] = reaching.Count,
            ["seedsRefused"] = refused.Count,
            ["seedsFaulted"] = faults.Count,
            ["seedsNeedingASecondAttempt"] = secondAttempts.Count,
            ["seedsNeedingASecondAttemptByCelex"] = new JsonArray(
                secondAttempts.OrderBy(static celex => celex, StringComparer.Ordinal)
                    .Select(static celex => (JsonNode)JsonValue.Create(celex)!).ToArray()),
            ["elapsedMs"] = populationStopwatch.ElapsedMilliseconds,
            ["limitations"] = PopulationLimitations(),
            ["seeds"] = seedReports,
        };
        var reportBytes = Encoding.UTF8.GetBytes(
            report.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
        await File.WriteAllBytesAsync(reportPath, reportBytes).ConfigureAwait(false);
        Console.WriteLine(
            $"POPULATION|report|{reportPath}|bytes={reportBytes.Length}"
            + $"|sha256={Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(reportBytes))}");
        Console.WriteLine(
            $"POPULATION|summary|attempted={seeds.Length}|reached={reaching.Count}"
            + $"|refused={refused.Count}|faulted={faults.Count}"
            + $"|secondAttempts={secondAttempts.Count}"
            + $"|ms={populationStopwatch.ElapsedMilliseconds}");
        foreach (var (celex, refusal) in refused.OrderBy(static entry => entry.Celex, StringComparer.Ordinal))
        {
            Console.WriteLine($"POPULATION|refused|{celex}|{refusal}");
        }

        // ---- A seed that produced no typed disposition at all. ----
        Assert.IsEmpty(
            faults,
            "these seeds threw instead of producing a typed disposition, which is the silent "
                + $"absence this run exists to make impossible: {string.Join("; ", faults)}");

        // ---- The per-seed run-truth rules (S1-A05), asserted over every seed that ran. ----
        Assert.IsEmpty(
            violations,
            $"run-truth violations: {string.Join("; ", violations)}");

        if (subset is not null)
        {
            // A SAMPLE STATES WHAT IT CANNOT ESTABLISH. Decision 84 names the bounded canary
            // prerequisite evidence that "never closes the stage"; a green subset run here would
            // say the opposite of that, so a subset ends inconclusive by construction.
            Assert.Inconclusive(
                $"{SeedSubsetVariable} restricted this run to {seeds.Length} of "
                + "82 seeds. A subset is diagnostic and establishes "
                + $"nothing about the population. Report: {reportPath}");
            return;
        }

        // ---- The population accounting. ----
        Assert.AreEqual(
            82,
            seeds.Length,
            "Decision 84's complete population is 82 seeds and this run attempted a different number.");
        Assert.AreEqual(
            seeds.Length,
            reaching.Count + refused.Count,
            "every seed must be either reaching or typed-refused; these are the only two "
                + "dispositions and a seed in neither is unaccounted.");

        var actualRefusals = refused
            .OrderBy(static entry => entry.Celex, StringComparer.Ordinal)
            .Select(static entry => $"{entry.Celex}={entry.Refusal}")
            .ToArray();
        var expectedRefusals = ExpectedRefusals
            .OrderBy(static entry => entry.Celex, StringComparer.Ordinal)
            .Select(static entry => $"{entry.Celex}={entry.Refusal}")
            .ToArray();
        Assert.AreEqual(
            string.Join(",", expectedRefusals),
            string.Join(",", actualRefusals),
            "the set of seeds that do not reach the manifest, and the exact typed reason each "
                + $"gives, moved from what was measured and reviewed. Report: {reportPath}");

        Assert.AreEqual(
            ExpectedReaching,
            reaching.Count,
            $"{reaching.Count} of 82 seeds reached a written manifest "
                + $"and record set; the reviewed measurement was {ExpectedReaching}. Report: "
                + reportPath);
    }

    /// <summary>
    /// One seed's own complete run, through exactly the doors
    /// <see cref="EuStageOneAcquisitionCanary"/> drives, with its own custody sub-root so every
    /// retained byte is attributable to the seed that fetched it.
    /// </summary>
    private static async Task<EuQueryExecutionResult> RunOneSeedAsync(string celex, string custodyRoot)
    {
        Directory.CreateDirectory(custodyRoot);
        var store = new FileSystemCustodyStore(custodyRoot);
        var executor = new EuRepeatedEnumerationExecutor(store, TimeProvider.System);
        var adapter = new EuQueryExecutionAdapter(store, executor);

        var (censusPlan, censusPlanId) = EuAcquisitionTestFixture.BuildCensusPlan();
        var censusRequests = new[]
        {
            (
                Request: new EuCensusPartitionRunRequest(
                    censusPlan, censusPlanId, celex, EuAcquisitionTestFixture.BuildRendererSource(7100)),
                Witness: EuAcquisitionTestFixture.SourceWitness()),
        };

        var (objectFactsPlan, objectFactsPlanId) = EuAcquisitionTestFixture.BuildObjectFactsPlan();
        var objectFactsPolicy = new EuObjectFactsBatchPolicy(
            objectFactsPlan,
            objectFactsPlanId,
            EuAcquisitionTestFixture.BuildRendererSource(7200),
            EuAcquisitionTestFixture.SourceWitness());

        var completeEnumerationRef = new SourceArtifactRef(
            $"urn:uuid:{Guid.NewGuid():D}",
            Convert.ToHexStringLower(
                System.Security.Cryptography.SHA256.HashData(
                    Encoding.UTF8.GetBytes("eu-population-complete-enumeration-" + celex))));

        return await adapter.RunAsync(
            censusRequests,
            objectFactsPolicy,
            EuAcquisitionTestFixture.BuildRendererSource(7300),
            EuAcquisitionTestFixture.SourceWitness(),
            EuAcquisitionTestFixture.BuildRendererSource(7400),
            EuAcquisitionTestFixture.DocumentFetchSourceWitness(),
            new PopulationPermissiveEvidenceResolver(completeEnumerationRef),
            CancellationToken.None).ConfigureAwait(false);
    }

    /// <summary>
    /// S1-A05 run truth, per seed: the rules that must hold whatever the seed's disposition is.
    /// Collected rather than thrown so one seed's violation does not hide the other 81's results.
    /// </summary>
    private static void CheckOneSeedsRunTruth(
        string celex,
        EuQueryExecutionResult result,
        bool reachedManifest,
        List<string> violations)
    {
        // EXACTLY ONE DISPOSITION. A run that refused AND wrote a record set, or did neither, is
        // not readable either way.
        if (result.Refusal is not null && reachedManifest)
        {
            violations.Add($"{celex} both refused ({result.Refusal.Code}) and reached the record set.");
        }

        if (result.Refusal is null && !reachedManifest)
        {
            violations.Add(
                $"{celex} neither refused nor reached the record set: manifest receipt "
                + $"{(result.ScopeManifestReceipt is null ? "absent" : "present")}, record set "
                + $"{(result.CorpusRecordSet is null ? "absent" : "present")}, record set ref "
                + $"{(result.CorpusRecordSetRef is null ? "absent" : "present")}.");
        }

        if (result.Refusal is { } refusal && refusal.Code == EuQueryExecutionRefusal.None)
        {
            violations.Add($"{celex} carries a refusal whose code is None, which names nothing.");
        }

        if (!reachedManifest)
        {
            return;
        }

        // The manifest went to custody AND came back: the receipt digest names the stored bytes and
        // the canonical digest names the manifest's own content address, so equal values would mean
        // nothing here distinguishes a reopen from a write.
        if (string.IsNullOrWhiteSpace(result.ScopeManifestCanonicalSha256))
        {
            violations.Add($"{celex} reached the manifest and it carries no canonical digest.");
        }
        else if (string.Equals(
            result.ScopeManifestReceipt!.Reference.ContentSha256,
            result.ScopeManifestCanonicalSha256,
            StringComparison.Ordinal))
        {
            violations.Add(
                $"{celex}'s manifest receipt digest equals its canonical digest, so nothing "
                + "distinguishes a reopen from a write.");
        }

        if (string.IsNullOrWhiteSpace(result.CorpusRecordSetRef!.Sha256))
        {
            violations.Add($"{celex}'s record set is not addressable by its own canonical digest.");
        }

        // EVERY RECORD IS HELD WITH ITS EVIDENCE OR TYPED BY REASON. A record that is neither is a
        // silent absence a reader cannot tell from a body we simply failed to fetch.
        foreach (var record in result.CorpusRecordSet!.Set.Records)
        {
            var key = record.ObjectRef.CanonicalKey;
            if (record.Body.Kind == CorpusBodyRecordKind.Held)
            {
                if (record.Body.Floor is null)
                {
                    violations.Add($"{celex}/{key} is held and does not say under which custody class.");
                }

                continue;
            }

            if (record.Body.NotHeldReason is null && record.Body.PendingAcquisitionReason is null)
            {
                violations.Add($"{celex}/{key} holds no body and gives no reason.");
            }
        }

        // EVERY MINTED ROW CARRIES A ROW, selected or not, and a selected row carries an outcome.
        // An absent row is the worst form of the unobserved-versus-zero defect: there is not even a
        // field to be wrong in.
        if (result.MintedRowsByOrdinal is null || result.DocumentAcquisitionOutcomesByOrdinal is null)
        {
            violations.Add($"{celex} reached the record set without minted-row accounting.");
            return;
        }

        foreach (var (ordinal, accounting) in result.MintedRowsByOrdinal)
        {
            if (string.IsNullOrWhiteSpace(accounting.CanonicalKey))
            {
                violations.Add($"{celex} minted row {ordinal} does not name the object it is about.");
            }

            var hasOutcome = result.DocumentAcquisitionOutcomesByOrdinal.TryGetValue(
                ordinal, out var outcome);
            if (!accounting.SelectedByBodyAxis)
            {
                if (hasOutcome)
                {
                    violations.Add(
                        $"{celex}/{accounting.CanonicalKey} was not selected by the body axis and a "
                        + "fetch was attempted for it anyway.");
                }

                continue;
            }

            if (!hasOutcome)
            {
                violations.Add(
                    $"{celex}/{accounting.CanonicalKey} was selected by the body axis and carries no "
                    + "outcome at all.");
                continue;
            }

            if (outcome!.Receipt is null && outcome.Refusal is null)
            {
                violations.Add(
                    $"{celex}/{accounting.CanonicalKey} carries neither a receipt nor a reason.");
                continue;
            }

            if (outcome.Receipt is null)
            {
                continue;
            }

            // WHAT A HOLD MEANS, not only that bytes exist. The digest and the length say the bytes
            // exist; the class and the membership say what holding them means, and Decision 71's
            // own distinction lives in the second.
            if (string.IsNullOrWhiteSpace(outcome.Receipt.Reference.ContentSha256))
            {
                violations.Add($"{celex}/{accounting.CanonicalKey} is held and carries no digest.");
            }

            if (outcome.Receipt.Reference.ByteLength <= 0)
            {
                violations.Add($"{celex}/{accounting.CanonicalKey} is held and carries no byte length.");
            }

            if (outcome.Receipt.Reference.CustodyClass != CustodyClass.NightlyFloor90d)
            {
                violations.Add(
                    $"{celex}/{accounting.CanonicalKey} is held under "
                    + $"{outcome.Receipt.Reference.CustodyClass} rather than the class this route "
                    + "writes bodies under.");
            }

            if (CustodyMembershipClassifier.Classify(outcome.Receipt) != CustodyMembership.RetainedUnenforced)
            {
                violations.Add(
                    $"{celex}/{accounting.CanonicalKey} ran over a filesystem store, so Decision "
                    + "71's own distinction must read retained_unenforced.");
            }
        }
    }

    /// <summary>
    /// What this population run does NOT establish, carried IN the report rather than only in this
    /// file, so the sentences travel with the numbers a reader is about to quote.
    /// </summary>
    private static JsonArray PopulationLimitations() =>
        new()
        {
            new JsonObject
            {
                ["limitation"] = "reductionStepNotProven",
                ["why"] = "The scope-reduction evidence resolver is a TEST DOUBLE whose three "
                    + "admission questions answer on SHA-256 SHAPE ALONE and whose fourth compares "
                    + "against the ref this run handed its own constructor. No seed's reduction "
                    + "step is proven here. That is the canary's residue R0, an EU production "
                    + "resolver of the kind LuxembourgProductionScopeReductionEvidenceResolver "
                    + "already is, and this run inherits the gap unchanged rather than closing it.",
            },
            new JsonObject
            {
                ["limitation"] = "perSeedRunsNotOneWholePopulationManifest",
                ["why"] = "Each seed is its own adapter run with its own manifest and record set, "
                    + "because EuQueryExecutionAdapter.RunAsync refuses the WHOLE run with "
                    + "CensusFamilyNotProven if any one census family fails, which would make "
                    + "Decision 84's per-seed count unobtainable. This therefore establishes 82 "
                    + "per-seed routes and NOT one manifest over the union of all 82 closures. A "
                    + "Stage 1 exit receipt that needs the second needs a further run.",
            },
            new JsonObject
            {
                ["limitation"] = "custodyIsFilesystemUnenforced",
                ["why"] = "FileSystemCustodyStore publishes CustodyProtection.NotEnforced, so every "
                    + "artifact this run holds is RetainedUnenforced and says so. Production "
                    + "custody enforcement is #459's, not this run's, and a hold here is not "
                    + "evidence of an enforced retention floor.",
            },
            new JsonObject
            {
                ["limitation"] = "noCrossRunCensusForEightyOfTheSeeds",
                ["why"] = "EuStageOneAcquisitionCanary compares two seeds against an independently "
                    + "measured census of expressions and manifestation types. No such census "
                    + "exists for the other 80, so for those this run checks the RULES its records "
                    + "must satisfy and does not check the counts against an outside observation.",
            },
        };

    /// <summary>
    /// One seed's own report object. Extracted so a test can drive the case that broke a complete
    /// run, without a publisher.
    /// </summary>
    /// <remarks>
    /// THE DEEP CLONE, AND THE RUN THAT TAUGHT ME WHY IT IS NEEDED. A <c>JsonNode</c> may have one
    /// parent, and a deferred second attempt is handed the FIRST attempt's index, which is already
    /// a child of the first attempt's own report. Attaching it directly throws
    /// <see cref="InvalidOperationException"/>. That threw out of the deferred pass, past the catch
    /// that only ever covered the RUN, so a complete 82-seed population wrote all 82 measured seed
    /// files and then produced no report at all: the one step that was not fault-tolerant was the
    /// recording of a result rather than the getting of it.
    /// </remarks>
    /// <summary>
    /// Selects the publisher-unavailable seeds, reruns each exactly once at its own position, and
    /// proves the accounting before returning the indices it touched.
    /// </summary>
    /// <remarks>
    /// <para>
    /// EXTRACTED SO THE WRITE-BACK ITSELF IS TESTABLE. The defect this repair answers happened
    /// here, in the loop that writes each rerun result back, and the first version of the repair
    /// left exactly this loop unreachable from any test: the accounting helper was unit-driven
    /// while the code that calls it was reachable only through a two-hour publisher run. Deleting
    /// the call site stayed green, which the reviewer demonstrated rather than argued. Taking the
    /// runner as a delegate lets a test drive the real selection, the real positional write-back
    /// and the real assertion with no network at all.
    /// </para>
    /// <para>
    /// The runner receives the record and its index and returns the rerun record. Production hands
    /// it <c>RunSeedAsync</c> at attempt two; a test hands it a counter.
    /// </para>
    /// </remarks>
    internal static IReadOnlyList<int> SelectDeferredIndices(IReadOnlyList<SeedRecord> records)
    {
        ArgumentNullException.ThrowIfNull(records);
        return records
            .Select(static (record, index) => (Record: record, Index: index))
            .Where(static entry => entry.Record.Result is not null
                && entry.Record.Result.Refusal is not null
                && IsPublisherUnavailable(entry.Record.Result))
            .Select(static entry => entry.Index)
            .ToArray();
    }

    internal static async Task<IReadOnlyList<int>> RunDeferredSecondAttemptsAsync(
        IList<SeedRecord> records,
        IReadOnlyList<int> deferredIndices,
        Func<SeedRecord, int, Task<SeedRecord>> runner)
    {
        ArgumentNullException.ThrowIfNull(records);
        ArgumentNullException.ThrowIfNull(deferredIndices);
        ArgumentNullException.ThrowIfNull(runner);

        foreach (var index in deferredIndices)
        {
            var record = records[index];
            Console.WriteLine(
                $"POPULATION|deferredSecondAttempt|{record.Seed.Celex}"
                + $"|firstRefusal={record.Result?.Refusal?.Code}"
                + $"|status={(record.Result is null ? null : PublisherStatuses(record.Result))}");
            records[index] = await runner(record, index).ConfigureAwait(false);
        }

        AssertDeferredAccounting(records.ToArray(), deferredIndices);
        return deferredIndices;
    }

    /// <summary>
    /// Every deferred seed, and only a deferred seed, carries a second attempt once the write-back
    /// has run.
    /// </summary>
    /// <remarks>
    /// This exists because the retry counter was wrong in a run that passed. The population
    /// assertions all held -- every seed reached -- so nothing failed, and the undercount surfaced
    /// only when the report was reconciled by hand against the per-seed files. A number nobody can
    /// check is not evidence, and this is the check.
    /// </remarks>
    internal static void AssertDeferredAccounting(
        IReadOnlyList<SeedRecord> records, IReadOnlyList<int> deferredIndices)
    {
        var expected = deferredIndices.OrderBy(static index => index).ToArray();
        var actual = records
            .Select(static (record, index) => (record, index))
            .Where(static entry => entry.record.Attempts > 1)
            .Select(static entry => entry.index)
            .OrderBy(static index => index)
            .ToArray();
        if (!expected.SequenceEqual(actual))
        {
            throw new InvalidOperationException(
                "Deferred second-attempt accounting disagrees with the records it wrote back: "
                + $"deferred [{string.Join(',', expected)}] but attempts>1 at [{string.Join(',', actual)}].");
        }

        // AND THE CEILING IS EXACTLY ONE EXTRA ATTEMPT, not merely "more than one". Checking the
        // set of retried positions without checking how far each went let a third request read as
        // an ordinary second attempt, which is precisely the traffic this field exists to make
        // visible. A seed is either untried-again at 1 or retried once at 2; nothing else is a
        // state this harness may reach.
        var offCeiling = records
            .Select(static (record, index) => (record, index))
            .Where(entry => entry.record.Attempts != (expected.Contains(entry.index) ? 2 : 1))
            .Select(static entry => $"{entry.index}:{entry.record.Attempts}")
            .ToArray();
        if (offCeiling.Length > 0)
        {
            throw new InvalidOperationException(
                "Deferred second-attempt accounting exceeded its one-extra-attempt ceiling: "
                + $"deferred [{string.Join(',', expected)}] but attempts at [{string.Join(',', offCeiling)}] "
                + "are not exactly 2 when deferred and 1 otherwise.");
        }
    }

    internal static JsonObject BuildSeedReport(
        (string Celex, string WorkRoot) seed,
        int ordinal,
        long elapsedMs,
        bool reached,
        int attempt,
        string? fault,
        JsonObject index,
        JsonObject? firstAttemptIndex) =>
        new()
        {
            ["celex"] = seed.Celex,
            ["workRoot"] = seed.WorkRoot,
            ["ordinal"] = ordinal,
            ["elapsedMs"] = elapsedMs,
            ["reachedManifestAndRecordSet"] = reached,
            ["attempts"] = attempt,
            ["harnessFault"] = fault,
            ["index"] = index,
            ["firstAttemptIndex"] = firstAttemptIndex?.DeepClone(),
        };

    /// <summary>One seed's own final state within this population run.</summary>
    internal sealed record SeedRecord(
        (string Celex, string WorkRoot) Seed,
        int Ordinal,
        int Attempts,
        bool Reached,
        string? Fault,
        EuQueryExecutionResult? Result,
        JsonObject Index,
        JsonObject Report);

    /// <summary>
    /// Runs one seed once and records it, whichever pass asked. Extracted so the deferred second
    /// attempt goes through the identical path as the first rather than a near-copy of it.
    /// </summary>
    private static async Task<SeedRecord> RunSeedAsync(
        (string Celex, string WorkRoot) seed,
        int ordinal,
        int seedCount,
        int attempt,
        JsonObject? firstAttemptIndex,
        string root,
        string seedDirectory,
        List<string> faults)
    {
        var stopwatch = Stopwatch.StartNew();
        JsonObject index;
        EuQueryExecutionResult? result = null;
        string? fault = null;
        var custodyRoot = Path.Combine(
            root, "custody", FileNameFor(seed.Celex) + (attempt == 1 ? string.Empty : $"-attempt-{attempt}"));
        try
        {
            result = await RunOneSeedAsync(seed.Celex, custodyRoot).ConfigureAwait(false);
            index = EuStageOneAcquisitionCanary.BuildEvidenceIndex(result);
        }
        catch (Exception exception)
            when (exception is not OperationCanceledException)
        {
            // A THROW IS A RESULT, recorded per seed rather than ending the population. An escaped
            // exception means this seed produced NO typed disposition at all, which is the silent
            // absence this run exists to make impossible; it is collected and failed by name at the
            // end so every other seed is still measured.
            fault = $"{exception.GetType().FullName}: {exception.Message}";
            faults.Add($"{seed.Celex}: {fault}");
            index = new JsonObject { ["harnessFault"] = fault };
        }

        stopwatch.Stop();

        var reached = result is not null
            && result.Refusal is null
            && result.ScopeManifestReceipt is not null
            && result.CorpusRecordSet is not null
            && result.CorpusRecordSetRef is not null;

        var report = BuildSeedReport(
            seed, ordinal, stopwatch.ElapsedMilliseconds, reached, attempt, fault, index, firstAttemptIndex);

        // Written per seed, as the run goes, so a population that dies at seed sixty still leaves
        // fifty-nine measured results on disk rather than nothing.
        //
        // AND RECORDING A RESULT CANNOT ITSELF END THE POPULATION. Everything from here down is a
        // fault of this harness rather than an observation of the publisher, so it is recorded the
        // same way a run's own throw is: named against its seed, collected, and failed at the end.
        try
        {
            await File.WriteAllBytesAsync(
                Path.Combine(seedDirectory, FileNameFor(seed.Celex) + ".json"),
                Encoding.UTF8.GetBytes(report.ToJsonString(new JsonSerializerOptions { WriteIndented = true })))
                .ConfigureAwait(false);
        }
        catch (Exception exception)
            when (exception is not OperationCanceledException)
        {
            fault = $"recording {seed.Celex}: {exception.GetType().FullName}: {exception.Message}";
            faults.Add(fault);
        }

        Console.WriteLine(
            $"POPULATION|seed|{ordinal + 1}/{seedCount}|{seed.Celex}|attempt={attempt}|reached={reached}"
            + $"|refusal={result?.Refusal?.Code}|objects={result?.ObservedObjectCount}"
            + $"|expressions={result?.ObservedExpressionCount}|ms={stopwatch.ElapsedMilliseconds}"
            + $"|fault={fault}");

        return new SeedRecord(seed, ordinal, attempt, reached, fault, result, index, report);
    }

    /// <summary>
    /// Every refused family on this run carries a publisher 5xx, so the publisher was unavailable
    /// rather than this route being wrong.
    /// </summary>
    /// <remarks>
    /// Requires at least one refused family AND that EVERY refused family carries a 5xx. A run
    /// mixing a 5xx with any other cause is NOT publisher unavailability and is never retried: the
    /// other cause is exactly what this population exists to find, and letting one 503 launder it
    /// into a second attempt is the loophole this shape is written to exclude.
    /// </remarks>
    internal static bool IsPublisherUnavailable(EuQueryExecutionResult result)
    {
        // THE WITNESS IS THE OTHER PHASE THAT TALKS TO THE PUBLISHER, and the first gated run found
        // it: seed 12016E/TXT refused witness_traversal_refused on a StatusNotAdmitted while every
        // family proved, so there was no refused family for the clause below to inspect and the
        // seed was not retried. The witness refusal is read structurally here rather than parsed
        // out of the whole-run refusal's prose.
        if (result.WitnessTraversalRefusal is { } witness)
        {
            return witness.TerminalStatus is >= 500 and <= 599;
        }

        var refusedFamilies = result.FamilyOutcomes
            .Where(static outcome => outcome.ExecutorRefusal is not null)
            .ToArray();
        return refusedFamilies.Length > 0
            && refusedFamilies.All(static outcome =>
                outcome.ExecutorRefusal!.TerminalStatus is >= 500 and <= 599);
    }

    private static string PublisherStatuses(EuQueryExecutionResult result) =>
        string.Join(",", result.FamilyOutcomes
            .Select(static outcome => outcome.ExecutorRefusal?.TerminalStatus)
            .Where(static status => status is not null)
            .Select(static status => status!.Value.ToString(System.Globalization.CultureInfo.InvariantCulture)));

    /// <summary>
    /// One seed's own path-safe file name. Six of Appendix A's 82 are treaty seeds whose CELEX
    /// carries a solidus (<c>12012E/TXT</c>), which is a directory separator on every platform this
    /// runs on, so a run keyed on the raw CELEX writes into a directory that does not exist. The
    /// CELEX itself is never rewritten: it stays verbatim in every report body and on every console
    /// line, and only the file name is mapped.
    /// </summary>
    private static string FileNameFor(string celex) =>
        string.Concat(celex.Select(static character =>
            char.IsAsciiLetterOrDigit(character) || character is '.' or '_' or '-' ? character : '-'));

    private static HashSet<string>? ParseSubset()
    {
        var raw = Environment.GetEnvironmentVariable(SeedSubsetVariable);
        if (string.IsNullOrWhiteSpace(raw))
        {
            return null;
        }

        return new HashSet<string>(
            raw.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries),
            StringComparer.Ordinal);
    }

    /// <summary>
    /// The same permissive shape as the canary's own, declared here because that one is private to
    /// its own test class. A TEST DOUBLE, and the reason this run cannot claim the reduction step
    /// for any seed: the three admission questions answer on SHA-256 SHAPE ALONE and the fourth
    /// compares against the ref this constructor was handed.
    /// </summary>
    private sealed class PopulationPermissiveEvidenceResolver(SourceArtifactRef completeEnumerationRef)
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
