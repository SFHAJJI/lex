using System.Text;
using Lex.V3.Contracts;
using Lex.V3.Contracts.Custody;
using Lex.V3.Contracts.Derivation;
using Lex.V3.Contracts.Source.Europe;
using Lex.V3.Ingest.Europe;

namespace Lex.V3.Ingest.Tests;

/// <summary>
/// The governed production of the corrigendum tripwire set: one acquisition through the accepted
/// expression producer, one fold over the two deliveries it rebuilt, the set held in custody.
/// </summary>
/// <remarks>
/// Every test drives the classifying offline handler; no publisher traffic. The measured shape is
/// the product specification's: a corrigendum in Estonian, German, Hungarian and Italian only,
/// correcting a work whose reader in English has no served body in which to read it.
/// </remarks>
[TestClass]
public sealed class EuCorrigendumTripwireProducerTests
{
    private const string Work = "http://publications.europa.eu/resource/cellar/work-0001";
    private const string Corrigendum = "http://publications.europa.eu/resource/cellar/work-0001-r01";
    private const string LanguageBase = "http://publications.europa.eu/resource/authority/language/";
    private const string Estonian = LanguageBase + "EST";
    private const string German = LanguageBase + "DEU";
    private const string Hungarian = LanguageBase + "HUN";
    private const string Italian = LanguageBase + "ITA";
    private const string XsdDate = "http://www.w3.org/2001/XMLSchema#date";
    private const string CorrigendumDate = "2018-05-23";

    private static readonly string CorrectsIri =
        EuObjectFactsDiscoveryPlan.RelationIri(EuRelationFamily.Corrects);
    private static readonly string WorkDateIri =
        EuObjectFactsDiscoveryPlan.CdmIri(EuCdmPredicate.WorkDateDocument);

    /// <summary>
    /// The measured shape, end to end: four lines outside the served languages, dated as the
    /// publisher wrote it, the set and its lineage held under their own digests and reopened, and
    /// the set's derivation the very derivation the inner run retained.
    /// </summary>
    [TestMethod]
    public async Task TheGdprShapeIsProducedRetainedAndReopenable()
    {
        var (result, handler, store) = await RunAsync(FourLanguageRows(), CorrigendumRows());

        Assert.AreEqual(EuCorrigendumTripwireProductionRefusal.None, result.Refusal, result.Detail);
        Assert.IsTrue(result.Delivered);
        var set = result.TripwireSet!;
        Assert.HasCount(1, set.Tripwires);
        var tripwire = set.TripwireFor(Work)!;
        Assert.HasCount(4, tripwire.Lines);
        Assert.AreEqual(0, tripwire.WithinServedCount, "no served body carries this corrigendum.");
        Assert.AreEqual(4, tripwire.OutsideServedCount);
        Assert.AreEqual(4, tripwire.DatedCount);
        Assert.AreEqual(CorrigendumDate, tripwire.Lines[0].PublisherCorrigendumDate!.RawLexical);
        Assert.IsEmpty(set.UnresolvedGaps);

        // REAL DISPATCH, not a declared expectation: both families came off a socket this handler served.
        Assert.IsGreaterThan(0, handler.OccurrenceCountFor("X"), "the Expression-facts query must actually be sent.");
        Assert.IsGreaterThan(0, handler.OccurrenceCountFor("P"), "and so must the object-facts query.");
        Assert.IsGreaterThan(0, result.ProductRequestCount, "a run that sent requests must report them.");
        Assert.AreEqual(result.Expressions!.ProductRequestCount, result.ProductRequestCount);

        // THE SET'S DERIVATION IS THE RETAINED ONE. The same two deliveries derive the same bytes
        // (S3-A04); pinned as a premise rather than checked by the producer, which cannot differ.
        Assert.IsTrue(result.Expressions.Delivered);
        Assert.AreEqual(result.Expressions.Derivation!.DerivationSha256, set.DerivationSha256);
        Assert.AreEqual(result.Expressions.RetainedDerivation!.Reference.ContentSha256, set.DerivationSha256);

        // WHAT IS RETAINED IS THE SET'S OWN BYTES, reopened by the digest the store returned.
        var reopened = await CustodyRestore.ReadByDigestCheckedAsync(
            store, result.RetainedTripwire!.Reference.ContentSha256, CancellationToken.None);
        CollectionAssert.AreEqual(set.CanonicalBytes.ToArray(), reopened.ToArray());
        Assert.AreEqual(set.CanonicalSha256, result.RetainedTripwire.Reference.ContentSha256);
        var reopenedLineage = await CustodyRestore.ReadByDigestCheckedAsync(
            store, result.RetainedTripwireLineage!.Reference.ContentSha256, CancellationToken.None);
        CollectionAssert.AreEqual(set.LineageBytes.ToArray(), reopenedLineage.ToArray());
        Assert.AreEqual(set.LineageSha256, result.RetainedTripwireLineage.Reference.ContentSha256);
    }

