using System.Buffers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using Lex.V3.Contracts;
using Lex.V3.Contracts.Facts;
using Lex.V3.Contracts.Source.Core;
using Lex.V3.Contracts.Source.Europe;

namespace Lex.V3.Ingest.Europe;

/// <summary>
/// Writes one located-amendment production as deterministic UTF-8 JSON while preserving every
/// publisher observation, corpus-dependent projection and typed exclusion.
/// </summary>
public static class EuLocatedAmendmentProductionCanonicalWriter
{
    public const string Schema = "eu_located_amendment_production/1";
    private const string DigestDomain = Schema + "\n";

    public static string Write(Stream destination, EuLocatedAmendmentProduction production)
    {
        ArgumentNullException.ThrowIfNull(destination);
        ArgumentNullException.ThrowIfNull(production);
        if (!destination.CanWrite)
            throw new ArgumentException("The canonical destination must be writable.", nameof(destination));

        using var buffer = new MemoryStream();
        using (var writer = NewWriter(buffer))
        {
            writer.WriteStartObject();
            writer.WriteString("schema", Schema);
            writer.WritePropertyName("coverage");
            writer.WriteStartObject();
            writer.WriteString("state", production.Coverage.State);
            writer.WriteString("unmet_prerequisite", production.Coverage.UnmetPrerequisite);
            writer.WriteEndObject();
            WriteSortedArray(writer, "admitted", production.Admitted.Select(AdmittedBytes));
            WriteSortedArray(writer, "ambiguous", production.Ambiguous.Select(AmbiguousBytes));
            WriteSortedArray(writer, "excluded", production.Excluded.Select(ExcludedBytes));
            writer.WriteEndObject();
            writer.Flush();
        }

        buffer.WriteByte((byte)'\n');
        var bytes = buffer.ToArray();
        destination.Write(bytes);
        return Digest(bytes);
    }

    private static byte[] AdmittedBytes(EuPublisherMarkedAmendmentAttribution value) => Item(writer =>
    {
        writer.WriteStartObject();
        writer.WritePropertyName("observation");
        WriteObservation(writer, value.Observation);
        var axiom = value.Axiom;
        var asserted = axiom.Edge.Asserted;
        writer.WritePropertyName("source_identity");
        WriteIdentity(writer, asserted.Source);
        writer.WritePropertyName("target_identity");
        WriteIdentity(writer, asserted.Target);
        writer.WriteString("predicate_iri", asserted.PredicateUri);
        writer.WriteString("source_observation_id", asserted.SourceObservationId);
        writer.WriteString("target_body_scope", BodyScope(axiom.Edge.Fact.TargetBodyScope));
        writer.WriteString("remote_axiom_id", axiom.Axiom.RemoteAxiomId);
        writer.WriteString("location", axiom.Location.RawValue);
        writer.WritePropertyName("role");
        WriteAuthorityToken(writer, axiom.Role);
        WriteNullable(writer, "start_of_validity", axiom.StartOfValidity?.RawLexicalValue);
        WriteNullable(writer, "end_of_validity", axiom.EndOfValidity?.RawLexicalValue);
        writer.WriteString("type_of_link_target", axiom.TypeOfLinkTarget);
        writer.WritePropertyName("interpretation_profile_ref");
        WriteArtifact(writer, value.InterpretationProfileRef);
        writer.WriteEndObject();
    });

    private static byte[] AmbiguousBytes(EuLocatedAmendmentAmbiguity value) => Item(writer =>
    {
        writer.WriteStartObject();
        writer.WritePropertyName("observation");
        WriteObservation(writer, value.Observation);
        writer.WritePropertyName("candidate_instrument_iris");
        WriteStrings(writer, value.CandidateInstrumentIris.Order(StringComparer.Ordinal));
        writer.WriteEndObject();
    });

    private static byte[] ExcludedBytes(EuLocatedAmendmentExclusion value) => Item(writer =>
    {
        writer.WriteStartObject();
        writer.WritePropertyName("observation");
        WriteObservation(writer, value.Observation);
        writer.WriteString("kind", Exclusion(value.Kind));
        WriteNullable(writer, "projection_refusal",
            value.ProjectionRefusal is null ? null : ProjectionRefusal(value.ProjectionRefusal.Value));
        WriteNullable(writer, "detail", value.Detail);
        writer.WriteEndObject();
    });

