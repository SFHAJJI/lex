using System.Text.Json;

namespace Lex.V3.Contracts.Platform;

public static class V3Verdicts
{
    public const string Answer = "answer";
    public const string AnswerWithEnrichment = "answer_with_enrichment";
    public const string Point = "point";
    public const string Clarify = "clarify";
    public const string Refuse = "refuse";
    public const string Split = "split";

    private static readonly HashSet<string> Known = new(
        [Answer, AnswerWithEnrichment, Point, Clarify, Refuse, Split],
        StringComparer.Ordinal);

    public static bool IsKnown(string verdict) => Known.Contains(verdict);
}

public sealed record V3EnvelopePayload(string Schema, string ObjectType, JsonElement Value);

public sealed record V3EnvelopeRefusal(string Schema, string Code, JsonElement HelpfulPayload);

public sealed class V3Envelope
{
    internal V3Envelope(
        string schema,
        string version,
        string objectType,
        string requestRef,
        string operationId,
        string registrySchema,
        string registrySha256,
        string verdict,
        V3EnvelopePayload? result,
        V3EnvelopeRefusal? refusal)
    {
        Schema = schema;
        Version = version;
        ObjectType = objectType;
        RequestRef = requestRef;
        OperationId = operationId;
        RegistrySchema = registrySchema;
        RegistrySha256 = registrySha256;
        Verdict = verdict;
        Result = result;
        Refusal = refusal;
    }

    public string Schema { get; }

    public string Version { get; }

    public string ObjectType { get; }

    public string RequestRef { get; }

    public string OperationId { get; }

    public string RegistrySchema { get; }

    public string RegistrySha256 { get; }

    public string Verdict { get; }

    public V3EnvelopePayload? Result { get; }

    public V3EnvelopeRefusal? Refusal { get; }
}

public sealed class V3EnvelopeBuilder
{
    public const string Schema = "lex-v3-envelope/1";

    private readonly V3OperationRegistry _registry;

    public V3EnvelopeBuilder(V3OperationRegistry registry)
    {
        _registry = registry ?? throw new ArgumentNullException(nameof(registry));
    }

    public V3Envelope Success(
        string requestRef,
        string operationId,
        string verdict,
        string resultSchema,
        string resultObjectType,
        JsonElement result)
    {
        var operation = _registry.Operation(operationId);
        RequireRequestRef(requestRef);
        if (!V3Verdicts.IsKnown(verdict) || string.Equals(verdict, V3Verdicts.Refuse, StringComparison.Ordinal))
        {
            throw new ArgumentException("Unknown or refusal-only success verdict.", nameof(verdict));
        }

        if (!string.Equals(resultSchema, operation.ResultSchema, StringComparison.Ordinal))
        {
            throw new ArgumentException("The result schema is not bound to this operation.", nameof(resultSchema));
        }

        if (!operation.ResultObjectTypes.Contains(resultObjectType, StringComparer.Ordinal))
        {
            throw new ArgumentException("The result object type is not bound to this operation.", nameof(resultObjectType));
        }

        RequireObject(result, nameof(result));
        return Create(
            requestRef,
            operationId,
            verdict,
            new V3EnvelopePayload(resultSchema, resultObjectType, result.Clone()),
            null);
    }

    public V3Envelope Refusal(
        string requestRef,
        string operationId,
        string refusalSchema,
        string refusalCode,
        JsonElement helpfulPayload)
    {
        var operation = _registry.Operation(operationId);
        RequireRequestRef(requestRef);
        if (!string.Equals(refusalSchema, operation.RefusalSchema, StringComparison.Ordinal))
        {
            throw new ArgumentException("The refusal schema is not bound to this operation.", nameof(refusalSchema));
        }

        if (!_registry.DeclaresRefusal(refusalCode))
        {
            throw new ArgumentException("The refusal code is not declared by the registry.", nameof(refusalCode));
        }

        RequireObject(helpfulPayload, nameof(helpfulPayload));
        if (!helpfulPayload.EnumerateObject().Any())
        {
            throw new ArgumentException("A refusal must carry a helpful payload.", nameof(helpfulPayload));
        }

        return Create(
            requestRef,
            operationId,
            V3Verdicts.Refuse,
            null,
            new V3EnvelopeRefusal(refusalSchema, refusalCode, helpfulPayload.Clone()));
    }

    public void VerifyBinding(V3Envelope envelope)
    {
        ArgumentNullException.ThrowIfNull(envelope);
        if (!string.Equals(envelope.Schema, Schema, StringComparison.Ordinal) ||
            !string.Equals(envelope.Version, V3OperationRegistry.Version, StringComparison.Ordinal) ||
            !string.Equals(envelope.ObjectType, "envelope", StringComparison.Ordinal) ||
            !string.Equals(envelope.RegistrySchema, V3OperationRegistry.Schema, StringComparison.Ordinal) ||
            !string.Equals(envelope.RegistrySha256, _registry.Sha256, StringComparison.Ordinal))
        {
            throw new ArgumentException("The envelope is not bound to this production registry.", nameof(envelope));
        }

        var operation = _registry.Operation(envelope.OperationId);
        if (!V3Verdicts.IsKnown(envelope.Verdict))
        {
            throw new ArgumentException("Unknown envelope verdict.", nameof(envelope));
        }

        if (string.Equals(envelope.Verdict, V3Verdicts.Refuse, StringComparison.Ordinal))
        {
            if (envelope.Result is not null || envelope.Refusal is null ||
                !string.Equals(envelope.Refusal.Schema, operation.RefusalSchema, StringComparison.Ordinal) ||
                !_registry.DeclaresRefusal(envelope.Refusal.Code))
            {
                throw new ArgumentException("Invalid registry-bound refusal envelope.", nameof(envelope));
            }

            return;
        }

        if (envelope.Refusal is not null || envelope.Result is null ||
            !string.Equals(envelope.Result.Schema, operation.ResultSchema, StringComparison.Ordinal) ||
            !operation.ResultObjectTypes.Contains(envelope.Result.ObjectType, StringComparer.Ordinal))
        {
            throw new ArgumentException("Invalid registry-bound result envelope.", nameof(envelope));
        }
    }

    private V3Envelope Create(
        string requestRef,
        string operationId,
        string verdict,
        V3EnvelopePayload? result,
        V3EnvelopeRefusal? refusal) =>
        new(
            Schema,
            V3OperationRegistry.Version,
            "envelope",
            requestRef,
            operationId,
            V3OperationRegistry.Schema,
            _registry.Sha256,
            verdict,
            result,
            refusal);

    private static void RequireRequestRef(string requestRef)
    {
        if (string.IsNullOrWhiteSpace(requestRef) ||
            requestRef.Length > 128 ||
            requestRef.Any(character => !(char.IsAsciiLetterOrDigit(character) || character is '_' or '-')) ||
            !string.Equals(requestRef, requestRef.ToLowerInvariant(), StringComparison.Ordinal))
        {
            throw new ArgumentException("An opaque bounded request reference is required.", nameof(requestRef));
        }
    }

    private static void RequireObject(JsonElement value, string parameterName)
    {
        if (value.ValueKind != JsonValueKind.Object)
        {
            throw new ArgumentException("A JSON object payload is required.", parameterName);
        }
    }
}
