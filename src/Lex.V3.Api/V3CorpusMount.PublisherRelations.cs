using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Lex.V3.Ingest.Luxembourg;
using Lex.V3.Ingest.Names;

namespace Lex.V3.Api;

/// <summary>
/// The publisher's relation records of a Luxembourg act on lex-v3-luxembourg-index/10 (the specification of record's R29, R72,
/// R21; the Release 3 scope's item 2): the typed edges <c>relations</c> serves and the dossier's relation tabs list, in both
/// directions, each with the acting act's dates by role, the families' acquisition states, and R72's notice that an act
/// published after the latest consolidated version in the publisher's catalogue amends this one. An index of schema 9, 8 or 6
/// holds no relation record, and every answer over it is unchanged: no member is added.
/// </summary>
internal sealed partial class V3CorpusMount
{
    internal const string PublisherRelationsRule =
        "the publisher's relation records about this act, read from the index's relation tables (lex-v3-luxembourg-index/10): the act-level " +
        "relations jolux:modifies, jolux:repeals and jolux:rectifies (the publisher's subject amends, repeals or rectifies its object) and " +
        "jolux:modifiedTempBy (the subject is modified for a time by the object), and the publisher's jolux:LegalResourceImpact records, its " +
        "table of modifications (the act a change comes from, the act it is made to, its type and the consolidated version the publisher says " +
        "integrated it); each edge is listed from this act's side, outbound when this act is the publisher's subject (for an impact record, the " +
        "act the change comes from) and inbound when it is the object (the act the change is made to); an edge whose derived is true is read " +
        "from a record the publisher keeps on another resource (an act-level relation asserted on the other act, or an impact record), so it " +
        "is derived from the publisher's forward relation and not asserted on this act; each other act is named by its identifier, with its " +
        "title where the publisher's catalogue states one; the acting act's publication date and entry into force are the publisher's own facts, " +
        "each the one date stated, said with its role and never as an interval; an edge says that the publisher records the relation and " +
        "nothing about its effect on any provision or date; the records are of the act as a whole, served whole whatever date, language, " +
        "direction or page is asked of the references; families gives each relation's acquisition state, and an absence is said only of a " +
        "relation acquired in full";

    internal const string PublisherRelationsDerivedLabel = "derived from the publisher's forward relation";

    internal const string AmendedAfterConsolidationRule =
        "R72, as the specification of record writes it: an act the publisher records as modifying this act (jolux:modifies, asserted on " +
        "the amending act and read from this act's side, so derived from the publisher's forward relation) that was published after the " +
        "latest consolidated version this index holds, that is, whose publication date is later than the publication date of every " +
        "consolidation or code state whose text this index holds, of this act and of every act the publisher's table of modifications " +
        "(jolux:impactConsolidatedBy, on the impact records this act received) names as integrating its changes; an amending act whose " +
        "impact record names, as integrating it, a version whose text this index holds is never later; status notice names each such act " +
        "with its publication date, never its effect, and does not say that a version leaves the change out; no_later_amending_act says " +
        "each recorded amending act was published on or before a held version's publication date or is integrated in a held version; " +
        "undetermined says a held version or an amending act has no publication date the publisher's facts state as one date, or an " +
        "impact record names its integrating version more than once, so the comparison cannot be made for an act it would name; " +
        "no_amending_act_recorded says the relation records name none, said only of a modifies relation acquired in full; not_acquired says " +
        "none is recorded and the modifies relation was not acquired in full, so nothing is said; a temporary modification " +
        "(jolux:modifiedTempBy) is listed among the relations and does not raise it; an act this act names as amending itself is not counted";

    // ---- The views of an edge, from this act's side, in the order a dossier lists them. ----
    private const string ViewAmends = "amends";
    private const string ViewAmendedBy = "amended_by";
    private const string ViewRepeals = "repeals";
    private const string ViewRepealedBy = "repealed_by";
    private const string ViewModifiedTemporarilyBy = "modified_temporarily_by";
    private const string ViewModifiesTemporarily = "modifies_temporarily";
    private const string ViewRectifies = "rectifies";
    private const string ViewRectifiedBy = "rectified_by";
    private const string ViewImpactMade = "impact_made";
    private const string ViewImpactReceived = "impact_received";

    internal static readonly string[] PublisherRelationViews =
    [
        ViewAmendedBy, ViewAmends, ViewRepealedBy, ViewRepeals, ViewModifiedTemporarilyBy, ViewModifiesTemporarily,
        ViewRectifiedBy, ViewRectifies, ViewImpactReceived, ViewImpactMade,
    ];

