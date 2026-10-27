using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Lex.V3.Api;
using Lex.V3.Contracts;
using Lex.V3.Contracts.Platform;
using Lex.V3.Contracts.Source.Core;
using Lex.V3.Contracts.Source.Europe;
using Lex.V3.Contracts.Source.Luxembourg;
using Lex.V3.Contracts.Source.Scope;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using static Lex.V3.Ingest.Tests.V3CorpusResolveMountTests;

namespace Lex.V3.Ingest.Tests;

/// <summary>
/// The text yield per class (R86(4)): the count over synthetic rows, every class, every reason and the zero cases; the block
/// held to its own arithmetic; and the coverage answer serving the build's figures, or none, from the build report beside a
/// mount, held to that mount.
/// </summary>
[TestClass]
public sealed class V3TextYieldTests
{
    private const string Eli = "http://data.legilux.public.lu/eli/etat/leg/";
    private const string Formats = "http://data.legilux.public.lu/resource/authority/user-format/";
    private const string French = "http://publications.europa.eu/resource/authority/language/FRA";
    private const string English = "http://publications.europa.eu/resource/authority/language/ENG";
    private static readonly SourceArtifactRef Evidence = new("urn:uuid:00000000-0000-4000-8000-000000000001", new string('e', 64));
    private static int _ordinal;

