using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using Lex.V3.Contracts.Source.Core;
using Lex.V3.Contracts.Source.Luxembourg;
using Lex.V3.Contracts.Source.Scope;

namespace Lex.V3.Ingest.Luxembourg;

/// <summary>
/// The exact set of object identities one Luxembourg run actually observed, in a form that outlives
/// the run.
/// </summary>
/// <remarks>
/// <para>
/// WHY THIS EXISTS. Every admission question on
/// <see cref="LuxembourgProductionScopeReductionEvidenceResolver"/> rests on that run's own derived
/// object-identity set, and until this type existed the set lived only in a private
/// <c>HashSet</c> for the duration of one <c>CreateAsync</c>. A scope manifest could therefore be
/// retained, reopened and re-read afterwards while the thing its bindings were admitted against
/// could not be: a later reviewer could see the conclusion and not the premise.
/// </para>
/// <para>
/// WHAT IT PROVES, AND WHAT IT DOES NOT. Reopening this artifact proves the run's stated observed
/// set is byte-for-byte what it was when the run wrote it -- tamper-evidence over the premise the
/// resolver used. It does NOT prove the publisher ever served those objects. That would require
/// re-deriving the observations from retained rows, which is a different and much larger producer;
/// this type deliberately does not pretend to it.
/// </para>
/// <para>
/// THE TRAP THIS TYPE REFUSES TO WALK INTO. The identity set looks rebuildable from a retained scope
/// manifest, because <see cref="ScopeManifestCanonicalWriter.ComputeObjectRefSha256"/> is public and
/// the manifest carries the objects. A resolver rebuilt that way would admit every binding by
/// construction and prove nothing at all, because the expected side would be derived from the side
/// under test. So the set is retained as its own artifact at the moment the run derives it, from the
/// run's own <see cref="LuxembourgResourceObservation"/> values, and never recomputed from a manifest.
/// </para>
/// <para>
/// IT IS BOUND TO ITS RUN. The canonical bytes carry the run identity reference, so an identity set
/// cannot be presented as another run's premise: the bytes that name run A do not carry run B's
/// digest and <see cref="VerifiedLuxembourgObservedObjectIdentitySet.ParseAndVerify"/> refuses them.
/// </para>
/// </remarks>
public sealed class LuxembourgObservedObjectIdentitySet
{
    /// <summary>The schema every canonical serialization of this set carries.</summary>
    public const string SchemaId = "lex-v3-luxembourg-observed-object-identity-set/1";

    internal LuxembourgObservedObjectIdentitySet(
        SourceArtifactRef runIdentity, IReadOnlyList<string> objectRefSha256Values)
    {
        RunIdentity = runIdentity ?? throw new ArgumentNullException(nameof(runIdentity));
        ArgumentNullException.ThrowIfNull(objectRefSha256Values);
        ObjectRefSha256Values = objectRefSha256Values;
    }

    /// <summary>The run whose observations produced this set. Part of the signed-over bytes.</summary>
    public SourceArtifactRef RunIdentity { get; }

    /// <summary>
    /// The distinct <see cref="ScopeManifestCanonicalWriter.ComputeObjectRefSha256"/> values of the
    /// objects this run observed, ordinal-ascending. Sorted and de-duplicated so two runs that
    /// observed the same objects in a different order, or observed one of them twice, produce the
    /// same bytes: the artifact states a set, and a set has no order and no multiplicity.
    /// </summary>
    public IReadOnlyList<string> ObjectRefSha256Values { get; }

    /// <summary>
    /// Derives the set from the run's own observations. <paramref name="observations"/> must be the
    /// adapter's independently re-derived values, exactly what
    /// <see cref="LuxembourgProductionScopeReductionEvidenceResolver.CreateAsync"/> is handed, never
    /// a caller-transcribed list.
    /// </summary>
    public static LuxembourgObservedObjectIdentitySet FromObservations(
        SourceArtifactRef runIdentity, IReadOnlyList<LuxembourgResourceObservation> observations)
    {
        ArgumentNullException.ThrowIfNull(runIdentity);
        ArgumentNullException.ThrowIfNull(observations);

        var values = new SortedSet<string>(StringComparer.Ordinal);
        foreach (var observation in observations)
        {
            ArgumentNullException.ThrowIfNull(observation);
            values.Add(ScopeManifestCanonicalWriter.ComputeObjectRefSha256(observation.ObjectRef));
        }

        return new LuxembourgObservedObjectIdentitySet(runIdentity, [.. values]);
    }
}

