using System.Text.Json;
using System.Text.Json.Nodes;
using Lex.V3.Contracts;
using Lex.V3.Contracts.Platform;
using Lex.V3.Ingest;
using Lex.V3.Ingest.Luxembourg;

namespace Lex.V3.Api;

/// <summary>
/// R77 (the specification of record's DS-02; the audit of 2026-10-27, gap 1): every Luxembourg act the publisher's catalogue
/// holds has a record here, even when this index holds no version of it. Such an act was answered <c>identifier_unknown</c>,
/// with no record, dates, status or official link. It now answers <c>text_not_available</c>, the code R77 names, with its
/// record from the catalogue's own facts (<c>work_facts</c>, which every mount's index carries): its identity and the address
/// this site gives it, its type, its dates by role (R21: the act's date, its publication, its entry into force and its end of
/// force, each the publisher's typed fact, never merged), its status in the publisher's vocabulary (the act's
/// <c>inForceStatus</c>, kept apart from any consolidation's), its Mémorial issue, its historical identifier, its official PDF,
/// its title, its versions, and why this site holds no text of each, by the reason Coverage counts it under
/// (<see cref="V3TextYield.CatalogueNotHeldReason"/>: what the catalogue states, never why the acquisition did not fetch it).
/// </summary>
/// <remarks>
/// <para>
/// Read when a request is answered, from the index as mounted: no new derive, on index 9 (derive-r1, the next D1) and on index 8
/// (derive-c) alike, whose fact table is the same. A fact the catalogue does not state is an empty list or null, said as not
/// stated, never inferred.
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
        "with its record, read from the catalogue's facts in the index as they stand: official_identity is the act as published " +
        "(the work followed by /jo) where the catalogue names it, else its first version; official_source is the publisher's PDF " +
        "file of that act's French text where the catalogue names one, else the same identity; document_type, date_document, " +
        "publication_date, entry_into_force, no_longer_in_force, in_force_status, memorial_issue and historical_legal_id are the " +
        "act's own typeDocument, dateDocument, publicationDate, dateEntryInForce, dateNoLongerInForce, inForceStatus, isPartOf and " +
        "historicalLegalId facts, verbatim, each a list that is empty when the catalogue states none; in_force_status is the act's, " +
        "never a consolidation's; each date is the publisher's and has the role its fact names, and none is an applicability " +
        "date; title is the act's French title as the catalogue states it, or null; versions lists the act's versions in the " +
        "catalogue (as published, consolidated, a code's), with each one's kind and the reason no text of it is held here " +
        "(reason_rule); record_address is the address this site gives the act, where this record is its page; nothing here is " +
        "the act's text, and an absence of text here is not an absence of law";

    internal const string CatalogueReasonRule =
        "each reason says what the publisher's catalogue states of a version of which this site holds no file, as Coverage counts " +
        "it, and never why the acquisition did not fetch it: catalogue_licence_scl, every XML and PDF file of its French text is " +
        "licensed licenceSCL, which this site does not reproduce; catalogue_licence_scl_in_part, some are licensed licenceSCL and " +
        "some otherwise; catalogue_cc_by_not_acquired, every one is licensed CC BY 4.0 and none was acquired; " +
        "catalogue_no_or_other_licence, they carry no licence or another one; catalogue_no_xml_or_pdf_manifestation, its French " +
        "text has no XML or PDF file; catalogue_translations_only, the catalogue names its text in other languages than French " +
        "only; catalogue_no_expression, the catalogue names no text of it; reason is the act as published's, else its first " +
        "version's";

    private const string LuxembourgLegRoot = LuxembourgEliRoot + "leg/";
    private const string JoluxPdfFormatPrefix = "http://data.legilux.public.lu/resource/authority/user-format/pdf";
    private const string FrenchLanguageIri = "http://publications.europa.eu/resource/authority/language/FRA";

    /// <summary>
    /// The work an identifier names when the publisher's catalogue holds it and this index holds no version of it: its work IRI,
    /// or null. Read: this site's address of a work (<c>/lu-legilux/{work key}</c>, as the index would mint it) and the
    /// publisher's IRI of the work or of anything under it (its act as published, a consolidation, an expression). The work must
    /// have a version in the catalogue (an act as published, a consolidation, a code's version): a Mémorial issue is no act.
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

        if (work is null || work.Length <= LuxembourgLegRoot.Length || WorkKeyOfIri(work) is not { } workKey ||
            (key.Length > 0 && !string.Equals(key, workKey, StringComparison.Ordinal)))
        {
            return null;
        }

        if (_reader.ResolveWorkStates(workKey).Count > 0 || _reader.ResolveWorkStates(work).Count > 0)
        {
            return null;
        }

        return V3TextYield.CatalogueVersionsOf(_reader.ResolveSubjectsUnder(work + "/"), work).Count > 0 ? work : null;
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

        var versions = V3TextYield.CatalogueVersionsOf(facts.Select(static fact => fact.SubjectIri), work);
        var act = work + "/jo";
        var primary = versions.Contains(act, StringComparer.Ordinal) ? act : versions[0];
        var hasAct = string.Equals(primary, act, StringComparison.Ordinal);
        var triples = facts.Select(static fact => (fact.SubjectIri, fact.Predicate, fact.ObjectValue)).ToArray();
        var reasons = versions.Select(version => V3TextYield.CatalogueNotHeldReason(triples, version)).ToArray();
        var reason = reasons[Array.IndexOf(versions.ToArray(), primary)];

        // The publisher's PDF of the act's French text: the files of its French expressions' PDF manifestations.
        var french = Values(primary, "isRealizedBy").Where(expression => Values(expression, "language").Contains(FrenchLanguageIri, StringComparer.Ordinal)).ToArray();
        var pdf = french
            .SelectMany(expression => Values(expression, "isEmbodiedBy"))
            .Where(manifestation => Values(manifestation, "userFormat").Any(static format => format.StartsWith(JoluxPdfFormatPrefix, StringComparison.Ordinal)))
            .SelectMany(manifestation => Values(manifestation, "isExemplifiedBy"))
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToArray();

        // The act's French title as the catalogue states it (its own expression's first), else the first the catalogue states
        // of the work's versions, French first (CatalogueTitles, which R92's record gates).
        var title = french.SelectMany(expression => Values(expression, "title")).FirstOrDefault() ?? CatalogueTitles(work).FirstOrDefault();
        var key = LuxembourgIndexBuilder.WorkKeyOf(work);
        var observations = _reader.ResolveObservations();
        return new JsonObject
        {
            ["official_identity"] = primary,
            ["official_source"] = pdf.FirstOrDefault() ?? primary,
            ["retained_transport_evidence"] = "none",
            ["record_kind"] = CatalogueRecordKind,
            ["reason"] = reason,
            ["reason_rule"] = CatalogueReasonRule,
            ["work_iri"] = work,
            ["work_key"] = key,
            ["record_address"] = "/lu-legilux/" + key,
            ["act_iri"] = hasAct ? act : null,
            ["title"] = title,
            ["title_language"] = title is null ? null : FrenchLanguage,
            ["document_type"] = List(Values(primary, "typeDocument")),
            ["date_document"] = List(Values(primary, "dateDocument")),
            ["publication_date"] = List(Values(primary, "publicationDate")),
            ["entry_into_force"] = List(Values(primary, "dateEntryInForce")),
            ["no_longer_in_force"] = List(Values(primary, "dateNoLongerInForce")),
            ["in_force_status"] = List(hasAct ? Values(primary, "inForceStatus") : []),
            ["memorial_issue"] = List(Values(primary, "isPartOf")),
            ["historical_legal_id"] = List(Values(primary, "historicalLegalId")),
            ["official_pdf"] = List(pdf),
            ["versions"] = List(versions),
            ["version_kinds"] = List(versions.Select(V3TextYield.CatalogueVersionKindOf)),
            ["version_reasons"] = List(reasons),
            ["record_evidence_sha256s"] = List(facts.Select(static fact => fact.EvidenceSha256).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal)),
            ["catalogue_read_no_later_than"] = observations.Count == 0 ? null : observations[^1].BuiltAt,
            ["rule"] = CatalogueRecordRule,
            ["what_would_answer"] = List([string.Equals(reason, V3TextYield.NotHeldCcByNotAcquired, StringComparison.Ordinal) ? "expanded_official_scope" : "new_official_observation"]),
            ["asserts_absence_of_law"] = false,
        };
    }
}
