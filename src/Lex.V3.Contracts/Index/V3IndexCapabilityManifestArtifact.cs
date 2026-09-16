using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using Lex.V3.Contracts.Source.Core;

namespace Lex.V3.Contracts.Index;

/// <summary>
/// Canonical unsigned wire artifact for one accepted publisher index capability manifest.
/// </summary>
public static class V3IndexCapabilityManifestArtifact
{
    public const string SchemaId = "lex-v3-index-capability-manifest/1";

    private static readonly byte[] Domain = Encoding.ASCII.GetBytes(SchemaId + "\n");
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);

    public static string Write(Stream destination, V3IndexCapabilityManifest manifest)
    {
        ArgumentNullException.ThrowIfNull(destination);
        ArgumentNullException.ThrowIfNull(manifest);
        if (!destination.CanWrite)
        {
            throw new ArgumentException("The canonical destination must be writable.", nameof(destination));
        }

        using var buffer = new MemoryStream();
        using (var writer = new Utf8JsonWriter(
            buffer,
            new JsonWriterOptions
            {
                Encoder = JavaScriptEncoder.Default,
                Indented = false,
                SkipValidation = false,
            }))
        {
            writer.WriteStartObject();
            writer.WriteString("schema", SchemaId);
            writer.WriteString("publisher", ContractWire.NameOf(manifest.Publisher));
            writer.WriteString("index_sha256", manifest.IndexSha256);
            writer.WriteString("period_granularity", V3IndexCapabilityManifest.PeriodGranularity);
            writer.WriteStartArray("cells");
            foreach (var cell in manifest.Cells)
            {
                writer.WriteStartObject();
                writer.WriteString("operation", cell.Operation);
                writer.WriteString("column", cell.Column);
                writer.WriteString("field", cell.Field);
                writer.WriteString("language", cell.Language);
                writer.WriteString("period_from", cell.PeriodFrom);
                writer.WriteString("period_to", cell.PeriodTo);
                writer.WriteNumber("population", cell.Population);
                writer.WriteEndObject();
            }

            writer.WriteEndArray();
            writer.WriteEndObject();
            writer.Flush();
        }

        buffer.WriteByte((byte)'\n');
        var bytes = buffer.ToArray();
        destination.Write(bytes);
        return ComputeSha256(bytes);
    }

    public static string ComputeSha256(ReadOnlySpan<byte> canonicalBytes)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        hash.AppendData(Domain);
        hash.AppendData(canonicalBytes);
        Span<byte> digest = stackalloc byte[SHA256.HashSizeInBytes];
        hash.GetHashAndReset(digest);
        return Convert.ToHexStringLower(digest);
    }

    public static V3IndexCapabilityManifest ParseAndVerify(
        SourceArtifactRef artifactRef,
        ReadOnlySpan<byte> canonicalBytes)
    {
        ArgumentNullException.ThrowIfNull(artifactRef);
        if (!string.Equals(ComputeSha256(canonicalBytes), artifactRef.Sha256, StringComparison.Ordinal))
        {
            throw new ArgumentException(
                "The index capability manifest bytes do not match their artifact reference.",
                nameof(canonicalBytes));
        }

        WireArtifact wire;
        try
        {
            wire = ContractJson.Deserialize<WireArtifact>(StrictUtf8.GetString(canonicalBytes));
        }
        catch (Exception exception) when (exception is JsonException or DecoderFallbackException)
        {
            throw new ArgumentException(
                "The index capability manifest bytes are not one valid typed document.",
                nameof(canonicalBytes),
                exception);
        }

        if (!string.Equals(wire.Schema, SchemaId, StringComparison.Ordinal))
        {
            throw new ArgumentException("Unexpected index capability manifest schema.", nameof(canonicalBytes));
        }

        if (!string.Equals(
                wire.PeriodGranularity,
                V3IndexCapabilityManifest.PeriodGranularity,
                StringComparison.Ordinal))
        {
            throw new ArgumentException(
                "Unexpected index capability period granularity.",
                nameof(canonicalBytes));
        }

        V3IndexCapabilityCell[] cells;
        try
        {
            cells = wire.Cells.Select(cell => new V3IndexCapabilityCell(
                wire.Publisher,
                wire.IndexSha256,
                cell.Operation,
                cell.Column,
                cell.Field,
                cell.Language,
                cell.PeriodFrom,
                cell.PeriodTo,
                cell.Population)).ToArray();
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException)
        {
            throw new ArgumentException(
                "The index capability manifest contains an invalid cell.",
                nameof(canonicalBytes),
                exception);
        }

        if (!V3IndexCapabilityManifest.TryCreate(
                wire.Publisher,
                wire.IndexSha256,
                cells,
                out var manifest,
                out var refusal))
        {
            throw new ArgumentException(
                $"The index capability manifest is invalid: {refusal}.",
                nameof(canonicalBytes));
        }

        using var rebuilt = new MemoryStream();
        Write(rebuilt, manifest!);
        if (!canonicalBytes.SequenceEqual(rebuilt.ToArray()))
        {
            throw new ArgumentException(
                "The index capability manifest is not its exact canonical representation.",
                nameof(canonicalBytes));
        }

        return manifest!;
    }

    private sealed record WireArtifact(
        string Schema,
        PublisherId Publisher,
        string IndexSha256,
        string PeriodGranularity,
        IReadOnlyList<WireCell> Cells);

    private sealed record WireCell(
        string Operation,
        string Column,
        string Field,
        string Language,
        DateOnly PeriodFrom,
        DateOnly PeriodTo,
        long Population);
}