    private static void WriteObservation(Utf8JsonWriter writer, EuLocatedAmendmentAxiomObservation value)
    {
        writer.WriteStartObject();
        writer.WriteString("axiom_iri", value.AxiomIri);
        writer.WritePropertyName("annotated_source_iris");
        WriteStrings(writer, value.AnnotatedSourceIris.Order(StringComparer.Ordinal));
        writer.WriteString("annotated_property_iri", value.AnnotatedPropertyIri);
        writer.WritePropertyName("annotated_target_iris");
        WriteStrings(writer, value.AnnotatedTargetIris.Order(StringComparer.Ordinal));
        WriteSortedArray(writer, "raw_properties", value.RawProperties.Select(RawPropertyBytes));
        writer.WritePropertyName("interpretation_profile_refs");
        writer.WriteStartArray();
        foreach (var reference in value.InterpretationProfileRefs
                     .OrderBy(static item => item.ResourceId, StringComparer.Ordinal)
                     .ThenBy(static item => item.Sha256, StringComparer.Ordinal))
            WriteArtifact(writer, reference);
        writer.WriteEndArray();
        writer.WriteEndObject();
    }

    private static byte[] RawPropertyBytes(EuLocatedAmendmentRawProperty value) => Item(writer =>
    {
        writer.WriteStartObject();
        writer.WriteString("predicate_iri", value.PredicateIri);
        writer.WritePropertyName("value");
        writer.WriteStartObject();
        writer.WriteString("kind", RdfKind(value.Value.Kind));
        WriteNullable(writer, "value", value.Value.Value);
        WriteNullable(writer, "datatype", value.Value.Datatype);
        WriteNullable(writer, "language", value.Value.Language);
        writer.WriteEndObject();
        writer.WriteEndObject();
    });

    private static void WriteIdentity(Utf8JsonWriter writer, OfficialIdentitySet value)
    {
        writer.WriteStartObject();
        writer.WriteString("publisher", value.Publisher switch
        {
            PublisherId.LuLegilux => "lu-legilux",
            PublisherId.EuEurLex => "eu-eurlex",
            _ => throw new ArgumentOutOfRangeException(nameof(value), value.Publisher, null),
        });
        writer.WritePropertyName("identifiers");
        writer.WriteStartArray();
        foreach (var identifier in value.Identifiers
                     .OrderBy(static item => IdentifierFamily(item.Family), StringComparer.Ordinal)
                     .ThenBy(static item => item.RawValue, StringComparer.Ordinal))
        {
            writer.WriteStartObject();
            writer.WriteString("family", IdentifierFamily(identifier.Family));
            writer.WriteString("raw_value", identifier.RawValue);
            writer.WriteEndObject();
        }
        writer.WriteEndArray();
        writer.WriteEndObject();
    }

    private static void WriteArtifact(Utf8JsonWriter writer, SourceArtifactRef value)
    {
        writer.WriteStartObject();
        writer.WriteString("resource_id", value.ResourceId);
        writer.WriteString("sha256", value.Sha256);
        writer.WriteEndObject();
    }

    private static void WriteAuthorityToken(Utf8JsonWriter writer, EuAuthorityQualifiedToken value)
    {
        writer.WriteStartObject();
        writer.WriteString("code", value.Code);
        writer.WriteString("authority_uri", value.AuthorityUri);
        WriteNullable(writer, "value", value.Value);
        writer.WriteEndObject();
    }

    private static void WriteSortedArray(
        Utf8JsonWriter writer,
        string name,
        IEnumerable<byte[]> items)
    {
        writer.WritePropertyName(name);
        writer.WriteStartArray();
        foreach (var item in items.Order(ByteArrayComparer.Instance))
        {
            using var document = JsonDocument.Parse(item);
            document.RootElement.WriteTo(writer);
        }
        writer.WriteEndArray();
    }

    private static void WriteStrings(Utf8JsonWriter writer, IEnumerable<string> values)
    {
        writer.WriteStartArray();
        foreach (var value in values)
            writer.WriteStringValue(value);
        writer.WriteEndArray();
    }

    private static void WriteNullable(Utf8JsonWriter writer, string name, string? value)
    {
        if (value is null)
            writer.WriteNull(name);
        else
            writer.WriteString(name, value);
    }

    private static byte[] Item(Action<Utf8JsonWriter> write)
    {
        var output = new ArrayBufferWriter<byte>();
        using var writer = NewWriter(output);
        write(writer);
        writer.Flush();
        return output.WrittenSpan.ToArray();
    }

