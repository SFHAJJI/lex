using System.Text;
using Lex.V3.Contracts.Custody;
using Lex.V3.Contracts.Source.Core;
using Lex.V3.Contracts.Source.Europe;
using Lex.V3.Ingest.Europe;

namespace Lex.V3.Ingest.Tests;

/// <summary>
/// #418's third slice: the governed production path from retained publisher rows into the
/// language-scoped expression decoder, and the exclusion the completion boundary requires.
/// </summary>
[TestClass]
public sealed class EuLanguageScopedExpressionProducerTests
{
    private const string Work = "http://publications.europa.eu/resource/cellar/work-0001";
    private const string OtherWork = "http://publications.europa.eu/resource/cellar/work-other";
    private const string EnglishExpression = "http://publications.europa.eu/resource/cellar/expr-eng";
    private const string FrenchExpression = "http://publications.europa.eu/resource/cellar/expr-fra";
    private const string BulgarianExpression = "http://publications.europa.eu/resource/cellar/expr-bul";

    /// <summary>
    /// A second English Expression of the same Work. #418's completion criterion is that multiple
    /// SAME-language expressions coexist, and every other fixture here gives each Expression a
    /// language of its own, so nothing yet distinguishes a run that carries both from one that keys
    /// its expressions by language and silently keeps the last.
    /// </summary>
    private const string SecondEnglishExpression =
        "http://publications.europa.eu/resource/cellar/expr-eng-2";

    private const string XsdDate = "http://www.w3.org/2001/XMLSchema#date";

    private const string WorkDate = "2018-05-23";

    private const string EnglishAuthority =
        "http://publications.europa.eu/resource/authority/language/ENG";
    private const string FrenchAuthority =
        "http://publications.europa.eu/resource/authority/language/FRA";

    /// <summary>
    /// Bulgarian. Chosen because <c>EuCellarObjectDecode</c> reads exactly two language authority
    /// IRIs and this is not one of them, which is what makes the exclusion below demonstrable rather
    /// than asserted.
    /// </summary>
    private const string BulgarianAuthority =
        "http://publications.europa.eu/resource/authority/language/BUL";

    /// <summary>
    /// The path exists at all: a real family X run reaches the decoder and its result is held.
    /// </summary>
    /// <remarks>
    /// Before this slice <c>LanguageScopedExpression</c> appeared in no file under
    /// <c>src/Lex.V3.Ingest/</c>, so nothing in production could reach the decoder and #418's
    /// boundary named that as the gap. This test is the one that would stop compiling if the path
    /// were removed again.
    /// </remarks>
    [TestMethod]
    public async Task AFamilyXRunReachesTheDecoderAndTheDerivationIsRetained()
    {
        var (result, handler, _) = await RunAsync(EnglishOnlyRows());

        Assert.AreEqual(
            EuLanguageScopedExpressionProductionRefusal.None, result.Refusal,
            $"the family must run end to end: {result.Refusal} {result.Detail}");
        Assert.IsNotNull(result.Derivation);
        Assert.IsNotNull(result.RetainedDerivation);
        Assert.HasCount(1, result.Derivation!.Expressions);
        Assert.AreEqual(EnglishAuthority, result.Derivation.Expressions[0].OfficialLanguage);

        // REAL DISPATCH, not a declared expectation: the rows came off a socket this handler served.
        Assert.IsGreaterThan(
            0, handler.OccurrenceCountFor("X"),
            "the Expression-facts query must actually be sent, not assumed.");
        Assert.IsGreaterThan(
            0, result.ProductRequestCount,
            "a run that sent requests must report them.");
    }

