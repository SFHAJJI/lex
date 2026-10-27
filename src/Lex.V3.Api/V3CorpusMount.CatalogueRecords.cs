using System.Text.Json;
using System.Text.Json.Nodes;
using Lex.V3.Contracts;
using Lex.V3.Contracts.Platform;
using Lex.V3.Ingest;
using Lex.V3.Ingest.Luxembourg;
using Lex.V3.Ingest.Names;

namespace Lex.V3.Api;

/// <summary>
/// R77 (the specification of record's DS-02; the audit of 2026-10-27, gap 1): every Luxembourg act the publisher's catalogue
/// holds has a record here, even when this index holds no version of it. Such an act was answered <c>identifier_unknown</c>,
/// with no record, dates, status or official link. It now answers <c>text_not_available</c>, the code R77 names, with its
/// record from the catalogue's own facts (<c>work_facts</c>, which every mount's index carries) and the files the corpus holds:
/// its identity and the address this site gives it, its type, its dates by role (R21: the act's date, its publication, its
/// entry into force and its end of force, each the publisher's typed fact, never merged), its status in the publisher's
/// vocabulary (the act's <c>inForceStatus</c>, kept apart from any consolidation's), its Mémorial issue, its historical
/// identifier, its official PDF, its title, its versions, and why no text of each is served here, by the reason Coverage counts
/// it under, by one rule (<see cref="V3TextYield.HeldFileWithoutText"/> for a version whose file the corpus holds,
/// <see cref="V3TextYield.CatalogueNotHeldReason"/>, what the catalogue states, for one it holds no file of).
/// </summary>
/// <remarks>
/// <para>
/// Read when a request is answered, from the index and corpus as mounted: no new derive, on index 9 (derive-r1, the next D1) and
/// on index 8 (derive-c) alike, whose fact table is the same. A fact the catalogue does not state is an empty list or null, said
/// as not stated, never inferred.
/// </para>
/// <para>
/// R92 first (<see cref="NamesWithheld"/>): an act the names record withholds answers <c>text_withheld</c> with the reason
/// <c>names_private_individuals</c>, its identity and official link, and no title and no record of what it says, as every other
/// answer about it does.
/// </para>
/// </remarks>
internal sealed partial class V3CorpusMount
{
    /// <summary>The kind every catalogue record names itself by (<c>record_kind</c>).</summary>
    internal const string CatalogueRecordKind = "catalogue_only";

    internal const string CatalogueRecordRule =
        "R77: the publisher's catalogue holds this act and this index holds no version of it, so it is answered text_not_available " +
        "with its record, read from the catalogue's facts in the index and the files the corpus holds, as they stand: " +
        "official_identity is the act as published (the work followed by /jo) where the catalogue names it, else the work's first " +
        "version; official_source is the publisher's PDF file of that version's French text where the catalogue names one, else the " +
        "same identity; retained_transport_evidence is the receipt of that version's file's body where the corpus holds one, else " +
        "none; document_type, date_document, publication_date, entry_into_force, no_longer_in_force, in_force_status, " +
        "memorial_issue and historical_legal_id are the act as published's own typeDocument, dateDocument, publicationDate, " +
        "dateEntryInForce, dateNoLongerInForce, inForceStatus, isPartOf and historicalLegalId facts, verbatim, and " +
        "document_type_codes and in_force_status_codes the last segment of each of the first two IRIs, the publisher's own word; " +
        "each is a list, empty when the catalogue states none and for a work the catalogue names no act as published of (a code, " +
        "whose versions' own facts are not the act's); in_force_status is never a consolidation's; each date is the publisher's and " +
        "has the role its fact names, and none is an applicability date; title is the French title the catalogue states of the act, " +
        "else of the work's first version, or null; versions lists the work's versions in the catalogue and in the corpus (as " +
        "published, consolidated, a code's), each with its kind, the reason no text of it is served here (reason_rule) and that " +
        "reason's disposition or dispositions, or null; record_address is the address this site gives the act, where this record is " +
        "its page; nothing here is the act's text, and an absence of text here is not an absence of law";

