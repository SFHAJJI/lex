using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Lex.V3.Contracts.Source.Core;
using Lex.V3.Ingest.Luxembourg;
using Microsoft.Data.Sqlite;
using static Lex.V3.Ingest.Tests.LuxembourgIndexBuilderTests;

namespace Lex.V3.Ingest.Tests;

/// <summary>
/// Index 9's catalogue states (design §3.1, §3.4) over a multi-body fixture: several works and many bodies, built through
/// the builder's own projection (<see cref="LuxembourgIndexBuilder.ProjectCatalogueStates"/>) and writer
/// (<see cref="LuxembourgIndexBuilder.WriteIndex"/>), the code path an envelope's build runs after it has read its rows, and
/// opened by the strict reader. The single-body helper builds one acquisition per index; this harness states each body's
/// member, record, inventory and content outcome and the catalogue's facts directly. Every value is synthetic: no
/// publisher byte, and none of the three withheld fixtures.
/// </summary>
[TestClass]
public sealed class LuxembourgIndexCatalogueStatesTests
{
    internal const string Eli = "http://data.legilux.public.lu/eli/etat/leg/";
    private const string Fra = "http://publications.europa.eu/resource/authority/language/FRA";
    private const string Eng = "http://publications.europa.eu/resource/authority/language/ENG";
    private const string BuiltAtText = "2026-10-12T18:17:05Z";
    private const string DerivedAtText = "2026-10-17T09:00:00Z";

    /// <summary>
    /// The harness: bodies, members, records, articles and catalogue facts, stated directly, and built through the builder's
    /// projection and writer. A body's object reference and body digest are derived from its legal resource.
    /// </summary>
    internal sealed class Harness
    {
        private readonly List<LuxembourgIndexBuilder.WorkFactRow> _facts = [];
        private readonly List<LuxembourgIndexBuilder.MemberRow> _members = [];
        private readonly List<LuxembourgIndexBuilder.ArticleRow> _articles = [];
        private readonly List<LuxembourgIndexBuilder.CatalogueBody> _bodies = [];
        private readonly List<LuxembourgIndexBuilder.CatalogueRecord> _records = [];
        private readonly Dictionary<string, string> _bodyDigests = new(StringComparer.Ordinal);