/// <summary>
/// Canonicalizes a <see cref="LuxembourgObservedObjectIdentitySet"/> into deterministic UTF-8 bytes
/// and their domain-separated SHA-256, the same way the corpus and scope writers domain-separate
/// theirs.
/// </summary>
/// <remarks>
/// The artifact reference encoding here is this type's own two explicit fields rather than the
/// Contracts writer's <c>WriteArtifact</c>, which is internal to that assembly and not reachable
/// from <c>Lex.V3.Ingest</c> (Decision 80). It is stated once, here, so there is exactly one
/// definition of what these bytes are.
/// </remarks>
public static class LuxembourgObservedObjectIdentitySetCanonicalWriter
{
    private const string SetDomain = "lex-v3-luxembourg-observed-object-identity-set/1\n";

    /// <summary>Writes the canonical bytes and returns their domain-separated digest.</summary>
    public static string Write(Stream destination, LuxembourgObservedObjectIdentitySet set)
    {
        ArgumentNullException.ThrowIfNull(destination);
        ArgumentNullException.ThrowIfNull(set);
        if (!destination.CanWrite)
        {
            throw new ArgumentException(
                "The canonical destination must be writable.", nameof(destination));
        }

        using var buffer = new MemoryStream();
        using (var writer = NewWriter(buffer))
        {
            writer.WriteStartObject();
            writer.WriteString("schema", LuxembourgObservedObjectIdentitySet.SchemaId);
            writer.WritePropertyName("run_identity");
            writer.WriteStartObject();
            writer.WriteString("resource_id", set.RunIdentity.ResourceId);
            writer.WriteString("sha256", set.RunIdentity.Sha256);
            writer.WriteEndObject();
            writer.WritePropertyName("object_ref_sha256");
            writer.WriteStartArray();
            foreach (var value in set.ObjectRefSha256Values)
            {
                writer.WriteStringValue(value);
            }

            writer.WriteEndArray();
            writer.WriteEndObject();
            writer.Flush();
        }

        buffer.WriteByte((byte)'\n');
        var bytes = buffer.ToArray();
        destination.Write(bytes, 0, bytes.Length);
        return ComputeSetSha256(bytes);
    }

    /// <summary>
    /// The exact digest <see cref="Write"/> returns for its own output, recomputed directly from
    /// durable bytes so a reader can check them against a pinned reference before parsing.
    /// <paramref name="canonicalBytes"/> must be exactly what <see cref="Write"/> wrote, trailing
    /// newline included.
    /// </summary>
    public static string ComputeSetSha256(ReadOnlySpan<byte> canonicalBytes)
    {
        var domain = Encoding.UTF8.GetBytes(SetDomain);
        var buffer = new byte[domain.Length + canonicalBytes.Length];
        domain.CopyTo(buffer.AsSpan());
        canonicalBytes.CopyTo(buffer.AsSpan(domain.Length));
        return Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(buffer));
    }

    internal static Utf8JsonWriter NewWriter(Stream output) => new(
        output,
        new JsonWriterOptions
        {
            Encoder = JavaScriptEncoder.Default,
            Indented = false,
            SkipValidation = false,
        });
}

/// <summary>
/// A set that came back out of durable bytes and was checked against them. Holding one is the
/// evidence that the check ran; there is no constructor that takes a set a caller is already holding.
/// </summary>
public sealed class VerifiedLuxembourgObservedObjectIdentitySet
{
    private VerifiedLuxembourgObservedObjectIdentitySet(
        SourceArtifactRef setRef, LuxembourgObservedObjectIdentitySet set)
    {
        SetRef = setRef;
        Set = set;
    }

    /// <summary>The reference the bytes were proved to carry.</summary>
    public SourceArtifactRef SetRef { get; }

    /// <summary>The set those exact bytes parse into.</summary>
    public LuxembourgObservedObjectIdentitySet Set { get; }

    /// <summary>
    /// Parses <paramref name="canonicalBytes"/> and proves they are this reference's set. Every
    /// rejection is stated as an <see cref="ArgumentException"/> naming the bytes: the reference's
    /// digest, strict UTF-8, the schema, the sorted-and-distinct rule, and the exact canonical round
    /// trip. The round trip is what makes a re-serialization the only accepted form: bytes that
    /// parse to the same set but are not what <see cref="LuxembourgObservedObjectIdentitySetCanonicalWriter.Write"/>
    /// produces are refused rather than quietly normalized.
    /// </summary>
    public static VerifiedLuxembourgObservedObjectIdentitySet ParseAndVerify(
        SourceArtifactRef setRef, ReadOnlySpan<byte> canonicalBytes)
    {
        ArgumentNullException.ThrowIfNull(setRef);

        if (!string.Equals(
                LuxembourgObservedObjectIdentitySetCanonicalWriter.ComputeSetSha256(canonicalBytes),
                setRef.Sha256,
                StringComparison.Ordinal))
        {
            throw new ArgumentException(
                "The observed object identity set bytes do not match their artifact reference.",
                nameof(canonicalBytes));
        }

        var set = Parse(canonicalBytes);

        using var round = new MemoryStream();
        LuxembourgObservedObjectIdentitySetCanonicalWriter.Write(round, set);
        if (!round.ToArray().AsSpan().SequenceEqual(canonicalBytes))
        {
            throw new ArgumentException(
                "The observed object identity set bytes are not the canonical form of the set they parse into.",
                nameof(canonicalBytes));
        }

        return new VerifiedLuxembourgObservedObjectIdentitySet(setRef, set);
    }