    // The status of R72's notice for one act.
    internal const string AmendedAfterNotice = "notice";
    internal const string AmendedAfterNoLaterAct = "no_later_amending_act";
    internal const string AmendedAfterUndetermined = "undetermined";
    internal const string AmendedAfterNoAct = "no_amending_act_recorded";
    internal const string AmendedAfterNotAcquired = "not_acquired";

    private const string FamilyAcquiredComplete = "acquired_complete";

    // The index's edge types and state kinds (lex-v3-luxembourg-index/10's tokens).
    private const string EdgeModifies = "modifies";
    private const string EdgeRepeals = "repeals";
    private const string EdgeRectifies = "rectifies";
    private const string EdgeModifiedTemporarilyBy = "modified_temporarily_by";
    private const string EdgeImpact = "impact";
    private const string StateKindConsolidation = "consolidation";
    private const string StateKindCode = "code";

    /// <summary>The relation predicates behind each edge type, as the index's family table names them.</summary>
    private static readonly (string EdgeType, string[] Predicates)[] FamilyPredicates =
    [
        (EdgeModifies, ["jolux:modifies"]),
        (EdgeRepeals, ["jolux:repeals"]),
        (EdgeRectifies, ["jolux:rectifies"]),
        (EdgeModifiedTemporarilyBy, ["jolux:modifiedTempBy"]),
        (EdgeImpact, [
            "jolux:impactFromLegalResource", "jolux:impactToLegalResource", "jolux:impactToExpression", "jolux:legalResourceImpactHasType",
            "jolux:impactConsolidatedBy", "jolux:impactConsolidatedByExpression",
        ]),
    ];

    private static readonly Regex RelationFamilyKey = new("^legislative-([a-z]+)-g$", RegexOptions.CultureInvariant);

    /// <summary>One amending act R72 compares: its IRI, its publication date (null when none is one date) and its evidence.</summary>
    internal sealed record AmendingAct(string Iri, string? PublicationDate, string EvidenceSha256);

    /// <summary>
    /// One consolidation or code state R72 may compare against: its work, its legal resource, its kind, its text status and its
    /// publication date (null when the catalogue states none as one date).
    /// </summary>
    internal sealed record ConsolidationFacts(string WorkKey, string LegalResourceIri, string? StateKind, string TextStatus, string? PublicationDate);

    /// <summary>
    /// One impact record this act received: the act the change comes from, the version the publisher says integrated it (null when it
    /// names none, or more than one), and whether it names more than one.
    /// </summary>
    internal sealed record ReceivedImpact(string FromIri, string? ConsolidatedByIri, bool ConsolidatedByAmbiguous);

    /// <summary>R72's reading of one act: its status, the acts it names, and what it compared.</summary>
    internal sealed record AmendedAfter(
        string Status,
        IReadOnlyList<AmendingAct> Later,
        ConsolidationFacts? LatestHeld,
        int HeldVersions,
        int HeldVersionsWithoutPublicationDate,
        IReadOnlyList<string> IntegratingWorkKeys,
        int AmendingActs,
        int AmendingActsWithoutPublicationDate,
        int AmendingActsIntegrated,
        int AmendingActsIntegrationUndecided);

