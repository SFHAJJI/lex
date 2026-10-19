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

/// <summary>
/// One publisher channel's freshness as the mounted law states it (the specification of record's R87; the plan's item 6.1):
/// the publisher whose machine channel was observed, when the newest acquisition of it that the mount holds completed (its
/// catalog retained, as the mount's build report states it; null when nothing beside the mount states it), and how many
/// observation times stand behind a measured lag range: none until observation times are carried into the records (the
/// plan's item 1.12) and the scheduled refresh has run, so no range is measured, and none is ever stated in its place.
/// </summary>
public sealed record V3FreshnessChannel
{
    public V3FreshnessChannel(PublisherId publisher, DateTimeOffset? newestCompletedObservation)
    {
        if (publisher is not (PublisherId.LuLegilux or PublisherId.EuEurLex))
        {
            throw new ArgumentOutOfRangeException(nameof(publisher));
        }

        if (newestCompletedObservation is { } completed && (completed == default || completed.UtcTicks % TimeSpan.TicksPerSecond != 0))
        {
            throw new ArgumentException("A completed observation is a real instant in whole seconds.", nameof(newestCompletedObservation));
        }

        Publisher = publisher;
        NewestCompletedObservation = newestCompletedObservation?.ToUniversalTime();
    }

    public PublisherId Publisher { get; }

    public DateTimeOffset? NewestCompletedObservation { get; }

    /// <summary>
    /// How many observation times stand behind the channel's lag range: none, since no index schema holds one (index 8 and 9
    /// require <c>observed_from</c> to be null, and the EU index has no such column), so no lag range is ever stated.
    /// </summary>
    public int LagObservations => 0;
}

public sealed record V3Freshness
{
    /// <summary>
    /// The declared maximum data age, in days (R87): past it a channel's data is stale, every page shows the staleness banner,
    /// and promotion refuses the release. The owner's answer to O11 of 2026-10-19 (option A, a limit of 30 days; the driver
    /// had held the specification's recommended 10 until then; STATUS-WEB.md).
    /// </summary>
    public const int MaximumDataAgeDays = 30;

    private static readonly TimeSpan MaximumDataAge = TimeSpan.FromDays(MaximumDataAgeDays);

    public V3Freshness(DateTimeOffset observedAt, string upstreamHealth)
        : this(observedAt, upstreamHealth, null, [])
    {
    }

    /// <param name="observedAt">The instant the server answered.</param>
    /// <param name="upstreamHealth">What the server knows of the publishers' health.</param>
    /// <param name="indexBuiltAt">The mounted index's build time (its log's newest build), in whole seconds, or null when it records none.</param>
    /// <param name="channels">Each publisher channel the mount holds an index for, Luxembourg first, each once.</param>
    public V3Freshness(
        DateTimeOffset observedAt,
        string upstreamHealth,
        DateTimeOffset? indexBuiltAt,
        IReadOnlyList<V3FreshnessChannel> channels)
    {
        if (observedAt == default)
        {
            throw new ArgumentException("An observation time is required.", nameof(observedAt));
        }

        if (upstreamHealth is not ("current" or "stale" or "unreachable"))
        {
            throw new ArgumentException("Unknown upstream health.", nameof(upstreamHealth));
        }

        if (indexBuiltAt is { } built && (built == default || built.UtcTicks % TimeSpan.TicksPerSecond != 0))
        {
            throw new ArgumentException("A build time is a real instant in whole seconds.", nameof(indexBuiltAt));
        }

        ArgumentNullException.ThrowIfNull(channels);
        if (channels.Any(static channel => channel is null) ||
            !channels.Select(static channel => channel.Publisher).SequenceEqual(
                channels.Select(static channel => channel.Publisher).Distinct().Order()))
        {
            throw new ArgumentException("Each publisher channel is named once, Luxembourg first.", nameof(channels));
        }

        ObservedAt = observedAt.ToUniversalTime();
        UpstreamHealth = upstreamHealth;
        IndexBuiltAt = indexBuiltAt?.ToUniversalTime();
        Channels = Array.AsReadOnly(channels.ToArray());
    }

    public DateTimeOffset ObservedAt { get; }

    public string UpstreamHealth { get; }

    public DateTimeOffset? IndexBuiltAt { get; }

    public IReadOnlyList<V3FreshnessChannel> Channels { get; }

