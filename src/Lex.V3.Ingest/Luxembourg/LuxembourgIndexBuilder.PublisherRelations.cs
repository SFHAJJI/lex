using System.Text.Json;
using Lex.V3.Contracts;
using Lex.V3.Contracts.Source.Luxembourg;

namespace Lex.V3.Ingest.Luxembourg;

/// <summary>
/// Index 10's publisher relation records (the specification of record's R29, R72; the Release 3 scope's item 2): the relations
/// run 15 acquired as set G (<c>relation-assertions</c>, over the code, loi and rgd families), resolved by the scope resolver and
/// never projected before. Four act-level relations are typed edges, each in the publisher's own direction: <c>modifies</c>,
/// <c>repeals</c>, <c>rectifies</c> (the subject is the act that amends, repeals or rectifies the object) and
/// <c>modified_temporarily_by</c> (<c>jolux:modifiedTempBy</c>: the subject is the act modified, the object the act that
/// modifies it for a time). The publisher's <c>jolux:LegalResourceImpact</c> records (its machine-readable table of
/// modifications) are one row each: the act the change comes from, the act it is made to, its type and the consolidation the
/// publisher says integrated it. Each row keeps the digest of the observation it was read from and the acting act's
/// publication date and entry into force, each the one exact date the publisher's facts state, else none. The families'
/// acquisition states are held beside them, so an absence is said only of a family acquired in full. Pure functions of the
/// envelope's resolved relations, its family states and the fact rows, so a read-only probe runs the same projection.
/// </summary>
public static partial class LuxembourgIndexBuilder
{
    // ---- Tokens of the relation tables (index 10). ----
    internal const string EdgeModifies = "modifies";
    internal const string EdgeRepeals = "repeals";
    internal const string EdgeRectifies = "rectifies";
    internal const string EdgeModifiedTemporarilyBy = "modified_temporarily_by";

    /// <summary>The JOLux namespace every relation predicate of the closed vocabulary is under.</summary>
    internal const string JoluxNamespace = "http://data.legilux.public.lu/resource/ontology/jolux#";

    /// <summary>
    /// Index 10's relation tables. <c>publisher_relations</c>: one row per act-level relation the publisher asserts, keyed by its
    /// type and its two IRIs, the source predicate fixed by the type. <c>publisher_impacts</c>: one row per impact record, each part
    /// the one value the publisher gives it or null (none, or more than one, the latter named in <c>ambiguous_parts_json</c>).
    /// <c>relation_families</c>: each relation predicate's acquisition state with the families that proved it.
    /// </summary>
    internal const string PublisherRelationsDdl = """
        CREATE TABLE publisher_relations (
          edge_type TEXT COLLATE BINARY NOT NULL CHECK (edge_type IN ('modifies', 'repeals', 'rectifies', 'modified_temporarily_by')),
          source_predicate TEXT COLLATE BINARY NOT NULL,
          subject_iri TEXT COLLATE BINARY NOT NULL,
          object_iri TEXT COLLATE BINARY NOT NULL,
          acting_publication_date TEXT COLLATE BINARY,
          acting_entry_into_force TEXT COLLATE BINARY,
          evidence_sha256 TEXT COLLATE BINARY NOT NULL CHECK (length(evidence_sha256) = 64),
          PRIMARY KEY (edge_type, subject_iri, object_iri),
          CHECK (source_predicate = CASE edge_type WHEN 'modifies' THEN 'jolux:modifies' WHEN 'repeals' THEN 'jolux:repeals'
            WHEN 'rectifies' THEN 'jolux:rectifies' ELSE 'jolux:modifiedTempBy' END)
        ) STRICT;
        CREATE INDEX publisher_relations_subject ON publisher_relations(subject_iri, edge_type);
        CREATE INDEX publisher_relations_object ON publisher_relations(object_iri, edge_type);
        CREATE TABLE publisher_impacts (
          impact_iri TEXT COLLATE BINARY NOT NULL PRIMARY KEY,
          from_legal_resource_iri TEXT COLLATE BINARY,
          to_legal_resource_iri TEXT COLLATE BINARY,
          to_expression_iri TEXT COLLATE BINARY,
          impact_type_iri TEXT COLLATE BINARY,
          consolidated_by_iri TEXT COLLATE BINARY,
          consolidated_by_expression_iri TEXT COLLATE BINARY,
          ambiguous_parts_json TEXT COLLATE BINARY NOT NULL,
          acting_publication_date TEXT COLLATE BINARY,
          acting_entry_into_force TEXT COLLATE BINARY,
          evidence_sha256 TEXT COLLATE BINARY NOT NULL CHECK (length(evidence_sha256) = 64),
          CHECK (from_legal_resource_iri IS NOT NULL OR (acting_publication_date IS NULL AND acting_entry_into_force IS NULL))
        ) STRICT;
        CREATE INDEX publisher_impacts_from ON publisher_impacts(from_legal_resource_iri);
        CREATE INDEX publisher_impacts_to ON publisher_impacts(to_legal_resource_iri);
        CREATE TABLE relation_families (
          predicate TEXT COLLATE BINARY NOT NULL PRIMARY KEY,
          state TEXT COLLATE BINARY NOT NULL CHECK (state IN ('acquired_complete', 'unacquired', 'incomplete', 'uncertain')),
          family_keys_json TEXT COLLATE BINARY NOT NULL,
          reason TEXT COLLATE BINARY,
          CHECK ((state = 'acquired_complete') = (reason IS NULL)),
          CHECK ((state = 'acquired_complete') = (family_keys_json <> '[]'))
        ) STRICT;
        """;