    /// <summary>
    /// R72's rule (<see cref="AmendedAfterConsolidationRule"/>), as the specification of record writes it: over the consolidation and
    /// code states whose text this index holds, of this act (<paramref name="ownStates"/>) and of every act the impact records it
    /// received name as integrating its changes (<paramref name="statesOfWork"/>, by work key), and its recorded amending acts, each
    /// once. An amending act an impact record says is integrated in a held version is never later; one whose impact record names its
    /// integrating version more than once is undecided. A notice when an amending act is later than every held version (or none is
    /// held) and no date it needs is missing; undetermined when one is, or the act would be named and is undecided; else no later
    /// amending act, or none recorded. A pure function, so the run-15 probe measures what the server says.
    /// </summary>
    internal static AmendedAfter AmendedAfterLatestHeldConsolidation(
        string workKey,
        IReadOnlyList<ConsolidationFacts> ownStates,
        Func<string, IReadOnlyList<ConsolidationFacts>> statesOfWork,
        IReadOnlyList<AmendingAct> amendingActs,
        IReadOnlyList<ReceivedImpact> impacts)
    {
        ArgumentNullException.ThrowIfNull(ownStates);
        ArgumentNullException.ThrowIfNull(statesOfWork);
        ArgumentNullException.ThrowIfNull(amendingActs);
        ArgumentNullException.ThrowIfNull(impacts);
        var acts = amendingActs
            .GroupBy(static act => act.Iri, StringComparer.Ordinal)
            .Select(static group => group.First())
            .ToArray();
        // The acts the publisher's table of modifications names as integrating this act's changes, other than this act itself.
        var integrating = impacts
            .Select(static impact => impact.ConsolidatedByIri)
            .Where(static iri => iri is not null)
            .Select(static iri => PrivateIndividuals.LuxembourgWorkIriOf(iri!) is { } work ? WorkKeyOfIri(work) : null)
            .Where(key => key is not null && !string.Equals(key, workKey, StringComparison.Ordinal))
            .Select(static key => key!)
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToArray();
        // The held versions: each consolidation or code state whose text this index holds, once per legal resource, with its one
        // publication date (null when its rows state none or two).
        var held = ownStates.Concat(integrating.SelectMany(statesOfWork))
            .Where(static state => state.StateKind is StateKindConsolidation or StateKindCode &&
                                   string.Equals(state.TextStatus, TextHeld, StringComparison.Ordinal))
            .GroupBy(static state => state.LegalResourceIri, StringComparer.Ordinal)
            .Select(static group => group.Select(static state => state.PublicationDate).Distinct(StringComparer.Ordinal).ToArray() is [var one]
                ? group.First() with { PublicationDate = one }
                : group.First() with { PublicationDate = null })
            .ToArray();
        var heldResources = held.Select(static state => state.LegalResourceIri).ToHashSet(StringComparer.Ordinal);
        var dated = held.Where(static state => state.PublicationDate is not null).ToArray();
        var missing = held.Length - dated.Length;
        var latest = dated
            .OrderByDescending(static state => state.PublicationDate, StringComparer.Ordinal)
            .ThenBy(static state => string.Equals(state.WorkKey, workKey, StringComparison.Ordinal) ? 0 : 1)
            .ThenByDescending(static state => state.LegalResourceIri, StringComparer.Ordinal)
            .FirstOrDefault();
        // An impact record belongs to an amending act when both name the same act (its work, its act as published or a part of it).
        static string ActOf(string iri) => PrivateIndividuals.LuxembourgWorkIriOf(iri) ?? iri;
        var integrated = acts
            .Where(act => impacts.Any(impact => string.Equals(ActOf(impact.FromIri), ActOf(act.Iri), StringComparison.Ordinal) &&
                                                 impact.ConsolidatedByIri is { } version && heldResources.Contains(version)))
            .Select(static act => act.Iri)
            .ToHashSet(StringComparer.Ordinal);
        var undecided = acts
            .Where(act => !integrated.Contains(act.Iri) &&
                          impacts.Any(impact => string.Equals(ActOf(impact.FromIri), ActOf(act.Iri), StringComparison.Ordinal) &&
                                                impact.ConsolidatedByAmbiguous))
            .Select(static act => act.Iri)
            .ToHashSet(StringComparer.Ordinal);
        var candidates = acts.Where(act => !integrated.Contains(act.Iri)).ToArray();
        var undated = candidates.Count(static act => act.PublicationDate is null);
        var later = candidates
            .Where(act => act.PublicationDate is not null &&
                          (latest is null || string.CompareOrdinal(act.PublicationDate, latest.PublicationDate) > 0))
            .OrderBy(static act => act.PublicationDate, StringComparer.Ordinal)
            .ThenBy(static act => act.Iri, StringComparer.Ordinal)
            .ToArray();
        var named = later.Where(act => !undecided.Contains(act.Iri)).ToArray();
        var status = acts.Length == 0
            ? AmendedAfterNoAct
            : named.Length > 0 && missing == 0
                ? AmendedAfterNotice
                : later.Length > 0 || undated > 0
                    ? AmendedAfterUndetermined
                    : AmendedAfterNoLaterAct;
        return new AmendedAfter(
            status, status == AmendedAfterNotice ? named : [], latest, held.Length, missing, integrating, acts.Length, undated,
            integrated.Count, undecided.Count);
    }

    /// <summary>Whether the mounted Luxembourg index holds the publisher's relation records (index 10).</summary>
    private bool RecordsPublisherRelations => _reader?.RecordsPublisherRelations == true;

