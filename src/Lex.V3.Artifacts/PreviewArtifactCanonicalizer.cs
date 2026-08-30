using System.Globalization;
using System.Text;
using Lex.V3.Contracts;

namespace Lex.V3.Artifacts;

public static class PreviewArtifactCanonicalizer
{
    public static byte[] GetSigningBytes(PreviewArtifactManifest manifest)
    {
        ArgumentNullException.ThrowIfNull(manifest);

        var builder = new StringBuilder(V3SchemaIds.PreviewArtifactSignature).Append('\n');
        Append(builder, "schema", manifest.Schema);
        Append(builder, "schema_resource", manifest.SchemaResource);
        Append(builder, "schema_sha256", manifest.SchemaSha256);
        Append(builder, "evidence_class", manifest.EvidenceClass);
        Append(builder, "synthetic", manifest.Synthetic ? "true" : "false");
        Append(builder, "source_kind", manifest.SourceKind);
        Append(builder, "environment.class", manifest.Environment.Class);
        Append(builder, "environment.binding", manifest.Environment.Binding);
        Append(builder, "issuer.role", manifest.Issuer.Role);
        Append(builder, "issuer.issuer_id", manifest.Issuer.IssuerId);
        Append(builder, "issuer.key_id", manifest.Issuer.KeyId);
        AppendContract(builder, "contract_set.envelope", manifest.ContractSet.Envelope);
        AppendContract(builder, "contract_set.object_set", manifest.ContractSet.ObjectSet);
        AppendContract(
            builder,
            "contract_set.operation_catalog",
            manifest.ContractSet.OperationCatalog);
        AppendContract(
            builder,
            "contract_set.refusal_registry",
            manifest.ContractSet.RefusalRegistry);
        Append(builder, "payload.schema", manifest.Payload.Schema);
        Append(builder, "payload.schema_resource", manifest.Payload.SchemaResource);
        Append(builder, "payload.schema_sha256", manifest.Payload.SchemaSha256);
        Append(builder, "payload.sha256", manifest.Payload.Sha256);
        Append(builder, "payload.bytes", manifest.Payload.Bytes.ToString(CultureInfo.InvariantCulture));
        Append(builder, "payload.media_type", manifest.Payload.MediaType);
        Append(builder, "attestation.purpose", manifest.Attestation.Purpose);
        Append(builder, "attestation.algorithm", manifest.Attestation.Algorithm);
        Append(builder, "attestation.signature_format", manifest.Attestation.SignatureFormat);
        return Encoding.UTF8.GetBytes(builder.ToString());
    }

    private static void AppendContract(
        StringBuilder builder,
        string prefix,
        PreviewTrackedSchemaReference reference)
    {
        Append(builder, prefix + ".schema", reference.Schema);
        Append(builder, prefix + ".schema_resource", reference.SchemaResource);
        Append(builder, prefix + ".sha256", reference.Sha256);
    }

    private static void Append(StringBuilder builder, string name, string value)
    {
        var nameLength = Encoding.UTF8.GetByteCount(name);
        var valueLength = Encoding.UTF8.GetByteCount(value);
        builder
            .Append(nameLength.ToString(CultureInfo.InvariantCulture))
            .Append(':')
            .Append(name)
            .Append('=')
            .Append(valueLength.ToString(CultureInfo.InvariantCulture))
            .Append(':')
            .Append(value)
            .Append('\n');
    }
}