    /// <summary>
    /// THE EXCLUSION, BEHAVIOURAL HALF. Every language the publisher stated survives, including one
    /// the legacy single-language fold has no way to name.
    /// </summary>
    /// <remarks>
    /// <para>
    /// #418's completion boundary requires proof that <c>EuCellarObjectDecode</c>'s
    /// <c>BuildLanguageObservation</c> "cannot silently substitute for, overwrite, or suppress the
    /// full expression set". This is the half that shows what the full set contains: one work, three
    /// stated Expressions, three distinct language authority IRIs, all three retained.
    /// </para>
    /// <para>
    /// The fold returns ONE <c>EuLanguageExpressionObservation</c> per object and would report this
    /// work as English -- not because English is more correct, but because English is the first
    /// branch it tests. The Bulgarian Expression is the interesting one: the fold has no branch for
    /// it and no enum member to put it in, so it would be reported as the English fallback. A
    /// substitution here would not merely lose two expressions; it would state something false about
    /// the third.
    /// </para>
    /// </remarks>
    [TestMethod]
    public async Task EveryStatedLanguageSurvivesIncludingOneTheLegacyFoldCannotName()
    {
        var (result, _, _) = await RunAsync(ThreeLanguageRows());

        Assert.AreEqual(
            EuLanguageScopedExpressionProductionRefusal.None, result.Refusal,
            $"{result.Refusal} {result.Detail}");

        var languages = result.Derivation!.Expressions
            .Select(expression => expression.OfficialLanguage)
            .OrderBy(static value => value, StringComparer.Ordinal)
            .ToArray();

        CollectionAssert.AreEqual(
            new[] { BulgarianAuthority, EnglishAuthority, FrenchAuthority },
            languages,
            "all three stated languages must survive, carried as the publisher's own authority IRIs.");

        // AND THEY ARE THREE EXPRESSIONS OF ONE WORK, which is the coexistence the legacy fold
        // cannot represent at all: its return type is one observation, not a list.
        Assert.HasCount(3, result.Derivation.Expressions);
        Assert.AreEqual(
            1,
            result.Derivation.Expressions
                .Select(expression => expression.Identity.PublisherWorkId)
                .Distinct(StringComparer.Ordinal)
                .Count(),
            "all three are Expressions of the same work.");
    }

    /// <summary>
    /// THE EXCLUSION, REPRESENTATIONAL HALF. The legacy fold recognises exactly two language
    /// authority IRIs, so it could not have carried the third expression above.
    /// </summary>
    /// <remarks>
    /// Read out of the reader's own source rather than asserted in prose, because the claim being
    /// made is about what that reader can represent. If someone later teaches the fold a third
    /// language, this fails and the exclusion above has to be restated rather than silently becoming
    /// weaker. That is the point of pinning it.
    /// </remarks>
    [TestMethod]
    public void TheLegacyFoldRecognisesExactlyTwoLanguageAuthorities()
    {
        var source = ReadSource("src/Lex.V3.Contracts/Source/Europe/EuCellarObjectDecode.cs");

        var authorities = System.Text.RegularExpressions.Regex
            .Matches(source, @"http://publications\.europa\.eu/resource/authority/language/[A-Z]+")
            .Select(match => match.Value)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(static value => value, StringComparer.Ordinal)
            .ToArray();

        CollectionAssert.AreEqual(
            new[] { EnglishAuthority, FrenchAuthority },
            authorities,
            "the legacy fold reads exactly ENG and FRA, so a Bulgarian Expression has nowhere to go "
            + "in it and the derivation above carries something it cannot.");
    }

    /// <summary>
    /// THE EXCLUSION, STRUCTURAL HALF. The production path does not reach the legacy fold, so the
    /// fold cannot overwrite or suppress anything on it.
    /// </summary>
    /// <remarks>
    /// The two behavioural tests above show the two readers disagree about the same rows. They
    /// cannot show that the disagreement is never resolved in the fold's favour somewhere inside the
    /// producer, because a test observes outputs and not paths. This does: the producer's own source
    /// names neither the fold's type nor the single-language observation it returns, so there is no
    /// call site at which a substitution could happen.
    /// </remarks>
    [TestMethod]
    public void TheProductionPathDoesNotReachTheLegacyFold()
    {
        var source = ReadSource("src/Lex.V3.Ingest/Europe/EuLanguageScopedExpressionProducer.cs");
        var code = StripComments(source);

        Assert.IsFalse(
            code.Contains("EuCellarObjectDecode", StringComparison.Ordinal),
            "the production path must not reach the single-language fold.");
        Assert.IsFalse(
            code.Contains("EuLanguageExpressionObservation", StringComparison.Ordinal),
            "nor mint the one-per-object observation it returns.");
    }

    /// <summary>What is retained is the derivation's own bytes, reopened from the store by digest.</summary>
    /// <remarks>
    /// A receipt proves a write happened; it does not prove WHAT was written. Reading the bytes back
    /// out of the store and comparing them to the derivation in hand is what makes "retained" a fact
    /// about this artifact rather than about some artifact.
    /// </remarks>
    [TestMethod]
    public async Task TheRetainedBytesAreTheDerivationsOwnCanonicalBytes()
    {
        var (result, _, store) = await RunAsync(ThreeLanguageRows());

        Assert.AreEqual(EuLanguageScopedExpressionProductionRefusal.None, result.Refusal, result.Detail);

        var reopened = await CustodyRestore.ReadByDigestCheckedAsync(
            store, result.RetainedDerivation!.Reference.ContentSha256, CancellationToken.None);

        CollectionAssert.AreEqual(
            result.Derivation!.DerivationBytes.ToArray(),
            reopened.ToArray(),
            "the store must hand back the derivation's own canonical bytes.");
        Assert.AreEqual(
            result.Derivation.DerivationSha256,
            result.RetainedDerivation.Reference.ContentSha256,
            "and the address it is held under must be the derivation's own digest.");

        // AND THE EPISODE IS HELD TOO, so splitting the two did not quietly drop the provenance
        // half. Retaining only the stable document would have satisfied S3-A04's first clause by
        // discarding what its second clause requires.
        Assert.IsNotNull(result.RetainedEpisode);
        var reopenedEpisode = await CustodyRestore.ReadByDigestCheckedAsync(
            store, result.RetainedEpisode!.Reference.ContentSha256, CancellationToken.None);
        CollectionAssert.AreEqual(
            result.Derivation.EpisodeBytes.ToArray(),
            reopenedEpisode.ToArray(),
            "the episode record is retained beside the derivation, not instead of it.");
    }

