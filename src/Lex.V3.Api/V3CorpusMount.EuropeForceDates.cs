using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Lex.V3.Ingest.Europe;

namespace Lex.V3.Api;

/// <summary>
/// The EU act's force dates (the plan's item 2.1; R79's E1, R21): entry into force, application, end of validity and the
/// publisher's deadline with its qualifier word for word, each a value of its own with its role, as the EU index /6 holds
/// them from the publisher's reified date statements on the act's Cellar work. Served beside the act's wordings by
/// <c>dossier</c>, <c>timeline</c>, <c>as_of</c> and <c>evidence_bundle</c>; never a wording date, never merged with a
/// Luxembourg date, and never the ground of a selection. An index /5 or older holds none and its answers are unchanged.
/// </summary>
internal sealed partial class V3CorpusMount
{
    internal const string EuropeForceDatesScope =
        "the act's dates as the publisher records them in its dated statements (reified owl:Axiom statements on a CDM date property of " +
        "the act's Cellar work, acquired by the EU census and read from custody by the build): each date with its role, the value exactly " +
        "as served, the publisher's code and note word for word, the date property it sits on and the publisher's statement it was read " +
        "from (the EU index also holds the delivered batch it came in); they are the act's, said beside its wordings, never a wording's date, never a consolidated version's own date, and " +
        "never used to choose a wording";

    internal const string EuropeForceDateRoleRule =
        "entry_into_force is a date on the publisher's entry-into-force property (cdm:resource_legal_date_entry-into-force) with its " +
        "type-of-date code EV, and application one on that property with the code MA: two values of their own, never one; " +
        "end_of_validity is a date on the end-of-validity property (cdm:resource_legal_date_end-of-validity), whose note may say it ends " +
        "part of the act only; deadline is a date on the deadline property (cdm:resource_legal_date_deadline), whatever its note says, and " +
        "never called a transposition deadline; signature is a date on the signature property; not_pinned is a date on the " +
        "entry-into-force property whose code this site's table does not pin (such as MA/PART) or that carries none, said with its code " +
        "and given no role; two dates of one role are both said, and none is chosen";

    internal const string EuropeForceDateNoteRule =
        "publisher_code is the publisher's type-of-date code (ann:type_of_date) exactly as served, or null when the statement carries none; " +
        "publisher_note is the publisher's comment on the date (ann:comment_on_date) exactly as served, or null when there is none; " +
        "publisher_note_codes is the same note with each of its {CODE|IRI} carriers written as its code and nothing else changed; this site " +
        "translates no code; open_ended is true only for the publisher's open end (9999-12-31 at xsd:date), which is no date";

    internal const string EuropeForceDatesNotHeldRule =
        "roles_not_held names the roles of entry into force, application, end of validity and deadline for which no date is held: the " +
        "publisher's dated statements acquired for the act carry none of that role, and a date the publisher records without a dated " +
        "statement is not acquired, so a role not held is no statement that the act has no such date";

    internal const string EuropeForceDatesConflictRule =
        "conflicts names the entry into force when the publisher's statements give it more than one date; every date is said, and this " +
        "site chooses none";

    internal const string EuropeForceDatesNotHeldWhenHeld =
        "the act's own dates are said under force_dates, as the publisher's dated statements on the act give them; none of them is used " +
        "to choose a wording, each wording carries its own date, and a date the publisher records without a dated statement is not held";

    private static readonly Regex EuropeNoteCarrier = new(@"\{([^{}|]+)\|[^{}]*\}", RegexOptions.CultureInvariant);

    /// <summary>The publisher's note with each <c>{CODE|IRI}</c> carrier written as its code (<see cref="EuropeForceDateNoteRule"/>).</summary>
    internal static string? EuropeNoteCodes(string? note) =>
        note is null ? null : EuropeNoteCarrier.Replace(note, static match => match.Groups[1].Value);

    /// <summary>
    /// The force dates of the seed act's original work, or null when the EU index holds no force-date table (an index /5 or
    /// older) or the act has no original work in its census.
    /// </summary>
    private object? EuropeForceDatesOf(string? seed)
    {
        if (seed is null || _europeReader is not { HasForceDates: true })
        {
            return null;
        }

        var root = EuropeTimelineOf(seed).RootWorkIri;
        if (root is null)
        {
            return null;
        }

        var rows = _europeReader.ReadForceDates(root);
        var entries = rows.Where(static row => row.Role == EuropeForceDateRoles.EntryIntoForce)
            .Select(static row => row.DateValue).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
        return new
        {
            scope = EuropeForceDatesScope,
            publisher_work_id = root,
            seed_celex = seed,
            dates = rows.Select(static row => new
            {
                role = row.Role,
                date = row.DateValue,
                datatype = row.DatatypeIri,
                open_ended = row.OpenEnded,
                publisher_code = row.QualifierCode,
                publisher_note = row.PublisherComment,
                publisher_note_codes = EuropeNoteCodes(row.PublisherComment),
                date_property = row.SourcePredicateIri,
                statement = row.AxiomIri,
                date_identity_sha256 = row.DateIdentitySha256,
            }).ToArray(),
            roles_not_held = EuropeForceDateRoles.Named.Where(role => rows.All(row => row.Role != role)).ToArray(),
            conflicts = entries.Length > 1
                ? new object[] { new { role = EuropeForceDateRoles.EntryIntoForce, dates = entries } }
                : Array.Empty<object>(),
            role_rule = EuropeForceDateRoleRule,
            note_rule = EuropeForceDateNoteRule,
            not_held_rule = EuropeForceDatesNotHeldRule,
            conflict_rule = EuropeForceDatesConflictRule,
        };
    }

    /// <summary>
    /// An EU answer with its act's force dates: when the EU index holds them, <c>force_dates</c> is added (the envelope writes
    /// its members in its own canonical order), and the not-held row <c>force_dates</c> says what of them is still not held (<see cref="EuropeForceDatesNotHeldWhenHeld"/>);
    /// otherwise the answer is returned as it was, byte for byte.
    /// </summary>
    private JsonElement WithEuropeForceDates(JsonElement answer, string? seed)
    {
        var forceDates = EuropeForceDatesOf(seed);
        if (forceDates is null)
        {
            return answer;
        }

        var source = JsonNode.Parse(answer.GetRawText())!.AsObject();
        var target = new JsonObject();
        foreach (var (key, value) in source.ToArray())
        {
            if (string.Equals(key, "not_held", StringComparison.Ordinal))
            {
                target["force_dates"] = JsonSerializer.SerializeToNode(forceDates);
                var rows = new JsonArray();
                foreach (var row in value!.AsArray())
                {
                    var copy = row!.DeepClone().AsObject();
                    if (string.Equals((string?)copy["item"], "force_dates", StringComparison.Ordinal))
                    {
                        copy["reason"] = EuropeForceDatesNotHeldWhenHeld;
                    }

                    rows.Add(copy);
                }

                target[key] = rows;
                continue;
            }

            target[key] = value?.DeepClone();
        }

        if (!target.ContainsKey("force_dates"))
        {
            throw new InvalidOperationException("An EU answer that says the act's force dates carries a not_held list.");
        }

        return JsonSerializer.SerializeToElement(target);
    }
}