    /// <summary>
    /// Another act an edge names, as an answer names it: its work key and site address where it has one, its title, and its own
    /// publication date and entry into force, each the one date the publisher's facts state, else none.
    /// </summary>
    private sealed record OtherAct(
        string Iri, string? WorkKey, string? Address, bool Held, string? Title, bool TitleWithheld, string? PublicationDate, string? EntryIntoForce);

    /// <summary>One edge from this act's side, before it is written.</summary>
    private sealed record RelationEdge(
        string View,
        string EdgeType,
        string SourcePredicate,
        string Direction,
        bool Derived,
        string AssertedOn,
        string? SubjectIri,
        string? ObjectIri,
        string? ActingIri,
        string? ActedOnIri,
        string OtherIri,
        bool IsSelfReference,
        string? ActingPublicationDate,
        string? ActingEntryIntoForce,
        string EvidenceSha256,
        LuxembourgIndexPublisherImpact? Impact);

    /// <summary>
    /// An answer about a work with its publisher relation records and R72's notice, on lex-v3-luxembourg-index/10 only; on an
    /// index of schema 9, 8 or 6 the answer is unchanged. <paramref name="direction"/> keeps the edges of one side, as
    /// <c>relations</c> asks; the dossier lists both.
    /// </summary>
    private object WithPublisherRelations(object result, string workKey, string direction = "both")
    {
        if (!RecordsPublisherRelations)
        {
            return result;
        }

        var node = result as JsonObject ?? JsonSerializer.SerializeToNode(result)!.AsObject();
        var catalogue = CatalogueOf(workKey);
        // The work's own IRIs: the work, its act as published (named whether or not the catalogue dates a state of it, since the
        // publisher's relations name an act by it) and every state's legal resource.
        var workIris = catalogue.Rows
            .SelectMany(static row => new[] { row.PublisherWorkIri, row.PublisherWorkIri + "/jo", row.PublisherLegalResourceIri })
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToArray();
        var ownWork = catalogue.Rows.Select(static row => row.PublisherWorkIri).FirstOrDefault();
        var edges = EdgesOf(workIris, ownWork);
        var families = FamiliesOf(_reader!.ResolveRelationFamilies());
        var others = OtherActsOf(edges.Select(static edge => edge.OtherIri));
        var listed = edges.Where(edge => direction == "both" || string.Equals(edge.Direction, direction, StringComparison.Ordinal)).ToArray();
        var stateOf = StateOfLegalResource(catalogue);
        node["publisher_relations"] = new JsonObject
        {
            ["rule"] = PublisherRelationsRule,
            ["inverse_label"] = PublisherRelationsDerivedLabel,
            ["not_seen"] = NotSeenOf(families),
            ["families"] = new JsonArray(families.Select(static family => (JsonNode)new JsonObject
            {
                ["edge_type"] = family.EdgeType,
                ["source_predicates"] = new JsonArray(family.Predicates.Select(static predicate => (JsonNode)predicate).ToArray()),
                ["state"] = family.State,
                ["family_keys"] = new JsonArray(family.FamilyKeys.Select(static key => (JsonNode)key).ToArray()),
                ["subject_classes"] = family.SubjectClasses is null
                    ? null
                    : new JsonArray(family.SubjectClasses.Select(static value => (JsonNode)value).ToArray()),
                ["reason"] = family.Reason,
            }).ToArray()),
            ["edge_count"] = listed.Length,
            ["edge_counts"] = new JsonObject(PublisherRelationViews.Select(view =>
                new KeyValuePair<string, JsonNode?>(view, listed.Count(edge => string.Equals(edge.View, view, StringComparison.Ordinal))))),
            ["edges"] = new JsonArray(listed.Select(edge => (JsonNode)EdgeNode(edge, others[edge.OtherIri], stateOf)).ToArray()),
        };
        node["amended_after_consolidation"] = AmendedAfterNode(workKey, catalogue, edges, families, others);
        return node;
    }