    /// <summary>A missing object-facts request refuses before a single request is sent.</summary>
    [TestMethod]
    public async Task AMissingObjectFactsRequestRefusesBeforeAnyTraffic()
    {
        var handler = new EuAcquisitionTestFixture.ClassifyingHandler(Scripts(FourLanguageRows(), CorrigendumRows()));
        var producer = new EuCorrigendumTripwireProducer(
            new EuAcquisitionTestFixture.EuInMemoryCustodyStore(), new EuAcquisitionTestFixture.FixedTimeProvider(), handler);
        var budget = EuAcquisitionTestFixture.TestWireBudget();

        var result = await producer.RunAsync(
            Request(EuObjectFactsQuerySet.ExpressionFacts, budget), null,
            EuAcquisitionTestFixture.SourceWitness(), CancellationToken.None);

        Assert.AreEqual(EuCorrigendumTripwireProductionRefusal.ObjectFactsRequestRequired, result.Refusal);
        Assert.AreEqual(0, handler.SendCount, "refused before the session's robots request, not after traffic.");
        Assert.AreEqual(0, result.ProductRequestCount);
        Assert.IsNull(result.Expressions);
        Assert.IsNull(result.TripwireSet);
        Assert.IsFalse(result.Delivered);
    }

    /// <summary>The inner producer's own refusal travels by name, with its refused result carried.</summary>
    [TestMethod]
    public async Task AnExpressionProductionRefusalTravelsByName()
    {
        var handler = new EuAcquisitionTestFixture.ClassifyingHandler(Scripts(FourLanguageRows(), CorrigendumRows()));
        var producer = new EuCorrigendumTripwireProducer(
            new EuAcquisitionTestFixture.EuInMemoryCustodyStore(), new EuAcquisitionTestFixture.FixedTimeProvider(), handler);
        var budget = EuAcquisitionTestFixture.TestWireBudget();

        var result = await producer.RunAsync(
            Request(EuObjectFactsQuerySet.ManifestationFacts, budget),
            Request(EuObjectFactsQuerySet.ObjectFacts, budget),
            EuAcquisitionTestFixture.SourceWitness(), CancellationToken.None);

        Assert.AreEqual(EuCorrigendumTripwireProductionRefusal.ExpressionProductionRefused, result.Refusal);
        StringAssert.Contains(
            result.Detail,
            nameof(EuLanguageScopedExpressionProductionRefusal.ExpressionFactsRequestIsNotTheExpressionFamily),
            "the inner code travels.");
        Assert.IsNotNull(result.Expressions, "the inner refused result is carried, not dropped.");
        Assert.AreEqual(
            EuLanguageScopedExpressionProductionRefusal.ExpressionFactsRequestIsNotTheExpressionFamily,
            result.Expressions.Refusal);
        Assert.AreEqual(0, handler.SendCount);
        Assert.AreEqual(0, result.ProductRequestCount, "refused before traffic: nothing to report.");
        Assert.IsNull(result.TripwireSet);
    }

