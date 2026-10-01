using Lex.V3.Contracts;
using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;
using Lex.V3.Contracts.Source.Core;
using Lex.V3.Contracts.Source.Europe;
using Microsoft.Data.Sqlite;

namespace Lex.V3.Ingest.Europe;

public enum EuropeIndexStateDateStatus
{
    OriginalWording = 1,
    ObservationMissing = 2,
    PublisherDateAbsent = 3,
    PublisherDateUnusable = 4,
    ObservedConsolidationDate = 5,
    AmbiguousVersion = 6,
}

/// <summary>
/// One census-discovered work in a seed family. PublisherConsolidationDate is an observed
/// consolidation coordinate, never an inferred entry-into-force or applicability date.
/// FactsJson keeps original RDF terms and their own batch and census evidence references.
/// A null PublisherWorkCelex means that the observed identity was not uniquely admitted.
/// </summary>
public sealed record EuropeIndexState(
    string StateIdentitySha256, string SeedCelex, string RootWorkIri, string PublisherWorkIri,
    string? PublisherWorkCelex, EuropeIndexStateDateStatus DateStatus,
    string? PublisherConsolidationDate, string FactsJson);

public static partial class EuropeIndexBuilder
{
    private const string StatesDdl = """

        CREATE TABLE states (
          state_identity_sha256 TEXT COLLATE BINARY NOT NULL PRIMARY KEY CHECK (length(state_identity_sha256) = 64),
          seed_celex TEXT COLLATE BINARY NOT NULL,
          root_work_iri TEXT COLLATE BINARY NOT NULL,
          publisher_work_iri TEXT COLLATE BINARY NOT NULL,
          publisher_work_celex TEXT COLLATE BINARY,
          date_status INTEGER NOT NULL CHECK (date_status BETWEEN 1 AND 6),
          publisher_consolidation_date TEXT COLLATE BINARY,
          facts_json TEXT COLLATE BINARY NOT NULL,
          UNIQUE(seed_celex, publisher_work_iri)
        ) STRICT;
        CREATE INDEX states_seed_date ON states(seed_celex, publisher_consolidation_date);
        """;

    internal static EuropeIndexState[] ProjectStates(IReadOnlyList<EuObservedWorkFacts> works)
    {
        var rows = works.Select(work =>
        {
            var (status, date) = StateDate(work);
            return new EuropeIndexState(StateIdentity(work.SeedCelex, work.RootWorkIri, work.PublisherWorkIri),
                work.SeedCelex, work.RootWorkIri, work.PublisherWorkIri,
                EuObservedWorkIdentity.Resolve(works, work.PublisherWorkIri), status, date, JsonSerializer.Serialize(work));
        }).ToArray();
        if (rows.Select(row => (row.SeedCelex, row.PublisherWorkIri)).Distinct().Count() != rows.Length)
            throw new InvalidDataException("The EU census state population contains a duplicate seed/work coordinate.");
        var ambiguous = rows.Where(row => row.PublisherConsolidationDate is not null)
            .GroupBy(row => (row.SeedCelex, row.RootWorkIri, row.PublisherConsolidationDate))
            .Where(group => group.Count() > 1).SelectMany(group => group)
            .Select(row => row.StateIdentitySha256).ToHashSet(StringComparer.Ordinal);
        return rows.Select(row => ambiguous.Contains(row.StateIdentitySha256)
                ? row with { DateStatus = EuropeIndexStateDateStatus.AmbiguousVersion } : row)
            .OrderBy(row => row.StateIdentitySha256, StringComparer.Ordinal).ToArray();
    }