    /// <summary>Every edge of one act's IRIs, from its side, in view order, then the acting act's publication date and the other act.</summary>
    private RelationEdge[] EdgesOf(IReadOnlyList<string> workIris, string? ownWork)
    {
        var own = new HashSet<string>(workIris, StringComparer.Ordinal);
        bool Self(string other) => ownWork is not null &&
            string.Equals(PrivateIndividuals.LuxembourgWorkIriOf(other), ownWork, StringComparison.Ordinal);
        var edges = new List<RelationEdge>();
        foreach (var relation in _reader!.ResolvePublisherRelations(workIris))
        {
            if (own.Contains(relation.SubjectIri))
            {
                var view = relation.EdgeType switch
                {
                    EdgeModifies => ViewAmends,
                    EdgeRepeals => ViewRepeals,
                    EdgeRectifies => ViewRectifies,
                    _ => ViewModifiedTemporarilyBy,
                };
                edges.Add(EdgeOf(relation, view, "outbound", derived: false, "this_act", relation.ObjectIri, Self(relation.ObjectIri)));
            }

            if (own.Contains(relation.ObjectIri))
            {
                var view = relation.EdgeType switch
                {
                    EdgeModifies => ViewAmendedBy,
                    EdgeRepeals => ViewRepealedBy,
                    EdgeRectifies => ViewRectifiedBy,
                    _ => ViewModifiesTemporarily,
                };
                edges.Add(EdgeOf(relation, view, "inbound", derived: true, "other_act", relation.SubjectIri, Self(relation.SubjectIri)));
            }
        }

        foreach (var impact in _reader.ResolvePublisherImpacts(workIris))
        {
            if (impact.FromLegalResourceIri is { } from && own.Contains(from) && impact.ToLegalResourceIri is { } to)
            {
                edges.Add(ImpactEdgeOf(impact, ViewImpactMade, "outbound", to, Self(to)));
            }

            if (impact.ToLegalResourceIri is { } target && own.Contains(target) && impact.FromLegalResourceIri is { } source)
            {
                edges.Add(ImpactEdgeOf(impact, ViewImpactReceived, "inbound", source, Self(source)));
            }
        }

        return edges
            .OrderBy(static edge => Array.IndexOf(PublisherRelationViews, edge.View))
            .ThenBy(static edge => edge.ActingPublicationDate is null ? 1 : 0)
            .ThenBy(static edge => edge.ActingPublicationDate, StringComparer.Ordinal)
            .ThenBy(static edge => edge.OtherIri, StringComparer.Ordinal)
            .ThenBy(static edge => edge.SubjectIri ?? edge.Impact?.ImpactIri, StringComparer.Ordinal)
            .ToArray();

        static RelationEdge EdgeOf(
            LuxembourgIndexPublisherRelation relation, string view, string direction, bool derived, string assertedOn, string other, bool self) =>
            new(view, relation.EdgeType, relation.SourcePredicate, direction, derived, assertedOn, relation.SubjectIri, relation.ObjectIri,
                relation.ActingIri, relation.ActedOnIri, other, self, relation.ActingPublicationDate, relation.ActingEntryIntoForce,
                relation.EvidenceSha256, null);

        static RelationEdge ImpactEdgeOf(LuxembourgIndexPublisherImpact impact, string view, string direction, string other, bool self) =>
            new(view, EdgeImpact, "jolux:LegalResourceImpact", direction, true, "impact_record", null, null,
                impact.FromLegalResourceIri, impact.ToLegalResourceIri, other, self, impact.ActingPublicationDate, impact.ActingEntryIntoForce,
                impact.EvidenceSha256, impact);
    }

    /// <summary>One relation family's state for an edge type: the weakest of its predicates', with the families that proved them.</summary>
    private sealed record EdgeFamily(
        string EdgeType, string[] Predicates, string State, string[] FamilyKeys, string[]? SubjectClasses, string? Reason);

    private static EdgeFamily[] FamiliesOf(IReadOnlyList<LuxembourgIndexRelationFamily> families)
    {
        var byPredicate = families.ToDictionary(static family => family.Predicate, StringComparer.Ordinal);
        return FamilyPredicates.Select(entry =>
        {
            var found = entry.Predicates.Select(predicate => byPredicate.GetValueOrDefault(predicate)).ToArray();
            var complete = found.All(static family => family is not null && string.Equals(family.State, FamilyAcquiredComplete, StringComparison.Ordinal));
            var keys = found.Where(static family => family is not null).SelectMany(static family => family!.FamilyKeys)
                .Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
            var notComplete = found.FirstOrDefault(static family => family is null || !string.Equals(family.State, FamilyAcquiredComplete, StringComparison.Ordinal));
            var classes = complete && keys.Length > 0 && keys.All(key => RelationFamilyKey.IsMatch(key))
                ? keys.Select(key => RelationFamilyKey.Match(key).Groups[1].Value).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray()
                : null;
            return new EdgeFamily(
                entry.EdgeType,
                entry.Predicates,
                complete ? FamilyAcquiredComplete : notComplete?.State ?? "unacquired",
                complete ? keys : [],
                classes,
                complete ? null : notComplete?.Reason ?? "this index holds no acquisition state for this relation predicate");
        }).ToArray();
    }

