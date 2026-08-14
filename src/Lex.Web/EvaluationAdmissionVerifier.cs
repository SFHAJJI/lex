using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using Lex.Evaluation;

namespace Lex.Web;

public sealed class EvaluationAdmissionVerifier(
    EvaluationAdmissionAuthority authority,
    EvaluationAdmissionIdentity identity,
    TimeProvider clock)
{
    public EvaluationAdmissionCapability Verify(
        ReadOnlySpan<byte> capability,
        string signature) => EvaluationAdmissionContract.Verify(
            capability, signature, authority, identity, clock.GetUtcNow());
}

internal static class EvaluationAdmissionTrustStore
{
    internal const string ResourceName =
        "Lex.Web.trusted-evaluation-review-roots.json";

    internal static EvaluationAdmissionAuthority Load()
    {
        using var stream = typeof(EvaluationAdmissionTrustStore).Assembly
            .GetManifestResourceStream(ResourceName)
            ?? throw new InvalidDataException(
                "The release-pinned evaluation admission trust root is absent.");
        if (stream.Length is <= 0 or > 64 * 1024)
            throw new InvalidDataException(
                "The release-pinned evaluation admission trust root is outside its byte limit.");
        using var buffer = new MemoryStream((int)stream.Length);
        stream.CopyTo(buffer);
        TrustDocument document;
        try
        {
            document = JsonSerializer.Deserialize<TrustDocument>(
                buffer.ToArray(), new JsonSerializerOptions(JsonSerializerDefaults.Web)
                {
                    PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
                    UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
                }) ?? throw new InvalidDataException(
                "The release-pinned evaluation admission trust root is empty.");
        }
        catch (JsonException exception)
        {
            throw new InvalidDataException(
                "The release-pinned evaluation admission trust root is malformed.", exception);
        }
        if (document.Schema != "lex-assistant-eval-review-roots/1"
            || document.Authorities is not { Count: 1 }
            || !document.Authorities[0].ReviewerId.StartsWith(
                "entra:", StringComparison.Ordinal)
            || document.Authorities[0].ReviewerId.Length > 100)
            throw new InvalidDataException(
                "The release-pinned evaluation admission authority is invalid.");
        return document.Authorities[0];
    }

    private sealed record TrustDocument(
        string Schema,
        IReadOnlyList<EvaluationAdmissionAuthority> Authorities);
}
