using Lex.V3.Contracts;
using Lex.V3.Contracts.Facts;

namespace Lex.V3.Tests.Facts;

/// <summary>
/// Fixtures shaped after real publisher conditions rather than convenient ones.
/// </summary>
internal static class FactsFixtures
{
    internal const string ScopeDigest =
        "1a2b3c4d5e6f708192a3b4c5d6e7f8091a2b3c4d5e6f708192a3b4c5d6e7f809";

    internal const string ConsolidatesPredicate =
        "http://data.legilux.public.lu/resource/ontology/jolux#consolidates";

    internal const string ConsolidatedByPredicate =
        "http://data.legilux.public.lu/resource/ontology/jolux#consolidatedBy";

    internal const string JoluxOntology = "http://data.legilux.public.lu/resource/ontology/jolux";
    internal const string OntologyVersion = "2026-06-01";

    internal const string Authority = "https://github.com/SFHAJJI/lex/authority/lu-date-reader/1";

    /// <summary>The observed axiom that authorizes the consolidates/consolidatedBy inversion.</summary>
    internal static ObservedInverseAxiom InverseAxiom() => new(
        JoluxOntology,
        OntologyVersion,
        ConsolidatesPredicate,
        ConsolidatedByPredicate,
        ObservationId);

    /// <summary>
    /// The one custody coordinate a Fact carries. It is opaque: this package resolves it through
    /// the unique http_observation record and does not restate anything that record owns.
    /// </summary>
    internal const string ObservationId = "obs-2026-08-31T09:14:02Z-lu-legilux-0001";

    internal static OfficialIdentitySet LuWork() => new(
        PublisherId.LuLegilux,
        [new OfficialIdentifier(FactsIdentifierFamily.Eli, "eli/etat/leg/loi/2019/07/15/a512/jo")]);

    internal static OfficialIdentitySet LuTarget() => new(
        PublisherId.LuLegilux,
        [new OfficialIdentifier(FactsIdentifierFamily.Eli, "eli/etat/leg/loi/2004/03/22/n1/jo")]);

    /// <summary>
    /// A EUR-Lex case as the publisher actually identifies it: a Cellar work URI, a CELEX number
    /// and an ECLI, all three at once. Retaining any one of them alone is the loss this package
    /// exists to prevent.
    /// </summary>
    internal const string CellarWorkUri =
        "http://publications.europa.eu/resource/cellar/1f8c2d3e-4a5b-6c7d-8e9f-0a1b2c3d4e5f";

    /// <summary>The CELEX persistent identifier, an alias tied to the work rather than the work.</summary>
    internal const string CellarPsiUri =
        "http://publications.europa.eu/resource/celex/62019CJ0311";

    internal static OfficialIdentitySet EuCaseWithEcli() => new(
        PublisherId.EuEurLex,
        [
            new OfficialIdentifier(FactsIdentifierFamily.CellarWorkUri, CellarWorkUri),
            new OfficialIdentifier(FactsIdentifierFamily.CellarPsiUri, CellarPsiUri),
            new OfficialIdentifier(FactsIdentifierFamily.Celex, "62019CJ0311"),
            new OfficialIdentifier(FactsIdentifierFamily.Ecli, "ECLI:EU:C:2020:1042"),
        ]);

    /// <summary>An EU identity whose only identifier is a resource beneath the work.</summary>
    internal static OfficialIdentitySet EuResource() => new(
        PublisherId.EuEurLex,
        [
            new OfficialIdentifier(
                FactsIdentifierFamily.CellarResourceUri, CellarWorkUri + "/DOC_1"),
        ]);

    /// <summary>The same case, whose publisher record carries no ECLI.</summary>
    internal static OfficialIdentitySet EuCaseWithoutEcli() => new(
        PublisherId.EuEurLex,
        [
            new OfficialIdentifier(FactsIdentifierFamily.CellarWorkUri, CellarWorkUri),
            new OfficialIdentifier(FactsIdentifierFamily.Celex, "62019CJ0311"),
        ]);

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