    internal const string CatalogueReasonRule =
        "each reason is the one Coverage counts the version under, by one rule (V3TextYield): for a version whose file the corpus " +
        "holds, file_articles_no_state, the index holds the file's articles and no state of the version, so none is served " +
        "(Coverage counts it with text), file_withheld, its rights withhold its text (its disposition: the rights disposition by " +
        "its wire name), " +
        "file_pdf_only, the file was admitted and is a PDF from which no text is served, or file_no_article_served, the file was " +
        "admitted and no article of it is served (its dispositions: its legal-content dispositions, joined with +); for a version " +
        "of which the corpus holds no file, what the publisher's catalogue states of it, never why the acquisition did not fetch it: " +
        "catalogue_licence_scl, every XML and PDF file of its French text is licensed licenceSCL, which this site does not reproduce; " +
        "catalogue_licence_scl_in_part, some are licensed licenceSCL and some otherwise; catalogue_cc_by_not_acquired, every one is " +
        "licensed CC BY 4.0 and none was acquired; catalogue_no_or_other_licence, they carry no licence or another one; " +
        "catalogue_no_xml_or_pdf_manifestation, its French text has no XML or PDF file; catalogue_translations_only, the catalogue " +
        "names its text in other languages than French only; catalogue_no_expression, the catalogue names no text of it; reason and " +
        "disposition are the act as published's, else the work's first version's";

    /// <summary>
    /// The record's reason for a version whose file the corpus holds, admitted, and whose articles the index holds while it
    /// holds no state of it, so none is served (Coverage's files_with_text).
    /// </summary>
    internal const string FileArticlesNoState = "file_articles_no_state";

    /// <summary>The record's reason for a version whose file the corpus holds and whose rights withhold its text (Coverage's files_withheld).</summary>
    internal const string FileWithheld = "file_withheld";

    /// <summary>The record's reason for a version whose file the corpus holds, admitted, a PDF with no text served (Coverage's files_pdf_only).</summary>
    internal const string FilePdfOnly = "file_pdf_only";

    /// <summary>The record's reason for a version whose file the corpus holds, admitted, with no article served (Coverage's files_parse_failed).</summary>
    internal const string FileNoArticleServed = "file_no_article_served";

    private const string LuxembourgLegRoot = LuxembourgEliRoot + "leg/";
    private const string JoluxPdfFormatPrefix = "http://data.legilux.public.lu/resource/authority/user-format/pdf";
    private const string FrenchLanguageIri = "http://publications.europa.eu/resource/authority/language/FRA";

    private readonly Lock _heldFilesGate = new();
    private IReadOnlyDictionary<string, LexCorpus6Member[]>? _heldFilesByWork;
    private long _catalogueRangeReads;

    /// <summary>
    /// How many times the record read the catalogue's facts under a work (a range of the fact table, under the reader's one
    /// lock): a test holds that an address with no work's shape (a whole act type, a year) reads none (the review of #1003, F2).
    /// </summary>
    internal long CatalogueRangeReads => Interlocked.Read(ref _catalogueRangeReads);

