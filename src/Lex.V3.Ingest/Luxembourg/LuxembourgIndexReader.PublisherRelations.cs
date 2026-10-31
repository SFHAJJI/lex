using System.Text.Json;
using Microsoft.Data.Sqlite;

namespace Lex.V3.Ingest.Luxembourg;

/// <summary>
/// One act-level relation the publisher asserts, as index 10 holds it: its type, its predicate, its subject and object verbatim,
/// the act that amends, repeals, rectifies or modifies for a time (<see cref="ActingIri"/>) and the act it is made to
/// (<see cref="ActedOnIri"/>), the acting act's publication date and entry into force as the publisher's facts state them (each
/// the one exact date, else null), and the digest of the observation it was read from.
/// </summary>
public sealed record LuxembourgIndexPublisherRelation(
    string EdgeType,
    string SourcePredicate,
    string SubjectIri,
    string ObjectIri,
    string ActingIri,
    string ActedOnIri,
    string? ActingPublicationDate,
    string? ActingEntryIntoForce,
    string EvidenceSha256);

/// <summary>
/// One of the publisher's <c>jolux:LegalResourceImpact</c> records, as index 10 holds it: the act the change comes from and the
/// act (and expression) it is made to, its type, the consolidation (and expression) the publisher says integrated it, each the
/// one value given or null (the parts given more than once are named in <see cref="AmbiguousParts"/>), the acting act's dates and
/// the digest of the observation it was read from.
/// </summary>
public sealed record LuxembourgIndexPublisherImpact(
    string ImpactIri,
    string? FromLegalResourceIri,
    string? ToLegalResourceIri,
    string? ToExpressionIri,
    string? ImpactTypeIri,
    string? ConsolidatedByIri,
    string? ConsolidatedByExpressionIri,
    IReadOnlyList<string> AmbiguousParts,
    string? ActingPublicationDate,
    string? ActingEntryIntoForce,
    string EvidenceSha256);

/// <summary>
/// One relation predicate's acquisition state, as index 10 holds it: <c>acquired_complete</c> with the families whose
/// enumeration proved it, or another state with its reason. Only a predicate acquired in full supports an absence.
/// </summary>
public sealed record LuxembourgIndexRelationFamily(string Predicate, string State, IReadOnlyList<string> FamilyKeys, string? Reason);

public sealed partial class LuxembourgIndexReader
{
    /// <summary>
    /// The act-level relations whose subject or object is one of <paramref name="iris"/>, each once, in type, subject and object
    /// order (<see cref="LuxembourgIndexQueries.PublisherRelationsOf"/>, by the subject and object indexes); none on an index of
    /// schema 9, 8 or 6, which holds none.
    /// </summary>
    public IReadOnlyList<LuxembourgIndexPublisherRelation> ResolvePublisherRelations(IReadOnlyList<string> iris)
    {
        ArgumentNullException.ThrowIfNull(iris);
        if (!_relations || iris.Count == 0)
        {
            return Array.Empty<LuxembourgIndexPublisherRelation>();
        }

        lock (_gate)
        {
            using var command = _connection.CreateCommand();
            command.CommandText = LuxembourgIndexQueries.PublisherRelationsOf;
            command.Parameters.AddWithValue("$iris", JsonSerializer.Serialize(iris));
            using var reader = command.ExecuteReader();
            var values = new List<LuxembourgIndexPublisherRelation>();
            while (reader.Read())
            {
                var row = PublisherRelationOf(reader);
                values.Add(new LuxembourgIndexPublisherRelation(
                    row.EdgeType, row.SourcePredicate, row.SubjectIri, row.ObjectIri, row.ActingIri, row.ActedOnIri,
                    row.ActingPublicationDate, row.ActingEntryIntoForce, row.EvidenceSha256));
            }

            return Array.AsReadOnly(values.ToArray());
        }
    }

    /// <summary>
    /// The impact records whose acting or amended act is one of <paramref name="iris"/>, each once, in impact order
    /// (<see cref="LuxembourgIndexQueries.PublisherImpactsOf"/>); none on an index of schema 9, 8 or 6.
    /// </summary>
    public IReadOnlyList<LuxembourgIndexPublisherImpact> ResolvePublisherImpacts(IReadOnlyList<string> iris)
    {
        ArgumentNullException.ThrowIfNull(iris);
        if (!_relations || iris.Count == 0)
        {
            return Array.Empty<LuxembourgIndexPublisherImpact>();
        }

        lock (_gate)
        {
            using var command = _connection.CreateCommand();
            command.CommandText = LuxembourgIndexQueries.PublisherImpactsOf;
            command.Parameters.AddWithValue("$iris", JsonSerializer.Serialize(iris));
            using var reader = command.ExecuteReader();
            var values = new List<LuxembourgIndexPublisherImpact>();
            while (reader.Read())
            {
                var row = PublisherImpactOf(reader);
                values.Add(new LuxembourgIndexPublisherImpact(
                    row.ImpactIri, row.FromLegalResourceIri, row.ToLegalResourceIri, row.ToExpressionIri, row.ImpactTypeIri,
                    row.ConsolidatedByIri, row.ConsolidatedByExpressionIri,
                    Array.AsReadOnly(JsonSerializer.Deserialize<string[]>(row.AmbiguousPartsJson) ?? []),
                    row.ActingPublicationDate, row.ActingEntryIntoForce, row.EvidenceSha256));
            }

            return Array.AsReadOnly(values.ToArray());
        }
    }

