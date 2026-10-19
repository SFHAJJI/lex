using System.Globalization;
using System.Text.Json;
using Lex.V3.Contracts;
using Lex.V3.Contracts.Facts;
using Lex.V3.Contracts.Source.Europe;
using Microsoft.Data.Sqlite;

namespace Lex.V3.Ingest.Europe;

/// <summary>
/// One date of an EU act as the publisher states it in a reified date statement (an <c>owl:Axiom</c> on the act's Cellar
/// work, family A of the acquisition, decoded by <see cref="EuReifiedAxiomDecode"/>): the plan's item 2.1, R79's E1. Its
/// <see cref="Role"/> is one of <see cref="EuropeForceDateRoles"/>, read from the date property and the publisher's
/// type-of-date code alone (<see cref="EuropeIndexBuilder.ForceDateRoleOf"/>); <see cref="FactRole"/> is the accepted E1
/// contract's own reading of the same statement (<see cref="DateSemanticRole"/>, its wire token). The value, its datatype,
/// the code and the publisher's comment are the publisher's, exactly as served; <see cref="QualifierLabel"/> is this
/// codebase's pinned label for a pinned code (never the publisher's); <see cref="QualifiersJson"/> is every property the
/// statement carried, in delivery order, as <c>[predicate, value]</c> pairs; <see cref="SourceObservationId"/> is the
/// coordinate of the delivered batch it was read from. Never a wording date, a consolidation date or an applicability date.
/// </summary>
public sealed record EuropeIndexForceDate(
    string DateIdentitySha256,
    string PublisherWorkIri,
    string Role,
    string DateValue,
    string DatatypeIri,
    bool OpenEnded,
    string SourcePredicateIri,
    string? QualifierCode,
    string? QualifierLabel,
    string? PublisherComment,
    string FactRole,
    string AxiomIri,
    string QualifiersJson,
    string SourceObservationId);

/// <summary>
/// The closed roles of an EU force date (<see cref="EuropeIndexForceDate.Role"/>), in the order an answer says them. Constants,
/// not an enum: the table stores the token, and the answers say it as stored.
/// </summary>
public static class EuropeForceDateRoles
{
    /// <summary>The entry-into-force property with the publisher's code EV.</summary>
    public const string EntryIntoForce = "entry_into_force";

    /// <summary>The entry-into-force property with the publisher's code MA.</summary>
    public const string Application = "application";

    /// <summary>The end-of-validity property, whatever its comment says.</summary>
    public const string EndOfValidity = "end_of_validity";

    /// <summary>The deadline property, whatever its comment says; never a transposition deadline.</summary>
    public const string Deadline = "deadline";

    /// <summary>The signature property.</summary>
    public const string Signature = "signature";

    /// <summary>The entry-into-force property with a code the accepted table does not pin (such as MA/PART), or none.</summary>
    public const string NotPinned = "not_pinned";

    public static IReadOnlyList<string> InOrder { get; } =
        Array.AsReadOnly(new[] { EntryIntoForce, Application, EndOfValidity, Deadline, Signature, NotPinned });

    /// <summary>The four roles the plan's item 2.1 names, which an answer says are not held when the act has none of them.</summary>
    public static IReadOnlyList<string> Named { get; } =
        Array.AsReadOnly(new[] { EntryIntoForce, Application, EndOfValidity, Deadline });
}

public static partial class EuropeIndexBuilder
{
    internal const string StatesSchema = "lex-v3-europe-index/5";

    private const string ForceDatesDdl = """

        CREATE TABLE force_dates (
          date_identity_sha256 TEXT COLLATE BINARY NOT NULL PRIMARY KEY CHECK (length(date_identity_sha256) = 64),
          publisher_work_iri TEXT COLLATE BINARY NOT NULL,
          role TEXT COLLATE BINARY NOT NULL CHECK (role IN ('entry_into_force','application','end_of_validity','deadline','signature','not_pinned')),
          date_value TEXT COLLATE BINARY NOT NULL,
          datatype_iri TEXT COLLATE BINARY NOT NULL,
          open_ended INTEGER NOT NULL CHECK (open_ended IN (0, 1)),
          source_predicate_iri TEXT COLLATE BINARY NOT NULL,
          qualifier_code TEXT COLLATE BINARY,
          qualifier_label TEXT COLLATE BINARY,
          publisher_comment TEXT COLLATE BINARY,
          fact_role TEXT COLLATE BINARY NOT NULL,
          axiom_iri TEXT COLLATE BINARY NOT NULL,
          qualifiers_json TEXT COLLATE BINARY NOT NULL,
          source_observation_id TEXT COLLATE BINARY NOT NULL
        ) STRICT;
        CREATE INDEX force_dates_work ON force_dates(publisher_work_iri, role, date_value);
        """;

