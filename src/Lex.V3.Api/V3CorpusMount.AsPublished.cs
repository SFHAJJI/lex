using System.Globalization;
using Lex.V3.Contracts;
using Lex.V3.Contracts.Source.Luxembourg;
using Lex.V3.Ingest.Luxembourg;

namespace Lex.V3.Api;

/// <summary>
/// A Luxembourg act held only as first published (the plan's item 0.6; the specification of record's R76, R18 and R20).
/// Almost the whole Luxembourg population is held this way: every state the index holds of the work is an expression of
/// the publisher's Journal officiel legal resource (<c>{work}/jo</c>, which the publisher types <c>jolux:Act</c>), and none
/// is a consolidated version (<c>{work}/consolide/{date}</c>, <c>jolux:Consolidation</c>). The answers that serve such a
/// work's states (<c>as_of</c>, <c>evidence_bundle</c>, <c>timeline</c>, <c>dossier</c>) say so in <c>as_published</c>,
/// with what the index holds about later versions, read from the publisher's own facts and never invented, and with an
/// authenticity sentence whose case is read from the publisher's publication date of the act as published. A work with a
/// consolidated state held answers <c>as_published: null</c>.
/// </summary>
internal sealed partial class V3CorpusMount
{
    /// <summary>The label every surface shows on an act held only as published (R76).</summary>
    internal const string AsPublishedLabel = "as published; may have been amended since; no consolidated text";

    internal const string AsPublishedRule =
        "a Luxembourg work is held only as published when every state this index holds of it is an expression of the publisher's Journal officiel " +
        "legal resource, the act as first published (the publisher work IRI followed by /jo); a consolidated version is a legal resource of its own " +
        "(/consolide/ and its date), and this index holds no state of one, so no consolidated text of the work is held here; this says nothing about " +
        "whether the publisher holds one: the consolidated versions the publisher's own facts name, if any, are listed under later_version_facts";

    internal const string AsPublishedLaterVersionsRule =
        "later versions are not held, and these are the facts the index holds about them: consolidations lists the legal resources the publisher's facts name under the work's /consolide/, each with its " +
        "dateApplicability and dateEndApplicability as one civil date or null (none, several or another form), and end_of_force the publisher's " +
        "dateNoLongerInForce facts on the act as published, verbatim; an empty list says only that the index holds no such fact, never that none exists; " +
        "the not-held row later_versions carries the same sentence";

    internal const string AsPublishedAuthenticityRule =
        "the sentence is chosen by the publisher's publicationDate of the act as published (the Journal officiel legal resource): one civil date on " +
        "or after 2017-01-01 is electronic_journal_officiel (loi du 23 décembre 2016, art. 5, applying from 1 January 2017 under its art. 9), one " +
        "before it is printed_memorial, and none, several or one that is not a civil date is publication_date_not_stated; nothing here calls a file " +
        "authentic: served_manifestations names the publisher file each listed state's text was read from, with the legalValue the publisher asserts " +
        "on that file, verbatim, and Lex's extraction is never authentic whatever that value is";