    /// <summary>
    /// An object this run never asked about is refused, not answered with an empty list.
    /// </summary>
    /// <remarks>
    /// Decision 64: an empty list and "we never asked" are indistinguishable unless something
    /// refuses to answer the second.
    /// </remarks>
    [TestMethod]
    public async Task AskingAboutAWorkThisRunNeverAskedAboutRefusesRatherThanAnsweringEmpty()
    {
        var (result, _, _) = await RunAsync(EnglishOnlyRows());

        Assert.AreEqual(EuLanguageScopedExpressionProductionRefusal.None, result.Refusal, result.Detail);
        Assert.HasCount(1, result.ExpressionsOf(Work));

        var thrown = Assert.ThrowsExactly<InvalidOperationException>(
            () => result.ExpressionsOf("http://publications.europa.eu/resource/cellar/work-never-asked"));
        StringAssert.Contains(
            thrown.Message, "never asked",
            "an unasked object is refused by name rather than answered empty.");
    }

    /// <summary>A family mix-up in the Expression slot refuses before a single request is sent.</summary>
    [TestMethod]
    public async Task AFamilyMixUpInTheExpressionSlotRefusesBeforeAnyTraffic()
    {
        var handler = new EuAcquisitionTestFixture.ClassifyingHandler(Scripts(EnglishOnlyRows()));
        var store = new EuAcquisitionTestFixture.EuInMemoryCustodyStore();
        var producer = new EuLanguageScopedExpressionProducer(
            store, new EuAcquisitionTestFixture.FixedTimeProvider(), handler);

        var result = await producer.RunAsync(
            Request(EuObjectFactsQuerySet.ManifestationFacts),
            null,
            EuAcquisitionTestFixture.SourceWitness(),
            CancellationToken.None);

        Assert.AreEqual(
            EuLanguageScopedExpressionProductionRefusal.ExpressionFactsRequestIsNotTheExpressionFamily,
            result.Refusal);
        Assert.AreEqual(
            0, handler.SendCount,
            "the wrong family must be refused before the session's robots request, not after two "
            + "sessions' worth of traffic.");
        Assert.AreEqual(0, result.ProductRequestCount);
    }

    /// <summary>The same, for the object-facts slot.</summary>
    [TestMethod]
    public async Task AFamilyMixUpInTheObjectSlotRefusesBeforeAnyTraffic()
    {
        var handler = new EuAcquisitionTestFixture.ClassifyingHandler(Scripts(EnglishOnlyRows()));
        var store = new EuAcquisitionTestFixture.EuInMemoryCustodyStore();
        var producer = new EuLanguageScopedExpressionProducer(
            store, new EuAcquisitionTestFixture.FixedTimeProvider(), handler);

        var budget = EuAcquisitionTestFixture.TestWireBudget();
        var result = await producer.RunAsync(
            Request(EuObjectFactsQuerySet.ExpressionFacts, budget),
            Request(EuObjectFactsQuerySet.RootWatermark, budget),
            EuAcquisitionTestFixture.SourceWitness(),
            CancellationToken.None);

        Assert.AreEqual(
            EuLanguageScopedExpressionProductionRefusal.ObjectFactsRequestIsNotTheObjectFamily,
            result.Refusal);
        Assert.AreEqual(0, handler.SendCount, "refused before any traffic.");
    }