    /// <summary>
    /// The work an identifier names when the publisher's catalogue holds it and this index holds no version of it: its work IRI,
    /// or null. Read: this site's address of a work (<c>/lu-legilux/{work key}</c>, as the index would mint it) and the
    /// publisher's IRI of the work or of anything under it (its act as published, a consolidation, an expression). Before any
    /// read, the work must have a work's shape (<see cref="IsWorkShape"/>), so a short address (a whole act type, a year) never
    /// reads a range of the catalogue (the review of #1003, F2); and the catalogue must name a version of it (an act as
    /// published, a consolidation, a code's version): a Mémorial issue is no act.
    /// </summary>
    private string? CatalogueOnlyWorkOf(string identifier)
    {
        if (_reader is null)
        {
            return null;
        }

        string? work = null;
        if (TryParseStableWorkCoordinate(identifier, out var key))
        {
            work = LuxembourgLegRoot + key.Replace('-', '/');
        }
        else if (identifier.StartsWith(LuxembourgLegRoot, StringComparison.Ordinal) && identifier.Length > LuxembourgLegRoot.Length)
        {
            work = ReadEli(identifier).Work;
        }

        if (work is null || !IsWorkShape(work) || WorkKeyOfIri(work) is not { } workKey ||
            (key.Length > 0 && !string.Equals(key, workKey, StringComparison.Ordinal)))
        {
            return null;
        }

        if (_reader.ResolveWorkStates(workKey).Count > 0 || _reader.ResolveWorkStates(work).Count > 0)
        {
            return null;
        }

        Interlocked.Increment(ref _catalogueRangeReads);
        return V3TextYield.CatalogueVersionsOf(_reader.ResolveSubjectsUnder(work + "/"), work).Count > 0 ? work : null;
    }

    /// <summary>
    /// Whether an IRI is a Luxembourg work's own, in R92's shape (<see cref="PrivateIndividuals.LuxembourgWorkIriOf"/>): a type,
    /// a date and a number, or a code's or a recueil's name, and nothing under it.
    /// </summary>
    private static bool IsWorkShape(string work) =>
        string.Equals(PrivateIndividuals.LuxembourgWorkIriOf(work), work, StringComparison.Ordinal);

    /// <summary>
    /// The held Luxembourg files of the corpus by the work of the version each is a file of (its selected WEMI's root), in the
    /// corpus's order, read once: the files Coverage counts (<see cref="V3TextYield"/>), each served with or without text.
    /// </summary>
    private IReadOnlyDictionary<string, LexCorpus6Member[]> HeldFilesByWork()
    {
        lock (_heldFilesGate)
        {
            return _heldFilesByWork ??= _corpus.HeldMembers
                .Where(static member => member.Publisher == PublisherId.LuLegilux && member.LuxembourgRights is not null)
                .GroupBy(static member => V3TextYield.CatalogueVersionWorkOf(member.LuxembourgRights!.SelectedWemi.RootIri)
                    ?? member.LuxembourgRights!.SelectedWemi.RootIri, StringComparer.Ordinal)
                .ToDictionary(static group => group.Key, static group => group.OrderBy(static member => member.SourceOrdinal).ToArray(), StringComparer.Ordinal);
        }
    }

    /// <summary>
    /// The publisher's own word for a value of its authority vocabularies: the IRI's last path segment
    /// (<c>…/resource-type/LOI</c> is <c>LOI</c>, <c>…/application-status/in-force</c> is <c>in-force</c>), or the value as it is
    /// when it has no segment.
    /// </summary>
    private static string AuthorityCode(string iri)
    {
        var at = iri.LastIndexOf('/');
        return at < 0 || at == iri.Length - 1 ? iri : iri[(at + 1)..];
    }

    /// <summary>The work key the index would mint for a work IRI (<see cref="LuxembourgIndexBuilder.WorkKeyOf"/>), or null.</summary>
    private static string? WorkKeyOfIri(string work)
    {
        try
        {
            return LuxembourgIndexBuilder.WorkKeyOf(work);
        }
        catch (InvalidDataException)
        {
            return null;
        }
    }

    /// <summary>
    /// The answer for an identifier that names an act the catalogue holds and this index holds no version of
    /// (<see cref="CatalogueOnlyWorkOf"/>): <c>text_not_available</c> with its record, or <c>text_withheld</c> when R92 withholds
    /// it; null when the identifier names no such act.
    /// </summary>
    private V3PlatformOperationOutcome? CatalogueOnlyRefusal(V3PlatformOperationRequest request, string identifier, DateTimeOffset observedAt) =>
        CatalogueOnlyWorkOf(identifier) is { } work ? CatalogueRecordRefusal(request, work, observedAt) : null;