    private const string AnnotatedSourceIri = "http://www.w3.org/2002/07/owl#annotatedSource";
    private const string AnnotatedPropertyIri = "http://www.w3.org/2002/07/owl#annotatedProperty";
    private const string AnnotatedTargetIri = "http://www.w3.org/2002/07/owl#annotatedTarget";
    private const string TypeOfDateIri = "http://publications.europa.eu/ontology/annotation#type_of_date";
    private const string CommentOnDateIri = "http://publications.europa.eu/ontology/annotation#comment_on_date";

    /// <summary>
    /// The role of one statement, from its date property and the accepted contract's reading of it (which reads the
    /// publisher's type-of-date code): EV on the entry-into-force property is the entry into force and MA the application;
    /// the end-of-validity and signature properties give their own role; the deadline property is the publisher's deadline,
    /// with no code (the population's 221 deadlines carry none; their qualifier is the comment, said word for word) or with
    /// the pinned "at the latest"; any other code on the entry-into-force property, or none, is not pinned and keeps its code.
    /// </summary>
    internal static string ForceDateRoleOf(string predicate, DateSemanticRole factRole) => factRole switch
    {
        DateSemanticRole.EntryIntoForce => EuropeForceDateRoles.EntryIntoForce,
        DateSemanticRole.ApplicationDate => EuropeForceDateRoles.Application,
        DateSemanticRole.EndOfValidity => EuropeForceDateRoles.EndOfValidity,
        DateSemanticRole.SignatureDate => EuropeForceDateRoles.Signature,
        DateSemanticRole.PublisherDeadline => EuropeForceDateRoles.Deadline,
        DateSemanticRole.RoleNotStatedByPublisher when predicate == EuDateQualifierVocabulary.DeadlinePredicateUri =>
            EuropeForceDateRoles.Deadline,
        DateSemanticRole.RoleNotStatedByPublisher when predicate == EuDateQualifierVocabulary.EntryIntoForceAndApplicationPredicateUri =>
            EuropeForceDateRoles.NotPinned,
        _ => throw new InvalidDataException($"An EU date statement on {predicate} has a role no force date takes ({factRole})."),
    };

    /// <summary>
    /// The force dates of the census's works, from the run's decoded date statements: one row per statement, keyed by its
    /// identity, so a statement two seeds' closures both delivered is one row. Refuses a statement whose work the census
    /// does not hold, or whose identity repeats with other values.
    /// </summary>
    internal static EuropeIndexForceDate[] ProjectForceDates(
        IReadOnlyList<EuDateAxiomBinding> axioms, IReadOnlyList<EuropeIndexState> states)
    {
        var works = states.Select(static state => state.PublisherWorkIri).ToHashSet(StringComparer.Ordinal);
        var rows = new Dictionary<string, EuropeIndexForceDate>(StringComparer.Ordinal);
        foreach (var binding in axioms)
        {
            var row = ForceDateOf(binding);
            if (!works.Contains(row.PublisherWorkIri))
                throw new InvalidDataException("An EU date statement names a work the census does not hold.");
            if (rows.TryGetValue(row.DateIdentitySha256, out var seen))
            {
                if (seen != row) throw new InvalidDataException("Two EU date statements share an identity and differ.");
                continue;
            }

            rows.Add(row.DateIdentitySha256, row);
        }

        return rows.Values.OrderBy(static row => row.DateIdentitySha256, StringComparer.Ordinal).ToArray();
    }