    private static bool IsSha256Hex(string value)
    {
        if (value.Length != 64)
        {
            return false;
        }

        foreach (var character in value)
        {
            if (character is not (>= '0' and <= '9' or >= 'a' and <= 'f'))
            {
                return false;
            }
        }

        return true;
    }

    private static LuxembourgObservedObjectIdentitySet Parse(ReadOnlySpan<byte> canonicalBytes)
    {
        JsonDocument document;
        try
        {
            var reader = new Utf8JsonReader(
                canonicalBytes,
                new JsonReaderOptions
                {
                    AllowTrailingCommas = false,
                    CommentHandling = JsonCommentHandling.Disallow,
                });
            document = JsonDocument.ParseValue(ref reader);
        }
        catch (JsonException exception)
        {
            throw new ArgumentException(
                "The observed object identity set bytes are not one JSON document: " + exception.Message,
                nameof(canonicalBytes));
        }

        using (document)
        {
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                throw new ArgumentException(
                    "The observed object identity set bytes are not a JSON object.",
                    nameof(canonicalBytes));
            }

            if (!root.TryGetProperty("schema", out var schema) ||
                schema.ValueKind != JsonValueKind.String ||
                !string.Equals(
                    schema.GetString(), LuxembourgObservedObjectIdentitySet.SchemaId, StringComparison.Ordinal))
            {
                throw new ArgumentException(
                    "The observed object identity set bytes do not declare " +
                    LuxembourgObservedObjectIdentitySet.SchemaId + ".",
                    nameof(canonicalBytes));
            }

            if (!root.TryGetProperty("run_identity", out var runIdentity) ||
                runIdentity.ValueKind != JsonValueKind.Object ||
                !runIdentity.TryGetProperty("resource_id", out var resourceId) ||
                !runIdentity.TryGetProperty("sha256", out var sha256) ||
                resourceId.ValueKind != JsonValueKind.String ||
                sha256.ValueKind != JsonValueKind.String)
            {
                throw new ArgumentException(
                    "The observed object identity set bytes do not carry a run identity reference.",
                    nameof(canonicalBytes));
            }

            SourceArtifactRef parsedRunIdentity;
            try
            {
                parsedRunIdentity = new SourceArtifactRef(resourceId.GetString()!, sha256.GetString()!);
            }
            catch (ArgumentException exception)
            {
                throw new ArgumentException(
                    "The observed object identity set bytes carry an invalid run identity reference: " +
                    exception.Message,
                    nameof(canonicalBytes));
            }

            if (!root.TryGetProperty("object_ref_sha256", out var values) ||
                values.ValueKind != JsonValueKind.Array)
            {
                throw new ArgumentException(
                    "The observed object identity set bytes do not carry an object_ref_sha256 array.",
                    nameof(canonicalBytes));
            }

            var parsed = new List<string>(values.GetArrayLength());
            foreach (var value in values.EnumerateArray())
            {
                if (value.ValueKind != JsonValueKind.String)
                {
                    throw new ArgumentException(
                        "The observed object identity set carries a non-string object reference digest.",
                        nameof(canonicalBytes));
                }

                var digest = value.GetString()!;
                if (!IsSha256Hex(digest))
                {
                    throw new ArgumentException(
                        "The observed object identity set carries a value that is not a lowercase "
                        + "hex SHA-256.",
                        nameof(canonicalBytes));
                }

                parsed.Add(digest);
            }

            for (var index = 1; index < parsed.Count; index++)
            {
                if (string.CompareOrdinal(parsed[index - 1], parsed[index]) >= 0)
                {
                    throw new ArgumentException(
                        "The observed object identity set is not ordinal-ascending and distinct.",
                        nameof(canonicalBytes));
                }
            }

            return new LuxembourgObservedObjectIdentitySet(parsedRunIdentity, parsed);
        }
    }
}