    /// <summary>What the relation family does not see, in the families' own classes, or that it holds no relation in full.</summary>
    private static string NotSeenOf(IReadOnlyList<EdgeFamily> families)
    {
        var classes = families.Where(static family => family.SubjectClasses is not null).SelectMany(static family => family.SubjectClasses!)
            .Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
        if (classes.Length == 0)
        {
            return "no relation family is held as acquired in full by classes of identifier, so no relation record is said to be absent";
        }

        var list = classes.Length == 1 ? classes[0] : string.Join(", ", classes[..^1]) + " and " + classes[^1];
        return $"the relation family holds the publisher's records whose subject identifier is under {list} " +
               $"({string.Join(", ", classes.Select(static value => "eli/etat/leg/" + value + "/"))}); a record whose subject is elsewhere was " +
               "not acquired, so an act of another class that amends, repeals or rectifies this act is not seen here, nor is an act of " +
               "another class that this act modifies for a time";
    }

    /// <summary>The other acts the edges name, each once: work key, site address, whether held, and title (R92: none of a withheld act).</summary>
    private IReadOnlyDictionary<string, OtherAct> OtherActsOf(IEnumerable<string> iris)
    {
        var distinct = iris.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
        if (distinct.Length == 0)
        {
            return new Dictionary<string, OtherAct>(StringComparer.Ordinal);
        }

        var works = distinct.ToDictionary(static iri => iri, static iri => PrivateIndividuals.LuxembourgWorkIriOf(iri), StringComparer.Ordinal);
        var held = _reader!.ResolveHeldWorks(distinct.Concat(works.Values.Where(static work => work is not null).Select(static work => work!))
            .Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray());
        var facts = _reader.ResolveFacts(distinct.Concat(distinct.Select(static iri => iri + "/fr")).ToArray());
        var described = facts.Select(static fact => fact.SubjectIri).ToHashSet(StringComparer.Ordinal);
        var titles = facts
            .Where(static fact => fact.Predicate == "title" && string.Equals(fact.ObjectKind, "literal", StringComparison.Ordinal))
            .GroupBy(static fact => fact.SubjectIri, StringComparer.Ordinal)
            .ToDictionary(static group => group.Key, static group => group.Select(static fact => fact.ObjectValue).First(), StringComparer.Ordinal);
        var bySubject = facts.ToLookup(static fact => fact.SubjectIri, StringComparer.Ordinal);
        string? OneDate(string subject, string predicate) => bySubject[subject]
            .Where(fact => string.Equals(fact.Predicate, predicate, StringComparison.Ordinal))
            .Select(static fact => fact.ObjectValue)
            .Distinct(StringComparer.Ordinal)
            .ToArray() is [var one] && DateOnly.TryParseExact(one, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out _)
            ? one
            : null;
        return distinct.ToDictionary(static iri => iri, iri =>
        {
            var work = works[iri];
            var key = held.GetValueOrDefault(iri) ?? (work is null ? null : held.GetValueOrDefault(work)) ?? (work is null ? null : WorkKeyOfIri(work));
            var isHeld = held.ContainsKey(iri) || (work is not null && held.ContainsKey(work));
            var withheld = TitlesWithheld(key, work);
            var title = withheld ? null : titles.GetValueOrDefault(iri + "/fr");
            // A site address where this site answers for the act: its dossier when held, its catalogue record when the catalogue
            // describes it; none for an identifier outside both.
            var address = key is not null && (isHeld || described.Contains(iri)) ? "/lu-legilux/" + key : null;
            return new OtherAct(iri, key, address, isHeld, title, withheld, OneDate(iri, "publicationDate"), OneDate(iri, "dateEntryInForce"));
        }, StringComparer.Ordinal);
    }

    private static JsonObject OtherNode(OtherAct other) => new()
    {
        ["iri"] = other.Iri,
        ["work_key"] = other.WorkKey,
        ["address"] = other.Address,
        ["held"] = other.Held,
        ["title"] = other.Title,
        ["title_language"] = other.Title is null ? null : FrenchLanguage,
        ["title_withheld"] = other.TitleWithheld,
        ["publication_date"] = other.PublicationDate,
        ["entry_into_force"] = other.EntryIntoForce,
    };

