using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Lex.V3.Artifacts;
using Lex.V3.Contracts.Custody;
using Lex.V3.Contracts.Source.Absence;
using Lex.V3.Contracts.Source.Core;
using Lex.V3.Contracts.Source.Corpus;
using Lex.V3.Contracts.Source.Http;
using Lex.V3.Contracts.Source.Luxembourg;
using Lex.V3.Contracts.Source.Scope;
using Lex.V3.Ingest.Luxembourg;

namespace Lex.V3.Ingest.Tests;

[TestClass]
[DoNotParallelize]
public sealed class LuxembourgLiveAdapterCanary
{
    private const string Start = "http://data.legilux.public.lu/eli/etat/leg/loi/2017/03/14/a439/jo";
    private const string End = "http://data.legilux.public.lu/eli/etat/leg/loi/2017/03/14/a439/jp";
    private const string CivilState = "http://data.legilux.public.lu/eli/etat/leg/code/civil/20251226";
    private const string CivilOriginal = "http://data.legilux.public.lu/eli/etat/leg/loi/1804/03/21/n1/jo";
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    /// <summary>
    /// The whole-canary charged-request ceiling, DERIVED and not chosen: the sum of what each leg of the run declares it
    /// will cost, from the executor's own exact cost function, plus a stated allowance for retries.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The vocabulary partitions are opened over whole classes, so their sizes are the publisher's and no number can be
    /// read off the code alone. What the code does fix is the cost of a partition as a function of its count
    /// (<see cref="LuxembourgEnumerationBudget.RequestsForPartition"/>: robots, then a count and its pages for each of
    /// two passes), and it checks that cost against the budget it has left as soon as the count is read, so a class
    /// larger than declared is refused right after its count with the count in the refusal. This file therefore
    /// DECLARES how large a partition may be, derives the ceiling from that, and writes the derivation into the
    /// retained scope before the first request. A first run is a measurement of whether the declaration was right: it
    /// either passes, or refuses at the first partition that is larger and says how large.
    /// </para>
    /// <para>
    /// The ceiling is a refusal and never a truncation. <see cref="HardCap"/> is what no derivation may exceed, and a
    /// run whose declarations sum past it does not start.
    /// </para>
    /// </remarks>
    internal sealed record WireCeilingDerivation(
        long DeclaredRowsPerPartition,
        int RequestsPerPartition,
        int VocabularyPartitions,
        int AdapterPartitions,
        int DeclaredDocuments,
        int RequestsPerDocument,
        int RetryAllowance)
    {
        public int Total =>
            checked(((VocabularyPartitions + AdapterPartitions) * RequestsPerPartition)
                + (DeclaredDocuments * RequestsPerDocument) + RetryAllowance);
    }

    /// <summary>The largest count a partition may declare, which costs 8 requests: 996 is 997 less one, the largest count of two pages in the first pass.</summary>
    internal const long DeclaredRowsPerPartition = 996;

    /// <summary>Distinct vocabulary values (predicates, types, categories, licences) are the size of a vocabulary, not of the corpus.</summary>
    internal const int VocabularyPartitionCount = 4;

    /// <summary>Subjects, assertions and graph rows: three families for each declared range.</summary>
    internal const int FamiliesPerDeclaredRange = 3;

    /// <summary>Each document fetch reserves its own robots fetch and then sends its GET.</summary>
    internal const int RequestsPerDocument = 2;

    /// <summary>Every retry is a further reserved request, and the source profile allows four attempts per request.</summary>
    internal const int RetryAllowance = 8;

    /// <summary>
    /// What no derivation may exceed. A first measurement of a public body's endpoint should cost tens of requests, so
    /// 200 is well above anything the declarations here sum to and still small; raising the declarations past it is a
    /// decision to change this number in the open, not a side effect.
    /// </summary>
    internal const int HardCap = 200;

    internal static WireCeilingDerivation DeriveWireCeiling(int declaredRanges, int declaredDocuments) =>
        new(
            DeclaredRowsPerPartition,
            LuxembourgEnumerationBudget.RequestsForPartition(DeclaredRowsPerPartition),
            VocabularyPartitionCount,
            FamiliesPerDeclaredRange * declaredRanges,
            declaredDocuments,
            RequestsPerDocument,
            RetryAllowance);