    private static EuropeIndexForceDate ForceDateOf(EuDateAxiomBinding binding)
    {
        var work = binding.WorkIdentity.Value(FactsIdentifierFamily.CellarWorkUri)
            ?? throw new InvalidDataException("An EU date statement names no Cellar work.");
        if (EuPackRootCanonicalForm.TryCanonicalize(work, out _) != work)
            throw new InvalidDataException("An EU date statement names a work that is not in its canonical form.");
        var fact = binding.Fact;
        var qualifiers = JsonSerializer.Serialize(fact.Axiom.Qualifiers.Select(static qualifier => new[] { qualifier.PredicateUri, qualifier.RawValue }));
        var identity = Identity("lex-v3-eu-index-force-date/1", [work, fact.Axiom.RemoteAxiomId, qualifiers]);
        return new EuropeIndexForceDate(identity, work, ForceDateRoleOf(fact.SourcePredicateUri, fact.SemanticRole),
            fact.Date.RawLexicalValue, fact.Date.DatatypeUri, fact.Date.OpenSentinel == DateOpenSentinel.OpenEnded,
            fact.SourcePredicateUri, binding.RawQualifierCode, binding.QualifierLabel, binding.PublisherComment,
            ContractWire.NameOf(fact.SemanticRole), fact.Axiom.RemoteAxiomId, qualifiers, fact.SourceObservationId);
    }

    /// <summary>
    /// Reopens each stored row through the accepted contract, as the build made it: the statement's own properties
    /// (<c>qualifiers_json</c>) name the row's work, date property, value, code and comment; the binding the contract makes
    /// of them has the row's role, open end and contract role; the identity is the row's; and the work is a census work.
    /// </summary>
    internal static void ValidateForceDateEvidence(IReadOnlyList<EuropeIndexForceDate> rows, IReadOnlyList<EuropeIndexState> states)
    {
        var works = states.Select(static state => state.PublisherWorkIri).ToHashSet(StringComparer.Ordinal);
        foreach (var row in rows)
        {
            try
            {
                var pairs = JsonSerializer.Deserialize<string[][]>(row.QualifiersJson)
                    ?? throw new InvalidDataException("EU force-date evidence is empty.");
                if (pairs.Any(static pair => pair is not { Length: 2 } || pair[0] is null || pair[1] is null))
                    throw new InvalidDataException("EU force-date evidence is not a list of predicate/value pairs.");
                var qualifiers = pairs.Select(static pair => new AxiomQualifier(pair[0], pair[1])).ToArray();
                string? Only(string predicate)
                {
                    var values = qualifiers.Where(qualifier => qualifier.PredicateUri == predicate).Select(static qualifier => qualifier.RawValue)
                        .Distinct(StringComparer.Ordinal).ToArray();
                    return values.Length switch
                    {
                        0 => null,
                        1 => values[0],
                        _ => throw new InvalidDataException("EU force-date evidence states a modelled property twice."),
                    };
                }

                var code = Only(TypeOfDateIri) is { } carrier
                    ? carrier.StartsWith('{') && carrier.IndexOf('|') > 1 ? carrier[1..carrier.IndexOf('|')] : throw new InvalidDataException("EU force-date evidence carries a malformed code.")
                    : null;
                if (Only(AnnotatedSourceIri) != row.PublisherWorkIri || Only(AnnotatedPropertyIri) != row.SourcePredicateIri ||
                    Only(AnnotatedTargetIri) != row.DateValue || code != row.QualifierCode || Only(CommentOnDateIri) != row.PublisherComment ||
                    !works.Contains(row.PublisherWorkIri) ||
                    row.DateIdentitySha256 != Identity("lex-v3-eu-index-force-date/1", [row.PublisherWorkIri, row.AxiomIri, row.QualifiersJson]) ||
                    JsonSerializer.Serialize(pairs) != row.QualifiersJson)
                    throw new InvalidDataException("EU force-date fields disagree with their retained statement.");
                if (!PublisherDate.PrecisionByDatatype.TryGetValue(row.DatatypeIri, out var precision))
                    throw new InvalidDataException("EU force-date evidence carries a datatype no date has.");
                var binding = EuDateAxiomBinding.Create(
                    new OfficialIdentitySet(PublisherId.EuEurLex, [new OfficialIdentifier(FactsIdentifierFamily.CellarWorkUri, row.PublisherWorkIri)]),
                    row.DateValue, row.DatatypeIri, precision, row.SourcePredicateIri, new QualifiedAxiom(row.AxiomIri, qualifiers),
                    row.QualifierCode, row.QualifierLabel, row.PublisherComment, EuReifiedAxiomDecode.ParsedByAuthority, row.SourceObservationId);
                if (ForceDateOf(binding) != row)
                    throw new InvalidDataException("EU force-date fields disagree with the contract's reading of their statement.");
            }
            catch (Exception exception) when (exception is JsonException or ArgumentException)
            {
                throw new InvalidDataException("EU force-date evidence could not be reopened.", exception);
            }
        }

        if (rows.Select(static row => row.DateIdentitySha256).Distinct(StringComparer.Ordinal).Count() != rows.Count ||
            !rows.Select(static row => row.DateIdentitySha256).SequenceEqual(
                rows.Select(static row => row.DateIdentitySha256).Order(StringComparer.Ordinal), StringComparer.Ordinal))
            throw new InvalidDataException("EU force dates repeat or are out of identity order.");
    }

