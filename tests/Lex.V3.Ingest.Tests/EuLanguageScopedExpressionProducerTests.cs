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

        var result = await producer.RunAsync(
            Request(EuObjectFactsQuerySet.ExpressionFacts),
            Request(EuObjectFactsQuerySet.RootWatermark),
            EuAcquisitionTestFixture.SourceWitness(),
            CancellationToken.None);

        Assert.AreEqual(
            EuLanguageScopedExpressionProductionRefusal.ObjectFactsRequestIsNotTheObjectFamily,
            result.Refusal);
        Assert.AreEqual(0, handler.SendCount, "refused before any traffic.");
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

        var result = await producer.RunAsync(
            Request(EuObjectFactsQuerySet.ExpressionFacts),
            RequestOver(EuObjectFactsQuerySet.ObjectFacts, OtherWork),
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
    /// Every field a proof publishes is read by one of the two canonical documents, and none by both.
    /// </summary>
    /// <remarks>
    /// <para>
    /// ADDED BECAUSE A MUTANT SURVIVED, NOT BECAUSE IT LOOKED TIDY. Hard-coding <c>RetainedFloor</c>
    /// to a literal in the derivation identity changed nothing that any test observed: the
    /// byte-stability test runs two productions whose floors are equal, so a field that stopped
    /// being read still produced agreeing digests. #584's review found the identical omission one
    /// type over - a comparison reading four of this proof's seven fields, so a proof retained under
    /// the weaker custody class replayed as identical to a floored one - and its conclusion applies
    /// unchanged: a behavioural test per field says nothing about a field nobody has added yet, so
    /// the surface is pinned against the documents' own source.
    /// </para>
    /// <para>
    /// The partition is asserted as well as the coverage. A field read by BOTH documents would be
    /// carried into the derivation identity as well as the episode, which is how a run-specific
    /// reference would creep back into the stable half - the defect review found. A field read by
    /// NEITHER is evidence silently dropped.
    /// </para>
    /// </remarks>
    [TestMethod]
    public void EveryProofFieldIsReadByExactlyOneOfTheTwoCanonicalDocuments()
    {
        var fields = typeof(Lex.V3.Contracts.Source.Absence.AbsenceFamilyEnumerationProof)
            .GetProperties(System.Reflection.BindingFlags.Public
                | System.Reflection.BindingFlags.Instance
                | System.Reflection.BindingFlags.DeclaredOnly)
            .Select(property => property.Name)
            .OrderBy(name => name, StringComparer.Ordinal)
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
            fields,
            "a proof that grew a field must be placed in one of the two documents deliberately, "
            + "rather than silently belonging to neither.");

        var source = ReadSource(
            "src/Lex.V3.Contracts/Source/Europe/EuLanguageScopedExpressionDerivation.cs");
        var derivation = MethodBody(source, "private sealed record CanonicalProofDocument(");
        var episode = MethodBody(source, "private sealed record CanonicalEpisodeProofDocument(");

        foreach (var field in fields)
        {
            var inDerivation = derivation.Contains("proof." + field, StringComparison.Ordinal);
            var inEpisode = episode.Contains("proof." + field, StringComparison.Ordinal);

            Assert.IsTrue(
                inDerivation || inEpisode,
                $"no canonical document reads {field}, so it is evidence this record drops.");

            // FamilyKey is the one field both halves carry, and deliberately: it is a LABEL saying
            // which family each half is about, not evidence either half owns. Every other field
            // belongs to exactly one, because a run-specific reference appearing in the derivation
            // identity is precisely what made it unstable.
            if (!string.Equals(field, "FamilyKey", StringComparison.Ordinal))
            {
                Assert.IsFalse(
                    inDerivation && inEpisode,
                    $"{field} is read by both documents; a run-specific reference in the derivation "
                    + "identity is what makes it unstable.");
            }
        }

        Assert.IsTrue(
            derivation.Contains("proof.FamilyKey", StringComparison.Ordinal)
                && episode.Contains("proof.FamilyKey", StringComparison.Ordinal),
            "the exception is that BOTH halves label themselves with the family, and it is an "
            + "exception this test states rather than tolerates silently.");
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

    private static EuObjectFactsPartitionRunRequest Request(EuObjectFactsQuerySet set) =>
        RequestOver(set, Work);

    private static EuObjectFactsPartitionRunRequest RequestOver(
        EuObjectFactsQuerySet set, string objectIri)
    {
        var (plan, planId) = EuAcquisitionTestFixture.BuildObjectFactsPlan();
        return new EuObjectFactsPartitionRunRequest(
            plan, planId, set, [objectIri], EuAcquisitionTestFixture.BuildRendererSource(4180));
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
    private static IReadOnlyList<string> ThreeLanguageRows() =>
    [
        EuAcquisitionTestFixture.ExpressionFactRow(Work, BulgarianExpression),
        EuAcquisitionTestFixture.ExpressionLanguageRow(Work, BulgarianExpression, BulgarianAuthority),
        EuAcquisitionTestFixture.ExpressionFactRow(Work, EnglishExpression),
        EuAcquisitionTestFixture.ExpressionLanguageRow(Work, EnglishExpression, EnglishAuthority),
        EuAcquisitionTestFixture.ExpressionFactRow(Work, FrenchExpression),
        EuAcquisitionTestFixture.ExpressionLanguageRow(Work, FrenchExpression, FrenchAuthority),
    ];

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