    private static JsonObject EdgeNode(RelationEdge edge, OtherAct other, Func<string, LuxembourgIndexCatalogueState?> stateOf)
    {
        var node = new JsonObject
        {
            ["view"] = edge.View,
            ["edge_type"] = edge.EdgeType,
            ["source_predicate"] = edge.SourcePredicate,
            ["direction"] = edge.Direction,
            ["derived"] = edge.Derived,
            ["asserted_on"] = edge.AssertedOn,
            ["subject_iri"] = edge.SubjectIri,
            ["object_iri"] = edge.ObjectIri,
            ["acting_iri"] = edge.ActingIri,
            ["acted_on_iri"] = edge.ActedOnIri,
            ["is_self_reference"] = edge.IsSelfReference,
            ["other"] = OtherNode(other),
            ["acting_publication_date"] = edge.ActingPublicationDate,
            ["acting_entry_into_force"] = edge.ActingEntryIntoForce,
            ["evidence_sha256"] = edge.EvidenceSha256,
            ["impact"] = null,
        };
        if (edge.Impact is { } impact)
        {
            // The consolidated version the publisher says integrated the change, as this work's catalogue or its own act's names it
            // (a code state, for an act whose changes a code integrates), where one does.
            var state = impact.ConsolidatedByIri is null ? null : stateOf(impact.ConsolidatedByIri);
            node["impact"] = new JsonObject
            {
                ["impact_iri"] = impact.ImpactIri,
                ["type_iri"] = impact.ImpactTypeIri,
                ["type_code"] = impact.ImpactTypeIri is null ? null : AuthorityCode(impact.ImpactTypeIri),
                ["to_expression_iri"] = impact.ToExpressionIri,
                ["consolidated_by_iri"] = impact.ConsolidatedByIri,
                ["consolidated_by_expression_iri"] = impact.ConsolidatedByExpressionIri,
                ["consolidated_by_state"] = state is null ? null : CatalogueRowSummary(state),
                ["ambiguous_parts"] = new JsonArray(impact.AmbiguousParts.Select(static part => (JsonNode)part).ToArray()),
            };
        }

        return node;
    }

    /// <summary>One catalogue row as R72 compares it.</summary>
    private static ConsolidationFacts FactsOf(LuxembourgIndexCatalogueState row) =>
        new(row.WorkKey, row.PublisherLegalResourceIri, row.StateKind, row.TextStatus, row.PublicationDate);

    /// <summary>
    /// The catalogue row of a legal resource, French first, in this work's catalogue or in the catalogue of the act its IRI belongs
    /// to (a code state an impact record names as integrating a change): what an impact's integrating version is named by.
    /// </summary>
    private Func<string, LuxembourgIndexCatalogueState?> StateOfLegalResource(CatalogueWork own) => iri =>
    {
        var rows = own.Rows.Where(row => string.Equals(row.PublisherLegalResourceIri, iri, StringComparison.Ordinal)).ToArray();
        if (rows.Length == 0 && PrivateIndividuals.LuxembourgWorkIriOf(iri) is { } work && WorkKeyOfIri(work) is { } key)
        {
            rows = CatalogueOf(key).Rows.Where(row => string.Equals(row.PublisherLegalResourceIri, iri, StringComparison.Ordinal)).ToArray();
        }

        return rows
            .OrderBy(static row => row.Language == FrenchLanguage ? 0 : 1)
            .ThenBy(static row => row.Language, StringComparer.Ordinal)
            .FirstOrDefault();
    };