    /// <summary>The refusal for one act the catalogue holds and this index holds no version of, by its work IRI.</summary>
    private V3PlatformOperationOutcome CatalogueRecordRefusal(V3PlatformOperationRequest request, string work, DateTimeOffset observedAt)
    {
        // R92: an act the names record withholds is said as every answer about it says it, with no title and no record of it.
        if (_names.ByLuxembourgIri(work) is { } withheld)
        {
            return RefuseNamesWithheld(request, withheld, null, observedAt);
        }

        using var document = JsonSerializer.SerializeToDocument(CatalogueRecordPayload(work));
        return V3PlatformOperationOutcome.Refused(
            Context("refusal", observedAt),
            new V3PlatformOperationRefusal(request, "text_not_available", document.RootElement));
    }

    /// <summary>
    /// Why no text of one version is served here, as Coverage counts it (the review of #1003, F1): for a version whose file the
    /// corpus holds, its bucket's reason and its disposition or dispositions (<see cref="V3TextYield.HeldFileWithoutText"/>); for
    /// one it holds no file of, what the catalogue states of it, with none. A test holds it to Coverage's count over every case.
    /// </summary>
    internal static (string Reason, string? Disposition) VersionWithoutText(
        string version,
        IReadOnlyList<LexCorpus6Member> files,
        IReadOnlyList<(string Subject, string Predicate, string Value)> facts,
        IReadOnlyDictionary<string, long> articles)
    {
        if (files.FirstOrDefault(member => string.Equals(member.LuxembourgRights!.SelectedWemi.RootIri, version, StringComparison.Ordinal)) is { } file)
        {
            var (bucket, reason) = V3TextYield.HeldFile(file, articles.GetValueOrDefault(file.ObjectRefSha256));
            return bucket switch
            {
                V3TextYield.HeldFileWithText => (FileArticlesNoState, null),
                V3TextYield.HeldFileWithheld => (FileWithheld, reason),
                V3TextYield.HeldFilePdfOnly => (FilePdfOnly, null),
                _ => (FileNoArticleServed, reason),
            };
        }

        return (V3TextYield.CatalogueNotHeldReason(facts, version), null);
    }