    /// <summary>
    /// Two families carrying different budget instances refuse before any traffic.
    /// </summary>
    /// <remarks>
    /// FOUND BY #579's REBASE ONTO THIS FILE, not by review. Making the object-facts request carry a
    /// required ceiling handed this door two of them, one per family. A budget is a mutable counter,
    /// so two instances both reading 100,000 bound 100,000 requests EACH and the production is
    /// bounded by neither.
    /// <para>
    /// The two budgets here are given EQUAL limits deliberately: a check comparing <c>Limit</c> would
    /// pass this test while the defect shipped, so only identity can tell them apart.
    /// <see cref="EuQueryExecutionAdapter"/> refuses the same shape for its census seeds.
    /// </para>
    /// </remarks>
    [TestMethod]
    public async Task TwoFamiliesCarryingDifferentBudgetInstancesRefuseBeforeAnyTraffic()
    {
        var handler = new EuAcquisitionTestFixture.ClassifyingHandler(ScriptsWithEmptyObjectFacts());
        var store = new EuAcquisitionTestFixture.EuInMemoryCustodyStore();
        var producer = new EuLanguageScopedExpressionProducer(
            store, new EuAcquisitionTestFixture.FixedTimeProvider(), handler);

        var expressionBudget = EuAcquisitionTestFixture.TestWireBudget();
        var objectBudget = EuAcquisitionTestFixture.TestWireBudget();
        Assert.AreEqual(
            expressionBudget.Limit, objectBudget.Limit,
            "the premise: equal limits, so only identity can tell these apart.");

        var result = await producer.RunAsync(
            Request(EuObjectFactsQuerySet.ExpressionFacts, expressionBudget),
            Request(EuObjectFactsQuerySet.ObjectFacts, objectBudget),
            EuAcquisitionTestFixture.SourceWitness(),
            CancellationToken.None);

        Assert.AreEqual(
            EuLanguageScopedExpressionProductionRefusal.FamiliesCarryDifferentWireBudgets,
            result.Refusal,
            "a production with two ceilings has none, and must say so.");
        Assert.AreEqual(
            0, handler.SendCount,
            "refused before either family is asked, not after both have spent their own allowance.");
        Assert.AreEqual(0, expressionBudget.Spent);
        Assert.AreEqual(0, objectBudget.Spent);
    }

    /// <summary>
    /// A date delivery asked about OTHER objects is refused, not accepted as "no date".
    /// </summary>
    /// <remarks>
    /// FOUND IN REVIEW, NOT BY ME, AND IT IS THIS FILE'S OWN RULE BEING BROKEN. The decoder filters
    /// family P rows to the works family X delivered Expressions of, so a P batch over a disjoint
    /// object set succeeds, contributes nothing, and every expression comes back with no date. The
    /// derivation then carries a non-null ObjectFactsProof, whose own documentation says that means
    /// "a date delivery WAS consulted" - so a reader concludes "asked, and the publisher stated
    /// none". P never asked. That is exactly the absence confusion ExpressionsOf refuses one type
    /// over, reintroduced at the door that pairs the two families.
    /// </remarks>
    [TestMethod]
    public async Task ADateDeliveryAskedAboutOtherObjectsIsRefusedRatherThanReadAsNoDate()
    {
        var handler = new EuAcquisitionTestFixture.ClassifyingHandler(ScriptsWithEmptyObjectFacts());
        var store = new EuAcquisitionTestFixture.EuInMemoryCustodyStore();
        var producer = new EuLanguageScopedExpressionProducer(
            store, new EuAcquisitionTestFixture.FixedTimeProvider(), handler);

        // ONE budget across both families, or this would refuse for the other reason and say
        // nothing about coverage.
        var budget = EuAcquisitionTestFixture.TestWireBudget();
        var result = await producer.RunAsync(
            Request(EuObjectFactsQuerySet.ExpressionFacts, budget),
            RequestOver(EuObjectFactsQuerySet.ObjectFacts, OtherWork, budget),
            EuAcquisitionTestFixture.SourceWitness(),
            CancellationToken.None);

        Assert.AreEqual(
            EuLanguageScopedExpressionProductionRefusal.ObjectFactsBatchDoesNotCoverTheExpressionBatch,
            result.Refusal,
            "a P batch that never asked about this run's works cannot stand in for one that did.");
        Assert.AreEqual(
            0, handler.SendCount,
            "and it is refused before either family is asked, not after both have been spent.");
    }

