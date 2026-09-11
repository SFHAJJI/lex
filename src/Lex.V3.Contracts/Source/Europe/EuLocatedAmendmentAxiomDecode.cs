using Lex.V3.Contracts.Source.Core;

namespace Lex.V3.Contracts.Source.Europe;

/// <summary>Why a delivered located-amendment axiom family could not be retained.</summary>
public enum EuLocatedAmendmentAxiomDecodeRefusal
{
    None = 0,
    RowShapeContradictsItsProjectedKind = 1,
    DeliveryCarriesNoRows = 2,
    ParentMissingOrNotAnIri = 3,
    AxiomNodeNotAnIri = 4,
    PredicateNotAnIri = 5,
    PropertyValueIsANonAddressableBlankNode = 6,
    AnnotatedSourceMissingOrNotAnIri = 7,
    AnnotatedSourceDisagreesWithSelectedParent = 8,
    AnnotatedPropertyMissingOrNotAdmitted = 9,
    AxiomTypeMissingOrNotOwlAxiom = 10,
    AnnotatedTargetMissingOrNotAnIri = 11,
    ModelledPredicateDeliveredMoreThanOnce = 12,
}

/// <summary>One publisher property retained exactly as delivered on an axiom node.</summary>
public sealed class EuLocatedAmendmentRawProperty
{
    internal EuLocatedAmendmentRawProperty(string predicateIri, RepeatedEnumerationRdfTerm value)
    {
        PredicateIri = predicateIri;
        Value = value;
    }

    public string PredicateIri { get; }
    public RepeatedEnumerationRdfTerm Value { get; }
}

/// <summary>
/// A proof-bound publisher observation that precedes corpus-dependent target-scope reconciliation.
/// </summary>
/// <remarks>
/// This type deliberately carries no <see cref="TargetBodyScope"/> and cannot become a final
/// <see cref="EuLocatedAmendmentAxiom"/> by itself. The target scope is knowable only after the
/// same run's corpus record set has been verified and reopened.
/// </remarks>
public sealed class EuLocatedAmendmentAxiomObservation
{
    internal EuLocatedAmendmentAxiomObservation(
        string axiomIri,
        string annotatedSourceIri,
        string annotatedPropertyIri,
        IEnumerable<string> annotatedTargetIris,
        IEnumerable<EuLocatedAmendmentRawProperty> rawProperties,
        SourceArtifactRef deliveryRef)
    {
        AxiomIri = axiomIri;
        AnnotatedSourceIri = annotatedSourceIri;
        AnnotatedPropertyIri = annotatedPropertyIri;
        AnnotatedTargetIris = Array.AsReadOnly(annotatedTargetIris.ToArray());
        RawProperties = Array.AsReadOnly(rawProperties.ToArray());
        DeliveryRef = deliveryRef;
    }

    public string AxiomIri { get; }
    public string AnnotatedSourceIri { get; }
    public string AnnotatedPropertyIri { get; }
    public IReadOnlyList<string> AnnotatedTargetIris { get; }
    public IReadOnlyList<EuLocatedAmendmentRawProperty> RawProperties { get; }
    public SourceArtifactRef DeliveryRef { get; }
    public bool IsPublisherTargetAmbiguous => AnnotatedTargetIris.Count > 1;
}

