using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using Lex.V3.Contracts;
using Lex.V3.Contracts.Platform;

namespace Lex.V3.Api;

/// <summary>
/// What Coverage declares beside its counts (the specification of record's R78, R82 and R12; R26's sentence reads them too): the
/// Luxembourg classes this mount does not hold, each declared once, at family level, with a typed disposition; how many of the EU's
/// official languages the EU index holds wording in; and when Lex's own observations of each publisher begin, from the log and the
/// build report the mount holds. Served on lex-v3-luxembourg-index/9 only: an index of schema 8 or 6 answers as it always did.
/// </summary>
internal sealed partial class V3CorpusMount
{
    /// <summary>
    /// The Luxembourg classes not held, by the publisher's own legal-type code where it has one (the resource-type authority's
    /// suffix, <c>LuxembourgActClassManifest</c>), in R78's order, each with its disposition, then every other code the build's
    /// resolver recognizes (the resolver's never types, ACCA and RC, are out of scope). Run 15's families (codes, lois and grand-ducal
    /// regulations: CODE, LOI and RGD) are held and are not declared here: the text yield counts them.
    /// </summary>
    internal static readonly IReadOnlyList<(string Class, string Disposition)> LuxembourgClassesNotHeld = Array.AsReadOnly(new[]
    {
        ("Constitution", "awaiting_a_run"),
        ("AGD", "admitted_awaiting_a_run"),
        ("ARGD", "admitted_awaiting_a_run"),
        ("RMIN", "admitted_awaiting_a_run"),
        ("AMIN", "admitted_where_regulatory_awaiting_a_run"),
        ("RCSF", "admitted_first_awaiting_a_run"),
        ("RBCL", "admitted_first_awaiting_a_run"),
        ("RILR", "admitted_awaiting_a_run"),
        ("TC", "documentation_awaiting_a_run"),
        ("RECT", "corrective_awaiting_a_run"),
        ("ACC", "constitutional_review_awaiting_a_run"),
        ("memorial_b", "out_of_scope"),
        ("communal_regulations", "out_of_scope"),
        ("A", "no_ruling_admits_it"),
        ("AGC", "no_ruling_admits_it"),
        ("CONV", "no_ruling_admits_it"),
        ("ORD", "no_ruling_admits_it"),
        ("PROT", "no_ruling_admits_it"),
        ("REG", "no_ruling_admits_it"),
        ("RGC", "no_ruling_admits_it"),
        ("RI", "no_ruling_admits_it"),
        ("ST", "no_ruling_admits_it"),
        ("ACCA", "out_of_scope"),
        ("RC", "out_of_scope"),
        ("DIV", "no_ruling_admits_it"),
        ("PA", "no_ruling_admits_it"),
        ("RECUEIL", "no_ruling_admits_it"),
        ("CODE_RECUEIL", "no_ruling_admits_it"),
    });

    /// <summary>The dispositions a class not held may have, closed (R78).</summary>
    internal static readonly IReadOnlyList<string> LuxembourgClassDispositions = Array.AsReadOnly(new[]
    {
        "awaiting_a_run", "admitted_awaiting_a_run", "admitted_where_regulatory_awaiting_a_run", "admitted_first_awaiting_a_run",
        "documentation_awaiting_a_run", "corrective_awaiting_a_run", "constitutional_review_awaiting_a_run", "out_of_scope",
        "no_ruling_admits_it",
    });

    /// <summary>The EU's official languages (Regulation No 1 of 1958, as last amended on Croatia's accession): the R82 denominator.</summary>
    internal const int EuropeOfficialLanguages = 24;

    internal const string DeclaredScopeRule =
        "luxembourg_classes_not_held declares, once per class and by the publisher's legal-type code where it has one, each Luxembourg " +
        "class this mount does not hold, with its disposition: awaiting_a_run (not admitted or refused by a ruling yet, and a bounded run " +
        "the owner authorises would collect it), admitted_awaiting_a_run, admitted_first_awaiting_a_run (among the first after launch), " +
        "admitted_where_regulatory_awaiting_a_run (only an act a role rule shows is regulatory), documentation_awaiting_a_run, " +
        "corrective_awaiting_a_run and constitutional_review_awaiting_a_run (admitted, under that label only, never as statutory text), " +
        "out_of_scope (not held and not planned), no_ruling_admits_it (not collected, and no ruling admits it yet); the families held are " +
        "the text yield's; europe_languages names the languages the EU index holds wording in, of the EU's official languages; " +
        "observations_begin gives, per publisher, when Lex's own observations held here begin: the first build the Luxembourg log " +
        "records (first_build_in_log), and for the EU the completion of the one acquisition the mount holds, as its build report states " +
        "it (acquisition_completed), or not_stated with a null date; date is that instant's calendar date in Luxembourg (the instant itself is " +
        "history's first build time, or the EU channel's newest completed observation in the envelope's freshness)";

    /// <summary>A coverage answer with its <c>declared_scope</c> member on index 9; on an index of schema 8 or 6 it is unchanged.</summary>
    private object WithDeclaredScope(object result)
    {
        if (!RulesByInterval)
        {
            return result;
        }

        var node = result as JsonObject ?? JsonSerializer.SerializeToNode(result)!.AsObject();
        node["declared_scope"] = JsonSerializer.SerializeToNode(DeclaredScopeBlock());
        return node;
    }

    /// <summary>The <c>declared_scope</c> member of coverage (index 9).</summary>
    private object DeclaredScopeBlock()
    {

        var observations = _reader!.ResolveObservations();
        var firstBuild = observations.Count == 0
            ? (DateTimeOffset?)null
            : DateTimeOffset.ParseExact(
                observations[0].BuiltAt, "yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal);
        var begins = new List<object> { ObservationsBegin("lu-legilux", firstBuild, "first_build_in_log") };
        if (_europeReader is not null)
        {
            var europe = _freshness.Channels.SingleOrDefault(static channel => channel.Publisher == PublisherId.EuEurLex);
            begins.Add(ObservationsBegin("eu-eurlex", europe?.NewestCompletedObservation, "acquisition_completed"));
        }

        return new
        {
            luxembourg_classes_not_held = LuxembourgClassesNotHeld
                .Select(static row => new { @class = row.Class, disposition = row.Disposition })
                .ToArray(),
            europe_languages = _europeReader is null
                ? null
                : new
                {
                    held = _europeReader.SearchableLanguages(),
                    official = EuropeOfficialLanguages,
                },
            observations_begin = begins,
            rule = DeclaredScopeRule,
        };
    }

    private static object ObservationsBegin(string publisher, DateTimeOffset? at, string basis) => new
    {
        publisher,
        date = at is { } day ? LuxembourgCalendar.DateAt(day).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) : null,
        basis = at is null ? "not_stated" : basis,
    };
}