    /// <summary>The record of one catalogue-only act (<see cref="CatalogueRecordRule"/>), every member a scalar or a list of scalars.</summary>
    private JsonObject CatalogueRecordPayload(string work)
    {
        var facts = _reader!.ResolveFacts([work]).Concat(_reader.ResolveFactsUnder(work + "/")).ToArray();
        string[] Values(string subject, string predicate) => facts
            .Where(fact => string.Equals(fact.SubjectIri, subject, StringComparison.Ordinal) &&
                           string.Equals(fact.Predicate, predicate, StringComparison.Ordinal))
            .Select(static fact => fact.ObjectValue)
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToArray();
        static JsonArray List(IEnumerable<string?> values) => new(values.Select(static value => (JsonNode?)value).ToArray());

        // The work's versions: those the catalogue names and those whose file the corpus holds, as Coverage counts them.
        var files = HeldFilesByWork().GetValueOrDefault(work) ?? [];
        var versions = V3TextYield.CatalogueVersionsOf(
            facts.Select(static fact => fact.SubjectIri).Concat(files.Select(static member => member.LuxembourgRights!.SelectedWemi.RootIri)), work);
        var act = work + "/jo";
        var primary = versions.Contains(act, StringComparer.Ordinal) ? act : versions[0];
        var hasAct = string.Equals(primary, act, StringComparison.Ordinal);
        var triples = facts.Select(static fact => (fact.SubjectIri, fact.Predicate, fact.ObjectValue)).ToArray();
        var articles = _reader.ResolveArticleCountsOfObjects(files.Select(static member => member.ObjectRefSha256).ToArray());
        var reasons = versions.Select(version => VersionWithoutText(version, files, triples, articles)).ToArray();
        var (reason, disposition) = reasons[Array.IndexOf(versions.ToArray(), primary)];

        // The publisher's PDF of the first version's French text: the files of its French expressions' PDF manifestations.
        var french = Values(primary, "isRealizedBy").Where(expression => Values(expression, "language").Contains(FrenchLanguageIri, StringComparer.Ordinal)).ToArray();
        var pdf = french
            .SelectMany(expression => Values(expression, "isEmbodiedBy"))
            .Where(manifestation => Values(manifestation, "userFormat").Any(static format => format.StartsWith(JoluxPdfFormatPrefix, StringComparison.Ordinal)))
            .SelectMany(manifestation => Values(manifestation, "isExemplifiedBy"))
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToArray();

        // The act's own facts (the review of #1003, F3): a work the catalogue names no act as published of (a code) has none, since
        // its first version's facts are that version's, not the act's.
        string[] ActValues(string predicate) => hasAct ? Values(act, predicate) : [];

        // The French title the catalogue states of the act (its own expression's first), else of the work's versions, French first
        // (CatalogueTitles, which R92's record gates).
        var title = french.SelectMany(expression => Values(expression, "title")).FirstOrDefault() ?? CatalogueTitles(work).FirstOrDefault();
        var key = LuxembourgIndexBuilder.WorkKeyOf(work);
        var observations = _reader.ResolveObservations();
        return new JsonObject
        {
            ["official_identity"] = primary,
            ["official_source"] = pdf.FirstOrDefault() ?? primary,
            // The receipt of the file's body the corpus holds of that version, where it holds one.
            ["retained_transport_evidence"] = files.FirstOrDefault(member => string.Equals(member.LuxembourgRights!.SelectedWemi.RootIri, primary, StringComparison.Ordinal))?.BodyReceiptSha256 ?? "none",
            ["record_kind"] = CatalogueRecordKind,
            ["reason"] = reason,
            ["disposition"] = disposition,
            ["reason_rule"] = CatalogueReasonRule,
            ["work_iri"] = work,
            ["work_key"] = key,
            ["record_address"] = "/lu-legilux/" + key,
            ["act_iri"] = hasAct ? act : null,
            ["title"] = title,
            ["title_language"] = title is null ? null : FrenchLanguage,
            ["document_type"] = List(ActValues("typeDocument")),
            // The publisher's own word for each, the last segment of its authority IRI (LOI, RGD; in-force), as a page says it.
            ["document_type_codes"] = List(ActValues("typeDocument").Select(AuthorityCode)),
            ["date_document"] = List(ActValues("dateDocument")),
            ["publication_date"] = List(ActValues("publicationDate")),
            ["entry_into_force"] = List(ActValues("dateEntryInForce")),
            ["no_longer_in_force"] = List(ActValues("dateNoLongerInForce")),
            ["in_force_status"] = List(ActValues("inForceStatus")),
            ["in_force_status_codes"] = List(ActValues("inForceStatus").Select(AuthorityCode)),
            ["memorial_issue"] = List(ActValues("isPartOf")),
            ["historical_legal_id"] = List(ActValues("historicalLegalId")),
            ["official_pdf"] = List(pdf),
            ["versions"] = List(versions),
            ["version_kinds"] = List(versions.Select(V3TextYield.CatalogueVersionKindOf)),
            ["version_reasons"] = List(reasons.Select(static entry => entry.Reason)),
            ["version_dispositions"] = List(reasons.Select(static entry => entry.Disposition)),
            ["record_evidence_sha256s"] = List(facts.Select(static fact => fact.EvidenceSha256).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal)),
            ["catalogue_read_no_later_than"] = observations.Count == 0 ? null : observations[^1].BuiltAt,
            ["rule"] = CatalogueRecordRule,
            // An expansion of the corpus would answer an act licensed CC BY and not acquired; a new observation (a file or a licence
            // the publisher publishes) any other.
            ["what_would_answer"] = List([string.Equals(reason, V3TextYield.NotHeldCcByNotAcquired, StringComparison.Ordinal) ? "expanded_official_scope" : "new_official_observation"]),
            ["asserts_absence_of_law"] = false,
        };
    }
}