    private static string Digest(string seed) =>
        Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(Encoding.UTF8.GetBytes(seed)));

    /// <summary>A held Luxembourg file of <paramref name="root"/> in <paramref name="format"/>, with its rights and legal-content outcomes.</summary>
    private static LexCorpus6Member Lu(
        string root, string format, LuxembourgRightsChannelDisposition rights, params LexCorpus6Stage3Disposition[] content)
    {
        var objectRef = Digest(root + "|" + format + "|" + _ordinal);
        var wemi = new LexCorpus6LuxembourgWemiBinding(
            root, root + "/fr", root + "/fr/" + format, "http://data.legilux.public.lu/filestore/" + objectRef, French, Formats + format, Evidence, Digest("wemi" + objectRef));
        return new LexCorpus6Member(
            PublisherId.LuLegilux, objectRef, _ordinal++, Evidence, Evidence, ScopeDisposition.AcceptedSelected,
            rights == LuxembourgRightsChannelDisposition.AgreedSameRunCcBy ? LexCorpus6OutcomeKind.Acquired : LexCorpus6OutcomeKind.RightsWithheld,
            Digest("body" + objectRef), 1, Digest("receipt" + objectRef), null,
            new LexCorpus6LuxembourgRights(wemi, Evidence, Digest("binding" + objectRef), rights, [Evidence, Evidence]),
            content.Select((disposition, index) => new LexCorpus6Stage3Outcome(
                disposition is >= LexCorpus6Stage3Disposition.PdfNotApplicable and <= LexCorpus6Stage3Disposition.PdfActScopeUnproven
                    ? LexCorpus6Stage3OutcomeDomain.LuxembourgPublisherPdfActScope
                    : LexCorpus6Stage3OutcomeDomain.LuxembourgAknLegalContent,
                Digest(objectRef + index), disposition)).ToArray(),
            []);
    }

    /// <summary>A held EU file of <paramref name="contentClass"/> with its main-body disposition and annex outcomes.</summary>
    private static LexCorpus6Member Eu(EuContentClass contentClass, LexCorpus6Stage3Disposition mainBody, params LexCorpus6Stage3Disposition[] annexes)
    {
        var objectRef = Digest("eu|" + _ordinal);
        return new LexCorpus6Member(
            PublisherId.EuEurLex, objectRef, _ordinal++, Evidence, Evidence, ScopeDisposition.AcceptedSelected, LexCorpus6OutcomeKind.Acquired,
            Digest("body" + objectRef), 1, Digest("receipt" + objectRef), new EuContentClassObservation(contentClass, Evidence), null,
            new[] { new LexCorpus6Stage3Outcome(LexCorpus6Stage3OutcomeDomain.EuropeFormexMainBody, Digest(objectRef), mainBody) }
                .Concat(annexes.Select((disposition, index) => new LexCorpus6Stage3Outcome(
                    LexCorpus6Stage3OutcomeDomain.EuropeAnnexBody, Digest(objectRef + "annex" + index), disposition)))
                .ToArray(),
            []);
    }

    /// <summary>A catalogue state whose French expression has the given manifestations, each (format, licence or null).</summary>
    private static IEnumerable<(string, string, string)> State(string state, params (string Format, string? Licence)[] manifestations)
    {
        var expression = state + "/fr";
        yield return (state, "isRealizedBy", expression);
        yield return (expression, "language", French);
        foreach (var (format, licence) in manifestations)
        {
            var manifestation = expression + "/" + format;
            yield return (expression, "isEmbodiedBy", manifestation);
            yield return (manifestation, "userFormat", Formats + format);
            if (licence is not null)
            {
                yield return (manifestation, "license", licence);
            }
        }
    }

    private static V3TextYieldClass ClassOf(V3TextYieldPublisher publisher, string family, string kind) =>
        publisher.Classes.Single(row => row.Family == family && row.Kind == kind);

    private static Dictionary<string, long> Counts(IEnumerable<V3TextYieldCount>? rows) =>
        (rows ?? []).ToDictionary(static row => row.Reason, static row => row.Count);

    [TestMethod]
    public void EveryLuxembourgClassAndReasonIsCountedAndEachHeldFileIsInExactlyOneBucket()
    {
        const LuxembourgRightsChannelDisposition Agreed = LuxembourgRightsChannelDisposition.AgreedSameRunCcBy;
        const LuxembourgRightsChannelDisposition Rejected = LuxembourgRightsChannelDisposition.TypedQuarantineInFileReadingRejected;
        var codeA = Eli + "code/civil/20200101";
        var codeB = Eli + "code/civil/20210101";
        var codeC = Eli + "code/penal/20200101";
        var actA = Eli + "loi/2020/01/01/a1/jo";
        var actB = Eli + "loi/2020/01/02/a2/jo";
        var actC = Eli + "loi/2020/01/03/a3/jo";
        var actD = Eli + "loi/2020/01/04/a4/jo";
        var consolidationA = Eli + "loi/2020/01/01/a1/consolide/20210101";
        var consolidationB = Eli + "loi/2020/01/01/a1/consolide/20220101";
        var rgdA = Eli + "rgd/2020/01/01/a1/jo";
        var rgdConsolidation = Eli + "rgd/2020/01/01/a1/consolide/20210101";
        var members = new[]
        {
            Lu(codeA, "xml", Agreed, LexCorpus6Stage3Disposition.AknAdmitted, LexCorpus6Stage3Disposition.AknAdmitted, LexCorpus6Stage3Disposition.PdfNotApplicable),
            Lu(codeB, "pdf", Rejected, LexCorpus6Stage3Disposition.PdfGazetteIssueScope),
            Lu(codeC, "xml", Agreed, LexCorpus6Stage3Disposition.AknUpstreamNotInventoried),
            Lu(actA, "xml", Agreed, LexCorpus6Stage3Disposition.AknAdmitted),
            Lu(actB, "xml", Agreed, LexCorpus6Stage3Disposition.AknUnsupportedContentShape),
            Lu(actC, "xml", Agreed, LexCorpus6Stage3Disposition.AknXmlRejected, LexCorpus6Stage3Disposition.AknAdmitted),
            Lu(actD, "xml", Agreed),
            Lu(consolidationA, "xml", Rejected, LexCorpus6Stage3Disposition.AknAdmitted),
            Lu(consolidationB, "pdf", Agreed, LexCorpus6Stage3Disposition.PdfUpstreamTextLayerGap),
            Lu(rgdA, "xml", LuxembourgRightsChannelDisposition.NonAdmittingLicenceScl, LexCorpus6Stage3Disposition.AknAdmitted),
            Lu(rgdConsolidation, "xml", LuxembourgRightsChannelDisposition.Conflict, LexCorpus6Stage3Disposition.AknAdmitted),
        };
        var articles = new Dictionary<string, long>(StringComparer.Ordinal)
        {
            [members[0].ObjectRefSha256] = 2,
            [members[3].ObjectRefSha256] = 5,
        };
        // The catalogue lists each held state and one state of each not-held reason; the held code PDF's state is not in it.
        var notHeld = new Dictionary<string, IEnumerable<(string, string, string)>>
        {
            [Eli + "loi/2021/01/01/a9/jo"] = [],
            [Eli + "loi/2021/01/02/a9/jo"] = [(Eli + "loi/2021/01/02/a9/jo", "isRealizedBy", Eli + "loi/2021/01/02/a9/jo/en"), (Eli + "loi/2021/01/02/a9/jo/en", "language", English)],
            [Eli + "loi/2021/01/03/a9/jo"] = State(Eli + "loi/2021/01/03/a9/jo", ("html", V3TextYield.LicenceCcBy), ("docx", V3TextYield.LicenceCcBy)),
            [Eli + "loi/2021/01/04/a9/jo"] = State(Eli + "loi/2021/01/04/a9/jo", ("xml", V3TextYield.LicenceScl), ("pdf", V3TextYield.LicenceScl), ("html", V3TextYield.LicenceCcBy)),
            [Eli + "loi/2021/01/05/a9/jo"] = State(Eli + "loi/2021/01/05/a9/jo", ("xml-akomantoso", V3TextYield.LicenceScl), ("pdfa", V3TextYield.LicenceCcBy)),
            [Eli + "loi/2021/01/06/a9/jo"] = State(Eli + "loi/2021/01/06/a9/jo", ("xml", V3TextYield.LicenceCcBy), ("pdf", V3TextYield.LicenceCcBy)),
            [Eli + "loi/2021/01/07/a9/jo"] = State(Eli + "loi/2021/01/07/a9/jo", ("xml", null)),
            [Eli + "loi/2021/01/08/a9/jo"] = State(Eli + "loi/2021/01/08/a9/jo", ("pdf", "http://example.invalid/another-licence")),
            [Eli + "rgd/2021/01/01/a9/consolide/20220101"] = State(Eli + "rgd/2021/01/01/a9/consolide/20220101", ("xml", V3TextYield.LicenceScl)),
        };
        var subjects = new[] { codeA, codeC, actA, actB, actC, actD, consolidationA, consolidationB, rgdA, rgdConsolidation }
            .Concat(notHeld.Keys)
            // Subjects that spell no state (a work, an expression) are not catalogue states and are never counted.
            .Concat([Eli + "loi/2020/01/01/a1", Eli + "loi/2020/01/01/a1/jo/fr"])
            .ToArray();
        var yieldOf = V3TextYield.Count(members, new V3TextYield.LuxembourgInputs(articles, subjects, notHeld.Values.SelectMany(static facts => facts).ToArray()), null);

        var lu = yieldOf.Publishers.Single();
        Assert.AreEqual("lu-legilux", lu.Publisher);
        CollectionAssert.AreEqual(
            new[] { "code/code", "loi/as_published", "loi/consolidation", "rgd/as_published", "rgd/consolidation" },
            lu.Classes.Select(static row => row.Family + "/" + row.Kind).ToArray(), "classes by family and kind, in ordinal order");

        var code = ClassOf(lu, "code", "code");
        Assert.AreEqual((2L, 1L, 3L, 1L, 2L), (code.Works, code.WorksWithText, code.Versions, code.VersionsWithText, code.Articles),
            "a held state the catalogue does not list is still one of its class's versions");
        Assert.AreEqual((3L, 1L, 0L), (code.Files, code.FilesWithText, code.FilesPdfOnly!.Value));
        Assert.AreEqual(("typed_quarantine_in_file_reading_rejected", 1L, 1L), (code.FilesWithheld.Single().Reason, code.FilesWithheld.Single().Files, code.FilesWithheld.Single().Pdf));
        CollectionAssert.AreEquivalent(new Dictionary<string, long> { ["akn_upstream_not_inventoried"] = 1 }, Counts(code.FilesParseFailed));
        Assert.HasCount(0, code.VersionsNotHeld!);
        Assert.IsNull(code.Annexes, "the corpus records no Luxembourg annex outcome");

        var acts = ClassOf(lu, "loi", "as_published");
        Assert.AreEqual((12L, 1L, 12L, 1L, 5L), (acts.Works, acts.WorksWithText, acts.Versions, acts.VersionsWithText, acts.Articles));
        Assert.AreEqual((4L, 1L, 0L), (acts.Files, acts.FilesWithText, acts.FilesPdfOnly!.Value));
        CollectionAssert.AreEquivalent(new Dictionary<string, long>
        {
            ["akn_unsupported_content_shape"] = 1,
            // A file's dispositions in the corpus's order, joined: the corpus admitted one article and rejected the XML.
            ["akn_admitted+akn_xml_rejected"] = 1,
            [V3TextYield.NoLegalContentOutcome] = 1,
        }, Counts(acts.FilesParseFailed));
        CollectionAssert.AreEquivalent(new Dictionary<string, long>
        {
            [V3TextYield.NotHeldNoExpression] = 1,
            [V3TextYield.NotHeldTranslationsOnly] = 1,
            [V3TextYield.NotHeldNoReadableManifestation] = 1,
            [V3TextYield.NotHeldLicenceScl] = 1,
            [V3TextYield.NotHeldLicenceSclInPart] = 1,
            [V3TextYield.NotHeldCcByNotAcquired] = 1,
            [V3TextYield.NotHeldOtherLicence] = 2,
        }, Counts(acts.VersionsNotHeld), "a licence is read from the XML and PDF manifestations only, so HTML's CC BY does not count");

        var consolidations = ClassOf(lu, "loi", "consolidation");
        Assert.AreEqual((1L, 0L, 2L, 0L, 0L), (consolidations.Works, consolidations.WorksWithText, consolidations.Versions, consolidations.VersionsWithText, consolidations.Articles),
            "two consolidations of one act are one work");
        Assert.AreEqual((2L, 0L, 1L), (consolidations.Files, consolidations.FilesWithText, consolidations.FilesPdfOnly!.Value), "an admitted PDF with no text is PDF-only");
        Assert.AreEqual(("typed_quarantine_in_file_reading_rejected", 1L, 0L), (consolidations.FilesWithheld.Single().Reason, consolidations.FilesWithheld.Single().Files, consolidations.FilesWithheld.Single().Pdf));

        // A rights disposition is counted under its own wire name, whatever it is.
        Assert.AreEqual("non_admitting_licence_scl", ClassOf(lu, "rgd", "as_published").FilesWithheld.Single().Reason);
        var rgdConsolidations = ClassOf(lu, "rgd", "consolidation");
        Assert.AreEqual("conflict", rgdConsolidations.FilesWithheld.Single().Reason);
        CollectionAssert.AreEquivalent(new Dictionary<string, long> { [V3TextYield.NotHeldLicenceScl] = 1 }, Counts(rgdConsolidations.VersionsNotHeld));

        // The publisher: versions, files and articles are its classes' sums; its acts are distinct (the act of a1 is one work in
        // its as-published and its consolidated classes).
        Assert.AreEqual(lu.Classes.Sum(static row => row.Versions), lu.Versions);
        Assert.AreEqual(7L, lu.Articles);
        Assert.AreEqual(11L, lu.Files);
        Assert.AreEqual(2L, lu.FilesWithText);
        Assert.AreEqual(lu.Classes.Sum(static row => row.Works) - 2, lu.Works, "loi/2020/01/01/a1 and rgd/2020/01/01/a1 are each one act in two classes");
        foreach (var row in lu.Classes)
        {
            Assert.AreEqual(row.Files, row.FilesWithText + row.FilesWithheld.Sum(static withheld => withheld.Files) + (row.FilesPdfOnly ?? 0) + row.FilesParseFailed.Sum(static failed => failed.Count),
                $"{row.Family}/{row.Kind}: each held file in exactly one bucket");
        }
    }

    [TestMethod]
    public void EveryRightsDispositionIsCountedUnderItsOwnNameSoANewOneNeedsNoChange()
    {
        var root = Eli + "loi/2020/01/01/a1/consolide/20210101";
        foreach (var disposition in Enum.GetValues<LuxembourgRightsChannelDisposition>().Where(static value => value != LuxembourgRightsChannelDisposition.AgreedSameRunCcBy))
        {
            var yieldOf = V3TextYield.Count([Lu(root, "xml", disposition, LexCorpus6Stage3Disposition.AknAdmitted)],
                new V3TextYield.LuxembourgInputs(new Dictionary<string, long>(), [root], []), null);
            Assert.AreEqual(ContractWire.NameOf(disposition), ClassOf(yieldOf.Publishers.Single(), "loi", "consolidation").FilesWithheld.Single().Reason);
        }
    }

    [TestMethod]
    public void EveryEuClassAndReasonIsCountedWithItsAnnexes()
    {
        var originalServed = Eu(EuContentClass.OriginalLegalText, LexCorpus6Stage3Disposition.FormexMainBodyAdmitted,
            LexCorpus6Stage3Disposition.AnnexTextNotAvailable, LexCorpus6Stage3Disposition.AnnexMappingUnresolved, LexCorpus6Stage3Disposition.AnnexUnclassified);
        var originalFailed = Eu(EuContentClass.OriginalLegalText, LexCorpus6Stage3Disposition.FormexMainBodyPackageNotAcquired, LexCorpus6Stage3Disposition.AnnexUnclassified);
        var consolidationServed = Eu(EuContentClass.Consolidation, LexCorpus6Stage3Disposition.FormexMainBodyAdmitted);
        var consolidationFailed = Eu(EuContentClass.Consolidation, LexCorpus6Stage3Disposition.FormexMainBodyMissing);
        var refused = new LexCorpus6Member(PublisherId.EuEurLex, Digest("refused"), _ordinal++, Evidence, Evidence, ScopeDisposition.TypedQuarantine,
            LexCorpus6OutcomeKind.Unavailable, null, null, null, null, null, [], ["typed_quarantine"]);
        var states = new[]
        {
            new V3TextYield.EuropeState("32016R0679", "work:gdpr", true, null),
            new V3TextYield.EuropeState("32016R0679", "work:gdpr-2018", false, "2018-05-25"),
            new V3TextYield.EuropeState("32016R0679", "work:gdpr-2018-de", false, "2018-05-25"),
            new V3TextYield.EuropeState("32016R0679", "work:gdpr-undated", false, null),
            new V3TextYield.EuropeState("32014L0065", "work:mifid", true, null),
        };
        var yieldOf = V3TextYield.Count(
            [originalServed, originalFailed, consolidationServed, consolidationFailed, refused],
            null,
            new V3TextYield.EuropeInputs(
                new Dictionary<string, long> { [originalServed.ObjectRefSha256] = 99, [consolidationServed.ObjectRefSha256] = 97 },
                new Dictionary<string, long> { ["work:gdpr"] = 99, ["work:gdpr-2018"] = 97 },
                states));

        var eu = yieldOf.Publishers.Single();
        Assert.AreEqual(("eu-eurlex", 5L, 2L, 4L, 2L, 196L, 4L, 2L), (eu.Publisher, eu.Works, eu.WorksWithText, eu.Versions, eu.VersionsWithText, eu.Articles, eu.Files, eu.FilesWithText),
            "a version is the seed's original wording or one consolidation date, an undated consolidated work a version of its own; the unavailable member is no file");
        var original = ClassOf(eu, V3TextYield.EuropeFamily, "original_legal_text");
        Assert.AreEqual((2L, 1L, 2L, 1L, 99L, 2L, 1L), (original.Works, original.WorksWithText, original.Versions, original.VersionsWithText, original.Articles, original.Files, original.FilesWithText));
        Assert.IsNull(original.VersionsNotHeld, "the EU index cannot tell a version of which no file is held");
        Assert.IsNull(original.FilesPdfOnly, "the corpus records no EU file's format, so no EU file is counted as PDF only");
        Assert.HasCount(0, original.FilesWithheld);
        CollectionAssert.AreEquivalent(new Dictionary<string, long> { ["formex_main_body_package_not_acquired"] = 1 }, Counts(original.FilesParseFailed));
        Assert.AreEqual((4L, 0L, 1L, 3L), (original.Annexes!.Annexes, original.Annexes.WithText, original.Annexes.ImageOnly, original.Annexes.Failed));
        CollectionAssert.AreEquivalent(new Dictionary<string, long>
        {
            ["annex_mapping_unresolved"] = 1,
            ["annex_text_not_available"] = 1,
            ["annex_unclassified"] = 2,
        }, Counts(original.Annexes.ByDisposition));

        var consolidation = ClassOf(eu, V3TextYield.EuropeFamily, "consolidation");
        Assert.AreEqual((3L, 1L, 2L, 1L, 97L, 2L, 1L), (consolidation.Works, consolidation.WorksWithText, consolidation.Versions, consolidation.VersionsWithText, consolidation.Articles, consolidation.Files, consolidation.FilesWithText));
        CollectionAssert.AreEquivalent(new Dictionary<string, long> { ["formex_main_body_missing"] = 1 }, Counts(consolidation.FilesParseFailed));
        Assert.AreEqual((0L, 0L, 0L, 0L), (consolidation.Annexes!.Annexes, consolidation.Annexes.WithText, consolidation.Annexes.ImageOnly, consolidation.Annexes.Failed),
            "an EU class whose files record no annex outcome counts none, and says it counted");
    }

    [TestMethod]
    public void NothingHeldAndNothingCataloguedCountsZeroAndAClassOfNothingButCatalogueStatesCountsOnlyThem()
    {
        var empty = V3TextYield.Count([], new V3TextYield.LuxembourgInputs(new Dictionary<string, long>(), [], []),
            new V3TextYield.EuropeInputs(new Dictionary<string, long>(), new Dictionary<string, long>(), []));
        Assert.HasCount(2, empty.Publishers);
        foreach (var publisher in empty.Publishers)
        {
            Assert.AreEqual((0L, 0L, 0L, 0L, 0L, 0L, 0L, 0), (publisher.Works, publisher.WorksWithText, publisher.Versions, publisher.VersionsWithText, publisher.Articles, publisher.Files, publisher.FilesWithText, publisher.Classes.Count));
        }

        Assert.HasCount(0, V3TextYield.Count([], null, null).Publishers, "a publisher whose index is not mounted is not reported");

        var state = Eli + "rgd/2020/01/01/a1/consolide/20210101";
        var onlyCatalogue = V3TextYield.Count([], new V3TextYield.LuxembourgInputs(new Dictionary<string, long>(), [state], []), null);
        var row = onlyCatalogue.Publishers.Single().Classes.Single();
        Assert.AreEqual((1L, 0L, 1L, 0L, 0L, 0L, 0L, 0L), (row.Works, row.WorksWithText, row.Versions, row.VersionsWithText, row.Articles, row.Files, row.FilesWithText, row.FilesPdfOnly!.Value));
        Assert.HasCount(0, row.FilesWithheld);
        Assert.HasCount(0, row.FilesParseFailed);
        CollectionAssert.AreEquivalent(new Dictionary<string, long> { [V3TextYield.NotHeldNoExpression] = 1 }, Counts(row.VersionsNotHeld));
    }

    [TestMethod]
    public void TheCountIsTheSameInWhateverOrderItsRowsComeAndItsClassesAreInOrdinalOrder()
    {
        // A work the census lists under two seeds gives text to both seeds' versions, whichever row comes first.
        var served = Eu(EuContentClass.Consolidation, LexCorpus6Stage3Disposition.FormexMainBodyAdmitted);
        var pdf = Eu(EuContentClass.Consolidation, LexCorpus6Stage3Disposition.FormexMainBodyNotEligible);
        var states = new[]
        {
            new V3TextYield.EuropeState("32016R0679", "work:shared", false, "2018-05-25"),
            new V3TextYield.EuropeState("32014L0065", "work:shared", false, "2018-05-25"),
            new V3TextYield.EuropeState("32014L0065", "work:other", false, "2019-01-01"),
        };
        string Written(IEnumerable<V3TextYield.EuropeState> rows, IReadOnlyList<LexCorpus6Member> members) =>
            JsonSerializer.Serialize(V3TextYield.Count(members, null, new V3TextYield.EuropeInputs(
                new Dictionary<string, long> { [served.ObjectRefSha256] = 5 },
                new Dictionary<string, long> { ["work:shared"] = 5 },
                rows.ToArray())), V3TextYield.JsonOptions);
        var forward = Written(states, [served, pdf]);
        Assert.AreEqual(forward, Written(states.Reverse(), [pdf, served]), "the same rows in another order write the same block");
        var row = V3TextYield.Read(JsonDocument.Parse(forward).RootElement).Publishers.Single().Classes.Single();
        Assert.AreEqual((3L, 2L, 2L, 1L), (row.Versions, row.VersionsWithText, row.Works, row.WorksWithText),
            "the shared work's text is both its seeds' versions'");
        // An EU file with no Formex main body (a PDF, as 02003R0001-20040501's are) is counted by its own disposition.
        CollectionAssert.AreEquivalent(new Dictionary<string, long> { ["formex_main_body_not_eligible"] = 1 }, Counts(row.FilesParseFailed));
        Assert.IsNull(row.FilesPdfOnly);

        // Luxembourg classes by family and kind in ordinal order, where a culture's order would differ ("Zz" before "aa").
        var upper = Eli + "Zz/2020/01/01/a1/jo";
        var lower = Eli + "aa/2020/01/01/a1/jo";
        var luxembourg = V3TextYield.Count([], new V3TextYield.LuxembourgInputs(new Dictionary<string, long>(), [lower, upper], []), null);
        CollectionAssert.AreEqual(new[] { "Zz", "aa" }, luxembourg.Publishers.Single().Classes.Select(static yieldRow => yieldRow.Family).ToArray());
    }

    [TestMethod]
    public void AnArticleOfNoHeldFileOrOfNoStateRefusesTheCount()
    {
        Assert.ThrowsExactly<InvalidDataException>(() => V3TextYield.Count([],
            new V3TextYield.LuxembourgInputs(new Dictionary<string, long> { [new string('a', 64)] = 3 }, [], []), null));
        var served = Eu(EuContentClass.Consolidation, LexCorpus6Stage3Disposition.FormexMainBodyAdmitted);
        Assert.ThrowsExactly<InvalidDataException>(() => V3TextYield.Count([served], null,
            new V3TextYield.EuropeInputs(new Dictionary<string, long> { [served.ObjectRefSha256] = 2 }, new Dictionary<string, long> { ["work:none"] = 2 }, [])));
        Assert.ThrowsExactly<InvalidDataException>(() => V3TextYield.Count([], null,
            new V3TextYield.EuropeInputs(new Dictionary<string, long> { [new string('b', 64)] = 2 }, new Dictionary<string, long> { ["work:a"] = 2 },
                [new V3TextYield.EuropeState("32016R0679", "work:a", true, null)])));
    }

    /// <summary>A small yield of both publishers, as the count gives it.</summary>
    private static V3TextYield Sample()
    {
        var root = Eli + "loi/2020/01/01/a1/jo";
        var held = Lu(root, "xml", LuxembourgRightsChannelDisposition.AgreedSameRunCcBy, LexCorpus6Stage3Disposition.AknAdmitted);
        var withheld = Lu(Eli + "loi/2020/01/02/a2/jo", "pdf", LuxembourgRightsChannelDisposition.TypedQuarantineInFileReadingRejected);
        var eu = Eu(EuContentClass.OriginalLegalText, LexCorpus6Stage3Disposition.FormexMainBodyAdmitted, LexCorpus6Stage3Disposition.AnnexTextNotAvailable);
        return V3TextYield.Count([held, withheld, eu],
            new V3TextYield.LuxembourgInputs(new Dictionary<string, long> { [held.ObjectRefSha256] = 4 }, [root, Eli + "loi/2020/01/03/a3/jo"], []),
            new V3TextYield.EuropeInputs(new Dictionary<string, long> { [eu.ObjectRefSha256] = 3 }, new Dictionary<string, long> { ["work:a"] = 3 },
                [new V3TextYield.EuropeState("32016R0679", "work:a", true, null)]));
    }

    [TestMethod]
    public void TheBlockRoundTripsAndIsHeldToItsArithmetic()
    {
        var sample = Sample();
        var written = JsonSerializer.SerializeToNode(sample, V3TextYield.JsonOptions)!;
        using (var document = JsonDocument.Parse(written.ToJsonString()))
        {
            Assert.AreEqual(written.ToJsonString(), JsonSerializer.SerializeToNode(V3TextYield.Read(document.RootElement), V3TextYield.JsonOptions)!.ToJsonString());
        }

        Assert.AreEqual(V3TextYield.SchemaId, written["schema"]!.GetValue<string>());
        var luClass = written["publishers"]![0]!["classes"]![0]!.AsObject();
        CollectionAssert.AreEqual(
            new[] { "family", "kind", "works", "works_with_text", "versions", "versions_with_text", "articles", "versions_not_held", "files", "files_with_text", "files_withheld", "files_pdf_only", "files_parse_failed", "annexes" },
            luClass.Select(static pair => pair.Key).ToArray(), "the class's members, in their order, snake_case");

        // Each relation the block states, broken once: the reader refuses it.
        foreach (var (rule, tamper) in new (string, Action<JsonNode>)[]
        {
            ("schema", static node => node["schema"] = "lex-v3-text-yield/0"),
            ("publisher", static node => node["publishers"]![0]!["publisher"] = "xx-unknown"),
            ("class files", static node => node["publishers"]![0]!["classes"]![0]!["files"] = 9),
            ("withheld pdf", static node => node["publishers"]![0]!["classes"]![0]!["files_withheld"]![0]!["pdf"] = 2),
            ("versions with text", static node => node["publishers"]![0]!["classes"]![0]!["versions_with_text"] = 9),
            ("not held", static node => node["publishers"]![0]!["classes"]![0]!["versions_not_held"]![0]!["count"] = 5),
            ("a reason listed twice", static node => node["publishers"]![0]!["classes"]![0]!["files_withheld"]!.AsArray().Add(node["publishers"]![0]!["classes"]![0]!["files_withheld"]![0]!.DeepClone())),
            ("a zero row", static node => node["publishers"]![0]!["classes"]![0]!["versions_not_held"]![0]!["count"] = 0),
            ("publisher versions", static node => node["publishers"]![0]!["versions"] = 7),
            ("publisher articles", static node => node["publishers"]![1]!["articles"] = 4),
            ("publisher works", static node => node["publishers"]![0]!["works"] = 9),
            ("annexes", static node => node["publishers"]![1]!["classes"]![0]!["annexes"]!["failed"] = 1),
            ("an unknown member", static node => node["publishers"]![0]!["more"] = 1),
            ("a missing member", static node => node["publishers"]![0]!.AsObject().Remove("files")),
            ("a null list", static node => node["publishers"]![0]!["classes"]![0]!["files_withheld"] = null),
        })
        {
            var broken = written.DeepClone();
            tamper(broken);
            using var document = JsonDocument.Parse(broken.ToJsonString());
            Assert.ThrowsExactly<InvalidDataException>(() => V3TextYield.Read(document.RootElement), rule);
        }
    }

    // ---- The coverage answer: the build's figures, or none, from the report beside the mount. ----

    internal static async Task<JsonElement> CoverageOfAsync(V3CorpusMount mount)
    {
        var bytes = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new { operation_id = "coverage", parameters = new { } }));
        var context = new DefaultHttpContext { TraceIdentifier = "text-yield" };
        context.Request.Method = HttpMethods.Post;
        context.Request.Body = new MemoryStream(bytes);
        context.Request.ContentLength = bytes.Length;
        context.Features.Get<IHttpRequestFeature>()!.RawTarget = V3RestRouteBinding.Coverage.RawTarget;
        context.Response.Body = new MemoryStream();
        await new V3ApiHandler(SyntheticApiState.Unavailable, new V3PlatformHost(), static () => ObservedAt, mount).HandleAsync(context, CancellationToken.None);
        Assert.AreEqual(StatusCodes.Status200OK, context.Response.StatusCode, Encoding.UTF8.GetString(ResponseBytes(context)));
        var envelope = V3EnvelopeJson.ParseAndVerify(ResponseBytes(context), V3OperationRegistry.Reviewed);
        return envelope.Result!.Value.Clone();
    }

    /// <summary>A report in the shape the mount writer writes, naming the fixture's corpus and index, with the block given.</summary>
    internal static string ReportWith(MountedFixture fixture, JsonNode? textYield, string schema = V3CorpusMountWriter.ReportSchema)
    {
        var report = new JsonObject
        {
            ["schema"] = schema,
            ["corpus"] = new JsonObject { ["resourceId"] = LexCorpus6Builder.ResourceIdOf(fixture.CorpusSha256), ["Sha256"] = fixture.CorpusSha256 },
            ["luxembourgIndex"] = new JsonObject { ["resourceId"] = LexCorpus6Builder.ResourceIdOf(fixture.IndexSha256), ["Sha256"] = fixture.IndexSha256 },
        };
        if (textYield is not null)
        {
            report["textYield"] = textYield;
        }

        return report.ToJsonString();
    }

    /// <summary>The fixture mount's own yield, counted as the mount writer counts it.</summary>
    internal static V3TextYield FixtureYield(MountedFixture fixture) =>
        V3TextYield.FromMount(
            VerifiedLexCorpus6ManifestSet.ParseCanonicalAndVerify(fixture.CorpusBytes).Set.Members,
            Path.Combine(fixture.Directory, V3CorpusMount.IndexFileName),
            null);

    /// <summary>
    /// Writes beside the fixture mount the report a build writes of its yield, and nothing else the server reads from a
    /// report (no commit, no catalog time), so every other answer over the mount is what it was: the census's mounts.
    /// </summary>
    internal static Task WriteYieldReportAsync(MountedFixture fixture) =>
        File.WriteAllTextAsync(
            Path.Combine(fixture.Directory, V3CorpusMount.BuildReportFileName),
            ReportWith(fixture, JsonSerializer.SerializeToNode(FixtureYield(fixture), V3TextYield.JsonOptions)));

    [TestMethod]
    public async Task AMountWithoutTheFiguresAnswersAsBeforeWithNoYieldAndSaysWhy()
    {
        var fixture = await MountedFixture.CreateAsync();
        await using var cleanup = fixture;
        foreach (var report in new string?[] { null, "{}", ReportWith(fixture, null, "lex-v3-first-mount-report/1") })
        {
            var path = Path.Combine(fixture.Directory, V3CorpusMount.BuildReportFileName);
            File.Delete(path);
            if (report is not null)
            {
                await File.WriteAllTextAsync(path, report);
            }

            using var mount = await V3CorpusMount.OpenAsync(fixture.Directory, CancellationToken.None);
            var answer = await CoverageOfAsync(mount!);
            Assert.AreEqual(JsonValueKind.Null, answer.GetProperty("text_yield").ValueKind, report ?? "no report");
            Assert.AreEqual(V3CorpusMount.TextYieldAbsentNote, answer.GetProperty("text_yield_note").GetString());
            Assert.AreEqual(V3CorpusMount.CoverageScope, answer.GetProperty("scope").GetString(), "the scope is the one it was");
            var universe = answer.GetProperty("not_held").EnumerateArray().Single(static row => row.GetProperty("item").GetString() == "publisher_universe");
            Assert.AreEqual(V3CorpusMount.CoverageNotHeld[0][1], universe.GetProperty("reason").GetString());
        }
    }

    [TestMethod]
    public async Task AMountWhoseBuildReportedItsYieldServesExactlyThoseFigures()
    {
        var fixture = await MountedFixture.CreateAsync();
        await using var cleanup = fixture;
        var counted = FixtureYield(fixture);
        var block = JsonSerializer.SerializeToNode(counted, V3TextYield.JsonOptions)!;
        await File.WriteAllTextAsync(Path.Combine(fixture.Directory, V3CorpusMount.BuildReportFileName), ReportWith(fixture, block));
        using var mount = await V3CorpusMount.OpenAsync(fixture.Directory, CancellationToken.None);
        var answer = await CoverageOfAsync(mount!);

        // The envelope is canonical JSON (its members sorted), so the block is compared as a tree.
        Assert.IsTrue(JsonNode.DeepEquals(block, JsonNode.Parse(answer.GetProperty("text_yield").GetRawText())), "the block is served as the build reported it");
        Assert.AreEqual(V3CorpusMount.TextYieldNote, answer.GetProperty("text_yield_note").GetString());
        Assert.AreEqual(V3CorpusMount.CoverageScopeWithTextYield, answer.GetProperty("scope").GetString());
        var universe = answer.GetProperty("not_held").EnumerateArray().Single(static row => row.GetProperty("item").GetString() == "publisher_universe");
        Assert.AreEqual(V3CorpusMount.CoveragePublisherUniverseWithTextYield[1], universe.GetProperty("reason").GetString());

        // The figures are the mount's: the fixture's one held file, its articles and its class.
        var lu = counted.Publishers.Single();
        Assert.AreEqual("lu-legilux", lu.Publisher);
        Assert.AreEqual(answer.GetProperty("totals").GetProperty("articles").GetInt64(), lu.Articles);
        Assert.AreEqual((1L, 1L, 1L, 1L), (lu.Files, lu.FilesWithText, lu.VersionsWithText, lu.WorksWithText));
    }

    [TestMethod]
    public async Task AYieldThatDoesNotHoldToTheMountRefusesIt()
    {
        var fixture = await MountedFixture.CreateAsync();
        await using var cleanup = fixture;
        var block = JsonSerializer.SerializeToNode(FixtureYield(fixture), V3TextYield.JsonOptions)!;

        // Consistent in itself, and not this mount's: one more article in the class and the publisher.
        var other = block.DeepClone();
        var row = other["publishers"]![0]!["classes"]![0]!;
        row["articles"] = row["articles"]!.GetValue<long>() + 1;
        other["publishers"]![0]!["articles"] = other["publishers"]![0]!["articles"]!.GetValue<long>() + 1;
        // An EU publisher on a mount with no EU index.
        var europe = block.DeepClone();
        europe["publishers"]!.AsArray().Add(new JsonObject
        {
            ["publisher"] = "eu-eurlex", ["works"] = 0, ["works_with_text"] = 0, ["versions"] = 0, ["versions_with_text"] = 0,
            ["articles"] = 0, ["files"] = 0, ["files_with_text"] = 0, ["classes"] = new JsonArray(),
        });
        // Inconsistent in itself.
        var broken = block.DeepClone();
        broken["publishers"]![0]!["files"] = 99;
        foreach (var (what, tampered) in new[] { ("another mount's articles", other), ("an unmounted publisher", europe), ("its own arithmetic", broken) })
        {
            await File.WriteAllTextAsync(Path.Combine(fixture.Directory, V3CorpusMount.BuildReportFileName), ReportWith(fixture, tampered));
            await Assert.ThrowsExactlyAsync<InvalidDataException>(() => V3CorpusMount.OpenAsync(fixture.Directory, CancellationToken.None), what);
        }
    }
}
