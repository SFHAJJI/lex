using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using Lex.V3.Contracts;
using Lex.V3.Contracts.Custody;
using Lex.V3.Contracts.Source.Core;
using Lex.V3.Contracts.Source.Corpus;
using Lex.V3.Contracts.Source.Europe;
using Lex.V3.Contracts.Source.Luxembourg;
using Lex.V3.Contracts.Source.Scope;

namespace Lex.V3.Ingest;

public enum LexCorpus6BuildRefusal
{
    [JsonStringEnumMemberName("none")] None = 0,
    [JsonStringEnumMemberName("evidence_incomplete")] EvidenceIncomplete = 1,
    [JsonStringEnumMemberName("eu_rights_binding_missing")] EuropeRightsBindingMissing = 2,
    [JsonStringEnumMemberName("luxembourg_rights_binding_missing")] LuxembourgRightsBindingMissing = 3,
    [JsonStringEnumMemberName("luxembourg_rights_evidence_incomplete")] LuxembourgRightsEvidenceIncomplete = 4,
}

public enum LexCorpus6OutcomeKind
{
    [JsonStringEnumMemberName("acquired")] Acquired = 1,
    [JsonStringEnumMemberName("unavailable")] Unavailable = 2,
    [JsonStringEnumMemberName("refused")] Refused = 3,
    [JsonStringEnumMemberName("rights_withheld")] RightsWithheld = 4,
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record LexCorpus6Member(
    PublisherId Publisher,
    string ObjectRefSha256,
    int SourceOrdinal,
    SourceArtifactRef SourceManifestRef,
    SourceArtifactRef RunIdentity,
    ScopeDisposition BodyDisposition,
    LexCorpus6OutcomeKind Outcome,
    string? BodySha256,
    long? BodyByteLength,
    string? BodyReceiptSha256,
    string RightsDisposition,
    IReadOnlyList<SourceArtifactRef> RightsEvidenceRefs,
    IReadOnlyList<string> Gaps)
{
    public LexCorpus6Member Validate()
    {
        RequireDefined(Publisher, nameof(Publisher));
        RequireSha256(ObjectRefSha256, nameof(ObjectRefSha256));
        ArgumentOutOfRangeException.ThrowIfNegative(SourceOrdinal, nameof(SourceOrdinal));
        ArgumentNullException.ThrowIfNull(SourceManifestRef);
        ArgumentNullException.ThrowIfNull(RunIdentity);
        RequireDefined(BodyDisposition, nameof(BodyDisposition));
        RequireDefined(Outcome, nameof(Outcome));
        ArgumentException.ThrowIfNullOrWhiteSpace(RightsDisposition);
        ArgumentNullException.ThrowIfNull(RightsEvidenceRefs);
        ArgumentNullException.ThrowIfNull(Gaps);
        var carriesHeldBytes = Outcome is LexCorpus6OutcomeKind.Acquired or LexCorpus6OutcomeKind.RightsWithheld;
        if (carriesHeldBytes != (BodySha256 is not null))
        {
            throw new ArgumentException(
                "Exactly an acquired or rights-withheld member carries held-body custody.",
                nameof(BodySha256));
        }

        if (BodySha256 is not null)
        {
            RequireSha256(BodySha256, nameof(BodySha256));
            RequireSha256(BodyReceiptSha256!, nameof(BodyReceiptSha256));
            ArgumentOutOfRangeException.ThrowIfNegative(BodyByteLength!.Value, nameof(BodyByteLength));
        }
        else if (BodyByteLength is not null || BodyReceiptSha256 is not null)
        {
            throw new ArgumentException("A member without acquired bytes carries no body custody fields.");
        }

        RequireSortedArtifacts(RightsEvidenceRefs, nameof(RightsEvidenceRefs));
        RequireSortedStrings(Gaps, nameof(Gaps));
        return this;
    }

    internal static void RequireSortedArtifacts(IReadOnlyList<SourceArtifactRef> values, string name)
    {
        if (values.Any(static value => value is null))
        {
            throw new ArgumentException("Artifact lists cannot contain null.", name);
        }

        for (var i = 1; i < values.Count; i++)
        {
            var left = values[i - 1];
            var right = values[i];
            var order = string.CompareOrdinal(left.ResourceId, right.ResourceId);
            if (order > 0 || order == 0 && string.CompareOrdinal(left.Sha256, right.Sha256) >= 0)
            {
                throw new ArgumentException("Artifact lists must be sorted and unique.", name);
            }
        }
    }

    internal static void RequireSortedStrings(IReadOnlyList<string> values, string name)
    {
        if (values.Any(string.IsNullOrWhiteSpace))
        {
            throw new ArgumentException("String lists cannot contain empty values.", name);
        }

        for (var i = 1; i < values.Count; i++)
        {
            if (string.CompareOrdinal(values[i - 1], values[i]) >= 0)
            {
                throw new ArgumentException("String lists must be sorted and unique.", name);
            }
        }
    }

    internal static string RequireSha256(string value, string name)
    {
        if (value is not { Length: 64 } || value.Any(static c => c is not ((>= '0' and <= '9') or (>= 'a' and <= 'f'))))
        {
            throw new ArgumentException("A SHA-256 must be 64 lowercase hexadecimal characters.", name);
        }

        return value;
    }

    private static T RequireDefined<T>(T value, string name) where T : struct, Enum
    {
        if (!Enum.IsDefined(value))
        {
            throw new ArgumentOutOfRangeException(name, value, "The value is outside the closed vocabulary.");
        }

        return value;
    }
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record LexCorpus6ManifestSet(
    string Schema,
    SourceArtifactRef EuropeSourceSetRef,
    SourceArtifactRef LuxembourgSourceSetRef,
    IReadOnlyList<string> ProfileIdentities,
    IReadOnlyList<string> CorrigendumEvidenceReceiptSha256,
    IReadOnlyList<LexCorpus6Member> Members)
{
    public LexCorpus6ManifestSet Validate()
    {
        if (!string.Equals(Schema, LexCorpus6Builder.Schema, StringComparison.Ordinal))
        {
            throw new ArgumentException("Unexpected corpus manifest-set schema.", nameof(Schema));
        }

        ArgumentNullException.ThrowIfNull(EuropeSourceSetRef);
        ArgumentNullException.ThrowIfNull(LuxembourgSourceSetRef);
        ArgumentNullException.ThrowIfNull(ProfileIdentities);
        ArgumentNullException.ThrowIfNull(CorrigendumEvidenceReceiptSha256);
        ArgumentNullException.ThrowIfNull(Members);
        foreach (var value in ProfileIdentities.Concat(CorrigendumEvidenceReceiptSha256))
        {
            LexCorpus6Member.RequireSha256(value, nameof(ProfileIdentities));
        }

        LexCorpus6Member.RequireSortedStrings(ProfileIdentities, nameof(ProfileIdentities));
        LexCorpus6Member.RequireSortedStrings(CorrigendumEvidenceReceiptSha256, nameof(CorrigendumEvidenceReceiptSha256));
        if (Members.Count == 0 || Members.Any(static member => member is null))
        {
            throw new ArgumentException("A corpus manifest set requires members.", nameof(Members));
        }

        foreach (var member in Members)
        {
            member.Validate();
        }

        for (var i = 1; i < Members.Count; i++)
        {
            if (CompareMembers(Members[i - 1], Members[i]) >= 0)
            {
                throw new ArgumentException("Corpus members must be sorted and unique.", nameof(Members));
            }
        }

        return this;
    }

    internal static int CompareMembers(LexCorpus6Member left, LexCorpus6Member right)
    {
        var publisher = left.Publisher.CompareTo(right.Publisher);
        return publisher != 0 ? publisher : string.CompareOrdinal(left.ObjectRefSha256, right.ObjectRefSha256);
    }
}

public static class LexCorpus6Builder
{
    public const string Schema = "lex-corpus/6";
    private static readonly byte[] Domain = Encoding.ASCII.GetBytes(Schema + "\n");

    public static LexCorpus6BuildResult? TryBuild(
        Stage3DerivationProfileEnvelope profileEnvelope,
        EuRightsMatrix euRightsMatrix,
        out LexCorpus6BuildRefusal refusal,
        out string? detail)
    {
        ArgumentNullException.ThrowIfNull(profileEnvelope);
        ArgumentNullException.ThrowIfNull(euRightsMatrix);
        refusal = LexCorpus6BuildRefusal.None;
        detail = null;
        var composition = profileEnvelope.BodyComposition;
        var evidence = composition.Envelope;
        var eu = evidence.Europe;
        var lu = evidence.Luxembourg;
        if (eu.CorpusRecordSetRef is null || eu.CorpusRecordSet is null ||
            lu.CorpusRecordSetRef is null || lu.CorpusRecordSet is null || eu.CorrigendumTripwires is null)
        {
            refusal = LexCorpus6BuildRefusal.EvidenceIncomplete;
            detail = "A source record set or corrigendum completion is missing.";
            return null;
        }

        if (eu.HeldBodyContentClasses is null)
        {
            refusal = LexCorpus6BuildRefusal.EuropeRightsBindingMissing;
            detail = "The EU adapter did not retain its held-body content-class population.";
            return null;
        }

        var luInputs = composition.LuxembourgDerivationPopulation.Inputs
            .ToDictionary(static input => input.CorpusRecord.ObjectRef);
        if (luInputs.Values.Any(static input => input.RightsResolution is null))
        {
            refusal = LexCorpus6BuildRefusal.LuxembourgRightsBindingMissing;
            detail = "A held Luxembourg body has no final two-channel rights resolution.";
            return null;
        }

        var members = new List<LexCorpus6Member>();
        foreach (var record in eu.CorpusRecordSet.Set.Records)
        {
            EuContentClassObservation? observedClass = null;
            EuRightsDisposition? rights = null;
            if (record.Body.Kind == CorpusBodyRecordKind.Held)
            {
                if (!eu.HeldBodyContentClasses.TryGetValue(record.ObjectRef, out observedClass))
                {
                    refusal = LexCorpus6BuildRefusal.EuropeRightsBindingMissing;
                    detail = record.ObjectRef.PublisherUri;
                    return null;
                }

                rights = euRightsMatrix.For(observedClass.ContentClass);
            }

            members.Add(MemberFromRecord(
                PublisherId.EuEurLex,
                record,
                record.Body.Kind == CorpusBodyRecordKind.Held ? LexCorpus6OutcomeKind.Acquired : OutcomeOf(record),
                rights is null ? "not_applicable" : ContractWire.NameOf(rights.Basis),
                SortArtifacts(rights is null ? [] : [observedClass!.EvidenceRef, rights.EvidenceRef]),
                GapOf(record)));
        }

        foreach (var record in lu.CorpusRecordSet.Set.Records)
        {
            if (record.Body.Kind != CorpusBodyRecordKind.Held)
            {
                members.Add(MemberFromRecord(PublisherId.LuLegilux, record, OutcomeOf(record), "not_applicable", [], GapOf(record)));
                continue;
            }

            if (!luInputs.TryGetValue(record.ObjectRef, out var input) || input.RightsResolution is null)
            {
                refusal = LexCorpus6BuildRefusal.LuxembourgRightsBindingMissing;
                detail = record.ObjectRef.PublisherUri;
                return null;
            }

            var rights = input.RightsResolution;
            if (!IsTerminalLuxembourgRights(rights.Disposition))
            {
                refusal = LexCorpus6BuildRefusal.LuxembourgRightsEvidenceIncomplete;
                detail = $"{record.ObjectRef.PublisherUri}: {rights.ReasonCode}";
                return null;
            }

            var admitted = rights.ChannelsAgreeOnAdmittingLicence;
            var rightsEvidence = new List<SourceArtifactRef>
            {
                rights.SparqlObservations.EnumerationRef,
                rights.InFileObservations.EnumerationRef,
            };
            if (rights.SparqlObservation is not null) rightsEvidence.Add(rights.SparqlObservation.EvidenceRef);
            if (rights.InFileObservation is not null) rightsEvidence.Add(rights.InFileObservation.EvidenceRef);
            members.Add(MemberFromRecord(
                PublisherId.LuLegilux,
                record,
                admitted ? LexCorpus6OutcomeKind.Acquired : LexCorpus6OutcomeKind.RightsWithheld,
                rights.ReasonCode,
                SortArtifacts(rightsEvidence),
                admitted ? [] : [rights.ReasonCode]));
        }

        var set = new LexCorpus6ManifestSet(
            Schema,
            eu.CorpusRecordSetRef,
            lu.CorpusRecordSetRef,
            ProfileIdentities(profileEnvelope),
            CorrigendumReceipts(eu.CorrigendumTripwires),
            members.OrderBy(static member => member, Comparer<LexCorpus6Member>.Create(LexCorpus6ManifestSet.CompareMembers)).ToArray()).Validate();
        var bytes = Write(set);
        var digest = ComputeSha256(bytes);
        var artifactRef = new SourceArtifactRef(ResourceIdOf(digest), digest);
        var verified = VerifiedLexCorpus6ManifestSet.ParseAndVerify(artifactRef, bytes);
        return new LexCorpus6BuildResult(artifactRef, bytes, verified);
    }

    internal static byte[] Write(LexCorpus6ManifestSet set)
    {
        set.Validate();
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream, new JsonWriterOptions
        {
            Encoder = JavaScriptEncoder.Default,
            Indented = false,
            SkipValidation = false,
        }))
        {
            writer.WriteStartObject();
            writer.WriteString("schema", Schema);
            WriteArtifact(writer, "eu_source_set_ref", set.EuropeSourceSetRef);
            WriteArtifact(writer, "luxembourg_source_set_ref", set.LuxembourgSourceSetRef);
            WriteStrings(writer, "profile_identities", set.ProfileIdentities);
            WriteStrings(writer, "corrigendum_evidence_receipt_sha256", set.CorrigendumEvidenceReceiptSha256);
            writer.WriteStartArray("members");
            foreach (var member in set.Members)
            {
                writer.WriteStartObject();
                writer.WriteString("publisher", ContractWire.NameOf(member.Publisher));
                writer.WriteString("object_ref_sha256", member.ObjectRefSha256);
                writer.WriteNumber("source_ordinal", member.SourceOrdinal);
                WriteArtifact(writer, "source_manifest_ref", member.SourceManifestRef);
                WriteArtifact(writer, "run_identity", member.RunIdentity);
                writer.WriteString("body_disposition", ContractWire.NameOf(member.BodyDisposition));
                writer.WriteString("outcome", ContractWire.NameOf(member.Outcome));
                if (member.BodySha256 is null)
                {
                    writer.WriteNull("body_sha256"); writer.WriteNull("body_byte_length"); writer.WriteNull("body_receipt_sha256");
                }
                else
                {
                    writer.WriteString("body_sha256", member.BodySha256);
                    writer.WriteNumber("body_byte_length", member.BodyByteLength!.Value);
                    writer.WriteString("body_receipt_sha256", member.BodyReceiptSha256);
                }

                writer.WriteString("rights_disposition", member.RightsDisposition);
                writer.WriteStartArray("rights_evidence_refs");
                foreach (var artifact in member.RightsEvidenceRefs) WriteArtifactValue(writer, artifact);
                writer.WriteEndArray();
                WriteStrings(writer, "gaps", member.Gaps);
                writer.WriteEndObject();
            }

            writer.WriteEndArray(); writer.WriteEndObject(); writer.Flush();
        }

        stream.WriteByte((byte)'\n');
        return stream.ToArray();
    }