    /// <summary>
    /// S3-A04: two independent executions over identical publisher rows derive byte-identically.
    /// </summary>
    /// <remarks>
    /// FOUND IN REVIEW, AND IT IS THE SAME LESSON THIS REPOSITORY ALREADY RECORDED ONE LAYER DOWN.
    /// <c>LanguageScopedExpression.CanonicalContentSha256</c>'s own remarks describe replacing a
    /// page-blob digest because it "made semantic identity depend on transport structure". The first
    /// head of the derivation did precisely that again: its digest covered each proof's
    /// <c>AcquisitionRunRef</c>, and <c>RoutedHttpAcquisitionSession</c> mints one from a fresh
    /// <c>Guid.NewGuid()</c> per session, so two identical runs addressed the same derivation
    /// differently.
    /// <para>
    /// S3-A04 requires both halves and they are not in tension: the DERIVATION is byte-stable, and
    /// the transport lineage is still retained - now as a separately digested episode section rather
    /// than inside the identity.
    /// </para>
    /// </remarks>
    [TestMethod]
    public async Task TwoIndependentExecutionsOverIdenticalRowsDeriveByteIdentically()
    {
        var (first, _, _) = await RunAsync(ThreeLanguageRows());
        var (second, _, _) = await RunAsync(ThreeLanguageRows());

        Assert.AreEqual(EuLanguageScopedExpressionProductionRefusal.None, first.Refusal, first.Detail);
        Assert.AreEqual(EuLanguageScopedExpressionProductionRefusal.None, second.Refusal, second.Detail);

        // The premise: these really are two independent episodes, or the test proves nothing about
        // stability ACROSS executions.
        Assert.AreNotEqual(
            first.Derivation!.ExpressionFactsProof.AcquisitionRunRef.Sha256,
            second.Derivation!.ExpressionFactsProof.AcquisitionRunRef.Sha256,
            "two runs must carry different acquisition runs, or there is nothing to stabilise.");

        Assert.AreEqual(
            first.Derivation.DerivationSha256,
            second.Derivation.DerivationSha256,
            "S3-A04: derivation is byte-stable across two independent executions.");
        CollectionAssert.AreEqual(
            first.Derivation.DerivationBytes.ToArray(),
            second.Derivation.DerivationBytes.ToArray(),
            "byte-stable means the bytes, not merely the digest.");

        // AND THE LINEAGE IS STILL RETAINED, which is the other half of the same clause.
        Assert.IsTrue(
            first.Derivation.Expressions.All(static expression => expression.Lineage.Entries.Count > 0),
            "every object retains its transport-byte lineage.");
        Assert.AreNotEqual(
            first.Derivation.EpisodeSha256,
            second.Derivation.EpisodeSha256,
            "and the episode section still records WHICH run observed it, so nothing is lost.");
    }