    /// <summary>The fold's own refusal travels by name, with the contract's detail and offending IRI.</summary>
    [TestMethod]
    public async Task ATripwireRefusalTravelsByName()
    {
        // An edge and the "corrects nothing" marker for one work, in ascending cursor order
        // ("iri" before "unbound" on key_3).
        var contradictory = new[]
        {
            EuAcquisitionTestFixture.ObjectFactRow(Corrigendum, CorrectsIri, Work),
            EuAcquisitionTestFixture.ObjectFactRow(Corrigendum, CorrectsIri, null),
        };

        var (result, handler, _) = await RunAsync(FourLanguageRows(), contradictory);

        Assert.AreEqual(EuCorrigendumTripwireProductionRefusal.TripwireRefused, result.Refusal);
        StringAssert.Contains(result.Detail, nameof(EuCorrigendumTripwireRefusal.CorrectsUnboundMarkerBesideEdges));
        StringAssert.Contains(result.Detail, Corrigendum, "the offending work is named.");
        Assert.IsNotNull(result.Expressions);
        Assert.IsTrue(result.Expressions.Delivered, "the inner run had already delivered; its receipts are not lost.");
        Assert.IsGreaterThan(0, handler.OccurrenceCountFor("P"));
        Assert.IsGreaterThan(0, result.ProductRequestCount, "the requests the inner run sent are reported on a refusal too.");
        Assert.AreEqual(result.Expressions.ProductRequestCount, result.ProductRequestCount);
        Assert.IsNull(result.TripwireSet);
        Assert.IsNull(result.RetainedTripwire);
    }

    /// <summary>A delivery stating that the work corrects nothing yields an empty set: a delivered fact, held.</summary>
    [TestMethod]
    public async Task AnEmptySetIsADeliveredFact()
    {
        var (result, _, store) = await RunAsync(
            FourLanguageRows(), [EuAcquisitionTestFixture.ObjectFactRow(Corrigendum, CorrectsIri, null)]);

        Assert.AreEqual(EuCorrigendumTripwireProductionRefusal.None, result.Refusal, result.Detail);
        var set = result.TripwireSet!;
        Assert.IsEmpty(set.Tripwires);
        Assert.IsEmpty(set.UnresolvedGaps, "the marker is a complete statement: a base act, not a gap.");
        var reopened = await CustodyRestore.ReadByDigestCheckedAsync(
            store, result.RetainedTripwire!.Reference.ContentSha256, CancellationToken.None);
        CollectionAssert.AreEqual(set.CanonicalBytes.ToArray(), reopened.ToArray());
    }

    /// <summary>
    /// A store that will not hold the set's canonical bytes refuses by name, and so does one that
    /// will not hold the lineage. The digests to fail are learned from a first, honest run - the
    /// canonical address is the same in every run (S3-A04), which is what makes this addressable.
    /// </summary>
    [TestMethod]
    public async Task AFailedHoldRefusesByName()
    {
        var (honest, _, _) = await RunAsync(FourLanguageRows(), CorrigendumRows());
        Assert.IsTrue(honest.Delivered, honest.Detail);
        var canonical = honest.TripwireSet!.CanonicalSha256;

        var (canonicalFailed, _, _) = await RunAsync(
            FourLanguageRows(), CorrigendumRows(),
            new EuAcquisitionTestFixture.EuInMemoryCustodyStore(failWriteDigest: (digest, _) => digest == canonical));
        Assert.AreEqual(EuCorrigendumTripwireProductionRefusal.TripwireNotRetained, canonicalFailed.Refusal);
        StringAssert.StartsWith(canonicalFailed.Detail, "canonical: ");
        StringAssert.Contains(canonicalFailed.Detail, "the scripted store refused to write this object.", "the store's own reason travels.");
        Assert.IsTrue(canonicalFailed.Expressions!.Delivered, "the inner run's own receipts are not lost.");
        Assert.AreEqual(canonicalFailed.Expressions.ProductRequestCount, canonicalFailed.ProductRequestCount);
        Assert.IsGreaterThan(0, canonicalFailed.ProductRequestCount);
        Assert.IsNull(canonicalFailed.RetainedTripwire);

        // The lineage differs per run, so it cannot be named ahead; it is the LAST write of a run,
        // and a counting store fails exactly that one - the premise being the honest run's count.
        var counting = new CountingCustodyStore(new EuAcquisitionTestFixture.EuInMemoryCustodyStore());
        var (recounted, _, _) = await RunAsync(FourLanguageRows(), CorrigendumRows(), counting);
        Assert.IsTrue(recounted.Delivered, recounted.Detail);
        var failing = new CountingCustodyStore(
            new EuAcquisitionTestFixture.EuInMemoryCustodyStore(), failCreateOrdinal: counting.CreateCount);
        var (lineageFailed, _, _) = await RunAsync(FourLanguageRows(), CorrigendumRows(), failing);
        Assert.AreEqual(EuCorrigendumTripwireProductionRefusal.TripwireNotRetained, lineageFailed.Refusal);
        StringAssert.StartsWith(lineageFailed.Detail, "lineage: ");
        StringAssert.Contains(lineageFailed.Detail, "the counting store refused this write.", "the store's own reason travels.");
        Assert.IsNull(lineageFailed.RetainedTripwireLineage);
        Assert.IsTrue(lineageFailed.Expressions!.Delivered);
        Assert.AreEqual(lineageFailed.Expressions.ProductRequestCount, lineageFailed.ProductRequestCount);
        Assert.IsGreaterThan(0, lineageFailed.ProductRequestCount);
    }

