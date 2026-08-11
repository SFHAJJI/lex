using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using Lex.Index;

namespace Lex.Ingest;

internal static class EvaluationReviewTrustStore
{
    internal const string ResourceName =
        "Lex.Ingest.trusted-evaluation-review-roots.json";

    internal static EvaluationReviewAuthority Load()
    {
        using var stream = Assembly.GetExecutingAssembly()
            .GetManifestResourceStream(ResourceName)
            ?? throw new InvalidDataException(
                "The release-pinned evaluation review trust roots are absent.");
        if (stream.Length is <= 0 or > 64 * 1024)
            throw new InvalidDataException(
                "The release-pinned evaluation review trust roots are outside their byte limit.");
        using var buffer = new MemoryStream((int)stream.Length);
        stream.CopyTo(buffer);
        EvaluationReviewTrustDocument document;
        try
        {
            document = JsonSerializer.Deserialize<EvaluationReviewTrustDocument>(
                buffer.ToArray(), new JsonSerializerOptions(JsonSerializerDefaults.Web)
                {
                    PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
                    UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
                }) ?? throw new InvalidDataException(
                    "The release-pinned evaluation review trust document is empty.");
        }
        catch (JsonException exception)
        {
            throw new InvalidDataException(
                "The release-pinned evaluation review trust document is malformed.", exception);
        }
        if (document.Schema != "lex-assistant-eval-review-roots/1"
            || document.Authorities is not { Count: 1 }
            || !document.Authorities[0].ReviewerId.StartsWith("entra:", StringComparison.Ordinal)
            || document.Authorities[0].ReviewerId.Length > 100)
            throw new InvalidDataException(
                "The release-pinned evaluation review authority is invalid.");
        return document.Authorities[0];
    }
}

internal sealed record EvaluationReviewTrustDocument(
    string Schema,
    IReadOnlyList<EvaluationReviewAuthority> Authorities);

internal sealed record EvaluationReviewAuthority(
    string ReviewerId,
    string KeyId,
    string FingerprintSha256,
    string PublicKeyPem)
{
    internal ArtifactTrustRoot Root => new(KeyId, FingerprintSha256, PublicKeyPem);
}