    /// <summary>
    /// Every COMPONENT a proof publishes is read by exactly one of the two canonical documents.
    /// </summary>
    /// <remarks>
    /// <para>
    /// COMPONENTS, NOT PROPERTIES, AND THE DIFFERENCE COST A REVIEW ROUND. The first version of this
    /// test read each property whole, so a <see cref="SourceArtifactRef"/> counted as one thing. That
    /// reading was satisfied by "the interpretation profile reference is in the episode" - and hid
    /// that the reference is a COMPOUND of a per-run <c>ResourceId</c> and a content <c>Sha256</c>,
    /// so moving it whole had moved a stable digest out of the derivation identity. Two deliveries
    /// differing only in their interpretation rules then shared one derivation address.
    /// </para>
    /// <para>
    /// A pin that reads a compound value as indivisible cannot see a split that has to happen inside
    /// it. So each reference is decomposed here, and each half has to be placed deliberately.
    /// </para>
    /// <para>
    /// Two earlier lessons are kept: the test exists at all because a mutant survived
    /// (<c>RetainedFloor</c> hard-coded to a literal changed nothing any behavioural test observed),
    /// and it pins a LIST because #584's review found the same class of omission one type over - a
    /// behavioural test per field says nothing about a field nobody has added yet.
    /// </para>
    /// </remarks>
    [TestMethod]
    public void EveryProofComponentIsReadByExactlyOneOfTheTwoCanonicalDocuments()
    {
        var properties = typeof(Lex.V3.Contracts.Source.Absence.AbsenceFamilyEnumerationProof)
            .GetProperties(System.Reflection.BindingFlags.Public
                | System.Reflection.BindingFlags.Instance
                | System.Reflection.BindingFlags.DeclaredOnly)
            .OrderBy(property => property.Name, StringComparer.Ordinal)
            .ToArray();

        CollectionAssert.AreEqual(
            new[]
            {
                "AcquisitionRunRef",
                "CanonicalKeyDigest",
                "DeliveredRowCount",
                "FamilyKey",
                "InterpretationProfileRef",
                "RetainedFloor",
                "SourceProfileRef",
            },
            properties.Select(property => property.Name).ToArray(),
            "a proof that grew a field must be placed in one of the two documents deliberately.");

        // A COMPOUND TYPE THIS TEST DOES NOT KNOW HOW TO DECOMPOSE WOULD MASK THE SAME DEFECT AGAIN,
        // so the set of compound types is itself pinned rather than assumed.
        CollectionAssert.AreEqual(
            new[] { "AcquisitionRunRef", "InterpretationProfileRef", "SourceProfileRef" },
            properties
                .Where(property => property.PropertyType == typeof(SourceArtifactRef))
                .Select(property => property.Name)
                .ToArray(),
            "SourceArtifactRef is the only compound this test decomposes; a new compound type needs "
            + "its own decomposition here before it can be placed.");

        var components = properties
            .SelectMany(property => property.PropertyType == typeof(SourceArtifactRef)
                ? new[]
                {
                    $"proof.{property.Name}.ResourceId",
                    $"proof.{property.Name}.Sha256",
                }
                : [$"proof.{property.Name}"])
            .ToArray();

        var source = ReadSource(
            "src/Lex.V3.Contracts/Source/Europe/EuLanguageScopedExpressionDerivation.cs");
        var derivation = MethodBody(source, "private sealed record CanonicalProofDocument(");
        var episode = MethodBody(source, "private sealed record CanonicalEpisodeProofDocument(");

        foreach (var component in components)
        {
            var inDerivation = derivation.Contains(component, StringComparison.Ordinal);
            var inEpisode = episode.Contains(component, StringComparison.Ordinal);

            Assert.IsTrue(
                inDerivation || inEpisode,
                $"no canonical document reads {component}, so it is evidence this record drops.");

            // FamilyKey is the one component both halves carry, and deliberately: it is a LABEL
            // saying which family each half is about, not evidence either half owns.
            if (!string.Equals(component, "proof.FamilyKey", StringComparison.Ordinal))
            {
                Assert.IsFalse(
                    inDerivation && inEpisode,
                    $"{component} is read by both documents; a per-run value in the derivation "
                    + "identity is what makes it unstable.");
            }
        }

        Assert.IsTrue(
            derivation.Contains("proof.FamilyKey", StringComparison.Ordinal)
                && episode.Contains("proof.FamilyKey", StringComparison.Ordinal),
            "the exception is that BOTH halves label themselves with the family, and it is an "
            + "exception this test states rather than tolerates silently.");

        // AND THE PLACEMENT ITSELF, not merely that each component landed somewhere. A content
        // digest in the episode is the exact defect review found; a per-run resource id in the
        // derivation is the one before it.
        Assert.IsTrue(
            derivation.Contains("proof.InterpretationProfileRef.Sha256", StringComparison.Ordinal)
                && derivation.Contains("proof.SourceProfileRef.Sha256", StringComparison.Ordinal),
            "the profile CONTENT digests belong to the derivation identity: different rules are a "
            + "different derivation.");
        Assert.IsTrue(
            episode.Contains("proof.AcquisitionRunRef.Sha256", StringComparison.Ordinal),
            "the acquisition run's digest is per-run - it covers a fresh resource id and the run's "
            + "start time - so it belongs to the episode, both components of it.");
    }

    /// <summary>One brace-matched declaration body, so a mention elsewhere cannot satisfy the pin.</summary>
    private static string MethodBody(string source, string signature)
    {
        var start = source.IndexOf(signature, StringComparison.Ordinal);
        Assert.IsGreaterThanOrEqualTo(0, start, $"'{signature}' is not in the source read.");

        var open = source.IndexOf('{', start);
        Assert.IsGreaterThanOrEqualTo(0, open, "the declaration has no body.");

        var depth = 0;
        for (var index = open; index < source.Length; index++)
        {
            if (source[index] == '{')
            {
                depth++;
            }
            else if (source[index] == '}' && --depth == 0)
            {
                return source[open..index];
            }
        }

        Assert.Fail("the declaration body is unterminated.");
        return string.Empty;
    }

    // ---------------------------------------------------------------- fixture

    private static async Task<(
        EuLanguageScopedExpressionProductionResult Result,
        EuAcquisitionTestFixture.ClassifyingHandler Handler,
        EuAcquisitionTestFixture.EuInMemoryCustodyStore Store)> RunAsync(IReadOnlyList<string> xRows)
    {
        var handler = new EuAcquisitionTestFixture.ClassifyingHandler(Scripts(xRows));
        var store = new EuAcquisitionTestFixture.EuInMemoryCustodyStore();
        var producer = new EuLanguageScopedExpressionProducer(
            store, new EuAcquisitionTestFixture.FixedTimeProvider(), handler);

        var result = await producer.RunAsync(
            Request(EuObjectFactsQuerySet.ExpressionFacts),
            null,
            EuAcquisitionTestFixture.SourceWitness(),
            CancellationToken.None);
        return (result, handler, store);
    }