    /// <summary>Two independent runs over the same rows hold one tripwire address and two lineages.</summary>
    [TestMethod]
    public async Task TwoRunsAgreeOnTheTripwireAddressAndDifferOnLineage()
    {
        var (first, _, _) = await RunAsync(FourLanguageRows(), CorrigendumRows());
        var (second, _, _) = await RunAsync(FourLanguageRows(), CorrigendumRows());

        Assert.IsTrue(first.Delivered, first.Detail);
        Assert.IsTrue(second.Delivered, second.Detail);
        Assert.AreNotEqual(
            first.Expressions!.Derivation!.EpisodeSha256, second.Expressions!.Derivation!.EpisodeSha256,
            "the premise: two acquisition runs.");
        Assert.AreEqual(first.TripwireSet!.CanonicalSha256, second.TripwireSet!.CanonicalSha256);
        Assert.AreEqual(first.RetainedTripwire!.Reference.ContentSha256, second.RetainedTripwire!.Reference.ContentSha256);
        Assert.AreNotEqual(first.TripwireSet.LineageSha256, second.TripwireSet.LineageSha256);
    }

    /// <summary>
    /// THE EXCLUSION, STRUCTURAL HALF, for this producer too: it reaches neither the legacy fold nor
    /// the snapshot pairing the tripwire contract refused before freeze.
    /// </summary>
    [TestMethod]
    public void TheProductionPathReachesNeitherTheLegacyFoldNorTheSnapshotPairing()
    {
        var code = StripComments(ReadSource("src/Lex.V3.Ingest/Europe/EuCorrigendumTripwireProducer.cs"));
        foreach (var forbidden in new[]
        {
            "EuCellarObjectDecode", "EuLanguageExpressionObservation", "EuCellarObjectSnapshot",
            "EuRelationFamilyObservation", "EuRelationEdgeObservation", "EuRepeatedEnumerationExecutor",
        })
        {
            Assert.IsFalse(code.Contains(forbidden, StringComparison.Ordinal), $"the producer must not reach {forbidden}.");
        }
    }