/// <summary>
/// Retains a located-amendment family delivery without guessing the corpus-dependent body scope.
/// </summary>
public static class EuLocatedAmendmentAxiomDecode
{
    public static IReadOnlyList<EuLocatedAmendmentAxiomObservation>? TryDecode(
        IReadOnlyList<RepeatedEnumerationRow> rows,
        RepeatedEnumerationInterpretationProfile profile,
        SourceArtifactRef deliveryRef,
        out EuLocatedAmendmentAxiomDecodeRefusal refusal,
        out string? offendingValue)
    {
        ArgumentNullException.ThrowIfNull(rows);
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentNullException.ThrowIfNull(deliveryRef);
        refusal = EuLocatedAmendmentAxiomDecodeRefusal.None;
        offendingValue = null;

        if (rows.Count == 0)
        {
            refusal = EuLocatedAmendmentAxiomDecodeRefusal.DeliveryCarriesNoRows;
            return null;
        }

        var byAxiom = new Dictionary<string, RawAxiom>(StringComparer.Ordinal);
        foreach (var row in rows)
        {
            if (!TryReadRowShape(row, profile, out var absence, out offendingValue))
            {
                refusal = EuLocatedAmendmentAxiomDecodeRefusal.RowShapeContradictsItsProjectedKind;
                return null;
            }

            var parent = Term(row, profile, "parent");
            if (parent is not { Kind: RepeatedEnumerationRdfTermKind.Iri, Value: not null })
            {
                refusal = EuLocatedAmendmentAxiomDecodeRefusal.ParentMissingOrNotAnIri;
                offendingValue = parent.Value;
                return null;
            }

            if (absence)
            {
                continue;
            }

            var axiom = Term(row, profile, "axiom");
            if (axiom is not { Kind: RepeatedEnumerationRdfTermKind.Iri, Value: not null })
            {
                refusal = EuLocatedAmendmentAxiomDecodeRefusal.AxiomNodeNotAnIri;
                offendingValue = axiom.Value;
                return null;
            }

            var predicate = Term(row, profile, "predicate");
            if (predicate is not { Kind: RepeatedEnumerationRdfTermKind.Iri, Value: not null })
            {
                refusal = EuLocatedAmendmentAxiomDecodeRefusal.PredicateNotAnIri;
                offendingValue = predicate.Value;
                return null;
            }

            var value = Term(row, profile, "value");
            if (value.Kind == RepeatedEnumerationRdfTermKind.BlankNode)
            {
                refusal = EuLocatedAmendmentAxiomDecodeRefusal.PropertyValueIsANonAddressableBlankNode;
                offendingValue = value.Value;
                return null;
            }

            if (!byAxiom.TryGetValue(axiom.Value, out var rawAxiom))
            {
                rawAxiom = new RawAxiom(parent.Value, []);
                byAxiom.Add(axiom.Value, rawAxiom);
            }
            else if (!string.Equals(rawAxiom.ParentIri, parent.Value, StringComparison.Ordinal))
            {
                refusal = EuLocatedAmendmentAxiomDecodeRefusal.AnnotatedSourceDisagreesWithSelectedParent;
                offendingValue = parent.Value;
                return null;
            }

            rawAxiom.Properties.Add(new EuLocatedAmendmentRawProperty(
                predicate.Value,
                value));
        }

        var observations = new List<EuLocatedAmendmentAxiomObservation>(byAxiom.Count);
        foreach (var pair in byAxiom.OrderBy(static pair => pair.Key, StringComparer.Ordinal))
        {
            var observation = DecodeOne(
                pair.Key, pair.Value, deliveryRef, out refusal, out offendingValue);
            if (observation is null)
            {
                return null;
            }

            observations.Add(observation);
        }

        return observations.AsReadOnly();
    }

    private static EuLocatedAmendmentAxiomObservation? DecodeOne(
        string axiomIri,
        RawAxiom rawAxiom,
        SourceArtifactRef deliveryRef,
        out EuLocatedAmendmentAxiomDecodeRefusal refusal,
        out string? offendingValue)
    {
        refusal = EuLocatedAmendmentAxiomDecodeRefusal.None;
        offendingValue = null;
        var properties = rawAxiom.Properties;

        if (!TrySingle(properties, EuObjectFactsDiscoveryPlan.AnnotatedSourcePredicateIri, out var source))
        {
            return Conflict(EuObjectFactsDiscoveryPlan.AnnotatedSourcePredicateIri, out refusal, out offendingValue);
        }
        if (source is not { Kind: RepeatedEnumerationRdfTermKind.Iri, Value: not null })
        {
            refusal = EuLocatedAmendmentAxiomDecodeRefusal.AnnotatedSourceMissingOrNotAnIri;
            offendingValue = axiomIri;
            return null;
        }
        if (!string.Equals(source.Value, rawAxiom.ParentIri, StringComparison.Ordinal))
        {
            refusal = EuLocatedAmendmentAxiomDecodeRefusal.AnnotatedSourceDisagreesWithSelectedParent;
            offendingValue = source.Value;
            return null;
        }

        if (!TrySingle(properties, EuObjectFactsDiscoveryPlan.AnnotatedPropertyPredicateIri, out var property))
        {
            return Conflict(EuObjectFactsDiscoveryPlan.AnnotatedPropertyPredicateIri, out refusal, out offendingValue);
        }
        if (property is not { Kind: RepeatedEnumerationRdfTermKind.Iri, Value: not null } ||
            !string.Equals(property.Value, EuAmendmentRelationVocabulary.AmendsPredicateUri, StringComparison.Ordinal))
        {
            refusal = EuLocatedAmendmentAxiomDecodeRefusal.AnnotatedPropertyMissingOrNotAdmitted;
            offendingValue = property?.Value ?? axiomIri;
            return null;
        }

        if (!TrySingle(properties, EuObjectFactsDiscoveryPlan.RdfTypePredicateIri, out var type))
        {
            return Conflict(EuObjectFactsDiscoveryPlan.RdfTypePredicateIri, out refusal, out offendingValue);
        }
        if (type is not { Kind: RepeatedEnumerationRdfTermKind.Iri, Value: not null } ||
            !string.Equals(type.Value, EuObjectFactsDiscoveryPlan.OwlAxiomClassIri, StringComparison.Ordinal))
        {
            refusal = EuLocatedAmendmentAxiomDecodeRefusal.AxiomTypeMissingOrNotOwlAxiom;
            offendingValue = type?.Value ?? axiomIri;
            return null;
        }

        var targets = properties
            .Where(static item => string.Equals(
                item.PredicateIri,
                EuObjectFactsDiscoveryPlan.AnnotatedTargetPredicateIri,
                StringComparison.Ordinal))
            .Select(static item => item.Value)
            .ToArray();
        if (targets.Length == 0 || targets.Any(static target =>
                target is not { Kind: RepeatedEnumerationRdfTermKind.Iri, Value: not null }))
        {
            refusal = EuLocatedAmendmentAxiomDecodeRefusal.AnnotatedTargetMissingOrNotAnIri;
            offendingValue = targets.FirstOrDefault(static target =>
                target is not { Kind: RepeatedEnumerationRdfTermKind.Iri, Value: not null })?.Value ?? axiomIri;
            return null;
        }

        var targetIris = targets
            .Select(static target => target.Value!)
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToArray();
        return new EuLocatedAmendmentAxiomObservation(
            axiomIri,
            source.Value,
            property.Value,
            targetIris,
            properties,
            deliveryRef);
    }