    private static Dictionary<string, EuAcquisitionTestFixture.FamilyScript> Scripts(
        IReadOnlyList<string> xRows) =>
        new(StringComparer.Ordinal)
        {
            ["X"] = EuAcquisitionTestFixture.ScriptFor(
                "X", xRows.Count, xRows, EuAcquisitionTestFixture.ExpressionFactsProjection),
        };

    private static EuObjectFactsPartitionRunRequest Request(
        EuObjectFactsQuerySet set, WireRequestBudget? budget = null) =>
        RequestOver(set, Work, budget);

    /// <summary>
    /// One partition request. Both families of a production must share ONE budget instance, so the
    /// default mints a shared one per test rather than a fresh one per request.
    /// </summary>
    private static EuObjectFactsPartitionRunRequest RequestOver(
        EuObjectFactsQuerySet set, string objectIri, WireRequestBudget? budget = null)
    {
        var (plan, planId) = EuAcquisitionTestFixture.BuildObjectFactsPlan();
        return new EuObjectFactsPartitionRunRequest(
            plan, planId, set, [objectIri], EuAcquisitionTestFixture.BuildRendererSource(4180),
            budget ?? EuAcquisitionTestFixture.TestWireBudget());
    }

    private static Dictionary<string, EuAcquisitionTestFixture.FamilyScript>
        ScriptsWithEmptyObjectFacts()
    {
        var scripts = Scripts(EnglishOnlyRows());
        scripts["P"] = EuAcquisitionTestFixture.ScriptFor(
            "P", 0, [], EuAcquisitionTestFixture.ObjectFactsProjection);
        return scripts;
    }

    private static IReadOnlyList<string> EnglishOnlyRows() =>
    [
        EuAcquisitionTestFixture.ExpressionFactRow(Work, EnglishExpression),
        EuAcquisitionTestFixture.ExpressionLanguageRow(Work, EnglishExpression, EnglishAuthority),
    ];

    /// <summary>
    /// Three Expressions of one work, in the publisher's own keyset order.
    /// </summary>
    /// <remarks>
    /// ORDERED BY CANONICAL KEY, NOT BY WHICH LANGUAGE READS FIRST. A page whose rows do not ascend
    /// refuses with <c>CursorDidNotAdvance</c> before any decode happens, which is the executor
    /// correctly rejecting a page no real publisher would send. Bulgarian sorts first here and that
    /// is a property of the IRI, not a statement about the language.
    /// </remarks>
    /// <summary>
    /// Two Expressions of one Work, both stating the SAME language authority, with the publisher's
    /// own work date delivered beside them.
    /// </summary>
    private static IReadOnlyList<string> SameLanguagePairRows() =>
    [
        EuAcquisitionTestFixture.ExpressionFactRow(Work, EnglishExpression),
        EuAcquisitionTestFixture.ExpressionLanguageRow(Work, EnglishExpression, EnglishAuthority),
        EuAcquisitionTestFixture.ExpressionFactRow(Work, SecondEnglishExpression),
        EuAcquisitionTestFixture.ExpressionLanguageRow(Work, SecondEnglishExpression, EnglishAuthority),
    ];

    private static Dictionary<string, EuAcquisitionTestFixture.FamilyScript> ScriptsWithWorkDate(
        IReadOnlyList<string> xRows)
    {
        var scripts = Scripts(xRows);
        string[] dateRows =
        [
            EuAcquisitionTestFixture.ObjectFactLiteralRow(
                Work, EuAcquisitionTestFixture.WorkDateDocument, WorkDate, XsdDate),
        ];
        scripts["P"] = EuAcquisitionTestFixture.ScriptFor(
            "P", dateRows.Length, dateRows, EuAcquisitionTestFixture.ObjectFactsProjection);
        return scripts;
    }

    private static IReadOnlyList<string> ThreeLanguageRows() =>
    [
        EuAcquisitionTestFixture.ExpressionFactRow(Work, BulgarianExpression),
        EuAcquisitionTestFixture.ExpressionLanguageRow(Work, BulgarianExpression, BulgarianAuthority),
        EuAcquisitionTestFixture.ExpressionFactRow(Work, EnglishExpression),
        EuAcquisitionTestFixture.ExpressionLanguageRow(Work, EnglishExpression, EnglishAuthority),
        EuAcquisitionTestFixture.ExpressionFactRow(Work, FrenchExpression),
        EuAcquisitionTestFixture.ExpressionLanguageRow(Work, FrenchExpression, FrenchAuthority),
    ];

