using System.Collections.ObjectModel;
using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using Lex.V3.Contracts.Source.Core;

namespace Lex.V3.Contracts.Canon;

/// <summary>Why candidate canon/2 alias entries could not form one direct bijective artifact.</summary>
public enum Canon2AliasArtifactRefusal
{
    [JsonStringEnumMemberName("none")]
    None = 0,

    [JsonStringEnumMemberName("duplicate_source")]
    DuplicateSource = 1,

    [JsonStringEnumMemberName("duplicate_target")]
    DuplicateTarget = 2,

    [JsonStringEnumMemberName("coordinate_collision")]
    CoordinateCollision = 3,
}

/// <summary>The coordinate a migration identity occupied before and after canonicalization.</summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record Canon2AliasCoordinate
{
    [JsonConstructor]
    public Canon2AliasCoordinate(string publisher, string versionKey, string anchor)
    {
        Publisher = ContractValidation.RequireIdentifier(publisher, nameof(publisher));
        VersionKey = ContractValidation.RequireIdentifier(versionKey, nameof(versionKey));
        Anchor = ContractValidation.RequireIdentifier(anchor, nameof(anchor));
    }

    public string Publisher { get; }

    public string VersionKey { get; }

    public string Anchor { get; }
}

/// <summary>
/// One changed migration identity. Source and target coordinates are carried separately so the
/// same-version-key-and-anchor rule is checked rather than implied by a shared field.
/// </summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record Canon2AliasEntry
{
    [JsonConstructor]
    public Canon2AliasEntry(
        Canon2AliasCoordinate sourceCoordinate,
        Canon2AliasCoordinate targetCoordinate,
        string sourceIdentity,
        string canonicalizedTo)
    {
        SourceCoordinate = sourceCoordinate
            ?? throw new ArgumentNullException(nameof(sourceCoordinate));
        TargetCoordinate = targetCoordinate
            ?? throw new ArgumentNullException(nameof(targetCoordinate));
        if (sourceCoordinate != targetCoordinate)
        {
            throw new ArgumentException(
                "A canon/2 alias must preserve publisher, version key and anchor.",
                nameof(targetCoordinate));
        }

        SourceIdentity = ContractValidation.RequireIdentifier(sourceIdentity, nameof(sourceIdentity));
        CanonicalizedTo = ContractValidation.RequireIdentifier(canonicalizedTo, nameof(canonicalizedTo));
        if (string.Equals(SourceIdentity, CanonicalizedTo, StringComparison.Ordinal))
        {
            throw new ArgumentException(
                "An unchanged identity is not a canon/2 alias edge.",
                nameof(canonicalizedTo));
        }
    }

    public Canon2AliasCoordinate SourceCoordinate { get; }

    public Canon2AliasCoordinate TargetCoordinate { get; }

    public string SourceIdentity { get; }

    public string CanonicalizedTo { get; }
}

/// <summary>
/// The unsigned <c>lex-canon-aliases/1</c> artifact. This is a deterministic format value, not
/// evidence that its entries are complete, rights-cleared, signed or published.
/// </summary>
public sealed class Canon2AliasArtifact
{
    public const string SchemaId = "lex-canon-aliases/1";

    private Canon2AliasArtifact(ReadOnlyCollection<Canon2AliasEntry> entries) => Entries = entries;

    public IReadOnlyList<Canon2AliasEntry> Entries { get; }

    /// <summary>
    /// Builds one canonical direct graph. Identity uniqueness is coordinate-scoped and one alias
    /// may occupy each coordinate. Because every entry preserves its coordinate and refuses a
    /// self-edge, that one-edge-per-coordinate rule structurally excludes fan-in, fan-out, chains
    /// and cycles.
    /// </summary>
    public static Canon2AliasArtifact? TryCreate(
        IEnumerable<Canon2AliasEntry> entries,
        out Canon2AliasArtifactRefusal refusal,
        out string? detail)
    {
        ArgumentNullException.ThrowIfNull(entries);

        var materialized = entries.ToArray();
        if (materialized.Any(static entry => entry is null))
        {
            throw new ArgumentException("Alias entries cannot contain null.", nameof(entries));
        }

        // Decision 45 scopes a migration identity to its public coordinate. The same opaque
        // identity token may therefore occur at two different coordinates without creating
        // fan-out, fan-in, or an intermediate node. Keep the coordinate in every graph key so a
        // token collision between provisions cannot turn two independent edges into one graph.
        var sourceIdentities = new HashSet<(Canon2AliasCoordinate Coordinate, string Identity)>();
        var targetIdentities = new HashSet<(Canon2AliasCoordinate Coordinate, string Identity)>();
        var sourceCoordinates = new HashSet<Canon2AliasCoordinate>();
        foreach (var entry in materialized)
        {
            if (!sourceIdentities.Add((entry.SourceCoordinate, entry.SourceIdentity)))
            {
                refusal = Canon2AliasArtifactRefusal.DuplicateSource;
                detail = entry.SourceIdentity;
                return null;
            }

            if (!targetIdentities.Add((entry.TargetCoordinate, entry.CanonicalizedTo)))
            {
                refusal = Canon2AliasArtifactRefusal.DuplicateTarget;
                detail = entry.CanonicalizedTo;
                return null;
            }

            if (!sourceCoordinates.Add(entry.SourceCoordinate))
            {
                refusal = Canon2AliasArtifactRefusal.CoordinateCollision;
                detail = CoordinateDetail(entry.SourceCoordinate);
                return null;
            }
        }

        Array.Sort(materialized, CompareEntries);
        refusal = Canon2AliasArtifactRefusal.None;
        detail = null;
        return new Canon2AliasArtifact(Array.AsReadOnly(materialized));
    }