    /// <summary>The refusal, or null: a derivation past the hard cap is not run.</summary>
    internal static string? RefusalFor(WireCeilingDerivation derivation) =>
        derivation.Total > HardCap
            ? $"the derived wire ceiling {derivation.Total} exceeds the hard cap {HardCap}; the declarations sum past what a canary may cost"
            : null;

    private const int DeclaredDocumentsForAnAct = 8;
    private const int DeclaredDocumentsForPlainXml = 2;

    [TestMethod]
    public void TheDerivedCeilingIsTheSumOfWhatEachLegDeclaresAndIsUnderTheHardCap()
    {
        // Eight requests a partition (a class of up to 996 rows: robots, a count and two pages, then a count and three
        // pages), seven partitions for one declared range (four of vocabulary, three of the adapter), then eight
        // documents at two requests each, then the retry allowance.
        var act = DeriveWireCeiling(declaredRanges: 1, DeclaredDocumentsForAnAct);
        Assert.AreEqual(8, act.RequestsPerPartition);
        Assert.AreEqual((7 * 8) + (8 * 2) + 8, act.Total);
        Assert.AreEqual(80, act.Total);

        // The plain-XML run declares two ranges (the Code civil state and its original) and two documents.
        var plain = DeriveWireCeiling(declaredRanges: 2, DeclaredDocumentsForPlainXml);
        Assert.AreEqual((10 * 8) + (2 * 2) + 8, plain.Total);
        Assert.AreEqual(92, plain.Total);

        Assert.IsNull(RefusalFor(act));
        Assert.IsNull(RefusalFor(plain));
    }

    [TestMethod]
    public void ADerivationPastTheHardCapIsRefusedBeforeAnythingIsSent()
    {
        // Twenty-five declared ranges sum past 200 whatever else is declared.
        var large = DeriveWireCeiling(declaredRanges: 25, DeclaredDocumentsForAnAct);
        Assert.IsGreaterThan(HardCap, large.Total);
        StringAssert.Contains(RefusalFor(large), "exceeds the hard cap 200");

        // Exactly at the cap is not past it, and one more is.
        var exact = new WireCeilingDerivation(996, 8, 4, 3, 0, 2, HardCap - 56);
        Assert.AreEqual(HardCap, exact.Total);
        Assert.IsNull(RefusalFor(exact));
        Assert.IsNotNull(RefusalFor(exact with { RetryAllowance = exact.RetryAllowance + 1 }));
    }

    /// <summary>
    /// The gated methods cannot run in a default build, so what they would do with the derivation is read from their
    /// source: one budget, built from the derived total, and the derivation retained in the scope before the first
    /// partition is run.
    /// </summary>
    [TestMethod]
    public void TheCanaryBuildsOneBudgetFromTheDerivationAndRetainsItBeforeItSendsAnything()
    {
        var source = File.ReadAllText(SourcePath());

        // The needles are built from two halves, so this file does not contain them whole and count itself.
        var budgetFromDerivation = "WireRequestBudget.Of" + "WireRequests(derivation.Total)";
        var anyBudget = "WireRequestBudget.Of" + "WireRequests(";
        var retained = "wireCeiling" + " = new";
        var refusalGate = "RefusalFor(derivation) is { } " + "refusal";
        var firstPartitionRun = "executor.Run" + "PartitionAsync(";

        Assert.AreEqual(
            1, CountOf(source, budgetFromDerivation),
            "one budget for the whole canary, built from the derived total and from nothing else");
        Assert.AreEqual(1, CountOf(source, anyBudget), "and no other budget is built here");
        Assert.AreEqual(1, CountOf(source, retained), "the derivation is retained in the scope");
        Assert.AreEqual(1, CountOf(source, refusalGate), "a derivation past the cap is refused");

        var refusal = source.IndexOf(refusalGate, StringComparison.Ordinal);
        var budget = source.IndexOf(budgetFromDerivation, StringComparison.Ordinal);
        var scope = source.IndexOf(retained, StringComparison.Ordinal);
        var firstPartition = source.IndexOf(firstPartitionRun, StringComparison.Ordinal);
        Assert.IsGreaterThan(0, firstPartition, "the canary runs its first partition");
        Assert.IsTrue(
            refusal < budget && budget < firstPartition && scope < firstPartition,
            "the refusal, the budget and the retained derivation all come before the first request is sent");
    }