    internal static string ComputeSha256(ReadOnlySpan<byte> bytes)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        hash.AppendData(Domain); hash.AppendData(bytes);
        Span<byte> digest = stackalloc byte[SHA256.HashSizeInBytes];
        hash.GetHashAndReset(digest);
        return Convert.ToHexStringLower(digest);
    }

    private static string ResourceIdOf(string digest)
    {
        var uuid = digest[..32].ToCharArray();
        uuid[12] = '5';
        uuid[16] = '8';
        return $"urn:uuid:{new string(uuid, 0, 8)}-{new string(uuid, 8, 4)}-" +
            $"{new string(uuid, 12, 4)}-{new string(uuid, 16, 4)}-{new string(uuid, 20, 12)}";
    }

    private static LexCorpus6Member MemberFromRecord(
        PublisherId publisher, CorpusRecord record, LexCorpus6OutcomeKind outcome,
        string rightsDisposition, IReadOnlyList<SourceArtifactRef> rightsEvidence, IReadOnlyList<string> gaps)
    {
        var acquired = outcome is LexCorpus6OutcomeKind.Acquired or LexCorpus6OutcomeKind.RightsWithheld
            ? record.Body.Receipt
            : null;
        return new LexCorpus6Member(
            publisher, ScopeManifestCanonicalWriter.ComputeObjectRefSha256(record.ObjectRef),
            record.ObjectOrdinal, record.ManifestRef, record.RunIdentity, record.BodyDisposition, outcome,
            acquired?.Reference.ContentSha256, acquired?.Reference.ByteLength,
            acquired is null ? null : DurableBlobWriteReceiptDigest.Of(acquired),
            rightsDisposition, rightsEvidence, gaps).Validate();
    }

    private static LexCorpus6OutcomeKind OutcomeOf(CorpusRecord record) => record.Body.Kind switch
    {
        CorpusBodyRecordKind.NotHeld => LexCorpus6OutcomeKind.Unavailable,
        CorpusBodyRecordKind.PendingAcquisition when record.Body.PendingAcquisitionReason!.Kind == CorpusBodyPendingAcquisitionReasonKind.AcquisitionRefused => LexCorpus6OutcomeKind.Refused,
        CorpusBodyRecordKind.PendingAcquisition => LexCorpus6OutcomeKind.Unavailable,
        CorpusBodyRecordKind.Held => LexCorpus6OutcomeKind.Acquired,
        _ => throw new InvalidOperationException("Unknown corpus body outcome."),
    };

    private static IReadOnlyList<string> GapOf(CorpusRecord record) => record.Body.Kind switch
    {
        CorpusBodyRecordKind.NotHeld => [ContractWire.NameOf(record.Body.NotHeldReason!.Value)],
        CorpusBodyRecordKind.PendingAcquisition => [ContractWire.NameOf(record.Body.PendingAcquisitionReason!.Kind)],
        _ => [],
    };

    private static bool IsTerminalLuxembourgRights(LuxembourgRightsChannelDisposition disposition) =>
        disposition is LuxembourgRightsChannelDisposition.MissingValue
            or LuxembourgRightsChannelDisposition.AgreedSameRunCcBy
            or LuxembourgRightsChannelDisposition.NonAdmittingLicenceScl
            or LuxembourgRightsChannelDisposition.TypedQuarantineUnruledLicence
            or LuxembourgRightsChannelDisposition.TypedQuarantineUnrepresentableLicenceShape
            or LuxembourgRightsChannelDisposition.TypedQuarantineInFileReadingRejected;

    private static IReadOnlyList<SourceArtifactRef> SortArtifacts(IEnumerable<SourceArtifactRef> values) => values
        .Distinct().OrderBy(static value => value.ResourceId, StringComparer.Ordinal)
        .ThenBy(static value => value.Sha256, StringComparer.Ordinal).ToArray();

    private static IReadOnlyList<string> ProfileIdentities(Stage3DerivationProfileEnvelope envelope) =>
        new[]
        {
            envelope.BodyComposition.Envelope.LuxembourgAknArticleInventoryPopulation.IdentitySha256,
            envelope.BodyComposition.Envelope.LuxembourgAknLegalContentPopulation.IdentitySha256,
            envelope.PdfEligibility.IdentitySha256,
            envelope.PdfLayoutEvidence.IdentitySha256,
            envelope.PublisherPdfTextLayer.IdentitySha256,
            envelope.PublisherPdfActScope.IdentitySha256,
        }.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();

    private static IReadOnlyList<string> CorrigendumReceipts(Europe.EuCorrigendumTripwireCompletion completion) =>
        completion.ProductionsByFamilyKey.Values
            .SelectMany(static result => new[]
            {
                result.Expressions!.RetainedDerivation, result.Expressions.RetainedEpisode,
                result.RetainedTripwire!, result.RetainedTripwireLineage!,
            })
            .Select(static receipt => DurableBlobWriteReceiptDigest.Of(receipt!))
            .Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();

    private static void WriteArtifact(Utf8JsonWriter writer, string name, SourceArtifactRef artifact)
    {
        writer.WritePropertyName(name); WriteArtifactValue(writer, artifact);
    }

    private static void WriteArtifactValue(Utf8JsonWriter writer, SourceArtifactRef artifact)
    {
        writer.WriteStartObject(); writer.WriteString("resource_id", artifact.ResourceId);
        writer.WriteString("sha256", artifact.Sha256); writer.WriteEndObject();
    }

    private static void WriteStrings(Utf8JsonWriter writer, string name, IReadOnlyList<string> values)
    {
        writer.WriteStartArray(name); foreach (var value in values) writer.WriteStringValue(value); writer.WriteEndArray();
    }
}