    private static (EuropeIndexStateDateStatus Status, string? Date) StateDate(EuObservedWorkFacts work)
    {
        if (work.PublisherWorkIri == work.RootWorkIri) return (EuropeIndexStateDateStatus.OriginalWording, null);
        var predicate = EuObjectFactsDiscoveryPlan.CdmIri(EuCdmPredicate.ActConsolidatedDate);
        var terms = work.Observations.Where(value => value.PredicateIri == predicate)
            .Select(value => value.Value).Distinct().ToArray();
        if (terms.Length == 0) return (EuropeIndexStateDateStatus.ObservationMissing, null);
        if (terms.Length > 1) return (EuropeIndexStateDateStatus.AmbiguousVersion, null);
        var term = terms[0];
        if (term.Kind == RepeatedEnumerationRdfTermKind.Unbound) return (EuropeIndexStateDateStatus.PublisherDateAbsent, null);
        if (term.Kind != RepeatedEnumerationRdfTermKind.Literal || term.Language is not null ||
            term.Datatype != "http://www.w3.org/2001/XMLSchema#date" ||
            !DateOnly.TryParseExact(term.Value, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
            return (EuropeIndexStateDateStatus.PublisherDateUnusable, null);
        return (EuropeIndexStateDateStatus.ObservedConsolidationDate, date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
    }

    internal static string StateIdentity(string seed, string root, string work) =>
        Convert.ToHexStringLower(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(new[] { seed, root, work })));

    private sealed record LogicalRowsWithStates(IReadOnlyList<MemberRow> Members,
        IReadOnlyList<CorrigendumLineRow> Lines, IReadOnlyList<CorrigendumGapRow> Gaps,
        IReadOnlyList<ArticleRow> Articles, IReadOnlyList<ArticleSourceRow> Sources,
        IReadOnlyList<ArticleDigestRow> Digests, IReadOnlyList<EuropeIndexState> States);
}

public sealed partial class EuropeIndexReader
{
    public bool HasStates { get; }

    /// <summary>All discovered works of a reviewed seed, including typed missing/ambiguous date outcomes.</summary>
    public IReadOnlyList<EuropeIndexState> ReadStates(string seedCelex)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(seedCelex);
        if (!HasStates) throw new InvalidOperationException("This historical EU index has no states table.");
        lock (_gate)
            return Array.AsReadOnly(ReadStates(_connection).Where(row => row.SeedCelex == seedCelex)
                .OrderBy(row => row.PublisherConsolidationDate, StringComparer.Ordinal)
                .ThenBy(row => row.PublisherWorkIri, StringComparer.Ordinal).ToArray());
    }

    private static EuropeIndexState[] ReadStates(SqliteConnection connection)
    {
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT state_identity_sha256,seed_celex,root_work_iri,publisher_work_iri,publisher_work_celex,date_status,publisher_consolidation_date,facts_json FROM states ORDER BY state_identity_sha256 COLLATE BINARY";
        using var reader = command.ExecuteReader();
        var rows = new List<EuropeIndexState>();
        while (reader.Read())
        {
            var row = new EuropeIndexState(reader.GetString(0), reader.GetString(1), reader.GetString(2),
                reader.GetString(3), reader.IsDBNull(4) ? null : reader.GetString(4),
                (EuropeIndexStateDateStatus)reader.GetInt32(5), reader.IsDBNull(6) ? null : reader.GetString(6), reader.GetString(7));
            if (!Enum.IsDefined(row.DateStatus) || row.StateIdentitySha256 != EuropeIndexBuilder.StateIdentity(row.SeedCelex, row.RootWorkIri, row.PublisherWorkIri) ||
                (row.DateStatus == EuropeIndexStateDateStatus.OriginalWording) != (row.RootWorkIri == row.PublisherWorkIri) ||
                (row.DateStatus == EuropeIndexStateDateStatus.ObservedConsolidationDate && row.PublisherConsolidationDate is null) ||
                (row.PublisherConsolidationDate is not null &&
                    (row.DateStatus is not (EuropeIndexStateDateStatus.ObservedConsolidationDate or EuropeIndexStateDateStatus.AmbiguousVersion) ||
                     !DateOnly.TryParseExact(row.PublisherConsolidationDate, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out _))))
                throw new InvalidDataException("An EU state has invalid identity or date semantics.");
            try { using var facts = JsonDocument.Parse(row.FactsJson); if (facts.RootElement.ValueKind != JsonValueKind.Object) throw new JsonException(); }
            catch (JsonException exception) { throw new InvalidDataException("EU state evidence is invalid.", exception); }
            rows.Add(row);
        }
        if (rows.Where(row => row.PublisherConsolidationDate is not null)
            .GroupBy(row => (row.SeedCelex, row.RootWorkIri, row.PublisherConsolidationDate))
            .Any(group => group.Count() > 1 && group.Any(row => row.DateStatus != EuropeIndexStateDateStatus.AmbiguousVersion)))
            throw new InvalidDataException("EU same-date states must carry ambiguity.");
        return rows.ToArray();
    }
}