    private static int CountOf(string text, string needle)
    {
        var count = 0;
        for (var at = text.IndexOf(needle, StringComparison.Ordinal); at >= 0;
             at = text.IndexOf(needle, at + needle.Length, StringComparison.Ordinal))
        {
            count++;
        }

        return count;
    }

    private static string SourcePath()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Lex.V3.slnx")))
        {
            directory = directory.Parent;
        }

        var root = directory?.FullName ?? throw new InvalidOperationException("Checkout root not found.");
        return Path.Combine(root, "tests", "Lex.V3.Ingest.Tests", "LuxembourgLiveAdapterCanary.cs");
    }

    [TestMethod]
    public async Task AnActRunsThroughThePublicAdapterWithObservedVocabularyAndSameRunRights()
    {
        if (Environment.GetEnvironmentVariable("LEX_LU_ADAPTER_CANARY") != "1")
            Assert.Inconclusive("Set LEX_LU_ADAPTER_CANARY=1 for publisher vocabulary and public-adapter acceptance.");

        await RunCanaryAsync(false);
    }

    [TestMethod]
    public async Task PlainXmlCodeCivilAndItsOriginalRunThroughThePublicAdapterWithSameRunRights()
    {
        if (Environment.GetEnvironmentVariable("LEX_LU_XML_ADAPTER_CANARY") != "1")
            Assert.Inconclusive("Set LEX_LU_XML_ADAPTER_CANARY=1 for the declared Code Civil and original-Act scope.");

        await RunCanaryAsync(true);
    }

    private static async Task RunCanaryAsync(bool plainXml)
    {
        var declaredRanges = plainXml
            ? new[] { (Name: "civil-state", Start: CivilState, End: "http://data.legilux.public.lu/eli/etat/leg/code/civil/20251227"),
                (Name: "civil-original", Start: CivilOriginal, End: CivilOriginal + "!") }
            : [(Name: "act-2017", Start, End)];

        // THE CEILING IS DERIVED FROM WHAT THIS RUN DECLARES, and a derivation past the hard cap is refused before a
        // range, a store or an executor is built, so nothing is sent for a canary whose declarations grew unnoticed.
        var derivation = DeriveWireCeiling(
            declaredRanges.Length, plainXml ? DeclaredDocumentsForPlainXml : DeclaredDocumentsForAnAct);
        if (RefusalFor(derivation) is { } refusal)
        {
            Assert.Fail(refusal);
            return;
        }

        // ONE INSTANCE FOR THE WHOLE CANARY: the four vocabulary partitions and the adapter run
        // that follows them all charge it, so the number bounds the run and not each of its legs.
        var budget = WireRequestBudget.OfWireRequests(derivation.Total);

        var checkout = CheckoutRoot();
        var root = Path.Combine(checkout, "artifacts", "lu-adapter-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var store = new FileSystemCustodyStore(root);
        var measured = new List<object>();
        var observed = new List<ObservedVocabulary>();
        object? provenance = null;
        object? finalResult = null;
        LuxembourgIriVocabularyValue[] missing = [];
        string status = "started";
        string? failure = null;
        try
        {
            // Capture before the first publisher request; dirty source and loaded binaries are
            // separately identified because a commit alone does not identify an uncommitted run.
            provenance = new
            {
                startedUtc = DateTimeOffset.UtcNow,
                head = Git(checkout, "rev-parse", "HEAD"),
                dirtyPaths = Git(checkout, "status", "--porcelain"),
                sourceFiles = Directory.EnumerateFiles(Path.Combine(checkout, "src"), "*.cs", SearchOption.AllDirectories)
                    .Where(path => !path.Contains(Path.DirectorySeparatorChar + "obj" + Path.DirectorySeparatorChar, StringComparison.Ordinal)
                        && !path.Contains(Path.DirectorySeparatorChar + "bin" + Path.DirectorySeparatorChar, StringComparison.Ordinal))
                    .Append(Path.Combine(checkout, "tests/Lex.V3.Ingest.Tests/LuxembourgLiveAdapterCanary.cs"))
                    .Order(StringComparer.Ordinal).Select(path => FileIdentity(checkout, path)).ToArray(),
                assemblies = new[] { typeof(LuxembourgLiveAdapterCanary).Assembly, typeof(LuxembourgQueryExecutionAdapter).Assembly,
                    typeof(LuxembourgQueryPlan).Assembly, typeof(FileSystemCustodyStore).Assembly }
                    .Distinct().Select(assembly => FileIdentity(checkout, assembly.Location)).ToArray(),
            };
            await HoldAsync(store, JsonSerializer.SerializeToUtf8Bytes(provenance, JsonOptions));
            var scope = await HoldAsync(store, JsonSerializer.SerializeToUtf8Bytes(new
            {
                schema = "lex-lu-declared-canary-scope/1",
                ranges = declaredRanges.Select(range => new { range.Name, range.Start, range.End }).ToArray(),
                vocabulary = "P/T/C whole range; O CC-BY range",
                wireCeiling = new
                {
                    derived = true,
                    total = derivation.Total,
                    hardCap = HardCap,
                    declaredRowsPerPartition = derivation.DeclaredRowsPerPartition,
                    requestsPerPartition = derivation.RequestsPerPartition,
                    vocabularyPartitions = derivation.VocabularyPartitions,
                    adapterPartitions = derivation.AdapterPartitions,
                    declaredDocuments = derivation.DeclaredDocuments,
                    requestsPerDocument = derivation.RequestsPerDocument,
                    retryAllowance = derivation.RetryAllowance,
                },
            }, JsonOptions));
            var plan = LuxembourgQueryPlan.CreateDefaultGraph(
                OfficialMachineQuerySourceProfiles.Resolve(OfficialMachineQuerySourceProfileId.LuxembourgSparql).ArtifactRef, scope);
            var planId = NewUrn();
            var queryRenderer = await RendererAsync(store, checkout, "src/Lex.V3.Contracts/Source/Luxembourg/LuxembourgQueryPlan.cs");
            var documentRenderer = await RendererAsync(store, checkout, "src/Lex.V3.Contracts/Source/Luxembourg/LuxembourgDocumentFetchPlan.cs");
            var executor = new LuxembourgRepeatedEnumerationExecutor(store, TimeProvider.System);

            foreach (var family in new[] { "P", "T", "C", "O" })
            {
                var range = family == "O"
                    ? Range("vocabulary-" + family.ToLowerInvariant(), "http://creativecommons.org/licenses/by/4.0/", "http://creativecommons.org/licenses/by/4.1/")
                    : Range("vocabulary-" + family.ToLowerInvariant(), "", "\uffff");
                var request = new LuxembourgPartitionRunRequest(plan, planId, family, range, queryRenderer);
                var witness = plan.BindCount(planId, NewUrn(), NewUrn(), family, LuxembourgQueryPass.Pass1, range, queryRenderer);
                var outcome = await executor.RunPartitionAsync(
                    request, witness.Request, budget, CancellationToken.None);
                measured.Add(new { family, outcome.ProductRequestCount, outcome.Refusal, outcome.Receipt?.Delivery,
                    retention = outcome.Receipt?.RetainedFloor.ToString() });
                Assert.IsNotNull(outcome.Receipt, $"Vocabulary {family} refused: {JsonSerializer.Serialize(outcome.Refusal)}");
                var proof = AbsenceFamilyEnumerationProof.TryCreate(range.PartitionId, outcome.Receipt.Delivery,
                    outcome.Receipt.RetainedFloor, out var proofRefusal);
                Assert.IsNotNull(proof, $"Vocabulary {family} proof refused: {proofRefusal}");
                var interpretation = plan.CreateDeliveryProfile(planId, family);
                var pages = new List<RepeatedEnumerationResolvedEvidence>();
                var glue = new RepeatedEnumerationDeliveryReopenGlue(store);
                foreach (var page in outcome.Receipt.Delivery.PagesA.Pages.OrderBy(page => page.Ordinal))
                    pages.Add(await glue.ReopenPageEvidenceAsync(page.Evidence, CancellationToken.None));
                var rows = VerifiedRepeatedEnumerationRows.TryOpen(proof, outcome.Receipt.Delivery, interpretation,
                    outcome.Receipt.Delivery.InterpretationProfileRef, outcome.Receipt.Delivery.CountA.HttpEvidenceRef,
                    pages, out var rowRefusal);
                Assert.IsNotNull(rows, $"Vocabulary {family} reopened rows refused: {rowRefusal}");
                var keyOrdinal = interpretation.ProjectionVariables.ToList().IndexOf("key_1");
                Assert.IsTrue(keyOrdinal >= 0);
                foreach (var row in rows)
                {
                    var value = row.Terms[keyOrdinal].Value;
                    Assert.IsNotNull(value, $"Unbound vocabulary {family} key_1");
                    observed.AddRange(Classify(family, value));
                }
            }

            var vocabulary = observed.Where(value => value.Kind is not null)
                .Select(value => new LuxembourgIriVocabularyValue(value.Kind!.Value, value.FullIri)).Distinct().ToArray();
            // Required values are expectations, never a source of observed values.
            missing = VerifiedLuxembourgSourceProfile.RequiredIriVocabulary.Except(vocabulary).ToArray();
            var vocabularyIndex = await HoldAsync(store, JsonSerializer.SerializeToUtf8Bytes(new
            {
                schema = "lex-lu-live-vocabulary-evidence/1", measured, observed, missing,
                limitation = "Observed publisher values only; no manufactured required vocabulary or production retention.",
            }, JsonOptions));
            var snapshot = new LuxembourgVocabularySnapshot(vocabularyIndex, vocabularyIndex, vocabulary, []);
            var profile = VerifiedLuxembourgSourceProfile.TryOpen(snapshot, out var profileFailure);
            Assert.IsEmpty(missing, $"Publisher vocabulary lacks required values; retained index {vocabularyIndex.Sha256}");
            Assert.IsNotNull(profile, $"Observed vocabulary refused: {profileFailure?.Code} {profileFailure?.Subject}");

            var families = new List<(LuxembourgPartitionRunRequest, BoundMachineRequest, LuxembourgPartitionChain?)>();
            foreach (var declared in declaredRanges)
            foreach (var family in new[] { "S", "A", "G" })
            {
                var range = Range(declared.Name + "-" + family.ToLowerInvariant(), declared.Start, declared.End);
                var request = new LuxembourgPartitionRunRequest(plan, planId, family, range, queryRenderer);
                var witness = plan.BindCount(planId, NewUrn(), NewUrn(), family, LuxembourgQueryPass.Pass1, range, queryRenderer);
                families.Add((request, witness.Request, null));
            }
            var adapter = new LuxembourgQueryExecutionAdapter(store, executor, profile);
            var result = plainXml
                ? await adapter.RunScopedAsync(families, declaredRanges.Select(range =>
                    new LuxembourgScopePartitionFamilies(range.Name + "-s", range.Name + "-a", range.Name + "-g")).ToArray(),
                    documentRenderer, budget, CancellationToken.None)
                : await adapter.RunAsync(families, "act-2017-g", "act-2017-s", "act-2017-a",
                    documentRenderer, budget, CancellationToken.None);
            finalResult = new { result.Refusal, result.Completion, result.FamilyOutcomes,
                result.ResourceObservationSubjects, result.ResourceObservationExclusions,
                result.ScopeManifestReceipt, result.ScopeManifestCanonicalSha256, result.CorpusRecordSetRef,
                result.DocumentAcquisitionOutcomesByOrdinal };
            Assert.IsNull(result.Refusal, JsonSerializer.Serialize(result.Refusal));
            Assert.AreEqual(LuxembourgQueryExecutionCompletion.AllFamiliesProven, result.Completion);
            Assert.IsNotNull(result.CorpusRecordSet);
            Assert.IsTrue(result.CorpusRecordSet.Set.Records.Any(record => record.Body.Kind == CorpusBodyRecordKind.Held),
                "The bounded work must have an actually held corpus body.");
            await ReplayRightsAndManifestAsync(store, profile, result,
                declaredRanges.Select(range => range.Name + "-a").ToArray(), plainXml ? CivilState + "/fr/xml" : null);
            status = "passed_bounded_adapter_canary";
        }
        catch (Exception exception)
        {
            status = "failed";
            failure = exception.ToString();
            throw;
        }
        finally
        {
            // Export also on publisher/refusal/assertion failures. The member index lists exact
            // on-disk bytes, even if any content-address verification itself failed.
            var members = new List<object>();
            foreach (var lane in new[] { "nightly-floor-90d", "legal-hold" })
            {
                var directory = Path.Combine(root, lane);
                if (!Directory.Exists(directory)) continue;
                foreach (var path in Directory.EnumerateFiles(directory).Order(StringComparer.Ordinal))
                {
                    var bytes = await File.ReadAllBytesAsync(path);
                    var digest = Convert.ToHexStringLower(SHA256.HashData(bytes));
                    members.Add(new { lane, claimedSha256 = Path.GetFileName(path), sha256 = digest,
                        byteLength = bytes.Length, matchesContentAddress = Path.GetFileName(path) == digest });
                }
            }
            var index = JsonSerializer.SerializeToUtf8Bytes(new
            {
                schema = "lex-lu-live-adapter-canary-evidence/1", status, failure, provenance, measured, observed, missing,
                finalResult, root, declaredRanges = declaredRanges.Select(range => new { range.Name, range.Start, range.End }).ToArray(),
                completedUtc = DateTimeOffset.UtcNow, members,
                limitations = "Bounded prerequisite only; not whole Luxembourg scope, Stage 1 acceptance, or production retention. FileSystemCustodyStore reports unenforced retention.",
            }, JsonOptions);
            var pathToIndex = Path.Combine(root, "evidence-index.json");
            await File.WriteAllBytesAsync(pathToIndex, index);
            Console.WriteLine($"LU adapter evidence: {pathToIndex}; sha256={Convert.ToHexStringLower(SHA256.HashData(index))}; bytes={index.Length}; status={status}");
            await store.CreateAsync(index, CustodyClass.NightlyFloor90d, CancellationToken.None);
        }
    }

    private static IEnumerable<ObservedVocabulary> Classify(string family, string value)
    {
        var required = VerifiedLuxembourgSourceProfile.RequiredIriVocabulary;
        if (family == "P")
        {
            var kinds = required.Where(item => item.FullIri == value && item.Kind is
                LuxembourgVocabularyKind.AssertionPredicate or LuxembourgVocabularyKind.RelationPredicate)
                .Select(item => item.Kind).Distinct().ToArray();
            return kinds.Length == 0 ? [new(family, null, value, "typed_quarantine_unruled_predicate")]
                : kinds.Select(kind => new ObservedVocabulary(family, kind, value, "governed_predicate_observed"));
        }
        LuxembourgVocabularyKind? category = family == "T" ? LuxembourgVocabularyKind.ResourceClass : null;
        if (family is "C" or "O")
        {
            (string Root, LuxembourgVocabularyKind Kind)[] roots =
            [
                ("http://data.legilux.public.lu/resource/authority/resource-type/", LuxembourgVocabularyKind.TypeDocument),
                ("http://data.legilux.public.lu/resource/authority/user-format/", LuxembourgVocabularyKind.UserFormat),
                ("http://data.legilux.public.lu/resource/authority/statut-version/", LuxembourgVocabularyKind.LegalValue),
                ("http://publications.europa.eu/resource/authority/language/", LuxembourgVocabularyKind.Language),
                ("http://data.legilux.public.lu/resource/authority/license/", LuxembourgVocabularyKind.Licence),
                ("http://creativecommons.org/licenses/", LuxembourgVocabularyKind.Licence),
            ];
            category = roots.Where(root => value.StartsWith(root.Root, StringComparison.Ordinal))
                .Select(root => (LuxembourgVocabularyKind?)root.Kind).SingleOrDefault();
        }
        return [new(family, category, value, category is { } kind && required.Any(item => item.Kind == kind && item.FullIri == value)
            ? "governed_value_observed" : "typed_quarantine_unruled_value")];
    }

    private static async Task ReplayRightsAndManifestAsync(ICustodyStore store,
        VerifiedLuxembourgSourceProfile profile, LuxembourgQueryExecutionResult result,
        IReadOnlyList<string> assertionFamilyKeys, string? expectedManifestation)
    {
        Assert.IsNotNull(result.ScopeManifestReceipt);
        var finalBytes = await CustodyRestore.ReadByDigestCheckedAsync(store,
            result.ScopeManifestReceipt.Reference.ContentSha256, CancellationToken.None);
        using var manifest = JsonDocument.Parse(finalBytes);
        var indexes = new Dictionary<string, (SourceArtifactRef Ref, JsonElement Json)>(StringComparer.Ordinal);
        foreach (var artifact in manifest.RootElement.GetProperty("ordered_evidence_artifacts").EnumerateArray())
        {
            var reference = new SourceArtifactRef(artifact.GetProperty("resource_id").GetString()!, artifact.GetProperty("sha256").GetString()!);
            var bytes = await CustodyRestore.ReadByDigestCheckedAsync(store, reference.Sha256, CancellationToken.None);
            using var document = JsonDocument.Parse(bytes);
            if (document.RootElement.TryGetProperty("schema", out var schema))
                indexes[schema.GetString()!] = (reference, document.RootElement.Clone());
        }
        var sparql = indexes["lex-lu-sparql-rights-evidence/1"];
        var inFile = indexes["lex-lu-in-file-rights-evidence/1"];
        var acquisitionBytes = await CustodyRestore.ReadByDigestCheckedAsync(store,
            inFile.Json.GetProperty("acquisitionManifestContentSha256").GetString()!, CancellationToken.None);
        var acquisitionRef = JsonSerializer.Deserialize<SourceArtifactRef>(inFile.Json.GetProperty("acquisitionManifestRef"))!;
        Assert.AreEqual(acquisitionRef.Sha256, ScopeManifestCanonicalWriter.ComputeManifestSha256(acquisitionBytes.Span));
        var run = profile.Snapshot.ObservationRef;
        Assert.AreEqual(run, JsonSerializer.Deserialize<SourceArtifactRef>(inFile.Json.GetProperty("runIdentity")),
            "The retained in-file index must identify this exact observation run.");
        var readings = inFile.Json.GetProperty("readings").EnumerateArray().ToArray();
        Assert.IsTrue(readings.Length > 0);
        var channelTwo = new List<LuxembourgRightsChannelObservation>();
        foreach (var reading in readings)
        {
            var bodyRef = JsonSerializer.Deserialize<SourceArtifactRef>(reading.GetProperty("BodyRef"))!;
            await CustodyRestore.ReadByDigestCheckedAsync(store, bodyRef.Sha256, CancellationToken.None);
            Assert.AreEqual((int)LuxembourgInFileRightsReadStatus.Observed, reading.GetProperty("Status").GetInt32());
            channelTwo.Add(new(reading.GetProperty("ManifestationIri").GetString()!, run, bodyRef,
                reading.GetProperty("LicenceIris").EnumerateArray().Select(value => value.GetString()!).ToArray()));
        }
        foreach (var acquisition in result.DocumentAcquisitionOutcomesByOrdinal!.Values.Where(value => value.Receipt is not null))
            Assert.IsTrue(channelTwo.Any(channel => channel.EvidenceRef.Sha256 == acquisition.Receipt!.Reference.ContentSha256),
                "Every held body must have this run's retained in-file channel reading.");
        var observations = sparql.Json.GetProperty("observations").EnumerateArray().Select(row =>
        {
            var objectRef = JsonSerializer.Deserialize<SourceObjectRef>(row.GetProperty("ObjectRef"))!;
            var assertions = JsonSerializer.Deserialize<LuxembourgObservedAssertion[]>(row.GetProperty("Assertions"))!;
            var channelOne = LuxembourgQueryExecutionAdapter.BuildSparqlRightsRows(assertions, run)
                .Select(value => new LuxembourgRightsChannelObservation(value.ManifestationIri, run, sparql.Ref, value.LicenceIris)).ToArray();
            return new LuxembourgResourceObservation(objectRef, run, assertions, [],
                new LuxembourgSparqlRightsChannelObservations(run, sparql.Ref, channelOne),
                new LuxembourgInFileRightsChannelObservations(run, inFile.Ref,
                    channelTwo.Where(value => assertions.Any(assertion => assertion.SubjectIri == value.ManifestationIri)).ToArray(), true));
        }).ToArray();
        var proofs = assertionFamilyKeys.Select(key => result.FamilyOutcomes.Single(outcome => outcome.FamilyKey == key).Proof).ToArray();
        Assert.IsTrue(proofs.All(proof => proof is not null));
        var replay = Assert.IsInstanceOfType<LuxembourgProfileResolution.Resolved>(profile.Resolve(
            LuxembourgProvenResourceObservations.RequireAllProven(proofs.Select(proof => proof!).ToArray(), observations)));
        if (expectedManifestation is not null)
        {
            Assert.IsTrue(channelTwo.Any(channel => channel.ManifestationIri == expectedManifestation),
                "The selected plain XML manifestation must have an actually held body and same-run in-file reading.");
            Assert.IsTrue(observations.SelectMany(observation => observation.Assertions).Any(assertion =>
                assertion.SubjectIri == expectedManifestation &&
                assertion.PredicateIri == "http://data.legilux.public.lu/resource/ontology/jolux#userFormat" &&
                assertion.ObjectKind == LuxembourgAssertionObjectKind.Iri &&
                assertion.ObjectIriOrLexical == "http://data.legilux.public.lu/resource/authority/user-format/xml"),
                "Plain XML must come from this run's proven publisher assertions, not its URL suffix.");
        }
        foreach (var channel in channelTwo)
        {
            var rights = replay.Resources.SelectMany(resource => resource.BodyJoin.Candidates)
                .Where(candidate => candidate.WemiCandidate.ManifestationIri == channel.ManifestationIri).ToArray();
            Assert.IsTrue(rights.Length > 0, "Retained in-file declaration must resolve onto this run's proven WEMI graph.");
            Assert.IsTrue(rights.All(candidate => candidate.RightsResolution.Disposition == LuxembourgRightsChannelDisposition.AgreedSameRunCcBy),
                $"Same-run dual-channel agreement not established for {channel.ManifestationIri}");
        }
        var resolver = await LuxembourgProductionScopeReductionEvidenceResolver.CreateAsync(store,
            profile.Snapshot.CompleteEnumerationRef, observations, replay.OrderedEvidenceArtifacts, CancellationToken.None);
        var rebuilt = profile.ReduceScope(replay, resolver, LuxembourgQueryExecutionAdapter.MintDocumentFetchAddresses(replay)
            .ToDictionary(pair => pair.Key, pair => pair.Value.ToScopeManifestFetchAddress()));
        using var stream = new MemoryStream();
        Assert.AreEqual(result.ScopeManifestCanonicalSha256, ScopeManifestCanonicalWriter.Write(stream, rebuilt));
        CollectionAssert.AreEqual(finalBytes.ToArray(), stream.ToArray(), "Final manifest must replay byte-for-byte from retained channels.");
        var finalRef = result.CorpusRecordSet!.Set.ManifestRef;
        VerifiedScopeManifest.ParseAndVerify(finalRef, finalBytes.Span, resolver);
    }

    private sealed record ObservedVocabulary(string Family, LuxembourgVocabularyKind? Kind, string FullIri, string Disposition);
    private static LuxembourgQueryPartitionRange Range(string name, string start, string end) => new(name,
        new LuxembourgQueryCursor(start, "", "", "", "", ""), new LuxembourgQueryCursor(end, "", "", "", "", ""));
    private static async Task<SourceArtifactRef> HoldAsync(ICustodyStore store, byte[] bytes)
    {
        var receipt = await store.CreateAsync(bytes, CustodyClass.NightlyFloor90d, CancellationToken.None);
        await CustodyRestore.ReadByDigestCheckedAsync(store, receipt.Reference.ContentSha256, CancellationToken.None);
        return new SourceArtifactRef(NewUrn(), receipt.Reference.ContentSha256);
    }
    private static async Task<MachineQueryRendererSource> RendererAsync(ICustodyStore store, string checkout, string path)
    {
        var bytes = await File.ReadAllBytesAsync(Path.Combine(checkout, path));
        return MachineQueryRendererSource.Open(await HoldAsync(store, bytes), bytes);
    }
    private static object FileIdentity(string checkout, string path)
    {
        var bytes = File.ReadAllBytes(path);
        return new { path = Path.GetRelativePath(checkout, path), sha256 = Convert.ToHexStringLower(SHA256.HashData(bytes)), byteLength = bytes.Length };
    }
    private static string CheckoutRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Lex.V3.slnx"))) directory = directory.Parent;
        return directory?.FullName ?? throw new InvalidOperationException("Checkout root not found.");
    }
    private static string Git(string checkout, params string[] arguments)
    {
        var info = new ProcessStartInfo("git") { WorkingDirectory = checkout, RedirectStandardOutput = true, UseShellExecute = false, CreateNoWindow = true };
        foreach (var argument in arguments) info.ArgumentList.Add(argument);
        using var process = Process.Start(info) ?? throw new InvalidOperationException("git did not start.");
        var result = process.StandardOutput.ReadToEnd().Trim();
        process.WaitForExit();
        if (process.ExitCode != 0) throw new InvalidOperationException("git failed.");
        return result;
    }
    private static string NewUrn() => $"urn:uuid:{Guid.NewGuid():D}";
}
