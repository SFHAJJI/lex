using Lex.V3.Contracts;
using Lex.V3.Contracts.Facts;

namespace Lex.V3.Tests.Facts;

/// <summary>
/// Fixtures shaped after real publisher conditions rather than convenient ones.
/// </summary>
internal static class FactsFixtures
{
    internal const string TransportDigest =
        "9f2c4d6a8b0e1f3572849a6bcd0e2f4185a7c9db3e5f70819a2b4c6d8e0f1a23";

    internal const string ScopeDigest =
        "1a2b3c4d5e6f708192a3b4c5d6e7f8091a2b3c4d5e6f708192a3b4c5d6e7f809";

    internal const string ConsolidatesPredicate =
        "http://data.legilux.public.lu/resource/ontology/jolux#consolidates";

    internal const string ConsolidatedByPredicate =
        "http://data.legilux.public.lu/resource/ontology/jolux#consolidatedBy";

    internal const string InverseOfStatement = "http://www.w3.org/2002/07/owl#inverseOf";

    internal static TransportByteReference TransportBytes() => new(TransportDigest, 48_112);

    internal static SourceObservationReference Observation() => new(
        "obs-2026-08-31T09:14:02Z-lu-legilux-0001",
        new DateTimeOffset(2026, 8, 31, 9, 14, 2, TimeSpan.Zero),
        TransportBytes());

    internal static OfficialIdentity LuWork() => new(
        PublisherId.LuLegilux,
        IdentifierFamily.Eli,
        "eli/etat/leg/loi/2019/07/15/a512/jo");

    internal static OfficialIdentity LuTarget() => new(
        PublisherId.LuLegilux,
        IdentifierFamily.Eli,
        "eli/etat/leg/loi/2004/03/22/n1/jo");

    internal static OfficialIdentity EuCase() => new(
        PublisherId.EuEurLex,
        IdentifierFamily.Celex,
        "62019CJ0311");

    /// <summary>
    /// Two axioms sharing one remote identifier, and one axiom carrying the same qualifier
    /// predicate twice with different values. Both are real publisher conditions and both are
    /// silently destroyed by a dictionary.
    /// </summary>
    internal static IReadOnlyList<QualifiedAxiom> MultimapAxioms() =>
    [
        new QualifiedAxiom(
            "axiom-7731",
            [
                new AxiomQualifier(ConsolidatesPredicate, "first"),
                new AxiomQualifier(ConsolidatesPredicate, "second"),
            ]),
        new QualifiedAxiom(
            "axiom-7731",
            [new AxiomQualifier(ConsolidatedByPredicate, "third")]),
    ];

    internal static PublisherRelation PublisherRelation() => new(
        FactsSchemaIds.PublisherRelation,
        LuWork(),
        LuTarget(),
        ConsolidatesPredicate,
        Observation(),
        MultimapAxioms());

    internal static DerivedInverseRelation DerivedInverse() => new(
        FactsSchemaIds.DerivedInverseRelation,
        LuTarget(),
        LuWork(),
        ConsolidatedByPredicate,
        ConsolidatesPredicate,
        InverseOfStatement,
        PublisherRelation());

    internal static LocalInboundView InboundView(bool scopeIsComplete = false) => new(
        FactsSchemaIds.LocalInboundView,
        LuTarget(),
        ConsolidatesPredicate,
        scopeIsComplete,
        ScopeDigest,
        [PublisherRelation()]);

    internal static RelationFact AssertedFact() => new(
        FactsSchemaIds.RelationFact,
        RelationAssertionKind.PublisherAsserted,
        TargetBodyScope.BodyInScopeHeld,
        EcliState.EcliPresent,
        "ECLI:EU:C:2020:1042",
        PublisherRelation(),
        null,
        null);

    /// <summary>A Cellar case relation whose publisher record carries no ECLI.</summary>
    internal static RelationFact CaseFactWithoutEcli() => new(
        FactsSchemaIds.RelationFact,
        RelationAssertionKind.PublisherAsserted,
        TargetBodyScope.BodyInScopeNotHeld,
        EcliState.EcliMissing,
        null,
        new PublisherRelation(
            FactsSchemaIds.PublisherRelation,
            LuWork(),
            EuCase(),
            ConsolidatesPredicate,
            Observation(),
            MultimapAxioms()),
        null,
        null);

    internal static PublisherDate YearOnlyDate() => new(
        FactsSchemaIds.PublisherDate,
        "2019",
        "http://www.w3.org/2001/XMLSchema#gYear",
        DatePrecision.Year,
        DateOpenSentinel.NotOpen);

    internal static PublisherDate OpenEndedDate() => new(
        FactsSchemaIds.PublisherDate,
        "9999-12-31",
        "http://www.w3.org/2001/XMLSchema#date",
        DatePrecision.YearMonthDay,
        DateOpenSentinel.OpenEnded);

    internal static PublisherDateFact DateFact(
        PublisherDate? date = null,
        DateSemanticRole role = DateSemanticRole.RoleNotStatedByPublisher,
        string? rawQualifier = null,
        string? comment = null) => new(
        FactsSchemaIds.PublisherDateFact,
        LuWork(),
        date ?? YearOnlyDate(),
        ConsolidatesPredicate,
        MultimapAxioms()[0],
        rawQualifier,
        comment,
        role,
        "lex-lu-date-reader/1",
        Observation());
}