    private static void InsertForceDates(SqliteConnection connection, SqliteTransaction transaction, IReadOnlyList<EuropeIndexForceDate> rows)
    {
        foreach (var row in rows)
            Insert(connection, transaction, "INSERT INTO force_dates VALUES($p0,$p1,$p2,$p3,$p4,$p5,$p6,$p7,$p8,$p9,$p10,$p11,$p12,$p13)",
                row.DateIdentitySha256, row.PublisherWorkIri, row.Role, row.DateValue, row.DatatypeIri, row.OpenEnded ? 1 : 0,
                row.SourcePredicateIri, row.QualifierCode, row.QualifierLabel, row.PublisherComment, row.FactRole, row.AxiomIri,
                row.QualifiersJson, row.SourceObservationId);
    }

    private sealed record LogicalRowsWithForceDates(IReadOnlyList<MemberRow> Members,
        IReadOnlyList<CorrigendumLineRow> Lines, IReadOnlyList<CorrigendumGapRow> Gaps,
        IReadOnlyList<ArticleRow> Articles, IReadOnlyList<ArticleSourceRow> Sources,
        IReadOnlyList<ArticleDigestRow> Digests, IReadOnlyList<EuropeIndexState> States,
        IReadOnlyList<EuropeIndexForceDate> ForceDates);
}

public sealed partial class EuropeIndexReader
{
    /// <summary>Whether this index holds the acts' force dates (schema 6): an index /5 or older holds none, and says nothing of them.</summary>
    public bool HasForceDates { get; }

    private IReadOnlyList<EuropeIndexForceDate>? _forceDates;

    /// <summary>
    /// The force dates the index holds for one work, in role order (<see cref="EuropeForceDateRoles.InOrder"/>), then date,
    /// then identity; empty when the work's statements carry none. Throws on an index that holds no force date table.
    /// </summary>
    public IReadOnlyList<EuropeIndexForceDate> ReadForceDates(string publisherWorkIri)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(publisherWorkIri);
        if (!HasForceDates) throw new InvalidOperationException("This historical EU index has no force dates table.");
        lock (_gate)
        {
            _forceDates ??= ReadForceDates(_connection);
            return Array.AsReadOnly(_forceDates.Where(row => row.PublisherWorkIri == publisherWorkIri)
                .OrderBy(static row => EuropeForceDateRoles.InOrder.ToList().IndexOf(row.Role))
                .ThenBy(static row => row.DateValue, StringComparer.Ordinal)
                .ThenBy(static row => row.DateIdentitySha256, StringComparer.Ordinal)
                .ToArray());
        }
    }

    private static EuropeIndexForceDate[] ReadForceDates(SqliteConnection connection)
    {
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT date_identity_sha256,publisher_work_iri,role,date_value,datatype_iri,open_ended,source_predicate_iri," +
            "qualifier_code,qualifier_label,publisher_comment,fact_role,axiom_iri,qualifiers_json,source_observation_id FROM force_dates " +
            "ORDER BY date_identity_sha256 COLLATE BINARY";
        using var reader = command.ExecuteReader();
        var rows = new List<EuropeIndexForceDate>();
        while (reader.Read())
        {
            string? Optional(int ordinal) => reader.IsDBNull(ordinal) ? null : reader.GetString(ordinal);
            rows.Add(new EuropeIndexForceDate(reader.GetString(0), reader.GetString(1), reader.GetString(2), reader.GetString(3),
                reader.GetString(4), reader.GetInt64(5) switch { 0 => false, 1 => true, _ => throw new InvalidDataException("An EU force date's open end is not 0 or 1.") },
                reader.GetString(6), Optional(7), Optional(8), Optional(9), reader.GetString(10), reader.GetString(11),
                reader.GetString(12), reader.GetString(13)));
        }

        return rows.ToArray();
    }

    internal static EuropeIndexForceDate[] ReadForceDateRows(SqliteConnection connection) => ReadForceDates(connection);
}
