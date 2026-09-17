using System.Text.Json;

namespace Lex.V3.Contracts.Platform;

public sealed record V3SnapshotReference
{
    public V3SnapshotReference(string snapshotId, string snapshotSha256)
    {
        SnapshotId = ContractValidation.RequireIdentifier(snapshotId, nameof(snapshotId));
        SnapshotSha256 = ContractValidation.RequireSha256(snapshotSha256, nameof(snapshotSha256));
    }

    public string SnapshotId { get; }

    public string SnapshotSha256 { get; }
}

public sealed record V3Freshness
{
    public V3Freshness(DateTimeOffset observedAt, string upstreamHealth)
    {
        if (observedAt == default)
        {
            throw new ArgumentException("An observation time is required.", nameof(observedAt));
        }

        if (upstreamHealth is not ("current" or "stale" or "unreachable"))
        {
            throw new ArgumentException("Unknown upstream health.", nameof(upstreamHealth));
        }

        ObservedAt = observedAt.ToUniversalTime();
        UpstreamHealth = upstreamHealth;
    }

    public DateTimeOffset ObservedAt { get; }

    public string UpstreamHealth { get; }
}

public sealed record V3EnvelopeContext
{
    public V3EnvelopeContext(
        PublisherId publisher,
        string status,
        TimelineSemantics timelineSemantics,
        V3SnapshotReference snapshot,
        string jurisdiction,
        bool provisional,
        V3Freshness freshness)
    {
        var expected = publisher switch
        {
            PublisherId.LuLegilux => (TimelineSemantics.PublisherApplicability, "lu"),
            PublisherId.EuEurLex => (TimelineSemantics.OfficialConsolidationState, "eu"),
            _ => throw new ArgumentOutOfRangeException(nameof(publisher)),
        };
        if (timelineSemantics != expected.Item1)
        {
            throw new ArgumentException("Timeline semantics must match the publisher.", nameof(timelineSemantics));
        }

        if (!string.Equals(jurisdiction, expected.Item2, StringComparison.Ordinal))
        {
            throw new ArgumentException("Jurisdiction must match the publisher.", nameof(jurisdiction));
        }

        if (status is not ("success" or "refusal"))
        {
            throw new ArgumentException("Unknown envelope status.", nameof(status));
        }

        Publisher = publisher;
        Status = status;
        TimelineSemantics = timelineSemantics;
        Snapshot = snapshot ?? throw new ArgumentNullException(nameof(snapshot));
        Jurisdiction = jurisdiction;
        Provisional = provisional;
        Freshness = freshness ?? throw new ArgumentNullException(nameof(freshness));
    }

    public PublisherId Publisher { get; }

    public string Status { get; }

    public TimelineSemantics TimelineSemantics { get; }

    public V3SnapshotReference Snapshot { get; }

    public string Jurisdiction { get; }

    public bool Provisional { get; }

    public V3Freshness Freshness { get; }
}

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
        V3EnvelopeContext context,
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
        Context = context;
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

    public V3EnvelopeContext Context { get; }

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
        V3EnvelopeContext context,
        string verdict,
        string resultSchema,
        string resultObjectType,
        JsonElement result)
    {
        var operation = _registry.Operation(operationId);
        RequireRequestRef(requestRef);
        RequireContext(context, "success");
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
            context,
            verdict,
            new V3EnvelopePayload(resultSchema, resultObjectType, result.Clone()),
            null);
    }

    public V3Envelope Refusal(
        string requestRef,
        string operationId,
        V3EnvelopeContext context,
        string refusalSchema,
        string refusalCode,
        JsonElement helpfulPayload)
    {
        var operation = _registry.Operation(operationId);
        RequireRequestRef(requestRef);
        RequireContext(context, "refusal");
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

        var missing = _registry.MandatoryPayloadFields(refusalCode)
            .Where(field => !helpfulPayload.TryGetProperty(field, out _))
            .ToArray();
        if (missing.Length > 0)
        {
            throw new ArgumentException(
                $"The refusal payload is missing mandatory fields: {string.Join(", ", missing)}.",
                nameof(helpfulPayload));
        }

        if (string.Equals(refusalCode, "anchor_not_in_version", StringComparison.Ordinal) &&
            (!helpfulPayload.TryGetProperty("nearest_anchors", out var anchors) ||
             anchors.ValueKind != JsonValueKind.Array ||
             anchors.GetArrayLength() == 0 ||
             !helpfulPayload.TryGetProperty("do_not_fall_back_to_full_text_search", out var noFallback) ||
             noFallback.ValueKind is not JsonValueKind.True))
        {
            throw new ArgumentException(
                "anchor_not_in_version requires nearest anchors and the do-not-fall-back rule.",
                nameof(helpfulPayload));
        }

        return Create(
            requestRef,
            operationId,
            context,
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

        RequireContext(envelope.Context, envelope.Refusal is null ? "success" : "refusal");

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
        V3EnvelopeContext context,
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
            context,
            verdict,
            result,
            refusal);

    private static void RequireContext(V3EnvelopeContext context, string expectedStatus)
    {
        ArgumentNullException.ThrowIfNull(context);
        if (!string.Equals(context.Status, expectedStatus, StringComparison.Ordinal))
        {
            throw new ArgumentException("Envelope status does not match its branch.", nameof(context));
        }
    }

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