    private static Utf8JsonWriter NewWriter(Stream output) => new(output, Options());
    private static Utf8JsonWriter NewWriter(IBufferWriter<byte> output) => new(output, Options());
    private static JsonWriterOptions Options() => new()
    {
        Encoder = JavaScriptEncoder.Default,
        Indented = false,
        SkipValidation = false,
    };

    private static string Digest(ReadOnlySpan<byte> bytes)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        hash.AppendData(Encoding.ASCII.GetBytes(DigestDomain));
        hash.AppendData(bytes);
        Span<byte> digest = stackalloc byte[SHA256.HashSizeInBytes];
        hash.GetHashAndReset(digest);
        return Convert.ToHexStringLower(digest);
    }

    private static string BodyScope(TargetBodyScope value) => value switch
    {
        TargetBodyScope.BodyInScopeHeld => "body_in_scope_held",
        TargetBodyScope.BodyInScopeNotHeld => "body_in_scope_not_held",
        TargetBodyScope.BodyOutsideScope => "body_outside_scope",
        _ => throw new ArgumentOutOfRangeException(nameof(value), value, null),
    };

    private static string Exclusion(EuLocatedAmendmentExclusionKind value) => value switch
    {
        EuLocatedAmendmentExclusionKind.SourceOutsideVerifiedCorpus => "source_outside_verified_corpus",
        EuLocatedAmendmentExclusionKind.PublisherWorkIdentityNotAdmitted => "publisher_work_identity_not_admitted",
        EuLocatedAmendmentExclusionKind.ProjectionRefused => "projection_refused",
        _ => throw new ArgumentOutOfRangeException(nameof(value), value, null),
    };

    private static string ProjectionRefusal(EuLocatedAmendmentAxiomProjectionRefusal value) => value switch
    {
        EuLocatedAmendmentAxiomProjectionRefusal.None => "none",
        EuLocatedAmendmentAxiomProjectionRefusal.PublisherTargetAmbiguous => "publisher_target_ambiguous",
        EuLocatedAmendmentAxiomProjectionRefusal.SourceIdentityDoesNotMatchObservation => "source_identity_does_not_match_observation",
        EuLocatedAmendmentAxiomProjectionRefusal.TargetIdentityDoesNotMatchObservation => "target_identity_does_not_match_observation",
        EuLocatedAmendmentAxiomProjectionRefusal.RequiredQualifierMissing => "required_qualifier_missing",
        EuLocatedAmendmentAxiomProjectionRefusal.QualifierRepeated => "qualifier_repeated",
        EuLocatedAmendmentAxiomProjectionRefusal.QualifierNotPlainLiteral => "qualifier_not_plain_literal",
        EuLocatedAmendmentAxiomProjectionRefusal.QualifierValueNotAdmitted => "qualifier_value_not_admitted",
        _ => throw new ArgumentOutOfRangeException(nameof(value), value, null),
    };

    private static string RdfKind(RepeatedEnumerationRdfTermKind value) => value switch
    {
        RepeatedEnumerationRdfTermKind.Iri => "iri",
        RepeatedEnumerationRdfTermKind.BlankNode => "blank_node",
        RepeatedEnumerationRdfTermKind.Literal => "literal",
        RepeatedEnumerationRdfTermKind.Unbound => "unbound",
        _ => throw new ArgumentOutOfRangeException(nameof(value), value, null),
    };

    private static string IdentifierFamily(FactsIdentifierFamily value) => value switch
    {
        FactsIdentifierFamily.Eli => "eli",
        FactsIdentifierFamily.Celex => "celex",
        FactsIdentifierFamily.Ecli => "ecli",
        FactsIdentifierFamily.CellarWorkUri => "cellar_work_uri",
        FactsIdentifierFamily.CellarResourceUri => "cellar_resource_uri",
        FactsIdentifierFamily.CellarPsiUri => "cellar_psi_uri",
        FactsIdentifierFamily.Memorial => "memorial",
        FactsIdentifierFamily.HistoricalLegalId => "historical_legal_id",
        _ => throw new ArgumentOutOfRangeException(nameof(value), value, null),
    };

    private sealed class ByteArrayComparer : IComparer<byte[]>
    {
        public static ByteArrayComparer Instance { get; } = new();
        public int Compare(byte[]? x, byte[]? y) => x.AsSpan().SequenceCompareTo(y);
    }
}