    /// <summary>The act-level relation predicates index 10 projects, by their local name, with the edge type each becomes.</summary>
    private static readonly IReadOnlyDictionary<string, string> EdgeTypeByPredicate = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["modifies"] = EdgeModifies,
        ["repeals"] = EdgeRepeals,
        ["rectifies"] = EdgeRectifies,
        ["modifiedTempBy"] = EdgeModifiedTemporarilyBy,
    };

    /// <summary>The parts of a <c>jolux:LegalResourceImpact</c> record index 10 projects, by their local name.</summary>
    private static readonly string[] ImpactParts =
    [
        "impactFromLegalResource", "impactToLegalResource", "impactToExpression", "legalResourceImpactHasType",
        "impactConsolidatedBy", "impactConsolidatedByExpression",
    ];

    /// <summary>One act-level relation as <c>publisher_relations</c> holds it.</summary>
    internal sealed record PublisherRelationRow(
        string EdgeType,
        string SourcePredicate,
        string SubjectIri,
        string ObjectIri,
        string? ActingPublicationDate,
        string? ActingEntryIntoForce,
        string EvidenceSha256)
    {
        /// <summary>
        /// The act that amends, repeals, rectifies or modifies for a time: the subject, except for <c>modifiedTempBy</c>, whose
        /// subject is the act modified.
        /// </summary>
        internal string ActingIri => EdgeType == EdgeModifiedTemporarilyBy ? ObjectIri : SubjectIri;

        /// <summary>The act the relation is made to.</summary>
        internal string ActedOnIri => EdgeType == EdgeModifiedTemporarilyBy ? SubjectIri : ObjectIri;
    }

    /// <summary>One impact record as <c>publisher_impacts</c> holds it.</summary>
    internal sealed record PublisherImpactRow(
        string ImpactIri,
        string? FromLegalResourceIri,
        string? ToLegalResourceIri,
        string? ToExpressionIri,
        string? ImpactTypeIri,
        string? ConsolidatedByIri,
        string? ConsolidatedByExpressionIri,
        string AmbiguousPartsJson,
        string? ActingPublicationDate,
        string? ActingEntryIntoForce,
        string EvidenceSha256);

    /// <summary>One relation predicate's acquisition state as <c>relation_families</c> holds it.</summary>
    internal sealed record RelationFamilyRow(string Predicate, string State, string FamilyKeysJson, string? Reason);

    /// <summary>Index 10's three relation tables' rows, as one value; none for a build whose envelope holds none.</summary>
    internal sealed record PublisherRelationRows(
        PublisherRelationRow[] Relations,
        PublisherImpactRow[] Impacts,
        RelationFamilyRow[] Families)
    {
        internal static PublisherRelationRows None { get; } = new([], [], []);
    }

    /// <summary>One resolved publisher relation: its three IRIs, verbatim, and the digest of the observation it was read from.</summary>
    internal readonly record struct ProjectionRelation(string SubjectIri, string PredicateIri, string ObjectIri, string EvidenceSha256);

    /// <summary>One relation predicate's acquisition state: its token, the families that proved it, and why it is not complete.</summary>
    internal sealed record ProjectionRelationFamily(string PredicateIri, string State, IReadOnlyList<string> FamilyKeys, string? Reason);

    /// <summary>The predicate's token as the tables write it: <c>jolux:</c> and the local name, or the IRI as it is outside JOLux.</summary>
    internal static string PredicateToken(string predicateIri) =>
        predicateIri.StartsWith(JoluxNamespace, StringComparison.Ordinal) ? "jolux:" + predicateIri[JoluxNamespace.Length..] : predicateIri;

    /// <summary>
    /// Index 10's relation rows from the resolved relations and the fact rows: each act-level relation of the four types, and
    /// each impact record from its parts. The acting act's dates are the one exact date its facts state for
    /// <c>publicationDate</c> and <c>dateEntryInForce</c>, else none. Every other predicate (cites, which the text's references
    /// already hold; basedOn, transposes, consolidates, basicAct, hasIndirectImpact, legalAnalysisHasLegalResourceImpact) is not
    /// projected here. A relation seen twice with two evidence digests, or an impact whose parts were read from two
    /// observations, is publisher data this projection cannot hold as one row, and refuses.
    /// </summary>
    internal static PublisherRelationRows ProjectPublisherRelations(
        IReadOnlyList<ProjectionRelation> relations,
        IReadOnlyList<ProjectionRelationFamily> families,
        IEnumerable<WorkFactRow> workFacts)
    {
        ArgumentNullException.ThrowIfNull(relations);
        ArgumentNullException.ThrowIfNull(families);
        ArgumentNullException.ThrowIfNull(workFacts);
        var facts = CatalogueFacts.From(workFacts);
        var rows = new Dictionary<(string, string, string), PublisherRelationRow>();
        var impacts = new Dictionary<string, List<ProjectionRelation>>(StringComparer.Ordinal);
        foreach (var relation in relations.Distinct())
        {
            if (!relation.PredicateIri.StartsWith(JoluxNamespace, StringComparison.Ordinal))
            {
                continue;
            }

            var local = relation.PredicateIri[JoluxNamespace.Length..];
            if (EdgeTypeByPredicate.TryGetValue(local, out var edgeType))
            {
                var acting = edgeType == EdgeModifiedTemporarilyBy ? relation.ObjectIri : relation.SubjectIri;
                var row = new PublisherRelationRow(
                    edgeType, "jolux:" + local, relation.SubjectIri, relation.ObjectIri,
                    facts.OneDate(acting, "publicationDate"), facts.OneDate(acting, "dateEntryInForce"), relation.EvidenceSha256);
                if (!rows.TryAdd((edgeType, relation.SubjectIri, relation.ObjectIri), row))
                {
                    throw new InvalidDataException(
                        $"The publisher's {local} relation from {relation.SubjectIri} to {relation.ObjectIri} was read from two observations.");
                }
            }
            else if (Array.IndexOf(ImpactParts, local) >= 0)
            {
                if (!impacts.TryGetValue(relation.SubjectIri, out var parts))
                {
                    impacts.Add(relation.SubjectIri, parts = []);
                }

                parts.Add(relation);
            }
        }

        var impactRows = impacts
            .OrderBy(static entry => entry.Key, StringComparer.Ordinal)
            .Select(entry => ImpactOf(entry.Key, entry.Value, facts))
            .ToArray();
        return new PublisherRelationRows(
            rows.Values
                .OrderBy(static row => row.EdgeType, StringComparer.Ordinal)
                .ThenBy(static row => row.SubjectIri, StringComparer.Ordinal)
                .ThenBy(static row => row.ObjectIri, StringComparer.Ordinal)
                .ToArray(),
            impactRows,
            ProjectRelationFamilies(families));
    }

    /// <summary>One impact record from its parts: each part the one value given, else null and, when several, named ambiguous.</summary>
    private static PublisherImpactRow ImpactOf(string impact, IReadOnlyList<ProjectionRelation> parts, CatalogueFacts facts)
    {
        var evidence = parts.Select(static part => part.EvidenceSha256).Distinct(StringComparer.Ordinal).ToArray();
        if (evidence.Length != 1)
        {
            throw new InvalidDataException($"The publisher's impact record {impact} was read from more than one observation.");
        }

        var ambiguous = new List<string>();
        string? Part(string local)
        {
            var values = parts
                .Where(part => string.Equals(part.PredicateIri, JoluxNamespace + local, StringComparison.Ordinal))
                .Select(static part => part.ObjectIri)
                .Distinct(StringComparer.Ordinal)
                .ToArray();
            if (values.Length > 1)
            {
                ambiguous.Add("jolux:" + local);
            }

            return values.Length == 1 ? values[0] : null;
        }

        var from = Part("impactFromLegalResource");
        var to = Part("impactToLegalResource");
        var toExpression = Part("impactToExpression");
        var type = Part("legalResourceImpactHasType");
        var consolidatedBy = Part("impactConsolidatedBy");
        var consolidatedByExpression = Part("impactConsolidatedByExpression");
        return new PublisherImpactRow(
            impact, from, to, toExpression, type, consolidatedBy, consolidatedByExpression,
            JsonSerializer.Serialize(ambiguous.Order(StringComparer.Ordinal).ToArray()),
            from is null ? null : facts.OneDate(from, "publicationDate"),
            from is null ? null : facts.OneDate(from, "dateEntryInForce"),
            evidence[0]);
    }

    /// <summary>Each relation predicate's acquisition state, by its token, with the families that proved it, sorted, each once.</summary>
    internal static RelationFamilyRow[] ProjectRelationFamilies(IReadOnlyList<ProjectionRelationFamily> families) =>
        families
            .Select(static family => new RelationFamilyRow(
                PredicateToken(family.PredicateIri),
                family.State,
                JsonSerializer.Serialize(family.FamilyKeys.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray()),
                family.Reason))
            .GroupBy(static row => row.Predicate, StringComparer.Ordinal)
            .Select(static group => group.Distinct().Count() == 1
                ? group.First()
                : throw new InvalidDataException($"The relation predicate {group.Key} has two acquisition states."))
            .OrderBy(static row => row.Predicate, StringComparer.Ordinal)
            .ToArray();

    /// <summary>
    /// The reader's check of index 10's dates: each row's acting dates are the ones the index's own fact rows give its acting act
    /// (<see cref="ProjectPublisherRelations"/>'s rule), so a row cannot carry a date the publisher's facts do not state.
    /// </summary>
    internal static void VerifyActingDates(PublisherRelationRows rows, IEnumerable<WorkFactRow> actingFacts)
    {
        var facts = CatalogueFacts.From(actingFacts);
        foreach (var row in rows.Relations)
        {
            if (!string.Equals(row.ActingPublicationDate, facts.OneDate(row.ActingIri, "publicationDate"), StringComparison.Ordinal) ||
                !string.Equals(row.ActingEntryIntoForce, facts.OneDate(row.ActingIri, "dateEntryInForce"), StringComparison.Ordinal))
            {
                throw new InvalidDataException(
                    $"The Luxembourg index's {row.EdgeType} relation from {row.SubjectIri} carries dates its facts do not state.");
            }
        }

        foreach (var row in rows.Impacts.Where(static row => row.FromLegalResourceIri is not null))
        {
            if (!string.Equals(row.ActingPublicationDate, facts.OneDate(row.FromLegalResourceIri!, "publicationDate"), StringComparison.Ordinal) ||
                !string.Equals(row.ActingEntryIntoForce, facts.OneDate(row.FromLegalResourceIri!, "dateEntryInForce"), StringComparison.Ordinal))
            {
                throw new InvalidDataException(
                    $"The Luxembourg index's impact record {row.ImpactIri} carries dates its facts do not state.");
            }
        }
    }

    /// <summary>The envelope's resolved publisher relations as the projection reads them: accepted, publisher-asserted, verbatim.</summary>
    internal static ProjectionRelation[] ProjectionRelationsOf(IEnumerable<LuxembourgResolvedRelation> resolved) =>
        resolved
            .Where(static relation =>
                relation.Authority == LuxembourgRelationAuthority.PublisherAsserted &&
                relation.Disposition == LuxembourgRelationDisposition.Accepted)
            .Select(static relation => new ProjectionRelation(
                relation.SubjectIri, relation.PredicateIri, relation.ObjectIri, relation.ObservationRef.Sha256))
            .ToArray();

    /// <summary>The envelope's relation family states as the projection reads them.</summary>
    internal static ProjectionRelationFamily[] ProjectionFamiliesOf(IEnumerable<LuxembourgRelationFamilyAcquisition> families) =>
        families
            .Select(static family => new ProjectionRelationFamily(
                family.PredicateIri,
                ContractWire.NameOf(family.State),
                family.CompletionProofs.Select(static proof => proof.FamilyKey).ToArray(),
                family.Reason))
            .ToArray();
}