    /// <summary>The authenticity sentence for each case, the act as published (its Journal officiel IRI) in place of <c>{0}</c>.</summary>
    internal static readonly IReadOnlyDictionary<string, string> AsPublishedAuthenticity = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["electronic_journal_officiel"] =
            "This text was extracted and normalised by Lex, for information; it is not authentic. Since 1 January 2017, only the Journal officiel " +
            "du Grand-Duché de Luxembourg (Mémorial A) as published electronically is authentic (loi du 23 décembre 2016, art. 5). The act as " +
            "published: {0}.",
        ["printed_memorial"] =
            "This text was extracted and normalised by Lex, for information; it is not authentic. For an act published before 1 January 2017, the " +
            "printed Mémorial is authentic, and the publisher's PDF is its digital reproduction. The act as published: {0}.",
        ["publication_date_not_stated"] =
            "This text was extracted and normalised by Lex, for information; it is not authentic. The publisher's record gives no single " +
            "publication date for this act, so Lex does not say which edition is authentic. The act as published: {0}.",
    };

    /// <summary>The not-held row's item for later versions, beside the fixed rows of <c>dossier</c> and <c>evidence_bundle</c>.</summary>
    internal const string LaterVersionsItem = "later_versions";

    /// <summary>What is said of later versions when the index holds no later-version or end-of-force fact for the act.</summary>
    internal const string LaterVersionsNotHeld = "later versions of this act are not held";

    /// <summary>The first day the published electronic Journal officiel alone is authentic (loi du 23 décembre 2016, arts. 5 and 9).</summary>
    private const string ElectronicJournalOfficielFrom = "2017-01-01";

    /// <summary>
    /// <c>as_published</c> for a work: null unless every held state of it (<paramref name="states"/>, every language) is an
    /// expression of its Journal officiel legal resource. <paramref name="listed"/> are the states the answer lists, whose
    /// retained publisher files are named with the legalValue the publisher asserts on each.
    /// </summary>
    private AsPublishedFacts? AsPublishedOf(
        IReadOnlyList<LuxembourgIndexResolvedState> states,
        IReadOnlyList<LuxembourgIndexResolvedState> listed)
    {
        if (states.Count == 0)
        {
            return null;
        }

        var work = states[0].PublisherWorkIri;
        var journalOfficiel = work + "/jo";
        if (states.Any(state => !string.Equals(state.PublisherWorkIri, work, StringComparison.Ordinal) ||
                                !string.Equals(state.PublisherLegalResourceIri, journalOfficiel, StringComparison.Ordinal) ||
                                !state.ExpressionIri.StartsWith(journalOfficiel + "/", StringComparison.Ordinal)))
        {
            return null;
        }

        var actFacts = _reader!.ResolveFacts([journalOfficiel]);
        var publicationDates = FactsOf(actFacts, LuxembourgAssertionPredicate.PublicationDate, journalOfficiel);
        var endOfForce = FactsOf(actFacts, LuxembourgAssertionPredicate.DateNoLongerInForce, journalOfficiel);

        // The consolidated versions the publisher names under the work: each legal resource exactly one segment below
        // /consolide/, read with its two applicability dates; its expressions and files are not listed.
        var prefix = work + "/consolide/";
        var underConsolidations = _reader.ResolveFactsUnder(prefix);
        var consolidations = underConsolidations
            .Select(static fact => fact.SubjectIri)
            .Where(subject => subject.Length > prefix.Length && subject.IndexOf('/', prefix.Length) < 0)
            .Distinct(StringComparer.Ordinal)
            .Select(subject => new AsPublishedConsolidation(
                subject,
                SingleCivilDate(FactsOf(underConsolidations, LuxembourgAssertionPredicate.DateApplicability, subject)),
                SingleCivilDate(FactsOf(underConsolidations, LuxembourgAssertionPredicate.DateEndApplicability, subject))))
            .OrderBy(static consolidation => consolidation.ApplicabilityDate ?? "￿", StringComparer.Ordinal)
            .ThenBy(static consolidation => consolidation.LegalResourceIri, StringComparer.Ordinal)
            .ToArray();

        var publicationDate = SingleCivilDate(publicationDates);
        var basis = publicationDate is null
            ? "publication_date_not_stated"
            : string.CompareOrdinal(publicationDate, ElectronicJournalOfficielFrom) >= 0
                ? "electronic_journal_officiel"
                : "printed_memorial";

        // The publisher file each listed state's text was read from, with the legalValue the publisher asserts on it.
        var served = listed
            .SelectMany(state => _reader.ResolveStateSources(state.StateSha256))
            .Select(source => MemberOf(source.ObjectRefSha256)?.LuxembourgRights?.SelectedWemi)
            .Where(static wemi => wemi is not null)
            .Select(static wemi => (Manifestation: wemi!.ManifestationIri, Format: wemi.FormatIri))
            .Distinct()
            .OrderBy(static entry => entry.Manifestation, StringComparer.Ordinal)
            .ThenBy(static entry => entry.Format, StringComparer.Ordinal)
            .ToArray();
        var servedFacts = _reader.ResolveFacts(served.Select(static entry => entry.Manifestation).Distinct(StringComparer.Ordinal).ToArray());

        return new AsPublishedFacts(
            journalOfficiel,
            consolidations,
            endOfForce,
            publicationDates,
            basis,
            served.Select(entry => new AsPublishedServedFile(
                entry.Manifestation, entry.Format, FactsOf(servedFacts, LuxembourgAssertionPredicate.LegalValue, entry.Manifestation))).ToArray());
    }

    /// <summary>The value of the one fact given, when it is exactly a civil date; null for none, several, or another form.</summary>
    private static string? SingleCivilDate(FactView[] facts)
    {
        var values = facts.Select(static fact => fact.value).Distinct(StringComparer.Ordinal).ToArray();
        return values.Length == 1 &&
               DateOnly.TryParseExact(values[0], "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out _)
            ? values[0]
            : null;
    }

    /// <summary>What an answer says of later versions, from the facts the index holds and nothing else.</summary>
    internal static string LaterVersionsSentence(
        IReadOnlyList<(string? ApplicabilityDate, string? EndApplicabilityDate)> consolidations,
        IReadOnlyList<string> endOfForce)
    {
        var sentence = LaterVersionsNotHeld;
        if (consolidations.Count > 0)
        {
            var dated = consolidations.Where(static entry => entry.ApplicabilityDate is not null)
                .Select(static entry => entry.EndApplicabilityDate is null
                    ? $"from {entry.ApplicabilityDate}"
                    : $"from {entry.ApplicabilityDate} to {entry.EndApplicabilityDate}")
                .ToArray();
            var undated = consolidations.Count - dated.Length;
            var named = consolidations.Count == 1 ? "1 consolidated version of it" : $"{consolidations.Count} consolidated versions of it";
            var parts = new List<string>();
            if (dated.Length > 0)
            {
                parts.Add($"applicable {JoinWithAnd(dated)} (publisher)");
            }
            if (undated > 0)
            {
                parts.Add(dated.Length == 0
                    ? "with no single applicability date stated"
                    : $"and {undated} with no single applicability date stated");
            }
            var whose = consolidations.Count == 1 ? "its" : "their";
            sentence += $": the publisher's record names {named}, {string.Join(", ", parts)}; this index holds none of {whose} text";
        }

        if (endOfForce.Count > 0)
        {
            sentence += $"; the publisher's record gives {JoinWithAnd(endOfForce)} as the act's end-of-force date (dateNoLongerInForce)";
        }

        return sentence;
    }

    private static string JoinWithAnd(IReadOnlyList<string> values) =>
        values.Count == 1 ? values[0] : string.Join(", ", values.Take(values.Count - 1)) + " and " + values[^1];

    /// <summary>The <c>as_published</c> member as an answer serves it, or null for a work with a consolidated state held.</summary>
    private static object? AsPublishedAnswer(AsPublishedFacts? facts)
    {
        if (facts is null)
        {
            return null;
        }

        return new
        {
            label = AsPublishedLabel,
            rule = AsPublishedRule,
            journal_officiel_iri = facts.JournalOfficielIri,
            later_version_facts = new
            {
                sentence = facts.LaterVersionsSentence,
                consolidations = facts.Consolidations.Select(static consolidation => new
                {
                    legal_resource_iri = consolidation.LegalResourceIri,
                    applicability_date = consolidation.ApplicabilityDate,
                    end_applicability_date = consolidation.EndApplicabilityDate,
                }).ToArray(),
                end_of_force = facts.EndOfForce,
                rule = AsPublishedLaterVersionsRule,
            },
            authenticity = new
            {
                basis = facts.AuthenticityBasis,
                sentence = facts.AuthenticitySentence,
                publication_dates = facts.PublicationDates,
                served_manifestations = facts.Served.Select(static file => new
                {
                    manifestation_iri = file.ManifestationIri,
                    format_iri = file.FormatIri,
                    legal_values = file.LegalValues,
                }).ToArray(),
                rule = AsPublishedAuthenticityRule,
            },
        };
    }

    /// <summary>The not-held rows of an answer: its fixed rows, and the later-versions row of an act held only as published.</summary>
    private static object[] NotHeldRows(string[][] fixedRows, AsPublishedFacts? facts) =>
        fixedRows.Select(static row => (object)new { item = row[0], reason = row[1] })
            .Concat(facts is null ? [] : [new { item = LaterVersionsItem, reason = facts.LaterVersionsSentence }])
            .ToArray();

    /// <summary>What the index holds about an act held only as published, read once per answer.</summary>
    private sealed record AsPublishedFacts(
        string JournalOfficielIri,
        AsPublishedConsolidation[] Consolidations,
        FactView[] EndOfForce,
        FactView[] PublicationDates,
        string AuthenticityBasis,
        AsPublishedServedFile[] Served)
    {
        public string LaterVersionsSentence => V3CorpusMount.LaterVersionsSentence(
            Consolidations.Select(static consolidation => (consolidation.ApplicabilityDate, consolidation.EndApplicabilityDate)).ToArray(),
            EndOfForce.Select(static fact => fact.value).Distinct(StringComparer.Ordinal).ToArray());

        public string AuthenticitySentence =>
            string.Format(CultureInfo.InvariantCulture, AsPublishedAuthenticity[AuthenticityBasis], JournalOfficielIri);
    }

    private sealed record AsPublishedConsolidation(string LegalResourceIri, string? ApplicabilityDate, string? EndApplicabilityDate);

    private sealed record AsPublishedServedFile(string ManifestationIri, string FormatIri, FactView[] LegalValues);
}