        internal static string Digest(string value) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(value)));

        internal static string ObjectRefOf(string root) => Digest("object:" + root);

        internal static string BodyOf(string root) => Digest("body:" + root);

        internal Harness Fact(string subject, string predicate, string value)
        {
            var iri = value.StartsWith("http", StringComparison.Ordinal);
            _facts.Add(new(subject, predicate, "fixture", iri ? "iri" : "literal", value,
                iri ? "" : "http://www.w3.org/2001/XMLSchema#date", "", Digest("evidence:" + subject + predicate + value)));
            return this;
        }

        /// <summary>A catalogue state's own facts: its date, end, entry into force, publication date, membership and expressions.</summary>
        internal Harness State(
            string root, string? applicability = null, string? end = null, string? entryIntoForce = null, string? publication = null,
            string? memberOf = null, params (string Leaf, string Language)[] expressions)
        {
            if (applicability is not null) Fact(root, "dateApplicability", applicability);
            if (end is not null) Fact(root, "dateEndApplicability", end);
            if (entryIntoForce is not null) Fact(root, "dateEntryInForce", entryIntoForce);
            if (publication is not null) Fact(root, "publicationDate", publication);
            if (memberOf is not null) Fact(root, "isMemberOf", memberOf);
            foreach (var (leaf, language) in expressions)
            {
                Fact(root, "isRealizedBy", root + "/" + leaf);
                Fact(root + "/" + leaf, "language", language);
            }

            return this;
        }

        /// <summary>A held body: acquired, admitted, inventoried, with the given main-body articles of its selected expression.</summary>
        internal Harness Held(string root, string[] articleIds, string leaf = "fr", string language = "fra", string? inFileDate = null, string identityClass = "own")
        {
            var objectRef = ObjectRefOf(root);
            Body(root, leaf, language, "acquired", "agreed_same_run_cc_by", "inventoried", null, inFileDate, identityClass, [], [new("admitted", articleIds.Length)]);
            foreach (var id in articleIds)
            {
                _articles.Add(new(
                    Digest("article:" + root + "/" + leaf + "#" + id), objectRef, root + "/" + leaf, id, null, null, language,
                    new string('c', 64), "texte de " + id, "[]"));
            }

            return this;
        }

        /// <summary>A held body whose rights are withheld (a conflict): its bytes held, its text not admitted.</summary>
        internal Harness Withheld(string root, string leaf = "fr", string? inFileDate = null) =>
            Body(root, leaf, "fra", "rights_withheld", "typed_quarantine_in_file_reading_rejected", "inventoried", null, inFileDate, "template_empty", [], []);

        /// <summary>A held body that is a PDF: acquired and admitted, not Akoma Ntoso.</summary>
        internal Harness Pdf(string root, string leaf = "fr") =>
            Body(root, leaf, "fra", "acquired", "agreed_same_run_cc_by", "not_akn", "pdf", null, null, [], []);

        /// <summary>A held body the inventory refused as another state's file, naming the check that fired.</summary>
        internal Harness Misfiled(string root, string finding, string leaf = "fr") =>
            Body(root, leaf, "fra", "acquired", "agreed_same_run_cc_by", "publisher_file_names_other_state", "content consistency: " + finding.Split(':')[0],
                null, "same_work_other_state", [finding], []);

        /// <summary>A corpus record with no held body (its body axis not acquired).</summary>
        internal Harness Unavailable(string root, string bodyDisposition = "typed_quarantine")
        {
            var objectRef = ObjectRefOf(root);
            _members.Add(new(objectRef, _members.Count, "unavailable", null, "[]", JsonSerializer.Serialize(new[] { bodyDisposition })));
            _records.Add(new(root, objectRef, "unavailable", bodyDisposition));
            return this;
        }

        private Harness Body(
            string root, string leaf, string language, string outcome, string rights, string inventory, string? detail,
            string? inFileDate, string? identityClass, string[] findings, KeyValuePair<string, long>[] content)
        {
            var objectRef = ObjectRefOf(root);
            _members.Add(new(objectRef, _members.Count, outcome, rights, "[]", "[]"));
            _records.Add(new(root, objectRef, outcome, "accepted_selected"));
            _bodies.Add(new(objectRef, root, root + "/" + leaf, language, outcome, rights, inventory, detail, new string('5', 64),
                identityClass, inFileDate, findings, content));
            _bodyDigests[objectRef] = BodyOf(root);
            return this;
        }

        internal LuxembourgIndexBuilder.WorkFactRow[] Facts => _facts
            .Distinct()
            .OrderBy(static row => row.SubjectIri, StringComparer.Ordinal)
            .ThenBy(static row => row.Predicate, StringComparer.Ordinal)
            .ThenBy(static row => row.ObjectKind, StringComparer.Ordinal)
            .ThenBy(static row => row.ObjectValue, StringComparer.Ordinal)
            .ThenBy(static row => row.DatatypeIri, StringComparer.Ordinal)
            .ThenBy(static row => row.LanguageTag, StringComparer.Ordinal)
            .ThenBy(static row => row.EvidenceSha256, StringComparer.Ordinal)
            .ToArray();

        internal LuxembourgIndexBuilder.ArticleRow[] Articles =>
            _articles.OrderBy(static row => row.ArticleIdentitySha256, StringComparer.Ordinal).ToArray();

        internal (LuxembourgIndexBuilder.StateRow[] States, LuxembourgIndexBuilder.IntervalRow[] Intervals) Project(string builtAt = BuiltAtText) =>
            LuxembourgIndexBuilder.ProjectCatalogueStates(Articles, LuxembourgIndexBuilder.CatalogueFacts.From(Facts), _bodies, _records, builtAt[..10]);

        internal static SourceArtifactRef CorpusRef { get; } = new(LexCorpus6Builder.ResourceIdOf(new string('c', 64)), new string('c', 64));

        internal LuxembourgIndexBuildResult Build(string builtAt = BuiltAtText, string? derivedAt = DerivedAtText, LuxembourgIndexPredecessor? predecessor = null)
        {
            var (states, intervals) = Project(builtAt);
            var built = LuxembourgIndexBuilder.WriteIndex(
                CorpusRef,
                _members.OrderBy(static row => row.ObjectRefSha256, StringComparer.Ordinal).ToArray(),
                Articles, states, intervals, [], Facts, _bodyDigests, predecessor, builtAt, derivedAt, null, out var refusal, out var detail);
            Assert.IsNotNull(built, $"{refusal}: {detail}");
            return built;
        }
    }

    internal static string W(string path) => Eli + path;

    /// <summary>
    /// The fixture of design §3.4: a code (its base act named by isMemberOf, an accented article id, a shared-file pair, a
    /// PDF state and a catalogued state with no expression); an act dated by its entry into force with a later withheld
    /// consolidation and a hole; an act whose file's applicability is not its entry into force; an act and its same-date
    /// consolidation, then a state not acquired; an end before its start and an overlap; a suspected inclusive end; an
    /// English translation; and a work with no held state.
    /// </summary>
    internal static Harness Fixture()
    {
        var h = new Harness();
        // 1. Code civil: keyed by the code; its states' isMemberOf names the base act, which is a fact and not its work.
        h.Fact(W("code/civil"), "rdf:type", "http://data.legilux.public.lu/resource/ontology/jolux#ComplexWork");
        h.State(W("code/civil/20160901"), "2016-09-01", "2017-04-01", publication: "2016-08-30", memberOf: W("loi/1803/03/08/n1"), expressions: ("fr", Fra))
            .Held(W("code/civil/20160901"), ["art_1", "art_141_à_144"]);
        h.State(W("code/civil/20170401"), "2017-04-01", "2018-04-01", memberOf: W("loi/1803/03/08/n1"), expressions: ("fr", Fra))
            .Held(W("code/civil/20170401"), ["art_1"], identityClass: "same_work_other_state");
        h.State(W("code/civil/20180401"), "2018-04-01", "2019-04-28", expressions: ("fr", Fra))
            .Misfiled(W("code/civil/20180401"), "shared_file:" + W("code/civil/20190428"));
        h.State(W("code/civil/20190428"), "2019-04-28", expressions: ("fr", Fra))
            .Misfiled(W("code/civil/20190428"), "shared_file:" + W("code/civil/20180401"));
        h.State(W("code/civil/20200101"), "2020-01-01", "2027-01-01", expressions: ("fr", Fra)).Pdf(W("code/civil/20200101"));
        h.State(W("code/civil/20270101"), "2027-01-01");

        // 2. An act with no file date, dated by its entry into force; a withheld consolidation, a hole, one not acquired.
        h.State(W("loi/1798/11/03/n1/jo"), entryIntoForce: "1798-11-07", publication: "1798-01-08", memberOf: W("loi/1798/11/03/n1"), expressions: ("fr", Fra))
            .Held(W("loi/1798/11/03/n1/jo"), ["art_1", "art_2"]);
        h.State(W("loi/1798/11/03/n1/consolide/20200101"), "2020-01-01", "2021-06-01", memberOf: W("loi/1798/11/03/n1"), expressions: ("fr", Fra))
            .Withheld(W("loi/1798/11/03/n1/consolide/20200101"), inFileDate: "2020-01-01");
        h.State(W("loi/1798/11/03/n1/consolide/20220101"), "2022-01-01", memberOf: W("loi/1798/11/03/n1"), expressions: ("fr", Fra));

        // 3. An act whose file applies it before its entry into force (derive-c's loi-2019-03-05-a129 shape).
        h.State(W("rgd/2019/03/05/a129/jo"), entryIntoForce: "2019-03-12", publication: "2019-03-08", expressions: ("fr", Fra))
            .Held(W("rgd/2019/03/05/a129/jo"), ["art_1"], inFileDate: "2019-01-01");

        // 4. An act and its consolidation on the same date, then a consolidation the catalogue lists and the run did not acquire.
        h.State(W("loi/2018/09/14/a883/jo"), entryIntoForce: "2018-09-18", memberOf: W("loi/2018/09/14/a883"), expressions: ("fr", Fra))
            .Held(W("loi/2018/09/14/a883/jo"), ["art_1"], inFileDate: "2018-09-14");
        h.State(W("loi/2018/09/14/a883/consolide/20180914"), "2018-09-14", "2020-07-01", memberOf: W("loi/2018/09/14/a883"), expressions: ("fr", Fra))
            .Held(W("loi/2018/09/14/a883/consolide/20180914"), ["art_1"], identityClass: "same_work_act");
        h.State(W("loi/2018/09/14/a883/consolide/20200701"), "2020-07-01", memberOf: W("loi/2018/09/14/a883"), expressions: ("fr", Fra))
            .Unavailable(W("loi/2018/09/14/a883/consolide/20200701"));

        // 5. An end before its start (invalid), then an overlap.
        h.State(W("rgd/2007/06/07/n2/consolide/20230717"), "2023-07-17", "2023-07-01", expressions: ("fr", Fra))
            .Held(W("rgd/2007/06/07/n2/consolide/20230717"), ["art_1"]);
        h.State(W("rgd/2007/06/07/n2/consolide/20240101"), "2024-01-01", "2024-12-31", expressions: ("fr", Fra))
            .Held(W("rgd/2007/06/07/n2/consolide/20240101"), ["art_1"]);
        h.State(W("rgd/2007/06/07/n2/consolide/20240601"), "2024-06-01", expressions: ("fr", Fra))
            .Held(W("rgd/2007/06/07/n2/consolide/20240601"), ["art_1"]);

        // 6. A suspected inclusive end: the end one day before the next start.
        h.State(W("loi/1996/11/07/n1/consolide/20200101"), "2020-01-01", "2022-12-31", expressions: ("fr", Fra))
            .Held(W("loi/1996/11/07/n1/consolide/20200101"), ["art_1"]);
        h.State(W("loi/1996/11/07/n1/consolide/20230101"), "2023-01-01", expressions: ("fr", Fra))
            .Held(W("loi/1996/11/07/n1/consolide/20230101"), ["art_1"]);

        // 7. An English translation is the body held; the French expression of the same act is catalogued and not acquired.
        h.State(W("loi/2017/07/20/a674/jo"), entryIntoForce: "2017-07-25", expressions: [("fr", Fra), ("en", Eng)])
            .Held(W("loi/2017/07/20/a674/jo"), ["art_1"], leaf: "en", language: "eng");

        // 8. A work with no held state: no row.
        h.State(W("loi/2000/01/01/n1/jo"), entryIntoForce: "2000-01-05", expressions: ("fr", Fra)).Unavailable(W("loi/2000/01/01/n1/jo"));
        return h;
    }

    /// <summary>One row rendered: its key, kind, role, entry into force, end and its source, boundary, text status, translation, provisional.</summary>
    private static string Render(LuxembourgIndexCatalogueState row) =>
        string.Join(' ', row.WorkKey, row.ApplicabilityDate, row.PublisherLegalResourceIri[Eli.Length..],
            row.Language.Length == 0 ? "-" : row.Language, row.StateKind, row.DateRole, "eif=" + (row.EntryIntoForce ?? "-"),
            "to=" + (row.ApplicableTo ?? "-") + "/" + (row.ApplicableToSource ?? "-"), row.Boundary, row.TextStatus,
            row.TranslationOf is null ? "-" : "translates " + row.TranslationOf[Eli.Length..], row.Provisional ? "provisional" : "-",
            "articles=" + row.ArticleIdentities.Count.ToString(CultureInfo.InvariantCulture));

    private static readonly string[] ExpectedRows =
    [
        "code-civil 2016-09-01 code/civil/20160901 fra code applicability eif=- to=2017-04-01/publisher half_open held - - articles=2",
        "code-civil 2017-04-01 code/civil/20170401 fra code applicability eif=- to=2018-04-01/publisher half_open held - - articles=1",
        "code-civil 2018-04-01 code/civil/20180401 fra code applicability eif=- to=2019-04-28/publisher half_open not_available_misfiled - - articles=0",
        "code-civil 2019-04-28 code/civil/20190428 fra code applicability eif=- to=2020-01-01/derived derived not_available_misfiled - - articles=0",
        "code-civil 2020-01-01 code/civil/20200101 fra code applicability eif=- to=2027-01-01/publisher half_open not_available_pdf - - articles=0",
        "code-civil 2027-01-01 code/civil/20270101 - code applicability eif=- to=-/- derived not_acquired - provisional articles=0",
        "loi-1798-11-03-n1 1798-11-07 loi/1798/11/03/n1/jo fra as_published entry_into_force eif=1798-11-07 to=2020-01-01/derived derived held - - articles=2",
        "loi-1798-11-03-n1 2020-01-01 loi/1798/11/03/n1/consolide/20200101 fra consolidation applicability eif=- to=2021-06-01/publisher half_open withheld_rights - - articles=0",
        "loi-1798-11-03-n1 2022-01-01 loi/1798/11/03/n1/consolide/20220101 fra consolidation applicability eif=- to=-/- derived not_acquired - - articles=0",
        "loi-1996-11-07-n1 2020-01-01 loi/1996/11/07/n1/consolide/20200101 fra consolidation applicability eif=- to=2022-12-31/publisher inclusive_suspected held - - articles=1",
        "loi-1996-11-07-n1 2023-01-01 loi/1996/11/07/n1/consolide/20230101 fra consolidation applicability eif=- to=-/- derived held - - articles=1",
        "loi-2017-07-20-a674 2017-07-25 loi/2017/07/20/a674/jo eng as_published entry_into_force eif=2017-07-25 to=-/- derived held translates loi/2017/07/20/a674/jo/fr - articles=1",
        "loi-2017-07-20-a674 2017-07-25 loi/2017/07/20/a674/jo fra as_published entry_into_force eif=2017-07-25 to=-/- derived not_acquired - - articles=0",
        "loi-2018-09-14-a883 2018-09-14 loi/2018/09/14/a883/consolide/20180914 fra consolidation applicability eif=- to=2020-07-01/publisher half_open held - - articles=1",
        "loi-2018-09-14-a883 2018-09-14 loi/2018/09/14/a883/jo fra as_published applicability_in_file eif=2018-09-18 to=2020-07-01/derived derived held - - articles=1",
        "loi-2018-09-14-a883 2020-07-01 loi/2018/09/14/a883/consolide/20200701 fra consolidation applicability eif=- to=-/- derived not_acquired - - articles=0",
        "rgd-2007-06-07-n2 2023-07-17 rgd/2007/06/07/n2/consolide/20230717 fra consolidation applicability eif=- to=2023-07-01/publisher invalid held - - articles=1",
        "rgd-2007-06-07-n2 2024-01-01 rgd/2007/06/07/n2/consolide/20240101 fra consolidation applicability eif=- to=2024-12-31/publisher half_open held - - articles=1",
        "rgd-2007-06-07-n2 2024-06-01 rgd/2007/06/07/n2/consolide/20240601 fra consolidation applicability eif=- to=-/- derived held - - articles=1",
        "rgd-2019-03-05-a129 2019-01-01 rgd/2019/03/05/a129/jo fra as_published applicability_in_file eif=2019-03-12 to=-/- derived held - - articles=1",
    ];

    private static LuxembourgIndexReader Open(LuxembourgIndexBuildResult built) =>
        LuxembourgIndexReader.OpenAndVerify(built.IndexRef, built.IndexBytes.Span, Harness.CorpusRef, built.CapabilityManifest);

    private static LuxembourgIndexCatalogueState[] AllRows(LuxembourgIndexReader reader) =>
        new[] { "code-civil", "loi-1798-11-03-n1", "loi-1996-11-07-n1", "loi-2017-07-20-a674", "loi-2018-09-14-a883", "rgd-2007-06-07-n2", "rgd-2019-03-05-a129", "loi-2000-01-01-n1" }
            .SelectMany(reader.ResolveCatalogueStates)
            .ToArray();

    /// <summary>
    /// Every catalogue state of a served work gets a row, held or textless, dated by its role (a consolidation or code
    /// state by the catalogue's applicability, an act by its file's applicability else the catalogue's entry into force,
    /// that entry into force kept beside the date), with the publisher's end or a derived one, the boundary its end makes
    /// with the next start, why no text is held, and a translation taking its French state's date and interval; a work
    /// with no held state gets none.
    /// </summary>
    [TestMethod]
    public void EveryCatalogueStateOfAServedWorkGetsARowDatedByItsRole()
    {
        var built = Fixture().Build();
        using var reader = Open(built);
        Assert.AreEqual(LuxembourgIndexBuilder.Schema, reader.SchemaIdentity);
        Assert.IsTrue(reader.RecordsCatalogueStates);
        var rows = AllRows(reader);
        CollectionAssert.AreEqual(ExpectedRows, rows.Select(Render).ToArray(), string.Join(Environment.NewLine, rows.Select(Render)));
        Assert.IsEmpty(reader.ResolveCatalogueStates("loi-2000-01-01-n1"), "a work with no held state has no row");

        // Why each textless row has no text, from the body, the record or the catalogue; the rights reason first.
        string Reason(string leaf) => rows.Single(row => row.PublisherLegalResourceIri == W(leaf) && row.Language != "eng").TextStatusReason!;
        Assert.AreEqual("content consistency: shared_file:" + W("code/civil/20190428"), Reason("code/civil/20180401"));
        Assert.AreEqual("not_akn: pdf", Reason("code/civil/20200101"));
        Assert.AreEqual("the catalogue names no expression of this state; not a corpus member", Reason("code/civil/20270101"));
        Assert.AreEqual("typed_quarantine_in_file_reading_rejected", Reason("loi/1798/11/03/n1/consolide/20200101"));
        Assert.AreEqual("not_a_corpus_member", Reason("loi/1798/11/03/n1/consolide/20220101"));
        Assert.AreEqual("not acquired; member unavailable, body typed_quarantine", Reason("loi/2018/09/14/a883/consolide/20200701"));
        Assert.AreEqual("another expression of this state was acquired: " + W("loi/2017/07/20/a674/jo/en"), Reason("loi/2017/07/20/a674/jo"));

        // What the file says stays as evidence beside the catalogue's date.
        var code = rows.Single(row => row.PublisherLegalResourceIri == W("code/civil/20160901"));
        Assert.AreEqual("own", code.InFileIdentity);
        Assert.AreEqual("2016-08-30", code.PublicationDate);
        CollectionAssert.Contains(reader.ResolveStateArticles(code.StateSha256).Select(static article => article.PublisherId).ToArray(), "art_141_à_144",
            "an accented publisher id is kept as written");
        var act = rows.Single(row => row.PublisherLegalResourceIri == W("rgd/2019/03/05/a129/jo"));
        Assert.AreEqual("2019-01-01", act.InFileDate, "the file's applicability is the date, by its role");
        Assert.AreEqual("2019-03-12", act.EntryIntoForce, "and the catalogue's entry into force is a separate value");
        Assert.AreEqual("applicability_in_file", act.DateRole);
        Assert.IsTrue(rows.Single(row => row.PublisherLegalResourceIri == W("code/civil/20270101")).ExpressionIri.Length == 0,
            "a state the catalogue names no expression of has an empty expression and language");
    }

    /// <summary>
    /// The interval transitions of each work's French chain, in chain order: contiguous ends, a derived end, a hole and an
    /// overlap with their spans, a suspected inclusive end, an end before its start, and a same-date pair; a translation
    /// is in no chain.
    /// </summary>
    [TestMethod]
    public void EachWorksChainHoldsItsTransitionsWithTheirKindsAndSpans()
    {
        var built = Fixture().Build();
        using var reader = Open(built);
        var byDigest = AllRows(reader).ToDictionary(static row => row.StateSha256, static row => row.PublisherLegalResourceIri[Eli.Length..], StringComparer.Ordinal);
        string Render(LuxembourgIndexInterval interval) =>
            $"{interval.WorkKey} {interval.Ordinal} {interval.Kind} {byDigest[interval.FromStateSha256]} -> {byDigest[interval.ToStateSha256]}" +
            (interval.SpanFrom is null ? "" : $" [{interval.SpanFrom}, {interval.SpanTo})");
        var intervals = new[] { "code-civil", "loi-1798-11-03-n1", "loi-1996-11-07-n1", "loi-2017-07-20-a674", "loi-2018-09-14-a883", "rgd-2007-06-07-n2", "rgd-2019-03-05-a129" }
            .SelectMany(reader.ResolveIntervals).Select(Render).ToArray();
        CollectionAssert.AreEqual(
            new[]
            {
                "code-civil 0 contiguous code/civil/20160901 -> code/civil/20170401",
                "code-civil 1 contiguous code/civil/20170401 -> code/civil/20180401",
                "code-civil 2 contiguous code/civil/20180401 -> code/civil/20190428",
                "code-civil 3 derived code/civil/20190428 -> code/civil/20200101",
                "code-civil 4 contiguous code/civil/20200101 -> code/civil/20270101",
                "loi-1798-11-03-n1 0 derived loi/1798/11/03/n1/jo -> loi/1798/11/03/n1/consolide/20200101",
                "loi-1798-11-03-n1 1 hole loi/1798/11/03/n1/consolide/20200101 -> loi/1798/11/03/n1/consolide/20220101 [2021-06-01, 2022-01-01)",
                "loi-1996-11-07-n1 0 inclusive_suspected loi/1996/11/07/n1/consolide/20200101 -> loi/1996/11/07/n1/consolide/20230101",
                "loi-2018-09-14-a883 0 same_date loi/2018/09/14/a883/jo -> loi/2018/09/14/a883/consolide/20180914",
                "loi-2018-09-14-a883 1 derived loi/2018/09/14/a883/jo -> loi/2018/09/14/a883/consolide/20200701",
                "loi-2018-09-14-a883 2 contiguous loi/2018/09/14/a883/consolide/20180914 -> loi/2018/09/14/a883/consolide/20200701",
                "rgd-2007-06-07-n2 0 invalid rgd/2007/06/07/n2/consolide/20230717 -> rgd/2007/06/07/n2/consolide/20240101",
                "rgd-2007-06-07-n2 1 overlap rgd/2007/06/07/n2/consolide/20240101 -> rgd/2007/06/07/n2/consolide/20240601 [2024-06-01, 2024-12-31)",
            },
            intervals,
            string.Join(Environment.NewLine, intervals));
    }

    /// <summary>
    /// Until the reader's interval rules land (S7a), every operation that existed before index 9 reads the held rows only,
    /// so a mount of index 9 answers as it would have: a work's states, the population, the coverage counts, the works a
    /// date reaches and the log folded at an observation hold no textless row; the catalogue states and intervals are read
    /// by their own methods.
    /// </summary>
    [TestMethod]
    public void TheOperationsBeforeIndexNineReadTheHeldRowsOnly()
    {
        var built = Fixture().Build();
        using var reader = Open(built);
        CollectionAssert.AreEqual(
            new[] { "2016-09-01", "2017-04-01" },
            reader.ResolveWorkStates("code-civil").Select(static state => state.ApplicabilityDate).ToArray(),
            "a code's held states, its textless rows not among them");
        Assert.IsEmpty(reader.ResolveState("loi-1798-11-03-n1", "2020-01-01"), "a withheld consolidation is not a state these operations serve");
        Assert.AreEqual(7, reader.ResolveStatePopulation().Works);
        CollectionAssert.AreEqual(new[] { "eng", "fra" }, reader.ResolveStatePopulation().Languages.ToArray(), "no empty language of a row naming no expression");
        var coverage = reader.ResolveCoverage();
        Assert.AreEqual(12L, coverage.States);
        Assert.AreEqual(7L, coverage.Works);
        Assert.IsEmpty(reader.ResolveStatesOfExpressions([W("code/civil/20180401/fr")]), "a misfiled state's expression has no held state");
        Assert.AreEqual(0, reader.ResolveHeldWorks([W("loi/2018/09/14/a883/consolide/20200701")]).Count, "a textless legal resource names no held work");
        Assert.AreEqual(1, reader.ResolveHeldWorks([W("loi/2018/09/14/a883/jo")]).Count);
        Assert.AreEqual(7L, reader.CountWorksWithStateOnOrBefore("2030-01-01", null));
        Assert.AreEqual(1L, reader.CountWorksWithStateOnOrBefore("1800-01-01", null), "the 1798 act, dated by its entry into force");
        var works = reader.ResolveWorkRecords(null, null, null, 20);
        Assert.AreEqual(2L, works.Single(static work => work.WorkKey == "code-civil").StateCount, "browse counts held states");
        var observed = reader.ResolveObservedStates("code-civil", reader.ResolveEventLog().LastSeq);
        Assert.HasCount(2, observed, "as_observed folds the held states of the log");
        CollectionAssert.AreEqual(
            new[] { ("code-civil", W("code/civil")) },
            reader.ResolveWorksUnder(W("code/")).ToArray(),
            "an exact reference's works under a prefix are the held works, once");
        Assert.IsEmpty(reader.ResolveWorksUnder(W("loi/2000/01/01/")), "a work with no held state is not a work an exact reference opens");
        Assert.HasCount(6, reader.ResolveCatalogueStates("code-civil"));
    }

    /// <summary>
    /// The genesis log holds one first_sighting per state row, held or textless, each carrying the end the publisher
    /// states (or none), the text status and the publication date, and the bodies a held state's articles were read from
    /// (none for a textless one); the observation records the derivation time beside the build time.
    /// </summary>
    [TestMethod]
    public void TheGenesisLogSightsEveryRowWithItsPublisherFactsAndTheObservationItsDerivationTime()
    {
        var built = Fixture().Build();
        using var reader = Open(built);
        var observation = reader.ResolveObservations().Single();
        Assert.AreEqual(BuiltAtText, observation.BuiltAt);
        Assert.AreEqual(DerivedAtText, observation.DerivedAt);
        var events = reader.ResolveEvents(0, null, 100);
        Assert.HasCount(20, events);
        Assert.IsTrue(events.All(static value => value.Event == "first_sighting"));
        var rows = AllRows(reader).ToDictionary(static row => row.StateSha256, StringComparer.Ordinal);
        foreach (var value in events)
        {
            var row = rows[value.StateSha256];
            using var detail = JsonDocument.Parse(value.DetailJson);
            var root = detail.RootElement;
            CollectionAssert.AreEqual(
                new[] { "state_sha256", "source_body_sha256", "applicable_to", "applicable_to_source", "text_status", "publication_date" },
                root.EnumerateObject().Select(static property => property.Name).ToArray());
            Assert.AreEqual(row.ApplicableToSource == "publisher" ? row.ApplicableTo : null, root.GetProperty("applicable_to").GetString(), "the publisher's end only");
            Assert.AreEqual(row.TextStatus, root.GetProperty("text_status").GetString());
            Assert.AreEqual(row.PublicationDate, root.GetProperty("publication_date").GetString());
            var bodies = root.GetProperty("source_body_sha256").EnumerateArray().Select(static body => body.GetString()).ToArray();
            CollectionAssert.AreEqual(
                row.TextStatus == "held" ? new[] { Harness.BodyOf(row.PublisherLegalResourceIri) } : Array.Empty<string>(),
                bodies, row.PublisherLegalResourceIri);
        }

        // An index with no derivation time records none.
        var acquiring = Fixture().Build(derivedAt: null);
        using var own = Open(acquiring);
        Assert.IsNull(own.ResolveObservations().Single().DerivedAt);
    }

    /// <summary>
    /// A chained build over the fixture that adds one later state emits interval_closed for the state whose end was derived
    /// and open, a first_sighting for the new state, and nothing for a state whose end the publisher states: no
    /// validity_revised for publisher ends that did not change.
    /// </summary>
    [TestMethod]
    public void AChainedBuildEmitsNoValidityRevisedForPublisherEndsThatDidNotChange()
    {
        var first = Fixture().Build();
        var predecessor = LuxembourgIndexPredecessor.TryRead(first.IndexRef, first.IndexBytes.Span, out var refusal, out var detail);
        Assert.IsNotNull(predecessor, $"{refusal}: {detail}");
        Assert.AreEqual(LuxembourgIndexBuilder.EventLogSchema, predecessor.LogSchema);
        var later = Fixture();
        later.State(W("loi/1996/11/07/n1/consolide/20240101"), "2024-01-01", expressions: ("fr", Fra))
            .Held(W("loi/1996/11/07/n1/consolide/20240101"), ["art_1"]);
        var chained = later.Build(builtAt: "2026-10-13T00:00:00Z", derivedAt: "2026-10-17T10:00:00Z", predecessor: predecessor);
        using var reader = Open(chained);
        var appended = reader.ResolveEvents(20, null, 100);
        CollectionAssert.AreEqual(
            new[] { "interval_closed loi-1996-11-07-n1 2023-01-01", "first_sighting loi-1996-11-07-n1 2024-01-01" },
            appended.Select(static value => $"{value.Event} {value.WorkKey} {value.ApplicabilityDate}").ToArray());
        Assert.AreEqual(0L, reader.CountEvents(["validity_revised"]), "the publisher's ends did not change");
        CollectionAssert.AreEqual(new[] { DerivedAtText, "2026-10-17T10:00:00Z" }, reader.ResolveObservations().Select(static o => o.DerivedAt).ToArray());
    }

    /// <summary>
    /// The derived fields are the rows' own: a provisional flag the observation bound does not give, a boundary its end does
    /// not make, a derived end where the publisher states one, a translation not naming its French state, an entry into
    /// force on a consolidation or not equal to a date of that role, a kind its legal resource does not spell, or an interval
    /// row that is not the chain's, each refuses with the digest recomputed where the digest covers the field.
    /// </summary>
    [TestMethod]
    [DataRow("UPDATE states SET provisional=1 WHERE applicability_date='2016-09-01'", false, "not canonical", DisplayName = "provisional before the bound")]
    [DataRow("UPDATE states SET boundary='half_open' WHERE applicability_date='2019-04-28'", false, "end or boundary is not the one", DisplayName = "a boundary its end does not make")]
    [DataRow("UPDATE states SET applicable_to='2017-05-01', applicable_to_source='derived' WHERE applicability_date='2016-09-01'", true, "end or boundary is not the one", DisplayName = "a derived end where the publisher states one")]
    [DataRow("UPDATE states SET translation_of=NULL WHERE language='eng'", true, "not canonical", DisplayName = "a translation naming no French state")]
    [DataRow("UPDATE states SET entry_into_force='2020-01-01' WHERE applicability_date='2020-01-01' AND state_kind='consolidation'", true, "not canonical", DisplayName = "an entry into force on a consolidation")]
    [DataRow("UPDATE states SET entry_into_force='1798-11-08' WHERE date_role='entry_into_force' AND language='fra' AND work_key='loi-1798-11-03-n1'", true, "not canonical", DisplayName = "an entry into force that is not the date of that role")]
    [DataRow("UPDATE states SET state_kind='consolidation' WHERE date_role='entry_into_force' AND work_key='loi-1798-11-03-n1'", true, "not canonical", DisplayName = "a kind its legal resource does not spell")]
    [DataRow("UPDATE states SET text_status_reason='rights' WHERE text_status='not_acquired' AND expression_iri=''", false, "digest is not derived", DisplayName = "a reason not covered by the digest")]
    [DataRow("DELETE FROM intervals WHERE kind='hole'", false, "interval rows are not the transitions", DisplayName = "a hole removed")]
    [DataRow("UPDATE intervals SET span_to='2022-02-01' WHERE kind='hole'", false, "interval rows are not the transitions", DisplayName = "a hole's span moved")]
    public void AStateRowsOwnAndDerivedFieldsAreTheReadersRecomputation(string sql, bool redigest, string expected)
    {
        var built = Fixture().Build();
        var tampered = MutateDatabase(built.IndexBytes.Span, connection =>
        {
            Execute(connection, sql);
            if (redigest)
            {
                ReplaceStatesRedigested(connection, ReadStates(connection));
            }
        });
        var reference = IndexRefOf(tampered);
        var exception = Assert.ThrowsExactly<InvalidDataException>(() => LuxembourgIndexReader.OpenAndVerify(
            reference, tampered, Harness.CorpusRef, RebindManifest(built.CapabilityManifest, reference.Sha256)));
        StringAssert.Contains(exception.Message, expected);
    }

    /// <summary>
    /// The build refuses what the catalogue contradicts: an act whose isMemberOf names another work, a code state whose work
    /// the catalogue types and not as a ComplexWork, and a held expression in a language the catalogue does not state.
    /// </summary>
    [TestMethod]
    public void TheWorkCheckAndTheCatalogueLanguageRefuseAContradiction()
    {
        var otherWork = new Harness();
        otherWork.State(W("loi/2020/01/01/n1/jo"), entryIntoForce: "2020-01-05", memberOf: W("loi/2020/01/01/n2"), expressions: ("fr", Fra))
            .Held(W("loi/2020/01/01/n1/jo"), ["art_1"]);
        StringAssert.Contains(Assert.ThrowsExactly<InvalidDataException>(() => otherWork.Project()).Message, "not of its work");

        var notComplex = new Harness();
        notComplex.Fact(W("code/penal"), "rdf:type", "http://data.legilux.public.lu/resource/ontology/jolux#Act");
        notComplex.State(W("code/penal/20200101"), "2020-01-01", expressions: ("fr", Fra)).Held(W("code/penal/20200101"), ["art_1"]);
        StringAssert.Contains(Assert.ThrowsExactly<InvalidDataException>(() => notComplex.Project()).Message, "is not a catalogue ComplexWork");

        var language = new Harness();
        language.State(W("loi/2020/01/01/n1/jo"), entryIntoForce: "2020-01-05", expressions: ("fr", Eng)).Held(W("loi/2020/01/01/n1/jo"), ["art_1"]);
        StringAssert.Contains(Assert.ThrowsExactly<InvalidDataException>(() => language.Project()).Message, "the catalogue says");
    }

    /// <summary>
    /// An act the catalogue dates by no exact entry into force and whose file states no date, or a consolidation with no
    /// exact catalogue applicability, gets no row: its articles stay in the index in no state, as an undated act's did.
    /// </summary>
    [TestMethod]
    public void AnUndatedStateGetsNoRow()
    {
        var h = new Harness();
        h.State(W("loi/2020/01/01/n1/jo"), memberOf: W("loi/2020/01/01/n1"), expressions: ("fr", Fra)).Held(W("loi/2020/01/01/n1/jo"), ["art_1"]);
        h.Fact(W("loi/2020/01/01/n2/jo"), "dateEntryInForce", "2020-01-05").Fact(W("loi/2020/01/01/n2/jo"), "dateEntryInForce", "2020-01-06");
        h.State(W("loi/2020/01/01/n2/jo"), expressions: ("fr", Fra)).Held(W("loi/2020/01/01/n2/jo"), ["art_1"]);
        var (states, intervals) = h.Project();
        Assert.IsEmpty(states, "no exact date of either role, and two entries into force, date nothing");
        Assert.IsEmpty(intervals);
        using var reader = Open(h.Build());
        Assert.AreEqual(2L, reader.ArticleCount, "the articles stay, in no state");
    }

    /// <summary>
    /// State digest /2, recomputed by an independent implementation (Python, the same length-prefixed UTF-8 fields in the same
    /// order): the code civil's first row of the fixture. And the rule profile's digest, which rule set lu-release-1 names.
    /// </summary>
    [TestMethod]
    public void StateDigestTwoAndItsRuleProfileArePinned()
    {
        var row = Fixture().Project().States.First();
        Assert.AreEqual("code-civil", row.WorkKey);
        Assert.AreEqual(PinnedCodeCivilFirstStateSha256, row.StateSha256);
        Assert.AreEqual(PinnedStateRuleProfileSha256, LuxembourgIndexBuilder.StateRuleProfileSha256);
        Assert.AreEqual(PinnedStateRuleProfileSha256, Convert.ToHexStringLower(SHA256.HashData(LuxembourgIndexBuilder.CopyStateRuleProfileBytes())));
        StringAssert.StartsWith(Encoding.UTF8.GetString(LuxembourgIndexBuilder.CopyStateRuleProfileBytes()), "lex-v3-luxembourg-expression-state/2\n");
    }

    internal const string PinnedCodeCivilFirstStateSha256 = "1c1c403a69b8c5d49299e4ae4b58fdbe419440a777c54444997512a6f5f617a5";
    internal const string PinnedStateRuleProfileSha256 = "943c77afa168d4f7d10aab5e11b46ed5443ee4a1a70353dbe1a134eb399459c2";
}