    private static EuLocatedAmendmentAxiomObservation? Conflict(
        string predicate,
        out EuLocatedAmendmentAxiomDecodeRefusal refusal,
        out string? offendingValue)
    {
        refusal = EuLocatedAmendmentAxiomDecodeRefusal.ModelledPredicateDeliveredMoreThanOnce;
        offendingValue = predicate;
        return null;
    }

    private static bool TrySingle(
        IReadOnlyList<EuLocatedAmendmentRawProperty> properties,
        string predicate,
        out RepeatedEnumerationRdfTerm? value)
    {
        value = null;
        foreach (var property in properties.Where(item =>
                     string.Equals(item.PredicateIri, predicate, StringComparison.Ordinal)))
        {
            if (value is null)
            {
                value = property.Value;
            }
            else if (!Agree(value, property.Value))
            {
                return false;
            }
        }

        return true;
    }

    private static bool Agree(RepeatedEnumerationRdfTerm left, RepeatedEnumerationRdfTerm right) =>
        left.Kind == right.Kind &&
        string.Equals(left.Value, right.Value, StringComparison.Ordinal) &&
        string.Equals(left.Datatype, right.Datatype, StringComparison.Ordinal) &&
        string.Equals(left.Language, right.Language, StringComparison.Ordinal);

    private static bool TryReadRowShape(
        RepeatedEnumerationRow row,
        RepeatedEnumerationInterpretationProfile profile,
        out bool absence,
        out string? offendingValue)
    {
        absence = false;
        var axiom = Term(row, profile, "axiom");
        var predicate = Term(row, profile, "predicate");
        var value = Term(row, profile, "value");
        var marker = Term(row, profile, "value_kind");
        var datatype = Term(row, profile, "datatype_iri");
        var language = Term(row, profile, "language_tag");
        offendingValue = marker.Value ?? datatype.Value ?? language.Value;

        if (!Plain(marker) || !Plain(language) ||
            !(Plain(datatype) || datatype.Kind == RepeatedEnumerationRdfTermKind.Unbound &&
                Plain(language) && !string.IsNullOrEmpty(language.Value)))
        {
            return false;
        }

        if (string.Equals(marker.Value, "unbound", StringComparison.Ordinal))
        {
            absence = axiom.Kind == RepeatedEnumerationRdfTermKind.Unbound &&
                predicate.Kind == RepeatedEnumerationRdfTermKind.Unbound &&
                value.Kind == RepeatedEnumerationRdfTermKind.Unbound &&
                string.IsNullOrEmpty(datatype.Value) && string.IsNullOrEmpty(language.Value);
            return absence;
        }

        var expected = marker.Value switch
        {
            "iri" => RepeatedEnumerationRdfTermKind.Iri,
            "literal" => RepeatedEnumerationRdfTermKind.Literal,
            "unsupported_blank_node" => RepeatedEnumerationRdfTermKind.BlankNode,
            _ => (RepeatedEnumerationRdfTermKind?)null,
        };
        offendingValue = axiom.Value ?? marker.Value;
        return expected is not null && value.Kind == expected &&
            axiom.Kind != RepeatedEnumerationRdfTermKind.Unbound &&
            predicate.Kind != RepeatedEnumerationRdfTermKind.Unbound;
    }

    private static bool Plain(RepeatedEnumerationRdfTerm term) =>
        term.Kind == RepeatedEnumerationRdfTermKind.Literal && term.Datatype is null && term.Language is null;

    private static RepeatedEnumerationRdfTerm Term(
        RepeatedEnumerationRow row,
        RepeatedEnumerationInterpretationProfile profile,
        string variable)
    {
        var index = profile.ProjectionVariables
            .Select(static (name, index) => (name, index))
            .FirstOrDefault(item => string.Equals(item.name, variable, StringComparison.Ordinal))
            .index;
        if (index < 0 || index >= row.Terms.Count ||
            !string.Equals(profile.ProjectionVariables[index], variable, StringComparison.Ordinal))
        {
            throw new ArgumentException($"'{variable}' is not readable from this profile's projection.", nameof(variable));
        }
        return row.Terms[index];
    }

    private sealed record RawAxiom(string ParentIri, List<EuLocatedAmendmentRawProperty> Properties);
}