    /// <summary>
    /// The internal factories guard what the run has already proved; the lens noted no test reached
    /// those guards, so they are reached here rather than left as untested premises.
    /// </summary>
    [TestMethod]
    public async Task TheFactoriesRefuseWhatARunNeverHandsThem()
    {
        var (honest, _, _) = await RunAsync(FourLanguageRows(), CorrigendumRows());
        Assert.IsTrue(honest.Delivered, honest.Detail);
        var expressions = honest.Expressions!;
        var set = honest.TripwireSet!;
        var receipt = honest.RetainedTripwire!;

        Assert.ThrowsExactly<ArgumentNullException>(() => EuCorrigendumTripwireProductionResult.Success(null!, set, receipt, receipt, 1));
        Assert.ThrowsExactly<ArgumentNullException>(() => EuCorrigendumTripwireProductionResult.Success(expressions, null!, receipt, receipt, 1));
        Assert.ThrowsExactly<ArgumentNullException>(() => EuCorrigendumTripwireProductionResult.Success(expressions, set, null!, receipt, 1));
        Assert.ThrowsExactly<ArgumentNullException>(() => EuCorrigendumTripwireProductionResult.Success(expressions, set, receipt, null!, 1));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => EuCorrigendumTripwireProductionResult.Refused(
            EuCorrigendumTripwireProductionRefusal.None, "not a refusal", null, 0));
    }

    /// <summary>
    /// The deliveries the fold takes leave the inner run as outputs; no member of either producer
    /// or result accepts a proof-bound delivery as an input. The lens's concern on an earlier head:
    /// a result factory taking deliveries would have been the pairing door one level up.
    /// </summary>
    [TestMethod]
    public void NoProducerSurfaceAcceptsADeliveryAsAnInput()
    {
        const System.Reflection.BindingFlags Everything =
            System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic
            | System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Static
            | System.Reflection.BindingFlags.DeclaredOnly;
        // #418 slice 6 widened this pin twice, because the join added both a nested pairing type and
        // a parameter that carries deliveries inside a tuple. NESTED TYPES ARE SCANNED, and a
        // parameter is refused if a delivery appears anywhere in its type - by ref, by array, or as
        // a generic argument of a tuple or any other container. The letter and the intent now agree.
        foreach (var type in new[]
        {
            typeof(EuCorrigendumTripwireProducer), typeof(EuCorrigendumTripwireProductionResult),
            typeof(EuLanguageScopedExpressionProducer), typeof(EuLanguageScopedExpressionProductionResult),
        }.SelectMany(type => type.GetNestedTypes(Everything).Prepend(type)))
        {
            var members = type.GetMethods(Everything).Cast<System.Reflection.MethodBase>()
                .Concat(type.GetConstructors(Everything));
            foreach (var member in members)
            {
                foreach (var parameter in member.GetParameters())
                {
                    Assert.IsFalse(
                        MentionsDelivery(parameter.ParameterType),
                        $"{type.Name}.{member.Name} accepts a proof-bound delivery as the input '{parameter.Name}'.");
                }
            }
        }

        static bool MentionsDelivery(Type type) =>
            type == typeof(EuProofBoundDelivery)
            || (type.HasElementType && MentionsDelivery(type.GetElementType()!))
            || type.GetGenericArguments().Any(MentionsDelivery);
    }

    [TestMethod]
    public async Task NullsAreCallerContractViolations()
    {
        var producer = new EuCorrigendumTripwireProducer(
            new EuAcquisitionTestFixture.EuInMemoryCustodyStore(), new EuAcquisitionTestFixture.FixedTimeProvider());
        var budget = EuAcquisitionTestFixture.TestWireBudget();

        Assert.ThrowsExactly<ArgumentNullException>(() => new EuCorrigendumTripwireProducer(null!, new EuAcquisitionTestFixture.FixedTimeProvider()));
        Assert.ThrowsExactly<ArgumentNullException>(() => new EuCorrigendumTripwireProducer(new EuAcquisitionTestFixture.EuInMemoryCustodyStore(), null!));
        await Assert.ThrowsExactlyAsync<ArgumentNullException>(() => producer.RunAsync(
            null!, Request(EuObjectFactsQuerySet.ObjectFacts, budget), EuAcquisitionTestFixture.SourceWitness(), CancellationToken.None));
        await Assert.ThrowsExactlyAsync<ArgumentNullException>(() => producer.RunAsync(
            Request(EuObjectFactsQuerySet.ExpressionFacts, budget), Request(EuObjectFactsQuerySet.ObjectFacts, budget), null!, CancellationToken.None));
    }

    // ---- Fixtures. ----

    /// <summary>Counts creates and, when asked, refuses exactly the Nth one as a genuine custody failure.</summary>
    private sealed class CountingCustodyStore(ICustodyStore inner, int? failCreateOrdinal = null) : ICustodyStore
    {
        private int _createCount;

        public int CreateCount => Volatile.Read(ref _createCount);

        public Task<DurableBlobWriteReceipt> CreateAsync(
            ReadOnlyMemory<byte> bytes, CustodyClass custodyClass, CancellationToken cancellationToken)
        {
            var ordinal = Interlocked.Increment(ref _createCount);
            return ordinal == failCreateOrdinal
                ? throw new CustodyRequiredException("the counting store refused this write.")
                : inner.CreateAsync(bytes, custodyClass, cancellationToken);
        }

        public Task<ReadOnlyMemory<byte>> ReadAsync(DurableBlobRef reference, CancellationToken cancellationToken) =>
            inner.ReadAsync(reference, cancellationToken);

        public Task<ReadOnlyMemory<byte>> ReadByDigestAsync(string contentSha256, CancellationToken cancellationToken) =>
            inner.ReadByDigestAsync(contentSha256, cancellationToken);
    }

    private static async Task<(EuCorrigendumTripwireProductionResult Result, EuAcquisitionTestFixture.ClassifyingHandler Handler, ICustodyStore Store)>
        RunAsync(
            IReadOnlyList<string> expressionRows,
            IReadOnlyList<string> objectRows,
            ICustodyStore? store = null)
    {
        var handler = new EuAcquisitionTestFixture.ClassifyingHandler(Scripts(expressionRows, objectRows));
        store ??= new EuAcquisitionTestFixture.EuInMemoryCustodyStore();
        var producer = new EuCorrigendumTripwireProducer(store, new EuAcquisitionTestFixture.FixedTimeProvider(), handler);
        var budget = EuAcquisitionTestFixture.TestWireBudget();
        var result = await producer.RunAsync(
            Request(EuObjectFactsQuerySet.ExpressionFacts, budget),
            Request(EuObjectFactsQuerySet.ObjectFacts, budget),
            EuAcquisitionTestFixture.SourceWitness(),
            CancellationToken.None);
        return (result, handler, store);
    }

    private static Dictionary<string, EuAcquisitionTestFixture.FamilyScript> Scripts(
        IReadOnlyList<string> expressionRows, IReadOnlyList<string> objectRows) =>
        new(StringComparer.Ordinal)
        {
            ["X"] = EuAcquisitionTestFixture.ScriptFor(
                "X", expressionRows.Count, expressionRows, EuAcquisitionTestFixture.ExpressionFactsProjection),
            ["P"] = EuAcquisitionTestFixture.ScriptFor(
                "P", objectRows.Count, objectRows, EuAcquisitionTestFixture.ObjectFactsProjection),
        };

    /// <summary>Both requests name the corrigendum work, under one shared wire budget.</summary>
    private static EuObjectFactsPartitionRunRequest Request(EuObjectFactsQuerySet set, WireRequestBudget budget)
    {
        var (plan, planId) = EuAcquisitionTestFixture.BuildObjectFactsPlan();
        return new EuObjectFactsPartitionRunRequest(
            plan, planId, set, [Corrigendum], EuAcquisitionTestFixture.BuildRendererSource(4180), budget);
    }

    /// <summary>The corrigendum's object-facts rows, in ascending cursor order: the edge, then the date.</summary>
    private static IReadOnlyList<string> CorrigendumRows() =>
    [
        EuAcquisitionTestFixture.ObjectFactRow(Corrigendum, CorrectsIri, Work),
        EuAcquisitionTestFixture.ObjectFactLiteralRow(Corrigendum, WorkDateIri, CorrigendumDate, XsdDate),
    ];

    private static IReadOnlyList<string> FourLanguageRows() =>
    [
        .. ExpressionRows("expr-r01-deu", German),
        .. ExpressionRows("expr-r01-est", Estonian),
        .. ExpressionRows("expr-r01-hun", Hungarian),
        .. ExpressionRows("expr-r01-ita", Italian),
    ];

    private static IReadOnlyList<string> ExpressionRows(string expressionKey, string languageAuthority)
    {
        var expression = "http://publications.europa.eu/resource/cellar/" + expressionKey;
        return
        [
            EuAcquisitionTestFixture.ExpressionFactRow(Corrigendum, expression),
            EuAcquisitionTestFixture.ExpressionLanguageRow(Corrigendum, expression, languageAuthority),
        ];
    }

    private static string StripComments(string source)
    {
        var withoutBlocks = System.Text.RegularExpressions.Regex.Replace(
            source, @"/\*.*?\*/", string.Empty, System.Text.RegularExpressions.RegexOptions.Singleline);
        var withoutLines = System.Text.RegularExpressions.Regex.Replace(withoutBlocks, @"//[^\n]*", string.Empty);
        return System.Text.RegularExpressions.Regex.Replace(withoutLines, "\"[^\"\\n]*\"", "\"\"");
    }

    private static string ReadSource(string repositoryRelativePath)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Lex.V3.slnx")))
        {
            directory = directory.Parent;
        }

        var root = directory?.FullName ?? throw new InvalidOperationException("Checkout root not found.");
        return File.ReadAllText(
            Path.Combine(root, repositoryRelativePath.Replace('/', Path.DirectorySeparatorChar)), Encoding.UTF8);
    }
}
