using System.Reflection;
using Lex.V3.Contracts;
using Lex.V3.Contracts.Facts;
using Lex.V3.Contracts.Source.Luxembourg;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Lex.V3.Tests.Contracts.Source.Luxembourg;

/// <summary>
/// Stage 2 item E8: the Conseil d'État opinion carried as an official locator and nothing else.
/// </summary>
/// <remarks>
/// <para>
/// The authority for the link-only disposition is the question catalogue's row 57: "the opinion PDFs
/// carry no licence triple, so their text is never re-served | POINT (opinion date via enrichment;
/// text always link-out to the filestore and conseil-etat.public.lu)".
/// </para>
/// <para>
/// The robots position is measured rather than assumed. `conseil-etat.public.lu/robots.txt` disallows
/// every query-string shape for `User-agent: *` while permitting bare paths, and a `HEAD` of the
/// proven document locator returned `200 application/pdf` with no licence, rights or `X-Robots-Tag`
/// header. `wdocs-pub.chd.lu/robots.txt` answers 404, so that host states no policy at all.
/// </para>
/// </remarks>
[TestClass]
public sealed class LuxembourgOpinionLinkOnlyTests
{
    private const string Opinion = "http://data.legilux.public.lu/resource/opinion/62629";
    private const string ProvenLocator =
        "https://conseil-etat.public.lu/content/dam/conseil_etat/fr/avis/2026/17072026/62629-avis-du-17-juillet-2026.pdf";
    private const string XsdDate = "http://www.w3.org/2001/XMLSchema#date";
    private const string Observation = "urn:uuid:00000000-0000-4000-8000-0000000000a1";

    private static LuxembourgOpinionLinkOnlyRecord? Create(
        out LuxembourgOpinionLocatorRefusal refusal,
        string? opinionIri = Opinion,
        string? locator = ProvenLocator,
        string? date = "2026-07-17",
        string? datatype = XsdDate) =>
        LuxembourgOpinionLinkOnlyRecord.TryCreate(
            opinionIri, locator, date, datatype, Observation, out refusal);

    /// <summary>A proven opinion locator round-trips with its host read from the locator.</summary>
    [TestMethod]
    public void AProvenOpinionLocatorIsCarriedWithItsOwnTerms()
    {
        var record = Create(out var refusal);

        Assert.AreEqual(LuxembourgOpinionLocatorRefusal.None, refusal);
        Assert.IsNotNull(record);
        Assert.AreEqual(ProvenLocator, record!.DocumentLocator);
        Assert.AreEqual("conseil-etat.public.lu", record.DocumentHost);
        Assert.AreEqual("2026-07-17", record.RawOpinionDateLexical);
        Assert.AreEqual(DatePrecision.YearMonthDay, record.OpinionDatePrecision);
    }

    /// <summary>
    /// THE CENTRAL GUARD: this type cannot carry the opinion text, by construction rather than by
    /// convention.
    /// </summary>
    /// <remarks>
    /// The opinion PDFs carry no licence triple, so their text is never re-served. Declining to fetch
    /// the body would be a convention a later caller could break; having nowhere to put it cannot be
    /// broken by mistake. This asserts over the real surface, so adding a body, content or text
    /// member later fails here rather than passing quietly.
    /// </remarks>
    [TestMethod]
    public void TheRecordHasNowhereToPutTheOpinionText()
    {
        var members = typeof(LuxembourgOpinionLinkOnlyRecord)
            .GetMembers(BindingFlags.Public | BindingFlags.NonPublic |
                        BindingFlags.Instance | BindingFlags.Static)
            .Select(static member => member.Name)
            .Where(static name => name.Contains("Text", StringComparison.OrdinalIgnoreCase)
                               || name.Contains("Body", StringComparison.OrdinalIgnoreCase)
                               || name.Contains("Content", StringComparison.OrdinalIgnoreCase)
                               || name.Contains("Bytes", StringComparison.OrdinalIgnoreCase))
            .ToArray();

        CollectionAssert.AreEqual(
            Array.Empty<string>(), members,
            "an unlicensed opinion's text must be unrepresentable here, not merely unfetched.");

        // And the door accepts no such argument either.
        var parameters = typeof(LuxembourgOpinionLinkOnlyRecord)
            .GetMethod(nameof(LuxembourgOpinionLinkOnlyRecord.TryCreate))!
            .GetParameters()
            .Select(static parameter => parameter.Name!)
            .ToArray();
        CollectionAssert.AreEqual(
            new[]
            {
                "opinionIri", "documentLocator", "rawOpinionDateLexical",
                "opinionDateDatatypeIri", "sourceObservationId", "refusal",
            },
            parameters);
    }

    /// <summary>The disposition is the already-closed <c>point</c>, fixed and not a parameter.</summary>
    /// <remarks>
    /// D1-01 Candidate 5 closes this axis to four members, one of which is `point`: "expose the
    /// official identity or locator and typed limitation without holding or claiming the selected
    /// payload". Minting a fifth name for "link-only" would be an invention that reads like a
    /// decision, so the existing member is reused.
    /// </remarks>
    [TestMethod]
    public void TheDispositionIsTheAlreadyClosedPointAndNoCallerChoosesIt()
    {
        Assert.AreEqual(LuScopeTerminalState.Point, LuxembourgOpinionLinkOnlyRecord.Disposition);

        var accepts = typeof(LuxembourgOpinionLinkOnlyRecord)
            .GetMethod(nameof(LuxembourgOpinionLinkOnlyRecord.TryCreate))!
            .GetParameters()
            .Any(static parameter => parameter.ParameterType == typeof(LuScopeTerminalState));
        Assert.IsFalse(accepts, "the disposition is a property of the type, never a caller's choice.");
    }

