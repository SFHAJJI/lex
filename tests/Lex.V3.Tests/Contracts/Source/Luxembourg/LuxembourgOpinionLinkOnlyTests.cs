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

    /// <summary>
    /// An arbitrary host is refused: one host's robots result is not evidence about another's.
    /// </summary>
    /// <remarks>
    /// This is Codex's finding against `16623c5c`, and it was a real over-generalisation of my own
    /// measurement. The door accepted any absolute bare HTTP(S) URI, stored its host, and stamped it
    /// with the link-only disposition — so <c>https://example.com/opinion.pdf</c> was carried as an
    /// official Conseil d'État locator on the strength of a robots policy <c>example.com</c> never
    /// published. I measured one host and let the door speak for every host on the internet.
    /// </remarks>
    [TestMethod]
    public void AnArbitraryHostIsRefusedRatherThanCarriedUnderAnotherHostsPermission()
    {
        Assert.IsNull(Create(out var foreign, locator: "https://example.com/opinion.pdf"));
        Assert.AreEqual(
            LuxembourgOpinionLocatorRefusal.DocumentLocatorIsNotAnAdmittedOfficialFamily, foreign);

        // The right host with the wrong path is refused too: the robots policies disallow by path,
        // so admission is per path and not merely per host.
        Assert.IsNull(Create(out var wrongPath, locator: "https://conseil-etat.public.lu/fr/search/x.pdf"));
        Assert.AreEqual(
            LuxembourgOpinionLocatorRefusal.DocumentLocatorIsNotAnAdmittedOfficialFamily, wrongPath);
    }

    /// <summary>
    /// An origin the evidence does not cover is refused even when the host and path are admitted.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Found in review on head <c>a30a5394</c>, and the second time this table generalised past what
    /// was measured. The first repair admitted any bare absolute HTTP(S) URI, projecting
    /// conseil-etat's robots result onto every host. This one bound host and path, so a scheme
    /// downgrade and an arbitrary port still walked through: <c>http://</c> is a different origin
    /// from <c>https://</c> and can reach a different service, and so can <c>:444</c>.
    /// </para>
    /// <para>
    /// Every probe behind <see cref="LuxembourgOpinionLinkOnlyVocabulary.AdmittedHostFamilies"/> —
    /// the robots fetches and the delivery checks alike — was made over <c>https</c> on the default
    /// port. So the admitted set is exactly those origins, and the explicit-<c>:443</c> case is
    /// asserted alongside the refusals to show this refuses a different origin rather than merely
    /// refusing any URL that carries a port.
    /// </para>
    /// </remarks>
    [TestMethod]
    public void AnOriginTheEvidenceDoesNotCoverIsRefusedEvenOnAnAdmittedHostAndPath()
    {
        const string AdmittedPath = "/content/dam/conseil_etat/fr/avis/2026/17072026/62629-avis.pdf";

        Assert.IsNull(
            Create(out var downgraded, locator: "http://conseil-etat.public.lu" + AdmittedPath),
            "a scheme downgrade reaches a different service than the one that answered robots.");
        Assert.AreEqual(
            LuxembourgOpinionLocatorRefusal.DocumentLocatorIsNotAnAdmittedOfficialFamily, downgraded);

        Assert.IsNull(
            Create(out var otherPort, locator: "https://conseil-etat.public.lu:444" + AdmittedPath),
            "an arbitrary port is a different origin and carries no measured permission.");
        Assert.AreEqual(
            LuxembourgOpinionLocatorRefusal.DocumentLocatorIsNotAnAdmittedOfficialFamily, otherPort);

        // Plaintext on the TLS port. This case is why the scheme is checked at all: it is the one
        // shape a port-only door admits, since 443 matches and the host and path do too. I found it
        // by mutation — deleting the scheme check left every other assertion here passing.
        Assert.IsNull(
            Create(out var plaintextOn443,
                locator: "http://conseil-etat.public.lu:443" + AdmittedPath),
            "http on 443 is still plaintext to a different service than the one measured.");
        Assert.AreEqual(
            LuxembourgOpinionLocatorRefusal.DocumentLocatorIsNotAnAdmittedOfficialFamily,
            plaintextOn443);

        // The same origin written with its default port explicit is the origin that was measured,
        // so it is admitted. Without this the two refusals above would also pass on a door that
        // simply refused every locator carrying a port.
        var explicitDefault = Create(
            out var admitted, locator: "https://conseil-etat.public.lu:443" + AdmittedPath);
        Assert.AreEqual(LuxembourgOpinionLocatorRefusal.None, admitted);
        Assert.IsNotNull(explicitDefault);

        // The table states the origin rather than leaving scheme and port to the door's discretion.
        foreach (var family in LuxembourgOpinionLinkOnlyVocabulary.AdmittedHostFamilies)
        {
            Assert.AreEqual("https", family.Scheme, family.Host);
            Assert.AreEqual(443, family.Port, family.Host);
        }
    }

    /// <summary>
    /// Each admitted family carries its own measured robots state, and they are not the same.
    /// </summary>
    /// <remarks>
    /// Flattening these into one boolean would repeat the same defect at a smaller scale.
    /// <c>conseil-etat</c> and the Legilux filestore are permitted by published policies — the first
    /// disallows only query shapes, the second an explicit list that does not include
    /// <c>/filestore/</c>. <c>wdocs-pub.chd.lu</c> answers 404 for <c>robots.txt</c> and therefore
    /// states nothing, which is not permission granted.
    /// </remarks>
    [TestMethod]
    public void EachAdmittedFamilyCarriesItsOwnMeasuredRobotsStateRatherThanASharedOne()
    {
        var families = LuxembourgOpinionLinkOnlyVocabulary.AdmittedHostFamilies;

        Assert.HasCount(3, families);
        CollectionAssert.AreEqual(
            new[] { "conseil-etat.public.lu", "legilux.public.lu", "wdocs-pub.chd.lu" },
            families.Select(static family => family.Host).ToArray());
        CollectionAssert.AreEqual(
            new[]
            {
                LuxembourgOpinionHostRobotsState.PermittedByStatedPolicy,
                LuxembourgOpinionHostRobotsState.PermittedByStatedPolicy,
                LuxembourgOpinionHostRobotsState.NoStatedPolicy,
            },
            families.Select(static family => family.RobotsState).ToArray(),
            "the chd document host states no policy, which is not the same as permission.");

        var record = Create(out _);
        Assert.IsNotNull(record);
        Assert.AreEqual(
            LuxembourgOpinionHostRobotsState.PermittedByStatedPolicy,
            record!.DocumentFamily.RobotsState,
            "a record carries its own family's state, never a projected one.");
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

        Assert.IsNull(Create(out var foreignHost, locator: "https://example.com/opinion.pdf"));
        reached.Add(foreignHost);

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
