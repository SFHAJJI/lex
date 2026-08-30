using System.Globalization;
using System.Text;
using Lex.V3.Contracts;

namespace Lex.V3.Artifacts;

public static class SyntheticSliceArtifactCanonicalizer
{
    private const string Domain = "lex-v3-synthetic-slice-artifact-signature/1";

    public static byte[] GetSigningBytes(SyntheticSliceArtifactManifest manifest)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        var builder = new StringBuilder(Domain).Append('\n');
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
        for (var index = 0; index < manifest.SchemaTable.Members.Count; index++)
        {
            var member = manifest.SchemaTable.Members[index];
            var prefix = $"schema_table.members[{index.ToString(CultureInfo.InvariantCulture)}]";
            Append(builder, prefix + ".schema", member.Schema);
            Append(builder, prefix + ".schema_resource", member.SchemaResource);
            Append(builder, prefix + ".sha256", member.Sha256);
            Append(builder, prefix + ".bytes", member.Bytes.ToString(CultureInfo.InvariantCulture));
        }

        Append(builder, "control.schema", manifest.Control.Schema);
        Append(builder, "control.schema_resource", manifest.Control.SchemaResource);
        Append(builder, "control.schema_sha256", manifest.Control.SchemaSha256);
        Append(builder, "control.sha256", manifest.Control.Sha256);
        Append(builder, "control.bytes", manifest.Control.Bytes.ToString(CultureInfo.InvariantCulture));
        Append(builder, "control.media_type", manifest.Control.MediaType);
        Append(builder, "attestation.purpose", manifest.Attestation.Purpose);
        Append(builder, "attestation.algorithm", manifest.Attestation.Algorithm);
        Append(builder, "attestation.signature_format", manifest.Attestation.SignatureFormat);
        return Encoding.UTF8.GetBytes(builder.ToString());
    }

    private static void Append(StringBuilder builder, string name, string value)
    {
        builder
            .Append(Encoding.UTF8.GetByteCount(name).ToString(CultureInfo.InvariantCulture))
            .Append(':')
            .Append(name)
            .Append('=')
            .Append(Encoding.UTF8.GetByteCount(value).ToString(CultureInfo.InvariantCulture))
            .Append(':')
            .Append(value)
            .Append('\n');
    }
}