    /// <summary>R72's member: its status, the acts it names, what it compared, its rule, and the notice sentence when it fires.</summary>
    private JsonObject AmendedAfterNode(
        string workKey, CatalogueWork catalogue, IReadOnlyList<RelationEdge> edges, IReadOnlyList<EdgeFamily> families,
        IReadOnlyDictionary<string, OtherAct> others)
    {
        var modifies = families.Single(static family => family.EdgeType == EdgeModifies);
        var amending = edges
            .Where(static edge => edge.View == ViewAmendedBy && !edge.IsSelfReference)
            .Select(static edge => new AmendingAct(edge.ActingIri!, edge.ActingPublicationDate, edge.EvidenceSha256))
            .ToArray();
        var received = edges
            .Where(static edge => edge.View == ViewImpactReceived && edge.Impact is not null)
            .Select(static edge => new ReceivedImpact(
                edge.Impact!.FromLegalResourceIri!, edge.Impact.ConsolidatedByIri,
                edge.Impact.AmbiguousParts.Contains("jolux:impactConsolidatedBy", StringComparer.Ordinal)))
            .ToArray();
        IReadOnlyList<ConsolidationFacts> StatesOf(string key) => CatalogueOf(key).Rows.Select(FactsOf).ToArray();
        // A recorded amending act is the publisher's record whatever its family's state; that none is recorded is said only of a
        // relation acquired in full.
        var reading = AmendedAfterLatestHeldConsolidation(workKey, catalogue.Rows.Select(FactsOf).ToArray(), StatesOf, amending, received);
        if (amending.Length == 0 && !string.Equals(modifies.State, FamilyAcquiredComplete, StringComparison.Ordinal))
        {
            reading = reading with { Status = AmendedAfterNotAcquired };
        }

        var latest = reading.LatestHeld is not { } held
            ? null
            : StateOfLegalResource(catalogue)(held.LegalResourceIri);
        return new JsonObject
        {
            ["status"] = reading.Status,
            ["derived"] = true,
            ["source_predicate"] = "jolux:modifies",
            ["acts"] = new JsonArray(reading.Later.Select(act => (JsonNode)new JsonObject
            {
                ["acting_iri"] = act.Iri,
                ["publication_date"] = act.PublicationDate,
                ["other"] = OtherNode(others[act.Iri]),
                ["evidence_sha256"] = act.EvidenceSha256,
            }).ToArray()),
            ["latest_consolidation"] = latest is null ? null : CatalogueRowSummary(latest),
            ["latest_consolidation_publication_date"] = reading.LatestHeld?.PublicationDate,
            ["latest_consolidation_integrating"] = reading.LatestHeld is { } version && !string.Equals(version.WorkKey, workKey, StringComparison.Ordinal),
            ["integrating_work_keys"] = new JsonArray(reading.IntegratingWorkKeys.Select(static key => (JsonNode)key).ToArray()),
            ["held_versions"] = reading.HeldVersions,
            ["held_versions_without_publication_date"] = reading.HeldVersionsWithoutPublicationDate,
            ["amending_acts"] = reading.AmendingActs,
            ["amending_acts_without_publication_date"] = reading.AmendingActsWithoutPublicationDate,
            ["amending_acts_integrated"] = reading.AmendingActsIntegrated,
            ["amending_acts_integration_undecided"] = reading.AmendingActsIntegrationUndecided,
            ["notice"] = NoticeOf(reading, others, latest),
            ["rule"] = AmendedAfterConsolidationRule,
        };
    }

    /// <summary>
    /// R72's sentence, as the specification of record writes it, with the dates and acts filled in and the latest held version named
    /// (the site says it in its reviewed languages).
    /// </summary>
    private static string? NoticeOf(AmendedAfter reading, IReadOnlyDictionary<string, OtherAct> others, LuxembourgIndexCatalogueState? latest)
    {
        if (reading.Status != AmendedAfterNotice)
        {
            return null;
        }

        static string Named(OtherAct act) => act.Title is null ? act.Iri : $"{act.Title}, {act.Iri}";
        var acts = reading.Later;
        // The specification's sentence (R72), word for word but "this act" for its "this law", since the notice stands on every class
        // of act (the French review of 2026-10-28); then the version it compared, and that it is derived.
        var head = acts.Count == 1
            ? $"An act published on {acts[0].PublicationDate} ({Named(others[acts[0].Iri])}) amends this act; no consolidated version Lex " +
              "holds was published after it."
            : $"{acts.Count.ToString(CultureInfo.InvariantCulture)} acts published from {acts[0].PublicationDate} to {acts[^1].PublicationDate} " +
              "amend this act; no consolidated version Lex holds was published after them.";
        var latestSaid = latest is null || reading.LatestHeld is null
            ? "Lex holds no consolidated version of this act, or of another act the publisher records as integrating changes made to it."
            : reading.IntegratingWorkKeys.Contains(latest.WorkKey, StringComparer.Ordinal)
                ? $"The latest consolidated version Lex holds, {StableCoordinateOf(latest)}, was published on {reading.LatestHeld.PublicationDate}; " +
                  "it is a version of another act, one the publisher records as integrating changes made to this act."
                : $"The latest consolidated version Lex holds, {StableCoordinateOf(latest)}, was published on {reading.LatestHeld.PublicationDate}.";
        return $"{head} {latestSaid} This is derived from the publisher's records ({PublisherRelationsDerivedLabel}).";
    }

    private static string StableCoordinateOf(LuxembourgIndexCatalogueState row) => $"/lu-legilux/{row.WorkKey}/{row.ApplicabilityDate}";
}