    /// <summary>
    /// A locator carrying a query or fragment is refused, because the host's robots policy disallows it.
    /// </summary>
    /// <remarks>
    /// Measured from the live policy: `conseil-etat.public.lu` disallows `/*?*` for `User-agent: *`
    /// with one narrow `Allow: /*?b=*`, while bare paths carry no restriction. A link this product
    /// publishes has to be one a reader may follow, so an unfollowable shape is refused rather than
    /// carried.
    /// </remarks>
    [TestMethod]
    public void ALocatorTheHostsRobotsPolicyDisallowsIsRefused()
    {
        Assert.IsNull(Create(out var query, locator: ProvenLocator + "?download=1"));
        Assert.AreEqual(LuxembourgOpinionLocatorRefusal.DocumentLocatorIsNotRobotsPermitted, query);

        Assert.IsNull(Create(out var fragment, locator: ProvenLocator + "#page=2"));
        Assert.AreEqual(LuxembourgOpinionLocatorRefusal.DocumentLocatorIsNotRobotsPermitted, fragment);
    }

    /// <summary>A date valid for its datatype but impossible in the calendar is refused.</summary>
    [TestMethod]
    public void ADateThatCannotExistIsRefusedEvenThoughItsDatatypeIsAccepted()
    {
        Assert.IsNull(Create(out var impossible, date: "2026-02-30"));
        Assert.AreEqual(LuxembourgOpinionLocatorRefusal.OpinionDateNotValidAtItsPrecision, impossible);

        Assert.IsNotNull(Create(out var real, date: "2026-02-28"));
        Assert.AreEqual(LuxembourgOpinionLocatorRefusal.None, real);
    }

    /// <summary>Each declared refusal is reachable from a delivered shape, so none is decoration.</summary>
    [TestMethod]
    public void EveryDeclaredRefusalIsReachableFromSomeDeliveredShape()
    {
        var reached = new List<LuxembourgOpinionLocatorRefusal>();

        Assert.IsNull(Create(out var notAnIri, opinionIri: "not-an-iri"));
        reached.Add(notAnIri);

        Assert.IsNull(Create(out var badLocator, locator: "urn:not-http:x"));
        reached.Add(badLocator);

        Assert.IsNull(Create(out var notPermitted, locator: ProvenLocator + "?q=1"));
        reached.Add(notPermitted);

        Assert.IsNull(Create(out var noDate, date: null));
        reached.Add(noDate);

        Assert.IsNull(Create(out var badDatatype, datatype: "http://www.w3.org/2001/XMLSchema#string"));
        reached.Add(badDatatype);

        Assert.IsNull(Create(out var impossibleDate, date: "2026-02-30"));
        reached.Add(impossibleDate);

        CollectionAssert.AreEqual(
            Enum.GetValues<LuxembourgOpinionLocatorRefusal>()
                .Where(static member => member != LuxembourgOpinionLocatorRefusal.None)
                .ToArray(),
            reached.Distinct().Order().ToArray(),
            "every declared refusal must be driven by a case above, each reaching a distinct one.");
    }

    /// <summary>The access path is exactly the four proven JOLux terms.</summary>
    /// <remarks>
    /// Written out as independent literals rather than rebuilt from the same prefix constant the
    /// vocabulary uses: a comparison between two compile-time constants is folded away and pins
    /// nothing.
    /// </remarks>
    [TestMethod]
    public void TheAccessPathTermsAreExactlyTheProvenOnes()
    {
        CollectionAssert.AreEqual(
            new[]
            {
                "http://data.legilux.public.lu/resource/ontology/jolux#OpinionConseilEtat",
                "http://data.legilux.public.lu/resource/ontology/jolux#hasOpinion",
                "http://data.legilux.public.lu/resource/ontology/jolux#hasResultingOpinionDocument",
                "http://data.legilux.public.lu/resource/ontology/jolux#opinionDate",
            },
            new[]
            {
                LuxembourgOpinionLinkOnlyVocabulary.OpinionConseilEtatClassIri,
                LuxembourgOpinionLinkOnlyVocabulary.HasOpinionPredicateIri,
                LuxembourgOpinionLinkOnlyVocabulary.HasResultingOpinionDocumentPredicateIri,
                LuxembourgOpinionLinkOnlyVocabulary.OpinionDatePredicateIri,
            });
    }

    /// <summary>
    /// The reversal criterion names both halves and says which one is actually blocking.
    /// </summary>
    /// <remarks>
    /// A reversal criterion that says only "a licence would change this" is a sentiment. This one has
    /// to survive being read by someone deciding whether the condition is met, so it names the two
    /// conditions separately and records that robots permission is already satisfied on one host and
    /// unstated on another, leaving the licence as the sole blocker.
    /// </remarks>
    [TestMethod]
    public void TheReversalCriterionNamesBothHalvesAndTheBlockingOne()
    {
        var criterion = LuxembourgOpinionLinkOnlyVocabulary.ReversalCriterion;

        StringAssert.Contains(criterion, "licence");
        StringAssert.Contains(criterion, "robots");
        StringAssert.Contains(criterion, "conseil-etat.public.lu");
        StringAssert.Contains(criterion, "wdocs-pub.chd.lu");
        StringAssert.Contains(criterion, "sole blocking condition");
    }
}