    /// <summary>
    /// The channel's data age at the answer, in whole days elapsed (a completion after the answer's instant is age 0); null
    /// when no completion is stated.
    /// </summary>
    public int? AgeDays(V3FreshnessChannel channel)
    {
        ArgumentNullException.ThrowIfNull(channel);
        return channel.NewestCompletedObservation is { } completed
            ? (int)Math.Max(0, Math.Floor((ObservedAt - completed).TotalDays))
            : null;
    }

    /// <summary>Whether the channel's data is past the maximum age at the answer; null when no completion is stated.</summary>
    public bool? IsStale(V3FreshnessChannel channel)
    {
        ArgumentNullException.ThrowIfNull(channel);
        return channel.NewestCompletedObservation is { } completed ? ObservedAt - completed > MaximumDataAge : null;
    }

    /// <summary>Whether any channel's data is past the maximum age at the answer: what puts the staleness banner on a page.</summary>
    public bool Stale => Channels.Any(channel => IsStale(channel) == true);
}

/// <summary>
/// The commits an answer names (the specification of record's R23; the plan's item 6.5), labelled "index built from" and
/// "service running": the source commit of the code that built the mounted index, as the mount's build report states it, and
/// the source commit the serving runtime was built from, as its build stamped it; each a full lowercase commit, or null when
/// nothing states it.
/// </summary>
public sealed record V3Commits
{
    public static V3Commits None { get; } = new(null, null);

    public V3Commits(string? indexBuilderSourceCommit, string? servingRuntimeSourceCommit)
    {
        IndexBuilderSourceCommit = RequireCommit(indexBuilderSourceCommit, nameof(indexBuilderSourceCommit));
        ServingRuntimeSourceCommit = RequireCommit(servingRuntimeSourceCommit, nameof(servingRuntimeSourceCommit));
    }

    public string? IndexBuilderSourceCommit { get; }

    public string? ServingRuntimeSourceCommit { get; }

    /// <summary>Whether <paramref name="value"/> is a full source commit: 40 lowercase hexadecimal digits.</summary>
    public static bool IsCommit(string? value) =>
        value is { Length: 40 } && value.All(static character => character is (>= '0' and <= '9') or (>= 'a' and <= 'f'));

    /// <summary>
    /// The source commit an assembly's build stamped: what follows the last <c>+</c> of its informational version (the SDK
    /// writes the commit it built from there), when that is a full commit; else null.
    /// </summary>
    public static string? StampedIn(System.Reflection.Assembly assembly)
    {
        ArgumentNullException.ThrowIfNull(assembly);
        var version = System.Reflection.CustomAttributeExtensions
            .GetCustomAttribute<System.Reflection.AssemblyInformationalVersionAttribute>(assembly)?.InformationalVersion;
        var plus = version?.LastIndexOf('+') ?? -1;
        var commit = plus < 0 ? null : version![(plus + 1)..];
        return IsCommit(commit) ? commit : null;
    }

    private static string? RequireCommit(string? value, string parameterName) =>
        value is null || IsCommit(value) ? value : throw new ArgumentException("A source commit is 40 lowercase hexadecimal digits.", parameterName);
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
        V3Freshness freshness,
        V3Commits? commits = null)
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
        Commits = commits ?? V3Commits.None;
    }

    public PublisherId Publisher { get; }

    public string Status { get; }

    public TimelineSemantics TimelineSemantics { get; }

    public V3SnapshotReference Snapshot { get; }

    public string Jurisdiction { get; }

    public bool Provisional { get; }

    public V3Freshness Freshness { get; }

    public V3Commits Commits { get; }

    /// <summary>
    /// This context as the server answering it states it: the commit its serving runtime was built from replaces whatever the
    /// context named (the mount that built the context knows the index's commit, never the server's).
    /// </summary>
    public V3EnvelopeContext WithServingRuntimeSourceCommit(string? commit) =>
        string.Equals(Commits.ServingRuntimeSourceCommit, commit, StringComparison.Ordinal)
            ? this
            : new V3EnvelopeContext(
                Publisher, Status, TimelineSemantics, Snapshot, Jurisdiction, Provisional, Freshness,
                new V3Commits(Commits.IndexBuilderSourceCommit, commit));
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

    /// <summary>The closed verdict set, in declaration order, for a census to publish rather than restate.</summary>
    public static IReadOnlyList<string> All { get; } = Array.AsReadOnly(new[] { Answer, AnswerWithEnrichment, Point, Clarify, Refuse, Split });

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