public sealed record LexCorpus6BuildResult(
    SourceArtifactRef ArtifactRef,
    ReadOnlyMemory<byte> CanonicalBytes,
    VerifiedLexCorpus6ManifestSet VerifiedSet);

public sealed class VerifiedLexCorpus6ManifestSet
{
    private VerifiedLexCorpus6ManifestSet(LexCorpus6ManifestSet set) => Set = set;
    public LexCorpus6ManifestSet Set { get; }

    public static VerifiedLexCorpus6ManifestSet ParseAndVerify(SourceArtifactRef artifactRef, ReadOnlySpan<byte> canonicalBytes)
    {
        ArgumentNullException.ThrowIfNull(artifactRef);
        if (!string.Equals(LexCorpus6Builder.ComputeSha256(canonicalBytes), artifactRef.Sha256, StringComparison.Ordinal))
        {
            throw new ArgumentException("The corpus manifest-set bytes do not match their artifact reference.", nameof(canonicalBytes));
        }

        LexCorpus6ManifestSet set;
        try
        {
            set = ContractJson.Deserialize<LexCorpus6ManifestSet>(new UTF8Encoding(false, true).GetString(canonicalBytes)).Validate();
        }
        catch (Exception exception) when (exception is JsonException or DecoderFallbackException or ArgumentException)
        {
            throw new ArgumentException("The corpus manifest-set bytes are not one valid typed document.", nameof(canonicalBytes), exception);
        }

        if (!canonicalBytes.SequenceEqual(LexCorpus6Builder.Write(set)))
        {
            throw new ArgumentException("The corpus manifest set is not its exact canonical representation.", nameof(canonicalBytes));
        }

        return new VerifiedLexCorpus6ManifestSet(set);
    }
}