    /// <summary>
    /// #418's completion criterion, at run level: multiple same-language expressions coexist and the
    /// corrigendum date survives onto each of them.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The coexistence itself is already pinned one layer down, on the type, by
    /// <c>LanguageScopedExpressionTests.TwoExpressionsOfOneWorkInOneLanguageCoexist</c>. That proves
    /// the model PERMITS two same-language expressions; it cannot prove a governed run DELIVERS
    /// them, because nothing in it goes near the decoder or the wire.
    /// </para>
    /// <para>
    /// What this closes is the gap between those two facts. Every other run-level fixture in this
    /// file gives each Expression a distinct language, so a decoder that kept its expressions in a
    /// map keyed by language would pass all of them and quietly drop one of these two. The decoder
    /// does not do that -- it keys identity by the Expression IRI -- and this is the test that would
    /// fail if that ever changed.
    /// </para>
    /// <para>
    /// The date half is checked on BOTH expressions rather than on the set. The publisher states one
    /// work_date_document for the Work, so a run that attached it to whichever expression it read
    /// last, or to only one of them, would still satisfy an assertion that "the date survives".
    /// </para>
    /// </remarks>
    [TestMethod]
    public async Task TwoSameLanguageExpressionsBothSurviveARunAndBothCarryTheWorksDate()
    {
        var handler = new EuAcquisitionTestFixture.ClassifyingHandler(
            ScriptsWithWorkDate(SameLanguagePairRows()));
        var store = new EuAcquisitionTestFixture.EuInMemoryCustodyStore();
        var producer = new EuLanguageScopedExpressionProducer(
            store, new EuAcquisitionTestFixture.FixedTimeProvider(), handler);

        // ONE budget across both families, exactly as a real production pairs them.
        var budget = EuAcquisitionTestFixture.TestWireBudget();
        var result = await producer.RunAsync(
            Request(EuObjectFactsQuerySet.ExpressionFacts, budget),
            Request(EuObjectFactsQuerySet.ObjectFacts, budget),
            EuAcquisitionTestFixture.SourceWitness(),
            CancellationToken.None);

        Assert.AreEqual(
            EuLanguageScopedExpressionProductionRefusal.None, result.Refusal,
            $"{result.Refusal} {result.Detail}");

        var expressions = result.Derivation!.Expressions
            .OrderBy(expression => expression.Identity.PublisherExpressionId, StringComparer.Ordinal)
            .ToArray();

        Assert.HasCount(2, expressions, "both same-language expressions must survive the run.");
        CollectionAssert.AreEqual(
            new[] { EnglishExpression, SecondEnglishExpression },
            expressions.Select(expression => expression.Identity.PublisherExpressionId).ToArray(),
            "they are two distinct Expressions, told apart by their own IRIs and not by language.");
        Assert.AreEqual(
            1,
            expressions.Select(expression => expression.Identity.PublisherWorkId)
                .Distinct(StringComparer.Ordinal).Count(),
            "and both are Expressions of the one Work.");

        foreach (var expression in expressions)
        {
            Assert.AreEqual(
                EnglishAuthority, expression.OfficialLanguage,
                "both state the same language authority; that is the whole point of the pair.");
            Assert.IsNotNull(
                expression.PublisherCorrigendumDate,
                $"the Work's date must survive onto {expression.Identity.PublisherExpressionId}, "
                    + "not onto whichever expression was read last.");
            Assert.AreEqual(WorkDate, expression.PublisherCorrigendumDate!.RawLexical);
            Assert.AreEqual(XsdDate, expression.PublisherCorrigendumDate.DatatypeIri);
        }

        // REAL DISPATCH: both families came off the socket this handler served.
        Assert.IsGreaterThan(0, handler.OccurrenceCountFor("X"));
        Assert.IsGreaterThan(0, handler.OccurrenceCountFor("P"));
    }

    /// <summary>Code with comments and string literals removed, so a mention in prose cannot satisfy a pin.</summary>
    private static string StripComments(string source)
    {
        var withoutBlocks = System.Text.RegularExpressions.Regex.Replace(
            source, @"/\*.*?\*/", string.Empty,
            System.Text.RegularExpressions.RegexOptions.Singleline);
        var withoutLines = System.Text.RegularExpressions.Regex.Replace(
            withoutBlocks, @"//[^\n]*", string.Empty);
        return System.Text.RegularExpressions.Regex.Replace(withoutLines, "\"[^\"\\n]*\"", "\"\"");
    }

    private static string ReadSource(string repositoryRelativePath)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Lex.V3.slnx")))
        {
            directory = directory.Parent;
        }

        var root = directory?.FullName
            ?? throw new InvalidOperationException("Checkout root not found.");
        return File.ReadAllText(
            Path.Combine(root, repositoryRelativePath.Replace('/', Path.DirectorySeparatorChar)),
            Encoding.UTF8);
    }
}