    internal static PublisherRelation PublisherRelation(
        OfficialIdentitySet? source = null,
        OfficialIdentitySet? target = null,
        string? predicate = null,
        string? sourceObservationId = null) => new(
        FactsSchemaIds.PublisherRelation,
        source ?? LuWork(),
        target ?? LuTarget(),
        predicate ?? ConsolidatesPredicate,
        sourceObservationId ?? ObservationId,
        MultimapAxioms());

    internal static DerivedInverseRelation DerivedInverse()
    {
        var forward = PublisherRelation();
        return new DerivedInverseRelation(
            FactsSchemaIds.DerivedInverseRelation,
            forward.Target,
            forward.Source,
            ConsolidatedByPredicate,
            ConsolidatesPredicate,
            InverseAxiom(),
            forward);
    }

    internal static LocalInboundView InboundView(bool scopeIsComplete = false)
    {
        var contributor = PublisherRelation();
        return new LocalInboundView(
            FactsSchemaIds.LocalInboundView,
            contributor.Target,
            ConsolidatesPredicate,
            scopeIsComplete,
            ScopeDigest,
            [contributor]);
    }

    /// <summary>A LU statute target, to which ECLI does not apply at all.</summary>
    internal static RelationFact AssertedFact() => new(
        FactsSchemaIds.RelationFact,
        RelationAssertionKind.PublisherAsserted,
        TargetBodyScope.BodyInScopeHeld,
        EcliState.EcliNotApplicable,
        PublisherRelation(),
        null,
        null);

    /// <summary>A case target carrying its ECLI inside its identity set.</summary>
    internal static RelationFact CaseFactWithEcli() => new(
        FactsSchemaIds.RelationFact,
        RelationAssertionKind.PublisherAsserted,
        TargetBodyScope.BodyInScopeNotHeld,
        EcliState.EcliPresent,
        PublisherRelation(target: EuCaseWithEcli()),
        null,
        null);

    /// <summary>A case target whose publisher record carries no ECLI.</summary>
    internal static RelationFact CaseFactWithoutEcli() => new(
        FactsSchemaIds.RelationFact,
        RelationAssertionKind.PublisherAsserted,
        TargetBodyScope.BodyInScopeNotHeld,
        EcliState.EcliNotInThisSet,
        PublisherRelation(target: EuCaseWithoutEcli()),
        null,
        null);

    internal static PublisherDate YearOnlyDate() => new(
        FactsSchemaIds.PublisherDate,
        "2019",
        PublisherDate.GYear,
        DatePrecision.Year,
        DateOpenSentinel.NotOpen);

    internal static PublisherDate DayDate() => new(
        FactsSchemaIds.PublisherDate,
        "2019-07-15",
        PublisherDate.Date,
        DatePrecision.YearMonthDay,
        DateOpenSentinel.NotOpen);

    internal static PublisherDate OpenEndedDate() => new(
        FactsSchemaIds.PublisherDate,
        PublisherDate.OpenEndedLexicalValue,
        PublisherDate.Date,
        DatePrecision.YearMonthDay,
        DateOpenSentinel.OpenEnded);

    internal static PublisherDateFact DateFact(
        PublisherDate? date = null,
        DateSemanticRole role = DateSemanticRole.RoleNotStatedByPublisher,
        string? rawQualifier = null,
        string? comment = null,
        TranspositionEvidence evidence = TranspositionEvidence.None) => new(
        FactsSchemaIds.PublisherDateFact,
        LuWork(),
        date ?? YearOnlyDate(),
        ConsolidatesPredicate,
        MultimapAxioms()[0],
        rawQualifier,
        comment,
        role,
        evidence,
        Authority,
        ObservationId);

    internal static VocabularyDrift Drift() =>
        ClosedVocabulary.TryRead<DateSemanticRole>(
            "ratification_date",
            ObservationId,
            out _,
            out var drift)
            ? throw new InvalidOperationException("ratification_date must be drift.")
            : drift!;
}