    /// <summary>Every relation predicate's acquisition state, in predicate order; none on an index of schema 9, 8 or 6.</summary>
    public IReadOnlyList<LuxembourgIndexRelationFamily> ResolveRelationFamilies()
    {
        if (!_relations)
        {
            return Array.Empty<LuxembourgIndexRelationFamily>();
        }

        lock (_gate)
        {
            return Array.AsReadOnly(ReadRelationFamilies(_connection)
                .Select(static row => new LuxembourgIndexRelationFamily(
                    row.Predicate, row.State, Array.AsReadOnly(JsonSerializer.Deserialize<string[]>(row.FamilyKeysJson) ?? []), row.Reason))
                .ToArray());
        }
    }

    /// <summary>Index 10's three relation tables, whole, in their key orders, as the logical rows hash them.</summary>
    internal static LuxembourgIndexBuilder.PublisherRelationRows ReadPublisherRelationRows(SqliteConnection connection)
    {
        var relations = new List<LuxembourgIndexBuilder.PublisherRelationRow>();
        using (var command = connection.CreateCommand())
        {
            command.CommandText =
                "SELECT edge_type,source_predicate,subject_iri,object_iri,acting_publication_date,acting_entry_into_force,evidence_sha256 " +
                "FROM publisher_relations ORDER BY edge_type,subject_iri,object_iri";
            using var reader = command.ExecuteReader();
            while (reader.Read()) relations.Add(PublisherRelationOf(reader));
        }

        var impacts = new List<LuxembourgIndexBuilder.PublisherImpactRow>();
        using (var command = connection.CreateCommand())
        {
            command.CommandText =
                "SELECT impact_iri,from_legal_resource_iri,to_legal_resource_iri,to_expression_iri,impact_type_iri,consolidated_by_iri," +
                "consolidated_by_expression_iri,ambiguous_parts_json,acting_publication_date,acting_entry_into_force,evidence_sha256 " +
                "FROM publisher_impacts ORDER BY impact_iri";
            using var reader = command.ExecuteReader();
            while (reader.Read()) impacts.Add(PublisherImpactOf(reader));
        }

        return new LuxembourgIndexBuilder.PublisherRelationRows(relations.ToArray(), impacts.ToArray(), ReadRelationFamilies(connection));
    }

    /// <summary>
    /// The publication and entry-into-force facts of every acting act index 10's relation rows name
    /// (<see cref="LuxembourgIndexQueries.ActingFacts"/>), which the reader holds the rows' dates to.
    /// </summary>
    internal static LuxembourgIndexBuilder.WorkFactRow[] ReadActingFacts(SqliteConnection connection)
    {
        using var command = connection.CreateCommand();
        command.CommandText = LuxembourgIndexQueries.ActingFacts;
        using var reader = command.ExecuteReader();
        var values = new List<LuxembourgIndexBuilder.WorkFactRow>();
        while (reader.Read())
        {
            values.Add(new(
                reader.GetString(0), reader.GetString(1), reader.GetString(2), reader.GetString(3),
                reader.GetString(4), reader.GetString(5), reader.GetString(6), reader.GetString(7)));
        }

        return values.ToArray();
    }

    private static LuxembourgIndexBuilder.RelationFamilyRow[] ReadRelationFamilies(SqliteConnection connection)
    {
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT predicate,state,family_keys_json,reason FROM relation_families ORDER BY predicate";
        using var reader = command.ExecuteReader();
        var values = new List<LuxembourgIndexBuilder.RelationFamilyRow>();
        while (reader.Read())
        {
            values.Add(new(reader.GetString(0), reader.GetString(1), reader.GetString(2), reader.IsDBNull(3) ? null : reader.GetString(3)));
        }

        return values.ToArray();
    }

    private static LuxembourgIndexBuilder.PublisherRelationRow PublisherRelationOf(SqliteDataReader reader) => new(
        reader.GetString(0), reader.GetString(1), reader.GetString(2), reader.GetString(3),
        reader.IsDBNull(4) ? null : reader.GetString(4), reader.IsDBNull(5) ? null : reader.GetString(5), reader.GetString(6));

    private static LuxembourgIndexBuilder.PublisherImpactRow PublisherImpactOf(SqliteDataReader reader)
    {
        string? Text(int ordinal) => reader.IsDBNull(ordinal) ? null : reader.GetString(ordinal);
        return new(
            reader.GetString(0), Text(1), Text(2), Text(3), Text(4), Text(5), Text(6), reader.GetString(7), Text(8), Text(9),
            reader.GetString(10));
    }
}