    private static int CompareEntries(Canon2AliasEntry left, Canon2AliasEntry right)
    {
        var comparison = string.CompareOrdinal(
            left.SourceCoordinate.Publisher,
            right.SourceCoordinate.Publisher);
        if (comparison != 0)
        {
            return comparison;
        }

        comparison = string.CompareOrdinal(
            left.SourceCoordinate.VersionKey,
            right.SourceCoordinate.VersionKey);
        if (comparison != 0)
        {
            return comparison;
        }

        comparison = string.CompareOrdinal(left.SourceCoordinate.Anchor, right.SourceCoordinate.Anchor);
        if (comparison != 0)
        {
            return comparison;
        }

        comparison = string.CompareOrdinal(left.SourceIdentity, right.SourceIdentity);
        return comparison != 0
            ? comparison
            : string.CompareOrdinal(left.CanonicalizedTo, right.CanonicalizedTo);
    }

    private static string CoordinateDetail(Canon2AliasCoordinate coordinate) =>
        $"{coordinate.Publisher}|{coordinate.VersionKey}|{coordinate.Anchor}";
}

/// <summary>Canonical UTF-8 writer for an unsigned <c>lex-canon-aliases/1</c> artifact.</summary>
public static class Canon2AliasArtifactCanonicalWriter
{
    private static readonly byte[] Domain = Encoding.ASCII.GetBytes(Canon2AliasArtifact.SchemaId + "\n");

    public static string Write(Stream destination, Canon2AliasArtifact artifact)
    {
        ArgumentNullException.ThrowIfNull(destination);
        ArgumentNullException.ThrowIfNull(artifact);
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
            writer.WriteString("schema", Canon2AliasArtifact.SchemaId);
            writer.WritePropertyName("entries");
            writer.WriteStartArray();
            foreach (var entry in artifact.Entries)
            {
                writer.WriteStartObject();
                writer.WritePropertyName("source_coordinate");
                WriteCoordinate(writer, entry.SourceCoordinate);
                writer.WritePropertyName("target_coordinate");
                WriteCoordinate(writer, entry.TargetCoordinate);
                writer.WriteString("source_identity", entry.SourceIdentity);
                writer.WriteString("canonicalized_to", entry.CanonicalizedTo);
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

    internal static string ComputeSha256(ReadOnlySpan<byte> canonicalBytes)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        hash.AppendData(Domain);
        hash.AppendData(canonicalBytes);
        Span<byte> digest = stackalloc byte[SHA256.HashSizeInBytes];
        hash.GetHashAndReset(digest);
        return Convert.ToHexStringLower(digest);
    }

    private static void WriteCoordinate(Utf8JsonWriter writer, Canon2AliasCoordinate coordinate)
    {
        writer.WriteStartObject();
        writer.WriteString("publisher", coordinate.Publisher);
        writer.WriteString("version_key", coordinate.VersionKey);
        writer.WriteString("anchor", coordinate.Anchor);
        writer.WriteEndObject();
    }
}

/// <summary>Reader door that verifies the addressed bytes and their exact canonical form.</summary>
public sealed class VerifiedCanon2AliasArtifact
{
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);

    private VerifiedCanon2AliasArtifact(Canon2AliasArtifact artifact) => Artifact = artifact;

    public Canon2AliasArtifact Artifact { get; }

    public static VerifiedCanon2AliasArtifact ParseAndVerify(
        SourceArtifactRef artifactRef,
        ReadOnlySpan<byte> canonicalBytes)
    {
        ArgumentNullException.ThrowIfNull(artifactRef);
        if (!string.Equals(
                Canon2AliasArtifactCanonicalWriter.ComputeSha256(canonicalBytes),
                artifactRef.Sha256,
                StringComparison.Ordinal))
        {
            throw new ArgumentException(
                "The canon/2 alias bytes do not match their artifact reference.",
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
                "The canon/2 alias bytes are not one valid typed document.",
                nameof(canonicalBytes),
                exception);
        }

        if (!string.Equals(wire.Schema, Canon2AliasArtifact.SchemaId, StringComparison.Ordinal))
        {
            throw new ArgumentException("Unexpected canon/2 alias schema.", nameof(canonicalBytes));
        }

        var artifact = Canon2AliasArtifact.TryCreate(wire.Entries, out var refusal, out var detail);
        if (artifact is null)
        {
            throw new ArgumentException(
                $"The canon/2 alias graph is invalid: {refusal}: {detail}",
                nameof(canonicalBytes));
        }

        using var rebuilt = new MemoryStream();
        Canon2AliasArtifactCanonicalWriter.Write(rebuilt, artifact);
        if (!canonicalBytes.SequenceEqual(rebuilt.ToArray()))
        {
            throw new ArgumentException(
                "The canon/2 alias artifact is not its exact canonical representation.",
                nameof(canonicalBytes));
        }

        return new VerifiedCanon2AliasArtifact(artifact);
    }

    private sealed record WireArtifact(string Schema, IReadOnlyList<Canon2AliasEntry> Entries);
}
